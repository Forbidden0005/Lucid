using ExplainMyPC.Intelligence.Rules;
using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Summary;

/// <summary>
/// Generates a human-readable one-paragraph summary of the system's current state.
///
/// Design principles (from intelligence-engine.md):
///   • Write like a calm, knowledgeable friend — not a support ticket or alarm.
///   • Name the actual cause, not a code or technical label.
///   • Never exaggerate. "May be affecting" is honest; "CRITICAL ERROR" is not.
///   • When nothing is wrong, say so positively rather than staying silent.
///   • Summaries are written in second person ("your PC", "you may notice").
///
/// Generation strategy:
///   1. If no issues → return a positive, specific good-health message.
///   2. If all issues are Informational/Low → acknowledge but reassure.
///   3. If one dominant issue → describe it directly by name.
///   4. If multiple issues → name the worst and mention the count of others.
///   5. If multiple issues share a root cause → produce a unified explanation
///      rather than listing them separately (e.g. RAM+disk pressure = paging).
///
/// The summary targets ~1–2 sentences. It is displayed in the Explain My PC
/// banner on the Dashboard and at the top of the Explain page.
/// </summary>
public static class SummaryGenerator
{
    public static string Generate(IReadOnlyList<RuleResult> issues, HealthScoreModel health)
    {
        if (issues.Count == 0)
            return GenerateHealthySummary(health);

        var sorted = issues
            .OrderByDescending(i => (int)i.Severity)
            .ThenByDescending(i => (int)i.Confidence)
            .ToList();

        IssueSeverity worst = sorted[0].Severity;

        // Detect the memory-pressure composite pattern and produce a unified message.
        if (IsMemoryPressurePattern(issues))
            return MemoryPressureSummary(sorted[0], issues.Count);

        return issues.Count switch
        {
            1 => SingleIssueSummary(sorted[0]),
            _ => MultiIssueSummary(sorted, worst),
        };
    }

    // ── Healthy state ─────────────────────────────────────────────────────────

    private static string GenerateHealthySummary(HealthScoreModel health) => health.Overall switch
    {
        >= 90 => "Your PC is running well. No performance, storage, or stability issues were detected.",
        >= 75 => "Your PC is running well overall. Everything looks healthy.",
        _     => "No active issues were detected. Some health categories could not be fully evaluated yet.",
    };

    // ── Single issue ──────────────────────────────────────────────────────────

    private static string SingleIssueSummary(RuleResult issue) => issue.Severity switch
    {
        IssueSeverity.Informational =>
            $"Your PC is running well. {issue.Description}",

        IssueSeverity.Low =>
            $"Your PC is generally running well. {FirstSentence(issue.Description)} " +
            "This is unlikely to cause significant problems right now.",

        IssueSeverity.Moderate =>
            $"{FirstSentence(issue.Description)} " +
            $"Addressing this should noticeably improve {CategoryImpact(issue.Category)}.",

        IssueSeverity.High =>
            $"{FirstSentence(issue.Description)} " +
            "This is likely the reason your PC feels slower than it should.",

        IssueSeverity.Critical =>
            $"{FirstSentence(issue.Description)} " +
            "This needs attention to prevent further impact on your PC.",

        _ => issue.Description,
    };

    // ── Multiple issues ───────────────────────────────────────────────────────

    private static string MultiIssueSummary(List<RuleResult> sorted, IssueSeverity worst)
    {
        var top   = sorted[0];
        int others = sorted.Count - 1;
        string otherSuffix = others == 1
            ? "one other item was also detected."
            : $"{others} other items were also detected.";

        return worst switch
        {
            IssueSeverity.High or IssueSeverity.Critical =>
                $"{FirstSentence(top.Description)} " +
                $"Additionally, {otherSuffix}",

            IssueSeverity.Moderate =>
                $"A few things may be affecting your PC's performance. " +
                $"{top.Title} is the most significant, and {otherSuffix}",

            _ =>
                $"Your PC is running well overall. " +
                $"{sorted.Count} minor {(sorted.Count > 1 ? "items are" : "item is")} worth keeping an eye on, " +
                $"with {top.Title.ToLowerFirstChar()} being the most notable.",
        };
    }

    // ── Composite patterns ────────────────────────────────────────────────────

    private static bool IsMemoryPressurePattern(IReadOnlyList<RuleResult> issues) =>
        issues.Any(i => i.RuleId == "perf.memory.pressure") &&
        (issues.Any(i => i.RuleId == "perf.ram.high") ||
         issues.Any(i => i.RuleId == "storage.disk.high_io"));

    private static string MemoryPressureSummary(RuleResult topIssue, int totalCount)
    {
        string tail = totalCount > 2
            ? $" {totalCount - 1} additional issues were also detected."
            : string.Empty;

        return "Your PC is running low on memory and has started using the disk as overflow storage. " +
               "This is why everything feels slow — disk access is much slower than RAM. " +
               $"Closing some applications should help immediately.{tail}";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FirstSentence(string text)
    {
        int dot = text.IndexOf(". ", StringComparison.Ordinal);
        return dot >= 0 ? text[..(dot + 1)] : text;
    }

    private static string CategoryImpact(IssueCategory cat) => cat switch
    {
        IssueCategory.Performance => "system responsiveness",
        IssueCategory.Memory      => "application switching and multitasking",
        IssueCategory.Startup     => "boot time and background resource usage",
        IssueCategory.Thermal     => "sustained performance under load",
        IssueCategory.Storage     => "disk performance and available space",
        IssueCategory.Security    => "your system's security posture",
        IssueCategory.Stability   => "system stability",
        IssueCategory.Privacy     => "your privacy",
        _                         => "overall performance",
    };
}

file static class StringExtensions
{
    internal static string ToLowerFirstChar(this string s) =>
        s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
