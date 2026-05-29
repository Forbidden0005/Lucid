namespace Lucid.Core.Contracts;

/// <summary>
/// Implemented by services that require async initialization after construction.
/// AppServices calls InitializeAsync() on all registered IInitializable services
/// before starting the telemetry loop.
/// </summary>
public interface IInitializable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
