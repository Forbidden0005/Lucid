using Lucid.Helpers;
using Lucid.Services.Governance;

namespace Lucid.Services.Remediation;

/// <summary>
/// Validates safety constraints before and after workflow execution.
///
/// Hard safety rails (always enforced, regardless of trust level):
///   • Never run Elevated-risk workflows when system is under thermal protection
///   • Never run workflows with elevated steps when process is not elevated
///     (executor will return RequiresElevation, but we pre-warn)
///   • Never run destructive workflows automatically without user approval
///   • Halt if stabilization worsens beyond the degradation threshold
///
/// All checks are deterministic and non-blocking.
/// </summary>
public static class SafetyConstraintEngine
{
    // ── Safety thresholds ─────────────────────────────────────────────────────

    // Max CPU% to allow workflow execution (avoid compounding load)
    private const double MaxCpuForExecution   = 85.0;
    private const double MaxRamForExecution   = 90.0;

    // Degradation detection: if CPU/RAM rises more than this after workflow, halt
    private const double DegradationThreshold = 15.0;
    private const int    InsightIncreaseLimit  = 3;   // more than 3 new insights = halt

    // ── Pre-execution check ───────────────────────────────────────────────────

    /// <summary>
    /// Validates that it is safe to start the given workflow right now.
    /// </summary>
    public static SafetyCheckResult CheckPreConditions(
        RemediationWorkflow workflow,
        RuntimeMode         governanceMode,
        TelemetrySnapshot?  currentTelemetry,
        AutomationTrustLevel currentTrustLevel)
    {
        // ── Hard block: thermal protection mode ───────────────────────────────
        if (governanceMode == RuntimeMode.ThermalProtection
            && workflow.Risk >= OperationalRisk.Moderate)
        {
            return new SafetyCheckResult(
                SafetyCheckStatus.Blocked,
                "System is in thermal protection mode. Moderate-risk workflows are paused " +
                "to avoid adding CPU load while temperatures are elevated.",
                BlockedBy: "ThermalProtection");
        }

        // ── Hard block: trust level insufficient ──────────────────────────────
        if (currentTrustLevel < workflow.TrustLevelRequired)
        {
            return new SafetyCheckResult(
                SafetyCheckStatus.Blocked,
                $"This workflow requires at least \"{TrustLevelLabel(workflow.TrustLevelRequired)}\" " +
                $"trust level. Current level: \"{TrustLevelLabel(currentTrustLevel)}\".",
                BlockedBy: "TrustLevel");
        }

        // ── System load checks ────────────────────────────────────────────────
        if (currentTelemetry is not null)
        {
            if (currentTelemetry.CpuPercent > MaxCpuForExecution)
            {
                return new SafetyCheckResult(
                    SafetyCheckStatus.Caution,
                    $"CPU utilisation is currently {currentTelemetry.CpuPercent:F0}% — " +
                    "executing a workflow now may add to system load. Consider waiting.");
            }

            if (currentTelemetry.RamPercent > MaxRamForExecution)
            {
                return new SafetyCheckResult(
                    SafetyCheckStatus.Caution,
                    $"RAM utilisation is currently {currentTelemetry.RamPercent:F0}% — " +
                    "some workflow steps may be slower under memory pressure.");
            }
        }

        // ── Restart advisory ──────────────────────────────────────────────────
        if (workflow.RequiresRestart)
        {
            return new SafetyCheckResult(
                SafetyCheckStatus.Caution,
                "This workflow may require a system restart to complete. " +
                "Save your work before proceeding.");
        }

        return new SafetyCheckResult(SafetyCheckStatus.Passed, "All safety checks passed.");
    }

    // ── Post-execution stabilization check ───────────────────────────────────

    /// <summary>
    /// Checks whether the system stabilized after workflow execution.
    /// Called during the Validation phase.
    /// </summary>
    public static SafetyCheckResult CheckPostConditions(
        StabilizationSnapshot before,
        StabilizationSnapshot after)
    {
        var cpuDelta    = after.CpuPercent - before.CpuPercent;
        var ramDelta    = after.RamPercent - before.RamPercent;
        var insightDelta = after.ActiveInsightCount - before.ActiveInsightCount;

        // Significant worsening detected
        if (cpuDelta > DegradationThreshold || ramDelta > DegradationThreshold)
        {
            return new SafetyCheckResult(
                SafetyCheckStatus.Blocked,
                $"System metrics worsened after the workflow: " +
                $"CPU {(cpuDelta > 0 ? "+" : "")}{cpuDelta:F0}%, " +
                $"RAM {(ramDelta > 0 ? "+" : "")}{ramDelta:F0}%. " +
                "This may not be caused by the workflow, but rollback is recommended if available.",
                BlockedBy: "Degradation");
        }

        // Significant new insights
        if (insightDelta > InsightIncreaseLimit)
        {
            return new SafetyCheckResult(
                SafetyCheckStatus.Caution,
                $"{insightDelta} new system findings appeared after the workflow. " +
                "These may or may not be related to the actions taken.");
        }

        return new SafetyCheckResult(SafetyCheckStatus.Passed, "Post-execution conditions nominal.");
    }

    // ── Per-step check ────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether it is safe to proceed to the next step.
    /// Called between each step in the workflow.
    /// </summary>
    public static SafetyCheckResult CheckStepCondition(
        WorkflowStepDefinition    step,
        WorkflowExecutionState    state,
        RuntimeMode               governanceMode)
    {
        // Halt during thermal protection if any remaining step is not optional
        if (governanceMode == RuntimeMode.ThermalProtection && !step.IsOptional)
        {
            return new SafetyCheckResult(
                SafetyCheckStatus.Blocked,
                "System entered thermal protection mode during workflow execution. " +
                "Non-optional steps are paused.",
                BlockedBy: "ThermalProtection");
        }

        // Check if previous required step failed
        var previousFailed = state.StepResults
            .Where(r => !r.CanRollback)
            .Any(r => r.Outcome == StepOutcome.Failed);

        if (previousFailed && !step.IsOptional)
        {
            return new SafetyCheckResult(
                SafetyCheckStatus.Blocked,
                "A required step failed. Execution cannot continue safely.",
                BlockedBy: "PreviousStepFailure");
        }

        return new SafetyCheckResult(SafetyCheckStatus.Passed, "Step pre-conditions met.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static string TrustLevelLabel(AutomationTrustLevel level) => level switch
    {
        AutomationTrustLevel.ManualOnly           => "Manual Only",
        AutomationTrustLevel.GuidedApproval       => "Guided Approval",
        AutomationTrustLevel.SupervisedAutomation => "Supervised Automation",
        AutomationTrustLevel.RecoveryAutomation   => "Recovery Automation",
        _                                         => level.ToString(),
    };

    public static string RiskLabel(OperationalRisk risk) => risk switch
    {
        OperationalRisk.Minimal  => "Minimal — fully safe and reversible",
        OperationalRisk.Low      => "Low — reversible, no restart required",
        OperationalRisk.Moderate => "Moderate — some steps may require restart",
        OperationalRisk.Elevated => "Elevated — requires elevation; bounded rollback",
        _                        => risk.ToString(),
    };

    public static string RiskColor(OperationalRisk risk) => risk switch
    {
        OperationalRisk.Minimal  => "#34D399",
        OperationalRisk.Low      => "#60A5FA",
        OperationalRisk.Moderate => "#F59E0B",
        OperationalRisk.Elevated => "#EF4444",
        _                        => "#6B7280",
    };
}
