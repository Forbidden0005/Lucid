# ExplainMyPC — Next Generation Roadmap

## What ExplainMyPC Is Becoming

ExplainMyPC is no longer just:
- a PC cleaner
- a repair utility
- a telemetry dashboard
- a troubleshooting app

It is evolving into:

> A local-first operational intelligence platform for Windows.

The platform now combines:
- live telemetry
- behavioral analysis
- forecasting
- operational narratives
- explainable recommendations
- reversible remediation
- process intelligence
- timeline cognition
- trust-aware security analysis
- historical operational memory

The long-term direction should optimize for:
- trust
- transparency
- explainability
- reversibility
- operational clarity
- local-only intelligence
- confidence-aware reasoning
- deterministic behavior

NOT:
- registry-cleaner behavior
- mystery optimization
- fake AI automation
- aggressive auto-remediation
- antivirus replacement behavior

---

# Current State

The platform already contains:

## Intelligence Layer
- telemetry engine
- baseline modeling
- forecasting
- synthesis rules
- process attribution
- trend analysis
- operational narratives
- session awareness
- timeline aggregation
- sparkline visualization
- insight drilldowns

## Operational Layer
- execution engine
- rollback architecture
- startup management
- cleanup tooling
- Windows repair tooling
- storage analysis
- live execution logs
- operation history

## Security & Process Layer
- process intelligence
- persistence detection
- signature verification
- Defender awareness
- startup trust analysis
- behavioral anomaly detection

This is already significantly beyond a standard utility application.

---

# Strategic Direction

The roadmap should now shift from:

"add isolated features"

to:

"deepen operational intelligence and ecosystem cohesion"

Future systems should reinforce one another.

Example:
- process intelligence improves forecasting
- forecasting improves narratives
- narratives improve recommendations
- timelines improve explainability
- storage analysis improves forecasting
- security analysis improves trust scoring
- repairs feed operational history
- operational history improves future recommendations

The platform should increasingly feel:
- aware
- contextual
- explainable
- operationally coherent

---

# Phase 1 — Platform Stabilization

## Goal
Ensure long-term maintainability before adding extreme complexity.

## Priority Tasks

### 1. Settings Infrastructure
Build:
- typed settings service
- schema versioning
- export/import
- defaults management
- feature flags
- environment profiles

Files:
- ISettingsService
- JsonSettingsStore
- SettingsSchemaVersion
- SettingsMigrationRunner

---

### 2. Resource Governance
Prevent ExplainMyPC from becoming resource-heavy.

Add:
- adaptive polling
- idle-aware throttling
- thermal-aware polling reduction
- battery-aware reduced mode
- low-power telemetry mode
- executor concurrency limits

Goal:
"Operational intelligence that stays lightweight."

---

### 3. Internal Diagnostics
Add self-observability.

Track:
- failed samplers
- executor crashes
- service stalls
- polling overruns
- UI thread stalls
- timeline backlog pressure

Add:
- DiagnosticsPage
- internal health timeline
- service-state monitor

Goal:
"The platform should explain itself too."

---

### 4. Persistent Indexed Storage
The current in-memory architecture is excellent for now, but growth will eventually require indexing.

Introduce SQLite carefully.

Use cases:
- historical telemetry retention
- operation history
- timeline persistence
- scan caching
- storage indexing
- trend replay

DO NOT:
- immediately persist everything
- create heavy ORM complexity

Recommended:
- lightweight repository pattern
- append-oriented event storage

---

# Phase 2 — Operational Intelligence Expansion

## Goal
Deepen causality and operational understanding.

---

## 1. Process Relationship Graph

Move beyond process lists.

Build:
- parent/child process trees
- lifetime correlation
- dependency visualization
- process clusters
- runaway-spawn detection
- restart-loop detection

Visualize:
- process ecosystems
- dominant resource clusters
- suspicious behavior chains

---

## 2. Advanced Forecasting

Current forecasting is strong.
Next step:

Add:
- confidence decay modeling
- multi-signal forecasting
- seasonal behavior awareness
- workload classification
- recovery probability modeling

Examples:
- "System likely to stabilize naturally in ~12 minutes"
- "Current RAM growth pattern resembles previous Chrome memory escalation"

---

## 3. Correlation Engine v2

Current synthesis rules are excellent.
Expand into:

- event graph correlation
- causal chains
- cross-domain reasoning
- confidence propagation
- narrative-linked causality

Examples:
- startup app → disk contention → elevated CPU → thermal rise
- browser extension → GPU spikes → memory growth → forecast pressure

---

## 4. Operational Replay

Build historical replay.

Users should be able to:
- scrub timelines
- replay degradation events
- view metric evolution
- inspect remediation impact
- compare before/after states

Goal:
"Forensic operational understanding."

---

# Phase 3 — Security Intelligence

## Goal
Become a transparent operational security layer.

NOT antivirus.

---

## Security Philosophy

ExplainMyPC should:
- explain
- contextualize
- correlate
- warn
- visualize
- classify confidence

It should NOT:
- auto-delete
- auto-quarantine
- claim malware certainty
- replace Defender

---

## Planned Systems

### Persistence Intelligence
- startup analysis
- scheduled task analysis
- autorun correlation
- unusual persistence scoring

### Trust Graph
- signed vs unsigned
- trust lineage
- vendor grouping
- resource-heavy unsigned apps

### Behavioral Heuristics
- suspicious launch loops
- temp-executable persistence
- abnormal PowerShell activity
- hidden background CPU/GPU activity

### Security Timeline
- Defender state changes
- trust posture changes
- suspicious persistence events
- repeated relaunch loops

---

# Phase 4 — ExplainMyPC Flagship Experience

## Goal
Make the platform conversational and operationally understandable.

This is the true flagship layer.

---

## Explain My PC Workspace

The platform should eventually answer:

- Why is my PC slow?
- What changed recently?
- What is causing heat?
- Why is memory increasing?
- Why did startup worsen?
- Is this behavior normal for MY machine?
- What happens if I ignore this?
- What should I safely do next?

And answer with:
- telemetry
- timelines
- process attribution
- narratives
- forecasts
- confidence-aware reasoning
- operational history

Combined into:

> coherent operational explanations.

---

## Planned Features

### Natural Operational Explanations
Examples:

"Chrome appears responsible for most current memory pressure. Memory usage has increased 22% over the last 18 minutes and is forecasted to reach elevated pressure within approximately 14 minutes if current behavior continues."

---

### Machine-Specific Understanding

Examples:
- "This CPU usage is abnormal for your machine"
- "Your startup time has worsened compared to the past week"
- "This thermal pattern resembles previous gaming sessions"

---

### Recommendation Ranking

Recommendations should eventually consider:
- risk
- reversibility
- confidence
- historical effectiveness
- current workload
- user habits

---

# Phase 5 — Advanced Visualization

## Goal
Turn operational understanding into visual cognition.

---

## Planned Systems

### Expanded Telemetry Graphs
- zoomable history
- event markers
- forecast overlays
- baseline bands
- comparison windows

### Timeline Intelligence
- causality linking
- event clustering
- narrative checkpoints
- operational replay

### Process Heatmaps
- live resource maps
- process dominance visualization
- temporal behavior layers

### Storage Visualization
- treemaps
- growth timelines
- cleanup impact previews

---

# Phase 6 — Ecosystem & Platformization

## Goal
Prepare ExplainMyPC for long-term extensibility.

---

## 1. Modular Feature Architecture

Introduce clearer module boundaries:
- telemetry
- storage
- repair
- security
- process intelligence
- forecasting
- narratives

Eventually support:
- optional modules
- experimental features
- plugin-like extensions

---

## 2. Update & Migration System

Needed for:
- schema changes
- feature rollout
- repair executor evolution
- historical compatibility

---

## 3. Crash Recovery & Recovery Mode

Add:
- safe startup mode
- executor recovery
- corrupted state detection
- timeline recovery

---

# Technical Principles Going Forward

## Keep
- local-first
- deterministic
- explainable
- reversible
- confidence-aware
- observable
- modular
- operationally coherent

## Avoid
- fake AI buzzwords
- aggressive optimization claims
- registry-cleaner behavior
- hidden automation
- unexplained heuristics
- cloud dependency
- "one-click fix everything"

---

# Important Project Notes

## Build
Use:

`dotnet build ExplainMyPC.slnx -c Debug -p:Platform=x64`

Avoid AnyCPU.

If XAML intermediates break:
- run build_vs.bat first
- regenerate intermediates
- rebuild solution

---

## Git
The repo currently uses large squash commits.
The codebase is intact even though history appears compressed.

Going forward:
- avoid premature PR merges
- keep milestone checkpoints
- tag major architectural milestones

Recommended future tags:
- v0.1-foundation
- v0.2-intelligence
- v0.3-operational-tools
- v0.4-security-intelligence
- v0.5-flagship-experience

---

# Current Highest-Value Priorities

In order:

1. Platform stabilization
2. Resource governance
3. Explain My PC flagship experience
4. Security intelligence
5. Process relationship intelligence
6. Operational replay
7. Indexed persistence layer
8. Advanced forecasting
9. Modular architecture
10. Historical analytics

---

# The Long-Term Vision

ExplainMyPC is becoming:

> A trustworthy, local-first operational intelligence layer for Windows.

Not merely:
- diagnostics
- optimization
- repair
- security scanning

But:
- understanding
- explanation
- forecasting
- operational guidance
- reversible remediation
- contextual awareness
- historical operational memory

The most important differentiator should remain:

> Explainability + Trust.

