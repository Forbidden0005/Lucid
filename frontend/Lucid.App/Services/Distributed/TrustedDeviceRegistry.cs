using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lucid.Services.Distributed;

/// <summary>
/// Manages the list of trusted paired devices and their shared AES-256 encryption keys.
///
/// Persistence: %LOCALAPPDATA%\Lucid\trusted_devices.json
/// Thread-safety: all mutations are guarded by a lock on the internal list.
/// All I/O is best-effort — failures are silently swallowed.
///
/// The registry never stores raw pairing codes. Keys are derived from the code
/// once during pairing and the code is then discarded.
/// </summary>
public sealed class TrustedDeviceRegistry
{
    private static readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucid",
        "trusted_devices.json");

    private readonly object _lock = new();
    private List<StoredDevicePairing> _pairings = [];

    public TrustedDeviceRegistry() => Load();

    // ── Query ─────────────────────────────────────────────────────────────────

    public IReadOnlyList<StoredDevicePairing> GetAll()
    {
        lock (_lock) return [.. _pairings];
    }

    public bool IsTrusted(string deviceId)
    {
        lock (_lock) return _pairings.Any(p => p.DeviceId == deviceId);
    }

    public StoredDevicePairing? Get(string deviceId)
    {
        lock (_lock) return _pairings.FirstOrDefault(p => p.DeviceId == deviceId);
    }

    public int Count
    {
        get { lock (_lock) return _pairings.Count; }
    }

    // ── Mutation ──────────────────────────────────────────────────────────────

    public void AddOrUpdatePairing(StoredDevicePairing pairing)
    {
        lock (_lock)
        {
            _pairings.RemoveAll(p => p.DeviceId == pairing.DeviceId);
            _pairings.Add(pairing);
        }
        Save();
    }

    public void UpdateSyncTime(string deviceId, string lastAddress)
    {
        lock (_lock)
        {
            var idx = _pairings.FindIndex(p => p.DeviceId == deviceId);
            if (idx < 0) return;
            _pairings[idx] = _pairings[idx] with
            {
                LastSync         = DateTimeOffset.UtcNow,
                LastKnownAddress = lastAddress,
            };
        }
        Save();
    }

    public bool Remove(string deviceId)
    {
        bool removed;
        lock (_lock) removed = _pairings.RemoveAll(p => p.DeviceId == deviceId) > 0;
        if (removed) Save();
        return removed;
    }

    // ── Key derivation ────────────────────────────────────────────────────────

    /// <summary>
    /// Derives a symmetric AES-256 key from the pairing code and both device IDs.
    /// Order-independent: device IDs are sorted so both sides derive the same key.
    /// The pairing code is not stored — only the derived key is persisted.
    /// </summary>
    public static byte[] DeriveSharedKey(string pairingCode, string deviceIdA, string deviceIdB)
    {
        // Sort for order-independence
        string first, second;
        if (string.Compare(deviceIdA, deviceIdB, StringComparison.Ordinal) <= 0)
            (first, second) = (deviceIdA, deviceIdB);
        else
            (first, second) = (deviceIdB, deviceIdA);

        var material = $"Lucid|{pairingCode}|{first}|{second}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(material));
    }

    // ── Categories shared during sync ─────────────────────────────────────────

    public static IReadOnlyList<string> SharedDataCategories =>
    [
        "Workload classification",
        "CPU / RAM usage percentages",
        "Thermal readings (if available)",
        "Storage utilization percentage",
        "Active insight count",
        "System uptime",
    ];

    // ── Persistence ───────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            _pairings = JsonSerializer.Deserialize<List<StoredDevicePairing>>(json) ?? [];
        }
        catch
        {
            _pairings = [];
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            List<StoredDevicePairing> snapshot;
            lock (_lock) snapshot = [.. _pairings];
            var json = JsonSerializer.Serialize(
                snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch { /* best-effort */ }
    }
}
