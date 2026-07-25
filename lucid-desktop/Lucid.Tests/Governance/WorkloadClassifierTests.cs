using FluentAssertions;
using Lucid.Services.Governance;
using Xunit;

namespace Lucid.Tests.Governance;

/// <summary>
/// Pins the authoritative category → priority map. If a category silently
/// drifts to a different priority class, workloads start bypassing (or being
/// wrongly caught by) the background ceiling and the idle-only gate.
/// </summary>
public sealed class WorkloadClassifierTests
{
    [Theory]
    [InlineData(WorkloadCategory.ActionExecution,  WorkloadPriority.Foreground)]
    [InlineData(WorkloadCategory.ExplainReasoning, WorkloadPriority.Foreground)]
    [InlineData(WorkloadCategory.ReplayAnalysis,   WorkloadPriority.Foreground)]
    [InlineData(WorkloadCategory.TelemetrySampling,   WorkloadPriority.Background)]
    [InlineData(WorkloadCategory.ProcessIntelligence, WorkloadPriority.Background)]
    [InlineData(WorkloadCategory.NarrativeGeneration, WorkloadPriority.Background)]
    [InlineData(WorkloadCategory.TimelineAggregation, WorkloadPriority.Background)]
    [InlineData(WorkloadCategory.InsightAnalysis,     WorkloadPriority.Background)]
    [InlineData(WorkloadCategory.StorageScan,         WorkloadPriority.IdleOnly)]
    [InlineData(WorkloadCategory.DuplicateHashing,    WorkloadPriority.IdleOnly)]
    [InlineData(WorkloadCategory.HistoricalAnalytics, WorkloadPriority.IdleOnly)]
    [InlineData(WorkloadCategory.LearningAnalysis,    WorkloadPriority.IdleOnly)]
    [InlineData(WorkloadCategory.RollbackMaintenance, WorkloadPriority.IdleOnly)]
    public void Classify_MapsEveryCategoryToDoctrinePriority(
        WorkloadCategory category, WorkloadPriority expected)
    {
        WorkloadClassifier.Classify(category).Should().Be(expected);
    }

    [Fact]
    public void Classify_UnknownCategory_FallsBackToBackground()
    {
        var unknown = (WorkloadCategory)999;

        WorkloadClassifier.Classify(unknown).Should().Be(WorkloadPriority.Background);
    }

    [Fact]
    public void GetDisplayName_ReturnsNonEmptyForEveryDefinedCategory()
    {
        foreach (var category in Enum.GetValues<WorkloadCategory>())
        {
            WorkloadClassifier.GetDisplayName(category)
                .Should().NotBeNullOrWhiteSpace(
                    $"category {category} needs a display name for the governance UI");
        }
    }

    [Fact]
    public void GetDisplayName_UnknownCategory_FallsBackToEnumText()
    {
        var unknown = (WorkloadCategory)999;

        WorkloadClassifier.GetDisplayName(unknown).Should().Be("999");
    }
}
