#!/bin/sh
# Postgres's archive_command, with the encryption key checked FIRST.
#
# ⚠️ Why this exists as a script rather than as `archive_command=wal-g wal-push %p`.
#
# WAL-G encrypts what it ships only when WALG_LIBSODIUM_KEY is set. With the key absent it does not fail — it
# uploads the segment IN CLEAR, and every layer of the deployment reports success. A WAL segment carries every
# write to every patient record, so that is the whole practice's clinical history leaving the building
# unencrypted, continuously, with nothing to see.
#
# The refusal for exactly this condition already existed — in `pitr-entrypoint.sh`, which guards the *sidecar*
# that takes base backups. But the WAL is pushed by the `postgres` service's own archive_command, in a different
# container, and that side had no guard at all. So a missing key crash-looped the sidecar (loud, and it looks
# like « backups are broken ») while the far more sensitive half kept flowing in cleartext (silent). The compose
# file's own comment states the risk in those words and no check was ever written for it.
#
# Failing here is the correct behaviour and is NOT data loss: Postgres keeps the segment in pg_wal/ and retries
# the archive_command indefinitely, so nothing is discarded — the archive simply stops advancing, which is
# visible in pg_stat_archiver and eventually as disk pressure. That is a loud, recoverable failure; shipping a
# practice's records unencrypted is a quiet, unrecoverable one.
#
# ⚠️ Keep this in step with `pitr-entrypoint.sh`: both accept WALG_LIBSODIUM_KEY_PATH as the file-backed form,
# because that is the direction `follow-up/hosted-secrets-to-files.md` moves the deployment's secrets in.
set -eu

if [ -z "${WALG_LIBSODIUM_KEY:-}" ] && [ -z "${WALG_LIBSODIUM_KEY_PATH:-}" ]; then
	echo "[wal-g] ERREUR : ni WALG_LIBSODIUM_KEY ni WALG_LIBSODIUM_KEY_PATH n'est définie — refus d'archiver" >&2
	echo "[wal-g]          un segment WAL en clair. Un segment contient chaque écriture de chaque dossier" >&2
	echo "[wal-g]          patient (FR-3.6). Le segment reste dans pg_wal/ et sera réessayé ; rien n'est perdu." >&2
	echo "[wal-g]          Voir deploy/KEY-CUSTODY.md ; ⚠️ perdre cette clé rend illisible toute sauvegarde déjà prise." >&2
	exit 1
fi

exec wal-g wal-push "$1"
