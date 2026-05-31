using System.Diagnostics;
using ExplainMyPC.Services.Execution.Validation;
using ExplainMyPC.Services.ProcessIntel;

namespace ExplainMyPC.Services.Execution.Executors;

/// <summary>
/// Executor for "action.process.terminate".
///
/// Terminates a process by PID after confirming it is not system-critical.
/// Requires explicit user confirmation (RequiresConfirmation = true).
///
/// Required parameter: "ProcessId" — string representation of the PID.
/// Optional parameter: "ProcessName" — for display purposes only.
///
/// Safety:
///   • Critical Windows processes (csrss, lsass, winlogon, etc.) are always refused.
///     The critical check is performed on the OS-verified process name, NOT the
///     caller-supplied name, to prevent PID-reuse attacks.
///   • OS identity is verified via ProcessIdentityValidator before any kill is issued.
///   • A dry-run explains what would happen without terminating anything.
/// </summary>
internal sealed class TerminateProcessExecutor : IActionExecutor
{
    public string               ActionId             => "action.process.terminate";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => true;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => false;

    public const string ParamProcessId   = "ProcessId";
    public const string ParamProcessName = "ProcessName";

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(() => context.IsDryRun
            ? RunDryRun(context)
            : RunTerminate(context), cancellationToken);

    private static ActionExecutionResult RunDryRun(ActionExecutionContext ctx)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (!TryGetPid(ctx, out int pid, out string name))
            return Fail("action.process.terminate", "No process ID specified.", sw, ctx);

        // NOTE: No critical-process check here against the caller-supplied name.
        // The caller-supplied ProcessName cannot be trusted — a caller could pass
        // ProcessName="svchost" with a PID that actually belongs to a different process
        // (including a critical one). Dry-run never terminates anything, so this check
        // provides false assurance rather than real safety. The authoritative
        // OS-verified critical check runs in RunTerminate() after identity validation.

        ctx.Log.Info($"Preview — would terminate: {name} (PID {pid})");
        ctx.Log.Info("  This terminates the process immediately. Unsaved work in that app will be lost.");
        ctx.Log.Info("  No rollback is possible — the process must be restarted manually.");
        ctx.Log.Info("  Identity and critical-process verification will occur at actual execution time.");

        sw.Stop();
        return ActionExecutionResult.DryRunCompleted(
            "action.process.terminate",
            $"Preview: {name} (PID {pid}) would be terminated.", sw.Elapsed, ctx.Log.Build());
    }

    private static ActionExecutionResult RunTerminate(ActionExecutionContext ctx)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (!TryGetPid(ctx, out int pid, out string name))
            return Fail("action.process.terminate", "No process ID specified.", sw, ctx);

        // NOTE: No critical-process check here against the caller-supplied name.
        // The caller-supplied ProcessName is not verified against the OS at this point —
        // a caller could pass ProcessName="notepad" with a PID that belongs to lsass or
        // another critical process. Checking the caller-supplied name here only creates
        // a false sense of security. The real safety gate is below, after OS-level
        // identity verification, where we check the OS-verified name.

        // ── OS identity verification ───────────────────────────────────────────
        // Verify the PID still belongs to the expected process before killing.
        // Windows PIDs are recycled — the PID could belong to a different process
        // by the time this executor runs. Abort if they don't match.
        var verified = ProcessIdentityValidator.TryVerify(pid, name);

        if (verified is null)
        {
            ctx.Log.Warn($"  Process PID {pid} no longer exists — it may have already exited.");
            sw.Stop();
            return ActionExecutionResult.Succeeded(
                "action.process.terminate",
                $"{name} was already gone.", sw.Elapsed, ctx.Log.Build());
        }

        if (!verified.IsConfirmed)
        {
            ctx.Log.Error(
                $"  Identity mismatch: expected '{name}', but PID {pid} is now " +
                $"'{verified.VerifiedName}'. The PID may have been recycled. Termination aborted.");
            sw.Stop();
            return ActionExecutionResult.Failed(
                "action.process.terminate",
                $"PID {pid} no longer belongs to '{name}' — termination aborted to prevent " +
                $"accidentally stopping the wrong process.",
                sw.Elapsed, ctx.Log.Build());
        }

        // Critical check on the OS-verified name, not the caller-supplied name.
        // This is the only authoritative safety gate — do not remove or move earlier.
        if (ProcessClassifier.IsCritical(verified.VerifiedName))
        {
            ctx.Log.Error($"  {verified.VerifiedName} is a critical Windows process. Termination refused.");
            return Fail("action.process.terminate",
                $"{verified.VerifiedName} is a critical system process — termination refused.", sw, ctx);
        }

        ctx.Log.Info($"Terminating {verified.VerifiedName} (PID {pid})…");

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: false);
            ctx.Log.Info($"  {verified.VerifiedName} terminated.");
        }
        catch (ArgumentException)
        {
            ctx.Log.Warn($"  Process PID {pid} not found — it exited during execution.");
            sw.Stop();
            return ActionExecutionResult.Succeeded(
                "action.process.terminate",
                $"{verified.VerifiedName} was already gone.", sw.Elapsed, ctx.Log.Build());
        }
        catch (Exception ex)
        {
            ctx.Log.Error($"  Failed: {ex.Message}");
            sw.Stop();
            return ActionExecutionResult.Failed(
                "action.process.terminate",
                $"Could not terminate {verified.VerifiedName}: {ex.Message}",
                sw.Elapsed, ctx.Log.Build(), ex.ToString());
        }

        sw.Stop();
        return ActionExecutionResult.Succeeded(
            "action.process.terminate",
            $"{verified.VerifiedName} (PID {pid}) terminated.", sw.Elapsed, ctx.Log.Build());
    }

    public Task<ActionExecutionResult> RollbackAsync(
        string rollbackToken, ActionExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        context.Log.Error("Process termination cannot be rolled back.");
        return Task.FromResult(ActionExecutionResult.Failed(
            "action.process.terminate", "Termination does not support rollback.",
            TimeSpan.Zero, context.Log.Build()));
    }

    private static bool TryGetPid(ActionExecutionContext ctx, out int pid, out string name)
    {
        name = ctx.Parameters.TryGetValue(ParamProcessName, out var n) ? n : "Unknown";
        if (ctx.Parameters.TryGetValue(ParamProcessId, out var raw) && int.TryParse(raw, out pid))
            return true;
        pid = -1; return false;
    }

    private static ActionExecutionResult Fail(string id, string msg,
        System.Diagnostics.Stopwatch sw, ActionExecutionContext ctx)
    {
        ctx.Log.Error(msg); sw.Stop();
        return ActionExecutionResult.Failed(id, msg, sw.Elapsed, ctx.Log.Build());
    }
}
