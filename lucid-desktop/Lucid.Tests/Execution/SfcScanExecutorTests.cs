using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Lucid.Services.Repair;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Covers the SFC repair contract through a fake runtime so the tests do not
/// touch the live system file checker.
/// </summary>
public sealed class SfcScanExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_DryRun_DescribesCommandAndLogPath()
    {
        var executor = new SfcScanExecutor(new TestRuntime());

        var result = await executor.ExecuteAsync(NewContext(isDryRun: true));

        result.ActionId.Should().Be("action.repair.sfc-scannow");
        result.Status.Should().Be(ActionExecutionStatus.DryRunSuccess);
        result.Message.Should().Contain("scan and repair Windows system files");
        result.Log.Should().Contain(entry =>
            entry.Message.Contains("sfc.exe /scannow", StringComparison.OrdinalIgnoreCase));
        result.Log.Should().Contain(entry =>
            entry.Message.Contains("%SystemRoot%\\Logs\\CBS\\CBS.log", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenOutputShowsRepairedFiles_ReturnsSuccess()
    {
        var runtime = new TestRuntime
        {
            Result = new ProcessExecutionHelper.RunResult(0, false, 1),
            OutputLines =
            {
                "Verification 40 percent complete.",
                "Windows Resource Protection found corrupt files and successfully repaired them."
            }
        };
        var executor = new SfcScanExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Success);
        result.Message.Should().Contain("successfully repaired");
        result.Log.Should().Contain(entry => entry.Message.Contains("Scan progress: 40%"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenOutputShowsUnrepairedCorruption_ReturnsPartialSuccess()
    {
        var runtime = new TestRuntime
        {
            Result = new ProcessExecutionHelper.RunResult(1, false, 1),
            OutputLines =
            {
                "Windows Resource Protection found corrupt files but was unable to fix some of them."
            }
        };
        var executor = new SfcScanExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.PartialSuccess);
        result.Message.Should().Contain("Run DISM /RestoreHealth");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ReturnsCancelled()
    {
        var runtime = new TestRuntime
        {
            Result = new ProcessExecutionHelper.RunResult(-1, true, 0)
        };
        var executor = new SfcScanExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Cancelled);
        result.Message.Should().Contain("did not complete");
    }

    [Fact]
    public async Task RollbackAsync_AlwaysFailsBecauseRepairIsNotReversible()
    {
        var executor = new SfcScanExecutor(new TestRuntime());

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

    private sealed class TestRuntime : SfcScanExecutor.ISfcScanRuntime
    {
        public ProcessExecutionHelper.RunResult Result { get; set; } = new(0, false, 0);
        public List<string> OutputLines { get; } = [];

        public ProcessExecutionHelper.RunResult Run(
            string exe,
            string arguments,
            ActionExecutionLog log,
            CancellationToken ct,
            IList<string> outputLines,
            Action<int> onProgress)
        {
            foreach (var line in OutputLines)
            {
                outputLines.Add(line);
                if (CommandOutputParser.TryParseSfcProgress(line, out var pct))
                    onProgress(pct);
            }

            return Result;
        }
    }
}
