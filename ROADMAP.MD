# Lucid (formerly ExplainMyPC) — Roadmap

## What Lucid Is Becoming

Lucid is no longer just:
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
- adaptive learning and confidence calibration
- cognitive-load-aware communication
- companion overlay and desktop context awareness

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
- cognitive reasoning engine
- recommendation arbitration
- adaptive learning and pattern intelligence

## Operational Layer
- execution engine
- rollback architecture
- startup management
- cleanup tooling
- Windows repair tooling
- storage analysis
- live execution logs
- operation history
- operational watchtower (autonomous degradation detection)

## Security & Process Layer
- process intelligence
- persistence detection
- signature verification
- Defender awareness
- startup trust analysis
- behavioral anomaly detection
- trust integrity service
- governance audit system

## Interaction & Context Layer
- companion overlay window (always-on-top, bubble/expanded states)
- desktop context awareness (foreground window, File Explorer path, clipboard)
- deterministic conversation engine (22 intent types, evidence-grounded)
- attention coordination and cognitive interrupt budget
- calm communication formatting and confidence tone adjustment

This is significantly beyond a standard utility application.

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
- adaptive learning improves confidence calibration over time

The platform should increasingly feel:
- aware
- contextual
- explainable
- operationally coherent

---

# Phase 1 — Platform Stabilization ✅ COMPLETE

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
Prevent Lucid from becoming resource-heavy.

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

### 2a. Execution Priority Queue (Formal Subsystem)

As the operation set grows, heavy tasks will collide without governance:
- DISM / SFC runs
- SHA-256 duplicate hashing across large drives
- Storage filesystem traversal
- Process graph analysis
- Telemetry forecasting

Build:
- `ExecutionPriorityQueue` — ordered work queue with foreground/background/idle-only buckets
- `ConcurrencyBudget` — max simultaneous heavy operations (e.g. 1 disk-bound + 1 CPU-bound)
- `ThrottlingPolicy` — per-executor resource ceiling
- `WorkClassification` — each executor declares its resource class at registration

Goal:
"Lucid must never be the reason the PC is slow."

Example problem this solves:
"Lucid is using 38% CPU to explain why your CPU is high."

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

# Phase 2 — Operational Intelligence Expansion ✅ COMPLETE

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

# Phase 3 — Security Intelligence ✅ COMPLETE

## Goal
Become a transparent operational security layer.

NOT antivirus.

---

## Security Philosophy

Lucid should:
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

# Phase 4 — Lucid Flagship Experience ✅ COMPLETE

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

# Phase 5 — Advanced Visualization ✅ COMPLETE

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

# Phase 6 — Ecosystem & Platformization ✅ COMPLETE

## Goal
Prepare Lucid for long-term extensibility.

---

## 1. Domain Event Bus

As the platform grows, these services will start observing each other directly:
- TimelineAggregationService
- NarrativeEngine
- IntelligenceEngine
- SecurityIntelligenceService
- ProcessIntelligenceService
- StorageAnalysisService
- future conversational layer

Direct cross-service subscriptions are fine now while the architecture fits in one person's head.
When that changes, introduce a Domain Event Bus for decoupling.

Design:
- `IDomainEvent` — base interface for all platform events
- `IEventBus` — publish/subscribe interface
- `InProcessEventBus` — lightweight in-process implementation (no external broker)
- Services publish events (e.g. `InsightOnsetEvent`, `ActionExecutedEvent`, `ProcessAnomalyEvent`)
- Consumers subscribe to the bus instead of holding direct service references

---

## 2. Modular Feature Architecture

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

## 3. Update & Migration System

Needed for:
- schema changes
- feature rollout
- repair executor evolution
- historical compatibility

---

## 4. Crash Recovery & Recovery Mode

Add:
- safe startup mode
- executor recovery
- corrupted state detection
- timeline recovery

---

# Phase History (7–21)

These phases were implemented after Phase 6 and are fully built into the codebase.

---

## Phase 7 — Autonomous Operational Watchtower ✅ COMPLETE

**Goal:** Proactive degradation detection without user intervention.

- `OperationalWatchtowerService` runs 30-minute autonomous cycles
- Detects pattern degradation and operational drift between cycles
- Generates proactive alerts surfaced to the UI without requiring user action
- Classified as a background/idle-only executor; yields to foreground work

---

## Phase 15 — Adaptive Personalization & Intervention Memory ✅ COMPLETE

**Goal:** Adapt the platform's behavior based on observed user response patterns.

- `PersonalizationEngine` tracks which recommendation types the user engages with
- `InterventionMemoryService` records timing and phrasing of past interventions
- Future intervention timing and phrasing adjusts based on historical response patterns
- Prevents repeated, ignored recommendations from re-surfacing at the same frequency

---

## Phase 16 — Unified Operational Evidence Graph & Investigation Workspace ✅ COMPLETE

**Goal:** Causal chain reasoning from correlated multi-source evidence.

- `OperationalEvidenceGraph` builds causal chains from telemetry, anomaly, drift, and session data
- `RootCauseAnalysisEngine` traverses the graph to identify probable root causes
- `EvidenceExplanationService` translates graph paths into plain-English causal narratives
- `InvestigationPage` UI surfaces the evidence graph and root cause walkthrough

---

## Phase 17A — Companion Overlay Window ✅ COMPLETE

**Goal:** Always-available surface for operational awareness without switching windows.

- Frameless always-on-top panel with bubble state and expanded state
- Drag to reposition; snap-to-edge behavior on release
- `CompanionSessionManager` tracks companion state across sessions
- Transitions between states are animated and non-intrusive

---

## Phase 17B — Desktop Context Awareness ✅ COMPLETE

**Goal:** Surface contextually relevant suggestions based on what the user is doing.

- `DesktopContextService` observes foreground window, active File Explorer path, and clipboard metadata via Win32 APIs
- Contextual suggestions refresh automatically on focus change
- No content of files or clipboard is read — only metadata (filenames, paths, window titles)
- Feeds context into the companion overlay and conversation engine

---

## Phase 17C — Conversation Engine ✅ COMPLETE

**Goal:** Deterministic, evidence-grounded natural language interaction layer.

- `OperationalConversationService` handles 22 distinct intent types
- Keyword-matched intent classification — no probabilistic LLM routing
- Every response includes a confidence score, evidence sources, and uncertainty notes where applicable
- 9-file implementation under `Services/Conversation/`
- Integrated with the companion overlay and InvestigationPage

---

## Phase 18 — Unified Operational Core ✅ COMPLETE

**Goal:** Decouple services via a shared event and lifecycle foundation.

- `LucidEventBus` — in-process pub/sub bus replacing direct cross-service subscriptions
- `LifecycleCoordinator` — manages ordered startup and shutdown of all services
- `OperationalStateCoordinator` — tracks system-wide operational state
- `OperationalLogger` — structured logging with correlation context (session ID, operation ID)

---

## Phase 18C — Trust & Governance Hardening ✅ COMPLETE

**Goal:** Enforce trust boundaries and auditability across the platform.

- `TrustIntegrityService` validates that reasoning outputs have traceable evidence sources
- `GovernanceDiagnosticsService` and `GovernanceAuditEvent` provide a complete audit trail for all governance decisions
- `OllamaClient` integrates a local LLM (localhost-only); `LocalEndpointValidator` enforces no remote calls
- `PersistenceHealthMonitor` detects and reports SQLite write failures before they silently corrupt state

---

## Phase 19 — Unified Cognitive Reasoning Layer ✅ COMPLETE

**Goal:** Evidence-weighted inference with uncertainty propagation across the intelligence layer.

- `CognitiveReasoningEngine` synthesizes multi-source evidence into weighted inferences
- `RecommendationArbitrator` resolves conflicts between competing recommendations using evidence weight
- `OperationalContextSynthesizer` assembles a unified operational context object from live + historical data
- `ReasoningMemoryService` persists past reasoning chains so future inferences can reference prior conclusions

---

## Phase 19 Gap — Evidence Attribution & Cognitive Diagnostics ✅ COMPLETE

**Goal:** Fill attribution and diagnostic gaps left by the Phase 19 initial implementation.

- Evidence attribution paths: every inference now traces back to its source signals
- Governance-aware reasoning: reasoning outputs are gated by trust integrity checks
- Diagnostic completeness: `CognitiveReasoningEngine` now emits structured diagnostics for failed or low-confidence inferences

---

## Phase 20 — Human Interaction & Cognitive UX Layer ✅ COMPLETE

**Goal:** Control how and when the system surfaces information to avoid overwhelming the user.

- `AttentionCoordinator` decides when to interrupt the user versus queue for later
- `CognitiveInterruptBudget` tracks how many interruptions have been surfaced in a time window
- `CognitiveLoadEstimator` estimates current user cognitive load from context signals
- `UnifiedRecommendationService` merges and deduplicates recommendations from all subsystems
- `CalmCommunicationFormatter` ensures output phrasing matches calm, factual tone
- `ConfidenceToneAdjuster` modulates phrasing based on inference confidence level
- `SeverityNarrativeEngine` generates severity-appropriate descriptions without fear-based language

---

## Phase 21 — Adaptive Operational Learning & Pattern Intelligence ✅ COMPLETE

**Goal:** The platform learns from its own inferences and adjusts confidence calibration over time.

- `AdaptiveBaselineTracker` updates per-machine behavioral baselines as the system observes more data
- `ConfidenceCalibrationEngine` adjusts raw inference confidence based on historical accuracy
- `HistoricalAccuracyTracker` records which past inferences were later confirmed or disconfirmed
- `PatternIntelligenceEngine` detects recurring operational patterns across sessions
- `PatternSimilarityAnalyzer` computes similarity scores between current and historical patterns
- `RecurrenceTracker` tracks how often a given pattern repeats and at what intervals
- `LongitudinalBehaviorTracker` maintains long-horizon behavioral trends across days and weeks

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

`dotnet build Lucid.slnx -c Debug -p:Platform=x64`

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
- v0.6-cognitive-layer (Phase 18–21)

---

# Current Highest-Value Priorities (Phase 22 Candidates)

Phase 21 is complete. The platform has a fully operational cognitive reasoning, adaptive learning, and interaction governance stack. The next priorities address hardening, namespace alignment, UI surface gaps, and the Rust native layer.

In order:

1. **Production hardening** — replace bare `catch` blocks in repositories with logged, structured errors; improve SQLite write durability (WAL mode, retry on busy)
2. **Full namespace/project rename** — `ExplainMyPC` → `Lucid` throughout all C# namespaces, `.csproj`, `.slnx`, and string references
3. **UI surface for Phases 18–21** — the learning, cognitive, and governance layers are fully built but have no dedicated UI pages; a Cognitive Layer diagnostics page and a Learning History page would expose this intelligence to users
4. **Rust native modules** — the `lucid-native/` skeleton exists but no Rust has been written; high-performance scanning engines (filesystem traversal, SHA-256 hashing, process enumeration) are planned here
5. **AppServices DI migration** — the static 80+-singleton service locator will need DI container migration as the service count grows; incremental migration via `IServiceProvider` shim preferred

---

# The Long-Term Vision

Lucid is becoming:

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
- adaptive learning that improves over time

The most important differentiator should remain:

> Explainability + Trust.
