using FluentAssertions;
using Lucid.Services.Governance;
using Xunit;

namespace Lucid.Tests.Governance;

/// <summary>
/// Proves the concurrency budget actually admits, refuses, and releases slots
/// as designed: per-category hard limits, the mode-driven background ceiling,
/// foreground bypass, and the unmatched-release no-op guarantee. This is the
/// enforcement core of the resource-governance doctrine ("Lucid must never
/// become the reason the PC is slow").
/// </summary>
public sealed class ConcurrencyBudgetTests
{
    // ── Per-category hard limits ──────────────────────────────────────────────

    [Fact]
    public void TryAcquire_SecondSlotInLimitOneCategory_RefusedWithReason()
    {
        var budget = new ConcurrencyBudget();

        budget.TryAcquire(WorkloadCategory.StorageScan, "scan-1", out _).Should().BeTrue();
        budget.TryAcquire(WorkloadCategory.StorageScan, "scan-2", out var reason).Should().BeFalse();

        reason.Should().NotBeNullOrWhiteSpace();
        reason.Should().Contain("1/1");
    }

    [Fact]
    public void Release_ThenReacquire_SameCategorySucceeds()
    {
        var budget = new ConcurrencyBudget();
        budget.TryAcquire(WorkloadCategory.DuplicateHashing, "hash-1", out _).Should().BeTrue();

        budget.Release(WorkloadCategory.DuplicateHashing, "hash-1");

        budget.TryAcquire(WorkloadCategory.DuplicateHashing, "hash-2", out var reason).Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void PerCategoryLimits_AreIndependent()
    {
        var budget = new ConcurrencyBudget();

        // Both are limit-1 IdleOnly categories — filling one must not affect the other.
        budget.TryAcquire(WorkloadCategory.StorageScan, "scan", out _).Should().BeTrue();
        budget.TryAcquire(WorkloadCategory.HistoricalAnalytics, "analytics", out var reason).Should().BeTrue();
        reason.Should().BeNull();
    }

    // ── Background ceiling ────────────────────────────────────────────────────

    [Fact]
    public void BackgroundCeiling_FourthBackgroundAcquire_RefusedAtMaxThree()
    {
        var budget = new ConcurrencyBudget(initialMaxBackground: 3);

        budget.TryAcquire(WorkloadCategory.TelemetrySampling,   "a", out _).Should().BeTrue();
        budget.TryAcquire(WorkloadCategory.ProcessIntelligence, "b", out _).Should().BeTrue();
        budget.TryAcquire(WorkloadCategory.NarrativeGeneration, "c", out _).Should().BeTrue();

        budget.TryAcquire(WorkloadCategory.InsightAnalysis, "d", out var reason).Should().BeFalse();
        reason.Should().Contain("Background concurrency limit (3)");
    }

    [Fact]
    public void ForegroundWorkloads_BypassTheBackgroundCeiling()
    {
        var budget = new ConcurrencyBudget(initialMaxBackground: 0);

        // ExplainReasoning is Foreground with no per-category limit.
        budget.TryAcquire(WorkloadCategory.ExplainReasoning, "explain-1", out _).Should().BeTrue();
        budget.TryAcquire(WorkloadCategory.ExplainReasoning, "explain-2", out var reason).Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void IdleOnlyWork_CountsAgainstTheBackgroundCeiling()
    {
        var budget = new ConcurrencyBudget(initialMaxBackground: 1);

        // IdleOnly (StorageScan) consumes the single background slot...
        budget.TryAcquire(WorkloadCategory.StorageScan, "scan", out _).Should().BeTrue();

        // ...so a Background workload is now refused.
        budget.TryAcquire(WorkloadCategory.TelemetrySampling, "poll", out var reason).Should().BeFalse();
        reason.Should().Contain("Background concurrency limit (1)");
    }

    [Fact]
    public void IdleOnlyWork_RefusedWithPausedReasonWhenBudgetIsZero()
    {
        var budget = new ConcurrencyBudget(initialMaxBackground: 0);

        budget.TryAcquire(WorkloadCategory.StorageScan, "scan", out var reason).Should().BeFalse();
        reason.Should().Contain("paused");
    }

    // ── UpdateMaxBackground mid-flight ────────────────────────────────────────

    [Fact]
    public void UpdateMaxBackground_LoweringBelowUsage_RefusesNewWorkButDoesNotEvict()
    {
        var budget = new ConcurrencyBudget(initialMaxBackground: 3);
        budget.TryAcquire(WorkloadCategory.TelemetrySampling,   "a", out _).Should().BeTrue();
        budget.TryAcquire(WorkloadCategory.ProcessIntelligence, "b", out _).Should().BeTrue();

        budget.UpdateMaxBackground(1);

        budget.TryAcquire(WorkloadCategory.NarrativeGeneration, "c", out _).Should().BeFalse();
        budget.GetActiveWorkloads().Should().HaveCount(2, "lowering the ceiling never evicts running work");
    }

    [Fact]
    public void UpdateMaxBackground_Raising_ReadmitsNewWork()
    {
        var budget = new ConcurrencyBudget(initialMaxBackground: 0);
        budget.TryAcquire(WorkloadCategory.TelemetrySampling, "a", out _).Should().BeFalse();

        budget.UpdateMaxBackground(3);

        budget.TryAcquire(WorkloadCategory.TelemetrySampling, "a", out _).Should().BeTrue();
    }

    // ── Unmatched-release safety (governance audit 2026-07-25, finding 6) ─────

    /// <summary>
    /// A Release for a workload that was never acquired must be a true no-op.
    /// Before the fix it decremented the shared background counter belonging to
    /// another in-flight workload, creating phantom capacity above the ceiling.
    /// </summary>
    [Fact]
    public void Release_UnmatchedWorkload_DoesNotCreatePhantomBackgroundCapacity()
    {
        var budget = new ConcurrencyBudget(initialMaxBackground: 1);
        budget.TryAcquire(WorkloadCategory.TelemetrySampling, "real-work", out _).Should().BeTrue();

        // Unmatched release: this (category, name) pair never acquired a slot.
        budget.Release(WorkloadCategory.ProcessIntelligence, "never-acquired");

        // The single background slot is still held by "real-work" — a second
        // background workload must still be refused.
        budget.TryAcquire(WorkloadCategory.NarrativeGeneration, "b", out var reason)
            .Should().BeFalse("the unmatched release must not free the slot held by real-work");
        reason.Should().Contain("Background concurrency limit (1)");
        budget.GetActiveWorkloads().Should().ContainSingle(w => w.Name == "real-work");
    }

    [Fact]
    public void Release_OnEmptyBudget_IsANoOp()
    {
        var budget = new ConcurrencyBudget();

        budget.Release(WorkloadCategory.StorageScan, "ghost");

        budget.GetActiveCount(WorkloadCategory.StorageScan).Should().Be(0);
        budget.GetActiveWorkloads().Should().BeEmpty();
        budget.GetSnapshot(RuntimeMode.Normal, ThrottlingReason.None)
            .UsedBackgroundSlots.Should().Be(0);
    }

    [Fact]
    public void Release_SameCategoryDifferentName_DoesNotReleaseTheHeldSlot()
    {
        var budget = new ConcurrencyBudget();
        budget.TryAcquire(WorkloadCategory.StorageScan, "scan-1", out _).Should().BeTrue();

        budget.Release(WorkloadCategory.StorageScan, "scan-2");

        budget.GetActiveCount(WorkloadCategory.StorageScan).Should().Be(1);
        budget.TryAcquire(WorkloadCategory.StorageScan, "scan-3", out _)
            .Should().BeFalse("scan-1 still holds the category's only slot");
    }

    // ── Observation ───────────────────────────────────────────────────────────

    [Fact]
    public void GetActiveWorkloads_ReturnsAPointInTimeCopy()
    {
        var budget = new ConcurrencyBudget();
        budget.TryAcquire(WorkloadCategory.StorageScan, "scan", out _);

        var snapshot = budget.GetActiveWorkloads();
        budget.Release(WorkloadCategory.StorageScan, "scan");

        snapshot.Should().HaveCount(1, "the snapshot is a copy, not a live view");
        budget.GetActiveWorkloads().Should().BeEmpty();
    }

    [Fact]
    public void GetSnapshot_ReflectsModeReasonsAndSlotUsage()
    {
        var budget = new ConcurrencyBudget(initialMaxBackground: 3);
        budget.TryAcquire(WorkloadCategory.TelemetrySampling, "a", out _);
        budget.TryAcquire(WorkloadCategory.ActionExecution,   "fg", out _);

        var snap = budget.GetSnapshot(RuntimeMode.HighLoad, ThrottlingReason.CpuPressure);

        snap.Mode.Should().Be(RuntimeMode.HighLoad);
        snap.Reasons.Should().Be(ThrottlingReason.CpuPressure);
        snap.ActiveWorkloadCount.Should().Be(2);
        snap.MaxConcurrentBackground.Should().Be(3);
        snap.UsedBackgroundSlots.Should().Be(1, "foreground work never counts against the ceiling");
        snap.TelemetryInterval.Should().Be(TimeSpan.FromSeconds(3.0));
        snap.ProcessSampleInterval.Should().Be(TimeSpan.FromSeconds(9.0));
    }

    [Fact]
    public void GetActiveCount_TracksPerCategory()
    {
        var budget = new ConcurrencyBudget();
        budget.TryAcquire(WorkloadCategory.ExplainReasoning, "e1", out _);
        budget.TryAcquire(WorkloadCategory.ExplainReasoning, "e2", out _);

        budget.GetActiveCount(WorkloadCategory.ExplainReasoning).Should().Be(2);
        budget.GetActiveCount(WorkloadCategory.StorageScan).Should().Be(0);
    }

    // ── Thread-safety smoke ───────────────────────────────────────────────────

    [Fact]
    public void ParallelAcquireRelease_NeverCorruptsCounts()
    {
        var budget = new ConcurrencyBudget(initialMaxBackground: 2);

        Parallel.For(0, 200, i =>
        {
            var name = $"w-{i}";
            if (budget.TryAcquire(WorkloadCategory.TelemetrySampling, name, out _))
            {
                budget.GetActiveWorkloads().Count.Should().BeGreaterThan(0);
                budget.Release(WorkloadCategory.TelemetrySampling, name);
            }
        });

        budget.GetActiveCount(WorkloadCategory.TelemetrySampling).Should().Be(0);
        budget.GetActiveWorkloads().Should().BeEmpty();
        budget.GetSnapshot(RuntimeMode.Normal, ThrottlingReason.None)
            .UsedBackgroundSlots.Should().Be(0);
    }
}
