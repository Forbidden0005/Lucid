using System.Diagnostics;
using System.Runtime.InteropServices;
using Lucid.Services.Cleanup;

namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.disk.empty-recycle-bin" action.
///
/// Queries and empties the Windows Recycle Bin using the Shell32 API.
/// Items already in the Recycle Bin have already been deleted by the user —
/// this executor only frees the disk space they are holding.
///
/// Safety model:
///   • Dry-run: calls SHQueryRecycleBin to show total size + item count
///     without touching any data.
///   • Live run: calls SHEmptyRecycleBin with NOCONFIRMATION | NOPROGRESSUI
///     so Windows does not show its own dialog (we handle confirmation in the
///     ViewModel layer before calling the engine).
///   • RequiresConfirmation = true: the engine enforces that the user has
///     explicitly acknowledged the action before it runs.
///   • SupportsRollback = false: items in the Recycle Bin are already in a
///     user-deleted state. There is no safe way to restore them programmatically
///     once the bin is emptied; users who want recovery should use dedicated
///     file-recovery tools before running this action.
///
/// Privilege:
///   Standard (no elevation). Each user owns their own Recycle Bin.
///   Passing null to SHEmptyRecycleBin empties all drives for the current user.
/// </summary>
internal sealed class RecycleBinCleanupExecutor : IActionExecutor
{
    private readonly IRecycleBinRuntime _runtime;

    public RecycleBinCleanupExecutor() : this(new DefaultRecycleBinRuntime())
    {
    }

    internal RecycleBinCleanupExecutor(IRecycleBinRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    public string               ActionId             => "action.disk.empty-recycle-bin";
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => true;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => false;

    // ── Shell32 P/Invoke ──────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int  cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHQueryRecycleBin(
        string?          pszRootPath,
        ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHEmptyRecycleBin(
        IntPtr  hwnd,
        string? pszRootPath,
        uint    dwFlags);

    // dwFlags values for SHEmptyRecycleBin
    private const uint SHERB_NOCONFIRMATION = 0x00000001; // no "are you sure?" dialog
    private const uint SHERB_NOPROGRESSUI   = 0x00000002; // no progress window
    private const uint SHERB_NOSOUND        = 0x00000004; // no delete sound
    private const int ShellUnexpectedResult = unchecked((int)0x8000FFFF);

    // ── IActionExecutor.ExecuteAsync ──────────────────────────────────────────

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(
            () => context.IsDryRun
                ? RunDryRun(context, cancellationToken)
                : RunEmpty(context, cancellationToken),
            cancellationToken);

    // ── Dry-run ───────────────────────────────────────────────────────────────

    private ActionExecutionResult RunDryRun(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        context.Log.Info(
            "Querying Recycle Bin — preview mode, no changes will be made.");

        var (hr, sizeBytes, itemCount) = _runtime.Query();

        sw.Stop();

        if (hr != 0)
        {
            var msg = $"SHQueryRecycleBin returned HRESULT 0x{hr:X8}.";
            context.Log.Error(msg);
            return ActionExecutionResult.Failed(
                ActionId, msg, sw.Elapsed, context.Log.Build());
        }

        if (itemCount == 0)
        {
            var msg = "The Recycle Bin is already empty.";
            context.Log.Info(msg);
            return ActionExecutionResult.DryRunCompleted(
                ActionId, msg, sw.Elapsed, context.Log.Build());
        }

        var previewMsg =
            $"Preview: {itemCount} item(s), " +
            $"{CleanupScanner.FormatBytes(sizeBytes)} would be freed.";
        context.Log.Info(previewMsg);
        return ActionExecutionResult.DryRunCompleted(
            ActionId, previewMsg, sw.Elapsed, context.Log.Build());
    }

    // ── Live empty ────────────────────────────────────────────────────────────

    private ActionExecutionResult RunEmpty(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        context.Log.Info("Querying Recycle Bin size before emptying…");

        // Snapshot the size before so we can report how much was freed.
        var (_, beforeSizeBytes, beforeItemCount) = _runtime.Query();

        if (beforeItemCount == 0)
        {
            context.Log.Info("The Recycle Bin is already empty.");
            sw.Stop();
            return ActionExecutionResult.Succeeded(
                ActionId, "The Recycle Bin was already empty.",
                sw.Elapsed, context.Log.Build());
        }

        context.Log.Info(
            $"Emptying Recycle Bin — " +
            $"{beforeItemCount} item(s), " +
            $"{CleanupScanner.FormatBytes(beforeSizeBytes)}…");

        var hr = _runtime.Empty(
            SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

        sw.Stop();

        // S_OK (0) = success. E_UNEXPECTED (0x8000FFFF) can be returned when
        // the bin was already empty or the shell is resetting — treat as success.
        if (hr != 0 && hr != ShellUnexpectedResult)
        {
            var msg = $"SHEmptyRecycleBin failed with HRESULT 0x{hr:X8}.";
            context.Log.Error(msg);
            return ActionExecutionResult.Failed(
                ActionId, msg, sw.Elapsed, context.Log.Build());
        }

        var doneMsg =
            $"Recycle Bin emptied — " +
            $"{beforeItemCount} item(s), " +
            $"{CleanupScanner.FormatBytes(beforeSizeBytes)} freed.";
        context.Log.Info(doneMsg);
        return ActionExecutionResult.Succeeded(
            ActionId, doneMsg, sw.Elapsed, context.Log.Build());
    }

    // ── IActionExecutor.RollbackAsync ─────────────────────────────────────────

    // SupportsRollback = false, so the engine will never call this. Implemented
    // as a precaution so the interface contract is satisfied.
    public Task<ActionExecutionResult> RollbackAsync(
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default)
    {
        context.Log.Error(
            "Recycle Bin cleanup does not support rollback. " +
            "Items that were in the Recycle Bin cannot be recovered by this tool.");
        return Task.FromResult(ActionExecutionResult.Failed(
            ActionId,
            "This action does not support rollback.",
            TimeSpan.Zero, context.Log.Build()));
    }

    internal interface IRecycleBinRuntime
    {
        (int HResult, long SizeBytes, long ItemCount) Query();
        int Empty(uint flags);
    }

    private sealed class DefaultRecycleBinRuntime : IRecycleBinRuntime
    {
        public (int HResult, long SizeBytes, long ItemCount) Query()
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            var hr = SHQueryRecycleBin(null, ref info);
            return (hr, info.i64Size, info.i64NumItems);
        }

        public int Empty(uint flags) => SHEmptyRecycleBin(IntPtr.Zero, null, flags);
    }
}
