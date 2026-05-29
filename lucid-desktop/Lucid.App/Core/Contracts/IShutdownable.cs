namespace Lucid.Core.Contracts;

/// <summary>
/// Implemented by services that need to perform cleanup on app shutdown.
/// AppServices calls ShutdownAsync() on all registered IShutdownable services
/// when the main window closes, in reverse initialization order.
/// </summary>
public interface IShutdownable
{
    Task ShutdownAsync();
}
