using System.Diagnostics;
using Lucid.Services.Cleanup;

namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.disk.clean-delivery-optimization" action.
///
/// Removes files from the Windows Delivery Optimization (DO) peer-to-peer
/// update cache.  DO stores downloaded update chunks here so they can be
/// shared with other PCs on the same network.  After updates are applied,
/// these chunks are no longer needed and can accumulate to several GB.
///
/// Safety model:
///   • The Delivery Optimization service (DoSvc) is stopped before scanning
///     and restarted unconditionally when cleanup finishes.
///   • Files are staged for rollback (atomic rename on the same drive).
///   • Locked or inaccessible files are skipped and reported.
///   • The service restart happens even if cleanup is cancelled or fails.
///
/// Privilege:
///   Administrator — the DO cache lives under
///   C:\Windows\ServiceProfiles\NetworkService\... which requires elevation
///   to access or modify, and stopping system services requires elevation.
/// </summary>
internal sealed class DeliveryOptimizationCacheExecutor : IActionExecutor
{
    // ── Identity ──────────────────────────────────────────────────────────────

    public string               ActionId             => "action.disk.clean-delivery-optimization";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Administrator;
    public bool                 RequiresConfirmation => true;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => true;

    // ── Target ────────────────────────────────────────────────────────────────

    // Primary cache path under the NetworkService profile.
    private static readonly string s_primaryCache = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "ServiceProfiles", "NetworkService",
        "AppData", "Local", "Microsoft", "Windows",
        "DeliveryOptimization", "Cache");

    // Secondary data path (metadata / chunk indices).
    private static readonly string s_dataCache = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "SoftwareDistribution", "DeliveryOptimization");

    private const string ServiceName   = "DoSvc";
    private const int    MinAgeMinutes = 0;

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
            "Scanning Delivery Optimization cache — preview mode, no changes will be made.");

        long totalBytes = 0;
        int  totalFiles = 0;

        foreach (var dir in new[] { s_primaryCache, s_dataCache })
        {
            if (!Directory.Exists(dir))
            {
                context.Log.Info($"  {dir} — not found, skipping.");
                continue;
            }

            var (count, bytes) = CleanupScanner.Scan(dir, MinAgeMinutes, ct);
            context.Log.Info(
                $"  {dir}  →  {count} file(s), {CleanupScanner.FormatBytes(bytes)}");
            totalFiles += count;
            totalBytes += bytes;
        }

        sw.Stop();

        if (ct.IsCancellationRequested)
            return ActionExecutionResult.Cancelled(
                ActionId, "Preview cancelled.", sw.Elapsed, context.Log.Build());

        var msg = totalFiles == 0
            ? "Delivery Optimization cache is already empty."
            : $"Preview: {totalFiles} file(s), {CleanupScanner.FormatBytes(totalBytes)} would be freed.";

        context.Log.Info(msg);
        return ActionExecutionResult.DryRunCompleted(
            ActionId, msg, sw.Elapsed, context.Log.Build());
    }

    // ── Live cleanup ──────────────────────────────────────────────────────────

    private ActionExecutionResult RunCleanup(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw      = Stopwatch.StartNew();
        var staging = CleanupScanner.NewStagingRoot("DeliveryOptimization");

        context.Log.Info("Starting Delivery Optimization cache cleanup…");
        context.Log.Info($"Rollback staging: {staging}");

        // ── Stop DoSvc ────────────────────────────────────────────────────────
        context.Log.Info($"Stopping {ServiceName} service…");
        if (!StopService(ServiceName, context.Log))
            context.Log.Warn(
                $"{ServiceName} did not confirm stopped. " +
                $"Some files may be locked and will be skipped.");

        // ── Create staging ────────────────────────────────────────────────────
        bool stagingReady = false;
        try
        {
            Directory.CreateDirectory(staging);
            stagingReady = true;
        }
        catch (Exception ex)
        {
            context.Log.Warn(
                $"Could not create staging area: {ex.Message}  " +
                $"Files will be permanently deleted — rollback unavailable.");
        }

        // ── Clean all target directories ──────────────────────────────────────
        long         totalBytes = 0;
        int          totalFiles = 0;
        int          skipped    = 0;
        List<string> manifest   = [];

        foreach (var dir in new[] { s_primaryCache, s_dataCache })
        {
            if (ct.IsCancellationRequested) break;

            if (!Directory.Exists(dir))
            {
                context.Log.Info($"  {dir} — not found, skipping.");
                continue;
            }

            context.Log.Info($"Cleaning {dir}…");

            foreach (var file in CleanupScanner.EnumerateFiles(dir, MinAgeMinutes, ct))
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var sz = file.Length;

                    if (stagingReady)
                    {
                        var stagingName = $"{Guid.NewGuid():N}{file.Extension}";
                        var stagingPath = Path.Combine(staging, stagingName);
                        File.Move(file.FullName, stagingPath);
                        manifest.Add($"{stagingName}|{file.FullName}");
                    }
                    else
                    {
                        File.Delete(file.FullName);
                    }

                    context.Log.Info(
                        $"  Removed  {file.Name}  ({CleanupScanner.FormatBytes(sz)})");
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
        }

        // ── Restart DoSvc (unconditional) ─────────────────────────────────────
        context.Log.Info($"Restarting {ServiceName} service…");
        StartService(ServiceName, context.Log);

        // ── Cancellation rollback ─────────────────────────────────────────────
        if (ct.IsCancellationRequested)
        {
            sw.Stop();
            string cancelMsg;
            if (manifest.Count > 0)
            {
                context.Log.Warn($"Cancellation — restoring {manifest.Count} file(s)…");
                CleanupScanner.RestoreFromStaging(manifest, staging, context.Log);
                CleanupScanner.TryDeleteDirectory(staging, context.Log);
                cancelMsg =
                    $"Cancelled and rolled back — {manifest.Count} file(s) restored.";
            }
            else
            {
                CleanupScanner.TryDeleteDirectory(staging, context.Log);
                cancelMsg = "Cancelled before any files were moved.";
            }
            context.Log.Warn(cancelMsg);
            return ActionExecutionResult.Cancelled(
                ActionId, cancelMsg, sw.Elapsed, context.Log.Build());
        }

        // ── Write manifest ────────────────────────────────────────────────────
        string? rollbackToken = null;
        if (stagingReady && manifest.Count > 0)
        {
            if (CleanupScanner.TryWriteManifest(staging, manifest, context.Log))
                rollbackToken = staging;
        }
        else if (stagingReady && totalFiles == 0)
        {
            CleanupScanner.TryDeleteDirectory(staging, context.Log);
        }

        sw.Stop();

        string msg;
        if (totalFiles == 0 && skipped == 0)
            msg = "Delivery Optimization cache was already empty.";
        else
        {
            msg =
                $"{totalFiles} file(s) removed, {CleanupScanner.FormatBytes(totalBytes)} freed.";
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

    // ── Service helpers ───────────────────────────────────────────────────────

    private static bool StopService(string name, ActionExecutionLog log)
    {
        try
        {
            RunSc($"stop {name}");
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
        catch (Exception ex) { log.Warn($"Could not restart {name}: {ex.Message}"); }
    }

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
        p.WaitForExit(10_000);
        return output;
    }
}
