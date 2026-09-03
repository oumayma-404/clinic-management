#!/bin/sh
# The object store, backed up INCREMENTALLY: one `age` file per stored object, refreshed only when the
# object itself changed, and removals moved into a dated attic instead of vanishing.
#
# ⚠️ WHY THIS EXISTS, in one number. It replaced `tar czf` of the whole volume, taken fresh every night,
# `age`-encrypted (so nothing dedupes or compresses between nights) and kept BACKUP_RETENTION_DAYS — 14 — of
# them on the same disk. Every hosted megabyte therefore cost about FIFTEEN on the server, plus one more
# off-site every night for ever. Measured on the live VPS on 2026-09-03: 96 Go of disk, 78 Go free, which at
# 15x is about 5 Go of objects before it fills — and a full disk does not degrade a cabinet, it stops every
# cabinet on the box at once. That multiplier, not the uplink, is what had the coffre threshold pinned at
# 25 Mo (`FileTypeCatalog.StudyStaysAtTheCabinetAbove`).
#
# ⚠️ Objects here are IMMUTABLE ONCE WRITTEN — the app creates and deletes blobs and never edits one — which
# is the property that makes an incremental mirror correct rather than merely cheaper. A nightly run now
# costs what actually changed.
#
# ⚠️ Per-object `age` and NOT an rclone `crypt` remote. That was considered and rejected where backup.sh
# verifies its own output: crypt puts the encryption in a gitignored config file, invisible to review and
# unverifiable without a round trip nobody would automate. This keeps one key, the one KEY-CUSTODY.md
# documents.
#
# Usage: mirror-objects.sh <source-dir> <mirror-dir> <attic-dir> <manifest> <age-recipient>
set -eu

SRC="${1:?source dir}"
MIRROR="${2:?mirror dir}"
ATTIC="${3:?attic dir}"
MANIFEST="${4:?manifest path}"
RECIPIENT="${5:?age recipient}"

TAB="$(printf '\t')"
WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

CURRENT="${WORK}/current"
PREVIOUS="${WORK}/previous"

# The state of the source right now: size, mtime and path, one line per file.
#
# ⚠️ Every file, `.minio.sys` included — this mirror has to be able to reconstitute the VOLUME, not just the
# blobs in it, exactly as the tar could. The metadata files are small and change often; the object data is
# large and never changes, which is the whole shape this exploits.
#
# ⚠️ Size AND mtime, compared as one line. Content hashing every object would re-read the entire store every
# night, which is the cost being removed. A changed file whose size and mtime both survived unchanged would
# be missed — for an immutable store that cannot arise, and a stale-but-present object is a far better
# failure than the nightly full copy this replaces.
( cd "${SRC}" && find . -type f -exec stat -c "%s${TAB}%Y${TAB}%n" {} + ) 2>/dev/null | sort > "${CURRENT}" || true

if [ -r "${MANIFEST}" ]; then
	sort "${MANIFEST}" > "${PREVIOUS}"
else
	: > "${PREVIOUS}"
	echo "[objects] no manifest yet — this run encrypts every object once"
fi

# ⚠️ **A store that reads as empty is refused, and this is the guard that matters most here.**
#
# The dangerous failure is not a crash — it is a volume that did not mount, because an unmounted path is an
# ordinary EMPTY DIRECTORY and not an error. Every tracked object would then look deleted, the whole mirror
# would move into tonight's attic, and BACKUP_RETENTION_DAYS later the attic would age out and take the only
# copy of every practice's imaging with it. Two clean nights is all that would take.
#
# The old nightly `tar` could not fail this way: it would simply have archived nothing, and yesterday's
# fourteen archives would still have been sitting there. An incremental mirror has no such cushion, so it
# has to refuse explicitly — and refusing is safe, since the previous manifest is left untouched and the run
# retries tomorrow.
#
# BACKUP_ALLOW_EMPTY_STORE=1 is the deliberate override for the one legitimate case: a deployment whose
# every file really has been removed.
if [ ! -s "${CURRENT}" ] && [ -s "${PREVIOUS}" ]; then
	if [ "${BACKUP_ALLOW_EMPTY_STORE:-0}" != "1" ]; then
		echo "[objects] ERROR: ${SRC} holds no files, but $(wc -l < "${PREVIOUS}" | tr -d ' ') were backed up" >&2
		echo "[objects]        last run. Refusing: this is what an unmounted volume looks like, and treating" >&2
		echo "[objects]        it as « everything was deleted » would move the entire mirror into the attic" >&2
		echo "[objects]        and lose it when the attic ages out." >&2
		echo "[objects]        Check the ${SRC} mount. If every object really was deleted, re-run with" >&2
		echo "[objects]        BACKUP_ALLOW_EMPTY_STORE=1." >&2
		exit 1
	fi
	echo "[objects] WARNING: the store is empty and BACKUP_ALLOW_EMPTY_STORE=1 — moving the whole mirror" >&2
	echo "[objects]          into the attic, where it survives only BACKUP_RETENTION_DAYS." >&2
fi

mkdir -p "${MIRROR}"

# ── What is new or changed ─────────────────────────────────────────────────────────────────────────────
#
# `comm -13` is « in CURRENT and not in PREVIOUS », over whole lines, so a file whose size or mtime moved
# counts as changed and is re-encrypted.
ADDED=0
comm -13 "${PREVIOUS}" "${CURRENT}" | cut -f3- > "${WORK}/added"
while IFS= read -r REL; do
	[ -n "${REL}" ] || continue
	DEST="${MIRROR}/${REL}.age"
	mkdir -p "$(dirname "${DEST}")"
	# Written to a temp name and moved into place, so an interrupted run never leaves a half-written
	# ciphertext that the next one would accept as complete.
	age --encrypt --recipient "${RECIPIENT}" --output "${DEST}.partial" "${SRC}/${REL}"
	if [ ! -s "${DEST}.partial" ]; then
		echo "[objects] ERROR: age produced an empty file for ${REL} — aborting" >&2
		rm -f "${DEST}.partial"
		exit 1
	fi
	mv "${DEST}.partial" "${DEST}"
	ADDED=$((ADDED + 1))
done < "${WORK}/added"

# ── What has gone ──────────────────────────────────────────────────────────────────────────────────────
#
# ⚠️ MOVED to a dated attic, never deleted. The tar it replaces kept fourteen days of history for free, so
# a file deleted by mistake last Tuesday could be recovered; a plain mirror would lose that the same night.
# The attic costs only what was actually removed.
REMOVED=0
cut -f3- "${PREVIOUS}" | sort > "${WORK}/previous-paths"
cut -f3- "${CURRENT}"  | sort > "${WORK}/current-paths"
comm -23 "${WORK}/previous-paths" "${WORK}/current-paths" > "${WORK}/removed"
while IFS= read -r REL; do
	[ -n "${REL}" ] || continue
	GONE="${MIRROR}/${REL}.age"
	[ -f "${GONE}" ] || continue
	KEEP="${ATTIC}/${REL}.age"
	mkdir -p "$(dirname "${KEEP}")"
	mv "${GONE}" "${KEEP}"
	REMOVED=$((REMOVED + 1))
done < "${WORK}/removed"

# Empty directories left behind by a move would accumulate for ever in a store keyed by clinic and file id.
find "${MIRROR}" -type d -empty -delete 2>/dev/null || true

# ── Only now is the manifest advanced ──────────────────────────────────────────────────────────────────
#
# ⚠️ Last, and only on success. `set -e` aborts above on any failure, which leaves the OLD manifest in
# place, so the next run redoes exactly the work that did not land. Advancing it earlier would record work
# as done that was not, and the objects it named would never be encrypted again.
mkdir -p "$(dirname "${MANIFEST}")"
cp "${CURRENT}" "${MANIFEST}"

TOTAL="$(wc -l < "${CURRENT}" | tr -d ' ')"
echo "[objects] ${TOTAL} objects tracked: ${ADDED} encrypted this run, ${REMOVED} moved to the attic"
