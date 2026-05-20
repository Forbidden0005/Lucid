using System.Diagnostics;
using ExplainMyPC.Services.Cleanup;

namespace ExplainMyPC.Services.Execution.Executors;

/// <summary>
/// Executor for "action.storage.delete-large-file".
///
/// Safely removes a single large file identified by the storage scanner.
/// The file is staged to the rollback area rather than permanently deleted,
/// so it can be restored if the user changes their mind.
///
/// Required context parameter: "FilePath" — the full path to delete.
///
/// Privilege: Standard (files in user profile) or may need admin for system files.
/// The executor attempts the operation and surfaces an access-denied error if
/// elevation would be needed.
/// </summary>
internal sealed class DeleteLargeFileExecutor : IActionExecutor
{
    public string               ActionId             => "action.storage.delete-large-file";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => true;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => true;

    public const string ParamFilePath = "FilePath";

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
        if (!ctx.Parameters.TryGetValue(ParamFilePath, out var path) ||
            string.IsNullOrWhiteSpace(path))
        {
            ctx.Log.Error("No file path provided.");
            return ActionExecutionResult.Failed("action.storage.delete-large-file",
                "No file path specified.", sw.Elapsed, ctx.Log.Build());
        }

        ctx.Log.Info($"Preview — would delete: {path}");

        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists)
            {
                ctx.Log.Warn("File not found — may have already been deleted.");
            }
            else
            {
                ctx.Log.Info($"  Size: {CleanupScanner.FormatBytes(fi.Length)}");
                ctx.Log.Info($"  Last modified: {fi.LastWriteTime:yyyy-MM-dd}");
                ctx.Log.Info("  File would be moved to rollback staging (recoverable).");
            }
        }
        catch (Exception ex)
        {
            ctx.Log.Warn($"  Could not inspect file: {ex.Message}");
        }

        sw.Stop();
        return ActionExecutionResult.DryRunCompleted(
            "action.storage.delete-large-file",
            $"Preview: {Path.GetFileName(path)} would be staged for deletion.",
            sw.Elapsed, ctx.Log.Build());
    }

    private static ActionExecutionResult RunDelete(
        ActionExecutionContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (!ctx.Parameters.TryGetValue(ParamFilePath, out var path) ||
            string.IsNullOrWhiteSpace(path))
        {
            ctx.Log.Error("No file path provided.");
            return ActionExecutionResult.Failed("action.storage.delete-large-file",
                "No file path specified.", sw.Elapsed, ctx.Log.Build());
        }

        ctx.Log.Info($"Staging large file for deletion: {path}");

        if (!File.Exists(path))
        {
            ctx.Log.Warn("File not found — may have already been deleted.");
            sw.Stop();
            return ActionExecutionResult.Succeeded(
                "action.storage.delete-large-file",
                "File was already gone — nothing to delete.",
                sw.Elapsed, ctx.Log.Build());
        }

        long size = 0;
        try { size = new FileInfo(path).Length; } catch { }

        var stagingRoot = CleanupScanner.NewStagingRoot("LargeFileDelete");
        string? rollbackToken = null;

        try
        {
            Directory.CreateDirectory(stagingRoot);
            var stagingName = $"{Guid.NewGuid():N}{Path.GetExtension(path)}";
            var stagingPath = Path.Combine(stagingRoot, stagingName);

            File.Move(path, stagingPath);

            var manifest = new[] { $"{stagingName}|{path}" };
            if (CleanupScanner.TryWriteManifest(stagingRoot, manifest, ctx.Log))
                rollbackToken = stagingRoot;

            ctx.Log.Info($"  Staged: {Path.GetFileName(path)} " +
                         $"({CleanupScanner.FormatBytes(size)})");
        }
        catch (Exception ex)
        {
            ctx.Log.Error($"Failed to stage file: {ex.Message}");
            sw.Stop();
            return ActionExecutionResult.Failed(
                "action.storage.delete-large-file",
                $"Could not delete {Path.GetFileName(path)}: {ex.Message}",
                sw.Elapsed, ctx.Log.Build(), ex.ToString());
        }

        sw.Stop();
        string msg = $"{Path.GetFileName(path)} removed " +
                     $"({CleanupScanner.FormatBytes(size)}).";
        if (rollbackToken is not null) msg += " Rollback available.";
        ctx.Log.Info(msg);

        return ActionExecutionResult.Succeeded(
            "action.storage.delete-large-file", msg,
            sw.Elapsed, ctx.Log.Build(), rollbackToken);
    }

    public Task<ActionExecutionResult> RollbackAsync(
        string rollbackToken, ActionExecutionContext context,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => CleanupScanner.RunRollback(
            "action.storage.delete-large-file", rollbackToken, context, cancellationToken),
            cancellationToken);
}
