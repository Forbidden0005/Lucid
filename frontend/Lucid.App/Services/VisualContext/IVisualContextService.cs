namespace Lucid.Services.VisualContext;

/// <summary>
/// Provides consent-gated semantic understanding of the visible desktop.
///
/// ALL analysis is:
///   • explicit — only runs when the user or companion explicitly asks
///   • consented — every mode (except GuideOnly) shows a consent card
///   • bounded  — results auto-expire; nothing persists without user action
///   • local    — no data leaves the machine
///
/// Threading: events are raised on the UI thread.
/// </summary>
public interface IVisualContextService
{
    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the UI thread when the semantic window context changes
    /// (i.e. the active window switches to a meaningfully different type).
    /// Only fires during an active GuideOnly session; never fires passively.
    /// </summary>
    event EventHandler<VisualContextChangedEventArgs>? ContextChanged;

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The most recent SemanticWindowContext produced by an analysis pass.
    /// Null until the first analysis completes.
    /// </summary>
    SemanticWindowContext? LastKnownContext { get; }

    /// <summary>True while an analysis pass is in progress.</summary>
    bool IsAnalyzing { get; }

    // ── Core operations ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs a full consent-gated analysis of the active foreground window.
    /// Shows a consent card and waits for the user to approve or deny.
    /// Returns ScreenAnalysisResult.Denied if the user declines.
    /// </summary>
    Task<ScreenAnalysisResult> AnalyzeCurrentWindowAsync(
        string            reason,
        string            requestedBy  = "user",
        CancellationToken ct           = default);

    /// <summary>
    /// Runs a lightweight GuideOnly analysis (UIA/window-title only, no capture).
    /// Requires a lighter consent prompt because no pixels are read.
    /// </summary>
    Task<ScreenAnalysisResult> GuideAnalysisAsync(
        string            reason,
        string            requestedBy  = "companion",
        CancellationToken ct           = default);

    /// <summary>
    /// Locates operational UI elements in the active window matching the query.
    /// Returns empty list if no match or if not consented.
    /// </summary>
    Task<IReadOnlyList<VisualWorkflowTarget>> LocateElementsAsync(
        string            query,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a plain-English description of what the user is currently
    /// looking at, based on window title and process name only (no capture).
    /// Always safe to call — no consent required.
    /// </summary>
    string DescribeActiveWindow();

}
