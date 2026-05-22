namespace ExplainMyPC.Services.Companion;

/// <summary>
/// Display mode for the Companion Overlay Window.
/// Controls both the window size and which UI layout is rendered.
/// </summary>
public enum CompanionMode
{
    /// <summary>64×64 circular button — minimized, lowest footprint.</summary>
    Bubble   = 0,

    /// <summary>320×60 horizontal strip — status visible, no conversation.</summary>
    Compact  = 1,

    /// <summary>360×560 full panel — conversation history and input visible.</summary>
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
/// </summary>
public sealed record CompanionMessage
{
    public required string                  Id        { get; init; }
    public required CompanionMessageRole    Role      { get; init; }
    public required string                  Text      { get; init; }
    public required DateTimeOffset          Timestamp { get; init; }
    public          CompanionMessageCategory Category  { get; init; } = CompanionMessageCategory.Answer;
    public          string?                 ActionId  { get; init; }

    // ── Display helpers ────────────────────────────────────────────────────────

    public bool IsUserMessage   => Role == CompanionMessageRole.User;
    public bool IsSystemMessage => Role == CompanionMessageRole.System;

    public string RoleLabel => Role == CompanionMessageRole.User ? "You" : "ExplainMyPC";
    public string TimeLabel => Timestamp.LocalDateTime.ToString("HH:mm");

    /// <summary>Visibility for the user-side bubble layout.</summary>
    public string UserBubbleVisibility   => IsUserMessage   ? "Visible" : "Collapsed";

    /// <summary>Visibility for the system-side bubble layout.</summary>
    public string SystemBubbleVisibility => IsSystemMessage ? "Visible" : "Collapsed";

    /// <summary>Segoe MDL2 glyph for the message category.</summary>
    public string CategoryGlyph => Category switch
    {
        CompanionMessageCategory.Warning  => "",   // Warning shield
        CompanionMessageCategory.Insight  => "",   // Lightbulb
        CompanionMessageCategory.Action   => "",   // Wrench
        CompanionMessageCategory.Workflow => "",   // Flow
        CompanionMessageCategory.Error    => "",   // Error badge
        CompanionMessageCategory.Welcome  => "",   // Info
        _                                 => "",   // Info (Answer)
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
    public string HasActionVisibility => !string.IsNullOrEmpty(ActionId) ? "Visible" : "Collapsed";
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
    public required string Query       { get; init; }
}
