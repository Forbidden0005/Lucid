using System.Diagnostics;
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

        if (ProcessClassifier.IsCritical(name))
        {
            ctx.Log.Error($"{name} (PID {pid}) is a critical Windows process and cannot be terminated.");
            return Fail("action.process.terminate",
                $"{name} is a critical system process — termination refused.", sw, ctx);
        }

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

        if (ProcessClassifier.IsCritical(name))
        {
            ctx.Log.Error($"{name} is a critical Windows process. Termination refused.");
            return Fail("action.process.terminate",
                $"{name} is a critical system process — termination refused.", sw, ctx);
        }

        ctx.Log.Info($"Terminating {name} (PID {pid})…");

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: false);
            ctx.Log.Info($"  ✓ {name} terminated.");
        }
        catch (ArgumentException)
        {
            ctx.Log.Warn($"  Process PID {pid} not found — it may have already exited.");
            sw.Stop();
            return ActionExecutionResult.Succeeded(
                "action.process.terminate",
                $"{name} was already gone.", sw.Elapsed, ctx.Log.Build());
        }
        catch (Exception ex)
        {
            ctx.Log.Error($"  Failed: {ex.Message}");
            sw.Stop();
            return ActionExecutionResult.Failed(
                "action.process.terminate",
                $"Could not terminate {name}: {ex.Message}", sw.Elapsed, ctx.Log.Build(), ex.ToString());
        }

        sw.Stop();
        return ActionExecutionResult.Succeeded(
            "action.process.terminate",
            $"{name} (PID {pid}) terminated.", sw.Elapsed, ctx.Log.Build());
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
