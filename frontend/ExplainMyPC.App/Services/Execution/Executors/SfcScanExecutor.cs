using System.Diagnostics;
using ExplainMyPC.Services.Repair;

namespace ExplainMyPC.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.repair.sfc-scannow" action.
///
/// Runs "sfc.exe /scannow" — the Windows System File Checker.
///
/// SFC scans every protected OS file against the trusted copy cached in the
/// WinSxS component store.  Corrupt or missing files are replaced silently.
/// The operation leaves a detailed log at:
///   %SystemRoot%\Logs\CBS\CBS.log
///
/// Safety model:
///   • Dry-run mode explains what SFC does and where its log is stored,
///     without running anything.
///   • Live output is streamed line-by-line; progress lines are sampled
///     (every 10 percent) to keep the log readable without losing fidelity.
///   • Cancellation kills the SFC process.  NOTE: SFC leaves a partial
///     scan in progress in this case — a fresh run will restart from the
///     beginning on the next boot.
///   • SupportsRollback = false: SFC replaces files with known-good system
///     versions.  The only "rollback" would be re-corrupting the file, which
///     is not a valid operation.
///
/// Privilege:
///   Administrator — SFC requires elevation to write to system directories.
///
/// Expected duration: 5–15 minutes.
/// </summary>
internal sealed class SfcScanExecutor : IActionExecutor
{
    // ── Identity ──────────────────────────────────────────────────────────────

    private const string Id = "action.repair.sfc-scannow";
    public string               ActionId             => Id;
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Administrator;
    public bool                 RequiresConfirmation => false;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => false;

    // ── Metadata (surfaced in UI before execution) ────────────────────────────

    public static RepairOperationMetadata Metadata =>
        RepairOperationCatalog.TryGet("action.repair.sfc-scannow")!;

    // ── IActionExecutor.ExecuteAsync ──────────────────────────────────────────

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(
            () => context.IsDryRun
                ? RunDryRun(context)
                : RunScan(context, cancellationToken),
            cancellationToken);

    // ── Dry-run ───────────────────────────────────────────────────────────────

    private static ActionExecutionResult RunDryRun(ActionExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        var m  = Metadata;

        context.Log.Info("System File Checker — preview mode.");
        context.Log.Info("");
        context.Log.Info($"What it does:  {m.HowItWorks}");
        context.Log.Info($"Expected time: {m.ExpectedDuration}");
        context.Log.Info($"Requires restart: {(m.RequiresRestart ? "Yes" : "No")}");
        context.Log.Info("");
        context.Log.Info("Command that would run:  sfc.exe /scannow");
        context.Log.Info("Log file:  %SystemRoot%\\Logs\\CBS\\CBS.log");
        context.Log.Info("");
        context.Log.Info(
            "To run, confirm the action on the repair card. " +
            "The scan will take several minutes and requires an active administrator session.");

        sw.Stop();
        return ActionExecutionResult.DryRunCompleted(
            Id,
            "Preview: SFC /scannow would scan and repair Windows system files.",
            sw.Elapsed, context.Log.Build());
    }

    // ── Live scan ─────────────────────────────────────────────────────────────

    private ActionExecutionResult RunScan(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        context.Log.Info("Starting System File Checker scan…");
        context.Log.Info("Command: sfc.exe /scannow");
        context.Log.Info("This will take several minutes — please do not close ExplainMyPC.");
        context.Log.Info("");

        // Progress tracking — log every 10 percent.
        int lastPctLogged = -1;
        var outputLines   = new List<string>();

        var result = ProcessExecutionHelper.Run(
            exe:       "sfc.exe",
            arguments: "/scannow",
            log:       context.Log,
            ct:        ct,
            progressLineCallback: line =>
            {
                // Capture every line for result classification.
                outputLines.Add(line);

                // Log progress updates at 10-percent intervals.
                if (CommandOutputParser.TryParseSfcProgress(line, out int pct))
                {
                    if (pct - lastPctLogged >= 10 || pct == 100)
                    {
                        context.Log.Info($"  Scan progress: {pct}%");
                        lastPctLogged = pct;
                    }
                }
            },
            // Suppress the raw progress lines — we log sampled progress above.
            lineFilter: line =>
                !CommandOutputParser.TryParseSfcProgress(line, out _) &&
                CommandOutputParser.IsInterestingLine(line));

        sw.Stop();

        // ── Cancellation ──────────────────────────────────────────────────────
        if (result.WasCancelled)
        {
            var cancelMsg =
                "SFC scan cancelled. The scan did not complete — run it again to " +
                "get a full result. No system changes were made before cancellation.";
            context.Log.Warn(cancelMsg);
            return ActionExecutionResult.Cancelled(
                ActionId, cancelMsg, sw.Elapsed, context.Log.Build());
        }

        // ── Classify result ───────────────────────────────────────────────────
        context.Log.Info("");
        context.Log.Info($"SFC finished in {ProcessExecutionHelper.FormatDuration(sw.Elapsed)}. " +
                         $"Exit code: {result.ExitCode}");

        var sfcResult = CommandOutputParser.ClassifySfcResult(result.ExitCode, outputLines);

        string msg;
        switch (sfcResult)
        {
            case SfcResult.NoViolationsFound:
                msg = "No integrity violations found — Windows system files are healthy.";
                context.Log.Info($"✓ {msg}");
                break;

            case SfcResult.RepairedSuccessfully:
                msg = "Corrupt files were found and successfully repaired by SFC.";
                context.Log.Info($"✓ {msg}");
                break;

            case SfcResult.UnrepairedCorruptionFound:
                msg = "SFC found corrupt files but could not repair them. " +
                      "Run DISM /RestoreHealth to repair the component store, " +
                      "then run SFC again.";
                context.Log.Warn($"⚠ {msg}");
                break;

            case SfcResult.PendingRestartRequired:
                msg = "SFC cannot run because a previous repair requires a restart. " +
                      "Restart Windows and run SFC again.";
                context.Log.Warn($"⚠ {msg}");
                break;

            default:
                msg = result.ExitCode == 0
                    ? "SFC completed. Check %SystemRoot%\\Logs\\CBS\\CBS.log for details."
                    : $"SFC exited with code {result.ExitCode}. " +
                      $"Check %SystemRoot%\\Logs\\CBS\\CBS.log for details.";
                context.Log.Info(msg);
                break;
        }

        context.Log.Info("Full log: %SystemRoot%\\Logs\\CBS\\CBS.log");

        // SFC found+repaired = Success. Unrepaired = PartialSuccess (partial fix).
        if (sfcResult == SfcResult.UnrepairedCorruptionFound ||
            sfcResult == SfcResult.PendingRestartRequired    ||
            (result.ExitCode != 0 && sfcResult == SfcResult.UnknownError))
        {
            return ActionExecutionResult.PartiallySucceeded(
                ActionId, msg, sw.Elapsed, context.Log.Build());
        }

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
            "SFC repairs system files to known-good versions. " +
            "There is no meaningful rollback for this operation.");
        return Task.FromResult(ActionExecutionResult.Failed(
            ActionId, "SFC does not support rollback.",
            TimeSpan.Zero, context.Log.Build()));
    }
}
