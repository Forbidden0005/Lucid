# Lucid — New Roadmap
*Generated 2026-06-01. Based on actual codebase state, not prior roadmap claims.*

---

## Where We Are

Lucid is a production-grade local-first operational intelligence platform for Windows with:

- **116 registered services** — all initialized, none stubbed
- **21 navigable pages** — all wired with real ViewModels
- **25 intelligence rules** running deterministically on every telemetry cycle
- **6 cognitive inference rules** synthesizing higher-level operational context
- **28 action executors** with dry-run, privilege detection, and 36% rollback coverage
- **Fully autonomous watchtower** running 30-minute governance-aware analysis cycles
- **Complete conversation engine** handling 25 intent types deterministically
- **Real Ollama/LLM integration** — local-only, streaming, conversation history
- **SQLite persistence** in WAL mode with batched write queues and health monitoring
- **Adaptive learning** — empirical outcome tracking and per-action effectiveness profiling

The platform is not a prototype. It is a functioning operational intelligence system. The next phases are about **deepening** existing capabilities, **extending time horizons**, and **building the surface features** that make the intelligence visible.

---

## Guiding Principles (Unchanged)

- **Local-first** — no cloud, no telemetry sent out, no external APIs
- **Confidence-aware** — all outputs express uncertainty; nothing claims certainty
- **Deterministic** — same inputs always produce the same outputs
- **Reversible** — destructive actions require staging, consent, and rollback paths
- **Transparent** — every conclusion traces to evidence; no black-box outputs
- **Non-alarmist** — probabilistic language only; never fear-based copy

---

## Phase 23 — Time Horizon Expansion

**Theme:** Make the platform's memory longer. Right now most analysis is bounded to the 30-minute in-memory rolling buffer. SQLite is live and has the schema. The query path just needs to be built.

### 23.1 — Extended Replay (Priority: High)

**Goal:** Allow users to replay any point in the last 24 hours, not just the last 30 minutes.

**Work:**
- `HistoricalTelemetryRepository`: add `GetSamplesInRange(from, to, resolution)` query
- `OperationalReplayService`: fall through to SQLite when requested timestamp is outside the in-memory buffer
- `ReplayViewModel`: expose full session / 1h / 4h / 24h window selectors
- `ReplayPage`: update time window UI to include longer windows

**Success metric:** User can scrub to "2 hours ago" and see full system state reconstruction.

---

### 23.2 — Extended Historical Analytics (Priority: High)

**Goal:** Enable 24h, 7d, 30d trend analysis on the Historical page.

**Work:**
- `HistoricalAnalyticsEngine`: query `telemetry_samples` for long-horizon windows when in-memory buffer is insufficient
- `HistoricalViewModel`: enable 7-day and 30-day health score trend charts
- `HistoricalPage`: render multi-week health trajectory

**Success metric:** HistoricalPage shows "past 7 days" health timeline with pattern annotations.

---

### 23.3 — Telemetry Downsampling Strategy (Priority: Medium)

**Goal:** Keep the SQLite database bounded as months of data accumulate.

**Retention policy:**
- 1-minute resolution: retain 24 hours
- 5-minute resolution: retain 7 days
- 1-hour resolution: retain 90 days

**Work:**
- Implement `resolution`-aware downsampling in the existing `_downsampleTimer`
- Add `HistoricalTelemetryRepository.CleanupOldSamples()` with resolution-based retention
- Run cleanup on a weekly timer (not every flush)

---

### 23.4 — SQLite Schema v2 (Priority: Medium)

**Goal:** Consolidate remaining JSON stores into SQLite and prepare schema for cognitive data.

**Work:**
- Schema v2: `operation_history` table (background worker in progress)
- Schema v3: `cognitive_inferences` table — persist inference history for longer calibration windows
- Schema v4: `operational_patterns` table — persist detected patterns across sessions

**Success metric:** Zero JSON files for operational data. All persistence in SQLite.

---

## Phase 24 — Surface Polish & Architecture Cleanup

**Theme:** Fix the rough edges. The intelligence is excellent but a few surfaces need cleanup before they can be called production-quality.

### 24.1 — SettingsPage ViewModel Refactor (Priority: High)

**Goal:** Bring SettingsPage into the same MVVM pattern as every other page.

**Work:**
- Create `SettingsViewModel.cs` with `[ObservableProperty]` for all settings
- Bind all toggles, combos, and text inputs to VM properties
- Move registry access (AutoStart) to a thin service wrapper
- Thin down `SettingsPage.xaml.cs` to navigation + animation only

---

### 24.2 — Companion Position Persistence (Priority: Low)

**Goal:** Companion overlay remembers its last position and dock edge.

**Work:**
- Add `LastCompanionPosition` and `LastDockEdge` to `AppSettings`
- Save on move/snap in `CompanionOverlayWindow`
- Restore on launch via `CompanionSessionManager`

---

### 24.3 — Privacy Write-Back (Priority: Medium)

**Goal:** Allow users to revoke per-app permission grants directly from PrivacyPage.

**Work:**
- Implement `PrivacyPermissionWriter` using `Windows.Security.Authorization.AppCapabilityAccess` or ConsentStore registry writes
- Add per-app toggle to PrivacyPage
- Gate all writes through the consent/trust pipeline

---

### 24.4 — Additional Executor Rollback Coverage (Priority: Low)

**Goal:** Increase rollback coverage from 36% to ~50%+.

**Candidates:**
- `RecycleBinCleanupExecutor` — stage to `%LOCALAPPDATA%\Lucid\Rollback\RecycleBin\` before emptying
- `BrowserCacheCleanupExecutor` — stage browser profile cache before deletion
- `DeliveryOptimizationCacheExecutor` — stage DO cache entries

---

## Phase 25 — Cognitive Depth

**Theme:** Make the cognitive layer smarter by giving it a longer memory and letting patterns drive inference confidence adjustments.

### 25.1 — Cross-Session Pattern Learning

**Goal:** Patterns detected by `PatternIntelligenceEngine` survive app restarts. Currently patterns are rebuilt from in-memory reasoning memory on each launch.

**Work:**
- `PatternIntelligenceEngine`: persist detected patterns to SQLite `operational_patterns` table
- Load patterns at startup; merge with newly detected in-memory patterns
- `ReasoningPage`: show "observed across N sessions" for long-running patterns

---

### 25.2 — Calibration Depth

**Goal:** `ConfidenceCalibrationEngine` currently adjusts based on short history. With SQLite cognitive history, it can calibrate over months of inferences.

**Work:**
- `HistoricalAccuracyTracker`: read from `cognitive_inferences` table for long-horizon calibration
- Per-machine calibration profiles that improve over weeks, not just the current session

---

### 25.3 — Inference Rule Expansion

**Goal:** Add 3–5 new inference rules to `CognitiveReasoningEngine` covering gaps.

**Candidates:**
- `MemoryLeakPatternRule` — detects process-specific memory creep correlated with rising RAM pressure
- `StartupRegression Rule` — detects worsening boot time over days/weeks using longitudinal baselines
- `NetworkContention Rule` — correlates disk I/O with network activity for backup/sync processes
- `RecurringThermalWindowRule` — detects time-of-day thermal patterns (e.g., always hot at 3pm)
- `BatteryDrainAccelerationRule` — detects above-baseline battery drain for mobile workloads

---

## Phase 26 — Visual Intelligence

**Theme:** Surface the operational intelligence in richer visual formats. The data exists; it needs better presentation.

### 26.1 — Process Relationship Graph

**Goal:** Visual parent/child process tree showing resource dominance, spawn chains, and anomaly clusters.

**Work:**
- Extend `ProcessIntelligenceService` to track full parent/child lineage
- Build `ProcessGraphViewModel` with node/edge model
- New `ProcessGraphControl.xaml` — zoomable canvas with D3-style layout
- Integrate into ProcessesPage as a "Graph" tab alongside the existing list

---

### 26.2 — Extended Telemetry Charts

**Goal:** Dashboard and InsightDetail charts become zoomable with baseline bands, event markers, and forecast overlays.

**Work:**
- `InsightDetailChart`: extend existing Polyline control to support baseline bands (mean ± 1σ) and event markers
- Dashboard: add optional "expand" view per metric with 24h zoom
- Forecast overlays: project trend line beyond current time

---

### 26.3 — Storage Treemap

**Goal:** Visual storage breakdown by category and directory, sized by bytes.

**Work:**
- After scan, build treemap data model from `StorageAnalysisResult`
- New `StorageTreemapControl.xaml` using nested Borders sized by proportional area
- Add as "Map" tab to StoragePage

---

## Phase 27 — Distributed Intelligence

**Theme:** Make Lucid aware of multiple machines on the local network. Architecture is already in place.

### 27.1 — Local Network Discovery & Sync

**Goal:** Trusted devices on the same local network can share operational state with each other.

**Work:**
- Implement mDNS/DNS-SD for local device discovery in `LocalSyncCoordinator`
- TCP/TLS transport for snapshot exchange (or named pipes for same-machine multi-session)
- `DistributedTimelineAggregator`: real merge from remote snapshots
- `TrustedDeviceRegistry`: pairing flow with TOTP or local-only QR code
- `DeviceIntelligencePage`: show real cross-device data

**Constraint:** All sync must remain local-network only. `LocalEndpointValidator` must reject any non-LAN sync endpoint.

---

### 27.2 — Cross-Machine Pattern Analysis

**Goal:** Identify patterns that affect multiple machines simultaneously (common root cause, shared startup apps, same network congestion).

---

## Phase 28 — Native Engine Expansion

**Theme:** Expand what the Rust native module covers.

### 28.1 — Native SHA-256 Duplicate Hashing

**Goal:** Replace `DuplicateDetectionService`'s C# MD5 hashing with a Rust SHA-256 implementation. SHA-256 provides collision resistance; Rust provides throughput.

**Work:**
- Add `lucid_hash_file(path) → [u8; 32]` export to `lucid-native/`
- Expose via `LucidNativeInterop`
- Replace `DuplicateDetectionService` hash path with native when available

---

### 28.2 — Native Process Enumeration

**Goal:** Replace `Process.GetProcesses()` with a Rust implementation using `NtQuerySystemInformation` for faster, lower-overhead process telemetry.

**Work:**
- Add `lucid_enumerate_processes() → Vec<ProcessSnapshot>` to native module
- Wire into `ProcessSampler` as fast path

---

## What This Roadmap Explicitly Excludes

These will not be built regardless of request:

- **Cloud sync or telemetry upload** — Lucid is local-first. Period.
- **LLM auto-remediation** — The LLM chat assists; it does not execute actions autonomously
- **Registry "optimization" cleaners** — Not that kind of platform
- **Antivirus-style threat detection** — We surface signals; we don't claim certainty about security
- **Aggressive auto-remediation** — All actions require explicit user consent or trust-level governance
- **"One-click fix everything" modes** — Reversibility and transparency are non-negotiable

---

## Phase Summary

| Phase | Theme | Key Deliverables | Effort |
|-------|-------|-----------------|--------|
| 23 | Time Horizon Expansion | Extended replay (24h), extended analytics (7d/30d), downsampling, Schema v2-v4 | Medium |
| 24 | Surface Polish | SettingsPage VM, companion persistence, privacy write-back, executor rollback | Small–Medium |
| 25 | Cognitive Depth | Cross-session patterns, calibration depth, new inference rules | Medium |
| 26 | Visual Intelligence | Process graph, extended charts, storage treemap | Large |
| 27 | Distributed Intelligence | LAN discovery, device sync, cross-machine patterns | Very Large |
| 28 | Native Engine Expansion | SHA-256 hashing, native process enumeration | Medium |

---

## Next Immediate Action

**Phase 23 is the right next move.** It requires no new architecture — the SQLite schema already exists, the query repositories already have the shape, and the gap is purely "write the query methods." It unlocks the replay, historical analytics, and calibration subsystems that are currently artificially bounded at 30 minutes, and closes the most visible gap in the platform.

Start with **Phase 23.1 (Extended Replay)** and **Phase 23.2 (Extended Historical Analytics)** in parallel — they share the same `HistoricalTelemetryRepository` work.
