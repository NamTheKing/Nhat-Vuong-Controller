# 05 — Development Workflow

Git, review and CI conventions for the Nhat Vuong Controller team. The goal: every change is traceable to a `US-xx` story, reviewed, and verified by CI before it reaches `main`.

## 1. Repository layout

As built, the code follows the architecture spine's `src/` layout rather than the original target below:

```
/src/Domain, /src/Application        Core: entities, policy, command pipeline, services, ports
/src/Adapters/{Rest,Mqtt,Persistence,Scheduler,Notification}
/src/Server                          Composition root (API + scheduler + embedded MQTT broker)
/src/Contracts                       Wire schema shared by server, app and simulator
/src/Client                          .NET MAUI app (Android 8.0+ / Windows 10+), .resx UI strings
/simulator                           Virtual device fleet
/tests                               Unit, integration, load
/deploy                              Container, systemd, drill scripts
/docs                                This documentation set + sprint records
/.github/workflows/ci.yml            CI gates (§5)
```

Original target:

```
/server        Backend: REST API, authorisation, scheduler, MQTT bridge
/app           Client app (Android 8.0+ / Windows 10+), UI resources in .resx-style files
/firmware      Device module: local control, cached schedule, LAN endpoint
/simulator     Virtual device fleet for development and load tests
/docs          This documentation set + sprint records
/.github       CI workflows (adjust path if another CI host is chosen)
```

## 2. Branching model

Trunk-based with short-lived branches:

- **`main`** — always releasable; protected. Direct pushes forbidden; changes arrive only via reviewed PRs with green CI.
- **Story branches** — `feature/US-07-lecturer-precedence`. One branch per story (or per task of a large story: `feature/US-24-lan-discovery`). Lifetime target: ≤ 3 days; rebase on `main` before opening the PR.
- **Bug branches** — `fix/BUG-123-offline-status-stuck`.
- **Sprint 0 / chores** — `chore/ci-pipeline`, `chore/mqtt-broker-tls`.

No long-lived `develop` branch: each sprint's increment is simply `main` at sprint end, tagged `sprint-NN` (milestones tagged `m1-pilot`, `m2-mvp`, `m3-feature-complete`, `m4-release`).

## 3. Commits

Conventional Commits with the story ID in the scope:

```
<type>(US-xx): <imperative summary ≤ 72 chars>
```

- **Types:** `feat`, `fix`, `test`, `refactor`, `docs`, `chore`, `perf`.
- Examples:
  - `feat(US-06): send power on/off command over MQTT`
  - `test(US-14): reject setpoint below configured minimum`
  - `fix(BUG-123): clear Offline badge after reconnect`
- Commit small and often; every commit compiles and passes unit tests.

## 4. Pull requests

**One PR per story branch.** The PR description must contain:

1. Story ID and link (`US-xx`), or bug ID.
2. What changed and why (2–5 sentences).
3. Which acceptance criteria this PR covers (list the Given–When–Then numbers).
4. Test evidence: which automated tests were added; simulator or hardware run notes for device-facing changes.

**Review rules**

- ≥ 1 approving review from someone who did not write the code; firmware and authorisation changes need a reviewer from that specialty.
- Reviewers check the [DoD](04-definition-of-ready-and-done.md) engineering items, with special attention to the security NFRs: server-side authorisation on every command path (NFR-05), no plaintext credentials (NFR-03), TLS on every transport (NFR-04), no hard-coded configuration parameters.
- Review turnaround target: same working day. Unblocking a review outranks starting new work (working agreement #3).
- The author merges after approval + green CI; squash-merge, keeping the `US-xx` scope in the squash title.

## 5. CI pipeline (gates on every PR)

| Stage | Gate |
|-------|------|
| Build | All projects compile with zero new warnings |
| Unit tests | 100% pass; service-layer coverage ≥ 60% (NFR-08) — the build **fails** below threshold |
| Integration tests | API + MQTT tests against the simulator, including the forged-request authorisation test (NFR-05) |
| Static checks | Linter/analyzer clean; secret scanner clean |
| Nightly (main) | k6/JMeter load profile (NFR-02); latency log analysis (NFR-01); simulator fleet smoke test |

A red `main` build is the whole team's top priority until green.

The gates are implemented in `.github/workflows/ci.yml`: the `server` job builds with warnings as errors, runs the
unit tests with the coverlet threshold on the service layer, then the integration tests against the simulator
(including forged-token and broker-ACL tests); the `client` job builds the Windows and Android targets; the
`secrets` job runs gitleaks. The nightly load profile is `tests/load/api-load.js` (not yet scheduled).

## 6. Issue tracker conventions

- Board columns: `Backlog → Ready → To Do (sprint) → In Progress → In Review → Done`.
- Every issue carries: story/bug ID, epic label (`epic-A`…`epic-G`), priority label (`must`/`should`/`could`/`nfr`), sprint milestone, SP estimate.
- Bugs are filed with [templates/bug-report.md](templates/bug-report.md); severity `blocker / major / minor`. Blockers found in-sprint are fixed in-sprint.

## 7. Configuration & secrets

- All admin-configurable parameters (control margin, auto-off X, long-run N, minimum setpoint, operating hours, disconnect alert threshold) live in server-side configuration with defaults from the SRS — never in client or firmware constants.
- Secrets (DB credentials, broker certs, per-device MQTT keys) come from environment/secret storage; `.gitignore` covers local secret files; the CI secret scanner is mandatory.
