using System.Diagnostics;
using System.Text;
using Lucid.Services.Intelligence;
using Lucid.Services.Narrative;
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

        // ── Live telemetry ─────────────────────────────────────────────────────
        var snap = AppServices.Telemetry.LastReading;
        if (snap is not null)
        {
            sb.AppendLine("=== CURRENT SYSTEM STATE ===");
            sb.AppendLine($"CPU usage     : {snap.CpuPercent:F1}%");
            sb.AppendLine($"RAM usage     : {snap.RamPercent:F1}%");
            sb.AppendLine($"Disk I/O      : {snap.DiskPercent:F1}%");

            if (snap.GpuAvailable)
                sb.AppendLine($"GPU usage     : {snap.GpuPercent:F1}%");

            if (snap.CpuTemperatureAvailable)
                sb.AppendLine($"CPU temp      : {snap.CpuTemperatureCelsius:F0} C");

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

            sb.AppendLine();
        }

        // ── Active intelligence findings ───────────────────────────────────────
        try
        {
            var insights = AppServices.Intelligence.CurrentInsights;
            if (insights is { Count: > 0 })
            {
                sb.AppendLine($"=== ACTIVE FINDINGS ({insights.Count}) ===");
                foreach (var i in insights.Take(10))
                {
                    sb.AppendLine($"[{i.Severity}] {i.Title} ({i.ConfidencePercent}% confidence)");
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

        // ── Top processes by CPU ───────────────────────────────────────────────
        try
        {
            var procs = Process.GetProcesses()
                .Where(p => { try { return p.TotalProcessorTime.TotalSeconds > 0; } catch { return false; } })
                .OrderByDescending(p => { try { return p.TotalProcessorTime.TotalMilliseconds; } catch { return 0; } })
                .Take(8)
                .ToList();

            if (procs.Count > 0)
            {
                sb.AppendLine("=== TOP PROCESSES (by CPU time) ===");
                foreach (var p in procs)
                {
                    try
                    {
                        var ramMb = p.WorkingSet64 / (1024 * 1024);
                        sb.AppendLine($"  {p.ProcessName,-30} RAM: {ramMb,6} MB");
                    }
                    catch { /* process may have exited */ }
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
            var session = AppServices.Session;
            var uptime  = DateTime.Now - Process.GetCurrentProcess().StartTime;
            sb.AppendLine("=== SESSION CONTEXT ===");
            sb.AppendLine($"Lucid has been running for: {FormatDuration(uptime)}");
            sb.AppendLine($"Windows uptime: {FormatDuration(TimeSpan.FromMilliseconds(Environment.TickCount64))}");
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
}
