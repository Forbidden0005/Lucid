namespace Lucid.Services.Infrastructure;

/// <summary>
/// Read-only lookup over the application's singleton services.
///
/// This is the strangler seam for retiring the <c>AppServices</c> static
/// service locator (ROADMAP C4): the registry is populated once from
/// <c>AppServices.Initialize()</c>, and migrated consumers resolve their
/// dependencies from here (via <c>App.GetService&lt;T&gt;()</c>) instead of
/// reading <c>AppServices.*</c> statics. New code must not reference
/// <c>AppServices</c> directly — a source-audit test enforces the freeze.
/// </summary>
public interface IServiceRegistry
{
    /// <summary>
    /// Returns the registered singleton for <typeparamref name="T"/>.
    /// Throws <see cref="InvalidOperationException"/> when the service is not
    /// registered — resolving before initialization is a programming error and
    /// must fail loudly rather than hand back null.
    /// </summary>
    T Get<T>() where T : class;

    /// <summary>Attempts to resolve without throwing.</summary>
    bool TryGet<T>(out T? service) where T : class;
}
