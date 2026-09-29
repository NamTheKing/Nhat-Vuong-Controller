---
review: rubric-walker
target: ../ARCHITECTURE-SPINE.md
altitude: feature
reviewed: '2026-09-11'
verdict: FAIL
---

# Rubric Walk — Architecture Spine, Nhat Vuong Controller

Gate review against the good-spine checklist. Target:
`docs/architecture/architecture-nhat-vuong-controller-2026-09-11/ARCHITECTURE-SPINE.md`
Driving spec: `docs/specs/spec-nhat-vuong-controller/SPEC.md` +
`quality-constraints.md`.

**Verdict: FAIL.** The spine is strong on the command/authority domain — AD-1, AD-5,
AD-7, AD-8, AD-11, AD-12 are genuine, enforceable, well-chosen invariants and I have no
quarrel with them. But it fails the gate on three counts: (a) AD-3/AD-14, the most
security-critical pair in the document, do not prevent the divergence they claim and are
arguably solving a problem the deployment topology has already solved; (b) the entire
operational and environmental envelope below "deployment is one host" is silent —
no backup, no observability contract, no health-check contract, no fleet update, no CI;
(c) several capabilities are mapped so shallowly (CAP-1, CAP-15, CAP-17, CAP-22) that the
epics under them have nothing to be coherent against.

Findings are numbered F-1…F-27 and tagged critical / high / medium / low.

---

## 1. Does it fix the real divergence points for the level below?

What it does fix, and fixes well — one line each, then I move on:

- AD-1 (single command pipeline) is the right spine decision and is the one that makes
  three driving adapters safe. Enforceable by an architecture test.
- AD-5 (replay as facts, never commands) is the correct and non-obvious call; it closes
  the reconnect double-execution hole cleanly.
- AD-7 (pipeline owns audit) and AD-8 (UTC to the edges) are exactly the kind of cheap,
  durable, cross-epic invariants this altitude exists to set.
- AD-11 (fan-out per unit) and AD-12 (only the server evaluates policy) are correct and
  directly traceable to CAP-6 and the spec's "never hard-coded" constraint.

### F-1 — critical — The identity and session dimension is entirely missing

CAP-1 is mapped to "`Adapters/Rest`, `Application`" governed by "NFR-03, NFR-05". No AD,
no convention row. Nothing in the document states:

- what a client presents to the API on a request (JWT? opaque session cookie? lifetime?
  refresh?),
- how an authenticated subject becomes the `subject` in `AccessGrant` (AD-2) — email?
  internal user id? — despite AD-2 naming `subject` as a column,
- how the MAUI client authenticates to the *device* on the AD-3 LAN path at all (see F-2),
- that passwords are BCrypt (NFR-03) — the quality constraint exists but the spine never
  carries it as a storage convention, so nothing binds the CAP-1 epic to it.

Every epic that touches a user touches this. Two epics building "sign in" and "who is the
actor in the audit entry" independently will diverge on the subject identifier alone,
which then corrupts AD-2 lookups and AD-7 audit rows.

### F-2 — critical — AD-3 does not prevent "the LAN control path becoming an authorisation bypass"

AD-3's Rule: *"A device accepts a LAN command only when presented with an unexpired,
validly signed grant."* That makes the grant a **bearer token**. Anyone on the campus
WiFi who observes one grant — and the Structural Seed draws the LAN hop as
`APP -.->|"LAN direct, signed grant (AD-3)"| MOD` with no TLS, unlike every other edge
which is labelled HTTPS or "MQTT over TLS" — can replay it and command the unit for the
rest of its validity window. The rule has:

- no proof-of-possession (the presenter never proves it is the `subject` the grant is
  bound to; binding the subject *into* the grant is inert if nothing checks the presenter),
- no per-command signature or nonce, so a captured command frame replays,
- no channel requirement, which also contradicts NFR-04 ("HTTPS/TLS for **all** traffic").

So the "Prevents" line is false as written. The fix is a per-command proof — the grant
carries a client public key and the app signs each command, or the grant establishes a
session key — but that is a decision the spine must make, not the firmware epic.

**Compounding this:** it is not clear AD-3's direct-LAN path is needed at all. The
Deployment paragraph puts the API, scheduler, broker and database on *one campus-hosted
server*, reached by clients "over campus WiFi". CAP-20's scenario is "the university loses
its internet connection but the local network is up" and "a command from an app on the
same LAN reaches and executes on the device **without the cloud**" — but there is no cloud
in this architecture. With the uplink down, app → API → broker → module still works
entirely on-campus, and the normal AD-1 pipeline already satisfies CAP-20 with full
server-side authorisation (NFR-05). Conversely, if the *server* is the thing that is down,
AD-3 does not help either: grants are issued and pushed "while online", so a client that
has not pre-fetched one, or whose grant has expired, is stuck. AD-3 therefore buys a
significant new attack surface for a scenario the topology already covers. Either justify
it against a failure mode the topology does not cover, or drop it and let CAP-20 be
satisfied by the on-campus server path.

### F-3 — critical — Nothing establishes a trusted clock on the device, yet four ADs depend on one

AD-3 ("unexpired"), AD-6 ("loss of connectivity exceeds the configured threshold"),
AD-5 ("an event log … with timestamps"), AD-8 ("store and transmit instants as UTC") and
CAP-21 ("performs its cached scheduled action at the scheduled time") all require the
ESP32 module to know what time it is, *while disconnected from the server*, for hours.
The spine never decides:

- time source (SNTP from the campus host? RTC? sync piggybacked on the MQTT session?),
- whether an RTC with battery backup is part of the module BOM — a hardware decision,
  therefore not deferrable to a software epic,
- acceptable drift bound, and what a device must do when its clock is untrusted (refuse
  grants? refuse cached schedules? both?).

A device whose clock is wrong or unset either enforces grant expiry incorrectly (security)
or fires cached shutdowns at the wrong time (CAP-21 correctness). This is the single
largest unwritten invariant in the document.

### F-4 — high — Device provisioning and per-device credentials are undecided, and AD-14 collides with NFR-04

NFR-04 requires "MQTT over TLS with **per-device authentication**". AD-14 says the device
"stores the server public key only" and that "symmetric HMAC is not used unless per-device
secret storage under flash encryption is separately justified." A per-device MQTT identity
*is* a per-device secret (client certificate + private key, or username/password). These
two statements cannot both hold. Unresolved, the firmware epic and the broker/ingest epic
will make opposite assumptions.

Adjacent and equally silent: how a factory-fresh module obtains its broker credential and
its copy of the server public key (CAP-15 registration by QR is mapped only to
"conventions (identifiers)"), and what CA the device trusts for the Mosquitto TLS
certificate on a campus host that may have no public DNS. That last one is a
firmware-baked decision.

### F-5 — high — AD-2's "and nothing else" leaves the whole role model and the precedence constraint undecided

AD-2: *"Authorising a command is a point-in-time lookup against that table and nothing
else."* But:

- An **administrator** creating a temporary grant (CAP-3), importing a timetable (CAP-16)
  or editing rooms (CAP-17) is not authorised by an `AccessGrant` row for a room.
- **Maintenance staff** resolving an incident (CAP-14) likewise.
- The spec's kernel Constraint "*A lecturer's command overrides a class monitor's when the
  two conflict, and the class monitor is notified*" (SPEC.md line 116, FR-A5) is a
  **precedence rule between two simultaneously valid grants**. It is not expressible as a
  point-in-time lookup, it appears nowhere in the spine, and the spec's own Open Question
  ("How does a class monitor obtain control rights at all?") is not carried forward either.

So either AD-2 is understated (there is a second authorisation surface for administrative
actions, unmodelled) or it is wrong. Either way the rights epic and the admin epic
diverge. Note also that the `USER` entity in the ER diagram carries no role at all.

### F-6 — high — Timetable re-import / grant re-materialisation semantics are undecided

The ER diagram asserts `TIMETABLE_ENTRY ||--o{ ACCESS_GRANT : materialises`, i.e. grants
are produced eagerly from timetable rows. Nothing says what a *second* import does:
full replace, merge, or append? CAP-16 says "a valid file imports every row" and a bad file
"writes nothing" — but says nothing about existing rows. This matters because:

- CAP-11 requires that "a cancelled schedule produces no command" — cancellation must
  therefore revoke already-materialised grants and already-scheduled pre-start actions;
- AD-3 pushes grants *to devices*, so a revoked grant may already be resident on a module,
  and AD-3 offers only expiry as a revocation mechanism ("which is also how CAP-2
  revocation is implemented"). There is no revoke-before-expiry path at all.

Import, scheduling and offline-grant epics each need the same answer and none is given.

### F-7 — high — No latency budget allocation, despite three different numeric limits

NFR-01 (≤3 s user action → device state change, P95), CAP-8 (result within 5 s or report
not responding) and CAP-7 (remote-originated change visible within 10 s) are three
distinct end-to-end budgets over a chain of app → API → core → broker → module → control
board → back. The spine never allocates them per hop, never states the device report
cadence (polling the control board? change-detect on the UART?) that CAP-7's 10 s depends
on, and never states the MQTT QoS level, which directly determines both latency and the
AD-9 duplicate behaviour. The client, server and firmware epics will each assume the
others absorbed the slack.

### F-8 — medium — MQTT topic namespace is not fixed

The "Message envelope" convention fixes the payload (`commandId`/`eventId`, `deviceId`,
`occurredAt`, `version`) — good, that is the right kind of row. But the topic structure is
the other half of the same contract and is absent. Command dispatch (`Adapters/Mqtt`),
device ingest, firmware and `simulator/` must all agree on it before any of them can be
built or tested. Either fix a topic shape or state explicitly that it is deferred to the
first epic that lands it.

### F-9 — medium — AD-6 has an unclosed race between the device's and the server's view of connectivity

AD-6: the device runs its cached schedule "only after loss of server connectivity exceeds
the configured threshold"; while connected "the server is the sole originator". Nothing
relates the **device's** disconnect threshold to the **server's** own liveness timeout.
If the server still believes a device is reachable while the device has already passed its
threshold, both fire the same shutdown — precisely the outcome AD-6's "Prevents" line
promises to stop. AD-9 does not save this: the device's autonomous action is not a
`Command` and carries no `commandId`, so the idempotency window never sees a collision.
The invariant needs a stated ordering (device threshold > server offline detection +
margin) or a server-side suppression rule.

### F-10 — medium — The unresolved semantic conflict between CAP-10 and CAP-2 is not carried forward

The spec raises it explicitly (SPEC.md line 145): operating hours end while a class is
still running — forced off, or does an active class suspend the envelope? This decides
scheduler behaviour and rights behaviour together, and it is exactly the sort of question
two epics will answer differently. The spine has no Open Questions section and does not
record it. See also F-22.

---

## 2. Is every AD's Rule enforceable, and does it prevent what it claims?

Enforceable and sound as written: AD-1, AD-5, AD-7, AD-8, AD-11, AD-12. AD-10 and AD-14
are enforceable (a hardware test; a code review for Ed25519/libsodium) though AD-14 has
the NFR-04 collision in F-4. The rest:

### F-11 — high — AD-4's "freshness bound" is undefined and unlocated

*"Consumers treat a projection older than the freshness bound as unknown, not as current."*
No value, and no statement of where the value lives. A reviewer cannot tell whether code
complies. The client epic will pick CAP-7's 10 s, the alerting epic will pick something
related to CAP-13's 30 minutes, and CAP-8's Online/Offline/Fault display will pick a third.
The bound is exactly the sort of thing AD-12 says belongs in server configuration and is
distributed as an absolute bound — say so.

### F-12 — medium — AD-9's "recent-id window" is unbounded and not persisted

*"Devices keep a recent-id window and silently acknowledge duplicates without
re-actuating."* No window size, no duration, and no statement of whether it survives a
module reboot. A reboot mid-retry re-actuates, which is the failure AD-9's "Prevents" line
targets. On a memory-constrained ESP32 this is a real design constraint, not a detail.

### F-13 — low — AD-3's "short-lived" and "configured margin" have no bound

The rule's only quantitative anchor is "never exceeds the class window plus configured
margin". For a 4-hour lab block that is a 4-hour bearer credential (F-2). A maximum grant
lifetime independent of class length is the missing half.

### F-14 — low — AD-13's second clause is not an architectural rule

*"…adopted from existing open-source implementations rather than written from scratch."*
That is sourcing guidance, not an invariant: no reviewer can fail a compliant
`ClimateDevice` adapter for having been hand-written, and it does not contribute to the
stated "Prevents" (per-brand detail leaking out), which the first clause already achieves
alone. It also silently imports a licence question (most open AC protocol libraries are
GPL-family) that the spine does not address. Move to Deferred or to a note.

### F-15 — low — AD-10 is an `[ASSUMPTION]` doing load-bearing work

It binds "all device capabilities" and answers a spec Open Question (SPEC.md line 148) by
assertion. The rule itself is good and testable. But an assumption that determines module
BOM and bus wiring should be flagged as requiring confirmation before firmware work
starts, not left as a tagged AD with no escalation path (the spine has nowhere to put one
— see F-22).

---

## 3. Does anything under "Deferred" let two units below diverge?

Genuinely fine to defer, one line: brand ordering; multi-campus tenancy; the
MAUI-vs-Avalonia revisit (the stack does pin MAUI 10, so the level below is not left
divergent — it is an accepted risk, not an open fork).

### F-16 — high — Deferring CAP-23 supervision also deferred things that *do* constrain the units below

*"Health-check-and-restart is a deployment concern … it does not constrain how the units
above are built."* Three things it does constrain:

1. **The health-check contract itself.** Any supervisor needs an endpoint or probe with
   defined semantics (does it report broker connectivity? DB connectivity? scheduler
   liveness?). That contract is the server epic's, not the deployment's.
2. **Restart-safety.** A 15-minute downtime budget with automatic restart only works if
   in-flight commands and the scheduler are restart-safe. The Stack section chooses
   `BackgroundService` + `PeriodicTimer` with "no Quartz.NET or Hangfire" — i.e. **no
   persistent job store**. So what happens to a CAP-9 shutdown or a CAP-11 pre-start that
   fell inside the outage window: fire late, or skip? That is a scheduler-epic invariant
   and it interacts with AD-6 (the device may have fired it already).
3. **Command-in-flight semantics** across a restart: a command audited (AD-7) but never
   dispatched must resolve to something.

### F-17 — medium — Deferring the "reporting store strategy" also deferred the producer contract

Retention and whether monthly aggregates are materialised are legitimately volume
questions. But who **writes** `RUNTIME_SAMPLE`, at what cadence, and from what signal is
not: the firmware/ingest epic and the reporting epic must agree on whether runtime is
device-reported samples, derived from state transitions, or computed from command history.
CAP-19's map row cites only AD-8 (time), which does not cover this.

### F-18 — low — Deferring the notification transport is fine; deferring the event set is not

Hiding push-vs-email behind `NotificationPort` is correct and I have no objection. But the
*set* of notification-raising events and the role→recipient resolution is a cross-epic
contract: CAP-12 (maintenance staff), CAP-13 (alerts) and the spec Constraint "the class
monitor is notified" on lecturer override (F-5) all feed the same port from different
epics. One sentence naming the port's contract would close it.

---

## 4. Capability coverage — CAP-1 … CAP-23

Mapped with real depth: CAP-2/3, CAP-4/5, CAP-6, CAP-7, CAP-8, CAP-9/10/11, CAP-16
(partially — see F-6), CAP-18, CAP-20, CAP-21. Shallow or absent:

### F-19 — high — CAP-15, CAP-17 and CAP-22 are mapped to nothing that governs them

| Row | Governed by | Problem |
| --- | --- | --- |
| CAP-15 registration | "conventions (identifiers)" | Registration is device *provisioning* — credential issuance, trust-store seeding, duplicate-code rejection, room binding. A naming convention governs none of it. See F-4. |
| CAP-17 reference data | "conventions (naming)" | The actual constraint in CAP-17 is referential ("deleting a room that still has devices is blocked") plus propagation ("saved changes take effect system-wide" — cache and device invalidation). A naming convention governs neither. |
| CAP-22 scale | "NFR-01, NFR-02" | Citing the numbers is not governing them. 300 devices and 2,000 concurrent users on **one host running API + scheduler + broker + database** (Deployment paragraph) is a capacity claim with no supporting invariant — notably, nothing states that the scheduler is a singleton (or how leadership is decided if it ever is not), which is the first thing that breaks if the API is ever scaled out. |

CAP-1's row is the fourth of these; it is covered as F-1.

### F-20 — medium — CAP-12/13/14 are collapsed into one row with no incident invariant

Governed by "AD-4, AD-12". Neither covers:

- **where liveness alerts are evaluated** (CAP-13's "disconnected more than 30 minutes
  during working hours" and "runs continuously beyond N hours" need a periodic evaluator —
  the scheduler? a separate monitor? AD-4 gives the raw material, not the owner);
- **incident lifecycle and its audit** (CAP-14 must later show "who resolved it, when, and
  the note" — AD-7 governs *command* audit only, so incident resolution has no audit rule);
- **de-duplication** of repeated identical error codes from one device into one incident,
  which at 300 modules is the difference between an alert and a flood.

### F-21 — medium — NFR-06/07/08 have no home in the spine

- NFR-07 (Vietnamese UI, extensible via resource files) appears only as a comment in the
  directory tree (`Client/  # MAUI app; Vietnamese resource files`). The durable invariant
  — no user-facing string literal in code, all through resources — is a one-row convention
  and is missing.
- NFR-08 (service-layer coverage ≥ 60% "from the CI pipeline") presumes a CI pipeline the
  spine never mentions. There is a `tests/` directory and a `simulator/` directory and no
  statement of what either is contractually for — in particular, what makes the simulator
  a valid stand-in for a real module (same envelope, same topics, same state machine).
- NFR-03 (BCrypt) is cited in the capability map but never asserted as a convention.

---

## 5. Is every dimension this altitude owns decided, deferred, or an open question?

The domain dimensions are well covered. The operational/environmental envelope is where
this fails, and it fails in the routine way.

Credit where due: the Deployment paragraph does exist, does name a topology, and does name
three environments (`dev` simulator-only, `staging` simulator fleet at CAP-22 scale, `prod`
one campus host) — that is more than most domain-focused drafts manage, and the
staging-at-CAP-22-scale call is a good one. What is missing sits underneath it: there is no
statement of *how* the prod host is provisioned or how the API is packaged and run
(bare-metal service, container, VM), which is the thing the deferred CAP-23 supervisor
choice depends on.

### F-22 — high — No Open Questions section at all

The spec carries six open questions; at least four are still live and architecture-shaping:
CAP-10 vs CAP-2 at closing hour (F-10), how a class monitor obtains rights (F-5), the
timetable file schema (F-6, and CAP-16 cannot be built without it), and "Sections 1–5 of
the SRS are cited throughout the source but are not in this repository; they may carry
scope or architecture decisions this contract should inherit." A spine that silently drops
its driving spec's open questions leaves each epic to answer them locally — which is the
definition of divergence. AD-10's `[ASSUMPTION]` tag (F-15) and the two `[ASSUMPTION]` tags
in the Stack table also have nowhere to escalate to.

### F-23 — high — Backup, restore and data retention are entirely absent

Single host, single Postgres instance. It holds the audit log that CAP-18 exists to make
available "when a dispute or incident arises", the timetable-derived grants that all
control rights depend on, and the runtime history behind CAP-19. There is no RPO, no RTO,
no backup target, no restore procedure, and no retention period for `AUDIT_ENTRY` or
`RUNTIME_SAMPLE`. There is also no **append-only / immutability** rule on the audit table,
without which the audit log is not evidence — a gap that partially undercuts AD-7's whole
purpose.

### F-24 — high — Observability is one line and does not reach the things that need it

The only row is "Logging: structured; every log line on a command path carries
`commandId`" — which is a genuinely good invariant, and correlates with AD-9. But there is
nothing on: metrics (how does anyone verify NFR-01 P95 or CAP-22 in `prod`, when the
stated verification method is "timestamp log analysis over 100 sample commands"?), a
health/readiness endpoint (which deferred CAP-23 *requires* — F-16), log aggregation and
retention on a single host, alerting on the alerting subsystem itself, or fleet-level
device connectivity visibility across 300 modules.

### F-25 — medium — No fleet/firmware update strategy

300 ESP32 modules, physically installed in classrooms, that hold a pinned server public key
(AD-14), a cached schedule (CAP-21), brand adapters (AD-13) and an envelope `version`
field. Nothing states whether firmware updates are OTA or manual, how a signed update is
authenticated, or — given the `version` field exists — what a device or server does when it
receives an envelope version it does not understand. The `version` field without a
compatibility rule is decoration.

### F-26 — medium — Secrets and TLS material get one clause

"Configuration: … secrets from environment, never in source or firmware images." Good as
far as it goes, and the firmware clause is the right instinct. Not covered: who supplies
the environment on the prod host, how the **server signing private key** for AD-3/AD-14 is
stored and — critically — **rotated**, given 300 devices each hold a pinned copy of its
public counterpart with no stated update path (F-25). Nor how TLS certificates are issued
and renewed for HTTPS and for the broker on a campus host that may have no public DNS
(F-4).

Also silent, lower weight: EF Core migration ownership and how schema changes are applied
to the prod host; REST API versioning (the MQTT envelope has a `version` field, the REST
contract has none) and pagination for the list endpoints CAP-17 and CAP-19 imply.

---

## 6. Is the seed minimal?

Mostly yes — the directory tree is at the right altitude, the ER diagram is restrained and
its "Attributes appear here only where they are not themselves invariants" note is exactly
the right instinct. Two small overreaches and one under-reach:

### F-27 — low — Patch-level version pins are lockfile content, not spine content

The Stack section's own preamble says "The code owns this once it exists", then pins
`EF Core 10.0.0`, `MQTTnet 5.2.0.1603`, `PostgreSQL 18.6`, `Mosquitto 2.1.2`,
`libsodium 1.0.21`. The durable invariants here are the major/LTS lines and the
.NET 10 LTS-to-2028 reasoning (which is well argued and worth keeping); the fourth-segment
build number is not. The same row set stated at major version would be strictly better.

Also low, same category: the "Connection handling" convention carries library trivia
("MQTTnet 5 removed `ManagedClient`") into a durable document. The invariant is "reconnect
and backoff exist in exactly one place"; the reason MQTTnet 5 forces the issue is a code
comment.

Under-reach, noted rather than numbered: the Stack's "no Quartz.NET or Hangfire" decision
is correct for this scale but its consequence — no durable job store, therefore missed
schedules do not survive a restart — is left unstated, which is what makes F-16 possible.

Minor ER observation, not a finding: `DEVICE ||--|| DEVICE_STATE` as one-to-one is
consistent with AD-4's "projection" framing and `RUNTIME_SAMPLE` carries the history, so
that is fine. `COMMAND ||--|| AUDIT_ENTRY` is fine for CAP-18's device-scoped wording, but
note it leaves administrative actions (grant creation, timetable import, device
registration, incident resolution) with no audit representation — see F-20.

---

## 7. Missing invariants — consolidated

A future builder could not read these off compliant code. In rough priority:

1. Trusted device clock: source, drift bound, behaviour when untrusted (F-3).
2. LAN command authentication beyond a bearer grant; transport security on that hop (F-2).
3. Device identity, provisioning, broker credential and trust store (F-4).
4. Client↔API authentication scheme and the canonical subject identifier (F-1).
5. Role model for non-room-scoped actions; lecturer-over-class-monitor precedence (F-5).
6. Timetable re-import semantics and grant revocation before expiry (F-6).
7. Latency budget per hop; device report cadence; MQTT QoS (F-7).
8. Scheduler singleton-ness and missed-schedule catch-up policy across restart (F-16).
9. Health-check contract (F-16, F-24).
10. Audit immutability and retention; backup RPO/RTO (F-23).
11. Freshness bound value and location (F-11).
12. `RUNTIME_SAMPLE` producer contract (F-17).
13. MQTT topic namespace (F-8).
14. Envelope version-compatibility rule; REST API versioning (F-25, F-26).
15. Signing-key rotation path (F-26).
16. Simulator conformance: what makes the simulator a valid stand-in for a module (F-21).
17. No-hardcoded-user-strings convention (NFR-07) and BCrypt storage (NFR-03) (F-21).

---

## Severity roll-up

| Severity | Count | IDs |
| --- | --- | --- |
| Critical | 3 | F-1, F-2, F-3 |
| High | 10 | F-4, F-5, F-6, F-7, F-11, F-16, F-19, F-22, F-23, F-24 |
| Medium | 9 | F-8, F-9, F-10, F-12, F-17, F-20, F-21, F-25, F-26 |
| Low | 5 | F-13, F-14, F-15, F-18, F-27 |

Total findings: 27.

**Gate decision: FAIL.** Three criticals, and two whole dimensions (identity/session;
operations — backup, observability, health, fleet update) with no decision, no deferral and
no open question recorded. The domain half of this spine is good enough that the fixes are
additive rather than structural: resolve F-1…F-3, give F-4…F-7 an AD each, add an Open
Questions section carrying the spec's live ones, and add an operational envelope section
covering health, backup, retention, secrets and firmware update.
