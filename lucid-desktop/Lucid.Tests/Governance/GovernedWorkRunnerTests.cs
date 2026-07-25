using FluentAssertions;
using Lucid.Services.Governance;
using Xunit;

namespace Lucid.Tests.Governance;

/// <summary>
/// Verifies the defer-for-retry contract used by every governed call site.
/// If this regresses, work refused by the concurrency budget goes back to
/// being silently dropped (governance adoption audit 2026-07-25, §1) instead
/// of retried when the budget loosens.
/// </summary>
public sealed class GovernedWorkRunnerTests
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Test double built on the REAL ConcurrencyBudget + ExecutionPriorityQueue
    /// so the retry choreography (refuse → enqueue → release → drain → retry)
    /// is exercised end to end without WinUI dependencies.
    /// </summary>
    private sealed class TestGovernance : IRuntimeGovernanceService
    {
        private readonly ConcurrencyBudget      _budget = new(initialMaxBackground: 3);
        public  readonly ExecutionPriorityQueue Queue   = new();

        /// <summary>When true, every acquire is refused regardless of budget.</summary>
        public bool RefuseAll { get; set; }

        public event EventHandler<RuntimeModeChangedEventArgs>? ModeChanged
        { add { } remove { } }

        public RuntimeMode      CurrentMode    => RuntimeMode.Normal;
        public ThrottlingReason CurrentReasons => ThrottlingReason.None;

        public ResourceBudgetSnapshot GetSnapshot()
            => _budget.GetSnapshot(RuntimeMode.Normal, ThrottlingReason.None);

        public IReadOnlyList<ActiveWorkloadEntry>   GetActiveWorkloads()   => _budget.GetActiveWorkloads();
        public IReadOnlyList<DeferredWorkloadEntry> GetDeferredWorkloads() => Queue.GetSnapshot();

        public bool TryAcquireSlot(WorkloadCategory category, string workloadName, out string? refusalReason)
        {
            if (RefuseAll)
            {
                refusalReason = "Refused by test policy.";
                return false;
            }
            return _budget.TryAcquire(category, workloadName, out refusalReason);
        }

        public void ReleaseSlot(WorkloadCategory category, string workloadName)
        {
            _budget.Release(category, workloadName);
            Queue.Drain();
        }

        public void DeferWorkload(WorkloadCategory category, string workloadName, string? refusalReason, Func<Task> retry)
            => Queue.Enqueue(
                new DeferredWorkloadEntry(
                    workloadName, category, WorkloadPriority.IdleOnly,
                    DateTimeOffset.Now, refusalReason ?? "System is busy."),
                retry);

        public void Start() { }
        public void Stop()  { }
    }

    // ── RunOrDeferAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunOrDefer_SlotAvailable_RunsWork_AndReleasesSlot()
    {
        var gov = new TestGovernance();
        var ran = false;

        var started = await GovernedWorkRunner.RunOrDeferAsync(
            gov, WorkloadCategory.LearningAnalysis, "learning pass",
            () => { ran = true; return Task.CompletedTask; });

        started.Should().BeTrue();
        ran.Should().BeTrue();
        gov.GetActiveWorkloads().Should().BeEmpty("the slot must be released after the work completes");
        gov.Queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task RunOrDefer_WorkThrows_StillReleasesSlot()
    {
        var gov = new TestGovernance();

        var act = () => GovernedWorkRunner.RunOrDeferAsync(
            gov, WorkloadCategory.LearningAnalysis, "faulty pass",
            () => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        gov.GetActiveWorkloads().Should().BeEmpty();
    }

    [Fact]
    public async Task RunOrDefer_Refused_DefersInsteadOfDropping_AndReportsReason()
    {
        var gov = new TestGovernance { RefuseAll = true };
        var ran = false;
        string? reported = null;

        var started = await GovernedWorkRunner.RunOrDeferAsync(
            gov, WorkloadCategory.StorageScan, "storage scan",
            () => { ran = true; return Task.CompletedTask; },
            onDeferred: reason => reported = reason);

        started.Should().BeFalse();
        ran.Should().BeFalse();
        reported.Should().Be("Refused by test policy.");
        gov.Queue.Count.Should().Be(1, "refused work must be queued for retry, not dropped");
    }

    [Fact]
    public async Task RunOrDefer_Refused_DoesNotStackDuplicateEntries()
    {
        var gov = new TestGovernance { RefuseAll = true };

        await GovernedWorkRunner.RunOrDeferAsync(
            gov, WorkloadCategory.HistoricalAnalytics, "hourly downsample", () => Task.CompletedTask);
        await GovernedWorkRunner.RunOrDeferAsync(
            gov, WorkloadCategory.HistoricalAnalytics, "hourly downsample", () => Task.CompletedTask);

        gov.Queue.Count.Should().Be(1, "periodic timers must not pile up duplicate deferred entries");
    }

    [Fact]
    public async Task RunOrDefer_DeferredWork_RunsWhenQueueDrains()
    {
        var gov  = new TestGovernance { RefuseAll = true };
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await GovernedWorkRunner.RunOrDeferAsync(
            gov, WorkloadCategory.StorageScan, "storage scan",
            () => { done.TrySetResult(); return Task.CompletedTask; });

        gov.Queue.Count.Should().Be(1);

        gov.RefuseAll = false;
        gov.Queue.Drain();

        await done.Task.WaitAsync(WaitBudget);
        gov.GetActiveWorkloads().Should().BeEmpty();
        gov.Queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task RunOrDefer_RetryStillRefused_RedefersItself()
    {
        var gov = new TestGovernance { RefuseAll = true };

        await GovernedWorkRunner.RunOrDeferAsync(
            gov, WorkloadCategory.StorageScan, "storage scan", () => Task.CompletedTask);

        // Drain while still refused: the retry must put itself back on the queue.
        gov.Queue.Drain();

        gov.Queue.Count.Should().Be(1, "a retry that is refused again must re-defer, not vanish");
    }

    [Fact]
    public async Task RunOrDefer_CategoryLimitReached_RetriesAfterSlotRelease()
    {
        var gov = new TestGovernance();
        var firstStarted  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRan     = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // LearningAnalysis has a hard per-category limit of 1.
        var first = GovernedWorkRunner.RunOrDeferAsync(
            gov, WorkloadCategory.LearningAnalysis, "pass A",
            async () => { firstStarted.TrySetResult(); await releaseFirst.Task; });

        await firstStarted.Task.WaitAsync(WaitBudget);

        var second = await GovernedWorkRunner.RunOrDeferAsync(
            gov, WorkloadCategory.LearningAnalysis, "pass B",
            () => { secondRan.TrySetResult(); return Task.CompletedTask; });

        second.Should().BeFalse("the category slot is occupied by pass A");
        gov.Queue.Count.Should().Be(1);

        // Completing pass A releases the slot, which drains the queue.
        releaseFirst.TrySetResult();
        await first;

        await secondRan.Task.WaitAsync(WaitBudget);
    }

    // ── WaitForSlotAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task WaitForSlot_SlotAvailable_ReturnsTrueHoldingSlot()
    {
        var gov = new TestGovernance();

        var acquired = await GovernedWorkRunner.WaitForSlotAsync(
            gov, WorkloadCategory.StorageScan, "storage scan");

        acquired.Should().BeTrue();
        gov.GetActiveWorkloads().Should().ContainSingle(w => w.Name == "storage scan");

        gov.ReleaseSlot(WorkloadCategory.StorageScan, "storage scan");
        gov.GetActiveWorkloads().Should().BeEmpty();
    }

    [Fact]
    public async Task WaitForSlot_Refused_ResumesWhenQueueDrains()
    {
        var gov = new TestGovernance { RefuseAll = true };
        string? reported = null;

        var waiter = GovernedWorkRunner.WaitForSlotAsync(
            gov, WorkloadCategory.StorageScan, "storage scan",
            onDeferred: reason => reported = reason);

        // Give the waiter a moment to register its deferred entry.
        await WaitUntilAsync(() => gov.Queue.Count == 1);
        waiter.IsCompleted.Should().BeFalse();
        reported.Should().Be("Refused by test policy.");

        gov.RefuseAll = false;
        gov.Queue.Drain();

        (await waiter.WaitAsync(WaitBudget)).Should().BeTrue();
        gov.GetActiveWorkloads().Should().ContainSingle(w => w.Name == "storage scan");
    }

    [Fact]
    public async Task WaitForSlot_Cancelled_ThrowsAndHoldsNoSlot()
    {
        var gov = new TestGovernance { RefuseAll = true };
        using var cts = new CancellationTokenSource();

        var waiter = GovernedWorkRunner.WaitForSlotAsync(
            gov, WorkloadCategory.StorageScan, "storage scan", cts.Token);

        await WaitUntilAsync(() => gov.Queue.Count == 1);
        cts.Cancel();

        var act = () => waiter.WaitAsync(WaitBudget);

        await act.Should().ThrowAsync<OperationCanceledException>();
        gov.GetActiveWorkloads().Should().BeEmpty();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.Now + WaitBudget;
        while (!condition())
        {
            if (DateTimeOffset.Now > deadline)
                throw new TimeoutException("Condition not reached within the wait budget.");
            await Task.Delay(10);
        }
    }
}
