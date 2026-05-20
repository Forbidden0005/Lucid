using System.Text;
using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Baseline;
using ExplainMyPC.Services.Intelligence;
using ExplainMyPC.Services.Session;

namespace ExplainMyPC.Services.Narrative;

/// <summary>
/// Deterministic, template-driven operational narrative engine.
///
/// Synthesizes the current set of active intelligence findings, telemetry
/// values, process attributions, forecast projections, and machine-specific
/// baseline context into coherent multi-paragraph prose.
///
/// No LLMs, cloud APIs, or random elements are used. The same input always
/// produces identical output. Confidence-aware phrasing and causal language
/// are selected from static phrase tables based on insight metadata.
///
/// Narrative structure (up to five paragraphs, each thematically distinct):
///   1. Status + metrics    — current health tier, key hardware readings
///   2. Active issues       — synthesis of warnings and recommendations
///   3. Process attribution — which apps are responsible and why
///   4. Forecast outlook    — trend projections and time-bounded predictions
///   5. Machine context     — baseline-relative interpretation for this device
///
/// Threading:
///   InsightsUpdated fires on the UI thread. BuildNarrative() executes
///   synchronously on that thread. All paragraph builders are pure functions
///   with no I/O or allocation beyond string building. Total budget: ≤200 µs.
/// </summary>
public sealed class OperationalNarrativeEngine : IOperationalNarrativeEngine
{
    private readonly ISystemInsightEngine    _insights;
    private readonly ITelemetryService       _telemetry;
    private readonly ISystemBaselineService? _baseline;
    private readonly ISessionContextService? _session;

    public OperationalNarrative? CurrentNarrative { get; private set; }
    public event EventHandler<OperationalNarrative>? NarrativeUpdated;

    public OperationalNarrativeEngine(
        ISystemInsightEngine    insights,
        ITelemetryService       telemetry,
        ISystemBaselineService? baseline = null,
        ISessionContextService? session  = null)
    {
        _insights  = insights;
        _telemetry = telemetry;
        _baseline  = baseline;
        _session   = session;
    }

    public void Start()
    {
        _insights.InsightsUpdated += OnInsightsUpdated;

        // Seed immediately if insights are already available.
        if (_insights.CurrentInsights.Count > 0)
            Regenerate(_insights.CurrentInsights);
    }

    public void Stop() => _insights.InsightsUpdated -= OnInsightsUpdated;

    private void OnInsightsUpdated(object? sender, IReadOnlyList<SystemInsight> insights)
        => Regenerate(insights);

    private void Regenerate(IReadOnlyList<SystemInsight> insights)
    {
        var narrative = BuildNarrative(
            insights,
            _telemetry.LastReading,
            _baseline?.CurrentBaseline,
            _session?.Current);
        CurrentNarrative = narrative;
        NarrativeUpdated?.Invoke(this, narrative);
    }

    // ── Core builder ──────────────────────────────────────────────────────────

    /// <summary>
    /// Main entry point for narrative construction. Classifies the insight set,
    /// determines health state, then assembles ordered paragraphs.
    /// </summary>
    private static OperationalNarrative BuildNarrative(
        IReadOnlyList<SystemInsight> all,
        TelemetrySnapshot?           snap,
        MachineBaseline?             baseline,
        SessionContext?              session = null)
    {
        // ── Classify insights by role ─────────────────────────────────────────
        bool isHealthy = all.Any(i => i.Id == "system.healthy");

        var warnings  = new List<SystemInsight>(4);
        var recs      = new List<SystemInsight>(6);
        var forecasts = new List<SystemInsight>(4);
        var syntheses = new List<SystemInsight>(2);
        var anomalies = new List<SystemInsight>(3);

        foreach (var insight in all)
        {
            if (insight.Id == "system.healthy") continue;

            if (insight.Id.StartsWith("synthesis.", StringComparison.Ordinal))
            {
                syntheses.Add(insight);
                continue;
            }
            if (insight.Id.StartsWith("forecast.", StringComparison.Ordinal))
            {
                forecasts.Add(insight);
                // Forecasts may also be warnings — classify by severity separately
                if (insight.Severity == InsightSeverity.Warning)
                    warnings.Add(insight);
                continue;
            }
            if (insight.Id.StartsWith("baseline.", StringComparison.Ordinal))
            {
                anomalies.Add(insight);
                if (insight.Severity == InsightSeverity.Warning)
                    warnings.Add(insight);
                else
                    recs.Add(insight);
                continue;
            }

            if (insight.Severity == InsightSeverity.Warning)
                warnings.Add(insight);
            else if (insight.Severity == InsightSeverity.Recommendation)
                recs.Add(insight);
        }

        var state = ComputeState(warnings.Count, isHealthy);

        // ── Build paragraphs ──────────────────────────────────────────────────
        var paragraphs = new List<string>(5);

        Add(paragraphs, BuildStatusParagraph(state, snap, warnings.Count, recs.Count, session));

        if (!isHealthy && all.Count > 0)
            Add(paragraphs, BuildIssuesParagraph(state, warnings, recs, syntheses, snap, session));

        Add(paragraphs, BuildAttributionParagraph(all));

        if (forecasts.Count > 0)
            Add(paragraphs, BuildForecastParagraph(forecasts, snap, session));

        if (anomalies.Count > 0 && baseline is not null)
            Add(paragraphs, BuildBaselineParagraph(anomalies, baseline, snap));

        int activeFindings = all.Count(i => i.Id != "system.healthy");

        return new OperationalNarrative(
            HealthState:    state,
            Headline:       BuildHeadline(state, warnings.Count, recs.Count, activeFindings),
            Paragraphs:     [.. paragraphs],
            GeneratedAt:    DateTimeOffset.Now,
            ActiveWarnings: warnings.Count,
            ActiveFindings: activeFindings);
    }

    // ── Health state ──────────────────────────────────────────────────────────

    private static SystemHealthState ComputeState(int warningCount, bool isHealthy)
    {
        if (isHealthy || warningCount == 0)
            return warningCount == 0 ? SystemHealthState.Healthy : SystemHealthState.Monitoring;
        return warningCount >= 3 ? SystemHealthState.Critical : SystemHealthState.Degraded;
    }

    private static string BuildHeadline(
        SystemHealthState state,
        int               warningCount,
        int               recCount,
        int               total) => state switch
    {
        SystemHealthState.Healthy    => "Your system is running well.",
        SystemHealthState.Monitoring => recCount == 1
            ? "One suggestion to review."
            : $"{recCount} suggestions worth reviewing.",
        SystemHealthState.Degraded   => warningCount == 1
            ? "One issue needs your attention."
            : $"{warningCount} issues need your attention.",
        _                            => $"{warningCount} concurrent issues are active.",
    };

    // ── Paragraph 1: Status + metrics ─────────────────────────────────────────

    private static string BuildStatusParagraph(
        SystemHealthState  state,
        TelemetrySnapshot? snap,
        int                warningCount,
        int                recCount,
        SessionContext?    session = null)
    {
        var sb = new StringBuilder(320);

        // Opening clause — describes health tier.
        sb.Append(state switch
        {
            SystemHealthState.Healthy    => "Your system is running well",
            SystemHealthState.Monitoring => recCount == 1
                ? "Your system is operating normally with one minor suggestion"
                : $"Your system is operating normally with {recCount} minor suggestions",
            SystemHealthState.Degraded   => warningCount == 1
                ? "Your system has one active issue that needs attention"
                : $"Your system has {warningCount} active issues that need attention",
            _                            => $"Your system is under significant pressure, with {warningCount} concurrent issues active",
        });

        // Append current metric readings when a snapshot is available.
        if (snap is not null)
        {
            sb.Append(". CPU is at ");
            sb.Append($"{snap.CpuPercent:0}%");

            if (snap.RamTotalGb > 0)
            {
                sb.Append($", RAM is at {snap.RamPercent:0}%");
                sb.Append($" ({snap.RamUsedGb:F1} GB of {snap.RamTotalGb:F0} GB used)");
            }

            if (snap.CpuTemperatureAvailable)
                sb.Append($", and the CPU is running at {snap.CpuTemperatureCelsius:0}°C");

            sb.Append('.');

            if (snap.DiskTotalGb > 0)
            {
                double freeGb = snap.DiskTotalGb - snap.DiskUsedGb;
                sb.Append($" The disk is {snap.DiskPercent:0}% full,");
                sb.Append($" with {freeGb:F0} GB of {snap.DiskTotalGb:F0} GB remaining.");
            }

            if (state == SystemHealthState.Healthy)
                sb.Append(" No issues are currently detected.");
        }
        else
        {
            sb.Append('.');
        }

        // ── Session phase context ─────────────────────────────────────────────
        if (session is not null)
        {
            // Post-wake: most contextually important — mention it first.
            if (session.TimeSinceWake is { } sinceWake && sinceWake.TotalMinutes < 30)
            {
                sb.Append($" The system resumed from sleep {SessionContext.FormatSpan(sinceWake)} ago.");
            }
            // Post-login startup settling window.
            else if (session.IsStartupSettling)
            {
                sb.Append($" The system is in its post-login startup window");
                sb.Append($" ({SessionContext.FormatSpan(session.SessionAge)} since launch).");
            }
            // Post-boot (but past startup settling).
            else if (session.IsPostBoot)
            {
                sb.Append($" The system has been running for {session.FormatUptime()}.");
            }

            // Idle state — append regardless of phase.
            if (session.IsIdle && session.IdleDuration is { } idleDur && idleDur.TotalMinutes >= 5)
            {
                sb.Append($" The system has been idle for {SessionContext.FormatSpan(idleDur)},");
                sb.Append(" with CPU activity below the active-use threshold.");
            }
        }

        return sb.ToString();
    }

    // ── Paragraph 2: Active issues synthesis ──────────────────────────────────

    private static string BuildIssuesParagraph(
        SystemHealthState    state,
        List<SystemInsight>  warnings,
        List<SystemInsight>  recs,
        List<SystemInsight>  syntheses,
        TelemetrySnapshot?   snap,
        SessionContext?      session = null)
    {
        if (warnings.Count == 0 && recs.Count == 0 && syntheses.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(512);

        // ── Warnings ──────────────────────────────────────────────────────────
        if (warnings.Count == 1)
        {
            var w = warnings[0];
            sb.Append("The primary concern is ");
            sb.Append(LowercaseFirst(w.Title));
            sb.Append(" — ");
            sb.Append(GetIssueOneLiner(w, snap));
            AppendOnsetContext(sb, w.Id, session);
            sb.Append('.');
        }
        else if (warnings.Count == 2)
        {
            sb.Append("There are two active issues. ");
            sb.Append(CapFirst(GetIssueOneLiner(warnings[0], snap)));
            AppendOnsetContext(sb, warnings[0].Id, session);
            sb.Append(". Additionally, ");
            sb.Append(GetIssueOneLiner(warnings[1], snap));
            AppendOnsetContext(sb, warnings[1].Id, session);
            sb.Append('.');

            if (AreBothHardwarePressure(warnings[0].Id, warnings[1].Id))
                sb.Append(" This combination of simultaneous hardware pressures can cause compounding slowdowns.");
        }
        else if (warnings.Count >= 3)
        {
            sb.Append("Multiple hardware metrics are elevated simultaneously. ");
            sb.Append(CapFirst(GetIssueOneLiner(warnings[0], snap)));
            AppendOnsetContext(sb, warnings[0].Id, session);
            sb.Append('.');

            for (int i = 1; i < Math.Min(warnings.Count, 3); i++)
            {
                sb.Append(' ');
                sb.Append(CapFirst(GetIssueOneLiner(warnings[i], snap)));
                AppendOnsetContext(sb, warnings[i].Id, session);
                sb.Append('.');
            }

            if (warnings.Count > 3)
                sb.Append($" There are {warnings.Count - 3} additional issue{Plural(warnings.Count - 3)} active.");
        }

        // ── Recommendations (only when no critical warnings dominate) ─────────
        if (warnings.Count == 0 && recs.Count > 0)
        {
            var primary = recs[0];
            sb.Append(CapFirst(GetIssueOneLiner(primary, snap)));
            AppendOnsetContext(sb, primary.Id, session);
            sb.Append('.');

            if (recs.Count == 2)
            {
                sb.Append(" There is also ");
                sb.Append(GetIssueOneLiner(recs[1], snap));
                sb.Append('.');
            }
            else if (recs.Count > 2)
            {
                sb.Append($" There are {recs.Count - 1} additional suggestion{Plural(recs.Count - 1)} that");
                sb.Append(" may improve system responsiveness.");
            }
        }
        else if (warnings.Count > 0 && recs.Count > 0)
        {
            sb.Append($" There {Is(recs.Count)} also {recs.Count} lower-priority");
            sb.Append($" suggestion{Plural(recs.Count)} that, while less urgent, are worth reviewing.");
        }

        // ── Synthesis (cross-insight correlation note) ─────────────────────────
        if (syntheses.Count > 0)
        {
            sb.Append(" Notably: ");
            sb.Append(LowercaseFirst(syntheses[0].Title));
            if (syntheses[0].Detail.IndexOf('.') is int dot and > 0)
                sb.Append(" — ").Append(LowercaseFirst(syntheses[0].Detail[..(dot + 1)]));
            else
                sb.Append('.');

            if (syntheses.Count > 1)
                sb.Append($" {syntheses.Count - 1} additional cross-insight correlation{Plural(syntheses.Count - 1)} detected.");
        }

        return sb.ToString().Trim();
    }

    // ── Paragraph 3: Process attribution ─────────────────────────────────────

    private static string BuildAttributionParagraph(IReadOnlyList<SystemInsight> all)
    {
        // Collect all attributions: displayName → list of (InsightId, Kind, UsageValue, UsageUnit)
        var byProcess = new Dictionary<string, List<(string InsightId, AttributionKind Kind, float Value, string Unit)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var insight in all)
        {
            foreach (var attr in insight.AttributedProcesses)
            {
                if (!byProcess.TryGetValue(attr.DisplayName, out var list))
                    byProcess[attr.DisplayName] = list = [];
                list.Add((insight.Id, attr.Kind, attr.UsageValue, attr.UsageUnit));
            }
        }

        if (byProcess.Count == 0)
            return string.Empty;

        // Separate processes that appear in multiple insights from single-insight ones.
        var crossInsight = byProcess.Where(kv =>
            kv.Value.Select(t => t.InsightId).Distinct().Count() >= 2).ToList();

        var singleInsight = byProcess.Where(kv =>
            kv.Value.Select(t => t.InsightId).Distinct().Count() == 1).ToList();

        var sb = new StringBuilder(400);

        if (crossInsight.Count > 0)
        {
            // Lead with the cross-insight process — it's the most interesting.
            var (name, appearances) = crossInsight[0];
            var kinds = appearances.Select(t => t.Kind).Distinct().ToList();
            string kindDesc = FormatKindList(kinds);

            sb.Append($"{name} appears to be the primary driver of current resource pressure, contributing to {kindDesc}.");

            // Quantify where possible.
            var cpuAttr = appearances.FirstOrDefault(t => t.Kind == AttributionKind.Cpu);
            var ramAttr = appearances.FirstOrDefault(t => t.Kind == AttributionKind.Ram);

            if (cpuAttr != default && ramAttr != default)
            {
                sb.Append($" It is currently using {cpuAttr.Value:F0}{cpuAttr.Unit} CPU");
                sb.Append($" and {ramAttr.Value:F1} {ramAttr.Unit} of RAM simultaneously.");
            }
            else if (cpuAttr != default)
            {
                sb.Append($" Its CPU utilization is {cpuAttr.Value:F0}{cpuAttr.Unit}.");
            }
            else if (ramAttr != default)
            {
                sb.Append($" Its memory footprint is {ramAttr.Value:F1} {ramAttr.Unit}.");
            }

            // Mention additional cross-insight processes.
            for (int i = 1; i < crossInsight.Count; i++)
            {
                var (name2, apps2) = crossInsight[i];
                sb.Append($" {name2} is also contributing across {FormatKindList(apps2.Select(t => t.Kind).Distinct().ToList())}.");
            }
        }

        // Add the most significant single-insight process if no cross-insight ones exist.
        if (crossInsight.Count == 0 && singleInsight.Count > 0)
        {
            // Pick the highest-value CPU or RAM attribution.
            var top = singleInsight
                .SelectMany(kv => kv.Value.Select(t => (Name: kv.Key, t.Kind, t.Value, t.Unit)))
                .Where(t => t.Kind is AttributionKind.Cpu or AttributionKind.Ram)
                .OrderByDescending(t => t.Kind == AttributionKind.Ram ? t.Value * 10 : t.Value)
                .FirstOrDefault();

            if (top != default)
            {
                if (top.Kind == AttributionKind.Ram)
                    sb.Append($"{top.Name} is the most memory-intensive application currently running, consuming {top.Value:F1} {top.Unit} of RAM.");
                else
                    sb.Append($"{top.Name} is the most CPU-intensive application currently running at {top.Value:F0}{top.Unit}.");

                // Mention up to one more.
                var second = singleInsight
                    .SelectMany(kv => kv.Value.Select(t => (Name: kv.Key, t.Kind, t.Value, t.Unit)))
                    .Where(t => t.Name != top.Name && t.Kind is AttributionKind.Cpu or AttributionKind.Ram)
                    .OrderByDescending(t => t.Kind == AttributionKind.Ram ? t.Value * 10 : t.Value)
                    .FirstOrDefault();

                if (second != default && second.Name != top.Name)
                {
                    if (second.Kind == AttributionKind.Ram)
                        sb.Append($" {second.Name} is also using {second.Value:F1} {second.Unit}.");
                    else
                        sb.Append($" {second.Name} is also drawing {second.Value:F0}{second.Unit} CPU.");
                }
            }
        }

        return sb.ToString().Trim();
    }

    // ── Paragraph 4: Forecast outlook ─────────────────────────────────────────

    private static string BuildForecastParagraph(
        List<SystemInsight> forecasts,
        TelemetrySnapshot?  snap,
        SessionContext?     session = null)
    {
        if (forecasts.Count == 0) return string.Empty;

        var sb = new StringBuilder(400);

        if (forecasts.Count == 1)
        {
            sb.Append("Looking at the current trend: ");
            sb.Append(GetForecastSentence(forecasts[0], snap));
        }
        else
        {
            sb.Append("Several trends warrant attention: ");
            for (int i = 0; i < forecasts.Count; i++)
            {
                if (i > 0) sb.Append(" ");
                sb.Append(GetForecastSentence(forecasts[i], snap));
            }
        }

        // Urgent action nudge.
        bool hasUrgent = forecasts.Any(f => f.Severity == InsightSeverity.Warning);
        if (hasUrgent)
            sb.Append(" Acting now — before these thresholds are reached — is significantly more effective than waiting for reactive alerts.");

        // ── Session-aware caveats ─────────────────────────────────────────────
        // Post-wake and startup-settling phases produce transient spikes that
        // can trigger forecasts. A brief caveat prevents unnecessary alarm.
        if (session?.IsStartupSettling == true)
        {
            sb.Append(" Note: the system is still in its post-login startup window —");
            sb.Append(" some of these trends may normalize as startup activity settles over the next few minutes.");
        }
        else if (session?.IsPostWake == true)
        {
            sb.Append(" Note: post-wake resource activity — from background sync,");
            sb.Append(" indexing, and memory re-population — can produce short-lived spikes");
            sb.Append(" that may not reflect the system's steady-state behaviour.");
        }

        return sb.ToString().Trim();
    }

    // ── Paragraph 5: Machine-specific baseline context ────────────────────────

    private static string BuildBaselineParagraph(
        List<SystemInsight>  anomalies,
        MachineBaseline      baseline,
        TelemetrySnapshot?   snap)
    {
        var sb = new StringBuilder(400);

        // Context framing: how long has the baseline been learning?
        string learningContext = FormatLearningContext(baseline);
        sb.Append($"For context about this specific machine ({learningContext}): ");

        bool first = true;
        foreach (var anomaly in anomalies)
        {
            if (!first) sb.Append(" ");
            first = false;

            switch (anomaly.Id)
            {
                case "baseline.cpu-above-norm":
                    if (snap is not null && baseline.IdleCpuReady)
                    {
                        double zscore = baseline.IdleCpuZScore(snap.CpuPercent);
                        sb.Append($"this machine typically idles at around {baseline.IdleCpuMean:0}% CPU");
                        sb.Append($" (± {baseline.IdleCpuStdDev:0}%),");
                        sb.Append($" making the current {snap.CpuPercent:0}% reading");
                        sb.Append($" {Sigma(zscore)} above the normal range for this system.");
                    }
                    else
                    {
                        sb.Append("CPU usage is elevated above this machine's learned idle baseline.");
                    }
                    break;

                case "baseline.ram-above-norm":
                    if (snap?.RamTotalGb > 0 && baseline.NormalRamReady)
                    {
                        sb.Append($"RAM usage at {snap.RamPercent:0}% ({snap.RamUsedGb:F1} GB) is higher than");
                        sb.Append($" this machine's typical {baseline.NormalRamMean:0}%,");
                        sb.Append($" suggesting an unusually heavy memory workload is active.");
                    }
                    else
                    {
                        sb.Append("RAM usage is above this machine's learned normal range.");
                    }
                    break;

                case "baseline.thermal-above-norm":
                    if (snap?.CpuTemperatureAvailable == true && baseline.NormalThermalReady)
                    {
                        sb.Append($"the current CPU temperature of {snap.CpuTemperatureCelsius:0}°C is warmer");
                        sb.Append($" than this machine's usual {baseline.NormalThermalMean:0}°C ± {baseline.NormalThermalStdDev:0}°C range,");
                        sb.Append(" pointing to a heavier-than-normal thermal load.");
                    }
                    else
                    {
                        sb.Append("CPU temperature is above this machine's learned thermal baseline.");
                    }
                    break;

                default:
                    // Generic fallback for future baseline rules.
                    sb.Append(LowercaseFirst(anomaly.Detail.Split('.')[0])).Append('.');
                    break;
            }
        }

        return sb.ToString().Trim();
    }

    // ── Per-insight one-liners (issues paragraph) ─────────────────────────────

    /// <summary>
    /// Returns a short (≤25 word) sentence describing an individual issue
    /// in the context of the current snapshot. Starts lower-case so callers
    /// can embed it mid-sentence or capitalise with <see cref="CapFirst"/>.
    /// </summary>
    private static string GetIssueOneLiner(SystemInsight insight, TelemetrySnapshot? snap) =>
        insight.Id switch
        {
            "cpu.sustained-high" => snap is not null
                ? $"CPU usage has been sustained at {snap.CpuPercent:0}% — well above comfortable operating levels"
                : "CPU usage has been elevated for an extended period",

            "ram.high-pressure" => snap?.RamTotalGb > 0
                ? $"RAM is at {snap.RamPercent:0}% ({snap.RamUsedGb:F1} GB), approaching the point where Windows will start using disk as overflow memory"
                : "RAM is approaching the pressure threshold where disk swapping begins",

            "gpu.unexpected-load" => snap?.GpuAvailable == true
                ? $"the GPU is showing {snap.GpuPercent:0}% utilization, which is higher than expected for the current workload"
                : "the GPU is showing unexpectedly high activity",

            "disk.low-space" => snap?.DiskTotalGb > 0
                ? $"disk storage is {snap.DiskPercent:0}% full with only {snap.DiskTotalGb - snap.DiskUsedGb:F0} GB remaining — Windows requires free space to function reliably"
                : "disk storage is critically low",

            "disk.sustained-io" => snap is not null
                ? $"disk I/O is running at {snap.DiskReadMbps + snap.DiskWriteMbps:F0} MB/s — sustained throughput at this level can slow all running applications"
                : "sustained disk I/O is consuming significant I/O bandwidth",

            "cpu.high-temperature" => snap?.CpuTemperatureAvailable == true
                ? $"the CPU is at {snap.CpuTemperatureCelsius:0}°C, which is approaching thermal limits and may be causing throttling"
                : "the CPU is running near or above thermal limits",

            "ram.rising-trend" =>
                "RAM usage has been climbing steadily — if this trend continues the system will reach the pressure threshold",

            "cpu.escalating" =>
                "CPU usage has been escalating over the past several minutes, not settling back to a normal level",

            "cpu.periodic-spikes" =>
                "CPU usage is spiking repeatedly in a recurring pattern, indicating a periodic background process",

            "cpu.thermal-trend" =>
                "CPU temperature has been trending upward and may reach warning levels if the workload continues",

            "disk.long-running-pressure" =>
                "disk I/O has been elevated for an extended period, which can slow application responsiveness",

            "startup.many-items"         =>
                "there are many applications configured to launch at Windows startup, which extends post-login settling time",

            "startup.high-impact-apps"   =>
                "several high-impact applications (such as browsers and launchers) are configured to start automatically",

            "startup.resource-pressure"  =>
                "startup applications appear to be driving the current resource pressure — a common pattern shortly after login",

            "forecast.ram-pressure" =>
                "RAM is trending toward the pressure threshold and may reach it soon if nothing changes",

            "forecast.disk-exhaustion" =>
                "disk usage is growing at a rate that will exhaust available space within the forecast horizon",

            "forecast.thermal-runaway" =>
                "CPU temperature is rising and could approach the thermal throttle point if the workload continues",

            "forecast.startup-degradation" =>
                "the current startup configuration will noticeably delay system responsiveness after the next login",

            _ when insight.Id.StartsWith("baseline.", StringComparison.Ordinal) =>
                "resource usage is higher than this machine's learned baseline",

            _ when insight.Id.StartsWith("synthesis.", StringComparison.Ordinal) =>
                LowercaseFirst(insight.Title),

            _ =>
                LowercaseFirst(insight.Title),
        };

    // ── Per-forecast sentences (forecast paragraph) ───────────────────────────

    private static string GetForecastSentence(SystemInsight forecast, TelemetrySnapshot? snap)
    {
        // Extract the time window from the insight title or detail.
        // The forecast rules embed the time in FormatMinuteHorizon / FormatHourHorizon
        // format inside the ActionHint, which is always more compact.
        string hint = forecast.ActionHint ?? string.Empty;

        return forecast.Id switch
        {
            "forecast.ram-pressure" => snap?.RamTotalGb > 0
                ? $"RAM is currently at {snap.RamPercent:0}% and trending upward — if this continues, the system may begin swapping to disk {ExtractTimeHint(hint)}."
                : $"RAM usage is trending toward the pressure threshold and may arrive {ExtractTimeHint(hint)}.",

            "forecast.disk-exhaustion" => snap?.DiskTotalGb > 0
                ? $"The disk is {snap.DiskPercent:0}% full and growing — at the current rate, available space could be exhausted {ExtractTimeHint(hint)}."
                : $"Disk usage is growing and may reach capacity {ExtractTimeHint(hint)}.",

            "forecast.thermal-runaway" => snap?.CpuTemperatureAvailable == true
                ? $"The CPU temperature ({snap.CpuTemperatureCelsius:0}°C) is rising — the thermal throttle threshold could be reached {ExtractTimeHint(hint)}, causing performance reduction."
                : $"CPU temperature is trending upward and may trigger thermal throttling {ExtractTimeHint(hint)}.",

            "forecast.startup-degradation" =>
                $"The current startup configuration is estimated to add several minutes of settling time to the next login — consider disabling unused startup entries.",

            _ => LowercaseFirst(forecast.Title) + ".",
        };
    }

    /// <summary>
    /// Extracts the time description embedded in an ActionHint string.
    /// Looks for "in {time}" patterns and returns them with "in" prefix.
    /// Falls back to "soon" when no pattern is found.
    /// </summary>
    private static string ExtractTimeHint(string hint)
    {
        if (string.IsNullOrEmpty(hint)) return "soon";
        int idx = hint.IndexOf(" in ", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            // Take from " in " to end of sentence or string.
            string tail = hint[(idx + 1)..];
            int end = tail.IndexOf('.');
            return end >= 0 ? tail[..end] : tail;
        }
        return "soon";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Adds a non-empty string to the list.</summary>
    private static void Add(List<string> list, string s)
    {
        if (!string.IsNullOrWhiteSpace(s))
            list.Add(s.Trim());
    }

    /// <summary>
    /// Appends a parenthetical temporal context phrase for an insight to the
    /// provided StringBuilder. The phrase captures:
    ///   • Age of the finding ("active for 8 minutes")
    ///   • Session-event correlation ("began shortly after waking from sleep")
    ///   • Post-login startup framing ("started during post-login startup")
    ///
    /// Produces no output for very recent findings (&lt; 60 s) or when
    /// session context is not available — avoids cluttering the text with
    /// trivially short durations or unhelpful "just now" context.
    /// </summary>
    private static void AppendOnsetContext(StringBuilder sb, string insightId, SessionContext? session)
    {
        if (session is null) return;

        var age = session.InsightAge(insightId);
        if (age is null || age.Value.TotalSeconds < 60) return;

        // Check for correlation with session events.
        bool onsetNearWake    = session.InsightOnsetNear(insightId, session.LastWakeTime, windowMinutes: 5.0);
        bool onsetDuringLogin = session.IsStartupSettling && age.Value.TotalMinutes < 5.0;

        if (onsetNearWake && session.LastWakeTime is not null)
        {
            // Most contextually valuable: the finding appeared around the wake event.
            sb.Append(" (this began shortly after the system woke from sleep)");
        }
        else if (onsetDuringLogin)
        {
            sb.Append(" (this began during the post-login startup period)");
        }
        else if (age.Value.TotalMinutes >= 5.0)
        {
            // Finding has been present long enough to note its persistence.
            sb.Append($" (active for {session.FormatInsightAge(insightId)})");
        }
    }

    private static string CapFirst(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string LowercaseFirst(string s) =>
        s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    private static string Plural(int n) => n == 1 ? "" : "s";

    private static string Is(int n) => n == 1 ? "is" : "are";

    /// <summary>
    /// True when two insight IDs both represent simultaneous hardware-pressure findings,
    /// which compounds into a feedback loop worth noting.
    /// </summary>
    private static bool AreBothHardwarePressure(string id1, string id2)
    {
        static bool IsHardware(string id) =>
            id is "cpu.sustained-high" or "ram.high-pressure" or "disk.sustained-io"
            or "cpu.high-temperature" or "gpu.unexpected-load"
            or "forecast.ram-pressure" or "forecast.thermal-runaway";
        return IsHardware(id1) && IsHardware(id2);
    }

    private static string FormatKindList(List<AttributionKind> kinds)
    {
        var labels = kinds.Select(k => k switch
        {
            AttributionKind.Cpu  => "elevated CPU usage",
            AttributionKind.Ram  => "elevated memory usage",
            AttributionKind.Gpu  => "elevated GPU activity",
            AttributionKind.Disk => "elevated disk activity",
            _                    => "elevated resource usage",
        }).ToList();

        return labels.Count switch
        {
            0 => "resource pressure",
            1 => labels[0],
            2 => $"{labels[0]} and {labels[1]}",
            _ => string.Join(", ", labels[..^1]) + ", and " + labels[^1],
        };
    }

    /// <summary>
    /// Formats a Z-score into a human-readable magnitude description.
    /// </summary>
    private static string Sigma(double z) => z switch
    {
        >= 4.0 => "significantly",
        >= 3.0 => "notably",
        >= 2.5 => "moderately",
        _      => "somewhat",
    };

    /// <summary>
    /// Converts the baseline's learning duration into a short context phrase.
    /// </summary>
    private static string FormatLearningContext(MachineBaseline baseline)
    {
        var age = DateTimeOffset.Now - baseline.LearnedSince;

        if (age.TotalHours < 2)
            return "based on recent observations";
        if (age.TotalDays < 1)
            return $"learned over {(int)age.TotalHours} hours of observation";
        if (age.TotalDays < 7)
            return $"learned over {(int)age.TotalDays} day{Plural((int)age.TotalDays)} of observation";
        return $"learned over {(int)(age.TotalDays / 7)} week{Plural((int)(age.TotalDays / 7))} of observation";
    }
}
