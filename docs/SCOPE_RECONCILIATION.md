# Scope Reconciliation — Implementation vs. Roadmap

> **Status:** Decision required (owner: Tyler). This is an analysis + options
> document, **not** a change. No code is removed or quarantined by this file.
> Created from a whole-project review on 2026-06-13.

## Why this exists

The `GUARDIAN PROTOCOL` in `CLAUDE.md` requires that project integrity take
priority over feature velocity, and that risky drift be surfaced rather than
silently absorbed. A review of the actual source tree found that **the
implementation has run well ahead of the documented roadmap**, and that the
governance docs no longer describe the system. Under the protocol's three-category
gate this is a **Category B** situation (debt / inconsistency / complexity that
warrants a deliberate decision) trending toward **Category C** if it keeps growing
unmanaged.

This document maps what exists to where the roadmap says we should be, so a
direction can be chosen on purpose instead of by accumulation.

## The gap, in numbers

> **Which "phases"?** Phase numbers in this document follow **CLAUDE.md's "Roadmap
> Phase Summary"** — product-capability phases (1 Platform Stabilization ·
> 2 Operational Intelligence · 3 Security Intelligence · 4 Explain My PC ·
> 5 Visualization · 6 Ecosystem). `ROADMAP.md` uses a *different*, engineering
> numbering (0 Stabilize · 1 Repository Hygiene · 2 Build/CI/Release · 3 Test
> Expansion · 4 Architecture Hardening · 5 Trust/Privacy/Governance · …). The two
> are **not** interchangeable — do not cross-read the numbers below against `ROADMAP.md`.

| | Planned (CLAUDE.md phase plan) | Tree actually contains |
|---|---|---|
| Current phase | **Phase 1 — Platform Stabilization** ("next priority"; note `ROADMAP.md`'s own Phase 1 is "Repository Hygiene") | product Phases 1–5 partially built; several subsystems in no phase at all |
| Service subdomains | a focused stabilization set | **42** under `Services/` |
| C# files (`Lucid.App`) | — | **~521** |
| Pages | "13 pages" (CLAUDE.md, now corrected) | **23** |
| Executors | "28" (CLAUDE.md, now corrected) | **27** registered |
| Tests | "126" (`docs/reports/audit-roadmap-2026-06-10.md`, stale) | **249** passing |

The headline: the roadmap names Phase 1 as the thing to finish first, but the code
already contains large parts of Phase 2 (Replay, advanced forecasting), Phase 3
(security/trust hardening), Phase 4 (Explain / cognitive reasoning), and entire
domains the roadmap never scoped (Companion overlay, Distributed local-sync,
Visual/Desktop context capture, Autonomy/autonomous remediation, Simulation).

## Subsystem → roadmap-phase map

Legend (phases = CLAUDE.md product-capability scheme above, **not** `ROADMAP.md`'s engineering phases): ✅ Phase-1 foundation · ⏩ built ahead of its product phase · ❓ in no roadmap phase

| Subsystem (`Services/…`) | Roadmap phase | Class |
|---|---|---|
| Telemetry, Baseline, Intelligence (rules), Narrative | Phase 1 | ✅ |
| Execution + 27 executors, Cleanup, Repair, Startup, Storage | Phase 1 | ✅ |
| Governance (resource), Settings, Persistence (SQLite), Diagnostics | Phase 1 | ✅ |
| Process, Security, Timeline, Session, History, Analytics, Apps, Privacy | Phase 1–3 | ✅ / ⏩ |
| Replay | Phase 2 (Replay) | ⏩ |
| Explain, Reasoning, Reasoning/Cognitive, Reasoning/Context/Memory | Phase 4 (flagship) | ⏩ |
| Trust, Trust/Integrity, Trust/EndpointValidation | Phase 3 (security) | ⏩ |
| Watchtower, Remediation | Phase 2–3 | ⏩ |
| Learning, Behavior | Phase 2 (forecasting/correlation) | ⏩ |
| Simulation | — | ❓ |
| Autonomy, Workflow, Automation | — | ❓ |
| Companion, Conversation, LlmChat | — | ❓ |
| DesktopContext, VisualContext | — | ❓ |
| Distributed | — | ❓ |
| Interaction | — | ❓ |

(Native Rust `lucid-scanner` is real and high-quality, and backs Storage — a
welcome Phase-1-aligned addition even though the roadmap framed Rust as future.)

## Integrity assessment

**What's reassuring** — the craft is high and the Phase-1 doctrines are honoured in
code, not just docs: resource governance is a real subsystem (Foreground/Background/
IdleOnly with a concurrency budget), executors implement dry-run + rollback, the
native FFI layer is exemplary, security-language discipline holds (zero banned
terms), and there are 249 passing tests. This is not low-quality sprawl.

**What's risky** — surface area is now far larger than the stated priority, the ❓
domains (autonomy, distributed sync, screen/clipboard capture, companion) carry the
highest trust/safety/performance stakes in the whole product yet sit outside the
roadmap's governance, and the docs that are supposed to anchor each session had
drifted (executor/page counts, "zero Rust"). Unmanaged, each new ❓ domain dilutes
the Phase-1 stabilization goal and raises the cost of every future change.

## Options (pick one)

**A. Stabilize-first freeze (recommended).** Declare a moratorium on *new* ❓ domains.
Finish Phase 1 hardening (tests, governance coverage, diagnostics, persistence
durability) across what exists. Existing ❓ code stays but is feature-frozen and
labelled experimental until its phase is formally reached. Lowest risk; honours the
roadmap and the Guardian Protocol as written.

**B. Adopt-and-rebaseline.** Accept that the product is further along than the plan,
and rewrite `ROADMAP.md` to match reality — promote the ⏩/❓ domains into real,
owned phases with their own stabilization bars. Higher honesty, but legitimizes the
sprawl and front-loads a large documentation + test-backfill effort.

**C. Quarantine experimental domains.** Move the ❓ domains behind an explicit
"experimental" boundary (separate folder/namespace, off by default, excluded from
the default build) so the shippable core is unambiguous. Most work, cleanest
long-term separation, reversible.

**Recommendation: A now, B at the next milestone.** Freeze new ❓ scope immediately to
protect Phase 1, then do a deliberate rebaseline (B) once Phase 1 has a green
stabilization bar — rather than letting the roadmap and the code keep diverging.

### Decision (owner-confirmed 2026-06-14): A now → B at the next milestone

The owner has explicitly chosen **Option A now, Option B at the next milestone**.
This supersedes the earlier "provisional default" framing.

**In force today (Option A — stabilize-first freeze):**

- **Do not add a new ❓ (out-of-roadmap) service domain** without explicit owner
  sign-off. New work goes into hardening what already exists (tests, governance
  coverage, diagnostics, persistence durability) — i.e. Phase 1.
- This is a *freeze*, not a deletion: no existing code is removed, disabled, or
  moved. Option C (quarantine) is not being pursued.
- The freeze is mirrored as a guardrail in `CLAUDE.md` so future sessions honour it.

**Planned next (Option B — rebaseline), triggered by the Phase-1 green bar.**
When Phase 1 — Platform Stabilization is verified complete — i.e. every ✅ Phase-1
subsystem above has test + governance coverage and a clean build/CI bar (the
`v0.1-foundation` milestone) — lift the freeze and rewrite `ROADMAP.md` to promote
the ⏩/❓ domains into real, owned phases, each with its own stabilization bar.
Until that bar is met, the freeze stays in force.

## Not in scope of this document

- No code is deleted, disabled, or moved here. Options B/C, if chosen, are separate
  changes that must each pass the Guardian Protocol gate on their own.
- Point-in-time reports under `docs/reports/` are snapshots and are intentionally
  left as-is; the living docs (`CLAUDE.md`, `ROADMAP.md`) are the ones to keep true.
