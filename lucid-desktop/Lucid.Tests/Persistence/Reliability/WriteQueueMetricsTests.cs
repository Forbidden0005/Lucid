using FluentAssertions;
using Lucid.Services.Persistence.Reliability;
using Xunit;

namespace Lucid.Tests.Persistence.Reliability;

public sealed class WriteQueueMetricsTests
{
    [Fact]
    public void RecordDrop_IncrementsDroppedCount()
    {
        var metrics = new WriteQueueMetrics();

        metrics.RecordDrop();
        metrics.RecordDrop();

        metrics.DroppedWriteCount.Should().Be(2);
    }

    [Fact]
    public void RecordEnqueue_IncrementsTotalCount()
    {
        var metrics = new WriteQueueMetrics();

        metrics.RecordEnqueue();
        metrics.RecordEnqueue();
        metrics.RecordEnqueue();

        metrics.TotalWriteCount.Should().Be(3);
    }

    [Fact]
    public void RecordFlush_IncrementsFlushCount()
    {
        var metrics = new WriteQueueMetrics();

        metrics.RecordFlush();

        metrics.TotalFlushCount.Should().Be(1);
    }

    [Fact]
    public void LastDroppedAt_IsNull_BeforeAnyDrop()
    {
        var metrics = new WriteQueueMetrics();

        metrics.LastDroppedAt.Should().BeNull();
    }

    [Fact]
    public void LastDroppedAt_IsSet_AfterFirstDrop()
    {
        var metrics = new WriteQueueMetrics();
        var before  = DateTime.UtcNow;

        metrics.RecordDrop();

        metrics.LastDroppedAt.Should().NotBeNull();
        metrics.LastDroppedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void DropRate_IsZero_WithNoWrites()
    {
        var metrics = new WriteQueueMetrics();

        metrics.DropRate.Should().Be(0.0);
    }

    [Fact]
    public void DropRate_IsCorrect_WithMixedEnqueuesAndDrops()
    {
        var metrics = new WriteQueueMetrics();

        // 3 enqueued, 1 dropped → drop rate = 1/3 ≈ 0.333
        metrics.RecordEnqueue();
        metrics.RecordEnqueue();
        metrics.RecordEnqueue();
        metrics.RecordDrop();

        metrics.DropRate.Should().BeApproximately(1.0 / 3.0, precision: 0.001);
    }

    [Fact]
    public void Metrics_AreThreadSafe_UnderConcurrentAccess()
    {
        var metrics = new WriteQueueMetrics();
        var tasks   = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    metrics.RecordEnqueue();
                    metrics.RecordDrop();
                }
            }));

        Task.WaitAll(tasks.ToArray());

        metrics.TotalWriteCount.Should().Be(1000);
        metrics.DroppedWriteCount.Should().Be(1000);
    }
}
