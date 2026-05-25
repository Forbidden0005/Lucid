namespace ExplainMyPC.Intelligence.Models;

/// <summary>
/// An actionable step a user can take to resolve a detected issue.
///
/// Risk describes what could go wrong if the recommendation is followed.
/// SupportsRollback indicates whether the system can undo the action
/// (e.g., re-enabling a startup app is trivial; deleting files is not).
/// </summary>
public sealed record Recommendation(
    string            Title,
    string            Reason,
    string            ExpectedBenefit,
    RecommendationRisk Risk,
    bool              SupportsRollback);

public enum RecommendationRisk
{
    None,   // Read-only or purely informational
    Low,    // Reversible with one click
    Medium, // Reversible but requires a few steps
    High,   // Hard to reverse or affects system behaviour permanently
}
