namespace ExplainMyPC.Services.Learning;

// ── Outcome classification ─────────────────────────────────────────────────────

/// <summary>
/// The net outcome classification for a single remediation action execution,
/// derived by comparing system state 5 minutes before vs 5 minutes after.
/// </summary>
public enum OutcomeClassification
{
    /// <summary>
    /// Conditions clearly improved: significant metric reduction and/or insights resolved.
    /// Confidence in improvement is high.
    /// </summary>
    Improved,

    /// <summary>
    /// Some positive signal — partial metric improvement or a subset of insights resolved —
    /// but the effect was not decisive enough to classify as Improved.
    /// </summary>
    PartiallyImproved,

    /// <summary>
    /// No meaningful change detected in the 5-minute windows on either side.
    /// </summary>
    Unchanged,

    /// <summary>
    /// System conditions measurably worsened in the window following the action.
    /// This does not imply the action caused the worsening — correlation only.
    /// </summary>
    Worsened,

    /// <summary>
    /// Insufficient telemetry data exists around the action time to produce a reliable
    /// before/after comparison.  Actions outside the 30-minute rolling window always
    /// receive this classification.
    /// </summary>
    InsufficientData,
}

// ── Per-execution outcome record ──────────────────────────────────────────────

/// <summary>
/// Persisted record of the measured outcome for one remediation action execution.
/// Produced by <see cref="EffectivenessAnalyzer"/> and stored in the learning
/// outcome log at %LOCALAPPDATA%\ExplainMyPC\Learning\outcome-records.json.
///
/// Each record is keyed by <see cref="OperationId"/> (the corresponding
/// <see cref="History.OperationRecord.Id"/>), which prevents duplicate analysis.
/// </summary>
public sealed record ActionOutcomeRecord
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>Matches the <see cref="History.OperationRecord.Id"/> that was analyzed.</summary>
    public string OperationId { get; init; } = string.Empty;

    /// <summary>Dot-separated action identifier, e.g. "action.disk.clean-temp-files".</summary>
    public string ActionKey { get; init; } = string.Empty;

    /// <summary>Human-readable action title for display.</summary>
    public string ActionTitle { get; init; } = string.Empty;

    // ── Timing ────────────────────────────────────────────────────────────────

    /// <summary>When the original action was executed.</summary>
    public DateTimeOffset ExecutedAt { get; init; }

    /// <summary>When this outcome record was produced by the analyzer.</summary>
    public DateTimeOffset AnalyzedAt { get; init; }

    // ── Classification ────────────────────────────────────────────────────────

    /// <summary>Net outcome determined by comparing state before and after the action.</summary>
    public OutcomeClassification Outcome { get; init; }

    /// <summary>
    /// Confidence in the outcome classification (0–100).
    /// Driven by telemetry sample density around the action time.
    /// Zero when Outcome is InsufficientData.
    /// </summary>
    public int ConfidencePercent { get; init; }

    // ── Before / after metrics snapshot ──────────────────────────────────────

    /// <summary>CPU utilisation (%) in the 5-minute window before the action.</summary>
    public double CpuBefore { get; init; }

    /// <summary>CPU utilisation (%) in the 5-minute window after the action.</summary>
    public double CpuAfter { get; init; }

    /// <summary>RAM utilisation (%) before.</summary>
    public double RamBefore { get; init; }

    /// <summary>RAM utilisation (%) after.</summary>
    public double RamAfter { get; init; }

    /// <summary>Disk utilisation (%) before.</summary>
    public double DiskBefore { get; init; }

    /// <summary>Disk utilisation (%) after.</summary>
    public double DiskAfter { get; init; }

    // ── Insight counts ────────────────────────────────────────────────────────

    /// <summary>Number of active insights in the 5-minute window before the action.</summary>
    public int InsightsActiveBefore { get; init; }

    /// <summary>Number of active insights in the 5-minute window after the action.</summary>
    public int InsightsActiveAfter { get; init; }

    /// <summary>Insights that resolved in the window following the action.</summary>
    public int InsightsResolved { get; init; }

    // ── Stabilization ─────────────────────────────────────────────────────────

    /// <summary>
    /// How many minutes conditions remained improved after the action before
    /// reverting (if applicable).  Zero when insufficient data or no improvement.
    /// </summary>
    public int StabilizationMinutes { get; init; }

    // ── Narrative ─────────────────────────────────────────────────────────────

    /// <summary>
    /// One-sentence human-readable summary of the measured outcome.
    /// Examples:
    ///   "CPU dropped from 82% to 34% and one active insight resolved."
    ///   "No significant change detected in the 5-minute window."
    ///   "Insufficient telemetry data around the action time."
    /// </summary>
    public string NarrativeSummary { get; init; } = string.Empty;
}

// ── Aggregated effectiveness profile ──────────────────────────────────────────

/// <summary>
/// Aggregated effectiveness profile for a specific action type, derived by
/// <see cref="RecommendationLearningEngine"/> from all <see cref="ActionOutcomeRecord"/>
/// instances that share the same <see cref="ActionKey"/>.
///
/// Profiles are recalculated in-memory after each analysis pass.
/// They are not persisted separately — they are always rehydrated from the
/// outcome records on startup.
/// </summary>
public sealed record RecommendationEffectivenessProfile
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>Dot-separated action identifier.</summary>
    public string ActionKey { get; init; } = string.Empty;

    /// <summary>Human-readable action title.</summary>
    public string ActionTitle { get; init; } = string.Empty;

    // ── Outcome counts ────────────────────────────────────────────────────────

    /// <summary>Total number of outcome records for this action key.</summary>
    public int TotalExecutions { get; init; }

    /// <summary>Count of Improved outcomes.</summary>
    public int ImprovedCount { get; init; }

    /// <summary>Count of PartiallyImproved outcomes.</summary>
    public int PartiallyImprovedCount { get; init; }

    /// <summary>Count of Unchanged outcomes.</summary>
    public int UnchangedCount { get; init; }

    /// <summary>Count of Worsened outcomes.</summary>
    public int WorsenedCount { get; init; }

    /// <summary>Count of InsufficientData outcomes (excluded from effectiveness rate).</summary>
    public int InsufficientDataCount { get; init; }

    // ── Derived metrics ───────────────────────────────────────────────────────

    /// <summary>Number of executions with sufficient data for analysis.</summary>
    public int TotalAnalyzed => TotalExecutions - InsufficientDataCount;

    /// <summary>
    /// Weighted effectiveness rate in [0, 1]: Improved=1.0, PartiallyImproved=0.5,
    /// others=0.0.  Zero when TotalAnalyzed &lt; 2 (cold-start guard).
    /// </summary>
    public double EffectivenessRate { get; init; }

    /// <summary>
    /// Short label for UI badges:
    ///   "Historically effective on this machine"
    ///   "Mixed results on this machine"
    ///   "Low effectiveness on this machine"
    ///   "Limited historical data"
    /// </summary>
    public string EffectivenessLabel { get; init; } = string.Empty;

    /// <summary>
    /// Longer descriptive sentence for tooltips and detail views.
    /// Example: "Helped reduce CPU pressure ~18% across 3 of 4 tries."
    /// </summary>
    public string SummaryText { get; init; } = string.Empty;

    /// <summary>True when TotalAnalyzed &gt;= 2 (enough data to trust the label).</summary>
    public bool IsWarmEnough { get; init; }

    // ── Average metric deltas (for the Improved + PartiallyImproved records) ──

    /// <summary>Average CPU change (negative = reduction = improvement).</summary>
    public double AvgCpuChange { get; init; }

    /// <summary>Average RAM change.</summary>
    public double AvgRamChange { get; init; }

    /// <summary>Average Disk I/O change.</summary>
    public double AvgDiskChange { get; init; }

    // ── Timestamp ─────────────────────────────────────────────────────────────

    /// <summary>When the most recent outcome record for this action was analyzed.</summary>
    public DateTimeOffset LastAnalyzedAt { get; init; }
}
