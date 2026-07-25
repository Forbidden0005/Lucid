using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Lucid.Tests.Interaction.Language;

/// <summary>
/// Audits active app source files for user-facing copy that violates Lucid's
/// security-language doctrine. This scans only likely UI/output literals, not
/// comments or historical material.
/// </summary>
public sealed class OperationalLanguagePolicySourceAuditTests
{
    private static readonly (string Phrase, string Guidance)[] ForbiddenPhrases =
    [
        ("malicious", "use confidence-aware language like 'unusual' or 'flagged for review'"),
        ("infected", "describe observations instead of infection claims"),
        ("dangerous", "prefer 'worth reviewing' or a scoped impact explanation"),
        ("threat detected", "describe what was flagged and why"),
        ("your pc is in danger", "remove fear-based claims"),
        ("critical error", "use proportional severity language"),
        ("critical threat", "use proportional severity language"),
        ("safe to delete", "avoid absolute safety claims; ask the user to review first"),
        ("completely safe", "avoid absolute safety claims"),
        ("guaranteed", "avoid certainty language the product cannot honestly prove"),
    ];

    private static readonly Regex XamlAttributeRegex = new(
        "(?<name>Text|Header|Content|Title)=\"(?<value>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CsAssignmentRegex = new(
        "(?<name>Narration|Rationale|ExpectedOutcome|Goal|Description|Message|StatusText|Text|Title)\\s*=\\s*\\$?\"(?<value>(?:[^\"\\\\]|\\\\.|\\{[^}]*\\})*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void ActiveUserFacingCopy_DoesNotContainForbiddenFearOrCertaintyPhrases()
    {
        var repoRoot = FindRepoRoot();
        var failures = new List<string>();

        foreach (var file in EnumerateAuditedFiles(repoRoot))
        {
            foreach (var literal in ExtractUserFacingLiterals(file))
            {
                var normalized = literal.Value.ToLowerInvariant();
                foreach (var (phrase, guidance) in ForbiddenPhrases)
                {
                    if (normalized.Contains(phrase, StringComparison.Ordinal))
                    {
                        failures.Add(
                            $"{Path.GetRelativePath(repoRoot, file)} [{literal.Kind}] contains '{phrase}': {literal.Value} ({guidance})");
                    }
                }
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    private static IEnumerable<string> EnumerateAuditedFiles(string repoRoot)
    {
        // Production copy is split across Lucid.App (UI + app-side services)
        // and Lucid.Core (pure services) since the library extraction — audit
        // both so moving a file never removes it from the language policy.
        var appRoot = Path.Combine(repoRoot, "lucid-desktop", "Lucid.App");
        var coreRoot = Path.Combine(repoRoot, "lucid-desktop", "Lucid.Core");

        foreach (var root in new[]
                 {
                     Path.Combine(appRoot, "Services"),
                     Path.Combine(appRoot, "ViewModels"),
                     Path.Combine(appRoot, "Views"),
                     Path.Combine(coreRoot, "Services"),
                 })
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (ext.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerable<(string Kind, string Value)> ExtractUserFacingLiterals(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var regex = filePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
            ? XamlAttributeRegex
            : CsAssignmentRegex;

        foreach (Match match in regex.Matches(text))
        {
            var value = Regex.Unescape(match.Groups["value"].Value).Trim();
            if (!string.IsNullOrWhiteSpace(value))
                yield return (match.Groups["name"].Value, value);
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "lucid-desktop", "Lucid.App", "Lucid.App.csproj");
            if (File.Exists(candidate))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root for source audit.");
    }
}
