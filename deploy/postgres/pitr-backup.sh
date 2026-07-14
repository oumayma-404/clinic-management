#!/bin/sh
# PITR physical base backup + retention prune (AC-2, AC-7).
#
# Reads PGDATA (mounted read-only from the postgres volume) and connects to the running primary
# (PG* env) so WAL-G can bracket the copy with pg_backup_start/stop, then uploads to the SAME
# off-site S3 prefix as the WAL (shared WALG_S3_PREFIX — WAL-G correlates the base + its WAL
# chain by that prefix). Fails loud on any error (set -e); never a silent partial.
set -eu

echo "[pitr] $(date -u +%Y-%m-%dT%H:%M:%SZ) base backup starting -> ${WALG_S3_PREFIX}"
wal-g backup-push "${PGDATA}"
echo "[pitr] base backup complete"

# Retention: keep the N most recent FULL base backups; older bases and the WAL they no longer
# anchor are pruned so the off-site bucket stays bounded (AC-7).
RETAIN="${PITR_RETAIN_BASE_BACKUPS:-7}"
echo "[pitr] pruning to last ${RETAIN} base backup(s)"
wal-g delete retain FULL "${RETAIN}" --confirm

echo "[pitr] $(date -u +%Y-%m-%dT%H:%M:%SZ) done"
