using ExplainMyPC.Intelligence.Models;
using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Rules;

/// <summary>
/// Read-only view of system state passed to every rule during evaluation.
///
/// Rules access data through typed helpers rather than raw TelemetryReading
/// properties. This keeps individual rules concise and insulates them from
/// future model changes.
///
/// History helpers gracefully handle short histories (fewer than N readings)
/// by averaging whatever is available. Rules must tolerate this — they should
/// express "average of last N" as a desired window, not an exact requirement.
/// </summary>
public sealed class RuleContext
{
    public TelemetryReading                Current         { get; }
    public IReadOnlyList<TelemetryReading> History         { get; }
    public int                             StartupAppCount { get; }

    public RuleContext(SystemSnapshot snapshot)
    {
        Current         = snapshot.Current;
        History         = snapshot.RecentHistory;
        StartupAppCount = snapshot.StartupAppCount;
    }

    // ── Trend helpers — average of the last N readings (inclusive of Current) ──

    /// <summary>Average CPU% across the most recent <paramref name="lastN"/> readings.</summary>
    public double CpuAverage(int lastN = 5) => Average(lastN, r => r.CpuPercent);

    /// <summary>Average RAM% across the most recent <paramref name="lastN"/> readings.</summary>
    public double RamAverage(int lastN = 5) => Average(lastN, r => r.RamPercent);

    /// <summary>Average disk I/O% across the most recent <paramref name="lastN"/> readings.</summary>
    public double DiskIoAverage(int lastN = 5) => Average(lastN, r => r.DiskUsagePercent);

    /// <summary>Average GPU% across the most recent <paramref name="lastN"/> readings.</summary>
    public double GpuAverage(int lastN = 5) => Average(lastN, r => r.GpuPercent);

    /// <summary>Disk fill ratio (0–1) from the current reading.</summary>
    public double DiskFillRatio =>
        Current.DiskTotalGb > 0
            ? (double)Current.DiskUsedGb / Current.DiskTotalGb
            : 0;

    /// <summary>Free disk space in GB from the current reading.</summary>
    public long DiskFreeGb => Math.Max(Current.DiskTotalGb - Current.DiskUsedGb, 0);

    // ── Private ───────────────────────────────────────────────────────────────

    private double Average(int lastN, Func<TelemetryReading, double> selector)
    {
        // Include Current plus however many history entries fit in the window.
        var window = History
            .TakeLast(Math.Max(lastN - 1, 0))
            .Append(Current)
            .ToList();

        return window.Count == 0 ? 0 : window.Average(selector);
    }
}
