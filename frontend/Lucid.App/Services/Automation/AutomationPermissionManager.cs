namespace Lucid.Services.Automation;

/// <summary>
/// Manages the current automation permission level and enforces confirmation requirements.
///
/// The user sets the <see cref="AutomationMode"/>; it cannot be changed by any
/// automated logic or observed content. This class is the single authority on
/// what Lucid is permitted to do automatically.
///
/// The default mode is <see cref="AutomationMode.ConfirmBeforeAction"/> — every step
/// requires explicit confirmation, regardless of risk level.
/// </summary>
public sealed class AutomationPermissionManager
{
    private AutomationMode _mode = AutomationMode.ConfirmBeforeAction;

    // ── Mode management ───────────────────────────────────────────────────────

    /// <summary>Current automation mode. Default: ConfirmBeforeAction.</summary>
    public AutomationMode CurrentMode => _mode;

    /// <summary>
    /// Sets the automation mode.
    /// Only call this in response to an explicit user gesture — never programmatically.
    /// </summary>
    public void SetMode(AutomationMode mode)
    {
        _mode = mode;
        ModeChanged?.Invoke(this, mode);
    }

    /// <summary>Raised when the user changes the automation mode.</summary>
    public event EventHandler<AutomationMode>? ModeChanged;

    // ── Permission checks ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the given step requires explicit user confirmation before executing.
    ///
    /// In ObserveOnly and GuidedAssist modes, every step requires confirmation
    /// (or produces guidance text only). In ConfirmBeforeAction, all steps require
    /// confirmation. In SemiAutomatic, only Medium/High risk steps require confirmation.
    /// </summary>
    public bool RequiresConfirmation(AutomationStep step) => _mode switch
    {
        AutomationMode.ObserveOnly         => true,
        AutomationMode.GuidedAssist        => true,
        AutomationMode.ConfirmBeforeAction => true,
        AutomationMode.SemiAutomatic       => step.Risk >= AutomationRiskLevel.Medium,
        _                                  => true,
    };

    /// <summary>
    /// Returns true if the step can actually execute (i.e., not ObserveOnly mode).
    /// ObserveOnly produces guidance text — the executor is never called.
    /// </summary>
    public bool CanExecute(AutomationStep step)
        => _mode != AutomationMode.ObserveOnly;

    /// <summary>
    /// Returns true if the step can auto-advance without a confirmation card.
    /// Only true in SemiAutomatic mode for None/Low risk steps.
    /// </summary>
    public bool CanAutoAdvance(AutomationStep step)
        => _mode == AutomationMode.SemiAutomatic && step.Risk < AutomationRiskLevel.Medium;

    // ── Describe mode ─────────────────────────────────────────────────────────

    /// <summary>Short human-readable label for the current mode.</summary>
    public string ModeLabel => _mode switch
    {
        AutomationMode.ObserveOnly         => "Observe only",
        AutomationMode.GuidedAssist        => "Guided assist",
        AutomationMode.ConfirmBeforeAction => "Confirm before action",
        AutomationMode.SemiAutomatic       => "Semi-automatic",
        _                                  => "Unknown",
    };

    /// <summary>Explanation sentence shown in the Settings/Companion panel.</summary>
    public string ModeDescription => _mode switch
    {
        AutomationMode.ObserveOnly =>
            "Lucid explains and guides. You do everything yourself.",
        AutomationMode.GuidedAssist =>
            "Lucid narrates each step. You follow the instructions at your own pace.",
        AutomationMode.ConfirmBeforeAction =>
            "Lucid prepares each action. You confirm before anything runs.",
        AutomationMode.SemiAutomatic =>
            "Safe steps run automatically. Risky steps still require your approval.",
        _ => string.Empty,
    };
}
