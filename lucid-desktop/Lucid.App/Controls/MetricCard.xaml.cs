using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.Controls;

/// <summary>
/// Reusable telemetry card: icon + title + value + detail text + live graph.
///
/// Usage in DashboardPage.xaml:
///   &lt;controls:MetricCard
///       Title="CPU"
///       IconGlyph="&#xE9F5;"
///       Value="{x:Bind ViewModel.CpuDisplay, Mode=OneWay}"
///       DetailText="{x:Bind ViewModel.CpuDetail, Mode=OneWay}"
///       DataPoints="{x:Bind ViewModel.CpuHistory, Mode=OneWay}"
///       GraphColor="{StaticResource AccentBlueColor}"
///       ValuePercent="{x:Bind ViewModel.CpuPercent, Mode=OneWay}"
///       WarningThreshold="70"
///       CriticalThreshold="90" /&gt;
///
/// The status dot colour is derived from ValuePercent vs the two thresholds:
///   Normal   (below warning)   → #57D68D (green)
///   Warning  (below critical)  → #FFB347 (orange)
///   Critical (at or above)     → #FF6B6B (red)
/// </summary>
public sealed partial class MetricCard : UserControl
{
    // ── Dependency Properties ─────────────────────────────────────────────────

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(MetricCard),
            new PropertyMetadata(string.Empty, (d, e) =>
                ((MetricCard)d).TitleText.Text = (string)e.NewValue));

    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(MetricCard),
            new PropertyMetadata(string.Empty, (d, e) =>
                ((MetricCard)d).TitleIcon.Glyph = (string)e.NewValue));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(MetricCard),
            new PropertyMetadata(string.Empty, (d, e) =>
                ((MetricCard)d).ValueText.Text = (string)e.NewValue));

    public static readonly DependencyProperty DetailTextProperty =
        DependencyProperty.Register(nameof(DetailText), typeof(string), typeof(MetricCard),
            new PropertyMetadata(string.Empty, (d, e) =>
                ((MetricCard)d).DetailText.Text = (string)e.NewValue));

    public static readonly DependencyProperty DataPointsProperty =
        DependencyProperty.Register(nameof(DataPoints), typeof(IReadOnlyList<double>), typeof(MetricCard),
            new PropertyMetadata(null, (d, e) =>
                ((MetricCard)d).Graph.DataPoints = (IReadOnlyList<double>?)e.NewValue));

    public static readonly DependencyProperty GraphColorProperty =
        DependencyProperty.Register(nameof(GraphColor), typeof(Color), typeof(MetricCard),
            new PropertyMetadata(Color.FromArgb(255, 78, 161, 255), (d, e) =>
            {
                var card = (MetricCard)d;
                var color = (Color)e.NewValue;
                card.Graph.GraphColor         = color;
                card.TitleIcon.Foreground     = new SolidColorBrush(color);
                card.ValueText.Foreground     = new SolidColorBrush(color);
            }));

    public static readonly DependencyProperty ValuePercentProperty =
        DependencyProperty.Register(nameof(ValuePercent), typeof(double), typeof(MetricCard),
            new PropertyMetadata(0.0, (d, e) =>
                ((MetricCard)d).UpdateStatusDot((double)e.NewValue)));

    public static readonly DependencyProperty MaxGraphValueProperty =
        DependencyProperty.Register(nameof(MaxGraphValue), typeof(double), typeof(MetricCard),
            new PropertyMetadata(100.0, (d, e) =>
                ((MetricCard)d).Graph.MaxValue = (double)e.NewValue));

    public static readonly DependencyProperty WarningThresholdProperty =
        DependencyProperty.Register(nameof(WarningThreshold), typeof(double), typeof(MetricCard),
            new PropertyMetadata(70.0, (d, e) =>
                ((MetricCard)d).UpdateStatusDot(((MetricCard)d).ValuePercent)));

    public static readonly DependencyProperty CriticalThresholdProperty =
        DependencyProperty.Register(nameof(CriticalThreshold), typeof(double), typeof(MetricCard),
            new PropertyMetadata(90.0, (d, e) =>
                ((MetricCard)d).UpdateStatusDot(((MetricCard)d).ValuePercent)));

    // ── CLR Wrappers ──────────────────────────────────────────────────────────

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
    public string DetailText
    {
        get => (string)GetValue(DetailTextProperty);
        set => SetValue(DetailTextProperty, value);
    }
    public IReadOnlyList<double>? DataPoints
    {
        get => (IReadOnlyList<double>?)GetValue(DataPointsProperty);
        set => SetValue(DataPointsProperty, value);
    }
    public Color GraphColor
    {
        get => (Color)GetValue(GraphColorProperty);
        set => SetValue(GraphColorProperty, value);
    }
    public double ValuePercent
    {
        get => (double)GetValue(ValuePercentProperty);
        set => SetValue(ValuePercentProperty, value);
    }
    public double MaxGraphValue
    {
        get => (double)GetValue(MaxGraphValueProperty);
        set => SetValue(MaxGraphValueProperty, value);
    }
    public double WarningThreshold
    {
        get => (double)GetValue(WarningThresholdProperty);
        set => SetValue(WarningThresholdProperty, value);
    }
    public double CriticalThreshold
    {
        get => (double)GetValue(CriticalThresholdProperty);
        set => SetValue(CriticalThresholdProperty, value);
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public MetricCard()
    {
        InitializeComponent();
        UpdateStatusDot(0);
    }

    // ── Status dot logic ──────────────────────────────────────────────────────

    private void UpdateStatusDot(double percent)
    {
        Color dotColor = percent >= CriticalThreshold
            ? Color.FromArgb(255, 255, 107, 107)    // #FF6B6B — Danger
            : percent >= WarningThreshold
                ? Color.FromArgb(255, 255, 179, 71)  // #FFB347 — Warning
                : Color.FromArgb(255, 87, 214, 141); // #57D68D — Success

        StatusDot.Fill = new SolidColorBrush(dotColor);
    }
}
