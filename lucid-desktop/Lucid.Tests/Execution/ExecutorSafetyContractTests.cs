using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Lucid.Services.Repair;
using Lucid.Services.Startup;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Safety contract suite over the full production executor set.
///
/// This suite mechanizes the platform safety doctrine so that every current
/// and future executor is held to the same invariants without needing a
/// bespoke test per executor:
///
///   1. The complete production set registers cleanly under the registry's
///      enforced metadata contract (privilege, confirmation, dry-run,
///      rollback, consent / failure-mode / diagnostics notes).
///   2. Catalog metadata matches each executor's runtime declarations.
///   3. Action IDs are unique and well-formed.
///   4. Dry-run never reports applied changes, never lets an exception
///      escape, and never touches a mutating runtime operation — proven at
///      the seam boundary with guarded fakes that record and refuse any
///      mutation attempt.
///   5. Rollback with an unknown token never claims restoration and fails
///      safely (the only permitted exception is <see cref="NotSupportedException"/>
///      from executors that declare <c>SupportsRollback == false</c>).
///
/// Scope notes (deliberate, documented):
///   • The executor list below mirrors the production registration in
///     <c>AppServices</c>. If an executor is added there but not here, the
///     registration-count assertion in this suite will not catch it — but the
///     existing source-scan test in <see cref="ActionExecutionMetadataCatalogTests"/>
///     catches catalog omissions, and reviewers should keep the two lists in sync.
///   • Four cleanup executors (temp files, browser cache, delivery
///     optimization, old downloads) scan live user directories in dry-run.
///     That scan is read-only by design but machine-dependent and potentially
///     slow, so they are excluded from the behavioural dry-run invocation
///     here; their dry-run and rollback behaviour is covered by dedicated
///     parameter/seam-based tests. All declaration-level checks in this suite
///     still apply to them.
/// </summary>
public sealed class ExecutorSafetyContractTests
{
    /// <summary>
    /// A rollback token that is simultaneously: not valid JSON, not an
    /// existing filesystem path, and not a real staging directory — so every
    /// token interpretation an executor might use fails safely.
    /// </summary>
    private const string UnknownRollbackToken =
        @"Z:\lucid-contract-suite\nonexistent-rollback-token";

    // ── 1. Full-set registration under the enforced metadata contract ─────────

    [Fact]
    public void ProductionExecutorSet_RegistersCleanly_UnderEnforcedMetadataContract()
    {
        // The enforcing registry re-validates every doctrine field at
        // registration time — this single test proves the whole production
        // set passes the same gate the app applies at startup.
        var registry  = new ActionExecutorRegistry(enforceMetadataContract: true);
        var executors = CreateHarnesses().Select(h => h.Executor).ToList();

        var act = () => registry.RegisterAll(executors);

        act.Should().NotThrow(
            "every production executor must satisfy the metadata contract " +
            "the application enforces at startup");
        registry.Count.Should().Be(executors.Count);
    }

    // ── 2. Action ID hygiene ───────────────────────────────────────────────────

    [Fact]
    public void ActionIds_AreUniqueAndWellFormed()
    {
        var ids = CreateHarnesses().Select(h => h.Executor.ActionId).ToList();

        ids.Should().OnlyHaveUniqueItems(
            "each action ID must map to exactly one executor");
        ids.Should().AllSatisfy(id => id.Should().MatchRegex(
            @"^action\.[a-z0-9]+(?:[.\-][a-z0-9]+)*$",
            "action IDs are stable lowercase dot/hyphen identifiers"));
    }

    // ── 3. Metadata / runtime declaration consistency ──────────────────────────

    [Theory]
    [MemberData(nameof(AllExecutorActionIds))]
    public void Metadata_MatchesRuntimeDeclarations(string actionId)
    {
        var executor = GetHarness(actionId).Executor;
        var metadata = ActionExecutionMetadataCatalog.GetRequired(actionId);

        metadata.RequiredPrivilege.Should().Be(executor.RequiredPrivilege);
        metadata.RequiresConfirmation.Should().Be(executor.RequiresConfirmation);
        metadata.SupportsDryRun.Should().Be(executor.SupportsDryRun);
        metadata.SupportsRollback.Should().Be(executor.SupportsRollback);

        // The explainability doctrine: every action must carry human-readable
        // consent, failure-mode, and diagnostics context.
        metadata.ConsentNote.Should().NotBeNullOrWhiteSpace();
        metadata.FailureModeSummary.Should().NotBeNullOrWhiteSpace();
        metadata.DiagnosticsCategory.Should().NotBeNullOrWhiteSpace();
    }

    // ── 4. Dry-run purity ──────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(DryRunInvocableActionIds))]
    public async Task DryRun_NeverAppliesChanges_NeverThrows_NeverTouchesMutationSeams(
        string actionId)
    {
        var harness = GetHarness(actionId);
        var context = NewContext(isDryRun: true);

        // Contract: ExecuteAsync never throws — failures are returned as results.
        var execute = () => harness.Executor.ExecuteAsync(context, CancellationToken.None);
        var result  = (await execute.Should().NotThrowAsync(
            "executors must contain all failures and return a result")).Which;

        result.Should().NotBeNull();
        result.ActionId.Should().Be(actionId);

        // A dry-run may describe, refuse, or fail — but it must never claim
        // that real changes were applied.
        result.Status.Should().NotBe(ActionExecutionStatus.Success)
            .And.NotBe(ActionExecutionStatus.PartialSuccess,
                "a dry-run must never report applied changes");

        harness.MutationAttempted().Should().BeFalse(
            "a dry-run must never invoke a mutating runtime operation");
    }

    // ── 5. Rollback hostile-token safety ───────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllExecutorActionIds))]
    public async Task RollbackWithUnknownToken_NeverClaimsRestoration_AndFailsSafely(
        string actionId)
    {
        var harness  = GetHarness(actionId);
        var executor = harness.Executor;
        var context  = NewContext(isDryRun: false);

        try
        {
            var result = await executor.RollbackAsync(
                UnknownRollbackToken, context, CancellationToken.None);

            result.Should().NotBeNull();
            result.ActionId.Should().Be(actionId);

            if (executor.SupportsRollback &&
                !IdempotentArtifactRollbacks.Contains(actionId))
            {
                // A rollback-capable executor handed a token it cannot resolve
                // must not tell the user their files / settings were restored.
                result.Status.Should().NotBe(ActionExecutionStatus.Success)
                    .And.NotBe(ActionExecutionStatus.PartialSuccess,
                        "an unknown rollback token must not be reported as restored");
            }
            // Non-rollbackable executors may return an explicit no-op
            // acknowledgement (e.g. "DNS cache self-heals") — that is allowed.
        }
        catch (NotSupportedException)
        {
            // The IActionExecutor contract permits NotSupportedException only
            // from executors that declare themselves non-rollbackable.
            executor.SupportsRollback.Should().BeFalse(
                "only executors with SupportsRollback == false may refuse " +
                "rollback by throwing NotSupportedException");
        }

        harness.MutationAttempted().Should().BeFalse(
            "a rollback with an unresolvable token must not attempt any " +
            "mutating runtime operation");
    }

    // ── Theory data ────────────────────────────────────────────────────────────

    public static TheoryData<string> AllExecutorActionIds()
    {
        var data = new TheoryData<string>();
        foreach (var harness in CreateHarnesses())
            data.Add(harness.Executor.ActionId);
        return data;
    }

    public static TheoryData<string> DryRunInvocableActionIds()
    {
        var data = new TheoryData<string>();
        foreach (var harness in CreateHarnesses())
            if (!DryRunInvocationExclusions.Contains(harness.Executor.ActionId))
                data.Add(harness.Executor.ActionId);
        return data;
    }

    /// <summary>
    /// Executors whose rollback is an idempotent deletion of their own
    /// artifact rather than a restoration of system state. For these, a
    /// missing rollback target means the desired end-state already holds
    /// (the artifact is gone), so reporting Success is honest — e.g. the
    /// startup-state backup executor, whose rollback deletes the snapshot
    /// file it created and never touches startup configuration.
    /// </summary>
    private static readonly HashSet<string> IdempotentArtifactRollbacks =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "action.startup.backup-startup-state",
    };

    /// <summary>
    /// Executors excluded from behavioural dry-run invocation because their
    /// dry-run scans live user directories (read-only by design, but
    /// machine-dependent and potentially slow on a developer machine).
    /// Their dry-run behaviour is covered by dedicated parameter/seam tests;
    /// every declaration-level contract in this suite still applies to them.
    /// </summary>
    private static readonly HashSet<string> DryRunInvocationExclusions =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "action.disk.clean-temp-files",
        "action.disk.clean-browser-cache",
        "action.disk.clean-delivery-optimization",
        "action.storage.clean-old-downloads",
    };

    // ── Harness construction ───────────────────────────────────────────────────

    /// <summary>
    /// Pairs an executor with a probe reporting whether any mutating runtime
    /// operation was attempted through its injected seam. Executors without
    /// an injectable seam use a constant-false probe; their mutation safety
    /// is enforced by parameter validation (they fail fast on the empty
    /// parameter set used here) and by their dedicated tests.
    /// </summary>
    private sealed record ExecutorHarness(IActionExecutor Executor, Func<bool> MutationAttempted);

    private static Func<bool> NoSeam => static () => false;

    /// <summary>
    /// Creates the full production executor set, mirroring the registration
    /// list in <c>AppServices</c> (same order, same executors). Seam-equipped
    /// executors receive guarded fakes so no test invocation can ever reach
    /// a real process launch, service operation, registry write, or file move.
    /// </summary>
    private static IReadOnlyList<ExecutorHarness> CreateHarnesses()
    {
        var harnesses = new List<ExecutorHarness>
        {
            // Guided navigation — no seams; dry-run short-circuits before Process.Start.
            new(new OpenTaskManagerExecutor(),     NoSeam),
            new(new OpenTaskManagerGpuExecutor(),  NoSeam),
            new(new OpenTaskSchedulerExecutor(),   NoSeam),
            new(new OpenStorageSenseExecutor(),    NoSeam),
            new(new OpenStartupAppsExecutor(),     NoSeam),
            new(new OpenWindowsSecurityExecutor(), NoSeam),

            // Disk cleanup — unseamed scanners (see DryRunInvocationExclusions).
            new(new TempFileCleanupExecutor(),          NoSeam),
            new(new DeliveryOptimizationCacheExecutor(), NoSeam),
            new(new BrowserCacheCleanupExecutor(),      NoSeam),
        };

        var recycleBin = new GuardedRecycleBinRuntime();
        harnesses.Add(new(new RecycleBinCleanupExecutor(recycleBin),
            () => recycleBin.MutationAttempted));

        var updateCache = new GuardedWindowsUpdateCacheRuntime();
        harnesses.Add(new(new WindowsUpdateCacheExecutor(updateCache),
            () => updateCache.MutationAttempted));

        // Startup management — registry writes guarded behind the service fake.
        var disableStartup = new GuardedStartupManagementService();
        harnesses.Add(new(new StartupAppDisableExecutor(disableStartup),
            () => disableStartup.MutationAttempted));
        var enableStartup = new GuardedStartupManagementService();
        harnesses.Add(new(new StartupAppEnableExecutor(enableStartup),
            () => enableStartup.MutationAttempted));
        var backupStartup = new GuardedStartupManagementService();
        harnesses.Add(new(new StartupStateBackupExecutor(backupStartup),
            () => backupStartup.MutationAttempted));
        var restoreStartup = new GuardedStartupManagementService();
        harnesses.Add(new(new StartupStateRestoreExecutor(restoreStartup),
            () => restoreStartup.MutationAttempted));

        // Repair & recovery — every external command goes through a guarded
        // process runtime that records and refuses the call.
        var sfc = new GuardedProcessRuntime();
        harnesses.Add(new(new SfcScanExecutor(sfc), () => sfc.MutationAttempted));
        var dism = new GuardedProcessRuntime();
        harnesses.Add(new(new DismRestoreHealthExecutor(dism), () => dism.MutationAttempted));
        var dns = new GuardedProcessRuntime();
        harnesses.Add(new(new FlushDnsExecutor(dns), () => dns.MutationAttempted));
        var winsock = new GuardedProcessRuntime();
        harnesses.Add(new(new WinsockResetExecutor(winsock), () => winsock.MutationAttempted));
        var store = new GuardedWindowsStoreResetRuntime();
        harnesses.Add(new(new WindowsStoreResetExecutor(store), () => store.MutationAttempted));
        var network = new GuardedProcessRuntime();
        harnesses.Add(new(new NetworkAdapterResetExecutor(network), () => network.MutationAttempted));

        // Storage intelligence — parameter-driven; fail fast on the empty
        // parameter set used by this suite, before any filesystem access.
        harnesses.Add(new(new DeleteLargeFileExecutor(),      NoSeam));
        harnesses.Add(new(new DeleteDuplicateGroupExecutor(), NoSeam));
        harnesses.Add(new(new CleanOldDownloadsExecutor(),    NoSeam));

        // Process intelligence — parameter-driven; fail fast without a PID.
        harnesses.Add(new(new TerminateProcessExecutor(),     NoSeam));
        harnesses.Add(new(new OpenProcessLocationExecutor(),  NoSeam));

        // Security intelligence.
        harnesses.Add(new(new OpenVirusTotalExecutor(),       NoSeam));

        return harnesses;
    }

    private static ExecutorHarness GetHarness(string actionId) =>
        CreateHarnesses().Single(h =>
            string.Equals(h.Executor.ActionId, actionId, StringComparison.OrdinalIgnoreCase));

    private static ActionExecutionContext NewContext(bool isDryRun) => new()
    {
        IsDryRun            = isDryRun,
        IsElevated          = true,
        ConfirmationGranted = true,
        RequestedBy         = "executor-safety-contract-suite",
        Log                 = new ActionExecutionLog(),
    };

    // ── Guarded fakes ──────────────────────────────────────────────────────────
    // Each fake answers read-only probes with benign empty values and records
    // then refuses any mutating operation. "Refuse" means throwing — which a
    // doctrine-compliant executor must catch and convert into a Failed result,
    // so an accidental mutation attempt surfaces as BOTH a flag assertion
    // failure and (if uncaught) a never-throw contract failure.

    /// <summary>
    /// Stand-in for every process-launching repair runtime. Launching an
    /// external command is always a mutation from the contract suite's
    /// perspective, so every overload records and refuses.
    /// </summary>
    private sealed class GuardedProcessRuntime :
        SfcScanExecutor.ISfcScanRuntime,
        DismRestoreHealthExecutor.IDismRepairRuntime,
        FlushDnsExecutor.IFlushDnsRuntime,
        WinsockResetExecutor.IWinsockResetRuntime,
        NetworkAdapterResetExecutor.INetworkRepairRuntime
    {
        public bool MutationAttempted { get; private set; }

        private ProcessExecutionHelper.RunResult Refuse()
        {
            MutationAttempted = true;
            throw new InvalidOperationException(
                "Contract violation: executor attempted to launch an external " +
                "command during a contract-suite invocation.");
        }

        public ProcessExecutionHelper.RunResult Run(
            string exe, string arguments, ActionExecutionLog log,
            CancellationToken ct, IList<string> outputLines, Action<int> onProgress) => Refuse();

        public ProcessExecutionHelper.RunResult Run(
            string exe, string arguments, ActionExecutionLog log,
            CancellationToken ct, IList<string> outputLines, Action<float> onProgress) => Refuse();

        public ProcessExecutionHelper.RunResult Run(
            string exe, string arguments, ActionExecutionLog log,
            CancellationToken ct, IList<string> outputLines) => Refuse();

        public ProcessExecutionHelper.RunResult Run(
            string exe, string arguments, ActionExecutionLog log,
            CancellationToken ct) => Refuse();
    }

    private sealed class GuardedRecycleBinRuntime : RecycleBinCleanupExecutor.IRecycleBinRuntime
    {
        public bool MutationAttempted { get; private set; }

        // Read-only probe — report an empty recycle bin.
        public (int HResult, long SizeBytes, long ItemCount) Query() => (0, 0L, 0L);

        public int Empty(uint flags)
        {
            MutationAttempted = true;
            throw new InvalidOperationException(
                "Contract violation: executor attempted to empty the recycle " +
                "bin during a contract-suite invocation.");
        }
    }

    private sealed class GuardedWindowsUpdateCacheRuntime :
        WindowsUpdateCacheExecutor.IWindowsUpdateCacheRuntime
    {
        public bool MutationAttempted { get; private set; }

        private InvalidOperationException Refuse(string operation)
        {
            MutationAttempted = true;
            return new InvalidOperationException(
                $"Contract violation: executor attempted '{operation}' during " +
                "a contract-suite invocation.");
        }

        // Read-only probes — point at a path that does not exist so the
        // executor reports an empty/missing cache instead of scanning.
        public string GetCachePath() =>
            Path.Combine(Path.GetTempPath(), "lucid-contract-suite-nonexistent", "Download");
        public string NewStagingRoot() =>
            Path.Combine(Path.GetTempPath(), "lucid-contract-suite-nonexistent", "staging");
        public bool DirectoryExists(string path) => false;
        public (int FileCount, long TotalBytes) Scan(
            string rootPath, int minAgeMinutes, CancellationToken ct) => (0, 0L);
        public IEnumerable<FileInfo> EnumerateFiles(
            string rootPath, int minAgeMinutes, CancellationToken ct) =>
            Enumerable.Empty<FileInfo>();

        // Mutating operations — record and refuse.
        public void CreateDirectory(string path) => throw Refuse(nameof(CreateDirectory));
        public void MoveFileToStaging(string sourcePath, string stagingPath) =>
            throw Refuse(nameof(MoveFileToStaging));
        public void DeleteFile(string path) => throw Refuse(nameof(DeleteFile));
        public bool TryStopService(string serviceName, ActionExecutionLog log) =>
            throw Refuse(nameof(TryStopService));
        public void StartService(string serviceName, ActionExecutionLog log) =>
            throw Refuse(nameof(StartService));
        public void RestoreFromStaging(
            IReadOnlyList<string> manifest, string stagingDir, ActionExecutionLog log) =>
            throw Refuse(nameof(RestoreFromStaging));
        public void TryDeleteDirectory(string stagingDir, ActionExecutionLog log) =>
            throw Refuse(nameof(TryDeleteDirectory));
        public bool TryWriteManifest(
            string stagingDir, IReadOnlyList<string> manifest, ActionExecutionLog log) =>
            throw Refuse(nameof(TryWriteManifest));
    }

    private sealed class GuardedWindowsStoreResetRuntime :
        WindowsStoreResetExecutor.IWindowsStoreResetRuntime
    {
        public bool MutationAttempted { get; private set; }

        public WindowsStoreResetExecutor.WindowsStoreResetRunResult Run(CancellationToken ct)
        {
            MutationAttempted = true;
            throw new InvalidOperationException(
                "Contract violation: executor attempted to launch wsreset " +
                "during a contract-suite invocation.");
        }
    }

    /// <summary>
    /// Startup management fake. The service contract specifies bool-returning,
    /// never-throwing write operations — so mutating calls record the attempt
    /// and report failure instead of throwing, honouring that contract while
    /// still surfacing the violation through the flag assertion.
    /// </summary>
    private sealed class GuardedStartupManagementService : IStartupManagementService
    {
        public bool MutationAttempted { get; private set; }

        public IReadOnlyList<StartupEntry> GetAllEntries() => Array.Empty<StartupEntry>();
        public bool IsDisabled(string entryName, StartupLocation location) => false;
        public string? GetApprovedRawValue(string entryName, StartupLocation location) => null;

        public bool DisableEntry(string entryName, StartupLocation location)
        {
            MutationAttempted = true;
            return false;
        }

        public bool EnableEntry(string entryName, StartupLocation location)
        {
            MutationAttempted = true;
            return false;
        }

        public bool RestoreApprovedRawValue(
            string entryName, StartupLocation location, string? hexValue)
        {
            MutationAttempted = true;
            return false;
        }
    }
}
