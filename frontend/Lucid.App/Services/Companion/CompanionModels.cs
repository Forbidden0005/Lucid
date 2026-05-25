namespace Lucid.Services.Companion;

// ── Navigation ────────────────────────────────────────────────────────────────

/// <summary>
/// Page the companion wants to navigate to when a suggested action is tapped.
/// Maps 1-to-1 with the tag strings in MainWindow.NavigateToPage().
/// </summary>
public enum NavigationTarget
{
    None          = 0,
    Dashboard     = 1,
    Insights      = 2,
    Timeline      = 3,
    Repairs       = 4,
    Storage       = 5,
    Processes     = 6,
    Security      = 7,
    Investigation = 8,
    Explain       = 9,
    Replay        = 10,
    Historical    = 11,
}

/// <summary>
/// A tappable action chip rendered below a companion response.
/// Navigates the main window to the target page — NEVER executes anything automatically.
///
/// Phase 17D: chips with an "automation:" prefix in NavigationTag are intercepted
/// by CompanionOverlayWindow and routed to the AutomationOrchestrator instead of
/// navigating to a page. The prefix is stripped before dispatching.
/// </summary>
public sealed record SuggestedAction
{
    public required string           Id          { get; init; }
    public required string           Label       { get; init; }
    public required string           Glyph       { get; init; }
    public required NavigationTarget Target      { get; init; }
    public          string?          Description { get; init; }

    /// <summary>
    /// Navigation tag for MainWindow.NavigateToPage(), or an "automation:{target}"
    /// string that the companion window routes to the automation orchestrator.
    /// If set explicitly, overrides the computed value from Target.
    /// </summary>
    public string? NavigationTag
    {
        get => _navigationTagOverride ?? Target switch
        {
            NavigationTarget.Dashboard     => "dashboard",
            NavigationTarget.Insights      => "insights",
            NavigationTarget.Timeline      => "timeline",
            NavigationTarget.Repairs       => "repairs",
            NavigationTarget.Storage       => "storage",
            NavigationTarget.Processes     => "processes",
            NavigationTarget.Security      => "security",
            NavigationTarget.Investigation => "investigation",
            NavigationTarget.Explain       => "explain",
            NavigationTarget.Replay        => "replay",
            NavigationTarget.Historical    => "historical",
            _                              => string.Empty,
        };
        init => _navigationTagOverride = value;
    }
    private readonly string? _navigationTagOverride;

    /// <summary>True if this chip routes to the automation orchestrator.</summary>
    public bool IsAutomationAction =>
        NavigationTag?.StartsWith("automation:", StringComparison.Ordinal) == true;

    /// <summary>Automation target extracted from the "automation:{target}" tag.</summary>
    public string? AutomationTarget =>
        IsAutomationAction ? NavigationTag!["automation:".Length..] : null;
}

/// <summary>
/// A single evidence item shown in the evidence badge on a system message.
/// Exposes which platform service provided the data for a response.
/// </summary>
public sealed record ConversationEvidenceItem
{
    public required string Source  { get; init; }
    public required string Summary { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Display mode for the Companion Overlay Window.
/// Controls both the window size and which UI layout is rendered.
/// </summary>
public enum CompanionMode
{
    /// <summary>64x64 circular button — minimized, lowest footprint.</summary>
    Bubble   = 0,

    /// <summary>320x60 horizontal strip — status visible, no conversation.</summary>
    Compact  = 1,

    /// <summary>360x560 full panel — conversation history and input visible.</summary>
    Expanded = 2,
}

/// <summary>
/// Who authored a companion conversation message.
/// </summary>
public enum CompanionMessageRole
{
    User   = 0,
    System = 1,
}

/// <summary>
/// Semantic category of a companion message.
/// Drives glyph selection and accent colour in the message bubble.
/// </summary>
public enum CompanionMessageCategory
{
    Answer   = 0,
    Warning  = 1,
    Insight  = 2,
    Action   = 3,
    Workflow = 4,
    Error    = 5,
    Welcome  = 6,
}

/// <summary>
/// An immutable conversation message in the companion session.
/// All display helpers are computed from the message data — no converters required.
///
/// Phase 17C enrichment fields:
///   SuggestedActions — navigation chips rendered below system messages
///   EvidenceItems    — evidence badge showing which sources were consulted
///   ConfidenceDisplay — chip label e.g. "High 87%"
///   UncertaintyNote  — explicit caveat when confidence is low
/// </summary>
public sealed record CompanionMessage
{
    public required string                   Id               { get; init; }
    public required CompanionMessageRole     Role             { get; init; }
    public required string                   Text             { get; init; }
    public required DateTimeOffset           Timestamp        { get; init; }
    public          CompanionMessageCategory Category         { get; init; } = CompanionMessageCategory.Answer;
    public          string?                  ActionId         { get; init; }

    // ── Phase 17C enrichment ───────────────────────────────────────────────────

    /// <summary>Navigation action chips shown below this message (system messages only).</summary>
    public IReadOnlyList<SuggestedAction>          SuggestedActions  { get; init; } = [];
    /// <summary>Evidence items shown in the evidence badge (system messages only).</summary>
    public IReadOnlyList<ConversationEvidenceItem> EvidenceItems     { get; init; } = [];
    /// <summary>Short confidence chip — null hides the chip entirely.</summary>
    public string?                                 ConfidenceDisplay { get; init; }
    /// <summary>Explicit uncertainty caveat — shown in italics when non-null.</summary>
    public string?                                 UncertaintyNote   { get; init; }

    // ── Display helpers ────────────────────────────────────────────────────────

    public bool IsUserMessage   => Role == CompanionMessageRole.User;
    public bool IsSystemMessage => Role == CompanionMessageRole.System;

    public string RoleLabel => Role == CompanionMessageRole.User ? "You" : "Lucid";
    public string TimeLabel => Timestamp.LocalDateTime.ToString("HH:mm");

    /// <summary>Visibility for the user-side bubble layout.</summary>
    public string UserBubbleVisibility   => IsUserMessage   ? "Visible" : "Collapsed";

    /// <summary>Visibility for the system-side bubble layout.</summary>
    public string SystemBubbleVisibility => IsSystemMessage ? "Visible" : "Collapsed";

    /// <summary>Segoe MDL2 glyph for the message category.</summary>
    public string CategoryGlyph => Category switch
    {
        CompanionMessageCategory.Warning  => "",  // Warning shield
        CompanionMessageCategory.Insight  => "",  // Lightbulb
        CompanionMessageCategory.Action   => "",  // Wrench
        CompanionMessageCategory.Workflow => "",  // Flow
        CompanionMessageCategory.Error    => "",  // Error badge
        CompanionMessageCategory.Welcome  => "",  // Info
        _                                 => "",  // Info (Answer)
    };

    /// <summary>Hex accent color for the message category icon.</summary>
    public string CategoryColor => Category switch
    {
        CompanionMessageCategory.Warning  => "#FBBF24",
        CompanionMessageCategory.Insight  => "#60A5FA",
        CompanionMessageCategory.Action   => "#34D399",
        CompanionMessageCategory.Error    => "#F87171",
        CompanionMessageCategory.Welcome  => "#A78BFA",
        _                                 => "#A1A1AA",
    };

    /// <summary>Visibility for the action-link row — shown only when ActionId is set.</summary>
    public string HasActionVisibility =>
        !string.IsNullOrEmpty(ActionId) ? "Visible" : "Collapsed";

    // ── Phase 17C visibility helpers ───────────────────────────────────────────

    /// <summary>Show suggested action chips only on system messages that have them.</summary>
    public string SuggestedActionsVisibility =>
        SuggestedActions.Count > 0 && IsSystemMessage ? "Visible" : "Collapsed";

    /// <summary>Show evidence badge only on system messages that have evidence.</summary>
    public string EvidenceVisibility =>
        EvidenceItems.Count > 0 && IsSystemMessage ? "Visible" : "Collapsed";

    /// <summary>Show confidence chip only on system messages that have a display label.</summary>
    public string ConfidenceVisibility =>
        !string.IsNullOrEmpty(ConfidenceDisplay) && IsSystemMessage ? "Visible" : "Collapsed";

    /// <summary>Show uncertainty note only on system messages that have one.</summary>
    public string UncertaintyVisibility =>
        !string.IsNullOrEmpty(UncertaintyNote) && IsSystemMessage ? "Visible" : "Collapsed";
}

/// <summary>
/// A quick-action chip shown in the companion panel.
/// Sends a predefined query to the conversation engine when tapped.
/// No direct action execution — all answers are explanatory.
/// </summary>
public sealed record QuickAction
{
    public required string Id          { get; init; }
    public required string Label       { get; init; }
    public required string Glyph       { get; init; }
    public required string Description { get; init; }

    /// <summary>Natural-language query submitted to the conversation engine.</summary>
    public required string Query { get; init; }

    /// <summary>
    /// True when this chip was injected by the contextual suggestion engine
    /// based on desktop context (folder, clipboard, active app).
    /// False for the six static base chips.
    /// </summary>
    public bool IsContextual { get; init; }
}
