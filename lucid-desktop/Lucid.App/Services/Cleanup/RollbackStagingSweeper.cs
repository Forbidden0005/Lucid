using System.Security;

namespace Lucid.Services.Cleanup;

/// <summary>
/// Outcome of a single rollback-staging sweep pass. All counts are best-effort:
/// directories that cannot be read or removed are tallied in <see cref="Errors"/>
/// rather than aborting the pass.
/// </summary>
public sealed record RollbackSweepResult(
    int  StagingSetsScanned,
    int  StagingSetsRemoved,
    long BytesReclaimed,
    int  Errors)
{
    /// <summary>A no-op result — nothing scanned, removed, or reclaimed.</summary>
    public static readonly RollbackSweepResult Empty = new(0, 0, 0, 0);
}

/// <summary>
/// Pure sweeper for the reversible-cleanup rollback staging area.
///
/// Multiple destructive executors move (rather than delete) files into a private
/// staging tree so their actions stay reversible:
///   <c>%LOCALAPPDATA%\Lucid\Rollback\{category}\{timestamp}\</c>
/// (see <see cref="CleanupScanner.NewStagingRoot"/> and
/// <c>TempFileCleanupExecutor</c>). Until this sweeper existed nothing ever
/// removed those sets, so every cleanup that was not explicitly rolled back left
/// its bytes parked under LocalAppData forever — a disk-cleanup feature that
/// silently relocated reclaimed space instead of freeing it.
///
/// This sweeper enforces a retention window: a staging set older than the window
/// is past the point where a user would realistically undo it (the files were
/// already "removed" from their perspective), so it is deleted and the space is
/// genuinely reclaimed. Sets inside the window are left untouched, preserving
/// the reversibility guarantee for recent actions.
///
/// Age basis: the staging directory's last-write time (UTC). This is producer
/// agnostic — it does not depend on parsing the timestamp folder name — and a
/// generous window (days) makes it impossible to race a cleanup that is still
/// in progress, since an active staging set was just written and reads as young.
///
/// The method is pure (no settings, governance, logging, or wall-clock reads):
/// <paramref name="now"/> is injected so the policy is fully unit-testable.
/// </summary>
public static class RollbackStagingSweeper
{
    /// <summary>
    /// Name prefix for a set that has been retired from rollback visibility and is
    /// being deleted. An expired set is renamed to <c>{PurgingPrefix}{guid}</c>
    /// (a same-volume metadata rename) before its tree is deleted, so an
    /// interrupted delete can only ever leave a half-emptied <c>.purging-*</c>
    /// directory — never a timestamp-named set that a later rollback would treat
    /// as live and silently restore from incompletely. Leftover <c>.purging-*</c>
    /// directories are reclaimed on the next pass.
    /// </summary>
    private const string PurgingPrefix = ".purging-";

    /// <summary>
    /// The default rollback staging root: <c>%LOCALAPPDATA%\Lucid\Rollback</c>.
    /// </summary>
    public static string DefaultRollbackRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lucid", "Rollback");

    /// <summary>
    /// Removes every staging set under <paramref name="rollbackRoot"/> whose
    /// last-write time is older than <paramref name="maxAge"/> relative to
    /// <paramref name="now"/>, then prunes any category folders left empty.
    ///
    /// Never throws: a missing root returns <see cref="RollbackSweepResult.Empty"/>
    /// and per-directory I/O failures are counted in
    /// <see cref="RollbackSweepResult.Errors"/>.
    /// </summary>
    public static RollbackSweepResult Sweep(
        string rollbackRoot, TimeSpan maxAge, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(rollbackRoot) || !Directory.Exists(rollbackRoot))
            return RollbackSweepResult.Empty;

        // Staging sets last written at or before this instant are expired.
        var cutoffUtc = (now - maxAge).UtcDateTime;

        string[] categoryDirs;
        try
        {
            categoryDirs = Directory.GetDirectories(rollbackRoot);
        }
        catch (Exception ex) when (IsIoError(ex))
        {
            return RollbackSweepResult.Empty;
        }

        int  scanned   = 0;
        int  removed   = 0;
        int  errors    = 0;
        long reclaimed = 0;

        foreach (var categoryDir in categoryDirs)
        {
            string[] stagingSets;
            try
            {
                stagingSets = Directory.GetDirectories(categoryDir);
            }
            catch (Exception ex) when (IsIoError(ex))
            {
                errors++;
                continue;
            }

            foreach (var stagingSet in stagingSets)
            {
                // Leftover from a previous pass whose recursive delete was
                // interrupted. It is already orphaned (no longer a live,
                // rollback-discoverable set) — retry reclaiming it, but never count
                // it as a scanned staging set.
                if (Path.GetFileName(stagingSet)
                        .StartsWith(PurgingPrefix, StringComparison.Ordinal))
                {
                    if (TryDeleteTree(stagingSet, out long freed)) reclaimed += freed;
                    else                                            errors++;
                    continue;
                }

                scanned++;

                DateTime lastWriteUtc;
                try
                {
                    lastWriteUtc = Directory.GetLastWriteTimeUtc(stagingSet);
                }
                catch (Exception ex) when (IsIoError(ex))
                {
                    errors++;
                    continue;
                }

                if (lastWriteUtc > cutoffUtc)
                    continue; // still within the retention window — keep it

                // Retire the live set name first via a same-volume metadata rename,
                // then delete the renamed tree. If the delete is interrupted partway,
                // only an orphaned .purging-* directory is left half-emptied — the
                // timestamp-named set a rollback would look for is already gone, so
                // no rollback can silently restore from an incomplete set.
                var purgePath = Path.Combine(
                    categoryDir, $"{PurgingPrefix}{Guid.NewGuid():N}");
                try
                {
                    Directory.Move(stagingSet, purgePath);
                }
                catch (Exception ex) when (IsIoError(ex))
                {
                    // Rename failed — the set is untouched and still live. Safe to
                    // leave; the next pass retries it.
                    errors++;
                    continue;
                }

                removed++;
                if (TryDeleteTree(purgePath, out long size)) reclaimed += size;
                else                                          errors++; // bytes reclaimed on a later pass
            }

            // Remove the category folder if the sweep emptied it.
            TryPruneIfEmpty(categoryDir);
        }

        return new RollbackSweepResult(scanned, removed, reclaimed, errors);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Best-effort recursive byte count of a directory. Returns 0 on any error so
    /// reclaimed-space reporting never blocks a deletion.
    /// </summary>
    private static long TryMeasureDirectory(string dir)
    {
        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(
                         dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch (Exception ex) when (IsIoError(ex)) { /* skip unreadable file */ }
            }
            return total;
        }
        catch (Exception ex) when (IsIoError(ex))
        {
            return 0;
        }
    }

    /// <summary>
    /// Measures <paramref name="dir"/> then deletes it recursively. Returns
    /// <see langword="true"/> only when the whole tree was removed; on a partial
    /// or failed delete returns <see langword="false"/> and reports no reclaimed
    /// bytes (the remainder is retried on a later pass). <paramref name="bytes"/>
    /// is the measured size regardless.
    /// </summary>
    private static bool TryDeleteTree(string dir, out long bytes)
    {
        bytes = TryMeasureDirectory(dir);
        try
        {
            ClearReadOnlyAttributes(dir);
            Directory.Delete(dir, recursive: true);
            return true;
        }
        catch (Exception ex) when (IsIoError(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Clears the ReadOnly attribute from everything under <paramref name="dir"/>.
    /// Staged files can carry ReadOnly (it survives <see cref="File.Move(string, string)"/>),
    /// and <see cref="Directory.Delete(string, bool)"/> throws on a read-only entry —
    /// which, after a set has already been renamed to <c>.purging-*</c>, would make
    /// every later pass retry the same delete forever and never reclaim the space.
    /// Best-effort: if attributes cannot be cleared, the subsequent delete simply
    /// throws and the set is retried on a later pass.
    /// </summary>
    private static void ClearReadOnlyAttributes(string dir)
    {
        try
        {
            foreach (var info in new DirectoryInfo(dir)
                         .EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                    info.Attributes &= ~FileAttributes.ReadOnly;
            }
        }
        catch (Exception ex) when (IsIoError(ex))
        {
            // Best-effort — the delete below will surface any real failure.
        }
    }

    /// <summary>Deletes <paramref name="dir"/> only if it has no entries left.</summary>
    private static void TryPruneIfEmpty(string dir)
    {
        try
        {
            if (Directory.Exists(dir) &&
                !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir, recursive: false);
            }
        }
        catch (Exception ex) when (IsIoError(ex))
        {
            // Leaving a stray empty folder is harmless — ignore.
        }
    }

    private static bool IsIoError(Exception ex) =>
        ex is IOException
           or UnauthorizedAccessException
           or DirectoryNotFoundException
           or SecurityException;
}
