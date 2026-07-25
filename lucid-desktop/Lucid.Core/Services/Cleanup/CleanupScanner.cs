namespace Lucid.Services.Cleanup;

/// <summary>
/// Shared file-system scanning and rollback-staging utilities used by all
/// disk-cleanup executors.
///
/// Design principles:
///   • All methods are static — no state, thread-safe, no DI required.
///   • File enumeration uses BFS so it never recurses deeply enough to blow
///     the stack, and checking the cancellation token once per directory
///     level is cheap relative to the I/O.
///   • Inaccessible files and directories are silently skipped; the caller
///     decides whether to log them.
///   • Staging uses atomic same-drive moves (metadata-only rename) when
///     possible, freeing space in the source directory instantly.
///
/// Staging layout:
///   %LOCALAPPDATA%\Lucid\Rollback\{category}\{timestamp}\
///     {guid}{ext}   — moved file (original name is in the manifest)
///     .manifest     — "{stagingName}|{originalPath}" per line
/// </summary>
internal static class CleanupScanner
{
    // ── Manifest file name ────────────────────────────────────────────────────

    internal const string ManifestFileName = ".manifest";

    // ── Directory enumeration ─────────────────────────────────────────────────

    /// <summary>
    /// Breadth-first enumeration of every file under <paramref name="rootPath"/>
    /// whose last-write time is at least <paramref name="minAgeMinutes"/> old.
    ///
    /// Directories or files that cannot be accessed are silently skipped.
    /// Cancellation is checked before each directory level.
    /// </summary>
    internal static IEnumerable<FileInfo> EnumerateFiles(
        string            rootPath,
        int               minAgeMinutes,
        CancellationToken ct)
    {
        if (!Directory.Exists(rootPath)) yield break;

        var cutoff = DateTime.UtcNow.AddMinutes(-minAgeMinutes);
        var queue  = new Queue<string>();
        queue.Enqueue(rootPath);

        while (queue.Count > 0)
        {
            if (ct.IsCancellationRequested) yield break;

            var dir = queue.Dequeue();

            FileInfo[] files;
            try
            {
                files = new DirectoryInfo(dir).GetFiles();
            }
            catch (Exception ex) when (
                ex is IOException           or
                UnauthorizedAccessException or
                DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) yield break;
                if (file.LastWriteTimeUtc > cutoff) continue;
                yield return file;
            }

            try
            {
                foreach (var sub in Directory.GetDirectories(dir))
                    queue.Enqueue(sub);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // ── Size scanning (dry-run) ───────────────────────────────────────────────

    /// <summary>
    /// Returns the total number of files and bytes that
    /// <see cref="EnumerateFiles"/> would yield for the given path + age.
    /// Does not take an action log — callers log results themselves.
    /// </summary>
    internal static (int FileCount, long TotalBytes) Scan(
        string            rootPath,
        int               minAgeMinutes,
        CancellationToken ct)
    {
        int  count = 0;
        long bytes = 0;

        foreach (var file in EnumerateFiles(rootPath, minAgeMinutes, ct))
        {
            try
            {
                bytes += file.Length;
                count++;
            }
            catch (Exception ex) when (
                ex is IOException or FileNotFoundException) { }
        }

        return (count, bytes);
    }

    // ── Staging helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns a unique staging root path for the given executor category.
    /// The path is never created here — the caller is responsible for calling
    /// <see cref="Directory.CreateDirectory"/> on the returned path.
    /// </summary>
    internal static string NewStagingRoot(string category) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lucid", "Rollback", category,
            DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss"));

    /// <summary>
    /// Writes the manifest file to <paramref name="stagingDir"/>.
    /// Returns <see langword="true"/> on success, logs a warning and returns
    /// <see langword="false"/> on I/O failure.
    /// </summary>
    internal static bool TryWriteManifest(
        string                stagingDir,
        IReadOnlyList<string> lines,
        Services.Execution.ActionExecutionLog log)
    {
        try
        {
            File.WriteAllLines(Path.Combine(stagingDir, ManifestFileName), lines);
            return true;
        }
        catch (Exception ex)
        {
            log.Warn(
                $"Could not write rollback manifest: {ex.Message} " +
                $"Rollback will not be available for this run.");
            return false;
        }
    }

    /// <summary>
    /// Silently attempts to delete the staging directory tree.
    /// Logs a warning if it fails; never throws.
    /// </summary>
    internal static void TryDeleteDirectory(
        string staging,
        Services.Execution.ActionExecutionLog log)
    {
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
        catch (Exception ex)
        {
            log.Warn($"Could not remove staging directory: {ex.Message}");
        }
    }

    // ── Cancellation restore ──────────────────────────────────────────────────

    /// <summary>
    /// Synchronously moves all files recorded in <paramref name="manifest"/>
    /// back from staging to their original locations.  Used when execution is
    /// cancelled mid-run to return the system to a clean state.
    /// </summary>
    internal static void RestoreFromStaging(
        IReadOnlyList<string>                 manifest,
        string                                stagingDir,
        Services.Execution.ActionExecutionLog log)
    {
        foreach (var line in manifest)
        {
            var sep = line.IndexOf('|');
            if (sep < 0) continue;

            var stagingName  = line[..sep];
            var originalPath = line[(sep + 1)..];
            var stagingFile  = Path.Combine(stagingDir, stagingName);

            if (!File.Exists(stagingFile)) continue;

            try
            {
                var originalDir = Path.GetDirectoryName(originalPath);
                if (originalDir is not null)
                    Directory.CreateDirectory(originalDir);

                File.Move(stagingFile, originalPath, overwrite: false);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                log.Warn(
                    $"  Could not restore  {Path.GetFileName(originalPath)}  " +
                    $"during cancellation cleanup: {ex.Message}");
            }
        }
    }

    // ── Common rollback runner ────────────────────────────────────────────────

    /// <summary>
    /// Reads a manifest from <paramref name="stagingDir"/> and moves all
    /// staged files back to their original locations.
    ///
    /// Called by an executor's <c>RollbackAsync</c> implementation.
    /// Returns a fully populated <see cref="Services.Execution.ActionExecutionResult"/>.
    /// </summary>
    internal static Services.Execution.ActionExecutionResult RunRollback(
        string                               actionId,
        string                               stagingDir,
        Services.Execution.ActionExecutionContext context,
        CancellationToken                    ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        context.Log.Info("Restoring files from rollback staging…");
        context.Log.Info($"Staging: {stagingDir}");

        if (!Directory.Exists(stagingDir))
        {
            context.Log.Error(
                "Staging directory not found — files may have been purged.");
            sw.Stop();
            return Services.Execution.ActionExecutionResult.Failed(
                actionId,
                "Rollback staging not found. Files may have been purged.",
                sw.Elapsed, context.Log.Build());
        }

        var manifestPath = Path.Combine(stagingDir, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            context.Log.Error(
                "Rollback manifest missing — cannot determine original paths.");
            sw.Stop();
            return Services.Execution.ActionExecutionResult.Failed(
                actionId,
                "Rollback manifest is missing — cannot restore files.",
                sw.Elapsed, context.Log.Build());
        }

        string[] lines;
        try { lines = File.ReadAllLines(manifestPath); }
        catch (Exception ex)
        {
            context.Log.Error($"Could not read rollback manifest: {ex.Message}");
            sw.Stop();
            return Services.Execution.ActionExecutionResult.Failed(
                actionId, "Could not read rollback manifest.",
                sw.Elapsed, context.Log.Build(), ex.ToString());
        }

        context.Log.Info($"Restoring {lines.Length} file(s)…");

        int restored = 0;
        int failed   = 0;

        foreach (var line in lines)
        {
            if (ct.IsCancellationRequested) break;

            var sep = line.IndexOf('|');
            if (sep < 0) continue;

            var stagingName  = line[..sep];
            var originalPath = line[(sep + 1)..];
            var stagingFile  = Path.Combine(stagingDir, stagingName);

            if (!File.Exists(stagingFile))
            {
                context.Log.Warn($"  Staging file missing: {stagingName} — skipped.");
                failed++;
                continue;
            }

            try
            {
                var originalDir = Path.GetDirectoryName(originalPath);
                if (originalDir is not null)
                    Directory.CreateDirectory(originalDir);

                File.Move(stagingFile, originalPath, overwrite: false);
                context.Log.Info($"  Restored  {Path.GetFileName(originalPath)}");
                restored++;
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                context.Log.Warn(
                    $"  Could not restore  {Path.GetFileName(originalPath)}  — " +
                    $"{ex.Message}");
                failed++;
            }
        }

        sw.Stop();

        if (ct.IsCancellationRequested)
        {
            context.Log.Warn("Rollback cancelled mid-run.");
            return Services.Execution.ActionExecutionResult.Cancelled(
                actionId,
                $"Rollback cancelled. {restored} file(s) restored before cancellation.",
                sw.Elapsed, context.Log.Build());
        }

        if (failed == 0)
        {
            context.Log.Info("Cleaning up staging area…");
            TryDeleteDirectory(stagingDir, context.Log);
        }

        if (restored == 0 && failed > 0)
            return Services.Execution.ActionExecutionResult.Failed(
                actionId,
                $"Rollback failed — could not restore {failed} file(s).",
                sw.Elapsed, context.Log.Build());

        if (failed > 0)
            return Services.Execution.ActionExecutionResult.PartiallySucceeded(
                actionId,
                $"Partially restored: {restored} file(s) recovered, " +
                $"{failed} could not be restored.",
                sw.Elapsed, context.Log.Build());

        return Services.Execution.ActionExecutionResult.Succeeded(
            actionId,
            $"Rollback complete — {restored} file(s) restored.",
            sw.Elapsed, context.Log.Build());
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    /// <summary>Formats a raw byte count as a human-readable string (B/KB/MB/GB).</summary>
    internal static string FormatBytes(long bytes) => bytes switch
    {
        < 1_024         => $"{bytes} B",
        < 1_048_576     => $"{bytes / 1_024.0:F1} KB",
        < 1_073_741_824 => $"{bytes / 1_048_576.0:F1} MB",
        _               => $"{bytes / 1_073_741_824.0:F1} GB",
    };
}
