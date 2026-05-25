using System.Diagnostics;

namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Abstract base for executors whose sole effect is launching a Windows
/// application or settings page via shell-execute.
///
/// Common characteristics of all "open" executors:
///   • Standard privilege — shell-execute never requires UAC.
///   • No confirmation — opening an app is trivially safe.
///   • Dry-run supported — log what would have been launched.
///   • No rollback — closing an application is the user's responsibility;
///     the process lifecycle is not tracked by this executor.
///
/// Subclasses supply three things:
///   <see cref="ActionId"/>       — stable dot-separated identifier.
///   <see cref="LaunchTarget"/>   — exe name or URI (e.g. "taskmgr.exe",
///                                  "ms-settings:storagesense").
///   <see cref="AppDisplayName"/> — human-readable name for log messages
///                                  and result descriptions.
///
/// Error handling:
///   Process.Start is wrapped in a try/catch so that a missing executable
///   or unregistered URI scheme returns a clean Failed result rather than
///   propagating an exception to the engine (which would catch it anyway,
///   but this gives executors a chance to write a more targeted error entry).
/// </summary>
internal abstract class OpenApplicationExecutorBase : IActionExecutor
{
    // ── Subclass contract ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public abstract string ActionId { get; }

    /// <summary>
    /// Executable file name or URI scheme passed to
    /// <see cref="ProcessStartInfo.FileName"/>.
    ///
    /// Examples:
    ///   "taskmgr.exe"             — searches PATH; reliable on all Windows versions.
    ///   "ms-settings:storagesense" — opens the Storage Sense settings page.
    ///   "windowsdefender:"         — opens Windows Security.
    /// </summary>
    protected abstract string LaunchTarget { get; }

    /// <summary>Human-readable application name used in log entries and result messages.</summary>
    protected abstract string AppDisplayName { get; }

    // ── Capability declarations ───────────────────────────────────────────────

    public ActionPrivilegeLevel RequiredPrivilege   => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => false;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback      => false;

    // ── IActionExecutor.ExecuteAsync ──────────────────────────────────────────

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        context.Log.Info($"[{ActionId}] Preparing to open {AppDisplayName}.");

        // ── Dry-run ───────────────────────────────────────────────────────────
        if (context.IsDryRun)
        {
            context.Log.Info(
                $"[{ActionId}] Dry-run: would shell-execute '{LaunchTarget}'. " +
                $"No system changes made.");
            sw.Stop();
            return Task.FromResult(ActionExecutionResult.DryRunCompleted(
                ActionId,
                $"Dry-run: {AppDisplayName} would be opened. No changes were made.",
                sw.Elapsed,
                context.Log.Build()));
        }

        // ── Execute ───────────────────────────────────────────────────────────
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = LaunchTarget,
                UseShellExecute = true,   // required for URI schemes + searches PATH
            });

            context.Log.Info($"[{ActionId}] {AppDisplayName} launched successfully.");
            sw.Stop();
            return Task.FromResult(ActionExecutionResult.Succeeded(
                ActionId,
                $"{AppDisplayName} opened.",
                sw.Elapsed,
                context.Log.Build()));
        }
        catch (Exception ex)
        {
            context.Log.Error(
                $"[{ActionId}] Failed to shell-execute '{LaunchTarget}': " +
                $"{ex.GetType().Name}: {ex.Message}");
            sw.Stop();
            return Task.FromResult(ActionExecutionResult.Failed(
                ActionId,
                $"Could not open {AppDisplayName}. Ensure Windows is up to date and try again.",
                sw.Elapsed,
                context.Log.Build(),
                errorDetail: ex.ToString()));
        }
    }

    // ── IActionExecutor.RollbackAsync ─────────────────────────────────────────

    /// <summary>
    /// Not supported — opening an application leaves no snapshot to restore.
    /// The engine guards against this call via <see cref="SupportsRollback"/>.
    /// </summary>
    public Task<ActionExecutionResult> RollbackAsync(
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        throw new NotSupportedException(
            $"{GetType().Name} does not support rollback. " +
            $"Close {AppDisplayName} manually to undo this action.");
}
