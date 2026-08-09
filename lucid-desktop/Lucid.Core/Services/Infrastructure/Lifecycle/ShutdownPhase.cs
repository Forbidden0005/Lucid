namespace Lucid.Services.Infrastructure.Lifecycle;

/// <summary>
/// Defines ordered shutdown phases for the Lucid platform.
///
/// Services are shut down in ascending phase order (Presentation first,
/// Infrastructure last) — the reverse of <see cref="StartupPhase"/>.
///
/// This ensures that user-facing services stop accepting new work before
/// the underlying infrastructure they depend on is torn down.
/// </summary>
public enum ShutdownPhase
{
    /// <summary>
    /// User-facing services stop first: companion, conversation, UI services.
    /// </summary>
    Presentation = 0,

    /// <summary>Application services: execution engine, governance, watchtower.</summary>
    Application = 1,

    /// <summary>Operational services: session, history, timeline, replay.</summary>
    Operational = 2,

    /// <summary>Intelligence and analysis pipelines.</summary>
    Intelligence = 3,

    /// <summary>Telemetry samplers — stop producing data.</summary>
    Telemetry = 4,

    /// <summary>Persistence layers — flush queues and close connections.</summary>
    Persistence = 5,

    /// <summary>
    /// Core infrastructure last: event bus drained, logger flushed, settings saved.
    /// </summary>
    Infrastructure = 6,
}
