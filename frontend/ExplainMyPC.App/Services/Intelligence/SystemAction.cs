namespace ExplainMyPC.Services.Intelligence;

// ── Dimension enums ───────────────────────────────────────────────────────────

/// <summary>
/// How much improvement the action is expected to deliver once completed.
/// Values are ordered: higher integers represent greater benefit.
/// </summary>
public enum ActionImpact
{
    /// <summary>Minor or diagnostic benefit — helps identify the problem.</summary>
    Low         = 0,

    /// <summary>Noticeable improvement in the affected metric.</summary>
    Moderate    = 1,

    /// <summary>Substantial improvement — resolves most of the symptom.</summary>
    High        = 2,

    /// <summary>Transformative improvement — eliminates the root cause.</summary>
    Significant = 3,
}

/// <summary>
/// How much active user effort the action requires.
/// Values are ordered: lower is faster.
/// </summary>
public enum ActionEffort
{
    /// <summary>Takes under a minute — a few clicks or keystrokes.</summary>
    Seconds     = 0,

    /// <summary>Takes two to five minutes of user attention.</summary>
    FewMinutes  = 1,

    /// <summary>Takes around ten minutes, possibly including wait time.</summary>
    TenMinutes  = 2,
}

/// <summary>
/// Probability and severity of unintended side effects.
/// Values are ordered: higher integers represent greater risk.
/// </summary>
public enum ActionRisk
{
    /// <summary>No meaningful risk — fully reversible, no data exposure.</summary>
    Safe        = 0,

    /// <summary>Minimal risk — reversible but requires a few extra steps.</summary>
    Low         = 1,

    /// <summary>
    /// Some risk — may affect application state or cause minor data loss
    /// (e.g. unsaved work). Should be confirmed by the user.
    /// </summary>
    Moderate    = 2,

    /// <summary>
    /// Elevated risk — involves hardware, system settings, or irreversible
    /// changes. Requires explicit user acknowledgement.
    /// </summary>
    High        = 3,
}

// ── SystemAction record ───────────────────────────────────────────────────────

/// <summary>
/// A single recommended remediation step attached to a <see cref="SystemInsight"/>.
///
/// Actions are data-only for now — no execution engine, no UAC elevation.
/// They express what the user <em>could</em> do, at what cost, and at what risk.
/// The UI surfaces them inside the expanded insight card so the user can make
/// an informed decision before acting manually.
///
/// Authoring guidance:
///   • Keep <see cref="Title"/> to ≤ 5 words (fits in a badge).
///   • Keep <see cref="Description"/> to 1–2 plain-English sentences.
///   • Set <see cref="RequiresConfirmation"/> = true whenever
///     <see cref="Risk"/> ≥ <see cref="ActionRisk.Moderate"/> or
///     <see cref="IsReversible"/> = false.
///   • Always provide <see cref="ConfirmationMessage"/> when
///     <see cref="RequiresConfirmation"/> is true.
///   • Allocate action instances as static readonly fields on the rule class
///     to avoid per-tick heap allocations.
/// </summary>
/// <param name="Id">
///     Stable dot-separated identifier, e.g. "action.disk.clean-temp-files".
///     Used for deduplication and future analytics.
/// </param>
/// <param name="Title">Short label for the action card header (≤ 5 words).</param>
/// <param name="Description">Plain-English explanation of what the action does.</param>
/// <param name="Impact">Expected benefit when the action is completed.</param>
/// <param name="Effort">How much user time and attention is required.</param>
/// <param name="Risk">Probability and severity of unintended side effects.</param>
/// <param name="RequiresConfirmation">
///     True if the UI should display a confirmation message before the user
///     proceeds. Always true when Risk ≥ Moderate or IsReversible is false.
/// </param>
/// <param name="IsReversible">
///     True if the action can be fully undone without data loss.
///     Shown as a chip on the action card to reassure cautious users.
/// </param>
/// <param name="ConfirmationMessage">
///     Plain-English caution shown in the confirmation chip when
///     RequiresConfirmation is true. Should state specifically what will be
///     lost or changed so the user can make an informed choice.
/// </param>
public sealed record SystemAction(
    string       Id,
    string       Title,
    string       Description,
    ActionImpact Impact,
    ActionEffort Effort,
    ActionRisk   Risk,
    bool         RequiresConfirmation,
    bool         IsReversible,
    string?      ConfirmationMessage = null);
