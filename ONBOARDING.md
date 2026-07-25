# Lucid Onboarding

This guide is for any engineer or agent entering the repo cold. Read it before changing code.

Lucid is a local-first operational intelligence platform for Windows. Its job is to explain system behavior with evidence, confidence, and reversible next steps. It is not a PC booster, registry cleaner, antivirus replacement, or cloud telemetry service.

## First Files To Read

Read these in order:

1. `PROJECT_INTEGRITY.md`
2. `README.md`
3. `ROADMAP.md`
4. `.github/workflows/lucid-build.yml`
5. `lucid-desktop/Lucid.App/Lucid.App.csproj`
6. `lucid-desktop/Lucid.App/AppServices.cs`

Why this order matters:

- `PROJECT_INTEGRITY.md` defines the quality gate.
- `README.md` gives the current verified baseline.
- `ROADMAP.md` separates built capability from production-readiness work.
- The workflow and project files show what actually builds.
- `AppServices.cs` shows how the application is wired today.

## Current Verified State

Verified on 2026-06-06:

```powershell
cd lucid-desktop
dotnet build Lucid.slnx -c Debug -p:Platform=x64 --no-restore
dotnet test Lucid.Tests\Lucid.Tests.csproj -c Debug -p:Platform=x64 --no-restore

cd ..\lucid-native
cargo test
```

Results:

- WinUI build passed with 0 warnings and 0 errors.
- xUnit passed 53 tests.
- Rust test command passed but currently runs 0 tests.

## Environment

Expected tools:

- Windows 10 19041+ or Windows 11.
- .NET 8 SDK.
- Visual Studio 2022 Build Tools with WinUI / Windows App SDK build support.
- Windows App SDK runtime 1.5.
- Rust toolchain for `lucid-native`.
- Git.
- Optional: Ollama for local-only chat features.

Always build with a platform:

```powershell
cd lucid-desktop
dotnet build Lucid.slnx -c Debug -p:Platform=x64
```

Without `-p:Platform=x64`, Windows App SDK self-contained builds can fail because AnyCPU is not supported.

If XAML compilation fails after a clean, run:

```powershell
C:\Users\tyler\build_vs.bat
```

The WinUI XAML precompile step is supplied by Visual Studio MSBuild targets, not the plain .NET SDK path.

## Repository Layout

```text
Lucid/
  README.md
  ONBOARDING.md
  PROJECT_INTEGRITY.md
  ROADMAP.md
  .github/workflows/lucid-build.yml
  docs/
  lucid-desktop/
    Lucid.slnx
    Lucid.App/
      AppServices.cs
      MainWindow.xaml
      Services/
      ViewModels/
      Views/
      Themes/
      Controls/
    Lucid.Tests/
  lucid-native/
    Cargo.toml
    lucid-scanner/
  _archive/
```

`_archive/` is historical reference material. Do not treat it as active source unless the roadmap or owner explicitly says to revive something.

## Architecture Notes

### Service Registration

The app currently uses `AppServices.cs` as a static service registry. It is not a DI-container app today.

This is a known scaling pressure point, but do not replace it in one broad rewrite. Add seams incrementally around modules being hardened, and keep each migration testable.

### Project File Inclusion

`Lucid.App.csproj` uses broad compile removals plus explicit `<Compile Include>` entries. A new `.cs` file can exist in the tree and still not compile.

When adding C# files under active app folders:

- Add the file to the correct `<Compile Include>` section.
- Update the matching `<None Include ... Exclude="...">` list if needed.
- Build the app after adding the file.
- Prefer adding a guard script/test that detects active files excluded from compilation.

### Tests

`Lucid.Tests` references the `Lucid.Core` class library, which holds the pure (WinUI-free)
production services. To put a new production file under test, move it into `Lucid.Core`
(it must not depend on WinUI, `AppServices`, or Views/ViewModels) — do not file-link it
into the test project; the old per-file `<Compile Include>` links are gone.

Reason: a direct project reference to `Lucid.App` pulls WinUI and Windows App SDK
packaging/resource targets into unit tests. `Lucid.Core` is the library boundary that
avoids that while keeping tests on real production code.

### Native Boundary

`lucid-native/lucid-scanner` exposes C-compatible exports for C# P/Invoke. The native module exists and builds, but has no test coverage yet. Add Rust tests before expanding native scanning.

### Local LLM Boundary

Ollama-related features must remain optional and local-only. The app must not depend on a remote model or external API for core diagnostics.

## Working Rules

Before editing:

- Check `git status --short`.
- Assume existing modifications belong to the user unless proven otherwise.
- Do not revert unrelated changes.
- Do not delete tracked files or archives without approval.
- Read the actual files before asserting what the system does.

When editing:

- Keep changes targeted.
- Preserve backward compatibility unless intentionally changing a contract.
- Prefer deterministic services and tests over clever runtime behavior.
- Make failures visible through diagnostics when they affect trust, data integrity, or user action.
- Classify heavy work as foreground, background, or idle-only.

Before finishing:

- Run the narrowest meaningful verification.
- State exactly what passed and what was not run.
- Call out uncertainty instead of hiding it.

## Product Language

Lucid must use calm, confidence-aware language.

Good patterns:

- "This is unusual for the current baseline."
- "This is worth reviewing because it starts with Windows and uses elevated resources."
- "Confidence is limited because the sample window is short."
- "This action can be reversed by restoring the recorded startup state."

Bad patterns:

- Fear-based security claims.
- Absolute certainty about process intent.
- Mystery optimization promises.
- Action copy that hides what will change.

## Current Highest-Value Work

1. Normalize repository hygiene and remove generated IDE state from Git.
2. Fix setup scripts and path drift.
3. Add packaging/release infrastructure.
4. Expand safety, persistence, trust, and native tests.
5. Add project-file inclusion guards.
6. Harden executor metadata, rollback, consent, and diagnostics.
7. Incrementally reduce `AppServices` pressure.
8. Audit the UI for empty, loading, error, and accessibility states.

Use `ROADMAP.md` as the source of truth for production-hardening order.
