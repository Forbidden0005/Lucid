using System.Text.RegularExpressions;
using FluentAssertions;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Xunit;

namespace Lucid.Tests.Execution;

public sealed class ActionExecutionMetadataCatalogTests
{
    [Fact]
    public void CatalogContainsMetadata_ForAllExecutorActionIdsInSource()
    {
        var repoRoot = FindRepoRoot();

        // Executors live in Lucid.Core since the library extraction; scan the
        // App location too so an executor mistakenly added there is still
        // held to the catalog contract.
        var executorRoots = new[]
        {
            Path.Combine(repoRoot, "lucid-desktop", "Lucid.Core", "Services", "Execution", "Executors"),
            Path.Combine(repoRoot, "lucid-desktop", "Lucid.App", "Services", "Execution", "Executors"),
        };

        var sourceActionIds = executorRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly))
            .SelectMany(file => ExtractActionIds(File.ReadAllText(file)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var catalogActionIds = ActionExecutionMetadataCatalog.All
            .Select(entry => entry.ActionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        sourceActionIds.Should().NotBeEmpty();
        catalogActionIds.Should().Contain(sourceActionIds,
            "every executor action ID in source should have a metadata entry");
    }

    [Fact]
    public void LinkedExecutors_HaveMetadataConsistentWithRuntimeDeclarations()
    {
        IActionExecutor[] executors =
        [
            new DeleteLargeFileExecutor(),
            new TempFileCleanupExecutor(),
            new WindowsUpdateCacheExecutor(),
            new RecycleBinCleanupExecutor(),
        ];

        foreach (var executor in executors)
        {
            var metadata = ActionExecutionMetadataCatalog.GetRequired(executor.ActionId);

            metadata.RequiredPrivilege.Should().Be(executor.RequiredPrivilege);
            metadata.RequiresConfirmation.Should().Be(executor.RequiresConfirmation);
            metadata.SupportsDryRun.Should().Be(executor.SupportsDryRun);
            metadata.SupportsRollback.Should().Be(executor.SupportsRollback);
            metadata.ResourceClass.Should().Be(ActionExecutionResourceClass.Cleanup);
            metadata.ConsentNote.Should().NotBeNullOrWhiteSpace();
            metadata.FailureModeSummary.Should().NotBeNullOrWhiteSpace();
            metadata.DiagnosticsCategory.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void RegistryRegistration_FailsWhenExecutorMetadataDrifts()
    {
        var registry = new ActionExecutorRegistry();
        var badExecutor = new FakeExecutor(
            actionId: "action.disk.clean-temp-files",
            requiredPrivilege: ActionPrivilegeLevel.Administrator,
            requiresConfirmation: false,
            supportsDryRun: true,
            supportsRollback: true);

        var act = () => registry.Register(badExecutor);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*metadata*");
    }

    private static IEnumerable<string> ExtractActionIds(string text)
    {
        var regex = new Regex("action\\.[a-z0-9\\-.]+", RegexOptions.CultureInvariant);
        return regex.Matches(text).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "lucid-desktop", "Lucid.App", "Lucid.App.csproj");
            if (File.Exists(candidate))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root for executor metadata audit.");
    }

    private sealed class FakeExecutor : IActionExecutor
    {
        public FakeExecutor(
            string actionId,
            ActionPrivilegeLevel requiredPrivilege,
            bool requiresConfirmation,
            bool supportsDryRun,
            bool supportsRollback)
        {
            ActionId = actionId;
            RequiredPrivilege = requiredPrivilege;
            RequiresConfirmation = requiresConfirmation;
            SupportsDryRun = supportsDryRun;
            SupportsRollback = supportsRollback;
        }

        public string ActionId { get; }
        public ActionPrivilegeLevel RequiredPrivilege { get; }
        public bool RequiresConfirmation { get; }
        public bool SupportsDryRun { get; }
        public bool SupportsRollback { get; }

        public Task<ActionExecutionResult> ExecuteAsync(
            ActionExecutionContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionExecutionResult.Succeeded(
                ActionId, "ok", TimeSpan.Zero, context.Log.Build()));

        public Task<ActionExecutionResult> RollbackAsync(
            string rollbackToken,
            ActionExecutionContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionExecutionResult.Succeeded(
                ActionId, "ok", TimeSpan.Zero, context.Log.Build()));
    }
}
