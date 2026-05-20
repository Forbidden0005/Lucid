using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services.Intelligence.Rules;

/// <summary>
/// Fires when total disk I/O throughput has averaged above 100 MB/s for
/// the last 60 seconds.
///
/// Elevated sustained disk I/O is informational — it often indicates a
/// legitimate background task (Windows Update, antivirus scan, backup) but
/// can explain why the system feels sluggish when it persists.
///
/// State note:
///   This rule maintains its own local time-windowed buffer for MB/s readings.
///   The shared history buffer tracks percentage metrics only; raw throughput
///   in MB/s is not stored there, so local state is the correct approach.
/// </summary>
public sealed class HighDiskThroughputRule : IInsightRule
{
    private const double HighThroughputMbps = 100.0;
    private const int    MinSamples         = 15;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    // Local ring of (timestamp, total MB/s) readings within the window.
    private readonly Queue<(DateTimeOffset Timestamp, double Mbps)> _window = new();

    public string RuleId => "disk.sustained-io";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        var    now    = DateTimeOffset.Now;
        double mbps   = current.DiskReadMbps + current.DiskWriteMbps;
        var    cutoff = now - Window;

        // Record the current reading.
        _window.Enqueue((now, mbps));

        // Evict readings that have fallen outside the window.
        while (_window.Count > 0 && _window.Peek().Timestamp < cutoff)
            _window.Dequeue();

        if (_window.Count < MinSamples)
            return null;

        double avgMbps = 0;
        foreach (var (_, value) in _window)
            avgMbps += value;
        avgMbps /= _window.Count;

        if (avgMbps <= HighThroughputMbps)
            return null;

        // 60-second window ≈ 40 samples at the 1.5-second poll rate.
        int confidence = Math.Clamp(_window.Count * 100 / 40, 72, 97);

        return new SystemInsight(
            Id:                RuleId,
            Severity:          InsightSeverity.Info,
            Title:             "High Disk Activity",
            Detail:            $"Your disk has sustained {avgMbps:0} MB/s of read/write throughput over the " +
                               $"last minute (currently {mbps:0} MB/s). A background task — such as Windows " +
                               $"Update, an antivirus scan, or a backup — is likely running.",
            ActionHint:        null,
            DetectedAt:        DateTimeOffset.Now,
            ConfidencePercent: confidence);
    }
}
