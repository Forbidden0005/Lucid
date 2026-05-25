using Lucid.Services.Automation;

namespace Lucid.Services.Trust;

/// <summary>
/// Produces causality-exposing transparency summaries for automation actions.
///
/// Before any consent gate, the companion overlay should answer three questions:
///   1. WHY was this action suggested? — produced by this engine
///   2. WHAT will change?             — produced by <see cref="ConsentExplanationService"/>
///   3. HOW can it be undone?         — produced by <see cref="ConsentExplanationService"/>
///
/// The transparency engine transforms the <c>Rationale</c> field from an
/// <see cref="AutomationStep"/> (which may be terse, code-style text) into a
/// warm, plain-English causal chain that the user can actually read and evaluate.
///
/// Examples:
///   Step rationale: "High CPU detected; temp files suspected."
///   Transparency:   "Lucid wants to clear temporary files because CPU usage has been
///                    elevated for the past 8 minutes. Temp file cleanup is one of the
///                    fastest ways to reduce background I/O pressure."
///
/// All methods are pure/synchronous. Thread-safe — holds no mutable state.
/// </summary>
public sealed class AutomationTransparencyEngine
{
    // ── Primary entry point ───────────────────────────────────────────────────

    /// <summary>
    /// Builds a plain-English "why suggested" explanation for the given step.
    ///
    /// The method:
    ///   1. Takes the step's existing <c>Rationale</c> text (if any)
    ///   2. Enriches it with scope-specific context from the scope registry
    ///   3. Returns a 1–3 sentence explanation the user can act on
    ///
    /// Never returns null — falls back to a generic explanation if no rationale exists.
    /// </summary>
    public string ExplainWhySuggested(
        AutomationStep  step,
        string?         activeInsight = null,
        string?         attributedProcess = null)
    {
        var rationale = step.Rationale?.Trim();
        var actionId  = step.ActionId;

        // Build the opening from the rationale if available
        var opening = BuildOpeningFromRationale(rationale, activeInsight, attributedProcess);

        // Add scope-specific context
        var context = BuildScopeContext(actionId, attributedProcess);

        // Combine
        if (string.IsNullOrEmpty(context))
            return opening;

        return $"{opening} {context}".Trim();
    }

    /// <summary>
    /// Builds a short causal chain label (≤ 60 chars) for compact timeline display.
    /// e.g. "CPU pressure → temp cleanup suggested"
    /// </summary>
    public string BuildCausalChainLabel(AutomationStep step)
    {
        var actionId = step.ActionId?.ToLowerInvariant() ?? string.Empty;

        if (actionId.Contains("temp")) return "High CPU/disk → temporary file cleanup";
        if (actionId.Contains("recycle")) return "Low disk space → Recycle Bin cleanup";
        if (actionId.Contains("browser")) return "Disk pressure → browser cache cleanup";
        if (actionId.Contains("startup")) return "Slow startup detected → startup entry review";
        if (actionId.Contains("sfc") || actionId.Contains("dism")) return "System instability → Windows repair";
        if (actionId.Contains("dns")) return "Network issues → DNS flush";
        if (actionId.Contains("winsock")) return "Network reset requested";
        if (actionId.Contains("terminate") || actionId.Contains("process")) return "High resource usage → process review";
        if (actionId.Contains("task-manager")) return "Resource pressure → Task Manager opened";
        if (actionId.Contains("device-manager")) return "Hardware issue suspected → Device Manager opened";
        if (actionId.Contains("downloads")) return "Disk space low → Downloads folder review";

        // Fallback
        return step.Title.Length > 55 ? step.Title[..52] + "…" : step.Title;
    }

    /// <summary>
    /// Builds a full multi-sentence summary for display in the timeline detail card.
    ///
    /// Longer than <see cref="ExplainWhySuggested"/> — provides the complete
    /// operational picture connecting the detected condition to the suggested action.
    /// </summary>
    public string BuildFullTransparencyNarration(
        AutomationStep step,
        string?        insightTitle    = null,
        string?        forecastContext = null,
        string?        processName     = null)
    {
        var sb = new System.Text.StringBuilder();

        // Part 1: the detected condition
        var condition = BuildConditionSentence(step.ActionId, insightTitle, processName);
        sb.Append(condition);

        // Part 2: the rationale, if distinct from the condition
        if (!string.IsNullOrWhiteSpace(step.Rationale) &&
            !step.Rationale.Equals(condition, StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(' ');
            sb.Append(NormalizeRationale(step.Rationale));
        }

        // Part 3: forecast context, if available
        if (!string.IsNullOrWhiteSpace(forecastContext))
        {
            sb.Append(' ');
            sb.Append(forecastContext.TrimEnd('.'));
            sb.Append('.');
        }

        // Part 4: the action outcome
        sb.Append(' ');
        sb.Append(BuildOutcomeSentence(step));

        return sb.ToString().Trim();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string BuildOpeningFromRationale(
        string? rationale, string? insight, string? process)
    {
        // Use the insight title if we have one
        if (!string.IsNullOrWhiteSpace(insight))
        {
            if (!string.IsNullOrWhiteSpace(process))
                return $"Lucid detected: {insight} (attributed to {process}).";
            return $"Lucid detected: {insight}.";
        }

        // Fall back to the step rationale
        if (!string.IsNullOrWhiteSpace(rationale))
            return NormalizeRationale(rationale);

        // Generic fallback
        return "Lucid identified this action as relevant to your current system state.";
    }

    private static string BuildScopeContext(string? actionId, string? process)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return string.Empty;
        var lower = actionId.ToLowerInvariant();

        if (lower.Contains("temp"))
            return "Clearing temporary files reduces background disk I/O and can free up " +
                   "significant space without affecting your data or applications.";

        if (lower.Contains("recycle"))
            return "The Recycle Bin is accumulating deleted files. Emptying it reclaims disk space immediately.";

        if (lower.Contains("browser"))
            return "Browser caches can grow to several gigabytes over time. Clearing them " +
                   "frees disk space — browsers rebuild caches automatically as you browse.";

        if (lower.Contains("startup.disable"))
            return process is not null
                ? $"{process} is set to launch at login and may be contributing to slow startup times."
                : "Disabling unused startup applications is one of the most effective ways to improve boot time.";

        if (lower.Contains("sfc") || lower.Contains("dism"))
            return "SFC (System File Checker) and DISM scan for corrupted Windows system files. " +
                   "Running these tools is the recommended first step when Windows behaves unexpectedly.";

        if (lower.Contains("dns"))
            return "Flushing the DNS cache resolves stale or corrupted address lookups " +
                   "that can cause websites to fail or load slowly.";

        if (lower.Contains("winsock"))
            return "Resetting the Winsock stack restores the network API to its default state, " +
                   "which can resolve persistent connectivity issues.";

        if (lower.Contains("terminate") || lower.Contains("process"))
            return process is not null
                ? $"{process} is using an unusual amount of resources. Ending it will free those resources immediately."
                : "Ending this process will immediately free the resources it is consuming.";

        if (lower.Contains("task-manager"))
            return "Task Manager shows real-time CPU, memory, disk, and network usage by process.";

        if (lower.Contains("device-manager"))
            return "Device Manager displays all hardware devices and flags those with driver errors.";

        return string.Empty;
    }

    private static string BuildConditionSentence(
        string? actionId, string? insightTitle, string? process)
    {
        if (!string.IsNullOrWhiteSpace(insightTitle))
        {
            return process is not null
                ? $"Lucid detected '{insightTitle}', attributed to {process}."
                : $"Lucid detected '{insightTitle}'.";
        }

        if (string.IsNullOrWhiteSpace(actionId)) return "Lucid identified a relevant condition.";
        var lower = actionId.ToLowerInvariant();

        if (lower.Contains("temp"))   return "Temporary file accumulation was detected on your system.";
        if (lower.Contains("sfc"))    return "Potential Windows system file corruption was detected.";
        if (lower.Contains("dism"))   return "A Windows component store issue was identified.";
        if (lower.Contains("dns"))    return "A network connectivity issue was detected.";
        if (lower.Contains("winsock"))return "A network stack irregularity was identified.";
        if (lower.Contains("startup"))return "Slow system startup was detected.";
        if (lower.Contains("recycle"))return "Available disk space is running low.";

        return "A relevant system condition was identified.";
    }

    private static string BuildOutcomeSentence(AutomationStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.ExpectedOutcome))
            return NormalizeRationale(step.ExpectedOutcome);

        return $"Completing this step should help address the detected condition.";
    }

    private static string NormalizeRationale(string text)
    {
        // Capitalise first letter, ensure it ends with a period
        if (string.IsNullOrWhiteSpace(text)) return text;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return trimmed;
        var capitalised = char.ToUpper(trimmed[0]) + trimmed[1..];
        if (!capitalised.EndsWith('.') && !capitalised.EndsWith('!') && !capitalised.EndsWith('?'))
            capitalised += ".";
        return capitalised;
    }
}
