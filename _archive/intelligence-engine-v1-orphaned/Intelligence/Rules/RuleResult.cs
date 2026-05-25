using ExplainMyPC.Intelligence.Models;
using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Rules;

/// <summary>
/// What a rule produces when it detects an issue.
///
/// A null return from IRule.Evaluate() means "no issue found."
/// A RuleResult always represents a real finding — rules must not emit
/// results below their evidence threshold.
///
/// Recommendations are specific to this rule's issue. The engine deduplicates
/// and flattens them across all fired rules in EngineOutput.
/// </summary>
public sealed record RuleResult(
    string                         RuleId,
    string                         Title,
    string                         Description,
    IssueSeverity                  Severity,
    IssueConfidence                Confidence,
    IssueCategory                  Category,
    bool                           CanFix,
    IReadOnlyList<Recommendation>  Recommendations)
{
    /// <summary>Convert to the ViewModel-facing model. No Intelligence types escape.</summary>
    internal SystemIssue ToSystemIssue() =>
        new(Title, Description, Severity, Category.ToDisplayString(), CanFix, Confidence);
}
