namespace Lucid.Services.Simulation;

/// <summary>
/// Derives a human-readable decision summary from a completed simulation result.
///
/// Answers the user's actual question: "So what should I do?"
///
/// Outputs:
///   • HeadlineVerdict      — one-sentence outcome projection
///   • DecisionLabel        — "Recommended" / "Consider" / "Monitor" / "High Risk"
///   • UrgencyLabel         — "Act now" / "This week" / "Low priority" / "Monitor"
///   • ProjectedGainSummary — key metric deltas formatted for display
///   • DivergenceSummary    — how much the with/without branches diverge at the horizon
///   • ConfidenceTierLabel  — "High Confidence" / "Moderate Confidence" / "Preliminary Estimate"
///
/// All methods are deterministic and pure. No state, no I/O.
/// </summary>
public static class SimulationDecisionEngine
{
    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Derives the full decision summary from a completed simulation.
    /// Safe to call with any scenario — never throws.
    /// </summary>
    public static SimulationDecision Derive(SimulationScenario scenario)
    {
        try
        {
            return DeriveInternal(scenario);
        }
        catch
        {
            return FallbackDecision();
        }
    }

    // ── Core derivation ───────────────────────────────────────────────────────

    private static SimulationDecision DeriveInternal(SimulationScenario scenario)
    {
        var withBranch    = scenario.WithActionBranch;
        var withoutBranch = scenario.WithoutActionBranch;
        var risk          = scenario.Risk;

        string decisionLabel = DeriveDecisionLabel(scenario.ScenarioType, risk.Level);
        string decisionColor = DecisionColor(decisionLabel);
        string urgencyLabel  = DeriveUrgencyLabel(risk.Level, scenario.ScenarioType);
        string urgencyColor  = UrgencyColor(urgencyLabel);

        string headline      = DeriveHeadline(
            scenario.ScenarioType,
            withBranch?.ProjectedState,
            withoutBranch?.ProjectedState,
            scenario.Input.Horizon);

        string gainSummary   = DeriveGainSummary(scenario.ScenarioType, withBranch?.ProjectedState);
        string divergence    = ComputeDivergence(withBranch, withoutBranch, scenario.Input.Horizon);

        string confTierLabel = ConfidenceTier(scenario.Confidence.OverallPercent);
        string confTierColor = ConfidenceTierColor(scenario.Confidence.OverallPercent);

        return new SimulationDecision(
            HeadlineVerdict:      headline,
            DecisionLabel:        decisionLabel,
            DecisionColor:        decisionColor,
            UrgencyLabel:         urgencyLabel,
            UrgencyColor:         urgencyColor,
            ProjectedGainSummary: gainSummary,
            DivergenceSummary:    divergence,
            ConfidenceTierLabel:  confTierLabel,
            ConfidenceTierColor:  confTierColor);
    }

    // ── Decision label ────────────────────────────────────────────────────────

    private static string DeriveDecisionLabel(
        SimulationScenarioType type, ProjectedRiskLevel risk)
    {
        // Passive / continuation scenarios — user is simulating inaction, not an action
        bool isPassive = type is SimulationScenarioType.DeferMaintenance
                               or SimulationScenarioType.ContinueCurrentWorkload
                               or SimulationScenarioType.StorageGrowthContinuation
                               or SimulationScenarioType.WorkloadStress;

        if (isPassive)
        {
            return risk switch
            {
                ProjectedRiskLevel.High     => "High Risk",
                ProjectedRiskLevel.Elevated => "Elevated Risk",
                ProjectedRiskLevel.Moderate => "Monitor",
                _                           => "Stable",
            };
        }

        // Active intervention scenarios
        return risk switch
        {
            ProjectedRiskLevel.Low      => "Recommended",
            ProjectedRiskLevel.Moderate => "Consider",
            ProjectedRiskLevel.Elevated => "Use Caution",
            ProjectedRiskLevel.High     => "High Risk",
            _                           => "Monitor",
        };
    }

    private static string DecisionColor(string label) => label switch
    {
        "Recommended"    => "#34D399",
        "Consider"       => "#60A5FA",
        "Stable"         => "#34D399",
        "Monitor"        => "#6B7280",
        "Use Caution"    => "#F59E0B",
        "Elevated Risk"  => "#F59E0B",
        "High Risk"      => "#EF4444",
        _                => "#6B7280",
    };

    // ── Urgency ───────────────────────────────────────────────────────────────

    private static string DeriveUrgencyLabel(
        ProjectedRiskLevel risk, SimulationScenarioType type)
    {
        bool isPassive = type is SimulationScenarioType.DeferMaintenance
                               or SimulationScenarioType.ContinueCurrentWorkload
                               or SimulationScenarioType.StorageGrowthContinuation;

        if (isPassive)
        {
            return risk switch
            {
                ProjectedRiskLevel.High     => "Act now",
                ProjectedRiskLevel.Elevated => "This week",
                _                           => "Monitor",
            };
        }

        return risk switch
        {
            ProjectedRiskLevel.Low      => "Low priority",
            ProjectedRiskLevel.Moderate => "This week",
            ProjectedRiskLevel.Elevated => "Act soon",
            ProjectedRiskLevel.High     => "Act now",
            _                           => "Monitor",
        };
    }

    private static string UrgencyColor(string label) => label switch
    {
        "Act now"      => "#EF4444",
        "Act soon"     => "#F59E0B",
        "This week"    => "#F59E0B",
        "Low priority" => "#34D399",
        _              => "#6B7280",
    };

    // ── Headline verdict ──────────────────────────────────────────────────────

    private static string DeriveHeadline(
        SimulationScenarioType  type,
        SimulatedResourceState? with,
        SimulatedResourceState? without,
        SimulationHorizon       horizon)
    {
        string h = FormatHorizon(horizon);
        if (with is null) return "Insufficient data for a headline projection.";

        return type switch
        {
            SimulationScenarioType.RestartSystem =>
                with.RamDelta < -5
                    ? $"Restart projected to recover ~{Math.Abs(with.RamDelta):F0}% RAM and reset CPU to idle."
                    : "Restart will reset session state and allow Windows to reclaim cached memory.",

            SimulationScenarioType.DisableStartupApps =>
                with.StartupSettlingSeconds > 30
                    ? $"Disabling startup apps may cut post-boot settling by ~{with.StartupSettlingSeconds / 60.0:F1} min."
                    : with.RamDelta < -2
                        ? $"Startup changes may free ~{Math.Abs(with.RamDelta):F0}% background RAM over time."
                        : "Fewer startup items will reduce boot congestion and lower background resource use.",

            SimulationScenarioType.CleanupDisk =>
                with.DiskDelta < -3
                    ? $"Cleanup projected to reclaim ~{Math.Abs(with.DiskDelta):F0}% disk space."
                    : "Cleaning temp files will reduce disk pressure and improve I/O responsiveness.",

            SimulationScenarioType.RepairWindows =>
                "Windows repair will scan system files and restore integrity. May require a restart to complete.",

            SimulationScenarioType.TerminateProcess =>
                with.CpuDelta < -5
                    ? $"Terminating the process may relieve ~{Math.Abs(with.CpuDelta):F0}% CPU load immediately."
                    : "Process termination will immediately free the resources it currently holds.",

            SimulationScenarioType.DeferMaintenance =>
                without is not null && without.Ram > 80
                    ? $"Deferring may allow RAM pressure to build to ~{without.Ram:F0}% over {h}."
                    : $"Deferred maintenance may allow minor issues to accumulate over {h}.",

            SimulationScenarioType.ContinueCurrentWorkload =>
                without is not null && (without.Cpu > 80 || without.Ram > 80)
                    ? $"Continuing this workload risks sustained high resource load over {h}."
                    : $"Current workload appears sustainable over {h}.",

            SimulationScenarioType.StorageGrowthContinuation =>
                without is not null
                    ? $"At the current growth rate, disk may reach ~{without.Disk:F0}% within {h}."
                    : $"Storage growth trend will likely continue over {h} without intervention.",

            SimulationScenarioType.WorkloadStress =>
                with.ThermalC > 75
                    ? "Sustained workload may push thermal load into throttling territory."
                    : "Workload stress is within manageable bounds for the forecast period.",

            _ => "Simulation complete — review the outcome comparison for details.",
        };
    }

    // ── Projected gain summary ────────────────────────────────────────────────

    private static string DeriveGainSummary(
        SimulationScenarioType type, SimulatedResourceState? with)
    {
        if (with is null) return "";

        var gains = new List<string>(4);

        if (with.RamDelta   < -1) gains.Add($"RAM ↓{Math.Abs(with.RamDelta):F0}%");
        if (with.CpuDelta   < -2) gains.Add($"CPU ↓{Math.Abs(with.CpuDelta):F0}%");
        if (with.DiskDelta  < -1) gains.Add($"Disk ↓{Math.Abs(with.DiskDelta):F0}%");
        if (with.StartupSettlingSeconds > 30)
            gains.Add($"Boot ↓{with.StartupSettlingSeconds / 60.0:F1} min");

        // Passive continuation — worsening metrics are the "gain" to report
        if (with.RamDelta  > 3) gains.Add($"RAM may reach {with.Ram:F0}%");
        if (with.DiskDelta > 3) gains.Add($"Disk may reach {with.Disk:F0}%");

        if (gains.Count == 0)
        {
            return type is SimulationScenarioType.RepairWindows
                ? "Estimates system file integrity restoration — no direct metric change."
                : "Minimal projected change — current state appears stable.";
        }

        return "Primary benefit: " + string.Join(", ", gains.Take(3));
    }

    // ── Trajectory divergence ─────────────────────────────────────────────────

    private static string ComputeDivergence(
        SimulationBranch? with,
        SimulationBranch? without,
        SimulationHorizon horizon)
    {
        if (with is null || without is null) return "";
        if (with.Trajectory.Count == 0 || without.Trajectory.Count == 0) return "";

        var withFinal    = with.Trajectory[^1];
        var withoutFinal = without.Trajectory[^1];

        double ramDiff = withoutFinal.Ram - withFinal.Ram;
        double cpuDiff = withoutFinal.Cpu - withFinal.Cpu;

        // Surface the largest divergence
        bool   ramLeads = Math.Abs(ramDiff) >= Math.Abs(cpuDiff);
        double diff     = ramLeads ? ramDiff : cpuDiff;
        string metric   = ramLeads ? "RAM" : "CPU";
        string h        = FormatHorizon(horizon);

        if (Math.Abs(diff) < 1.5)
            return $"Branches converge — minimal difference projected over {h}.";

        return diff > 0
            ? $"Paths diverge by ~{diff:F0}% {metric} over {h} — taking action leads to a meaningfully better outcome."
            : $"Paths converge within ~{Math.Abs(diff):F0}% {metric} over {h}.";
    }

    // ── Confidence tier ───────────────────────────────────────────────────────

    private static string ConfidenceTier(int pct) => pct switch
    {
        >= 75 => "High Confidence",
        >= 55 => "Moderate Confidence",
        >= 40 => "Limited Data",
        _     => "Preliminary Estimate",
    };

    private static string ConfidenceTierColor(int pct) => pct switch
    {
        >= 75 => "#34D399",
        >= 55 => "#F59E0B",
        _     => "#EF4444",
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatHorizon(SimulationHorizon h) => h switch
    {
        SimulationHorizon.FifteenMinutes  => "15 minutes",
        SimulationHorizon.OneHour         => "1 hour",
        SimulationHorizon.FourHours       => "4 hours",
        SimulationHorizon.TwentyFourHours => "24 hours",
        SimulationHorizon.SevenDays       => "7 days",
        _                                  => "the forecast period",
    };

    private static SimulationDecision FallbackDecision() =>
        new(HeadlineVerdict:      "Simulation complete — review the outcome comparison.",
            DecisionLabel:        "Monitor",
            DecisionColor:        "#6B7280",
            UrgencyLabel:         "Monitor",
            UrgencyColor:         "#6B7280",
            ProjectedGainSummary: "",
            DivergenceSummary:    "",
            ConfidenceTierLabel:  "Preliminary Estimate",
            ConfidenceTierColor:  "#EF4444");
}

// ── Decision model ─────────────────────────────────────────────────────────────

/// <summary>
/// Derived decision summary produced by <see cref="SimulationDecisionEngine"/>.
/// Carries all the data needed to populate the decision card in the UI.
/// </summary>
public record SimulationDecision(
    string HeadlineVerdict,
    string DecisionLabel,
    string DecisionColor,
    string UrgencyLabel,
    string UrgencyColor,
    string ProjectedGainSummary,
    string DivergenceSummary,
    string ConfidenceTierLabel,
    string ConfidenceTierColor);
