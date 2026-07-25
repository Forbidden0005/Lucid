# Active File Inventory

This document records the active Lucid application surface that is expected to stay under direct maintenance.

## Verified counts

Verified on 2026-06-07 from the current repository state:

- `lucid-desktop/Lucid.App/Views`: 27 `.xaml` files
- `lucid-desktop/Lucid.App/ViewModels`: 41 `.cs` files
- `lucid-desktop/Lucid.App/Services`: 436 `.cs` files
- `lucid-desktop/Lucid.Tests`: 6 `.cs` files
- `lucid-native`: 2 `.rs` files

## Managed-folder rule

`Lucid.App.csproj` uses selective compile includes for large folders after broad `Compile Remove` rules. That means a file can exist under an active folder but never build unless it is explicitly included.

The managed folders are:

- `ViewModels`
- `Services`
- `Core`

The repository now enforces this through:

- `scripts/check-app-source-includes.ps1`
- `scripts/verify-dev.ps1`

Any new `.cs` file under a managed folder must be either:

- explicitly compiled in `lucid-desktop/Lucid.App/Lucid.App.csproj`, or
- explicitly documented as an intentional exclusion in `scripts/check-app-source-includes.ps1`

Silent omissions are treated as a verification failure.

## Current intentional exclusions

These files currently exist under managed folders but are intentionally not compiled:

- `ViewModels\ShellViewModel.cs`: legacy shell/navigation prototype that depends on non-live `Lucid.Core` namespaces
- `ViewModels\SystemIssueViewModel.cs`: legacy presentation wrapper tied to excluded model flow
- `Services\MockTelemetryService.cs`: preserved mock implementation while runtime uses `WindowsTelemetryService`

If any of these files become active again, promote them by updating `Lucid.App.csproj` and removing them from the exclusion registry.
