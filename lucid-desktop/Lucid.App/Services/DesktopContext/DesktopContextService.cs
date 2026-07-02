using Microsoft.UI.Dispatching;

namespace Lucid.Services.DesktopContext;

/// <summary>
/// Orchestrates the desktop context providers into a single live awareness service.
///
/// Polling cadence: 1750ms (lightweight — Win32 reads + cached COM result)
/// Thread-safety:   ContextChanged fires on the UI thread via DispatcherQueue
/// Memory:          Current snapshot only — no history stored here
/// Privacy:         Consent-gated — capture only runs while the user has the
///                  opt-in setting enabled (AppSettings.DesktopContextAwarenessEnabled,
///                  off by default). All context is ephemeral; nothing is
///                  persisted by this service. While stopped, CurrentSnapshot
///                  stays an empty snapshot, so downstream consumers simply see
///                  "no desktop context".
///
/// Start() / Stop() must be called on the UI thread. The service is
/// restart-safe: the Explorer STA provider is created per Start() and
/// disposed per Stop(), so the user can toggle awareness at runtime.
/// </summary>
public sealed class DesktopContextService : IDesktopContextService
{
    private readonly DispatcherQueue         _uiDispatcher;
    private readonly ContextChangeAggregator _aggregator = new();

    private ExplorerContextProvider?   _explorerProvider;
    private System.Threading.Timer?    _timer;
    private WorkflowContextSnapshot    _currentSnapshot;

    /// <summary>
    /// Guards the consent boundary: Stop()'s teardown and Poll()'s publish are
    /// mutually exclusive, so an in-flight poll can never republish captured
    /// context after consent was withdrawn.
    /// </summary>
    private readonly object _publishGate = new();

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1750);

    public DesktopContextService(DispatcherQueue uiDispatcher)
    {
        _uiDispatcher     = uiDispatcher;
        _currentSnapshot  = new WorkflowContextSnapshot { SnapshotCreatedAt = DateTimeOffset.Now };
    }

    // ── Public surface ─────────────────────────────────────────────────────────

    public WorkflowContextSnapshot CurrentSnapshot => _currentSnapshot;

    public bool IsRunning => _timer is not null;

    public event EventHandler<ContextChangedEventArgs>? ContextChanged;

    public void Start()
    {
        if (IsRunning) return;

        // Fresh provider per session — Stop() tears down its STA thread.
        _explorerProvider = new ExplorerContextProvider();
        _explorerProvider.Wake();   // kick the STA thread for initial explorer read

        _timer = new System.Threading.Timer(
            _ => Poll(),
            state:    null,
            dueTime:  TimeSpan.FromSeconds(1),   // first poll after 1s
            period:   PollInterval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;

        lock (_publishGate)
        {
            _explorerProvider?.Dispose();
            _explorerProvider = null;

            // Clear captured context immediately — consent was withdrawn, so no
            // stale window/clipboard data may linger for downstream consumers.
            // Inside the gate: an in-flight Poll() either publishes before this
            // clear (and is overwritten here) or observes the null provider and
            // publishes nothing.
            _currentSnapshot = new WorkflowContextSnapshot { SnapshotCreatedAt = DateTimeOffset.Now };
        }
    }

    // ── Polling ────────────────────────────────────────────────────────────────

    private void Poll()
    {
        // Timer callbacks can race a concurrent Stop(); a null provider means
        // consent was just withdrawn — capture nothing.
        var provider = _explorerProvider;
        if (provider is null) return;

        try
        {
            // These two calls are cheap Win32 reads — no STA needed
            var window    = ActiveWindowTracker.GetCurrent();
            var clipboard = ClipboardContextProvider.GetCurrent();

            // Explorer context comes from the STA cache (updated asynchronously)
            var explorer  = provider.GetCurrent();

            // Wake the STA thread for the next cycle
            provider.Wake();

            var snapshot = WorkflowContextSnapshotBuilder.Build(window, explorer, clipboard);

            // Re-check consent at the publish boundary: Stop() may have run
            // while this poll was capturing. Publishing is only legal while
            // the capture session that started this poll is still the active
            // one — otherwise the data is dropped on the floor.
            ContextChangedEventArgs? change = null;
            lock (_publishGate)
            {
                if (!ReferenceEquals(_explorerProvider, provider)) return;

                _currentSnapshot = snapshot;
                change = _aggregator.Evaluate(snapshot);
            }

            if (change is not null)
                FireContextChanged(change);
        }
        catch { /* polling is best-effort — never crash the timer */ }
    }

    private void FireContextChanged(ContextChangedEventArgs args)
    {
        _uiDispatcher.TryEnqueue(() => ContextChanged?.Invoke(this, args));
    }
}
