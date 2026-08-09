namespace Lucid.Services.Learning;

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Builds a <see cref="PersonalizationProfile"/> from recorded intervention history.
/// Pure function — stateless, no side effects.
/// All adaptation is deterministic and locally derived; no cloud, no ML models.
/// </summary>
public interface IPersonalizationEngine
{
    /// <summary>
    /// Computes a fresh <see cref="PersonalizationProfile"/> from the given records.
    /// Returns a cold-start profile when fewer than 3 records are available.
    /// </summary>
    PersonalizationProfile ComputeProfile(IReadOnlyList<InterventionRecord> records);
}

// ── Implementation ─────────────────────────────────────────────────────────────

/// <summary>
/// Deterministic personalization engine.
///
/// Category weight formula:
///   acceptance_rate = accepted / total (for that category)
///   weight = clamp(0.5 + acceptance_rate, 0.5, 1.5)
///
/// This gives:
///   0 % acceptance → weight = 0.50  (suppress)
///   50% acceptance → weight = 1.00  (neutral)
///   100% acceptance → weight = 1.50 (boost)
///
/// Cold-start (< 3 records): all weights default to 1.0.
/// </summary>
public sealed class PersonalizationEngine : IPersonalizationEngine
{
    // Alert-heavy categories for AutomationComfort heuristic
    private static readonly HashSet<string> s_automationCategories =
        new(StringComparer.OrdinalIgnoreCase) { "repair", "startup", "process" };

    public PersonalizationProfile ComputeProfile(IReadOnlyList<InterventionRecord> records)
    {
        if (records.Count < 3)
        {
            return new PersonalizationProfile
            {
                TotalRecords    = records.Count,
                AcceptedCount   = records.Count(r => r.Accepted),
                Style           = UserOperationalStyle.Unknown,
                AlertTolerance  = AlertTolerance.Medium,
                AutomationComfort = AutomationComfort.Manual,
                ComputedAt      = DateTimeOffset.Now,
            };
        }

        int total    = records.Count;
        int accepted = records.Count(r => r.Accepted);

        // ── Per-category rates and weights ────────────────────────────────────
        var byCategory = records
            .GroupBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    int t = g.Count();
                    int a = g.Count(r => r.Accepted);
                    return (Total: t, Accepted: a);
                });

        var categoryRates   = new Dictionary<string, double>();
        var categoryWeights = new Dictionary<string, double>();

        foreach (var (cat, (t, a)) in byCategory)
        {
            double rate   = t > 0 ? (double)a / t : 0.0;
            double weight = Math.Clamp(0.5 + rate, 0.5, 1.5);
            categoryRates[cat]   = rate;
            categoryWeights[cat] = weight;
        }

        // ── Alert tolerance ───────────────────────────────────────────────────
        // Measured by dismissal rate for "Warning"-severity items
        int warningShown    = records.Count(r => r.InsightSeverity == "Warning");
        int warningAccepted = records.Count(r => r.InsightSeverity == "Warning" && r.Accepted);
        double warningRate  = warningShown > 0 ? (double)warningAccepted / warningShown : 0.5;

        var alertTolerance = warningRate switch
        {
            >= 0.7 => AlertTolerance.High,
            >= 0.35 => AlertTolerance.Medium,
            _ => AlertTolerance.Low,
        };

        // ── Automation comfort ────────────────────────────────────────────────
        // Measured by acceptance rate of automation-capable categories
        var autoRecords = records
            .Where(r => s_automationCategories.Contains(r.Category))
            .ToList();
        double autoRate = autoRecords.Count > 0
            ? (double)autoRecords.Count(r => r.Accepted) / autoRecords.Count
            : 0.0;

        var automationComfort = autoRate switch
        {
            >= 0.7 => AutomationComfort.Autonomous,
            >= 0.35 => AutomationComfort.Guided,
            _ => AutomationComfort.Manual,
        };

        // ── Style classification ──────────────────────────────────────────────
        double overallRate = (double)accepted / total;
        var style = ClassifyStyle(records, overallRate, warningRate, categoryRates);

        return new PersonalizationProfile
        {
            TotalRecords          = total,
            AcceptedCount         = accepted,
            CategoryAcceptanceRates = categoryRates,
            CategoryWeights       = categoryWeights,
            Style                 = style,
            AlertTolerance        = alertTolerance,
            AutomationComfort     = automationComfort,
            ComputedAt            = DateTimeOffset.Now,
        };
    }

    // ── Style classification ───────────────────────────────────────────────────

    private static UserOperationalStyle ClassifyStyle(
        IReadOnlyList<InterventionRecord> records,
        double overallRate,
        double warningRate,
        Dictionary<string, double> categoryRates)
    {
        // Hands-off: accepts fewer than 25% of all recommendations
        if (overallRate < 0.25) return UserOperationalStyle.HandsOff;

        // Reactive: accepts warnings at a much higher rate than non-warnings
        int nonWarningShown    = records.Count(r => r.InsightSeverity != "Warning");
        int nonWarningAccepted = records.Count(r => r.InsightSeverity != "Warning" && r.Accepted);
        double nonWarningRate  = nonWarningShown > 0
            ? (double)nonWarningAccepted / nonWarningShown : 0.0;

        if (warningRate > 0.6 && warningRate - nonWarningRate > 0.3)
            return UserOperationalStyle.Reactive;

        // Proactive: accepts non-warnings at a higher rate than warnings (acts early)
        if (nonWarningRate > 0.6 && nonWarningRate - warningRate > 0.2)
            return UserOperationalStyle.Proactive;

        // Investigative: high overall acceptance with many categories used
        if (overallRate >= 0.6 && categoryRates.Count >= 3)
            return UserOperationalStyle.Investigative;

        // Balanced: 35–65% overall, across multiple categories
        if (overallRate is >= 0.35 and <= 0.65)
            return UserOperationalStyle.Balanced;

        return UserOperationalStyle.Balanced;
    }
}
