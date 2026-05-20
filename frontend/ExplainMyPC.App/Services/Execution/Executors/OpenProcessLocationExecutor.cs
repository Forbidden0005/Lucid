using System.Diagnostics;

namespace ExplainMyPC.Services.Execution.Executors;

/// <summary>
/// Executor for "action.process.open-location".
/// Opens the directory containing the process executable in Windows Explorer.
/// Required parameter: "ExecutablePath" — full path to the exe.
/// </summary>
internal sealed class OpenProcessLocationExecutor : IActionExecutor
{
    public string               ActionId             => "action.process.open-location";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => false;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => false;

    public const string ParamExecutablePath = "ExecutablePath";
    public const string ParamProcessName    = "ProcessName";

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(() => context.IsDryRun
            ? RunDryRun(context)
            : RunOpen(context), cancellationToken);

    private static ActionExecutionResult RunDryRun(ActionExecutionContext ctx)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ctx.Parameters.TryGetValue(ParamExecutablePath, out var path);
        ctx.Log.Info($"Preview — would open folder in Explorer: {path ?? "(unknown)"}");
        sw.Stop();
        return ActionExecutionResult.DryRunCompleted(
            "action.process.open-location",
            "Preview: Explorer would open the process folder.", sw.Elapsed, ctx.Log.Build());
    }

    private static ActionExecutionResult RunOpen(ActionExecutionContext ctx)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ctx.Parameters.TryGetValue(ParamExecutablePath, out var exePath);
        ctx.Parameters.TryGetValue(ParamProcessName, out var name);

        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            // Fallback: open C:\Windows\System32 or just explorer
            exePath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            ctx.Log.Warn($"Executable path not available for {name}. Opening System32.");
        }

        string folder = Path.GetDirectoryName(exePath) ?? exePath;
        ctx.Log.Info($"Opening Explorer: {folder}");

        try
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName        = "explorer.exe",
                Arguments       = $"/select,\"{exePath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ctx.Log.Error($"Failed to open Explorer: {ex.Message}");
            sw.Stop();
            return ActionExecutionResult.Failed(
                "action.process.open-location",
                $"Could not open folder: {ex.Message}", sw.Elapsed, ctx.Log.Build());
        }

        sw.Stop();
        return ActionExecutionResult.Succeeded(
            "action.process.open-location",
            $"Opened {folder} in Explorer.", sw.Elapsed, ctx.Log.Build());
    }

    public Task<ActionExecutionResult> RollbackAsync(
        string rollbackToken, ActionExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        context.Log.Error("Open-location does not support rollback.");
        return Task.FromResult(ActionExecutionResult.Failed(
            "action.process.open-location", "No rollback available.",
            TimeSpan.Zero, context.Log.Build()));
    }
}
