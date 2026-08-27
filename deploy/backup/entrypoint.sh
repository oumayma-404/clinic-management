#!/bin/sh
# Installs the nightly cron schedule and runs busybox crond in the foreground so the
# container stays up. Backup output is routed to PID 1's stdout => visible in `docker logs`.
set -eu

CRON_EXPR="${BACKUP_CRON:-0 2 * * *}"
echo "${CRON_EXPR} /usr/local/bin/backup.sh > /proc/1/fd/1 2>&1" > /etc/crontabs/root

echo "[backup] scheduled: '${CRON_EXPR}' (remote='${BACKUP_REMOTE:-<none>}', retention=${BACKUP_RETENTION_DAYS:-14}d)"
echo "[backup] on-demand: docker compose -f docker-compose.prod.yml run --rm backup /usr/local/bin/backup.sh"

exec crond -f -l 8
