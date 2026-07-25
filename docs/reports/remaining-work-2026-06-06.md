# Lucid - Remaining Work

Updated 2026-06-06 after production-readiness audit.

## Highest Priority

1. Broaden safety tests around destructive executors, registry writes, process launch, and rollback behavior.
2. Continue replacing silent repository/service failures with structured diagnostics events.
3. Add Rust scanner unit/integration tests before expanding native scanning.
4. Start an incremental `AppServices` migration plan through a small service-provider shim; do not do a big-bang DI rewrite.
5. Add SQLite schema v2 planning for operation history and longer replay/analytics windows.

## Medium Priority

- Improve persistence durability tests around queue overflow, direct writes, and final flush behavior.
- Expand local-only endpoint enforcement tests for every network-capable service.
- Audit executor rollback coverage and classify which actions are inherently non-rollbackable.
- Reduce `Lucid.App.csproj` selective-compilation fragility once the active feature set is stable.

## Deferred

- Major NuGet upgrades, especially WindowsAppSDK and SQLite package major versions.
- Broad UI redesigns.
- Distributed multi-device sync.
- Full dependency-injection migration.

