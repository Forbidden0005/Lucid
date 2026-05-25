using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace Lucid.Controls;

/// <summary>
/// Circular health-score ring with an animated arc and colour-coded status.
///
/// Technique: StrokeDashArray + StrokeDashOffset on an Ellipse.
///   StrokeDashArray = [circumference, circumference]  → one full dash + one full gap
///   StrokeDashOffset controls how much of the dash is hidden (0 = full ring shown).
///   A RotateTransform(-90°) on the Ellipse starts the arc from the top.
///
/// Animation: DoubleAnimation on StrokeDashOffset fires whenever Score changes,
/// giving a smooth draw-in effect at 700 ms with a cubic ease-out.
///
/// Dependency Properties:
///   Score (int 0-100)  — the numeric score driving the arc
///   Label (string)     — text shown beneath the score number
/// </summary>
public sealed partial class HealthScoreRing : UserControl
{
    private double _circumference;
    private Storyboard? _currentAnimation;

    // ── Dependency Properties ─────────────────────────────────────────────────

    public static readonly DependencyProperty ScoreProperty =
        DependencyProperty.Register(
            nameof(Score),
            typeof(int),
            typeof(HealthScoreRing),
            new PropertyMetadata(0, OnScoreChanged));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(
            nameof(Label),
            typeof(string),
            typeof(HealthScoreRing),
            new PropertyMetadata(string.Empty, OnLabelChanged));

    public int Score
    {
        get => (int)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public HealthScoreRing()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ComputeCircumference();
        // Start the arc at empty (full offset) then animate in.
        ScoreEllipse.StrokeDashOffset = _circumference;
        RefreshText();
        RefreshColor();
        AnimateArcTo(TargetOffset());
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ComputeCircumference();
        // Snap to current value without animation on resize.
        if (_circumference > 0)
        {
            ScoreEllipse.StrokeDashArray = new DoubleCollection { _circumference, _circumference };
            ScoreEllipse.StrokeDashOffset = TargetOffset();
        }
    }

    // ── DP Callbacks ──────────────────────────────────────────────────────────

    private static void OnScoreChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ring = (HealthScoreRing)d;
        ring.RefreshText();
        ring.RefreshColor();
        if (ring._circumference > 0)
            ring.AnimateArcTo(ring.TargetOffset());
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((HealthScoreRing)d).LabelText.Text = (string)e.NewValue;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ComputeCircumference()
    {
        const double margin = 8.0;
        const double strokeThickness = 10.0;
        var diameter   = Math.Min(ActualWidth, ActualHeight) - (margin * 2) - strokeThickness;
        var radius     = diameter / 2.0;
        _circumference = 2.0 * Math.PI * radius;

        if (_circumference > 0)
            ScoreEllipse.StrokeDashArray = new DoubleCollection { _circumference, _circumference };
    }

    /// <summary>
    /// StrokeDashOffset where 0 = full ring, circumference = empty ring.
    /// </summary>
    private double TargetOffset() =>
        _circumference * (1.0 - Math.Clamp(Score, 0, 100) / 100.0);

    private void RefreshText()
    {
        ScoreText.Text = Score.ToString();
        LabelText.Text = Label;
    }

    private void RefreshColor()
    {
        ScoreEllipse.Stroke = new SolidColorBrush(ScoreToColor(Score));
    }

    private void AnimateArcTo(double targetOffset)
    {
        _currentAnimation?.Stop();

        var sb = new Storyboard();
        var anim = new DoubleAnimation
        {
            To             = targetOffset,
            Duration       = new Duration(TimeSpan.FromMilliseconds(700)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(anim, ScoreEllipse);
        Storyboard.SetTargetProperty(anim, "StrokeDashOffset");
        sb.Children.Add(anim);

        _currentAnimation = sb;
        sb.Begin();
    }

    // Maps score ranges to the UI-guidelines status colours.
    private static Color ScoreToColor(int score) => score switch
    {
        >= 75 => Color.FromArgb(255, 87,  214, 141),   // #57D68D — Success
        >= 55 => Color.FromArgb(255, 255, 179, 71),    // #FFB347 — Warning
        _     => Color.FromArgb(255, 255, 107, 107),   // #FF6B6B — Danger
    };
}
