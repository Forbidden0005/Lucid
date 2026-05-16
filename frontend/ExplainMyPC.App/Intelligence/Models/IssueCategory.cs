namespace ExplainMyPC.Intelligence.Models;

/// <summary>
/// Canonical category for a detected issue.
/// Maps to health score dimensions via HealthScorer.
/// </summary>
public enum IssueCategory
{
    Performance,    // maps to PerformanceScore
    Memory,         // maps to PerformanceScore (memory pressure affects perf)
    Startup,        // maps to PerformanceScore + StabilityScore
    Thermal,        // maps to StabilityScore
    Storage,        // maps to StorageScore
    Security,       // maps to SecurityScore
    Stability,      // maps to StabilityScore
    Privacy,        // maps to PrivacyScore
}

public static class IssueCategoryExtensions
{
    public static string ToDisplayString(this IssueCategory category) => category switch
    {
        IssueCategory.Performance => "Performance",
        IssueCategory.Memory      => "Memory",
        IssueCategory.Startup     => "Startup",
        IssueCategory.Thermal     => "Temperature",
        IssueCategory.Storage     => "Storage",
        IssueCategory.Security    => "Security",
        IssueCategory.Stability   => "Stability",
        IssueCategory.Privacy     => "Privacy",
        _                         => category.ToString()
    };
}
