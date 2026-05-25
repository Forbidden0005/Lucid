using Lucid.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Lucid.Views;

/// <summary>
/// Expanded canvas-based metric chart for the InsightDetailPage investigation workspace.
///
/// Rendering pipeline (all in Redraw(), z-order bottom → top):
///   1. Grid lines          — 5 horizontal lines at equal value intervals, with labels
///   2. Baseline band       — ±1σ semi-transparent rectangle (when baseline is available)
///   3. Area fill           — Polygon from bottom of chart up to the history line
///   4. History line        — Polyline connecting downsampled metric samples (1.5 px)
///   5. "Now" indicator     — Vertical 1 px line at the current timestamp
///   6. Forecast line       — Dashed Polyline extending past "Now" (40 % alpha)
///   7. Event markers       — Vertical colored lines with short text labels
///   8. Time axis labels    — TextBlock elements at 5 evenly-spaced timestamps
///   9. Value axis labels   — TextBlock elements at each grid line
///
/// Layout margins (constants at top of Redraw):
///   Left  44 px — value axis labels
///   Right  8 px
///   Top    8 px
///   Bottom 24 px — time axis labels
///
/// Static brush palette: one SolidColorBrush per color × role allocated at
/// class init time. Zero allocations per redraw except for shape/text elements.
/// </summary>
public sealed partial class InsightDetailChart : UserControl
{
    // ── Dependency property ───────────────────────────────────────────────────

    public static readonly DependencyProperty ChartDataProperty =
        DependencyProperty.Register(
            nameof(ChartData),
            typeof(InsightDetailChartData),
            typeof(InsightDetailChart),
            new PropertyMetadata(null, OnDataChanged));

    public InsightDetailChartData? ChartData
    {
        get => (InsightDetailChartData?)GetValue(ChartDataProperty);
        set => SetValue(ChartDataProperty, value);
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((InsightDetailChart)d).Redraw();

    // ── Static brush palette ──────────────────────────────────────────────────

    // History lines (solid)
    private static readonly SolidColorBrush s_lineGood       = Mk(0xFF,  87, 214, 141);
    private static readonly SolidColorBrush s_lineMonitoring = Mk(0xFF,  78, 161, 255);
    private static readonly SolidColorBrush s_lineWarning    = Mk(0xFF, 255, 179,  71);
    private static readonly SolidColorBrush s_lineCritical   = Mk(0xFF, 255,  80,  80);

    // Area fills (16 % alpha)
    private static readonly SolidColorBrush s_fillGood       = Mk(0x28,  87, 214, 141);
    private static readonly SolidColorBrush s_fillMonitoring = Mk(0x28,  78, 161, 255);
    private static readonly SolidColorBrush s_fillWarning    = Mk(0x28, 255, 179,  71);
    private static readonly SolidColorBrush s_fillCritical   = Mk(0x28, 255,  80,  80);

    // Forecast (40 % alpha)
    private static readonly SolidColorBrush s_fcGood         = Mk(0x66,  87, 214, 141);
    private static readonly SolidColorBrush s_fcMonitoring   = Mk(0x66,  78, 161, 255);
    private static readonly SolidColorBrush s_fcWarning      = Mk(0x66, 255, 179,  71);
    private static readonly SolidColorBrush s_fcCritical     = Mk(0x66, 255,  80,  80);

    // Grid lines (subtle)
    private static readonly SolidColorBrush s_gridLine  = Mk(0x12, 255, 255, 255);

    // Axis labels
    private static readonly SolidColorBrush s_axisLabel = Mk(0x66, 200, 200, 200);

    // Baseline band
    private static readonly SolidColorBrush s_baseBand  = Mk(0x0F, 255, 255, 255);

    // "Now" indicator
    private static readonly SolidColorBrush s_nowLine   = Mk(0x40, 255, 255, 255);

    // Marker colors
    private static readonly SolidColorBrush s_markerOnset    = Mk(0x99,  78, 161, 255);
    private static readonly SolidColorBrush s_markerResolved = Mk(0x99,  87, 214, 141);
    private static readonly SolidColorBrush s_markerAction   = Mk(0x99, 255, 179,  71);
    private static readonly SolidColorBrush s_markerSession  = Mk(0x80, 180, 180, 180);

    private static SolidColorBrush Mk(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    // ── Construction ──────────────────────────────────────────────────────────

    public InsightDetailChart() => InitializeComponent();

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    // ── Core render ───────────────────────────────────────────────────────────

    private void Redraw()
    {
        ChartCanvas.Children.Clear();

        var data = ChartData;
        if (data is null || data.History.Count < 2) return;

        double w = ChartCanvas.ActualWidth;
        double h = ChartCanvas.ActualHeight;
        if (w < 10 || h < 10) return;

        // ── Layout margins ────────────────────────────────────────────────────

        const double leftM   = 44;
        const double rightM  =  8;
        const double topM    =  8;
        const double bottomM = 24;

        double cx = leftM;                   // chart area left x
        double cy = topM;                    // chart area top y
        double cw = w - leftM - rightM;     // chart area width
        double ch = h - topM  - bottomM;    // chart area height

        if (cw < 4 || ch < 4) return;

        // ── Axis range ────────────────────────────────────────────────────────

        double axisMin = data.AxisMin;
        double axisMax = data.AxisMax;
        if (axisMax - axisMin < 1) { axisMin -= 1; axisMax += 1; }

        // ── Time range ────────────────────────────────────────────────────────

        var tStart = data.History[0].Timestamp;
        var tEnd   = data.Forecast.Count > 1
            ? data.Forecast[^1].Timestamp
            : data.History[^1].Timestamp;
        double tMs = (tEnd - tStart).TotalMilliseconds;
        if (tMs < 1) return;

        // ── Coordinate helpers ────────────────────────────────────────────────

        double Px(DateTimeOffset t) =>
            cx + (t - tStart).TotalMilliseconds / tMs * cw;

        double Py(double v) =>
            cy + ch - (v - axisMin) / (axisMax - axisMin) * ch;

        // ── 1. Grid lines + value axis labels ─────────────────────────────────

        const int gridCount = 5;
        for (int gi = 0; gi <= gridCount; gi++)
        {
            double frac  = (double)gi / gridCount;
            double val   = axisMin + (axisMax - axisMin) * frac;
            double y     = Py(val);

            // Horizontal grid line
            var line = new Rectangle { Width = cw, Height = 1, Fill = s_gridLine };
            Canvas.SetLeft(line, cx);
            Canvas.SetTop(line, y);
            ChartCanvas.Children.Add(line);

            // Value label
            string labelText = data.UnitLabel == "°C"
                ? $"{val:F0}°C"
                : $"{val:F0}%";
            var lbl = MakeLabel(labelText);
            Canvas.SetLeft(lbl, 2);
            Canvas.SetTop(lbl, y - 7);
            ChartCanvas.Children.Add(lbl);
        }

        // ── 2. Baseline band ──────────────────────────────────────────────────

        if (data.BaselineMean is double bm && data.BaselineStdDev is double bs && bs > 0)
        {
            double bandTop    = Py(bm + bs);
            double bandBottom = Py(bm - bs);
            double bandH      = Math.Max(1, bandBottom - bandTop);

            var band = new Rectangle { Fill = s_baseBand, Width = cw, Height = bandH };
            Canvas.SetLeft(band, cx);
            Canvas.SetTop(band, bandTop);
            ChartCanvas.Children.Add(band);
        }

        // ── 3. Area fill ──────────────────────────────────────────────────────

        var fillPts = new PointCollection();
        fillPts.Add(new Point(Px(data.History[0].Timestamp), cy + ch));
        foreach (var pt in data.History)
            fillPts.Add(new Point(Px(pt.Timestamp), Py(pt.Value)));
        fillPts.Add(new Point(Px(data.History[^1].Timestamp), cy + ch));

        ChartCanvas.Children.Add(new Polygon
        {
            Points = fillPts,
            Fill   = FillBrush(data.ColorState),
        });

        // ── 4. History line ───────────────────────────────────────────────────

        var histPts = new PointCollection();
        foreach (var pt in data.History)
            histPts.Add(new Point(Px(pt.Timestamp), Py(pt.Value)));

        ChartCanvas.Children.Add(new Polyline
        {
            Points          = histPts,
            Stroke          = LineBrush(data.ColorState),
            StrokeThickness = 1.5,
            StrokeLineJoin  = PenLineJoin.Round,
        });

        // ── 5. "Now" indicator ────────────────────────────────────────────────

        var now = DateTimeOffset.Now;
        if (now >= tStart && now <= tEnd)
        {
            double nx = Px(now);
            var nowRect = new Rectangle
            {
                Width  = 1,
                Height = ch,
                Fill   = s_nowLine,
            };
            Canvas.SetLeft(nowRect, nx);
            Canvas.SetTop(nowRect, cy);
            ChartCanvas.Children.Add(nowRect);

            var nowLbl = MakeLabel("now", fontSize: 8);
            Canvas.SetLeft(nowLbl, Math.Max(cx, nx - 10));
            Canvas.SetTop(nowLbl, cy + ch + 2);
            ChartCanvas.Children.Add(nowLbl);
        }

        // ── 6. Forecast line (dashed) ─────────────────────────────────────────

        if (data.Forecast.Count >= 2)
        {
            var fcPts = new PointCollection();
            foreach (var pt in data.Forecast)
                fcPts.Add(new Point(Px(pt.Timestamp), Py(pt.Value)));

            ChartCanvas.Children.Add(new Polyline
            {
                Points              = fcPts,
                Stroke              = ForecastBrush(data.ColorState),
                StrokeThickness     = 1.5,
                StrokeDashArray     = new DoubleCollection { 5.0, 3.0 },
                StrokeLineJoin      = PenLineJoin.Round,
            });
        }

        // ── 7. Event markers ──────────────────────────────────────────────────

        foreach (var marker in data.Markers)
        {
            if (marker.OccurredAt < tStart || marker.OccurredAt > tEnd) continue;

            double mx = Px(marker.OccurredAt);
            var markerBrush = MarkerBrush(marker.Kind);

            var markerLine = new Rectangle
            {
                Width  = 1,
                Height = ch,
                Fill   = markerBrush,
            };
            Canvas.SetLeft(markerLine, mx);
            Canvas.SetTop(markerLine, cy);
            ChartCanvas.Children.Add(markerLine);

            // Small label above the chart area
            var mLbl = MakeLabel(marker.Label, fontSize: 8, brush: markerBrush);
            Canvas.SetLeft(mLbl, Math.Clamp(mx - 16, cx, cx + cw - 40));
            Canvas.SetTop(mLbl, cy + 2);
            ChartCanvas.Children.Add(mLbl);
        }

        // ── 8. Time axis labels ───────────────────────────────────────────────

        const int timeSteps = 5;
        for (int ti = 0; ti <= timeSteps; ti++)
        {
            double frac = (double)ti / timeSteps;
            double xPos = cx + frac * cw;
            var   t     = tStart + TimeSpan.FromMilliseconds(frac * tMs);

            var timeLbl = MakeLabel(t.ToString("HH:mm"), fontSize: 8);
            Canvas.SetLeft(timeLbl, Math.Clamp(xPos - 12, cx, cx + cw - 30));
            Canvas.SetTop(timeLbl, cy + ch + 6);
            ChartCanvas.Children.Add(timeLbl);
        }

        // ── 9. Current value dot at last history point ────────────────────────

        var last = data.History[^1];
        double dotX = Px(last.Timestamp);
        double dotY = Py(last.Value);
        const double dotR = 3;

        var dot = new Ellipse
        {
            Width  = dotR * 2,
            Height = dotR * 2,
            Fill   = LineBrush(data.ColorState),
        };
        Canvas.SetLeft(dot, dotX - dotR);
        Canvas.SetTop(dot, dotY - dotR);
        ChartCanvas.Children.Add(dot);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TextBlock MakeLabel(
        string text,
        double fontSize = 9,
        SolidColorBrush? brush = null) =>
        new()
        {
            Text       = text,
            FontSize   = fontSize,
            FontFamily = new FontFamily("Segoe UI Variable"),
            Foreground = brush ?? s_axisLabel,
        };

    // Brush selectors
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

    private static SolidColorBrush MarkerBrush(ChartMarkerKind k) => k switch
    {
        ChartMarkerKind.InsightOnset    => s_markerOnset,
        ChartMarkerKind.InsightResolved => s_markerResolved,
        ChartMarkerKind.ActionExecuted  => s_markerAction,
        ChartMarkerKind.ActionRollback  => s_markerAction,
        _                               => s_markerSession,
    };
}
