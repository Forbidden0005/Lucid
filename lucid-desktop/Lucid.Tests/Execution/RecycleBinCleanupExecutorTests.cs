using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Covers the recycle-bin cleanup contract through a fake shell runtime so the
/// tests do not touch the real user recycle bin.
/// </summary>
public sealed class RecycleBinCleanupExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_DryRun_ReportsPreviewFromQuery()
    {
        var runtime = new TestRuntime
        {
            QueryResult = (0, 1_536, 3)
        };
        var executor = new RecycleBinCleanupExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext(isDryRun: true));

        result.ActionId.Should().Be("action.disk.empty-recycle-bin");
        result.Status.Should().Be(ActionExecutionStatus.DryRunSuccess);
        result.Message.Should().Contain("3 item(s)");
        result.Message.Should().Contain("1.5 KB");
        runtime.QueryCalls.Should().Be(1);
        runtime.EmptyCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyEmpty_ReturnsSuccessWithoutEmptyCall()
    {
        var runtime = new TestRuntime
        {
            QueryResult = (0, 0, 0)
        };
        var executor = new RecycleBinCleanupExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Success);
        result.Message.Should().Contain("already empty");
        runtime.QueryCalls.Should().Be(1);
        runtime.EmptyCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ToleratesShellUnexpectedResult_AsSuccess()
    {
        var runtime = new TestRuntime
        {
            QueryResult = (0, 2_048, 4),
            EmptyResult = unchecked((int)0x8000FFFF)
        };
        var executor = new RecycleBinCleanupExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Success);
        result.Message.Should().Contain("4 item(s)");
        runtime.QueryCalls.Should().Be(1);
        runtime.EmptyCalls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenShellEmptyFails_ReturnsFailed()
    {
        var runtime = new TestRuntime
        {
            QueryResult = (0, 512, 1),
            EmptyResult = unchecked((int)0x80070005)
        };
        var executor = new RecycleBinCleanupExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.Message.Should().Contain("0x80070005");
    }

    [Fact]
    public async Task RollbackAsync_AlwaysFailsBecauseActionIsNotReversible()
    {
        var executor = new RecycleBinCleanupExecutor(new TestRuntime());

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

    private sealed class TestRuntime : RecycleBinCleanupExecutor.IRecycleBinRuntime
    {
        public (int HResult, long SizeBytes, long ItemCount) QueryResult { get; set; }
        public int EmptyResult { get; set; }
        public int QueryCalls { get; private set; }
        public int EmptyCalls { get; private set; }

        public (int HResult, long SizeBytes, long ItemCount) Query()
        {
            QueryCalls++;
            return QueryResult;
        }

        public int Empty(uint flags)
        {
            EmptyCalls++;
            return EmptyResult;
        }
    }
}
