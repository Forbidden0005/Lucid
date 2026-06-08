# Lucid Roadmap

This roadmap is based on repository inspection and verification on 2026-06-07. It separates what exists from what is required before Lucid can be treated as a professional production Windows application.

## Current Verified Baseline

Lucid currently has:

- A WinUI 3 application under `lucid-desktop/Lucid.App`.
- A test project under `lucid-desktop/Lucid.Tests`.
- A Rust workspace under `lucid-native` with the `lucid-scanner` native module.
- SQLite persistence code, runtime governance code, diagnostics code, trust/integrity code, operational intelligence services, storage analysis, replay, simulation, privacy, companion overlay, and many UI pages.
- 27 XAML view files, 41 ViewModel files, and 436 C# files under `Lucid.App/Services`.
- A GitHub Actions workflow for Windows build and tests.

Verified commands:

```powershell
cd lucid-desktop
dotnet build Lucid.slnx -c Debug -p:Platform=x64 --no-restore
dotnet test Lucid.Tests\Lucid.Tests.csproj -c Debug -p:Platform=x64 --no-restore

cd ..\lucid-native
cargo test
```

Verified results:

- WinUI solution build passes with 0 warnings and 0 errors.
- xUnit test suite passes 53 tests.
- Rust tests pass but currently run 0 tests.

## Completed

This section is the canonical record of work that is done and should no longer be treated as open.

### Verified Baseline Already In Place

- WinUI 3 application scaffold and active solution under `lucid-desktop/Lucid.App` and `lucid-desktop/Lucid.slnx`.
- xUnit test project under `lucid-desktop/Lucid.Tests`.
- Rust native workspace under `lucid-native` with `lucid-scanner`.
- SQLite persistence, runtime governance, diagnostics, trust/integrity, replay, simulation, privacy, and companion-related code paths present in the tree.
- GitHub Actions workflow for Windows build and test.

### Documentation And Governance Completed

- Root project docs were rewritten to match the inspected codebase and production-hardening direction: `README.md`, `ONBOARDING.md`, `PROJECT_INTEGRITY.md`, and `ROADMAP.md`.
- `CODEX.md` explicitly requires roadmap review before every task and roadmap maintenance after every completed task.
- `.gitignore` now excludes generated `TestResults` artifacts from local verification runs.
- Historical root reports were moved under `docs/reports/`, and `docs/repository-hygiene.md` now documents which files stay active at the root.
- Git-tracked root doc casing is now normalized to `README.md` and `ROADMAP.md`.
- Active stale-name cleanup is complete. Remaining `ExplainMyPC` references are historical only and live in `_archive/` or `docs/reports/`.

### Recently Completed Repairs

- CI unit test workflow was corrected so the test job does not assume compiled artifacts from another GitHub runner. The fix removed `--no-build` from the `dotnet test` step in `.github/workflows/lucid-build.yml`.
- `setup.ps1` now resolves the solution and launch paths from the repo location instead of the stale `ExplainMyPC` path.
- `scripts/verify-dev.ps1` was added as a one-command local verification entrypoint for restore, build, C# tests, and Rust tests.
- `scripts/check-app-source-includes.ps1` now verifies that active C# files under `ViewModels`, `Services`, and `Core` are either explicitly compiled or intentionally documented as exclusions.
- `docs/active-file-inventory.md` now records the active application file counts and the current intentional non-compiled files.
- Release verification now exists through `scripts/verify-dev.ps1 -Configuration Release -PublishApp` and `scripts/verify-release.ps1`.
- CI now proves Debug and Release lanes separately and uploads an unpackaged self-contained `win-x64` publish artifact.
- `docs/release-packaging.md` now records the current packaging decision: unpackaged self-contained `win-x64` first, MSIX deferred until packaging metadata, signing, and upgrade behavior are designed and verified.
- Rust scanner tests now cover missing-root failure, file-root rejection, directory/file/byte counting, top-file ordering, FFI argument validation, version reporting, and owned-string cleanup.
- Rust scanner root validation now rejects nonexistent or non-directory roots instead of silently returning empty success results.
- Execution engine safety tests now cover pre-flight elevation and confirmation gates, synthetic dry-run behavior, rollback support gating, and exception containment for execution and rollback paths.
- `DeleteLargeFileExecutor` tests now cover missing-path failure, rollback token issuance after staged deletion, successful restore from staging, and safe rollback failure when the original path is already occupied by newer data.
- Shared `CleanupScanner` rollback tests now cover missing staging, missing manifests, successful staged-file restoration with staging cleanup, and partial restore behavior when some staged files are missing.
- `TempFileCleanupExecutor` rollback tests now cover missing-manifest failure, successful staged-file restoration with staging cleanup, and mid-rollback cancellation that preserves remaining staged data for retry.
- `WindowsUpdateCacheExecutor` rollback tests now cover missing-manifest failure, successful staged-file restoration with staging cleanup, and mid-rollback cancellation that preserves remaining staged data for retry.
- `WindowsUpdateCacheExecutor` now has a narrow runtime seam for cache path resolution, staging, per-file removal, and service control, plus live cleanup tests for partial success, cancellation rollback, and unconditional restart behavior without touching machine state.
- `SQLitePersistenceService` now has a deterministic internal constructor for explicit database paths and optional timer startup, plus persistence tests for schema initialization, queued flush behavior, batch-size limits, and final flush on dispose.
- `SQLitePersistenceService.FlushQueueAsync` no longer drops one queued write at the flush batch boundary; the dequeue loop now respects `MaxFlushBatchSize` before removing work from the queue.
- `PrivacyPermissionWriter` now has an internal registry-root/path seam for deterministic tests, plus coverage for allow writes, deny-based revocation writes, and the non-creation contract when an app permission key does not already exist.
- Guided storage workflow copy no longer claims downloads are "safe to delete"; it now frames deletion as a reviewed candidate action.
- Operational language policy tests now cover prohibited-term compliance, sanitization, and an active-source audit over user-facing literals in Services, ViewModels, and Views.
- `RecycleBinCleanupExecutor` now has a narrow shell-runtime seam, plus tests for dry-run preview, already-empty success, tolerated shell reset behavior, explicit shell failure, and non-reversible rollback refusal.
- `SQLitePersistenceService` now has migration-path tests for legacy schema version `0` databases and idempotent initialization of current-schema databases.
- Action executor metadata is now explicit in `ActionExecutionMetadataCatalog`, validated at registry registration time, and audited by tests against executor source and linked runtime declarations.

### Verification Completed

- `dotnet build Lucid.slnx -c Debug -p:Platform=x64 --no-restore`
- `dotnet test Lucid.Tests\Lucid.Tests.csproj -c Debug -p:Platform=x64 --logger "trx;LogFileName=test-results.trx" --collect:"XPlat Code Coverage"`
- `dotnet publish Lucid.App\Lucid.App.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:WindowsPackageType=None`
- `cargo test`

Current verified outcome:

- Build passes with 0 warnings and 0 errors.
- 102 C# tests pass.
- Rust test command passes with 9 tests.

## Strategic Direction

Lucid should become a trusted local Windows intelligence layer.

Every roadmap item must strengthen at least one of these pillars:

- Explainability
- Reversibility
- Local-first operation
- Confidence-aware reasoning
- Resource governance
- Operational transparency
- Deterministic behavior
- User consent and auditability

Lucid should not drift into:

- Mystery optimization
- Fear-based security UX
- Aggressive auto-remediation
- Cloud dependency
- Background work that competes with the user
- Large rewrites that destabilize working systems

## Immediate Audit Findings

### Repository And File Hygiene

Current issues:

- `_archive/` contains useful historical context, but it should be documented as archive-only so it is not mistaken for active code.

Required work:

- Confirm the Git index stays free of tracked IDE state.
- Extend repository hygiene notes if additional root files change role.

### Setup And Developer Experience

Current issues:

- Build instructions must always include `-p:Platform=x64`.
- XAML precompile behavior depends on Visual Studio MSBuild when the intermediate DLL is missing.
- The test project intentionally links pure production files instead of referencing `Lucid.App`; this is valid but fragile and must be documented.

Required work:

- Add setup verification steps for .NET, Visual Studio Build Tools, Windows App SDK, Rust, and Ollama if local chat remains enabled.
- Document the XAML build pipeline limitation in one canonical place.

### Build, CI, And Release

Current issues:

- Build and unit tests pass locally.
- CI exists but only covers basic build/test.
- `EnableMsixTooling=true` exists, but `Lucid.App/Package.appxmanifest` is not present.
- There is no clear release packaging path, signing story, installer story, crash dump policy, versioning policy, or update policy.

Required work:

- Add artifact packaging for unpackaged and packaged distributions.
- Build installer/signing policy on top of the current unpackaged release artifact path.
- Add version stamping and release notes generation.
- Add smoke-test checklist for launch, navigation, telemetry, persistence, executor dry-run, and shutdown.

### Test Coverage

Current issues:

- 99 C# tests pass, but coverage is still thin compared with the application surface.
- Executor safety, rollback, privilege gates, persistence durability, language policy, local-only endpoint enforcement, and setup scripts need deeper tests.
Required work:

- Expand executor coverage beyond rollback paths into live temp cleanup, Windows Update cache cleanup, recycle bin, and repair executors with partial-success and cancellation behavior.
- Add tests for operation history, SQLite queue behavior, final flush, and schema migration.
- Add tests for language policy so security copy stays confidence-aware.
- Expand Rust tests further for inaccessible-directory handling and any additional FFI edge cases that emerge.
- Add integration tests where pure service boundaries allow them without dragging WinUI targets into test execution.

### Architecture And Maintainability

Current issues:

- `AppServices.cs` is a central static registry and a scaling pressure point.
- `Lucid.App.csproj` uses explicit compile includes after broad removals; files can exist but not compile.
- Several service namespaces are large enough that ownership boundaries need review.
- Some docs say dependency injection is used, but the current app relies on a static service registry.

Required work:

- Keep `AppServices` stable short term, but introduce small service-provider seams around new or heavily touched modules.
- Keep the source-inclusion guard and intentional exclusion registry current as active folders change.
- Split only modules that are already being touched for production hardening.
- Create architecture decision records for the static registry, linked test source strategy, native scanner boundary, and local LLM boundary.

### Safety, Trust, And Privacy

Current issues:

- Safety systems exist, but each executor needs a consistent production contract.
- Rollback coverage is uneven.
- Privacy write-back and automation boundaries are under active development.
- Local LLM/Ollama support must remain optional, explicit, and local-only.

Required work:

- Require every executor to declare resource class, privilege requirement, reversibility, consent copy, dry-run behavior, diagnostics events, and failure mode.
- Create a non-rollbackable action registry for actions where rollback is not technically honest.
- Audit all outward network paths and enforce local-only validation by default.
- Add privacy consent and revocation tests.
- Add security-language tests for UI-facing strings and generated operational narratives.

### Performance And Resource Governance

Current issues:

- Runtime governance and adaptive scheduling code exists.
- Heavy operations still need cross-executor validation.
- Native scanning can improve throughput but must not starve foreground work.

Required work:

- Classify all background and executor work as foreground, background, or idle-only.
- Enforce concurrency budgets for disk-bound, CPU-bound, network-bound, and repair operations.
- Add diagnostics for queue depth, dropped work, over-budget operations, and shutdown flush time.
- Add performance baselines for telemetry idle CPU, memory use, storage scan throughput, and app startup time.

### UI And Product Polish

Current issues:

- The app has a broad surface area and many pages.
- Production polish requires consistency across navigation, empty states, error states, loading states, accessibility, and text tone.
- Some docs still describe old page counts and old phase labels.

Required work:

- Audit every page for empty/error/loading states.
- Verify text does not use fear-based security language.
- Add accessibility pass for keyboard navigation, focus order, contrast, and screen reader labels.
- Create a UI inventory: active pages, owning ViewModels, backing services, and status.

## Professionalization Roadmap

### Phase 0: Stabilize The Baseline

Status: in progress

Goal: make the current repo safe to work in.

Work:

- [done] Preserve current user changes while making targeted edits.
- [done] Confirm build and tests pass from a clean restore.
- [done] Normalize root documentation and align it with the inspected codebase.
- [done] Record completed work explicitly in this roadmap.
- [done] Fix setup script path drift.
- [done] Add `scripts/verify-dev.ps1`.
- Remove tracked IDE state from source control after owner approval.

Exit criteria:

- Root docs agree with the codebase.
- `git status` contains only intentional changes.
- Build, tests, and cargo tests are one-command verifiable.

### Phase 1: Repository Hygiene And Canonical Structure

Status: in progress

Goal: make the project understandable to a new engineer in under 30 minutes.

Work:

- Define active folders: `lucid-desktop`, `lucid-native`, `docs`, `.github`.
- Define archive folders: `_archive` and historical reports.
- [done] Move historical root reports under `docs/reports/`.
- [done] Add `docs/repository-hygiene.md` to document active vs archived material.
- [done] Normalize the Git-tracked casing of `README.md` and `ROADMAP.md`.
- [done] Add active-file inventory for views, services, tests, and native modules.
- [done] Complete the stale-name audit for `ExplainMyPC` references and confine remaining hits to historical material.

Exit criteria:

- No tracked generated IDE/build artifacts.
- No stale root clutter.
- Old project names remain only where historically intentional.

### Phase 2: Build, CI, Packaging, And Release

Status: in progress

Goal: produce repeatable developer and release builds.

Work:

- [done] Add a clean local restore/build/test/native-test workflow through `scripts/verify-dev.ps1`.
- [done] Fix the CI unit test job so it does not depend on missing cross-job binaries.
- [done] Add Release x64 build verification.
- [done] Decide packaged vs unpackaged distribution.
- [done] Document why unpackaged release is the first target and MSIX is deferred.
- Add signing and versioning plan.
- [done] Add release artifact generation in CI.

Exit criteria:

- CI proves Debug and Release builds.
- Release artifacts are produced deterministically.
- Packaging requirements are explicit and tested.

### Phase 3: Test Expansion And Safety Nets

Status: in progress

Goal: protect the parts of Lucid that can harm trust.

Work:

- [done] Execution engine safety gate tests.
- [done] First executor-specific rollback safety tests (`DeleteLargeFileExecutor`).
- [done] Shared cleanup rollback helper tests (`CleanupScanner`).
- [done] Temp cleanup executor rollback tests.
- [done] Windows Update cache executor rollback tests.
- [done] Windows Update cache executor live cleanup seam and safety tests.
- [done] SQLite persistence durability tests for schema initialization, queue flushing, batch limits, and final flush.
- [done] SQLite persistence migration tests for legacy schema upgrade and idempotent current-schema initialization.
- [done] Privacy permission write-back tests for grant, revocation, and non-creation behavior.
- [done] Operational language policy tests and source-audit guard for user-facing literals.
- [done] Recycle Bin cleanup executor tests for shell behavior and rollback refusal.
- [done] Executor metadata contract and registration-time validation.
- Additional executor-specific safety and rollback tests.
- Persistence durability and migration tests.
- Privacy consent/write-back tests.
- Local endpoint enforcement tests.
- Operational language policy tests.
- [done] Rust scanner tests.
- Build-inclusion tests for explicitly included C# files.

Exit criteria:

- Every destructive or semi-destructive executor has tests for consent, dry-run, failure, and audit behavior.
- Rust scanner has meaningful tests before feature expansion.
- CI blocks common trust regressions.

### Phase 4: Architecture Hardening

Status: not started

Goal: reduce long-term fragility without destabilizing the app.

Work:

- Add small service-provider seams around new work.
- Document and gradually reduce `AppServices` centralization.
- Add module boundary docs for telemetry, intelligence, execution, persistence, trust, privacy, storage, companion, and native scanning.
- Add diagnostics event contracts for service failures.
- Replace silent best-effort catches with structured diagnostics where user trust or data integrity is affected.

Exit criteria:

- New services can be tested without WinUI packaging targets.
- Service boundaries are documented and enforced by tests or review checklists.
- Failures become inspectable diagnostics, not invisible behavior.

### Phase 5: Trust, Privacy, And Governance Completion

Status: not started

Goal: make safety guarantees consistent across the platform.

Work:

- Create executor metadata contract.
- Audit rollback coverage.
- Add non-rollbackable action disclosures.
- Enforce resource classification for all heavy work.
- Add privacy consent revocation workflow.
- Verify local-only behavior for local LLM and any sync/distributed code.

Exit criteria:

- Every action explains impact, confidence, reversibility, privilege, and audit trail.
- Background work yields under pressure.
- Privacy-sensitive features are opt-in and revocable.

### Phase 6: Product Polish And UX Readiness

Status: not started

Goal: make Lucid feel like a trustworthy Windows application rather than an engineering cockpit.

Work:

- Page-by-page UI audit.
- Consistent empty, loading, and error states.
- Accessibility pass.
- Navigation and information architecture cleanup.
- Operational copy review.
- First-run experience and settings defaults.

Exit criteria:

- A new user can understand what Lucid is observing and why.
- The app communicates uncertainty clearly.
- No page relies on placeholder copy or unexplained metrics.

### Phase 7: Production Operations

Status: not started

Goal: prepare Lucid for real users.

Work:

- Crash logging policy.
- Local diagnostics export bundle.
- User data location and retention policy.
- Upgrade and migration policy.
- Release checklist.
- Support triage guide.

Exit criteria:

- Releases are repeatable.
- User data is local, inspectable, and bounded.
- Failures can be diagnosed without violating privacy.

## Refactor And Repair Backlog

High priority:

- Fix `setup.ps1` hard-coded old repo paths.
- Remove tracked `.vs` files from Git.
- Add Release build to CI.
- Add packaging decision and manifest path.
- Add executor metadata audit.

Medium priority:

- Move old root reports out of the root.
- Add ADRs for static service registry, linked test source files, native scanner boundary, and local LLM.
- Expand persistence tests for SQLite queue and schema reliability.
- Add language-policy tests for all user-facing security copy.
- Audit hidden network assumptions in distributed and local LLM services.

Deferred:

- Big-bang dependency injection migration.
- Broad UI redesign.
- Distributed multi-device intelligence.
- Large dependency upgrades.
- Native hashing expansion.

## Working Rule For Future Agents

Do not add more features until the current production-hardening phases are under control. If a requested feature bypasses build reliability, safety, rollback, diagnostics, or trust language, treat it as a risky change and propose a smaller path.
