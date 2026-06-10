namespace Microsoft.UI.Dispatching;

/// <summary>
/// Test-only stand-in for WinUI's DispatcherQueue so linked trust services can
/// compile without pulling Windows App SDK into the unit test surface.
/// </summary>
public sealed class DispatcherQueue
{
    public bool HasThreadAccess => true;

    public bool TryEnqueue(Action callback)
    {
        callback();
        return true;
    }
}
