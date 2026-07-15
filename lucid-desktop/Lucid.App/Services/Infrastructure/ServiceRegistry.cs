using System.Collections.Concurrent;

namespace Lucid.Services.Infrastructure;

/// <summary>
/// Dictionary-backed <see cref="IServiceRegistry"/>. Population happens once
/// during startup (from <c>AppServices.Initialize()</c>); lookups are
/// lock-free reads thereafter.
/// </summary>
public sealed class ServiceRegistry : IServiceRegistry
{
    private readonly ConcurrentDictionary<Type, object> _services = new();

    /// <summary>
    /// Registers <paramref name="instance"/> under <paramref name="serviceType"/>.
    /// Last registration wins — Initialize() registers each service exactly once,
    /// so a duplicate indicates a wiring bug surfaced by tests rather than a
    /// supported scenario.
    /// </summary>
    public void Register(Type serviceType, object instance)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(instance);
        _services[serviceType] = instance;
    }

    public T Get<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var instance))
            return (T)instance;

        throw new InvalidOperationException(
            $"Service {typeof(T).Name} is not registered. " +
            "AppServices.Initialize() must complete before services are resolved.");
    }

    public bool TryGet<T>(out T? service) where T : class
    {
        if (_services.TryGetValue(typeof(T), out var instance))
        {
            service = (T)instance;
            return true;
        }

        service = null;
        return false;
    }
}
