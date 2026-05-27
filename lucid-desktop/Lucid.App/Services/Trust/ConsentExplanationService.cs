namespace Lucid.Services.Trust;

/// <summary>
/// Produces human-readable explanations for consent request cards.
///
/// Given a <see cref="ConsentRequest"/>, this service generates three plain-English
/// strings for the companion overlay:
///   1. What specifically will change on the system (<c>WhatChanges</c>)
///   2. How to undo the change (<c>HowToUndo</c>)
///   3. A risk-level label appropriate for the badge (<c>RiskLabel</c>)
///
/// The WHY a suggestion was made is handled by <see cref="AutomationTransparencyEngine"/>;
/// this service is purely responsible for the consequences.
///
/// All methods are synchronous and pure — no I/O, no state mutations.
/// Thread-safe: all members are instance-based but hold no mutable state.
/// </summary>
public sealed class ConsentExplanationService
{
    // ── WhatChanges ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a plain-English description of what will specifically change on
    /// the system if the user approves this consent request.
    /// </summary>
    public string ExplainWhatChanges(PermissionScope scope, string? actionTitle = null)
    {
        return scope switch
        {
            // Navigation — nothing changes
            PermissionScope.ExplorerNavigation =>
                "A folder will open in File Explorer. Nothing on your system is modified.",

            PermissionScope.SystemToolLaunch =>
                $"The {actionTitle ?? "system tool"} window will open. " +
                "No settings or files are changed.",

            PermissionScope.SettingsNavigation =>
                "A Windows Settings page will open in a new window. " +
                "No settings are changed automatically.",

            PermissionScope.SystemInfoRead =>
                "Lucid will read system information (processes, startup entries, or disk usage). " +
                "This is read-only — nothing is modified.",

            // Reversible operational
            PermissionScope.CacheCleanup =>
                "Temporary and cached files will be deleted from your system. " +
                "Windows will automatically recreate these files as needed — " +
                "your apps and data are not affected.",

            PermissionScope.StartupManagement =>
                $"A startup application entry will be {(actionTitle?.Contains("Disable") == true ? "disabled" : "changed")}. " +
                "A backup snapshot is created before the change so it can be restored if needed.",

            PermissionScope.ProcessTermination =>
                "The selected process will be forcibly ended. " +
                "Any unsaved work in that application will be lost.",

            PermissionScope.RecycleBinEmpty =>
                "All files currently in the Recycle Bin will be permanently deleted. " +
                "This frees up disk space but the files cannot be recovered afterward.",

            // Elevated operational
            PermissionScope.WindowsRepair =>
                "Windows will run a system file integrity check and attempt to repair any " +
                "corrupted or missing files. This may take several minutes and cannot be undone.",

            PermissionScope.NetworkReset =>
                "Your network adapter settings or Winsock configuration will be reset to " +
                "Windows defaults. Active network connections will be interrupted. " +
                "A restart may be needed for changes to take full effect.",

            PermissionScope.AppReset =>
                "The selected Windows app will be reset to its factory defaults. " +
                "App data and settings will be cleared. The app itself is not uninstalled.",

            // File operations
            PermissionScope.LargeFileDeletion =>
                "The selected file will be permanently deleted from your drive. " +
                "It will NOT go to the Recycle Bin. This action cannot be undone.",

            PermissionScope.DuplicateFileDeletion =>
                "Duplicate files in the selected group will be permanently deleted. " +
                "One copy will be kept. Deleted copies go directly to the Recycle Bin " +
                "and can be recovered from there.",

            PermissionScope.OldDownloadsDeletion =>
                "Files in your Downloads folder that haven't been accessed recently " +
                "will be moved to the Recycle Bin. You can recover them from there if needed.",

            // Context reads
            PermissionScope.DesktopContextRead =>
                "Lucid will read the title and path of your currently active window " +
                "to improve the relevance of its suggestions. Nothing is stored or uploaded.",

            PermissionScope.ClipboardRead =>
                "Lucid will read the current clipboard text to provide more helpful suggestions. " +
                "Your clipboard content is never stored, logged, or sent anywhere.",

            PermissionScope.CompanionMessage =>
                "A message will appear in the companion chat panel. " +
                "You can dismiss or ignore it at any time.",

            _ => $"The operation '{actionTitle ?? scope.ToString()}' will be performed. " +
                 "Review the details above before approving.",
        };
    }

    // ── HowToUndo ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a plain-English description of how to undo the action,
    /// or a clear statement that it cannot be undone.
    /// </summary>
    public string ExplainHowToUndo(PermissionScope scope)
    {
        return scope switch
        {
            // Navigation / read-only — nothing to undo
            PermissionScope.ExplorerNavigation  or
            PermissionScope.SystemToolLaunch    or
            PermissionScope.SettingsNavigation  or
            PermissionScope.SystemInfoRead      or
            PermissionScope.DesktopContextRead  or
            PermissionScope.ClipboardRead       or
            PermissionScope.CompanionMessage    =>
                "Nothing changes — there is nothing to undo.",

            // Reversible with a clear path
            PermissionScope.CacheCleanup =>
                "Caches will be rebuilt automatically by Windows and your apps. " +
                "No manual action is needed.",

            PermissionScope.StartupManagement =>
                "You can re-enable or restore the startup entry using Lucid's Startup page, " +
                "or by opening Windows Startup Apps settings. " +
                "A backup snapshot can also be restored from the Repairs page.",

            PermissionScope.ProcessTermination =>
                "Relaunch the application from the Start menu or its usual shortcut.",

            PermissionScope.RecycleBinEmpty =>
                "This action cannot be undone. Once the Recycle Bin is emptied, " +
                "files cannot be recovered without a third-party recovery tool.",

            PermissionScope.WindowsRepair =>
                "Windows repair operations cannot be undone. " +
                "If problems occur after repair, a System Restore point (if available) " +
                "can revert Windows to an earlier state.",

            PermissionScope.NetworkReset =>
                "Network settings will return to Windows defaults. " +
                "If you had custom DNS, proxy, or adapter settings, " +
                "you will need to re-enter them manually.",

            PermissionScope.AppReset =>
                "Re-configuring the app after reset is the only way to restore your settings. " +
                "This action cannot be automatically undone.",

            PermissionScope.LargeFileDeletion =>
                "This action cannot be undone. The file will be permanently deleted. " +
                "Ensure you no longer need it before approving.",

            PermissionScope.DuplicateFileDeletion =>
                "Deleted duplicates are moved to the Recycle Bin and can be recovered from there.",

            PermissionScope.OldDownloadsDeletion =>
                "Deleted files are moved to the Recycle Bin and can be recovered from there " +
                "until the Bin is emptied.",

            _ => "Review the action details above to understand how to reverse this change.",
        };
    }

    // ── Risk label ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a short human-readable risk label for the consent card badge.
    /// </summary>
    public string GetRiskLabel(TrustRiskLevel risk) => risk switch
    {
        TrustRiskLevel.None     => "Safe",
        TrustRiskLevel.Low      => "Low risk",
        TrustRiskLevel.Medium   => "Moderate risk",
        TrustRiskLevel.High     => "High risk",
        TrustRiskLevel.Critical => "Critical — review carefully",
        _                       => "Unknown risk",
    };

    /// <summary>
    /// Returns the hex colour string for the risk badge in the companion overlay.
    /// </summary>
    public string GetRiskColour(TrustRiskLevel risk) => risk switch
    {
        TrustRiskLevel.None     => "#22C55E",  // green
        TrustRiskLevel.Low      => "#3B82F6",  // blue
        TrustRiskLevel.Medium   => "#F59E0B",  // amber
        TrustRiskLevel.High     => "#EF4444",  // red
        TrustRiskLevel.Critical => "#DC2626",  // deep red
        _                       => "#6B7280",  // grey
    };

    // ── Consent card header ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the instructional header line shown at the top of a consent card.
    /// The tone is calm and informational, not alarming.
    /// </summary>
    public string GetConsentCardHeader(TrustRiskLevel risk) => risk switch
    {
        TrustRiskLevel.None or
        TrustRiskLevel.Low  => "Lucid wants to perform this action:",
        TrustRiskLevel.Medium => "Lucid is about to make a change — please review:",
        TrustRiskLevel.High or
        TrustRiskLevel.Critical => "Lucid wants to make a significant change — review carefully:",
        _ => "Lucid is requesting permission to proceed:",
    };

    /// <summary>
    /// Returns the approve button label for the given risk level.
    /// </summary>
    public string GetApproveLabel(TrustRiskLevel risk) => risk switch
    {
        TrustRiskLevel.None or TrustRiskLevel.Low => "Proceed",
        TrustRiskLevel.Medium => "Approve",
        TrustRiskLevel.High or TrustRiskLevel.Critical => "Yes, I understand — proceed",
        _ => "Approve",
    };
}
