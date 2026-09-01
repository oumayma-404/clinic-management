#!/usr/bin/env bash
# Does every running container read the configuration that is actually on disk?
#
# ⚠️ **A single-FILE bind mount pins an inode.** Shipping `deploy/` REPLACES files rather than editing them in
# place, so the inode changes and a container started before the change keeps reading the old one — forever, and
# with nothing anywhere saying so. `docker compose up -d` does not help: Compose recreates a container when its
# *service definition* changes, and « the file behind an unchanged mount now has different bytes » is not part of
# that definition. Worse, the tools that look like they would catch it do not: `caddy validate` and `caddy reload`
# both read the path the container sees, so both report success against the stale content.
#
# That is not hypothetical. A CSP fix shipped, the Caddyfile on disk carried the new token twice, the container
# saw it zero times, and the browser kept refusing the coffre's hasher for a day.
#
# The mount list is DERIVED from `docker inspect`, never written down here: a config file bind-mounted next month
# is covered the day it is added, which a hand-kept list would not be. Directories are skipped on purpose — a
# directory's inode is stable and its contents are read through live, so they are immune to this whole class.
#
#   config-freshness.sh              report, and exit 1 when anything is stale
#   config-freshness.sh --services   print just the stale compose service names, one per line (for `up -d`)
#
# Exit: 0 everything fresh · 1 at least one container is stale.

set -uo pipefail

services_only=0
[ "${1:-}" = "--services" ] && services_only=1

# Diagnostics go to stderr so `--services` output stays pipeable into `docker compose up -d`.
note() { if [ "$services_only" -eq 1 ]; then echo "$*" >&2; else echo "$*"; fi; }

stale_services=""
checked=0
unreadable=0
opaque=0

for id in $(docker ps -q); do
  service=$(docker inspect "$id" --format '{{index .Config.Labels "com.docker.compose.service"}}')
  name=$(docker inspect "$id" --format '{{.Name}}' | tr -d '/')
  [ -n "$service" ] || service="$name"

  while IFS='|' read -r src dst; do
    [ -n "$src" ] || continue
    [ -f "$src" ] || continue          # a directory mount, or gone — neither is this trap

    host=$(sha256sum "$src" 2>/dev/null | cut -d' ' -f1)
    if [ -z "$host" ]; then
      # Unreadable to whoever runs this — the API's secret files are owned by uid 1654 and the deploy
      # account has no sudo. They are also excluded from the shipped archive, so they never go stale this
      # way; counted rather than ignored so the number below is never mistaken for full coverage.
      unreadable=$((unreadable + 1))
      continue
    fi

    seen=$(docker exec -u 0 "$id" sha256sum "$dst" 2>/dev/null | cut -d' ' -f1)
    if [ -z "$seen" ]; then
      opaque=$((opaque + 1))           # no sha256sum in that image: skip rather than guess
      continue
    fi

    checked=$((checked + 1))
    if [ "$host" != "$seen" ]; then
      note "STALE  service '$service' ($name) is reading an old $dst"
      note "       on disk: $src"
      case " $stale_services " in
        *" $service "*) ;;
        *) stale_services="$stale_services $service" ;;
      esac
    fi
  done < <(docker inspect "$id" --format '{{range .Mounts}}{{if eq .Type "bind"}}{{.Source}}|{{.Destination}}{{println}}{{end}}{{end}}')
done

stale_services=$(echo "$stale_services" | xargs || true)

note "compared $checked single-file bind mount(s); $unreadable unreadable here, $opaque in images without sha256sum"

# ⚠️ `--services` is a LISTING, and it exits 0 whether or not it found something. Finding stale services is its
# job, not a failure of it — the caller recreates them and then runs this script again in report mode, and *that*
# run is the verdict. An exit code here would make the caller's `set -e` treat a successful detection as a broken
# deploy, and the natural workaround (`|| true`) would swallow a genuine crash of this script along with it.
if [ "$services_only" -eq 1 ]; then
  [ -n "$stale_services" ] && echo "$stale_services" | tr ' ' '\n'
  exit 0
fi

if [ -n "$stale_services" ]; then
  echo "At least one container is serving configuration that is no longer on disk."
  echo "Recreate it:  docker compose -f docker-compose.hosted.yml -f docker-compose.registry.yml \\"
  echo "                up -d --no-build --force-recreate --no-deps $stale_services"
  exit 1
fi

echo "Every container reads the configuration that is on disk."
