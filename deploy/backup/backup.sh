#!/bin/sh
# Off-server backup (AC-7): pg_dump (custom format, pg_restore-able) + an INCREMENTAL mirror of the
# object store + the Data Protection key ring, ENCRYPTED (hosted-security-hardening FR-3.6), the dump
# VERIFIED BY BEING DECRYPTED (FR-3.7), stamped with the key-ring generation in force (FR-3.9), then
# uploaded to the off-server destination. Fails loud on any error and never produces a silent partial
# (set -e + explicit checks + the EXIT trap below, which removes a failed run's directory whole).
#
# ⚠️ The object half was a nightly full `tar czf` until `large-file-transfer` Part 3, which cost about
# fifteen copies of every hosted byte on this disk. See mirror-objects.sh for the measurement.
#
# ⚠️ Its outcome is written to ${BACKUP_STATUS_DIR}/backup.status on EVERY run, success or failure, and
# the API reads that file (server-loss-recovery Part 3). Measured 2026-10-06: this script aborted every
# night for 33 nights (`mirror-objects.sh` was called with literal `\n` instead of line continuations)
# and the only record was a `docker logs` line nobody read — the patient files had no off-site copy the
# whole time. A backup that fails must leave something a machine can check.
set -eu

TS="$(date -u +%Y%m%dT%H%M%SZ)"
WORK="/backups/${TS}"

# The object mirror is cumulative and lives OUTSIDE the dated run directories, which the retention prune
# clears. `attic` holds what has been deleted from the store, dated, so a file removed by mistake is still
# recoverable for the retention window — the one thing the nightly full archive gave away for free.
OBJECTS_DIR="/backups/objects"
ATTIC_DIR="/backups/attic"
OBJECTS_MANIFEST="/backups/objects.manifest"

STATUS_DIR="${BACKUP_STATUS_DIR:-/status}"
STATUS_FILE="${STATUS_DIR}/backup.status"

# The off-site remote is configured from the environment (RCLONE_CONFIG_OFFSITE_* in docker-compose.prod.yml),
# never from a file: `/dev/null` keeps rclone's configuration in memory only. The gitignored rclone.conf this
# replaced was never written on the live server, so no nightly copy had ever left it.
RCLONE="rclone --config /dev/null"

# Which step is running, so a failure says where it stopped rather than only that it did.
STAGE="start"

# ── Outcome, written whatever happens ──────────────────────────────────────────────────────────────────
#
# key=value lines, the same shape as the key-ring marker, so `grep` reads it here and the API parses it
# without a JSON library in this image. `lastSuccess` is carried over from the previous file on a failure —
# « when did a copy last leave this server? » is the question the reader has to answer.
write_status() {
	OUTCOME="$1"
	PREVIOUS_SUCCESS=""
	if [ -r "${STATUS_FILE}" ]; then
		PREVIOUS_SUCCESS="$(grep '^lastSuccess=' "${STATUS_FILE}" | head -n 1 | cut -d= -f2- || true)"
	fi
	NOW="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
	if [ "${OUTCOME}" = "succeeded" ]; then
		LAST_SUCCESS="${NOW}"
		FAILED_STAGE=""
	else
		LAST_SUCCESS="${PREVIOUS_SUCCESS}"
		FAILED_STAGE="${STAGE}"
	fi
	mkdir -p "${STATUS_DIR}"
	# Written beside and moved into place, so a reader never sees half a file.
	{
		echo "run=${TS}"
		echo "outcome=${OUTCOME}"
		echo "stage=${FAILED_STAGE}"
		echo "finished=${NOW}"
		echo "lastSuccess=${LAST_SUCCESS}"
	} > "${STATUS_FILE}.partial"
	mv "${STATUS_FILE}.partial" "${STATUS_FILE}"
}

# ⚠️ On ANY non-zero exit — `set -e` included — the run's directory goes, whole. A half-written run left
# behind looks complete to an operator choosing what to restore (features/LEARNINGS.md: « no silent partial
# success »), and before the encryption step it holds a PLAINTEXT dump of every practice's records. That is
# exactly what 33 failed nights left on the live disk.
finish() {
	RC=$?
	if [ "${RC}" -ne 0 ]; then
		rm -rf "${WORK}"
		write_status failed || true
		echo "[backup] ${TS} FAILED at stage '${STAGE}' (exit ${RC}) — nothing from this run was kept" >&2
	fi
}
trap finish EXIT

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
STAGE="tls"
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
STAGE="config"
if [ -z "${BACKUP_AGE_RECIPIENT:-}" ]; then
	echo "[backup] ERROR: BACKUP_AGE_RECIPIENT is not set — refusing to upload an unencrypted copy of every" >&2
	echo "[backup]        practice's records (FR-3.6). Generate a key pair with \`age-keygen\`, put the PUBLIC" >&2
	echo "[backup]        key here and store the private one as deploy/KEY-CUSTODY.md describes." >&2
	echo "[backup]        ⚠️ If that private key is lost, every backup taken with it is unrecoverable." >&2
	exit 1
fi

# 0c. There is somewhere off this server to put it, or there is no backup.
#
# ⚠️ This was a WARNING (« kept LOCAL ONLY ») and it is now a refusal. A copy that never leaves the server dies
# with the server, which is the one event a backup exists for — so « succeeded, locally » is a success at
# nothing. The remote is named `offsite` because that is the name the environment configures.
if [ -z "${BACKUP_REMOTE:-}" ]; then
	echo "[backup] ERROR: BACKUP_REMOTE is not set — refusing to keep the only copy on the server it protects." >&2
	echo "[backup]        Set BACKUP_REMOTE=offsite:<bucket>[/<path>] and the BACKUP_S3_* values in deploy/.env." >&2
	exit 1
fi
case "${BACKUP_REMOTE}" in
	offsite:*) ;;
	*)
		echo "[backup] ERROR: BACKUP_REMOTE='${BACKUP_REMOTE}' must start with 'offsite:' — that is the remote the" >&2
		echo "[backup]        BACKUP_S3_* values configure (RCLONE_CONFIG_OFFSITE_* in docker-compose.prod.yml)." >&2
		exit 1
		;;
esac

# Asked BEFORE anything is dumped, so an unreachable remote costs a refusal at 02:00 and not a dump that has
# to be thrown away. Listing an absent prefix on S3 is an empty answer, not an error; a wrong key or a
# missing bucket is an error.
STAGE="remote"
if ! ${RCLONE} lsf --max-depth 1 "${BACKUP_REMOTE}" > /dev/null; then
	echo "[backup] ERROR: the off-site remote '${BACKUP_REMOTE}' cannot be listed — check BACKUP_S3_* in deploy/.env." >&2
	exit 1
fi

# `--preflight` (entrypoint.sh, at container start): the checks above and nothing else. A refusal is recorded in
# the status file exactly like a failed night — by the EXIT trap — so it is reported the moment the container
# starts; success writes nothing, so the last real run's outcome stands.
if [ "${1:-}" = "--preflight" ]; then
	echo "[backup] preflight: database hop encrypted, encryption key set, off-site remote '${BACKUP_REMOTE}' reachable"
	exit 0
fi

# The leftover of the 33 failed nights: the broken call made mirror-objects.sh write its MANIFEST at the
# MIRROR's path, so /backups/objects is an empty FILE where the mirror directory belongs. Only that exact
# shape — a regular file of zero bytes — is removed; anything else at that path is a refusal, never a guess.
STAGE="objects"
if [ -e "${OBJECTS_DIR}" ] && [ ! -d "${OBJECTS_DIR}" ]; then
	if [ -f "${OBJECTS_DIR}" ] && [ ! -s "${OBJECTS_DIR}" ]; then
		rm -f "${OBJECTS_DIR}"
		echo "[backup] removed the empty file left at ${OBJECTS_DIR} by the broken mirror call"
	else
		echo "[backup] ERROR: ${OBJECTS_DIR} exists and is not a directory — refusing to guess what it is." >&2
		exit 1
	fi
fi

mkdir -p "${WORK}"

# 1. PostgreSQL dump — custom format so it restores with `pg_restore` (connection via PG* env).
STAGE="dump"
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
#
# ⚠️ Real line continuations. This call shipped with literal `\n` between its arguments, so `sh` passed
# `n /minio-data n /backups/objects n` as the five: the mirror read an empty « store » called `n`, wrote its
# manifest over the mirror directory's path, and the `cp` below aborted every run. shellcheck flags the
# shape (SC1001); the CI `backup-roundtrip` job runs this script end to end.
/usr/local/bin/mirror-objects.sh \
	/minio-data \
	"${OBJECTS_DIR}" \
	"${ATTIC_DIR}/${TS}" \
	"${OBJECTS_MANIFEST}" \
	"${BACKUP_AGE_RECIPIENT}"

# The manifest travels with the run so a rebuilt server can tell what the mirror is supposed to contain.
cp "${OBJECTS_MANIFEST}" "${WORK}/objects-${TS}.manifest"

# 3. Stamp the key-ring generation this dump belongs to (FR-3.9).
#
# The API writes a marker file carrying key IDS ONLY (no key material) and this mounts it read-only. An absent
# marker stamps `unknown`, and check-keyring.sh refuses an unknown stamp: erring toward « I cannot prove this
# matches » is the safe direction, because the failure it guards against is silent (a restored practice whose
# every second factor is undecryptable).
STAGE="keyring"
STAMP_FILE="${WORK}/keyring-${TS}.txt"
if [ -r "${KEYRING_MARKER_FILE:-/keyring-marker/generation}" ]; then
	cp "${KEYRING_MARKER_FILE:-/keyring-marker/generation}" "${STAMP_FILE}"
	echo "[backup] key-ring stamp: $(head -n 1 "${STAMP_FILE}")"
else
	echo "active=unknown" > "${STAMP_FILE}"
	echo "[backup] WARNING: no key-ring marker readable — stamped 'unknown'; a restore will be REFUSED until" >&2
	echo "[backup]          the API has written one (FR-3.9). Check the keyring_marker volume mount." >&2
fi

# 3b. The key ring itself (FR-3.11).
#
# ⚠️ This reverses what this script used to say — « the ring is NEVER mounted here » — and KEY-CUSTODY.md
# § « What travels together » is why. That rule was written while the ring sat in cleartext. Since FR-3.1 the
# ring is ENCRYPTED by the key-ring certificate, so the ring is no longer the secret: the CERTIFICATE is, and
# the certificate is what must never be in this archive. Nothing backed the ring up in the meantime, so losing
# the server lost every second factor, every reminder credential and every Google token at once.
#
# The ACTIVE key — the one every new secret is written under — must be in what is archived, or this backup
# restores a ring that cannot read what it is restored beside. keyring-id.sh holds the guid ↔ marker-id rule.
# shellcheck source=keyring-id.sh
. /usr/local/bin/keyring-id.sh

RING_ARCHIVE=""
if [ -n "${KEYRING_DIR:-}" ]; then
	KEY_COUNT="$(find "${KEYRING_DIR}" -maxdepth 1 -type f -name 'key-*.xml' | wc -l | tr -d ' ')"
	if [ "${KEY_COUNT}" -lt 1 ]; then
		echo "[backup] ERROR: KEYRING_DIR=${KEYRING_DIR} holds no key-*.xml file — the ring did not mount." >&2
		exit 1
	fi

	ACTIVE_ID="$(grep '^active=' "${STAMP_FILE}" | head -n 1 | cut -d= -f2- | tr -d '[:space:]')"
	if [ -n "${ACTIVE_ID}" ] && [ "${ACTIVE_ID}" != "unknown" ] && ! ring_holds_key "${KEYRING_DIR}" "${ACTIVE_ID}"; then
		echo "[backup] ERROR: the active key ${ACTIVE_ID} named by the marker is not in ${KEYRING_DIR}." >&2
		echo "[backup]        Archiving this ring would restore secrets nothing can read (FR-3.9)." >&2
		exit 1
	fi

	# Key files from before FR-3.1 are still in CLEARTEXT until KEY-CUSTODY.md § « Rotating » step 4 removes
	# them; Data Protection marks each one with this comment. The archive is age-encrypted either way, so this
	# is said, not refused — refusing would stop every nightly backup over a cleanup step.
	CLEARTEXT_KEYS="$(grep -l 'the key below is in an unencrypted form' "${KEYRING_DIR}"/key-*.xml 2>/dev/null | wc -l | tr -d ' ')"
	if [ "${CLEARTEXT_KEYS}" -gt 0 ]; then
		echo "[backup] WARNING: ${CLEARTEXT_KEYS} key file(s) in the ring are still in cleartext (pre-FR-3.1) — they are" >&2
		echo "[backup]          archived under age only. Finish KEY-CUSTODY.md § « Rotating it » step 4 to remove them." >&2
	fi

	RING_ARCHIVE="${WORK}/keyring-ring-${TS}.tar"
	tar -cf "${RING_ARCHIVE}" -C "${KEYRING_DIR}" .
	echo "[backup] key ring: ${KEY_COUNT} key file(s) -> $(basename "${RING_ARCHIVE}")"
else
	echo "[backup] WARNING: KEYRING_DIR is not set — the key ring is NOT in this backup. A rebuilt server would" >&2
	echo "[backup]          lose every second factor, reminder credential and Google token (KEY-CUSTODY.md)." >&2
fi

# 4. Encrypt everything that leaves this host (FR-3.6).
#
# ⚠️ Only the dump, the manifest and the ring are encrypted HERE. The objects were encrypted one by one in
# step 2 — that is what makes the mirror incremental, since `age` output is nondeterministic and re-encrypting
# an unchanged object would produce different bytes every night and be re-uploaded every night.
STAGE="encrypt"
for PLAIN in "${DB_FILE}" "${WORK}/objects-${TS}.manifest" ${RING_ARCHIVE:+"${RING_ARCHIVE}"}; do
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
STAGE="verify"
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
#
# ⚠️ Nothing readable leaves: before anything is copied, the run directory must hold only `.age` files and the
# key-ring stamp (key ids, no key material). A plaintext file here is a bug upstream, and uploading it would be
# the one irreversible mistake this script can make.
STAGE="upload"
UNEXPECTED="$(find "${WORK}" -type f ! -name '*.age' ! -name "keyring-${TS}.txt")"
if [ -n "${UNEXPECTED}" ]; then
	echo "[backup] ERROR: refusing to upload — unencrypted file(s) in the run: ${UNEXPECTED}" >&2
	exit 1
fi

RETENTION="${BACKUP_RETENTION_DAYS:-14}"

# The dated run: the dump, the key-ring stamp, the ring and the manifest. Small, and one per night.
${RCLONE} copy "${WORK}" "${BACKUP_REMOTE}/${TS}"

# ⚠️ The objects go up with `sync`, not `copy`, and the difference is the whole point: `copy` never
# removes, so the remote would grow for ever and a deleted file would silently stay backed up. `sync`
# with `--backup-dir` MOVES what is no longer in the mirror into the same dated attic used locally,
# rather than deleting it — so a removal is still recoverable for the retention window.
#
# ⚠️ The attic must be OUTSIDE the destination or rclone refuses the run; `objects/` and `attic/` are
# siblings for exactly that reason.
${RCLONE} sync "${OBJECTS_DIR}" "${BACKUP_REMOTE}/objects" \
	--backup-dir "${BACKUP_REMOTE}/attic/${TS}"

# Age out the remote attic on the same window as the local one.
${RCLONE} delete "${BACKUP_REMOTE}/attic" --min-age "${RETENTION}d" || true
${RCLONE} rmdirs "${BACKUP_REMOTE}/attic" --leave-root || true

echo "[backup] uploaded off-server -> ${BACKUP_REMOTE}/${TS} + objects mirror"

# 7. Prune the dated run directories and the attic. The MIRROR itself is never pruned — it is the backup.
STAGE="prune"
find /backups -mindepth 1 -maxdepth 1 -type d -name '20*' -mtime "+${RETENTION}" -exec rm -rf {} + 2>/dev/null || true
find "${ATTIC_DIR}" -mindepth 1 -maxdepth 1 -type d -name '20*' -mtime "+${RETENTION}" -exec rm -rf {} + 2>/dev/null || true

# ⚠️ Plaintext dumps left by runs that died before step 4 (the 33 nights above left one each) are removed
# only HERE — after tonight's encrypted copy has reached the remote — so there is never a moment with fewer
# copies than before. Only `db-*.dump`, never `*.dump.age`; then the run directories that leaves empty.
find /backups -mindepth 2 -maxdepth 2 -path '/backups/20*/db-*.dump' -type f -delete 2>/dev/null || true
find /backups -mindepth 1 -maxdepth 1 -type d -name '20*' -empty -delete 2>/dev/null || true

write_status succeeded
echo "[backup] ${TS} done"
