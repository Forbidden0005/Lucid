using Lucid.Services.Companion;

namespace Lucid.Services.Chat;

// ─────────────────────────────────────────────────────────────────────────────
// Chat session domain — the persistence-facing shape of a saved conversation.
//
// Dependency direction: Chat → Companion. The conversation message model
// (CompanionMessage / CompanionMessageRole / CompanionMessageCategory) already
// exists under Services/Companion and is the single definition of "a message in
// a Lucid conversation". This domain deliberately reuses it rather than
// declaring a parallel role enum that would immediately start drifting.
// Companion must never take a dependency back on Chat.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Rail-level metadata for one saved conversation.
///
/// This is the summary shown in the session list — it never carries the
/// transcript itself, so listing 500 conversations stays cheap.
/// </summary>
public sealed record ChatSessionSummary
{
    public required string         Id        { get; init; }
    public required string         Title     { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Timestamp of the most recent message. Drives rail ordering and date grouping.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Pinned sessions sort above everything else regardless of age.</summary>
    public bool IsPinned { get; init; }

    public int MessageCount { get; init; }

    /// <summary>Single-line excerpt of the last message, for the rail's secondary text.</summary>
    public string Preview { get; init; } = string.Empty;

    /// <summary>
    /// True while the title is still machine-derived from the first user message.
    /// Set to false permanently once the user renames the session, so auto-titling
    /// never overwrites a name the user chose.
    /// </summary>
    public bool IsAutoTitled { get; init; } = true;
}

/// <summary>
/// One persisted message in a conversation transcript.
///
/// Deliberately narrower than <see cref="CompanionMessage"/>: the transient
/// enrichment fields (suggested-action chips, evidence badges, confidence chips)
/// are rebuilt from live services on display and are not part of what a session
/// stores. Only the durable conversation content is kept.
/// </summary>
public sealed record ChatTranscriptEntry
{
    public required string                   Id        { get; init; }
    public required CompanionMessageRole     Role      { get; init; }
    public required string                   Text      { get; init; }
    public required DateTimeOffset           Timestamp { get; init; }
    public          CompanionMessageCategory Category  { get; init; } = CompanionMessageCategory.Answer;
}

// ── Rail grouping ────────────────────────────────────────────────────────────

/// <summary>
/// Bucket a session falls into in the rail. Order of the enum is the order the
/// groups are rendered.
/// </summary>
public enum ChatSessionGroupKind
{
    Pinned        = 0,
    Today         = 1,
    Yesterday     = 2,
    PreviousWeek  = 3,
    PreviousMonth = 4,
    Older         = 5,
}

/// <summary>A labelled run of sessions in the rail. Never empty.</summary>
public sealed record ChatSessionGroup
{
    public required ChatSessionGroupKind             Kind     { get; init; }
    public required string                           Label    { get; init; }
    public required IReadOnlyList<ChatSessionSummary> Sessions { get; init; }
}
