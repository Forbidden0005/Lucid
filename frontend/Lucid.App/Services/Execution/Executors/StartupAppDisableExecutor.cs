using System.Diagnostics;
using System.Text.Json;
using Lucid.Services.Startup;

namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.startup.disable-startup-app" action.
///
/// Disables a specific startup entry by writing to the Windows
/// <c>StartupApproved</c> registry mechanism — the same approach used by
/// Windows Task Manager and the Settings → Apps → Startup page.
/// The Run key entry is NOT removed; Windows simply ignores it at sign-in.
///
/// Parameters (from <see cref="ActionExecutionContext.Parameters"/>):
///   "StartupEntryName"     — the registry value name / shortcut file name
///   "StartupEntryLocation" — serialised <see cref="StartupLocation"/> enum
///                            e.g. "HkcuRun", "HklmRun", "StartupFolder"
///
/// Safety model:
///   • Reads and preserves the existing StartupApproved value for the entry
///     (or records its absence) so rollback can restore byte-for-byte.
///   • HKLM entries require elevation — the engine enforces this via
///     RequiredPrivilege when the location is HklmRun.  The executor checks
///     at runtime and fails gracefully if elevation is absent.
///   • SupportsDryRun = true: in dry-run mode the executor logs what it would
///     change without writing to the registry.
///
/// Rollback:
///   The rollback token is a JSON object containing the entry name, location,
///   and the previous raw StartupApproved value (or null if absent).
///   RollbackAsync restores exactly that previous value.
/// </summary>
internal sealed class StartupAppDisableExecutor : IActionExecutor
{
    // ── Parameter keys (callers use these as dictionary keys) ─────────────────

    public const string ParamEntryName     = "StartupEntryName";
    public const string ParamEntryLocation = "StartupEntryLocation";

    // ── Identity ──────────────────────────────────────────────────────────────

    public string               ActionId             => "action.startup.disable-startup-app";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => false;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => true;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IStartupManagementService _startup;

    public StartupAppDisableExecutor(IStartupManagementService startup)
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

        // HKLM entries require elevation; the engine checks RequiredPrivilege
        // for the declared level, but since this executor declares Standard
        // (so it can also handle HKCU entries), we must check elevation
        // ourselves for HKLM targets.
        if (location == StartupLocation.HklmRun && !context.IsElevated)
        {
            context.Log.Error(
                $"Disabling '{entryName}' from HKLM requires administrator privileges. " +
                "Re-launch Lucid as administrator and try again.");
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId,
                "HKLM startup entries require administrator elevation.",
                sw.Elapsed, context.Log.Build());
        }

        // ── Dry-run ───────────────────────────────────────────────────────────
        if (context.IsDryRun)
        {
            bool alreadyDisabled = _startup.IsDisabled(entryName, location);
            var previewMsg = alreadyDisabled
                ? $"'{entryName}' is already disabled — no change needed."
                : $"Preview: would disable '{entryName}' startup entry ({location}).";
            context.Log.Info(previewMsg);
            sw.Stop();
            return ActionExecutionResult.DryRunCompleted(
                ActionId, previewMsg, sw.Elapsed, context.Log.Build());
        }

        // ── Live run ──────────────────────────────────────────────────────────
        context.Log.Info($"Disabling startup entry '{entryName}' ({location})…");

        // Snapshot the current registry value for rollback.
        var previousRaw = _startup.GetApprovedRawValue(entryName, location);
        context.Log.Info(
            previousRaw is null
                ? "  (Entry has no existing StartupApproved value — was implicitly enabled.)"
                : $"  Previous StartupApproved value: {previousRaw}");

        // Check if already disabled.
        if (_startup.IsDisabled(entryName, location))
        {
            var alreadyMsg = $"'{entryName}' is already disabled.";
            context.Log.Info(alreadyMsg);
            sw.Stop();
            return ActionExecutionResult.Succeeded(
                ActionId, alreadyMsg, sw.Elapsed, context.Log.Build());
        }

        if (!_startup.DisableEntry(entryName, location))
        {
            var failMsg = $"Could not write to StartupApproved registry key for '{entryName}'.";
            context.Log.Error(failMsg);
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, failMsg, sw.Elapsed, context.Log.Build());
        }

        var doneMsg = $"'{entryName}' disabled. It will not launch at next sign-in.";
        context.Log.Info(doneMsg);
        sw.Stop();

        // Build rollback token.
        var rollbackData = new RollbackData(entryName, location.ToString(), previousRaw);
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
        context.Log.Info("Rolling back startup app disable…");

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
            $"Restoring '{data.EntryName}' ({location}) to previous state " +
            $"(was: {(data.PreviousRaw is null ? "implicitly enabled" : data.PreviousRaw)})…");

        if (!_startup.RestoreApprovedRawValue(data.EntryName, location, data.PreviousRaw))
        {
            var failMsg = $"Could not restore StartupApproved value for '{data.EntryName}'.";
            context.Log.Error(failMsg);
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, failMsg, sw.Elapsed, context.Log.Build());
        }

        var doneMsg = data.PreviousRaw is null
            ? $"'{data.EntryName}' re-enabled (StartupApproved entry removed)."
            : $"'{data.EntryName}' restored to previous state.";
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
            error = $"Missing or empty parameter '{ParamEntryName}'. " +
                    "Specify the startup entry name to disable.";
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

    // ── Rollback data model ───────────────────────────────────────────────────

    private sealed record RollbackData(
        string  EntryName,
        string  Location,
        string? PreviousRaw);
}
