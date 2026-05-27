using System.Management;

namespace Lucid.Services.Distributed;

/// <summary>
/// Generates and persists a stable device identity.
/// The device ID (GUID) is stored to %LOCALAPPDATA%\Lucid\device_id.txt and
/// survives app restarts. Hardware characteristics (CPU, RAM, battery, GPU) are
/// probed via WMI and used to infer the device role.
///
/// All hardware probes are wrapped in try/catch — failures degrade gracefully
/// to safe defaults. The identity is lazily computed on first access.
/// </summary>
public sealed class DeviceIdentityService
{
    private static readonly string _dataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucid");

    private static readonly string _idFile = Path.Combine(_dataDir, "device_id.txt");

    private DeviceIdentity? _identity;

    /// <summary>
    /// Returns the device identity, computing it once on first call.
    /// Safe to call from any thread — the identity is immutable after creation.
    /// </summary>
    public DeviceIdentity GetOrCreateIdentity()
    {
        _identity ??= BuildIdentity();
        return _identity;
    }

    // ── Identity construction ─────────────────────────────────────────────────

    private static DeviceIdentity BuildIdentity()
    {
        var deviceId = LoadOrCreateDeviceId();
        var cpuCores = Environment.ProcessorCount;
        var ramBytes = ProbeInstalledRamBytes();
        var battery  = ProbeHasBattery();
        var gpu      = ProbeHasDedicatedGpu();
        var role     = DeviceRoleClassifier.Classify(battery, gpu, cpuCores, ramBytes);

        return new DeviceIdentity(
            DeviceId:        deviceId,
            DeviceName:      Environment.MachineName,
            OsVersion:       Environment.OSVersion.VersionString,
            MachineName:     Environment.MachineName,
            InferredRole:    role,
            CpuCoreCount:    cpuCores,
            RamBytes:        ramBytes,
            HasBattery:      battery,
            HasDedicatedGpu: gpu,
            ProfiledAt:      DateTimeOffset.Now);
    }

    private static string LoadOrCreateDeviceId()
    {
        try
        {
            Directory.CreateDirectory(_dataDir);

            if (File.Exists(_idFile))
            {
                var stored = File.ReadAllText(_idFile).Trim();
                if (Guid.TryParse(stored, out _))
                    return stored;
            }

            var newId = Guid.NewGuid().ToString();
            File.WriteAllText(_idFile, newId);
            return newId;
        }
        catch
        {
            // File I/O failed — session-only ID that won't persist across restarts
            return Guid.NewGuid().ToString();
        }
    }

    // ── Hardware probes ───────────────────────────────────────────────────────

    private static long ProbeInstalledRamBytes()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["TotalVisibleMemorySize"] is ulong kb)
                    return (long)(kb * 1024UL);
            }
        }
        catch { /* ignore — fall through to fallback */ }

        // Fallback: GC-reported available memory (lower bound, not total)
        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    private static bool ProbeHasBattery()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
            foreach (ManagementObject _ in searcher.Get())
                return true;
        }
        catch { /* ignore */ }
        return false;
    }

    private static bool ProbeHasDedicatedGpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT AdapterCompatibility, AdapterRAM FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                var compat = obj["AdapterCompatibility"]?.ToString() ?? string.Empty;

                // Skip Microsoft Basic Display Adapter (software renderer)
                if (compat.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Require at least 512 MB VRAM to qualify as dedicated GPU
                if (obj["AdapterRAM"] is uint vram && vram >= 512u * 1024u * 1024u)
                    return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }
}
