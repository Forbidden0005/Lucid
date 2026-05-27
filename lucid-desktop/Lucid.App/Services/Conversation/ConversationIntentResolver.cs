namespace Lucid.Services.Conversation;

/// <summary>
/// Deterministic keyword-based intent resolver for companion queries.
///
/// Resolution rules:
///   • No machine learning, no embeddings, no cloud NLP.
///   • Rules are evaluated top-to-bottom; first match wins.
///   • Confidence is a function of how many trigger keywords matched.
///   • Results expose matched keywords and plain-English reasoning
///     so the response can always explain why a particular intent was chosen.
///
/// Adding new intents: add a rule block in Resolve() and register
/// trigger words in the KeywordTable at the bottom of this file.
/// No other changes required.
/// </summary>
public sealed class ConversationIntentResolver
{
    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a natural-language query into an <see cref="IntentMatch"/>.
    /// Never throws; returns <see cref="ConversationIntent.Unknown"/> when
    /// no rule matches with at least one keyword hit.
    /// </summary>
    public IntentMatch Resolve(string query, OperationalPrompt? prompt = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return NoMatch();

        var q = query.ToLowerInvariant();

        // Try each intent in priority order; first confident match wins.
        return
            TryMatch(q, ConversationIntent.Help,                 _helpWords)             ??
            TryMatch(q, ConversationIntent.Greeting,              _greetingWords)         ??
            TryMatch(q, ConversationIntent.InvestigateProblem,    _investigateWords)      ??
            TryMatch(q, ConversationIntent.CompareChanges,        _compareWords)          ??
            TryMatch(q, ConversationIntent.WhyDidSomethingChange, _changeWords)           ??
            TryMatch(q, ConversationIntent.WhyIsHot,              _thermalWords)          ??
            TryMatch(q, ConversationIntent.WhyIsDiskFull,         _diskWords)             ??
            TryMatch(q, ConversationIntent.WhyIsMemoryHigh,       _ramWords)              ??
            TryMatch(q, ConversationIntent.WhyIsSlow,             _slowWords)             ??
            // ── Phase 17D: automation/launch intents (checked before generic nav) ──
            TryMatch(q, ConversationIntent.LaunchTaskManager,     _taskManagerWords)      ??
            TryMatch(q, ConversationIntent.LaunchDeviceManager,   _deviceManagerWords)    ??
            TryMatch(q, ConversationIntent.LaunchEventViewer,     _eventViewerWords)      ??
            TryMatch(q, ConversationIntent.LaunchStorageSettings, _storageSettingsWords)  ??
            TryMatch(q, ConversationIntent.LaunchStartupApps,     _startupAppsWords)      ??
            TryMatch(q, ConversationIntent.LaunchWindowsSecurity, _windowsSecurityWords)  ??
            TryMatch(q, ConversationIntent.OpenDownloadsFolder,   _openDownloadsWords)    ??
            TryMatch(q, ConversationIntent.OpenDocumentsFolder,   _openDocumentsWords)    ??
            TryMatch(q, ConversationIntent.LaunchNetworkSettings, _networkSettingsWords)  ??
            TryMatch(q, ConversationIntent.LaunchWindowsUpdate,   _windowsUpdateWords)    ??
            // ── Phase 18B: visual context (checked before standard nav) ────────
            TryMatch(q, ConversationIntent.WhatAmILookingAt,  _whatAmILookingAtWords)  ??
            TryMatch(q, ConversationIntent.LocateUiElement,   _locateUiElementWords)   ??
            TryMatch(q, ConversationIntent.RunVisualWorkflow, _runVisualWorkflowWords) ??
            // ── Standard navigation ────────────────────────────────────────────
            TryMatch(q, ConversationIntent.OpenRepairs,           _repairsWords)          ??
            TryMatch(q, ConversationIntent.OpenStorage,           _storageNavWords)       ??
            TryMatch(q, ConversationIntent.OpenTimeline,          _timelineNavWords)      ??
            TryMatch(q, ConversationIntent.OpenInvestigation,     _investigationNavWords) ??
            TryMatch(q, ConversationIntent.OpenReplay,            _replayNavWords)        ??
            TryMatch(q, ConversationIntent.OpenHistorical,        _historicalNavWords)    ??
            TryMatch(q, ConversationIntent.StartGuidedWorkflow,   _workflowWords)         ??
            TryMatch(q, ConversationIntent.ExplainRecommendation, _explainRecWords)       ??
            TryMatch(q, ConversationIntent.ReviewTimeline,        _reviewTimelineWords)   ??
            TryMatch(q, ConversationIntent.FindRecentDownloads,   _downloadsWords)        ??
            TryMatch(q, ConversationIntent.ReviewClipboardFiles,  _clipboardWords)        ??
            TryMatchContextual(q, prompt)                                                 ??
            TryMatch(q, ConversationIntent.SummarizeCurrentState, _statusWords)           ??
            NoMatch();
    }

    // ── Resolution helpers ─────────────────────────────────────────────────────

    private static IntentMatch? TryMatch(
        string query, ConversationIntent intent, string[] keywords)
    {
        var matched = keywords.Where(k => query.Contains(k, StringComparison.Ordinal)).ToList();
        if (matched.Count == 0) return null;

        var confidence = matched.Count switch
        {
            >= 3 => 92,
            2    => 83,
            _    => 72,
        };

        var reason = intent switch
        {
            ConversationIntent.WhyIsSlow             => "performance/lag keywords matched",
            ConversationIntent.WhyIsHot              => "thermal/temperature keywords matched",
            ConversationIntent.WhyIsDiskFull         => "disk/storage keywords matched",
            ConversationIntent.WhyIsMemoryHigh       => "RAM/memory keywords matched",
            ConversationIntent.WhyDidSomethingChange => "change/event keywords matched",
            ConversationIntent.InvestigateProblem    => "investigation keywords matched",
            ConversationIntent.CompareChanges        => "comparison keywords matched",
            ConversationIntent.Help                  => "help/capability keywords matched",
            ConversationIntent.Greeting              => "greeting keywords matched",
            ConversationIntent.OpenRepairs           => "repairs navigation keywords matched",
            ConversationIntent.OpenStorage           => "storage navigation keywords matched",
            ConversationIntent.OpenTimeline          => "timeline navigation keywords matched",
            ConversationIntent.OpenInvestigation     => "investigation page keywords matched",
            ConversationIntent.OpenReplay            => "replay navigation keywords matched",
            ConversationIntent.OpenHistorical        => "historical navigation keywords matched",
            ConversationIntent.StartGuidedWorkflow   => "guided workflow keywords matched",
            ConversationIntent.ExplainRecommendation => "recommendation explanation keywords matched",
            ConversationIntent.ReviewTimeline        => "timeline review keywords matched",
            ConversationIntent.FindRecentDownloads   => "downloads keywords matched",
            ConversationIntent.ReviewClipboardFiles  => "clipboard keywords matched",
            ConversationIntent.SummarizeCurrentState => "status/summary keywords matched",
            ConversationIntent.LaunchTaskManager     => "Task Manager launch keywords matched",
            ConversationIntent.LaunchDeviceManager   => "Device Manager launch keywords matched",
            ConversationIntent.LaunchEventViewer     => "Event Viewer launch keywords matched",
            ConversationIntent.LaunchStorageSettings => "storage settings keywords matched",
            ConversationIntent.LaunchStartupApps     => "startup apps launch keywords matched",
            ConversationIntent.LaunchWindowsSecurity => "Windows Security launch keywords matched",
            ConversationIntent.OpenDownloadsFolder   => "downloads folder keywords matched",
            ConversationIntent.OpenDocumentsFolder   => "documents folder keywords matched",
            ConversationIntent.LaunchNetworkSettings => "network settings keywords matched",
            ConversationIntent.LaunchWindowsUpdate   => "Windows Update keywords matched",
            ConversationIntent.WhatAmILookingAt      => "visual context description keywords matched",
            ConversationIntent.LocateUiElement       => "UI element location keywords matched",
            ConversationIntent.RunVisualWorkflow     => "visual workflow guidance keywords matched",
            _                                        => "keywords matched",
        };

        return new IntentMatch
        {
            Intent            = intent,
            ConfidencePercent = confidence,
            MatchedKeywords   = string.Join(", ", matched),
            ReasonExplained   = reason,
        };
    }

    /// <summary>
    /// Contextual intent resolution: uses desktop context (current folder, clipboard)
    /// to infer intent when no strong keyword match fired.
    /// </summary>
    private static IntentMatch? TryMatchContextual(string query, OperationalPrompt? prompt)
    {
        if (prompt is null) return null;

        // If clipboard has files and the query mentions anything file-related
        if (prompt.ClipboardHasFiles &&
            HasAny(query, "file", "clipboard", "copy", "paste", "these"))
            return new IntentMatch
            {
                Intent            = ConversationIntent.ReviewClipboardFiles,
                ConfidencePercent = 65,
                MatchedKeywords   = "(clipboard context)",
                ReasonExplained   = "query combined with clipboard file context",
            };

        // If current focus label contains "download" and query is vague
        if (prompt.DesktopContextFocus?.Contains("download", StringComparison.OrdinalIgnoreCase) == true &&
            HasAny(query, "this", "here", "folder", "clean", "help"))
            return new IntentMatch
            {
                Intent            = ConversationIntent.FindRecentDownloads,
                ConfidencePercent = 60,
                MatchedKeywords   = "(downloads context)",
                ReasonExplained   = "desktop context indicates Downloads folder",
            };

        return null;
    }

    private static bool HasAny(string text, params string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.Ordinal));

    private static IntentMatch NoMatch() => new()
    {
        Intent            = ConversationIntent.Unknown,
        ConfidencePercent = 45,
        MatchedKeywords   = string.Empty,
        ReasonExplained   = "no keywords matched; providing general status",
    };

    // ── Keyword tables ─────────────────────────────────────────────────────────
    //    Each array lists trigger words for that intent.
    //    Multiple hits raise confidence.

    private static readonly string[] _helpWords =
        ["help", "what can you", "can you", "what do you do", "what are you",
         "capabilities", "how do you work", "what questions"];

    private static readonly string[] _greetingWords =
        ["hello", "hi ", "hey ", "howdy", "good morning", "good afternoon", "good evening"];

    private static readonly string[] _slowWords =
        ["slow", "lag", "sluggish", "freeze", "frozen", "performance", "unresponsive",
         "hanging", "stutter", "fps", "taking forever", "why is it slow", "speed up"];

    private static readonly string[] _thermalWords =
        ["hot", "heat", "thermal", "temperature", "overheating", "overheat", "fan",
         "throttl", "cooling", "burning", "warm"];

    private static readonly string[] _diskWords =
        ["disk", "drive", "space", "storage", "ssd", "hdd", "nvme", "full",
         "running out", "low space", "no space", "disk usage", "disk full"];

    private static readonly string[] _ramWords =
        ["ram", "memory", "mem ", "heap", "out of memory", "ram usage",
         "memory pressure", "memory leak"];

    private static readonly string[] _changeWords =
        ["changed", "recent", "today", "what happened", "history", "timeline",
         "event", "last hour", "since", "different", "notice", "occurred"];

    private static readonly string[] _investigateWords =
        ["investigate", "root cause", "root-cause", "primary cause", "what is causing",
         "why exactly", "dig in", "deep dive", "evidence", "cause of"];

    private static readonly string[] _compareWords =
        ["compare", "before", "after", "before and after", "difference",
         "delta", "what improved", "what got worse", "compare to"];

    private static readonly string[] _repairsWords =
        ["repair", "fix", "repairs page", "show repairs", "open repairs",
         "take me to repairs", "see fixes"];

    private static readonly string[] _storageNavWords =
        ["storage page", "show storage", "open storage", "take me to storage",
         "storage analysis", "large files", "duplicates", "see storage"];

    private static readonly string[] _timelineNavWords =
        ["timeline page", "show timeline", "open timeline", "see timeline",
         "take me to timeline", "event log", "activity log"];

    private static readonly string[] _investigationNavWords =
        ["investigation page", "open investigation", "show investigation",
         "evidence graph", "causal graph", "take me to investigation"];

    private static readonly string[] _replayNavWords =
        ["replay page", "show replay", "open replay", "replay session",
         "take me to replay", "operational replay"];

    private static readonly string[] _historicalNavWords =
        ["historical", "trends", "long-term", "health score", "over time",
         "past data", "historical analytics", "show history"];

    private static readonly string[] _workflowWords =
        ["workflow", "guided", "step by step", "guide me", "walk me through",
         "start workflow", "run workflow", "help me fix"];

    private static readonly string[] _explainRecWords =
        ["explain", "why recommend", "why suggested", "what does this mean",
         "explain this", "what is this recommendation", "tell me about this fix"];

    private static readonly string[] _reviewTimelineWords =
        ["review", "walk through", "show me what", "summary of events",
         "what events", "overview of"];

    private static readonly string[] _downloadsWords =
        ["download", "downloads folder", "recent downloads", "files i downloaded",
         "clean downloads", "what's in downloads"];

    private static readonly string[] _clipboardWords =
        ["clipboard", "copied files", "what i copied", "clipboard files",
         "files in clipboard", "what's in clipboard"];

    private static readonly string[] _statusWords =
        ["status", "how is", "how's", "overall", "summary", "what's going on",
         "what is happening", "general", "current state", "my pc", "my system",
         "everything ok", "all good", "system health"];

    // ── Phase 17D: automation/launch keyword tables ────────────────────────────

    private static readonly string[] _taskManagerWords =
        ["task manager", "taskmgr", "open task manager", "show task manager",
         "launch task manager", "show processes", "open processes", "process list"];

    private static readonly string[] _deviceManagerWords =
        ["device manager", "devmgmt", "open device manager", "show device manager",
         "check drivers", "driver status", "hardware drivers", "driver issues",
         "driver errors", "yellow warning"];

    private static readonly string[] _eventViewerWords =
        ["event viewer", "eventvwr", "open event viewer", "event logs", "windows logs",
         "system logs", "application logs", "check logs", "see errors in logs",
         "event log", "crash log"];

    private static readonly string[] _storageSettingsWords =
        ["storage settings", "storage sense", "open storage settings",
         "show storage settings", "manage storage", "storage cleanup settings",
         "storage sense settings"];

    private static readonly string[] _startupAppsWords =
        ["startup apps", "startup applications", "open startup", "show startup apps",
         "manage startup", "startup settings", "apps at startup", "launch on boot",
         "run on startup", "startup items", "open startup tab"];

    private static readonly string[] _windowsSecurityWords =
        ["windows security", "defender", "open defender", "antivirus", "open security",
         "show windows security", "windows defender", "virus protection",
         "protection status", "security center"];

    private static readonly string[] _openDownloadsWords =
        ["open downloads", "show downloads folder", "open downloads folder",
         "take me to downloads", "downloads directory", "my downloads",
         "show me downloads", "navigate to downloads"];

    private static readonly string[] _openDocumentsWords =
        ["open documents", "show documents folder", "open documents folder",
         "my documents", "documents directory", "navigate to documents",
         "show me documents"];

    private static readonly string[] _networkSettingsWords =
        ["network settings", "open network", "network troubleshooter",
         "fix network", "troubleshoot network", "internet settings",
         "wifi settings", "ethernet settings", "network adapter"];

    private static readonly string[] _windowsUpdateWords =
        ["windows update", "check for updates", "open updates", "update windows",
         "update settings", "pending updates", "install updates", "update history"];

    // ── Phase 18B: visual context keyword tables ───────────────────────────────

    private static readonly string[] _whatAmILookingAtWords =
        ["what am i looking at", "what is this", "describe this window",
         "what page is this", "what app is this", "analyze screen",
         "what's on screen", "explain what i see", "what is this page",
         "identify this window", "what is open"];

    private static readonly string[] _locateUiElementWords =
        ["where is the", "find the toggle", "find the setting", "find the button",
         "locate the", "where do i click", "startup toggle", "find startup",
         "locate startup", "show me the toggle", "where is the option"];

    private static readonly string[] _runVisualWorkflowWords =
        ["show me how to", "visual guide", "guided steps to", "guide me to",
         "visual workflow", "show me where to click", "interactive guide",
         "step by step how to navigate", "navigate me to", "guide me through this"];
}
