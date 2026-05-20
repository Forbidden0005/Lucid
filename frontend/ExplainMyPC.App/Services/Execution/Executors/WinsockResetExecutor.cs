using System.Diagnostics;
using ExplainMyPC.Services.Repair;

namespace ExplainMyPC.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.network.winsock-reset" action.
///
/// Runs "netsh.exe winsock reset" to rebuild the Windows Sockets catalog.
///
/// ⚠ REQUIRES RESTART — the reset takes effect only after Windows reboots.
///
/// When to use:
///   The Winsock catalog can be corrupted by poorly-written VPNs, firewalls,
///   or antivirus tools that install Layered Service Provider (LSP) entries.
///   Symptoms: all applications fail to connect, browsers show "no internet",
///   ping works but TCP connections fail, or "Winsock provider catalog is corrupt"
///   errors in Event Viewer.
///
/// Safety model:
///   • RequiresConfirmation = true — this removes all third-party LSP entries,
///     which may affect VPN clients and security software.
///   • Dry-run explains the reset in plain English and lists the restart requirement.
///   • SupportsRollback = false — the catalog is reset to Windows defaults and
///     LSP entries are not preserved.
///   • A prominent restart-required warning is surfaced via RepairOperationMetadata.
///
/// Privilege:
///   Administrator — modifying the Winsock catalog requires elevation.
/// </summary>
internal sealed class WinsockResetExecutor : IActionExecutor
{
    // ── Identity ──────────────────────────────────────────────────────────────

    private const string Id = "action.network.winsock-reset";
    public string               ActionId             => Id;
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Administrator;
    public bool                 RequiresConfirmation => true;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => false;

    // ── Metadata ──────────────────────────────────────────────────────────────

    public static RepairOperationMetadata Metadata =>
        RepairOperationCatalog.TryGet("action.network.winsock-reset")!;

    // ── IActionExecutor.ExecuteAsync ──────────────────────────────────────────

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(
            () => context.IsDryRun
                ? RunDryRun(context)
                : RunReset(context, cancellationToken),
            cancellationToken);

    // ── Dry-run ───────────────────────────────────────────────────────────────

    private static ActionExecutionResult RunDryRun(ActionExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        var m  = Metadata;

        context.Log.Info("Winsock Catalog Reset — preview mode.");
        context.Log.Info("");
        context.Log.Info($"What it does:  {m.Purpose}");
        context.Log.Info($"How:           {m.HowItWorks}");
        context.Log.Info($"Expected time: {m.ExpectedDuration}");
        context.Log.Info("");
        context.Log.Info(
            "⚠ RESTART REQUIRED — the reset does not take effect until Windows restarts.");
        context.Log.Info("");
        context.Log.Info($"Note: {m.ReversibilityNote}");
        context.Log.Info("");
        context.Log.Info("Command that would run:  netsh.exe winsock reset");

        sw.Stop();
        return ActionExecutionResult.DryRunCompleted(
            Id,
            "Preview: would reset the Windows Sockets catalog (restart required).",
            sw.Elapsed, context.Log.Build());
    }

    // ── Live reset ────────────────────────────────────────────────────────────

    private ActionExecutionResult RunReset(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        context.Log.Info("Resetting Winsock catalog…");
        context.Log.Info("Command: netsh.exe winsock reset");
        context.Log.Info("");
        context.Log.Warn(
            "⚠ A restart will be required after this operation completes.");

        var outputLines = new List<string>();

        var result = ProcessExecutionHelper.Run(
            exe:       "netsh.exe",
            arguments: "winsock reset",
            log:       context.Log,
            ct:        ct,
            progressLineCallback: line => outputLines.Add(line),
            lineFilter: CommandOutputParser.IsInterestingLine);

        sw.Stop();

        if (result.WasCancelled)
            return ActionExecutionResult.Cancelled(
                ActionId, "Winsock reset cancelled.",
                sw.Elapsed, context.Log.Build());

        bool ok = CommandOutputParser.WinsockResetSucceeded(outputLines)
                  || result.ExitCode == 0;

        string msg;
        if (ok)
        {
            msg = "Winsock catalog reset successfully. " +
                  "Restart Windows now to apply the change — network will not work correctly until you do.";
            context.Log.Info($"✓ {msg}");
            context.Log.Warn("⚠ Please restart Windows before using the network.");
        }
        else
        {
            msg = $"netsh winsock reset exited with code {result.ExitCode}. " +
                  "The catalog may not have been fully reset.";
            context.Log.Warn($"⚠ {msg}");
        }

        return ok
            ? ActionExecutionResult.Succeeded(ActionId, msg, sw.Elapsed, context.Log.Build())
            : ActionExecutionResult.Failed(ActionId, msg, sw.Elapsed, context.Log.Build());
    }

    // ── IActionExecutor.RollbackAsync ─────────────────────────────────────────

    public Task<ActionExecutionResult> RollbackAsync(
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default)
    {
        context.Log.Error(
            "Winsock reset cannot be rolled back. The catalog has been restored " +
            "to Windows defaults. Re-install any VPN or network security software " +
            "that required custom LSP entries.");
        return Task.FromResult(ActionExecutionResult.Failed(
            ActionId, "Winsock reset does not support rollback.",
            TimeSpan.Zero, context.Log.Build()));
    }
}
