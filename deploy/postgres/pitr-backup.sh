#!/bin/sh
# PITR physical base backup + retention prune (AC-2, AC-7).
#
# Reads PGDATA (mounted read-only from the postgres volume) and connects to the running primary
# (PG* env) so WAL-G can bracket the copy with pg_backup_start/stop, then uploads to the SAME
# off-site S3 prefix as the WAL (shared WALG_S3_PREFIX — WAL-G correlates the base + its WAL
# chain by that prefix). Fails loud on any error (set -e); never a silent partial.
set -eu

# The hop is encrypted, or there is no base backup (hosted-security-hardening Part 2, FR-2.3's ⚠️).
#
# ⚠️ `wal-g backup-push` brackets its copy with pg_backup_start/stop over an ordinary connection using this
# container's own libpq environment — so, like the logical sidecar, this is a hop that fails on a schedule
# rather than at deploy time, and a PITR chain with no base backup is discovered by needing to restore.
#
# ⚠️ It asks PostgreSQL whether THIS connection is encrypted rather than trusting PGSSLMODE: the variable
# states an intention, `pg_stat_ssl` states what happened. Where they come apart is `require` and libpq's own
# default `prefer` — both encrypt while verifying NOTHING, so an env-var check passes on exactly the
# configuration FR-2.1 exists to rule out. Identity is what verify-full plus PGSSLROOTCERT buy, and a wrong
# root fails this block by failing to connect at all.
#
# Byte-identical to the block in ../backup/backup.sh; the two sidecars share no image, so there is nowhere to
# put one copy. Change both.
SSL_IN_USE="$(psql -tAqc 'SELECT ssl FROM pg_stat_ssl WHERE pid = pg_backend_pid()' 2>&1)" || {
	echo "[pitr] ERROR: could not reach PostgreSQL to verify the connection is encrypted." >&2
	echo "[pitr]        psql said: ${SSL_IN_USE}" >&2
	echo "[pitr]        Check PGSSLMODE (expected verify-full) and PGSSLROOTCERT in docker-compose." >&2
	exit 1
}
if [ "$(echo "${SSL_IN_USE}" | tr -d '[:space:]')" != "t" ]; then
	echo "[pitr] ERROR: this connection to PostgreSQL is NOT encrypted (pg_stat_ssl.ssl = '${SSL_IN_USE}')." >&2
	echo "[pitr]        Refusing to take a base backup over a cleartext hop. Set PGSSLMODE=verify-full and" >&2
	echo "[pitr]        PGSSLROOTCERT=/certs/ca.crt on the pitr service." >&2
	exit 1
fi
echo "[pitr] connection to PostgreSQL is encrypted and verified"

echo "[pitr] $(date -u +%Y-%m-%dT%H:%M:%SZ) base backup starting -> ${WALG_S3_PREFIX}"
wal-g backup-push "${PGDATA}"
echo "[pitr] base backup complete"

# Retention: keep the N most recent FULL base backups; older bases and the WAL they no longer
# anchor are pruned so the off-site bucket stays bounded (AC-7).
RETAIN="${PITR_RETAIN_BASE_BACKUPS:-7}"
echo "[pitr] pruning to last ${RETAIN} base backup(s)"
wal-g delete retain FULL "${RETAIN}" --confirm

echo "[pitr] $(date -u +%Y-%m-%dT%H:%M:%SZ) done"
