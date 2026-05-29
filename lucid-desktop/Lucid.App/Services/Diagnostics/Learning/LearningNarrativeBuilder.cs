using Lucid.Services.Intelligence.Calibration;
using Lucid.Services.Intelligence.Patterns;

namespace Lucid.Services.Diagnostics.Learning;

/// <summary>
/// Builds human-readable narratives explaining adaptive learning events.
///
/// The LearningNarrativeBuilder answers user questions such as:
///   "Why did this pattern form?"
///   "How has this baseline changed?"
///   "Why was this recommendation deprioritized?"
///
/// All narratives are grounded in observable evidence — no post-hoc
/// rationalization or invented explanations.
///
/// Used by the Diagnostics page "What has Lucid learned?" panel.
/// Thread-safe: stateless and pure.
/// </summary>
public static class LearningNarrativeBuilder
{
    // ── Pattern Formation ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds a brief narrative explaining why an operational pattern formed.
    /// </summary>
    public static string BuildPatternFormationNarrative(OperationalPattern pattern)
    {
        var parts = new List<string>(3);

        parts.Add($"Lucid recognized a recurring '{pattern.PatternType}' pattern " +
                  $"after observing it {pattern.OccurrenceCount} time{Plural(pattern.OccurrenceCount)} " +
                  $"over {FormatAge(pattern.PatternAge)}.");

        if (pattern.DominantWorkloadContext is not null)
            parts.Add($"It most often occurs during {pattern.DominantWorkloadContext} workloads.");

        if (pattern.IsStable)
            parts.Add("The pattern is consistent — occurrences show low priority variance.");
        else
            parts.Add("The pattern has some variability — may reflect different underlying causes.");

        if (pattern.AverageRecurrenceInterval is TimeSpan interval)
            parts.Add($"Average interval between occurrences: {FormatInterval(interval)}.");

        return string.Join(" ", parts);
    }

    // ── Calibration Adjustment ────────────────────────────────────────────────

    /// <summary>
    /// Builds a narrative explaining a confidence calibration adjustment.
    /// </summary>
    public static string BuildCalibrationNarrative(AccuracySummary summary, double adjustmentFactor)
    {
        var direction = adjustmentFactor < 1.0 ? "reduced" : "increased";
        var magnitude = Math.Abs(1.0 - adjustmentFactor);

        var parts = new List<string>(3);

        parts.Add($"Confidence scores for '{summary.InferenceType}' inferences have been {direction} " +
                  $"by ~{magnitude:P0} based on {summary.RecordCount} historical outcome records.");

        if (summary.IsOverConfident)
            parts.Add($"The engine was predicting {summary.MeanCalibrationError:P0} higher confidence " +
                      "than outcomes supported.");
        else if (summary.IsUnderConfident)
            parts.Add($"The engine was predicting {Math.Abs(summary.MeanCalibrationError):P0} lower " +
                      "confidence than outcomes supported.");

        parts.Add($"Confirmation rate: {summary.ConfirmationRate:P0} of predictions were confirmed.");

        return string.Join(" ", parts);
    }

    // ── Effectiveness Deprioritization ────────────────────────────────────────

    /// <summary>
    /// Builds a narrative explaining why a recommendation was deprioritized.
    /// </summary>
    public static string BuildDeprioritizationNarrative(
        string actionId,
        double effectivenessScore,
        int    totalShown)
    {
        var parts = new List<string>(2);

        parts.Add($"The recommendation '{actionId}' has been deprioritized.");

        if (totalShown >= 5)
        {
            parts.Add(effectivenessScore < 0.3
                ? $"After {totalShown} presentations, the acceptance rate and condition " +
                  "improvement signals both indicate low operational value."
                : $"After {totalShown} presentations, user engagement has been low.");
        }
        else
        {
            parts.Add("This is a tentative deprioritization — it may be reversed as more data accumulates.");
        }

        parts.Add("It will re-appear when the underlying condition changes significantly.");
        return string.Join(" ", parts);
    }

    // ── Learning Trace Narration ───────────────────────────────────────────────

    /// <summary>
    /// Builds a one-sentence summary for a learning trace record.
    /// </summary>
    public static string SummarizeTrace(LearningTraceRecord trace) => trace.EventType switch
    {
        LearningEventType.PatternRecognized  =>
            $"Pattern recognized: {trace.Description}",
        LearningEventType.BaselineUpdated    =>
            $"Baseline updated: {trace.Description}",
        LearningEventType.EffectivenessUpdated =>
            $"Effectiveness updated: {trace.Description}",
        LearningEventType.CalibrationAdjusted =>
            $"Confidence calibrated: {trace.Description}",
        LearningEventType.DriftRecorded      =>
            $"Drift recorded: {trace.Description}",
        LearningEventType.GovernanceSuppressed =>
            $"Learning suppressed by governance: {trace.Description}",
        _                                    => trace.Description,
    };

    // ── Private ───────────────────────────────────────────────────────────────

    private static string Plural(int count) => count == 1 ? "" : "s";

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 60)  return $"{(int)age.TotalMinutes} minutes";
        if (age.TotalHours   < 24)  return $"{(int)age.TotalHours} hours";
        return $"{(int)age.TotalDays} days";
    }

    private static string FormatInterval(TimeSpan interval)
    {
        if (interval.TotalMinutes < 60)  return $"~{(int)interval.TotalMinutes} minutes";
        if (interval.TotalHours   < 24)  return $"~{(int)interval.TotalHours} hours";
        return $"~{(int)interval.TotalDays} days";
    }
}
