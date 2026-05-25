using Microsoft.UI.Dispatching;

namespace Lucid.Services.DesktopContext;

/// <summary>
/// Orchestrates the desktop context providers into a single live awareness service.
///
/// Polling cadence: 1750ms (lightweight — Win32 reads + cached COM result)
/// Thread-safety:   ContextChanged fires on the UI thread via DispatcherQueue
/// Memory:          Current snapshot only — no history stored here
/// Privacy:         All context is ephemeral; nothing is persisted by this service
///
/// Start() / Stop() must be called on the UI thread.
/// </summary>
public sealed class DesktopContextService : IDesktopContextService
{
    private readonly DispatcherQueue         _uiDispatcher;
    private readonly ExplorerContextProvider _explorerProvider;
    private readonly ContextChangeAggregator _aggregator = new();

    private System.Threading.Timer?    _timer;
    private WorkflowContextSnapshot    _currentSnapshot;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1750);

    public DesktopContextService(DispatcherQueue uiDispatcher)
    {
        _uiDispatcher     = uiDispatcher;
        _explorerProvider = new ExplorerContextProvider();
        _currentSnapshot  = new WorkflowContextSnapshot { SnapshotCreatedAt = DateTimeOffset.Now };
    }

    // ── Public surface ─────────────────────────────────────────────────────────

    public WorkflowContextSnapshot CurrentSnapshot => _currentSnapshot;

    public event EventHandler<ContextChangedEventArgs>? ContextChanged;

    public void Start()
    {
        // Kick the STA thread for initial explorer read
        _explorerProvider.Wake();

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
        _explorerProvider.Dispose();
    }

    // ── Polling ────────────────────────────────────────────────────────────────

    private void Poll()
    {
        try
        {
            // These two calls are cheap Win32 reads — no STA needed
            var window    = ActiveWindowTracker.GetCurrent();
            var clipboard = ClipboardContextProvider.GetCurrent();

            // Explorer context comes from the STA cache (updated asynchronously)
            var explorer  = _explorerProvider.GetCurrent();

            // Wake the STA thread for the next cycle
            _explorerProvider.Wake();

            var snapshot = WorkflowContextSnapshotBuilder.Build(window, explorer, clipboard);
            _currentSnapshot = snapshot;

            var change = _aggregator.Evaluate(snapshot);
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
