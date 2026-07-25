# Repository Hygiene

This file documents which top-level content is active, which content is historical, and where cleanup artifacts belong.

## Active Root Files

These files are active project control or operational context and are expected to remain at the repository root:

- `README.md`
- `ONBOARDING.md`
- `PROJECT_INTEGRITY.md`
- `ROADMAP.md`
- `CODEX.md` (pointer to `CLAUDE.md`)
- `AGENTS.md` (pointer to `CLAUDE.md` plus autonomous-session rules)
- `CLAUDE.md`
- `setup.ps1`
- `setup.bat`

Former root state snapshots (`CURRENT_STATE.md`, `REMAINING_WORK.md`, `AUDIT_ROADMAP.md`) were
archived under `docs/reports/` with dated filenames on 2026-07-25; `ROADMAP.md` is the live state.

## Active Folders

- `lucid-desktop/`: WinUI application and tests.
- `lucid-native/`: Rust native workspace.
- `docs/`: active design, architecture, and project documentation.
- `.github/`: CI workflow definitions.
- `_archive/`: historical code and scaffolds retained for reference.

## Historical Reports

Historical audits, reviews, and one-off cleanup reports belong under `docs/reports/`, not at the repository root.

Current report files:

- `docs/reports/CLAUDE_REVIEW.md`
- `docs/reports/CLAUDE_REVIEW_REPORT.md`
- `docs/reports/NEW_ROADMAP.md`
- `docs/reports/TIDYING_REPORT.md`
- `docs/reports/current-state-2026-07-02.md` (formerly root `CURRENT_STATE.md`)
- `docs/reports/remaining-work-2026-06-06.md` (formerly root `REMAINING_WORK.md`)
- `docs/reports/audit-roadmap-2026-06-10.md` (formerly root `AUDIT_ROADMAP.md`)
- `docs/reports/structure-snapshot-undated.txt` (formerly `docs/Structure.txt`)
- `docs/reports/active-file-inventory-snapshot-2026-06-07.md` (formerly `docs/active-file-inventory.md`)

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
