using System.Text.RegularExpressions;

namespace Lucid.Services.Interaction.Language;

/// <summary>
/// Enforces Lucid's calm, non-theatrical operational communication policy.
///
/// The language policy codifies the core product doctrine:
///   - Confidence-aware phrasing (never certainty without strong evidence)
///   - Probabilistic language ("may indicate" vs "is infected")
///   - Severity-proportionate urgency (never exaggerate)
///   - Transparent limitation acknowledgement
///   - No antivirus-style fear-based copy
///
/// All Interaction-layer output must pass this policy before reaching the UI.
/// This is a project-wide invariant — see CLAUDE.md Core Doctrine: Security Language.
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class OperationalLanguagePolicy
{
    // ── Prohibited term registry ──────────────────────────────────────────────

    /// <summary>
    /// Terms that must never appear in user-facing output.
    /// Any text containing these is non-compliant — use Sanitize() or rewrite.
    /// </summary>
    private static readonly (string Prohibited, string Replacement)[] _replacements =
    [
        ("malicious",        "unusual"),
        ("infected",         "flagged for review"),
        ("dangerous",        "worth reviewing"),
        ("threat detected",  "pattern flagged for inspection"),
        ("compromised",      "showing unexpected behavior"),
        ("attack",           "anomalous activity"),
        ("hacked",           "showing unexpected access patterns"),
        ("virus",            "unrecognized program"),
        ("malware",          "unrecognized program"),
        ("in danger",        "showing signs worth reviewing"),
        ("critical threat",  "pattern requiring attention"),
    ];

    // Pre-compiled regex entries for performance.
    private static readonly (Regex Re, string Replacement)[] _compiled =
        _replacements
            .Select(pair => (
                new Regex(pair.Prohibited, RegexOptions.IgnoreCase | RegexOptions.Compiled),
                pair.Replacement))
            .ToArray();

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the text conforms to the calm communication policy.
    /// False means one or more prohibited terms are present.
    /// </summary>
    public static bool IsCompliant(string? text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        var lower = text.ToLowerInvariant();
        return _replacements.All(pair => !lower.Contains(pair.Prohibited));
    }

    /// <summary>
    /// Sanitizes text by replacing prohibited terms with compliant alternatives.
    /// Use as a last-resort safety net — prefer writing compliant text from the start.
    /// </summary>
    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var result = text;
        foreach (var (re, replacement) in _compiled)
            result = re.Replace(result, replacement);
        return result;
    }

    /// <summary>
    /// Returns a list of prohibited terms found in the text (for diagnostics).
    /// Empty when the text is compliant.
    /// </summary>
    public static IReadOnlyList<string> FindViolations(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var lower = text.ToLowerInvariant();
        return _replacements
            .Where(pair => lower.Contains(pair.Prohibited))
            .Select(pair => pair.Prohibited)
            .ToList()
            .AsReadOnly();
    }
}
