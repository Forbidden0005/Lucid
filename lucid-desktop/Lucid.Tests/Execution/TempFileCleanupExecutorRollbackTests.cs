using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Covers the executor-specific rollback path for temp cleanup without touching
/// the live system temp directories. This verifies that the rollback contract
/// stays stable for one of Lucid's richer cleanup workflows.
/// </summary>
public sealed class TempFileCleanupExecutorRollbackTests
{
    [Fact]
    public async Task RollbackAsync_MissingManifest_FailsCleanly()
    {
        using var dir = new TestDir();
        var executor = new TempFileCleanupExecutor();

        var result = await executor.RollbackAsync(dir.StagingPath, NewContext());

        result.ActionId.Should().Be("action.disk.clean-temp-files");
        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.Message.Should().Contain("Rollback manifest is missing");
    }

    [Fact]
    public async Task RollbackAsync_RestoresFiles_AndDeletesStagingOnSuccess()
    {
        using var dir = new TestDir();
        var executor = new TempFileCleanupExecutor();
        var originalFile = dir.TargetPath("temp-a.tmp");
        dir.WriteStagedFile("one.tmp", new byte[] { 1, 3, 5, 7 });
        dir.WriteManifest("one.tmp|" + originalFile);

        var result = await executor.RollbackAsync(dir.StagingPath, NewContext());

        result.ActionId.Should().Be("action.disk.clean-temp-files");
        result.Status.Should().Be(ActionExecutionStatus.Success);
        File.Exists(originalFile).Should().BeTrue();
        File.ReadAllBytes(originalFile).Should().Equal(new byte[] { 1, 3, 5, 7 });
        Directory.Exists(dir.StagingPath).Should().BeFalse(
            "successful rollback should clean up staging");
    }

    [Fact]
    public async Task RollbackAsync_CancelledMidRun_ReturnsCancelledAndPreservesStaging()
    {
        using var dir = new TestDir();
        var executor = new TempFileCleanupExecutor();
        dir.WriteStagedFile("first.tmp", new byte[] { 1 });
        dir.WriteStagedFile("second.tmp", new byte[] { 2 });
        dir.WriteManifest(
            "first.tmp|" + dir.TargetPath("first.tmp"),
            "second.tmp|" + dir.TargetPath("second.tmp"));

        using var cts = new CancellationTokenSource();
        var context = NewContext(entry =>
        {
            if (entry.Message.Contains("Restored", StringComparison.OrdinalIgnoreCase))
                cts.Cancel();
        });

        var result = await executor.RollbackAsync(dir.StagingPath, context, cts.Token);

        result.ActionId.Should().Be("action.disk.clean-temp-files");
        result.Status.Should().Be(ActionExecutionStatus.Cancelled);
        File.Exists(dir.TargetPath("first.tmp")).Should().BeTrue(
            "at least one file should have been restored before cancellation");
        Directory.Exists(dir.StagingPath).Should().BeTrue(
            "cancelled rollback should preserve staging for retry");
        File.Exists(Path.Combine(dir.StagingPath, "second.tmp")).Should().BeTrue(
            "unrestored staged data must remain available");
    }

    private static ActionExecutionContext NewContext(Action<ActionLogEntry>? onEntry = null) => new()
    {
        IsDryRun = false,
        IsElevated = true,
        ConfirmationGranted = true,
        RequestedBy = "test",
        Log = onEntry is null ? new ActionExecutionLog() : new ActionExecutionLog(onEntry),
    };

    private sealed class TestDir : IDisposable
    {
        public TestDir()
        {
            RootPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "lucid-temp-cleanup-rollback-tests",
                Guid.NewGuid().ToString("N"));
            StagingPath = System.IO.Path.Combine(RootPath, "staging");
            RestoredRoot = System.IO.Path.Combine(RootPath, "restored");
            Directory.CreateDirectory(StagingPath);
            Directory.CreateDirectory(RestoredRoot);
        }

        public string RootPath { get; }
        public string StagingPath { get; }
        public string RestoredRoot { get; }

        public string TargetPath(string fileName) => System.IO.Path.Combine(RestoredRoot, fileName);

        public void WriteStagedFile(string fileName, byte[] bytes)
        {
            File.WriteAllBytes(System.IO.Path.Combine(StagingPath, fileName), bytes);
        }

        public void WriteManifest(params string[] lines)
        {
            File.WriteAllLines(System.IO.Path.Combine(StagingPath, ".manifest"), lines);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                    Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
            }
        }
    }
}
