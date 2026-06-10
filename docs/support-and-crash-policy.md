# Lucid Support And Crash Policy

This document defines what Lucid currently captures, what it exports for support, and what remains intentionally out of scope for the current release path.

## Current support bundle scope

The support bundle exported by `installer/Export-LucidSupportBundle.ps1` is conservative and local-first.

Included when present:

- `settings.json`
- `settings.integrity`
- `migrations/migration-state.json`
- `install/current.json`
- up to 5 recent Lucid log files from `%LocalAppData%\Lucid\logs`
- `explainmypc.db`
- `inference-history.json`
- up to 3 recent Lucid crash dumps from `%LocalAppData%\CrashDumps`
- up to 3 recent Lucid WER report folders from `%LocalAppData%\Microsoft\Windows\WER\ReportArchive`
- `support-bundle-manifest.json`
- `support-policy.json`

Excluded by default:

- arbitrary user documents
- browser history
- non-Lucid application data
- full Windows event logs
- arbitrary dump files not clearly associated with `Lucid.App`

## Crash artifact policy

Current Lucid crash handling is evidence-oriented, not auto-uploading.

- Lucid does not auto-submit crash dumps anywhere.
- Crash dumps remain local unless the user exports a support bundle.
- The support export only collects files that match `Lucid.App*.dmp` or Lucid-named WER folders.
- The release lane now verifies that the support export path can bundle those artifacts when they exist.

## Retention expectations

- runtime logs rotate locally through the Lucid logger
- crash dumps are governed by Windows, not Lucid
- support bundles are point-in-time exports created on demand
- migration history is retained locally under `%LocalAppData%\Lucid\Migrations`

## Current operational gaps

These are still not complete:

- live hosted support intake workflow
- customer-facing crash submission flow
- active public symbol publication
- automatic correlation of bundle contents to a release channel endpoint

Until those exist, Lucid's support posture is:

- local-first
- manual export
- explicit review before sharing
