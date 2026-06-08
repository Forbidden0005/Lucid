using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Covers the rollback contract for the Windows Update cache executor without
/// touching the live SoftwareDistribution directory or Windows service state.
/// Live cleanup still needs a service-control seam before it can be tested
/// honestly in unit tests.
/// </summary>
public sealed class WindowsUpdateCacheExecutorRollbackTests
{
    [Fact]
    public async Task RollbackAsync_MissingManifest_FailsCleanly()
    {
        using var dir = new TestDir();
        var executor = new WindowsUpdateCacheExecutor();

        var result = await executor.RollbackAsync(dir.StagingPath, NewContext());

        result.ActionId.Should().Be("action.disk.clean-windows-update-cache");
        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.Message.Should().Contain("Rollback manifest is missing");
    }

    [Fact]
    public async Task RollbackAsync_RestoresFiles_AndDeletesStagingOnSuccess()
    {
        using var dir = new TestDir();
        var executor = new WindowsUpdateCacheExecutor();
        var originalFile = dir.TargetPath("download-a.bin");
        dir.WriteStagedFile("one.bin", new byte[] { 1, 4, 9 });
        dir.WriteManifest("one.bin|" + originalFile);

        var result = await executor.RollbackAsync(dir.StagingPath, NewContext());

        result.ActionId.Should().Be("action.disk.clean-windows-update-cache");
        result.Status.Should().Be(ActionExecutionStatus.Success);
        File.Exists(originalFile).Should().BeTrue();
        File.ReadAllBytes(originalFile).Should().Equal(new byte[] { 1, 4, 9 });
        Directory.Exists(dir.StagingPath).Should().BeFalse(
            "successful rollback should clean up staging");
    }

    [Fact]
    public async Task RollbackAsync_CancelledMidRun_ReturnsCancelledAndPreservesStaging()
    {
        using var dir = new TestDir();
        var executor = new WindowsUpdateCacheExecutor();
        dir.WriteStagedFile("first.bin", new byte[] { 1 });
        dir.WriteStagedFile("second.bin", new byte[] { 2 });
        dir.WriteManifest(
            "first.bin|" + dir.TargetPath("first.bin"),
            "second.bin|" + dir.TargetPath("second.bin"));

        using var cts = new CancellationTokenSource();
        var context = NewContext(entry =>
        {
            if (entry.Message.Contains("Restored", StringComparison.OrdinalIgnoreCase))
                cts.Cancel();
        });

        var result = await executor.RollbackAsync(dir.StagingPath, context, cts.Token);

        result.ActionId.Should().Be("action.disk.clean-windows-update-cache");
        result.Status.Should().Be(ActionExecutionStatus.Cancelled);
        File.Exists(dir.TargetPath("first.bin")).Should().BeTrue(
            "at least one file should have been restored before cancellation");
        Directory.Exists(dir.StagingPath).Should().BeTrue(
            "cancelled rollback should preserve staging for retry");
        File.Exists(Path.Combine(dir.StagingPath, "second.bin")).Should().BeTrue(
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
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "lucid-windows-update-cache-rollback-tests",
                Guid.NewGuid().ToString("N"));
            StagingPath = Path.Combine(RootPath, "staging");
            RestoredRoot = Path.Combine(RootPath, "restored");
            Directory.CreateDirectory(StagingPath);
            Directory.CreateDirectory(RestoredRoot);
        }

        public string RootPath { get; }
        public string StagingPath { get; }
        public string RestoredRoot { get; }

        public string TargetPath(string fileName) => Path.Combine(RestoredRoot, fileName);

        public void WriteStagedFile(string fileName, byte[] bytes)
        {
            File.WriteAllBytes(Path.Combine(StagingPath, fileName), bytes);
        }

        public void WriteManifest(params string[] lines)
        {
            File.WriteAllLines(Path.Combine(StagingPath, ".manifest"), lines);
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
