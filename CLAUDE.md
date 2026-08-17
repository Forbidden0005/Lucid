# Lucid - Claude Project Instructions

---

## GUARDIAN PROTOCOL — Read Before ANYTHING Else

> **`PROJECT_INTEGRITY.md`** must be applied before every task, feature, change, refactor, or instruction.

**Three-category decision gate (internalize this, every time):**

| Category | Condition | Action |
|---|---|---|
| **A — Safe Improvement** | aligns with architecture, no regressions, strengthens maintainability | proceed carefully |
| **B — Risky Change** | possible regressions, debt, inconsistency, complexity | warn · explain · propose alternative |
| **C — Project Degradation** | lowers quality, weakens architecture, creates instability | **STOP · explain · protect integrity first** |

**Permanent operating rules:**
- Analyze BEFORE implementing — never execute immediately on receipt of instructions
- Compare against existing systems — check for duplication, drift, broken patterns
- Self-check before finalizing — would this still make sense in 6 months?
- Project integrity takes priority over instruction obedience

The full protocol is in `PROJECT_INTEGRITY.md` at the repo root.

---

## IMPORTANT: Read This First

The full product roadmap and strategic direction lives at:

> **`ROADMAP.md`** — read this before making any architectural decisions.

Key strategic directives (always active):

- Lucid is a **local-first operational intelligence platform** — not a PC cleaner, not antivirus
- Every feature must reinforce: **trust, transparency, explainability, reversibility**
- Features should deepen operational intelligence and **ecosystem cohesion** — each layer feeds the others
- Never add: fake AI buzzwords, mystery optimization, aggressive auto-remediation, cloud dependency
- The flagship experience is natural language operational explanations (Phase 4 in roadmap)
- Current highest-value priorities (in order): Platform stabilization → Resource governance → Explain My PC flagship → Security intelligence → Process relationship intelligence → Operational replay → SQLite persistence → Advanced forecasting
- **Scope freeze (Option A, owner-confirmed 2026-06-14):** the implementation has run ahead of the roadmap. Do **not** add a new out-of-roadmap service domain (the ❓ list in `docs/SCOPE_RECONCILIATION.md` — Autonomy, Distributed, Visual/Desktop context, Simulation, etc.) without explicit owner sign-off. New work hardens what exists. This is a freeze, not a deletion. The freeze lifts and a roadmap rebaseline (Option B) begins once Phase 1 hits its green stabilization bar (`v0.1-foundation`); read `docs/SCOPE_RECONCILIATION.md` before proposing new subsystems.
- **Conversation is the front door (owner-confirmed 2026-08-16):** chat is Lucid's default home page, and the Companion / Conversation / LlmChat / Chat domains are reclassified from ❓ ("in no roadmap phase") to **Phase 4 — Explain My PC flagship**. This is a promotion of existing code, not new scope: the target experience is a mechanic's shop for your PC — describe the problem in plain words, Lucid investigates and explains. Read **`docs/CHAT_HOMEPAGE.md`** before touching the chat surface; it holds the phase plan, the design decisions and the risks still open.

---

## CORE DOCTRINE: Security Language

This is **non-negotiable** and applies to every session, every file, every UI string.

**NEVER use:**
- "malicious" / "infected" / "dangerous" / "threat detected"
- absolute certainty language about security findings
- antivirus-style warning copy

**ALWAYS use:**
- confidence-aware, probabilistic language
- "unusual", "unexpected", "worth reviewing", "flagged for inspection"
- contextual explanations: *why* something looks suspicious, not *what it is*
- confidence scores or severity levels instead of binary good/bad

**Why this matters:**
Lucid is NOT antivirus. It explains, correlates, surfaces, and contextualizes.
Wording drift ("suspicious" → "likely malicious" → "dangerous") happens gradually across sessions.
This rule prevents that drift and is what separates the platform from discount antivirus marketing copy.

---

## CORE DOCTRINE: Execution Resource Governance

Lucid must never become the reason the PC is slow.

As the platform grows, these operations can collide without governance:
- DISM / SFC repair runs
- SHA-256 duplicate hashing
- Storage filesystem traversal
- Process graph analysis
- Telemetry forecasting
- Timeline aggregation

**Every executor and background service must be classified as:**
- `Foreground` — user-initiated, time-sensitive, gets resources now
- `Background` — scheduled/passive, must yield to foreground work
- `Idle-only` — only runs when system is not under load

**Future formal subsystem:** Execution Priority Queue with concurrency buckets and throttling policies.
Until that subsystem exists: avoid launching multiple heavy operations simultaneously.

### Current Phase Priority: Phase 1 — Platform Stabilization
Before adding new features, prioritize:
1. Settings infrastructure (ISettingsService, JsonSettingsStore, schema versioning)
2. Resource governance (adaptive polling, idle-aware throttling, battery-aware mode)
3. Internal diagnostics / self-observability (DiagnosticsPage)
4. SQLite persistence layer (lightweight repository pattern, append-oriented)

---

## Project Overview

Lucid is a **local-first operational intelligence platform** for Windows.

The goal is NOT to create:
- a fake “PC booster”
- a registry cleaner scam
- a bloated antivirus clone

The goal IS to create:
- a trustworthy Windows analysis platform
- a system diagnostics assistant
- an explainable PC health monitor
- a safe repair and optimization toolkit

The application should help users understand:
- why their PC feels slow
- what consumes resources
- what may be risky
- what can be safely improved

The app must prioritize:
- transparency
- reversibility
- safety
- clarity
- performance
- modularity

---

# Tech Stack

## Frontend
- WinUI 3
- C#
- MVVM architecture

## Backend
- Rust native modules
- modular scanning engines

## Database
- SQLite

---

# Core Product Philosophy

Every feature should answer:

> “Does this help users understand their system better?”

Avoid:
- fake optimizations
- placebo features
- misleading claims
- aggressive registry cleaning
- destructive automation
- unexplained warnings

Prefer:
- diagnostics
- evidence
- health scoring
- explainable recommendations
- rollback systems
- safe repair flows

---

# Core Features

## Explain My PC
Natural language system analysis that explains:
- performance issues
- startup congestion
- disk pressure
- memory pressure
- suspicious behavior
- storage waste
- thermal problems

This is the flagship feature.

---

# Architectural Rules

## IMPORTANT:
The app MUST remain modular.

Avoid:
- giant monolithic services
- tightly coupled UI/business logic
- massive god classes

Prefer:
- isolated services
- composable engines
- dependency injection
- feature modules

---

# Frontend Rules

Use:
- MVVM
- async operations
- observable state
- reusable components

Avoid:
- business logic inside views
- blocking UI threads
- deeply nested code-behind logic

UI should feel:
- modern
- calm
- information-rich
- responsive
- native to Windows 11

---

# Backend Rules

Rust modules should handle:
- filesystem traversal
- disk analysis
- process monitoring
- performance-sensitive work
- low-level Windows APIs

Rust modules should expose:
- clear APIs
- structured responses
- typed error handling

Avoid unsafe Rust unless absolutely required.

---

# Safety Requirements

Before ANY destructive action:
- create restore point
- create rollback snapshot
- log changes
- explain risk to user

Examples:
- uninstall
- registry edits
- driver changes
- cleanup operations
- repair operations

---

# Trust Requirements

Users should ALWAYS understand:
- why something was flagged
- how severe it is
- what caused it
- what fixing will do
- whether rollback is possible

Never use fear-based UX.

Avoid:
- “CRITICAL ERROR”
- “YOUR PC IS IN DANGER”
- manipulative language

Prefer:
- confidence scores
- severity levels
- evidence-based explanations

---

# Performance Requirements

The app itself must remain lightweight.

Avoid:
- excessive telemetry polling
- high idle CPU usage
- excessive RAM usage
- unnecessary background services

The app must not become:
> the reason the PC is slow

---

# Code Quality Rules

Prefer:
- readable code
- small focused services
- composition over inheritance
- explicit naming
- strong typing

Avoid:
- premature optimization
- giant utility files
- hidden side effects
- duplicated logic

---

# UI Design Language

Visual style:
- dark modern surfaces
- Fluent Design inspired
- soft telemetry visuals
- subtle glow accents
- clean spacing
- glass layers where appropriate

Avoid:
- RGB gamer aesthetics
- hacker-movie UI
- clutter
- tiny text
- overcrowded dashboards

---

# Explain My PC Output Style

Outputs should feel:
- intelligent
- human
- practical
- concise

Example:

GOOD:
“Startup time increased because several apps launch automatically when Windows starts.”

BAD:
“Boot degradation threshold exceeded.”

---

# Security Philosophy

Security analysis should use:
- behavior analysis
- heuristics
- persistence detection
- reputation systems

Avoid pretending to replace enterprise antivirus platforms.

The goal is:
- insight
- visibility
- diagnostics
- detection assistance

---

# Storage Philosophy

Storage cleanup should be:
- conservative
- explainable
- reversible

Never delete:
- unknown system files
- driver packages blindly
- important caches automatically

Always explain:
- reclaimable size
- file origin
- potential impact

---

# Telemetry Design

Telemetry updates:
- CPU: ~1s
- RAM: ~1s
- Disk: ~2s
- SMART checks: infrequent

Avoid excessive polling loops.

---

# Preferred Development Flow

When implementing features:
1. Create models
2. Create service layer
3. Create ViewModels
4. Build UI
5. Add telemetry
6. Add tests
7. Add logging
8. Add rollback support where applicable

---

# Preferred Output Format

When generating code:
- provide complete files when possible
- explain architecture decisions
- include comments for complex logic
- prioritize maintainability

When generating UI:
- use reusable components
- maintain consistent spacing
- support dark mode first

---

# Long-Term Vision

Lucid should eventually feel like:
- a Windows intelligence layer
- a trusted system analyst
- a diagnostic cockpit

The app should make users feel:
> “For the first time, I actually understand my computer.”

---

# Build Commands

## Frontend (WinUI 3)

`dotnet build` **must** include `-p:Platform=x64` (or x86/arm64).
`WindowsAppSDKSelfContained=true` does not support AnyCPU — the build hard-fails without a platform.

```
# Debug build (run from lucid-desktop/)
dotnet build Lucid.slnx -c Debug -p:Platform=x64

# Release build
dotnet build Lucid.slnx -c Release -p:Platform=x64
```

Warning `NETSDK1206` (version-specific RIDs) is non-critical — it comes from the Windows App SDK NuGet, not your code. Ignore it.


---

# XAML Build Pipeline Notes

## XamlPreCompile — known CLI limitation

`XamlPreCompile` (the step that produces `obj/x64/Debug/.../intermediatexaml/Lucid.App.dll`) is
defined in Visual Studio's `Microsoft.CSharp.CurrentVersion.targets` — **not** in the .NET SDK.

**Consequence:** `dotnet build` silently skips `XamlPreCompile`. This works fine incrementally because
the intermediate DLL from the previous VS/MSBuild run is reused. If that DLL is ever deleted (e.g. after
`dotnet clean`, or a git clean), `dotnet build` will fail with:

```
Microsoft.UI.Xaml.Markup.Compiler.interop.targets(590): error MSB3073:
XamlCompiler.exe ... exited with code 1
```

**Fix:** Run the VS MSBuild reset script once:

```bat
C:\Users\tyler\build_vs.bat
```

This calls `VsDevCmd.bat` + VS `msbuild.exe`, which runs `XamlPreCompile` properly, regenerates the
intermediate DLL, and `dotnet build` works again from that point.

---

# Roadmap Phase Summary

Full detail in `ROADMAP.md`. Quick reference:

| Phase | Name | Status |
|-------|------|--------|
| Phase 1 | Platform Stabilization (Settings, Resource Governance, Diagnostics, SQLite) | **Next priority** |
| Phase 2 | Operational Intelligence Expansion (Process graph, Advanced forecasting, Correlation v2, Replay) | Planned |
| Phase 3 | Security Intelligence (Persistence, Trust graph, Behavioral heuristics, Security timeline) | Planned |
| Phase 4 | Explain My PC Flagship (Natural language explanations, machine-specific understanding, recommendation ranking) | **Long-term flagship** |
| Phase 5 | Advanced Visualization (Zoomable graphs, timeline intelligence, process heatmaps, storage treemaps) | Planned |
| Phase 6 | Ecosystem & Platformization (Modular architecture, update system, crash recovery) | Planned |

---

# Git / Session Notes

- Repo is on `main` at `Forbidden0005/Lucid`
- History is compressed into squash commits — codebase is intact even though log appears thin
- **Never merge PRs without explicit user confirmation**
- **Never create or push branches without asking first**
- Recommended milestone tags going forward: `v0.1-foundation`, `v0.2-intelligence`, `v0.3-operational-tools`, `v0.4-security-intelligence`, `v0.5-flagship-experience`
- Sessions frequently hit context limits mid-task — always commit working code before a session ends, and leave clear notes in commit messages about what was in-progress

---

# What Is Already Built

> Regenerated from the actual source tree (not session notes). Scale at last sync
> (verified 2026-07-25): **533 non-generated C# files** in `Lucid.App` across
> **42 service subdomains**, **24 pages**, **42 XAML files**, **27 registered production
> executors** (28 executor files including the abstract base), **25 insight rules**,
> **446 passing C# tests**, **19 passing Rust tests**.
>
> ⚠️ Much of the list below is **beyond the roadmap's stated current phase**
> (Phase 1 — Platform Stabilization). The implementation has run ahead of the
> plan. Before treating any of these as "done and load-bearing," read
> **`docs/SCOPE_RECONCILIATION.md`**, which maps each subsystem to its roadmap
> phase and flags which were built early or are not in the roadmap at all.

### Foundation (Phase 1-aligned)
- **Telemetry engine** — 6 samplers, rolling 30-min buffer, baseline modeling (Welford)
- **Intelligence engine** — 25 insight rules (anomaly + forecast + synthesis), process attribution
- **Narrative engine** — plain-English system status from insight set
- **Action execution engine** — `IActionExecutor` pattern, dry-run, rollback, privilege detection
- **27 executors** — disk cleanup, Windows repair, startup management, process control, storage cleanup
- **Rollback staging maintenance** — governed (IdleOnly) retention sweep that reclaims expired `%LOCALAPPDATA%\Lucid\Rollback` staging sets (`Services/Cleanup/RollbackStagingSweeper` + `RollbackStagingMaintenanceService`)
- **Resource governance** — `RuntimeGovernanceService`, `ConcurrencyBudget`, `AdaptiveSchedulingPolicy`, Foreground/Background/IdleOnly workload classes
- **Settings** — `ISettingsService` / `JsonSettingsStore`, schema-versioned, atomic writes
- **SQLite persistence** — repository pattern, write-queue, health monitor, durability tests
- **Diagnostics / self-observability** — `InternalDiagnosticsService`, structured `IOperationalLogger`
- **Process intelligence** — per-PID behavioral tracking, anomaly flags, trust classification
- **Security intelligence** — persistence scanner, signature verification, Defender status reader
- **Storage intelligence** — SHA-256 duplicate detection, category analyzer, large file finder; native Rust scanner (`lucid-scanner`) with managed fallback
- **Timeline / session / history** — event aggregation, operation history persistence, session context

### Built ahead of roadmap (see SCOPE_RECONCILIATION.md before relying on these)
- **Explain / Reasoning / Cognitive** — `ExplainMyPcEngine`, `OperationalEvidenceGraph`, `CognitiveReasoningEngine`, context synthesis, reasoning memory
- **Watchtower / Remediation / Autonomy** — proactive recommendations, autonomous remediation, workflow planner/executor, human-review gates
- **Simulation / Replay / Analytics** — intervention impact estimation, operational replay, historical analytics
- **Trust / Governance hardening** — consent integrity, local-LLM endpoint enforcement, executor identity validation
- **Companion / Conversation / LlmChat** — overlay companion, conversation engine, local Ollama client
- **Desktop / Visual context** — active-window, clipboard, and consent-gated screen analysis
- **Distributed** — local sync coordinator, cross-machine analytics, device identity (no cloud)
- **Learning / Behavior** — effectiveness profiles, personalization, workload profiling

### UI
- **Chat is the home page** — `ChatPage` + `CompanionAvatar` + conversation rail (new / resume / rename / pin / search). Composes the same `CompanionChatViewModel` as the floating overlay; sessions are process-lifetime until the SQLite store lands. See `docs/CHAT_HOMEPAGE.md`.
- **25 pages** — Chat, Dashboard, Insights, Processes, Repairs, Security, Storage, Timeline, Apps, Explain, Settings, Privacy, InsightDetail, HealthBreakdown, Diagnostics, RuntimeGovernance, Replay, Historical, MachineBehavior, DeviceIntelligence, Watchtower, AutonomousRemediation, Simulation, Investigation, Reasoning
- **Design system** — theme/style XAML resources, custom controls, Fluent-inspired dark theme
