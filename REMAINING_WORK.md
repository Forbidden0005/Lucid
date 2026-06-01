# Lucid — Remaining Work
*Generated 2026-06-01 from full codebase inspection. Derived from CURRENT_STATE.md gaps.*

---

## In Progress (Background Workers)

These are already being worked on by spawned agents:

| # | Item | Worker | Status |
|---|------|--------|--------|
| 1 | Wire `NativeScannerService` into `StorageAnalysisService` as fast-path for large-file detection | Active | No PR yet |
| 2 | Migrate `OperationHistoryService` from JSON → SQLite (`operation_history` table, Schema v2) | Active | No PR yet |

---

## High Priority

### H1 — Extend Replay & Analytics Beyond 30-Minute Window
**Impact: High** — Replay and Historical Analytics are both currently capped at the 30-minute in-memory telemetry buffer. The SQLite schema already has `telemetry_samples`, `timeline_events`, and `insight_history` tables. The query path just needs to be implemented.

**What's needed:**
- `HistoricalAnalyticsEngine`: query `telemetry_samples` for 24h/7d/30d windows when `_history` buffer is insufficient
- `OperationalReplayService`: read from `timeline_events` and `telemetry_samples` when replaying outside the 30-min window
- `ReplayViewModel`: enable full session/daily replay, not just recent 30 min
- `HistoricalViewModel`: enable 7-day and 30-day health trend views with real data

**Files to change:**
- `Services/Analytics/HistoricalAnalyticsEngine.cs`
- `Services/Replay/OperationalReplayService.cs`
- `Services/Persistence/HistoricalTelemetryRepository.cs` (add time-range query methods)
- `ViewModels/HistoricalViewModel.cs`
- `ViewModels/ReplayViewModel.cs`

**Blocked by:** Nothing — SQLite schema already exists.

---

### H2 — SettingsPage MVVM Refactor
**Impact: Medium** — SettingsPage is the only page that uses thick code-behind instead of MVVM. It directly accesses AppServices, writes to the registry, and manages state inline. This violates the architecture pattern used everywhere else and makes it untestable.

**What's needed:**
- Create `SettingsViewModel.cs` with observable properties for all settings
- Bind `ISettingsService.CurrentSettings` as the source of truth
- Move registry access (AutoStart) into a thin executor or service wrapper
- Update `SettingsPage.xaml.cs` to the thin code-behind pattern

**Files to change:**
- `Views/SettingsPage.xaml.cs` (thin down)
- `Views/SettingsPage.xaml` (bind to VM)
- New: `ViewModels/SettingsViewModel.cs`

---

### H3 — NativeScannerService → StorageAnalysisService Integration
*(Already in progress via background worker — listed here for visibility)*

**Impact: High** — The Rust scanner is ~3× faster than the C# BFS scanner for large-file detection. The DLL is present and P/Invoked. The integration point is `StorageAnalysisService.RunScan()` Phase 1. If native fails, falls back to C# automatically.

---

### H4 — OperationHistory SQLite Migration
*(Already in progress via background worker — listed here for visibility)*

**Impact: Medium** — Consolidates the last JSON-backed service into SQLite. Adds proper indexed queries and removes the separate JSON file.

---

## Medium Priority

### M1 — Historical Telemetry Downsampling
**Impact: Medium** — The telemetry downsampler (`_downsampleTimer`) inserts into `telemetry_samples` but the resolution strategy (1-min averages → 5-min averages → hourly averages) needs to be fully implemented for long-term retention without unbounded growth.

**What's needed:**
- Implement resolution-based downsampling in the flush timer
- Add `CleanupOldSamples()` to keep db size bounded (e.g., keep 1-min res for 24h, 5-min res for 7d, hourly for 30d)

**Files:** `AppServices.cs` (downsample timer), `Services/Persistence/HistoricalTelemetryRepository.cs`

---

### M2 — Privacy Write-Back
**Impact: Medium** — `PrivacyPermissionScanner` reads ConsentStore but cannot modify per-app grants. Users can only view permissions, not revoke them from within Lucid.

**What's needed:**
- `PrivacyPermissionWriter` service wrapping `Windows.Security.Authorization.AppCapabilityAccess` or registry writes
- UI toggle in PrivacyPage per app entry
- Full consent gating before any write

**Files:** `Services/Privacy/`, `Views/PrivacyPage.xaml`, `ViewModels/PrivacyViewModel.cs`

---

### M3 — Companion Snap-to-Edge Preference Persistence
**Impact: Low-Medium** — The companion overlay resets position on restart. The `CompanionSessionManager` has the state model but doesn't persist the snap-to-edge preference.

**What's needed:**
- Add `LastDockEdge` and `LastPosition` to `AppSettings`
- Save on window move/snap
- Restore on `CompanionOverlayWindow` launch

**Files:** `Services/Settings/AppSettings.cs`, `Services/Companion/CompanionSessionManager.cs`, `Views/CompanionOverlayWindow.xaml.cs`

---

### M4 — Advanced Autonomous Workflow Patterns
**Impact: Medium** — `AutonomousWorkflowEngine` supports single-step and sequential workflows. Multi-branch workflows (conditional execution, parallel steps, compensating transactions) are architectural stubs.

**What's needed:**
- Implement conditional branching in `WorkflowExecutionPlanner`
- Implement parallel step coordinator in `TaskCompletionCoordinator`
- Add compensating transaction support in `WorkflowRollbackCoordinator`

**Files:** `Services/Autonomy/WorkflowExecutionPlanner.cs`, `TaskCompletionCoordinator.cs`, `WorkflowRollbackCoordinator.cs` (in Remediation)

---

### M5 — Operational Workflow Engine Depth
**Impact: Medium** — `OperationalWorkflowEngine` is at ~70% and bridges the autonomy and automation layers. The foundation exists but advanced workflow orchestration patterns are deferred.

---

## Low Priority / Future Phases

### L1 — Distributed Multi-Device Active Sync
**Impact: High (when multi-device)** — `LocalSyncCoordinator`, `DistributedTimelineAggregator`, `CrossMachineAnalyticsEngine`, and `TrustedDeviceRegistry` are all registered and architecturally complete. No actual inter-device communication protocol exists (no mDNS, no WCF, no QUIC, etc.).

**What's needed:**
- Choose and implement transport (mDNS discovery + TCP/TLS or named pipes for local network)
- `DistributedTimelineAggregator`: real merge from remote device snapshots
- `CrossMachineAnalyticsEngine`: real cross-device pattern analysis

---

### L2 — Tamper Recovery Workflow
**Impact: Low** — `TrustIntegrityService` detects HMAC tampering of settings. Detection is complete. Recovery path (what happens when tamper is detected at startup) is not implemented.

**What's needed:**
- Recovery branch in `AppServices.Initialize()` when integrity check fails
- `TrustPostureRecoveryManager` workflow: reset settings to defaults, notify user, restart clean

---

### L3 — More Executor Rollback Coverage
**Impact: Low** — 18/28 executors have no rollback. Many open-only executors (OpenTaskManager, OpenStartupApps, etc.) are inherently non-rollbackable. The real gaps are:
- `RecycleBinCleanupExecutor` — hard to rollback but could stage to backup
- `BrowserCacheCleanupExecutor` — could stage files
- `DeliveryOptimizationCacheExecutor` — could stage files

---

### L4 — Schema v2+ Migrations
**Impact: Low-Medium** — SQLite schema is at v1. Schema v2 (operation_history table) is pending the background worker. Future schemas should be planned for:
- Cognitive inference history (for longer calibration windows)
- Pattern intelligence persistence (cross-session pattern learning)
- Session snapshots (long-term machine behavior reconstruction)

---

### L5 — Process Relationship Graph Visualization
**Impact: Medium (user-facing)** — `ProcessIntelligenceService` tracks parent/child relationships and anomaly chains. No visual process tree exists. A zoomable process graph showing resource dominance, spawn chains, and restart loops would be highly visible.

---

### L6 — Telemetry Graph Enhancements
**Impact: Medium (user-facing)** — Dashboard telemetry shows live sparklines. Extended historical charts (zoomable, with event markers, baseline bands, forecast overlays) would significantly improve operational clarity.

---

## Summary Table

| Priority | Item | Effort | Blocked? |
|----------|------|--------|---------|
| In Progress | Native scanner storage integration | Small | No |
| In Progress | OperationHistory → SQLite | Small | No |
| H1 | Replay/Analytics beyond 30-min | Medium | No |
| H2 | SettingsPage ViewModel refactor | Small | No |
| H3 | (Same as In Progress) | — | — |
| H4 | (Same as In Progress) | — | — |
| M1 | Telemetry downsampling strategy | Small | No |
| M2 | Privacy write-back | Medium | No |
| M3 | Companion position persistence | Tiny | No |
| M4 | Advanced workflow branches | Large | No |
| M5 | Workflow engine depth | Medium | No |
| L1 | Distributed active sync | Very Large | No |
| L2 | Tamper recovery workflow | Small | No |
| L3 | More executor rollback | Medium | No |
| L4 | Schema v2+ planning | Small | No |
| L5 | Process relationship graph | Large | No |
| L6 | Extended telemetry graphs | Large | No |
