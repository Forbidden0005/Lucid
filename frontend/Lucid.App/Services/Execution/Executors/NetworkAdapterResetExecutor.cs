using System.Diagnostics;
using Lucid.Services.Repair;

namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.network.reset-adapter" action.
///
/// Runs a multi-step TCP/IP stack reset workflow:
///   Step 1 — netsh int ip reset    (resets IP settings to defaults)
///   Step 2 — netsh int tcp reset   (resets TCP parameters to defaults)
///   Step 3 — ipconfig /release     (releases the current DHCP IP lease)
///   Step 4 — ipconfig /renew       (requests a new DHCP lease)
///
/// When to use:
///   • "Limited connectivity" / "No internet, secured" — 169.x.x.x APIPA address.
///   • IP configuration is permanently stuck (static/DHCP confusion).
///   • VPN or network software left the stack in a broken state.
///   • DNS resolves but TCP connections fail consistently.
///
/// Safety model:
///   • RequiresConfirmation = true — custom DNS, static IPs, and VPN routes
///     will be cleared.
///   • Each step is logged with success/failure before the next begins.
///   • Cancellation halts the sequence after the current step completes.
///     The TCP/IP stack is not in a worse state if cancelled mid-sequence.
///   • SupportsRollback = false — TCP/IP settings are reset to defaults.
///     Manual reconfiguration of static IPs or custom DNS may be needed.
///   • ipconfig /release is skipped gracefully when no lease is active.
///
/// Privilege:
///   Administrator — netsh commands that modify stack settings require elevation.
/// </summary>
internal sealed class NetworkAdapterResetExecutor : IActionExecutor
{
    // ── Identity ──────────────────────────────────────────────────────────────

    private const string Id = "action.network.reset-adapter";
    public string               ActionId             => Id;
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Administrator;
    public bool                 RequiresConfirmation => true;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => false;

    // ── Metadata ──────────────────────────────────────────────────────────────

    public static RepairOperationMetadata Metadata =>
        RepairOperationCatalog.TryGet("action.network.reset-adapter")!;

    // ── Reset steps ───────────────────────────────────────────────────────────

    private sealed record ResetStep(
        string DisplayName,
        string Exe,
        string Arguments,
        bool   FailureIsNonFatal = false);

    private static readonly IReadOnlyList<ResetStep> s_steps =
    [
        new("Reset IP stack",     "netsh.exe",    "int ip reset",  FailureIsNonFatal: false),
        new("Reset TCP stack",    "netsh.exe",    "int tcp reset", FailureIsNonFatal: false),
        new("Release IP lease",   "ipconfig.exe", "/release",      FailureIsNonFatal: true),
        new("Renew IP lease",     "ipconfig.exe", "/renew",        FailureIsNonFatal: true),
    ];

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

        context.Log.Info("Network Stack Reset — preview mode.");
        context.Log.Info("");
        context.Log.Info($"What it does:  {m.Purpose}");
        context.Log.Info($"How:           {m.HowItWorks}");
        context.Log.Info($"Expected time: {m.ExpectedDuration}");
        context.Log.Info("");
        context.Log.Info("Steps that would run:");
        int i = 1;
        foreach (var step in s_steps)
        {
            context.Log.Info(
                $"  {i++}. {step.DisplayName}" +
                $"   ({step.Exe} {step.Arguments})" +
                (step.FailureIsNonFatal ? "   [continues on failure]" : string.Empty));
        }
        context.Log.Info("");
        context.Log.Info($"Note: {m.ReversibilityNote}");

        sw.Stop();
        return ActionExecutionResult.DryRunCompleted(
            Id,
            "Preview: 4-step TCP/IP stack reset would be performed.",
            sw.Elapsed, context.Log.Build());
    }

    // ── Live reset ────────────────────────────────────────────────────────────

    private ActionExecutionResult RunReset(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        context.Log.Info("Starting network stack reset…");
        context.Log.Warn(
            "⚠ Network connectivity will be temporarily interrupted during this process.");
        context.Log.Info("");

        int  stepsOk     = 0;
        int  stepsFailed = 0;

        for (int i = 0; i < s_steps.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                var cancelMsg =
                    $"Network reset cancelled after step {i} of {s_steps.Count}. " +
                    $"The changes made so far are in effect — a restart may help " +
                    $"if connectivity is disrupted.";
                context.Log.Warn(cancelMsg);
                return ActionExecutionResult.Cancelled(
                    ActionId, cancelMsg, sw.Elapsed, context.Log.Build());
            }

            var step = s_steps[i];
            context.Log.Info(
                $"Step {i + 1}/{s_steps.Count}: {step.DisplayName}" +
                $"  ({step.Exe} {step.Arguments})");

            var result = ProcessExecutionHelper.Run(
                exe:       step.Exe,
                arguments: step.Arguments,
                log:       context.Log,
                ct:        ct,
                lineFilter: CommandOutputParser.IsInterestingLine);

            bool stepOk = result.ExitCode == 0 && !result.WasCancelled;

            if (stepOk)
            {
                context.Log.Info($"  ✓ {step.DisplayName} completed.");
                stepsOk++;
            }
            else if (step.FailureIsNonFatal)
            {
                context.Log.Warn(
                    $"  ⚠ {step.DisplayName} returned exit code {result.ExitCode} " +
                    $"— continuing (non-fatal).");
                stepsOk++; // Non-fatal steps don't count as failures for the summary.
            }
            else
            {
                context.Log.Warn(
                    $"  ✗ {step.DisplayName} returned exit code {result.ExitCode}.");
                stepsFailed++;
            }

            context.Log.Info("");
        }

        sw.Stop();

        string msg;

        if (stepsFailed == 0)
        {
            msg =
                $"Network stack reset complete ({stepsOk}/{s_steps.Count} steps). " +
                $"Your IP lease has been renewed. " +
                $"If connectivity is still unavailable, restart Windows.";
            context.Log.Info($"✓ {msg}");
            return ActionExecutionResult.Succeeded(
                ActionId, msg, sw.Elapsed, context.Log.Build());
        }

        if (stepsOk > 0)
        {
            msg =
                $"Network stack partially reset ({stepsOk} OK, {stepsFailed} failed). " +
                $"Try restarting Windows to apply the changes that did succeed.";
            context.Log.Warn($"⚠ {msg}");
            return ActionExecutionResult.PartiallySucceeded(
                ActionId, msg, sw.Elapsed, context.Log.Build());
        }

        msg = "Network stack reset failed — all steps returned errors. " +
              "Check the log for details and try running each command manually as administrator.";
        context.Log.Error($"✗ {msg}");
        return ActionExecutionResult.Failed(
            ActionId, msg, sw.Elapsed, context.Log.Build());
    }

    // ── IActionExecutor.RollbackAsync ─────────────────────────────────────────

    public Task<ActionExecutionResult> RollbackAsync(
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default)
    {
        context.Log.Error(
            "Network stack reset does not support rollback. " +
            "TCP/IP settings have been restored to Windows defaults. " +
            "Reconfigure any static IPs, custom DNS, or VPN routes manually.");
        return Task.FromResult(ActionExecutionResult.Failed(
            ActionId, "Network stack reset does not support rollback.",
            TimeSpan.Zero, context.Log.Build()));
    }
}
