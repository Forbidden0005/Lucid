using System.Collections.Concurrent;
using Lucid.Services.Baseline;

namespace Lucid.Services.Intelligence.Baselines;

/// <summary>
/// Maintains per-workload-type adaptive baseline profiles for key system metrics.
///
/// While <see cref="ISystemBaselineService"/> tracks machine-wide statistics,
/// the AdaptiveBaselineTracker segments baselines by workload context — allowing
/// Lucid to understand "CPU at 80% is normal during gaming, unusual when idle."
///
/// Supported metrics: CpuPercent, RamPercent, CpuTemperatureCelsius.
///
/// Adaptation is conservative:
///   - Baselines only form with ≥ 50 samples per workload context
///   - Outliers (Z-score > 3) are excluded from baseline updates
///   - Baselines never retroactively reset without explicit user action
///
/// Thread-safe: ConcurrentDictionary per metric + workload key.
/// </summary>
public sealed class AdaptiveBaselineTracker
{
    // Key: "{MetricName}:{WorkloadLabel}" → WelfordStats
    private readonly ConcurrentDictionary<string, WelfordStats> _stats = new();

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Incorporates a new metric observation into the appropriate workload baseline.
    /// Outliers are silently rejected to keep baselines stable.
    /// </summary>
    public void Record(string metricName, double value, string workloadLabel)
    {
        if (string.IsNullOrWhiteSpace(metricName) || string.IsNullOrWhiteSpace(workloadLabel))
            return;

        var key    = BuildKey(metricName, workloadLabel);
        var stats  = _stats.GetOrAdd(key, _ => new WelfordStats());

        // Outlier rejection: if the baseline exists and the value is > 3σ away, skip it.
        if (stats.HasSufficientData && Math.Abs(stats.ZScore(value)) > 3.0)
            return;

        stats.Update(value);
    }

    /// <summary>
    /// Returns the baseline profile for a specific metric + workload combination.
    /// Returns null when no baseline has been established.
    /// </summary>
    public WorkloadBaselineProfile? GetProfile(string metricName, string workloadLabel)
    {
        var key = BuildKey(metricName, workloadLabel);
        if (!_stats.TryGetValue(key, out var stats) || stats.Count == 0)
            return null;

        return new WorkloadBaselineProfile
        {
            WorkloadLabel  = workloadLabel,
            MetricName     = metricName,
            SampleCount    = stats.Count,
            Mean           = stats.Mean,
            StdDev         = stats.StdDev,
            LastUpdatedAt  = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Classifies a metric value against the workload baseline and returns
    /// a normality result explaining how it compares.
    /// </summary>
    public OperationalNormResult Classify(
        string metricName,
        double value,
        string workloadLabel)
    {
        var key = BuildKey(metricName, workloadLabel);
        if (!_stats.TryGetValue(key, out var stats) || !stats.HasSufficientData)
        {
            return new OperationalNormResult
            {
                Value              = value,
                BaselineMean       = 0,
                BaselineStdDev     = 0,
                ZScore             = 0,
                Classification     = NormClassification.UnknownBaseline,
                IsBaselineReliable = false,
            };
        }

        var zScore = stats.ZScore(value);
        var classification = zScore switch
        {
            >= 3.0 => NormClassification.ElevatedHigh,
            >= 2.0 => NormClassification.ElevatedModerate,
            <= -2.0 => NormClassification.Suppressed,
            _       => NormClassification.Normal,
        };

        return new OperationalNormResult
        {
            Value              = value,
            BaselineMean       = stats.Mean,
            BaselineStdDev     = stats.StdDev,
            ZScore             = zScore,
            Classification     = classification,
            IsBaselineReliable = true,
        };
    }

    /// <summary>Returns all established profiles (those with reliable baselines).</summary>
    public IReadOnlyList<WorkloadBaselineProfile> GetAllEstablishedProfiles()
    {
        return _stats
            .Where(kvp => kvp.Value.HasSufficientData)
            .Select(kvp =>
            {
                var parts   = kvp.Key.Split(':', 2);
                var metric  = parts.Length > 0 ? parts[0] : kvp.Key;
                var wrkload = parts.Length > 1 ? parts[1] : "Unknown";
                var stats   = kvp.Value;

                return new WorkloadBaselineProfile
                {
                    MetricName    = metric,
                    WorkloadLabel = wrkload,
                    SampleCount   = stats.Count,
                    Mean          = stats.Mean,
                    StdDev        = stats.StdDev,
                    LastUpdatedAt = DateTime.UtcNow,
                };
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>Total number of (metric, workload) combinations being tracked.</summary>
    public int ProfileCount => _stats.Count;

    // ── Private ───────────────────────────────────────────────────────────────

    private static string BuildKey(string metricName, string workloadLabel) =>
        $"{metricName.ToLowerInvariant()}:{workloadLabel.ToLowerInvariant()}";
}
