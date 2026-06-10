# Update Publication

This document defines the current Lucid update publication and discovery contract.

## Current discovery model

Lucid now uses a static JSON discovery model rooted under:

- `release/packages/feeds/`

Generated files:

- `release/packages/feeds/index.json`
- `release/packages/feeds/<channel>.json`

Current supported channels:

- `preview`
- `stable`
- `internal`

## Publication rules

The publication unit is the `release/packages/` directory. A valid published tree now includes:

- the versioned release zip
- the zip checksum file
- the per-package update manifest
- `feeds/index.json`
- `feeds/<channel>.json`

Current rule: publish the full `release/packages/` tree together. Do not publish a package zip without its matching checksum, update manifest, and feed files.

## Discovery contract

`feeds/index.json` is the entry point.

It tells a client:

- which channels exist
- which channel file belongs to each channel
- the latest version per channel

`feeds/<channel>.json` tells a client:

- the latest release on that channel
- the package path
- the package checksum path
- the package update-manifest path
- release notes path
- support/export references
- install policy, including downgrade posture and migration-state path

## Current install policy exposed through the feed

- install is user-initiated
- downgrade is blocked by default
- migration state is expected at `migrations/migration-state.json`

## Verification

The release gate now includes:

- `scripts/generate-release-update-feed.ps1`
- `scripts/verify-release-update-feed.ps1`

Those scripts prove the current release is discoverable through the feed and that the feed points at the verified package artifacts.

Release operations ownership and rollout policy now live in:

- `release/release-operations.json`
- `scripts/validate-release-operations.ps1`

## Current limitations

This is a publication/discovery contract, not a live updater client.

Still not done:

- live hosted endpoint deployment
- client polling implementation in Lucid
- signed public distribution requirement enforcement at the feed host
- staged rollout implementation beyond manual full-channel publication
- rollback-to-previous-release policy at the feed level
