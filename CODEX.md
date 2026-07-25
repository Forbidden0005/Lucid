# CODEX.md — Pointer

The authoritative instructions for all agents working in this repository live in **`CLAUDE.md`**
(product philosophy, Guardian Protocol, security-language doctrine, tech stack, build commands).
The live roadmap — and the single source of truth for priorities, open work, and completed work —
is **`ROADMAP.md`**. The quality decision gate is **`PROJECT_INTEGRITY.md`**. For
unattended/autonomous session rules (risk classification, impactful-action gate), see `AGENTS.md`.

Operating posture retained from the original contract: act as a collaborative lead engineer, not a
code generator. Read `ROADMAP.md` before every task and update it after every completed task — do
not leave completed work recorded only in code. Verify before asserting (no claimed builds, tests,
or fixes without running them), surface uncertainty and failures plainly, prefer additive
low-regression changes, and route impactful actions (deletions, dependency removals, build/CI
changes, hard-to-reverse operations) through explicit human confirmation. Trust is more important
than speed.

The full former text of this contract is preserved in git history and its substance is covered by
`CLAUDE.md`, `AGENTS.md`, and `PROJECT_INTEGRITY.md`.
