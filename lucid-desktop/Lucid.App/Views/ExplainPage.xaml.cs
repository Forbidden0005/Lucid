using CommunityToolkit.Mvvm.DependencyInjection;
using Lucid.Helpers;
using Lucid.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Lucid.Views;

/// <summary>
/// Explain My PC flagship page — operational reasoning workspace.
///
/// Code-behind responsibilities:
///   • Set DataContext to ExplainViewModel wired to the live engine.
///   • Animate the hero card entrance on page load.
///   • Delegate hover effects to AnimationHelper.
///
/// All business logic lives in ExplainViewModel and the ExplainMyPcEngine.
/// The page is purely a display surface.
/// </summary>
public sealed partial class ExplainPage : Page
{
    public ExplainPage()
    {
        InitializeComponent();

        // Wire ViewModel to the live engine registered in AppServices
        DataContext = Ioc.Default.GetRequiredService<ExplainViewModel>();

        // Unsubscribe when the page is unloaded to prevent event-handler leaks
        // on back-navigation (each navigation creates a fresh ExplainViewModel).
        Unloaded += (_, _) => ((ExplainViewModel)DataContext).Cleanup();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Hero card slides up and fades in as the flagship visual anchor
        AnimationHelper.AnimateEntrance(CardHero, delayMs: 0);
    }

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
        => AnimationHelper.PointerEntered(sender);

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
        => AnimationHelper.PointerExited(sender);
}
