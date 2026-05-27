namespace Lucid.Services.Explain;

/// <summary>
/// Contract for the Lucid operational reasoning engine.
///
/// The engine synthesizes live telemetry, intelligence findings, timeline events,
/// and machine-baseline data into a single coherent <see cref="OperationalExplanation"/>
/// that drives the flagship Explain My PC page.
///
/// Design principles:
///   • Deterministic — same inputs always produce the same output.
///   • Local-only    — no LLM, no cloud API, no network calls.
///   • Confidence-aware — all findings express confidence percentages.
///   • Non-alarmist  — language is probabilistic and contextual, never absolute.
///
/// Threading:
///   <see cref="ExplanationUpdated"/> fires on the UI thread (inherited from the
///   upstream narrative engine). <see cref="CurrentExplanation"/> is safe to read
///   from the UI thread without additional synchronization.
/// </summary>
public interface IExplainMyPcEngine
{
    /// <summary>
    /// The most recently produced explanation, or <c>null</c> before the first
    /// intelligence cycle completes. Safe to read from the UI thread.
    /// </summary>
    OperationalExplanation? CurrentExplanation { get; }

    /// <summary>
    /// Raised on the UI thread whenever a new <see cref="OperationalExplanation"/>
    /// has been produced. Fires whenever the active insight set, narrative, or
    /// timeline changes materially.
    /// </summary>
    event EventHandler<OperationalExplanation>? ExplanationUpdated;

    /// <summary>
    /// Subscribes to upstream services and begins producing explanations.
    /// Must be called after the narrative engine and timeline service have started.
    /// If a current narrative already exists, fires <see cref="ExplanationUpdated"/>
    /// synchronously from this call to seed the UI.
    /// </summary>
    void Start();

    /// <summary>Unsubscribes from all upstream services.</summary>
    void Stop();
}
