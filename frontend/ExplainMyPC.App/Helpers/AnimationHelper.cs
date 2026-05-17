using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ExplainMyPC.Helpers;

/// <summary>
/// Shared animation utilities used across all ExplainMyPC pages.
///
/// Centralises three repeated patterns:
///   • Card hover — subtle scale-up + border brighten on PointerEnter,
///     scale-back + border restore on PointerExit.
///   • Card entrance — staggered slide-up + fade-in when a page loads.
///   • Progress bar fill — animates a bar from 0 to a target value.
///
/// Pages delegate to these methods from thin event-handler wrappers so
/// the code-behind files stay small and the animation logic lives in
/// one place.
///
/// ⚠️  CompositeTransform rule:
///     Each animated Border MUST have its OWN inline transform:
///         <Border.RenderTransform><CompositeTransform /></Border.RenderTransform>
///     WinUI 3 Style Setter values are shared instances — putting
///     CompositeTransform in a Style would make all cards animate
///     simultaneously from a single shared object.
/// </summary>
public static class AnimationHelper
{
    // ════════════════════════════════════════════════════════════════════
    // Hover effects
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scale the card up slightly and brighten its border.
    /// Call from <c>Card_PointerEntered</c>.
    /// </summary>
    public static void PointerEntered(object sender)
    {
        if (sender is not Border card) return;

        if (card.RenderTransform is CompositeTransform t)
            AnimateScale(t, 1.01);

        // Brighten the border to AccentBlue at ~27% opacity
        card.BorderBrush = new SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(70, 78, 161, 255));
    }

    /// <summary>
    /// Scale the card back to normal and restore its default border.
    /// Call from <c>Card_PointerExited</c>.
    /// </summary>
    public static void PointerExited(object sender)
    {
        if (sender is not Border card) return;

        if (card.RenderTransform is CompositeTransform t)
            AnimateScale(t, 1.0);

        card.BorderBrush =
            (SolidColorBrush)Application.Current.Resources["BorderSubtleBrush"];
    }

    // ════════════════════════════════════════════════════════════════════
    // Card entrance animation
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Slides the card up 18 px from below and fades it in from transparent,
    /// starting after <paramref name="delayMs"/> milliseconds.
    /// Requires the Border to have an inline <c>CompositeTransform</c>.
    /// </summary>
    public static void AnimateEntrance(Border card, int delayMs)
    {
        card.Opacity = 0;

        var delay    = TimeSpan.FromMilliseconds(delayMs);
        var duration = new Duration(TimeSpan.FromMilliseconds(320));
        var ease     = new CubicEase { EasingMode = EasingMode.EaseOut };

        var sb = new Storyboard();

        // Fade in: 0 → 1
        var fade = new DoubleAnimation
        {
            From = 0, To = 1,
            Duration = duration, EasingFunction = ease,
            BeginTime = delay, EnableDependentAnimation = true
        };
        sb.Children.Add(fade);
        Storyboard.SetTarget(fade, card);
        Storyboard.SetTargetProperty(fade, "Opacity");

        // Slide up: TranslateY 18 → 0 (only when a CompositeTransform is present)
        if (card.RenderTransform is CompositeTransform ct)
        {
            ct.TranslateY = 18;

            var slide = new DoubleAnimation
            {
                From = 18, To = 0,
                Duration = duration, EasingFunction = ease,
                BeginTime = delay, EnableDependentAnimation = true
            };
            sb.Children.Add(slide);
            Storyboard.SetTarget(slide, ct);
            Storyboard.SetTargetProperty(slide, "TranslateY");
        }

        sb.Begin();
    }

    // ════════════════════════════════════════════════════════════════════
    // Progress bar fill animation
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Animates a ProgressBar value from 0 to <paramref name="targetValue"/>
    /// over <paramref name="durationMs"/> ms with an ease-out curve,
    /// starting after <paramref name="delayMs"/> ms.
    /// </summary>
    public static void AnimateProgressBar(
        ProgressBar bar,
        double targetValue,
        int delayMs    = 0,
        int durationMs = 600)
    {
        bar.Value = 0;

        var animation = new DoubleAnimation
        {
            From           = 0,
            To             = targetValue,
            Duration       = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            BeginTime      = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };

        var sb = new Storyboard();
        sb.Children.Add(animation);
        Storyboard.SetTarget(animation, bar);
        Storyboard.SetTargetProperty(animation, "Value");
        sb.Begin();
    }

    // ════════════════════════════════════════════════════════════════════
    // Internal helpers
    // ════════════════════════════════════════════════════════════════════

    private static void AnimateScale(CompositeTransform transform, double to)
    {
        var ease     = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(150));

        var sx = new DoubleAnimation
        {
            To = to, Duration = duration,
            EasingFunction = ease, EnableDependentAnimation = true
        };
        var sy = new DoubleAnimation
        {
            To = to, Duration = duration,
            EasingFunction = ease, EnableDependentAnimation = true
        };

        var sb = new Storyboard();
        sb.Children.Add(sx);
        sb.Children.Add(sy);

        Storyboard.SetTarget(sx, transform);
        Storyboard.SetTargetProperty(sx, "ScaleX");
        Storyboard.SetTarget(sy, transform);
        Storyboard.SetTargetProperty(sy, "ScaleY");

        sb.Begin();
    }
}
