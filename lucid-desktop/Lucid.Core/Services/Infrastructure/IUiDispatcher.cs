namespace Lucid.Services.Infrastructure;

/// <summary>
/// UI-thread dispatch seam.
///
/// Lucid.Core is WinUI-free, but many services need to raise events on the UI
/// thread so ViewModels can assign observable properties without dispatching
/// again. Depending on <c>Microsoft.UI.Dispatching.DispatcherQueue</c> directly
/// would pin those services to Lucid.App and put them out of reach of the test
/// project — so they take this interface instead.
///
/// Implementations:
///   • <c>Lucid.Services.Trust.DispatcherQueueUiDispatcher</c> (Lucid.App) wraps
///     the real WinUI DispatcherQueue.
///   • Tests supply an immediate dispatcher that runs the action inline.
///
/// This was previously a nested internal interface on
/// <see cref="Lucid.Services.Trust.AutomationConsentService"/>. It was promoted
/// here when the second consumer appeared; the nested form is kept as an alias
/// so existing call sites keep compiling.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// <see langword="true"/> when the calling thread is already the UI thread,
    /// so the caller may invoke directly instead of enqueuing.
    /// </summary>
    bool HasThreadAccess { get; }

    /// <summary>
    /// Queues <paramref name="action"/> to run on the UI thread.
    /// Implementations must not throw when the queue is shutting down —
    /// dropping the callback is the correct behaviour at that point.
    /// </summary>
    void TryEnqueue(Action action);
}
