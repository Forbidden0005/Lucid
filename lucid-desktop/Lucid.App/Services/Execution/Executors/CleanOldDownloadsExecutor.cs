using System.Diagnostics;
using Lucid.Services.Cleanup;

namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Executor for "action.storage.clean-old-downloads".
///
/// Scans the user's Downloads folder and stages files older than a configurable
/// threshold (default: 90 days) for deletion.  Supports rollback via staging manifest.
///
/// Context parameter: "MinAgeDays" — minimum file age in days (default 90).
/// </summary>
internal sealed class CleanOldDownloadsExecutor : IActionExecutor
{
    public string               ActionId             => "action.storage.clean-old-downloads";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => true;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => true;

    public const string ParamMinAgeDays = "MinAgeDays";
    private const int   DefaultMinAgeDays = 90;

    private static string DownloadsPath =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) +
        @"\Downloads";

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(
            () => context.IsDryRun
                ? RunDryRun(context, cancellationToken)
                : RunClean(context, cancellationToken),
            cancellationToken);

    private static ActionExecutionResult RunDryRun(
        ActionExecutionContext ctx, CancellationToken ct)
    {
        var sw      = Stopwatch.StartNew();
        int minDays = GetMinAge(ctx);
        var cutoff  = DateTime.UtcNow.AddDays(-minDays);

        ctx.Log.Info($"Old downloads preview — files older than {minDays} days.");
        ctx.Log.Info($"Downloads folder: {DownloadsPath}");

        long total = 0; int count = 0;
        foreach (var file in SafeEnumerate(DownloadsPath, ct))
        {
            if (file.LastWriteTimeUtc > cutoff) continue;
            try
            {
                total += file.Length;
                count++;
                ctx.Log.Info($"  {file.Name} — {CleanupScanner.FormatBytes(file.Length)} " +
                             $"— last modified {file.LastWriteTimeUtc:yyyy-MM-dd}");
            }
            catch { /* best-effort: file may vanish or deny access mid-scan — skip it in the dry-run listing */ }
        }

        sw.Stop();
        string msg = count == 0
            ? $"No downloads older than {minDays} days found."
            : $"Preview: {count} file(s) ({CleanupScanner.FormatBytes(total)}) would be staged.";
        ctx.Log.Info(msg);
        return ActionExecutionResult.DryRunCompleted(
            "action.storage.clean-old-downloads", msg, sw.Elapsed, ctx.Log.Build());
    }

    private static ActionExecutionResult RunClean(
        ActionExecutionContext ctx, CancellationToken ct)
    {
        var sw      = Stopwatch.StartNew();
        int minDays = GetMinAge(ctx);
        var cutoff  = DateTime.UtcNow.AddDays(-minDays);

        ctx.Log.Info($"Cleaning downloads older than {minDays} days…");

        var stagingRoot = CleanupScanner.NewStagingRoot("OldDownloads");
        Directory.CreateDirectory(stagingRoot);

        var manifest = new List<string>();
        int deleted = 0, skipped = 0;
        long totalBytes = 0;

        foreach (var file in SafeEnumerate(DownloadsPath, ct))
        {
            if (ct.IsCancellationRequested) break;
            if (file.LastWriteTimeUtc > cutoff) continue;

            try
            {
                long sz = 0;
                try { sz = file.Length; } catch { /* best-effort: size probe only — file may be locked or gone */ }

                var stagingName = $"{Guid.NewGuid():N}{file.Extension}";
                File.Move(file.FullName, Path.Combine(stagingRoot, stagingName));
                manifest.Add($"{stagingName}|{file.FullName}");
                totalBytes += sz;
                deleted++;
                ctx.Log.Info(
                    $"  Staged: {file.Name} ({CleanupScanner.FormatBytes(sz)})");
            }
            catch (Exception ex)
            {
                ctx.Log.Warn($"  Skipped {file.Name}: {ex.Message}");
                skipped++;
            }
        }

        if (ct.IsCancellationRequested && manifest.Count > 0)
        {
            CleanupScanner.RestoreFromStaging(manifest, stagingRoot, ctx.Log);
            CleanupScanner.TryDeleteDirectory(stagingRoot, ctx.Log);
            sw.Stop();
            return ActionExecutionResult.Cancelled(
                "action.storage.clean-old-downloads",
                "Cancelled and rolled back.", sw.Elapsed, ctx.Log.Build());
        }

        string? rollbackToken = null;
        if (manifest.Count > 0 &&
            CleanupScanner.TryWriteManifest(stagingRoot, manifest, ctx.Log))
            rollbackToken = stagingRoot;

        sw.Stop();
        string msg = deleted == 0
            ? "No old downloads found to clean."
            : $"{deleted} old download(s) staged ({CleanupScanner.FormatBytes(totalBytes)}).";
        if (skipped > 0) msg += $" {skipped} skipped.";
        if (rollbackToken is not null) msg += " Rollback available.";
        ctx.Log.Info(msg);

        return deleted > 0 && skipped > 0
            ? ActionExecutionResult.PartiallySucceeded(
                "action.storage.clean-old-downloads",
                msg, sw.Elapsed, ctx.Log.Build(), rollbackToken)
            : ActionExecutionResult.Succeeded(
                "action.storage.clean-old-downloads",
                msg, sw.Elapsed, ctx.Log.Build(), rollbackToken);
    }

    public Task<ActionExecutionResult> RollbackAsync(
        string rollbackToken, ActionExecutionContext context,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => CleanupScanner.RunRollback(
            "action.storage.clean-old-downloads",
            rollbackToken, context, cancellationToken),
            cancellationToken);

    private static int GetMinAge(ActionExecutionContext ctx) =>
        ctx.Parameters.TryGetValue(ParamMinAgeDays, out var raw) &&
        int.TryParse(raw, out var days) && days > 0
            ? days : DefaultMinAgeDays;

    private static IEnumerable<FileInfo> SafeEnumerate(
        string root, CancellationToken ct)
    {
        if (!Directory.Exists(root)) yield break;
        FileInfo[] files;
        try { files = new DirectoryInfo(root).GetFiles(); }
        catch { yield break; }
        foreach (var f in files)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return f;
        }
    }
}
