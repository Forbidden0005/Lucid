using ExplainMyPC.Intelligence.Models;
using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Rules.Storage;

/// <summary>
/// Fires when disk I/O utilization is sustained at a level that causes
/// visible application slowness.
///
/// Unlike CPU and RAM, high disk I/O is often invisible to users — they
/// experience it as programs "hanging" or taking a long time to save files,
/// without understanding why. HDDs are far more susceptible than SSDs because
/// their mechanical seek time creates a hard bottleneck.
///
/// This rule deliberately avoids firing on brief bursts (antivirus scans,
/// Windows Update downloads) by requiring a sustained 8-reading average.
///
/// Thresholds (I/O% — normalised: 3.7 MB/s = 100% in the ViewModel):
///   ≥ 85% avg sustained → Moderate (disk is the bottleneck)
///   ≥ 95% avg sustained → High     (disk saturated; severe I/O wait)
///
/// Not fired for brief spikes regardless of peak value.
/// </summary>
public sealed class HighDiskIoRule : IRule
{
    public string RuleId => "storage.disk.high_io";

    private const double HighThreshold     = 95;
    private const double ModerateThreshold = 85;

    public RuleResult? Evaluate(RuleContext ctx)
    {
        double avg = ctx.DiskIoAverage(lastN: 8);

        if (avg < ModerateThreshold) return null;

        var (severity, confidence) = avg >= HighThreshold
            ? (IssueSeverity.High,     IssueConfidence.High)
            : (IssueSeverity.Moderate, IssueConfidence.Medium);

        double readMbps  = ctx.Current.DiskReadMbps;
        double writeMbps = ctx.Current.DiskWriteMbps;

        string description = severity switch
        {
            IssueSeverity.High =>
                $"Your disk is running at {avg:F0}% capacity " +
                $"(reading {readMbps:F1} MB/s, writing {writeMbps:F1} MB/s). " +
                "The disk is the bottleneck — applications waiting to read or write data will " +
                "appear frozen until the queue clears.",
            _ =>
                $"Disk activity is sustained at {avg:F0}% " +
                $"(reading {readMbps:F1} MB/s, writing {writeMbps:F1} MB/s). " +
                "File operations may feel slower than usual while this continues.",
        };

        return new RuleResult(
            RuleId:          RuleId,
            Title:           "High disk activity is slowing your PC",
            Description:     description,
            Severity:        severity,
            Confidence:      confidence,
            Category:        IssueCategory.Storage,
            CanFix:          true,
            Recommendations: [
                new Recommendation(
                    Title:           "Identify the process causing disk activity",
                    Reason:          "Task Manager's Performance → Disk tab shows which process is responsible.",
                    ExpectedBenefit: "Allows targeted action — pausing or closing the process stops the bottleneck.",
                    Risk:            RecommendationRisk.None,
                    SupportsRollback: false),
                new Recommendation(
                    Title:           "Check if Windows Update is downloading or installing",
                    Reason:          "Windows Update frequently causes background disk I/O without notifying the user.",
                    ExpectedBenefit: "Update-related I/O is temporary and resolves automatically.",
                    Risk:            RecommendationRisk.None,
                    SupportsRollback: false),
            ]);
    }
}
