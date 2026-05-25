using ExplainMyPC.Intelligence.Models;
using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Rules.Performance;

/// <summary>
/// Fires when GPU utilization is critically saturated for an extended period.
///
/// High GPU usage is typically expected during gaming, video editing, or 3D
/// rendering. This rule therefore uses a very conservative threshold and only
/// fires if GPU has been near-maxed for several consecutive readings — a pattern
/// more consistent with a runaway process than an intentional workload.
///
/// Severity is intentionally capped at Low/Informational because:
///   1. Users cannot easily reduce GPU usage without closing the GPU-bound app.
///   2. The GPU being busy is rarely a system health problem.
///   3. False-positive stigma is worse than a missed warning here.
///
/// Threshold: ≥ 98% avg over 10 readings → Low (informational only)
/// </summary>
public sealed class HighGpuRule : IRule
{
    public string RuleId => "perf.gpu.high";

    private const double Threshold = 98;

    public RuleResult? Evaluate(RuleContext ctx)
    {
        // Require a longer window than CPU/RAM to reduce false positives.
        double avg = ctx.GpuAverage(lastN: 10);

        if (avg < Threshold) return null;

        // Only fire if VRAM data is present (counter available).
        // GPU% without any VRAM data suggests a measurement artifact.
        if (ctx.Current.GpuVramTotalGb <= 0) return null;

        return new RuleResult(
            RuleId:          RuleId,
            Title:           "GPU is running at maximum capacity",
            Description:     $"GPU utilization has been at {avg:F0}% for an extended period. " +
                             "If you are not running a game or GPU-intensive application, " +
                             "a background process may be unexpectedly using the GPU.",
            Severity:        IssueSeverity.Low,
            Confidence:      IssueConfidence.Low,
            Category:        IssueCategory.Performance,
            CanFix:          false,
            Recommendations: [
                new Recommendation(
                    Title:           "Check Task Manager for unexpected GPU usage",
                    Reason:          "The GPU column in Task Manager shows which processes are using the GPU.",
                    ExpectedBenefit: "Identifies any background process consuming GPU resources unexpectedly.",
                    Risk:            RecommendationRisk.None,
                    SupportsRollback: false),
            ]);
    }
}
