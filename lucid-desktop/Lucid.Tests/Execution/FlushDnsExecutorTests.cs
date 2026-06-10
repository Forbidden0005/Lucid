using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Lucid.Services.Repair;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Covers the DNS flush contract through a fake runtime so the tests do not
/// touch the live resolver cache.
/// </summary>
public sealed class FlushDnsExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_DryRun_DescribesCommandAndReversibility()
    {
        var executor = new FlushDnsExecutor(new TestRuntime());

        var result = await executor.ExecuteAsync(NewContext(isDryRun: true));

        result.ActionId.Should().Be("action.network.flush-dns");
        result.Status.Should().Be(ActionExecutionStatus.DryRunSuccess);
        result.Message.Should().Contain("clear the Windows DNS resolver cache");
        result.Log.Should().Contain(entry =>
            entry.Message.Contains("ipconfig.exe /flushdns", StringComparison.OrdinalIgnoreCase));
        result.Log.Should().Contain(entry =>
            entry.Message.Contains("rebuilds automatically", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenOutputConfirmsFlush_ReturnsSuccess()
    {
        var runtime = new TestRuntime
        {
            Result = new ProcessExecutionHelper.RunResult(1, false, 1),
            OutputLines =
            {
                "Successfully flushed the DNS Resolver Cache."
            }
        };
        var executor = new FlushDnsExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Success);
        result.Message.Should().Contain("Stale entries have been removed");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ReturnsCancelled()
    {
        var runtime = new TestRuntime
        {
            Result = new ProcessExecutionHelper.RunResult(-1, true, 0)
        };
        var executor = new FlushDnsExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Cancelled);
        result.Message.Should().Contain("cancelled");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandFailsWithoutSuccessOutput_ReturnsFailed()
    {
        var runtime = new TestRuntime
        {
            Result = new ProcessExecutionHelper.RunResult(5, false, 0)
        };
        var executor = new FlushDnsExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.Message.Should().Contain("exited with code 5");
    }

    [Fact]
    public async Task RollbackAsync_ReturnsSuccessBecauseCacheSelfHeals()
    {
        var executor = new FlushDnsExecutor(new TestRuntime());

        var result = await executor.RollbackAsync("unused", NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Success);
        result.Message.Should().Contain("No rollback needed");
    }

    private static ActionExecutionContext NewContext(bool isDryRun = false) => new()
    {
        IsDryRun = isDryRun,
        IsElevated = true,
        ConfirmationGranted = true,
        RequestedBy = "test",
        Log = new ActionExecutionLog(),
    };

    private sealed class TestRuntime : FlushDnsExecutor.IFlushDnsRuntime
    {
        public ProcessExecutionHelper.RunResult Result { get; set; } = new(0, false, 0);
        public List<string> OutputLines { get; } = [];

        public ProcessExecutionHelper.RunResult Run(
            string exe,
            string arguments,
            ActionExecutionLog log,
            CancellationToken ct,
            IList<string> outputLines)
        {
            foreach (var line in OutputLines)
                outputLines.Add(line);

            return Result;
        }
    }
}
