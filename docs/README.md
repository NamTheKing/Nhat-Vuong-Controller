# Nhat Vuong Controller — Development Documentation

Documentation set for the **Nhat Vuong Controller** project: a campus-wide smart air-conditioner control system for a university (mobile/desktop app, cloud server, MQTT-connected device modules). Development follows **Agile Scrum**.

## Document map

| # | Document | Purpose | Scrum artifact / practice |
|---|----------|---------|---------------------------|
| — | [project.md](project.md) | SRS Section 6 — 27 user stories, traceability matrix, DoD source | Requirements input |
| 01 | [01-scrum-process.md](01-scrum-process.md) | Roles, events, cadence, working agreements | Scrum framework |
| 02 | [02-product-backlog.md](02-product-backlog.md) | Ordered backlog with estimates and dependencies | Product Backlog |
| 03 | [03-release-plan.md](03-release-plan.md) | Sprint map, milestones, velocity assumptions | Release planning |
| 04 | [04-definition-of-ready-and-done.md](04-definition-of-ready-and-done.md) | DoR and DoD checklists (embeds NFR-01…08) | DoR / DoD |
| 05 | [05-development-workflow.md](05-development-workflow.md) | Git branching, commits, pull requests, CI gates | Engineering practice |
| 06 | [06-testing-strategy.md](06-testing-strategy.md) | Test levels, NFR verification, device simulation | Quality practice |

## Specification and architecture

- [Canonical specification](specs/spec-nhat-vuong-controller/SPEC.md), [quality constraints](specs/spec-nhat-vuong-controller/quality-constraints.md), and [traceability](specs/spec-nhat-vuong-controller/traceability.md) define the system contract.
- [Architecture spine](architecture/architecture-nhat-vuong-controller-2026-09-11/ARCHITECTURE-SPINE.md) is a draft; its [review](architecture/architecture-nhat-vuong-controller-2026-09-11/reviews/review-rubric-walker.md) records unresolved findings.
- [BMAD build plan](specs/spec-nhat-vuong-controller/build.md) records the implementation sequence and outstanding decisions for building the complete specification.

## Implementation

The code lives beside these documents: see the repository [README](../README.md) for layout, run instructions,
demo accounts and the sprint → story → code → test map.

## Templates (`templates/`)

| Template | Used at |
|----------|--------|
| [user-story.md](templates/user-story.md) | Backlog refinement — every new story |
| [sprint-planning.md](templates/sprint-planning.md) | First day of each sprint |
| [daily-scrum.md](templates/daily-scrum.md) | Daily, 15 minutes |
| [sprint-review.md](templates/sprint-review.md) | Last day of each sprint |
| [sprint-retrospective.md](templates/sprint-retrospective.md) | Last day of each sprint, after the review |
| [bug-report.md](templates/bug-report.md) | Whenever a defect is found |

## Conventions

- Story IDs (`US-xx`), requirement IDs (`FR-xx`, `NFR-xx`) and epic letters (A–G) are those defined in [project.md](project.md). Never renumber them; new stories take the next free `US` number.
- Sprint records live in `docs/sprints/sprint-NN/` (created from the templates at each ceremony).
- All documents are written in English; the product UI is in Vietnamese per NFR-07.
