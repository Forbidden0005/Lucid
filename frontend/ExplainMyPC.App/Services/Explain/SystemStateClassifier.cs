using ExplainMyPC.Services.Intelligence;
using ExplainMyPC.Services.Narrative;

namespace ExplainMyPC.Services.Explain;

/// <summary>
/// Classifies the overall system health state from the active insight set and
/// derives the dominant causes and overall confidence for the explanation.
///
/// Classification rules:
///   Healthy    — no actionable findings
///   Monitoring — recommendations only (no warnings)
///   Degraded   — 1–2 active warnings
///   Critical   — 3+ active warnings
///
/// Overall confidence is the arithmetic mean of all active findings' confidence
/// scores, falling back to 90% when the system is healthy (sensor readings are
/// always available even when no rules fire).
/// </summary>
internal static class SystemStateClassifier
{
    internal static (SystemHealthState State, string StateLabel, IReadOnlyList<string> DominantCauses, int Confidence)
        Classify(IReadOnlyList<SystemInsight> insights)
    {
        var actionable = insights
            .Where(i => i.Id != "system.healthy")
            .ToList();

        int warningCount = actionable.Count(i => i.Severity == InsightSeverity.Warning);
        int recCount     = actionable.Count(i => i.Severity == InsightSeverity.Recommendation);

        var state = warningCount switch
        {
            0 when recCount == 0 => SystemHealthState.Healthy,
            0                    => SystemHealthState.Monitoring,
            <= 2                 => SystemHealthState.Degraded,
            _                    => SystemHealthState.Critical,
        };

        var stateLabel = state switch
        {
            SystemHealthState.Healthy    => "Healthy",
            SystemHealthState.Monitoring => "Monitoring",
            SystemHealthState.Degraded   => "Needs attention",
            SystemHealthState.Critical   => "Multiple issues",
            _                            => "Analyzing",
        };

        // Top 3 contributing factors for the hero card
        var dominantCauses = actionable
            .OrderByDescending(i => (int)i.Severity)
            .ThenByDescending(i => i.ConfidencePercent)
            .Take(3)
            .Select(i => i.Title)
            .ToList();

        int confidence = actionable.Count > 0
            ? (int)actionable.Average(i => i.ConfidencePercent)
            : 90;

        return (state, stateLabel, dominantCauses, confidence);
    }
}
