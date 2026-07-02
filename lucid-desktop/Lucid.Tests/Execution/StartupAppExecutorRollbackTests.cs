using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Lucid.Services.Startup;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Behavioral coverage for the registry-writing startup executors: the
/// mutate-and-restore round trip must be byte-for-byte, including the
/// easy-to-regress "no prior StartupApproved value" branch where rollback
/// must delete the value rather than write anything. A regression here
/// corrupts the user's boot configuration with a rollback that lies.
/// </summary>
public sealed class StartupAppExecutorRollbackTests
{
    private const string EntryName = "TestApp";

    [Fact]
    public async Task Disable_ThenRollback_RestoresExactPreviousValue()
    {
        var registry = new FakeStartupService();
        registry.Seed(EntryName, StartupLocation.HkcuRun, FakeStartupService.EnabledHex);
        var executor = new StartupAppDisableExecutor(registry);

        var execute = await executor.ExecuteAsync(NewContext());

        execute.Status.Should().Be(ActionExecutionStatus.Success);
        execute.CanRollback.Should().BeTrue();
        registry.RawValue(EntryName, StartupLocation.HkcuRun)
            .Should().Be(FakeStartupService.DisabledHex, "disable must write the disabled marker");

        var rollback = await executor.RollbackAsync(execute.RollbackToken!, NewContext());

        rollback.Status.Should().Be(ActionExecutionStatus.Success);
        registry.RawValue(EntryName, StartupLocation.HkcuRun)
            .Should().Be(FakeStartupService.EnabledHex,
                "rollback must restore the exact previous raw value");
    }

    [Fact]
    public async Task Disable_WithNoPriorValue_RollbackDeletesTheValue()
    {
        // The entry was implicitly enabled (no StartupApproved value at all).
        // Rollback must restore that absence — not write an 'enabled' value.
        var registry = new FakeStartupService();
        var executor = new StartupAppDisableExecutor(registry);

        var execute = await executor.ExecuteAsync(NewContext());

        execute.Status.Should().Be(ActionExecutionStatus.Success);
        registry.RawValue(EntryName, StartupLocation.HkcuRun).Should().NotBeNull();

        var rollback = await executor.RollbackAsync(execute.RollbackToken!, NewContext());

        rollback.Status.Should().Be(ActionExecutionStatus.Success);
        registry.RawValue(EntryName, StartupLocation.HkcuRun)
            .Should().BeNull("rollback of an implicitly-enabled entry must delete the value");
    }

    [Fact]
    public async Task Enable_ThenRollback_RestoresPreviousDisabledValue()
    {
        var registry = new FakeStartupService();
        registry.Seed(EntryName, StartupLocation.HkcuRun, FakeStartupService.DisabledHex);
        var executor = new StartupAppEnableExecutor(registry);

        var execute = await executor.ExecuteAsync(NewContext());

        execute.Status.Should().Be(ActionExecutionStatus.Success);
        registry.RawValue(EntryName, StartupLocation.HkcuRun)
            .Should().Be(FakeStartupService.EnabledHex);

        var rollback = await executor.RollbackAsync(execute.RollbackToken!, NewContext());

        rollback.Status.Should().Be(ActionExecutionStatus.Success);
        registry.RawValue(EntryName, StartupLocation.HkcuRun)
            .Should().Be(FakeStartupService.DisabledHex,
                "rolling back an enable must restore the disabled marker byte-for-byte");
    }

    [Fact]
    public async Task Disable_HklmWithoutElevation_FailsWithoutTouchingRegistry()
    {
        var registry = new FakeStartupService();
        registry.Seed(EntryName, StartupLocation.HklmRun, FakeStartupService.EnabledHex);
        var executor = new StartupAppDisableExecutor(registry);

        var result = await executor.ExecuteAsync(
            NewContext(location: StartupLocation.HklmRun, isElevated: false));

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        registry.WriteCalls.Should().Be(0, "no registry write may happen without elevation");
        registry.RawValue(EntryName, StartupLocation.HklmRun)
            .Should().Be(FakeStartupService.EnabledHex);
    }

    [Fact]
    public async Task Disable_DryRun_DoesNotWrite()
    {
        var registry = new FakeStartupService();
        registry.Seed(EntryName, StartupLocation.HkcuRun, FakeStartupService.EnabledHex);
        var executor = new StartupAppDisableExecutor(registry);

        var result = await executor.ExecuteAsync(NewContext(isDryRun: true));

        result.Status.Should().Be(ActionExecutionStatus.DryRunSuccess);
        registry.WriteCalls.Should().Be(0, "dry-run must not mutate the registry");
    }

    [Fact]
    public async Task Disable_WhenRegistryWriteFails_ReturnsFailedWithoutRollbackToken()
    {
        var registry = new FakeStartupService { FailWrites = true };
        registry.Seed(EntryName, StartupLocation.HkcuRun, FakeStartupService.EnabledHex);
        var executor = new StartupAppDisableExecutor(registry);

        var result = await executor.ExecuteAsync(NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.CanRollback.Should().BeFalse(
            "a failed write must not hand out a rollback token");
    }

    [Fact]
    public async Task Rollback_WithCorruptToken_FailsWithoutTouchingRegistry()
    {
        var registry = new FakeStartupService();
        var executor = new StartupAppDisableExecutor(registry);

        var result = await executor.RollbackAsync("{ not json", NewContext());

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        registry.WriteCalls.Should().Be(0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ActionExecutionContext NewContext(
        StartupLocation location = StartupLocation.HkcuRun,
        bool isElevated = true,
        bool isDryRun = false) => new()
    {
        IsDryRun = isDryRun,
        IsElevated = isElevated,
        ConfirmationGranted = true,
        RequestedBy = "test",
        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [StartupAppDisableExecutor.ParamEntryName]     = EntryName,
            [StartupAppDisableExecutor.ParamEntryLocation] = location.ToString(),
        },
        Log = new ActionExecutionLog(),
    };

    /// <summary>
    /// In-memory StartupApproved store: behaves like the real registry-backed
    /// service (Windows uses a leading 0x03 byte for disabled, 0x02 for
    /// enabled) so the tests exercise real round-trip semantics rather than
    /// mock-verify call sequences.
    /// </summary>
    private sealed class FakeStartupService : IStartupManagementService
    {
        public const string EnabledHex  = "020000000000000000000000";
        public const string DisabledHex = "030000000000000000000000";

        private readonly Dictionary<(string Name, StartupLocation Loc), string> _approved = [];

        public bool FailWrites { get; set; }
        public int  WriteCalls { get; private set; }

        public void Seed(string name, StartupLocation loc, string hex) =>
            _approved[(name, loc)] = hex;

        public string? RawValue(string name, StartupLocation loc) =>
            _approved.TryGetValue((name, loc), out var v) ? v : null;

        // ── IStartupManagementService ─────────────────────────────────────────

        public IReadOnlyList<StartupEntry> GetAllEntries() => [];

        public bool IsDisabled(string entryName, StartupLocation location) =>
            RawValue(entryName, location) is { } v &&
            v.StartsWith("03", StringComparison.Ordinal);

        public bool DisableEntry(string entryName, StartupLocation location) =>
            Write(entryName, location, DisabledHex);

        public bool EnableEntry(string entryName, StartupLocation location) =>
            Write(entryName, location, EnabledHex);

        public string? GetApprovedRawValue(string entryName, StartupLocation location) =>
            RawValue(entryName, location);

        public bool RestoreApprovedRawValue(
            string entryName, StartupLocation location, string? hexValue)
        {
            if (FailWrites) return false;
            WriteCalls++;

            if (string.IsNullOrEmpty(hexValue))
                _approved.Remove((entryName, location));
            else
                _approved[(entryName, location)] = hexValue;

            return true;
        }

        private bool Write(string entryName, StartupLocation location, string hex)
        {
            if (FailWrites) return false;
            WriteCalls++;
            _approved[(entryName, location)] = hex;
            return true;
        }
    }
}
