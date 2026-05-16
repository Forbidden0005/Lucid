namespace ExplainMyPC.Intelligence.Rules;

/// <summary>
/// A single deterministic rule that inspects system state and optionally
/// emits a finding.
///
/// Contract:
///   • Return null  → condition not met; no issue to report.
///   • Return result → a real finding; threshold was crossed with sufficient evidence.
///   • Must never throw. Catch internal errors and return null.
///   • Must be stateless. RuleContext carries all required state.
///   • Must be fast (no I/O, no blocking calls). The engine calls rules
///     synchronously on the telemetry thread.
/// </summary>
public interface IRule
{
    string      RuleId   { get; }
    RuleResult? Evaluate(RuleContext context);
}
