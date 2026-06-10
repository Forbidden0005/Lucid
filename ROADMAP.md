# Lucid Roadmap

> Last audited: 2026-06-10. This is the single source of truth for project state, priorities,
> completed work, and engineering direction. All other roadmap and state documents are retired
> into `docs/history/`. Update this file after every completed task.

---

## Table of Contents

1. [Project Identity](#project-identity)
2. [Strategic Direction](#strategic-direction)
3. [Current Verified Baseline](#current-verified-baseline)
4. [Critical Issues — Act First](#critical-issues--act-first)
5. [Completed Work](#completed-work)
6. [Product Roadmap](#product-roadmap)
7. [Engineering Professionalization Roadmap](#engineering-professionalization-roadmap)
8. [Architecture Review](#architecture-review)
9. [Code Quality Backlog](#code-quality-backlog)
10. [Testing Plan](#testing-plan)
11. [Tooling and Standards](#tooling-and-standards)
12. [Documentation State](#documentation-state)
13. [Dependency Review](#dependency-review)
14. [Deferred Work](#deferred-work)
15. [Definition of Professional Quality](#definition-of-professional-quality)

---

## Project Identity

| Attribute | Value |
|---|---|
| Project name | Lucid |
| Project type | Windows desktop application — local-first operational intelligence platform |
| Languages | C# (~90,700 LOC), Rust (539 LOC native module), PowerShell (17 ops scripts), XAML |
| Frameworks | WinUI 3 / Windows App SDK 1.5, .NET 8 (`net8.0-windows10.0.19041.0`), CommunityToolkit.Mvvm 8.2.2 |
| Native layer | `lucid-native/lucid-scanner` — Rust `cdylib` over `windows-sys`, consumed via P/Invoke |
| Persistence | SQLite via `Microsoft.Data.Sqlite` 8.0.0 |
| Build system | `dotnet build Lucid.slnx -p:Platform=x64`; VS MSBuild required once after clean for `XamlPreCompile` |
| Test system | xUnit 2.9.2 + FluentAssertions 6.12.1 + Moq 4.20.72 + coverlet; 40 test files, 160 passing tests |
| CI | GitHub Actions: Debug + Release build/test on windows-latest, plus publish job |
| Deployment | Unpackaged self-contained win-x64 (`WindowsPackageType=None`), PowerShell installer in `installer/` |

---

## Strategic Direction

Lucid is a trusted local Windows intelligence layer. Every roadmap item must strengthen at least one of:

- Explainability
- Reversibility
- Local-first operation
- Confidence-aware reasoning
- Resource governance
- Operational transparency
- Deterministic behavior
- User consent and auditability

Lucid must never drift into:

- Mystery optimization
- Fear-based security UX
- Aggressive auto-remediation
- Cloud dependency
- Background work that competes with the user
- Large rewrites that destabilize working systems

---

## Current Verified Baseline

Verified 2026-06-10 from repository inspection.

**Build commands (verified passing):**
```powershell
# From lucid-desktop/
dotnet build Lucid.slnx -c Debug -p:Platform=x64 --no-restore
dotnet test Lucid.Tests\Lucid.Tests.csproj -c Debug -p:Platform=x64 --no-restore

# From lucid-native/
cargo test
```

**Verified counts:**
- ~480 compiled C# production files across 33 service domains
- 27 XAML view files, 41 ViewModel files
- 160 passing C# tests (verified 2026-06-10 via `dotnet test`)
- 9 Rust tests passing (verified 2026-06-10 via `cargo test`)
- 28 action executors implementing `IActionExecutor`

**Known active issues (not yet fixed):**
- Untracked/uncommitted CI + release infrastructure: `.github/workflows/lucid-build.yml` (modified),
  15 `scripts/*.ps1`, `installer/`, `Directory.Build.props`, `release/*.json` — awaiting human
  review before commit (autonomous hard stop). Test files, docs, and source fixes were committed
  2026-06-10; a fresh clone still fails CI until the scripts land.
- CRLF/LF churn — `.gitattributes` committed 2026-06-10; repo-wide renormalize still pending (C2)
- ~~`release/` (740 MB of generated artifacts) not in `.gitignore`~~ — resolved 2026-06-10 (C3)
- `AppServices.cs` is 2,052 lines, ~100 static properties — static service locator
- `Lucid.App.csproj` has 481 explicit `<Compile Include>` entries instead of default globbing
- 48 empty `catch { }` blocks; 33 `Debug/Console.WriteLine` calls
- Rust scanner has 0 tests and is absent from CI entirely
- `NETSDK1206` warning during build — expected, non-critical, from Windows App SDK NuGet

---

## Critical Issues — Act First

These block professional quality and must be resolved before any new feature work.

### C1 — Untracked source files that committed code depends on (P0)
CI invokes `scripts/validate-release-metadata.ps1`, `validate-release-operations.ps1`, and
`check-app-source-includes.ps1` — all untracked. A fresh clone of `main` fails CI today.
~25% of the test suite exists only on this machine. `Directory.Build.props`, the entire
`installer/` directory, 10 test files, and 4 docs are also untracked.

**Fix:** Commit all untracked source after C2 is in place (so endings land normalized).
Verify with a scratch clone + CI run.

- [x] Commit `.gitattributes` + `.editorconfig` first (C2) — done 2026-06-10 (`f7e38ea`)
- [x] Stage and commit: test files, docs, production source fixes — done 2026-06-10 (`e1f52fa`);
      verified beforehand: build clean, 153/153 C# tests, 9/9 Rust tests
- [ ] Stage and commit: `scripts/` (15 untracked), `installer/`, `Directory.Build.props`,
      modified `.github/workflows/lucid-build.yml`, `scripts/verify-dev.ps1`, `release/*.json` —
      **blocked on human review**: autonomous agents must not commit CI/build/release-script
      changes (AGENTS.md impactful-action gate). Tyler: review and commit these to unblock CI.
- [ ] Confirm CI green from clean checkout

### C2 — No `.gitattributes`; line-ending churn poisons every diff (P0)
613 modified files showing ~113k insertions / ~112k deletions — almost entirely CRLF↔LF.
Real changes are invisible inside whole-file diffs. `git blame` is destroyed on every touched file.

**Fix:** Add `.gitattributes` (`* text=auto`, explicit `eol=crlf` for `.ps1/.bat/.slnx` if desired),
add `.editorconfig`, then run a one-time `git add --renormalize .` commit — isolated from any
functional change.

- [x] Add `.gitattributes` and `.editorconfig` — committed 2026-06-10 (`f7e38ea`); newly staged
      files now land normalized
- [ ] Run `git add --renormalize .` — deferred: must be isolated from functional changes, and
      the modified CI workflow + `verify-dev.ps1` are still uncommitted (pending human review)
- [ ] Commit as `chore: normalize line endings` with no functional changes mixed in

### C3 — `release/` (740 MB) not in `.gitignore` (P0)
One careless `git add .` permanently bloats history. Also makes `git status` noise normal —
which is how C1 happened.

**Fix:** Add `release/` to `.gitignore`. `installer/` is source — commit it.

- [x] Add `release/` to `.gitignore` — done 2026-06-10 as `release/*` with carve-outs for the two
      repo-tracked contract files (`release-metadata.json`, `release-operations.json`) that
      `scripts/validate-release-*.ps1` and 10 other release scripts consume. A bare `release/` rule
      would have prevented git from ever descending into the directory, blocking those negations.
- [x] Confirm `git status` ignores it — verified: `git check-ignore` matches all artifact
      subdirectories; only the two contract JSONs remain visible as untracked (intentional, for C1)

### C4 — `AppServices.cs`: 2,052-line static service locator (P1)
~100 `public static` service properties; 32 ViewModel/View files reach into it directly.
Meanwhile page-level ViewModels use constructor injection. Two competing DI idioms in one app.
15-parameter constructors are the same disease from the other side.

**Fix:** Incremental strangler migration — not a big-bang rewrite. See Architecture Review.

- [ ] Introduce `IServiceRegistry` shim behind `AppServices` statics (zero consumer changes)
- [ ] Freeze the locator: ban new `AppServices.*` references via analyzer (grandfather existing 32 files)
- [ ] Migrate one page end-to-end as the template
- [ ] Continue one page per session

### C5 — 633-line manual compile whitelist in `Lucid.App.csproj` (P1)
481 explicit `<Compile Include>` entries. Only 9 files remain excluded. The original reason
(excluding future scaffolding) no longer applies. Every new file requires a csproj edit.

**Fix:** Delete or archive the 9 orphans, remove the Remove/Include machinery, return to default globbing,
retire `check-app-source-includes.ps1`.

- [ ] Identify the 9 excluded orphan files — determine: delete vs. keep vs. move to branch
- [ ] Remove `<Compile Remove>` globs and all 481 `<Compile Include>` entries
- [ ] Confirm build output identical; retire guard script from CI

### C6 — Test depth: 143 tests for 480 files; Rust at zero; Rust absent from CI (P1)
Coverage concentrated in Cleanup/Execution/Persistence/Trust. 28 executors exist; destructive-path
and rollback coverage is partial. Rust scanner has 0 tests and no CI job. The build silently skips
copying `lucid_scanner.dll` when missing — a broken native build is undetectable until runtime.

**Fix:** See Testing Plan. Make Release copy step a hard error when DLL is missing.

- [ ] Executor safety contract suite (all 28 executors)
- [ ] Rust unit tests + `cargo test/clippy/fmt` CI job
- [ ] Make missing DLL a hard build error for Release configuration

### C7 — 48 empty `catch { }` blocks; 33 `Debug/Console.WriteLine` calls (P1)
Silent failure directly contradicts the explainability doctrine. A platform that explains the system
to users must not hide its own failures.

**Fix:** Sweep each `catch { }` into `IOperationalLogger` event, a justified `// best-effort: <why>`
comment, or removal. Route all `Debug/Console.WriteLine` through the operational logger.
Enforce via `BannedApiAnalyzers`.

- [ ] Audit and sweep all 48 empty catches
- [ ] Replace all 33 debug/console prints with `IOperationalLogger`
- [ ] Add banned-API analyzer rule to prevent recurrence

### C8 — Doc sprawl: triplicated agent instructions; stale state snapshots (P2)
`CLAUDE.md` / `AGENTS.md` / `CODEX.md` are near-identical ~14 KB copies — drift is inevitable.
`CURRENT_STATE.md` counts are stale. `docs/reports/` contains `NEW_ROADMAP.md` competing with this file.
`docs/Structure.txt` and `docs/active-file-inventory.md` cannot describe a 500-file app.

**Fix:** Single-source agent instructions. Retire stale docs. See Documentation State.

- [ ] Reduce `AGENTS.md` and `CODEX.md` to one-paragraph pointers to `CLAUDE.md`
- [ ] Delete `CURRENT_STATE.md` (this file + CI are the live state)
- [ ] Retire `REMAINING_WORK.md` (folded into this roadmap)
- [ ] Move `docs/reports/` → `docs/history/` with dated filenames
- [ ] Delete `docs/Structure.txt` and `docs/active-file-inventory.md`

### C9 — `_archive/` committed to main (39 tracked files) (P2)
Git history already preserves deleted code. Tracked archives rot, participate in repo-wide operations,
and confuse search. 36 of the 613 line-ending-churned files are in `_archive/`.

**Fix:** Tag current state (e.g. `archive/intelligence-v1`), then `git rm -r _archive/`.

- [ ] Tag current commit before deletion
- [ ] `git rm -r _archive/`
- [ ] Confirm no live code referenced anything in it


---

## Completed Work

This section is the canonical record of everything done. Do not re-open completed items.
Do not add items here that have not been verified.

### Verified Baseline
- WinUI 3 application scaffold and active solution under `lucid-desktop/Lucid.App` and `lucid-desktop/Lucid.slnx`
- xUnit test project under `lucid-desktop/Lucid.Tests`
- Rust native workspace under `lucid-native` with `lucid-scanner`
- SQLite persistence, runtime governance, diagnostics, trust/integrity, replay, simulation, privacy, and companion code paths
- GitHub Actions CI for Windows build and test

### Documentation and Governance
- Root docs rewritten to match inspected codebase: `README.md`, `ONBOARDING.md`, `PROJECT_INTEGRITY.md`, `ROADMAP.md`
- `CODEX.md` requires roadmap review before every task and roadmap maintenance after every completed task
- `.gitignore` excludes generated `TestResults` artifacts
- Historical root reports moved under `docs/reports/`; `docs/repository-hygiene.md` documents active vs archived material
- Git-tracked root doc casing normalized to `README.md` and `ROADMAP.md`
- Stale-name cleanup complete — remaining `ExplainMyPC` references are historical only, in `_archive/` or `docs/reports/`

### Repository and Setup Repairs
- CI unit test workflow corrected — test job no longer assumes compiled artifacts from another runner
- `setup.ps1` resolves solution and launch paths from repo location instead of stale `ExplainMyPC` path
- `scripts/verify-dev.ps1` added as one-command local verification entrypoint
- `scripts/check-app-source-includes.ps1` verifies active C# files are either compiled or documented as intentional exclusions
- `docs/active-file-inventory.md` records active file counts and current intentional non-compiled files

### Build, CI, and Release Pipeline
- Release verification via `scripts/verify-dev.ps1 -Configuration Release -PublishApp` and `scripts/verify-release.ps1`
- CI proves Debug and Release lanes separately, uploads unpackaged self-contained `win-x64` publish artifact
- `docs/release-packaging.md` documents unpackaged self-contained `win-x64` as first distribution path; MSIX deferred
- `Directory.Build.props`, `release/release-metadata.json`, and `docs/releases/0.1.0-preview.md` define version, assembly stamping, and signing mode
- `scripts/validate-release-metadata.ps1` enforces version/signing metadata consistency in local verification and CI
- `scripts/prepare-release-artifact.ps1` validates publish outputs, copies smoke checklist, generates `RELEASE-SHA256.txt` and `release-artifact-manifest.json`
- `scripts/verify-release-artifact.ps1` verifies file counts, byte totals, per-file SHA-256, checksum-file consistency, manifest metadata, and signing-mode enforcement
- `scripts/run-release-smoke.ps1` launches published `Lucid.App.exe`, requires it to stay alive through startup threshold, records outcome in `release-smoke-result.json`
- `scripts/sign-release-artifact.ps1` provides Authenticode signing entrypoint; skips in `unsigned-ci-artifact` mode; requires cert/timestamp inputs when `authenticode-required`
- `scripts/package-release-artifact.ps1` produces versioned unpackaged distribution zip + zip-level SHA-256 under `release/packages/`
- `scripts/verify-release-package.ps1` verifies packaged zip checksum and required release entries
- `installer/Install-Lucid.ps1` and `Uninstall-Lucid.ps1` provide deterministic unpackaged installer targeting `LocalAppData\Programs\Lucid`
- `scripts/verify-installer-roundtrip.ps1` verifies install/uninstall flow without touching real user profile
- Unpackaged installer has explicit upgrade behavior, blocks downgrade attempts by default
- `scripts/verify-installer-roundtrip.ps1` verifies multi-version install state, downgrade blocking, and allowed downgrade behavior
- `installer/Migrate-LucidData.ps1` normalizes legacy data files into canonical `Data/` and `History/` paths with migration history and backup
- `scripts/generate-release-update-manifest.ps1` generates package-level update descriptor for verified release zip
- `scripts/verify-release-update-manifest.ps1` verifies update descriptor against packaged zip checksum, size, and support-script path
- `scripts/generate-release-update-feed.ps1` generates publishable `feeds/index.json` and `feeds/<channel>.json` discovery tree
- `scripts/verify-release-update-feed.ps1` verifies current release is discoverable through that feed
- `release/release-operations.json` defines repo-tracked channel ownership, rollout posture, symbol handling, and support-intake policy
- `scripts/validate-release-operations.ps1` enforces that contract during local and CI verification
- `installer/Export-LucidSupportBundle.ps1` exports support bundle; `scripts/verify-support-bundle-export.ps1` verifies it

### Safety and Executor Tests
- Execution engine safety gate tests (pre-flight elevation, confirmation gates, dry-run, rollback gating, exception containment)
- `DeleteLargeFileExecutor` rollback tests (missing-path failure, staging, restore, safe rollback failure)
- Shared `CleanupScanner` rollback tests (missing staging/manifests, partial restore)
- `TempFileCleanupExecutor` rollback tests (missing manifest, restore, mid-rollback cancellation)
- `WindowsUpdateCacheExecutor` rollback tests and live cleanup seam (partial success, cancellation rollback, unconditional restart)
- `RecycleBinCleanupExecutor` tests (shell behavior, dry-run, rollback refusal)
- `NetworkAdapterResetExecutor` tests (multi-step behavior, cancellation, rollback refusal)
- `WinsockResetExecutor` tests (restart-gated repair, cancellation, rollback refusal)
- `DismRestoreHealthExecutor` tests (output-classified repair, cancellation, rollback refusal)
- `SfcScanExecutor` tests (output-classified repair, partial success, cancellation, rollback refusal)
- `WindowsStoreResetExecutor` tests (timeout-sensitive launch, cancellation, launch failure, rollback semantics)
- `FlushDnsExecutor` tests (command result, cache-self-healing rollback)
- Action executor metadata contract and registration-time validation in `ActionExecutionMetadataCatalog`

### Persistence and Privacy Tests
- `SQLitePersistenceService` tests: schema initialization, queue flushing, batch limits, final flush on dispose
- `SQLitePersistenceService` migration tests: legacy schema v0 upgrade, idempotent current-schema initialization
- Off-by-one bug fixed: `FlushQueueAsync` now correctly respects `MaxFlushBatchSize` without dropping a queued write
- `PrivacyPermissionWriter` tests: allow writes, deny-based revocation, non-creation contract
- `PrivacyPermissionScanner` tests: fallback resolution, Win32 `NonPackaged` handling, category ordering, empty-capability suppression

### Trust and Consent Tests
- `AutomationConsentService` trust-gate tests: hard boundary blocking, low-risk auto-approval, observe-only denial, explicit approval
- `AutomationConsentService` outcome tests: denial, cancellation-as-denial, consent-request and consent-denied timeline
- Fixed: `AutomationConsentService` no longer auto-approves in `ObserveOnly` or `GuidedOnly` mode
- Fixed: consent wait cancellation treated as clean denial instead of leaking `OperationCanceledException`

### Language Policy and Network Enforcement
- Operational language policy tests and source-audit guard for user-facing literals in Services, ViewModels, and Views
- Fixed: guided storage workflow no longer claims downloads are "safe to delete"
- `OllamaClient` validates originally supplied endpoint before falling back to localhost
- Local endpoint enforcement tests: constructor behavior, private-LAN/remote fallback, blank endpoint rejection, default-model fallback

### Rust
- Rust scanner tests: missing-root failure, file-root rejection, directory/file/byte counting, top-file ordering, FFI argument validation, version reporting, owned-string cleanup
- Rust scanner root validation now rejects nonexistent or non-directory roots

### Session 2026-06-10 (autonomous)
- Verified full green state: x64 Debug build clean, 153/153 C# tests, 9/9 Rust tests
- Committed `.gitattributes` + `.editorconfig` (C2 step 1, `f7e38ea`)
- Committed prior session's verified work — executor tests, OllamaClient endpoint enforcement,
  consent fixes, privacy scanner tests, `.gitignore` release carve-out (C3) — as `e1f52fa`
- Removed stale `.git/index.lock` (0-byte, orphaned by a crashed git process; no git running)
- Deliberately left uncommitted pending human review: modified CI workflow, 15 release scripts,
  `installer/`, `Directory.Build.props`, `release/*.json`, `verify-dev.ps1`, `AUDIT_ROADMAP.md`

### Session 2026-06-10 (autonomous, second run)
- Completed and committed `SQLitePersistenceDurabilityTests` (7 tests), found untracked from a
  prior interrupted session: queue-overflow back-pressure visibility (drop metrics + callback),
  post-drain write acceptance, corrupt-DB backup/recreate with preserved evidence, poison-write
  batch isolation, pre-init/post-dispose write gating, query-failure degradation
- The corruption test exposed a real recovery bug: `InitializeAsync` never disposed the failed
  connection before renaming the corrupt file. SQLite's Windows VFS opens without
  `FILE_SHARE_DELETE`, so the backup rename failed while the handle was alive and recovery
  re-opened the same corrupt file and gave up. Fixed by disposing the failed connection before
  `TryBackupAndDelete` and opening with `Pooling=false` (one lifetime connection; pooled handles
  survive Dispose and would also block the rename)
- Corrected one test over-specification: live `-wal`/`-shm` files are legitimate for an open
  WAL-mode connection; stale-shim assertions now check post-close state
- Verified: x64 Debug build clean (0 warnings), 160/160 C# tests, 9/9 Rust tests


---

## Product Roadmap

### Phase 0 — Stabilize the Baseline
**Status: Complete**

Goal: make the current repo safe to work in.

- [x] Preserve current user changes while making targeted edits
- [x] Confirm build and tests pass from a clean restore
- [x] Normalize root documentation and align with the inspected codebase
- [x] Record completed work explicitly in this roadmap
- [x] Fix setup script path drift
- [x] Add `scripts/verify-dev.ps1`
- [ ] Remove tracked IDE state from source control after owner approval

**Exit criteria:**
- Root docs agree with the codebase
- `git status` contains only intentional changes
- Build, tests, and cargo tests are one-command verifiable

---

### Phase 1 — Repository Hygiene and Canonical Structure
**Status: In progress**

Goal: make the project understandable to a new engineer in under 30 minutes.

- [x] Move historical root reports under `docs/reports/`
- [x] Add `docs/repository-hygiene.md`
- [x] Normalize Git-tracked casing of `README.md` and `ROADMAP.md`
- [x] Complete stale-name audit for `ExplainMyPC` references
- [ ] Add `.gitattributes` and normalize line endings (C2) — attributes committed 2026-06-10;
      renormalize pending
- [ ] Commit all untracked source (C1) — test files, docs, source fixes committed 2026-06-10;
      CI/release scripts, `installer/`, `Directory.Build.props` await human review
- [x] Add `release/` to `.gitignore` (C3) — done 2026-06-10, verified via `git check-ignore`
- [ ] Delete `_archive/` after tagging (C9)
- [ ] Consolidate triplicated agent instruction docs (C8)
- [ ] Retire stale `CURRENT_STATE.md` and `REMAINING_WORK.md` (C8)

**Exit criteria:**
- No tracked generated IDE/build artifacts
- No stale root clutter
- Fresh clone passes CI

---

### Phase 2 — Build, CI, Packaging, and Release
**Status: In progress (core pipeline complete; signing and live deployment deferred)**

- [x] Clean local restore/build/test/native-test workflow
- [x] Fix CI unit test job cross-job binary dependency
- [x] Release and Debug x64 build verification
- [x] Unpackaged distribution decision and documentation
- [x] Signing and versioning plan
- [x] Repo-tracked release metadata validation and release-notes baseline
- [x] Release artifact generation in CI with checksums, manifest, smoke checklist
- [x] Release artifact verification for manifest, checksums, signing-mode
- [x] Executed release launch smoke gate with recorded results
- [x] Operational release signing hook ready for certificate-backed inputs
- [x] Versioned unpackaged release packaging with zip-level verification
- [x] Deterministic unpackaged installer/uninstaller with workspace round-trip verification
- [x] Installer upgrade behavior with downgrade blocking and multi-version verification
- [x] Repo-side update-manifest generation and verified support-bundle export
- [x] Installer-managed data migration with canonical-path normalization and backup
- [x] Repo-side update-feed generation and discovery verification
- [x] Repo-tracked release-operations policy and validation
- [ ] Remove csproj compile whitelist (C5)
- [ ] Real certificate-backed signing inputs — flip release metadata to `authenticode-required`
- [ ] Expand launch smoke gate into deeper navigation and telemetry assertions
- [ ] Define hosted update publication/discovery rules
- [ ] Crash/support operational ownership for customer-facing distribution
- [ ] Packaged-distribution parity (MSIX — deferred until after unpackaged path is stable)

**Exit criteria:**
- CI proves Debug and Release builds
- Release artifacts produced deterministically
- Packaging requirements explicit and tested

---

### Phase 3 — Test Expansion and Safety Nets
**Status: In progress**

Goal: protect the parts of Lucid that can harm trust.

- [x] Execution engine safety gate tests
- [x] Executor-specific rollback safety tests (Delete, TempCleanup, WinUpdateCache, RecycleBin, NetworkReset, Winsock, DISM, SFC, StoreReset, FlushDns)
- [x] Shared cleanup rollback helper tests (`CleanupScanner`)
- [x] SQLite persistence durability tests
- [x] SQLite persistence migration tests
- [x] Privacy permission write-back and scanner tests
- [x] Operational language policy tests and source-audit guard
- [x] Executor metadata contract and registration-time validation
- [x] Automation consent gate tests (all paths including denial and cancellation)
- [x] Local endpoint enforcement tests
- [x] Rust scanner tests (9 tests)
- [ ] Executor safety contract suite across all 28 executors (dry-run purity, rollback metadata, hostile-path inputs) (C6)
- [ ] Rust unit tests for path handling, long-path `\\?\` behavior, junction/symlink cycles, FFI null/invalid inputs (C6)
- [ ] Rust CI job: `cargo test`, `cargo clippy -D warnings`, `cargo fmt --check` (C6)
- [ ] Make missing `lucid_scanner.dll` a hard build error for Release (C6)
- [x] Persistence durability tests: queue-overflow back-pressure, corrupt-DB backup/recreate,
      poison-write batch isolation, lifecycle write gating — done 2026-06-10
      (flush-on-shutdown was already covered by existing dispose final-flush tests)
- [ ] Build-inclusion tests for explicitly included C# files (retire after C5)
- [ ] Coverage visibility: surface summary in CI job; set ratcheting floor

**Exit criteria:**
- Every destructive executor has tests for consent, dry-run, failure, and audit behavior
- Rust scanner has meaningful coverage before feature expansion
- CI blocks common trust regressions

---

### Phase 4 — Architecture Hardening
**Status: Not started**

Goal: reduce long-term fragility without destabilizing the app.

- [ ] AppServices strangler: introduce `IServiceRegistry` shim behind statics (C4)
- [ ] Freeze `AppServices.*` references with analyzer ban + grandfather list (C4)
- [ ] Migrate one page end-to-end as the template (C4)
- [ ] Continue page-by-page migration (one per session)
- [ ] Extract `Lucid.Core` class library; replace 57 file-linked tests with project reference
- [ ] Module boundary docs for each of the 33 service domains
- [ ] ADRs for: static registry, linked test source strategy, native scanner boundary, local LLM boundary
- [ ] Replace silent `catch { }` blocks with structured diagnostics (C7)
- [ ] Route all `Debug/Console.WriteLine` to `IOperationalLogger` (C7)
- [ ] Add banned-API analyzer rule for debug prints and (post-migration) `AppServices` (C7)
- [ ] Enable `.NET analyzers` + `TreatWarningsAsErrors` + `dotnet format` CI check
- [ ] Audit 12 `async void` occurrences — acceptable only for UI event handlers
- [ ] Fix 9 `.Result`/`.Wait()` sync-over-async patterns
- [ ] Refactor `CompanionOverlayWindow.xaml.cs` (879 lines) into VM + window-interop helper
- [ ] Decompose `SimulationViewModel` (1,182 lines) and `InsightsPageViewModel` (851 lines)
- [ ] Add diagnostics event contracts for service failures
- [ ] Replace silent best-effort catches with structured diagnostics where user trust or data integrity is affected

**Exit criteria:**
- New services can be tested without WinUI packaging targets
- Service boundaries documented and enforced by tests or review checklists
- Failures become inspectable diagnostics, not invisible behavior

---

### Phase 5 — Trust, Privacy, and Governance Completion
**Status: Not started**

Goal: make safety guarantees consistent across the entire platform.

- [ ] Require every executor to declare: resource class, privilege requirement, reversibility, consent copy, dry-run behavior, diagnostics events, failure mode
- [ ] Create non-rollbackable action registry for actions where rollback is not technically honest
- [ ] Classify all background and executor work as Foreground, Background, or Idle-only
- [ ] Enforce concurrency budgets for disk-bound, CPU-bound, network-bound, and repair operations
- [ ] Add diagnostics for queue depth, dropped work, over-budget operations, and shutdown flush time
- [ ] Add performance baselines for idle CPU, memory use, storage scan throughput, and app startup time
- [ ] Audit all outward network paths and enforce local-only validation by default
- [ ] Extend privacy coverage from registry read/write into page/viewmodel workflow
- [ ] Add privacy consent revocation workflow
- [ ] Verify local-only behavior for local LLM and any sync/distributed code

**Exit criteria:**
- Every action explains impact, confidence, reversibility, privilege, and audit trail
- Background work yields under pressure
- Privacy-sensitive features are opt-in and revocable

---

### Phase 6 — Product Polish and UX Readiness
**Status: Not started**

Goal: make Lucid feel like a trustworthy Windows application rather than an engineering cockpit.

- [ ] Page-by-page UI audit: empty states, error states, loading states
- [ ] Verify no page uses fear-based or absolute-certainty security language
- [ ] Accessibility pass: keyboard navigation, focus order, contrast, screen reader labels
- [ ] Navigation and information architecture cleanup
- [ ] Operational copy review for confidence-aware language throughout
- [ ] First-run experience and settings defaults
- [ ] UI inventory: active pages, owning ViewModels, backing services, and status
- [ ] Consistent empty/loading/error state treatment across all pages

**Exit criteria:**
- A new user can understand what Lucid is observing and why
- The app communicates uncertainty clearly
- No page relies on placeholder copy or unexplained metrics

---

### Phase 7 — Production Operations
**Status: Not started**

Goal: prepare Lucid for real users.

- [ ] Crash logging policy
- [ ] Local diagnostics export bundle
- [ ] User data location and retention policy
- [ ] Upgrade and migration policy
- [ ] Release checklist
- [ ] Support triage guide
- [ ] Staged rollout implementation beyond manual full-channel publication
- [ ] Public symbol publication
- [ ] Customer-facing crash intake flow
- [ ] CI DRY: composite action, `concurrency:` group with cancel-in-progress, NuGet caching
- [ ] Wire `verify-release-update-feed.ps1` as a release-blocking CI gate
- [ ] Secret-scan step in CI (e.g. gitleaks)
- [ ] Document update-channel trust model in `docs/security-model.md` (key custody, feed integrity, downgrade protection)
- [ ] Central package management (`Directory.Packages.props`)
- [ ] Dependabot/Renovate config scoped to patch/minor only

**Exit criteria:**
- Releases are repeatable
- User data is local, inspectable, and bounded
- Failures can be diagnosed without violating privacy


---

## Architecture Review

### Layered Shape (Correct — Preserve)
Views → ViewModels → Services (33 domains) → Native/Persistence. Matches `docs/architecture.md`.
The architecture problem is not the layers — it is composition and assembly boundaries.

### Concern 1: Dual DI Idioms (C4)
`AppServices.cs` is a static service locator consumed by 32 files. Page-level ViewModels use
constructor injection. Two competing idioms. The strangler plan:

1. Introduce `IServiceRegistry` (or `Microsoft.Extensions.DependencyInjection` — zero extra weight on .NET 8)
   and have `AppServices.Initialize()` populate it. Static properties become thin delegating reads.
   No consumer changes yet. Zero behavioral risk.
2. Freeze the locator — new code may not reference `AppServices.*` (banned-symbol analyzer with
   grandfather list of the existing 32 files).
3. Migrate per page — construct each ViewModel from the registry in one factory, removing
   `AppServices` reads from that feature. One page per session keeps regression risk near zero.
4. Endgame — `AppServices` shrinks to lifecycle orchestration (start order, shutdown flush),
   which is its legitimate remaining job.

### Concern 2: No Library Boundary (Test Linking Is the Symptom)
`Lucid.Tests` links 57 production files by path because referencing the WinUI exe drags in
packaging targets. The fix is `Lucid.Core`: a plain `net8.0-windows` class library holding pure
services (Cleanup, Automation models, Persistence, Trust, Intelligence rules). App and Tests both
reference it; file-linking disappears; the csproj whitelist problem also shrinks. Do this after C5
— moving files is cheap once globbing is default.

### Concern 3: Execution Priority Queue Missing
28 executors follow `IActionExecutor` with dry-run/rollback — good. Missing: a formal Execution
Priority Queue (Foreground/Background/Idle-only classes). Until built, concurrent heavy operations
are prevented only by convention. Belongs in Phase 5.

### Concern 4: Native Boundary Is Silently Optional
The csproj copies `lucid_scanner.dll` if present and logs "skipping" if not. Release builds must
fail loudly — make the copy step `Error` severity for Release/publish. Add Rust build+test to CI.

---

## Code Quality Backlog

| Issue | Evidence | Action | Priority |
|---|---|---|---|
| God composition root | `AppServices.cs` 2,052 lines / ~100 statics | Strangler migration | P1 |
| Silent failures | 48 `catch { }` | Sweep → logger events or justified comments | P1 |
| Debug prints | 33 `Debug/Console.WriteLine` | Route to `IOperationalLogger`; ban via analyzer | P1 |
| Oversized ViewModels | `SimulationViewModel` 1,182; `InsightsPageViewModel` 851; `DashboardViewModel` 813 | Extract presentation sub-services; decompose per feature | P2 |
| Oversized code-behind | `CompanionOverlayWindow.xaml.cs` 879 lines | Move logic into VM + window-interop helper | P2 |
| Constructor bloat | `DashboardViewModel` 15 params | Group into 2–3 cohesive facades after DI migration | P2 |
| `async void` | 12 occurrences | Audit — acceptable only for UI event handlers; wrap bodies in try/catch | P1 |
| Sync-over-async | 9 `.Result`/`.Wait()` | Replace with await or document why safe | P1 |
| Dead code | 9 excluded files incl. `MockTelemetryService.cs`, `ShellViewModel.cs`, 3 controls, 3 models | Delete (git preserves them) | P1 |
| Loose service files | 5 telemetry files at `Services/` root | Relocate to `Services/Telemetry/` | P2 |
| TFM mismatch | App targets `19041.0`, Tests target `22621.0` | Align or document why tests target newer SDK | P2 |

---

## Testing Plan

### Current Status
- 40 test files / 160 test methods — good structure, real assertions, Moq + FluentAssertions
- ~25% of test files are untracked (C1) — commit first
- No coverage threshold or report rendering; Cobertura XML uploaded then ignored
- Rust: 9 tests; no CI job
- No integration tests of SQLite persistence against real files beyond unit scope

### Improvement Plan (ordered)

**P0:** Commit the untracked test files (part of C1)

**P1: Executor safety contract suite**
Parameterized test over all 28 executors asserting doctrine invariants:
- Dry-run never mutates state
- Destructive executors declare rollback metadata or are explicitly non-rollbackable
- Rollback after execute restores state where testable
- Metadata contract validation passes
This is the highest-value test investment — it mechanizes the safety doctrine.

**P1: Persistence durability tests — done 2026-06-10**
- Queue overflow behavior — covered (back-pressure metrics, drop callback, post-drain recovery)
- Flush-on-shutdown — covered (existing dispose final-flush tests)
- Corrupt DB recovery — covered (backup evidence, WAL/SHM hygiene, recreate + migrate)
- Schema migration paths — covered (existing migration tests)

**P1: Rust tests + CI job**
- Path handling, long-path `\\?\` behavior, junction/symlink cycles
- FFI surface: null/invalid UTF-16 inputs must not panic across the boundary (panic across FFI is UB)
- Add `cargo test`, `cargo clippy -D warnings`, `cargo fmt --check` to CI
- Publish DLL as CI artifact consumed by publish job

**P2: Coverage visibility**
- Publish coverage summary to CI job summary
- Set a soft floor (e.g. fail under 30%, ratchet upward)

**P3: Smoke automation**
- Script the manual smoke checklist steps that are scriptable
- Run post-publish in CI

---

## Tooling and Standards

| Area | Current State | Target |
|---|---|---|
| Line endings | No `.gitattributes`; 613-file churn | Add `.gitattributes`; renormalize once (C2) |
| Editor config | None | `.editorconfig` encoding/indent/whitespace + C# naming rules |
| Formatting | Manual consistency (unenforced) | `dotnet format --verify-no-changes` in CI |
| Analyzers | Default only | Enable `AnalysisLevel=latest`, `EnforceCodeStyleInBuild=true`, `BannedApiAnalyzers` |
| Warnings | 0 today | `TreatWarningsAsErrors=true` in `Directory.Build.props` — cheapest moment is now |
| Package versions | Inline in csproj ×2 | `Directory.Packages.props` central package management |
| Rust tooling | No fmt/clippy config; no CI | `cargo fmt --chec