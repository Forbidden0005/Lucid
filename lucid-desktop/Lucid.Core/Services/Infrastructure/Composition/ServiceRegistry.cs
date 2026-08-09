using System.Collections.Concurrent;

namespace Lucid.Services.Infrastructure.Composition;

/// <summary>
/// Minimal factory-based implementation of <see cref="IServiceRegistry"/>.
///
/// Deliberately not a general-purpose DI container: no reflection, no
/// auto-wiring, no lifetime scopes. Each registration is an explicit
/// factory closing over the hand-ordered services built during the composition root
/// initialization, which keeps construction order visible and the
/// migration reviewable one page at a time.
/// </summary>
public sealed class ServiceRegistry : IServiceRegistry
{
    private readonly ConcurrentDictionary<Type, Func<object>> _factories = new();

    /// <summary>
    /// Registers a factory for <typeparamref name="T"/>. Duplicate
    /// registration throws — each type must have exactly one composition
    /// site, so an accidental double-registration is a bug, not a request
    /// to overwrite.
    /// </summary>
    public void Register<T>(Func<T> factory) where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (!_factories.TryAdd(typeof(T), () => factory()))
        {
            throw new InvalidOperationException(
                $"A factory for {typeof(T).Name} is already registered. " +
                "Each type has exactly one composition site; remove the duplicate registration.");
        }
    }

    public T Resolve<T>() where T : class
    {
        if (!_factories.TryGetValue(typeof(T), out var factory))
        {
            throw new InvalidOperationException(
                $"No factory registered for {typeof(T).Name}. " +
                "Register it during the composition root initialization before the first page resolves it.");
        }

        return (T)factory();
    }
}
