namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Synthesizes a "possible thermal throttling" insight when both CPU escalation
/// and a worsening thermal trend are simultaneously active.
///
/// Thermal throttle cascade (the causal chain this rule detects):
///   1. Workload rises  → cpu.escalating fires
///   2. Temperature rises → cpu.thermal-trend fires
///   3. Firmware/OS reduces CPU clock speed to protect hardware
///   4. System feels slow despite showing high CPU usage
///
/// Neither contributing rule alone explains this chain:
///   • cpu.escalating says "CPU load is rising" — but not why performance is poor.
///   • cpu.thermal-trend says "temperature is climbing" — but not that it's causing throttling.
/// Together they form a compound finding: the machine is likely throttling.
///
/// Confidence:
///   Average of the two contributing insight confidence scores, minus 8 points to
///   reflect that causal inference between the two signals is less certain than
///   either individual observation alone. Clamped to [55, 90].
///
/// Synthesis ID: synthesis.thermal-throttle-cascade
///   The ID is deterministic and does not depend on process names (unlike
///   SharedProcessSynthesisRule), so the insight appears as a single stable card.
///
/// Temperature guard:
///   This rule fires only when cpu.thermal-trend is active. That rule requires
///   CpuTemperatureAvailable = true and a meaningful upward slope. On machines
///   without temperature sensors this synthesis rule is automatically silent.
/// </summary>
public sealed class ThermalThrottlingSynthesisRule : ISynthesisRule
{
    private const string CpuEscalatingId  = "cpu.escalating";
    private const string ThermalTrendId   = "cpu.thermal-trend";
    private const string SynthesisId      = "synthesis.thermal-throttle-cascade";

    // Allocated once at class load — no per-tick heap pressure.
    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.thermal.improve-airflow",
            Title:                "Improve Airflow",
            Description:          "Ensure the PC's vents are clear and the chassis is not sitting on a soft surface that blocks airflow. Even small improvements can lower temperatures by several degrees and stop throttling.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.thermal.reduce-workload",
            Title:                "Reduce CPU Workload",
            Description:          "Closing resource-intensive applications will reduce heat generation. Once temperatures drop, the processor stops throttling and returns to its full clock speed.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),
    ];

    public string RuleId => "synthesis.thermal-throttle";

    public IReadOnlyList<SystemInsight> Evaluate(IReadOnlyList<SystemInsight> baseInsights)
    {
        // Both contributing rules must be simultaneously active.
        var escalating   = baseInsights.FirstOrDefault(i => i.Id == CpuEscalatingId);
        var thermalTrend = baseInsights.FirstOrDefault(i => i.Id == ThermalTrendId);

        if (escalating is null || thermalTrend is null)
            return [];

        int confidence = Math.Clamp(
            (escalating.ConfidencePercent + thermalTrend.ConfidencePercent) / 2 - 8,
            55, 90);

        return
        [
            new SystemInsight(
                Id:       SynthesisId,
                Severity: InsightSeverity.Warning,
                Title:    "Possible Thermal Throttling",
                Detail:   "CPU load is escalating while temperature is also rising. " +
                          "When both conditions occur together, the processor firmware " +
                          "automatically reduces clock speed to stay within safe thermal limits " +
                          "— a mechanism called thermal throttling. " +
                          "Your CPU may be running below its rated speed right now, which " +
                          "explains sluggishness even when the task manager shows high CPU usage. " +
                          "Addressing the heat source will restore full processor speed.",
                ActionHint:         "Improving airflow or reducing workload lets the processor return to full speed once temperatures drop.",
                DetectedAt:         DateTimeOffset.Now,
                ConfidencePercent:  confidence,
                RecommendedActions: s_actions),
        ];
    }
}
