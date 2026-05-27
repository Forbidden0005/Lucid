namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Opens the Storage Sense settings page in the Windows Settings app.
///
/// At this stage the executor navigates the user to Storage Sense so they can
/// review configuration and trigger cleanup manually. A future iteration will
/// invoke Storage Sense programmatically (via COM / PowerShell) once the
/// elevated-action pipeline is in place.
///
/// Launch target: <c>ms-settings:storagesense</c> — the registered URI scheme
/// for the Storage Sense page, available on Windows 10 1703+ and Windows 11.
///
/// Maps to <see cref="SystemAction"/> id <c>"action.disk.run-storage-sense"</c>
/// produced by <c>LowDiskSpaceRule</c>.
/// </summary>
internal sealed class OpenStorageSenseExecutor : OpenApplicationExecutorBase
{
    public override string ActionId        => "action.disk.run-storage-sense";
    protected override string LaunchTarget   => "ms-settings:storagesense";
    protected override string AppDisplayName => "Storage Sense";
}
