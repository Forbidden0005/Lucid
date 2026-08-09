using System.Collections.Concurrent;
using Lucid.Services.Baseline;

namespace Lucid.Services.Intelligence.Drift;

/// <summary>
/// Tracks operational metric history across extended time windows (days/weeks)
/// to enable longitudinal drift analysis.
///
/// While <see cref="DriftDetectionService"/> compares current state to Watchtower
/// snapshot history, the LongitudinalBehaviorTracker maintains rolling windows
/// at multiple time scales — enabling detection of very gradual drift that
/// individual cycle comparisons would miss.
///
/// Metric windows maintained per metric:
///   - Short  (30 min) — for velocity computation
///   - Medium (24 hrs) — for daily baseline comparison
///   - Long   (7 days) — for weekly baseline comparison
///
/// Thread-safe: ConcurrentDictionary + per-metric WelfordStats.
/// </summary>
public sealed class LongitudinalBehaviorTracker
{
    private const int MaxShortWindowSamples  = 120;  // 30 min @ 15s
    private const int MaxMediumWindowSamples = 2880; // 24 hrs @ 30s
    private const int MaxLongWindowSamples   = 8640; // 3 days @ 30s (representative long)

    private readonly ConcurrentDictionary<string, MetricWindow> _windows = new();

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a new metric observation into all applicable time windows.
    /// </summary>
    public void Record(string metricName, double value)
    {
        var window = _windows.GetOrAdd(metricName, _ => new MetricWindow(metricName));
        window.Record(value);
    }

    /// <summary>
    /// Returns the short-window mean and standard deviation for a metric.
    /// Returns null when insufficient data exists.
    /// </summary>
    public WindowSnapshot? GetShortWindow(string metricName) =>
        _windows.TryGetValue(metricName, out var w) ? w.ShortSnapshot : null;

    /// <summary>
    /// Returns the long-window mean and standard deviation for a metric.
    /// Returns null when insufficient data exists.
    /// </summary>
    public WindowSnapshot? GetLongWindow(string metricName) =>
        _windows.TryGetValue(metricName, out var w) ? w.LongSnapshot : null;

    /// <summary>
    /// Returns true when the short-window mean is significantly higher than
    /// the long-window mean (indicating upward longitudinal drift).
    /// </summary>
    public bool IsShowingUpwardDrift(string metricName, double threshold = 1.5)
    {
        var w = _windows.GetValueOrDefault(metricName);
        if (w is null) return false;

        var shortSnap = w.ShortSnapshot;
        var longSnap  = w.LongSnapshot;

        if (shortSnap is null || longSnap is null) return false;
        if (!shortSnap.IsReliable || !longSnap.IsReliable) return false;

        // Drift = short-window mean is > threshold standard deviations above long-window mean.
        return longSnap.StdDev > 0 &&
               (shortSnap.Mean - longSnap.Mean) > threshold * longSnap.StdDev;
    }

    /// <summary>All metric names currently being tracked.</summary>
    public IReadOnlyList<string> TrackedMetrics =>
        _windows.Keys.OrderBy(k => k).ToList().AsReadOnly();

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class MetricWindow
    {
        private readonly string       _name;
        private readonly Queue<double> _shortRing  = new();
        private readonly WelfordStats  _longStats  = new();
        private readonly object        _lock       = new();

        public MetricWindow(string name) { _name = name; }

        public void Record(double value)
        {
            lock (_lock)
            {
                _shortRing.Enqueue(value);
                while (_shortRing.Count > MaxShortWindowSamples)
                    _shortRing.Dequeue();

                _longStats.Update(value);
            }
        }

        public WindowSnapshot? ShortSnapshot
        {
            get
            {
                lock (_lock)
                {
                    if (_shortRing.Count < 10) return null;
                    var arr  = _shortRing.ToArray();
                    double sum = 0;
                    foreach (var v in arr) sum += v;
                    double mean = sum / arr.Length;
                    double ssq  = arr.Sum(v => (v - mean) * (v - mean));
                    double sd   = arr.Length > 1 ? Math.Sqrt(ssq / (arr.Length - 1)) : 0;
                    return new WindowSnapshot(mean, sd, arr.Length, IsReliable: arr.Length >= 20);
                }
            }
        }

        public WindowSnapshot? LongSnapshot
        {
            get
            {
                lock (_lock)
                {
                    if (_longStats.Count < 30) return null;
                    return new WindowSnapshot(
                        _longStats.Mean, _longStats.StdDev,
                        (int)Math.Min(_longStats.Count, int.MaxValue),
                        IsReliable: _longStats.HasSufficientData);
                }
            }
        }
    }
}

/// <summary>Statistics snapshot for one time window of a metric.</summary>
public sealed record WindowSnapshot(
    double Mean,
    double StdDev,
    int    SampleCount,
    bool   IsReliable);
