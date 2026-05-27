using Lucid.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Lucid.Views;

/// <summary>
/// Lightweight WinUI 3 canvas-based sparkline control for insight cards.
///
/// Rendering pipeline (all in Redraw()):
///   1. Baseline band  — semi-transparent Rectangle spanning ±1σ of the machine norm
///   2. Area fill      — Polygon tracing the history line down to the baseline
///   3. History line   — Polyline connecting downsampled metric samples
///   4. Forecast line  — Dashed Polyline projecting the current trend forward
///
/// Static brush palette: one SolidColorBrush per color × role, allocated at
/// class init time. Zero per-frame allocations — safe to call on every tick.
///
/// Sizing: Canvas height is fixed at 64 px; width fills the parent via
/// HorizontalAlignment="Stretch". SizeChanged triggers a full Redraw().
/// </summary>
public sealed partial class SparklineControl : UserControl
{
    // ── Dependency property ───────────────────────────────────────────────────

    public static readonly DependencyProperty SparklineDataProperty =
        DependencyProperty.Register(
            nameof(SparklineData),
            typeof(SparklineData),
            typeof(SparklineControl),
            new PropertyMetadata(null, OnDataChanged));

    /// <summary>
    /// The sparkline data to render. Set via x:Bind from the insight card template.
    /// Changing this triggers a full canvas redraw.
    /// </summary>
    public SparklineData? SparklineData
    {
        get => (SparklineData?)GetValue(SparklineDataProperty);
        set => SetValue(SparklineDataProperty, value);
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SparklineControl)d).Redraw();

    // ── Static brush palette ── one allocation per color × role, ever ─────────

    // Solid history lines
    private static readonly SolidColorBrush s_lineGood       = Mk(0xFF,  87, 214, 141); // #57D68D
    private static readonly SolidColorBrush s_lineMonitoring = Mk(0xFF,  78, 161, 255); // #4EA1FF
    private static readonly SolidColorBrush s_lineWarning    = Mk(0xFF, 255, 179,  71); // #FFB347
    private static readonly SolidColorBrush s_lineCritical   = Mk(0xFF, 255,  80,  80); // #FF5050

    // Area fill (20 % alpha)
    private static readonly SolidColorBrush s_fillGood       = Mk(0x33,  87, 214, 141);
    private static readonly SolidColorBrush s_fillMonitoring = Mk(0x33,  78, 161, 255);
    private static readonly SolidColorBrush s_fillWarning    = Mk(0x33, 255, 179,  71);
    private static readonly SolidColorBrush s_fillCritical   = Mk(0x33, 255,  80,  80);

    // Forecast lines (40 % alpha — visually distinct from history)
    private static readonly SolidColorBrush s_fcGood         = Mk(0x66,  87, 214, 141);
    private static readonly SolidColorBrush s_fcMonitoring   = Mk(0x66,  78, 161, 255);
    private static readonly SolidColorBrush s_fcWarning      = Mk(0x66, 255, 179,  71);
    private static readonly SolidColorBrush s_fcCritical     = Mk(0x66, 255,  80,  80);

    // Baseline band (5 % white tint — very subtle reference)
    private static readonly SolidColorBrush s_baselineBand   = Mk(0x0D, 255, 255, 255);

    private static SolidColorBrush Mk(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    // ── Construction ──────────────────────────────────────────────────────────

    public SparklineControl()
    {
        InitializeComponent();
    }

    // ── Resize trigger ────────────────────────────────────────────────────────

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    // ── Core rendering ────────────────────────────────────────────────────────

    private void Redraw()
    {
        SparkCanvas.Children.Clear();

        var data = SparklineData;
        if (data is null || data.History.Count < 2)
        {
            TrendText.Text = string.Empty;
            return;
        }

        double w = SparkCanvas.ActualWidth;
        double h = SparkCanvas.ActualHeight;

        if (w < 4 || h < 4) return;

        // ── Value range ───────────────────────────────────────────────────────
        // Include history, forecast, and baseline in the range so nothing clips.

        double minVal = double.MaxValue;
        double maxVal = double.MinValue;

        foreach (var pt in data.History)  { minVal = Math.Min(minVal, pt.Value); maxVal = Math.Max(maxVal, pt.Value); }
        foreach (var pt in data.Forecast) { minVal = Math.Min(minVal, pt.Value); maxVal = Math.Max(maxVal, pt.Value); }

        if (data.BaselineMean is double bm && data.BaselineStdDev is double bs && bs > 0)
        {
            minVal = Math.Min(minVal, bm - bs);
            maxVal = Math.Max(maxVal, bm + bs);
        }

        // Minimum 2-unit range prevents degenerate flat lines.
        if (maxVal - minVal < 2)
        {
            double center = (maxVal + minVal) / 2.0;
            minVal = center - 1;
            maxVal = center + 1;
        }

        // ── Time range ────────────────────────────────────────────────────────
        // Extend to the end of the forecast window so forecast points render correctly.

        var tStart  = data.History[0].Timestamp;
        var tEnd    = data.Forecast.Count > 1 ? data.Forecast[^1].Timestamp
                                              : data.History[^1].Timestamp;
        double tMs  = (tEnd - tStart).TotalMilliseconds;
        if (tMs < 1) return;

        // ── Coordinate helpers ────────────────────────────────────────────────
        // 5 % vertical padding so the line is never flush against top/bottom edge.

        double Px(DateTimeOffset t) =>
            (t - tStart).TotalMilliseconds / tMs * w;

        double Py(double v) =>
            h - ((v - minVal) / (maxVal - minVal) * h * 0.90 + h * 0.05);

        // ── 1. Baseline band ──────────────────────────────────────────────────

        if (data.BaselineMean is double mean && data.BaselineStdDev is double stdDev && stdDev > 0)
        {
            double bandTop    = Py(mean + stdDev);
            double bandBottom = Py(mean - stdDev);
            double bandHeight = Math.Max(1, bandBottom - bandTop);

            var band = new Rectangle { Fill = s_baselineBand, Width = w, Height = bandHeight };
            Canvas.SetLeft(band, 0);
            Canvas.SetTop(band, bandTop);
            SparkCanvas.Children.Add(band);
        }

        // ── 2. Area fill ──────────────────────────────────────────────────────

        var fillPts = new PointCollection();
        fillPts.Add(new Point(Px(data.History[0].Timestamp), h));
        foreach (var pt in data.History)
            fillPts.Add(new Point(Px(pt.Timestamp), Py(pt.Value)));
        fillPts.Add(new Point(Px(data.History[^1].Timestamp), h));

        SparkCanvas.Children.Add(new Polygon
        {
            Points = fillPts,
            Fill   = FillBrush(data.ColorState),
        });

        // ── 3. History line ───────────────────────────────────────────────────

        var histPts = new PointCollection();
        foreach (var pt in data.History)
            histPts.Add(new Point(Px(pt.Timestamp), Py(pt.Value)));

        SparkCanvas.Children.Add(new Polyline
        {
            Points          = histPts,
            Stroke          = LineBrush(data.ColorState),
            StrokeThickness = 1.5,
            StrokeLineJoin  = PenLineJoin.Round,
        });

        // ── 4. Forecast line (dashed) ─────────────────────────────────────────

        if (data.Forecast.Count >= 2)
        {
            var fcPts = new PointCollection();
            foreach (var pt in data.Forecast)
                fcPts.Add(new Point(Px(pt.Timestamp), Py(pt.Value)));

            SparkCanvas.Children.Add(new Polyline
            {
                Points              = fcPts,
                Stroke              = ForecastBrush(data.ColorState),
                StrokeThickness     = 1.5,
                StrokeDashArray     = new DoubleCollection { 4.0, 3.0 },
                StrokeLineJoin      = PenLineJoin.Round,
            });
        }

        // ── Trend summary text ────────────────────────────────────────────────

        TrendText.Text = data.TrendSummary;
    }

    // ── Brush selectors ───────────────────────────────────────────────────────

    private static SolidColorBrush LineBrush(SparklineColorState s) => s switch
    {
        SparklineColorState.Good     => s_lineGood,
        SparklineColorState.Warning  => s_lineWarning,
        SparklineColorState.Critical => s_lineCritical,
        _                            => s_lineMonitoring,
    };

    private static SolidColorBrush FillBrush(SparklineColorState s) => s switch
    {
        SparklineColorState.Good     => s_fillGood,
        SparklineColorState.Warning  => s_fillWarning,
        SparklineColorState.Critical => s_fillCritical,
        _                            => s_fillMonitoring,
    };

    private static SolidColorBrush ForecastBrush(SparklineColorState s) => s switch
    {
        SparklineColorState.Good     => s_fcGood,
        SparklineColorState.Warning  => s_fcWarning,
        SparklineColorState.Critical => s_fcCritical,
        _                            => s_fcMonitoring,
    };
}
