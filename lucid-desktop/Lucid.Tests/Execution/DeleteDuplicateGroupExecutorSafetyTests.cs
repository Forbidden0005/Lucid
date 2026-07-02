using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Covers the duplicate-group delete executor: the path guard must refuse the
/// entire group before any mutation when a protected path appears in it, and
/// the normal staging + rollback round trip must keep working for safe paths.
/// </summary>
public sealed class DeleteDuplicateGroupExecutorSafetyTests
{
    [Fact]
    public async Task ExecuteAsync_GroupContainsProtectedPath_FailsWithoutMovingAnyFile()
    {
        using var dir = new TestDir();
        var safePath = dir.WriteFile("copy-a.bin", new byte[] { 1, 2, 3 });
        // The guard matches the "\drivers\" fragment, so a real file under a
        // drivers subfolder proves nothing in the group is touched.
        var protectedPath = dir.WriteFile(@"drivers\copy-b.bin", new byte[] { 1, 2, 3 });
        var executor = new DeleteDuplicateGroupExecutor();

        var result = await executor.ExecuteAsync(
            NewContext(deletePaths: $"{safePath}|{protectedPath}"));

        result.Status.Should().Be(ActionExecutionStatus.Failed,
            "a protected path means the duplicate grouping cannot be trusted");
        result.CanRollback.Should().BeFalse();
        result.Message.Should().Contain("protected system directory");
        File.Exists(safePath).Should().BeTrue("no file may move when the group is refused");
        File.Exists(protectedPath).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ProtectedKeepPath_FailsWithoutMovingAnyDeleteFile()
    {
        using var dir = new TestDir();
        var dupA = dir.WriteFile("dup-a.bin", new byte[] { 4, 5, 6 });
        var dupB = dir.WriteFile("dup-b.bin", new byte[] { 4, 5, 6 });
        // A protected keep candidate taints the whole group even though the
        // keep file itself is never touched — the grouping cannot be trusted.
        var protectedKeep = dir.WriteFile(@"drivers\keep.sys", new byte[] { 4, 5, 6 });
        var executor = new DeleteDuplicateGroupExecutor();

        var result = await executor.ExecuteAsync(
            NewContext(deletePaths: $"{dupA}|{dupB}", keepPath: protectedKeep));

        result.Status.Should().Be(ActionExecutionStatus.Failed,
            "a protected keep path means the duplicate grouping cannot be trusted");
        result.Message.Should().Contain("protected system directory");
        File.Exists(dupA).Should().BeTrue("no delete-list file may move when the group is refused");
        File.Exists(dupB).Should().BeTrue();
        File.Exists(protectedKeep).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_DryRunWithProtectedKeepPath_SurfacesRefusalInPreview()
    {
        var executor = new DeleteDuplicateGroupExecutor();
        var context = NewContext(
            deletePaths: @"C:\Users\u\Downloads\dup-1.bin|C:\Users\u\Downloads\dup-2.bin",
            keepPath: @"C:\Windows\System32\dup.bin",
            isDryRun: true);

        var result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(ActionExecutionStatus.Failed,
            "the preview must surface a protected keep candidate before approval");
        result.Message.Should().Contain("protected system directory");
    }

    [Fact]
    public async Task ExecuteAsync_DryRunWithProtectedPath_SurfacesRefusalInPreview()
    {
        var executor = new DeleteDuplicateGroupExecutor();
        var context = NewContext(
            deletePaths: @"C:\Users\u\Downloads\dup.bin|C:\Windows\System32\dup.bin",
            isDryRun: true);

        var result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(ActionExecutionStatus.Failed,
            "the preview the user approves must already surface the refusal");
        result.Message.Should().Contain("protected system directory");
    }

    [Fact]
    public async Task ExecuteAsync_SafeGroup_StagesFiles_AndRollbackRestoresThem()
    {
        using var dir = new TestDir();
        var keepPath = dir.WriteFile("keep.bin", new byte[] { 9, 9, 9 });
        var dupA = dir.WriteFile("dup-a.bin", new byte[] { 9, 9, 9 });
        var dupB = dir.WriteFile("dup-b.bin", new byte[] { 9, 9, 9 });
        var executor = new DeleteDuplicateGroupExecutor();

        var executeResult = await executor.ExecuteAsync(
            NewContext(deletePaths: $"{dupA}|{dupB}", keepPath: keepPath));

        executeResult.Status.Should().Be(ActionExecutionStatus.Success);
        executeResult.CanRollback.Should().BeTrue();
        executeResult.RollbackToken.Should().NotBeNullOrWhiteSpace();
        File.Exists(keepPath).Should().BeTrue("the keep candidate is never touched");
        File.Exists(dupA).Should().BeFalse("duplicates should be staged away");
        File.Exists(dupB).Should().BeFalse();

        var rollbackResult = await executor.RollbackAsync(
            executeResult.RollbackToken!,
            NewContext(deletePaths: $"{dupA}|{dupB}", keepPath: keepPath));

        rollbackResult.Status.Should().Be(ActionExecutionStatus.Success);
        File.ReadAllBytes(dupA).Should().Equal(new byte[] { 9, 9, 9 },
            "rollback should restore staged duplicates byte-for-byte");
        File.ReadAllBytes(dupB).Should().Equal(new byte[] { 9, 9, 9 });
    }

    private static ActionExecutionContext NewContext(
        string deletePaths, string? keepPath = null, bool isDryRun = false)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DeleteDuplicateGroupExecutor.ParamDeletePaths] = deletePaths,
        };
        if (keepPath is not null)
            parameters[DeleteDuplicateGroupExecutor.ParamKeepPath] = keepPath;

        return new ActionExecutionContext
        {
            IsDryRun = isDryRun,
            IsElevated = true,
            ConfirmationGranted = true,
            RequestedBy = "test",
            Parameters = parameters,
            Log = new ActionExecutionLog(),
        };
    }

    private sealed class TestDir : IDisposable
    {
        public TestDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "lucid-delete-duplicate-group-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string WriteFile(string relativePath, byte[] bytes)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            var parent = System.IO.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            File.WriteAllBytes(fullPath, bytes);
            return fullPath;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
