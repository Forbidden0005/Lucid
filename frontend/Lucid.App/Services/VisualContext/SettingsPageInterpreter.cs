namespace Lucid.Services.VisualContext;

/// <summary>
/// Classifies the active Windows Settings page by reading the window title.
///
/// Each Settings page changes the window title to reflect the current section
/// (e.g. "Startup Apps — Settings", "Storage — Settings").
/// This lets Lucid narrate exactly where the user is without any pixel analysis.
///
/// Privacy: reads only the window title string — no content inspection.
/// </summary>
public sealed class SettingsPageInterpreter
{
    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="SettingsPageContext"/> for the given Settings window title.
    /// Returns null if the title does not look like a Settings page.
    /// </summary>
    public SettingsPageContext? Interpret(string windowTitle)
    {
        var pageType = ClassifyPageTitle(windowTitle);
        if (pageType == SettingsPageType.Unknown) return null;

        return new SettingsPageContext
        {
            PageType       = pageType,
            PageTitle      = windowTitle,
            Description    = DescribePageType(pageType),
            GuidanceSteps  = GuidanceFor(pageType),
            RelatedActions = ActionsFor(pageType),
        };
    }

    // ── Classification (also used by WindowSemanticAnalyzer) ─────────────────

    /// <summary>
    /// Maps a Settings window title to a <see cref="SettingsPageType"/>.
    /// Called by <see cref="WindowSemanticAnalyzer"/> during window classification.
    /// </summary>
    public static SettingsPageType ClassifyPageTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return SettingsPageType.Unknown;

        var t = title.ToLowerInvariant();

        // Startup / apps
        if (t.Contains("startup"))                      return SettingsPageType.StartupApps;
        if (t.Contains("apps & features") ||
            t.Contains("apps and features") ||
            t.Contains("installed apps"))               return SettingsPageType.AppsFeatures;
        if (t.Contains("optional features"))            return SettingsPageType.AppsFeatures;

        // Storage
        if (t.Contains("storage") && !t.Contains("windows update"))
                                                        return SettingsPageType.StorageSense;

        // Network
        if (t.Contains("network") || t.Contains("wi-fi") ||
            t.Contains("ethernet") || t.Contains("vpn"))
                                                        return SettingsPageType.Network;

        // Privacy / Security
        if (t.Contains("privacy") || t.Contains("security") &&
            !t.Contains("windows security"))            return SettingsPageType.PrivacySecurity;
        if (t.Contains("windows security"))             return SettingsPageType.WindowsSecurity;

        // Windows Update
        if (t.Contains("windows update") || t.Contains("update"))
                                                        return SettingsPageType.WindowsUpdate;

        // Display
        if (t.Contains("display") || t.Contains("resolution") ||
            t.Contains("screen") || t.Contains("night light"))
                                                        return SettingsPageType.Display;

        // Bluetooth / Devices
        if (t.Contains("bluetooth") || t.Contains("devices") ||
            t.Contains("printers") || t.Contains("mouse") ||
            t.Contains("keyboard"))                     return SettingsPageType.Bluetooth;

        // Sound
        if (t.Contains("sound") || t.Contains("audio") ||
            t.Contains("microphone") || t.Contains("speaker"))
                                                        return SettingsPageType.Sound;

        // Accessibility
        if (t.Contains("accessibility") || t.Contains("ease of access"))
                                                        return SettingsPageType.Accessibility;

        // Recovery / Troubleshoot
        if (t.Contains("recovery"))                     return SettingsPageType.Recovery;
        if (t.Contains("troubleshoot"))                 return SettingsPageType.Troubleshoot;

        // Personalization
        if (t.Contains("personalization") || t.Contains("background") ||
            t.Contains("colours") || t.Contains("colors") ||
            t.Contains("themes") || t.Contains("lock screen"))
                                                        return SettingsPageType.Personalization;

        // Accounts
        if (t.Contains("accounts") || t.Contains("sign-in") || t.Contains("email"))
                                                        return SettingsPageType.Accounts;

        // Time & Language
        if (t.Contains("time") || t.Contains("language") || t.Contains("region"))
                                                        return SettingsPageType.TimeLanguage;

        // Gaming
        if (t.Contains("gaming") || t.Contains("game mode") || t.Contains("xbox"))
                                                        return SettingsPageType.Gaming;

        // System (general)
        if (t.Contains("system"))                       return SettingsPageType.System;

        // Main Settings hub
        if (t.Trim().Equals("settings", StringComparison.OrdinalIgnoreCase))
                                                        return SettingsPageType.MainSettings;

        return SettingsPageType.Unknown;
    }

    // ── Descriptions ──────────────────────────────────────────────────────────

    private static string DescribePageType(SettingsPageType type) => type switch
    {
        SettingsPageType.StartupApps     => "You are on the Startup Apps page. You can see and toggle which apps launch automatically when Windows starts.",
        SettingsPageType.StorageSense    => "You are on the Storage settings page. Storage Sense and cleanup options are here.",
        SettingsPageType.PrivacySecurity => "You are on the Privacy & Security page.",
        SettingsPageType.WindowsSecurity => "You are on the Windows Security page.",
        SettingsPageType.Network         => "You are on the Network & Internet settings page.",
        SettingsPageType.Display         => "You are on the Display settings page.",
        SettingsPageType.WindowsUpdate   => "You are on the Windows Update page. You can check for updates and manage update settings here.",
        SettingsPageType.Bluetooth       => "You are on the Bluetooth & Devices page.",
        SettingsPageType.AppsFeatures    => "You are on the Apps & Features (Installed Apps) page.",
        SettingsPageType.Sound           => "You are on the Sound settings page.",
        SettingsPageType.Accessibility   => "You are on the Accessibility settings page.",
        SettingsPageType.Recovery        => "You are on the Recovery settings page.",
        SettingsPageType.Troubleshoot    => "You are on the Troubleshoot settings page.",
        SettingsPageType.System          => "You are in the System settings section.",
        SettingsPageType.MainSettings    => "You are on the main Windows Settings page.",
        _                                => "You are in Windows Settings.",
    };

    // ── Guidance steps ────────────────────────────────────────────────────────

    private static IReadOnlyList<string> GuidanceFor(SettingsPageType type) => type switch
    {
        SettingsPageType.StartupApps => [
            "Each toggle in the list controls whether an app starts automatically.",
            "Look for apps marked as 'High impact' — these slow boot the most.",
            "Click the toggle next to any app to enable or disable it.",
        ],
        SettingsPageType.StorageSense => [
            "Click 'Storage Sense' to configure automatic cleanup.",
            "'Cleanup recommendations' shows files that can be safely removed.",
            "Use 'Other drives' to check space on secondary drives.",
        ],
        SettingsPageType.WindowsUpdate => [
            "Click 'Check for updates' to search for new patches.",
            "'Advanced options' lets you control how updates are delivered.",
            "Restart may be required after updates complete.",
        ],
        SettingsPageType.Network => [
            "Select your network connection to see IP, DNS, and speed details.",
            "'Network troubleshooter' can diagnose common connection problems.",
            "'VPN' settings are in the left panel.",
        ],
        _ => [],
    };

    // ── Related Lucid actions ─────────────────────────────────────────────────

    private static IReadOnlyList<string> ActionsFor(SettingsPageType type) => type switch
    {
        SettingsPageType.StartupApps  => ["Analyse startup impact", "Disable selected startup apps"],
        SettingsPageType.StorageSense => ["Run storage analysis", "Find large files"],
        SettingsPageType.WindowsUpdate => ["Check update status"],
        SettingsPageType.WindowsSecurity => ["Run security scan"],
        _ => [],
    };
}
