namespace ExplainMyPC.Services.Session;

/// <summary>
/// Immutable snapshot of the current operational session context.
///
/// Built by <see cref="ISessionContextService"/> whenever session state changes
/// (wake event, idle transition, insight onset/clear). All time-sensitive phase
/// flags (<see cref="IsPostBoot"/>, <see cref="IsPostWake"/>, etc.) compute
/// dynamically from stored anchor timestamps, so they remain accurate throughout
/// the narrative engine's evaluation even if the snapshot is a few seconds old.
///
/// Session phases:
///   Post-boot         — within 10 minutes of system startup; startup services
///                       and shell components are still initializing.
///   Startup settling  — within 5 minutes of ExplainMyPC launch (proxy for
///                       user login); startup apps are loading and competing
///                       for CPU, RAM, and disk.
///   Post-wake         — within 3 minutes of a detected sleep/resume cycle;
///                       OS memory compression, indexer re-scans, and syncing
///                       services create a characteristic transient spike.
///   Idle              — CPU has been below the 15% threshold for ≥ 3 minutes;
///                       machine is not under active workload.
///
/// Insight onset tracking:
///   <see cref="InsightOnsets"/> maps each active insight rule ID to the
///   moment it first appeared. This enables "this started X minutes ago"
///   and "this appeared shortly after waking" correlations.
///
/// Threading:
///   Instances are immutable records. Safe to read from any thread after the
///   reference is obtained from <see cref="ISessionContextService.Current"/>.
/// </summary>
public sealed record SessionContext
{
    // ── Anchor timestamps (stored; used as base for dynamic phase flags) ──────

    /// <summary>Approximate time when Windows finished booting.</summary>
    public DateTimeOffset SystemBootTime { get; init; }

    /// <summary>
    /// Time when ExplainMyPC was launched — used as a proxy for the moment
    /// the user's interactive session became active.
    /// </summary>
    public DateTimeOffset SessionStart { get; init; }

    /// <summary>
    /// Most recent sleep/resume event timestamp, or null if no wake has been
    /// detected since ExplainMyPC started (gap-detection heuristic).
    /// </summary>
    public DateTimeOffset? LastWakeTime { get; init; }

    // ── Idle state ────────────────────────────────────────────────────────────

    /// <summary>
    /// True when the CPU has been below the idle threshold for the minimum
    /// required consecutive period (≥ 3 minutes at &lt; 15 % CPU).
    /// </summary>
    public bool IsIdle { get; init; }

    /// <summary>
    /// How long the current idle period has lasted.
    /// Null when <see cref="IsIdle"/> is false.
    /// </summary>
    public TimeSpan? IdleDuration { get; init; }

    // ── Insight onset tracking ────────────────────────────────────────────────

    /// <summary>
    /// Maps insight rule IDs to the time they first appeared in the active
    /// finding set during this session. Updated whenever <see cref="ISessionContextService"/>
    /// receives an InsightsUpdated notification. Cleared when a finding resolves.
    /// </summary>
    public IReadOnlyDictionary<string, DateTimeOffset> InsightOnsets { get; init; } =
        new Dictionary<string, DateTimeOffset>();

    // ── Recent event timeline ─────────────────────────────────────────────────

    /// <summary>
    /// The most recent operational events, ordered newest-first.
    /// Capped at 50 entries. Used for structured timeline reasoning.
    /// </summary>
    public IReadOnlyList<SessionEvent> RecentEvents { get; init; } = [];

    // ── Dynamic phase flags (computed at read time from stored timestamps) ────

    /// <summary>Time elapsed since system boot.</summary>
    public TimeSpan SystemUptime => DateTimeOffset.Now - SystemBootTime;

    /// <summary>Time elapsed since ExplainMyPC was launched.</summary>
    public TimeSpan SessionAge => DateTimeOffset.Now - SessionStart;

    /// <summary>
    /// Time since the last detected wake event, or null if no wake has
    /// been detected this session.
    /// </summary>
    public TimeSpan? TimeSinceWake => LastWakeTime.HasValue
        ? DateTimeOffset.Now - LastWakeTime.Value
        : null;

    /// <summary>
    /// True within 10 minutes of system boot.
    /// During this window, OS background initialization can cause sustained
    /// elevated disk and CPU readings that are not indicative of user workload.
    /// </summary>
    public bool IsPostBoot => SystemUptime.TotalMinutes < 10.0;

    /// <summary>
    /// True within 5 minutes of ExplainMyPC launch (post-login proxy).
    /// During this window, startup apps are loading concurrently, causing
    /// transient CPU, RAM, and disk pressure that typically resolves naturally.
    /// </summary>
    public bool IsStartupSettling => SessionAge.TotalMinutes < 5.0;

    /// <summary>
    /// True within 3 minutes of a detected sleep/resume cycle.
    /// Post-wake resource spikes from indexing, sync clients, and OS cache
    /// warming are expected and typically transient.
    /// </summary>
    public bool IsPostWake => LastWakeTime.HasValue &&
                              (DateTimeOffset.Now - LastWakeTime.Value).TotalMinutes < 3.0;

    // ── Insight onset helpers ─────────────────────────────────────────────────

    /// <summary>
    /// How long the given insight has been continuously active, or null if
    /// it is not currently in the onset dictionary.
    /// </summary>
    public TimeSpan? InsightAge(string id) =>
        InsightOnsets.TryGetValue(id, out var t) ? DateTimeOffset.Now - t : null;

    /// <summary>
    /// True when the insight has been active for less than
    /// <paramref name="withinMinutes"/> minutes.
    /// </summary>
    public bool InsightIsRecent(string id, double withinMinutes = 5.0) =>
        InsightAge(id) is { } age && age.TotalMinutes <= withinMinutes;

    /// <summary>
    /// True when the insight first appeared within <paramref name="windowMinutes"/>
    /// minutes of the given session event time — enabling causal correlation.
    /// </summary>
    public bool InsightOnsetNear(string id, DateTimeOffset? eventTime, double windowMinutes = 5.0)
    {
        if (eventTime is null) return false;
        if (!InsightOnsets.TryGetValue(id, out var onset)) return false;
        return Math.Abs((onset - eventTime.Value).TotalMinutes) <= windowMinutes;
    }

    // ── Human-readable formatting helpers ─────────────────────────────────────

    /// <summary>
    /// Short phrase describing the current session phase, for embedding in
    /// narrative sentences. Returns an empty string in mid-session steady state.
    /// </summary>
    public string SessionPhrase => IsPostWake        ? "shortly after waking from sleep"
                                 : IsStartupSettling ? "during post-login startup"
                                 : IsPostBoot        ? "during the post-boot period"
                                 : string.Empty;

    /// <summary>
    /// Formats system uptime as a short human-readable string.
    /// Examples: "4 minutes", "2h 15m", "3 days".
    /// </summary>
    public string FormatUptime()
    {
        var up = SystemUptime;
        if (up.TotalMinutes < 60) return $"{(int)up.TotalMinutes} minute{((int)up.TotalMinutes == 1 ? "" : "s")}";
        if (up.TotalHours   < 24) return $"{up.Hours}h {up.Minutes}m";
        int days = (int)up.TotalDays;
        return $"{days} day{(days == 1 ? "" : "s")}";
    }

    /// <summary>
    /// Formats the age of an insight as a short human-readable string.
    /// Returns an empty string when the insight ID is not in the onset dictionary.
    /// </summary>
    public string FormatInsightAge(string id)
    {
        var age = InsightAge(id);
        if (age is null) return string.Empty;
        if (age.Value.TotalSeconds < 60) return "less than a minute";
        if (age.Value.TotalMinutes < 60)
        {
            int m = (int)age.Value.TotalMinutes;
            return $"{m} minute{(m == 1 ? "" : "s")}";
        }
        int h = (int)age.Value.TotalHours;
        return $"{h} hour{(h == 1 ? "" : "s")}";
    }

    /// <summary>
    /// Formats a <see cref="TimeSpan"/> as a short human-readable string.
    /// </summary>
    public static string FormatSpan(TimeSpan ts)
    {
        if (ts.TotalSeconds < 90)
        {
            int s = (int)ts.TotalSeconds;
            return $"{s} second{(s == 1 ? "" : "s")}";
        }
        if (ts.TotalHours < 1)
        {
            int m = (int)ts.TotalMinutes;
            return $"{m} minute{(m == 1 ? "" : "s")}";
        }
        int hr = (int)ts.TotalHours;
        int mn = ts.Minutes;
        return mn == 0 ? $"{hr}h" : $"{hr}h {mn}m";
    }
}
