# Repository Hygiene

This file documents which top-level content is active, which content is historical, and where cleanup artifacts belong.

## Active Root Files

These files are active project control or operational context and are expected to remain at the repository root:

- `README.md`
- `ONBOARDING.md`
- `PROJECT_INTEGRITY.md`
- `ROADMAP.md`
- `CODEX.md`
- `AGENTS.md`
- `CLAUDE.md`
- `CURRENT_STATE.md`
- `REMAINING_WORK.md`
- `setup.ps1`
- `setup.bat`

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
