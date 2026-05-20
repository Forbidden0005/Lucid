# ExplainMyPC - Claude Project Instructions

## Project Overview

ExplainMyPC is a modern Windows intelligence platform.

The goal is NOT to create:
- a fake “PC booster”
- a registry cleaner scam
- a bloated antivirus clone

The goal IS to create:
- a trustworthy Windows analysis platform
- a system diagnostics assistant
- an explainable PC health monitor
- a safe repair and optimization toolkit

The application should help users understand:
- why their PC feels slow
- what consumes resources
- what may be risky
- what can be safely improved

The app must prioritize:
- transparency
- reversibility
- safety
- clarity
- performance
- modularity

---

# Tech Stack

## Frontend
- WinUI 3
- C#
- MVVM architecture

## Backend
- Rust native modules
- modular scanning engines

## Database
- SQLite

---

# Core Product Philosophy

Every feature should answer:

> “Does this help users understand their system better?”

Avoid:
- fake optimizations
- placebo features
- misleading claims
- aggressive registry cleaning
- destructive automation
- unexplained warnings

Prefer:
- diagnostics
- evidence
- health scoring
- explainable recommendations
- rollback systems
- safe repair flows

---

# Core Features

## Explain My PC
Natural language system analysis that explains:
- performance issues
- startup congestion
- disk pressure
- memory pressure
- suspicious behavior
- storage waste
- thermal problems

This is the flagship feature.

---

# Architectural Rules

## IMPORTANT:
The app MUST remain modular.

Avoid:
- giant monolithic services
- tightly coupled UI/business logic
- massive god classes

Prefer:
- isolated services
- composable engines
- dependency injection
- feature modules

---

# Frontend Rules

Use:
- MVVM
- async operations
- observable state
- reusable components

Avoid:
- business logic inside views
- blocking UI threads
- deeply nested code-behind logic

UI should feel:
- modern
- calm
- information-rich
- responsive
- native to Windows 11

---

# Backend Rules

Rust modules should handle:
- filesystem traversal
- disk analysis
- process monitoring
- performance-sensitive work
- low-level Windows APIs

Rust modules should expose:
- clear APIs
- structured responses
- typed error handling

Avoid unsafe Rust unless absolutely required.

---

# Safety Requirements

Before ANY destructive action:
- create restore point
- create rollback snapshot
- log changes
- explain risk to user

Examples:
- uninstall
- registry edits
- driver changes
- cleanup operations
- repair operations

---

# Trust Requirements

Users should ALWAYS understand:
- why something was flagged
- how severe it is
- what caused it
- what fixing will do
- whether rollback is possible

Never use fear-based UX.

Avoid:
- “CRITICAL ERROR”
- “YOUR PC IS IN DANGER”
- manipulative language

Prefer:
- confidence scores
- severity levels
- evidence-based explanations

---

# Performance Requirements

The app itself must remain lightweight.

Avoid:
- excessive telemetry polling
- high idle CPU usage
- excessive RAM usage
- unnecessary background services

The app must not become:
> the reason the PC is slow

---

# Code Quality Rules

Prefer:
- readable code
- small focused services
- composition over inheritance
- explicit naming
- strong typing

Avoid:
- premature optimization
- giant utility files
- hidden side effects
- duplicated logic

---

# UI Design Language

Visual style:
- dark modern surfaces
- Fluent Design inspired
- soft telemetry visuals
- subtle glow accents
- clean spacing
- glass layers where appropriate

Avoid:
- RGB gamer aesthetics
- hacker-movie UI
- clutter
- tiny text
- overcrowded dashboards

---

# Explain My PC Output Style

Outputs should feel:
- intelligent
- human
- practical
- concise

Example:

GOOD:
“Startup time increased because several apps launch automatically when Windows starts.”

BAD:
“Boot degradation threshold exceeded.”

---

# Security Philosophy

Security analysis should use:
- behavior analysis
- heuristics
- persistence detection
- reputation systems

Avoid pretending to replace enterprise antivirus platforms.

The goal is:
- insight
- visibility
- diagnostics
- detection assistance

---

# Storage Philosophy

Storage cleanup should be:
- conservative
- explainable
- reversible

Never delete:
- unknown system files
- driver packages blindly
- important caches automatically

Always explain:
- reclaimable size
- file origin
- potential impact

---

# Telemetry Design

Telemetry updates:
- CPU: ~1s
- RAM: ~1s
- Disk: ~2s
- SMART checks: infrequent

Avoid excessive polling loops.

---

# Preferred Development Flow

When implementing features:
1. Create models
2. Create service layer
3. Create ViewModels
4. Build UI
5. Add telemetry
6. Add tests
7. Add logging
8. Add rollback support where applicable

---

# Preferred Output Format

When generating code:
- provide complete files when possible
- explain architecture decisions
- include comments for complex logic
- prioritize maintainability

When generating UI:
- use reusable components
- maintain consistent spacing
- support dark mode first

---

# Long-Term Vision

ExplainMyPC should eventually feel like:
- a Windows intelligence layer
- a trusted system analyst
- a diagnostic cockpit

The app should make users feel:
> “For the first time, I actually understand my computer.”

---

# Build Commands

## Frontend (WinUI 3)

`dotnet build` **must** include `-p:Platform=x64` (or x86/arm64).
`WindowsAppSDKSelfContained=true` does not support AnyCPU — the build hard-fails without a platform.

```
# Debug build (run from frontend/)
dotnet build ExplainMyPC.slnx -c Debug -p:Platform=x64

# Release build
dotnet build ExplainMyPC.slnx -c Release -p:Platform=x64
```

Warning `NETSDK1206` (version-specific RIDs) is non-critical — it comes from the Windows App SDK NuGet, not your code. Ignore it.


---

# XAML Build Pipeline Notes

## XamlPreCompile — known CLI limitation

`XamlPreCompile` (the step that produces `obj/x64/Debug/.../intermediatexaml/ExplainMyPC.App.dll`) is
defined in Visual Studio's `Microsoft.CSharp.CurrentVersion.targets` — **not** in the .NET SDK.

**Consequence:** `dotnet build` silently skips `XamlPreCompile`. This works fine incrementally because
the intermediate DLL from the previous VS/MSBuild run is reused. If that DLL is ever deleted (e.g. after
`dotnet clean`, or a git clean), `dotnet build` will fail with:

```
Microsoft.UI.Xaml.Markup.Compiler.interop.targets(590): error MSB3073:
XamlCompiler.exe ... exited with code 1
```

**Fix:** Run the VS MSBuild reset script once:

```bat
C:\Users\tyler\build_vs.bat
```

This calls `VsDevCmd.bat` + VS `msbuild.exe`, which runs `XamlPreCompile` properly, regenerates the
intermediate DLL, and `dotnet build` works again from that point.
