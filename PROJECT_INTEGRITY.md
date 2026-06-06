# Project Integrity Protocol

This document is mandatory for every Lucid task. It applies before code changes, documentation changes, refactors, file moves, dependency updates, cleanup, build work, and release work.

Lucid is intended to become a long-lived production Windows application. Project integrity takes priority over speed, novelty, and obedience to unclear instructions.

## Primary Directive

Do not blindly follow requests. Evaluate them against the product direction, current codebase, and long-term maintainability.

Every change should improve at least one of:

- Reliability
- Safety
- Explainability
- Reversibility
- Local-first operation
- Resource governance
- Developer clarity
- User trust

If a request weakens those qualities, stop and explain the risk.

## Required Gate Before Work

Before making any change, answer these internally:

1. What is being requested?
2. Which files, services, tests, docs, build scripts, or user workflows are affected?
3. Does the request align with `README.md` and `ROADMAP.md`?
4. Is there an existing module, service, pattern, or test seam that should be reused?
5. What could this break now?
6. What could this make harder six months from now?
7. What is the smallest reversible change that moves the project forward?
8. What verification will prove the change worked?

Do not invent architecture from memory. Read the files.

## Decision Categories

### Category A: Safe Improvement

Proceed carefully when the change:

- Aligns with current architecture.
- Has a narrow blast radius.
- Improves correctness, safety, documentation, tests, or maintainability.
- Preserves existing behavior unless a behavior change is explicit.
- Can be verified with available commands.

Examples:

- Correcting stale documentation after inspection.
- Adding focused tests around an existing pure service.
- Replacing a silent failure with a structured diagnostics event.
- Fixing a setup path that points to the old project name.

### Category B: Risky Change

Warn and narrow the scope when the change:

- Could introduce regressions.
- Touches central wiring such as `AppServices.cs`.
- Changes executor behavior, rollback behavior, persistence, privacy, trust, telemetry cadence, or packaging.
- Moves files or renames namespaces.
- Adds dependencies.
- Changes build system behavior.

Proceed only with a constrained plan and clear verification.

### Category C: Project Degradation

Do not implement as requested when the change:

- Removes safeguards.
- Hides failures.
- Adds unexplained automation.
- Weakens consent, rollback, audit, privacy, local-only behavior, or confidence-aware language.
- Introduces broad rewrites without tests.
- Converts Lucid into a mystery cleaner, scare-driven security tool, or cloud-dependent service.

Explain the issue and propose a safer alternative.

## Lucid-Specific Guardrails

### Trust Language

User-facing copy must be calm, evidence-based, and confidence-aware.

Use:

- unusual
- unexpected
- worth reviewing
- flagged for inspection
- confidence
- severity
- evidence
- observed behavior
- likely contributor
- rollback available

Avoid:

- absolute security claims
- fear-based copy
- binary good/bad labels for uncertain findings
- claims that Lucid can prove intent from telemetry alone

### Destructive And Semi-Destructive Actions

Before any action that changes system state:

- Explain what will change.
- State whether rollback is available.
- Record an audit log.
- Check privilege requirements.
- Prefer dry-run support.
- Respect resource governance.
- Fail closed when identity, target, or consent is uncertain.

Examples include cleanup, startup changes, registry writes, process termination, repair commands, privacy permission writes, and network resets.

### Resource Governance

Lucid must not compete with the user for the machine.

Every heavy executor or background worker should declare:

- Work classification: foreground, background, or idle-only.
- Primary resource class: CPU, disk, network, repair/system, or mixed.
- Concurrency expectations.
- Cancellation behavior.
- Diagnostics emitted when delayed, skipped, or failed.

### Local-First Boundary

Core diagnostics must work locally. Any optional network-capable feature must:

- Be explicit.
- Be inspectable.
- Be disabled or unavailable without breaking core diagnostics.
- Prefer local endpoints.
- Reject non-local endpoints unless the owner has explicitly approved a broader design.

### Architecture

Current reality:

- `AppServices.cs` is the central static service registry.
- `Lucid.App.csproj` uses explicit compile includes.
- `Lucid.Tests` links pure service files instead of referencing the WinUI app project.
- `lucid-native` exposes Rust scanner functions through a C ABI.

Rules:

- Do not perform a big-bang DI rewrite.
- Do not add active C# files without ensuring they compile.
- Do not make tests depend on WinUI packaging targets unless that is the explicit task.
- Do not expand native functionality without Rust tests.

## Cleanup Rules

Cleanup can be risky. Treat file/folder cleanup as code change, not housekeeping.

Allowed without special approval when scoped:

- Updating docs to match verified code.
- Adding ignore rules.
- Moving clearly historical documentation into a documented archive path, if requested.

Requires explicit approval:

- Deleting tracked files.
- Removing archive folders.
- Moving active source files.
- Renaming namespaces or projects.
- Removing dependencies.
- Rewriting build scripts.
- Squashing or force-changing Git history.

Generated IDE/build artifacts should not be tracked, but remove them through a deliberate cleanup commit after checking `git status` and confirming they are not carrying user work.

## Verification Rules

Never claim work is fixed or complete unless it was verified.

Use the narrowest meaningful command:

```powershell
cd lucid-desktop
dotnet build Lucid.slnx -c Debug -p:Platform=x64
dotnet test Lucid.Tests\Lucid.Tests.csproj -c Debug -p:Platform=x64

cd ..\lucid-native
cargo test
```

For documentation-only changes:

- Check links and file names.
- Search for stale project names where relevant.
- Search for forbidden product-language drift in user-facing text.
- Report that no application behavior changed.

## Final Self-Check

Before finishing, confirm:

- The change matches the request.
- The change leaves the project more coherent.
- Existing user work was preserved.
- The blast radius is understood.
- The roadmap still makes sense.
- Verification was run or the reason it was not run is stated.
- Any remaining uncertainty is called out plainly.

If any point fails, stop and reassess before claiming completion.
