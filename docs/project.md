# NHAT VUONG CONTROLLER — SRS, SECTION 6: USER STORIES

**Version:** 1.0  |  **Methodology:** Agile Scrum  |  **Input:** Section 4 (30 FR) and Section 5 (12 NFR)  |  **Output:** 27 User Stories + 8 Definition-of-Done criteria

## 6.1. Conventions

1. **Story template:** *As a \<role\>, I want \<goal\>, so that \<value\>.*
2. **Acceptance criteria** are written in Given – When – Then form, covering both the success path and an exception path.
3. **Requirement grouping:** several FRs serving one user goal are merged into a single story; the traceability matrix in 6.2 guarantees that no requirement is lost in the merge.
4. **Priority:** the MoSCoW scale inherited from Section 4; a story takes the highest priority among the FRs it contains.
5. **Non-functional requirements:** NFR-01…08 are quality constraints and become the Definition of Done (6.3); NFR-09…12 carry independent development and test effort and therefore become stories of their own (Epic G).

**Result:** 30 FR → 23 stories (US-01…US-23); 4 NFR → 4 stories (US-24…US-27); 8 NFR → Definition of Done. All 42 input requirements are mapped. Distribution: 16 Must, 6 Should, 1 Could, 4 non-functional stories.

## 6.2. Requirements Traceability Matrix

**Table 6.1 — Functional requirements**

| ID | Requirement | Priority | Where it lands |
|---|---|---|---|
| FR-A1 | Sign in with a university account (email + password) | Must | US-01 |
| FR-A2 | Derive control rights over a device from the timetable currently in effect | Must | US-02 |
| FR-A3 | Allow control during the class period plus a configurable margin (default 15 minutes) | Must | US-03 |
| FR-A4 | Administrator grants temporary access to a person for a room over a time range | Should | US-04 |
| FR-A5 | Lecturer's command takes precedence over the class monitor's when they conflict | Should | US-07 |
| FR-A6 | Revoke access automatically when the timetable entry expires, with no manual action | Must | US-05 |
| FR-B1 | Switch the air conditioner on and off | Must | US-06 |
| FR-B2 | Set the target temperature within the range permitted by policy | Must | US-07 |
| FR-B3 | Change the operating mode (Cool/Dry/Fan) and the fan speed | Must | US-07 |
| FR-B4 | Reject any command below the minimum temperature threshold configured by the administrator | Should | US-14 |
| FR-B5 | Return a command result within 5 seconds, or report that the device is not responding | Must | US-10 |
| FR-B6 | Control every unit in one room with a single action | Could | US-11 |
| FR-C1 | Display true state read from the control board: on/off, setpoint, mode, room temperature | Must | US-08 |
| FR-C2 | Update the app display within 10 seconds when the physical remote is used | Must | US-09 |
| FR-C3 | Display device connectivity status (Online / Offline / Fault) | Must | US-10 |
| FR-C4 | Display whether the compressor is currently running or idle | Could | US-08 |
| FR-D1 | Switch a unit off when a class ends and no further class is scheduled within X minutes | Must | US-12 |
| FR-D2 | Configure campus-wide operating hours; outside those hours all units switch off | Must | US-13 |
| FR-D3 | Schedule a unit to switch on before a class begins | Should | US-15 |
| FR-D4 | Record and raise an alert when a unit runs continuously for more than N hours | Could | US-12 |
| FR-E1 | Receive and store error codes reported by devices | Must | US-16 |
| FR-E2 | Notify maintenance staff when a new error code is reported | Must | US-16 |
| FR-E3 | Mark an incident as resolved and attach a note | Should | US-17 |
| FR-E4 | Alert when a device stays disconnected for more than 30 minutes during working hours | Should | US-18 |
| FR-F1 | Register a new device by scanning its QR code and assigning it to a room | Must | US-19 |
| FR-F2 | Import the timetable from a CSV or Excel file | Must | US-20 |
| FR-F3 | Report per-row validation errors on import and perform no partial import | Should | US-20 |
| FR-F4 | Manage the list of rooms, buildings and users | Must | US-21 |
| FR-F5 | Write an audit log entry for every command: who, what, which device, when, result | Must | US-22 |
| FR-F6 | View a monthly runtime report by room and by building | Should | US-23 |

**Table 6.2 — Non-functional requirements promoted to their own stories**

| ID | Category | Requirement | Where it lands |
|---|---|---|---|
| NFR-09 | Scalability | Support 300 concurrent devices and 2,000 concurrent users | US-26 |
| NFR-10 | Reliability | A device follows the schedule cached in its module when the server connection is lost | US-25 |
| NFR-11 | Reliability | The app controls the device directly over the LAN, without the cloud | US-24 |
| NFR-12 | Reliability | The server recovers within 15 minutes via health checks and automatic restart | US-27 |

## 6.3. Definition of Done

The constraints below apply to **every** user story. A story is accepted only when it satisfies both its own acceptance criteria and all of the following.

| ID | Category | Criterion | Verification |
|---|---|---|---|
| NFR-01 | Performance | Command latency from tap to device state change ≤ 3 seconds (P95) | Timestamp log analysis over 100 sample commands |
| NFR-02 | Performance | API response time ≤ 500 ms (P95) under 100 concurrent requests | Load test with k6 or JMeter |
| NFR-03 | Security | Passwords stored as BCrypt hashes, never in plaintext | Code review and database inspection |
| NFR-04 | Security | HTTPS/TLS for all traffic; MQTT over TLS with per-device authentication | Code review and packet capture |
| NFR-05 | Security | Every command authorised on the server; the client is never trusted | Code review and integration test with a forged request |
| NFR-06 | Compatibility | Runs on Android 8.0+ and Windows 10+ | Testing on at least three physical devices |
| NFR-07 | Usability | UI in Vietnamese, extensible to other languages via resource files | Code review of the .resx structure |
| NFR-08 | Code Quality | Unit test coverage of the Service layer ≥ 60% | Coverage report from the CI pipeline |

## 6.4. User Story Catalogue

### Epic A — Authentication & Access

**US-01 — Sign in with a university account**  *(Source: FR-A1 | Priority: Must)*

> As a **system user**, I want to **sign in with the email and password issued by the university**, so that **only members of the university can reach the system, each in their own role**.

1. Given valid credentials, When the user confirms sign-in, Then the system grants access to the home screen for that role.
2. Given invalid credentials, When the user confirms sign-in, Then the system rejects the attempt and grants no access.

**US-02 — Derive control rights from the timetable**  *(Source: FR-A2 | Priority: Must)*

> As a **lecturer**, I want to **have the system derive my control rights from the timetable currently in effect**, so that **I am granted control of the room I am teaching in without registering for it manually**.

1. Given the timetable records the lecturer as teaching in room X now, When the device list is opened, Then the system permits control of the units in room X.
2. Given the lecturer has no class in room X, When a command is sent to room X, Then the system rejects it for lack of rights.

**US-03 — Control window around the class period**  *(Source: FR-A3 | Priority: Must)*

> As a **lecturer**, I want to **control the unit throughout the class plus a configurable margin before and after (default 15 minutes)**, so that **I can cool the room before students arrive and still adjust it right after the class**.

1. Given a class starts at 08:00 with a 15-minute margin, When a command is sent at 07:46, Then the system accepts it.
2. Given a class ends at 10:00 with a 15-minute margin, When a command is sent at 10:20, Then the system rejects it.

**US-04 — Grant temporary access**  *(Source: FR-A4 | Priority: Should)*

> As a **administrator**, I want to **grant a user temporary control of a room over a defined time range**, so that **make-up classes, seminars and events outside the standard timetable are still served**.

1. Given the administrator selects a user, a room and a time range, When the grant is saved, Then that user holds control rights for exactly that range.
2. Given the time range has ended, When that user sends a command, Then the system rejects it.

**US-05 — Revoke access automatically on expiry**  *(Source: FR-A6 | Priority: Must)*

> As a **administrator**, I want to **have the system revoke control rights the moment the timetable entry expires**, so that **no rights outlive the class and no manual clean-up is required**.

1. Given the class and its margin have ended, When that moment passes, Then the system revokes control rights with no manual action.
2. Given rights have been revoked, When the user sends a command, Then the system rejects it and writes an audit log entry.

### Epic B — Device Control

**US-06 — Switch the air conditioner on and off**  *(Source: FR-B1 | Priority: Must)*

> As a **authorised user**, I want to **switch the air conditioner on or off from the app**, so that **I can operate the unit without depending on the physical remote**.

1. Given the user holds control rights and the device is Online, When an on command is sent, Then the device switches on and the app shows the new state.
2. Given the user holds no rights in that room, When a command is sent, Then the system rejects it.

**US-07 — Set temperature, mode and fan speed with lecturer precedence**  *(Source: FR-B2, FR-B3, FR-A5 | Priority: Must)*

> As a **lecturer**, I want to **set the temperature within the permitted range, change mode and fan speed, and have my command win any conflict with the class monitor**, so that **I retain full control of classroom conditions during my own class**.

1. Given a temperature within the policy range, When the lecturer sends the command, Then the system applies the new setpoint.
2. Given the class monitor and the lecturer send conflicting commands at the same time, When the system processes them, Then the lecturer's command is applied and the class monitor is notified.

**US-11 — Control a whole room in one action**  *(Source: FR-B6 | Priority: Could)*

> As a **authorised user**, I want to **control every air conditioner in one room with a single action**, so that **I avoid repeating the same operation on each unit in a large room**.

1. Given a room holds several units and the user holds rights, When the room-wide action is used, Then every unit in the room receives and executes the command.
2. Given one unit is Offline, When the room-wide command is sent, Then the system reports which units succeeded and which failed.

**US-14 — Block setpoints below the minimum threshold**  *(Source: FR-B4 | Priority: Should)*

> As a **administrator**, I want to **have the system reject any command setting a temperature below the minimum threshold I configure**, so that **units are never driven to levels that waste power and shorten equipment life**.

1. Given a minimum threshold of 20°C, When a user requests 18°C, Then the system rejects the command and reports the violated threshold.
2. Given a command is rejected, When processing ends, Then the device state remains unchanged.

### Epic C — State Synchronisation

**US-08 — Show true state read from the control board**  *(Source: FR-C1, FR-C4 | Priority: Must)*

> As a **user**, I want to **see the true device state — on/off, setpoint, mode, room temperature and compressor status**, so that **what the app shows always matches the actual condition of the unit**.

1. Given the unit runs at 25°C in Cool mode, When the detail screen is opened, Then the app shows those values as read from the control board.
2. Given the compressor is running or idle, When the detail screen is viewed, Then the app shows the correct compressor status.

**US-09 — Stay in sync with the physical remote**  *(Source: FR-C2 | Priority: Must)*

> As a **user**, I want to **have the app refresh within 10 seconds when the unit is operated by the physical remote**, so that **the app and the remote never show two contradictory states**.

1. Given the app is on the device screen, When someone changes the temperature with the remote, Then the app reflects the new value within 10 seconds.
2. Given the unit is switched off by the remote, When the state syncs, Then the app shows it as off within 10 seconds.

**US-10 — Command feedback and connectivity status**  *(Source: FR-B5, FR-C3 | Priority: Must)*

> As a **user**, I want to **receive a command result within 5 seconds or be told the device is not responding, and always see Online / Offline / Fault status**, so that **I know whether my command took effect and notice device trouble immediately**.

1. Given a command is sent, When the device replies within 5 seconds, Then the app shows the success or failure result.
2. Given no reply arrives within 5 seconds, When the timeout elapses, Then the app reports that the device is not responding.

### Epic D — Scheduling & Energy Saving

**US-12 — Auto-off after class and long-run alerting**  *(Source: FR-D1, FR-D4 | Priority: Must)*

> As a **administrator**, I want to **have units switch off when a class ends with no class within X minutes, and be alerted when a unit runs longer than N hours**, so that **no power is wasted on empty rooms and abnormal operation is caught early**.

1. Given a class ends and none is scheduled within the next X minutes, When X minutes elapse, Then the system switches the room's units off.
2. Given a unit has run continuously for more than N hours, When the threshold is crossed, Then the system records the event and raises an alert.

**US-13 — Configure campus-wide operating hours**  *(Source: FR-D2 | Priority: Must)*

> As a **administrator**, I want to **configure operating hours for the whole campus, outside which every unit switches off automatically**, so that **no unit runs outside the hours the university permits**.

1. Given operating hours of 06:00–22:00, When the clock passes 22:00, Then every running unit is switched off automatically.
2. Given the current time is outside operating hours, When a user sends an on command, Then the system rejects it.

**US-15 — Pre-heat cooling before a class**  *(Source: FR-D3 | Priority: Should)*

> As a **authorised user**, I want to **schedule a unit to switch on before my class begins**, so that **the room has reached a comfortable temperature by the time the class starts**.

1. Given a class at 08:00 and a 10-minute lead time, When the clock reaches 07:50, Then the system switches the room's unit on.
2. Given the schedule is cancelled before it triggers, When the scheduled time arrives, Then the system performs no on command.

### Epic E — Maintenance & Alerting

**US-16 — Capture, store and announce device error codes**  *(Source: FR-E1, FR-E2 | Priority: Must)*

> As a **maintenance officer**, I want to **have the system store error codes reported by devices and notify me when a new one arrives**, so that **I can act on faults promptly instead of waiting for users to report them**.

1. Given a device reports a new error code, When the system receives it, Then the code is stored with the device identifier and the time it occurred.
2. Given a new error code has been stored, When storage completes, Then the system sends a notification to maintenance staff.

**US-17 — Close an incident with a note**  *(Source: FR-E3 | Priority: Should)*

> As a **maintenance officer**, I want to **mark an incident as resolved and attach a note**, so that **the maintenance history is fully traceable for follow-up and reporting**.

1. Given an open incident, When it is marked resolved with a note, Then its status changes to resolved and the note is stored with the record.
2. Given a resolved incident, When it is reviewed later, Then the system shows who resolved it, when, and the note content.

**US-18 — Alert on prolonged disconnection**  *(Source: FR-E4 | Priority: Should)*

> As a **maintenance officer**, I want to **be alerted when a device stays disconnected for more than 30 minutes during working hours**, so that **hardware or network faults surface early instead of going unnoticed**.

1. Given a device is disconnected for more than 30 minutes during working hours, When the threshold is crossed, Then the system raises an alert.
2. Given a device is disconnected for 10 minutes and reconnects, When it returns Online, Then no alert is raised.

### Epic F — Administration

**US-19 — Register a new device by QR code**  *(Source: FR-F1 | Priority: Must)*

> As a **administrator**, I want to **register a new device by scanning its QR code and assigning it to a room**, so that **each device enters the system bound to its correct physical location from installation**.

1. Given the QR code of an unregistered device, When a room is chosen and confirmed, Then the device is added and linked to that room.
2. Given the QR code belongs to an already registered device, When it is scanned, Then the system reports the duplicate and creates no second record.

**US-20 — Import the timetable from CSV or Excel**  *(Source: FR-F2, FR-F3 | Priority: Must)*

> As a **administrator**, I want to **import the timetable from a CSV or Excel file, with per-row error reporting and no partial import**, so that **timetable data stays complete, so control rights are never derived from a half-loaded file**.

1. Given a fully valid file, When it is uploaded, Then every row is imported and the system reports the number of rows loaded.
2. Given a file containing invalid rows, When it is uploaded, Then the system lists the errors by row number and writes no rows to the database.

**US-21 — Manage rooms, buildings and users**  *(Source: FR-F4 | Priority: Must)*

> As a **administrator**, I want to **create, edit and delete room, building and user records**, so that **the system's reference data always matches the university's actual structure**.

1. Given a record is created or edited, When the change is saved, Then the updated data takes effect across the system.
2. Given a room still has devices assigned, When the administrator deletes it, Then the system reports the constraint and blocks the deletion.

**US-22 — Audit-log every control command**  *(Source: FR-F5 | Priority: Must)*

> As a **administrator**, I want to **have every control command logged with the actor, the command, the device, the timestamp and the result**, so that **the university can establish accountability whenever a dispute or incident arises**.

1. Given any command is sent, When it is processed, Then the system writes an audit entry containing all five fields.
2. Given a command is rejected for insufficient rights, When processing ends, Then the system still logs it with a rejected result.

**US-23 — Monthly runtime reporting**  *(Source: FR-F6 | Priority: Should)*

> As a **administrator**, I want to **view monthly runtime broken down by room and by building**, so that **the university can assess usage and plan energy savings on real evidence**.

1. Given runtime data exists for the selected month, When the report is opened, Then the system shows total runtime per room and aggregated per building.
2. Given the selected month has no data, When the report is opened, Then the system shows a no-data message rather than an error.

### Epic G — Reliability & Scalability

**US-24 — Direct LAN control when the internet is down**  *(Source: NFR-11 | Priority: non-functional)*

> As a **user**, I want to **control the device directly over the local network when the app and the device share a LAN, without the cloud server**, so that **classes are not disrupted when the university loses its internet connection**.

1. Given the internet is down but the local WiFi is up, When a command is sent from the app on the same LAN, Then the device receives and executes it without the cloud.
2. Given the internet is restored, When the app reconnects to the server, Then state changes made while offline are synchronised upward.

**US-25 — Device follows its cached schedule when the server is unreachable**  *(Source: NFR-10 | Priority: non-functional)*

> As a **administrator**, I want to **have a device keep following the schedule cached in its module when the server connection is lost**, so that **essential behaviour such as scheduled shutdown survives a server outage**.

1. Given the device has lost its connection to the server, When a cached scheduled time arrives, Then the device performs the scheduled action on its own.
2. Given the connection is restored, When the device reconnects, Then the actions taken while offline are reported to the server.

**US-26 — Operate at full campus scale**  *(Source: NFR-09 | Priority: non-functional)*

> As a **system administrator**, I want to **have the system remain stable with at least 300 concurrent devices and 2,000 concurrent users**, so that **the system can be deployed across the whole campus, not merely at pilot scale**.

1. Given 300 simulated devices and a load equivalent to 2,000 users, When the stress test runs, Then the system stays stable with no failure or outage.
2. Given the system is at peak load, When commands are issued, Then the NFR-01 and NFR-02 performance thresholds still hold.

**US-27 — Recover automatically from a server failure**  *(Source: NFR-12 | Priority: non-functional)*

> As a **system administrator**, I want to **have the server recover within 15 minutes through health checks and automatic restart**, so that **service downtime is bounded, limiting the campus-wide impact of a failure**.

1. Given the server process stops unexpectedly, When the health check detects it, Then the server is restarted automatically.
2. Given the server has restarted, When recovery completes, Then total downtime does not exceed 15 minutes.

## 6.5. Notes

1. The identifier **US-27** is assigned to NFR-12: Section 5 classifies that requirement as an "own story" but leaves it unnumbered, so the next free identifier after US-01…US-26 is used to keep identifiers unique.
2. The parameters **X** (idle interval before automatic shutdown after a class) and **N** (continuous runtime that triggers an alert) remain configuration parameters as defined in Section 4; concrete values are set by the administrator at deployment.
3. The permission model follows the scope fixed in Section 1: control rights originate from the university timetable, not from an end user scanning a QR code. QR scanning is reserved for administrators registering a device (US-19).
4. Acceptance criteria are stated as user-observable behaviour and do not prescribe a technical solution, keeping requirements independent of design.
