using System.Diagnostics;
using ExplainMyPC.Services.Cleanup;

namespace ExplainMyPC.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.disk.clean-browser-cache" action.
///
/// Removes cached web content from the default user profiles of Google Chrome
/// and Microsoft Edge.  Browser caches regenerate automatically on next use,
/// so deletion is fully safe from a data-loss perspective.
///
/// Safety model:
///   • Before touching any cache directory, the executor checks whether the
///     browser process is running.  If Chrome or Edge is open, its cache
///     directories are skipped with a warning.  The run is still considered
///     PartialSuccess if at least one browser was cleaned.
///   • Files are permanently deleted (no staging / rollback) because cached
///     web content is ephemeral by nature — browsers expect it to disappear
///     and regenerate it seamlessly.
///   • SupportsRollback = false is enforced here.
///   • RequiresConfirmation = false — the operation is safe and reversible
///     by simply browsing; no confirmation prompt is needed.
///
/// Supported browsers:
///   • Google Chrome (chrome.exe)  — %LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache
///   • Microsoft Edge (msedge.exe) — %LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Cache
///
/// Privilege:
///   Standard — both cache paths live under the current user's profile.
/// </summary>
internal sealed class BrowserCacheCleanupExecutor : IActionExecutor
{
    // ── Identity ──────────────────────────────────────────────────────────────

    public string               ActionId             => "action.disk.clean-browser-cache";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => false;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => false;

    // ── Browser targets ───────────────────────────────────────────────────────

    private sealed record BrowserTarget(
        string DisplayName,
        string ProcessName,   // process exe name without extension, lower-case
        string CachePath);

    private static IReadOnlyList<BrowserTarget> BuildTargets()
    {
        var localApp = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        return new[]
        {
            new BrowserTarget(
                "Google Chrome",
                "chrome",
                Path.Combine(localApp, "Google", "Chrome",
                             "User Data", "Default", "Cache")),

            new BrowserTarget(
                "Microsoft Edge",
                "msedge",
                Path.Combine(localApp, "Microsoft", "Edge",
                             "User Data", "Default", "Cache")),
        };
    }

    private static readonly IReadOnlyList<BrowserTarget> s_targets = BuildTargets();

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
            "Scanning browser caches — preview mode, no changes will be made.");

        long totalBytes = 0;
        int  totalFiles = 0;
        int  skippedBrowsers = 0;

        foreach (var target in s_targets)
        {
            if (ct.IsCancellationRequested) break;

            if (!Directory.Exists(target.CachePath))
            {
                context.Log.Info($"  {target.DisplayName} — not installed or cache not found.");
                continue;
            }

            if (IsBrowserRunning(target.ProcessName))
            {
                context.Log.Warn(
                    $"  {target.DisplayName} — currently running. " +
                    $"Close the browser before cleaning its cache.");
                skippedBrowsers++;
                continue;
            }

            var (count, bytes) = CleanupScanner.Scan(target.CachePath, 0, ct);
            context.Log.Info(
                $"  {target.DisplayName} — {count} file(s), " +
                $"{CleanupScanner.FormatBytes(bytes)} cached.");
            totalFiles += count;
            totalBytes += bytes;
        }

        sw.Stop();

        if (ct.IsCancellationRequested)
            return ActionExecutionResult.Cancelled(
                ActionId, "Preview cancelled.", sw.Elapsed, context.Log.Build());

        var msg = totalFiles == 0 && skippedBrowsers == 0
            ? "No browser caches found."
            : totalFiles == 0 && skippedBrowsers > 0
                ? $"{skippedBrowsers} browser(s) skipped — close them and try again."
                : $"Preview: {totalFiles} file(s), {CleanupScanner.FormatBytes(totalBytes)} would be freed.";

        if (skippedBrowsers > 0 && totalFiles > 0)
            msg += $" {skippedBrowsers} browser(s) skipped (running).";

        context.Log.Info(msg);
        return ActionExecutionResult.DryRunCompleted(
            ActionId, msg, sw.Elapsed, context.Log.Build());
    }

    // ── Live cleanup ──────────────────────────────────────────────────────────

    private ActionExecutionResult RunCleanup(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        context.Log.Info("Starting browser cache cleanup…");

        long totalBytes     = 0;
        int  totalFiles     = 0;
        int  skipped        = 0;
        int  skippedBrowsers = 0;
        int  browsersClean  = 0;

        foreach (var target in s_targets)
        {
            if (ct.IsCancellationRequested) break;

            if (!Directory.Exists(target.CachePath))
            {
                context.Log.Info($"  {target.DisplayName} — not installed.");
                continue;
            }

            if (IsBrowserRunning(target.ProcessName))
            {
                context.Log.Warn(
                    $"  {target.DisplayName} is running — skipping cache. " +
                    $"Close the browser first and run this action again to clean it.");
                skippedBrowsers++;
                continue;
            }

            context.Log.Info($"Cleaning {target.DisplayName} cache…");

            int  dirFiles = 0;
            long dirBytes = 0;

            foreach (var file in CleanupScanner.EnumerateFiles(target.CachePath, 0, ct))
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var sz = file.Length;
                    File.Delete(file.FullName);
                    context.Log.Info(
                        $"  Removed  {file.Name}  ({CleanupScanner.FormatBytes(sz)})");
                    dirFiles++;
                    dirBytes += sz;
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

            if (dirFiles > 0)
            {
                context.Log.Info(
                    $"  → {dirFiles} file(s) removed ({CleanupScanner.FormatBytes(dirBytes)}).");
                browsersClean++;
            }
            else if (!ct.IsCancellationRequested)
            {
                context.Log.Info($"  → {target.DisplayName} cache was already empty.");
            }
        }

        sw.Stop();

        if (ct.IsCancellationRequested)
        {
            var cancelMsg = "Cancelled — no rollback needed (browser caches regenerate automatically).";
            context.Log.Warn(cancelMsg);
            return ActionExecutionResult.Cancelled(
                ActionId, cancelMsg, sw.Elapsed, context.Log.Build());
        }

        string msg;
        if (totalFiles == 0 && skippedBrowsers == 0 && skipped == 0)
            msg = "No browser caches found.";
        else if (totalFiles == 0 && skippedBrowsers > 0)
            msg = $"All browsers are currently running. Close them and try again.";
        else
        {
            msg = $"{totalFiles} file(s) removed, {CleanupScanner.FormatBytes(totalBytes)} freed.";
            if (skippedBrowsers > 0)
                msg += $" {skippedBrowsers} browser(s) skipped (still open).";
            if (skipped > 0)
                msg += $" {skipped} file(s) skipped (in use).";
        }

        context.Log.Info($"Done. {msg}");

        // Partial success if we cleaned at least one browser but skipped others.
        bool partial = (skippedBrowsers > 0 && totalFiles > 0) || (skipped > 0 && totalFiles > 0);
        if (partial)
            return ActionExecutionResult.PartiallySucceeded(
                ActionId, msg, sw.Elapsed, context.Log.Build());

        // All browsers were running — treat as failure so the user is prompted.
        if (totalFiles == 0 && skippedBrowsers > 0)
            return ActionExecutionResult.Failed(
                ActionId, msg, sw.Elapsed, context.Log.Build());

        return ActionExecutionResult.Succeeded(
            ActionId, msg, sw.Elapsed, context.Log.Build());
    }

    // ── IActionExecutor.RollbackAsync ─────────────────────────────────────────

    public Task<ActionExecutionResult> RollbackAsync(
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default)
    {
        context.Log.Error(
            "Browser cache cleanup does not support rollback. " +
            "Browser caches are ephemeral and regenerate automatically.");
        return Task.FromResult(ActionExecutionResult.Failed(
            ActionId, "This action does not support rollback.",
            TimeSpan.Zero, context.Log.Build()));
    }

    // ── Process detection ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when at least one process matching
    /// <paramref name="exeName"/> (without extension, case-insensitive)
    /// is currently running.
    /// </summary>
    private static bool IsBrowserRunning(string exeName)
    {
        try
        {
            return Process.GetProcessesByName(exeName).Length > 0;
        }
        catch
        {
            // GetProcessesByName can throw on access-denied in restricted
            // environments. Assume running for safety.
            return true;
        }
    }
}
