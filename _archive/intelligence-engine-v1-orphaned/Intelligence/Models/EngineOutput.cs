using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Models;

/// <summary>
/// Everything the Intelligence Engine produces for a single system snapshot.
///
/// Issues maps directly to what the Dashboard ViewModel binds — the engine
/// converts internal RuleResults to SystemIssue records so no Intelligence
/// types leak into the UI layer.
///
/// Recommendations are flattened across all fired rules, deduplicated, and
/// ordered by expected impact. They feed the Repairs page and the bottom of
/// each issue card.
///
/// SystemSummary is a single human-readable paragraph suitable for display
/// in the "Explain My PC" banner on the Dashboard.
/// </summary>
public sealed record EngineOutput(
    HealthScoreModel             Health,
    IReadOnlyList<SystemIssue>   Issues,
    IReadOnlyList<Recommendation> Recommendations,
    string                       SystemSummary);
