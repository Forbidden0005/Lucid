namespace Lucid.Services.Reasoning.Context;

/// <summary>
/// Defines the expected resource behavior for a given <see cref="ActivityClass"/>.
///
/// Used by the cognitive reasoning engine to determine whether observed metrics
/// are anomalous *within context* — a 90% CPU during compilation is expected,
/// the same reading during idle is not.
///
/// Values are conservative, population-derived ranges — not per-machine baselines.
/// Per-machine personalization lives in <see cref="Lucid.Services.Baseline.SystemBaselineService"/>.
///
/// Immutable. Create via <see cref="For"/>.
/// </summary>
public sealed record RuntimeBehaviorModel
{
    /// <summary>The activity class this model describes.</summary>
    public required ActivityClass WorkloadType { get; init; }

    /// <summary>Human-readable description of this workload.</summary>
    public required string WorkloadDescription { get; init; }

    /// <summary>Expected CPU utilization range (%) for this workload.</summary>
    public required (double Low, double High) ExpectedCpuRange { get; init; }

    /// <summary>Expected RAM utilization range (%) for this workload.</summary>
    public required (double Low, double High) ExpectedRamRange { get; init; }

    /// <summary>Expected CPU temperature range (°C) for this workload.</summary>
    public required (double Low, double High) ExpectedThermalRange { get; init; }

    /// <summary>True if high CPU is contextually expected for this workload type.</summary>
    public required bool HighCpuIsExpected { get; init; }

    /// <summary>True if high disk I/O is contextually expected.</summary>
    public required bool HighDiskIoIsExpected { get; init; }

    /// <summary>True if elevated thermal readings are expected.</summary>
    public required bool ThermalElevationIsExpected { get; init; }

    /// <summary>
    /// True if this workload has a long setup window where transient
    /// elevated resource usage should not trigger alerts.
    /// </summary>
    public required bool HasTransientStartupPhase { get; init; }

    // ── Factory ────────────────────────────────────────────────────────────────

    /// <summary>Returns the runtime behavior model for the given workload type.</summary>
    public static RuntimeBehaviorModel For(ActivityClass activity) =>
        activity switch
        {
            ActivityClass.Gaming         => new()
            {
                WorkloadType              = ActivityClass.Gaming,
                WorkloadDescription       = "Gaming session",
                ExpectedCpuRange          = (40, 95),
                ExpectedRamRange          = (40, 90),
                ExpectedThermalRange      = (60, 92),
                HighCpuIsExpected         = true,
                HighDiskIoIsExpected      = false,
                ThermalElevationIsExpected = true,
                HasTransientStartupPhase  = true,
            },

            ActivityClass.Compiling      => new()
            {
                WorkloadType              = ActivityClass.Compiling,
                WorkloadDescription       = "Compiling code",
                ExpectedCpuRange          = (70, 100),
                ExpectedRamRange          = (40, 85),
                ExpectedThermalRange      = (65, 90),
                HighCpuIsExpected         = true,
                HighDiskIoIsExpected      = true,
                ThermalElevationIsExpected = true,
                HasTransientStartupPhase  = false,
            },

            ActivityClass.Rendering      => new()
            {
                WorkloadType              = ActivityClass.Rendering,
                WorkloadDescription       = "Rendering or encoding",
                ExpectedCpuRange          = (80, 100),
                ExpectedRamRange          = (50, 90),
                ExpectedThermalRange      = (70, 95),
                HighCpuIsExpected         = true,
                HighDiskIoIsExpected      = true,
                ThermalElevationIsExpected = true,
                HasTransientStartupPhase  = false,
            },

            ActivityClass.SystemUpdate   => new()
            {
                WorkloadType              = ActivityClass.SystemUpdate,
                WorkloadDescription       = "Windows Update running",
                ExpectedCpuRange          = (30, 85),
                ExpectedRamRange          = (30, 75),
                ExpectedThermalRange      = (45, 80),
                HighCpuIsExpected         = true,
                HighDiskIoIsExpected      = true,
                ThermalElevationIsExpected = false,
                HasTransientStartupPhase  = true,
            },

            ActivityClass.Backup         => new()
            {
                WorkloadType              = ActivityClass.Backup,
                WorkloadDescription       = "Backup operation",
                ExpectedCpuRange          = (20, 60),
                ExpectedRamRange          = (30, 70),
                ExpectedThermalRange      = (40, 75),
                HighCpuIsExpected         = false,
                HighDiskIoIsExpected      = true,
                ThermalElevationIsExpected = false,
                HasTransientStartupPhase  = false,
            },

            ActivityClass.VideoStreaming  => new()
            {
                WorkloadType              = ActivityClass.VideoStreaming,
                WorkloadDescription       = "Streaming video",
                ExpectedCpuRange          = (15, 50),
                ExpectedRamRange          = (30, 70),
                ExpectedThermalRange      = (40, 72),
                HighCpuIsExpected         = false,
                HighDiskIoIsExpected      = false,
                ThermalElevationIsExpected = false,
                HasTransientStartupPhase  = false,
            },

            ActivityClass.BrowsingHeavy  => new()
            {
                WorkloadType              = ActivityClass.BrowsingHeavy,
                WorkloadDescription       = "Heavy browser session",
                ExpectedCpuRange          = (20, 65),
                ExpectedRamRange          = (40, 85),
                ExpectedThermalRange      = (40, 75),
                HighCpuIsExpected         = false,
                HighDiskIoIsExpected      = false,
                ThermalElevationIsExpected = false,
                HasTransientStartupPhase  = false,
            },

            ActivityClass.Productivity   => new()
            {
                WorkloadType              = ActivityClass.Productivity,
                WorkloadDescription       = "Productivity workload",
                ExpectedCpuRange          = (10, 55),
                ExpectedRamRange          = (25, 75),
                ExpectedThermalRange      = (38, 70),
                HighCpuIsExpected         = false,
                HighDiskIoIsExpected      = false,
                ThermalElevationIsExpected = false,
                HasTransientStartupPhase  = false,
            },

            ActivityClass.Idle           => new()
            {
                WorkloadType              = ActivityClass.Idle,
                WorkloadDescription       = "System idle",
                ExpectedCpuRange          = (0, 20),
                ExpectedRamRange          = (15, 60),
                ExpectedThermalRange      = (30, 60),
                HighCpuIsExpected         = false,
                HighDiskIoIsExpected      = false,
                ThermalElevationIsExpected = false,
                HasTransientStartupPhase  = false,
            },

            _                            => new()
            {
                WorkloadType              = ActivityClass.Mixed,
                WorkloadDescription       = "General use",
                ExpectedCpuRange          = (5, 70),
                ExpectedRamRange          = (20, 80),
                ExpectedThermalRange      = (35, 80),
                HighCpuIsExpected         = false,
                HighDiskIoIsExpected      = false,
                ThermalElevationIsExpected = false,
                HasTransientStartupPhase  = false,
            },
        };

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the given CPU percent falls within the expected range
    /// for this workload (with an optional tolerance margin).
    /// </summary>
    public bool IsCpuWithinExpectedRange(double cpuPercent, double tolerance = 10.0) =>
        cpuPercent >= ExpectedCpuRange.Low  - tolerance &&
        cpuPercent <= ExpectedCpuRange.High + tolerance;

    /// <summary>Returns true if the given temperature is within expected range.</summary>
    public bool IsThermalWithinExpectedRange(double temperatureCelsius, double tolerance = 5.0) =>
        temperatureCelsius >= ExpectedThermalRange.Low  - tolerance &&
        temperatureCelsius <= ExpectedThermalRange.High + tolerance;
}
