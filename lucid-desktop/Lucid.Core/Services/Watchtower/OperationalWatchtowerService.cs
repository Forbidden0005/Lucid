using Lucid.Services.Governance;
using Lucid.Services.Infrastructure;

namespace Lucid.Services.Watchtower;

/// <summary>
/// Autonomous Operational Watchtower service.
///
/// Orchestrates the <see cref="ProactiveRecommendationCoordinator"/> on a
/// 30-minute analysis cycle. Respects runtime governance — skips passes when
/// the system is under thermal or gaming load.
///
/// The watchtower ONLY observes, suggests, and forecasts.
/// It NEVER auto-remediates, silently changes the system, or creates fear messaging.
///
/// Lifecycle: call <see cref="Start"/> once, <see cref="Stop"/> on shutdown.
/// </summary>
public sealed class OperationalWatchtowerService : IDisposable
{
    private readonly ProactiveRecommendationCoordinator _coordinator;
    private readonly IRuntimeGovernanceService          _governance;
    private readonly IUiDispatcher                    _ui;

    private System.Threading.Timer? _cycleTimer;
    private bool                    _started;
    private bool                    _disposed;

    // Analysis interval: 30 minutes in normal mode
    private static readonly TimeSpan AnalysisInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan InitialDelay      = TimeSpan.FromSeconds(90);

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the UI thread after each completed analysis pass.
    /// The snapshot is the full output of the Watchtower analysis.
    /// </summary>
    public event EventHandler<WatchtowerSnapshot>? SnapshotUpdated;

    // ── Properties ────────────────────────────────────────────────────────────

    /// <summary>
    /// The most recently computed snapshot, or null before the first pass.
    /// </summary>
    public WatchtowerSnapshot? LastSnapshot => _coordinator.LastSnapshot;

    /// <summary>True while an analysis pass is running.</summary>
    public bool IsAnalyzing => _coordinator.IsRefreshing;

    /// <summary>The underlying coordinator — exposed for direct refresh from the UI.</summary>
    public ProactiveRecommendationCoordinator Coordinator => _coordinator;

    // ── Constructor ───────────────────────────────────────────────────────────

    public OperationalWatchtowerService(
        ProactiveRecommendationCoordinator coordinator,
        IRuntimeGovernanceService          governance,
        IUiDispatcher                    uiDispatcher)
    {
        _coordinator = coordinator;
        _governance  = governance;
        _ui          = uiDispatcher;

        // Forward coordinator events to the UI thread
        _coordinator.SnapshotUpdated += OnSnapshotUpdated;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the 30-minute analysis cycle.
    /// The first pass runs 90 seconds after start to avoid startup contention.
    /// </summary>
    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;

        _cycleTimer = new System.Threading.Timer(
            callback: _ => RunCycleAsync(),
            state:    null,
            dueTime:  InitialDelay,
            period:   AnalysisInterval);
    }

    /// <summary>Stops the analysis cycle.</summary>
    public void Stop()
    {
        _cycleTimer?.Dispose();
        _cycleTimer = null;
        _started    = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _coordinator.SnapshotUpdated -= OnSnapshotUpdated;
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void RunCycleAsync()
    {
        // Governed cycle (adoption audit 2026-07-25, site #3): the analytics
        // pass holds the HistoricalAnalytics slot (IdleOnly), so it only runs
        // when the system is calm — replacing the old advisory check that
        // still let it run under HighLoad/LowPower. When refused, the pass is
        // deferred for retry on the next queue drain instead of dropped; the
        // 30-minute timer and the deferred-entry dedupe keep the queue from
        // stacking duplicate cycles.
        _ = GovernedWorkRunner.RunOrDeferAsync(
            _governance,
            WorkloadCategory.HistoricalAnalytics,
            "Watchtower analysis cycle",
            () => _coordinator.RefreshAsync());
    }

    private void OnSnapshotUpdated(object? sender, WatchtowerSnapshot snapshot)
    {
        // Marshal to UI thread before raising the public event
        _ui.TryEnqueue(() => SnapshotUpdated?.Invoke(this, snapshot));
    }
}
