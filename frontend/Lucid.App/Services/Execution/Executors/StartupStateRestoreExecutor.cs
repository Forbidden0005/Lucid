using System.Diagnostics;
using System.Text.Json;
using Lucid.Services.Startup;

namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.startup.restore-startup-state" action.
///
/// Reads a <see cref="StartupBackupSnapshot"/> JSON file produced by
/// <see cref="StartupStateBackupExecutor"/> and restores each entry's
/// <c>StartupApproved</c> registry value back to the state it was in at
/// snapshot time.
///
/// Parameter (from <see cref="ActionExecutionContext.Parameters"/>):
///   "SnapshotPath" — absolute path to the .json snapshot file to restore.
///
/// Safety model:
///   • Before writing any registry values, the executor takes a fresh
///     snapshot of the current state and stores it as the rollback token.
///   • Restoration applies the raw binary value stored in the snapshot,
///     not a synthesised enabled/disabled flag — byte-for-byte fidelity.
///   • Each entry is applied independently; a failure on one entry does not
///     abort the rest (logged as PartialSuccess).
///   • SupportsDryRun = true: dry-run shows what would change entry-by-entry.
///   • Rollback re-writes the fresh snapshot taken before restore — it
///     reverts whatever the restore changed.
///
/// Privilege:
///   Standard — same as the backup. HKLM entries are skipped with a warning
///   unless the process is elevated.
/// </summary>
internal sealed class StartupStateRestoreExecutor : IActionExecutor
{
    // ── Parameter keys ────────────────────────────────────────────────────────

    public const string ParamSnapshotPath = "SnapshotPath";

    // ── Identity ──────────────────────────────────────────────────────────────

    public string               ActionId             => "action.startup.restore-startup-state";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => true;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => true;

    // ── Storage path ──────────────────────────────────────────────────────────

    private static string BackupDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lucid", "StartupBackups");

    private static string NewRollbackPath() =>
        Path.Combine(BackupDir,
            $"pre_restore_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.json");

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
    };

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IStartupManagementService _startup;

    public StartupStateRestoreExecutor(IStartupManagementService startup)
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

        // ── Resolve snapshot path ─────────────────────────────────────────────
        if (!context.Parameters.TryGetValue(ParamSnapshotPath, out var snapshotPath) ||
            string.IsNullOrWhiteSpace(snapshotPath))
        {
            var err = $"Missing parameter '{ParamSnapshotPath}'. " +
                      "Specify the path to a startup snapshot file.";
            context.Log.Error(err);
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, err, sw.Elapsed, context.Log.Build());
        }

        if (!File.Exists(snapshotPath))
        {
            var err = $"Snapshot file not found: {snapshotPath}";
            context.Log.Error(err);
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, err, sw.Elapsed, context.Log.Build());
        }

        // ── Load snapshot ─────────────────────────────────────────────────────
        StartupBackupSnapshot? snapshot;
        try
        {
            var json = File.ReadAllText(snapshotPath);
            snapshot = JsonSerializer.Deserialize<StartupBackupSnapshot>(json);
        }
        catch (Exception ex)
        {
            context.Log.Error($"Could not read snapshot: {ex.Message}");
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, $"Snapshot read failed: {ex.Message}",
                sw.Elapsed, context.Log.Build(), ex.ToString());
        }

        if (snapshot is null || snapshot.Entries.Count == 0)
        {
            var emptyMsg = "Snapshot is empty — nothing to restore.";
            context.Log.Warn(emptyMsg);
            sw.Stop();
            return ActionExecutionResult.Succeeded(
                ActionId, emptyMsg, sw.Elapsed, context.Log.Build());
        }

        context.Log.Info(
            $"Loaded snapshot from {snapshot.CreatedAt:g} " +
            $"({snapshot.Entries.Count} entries, machine: {snapshot.MachineName}).");

        // ── Dry-run ───────────────────────────────────────────────────────────
        if (context.IsDryRun)
        {
            context.Log.Info("Preview mode — no registry changes will be made.");
            int changesNeeded = 0;

            foreach (var entry in snapshot.Entries)
            {
                bool currentlyDisabled = _startup.IsDisabled(entry.Name, entry.Location);
                bool snapDisabled      = !entry.IsEnabled;

                if (currentlyDisabled == snapDisabled)
                {
                    context.Log.Info(
                        $"  {entry.Name} [{entry.Location}] — already in target state " +
                        $"({(entry.IsEnabled ? "enabled" : "disabled")}), no change.");
                }
                else
                {
                    context.Log.Info(
                        $"  {entry.Name} [{entry.Location}] — would change from " +
                        $"{(currentlyDisabled ? "disabled" : "enabled")} " +
                        $"to {(entry.IsEnabled ? "enabled" : "disabled")}.");
                    changesNeeded++;
                }
            }

            var previewMsg = changesNeeded == 0
                ? "All entries are already in the snapshot state — no changes needed."
                : $"Preview: {changesNeeded} entry/entries would be changed.";
            context.Log.Info(previewMsg);
            sw.Stop();
            return ActionExecutionResult.DryRunCompleted(
                ActionId, previewMsg, sw.Elapsed, context.Log.Build());
        }

        if (ct.IsCancellationRequested)
        {
            sw.Stop();
            return ActionExecutionResult.Cancelled(
                ActionId, "Cancelled before applying changes.",
                sw.Elapsed, context.Log.Build());
        }

        // ── Take pre-restore rollback snapshot ────────────────────────────────
        context.Log.Info("Capturing current startup state for rollback…");
        string? rollbackToken = TakePrerRestoreSnapshot(context.Log);
        if (rollbackToken is not null)
            context.Log.Info($"Pre-restore snapshot saved to: {rollbackToken}");
        else
            context.Log.Warn(
                "Could not save pre-restore snapshot — rollback will be unavailable.");

        // ── Apply snapshot ────────────────────────────────────────────────────
        context.Log.Info("Applying startup state…");
        int applied = 0;
        int skipped = 0;
        int failed  = 0;

        foreach (var entry in snapshot.Entries)
        {
            if (ct.IsCancellationRequested) break;

            // Skip HKLM entries if not elevated.
            if (entry.Location == StartupLocation.HklmRun && !context.IsElevated)
            {
                context.Log.Warn(
                    $"  Skipped '{entry.Name}' (HKLM) — not elevated.");
                skipped++;
                continue;
            }

            bool ok = _startup.RestoreApprovedRawValue(
                entry.Name, entry.Location, entry.ApprovedRawValue);

            if (ok)
            {
                context.Log.Info(
                    $"  Restored  {entry.Name} [{entry.Location}]  " +
                    $"→ {(entry.IsEnabled ? "enabled" : "disabled")}");
                applied++;
            }
            else
            {
                context.Log.Warn(
                    $"  Failed    {entry.Name} [{entry.Location}]");
                failed++;
            }
        }

        sw.Stop();

        if (ct.IsCancellationRequested)
        {
            var cancelMsg =
                $"Cancelled mid-restore. {applied} entries restored before cancellation.";
            context.Log.Warn(cancelMsg);
            return ActionExecutionResult.Cancelled(
                ActionId, cancelMsg, sw.Elapsed, context.Log.Build());
        }

        string msg;
        if (applied == 0 && failed == 0 && skipped > 0)
            msg = $"All entries skipped (elevation required for {skipped} HKLM entry/entries).";
        else if (applied == 0 && failed > 0)
            msg = $"Restore failed — could not apply any of {failed} entries.";
        else
        {
            msg = $"{applied} entry/entries restored to snapshot state.";
            if (failed > 0) msg += $" {failed} failed.";
            if (skipped > 0) msg += $" {skipped} skipped (elevation needed).";
        }

        context.Log.Info($"Done. {msg}");

        if (failed > 0 && applied > 0)
            return ActionExecutionResult.PartiallySucceeded(
                ActionId, msg, sw.Elapsed, context.Log.Build());

        if (failed > 0 && applied == 0)
            return ActionExecutionResult.Failed(
                ActionId, msg, sw.Elapsed, context.Log.Build());

        return ActionExecutionResult.Succeeded(
            ActionId, msg, sw.Elapsed, context.Log.Build(),
            rollbackToken: rollbackToken);
    }

    // ── IActionExecutor.RollbackAsync ─────────────────────────────────────────

    // Rollback = restore the pre-restore snapshot (which itself is a startup state snapshot).
    // This delegates to the same restore logic, but using the pre-restore file as the source.
    public Task<ActionExecutionResult> RollbackAsync(
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default)
    {
        // The rollback token is the path to the pre-restore snapshot file.
        // Inject it as the SnapshotPath parameter and re-run the restore.
        var rollbackContext = new ActionExecutionContext
        {
            IsDryRun            = false,
            IsElevated          = context.IsElevated,
            ConfirmationGranted = true, // rollback is always confirmed
            RequestedBy         = context.RequestedBy,
            Log                 = context.Log,
            Parameters          = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ParamSnapshotPath] = rollbackToken,
            },
        };

        return Task.Run(
            () => Run(rollbackContext, cancellationToken),
            cancellationToken);
    }

    // ── Pre-restore snapshot helper ───────────────────────────────────────────

    private string? TakePrerRestoreSnapshot(ActionExecutionLog log)
    {
        try
        {
            var entries = _startup.GetAllEntries();
            var snaps   = entries.Select(e => new StartupEntrySnapshot(
                e.Name,
                e.Command,
                e.ExecutableName,
                e.Location,
                e.IsEnabled,
                _startup.GetApprovedRawValue(e.Name, e.Location))).ToList();

            var snapshot = new StartupBackupSnapshot(
                DateTimeOffset.Now,
                Environment.MachineName,
                snaps.AsReadOnly());

            Directory.CreateDirectory(BackupDir);
            var path = NewRollbackPath();
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, s_jsonOptions));
            return path;
        }
        catch (Exception ex)
        {
            log.Warn($"Pre-restore snapshot failed: {ex.Message}");
            return null;
        }
    }
}
