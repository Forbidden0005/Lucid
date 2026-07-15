using System.Diagnostics;

namespace Lucid.Services.Telemetry.Samplers;

public interface IThermalSampler : IDisposable
{
    /// <summary>Enumerate thermal zones. Call once before Read().</summary>
    void Initialize();
    ThermalSample Read();
}

public sealed record ThermalSample(
    double CpuCelsius,
    bool   IsAvailable);

/// <summary>
/// Reads CPU temperature from the Windows "Thermal Zone Information" performance
/// counter category — no kernel driver, WMI, or elevation required.
///
/// Zone selection:
///   Windows exposes one instance per ACPI thermal zone. Instance names are
///   firmware-defined (e.g. "_TZ.THM0", "ACPI\ThermalZone\CPU_") and vary widely
///   across OEMs and motherboards. This sampler heuristically selects the hottest
///   zone, which on most consumer hardware is the CPU package zone.
///
/// Unit:
///   The counter stores temperature in tenths of a degree Kelvin (dK).
///   Conversion: °C = (value / 10.0) − 273.15
///
/// Availability:
///   The category may be absent on some hypervisors, Windows Server Core
///   installations, or systems where ACPI thermal tables report no zones.
///   ThermalSample.IsAvailable tells callers whether a real reading was obtained
///   so the UI can show "N/A" rather than "0 °C".
///
/// Polling rate:
///   The WindowsTelemetryService samples thermal data every ThermalPollInterval
///   ticks (default: every 5 seconds). Thermal data changes slowly enough that
///   higher frequency adds no value and keeps counter overhead low.
/// </summary>
public sealed class ThermalSampler : IThermalSampler
{
    private const string Category    = "Thermal Zone Information";
    private const string CounterName = "Temperature";

    // Tenths-of-Kelvin to Celsius: (dK / 10.0) - 273.15
    private const double DkToCelsiusOffset = 273.15;
    private const double DkScale           = 10.0;

    // Plausible CPU temperature bounds (Celsius). Values outside are noise.
    private const double MinPlausible = 10.0;
    private const double MaxPlausible = 110.0;

    private readonly List<PerformanceCounter> _counters = [];
    private bool _available;

    public void Initialize()
    {
        if (!PerformanceCounterCategory.Exists(Category))
            return;

        try
        {
            var cat       = new PerformanceCounterCategory(Category);
            var instances = cat.GetInstanceNames();

            foreach (var inst in instances)
            {
                try
                {
                    _counters.Add(new PerformanceCounter(Category, CounterName, inst, readOnly: true));
                }
                catch { /* best-effort: counter instance vanished — sampler degrades to unavailable */ }
            }
        }
        catch { /* best-effort: thermal counters unavailable on this machine */ }

        if (_counters.Count == 0) return;

        _available = true;

        // Warm-up: rate counters return 0 on the first read.
        foreach (var c in _counters)
            _ = c.NextValue();
    }

    public ThermalSample Read()
    {
        if (!_available)
            return new ThermalSample(0, IsAvailable: false);

        double maxCelsius = double.MinValue;

        foreach (var counter in _counters)
        {
            try
            {
                double raw     = counter.NextValue();
                double celsius = raw / DkScale - DkToCelsiusOffset;

                if (celsius >= MinPlausible && celsius <= MaxPlausible)
                    maxCelsius = Math.Max(maxCelsius, celsius);
            }
            catch { /* stale counter — skip, still report other zones */ }
        }

        if (maxCelsius == double.MinValue)
            return new ThermalSample(0, IsAvailable: false);

        return new ThermalSample(Math.Round(maxCelsius, 1), IsAvailable: true);
    }

    public void Dispose()
    {
        foreach (var c in _counters) c.Dispose();
        _counters.Clear();
    }
}
