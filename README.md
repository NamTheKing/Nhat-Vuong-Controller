# Nhat Vuong Controller

Campus-wide air-conditioner control for a university: a .NET 10 server (REST API, scheduler, embedded MQTT
broker), a .NET MAUI client for Android 8.0+ and Windows 10+ with a Vietnamese UI, and a device-module simulator
that speaks the same MQTT and LAN contracts as the classroom modules.

Control rights come from the timetable: a lecturer opens the app in the room they are timetabled into and switches
the unit on without registering for anything; when the last class ends the units switch themselves off.

Requirements, backlog and sprint plan are in [docs/](docs/README.md). This file covers the code.

## Repository layout

```text
src/
  Domain/                 entities and pure policy rules (no framework references)
  Application/            the core: command pipeline, authorisation, scheduling, alerts, reports, ports
  Contracts/              wire schema shared by server, app and simulator (REST DTOs, MQTT envelope, LAN protocol)
  Adapters/
    Persistence/          EF Core — SQLite (dev/test) or PostgreSQL (staging/prod)
    Mqtt/                 embedded MQTTnet broker (per-device auth + topic ACL) and the device gateway
    Rest/                 Minimal API endpoints, JWT auth, problem+json, CSV/Excel timetable reader
    Scheduler/            PeriodicTimer loop: auto-off, closing time, pre-cool, expiry, alerts
    Notification/         log and SMTP delivery behind INotificationPort
  Server/                 composition root, health checks, grant-signing key
  Client/                 .NET MAUI app (Android + Windows), resources in Resources/Strings/*.resx
simulator/                virtual device fleet: MQTT module twin, fault injection, LAN endpoint, cached schedule
tests/
  Application.Tests/      unit tests of the core on SQLite with a controllable clock (+ architecture tests)
  Integration.Tests/      real server + real MQTT + simulated modules + LAN, via WebApplicationFactory
  load/                   k6 profile and the NFR-01/02, US-26 campaign
deploy/                   Dockerfile, docker-compose (PostgreSQL, auto-restart, autoheal), systemd, drill scripts
docs/                     Scrum process, backlog, release plan, SRS §6, spec, architecture
```

The architecture is hexagonal (see the [architecture spine](docs/architecture/architecture-nhat-vuong-controller-2026-09-11/ARCHITECTURE-SPINE.md)):
REST, the scheduler and device-report ingest are three driving adapters that all enter one command pipeline
(`CommandService`), which checks policy, authorises against `AccessGrant` rows, writes the audit entry before
dispatch, sends one correlated command per unit and records the device's reply.

## Quick start (development)

Prerequisites: .NET SDK 10.0.400+; for the app, the MAUI workload (`dotnet workload install maui`).

```sh
# 1. Server: SQLite, embedded MQTT broker on 1883, demo campus seeded (appsettings.Development.json)
dotnet dev-certs https --trust                                   # once, for the local HTTPS endpoint
dotnet run --project src/Server --launch-profile https          # https://localhost:7180

# 2. Four simulated modules (the demo devices), LAN endpoint on https://localhost:7443
dotnet run --project simulator                                   # type 'help' for fault injection

# 3. App
dotnet build src/Client -t:Run -f net10.0-windows10.0.19041.0    # Windows
dotnet build src/Client -t:Run -f net10.0-android                # Android emulator (server at https://10.0.2.2:7180)
```

Demo accounts (password `Demo@12345`, development seed only):

| Account | Role | What to try |
|---|---|---|
| `giangvien1@nhatvuong.edu.vn` | Lecturer | Has a class in A101 now: control both units, schedule a pre-cool |
| `giangvien2@nhatvuong.edu.vn` | Lecturer | Class later today in A102 — A101 commands are rejected |
| `loptruong@nhatvuong.edu.vn` | Class monitor | Temporary grant in A101; lecturer's commands take precedence |
| `baotri@nhatvuong.edu.vn` | Maintenance | Incidents; in the simulator type `error SIM-A101-1 E5` |
| `admin@nhatvuong.edu.vn` | Administrator | Campus data, QR registration, timetable import, grants, policy, audit, reports |

Operating hours default to 06:00–22:00 campus time; outside them on-commands are rejected by design (US-13).
Change them on the Admin → Operating policy screen.

## Sprint → story → code

The release plan ([docs/03-release-plan.md](docs/03-release-plan.md)) maps the 27 stories to sprints 01–08.
All of them are implemented; "verified" below means automated tests against the simulator. Hardware, physical
device and full-scale campaigns are listed under [Not yet verified](#not-yet-verified).

| Sprint | Stories | Where | Verified by |
|---|---|---|---|
| 0 | CI, simulator, broker, architecture skeleton | `.github/workflows/ci.yml`, `simulator/`, `Adapters/Mqtt` | CI, integration suite |
| 01 | US-01 sign-in, US-21 rooms/buildings/users, US-19 QR registration | `Identity/`, `Administration/`, `DeviceRegistryService` | `AdministrationTests`, `BrokerSecurity` |
| 02 | US-06 on/off, US-08 true state, US-10 result + connectivity, US-22 audit | `CommandService`, `DeviceReportService` | `AccessAndCommandTests`, `SwitchOnRoundTrip`, `NoReplyTimesOut` |
| 03 | US-20 timetable import, US-02 rights, US-03 window, US-05 expiry | `TimetableImportService`, `AccessService` | `TimetableImportTests`, `ControlWindow`, `ExpiryRevokesAutomatically` |
| 04 | US-07 setpoint/mode/fan + lecturer precedence, US-09 remote sync, US-14 minimum | `CommandPolicy`, `CommandService` | precedence tests, `RemoteSync` |
| 05 | US-13 operating hours, US-12 auto-off + long-run, US-15 pre-cool, US-04 temporary grants | `SchedulingService`, `MonitoringService` | `SchedulingTests` |
| 06 | US-16 error codes, US-17 resolve, US-18 disconnect alert, US-23 runtime report | `MaintenanceService`, `ReportingService` | `MaintenanceAndReportingTests`, `ErrorCodeFlow` |
| 07 | US-24 LAN control, US-25 cached schedule | `OfflineGrantService`, `LanControlService`, `VirtualDevice` | `LanControl`, `OfflineFactsReplay`, `CachedScheduleContents` |
| 08 | US-26 scale, US-27 recovery, US-11 room-wide | `tests/load`, `deploy/`, `ExecuteRoomAsync` | `RoomWide`; recovery drill; k6 profile |

## Tests

```sh
dotnet test tests/Application.Tests                  # 101 unit tests
dotnet test tests/Application.Tests -p:CollectCoverage=true "-p:Include=[NhatVuong.Application]*" -p:Threshold=60   # NFR-08 gate
dotnet test tests/Integration.Tests                  # 10 end-to-end tests over real MQTT + LAN
```

Last local run (2026-09-29): 111/111 passed; service-layer line coverage 81.4%.

## Deployment

`deploy/docker-compose.yml` runs PostgreSQL, the server (MQTT over TLS on 8883, API on 8080 behind your
TLS-terminating proxy) and an autoheal sidecar. Secrets come from `deploy/.env` (see `.env.example`); nothing
secret is committed. `deploy/systemd/` has the bare-metal equivalent with a readiness watchdog.

- Health: `GET /health/live`, `GET /health/ready` (database, MQTT gateway/broker, scheduler heartbeat).
- Recovery drill (US-27): `deploy/scripts/recovery-drill.ps1 -Mode docker` — kills the server and measures
  downtime against the 15-minute budget.
- MQTT TLS certificate: `deploy/scripts/new-mqtt-cert.ps1`.
- Backups: the audit log and timetable-derived grants live in PostgreSQL; schedule `pg_dump` of the `nhatvuong`
  database at least daily (retention is an open decision, see the architecture spine).

## Design decisions made while building

Recorded in full in the architecture spine's "As-built decisions" section. The ones that change the stack:

- **Embedded MQTTnet broker** instead of a separate Mosquitto: the broker authenticates each module against the
  device registry (BCrypt-hashed per-device passwords) and confines it to its own topics. `Mqtt:EmbeddedBroker=false`
  switches to an external broker.
- **ECDSA P-256 (ES256)** for LAN grants instead of Ed25519: it is built into .NET on Android and Windows and into
  ESP32 mbedTLS. The app proves possession of the key named in the grant by signing each LAN command, and pins the
  module's TLS certificate to the thumbprint the server relays.
- **SQLite for development and tests**, PostgreSQL for staging/production, selected by `Database:Provider`.

## Not yet verified

These need physical equipment or a staging environment and remain open against the Definition of Done:

- Hardware-in-the-loop with real modules and air conditioners (board-truth, physical remote, fail-safe). The ESP32
  firmware itself is not part of this C# codebase; `simulator/VirtualDevice.cs` is the reference implementation
  of the device contract it must follow.
- NFR-06 on three physical devices (Android 8+ phones, Windows 10 PC). Both client targets build; the Windows app
  was exercised end to end against the simulator.
- US-26 full campaign (300 devices, 2,000 users) and NFR-02 under load; the k6 profile and fleet tooling are ready.
- NFR-04 packet capture; PostgreSQL provider against a real database (tests run on SQLite).
