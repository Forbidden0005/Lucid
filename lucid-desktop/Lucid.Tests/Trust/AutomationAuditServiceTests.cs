using System.Text.Json;
using FluentAssertions;
using Lucid.Services.Automation;
using Lucid.Services.Trust;
using Xunit;

namespace Lucid.Tests.Trust;

/// <summary>
/// Covers the audit ledger's persistence integrity: the ledger is the
/// accountability record for autonomous actions, so writes must be
/// serialized, atomic, and never silently lost across restarts.
/// </summary>
public sealed class AutomationAuditServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _ledgerPath;

    public AutomationAuditServiceTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "lucid-audit-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _ledgerPath = Path.Combine(_dir, "audit-log.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task Entries_PersistToDisk_AndSurviveReload()
    {
        using (var service = new AutomationAuditService(auditFilePath: _ledgerPath))
        {
            service.RecordAutoApproved(NewStep("action.test.one"), "wf-1", PermissionScope.SystemInfoRead);
            service.RecordAutoApproved(NewStep("action.test.two"), "wf-1", PermissionScope.SystemInfoRead);

            await WaitForLedgerAsync(entries => entries.Count == 2);
        }

        using var reloaded = new AutomationAuditService(auditFilePath: _ledgerPath);
        reloaded.GetAllEntries().Should().HaveCount(2,
            "persisted entries must survive an app restart");
    }

    [Fact]
    public async Task ConcurrentRecording_ProducesOneCompleteValidLedger()
    {
        const int writers = 8, perWriter = 5;

        using (var service = new AutomationAuditService(auditFilePath: _ledgerPath))
        {
            // Hammer the ledger from parallel threads — every mutation kicks a
            // fire-and-forget persist, which previously raced each other on
            // one file with no synchronization.
            var tasks = Enumerable.Range(0, writers).Select(w => Task.Run(() =>
            {
                for (int i = 0; i < perWriter; i++)
                    service.RecordAutoApproved(
                        NewStep($"action.test.{w}-{i}"), $"wf-{w}", PermissionScope.SystemInfoRead);
            }));
            await Task.WhenAll(tasks);

            await WaitForLedgerAsync(entries => entries.Count == writers * perWriter);
        }

        // The file must be one complete, parseable serialization — never
        // interleaved or torn output from overlapping writers.
        var final = JsonSerializer.Deserialize<List<JsonElement>>(
            File.ReadAllText(_ledgerPath));
        final.Should().NotBeNull();
        final!.Count.Should().Be(writers * perWriter);
    }

    [Fact]
    public async Task CompletedWrite_LeavesNoTempFileBehind()
    {
        using (var service = new AutomationAuditService(auditFilePath: _ledgerPath))
        {
            service.RecordAutoApproved(NewStep("action.test.tmp"), "wf-tmp", PermissionScope.SystemInfoRead);
            await WaitForLedgerAsync(entries => entries.Count == 1);
        }

        File.Exists(_ledgerPath + ".tmp").Should().BeFalse(
            "the write-then-rename must consume its temp file");
        File.Exists(_ledgerPath).Should().BeTrue();
    }

    [Fact]
    public void CorruptLedgerOnDisk_StartsEmpty_WithoutThrowing()
    {
        File.WriteAllText(_ledgerPath, "{ this is not valid json");

        using var service = new AutomationAuditService(auditFilePath: _ledgerPath);

        service.GetAllEntries().Should().BeEmpty(
            "a corrupt ledger must degrade to empty rather than crash startup");
    }

    [Fact]
    public async Task Dispose_AllowsInFlightWriteToFinish()
    {
        var service = new AutomationAuditService(auditFilePath: _ledgerPath);
        service.RecordAutoApproved(NewStep("action.test.flush"), "wf-flush", PermissionScope.SystemInfoRead);

        // Dispose immediately — the grace window should let the pending
        // fire-and-forget persist land before handles are released.
        service.Dispose();

        await WaitForLedgerAsync(entries => entries.Count == 1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task WaitForLedgerAsync(Func<List<JsonElement>, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(_ledgerPath))
                {
                    var entries = JsonSerializer.Deserialize<List<JsonElement>>(
                        File.ReadAllText(_ledgerPath));
                    if (entries is not null && condition(entries)) return;
                }
            }
            catch (JsonException)
            {
                // A torn/partial file would keep failing here and time out below,
                // which is exactly the failure this suite exists to catch.
            }
            catch (IOException)
            {
                // Writer still holds the file — retry.
            }

            await Task.Delay(50);
        }

        throw new TimeoutException(
            $"Ledger at {_ledgerPath} did not reach the expected state within 10s.");
    }

    private static AutomationStep NewStep(string actionId) => new()
    {
        Id = AutomationStep.NewId(),
        Title = "Test Step",
        Narration = "Lucid wants to run a test step.",
        Rationale = "The current condition suggests this step may help.",
        ActionId = actionId,
        Risk = AutomationRiskLevel.Low,
        RequiresConfirmation = false,
        RollbackAvailable = false,
        ExpectedOutcome = "A test outcome will be observed.",
        HowToUndo = "Undo is not required for this test."
    };
}
