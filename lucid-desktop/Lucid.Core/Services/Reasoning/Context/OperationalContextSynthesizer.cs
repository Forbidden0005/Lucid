using Lucid.Services.Infrastructure.OperationalState;

namespace Lucid.Services.Reasoning.Context;

/// <summary>
/// Production implementation of <see cref="IOperationalContextSynthesizer"/>.
///
/// Synthesis algorithm:
///   1. Read latest telemetry readings from the operational state coordinator.
///   2. Collect running process names from the process intelligence layer.
///   3. Call <see cref="ActivityInterpretationEngine.Classify"/> to get activity class.
///   4. Build expected resource ranges for the classified activity.
///   5. Determine suppression flags (high CPU expected, thermal expected, etc.).
///   6. Publish new profile atomically.
///
/// The synthesizer is stateless between cycles — each call produces a fresh
/// profile from the current signal set.
///
/// Thread-safe: all public methods are safe for concurrent callers.
/// </summary>
public sealed class OperationalContextSynthesizer : IOperationalContextSynthesizer
{
    private readonly IOperationalStateCoordinator _operationalState;

    // Latest profile stored atomically — lock-free read.
    private volatile ContextProfile? _current;

    public OperationalContextSynthesizer(
        IOperationalStateCoordinator operationalState)
    {
        _operationalState = operationalState;
    }

    // ── IOperationalContextSynthesizer ────────────────────────────────────────

    /// <inheritdoc />
    public ContextProfile? Current => _current;

    /// <inheritdoc />
    public Task<ContextProfile> SynthesizeAsync(CancellationToken cancellationToken = default)
    {
        // Context synthesis is fast (< 5 ms) and synchronous internally.
        // Wrapping in Task.Run would be wasteful; return completed task.
        var profile = BuildProfile();
        _current = profile;
        return Task.FromResult(profile);
    }

    // ── Profile construction ──────────────────────────────────────────────────

    private ContextProfile BuildProfile()
    {
        var state = _operationalState.Current;

        // Derive telemetry readings from operational state.
        var cpu    = state.CpuPercent;
        var ram    = state.RamPercent;
        var uptime = GetUptimeSeconds();

        // GPU and disk throughput: use 0 as safe defaults when unavailable.
        // Future: inject ITelemetryService for richer readings.
        const double gpu  = 0;
        const double disk = 0;

        // Windows Update detection: check for high disk pressure + low CPU workload heuristic.
        // Full Windows Update detection uses the process list (future: ProcessIntelligenceService).
        bool windowsUpdateActive =
            state.HasCondition(RuntimeCondition.LowDiskSpace) && disk > 50;

        // Classify activity — returns process list from known running processes.
        // Note: full process scanning happens in ProcessIntelligenceService.
        // The synthesizer uses a minimal set of well-known process indicators
        // available without a full process enumeration on each cycle.
        var (activity, confidence, driverProcesses) =
            ActivityInterpretationEngine.Classify(
                cpu, gpu, disk, ram, uptime,
                GetIndicatorProcesses(state),
                windowsUpdateActive);

        var cpuRange     = ActivityInterpretationEngine.ExpectedCpuRange(activity);
        var thermalRange = ActivityInterpretationEngine.ExpectedThermalRange(activity);

        bool isPostBoot = uptime < 300;

        return new ContextProfile
        {
            PrimaryActivity              = activity,
            ClassificationConfidence     = confidence,
            WorkloadLabel                = BuildWorkloadLabel(activity, confidence),
            ExpectedCpuRange             = cpuRange,
            ExpectedRamRange             = (10, 80),  // RAM range is activity-independent at this tier
            ExpectedThermalRange         = thermalRange,
            HighCpuIsExpected            = activity is ActivityClass.Gaming or ActivityClass.Compiling
                                                    or ActivityClass.Rendering or ActivityClass.SystemUpdate,
            HighDiskIoIsExpected         = activity is ActivityClass.SystemUpdate or ActivityClass.Backup,
            ThermalElevationIsExpected   = activity is ActivityClass.Gaming or ActivityClass.Rendering
                                                    or ActivityClass.Compiling,
            IsPostBootTransient          = isPostBoot,
            DriverProcessNames           = driverProcesses,
            WindowsUpdateActive          = windowsUpdateActive,
        };
    }

    private static long GetUptimeSeconds()
    {
        try { return (long)TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds; }
        catch { return 0; }
    }

    private static IEnumerable<string> GetIndicatorProcesses(OperationalStateSnapshot state)
    {
        // Without direct process list access, return empty set.
        // CognitiveReasoningEngine will enrich this via ProcessIntelligenceService
        // in future cycles. For now context synthesis uses telemetry signals only.
        _ = state;
        return [];
    }

    private static string BuildWorkloadLabel(ActivityClass activity, double confidence) =>
        activity switch
        {
            ActivityClass.Gaming         => "Gaming session",
            ActivityClass.Compiling      => "Compiling code",
            ActivityClass.Rendering      => "Rendering or encoding",
            ActivityClass.SystemUpdate   => "Windows Update running",
            ActivityClass.Backup         => "Backup operation",
            ActivityClass.VideoStreaming => "Streaming video",
            ActivityClass.BrowsingHeavy  => "Heavy browser session",
            ActivityClass.Productivity   => "Productivity workload",
            ActivityClass.Idle           => "System idle",
            ActivityClass.Mixed          => confidence > 0.5 ? "Mixed workload" : "General use",
            _                            => "Workload establishing...",
        };
}

