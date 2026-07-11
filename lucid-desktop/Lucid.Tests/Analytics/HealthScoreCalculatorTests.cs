using FluentAssertions;
using Lucid.Services.Analytics;
using Lucid.Services.Persistence;
using Xunit;

namespace Lucid.Tests.Analytics;

/// <summary>
/// Guards the health-score math — specifically the regression where a single
/// persistent condition that re-fired all week collapsed the score to 0/100
/// ("Needs Attention" on an otherwise-healthy machine). Scoring is now by
/// DISTINCT condition, not raw occurrence count.
/// </summary>
public sealed class HealthScoreCalculatorTests
{
    private static PersistedInsightRecord Rec(
        string insightId, int severity, DateTimeOffset onset,
        string title = "t", DateTimeOffset? resolvedAt = null)
        => new()
        {
            InsightId       = insightId,
            Title           = title,
            SeverityOrdinal = severity,
            OnsetAt         = onset,
            ResolvedAt      = resolvedAt,
        };

    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoInsights_ScoresPerfect()
    {
        var s = HealthScoreCalculator.Compute([], [], TimeSpan.FromDays(7));

        s.Score.Should().Be(100);
        s.Label.Should().Be("Excellent");
        s.Contributions.Should().BeEmpty();
    }

    [Fact]
    public void OnePersistentCondition_FiringManyTimes_DoesNotCollapseScore()
    {
        // The exact field bug: one warning condition re-fires 30× in a week.
        // Old formula: 100 - 30*5 = clamped to 0. New: one distinct warning
        // (+ recurrence bump) = 100 - 10 = 90.
        var records = Enumerable.Range(0, 30)
            .Select(i => Rec("disk.almost-full", severity: 2, T0.AddHours(i)))
            .ToList();

        var s = HealthScoreCalculator.Compute(records, [], TimeSpan.FromDays(7));

        s.Score.Should().Be(90, "one recurring warning costs 5 + 5 recurrence, not 5×30");
        s.Contributions.Should().ContainSingle();
        s.Contributions[0].InsightId.Should().Be("disk.almost-full");
        s.Contributions[0].Occurrences.Should().Be(30);
        s.Contributions[0].PointsDeducted.Should().Be(10);
    }

    [Fact]
    public void SingleWarningOccurrence_CostsBasePenaltyOnly()
    {
        var s = HealthScoreCalculator.Compute(
            [Rec("cpu.spike", severity: 2, T0)], [], TimeSpan.FromDays(7));

        s.Score.Should().Be(95, "a one-off warning costs 5, no recurrence bump");
        s.Contributions[0].PointsDeducted.Should().Be(5);
    }

    [Fact]
    public void DistinctConditions_EachCountOnce()
    {
        var records = new[]
        {
            Rec("a", 2, T0),
            Rec("b", 2, T0),
            Rec("c", 1, T0), // recommendation
        };

        var s = HealthScoreCalculator.Compute(records, [], TimeSpan.FromDays(7));

        // 100 - 5(a) - 5(b) - 2(c) = 88
        s.Score.Should().Be(88);
        s.Contributions.Should().HaveCount(3);
    }

    [Fact]
    public void InformationalInsights_DoNotAffectScoreOrBreakdown()
    {
        var s = HealthScoreCalculator.Compute(
            [Rec("info", severity: 0, T0), Rec("info2", severity: 0, T0)],
            [], TimeSpan.FromDays(7));

        s.Score.Should().Be(100);
        s.Contributions.Should().BeEmpty();
    }

    [Fact]
    public void ManyDistinctWarnings_ClampsAtZero_NeverNegative()
    {
        var records = Enumerable.Range(0, 40)
            .Select(i => Rec($"cond-{i}", severity: 2, T0))
            .ToList();

        var s = HealthScoreCalculator.Compute(records, [], TimeSpan.FromDays(7));

        s.Score.Should().Be(0, "clamped, not negative");
    }

    [Fact]
    public void Trend_ReflectsImprovementVsPreviousWindow()
    {
        var current  = new[] { Rec("a", 2, T0) };                    // score 95
        var previous = new[] { Rec("a", 2, T0), Rec("b", 2, T0),     // score 85
                               Rec("c", 2, T0) };

        var s = HealthScoreCalculator.Compute(current, previous, TimeSpan.FromDays(7));

        s.Trend.Should().Be(TrendDirection.Improving, "95 vs 85 is +10");
    }

    [Fact]
    public void Contributions_OrderedByPointsThenOccurrences()
    {
        var records = new[]
        {
            Rec("light", 1, T0),                                  // -2
            Rec("heavy", 2, T0), Rec("heavy", 2, T0.AddHours(1)), // recurring? only 2 < threshold 3 → -5
            Rec("recurring", 2, T0), Rec("recurring", 2, T0.AddHours(1)), Rec("recurring", 2, T0.AddHours(2)), // -10
        };

        var s = HealthScoreCalculator.Compute(records, [], TimeSpan.FromDays(7));

        s.Contributions[0].InsightId.Should().Be("recurring", "highest deduction first");
        s.Contributions[0].PointsDeducted.Should().Be(10);
        s.Contributions.Last().InsightId.Should().Be("light");
    }

    [Fact]
    public void Contribution_IsActive_TracksLatestOnsetResolution()
    {
        // Latest onset is unresolved → active (has a live fix path).
        var active = HealthScoreCalculator.Compute(
            [Rec("x", 2, T0, resolvedAt: T0.AddHours(1)),   // earlier, resolved
             Rec("x", 2, T0.AddHours(2), resolvedAt: null)], // latest, still open
            [], TimeSpan.FromDays(7));
        active.Contributions[0].IsActive.Should().BeTrue();

        // Latest onset resolved → historical, no live fix.
        var resolved = HealthScoreCalculator.Compute(
            [Rec("y", 2, T0, resolvedAt: T0.AddHours(3))],
            [], TimeSpan.FromDays(7));
        resolved.Contributions[0].IsActive.Should().BeFalse();
    }

    // ── Resolved-condition weighting (second score-collapse fix) ─────────────
    // A busy week whose conditions all cleared must not read as a broken
    // machine: resolved conditions carry a token penalty with a combined cap.

    [Fact]
    public void ResolvedWarning_CostsTokenPenaltyOnly()
    {
        var s = HealthScoreCalculator.Compute(
            [Rec("x", 2, T0, resolvedAt: T0.AddHours(1))], [], TimeSpan.FromDays(7));

        s.Score.Should().Be(99, "a resolved one-off warning costs 1, not 5");
        s.Contributions[0].PointsDeducted.Should().Be(
            HealthScoreCalculator.PenaltyPerResolvedCondition);
    }

    [Fact]
    public void ResolvedRecurringWarning_CostsSlightlyMore()
    {
        var records = Enumerable.Range(0, 5)
            .Select(i => Rec("x", 2, T0.AddHours(i), resolvedAt: T0.AddHours(i).AddMinutes(10)))
            .ToList();

        var s = HealthScoreCalculator.Compute(records, [], TimeSpan.FromDays(7));

        s.Score.Should().Be(97, "a resolved recurring warning costs 3");
        s.Contributions[0].PointsDeducted.Should().Be(
            HealthScoreCalculator.PenaltyPerResolvedRecurringWarning);
    }

    [Fact]
    public void ManyResolvedConditions_CappedAtMaxResolvedPenalty()
    {
        // 30 distinct resolved warnings — the exact field bug where a noisy but
        // fully-recovered week collapsed the score to 0.
        var records = Enumerable.Range(0, 30)
            .Select(i => Rec($"cond-{i}", severity: 2, T0.AddMinutes(i),
                             resolvedAt: T0.AddMinutes(i + 5)))
            .ToList();

        var s = HealthScoreCalculator.Compute(records, [], TimeSpan.FromDays(7));

        s.Score.Should().Be(100 - HealthScoreCalculator.MaxResolvedPenalty,
            "resolved history is capped so it can never bury the current picture");
    }

    [Fact]
    public void ActiveConditions_NotAffectedByResolvedCap()
    {
        var records = new List<PersistedInsightRecord>
        {
            Rec("active-1", 2, T0),                                   // active warning −5
            Rec("active-2", 2, T0),                                   // active warning −5
        };
        // 20 resolved warnings → raw 20, capped at 15
        records.AddRange(Enumerable.Range(0, 20)
            .Select(i => Rec($"res-{i}", 2, T0.AddMinutes(i), resolvedAt: T0.AddHours(1))));

        var s = HealthScoreCalculator.Compute(records, [], TimeSpan.FromDays(7));

        s.Score.Should().Be(100 - 10 - HealthScoreCalculator.MaxResolvedPenalty,
            "active conditions keep full weight; only the resolved pile is capped");
    }

    [Fact]
    public void ResolvedSuggestion_CostsTokenPenalty()
    {
        var s = HealthScoreCalculator.Compute(
            [Rec("tip", 1, T0, resolvedAt: T0.AddHours(1))], [], TimeSpan.FromDays(7));

        s.Score.Should().Be(99);
    }

    [Fact]
    public void NullInputs_TreatedAsEmpty_NoThrow()
    {
        var act = () => HealthScoreCalculator.Compute(null!, null!, TimeSpan.FromDays(7));

        act.Should().NotThrow();
        HealthScoreCalculator.Compute(null!, null!, TimeSpan.FromDays(7)).Score.Should().Be(100);
    }
}
