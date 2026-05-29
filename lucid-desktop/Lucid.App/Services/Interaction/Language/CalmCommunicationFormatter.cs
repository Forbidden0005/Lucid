namespace Lucid.Services.Interaction.Language;

/// <summary>
/// Final-stage output formatter that enforces calm, grounded communication.
///
/// All user-facing text should flow through this formatter before reaching
/// the UI layer. The formatter:
///   1. Applies language policy sanitization as a safety net.
///   2. Normalizes whitespace and punctuation.
///   3. Enforces length limits appropriate for the presentation context.
///
/// Three presentation contexts are supported:
///   - Compact (≤ 120 chars) — notifications, card headers, chips
///   - Standard (≤ 300 chars) — recommendation card bodies, summaries
///   - Detail (no limit)      — evidence panels, diagnostics, tooltips
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class CalmCommunicationFormatter
{
    /// <summary>Maximum characters for compact/notification/header presentation.</summary>
    public const int CompactMaxLength = 120;

    /// <summary>Maximum characters for standard card/summary presentation.</summary>
    public const int StandardMaxLength = 300;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Formats text for compact presentation (notification, card header, chip).
    /// Sanitizes prohibited terms and truncates cleanly at a word boundary.
    /// </summary>
    public static string FormatCompact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sanitized = OperationalLanguagePolicy.Sanitize(text.Trim());
        return Truncate(sanitized, CompactMaxLength);
    }

    /// <summary>
    /// Formats text for standard presentation (recommendation card body, summary).
    /// Sanitizes, normalizes punctuation, and truncates with period completion.
    /// </summary>
    public static string FormatStandard(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sanitized = OperationalLanguagePolicy.Sanitize(text.Trim());
        return EnsureTerminalPeriod(Truncate(sanitized, StandardMaxLength));
    }

    /// <summary>
    /// Formats text for detail/expanded presentation (evidence panel, diagnostics).
    /// Sanitizes only — no truncation applied.
    /// </summary>
    public static string FormatDetail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return EnsureTerminalPeriod(OperationalLanguagePolicy.Sanitize(text.Trim()));
    }

    /// <summary>
    /// Combines a lead phrase and body, applying the appropriate format context.
    /// Used by ConfidenceToneAdjuster and ExplainabilityRenderer when building
    /// composite sentences.
    /// </summary>
    public static string BuildSentence(string lead, string body, string? hedge = null)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(lead)) parts.Add(lead.TrimEnd('.'));
        if (!string.IsNullOrWhiteSpace(body)) parts.Add(body.TrimEnd('.'));

        var sentence = string.Join(" ", parts) + ".";
        if (!string.IsNullOrWhiteSpace(hedge))
            sentence += " " + EnsureTerminalPeriod(hedge.Trim());

        return OperationalLanguagePolicy.Sanitize(sentence);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Truncate(string text, int maxLen)
    {
        if (text.Length <= maxLen) return text;

        // Truncate at the last whitespace before the limit to preserve words.
        var cutAt = text.LastIndexOf(' ', maxLen - 2);
        if (cutAt < maxLen / 2) cutAt = maxLen - 2; // fallback: hard cut
        return text[..cutAt].TrimEnd() + "…";
    }

    private static string EnsureTerminalPeriod(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var last = text[^1];
        return last is '.' or '?' or '!' or '…' ? text : text + ".";
    }
}
