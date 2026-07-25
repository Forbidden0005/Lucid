using System.Diagnostics;
using Lucid.Services.Execution.Validation;
using Lucid.Services.ProcessIntel;

namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Executor for "action.process.terminate".
///
/// Terminates a process by PID after confirming it is not system-critical.
/// Requires explicit user confirmation (RequiresConfirmation = true).
///
/// Required parameter: "ProcessId" — string representation of the PID.
/// Optional parameter: "ProcessName" — for display and safety checks.
///
/// Safety:
///   • Critical Windows processes (csrss, lsass, winlogon, etc.) are always refused.
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

        // NOTE: we deliberately do NOT check ProcessClassifier.IsCritical on the
        // caller-supplied `name` here. Dry-run can't verify PID-to-name mapping, so
        // a name-based gate gives false assurance. The real safety check runs on the
        // OS-verified process name at actual execution time (RunTerminate → line ~105).
        ctx.Log.Info($"Preview — would terminate: {name} (PID {pid})");
        ctx.Log.Info("  This terminates the process immediately. Unsaved work in that app will be lost.");
        ctx.Log.Info("  No rollback is possible — the process must be restarted manually.");

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

        // NOTE: we deliberately do NOT check ProcessClassifier.IsCritical on the
        // caller-supplied `name` here. The caller could pass "notepad" while the PID
        // actually points to lsass due to PID recycling — checking the caller name
        // would be a false safety gate. The authoritative critical-process check
        // runs below on verified.VerifiedName after OS identity confirmation.

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

        // Critical check on the OS-verified name, not just the caller-supplied name.
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
            ctx.Log.Info($"  ✓ {verified.VerifiedName} terminated.");
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
