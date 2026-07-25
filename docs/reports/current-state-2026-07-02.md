> ARCHIVED SNAPSHOT (2026-07-02) — superseded by `ROADMAP.md`, do not act on this.

# Lucid - Current State

Generated from repo inspection on 2026-07-02. Code is the source of truth.

## Build And Tests

- Active branch: `main`
- Primary solution: `lucid-desktop/Lucid.slnx`
- Required build command: `dotnet build Lucid.slnx -c Debug -p:Platform=x64`
- Test command: `dotnet test Lucid.Tests\Lucid.Tests.csproj -c Debug -p:Platform=x64`
- Rust command: `cargo test` from `lucid-native/`
- Full release gate: `scripts/verify-release.ps1` (build, tests, publish, smoke,
  package, update manifest/feed, installer round-trip, support bundle)

## Current Verified Counts

- Non-generated C# app files: 528
- XAML files: 41
- Unit test files: 34
- Unit tests: 296 (all passing)
- Rust tests: 19 (all passing)

## Important Architecture Notes

- `Lucid.App` is a WinUI 3 desktop app targeting .NET 8.
- `Lucid.Tests` links pure production service files directly instead of referencing
  the WinUI app project. New production files under test must be added to the
  `<Compile Include>` list in `Lucid.Tests.csproj`.
- `AppServices.cs` remains the central static service registry and the largest
  architectural pressure point. Do not rewrite it wholesale; migrate incrementally
  behind tested module boundaries.
- `Lucid.App.csproj` uses default-glob compilation with documented
  `<Compile Remove>` exclusions, enforced by `scripts/check-app-source-includes.ps1`.
- SQLite persistence exists for telemetry, timeline, insight history, and
  recommendation outcomes, with durability tests (queue overflow, corrupt-DB
  recovery, flush-on-shutdown).
- The Rust native scanner compiles and carries 19 tests (long paths,
  junction cycles, FFI boundary safety).
- Privacy-sensitive subsystems are opt-in, OFF by default, with live Settings
  toggles: desktop context capture (`DesktopContextAwarenessEnabled`) and
  LAN device sync (`DeviceSyncEnabled`).
- Sync payloads use authenticated encryption (AES-256-GCM via
  `SyncEnvelopeCrypto`); tampered or truncated envelopes fail closed.
- Destructive executors route path parameters through
  `Services/Execution/Validation/ExecutionPathGuard` (system/driver/credential
  denylist) and fail — never silently escalate to permanent deletion — when
  rollback staging is unavailable.

## Current Production Risks

- `AppServices.cs` (1800+ lines, ~90 static fields) — hand-ordered init;
  incremental DI migration is the agreed path (see docs/SCOPE_RECONCILIATION.md
  and the audit tracker).
- Several large ViewModels (SimulationViewModel ~970 lines) carry
  service-layer concerns; SRP splits pending.
- Docs are partially historical: CODEX.md, docs/active-file-inventory.md, and
  docs/Structure.txt are candidates for archiving; REMAINING_WORK.md overlaps
  AUDIT_ROADMAP.md.
- Dependency upgrades include major-version jumps and should be handled in a
  separate pass with targeted regression testing.
