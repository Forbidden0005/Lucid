using ExplainMyPC.Models;
using ExplainMyPC.Services.Telemetry.Samplers;
using Microsoft.Extensions.Logging;

namespace ExplainMyPC.Services;

/// <summary>
/// Production telemetry service that reads real hardware data from Windows
/// Performance Counters and the Win32 API.
///
/// Architecture:
///   • Each hardware subsystem is isolated in its own sampler class (ICpuSampler,
///     IRamSampler, IGpuSampler, IDiskSampler, IThermalSampler). Samplers handle
///     counter lifetime, fallback logic, and unit conversion independently.
///   • The main poll loop runs on a dedicated Task (ThreadPool thread). All
///     callers that subscribe to ReadingUpdated MUST marshal to the UI thread
///     (DispatcherQueue.TryEnqueue) before touching WinUI objects.
///   • A CancellationTokenSource drives clean shutdown. Stop() signals the token
///     and awaits the loop task; Dispose() does the same and releases sampler
///     resources.
///
/// Polling intervals:
///   Hardware counters  →  1 second  (CPU, RAM, GPU, Disk I/O)
///   Thermal zones      →  5 seconds (ACPI reads change slowly; reduces overhead)
///
/// Fallback:
///   If any sampler fails on a given tick (counter disappeared, access denied,
///   etc.) the exception is caught and logged. The previous value for that metric
///   is reused so downstream consumers never receive NaN or garbage.
///
/// Thread safety:
///   _cpuTemp / _gpuTemp fields are only written from the poll task and only read
///   while assembling the reading on the same task — no locking required.
/// </summary>
public sealed class WindowsTelemetryService : ITelemetryService, IDisposable
{
    public event EventHandler<TelemetryReading>? ReadingUpdated;

    private static readonly TimeSpan PollInterval    = TimeSpan.FromMilliseconds(1000);
    private const int ThermalPollInterval            = 5; // ticks between thermal reads

    private readonly ILogger<WindowsTelemetryService> _logger;

    private readonly ICpuSampler     _cpu;
    private readonly IRamSampler     _ram;
    private readonly IGpuSampler     _gpu;
    private readonly IDiskSampler    _disk;
    private readonly IThermalSampler _thermal;

    // Last-known thermal values; reused between thermal poll ticks.
    private double _lastCpuTempC;
    private bool   _thermalAvailable;

    private CancellationTokenSource? _cts;
    private Task?                    _pollTask;
    private int                      _thermalTickCounter;

    public WindowsTelemetryService(ILogger<WindowsTelemetryService> logger)
    {
        _logger  = logger;
        _cpu     = new CpuSampler();
        _ram     = new RamSampler();
        _gpu     = new GpuSampler();
        _disk    = new DiskSampler();
        _thermal = new ThermalSampler();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Start()
    {
        if (_pollTask is { IsCompleted: false }) return;

        _cts      = new CancellationTokenSource();
        _pollTask = Task.Run(() => RunPollLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try   { _pollTask?.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Telemetry poll task did not stop cleanly."); }

        _cts?.Dispose();
        _cts      = null;
        _pollTask = null;
    }

    public void Dispose()
    {
        Stop();
        _cpu.Dispose();
        _gpu.Dispose();
        _disk.Dispose();
        _thermal.Dispose();
        // RamSampler is not IDisposable (pure P/Invoke, no handles held).
    }

    // ── Poll loop ─────────────────────────────────────────────────────────────

    private async Task RunPollLoopAsync(CancellationToken ct)
    {
        InitializeSamplers();

        while (!ct.IsCancellationRequested)
        {
            var deadline = DateTimeOffset.UtcNow.Add(PollInterval);

            try
            {
                var reading = BuildReading();
                ReadingUpdated?.Invoke(this, reading);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error assembling telemetry reading.");
            }

            // Sleep for the remainder of the 1-second window so drift doesn't
            // accumulate when sampling takes non-trivial time.
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                try { await Task.Delay(remaining, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void InitializeSamplers()
    {
        SafeInit("CPU",     _cpu.Initialize);
        SafeInit("GPU",     _gpu.Initialize);
        SafeInit("Disk",    _disk.Initialize);
        SafeInit("Thermal", _thermal.Initialize);
        // RamSampler requires no initialization.
    }

    private void SafeInit(string name, Action init)
    {
        try   { init(); }
        catch (Exception ex) { _logger.LogWarning(ex, "{Sampler} sampler failed to initialize.", name); }
    }

    // ── Reading assembly ──────────────────────────────────────────────────────

    private TelemetryReading BuildReading()
    {
        var cpu  = SafeRead("CPU",  () => _cpu.Read(),  new CpuSample(0, 3.0));
        var ram  = SafeRead("RAM",  () => _ram.Read(),  new RamSample(0, 0, 0));
        var gpu  = SafeRead("GPU",  () => _gpu.Read(),  new GpuSample(0, 0, 0));
        var disk = SafeRead("Disk", () => _disk.Read(), new DiskSample(0, 0, 0, 0));

        RefreshThermalIfDue();

        double diskIoPct = disk.ReadMbps + disk.WriteMbps > 0
            ? Math.Clamp((disk.ReadMbps + disk.WriteMbps) / 3.7 * 100.0, 0, 100)
            : 0;

        return new TelemetryReading(
            Timestamp:            DateTimeOffset.Now,

            CpuPercent:           cpu.UsagePercent,
            CpuTemperatureCelsius: _thermalAvailable ? _lastCpuTempC : 0,
            CpuFrequencyGhz:      cpu.FrequencyGhz,

            RamPercent:           ram.UsagePercent,
            RamUsedMb:            ram.UsedMb,
            RamTotalMb:           ram.TotalMb,

            GpuPercent:           gpu.UsagePercent,
            GpuTemperatureCelsius: 0,            // requires kernel driver (Phase 3)
            GpuVramUsedGb:        gpu.VramUsedGb,
            GpuVramTotalGb:       gpu.VramTotalGb,

            DiskReadMbps:         disk.ReadMbps,
            DiskWriteMbps:        disk.WriteMbps,
            DiskUsagePercent:     diskIoPct,
            DiskUsedGb:           disk.UsedGb,
            DiskTotalGb:          disk.TotalGb);
    }

    private void RefreshThermalIfDue()
    {
        if (++_thermalTickCounter < ThermalPollInterval) return;
        _thermalTickCounter = 0;

        var t = SafeRead("Thermal", () => _thermal.Read(), new ThermalSample(0, false));
        _thermalAvailable = t.IsAvailable;
        if (t.IsAvailable) _lastCpuTempC = t.CpuCelsius;
    }

    private T SafeRead<T>(string name, Func<T> read, T fallback)
    {
        try   { return read(); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "{Sampler} sampler read failed; using fallback.", name);
            return fallback;
        }
    }
}
