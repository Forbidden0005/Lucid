# Lucid Roadmap

This roadmap is based on repository inspection and verification on 2026-06-06. It separates what exists from what is required before Lucid can be treated as a professional production Windows application.

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

- Visual Studio `.vs` files are staged as deleted, which means they were previously tracked or staged in the index.
- `.gitignore` ignores `.vs/`, but the repository needs a deliberate cleanup commit to remove historical IDE state.
- Root docs used mixed casing (`README.MD`, `ROADMAP.MD`) while internal references expected `README.md` and `ROADMAP.md`.
- There are historical review and tidying files at the root that should either move under `docs/reports/` or `_archive/` after owner approval.
- `_archive/` contains useful historical context, but it should be documented as archive-only so it is not mistaken for active code.

Required work:

- Normalize documentation names and references.
- Remove tracked IDE and build state from Git.
- Decide which root reports remain active and which move to `docs/reports/` or `_archive/`.
- Add a short `docs/repository-hygiene.md` explaining active vs archived folders.

### Setup And Developer Experience

Current issues:

- `setup.ps1` still references `C:\Users\tyler\ExplainMyPC`.
- Build instructions must always include `-p:Platform=x64`.
- XAML precompile behavior depends on Visual Studio MSBuild when the intermediate DLL is missing.
- The test project intentionally links pure production files instead of referencing `Lucid.App`; this is valid but fragile and must be documented.

Required work:

- Update setup scripts to resolve paths relative to the repo root.
- Add setup verification steps for .NET, Visual Studio Build Tools, Windows App SDK, Rust, and Ollama if local chat remains enabled.
- Add a quick `scripts/verify-dev.ps1` that runs build, tests, and native tests.
- Document the XAML build pipeline limitation in one canonical place.

### Build, CI, And Release

Current issues:

- Build and unit tests pass locally.
- CI exists but only covers basic build/test.
- `EnableMsixTooling=true` exists, but `Lucid.App/Package.appxmanifest` is not present.
- There is no clear release packaging path, signing story, installer story, crash dump policy, versioning policy, or update policy.

Required work:

- Add release-mode build verification.
- Add artifact packaging for unpackaged and packaged distributions.
- Create or intentionally defer MSIX packaging with documented rationale.
- Add version stamping and release notes generation.
- Add smoke-test checklist for launch, navigation, telemetry, persistence, executor dry-run, and shutdown.

### Test Coverage

Current issues:

- 53 C# tests pass, but coverage is thin compared with the application surface.
- Rust scanner has no tests.
- Executor safety, rollback, privilege gates, persistence durability, language policy, local-only endpoint enforcement, and setup scripts need deeper tests.

Required work:

- Add tests around destructive executor preconditions and rollback metadata.
- Add tests for operation history, SQLite queue behavior, final flush, and schema migration.
- Add tests for language policy so security copy stays confidence-aware.
- Add Rust unit tests for path validation, inaccessible directories, top-file ordering, and returned memory ownership behavior.
- Add integration tests where pure service boundaries allow them without dragging WinUI targets into test execution.

### Architecture And Maintainability

Current issues:

- `AppServices.cs` is a central static registry and a scaling pressure point.
- `Lucid.App.csproj` uses explicit compile includes after broad removals; files can exist but not compile.
- Several service namespaces are large enough that ownership boundaries need review.
- Some docs say dependency injection is used, but the current app relies on a static service registry.

Required work:

- Keep `AppServices` stable short term, but introduce small service-provider seams around new or heavily touched modules.
- Add a script or test that detects source files under active folders that are not included in the project file.
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

Goal: make the current repo safe to work in.

Work:

- Preserve current user changes.
- Confirm build and tests pass from a clean restore.
- Normalize root documentation.
- Remove tracked IDE state from source control after owner approval.
- Fix setup script path drift.
- Add `scripts/verify-dev.ps1`.

Exit criteria:

- Root docs agree with the codebase.
- `git status` contains only intentional changes.
- Build, tests, and cargo tests are one-command verifiable.

### Phase 1: Repository Hygiene And Canonical Structure

Goal: make the project understandable to a new engineer in under 30 minutes.

Work:

- Define active folders: `lucid-desktop`, `lucid-native`, `docs`, `.github`.
- Define archive folders: `_archive` and historical reports.
- Move historical reports under `docs/reports/` or `_archive/` after approval.
- Add active-file inventory for views, services, tests, and native modules.
- Add stale-name audit for `ExplainMyPC` references, separating product phrase from obsolete namespace/path usage.

Exit criteria:

- No tracked generated IDE/build artifacts.
- No stale root clutter.
- Old project names remain only where historically intentional.

### Phase 2: Build, CI, Packaging, And Release

Goal: produce repeatable developer and release builds.

Work:

- Add clean restore/build/test/native-test workflow.
- Add Release x64 build verification.
- Decide packaged vs unpackaged distribution.
- Add MSIX manifest or document why unpackaged release is the first target.
- Add signing and versioning plan.
- Add release artifact generation in CI.

Exit criteria:

- CI proves Debug and Release builds.
- Release artifacts are produced deterministically.
- Packaging requirements are explicit and tested.

### Phase 3: Test Expansion And Safety Nets

Goal: protect the parts of Lucid that can harm trust.

Work:

- Executor safety and rollback tests.
- Persistence durability and migration tests.
- Privacy consent/write-back tests.
- Local endpoint enforcement tests.
- Operational language policy tests.
- Rust scanner tests.
- Build-inclusion tests for explicitly included C# files.

Exit criteria:

- Every destructive or semi-destructive executor has tests for consent, dry-run, failure, and audit behavior.
- Rust scanner has meaningful tests before feature expansion.
- CI blocks common trust regressions.

### Phase 4: Architecture Hardening

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
- Add Rust scanner tests.
- Add project-file inclusion guard for active C# files.
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
