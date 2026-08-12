#!/bin/sh
# Off-server backup (AC-7): pg_dump (custom format, pg_restore-able) + a MinIO data archive,
# uploaded to the operator-configured off-server destination. Fails loud on any error and
# never produces a silent partial (set -e + explicit checks).
set -eu

TS="$(date -u +%Y%m%dT%H%M%SZ)"
WORK="/backups/${TS}"
mkdir -p "${WORK}"

echo "[backup] ${TS} starting"

# 0. The hop is encrypted, or there is no backup (hosted-security-hardening Part 2, FR-2.3's ⚠️).
#
# ⚠️ This sidecar connects with its OWN credentials and its own libpq environment, so it is the half of the
# transit change that fails at 02:00 rather than at deploy time — and a nightly dump that stopped running is
# discovered by needing it. Hence a check that FAILS THE RUN rather than a warning nobody reads.
#
# ⚠️ It asks PostgreSQL whether THIS connection is encrypted rather than checking that PGSSLMODE reads
# verify-full: the variable states an intention, `pg_stat_ssl` states what happened. Where they come apart is
# `require` and libpq's own default `prefer` — both encrypt while verifying NOTHING, so an env-var check
# passes on exactly the configuration FR-2.1 exists to rule out. Identity is what verify-full plus
# PGSSLROOTCERT buy, and a wrong root fails this block by failing to connect at all (verified by pointing it
# at the minio leaf: « SSL error: certificate verify failed », exit 1, nothing dumped).
#
# Byte-identical to the block in ../postgres/pitr-backup.sh; the two sidecars share no image, so there is
# nowhere to put one copy. Change both.
SSL_IN_USE="$(psql -tAqc 'SELECT ssl FROM pg_stat_ssl WHERE pid = pg_backend_pid()' 2>&1)" || {
	echo "[backup] ERROR: could not reach PostgreSQL to verify the connection is encrypted." >&2
	echo "[backup]        psql said: ${SSL_IN_USE}" >&2
	echo "[backup]        Check PGSSLMODE (expected verify-full) and PGSSLROOTCERT in docker-compose." >&2
	exit 1
}
if [ "$(echo "${SSL_IN_USE}" | tr -d '[:space:]')" != "t" ]; then
	echo "[backup] ERROR: this connection to PostgreSQL is NOT encrypted (pg_stat_ssl.ssl = '${SSL_IN_USE}')." >&2
	echo "[backup]        Refusing to dump patient data over a cleartext hop. Set PGSSLMODE=verify-full and" >&2
	echo "[backup]        PGSSLROOTCERT=/certs/ca.crt on the backup service." >&2
	exit 1
fi
echo "[backup] connection to PostgreSQL is encrypted and verified"

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
