namespace ExplainMyPC.Services.Intelligence;

/// <summary>
/// How urgent an insight is. Values are ordered so higher integers are
/// higher priority — the engine sorts findings descending by severity.
/// </summary>
public enum InsightSeverity
{
    /// <summary>Informational — everything is fine, or just noteworthy.</summary>
    Info           = 0,

    /// <summary>
    /// An actionable improvement the user can make. Less urgent than a
    /// warning but has a clear next step.
    /// </summary>
    Recommendation = 1,

    /// <summary>Something needs attention — a metric is outside healthy range.</summary>
    Warning        = 2
}

/// <summary>
/// A single intelligence finding produced by <see cref="IInsightRule"/>.
///
/// Findings are immutable value objects. The engine replaces an existing
/// finding each tick (keyed by <see cref="Id"/>) rather than mutating it,
/// so consumers always see a consistent snapshot.
/// </summary>
/// <param name="Id">
///     Stable dot-separated identifier, e.g. "cpu.sustained-high".
///     Used as a deduplication key — only one finding per Id is active at
///     a time. The engine stores and replaces findings by this key.
/// </param>
/// <param name="Severity">How critical the finding is.</param>
/// <param name="Title">Short one-line label suitable for a card header.</param>
/// <param name="Detail">
///     2–4 sentence human-readable explanation including current metric values.
///     Refreshed on every evaluation tick so it always reflects latest data.
/// </param>
/// <param name="ActionHint">
///     Optional: a concise suggestion for what the user can do.
///     Null for purely informational findings.
/// </param>
/// <param name="DetectedAt">When the engine last evaluated this condition.</param>
public sealed record SystemInsight(
    string          Id,
    InsightSeverity Severity,
    string          Title,
    string          Detail,
    string?         ActionHint,
    DateTimeOffset  DetectedAt,
    /// <summary>
    /// How confident the rule is in this finding, expressed as a percentage.
    /// Rules with direct sensor readings (disk space, temperature) report 100.
    /// History-based rules scale from ~70 % at MinSamples to ~98 % at a full window.
    /// </summary>
    int             ConfidencePercent = 100);
