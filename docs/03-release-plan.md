# 03 — Release Plan

Sprint-by-sprint plan for delivering the 27 stories in [02-product-backlog.md](02-product-backlog.md). This is a forecast, not a commitment: the PO re-plans at every sprint boundary based on measured velocity.

## Assumptions

- **Sprint length:** 2 weeks; **Sprint 0** (setup) precedes Sprint 01.
- **Assumed initial velocity:** 12–16 SP per sprint. Recalibrate after Sprint 02 using the actual average.
- **Total scope:** 105 SP → 8 development sprints ≈ **4.5 months** including Sprint 0.
- Firmware, backend and client work proceed in parallel within each sprint; the device **simulator** (built in Sprint 0) decouples software sprints from hardware availability.

## Sprint map

| Sprint | Goal | Stories | SP |
|--------|------|---------|---:|
| **0** | Development environment ready: repo, CI, architecture skeleton, MQTT broker (TLS), device simulator, issue tracker | — (technical tasks only) | — |
| **01** | An administrator can set up the campus: accounts, rooms, buildings, registered devices | US-01, US-21, US-19 | 11 |
| **02** | A user can switch an AC on/off from the app and trust what the app shows | US-06, US-08, US-10, US-22 | 14 |
| **03** | Control rights flow automatically from the imported timetable | US-20, US-02, US-03, US-05 | 14 |
| **04** | Full in-class control: temperature, mode, fan, lecturer precedence, remote sync | US-07, US-09, US-14 | 12 |
| **05** | The campus saves energy without human action | US-13, US-12, US-15, US-04 | 14 |
| **06** | Maintenance staff learn about faults before users do | US-16, US-17, US-18, US-23 | 13 |
| **07** | Classes survive an internet or server outage | US-24, US-25 | 16 |
| **08** | The system holds at campus scale and heals itself | US-26, US-27, US-11 | 11 |

Sprint 07 is above the velocity band on purpose: it contains only two stories and both are firmware-heavy (8 SP each). If refinement splits them (likely — e.g., US-24 into LAN discovery + LAN command + re-sync), the spill moves to Sprint 08, whose load is deliberately light.

## Milestones

| Milestone | After | Demonstrates | Release decision |
|-----------|-------|--------------|------------------|
| **M1 — Pilot control** | Sprint 02 | Manual on/off with true state, feedback and audit trail on real hardware in one room | Internal demo |
| **M2 — MVP** | Sprint 04 | Timetable-driven access + full control; a real class can run on it | **Pilot deployment** in 1–2 classrooms |
| **M3 — Feature complete** | Sprint 06 | All Must + Should stories done; scheduling, energy saving, maintenance flows | Building-wide pilot |
| **M4 — Campus release** | Sprint 08 | Epic G verified: offline resilience, 300 devices / 2,000 users, auto-recovery | **Production rollout** |

## Scope management rules

1. If velocity falls below plan, scope is cut in strict reverse-priority order: **US-11 (Could) first, then Should stories** (US-23, US-18, US-17, US-04, US-15, US-14 — in reverse backlog rank). Must and Epic G stories are never cut; they define the product and its release gate.
2. Stories are never moved *earlier* across a dependency listed in the backlog.
3. Any change to this plan is decided by the PO at sprint planning and recorded in that sprint's `planning.md`.

## Release gate (M4)

The campus release ships only when:

- All Must stories are Done per the [DoD](04-definition-of-ready-and-done.md).
- US-26 stress test passed: 300 simulated devices, 2,000-user load, NFR-01/NFR-02 thresholds held at peak.
- US-27 recovery drill passed: forced server kill, automatic restart, downtime ≤ 15 minutes.
- Offline drills passed: internet cut (US-24) and server cut (US-25) during a simulated class day.
