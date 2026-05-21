using ExplainMyPC.Services.Analytics;
using ExplainMyPC.Services.Governance;
using ExplainMyPC.Services.Learning;

namespace ExplainMyPC.Services.Watchtower;

/// <summary>
/// Orchestrates all Watchtower sub-engines to produce a <see cref="WatchtowerSnapshot"/>.
///
/// Responsibilities:
///   • Fetches historical summary from <see cref="IHistoricalAnalyticsEngine"/>
///   • Runs DegradationEarlyWarningEngine, OperationalDriftAnalyzer,
///     MaintenanceWindowDetector, InterventionPlanner, StabilityRiskForecaster
///   • Caches the result as <see cref="LastSnapshot"/>
///   • Raises <see cref="SnapshotUpdated"/> after each successful refresh
///
/// Threading:
///   <see cref="RefreshAsync"/> runs analysis on a thread-pool thread.
///   <see cref="SnapshotUpdated"/> is raised on the calling thread after analysis completes.
///   Callers must marshal to the UI thread if needed.
/// </summary>
public sealed class ProactiveRecommendationCoordinator
{
    private readonly IHistoricalAnalyticsEngine  _historicalAnalytics;
    private readonly IRemediationLearningService _learningService;
    private readonly IRuntimeGovernanceService   _governance;

    private          WatchtowerSnapshot?   _lastSnapshot;
    private          bool                  _isRefreshing;
    private readonly object                _lock = new();

    // ── Minimum data coverage for meaningful analysis ─────────────────────────
    private const int MinTelemetryRows    = 200;
    private const int MinInsightEvents    = 5;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised after a successful refresh with the new snapshot.</summary>
    public event EventHandler<WatchtowerSnapshot>? SnapshotUpdated;

    // ── Properties ────────────────────────────────────────────────────────────

    /// <summary>The most recently computed snapshot, or null before the first refresh.</summary>
    public WatchtowerSnapshot? LastSnapshot
    {
        get { lock (_lock) { return _lastSnapshot; } }
    }

    /// <summary>True while a refresh is in progress.</summary>
    public bool IsRefreshing
    {
        get { lock (_lock) { return _isRefreshing; } }
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public ProactiveRecommendationCoordinator(
        IHistoricalAnalyticsEngine  historicalAnalytics,
        IRemediationLearningService learningService,
        IRuntimeGovernanceService   governance)
    {
        _historicalAnalytics = historicalAnalytics;
        _learningService     = learningService;
        _governance          = governance;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a full Watchtower analysis pass asynchronously.
    ///
    /// Re-entrant safe — if a refresh is already running this returns immediately.
    /// Governance-aware — skips the pass if the system is under heavy load or
    /// thermal stress (only runs in Normal or LowPower modes).
    ///
    /// Never throws — all exceptions are caught and the old snapshot is preserved.
    /// </summary>
    public async Task RefreshAsync()
    {
        lock (_lock)
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
        }

        try
        {
            // Respect governance — don't add pressure during heavy loads
            var mode = _governance.CurrentMode;
            if (mode == RuntimeMode.ThermalProtection || mode == RuntimeMode.Gaming)
            {
                return;
            }

            var snapshot = await Task.Run(RunAnalysisAsync).ConfigureAwait(false);

            lock (_lock)
            {
                _lastSnapshot = snapshot;
            }

            SnapshotUpdated?.Invoke(this, snapshot);
        }
        catch
        {
            // Never crash — preserve last snapshot
        }
        finally
        {
            lock (_lock)
            {
                _isRefreshing = false;
            }
        }
    }

    // ── Internal analysis ─────────────────────────────────────────────────────

    private async Task<WatchtowerSnapshot> RunAnalysisAsync()
    {
        // ── 1. Fetch historical summary ───────────────────────────────────────
        var summary = await _historicalAnalytics.ComputeAsync().ConfigureAwait(false);

        // Determine if we have enough data for meaningful analysis
        var hasSufficientHistory =
            summary.OldestDataPoint is not null
            && summary.TotalTelemetryRows >= MinTelemetryRows
            && summary.TotalInsightEvents >= MinInsightEvents;

        if (!hasSufficientHistory)
        {
            return BuildColdStartSnapshot(summary);
        }

        // ── 2. Fetch learning profiles ────────────────────────────────────────
        var profiles = _learningService.GetAllProfiles();

        // ── 3. Run sub-engines ────────────────────────────────────────────────
        var alerts       = DegradationEarlyWarningEngine.Analyze(summary);
        var drift        = OperationalDriftAnalyzer.Analyze(summary);
        var forecasts    = StabilityRiskForecaster.BuildForecasts(summary, alerts);
        var outlook      = StabilityRiskForecaster.ComputeOutlook(summary, alerts, hasSufficientHistory: true);
        var interventions = InterventionPlanner.Plan(alerts, profiles);
        var maintenance  = MaintenanceWindowDetector.GetRecommendations(
            _governance.CurrentMode, summary);

        return new WatchtowerSnapshot(
            Outlook:            outlook,
            Alerts:             alerts,
            DriftObservations:  drift,
            Forecasts:          forecasts,
            MaintenanceWindows: maintenance,
            Interventions:      interventions,
            ComputedAt:         DateTimeOffset.Now,
            HasSufficientHistory: true);
    }

    private static WatchtowerSnapshot BuildColdStartSnapshot(HistoricalSummary summary)
    {
        var outlook = StabilityRiskForecaster.ComputeOutlook(
            summary,
            alerts: [],
            hasSufficientHistory: false);

        return new WatchtowerSnapshot(
            Outlook:             outlook,
            Alerts:              [],
            DriftObservations:   [],
            Forecasts:           [],
            MaintenanceWindows:  [],
            Interventions:       [],
            ComputedAt:          DateTimeOffset.Now,
            HasSufficientHistory: false);
    }
}
