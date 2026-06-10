using System.Diagnostics;
using Lucid.Services.Repair;

namespace Lucid.Services.Execution.Executors;

/// <summary>
/// Executor for the "action.network.flush-dns" action.
///
/// Runs "ipconfig.exe /flushdns" to clear the Windows DNS resolver cache.
///
/// The DNS cache stores recent domain→IP mappings so browsers don't look
/// up the same domain on every request.  Stale or incorrect entries can
/// cause sites to fail to load even when connectivity is fine.
///
/// Safety model:
///   • Fully safe — the cache rebuilds automatically as you browse.
///   • No system files are modified, no configuration is changed.
///   • No rollback needed (cache is ephemeral).
///   • Standard privilege — no elevation required.
///   • Sub-second execution; dry-run is still provided for consistency.
/// </summary>
internal sealed class FlushDnsExecutor : IActionExecutor
{
    private readonly IFlushDnsRuntime _runtime;

    public FlushDnsExecutor() : this(new DefaultFlushDnsRuntime())
    {
    }

    internal FlushDnsExecutor(IFlushDnsRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    private const string Id = "action.network.flush-dns";
    public string               ActionId             => Id;
    public ActionPrivilegeLevel RequiredPrivilege    => ActionPrivilegeLevel.Standard;
    public bool                 RequiresConfirmation => false;
    public bool                 SupportsDryRun       => true;
    public bool                 SupportsRollback     => false;

    // ── Metadata ──────────────────────────────────────────────────────────────

    public static RepairOperationMetadata Metadata =>
        RepairOperationCatalog.TryGet("action.network.flush-dns")!;

    // ── IActionExecutor.ExecuteAsync ──────────────────────────────────────────

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default) =>
        Task.Run(
            () => context.IsDryRun
                ? RunDryRun(context)
                : RunFlush(context, cancellationToken),
            cancellationToken);

    // ── Dry-run ───────────────────────────────────────────────────────────────

    private static ActionExecutionResult RunDryRun(ActionExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        var m  = Metadata;

        context.Log.Info("DNS Cache Flush — preview mode.");
        context.Log.Info("");
        context.Log.Info($"What it does:  {m.Purpose}");
        context.Log.Info($"How:           {m.HowItWorks}");
        context.Log.Info($"Expected time: {m.ExpectedDuration}");
        context.Log.Info($"Reversible:    Yes — DNS cache rebuilds automatically.");
        context.Log.Info("");
        context.Log.Info("Command that would run:  ipconfig.exe /flushdns");

        sw.Stop();
        return ActionExecutionResult.DryRunCompleted(
            Id,
            "Preview: would clear the Windows DNS resolver cache.",
            sw.Elapsed, context.Log.Build());
    }

    // ── Live flush ────────────────────────────────────────────────────────────

    private ActionExecutionResult RunFlush(
        ActionExecutionContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        context.Log.Info("Flushing DNS resolver cache…");
        context.Log.Info("Command: ipconfig.exe /flushdns");

        var outputLines = new List<string>();

        var result = _runtime.Run(
            exe: "ipconfig.exe",
            arguments: "/flushdns",
            log: context.Log,
            ct: ct,
            outputLines: outputLines);

        sw.Stop();

        if (result.WasCancelled)
            return ActionExecutionResult.Cancelled(
                ActionId, "DNS flush cancelled.", sw.Elapsed, context.Log.Build());

        bool ok = CommandOutputParser.FlushDnsSucceeded(outputLines)
                  || result.ExitCode == 0;

        var msg = ok
            ? "DNS resolver cache cleared. Stale entries have been removed."
            : $"ipconfig /flushdns exited with code {result.ExitCode}. " +
              "The cache may not have been fully cleared.";

        context.Log.Info(ok ? $"✓ {msg}" : $"⚠ {msg}");

        return ok
            ? ActionExecutionResult.Succeeded(ActionId, msg, sw.Elapsed, context.Log.Build())
            : ActionExecutionResult.Failed(ActionId, msg, sw.Elapsed, context.Log.Build());
    }

    // ── IActionExecutor.RollbackAsync ─────────────────────────────────────────

    public Task<ActionExecutionResult> RollbackAsync(
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default)
    {
        context.Log.Info(
            "DNS flush does not require rollback — the cache rebuilds automatically.");
        return Task.FromResult(ActionExecutionResult.Succeeded(
            ActionId, "No rollback needed — DNS cache self-heals.",
            TimeSpan.Zero, context.Log.Build()));
    }

    internal interface IFlushDnsRuntime
    {
        ProcessExecutionHelper.RunResult Run(
            string exe,
            string arguments,
            ActionExecutionLog log,
            CancellationToken ct,
            IList<string> outputLines);
    }

    private sealed class DefaultFlushDnsRuntime : IFlushDnsRuntime
    {
        public ProcessExecutionHelper.RunResult Run(
            string exe,
            string arguments,
            ActionExecutionLog log,
            CancellationToken ct,
            IList<string> outputLines) =>
            ProcessExecutionHelper.Run(
                exe: exe,
                arguments: arguments,
                log: log,
                ct: ct,
                progressLineCallback: line => outputLines.Add(line),
                lineFilter: CommandOutputParser.IsInterestingLine);
    }
}
