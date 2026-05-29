using Lucid.Services.Diagnostics.Reasoning;
using Lucid.Services.Interaction.Language;

namespace Lucid.Services.Interaction.Explainability;

/// <summary>
/// Builds human-readable prose explaining why an arbitration decision was made.
///
/// Arbitration narration answers:
///   "Why was this recommendation shown / suppressed / merged?"
///
/// Transparency is a core Lucid requirement. Every arbitration decision that
/// affects the user's recommendation surface must be explainable. This builder
/// turns <see cref="ArbitrationDecisionRecord"/> entries into plain English.
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class ArbitrationNarrativeBuilder
{
    /// <summary>
    /// Builds a compact explanation of a single arbitration decision.
    /// Used in the "Why was this hidden?" tooltip.
    /// </summary>
    public static string BuildCompact(ArbitrationDecisionRecord decision)
    {
        if (decision.Passed)
        {
            var domain = decision.AssignedDomain ?? "General";
            return CalmCommunicationFormatter.FormatCompact(
                $"Surfaced in the {domain} section (priority score: " +
                $"{decision.PriorityScore:F1}).");
        }

        var reason = decision.SuppressReason ?? "context filter";
        return CalmCommunicationFormatter.FormatCompact(
            $"Filtered: {reason}");
    }

    /// <summary>
    /// Builds a full arbitration explanation for the Explainability panel.
    /// </summary>
    public static string BuildFull(ArbitrationDecisionRecord decision)
    {
        var parts = new List<string>(4);

        if (decision.Passed)
        {
            parts.Add(
                $"This recommendation was evaluated at {decision.OccurredAtUtc:HH:mm:ss} UTC.");
            parts.Add(
                $"It passed all arbitration filters and was assigned to the " +
                $"'{decision.AssignedDomain ?? "General"}' surface with a priority " +
                $"score of {decision.PriorityScore:F2}.");
            parts.Add(
                $"Severity classification: " +
                $"{SeverityNarrativeEngine.GetLabel(decision.Severity)}.");
        }
        else
        {
            parts.Add(
                $"This recommendation was evaluated at {decision.OccurredAtUtc:HH:mm:ss} UTC " +
                $"but was not surfaced to the recommendation panel.");
            parts.Add(
                $"Reason: {decision.SuppressReason ?? "context filter applied"}.");
            parts.Add(
                $"This is normal — recommendations are filtered based on system context, " +
                $"current workload, and alert fatigue logic to reduce noise.");
            parts.Add("You can always view suppressed recommendations in the Diagnostics panel.");
        }

        return CalmCommunicationFormatter.FormatDetail(string.Join(" ", parts));
    }

    /// <summary>
    /// Builds a brief suppression reason for the "Why was this recommendation hidden?" UI.
    /// Returns null when the recommendation was not suppressed.
    /// </summary>
    public static string? BuildSuppressionSummary(ArbitrationDecisionRecord decision)
    {
        if (decision.Passed) return null;
        var reason = decision.SuppressReason ?? "context filter";
        return CalmCommunicationFormatter.FormatStandard(
            $"This recommendation was filtered because: {reason}. " +
            "It may re-appear when system context changes.");
    }
}
