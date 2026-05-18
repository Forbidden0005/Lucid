using System.Diagnostics;
using ExplainMyPC.Services.Repair;

namespace ExplainMyPC.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.apps.reset-windows-store" action.
///
/// Runs "wsreset.exe" to clear the Windows Store application cache.
///
/// wsreset.exe is a built-in Windows tool that:
///   1. Terminates the Store process if it is running.
///   2. Deletes the Store's local cache from %LocalAppData%\Packages\
///      Microsoft.WindowsStore_[id]\LocalState\Cache.
///   3. Re-launches the Windows Store.
///
/// It does NOT remove installed apps, purchases, subscriptions, or account data.
///
/// Safety model:
///   • Fully safe — only temporary cache files are removed.
///   • Standard privilege — no elevation required.
///   • SupportsRollback = false: cache is ephemeral and self-heals.
///   • wsreset does not write to stdout; success is inferred from exit code
///     and the fact that the Store window re-opens.
///   • Dry-run mode is provided for consistency even though the real run
///     takes under 30 seconds.
///
/// Note: wsreset launches the Store as a side effect.  This is standard Windows
/// behaviour and cannot be suppressed without calling internal APIs.  The Store
/// window can be closed immediately after the reset completes.
/// </summary>
internal sealed class WindowsStoreResetExecutor : IActionExecutor
{
    // ── Identity ──────────────────────────────────────────────────────────────

    private const string Id = "action.apps.reset-windows-store";
    public string               ActionId             => Id;
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => false;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => false;

    // ── Metadata ──────────────────────────────────────────────────────────────

    public static RepairOperationMetadata Metadata =>
        RepairOperationCatalog.TryGet("action.apps.reset-windows-store")!;

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

        context.Log.Info("Windows Store Cache Reset — preview mode.");
        context.Log.Info("");
        context.Log.Info($"What it does:  {m.Purpose}");
        context.Log.Info($"How:           {m.HowItWorks}");
        context.Log.Info($"Expected time: {m.ExpectedDuration}");
        context.Log.Info($"Reversible:    Yes — the Store rebuilds its cache automatically.");
        context.Log.Info("");
        context.Log.Info(
            "Note: The Windows Store will open automatically when wsreset finishes. " +
            "You can close it immediately.");
        context.Log.Info("");
        context.Log.Info("Command that would run:  wsreset.exe");

        sw.Stop();
        return ActionExecutionResult.DryRunCompleted(
            Id,
            "Preview: would clear the Windows Store cache and re-launch the Store.",
            sw.Elapsed, context.Log.Build());
    }

    // ── Live reset ────────────────────────────────────────────────────────────

    private ActionExecutionResult RunReset(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        context.Log.Info("Resetting Windows Store cache…");
        context.Log.Info("Command: wsreset.exe");
        context.Log.Info(
            "Note: The Windows Store window will open when the reset finishes. " +
            "You can close it.");

        // wsreset.exe does not write to stdout — it works silently and then opens
        // the Store window.  We launch it with UseShellExecute=true so it can
        // start the Store normally (it needs the shell to activate UWP).
        bool succeeded;
        try
        {
            var psi = new ProcessStartInfo("wsreset.exe")
            {
                UseShellExecute = true,
                CreateNoWindow  = false,
            };

            using var process = Process.Start(psi)!;

            // wsreset typically finishes in under 10 seconds, but we allow 60 s.
            bool exited = false;
            for (int i = 0; i < 120; i++) // 120 × 500ms = 60 s
            {
                if (ct.IsCancellationRequested)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    sw.Stop();
                    return ActionExecutionResult.Cancelled(
                        ActionId, "Windows Store reset cancelled.",
                        sw.Elapsed, context.Log.Build());
                }

                if (process.WaitForExit(500)) { exited = true; break; }
            }

            succeeded = exited && process.ExitCode == 0;

            if (!exited)
            {
                context.Log.Warn(
                    "wsreset.exe did not exit within 60 seconds. " +
                    "The reset may still be running in the background.");
                succeeded = false; // report as partial
            }
        }
        catch (Exception ex)
        {
            context.Log.Error($"Failed to start wsreset.exe: {ex.Message}");
            sw.Stop();
            return ActionExecutionResult.Failed(
                ActionId, $"Could not start wsreset: {ex.Message}",
                sw.Elapsed, context.Log.Build(), ex.ToString());
        }

        sw.Stop();

        var msg = succeeded
            ? "Windows Store cache cleared. The Store will rebuild it on next use."
            : "wsreset.exe completed but returned a non-zero exit code. " +
              "The Store cache may not have been fully cleared.";

        context.Log.Info(succeeded ? $"✓ {msg}" : $"⚠ {msg}");

        return succeeded
            ? ActionExecutionResult.Succeeded(ActionId, msg, sw.Elapsed, context.Log.Build())
            : ActionExecutionResult.PartiallySucceeded(ActionId, msg, sw.Elapsed, context.Log.Build());
    }

    // ── IActionExecutor.RollbackAsync ─────────────────────────────────────────

    public Task<ActionExecutionResult> RollbackAsync(
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default)
    {
        context.Log.Info(
            "No rollback needed — the Store cache rebuilds automatically.");
        return Task.FromResult(ActionExecutionResult.Succeeded(
            ActionId, "No rollback needed — Store cache self-heals.",
            TimeSpan.Zero, context.Log.Build()));
    }
}
