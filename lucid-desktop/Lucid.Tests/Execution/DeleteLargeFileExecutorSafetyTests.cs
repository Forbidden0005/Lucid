using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Covers a concrete destructive executor with rollback support. These tests
/// verify that Lucid stages data before removal, returns a rollback token when
/// recovery is possible, and fails safely when restore would overwrite newer
/// user data.
/// </summary>
public sealed class DeleteLargeFileExecutorSafetyTests
{
    [Fact]
    public async Task ExecuteAsync_WithMissingFilePath_FailsWithoutRollback()
    {
        var executor = new DeleteLargeFileExecutor();
        var context = NewContext();

        var result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.CanRollback.Should().BeFalse();
        result.Message.Should().Contain("No file path specified");
    }

    [Fact]
    public async Task ExecuteAsync_WithProtectedPath_FailsWithoutTouchingTheFile()
    {
        using var dir = new TestDir();
        // The guard matches the "\drivers\" fragment, so a real file under a
        // drivers subfolder proves the refusal happens before any mutation.
        var filePath = dir.WriteFile(@"drivers\pseudo-driver.sys", new byte[] { 1, 2, 3 });
        var executor = new DeleteLargeFileExecutor();

        var result = await executor.ExecuteAsync(NewContext(filePath));

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.CanRollback.Should().BeFalse();
        result.Message.Should().Contain("protected system directory");
        File.Exists(filePath).Should().BeTrue("a protected path must never be staged or moved");
    }

    [Fact]
    public async Task ExecuteAsync_DryRunWithProtectedPath_SurfacesRefusalInPreview()
    {
        var executor = new DeleteLargeFileExecutor();
        var context = new ActionExecutionContext
        {
            IsDryRun = true,
            IsElevated = true,
            ConfirmationGranted = true,
            RequestedBy = "test",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DeleteLargeFileExecutor.ParamFilePath] = @"C:\Windows\System32\config\SYSTEM"
            },
            Log = new ActionExecutionLog(),
        };

        var result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(ActionExecutionStatus.Failed,
            "the preview the user approves must already surface the refusal");
        result.Message.Should().Contain("protected system directory");
    }

    [Fact]
    public async Task ExecuteAsync_StagesFile_AndRollbackRestoresIt()
    {
        using var dir = new TestDir();
        var filePath = dir.WriteFile("sample.bin", new byte[] { 1, 2, 3, 4, 5, 6 });
        var executor = new DeleteLargeFileExecutor();
        var executeContext = NewContext(filePath);

        var executeResult = await executor.ExecuteAsync(executeContext);

        executeResult.Status.Should().Be(ActionExecutionStatus.Success);
        executeResult.CanRollback.Should().BeTrue();
        executeResult.RollbackToken.Should().NotBeNullOrWhiteSpace();
        File.Exists(filePath).Should().BeFalse("the file should be moved to staging first");

        var rollbackContext = NewContext(filePath);
        var rollbackResult = await executor.RollbackAsync(
            executeResult.RollbackToken!,
            rollbackContext);

        rollbackResult.Status.Should().Be(ActionExecutionStatus.Success);
        File.Exists(filePath).Should().BeTrue("rollback should restore the original file");
        File.ReadAllBytes(filePath).Should().Equal(new byte[] { 1, 2, 3, 4, 5, 6 });
        Directory.Exists(executeResult.RollbackToken!).Should().BeFalse(
            "successful rollback should clean up the staging directory");
    }

    [Fact]
    public async Task RollbackAsync_WithConflictingDestination_FailsWithoutOverwritingExistingFile()
    {
        using var dir = new TestDir();
        var filePath = dir.WriteFile("conflict.bin", new byte[] { 9, 8, 7, 6 });
        var executor = new DeleteLargeFileExecutor();

        var executeResult = await executor.ExecuteAsync(NewContext(filePath));
        executeResult.CanRollback.Should().BeTrue();

        File.WriteAllBytes(filePath, new byte[] { 0, 0, 0 });

        var rollbackResult = await executor.RollbackAsync(
            executeResult.RollbackToken!,
            NewContext(filePath));

        rollbackResult.Status.Should().Be(ActionExecutionStatus.Failed);
        File.ReadAllBytes(filePath).Should().Equal(new byte[] { 0, 0, 0 },
            "rollback must not overwrite a newer file at the original path");
        Directory.Exists(executeResult.RollbackToken!).Should().BeTrue(
            "failed rollback should preserve staging so recovery can be retried");
    }

    private static ActionExecutionContext NewContext(string? filePath = null)
    {
        var parameters = filePath is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DeleteLargeFileExecutor.ParamFilePath] = filePath
            };

        return new ActionExecutionContext
        {
            IsDryRun = false,
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
                "lucid-delete-large-file-tests",
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
