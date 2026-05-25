using Lucid.Services.Analytics;
using Lucid.Services.Watchtower;

namespace Lucid.Services.Intelligence;

// ── Interface ───────────────────────────────────────────────────────────────────

/// <summary>
/// Classifies this machine's operational personality from historical, drift,
/// and anomaly signals. Stateless and pure — no Start()/Stop() required.
///
/// Classification is entirely deterministic: every label has an explicit rule.
/// </summary>
public interface ISystemPersonalityClassifier
{
    /// <summary>
    /// Classify this machine's operational personality from available signals.
    /// Returns a report with confidence score and supporting evidence.
    /// Returns an Unknown report when insufficient data is available.
    /// </summary>
    MachinePersonalityReport Classify(
        HistoricalSummary?             summary,
        WatchtowerSnapshot?            snapshot,
        IReadOnlyList<DetectedDrift>   drifts,
        IReadOnlyList<DetectedAnomaly> anomalies);
}

// ── Implementation ──────────────────────────────────────────────────────────────

/// <summary>
/// Deterministic, rule-based machine personality classifier.
///
/// Rules (evaluated in priority order):
/// 1. ThermallyConstrained — thermal drift elevated AND thermal anomalies present
/// 2. GraduallyDegrading   — 3+ significant drifts OR 30-day health worsening significantly
/// 3. ResourceHeavy        — 30-day CPU avg ≥ 70% AND RAM avg ≥ 75%
/// 4. Volatile             — 5+ anomalies detected in recent history OR high-frequency insights
/// 5. BurstHeavy           — mixed drift directions across metrics (some up, some stable/down)
/// 6. Stable               — 7-day health ≥ 75, no significant drifts, no anomalies
/// 7. Unknown              — fallback when data is insufficient
///
/// All classifications are conservative — the system prefers Unknown to a wrong label.
/// </summary>
public sealed class SystemPersonalityClassifier : ISystemPersonalityClassifier
{
    /// <inheritdoc/>
    public MachinePersonalityReport Classify(
        HistoricalSummary?             summary,
        WatchtowerSnapshot?            snapshot,
        IReadOnlyList<DetectedDrift>   drifts,
        IReadOnlyList<DetectedAnomaly> anomalies)
    {
        var now      = DateTime.UtcNow;
        var evidence = new List<string>(6);

        // Guard — insufficient historical data.
        if (summary is null || !summary.HasSufficientData())
        {
            return new MachinePersonalityReport(
                Personality:        MachinePersonality.Unknown,
                Title:              "Unknown",
                Description:        "Not enough data has been collected yet to characterize this machine.",
                SupportingEvidence: [],
                ConfidencePercent:  0,
                ComputedAt:         now);
        }

        var significantDrifts  = drifts.Where(d => d.Severity >= AnomalyIntelligenceSeverity.Elevated).ToList();
        var thermalDrift       = drifts.FirstOrDefault(d => d.MetricName == "Thermal");
        var hasThermalAnomaly  = anomalies.Any(a => a.Metric == "Thermal" &&
                                                     a.Severity >= AnomalyIntelligenceSeverity.Elevated);
        var cpuAvg30d          = summary.CpuTrend.ThirtyDayAvg;
        var ramAvg30d          = summary.RamTrend.ThirtyDayAvg;
        var health7d           = summary.SevenDayHealth.Score;
        var health30d          = summary.ThirtyDayHealth.Score;
        var alerts             = snapshot?.Alerts ?? [];

        // ── Rule 1: ThermallyConstrained ─────────────────────────────────────
        bool isThermallyConstrained =
            hasThermalAnomaly ||
            (thermalDrift is not null && thermalDrift.Direction == AnomalyDriftDirection.Elevated);

        if (isThermallyConstrained)
        {
            if (hasThermalAnomaly)
                evidence.Add("Temperature is currently elevated above this machine's typical range.");
            if (thermalDrift?.Direction == AnomalyDriftDirection.Elevated)
                evidence.Add($"Temperature has been drifting upward: {thermalDrift.AvgComparisonLabel}.");

            return MakeReport(
                MachinePersonality.ThermallyConstrained,
                "Thermally Constrained",
                "This machine is running warmer than its learned baseline suggests. " +
                "Ensure ventilation is clear and consider checking cooling performance.",
                evidence, confidence: 78, now);
        }

        // ── Rule 2: GraduallyDegrading ───────────────────────────────────────
        bool isGraduallyDegrading =
            significantDrifts.Count >= 2 ||
            (health30d > 0 && health7d < health30d - 12);

        if (isGraduallyDegrading)
        {
            if (significantDrifts.Count >= 2)
                evidence.Add($"{significantDrifts.Count} metrics show upward drift over recent weeks.");
            if (health7d < health30d - 12)
                evidence.Add($"7-day health ({health7d:0}) is noticeably lower than 30-day average ({health30d:0}).");

            foreach (var d in significantDrifts.Take(3))
                evidence.Add(d.Summary);

            return MakeReport(
                MachinePersonality.GraduallyDegrading,
                "Gradually Degrading",
                "Multiple metrics show a slow upward trend over recent weeks. " +
                "This pattern may indicate accumulating background work, memory leaks, or aging hardware.",
                evidence, confidence: 75, now);
        }

        // ── Rule 3: ResourceHeavy ────────────────────────────────────────────
        bool isResourceHeavy =
            (cpuAvg30d.HasValue && cpuAvg30d.Value >= 65) &&
            (ramAvg30d.HasValue && ramAvg30d.Value >= 70);

        if (isResourceHeavy)
        {
            if (cpuAvg30d.HasValue) evidence.Add($"30-day average CPU: {cpuAvg30d.Value:0}%.");
            if (ramAvg30d.HasValue) evidence.Add($"30-day average RAM: {ramAvg30d.Value:0}%.");
            evidence.Add("This machine operates close to its resource capacity during typical use.");

            return MakeReport(
                MachinePersonality.ResourceHeavy,
                "Resource Heavy",
                "This machine consistently operates at high CPU and RAM utilization. " +
                "This may reflect demanding software, insufficient resources, or background activity.",
                evidence, confidence: 82, now);
        }

        // ── Rule 4: Volatile ─────────────────────────────────────────────────
        bool isVolatile =
            anomalies.Count >= 2 ||
            alerts.Count(a => a.SignalType == DegradationSignalType.HighFrequencyAnomaly) >= 1;

        if (isVolatile)
        {
            evidence.Add($"{anomalies.Count} active behavioral anomalies detected.");
            if (alerts.Any(a => a.SignalType == DegradationSignalType.HighFrequencyAnomaly))
                evidence.Add("Recurring insight patterns detected — some metrics spike frequently.");

            return MakeReport(
                MachinePersonality.Volatile,
                "Volatile",
                "This machine shows frequent short-lived deviations from its baseline. " +
                "Workload spikes are common — this may be normal for compute-heavy use.",
                evidence, confidence: 72, now);
        }

        // ── Rule 5: BurstHeavy ───────────────────────────────────────────────
        bool isBurstHeavy =
            drifts.Any(d => d.Direction == AnomalyDriftDirection.Elevated) &&
            drifts.Any(d => d.Direction is AnomalyDriftDirection.Stable or AnomalyDriftDirection.Improving);

        if (isBurstHeavy)
        {
            evidence.Add("Some metrics are elevated while others remain stable — suggesting burst workload patterns.");
            foreach (var d in drifts.Where(d => d.Direction != AnomalyDriftDirection.Stable).Take(2))
                evidence.Add(d.Summary);

            return MakeReport(
                MachinePersonality.BurstHeavy,
                "Burst Heavy",
                "This machine shows bursty activity — quiet periods interspersed with heavy loads. " +
                "Resource usage is uneven across metrics.",
                evidence, confidence: 68, now);
        }

        // ── Rule 6: Stable ───────────────────────────────────────────────────
        bool isStable =
            health7d >= 72 &&
            !significantDrifts.Any() &&
            anomalies.Count == 0;

        if (isStable)
        {
            evidence.Add($"7-day health score: {health7d:0} — within normal range.");
            evidence.Add("No significant drift detected across CPU, RAM, or disk.");
            if (anomalies.Count == 0)
                evidence.Add("No active behavioral anomalies.");

            return MakeReport(
                MachinePersonality.Stable,
                "Stable",
                "This machine is operating consistently within its learned norms. " +
                "Performance is predictable and no concerning patterns have emerged.",
                evidence, confidence: 85, now);
        }

        // ── Fallback: Unknown ─────────────────────────────────────────────────
        return new MachinePersonalityReport(
            Personality:        MachinePersonality.Unknown,
            Title:              "Characterizing…",
            Description:        "The system is still gathering enough data to characterize this machine's personality.",
            SupportingEvidence: [],
            ConfidencePercent:  0,
            ComputedAt:         now);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MachinePersonalityReport MakeReport(
        MachinePersonality personality,
        string             title,
        string             description,
        List<string>       evidence,
        int                confidence,
        DateTime           computedAt)
        => new(personality, title, description, [.. evidence], confidence, computedAt);
}

// ── HistoricalSummary extension (local to this file) ─────────────────────────

file static class HistoricalSummaryExtensions
{
    /// <summary>True when the summary contains enough data to make reliable inferences.</summary>
    internal static bool HasSufficientData(this HistoricalSummary s)
        => s.TotalTelemetryRows >= 500 && s.CpuTrend.SevenDayAvg.HasValue;
}
