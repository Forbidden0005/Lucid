using System.Text;
using Lucid.Services.Reliability;

namespace Lucid.Services.Chat;

/// <summary>
/// Renders a <see cref="ReliabilityReport"/> into the text the language model is
/// given.
///
/// This is the highest-leverage file for answer quality in the whole chat path.
/// The model does not investigate anything itself — it explains what this writer
/// hands it — so the difference between a useful answer and a confidently wrong
/// one is largely decided here.
///
/// Three rules it enforces, because a small model will not enforce them itself:
///
///   • An unreadable log is stated as an unreadable log. Left implicit, a model
///     handed an empty findings list will cheerfully conclude the machine is
///     healthy, which is the worst possible answer for someone whose PC keeps
///     crashing.
///
///   • Confidence bands travel with every finding, and the model is told to
///     preserve them. Findings are inferences from log patterns; a 3b model left
///     to its own devices will flatten "worth reviewing" into "this is the cause".
///
///   • Counts and codes are given verbatim so the model has no reason to invent
///     any. Fabricated specifics are the failure mode that destroys trust
///     fastest, because they are indistinguishable from real ones.
///
/// Pure — a report in, a string out.
/// </summary>
public static class ReliabilityPromptWriter
{
    /// <summary>Findings included in full. Beyond this the tail adds noise, not signal.</summary>
    private const int MaxFindings = 6;

    /// <summary>Individual events listed after the findings.</summary>
    private const int MaxEvents = 25;

    /// <summary>
    /// Builds the prompt section for a reliability report.
    /// </summary>
    public static string Write(ReliabilityReport report)
    {
        var sb = new StringBuilder(2048);

        sb.AppendLine("=== CRASH AND STABILITY HISTORY (from the Windows event logs) ===");
        sb.AppendLine($"Window examined: {report.Since:yyyy-MM-dd} to {report.GeneratedAt:yyyy-MM-dd} " +
                      $"({(report.GeneratedAt - report.Since).Days} days).");
        sb.AppendLine();

        if (report.ReadFailed)
        {
            WriteReadFailure(sb, report);
            return sb.ToString();
        }

        if (report.IsClean)
        {
            WriteClean(sb);
            return sb.ToString();
        }

        WriteFindings(sb, report);
        WriteEventList(sb, report);
        WriteInstructions(sb);

        return sb.ToString();
    }

    // ── Could not look ────────────────────────────────────────────────────────

    private static void WriteReadFailure(StringBuilder sb, ReliabilityReport report)
    {
        sb.AppendLine("THE EVENT LOG COULD NOT BE READ.");
        sb.AppendLine($"Reason: {report.ReadFailureReason}");
        sb.AppendLine();
        sb.AppendLine("This means the crash history is UNKNOWN, which is not the same as there being none.");
        sb.AppendLine("Tell the user plainly that you could not check the crash history and why.");
        sb.AppendLine("Do NOT say the system looks stable, healthy, or free of errors — you have no");
        sb.AppendLine("evidence either way. Do not substitute current CPU or memory readings as if");
        sb.AppendLine("they answered a question about crashes; they describe a machine that is");
        sb.AppendLine("running right now, and say nothing about why it stopped earlier.");
    }

    // ── Nothing found ─────────────────────────────────────────────────────────

    private static void WriteClean(StringBuilder sb)
    {
        sb.AppendLine("No crash, shutdown, hardware, storage or application-failure events were");
        sb.AppendLine("recorded in this window. The logs were read successfully — this is a genuine");
        sb.AppendLine("absence of events, not a failure to look.");
        sb.AppendLine();
        sb.AppendLine("If the user is certain the machine has been crashing, say that the event logs");
        sb.AppendLine("do not show it and offer the explanations that leave no trace: a hard power");
        sb.AppendLine("cut, a machine held down by the power button, the logs having been cleared, or");
        sb.AppendLine("a freeze the user reset manually before Windows could write anything. Ask when");
        sb.AppendLine("it last happened rather than assuming they are mistaken.");
    }

    // ── Findings ──────────────────────────────────────────────────────────────

    private static void WriteFindings(StringBuilder sb, ReliabilityReport report)
    {
        if (report.Findings.Count == 0)
        {
            sb.AppendLine("Events were recorded, but none formed a pattern strong enough to be a");
            sb.AppendLine("finding. Describe what was logged without inferring a cause from it.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"WHAT THE EVIDENCE SUPPORTS ({report.Findings.Count} finding(s), " +
                      "strongest first — each with a confidence level you must preserve):");
        sb.AppendLine();

        var index = 0;

        foreach (var finding in report.Findings.Take(MaxFindings))
        {
            index++;
            sb.AppendLine($"{index}. [{Confidence(finding.Confidence)}] {finding.Headline}");
            sb.AppendLine($"   Seen {finding.Occurrences} time(s), most recently " +
                          $"{finding.LastSeen:yyyy-MM-dd HH:mm}.");
            sb.AppendLine($"   {finding.Explanation}");

            if (finding.SuggestedChecks.Count > 0)
            {
                sb.AppendLine("   Worth checking:");
                foreach (var check in finding.SuggestedChecks)
                    sb.AppendLine($"     - {check}");
            }

            sb.AppendLine();
        }

        if (report.Findings.Count > MaxFindings)
            sb.AppendLine($"({report.Findings.Count - MaxFindings} weaker finding(s) omitted.)");
    }

    // ── Raw events ────────────────────────────────────────────────────────────

    private static void WriteEventList(StringBuilder sb, ReliabilityReport report)
    {
        sb.AppendLine($"UNDERLYING EVENTS ({report.Events.Count} total, newest first):");

        foreach (var e in report.Events.Take(MaxEvents))
        {
            var stopCode = e.StopCode is not null ? $" [{e.StopCode}]" : string.Empty;
            sb.AppendLine($"  {e.When:yyyy-MM-dd HH:mm}  {e.Kind}{stopCode}  {e.Summary}");
        }

        if (report.Events.Count > MaxEvents)
            sb.AppendLine($"  … {report.Events.Count - MaxEvents} older event(s) not listed.");

        sb.AppendLine();
    }

    // ── How to use it ─────────────────────────────────────────────────────────

    private static void WriteInstructions(StringBuilder sb)
    {
        sb.AppendLine("HOW TO USE THIS:");
        sb.AppendLine("- Lead with the strongest finding. Give the actual counts, dates and stop codes");
        sb.AppendLine("  from above — never invent, round, or estimate any of them.");
        sb.AppendLine("- Keep each finding's confidence level. Say 'worth reviewing' for low, 'likely'");
        sb.AppendLine("  for moderate, 'strongly suggests' for high. Never state a cause as certain.");
        sb.AppendLine("- Do not name a culprit the evidence does not name. If a stop code points at a");
        sb.AppendLine("  category of cause, say the category.");
        sb.AppendLine("- An application crashing repeatedly does not explain the whole machine going");
        sb.AppendLine("  down. Treat it as a clue, not a verdict.");
        sb.AppendLine("- Finish with the one or two most useful checks from the lists above, in plain");
        sb.AppendLine("  language. Explain what each would tell us.");
        sb.AppendLine("- Never use the words malicious, infected, dangerous, or virus.");
    }

    private static string Confidence(FindingConfidence confidence) => confidence switch
    {
        FindingConfidence.High     => "HIGH CONFIDENCE",
        FindingConfidence.Moderate => "MODERATE CONFIDENCE",
        _                          => "LOW CONFIDENCE — worth reviewing only",
    };

    // ── UI trail ──────────────────────────────────────────────────────────────

    /// <summary>
    /// One line for the chat, so the user sees what was actually looked at rather
    /// than an unexplained pause. Written for a person, not for the model.
    /// </summary>
    public static string DescribeInvestigation(ReliabilityReport report)
    {
        var days = (report.GeneratedAt - report.Since).Days;

        if (report.ReadFailed)
            return $"Tried to read the Windows event logs — could not. {report.ReadFailureReason}";

        if (report.IsClean)
            return $"Checked the Windows event logs for the last {days} days — " +
                   "no crash, hardware or storage failures recorded.";

        var parts = new List<string>();

        void Count(ReliabilityEventKind kind, string singular, string plural)
        {
            var n = report.Events.Count(e => e.Kind == kind);
            if (n > 0) parts.Add($"{n} {(n == 1 ? singular : plural)}");
        }

        Count(ReliabilityEventKind.UnexpectedShutdown, "unexpected shutdown", "unexpected shutdowns");
        Count(ReliabilityEventKind.BugCheck,           "stop error",          "stop errors");
        Count(ReliabilityEventKind.HardwareError,      "hardware error",      "hardware errors");
        Count(ReliabilityEventKind.DiskFault,          "storage fault",       "storage faults");
        Count(ReliabilityEventKind.ApplicationCrash,   "app crash",           "app crashes");
        Count(ReliabilityEventKind.ApplicationHang,    "app hang",            "app hangs");

        var summary = parts.Count > 0 ? string.Join(", ", parts) : $"{report.Events.Count} event(s)";

        return $"Checked the Windows event logs for the last {days} days — found {summary}.";
    }
}
