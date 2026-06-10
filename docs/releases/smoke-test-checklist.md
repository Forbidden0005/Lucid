# Lucid Release Smoke Checklist

Run this checklist against the unpackaged `win-x64` publish output before any user-facing distribution.

## Launch And Shell

- Launch `Lucid.App.exe` from the published artifact folder.
- Confirm the app opens without a crash dialog or missing-runtime error.
- Confirm the main shell renders and navigation is responsive.
- Confirm the app can be closed cleanly without hanging in Task Manager.

## Navigation And Core Pages

- Open Dashboard, Insights, Processes, Repairs, Security, Storage, Timeline, Explain, Settings, and Privacy.
- Confirm each page renders without blank sections, obvious binding failures, or crash dialogs.
- Confirm returning between pages does not leave the shell unresponsive.

## Telemetry And Persistence

- Confirm live telemetry updates appear on Dashboard within a few seconds.
- Confirm settings changes persist after closing and reopening the app.
- Confirm the app can reopen after a prior session without startup corruption or reset state.

## Trust And Safety Surfaces

- Open the companion or local LLM settings and confirm the endpoint still resolves to a local-only address.
- Trigger at least one executor dry-run from Repairs or Storage and confirm the explanation, privilege, and rollback messaging render.
- Confirm non-reversible actions are labeled honestly and do not imply rollback where none exists.

## Shutdown

- Close the app after telemetry and settings activity.
- Reopen once more and confirm the app still starts cleanly.
- Archive the smoke result with the release artifact or release notes.
