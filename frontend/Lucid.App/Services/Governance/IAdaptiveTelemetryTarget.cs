namespace Lucid.Services.Governance;

/// <summary>
/// Implemented by services that expose a mutable polling interval.
///
/// The <see cref="PollingCoordinator"/> pushes new timing values here
/// whenever the <see cref="RuntimeMode"/> changes.  Each implementing service
/// applies the change on its next loop iteration — there is no hard
/// interruption of an in-progress sleep.
///
/// Contract:
///   • Both methods are safe to call from any thread.
///   • Implementations must not throw — swallow exceptions internally if needed.
///   • A value of zero means "pause" — the service should stop polling until
///     a non-zero interval is applied.
/// </summary>
public interface IAdaptiveTelemetryTarget
{
    /// <summary>
    /// Sets the target telemetry sampling interval.
    /// Applied on the next poll cycle — the current sleep is not interrupted.
    /// </summary>
    void SetTelemetryInterval(TimeSpan interval);

    /// <summary>
    /// Sets the target process list sampling interval.
    /// Applied on the next tick where the process sampler would fire.
    /// </summary>
    void SetProcessSampleInterval(TimeSpan interval);
}
