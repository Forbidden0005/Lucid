using FluentAssertions;
using Lucid.Services.Cleanup;
using Xunit;

namespace Lucid.Tests.Cleanup;

/// <summary>
/// Verifies the retention policy that keeps the rollback staging area from
/// growing without bound. If this regresses, every reversible cleanup silently
/// hoards its "freed" bytes under LocalAppData forever, or — worse — a too-eager
/// sweep deletes staging sets a user could still undo.
/// </summary>
public sealed class RollbackStagingSweeperTests
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    [Fact]
    public void Sweep_MissingRoot_ReturnsEmpty_AndDoesNotThrow()
    {
        var missing = Path.Combine(Path.GetTempPath(), "lucid-rollback-missing", Guid.NewGuid().ToString("N"));

        var result = RollbackStagingSweeper.Sweep(missing, Retention, DateTimeOffset.UtcNow);

        result.Should().Be(RollbackSweepResult.Empty);
    }

    [Fact]
    public void Sweep_RemovesExpiredSet_AndKeepsRecentSet()
    {
        using var root = new RollbackRoot();
        var now = DateTimeOffset.UtcNow;

        var expired = root.AddStagingSet("TempCleanup", ageDays: 30, "old.tmp", new byte[100]);
        var recent  = root.AddStagingSet("TempCleanup", ageDays: 1,  "new.tmp", new byte[50]);

        var result = RollbackStagingSweeper.Sweep(root.Path, Retention, now);

        result.StagingSetsScanned.Should().Be(2);
        result.StagingSetsRemoved.Should().Be(1);
        result.Errors.Should().Be(0);
        Directory.Exists(expired).Should().BeFalse("a set older than the window is past undo and should be reclaimed");
        Directory.Exists(recent).Should().BeTrue("a set inside the window must stay reversible");
    }

    [Fact]
    public void Sweep_TalliesReclaimedBytes_FromAllFilesInExpiredSet()
    {
        using var root = new RollbackRoot();

        root.AddStagingSet("BrowserCache", ageDays: 14, "a.bin", new byte[100]);
        var expired = root.LastSet;
        File.WriteAllBytes(Path.Combine(expired, "b.bin"), new byte[40]);
        // Re-age: writing the second file refreshed the directory's last-write time.
        Directory.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-14));

        var result = RollbackStagingSweeper.Sweep(root.Path, Retention, DateTimeOffset.UtcNow);

        result.StagingSetsRemoved.Should().Be(1);
        result.BytesReclaimed.Should().Be(140);
    }

    [Fact]
    public void Sweep_PrunesCategoryFolder_WhenItBecomesEmpty()
    {
        using var root = new RollbackRoot();
        root.AddStagingSet("TempCleanup", ageDays: 30, "old.tmp", new byte[10]);
        var categoryDir = Path.Combine(root.Path, "TempCleanup");

        RollbackStagingSweeper.Sweep(root.Path, Retention, DateTimeOffset.UtcNow);

        Directory.Exists(categoryDir).Should().BeFalse("an emptied category folder should be pruned");
    }

    [Fact]
    public void Sweep_KeepsCategoryFolder_WhenARecentSetRemains()
    {
        using var root = new RollbackRoot();
        root.AddStagingSet("TempCleanup", ageDays: 30, "old.tmp", new byte[10]);
        root.AddStagingSet("TempCleanup", ageDays: 2,  "new.tmp", new byte[10]);
        var categoryDir = Path.Combine(root.Path, "TempCleanup");

        var result = RollbackStagingSweeper.Sweep(root.Path, Retention, DateTimeOffset.UtcNow);

        result.StagingSetsRemoved.Should().Be(1);
        Directory.Exists(categoryDir).Should().BeTrue("the category still holds a live set");
    }

    [Fact]
    public void Sweep_AtExactWindowBoundary_RemovesSet()
    {
        using var root = new RollbackRoot();
        var now = DateTimeOffset.UtcNow;
        root.AddStagingSet("TempCleanup", ageDays: 0, "edge.tmp", new byte[1]);
        // Age the set to exactly the retention horizon.
        Directory.SetLastWriteTimeUtc(root.LastSet, (now - Retention).UtcDateTime);

        var result = RollbackStagingSweeper.Sweep(root.Path, Retention, now);

        result.StagingSetsRemoved.Should().Be(1, "a set exactly at the cutoff is expired");
    }

    [Fact]
    public void Sweep_TalliesReclaimedBytes_AcrossNestedSubdirectories()
    {
        using var root = new RollbackRoot();
        var set = root.AddStagingSet("TempCleanup", ageDays: 20, "top.bin", new byte[30]);
        var nested = Path.Combine(set, "sub", "deep");
        Directory.CreateDirectory(nested);
        File.WriteAllBytes(Path.Combine(nested, "leaf.bin"), new byte[70]);
        // Re-age: creating the nested tree refreshed the set's last-write time.
        Directory.SetLastWriteTimeUtc(set, DateTime.UtcNow.AddDays(-20));

        var result = RollbackStagingSweeper.Sweep(root.Path, Retention, DateTimeOffset.UtcNow);

        result.StagingSetsRemoved.Should().Be(1);
        result.BytesReclaimed.Should().Be(100, "measurement must recurse into nested subdirectories");
        Directory.Exists(set).Should().BeFalse();
    }

    [Fact]
    public void Sweep_RemovesMultipleExpiredSetsInOneCategory_AndSumsBytes()
    {
        using var root = new RollbackRoot();
        root.AddStagingSet("TempCleanup", ageDays: 10, "a.bin", new byte[15]);
        root.AddStagingSet("TempCleanup", ageDays: 40, "b.bin", new byte[25]);

        var result = RollbackStagingSweeper.Sweep(root.Path, Retention, DateTimeOffset.UtcNow);

        result.StagingSetsScanned.Should().Be(2);
        result.StagingSetsRemoved.Should().Be(2);
        result.BytesReclaimed.Should().Be(40);
        Directory.Exists(Path.Combine(root.Path, "TempCleanup"))
            .Should().BeFalse("both sets expired, so the category is pruned");
    }

    [Fact]
    public void Sweep_ReclaimsLeftoverPurgingDirs_WithoutCountingThemAsSets()
    {
        using var root = new RollbackRoot();
        // A normal expired set...
        root.AddStagingSet("TempCleanup", ageDays: 30, "real.bin", new byte[10]);
        // ...plus an orphaned ".purging-*" leftover from a prior interrupted pass.
        var purging = Path.Combine(root.Path, "TempCleanup", ".purging-deadbeef");
        Directory.CreateDirectory(purging);
        File.WriteAllBytes(Path.Combine(purging, "orphan.bin"), new byte[5]);

        var result = RollbackStagingSweeper.Sweep(root.Path, Retention, DateTimeOffset.UtcNow);

        result.StagingSetsScanned.Should().Be(1, "a .purging-* leftover is not a live staging set");
        result.StagingSetsRemoved.Should().Be(1);
        Directory.Exists(purging).Should().BeFalse("orphaned purge dirs are reclaimed on the next pass");
        Directory.Exists(Path.Combine(root.Path, "TempCleanup")).Should().BeFalse();
    }

    [Fact]
    public void Sweep_WhenDeleteIsBlocked_NeverLeavesAPartialLiveSet()
    {
        using var root = new RollbackRoot();
        var lockedSet = root.AddStagingSet("TempCleanup", ageDays: 30, "locked.bin", new byte[20]);
        var freeSet   = root.AddStagingSet("TempCleanup", ageDays: 30, "free.bin",   new byte[8]);

        // Hold a delete-denying handle so the locked set's tree cannot be fully removed.
        using (new FileStream(Path.Combine(lockedSet, "locked.bin"),
                   FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = RollbackStagingSweeper.Sweep(root.Path, Retention, DateTimeOffset.UtcNow);

            result.Should().NotBeNull("the sweep must never throw");
            Directory.Exists(freeSet).Should().BeFalse("the unblocked set is still reclaimed");

            // The safety invariant the fix guarantees: a blocked delete leaves the set
            // EITHER fully gone (retired to .purging) OR fully intact — never a
            // partially-emptied set that still looks live to a rollback.
            bool goneOrIntact =
                !Directory.Exists(lockedSet) ||
                File.Exists(Path.Combine(lockedSet, "locked.bin"));
            goneOrIntact.Should().BeTrue(
                "a blocked delete must never leave a partial, still-live staging set");
        }
    }

    private sealed class RollbackRoot : IDisposable
    {
        public RollbackRoot()
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "lucid-rollback-sweeper-tests",
                Guid.NewGuid().ToString("N"));
            Path = System.IO.Path.Combine(Root, "Rollback");
            Directory.CreateDirectory(Path);
        }

        private string Root { get; }
        public string Path { get; }

        /// <summary>Path of the most recently created staging set.</summary>
        public string LastSet { get; private set; } = string.Empty;

        /// <summary>
        /// Creates a <c>Rollback\{category}\{timestamp}\</c> set with one file,
        /// then back-dates the set's last-write time by <paramref name="ageDays"/>.
        /// Returns the set's path.
        /// </summary>
        public string AddStagingSet(string category, int ageDays, string fileName, byte[] bytes)
        {
            var stamp   = Guid.NewGuid().ToString("N");
            var setPath = System.IO.Path.Combine(Path, category, stamp);
            Directory.CreateDirectory(setPath);
            File.WriteAllBytes(System.IO.Path.Combine(setPath, fileName), bytes);
            Directory.SetLastWriteTimeUtc(setPath, DateTime.UtcNow.AddDays(-ageDays));
            LastSet = setPath;
            return setPath;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
