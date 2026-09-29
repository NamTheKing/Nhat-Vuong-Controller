---
id: SPEC-nhat-vuong-controller
companions:
  - quality-constraints.md
  - traceability.md
  - ../../../AGENTS.md
sources:
  - ../../project.md
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# Nhat Vuong Controller

## Why

A pain to solve. University classrooms run wall-split air conditioners that nobody owns: a lecturer cannot turn one on without the physical remote, nobody is accountable when a unit runs all night in an empty room, and maintenance learns a unit is broken only when someone complains. Existing smart-AC products solve the wrong half of this — they can schedule a device but have no idea who is teaching in the room right now, so they either lock control to an administrator or hand it to anyone with the app. The force behind this work is that a university already knows, hour by hour, who should be in which room: the timetable. Deriving control rights from it makes the permission problem disappear, and makes automatic shutdown safe because the system knows when a room is genuinely finished for the day.

## Capabilities

- **CAP-1**
  - **intent:** A university member signs in with their institutional email and password and reaches the functions of their role.
  - **success:** Valid credentials open the role's home screen; invalid credentials grant no access.

- **CAP-2**
  - **intent:** A lecturer receives control of the room they are timetabled into, for the duration of the class plus a configurable margin, without registering for it.
  - **success:** A command sent inside the class window plus margin is accepted; the same command outside it is rejected, and rights lapse at expiry with no manual action.

- **CAP-3**
  - **intent:** An administrator grants a named person control of a named room for a defined time range, covering make-up classes, seminars and events outside the timetable.
  - **success:** The grantee can command that room inside the range and is rejected outside it.

- **CAP-4**
  - **intent:** An authorised user switches an air conditioner on or off from the app instead of the physical remote.
  - **success:** The unit changes state and the app shows the new state; a user without rights in that room is rejected.

- **CAP-5**
  - **intent:** An authorised user sets target temperature, operating mode (Cool/Dry/Fan) and fan speed within the range policy permits.
  - **success:** An in-range setpoint is applied; a setpoint below the configured minimum is rejected naming the violated threshold, and device state is unchanged.

- **CAP-6**
  - **intent:** A user applies one action to every air conditioner in a room rather than repeating it per unit.
  - **success:** Every unit in the room executes the command, and the result reports which units succeeded and which failed.

- **CAP-7**
  - **intent:** The app shows the unit's true condition — on/off, setpoint, mode, room temperature and compressor running/idle — including changes made with the physical remote.
  - **success:** Displayed values match the control board; a change made by the physical remote appears in the app within 10 seconds.

- **CAP-8**
  - **intent:** A user learns whether a command took effect and whether a device is reachable.
  - **success:** A command result arrives within 5 seconds or is reported as not responding; every device shows Online, Offline or Fault.

- **CAP-9**
  - **intent:** The system switches a room's units off when its class ends and no further class is scheduled within a configurable interval.
  - **success:** With no class scheduled in the next X minutes, the room's units are off X minutes after the class ends.

- **CAP-10**
  - **intent:** An administrator defines campus-wide operating hours outside which no unit runs.
  - **success:** Every running unit switches off when the closing hour passes, and on-commands sent outside operating hours are rejected.

- **CAP-11**
  - **intent:** A unit starts before a class begins so the room is comfortable on arrival.
  - **success:** The unit switches on at the class start minus the configured lead time; a cancelled schedule produces no command.

- **CAP-12**
  - **intent:** Maintenance staff learn of device faults from the system rather than from users.
  - **success:** A device-reported error code is stored with its device identifier and time of occurrence, and a notification reaches maintenance staff.

- **CAP-13**
  - **intent:** The system raises alerts on abnormal operation that no error code reports — a device unreachable too long, or a unit running beyond a sane duration.
  - **success:** An alert is raised after a device is disconnected more than 30 minutes during working hours, and after a unit runs continuously beyond N hours; a device that reconnects before the threshold raises none.

- **CAP-14**
  - **intent:** Maintenance staff close an incident with a written note so the history is traceable.
  - **success:** The incident becomes resolved and later shows who resolved it, when, and the note.

- **CAP-15**
  - **intent:** An administrator registers a device by scanning its QR code and binding it to a room at installation.
  - **success:** An unregistered code creates a device linked to the chosen room; an already-registered code is reported as duplicate and creates no second record.

- **CAP-16**
  - **intent:** An administrator loads the timetable from a CSV or Excel file, since control rights depend on it.
  - **success:** A valid file imports every row and reports the count; a file with invalid rows reports errors by row number and writes nothing.

- **CAP-17**
  - **intent:** An administrator maintains the rooms, buildings and users the system reasons about.
  - **success:** Saved changes take effect system-wide; deleting a room that still has devices is blocked and the constraint reported.

- **CAP-18**
  - **intent:** The university can establish who did what to which device when a dispute or incident arises.
  - **success:** Every command writes an audit entry with actor, command, device, timestamp and result — including commands rejected for insufficient rights.

- **CAP-19**
  - **intent:** The university sees where air-conditioner runtime is actually spent, to plan energy savings on evidence.
  - **success:** A selected month shows total runtime per room aggregated per building; a month with no data shows a no-data message, not an error.

- **CAP-20**
  - **intent:** A class continues to control its air conditioner when the university loses its internet connection but the local network is up.
  - **success:** With the internet down, a command from an app on the same LAN reaches and executes on the device without the cloud; changes made offline synchronise upward on reconnection.

- **CAP-21**
  - **intent:** A device keeps honouring its schedule when it cannot reach the server, so scheduled shutdown survives a server outage.
  - **success:** A disconnected device performs its cached scheduled action at the scheduled time and reports the action to the server on reconnection.

- **CAP-22**
  - **intent:** The system serves a whole campus rather than a pilot.
  - **success:** With 300 concurrent devices and load equivalent to 2,000 concurrent users, the system stays up and the latency and API-response limits in `quality-constraints.md` still hold.

- **CAP-23**
  - **intent:** A server failure resolves itself rather than waiting for a person.
  - **success:** A stopped server process is detected by health check and restarted automatically, with total downtime no greater than 15 minutes.

## Constraints

- Control rights originate from the university timetable, never from an end user scanning a QR code; QR scanning is an administrator device-registration action only.
- A lecturer's command overrides a class monitor's when the two conflict, and the class monitor is notified.
- Every command is authorised on the server; the client is never trusted.
- Device state is read from the unit's control board, not inferred from the last command sent.
- Timing and threshold parameters — pre/post-class margin (default 15 minutes), idle interval X before automatic shutdown, continuous-runtime limit N, minimum permitted setpoint, campus operating hours — are administrator-configurable at deployment and never hard-coded.
- A rejected command leaves device state unchanged and is still written to the audit log.
- The quality constraints in `quality-constraints.md` (NFR-01…08) apply to every capability above, not to a subset.

## Non-goals

- No end-user self-service claiming of a device: a lecturer or student cannot scan a QR code to obtain control.
- No control of device classes other than air conditioners — not lighting, projectors, door locks or AV equipment.
- No integration with an external building-management system or BACnet plant.
- No learned or optimised setpoints: the system enforces policy and schedule, it does not decide what temperature is best.
- No billing, chargeback or per-department cost allocation from the runtime data; CAP-19 reports runtime only.

## Success signal

A lecturer walks into the room they are timetabled into, opens the app, and turns the air conditioner on without having registered for anything — and when the last class in the building ends, every unit switches itself off without a human deciding to. The monthly runtime report for the following month is visibly lower than the one before it.

## Assumptions

- The timetable is authoritative and current; control rights are only as correct as the imported data (CAP-16 gates CAP-2).
- "Class monitor" is a distinct role with some path to control rights, since the precedence constraint references it — but no requirement in the source grants that role rights. See open questions.
- Non-goals for BMS integration, other device classes, learned setpoints and billing are inferred from the absence of any requirement covering them, not from an explicit exclusion in the source.
- The device module can read state from the control board of whatever air conditioners the university already owns; the source does not name a brand, model or protocol.

## Open Questions

- Which air-conditioner brands and control-board protocols must the device module support? This decides whether one hardware design suffices or a per-brand variant is needed, and the source never states it.
- CAP-10 switches off every running unit when operating hours end, but CAP-2 grants a lecturer rights for a class plus margin. What happens to a class still running at the closing hour — forced off, or does an active class suspend the envelope?
- How does a class monitor obtain control rights at all? The lecturer-precedence constraint implies they can hold them, but no requirement grants them.
- What is the timetable file's expected schema — which columns identify room, lecturer, and time range, and how do they resolve to registered devices?
- If the device module fails or is removed, does the air conditioner remain operable from its physical remote? The source is silent on fail-safe behaviour for a control system attached to existing equipment.
- Sections 1–5 of the SRS are cited throughout the source but are not in this repository; they may carry scope or architecture decisions this contract should inherit.
