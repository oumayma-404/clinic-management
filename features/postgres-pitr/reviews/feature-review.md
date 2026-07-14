# Feature Review: postgres-pitr

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-07-13
**Parent Branch:** feature/windows-desktop-app (feature work is untracked on this branch — see progress.md)
**Merge Base:** n/a (all PITR files are untracked working-tree additions; reviewed against the working tree, not a git diff)
**Files Reviewed:** 6 PITR-scoped files — `deploy/postgres/Dockerfile` (new), `deploy/postgres/pitr-backup.sh` (new), `deploy/postgres/pitr-entrypoint.sh` (new), `deploy/docker-compose.prod.yml` (`postgres` + new `pitr` service), `deploy/.env.example` (WALG_*/PITR_* keys), `DEPLOY.md` (PITR section). Out of scope (prior cloud-deployment feature, untracked): `Caddyfile`, `backup/`, `rclone/`.

**Review method:** Infra/config-only diff (Docker, shell, Postgres config, env, docs) — no C#/TS surface. The default four C#/ROP agents do not apply; reviewed inline against adapted mandates: (1) config/compose correctness, (2) shell-script correctness, (3) PITR business-logic/AC coherence, (4) breaking-change/regression risk to the existing backup path, plus secret hygiene. Diff is small (6 files, single intent) → inline review per skill guidance.

**Verified clean (no findings):** WAL-G env var names (`WALG_S3_PREFIX`, `AWS_ENDPOINT`, `AWS_REGION`, `AWS_ACCESS_KEY_ID`/`SECRET`, `AWS_S3_FORCE_PATH_STYLE`) match WAL-G's contract; `archive_command=wal-g wal-push %p` has no `|| true`/exit-masking (AC-6 holds — Postgres logs + retries + retains WAL on failure); `%p` is not subject to compose `$`-interpolation; `archive_command` passed as a single compose list item preserves its spaces; secret hygiene (`deploy/.env` gitignored, no tracked `.env`, `.env.example` placeholders only — AC-3); `WALG_S3_PREFIX` distinct from `BACKUP_REMOTE` and off-VPS; AC-5 (`backup.sh`/`backup` service carry zero PITR references); `ca-certificates` is explicitly installed so `apt-get purge --auto-remove curl` retains it (wal-g TLS to S3 keeps working); the restore runbook's throwaway instance omits `archive_mode` so it cannot pollute the archive on promotion; base-backup `:ro` volume mount is safe (PG16 non-exclusive backup writes nothing into `PGDATA`); root-owned `backup-fetch` output is chowned to `postgres` by the stock entrypoint on restore-instance start.

## Findings

### Finding 1
- **Severity:** Minor
- **Category:** Business Logic
- **File:** DEPLOY.md
- **Line:** 238
- **Anchor:** "Restore to a point in time" runbook — `backup-fetch /restore LATEST`
- **Comment:** The headline use case (spec Overview / AC-4: "restore to the instant before an accidental delete") can require a base backup that **precedes** the target time, but the copy-paste command hardcodes `backup-fetch /restore LATEST`. If the accidental delete happened before the most recent nightly base backup (e.g. delete at 10:00 yesterday, base backup ran 01:00 today), `LATEST` is the base *after* the target, and Postgres will refuse recovery ("recovery_target_time is before backup end"). The surrounding prose does say to "fetch the one that PRECEDES your target time," so this is a command/text mismatch, not a logic bug. Fix: change the example to fetch a specific base-backup name selected from `backup-list` (e.g. `backup-fetch /restore base_00000001...`) and keep the "LATEST is fine only when the target is newer than the most recent base" caveat as a note, so a careless copy-paste doesn't pick the wrong base for the primary scenario.

### Finding 2
- **Severity:** Minor
- **Category:** Breaking Change
- **File:** deploy/postgres/Dockerfile
- **Line:** 14
- **Anchor:** `FROM postgres:16` (DEV-1: alpine → Debian base)
- **Comment:** Switching the DB image from `postgres:16-alpine` (musl libc) to `postgres:16` (Debian, glibc) changes the C library that backs PostgreSQL's default text collation. The PG16 on-disk *format* is identical (so the volume mounts fine — correctly noted in the Dockerfile), but musl and glibc **sort text differently**: an existing `postgres_data` volume that was `initdb`'d under the alpine image would carry indexes ordered by musl collation, and running it under glibc can yield wrong results on range/ORDER BY queries and corrupt unique-constraint enforcement until a `REINDEX`. Real-world risk is low here because `deploy/` is unreleased (a fresh deploy `initdb`s once under Debian — no migration), but it is a non-obvious foot-gun. Fix: add one line to the DEPLOY.md PITR notes — "if you are migrating an existing volume that was first created with the alpine image, run `REINDEX DATABASE` after the switch" — so an operator upgrading a test volume isn't bitten silently.

### Finding 3
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** deploy/docker-compose.prod.yml
- **Line:** 186
- **Anchor:** `pitr` service (`image: clinic-postgres-pitr:16`, no `build:`)
- **Comment:** The `pitr` sidecar references `image: clinic-postgres-pitr:16` with no `build:` block, relying on the `postgres` service to build and tag that image first. This holds for `docker compose up -d --build`, but the documented on-demand commands (`docker compose run --rm --entrypoint wal-g pitr backup-list`, the on-demand base backup, and the restore runbook — all invoked via the `pitr` service) will fail with "image not found" if run on a host where the stack was never brought up with `--build`. Consider giving `pitr` the same `build: { context: ./postgres }` block (compose de-duplicates identical builds, so no extra image) so the service is self-sufficient, or make the `--build` prerequisite explicit right next to each on-demand `run` command in DEPLOY.md.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 2 |
| Suggestion | 1 |
| **Total** | 3 |

## Resolution (2026-07-13)

All 3 findings applied directly per user directive ("do not challenge, just apply" — challenge step skipped).

| # | Severity | Fix applied |
|---|----------|-------------|
| 1 | Minor | `DEPLOY.md` restore runbook — replaced hardcoded `backup-fetch /restore LATEST` with an explicit `base_...` name selected from `backup-list`, plus a note explaining the accidental-delete case where the target precedes the latest base backup; kept the "LATEST only when target is newer than the most recent base" caveat. |
| 2 | Minor | `DEPLOY.md` PITR operational notes — added a note that reusing an alpine-`initdb`'d `postgres_data` volume under the Debian image requires `REINDEX DATABASE` due to the musl→glibc collation change (fresh deploys unaffected). |
| 3 | Suggestion | `deploy/docker-compose.prod.yml` — gave the `pitr` sidecar its own `build:` block (same context/tag as `postgres`) so on-demand `run` commands work without a prior `--build`; compose de-duplicates the identical build. |

Quality gate: `docker compose --env-file .env.example -f docker-compose.prod.yml config` → VALID, exit 0, no warnings. DEPLOY.md changes are docs-only.
