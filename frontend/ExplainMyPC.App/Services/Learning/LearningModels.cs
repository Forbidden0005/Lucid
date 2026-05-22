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

// ── Part 15: Adaptive Personalization models ───────────────────────────────────

/// <summary>
/// Classifies how a user tends to interact with ExplainMyPC recommendations.
/// Derived deterministically from intervention history — never infers identity.
/// </summary>
public enum UserOperationalStyle
{
    /// <summary>Not enough history to classify (fewer than 3 interventions).</summary>
    Unknown = 0,

    /// <summary>Runs actions mainly when a warning is already active.</summary>
    Reactive = 1,

    /// <summary>Tends to act on recommendations before warnings escalate.</summary>
    Proactive = 2,

    /// <summary>Rarely accepts recommendations; prefers to monitor.</summary>
    HandsOff = 3,

    /// <summary>Frequently expands detail cards and reads explanations before acting.</summary>
    Investigative = 4,

    /// <summary>Mixed pattern — accepts roughly half of recommendations across all categories.</summary>
    Balanced = 5,
}

/// <summary>
/// How tolerant the user has been of repeated alert-style recommendations.
/// Used by AlertFatigueManager to calibrate suppression thresholds.
/// </summary>
public enum AlertTolerance
{
    /// <summary>User frequently dismisses or ignores repeated alerts.</summary>
    Low = 0,

    /// <summary>User engages with alerts at a normal rate.</summary>
    Medium = 1,

    /// <summary>User responds to most alerts even when repeated.</summary>
    High = 2,
}

/// <summary>
/// How comfortable the user is with automated or multi-step workflows.
/// </summary>
public enum AutomationComfort
{
    /// <summary>Prefers single, explicit actions. Declines multi-step workflows.</summary>
    Manual = 0,

    /// <summary>Accepts guided workflows with explanations at each step.</summary>
    Guided = 1,

    /// <summary>Comfortable with autonomous remediation workflows.</summary>
    Autonomous = 2,
}

/// <summary>
/// A single recorded user interaction with a recommendation.
/// Stored in intervention_memory.json and used to build the personalization profile.
/// </summary>
public sealed record InterventionRecord
{
    /// <summary>Unique ID for this record.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Dot-separated action identifier, e.g. "action.disk.clean-temp-files".</summary>
    public string ActionKey { get; init; } = string.Empty;

    /// <summary>Human-readable action title.</summary>
    public string ActionTitle { get; init; } = string.Empty;

    /// <summary>Broad category for the action (e.g. "disk", "startup", "process").</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>When the action was shown/offered to the user.</summary>
    public DateTimeOffset ShownAt { get; init; }

    /// <summary>True when the user executed the action; false when dismissed/ignored.</summary>
    public bool Accepted { get; init; }

    /// <summary>
    /// Severity of the associated insight when the action was shown.
    /// Stored as a string to avoid a cross-namespace dependency.
    /// Values: "Warning", "Recommendation", "Info".
    /// </summary>
    public string InsightSeverity { get; init; } = string.Empty;
}

/// <summary>
/// Aggregated personalization profile computed from InterventionRecord history.
/// Rebuilt in-memory on startup from the persisted record set.
/// Deterministic — no randomness, no hidden weights.
/// </summary>
public sealed record PersonalizationProfile
{
    /// <summary>Total intervention records analyzed.</summary>
    public int TotalRecords { get; init; }

    /// <summary>How many records were accepted vs shown.</summary>
    public int AcceptedCount { get; init; }

    /// <summary>Overall acceptance rate (0–1). Zero when no records.</summary>
    public double AcceptanceRate => TotalRecords > 0 ? (double)AcceptedCount / TotalRecords : 0.0;

    /// <summary>Acceptance rate per broad category key (e.g. "disk" → 0.8).</summary>
    public IReadOnlyDictionary<string, double> CategoryAcceptanceRates { get; init; } =
        new Dictionary<string, double>();

    /// <summary>
    /// Personalization weight multiplier per category (0.5–1.5).
    /// 1.0 = neutral (cold start); above 1.0 = boost; below 1.0 = suppress.
    /// </summary>
    public IReadOnlyDictionary<string, double> CategoryWeights { get; init; } =
        new Dictionary<string, double>();

    /// <summary>Classified user operational style.</summary>
    public UserOperationalStyle Style { get; init; }

    /// <summary>Alert tolerance derived from dismissal patterns.</summary>
    public AlertTolerance AlertTolerance { get; init; }

    /// <summary>Automation comfort derived from workflow acceptance patterns.</summary>
    public AutomationComfort AutomationComfort { get; init; }

    /// <summary>Whether there is enough data to trust the profile (≥ 3 records).</summary>
    public bool IsWarmEnough => TotalRecords >= 3;

    /// <summary>When this profile was last computed.</summary>
    public DateTimeOffset ComputedAt { get; init; }
}

/// <summary>
/// Human-readable report describing this machine's operational style.
/// Shown on the Dashboard personalization card.
/// </summary>
public sealed record OperationalStyleReport
{
    /// <summary>Classified style.</summary>
    public UserOperationalStyle Style { get; init; }

    /// <summary>Short label shown in the style badge (e.g. "Proactive User").</summary>
    public string StyleLabel { get; init; } = string.Empty;

    /// <summary>One or two sentence explanation of what the style means.</summary>
    public string StyleDescription { get; init; } = string.Empty;

    /// <summary>Glyph for the style badge (Segoe MDL2 / Fluent icons).</summary>
    public string StyleGlyph { get; init; } = string.Empty;

    /// <summary>Accent color hex for the style badge.</summary>
    public string StyleColor { get; init; } = "#6B7280";

    /// <summary>
    /// A concise personalization insight sentence about this user's pattern,
    /// e.g. "You tend to act on disk recommendations but rarely touch startup items."
    /// </summary>
    public string PersonalInsight { get; init; } = string.Empty;

    /// <summary>Number of interventions used to derive the style.</summary>
    public int SampleCount { get; init; }
}

/// <summary>
/// Breakdown of how a recommendation's priority score was computed.
/// All fields are in [0, 1] unless noted.
/// Exposed on PrioritizedAction so the UI can render transparent reasoning.
/// </summary>
public sealed record ScoreBreakdown
{
    /// <summary>Raw impact component: (int)Impact / 3.0.</summary>
    public double ImpactComponent { get; init; }

    /// <summary>Effort factor: 1 − (int)Effort / 4.0.</summary>
    public double EffortComponent { get; init; }

    /// <summary>Severity boost multiplier (0.80–1.20).</summary>
    public double SeverityBoost { get; init; }

    /// <summary>Machine-learning effectiveness weight (0–1).</summary>
    public double EffectivenessWeight { get; init; }

    /// <summary>
    /// Personalization multiplier derived from category acceptance rates (0.5–1.5).
    /// 1.0 when cold-start or personalization is disabled.
    /// </summary>
    public double PersonalizationMultiplier { get; init; }

    /// <summary>
    /// Alert fatigue suppression factor (0–1).
    /// 1.0 = no suppression; less than 1.0 = this action has been shown many times recently.
    /// </summary>
    public double FatigueFactor { get; init; }

    /// <summary>Final clamped composite score.</summary>
    public double FinalScore { get; init; }
}

/// <summary>
/// Human-readable explanation of why a recommendation was ranked at its current priority.
/// Shown in "Why Recommended" sections on Dashboard and Insights pages.
/// </summary>
public sealed record RecommendationExplanation
{
    /// <summary>The action key this explanation describes.</summary>
    public string ActionKey { get; init; } = string.Empty;

    /// <summary>Primary reason sentence (always present).</summary>
    public string PrimaryReason { get; init; } = string.Empty;

    /// <summary>
    /// Personalization reason sentence.
    /// Empty string when the profile is cold (< 3 interventions).
    /// </summary>
    public string PersonalizationReason { get; init; } = string.Empty;

    /// <summary>
    /// Effectiveness reason sentence.
    /// Empty string when the learning profile has insufficient data.
    /// </summary>
    public string EffectivenessReason { get; init; } = string.Empty;

    /// <summary>
    /// Fatigue note.
    /// Non-empty when the action's FatigueFactor is below 0.8 (shown many times recently).
    /// </summary>
    public string FatigueNote { get; init; } = string.Empty;

    /// <summary>
    /// Full multi-sentence explanation combining all non-empty reasons above.
    /// Ready to render directly in the UI.
    /// </summary>
    public string FullText { get; init; } = string.Empty;
}
