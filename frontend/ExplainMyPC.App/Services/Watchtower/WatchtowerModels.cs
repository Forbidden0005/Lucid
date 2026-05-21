namespace ExplainMyPC.Services.Watchtower;

// ── Alert severity ──────────────────────────────────────────────────────────────

/// <summary>
/// Severity level for a watchtower alert.
/// Uses probability-aware, non-alarming language throughout.
/// </summary>
public enum WatchtowerAlertSeverity
{
    /// <summary>Informational signal — no action needed, just awareness.</summary>
    Info     = 0,

    /// <summary>A pattern worth monitoring — check in if it continues.</summary>
    Advisory = 1,

    /// <summary>A trend that warrants attention and possible action.</summary>
    Warning  = 2,

    /// <summary>A pattern that strongly suggests review and likely intervention.</summary>
    Elevated = 3,
}

// ── Degradation signal type ─────────────────────────────────────────────────────

/// <summary>
/// The type of degradation signal that triggered a watchtower alert.
/// </summary>
public enum DegradationSignalType
{
    /// <summary>7-day health score is declining relative to the 30-day baseline.</summary>
    HealthScoreDecline       = 0,

    /// <summary>A recurring insight pattern is becoming more frequent.</summary>
    EscalatingPattern        = 1,

    /// <summary>A metric trend is consistently worsening over the look-back window.</summary>
    WorseningMetricTrend     = 2,

    /// <summary>The overall system health score has dropped below a threshold.</summary>
    LowOverallHealth         = 3,

    /// <summary>The same insight has appeared many times without resolution.</summary>
    HighFrequencyAnomaly     = 4,

    /// <summary>A metric is trending toward a problematic range at current rate.</summary>
    ProjectedDegradation     = 5,
}

// ── Drift direction ─────────────────────────────────────────────────────────────

/// <summary>
/// The direction of operational drift for a single metric.
/// </summary>
public enum DriftDirection
{
    Stable    = 0,
    Improving = 1,
    Drifting  = 2,   // Worsening but not yet alarming
    Elevated  = 3,   // Significant worsening
}

// ── Maintenance priority ────────────────────────────────────────────────────────

/// <summary>
/// How urgently a maintenance action is suggested.
/// </summary>
public enum MaintenancePriority
{
    Routine     = 0,   // Anytime, no hurry
    Recommended = 1,   // Soon — within a few days
    Timely      = 2,   // Worth doing this session
}

// ── WatchtowerAlert ─────────────────────────────────────────────────────────────

/// <summary>
/// A single proactive alert raised by the Operational Watchtower.
/// Alerts describe patterns — they never prescribe actions or claim certainty.
/// </summary>
public sealed record WatchtowerAlert(
    /// <summary>Stable unique identifier for this alert type.</summary>
    string                 AlertId,

    /// <summary>Short display title (≤ 60 characters).</summary>
    string                 Title,

    /// <summary>
    /// One-to-two sentence explanation of what was observed.
    /// Uses confidence-aware language: "appears to be", "may suggest", etc.
    /// </summary>
    string                 Explanation,

    /// <summary>What kind of degradation signal triggered this alert.</summary>
    DegradationSignalType  SignalType,

    /// <summary>How significant this signal is.</summary>
    WatchtowerAlertSeverity Severity,

    /// <summary>When this alert was first detected.</summary>
    DateTimeOffset         DetectedAt,

    /// <summary>Confidence in this finding (0–100). Based on data coverage.</summary>
    int                    ConfidencePercent,

    /// <summary>
    /// The metric most associated with this alert ("CPU", "RAM", "Disk", or null for insight-level alerts).
    /// </summary>
    string?                AffectedMetric,

    /// <summary>Optional linked action key (e.g., "action.disk.clean-temp-files") for quick navigation.</summary>
    string?                LinkedActionKey,

    /// <summary>Short human-readable action hint (e.g., "Consider reviewing disk usage").</summary>
    string?                ActionHint);

// ── DriftObservation ────────────────────────────────────────────────────────────

/// <summary>
/// An observation about how a specific operational metric has shifted over time.
/// Computed by <see cref="OperationalDriftAnalyzer"/> from historical trend data.
/// </summary>
public sealed record DriftObservation(
    /// <summary>Metric name: "CPU", "RAM", or "Disk".</summary>
    string         MetricName,

    /// <summary>Icon glyph for this metric.</summary>
    string         Glyph,

    /// <summary>Direction of drift.</summary>
    DriftDirection Direction,

    /// <summary>7-day average utilisation (null when insufficient data).</summary>
    double?        SevenDayAvg,

    /// <summary>30-day average utilisation (null when insufficient data).</summary>
    double?        ThirtyDayAvg,

    /// <summary>Percentage change from 30-day to 7-day (positive = worsening).</summary>
    double         ChangePercent,

    /// <summary>Short human-readable drift label, e.g. "▲ 12% vs 30-day avg".</summary>
    string         DriftLabel,

    /// <summary>Accent color for UI display based on drift direction.</summary>
    string         DriftColor,

    /// <summary>True when the drift is large enough to warrant attention.</summary>
    bool           IsSignificant);

// ── StabilityRiskForecast ───────────────────────────────────────────────────────

/// <summary>
/// A forward-looking stability risk computed from current trends and alert signals.
/// Does NOT use probabilistic models — estimates are linear projections from historical averages.
/// </summary>
public sealed record StabilityRiskForecast(
    /// <summary>Stable unique identifier.</summary>
    string                    ForecastId,

    /// <summary>Short title, e.g. "Disk pressure projected to increase".</summary>
    string                    Title,

    /// <summary>One-sentence description of the forecasted scenario.</summary>
    string                    Description,

    /// <summary>
    /// Estimated days before the projected threshold is reached.
    /// Null when insufficient data to project.
    /// </summary>
    int?                      DaysToImpact,

    /// <summary>Risk score (0–100). Not a probability — a composite signal strength.</summary>
    int                       RiskScore,

    /// <summary>Confidence in this forecast (0–100). Based on data coverage.</summary>
    int                       ConfidencePercent,

    /// <summary>List of contributing signal descriptions.</summary>
    IReadOnlyList<string>     ContributingSignals,

    /// <summary>When this forecast was computed.</summary>
    DateTimeOffset            GeneratedAt);

// ── MaintenanceWindowRecommendation ─────────────────────────────────────────────

/// <summary>
/// A suggestion for when to run maintenance operations, based on
/// system idle patterns and workload history.
/// </summary>
public sealed record MaintenanceWindowRecommendation(
    /// <summary>Stable unique identifier.</summary>
    string                    WindowId,

    /// <summary>Short title, e.g. "Run disk cleanup".</summary>
    string                    Title,

    /// <summary>Why this maintenance is suggested now.</summary>
    string                    Rationale,

    /// <summary>Suggested timing description, e.g. "When the system is next idle".</summary>
    string                    SuggestedTimeDescription,

    /// <summary>Urgency of this recommendation.</summary>
    MaintenancePriority       Priority,

    /// <summary>List of suggested action keys for this maintenance window.</summary>
    IReadOnlyList<string>     SuggestedActionKeys);

// ── InterventionSuggestion ──────────────────────────────────────────────────────

/// <summary>
/// A ranked intervention suggestion derived from active alerts and effectiveness history.
/// The watchtower suggests — it never auto-executes.
/// </summary>
public sealed record InterventionSuggestion(
    /// <summary>Stable unique identifier.</summary>
    string              SuggestionId,

    /// <summary>Short title for the suggestion.</summary>
    string              Title,

    /// <summary>One-sentence rationale explaining why this is suggested.</summary>
    string              Rationale,

    /// <summary>The action key this suggestion links to (may be null for navigation suggestions).</summary>
    string?             ActionKey,

    /// <summary>Short human-readable effectiveness label from the learning service.</summary>
    string              EffectivenessLabel,

    /// <summary>Relative priority rank (1 = highest).</summary>
    int                 Priority,

    /// <summary>Confidence in this suggestion (0–100).</summary>
    int                 ConfidencePercent);

// ── StabilityOutlook ────────────────────────────────────────────────────────────

/// <summary>
/// High-level stability assessment computed from alerts, drift, and forecasts.
/// The headline summary shown at the top of the Watchtower page.
/// </summary>
public sealed record StabilityOutlook(
    /// <summary>Composite stability score (0–100). Higher = more stable.</summary>
    int                    OverallScore,

    /// <summary>Human-readable label: "Stable", "Monitoring", "Attention Suggested", "Review Recommended".</summary>
    string                 Label,

    /// <summary>Accent color for the outlook score.</summary>
    string                 Color,

    /// <summary>One-paragraph narrative summary of the current stability outlook.</summary>
    string                 NarrativeSummary,

    /// <summary>When this outlook was computed.</summary>
    DateTimeOffset         LastComputedAt);

// ── WatchtowerSnapshot ──────────────────────────────────────────────────────────

/// <summary>
/// Complete output of one Operational Watchtower analysis pass.
/// Produced by <see cref="ProactiveRecommendationCoordinator.RefreshAsync"/>.
/// All fields are computed deterministically and locally.
/// </summary>
public sealed record WatchtowerSnapshot(
    /// <summary>High-level stability outlook headline.</summary>
    StabilityOutlook                         Outlook,

    /// <summary>Active alerts detected in this pass.</summary>
    IReadOnlyList<WatchtowerAlert>           Alerts,

    /// <summary>Operational drift observations per metric.</summary>
    IReadOnlyList<DriftObservation>          DriftObservations,

    /// <summary>Forward-looking stability risk forecasts.</summary>
    IReadOnlyList<StabilityRiskForecast>     Forecasts,

    /// <summary>Suggested maintenance windows.</summary>
    IReadOnlyList<MaintenanceWindowRecommendation> MaintenanceWindows,

    /// <summary>Ranked intervention suggestions derived from alerts and learning history.</summary>
    IReadOnlyList<InterventionSuggestion>    Interventions,

    /// <summary>When this snapshot was computed.</summary>
    DateTimeOffset                           ComputedAt,

    /// <summary>
    /// True when the snapshot was computed with sufficient historical data.
    /// False for new installs or machines with less than 7 days of telemetry.
    /// </summary>
    bool                                     HasSufficientHistory);
