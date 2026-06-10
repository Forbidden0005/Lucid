using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Lucid.Services.Repair;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Covers the DISM repair contract through a fake runtime so the tests do not
/// touch the live component store or Windows Update.
/// </summary>
public sealed class DismRestoreHealthExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_DryRun_DescribesRequirementsAndCommand()
    {
        var executor = new DismRestoreHealthExecutor(new TestRuntime());

        var result = await executor.ExecuteAsync(NewContext(isDryRun: true));

        result.ActionId.Should().Be("action.repair.dism-restore-health");
        result.Status.Should().Be(ActionExecutionStatus.DryRunSuccess);
        result.Message.Should().Contain("scan and repair the Windows component store");
        result.Log.Should().Contain(entry =>
            entry.Message.Contains("Active internet connection", StringComparison.OrdinalIgnoreCase));
        result.Log.Should().Contain(entry =>
            entry.Message.Contains("DISM.exe /Online /Cleanup-Image /RestoreHealth", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenOutputShowsSuccessfulRestore_ReturnsSuccess()
    {
        var runtime = new TestRuntime
        {
            Result = new ProcessExecutionHelper.RunResult(0, false, 1),
            OutputLines =
            {
                "[=====     ] 45.0%",
                "The restore operation completed successfully."
            }
        };
        var executor = new DismRestoreHealthExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Success);
        result.Message.Should().Contain("Run SFC /scannow next");
        result.Log.Should().Contain(entry => entry.Message.Contains("Progress: 45%"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenSourceFilesNotFound_ReturnsFailed()
    {
        var runtime = new TestRuntime
        {
            Result = new ProcessExecutionHelper.RunResult(2, false, 1),
            OutputLines =
            {
                "The source files could not be found."
            }
        };
        var executor = new DismRestoreHealthExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.Message.Should().Contain("could not download repair files");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ReturnsCancelled()
    {
        var runtime = new TestRuntime
        {
            Result = new ProcessExecutionHelper.RunResult(-1, true, 0)
        };
        var executor = new DismRestoreHealthExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Cancelled);
        result.Message.Should().Contain("partial state");
    }

    [Fact]
    public async Task RollbackAsync_AlwaysFailsBecauseRepairIsNotReversible()
    {
        var executor = new DismRestoreHealthExecutor(new TestRuntime());

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

    private sealed class TestRuntime : DismRestoreHealthExecutor.IDismRepairRuntime
    {
        public ProcessExecutionHelper.RunResult Result { get; set; } = new(0, false, 0);
        public List<string> OutputLines { get; } = [];

        public ProcessExecutionHelper.RunResult Run(
            string exe,
            string arguments,
            ActionExecutionLog log,
            CancellationToken ct,
            IList<string> outputLines,
            Action<float> onProgress)
        {
            foreach (var line in OutputLines)
            {
                outputLines.Add(line);
                if (CommandOutputParser.TryParseDismProgress(line, out var pct))
                    onProgress(pct);
            }

            return Result;
        }
    }
}
