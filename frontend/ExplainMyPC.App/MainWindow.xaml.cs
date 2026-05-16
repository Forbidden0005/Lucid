using ExplainMyPC.Core.Navigation;
using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace ExplainMyPC;

/// <summary>
/// The application shell window.
///
/// Responsibilities:
///   • Host the NavigationView sidebar and content Frame.
///   • Wire the NavigationView's item-invoked events to NavigationService.
///   • Set the initial window size and navigate to the Dashboard on first load.
///
/// What this class does NOT do:
///   • It does not contain business logic — that lives in ShellViewModel.
///   • It does not navigate programmatically from ViewModels — pages call
///     INavigationService directly via DI.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly INavigationService _navigationService;
    private readonly ShellViewModel _shellViewModel;

    public MainWindow()
    {
        InitializeComponent();

        _navigationService = App.GetService<INavigationService>();
        _shellViewModel    = App.GetService<ShellViewModel>();

        // Give NavigationService the Frame it will drive for the app lifetime.
        _navigationService.SetFrame(ContentFrame);

        // Reasonable default size for a diagnostics dashboard.
        AppWindow.Resize(new SizeInt32(1280, 820));
        Title = "ExplainMyPC";

        Loaded += OnLoaded;
    }

    // ── Event Handlers ───────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Pre-select Dashboard in the sidebar and navigate there.
        NavView.SelectedItem = NavDashboard;
        _navigationService.Navigate("dashboard");
    }

    private void NavView_ItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: string tag })
            _navigationService.Navigate(tag);
    }

    private void NavView_BackRequested(
        NavigationView sender,
        NavigationViewBackRequestedEventArgs args)
    {
        _shellViewModel.GoBack();
        // Keep the sidebar selection in sync after going back.
        SyncSidebarSelection();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// After a back-navigation, finds the NavigationViewItem whose Tag
    /// matches the new current page key and re-selects it in the sidebar.
    /// </summary>
    private void SyncSidebarSelection()
    {
        var key = _navigationService.CurrentPageKey;
        if (key is null) return;

        foreach (var obj in NavView.MenuItems)
        {
            if (obj is NavigationViewItem item &&
                item.Tag is string tag &&
                tag.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                NavView.SelectedItem = item;
                return;
            }
        }

        foreach (var obj in NavView.FooterMenuItems)
        {
            if (obj is NavigationViewItem item &&
                item.Tag is string tag &&
                tag.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                NavView.SelectedItem = item;
                return;
            }
        }
    }
}
