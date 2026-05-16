\# ExplainMyPC Architecture



\# Overview



ExplainMyPC uses a modular layered architecture designed for:



\* maintainability

\* scalability

\* safety

\* performance

\* separation of concerns



The system combines:



\* WinUI frontend

\* C# orchestration/services

\* Rust scanning engines

\* SQLite storage



\---



\# High-Level Architecture



```text

┌──────────────────────────────┐

│ WinUI 3 Frontend             │

│ Views + ViewModels           │

└──────────────┬───────────────┘

&#x20;              │

┌──────────────▼───────────────┐

│ Application Services Layer   │

│ Orchestration + State        │

└──────────────┬───────────────┘

&#x20;              │

┌──────────────▼───────────────┐

│ Intelligence Engine          │

│ Correlation + Recommendations│

└──────────────┬───────────────┘

&#x20;              │

┌──────────────▼───────────────┐

│ Backend Engine Layer         │

│ Rust Native Modules          │

└──────────────┬───────────────┘

&#x20;              │

┌──────────────▼───────────────┐

│ SQLite Database              │

└──────────────────────────────┘

```



\---



\# Frontend Layer



\## Responsibilities



\* UI rendering

\* user interactions

\* state binding

\* navigation

\* telemetry visualization

\* notifications



\## Technology



\* WinUI 3

\* MVVM

\* Dependency Injection



\---



\# ViewModel Layer



\## Responsibilities



\* expose observable state

\* coordinate services

\* async orchestration

\* validation

\* command handling



\## Rules



\* no low-level system access

\* no direct database logic

\* no heavy processing



\---



\# Services Layer



\## Responsibilities



\* orchestrate backend modules

\* aggregate scan results

\* manage caching

\* coordinate telemetry

\* manage repair flows



\## Examples



\* TelemetryService

\* ScanService

\* RepairService

\* SecurityService

\* SnapshotService



\---



\# Intelligence Engine



\## Responsibilities



\* correlate findings

\* generate recommendations

\* compute health scores

\* prioritize issues

\* generate natural-language summaries



\## Example Logic



IF:



\* startup apps > threshold

\* disk near capacity

\* high paging activity



THEN:



\* recommend startup optimization

\* recommend cleanup

\* reduce severity confidence if RAM availability remains healthy



\---



\# Rust Backend Modules



\## Responsibilities



\* filesystem traversal

\* process inspection

\* SMART analysis

\* low-level Windows APIs

\* high-performance scanning

\* parallel processing



\## Design Requirements



\* strongly typed APIs

\* structured responses

\* minimal unsafe code

\* async-friendly interfaces



\---



\# Database Layer



\## SQLite Usage



Store:



\* telemetry history

\* issue history

\* snapshots

\* health scores

\* scan history

\* recommendations



\---



\# Security Architecture



\## Principles



\* least privilege

\* privilege escalation only when required

\* signed binaries

\* no silent destructive actions



\---



\# Repair Workflow Architecture



Before repair:



1\. create restore point

2\. create rollback snapshot

3\. log intended changes

4\. request confirmation

5\. execute repair

6\. verify result

7\. allow rollback



\---



\# Telemetry Architecture



\## Polling Rates



\* CPU: 1s

\* RAM: 1s

\* Disk: 2s

\* SMART: infrequent

\* Deep scans: manual or scheduled



Avoid excessive background polling.



\---



\# Communication Layer



Preferred approach:



\* Rust DLL bridge via FFI



Alternative future approach:



\* local gRPC services



\---



\# Modular Design Rules



Each feature module should:



\* expose clear interfaces

\* avoid circular dependencies

\* support independent testing

\* support future replacement



\---



\# Logging Strategy



\## Requirements



\* structured logs

\* rotating logs

\* log levels

\* diagnostic export support



\---



\# Future Expansion Support



Architecture should support:



\* plugin systems

\* remote monitoring

\* multiple machines

\* AI engines

\* distributed telemetry



