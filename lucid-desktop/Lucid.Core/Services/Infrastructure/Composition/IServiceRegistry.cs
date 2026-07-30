namespace Lucid.Services.Infrastructure.Composition;

/// <summary>
/// Composition seam for the static-locator strangler migration (ROADMAP C4).
///
/// Pages and ViewModels migrating off the static service-locator
/// resolve their ViewModels through this registry instead (via
/// <c>App.Registry</c>), so the only remaining static read is the single
/// composition entry point. The composition root initializer populates the
/// registry with one factory per migrated ViewModel; registration is
/// explicit and construction stays hand-ordered — this is a seam, not a
/// container.
///
/// Lifetime semantics: <see cref="Resolve{T}"/> invokes the registered
/// factory on every call (transient), matching the previous
/// new-ViewModel-per-page-instantiation behavior.
/// </summary>
public interface IServiceRegistry
{
    /// <summary>
    /// Resolves a new instance of <typeparamref name="T"/> from its
    /// registered factory (registered during the composition root initialization).
    /// Throws <see cref="InvalidOperationException"/> with the missing type
    /// name when nothing is registered for <typeparamref name="T"/>.
    /// </summary>
    T Resolve<T>() where T : class;
}
