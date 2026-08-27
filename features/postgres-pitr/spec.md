# Feature Specification: PostgreSQL Point-in-Time Recovery (WAL-G, cloud deployment)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-13
**Scope:** BE (infra/config — Docker, Postgres config, env, DEPLOY.md; near-zero application source change)
**Feature:** Add continuous WAL archiving + periodic physical base backups to the single-VPS Cloud deployment via **WAL-G**, streamed to a **dedicated off-site S3-compatible bucket**, so the clinic can restore the database to *any second* (e.g. just before an accidental delete) instead of losing up to a day. Complements — does not replace — the existing nightly `pg_dump`.

## Overview
The existing `deploy/` backup takes a nightly logical dump (`pg_dump`) + a MinIO archive. That caps worst-case loss at ~24h and can't rewind to a precise moment. PITR closes that gap: Postgres continuously ships its write-ahead log (WAL) off-site, and WAL-G takes periodic base backups, so recovery can target any point in time between base backups. All new config is infra-only and lives under `deploy/`; secrets stay in the gitignored `.env`.

## What Changes
- The Postgres service runs from a **custom image** (`postgres:16-alpine` + the `wal-g` binary) with archiving enabled: `archive_mode = on`, `archive_command = 'wal-g wal-push %p'` (`wal_level` stays at the PG16 default `replica`).
- **Continuous WAL push** — every completed WAL segment is uploaded off-site by `wal-g wal-push` (invoked by Postgres's `archive_command`).
- **Periodic physical base backup** — `wal-g backup-push` runs on a schedule (default nightly) so a fresh base + its WAL chain is always available; WAL-G correlates them by the shared S3 prefix.
- **Dedicated off-site S3 destination** — WAL + base backups go to an S3-compatible bucket that is **not** on this VPS (survives total server loss), configured entirely via new gitignored `.env` keys (endpoint, region, access/secret key, bucket prefix).
- **Retention** — old base backups + superseded WAL are pruned to a configured window (default keep last N base backups) so the bucket doesn't grow unbounded.
- **`DEPLOY.md`** gains a PITR section: enabling it, the required off-site bucket, and a step-by-step restore-to-a-timestamp runbook.
- The existing nightly `pg_dump` + MinIO archive backup is **unchanged** and keeps running.

## Acceptance Criteria
- **AC-1:** Postgres starts with `archive_mode = on` and a working `archive_command`; after write activity, completed WAL segments appear in the off-site S3 bucket (visible via `wal-g wal-verify` / bucket listing).
- **AC-2:** A base backup runs on the configured schedule and appears in `wal-g backup-list` against the off-site bucket; a fresh install produces its first base backup without manual steps.
- **AC-3:** WAL + base backups land in a dedicated off-site S3-compatible bucket distinct from the on-VPS MinIO and from the `pg_dump` `BACKUP_REMOTE`; every credential comes from the gitignored `.env`, and `.env.example` enumerates each new key with placeholders — no secret in any tracked file.
- **AC-4:** Following the `DEPLOY.md` runbook, a restore to an arbitrary `recovery_target_time` reconstructs the database to that instant (base backup fetch + WAL replay), verified into a throwaway instance — data written after the target time is absent, data before it is present.
- **AC-5:** The existing nightly `pg_dump` + MinIO archive backup still runs unchanged (PITR complements, does not replace it).
- **AC-6:** Archiving failure is **not silent** — a bad-credentials/unreachable-bucket `archive_command` failure surfaces in the Postgres logs (Postgres retries and retains the WAL), rather than appearing to succeed.
- **AC-7:** Retention prunes base backups/WAL beyond the configured window so the off-site bucket stays bounded.

## Data / Schema Changes
- None. Config only. New gitignored `.env` keys (e.g. `WALG_S3_PREFIX`, `WALG_S3_ENDPOINT`/`AWS_ENDPOINT`, `AWS_REGION`, `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, base-backup cron, retention count), all mirrored in `.env.example` with placeholders.

## Out of Scope
- **Streaming replication / hot standby / automatic failover** — that's *availability*, a different goal; PITR here is about *durability* (no data loss).
- Migrating to managed Postgres (RDS / Cloud SQL) — MinIO/Postgres stay containerized.
- Changing or removing the existing logical `pg_dump` backup.
- Multi-region / multi-bucket WAL redundancy.
- The offline/LAN (Local mode) packaging — this is Cloud-mode only.
- Provisioning the off-site S3 bucket itself (operator work, per `DEPLOY.md`).

## Edge Cases (Critical only)
- **Off-site bucket unreachable for a long stretch** — Postgres cannot recycle WAL until `archive_command` succeeds, so `pg_wal` grows and can eventually fill the disk. Must be operator-visible (logs / a note in `DEPLOY.md`), not a silent creep to a full volume.
- **WAL without a base backup can't restore** — the *first* `wal-g backup-push` must complete before PITR is usable; document that PITR is only in effect after the first base backup.
- **Base backups and WAL must share the same S3 prefix** — otherwise WAL-G can't correlate the chain; a single `WALG_S3_PREFIX` drives both.
- **Restore targets a fresh/empty data dir**, never an overwrite of live `PGDATA` in place — the runbook restores into a throwaway/new volume to avoid clobbering a running primary.
