#!/bin/sh
# Entrypoint for the pitr sidecar: schedules periodic physical base backups with supercronic.
#
# On first boot, if the off-site prefix has no base backup yet, it takes one IMMEDIATELY so PITR
# is usable without manual steps (AC-2) and WAL has a base to anchor to (edge case: WAL without a
# base backup can't restore). Then it hands off to supercronic (PID 1) which fires the base
# backup on PITR_BASE_BACKUP_CRON and logs every run to stdout (visible in `docker logs`).
set -eu

CRON_EXPR="${PITR_BASE_BACKUP_CRON:-0 1 * * *}"

# The continuous stream leaves encrypted, or it does not leave (hosted-security-hardening FR-3.6).
#
# ⚠️ This is the OTHER copy of the database, and the easier one to forget: the nightly dump is something an
# operator thinks about, while WAL segments ship every few minutes, for ever, unattended. Unencrypted they are
# a rolling, complete copy of every practice's records on somebody else's storage.
#
# ⚠️ Refusing here rather than warning, for backup.sh's reason: a warning nobody reads means the stream keeps
# flowing in the clear while every log line says the sidecar is healthy. WAL-G reads WALG_LIBSODIUM_KEY itself
# and encrypts both the WAL segments and the base backups with it.
#
# ⚠️ If this key is lost, every base backup and every WAL segment taken with it is unrecoverable — the same
# statement KEY-CUSTODY.md makes about the age key, and for the same reason.
if [ -z "${WALG_LIBSODIUM_KEY:-}" ] && [ -z "${WALG_LIBSODIUM_KEY_PATH:-}" ]; then
	echo "[pitr] ERROR: neither WALG_LIBSODIUM_KEY nor WALG_LIBSODIUM_KEY_PATH is set — refusing to ship an" >&2
	echo "[pitr]        unencrypted copy of every practice's records off-site (FR-3.6). See" >&2
	echo "[pitr]        deploy/KEY-CUSTODY.md; ⚠️ losing this key makes every archived backup unrecoverable." >&2
	exit 1
fi

echo "[pitr] scheduler starting: cron='${CRON_EXPR}', retain=${PITR_RETAIN_BASE_BACKUPS:-7}, prefix='${WALG_S3_PREFIX}', encrypted=yes"

# First-boot bootstrap. `backup-list` returns non-zero / empty on a fresh prefix; either way,
# no line beginning with `base_` means there is no base backup yet.
if wal-g backup-list 2>/dev/null | grep -q '^base_'; then
	echo "[pitr] existing base backup(s) found — skipping initial backup"
else
	echo "[pitr] no base backup found — taking initial base backup now"
	/usr/local/bin/pitr-backup.sh
fi

# Install the crontab and hand off to supercronic in the foreground.
#
# ⚠️ **The ABSOLUTE path is load-bearing.** `exec supercronic` resolves through `PATH`, so the process starts with
# `argv[0]` = `supercronic` — and supercronic, seeing it is PID 1, re-execs `os.Args[0]` to install its process
# reaper WITHOUT a PATH lookup. That fork exec gets ENOENT and the sidecar dies with
# « Failed to fork exec: no such file or directory », one second after printing that it was handing off.
#
# It then restarts, prints the same three healthy-looking lines, and dies again — for ever. Observed on the
# hosted VPS: `docker ps` says `Restarting`, the log's last full cycle says « existing base backup(s) found »
# and « handing off to supercronic », and NO scheduled base backup had ever been taken. WAL kept shipping the
# whole time (that is postgres's own `archive_command`, in another container), so the off-site prefix looked
# alive while the base backups every one of those segments has to anchor to had stopped at the first one.
echo "${CRON_EXPR} /usr/local/bin/pitr-backup.sh" > /etc/pitr.cron
echo "[pitr] handing off to supercronic"
exec /usr/local/bin/supercronic /etc/pitr.cron
