using Lucid.Services.Watchtower;

namespace Lucid.Services.Intelligence;

// ── Interface ───────────────────────────────────────────────────────────────────

/// <summary>
/// Synthesizes Watchtower alerts and anomaly detections into a unified stream of
/// <see cref="EarlyWarning"/> records — the primary proactive awareness surface.
///
/// All warnings use confidence-aware, non-alarming language.
/// Findings are suppressed when confidence is too low to be actionable.
/// </summary>
public interface IEarlyWarningService
{
    /// <summary>Currently active early warnings, ordered by severity then detection time.</summary>
    IReadOnlyList<EarlyWarning> CurrentWarnings { get; }

    /// <summary>
    /// Raised on the UI thread whenever the active warning set changes.
    /// </summary>
    event EventHandler<IReadOnlyList<EarlyWarning>>? WarningsUpdated;
}

// ── Implementation ──────────────────────────────────────────────────────────────

/// <summary>
/// Combines signals from <see cref="IAnomalyDetectionService"/> and
/// <see cref="OperationalWatchtowerService"/> into a unified early warning stream.
///
/// Design notes:
/// • Watchtower alerts → translated directly to EarlyWarning records.
/// • Anomaly detections → promoted to EarlyWarning when severity ≥ Elevated.
/// • Duplicate suppression: anomaly-based warnings are not emitted when the
///   Watchtower has already raised an alert for the same metric.
/// • Only fires <see cref="WarningsUpdated"/> when the warning ID set changes.
/// • All events arrive on the UI thread — no marshalling needed.
/// </summary>
public sealed class EarlyWarningService : IEarlyWarningService
{
    private readonly IAnomalyDetectionService _anomaly;

    private IReadOnlyList<EarlyWarning> _current      = [];
    private IReadOnlyList<EarlyWarning> _watchtowerWarnings = [];
    private IReadOnlyList<EarlyWarning> _anomalyWarnings    = [];
    private HashSet<string>             _activeIds    = [];

    /// <inheritdoc/>
    public IReadOnlyList<EarlyWarning> CurrentWarnings => _current;

    /// <inheritdoc/>
    public event EventHandler<IReadOnlyList<EarlyWarning>>? WarningsUpdated;

    public EarlyWarningService(IAnomalyDetectionService anomaly, OperationalWatchtowerService watchtower)
    {
        _anomaly = anomaly;

        anomaly.AnomaliesUpdated     += OnAnomaliesUpdated;
        watchtower.SnapshotUpdated   += OnSnapshotUpdated;

        // Seed immediately from current state.
        _anomalyWarnings = BuildFromAnomalies(anomaly.CurrentAnomalies);
        if (watchtower.LastSnapshot is { } snap)
            _watchtowerWarnings = BuildFromSnapshot(snap);

        Merge();
    }

    // ── Anomaly intake ────────────────────────────────────────────────────────

    private void OnAnomaliesUpdated(object? sender, IReadOnlyList<DetectedAnomaly> anomalies)
    {
        _anomalyWarnings = BuildFromAnomalies(anomalies);
        Merge();
    }

    private static IReadOnlyList<EarlyWarning> BuildFromAnomalies(
        IReadOnlyList<DetectedAnomaly> anomalies)
    {
        // Only promote anomalies with severity ≥ Elevated to early warnings.
        return anomalies
            .Where(a => a.Severity >= AnomalyIntelligenceSeverity.Elevated)
            .Select(a => new EarlyWarning(
                WarningId:         $"anomaly.{a.Metric.ToLowerInvariant()}",
                Title:             BuildAnomalyTitle(a),
                Explanation:       a.Description,
                Severity:          MapAnomalySeverity(a.Severity),
                ConfidencePercent: ConfidenceFromZ(a.DivergenceScore),
                AffectedMetric:    a.Metric,
                ActionHint:        BuildAnomalyHint(a.Metric),
                DetectedAt:        a.DetectedAt))
            .ToList();
    }

    private static string BuildAnomalyTitle(DetectedAnomaly a) => a.Metric switch
    {
        "CPU"     => $"CPU activity appears unusual ({a.ActualValue:0}%)",
        "RAM"     => $"RAM usage higher than typical ({a.ActualValue:0}%)",
        "Thermal" => $"Temperature elevated above normal ({a.ActualValue:0}°C)",
        _         => $"{a.Metric} showing unexpected activity",
    };

    private static string BuildAnomalyHint(string metric) => metric switch
    {
        "CPU"     => "Review active processes to identify what's consuming CPU.",
        "RAM"     => "Check for memory-heavy applications or background tasks.",
        "Thermal" => "Ensure ventilation is clear and consider checking fan health.",
        _         => "Review recent changes that may have affected this metric.",
    };

    private static EarlyWarningSeverity MapAnomalySeverity(AnomalyIntelligenceSeverity s) => s switch
    {
        AnomalyIntelligenceSeverity.High     => EarlyWarningSeverity.Elevated,
        AnomalyIntelligenceSeverity.Elevated => EarlyWarningSeverity.Warning,
        _                                    => EarlyWarningSeverity.Advisory,
    };

    private static int ConfidenceFromZ(double z) =>
        z >= 3.0 ? 88 : z >= 2.5 ? 80 : z >= 2.0 ? 72 : 65;

    // ── Watchtower intake ─────────────────────────────────────────────────────

    private void OnSnapshotUpdated(object? sender, WatchtowerSnapshot snap)
    {
        _watchtowerWarnings = BuildFromSnapshot(snap);
        Merge();
    }

    private static IReadOnlyList<EarlyWarning> BuildFromSnapshot(WatchtowerSnapshot snap)
    {
        if (!snap.HasSufficientHistory) return [];

        return snap.Alerts.Select(alert => new EarlyWarning(
            WarningId:         $"watchtower.{alert.AlertId}",
            Title:             alert.Title,
            Explanation:       alert.Explanation,
            Severity:          MapWatchtowerSeverity(alert.Severity),
            ConfidencePercent: alert.ConfidencePercent,
            AffectedMetric:    alert.AffectedMetric,
            ActionHint:        alert.ActionHint,
            DetectedAt:        alert.DetectedAt.UtcDateTime))
        .ToList();
    }

    private static EarlyWarningSeverity MapWatchtowerSeverity(WatchtowerAlertSeverity s) => s switch
    {
        WatchtowerAlertSeverity.Elevated => EarlyWarningSeverity.Elevated,
        WatchtowerAlertSeverity.Warning  => EarlyWarningSeverity.Warning,
        WatchtowerAlertSeverity.Advisory => EarlyWarningSeverity.Advisory,
        _                                => EarlyWarningSeverity.Info,
    };

    // ── Merge and deduplicate ─────────────────────────────────────────────────

    private void Merge()
    {
        // Build a deduplicated merged set.
        // Watchtower warnings take priority — suppress anomaly-based warnings for the same metric.
        var watchtowerMetrics = new HashSet<string>(
            _watchtowerWarnings
                .Where(w => w.AffectedMetric is not null)
                .Select(w => w.AffectedMetric!));

        var filteredAnomalyWarnings = _anomalyWarnings
            .Where(w => w.AffectedMetric is null || !watchtowerMetrics.Contains(w.AffectedMetric))
            .ToList();

        var merged = _watchtowerWarnings
            .Concat(filteredAnomalyWarnings)
            .OrderByDescending(w => (int)w.Severity)
            .ThenBy(w => w.DetectedAt)
            .ToList();

        // Only publish when the set of warning IDs actually changes.
        var newIds = new HashSet<string>(merged.Select(w => w.WarningId));
        if (newIds.SetEquals(_activeIds)) return;

        _activeIds = newIds;
        _current   = merged;
        WarningsUpdated?.Invoke(this, _current);
    }
}
