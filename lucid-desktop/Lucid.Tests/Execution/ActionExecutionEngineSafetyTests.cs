using FluentAssertions;
using Lucid.Services.Execution;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Verifies the engine-level safety gates that must run before any executor can
/// mutate the machine. A regression here would let risky actions bypass trust
/// checks even if individual executors are well-behaved.
/// </summary>
public sealed class ActionExecutionEngineSafetyTests
{
    [Fact]
    public async Task ExecuteAsync_RequiresElevation_BlocksDispatch()
    {
        var executor = new TestExecutor(
            actionId: "action.test.admin",
            requiredPrivilege: ActionPrivilegeLevel.Administrator);
        var engine = CreateEngine(executor);
        var context = NewContext(isElevated: false);

        var result = await engine.ExecuteAsync(executor.ActionId, context);

        result.Status.Should().Be(ActionExecutionStatus.RequiresElevation);
        result.Log.Should().BeEmpty();
        executor.ExecuteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_RequiresConfirmation_BlocksDispatch()
    {
        var executor = new TestExecutor(
            actionId: "action.test.confirm",
            requiresConfirmation: true);
        var engine = CreateEngine(executor);
        var context = NewContext(confirmationGranted: false);

        var result = await engine.ExecuteAsync(executor.ActionId, context);

        result.Status.Should().Be(ActionExecutionStatus.RequiresConfirmation);
        result.Log.Should().BeEmpty();
        executor.ExecuteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_DryRunUnsupported_ReturnsSyntheticDryRunWithoutCallingExecutor()
    {
        var executor = new TestExecutor(
            actionId: "action.test.no-dry-run",
            supportsDryRun: false);
        var engine = CreateEngine(executor);
        var context = NewContext(isDryRun: true);

        var result = await engine.ExecuteAsync(executor.ActionId, context);

        result.Status.Should().Be(ActionExecutionStatus.DryRunSuccess);
        result.IsDryRun.Should().BeTrue();
        result.Message.Should().Contain("No changes were made");
        result.Log.Should().ContainSingle(entry =>
            entry.Message.Contains("does not implement dry-run simulation"));
        executor.ExecuteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ExecutorThrows_ReturnsFailedWithErrorDetail()
    {
        var executor = new TestExecutor(
            actionId: "action.test.exception",
            executeImpl: (_, _) => throw new InvalidOperationException("boom"));
        var engine = CreateEngine(executor);
        var context = NewContext();

        var result = await engine.ExecuteAsync(executor.ActionId, context);

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.ErrorDetail.Should().Contain("InvalidOperationException");
        result.Log.Should().Contain(entry =>
            entry.Level == ActionLogLevel.Error &&
            entry.Message.Contains("Unhandled exception"));
        executor.ExecuteCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ExecutorCancellation_ReturnsCancelled()
    {
        var executor = new TestExecutor(
            actionId: "action.test.cancel",
            executeImpl: (_, _) => throw new OperationCanceledException());
        var engine = CreateEngine(executor);
        var context = NewContext();

        var result = await engine.ExecuteAsync(executor.ActionId, context);

        result.Status.Should().Be(ActionExecutionStatus.Cancelled);
        result.Message.Should().Contain("cancelled");
        result.Log.Should().Contain(entry =>
            entry.Level == ActionLogLevel.Warning &&
            entry.Message.Contains("was cancelled"));
    }

    [Fact]
    public async Task RollbackAsync_UnsupportedRollback_ReturnsFailedWithoutCallingExecutor()
    {
        var executor = new TestExecutor(
            actionId: "action.test.no-rollback",
            supportsRollback: false);
        var engine = CreateEngine(executor);
        var context = NewContext();

        var result = await engine.RollbackAsync(executor.ActionId, "token", context);

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.Message.Should().Contain("does not support rollback");
        result.Log.Should().ContainSingle(entry =>
            entry.Level == ActionLogLevel.Warning &&
            entry.Message.Contains("does not support rollback"));
        executor.RollbackCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RollbackAsync_RequiresElevation_BlocksRollbackDispatch()
    {
        var executor = new TestExecutor(
            actionId: "action.test.rollback-admin",
            requiredPrivilege: ActionPrivilegeLevel.Administrator,
            supportsRollback: true);
        var engine = CreateEngine(executor);
        var context = NewContext(isElevated: false);

        var result = await engine.RollbackAsync(executor.ActionId, "token", context);

        result.Status.Should().Be(ActionExecutionStatus.RequiresElevation);
        result.Log.Should().BeEmpty();
        executor.RollbackCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RollbackAsync_ExecutorThrows_ReturnsFailedWithErrorDetail()
    {
        var executor = new TestExecutor(
            actionId: "action.test.rollback-exception",
            supportsRollback: true,
            rollbackImpl: (_, _, _) => throw new InvalidOperationException("rollback boom"));
        var engine = CreateEngine(executor);
        var context = NewContext();

        var result = await engine.RollbackAsync(executor.ActionId, "token", context);

        result.Status.Should().Be(ActionExecutionStatus.Failed);
        result.ErrorDetail.Should().Contain("rollback boom");
        result.Log.Should().Contain(entry =>
            entry.Level == ActionLogLevel.Error &&
            entry.Message.Contains("Unhandled exception during rollback"));
        executor.RollbackCallCount.Should().Be(1);
    }

    private static ActionExecutionEngine CreateEngine(params IActionExecutor[] executors)
    {
        var registry = new ActionExecutorRegistry();
        registry.RegisterAll(executors);
        return new ActionExecutionEngine(registry);
    }

    private static ActionExecutionContext NewContext(
        bool isDryRun = false,
        bool isElevated = true,
        bool confirmationGranted = true) => new()
    {
        IsDryRun = isDryRun,
        IsElevated = isElevated,
        ConfirmationGranted = confirmationGranted,
        RequestedBy = "test",
        Log = new ActionExecutionLog(),
    };

    private sealed class TestExecutor : IActionExecutor
    {
        private readonly Func<ActionExecutionContext, CancellationToken, ActionExecutionResult> _executeImpl;
        private readonly Func<string, ActionExecutionContext, CancellationToken, ActionExecutionResult> _rollbackImpl;

        public TestExecutor(
            string actionId,
            ActionPrivilegeLevel requiredPrivilege = ActionPrivilegeLevel.Standard,
            bool requiresConfirmation = false,
            bool supportsDryRun = true,
            bool supportsRollback = false,
            Func<ActionExecutionContext, CancellationToken, ActionExecutionResult>? executeImpl = null,
            Func<string, ActionExecutionContext, CancellationToken, ActionExecutionResult>? rollbackImpl = null)
        {
            ActionId = actionId;
            RequiredPrivilege = requiredPrivilege;
            RequiresConfirmation = requiresConfirmation;
            SupportsDryRun = supportsDryRun;
            SupportsRollback = supportsRollback;
            _executeImpl = executeImpl ?? ((context, _) =>
                ActionExecutionResult.Succeeded(ActionId, "ok", TimeSpan.Zero, context.Log.Build()));
            _rollbackImpl = rollbackImpl ?? ((_, context, _) =>
                ActionExecutionResult.Succeeded(ActionId, "rollback ok", TimeSpan.Zero, context.Log.Build()));
        }

        public int ExecuteCallCount { get; private set; }
        public int RollbackCallCount { get; private set; }

        public string ActionId { get; }
        public ActionPrivilegeLevel RequiredPrivilege { get; }
        public bool RequiresConfirmation { get; }
        public bool SupportsDryRun { get; }
        public bool SupportsRollback { get; }

        public Task<ActionExecutionResult> ExecuteAsync(
            ActionExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            ExecuteCallCount++;
            return Task.FromResult(_executeImpl(context, cancellationToken));
        }

        public Task<ActionExecutionResult> RollbackAsync(
            string rollbackToken,
            ActionExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            RollbackCallCount++;
            return Task.FromResult(_rollbackImpl(rollbackToken, context, cancellationToken));
        }
    }
}
