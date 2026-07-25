# Lucid Professionalization Roadmap

> **Scope note:** This document is the output of a full-repository quality audit performed 2026-06-10.
> It is deliberately separate from `ROADMAP.md`, which remains the authoritative *product/strategy*
> roadmap. This file covers *engineering professionalization only*: repository hygiene, code quality,
> architecture debt, tooling, testing, and release readiness. Where the two overlap (e.g. Phase 1
> Platform Stabilization), this document references rather than duplicates.

---

## Executive Summary

Lucid is substantially more mature than a prototype: it has a real CI pipeline, a doctrine-driven
documentation culture, a disciplined executor/rollback pattern, structured operational logging, and
~480 compiled production files organized into 33 service domains. The architecture philosophy
(trust-first, explainable, reversible) is visibly enforced in code, not just documents.

The gap to professional production quality is concentrated in five areas:

1. **Repository state integrity** — 30+ source files are untracked, including 14 release scripts that
   the committed CI workflow *invokes*. A fresh clone of `main` today would fail CI. This is the
   single most urgent issue in the repository.
2. **Line-ending instability** — no `.gitattributes` exists; the working tree currently shows a
   613-file, ~113,000-line CRLF/LF churn diff. Until fixed, every real change is buried in noise.
3. **Composition-root debt** — `AppServices.cs` (2,052 lines, ~100 static service properties) is a
   static service locator consumed directly by 32 ViewModel/View files, alongside a parallel
   constructor-injection pattern. Two competing DI idioms coexist.
4. **Build-system fragility** — `Lucid.App.csproj` is a 633-line manual compile whitelist
   (481 explicit `<Compile Include>` entries against 5 directory-wide `<Compile Remove>` globs),
   and `Lucid.Tests` links 57 production files by relative path instead of referencing a library.
5. **Test depth vs. surface area** — 126 unit tests against ~480 production files and 28 executors;
   the Rust scanner has zero tests and is absent from CI entirely.

Overall assessment: **mid-stage professionalization**. The bones are strong; the work required is
mostly hygiene, consolidation, and coverage — not redesign. Estimated effort: 4–6 focused working
sessions across the five phases below, with Phase 1 achievable in under one session.

---

## Project Identity

| Attribute | Value |
|---|---|
| Project name | Lucid |
| Project type | Windows desktop application (local-first operational intelligence platform) |
| Languages | C# (primary, ~90,700 LOC in app), Rust (539 LOC native module), PowerShell (17 ops scripts), XAML |
| Frameworks | WinUI 3 / Windows App SDK 1.5, .NET 8 (`net8.0-windows10.0.19041.0`), CommunityToolkit.Mvvm 8.2.2 |
| Native layer | `lucid-native/lucid-scanner` — Rust `cdylib` over `windows-sys`, consumed via P/Invoke |
| Persistence | SQLite via `Microsoft.Data.Sqlite` 8.0.0 |
| Package manager | NuGet (C#), Cargo (Rust) |
| Build system | `dotnet build Lucid.slnx -p:Platform=x64` (slnx solution format); VS MSBuild required once after clean for `XamlPreCompile` |
| Test system | xUnit 2.9.2 + FluentAssertions 6.12.1 + Moq 4.20.72 + coverlet; 39 test files, 126 `[Fact]`/`[Theory]` methods |
| CI | GitHub Actions (`.github/workflows/lucid-build.yml`): Debug + Release build/test on windows-latest, plus publish job |
| Deployment target | Unpackaged self-contained win-x64 (`WindowsPackageType=None`), PowerShell installer scripts in `installer/` |

---

## Current Strengths

These should be preserved and treated as load-bearing conventions:

- **Doctrine enforcement in code.** Confidence-aware security language, rollback tokens on
  destructive executors, dry-run support, and consent gates (`HumanReviewGate`) actually exist —
  the philosophy documents are not aspirational.
- **CI is real.** Four jobs (build/test × Debug/Release) plus a publish job, with TRX and Cobertura
  coverage artifacts uploaded. Policy validation scripts run as CI gates.
- **The source-inclusion policy has a CI guard** (`scripts/check-app-source-includes.ps1`), so the
  fragile csproj whitelist at least cannot drift silently.
- **Nullable reference types and `LangVersion=latest`** are enabled; `ConfigureAwait` appears 222
  times — async hygiene is taken seriously.
- **Zero TODO/FIXME/HACK markers** in the app source. Unfinished work is tracked in docs, not code.
- **No committed binaries, no secrets.** `git ls-files` shows no DLL/EXE/PDB; a credentials scan
  found nothing (the only "token" hits are rollback tokens, which are correct domain language).
- **Service domain organization.** 33 cohesive subdirectories under `Services/` map cleanly to the
  documented architecture (Telemetry, Intelligence, Execution, Trust, Privacy, Replay, …).
- **Test infrastructure pattern is deliberate.** The file-linking approach in `Lucid.Tests.csproj`
  is documented inline with its WinUI-packaging rationale — a real tradeoff, consciously made.
- **Executor metadata contract validation** (most recent commit) shows the project already moving
  toward self-enforcing invariants.
- **`Directory.Build.props` centralizes versioning** (0.1.0) — single version source of truth.

---

## Critical Issues

### C1. Untracked source files that committed code depends on
- **Problem:** `git status` shows 30+ untracked files, including: 14 of the 17 scripts in `scripts/`
  (all release tooling), `Directory.Build.props`, the entire `installer/` directory, 10 test files
  (`Lucid.Tests/Execution/*ExecutorTests.cs`, `Trust/AutomationConsentServiceTests.cs`,
  `Privacy/PrivacyPermissionScannerTests.cs`, `LlmChat/`, `TestInfrastructure/`), and four docs
  (`docs/releases/`, `support-and-crash-policy.md`, `support-triage-guide.md`, `update-publication.md`).
- **Why it matters:** The committed CI workflow invokes `scripts/validate-release-metadata.ps1`,
  `validate-release-operations.ps1`, and `check-app-source-includes.ps1` — all untracked. **CI fails
  on any fresh clone or any other machine.** ~25% of the test suite exists only on this machine.
  One disk failure loses the release pipeline.
- **Affected:** `scripts/`, `installer/`, `lucid-desktop/Lucid.Tests/`, `docs/`, `Directory.Build.props`
- **Fix:** Commit all of the above (after the `.gitattributes` fix in C2 so they land with normalized
  endings). Verify with a scratch clone + CI run.
- **Priority:** **P0 — do first.**

### C2. No `.gitattributes`; line-ending churn poisons every diff
- **Problem:** The working tree shows 613 modified files with ~113k insertions / ~112k deletions —
  almost entirely CRLF↔LF normalization, including in `_archive/`. No `.gitattributes` and no
  `.editorconfig` exist.
- **Why it matters:** Real changes are invisible inside whole-file diffs; `git blame` is destroyed
  on every touched file; cross-environment work (Windows + WSL/CI/agents) re-churns endlessly.
- **Fix:** Add `.gitattributes` (`* text=auto`, explicit `eol=crlf` for `.ps1`/`.bat`/`.slnx` if
  desired, binary patterns), add `.editorconfig`, then run a one-time `git add --renormalize .`
  commit, isolated from any functional change.
- **Priority:** **P0 — do alongside C1, before any other commit.**

### C3. 740 MB `release/` directory of local artifacts, not ignored
- **Problem:** `release/` (740 MB of packages, symbols, installer-test trees, support-bundle test
  output) and `installer-test`/`copy-debug` artifacts sit untracked at the repo root, with no
  `.gitignore` entry. `lucid-desktop/.vs/` also exists locally (ignored, but it confirms VS state
  living inside the tree).
- **Why it matters:** One careless `git add .` commits 740 MB and permanently bloats history. It
  also makes `git status` noise normal, which is how C1 happened.
- **Fix:** Add `release/` to `.gitignore` (it is generated output). Decide `installer/` is *source*
  (commit it — it is 28 KB of PowerShell that the release scripts depend on).
- **Priority:** **P0.**

### C4. `AppServices.cs` — 2,052-line static service registry
- **Problem:** ~100 `public static` service properties; 32 ViewModel/View files reach into it
  directly. Meanwhile page-level ViewModels (e.g. `DashboardViewModel`, 15 constructor parameters)
  use constructor injection — two competing composition idioms in one app.
- **Why it matters:** Static access defeats testability (the linked-file test strategy exists
  partly to route around it), hides dependency graphs, and makes lifetime/ordering implicit. The
  15-parameter constructor is the same disease from the other side: no facade/aggregate boundaries.
- **Affected:** `Lucid.App/AppServices.cs`, `ViewModels/*`, `Views/*`
- **Fix:** Incremental strangler migration (see Architecture Review) — **not** a big-bang rewrite.
  `CURRENT_STATE.md` already says this; this roadmap adds the concrete mechanism.
- **Priority:** **P1 — highest-value structural item, after hygiene.**

### C5. 633-line manual compile whitelist in `Lucid.App.csproj`
- **Problem:** Five `<Compile Remove>` directory globs followed by 481 individual
  `<Compile Include>` entries. Comment says "Remove this section when you're ready to wire up the
  full architecture" — but only **9 files** remain excluded.
- **Why it matters:** Every new file requires a csproj edit; merge conflicts concentrate in one
  file; the CI guard script is a workaround for a problem that can simply be deleted. The original
  reason (excluding future scaffolding) no longer applies at 481/490 inclusion.
- **Fix:** Delete or archive the 9 orphans, remove the Remove/Include machinery, return to default
  globbing, retire `check-app-source-includes.ps1`.
- **Priority:** **P1.**

### C6. Test depth: 126 tests for 480 files; Rust at zero; Rust absent from CI
- **Problem:** Coverage is concentrated in Cleanup/Execution/Persistence/Trust. 28 executors exist;
  destructive-path and rollback coverage is partial. `lucid-native` has **0 tests** and **no CI job**
  — and the build silently skips copying `lucid_scanner.dll` when missing, so a broken native build
  is undetectable until runtime.
- **Why it matters:** For a platform whose brand is *safety and reversibility*, the destructive
  executors and rollback paths are precisely the code that must be provably correct.
- **Fix:** See Testing Review — prioritized plan starting with executor rollback contract tests.
- **Priority:** **P1.**

### C7. 48 empty `catch { }` blocks; 33 `Debug/Console.WriteLine` calls
- **Problem:** Silent failure swallowing in services (e.g. `FileOrganizationWorkflowService`,
  `HumanReviewGate`), and debug-print logging bypassing the structured `IOperationalLogger`.
- **Why it matters:** Directly contradicts the explainability doctrine: a platform that explains
  the system to users must not hide its *own* failures. `REMAINING_WORK.md` already flags this.
- **Fix:** Sweep each `catch { }` into either (a) `IOperationalLogger` diagnostic event, (b) a
  justified `// best-effort: <why>` comment where swallowing is genuinely correct, or (c) removal.
  Replace `Debug.WriteLine` with the operational logger. Enforce via analyzer (CA1031 + banned-API).
- **Priority:** **P1.**

### C8. Documentation drift and root-level doc sprawl
- **Problem:** Nine root `.md` files. `CLAUDE.md`/`AGENTS.md`/`CODEX.md` are near-identical
  14 KB copies that differ in title — guaranteed drift. `CURRENT_STATE.md` is already stale
  (claims 6 test files / 53 tests; reality is 39 files / 126 tests). `docs/reports/` contains four
  historical audit artifacts including `NEW_ROADMAP.md`, which competes with `ROADMAP.md`.
  `docs/Structure.txt` (44 lines) and `docs/active-file-inventory.md` (45 lines) cannot describe a
  524-file app and are stale by construction.
- **Why it matters:** Stale "source of truth" docs are worse than none — agents and humans act on them.
- **Fix:** Single-source the agent instructions; delete or date-stamp historical reports; remove
  stale inventories (git is the inventory). See Documentation Review.
- **Priority:** **P2.**

### C9. `_archive/` committed to main (39 files)
- **Problem:** Orphaned intelligence-engine v1 and a 2026-05-16 WinUI scaffold (including a
  committed `.csproj.user` file) live in tracked `_archive/`.
- **Why it matters:** Git history already preserves deleted code; tracked archives rot, participate
  in repo-wide operations (they are 36 of the 613 line-ending-churned files), and confuse search.
- **Fix:** Tag current state (e.g. `archive/intelligence-v1`), then `git rm -r _archive/`.
- **Priority:** **P2.**

---

## File and Folder Structure Review

### Current structure (tracked, simplified)

```
Lucid/
├── .github/workflows/lucid-build.yml
├── .gitignore
├── AGENTS.md  CLAUDE.md  CODEX.md            ← triplicated agent instructions
├── CURRENT_STATE.md  REMAINING_WORK.md       ← stale state snapshots
├── ONBOARDING.md  PROJECT_INTEGRITY.md  README.md  ROADMAP.md
├── Directory.Build.props                     ← UNTRACKED
├── setup.bat  setup.ps1
├── _archive/                                 ← tracked archive (remove)
├── docs/                                     ← mixed: living docs + stale reports
├── installer/                                ← UNTRACKED source scripts
├── lucid-desktop/
│   ├── Lucid.slnx
│   ├── Lucid.App/        (Controls, Core, Helpers, Models, Services×33, Themes, ViewModels, Views)
│   └── Lucid.Tests/      (8 domain folders; ~25% untracked)
├── lucid-native/lucid-scanner/  (src/lib.rs, src/scanner.rs)
├── release/                                  ← 740 MB local artifacts, NOT ignored
└── scripts/                                  ← 17 scripts; 14 UNTRACKED
```

### Problems
1. Untracked source intermixed with generated output at root (C1, C3).
2. Nine root markdown files where ~4 belong (README, ROADMAP, CLAUDE/agent instructions, this file).
3. `docs/reports/` mixes living documentation with dead audit artifacts.
4. `_archive/` tracked (C9).
5. Inside `Lucid.App`: `Models/` has only 3 files while model types actually live inside each
   `Services/<domain>/` — fine as a convention, but the residual top-level `Models/` (all 3 files
   excluded from compilation) is dead.
6. `Services/` root contains 5 loose files (`ITelemetryService.cs`, `TelemetryHistoryBuffer.cs`,
   `MockTelemetryService.cs`, …) that belong in `Services/Telemetry/`.

### Recommended target structure

```
Lucid/
├── .editorconfig  .gitattributes  .gitignore
├── .github/workflows/
├── README.md  ROADMAP.md  AUDIT_ROADMAP.md  CHANGELOG.md  LICENSE
├── CLAUDE.md                      (single agent-instruction source; AGENTS.md → 3-line pointer)
├── docs/
│   ├── architecture.md  security-model.md  ui-guidelines.md  …
│   ├── releases/
│   └── history/                   (dated, frozen audit reports — or deleted)
├── installer/                     (tracked)
├── lucid-desktop/
│   ├── Lucid.slnx
│   ├── Lucid.App/                 (default compile globs, no whitelist)
│   ├── Lucid.Core/                (future: extracted pure services — see Architecture)
│   └── Lucid.Tests/
├── lucid-native/
└── scripts/                       (tracked, with a scripts/README.md index)
```

### Migration steps
1. Commit `.gitattributes` + `.editorconfig`; run `git add --renormalize .`; commit as
   `chore: normalize line endings` (no functional changes mixed in).
2. Commit all untracked source (scripts, installer, tests, docs, Directory.Build.props).
3. Add `release/` to `.gitignore`.
4. Tag, then delete `_archive/`.
5. Move the 3 loose telemetry files from `Services/` root into `Services/Telemetry/` (namespace
   already matches `Lucid.Services`; adjust if you align namespaces to folders).
6. Delete the 9 compile-excluded orphan files (or move genuinely-future ones to a branch).
7. Consolidate root docs (see Documentation Review).

---

## Code Quality Review

### What's good
- Consistent file-scoped namespaces, XML documentation culture, expressive naming
  (`OperationalNarrativeEngine`, `AlertFatigueManager`), section-divider comments, aligned
  field/parameter formatting. The code *reads* like one author with standards.
- Zero TODO/FIXME debt markers.

### Issues and refactoring targets

| Issue | Evidence | Action |
|---|---|---|
| God composition root | `AppServices.cs` 2,052 lines / ~100 statics | Strangler migration (Architecture) |
| Oversized ViewModels | `SimulationViewModel` 1,182; `InsightsPageViewModel` 851; `DashboardViewModel` 813 | Extract presentation sub-services / partial decomposition per feature |
| Oversized view code-behind | `CompanionOverlayWindow.xaml.cs` 879 lines | Move logic into a `CompanionOverlayViewModel` + window-interop helper; code-behind should be windowing plumbing only |
| Constructor bloat | `DashboardViewModel` 15 params | Group into 2–3 cohesive facades (e.g. `IDashboardIntelligenceFacade`) — but only *after* AppServices migration settles the DI story |
| Silent failures | 48 `catch { }` | Sweep per C7; add analyzer enforcement |
| Debug prints | 33 `Debug/Console.WriteLine` | Route to `IOperationalLogger`; ban via `BannedSymbols.txt` |
| `async void` | 12 occurrences | Audit: acceptable only for UI event handlers; wrap bodies in try/catch that reports to logger |
| Sync-over-async | 9 `.Result`/`.Wait()` | Replace with await or document why safe (e.g. completed-task reads) |
| Dead code | 9 excluded files incl. `MockTelemetryService.cs`, `ShellViewModel.cs`, 3 controls, 3 models | Delete (git preserves them) |
| Loose files | 5 telemetry files at `Services/` root | Relocate to `Services/Telemetry/` |

---

## Architecture Review

### Verdict
The layered shape — Views → ViewModels → Services (33 domains) → Native/Persistence — is correct
for this product and matches `docs/architecture.md`. The architecture problem is not the layers;
it is **composition** and **assembly boundaries**.

### Concern 1: dual DI idioms (C4)
Recommended strangler plan, consistent with `REMAINING_WORK.md`'s "service-provider shim" note:

1. **Introduce `IServiceRegistry`** (or adopt `Microsoft.Extensions.DependencyInjection` —
  zero extra dependency weight on .NET 8) and have `AppServices.Initialize()` populate it.
  `AppServices` static properties become thin delegating reads. No consumer changes yet; zero
  behavioral risk.
2. **Freeze the locator:** new code may not reference `AppServices.*` (analyzer banned-symbol rule
  with a grandfather list of the existing 32 files).
3. **Migrate per page:** each page's ViewModel already takes constructor injection — construct them
  from the registry in one factory location, removing `AppServices` reads from that feature.
  One page per session keeps regression risk near zero.
4. **Endgame:** `AppServices` shrinks to lifecycle orchestration (start order, shutdown flush),
  which is its legitimate remaining job.

### Concern 2: no library boundary (test linking is the symptom)
`Lucid.Tests` links 57 production `.cs` files by path because referencing the WinUI exe drags in
packaging targets. The professional fix is **`Lucid.Core`**: a plain `net8.0-windows` class library
holding pure services (Cleanup, Automation models, Persistence, Trust, Intelligence rules — anything
with no WinUI dependency). App and Tests both reference it; file-linking disappears; the csproj
whitelist problem also shrinks because the exe project gets smaller. Do this *after* the whitelist
removal (C5) — moving files is cheap once globbing is default.

### Concern 3: executor governance
28 executors follow `IActionExecutor` with dry-run/rollback — good. Missing: the roadmap's
*Execution Priority Queue* (foreground/background/idle-only classes). Until built, the
CLAUDE.md rule ("avoid concurrent heavy operations") is enforced only by convention. Keep it on the
product roadmap (Phase 1/2); this document just notes no code-level guard exists today.

### Concern 4: native boundary is silently optional
The csproj copies `lucid_scanner.dll` if present and logs "skipping" if not. Production builds must
fail loudly: make the copy step `Error` severity for `Release`/publish configurations, and add a
Rust build+test job to CI that publishes the DLL artifact consumed by the publish job.

---

## Tooling, Formatting, and Standards

| Area | Current | Recommendation |
|---|---|---|
| Line endings | No `.gitattributes`; active churn | Add `.gitattributes`; renormalize once (P0) |
| Editor config | None | `.editorconfig` encoding/indent/whitespace + C# naming rules |
| Formatting | Manual consistency (good but unenforced) | `dotnet format --verify-no-changes` in CI (warning-level first) |
| Analyzers | None beyond defaults | Enable .NET analyzers (`AnalysisLevel=latest`, `EnforceCodeStyleInBuild=true`); add `BannedApiAnalyzers` for `Console.WriteLine`, `Debug.WriteLine`, and (post-migration) `AppServices` |
| Warnings | 0 today | Set `TreatWarningsAsErrors=true` in `Directory.Build.props` *now*, while count is zero — cheapest moment to lock it in |
| Package versions | Inline in csproj ×2 | `Directory.Packages.props` central package management (two projects already share Microsoft.Data.Sqlite) |
| Test TFM mismatch | App `…19041.0`, Tests `…22621.0` | Align (or document why tests target newer SDK) |
| Rust | No fmt/clippy config, no CI | `cargo fmt --check` + `cargo clippy -D warnings` + `cargo test` job |
| Commit hooks | None | Optional: lightweight pre-commit running `dotnet format` on staged files — skip if it adds friction |
| CI duplication | 4 jobs repeat restore/validate verbatim | Composite action or `workflow_call`; add `concurrency:` group with cancel-in-progress; add NuGet caching (`actions/setup-dotnet` cache) |

---

## Documentation Review

### Exists and is good
`README.md` (honest, verified-on date, doctrine summary), `ROADMAP.md` (strategy), `docs/architecture.md`,
`security-model.md`, `ui-guidelines.md`, `release-packaging.md`, release checklists, `ONBOARDING.md`,
`PROJECT_INTEGRITY.md`.

### Problems and fixes
1. **Triplicated agent instructions** (`CLAUDE.md` / `AGENTS.md` / `CODEX.md`, ~14 KB each).
   Keep `CLAUDE.md` (or a tool-neutral `AGENT_INSTRUCTIONS.md`) as the single source; reduce the
   other two to a one-paragraph pointer. Drift between them is otherwise inevitable.
2. **Stale state snapshots.** `CURRENT_STATE.md` counts are wrong within days of writing. Either
   delete (git + CI are the live state) or generate the counts from a script so they cannot rot.
   `REMAINING_WORK.md` content is good — fold it into this roadmap's checklist and retire the file.
3. **`docs/reports/`** — move to `docs/history/` with dates in filenames, or delete.
   `NEW_ROADMAP.md` in particular must not coexist with `ROADMAP.md` unmarked.
4. **Delete `docs/Structure.txt` and `docs/active-file-inventory.md`** — stale by construction.
5. **Missing:** `LICENSE` (decide: proprietary notice or OSS license — currently legally ambiguous),
   `CHANGELOG.md` (the squash-commit history makes this *more* important, since git log carries less
   narrative), `scripts/README.md` (17 scripts, no index of when each runs), environment/setup
   documentation of the `XamlPreCompile` + `build_vs.bat` quirk *inside the repo* (currently only in
   CLAUDE.md, and `build_vs.bat` lives outside the repo at `C:\Users\tyler\` — move it into `scripts/`).
6. **README:** add a Quick Start (clone → setup.ps1 → build → run), prerequisites (VS components,
   Rust toolchain), and link map of the doc set.

---

## Testing Review

### Current status
- 39 test files / 126 test methods, organized by domain (Cleanup, Execution, Infrastructure,
  Interaction, LlmChat, Persistence, Privacy, Trust) — good structure, real assertions, Moq +
  FluentAssertions. CI runs Debug and Release with coverage collection.
- ~25% of test files are untracked (C1).
- No coverage threshold or report rendering; Cobertura XML is uploaded then ignored.
- Rust: zero tests.
- No integration tests of the SQLite persistence layer against real files beyond unit scope; no
  UI/smoke automation (manual smoke checklist exists in `docs/releases/`).

### Improvement plan (ordered)
1. **Commit the untracked tests** (P0, part of C1).
2. **Executor safety contract suite** (P1): a parameterized test over all 28 executors asserting the
   doctrine invariants — dry-run never mutates; destructive executors declare rollback metadata or
   are explicitly classified non-rollbackable; rollback after execute restores state (where
   testable); metadata contract validation passes. This is the highest-value test investment in the
   repo because it mechanizes the safety doctrine.
3. **Persistence durability tests** (P1): queue overflow, flush-on-shutdown, corrupt-DB recovery
   (`REMAINING_WORK.md` already names these).
4. **Rust tests + CI job** (P1): unit tests for path handling, long-path (`\\?\`) behavior, junction/
   symlink cycles, and the FFI surface (null/invalid UTF-16 inputs must not panic across the
   boundary — a panic across FFI is UB). Add `cargo test`/`clippy`/`fmt` to CI.
5. **Coverage visibility** (P2): publish coverage summary to the job summary; set a *soft* floor
   (e.g. fail under 30%, ratchet upward) rather than an aspirational number that gets ignored.
6. **Smoke automation** (P3): script the existing manual smoke checklist steps that are scriptable
   (app launches, DB created, no first-chance exceptions in 60 s) and run post-publish in CI.

---

## Security and Reliability Review

- **Secrets:** none found in source or scripts. Keep it that way with a CI secret-scan step
  (e.g. gitleaks) — cheap insurance.
- **Silent failure handling:** 48 empty catches (C7) is the main reliability gap.
- **Input validation at trust boundaries:** executors operate on filesystem/registry/process
  surfaces; the executor contract validation commit is the right direction. Extend contract tests
  to hostile inputs (paths with `..`, reparse points, denied ACLs).
- **FFI boundary:** 22 `unsafe` sites in Rust are appropriately concentrated (P/Invoke + Win32),
  but untested. `AllowUnsafeBlocks` on the C# side is scoped to the P/Invoke wrapper — good.
  Priority: tests proving no panic crosses FFI (see Testing #4).
- **LLM surface:** `OllamaClient` implies a local endpoint; `Trust/EndpointValidation` exists.
  Ensure the local-only guarantee is tested for every network-capable service
  (`REMAINING_WORK.md` names this; keep it P1) — it is a *product promise*, not an implementation detail.
- **Release signing:** `sign-release-artifact.ps1` exists; verify it fails closed (no cert →
  pipeline fails, not "skipped") once scripts are committed and reviewable in CI.
- **Update feed:** `generate/verify-release-update-feed.ps1` imply a self-update channel — this is
  the highest-consequence security surface in the project (update = arbitrary code). Document the
  trust model in `docs/security-model.md` (key custody, feed integrity, downgrade protection) and
  add `verify-release-update-feed.ps1` as a release-blocking CI gate.
- **Observability:** `IOperationalLogger` with correlation context is solid; finish routing all
  diagnostics through it (C7) and confirm log files are included in the support-bundle export.

---

## Dependency Review

| Package | Version | Status |
|---|---|---|
| Microsoft.WindowsAppSDK | 1.5.240802000 | Behind current (1.6/1.7 line). Major-version upgrade correctly deferred in `REMAINING_WORK.md`; schedule as an isolated pass with smoke testing |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.1742 | Fine |
| CommunityToolkit.Mvvm | 8.2.2 | Minor updates available; low risk |
| Microsoft.Data.Sqlite | 8.0.0 | Patch updates available (8.0.x); take patches, defer 9.x/10.x with the .NET upgrade |
| System.* (PerformanceCounter, Management, ServiceController) | 8.0.0 | Aligned with TFM; fine |
| xunit / runner / coverlet / FluentAssertions / Moq | current-ish | Note: FluentAssertions 7+ changed licensing — staying on 6.12.1 is a *reasonable deliberate choice*; document it |
| windows-sys (Rust) | 0.59 | Fine; pin via committed `Cargo.lock` (already committed — good) |

Hygiene: no unused dependencies detected; dev-only packages correctly `PrivateAssets=all`.
Recommended additions: central package management file; Dependabot/Renovate config scoped to
patch/minor only, so upgrades become visible PRs instead of background drift.

---

## Step-by-Step Professionalization Plan

### Phase 1: Stabilize the Repository (P0 — one session)
**Goal:** A fresh clone of `main` builds, tests, and runs CI green; no work exists only on one machine.

Steps:
1. Add `.gitattributes` + `.editorconfig`; `git add --renormalize .`; commit the normalization alone.
2. Add `release/` to `.gitignore`. Commit all untracked source: 14 scripts, `installer/`,
   `Directory.Build.props`, 10 test files + `TestInfrastructure/` + `LlmChat/`, 4 docs.
3. Push, confirm all four CI jobs pass from clean checkout; fix anything CI exposes.
4. Tag the result (e.g. `v0.1-hygiene-baseline`).

Expected outcome: repository state is trustworthy; diffs become reviewable; CI is honest.

### Phase 2: Clean and Reorganize (P0/P2 — one session)
**Goal:** The tree contains only living code and current documentation.

Steps:
1. Tag then delete `_archive/`; delete the 9 compile-excluded orphan files.
2. Remove the csproj whitelist (5 `Compile Remove` + 481 `Compile Include`); return to default
   globs; retire `check-app-source-includes.ps1` from CI and `scripts/`.
3. Consolidate docs: single agent-instruction source; retire `CURRENT_STATE.md`/`REMAINING_WORK.md`
   into this roadmap; move `docs/reports/` → `docs/history/`; delete stale inventories; add
   `LICENSE`, `CHANGELOG.md`, `scripts/README.md`; move `build_vs.bat` into `scripts/`.
4. Move the 5 loose `Services/` root files into `Services/Telemetry/`.

Expected outcome: navigable repo where everything present is real; csproj merge conflicts end.

### Phase 3: Improve Code Quality (P1 — one to two sessions)
**Goal:** Failures are visible, standards are machine-enforced, composition debt has a ratchet.

Steps:
1. Enable analyzers + `TreatWarningsAsErrors` + `dotnet format` CI check; add banned-API rules for
   `Debug/Console.WriteLine`.
2. Sweep 48 empty catches → logger events or justified comments; replace the 33 debug prints;
   audit 12 `async void` and 9 `.Result/.Wait()`.
3. AppServices strangler steps 1–2: introduce registry shim behind the statics; ban new
   `AppServices.*` references (grandfathered list); migrate one page end-to-end as the template.
4. Refactor `CompanionOverlayWindow.xaml.cs` (879 lines) into VM + interop helper as the
   code-behind exemplar.

Expected outcome: zero silent failures policy is enforced, not aspirational; DI direction is fixed
with a mechanical ratchet instead of a rewrite.

### Phase 4: Improve Testing and Reliability (P1 — one to two sessions)
**Goal:** The safety doctrine is mechanically verified.

Steps:
1. Build the executor safety contract suite over all 28 executors (dry-run purity, rollback
   metadata, hostile-path inputs).
2. Add persistence durability tests (overflow, shutdown flush, corrupt DB recovery).
3. Add Rust unit tests + `cargo test/clippy/fmt` CI job; make the missing-DLL copy step a hard
   error for Release; publish the DLL as a CI artifact consumed by the publish job.
4. Add local-only endpoint enforcement tests for every network-capable service.
5. Surface coverage in CI job summary with a ratcheting floor.

Expected outcome: destructive operations and the native boundary are provably safe; coverage trends
are visible and enforced.

### Phase 5: Production and Collaboration Readiness (P2/P3 — one session)
**Goal:** Release and onboarding are repeatable by someone who is not you.

Steps:
1. Extract `Lucid.Core` class library; replace the 57 linked test files with a project reference.
2. DRY the CI workflow (composite action), add concurrency cancellation + NuGet caching + secret
   scanning; wire `verify-release-update-feed.ps1` as a release-blocking gate.
3. Document the update-channel trust model in `docs/security-model.md`; verify signing fails closed.
4. README quick-start + prerequisites; `CHANGELOG.md` seeded from milestone tags; clean stale
   branches (`feature/phase-3`, worktree branches, `origin/master`).
5. Optional: script the manual smoke checklist post-publish.

Expected outcome: a new collaborator (human or agent) is productive from README alone; releases are
gated, signed, and reproducible.

---

## Priority Checklist

- [ ] Add `.gitattributes` and renormalize line endings
  - Priority: P0
  - Files/folders: repo root, all text files
  - Reason: 613-file diff noise destroys reviewability and blame
  - Acceptance criteria: `git status` clean after fresh clone on Windows and Linux; diff of normalization commit contains no logic changes
- [ ] Commit all untracked source (scripts, installer, tests, docs, Directory.Build.props)
  - Priority: P0
  - Files/folders: `scripts/`, `installer/`, `lucid-desktop/Lucid.Tests/`, `docs/`, root
  - Reason: CI invokes untracked scripts; ~25% of tests exist on one machine only
  - Acceptance criteria: fresh clone passes all CI jobs; `git status --short` shows nothing unexpected
- [ ] Ignore `release/` output
  - Priority: P0
  - Files/folders: `.gitignore`
  - Reason: 740 MB of artifacts one `git add .` away from history bloat
  - Acceptance criteria: `git status` ignores `release/`; `git check-ignore release/packages` passes
- [ ] Remove csproj compile whitelist and delete 9 orphan files
  - Priority: P1
  - Files/folders: `Lucid.App.csproj`, `Controls/`, `Models/`, `ViewModels/`, `Services/MockTelemetryService.cs`
  - Reason: 633-line csproj is the top merge-conflict and friction source; original rationale expired
  - Acceptance criteria: csproj < 150 lines; build output identical (binary diff of file list); guard script retired
- [ ] Sweep empty catches and debug prints into `IOperationalLogger`
  - Priority: P1
  - Files/folders: 48 + 33 sites across `Services/`
  - Reason: explainability doctrine requires the platform not to hide its own failures
  - Acceptance criteria: zero unjustified `catch { }`; banned-API analyzer blocks `Debug/Console.WriteLine`; build green
- [ ] AppServices strangler: shim + freeze + first migrated page
  - Priority: P1
  - Files/folders: `AppServices.cs`, one page's VM/View, analyzer config
  - Reason: dual DI idioms; 2,052-line static locator blocks testability
  - Acceptance criteria: no new `AppServices.*` references possible (analyzer); one page fully constructor-injected; app behavior unchanged
- [ ] Executor safety contract test suite
  - Priority: P1
  - Files/folders: `Lucid.Tests/Execution/`, all 28 executors
  - Reason: rollback/dry-run is the product's core promise; currently partially tested
  - Acceptance criteria: parameterized suite enumerates every `IActionExecutor`; dry-run purity and rollback metadata asserted for all
- [ ] Rust tests + Rust CI job + hard-fail DLL copy in Release
  - Priority: P1
  - Files/folders: `lucid-native/`, `.github/workflows/`, `Lucid.App.csproj` copy target
  - Reason: zero native coverage; broken native build currently invisible
  - Acceptance criteria: `cargo test/clippy/fmt` green in CI; Release build fails without `lucid_scanner.dll`
- [ ] Enable analyzers, `TreatWarningsAsErrors`, `dotnet format` gate
  - Priority: P1
  - Files/folders: `Directory.Build.props`, `.editorconfig`, CI workflow
  - Reason: standards currently enforced by discipline alone; warning count is zero today (cheapest moment)
  - Acceptance criteria: CI fails on new warnings or formatting drift
- [ ] Consolidate agent docs; retire stale state docs; add LICENSE + CHANGELOG
  - Priority: P2
  - Files/folders: root `.md` files, `docs/reports/`
  - Reason: drift between triplicated/stale docs misleads humans and agents
  - Acceptance criteria: one instruction source; no doc contradicts `git`/CI-derivable facts; LICENSE present
- [ ] Delete `_archive/` (after tagging)
  - Priority: P2
  - Files/folders: `_archive/` (39 tracked files)
  - Reason: git history is the archive; tracked archives rot and pollute repo-wide operations
  - Acceptance criteria: tag exists; tree contains no archived code
- [ ] Extract `Lucid.Core`; replace 57 linked test files with project reference
  - Priority: P2
  - Files/folders: `lucid-desktop/`, `Lucid.Tests.csproj`
  - Reason: file-linking is a scaling dead end; library boundary improves testability and build time
  - Acceptance criteria: zero `<Compile Include>` links in test project; tests green
- [ ] CI DRY + caching + concurrency + secret scan; update-feed verification as release gate
  - Priority: P2
  - Files/folders: `.github/workflows/`
  - Reason: 4× duplicated steps; update channel is highest-consequence security surface
  - Acceptance criteria: single source of build steps; release job blocked on feed verification
- [ ] Central package management + Dependabot (patch/minor)
  - Priority: P3
  - Files/folders: `Directory.Packages.props`, `.github/dependabot.yml`
  - Reason: version drift between projects; upgrades should be visible PRs
  - Acceptance criteria: no versions in csproj files; bot PRs scoped to patch/minor

---

## Recommended Target Structure

See **File and Folder Structure Review → Recommended target structure** above. Headline changes:
`release/` ignored, `installer/` + `scripts/` fully tracked with README index, `_archive/` removed,
docs consolidated with a `docs/history/` for frozen reports, `Lucid.Core` library introduced,
default compile globbing restored.

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

---

## Final Notes

- **Sequencing matters:** Phase 1 (commit + normalize) must precede everything; mixing the
  renormalization with functional changes would permanently obscure the diff. Do it as two clean
  commits and the entire problem disappears in under an hour.
- **The csproj whitelist and the AppServices locator are connected debts** — both are "manual
  registry" patterns. Removing the first is mechanical; the second needs the strangler ratchet.
  Resist any suggestion to big-bang either one.
- **Assumption:** `git push` access and CI on GitHub work as configured; this audit ran against the
  local working tree on 2026-06-10. Counts (481 includes, 48 catches, 126 tests, 740 MB) were
  measured, not estimated, but will drift — treat them as audit-date snapshots.
- **Out of scope here, on `ROADMAP.md`:** Execution Priority Queue, Explain My PC flagship,
  forecasting, security intelligence phases. Nothing in this document changes product priorities;
  it clears the ground they will be built on.
- **Per repository policy:** none of the changes above have been made. Nothing was committed,
  no branches created. Every step in this roadmap awaits your explicit go-ahead.
