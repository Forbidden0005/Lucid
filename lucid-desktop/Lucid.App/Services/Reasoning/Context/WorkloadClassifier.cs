namespace Lucid.Services.Reasoning.Context;

/// <summary>
/// Stateless workload classifier that maps a set of telemetry readings and
/// process indicators to an <see cref="ActivityClass"/> and <see cref="RuntimeBehaviorModel"/>.
///
/// Wraps <see cref="ActivityInterpretationEngine.Classify"/> with a richer
/// typed result that the cognitive reasoning engine and diagnostics layer can
/// inspect directly.
///
/// All classifications are:
///   - Deterministic (same inputs → same output)
///   - Confidence-qualified (never asserted as certainty below the threshold)
///   - Explainable (driver rationale is always included)
/// </summary>
public static class WorkloadClassifier
{
    private const double HighConfidenceThreshold  = 0.70;
    private const double MinimumReportedConfidence = 0.30;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Classifies the current workload from telemetry readings.
    /// Returns an <see cref="WorkloadClassificationResult"/> with the activity type,
    /// confidence, and a corresponding <see cref="RuntimeBehaviorModel"/>.
    /// </summary>
    /// <param name="cpuPercent">Current CPU utilization 0–100.</param>
    /// <param name="gpuPercent">Current GPU utilization 0–100 (0 = unavailable).</param>
    /// <param name="diskPercent">Current disk throughput as % of rated speed (0 = unavailable).</param>
    /// <param name="ramPercent">Current RAM utilization 0–100.</param>
    /// <param name="uptimeSeconds">System uptime in seconds.</param>
    /// <param name="indicatorProcesses">Known-active process names (optional).</param>
    /// <param name="windowsUpdateActive">True if Windows Update is detected running.</param>
    public static WorkloadClassificationResult Classify(
        double             cpuPercent,
        double             gpuPercent           = 0,
        double             diskPercent          = 0,
        double             ramPercent           = 0,
        long               uptimeSeconds        = 0,
        IEnumerable<string>? indicatorProcesses = null,
        bool               windowsUpdateActive  = false)
    {
        var processes = indicatorProcesses?.ToList() ?? [];

        var (activity, confidence, driverProcesses) =
            ActivityInterpretationEngine.Classify(
                cpuPercent, gpuPercent, diskPercent, ramPercent,
                uptimeSeconds, processes, windowsUpdateActive);

        // Floor confidence at minimum to avoid noise
        confidence = Math.Max(confidence, MinimumReportedConfidence);

        var model = RuntimeBehaviorModel.For(activity);

        return new WorkloadClassificationResult(
            Activity:         activity,
            Confidence:       confidence,
            IsHighConfidence: confidence >= HighConfidenceThreshold,
            BehaviorModel:    model,
            DriverProcesses:  [.. driverProcesses],
            ClassifiedAtUtc:  DateTime.UtcNow);
    }
}

/// <summary>
/// Result of a workload classification cycle.
/// Immutable — produced by <see cref="WorkloadClassifier.Classify"/>.
/// </summary>
public sealed record WorkloadClassificationResult(
    ActivityClass              Activity,
    double                     Confidence,
    bool                       IsHighConfidence,
    RuntimeBehaviorModel       BehaviorModel,
    IReadOnlyList<string>      DriverProcesses,
    DateTime                   ClassifiedAtUtc)
{
    /// <summary>Human-readable workload description for UI and LLM context injection.</summary>
    public string Label => BehaviorModel.WorkloadDescription;

    /// <summary>True when there is enough confidence to use suppression rules.</summary>
    public bool CanSuppressAlerts => IsHighConfidence && Activity != ActivityClass.Unknown;
}
