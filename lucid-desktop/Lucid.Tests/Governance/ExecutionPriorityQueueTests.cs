using FluentAssertions;
using Lucid.Services.Governance;
using Xunit;

namespace Lucid.Tests.Governance;

/// <summary>
/// Proves the deferred-workload queue mechanism works as documented: priority
/// ordering on drain, FIFO within a class, stale-entry expiry, and callback
/// fault isolation.
///
/// NOTE (governance audit 2026-07-25): production code never calls Enqueue —
/// the deferral/retry path is currently a dead mechanism. These tests prove
/// the mechanism is sound so adoption work can wire it up with confidence;
/// they do not imply the runtime exercises it today.
/// </summary>
public sealed class ExecutionPriorityQueueTests
{
    private static DeferredWorkloadEntry Entry(
        string           name,
        WorkloadPriority priority   = WorkloadPriority.Background,
        TimeSpan?        age        = null,
        WorkloadCategory category   = WorkloadCategory.TelemetrySampling)
        => new(
            Name:           name,
            Category:       category,
            Priority:       priority,
            DeferredAt:     DateTimeOffset.Now - (age ?? TimeSpan.Zero),
            DeferralReason: "test deferral");

    // ── Enqueue guards / Count ────────────────────────────────────────────────

    [Fact]
    public void Enqueue_NullEntry_Throws()
    {
        var queue = new ExecutionPriorityQueue();

        var act = () => queue.Enqueue(null!, () => Task.CompletedTask);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Enqueue_NullCallback_Throws()
    {
        var queue = new ExecutionPriorityQueue();

        var act = () => queue.Enqueue(Entry("w"), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Count_TracksEnqueuedEntries()
    {
        var queue = new ExecutionPriorityQueue();
        queue.Count.Should().Be(0);

        queue.Enqueue(Entry("a"), () => Task.CompletedTask);
        queue.Enqueue(Entry("b"), () => Task.CompletedTask);

        queue.Count.Should().Be(2);
    }

    // ── Drain ordering ────────────────────────────────────────────────────────

    [Fact]
    public void Drain_InvokesCallbacksInPriorityOrder_ForegroundFirst()
    {
        var queue = new ExecutionPriorityQueue();
        var order = new List<string>();
        Func<string, Func<Task>> record = name => () => { order.Add(name); return Task.CompletedTask; };

        // Enqueue lowest-priority first to prove ordering is not insertion order.
        queue.Enqueue(Entry("idle", WorkloadPriority.IdleOnly,   age: TimeSpan.FromMinutes(3)), record("idle"));
        queue.Enqueue(Entry("bg",   WorkloadPriority.Background, age: TimeSpan.FromMinutes(2)), record("bg"));
        queue.Enqueue(Entry("fg",   WorkloadPriority.Foreground, age: TimeSpan.FromMinutes(1)), record("fg"));

        queue.Drain();

        order.Should().Equal("fg", "bg", "idle");
        queue.Count.Should().Be(0, "drain empties the queue");
    }

    [Fact]
    public void Drain_IsFifoWithinAPriorityClass()
    {
        var queue = new ExecutionPriorityQueue();
        var order = new List<string>();
        Func<string, Func<Task>> record = name => () => { order.Add(name); return Task.CompletedTask; };

        // DeferredAt is caller-supplied — older entries drain first within a class.
        queue.Enqueue(Entry("second", age: TimeSpan.FromMinutes(5)),  record("second"));
        queue.Enqueue(Entry("first",  age: TimeSpan.FromMinutes(10)), record("first"));

        queue.Drain();

        order.Should().Equal("first", "second");
    }

    [Fact]
    public void Drain_EmptyQueue_IsANoOp()
    {
        var queue = new ExecutionPriorityQueue();

        var act = () => queue.Drain();

        act.Should().NotThrow();
        queue.Count.Should().Be(0);
    }

    // ── Expiry ────────────────────────────────────────────────────────────────

    [Fact]
    public void Drain_DropsEntriesOlderThanThirtyMinutes_WithoutInvokingThem()
    {
        var queue = new ExecutionPriorityQueue();
        var invoked = new List<string>();
        Func<string, Func<Task>> record = name => () => { invoked.Add(name); return Task.CompletedTask; };

        queue.Enqueue(Entry("stale", age: TimeSpan.FromMinutes(31)), record("stale"));
        queue.Enqueue(Entry("fresh", age: TimeSpan.FromMinutes(29)), record("fresh"));

        queue.Drain();

        invoked.Should().Equal("fresh");
        queue.Count.Should().Be(0, "expired entries are discarded, not retained");
    }

    // ── Fault isolation ───────────────────────────────────────────────────────

    [Fact]
    public void Drain_ThrowingCallback_DoesNotPreventLaterCallbacks()
    {
        var queue = new ExecutionPriorityQueue();
        var invoked = new List<string>();

        queue.Enqueue(
            Entry("broken", WorkloadPriority.Foreground),
            () => throw new InvalidOperationException("simulated retry failure"));
        queue.Enqueue(
            Entry("healthy", WorkloadPriority.Background),
            () => { invoked.Add("healthy"); return Task.CompletedTask; });

        var act = () => queue.Drain();

        act.Should().NotThrow();
        invoked.Should().Equal("healthy");
    }

    // ── Snapshot / Clear ──────────────────────────────────────────────────────

    [Fact]
    public void GetSnapshot_ReturnsEntriesOrderedByPriorityThenDeferralTime()
    {
        var queue = new ExecutionPriorityQueue();
        queue.Enqueue(Entry("idle", WorkloadPriority.IdleOnly),                                  () => Task.CompletedTask);
        queue.Enqueue(Entry("fg-late",  WorkloadPriority.Foreground, age: TimeSpan.FromMinutes(1)), () => Task.CompletedTask);
        queue.Enqueue(Entry("fg-early", WorkloadPriority.Foreground, age: TimeSpan.FromMinutes(2)), () => Task.CompletedTask);

        var snapshot = queue.GetSnapshot();

        snapshot.Select(e => e.Name).Should().Equal("fg-early", "fg-late", "idle");
        queue.Count.Should().Be(3, "snapshotting does not drain");
    }

    [Fact]
    public void Clear_DiscardsEverythingWithoutInvokingCallbacks()
    {
        var queue = new ExecutionPriorityQueue();
        var invoked = false;
        queue.Enqueue(Entry("w"), () => { invoked = true; return Task.CompletedTask; });

        queue.Clear();

        queue.Count.Should().Be(0);
        invoked.Should().BeFalse();
        queue.Drain(); // nothing left to invoke
        invoked.Should().BeFalse();
    }
}
