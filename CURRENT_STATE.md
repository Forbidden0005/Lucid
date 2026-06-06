# Lucid - Current State

Generated from repo inspection on 2026-06-06. Code is the source of truth.

## Build And Tests

- Active branch: `main`
- Primary solution: `lucid-desktop/Lucid.slnx`
- Required build command: `dotnet build Lucid.slnx -c Debug -p:Platform=x64`
- Test command: `dotnet test Lucid.Tests\Lucid.Tests.csproj -c Debug -p:Platform=x64`
- Rust command: `cargo test` from `lucid-native/`

## Current Verified Counts

- Non-generated C# app files: 523
- XAML files: 41
- Unit test files: 6
- Unit tests: 53
- Rust tests: 0

## Important Architecture Notes

- `Lucid.App` is a WinUI 3 desktop app targeting .NET 8.
- `Lucid.Tests` links pure production service files directly instead of referencing the WinUI app project. This keeps unit tests out of WindowsAppSDK packaging/resource targets.
- `AppServices.cs` remains the central static service registry and is the largest architectural pressure point. Do not rewrite it wholesale; migrate incrementally behind tested module boundaries.
- `Lucid.App.csproj` still uses selective compilation with explicit `<Compile Include>` entries. New app files must be added deliberately.
- SQLite persistence exists for telemetry, timeline, insight history, and recommendation outcomes. Operation history is still separate unless a later migration changes that.
- The Rust native scanner exists and compiles, but its test coverage is empty.

## Current Production Risks

- Test coverage is thin relative to the number of service and executor surfaces.
- Several services still use best-effort `catch` blocks where failures should become diagnostics events.
- Some docs are historical and may not match the current filesystem.
- Dependency upgrades include major-version jumps and should be handled in a separate pass with targeted regression testing.

