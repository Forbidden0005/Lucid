using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Lucid.Tests.Infrastructure;

/// <summary>
/// Guards the explainability doctrine at the source level (ROADMAP C7):
/// a platform that explains the system to users must not hide its own
/// failures. Debug/Console prints bypass the operational log, and bare
/// empty catch blocks make failures invisible.
/// </summary>
public sealed class SilentFailureSourceAuditTests
{
    /// <summary>
    /// The one sanctioned Debug.WriteLine lives in the diagnostics funnel as
    /// its pre-initialization fallback.
    /// </summary>
    private static readonly string[] DebugPrintAllowlist =
    [
        "Services/Diagnostics/Logging/OperationalDiagnostics.cs",
    ];

    // catch / catch (Ex) followed by an empty or whitespace-only block —
    // a block whose only content is a comment does NOT match ([^{}/] guard).
    private static readonly Regex UnjustifiedEmptyCatchRegex = new(
        @"catch(\s*\([^)]*\))?\s*\{\s*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DebugPrintRegex = new(
        @"\b(System\.Diagnostics\.)?Debug\.WriteLine\s*\(|\bConsole\.WriteLine\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void NoDebugOrConsolePrints_AllDiagnosticsFlowThroughTheOperationalLogger()
    {
        var violations = AuditSources((relative, line) =>
            DebugPrintRegex.IsMatch(line) &&
            !DebugPrintAllowlist.Contains(relative, StringComparer.OrdinalIgnoreCase));

        violations.Should().BeEmpty(
            "Debug/Console.WriteLine hides failures from the operational log (ROADMAP C7). " +
            "Use OperationalDiagnostics.ReportFailure or an injected IOperationalLogger. Found: " +
            string.Join(", ", violations));
    }

    [Fact]
    public void NoUnjustifiedEmptyCatchBlocks_EverySwallowedExceptionStatesWhy()
    {
        var violations = AuditSources((_, line) => UnjustifiedEmptyCatchRegex.IsMatch(line));

        violations.Should().BeEmpty(
            "an empty catch without a justification hides a failure (ROADMAP C7). Either log via " +
            "OperationalDiagnostics/IOperationalLogger or annotate the block with " +
            "/* best-effort: <why> */. Found: " + string.Join(", ", violations));
    }

    private static List<string> AuditSources(Func<string, string, bool> isViolation)
    {
        var appRoot = Path.Combine(FindRepoRoot(), "lucid-desktop", "Lucid.App");
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(appRoot, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("*", StringComparison.Ordinal))
                {
                    continue;
                }

                if (isViolation(relative, line))
                    violations.Add($"{relative}:{lineNumber}");
            }
        }

        return violations;
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
