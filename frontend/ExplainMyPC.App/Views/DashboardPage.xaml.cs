using ExplainMyPC.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ExplainMyPC.Views;

/// <summary>
/// Dashboard page — system overview with health score, live metrics,
/// trends, and quick actions. All data is static placeholder for now.
///
/// Code-behind is intentionally thin:
///   • Progress bar entrance animation delegated to AnimationHelper.
///   • Card hover effects (scale + border) delegated to AnimationHelper.
///   • No business logic lives here.
/// </summary>
public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
    }

    // Animate usage bars from 0 to their target value when the page appears.
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        AnimationHelper.AnimateProgressBar(CpuBar,  23);
        AnimationHelper.AnimateProgressBar(RamBar,  61);
        AnimationHelper.AnimateProgressBar(GpuBar,  12);
        AnimationHelper.AnimateProgressBar(DiskBar, 46);
    }

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
        => AnimationHelper.PointerEntered(sender);

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
        => AnimationHelper.PointerExited(sender);
}
