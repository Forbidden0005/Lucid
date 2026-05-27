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

ExplainMyPC must never become the reason the PC is slow.

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

`XamlPreCompile` (the step that produces `obj/x64/Debug/.../intermediatexaml/ExplainMyPC.App.dll`) is
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

- Repo is on `main` at `Forbidden0005/ExplainMyPC`
- History is compressed into squash commits — codebase is intact even though log appears thin
- **Never merge PRs without explicit user confirmation**
- **Never create or push branches without asking first**
- Recommended milestone tags going forward: `v0.1-foundation`, `v0.2-intelligence`, `v0.3-operational-tools`, `v0.4-security-intelligence`, `v0.5-flagship-experience`
- Sessions frequently hit context limits mid-task — always commit working code before a session ends, and leave clear notes in commit messages about what was in-progress

---

# What Is Already Built

See full inventory in session notes. Summary of major systems:

- **Telemetry engine** — 6 samplers, rolling 30-min buffer, baseline modeling (Welford)
- **Intelligence engine** — 25 insight rules (anomaly + forecast + synthesis), process attribution
- **Narrative engine** — plain-English system status from insight set
- **Action execution engine** — IActionExecutor pattern, dry-run, rollback, privilege detection
- **28 executors** — disk cleanup, Windows repair, startup management, process control, storage cleanup
- **Process intelligence** — per-PID behavioral tracking, anomaly flags, trust classification
- **Security intelligence** — persistence scanner, signature verification, Defender status reader
- **Storage intelligence** — SHA-256 duplicate detection, category analyzer, large file finder
- **Timeline system** — chronological event aggregation, grouped by time, filter chips
- **Session & history** — operation history persistence, session context tracking
- **13 pages** — Dashboard, Insights, Processes, Repairs, Security, Storage, Timeline, Apps, Explain, Settings, Privacy, InsightDetail
- **Design system** — 9 XAML style files, 5 custom controls, Fluent-inspired dark theme
