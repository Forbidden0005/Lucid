using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucid.Helpers;
using Lucid.Services.Behavior;
using Lucid.Services.Session;

namespace Lucid.Services.Distributed;

/// <summary>
/// Manages local-network device discovery (UDP broadcast beacons) and
/// encrypted operational snapshot exchange (TCP).
///
/// Discovery: 30-second UDP broadcast on port 42998. Beacons announce device ID,
///            name, and TCP port. Trusted devices' addresses are tracked from beacons.
///
/// Data exchange: Length-prefixed, AES-256-CBC encrypted JSON over TCP port 43001.
///                Envelope format: [senderDeviceId 36B] [IV 16B] [ciphertext NB].
///
/// Pairing: 6-digit code valid for 5 minutes. Both sides derive the same AES-256 key
///          via SHA-256(code + sorted device IDs). The code is never persisted.
///
/// All I/O is best-effort — networking failures are swallowed so the app remains
/// fully functional as a standalone tool when no peers are available.
/// </summary>
public sealed class LocalSyncCoordinator : IDisposable
{
    private const string BroadcastAddress = "255.255.255.255";
    private const int    DiscoveryPort    = 42998;
    private const int    DataPort         = 43001;
    private const int    BeaconIntervalMs = 30_000;
    private const int    SyncIntervalMs   = 60_000;
    private const int    SocketTimeoutMs  = 5_000;

    // Maximum envelope size guard (64 KB) to prevent memory exhaustion
    private const int MaxPayloadBytes = 65_536;

    private readonly DeviceIdentityService    _identity;
    private readonly TrustedDeviceRegistry    _registry;
    private readonly WorkloadProfilingService  _workload;
    private readonly ISessionContextService    _session;
    private readonly ITelemetryService         _telemetry;

    private readonly ConcurrentDictionary<string, SharedOperationalSnapshot> _remoteSnapshots   = new();
    private readonly ConcurrentDictionary<string, string>                    _discoveredAddresses = new();

    private TelemetrySnapshot?       _latestTelemetry;
    private CancellationTokenSource? _cts;

    // ── Pairing state ─────────────────────────────────────────────────────────
    private string?        _pendingCode;
    private DateTimeOffset _pendingExpiry;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised whenever a fresh snapshot is received from a trusted device.</summary>
    public event EventHandler<SharedOperationalSnapshot>? SnapshotReceived;

    // ── Construction ──────────────────────────────────────────────────────────

    public LocalSyncCoordinator(
        DeviceIdentityService   identity,
        TrustedDeviceRegistry   registry,
        WorkloadProfilingService workload,
        ISessionContextService   session,
        ITelemetryService        telemetry)
    {
        _identity  = identity;
        _registry  = registry;
        _workload  = workload;
        _session   = session;
        _telemetry = telemetry;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>True while discovery/sync loops are active (user has opted in).</summary>
    public bool IsRunning => _cts is not null;

    public void Start()
    {
        if (IsRunning) return;

        _telemetry.ReadingAvailable += OnTelemetryReading;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => BeaconSendLoopAsync(_cts.Token));
        _ = Task.Run(() => BeaconReceiveLoopAsync(_cts.Token));
        _ = Task.Run(() => DataListenerLoopAsync(_cts.Token));
        _ = Task.Run(() => SyncLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _telemetry.ReadingAvailable -= OnTelemetryReading;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();

    private void OnTelemetryReading(object? _, TelemetrySnapshot snap) =>
        _latestTelemetry = snap;

    // ── Pairing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a 6-digit pairing code valid for 5 minutes.
    /// Display this code on screen. The other device enters it to confirm pairing.
    /// </summary>
    public string BeginPairing()
    {
        _pendingCode   = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
        _pendingExpiry = DateTimeOffset.UtcNow.AddMinutes(5);
        return _pendingCode;
    }

    public void CancelPairing() => _pendingCode = null;

    /// <summary>
    /// Completes pairing with a remote device using the matching pairing code.
    /// Derives the shared AES key and persists the pairing record.
    ///
    /// Returns false if no pairing session is active, the code does not match,
    /// or the 5-minute window has expired.  Clears the pending code on every
    /// exit path so a failed attempt cannot be retried without a new BeginPairing.
    /// </summary>
    public bool ConfirmPairing(
        string remoteDeviceId,
        string remoteName,
        string remoteRole,
        string remoteIp,
        string pairingCode)
    {
        // Validate before touching any crypto or registry state.
        if (_pendingCode is null
            || pairingCode != _pendingCode
            || DateTimeOffset.UtcNow > _pendingExpiry)
        {
            _pendingCode   = null;          // invalidate expired/used session
            _pendingExpiry = default;
            return false;
        }

        // Consume the code immediately — one-time use even on exception.
        _pendingCode   = null;
        _pendingExpiry = default;

        try
        {
            var myId   = _identity.GetOrCreateIdentity().DeviceId;
            var aesKey = TrustedDeviceRegistry.DeriveSharedKey(pairingCode, myId, remoteDeviceId);

            _registry.AddOrUpdatePairing(new StoredDevicePairing(
                DeviceId:         remoteDeviceId,
                DeviceName:       remoteName,
                InferredRole:     remoteRole,
                AesKeyBase64:     Convert.ToBase64String(aesKey),
                LastKnownAddress: remoteIp,
                PairedAt:         DateTimeOffset.UtcNow,
                LastSync:         DateTimeOffset.MinValue));

            _discoveredAddresses[remoteDeviceId] = remoteIp;
            return true;
        }
        catch { return false; }
    }

    // ── Snapshot access ───────────────────────────────────────────────────────

    /// <summary>Read-only view of the most recent snapshot from each remote device.</summary>
    public IReadOnlyDictionary<string, SharedOperationalSnapshot> GetRemoteSnapshots() =>
        _remoteSnapshots;

    // ── UDP beacon sender ─────────────────────────────────────────────────────

    private async Task BeaconSendLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { SendBeacon(); }
            catch { /* best-effort */ }

            try { await Task.Delay(BeaconIntervalMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void SendBeacon()
    {
        var id      = _identity.GetOrCreateIdentity();
        var payload = JsonSerializer.Serialize(new
        {
            id   = id.DeviceId,
            name = id.DeviceName,
            role = id.InferredRole.ToString(),
            port = DataPort,
            v    = 1,
        });
        var data = Encoding.UTF8.GetBytes(payload);

        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        udp.Send(data, data.Length, BroadcastAddress, DiscoveryPort);
    }

    // ── UDP beacon receiver ───────────────────────────────────────────────────

    private async Task BeaconReceiveLoopAsync(CancellationToken ct)
    {
        UdpClient? udp = null;
        try
        {
            udp = new UdpClient();
            udp.Client.SetSocketOption(
                SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                    ProcessBeacon(result.Buffer, result.RemoteEndPoint.Address.ToString());
                }
                catch (OperationCanceledException) { break; }
                catch { /* ignore malformed/unrelated beacons */ }
            }
        }
        catch
        {
            // Port may be in use by another Lucid instance or blocked by firewall.
            // Discovery degrades: sync still works when LastKnownAddress is populated.
        }
        finally { udp?.Dispose(); }
    }

    private void ProcessBeacon(byte[] data, string senderIp)
    {
        try
        {
            using var doc      = JsonDocument.Parse(Encoding.UTF8.GetString(data));
            var       remoteId = doc.RootElement.GetProperty("id").GetString();
            if (string.IsNullOrEmpty(remoteId)) return;

            var myId = _identity.GetOrCreateIdentity().DeviceId;
            if (remoteId == myId) return; // ignore own broadcast

            // Track address for trusted devices to enable TCP sync
            if (_registry.IsTrusted(remoteId))
                _discoveredAddresses[remoteId] = senderIp;
        }
        catch { /* ignore */ }
    }

    // ── TCP data listener ─────────────────────────────────────────────────────

    private async Task DataListenerLoopAsync(CancellationToken ct)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, DataPort);
            listener.Start();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    _ = Task.Run(() => HandleInboundAsync(client, ct), ct);
                }
                catch (OperationCanceledException) { break; }
                catch { /* keep accepting */ }
            }
        }
        catch
        {
            // TCP port unavailable — inbound sync disabled for this session.
            // Outbound sync and discovery still work normally.
        }
        finally { listener?.Stop(); }
    }

    private async Task HandleInboundAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = SocketTimeoutMs;
                client.SendTimeout    = SocketTimeoutMs;

                var remoteIp = ((IPEndPoint?)client.Client.RemoteEndPoint)
                                   ?.Address.ToString() ?? string.Empty;

                using var stream = client.GetStream();

                // Read length-prefixed envelope
                var lenBuf = new byte[4];
                await stream.ReadExactlyAsync(lenBuf.AsMemory(0, 4), ct).ConfigureAwait(false);
                var len = BitConverter.ToInt32(lenBuf, 0);
                if (len is <= 0 or > MaxPayloadBytes) return;

                var msgBuf = new byte[len];
                await stream.ReadExactlyAsync(msgBuf.AsMemory(0, len), ct).ConfigureAwait(false);

                if (!TryDecrypt(msgBuf, remoteIp, out var plaintext, out var senderId) ||
                    plaintext is null || senderId is null) return;

                var snap = JsonSerializer.Deserialize<SharedOperationalSnapshot>(plaintext);
                if (snap is null) return;

                _remoteSnapshots[senderId] = snap;
                _registry.UpdateSyncTime(senderId, remoteIp);
                SnapshotReceived?.Invoke(this, snap);

                // Respond with our own snapshot
                var response = EncryptPayload(
                    JsonSerializer.Serialize(BuildLocalSnapshot()), senderId);
                if (response is null) return;

                var rspLen = BitConverter.GetBytes(response.Length);
                await stream.WriteAsync(rspLen.AsMemory(), ct).ConfigureAwait(false);
                await stream.WriteAsync(response.AsMemory(), ct).ConfigureAwait(false);
            }
        }
        catch { /* best-effort — connection may have dropped */ }
    }

    // ── Outbound sync loop ────────────────────────────────────────────────────

    private async Task SyncLoopAsync(CancellationToken ct)
    {
        // Give the listener time to bind before starting outbound syncs
        try { await Task.Delay(8_000, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await SyncAllAsync(ct).ConfigureAwait(false); }
            catch { /* best-effort */ }

            try { await Task.Delay(SyncIntervalMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SyncAllAsync(CancellationToken ct)
    {
        foreach (var pairing in _registry.GetAll())
        {
            if (ct.IsCancellationRequested) break;

            // Prefer live-discovered address; fall back to last known
            if (!_discoveredAddresses.TryGetValue(pairing.DeviceId, out var ip))
                ip = pairing.LastKnownAddress;
            if (string.IsNullOrEmpty(ip)) continue;

            try { await SyncOneAsync(pairing, ip, ct).ConfigureAwait(false); }
            catch { /* device is offline — try again next cycle */ }
        }
    }

    private async Task SyncOneAsync(
        StoredDevicePairing pairing, string ip, CancellationToken ct)
    {
        using var client = new TcpClient
        {
            ReceiveTimeout = SocketTimeoutMs,
            SendTimeout    = SocketTimeoutMs,
        };
        await client.ConnectAsync(ip, DataPort, ct).ConfigureAwait(false);
        using var stream = client.GetStream();

        // Send our snapshot
        var payload = EncryptPayload(
            JsonSerializer.Serialize(BuildLocalSnapshot()), pairing.DeviceId);
        if (payload is null) return;

        var reqLen = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(reqLen.AsMemory(), ct).ConfigureAwait(false);
        await stream.WriteAsync(payload.AsMemory(), ct).ConfigureAwait(false);

        // Receive remote snapshot
        var rspLenBuf = new byte[4];
        await stream.ReadExactlyAsync(rspLenBuf.AsMemory(0, 4), ct).ConfigureAwait(false);
        var rspLen = BitConverter.ToInt32(rspLenBuf, 0);
        if (rspLen is <= 0 or > MaxPayloadBytes) return;

        var rspBuf = new byte[rspLen];
        await stream.ReadExactlyAsync(rspBuf.AsMemory(0, rspLen), ct).ConfigureAwait(false);

        if (!TryDecrypt(rspBuf, ip, out var rspPlain, out _) || rspPlain is null) return;

        var snap = JsonSerializer.Deserialize<SharedOperationalSnapshot>(rspPlain);
        if (snap is null) return;

        _remoteSnapshots[pairing.DeviceId] = snap;
        _registry.UpdateSyncTime(pairing.DeviceId, ip);
        SnapshotReceived?.Invoke(this, snap);
    }

    // ── Encryption ────────────────────────────────────────────────────────────
    // Envelope: [senderDeviceId 36B ASCII] + [AES IV 16B] + [ciphertext NB]

    private byte[]? EncryptPayload(string plaintext, string targetDeviceId)
    {
        var pairing = _registry.Get(targetDeviceId);
        if (pairing is null) return null;

        try
        {
            var key  = Convert.FromBase64String(pairing.AesKeyBase64);
            var data = Encoding.UTF8.GetBytes(plaintext);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();

            using var enc    = aes.CreateEncryptor();
            var       cipher = enc.TransformFinalBlock(data, 0, data.Length);

            // Pad/truncate device ID to exactly 36 ASCII bytes
            var myIdBytes = Encoding.ASCII.GetBytes(
                _identity.GetOrCreateIdentity().DeviceId.PadRight(36)[..36]);

            var result = new byte[36 + 16 + cipher.Length];
            Buffer.BlockCopy(myIdBytes, 0, result,  0, 36);
            Buffer.BlockCopy(aes.IV,   0, result, 36, 16);
            Buffer.BlockCopy(cipher,   0, result, 52, cipher.Length);
            return result;
        }
        catch { return null; }
    }

    private bool TryDecrypt(byte[] data, string senderIp,
        out string? plaintext, out string? senderId)
    {
        plaintext = null;
        senderId  = null;
        if (data.Length < 53) return false;

        try
        {
            senderId = Encoding.ASCII.GetString(data, 0, 36).Trim();

            var pairing = _registry.Get(senderId);
            if (pairing is null) return false;

            var key    = Convert.FromBase64String(pairing.AesKeyBase64);
            var iv     = new byte[16];
            var cipher = new byte[data.Length - 52];
            Buffer.BlockCopy(data, 36, iv,     0, 16);
            Buffer.BlockCopy(data, 52, cipher, 0, cipher.Length);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV  = iv;
            using var dec = aes.CreateDecryptor();
            var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
            plaintext = Encoding.UTF8.GetString(plain);

            // Update address from the decrypted sender so future outbound syncs
            // use the correct IP even without a beacon from this device
            if (!string.IsNullOrEmpty(senderIp))
                _discoveredAddresses[senderId] = senderIp;

            return true;
        }
        catch { return false; }
    }

    // ── Local snapshot builder ────────────────────────────────────────────────

    private SharedOperationalSnapshot BuildLocalSnapshot()
    {
        var id  = _identity.GetOrCreateIdentity();
        var wl  = _workload.CurrentClassification;
        var ctx = _session.Current;
        var tel = _latestTelemetry;

        return new SharedOperationalSnapshot(
            SourceDeviceId:        id.DeviceId,
            DeviceName:            id.DeviceName,
            Role:                  id.InferredRole,
            SnapshotAt:            DateTimeOffset.UtcNow,
            CurrentWorkload:       wl?.PrimaryWorkload ?? WorkloadType.Unknown,
            CpuPercent:            tel?.CpuPercent              ?? 0,
            RamPercent:            tel?.RamPercent              ?? 0,
            CpuTemperatureCelsius: tel?.CpuTemperatureCelsius   ?? 0,
            TemperatureAvailable:  tel?.CpuTemperatureAvailable ?? false,
            StorageUsedPercent:    tel?.DiskPercent             ?? 0,
            ActiveInsightCount:    0,   // enriched externally via IntelligenceEngine if needed
            SystemUptime:          ctx.SystemUptime,
            WorkloadRationale:     wl?.Rationale,
            TopActiveInsightIds:   []);
    }
}
