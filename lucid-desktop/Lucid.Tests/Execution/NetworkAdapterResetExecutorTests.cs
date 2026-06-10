using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Lucid.Services.Repair;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Covers the multi-step network reset contract through a fake runtime so the
/// tests do not touch the real TCP/IP stack.
/// </summary>
public sealed class NetworkAdapterResetExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_DryRun_ListsAllResetSteps()
    {
        var executor = new NetworkAdapterResetExecutor(new TestRuntime());

        var result = await executor.ExecuteAsync(NewContext(isDryRun: true));

        result.ActionId.Should().Be("action.network.reset-adapter");
        result.Status.Should().Be(ActionExecutionStatus.DryRunSuccess);
        result.Message.Should().Contain("4-step TCP/IP stack reset");
        result.Log.Should().Contain(entry => entry.Message.Contains("Reset IP stack"));
        result.Log.Should().Contain(entry => entry.Message.Contains("Reset TCP stack"));
        result.Log.Should().Contain(entry => entry.Message.Contains("Release IP lease"));
        result.Log.Should().Contain(entry => entry.Message.Contains("Renew IP lease"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenOnlyNonFatalStepsFail_ReturnsSuccess()
    {
        var runtime = new TestRuntime();
        runtime.EnqueueSuccess();
        runtime.EnqueueSuccess();
        runtime.EnqueueFailure();
        runtime.EnqueueFailure();
        var executor = new NetworkAdapterResetExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Success);
        result.Message.Should().Contain("4/4 steps");
        runtime.Commands.Should().ContainInOrder(
            "netsh.exe int ip reset",
            "netsh.exe int tcp reset",
            "ipconfig.exe /release",
            "ipconfig.exe /renew");
    }

    [Fact]
    public async Task ExecuteAsync_WhenOneFatalStepFails_ReturnsPartialSuccess()
    {
        var runtime = new TestRuntime();
        runtime.EnqueueSuccess();
        runtime.EnqueueFailure();
        runtime.EnqueueFailure();
        runtime.EnqueueFailure();
        var executor = new NetworkAdapterResetExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.PartialSuccess);
        result.Message.Should().Contain("3 OK, 1 failed");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledDuringStep_ReturnsCancelledAndStopsSequence()
    {
        var runtime = new TestRuntime();
        runtime.EnqueueSuccess();
        runtime.EnqueueCancelled();
        runtime.EnqueueSuccess();
        var executor = new NetworkAdapterResetExecutor(runtime);
        using var cts = new CancellationTokenSource();

        var result = await executor.ExecuteAsync(NewContext(), cts.Token);

        result.Status.Should().Be(ActionExecutionStatus.Cancelled);
        result.Message.Should().Contain("during step 2 of 4");
        runtime.Commands.Should().ContainInOrder(
            "netsh.exe int ip reset",
            "netsh.exe int tcp reset");
        runtime.Commands.Should().HaveCount(2,
            "later steps must not run after a cancelled step");
    }

    [Fact]
    public async Task RollbackAsync_AlwaysFailsBecauseResetIsNotReversible()
    {
        var executor = new NetworkAdapterResetExecutor(new TestRuntime());

        var result = await executor.RollbackAsync("unused", NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.Message.Should().Contain("does not support rollback");
    }

    private static ActionExecutionContext NewContext(bool isDryRun = false) => new()
    {
        IsDryRun = isDryRun,
        IsElevated = true,
        ConfirmationGranted = true,
        RequestedBy = "test",
        Log = new ActionExecutionLog(),
    };

    private sealed class TestRuntime : NetworkAdapterResetExecutor.INetworkRepairRuntime
    {
        private readonly Queue<ProcessExecutionHelper.RunResult> _results = new();

        public List<string> Commands { get; } = [];

        public void EnqueueSuccess() =>
            _results.Enqueue(new ProcessExecutionHelper.RunResult(0, false, 0));

        public void EnqueueFailure(int exitCode = 1) =>
            _results.Enqueue(new ProcessExecutionHelper.RunResult(exitCode, false, 0));

        public void EnqueueCancelled() =>
            _results.Enqueue(new ProcessExecutionHelper.RunResult(-1, true, 0));

        public ProcessExecutionHelper.RunResult Run(
            string exe,
            string arguments,
            ActionExecutionLog log,
            CancellationToken ct)
        {
            Commands.Add($"{exe} {arguments}");

            if (_results.Count == 0)
                throw new InvalidOperationException("No queued command result for test runtime.");

            return _results.Dequeue();
        }
    }
}
