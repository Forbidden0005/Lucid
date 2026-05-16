using Microsoft.UI.Xaml.Controls;

namespace ExplainMyPC.Core.Navigation;

/// <summary>
/// Abstracts frame-based page navigation from ViewModels and the shell.
/// ViewModels depend on this interface, never on Frame directly, which
/// keeps navigation logic testable and decoupled from WinUI internals.
/// </summary>
public interface INavigationService
{
    /// <summary>True when the back-stack has at least one entry.</summary>
    bool CanGoBack { get; }

    /// <summary>
    /// The page key that was last navigated to, or null if no navigation
    /// has occurred yet.
    /// </summary>
    string? CurrentPageKey { get; }

    /// <summary>
    /// Must be called once from the shell window after InitializeComponent()
    /// to give the service a reference to the content Frame.
    /// </summary>
    void SetFrame(Frame frame);

    /// <summary>
    /// Navigates to the page registered under <paramref name="pageKey"/>.
    /// No-ops if the destination is already the current page.
    /// </summary>
    void Navigate(string pageKey, object? parameter = null);

    /// <summary>Navigates back one entry in the back-stack.</summary>
    void GoBack();
}
