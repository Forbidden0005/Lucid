using System.Diagnostics;
using System.Text;
using Lucid.Services.Behavior;
using Lucid.Services.DesktopContext;
using Lucid.Services.Governance;
using Lucid.Services.Intelligence;
using Lucid.Services.Narrative;
using Lucid.Services.Session;
using Lucid.Services.Startup;
using Lucid.Services.Timeline;

namespace Lucid.Services.LlmChat;

/// <summary>
/// Builds the LLM system prompt by injecting live data from all platform services.
///
/// Called fresh on every message so the LLM always sees current system state.
/// The prompt includes: telemetry, insights, narrative, top processes, recent
/// timeline events, and session context.
///
/// Design: reads from AppServices directly (the established pattern in this app).
/// Pure function — no state, no side effects, always returns a string.
/// </summary>
public static class LlmSystemContextBuilder
{
    public static string Build()
    {
        var sb = new StringBuilder(2048);

        sb.AppendLine("You are Lucid, an intelligent Windows PC health assistant running locally on this machine.");
        sb.AppendLine("You have access to real-time system data shown below. Use it to give specific, accurate answers.");
        sb.AppendLine("Be conversational, clear, and helpful. Reference actual numbers from the data.");
        sb.AppendLine("If you don't have data for something, say so honestly rather than guessing.");
        sb.AppendLine("Never fabricate metrics. Do not add labels like 'Lucid:' before your responses.");
        sb.AppendLine("All analysis runs locally — nothing ever leaves this machine.");
        sb.AppendLine();

        // ── Session context — read early so it can annotate multiple sections ──
        SessionContext? ctx = null;
        try { ctx = AppServices.Session.Current; } catch { }

        // ── Live telemetry ─────────────────────────────────────────────────────
        var snap = AppServices.Telemetry.LastReading;
        if (snap is not null)
        {
            sb.AppendLine("=== CURRENT SYSTEM STATE ===");

            // CPU — include core count and frequency when available
            var cpuDetail = new System.Text.StringBuilder();
            if (snap.CpuCoreCount > 0)   cpuDetail.Append($" ({snap.CpuCoreCount} cores");
            if (snap.CpuFrequencyGhz > 0.5) cpuDetail.Append($"{(snap.CpuCoreCount > 0 ? " @ " : " (")}{snap.CpuFrequencyGhz:F1} GHz");
            if (cpuDetail.Length > 0)    cpuDetail.Append(')');
            sb.AppendLine($"CPU           : {snap.CpuPercent:F1}%{cpuDetail}");

            // RAM — percentage + absolute capacity
            if (snap.RamTotalGb > 0)
                sb.AppendLine($"RAM           : {snap.RamPercent:F1}% — {snap.RamUsedGb:F1} GB used of {snap.RamTotalGb:F1} GB");
            else
                sb.AppendLine($"RAM           : {snap.RamPercent:F1}%");

            // Disk I/O — percentage + throughput when available
            if (snap.DiskReadMbps > 0 || snap.DiskWriteMbps > 0)
                sb.AppendLine($"Disk I/O      : {snap.DiskPercent:F1}% — {snap.DiskReadMbps:F0} MB/s read, {snap.DiskWriteMbps:F0} MB/s write");
            else
                sb.AppendLine($"Disk I/O      : {snap.DiskPercent:F1}%");

            // Disk space — gives context for storage-related findings
            if (snap.DiskTotalGb > 0)
            {
                var freeGb = snap.DiskTotalGb - snap.DiskUsedGb;
                sb.AppendLine($"Disk space    : {snap.DiskUsedGb:F1} GB used of {snap.DiskTotalGb:F1} GB ({freeGb:F1} GB free)");
            }

            // GPU — usage + VRAM capacity
            if (snap.GpuAvailable)
            {
                sb.AppendLine($"GPU           : {snap.GpuPercent:F1}%");
                if (snap.GpuVramTotalGb > 0)
                    sb.AppendLine($"GPU VRAM      : {snap.GpuVramUsedGb:F1} GB / {snap.GpuVramTotalGb:F1} GB");
            }

            if (snap.CpuTemperatureAvailable)
                sb.AppendLine($"CPU temp      : {snap.CpuTemperatureCelsius:F0} °C");

            // Baseline comparison
            try
            {
                var baseline = AppServices.Baseline.CurrentBaseline;
                if (baseline is not null)
                {
                    sb.AppendLine($"Baseline CPU  : {baseline.IdleCpuMean:F1}% average (this machine's normal)");
                    sb.AppendLine($"Baseline RAM  : {baseline.NormalRamMean:F1}% average (this machine's normal)");
                }
            }
            catch { /* baseline may not be ready yet */ }

            // Session phase — helps the LLM interpret whether current readings are expected
            if (ctx is not null)
            {
                if (ctx.IsPostWake && ctx.TimeSinceWake is { } sinceWake)
                    sb.AppendLine($"Session phase : recovering from sleep ({(int)sinceWake.TotalMinutes}m ago — post-wake resource spikes are normal)");
                else if (ctx.IsStartupSettling)
                    sb.AppendLine($"Session phase : startup settling ({(int)ctx.SessionAge.TotalMinutes}m since launch — startup apps still competing for resources)");
                else if (ctx.IsPostBoot)
                    sb.AppendLine($"Session phase : post-boot period ({(int)ctx.SystemUptime.TotalMinutes}m since boot — OS still initializing)");
                else if (ctx.IsIdle && ctx.IdleDuration is { } idleDur)
                    sb.AppendLine($"Session phase : idle ({(int)idleDur.TotalMinutes}m with low CPU load)");
            }

            // Workload classification — helps contextualise why CPU/RAM are at their current level
            try
            {
                var behavCtx  = AppServices.BehavioralContext.GetCurrentContext();
                var workload  = behavCtx.CurrentWorkload;
                if (workload.PrimaryWorkload is not WorkloadType.Unknown)
                {
                    var confLabel = workload.Confidence switch
                    {
                        BehaviorConfidence.High   => "high confidence",
                        BehaviorConfidence.Medium => "medium confidence",
                        _                         => "low confidence",
                    };
                    sb.AppendLine($"Workload      : {WorkloadLabel(workload.PrimaryWorkload)} ({confLabel})");
                }
            }
            catch { }

            // Foreground app category — process name only (no window title — privacy-safe)
            try
            {
                var deskCtx = AppServices.DesktopContext.CurrentSnapshot;
                if (deskCtx?.ActiveWindow is { } win
                    && !string.IsNullOrEmpty(win.ProcessName)
                    && win.AppCategory != AppCategory.Unknown)
                {
                    sb.AppendLine($"Foreground app: {win.ProcessName} ({win.AppCategory})");
                }
            }
            catch { }

            // Startup apps — helps contextualise slow boots and persistent background load
            try
            {
                var entries    = AppServices.StartupManagement.GetAllEntries();
                var enabled    = entries.Where(e => e.IsEnabled).ToList();
                var highImpact = enabled.Where(e => e.Impact == StartupImpact.High).ToList();
                if (enabled.Count > 0)
                {
                    var label = highImpact.Count > 0
                        ? $"{enabled.Count} enabled, {highImpact.Count} high-impact " +
                          $"({string.Join(", ", highImpact.Take(4).Select(e => e.Name))})"
                        : $"{enabled.Count} enabled, none high-impact";
                    sb.AppendLine($"Startup apps  : {label}");
                }
            }
            catch { }

            // Runtime governance mode — if non-normal, the system is actively throttled
            try
            {
                var govMode = AppServices.Governance.CurrentMode;
                if (govMode != RuntimeMode.Normal)
                {
                    var govLabel = govMode switch
                    {
                        RuntimeMode.HighLoad          => "throttling active (high CPU load detected)",
                        RuntimeMode.LowPower          => "power-saving mode (battery)",
                        RuntimeMode.Gaming            => "gaming mode (reduced telemetry)",
                        RuntimeMode.ThermalProtection => "thermal protection (CPU throttled by heat)",
                        _                             => govMode.ToString(),
                    };
                    sb.AppendLine($"System mode   : {govLabel}");
                }
            }
            catch { }

            sb.AppendLine();
        }

        // ── 7-day machine health analytics ────────────────────────────────────
        // LastReport is a cached result populated whenever DashboardPage is visited.
        // Null on first launch before Dashboard navigation — skip the section gracefully.
        try
        {
            var report = AppServices.HealthTrajectory.LastReport;
            if (report is { HasSufficientData: true })
            {
                sb.AppendLine("=== MACHINE HEALTH (7-day analytics) ===");
                sb.AppendLine($"Health score   : {report.HealthScore}/100 — {report.HealthLabel} ({report.MomentumLabel})");

                if (report.CpuAvgLabel is not null)
                    sb.AppendLine($"CPU 7-day avg  : {report.CpuAvgLabel}  ({report.CpuTrendLabel})");

                if (report.RamAvgLabel is not null)
                    sb.AppendLine($"RAM 7-day avg  : {report.RamAvgLabel}  ({report.RamTrendLabel})");

                sb.AppendLine();
            }
        }
        catch { }

        // ── Long-term metric drift (7d vs 30d trends) ────────────────────────
        // Drift tells the LLM whether performance has been gradually worsening
        // over days/weeks, independent of today's snapshot. Only injected when present.
        try
        {
            var drifts = AppServices.DriftDetection.CurrentDrifts;
            if (drifts is { Count: > 0 })
            {
                sb.AppendLine($"=== LONG-TERM DRIFT ({drifts.Count} metric{(drifts.Count == 1 ? "" : "s")}) ===");
                foreach (var d in drifts.Take(4))
                {
                    sb.AppendLine($"[{d.Severity}] {d.MetricName}: {d.DirectionGlyph} {d.DriftLabel} — {d.AvgComparisonLabel}");
                    if (!string.IsNullOrWhiteSpace(d.Summary))
                        sb.AppendLine($"  {d.Summary}");
                }
                sb.AppendLine();
            }
        }
        catch { }

        // ── Active anomalies (z-score short-term deviations) ──────────────────
        // These are separate from the rule-based insights — they fire when a
        // metric deviates significantly from this machine's learned baseline,
        // even if no rule has been triggered yet. Only injected when present.
        try
        {
            var anomalies = AppServices.AnomalyDetection.CurrentAnomalies;
            if (anomalies is { Count: > 0 })
            {
                sb.AppendLine($"=== ACTIVE ANOMALIES ({anomalies.Count}) ===");
                foreach (var a in anomalies.Take(5))
                {
                    sb.AppendLine($"[{a.Severity}] {a.Metric}: {a.ActualValue:F1}% " +
                                  $"(expected ~{a.ExpectedMean:F1}% ± {a.ExpectedStdDev:F1}%) " +
                                  $"— {a.Description}");
                }
                sb.AppendLine();
            }
        }
        catch { }

        // ── Early warnings (synthesized from anomalies + findings) ───────────────
        // These are higher-level synthesized alerts, distinct from raw anomalies.
        // Only injected when warnings are active.
        try
        {
            var warnings = AppServices.EarlyWarning.CurrentWarnings;
            if (warnings is { Count: > 0 })
            {
                sb.AppendLine($"=== EARLY WARNINGS ({warnings.Count}) ===");
                foreach (var w in warnings.Take(4))
                {
                    sb.AppendLine($"[{w.Severity}] {w.Title} ({w.ConfidencePercent}% confidence)");
                    sb.AppendLine($"  {w.Explanation}");
                    if (w.ActionHint is not null)
                        sb.AppendLine($"  Suggested: {w.ActionHint}");
                }
                sb.AppendLine();
            }
        }
        catch { }

        // ── Active intelligence findings ───────────────────────────────────────
        try
        {
            var insights = AppServices.Intelligence.CurrentInsights;
            if (insights is { Count: > 0 })
            {
                sb.AppendLine($"=== ACTIVE FINDINGS ({insights.Count}) ===");
                foreach (var i in insights.Take(10))
                {
                    // Show how long this finding has been active when we have onset data
                    var ageStr = ctx?.InsightAge(i.Id) is { } age
                        ? $", active {(int)age.TotalMinutes}m"
                        : "";
                    sb.AppendLine($"[{i.Severity}] {i.Title} ({i.ConfidencePercent}% confidence{ageStr})");
                    sb.AppendLine($"  Detail: {i.Detail}");
                    if (i.ActionHint is not null)
                        sb.AppendLine($"  Suggested action: {i.ActionHint}");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("=== ACTIVE FINDINGS ===");
                sb.AppendLine("No active findings — system appears stable.");
                sb.AppendLine();
            }
        }
        catch { /* intelligence engine may not have run yet */ }

        // ── Narrative summary ──────────────────────────────────────────────────
        try
        {
            var narrative = AppServices.Narrative.CurrentNarrative;
            if (narrative is not null)
            {
                sb.AppendLine("=== SYSTEM NARRATIVE ===");
                sb.AppendLine(narrative.Headline);
                if (!string.IsNullOrWhiteSpace(narrative.StatusParagraph))
                    sb.AppendLine(narrative.StatusParagraph);
                sb.AppendLine();
            }
        }
        catch { }

        // ── Top processes by current CPU usage ────────────────────────────────
        // Use ProcessIntelligenceService snapshot so the LLM sees actual real-time
        // CPU% per process, not historical cumulative processor time.
        try
        {
            var procSnap = AppServices.ProcessIntelligence.LastSnapshot;
            if (procSnap is { TopByCpu.Count: > 0 })
            {
                sb.AppendLine($"=== TOP PROCESSES (current CPU%, {procSnap.SnapshotAt.LocalDateTime:h:mm:ss tt}) ===");
                foreach (var p in procSnap.TopByCpu.Take(8))
                {
                    var anomaly = p.HasAnomalies ? $" ⚠ {p.AnomalySummary}" : "";
                    sb.AppendLine($"  {p.DisplayName,-30} CPU: {p.CpuFormatted,6}  RAM: {p.RamFormatted,9}{anomaly}");
                }
                sb.AppendLine();
            }
        }
        catch { }

        // ── Recent timeline events (last hour) ─────────────────────────────────
        try
        {
            var cutoff = DateTimeOffset.Now.AddHours(-1);
            var events = AppServices.Timeline.Events
                .Where(e => e.OccurredAt >= cutoff)
                .OrderByDescending(e => e.OccurredAt)
                .Take(10)
                .ToList();

            if (events.Count > 0)
            {
                sb.AppendLine($"=== RECENT EVENTS (last hour, {events.Count} total) ===");
                foreach (var ev in events)
                {
                    var age = (DateTimeOffset.Now - ev.OccurredAt).TotalMinutes;
                    sb.AppendLine($"  [{age:F0}m ago] {ev.Title}");
                }
                sb.AppendLine();
            }
        }
        catch { }

        // ── Session context ────────────────────────────────────────────────────
        try
        {
            var appUptime     = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
            var windowsUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

            sb.AppendLine("=== SESSION CONTEXT ===");
            sb.AppendLine($"Lucid running  : {FormatDuration(appUptime)}");
            sb.AppendLine($"Windows uptime : {FormatDuration(windowsUptime)}");

            // Recent session events (last hour) — newest-first from service.
            // ctx was captured at the top of Build() and may be null if Session
            // threw on startup; fall back gracefully.
            var recentEvents = ctx?.RecentEvents ?? [];
            var recent = recentEvents
                .Where(ev => ev.Age.TotalHours < 1)
                .Take(5)
                .ToList();

            if (recent.Count > 0)
            {
                var summaries = recent.Select(ev =>
                {
                    var ageMin = (int)ev.Age.TotalMinutes;
                    var label  = ev.Kind switch
                    {
                        SessionEventKind.SystemBoot      => "system boot",
                        SessionEventKind.AppSessionStart => "Lucid started",
                        SessionEventKind.WakeFromSleep   => "wake from sleep",
                        SessionEventKind.IdleBegin       => "idle began",
                        SessionEventKind.IdleEnd         => "idle ended",
                        SessionEventKind.InsightOnset    => $"insight onset ({ev.AssociatedId})",
                        SessionEventKind.InsightCleared  => $"insight cleared ({ev.AssociatedId})",
                        _                                => ev.Kind.ToString(),
                    };
                    return $"{label} ({ageMin}m ago)";
                });
                sb.AppendLine($"Recent session events: {string.Join(", ", summaries)}");
            }

            sb.AppendLine();
        }
        catch { }

        sb.AppendLine("=== END OF SYSTEM CONTEXT ===");
        sb.AppendLine("Use the data above to answer the user's question. Be specific and reference real numbers.");

        return sb.ToString();
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalDays >= 1)    return $"{(int)t.TotalDays}d {t.Hours}h";
        if (t.TotalHours >= 1)   return $"{(int)t.TotalHours}h {t.Minutes}m";
        return $"{(int)t.TotalMinutes}m";
    }

    private static string WorkloadLabel(WorkloadType t) => t switch
    {
        WorkloadType.Gaming               => "gaming",
        WorkloadType.Development          => "software development",
        WorkloadType.MediaEditing         => "media editing",
        WorkloadType.Rendering            => "rendering",
        WorkloadType.Streaming            => "streaming",
        WorkloadType.BrowserResearch      => "browser / research",
        WorkloadType.Productivity         => "productivity",
        WorkloadType.BackgroundProcessing => "background processing",
        WorkloadType.StartupHeavy         => "startup-heavy",
        WorkloadType.ThermalIntensive     => "thermal-intensive",
        WorkloadType.OvernightProcessing  => "overnight processing",
        WorkloadType.Idle                 => "idle",
        _                                 => "general",
    };
}
