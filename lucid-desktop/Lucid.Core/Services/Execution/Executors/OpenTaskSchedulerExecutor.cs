namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Opens Windows Task Scheduler so the user can inspect timed tasks that may
/// be causing recurring CPU spikes.
///
/// Launch target: <c>taskschd.msc</c> — the Task Scheduler MMC snap-in.
/// Present on all Windows versions; shell-executed without an absolute path.
///
/// Maps to <see cref="SystemAction"/> id <c>"action.cpu.open-task-scheduler"</c>
/// produced by <c>PeriodicCpuSpikeRule</c>.
/// </summary>
internal sealed class OpenTaskSchedulerExecutor : OpenApplicationExecutorBase
{
    public override string ActionId        => "action.cpu.open-task-scheduler";
    protected override string LaunchTarget   => "taskschd.msc";
    protected override string AppDisplayName => "Task Scheduler";
}
