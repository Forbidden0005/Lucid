# AGENTS.md — Pointer

The authoritative agent instructions live in **`CLAUDE.md`** (product philosophy, Guardian
Protocol, security-language doctrine, tech stack, build commands). The live roadmap and single
source of truth for project state and priorities is **`ROADMAP.md`**; the decision gate is
**`PROJECT_INTEGRITY.md`**. Read `ROADMAP.md` and `PROJECT_INTEGRITY.md` before every task, and
update `ROADMAP.md` after every completed task. Do not duplicate their content here.

## Unattended-session rules (unique to this file)

When operating **autonomously** (Tyler not present), the following additional gates apply:

- **Task selection:** work only open `ROADMAP.md` items — Critical Issues (P0/P1) first, then the
  active phase. If the roadmap is silent on the work, stop; do not invent scope.
- **Risk gate:** classify each task low / medium / high before executing. Low (tests for existing
  behavior, doc truth-ups, lint fixes, roadmap updates) — proceed. Medium (narrow additive
  services/executors/tests/bug fixes) — proceed with explicit pre-task reasoning. High — stop and
  document instead: anything touching `AppServices.cs`, build/CI/release config, executor
  rollback/consent/dry-run behavior, trust/privacy/consent subsystems, persisted data formats,
  broad refactors, deletions, or hard-to-reverse changes.
- **Impactful action gate:** deleting/overwriting files, removing dependencies, history-rewriting
  git operations, and security/secret-handling changes always require a human. Never take them
  autonomously.
- **Evidence rules:** verify before asserting — never claim a build, test, fix, or absence of
  secrets without having run the check and read the output. Surface failures; state uncertainty
  plainly. Trust is more important than speed.
- **Session termination:** verify build + tests, update `ROADMAP.md`, commit working code with a
  descriptive message, and note any in-progress work. If the build fails on the XAML intermediate
  DLL, stop and report — that requires `C:\Users\tyler\build_vs.bat` run interactively (see
  `CLAUDE.md`).
