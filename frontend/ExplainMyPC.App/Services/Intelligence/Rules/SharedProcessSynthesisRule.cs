namespace ExplainMyPC.Services.Intelligence.Rules;

/// <summary>
/// Synthesizes a higher-level finding when the same process is identified as a
/// contributor to two or more distinct active base insights.
///
/// Algorithm:
///   1. Build a lookup: DisplayName → list of (AttributionKind, insight)
///      from all attributions across all base insights.
///   2. Filter to process names that appear in 2 or more DISTINCT insights
///      (keyed by insight ID — not just different kinds on the same insight).
///   3. For each qualifying process, produce one synthesized SystemInsight
///      whose ID, title, and detail are derived deterministically from the
///      resource kinds involved.
///
/// Synthesized insight IDs:
///   "synthesis.{sanitizedProcessName}.{sortedKinds}"
///   e.g. "synthesis.google-chrome.cpu-ram"
///        "synthesis.discord.gpu-ram"
///
/// Confidence:
///   Average of the contributing attribution confidence scores minus 5 points,
///   clamped to [55, 95]. Mixing a direct measurement (90%) and a GPU heuristic
///   (55%) yields ≈ 72%, reflecting that the compound claim is less certain
///   than either contributor alone.
///
/// Recommended actions:
///   Union of all RecommendedActions from the contributing insights, deduplicated
///   by SystemAction.Id. This consolidates the full remediation toolkit onto the
///   single correlated card.
/// </summary>
public sealed class SharedProcessSynthesisRule : ISynthesisRule
{
    public string RuleId => "synthesis.shared-process";

    public IReadOnlyList<SystemInsight> Evaluate(IReadOnlyList<SystemInsight> baseInsights)
    {
        // ── Step 1: Build process → attribution entries map ───────────────────

        // Key: DisplayName (human-readable, stable across ticks for the same exe).
        // Value: list of all (kind, insight, confidence) entries across all insights.
        var byProcess =
            new Dictionary<string, List<(AttributionKind Kind, SystemInsight Insight, int Confidence)>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var insight in baseInsights)
        {
            foreach (var attr in insight.AttributedProcesses)
            {
                if (!byProcess.TryGetValue(attr.DisplayName, out var list))
                {
                    list = [];
                    byProcess[attr.DisplayName] = list;
                }

                // Avoid double-counting if the same insight attributes the same
                // process with the same kind (shouldn't happen but defensive).
                bool duplicate = list.Any(e =>
                    e.Kind == attr.Kind &&
                    string.Equals(e.Insight.Id, insight.Id, StringComparison.Ordinal));

                if (!duplicate)
                    list.Add((attr.Kind, insight, attr.ConfidencePercent));
            }
        }

        // ── Step 2: Filter to processes spanning 2+ distinct insights ─────────

        var results = new List<SystemInsight>();

        foreach (var (processName, entries) in byProcess)
        {
            // Count how many distinct base insights mention this process.
            int distinctInsightCount = entries
                .Select(e => e.Insight.Id)
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (distinctInsightCount < 2)
                continue;   // single-insight attribution — nothing to correlate

            // ── Step 3: Build synthesized insight ─────────────────────────────

            // Unique kinds this process is attributed to, sorted for a stable ID.
            var kinds = entries
                .Select(e => e.Kind)
                .Distinct()
                .OrderBy(k => k)
                .ToList();

            // Deterministic ID — same input always produces the same ID.
            var sanitizedName = SanitizeName(processName);
            var kindSlug      = string.Join("-", kinds.Select(k => k.ToString().ToLowerInvariant()));
            var synthesisId   = $"synthesis.{sanitizedName}.{kindSlug}";

            // Confidence: average of contributing attribution confidences minus 5,
            // clamped to [55, 95]. Reflects that the compound claim is slightly
            // more specific (and thus more falsifiable) than either contributor.
            int avgConfidence = (int)entries.Average(e => e.Confidence);
            int confidence    = Math.Clamp(avgConfidence - 5, 55, 95);

            // Severity: promote to the max severity among contributors.
            var maxSeverity = entries
                .Select(e => e.Insight.Severity)
                .Max();

            // Actions: deduplicated union from all contributing insights.
            var allActions = entries
                .Select(e => e.Insight)
                .DistinctBy(i => i.Id)
                .SelectMany(i => i.RecommendedActions)
                .DistinctBy(a => a.Id)
                .ToList();

            // Attribution for the synthesis card itself.
            var attributions = new List<ProcessAttribution>
            {
                new(DisplayName:       processName,
                    Kind:              kinds[0],   // primary resource kind
                    UsageValue:        0,
                    UsageUnit:         "%",
                    ConfidencePercent: confidence),
            };

            results.Add(new SystemInsight(
                Id:                  synthesisId,
                Severity:            maxSeverity,
                Title:               BuildTitle(processName, kinds),
                Detail:              BuildDetail(processName, kinds, confidence),
                ActionHint:          BuildActionHint(processName, kinds),
                DetectedAt:          DateTimeOffset.Now,
                ConfidencePercent:   confidence,
                RecommendedActions:  allActions,
                AttributedProcesses: attributions));
        }

        // Limit output in case an unusual state produces many correlated processes.
        // The highest-confidence ones are the most actionable.
        if (results.Count > 3)
        {
            results.Sort(static (a, b) => b.ConfidencePercent.CompareTo(a.ConfidencePercent));
            results = results.Take(3).ToList();
        }

        return results;
    }

    // ── Message construction ──────────────────────────────────────────────────

    private static string BuildTitle(string name, List<AttributionKind> kinds)
    {
        bool hasCpu  = kinds.Contains(AttributionKind.Cpu);
        bool hasRam  = kinds.Contains(AttributionKind.Ram);
        bool hasGpu  = kinds.Contains(AttributionKind.Gpu);
        bool hasDisk = kinds.Contains(AttributionKind.Disk);

        if (hasCpu && hasRam && hasGpu)
            return $"{name}: CPU, Memory, and GPU Impact";

        if (hasCpu && hasRam)
            return $"{name} Is Straining CPU and Memory";

        if (hasCpu && hasGpu)
            return $"{name}: CPU and GPU Activity";

        if (hasRam && hasGpu)
            return $"{name}: Memory and GPU Activity";

        if (hasCpu && hasDisk)
            return $"{name}: CPU and Disk Activity";

        if (hasRam && hasDisk)
            return $"{name}: Memory and Disk Activity";

        // Generic fallback for unexpected combinations.
        var kindLabels = kinds.Select(k => k switch
        {
            AttributionKind.Cpu  => "CPU",
            AttributionKind.Ram  => "memory",
            AttributionKind.Gpu  => "GPU",
            AttributionKind.Disk => "disk",
            _                    => k.ToString().ToLowerInvariant(),
        });
        return $"{name}: {string.Join(" and ", kindLabels)} impact";
    }

    private static string BuildDetail(
        string name,
        List<AttributionKind> kinds,
        int confidence)
    {
        bool hasCpu = kinds.Contains(AttributionKind.Cpu);
        bool hasRam = kinds.Contains(AttributionKind.Ram);
        bool hasGpu = kinds.Contains(AttributionKind.Gpu);

        // Use hedged language for lower-confidence findings (GPU heuristic involved).
        bool hedged = confidence < 80;
        string verb = hedged ? "appears to be" : "is";

        if (hasCpu && hasRam && hasGpu)
            return $"{name} {verb} actively using CPU, RAM, and GPU simultaneously — " +
                   $"it is the most likely root cause of your PC's overall slowness. " +
                   $"Closing or restarting {name} would likely provide the most " +
                   $"significant performance improvement.";

        if (hasCpu && hasRam)
            return $"{name} {verb} the primary source of both CPU and RAM pressure right now. " +
                   $"When a single application consumes both resources simultaneously, " +
                   $"the combined effect on responsiveness is greater than either alone. " +
                   $"Restarting {name} or reducing its workload would likely provide " +
                   $"the most significant overall improvement.";

        if (hasCpu && hasGpu)
            return $"{name} {verb} simultaneously loading your processor and graphics card. " +
                   $"This is common during video rendering or screen recording, but may be " +
                   $"unexpected during routine use. Checking {name}'s hardware acceleration " +
                   $"settings may reduce overall system load.";

        if (hasRam && hasGpu)
            return $"{name} {verb} holding substantial memory and may be driving background " +
                   $"GPU activity. Browser-based apps with hardware acceleration are a " +
                   $"common cause of this pattern — disabling GPU acceleration in " +
                   $"{name}'s settings often resolves it.";

        // Generic fallback.
        var kindNames = kinds.Select(k => k switch
        {
            AttributionKind.Cpu  => "CPU",
            AttributionKind.Ram  => "RAM",
            AttributionKind.Gpu  => "GPU",
            AttributionKind.Disk => "disk",
            _                    => k.ToString(),
        }).ToList();
        return $"{name} {verb} contributing to {string.Join(", ", kindNames)} pressure " +
               $"simultaneously. Investigating or restarting this application may " +
               $"provide the most significant performance improvement.";
    }

    private static string? BuildActionHint(string name, List<AttributionKind> kinds)
    {
        bool hasCpu = kinds.Contains(AttributionKind.Cpu);
        bool hasRam = kinds.Contains(AttributionKind.Ram);
        bool hasGpu = kinds.Contains(AttributionKind.Gpu);

        if (hasCpu && hasRam)
            return $"Restarting {name} is the single highest-impact action available.";

        if (hasGpu)
            return $"Check {name}'s settings for a hardware acceleration toggle.";

        if (hasCpu)
            return $"Use Task Manager to confirm {name}'s CPU usage before restarting.";

        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces a URL-safe ID segment from a display name.
    /// "Google Chrome" → "google-chrome", "obs64" → "obs64"
    /// </summary>
    private static string SanitizeName(string name) =>
        name.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('.', '-')
            .Replace('(', '-')
            .Replace(')', '-')
            .Trim('-');
}
