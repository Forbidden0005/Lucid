using System.Threading;

namespace Lucid.Services.Interaction.Diagnostics;

/// <summary>
/// Thread-safe runtime metrics for the cognitive interaction pipeline.
///
/// Tracks interaction-layer telemetry to allow Lucid to understand how
/// effectively it is communicating and where cognitive burden is highest.
///
/// Privacy guarantee: No user-identifiable data is stored. All metrics are
/// aggregate operational counts — not behavioral profiles.
///
/// Exposed via <c>InteractionMetrics</c> on the composition root.
/// Consumed by DiagnosticsPageViewModel and InteractionDiagnosticsService.
///
/// All counters use <see cref="Interlocked"/> — safe to read/write from any thread.
/// </summary>
public sealed class InteractionHealthMetrics
{
    private long   _totalSurfacedRecommendations;
    private long   _totalSuppressedRecommendations;
    private long   _totalBudgetExhaustedEvents;
    private long   _totalCooldownSuppressedEvents;
    private long   _totalFatigueSuppressedEvents;
    private long   _totalDensityRefreshes;
    private long   _totalExplainabilityRenders;
    private double _runningRenderLatencyMsSum;
    private long   _renderLatencyCount;

    // ── Update API ────────────────────────────────────────────────────────────

    /// <summary>Records a recommendation that was surfaced to the user.</summary>
    public void RecordSurfaced()   => Interlocked.Increment(ref _totalSurfacedRecommendations);

    /// <summary>Records a recommendation suppressed by any attention filter.</summary>
    public void RecordSuppressed() => Interlocked.Increment(ref _totalSuppressedRecommendations);

    /// <summary>Records an interrupt-budget exhaustion event.</summary>
    public void RecordBudgetExhausted() => Interlocked.Increment(ref _totalBudgetExhaustedEvents);

    /// <summary>Records a cooldown-window suppression.</summary>
    public void RecordCooldownSuppressed() =>
        Interlocked.Increment(ref _totalCooldownSuppressedEvents);

    /// <summary>Records a fatigue-based suppression.</summary>
    public void RecordFatigueSuppressed() =>
        Interlocked.Increment(ref _totalFatigueSuppressedEvents);

    /// <summary>Records a density refresh cycle.</summary>
    public void RecordDensityRefresh() => Interlocked.Increment(ref _totalDensityRefreshes);

    /// <summary>Records an explainability render with its latency.</summary>
    public void RecordExplainabilityRender(double latencyMs)
    {
        Interlocked.Increment(ref _totalExplainabilityRenders);
        Interlocked.Increment(ref _renderLatencyCount);
        // Lock-free running sum — slight inaccuracy under contention is acceptable for display.
        _runningRenderLatencyMsSum += latencyMs;
    }

    // ── Read API ──────────────────────────────────────────────────────────────

    public long TotalSurfaced           => Interlocked.Read(ref _totalSurfacedRecommendations);
    public long TotalSuppressed         => Interlocked.Read(ref _totalSuppressedRecommendations);
    public long TotalBudgetExhausted    => Interlocked.Read(ref _totalBudgetExhaustedEvents);
    public long TotalCooldownSuppressed => Interlocked.Read(ref _totalCooldownSuppressedEvents);
    public long TotalFatigueSuppressed  => Interlocked.Read(ref _totalFatigueSuppressedEvents);
    public long TotalDensityRefreshes   => Interlocked.Read(ref _totalDensityRefreshes);
    public long TotalExplainabilityRenders => Interlocked.Read(ref _totalExplainabilityRenders);

    /// <summary>
    /// Suppression rate across all evaluated recommendations (0.0–1.0).
    /// High suppression rate indicates active noise reduction.
    /// </summary>
    public double SuppressionRate
    {
        get
        {
            var total = TotalSurfaced + TotalSuppressed;
            return total == 0 ? 0 : (double)TotalSuppressed / total;
        }
    }

    /// <summary>Average explainability render latency in milliseconds.</summary>
    public double AverageRenderLatencyMs
    {
        get
        {
            var count = Interlocked.Read(ref _renderLatencyCount);
            return count == 0 ? 0 : _runningRenderLatencyMsSum / count;
        }
    }
}
