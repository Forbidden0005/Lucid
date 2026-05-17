namespace ExplainMyPC.Services.Narrative;

/// <summary>
/// Produces human-readable, multi-paragraph system narratives from the active
/// set of intelligence findings.
///
/// The narrative synthesizes active insights, trend findings, process attributions,
/// forecast projections, and baseline anomalies into coherent prose — moving beyond
/// individual insight cards toward holistic system understanding.
///
/// Threading:
///   All events fire on the UI thread (the intelligence engine fires InsightsUpdated
///   on the UI thread; the narrative engine inherits this contract).
///   <see cref="CurrentNarrative"/> is safe to read on the UI thread without locking.
///
/// Determinism:
///   All output is produced by template-driven synthesis rules.
///   No LLM, no cloud APIs, no randomness. The same input always produces
///   the same output.
///
/// Lifecycle:
///   Call <see cref="Start"/> before the first insight engine reading arrives.
///   Call <see cref="Stop"/> from AppServices.Shutdown().
/// </summary>
public interface IOperationalNarrativeEngine
{
    /// <summary>
    /// The most recently generated narrative, or null before the first
    /// <see cref="NarrativeUpdated"/> fires. Safe to read on the UI thread.
    /// </summary>
    OperationalNarrative? CurrentNarrative { get; }

    /// <summary>
    /// Raised on the UI thread whenever the active insight set changes and a
    /// new narrative has been generated. Fires at the same cadence as
    /// ISystemInsightEngine.InsightsUpdated — only on actual change.
    /// </summary>
    event EventHandler<OperationalNarrative>? NarrativeUpdated;

    /// <summary>Subscribes to the intelligence engine. Call once from AppServices.</summary>
    void Start();

    /// <summary>Unsubscribes from the intelligence engine. Call from AppServices.Shutdown().</summary>
    void Stop();
}
