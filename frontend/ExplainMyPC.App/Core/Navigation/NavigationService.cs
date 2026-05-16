using ExplainMyPC.Views;
using Microsoft.UI.Xaml.Controls;

namespace ExplainMyPC.Core.Navigation;

/// <inheritdoc cref="INavigationService"/>
public sealed class NavigationService : INavigationService
{
    private Frame? _frame;

    /// <summary>
    /// Central mapping from stable string keys to page types.
    /// Keys are lowercase slugs that match the Tag values set on each
    /// NavigationViewItem in MainWindow.xaml — one source of truth for routing.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Type> PageMap =
        new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["dashboard"] = typeof(DashboardPage),
            ["explain"]   = typeof(ExplainPage),
            ["security"]  = typeof(SecurityPage),
            ["processes"] = typeof(ProcessesPage),
            ["storage"]   = typeof(StoragePage),
            ["apps"]      = typeof(AppsPage),
            ["privacy"]   = typeof(PrivacyPage),
            ["repairs"]   = typeof(RepairsPage),
            ["settings"]  = typeof(SettingsPage),
        };

    // ── INavigationService ───────────────────────────────────────────────────

    public bool CanGoBack => _frame?.CanGoBack ?? false;
    public string? CurrentPageKey { get; private set; }

    public void SetFrame(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
    }

    public void Navigate(string pageKey, object? parameter = null)
    {
        if (_frame is null)
            throw new InvalidOperationException(
                "SetFrame() must be called before Navigate().");

        if (!PageMap.TryGetValue(pageKey, out var pageType))
            throw new ArgumentException(
                $"No page is registered for key '{pageKey}'.", nameof(pageKey));

        // Avoid duplicate navigation to the same page type.
        if (_frame.CurrentSourcePageType == pageType)
            return;

        _frame.Navigate(pageType, parameter);
        CurrentPageKey = pageKey.ToLowerInvariant();
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
            _frame.GoBack();
    }
}
