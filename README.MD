# Lucid

Lucid is a local-first operational intelligence platform for Windows.

It helps a user understand what their PC is doing, why conditions changed, what evidence supports that interpretation, and what can be safely done next. The product direction is diagnostic, explanatory, and reversible. It is not a PC booster, registry cleaner, antivirus replacement, or cloud telemetry product.

Current status: the WinUI application builds, the focused unit test suite passes, and the native Rust scanner workspace exists. The project is not yet production-release ready. The immediate work is professional hardening: repository hygiene, repeatable setup, stronger tests, packaging, diagnostics, safety coverage, and architecture cleanup.

Verified on 2026-06-06:

```powershell
cd lucid-desktop
dotnet build Lucid.slnx -c Debug -p:Platform=x64 --no-restore
dotnet test Lucid.Tests\Lucid.Tests.csproj -c Debug -p:Platform=x64 --no-restore

cd ..\lucid-native
cargo test
```

Results from this audit:

- `dotnet build`: passed, 0 warnings, 0 errors.
- `dotnet test`: passed, 53 tests.
- `cargo test`: passed, 0 Rust tests currently exist.

## Product Doctrine

Lucid has four non-negotiable product commitments:

- **Explainability:** Findings must explain the evidence and reasoning behind them.
- **Confidence awareness:** The app must avoid absolute security or performance claims.
- **Reversibility:** Destructive operations require consent, audit logs, and rollback planning.
- **Lightweight operation:** Lucid must not become the reason the machine feels slow.

Preferred language is calm and evidence-grounded: unusual, unexpected, worth reviewing, flagged for inspection, confidence, severity, evidence, impact, rollback, and next step.

Avoid fear-based security copy, mystery optimization claims, and hidden remediation.

## Current Architecture

```text
Lucid/
  lucid-desktop/
    Lucid.slnx
    Lucid.App/        WinUI 3 desktop application
    Lucid.Tests/      xUnit tests for pure service code
  lucid-native/
    Cargo.toml
    lucid-scanner/    Rust native scanner exported through C ABI
  docs/               Architecture and feature documentation
  _archive/           Historical scaffold/orphaned code retained for reference
```

Primary technologies:

- WinUI 3 / Windows App SDK
- C# / .NET 8
- MVVM with CommunityToolkit.Mvvm
- SQLite through Microsoft.Data.Sqlite
- Rust native module for performance-sensitive scanning
- xUnit, FluentAssertions, Moq, and cargo test

## Important Build Notes

Always pass a concrete platform:

```powershell
cd lucid-desktop
dotnet build Lucid.slnx -c Debug -p:Platform=x64
```

`WindowsAppSDKSelfContained=true` does not support AnyCPU. Builds without `-p:Platform=x64` can fail.

If a clean build loses the WinUI XAML intermediate output and `XamlCompiler.exe` fails, run the Visual Studio MSBuild reset script:

```powershell
C:\Users\tyler\build_vs.bat
```

That script regenerates the XAML precompile output used by later CLI builds.

## Development Rules

Before changing code, read:

- [PROJECT_INTEGRITY.md](PROJECT_INTEGRITY.md)
- [ROADMAP.md](ROADMAP.md)
- [ONBOARDING.md](ONBOARDING.md)

Core rules:

- Read real files before making architectural claims.
- Prefer targeted, reversible changes over broad rewrites.
- Keep UI logic in ViewModels and service logic in services.
- Keep security and operational language confidence-aware.
- Add tests for safety, persistence, executor, trust, and parsing logic.
- Do not launch multiple heavy operations without governance.
- Do not delete tracked files, archived context, or generated cleanup targets without confirming scope.

## Production-Readiness Gaps

This repo is substantial, but production hardening is still required.

Known work:

- Remove tracked/generated IDE state from source control and keep `.vs/` ignored.
- Normalize canonical documentation casing to `README.md` and `ROADMAP.md`.
- Repair stale setup paths that still reference the old `ExplainMyPC` location.
- Add packaging and release infrastructure; there is no active `Package.appxmanifest` in `Lucid.App`.
- Expand unit and integration coverage well beyond the current focused 53-test suite.
- Add Rust scanner tests before expanding native filesystem work.
- Reduce `Lucid.App.csproj` selective-compilation fragility.
- Incrementally reduce pressure on `AppServices.cs` without a big-bang DI rewrite.
- Convert silent best-effort failures into structured diagnostics events.
- Audit every executor for consent, rollback, privilege, governance, and logging behavior.

See [ROADMAP.md](ROADMAP.md) for the full cleanup and production-hardening plan.

## Repository Hygiene

The current worktree already contains modified files and staged deletions from previous work. Treat cleanup as a controlled pass:

- Review `git status --short` before editing.
- Do not revert unrelated user changes.
- Do not delete archive folders unless the roadmap explicitly calls for archiving or removal and the owner approves.
- Prefer documentation and audit reports before structural moves.

## Further Reading

- [ONBOARDING.md](ONBOARDING.md): developer orientation and build workflow.
- [PROJECT_INTEGRITY.md](PROJECT_INTEGRITY.md): mandatory quality gate.
- [ROADMAP.md](ROADMAP.md): production-hardening roadmap.
- [docs/architecture.md](docs/architecture.md): architecture notes, pending cleanup.
- [docs/intelligence-engine.md](docs/intelligence-engine.md): intelligence layer notes.
- [docs/security-model.md](docs/security-model.md): security model notes.
