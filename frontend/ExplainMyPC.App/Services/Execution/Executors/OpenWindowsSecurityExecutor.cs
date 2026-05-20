namespace ExplainMyPC.Services.Execution.Executors;

/// <summary>
/// Opens the Windows Security application.
///
/// Windows Security provides virus and threat protection, firewall status,
/// device health reports, and real-time protection settings. Directing the user
/// here is appropriate when ExplainMyPC detects anomalous behaviour that warrants
/// a deeper security review (e.g. sustained unexpected GPU load, unfamiliar
/// background network activity).
///
/// Launch target: <c>windowsdefender:</c> — the registered URI scheme for the
/// Windows Security app (formerly Windows Defender Security Center), available
/// on Windows 10 1703+ and Windows 11.
///
/// Intended to back future security-focused insight rules.
/// Action id: <c>"action.security.open-windows-security"</c>.
/// </summary>
internal sealed class OpenWindowsSecurityExecutor : OpenApplicationExecutorBase
{
    public override string ActionId        => "action.security.open-windows-security";
    protected override string LaunchTarget   => "windowsdefender:";
    protected override string AppDisplayName => "Windows Security";
}
