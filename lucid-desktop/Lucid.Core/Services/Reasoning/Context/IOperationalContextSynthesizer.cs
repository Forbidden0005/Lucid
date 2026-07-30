namespace Lucid.Services.Reasoning.Context;

/// <summary>
/// Synthesizes a <see cref="ContextProfile"/> from behavioral signals,
/// telemetry, and process information to establish what the system is
/// currently doing and how resource readings should be interpreted.
///
/// This is the bridge between raw telemetry ("CPU = 87%") and contextual
/// intelligence ("87% CPU is expected — the user is compiling code").
///
/// Thread-safe: all methods are safe for concurrent callers.
/// </summary>
public interface IOperationalContextSynthesizer
{
    /// <summary>
    /// The most recently synthesized context profile.
    /// Null before the first synthesis cycle completes.
    /// </summary>
    ContextProfile? Current { get; }

    /// <summary>
    /// Synthesizes a context profile from the latest available signals.
    ///
    /// This method reads current telemetry, process list, session state, and
    /// workload behavioral data and produces an updated <see cref="ContextProfile"/>.
    ///
    /// Typically called at the start of each cognitive reasoning cycle.
    /// Designed to complete in < 10 ms on the happy path.
    /// </summary>
    Task<ContextProfile> SynthesizeAsync(CancellationToken cancellationToken = default);
}
