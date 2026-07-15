using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Lucid.Tests.Infrastructure;

/// <summary>
/// Freezes the AppServices static service locator (ROADMAP C4).
///
/// The locator is being retired through a strangler migration: consumers move
/// to constructor injection resolved via App.GetService&lt;T&gt;() over
/// IServiceRegistry. This audit pins the set of files still allowed to read
/// AppServices.* — new references anywhere else fail the build's test gate,
/// and the grandfather list below may only ever shrink.
/// </summary>
public sealed class AppServicesFreezeSourceAuditTests
{
    /// <summary>
    /// Legitimate permanent users: the locator itself, the composition root,
    /// and the registry seam that exists to replace it.
    /// </summary>
    private static readonly string[] PermanentAllowlist =
    [
        "AppServices.cs",
        "App.xaml.cs",
        "MainWindow.xaml.cs",
        "Services/Infrastructure/IServiceRegistry.cs",
        "Services/Infrastructure/ServiceRegistry.cs",
    ];

    /// <summary>
    /// Pre-freeze consumers, grandfathered 2026-07-15 (ROADMAP C4 step 2).
    /// Migrating a file? Delete its entry here — never add one.
    /// </summary>
    private static readonly string[] GrandfatheredConsumers =
    [
        "Services/LlmChat/LlmSystemContextBuilder.cs",
        "ViewModels/ActionExecutionViewModel.cs",
        "ViewModels/AppEntryViewModel.cs",
        "ViewModels/RepairsViewModel.cs",
        "ViewModels/TrendVisualizationHelper.cs",
        "Views/AutonomousRemediationPage.xaml.cs",
        "Views/CompanionOverlayWindow.xaml.cs",
        "Views/DashboardPage.xaml.cs",
        "Views/DeviceIntelligencePage.xaml.cs",
        "Views/DiagnosticsPage.xaml.cs",
        "Views/ExplainPage.xaml.cs",
        "Views/GuidedInteractionOverlay.xaml.cs",
        "Views/HealthBreakdownPage.xaml.cs",
        "Views/HistoricalPage.xaml.cs",
        "Views/InsightDetailPage.xaml.cs",
        "Views/InsightsPage.xaml.cs",
        "Views/InvestigationPage.xaml.cs",
        "Views/MachineBehaviorPage.xaml.cs",
        "Views/ProcessesPage.xaml.cs",
        "Views/ReasoningPage.xaml.cs",
        "Views/RepairsPage.xaml.cs",
        "Views/ReplayPage.xaml.cs",
        "Views/RuntimeGovernancePage.xaml.cs",
        "Views/SecurityPage.xaml.cs",
        "Views/SettingsPage.xaml.cs",
        "Views/SimulationPage.xaml.cs",
        "Views/StoragePage.xaml.cs",
        "Views/TimelinePage.xaml.cs",
        "Views/WatchtowerPage.xaml.cs",
    ];

    private static readonly Regex LocatorReferenceRegex = new(
        @"\bAppServices\.",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void NoNewFilesReferenceTheAppServicesStaticLocator()
    {
        var appRoot = Path.Combine(FindRepoRoot(), "lucid-desktop", "Lucid.App");
        var allowed = PermanentAllowlist.Concat(GrandfatheredConsumers)
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = NormalizePath(Path.GetRelativePath(appRoot, file));
            if (relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
                allowed.Contains(relative))
            {
                continue;
            }

            if (ContainsLocatorReferenceOutsideComments(file))
                violations.Add(relative);
        }

        violations.Should().BeEmpty(
            "the AppServices static locator is frozen (ROADMAP C4). Resolve dependencies via " +
            "App.GetService<T>() / constructor injection instead. Files: " +
            string.Join(", ", violations));
    }

    [Fact]
    public void GrandfatherListOnlyShrinks_EntriesWithoutReferencesMustBeRemoved()
    {
        var appRoot = Path.Combine(FindRepoRoot(), "lucid-desktop", "Lucid.App");
        var stale = GrandfatheredConsumers
            .Where(entry =>
            {
                var full = Path.Combine(appRoot, entry.Replace('/', Path.DirectorySeparatorChar));
                return !File.Exists(full) || !ContainsLocatorReferenceOutsideComments(full);
            })
            .ToList();

        stale.Should().BeEmpty(
            "these grandfathered files no longer reference AppServices — remove their entries " +
            "so the freeze ratchets forward: " + string.Join(", ", stale));
    }

    private static bool ContainsLocatorReferenceOutsideComments(string filePath)
    {
        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("*", StringComparison.Ordinal))
            {
                continue;
            }

            if (LocatorReferenceRegex.IsMatch(line))
                return true;
        }

        return false;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

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
