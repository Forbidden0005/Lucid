using FluentAssertions;
using Lucid.Services.Cleanup;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Covers live cleanup behavior for the Windows Update cache executor using a
/// temp directory and a fake service/runtime seam instead of touching the real
/// SoftwareDistribution cache or Windows service state.
/// </summary>
public sealed class WindowsUpdateCacheExecutorSafetyTests
{
    [Fact]
    public async Task ExecuteAsync_WhenStopIsNotConfirmed_StillCleansAndRestartsService()
    {
        using var dir = new TestDir();
        dir.WriteCacheFile("update-a.bin", new byte[] { 1, 2, 3, 4 });
        var runtime = new TestRuntime(dir.CachePath, dir.StagingPath)
        {
            StopServiceResult = false
        };
        var executor = new WindowsUpdateCacheExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.ActionId.Should().Be("action.disk.clean-windows-update-cache");
        result.Status.Should().Be(ActionExecutionStatus.Success);
        result.CanRollback.Should().BeTrue();
        runtime.StopServiceCalls.Should().Be(1);
        runtime.StartServiceCalls.Should().Be(1);
        Directory.GetFiles(dir.CachePath, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenOneMoveFails_ReturnsPartialSuccessAndStillRestartsService()
    {
        using var dir = new TestDir();
        dir.WriteCacheFile("good.bin", new byte[] { 1, 2, 3 });
        dir.WriteCacheFile("locked.bin", new byte[] { 4, 5, 6 });
        var runtime = new TestRuntime(dir.CachePath, dir.StagingPath);
        runtime.FailMoveFileNames.Add("locked.bin");
        var executor = new WindowsUpdateCacheExecutor(runtime);

        var result = await executor.ExecuteAsync(NewContext());

        result.ActionId.Should().Be("action.disk.clean-windows-update-cache");
        result.Status.Should().Be(ActionExecutionStatus.PartialSuccess);
        result.CanRollback.Should().BeTrue();
        result.Message.Should().Contain("1 file(s) skipped");
        runtime.StopServiceCalls.Should().Be(1);
        runtime.StartServiceCalls.Should().Be(1);
        File.Exists(Path.Combine(dir.CachePath, "locked.bin")).Should().BeTrue(
            "failed removals must be left in place");
        File.Exists(Path.Combine(dir.CachePath, "good.bin")).Should().BeFalse(
            "successful removals should be staged away from the source directory");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledAfterFirstRemoval_RestoresFilesAndRestartsService()
    {
        using var dir = new TestDir();
        var first = dir.WriteCacheFile("first.bin", new byte[] { 1 });
        var second = dir.WriteCacheFile("second.bin", new byte[] { 2 });
        var runtime = new TestRuntime(dir.CachePath, dir.StagingPath);
        var executor = new WindowsUpdateCacheExecutor(runtime);
        using var cts = new CancellationTokenSource();
        var context = NewContext(entry =>
        {
            if (entry.Message.Contains("Removed", StringComparison.OrdinalIgnoreCase))
                cts.Cancel();
        });

        var result = await executor.ExecuteAsync(context, cts.Token);

        result.ActionId.Should().Be("action.disk.clean-windows-update-cache");
        result.Status.Should().Be(ActionExecutionStatus.Cancelled);
        result.Message.Should().Contain("rolled back");
        runtime.StopServiceCalls.Should().Be(1);
        runtime.StartServiceCalls.Should().Be(1);
        File.Exists(first).Should().BeTrue("moved files must be restored on cancellation");
        File.Exists(second).Should().BeTrue("unmoved files must remain in place");
        Directory.Exists(dir.StagingPath).Should().BeFalse(
            "successful cancellation rollback should clean up staging");
    }

    private static ActionExecutionContext NewContext(Action<ActionLogEntry>? onEntry = null) => new()
    {
        IsDryRun = false,
        IsElevated = true,
        ConfirmationGranted = true,
        RequestedBy = "test",
        Log = onEntry is null ? new ActionExecutionLog() : new ActionExecutionLog(onEntry),
    };

    private sealed class TestRuntime : WindowsUpdateCacheExecutor.IWindowsUpdateCacheRuntime
    {
        public TestRuntime(string cachePath, string stagingPath)
        {
            CachePath = cachePath;
            StagingPath = stagingPath;
        }

        public string CachePath { get; }
        public string StagingPath { get; }
        public bool StopServiceResult { get; set; } = true;
        public int StopServiceCalls { get; private set; }
        public int StartServiceCalls { get; private set; }
        public HashSet<string> FailMoveFileNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string GetCachePath() => CachePath;

        public string NewStagingRoot() => StagingPath;

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public (int FileCount, long TotalBytes) Scan(
            string rootPath,
            int minAgeMinutes,
            CancellationToken ct) => CleanupScanner.Scan(rootPath, minAgeMinutes, ct);

        public IEnumerable<FileInfo> EnumerateFiles(
            string rootPath,
            int minAgeMinutes,
            CancellationToken ct) => CleanupScanner.EnumerateFiles(rootPath, minAgeMinutes, ct);

        public void MoveFileToStaging(string sourcePath, string stagingPath)
        {
            if (FailMoveFileNames.Contains(Path.GetFileName(sourcePath)))
                throw new IOException("Simulated move failure.");

            File.Move(sourcePath, stagingPath);
        }

        public void DeleteFile(string path) => File.Delete(path);

        public bool TryStopService(string serviceName, ActionExecutionLog log)
        {
            StopServiceCalls++;
            return StopServiceResult;
        }

        public void StartService(string serviceName, ActionExecutionLog log)
        {
            StartServiceCalls++;
        }

        public void RestoreFromStaging(
            IReadOnlyList<string> manifest,
            string stagingDir,
            ActionExecutionLog log) => CleanupScanner.RestoreFromStaging(manifest, stagingDir, log);

        public void TryDeleteDirectory(string stagingDir, ActionExecutionLog log) =>
            CleanupScanner.TryDeleteDirectory(stagingDir, log);

        public bool TryWriteManifest(
            string stagingDir,
            IReadOnlyList<string> manifest,
            ActionExecutionLog log) => CleanupScanner.TryWriteManifest(stagingDir, manifest, log);
    }

    private sealed class TestDir : IDisposable
    {
        public TestDir()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "lucid-windows-update-cache-execution-tests",
                Guid.NewGuid().ToString("N"));
            CachePath = Path.Combine(RootPath, "SoftwareDistribution", "Download");
            StagingPath = Path.Combine(RootPath, "staging");
            Directory.CreateDirectory(CachePath);
        }

        public string RootPath { get; }
        public string CachePath { get; }
        public string StagingPath { get; }

        public string WriteCacheFile(string fileName, byte[] bytes)
        {
            var path = Path.Combine(CachePath, fileName);
            File.WriteAllBytes(path, bytes);
            return path;
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
