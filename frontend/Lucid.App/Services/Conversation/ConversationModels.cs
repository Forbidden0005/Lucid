using Lucid.Services.Companion;

namespace Lucid.Services.Conversation;

// ── Intents ───────────────────────────────────────────────────────────────────

/// <summary>
/// Resolved intent category for a companion query.
/// Determined entirely by deterministic keyword matching — no ML, no cloud NLP.
/// Values are spaced so new intents can be inserted within each band without collisions.
/// </summary>
public enum ConversationIntent
{
    // ── Operational questions (10-19) ─────────────────────────────────────────
    /// <summary>"Why is my PC slow?" — performance analysis</summary>
    WhyIsSlow               = 10,
    /// <summary>"Why is it hot / overheating?" — thermal analysis</summary>
    WhyIsHot                = 11,
    /// <summary>"Why is disk full / storage low?" — disk pressure</summary>
    WhyIsDiskFull           = 12,
    /// <summary>"Why is memory high / RAM full?" — memory pressure</summary>
    WhyIsMemoryHigh         = 13,
    /// <summary>"What changed / what happened recently?" — delta</summary>
    WhyDidSomethingChange   = 14,

    // ── Navigation requests (20-29) ──────────────────────────────────────────
    /// <summary>"Show / open repairs / fixes" — Repairs page</summary>
    OpenRepairs             = 20,
    /// <summary>"Show / open replay / history" — Replay page</summary>
    OpenReplay              = 21,
    /// <summary>"Show / open storage" — Storage page</summary>
    OpenStorage             = 22,
    /// <summary>"Show / open timeline / events" — Timeline page</summary>
    OpenTimeline            = 23,
    /// <summary>"Investigate / evidence graph" — Investigation page</summary>
    OpenInvestigation       = 24,
    /// <summary>"Historical / long-term / trends" — Historical page</summary>
    OpenHistorical          = 25,

    // ── Workflow guidance (30-39) ─────────────────────────────────────────────
    /// <summary>"Help me investigate…" — guided investigation workflow</summary>
    InvestigateProblem      = 30,
    /// <summary>"Compare before/after…" — replay delta</summary>
    CompareChanges          = 31,
    /// <summary>"Explain this recommendation…" — recommendation walkthrough</summary>
    ExplainRecommendation   = 32,
    /// <summary>"Review what happened…" — timeline walkthrough</summary>
    ReviewTimeline          = 33,
    /// <summary>"Start a guided workflow / repair flow" — remediationguide</summary>
    StartGuidedWorkflow     = 34,

    // ── Contextual assistance (40-49) ─────────────────────────────────────────
    /// <summary>"What's in my downloads?" — Downloads folder context</summary>
    FindRecentDownloads     = 40,
    /// <summary>"Help with these clipboard files" — clipboard context</summary>
    ReviewClipboardFiles    = 41,
    /// <summary>"What am I doing / what's active?" — current context</summary>
    ExplainCurrentContext   = 42,
    /// <summary>General status summary of the current state</summary>
    SummarizeCurrentState   = 43,

    // ── General (50-59) ───────────────────────────────────────────────────────
    /// <summary>"Hi / hello / hey" — greeting</summary>
    Greeting                = 50,
    /// <summary>"What can you do / help" — capability list</summary>
    Help                    = 51,

    // ── Fallback ─────────────────────────────────────────────────────────────
    /// <summary>No keywords matched — provide general status</summary>
    Unknown                 = 99,
}

// ── Intent resolution result ──────────────────────────────────────────────────

/// <summary>
/// Output of <see cref="ConversationIntentResolver"/>.
/// Exposes confidence and matched keywords for full explainability.
/// </summary>
public sealed record IntentMatch
{
    public ConversationIntent Intent            { get; init; }
    /// <summary>0–100 confidence in the resolution.</summary>
    public int                ConfidencePercent { get; init; } = 70;
    /// <summary>Comma-separated list of matched trigger words.</summary>
    public string             MatchedKeywords   { get; init; } = string.Empty;
    /// <summary>Human-readable explanation of why this intent was chosen.</summary>
    public string             ReasonExplained   { get; init; } = string.Empty;
}

// ── Evidence sources ──────────────────────────────────────────────────────────

/// <summary>
/// Platform service categories consulted when building a response.
/// Exposed on each response for full evidence transparency.
/// </summary>
public enum EvidenceSource
{
    Telemetry           = 0,
    InsightEngine       = 1,
    TimelineEvents      = 2,
    EvidenceGraph       = 3,
    ReplayDelta         = 4,
    OperationHistory    = 5,
    DesktopContext      = 6,
    StorageAnalysis     = 7,
    ProcessIntelligence = 8,
    NarrativeEngine     = 9,
}

// ── Query model ───────────────────────────────────────────────────────────────

/// <summary>
/// Enriched query submitted to <see cref="IOperationalConversationService"/>.
/// Carries both the raw text and any ambient desktop context available at query time.
/// </summary>
public sealed record OperationalPrompt
{
    public required string Text                  { get; init; }
    /// <summary>CurrentOperationalFocus from the desktop context service, if any.</summary>
    public          string? DesktopContextFocus  { get; init; }
    /// <summary>Active app category at query time, for contextual tailoring.</summary>
    public          string? ActiveAppCategory    { get; init; }
    /// <summary>Whether the clipboard carries file references at query time.</summary>
    public          bool    ClipboardHasFiles    { get; init; }
    public          DateTimeOffset Timestamp     { get; init; } = DateTimeOffset.Now;
}

// ── Confidence model ──────────────────────────────────────────────────────────

/// <summary>
/// Response-level confidence — derived from the evidence available, not invented.
/// </summary>
public sealed record ResponseConfidence
{
    public int    Percent { get; init; }
    public string Basis   { get; init; } = string.Empty;

    public string Label => Percent switch
    {
        >= 85 => "High",
        >= 65 => "Moderate",
        >= 40 => "Low",
        _     => "Uncertain",
    };

    /// <summary>Short chip label: "High · 87%" — shown in the companion UI.</summary>
    public string Display => $"{Label} · {Percent}%";
}

// ── Response model ────────────────────────────────────────────────────────────

/// <summary>
/// Rich response from <see cref="IOperationalConversationService"/>.
/// Carries the prose text PLUS structured evidence, actions, and confidence.
/// </summary>
public sealed record OperationalResponse
{
    public required string                             Text             { get; init; }
    public required IntentMatch                        Intent           { get; init; }
    public required ResponseConfidence                 Confidence       { get; init; }
    public          IReadOnlyList<ConversationEvidenceItem> Evidence    { get; init; } = [];
    public          IReadOnlyList<SuggestedAction>     SuggestedActions { get; init; } = [];
    /// <summary>Explicit uncertainty caveat when confidence is low.</summary>
    public          string?                            UncertaintyNote  { get; init; }
    public          CompanionMessageCategory           Category         { get; init; }
    /// <summary>
    /// Evidence sources consulted — for the UI badge and explainability log.
    /// </summary>
    public          IReadOnlyList<EvidenceSource>      EvidenceSources  { get; init; } = [];
}

// ── Contextual suggestion ─────────────────────────────────────────────────────

/// <summary>
/// A lightweight proactive suggestion surfaced from desktop context.
/// Clicking converts it to a QuickAction query.
/// These are ALWAYS optional and NEVER intrusive.
/// </summary>
public sealed record ContextualSuggestion
{
    public required string           Id       { get; init; }
    public required string           Text     { get; init; }
    public required string           Glyph    { get; init; }
    public required NavigationTarget Target   { get; init; }
    /// <summary>Auto-fills the input box when the chip is tapped.</summary>
    public          string?          QueryStub { get; init; }
}
