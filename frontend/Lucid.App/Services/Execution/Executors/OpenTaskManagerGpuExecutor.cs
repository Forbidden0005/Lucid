namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Opens Windows Task Manager so the user can navigate to the Performance → GPU
/// tab and inspect which process is driving unexpected background GPU activity.
///
/// Launch target: <c>taskmgr.exe</c> — always present on Windows 8+.
/// Windows opens Task Manager on the last-used tab, so users familiar with the
/// Performance view will land there directly.
///
/// Maps to <see cref="SystemAction"/> id <c>"action.gpu.inspect-processes"</c>
/// produced by <c>AbnormalGpuUsageRule</c>.
/// </summary>
internal sealed class OpenTaskManagerGpuExecutor : OpenApplicationExecutorBase
{
    public override string ActionId        => "action.gpu.inspect-processes";
    protected override string LaunchTarget   => "taskmgr.exe";
    protected override string AppDisplayName => "Task Manager";
}
