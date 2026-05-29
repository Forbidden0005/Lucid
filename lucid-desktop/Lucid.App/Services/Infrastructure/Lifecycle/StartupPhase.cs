namespace Lucid.Services.Infrastructure.Lifecycle;

/// <summary>
/// Defines ordered startup phases for the Lucid platform.
///
/// Services registered with <see cref="LifecycleCoordinator"/> are initialized
/// in ascending phase order. Services within the same phase are initialized
/// sequentially in registration order.
///
/// Dependency rule: a service in phase N may safely depend on any service
/// that completed initialization in phase N-1 or earlier.
/// </summary>
public enum StartupPhase
{
    /// <summary>
    /// Core infrastructure: logging, settings, database schema, event bus.
    /// No business logic — only platform foundations.
    /// </summary>
    Infrastructure = 0,

    /// <summary>
    /// Data and persistence layers: repositories, history buffers, caches.
    /// May depend on Infrastructure.
    /// </summary>
    Persistence = 1,

    /// <summary>
    /// Telemetry and sampling: CPU/RAM/Disk/GPU/Thermal samplers, baseline.
    /// May depend on Persistence.
    /// </summary>
    Telemetry = 2,

    /// <summary>
    /// Intelligence and analysis: insight engine, synthesis, forecasting,
    /// anomaly detection, narrative engine.
    /// May depend on Telemetry.
    /// </summary>
    Intelligence = 3,

    /// <summary>
    /// Operational services: session management, history, timeline, replay.
    /// May depend on Intelligence.
    /// </summary>
    Operational = 4,

    /// <summary>
    /// Application services: execution engine, governance, diagnostics,
    /// watchtower, simulation.
    /// May depend on Operational.
    /// </summary>
    Application = 5,

    /// <summary>
    /// User-facing services: companion, conversation engine, UI services.
    /// May depend on Application.
    /// </summary>
    Presentation = 6,
}
