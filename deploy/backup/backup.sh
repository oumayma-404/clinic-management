#!/bin/sh
# Off-server backup (AC-7): pg_dump (custom format, pg_restore-able) + an INCREMENTAL mirror of the
# object store, ENCRYPTED (hosted-security-hardening FR-3.6), the dump VERIFIED BY BEING DECRYPTED
# (FR-3.7), stamped with the key-ring generation in force (FR-3.9), then uploaded to the
# operator-configured off-server destination. Fails loud on any error and never produces a silent
# partial (set -e + explicit checks).
#
# ⚠️ The object half was a nightly full `tar czf` until `large-file-transfer` Part 3, which cost about
# fifteen copies of every hosted byte on this disk. See mirror-objects.sh for the measurement.
set -eu

TS="$(date -u +%Y%m%dT%H%M%SZ)"
WORK="/backups/${TS}"
mkdir -p "${WORK}"

# The object mirror is cumulative and lives OUTSIDE the dated run directories, which the retention prune
# clears. `attic` holds what has been deleted from the store, dated, so a file removed by mistake is still
# recoverable for the retention window — the one thing the nightly full archive gave away for free.
OBJECTS_DIR="/backups/objects"
ATTIC_DIR="/backups/attic"
OBJECTS_MANIFEST="/backups/objects.manifest"

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

# 0b. There is a backup encryption key, or there is no backup (FR-3.6).
#
# ⚠️ Refusing is the whole point, and « encrypt if a key happens to be set » is the version that fails
# silently: the run would succeed, the operator would see « uploaded off-server », and a complete copy of
# every practice's medical records would sit on somebody else's storage in the clear. The one thing this
# script must never do is decide by itself that encryption was optional tonight.
#
# ⚠️ BACKUP_AGE_RECIPIENT is a PUBLIC key (age1…): the sidecar can encrypt and CANNOT decrypt what it wrote
# earlier — which is deliberate, since a container reachable from the network holding the identity that opens
# every archive is the exposure the encryption exists to prevent. The identity is needed only for the
# self-check below and for a real restore, and lives wherever KEY-CUSTODY.md says it lives.
if [ -z "${BACKUP_AGE_RECIPIENT:-}" ]; then
	echo "[backup] ERROR: BACKUP_AGE_RECIPIENT is not set — refusing to upload an unencrypted copy of every" >&2
	echo "[backup]        practice's records (FR-3.6). Generate a key pair with \`age-keygen\`, put the PUBLIC" >&2
	echo "[backup]        key here and store the private one as deploy/KEY-CUSTODY.md describes." >&2
	echo "[backup]        ⚠️ If that private key is lost, every backup taken with it is unrecoverable." >&2
	exit 1
fi

# 1. PostgreSQL dump — custom format so it restores with `pg_restore` (connection via PG* env).
DB_FILE="${WORK}/db-${TS}.dump"
pg_dump --format=custom --no-owner --no-privileges --file="${DB_FILE}"
if [ ! -s "${DB_FILE}" ]; then
	echo "[backup] ERROR: pg_dump produced an empty file — aborting" >&2
	exit 1
fi
echo "[backup] db dump: $(du -h "${DB_FILE}" | cut -f1) -> $(basename "${DB_FILE}")"

# 2. The object store — an INCREMENTAL encrypted mirror, not a nightly archive.
#
# ⚠️ This used to be `tar czf` of the whole volume, age-encrypted, kept BACKUP_RETENTION_DAYS times over on
# this disk: about FIFTEEN copies of every hosted byte, plus one more off-site every night for ever. On the
# live VPS (96 Go, 78 Go free) that left room for roughly 5 Go of objects, and a full disk stops every
# cabinet on the box at once — which is what had the coffre threshold pinned at 25 Mo. Objects here are
# immutable once written, so a mirror refreshed per object costs what actually changed. See
# mirror-objects.sh for the rest of the reasoning.
#
# ⚠️ It writes into /backups, NOT into ${WORK}: the mirror is cumulative and must survive the retention
# prune that clears the dated run directories. A mirror inside a run directory would be deleted after two
# weeks and silently rebuilt from scratch, restoring the very cost this removes.
/usr/local/bin/mirror-objects.sh \n	/minio-data \n	"${OBJECTS_DIR}" \n	"${ATTIC_DIR}/${TS}" \n	"${OBJECTS_MANIFEST}" \n	"${BACKUP_AGE_RECIPIENT}"

# The manifest travels with the run so a rebuilt server can tell what the mirror is supposed to contain.
cp "${OBJECTS_MANIFEST}" "${WORK}/objects-${TS}.manifest"

# 3. Stamp the key-ring generation this dump belongs to (FR-3.9).
#
# ⚠️ The Data Protection key ring is NEVER mounted here — one archive holding both the ciphertext and the key
# that opens it defeats the encryption entirely. The API writes a marker file carrying key IDS ONLY (no key
# material) and this mounts it read-only. An absent marker stamps `unknown`, and check-keyring.sh refuses an
# unknown stamp: erring toward « I cannot prove this matches » is the safe direction, because the failure it
# guards against is silent (a restored practice whose every second factor is undecryptable).
STAMP_FILE="${WORK}/keyring-${TS}.txt"
if [ -r "${KEYRING_MARKER_FILE:-/keyring-marker/generation}" ]; then
	cp "${KEYRING_MARKER_FILE:-/keyring-marker/generation}" "${STAMP_FILE}"
	echo "[backup] key-ring stamp: $(head -n 1 "${STAMP_FILE}")"
else
	echo "active=unknown" > "${STAMP_FILE}"
	echo "[backup] WARNING: no key-ring marker readable — stamped 'unknown'; a restore will be REFUSED until" >&2
	echo "[backup]          the API has written one (FR-3.9). Check the keyring_marker volume mount." >&2
fi

# 4. Encrypt everything that leaves this host (FR-3.6).
#
# ⚠️ Only the dump and the manifest are encrypted HERE. The objects were encrypted one by one in step 2 —
# that is what makes the mirror incremental, since `age` output is nondeterministic and re-encrypting an
# unchanged object would produce different bytes every night and be re-uploaded every night.
for PLAIN in "${DB_FILE}" "${WORK}/objects-${TS}.manifest"; do
	age --encrypt --recipient "${BACKUP_AGE_RECIPIENT}" --output "${PLAIN}.age" "${PLAIN}"
	if [ ! -s "${PLAIN}.age" ]; then
		echo "[backup] ERROR: age produced an empty file for $(basename "${PLAIN}") — aborting" >&2
		exit 1
	fi
	rm -f "${PLAIN}"
done
echo "[backup] encrypted with age -> $(basename "${DB_FILE}").age"

# 5. A backup nobody can restore is not a backup (FR-3.7): decrypt what was just written and confirm it PARSES.
#
# ⚠️ « It decrypts » is not the check — a truncated dump decrypts perfectly. `pg_restore --list` is what proves
# the archive's own table of contents is readable, which is the same verification the in-app backup already
# performs, and a NON-EMPTY listing is what proves it is not an empty archive.
#
# ⚠️ It runs here, in the same script, rather than against the remote, because that is what makes the check
# real: an rclone *crypt* remote would put the encryption in a gitignored config file, invisible to review and
# unverifiable without a round trip nobody would automate.
#
# ⚠️ Skipped, LOUDLY, when no identity is mounted. Most deployments deliberately do not keep the private key
# beside the encrypting container — see the ⚠️ on BACKUP_AGE_RECIPIENT — and in that case FR-3.7's verification
# is the quarterly drill in deploy/RESTORE-DRILL.md instead. A skip that said nothing would let « verified »
# quietly mean « not checked ».
if [ -n "${BACKUP_AGE_IDENTITY_FILE:-}" ] && [ -r "${BACKUP_AGE_IDENTITY_FILE}" ]; then
	VERIFY_DIR="$(mktemp -d)"
	age --decrypt --identity "${BACKUP_AGE_IDENTITY_FILE}" \
		--output "${VERIFY_DIR}/db.dump" "${DB_FILE}.age" || {
		echo "[backup] ERROR: the dump just written could NOT be decrypted — failing the run (FR-3.7)." >&2
		rm -rf "${VERIFY_DIR}"
		exit 1
	}
	TOC_LINES="$(pg_restore --list "${VERIFY_DIR}/db.dump" 2>/dev/null | grep -c ';' || true)"
	rm -rf "${VERIFY_DIR}"
	if [ "${TOC_LINES}" -lt 1 ]; then
		echo "[backup] ERROR: the decrypted dump does not parse as a pg_restore archive — failing the run." >&2
		exit 1
	fi
	echo "[backup] verified: decrypts and parses (${TOC_LINES} archive entries)"
else
	echo "[backup] NOTE: BACKUP_AGE_IDENTITY_FILE is not mounted, so this run did not decrypt what it wrote." >&2
	echo "[backup]       FR-3.7's verification is then the quarterly drill — see deploy/RESTORE-DRILL.md." >&2
fi

# 6. Upload off-server.
RETENTION="${BACKUP_RETENTION_DAYS:-14}"
if [ -n "${BACKUP_REMOTE:-}" ]; then
	RCLONE="rclone --config /config/rclone/rclone.conf"

	# The dated run: the dump, the key-ring stamp and the manifest. Small, and one per night as before.
	${RCLONE} copy "${WORK}" "${BACKUP_REMOTE}/${TS}"

	# ⚠️ The objects go up with `sync`, not `copy`, and the difference is the whole point: `copy` never
	# removes, so the remote would grow for ever and a deleted file would silently stay backed up. `sync`
	# with `--backup-dir` MOVES what is no longer in the mirror into the same dated attic used locally,
	# rather than deleting it — so a removal is still recoverable for the retention window.
	#
	# ⚠️ The attic must be OUTSIDE the destination or rclone refuses the run; `objects/` and `attic/` are
	# siblings for exactly that reason.
	${RCLONE} sync "${OBJECTS_DIR}" "${BACKUP_REMOTE}/objects" 		--backup-dir "${BACKUP_REMOTE}/attic/${TS}"

	# Age out the remote attic on the same window as the local one.
	${RCLONE} delete "${BACKUP_REMOTE}/attic" --min-age "${RETENTION}d" || true
	${RCLONE} rmdirs "${BACKUP_REMOTE}/attic" --leave-root || true

	echo "[backup] uploaded off-server -> ${BACKUP_REMOTE}/${TS} + objects mirror"
else
	echo "[backup] WARNING: BACKUP_REMOTE not set — kept LOCAL ONLY at ${WORK} (not off-server)" >&2
fi

# 7. Prune the dated run directories and the attic. The MIRROR itself is never pruned — it is the backup.
find /backups -mindepth 1 -maxdepth 1 -type d -name '20*' -mtime "+${RETENTION}" -exec rm -rf {} + 2>/dev/null || true
find "${ATTIC_DIR}" -mindepth 1 -maxdepth 1 -type d -name '20*' -mtime "+${RETENTION}" -exec rm -rf {} + 2>/dev/null || true

echo "[backup] ${TS} done"
