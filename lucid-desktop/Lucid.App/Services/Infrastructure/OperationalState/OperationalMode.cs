namespace Lucid.Services.Infrastructure.OperationalState;

/// <summary>
/// High-level operational mode of the Lucid platform.
///
/// The mode reflects the overall posture of the platform and influences
/// how aggressive intelligence, governance, and automation systems behave.
/// </summary>
public enum OperationalMode
{
    /// <summary>
    /// Normal operation: all subsystems active, standard thresholds.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Platform is actively analyzing the system at user request.
    /// Foreground analysis takes priority; background services yield.
    /// </summary>
    ActiveAnalysis = 1,

    /// <summary>
    /// One or more subsystems have reported degradation.
    /// Non-critical background work is suppressed; health recovery is prioritized.
    /// </summary>
    Degraded = 2,

    /// <summary>
    /// An automated remediation or workflow is executing.
    /// Governance is heightened; new user requests are queued.
    /// </summary>
    WorkflowExecution = 3,

    /// <summary>
    /// Simulation mode: the platform is running a "what if" scenario.
    /// No real system changes are made.
    /// </summary>
    Simulation = 4,

    /// <summary>
    /// The platform is in the process of shutting down.
    /// All non-critical work is cancelled.
    /// </summary>
    Shutdown = 5,
}
