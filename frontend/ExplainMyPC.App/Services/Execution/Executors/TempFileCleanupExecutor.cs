using System.Diagnostics;

namespace ExplainMyPC.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.disk.clean-temp-files" action.
///
/// Scans a curated set of known-safe temporary directories and removes stale
/// files that are no longer needed by any running process.
///
/// Safety model:
///   • Files are moved to a private rollback staging area rather than deleted
///     outright. On the same drive this is an atomic metadata-only rename —
///     instantaneous and requires no extra disk space. Effectively frees the
///     space from the originating temp directory immediately.
///   • Locked or inaccessible files are skipped with a log warning and never
///     cause the run to abort.
///   • Windows\Temp files are only touched if their last-write time is at least
///     60 minutes in the past, to avoid interfering with active OS operations.
///   • If the user cancels mid-run, every file moved to staging is automatically
///     restored to its original location before the Cancelled result is returned.
///     Cancellation is always fully safe.
///
/// Rollback:
///   The rollback token is the path to the staging directory. A plain-text
///   manifest file (.manifest) inside it maps staging file names back to original
///   paths. Rollback moves every staged file back and deletes the staging tree.
///
/// Staging layout:
///   %LOCALAPPDATA%\ExplainMyPC\Rollback\TempCleanup\{timestamp}\
///     {guid}{ext}   — moved temp file (original name is in the manifest)
///     .manifest     — "{stagingName}|{originalPath}" per line
///
/// Threading:
///   ExecuteAsync and RollbackAsync offload all file I/O to the thread pool via
///   Task.Run so the UI thread is never blocked. Log entries stream to the UI
///   thread via the callback wired in ActionExecutionViewModel.BuildLiveLog().
/// </summary>
internal sealed class TempFileCleanupExecutor : IActionExecutor
{
    // ── Identity ──────────────────────────────────────────────────────────────

    public string               ActionId             => "action.disk.clean-temp-files";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => false;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => true;

    // ── Target directory definitions ──────────────────────────────────────────

    /// <summary>
    /// A single directory to scan, paired with a minimum-age filter.
    /// Files whose last-write time is less than <see cref="MinAgeMinutes"/>
    /// minutes old are skipped to avoid disrupting active processes.
    /// </summary>
    private sealed record TempTarget(string Path, string DisplayName, int MinAgeMinutes);

    private static readonly IReadOnlyList<TempTarget> s_targets = BuildTargets();

    private static IReadOnlyList<TempTarget> BuildTargets()
    {
        var targets = new List<TempTarget>();

        // ── User temp (%TEMP% / %TMP%) ────────────────────────────────────────
        // Owned entirely by the current user. Safe to scan with no age filter;
        // files that are actively locked by a running process are skipped
        // automatically when File.Move throws IOException.
        var userTemp = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        targets.Add(new TempTarget(userTemp, "User Temp (%TEMP%)", MinAgeMinutes: 0));

        // ── Windows\Temp ──────────────────────────────────────────────────────
        // System-wide staging area used by Windows Update, MSI installers, and
        // other OS components. Only include files untouched for ≥ 60 minutes to
        // avoid interfering with operations in progress.
        var winTemp = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        if (Directory.Exists(winTemp))
            targets.Add(new TempTarget(winTemp, "Windows Temp", MinAgeMinutes: 60));

        // Deduplicate in the rare case %TEMP% resolves to the same path as
        // Windows\Temp (e.g. on custom or non-standard Windows configurations).
        return targets
            .DistinctBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    // ── Staging area ──────────────────────────────────────────────────────────

    private static string NewStagingRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExplainMyPC", "Rollback", "TempCleanup",
            DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss"));

    private const string ManifestFileName = ".manifest";

    // ── IActionExecutor.ExecuteAsync ──────────────────────────────────────────

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(
            () => context.IsDryRun
                ? RunDryRun(context, cancellationToken)
                : RunCleanup(context, cancellationToken),
            cancellationToken);

    // ── Dry-run ───────────────────────────────────────────────────────────────

    private ActionExecutionResult RunDryRun(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        context.Log.Info(
            "Scanning temporary directories — preview mode, no changes will be made.");

        long totalBytes = 0;
        int  totalFiles = 0;
        int  skipped    = 0;

        foreach (var target in s_targets)
        {
            if (ct.IsCancellationRequested) break;

            if (!Directory.Exists(target.Path))
            {
                context.Log.Warn(
                    $"Skipping {target.DisplayName} — directory not found.");
                continue;
            }

            var ageNote = target.MinAgeMinutes > 0
                ? $" (files idle ≥ {target.MinAgeMinutes} min)" : string.Empty;
            context.Log.Info($"Scanning {target.DisplayName}{ageNote}…");

            int  dirFiles = 0;
            long dirBytes = 0;

            foreach (var file in EnumerateFiles(target, ct))
            {
                // Read size — the file could be deleted between enumeration and
                // here; catch that and log as skipped rather than aborting.
                try
                {
                    var sz = file.Length;
                    context.Log.Info(
                        $"  Would delete  {file.Name}  ({FormatBytes(sz)})");
                    dirFiles++;
                    dirBytes += sz;
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or FileNotFoundException)
                {
                    context.Log.Warn($"  Skipped  {file.Name}  — {ex.Message}");
                    skipped++;
                }
            }

            totalFiles += dirFiles;
            totalBytes += dirBytes;

            if (!ct.IsCancellationRequested)
            {
                if (dirFiles > 0)
                    context.Log.Info(
                        $"  → {dirFiles} file(s), {FormatBytes(dirBytes)} to free.");
                else
                    context.Log.Info($"  → Nothing to clean.");
            }
        }

        sw.Stop();

        if (ct.IsCancellationRequested)
            return ActionExecutionResult.Cancelled(
                ActionId, "Preview cancelled.", sw.Elapsed, context.Log.Build());

        string msg;
        if (totalFiles == 0 && skipped == 0)
        {
            msg = "No temporary files found to clean.";
        }
        else
        {
            msg = $"Preview: {totalFiles} file(s), {FormatBytes(totalBytes)} would be freed.";
            if (skipped > 0)
                msg += $" {skipped} file(s) would be skipped (locked or inaccessible).";
        }

        context.Log.Info(msg);
        return ActionExecutionResult.DryRunCompleted(
            ActionId, msg, sw.Elapsed, context.Log.Build());
    }

    // ── Live cleanup ──────────────────────────────────────────────────────────

    private ActionExecutionResult RunCleanup(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw      = Stopwatch.StartNew();
        var staging = NewStagingRoot();

        context.Log.Info("Starting temporary file cleanup…");
        context.Log.Info($"Rollback staging: {staging}");

        // ── Create staging directory ──────────────────────────────────────────
        // If staging creation fails, fall back to permanent deletion with no
        // rollback. A warning is shown but the cleanup still proceeds.
        bool stagingReady = false;
        try
        {
            Directory.CreateDirectory(staging);
            stagingReady = true;
        }
        catch (Exception ex)
        {
            context.Log.Warn(
                $"Could not create rollback staging area: {ex.Message}  " +
                $"Files will be permanently deleted — rollback unavailable for this run.");
        }

        // ── Scan and clean each target directory ──────────────────────────────

        long         totalBytes = 0;
        int          totalFiles = 0;
        int          skipped    = 0;
        List<string> manifest   = [];   // "{stagingName}|{originalPath}" per line

        foreach (var target in s_targets)
        {
            if (ct.IsCancellationRequested) break;

            if (!Directory.Exists(target.Path))
            {
                context.Log.Warn(
                    $"Skipping {target.DisplayName} — directory not found.");
                continue;
            }

            var ageNote = target.MinAgeMinutes > 0
                ? $" (files idle ≥ {target.MinAgeMinutes} min)" : string.Empty;
            context.Log.Info($"Cleaning {target.DisplayName}{ageNote}…");

            int  dirFiles = 0;
            long dirBytes = 0;

            foreach (var file in EnumerateFiles(target, ct))
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var sz = file.Length;

                    if (stagingReady)
                    {
                        // Move to staging (same-drive = atomic rename, no data copy).
                        // Preserve the extension so the staged file is recognisable.
                        var stagingName = $"{Guid.NewGuid():N}{file.Extension}";
                        var stagingPath = Path.Combine(staging, stagingName);
                        File.Move(file.FullName, stagingPath);
                        manifest.Add($"{stagingName}|{file.FullName}");
                    }
                    else
                    {
                        // Staging unavailable — permanent delete.
                        File.Delete(file.FullName);
                    }

                    context.Log.Info(
                        $"  Removed  {file.Name}  ({FormatBytes(sz)})");
                    dirFiles++;
                    dirBytes += sz;
                    totalFiles++;
                    totalBytes += sz;
                }
                catch (Exception ex) when (
                    ex is IOException            or
                    UnauthorizedAccessException  or
                    FileNotFoundException)
                {
                    context.Log.Warn(
                        $"  Skipped  {file.Name}  — " +
                        $"{ex.GetType().Name}: {ex.Message}");
                    skipped++;
                }
            }

            if (dirFiles > 0)
                context.Log.Info(
                    $"  → {dirFiles} file(s) removed ({FormatBytes(dirBytes)}).");
            else if (!ct.IsCancellationRequested)
                context.Log.Info($"  → Nothing to clean.");
        }

        // ── Cancellation: restore staged files and return clean state ─────────
        if (ct.IsCancellationRequested)
        {
            sw.Stop();

            string cancelMsg;
            if (manifest.Count > 0)
            {
                context.Log.Warn(
                    $"Cancellation requested — restoring {manifest.Count} " +
                    $"file(s) moved before cancellation…");
                RestoreFromStaging(manifest, staging, context.Log);
                TryDeleteDirectory(staging, context.Log);
                cancelMsg =
                    $"Cancelled and fully rolled back — " +
                    $"{manifest.Count} file(s) restored to their original locations.";
            }
            else
            {
                TryDeleteDirectory(staging, context.Log);
                cancelMsg = "Cancelled before any files were moved.";
            }

            context.Log.Warn(cancelMsg);
            return ActionExecutionResult.Cancelled(
                ActionId, cancelMsg, sw.Elapsed, context.Log.Build());
        }

        // ── Write rollback manifest ───────────────────────────────────────────
        string? rollbackToken = null;
        if (stagingReady && manifest.Count > 0)
        {
            if (TryWriteManifest(staging, manifest, context.Log))
                rollbackToken = staging;
        }
        else if (stagingReady && totalFiles == 0)
        {
            // Nothing moved — clean up the empty staging directory.
            TryDeleteDirectory(staging, context.Log);
        }

        // ── Build result ──────────────────────────────────────────────────────
        sw.Stop();

        string msg;
        if (totalFiles == 0 && skipped == 0)
        {
            msg = "No temporary files found to clean.";
        }
        else
        {
            msg = $"{totalFiles} file(s) removed, {FormatBytes(totalBytes)} freed.";
            if (skipped > 0)
                msg += $" {skipped} file(s) skipped (in use or inaccessible).";
            if (rollbackToken is not null)
                msg += " Rollback available.";
        }

        context.Log.Info($"Done. {msg}");

        // Partial success when some files were cleaned but others were skipped.
        // Pass the rollback token so the user can restore the files that were moved.
        if (totalFiles > 0 && skipped > 0)
            return ActionExecutionResult.PartiallySucceeded(
                ActionId, msg, sw.Elapsed, context.Log.Build(),
                rollbackToken: rollbackToken);

        return ActionExecutionResult.Succeeded(
            ActionId, msg, sw.Elapsed, context.Log.Build(),
            rollbackToken: rollbackToken);
    }

    // ── IActionExecutor.RollbackAsync ─────────────────────────────────────────

    public Task<ActionExecutionResult> RollbackAsync(
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(
            () => RunRollback(rollbackToken, context, cancellationToken),
            cancellationToken);

    private ActionExecutionResult RunRollback(
        string                staging,
        ActionExecutionContext context,
        CancellationToken     ct)
    {
        var sw = Stopwatch.StartNew();
        context.Log.Info("Restoring temporary files from rollback staging…");
        context.Log.Info($"Staging: {staging}");

        if (!Directory.Exists(staging))
        {
            context.Log.Error(
                "Staging directory not found — files may have been purged already.");
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId,
                "Rollback staging not found. Files may have been purged.",
                sw.Elapsed, context.Log.Build());
        }

        var manifestPath = Path.Combine(staging, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            context.Log.Error(
                "Rollback manifest not found — cannot determine original file paths.");
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId,
                "Rollback manifest is missing — cannot restore files.",
                sw.Elapsed, context.Log.Build());
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(manifestPath);
        }
        catch (Exception ex)
        {
            context.Log.Error($"Could not read rollback manifest: {ex.Message}");
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, "Could not read rollback manifest.",
                sw.Elapsed, context.Log.Build(), ex.ToString());
        }

        context.Log.Info($"Restoring {lines.Length} file(s)…");

        int restored = 0;
        int failed   = 0;

        foreach (var line in lines)
        {
            if (ct.IsCancellationRequested) break;

            var sep = line.IndexOf('|');
            if (sep < 0) continue; // malformed line — skip

            var stagingName  = line[..sep];
            var originalPath = line[(sep + 1)..];
            var stagingFile  = Path.Combine(staging, stagingName);

            if (!File.Exists(stagingFile))
            {
                context.Log.Warn(
                    $"  Staging file missing: {stagingName} — skipped.");
                failed++;
                continue;
            }

            try
            {
                // Recreate the original directory if a cleanup already removed it.
                var originalDir = Path.GetDirectoryName(originalPath);
                if (originalDir is not null)
                    Directory.CreateDirectory(originalDir);

                // Do not overwrite: if the app has since created a new file at
                // the same path, preserve the new one rather than clobbering it.
                File.Move(stagingFile, originalPath, overwrite: false);
                context.Log.Info(
                    $"  Restored  {Path.GetFileName(originalPath)}");
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
            return ActionExecutionResult.Cancelled(
                ActionId,
                $"Rollback cancelled. {restored} file(s) restored before cancellation.",
                sw.Elapsed, context.Log.Build());
        }

        // Clean up staging when the rollback completed without errors.
        if (failed == 0)
        {
            context.Log.Info("Cleaning up staging area…");
            TryDeleteDirectory(staging, context.Log);
        }

        if (restored == 0 && failed > 0)
            return ActionExecutionResult.Failed(
                ActionId,
                $"Rollback failed — could not restore {failed} file(s).",
                sw.Elapsed, context.Log.Build());

        if (failed > 0)
            return ActionExecutionResult.PartiallySucceeded(
                ActionId,
                $"Partially restored: {restored} file(s) recovered, " +
                $"{failed} could not be restored.",
                sw.Elapsed, context.Log.Build());

        return ActionExecutionResult.Succeeded(
            ActionId,
            $"Rollback complete — {restored} file(s) restored.",
            sw.Elapsed, context.Log.Build());
    }

    // ── File enumeration ──────────────────────────────────────────────────────

    /// <summary>
    /// Breadth-first enumeration of every file in <paramref name="target"/>
    /// that satisfies the age constraint. Directories or files that cannot be
    /// read (access denied, locked, etc.) are silently skipped.
    ///
    /// Checks <paramref name="ct"/> before each directory level rather than
    /// throwing, so callers can react to cancellation without exception overhead.
    /// </summary>
    private static IEnumerable<FileInfo> EnumerateFiles(
        TempTarget target, CancellationToken ct)
    {
        if (!Directory.Exists(target.Path)) yield break;

        // Age filter: files modified more recently than the cutoff are skipped.
        var cutoff = DateTime.UtcNow.AddMinutes(-target.MinAgeMinutes);
        var queue  = new Queue<string>();
        queue.Enqueue(target.Path);

        while (queue.Count > 0)
        {
            if (ct.IsCancellationRequested) yield break;

            var dir = queue.Dequeue();

            // Enumerate files in this directory level.
            FileInfo[] files;
            try
            {
                files = new DirectoryInfo(dir).GetFiles();
            }
            catch (Exception ex) when (
                ex is IOException            or
                UnauthorizedAccessException  or
                DirectoryNotFoundException)
            {
                continue; // Skip directories we cannot read.
            }

            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) yield break;
                // Skip files that are too recent (only relevant for Windows\Temp).
                if (file.LastWriteTimeUtc > cutoff) continue;
                yield return file;
            }

            // Enqueue subdirectories for BFS traversal.
            try
            {
                foreach (var sub in Directory.GetDirectories(dir))
                    queue.Enqueue(sub);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                // Skip inaccessible subdirectory levels.
            }
        }
    }

    // ── Cancellation rollback helper ──────────────────────────────────────────

    /// <summary>
    /// Synchronously moves all files listed in <paramref name="manifest"/> from
    /// staging back to their original paths. Used to restore a clean system state
    /// when the user cancels a mid-run execution.
    /// </summary>
    private static void RestoreFromStaging(
        IReadOnlyList<string> manifest,
        string                staging,
        ActionExecutionLog    log)
    {
        foreach (var line in manifest)
        {
            var sep = line.IndexOf('|');
            if (sep < 0) continue;

            var stagingName  = line[..sep];
            var originalPath = line[(sep + 1)..];
            var stagingFile  = Path.Combine(staging, stagingName);

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

    // ── Manifest helpers ──────────────────────────────────────────────────────

    private static bool TryWriteManifest(
        string             staging,
        IReadOnlyList<string> lines,
        ActionExecutionLog log)
    {
        try
        {
            File.WriteAllLines(Path.Combine(staging, ManifestFileName), lines);
            return true;
        }
        catch (Exception ex)
        {
            log.Warn(
                $"Could not write rollback manifest: {ex.Message}  " +
                $"Rollback will not be available for this run.");
            return false;
        }
    }

    private static void TryDeleteDirectory(string path, ActionExecutionLog log)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            log.Warn($"Could not remove staging directory: {ex.Message}");
        }
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    /// <summary>Formats a raw byte count as a human-readable string.</summary>
    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1_024             => $"{bytes} B",
        < 1_048_576         => $"{bytes / 1_024.0:F1} KB",
        < 1_073_741_824     => $"{bytes / 1_048_576.0:F1} MB",
        _                   => $"{bytes / 1_073_741_824.0:F1} GB",
    };
}
