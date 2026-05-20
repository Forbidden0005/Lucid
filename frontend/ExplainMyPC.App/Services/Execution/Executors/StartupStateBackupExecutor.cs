using System.Diagnostics;
using System.Text.Json;
using ExplainMyPC.Services.Startup;

namespace ExplainMyPC.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.startup.backup-startup-state" action.
///
/// Takes a point-in-time snapshot of every startup entry (name, command,
/// location, enabled state, and the raw StartupApproved registry value) and
/// serialises it to a human-readable JSON file in:
///   <c>%LOCALAPPDATA%\ExplainMyPC\StartupBackups\{timestamp}.json</c>
///
/// Purpose:
///   • Provides a safety net before bulk startup operations.
///   • Snapshots are human-readable — users can inspect them.
///   • Consumed by <see cref="StartupStateRestoreExecutor"/> to roll back to
///     a known-good startup configuration.
///
/// Safety model:
///   • Read-only w.r.t. the startup configuration — no registry changes.
///   • The only write is a new JSON file in the app's private data directory.
///   • Rollback of this action = delete the snapshot file (the system was
///     not modified, so there is nothing to restore).
///   • SupportsDryRun = true: dry-run logs what would be captured.
///
/// Privilege:
///   Standard — reads only HKCU Run and the user's Startup folder.
///   HKLM Run entries visible to the user are included read-only.
/// </summary>
internal sealed class StartupStateBackupExecutor : IActionExecutor
{
    // ── Identity ──────────────────────────────────────────────────────────────

    public string               ActionId             => "action.startup.backup-startup-state";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => false;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => true; // rollback = delete the file

    // ── Storage path ──────────────────────────────────────────────────────────

    private static string BackupDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExplainMyPC", "StartupBackups");

    private static string NewBackupPath() =>
        Path.Combine(BackupDir,
            $"startup_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.json");

    // ── JSON options ──────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
    };

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IStartupManagementService _startup;

    public StartupStateBackupExecutor(IStartupManagementService startup)
        => _startup = startup;

    // ── IActionExecutor.ExecuteAsync ──────────────────────────────────────────

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(() => Run(context, cancellationToken), cancellationToken);

    private ActionExecutionResult Run(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        context.Log.Info(context.IsDryRun
            ? "Scanning startup entries — preview mode, no file will be written."
            : "Taking startup configuration snapshot…");

        // ── Collect entries ───────────────────────────────────────────────────
        var entries = _startup.GetAllEntries();
        context.Log.Info($"  Found {entries.Count} startup entr{(entries.Count == 1 ? "y" : "ies")}:");

        var snapshots = new List<StartupEntrySnapshot>(entries.Count);
        foreach (var e in entries)
        {
            var raw = _startup.GetApprovedRawValue(e.Name, e.Location);
            var snap = new StartupEntrySnapshot(
                e.Name,
                e.Command,
                e.ExecutableName,
                e.Location,
                e.IsEnabled,
                raw);

            context.Log.Info(
                $"    {(e.IsEnabled ? "✓" : "✗")}  {e.Name}  " +
                $"[{e.Location}]  {e.ExecutableName}");
            snapshots.Add(snap);
        }

        // ── Dry-run exit ──────────────────────────────────────────────────────
        if (context.IsDryRun)
        {
            var previewMsg =
                $"Preview: {snapshots.Count} startup entr{(snapshots.Count == 1 ? "y" : "ies")} " +
                $"would be saved to a snapshot file.";
            context.Log.Info(previewMsg);
            sw.Stop();
            return ActionExecutionResult.DryRunCompleted(
                ActionId, previewMsg, sw.Elapsed, context.Log.Build());
        }

        if (ct.IsCancellationRequested)
        {
            sw.Stop();
            return ActionExecutionResult.Cancelled(
                ActionId, "Cancelled before writing snapshot.",
                sw.Elapsed, context.Log.Build());
        }

        // ── Write snapshot file ───────────────────────────────────────────────
        var snapshot = new StartupBackupSnapshot(
            DateTimeOffset.Now,
            Environment.MachineName,
            snapshots.AsReadOnly());

        var path = NewBackupPath();
        context.Log.Info($"Writing snapshot to: {path}");

        try
        {
            Directory.CreateDirectory(BackupDir);
            var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            context.Log.Error($"Could not write snapshot file: {ex.Message}");
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, $"Snapshot write failed: {ex.Message}",
                sw.Elapsed, context.Log.Build(), ex.ToString());
        }

        var doneMsg =
            $"Startup snapshot saved — {snapshots.Count} " +
            $"entr{(snapshots.Count == 1 ? "y" : "ies")} captured.";
        context.Log.Info(doneMsg);
        sw.Stop();

        // Rollback token = path of the snapshot file (rollback deletes it).
        return ActionExecutionResult.Succeeded(
            ActionId, doneMsg, sw.Elapsed, context.Log.Build(),
            rollbackToken: path);
    }

    // ── IActionExecutor.RollbackAsync ─────────────────────────────────────────

    // Rollback of the backup operation = delete the snapshot file.
    // The system startup configuration was never modified.
    public Task<ActionExecutionResult> RollbackAsync(
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            context.Log.Info("Rolling back startup snapshot — deleting snapshot file…");

            if (!File.Exists(rollbackToken))
            {
                var msg = "Snapshot file not found — nothing to delete.";
                context.Log.Warn(msg);
                sw.Stop();
                return ActionExecutionResult.Succeeded(
                    ActionId, msg, sw.Elapsed, context.Log.Build());
            }

            try
            {
                File.Delete(rollbackToken);
                var doneMsg = $"Snapshot file deleted: {rollbackToken}";
                context.Log.Info(doneMsg);
                sw.Stop();
                return ActionExecutionResult.Succeeded(
                    ActionId, doneMsg, sw.Elapsed, context.Log.Build());
            }
            catch (Exception ex)
            {
                context.Log.Error($"Could not delete snapshot: {ex.Message}");
                sw.Stop();
                return ActionExecutionResult.Failed(
                    ActionId, $"Could not delete snapshot file: {ex.Message}",
                    sw.Elapsed, context.Log.Build());
            }
        }, cancellationToken);
}
