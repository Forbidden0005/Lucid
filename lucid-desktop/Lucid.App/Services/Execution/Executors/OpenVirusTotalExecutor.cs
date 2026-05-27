using System.Diagnostics;

namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Executor for "action.security.open-virustotal".
///
/// Opens the user's default browser to a VirusTotal search for the
/// given file hash or file name. This is a manual browser launch — 
/// no file data is uploaded automatically. The user decides whether
/// to proceed on the VirusTotal site.
///
/// Required parameter: "FilePath" — the path to look up.
/// Optional parameter: "FileName" — display name for logging.
///
/// Safety: opens a browser URL only. Never uploads files. Never connects
/// to external services directly. User retains full control.
/// </summary>
internal sealed class OpenVirusTotalExecutor : IActionExecutor
{
    public string               ActionId             => "action.security.open-virustotal";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => false;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => false;

    public const string ParamFilePath = "FilePath";
    public const string ParamFileName = "FileName";

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(() => context.IsDryRun ? RunDryRun(context) : RunOpen(context), cancellationToken);

    private static ActionExecutionResult RunDryRun(ActionExecutionContext ctx)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ctx.Parameters.TryGetValue(ParamFileName, out var name);
        ctx.Parameters.TryGetValue(ParamFilePath, out var path);
        ctx.Log.Info($"Preview — would open VirusTotal search for: {name ?? path ?? "(unknown)"}");
        ctx.Log.Info("  This opens your default browser. No file data is uploaded automatically.");
        ctx.Log.Info("  You remain in control of whether to submit anything to VirusTotal.");
        sw.Stop();
        return ActionExecutionResult.DryRunCompleted("action.security.open-virustotal",
            "Preview: VirusTotal search page would open in your browser.", sw.Elapsed, ctx.Log.Build());
    }

    private static ActionExecutionResult RunOpen(ActionExecutionContext ctx)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ctx.Parameters.TryGetValue(ParamFilePath, out var path);
        ctx.Parameters.TryGetValue(ParamFileName, out var name);

        string searchTerm = string.Empty;

        // Try to compute SHA-256 for a more accurate VT lookup
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                using var stream = File.OpenRead(path);
                var hash = System.Security.Cryptography.SHA256.HashData(stream);
                searchTerm = Convert.ToHexString(hash).ToLowerInvariant();
                ctx.Log.Info($"SHA-256: {searchTerm}");
            }
            catch
            {
                searchTerm = Path.GetFileName(path) ?? string.Empty;
                ctx.Log.Warn("Could not hash file — searching by name instead.");
            }
        }
        else if (!string.IsNullOrEmpty(name))
        {
            searchTerm = name;
        }

        if (string.IsNullOrEmpty(searchTerm))
        {
            ctx.Log.Error("No file path or name provided.");
            sw.Stop();
            return ActionExecutionResult.Failed("action.security.open-virustotal",
                "No file to search for.", sw.Elapsed, ctx.Log.Build());
        }

        string url = $"https://www.virustotal.com/gui/search/{Uri.EscapeDataString(searchTerm)}";
        ctx.Log.Info($"Opening: {url}");

        try
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName        = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ctx.Log.Error($"Failed to open browser: {ex.Message}");
            sw.Stop();
            return ActionExecutionResult.Failed("action.security.open-virustotal",
                "Could not open browser.", sw.Elapsed, ctx.Log.Build());
        }

        sw.Stop();
        return ActionExecutionResult.Succeeded("action.security.open-virustotal",
            "VirusTotal search opened in your browser. You remain in control of any submission.",
            sw.Elapsed, ctx.Log.Build());
    }

    public Task<ActionExecutionResult> RollbackAsync(
        string rollbackToken, ActionExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        context.Log.Error("Open-VirusTotal does not support rollback.");
        return Task.FromResult(ActionExecutionResult.Failed(
            "action.security.open-virustotal", "No rollback available.",
            TimeSpan.Zero, context.Log.Build()));
    }
}
