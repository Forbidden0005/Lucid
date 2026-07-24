using Lucid.Helpers;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Lucid.Services;

/// <summary>
/// Deterministic-looking telemetry source retained for local UI diagnostics.
/// </summary>
public sealed class MockTelemetryService : ITelemetryService, IDisposable
{
    private readonly Timer _timer;
    private readonly Random _rng = new();
    private bool _disposed;
    private bool _running;
    private double _cpuPhase;
    private double _diskBurst;
    private double _diskPhase = 3.14;
    private double _gpuPhase = 1.57;
    private double _ramDrift = 56.0;
    private double _cpuSpike;

    /// <inheritdoc />
    public event EventHandler<TelemetrySnapshot>? ReadingAvailable;

    /// <inheritdoc />
    public TelemetrySnapshot? LastReading { get; private set; }

    /// <inheritdoc />
    public bool IsRunning => _running;

    /// <summary>
    /// Initializes a new instance of the <see cref="MockTelemetryService"/> class.
    /// </summary>
    public MockTelemetryService()
    {
        _timer = new Timer(interval: 1_000)
        {
            AutoReset = true,
        };
        _timer.Elapsed += OnTimerElapsed;
    }

    /// <inheritdoc />
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_running)
        {
            return;
        }

        _running = true;
        _timer.Start();
    }

    /// <inheritdoc />
    public void Stop()
    {
        _running = false;
        _timer.Stop();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        var reading = GenerateReading();
        LastReading = reading;
        ReadingAvailable?.Invoke(this, reading);
    }

    private TelemetrySnapshot GenerateReading()
    {
        _cpuPhase += 0.11;
        if (_rng.NextDouble() < 0.05)
        {
            _cpuSpike = 30 + _rng.NextDouble() * 35;
        }

        _cpuSpike = Math.Max(0, _cpuSpike - 8);
        var cpu = Math.Clamp(
            14
            + 10 * Math.Sin(_cpuPhase)
            + 6 * Math.Sin(_cpuPhase * 2.3 + 1.1)
            + _cpuSpike
            + (_rng.NextDouble() - 0.5) * 5,
            2,
            97);

        _ramDrift += (_rng.NextDouble() - 0.5) * 0.6;
        _ramDrift = Math.Clamp(_ramDrift, 48.0, 72.0);
        var ram = _ramDrift + (_rng.NextDouble() - 0.5) * 1.2;

        _gpuPhase += 0.07;
        var gpuBase = 8 + 6 * Math.Sin(_gpuPhase) + 4 * Math.Sin(_gpuPhase * 1.7);
        var gpuBurst = _rng.NextDouble() < 0.03 ? 25 + _rng.NextDouble() * 45 : 0;
        var gpu = Math.Clamp(gpuBase + gpuBurst + (_rng.NextDouble() - 0.5) * 3, 2, 97);

        _diskPhase += 0.05;
        if (_rng.NextDouble() < 0.08)
        {
            _diskBurst = 40 + _rng.NextDouble() * 110;
        }

        _diskBurst = Math.Max(0, _diskBurst - 18);
        var diskRead = Math.Clamp(_diskBurst * 0.7 + _rng.NextDouble() * 5, 0, 250);
        var diskWrite = Math.Clamp(_diskBurst * 0.3 + _rng.NextDouble() * 3, 0, 120);

        const double ramTotalGb = 16.0;
        const double diskTotalGb = 512.0;
        const double diskUsedGb = 347.0;
        const double gpuVramTotalGb = 8.0;

        return new TelemetrySnapshot(
            CpuPercent: Math.Round(cpu, 1),
            CpuFrequencyGhz: Math.Round(3.2 + cpu / 100.0 * 1.4 + (_rng.NextDouble() - 0.5) * 0.1, 2),
            CpuCoreCount: Environment.ProcessorCount,
            RamPercent: Math.Round(ram, 1),
            RamUsedGb: Math.Round(ram / 100.0 * ramTotalGb, 1),
            RamTotalGb: ramTotalGb,
            DiskPercent: Math.Round(diskUsedGb / diskTotalGb * 100.0, 1),
            DiskUsedGb: diskUsedGb,
            DiskTotalGb: diskTotalGb,
            GpuPercent: Math.Round(gpu, 1),
            GpuAvailable: true,
            GpuVramUsedGb: Math.Round(Math.Clamp(1.8 + gpu / 100.0 * 5.2, 1.5, gpuVramTotalGb), 1),
            GpuVramTotalGb: gpuVramTotalGb,
            DiskReadMbps: Math.Round(diskRead, 1),
            DiskWriteMbps: Math.Round(diskWrite, 1),
            CpuTemperatureCelsius: Math.Round(43 + cpu * 0.38 + (_rng.NextDouble() - 0.5) * 2, 1),
            CpuTemperatureAvailable: true);
    }
}
