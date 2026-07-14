#!/bin/sh
# Entrypoint for the pitr sidecar: schedules periodic physical base backups with supercronic.
#
# On first boot, if the off-site prefix has no base backup yet, it takes one IMMEDIATELY so PITR
# is usable without manual steps (AC-2) and WAL has a base to anchor to (edge case: WAL without a
# base backup can't restore). Then it hands off to supercronic (PID 1) which fires the base
# backup on PITR_BASE_BACKUP_CRON and logs every run to stdout (visible in `docker logs`).
set -eu

CRON_EXPR="${PITR_BASE_BACKUP_CRON:-0 1 * * *}"

echo "[pitr] scheduler starting: cron='${CRON_EXPR}', retain=${PITR_RETAIN_BASE_BACKUPS:-7}, prefix='${WALG_S3_PREFIX}'"

# First-boot bootstrap. `backup-list` returns non-zero / empty on a fresh prefix; either way,
# no line beginning with `base_` means there is no base backup yet.
if wal-g backup-list 2>/dev/null | grep -q '^base_'; then
	echo "[pitr] existing base backup(s) found — skipping initial backup"
else
	echo "[pitr] no base backup found — taking initial base backup now"
	/usr/local/bin/pitr-backup.sh
fi

# Install the crontab and hand off to supercronic in the foreground.
echo "${CRON_EXPR} /usr/local/bin/pitr-backup.sh" > /etc/pitr.cron
echo "[pitr] handing off to supercronic"
exec supercronic /etc/pitr.cron
