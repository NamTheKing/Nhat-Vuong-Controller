# 01 — Scrum Process Guide

How the Nhat Vuong Controller team applies Scrum. This is the team's single source of truth for roles, events, and working agreements.

## 1. Roles

| Role | Responsibility on this project |
|------|-------------------------------|
| **Product Owner (PO)** | Owns [02-product-backlog.md](02-product-backlog.md): ordering, priorities (MoSCoW from the SRS), acceptance of stories against their Given–When–Then criteria. Represents the university stakeholders (academic affairs, facilities/maintenance, IT). |
| **Scrum Master (SM)** | Facilitates all events, removes impediments (e.g., blocked hardware access, broker downtime), guards the process and the timeboxes. |
| **Development Team** | Cross-functional: backend (server + MQTT), client app (Android/Windows), firmware (device module), QA. Owns estimates and the Sprint Backlog. |

One person may hold at most one Scrum role plus a development specialty. The PO does not assign tasks; the team pulls work.

## 2. Cadence

- **Sprint length:** 2 weeks, fixed. Sprints are numbered `Sprint 01`, `Sprint 02`, …
- **Sprint 0** (before Sprint 01) is a one-off setup iteration: repository, CI pipeline, architecture skeleton, MQTT broker, device simulator. No user stories are burned in Sprint 0.
- A sprint is never extended. Unfinished stories return to the Product Backlog and are re-planned.

## 3. Events

| Event | Timebox | When | Output (recorded in `docs/sprints/sprint-NN/`) |
|-------|---------|------|------------------------------------------------|
| Sprint Planning | 2 h | Day 1 | `planning.md` — Sprint Goal, selected stories, task breakdown |
| Daily Scrum | 15 min | Every working day | `daily.md` — one appended section per day |
| Backlog Refinement | 1 h | Mid-sprint (day 5–6) | Updated backlog: estimates, split stories, DoR check |
| Sprint Review | 1 h | Last day | `review.md` — demo notes, PO accept/reject per story |
| Sprint Retrospective | 45 min | Last day, after review | `retrospective.md` — max 3 improvement actions |

Templates for each record are in [templates/](templates/).

### Sprint Goal rules

Every sprint has a one-sentence goal expressed in user value (e.g., *"A lecturer can switch on the AC of the room they are teaching in"*), not a list of stories. Stories are selected because they serve the goal.

### Demo rules for this project

A story is demoed against **real transport** (app → server → MQTT → device module or simulator), never with mocked device responses. State-sync stories (Epic C) are demoed with the physical remote in hand.

## 4. Artifacts

| Artifact | Location | Owner |
|----------|----------|-------|
| Product Backlog | [02-product-backlog.md](02-product-backlog.md) + issue tracker | PO |
| Sprint Backlog | Issue tracker board (`To Do / In Progress / In Review / Done`) | Dev Team |
| Increment | `main` branch, deployable at every sprint end | Dev Team |
| Definition of Done | [04-definition-of-ready-and-done.md](04-definition-of-ready-and-done.md) | Whole team |

## 5. Estimation

- Unit: **story points**, Fibonacci scale (1, 2, 3, 5, 8, 13).
- Method: planning poker during refinement; the whole Dev Team estimates, the PO clarifies but does not vote.
- A story estimated at **13 or more must be split** before it can enter a sprint.
- Reference story: **US-01 (sign in) = 3 points**. Calibrate all other estimates against it.
- Velocity is measured from completed sprints; the initial planning assumption is in [03-release-plan.md](03-release-plan.md).

## 6. Working agreements

1. **Priorities are honoured:** no Should story enters a sprint while a Must story that is Ready and unblocked remains in the backlog, unless the PO explicitly decides otherwise (recorded in `planning.md`).
2. **Traceability is preserved:** every story, branch, commit, and PR carries its `US-xx` ID (see [05-development-workflow.md](05-development-workflow.md)).
3. **Blocked > new:** team members help unblock or review before starting new work; WIP limit of 1 story in progress per person.
4. **Hardware is shared:** physical device modules and AC test units are booked in the daily scrum; the simulator is the default target for development.
5. **Definition of Done is non-negotiable:** a story that misses any DoD item (including NFR-01…08) is *not done* and is not demoed as done.
6. **Configuration parameters** (margin minutes, threshold X, runtime N, minimum setpoint) are never hard-coded; they are administrator-configurable per SRS Note 6.5-2.
7. **Retrospective actions** (max 3) are added to the next sprint's backlog as tasks and reviewed at the following retrospective.
