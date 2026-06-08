using System.Diagnostics;
using Lucid.Services.Cleanup;

namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.disk.clean-windows-update-cache" action.
///
/// Removes stale files from C:\Windows\SoftwareDistribution\Download —
/// the staging area Windows Update uses to hold downloaded update packages
/// before installation. After a successful update cycle, these files are
/// no longer needed and can consume several gigabytes.
///
/// Safety model:
///   • The Windows Update service (wuauserv) is stopped before scanning and
///     restarted when cleanup finishes, preventing the service from writing
///     to files we are moving.
///   • Files are moved to a private rollback staging area (atomic rename on
///     the same drive) rather than deleted outright.
///   • Locked files are skipped with a log warning — a partial clean is
///     reported as PartialSuccess rather than failure.
///   • The service restart is unconditional; even if cleanup fails or is
///     cancelled, wuauserv is restarted so Windows Update keeps working.
///
/// Privilege:
///   Administrator — the SoftwareDistribution directory and the wuauserv
///   service both require elevation to modify.
/// </summary>
internal sealed class WindowsUpdateCacheExecutor : IActionExecutor
{
    private readonly IWindowsUpdateCacheRuntime _runtime;

    public WindowsUpdateCacheExecutor() : this(new DefaultWindowsUpdateCacheRuntime())
    {
    }

    internal WindowsUpdateCacheExecutor(IWindowsUpdateCacheRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    public string               ActionId             => "action.disk.clean-windows-update-cache";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Administrator;
    public bool                 RequiresConfirmation => true;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => true;

    // ── Target ────────────────────────────────────────────────────────────────

    private const string ServiceName = "wuauserv";
    private const int    MinAgeMinutes = 0; // All files are safe once service is stopped

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
        var cachePath = _runtime.GetCachePath();
        context.Log.Info(
            "Scanning Windows Update cache — preview mode, no changes will be made.");
        context.Log.Info($"Cache path: {cachePath}");

        if (!_runtime.DirectoryExists(cachePath))
        {
            var msg = "Windows Update cache directory not found.";
            context.Log.Warn(msg);
            sw.Stop();
            return ActionExecutionResult.DryRunCompleted(
                ActionId, msg, sw.Elapsed, context.Log.Build());
        }

        var (count, bytes) = _runtime.Scan(cachePath, MinAgeMinutes, ct);

        sw.Stop();

        if (ct.IsCancellationRequested)
            return ActionExecutionResult.Cancelled(
                ActionId, "Preview cancelled.", sw.Elapsed, context.Log.Build());

        var msg2 = count == 0
            ? "Windows Update cache is already empty."
            : $"Preview: {count} file(s), {CleanupScanner.FormatBytes(bytes)} would be freed.";

        context.Log.Info(msg2);
        return ActionExecutionResult.DryRunCompleted(
            ActionId, msg2, sw.Elapsed, context.Log.Build());
    }

    // ── Live cleanup ──────────────────────────────────────────────────────────

    private ActionExecutionResult RunCleanup(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw      = Stopwatch.StartNew();
        var cachePath = _runtime.GetCachePath();
        var staging = _runtime.NewStagingRoot();

        context.Log.Info("Starting Windows Update cache cleanup…");
        context.Log.Info($"Cache: {cachePath}");
        context.Log.Info($"Rollback staging: {staging}");

        if (!_runtime.DirectoryExists(cachePath))
        {
            context.Log.Warn("Windows Update cache directory not found — nothing to clean.");
            sw.Stop();
            return ActionExecutionResult.Succeeded(
                ActionId, "Windows Update cache directory not found.",
                sw.Elapsed, context.Log.Build());
        }

        // ── Stop Windows Update service ───────────────────────────────────────
        context.Log.Info($"Stopping {ServiceName} service…");
        if (!_runtime.TryStopService(ServiceName, context.Log))
        {
            context.Log.Warn(
                $"Could not confirm {ServiceName} stopped — proceeding anyway. " +
                $"Some files may be locked and will be skipped.");
        }

        // ── Create staging directory ──────────────────────────────────────────
        bool stagingReady = false;
        try
        {
            _runtime.CreateDirectory(staging);
            stagingReady = true;
        }
        catch (Exception ex)
        {
            context.Log.Warn(
                $"Could not create staging area: {ex.Message}  " +
                $"Files will be permanently deleted — rollback unavailable.");
        }

        // ── Scan and move files ───────────────────────────────────────────────
        long         totalBytes = 0;
        int          totalFiles = 0;
        int          skipped    = 0;
        List<string> manifest   = [];

        foreach (var file in _runtime.EnumerateFiles(cachePath, MinAgeMinutes, ct))
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var sz = file.Length;

                if (stagingReady)
                {
                    var stagingName = $"{Guid.NewGuid():N}{file.Extension}";
                    var stagingPath = Path.Combine(staging, stagingName);
                    _runtime.MoveFileToStaging(file.FullName, stagingPath);
                    manifest.Add($"{stagingName}|{file.FullName}");
                }
                else
                {
                    _runtime.DeleteFile(file.FullName);
                }

                context.Log.Info($"  Removed  {file.Name}  ({CleanupScanner.FormatBytes(sz)})");
                totalFiles++;
                totalBytes += sz;
            }
            catch (Exception ex) when (
                ex is IOException           or
                UnauthorizedAccessException or
                FileNotFoundException)
            {
                context.Log.Warn(
                    $"  Skipped  {file.Name}  — {ex.GetType().Name}: {ex.Message}");
                skipped++;
            }
        }

        // ── Restart Windows Update service (unconditional) ────────────────────
        context.Log.Info($"Restarting {ServiceName} service…");
        _runtime.StartService(ServiceName, context.Log);

        // ── Cancellation: restore staged files ────────────────────────────────
        if (ct.IsCancellationRequested)
        {
            sw.Stop();
            string cancelMsg;
            if (manifest.Count > 0)
            {
                context.Log.Warn(
                    $"Cancellation requested — restoring {manifest.Count} file(s)…");
                _runtime.RestoreFromStaging(manifest, staging, context.Log);
                _runtime.TryDeleteDirectory(staging, context.Log);
                cancelMsg =
                    $"Cancelled and rolled back — " +
                    $"{manifest.Count} file(s) restored.";
            }
            else
            {
                _runtime.TryDeleteDirectory(staging, context.Log);
                cancelMsg = "Cancelled before any files were moved.";
            }
            context.Log.Warn(cancelMsg);
            return ActionExecutionResult.Cancelled(
                ActionId, cancelMsg, sw.Elapsed, context.Log.Build());
        }

        // ── Write manifest, build result ──────────────────────────────────────
        string? rollbackToken = null;
        if (stagingReady && manifest.Count > 0)
        {
            if (_runtime.TryWriteManifest(staging, manifest, context.Log))
                rollbackToken = staging;
        }
        else if (stagingReady && totalFiles == 0)
        {
            _runtime.TryDeleteDirectory(staging, context.Log);
        }

        sw.Stop();

        string msg;
        if (totalFiles == 0 && skipped == 0)
            msg = "Windows Update cache was already empty.";
        else
        {
            msg = $"{totalFiles} file(s) removed, {CleanupScanner.FormatBytes(totalBytes)} freed.";
            if (skipped > 0) msg += $" {skipped} file(s) skipped (in use or locked).";
            if (rollbackToken is not null) msg += " Rollback available.";
        }

        context.Log.Info($"Done. {msg}");

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
            () => CleanupScanner.RunRollback(
                ActionId, rollbackToken, context, cancellationToken),
            cancellationToken);

    // ── Service control helpers ───────────────────────────────────────────────

    private static bool StopService(string name, ActionExecutionLog log)
    {
        try
        {
            var result = RunSc($"stop {name}");
            // Poll until stopped (max 30 s, 500 ms intervals)
            for (int i = 0; i < 60; i++)
            {
                Thread.Sleep(500);
                var query = RunSc($"query {name}");
                if (query.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            log.Warn($"Service {name} did not reach STOPPED state within 30 s.");
            return false;
        }
        catch (Exception ex)
        {
            log.Warn($"Error stopping service {name}: {ex.Message}");
            return false;
        }
    }

    private static void StartService(string name, ActionExecutionLog log)
    {
        try { RunSc($"start {name}"); }
        catch (Exception ex)
        {
            log.Warn($"Could not restart {name}: {ex.Message}");
        }
    }

    /// <summary>Runs sc.exe with the given arguments and returns stdout.</summary>
    private static string RunSc(string arguments)
    {
        var psi = new ProcessStartInfo("sc.exe", arguments)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(10_000); // 10 s max
        return output;
    }

    internal interface IWindowsUpdateCacheRuntime
    {
        string GetCachePath();
        string NewStagingRoot();
        bool DirectoryExists(string path);
        void CreateDirectory(string path);
        (int FileCount, long TotalBytes) Scan(string rootPath, int minAgeMinutes, CancellationToken ct);
        IEnumerable<FileInfo> EnumerateFiles(string rootPath, int minAgeMinutes, CancellationToken ct);
        void MoveFileToStaging(string sourcePath, string stagingPath);
        void DeleteFile(string path);
        bool TryStopService(string serviceName, ActionExecutionLog log);
        void StartService(string serviceName, ActionExecutionLog log);
        void RestoreFromStaging(IReadOnlyList<string> manifest, string stagingDir, ActionExecutionLog log);
        void TryDeleteDirectory(string stagingDir, ActionExecutionLog log);
        bool TryWriteManifest(string stagingDir, IReadOnlyList<string> manifest, ActionExecutionLog log);
    }

    private sealed class DefaultWindowsUpdateCacheRuntime : IWindowsUpdateCacheRuntime
    {
        public string GetCachePath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SoftwareDistribution", "Download");

        public string NewStagingRoot() => CleanupScanner.NewStagingRoot("WindowsUpdateCache");

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

        public void MoveFileToStaging(string sourcePath, string stagingPath) =>
            File.Move(sourcePath, stagingPath);

        public void DeleteFile(string path) => File.Delete(path);

        public bool TryStopService(string serviceName, ActionExecutionLog log) =>
            WindowsUpdateCacheExecutor.StopService(serviceName, log);

        public void StartService(string serviceName, ActionExecutionLog log) =>
            WindowsUpdateCacheExecutor.StartService(serviceName, log);

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
}
