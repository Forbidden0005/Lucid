using ExplainMyPC.Services.Intelligence;

namespace ExplainMyPC.Services.Repair;

/// <summary>
/// Rich metadata for a single repair executor, surfaced in the UI before the
/// user confirms execution.
///
/// This complements <see cref="SystemAction"/> (which carries impact/effort/risk
/// used on insight cards) with repair-specific fields: expected duration,
/// restart requirement, and a plain-English explanation of the tool's mechanism.
///
/// Authoring:
///   Repair executors expose their metadata as a public static readonly property
///   so ViewModels can retrieve it without constructing an executor instance.
///   <see cref="RepairOperationCatalog"/> aggregates all entries for lookup by
///   action ID.
/// </summary>
public sealed record RepairOperationMetadata(
    /// <summary>The stable executor action ID, e.g. "action.repair.sfc-scannow".</summary>
    string     ActionId,

    /// <summary>Short display name shown in the repair card header.</summary>
    string     DisplayName,

    /// <summary>
    /// One-sentence description of the problem this repair addresses.
    /// Shown before the user runs the action.
    /// </summary>
    string     Purpose,

    /// <summary>
    /// Plain-English explanation of what the tool does internally.
    /// Shown in the expanded details section — helps power users understand
    /// the mechanism without requiring them to research it themselves.
    /// </summary>
    string     HowItWorks,

    /// <summary>Human-readable expected duration, e.g. "2–10 minutes".</summary>
    string     ExpectedDuration,

    /// <summary>Estimated risk level for UI colour-coding.</summary>
    ActionRisk Risk,

    /// <summary>
    /// True when the repair is fully reversible without additional steps.
    /// Shown as a reassurance chip on the confirmation dialog.
    /// </summary>
    bool       IsReversible,

    /// <summary>
    /// True when Windows must be restarted before the repair takes effect.
    /// A prominent warning banner is shown when this is true.
    /// </summary>
    bool       RequiresRestart,

    /// <summary>
    /// Shown when <see cref="RequiresRestart"/> is true — explains why a
    /// restart is needed in user-facing language.
    /// </summary>
    string?    RestartReason = null,

    /// <summary>
    /// Plain-English note about how the operation can be undone (or why
    /// it cannot). Shown on the confirmation dialog.
    /// </summary>
    string?    ReversibilityNote = null,

    /// <summary>
    /// When true, the action reads system state but makes no changes.
    /// Used for executors like SFC that scan first, then repair.
    /// Displayed as "Scan &amp; repair" vs "Repair" in the UI.
    /// </summary>
    bool       IncludesScanPhase = false);


// ─── Catalog ──────────────────────────────────────────────────────────────────

/// <summary>
/// Static registry mapping each repair action ID to its
/// <see cref="RepairOperationMetadata"/>.
///
/// Used by ViewModels to populate the pre-execution explanation panel without
/// needing to hold a reference to a specific executor type.
/// </summary>
public static class RepairOperationCatalog
{
    private static readonly Dictionary<string, RepairOperationMetadata> s_entries =
        new(StringComparer.OrdinalIgnoreCase);

    static RepairOperationCatalog()
    {
        Register(new(
            ActionId:         "action.repair.sfc-scannow",
            DisplayName:      "System File Checker",
            Purpose:          "Scans and repairs corrupted or missing Windows system files.",
            HowItWorks:
                "SFC (System File Checker) verifies the integrity of every protected " +
                "Windows file against a cached copy stored in WinSxS. Any corrupted " +
                "or altered file is automatically replaced with the known-good version. " +
                "The scan runs entirely offline — no internet connection needed.",
            ExpectedDuration: "5–15 minutes",
            Risk:             ActionRisk.Low,
            IsReversible:     false,
            RequiresRestart:  false,
            ReversibilityNote:
                "Replaced files are backed up to %SystemRoot%\\System32\\catroot2 " +
                "and %SystemRoot%\\Logs\\CBS\\CBS.log records every change.",
            IncludesScanPhase: true));

        Register(new(
            ActionId:         "action.repair.dism-restore-health",
            DisplayName:      "DISM Component Store Repair",
            Purpose:          "Repairs the Windows component store used by Windows Update and SFC.",
            HowItWorks:
                "DISM (Deployment Image Servicing and Management) verifies and repairs the " +
                "Windows component store (WinSxS). When SFC cannot fix files because the " +
                "store itself is corrupted, DISM downloads replacement components from " +
                "Windows Update and repairs them. Run this before or after SFC when SFC " +
                "reports unrepairable files.",
            ExpectedDuration: "10–30 minutes",
            Risk:             ActionRisk.Low,
            IsReversible:     false,
            RequiresRestart:  false,
            ReversibilityNote:
                "DISM logs all operations to %SystemRoot%\\Logs\\DISM\\dism.log. " +
                "Changes cannot be manually undone but do not affect personal files.",
            IncludesScanPhase: true));

        Register(new(
            ActionId:         "action.network.flush-dns",
            DisplayName:      "Flush DNS Cache",
            Purpose:          "Clears cached DNS records that may be causing incorrect domain resolution.",
            HowItWorks:
                "Windows caches DNS lookups to speed up browsing. Stale or poisoned " +
                "cache entries can cause sites to not load or resolve to wrong addresses. " +
                "ipconfig /flushdns clears this cache — your browser will look up " +
                "fresh DNS records on the next visit to each site.",
            ExpectedDuration: "Under 5 seconds",
            Risk:             ActionRisk.Safe,
            IsReversible:     true,
            RequiresRestart:  false,
            ReversibilityNote:
                "Fully reversible — DNS records are re-cached automatically as you browse.",
            IncludesScanPhase: false));

        Register(new(
            ActionId:         "action.network.winsock-reset",
            DisplayName:      "Reset Winsock Catalog",
            Purpose:          "Repairs network connectivity issues caused by a corrupted Winsock catalog.",
            HowItWorks:
                "The Winsock (Windows Sockets) catalog is the layer between applications " +
                "and the network stack. Third-party software (VPNs, firewalls, antivirus) " +
                "can install LSP (Layered Service Provider) entries that corrupt it, " +
                "causing connection failures. 'netsh winsock reset' rebuilds the catalog " +
                "from scratch. A restart is required before the change takes effect.",
            ExpectedDuration: "Under 10 seconds (restart required)",
            Risk:             ActionRisk.Moderate,
            IsReversible:     false,
            RequiresRestart:  true,
            RestartReason:
                "The Winsock catalog is loaded at boot time. The reset takes effect " +
                "only after Windows restarts.",
            ReversibilityNote:
                "LSP entries installed by third-party software will be removed. " +
                "Re-installing those applications may be needed if they require LSPs.",
            IncludesScanPhase: false));

        Register(new(
            ActionId:         "action.apps.reset-windows-store",
            DisplayName:      "Reset Windows Store Cache",
            Purpose:          "Fixes Windows Store app installation or update failures.",
            HowItWorks:
                "wsreset.exe clears the Windows Store's local cache, which can become " +
                "corrupted and cause app downloads, updates, or launches to fail. " +
                "It does not remove installed apps or account data — only temporary " +
                "cache files used by the Store itself.",
            ExpectedDuration: "Under 30 seconds",
            Risk:             ActionRisk.Safe,
            IsReversible:     true,
            RequiresRestart:  false,
            ReversibilityNote:
                "Fully safe — only the Store's temporary cache is cleared. " +
                "Apps and purchases are not affected.",
            IncludesScanPhase: false));

        Register(new(
            ActionId:         "action.network.reset-adapter",
            DisplayName:      "Reset Network Stack",
            Purpose:          "Resolves persistent network connectivity or IP addressing problems.",
            HowItWorks:
                "Resets the TCP/IP stack and releases/renews the IP address lease. " +
                "This is a multi-step process: " +
                "(1) 'netsh int ip reset' rewrites IP-related registry keys to defaults; " +
                "(2) 'netsh int tcp reset' resets TCP parameters; " +
                "(3) 'ipconfig /release' releases the current IP lease from the DHCP server; " +
                "(4) 'ipconfig /renew' requests a fresh IP lease. " +
                "Most effective for 'Limited connectivity', 169.x.x.x addressing, and " +
                "hosts-file or routing-table corruption.",
            ExpectedDuration: "Under 30 seconds",
            Risk:             ActionRisk.Moderate,
            IsReversible:     false,
            RequiresRestart:  false,
            ReversibilityNote:
                "TCP/IP stack settings are restored to Windows defaults. " +
                "Manual static IP, VPN routes, or custom DNS configurations may need " +
                "to be reconfigured after this reset.",
            IncludesScanPhase: false));
    }

    private static void Register(RepairOperationMetadata m) =>
        s_entries[m.ActionId] = m;

    /// <summary>
    /// Returns the metadata for <paramref name="actionId"/>, or
    /// <see langword="null"/> if the action is not in the catalog.
    /// </summary>
    public static RepairOperationMetadata? TryGet(string actionId) =>
        s_entries.TryGetValue(actionId, out var m) ? m : null;

    /// <summary>All registered repair operation entries.</summary>
    public static IReadOnlyCollection<RepairOperationMetadata> All =>
        s_entries.Values;
}
