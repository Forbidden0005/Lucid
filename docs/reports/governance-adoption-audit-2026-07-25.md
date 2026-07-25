# Governance Adoption Audit

Snapshot 2026-07-25 — feeds the Phase-5-pulled-forward governance work; supersedes nothing.

Scope: `lucid-desktop/Lucid.App/Services/Governance/` contract review + full inventory of self-scheduled background work under `Services/`, `Infrastructure/` (lives at `Services/Infrastructure/`), and `AppServices.cs`. All paths relative to `lucid-desktop/Lucid.App/`.

---

## 1. What the Governance subsystem actually does

### Contract (as implemented)

- **`RuntimeGovernanceService`** (`Services/Governance/RuntimeGovernanceService.cs`) subscribes to `ITelemetryService.ReadingAvailable` (no timer of its own). On each reading it queries `PowerManager.BatteryStatus`, runs `RuntimePressureAnalyzer.Analyze`, applies 3-sample hysteresis (recovery to `Normal` is immediate), and on a mode change: (1) `ConcurrencyBudget.UpdateMaxBackground`, (2) `PollingCoordinator.ApplyMode`, (3) raises `ModeChanged` on the UI `DispatcherQueue`, (4) drains the queue when the new mode is `Normal`.
- **`ConcurrencyBudget`** — **enforced**, but only for callers that opt in via `TryAcquireSlot`/`ReleaseSlot`. Per-category hard limits (1 slot each for ActionExecution, StorageScan, DuplicateHashing, HistoricalAnalytics, LearningAnalysis, ReplayAnalysis) plus a mode-driven background ceiling (Normal=3, HighLoad=1, LowPower/Gaming/Thermal=0). Foreground bypasses the ceiling. IdleOnly work counts against the background ceiling (`priority >= Background`).
- **`AdaptiveSchedulingPolicy`** — pure static policy table (mode → telemetry/process intervals, max background, `AllowIdleOnlyWork` = Normal only). Enforced only insofar as its consumers (`RuntimeGovernanceService.TryAcquireSlot` idle-only gate, `PollingCoordinator`) call it.
- **`PollingCoordinator`** — **enforced for exactly one target.** `RegisterTarget` is called once: `AppServices.cs:1254` registers `WindowsTelemetryService`. Mode changes push new intervals; the telemetry loop applies them on its next iteration.
- **`WorkloadClassifier`** — static category→priority map. Authoritative, side-effect free.
- **`RuntimePressureAnalyzer`** — pure static: snapshot + battery flag → (mode, reason flags). Thresholds: CPU ≥ 75%, GPU ≥ 68% with CPU ≥ 25% (gaming), temp ≥ 87 °C, disk ≥ 85%. Mode priority: Thermal > Gaming > LowPower > HighLoad > Normal. Note: `DiskPressure` sets a reason flag but **never influences the mode** — it is observability-only.

### Consumers today (grep-verified)

| Consumer | What it uses | Enforcement |
|---|---|---|
| `Services/Execution/GovernanceAwareExecutionEngine.cs:80` | `TryAcquireSlot(ActionExecution)` around every non-navigation executor; releases in `finally`. Rollbacks bypass governance by design. | Hard (blocks execution with explanation) |
| `Services/Cleanup/RollbackStagingMaintenanceService.cs:119` | `TryAcquireSlot(RollbackMaintenance)` (IdleOnly) per 6-hour sweep; skips sweep on refusal | Hard |
| `Services/WindowsTelemetryService.cs:201` | `IAdaptiveTelemetryTarget` — receives interval pushes | Hard (interval change) |
| `Services/Watchtower/OperationalWatchtowerService.cs` / `ProactiveRecommendationCoordinator.cs` | Reads `CurrentMode`, skips pass in Thermal/Gaming | **Advisory only** — no slot |
| `Services/Remediation/AutonomousRemediationService.cs:200`, `WorkflowExecutionCoordinator.cs:97` | `CurrentMode` fed into `SafetyConstraintEngine` pre-checks; actual steps run through the governed execution engine | Advisory + governed inner |
| `Services/Diagnostics/RecoveryCoordinator.cs:194` | `PollingCoordinator.ApplyMode(Normal)` as a recovery action | Direct |
| `Services/Diagnostics/InternalDiagnosticsService.cs`, `ViewModels/RuntimeGovernanceViewModel.cs`, `ViewModels/SettingsViewModel.cs`, `AppServices.cs:1363` | Snapshots / `ModeChanged` for display and logging | Observability |
| `Services/Simulation/OperationalSimulationEngine.cs` | Injects `IRuntimeGovernanceService` (constructor dep) | Injected, minimal use |

### Dead / unwired parts

1. **`ExecutionPriorityQueue.Enqueue` is never called anywhere.** The only callers of the queue are `RuntimeGovernanceService` (`Drain`/`Clear`/`GetSnapshot`) and the governance UI. The queue is permanently empty; every `Drain()` is a no-op; the deferred-retry mechanism described in its doc comment does not exist in practice. Refused workloads are simply dropped (executor: hard-fail with message; rollback sweep: skip until next 6 h tick).
2. **11 of 13 `WorkloadCategory` values are never acquired.** Only `ActionExecution` and `RollbackMaintenance` reach `TryAcquireSlot`. `StorageScan`, `DuplicateHashing`, `HistoricalAnalytics`, `LearningAnalysis`, `ExplainReasoning`, `ReplayAnalysis`, `TelemetrySampling`, `ProcessIntelligence`, `NarrativeGeneration`, `TimelineAggregation`, `InsightAnalysis` exist only in the enum/classifier — the per-category limits on hashing and scans enforce nothing today.
3. **`IAdaptiveTelemetryTarget`'s "0 = pause" contract is not implemented.** `WindowsTelemetryService.SetTelemetryInterval` (`:201`) clamps to ≥ 500 ms; a zero interval would poll at 500 ms, not pause. Latent — `AdaptiveSchedulingPolicy` never emits zero — but the interface doc promises pause semantics no implementation honors.
4. **`Services/Native/NativeScannerService.cs` is referenced by nothing** except `LucidNativeInterop.cs` — its `Task.Run` scan entry points have no callers.
5. **Doc drift:** `ProactiveRecommendationCoordinator.RefreshAsync` doc says "only runs in Normal or LowPower modes" but the code only skips Thermal/Gaming — it also runs in HighLoad.
6. **`ConcurrencyBudget.Release` is not actually a safe no-op** for unmatched releases: the guards (`> 0` checks) prevent negative counts, but a release for a category that was never acquired *while other work is active* decrements `_usedBackground` belonging to someone else. Also, MD5 (not SHA-256, contra CLAUDE.md) is the duplicate-hash algorithm (`Services/Storage/DuplicateDetectionService.cs`).

---

## 2. Site inventory — self-scheduled background work

Governance routing legend: **Governed** = acquires a slot or is interval-controlled by PollingCoordinator; **Advisory** = reads `CurrentMode` only; **None** = no governance contact. Flags: 💾 disk-heavy, 🔥 CPU-heavy, 🌐 network.

### 2a. Periodic timers and long-running loops

| # | Site | Work | Cadence / trigger | Routing | Proposed class | Flags |
|---|---|---|---|---|---|---|
| 1 | `Services/WindowsTelemetryService.cs:86,120` | Telemetry poll loop (`Task.Run` + `Task.Delay`), samplers + process list | 1.5 s default, mode-adjusted 1.5–6 s | **Governed** (PollingCoordinator target) | Background | — |
| 2 | `Services/Cleanup/RollbackStagingMaintenanceService.cs:95` | Rollback staging sweep (`PeriodicTimer`) | 45 s after start, then every 6 h | **Governed** (`RollbackMaintenance` slot) | IdleOnly (as-is) | 💾 |
| 3 | `Services/Watchtower/OperationalWatchtowerService.cs:79` | Watchtower cycle timer → `ProactiveRecommendationCoordinator.RefreshAsync` → `HistoricalAnalyticsEngine` + learning analysis on threadpool | 90 s after start, then every 30 min | **Advisory** (skips Thermal/Gaming only; no `HistoricalAnalytics`/`LearningAnalysis` slot) | IdleOnly | 🔥💾 (SQLite reads, analytics) |
| 4 | `AppServices.cs:1881` | Hourly telemetry downsample + retention purge (`System.Threading.Timer` → `Task.Run` → `DownsampleAndPurgeAsync`) | Every 1 h | **None** | IdleOnly | 💾 (SQLite churn) |
| 5 | `Services/Persistence/SQLitePersistenceService.cs:152` | Write-queue batch flush (`Timer`) | Every 30 s | **None** | Background (must never be paused — data-loss risk; exempt, don't migrate to slot model) | 💾 (light) |
| 6 | `Services/Diagnostics/InternalDiagnosticsService.cs:315` | Self-monitor tick (`Timer`) | Every 30 s | **None** | Background | — |
| 7 | `Services/DesktopContext/DesktopContextService.cs:62` | Active-window/clipboard poll (`Timer`), consent-gated (off by default, `AppServices.cs:1704`) | Every 1.75 s | **None** | Background | — |
| 8 | `Services/DesktopContext/ExplorerContextProvider.cs:23` | Dedicated STA `Thread`, 2 s wake loop, Explorer COM queries; child of #7, consent-gated | ≤ every 2 s | **None** | Background | — |
| 9 | `Services/Distributed/LocalSyncCoordinator.cs:94-97,203,363` | 4 loops: UDP beacon send (30 s), UDP receive, TCP data listener, sync loop (60 s); opt-in gated (`AppServices.cs:1481`) | Continuous while enabled | **None** | Background | 🌐 |
| 10 | `Services/MockTelemetryService.cs:37` | 1 s mock telemetry `Timer` — **no consumers found; apparent dead code** | 1 s | **None** | n/a (dev-only/dead) | — |

### 2b. Fire-and-forget / one-shot `Task.Run`

| # | Site | Work | Trigger | Routing | Proposed class | Flags |
|---|---|---|---|---|---|---|
| 11 | `Services/Storage/StorageAnalysisService.cs:82` | Full-drive traversal + category/large-file analysis + **MD5 duplicate hashing** (`DuplicateDetectionService`) + near-duplicate pass | User click (Storage page, `ViewModels/StorageViewModel.cs:275`) | **None** | Acquire `StorageScan` + `DuplicateHashing` slots (user-initiated, so admit in any mode, but the cat-limit-1 prevents double scans and collisions become visible) | 💾🔥 |
| 12 | `Services/Security/SecurityIntelligenceService.cs:74` | Security scan: startup trust, signature verification, Defender status | User click (`ViewModels/SecurityViewModel.cs:193`) | **None** | Foreground w/ new category (or `ActionExecution`-style slot) | 💾 |
| 13 | `Services/Remediation/RemediationOutcomeValidator.cs:76` | `_ = Task.Run(() => _learning.AnalyzePendingActionsAsync())` — learning pass after each remediation | Per remediation outcome | **None** (category `LearningAnalysis` exists, unused) | IdleOnly (`LearningAnalysis` slot) | 🔥 |
| 14 | `AppServices.cs:1216` | One-shot startup sweep: close orphaned insight rows (SQLite) | App start | **None** | Background (one-shot; fine) | 💾 (light) |
| 15 | `AppServices.cs:1903` | One-shot health-analytics pre-warm (`ComputeReportAsync`, 7-day aggregates) | App start | **None** | Background; lands in the startup contention window — consider deferring behind governance | 💾 |
| 16 | `Services/Baseline/SystemBaselineService.cs:168` | Periodic baseline DTO write to disk | Every Nth telemetry tick | **None** | Background (tiny write; fine) | — |
| 17 | `Services/Autonomy/WorkflowCheckpointManager.cs:144` | Fire-and-forget checkpoint JSON write | Per checkpoint | **None** | Background (fine) | — |
| 18 | `Services/Infrastructure/Events/LucidEventBus.cs:82` | `Task.Run` per FireAndForget event dispatch | Per event | **None** | Infrastructure (fine) | — |
| 19 | `Services/Infrastructure/Startup/StartupTimeoutGuard.cs:49` | `Task.Run` wrapping a startup init step with timeout | App start, one-shot | **None** | Foreground init (fine) | — |
| 20 | `Services/Remediation/AutonomousRemediationService.cs:234` | Workflow execution on threadpool | User-approved workflow | Inner steps **Governed** (execution engine); mode pre-check | Foreground | — |
| 21 | `Services/Native/NativeScannerService.cs:80,89` | Rust scanner entry points (`Task.Run`) | **No callers** | **None** | n/a (unwired) | 💾 |
| 22 | `Services/Reasoning/OperationalEvidenceGraph.cs:116` | Evidence-chain build | User (Explain) | **None** (`ExplainReasoning` category unused) | Foreground | 🔥 (light) |
| 23 | `Services/Simulation/OperationalSimulationEngine.cs:78` | Simulation compute | User (Simulation page) | Injected, minimal use | Foreground | 🔥 (light) |
| 24 | `Services/VisualContext/VisualContextService.cs:114-190`, `ScreenCaptureCoordinator.cs:119` | Consent-gated screen capture + interpretation | User/consented request | **None** | Foreground | — |
| 25 | `Services/Companion/OperationalConversationEngine.cs:57-66` | Chat response composition | User query | **None** | Foreground (fine) | — |

### 2c. `Task.Delay` pacing (not polling loops — benign)

`Services/Automation/ApplicationLaunchAutomation.cs:127,145`, `AutomationOrchestrator.cs:265,365`, `ExplorerAutomationService.cs:59,87,122`, `Services/Autonomy/TaskCompletionCoordinator.cs:184,281` — 200–400 ms UI/step pacing inside user-approved automation flows. Foreground, no migration needed. `LocalSyncCoordinator.cs:355` (8 s retry backoff) belongs to site #9.

### 2d. Executor `Task.Run` sites — governed, not counted as ungoverned

All `Services/Execution/Executors/*.cs` `Task.Run` sites (27 executors: DISM, SFC, temp-file cleanup, recycle bin, WU cache, delivery optimization, browser cache, duplicate/large-file delete, startup enable/disable/backup/restore, network resets, terminate process, open-* navigation, rollbacks) execute under `GovernanceAwareExecutionEngine`, which holds the `ActionExecution` slot (limit 1) for the duration — Foreground, governed. Navigation-only actions and rollbacks intentionally bypass the slot. `Services/Repair/ProcessExecutionHelper.cs` runs inside these.

---

## 3. Prioritized migration list

### High priority (heavy, can collide with executor runs)

1. **Storage scan + duplicate hashing** (#11) — full-drive traversal + MD5 hashing with zero governance contact. Can run concurrently with a DISM/SFC executor (different category, no cross-signal) and with itself only by luck of the `IsScanning` flag. Acquire `StorageScan` and (phase 2 of the scan) `DuplicateHashing` slots; both categories and their limit-1 rules already exist.
2. **Watchtower 30-min analytics cycle** (#3) — routes `HistoricalAnalyticsEngine` + learning work through a bare `Task.Run` with only a Thermal/Gaming advisory check (also runs under HighLoad/LowPower, contradicting its own docs). Acquire `HistoricalAnalytics` slot; fix mode check or rely on the IdleOnly gate.
3. **Hourly downsample/purge timer** (`AppServices.cs:1881`, #4) — recurring SQLite churn owned by an anonymous lambda in AppServices. Extract to a governed service (model: `RollbackStagingMaintenanceService`), classify IdleOnly.
4. **Post-remediation learning pass** (#13) — fire-and-forget CPU work right after a remediation, exactly when the user is watching outcome telemetry; `LearningAnalysis` category exists and is unused.
5. **Security scan** (#12) — signature verification walks disk; at minimum take a slot so it serializes against storage scans and shows in the governance page.

### Medium

6. **LocalSyncCoordinator loops** (#9) — opt-in and network-bound, but 4 always-on loops once enabled; register for mode awareness (pause sync loop on LowPower).
7. **Health-analytics pre-warm** (#15) — one-shot but lands in the startup contention window alongside #14 and telemetry warm-up.
8. **DesktopContext poll + STA thread** (#7/#8) — 1.75 s cadence with no adaptive slow-down; make it an `IAdaptiveTelemetryTarget` or mode-aware.

### Trivial / leave alone

- SQLite flush timer (#5 — pausing risks data loss; keep exempt, document the exemption), diagnostics self-monitor (#6), baseline DTO write (#16), checkpoint persist (#17), event bus (#18), startup guard (#19), orphan-row sweep (#14), all §2c pacing delays, all §2d executor sites.
- **Delete, don't migrate:** `MockTelemetryService` (#10, no consumers) and decide the fate of `NativeScannerService` (#21, unwired) — flag to owner rather than governing dead code.

---

## 4. Unit-test plan for the Governance subsystem

Current coverage: **zero test files** reference `Services/Governance/`. The test project (`lucid-desktop/Lucid.Tests/Lucid.Tests.csproj`) links production sources via explicit `<Compile Include="..\Lucid.App\Services\...">` entries — add the Governance `.cs` files the same way. Idiom: xUnit + FluentAssertions + Moq; `TestInfrastructure/DispatcherQueueStub.cs` already provides a synchronous `Microsoft.UI.Dispatching.DispatcherQueue` stand-in (namespace-shadowed), which unblocks `RuntimeGovernanceService`'s constructor.

### WorkloadClassifier (pure static — trivial)
- Every enum value maps to the doctrine priority (Foreground: ActionExecution/ExplainReasoning/ReplayAnalysis; Background: the 5 sampling/synthesis categories; IdleOnly: the 5 heavy categories).
- Unknown/default value falls back to Background.
- `GetDisplayName` returns non-empty for every defined value.

### AdaptiveSchedulingPolicy (pure static)
- Interval table per mode matches documented values (telemetry 1.5/3/6/4/2.5 s; process 4.5/9/20/15/12 s).
- `GetMaxConcurrentBackground`: Normal=3, HighLoad=1, LowPower/Gaming/Thermal=0.
- `AllowIdleOnlyWork` true only for Normal.

### RuntimePressureAnalyzer (pure static)
- Each threshold boundary (CPU 75, GPU 68 + CPU floor 25, temp 87 with/without `CpuTemperatureAvailable`, disk 85, battery flag) sets exactly its reason flag.
- Mode precedence: Thermal > Gaming > LowPower > HighLoad; combined-reason snapshots resolve to the highest.
- **DiskPressure alone yields `Normal` mode** — pin the current (surprising) behavior or change it deliberately.
- `DescribeReasons`: none / single / multi ("and" joining) formatting.
- Needs a construct-able `TelemetrySnapshot` (`Lucid.Helpers`) — verify it links cleanly; it's a plain record.

### ConcurrencyBudget (instance, lock-based — no time/thread seams needed)
- Per-category limit: second `TryAcquire(StorageScan)` refused with populated `refusalReason`; release then re-acquire succeeds.
- Background ceiling: with max=3, fourth Background acquire refused; Foreground acquires always succeed past the ceiling.
- IdleOnly counts against the background ceiling (`priority >= Background`) — pin it.
- IdleOnly with `_maxBackground == 0` refused with the "paused" reason even when nothing is active.
- `UpdateMaxBackground` mid-flight: lowering below current usage refuses new work but doesn't evict; raising re-admits.
- **Unmatched-release bug:** `Release` for a never-acquired workload while another Background workload is active decrements the shared `_usedBackground` counter, allowing over-admission. Write the failing test first; fix is to only decrement when a matching `_activeList` entry existed.
- `GetActiveWorkloads` returns a copy; `GetSnapshot` reflects mode/reasons/slots; `GetActiveCount` per category.
- Thread-safety smoke: parallel acquire/release keeps counts ≥ 0 and ≤ limits.

### ExecutionPriorityQueue (instance)
- Enqueue null-arg guards; `Count`.
- `Drain` invokes callbacks in priority order (Foreground → Background → IdleOnly), FIFO within a class, and empties the queue.
- Expiry: entry with `DeferredAt` > 30 min old is dropped without callback. **Testability blocker:** `MaxEntryAge` compares against `DateTimeOffset.Now` — but `DeferredAt` is caller-supplied, so tests can backdate entries; no clock seam strictly required (a `TimeProvider` would still be cleaner).
- A throwing callback doesn't prevent later callbacks.
- `Drain` fire-and-forgets (`_ = cb()`) — async callback completion is unobservable; test with synchronously-completing callbacks and a signal.
- `Clear` discards everything.
- Note for the migration work: until something calls `Enqueue`, these tests document a mechanism the app never exercises.

### PollingCoordinator (instance)
- `RegisterTarget` immediately pushes current intervals to a mock `IAdaptiveTelemetryTarget`; duplicate registration doesn't double-notify.
- `ApplyMode` pushes only-changed values (Normal→Normal: no calls; Normal→HighLoad: both change).
- A throwing target doesn't block others.
- Race note: `RegisterTarget` reads `_currentTelemetryInterval` outside the lock for the initial push — benign today (single target at startup) but worth pinning.

### RuntimeGovernanceService (instance — the integration seam)
- **Blockers and seams:** constructor needs `ITelemetryService` (interface — Moq-able; raise `ReadingAvailable` manually), `DispatcherQueue` (use existing stub), and reads `Windows.System.Power.PowerManager` statically inside `OnReadingAvailable` — the WinRT call is wrapped in try/catch so it degrades to "plugged in" on test hosts, but battery-driven `LowPower` transitions are **untestable without a seam**. Introduce a `Func<bool> isOnBattery` constructor parameter (default: current PowerManager read) — smallest possible change.
- Hysteresis: 2 consecutive HighLoad readings → still Normal; 3rd → HighLoad. Alternating HighLoad/Gaming candidates never accumulate 3 → no transition. Recovery: single Normal reading flips back immediately.
- Mode change side effects: budget `MaxBackground` updated (verify via `GetSnapshot`), `ModeChanged` raised once with correct prev/new/reasons (synchronous via stub dispatcher), coordinator intervals updated.
- Reasons refresh without mode change (e.g. HighLoad + battery while already HighLoad… note reasons only update when `shouldApply`) — pin current behavior.
- `TryAcquireSlot` IdleOnly gate: refused in every non-Normal mode with the policy's deferral text, granted in Normal.
- `ReleaseSlot` drains the queue (observable once `Enqueue` has a caller; until then assert no-throw).
- `Start` idempotent; `Stop` unsubscribes and clears queue; `Dispose` after `Stop` safe; `Start` after `Dispose` throws `ObjectDisposedException`.

### Suggested file layout
`Lucid.Tests/Governance/{WorkloadClassifierTests, AdaptiveSchedulingPolicyTests, RuntimePressureAnalyzerTests, ConcurrencyBudgetTests, ExecutionPriorityQueueTests, PollingCoordinatorTests, RuntimeGovernanceServiceTests}.cs` + `<Compile Include>` entries for the 10 Governance sources, `TelemetrySnapshot`'s file, and `ITelemetryService`'s defining file. `AdaptiveSchedulingPolicy`, `WorkloadClassifier`, `RuntimePressureAnalyzer` are `internal` — add `InternalsVisibleTo` or (matching the linked-source pattern) they compile directly into the test assembly, so `internal` is already visible. Verify `ITelemetryService`/`TelemetrySnapshot` don't drag WinUI types; if they do, extend the shadow-stub approach used for `DispatcherQueue`.

---

## 5. Counts

- Total distinct self-scheduling sites inventoried: **35** (10 timers/loops, 15 fire-and-forget/one-shot, ~8 pacing-delay clusters, plus the governed executor family counted as one).
- Governed (slot or polling-coordinator): **3** subsystems (execution engine incl. all 27 executors, rollback maintenance, telemetry loop).
- Advisory-only: **3** (Watchtower cycle, remediation safety checks, simulation injection).
- Ungoverned-risky: **8** (sites #3, #4, #7/#8, #9, #11, #12, #13, #15).
- Ungoverned-benign: **~13** (one-shot init, pacing delays, tiny writes, infrastructure dispatch).
- Dead/unwired: ExecutionPriorityQueue deferral path, 11 of 13 workload categories, `MockTelemetryService`, `NativeScannerService`, the "0 = pause" adaptive-target contract.
