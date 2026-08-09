using Lucid.Services.Diagnostics.Governance;
using Lucid.Services.Infrastructure.OperationalState;
using Lucid.Services.Settings;

namespace Lucid.Services.Intelligence.Learning;

/// <summary>
/// Evaluates the current trust posture and operational state to determine
/// what level of adaptive learning is permitted.
///
/// Learning is a trust-sensitive operation. Lucid must not silently expand
/// the scope of what it learns about a system beyond what the user has
/// consented to.
///
/// Learning permission tiers:
///   Full     — all adaptive learning features active
///   Reduced  — only high-value, low-sensitivity learning active
///   Disabled — no adaptive learning; no new patterns, baselines, or calibration
///
/// Conditions that reduce or disable learning:
///   - Consent mode "NeverAsk"  → Disabled (user has opted out of assistance)
///   - Trust posture tamper     → Reduced (system may be compromised)
///   - Degraded mode active     → Reduced (platform is unreliable)
///   - Usage telemetry enabled  → allowed (local-only learning is always fine)
///
/// Thread-safe: stateless evaluation over injected service snapshots.
/// </summary>
public sealed class GovernanceLearningPolicy
{
    private readonly ISettingsService             _settings;
    private readonly GovernanceDiagnosticsService _governance;
    private readonly IOperationalStateCoordinator _operationalState;

    public GovernanceLearningPolicy(
        ISettingsService             settings,
        GovernanceDiagnosticsService governance,
        IOperationalStateCoordinator operationalState)
    {
        _settings         = settings;
        _governance       = governance;
        _operationalState = operationalState;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates the current posture and returns the applicable learning permission level.
    /// </summary>
    public LearningPermission Evaluate()
    {
        var settings = _settings.Current;
        var state    = _operationalState.Current;
        var auditLog = _governance.GetAuditLog();

        // Opt-out: if consent mode is "NeverAsk", the user has signaled they
        // don't want active analysis. Disable adaptive learning entirely.
        if (string.Equals(settings.ConsentMode, "NeverAsk",
                StringComparison.OrdinalIgnoreCase))
        {
            return LearningPermission.Disabled(
                "Consent mode is set to NeverAsk — adaptive learning disabled.");
        }

        // Trust tamper: a tampered consent file indicates an unexpected environment change.
        if (auditLog.Any(e => e.TransitionType == "TrustPostureTampered"))
        {
            return LearningPermission.Reduced(
                "Trust posture tamper detected — adaptive learning reduced to high-confidence patterns only.");
        }

        // Degraded platform: learning from degraded-service data could corrupt baselines.
        if (state.DegradedServiceCount >= 2)
        {
            return LearningPermission.Reduced(
                $"{state.DegradedServiceCount} services are degraded — learning paused to avoid baseline corruption.");
        }

        return LearningPermission.Full();
    }

    /// <summary>
    /// Returns true when pattern recognition is permitted at the given confidence level.
    /// Under Reduced permission, only high-confidence (≥ 0.70) patterns are recorded.
    /// </summary>
    public bool IsPatternRecognitionPermitted(double patternConfidence)
    {
        var permission = Evaluate();
        return permission.Level switch
        {
            LearningPermissionLevel.Full     => true,
            LearningPermissionLevel.Reduced  => patternConfidence >= 0.70,
            LearningPermissionLevel.Disabled => false,
            _                               => false,
        };
    }

    /// <summary>
    /// Returns true when baseline updates are permitted.
    /// Under Reduced permission, baseline updates are still allowed (low sensitivity).
    /// Under Disabled permission, no baseline updates occur.
    /// </summary>
    public bool IsBaselineUpdatePermitted()
    {
        var permission = Evaluate();
        return permission.Level != LearningPermissionLevel.Disabled;
    }
}

// ── Permission models ─────────────────────────────────────────────────────────

/// <summary>Learning permission tier.</summary>
public enum LearningPermissionLevel
{
    /// <summary>All adaptive learning features active.</summary>
    Full,

    /// <summary>Only high-confidence, low-sensitivity learning active.</summary>
    Reduced,

    /// <summary>No adaptive learning — user has opted out or system is compromised.</summary>
    Disabled,
}

/// <summary>Result of a <see cref="GovernanceLearningPolicy.Evaluate"/> call.</summary>
public sealed record LearningPermission(
    LearningPermissionLevel Level,
    string?                 Reason)
{
    public static LearningPermission Full()                  => new(LearningPermissionLevel.Full, null);
    public static LearningPermission Reduced(string reason)  => new(LearningPermissionLevel.Reduced, reason);
    public static LearningPermission Disabled(string reason) => new(LearningPermissionLevel.Disabled, reason);

    public bool IsFullPermission    => Level == LearningPermissionLevel.Full;
    public bool IsActive            => Level != LearningPermissionLevel.Disabled;
}
