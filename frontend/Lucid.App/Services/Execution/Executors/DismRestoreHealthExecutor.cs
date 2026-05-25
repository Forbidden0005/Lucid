using System.Diagnostics;
using Lucid.Services.Repair;

namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.repair.dism-restore-health" action.
///
/// Runs "DISM.exe /Online /Cleanup-Image /RestoreHealth".
///
/// DISM repairs the Windows component store (WinSxS) — the authoritative
/// source of system files that SFC uses when replacing corrupt files.
/// When SFC reports "found corrupt files but was unable to fix some of them",
/// the component store itself is damaged; DISM must repair it first.
///
/// DISM downloads replacement components from Windows Update, so an internet
/// connection is required unless a local source is specified (not supported
/// by this executor — power users can use the DISM command line directly).
///
/// Safety model:
///   • Dry-run explains the tool, requirements (internet + elevation), and
///     expected duration without running anything.
///   • Progress lines ("[=====  ] 45.2%") are sampled at 5-percent intervals
///     to avoid flooding the log; all other output is streamed normally.
///   • Cancellation kills DISM and its sub-processes.  DISM is safe to cancel
///     mid-run — the component store will be in a partial-repair state, but
///     re-running DISM will pick up where it left off.
///   • SupportsRollback = false: component store repairs cannot be meaningfully
///     reversed.
///
/// Privilege:
///   Administrator — DISM requires elevation.
///
/// Expected duration: 10–30 minutes (depends on internet speed + store damage).
/// </summary>
internal sealed class DismRestoreHealthExecutor : IActionExecutor
{
    // ── Identity ──────────────────────────────────────────────────────────────

    private const string Id = "action.repair.dism-restore-health";
    public string               ActionId             => Id;
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Administrator;
    public bool                 RequiresConfirmation => false;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => false;

    // ── Metadata ──────────────────────────────────────────────────────────────

    public static RepairOperationMetadata Metadata =>
        RepairOperationCatalog.TryGet("action.repair.dism-restore-health")!;

    // ── IActionExecutor.ExecuteAsync ──────────────────────────────────────────

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(
            () => context.IsDryRun
                ? RunDryRun(context)
                : RunRepair(context, cancellationToken),
            cancellationToken);

    // ── Dry-run ───────────────────────────────────────────────────────────────

    private static ActionExecutionResult RunDryRun(ActionExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        var m  = Metadata;

        context.Log.Info("DISM Component Store Repair — preview mode.");
        context.Log.Info("");
        context.Log.Info($"What it does:  {m.HowItWorks}");
        context.Log.Info($"Expected time: {m.ExpectedDuration}");
        context.Log.Info($"Requires restart: {(m.RequiresRestart ? "Yes" : "No")}");
        context.Log.Info("");
        context.Log.Info(
            "Requirements:" +
            "\n  • Active internet connection (downloads from Windows Update)" +
            "\n  • Administrator privileges (already confirmed)");
        context.Log.Info("");
        context.Log.Info(
            "Command that would run:" +
            "\n  DISM.exe /Online /Cleanup-Image /RestoreHealth");
        context.Log.Info("Log file:  %SystemRoot%\\Logs\\DISM\\dism.log");
        context.Log.Info("");
        context.Log.Info(
            "Tip: Run SFC /scannow first. If SFC reports unfixable corruption, " +
            "run this tool and then run SFC again.");

        sw.Stop();
        return ActionExecutionResult.DryRunCompleted(
            Id,
            "Preview: DISM would scan and repair the Windows component store.",
            sw.Elapsed, context.Log.Build());
    }

    // ── Live repair ───────────────────────────────────────────────────────────

    private ActionExecutionResult RunRepair(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        context.Log.Info("Starting DISM component store repair…");
        context.Log.Info("Command: DISM.exe /Online /Cleanup-Image /RestoreHealth");
        context.Log.Info(
            "This may take 10–30 minutes depending on your internet speed. " +
            "Please keep Lucid open.");
        context.Log.Info("");

        // Progress sampling — log a line every 5%.
        float lastPctLogged = -1f;
        var   outputLines   = new List<string>();

        var result = ProcessExecutionHelper.Run(
            exe:       "DISM.exe",
            arguments: "/Online /Cleanup-Image /RestoreHealth",
            log:       context.Log,
            ct:        ct,
            progressLineCallback: line =>
            {
                outputLines.Add(line);

                if (CommandOutputParser.TryParseDismProgress(line, out float pct))
                {
                    if (pct - lastPctLogged >= 5f || pct >= 99.9f)
                    {
                        context.Log.Info($"  Progress: {pct:F0}%");
                        lastPctLogged = pct;
                    }
                }
            },
            // Suppress raw DISM progress bars; keep all other lines.
            lineFilter: line =>
                !CommandOutputParser.IsDismProgressLine(line) &&
                CommandOutputParser.IsInterestingLine(line));

        sw.Stop();

        // ── Cancellation ──────────────────────────────────────────────────────
        if (result.WasCancelled)
        {
            var cancelMsg =
                "DISM repair cancelled. The component store may be in a partial state. " +
                "Re-run DISM to resume. No permanent damage was caused.";
            context.Log.Warn(cancelMsg);
            return ActionExecutionResult.Cancelled(
                ActionId, cancelMsg, sw.Elapsed, context.Log.Build());
        }

        // ── Classify result ───────────────────────────────────────────────────
        context.Log.Info("");
        context.Log.Info(
            $"DISM finished in {ProcessExecutionHelper.FormatDuration(sw.Elapsed)}. " +
            $"Exit code: {result.ExitCode}");

        var dismResult = CommandOutputParser.ClassifyDismResult(result.ExitCode, outputLines);

        string msg;
        bool   succeeded;

        switch (dismResult)
        {
            case DismResult.RestoredSuccessfully:
                msg       = "Component store repair completed successfully. Run SFC /scannow next.";
                succeeded = true;
                context.Log.Info($"✓ {msg}");
                break;

            case DismResult.NoCorruptionFound:
                msg       = "No component store corruption detected — the store is healthy.";
                succeeded = true;
                context.Log.Info($"✓ {msg}");
                break;

            case DismResult.SourceNotFound:
                msg       = "DISM could not download repair files from Windows Update. " +
                            "Check your internet connection and try again, or use a local " +
                            "Windows ISO as a source with /Source parameter.";
                succeeded = false;
                context.Log.Warn($"⚠ {msg}");
                break;

            case DismResult.CannotRepair:
                msg       = "DISM could not repair the component store. " +
                            "Consider running 'DISM /Online /Cleanup-Image /CheckHealth' " +
                            "for a detailed assessment, or an in-place Windows upgrade.";
                succeeded = false;
                context.Log.Error($"✗ {msg}");
                break;

            case DismResult.RestartRequired:
                msg       = "DISM repair complete. A restart is required for changes to take effect.";
                succeeded = true;
                context.Log.Warn($"⚠ {msg}");
                break;

            default:
                msg       = result.ExitCode == 0
                    ? "DISM completed. Check %SystemRoot%\\Logs\\DISM\\dism.log for details."
                    : $"DISM exited with code {result.ExitCode}. Check %SystemRoot%\\Logs\\DISM\\dism.log.";
                succeeded = result.ExitCode == 0;
                context.Log.Info(msg);
                break;
        }

        context.Log.Info("Full log: %SystemRoot%\\Logs\\DISM\\dism.log");

        return succeeded
            ? ActionExecutionResult.Succeeded(ActionId, msg, sw.Elapsed, context.Log.Build())
            : ActionExecutionResult.Failed(ActionId, msg, sw.Elapsed, context.Log.Build());
    }

    // ── IActionExecutor.RollbackAsync ─────────────────────────────────────────

    public Task<ActionExecutionResult> RollbackAsync(
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default)
    {
        context.Log.Error("DISM component store repairs cannot be rolled back.");
        return Task.FromResult(ActionExecutionResult.Failed(
            ActionId, "DISM does not support rollback.",
            TimeSpan.Zero, context.Log.Build()));
    }
}
