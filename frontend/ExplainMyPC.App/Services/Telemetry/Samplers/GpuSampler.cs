using System.Diagnostics;
using Microsoft.Win32;

namespace ExplainMyPC.Services.Telemetry.Samplers;

public interface IGpuSampler : IDisposable
{
    /// <summary>Check counter availability and read VRAM capacity. Call once before Read().</summary>
    void Initialize();
    GpuSample Read();

    /// <summary>
    /// True after Initialize() if the "GPU Engine" performance-counter category
    /// is present on this system. False on VMs or machines without a WDDM driver.
    /// </summary>
    bool IsAvailable { get; }
}

public sealed record GpuSample(double UsagePercent, double VramUsedGb, double VramTotalGb);

/// <summary>
/// Reads GPU utilization and VRAM usage from Windows Performance Counters.
///
/// GPU Engine counter (Windows 10 1803 / build 17134+):
///   Category "GPU Engine", counter "Utilization Percentage".
///   Each instance encodes a process PID, adapter LUID, physical GPU index, engine
///   index, and engine type in its name, e.g.:
///     pid_2468_luid_0x0000_0x9274_phys_0_eng_0_engtype_3D
///   Instances are created and destroyed as GPU-using processes start and exit, so
///   the instance list is re-enumerated on every Read() to stay current.
///   Utilization is taken as the MAX across all 3D and Compute engine instances
///   on phys_0, mirroring the GPU widget in Windows Task Manager.
///
/// VRAM usage:
///   Category "GPU Adapter Memory", counter "Dedicated Usage" (bytes).
///   Adapter instances are static for the session lifetime.
///
/// VRAM capacity:
///   Read once at Initialize() from the display-adapter registry key written by
///   the GPU driver — no WMI or elevation required. The key stores capacity as a
///   QWORD (8-byte value or byte[] depending on driver).
///
/// GPU temperature:
///   Not available via public Windows Performance Counters without a kernel driver
///   (e.g. LibreHardwareMonitor). GpuSample.TemperatureCelsius is intentionally
///   omitted; the ThermalSampler provides an ACPI zone temperature as a proxy.
/// </summary>
public sealed class GpuSampler : IGpuSampler
{
    private const string EngineCategory      = "GPU Engine";
    private const string UtilizationCounter  = "Utilization Percentage";
    private const string MemoryCategory      = "GPU Adapter Memory";
    private const string DedicatedCounter    = "Dedicated Usage";

    private bool _engineAvailable;
    private bool _memoryAvailable;
    private double _vramTotalGb;

    public bool IsAvailable => _engineAvailable;

    // Keyed by instance name. Re-populated every Read() as processes come and go.
    private readonly Dictionary<string, PerformanceCounter> _engineCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PerformanceCounter> _vramCounters = [];

    public void Initialize()
    {
        _engineAvailable = PerformanceCounterCategory.Exists(EngineCategory);
        _memoryAvailable = PerformanceCounterCategory.Exists(MemoryCategory);
        _vramTotalGb     = ReadVramTotalGb();

        if (_memoryAvailable)
            InitVramCounters();

        // Warm-up: rate counters return 0 on their first NextValue().
        // Pre-populate the engine counter dictionary with the current instance list
        // so the first real Read() returns actual data instead of all-zeros.
        if (_engineAvailable)
            RefreshEngineCounters();

        foreach (var c in _engineCounters.Values)
            _ = c.NextValue();
        foreach (var c in _vramCounters)
            _ = c.NextValue();
    }

    public GpuSample Read()
    {
        double usagePct  = ReadEngineUtilization();
        double vramUsed  = ReadVramUsedGb();
        return new GpuSample(usagePct, vramUsed, _vramTotalGb);
    }

    // ── Engine utilization ────────────────────────────────────────────────────

    private double ReadEngineUtilization()
    {
        if (!_engineAvailable) return 0;

        try
        {
            RefreshEngineCounters();

            double max = 0;
            foreach (var (name, counter) in _engineCounters.ToList())
            {
                try
                {
                    max = Math.Max(max, counter.NextValue());
                }
                catch
                {
                    counter.Dispose();
                    _engineCounters.Remove(name);
                }
            }
            return Math.Clamp(max, 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    private void RefreshEngineCounters()
    {
        string[] currentInstances;
        try
        {
            currentInstances = new PerformanceCounterCategory(EngineCategory).GetInstanceNames();
        }
        catch
        {
            _engineAvailable = false;
            return;
        }

        var relevant = currentInstances
            .Where(IsRelevantEngine)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove counters for processes that exited.
        foreach (var dead in _engineCounters.Keys.Where(k => !relevant.Contains(k)).ToList())
        {
            _engineCounters[dead].Dispose();
            _engineCounters.Remove(dead);
        }

        // Add counters for new instances.
        foreach (var inst in relevant.Where(i => !_engineCounters.ContainsKey(i)))
        {
            try
            {
                _engineCounters[inst] = new PerformanceCounter(
                    EngineCategory, UtilizationCounter, inst, readOnly: true);
            }
            catch { /* instance already gone — race condition, skip */ }
        }
    }

    /// <summary>
    /// We track 3D and Compute engines, which represent GPU shader workloads.
    /// VideoDecode/VideoEncode are deliberately excluded: they saturate their own
    /// fixed-function units while the 3D engine stays idle, so including them
    /// would inflate the "GPU busy" reading for video playback.
    /// </summary>
    private static bool IsRelevantEngine(string instanceName) =>
        instanceName.Contains("engtype_3D",      StringComparison.OrdinalIgnoreCase) ||
        instanceName.Contains("engtype_Compute",  StringComparison.OrdinalIgnoreCase);

    // ── VRAM ──────────────────────────────────────────────────────────────────

    private void InitVramCounters()
    {
        try
        {
            var cat       = new PerformanceCounterCategory(MemoryCategory);
            var instances = cat.GetInstanceNames();
            foreach (var inst in instances)
            {
                try
                {
                    _vramCounters.Add(new PerformanceCounter(
                        MemoryCategory, DedicatedCounter, inst, readOnly: true));
                }
                catch { }
            }
        }
        catch
        {
            _memoryAvailable = false;
        }
    }

    private double ReadVramUsedGb()
    {
        if (!_memoryAvailable || _vramCounters.Count == 0) return 0;

        double totalBytes = 0;
        foreach (var c in _vramCounters)
        {
            try { totalBytes += c.NextValue(); }
            catch { /* counter stale — ignore */ }
        }
        return totalBytes / (1024.0 * 1024.0 * 1024.0);
    }

    /// <summary>
    /// Reads dedicated VRAM size from the display adapter driver registry key.
    /// Works for NVIDIA, AMD, and Intel GPUs without elevation or WMI.
    /// The value is stored differently across driver versions:
    ///   - Modern drivers: QWORD as a long (8 bytes)
    ///   - Older drivers:  QWORD encoded as byte[] of length 8
    /// </summary>
    private static double ReadVramTotalGb()
    {
        const string driverClass =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(driverClass);
            if (classKey is null) return 0;

            foreach (var subName in classKey.GetSubKeyNames())
            {
                if (!int.TryParse(subName, out _)) continue;

                using var adapterKey = classKey.OpenSubKey(subName);
                var raw = adapterKey?.GetValue("HardwareInformation.qwMemorySize");

                long bytes = raw switch
                {
                    long   l  when l  > 0                        => l,
                    byte[] ba when ba.Length == 8                => BitConverter.ToInt64(ba, 0),
                    int    i  when i  > 0                        => i,
                    _                                            => 0
                };

                if (bytes > 0)
                    return bytes / (1024.0 * 1024.0 * 1024.0);
            }
        }
        catch { }
        return 0;
    }

    public void Dispose()
    {
        foreach (var c in _engineCounters.Values) c.Dispose();
        _engineCounters.Clear();

        foreach (var c in _vramCounters) c.Dispose();
        _vramCounters.Clear();
    }
}
