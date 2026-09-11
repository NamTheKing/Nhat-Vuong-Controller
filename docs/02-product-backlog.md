# 02 — Product Backlog

Ordered backlog for the Nhat Vuong Controller. Story definitions and acceptance criteria live in [project.md](project.md) (SRS §6.4); this document adds ordering, initial estimates, dependencies and status. The PO owns the order; the Dev Team owns the estimates.

- **Estimates** are initial planning-poker values (Fibonacci); the team re-confirms them at refinement before a story enters a sprint.
- **Depends on** lists hard technical/functional dependencies — a story is not Ready while a dependency is unfinished.
- **Status:** `Backlog` → `Ready` → `In Sprint NN` → `Done (Sprint NN)`.

## Backlog (ordered)

| Rank | ID | Story (short) | Epic | Priority | SP | Depends on | Planned sprint | Status |
|-----:|----|---------------|------|----------|---:|------------|:--------------:|--------|
| 1 | US-01 | Sign in with a university account | A | Must | 3 | — | 01 | Backlog |
| 2 | US-21 | Manage rooms, buildings and users | F | Must | 5 | US-01 | 01 | Backlog |
| 3 | US-19 | Register a device by QR code | F | Must | 3 | US-21 | 01 | Backlog |
| 4 | US-06 | Switch the AC on and off | B | Must | 5 | US-19 | 02 | Backlog |
| 5 | US-08 | Show true state from the control board | C | Must | 3 | US-06 | 02 | Backlog |
| 6 | US-10 | Command feedback + Online/Offline/Fault | B/C | Must | 3 | US-06 | 02 | Backlog |
| 7 | US-22 | Audit-log every control command | F | Must | 3 | US-06 | 02 | Backlog |
| 8 | US-20 | Import timetable from CSV/Excel | F | Must | 5 | US-21 | 03 | Backlog |
| 9 | US-02 | Derive control rights from the timetable | A | Must | 5 | US-20 | 03 | Backlog |
| 10 | US-03 | Control window around the class period | A | Must | 2 | US-02 | 03 | Backlog |
| 11 | US-05 | Revoke access automatically on expiry | A | Must | 2 | US-02 | 03 | Backlog |
| 12 | US-07 | Temperature / mode / fan + lecturer precedence | B | Must | 5 | US-06, US-02 | 04 | Backlog |
| 13 | US-09 | Stay in sync with the physical remote | C | Must | 5 | US-08 | 04 | Backlog |
| 14 | US-14 | Block setpoints below minimum threshold | B | Should | 2 | US-07 | 04 | Backlog |
| 15 | US-13 | Campus-wide operating hours | D | Must | 3 | US-06 | 05 | Backlog |
| 16 | US-12 | Auto-off after class + long-run alert | D | Must | 5 | US-02, US-13 | 05 | Backlog |
| 17 | US-15 | Pre-cool before a class | D | Should | 3 | US-12 | 05 | Backlog |
| 18 | US-04 | Grant temporary access | A | Should | 3 | US-02 | 05 | Backlog |
| 19 | US-16 | Capture, store and announce error codes | E | Must | 5 | US-10 | 06 | Backlog |
| 20 | US-17 | Close an incident with a note | E | Should | 2 | US-16 | 06 | Backlog |
| 21 | US-18 | Alert on prolonged disconnection | E | Should | 3 | US-10 | 06 | Backlog |
| 22 | US-23 | Monthly runtime reporting | F | Should | 3 | US-22 | 06 | Backlog |
| 23 | US-24 | Direct LAN control without the cloud | G | NFR | 8 | US-06 | 07 | Backlog |
| 24 | US-25 | Device follows cached schedule offline | G | NFR | 8 | US-12, US-13 | 07 | Backlog |
| 25 | US-26 | Operate at full campus scale (300 dev / 2,000 users) | G | NFR | 5 | all Must | 08 | Backlog |
| 26 | US-27 | Automatic server recovery ≤ 15 min | G | NFR | 3 | — | 08 | Backlog |
| 27 | US-11 | Control a whole room in one action | B | Could | 3 | US-06 | 08 | Backlog |

**Total: 105 SP** — 16 Must (62 SP), 6 Should (16 SP), 1 Could (3 SP), 4 NFR stories (24 SP).

## Ordering rationale

1. **Foundation before features (Sprints 01–02):** authentication (US-01), reference data (US-21) and device registration (US-19) are prerequisites for any command path. The first end-to-end command (US-06) plus its feedback (US-08, US-10) and the audit log (US-22) establish the app → server → MQTT → device pipeline that everything else reuses.
2. **The timetable is the heart of the permission model (Sprint 03):** US-20 must precede US-02/03/05 because rights are derived from imported timetable data (SRS Note 6.5-3).
3. **Should stories ride with their Must neighbours** (e.g., US-14 with US-07; US-15 with US-12) because they touch the same code and are cheap once the neighbour exists — this respects working agreement #1 since the blocking Must stories are already scheduled first.
4. **Epic G lands late deliberately:** LAN control (US-24) and cached schedules (US-25) are firmware-heavy and only meaningful once the online behaviour they must replicate is stable. Scale testing (US-26) needs a feature-complete system to be a valid test.
5. **US-11 (Could)** is last; it is dropped first if the release date is at risk.

## Backlog hygiene

- New stories: create from [templates/user-story.md](templates/user-story.md), assign the next free `US` number, map source FRs, and insert at the PO's chosen rank.
- A story ≥ 13 SP must be split before entering a sprint.
- Update the **Status** column at sprint planning and sprint review — this table is the authoritative snapshot; the issue tracker holds the live task detail.
