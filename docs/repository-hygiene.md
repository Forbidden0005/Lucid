# Repository Hygiene

This file documents which top-level content is active, which content is historical, and where cleanup artifacts belong.

## Active Root Files

These files are active project control or operational context and are expected to remain at the repository root:

- `README.md`
- `ONBOARDING.md`
- `PROJECT_INTEGRITY.md`
- `ROADMAP.md`
- `CLAUDE.md` — single source of truth for agent instructions
- `CODEX.md`, `AGENTS.md` — one-paragraph pointers to `CLAUDE.md` (reduced 2026-07-15, ROADMAP C8)
- `setup.ps1`
- `setup.bat`

Retired 2026-07-15 (ROADMAP C8; content preserved in git history): `CURRENT_STATE.md`
(counts rot within days — `ROADMAP.md` plus CI are the live state) and `REMAINING_WORK.md`
(folded into `ROADMAP.md`).

## Active Folders

- `lucid-desktop/`: WinUI application and tests.
- `lucid-native/`: Rust native workspace.
- `docs/`: active design, architecture, and project documentation.
- `.github/`: CI workflow definitions.
- `_archive/`: historical code and scaffolds retained for reference.

## Historical Reports

Historical audits, reviews, and one-off cleanup reports belong under `docs/history/` with
date-prefixed filenames (moved from `docs/reports/` on 2026-07-15, ROADMAP C8), not at the
repository root.

Current report files:

- `docs/history/2026-06-07-CLAUDE_REVIEW.md`
- `docs/history/2026-06-07-CLAUDE_REVIEW_REPORT.md`
- `docs/history/2026-06-07-NEW_ROADMAP.md` (superseded by `ROADMAP.md`)
- `docs/history/2026-06-07-TIDYING_REPORT.md`

## Generated Files

Generated content should not be committed unless there is a specific reason:

- `**/bin/`
- `**/obj/`
- `.vs/`
- `**/TestResults/`
- `lucid-native/target/`

## Cleanup Rule

Before moving or removing files, decide whether the file is:

- Active project control documentation
- Active engineering context
- Historical reference material
- Generated output

If the file is historical, move it into a documented archival location instead of leaving it at the root.
