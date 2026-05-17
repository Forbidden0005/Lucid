using ExplainMyPC.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace ExplainMyPC;

/// <summary>
/// The application shell window.
/// Handles sidebar navigation by switching pages in a Frame.
/// No DI, no services — just simple code-behind navigation.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Set a reasonable default window size.
        AppWindow.Resize(new SizeInt32(1280, 820));
        Title = "ExplainMyPC";

        // Navigate to Dashboard on startup.
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    /// <summary>
    /// When a sidebar item is clicked, navigate to the matching page.
    /// The Tag on each NavigationViewItem tells us which page to load.
    /// </summary>
    private void NavView_ItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: string tag })
        {
            NavigateToPage(tag);
        }
    }

    /// <summary>
    /// Simple switch that maps sidebar tag strings to page types.
    /// </summary>
    private void NavigateToPage(string tag)
    {
        Type? pageType = tag switch
        {
            "dashboard" => typeof(DashboardPage),
            "explain"   => typeof(ExplainPage),
            "insights"  => typeof(InsightsPage),
            "security"  => typeof(SecurityPage),
            "processes" => typeof(ProcessesPage),
            "storage"   => typeof(StoragePage),
            "apps"      => typeof(AppsPage),
            "privacy"   => typeof(PrivacyPage),
            "repairs"   => typeof(RepairsPage),
            "settings"  => typeof(SettingsPage),
            _ => null
        };

        // Only navigate if we got a valid page and it's not already showing.
        if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
