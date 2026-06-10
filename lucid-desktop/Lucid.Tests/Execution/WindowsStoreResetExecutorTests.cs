using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Covers the Windows Store reset contract through a fake runtime so the tests
/// do not launch wsreset.exe or the Store UI.
/// </summary>
public sealed class WindowsStoreResetExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_DryRun_DescribesStoreRelaunchSideEffect()
    {
        var executor = new WindowsStoreResetExecutor(new TestRuntime());

        var result = await executor.ExecuteAsync(NewContext(isDryRun: true));

        result.ActionId.Should().Be("action.apps.reset-windows-store");
        result.Status.Should().Be(ActionExecutionStatus.DryRunSuccess);
        result.Message.Should().Contain("re-launch the Store");
        result.Log.Should().Contain(entry =>
            entry.Message.Contains("The Windows Store will open automatically", StringComparison.OrdinalIgnoreCase));
        result.Log.Should().Contain(entry =>
            entry.Message.Contains("wsreset.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenWsresetSucceeds_ReturnsSuccess()
    {
        var runtime = new TestRuntime
        {
            Result = new WindowsStoreResetExecutor.WindowsStoreResetRunResult(
                Exited: true,
                WasCancelled: false,
                ExitCode: 0)
        };
        var executor = new WindowsStoreResetExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Success);
        result.Message.Should().Contain("Store will rebuild it on next use");
    }

    [Fact]
    public async Task ExecuteAsync_WhenWsresetTimesOut_ReturnsPartialSuccessWithTimeoutMessage()
    {
        var runtime = new TestRuntime
        {
            Result = new WindowsStoreResetExecutor.WindowsStoreResetRunResult(
                Exited: false,
                WasCancelled: false,
                ExitCode: -1)
        };
        var executor = new WindowsStoreResetExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.PartialSuccess);
        result.Message.Should().Contain("did not finish within 60 seconds");
        result.Log.Should().Contain(entry =>
            entry.Message.Contains("did not exit within 60 seconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ReturnsCancelled()
    {
        var runtime = new TestRuntime
        {
            Result = new WindowsStoreResetExecutor.WindowsStoreResetRunResult(
                Exited: false,
                WasCancelled: true,
                ExitCode: -1)
        };
        var executor = new WindowsStoreResetExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Cancelled);
        result.Message.Should().Contain("cancelled");
    }

    [Fact]
    public async Task ExecuteAsync_WhenLaunchFails_ReturnsFailed()
    {
        var executor = new WindowsStoreResetExecutor(new ThrowingRuntime("boom"));

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.Message.Should().Contain("Could not start wsreset");
        result.ErrorDetail.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task RollbackAsync_ReturnsSuccessBecauseCacheSelfHeals()
    {
        var executor = new WindowsStoreResetExecutor(new TestRuntime());

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

    private sealed class TestRuntime : WindowsStoreResetExecutor.IWindowsStoreResetRuntime
    {
        public WindowsStoreResetExecutor.WindowsStoreResetRunResult Result { get; set; } =
            new(Exited: true, WasCancelled: false, ExitCode: 0);

        public WindowsStoreResetExecutor.WindowsStoreResetRunResult Run(CancellationToken ct) => Result;
    }

    private sealed class ThrowingRuntime(string message) : WindowsStoreResetExecutor.IWindowsStoreResetRuntime
    {
        public WindowsStoreResetExecutor.WindowsStoreResetRunResult Run(CancellationToken ct) =>
            throw new InvalidOperationException(message);
    }
}
