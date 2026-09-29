# Traceability

Companion to `SPEC.md`. Maps every requirement and user story in the source
(`../../project.md`, SRS §6) onto this contract, so no requirement is lost when 42
requirements are distilled into 23 capabilities.

The original Given–When–Then acceptance criteria remain in the source document. This
contract restates them as capability `success` criteria; consult the source when you need
the full two-path form (success plus exception) for a specific story.

## Functional requirements → capabilities

| FR | Requirement | Lands in |
|----|-------------|----------|
| FR-A1 | Sign in with a university account | CAP-1 |
| FR-A2 | Derive control rights from the timetable in effect | CAP-2 |
| FR-A3 | Allow control during the class plus a configurable margin | CAP-2 |
| FR-A4 | Administrator grants temporary access for a room and time range | CAP-3 |
| FR-A5 | Lecturer's command takes precedence over the class monitor's | **Constraint** (kernel) |
| FR-A6 | Revoke access automatically when the timetable entry expires | CAP-2 |
| FR-B1 | Switch the air conditioner on and off | CAP-4 |
| FR-B2 | Set target temperature within policy range | CAP-5 |
| FR-B3 | Change operating mode and fan speed | CAP-5 |
| FR-B4 | Reject commands below the configured minimum temperature | CAP-5 |
| FR-B5 | Return a command result within 5 seconds or report not responding | CAP-8 |
| FR-B6 | Control every unit in one room with a single action | CAP-6 |
| FR-C1 | Display true state read from the control board | CAP-7 |
| FR-C2 | Update the display within 10 seconds when the physical remote is used | CAP-7 |
| FR-C3 | Display connectivity status (Online / Offline / Fault) | CAP-8 |
| FR-C4 | Display whether the compressor is running or idle | CAP-7 |
| FR-D1 | Switch off when a class ends and none follows within X minutes | CAP-9 |
| FR-D2 | Campus-wide operating hours; outside them all units off | CAP-10 |
| FR-D3 | Schedule a unit to switch on before a class begins | CAP-11 |
| FR-D4 | Record and alert when a unit runs continuously beyond N hours | CAP-13 |
| FR-E1 | Receive and store device error codes | CAP-12 |
| FR-E2 | Notify maintenance staff of a new error code | CAP-12 |
| FR-E3 | Mark an incident resolved and attach a note | CAP-14 |
| FR-E4 | Alert when a device is disconnected >30 min in working hours | CAP-13 |
| FR-F1 | Register a device by QR code, assigned to a room | CAP-15 |
| FR-F2 | Import the timetable from CSV or Excel | CAP-16 |
| FR-F3 | Per-row validation errors, no partial import | CAP-16 |
| FR-F4 | Manage rooms, buildings and users | CAP-17 |
| FR-F5 | Audit-log every command: who, what, device, when, result | CAP-18 |
| FR-F6 | Monthly runtime report by room and building | CAP-19 |

30 of 30 mapped.

## Non-functional requirements → contract

| NFR | Category | Lands in |
|-----|----------|----------|
| NFR-01 | Performance — command latency ≤ 3 s P95 | `quality-constraints.md`; re-asserted in CAP-22 |
| NFR-02 | Performance — API ≤ 500 ms P95 | `quality-constraints.md`; re-asserted in CAP-22 |
| NFR-03 | Security — BCrypt password storage | `quality-constraints.md` |
| NFR-04 | Security — TLS everywhere, per-device MQTT auth | `quality-constraints.md` |
| NFR-05 | Security — server-side authorisation | `quality-constraints.md` **and** kernel Constraint |
| NFR-06 | Compatibility — Android 8.0+, Windows 10+ | `quality-constraints.md` |
| NFR-07 | Usability — Vietnamese UI via resource files | `quality-constraints.md` |
| NFR-08 | Code quality — service-layer coverage ≥ 60% | `quality-constraints.md` |
| NFR-09 | Scalability — 300 devices, 2,000 users | CAP-22 |
| NFR-10 | Reliability — device follows cached schedule offline | CAP-21 |
| NFR-11 | Reliability — direct LAN control without cloud | CAP-20 |
| NFR-12 | Reliability — server recovery within 15 minutes | CAP-23 |

12 of 12 mapped. **42 of 42 source requirements accounted for.**

## User stories → capabilities

| US | Lands in | | US | Lands in |
|----|----------|-|----|----------|
| US-01 | CAP-1 | | US-15 | CAP-11 |
| US-02 | CAP-2 | | US-16 | CAP-12 |
| US-03 | CAP-2 | | US-17 | CAP-14 |
| US-04 | CAP-3 | | US-18 | CAP-13 |
| US-05 | CAP-2 | | US-19 | CAP-15 |
| US-06 | CAP-4 | | US-20 | CAP-16 |
| US-07 | CAP-5 + Constraint | | US-21 | CAP-17 |
| US-08 | CAP-7 | | US-22 | CAP-18 |
| US-09 | CAP-7 | | US-23 | CAP-19 |
| US-10 | CAP-8 | | US-24 | CAP-20 |
| US-11 | CAP-6 | | US-25 | CAP-21 |
| US-12 | CAP-9 + CAP-13 | | US-26 | CAP-22 |
| US-13 | CAP-10 | | US-27 | CAP-23 |
| US-14 | CAP-5 | | | |

27 of 27 mapped.

## Where the distillation changed the source's grouping

Three merges and two splits, recorded so the difference is deliberate rather than lost:

- **US-02, US-03, US-05 merged into CAP-2.** All three describe one mechanism — rights
  derived from the timetable, bounded by a window, expiring on their own. Splitting them
  produces three capabilities that cannot be built or tested independently.
- **US-08 and US-09 merged into CAP-7.** Both assert the same property: the app shows the
  control board's truth. The 10-second bound for remote-originated changes is a criterion
  of that property, not a separate capability.
- **US-14 merged into CAP-5.** The minimum-setpoint rejection is the exception path of
  setting a temperature, not a feature of its own.
- **US-12 split across CAP-9 and CAP-13.** The source pairs automatic shutdown with
  long-run alerting because both are triggered by elapsed time, but they serve different
  users and fail independently: shutdown is energy policy, the alert is maintenance.
- **US-07 split between CAP-5 and a kernel Constraint.** Setting temperature/mode/fan is a
  capability; lecturer-over-monitor precedence is an arbitration rule that bends the
  authorisation design and applies beyond this one capability.

## Wrapper-only content, deliberately not absorbed

Recorded so the drop is on the record rather than silent:

- `docs/01-scrum-process.md`, `docs/03-release-plan.md`, `docs/05-development-workflow.md`,
  `docs/06-testing-strategy.md`, `docs/templates/` — Scrum process, sprint cadence,
  branching, ceremony templates. Process, not what-to-build.
- `docs/02-product-backlog.md` — ordering, story points and sprint assignment. Planning
  state, superseded whenever the plan changes.
- `docs/04-definition-of-ready-and-done.md` — its NFR-01…08 table is identical to SRS §6.3
  and is preserved in `quality-constraints.md`; the surrounding DoR/DoD checklists are
  process.
- SRS §6.1 (story-template conventions) and §6.2 table structure — the conventions are
  restated as this file; the requirement content is preserved above.
- SRS §6.5 notes — absorbed: note 2 (X and N stay configurable) and note 3 (rights come
  from the timetable, not QR) became kernel Constraints; note 4 (criteria state observable
  behaviour) is honoured by construction; note 1 (why US-27 is numbered as it is) is
  bookkeeping about the source's own numbering.
