using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Lucid.Helpers;

/// <summary>
/// Lightweight Windows system telemetry poller.
///
/// Sampling strategy:
///   • CPU    — PerformanceCounter "Processor / % Processor Time / _Total"
///              (no admin rights required; first sample discarded as warmup)
///   • CPU Hz — CallNtPowerInformation (powrprof.dll); averages CurrentMhz
///              across all logical processors; &lt;1 µs per call
///   • RAM    — GlobalMemoryStatusEx (kernel32.dll); single syscall, &lt;1 µs
///   • Disk   — DriveInfo for the system drive; reports space usage, not I/O
///   • GPU    — PerformanceCounter "GPU Engine / Utilization (%)"
///              filtered to engtype_3D instances; sums across all adapters;
///              Windows 10 1803+ only; gracefully skipped if unavailable
///
/// Threading:
///   All sampling runs on a dedicated ThreadPool Task. Each completed snapshot
///   is marshalled back to the UI thread via DispatcherQueue.TryEnqueue before
///   the <see cref="ReadingAvailable"/> event is raised, so subscribers never
///   need their own dispatcher calls.
///
/// Lifetime:
///   Call <see cref="Start"/> once after construction.
///   Call <see cref="Dispose"/> (or <see cref="Stop"/> then Dispose) when the
///   owning page unloads to cancel the background task and release counters.
/// </summary>
public sealed class TelemetryPoller : IDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────

    private const int WarmupDelayMs  = 1_000; // wait one second for CPU counter to settle
    private const int PollIntervalMs = 1_500; // 1.5 s per cycle — snappy but not aggressive

    // ── Public event ──────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the UI thread after each successful poll cycle.
    /// Safe to update ObservableObject properties directly from the handler.
    /// </summary>
    public event EventHandler<TelemetrySnapshot>? ReadingAvailable;

    // ── Fields ────────────────────────────────────────────────────────────

    private readonly DispatcherQueue _dispatcher;

    private CancellationTokenSource? _cts;
    private Task?                    _pollTask;

    // Initialised on the background thread inside PollLoopAsync.
    private PerformanceCounter?       _cpuCounter;
    private List<PerformanceCounter>  _gpuCounters = [];
    private bool                      _gpuAvailable;

    private readonly int  _coreCount = Environment.ProcessorCount;
    private bool          _disposed;

    // ── Constructor ───────────────────────────────────────────────────────

    /// <param name="dispatcher">
    ///     The UI dispatcher to marshal readings onto.
    ///     Pass <c>DispatcherQueue.GetForCurrentThread()</c> from the ViewModel
    ///     constructor (which must run on the UI thread).
    /// </param>
    public TelemetryPoller(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Begins background polling. Safe to call once after construction.</summary>
    public void Start()
    {
        _cts      = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    /// <summary>Signals the background task to stop. Returns immediately.</summary>
    public void Stop() => _cts?.Cancel();

    // ── Background loop ───────────────────────────────────────────────────

    private async Task PollLoopAsync(CancellationToken ct)
    {
        // ── Initialise performance counters on the background thread ──────
        // PerformanceCounterCategory.Exists can do a registry query; keeping
        // it off the UI thread ensures the app stays responsive on first load.

        try
        {
            _cpuCounter = new PerformanceCounter(
                "Processor", "% Processor Time", "_Total", readOnly: true);
        }
        catch
        {
            // CPU counter unavailable — exit the loop silently.
            return;
        }

        TryInitGpuCounters();

        // ── Warmup — the CPU counter always returns 0 on the first call ──
        _cpuCounter.NextValue();
        foreach (var c in _gpuCounters) c.NextValue();

        try { await Task.Delay(WarmupDelayMs, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        // ── Poll loop ─────────────────────────────────────────────────────
        while (!ct.IsCancellationRequested)
        {
            TelemetrySnapshot snapshot;
            try   { snapshot = Sample(); }
            catch { break; } // unexpected error — stop cleanly

            // Marshal to UI thread before raising the event.
            _dispatcher.TryEnqueue(() => ReadingAvailable?.Invoke(this, snapshot));

            try { await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ── Sampling ──────────────────────────────────────────────────────────

    private TelemetrySnapshot Sample()
    {
        // Cache RAM and Disk results to avoid redundant syscalls and ensure the
        // three fields in each snapshot come from a single consistent read.
        var ram  = SampleRam();
        var disk = SampleDisk();

        return new TelemetrySnapshot(
            CpuPercent:      SampleCpu(),
            CpuFrequencyGhz: SampleCpuFrequency(),
            CpuCoreCount:    _coreCount,
            RamPercent:      ram.Percent,
            RamUsedGb:       ram.UsedGb,
            RamTotalGb:      ram.TotalGb,
            DiskPercent:     disk.Percent,
            DiskUsedGb:      disk.UsedGb,
            DiskTotalGb:     disk.TotalGb,
            GpuPercent:      SampleGpu(),
            GpuAvailable:    _gpuAvailable);
    }

    private double SampleCpu()
    {
        try   { return Math.Round(_cpuCounter!.NextValue(), 1); }
        catch { return 0; }
    }

    private double SampleCpuFrequency()
    {
        // CallNtPowerInformation(ProcessorInformation) returns one struct per
        // logical processor with CurrentMhz — the actual speed the OS is running
        // each core at right now. Average across all cores for a single value.
        try
        {
            int  count  = _coreCount;
            int  stride = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
            nint buf    = Marshal.AllocHGlobal(stride * count);
            try
            {
                uint status = CallNtPowerInformation(
                    ProcessorInformation, 0, 0, buf, (uint)(stride * count));
                if (status != 0) return 0;

                double totalMhz = 0;
                for (int i = 0; i < count; i++)
                {
                    var info = Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(buf + i * stride);
                    totalMhz += info.CurrentMhz;
                }
                return Math.Round(totalMhz / count / 1_000.0, 2);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return 0; }
    }

    // RAM is called twice in Sample() — cache it for this cycle to avoid double syscalls.
    // For a 1.5 s poll interval the tiny allocation is negligible.
    private (double Percent, double UsedGb, double TotalGb) SampleRam()
    {
        var info = new MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };
        if (!GlobalMemoryStatusEx(ref info)) return (0, 0, 0);

        const double GB  = 1024.0 * 1024 * 1024;
        double totalGb   = info.ullTotalPhys / GB;
        double availGb   = info.ullAvailPhys / GB;
        double usedGb    = totalGb - availGb;
        double pct       = totalGb > 0 ? usedGb / totalGb * 100.0 : 0;

        return (Math.Round(pct, 1), Math.Round(usedGb, 1), Math.Round(totalGb, 0));
    }

    private static (double Percent, double UsedGb, double TotalGb) SampleDisk()
    {
        try
        {
            string root  = Path.GetPathRoot(Environment.SystemDirectory)!;
            var    drive = new DriveInfo(root);
            if (!drive.IsReady) return (0, 0, 0);

            const double GB = 1024.0 * 1024 * 1024;
            double totalGb  = drive.TotalSize       / GB;
            double freeGb   = drive.TotalFreeSpace  / GB;
            double usedGb   = totalGb - freeGb;
            double pct      = totalGb > 0 ? usedGb / totalGb * 100.0 : 0;

            return (Math.Round(pct, 1), Math.Round(usedGb, 1), Math.Round(totalGb, 0));
        }
        catch { return (0, 0, 0); }
    }

    private double SampleGpu()
    {
        if (!_gpuAvailable) return 0;
        try
        {
            // Sum utilisation across all 3D engine instances.
            // Multiple adapters (e.g. iGPU + dGPU) are naturally aggregated.
            double total = _gpuCounters.Sum(c => c.NextValue());
            return Math.Min(Math.Round(total, 1), 100.0);
        }
        catch { return 0; }
    }

    // ── GPU counter initialisation ─────────────────────────────────────────

    private void TryInitGpuCounters()
    {
        // "GPU Engine" is a Windows 10 1803+ (build 17134) performance-counter
        // category. Each instance name encodes the engine type, e.g.:
        //   pid_1234_luid_0x00000000_0x0000ABCD_phys_0_eng_0_engtype_3D
        // We target engtype_3D — render/compute load — as the most meaningful
        // general-purpose signal. Other engine types (VideoDecode, Copy, etc.)
        // are excluded to avoid inflating the number.
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine")) return;

            var    cat       = new PerformanceCounterCategory("GPU Engine");
            var    instances = cat.GetInstanceNames();

            var targets = instances
                .Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (targets.Count == 0) return; // no 3D engines on this system

            foreach (var name in targets)
                _gpuCounters.Add(new PerformanceCounter(
                    "GPU Engine", "Utilization (%)", name, readOnly: true));

            _gpuAvailable = true;
        }
        catch
        {
            _gpuAvailable = false;
        }
    }

    // ── P/Invoke — RAM ────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint  dwLength;
        public uint  dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ── P/Invoke — CPU frequency ──────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSOR_POWER_INFORMATION
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    // informationLevel = 11 (ProcessorInformation)
    private const int ProcessorInformation = 11;

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern uint CallNtPowerInformation(
        int   informationLevel,
        nint  inputBuffer,
        uint  inputBufferLength,
        nint  outputBuffer,
        uint  outputBufferLength);

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();

        _cpuCounter?.Dispose();
        foreach (var c in _gpuCounters) c.Dispose();
        _gpuCounters.Clear();
    }
}
