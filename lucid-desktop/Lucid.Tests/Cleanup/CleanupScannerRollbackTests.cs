using FluentAssertions;
using Lucid.Services.Cleanup;
using Lucid.Services.Execution;
using Xunit;

namespace Lucid.Tests.Cleanup;

/// <summary>
/// Verifies the shared rollback path used by multiple cleanup executors.
/// If this logic regresses, several destructive actions can lose their
/// reversibility guarantees at once.
/// </summary>
public sealed class CleanupScannerRollbackTests
{
    [Fact]
    public void RunRollback_MissingStagingDirectory_FailsCleanly()
    {
        var missingDir = Path.Combine(Path.GetTempPath(), "lucid-missing-staging", Guid.NewGuid().ToString("N"));
        var context = NewContext();

        var result = CleanupScanner.RunRollback("action.test.cleanup", missingDir, context, CancellationToken.None);

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.Message.Should().Contain("Rollback staging not found");
    }

    [Fact]
    public void RunRollback_MissingManifest_FailsCleanly()
    {
        using var dir = new TestDir();
        var context = NewContext();

        var result = CleanupScanner.RunRollback("action.test.cleanup", dir.Path, context, CancellationToken.None);

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.Message.Should().Contain("Rollback manifest is missing");
    }

    [Fact]
    public void RunRollback_RestoresFiles_AndDeletesStagingOnSuccess()
    {
        using var dir = new TestDir();
        var originalFile = dir.TargetPath("restored.txt");
        var stagingName = "staged-file.txt";
        dir.WriteStagedFile(stagingName, new byte[] { 1, 2, 3, 4 });
        dir.WriteManifest($"{stagingName}|{originalFile}");

        var result = CleanupScanner.RunRollback(
            "action.test.cleanup",
            dir.Path,
            NewContext(),
            CancellationToken.None);

        result.Status.Should().Be(ActionExecutionStatus.Success);
        File.Exists(originalFile).Should().BeTrue();
        File.ReadAllBytes(originalFile).Should().Equal(new byte[] { 1, 2, 3, 4 });
        Directory.Exists(dir.Path).Should().BeFalse("successful rollback should clean up staging");
    }

    [Fact]
    public void RunRollback_WithMissingStagedFile_ReturnsPartialSuccess_AndPreservesStaging()
    {
        using var dir = new TestDir();
        var restoredFile = dir.TargetPath("restored.txt");
        var missingFile = dir.TargetPath("missing.txt");
        var existingStagingName = "present.txt";
        var missingStagingName = "gone.txt";

        dir.WriteStagedFile(existingStagingName, new byte[] { 8, 8, 8 });
        dir.WriteManifest(
            $"{existingStagingName}|{restoredFile}",
            $"{missingStagingName}|{missingFile}");

        var result = CleanupScanner.RunRollback(
            "action.test.cleanup",
            dir.Path,
            NewContext(),
            CancellationToken.None);

        result.Status.Should().Be(ActionExecutionStatus.PartialSuccess);
        File.Exists(restoredFile).Should().BeTrue();
        File.ReadAllBytes(restoredFile).Should().Equal(new byte[] { 8, 8, 8 });
        Directory.Exists(dir.Path).Should().BeTrue("partial rollback should preserve staging for inspection or retry");
        File.Exists(Path.Combine(dir.Path, CleanupScanner.ManifestFileName)).Should().BeTrue();
    }

    private static ActionExecutionContext NewContext() => new()
    {
        IsDryRun = false,
        IsElevated = true,
        ConfirmationGranted = true,
        RequestedBy = "test",
        Log = new ActionExecutionLog(),
    };

    private sealed class TestDir : IDisposable
    {
        public TestDir()
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "lucid-cleanup-rollback-tests",
                Guid.NewGuid().ToString("N"));
            Path = System.IO.Path.Combine(Root, "staging");
            TargetRoot = System.IO.Path.Combine(Root, "restored");
            Directory.CreateDirectory(Path);
            Directory.CreateDirectory(TargetRoot);
        }

        public string Root { get; }
        public string Path { get; }
        public string TargetRoot { get; }

        public string TargetPath(string fileName) => System.IO.Path.Combine(TargetRoot, fileName);

        public void WriteStagedFile(string fileName, byte[] bytes)
        {
            File.WriteAllBytes(System.IO.Path.Combine(Path, fileName), bytes);
        }

        public void WriteManifest(params string[] lines)
        {
            File.WriteAllLines(System.IO.Path.Combine(Path, CleanupScanner.ManifestFileName), lines);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
