using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Lucid.Services.Repair;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Covers the Winsock reset contract through a fake runtime so the tests do
/// not touch the live network stack.
/// </summary>
public sealed class WinsockResetExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_DryRun_EmphasizesRestartRequirement()
    {
        var executor = new WinsockResetExecutor(new TestRuntime());

        var result = await executor.ExecuteAsync(NewContext(isDryRun: true));

        result.ActionId.Should().Be("action.network.winsock-reset");
        result.Status.Should().Be(ActionExecutionStatus.DryRunSuccess);
        result.Message.Should().Contain("restart required");
        result.Log.Should().Contain(entry =>
            entry.Message.Contains("RESTART REQUIRED", StringComparison.OrdinalIgnoreCase));
        result.Log.Should().Contain(entry =>
            entry.Message.Contains("netsh.exe winsock reset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenOutputConfirmsReset_ReturnsSuccessAndRestartWarning()
    {
        var runtime = new TestRuntime
        {
            Result = new ProcessExecutionHelper.RunResult(1, false, 1),
            OutputLines = { "Successfully reset the Winsock Catalog." }
        };
        var executor = new WinsockResetExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Success);
        result.Message.Should().Contain("Restart Windows now");
        result.Log.Should().Contain(entry =>
            entry.Message.Contains("Please restart Windows before using the network."));
        runtime.Commands.Should().ContainSingle("netsh.exe winsock reset");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ReturnsCancelled()
    {
        var runtime = new TestRuntime
        {
            Result = new ProcessExecutionHelper.RunResult(-1, true, 0)
        };
        var executor = new WinsockResetExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Cancelled);
        result.Message.Should().Contain("cancelled");
    }

    [Fact]
    public async Task ExecuteAsync_WhenResetFails_ReturnsFailed()
    {
        var runtime = new TestRuntime
        {
            Result = new ProcessExecutionHelper.RunResult(5, false, 0)
        };
        var executor = new WinsockResetExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.Message.Should().Contain("exited with code 5");
    }

    [Fact]
    public async Task RollbackAsync_AlwaysFailsBecauseResetIsNotReversible()
    {
        var executor = new WinsockResetExecutor(new TestRuntime());

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

    private sealed class TestRuntime : WinsockResetExecutor.IWinsockResetRuntime
    {
        public ProcessExecutionHelper.RunResult Result { get; set; } =
            new(0, false, 0);

        public List<string> OutputLines { get; } = [];
        public List<string> Commands { get; } = [];

        public ProcessExecutionHelper.RunResult Run(
            string exe,
            string arguments,
            ActionExecutionLog log,
            CancellationToken ct,
            IList<string> outputLines)
        {
            Commands.Add($"{exe} {arguments}");
            foreach (var line in OutputLines)
            {
                outputLines.Add(line);
            }

            return Result;
        }
    }
}
