using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace ExplainMyPC.Controls;

/// <summary>
/// A lightweight real-time line graph for telemetry data.
///
/// Usage in XAML:
///   &lt;controls:TelemetryGraph
///       DataPoints="{x:Bind ViewModel.CpuHistory, Mode=OneWay}"
///       GraphColor="{StaticResource AccentBlueColor}"
///       MaxValue="100"
///       Height="56" /&gt;
///
/// Architecture:
///   • Two Canvas children built in the constructor (fill polygon + line polyline).
///   • Re-rendered whenever DataPoints changes or the control is resized.
///   • No animation between ticks — the 1-second update cadence looks alive
///     because the data shifts left each frame (60-point rolling window).
/// </summary>
public sealed partial class TelemetryGraph : UserControl
{
    // ── Visual children ──────────────────────────────────────────────────────
    private readonly Path     _fillPath;
    private readonly Polyline _graphLine;

    // ── Dependency Properties ─────────────────────────────────────────────────

    public static readonly DependencyProperty DataPointsProperty =
        DependencyProperty.Register(
            nameof(DataPoints),
            typeof(IReadOnlyList<double>),
            typeof(TelemetryGraph),
            new PropertyMetadata(null, (d, _) => ((TelemetryGraph)d).Render()));

    public static readonly DependencyProperty GraphColorProperty =
        DependencyProperty.Register(
            nameof(GraphColor),
            typeof(Color),
            typeof(TelemetryGraph),
            new PropertyMetadata(Color.FromArgb(255, 78, 161, 255), // AccentBlue
                (d, _) => ((TelemetryGraph)d).ApplyColor()));

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(
            nameof(MaxValue),
            typeof(double),
            typeof(TelemetryGraph),
            new PropertyMetadata(100.0, (d, _) => ((TelemetryGraph)d).Render()));

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

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public TelemetryGraph()
    {
        InitializeComponent();

        // Fill polygon — drawn first so the line renders on top.
        _fillPath = new Path { Opacity = 0.18 };
        RootCanvas.Children.Add(_fillPath);

        // Stroke line.
        _graphLine = new Polyline
        {
            StrokeThickness   = 1.8,
            StrokeLineJoin    = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        };
        RootCanvas.Children.Add(_graphLine);

        ApplyColor();
        Loaded += (_, _) => Render();
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private void Render()
    {
        var pts = DataPoints;
        if (pts is null || pts.Count < 2) return;

        var w = RootCanvas.ActualWidth;
        var h = RootCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var max    = MaxValue > 0 ? MaxValue : 100.0;
        var xStep  = w / (pts.Count - 1);

        // Leave a small top/bottom margin so the line is never clipped.
        const double topPad    = 0.04;
        const double bottomPad = 0.04;
        var drawH = h * (1.0 - topPad - bottomPad);

        // Screen coordinates for each data point.
        var screenPts = new Point[pts.Count];
        for (int i = 0; i < pts.Count; i++)
        {
            var normalised = Math.Clamp(pts[i] / max, 0.0, 1.0);
            screenPts[i] = new Point(
                i * xStep,
                h * topPad + drawH * (1.0 - normalised));
        }

        // ── Update line ───────────────────────────────────────────────────────
        var linePoints = new PointCollection();
        foreach (var p in screenPts) linePoints.Add(p);
        _graphLine.Points = linePoints;

        // ── Update fill (closed polygon: bottom-left → line → bottom-right) ──
        var figure = new PathFigure
        {
            IsClosed     = true,
            IsFilled     = true,
            StartPoint   = new Point(0, h),
        };
        figure.Segments.Add(new LineSegment { Point = screenPts[0] });
        for (int i = 1; i < screenPts.Length; i++)
            figure.Segments.Add(new LineSegment { Point = screenPts[i] });
        figure.Segments.Add(new LineSegment { Point = new Point(screenPts[^1].X, h) });

        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        _fillPath.Data = geo;
    }

    private void ApplyColor()
    {
        var color = GraphColor;

        _graphLine.Stroke = new SolidColorBrush(color);

        var grad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint   = new Point(0, 1),
        };
        grad.GradientStops.Add(new GradientStop { Color = color,         Offset = 0.0 });
        grad.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 1.0 });
        _fillPath.Fill = grad;
    }

    // ── Canvas resize ─────────────────────────────────────────────────────────

    private void RootCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Render();
}
