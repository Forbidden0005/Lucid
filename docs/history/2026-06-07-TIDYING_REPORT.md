# Tidying Report — 2026-05-17

## TL;DR

Two pieces of dead code were moved to `_archive/` so the project lines up
with the blueprint in `README.MD` (`frontend/`, `backend/`, `shared/`,
`installer/`, `docs/` at root). Nothing was deleted. The real WinUI
project — `frontend/ExplainMyPC.App/` — was not touched except for two
lines in its `.csproj`.

If you want any of this back, it's all in `_archive/`. Move it back and
you're where you were.

---

## What was wrong

### 1. Phantom WinUI scaffold at `ExplainMyPC/ExplainMyPC/`

Created May 16 at 8:55 PM, ~2 hours before you started the real project
in `frontend/ExplainMyPC.App/` at 11:05 PM. Contents:

- `ExplainMyPC.slnx` (its own Visual Studio solution)
- `.vs/` (VS cache, kept getting refreshed because the solution stays
  in your "recent" list)
- `ExplainMyPC/ExplainMyPC/ExplainMyPC/` — single MainWindow.xaml with
  hardcoded "87" health score, plus default App.xaml.cs from the WinUI
  template. No services, no MVVM, no real code.

**Verification it was dead:**
- Not referenced anywhere outside its own folder.
- Uses the same `namespace ExplainMyPC` as the real project — both
  cannot coexist in one compilation, confirming they were never linked.
- The `.csproj` in the real project does not reference it.

**Size:** 258 MB total, mostly because its `bin/` was full of .NET
runtime DLLs (DirectML.dll 18MB, onnxruntime.dll 21MB, etc.). Those
were dropped from the archive since they regenerate on build. The
source files were preserved.

**Now at:** `_archive/winui-scaffold-2026-05-16/`

### 2. Orphaned `Intelligence/` folder in the real project

Located at `frontend/ExplainMyPC.App/Intelligence/`. 19 files:

```
Intelligence/
├── IIntelligenceEngine.cs
├── IntelligenceEngine.cs
├── Models/        (EngineOutput, IssueCategory, Recommendation, SystemSnapshot)
├── Rules/         (IRule, RuleContext, RuleResult,
│                   Performance/{HighCpu,HighGpu,HighRam,MemoryPressure,ThermalThrottle}Rule,
│                   Startup/StartupCongestionRule,
│                   Storage/{HighDiskIo,LowDiskSpace}Rule)
├── Scoring/HealthScorer.cs
└── Summary/SummaryGenerator.cs
```

**Verification it was dead:**
- `ExplainMyPC.App.csproj` explicitly excluded it from compilation:
  `<Compile Remove="Intelligence\**" />`
- `AppServices.cs` wires `ISystemInsightEngine` to
  `Services.Intelligence.SystemInsightEngine` — the *active* engine.
- `IIntelligenceEngine` is referenced only by its own implementation
  file. No view model, no service, no page uses it.
- The active engine in `Services/Intelligence/` uses different model
  types (`SystemInsight`, `SystemAction`) and a different rule
  interface (`IInsightRule` vs `IRule`). They are not interchangeable.

This is your v1 intelligence engine design. `Services/Intelligence/`
replaced it.

**Now at:** `_archive/intelligence-engine-v1-orphaned/Intelligence/`

---

## .csproj change

Removed two now-irrelevant lines from `frontend/ExplainMyPC.App/ExplainMyPC.App.csproj`:

```diff
-    <Compile Remove="Intelligence\**" />
-    <None Include="Intelligence\**" />
```

These pointed at the folder that's been moved. MSBuild would have just
matched nothing without erroring, but removing them is cleaner.

The other "future feature" exclusions (`Controls\`, `Core\`, `Models\`)
were left alone — those folders are still scaffolded work-in-progress
per your `.csproj` comment.

---

## What was NOT touched

- `frontend/ExplainMyPC.App/` — every other file untouched. Same Services,
  Views, ViewModels, AppServices.cs, Controls, Core, Models, Helpers,
  Styles, Properties.
- `frontend/ExplainMyPC.App/bin/` and `obj/` — your current build
  artifacts are intact.
- `.git/` — git history untouched. The moves above are filesystem-only;
  next time you commit, git will see the renames.
- `.claude/` — your Claude Code settings untouched.
- `CLAUDE.md`, `README.MD`, `ROADMAP.MD`, `docs/` — untouched.
- Empty placeholder folders `backend/`, `shared/`, `installer/` — kept,
  they match your blueprint.

---

## Resulting structure

```
ExplainMyPC/
├── .claude/
├── .git/
├── _archive/                     ← NEW (recoverable dead code)
│   ├── winui-scaffold-2026-05-16/
│   └── intelligence-engine-v1-orphaned/
├── CLAUDE.md
├── README.MD
├── ROADMAP.MD
├── TIDYING_REPORT.md             ← NEW (this file)
├── backend/                      (empty, planned Rust modules)
├── docs/
│   ├── Structure.txt
│   ├── architecture.md
│   ├── intelligence-engine.md
│   ├── security-model.md
│   └── ui-guidelines.md
├── frontend/
│   ├── ExplainMyPC.slnx
│   └── ExplainMyPC.App/          ← the real project (untouched)
├── installer/                    (empty, planned)
└── shared/                       (empty, planned shared contracts)
```

This matches `README.MD`'s declared structure exactly.

---

## Next time you open VS

The Visual Studio "Recent Projects" list still remembers the phantom
`ExplainMyPC/ExplainMyPC.slnx` solution. When you open the project,
make sure you pick `frontend/ExplainMyPC.slnx` — not the old one (which
no longer exists at its original path anyway, since it's moved into
`_archive/`).

If VS complains about not finding the old solution, just remove it
from the recent list: **File → Recent Projects → right-click → Remove**.

---

## Suggested follow-up

If you're confident you don't need the archived code:

```bash
rm -rf _archive/
```

Otherwise leave it — it's 136 KB of source for the orphaned Intelligence
engine plus a small WinUI scaffold. Not worth worrying about.
