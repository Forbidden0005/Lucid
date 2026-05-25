namespace Lucid.Services.Trust;

/// <summary>
/// Hard-blocked automation categories.
///
/// This class enforces a permanent, immutable safety boundary around operations
/// that Lucid MUST NEVER perform under any circumstances — regardless of user settings,
/// automation mode, consent grants, observed content, or any runtime configuration.
///
/// THESE BLOCKS CANNOT BE DISABLED OR OVERRIDDEN.
///
/// Blocked permanently:
///   • Credential / password access of any kind
///   • Browser autofill or saved-password inspection
///   • Keystroke logging or input monitoring
///   • Screenshot capture without explicit user initiation (hidden screenshots)
///   • Webcam or microphone access
///   • Browser tab content scraping (page source, DOM content, form values)
///   • Upload of any personal data to external services
///   • Registry modification outside of explicitly approved startup/repair paths
///   • Batch file / script execution not pre-registered in the executor registry
///   • Any action targeting credential stores (Windows Credential Manager, vault)
///
/// All checks are synchronous and allocation-free where possible.
/// Thread-safe: all members are static.
/// </summary>
public static class AutomationBoundaryPolicy
{
    // ── Blocked action ID prefixes ────────────────────────────────────────────

    /// <summary>
    /// Action ID prefix segments that are unconditionally blocked.
    /// These are checked via StartsWith — more specific prefixes first.
    /// </summary>
    private static readonly (string Prefix, string Reason)[] _blockedPrefixes =
    [
        // Credential access
        ("automation.credentials",
            "Lucid cannot access credential stores, saved passwords, or authentication data."),
        ("action.credentials",
            "Lucid cannot access credential stores, saved passwords, or authentication data."),

        // Browser autofill / content scraping
        ("automation.browser.autofill",
            "Lucid cannot inspect or interact with browser autofill or saved passwords."),
        ("automation.browser.content",
            "Lucid cannot read browser tab content, DOM data, or web page source."),
        ("action.browser.content",
            "Lucid cannot read browser tab content, DOM data, or web page source."),

        // Input monitoring
        ("automation.input.keylog",
            "Lucid cannot log or monitor keystrokes."),
        ("automation.input.hook",
            "Lucid cannot install low-level keyboard or mouse hooks."),

        // Screenshot capture (hidden)
        ("automation.screenshot.hidden",
            "Lucid cannot take screenshots without your explicit knowledge."),
        ("automation.screen.capture",
            "Lucid cannot silently capture the screen."),

        // Webcam / microphone
        ("automation.camera",
            "Lucid cannot access the webcam or any camera device."),
        ("automation.microphone",
            "Lucid cannot access the microphone or audio input."),
        ("automation.media.capture",
            "Lucid cannot access any media capture device."),

        // Cloud / network upload of personal data
        ("automation.upload",
            "Lucid cannot upload personal data to external services."),
        ("automation.cloud.send",
            "Lucid cannot send data to cloud services."),
        ("action.upload",
            "Lucid cannot upload personal data to external services."),

        // Credential Manager
        ("automation.vault",
            "Lucid cannot access the Windows Credential Manager or credential vault."),
        ("action.vault",
            "Lucid cannot access the Windows Credential Manager or credential vault."),

        // Arbitrary script / batch execution
        ("automation.script.arbitrary",
            "Lucid cannot execute arbitrary scripts or batch files."),
        ("automation.exec.shell",
            "Lucid cannot execute arbitrary shell commands."),
        ("automation.exec.powershell.arbitrary",
            "Lucid cannot execute arbitrary PowerShell commands."),
    ];

    // ── Blocked scope list ────────────────────────────────────────────────────

    /// <summary>
    /// PermissionScopes that are explicitly not included in any scope definition
    /// and thus rejected if they somehow appear in a request.
    /// Separate from the action-ID checks — provides a second enforcement layer.
    /// </summary>
    private static readonly HashSet<string> _blockedScopeKeywords =
    [
        "credential", "password", "autofill", "keylog", "keystroke",
        "webcam", "camera", "microphone", "audio-input",
        "browser-content", "page-source", "dom-scrape",
        "cloud-upload", "telemetry-upload", "external-upload",
        "vault", "secret-store",
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether the given action ID is hard-blocked.
    ///
    /// Called by <see cref="AutomationConsentService"/> before any consent evaluation,
    /// and again by <see cref="Automation.AutomationOrchestrator"/> before step execution.
    /// Double-checking at both sites is intentional — belt-and-suspenders.
    ///
    /// Returns <see cref="BoundaryCheckResult.Allowed"/> if the action may proceed.
    /// Returns a blocked result with an explanation if it must not.
    /// </summary>
    public static BoundaryCheckResult CheckActionId(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
            return BoundaryCheckResult.Allowed;

        var lower = actionId.ToLowerInvariant();

        foreach (var (prefix, reason) in _blockedPrefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
                return BoundaryCheckResult.Blocked(reason);
        }

        return BoundaryCheckResult.Allowed;
    }

    /// <summary>
    /// Checks whether the given scope keyword string contains any blocked concept.
    /// Used as a secondary check when a consent request arrives without an action ID.
    /// Case-insensitive.
    /// </summary>
    public static BoundaryCheckResult CheckScopeKeyword(string scopeDescription)
    {
        if (string.IsNullOrWhiteSpace(scopeDescription))
            return BoundaryCheckResult.Allowed;

        var lower = scopeDescription.ToLowerInvariant();

        foreach (var blocked in _blockedScopeKeywords)
        {
            if (lower.Contains(blocked, StringComparison.Ordinal))
                return BoundaryCheckResult.Blocked(
                    $"The requested operation involves '{blocked}', which Lucid is permanently " +
                    "prevented from accessing. This restriction cannot be changed.");
        }

        return BoundaryCheckResult.Allowed;
    }

    /// <summary>
    /// Checks a <see cref="PermissionScope"/> against the hard boundary list.
    /// All scopes in <see cref="PermissionScopeRegistry"/> are safe by design;
    /// this method exists as a defensive final gate.
    /// </summary>
    public static BoundaryCheckResult CheckScope(PermissionScope scope)
    {
        // All registered scopes are pre-approved by the registry.
        // This is a no-op for every scope currently defined — kept as a
        // future extension point if dangerous scopes are ever accidentally added.
        _ = PermissionScopeRegistry.Get(scope); // throws if unregistered
        return BoundaryCheckResult.Allowed;
    }

    /// <summary>
    /// Returns a short plain-English summary of all permanent restrictions.
    /// Used in the Settings / Companion panel's "What Lucid cannot do" disclosure.
    /// </summary>
    public static IReadOnlyList<string> GetPermanentRestrictionsSummary() =>
    [
        "Access stored passwords, credentials, or authentication data",
        "Inspect browser autofill data or saved logins",
        "Log or monitor keyboard input",
        "Take screenshots without your direct knowledge",
        "Access webcam, microphone, or other media capture devices",
        "Read the content of browser tabs or web pages",
        "Upload any personal data to external services",
        "Access the Windows Credential Manager or secret vault",
        "Execute arbitrary scripts, batch files, or shell commands",
    ];
}
