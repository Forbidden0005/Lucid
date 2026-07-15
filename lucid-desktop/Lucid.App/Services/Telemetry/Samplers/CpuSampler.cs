using System.Diagnostics;
using Microsoft.Win32;

namespace Lucid.Services.Telemetry.Samplers;

public interface ICpuSampler : IDisposable
{
    /// <summary>Prime the counters. Must be called once before the first Read().</summary>
    void Initialize();
    CpuSample Read();
}

public sealed record CpuSample(double UsagePercent, double FrequencyGhz);

/// <summary>
/// Reads CPU utilization and frequency from Windows Performance Counters.
///
/// Counter selection:
///   "Processor Information \ % Processor Utility" is preferred — it accounts
///   for turbo boost and reports true utilization rather than clock-cycle fraction.
///   Falls back to "Processor \ % Processor Time" when the preferred category is
///   absent (older Windows or some VM hypervisors).
///
/// Frequency:
///   "Processor Information \ % of Maximum Frequency" gives the current speed as
///   a fraction of the rated maximum. The rated maximum is read once from the
///   registry key written by the CPU driver at boot — no WMI or elevation needed.
///
/// First-read caveat:
///   Rate-based counters return 0.0 on the very first NextValue() call because
///   they need two time-stamps to compute a rate. The WindowsTelemetryService
///   calls Initialize() during Start(), which issues a warm-up sample so the
///   first real reading the ViewModel receives is already meaningful.
/// </summary>
public sealed class CpuSampler : ICpuSampler
{
    private const string PreferredCategory = "Processor Information";
    private const string FallbackCategory  = "Processor";
    private const string UtilityCounter    = "% Processor Utility";
    private const string ProcessorTime     = "% Processor Time";
    private const string FreqPercentCounter = "% of Maximum Frequency";
    private const string TotalInstance     = "_Total";

    private PerformanceCounter? _usageCounter;
    private PerformanceCounter? _freqCounter;
    private double _maxFrequencyGhz = 3.0;

    public void Initialize()
    {
        // Preferred path: "Processor Information" (Windows 7+, reflects turbo).
        try
        {
            _usageCounter = new PerformanceCounter(
                PreferredCategory, UtilityCounter, TotalInstance, readOnly: true);
            _freqCounter = new PerformanceCounter(
                PreferredCategory, FreqPercentCounter, TotalInstance, readOnly: true);
        }
        catch
        {
            _usageCounter?.Dispose();
            _usageCounter = null;
            _freqCounter  = null;
        }

        // Fallback path: "Processor" exists on all Windows versions.
        if (_usageCounter is null)
        {
            try
            {
                _usageCounter = new PerformanceCounter(
                    FallbackCategory, ProcessorTime, TotalInstance, readOnly: true);
            }
            catch { /* telemetry unavailable on this machine */ }
        }

        _maxFrequencyGhz = ReadMaxFrequencyGhz();

        // Warm-up: discard the mandatory first 0.0 sample.
        _ = _usageCounter?.NextValue();
        _ = _freqCounter?.NextValue();
    }

    public CpuSample Read()
    {
        double usage   = 0;
        double freqGhz = _maxFrequencyGhz;

        try
        {
            if (_usageCounter is not null)
                usage = Math.Clamp(_usageCounter.NextValue(), 0, 100);
        }
        catch
        {
            _usageCounter?.Dispose();
            _usageCounter = null;
        }

        try
        {
            if (_freqCounter is not null)
            {
                double pct = _freqCounter.NextValue();
                freqGhz = _maxFrequencyGhz * Math.Clamp(pct / 100.0, 0, 2);
            }
        }
        catch
        {
            _freqCounter?.Dispose();
            _freqCounter = null;
        }

        return new CpuSample(usage, Math.Max(freqGhz, 0.1));
    }

    /// <summary>
    /// Reads rated CPU max speed from the registry key populated by the CPU
    /// driver at Windows boot. No elevation or WMI required.
    /// </summary>
    private static double ReadMaxFrequencyGhz()
    {
        try
        {
            using var key = Registry.LocalMachine
                .OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("~MHz") is int mhz && mhz > 0)
                return mhz / 1000.0;
        }
        catch { /* best-effort: registry read failed — fall back to nominal 3.0 GHz */ }
        return 3.0;
    }

    public void Dispose()
    {
        _usageCounter?.Dispose();
        _freqCounter?.Dispose();
    }
}
