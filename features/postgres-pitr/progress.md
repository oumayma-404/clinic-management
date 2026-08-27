# Progress: PostgreSQL Point-in-Time Recovery (WAL-G)

**Started:** 2026-07-13
**Type:** Small
**Branch:** feature/windows-desktop-app (per user choice — all cloud-deployment work lives here untracked)

## Status
- [x] Implementation
- [x] Quality checks (config/compose/Dockerfile syntax + producer/consumer consistency)
- [x] Tests (infra-only → AC validation gates, see below)

## Test approach
Infra/config-only feature (Docker image, compose, shell scripts, `.env`, `DEPLOY.md`) — **no
C#/TS surface, so no xUnit/integration classes**. Per `/test-small-feature` "Config / infra-only
features", the test pass is **validation gates**: each AC maps to a static/render check run here,
or is marked **DEFERRED** (needs live off-site S3 + a running stack) with the exact command from
`DEPLOY.md` to run it.

## AC Validation Gates
| AC | Gate(s) run | Result |
|----|-------------|--------|
| AC-1 (archive_mode on + working archive_command; WAL lands off-site) | `docker compose config` renders `archive_mode=on`, `archive_command=wal-g wal-push %p`, `archive_timeout=300`; no exit-code masking in the command | **PASS (static)** — runtime "WAL appears in bucket" **DEFERRED** (`... logs -f postgres \| grep -i archiv` + `wal-g wal-verify timeline`, needs live S3 + write activity) |
| AC-2 (scheduled base backup; fresh install auto-produces first) | compose `pitr` sidecar reuses `clinic-postgres-pitr:16`, cron + first-boot bootstrap wired in `pitr-entrypoint.sh` (`backup-list \| grep '^base_'` → else run `pitr-backup.sh`) | **PASS (static)** — "appears in `backup-list`" **DEFERRED** (`... run --rm --no-deps --entrypoint wal-g pitr backup-list`) |
| AC-3 (dedicated off-site bucket, distinct; all creds from gitignored .env; .env.example placeholders; no tracked secret) | `docker compose config` VALID with **0 unresolved-var warnings** (producer↔consumer holds); `git check-ignore deploy/.env` ✓; no tracked `.env`; `WALG_S3_*` values are `CHANGE_ME_*`; `WALG_S3_PREFIX` (`s3://clinic-pitr-bucket/prod`) ≠ `BACKUP_REMOTE` (`offsite:clinic-backups`) and is an external endpoint, not the on-VPS `minio` service; single shared `WALG_S3_PREFIX` drives postgres + sidecar | **PASS** |
| AC-4 (restore to arbitrary `recovery_target_time`) | `DEPLOY.md` restore runbook present & coherent (fresh throwaway volume, `backup-fetch LATEST`, `restore_command=wal-g wal-fetch`, `recovery_target_time`, `recovery_target_action=promote`, `recovery.signal`) | **PASS (runbook)** — actual restore-to-instant **DEFERRED** (follow the `DEPLOY.md` "Restore to a point in time" steps against a real archive) |
| AC-5 (existing nightly pg_dump + MinIO archive unchanged) | `deploy/backup/backup.sh` has **zero** PITR/WAL references; `backup` service still present in compose, untouched | **PASS** |
| AC-6 (archiving failure not silent) | `archive_command` is a plain `wal-g wal-push %p` with no `\|\| true`/exit-masking → Postgres logs + retries + retains WAL on non-zero exit; documented in `DEPLOY.md` operational notes | **PASS (static/by-design)** — observing a real logged failure **DEFERRED** (point at bad creds, check `logs -f postgres`) |
| AC-7 (retention prunes to bounded window) | `pitr-backup.sh` runs `wal-g delete retain FULL "${PITR_RETAIN_BASE_BACKUPS:-7}" --confirm` after every base backup, under `set -e` | **PASS (static)** — actual prune **DEFERRED** (verify `backup-list` count ≤ retain after N+1 backups) |

## Gates executed this session
- `sh -n` on `pitr-backup.sh` + `pitr-entrypoint.sh`: **OK**
- Line endings LF on both new scripts (match `backup.sh`): **OK**
- `docker compose --env-file .env.example -f docker-compose.prod.yml config`: **VALID, exit 0, no warnings** (every `${VAR}` resolved from `.env.example`; PITR command/env render correctly)
- Secret hygiene: `deploy/.env` gitignored, no tracked `.env`, `.env.example` placeholders only
- Distinctness: `WALG_S3_PREFIX` ≠ `BACKUP_REMOTE`, external S3 (not on-VPS MinIO)
- `pg_dump` path unchanged: `backup.sh`/`backup` service carry zero PITR references

**No test failures.** 4/7 ACs fully verified statically here; AC-1/AC-2/AC-4/AC-7 have a runtime
portion that requires a live off-site S3 bucket + running stack and is **DEFERRED** to the
operator (exact commands live in `DEPLOY.md`).

## Working tree note (start of session)
Pre-existing uncommitted/untracked, NOT part of this feature — exclude from commits:
- `M .gitignore`, `M api/ClinicManagement.API/appsettings.json`, `M web/Dockerfile`
- `?? DEPLOY.md`, `?? deploy/`, `?? features/cloud-deployment/` (prior cloud-deployment feature, untracked)
This feature adds the PITR pieces under `deploy/` and a PITR section to `DEPLOY.md`.

## Files Changed
- `deploy/postgres/Dockerfile` (new) — custom `postgres:16` + `wal-g` + `supercronic` image
- `deploy/postgres/pitr-backup.sh` (new) — `wal-g backup-push` + retention prune (AC-2/AC-7)
- `deploy/postgres/pitr-entrypoint.sh` (new) — first-boot initial base backup + supercronic scheduler
- `deploy/docker-compose.prod.yml` — postgres now builds the custom image, enables `archive_mode`/
  `archive_command`/`archive_timeout` + WAL-G env; new `pitr` sidecar for base backups + retention
- `deploy/.env.example` — new `WALG_*` / `PITR_*` keys with placeholders (AC-3)
- `DEPLOY.md` — architecture diagram + "Point-in-Time Recovery (PITR)" section (enable, verify,
  restore-to-timestamp runbook, operational notes) + Operations table rows

## Quality Checks
- `sh -n` on both scripts: OK
- `docker compose config`: VALID (structure/interpolation)
- Producer/consumer: every non-defaulted `${VAR}` in compose exists in `.env.example`; no orphans
- Line endings: both new `.sh` are LF (matches existing `backup.sh`)

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Added `supercronic` as the sidecar scheduler | Debian base has no busybox crond, and vixie `cron` scrubs the container env WAL-G needs; supercronic is the container-idiomatic cron (env passthrough + stdout logging). Infra tooling choice, no external contract. |
| Added `PITR_ARCHIVE_TIMEOUT` env key (default 300s) | Spec listed env keys with "e.g."; bounds RPO for a low-write clinic. Sensible default, config-only. |
| New `pitr` sidecar service reusing the custom postgres image | Spec says base backups "run on a schedule" without pinning where; a sidecar mirrors the existing `backup` sidecar pattern and shares one built image. |

## Significant Deviations
### DEV-1: Custom image base `postgres:16-alpine` → `postgres:16` (Debian)
- **Spec said:** custom image = `postgres:16-alpine` + `wal-g`.
- **Actual:** `postgres:16` (Debian bookworm, glibc) + prebuilt `wal-g`.
- **Why:** WAL-G ships only glibc prebuilt binaries; there is no musl/alpine build, so a prebuilt
  wal-g cannot run on Alpine's musl libc. Same PG16 on-disk format → `postgres_data` volume stays
  compatible.
- **Impact:** Larger image; DB container OS changes from Alpine to Debian. No data/behavior change.
- **Approved:** Y (asked via AskUserQuestion — user chose "Debian postgres:16 + prebuilt").
