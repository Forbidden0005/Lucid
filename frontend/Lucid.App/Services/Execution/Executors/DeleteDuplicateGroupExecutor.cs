using System.Diagnostics;
using Lucid.Services.Cleanup;

namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Executor for "action.storage.delete-duplicate-group".
///
/// Removes all but the earliest-modified file in a duplicate group,
/// staging the deletions for rollback. The keep candidate is never touched.
///
/// Required context parameters:
///   "KeepPath"   — the full path of the file to keep.
///   "DeletePaths" — pipe-separated list of full paths to delete.
/// </summary>
internal sealed class DeleteDuplicateGroupExecutor : IActionExecutor
{
    public string               ActionId             => "action.storage.delete-duplicate-group";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => true;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => true;

    public const string ParamKeepPath    = "KeepPath";
    public const string ParamDeletePaths = "DeletePaths";

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(
            () => context.IsDryRun
                ? RunDryRun(context)
                : RunDelete(context, cancellationToken),
            cancellationToken);

    private static ActionExecutionResult RunDryRun(ActionExecutionContext ctx)
    {
        var sw = Stopwatch.StartNew();

        ctx.Parameters.TryGetValue(ParamKeepPath,    out var keepPath);
        ctx.Parameters.TryGetValue(ParamDeletePaths, out var deletePaths);
        var toDelete = (deletePaths ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries);

        ctx.Log.Info("Duplicate group cleanup — preview mode.");
        ctx.Log.Info($"  Keep:   {keepPath ?? "(not specified)"}");
        ctx.Log.Info($"  Delete: {toDelete.Length} file(s)");

        long totalWaste = 0;
        foreach (var path in toDelete)
        {
            try
            {
                var sz = new FileInfo(path).Length;
                totalWaste += sz;
                ctx.Log.Info($"    Would delete: {Path.GetFileName(path)} " +
                             $"({CleanupScanner.FormatBytes(sz)})");
            }
            catch { ctx.Log.Info($"    Would delete: {Path.GetFileName(path)}"); }
        }

        ctx.Log.Info($"  Total recoverable: {CleanupScanner.FormatBytes(totalWaste)}");
        sw.Stop();
        return ActionExecutionResult.DryRunCompleted(
            "action.storage.delete-duplicate-group",
            $"Preview: {toDelete.Length} duplicate(s) " +
            $"({CleanupScanner.FormatBytes(totalWaste)}) would be staged.",
            sw.Elapsed, ctx.Log.Build());
    }

    private static ActionExecutionResult RunDelete(
        ActionExecutionContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        ctx.Parameters.TryGetValue(ParamDeletePaths, out var deletePaths);
        var toDelete = (deletePaths ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries);

        if (toDelete.Length == 0)
        {
            ctx.Log.Warn("No files to delete.");
            return ActionExecutionResult.Succeeded(
                "action.storage.delete-duplicate-group",
                "Nothing to delete.", sw.Elapsed, ctx.Log.Build());
        }

        var stagingRoot = CleanupScanner.NewStagingRoot("DuplicateCleanup");
        Directory.CreateDirectory(stagingRoot);

        var manifest = new List<string>();
        int deleted  = 0, skipped = 0;
        long totalBytes = 0;

        foreach (var path in toDelete)
        {
            if (ct.IsCancellationRequested) break;
            if (!File.Exists(path)) { skipped++; continue; }

            try
            {
                long sz = 0;
                try { sz = new FileInfo(path).Length; } catch { }
                var stagingName = $"{Guid.NewGuid():N}{Path.GetExtension(path)}";
                File.Move(path, Path.Combine(stagingRoot, stagingName));
                manifest.Add($"{stagingName}|{path}");
                totalBytes += sz;
                deleted++;
                ctx.Log.Info($"  Staged: {Path.GetFileName(path)} " +
                             $"({CleanupScanner.FormatBytes(sz)})");
            }
            catch (Exception ex)
            {
                ctx.Log.Warn($"  Skipped {Path.GetFileName(path)}: {ex.Message}");
                skipped++;
            }
        }

        string? rollbackToken = null;
        if (manifest.Count > 0 &&
            CleanupScanner.TryWriteManifest(stagingRoot, manifest, ctx.Log))
            rollbackToken = stagingRoot;

        sw.Stop();
        string msg = $"{deleted} duplicate(s) removed " +
                     $"({CleanupScanner.FormatBytes(totalBytes)}).";
        if (skipped > 0) msg += $" {skipped} skipped.";
        if (rollbackToken is not null) msg += " Rollback available.";
        ctx.Log.Info(msg);

        return deleted > 0 && skipped > 0
            ? ActionExecutionResult.PartiallySucceeded(
                "action.storage.delete-duplicate-group",
                msg, sw.Elapsed, ctx.Log.Build(), rollbackToken)
            : ActionExecutionResult.Succeeded(
                "action.storage.delete-duplicate-group",
                msg, sw.Elapsed, ctx.Log.Build(), rollbackToken);
    }

    public Task<ActionExecutionResult> RollbackAsync(
        string rollbackToken, ActionExecutionContext context,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => CleanupScanner.RunRollback(
            "action.storage.delete-duplicate-group",
            rollbackToken, context, cancellationToken),
            cancellationToken);
}
