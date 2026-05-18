using System.Diagnostics;
using System.Text.Json;
using ExplainMyPC.Services.Startup;

namespace ExplainMyPC.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.startup.enable-startup-app" action.
///
/// Re-enables a previously disabled startup entry by writing an "enabled"
/// binary value to the Windows <c>StartupApproved</c> registry key.
///
/// Parameters (from <see cref="ActionExecutionContext.Parameters"/>):
///   "StartupEntryName"     — the registry value name / shortcut filename
///   "StartupEntryLocation" — serialised <see cref="StartupLocation"/> enum
///
/// Safety model:
///   • Preserves the previous StartupApproved value so rollback can restore it.
///   • HKLM entries require elevation — checked at runtime for that location.
///   • SupportsDryRun = true.
///
/// Rollback:
///   Rollback re-writes the previous StartupApproved value, effectively
///   re-disabling the entry.
/// </summary>
internal sealed class StartupAppEnableExecutor : IActionExecutor
{
    // ── Parameter keys ────────────────────────────────────────────────────────

    public const string ParamEntryName     = StartupAppDisableExecutor.ParamEntryName;
    public const string ParamEntryLocation = StartupAppDisableExecutor.ParamEntryLocation;

    // ── Identity ──────────────────────────────────────────────────────────────

    public string               ActionId             => "action.startup.enable-startup-app";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => false;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => true;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IStartupManagementService _startup;

    public StartupAppEnableExecutor(IStartupManagementService startup)
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

        if (!TryParseParameters(context, out var entryName, out var location, out var parseError))
        {
            context.Log.Error(parseError);
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, parseError, sw.Elapsed, context.Log.Build());
        }

        if (location == StartupLocation.HklmRun && !context.IsElevated)
        {
            context.Log.Error(
                $"Enabling '{entryName}' from HKLM requires administrator privileges.");
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId,
                "HKLM startup entries require administrator elevation.",
                sw.Elapsed, context.Log.Build());
        }

        // ── Dry-run ───────────────────────────────────────────────────────────
        if (context.IsDryRun)
        {
            bool alreadyEnabled = !_startup.IsDisabled(entryName, location);
            var previewMsg = alreadyEnabled
                ? $"'{entryName}' is already enabled — no change needed."
                : $"Preview: would enable '{entryName}' startup entry ({location}).";
            context.Log.Info(previewMsg);
            sw.Stop();
            return ActionExecutionResult.DryRunCompleted(
                ActionId, previewMsg, sw.Elapsed, context.Log.Build());
        }

        // ── Live run ──────────────────────────────────────────────────────────
        context.Log.Info($"Enabling startup entry '{entryName}' ({location})…");

        var previousRaw = _startup.GetApprovedRawValue(entryName, location);
        context.Log.Info(
            previousRaw is null
                ? "  (No StartupApproved value exists — entry is already implicitly enabled.)"
                : $"  Previous StartupApproved value: {previousRaw}");

        if (!_startup.IsDisabled(entryName, location))
        {
            var alreadyMsg = $"'{entryName}' is already enabled.";
            context.Log.Info(alreadyMsg);
            sw.Stop();
            return ActionExecutionResult.Succeeded(
                ActionId, alreadyMsg, sw.Elapsed, context.Log.Build());
        }

        if (!_startup.EnableEntry(entryName, location))
        {
            var failMsg = $"Could not write to StartupApproved registry key for '{entryName}'.";
            context.Log.Error(failMsg);
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, failMsg, sw.Elapsed, context.Log.Build());
        }

        var doneMsg = $"'{entryName}' enabled. It will launch at next sign-in.";
        context.Log.Info(doneMsg);
        sw.Stop();

        var rollbackData  = new RollbackData(entryName, location.ToString(), previousRaw);
        var rollbackToken = JsonSerializer.Serialize(rollbackData);

        return ActionExecutionResult.Succeeded(
            ActionId, doneMsg, sw.Elapsed, context.Log.Build(),
            rollbackToken: rollbackToken);
    }

    // ── IActionExecutor.RollbackAsync ─────────────────────────────────────────

    public Task<ActionExecutionResult> RollbackAsync(
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(() => RunRollback(rollbackToken, context), cancellationToken);

    private ActionExecutionResult RunRollback(
        string rollbackToken, ActionExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        context.Log.Info("Rolling back startup app enable…");

        RollbackData? data;
        try { data = JsonSerializer.Deserialize<RollbackData>(rollbackToken); }
        catch (Exception ex)
        {
            context.Log.Error($"Could not parse rollback token: {ex.Message}");
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, "Rollback token is corrupt.", sw.Elapsed, context.Log.Build());
        }

        if (data is null || string.IsNullOrEmpty(data.EntryName))
        {
            context.Log.Error("Rollback token is empty or invalid.");
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, "Rollback token is invalid.", sw.Elapsed, context.Log.Build());
        }

        if (!Enum.TryParse<StartupLocation>(data.Location, out var location))
        {
            context.Log.Error($"Unknown startup location in rollback token: {data.Location}");
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, "Rollback token has unknown location.", sw.Elapsed, context.Log.Build());
        }

        context.Log.Info(
            $"Restoring '{data.EntryName}' ({location}) to previous disabled state…");

        if (!_startup.RestoreApprovedRawValue(data.EntryName, location, data.PreviousRaw))
        {
            var failMsg = $"Could not restore StartupApproved value for '{data.EntryName}'.";
            context.Log.Error(failMsg);
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, failMsg, sw.Elapsed, context.Log.Build());
        }

        var doneMsg = $"'{data.EntryName}' restored to previous disabled state.";
        context.Log.Info(doneMsg);
        sw.Stop();
        return ActionExecutionResult.Succeeded(
            ActionId, doneMsg, sw.Elapsed, context.Log.Build());
    }

    // ── Parameter parsing ─────────────────────────────────────────────────────

    private static bool TryParseParameters(
        ActionExecutionContext context,
        out string             entryName,
        out StartupLocation    location,
        out string             error)
    {
        entryName = string.Empty;
        location  = StartupLocation.HkcuRun;
        error     = string.Empty;

        if (!context.Parameters.TryGetValue(ParamEntryName, out var name) ||
            string.IsNullOrWhiteSpace(name))
        {
            error = $"Missing or empty parameter '{ParamEntryName}'.";
            return false;
        }

        entryName = name;

        if (!context.Parameters.TryGetValue(ParamEntryLocation, out var loc) ||
            !Enum.TryParse<StartupLocation>(loc, out location))
        {
            error = $"Missing or invalid parameter '{ParamEntryLocation}'. " +
                    "Valid values: HkcuRun, HklmRun, StartupFolder.";
            return false;
        }

        return true;
    }

    // ── Rollback data ─────────────────────────────────────────────────────────

    private sealed record RollbackData(
        string  EntryName,
        string  Location,
        string? PreviousRaw);
}
