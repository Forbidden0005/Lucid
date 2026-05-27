namespace Lucid.Services.Trust;

// ── Permission scopes ─────────────────────────────────────────────────────────

/// <summary>
/// Classifies what system domain an automation action operates in.
/// Drives the consent UI, risk badging, and boundary-policy checks.
/// </summary>
public enum PermissionScope
{
    // ── Navigation / informational ──────────────────────────────────────────
    /// <summary>Opens a folder in File Explorer. No state change.</summary>
    ExplorerNavigation      = 0,

    /// <summary>Opens a Windows system tool (Task Manager, Device Manager, etc.). No state change.</summary>
    SystemToolLaunch        = 1,

    /// <summary>Opens a Windows Settings deep-link. No state change.</summary>
    SettingsNavigation      = 2,

    /// <summary>Reads system information — processes, startup entries, disk usage. Read-only.</summary>
    SystemInfoRead          = 3,

    // ── Reversible operational ────────────────────────────────────────────
    /// <summary>Clears a system cache (temp files, browser cache, WU cache). Reversible.</summary>
    CacheCleanup            = 10,

    /// <summary>Toggles a startup entry on/off. Reversible via snapshot restore.</summary>
    StartupManagement       = 11,

    /// <summary>Terminates a running process. Reversible by relaunching the app.</summary>
    ProcessTermination      = 12,

    /// <summary>Empties the Recycle Bin. Not reversible — requires explicit acknowledgement.</summary>
    RecycleBinEmpty         = 13,

    // ── Elevated operational ──────────────────────────────────────────────
    /// <summary>Runs SFC or DISM repair tools. Elevated. Long-running. Not reversible.</summary>
    WindowsRepair           = 20,

    /// <summary>Resets a network adapter or Winsock. Elevated. Requires restart to take full effect.</summary>
    NetworkReset            = 21,

    /// <summary>Resets Windows Store. Elevated. Moderate impact.</summary>
    AppReset                = 22,

    // ── File operations ───────────────────────────────────────────────────
    /// <summary>Deletes a large file from the user's drive. Not reversible unless backup exists.</summary>
    LargeFileDeletion       = 30,

    /// <summary>Deletes one or more duplicate files. Not reversible without backup.</summary>
    DuplicateFileDeletion   = 31,

    /// <summary>Deletes files from the Downloads folder older than a threshold. Not reversible.</summary>
    OldDownloadsDeletion    = 32,

    // ── Companion / context ───────────────────────────────────────────────
    /// <summary>Reads the current desktop context (active window title, folder path). Read-only.</summary>
    DesktopContextRead      = 40,

    /// <summary>Reads the clipboard text for contextual suggestions. Read-only.</summary>
    ClipboardRead           = 41,

    /// <summary>Sends a follow-up message in the companion chat. User-visible.</summary>
    CompanionMessage        = 42,
}

// ── Risk level ────────────────────────────────────────────────────────────────

/// <summary>
/// Trust-framework risk level for a permission scope.
/// Maps to the existing <see cref="Automation.AutomationRiskLevel"/> but
/// is defined independently here so trust decisions are not coupled to
/// the automation execution model.
/// </summary>
public enum TrustRiskLevel
{
    /// <summary>Read-only or pure navigation. Nothing changes.</summary>
    None     = 0,

    /// <summary>Reversible change with minimal impact.</summary>
    Low      = 1,

    /// <summary>Reversible change with moderate system impact.</summary>
    Medium   = 2,

    /// <summary>Significant or irreversible system change.</summary>
    High     = 3,

    /// <summary>Irreversible, elevated-privilege, or high-impact operation.</summary>
    Critical = 4,
}

// ── Consent mode ──────────────────────────────────────────────────────────────

/// <summary>
/// Controls how aggressively the consent service gates automation actions.
///
/// The user sets this; it cannot be changed by automation logic, observed content,
/// or any code path other than an explicit user gesture in the Settings UI.
///
/// Default: <see cref="AskForMediumAndHighRisk"/>.
/// </summary>
public enum TrustConsentMode
{
    /// <summary>
    /// Lucid only explains and describes what it would do.
    /// No automation runs under any circumstances.
    /// Every capability produces guidance text only.
    /// </summary>
    ObserveOnly              = 0,

    /// <summary>
    /// Lucid walks the user through each step interactively.
    /// The user clicks through each action themselves.
    /// </summary>
    GuidedOnly               = 1,

    /// <summary>
    /// Every action — regardless of risk — requires explicit user confirmation.
    /// Low-risk open/launch operations still show a confirmation card.
    /// </summary>
    AskAlways                = 2,

    /// <summary>
    /// Medium and High risk actions require confirmation.
    /// None/Low risk actions (open folder, launch tool) may proceed after narration.
    /// This is the recommended default for power users.
    /// </summary>
    AskForMediumAndHighRisk  = 3,

    /// <summary>
    /// Only High and Critical risk actions require confirmation.
    /// None/Low/Medium actions proceed automatically after narration.
    /// Users must opt into this deliberately.
    /// </summary>
    AskHighRiskOnly          = 4,
}

// ── Trust posture ─────────────────────────────────────────────────────────────

/// <summary>
/// The current operational trust posture, derived from consent history,
/// recent high-risk activity, and user-configured mode.
/// Drives narration tone, badge prominence, and adaptive consent escalation.
/// </summary>
public enum TrustPosture
{
    /// <summary>Normal operation — trust history is clean.</summary>
    Standard    = 0,

    /// <summary>
    /// Cautious mode — multiple high-risk requests or recent denials have occurred.
    /// The trust manager adds extra narration and slows down confirmation waits.
    /// </summary>
    Cautious    = 1,

    /// <summary>
    /// Restricted mode — the user denied two or more high-risk requests in a row,
    /// or manually set ObserveOnly. All automation is paused until explicitly resumed.
    /// </summary>
    Restricted  = 2,
}

// ── Consent records ───────────────────────────────────────────────────────────

/// <summary>
/// Immutable request presented to the user before a consent-gated action executes.
/// Created by <see cref="AutomationConsentService"/> and surfaced in the companion overlay.
/// </summary>
public sealed record ConsentRequest
{
    /// <summary>Unique ID for this consent request (32-char hex).</summary>
    public required string           Id                { get; init; }

    /// <summary>Workflow / plan ID this request belongs to.</summary>
    public required string           WorkflowId        { get; init; }

    /// <summary>Short title shown on the consent card header.</summary>
    public required string           ActionTitle       { get; init; }

    /// <summary>The permission scope being requested.</summary>
    public required PermissionScope  Scope             { get; init; }

    /// <summary>Risk level for this specific action.</summary>
    public required TrustRiskLevel   Risk              { get; init; }

    /// <summary>
    /// Plain-English explanation of WHY this action was suggested.
    /// Produced by <see cref="AutomationTransparencyEngine"/>.
    /// </summary>
    public required string           WhySuggested      { get; init; }

    /// <summary>
    /// Plain-English description of WHAT will specifically change on the system.
    /// Produced by <see cref="ConsentExplanationService"/>.
    /// </summary>
    public required string           WhatChanges       { get; init; }

    /// <summary>
    /// How the change can be undone, or "This action cannot be undone" if irreversible.
    /// Produced by <see cref="ConsentExplanationService"/>.
    /// </summary>
    public required string           HowToUndo         { get; init; }

    /// <summary>Whether a rollback mechanism exists for this action.</summary>
    public required bool             CanRollback       { get; init; }

    /// <summary>Whether elevated privileges are required to execute this action.</summary>
    public required bool             RequiresElevation { get; init; }

    /// <summary>When this request was created.</summary>
    public required DateTimeOffset   RequestedAt       { get; init; }

    /// <summary>Factory helper — new random ID, 32 lowercase hex chars.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");
}

/// <summary>
/// Immutable record of the user's response to a <see cref="ConsentRequest"/>.
/// Stored in the audit log and used by <see cref="OperationalTrustManager"/>
/// to adapt the trust posture over time.
/// </summary>
public sealed record ConsentResponse
{
    /// <summary>ID of the corresponding <see cref="ConsentRequest"/>.</summary>
    public required string         RequestId    { get; init; }

    /// <summary>True if the user approved the action.</summary>
    public required bool           Approved     { get; init; }

    /// <summary>When the user responded.</summary>
    public required DateTimeOffset RespondedAt  { get; init; }

    /// <summary>How long the user took to respond (useful for trust heuristics).</summary>
    public required TimeSpan       DecisionTime { get; init; }

    /// <summary>
    /// Optional user-provided context or comment. Null if none.
    /// Reserved for future "why did you deny this?" prompt.
    /// </summary>
    public          string?        UserComment  { get; init; }
}

// ── Audit records ─────────────────────────────────────────────────────────────

/// <summary>
/// Complete audit ledger entry for a single automation action.
///
/// Every action that passes through the consent gate — approved or denied —
/// is written to the <see cref="AutomationAuditService"/> ledger.
/// Entries are retained for <see cref="AutomationAuditService.RetentionDays"/> days.
/// </summary>
public sealed record AutomationAuditEntry
{
    /// <summary>Unique ID (32-char hex).</summary>
    public required string           Id              { get; init; }

    /// <summary>When the action was requested.</summary>
    public required DateTimeOffset   RequestedAt     { get; init; }

    /// <summary>ID of the workflow / plan this action belongs to.</summary>
    public required string           WorkflowId      { get; init; }

    /// <summary>Short title of the action.</summary>
    public required string           ActionTitle     { get; init; }

    /// <summary>Executor action ID (e.g. "action.disk.clean-temp-files").</summary>
    public required string           ActionId        { get; init; }

    /// <summary>The permission scope that was requested.</summary>
    public required PermissionScope  Scope           { get; init; }

    /// <summary>Risk level of the action.</summary>
    public required TrustRiskLevel   Risk            { get; init; }

    /// <summary>The causal evidence that motivated this suggestion.</summary>
    public required string           EvidenceSummary { get; init; }

    /// <summary>Whether the user approved the action.</summary>
    public required bool             Approved        { get; init; }

    /// <summary>When the user approved or denied. Null if approval is still pending.</summary>
    public          DateTimeOffset?  DecidedAt       { get; init; }

    /// <summary>True if the action executed successfully after approval.</summary>
    public          bool?            Succeeded       { get; init; }

    /// <summary>Error message if the action failed. Null otherwise.</summary>
    public          string?          ErrorDetail     { get; init; }

    /// <summary>True if a rollback was performed after this action.</summary>
    public          bool             WasRolledBack   { get; init; }

    /// <summary>When the rollback occurred. Null if no rollback.</summary>
    public          DateTimeOffset?  RolledBackAt    { get; init; }

    /// <summary>Factory helper — new random ID, 32 lowercase hex chars.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");
}

// ── Trust events ─────────────────────────────────────────────────────────────

/// <summary>
/// Immutable event record published by the trust layer to the operational timeline.
/// Created by <see cref="OperationalTrustManager"/> and forwarded to
/// <see cref="Timeline.ITimelineAggregationService"/> via the standard AddExternalEvent path.
/// </summary>
public sealed record TrustEvent
{
    /// <summary>Unique ID (32-char hex).</summary>
    public required string         Id          { get; init; }

    /// <summary>When the event occurred.</summary>
    public required DateTimeOffset OccurredAt  { get; init; }

    /// <summary>Short human-readable description shown on the timeline card.</summary>
    public required string         Title       { get; init; }

    /// <summary>Optional detail text shown in the expanded card view.</summary>
    public          string?        Detail      { get; init; }

    /// <summary>Scope of the permission that was requested.</summary>
    public          PermissionScope? Scope     { get; init; }

    /// <summary>Risk level at the time of the event.</summary>
    public          TrustRiskLevel?  Risk      { get; init; }

    /// <summary>True if this event represents an approved consent.</summary>
    public          bool           Approved    { get; init; }

    /// <summary>Factory helper — new random ID, 32 lowercase hex chars.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");
}

// ── Boundary block result ─────────────────────────────────────────────────────

/// <summary>
/// Result returned by <see cref="AutomationBoundaryPolicy.CheckActionId"/>.
/// If <see cref="IsBlocked"/> is true, <see cref="Reason"/> explains why.
/// </summary>
public sealed record BoundaryCheckResult
{
    /// <summary>True if the action is hard-blocked and must not execute.</summary>
    public required bool   IsBlocked { get; init; }

    /// <summary>
    /// Human-readable reason for the block, shown in the companion narration.
    /// Null when not blocked.
    /// </summary>
    public          string? Reason   { get; init; }

    /// <summary>A clean "not blocked" result.</summary>
    public static readonly BoundaryCheckResult Allowed
        = new() { IsBlocked = false };

    /// <summary>Creates a blocked result with the given reason.</summary>
    public static BoundaryCheckResult Blocked(string reason)
        => new() { IsBlocked = true, Reason = reason };
}
