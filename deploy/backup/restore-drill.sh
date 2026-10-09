#!/bin/sh
# Fetches one nightly run from the off-site remote, decrypts it, and PROVES it is whole — the first half of
# deploy/RESTORE-DRILL.md as a script, so the same steps run in CI on every change to deploy/backup and on a
# scratch host for the real drill. It never touches a running deployment: it only reads the remote and writes
# into --out.
#
#   restore-drill.sh --identity <age identity file> --out <empty dir> [--run <timestamp>|latest]
#
# Runs inside the backup image (rclone, age, pg_restore), with the same BACKUP_REMOTE / RCLONE_CONFIG_OFFSITE_*
# environment as the nightly job:
#
#   docker compose -f docker-compose.hosted.yml run --rm --no-deps --entrypoint /usr/local/bin/restore-drill.sh \
#     -v "$PWD/drill:/drill" -v "$PWD/backup-identity.txt:/identity:ro" backup --identity /identity --out /drill
#
# Leaves in --out:  db.dump · minio-data/ (the object volume, as MinIO lays it out) · keyring/ (the ring's
# key-*.xml) · keyring-<ts>.txt (the stamp) · objects.manifest. ⚠️ Every one of them is DECRYPTED patient data
# or key material: `shred` and remove the whole directory when the drill ends.
#
# Exit codes: 0 = the run is whole · 1 = could not run · 2 = the run is NOT whole — do not restore from it.
set -eu

IDENTITY=""
OUT=""
RUN="latest"
while [ $# -gt 0 ]; do
	case "$1" in
		--identity) IDENTITY="$2"; shift 2 ;;
		--out) OUT="$2"; shift 2 ;;
		--run) RUN="$2"; shift 2 ;;
		*) echo "Usage: restore-drill.sh --identity <file> --out <dir> [--run <timestamp>|latest]" >&2; exit 1 ;;
	esac
done

if [ -z "${IDENTITY}" ] || [ ! -r "${IDENTITY}" ]; then
	echo "[drill] ERROR: --identity must name a readable age identity file (the backup PRIVATE key)." >&2
	exit 1
fi
if [ -z "${OUT}" ]; then
	echo "[drill] ERROR: --out is required." >&2
	exit 1
fi
if [ -z "${BACKUP_REMOTE:-}" ]; then
	echo "[drill] ERROR: BACKUP_REMOTE is not set." >&2
	exit 1
fi
mkdir -p "${OUT}"
if [ -n "$(ls -A "${OUT}")" ]; then
	echo "[drill] ERROR: ${OUT} is not empty — refusing to mix two drills' decrypted data." >&2
	exit 1
fi

RCLONE="rclone --config /dev/null"
# shellcheck source=keyring-id.sh
. /usr/local/bin/keyring-id.sh

FAILURES=0
fail() { echo "[drill] ❌ $*" >&2; FAILURES=$((FAILURES + 1)); }
ok()   { echo "[drill] ✅ $*"; }

# ── 1. Which run ───────────────────────────────────────────────────────────────────────────────────────
if [ "${RUN}" = "latest" ]; then
	RUN="$(${RCLONE} lsf --dirs-only "${BACKUP_REMOTE}" | tr -d '/' | grep '^20' | sort | tail -n 1 || true)"
	if [ -z "${RUN}" ]; then
		echo "[drill] ERROR: no dated run found at ${BACKUP_REMOTE}." >&2
		exit 1
	fi
fi
echo "[drill] run ${RUN} from ${BACKUP_REMOTE}"

ENC="${OUT}/.encrypted"
mkdir -p "${ENC}/run" "${ENC}/objects"
${RCLONE} copy "${BACKUP_REMOTE}/${RUN}" "${ENC}/run"
${RCLONE} copy "${BACKUP_REMOTE}/objects" "${ENC}/objects"

# ── 2. The database ────────────────────────────────────────────────────────────────────────────────────
#
# ⚠️ « It decrypts » is not the check — a truncated dump decrypts perfectly. A NON-EMPTY `pg_restore --list` is
# what proves the archive's own table of contents is readable (pass condition 1).
if age --decrypt --identity "${IDENTITY}" --output "${OUT}/db.dump" "${ENC}/run/db-${RUN}.dump.age"; then
	TOC="$(pg_restore --list "${OUT}/db.dump" 2>/dev/null | grep -c ';' || true)"
	if [ "${TOC}" -ge 1 ]; then
		ok "database dump decrypts and parses (${TOC} archive entries)"
	else
		fail "database dump decrypts but does not parse as a pg_restore archive"
	fi
else
	fail "database dump does not decrypt with this identity"
fi

# ── 3. The objects, against the manifest that travelled with the dump ──────────────────────────────────
#
# ⚠️ Size AND path, line by line — not only a count. A short or truncated mirror restores a practice whose
# records are all present and whose radiographs are silently missing or broken.
if age --decrypt --identity "${IDENTITY}" --output "${OUT}/objects.manifest" "${ENC}/run/objects-${RUN}.manifest.age"; then
	mkdir -p "${OUT}/minio-data"
	DECRYPT_FAILED=0
	( cd "${ENC}/objects" && find . -type f -name '*.age' ) | while IFS= read -r REL_ENC; do
		REL="${REL_ENC#./}"
		REL="${REL%.age}"
		mkdir -p "${OUT}/minio-data/$(dirname "${REL}")"
		age --decrypt --identity "${IDENTITY}" --output "${OUT}/minio-data/${REL}" "${ENC}/objects/${REL_ENC#./}" || echo "${REL}" >> "${OUT}/.decrypt-failures"
	done
	if [ -s "${OUT}/.decrypt-failures" ]; then
		DECRYPT_FAILED="$(wc -l < "${OUT}/.decrypt-failures" | tr -d ' ')"
		fail "${DECRYPT_FAILED} object(s) did not decrypt (listed in ${OUT}/.decrypt-failures)"
	fi

	TAB="$(printf '\t')"
	MISSING=0
	WRONG_SIZE=0
	# ⚠️ Size is checked on the stored OBJECTS only. They are immutable once written; MinIO's own `.minio.sys`
	# metadata is rewritten on its own, so its size may legitimately move between the listing and the copy.
	while IFS="${TAB}" read -r SIZE _MTIME REL; do
		[ -n "${REL}" ] || continue
		FILE="${OUT}/minio-data/${REL#./}"
		if [ ! -f "${FILE}" ]; then
			MISSING=$((MISSING + 1))
		elif [ "${REL#./.minio.sys/}" = "${REL}" ] && [ "$(stat -c %s "${FILE}")" != "${SIZE}" ]; then
			WRONG_SIZE=$((WRONG_SIZE + 1))
		fi
	done < "${OUT}/objects.manifest"
	EXPECTED="$(grep -c . "${OUT}/objects.manifest" || true)"
	RESTORED="$(find "${OUT}/minio-data" -type f | wc -l | tr -d ' ')"
	if [ "${MISSING}" -eq 0 ] && [ "${WRONG_SIZE}" -eq 0 ]; then
		ok "objects: ${RESTORED} restored, all ${EXPECTED} in the manifest present at their recorded size"
	else
		fail "objects: ${MISSING} missing and ${WRONG_SIZE} at the wrong size, of ${EXPECTED} in the manifest — a file deleted since this run is in attic/<night>/ (RESTORE-DRILL.md)"
	fi
else
	fail "object manifest does not decrypt with this identity"
fi

# ── 4. The key ring, against the stamp ─────────────────────────────────────────────────────────────────
#
# ⚠️ The restored ring must hold the key this dump's secrets were written under, or the restore produces a
# practice whose second factors and reminder credentials are ALL silently undecryptable (FR-3.9).
cp "${ENC}/run/keyring-${RUN}.txt" "${OUT}/keyring-${RUN}.txt"
ACTIVE_ID="$(grep '^active=' "${OUT}/keyring-${RUN}.txt" | head -n 1 | cut -d= -f2- | tr -d '[:space:]')"
if [ -f "${ENC}/run/keyring-ring-${RUN}.tar.age" ]; then
	if age --decrypt --identity "${IDENTITY}" --output "${OUT}/.ring.tar" "${ENC}/run/keyring-ring-${RUN}.tar.age"; then
		mkdir -p "${OUT}/keyring"
		tar -xf "${OUT}/.ring.tar" -C "${OUT}/keyring"
		rm -f "${OUT}/.ring.tar"
		KEYS="$(find "${OUT}/keyring" -maxdepth 1 -type f -name 'key-*.xml' | wc -l | tr -d ' ')"
		if [ -z "${ACTIVE_ID}" ] || [ "${ACTIVE_ID}" = "unknown" ]; then
			fail "key ring: ${KEYS} key(s), but the stamp is '${ACTIVE_ID:-empty}' — nothing proves they match"
		elif ring_holds_key "${OUT}/keyring" "${ACTIVE_ID}"; then
			ok "key ring: ${KEYS} key(s), including the active key ${ACTIVE_ID} the stamp names"
		else
			fail "key ring: ${KEYS} key(s), but NOT the active key ${ACTIVE_ID} the stamp names"
		fi
	else
		fail "key ring does not decrypt with this identity"
	fi
else
	fail "key ring: this run carries no keyring-ring-${RUN}.tar.age — a rebuilt server would lose every second factor"
fi

rm -rf "${ENC}"

echo "[drill] ⚠️ ${OUT} now holds DECRYPTED patient data and key material — shred it when the drill ends."
if [ "${FAILURES}" -gt 0 ]; then
	echo "[drill] ${FAILURES} check(s) FAILED — do NOT restore from run ${RUN}." >&2
	exit 2
fi
echo "[drill] run ${RUN} is whole. Next: RESTORE-DRILL.md step 3 (load it into an isolated stack)."
