# Session Handoff — Lucid

_Last updated: 2026-05-31_

---

## Real development branch

**`feature/phase-17a-companion-overlay`** is the active trunk — 84 commits ahead of `main`, currently at **Phase 21**.  
`main` only has Phase 17A (merge commit `0741307`). Do not develop against `main`.

The app directory was renamed at commit `4ac72b8`:

```
frontend/     →  lucid-desktop/
backend/      →  lucid-native/
shared/       →  lucid-shared/
```

All source is under `lucid-desktop/Lucid.App/`. Every path in csproj, CI, and service registration uses this prefix.

---

## Phase status

| Phase | Commit | Description |
|-------|--------|-------------|
| 18C | `faf19fb` | Trust & Governance Hardening — LocalEndpointValidator, TrustIntegrityService, ProcessIdentityValidator, WriteQueueMetrics/PersistenceHealthMonitor, StartupTimeoutGuard, GovernanceDiagnosticsService, xUnit test project (37 tests), GitHub Actions CI |
| 19 | `86e3e54` | Unified Cognitive Reasoning Layer |
| 19-gap | `2feeaf0` | Evidence attribution, cognitive diagnostics, governance-aware reasoning |
| 20 | `46936ec` | Unified Human Interaction & Cognitive UX Layer |
| **21** | **`3bce694`** | **Adaptive Operational Learning & Pattern Intelligence — current tip** |

---

## csproj — critical pattern

Wildcard includes are disabled. Every `.cs` file needs **two** entries:

```xml
<!-- 1. Compile it -->
<Compile Include="Services\Path\To\NewFile.cs" />

<!-- 2. Add its path to the Exclude list on the None Include line -->
<None Include="Services\**" Exclude="...;Services\Path\To\NewFile.cs;..." />
```

The `<None Include Exclude=...>` line is extremely long. Use Python `re.sub` to edit it — the Edit tool's exact-match fails on lines this large.

---

## Build

### CI workflow — `.github/workflows/lucid-build.yml`
- Runs on `windows-latest`
- `working-directory: lucid-desktop`
- `dotnet restore Lucid.slnx -p:Platform=x64` → `dotnet build Lucid.App/Lucid.App.csproj -c Debug -p:Platform=x64`
- Triggers: push to `main`/`phase-18c/**`/`feature/**`; PR targeting `main`
- **Important**: the intermediate XAML DLL (`obj/x64/Debug/.../intermediatexaml/Lucid.App.dll`) is committed to the repo. `dotnet build` on CI reuses it — without it the build fails because `XamlPreCompile` only runs under VS MSBuild, not the .NET CLI.

### Local (Windows)
```bat
cd C:\Users\tyler\ExplainMyPC\lucid-desktop
dotnet build Lucid.App\Lucid.App.csproj -c Debug -p:Platform=x64
```

---

## Intelligence engine — adding rules

**`IInsightRule`** (base rules, Phase 1):
- Implement `Evaluate(TelemetrySnapshot, ITelemetryHistoryBuffer) → SystemInsight?`
- Register in `SystemInsightEngine.CreateRules()`

**`ISynthesisRule`** (cross-insight patterns, Phase 2):
- Implement `Evaluate(IReadOnlyList<SystemInsight>) → IReadOnlyList<SystemInsight>`
- IDs must start with `"synthesis."`
- Register in `SystemInsightEngine.CreateSynthesisRules()`

Both live in `lucid-desktop/Lucid.App/Services/Intelligence/Rules/`.

---

## AppServices.cs

One large static class (`lucid-desktop/Lucid.App/AppServices.cs`).  
Pattern: `Initialize(DispatcherQueue)` / `Shutdown()`.  
Every service is registered here. Check here first before assuming a service doesn't exist.

---

## Navigation

22 routable pages. All wired in:
- `lucid-desktop/Lucid.App/MainWindow.xaml` — NavigationViewItem entries
- `lucid-desktop/Lucid.App/MainWindow.xaml.cs` — switch/case on route string

---

## User preferences

- **Work locally first** — make all file changes, then one commit + push at the end. No mid-task commits.
- **Local machine**: `C:\Users\tyler\ExplainMyPC` (Windows). Claude Code CLI/desktop app sessions have direct filesystem access here. Remote (web/mobile) sessions only see `/home/user/Lucid/` (the cloned container).
- **Never merge PRs** without explicit user confirmation.
- **Never push to `main`** directly.
- **Never add, remove, or change anything not explicitly asked for.**

---

## Security language (non-negotiable)

Never: "malicious", "infected", "dangerous", "threat detected", absolute certainty claims.  
Always: probabilistic language — "unusual", "unexpected", "worth reviewing", "flagged for inspection", confidence scores, contextual explanations of *why* something looks off.
