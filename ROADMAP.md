# Lucid Roadmap

> Last audited: 2026-06-11. This is the single source of truth for project state, priorities,
> completed work, and engineering direction. All other roadmap and state documents are retired
> into `docs/history/`. Update this file after every completed task.

> ⚠️ **Scope freeze in force (decided 2026-06-14):** a whole-project review found the
> implementation has run ahead of this roadmap — ~521 files across 42 service
> subdomains, including several domains not scoped here (Autonomy, Distributed,
> Companion, Visual/Desktop context, Simulation). **Decision: Option A — freeze new
> out-of-roadmap scope now; rebaseline (Option B) at the Phase-1 green bar
> (`v0.1-foundation`).** Do **not** add a new out-of-roadmap service domain without
> explicit owner sign-off — new work hardens what already exists. Full mapping and
> the rebaseline trigger: **[`docs/SCOPE_RECONCILIATION.md`](docs/SCOPE_RECONCILIATION.md)**.

---

## Table of Contents

1. [Project Identity](#project-identity)
2. [Strategic Direction](#strategic-direction)
3. [Current Verified Baseline](#current-verified-baseline)
4. [Critical Issues — Act First](#critical-issues--act-first)
5. [Completed Work](#completed-work)
6. [Product Roadmap](#product-roadmap)
7. [Architecture Review](#architecture-review)
8. [Code Quality Backlog](#code-quality-backlog)
9. [Testing Plan](#testing-plan)
10. [Tooling and Standards](#tooling-and-standards)
11. [Documentation State](#documentation-state)
12. [Dependency Review](#dependency-review)
13. [Deferred Work](#deferred-work)
14. [Definition of Professional Quality](#definition-of-professional-quality)

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
| Test system | xUnit 2.9.2 + FluentAssertions 6.12.1 + Moq 4.20.72 + coverlet; 296 passing C# tests |
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

Verified 2026-06-11 from repository inspection and local verification.

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
- 296 passing C# tests (verified 2026-07-02 via `dotnet test`)
- 19 Rust tests passing (verified 2026-07-02 via `cargo test`)
- 27 concrete action executors implementing `IActionExecutor` (28 executor files including
  the abstract `OpenApplicationExecutorBase` — earlier docs counted 28; the registered
  production set in `AppServices` is 27)

**Known active issues (not yet fixed):**
- CRLF/LF churn — `.gitattributes` committed 2026-06-10; repo-wide renormalize still pending (C2)
- ~~`release/` (740 MB of generated artifacts) not in `.gitignore`~~ — resolved 2026-06-10 (C3)
- `AppServices.cs` is 2,052 lines, ~100 static properties — static service locator
- `Lucid.App.csproj` has 481 explicit `<Compile Include>` entries instead of default globbing
- 48 empty `catch { }` blocks; 33 `Debug/Console.WriteLine` calls
- Rust scanner now has 19 tests but is still absent from CI entirely
- Local workstation prerequisites now include Visual Studio Build Tools with the C++ workload,
  Windows SDK import libraries, Rustup/Cargo, and Inno Setup for the optional setup-exe gate.
  `scripts/verify-dev.ps1` initializes the Visual Studio developer environment for Rust tests
  when `link.exe` is not already on PATH.
- `NETSDK1206` warning during build — expected, non-critical, from Windows App SDK NuGet

---

## Critical Issues — Act First

These block professional quality and must be resolved before any new feature work.

### C1 — Clean-checkout / CI proof (P0) — done 2026-07-24
The load-bearing CI, release, installer, and support infrastructure that was previously local-only
is now committed and verified from a clean checkout and GitHub Actions.

**Fix:** Verify with a scratch clone + CI run, then keep the roadmap aligned with that evidence.

- [x] Commit `.gitattributes` + `.editorconfig` first (C2) — done 2026-06-10 (`f7e38ea`)
- [x] Stage and commit: test files, docs, production source fixes — done 2026-06-10 (`e1f52fa`);
      verified beforehand: build clean, 153/153 C# tests, 9/9 Rust tests
- [x] Stage and commit: CI, release, installer, support, and versioning infrastructure —
      done 2026-06-11 (`107a6fb`): `.github/workflows/lucid-build.yml`, `scripts/verify-dev.ps1`,
      14 additional `scripts/*.ps1`, `installer/`, `Directory.Build.props`, `release/*.json`,
      and `AUDIT_ROADMAP.md`
- [x] Confirm CI green from clean checkout — done 2026-07-24:
      scratch clone `C:\Users\tyler\AppData\Local\Temp\lucid-clean-checkout-20260724-165334`
      passed `scripts\verify-release.ps1` end-to-end: Release build, 351 C# tests, publish,
      smoke, package/update-feed checks, installer round-trip, support bundle export, and 19 Rust
      tests. GitHub Actions run `30129408800` on PR #28 / commit `7f5affc` passed: Debug build,
      Release build, Debug tests, Release tests, and publish release artifact.

### C2 — Line-ending renormalization still pending (P0)
613 modified files showing ~113k insertions / ~112k deletions — almost entirely CRLF↔LF.
Real changes are invisible inside whole-file diffs. `git blame` is destroyed on every touched file.

**Fix:** Add `.gitattributes` (`* text=auto`, explicit `eol=crlf` for `.ps1/.bat/.slnx` if desired),
add `.editorconfig`, then run a one-time `git add --renormalize .` commit — isolated from any
functional change.

- [x] Add `.gitattributes` and `.editorconfig` — committed 2026-06-10 (`f7e38ea`); newly staged
      files now land normalized
- [ ] Run `git add --renormalize .` — deferred: keep this isolated from functional changes as a
      dedicated normalization commit now that the load-bearing source is tracked
- [ ] Commit as `chore: normalize line endings` with no functional changes mixed in

### C3 — `release/` (740 MB) not in `.gitignore` (P0)
One careless `git add .` permanently bloats history. Also makes `git status` noise normal —
which is how C1 happened.

**Fix:** Add `release/` to `.gitignore`. `installer/` is source — commit it.

- [x] Add `release/` to `.gitignore` — done 2026-06-10 as `release/*` with carve-outs for the two
      repo-tracked contract files (`release-metadata.json`, `release-operations.json`) that
      `scripts/validate-release-*.ps1` and 10 other release scripts consume. A bare `release/` rule
      would have prevented git from ever descending into the directory, blocking those negations.
- [x] Confirm `git status` ignores it — verified: `git check-ignore` matches generated artifact
      subdirectories while the two repo-tracked contract JSONs remain visible to git as intended

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
Current guard coverage is partial: `scripts/check-app-source-includes.ps1` verifies ViewModels,
Services, and Core, but the 6 excluded Controls/Models files are still outside that guard.

**Fix:** Delete or archive the 9 orphans, remove the Remove/Include machinery, return to default globbing,
retire `check-app-source-includes.ps1`.

- [ ] Identify the 9 excluded orphan files — determine: delete vs. keep vs. move to branch
- [ ] Remove `<Compile Remove>` globs and all 481 `<Compile Include>` entries
- [ ] Confirm build output identical; retire guard script from CI

### C6 — Rust absent from CI; Release native DLL remains optional (P1)
Test depth improved materially during Phase 3: 249 C# tests, 19 Rust tests, and executor safety
coverage across the 27 registered production executors. Remaining risk is CI enforcement and native
artifact reliability: Rust still has no CI job, and the build silently skips copying
`lucid_scanner.dll` when missing — a broken native build is undetectable until runtime.

**Fix:** See Testing Plan. Make Release copy step a hard error when DLL is missing.

- [x] Executor safety contract suite (all 27 registered executors) — done 2026-06-10:
      parameterized suite over the full production set (registration under enforced metadata
      contract, metadata/runtime declaration consistency, dry-run purity at guarded runtime
      seams, hostile rollback-token safety)
- [x] Rust unit tests — done 2026-06-10: 19 tests covering FFI argument/UTF-8 validation,
      long-path `\\?\` traversal, junction-cycle termination, path-form handling
- [ ] `cargo test/clippy/fmt` CI job — blocked on human review (CI changes are an
      autonomous hard stop)
      - Local precondition now clean as of 2026-06-11: `cargo fmt --check`,
        `cargo clippy -- -D warnings`, and `cargo test` all pass
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
- `scripts/check-app-source-includes.ps1` verifies ViewModels, Services, and Core C# files are either compiled or documented as intentional exclusions
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
- `scripts/verify-release.ps1` keeps setup-exe generation as an explicit `-BuildSetupExe` gate
  so the normal release verification path does not depend on Inno Setup being installed.
  `scripts/build-setup-exe.ps1` derives the exact package zip from `release/release-metadata.json`
  instead of selecting the newest zip by timestamp, and `scripts/verify-dev.ps1` resolves Cargo
  from PATH or the standard Rustup user install path. Verified 2026-07-24: PowerShell parse checks
  passed, `git diff --check` passed, and `scripts/verify-release.ps1` reached Rust after Release
  build, 351 C# tests, publish, smoke, package, update feed, installer round-trip, and support
  bundle gates. Follow-up on 2026-07-24 installed Visual Studio Build Tools / Windows SDK and
  updated `scripts/verify-dev.ps1` to initialize `VsDevCmd.bat` for Rust when needed. Full
  `scripts/verify-release.ps1` then passed locally end-to-end, including 351 C# tests and 19 Rust
  tests. `scripts/build-setup-exe.ps1` also produced `Lucid-Setup-0.1.0-preview.exe` with checksum.
- C1 clean-checkout and CI proof completed 2026-07-24: scratch clone
  `C:\Users\tyler\AppData\Local\Temp\lucid-clean-checkout-20260724-165334` passed
  `scripts\verify-release.ps1` end-to-end; GitHub Actions run `30129408800` passed all Debug,
  Release, test, and publish artifact jobs for PR #28 at `7f5affc`.

### Storage Intelligence — asset migration (2026-07-13)
- Near-duplicate detection (`NearDuplicateDetectionService`) ported from the archived Drive_Agent project: copy/version naming patterns, format-variant pairs, and name-similarity matching, with size-proximity and per-directory bucketing guards. Review-only by design — each match carries a plain-English reason and confidence, pairs already reported as exact-hash duplicates are excluded, and no delete action is exposed. Surfaced in a "Possible near-duplicates — review manually" section on the Storage page and in the scan-complete timeline detail.
- `StorageCategoryAnalyzer` enriched with cache/junk location rules ported from the archived Drive Management project (INetCache, Prefetch, SoftwareDistribution\Download, CrashDumps, Chromium/VS Code cache dirs, pip/npm/yarn caches) plus `.crash`/`.swp` extensions. Classification only. Specific cache rules are ordered before the general `\Windows\` rule; the source project's overbroad "Firefox Profiles = cache" rule was narrowed to `cache2` only (profiles hold bookmarks/credentials), with a regression test pinning that.
- 29 new tests in `Lucid.Tests/Storage/`. Verified 2026-07-13: `dotnet build` (Debug x64) 0 warnings 0 errors; `dotnet test` 351 passed, 0 failed.

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
- Rust scanner formatting and lint are locally clean: `cargo fmt --check` and
  `cargo clippy -- -D warnings` pass as of 2026-06-11

### Session 2026-06-10 (autonomous)
- Verified full green state: x64 Debug build clean, 153/153 C# tests, 9/9 Rust tests
- Committed `.gitattributes` + `.editorconfig` (C2 step 1, `f7e38ea`)
- Committed prior session's verified work — executor tests, OllamaClient endpoint enforcement,
  consent fixes, privacy scanner tests, `.gitignore` release carve-out (C3) — as `e1f52fa`
- Removed stale `.git/index.lock` (0-byte, orphaned by a crashed git process; no git running)
- Deliberately left uncommitted pending human review: modified CI workflow, 14 release/support scripts,
  `installer/`, `Directory.Build.props`, `release/*.json`, `verify-dev.ps1`, `AUDIT_ROADMAP.md`

### Session 2026-06-11 (Codex review)
- Reviewed committed stabilization work and pending CI/release infrastructure without committing
  human-gated build, CI, release, or installer files
- Corrected roadmap drift: stale test counts, stale Rust/executor wording, stale untracked-test
  claims, partial source-inclusion guard scope, and the missing-section Table of Contents entry
- Fixed local Rust CI preconditions without changing scanner behavior: formatted Rust sources,
  added `# Safety` docs for unsafe FFI exports, replaced manual C string construction, and used
  clippy-preferred descending sort key
- Verified: x64 Debug build clean (0 warnings, 0 errors), 239/239 C# tests, 19/19 Rust tests,
  `cargo fmt --check`, `cargo clippy -- -D warnings`, release metadata validation,
  release operations validation, and source-inclusion check
- Commit produced: `0e8cb13` (`chore: align roadmap and rust lint baseline`)

### Session 2026-06-11 (post-release-infra verification)
- Re-audited `main` after the human-reviewed release/installer/CI commit `107a6fb`; repository is
  now clean and the previously local-only support infrastructure is tracked on `origin/main`
- Verified current integrated developer path: `scripts/verify-dev.ps1` completes successfully on
  the committed tree, including release metadata validation, operations-policy validation,
  source-inclusion checks, x64 Debug build, 239/239 C# tests, and 19/19 Rust tests
- Roadmap advanced from the pre-commit C1 snapshot to the current post-commit state; remaining C1
  proof is fresh-clone / CI confirmation rather than missing tracked source

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

### Session 2026-06-10 (autonomous, third run)
- Closed Phase 3 / C6 item: Rust unit tests for path handling, long-path `\\?\` behavior,
  junction/symlink cycles, FFI null/invalid inputs — 10 new tests (9 → 19), test-only change,
  no production code touched
- New scanner tests: verbatim `\\?\` root parity with plain roots, traversal of trees beyond
  the 260-char legacy MAX_PATH limit, junction-cycle termination via `mklink /J` (skipped, not
  counted, no infinite recursion), trailing-separator root handling
- New FFI tests: per-out-param null rejection, non-UTF-8 path rejection without panicking
  across the C boundary, `n > 1000` cap enforcement with untouched out-params, null-argument
  rejection for `lucid_scan_top_files`, missing-root I/O error, `lucid_free(null)` no-op
- Remaining C6 sub-items unchanged: Rust CI job and Release hard-error for missing DLL are
  blocked on human review (CI/build-config = autonomous hard stop)
- Verified: 19/19 Rust tests (`cargo test`), x64 Debug build clean (0 warnings, 0 errors),
  160/160 C# tests (`dotnet test`)

### Session 2026-06-10 (autonomous, fifth run)
- Closed Phase 3 / C6 item: executor safety contract suite across the full registered
  production executor set — new `ExecutorSafetyContractTests` (79 test cases) asserting,
  for every executor: (1) clean registration under the registry's enforced metadata
  contract, (2) catalog metadata matches runtime declarations (privilege, confirmation,
  dry-run, rollback, consent/failure-mode/diagnostics notes), (3) ActionId uniqueness and
  format, (4) dry-run never reports applied changes, never lets an exception escape, and
  never touches a mutating runtime operation — proven via guarded throwing fakes injected
  into all 8 seam-equipped executors (Sfc, DISM, FlushDns, Winsock, StoreReset,
  NetworkAdapter, RecycleBin, WindowsUpdateCache) plus a guarded `IStartupManagementService`
  for the 4 startup executors, (5) rollback with an unknown token never claims restoration
  and fails safely (only `NotSupportedException` from non-rollbackable executors is allowed)
- Scope notes recorded in the suite itself: 4 unseamed cleanup executors (temp files,
  browser cache, delivery optimization, old downloads) are excluded from behavioural
  dry-run invocation because their dry-run scans live user directories (read-only by
  design, machine-dependent); their dry-run/rollback behaviour remains covered by
  dedicated seam/parameter tests. `action.startup.backup-startup-state` is an explicit,
  documented exception to the unknown-token rule: its rollback is an idempotent deletion
  of its own snapshot artifact, so a missing target legitimately reports success
- Count correction: the registered production set is 27 concrete executors (28 files
  including the abstract `OpenApplicationExecutorBase`); docs previously said 28
- Test-only change: new test file + 24 additive `<Compile Include>` links in
  `Lucid.Tests.csproj` (18 executor sources + 6 support files); zero production code edits
- Verified: x64 Debug build clean (0 warnings, 0 errors), 239/239 C# tests
  (160 → 239), 19/19 Rust tests

### Session 2026-06-10 (autonomous, fourth run)
- Discovered and repaired truncation of this file: the tail (final rows of the Tooling table plus
  sections Documentation State, Dependency Review, Deferred Work, Definition of Professional
  Quality) was lost to a truncated write committed unnoticed in `e1f52fa` and carried through all
  subsequent commits. Reconstructed from `AUDIT_ROADMAP.md` with an inline reconstruction note.
  The stale ToC entry for a missing "Engineering Professionalization Roadmap" section was removed
  2026-06-11
- Diagnosed and dismissed phantom repo corruption: the agent sandbox's filesystem mount served
  stale/partial views of committed files and `.git` internals (apparent mid-file truncations,
  "index file corrupt" / "improper chunk offset" errors). Native Windows `git status` and
  `git fsck` confirmed the repository is healthy and the working tree matches HEAD apart from the
  known human-review-pending files. Operational note for future sessions: verify repo state with
  native Windows git before trusting sandbox-mount git output
- No production code touched; documentation-only session
- Verified after repair: x64 Debug build clean (0 warnings, 0 errors), 160/160 C# tests,
  19/19 Rust tests


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
- [x] Commit all untracked source (C1) — test files, docs, source fixes committed 2026-06-10;
      completed 2026-06-11 (`107a6fb`) for CI/release scripts, `installer/`,
      `Directory.Build.props`, `release/*.json`, and `AUDIT_ROADMAP.md`; remaining C1 work is
      clean-checkout / CI proof
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
- [ ] Preserve malformed migration-state evidence instead of silently discarding it during
      installer data migration (`installer/Migrate-LucidData.ps1`)
- [ ] Remove csproj compile whitelist (C5)
- [ ] Real certificate-backed signing inputs — flip release metadata to `authenticode-required`
- [ ] Expand launch smoke gate into deeper navigation and telemetry assertions
- [ ] Decide non-interactive CI smoke policy: current pending script can record `skipped`, and
      artifact verification accepts `passed` or `skipped`
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
- [x] Rust scanner tests (19 tests)
- [x] Executor safety contract suite across all 27 registered executors (dry-run purity, rollback metadata, hostile-path inputs) (C6) — done 2026-06-10, see session notes
- [x] Rust unit tests for path handling, long-path `\\?\` behavior, junction/symlink cycles, FFI null/invalid inputs (C6) — done 2026-06-10, all 19 passing
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
27 registered production executors follow `IActionExecutor` with dry-run/rollback — good. Missing:
a formal Execution Priority Queue (Foreground/Background/Idle-only classes). Until built,
concurrent heavy operations are prevented only by convention. Belongs in Phase 5.

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
| Migration-state recovery loses evidence | `installer/Migrate-LucidData.ps1` swallows JSON parse failure and rewrites migration state | Preserve the bad state file or emit explicit recovery evidence before replacement | P1 |
| Loose service files | 5 telemetry files at `Services/` root | Relocate to `Services/Telemetry/` | P2 |
| TFM mismatch | App targets `19041.0`, Tests target `22621.0` | Align or document why tests target newer SDK | P2 |

---

## Testing Plan

### Current Status
- 296 passing C# test cases — good structure, real assertions, Moq + FluentAssertions
- Test files are committed; remaining C1 blocker is fresh-clone / CI proof on the now-tracked infrastructure
- No coverage threshold or report rendering; Cobertura XML uploaded then ignored
- Rust: 19 tests; no CI job
- SQLite durability tests cover real file behavior; broader app-level persistence integration coverage is still absent

### Improvement Plan (ordered)

**P0: Commit the untracked test files — done 2026-06-10**
Completed as part of the C1 cleanup sequence. Remaining P0 proof is clean-checkout / CI verification
on the committed infrastructure.

**P1: Executor safety contract suite — done 2026-06-10**
Parameterized test over all 27 registered executors asserting doctrine invariants:
- Dry-run never mutates state — covered at the seam boundary (guarded throwing fakes)
- Destructive executors declare rollback metadata or are explicitly non-rollbackable — covered
- Rollback with unknown token never claims restoration — covered (idempotent
  artifact-deletion rollback documented as the one legitimate exception)
- Metadata contract validation passes for the full set — covered
Remaining (not blocking): "rollback after execute restores state" end-to-end remains
per-executor (existing rollback tests cover the staging-based cleanup executors).

**P1: Persistence durability tests — done 2026-06-10**
- Queue overflow behavior — covered (back-pressure metrics, drop callback, post-drain recovery)
- Flush-on-shutdown — covered (existing dispose final-flush tests)
- Corrupt DB recovery — covered (backup evidence, WAL/SHM hygiene, recreate + migrate)
- Schema migration paths — covered (existing migration tests)

**P1: Rust tests + CI job**
- Path handling, long-path `\\?\` behavior, junction/symlink cycles — done 2026-06-10 (19 tests total)
- FFI surface: null/invalid inputs must not panic across the boundary (panic across FFI is UB) — done 2026-06-10
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
| Line endings | `.gitattributes` committed; one-time renormalize still pending | Renormalize once after pending CI/release files are reviewed (C2) |
| Editor config | `.editorconfig` committed; enforcement still limited | Enforce formatting/code style in CI |
| Formatting | Manual consistency (unenforced) | `dotnet format --verify-no-changes` in CI |
| Analyzers | Default only | Enable `AnalysisLevel=latest`, `EnforceCodeStyleInBuild=true`, `BannedApiAnalyzers` |
| Warnings | 0 today | `TreatWarningsAsErrors=true` in `Directory.Build.props` — cheapest moment is now |
| Package versions | Inline in csproj ×2 | `Directory.Packages.props` central package management |
| Rust tooling | Local `fmt`, `clippy -D warnings`, and tests pass; no CI job | Add `cargo fmt --check` + `cargo clippy -D warnings` + `cargo test` CI job |
| Commit hooks | None | Optional lightweight pre-commit running `dotnet format` on staged files — skip if it adds friction |
| CI duplication | 4 jobs repeat restore/validate verbatim | Composite action or `workflow_call`; `concurrency:` group with cancel-in-progress; NuGet caching |

> **Reconstruction note (2026-06-10):** everything from the final three rows of the table above
> through the end of this file was reconstructed after the original tail of this document was lost
> to a truncated write — the truncation was committed unnoticed in `e1f52fa` and carried through
> every subsequent commit. Sources for the reconstruction: `AUDIT_ROADMAP.md` (the 2026-06-10 audit
> this file consolidates) and the surviving body of this document. The stale Table of Contents entry
> for a missing "Engineering Professionalization Roadmap" section was removed on 2026-06-11; the
> professionalization phases live under Product Roadmap.

---

## Documentation State

**Good and current:** `README.md` (honest, verified-on date, doctrine summary), `docs/architecture.md`,
`docs/security-model.md`, `docs/ui-guidelines.md`, `docs/release-packaging.md`, release checklists,
`ONBOARDING.md`, `PROJECT_INTEGRITY.md`.

**Problems (tracked as C8):**
- `CLAUDE.md` / `AGENTS.md` / `CODEX.md` are near-identical ~14 KB copies — single-source them;
  reduce the other two to one-paragraph pointers
- `CURRENT_STATE.md` counts rot within days of writing — delete it; this file plus CI are the live state
- `REMAINING_WORK.md` content was folded into this roadmap — retire the file
- `docs/reports/` mixes living docs with dead audit artifacts — move to `docs/history/` with dated
  filenames; `NEW_ROADMAP.md` must not coexist with this file unmarked
- `docs/Structure.txt` and `docs/active-file-inventory.md` are stale by construction — delete

**Missing:**
- `LICENSE` — decide proprietary notice vs. OSS license; currently legally ambiguous
- `CHANGELOG.md` — squash-commit history makes this more important, since `git log` carries less narrative
- `scripts/README.md` — 17 scripts with no index of when each runs
- In-repo documentation of the `XamlPreCompile` + `build_vs.bat` quirk (currently only in `CLAUDE.md`;
  `build_vs.bat` lives outside the repo at `C:\Users\tyler\` — move it into `scripts/`)
- README quick-start (clone → setup → build → run), prerequisites (VS components, Rust toolchain),
  and a link map of the doc set

---

## Dependency Review

| Package | Version | Status |
|---|---|---|
| Microsoft.WindowsAppSDK | 1.5.240802000 | Behind current (1.6/1.7 line). Major upgrade deliberately deferred; schedule as an isolated pass with smoke testing |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.1742 | Fine |
| CommunityToolkit.Mvvm | 8.2.2 | Minor updates available; low risk |
| Microsoft.Data.Sqlite | 8.0.0 | Take 8.0.x patches; defer 9.x/10.x to the next .NET upgrade |
| System.* (PerformanceCounter, Management, ServiceController) | 8.0.0 | Aligned with TFM; fine |
| xunit / runner / coverlet / FluentAssertions / Moq | current-ish | FluentAssertions 7+ changed licensing — staying on 6.12.1 is a deliberate, reasonable choice; document it |
| windows-sys (Rust) | 0.59 | Fine; pinned via committed `Cargo.lock` |

Hygiene: no unused dependencies detected; dev-only packages correctly `PrivateAssets=all`.
Recommended additions: central package management (`Directory.Packages.props`); Dependabot/Renovate
scoped to patch/minor only, so upgrades become visible PRs instead of background drift.

---

## Deferred Work

Deliberately not scheduled. Do not pick these up without revisiting the rationale recorded here.

- **MSIX packaged distribution** — deferred until the unpackaged path is stable (Phase 2)
- **Windows App SDK 1.6/1.7 major upgrade** — isolated pass with smoke testing; not during stabilization
- **Microsoft.Data.Sqlite 9.x/10.x** — patches only on 8.0.x; majors ride the next .NET upgrade
- **FluentAssertions 7+** — licensing change; staying on 6.12.1 is deliberate
- **`Lucid.Core` library extraction** — after C5 lands (moving files is cheap once globbing is default)
- **Execution Priority Queue** — Phase 5 governance work; concurrency restraint is convention-only until then
- **Repo-wide line-ending renormalization (C2 final step)** — blocked until the pending CI/script
  changes are human-reviewed and committed, so the renormalize commit stays isolated
- **Pre-commit hooks** — optional; skip if friction outweighs value

---

## Definition of Professional Quality

Lucid is professionally maintained when all of the following are simultaneously true:

1. A fresh `git clone` on a clean Windows machine reaches green build + green tests using only
   README instructions.
2. `git status` is empty after any completed work session; nothing load-bearing is untracked.
3. CI fails on: build warnings, formatting drift, failed tests (C# *and* Rust), missing native
   artifact in Release, unverified release feed.
4. Every destructive executor has contract tests proving dry-run purity and rollback behavior
   (or an explicit non-rollbackable classification).
5. No empty catch blocks without a written justification; all diagnostics flow through
   `IOperationalLogger`.
6. One composition idiom: constructor injection from a single registry; `AppServices` static
   surface is frozen and shrinking.
7. Documentation contains no claims contradicted by the repository (counts, structure, commands),
   and exactly one source of truth exists per topic.
8. Releases are signed (fail-closed), changelogged, tagged, and reproducible from CI.
