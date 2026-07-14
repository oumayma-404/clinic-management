#!/bin/sh
# Off-server backup (AC-7): pg_dump (custom format, pg_restore-able) + a MinIO data archive,
# uploaded to the operator-configured off-server destination. Fails loud on any error and
# never produces a silent partial (set -e + explicit checks).
set -eu

TS="$(date -u +%Y%m%dT%H%M%SZ)"
WORK="/backups/${TS}"
mkdir -p "${WORK}"

echo "[backup] ${TS} starting"

# 1. PostgreSQL dump — custom format so it restores with `pg_restore` (connection via PG* env).
DB_FILE="${WORK}/db-${TS}.dump"
pg_dump --format=custom --no-owner --no-privileges --file="${DB_FILE}"
if [ ! -s "${DB_FILE}" ]; then
	echo "[backup] ERROR: pg_dump produced an empty file — aborting" >&2
	exit 1
fi
echo "[backup] db dump: $(du -h "${DB_FILE}" | cut -f1) -> $(basename "${DB_FILE}")"

# 2. MinIO data archive — tar the object-store volume (mounted read-only).
MINIO_FILE="${WORK}/minio-${TS}.tar.gz"
tar czf "${MINIO_FILE}" -C /minio-data .
echo "[backup] minio archive: $(du -h "${MINIO_FILE}" | cut -f1) -> $(basename "${MINIO_FILE}")"

# 3. Upload off-server.
if [ -n "${BACKUP_REMOTE:-}" ]; then
	rclone copy "${WORK}" "${BACKUP_REMOTE}/${TS}" --config /config/rclone/rclone.conf
	echo "[backup] uploaded off-server -> ${BACKUP_REMOTE}/${TS}"
else
	echo "[backup] WARNING: BACKUP_REMOTE not set — kept LOCAL ONLY at ${WORK} (not off-server)" >&2
fi

# 4. Prune local staged copies older than the retention window.
RETENTION="${BACKUP_RETENTION_DAYS:-14}"
find /backups -mindepth 1 -maxdepth 1 -type d -name '20*' -mtime "+${RETENTION}" -exec rm -rf {} + 2>/dev/null || true

echo "[backup] ${TS} done"
