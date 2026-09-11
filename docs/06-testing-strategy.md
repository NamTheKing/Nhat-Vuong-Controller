# 06 — Testing Strategy

How the team verifies the 27 stories and 12 NFRs. Principle: **every Given–When–Then acceptance criterion maps to at least one automated or scripted test**, and the SRS-defined verification methods (§6.3) are implemented literally.

## 1. Test levels

| Level | Scope | Tooling (indicative) | Runs |
|-------|-------|----------------------|------|
| Unit | Service-layer logic: authorisation rules, control-window arithmetic, threshold checks, import validation | xUnit-style framework + mocks | Every commit; coverage gate ≥ 60% (NFR-08) |
| Integration | REST API + DB + MQTT bridge against the **device simulator** | API test client + simulator fleet | Every PR |
| End-to-end | App → server → MQTT → device (simulator or hardware), full command round-trip | Scripted E2E suite + manual protocol | Sprint review prep; nightly on `main` (simulator) |
| Load / stress | NFR-01, NFR-02, US-26 | k6 or JMeter + simulator fleet | Nightly profile; full campaign in Sprint 08 |
| Hardware-in-the-loop | Physical device module + real AC unit + physical remote | Manual scripted sessions | Each sprint for device-facing stories |

## 2. The device simulator (Sprint 0 deliverable)

A software twin of the device module, able to run as a fleet of N instances. It must support:

- MQTT over TLS with per-device credentials (so NFR-04 paths are exercised even in tests).
- State reporting (on/off, setpoint, mode, room temperature, compressor status) and error-code injection (US-16).
- Fault injection: no-reply (US-10 timeout), disconnect/reconnect (US-18), delayed responses (NFR-01).
- "Physical remote" events: out-of-band state changes to test US-09 sync.
- 300+ concurrent instances for US-26 stress tests.

## 3. Acceptance-criteria coverage map

Where each epic's criteria are primarily verified:

| Epic | Success paths | Exception paths | Notes |
|------|---------------|-----------------|-------|
| A — Auth & access | Integration (API) | Integration: expired window, revoked rights, forged request (NFR-05) | Time-dependent rules tested with a controllable clock, not real waiting |
| B — Device control | E2E via simulator | Unit (threshold rejection US-14) + integration (no-rights rejection) | Latency measured per NFR-01 method: timestamp logs over 100 commands |
| C — State sync | E2E + hardware-in-the-loop | Simulator no-reply / stale-state injection | US-09's 10 s bound asserted in the nightly E2E run |
| D — Scheduling | Integration with simulated clock | Cancelled schedule, outside-hours rejection | Scheduler tests never depend on wall-clock time |
| E — Maintenance | Integration (error-code injection) | Reconnect-before-threshold produces no alert (US-18-2) | Notification delivery asserted via a test notification sink |
| F — Administration | Integration + unit (import validator) | Per-row errors with **zero** partial import (US-20-2), duplicate QR, blocked room deletion | Import tests include a mixed valid/invalid fixture file |
| G — Reliability | Scripted drills (see §5) | — | Pass/fail criteria from the release gate |

## 4. NFR verification (who/how, per SRS §6.3 and §6.2)

| NFR | Method | Automated? |
|-----|--------|-----------|
| NFR-01 latency ≤ 3 s P95 | Log-timestamp analysis over 100 sample commands, nightly | Yes (report artifact) |
| NFR-02 API ≤ 500 ms P95 @100 concurrent | k6/JMeter profile, nightly + per release | Yes |
| NFR-03 BCrypt passwords | Code review checklist + DB inspection script | Semi |
| NFR-04 TLS everywhere | Code review + packet capture session per release | Semi |
| NFR-05 server-side authorisation | Forged-request integration test in CI | Yes |
| NFR-06 Android 8+/Win 10+ | Device matrix session on ≥ 3 physical devices per milestone | Manual |
| NFR-07 Vietnamese UI via resources | Resource-file lint + review | Semi |
| NFR-08 coverage ≥ 60% | CI coverage gate | Yes |
| NFR-09 scale (US-26) | 300 simulated devices + 2,000-user load, Sprint 08 campaign | Yes |
| NFR-10/11 offline behaviour (US-24/25) | Outage drills, §5 | Scripted manual |
| NFR-12 recovery (US-27) | Kill-and-recover drill with health-check monitoring | Scripted manual |

## 5. Resilience drills (Sprints 07–08, repeated before release)

1. **Internet cut (US-24):** disconnect the campus uplink while app and device share the LAN → commands still execute; on restore, offline state changes sync upward.
2. **Server cut (US-25):** stop the server during a scheduled shutdown window → device executes its cached schedule; on reconnect, offline actions are reported.
3. **Server crash (US-27):** kill the server process → health check triggers automatic restart; measured downtime ≤ 15 minutes.
4. **Peak load (US-26):** full fleet + user load while asserting NFR-01/NFR-02 still hold (US-26-2).

Each drill has a written runbook, expected observations, and a recorded result attached to the release gate checklist.

## 6. Defect management

- File with [templates/bug-report.md](templates/bug-report.md); link to the violated story/criterion when known.
- Severity: **blocker** (Must path broken / DoD violation) — fixed in-sprint before new work; **major** — next sprint at the latest; **minor** — PO prioritises in backlog.
- Every fixed bug gets a regression test in the level where it *should* have been caught.
