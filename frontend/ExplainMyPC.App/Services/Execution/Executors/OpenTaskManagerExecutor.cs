namespace ExplainMyPC.Services.Execution.Executors;

/// <summary>
/// Opens Windows Task Manager so the user can inspect CPU-heavy or memory-
/// hungry processes.
///
/// Launch target: <c>taskmgr.exe</c> — always present on Windows 8+,
/// found via PATH without an absolute path.
///
/// Maps to <see cref="SystemAction"/> id <c>"action.cpu.open-task-manager"</c>
/// produced by <c>SustainedHighCpuRule</c>.
/// </summary>
internal sealed class OpenTaskManagerExecutor : OpenApplicationExecutorBase
{
    public override string ActionId        => "action.cpu.open-task-manager";
    protected override string LaunchTarget   => "taskmgr.exe";
    protected override string AppDisplayName => "Task Manager";
}
