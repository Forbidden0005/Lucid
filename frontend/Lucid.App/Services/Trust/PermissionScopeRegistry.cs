namespace Lucid.Services.Trust;

/// <summary>
/// Metadata describing a single <see cref="PermissionScope"/> entry.
/// All fields are static/compile-time constants — nothing here reads from
/// disk, network, or runtime state.
/// </summary>
public sealed record ScopeDefinition
{
    /// <summary>The permission scope this definition describes.</summary>
    public required PermissionScope Scope             { get; init; }

    /// <summary>Risk level for this scope — drives consent requirements.</summary>
    public required TrustRiskLevel  Risk              { get; init; }

    /// <summary>Short human-readable label shown on consent cards.</summary>
    public required string          Label             { get; init; }

    /// <summary>One-sentence plain-English description of what this scope permits.</summary>
    public required string          Description       { get; init; }

    /// <summary>Whether actions in this scope can be rolled back by the rollback coordinator.</summary>
    public required bool            Reversible        { get; init; }

    /// <summary>Whether execution requires elevated (admin) privileges.</summary>
    public required bool            RequiresElevation { get; init; }

    /// <summary>
    /// Glyph character from Segoe MDL2 Assets used in consent cards.
    /// </summary>
    public required string          Glyph             { get; init; }
}

/// <summary>
/// Static registry of all <see cref="PermissionScope"/> definitions.
///
/// This is the single source of truth for risk levels, reversibility, elevation
/// requirements, and display metadata for every scope. All trust decisions that
/// need scope metadata must go through this registry — never hard-code risk levels
/// elsewhere.
///
/// This class is stateless and thread-safe. All members are static.
/// </summary>
public static class PermissionScopeRegistry
{
    // ── Scope table ───────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<PermissionScope, ScopeDefinition> _definitions =
        new Dictionary<PermissionScope, ScopeDefinition>
        {
            // ── Navigation / informational ────────────────────────────────────
            [PermissionScope.ExplorerNavigation] = new()
            {
                Scope             = PermissionScope.ExplorerNavigation,
                Risk              = TrustRiskLevel.None,
                Label             = "Open Folder",
                Description       = "Opens a folder in File Explorer. Nothing is modified.",
                Reversible        = true,
                RequiresElevation = false,
                Glyph             = "",  // FolderOpen
            },

            [PermissionScope.SystemToolLaunch] = new()
            {
                Scope             = PermissionScope.SystemToolLaunch,
                Risk              = TrustRiskLevel.None,
                Label             = "Open System Tool",
                Description       = "Opens a Windows system utility. Nothing is modified.",
                Reversible        = true,
                RequiresElevation = false,
                Glyph             = "",  // Settings
            },

            [PermissionScope.SettingsNavigation] = new()
            {
                Scope             = PermissionScope.SettingsNavigation,
                Risk              = TrustRiskLevel.None,
                Label             = "Open Settings",
                Description       = "Opens a Windows Settings page. Nothing is modified.",
                Reversible        = true,
                RequiresElevation = false,
                Glyph             = "",  // Settings (gear)
            },

            [PermissionScope.SystemInfoRead] = new()
            {
                Scope             = PermissionScope.SystemInfoRead,
                Risk              = TrustRiskLevel.None,
                Label             = "Read System Info",
                Description       = "Reads process, startup, or disk information. No changes are made.",
                Reversible        = true,
                RequiresElevation = false,
                Glyph             = "",  // Diagnostic
            },

            // ── Reversible operational ────────────────────────────────────────
            [PermissionScope.CacheCleanup] = new()
            {
                Scope             = PermissionScope.CacheCleanup,
                Risk              = TrustRiskLevel.Low,
                Label             = "Clear Cache",
                Description       = "Deletes temporary or cached files. Windows will rebuild caches as needed.",
                Reversible        = true,
                RequiresElevation = false,
                Glyph             = "",  // Delete
            },

            [PermissionScope.StartupManagement] = new()
            {
                Scope             = PermissionScope.StartupManagement,
                Risk              = TrustRiskLevel.Medium,
                Label             = "Startup Entry",
                Description       = "Enables or disables a startup application. A snapshot backup is created first.",
                Reversible        = true,
                RequiresElevation = false,
                Glyph             = "",  // Flag
            },

            [PermissionScope.ProcessTermination] = new()
            {
                Scope             = PermissionScope.ProcessTermination,
                Risk              = TrustRiskLevel.Medium,
                Label             = "End Process",
                Description       = "Terminates a running process. Unsaved work in that process will be lost.",
                Reversible        = true,   // user can relaunch
                RequiresElevation = false,
                Glyph             = "",  // Cancel
            },

            [PermissionScope.RecycleBinEmpty] = new()
            {
                Scope             = PermissionScope.RecycleBinEmpty,
                Risk              = TrustRiskLevel.Medium,
                Label             = "Empty Recycle Bin",
                Description       = "Permanently removes all files currently in the Recycle Bin.",
                Reversible        = false,
                RequiresElevation = false,
                Glyph             = "",  // Delete
            },

            // ── Elevated operational ──────────────────────────────────────────
            [PermissionScope.WindowsRepair] = new()
            {
                Scope             = PermissionScope.WindowsRepair,
                Risk              = TrustRiskLevel.High,
                Label             = "Windows Repair",
                Description       = "Runs SFC or DISM to repair Windows system files. Elevated. Cannot be undone.",
                Reversible        = false,
                RequiresElevation = true,
                Glyph             = "",  // Repair
            },

            [PermissionScope.NetworkReset] = new()
            {
                Scope             = PermissionScope.NetworkReset,
                Risk              = TrustRiskLevel.High,
                Label             = "Network Reset",
                Description       = "Resets network adapters or Winsock stack. May require a restart to take effect.",
                Reversible        = false,
                RequiresElevation = true,
                Glyph             = "",  // NetworkAdapter
            },

            [PermissionScope.AppReset] = new()
            {
                Scope             = PermissionScope.AppReset,
                Risk              = TrustRiskLevel.Medium,
                Label             = "Reset App",
                Description       = "Resets a Windows app (e.g. Windows Store) to its default state.",
                Reversible        = false,
                RequiresElevation = true,
                Glyph             = "",  // Refresh
            },

            // ── File operations ───────────────────────────────────────────────
            [PermissionScope.LargeFileDeletion] = new()
            {
                Scope             = PermissionScope.LargeFileDeletion,
                Risk              = TrustRiskLevel.High,
                Label             = "Delete Large File",
                Description       = "Permanently deletes a file larger than the configured threshold.",
                Reversible        = false,
                RequiresElevation = false,
                Glyph             = "",  // Delete
            },

            [PermissionScope.DuplicateFileDeletion] = new()
            {
                Scope             = PermissionScope.DuplicateFileDeletion,
                Risk              = TrustRiskLevel.High,
                Label             = "Delete Duplicate",
                Description       = "Permanently deletes duplicate files, keeping one copy per group.",
                Reversible        = false,
                RequiresElevation = false,
                Glyph             = "",  // Delete
            },

            [PermissionScope.OldDownloadsDeletion] = new()
            {
                Scope             = PermissionScope.OldDownloadsDeletion,
                Risk              = TrustRiskLevel.Medium,
                Label             = "Clean Old Downloads",
                Description       = "Removes files from the Downloads folder that haven't been accessed recently.",
                Reversible        = false,
                RequiresElevation = false,
                Glyph             = "",  // Delete
            },

            // ── Companion / context ───────────────────────────────────────────
            [PermissionScope.DesktopContextRead] = new()
            {
                Scope             = PermissionScope.DesktopContextRead,
                Risk              = TrustRiskLevel.None,
                Label             = "Read Desktop Context",
                Description       = "Reads the active window title and folder path to provide contextual help.",
                Reversible        = true,
                RequiresElevation = false,
                Glyph             = "",  // View
            },

            [PermissionScope.ClipboardRead] = new()
            {
                Scope             = PermissionScope.ClipboardRead,
                Risk              = TrustRiskLevel.Low,
                Label             = "Read Clipboard",
                Description       = "Reads clipboard text to improve contextual suggestions. Never stores or uploads clipboard data.",
                Reversible        = true,
                RequiresElevation = false,
                Glyph             = "",  // Paste
            },

            [PermissionScope.CompanionMessage] = new()
            {
                Scope             = PermissionScope.CompanionMessage,
                Risk              = TrustRiskLevel.None,
                Label             = "Companion Message",
                Description       = "Sends a message in the companion chat panel. Visible to you immediately.",
                Reversible        = true,
                RequiresElevation = false,
                Glyph             = "",  // Message
            },
        };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the <see cref="ScopeDefinition"/> for the given scope.
    /// Throws <see cref="ArgumentOutOfRangeException"/> if the scope is not registered —
    /// this should never happen; it means a new scope was added without a registry entry.
    /// </summary>
    public static ScopeDefinition Get(PermissionScope scope)
    {
        if (_definitions.TryGetValue(scope, out var def)) return def;
        throw new ArgumentOutOfRangeException(nameof(scope),
            $"PermissionScope '{scope}' has no entry in PermissionScopeRegistry. " +
            "Add a ScopeDefinition for the new scope.");
    }

    /// <summary>Returns the risk level for the given scope.</summary>
    public static TrustRiskLevel RiskFor(PermissionScope scope) => Get(scope).Risk;

    /// <summary>Returns true if the given scope requires elevated privileges.</summary>
    public static bool RequiresElevation(PermissionScope scope) => Get(scope).RequiresElevation;

    /// <summary>Returns true if the given scope supports rollback.</summary>
    public static bool IsReversible(PermissionScope scope) => Get(scope).Reversible;

    /// <summary>All registered scope definitions.</summary>
    public static IEnumerable<ScopeDefinition> All => _definitions.Values;
}
