# Feature Specification: Server Installer Reliability (Fresh Install + In-Place Upgrade)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-15
**Scope:** BE
**Feature:** Make the offline-LAN Windows server installer succeed on a fresh install and on an in-place reinstall without wiping the database.

## Overview
The offline-LAN Windows server installer (`packaging/server/clinic-server.iss`) has two reliability gaps. (1) On a truly fresh install, the API service's first boot can miss the ~30s Windows SCM start window while it generates the HTTPS cert on top of first-run JIT. (2) It regenerates the DB passwords on every run, so a reinstall over an existing PostgreSQL cluster fails authentication and forces a full data wipe. This feature moves cert generation to install time (off the SCM clock) and reuses existing DB credentials on reinstall, so both fresh install and in-place upgrade "just work". Builds on the already-committed deferred-migrations + ReadyToRun + `pg_isready`-retry fixes (`09390eb`).

## What Changes
- New idempotent one-shot API CLI branch `provision-cert` that generates (or reuses) the HTTPS CA + server cert into `.local/` and exits 0 — mirroring the existing `reset-admin-password` CLI pattern; makes no DB connection.
- The installer runs `{app}\api\ClinicManagement.API.exe provision-cert` during install, before the API service is started (before `sc start {#ServiceApi}` in `StartAndExportCa`, clinic-server.iss:347), so the service's first boot reuses the cert instead of generating it under the SCM timeout.
- Fix the CA-export source path: copy the CA from where the API actually writes it — `{app}\api\.local\ca.crt` — not the current `{app}\.local\ca.crt` (a path mismatch that left `%ProgramData%\ClinicManagement\ca.crt` empty).
- The DB passwords (`clinic_user` + `postgres` superuser) are persisted to a per-install gitignored file on first install and **reused** on later installs when an existing cluster is present, instead of regenerating every run (clinic-server.iss:372-373).

## Acceptance Criteria
- **AC-1:** `ClinicManagement.API.exe provision-cert` generates the CA + server cert into `.local/` (or reuses an existing loadable set), prints the CA path, makes no DB connection, returns exit 0, and is safe to run repeatedly (idempotent — never regenerates an existing set).
- **AC-2:** On a fresh install, the API service reaches **Running** and serves `https://<host>:5001` on first boot with **no manual `Start-Service`** (cert already provisioned at install time; combined with the committed deferred-migrations + R2R fixes).
- **AC-3:** After a fresh install, the CA is exported to `%ProgramData%\ClinicManagement\ca.crt`, copied from `{app}\api\.local\ca.crt`.
- **AC-4:** A reinstall over an existing install keeps the existing PostgreSQL cluster + data: the role/DB bootstrap and the written `appsettings.Production.json` use the **persisted** passwords, the install completes without the "Échec de la création du rôle/de la base" abort, and the API connects successfully afterward.
- **AC-5:** A fresh install (no persisted credentials file / fresh cluster) still generates new random passwords exactly as today.
- **AC-6:** Cloud is byte-for-byte unchanged; `dotnet build ClinicManagement.sln` is 0 errors; `packaging/README.md` documents the fresh-install and in-place-reinstall behavior.

## Out of Scope
- The already-committed fixes (deferred migrations, ReadyToRun, `pg_isready` retry loop, client-installer CA embed) — build on them, do not touch.
- Any application/runtime feature change or DB schema/migration change.
- A full web-host warm-up: `provision-cert` removes cert generation from the service-start path and warms the runtime + core assemblies, but does not pre-JIT the ASP.NET/Hangfire/YARP pipeline (the committed R2R + deferred-migrations changes cover the rest).
- Auto-rebuilding or redistributing the client installer with a regenerated CA (remains a documented operator step).

## Edge Cases (Critical only)
- **Reinstall where `pgdata` exists but the persisted credentials file is missing/unreadable:** fail loud with a clear operator message and abort — do NOT silently generate new passwords that won't match the existing cluster. Recovery (restore the file, or intentionally wipe `pgdata`) is documented.
- **`provision-cert` on an already-provisioned machine:** reuses the existing cert set (keeps the CA stable so clients that already trust it keep working); never regenerates.
