#!/bin/sh
# Installs the nightly cron schedule and runs busybox crond in the foreground so the
# container stays up. Backup output is routed to PID 1's stdout => visible in `docker logs`.
set -eu

# ⚠️ The preflight runs HERE, at container start, and not only at 02:00: a wrong key, a missing bucket or an
# unencrypted database hop is written to the status file (outcome=failed, with its stage) the moment the
# container starts, and the API reports it from there (server-loss-recovery Part 3).
#
# ⚠️ And the container stays up regardless. Refusing to start looked like the louder choice, but
# `deploy-hosted.yml` brings the stack up with `--wait`, so a sidecar restarting in a loop would fail EVERY deploy
# — app releases included — over a backup setting. The status file is the loud channel; crond still retries
# every night, so fixing `.env` and restarting this one container is the whole repair.
/usr/local/bin/backup.sh --preflight || echo "[backup] preflight FAILED — recorded in the status file; the nightly run will retry" >&2

CRON_EXPR="${BACKUP_CRON:-0 2 * * *}"
echo "${CRON_EXPR} /usr/local/bin/backup.sh > /proc/1/fd/1 2>&1" > /etc/crontabs/root

echo "[backup] scheduled: '${CRON_EXPR}' (remote='${BACKUP_REMOTE:-<none>}', retention=${BACKUP_RETENTION_DAYS:-14}d)"
echo "[backup] on-demand: docker compose -f docker-compose.hosted.yml exec backup /usr/local/bin/backup.sh"

exec crond -f -l 8
