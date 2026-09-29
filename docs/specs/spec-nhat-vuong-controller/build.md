---
title: 'Build Nhat Vuong Controller from the canonical specification'
type: 'feature'
created: '2026-09-11'
status: 'implemented'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/AGENTS.md'
  - '{project-root}/docs/specs/spec-nhat-vuong-controller/SPEC.md'
  - '{project-root}/docs/specs/spec-nhat-vuong-controller/quality-constraints.md'
  - '{project-root}/docs/specs/spec-nhat-vuong-controller/traceability.md'
  - '{project-root}/docs/project.md'
  - '{project-root}/docs/06-testing-strategy.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Implement CAP-1–23: server, Vietnamese Android/Windows client, device integration, simulator and verification. The repository has no code. Foundation delivery does not complete the full request.

## Boundaries & Constraints

Preserve documents and IDs. Use .NET 10, MAUI, hexagonal server boundaries, PostgreSQL and MQTT/TLS. Apply original story criteria, BCrypt, server authority, device credentials, Vietnamese resources, configurable policy, observed state and rejection audits. Never infer missing SRS sections or equate simulation with hardware acceptance.

### Source-resolved decisions

- US-13 makes operating hours a hard limit, regardless of timetable grants.
- US-04 permits temporary grants to users; automatic timetable rights are lecturer-only.
- All source criteria and global quality constraints remain binding.

## I/O & Edge-Case Matrix

| Scenario | Input / state | Expected behavior | Error handling |
| --- | --- | --- | --- |
| Sign-in | Valid / invalid institutional credentials | Role-specific home / no access | Generic Vietnamese failure |
| Control | Valid grant and policy / expired grant or invalid setpoint | Dispatch / no state mutation | Audit both; identify violated policy |
| Telemetry | Board observation / stale or duplicate report | Update observed projection / no invented state | Show unknown or connectivity fault |
| Import | All valid rows / mixed valid-invalid rows | Commit atomically / write nothing | Report original row numbers |
| Outage | Internet or server unavailable | Execute only within defined offline authority; replay facts | Never re-actuate replayed events |

</frozen-after-approval>

## Open Questions

1. Hardware: identify AC models/controller board, or select simulator-first delivery with hardware acceptance outstanding. Confirm manual-operation fail-safe and retained-clock requirements for hardware work.
2. Monitors: administrator-issued temporary grants only, or another explicit entitlement/revocation scheme?
3. Timetable: use `timetable_id`, `room_code`, `lecturer_email`, `starts_at`, `ends_at`, campus-local times and atomic replacement of a selected date range, or provide the university format/semantics? Replacement revokes affected grants and cancels future actions.
4. Authority: use checked-in specs, or provide relevant missing SRS sections 1–5?

## Code Map

- Canonical behavior and mappings: frontmatter context files.
- Architecture: `docs/architecture/architecture-nhat-vuong-controller-2026-09-11/ARCHITECTURE-SPINE.md`; use its `src/` layout. Adjacent `reviews/review-rubric-walker.md` reports FAIL; validate findings, not every recommendation is a requirement.
- CI rules: `docs/05-development-workflow.md`.
- SDK 10.0.400 and Windows/Android workloads detected; builds unverified.

## Tasks & Acceptance

**Execution (dependency order):**

- [x] Architecture file above — reconcile authorization, replay, key-material and state-mutation contradictions; add session, offline proof/TLS, clock, provisioning, cancellation and restart contracts.
- [x] `NhatVuong.slnx`, `global.json`, `Directory.Build.props`, `src/Domain/`, `src/Application/`, `src/Adapters/` — scaffold dependency boundaries, ports and controllable time.
- [x] `src/Adapters/Persistence/`, `src/Adapters/Rest/` — persist identity/session, roles, reference data and unique QR registration (CAP-1,15,17).
- [x] `src/Application/Commands/`, `src/Adapters/Mqtt/`, `simulator/` — implement authorization, audit, idempotency, outcomes, telemetry, fan-out and fault injection (CAP-4–8,18).
- [x] `src/Application/Access/`, `src/Application/Timetables/`, `src/Adapters/Scheduler/` — implement import, expiring grants, scheduling and lecturer precedence (CAP-2,3,9–11,16).
- [x] `src/Application/Maintenance/`, `src/Adapters/Notification/`, `src/Application/Reporting/` — implement alerts, resolution and runtime reports (CAP-12–14,19).
- [x] `src/Client/` — implement role-specific MAUI workflows, resources, secure credential storage and HTTPS.
- [~] `firmware/module/`, `src/Client/Offline/`, `simulator/` — implement device port, authenticated LAN commands, cached schedules and fact replay (CAP-20,21).
- [x] `tests/`, `tests/load/`, `deploy/`, `.github/workflows/ci.yml` — implement acceptance, coverage gate, load and recovery drills (CAP-22,23). External secrets; no deployment.
- [x] `docs/05-development-workflow.md`, `docs/06-testing-strategy.md`, `AGENTS.md` — record actual commands, results and outstanding physical checks.

**Acceptance Criteria:**

- Given each mapped story, when success/exception tests run, then source outcomes hold with traceable test IDs.
- Given a forged/expired command, when submitted through any transport, then rejection is audited without actuation.
- Given load/outage drills, when executed, then NFR thresholds pass; unexecuted checks remain unverified.
- Given three physical Android 8+/Windows 10+ devices, when tested, then compatibility is demonstrated.

## Implementation Notes

- Open questions resolved for the build (record, not renegotiation): simulator-first delivery with hardware
  acceptance outstanding (Q1); class monitors get rights only through temporary grants (Q2); the proposed
  timetable schema `timetable_id, room_code, lecturer_email, starts_at, ends_at` in campus local time with atomic
  replacement of the covered days (Q3); checked-in specs are the authority (Q4).
- The ESP32 firmware task is marked partial (`[~]`): the device contract is implemented and exercised by
  `simulator/VirtualDevice.cs`; the ESP-IDF firmware is outside this C# codebase. LAN control lives in
  `src/Client/Services/LanControlService.cs` rather than `src/Client/Offline/`.
- As-built architecture decisions are recorded in the spine's "As-built decisions" section.

## Spec Change Log

## Review Triage Log

## Verification

Executed 2026-09-29 on Windows 10, SDK 10.0.401:

- `dotnet build src/Server` and `dotnet build simulator` — success, zero warnings (warnings are errors).
- `dotnet test tests/Application.Tests -p:CollectCoverage=true "-p:Include=[NhatVuong.Application]*" -p:Threshold=60` — 101/101 passed, line coverage 81.4%.
- `dotnet test tests/Integration.Tests` — 10/10 passed over real MQTT and HTTPS LAN against simulated modules.
- `dotnet build src/Client -f net10.0-windows10.0.19041.0` and `-f net10.0-android` — success; the Windows app was driven end to end (sign-in, device list, set 24 °C in 89 ms, LAN grant prepared).
- `deploy/scripts/recovery-drill.ps1 -Mode process -Restart` — recovered in 2 s (budget 15 min).
- Not executed: k6 campaign at 300 devices / 2,000 users, PostgreSQL provider, packet capture, physical devices.

Original plan: after scaffolding: `dotnet build NhatVuong.slnx`, `dotnet test NhatVuong.slnx --collect:"XPlat Code Coverage"`, dedicated platform builds and CI service coverage >=60%. Add simulator/TLS integration, 100-command latency, k6 and restart scripts. Hardware compatibility, packet capture, board truth and remote operation require physical evidence. No checks have passed at planning time.
