using Lucid.Helpers;
using Lucid.Services.Infrastructure;

namespace Lucid.Services.Governance;

/// <summary>
/// Concrete implementation of <see cref="IRuntimeGovernanceService"/>.
///
/// Subscribes to the live telemetry stream and re-evaluates the runtime mode
/// on every reading. When the mode changes, it:
///   1. Updates the concurrency budget ceiling.
///   2. Notifies the polling coordinator so telemetry rates adjust.
///   3. Raises <see cref="ModeChanged"/> on the UI thread.
///   4. Drains the deferred work queue (new mode may allow previously blocked work).
///
/// Hysteresis:
///   Mode transitions require the new state to be sustained for at least
///   <see cref="HysteresisCount"/> consecutive samples before being applied.
///   This prevents rapid oscillation when CPU load hovers near a threshold.
///   Normal mode is exempt — recovery is applied immediately.
///
/// Battery check:
///   Battery state is read via IPowerStateProvider on each evaluation. The call is
///   cheap (a cached kernel value) and safe to call from a background thread.
/// </summary>
public sealed class RuntimeGovernanceService : IRuntimeGovernanceService, IDisposable
{
    // ── Hysteresis ────────────────────────────────────────────────────────────

    /// <summary>
    /// Consecutive samples the new mode must appear in before it is applied.
    /// At 1.5 s/sample this is ~4.5 s of sustained pressure before throttling.
    /// Recovery to Normal is immediate (count is not applied).
    /// </summary>
    private const int HysteresisCount = 3;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ITelemetryService      _telemetry;
    private readonly PollingCoordinator     _polling;
    private readonly ConcurrencyBudget      _budget;
    private readonly ExecutionPriorityQueue _queue;
    private readonly IUiDispatcher        _uiDispatcher;
    private readonly IPowerStateProvider _power;

    // ── Mode state ────────────────────────────────────────────────────────────

    private readonly object  _lock              = new();
    private RuntimeMode      _currentMode       = RuntimeMode.Normal;
    private ThrottlingReason _currentReasons    = ThrottlingReason.None;
    private RuntimeMode      _pendingMode       = RuntimeMode.Normal;
    private int              _pendingCount      = 0;

    private bool             _started;
    private bool             _disposed;

    // ── Events ────────────────────────────────────────────────────────────────

    public event EventHandler<RuntimeModeChangedEventArgs>? ModeChanged;

    // ── Construction ──────────────────────────────────────────────────────────

    public RuntimeGovernanceService(
        ITelemetryService      telemetry,
        PollingCoordinator     pollingCoordinator,
        ConcurrencyBudget      budget,
        ExecutionPriorityQueue queue,
        IUiDispatcher        uiDispatcher,
        IPowerStateProvider  power)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(pollingCoordinator);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(uiDispatcher);

        _telemetry    = telemetry;
        _polling      = pollingCoordinator;
        _budget       = budget;
        _queue        = queue;
        _uiDispatcher = uiDispatcher;
        _power        = power;
    }

    // ── IRuntimeGovernanceService ─────────────────────────────────────────────

    public RuntimeMode      CurrentMode    { get { lock (_lock) return _currentMode;    } }
    public ThrottlingReason CurrentReasons { get { lock (_lock) return _currentReasons; } }

    public ResourceBudgetSnapshot GetSnapshot()
    {
        RuntimeMode      mode;
        ThrottlingReason reasons;
        lock (_lock) { mode = _currentMode; reasons = _currentReasons; }
        return _budget.GetSnapshot(mode, reasons);
    }

    public IReadOnlyList<ActiveWorkloadEntry>   GetActiveWorkloads()  => _budget.GetActiveWorkloads();
    public IReadOnlyList<DeferredWorkloadEntry> GetDeferredWorkloads() => _queue.GetSnapshot();

    public bool TryAcquireSlot(
        WorkloadCategory category,
        string           workloadName,
        out string?      refusalReason)
    {
        // IdleOnly check: policy says only allowed in Normal mode.
        // This is a governance-level gate that supplements the budget gate.
        var priority = WorkloadClassifier.Classify(category);
        if (priority == WorkloadPriority.IdleOnly)
        {
            RuntimeMode mode;
            lock (_lock) { mode = _currentMode; }

            if (!AdaptiveSchedulingPolicy.AllowIdleOnlyWork(mode))
            {
                refusalReason = AdaptiveSchedulingPolicy.GetDeferralDescription(mode);
                return false;
            }
        }

        return _budget.TryAcquire(category, workloadName, out refusalReason);
    }

    public void DeferWorkload(
        WorkloadCategory category,
        string           workloadName,
        string?          refusalReason,
        Func<Task>       retry)
    {
        ArgumentNullException.ThrowIfNull(retry);

        _queue.Enqueue(
            new DeferredWorkloadEntry(
                workloadName,
                category,
                WorkloadClassifier.Classify(category),
                DateTimeOffset.Now,
                refusalReason ?? "System is busy."),
            retry);
    }

    public void ReleaseSlot(WorkloadCategory category, string workloadName)
    {
        _budget.Release(category, workloadName);

        // Releasing a slot may have freed space for queued work — drain now.
        _queue.Drain();
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RuntimeGovernanceService));
        if (_started)  return;

        _started = true;
        _telemetry.ReadingAvailable += OnReadingAvailable;
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _telemetry.ReadingAvailable -= OnReadingAvailable;
        _queue.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // ── Telemetry callback ────────────────────────────────────────────────────

    private void OnReadingAvailable(object? sender, TelemetrySnapshot snapshot)
    {
        // Battery status via the power seam — cheap cached read, safe off-thread.
        // The provider swallows platform failures and reports "plugged in", so
        // no try/catch is needed here (see IPowerStateProvider).
        bool isOnBattery = _power.IsOnBattery;

        var (candidateMode, reasons) = RuntimePressureAnalyzer.Analyze(snapshot, isOnBattery);

        RuntimeMode previous;
        RuntimeMode applied;
        bool        modeChanged;

        lock (_lock)
        {
            previous = _currentMode;

            // Recovery to Normal is immediate — no hysteresis.
            if (candidateMode == RuntimeMode.Normal)
            {
                _pendingMode  = RuntimeMode.Normal;
                _pendingCount = HysteresisCount; // reset hysteresis counter
            }
            else if (candidateMode == _pendingMode)
            {
                _pendingCount++;
            }
            else
            {
                _pendingMode  = candidateMode;
                _pendingCount = 1;
            }

            // Apply the candidate once it has been sustained long enough,
            // OR immediately when recovering to Normal.
            bool shouldApply = candidateMode == RuntimeMode.Normal
                || _pendingCount >= HysteresisCount;

            if (shouldApply && _currentMode != candidateMode)
            {
                _currentMode    = candidateMode;
                _currentReasons = reasons;
                modeChanged     = true;
            }
            else
            {
                if (shouldApply) _currentReasons = reasons; // update reasons even if mode same
                modeChanged = false;
            }

            applied = _currentMode;
        }

        if (!modeChanged)
            return;

        // ── Mode changed — apply consequences ─────────────────────────────────

        // 1. Update background concurrency ceiling.
        int newMax = AdaptiveSchedulingPolicy.GetMaxConcurrentBackground(applied);
        _budget.UpdateMaxBackground(newMax);

        // 2. Adjust telemetry polling rates.
        _polling.ApplyMode(applied);

        // 3. Raise event on UI thread.
        var args = new RuntimeModeChangedEventArgs(previous, applied, reasons);
        _uiDispatcher.TryEnqueue(() => ModeChanged?.Invoke(this, args));

        // 4. Drain the deferred queue — the new mode may allow previously blocked
        //    work. Any transition can loosen the budget (e.g. Gaming → HighLoad
        //    raises the background ceiling from 0 to 1), so drain on every change.
        //    Retries that still can't acquire simply re-defer themselves.
        _queue.Drain();
    }
}
