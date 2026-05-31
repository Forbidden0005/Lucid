# Lucid

Lucid is a local-first operational intelligence platform for Windows. It helps users understand what their PC is doing, why it is behaving that way, and what they can safely do about it — through explainable diagnostics, evidence-grounded reasoning, and confidence-aware language.

Lucid is not a PC cleaner, not antivirus, and not a "booster." It is a diagnostic and intelligence layer.

---

## Vision

Most system utilities overwhelm users with cryptic metrics, vague warnings, and aggressive upsells.

Lucid answers a different question:

> "What is happening on my computer, why is it happening, and what can I safely do about it?"

The platform is built on four commitments:

- **Explainability** — every finding explains why, not just what
- **Trust** — confidence-aware language, never binary good/bad classification
- **Reversibility** — rollback protection before any destructive action
- **Lightweight** — the platform must never become the reason the PC is slow

---

## What Lucid Is Not

- Not antivirus or a replacement for endpoint security
- Not a registry cleaner or a "speed booster"
- Not a cloud-dependent service — all analysis is local
- Not fear-driven — no "YOUR PC IS IN DANGER" copy anywhere

---

## Core Features

### Telemetry Engine
- 6 samplers: CPU, RAM, GPU, Disk, Thermal, Process
- 30-minute rolling buffer with configurable resolution
- Welford online algorithm for machine-specific baseline learning
- Anomaly detection via z-score comparison to learned baseline

### Insight Engine
- 25 insight rules: anomaly detection, forecasting, synthesis
- Per-process attribution — links resource pressure to specific processes
- Session context awareness: boot time, sleep/wake cycles, idle periods

### Narrative Engine
- Deterministic plain-English system summaries
- Describes current state in human-readable form without exaggeration
- Outputs that read like a trusted analyst, not a threat scanner

### Evidence Graph
- Causal chain construction from telemetry, anomaly, drift, and session data
- Root cause analysis: identifies most probable causes from weighted evidence
- Investigation workspace: explore the evidence behind any finding

### Operational Tools (28 Action Executors)
- Disk cleanup, Windows repair (SFC / DISM), startup management
- Process control, storage cleanup, network stack reset
- Action execution engine: dry-run mode, rollback, privilege detection
- Full operation audit log

### Operational Replay
- Reconstructs historical system state from the telemetry buffer
- Allows investigation of what the system was doing at a past point in time

### Watchtower
- 30-minute autonomous degradation detection cycles
- Proactive surfacing of emerging issues before they become user-visible
- Runs as a background observer, not an intrusive notification system

### Simulation Engine
- "What if?" projections with confidence bounds
- Models the expected impact of proposed changes before execution

### Remediation Learning
- Before/after effectiveness profiles for executed actions
- Tracks whether past interventions actually improved the measured condition

### Security Intelligence
- Persistence scanner: startup analysis, autorun correlation, trust scoring
- Signature verification for executable files
- Windows Defender status awareness
- Behavioral heuristics — patterns, not binary classifications

### Companion Overlay
- Always-on-top floating panel with snap-to-edge positioning
- Desktop context awareness: foreground window, File Explorer path, clipboard observation
- Designed for ambient operational awareness without interrupting workflow

### Conversation Engine
- Deterministic intent-matching over 22 intent types
- Evidence-grounded responses — every answer is traceable to data
- Confidence-scored outputs, no speculative or fabricated diagnostics

### Adaptive Intelligence
- Cognitive reasoning engine with evidence-weighted inference
- Unified recommendation service with attention budget and cognitive load estimation
- Pattern intelligence: recurrence tracking, behavioral drift analysis
- Longitudinal baseline calibration for improving accuracy over time

---

## UI Pages

Dashboard, Insights, Processes, Repairs, Security, Storage, Timeline, Apps, Explain, Settings, Replay, Historical, Investigation, Simulation, Diagnostics, RuntimeGovernance, MachineBehavior, DeviceIntelligence, Watchtower, Privacy

---

## Technology Stack

**Frontend**
- WinUI 3 / Windows App SDK
- C# / .NET 8
- MVVM architecture

**Database**
- SQLite (local, append-oriented repository pattern)

**Native Modules** (planned)
- Rust — filesystem traversal, process monitoring, performance-sensitive analysis

---

## Build Instructions

```
cd lucid-desktop
dotnet build Lucid.slnx -c Debug -p:Platform=x64
```

The `-p:Platform=x64` flag is required. Omitting it causes a hard build failure because `WindowsAppSDKSelfContained=true` does not support AnyCPU.

**If XamlCompiler.exe fails after a clean build**, run this once to regenerate the XAML intermediate assembly:

```
C:\Users\tyler\build_vs.bat
```

This invokes the full VS MSBuild pipeline which handles `XamlPreCompile`. After running it once, `dotnet build` works again.

---

## Repository Structure

```
Lucid/
├── lucid-desktop/     → WinUI 3 frontend (C#, .NET 8)
├── lucid-native/      → Rust native modules (planned)
├── lucid-shared/      → Shared contracts and models (planned)
├── docs/              → Architecture and design documentation
├── ONBOARDING.md      → Developer quick-start guide
├── ROADMAP.MD         → Full phase roadmap with detailed plans
├── CLAUDE.md          → Project doctrine, build notes, AI session context
└── PROJECT_INTEGRITY.md → Code quality governance protocol
```

---

## Core Principles

- **Local-first** — no cloud dependency, no telemetry upload, no internet requirement
- **Explainability** — every finding explains why, not just what was detected
- **Confidence-aware** — probabilistic language throughout; never binary classification
- **Reversibility** — every destructive action requires a rollback path
- **Lightweight** — adaptive polling, idle-aware throttling, background work yields to foreground
- **Modular** — isolated services, composable engines, dependency injection throughout

---

## Development Status

Active development — Phase 21.

The core platform is operational: telemetry, intelligence, evidence graph, operational tools, companion overlay, conversation engine, adaptive intelligence layers, and 20+ UI pages are all built and integrated. Current focus is platform stabilization, resource governance, and SQLite persistence before the next major capability phase.

See [ROADMAP.MD](ROADMAP.MD) for the full phase plan.

---

## Further Reading

- [ONBOARDING.md](ONBOARDING.md) — how to set up and run the project
- [ROADMAP.MD](ROADMAP.MD) — phase-by-phase feature and architecture roadmap
- [CLAUDE.md](CLAUDE.md) — project doctrine, security language rules, build quirks
- [PROJECT_INTEGRITY.md](PROJECT_INTEGRITY.md) — code quality governance protocol
