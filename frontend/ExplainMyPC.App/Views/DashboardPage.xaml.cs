using ExplainMyPC.Helpers;
using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ExplainMyPC.Views;

/// <summary>
/// Dashboard page — system overview with health score, live metrics,
/// trends, and quick actions.
///
/// Code-behind is intentionally thin:
///   • ViewModel is instantiated here and exposed as a public property
///     so the XAML can reach it via x:Bind.
///   • Progress bar entrance animations read their targets from the ViewModel
///     (no magic numbers in code-behind).
///   • Card hover effects delegated to AnimationHelper.
///   • No business logic lives here.
/// </summary>
public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; } = new DashboardViewModel();

    public DashboardPage()
    {
        InitializeComponent();
    }

    // Animate usage bars from 0 to their ViewModel-defined targets when the page appears.
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        AnimationHelper.AnimateProgressBar(CpuBar,  ViewModel.CpuPercent);
        AnimationHelper.AnimateProgressBar(RamBar,  ViewModel.RamPercent);
        AnimationHelper.AnimateProgressBar(GpuBar,  ViewModel.GpuPercent);
        AnimationHelper.AnimateProgressBar(DiskBar, ViewModel.DiskPercent);
    }

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
        => AnimationHelper.PointerEntered(sender);

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
        => AnimationHelper.PointerExited(sender);
}
