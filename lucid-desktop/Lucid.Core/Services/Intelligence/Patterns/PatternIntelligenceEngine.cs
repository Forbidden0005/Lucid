using System.Text.Json;
using Lucid.Services.Reasoning.Cognitive;
using Lucid.Services.Reasoning.Memory;

namespace Lucid.Services.Intelligence.Patterns;

/// <summary>
/// Analyzes historical inference records to produce a list of recognized
/// recurring <see cref="OperationalPattern"/> instances.
///
/// Cross-session persistence (Phase 25.1):
///   Call <see cref="LoadPersistedHistoryAsync"/> once at startup. The engine
///   then merges prior-session records with current-session records on every
///   <see cref="Analyze"/> call, giving patterns the full lifetime of the
///   machine's observation window rather than just the current session.
///
///   Persisted to: %LocalAppData%\Lucid\Data\inference-history.json
///   Retention cap: <see cref="MaxPersistedRecords"/> (500 records, evict oldest).
///   Save strategy: fire-and-forget async after each Analyze() call.
///
/// Integration:
///   - Input: <see cref="IReasoningMemoryService.GetRecent"/> history window
///     + loaded cross-session records
///   - Output: <see cref="IReadOnlyList{OperationalPattern}"/> sorted by confidence
///   - Side effect: updates <see cref="RecurrenceTracker"/> with each inference type
///
/// Transparency guarantee: every OperationalPattern is fully derivable from
/// the input history — no hidden state, no black-box weighting.
///
/// Thread-safe: history mutations are guarded by _historyLock.
/// </summary>
public sealed class PatternIntelligenceEngine
{
    private readonly RecurrenceTracker _tracker;

    // ── Cross-session history ─────────────────────────────────────────────────

    private readonly object                      _historyLock     = new();
    private          List<HistoricalInferenceRecord> _persistedHistory = [];
    private readonly string                      _historyPath;

    // ── Thresholds ────────────────────────────────────────────────────────────

    /// <summary>Minimum occurrences before a group becomes an OperationalPattern.</summary>
    private const int MinOccurrences      = 2;

    /// <summary>Maximum observation window analyzed per Analyze() call.</summary>
    private const int MaxHistoryEntries   = 200;

    /// <summary>Maximum records to retain across sessions.</summary>
    private const int MaxPersistedRecords = 500;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented          = false,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // ── Construction ──────────────────────────────────────────────────────────

    public PatternIntelligenceEngine(RecurrenceTracker? tracker = null)
    {
        _tracker     = tracker ?? new RecurrenceTracker();
        _historyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lucid", "Data", "inference-history.json");
    }

    // ── Startup seeding ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads persisted inference history from disk so the next Analyze() call
    /// has cross-session context. Safe to call from any thread; idempotent.
    /// Best-effort: failures are silently ignored so a corrupt file never
    /// prevents the app from starting.
    /// </summary>
    public async Task LoadPersistedHistoryAsync()
    {
        try
        {
            if (!File.Exists(_historyPath)) return;

            var json = await File.ReadAllTextAsync(_historyPath).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return;

            var dtos = JsonSerializer.Deserialize<List<InferenceRecordDto>>(json, s_jsonOptions);
            if (dtos is null || dtos.Count == 0) return;

            var records = dtos.Select(DtoToRecord).ToList();

            lock (_historyLock)
            {
                _persistedHistory = records;
            }
        }
        catch
        {
            // Best-effort — a missing or corrupt history file means we start fresh.
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Analyzes the provided current-session history window and returns detected
    /// patterns, sorted by confidence descending.
    ///
    /// Merges the current-session records with any prior-session records loaded
    /// via <see cref="LoadPersistedHistoryAsync"/> to produce cross-session patterns.
    /// The merged history is saved back to disk asynchronously after each call.
    /// </summary>
    public IReadOnlyList<OperationalPattern> Analyze(
        IReadOnlyList<HistoricalInferenceRecord> currentRecords)
    {
        if (currentRecords.Count == 0 && GetPersistedHistoryCount() == 0)
            return [];

        // ── Merge persisted + current, deduplicate by InferenceId ─────────────
        List<HistoricalInferenceRecord> merged;
        lock (_historyLock)
        {
            merged = [.. _persistedHistory];
        }

        var existingIds = new HashSet<Guid>(merged.Select(r => r.InferenceId));
        foreach (var r in currentRecords)
        {
            if (existingIds.Add(r.InferenceId))
                merged.Add(r);
        }

        // Cap to retention limit, keeping most recent
        if (merged.Count > MaxPersistedRecords)
        {
            merged = [.. merged
                .OrderByDescending(r => r.RecordedAtUtc)
                .Take(MaxPersistedRecords)
                .OrderBy(r => r.RecordedAtUtc)];
        }

        // Persist the merged result asynchronously so next session benefits
        lock (_historyLock) { _persistedHistory = merged; }
        _ = SaveHistoryAsync(merged);

        // ── Analyze the merged window ─────────────────────────────────────────
        var window = merged.TakeLast(MaxHistoryEntries).ToList();

        var groups = window
            .GroupBy(r => r.InferenceType, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= MinOccurrences)
            .ToList();

        var patterns = new List<OperationalPattern>(groups.Count);

        foreach (var group in groups)
        {
            var entries    = group.OrderBy(r => r.RecordedAtUtc).ToList();
            var patternKey = BuildPatternKey(group.Key, entries);
            var confidence = ComputePatternConfidence(entries);
            var isStable   = IsStablePattern(entries);
            var workload   = DominantWorkloadContext(entries);

            _tracker.Record(patternKey, workload);

            var allDomains = entries
                .SelectMany(e => e.Domains)
                .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(4)
                .ToList()
                .AsReadOnly();

            // Detect whether the pattern spans multiple sessions
            // (first and last occurrence more than 2 hours apart)
            bool isCrossSession = (entries.Last().RecordedAtUtc - entries.First().RecordedAtUtc)
                                   .TotalHours >= 2.0;

            patterns.Add(new OperationalPattern
            {
                PatternKey              = patternKey,
                PatternType             = group.Key,
                Description             = BuildDescription(group.Key, entries.Count, workload, isCrossSession),
                OccurrenceCount         = entries.Count,
                Domains                 = allDomains,
                PatternConfidence       = confidence,
                FirstSeenAt             = entries.First().RecordedAtUtc,
                LastSeenAt              = entries.Last().RecordedAtUtc,
                DominantWorkloadContext = workload,
                IsStable                = isStable,
            });
        }

        return [.. patterns
            .OrderByDescending(p => p.PatternConfidence)
            .ThenByDescending(p => p.OccurrenceCount)];
    }

    /// <summary>Exposes the underlying recurrence tracker for diagnostics.</summary>
    public RecurrenceTracker Tracker => _tracker;

    // ── Persistence helpers ────────────────────────────────────────────────────

    private async Task SaveHistoryAsync(List<HistoricalInferenceRecord> records)
    {
        try
        {
            var dir = Path.GetDirectoryName(_historyPath)!;
            Directory.CreateDirectory(dir);

            var dtos = records.Select(RecordToDto).ToList();
            var json = JsonSerializer.Serialize(dtos, s_jsonOptions);

            var tmp  = _historyPath + ".tmp";
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, _historyPath, overwrite: true);
        }
        catch
        {
            // Best-effort — a failed save is not user-visible.
        }
    }

    private int GetPersistedHistoryCount()
    {
        lock (_historyLock) { return _persistedHistory.Count; }
    }

    // ── Pattern analysis helpers ───────────────────────────────────────────────

    private static string BuildPatternKey(
        string                          inferenceType,
        List<HistoricalInferenceRecord> entries)
    {
        var topDomain = entries
            .SelectMany(e => e.Domains)
            .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key ?? "General";

        return $"{inferenceType}:{topDomain}".ToLowerInvariant();
    }

    private static double ComputePatternConfidence(
        List<HistoricalInferenceRecord> entries)
    {
        var highConfidenceCount = entries.Count(e =>
            e.ConfidenceAtTime >= ConfidenceLevel.High);

        double baseScore = (double)highConfidenceCount / entries.Count;

        double recurrenceBoost = entries.Count switch
        {
            >= 10 => 0.20,
            >= 5  => 0.15,
            >= 3  => 0.10,
            _     => 0.00,
        };

        var suppressedFraction = (double)entries.Count(e => e.WasSuppressed) / entries.Count;
        double suppressionPenalty = suppressedFraction * 0.30;

        return Math.Clamp(baseScore + recurrenceBoost - suppressionPenalty, 0.0, 0.95);
    }

    private static bool IsStablePattern(List<HistoricalInferenceRecord> entries)
    {
        if (entries.Count < 2) return false;
        var uniquePriorities = entries.Select(e => e.PriorityAtTime).Distinct().Count();
        return uniquePriorities <= 2;
    }

    private static string? DominantWorkloadContext(
        List<HistoricalInferenceRecord> entries)
    {
        var contexts = entries
            .Where(e => !string.IsNullOrEmpty(e.WorkloadContext))
            .GroupBy(e => e.WorkloadContext!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (contexts.Count == 0) return null;

        var top = contexts.First();
        return (double)top.Count() / entries.Count > 0.5 ? top.Key : null;
    }

    private static string BuildDescription(
        string  inferenceType,
        int     count,
        string? workload,
        bool    crossSession)
    {
        var base_ = inferenceType switch
        {
            "StartupCongestion"  => "Startup resource pressure",
            "ThermalStress"      => "Thermal elevation",
            "StoragePressure"    => "Storage space pressure",
            "CombinedPressure"   => "Combined multi-domain resource pressure",
            "SessionDegradation" => "Session-length performance degradation",
            "SustainedPressure"  => "Sustained resource pressure",
            _                    => inferenceType,
        };

        var countStr = count switch
        {
            2     => "has occurred twice",
            3     => "has occurred 3 times",
            >= 10 => $"recurs regularly ({count} occurrences)",
            _     => $"has occurred {count} times",
        };

        var scope = crossSession ? "across multiple sessions" : "in the current session window";

        return workload is not null
            ? $"{base_} {countStr}, often during {workload}."
            : $"{base_} {countStr} {scope}.";
    }

    // ── Serialization DTOs ────────────────────────────────────────────────────
    // Plain classes with mutable setters so System.Text.Json can deserialize
    // without requiring parameterized constructors or [JsonConstructor].

    private sealed class InferenceRecordDto
    {
        public Guid         InferenceId          { get; set; }
        public string       InferenceType        { get; set; } = "";
        public string       Headline             { get; set; } = "";
        public int          ConfidenceAtTime     { get; set; }
        public int          PriorityAtTime       { get; set; }
        public List<string> Domains              { get; set; } = [];
        public DateTime     RecordedAtUtc        { get; set; }
        public string?      WorkloadContext      { get; set; }
        public bool         WasActionable        { get; set; }
        public bool         WasSuppressed        { get; set; }
        public List<string> RecommendedActionIds { get; set; } = [];
    }

    private static InferenceRecordDto RecordToDto(HistoricalInferenceRecord r) => new()
    {
        InferenceId          = r.InferenceId,
        InferenceType        = r.InferenceType,
        Headline             = r.Headline,
        ConfidenceAtTime     = (int)r.ConfidenceAtTime,
        PriorityAtTime       = (int)r.PriorityAtTime,
        Domains              = [.. r.Domains],
        RecordedAtUtc        = r.RecordedAtUtc,
        WorkloadContext      = r.WorkloadContext,
        WasActionable        = r.WasActionable,
        WasSuppressed        = r.WasSuppressed,
        RecommendedActionIds = [.. r.RecommendedActionIds],
    };

    private static HistoricalInferenceRecord DtoToRecord(InferenceRecordDto d) => new()
    {
        InferenceId          = d.InferenceId,
        InferenceType        = d.InferenceType,
        Headline             = d.Headline,
        ConfidenceAtTime     = (ConfidenceLevel)d.ConfidenceAtTime,
        PriorityAtTime       = (InferencePriority)d.PriorityAtTime,
        Domains              = d.Domains.AsReadOnly(),
        RecordedAtUtc        = d.RecordedAtUtc,
        WorkloadContext      = d.WorkloadContext,
        WasActionable        = d.WasActionable,
        WasSuppressed        = d.WasSuppressed,
        RecommendedActionIds = d.RecommendedActionIds.AsReadOnly(),
    };
}
