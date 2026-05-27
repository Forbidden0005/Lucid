# Lucid — Claude Onboarding Guide

> Hey Claude. Welcome to Lucid. Here's everything you need to hit the ground running — the stuff that took sessions to figure out, handed to you upfront.

---

## What Is Lucid?

Lucid is a **local-first operational intelligence platform** for Windows. It helps users understand what their PC is actually doing — in plain English, with evidence, confidence scores, and no fear-based language.

**The flagship experience:**
> "For the first time, I actually understand my computer."

**What it is NOT:**
- Not a PC cleaner / booster scam
- Not antivirus
- Not cloud-dependent
- Not a mystery optimization black box

**What it IS:**
- A system diagnostics assistant
- An explainable PC health monitor
- A safe repair and optimization toolkit
- A local operational intelligence layer

The repo was originally named `ExplainMyPC` — the GitHub URL and namespaces still reflect that. A full rename to `Lucid` is planned but hasn't happened yet (user-visible strings were updated; `ExplainMyPC` still appears in namespaces, folder names, `.csproj`, `.slnx`).

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | WinUI 3, C#, .NET 8 |
| MVVM | CommunityToolkit.Mvvm 8.2.2 |
| Windows SDK | WindowsAppSDK 1.5.240802000 |
| Database | SQLite via Microsoft.Data.Sqlite 8.0.0 |
| Backend (planned) | Rust native modules |

---

## Build Instructions

**Always build with `-p:Platform=x64`** — the project hard-fails without it.

```bat
cd lucid-desktop
dotnet build Lucid.slnx -c Debug -p:Platform=x64
```

**If you get a XamlCompiler.exe error (MSB3073):**
The intermediate XAML DLL was deleted (e.g. after `dotnet clean`). Run:
```bat
C:\Users\tyler\build_vs.bat
```
This calls VS MSBuild which regenerates the intermediate DLL. After that, `dotnet build` works again.

**Warning `NETSDK1206`** — non-critical, comes from WindowsAppSDK NuGet. Ignore it.

---

## Repository Layout

```
Lucid/
├── CLAUDE.md                          ← Project doctrine (read this too)
├── ROADMAP.md                         ← Full phase-by-phase roadmap
├── lucid-desktop/
│   └── Lucid.App/
│       ├── AppServices.cs             ← Service registry (no DI container)
│       ├── MainWindow.xaml(.cs)       ← App shell, sidebar nav, companion toggle
│       ├── Services/
│       │   ├── Companion/             ← Overlay session + conversation models
│       │   ├── Conversation/          ← Phase 17C conversation engine (9 files)
│       │   ├── DesktopContext/        ← Foreground window + clipboard observation
│       │   ├── Intelligence/          ← 25 insight rules, anomaly detection
│       │   ├── Narrative/             ← Plain-English system summaries
│       │   ├── Timeline/              ← Chronological event aggregation
│       │   ├── Explain/               ← Flagship ExplainMyPC engine
│       │   ├── Reasoning/             ← Evidence graph + root cause analysis
│       │   ├── Workflow/              ← Guided workflow engine
│       │   ├── Replay/                ← Operational replay (time-travel)
│       │   ├── Security/              ← Persistence scanner, Defender status
│       │   ├── Storage/               ← SHA-256 duplicate detection, category analysis
│       │   ├── Governance/            ← Runtime resource governance
│       │   ├── Watchtower/            ← 30-min autonomous analysis cycles
│       │   ├── Remediation/           ← Multi-step remediation workflows
│       │   ├── Simulation/            ← "What if?" scenario modeling
│       │   └── ...                    ← 30+ more service namespaces
│       ├── ViewModels/                ← MVVM ViewModels, one per page/component
│       └── Views/                     ← XAML pages + code-behind
```

---

## What Has Been Built (Phase by Phase)

### Core Platform
- **Telemetry engine** — 6 samplers (CPU, RAM, GPU, Disk, Thermal, Process), 1–2s cadence, 30-min rolling buffer
- **Welford baseline** — machine-specific normal ranges learned over time, cold-start safe
- **25 insight rules** — anomaly + forecast + synthesis, process attribution per finding
- **Narrative engine** — deterministic plain-English summaries from active insight set
- **Session context** — boot time, sleep/wake cycles, idle periods, insight onset times
- **SQLite persistence** — telemetry history, insight history, timeline events, recommendation outcomes

### Operational Tools
- **28 action executors** — disk cleanup, Windows repair (SFC/DISM), startup management, process control, storage cleanup, network reset, security tools
- **Action execution engine** — dry-run, rollback, privilege detection, governance-aware
- **Operation history** — full audit log of every execution and rollback
- **Startup management** — uses Windows StartupApproved registry mechanism (same as Task Manager)

### Intelligence Layers
- **Evidence graph** — causal chains from intelligence + anomaly + drift + session data
- **Root cause analysis** — identifies most probable causes from evidence
- **Operational replay** — reconstructs historical system state at any point in time
- **Remediation learning** — before/after effectiveness profiles per action
- **Watchtower** — 30-minute autonomous degradation + drift detection cycles
- **Anomaly detection** — z-score comparison to machine baseline at telemetry cadence
- **Simulation engine** — "What if?" projections capped at 88% confidence to acknowledge uncertainty

### UI / Pages (13 pages)
Dashboard, Insights, Processes, Repairs, Security, Storage, Timeline, Apps, Explain, Settings, Replay, Historical, Investigation, Simulation, Diagnostics, RuntimeGovernance, MachineBehavior, DeviceIntelligence, Watchtower, AutonomousRemediation

### Companion Overlay (Phase 17A–17C)
A floating always-on-top panel that surfaces operational presence without forcing app switching.

- **Phase 17A** — Frameless overlay window, bubble/expanded states, drag + snap-to-edge
- **Phase 17B** — Desktop context awareness: observes foreground window, File Explorer path, clipboard metadata via Win32. Contextual banner + suggestion refresh on focus change
- **Phase 17C** — Full conversation engine (see below)

---

## Phase 17C: The Conversation Engine (Most Recent Major Work)

This is the freshest, most complex layer. Read this carefully.

### What It Is
A **deterministic, keyword-matched, evidence-grounded** conversation layer. Not an LLM. Not generative AI. Not a chatbot.

Every response:
- Is sourced from live platform service data
- Has a confidence score (0–100, computed from signal quality)
- Exposes which evidence sources were consulted
- Includes uncertainty notes when confidence is low
- Offers navigation-only suggested action chips (never auto-executes)

### The 9 Files in `Services/Conversation/`

| File | Role |
|------|------|
| `ConversationModels.cs` | `ConversationIntent` enum (22 values), `OperationalPrompt`, `OperationalResponse`, `ResponseConfidence`, `ContextualSuggestion`, `EvidenceSource` |
| `IOperationalConversationService.cs` | Rich Phase 17C interface |
| `ConversationIntentResolver.cs` | Keyword matching → `IntentMatch`. 1 keyword = 72% confidence, 2 = 83%, 3+ = 92% |
| `EvidenceRetrievalPlanner.cs` | Pure switch: intent → `EvidenceSource[]` |
| `WorkflowConversationBridge.cs` | Navigation chip generation from insight state |
| `ReplayConversationBridge.cs` | Timeline change summaries for "what changed?" queries |
| `ContextualSuggestionEngine.cs` | Desktop context → contextual chip suggestions |
| `OperationalResponseComposer.cs` | Per-intent response composition from live data |
| `OperationalConversationService.cs` | Orchestrator; implements both new + legacy interfaces |

### Strict Language Rules (Non-Negotiable)
- **Never** fabricate certainty
- **Never** use "malicious", "infected", "dangerous", "threat detected"
- **Always** use "unusual", "unexpected", "worth reviewing", "flagged for inspection"
- **Always** expose confidence scores and uncertainty notes
- All reasoning must be deterministic, explainable, inspectable

---

## Key Architectural Patterns

### AppServices.cs — Service Registry
No DI container. `AppServices` is a static registry with typed properties.

```csharp
AppServices.Intelligence    // ISystemInsightEngine
AppServices.Timeline        // ITimelineAggregationService
AppServices.ConversationService  // IOperationalConversationService (Phase 17C)
AppServices.DesktopContext  // IDesktopContextService
```

**Initialization order matters.** `DesktopContextService` must be created before `OperationalConversationService` (it's passed in the constructor). This was a bug that was fixed — don't break the ordering.

### MVVM Pattern
- ViewModels use `[ObservableProperty]` from CommunityToolkit.Mvvm
- Use the **generated property** (e.g. `IsProcessing`), not the backing field (`_isProcessing`) — the source generator warns if you reference the field directly
- `[RelayCommand]` on `SendMessageAsync()` generates `SendMessageCommand` — don't add a second `[RelayCommand]` to a sync alias of the same method (causes CS0102 duplicate)
- `x:Bind` inside DataTemplates can't resolve ViewModel commands — use code-behind event handlers with `Tag` carrying the data item

### csproj — Selective Compilation
The project has a large `<Compile Remove="**" />` + explicit `<Compile Include>` pattern. **Every new file must be added explicitly** to both the `<Compile Include>` list and the `<None Include ... Exclude="...">` list. Forgetting this means the file is silently excluded from the build.

### Unicode in Source Files
Several files use Segoe MDL2 Assets glyph characters (e.g. `""`, `""`) and em-dash section comments (`// ── Section ──`). The `Edit` tool's string matching **fails** on these characters. Use `[System.IO.File]::ReadAllText` + `[System.IO.File]::WriteAllText` in PowerShell for any edits to files containing these characters.

---

## Desktop Context Model — Actual Property Names

The `WorkflowContextSnapshot` type (common source of confusion):

```csharp
snap.ExplorerContext        // NOT snap.Explorer
snap.ClipboardContext       // NOT snap.Clipboard
snap.DetectedWorkflowHints  // NOT snap.ContextHints
snap.ActiveWindow           // ActiveWindowContext
snap.CurrentOperationalFocus

// ExplorerContext properties:
explorer.IsDownloadsFolder  // NOT IsDownloads
explorer.IsMediaFolder      // NOT IsMedia
explorer.IsDocumentsFolder  // NOT IsDocuments
explorer.IsProjectFolder    // NOT IsProject
```

`TimelineEvent` uses `Type` (not `EventType`). Rollback enum value is `ActionRollback` (not `ActionRolledBack`).

---

## Core Doctrine

### Security Language
Never use: "malicious", "infected", "dangerous", "threat detected"  
Always use: probabilistic, observational, confidence-aware language

### Resource Governance
Lucid must never become the reason the PC is slow. Every heavy operation must classify itself as `Foreground`, `Background`, or `Idle-only`.

### Reversibility
Before any destructive action: restore point, rollback snapshot, change log, risk explanation. No exceptions.

### Local-Only
No cloud. No telemetry upload. No internet dependency. Everything runs on-device.

---

## Current Branch

`feature/phase-17a-companion-overlay` — this is where all active development lives.

Latest commits:
- `c758f5b` — rename to Lucid (user-visible strings)
- `4201223` — Phase 17C conversation engine
- `eaa2e2e` — Phase 17B desktop context awareness

**Never merge PRs without explicit user confirmation.**

---

## What's Next (Roadmap Priority Order)

1. **Phase 1 — Platform Stabilization**: Settings infrastructure (`ISettingsService`), resource governance formalization, SQLite append-only persistence improvements
2. **Phase 2 — Operational Intelligence Expansion**: Process graph, advanced forecasting, correlation v2
3. **Phase 3 — Security Intelligence**: Behavioral heuristics, trust graph, security timeline
4. **Phase 4 — Explain My PC Flagship**: Natural language operational explanations (the main event)
5. **Full rename**: `ExplainMyPC` → `Lucid` across namespaces, files, solution, GitHub repo

---

## One Last Thing

The user (Tyler) knows this codebase inside out and has strong opinions about the product direction. When in doubt: ask. He'd rather be consulted than have something quietly done wrong.

Good luck — it's a genuinely interesting system to work on.
