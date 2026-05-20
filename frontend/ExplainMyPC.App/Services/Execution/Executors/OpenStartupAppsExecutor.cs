namespace ExplainMyPC.Services.Execution.Executors;

/// <summary>
/// Opens the Startup Apps settings page in the Windows Settings app.
///
/// The user can then review and toggle which applications launch automatically
/// at sign-in. Disabling unneeded startup entries is one of the most impactful
/// reversible steps a user can take to improve boot time and idle resource usage.
///
/// At this stage the executor is a guided navigation action. A future iteration
/// will enumerate startup entries programmatically and surface them as individual
/// toggleable actions within ExplainMyPC.
///
/// Launch target: <c>ms-settings:startupapps</c> — the registered URI scheme
/// for the Startup Apps page, available on Windows 10 1803+ and Windows 11.
///
/// Intended to back the "Startup Apps" quick action on the dashboard and any
/// future startup-management insight rules.
/// Action id: <c>"action.startup.open-startup-apps"</c>.
/// </summary>
internal sealed class OpenStartupAppsExecutor : OpenApplicationExecutorBase
{
    public override string ActionId        => "action.startup.open-startup-apps";
    protected override string LaunchTarget   => "ms-settings:startupapps";
    protected override string AppDisplayName => "Startup Apps";
}
