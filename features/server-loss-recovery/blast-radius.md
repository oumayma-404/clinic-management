# Blast radius — server-loss-recovery (Phase A)

Written before the first edit. « Prod » column = what a later deploy of this row would restart.

| # | Touching | What it is | Other consumers | Verdict | Prod restart when deployed |
|---|----------|------------|-----------------|---------|----------------------------|
| 1 | `deploy/backup/backup.sh` | nightly sidecar script | `RESTORE-DRILL.md`, `README.md` § backup, `backup-settings.tsx` comment (quotes its « LOCAL ONLY » line) | must change — docs + the quoted line move with it | `backup` only |
| 2 | `deploy/backup/entrypoint.sh` | sidecar start | none | must re-test (roundtrip) | `backup` only |
| 3 | `deploy/backup/Dockerfile` | sidecar image | `deploy-hosted.yml` `build … backup` | unaffected — adds one COPY | `backup` only |
| 4 | `docker-compose.prod.yml` › `backup` service (env, volumes) | shared infra base | `docker-compose.hosted.yml` `extends` it; `VPS-BRINGUP.md` § 5 rclone; `deploy-hosted.yml` tar `--exclude rclone/rclone.conf` | must change — bring-up + README; the exclude stays harmless | `backup` only |
| 5 | `docker-compose.hosted.yml` › `backup` override (ring mount) + new `backup_status` volume | hosted deployment | `KEY-CUSTODY.md` FR-3.11, `KeyRingGenerationMarker.cs` doc (« ring NEVER mounted into the sidecar »), prod.yml `keyring_marker` comment | must change — the three statements are rewritten to FR-3.11 in the same commit | `backup` only |
| 6 | `.env.example`, `.env.hosted.example` | operator templates | `VPS-BRINGUP.md` table, `README.md` table | must change | none |
| 7 | `Backup__Remote` reported to the API | `DataResidencyAssurance` input | `DataResidencyAssuranceTests` | must re-test — key unchanged; README's « reported, not verified » sentence updated | `api` (only if the env line changes) |
| 8 | `.github/workflows/ci.yml` (new `backup-roundtrip` job + shellcheck) | CI | none | unaffected — additive jobs, path-filtered | none |
| 9 | `postgres`, `minio`, `pitr`, `api`, `web`, `console`, `caddy` service definitions | live services | — | **NOT touched in Parts 1–2** — a change here recreates the database or the app | — |
| 10 | anything under `deploy/postgres/` (incl. `pitr-backup.sh`) | baked into `clinic-postgres-pitr:16`, the image **both** `postgres` and `pitr` run | the live database | **NOT touched.** A pitr status file was written and reverted: a changed image recreates `postgres`, i.e. restarts the database on the next deploy. WAL health is read from `pg_stat_archiver` instead | — |
| 11 | `docker-compose.hosted.yml` › `api` (Part 3: `backup_status` mount, `Backup__StatusFile`, `Backup__RemoteEndpoint`) | the app | — | must re-test — additive env + a read-only mount | **`api` restarts** (Phase B2, at night) |

## Deploy note (Phase B, not now)

The normal `deploy-hosted.yml` run pulls api/web/console and recreates whatever changed — a full deploy
restarts the app. Shipping Parts 1–2 alone is a targeted step instead: ship `deploy/`, `build backup`,
`up -d --no-deps --no-build backup`, after a `--dry-run` that must list `backup` and nothing else
(with `CLINIC_IMAGE_TAG` set to the live tag, or every service falsely reads `Recreate`).
