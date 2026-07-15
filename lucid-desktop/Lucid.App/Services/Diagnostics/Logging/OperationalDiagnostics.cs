namespace Lucid.Services.Diagnostics.Logging;

using Lucid.Services.Infrastructure;

/// <summary>
/// Static funnel for failure reports from code that has no injected logger
/// (ViewModel command handlers, page navigation hooks).
///
/// Replaces the scattered <c>Debug.WriteLine</c> calls that hid failures from
/// the operational log (ROADMAP C7): once the service registry is attached at
/// startup, reports flow into <see cref="IOperationalLogger"/> and become
/// inspectable diagnostics. Before attachment (unit tests, XAML design time)
/// reports fall back to the debugger output — the only sanctioned
/// <c>Debug.WriteLine</c> in the app; a source-audit test bans new ones.
/// </summary>
public static class OperationalDiagnostics
{
    private static IServiceRegistry? _registry;

    /// <summary>Called once from AppServices.Initialize() after the registry is populated.</summary>
    public static void Attach(IServiceRegistry registry) => _registry = registry;

    /// <summary>
    /// Reports a caught failure. Never throws — this is called from catch
    /// blocks whose surrounding operation must complete its cleanup.
    /// </summary>
    public static void ReportFailure(string category, string message, Exception exception)
    {
        try
        {
            if (_registry is not null &&
                _registry.TryGet<IOperationalLogger>(out var logger) &&
                logger is not null)
            {
                logger.Error(category, message, exception: exception);
                return;
            }
        }
        catch { /* best-effort: diagnostics must never take down the caller */ }

        System.Diagnostics.Debug.WriteLine($"[{category}] {message}: {exception}");
    }
}
