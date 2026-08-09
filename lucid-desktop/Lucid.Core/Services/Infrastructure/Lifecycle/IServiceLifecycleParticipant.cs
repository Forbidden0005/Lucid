namespace Lucid.Services.Infrastructure.Lifecycle;

/// <summary>
/// Implemented by services that participate in the coordinated Lucid
/// platform lifecycle managed by <see cref="LifecycleCoordinator"/>.
///
/// Services register themselves during <see cref="LifecycleCoordinator"/>
/// construction. The coordinator calls <see cref="InitializeAsync"/> in
/// <see cref="StartupPhase"/> order, and <see cref="ShutdownAsync"/> in
/// <see cref="ShutdownPhase"/> order (reverse of startup).
///
/// Contrast with <see cref="Lucid.Core.Contracts.IInitializable"/> — that
/// interface is the minimal marker; this interface adds phase ordering,
/// naming, and health reporting for coordinator-managed services.
/// </summary>
public interface IServiceLifecycleParticipant
{
    /// <summary>
    /// Logical name of this service, used in health reports and log output.
    /// Should be unique across all registered participants.
    /// Examples: "TelemetryService", "InsightEngine", "SessionContext"
    /// </summary>
    string ServiceName { get; }

    /// <summary>The startup phase this service belongs to.</summary>
    StartupPhase StartupPhase { get; }

    /// <summary>The shutdown phase this service belongs to.</summary>
    ShutdownPhase ShutdownPhase { get; }

    /// <summary>
    /// Called by <see cref="LifecycleCoordinator"/> during platform startup.
    /// Must complete before services in the next <see cref="StartupPhase"/> begin.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by <see cref="LifecycleCoordinator"/> during platform shutdown.
    /// Must flush in-flight work and release resources.
    /// </summary>
    Task ShutdownAsync();

    /// <summary>
    /// Returns the current health state of this service.
    /// Called periodically by <see cref="ServiceHealthRegistry"/> to maintain
    /// the platform-wide health picture.
    /// </summary>
    ServiceHealthState GetHealthState();
}
