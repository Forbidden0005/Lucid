namespace ExplainMyPC.Services.Baseline;

/// <summary>
/// Contract for the adaptive machine-specific behavioral baseline service.
///
/// The service observes every telemetry snapshot and builds statistical models
/// of normal system behaviour for this specific PC. These models feed
/// baseline-aware insight rules that can detect anomalies a fixed-threshold
/// rule would miss — e.g. a machine that normally idles at 3 % CPU is
/// behaving unusually at 20 %, even though 20 % is well below the global
/// "SustainedHighCpu" threshold of 80 %.
///
/// Learning strategy:
///   Welford's online algorithm provides numerically stable mean and variance
///   in O(1) per observation with constant memory. No ML frameworks, no
///   external services. All reasoning is deterministic and local.
///
/// Warm-up period:
///   <see cref="CurrentBaseline"/> returns null until at least
///   <see cref="WelfordStats.MinAnomalyCount"/> observations have been
///   collected (≈ 5 minutes). Rules must guard against null.
///
/// Persistence:
///   Baseline data is saved to %LOCALAPPDATA%\ExplainMyPC\baseline.json
///   approximately every 5 minutes. Data persists across app restarts so
///   the model improves over days, not just within a single session.
///
/// Lifetime:
///   Start() once from AppServices.Initialize().
///   Stop() from AppServices.Shutdown().
/// </summary>
public interface ISystemBaselineService
{
    /// <summary>
    /// The current learned baseline for this machine, or <c>null</c> while
    /// still in the warm-up period (fewer than 200 observations collected).
    ///
    /// Always read this into a local variable at the start of a rule evaluation
    /// — it is rebuilt on every telemetry tick and the reference may change.
    /// </summary>
    MachineBaseline? CurrentBaseline { get; }

    /// <summary>
    /// Subscribes to the telemetry service and begins accumulating observations.
    /// Must be called after the telemetry service has been started.
    /// </summary>
    void Start();

    /// <summary>
    /// Unsubscribes from the telemetry service.
    /// Accumulated baseline data is preserved in memory and on disk.
    /// </summary>
    void Stop();
}
