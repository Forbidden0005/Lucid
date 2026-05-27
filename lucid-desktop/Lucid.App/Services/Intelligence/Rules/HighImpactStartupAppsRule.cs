using Lucid.Helpers;
using Lucid.Services.Startup;
using Lucid.Services.Telemetry;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Fires when two or more high-impact applications are configured to start
/// automatically at sign-in.
///
/// "High-impact" means the app is known to load a heavy runtime (Electron,
/// Java, or a full browser engine), maintain persistent network connections,
/// or run background services that consume CPU even while idle. Examples:
/// Chrome, Discord, Teams, Steam, Spotify, Zoom.
///
/// Severity:
///   Recommendation — 2 or 3 high-impact entries (informational nudge).
///   Warning         — 4 or more high-impact entries (meaningful load).
///
/// The detail text names the specific apps so the user can identify which
/// entries to consider disabling.
///
/// ID: startup.high-impact-apps
/// </summary>
public sealed class HighImpactStartupAppsRule : IInsightRule
{
    private const int RecommendationThreshold = 2;   // ≥ 2 → Recommendation
    private const int WarningThreshold        = 4;   // ≥ 4 → Warning

    // Maximum number of app names shown inline in the detail text.
    // Keeps sentences readable even when 10+ heavy apps are present.
    private const int MaxNamesInDetail = 4;

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.startup.open-startup-apps",
            Title:                "Review Startup Apps",
            Description:          "Open the Windows Startup Apps settings page to see each app's startup impact rating and selectively disable the ones you don't need to open automatically.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),
    ];

    public string RuleId => "startup.high-impact-apps";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Wait until the first StartupSampler refresh cycle completes.
        if (current.StartupEntries.Count == 0)
            return null;

        var highImpact = current.StartupEntries
            .Where(static e => e.Impact == StartupImpact.High)
            .ToList();

        if (highImpact.Count < RecommendationThreshold)
            return null;

        var severity = highImpact.Count >= WarningThreshold
            ? InsightSeverity.Warning
            : InsightSeverity.Recommendation;

        // Build a readable list of app display names.
        var names     = BuildNameList(highImpact);
        var countText = highImpact.Count == 1 ? "1 high-impact app" : $"{highImpact.Count} high-impact apps";

        string detail;
        if (highImpact.Count >= WarningThreshold)
        {
            detail = $"{countText} are set to launch automatically at sign-in: {names}. " +
                     $"These applications load large runtimes or run background services and together " +
                     $"meaningfully extend your startup time and increase idle resource usage. " +
                     $"Disabling ones you don't need right away can free RAM and CPU during the critical post-boot window.";
        }
        else
        {
            detail = $"{names} {(highImpact.Count == 1 ? "is" : "are")} set to launch automatically at sign-in. " +
                     $"These are resource-intensive applications that run background services even when you're not actively using them. " +
                     $"If you don't need them immediately after login, disabling their startup entry frees resources without removing the app.";
        }

        return new SystemInsight(
            Id:       RuleId,
            Severity: severity,
            Title:    severity == InsightSeverity.Warning
                          ? "Heavy Apps Load at Every Startup"
                          : "High-Impact Apps Start at Login",
            Detail:            detail,
            ActionHint:        "Review Startup Apps to selectively disable entries you don't need at sign-in.",
            DetectedAt:        DateTimeOffset.Now,
            ConfidencePercent: 100,
            RecommendedActions: s_actions);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a comma-separated list of app display names (Title Case), capped
    /// at <see cref="MaxNamesInDetail"/> entries, with an "and N more" suffix if needed.
    /// </summary>
    private static string BuildNameList(List<StartupEntry> entries)
    {
        var shown = entries
            .Take(MaxNamesInDetail)
            .Select(e => FormatName(e))
            .ToList();

        int overflow = entries.Count - shown.Count;

        return overflow > 0
            ? string.Join(", ", shown) + $", and {overflow} more"
            : string.Join(", ", shown);
    }

    /// <summary>
    /// Converts an entry to a readable display name. Prefers the registry value
    /// name (usually the app's own display name) over the exe name.
    /// </summary>
    private static string FormatName(StartupEntry entry)
    {
        // Registry value names like "Discord" or "Spotify" are already well-formatted.
        // Exe names like "chrome" need title-casing.
        var name = entry.Name.Trim();
        return name.Length > 0 && char.IsUpper(name[0])
            ? name
            : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
    }
}
