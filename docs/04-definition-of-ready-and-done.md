# 04 — Definition of Ready & Definition of Done

## 1. Definition of Ready (DoR)

A story may be pulled into a sprint only when **all** of the following hold. Checked during backlog refinement; the Scrum Master blocks planning of stories that fail it.

- [ ] Written in the standard template (*As a … I want … so that …*) with a `US-xx` ID and mapped source FR/NFR IDs (traceability to SRS §6.2).
- [ ] At least two Given–When–Then acceptance criteria: one success path, one exception path.
- [ ] Estimated by the whole Dev Team; estimate ≤ 8 SP (13+ must be split first).
- [ ] Dependencies in [02-product-backlog.md](02-product-backlog.md) are Done or scheduled earlier in the same sprint.
- [ ] Acceptance criteria describe user-observable behaviour, not a technical design (SRS Note 6.5-4).
- [ ] Configurable parameters the story touches (margin, X, N, minimum setpoint, operating hours) are identified and confirmed as admin-configurable — no hard-coded values.
- [ ] Test approach identified: which criteria are covered by unit/integration tests, which need the simulator, which need physical hardware (and hardware is bookable that sprint).
- [ ] UI stories: screen text defined in Vietnamese and externalised via resource files (NFR-07).

## 2. Definition of Done (DoD) — story level

A story is Done only when its own acceptance criteria **and every item below** are satisfied. This embeds NFR-01…08 from SRS §6.3 verbatim — they apply to *every* story.

### Functional completeness
- [ ] All Given–When–Then acceptance criteria pass, demonstrated over real transport (app → server → MQTT → device/simulator), not mocks.
- [ ] Exception paths behave as specified (rejections logged, state unchanged on rejected commands).

### Quality constraints (SRS §6.3)

| ID | Criterion | Verification |
|----|-----------|--------------|
| NFR-01 | Command latency tap → device state change ≤ 3 s (P95) | Timestamp log analysis over 100 sample commands |
| NFR-02 | API response ≤ 500 ms (P95) under 100 concurrent requests | k6/JMeter load test in CI or staging |
| NFR-03 | Passwords stored as BCrypt hashes, never plaintext | Code review + database inspection |
| NFR-04 | HTTPS/TLS everywhere; MQTT over TLS with per-device auth | Code review + packet capture |
| NFR-05 | Every command authorised on the server; client never trusted | Code review + integration test with a forged request |
| NFR-06 | Runs on Android 8.0+ and Windows 10+ | Tested on ≥ 3 physical devices |
| NFR-07 | UI in Vietnamese, language-extensible via resource files | Code review of resource-file structure |
| NFR-08 | Service-layer unit test coverage ≥ 60% | Coverage report from the CI pipeline |

NFR-01/02/06 apply when the story touches the command path, an API, or the UI respectively; NFR-03/04/05 apply to any story handling credentials, transport or commands. When in doubt, they apply.

### Engineering
- [ ] Code merged to `main` via a reviewed pull request (rules in [05-development-workflow.md](05-development-workflow.md)); CI green.
- [ ] New/changed behaviour covered by automated tests per [06-testing-strategy.md](06-testing-strategy.md); overall service-layer coverage did not drop below 60%.
- [ ] Audit logging present for any new command type (FR-F5 is cross-cutting).
- [ ] No new compiler warnings; no secrets, credentials or device keys committed.
- [ ] Documentation updated where behaviour changed (API contract, configuration parameters, operator notes).

### Acceptance
- [ ] Demoed at Sprint Review; PO explicitly accepted.
- [ ] Backlog status updated to `Done (Sprint NN)`.

## 3. Definition of Done — sprint level

- All stories accepted or explicitly returned to the backlog (never "90% done").
- The increment on `main` is deployable: staging deployment succeeds, smoke test passes.
- No known Must-severity defect is open against the increment.
- Sprint records (`planning.md`, `daily.md`, `review.md`, `retrospective.md`) committed under `docs/sprints/sprint-NN/`.

## 4. Definition of Done — release level (M4)

The release gate checklist in [03-release-plan.md](03-release-plan.md) §Release gate, in addition to every sprint-level DoD.
