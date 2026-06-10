# Support Triage Guide

This document defines the current Lucid support-triage workflow for the unpackaged release path.

## Current intake mode

Current intake mode is:

- manual
- bundle-first
- local-first

The expected user-provided artifact is a support bundle exported through:

- `installer/Export-LucidSupportBundle.ps1`

## Required first-pass artifacts

Review these first:

1. `support-bundle-manifest.json`
2. `support-policy.json`
3. `install/current.json`
4. `migrations/migration-state.json`
5. recent logs under `logs/`

If the issue is crash-related, then review:

6. `crash-dumps/`
7. `wer/`

## First-pass triage questions

Answer these before deeper diagnosis:

1. Which Lucid version was installed?
2. Was the issue on `preview`, `stable`, or `internal`?
3. Was the install an upgrade, downgrade, or fresh install?
4. Does the bundle show migration activity or legacy-file normalization?
5. Do logs show a startup, execution, persistence, or privacy boundary failure?
6. Is the failure reproducible on the current packaged release?

## Severity routing

- `build/release integrity`: release engineering
- `installer/update/migration`: release engineering
- `privacy/consent/trust posture`: platform owner
- `crash with dump/WER evidence`: release engineering first, then feature owner
- `executor behavior/regression`: execution surface owner

## Current limits

This triage guide assumes:

- manual bundle exchange
- no automatic crash submission
- no public symbol server
- no live staged rollout telemetry

If those assumptions change, this guide must be revised with the release operations policy.
