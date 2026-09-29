<!-- bmad:context -->
<!-- Verified 2026-09-11 against f63c6c5. Managed by bmad-project-context; edits inside this block are replaced on refresh. Keep anything you want preserved outside the markers. -->

## sad — Nhat Vuong Controller

University System Analysis & Design coursework. Specifies a campus air-conditioner
control system: client app, server, and MQTT device modules on classroom AC units.
Stack: .NET 10 — ASP.NET Core server with embedded MQTTnet broker (`src/Server`,
`src/Application`, `src/Adapters/*`), .NET MAUI client for Android 8.0+ and Windows 10+
(`src/Client`), device simulator (`simulator/`). Planning and requirements live in `docs/`,
indexed by `docs/README.md`; the code map is in the root `README.md`.

## Policy

- Extend documents under `docs/` in place; never rewrite or replace one wholesale — the
  existing set is the project deliverable, not scratch material.

## Where things are

- Requirements authority: `docs/project.md` — SRS §6, 27 user stories, 30 FR, 12 NFR,
  plus the traceability matrix every story ID resolves against.
- Documentation index: `docs/README.md` — read it before adding a document, so new work
  lands in the right file instead of a new one.
- SRS sections 1–5 are cited throughout `docs/project.md` but are not in this repo; ask
  rather than inferring what they contain.
- Ceremony templates in `docs/templates/`; sprint records land in `docs/sprints/sprint-NN/`.

## Running and verifying

- Server: `dotnet run --project src/Server --launch-profile https` (dev seed, SQLite,
  broker on 1883); simulator: `dotnet run --project simulator`.
- Tests: `dotnet test tests/Application.Tests` and `dotnet test tests/Integration.Tests`;
  coverage gate: add `-p:CollectCoverage=true "-p:Include=[NhatVuong.Application]*" -p:Threshold=60`
  (in Git Bash use `-p:`, not `/p:`).
- Client: `dotnet build src/Client -f net10.0-windows10.0.19041.0` (or `-f net10.0-android`);
  needs the MAUI workload. Server-side projects treat warnings as errors.
- Run BMAD helper scripts from `.claude/skills/bmad/scripts/`, not `_bmad/scripts/`.

## Conventions that differ from defaults

- Never renumber `US-xx`, `FR-xx`, `NFR-xx` or the epic letters A–G; give a new story the
  next free `US` number — renumbering breaks the traceability matrix in `docs/project.md`.
- Write documentation and commit messages in English; product UI strings are Vietnamese
  per NFR-07.
- Exclude `.claude/skills/` from repository-wide searches — it is vendored BMAD tooling
  and 94% of tracked files.

## Known pitfalls

- BMAD skills are installed but `_bmad/` was never created, so every skill's
  `uv run {project-root}/_bmad/scripts/...` activation step fails; read the skill's own
  `customize.toml` instead, or run BMAD setup to fix it permanently.

<!-- /bmad:context -->
