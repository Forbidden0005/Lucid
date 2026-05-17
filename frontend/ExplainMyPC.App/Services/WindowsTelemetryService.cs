using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Telemetry.Samplers;
using Microsoft.UI.Dispatching;

namespace ExplainMyPC.Services;

/// <summary>
/// Production implementation of ITelemetryService.
///
/// Coordinates five sampler classes on a background ThreadPool task and
/// marshals each completed TelemetrySnapshot to the UI thread via
/// DispatcherQueue before raising ReadingAvailable.
///
/// Sampler schedule:
///   CPU, RAM, GPU, Disk  — every poll cycle (~1.5 s)
///   Thermal              — every ThermalIntervalTicks cycles (~7.5 s)
///   Temperature changes slowly; sampling it every cycle adds overhead with
///   no perceptible benefit.
///
/// All sampler initialisation (counter discovery, registry reads, warm-up
/// samples) runs on the background thread so the UI thread stays responsive
/// at app startup.
///
/// Lifetime:
///   Created and started by AppServices.Initialize().
///   Disposed by AppServices.Shutdown() when the main window closes.
/// </summary>
public sealed class WindowsTelemetryService : ITelemetryService, IDisposable
{
    private const int WarmupDelayMs         = 1_000;   // let rate counters settle
    private const int PollIntervalMs        = 1_500;   // 1.5 s per cycle
    private const int ThermalIntervalTicks  = 5;       // sample thermal every 5 cycles (~7.5 s)
    private const int ProcessIntervalTicks  = 3;       // sample processes every 3 cycles (~4.5 s)

    private readonly DispatcherQueue _dispatcher;

    // Samplers — created on the UI thread, initialised on the background thread.
    private readonly CpuSampler     _cpu     = new();
    private readonly RamSampler     _ram     = new();
    private readonly GpuSampler     _gpu     = new();
    private readonly DiskSampler    _disk    = new();
    private readonly ThermalSampler _thermal = new();
    private readonly ProcessSampler _process = new();

    private CancellationTokenSource? _cts;
    private Task?                    _pollTask;
    private int                      _thermalTick;
    private int                      _processTick;
    private ThermalSample            _lastThermal   = new(0, IsAvailable: false);
    private IReadOnlyList<Helpers.ProcessSample> _lastProcesses = [];
    private bool                     _disposed;

    public event EventHandler<TelemetrySnapshot>? ReadingAvailable;
    public TelemetrySnapshot? LastReading { get; private set; }

    public bool IsRunning =>
        _cts is { IsCancellationRequested: false } &&
        _pollTask is { IsCompleted: false };

    public WindowsTelemetryService(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsTelemetryService));
        if (IsRunning) return;

        _cts      = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    // ── Background loop ───────────────────────────────────────────────────────

    private async Task PollLoopAsync(CancellationToken ct)
    {
        // Initialise all samplers on the background thread — counter discovery,
        // registry reads, and warm-up samples must not block the UI.
        _cpu.Initialize();
        _gpu.Initialize();
        _disk.Initialize();
        _thermal.Initialize();
        // RamSampler is stateless — no initialisation required.

        try { await Task.Delay(WarmupDelayMs, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            TelemetrySnapshot snapshot;
            try   { snapshot = Sample(); }
            catch { break; }   // unexpected error — stop cleanly

            // Marshal to UI thread before raising the event so subscribers
            // can update ObservableObject properties without extra dispatching.
            _dispatcher.TryEnqueue(() =>
            {
                LastReading = snapshot;
                ReadingAvailable?.Invoke(this, snapshot);
            });

            try { await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ── Sampling ──────────────────────────────────────────────────────────────

    private TelemetrySnapshot Sample()
    {
        // Thermal changes slowly — only refresh every ThermalIntervalTicks cycles.
        if (_thermalTick == 0)
            _lastThermal = _thermal.Read();
        _thermalTick = (_thermalTick + 1) % ThermalIntervalTicks;

        // Process list refreshes every ProcessIntervalTicks cycles (~4.5 s).
        // Enumeration is fast but CPU-delta accuracy doesn't benefit from sub-second sampling.
        if (_processTick == 0)
            _lastProcesses = _process.Sample();
        _processTick = (_processTick + 1) % ProcessIntervalTicks;

        var cpu  = _cpu.Read();
        var ram  = _ram.Read();
        var gpu  = _gpu.Read();
        var disk = _disk.Read();

        // RamSampler returns MiB; TelemetrySnapshot and ViewModel use GB.
        const double MibPerGib = 1024.0;
        double ramUsedGb  = Math.Round(ram.UsedMb  / MibPerGib, 1);
        double ramTotalGb = Math.Round(ram.TotalMb / MibPerGib, 0);

        // DiskSampler aggregates all fixed drives; compute overall usage %.
        double diskPct = disk.TotalGb > 0
            ? Math.Round((double)disk.UsedGb / disk.TotalGb * 100.0, 1)
            : 0;

        return new TelemetrySnapshot(
            // Core
            CpuPercent:    Math.Round(cpu.UsagePercent, 1),
            CpuFrequencyGhz: Math.Round(cpu.FrequencyGhz, 2),
            CpuCoreCount:  Environment.ProcessorCount,
            RamPercent:    Math.Round(ram.UsagePercent, 1),
            RamUsedGb:     ramUsedGb,
            RamTotalGb:    ramTotalGb,
            DiskPercent:   diskPct,
            DiskUsedGb:    disk.UsedGb,
            DiskTotalGb:   disk.TotalGb,
            GpuPercent:    Math.Round(gpu.UsagePercent, 1),
            GpuAvailable:  _gpu.IsAvailable,

            // Extended (Phase 3)
            GpuVramUsedGb:           Math.Round(gpu.VramUsedGb,  1),
            GpuVramTotalGb:          Math.Round(gpu.VramTotalGb, 1),
            DiskReadMbps:            Math.Round(disk.ReadMbps,   1),
            DiskWriteMbps:           Math.Round(disk.WriteMbps,  1),
            CpuTemperatureCelsius:   Math.Round(_lastThermal.CpuCelsius, 1),
            CpuTemperatureAvailable: _lastThermal.IsAvailable,

            // Process samples (Phase 4) — cached between sampling ticks
            TopProcesses:            _lastProcesses);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();

        _cpu.Dispose();
        _gpu.Dispose();
        _disk.Dispose();
        _thermal.Dispose();
        _process.Dispose();
        // RamSampler holds no resources.
    }
}
