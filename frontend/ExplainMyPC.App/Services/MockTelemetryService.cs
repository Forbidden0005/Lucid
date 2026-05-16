using ExplainMyPC.Models;
using System.Timers;
using Timer = System.Timers.Timer;

namespace ExplainMyPC.Services;

/// <summary>
/// Phase 1 mock implementation of ITelemetryService.
///
/// Generates realistic-looking telemetry using:
///   • Sine waves with different periods for each metric (natural drift)
///   • Small random noise on top of the wave
///   • Occasional short spikes (CPU / Disk) to simulate real load events
///   • RAM that drifts slowly (memory doesn't spike the way CPU does)
///
/// All values stay within physically plausible bounds.
/// The timer fires on a ThreadPool thread — subscribers marshal to UI thread.
///
/// Replace this class with a Rust FFI bridge in Phase 2; the interface is
/// identical so no consumers need to change.
/// </summary>
public sealed class MockTelemetryService : ITelemetryService, IDisposable
{
    private readonly Timer  _timer;
    private readonly Random _rng = new();
    private bool _running;

    // ── Oscillator state (one phase per metric for independent variation) ────
    private double _cpuPhase  = 0.0;
    private double _ramDrift  = 56.0;   // RAM drifts slowly, no oscillator
    private double _gpuPhase  = 1.57;   // π/2 offset so GPU doesn't mirror CPU
    private double _diskPhase = 3.14;

    // ── Spike state ──────────────────────────────────────────────────────────
    private double _cpuSpike  = 0;
    private double _diskBurst = 0;

    // ── Hardware constants (stable mock "facts" about this machine) ──────────
    private const long   RamTotalMb      = 16_384;
    private const double GpuVramTotalGb  = 8.0;
    private const long   DiskTotalGb     = 512;
    private const long   DiskUsedGb      = 347;

    public event EventHandler<TelemetryReading>? ReadingUpdated;

    public MockTelemetryService()
    {
        _timer = new Timer(interval: 1_000); // 1-second cadence
        _timer.Elapsed   += OnTimerElapsed;
        _timer.AutoReset  = true;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer.Stop();
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    // ── Timer callback (ThreadPool thread) ───────────────────────────────────

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        var reading = GenerateReading();
        ReadingUpdated?.Invoke(this, reading);
    }

    private TelemetryReading GenerateReading()
    {
        var now = DateTimeOffset.Now;

        // ── CPU ──────────────────────────────────────────────────────────────
        _cpuPhase += 0.11;

        // Occasional load spikes (5% chance per tick)
        if (_rng.NextDouble() < 0.05) _cpuSpike = 30 + _rng.NextDouble() * 35;
        _cpuSpike = Math.Max(0, _cpuSpike - 8);    // spikes decay quickly

        var cpu = 14
            + 10 * Math.Sin(_cpuPhase)
            +  6 * Math.Sin(_cpuPhase * 2.3 + 1.1)
            + _cpuSpike
            + (_rng.NextDouble() - 0.5) * 5;
        cpu = Math.Clamp(cpu, 2, 97);

        var cpuTemp  = 43 + cpu * 0.38 + (_rng.NextDouble() - 0.5) * 2;
        var cpuFreq  = 3.2 + cpu / 100.0 * 1.4 + (_rng.NextDouble() - 0.5) * 0.1;

        // ── RAM ──────────────────────────────────────────────────────────────
        // Slow random walk, clamped to a realistic range
        _ramDrift += (_rng.NextDouble() - 0.5) * 0.6;
        _ramDrift  = Math.Clamp(_ramDrift, 48.0, 72.0);

        var ram    = _ramDrift + (_rng.NextDouble() - 0.5) * 1.2;
        var ramMb  = (long)(ram / 100.0 * RamTotalMb);

        // ── GPU ──────────────────────────────────────────────────────────────
        _gpuPhase += 0.07;

        var gpuBase = 8 + 6 * Math.Sin(_gpuPhase) + 4 * Math.Sin(_gpuPhase * 1.7);
        // Occasional GPU burst (3% chance)
        var gpuBurst = _rng.NextDouble() < 0.03 ? 25 + _rng.NextDouble() * 45 : 0;
        var gpu = Math.Clamp(gpuBase + gpuBurst + (_rng.NextDouble() - 0.5) * 3, 2, 97);

        var gpuTemp   = 37 + gpu * 0.42 + (_rng.NextDouble() - 0.5) * 2;
        var gpuVram   = 1.8 + gpu / 100.0 * 5.2 + (_rng.NextDouble() - 0.5) * 0.2;
        gpuVram       = Math.Clamp(gpuVram, 1.5, GpuVramTotalGb);

        // ── Disk I/O ─────────────────────────────────────────────────────────
        _diskPhase += 0.05;

        // Bursts: occasionally spiky, otherwise near-quiet
        if (_rng.NextDouble() < 0.08) _diskBurst = 40 + _rng.NextDouble() * 110;
        _diskBurst = Math.Max(0, _diskBurst - 18);

        var diskRead  = Math.Clamp(_diskBurst * 0.7 + (_rng.NextDouble() * 5), 0, 250);
        var diskWrite = Math.Clamp(_diskBurst * 0.3 + (_rng.NextDouble() * 3), 0, 120);

        // Disk activity % (for the graph): normalised I/O throughput
        var diskIoPercent = Math.Clamp((diskRead + diskWrite) / 3.7, 0, 100);

        return new TelemetryReading(
            Timestamp:            now,
            CpuPercent:           Math.Round(cpu,     1),
            CpuTemperatureCelsius:Math.Round(cpuTemp, 1),
            CpuFrequencyGhz:      Math.Round(cpuFreq, 2),
            RamPercent:           Math.Round(ram,     1),
            RamUsedMb:            ramMb,
            RamTotalMb:           RamTotalMb,
            GpuPercent:           Math.Round(gpu,     1),
            GpuTemperatureCelsius:Math.Round(gpuTemp, 1),
            GpuVramUsedGb:        Math.Round(gpuVram, 1),
            GpuVramTotalGb:       GpuVramTotalGb,
            DiskReadMbps:         Math.Round(diskRead,  1),
            DiskWriteMbps:        Math.Round(diskWrite, 1),
            DiskUsagePercent:     (double)DiskUsedGb / DiskTotalGb * 100,
            DiskUsedGb:           DiskUsedGb,
            DiskTotalGb:          DiskTotalGb
        );
    }
}
