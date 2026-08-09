namespace Lucid.Services.Trust;

/// <summary>
/// WinUI-side adapter for <see cref="Lucid.Services.Infrastructure.IUiDispatcher"/>.
/// Lucid.Core is WinUI-free, so the DispatcherQueue coupling lives here in
/// Lucid.App (the interface is internal in Lucid.Core; this assembly sees it
/// via InternalsVisibleTo).
/// </summary>
internal sealed class DispatcherQueueUiDispatcher : Lucid.Services.Infrastructure.IUiDispatcher
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;

    public DispatcherQueueUiDispatcher(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool HasThreadAccess => _dispatcher.HasThreadAccess;

    public void TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _dispatcher.TryEnqueue(() => action());
    }
}
