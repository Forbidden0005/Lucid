using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ExplainMyPC.Views;

/// <summary>
/// Dashboard page — system overview with health score, metrics, trends,
/// and quick actions. All data is static placeholder (mock) for now.
///
/// Code-behind handles:
///   • Card hover effect (subtle scale + border brighten)
///   • Progress bar entrance animation (bars fill on page load)
/// </summary>
public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
    }

    // ════════════════════════════════════════════════════════════════════
    // Progress bar entrance animation
    //
    // On page load, the bars animate from 0 to their target value
    // so they visually "fill in" instead of appearing fully filled.
    // ════════════════════════════════════════════════════════════════════

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        AnimateProgressBar(CpuBar, 23);
        AnimateProgressBar(RamBar, 61);
        AnimateProgressBar(GpuBar, 12);
        AnimateProgressBar(DiskBar, 46);
    }

    /// <summary>
    /// Animates a ProgressBar from 0 to the target value over 600ms
    /// using a smooth ease-out curve.
    /// </summary>
    private static void AnimateProgressBar(ProgressBar bar, double targetValue)
    {
        bar.Value = 0;

        var animation = new DoubleAnimation
        {
            From = 0,
            To = targetValue,
            Duration = new Duration(TimeSpan.FromMilliseconds(600)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);

        Storyboard.SetTarget(animation, bar);
        Storyboard.SetTargetProperty(animation, "Value");

        storyboard.Begin();
    }

    // ════════════════════════════════════════════════════════════════════
    // Card hover effect
    //
    // When the pointer enters a card, it scales up slightly (1.01x)
    // and the border brightens. On exit, it returns to normal.
    // ════════════════════════════════════════════════════════════════════

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border card) return;

        // Scale up slightly
        if (card.RenderTransform is CompositeTransform transform)
        {
            AnimateScale(card, transform, 1.01);
        }

        // Brighten border
        card.BorderBrush = new SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(80, 78, 161, 255)); // AccentBlue at ~30%
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border card) return;

        // Scale back to normal
        if (card.RenderTransform is CompositeTransform transform)
        {
            AnimateScale(card, transform, 1.0);
        }

        // Restore subtle border
        card.BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderSubtleBrush"];
    }

    /// <summary>
    /// Animates the ScaleX and ScaleY of a CompositeTransform
    /// to the target value over 150ms with ease-out.
    /// </summary>
    private static void AnimateScale(Border card, CompositeTransform transform, double targetScale)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(150));

        var scaleX = new DoubleAnimation
        {
            To = targetScale,
            Duration = duration,
            EasingFunction = ease,
            EnableDependentAnimation = true
        };

        var scaleY = new DoubleAnimation
        {
            To = targetScale,
            Duration = duration,
            EasingFunction = ease,
            EnableDependentAnimation = true
        };

        var sb = new Storyboard();
        sb.Children.Add(scaleX);
        sb.Children.Add(scaleY);

        Storyboard.SetTarget(scaleX, transform);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");

        Storyboard.SetTarget(scaleY, transform);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");

        sb.Begin();
    }
}
