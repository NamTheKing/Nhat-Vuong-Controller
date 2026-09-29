# Quality Constraints

Companion to `SPEC.md`. These eight constraints apply to **every** capability in the
kernel, not to a subset. A capability that satisfies its own success criterion but
violates one of these is not complete.

Source: SRS §6.3, where they are stated as the project's Definition of Done.

| ID | Category | Constraint | How it is verified |
|----|----------|-----------|--------------------|
| NFR-01 | Performance | Command latency from user action to device state change ≤ 3 seconds (P95) | Timestamp log analysis over 100 sample commands |
| NFR-02 | Performance | API response time ≤ 500 ms (P95) under 100 concurrent requests | Load test (k6 or JMeter) |
| NFR-03 | Security | Passwords stored as BCrypt hashes, never plaintext | Code review and database inspection |
| NFR-04 | Security | HTTPS/TLS for all traffic; MQTT over TLS with per-device authentication | Code review and packet capture |
| NFR-05 | Security | Every command authorised on the server; the client is never trusted | Code review and integration test with a forged request |
| NFR-06 | Compatibility | Runs on Android 8.0+ and Windows 10+ | Testing on at least three physical devices |
| NFR-07 | Usability | UI in Vietnamese, extensible to other languages via resource files | Code review of the resource-file structure |
| NFR-08 | Code quality | Unit test coverage of the service layer ≥ 60% | Coverage report from the CI pipeline |

## Notes for downstream consumers

- **NFR-05 is also a kernel constraint** because it bends architecture, not just quality:
  no capability may authorise a command client-side, which rules out any design where the
  app decides whether the user holds rights.
- **NFR-01 and NFR-02 are re-asserted as a success criterion of CAP-22** — they must hold
  at full campus load, not only when the system is idle.
- **NFR-06 fixes the client platform envelope** (Android 8.0+, Windows 10+) and therefore
  constrains the technology choice for every user-facing capability.
- **NFR-09…12 are not in this table.** They carry independent development effort and are
  capabilities in their own right: CAP-22 (scale), CAP-21 (cached schedule), CAP-20 (LAN
  control), CAP-23 (automatic recovery).
