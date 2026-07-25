# Release Checklist

Use this checklist before treating a Lucid release as publishable.

## Required gates

1. `scripts/validate-release-metadata.ps1`
2. `scripts/verify-release.ps1`
3. review `release/packages/feeds/index.json`
4. review `release/packages/feeds/<channel>.json`
5. review `release/packages/*.update.json`

## Optional setup-exe gate

Run `scripts/verify-release.ps1 -BuildSetupExe` only on machines with Inno Setup 6 available as `ISCC.exe`, or pass `-IsccPath`.
The setup wrapper is built from the metadata-derived package zip, not from the newest file in `release/packages`.

## Required manual checks

1. Confirm the intended release channel is correct.
2. Confirm release notes match the actual shipped scope.
3. Confirm support and crash policy docs are bundled.
4. Confirm installer migration behavior is expected for this version.
5. Confirm public distribution is still blocked unless signing mode and operations policy both allow it.

## Public-distribution hold points

Do not treat a release as public-distribution ready until all of these are true:

1. `release/release-metadata.json` uses `authenticode-required`
2. valid signing material is available
3. release operations policy allows public distribution for the target channel
4. symbol publication process is available for the shipped build
5. support intake path is defined for real users
