#!/usr/bin/env bash
# relay-roundtrip — clinic-pc-copy D26, FR-10: a cloud and its PC de secours, the cabinet's network cut, work on both
# sides, the network back. Asserts the manual takeover (the cloud stays writable on silence, the PC waits for
# « Travailler sur le PC de secours », then takes the saves), note numbers continuous, both copies equal, and
# `reconcile-money` exit 0 on both. Then two endings a cut can have besides the return (review findings 9, 10):
#   · the PC retired while it holds the cabinet's saves — the cloud takes them back first and the PC keeps the cut's
#     work (erase, uninstall and re-pairing all refuse);
#   · a second PC, then the cloud restored from a backup taken before that PC was paired — the PC stops and keeps its
#     copy (same three refusals).
#
# Everything runs in containers built from api/Dockerfile: the cloud (HostedMultiTenant) on networks cloudnet +
# cabinet, the PC (ClinicRelay) on cabinet only, each with its own Postgres; MinIO beside the cloud. The cut is
# `docker network disconnect cabinet cloud` — the PC loses the cloud, its own box (the network's gateway) still
# answers, which is exactly an internet cut seen from a cabinet.
#
#   e2e/relay-roundtrip/run.sh                 build the image, run, tear down
#   RT_SKIP_BUILD=1 RT_KEEP=1 …/run.sh         reuse the image, leave the containers up afterwards
#   RT_PREFIX=rtb RT_STOP_AFTER=paired RT_PUBLISH=5098 RT_FRONTEND_URL=http://localhost:3099 RT_KEEP=1 …/run.sh
#                                              a second, separate stack stopped once its PC is paired, the cloud
#                                              published on 127.0.0.1:5098 — for a browser pass on the relay card
#
# Needs: docker, node ≥ 20, bash. No port is published: the steps that talk HTTP run in a small node container on
# both networks (Docker Desktop cannot forward a published port to a container on two networks — measured).
set -euo pipefail
export MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*'
exec 3>&1 # the run's own output, still reachable from inside a check whose output is silenced

HERE=$(cd "$(dirname "$0")" && (pwd -W 2>/dev/null || pwd))
ROOT=$(cd "$HERE/../.." && (pwd -W 2>/dev/null || pwd))
P=${RT_PREFIX:-rt}
OUT_NAME=$([ "$P" = rt ] && echo .out || echo ".out-$P")
OUT=$HERE/$OUT_NAME
OUT_IN=/e2e/relay-roundtrip/$OUT_NAME # the same folder, as the driver container sees it
mkdir -p "$OUT"
rm -f "$OUT"/*.json "$OUT"/*.log "$OUT"/*.txt "$OUT"/block-handback
IMAGE=${RT_IMAGE:-clinic-api-relay-rt}
EMAIL=rt.admin@cabinet-roundtrip.tn

export RT_STATE="$OUT/state.json"

step() { echo; echo "── $* ($(date -u +%H:%M:%S))"; }
fail() { echo "::error title=relay-roundtrip::$*"; exit 1; }

teardown() {
  docker rm -f $P-cloud $P-pc $P-pg-cloud $P-pg-pc $P-minio $P-driver $P-link $P-pc2 $P-pg-pc2 $P-link2 $P-publish \
    >/dev/null 2>&1 || true
  docker network rm $P-cloudnet $P-cabinet >/dev/null 2>&1 || true
  docker volume rm $P-pc-local $P-pc-files $P-cloud-local $P-pc2-local $P-pc2-files >/dev/null 2>&1 || true
}
cleanup() {
  docker logs $P-cloud > "$OUT/cloud.log" 2>&1 || true
  docker logs $P-pc > "$OUT/pc.log" 2>&1 || true
  docker logs $P-pc2 > "$OUT/pc2.log" 2>&1 || true
  [ -n "${RT_KEEP:-}" ] || teardown
}
trap cleanup EXIT

# The HTTP half (drive.mjs, bootstrap.mjs) runs in $P-driver, which sits on both networks and is never cut.
node_in() { # node_in <script under e2e/> <args…>
  docker exec -e CLOUD_API=http://$P-cloud:5000/api -e PC_API=https://$P-pc:5096/api \
    -e RT_STATE=$OUT_IN/state.json -e NODE_TLS_REJECT_UNAUTHORIZED=0 -e NODE_NO_WARNINGS=1 \
    -e CLINIC_EMAIL="${CLINIC_EMAIL:-}" -e CLINIC_PASSWORD="${CLINIC_PASSWORD:-}" -e CLINIC_TOTP_SECRET="${CLINIC_TOTP_SECRET:-}" \
    $P-driver node "/e2e/$@"
}
drive() { node_in relay-roundtrip/drive.mjs "$@"; }

# The PC reaches its cloud at http://127.0.0.1:5000: `pair-relay` accepts plain HTTP only on loopback (a real cloud
# is HTTPS). A socat sidecar sharing the PC's network namespace carries that port to the cloud — on the cabinet's
# side of the cut, so `docker network disconnect` cuts it like any other path. Re-created whenever the PC restarts.
pc_link() { # pc_link [pc container] [link container]
  local pc=${1:-$P-pc} link=${2:-$P-link}
  docker rm -f $link >/dev/null 2>&1 || true
  docker run -d --name $link --network container:$pc alpine/socat:1.8.0.1 \
    TCP-LISTEN:5000,fork,reuseaddr TCP:$P-cloud:5000 >/dev/null
}
# The same road through proxy.mjs, which fails the PC's return while $OUT/block-handback exists.
pc_proxy() {
  docker rm -f $P-link >/dev/null 2>&1 || true
  docker run -d --name $P-link --network container:$P-pc -v "$ROOT/e2e:/e2e" node:22-bookworm-slim \
    node /e2e/relay-roundtrip/proxy.mjs 5000 http://$P-cloud:5000 $OUT_IN/block-handback >/dev/null
}

sql() { # sql <cloud|pc> "<query>" — one value, unaligned
  docker exec $P-pg-$1 psql -U clinic -d "$1" -tAc "$2"
}

wait_for() { # wait_for <seconds> <description> <command…>
  local seconds=$1 what=$2; shift 2
  local until=$(( $(date +%s) + seconds ))
  while [ "$(date +%s)" -lt "$until" ]; do
    if "$@" >/dev/null 2>&1; then echo "  ✓ $what"; return 0; fi
    sleep 3
  done
  fail "timed out after ${seconds}s waiting for: $what"
}

# The copy's identity in a handful of tables a cut touches: patients, fiches, notes (number, status), payments,
# dépenses. Each side computes it over its own rows; equal strings = equal copies of what the run wrote.
DIGEST='SELECT md5(string_agg(x, '"'"'|'"'"' ORDER BY x)) FROM (
  SELECT '"'"'P'"'"'||"Id" FROM "Patients"
  UNION ALL SELECT '"'"'D'"'"'||"Id" FROM "DentalRecords"
  UNION ALL SELECT '"'"'I'"'"'||"Id"||'"'"':'"'"'||coalesce("Number",'"'"''"'"')||'"'"':'"'"'||"Status" FROM "Invoices"
  UNION ALL SELECT '"'"'Y'"'"'||"Id"||'"'"':'"'"'||"Amount"||'"'"':'"'"'||"IsVoided" FROM "Payments"
  UNION ALL SELECT '"'"'E'"'"'||"Id"||'"'"':'"'"'||"Amount" FROM "Expenses") t(x)'
digests_equal() { [ "$(sql cloud "$DIGEST")" = "$(sql ${1:-pc} "$DIGEST")" ]; }
pc_caught_up() {
  [ "$(sql cloud 'SELECT (r."AppliedSeq" >= c."LastSeq")::text FROM "ClinicRelays" r JOIN "ClinicChangeCursors" c USING ("ClinicId") WHERE r."Status" = 2')" = "true" ]
}
pc_armed() { [ "$(sql cloud 'SELECT "ConfirmedAckArmed"::text FROM "ClinicRelays" WHERE "Status" = 2')" = "true" ]; }
lease() { docker exec ${1:-$P-pc} cat /app/.local/relay-lease.json 2>/dev/null; }
pc_state() { docker exec ${1:-$P-pc} cat /app/.local/relay-state.json 2>/dev/null; }
pc_holding() { lease | grep -q '"holdingSinceUtc": "'; }
pc_not_holding() { lease | grep -q '"holdingSinceUtc": null'; }
returned() { [ -n "$(sql cloud 'SELECT "ReturnedAtUtc" FROM "ClinicRelays" WHERE "Status" = 2 AND "ReturnedAtUtc" IS NOT NULL')" ]; }
healthy() { alive $P-cloud && docker exec $P-cloud curl -sf http://127.0.0.1:5000/health >/dev/null; }
# A container that died is a failure now, not a timeout in four minutes.
alive() {
  [ "$(docker inspect -f '{{.State.Running}}' "$1" 2>/dev/null)" = "true" ] && return 0
  echo "::error title=relay-roundtrip::$1 stopped — its last lines:" >&3
  docker logs --tail 15 "$1" >&3 2>&1
  exit 1
}
pc_ready() { alive ${1:-$P-pc} && docker logs ${1:-$P-pc} 2>&1 | grep -q "API fully ready"; }

PC_ENV=(-e ASPNETCORE_ENVIRONMENT=Production -e Deployment__Profile=ClinicRelay -e Auth__Mode=Local
  -e "ConnectionStrings__DefaultConnection=Host=$P-pg-pc;Port=5432;Database=pc;Username=clinic;Password=clinic"
  -e Hosting__HttpPort=5097 -e Hosting__HttpsPort=5096 -e Hosting__TrustPort=0 -e Hosting__WebPort=3097
  -e Console__Port=0 -e "FrontendUrl=https://$P-pc:5096"
  -e RateLimiting__Auth__PermitLimit=2000 -e RateLimiting__Auth__AddressPermitLimit=5000 -e RateLimiting__Api__PermitLimit=5000)
PC_VOLUMES=(-v $P-pc-local:/app/.local -v $P-pc-files:/app/Files)

# ── the stack ─────────────────────────────────────────────────────────────────────────────────────────────────
teardown # whatever a kept run (RT_KEEP) left behind
if [ -z "${RT_SKIP_BUILD:-}" ]; then
  step "build the API image"
  docker build -q -t "$IMAGE" --build-arg SOURCE_REVISION=roundtrip "$ROOT/api"
fi

step "networks, databases, object store"
docker network create $P-cloudnet >/dev/null
docker network create $P-cabinet >/dev/null
docker run -d --name $P-pg-cloud --network $P-cloudnet -e POSTGRES_USER=clinic -e POSTGRES_PASSWORD=clinic -e POSTGRES_DB=cloud postgres:16 >/dev/null
docker run -d --name $P-pg-pc --network $P-cabinet -e POSTGRES_USER=clinic -e POSTGRES_PASSWORD=clinic -e POSTGRES_DB=pc postgres:16 >/dev/null
docker run -d --name $P-driver --network $P-cloudnet -v "$ROOT/e2e:/e2e" node:22-bookworm-slim sleep infinity >/dev/null
docker network connect $P-cabinet $P-driver
# ⚠️ `minio/minio` is no longer pullable from Docker Hub (2026-10: « pull access denied », every tag); this is the
# Bitnami legacy build, pinned by digest. It runs as uid 1001, so the data dir lives under /tmp.
docker run -d --name $P-minio --network $P-cloudnet -e MINIO_ROOT_USER=minioadmin -e MINIO_ROOT_PASSWORD=minioadmin \
  --entrypoint sh bitnamilegacy/minio@sha256:451fe6858cb770cc9d0e77ba811ce287420f781c7c1b806a386f6896471a349c \
  -c 'mkdir -p /tmp/data/clinic-files && exec minio server /tmp/data' >/dev/null
wait_for 90 "cloud Postgres" docker exec $P-pg-cloud pg_isready -U clinic -d cloud
wait_for 90 "PC Postgres" docker exec $P-pg-pc pg_isready -U clinic -d pc

step "the cloud"
docker run -d --name $P-cloud --network $P-cloudnet -v $P-cloud-local:/app/.local \
  -e ASPNETCORE_ENVIRONMENT=Development -e Deployment__Profile=HostedMultiTenant -e Auth__Mode=Local   -e DataProtection__KeyRingPath=/app/.local/dataprotection-keys -e MinIO__AccessKey=minioadmin -e MinIO__SecretKey=minioadmin \
  -e "ConnectionStrings__DefaultConnection=Host=$P-pg-cloud;Port=5432;Database=cloud;Username=clinic;Password=clinic" \
  -e MinIO__Endpoint=$P-minio:9000 -e Console__Port=0 -e Hosting__Urls=http://0.0.0.0:5000 -e "FrontendUrl=${RT_FRONTEND_URL:-http://localhost:3000}" \
  -e RateLimiting__Auth__PermitLimit=2000 -e RateLimiting__Auth__AddressPermitLimit=5000 -e RateLimiting__Api__PermitLimit=5000 \
  "$IMAGE" >/dev/null
docker network connect --alias $P-cloud $P-cabinet $P-cloud
wait_for 240 "cloud /health" healthy

step "a cabinet and its administrator (the product's own first sign-in)"
ONE_TIME=$(docker exec $P-cloud dotnet ClinicManagement.API.dll provision-clinic --name "Cabinet Roundtrip" \
  --admin-email "$EMAIL" --admin-name "Dr Roundtrip" --city Tunis | sed -n 's/.*Temporary password: //p')
[ -n "$ONE_TIME" ] || fail "provision-clinic printed no temporary password"
CLINIC_PASSWORD="Rt!$(openssl rand -hex 10)aA1"
node_in bootstrap.mjs --password "$CLINIC_PASSWORD" --api "http://$P-cloud:5000/api" --email "$EMAIL" --temp-password "$ONE_TIME" > "$OUT/bootstrap.txt"
export CLINIC_EMAIL=$EMAIL
export CLINIC_PASSWORD # generated above, never a constant (bootstrap.mjs § PASSWORD)
CLINIC_TOTP_SECRET=$(sed -n 's/^CLINIC_TOTP_SECRET=//p' "$OUT/bootstrap.txt"); export CLINIC_TOTP_SECRET
[ -n "$CLINIC_TOTP_SECRET" ] || fail "bootstrap produced no authenticator secret"
drive login cloud
drive work cloud avant

step "the PC de secours: first start, pairing, first copy"
docker run -d --name $P-pc --network $P-cabinet "${PC_VOLUMES[@]}" "${PC_ENV[@]}" "$IMAGE" >/dev/null
pc_link
wait_for 240 "PC migrated" pc_ready
drive code $OUT_IN/pair-code.txt
docker cp "$OUT/pair-code.txt" $P-pc:/tmp/pair-code.txt
docker exec $P-pc dotnet ClinicManagement.API.dll pair-relay --code-file /tmp/pair-code.txt --cloud http://127.0.0.1:5000 --label "PC-CI"
docker restart $P-pc >/dev/null
pc_link
wait_for 480 "the PC is armed (Prêt)" pc_armed
wait_for 120 "the PC holds the cloud's latest change" pc_caught_up
wait_for 120 "both copies equal before the cut" digests_equal
drive login pc

if [ "${RT_STOP_AFTER:-}" = paired ]; then
  if [ -n "${RT_PUBLISH:-}" ]; then
    # A container on ONE network publishes fine; the cloud itself sits on two (see the header).
    docker run -d --name $P-publish --network $P-cloudnet -p 127.0.0.1:$RT_PUBLISH:5000 alpine/socat:1.8.0.1 \
      TCP-LISTEN:5000,fork,reuseaddr TCP:$P-cloud:5000 >/dev/null
    echo "  cloud published on http://127.0.0.1:$RT_PUBLISH"
  fi
  printf 'CLINIC_EMAIL=%s\nCLINIC_PASSWORD=%s\n' "$EMAIL" "$CLINIC_PASSWORD" >> "$OUT/bootstrap.txt"
  echo; echo "RELAY ROUNDTRIP: STOPPED AFTER PAIRING (RT_STOP_AFTER=paired)"
  exit 0
fi

# ── the cut ───────────────────────────────────────────────────────────────────────────────────────────────────
step "cut the cabinet's network (docker network disconnect)"
CUT=$(node -e 'console.log(Date.now())')
docker network disconnect $P-cabinet $P-cloud
# Manual takeover: for as long as nobody presses the button, the PC refuses and the cloud keeps recording.
drive probe 60 $OUT_IN/probe-before.json
pc_not_holding || fail "the PC took over on its own"
drive offered 60
drive take-over
pc_holding || fail "the button was pressed and the PC does not hold the cabinet's saves"
drive probe 20 $OUT_IN/probe-after.json

BEFORE="$OUT/probe-before.json" AFTER="$OUT/probe-after.json" node -e '
  const before = require(process.env.BEFORE), after = require(process.env.AFTER)
  const ok = (x) => x.status >= 200 && x.status < 300
  const fail = (m) => { console.log("::error title=manual takeover::" + m); process.exit(1) }
  const pcBefore = before.filter((x) => x.side === "pc" && ok(x))
  if (pcBefore.length) fail(`the PC accepted ${pcBefore.length} save(s) before anybody pressed the button`)
  if (before.some((x) => x.side === "cloud" && x.status === 423)) fail("the cloud locked on silence alone")
  if (!before.some((x) => x.side === "cloud" && ok(x))) fail("the cloud stopped recording during the cut")
  if (!after.some((x) => x.side === "pc" && ok(x))) fail("the PC accepted nothing after the press")
  console.log("  ✓ manual takeover: the cloud kept recording, the PC waited for the press, then took the saves")
'

step "work on the PC during the cut"
drive work pc pendant

# ── the network back ──────────────────────────────────────────────────────────────────────────────────────────
step "reconnect"
docker network connect --alias $P-cloud $P-cabinet $P-cloud
wait_for 480 "the cut returned to the cloud" returned
wait_for 120 "the PC no longer holds the saves" pc_not_holding
drive work cloud apres
wait_for 180 "the PC holds the cloud's latest change again" pc_caught_up
wait_for 180 "both copies equal after the return" digests_equal

# ── the verdicts ──────────────────────────────────────────────────────────────────────────────────────────────
step "note numbers continuous, every note on the cloud"
NUMBERS=$(sql cloud 'SELECT string_agg("Id"::text||'"'"'='"'"'||"Number", '"'"' '"'"' ORDER BY "Number") FROM "Invoices" WHERE "Number" IS NOT NULL')
echo "  cloud notes: $NUMBERS"
# Only the cloud gives legal numbers (review finding 1): a note made on the PC during the cut is provisional there
# (AAAA-P…) and comes back with the cloud's next real number — so notes are followed by id, not by number.
STATE="$RT_STATE" NUMBERS="$NUMBERS" node -e '
  const made = require(process.env.STATE).notes
  const byId = Object.fromEntries(process.env.NUMBERS.split(" ").filter(Boolean).map((p) => p.split("=")))
  const held = Object.values(byId)
  const fail = (m) => { console.log("::error title=numbers::" + m); process.exit(1) }
  for (const n of made) {
    const now = byId[n.id]
    if (!now) fail(`note ${n.number} made during the run is not on the cloud`)
    if (n.side === "pc" && !/^\d{4}-P[0-9A-F]{8}$/.test(n.number)) fail(`the PC gave note ${n.number} a legal number`)
    if (n.side === "pc") console.log(`  ✓ the PC numbered ${n.number} provisionally; the cloud made it ${now}`)
    else if (now !== n.number) fail(`the cloud note ${n.number} now reads ${now}`)
  }
  if (held.some((n) => !/^\d{4}-\d+$/.test(n))) fail(`a provisional number is left on the cloud: ${held.join(", ")}`)
  if (new Set(held).size !== held.length) fail("a note number is used twice")
  const seq = held.map((n) => Number(n.split("-")[1])).sort((a, b) => a - b)
  seq.forEach((v, i) => { if (v !== i + 1) fail(`numbers are not continuous: ${held.join(", ")}`) })
  console.log(`  ✓ ${held.length} notes, numbered 1 to ${held.length} with no gap and no duplicate`)
'

step "reconcile-money on both sides (exit 0)"
docker exec $P-cloud dotnet ClinicManagement.API.dll reconcile-money > "$OUT/reconcile-cloud.txt" 2>&1 \
  || { cat "$OUT/reconcile-cloud.txt"; fail "reconcile-money found drift on the cloud"; }
docker exec $P-pc dotnet ClinicManagement.API.dll reconcile-money > "$OUT/reconcile-pc.txt" 2>&1 \
  || { cat "$OUT/reconcile-pc.txt"; fail "reconcile-money found drift on the PC de secours"; }
echo "  ✓ both clean"

# ── retired while it holds the cabinet's saves (review finding 10, AC-8.6) ───────────────────────────────────────
step "retire the PC while it holds the cabinet's saves and the cloud knows it"
cloud_fenced() {
  [ "$(sql cloud 'SELECT ("PcHoldingSinceUtc" IS NOT NULL)::text FROM "ClinicRelays" WHERE "Status" = 2')" = "true" ]
}
pc_kept_cut() { # released, no longer holding, the cut's work marked unreturned
  pc_state | grep -q '"released": true' && lease | grep -q '"holdingSinceUtc": null' \
    && lease | grep -q '"unreturnedSinceUtc": "'
}
expect_exit() { # expect_exit <code> <sentence fragment> <what> <command…>
  local want=$1 says=$2 what=$3; shift 3
  local out code
  set +e; out=$("$@" 2>&1); code=$?; set -e
  [ "$code" = "$want" ] || { echo "$out"; fail "$what: exit $code, expected $want"; }
  echo "$out" | grep -q "$says" || { echo "$out"; fail "$what: the sentence does not say « $says »"; }
  echo "  ✓ $what (exit $code)"
}
wait_for 180 "the PC is armed again" pc_armed
docker network disconnect $P-cabinet $P-cloud
drive offered 90
drive take-over
pc_holding || fail "the button was pressed and the PC does not hold the cabinet's saves"
drive work pc retrait
# The line comes back but the return keeps failing: the PC's heartbeat says it holds, so the cloud locks.
touch "$OUT/block-handback"
pc_proxy
docker network connect --alias $P-cloud $P-cabinet $P-cloud
wait_for 180 "the cloud knows its PC holds the saves (the return is blocked)" cloud_fenced
pc_holding || fail "the PC let go of the saves although its return never landed"
drive login cloud
drive save cloud refused
drive retire
[ "$(sql cloud 'SELECT ("RetiredAtUtc" IS NOT NULL AND "ReclaimedAtUtc" IS NOT NULL)::text FROM "ClinicRelays" WHERE "Label" = '"'"'PC-CI'"'"'')" = "true" ] \
  || fail "the PC is not both retired and taken back on the cloud"
JOURNAL='SELECT count(*) FROM "AuditEntries" WHERE "EntityType" = '"'"'ClinicRelay'"'"' AND "ChangedFields" LIKE '
[ "$(sql cloud "$JOURNAL'Le cloud a repris la main sur le PC de secours (%'")" = "1" ] || fail "no journal row for the take-back"
[ "$(sql cloud "$JOURNAL'PC de secours retiré%'")" -ge 1 ] || fail "no journal row for the retirement"
echo "  ✓ the cloud took the saves back, then retired the PC — one journal row each"
drive save cloud ok
wait_for 180 "the PC learned it is retired and keeps the cut's work" pc_kept_cut
rm -f "$OUT/block-handback"
drive login pc
drive erase-refused relay_cut_work_kept
expect_exit 3 "pendant une coupure" "uninstalling refuses to erase the cut's work, before telling the cloud" \
  docker exec $P-pc dotnet ClinicManagement.API.dll uninstall-relay --erase
[ "$(sql cloud 'SELECT ("UninstalledAtUtc" IS NULL)::text FROM "ClinicRelays" WHERE "Label" = '"'"'PC-CI'"'"'')" = "true" ] \
  || fail "the cloud was told of an uninstall that refused"
docker exec $P-pc sh -c 'echo not-a-code > /tmp/not-a-code.txt'
expect_exit 1 "pendant une coupure" "re-pairing refuses to seed over the cut's work" \
  docker exec $P-pc dotnet ClinicManagement.API.dll pair-relay --code-file /tmp/not-a-code.txt --cloud http://127.0.0.1:5000 --replace
[ "$(sql pc "SELECT count(*) FROM \"Patients\" WHERE \"LastName\" LIKE 'retrait-%'")" = "1" ] \
  || fail "the patient made on the PC during the cut is gone from the PC"
echo "  ✓ the cut's patient is still on the PC"

# ── a cloud restored from before its PC was paired (review finding 9) ────────────────────────────────────────────
step "a second PC, then the cloud restored from a backup taken before it was paired"
pc2_unknown() { pc_state $P-pc2 | grep -q '"stoppedReason": "Le cloud ne reconna'; }
docker exec $P-pg-cloud pg_dump -U clinic -Fc -d cloud -f /tmp/before-pc2.dump
docker run -d --name $P-pg-pc2 --network $P-cabinet -e POSTGRES_USER=clinic -e POSTGRES_PASSWORD=clinic -e POSTGRES_DB=pc2 postgres:16 >/dev/null
wait_for 90 "PC 2 Postgres" docker exec $P-pg-pc2 pg_isready -U clinic -d pc2
PC2_ENV=("${PC_ENV[@]/Host=$P-pg-pc;Port=5432;Database=pc;/Host=$P-pg-pc2;Port=5432;Database=pc2;}")
docker run -d --name $P-pc2 --network $P-cabinet -v $P-pc2-local:/app/.local -v $P-pc2-files:/app/Files "${PC2_ENV[@]}" "$IMAGE" >/dev/null
pc_link $P-pc2 $P-link2
wait_for 240 "PC 2 migrated" pc_ready $P-pc2
drive code $OUT_IN/pair-code-2.txt
docker cp "$OUT/pair-code-2.txt" $P-pc2:/tmp/pair-code.txt
docker exec $P-pc2 dotnet ClinicManagement.API.dll pair-relay --code-file /tmp/pair-code.txt --cloud http://127.0.0.1:5000 --label "PC-CI-2"
docker restart $P-pc2 >/dev/null
pc_link $P-pc2 $P-link2
wait_for 480 "PC 2 is armed (Prêt)" pc_armed
drive work cloud apres-jumelage
wait_for 180 "PC 2 holds the cloud's latest change" pc_caught_up
wait_for 180 "both copies equal before the restore" digests_equal pc2
PATIENTS=$(sql pc2 'SELECT count(*) FROM "Patients"')
docker stop $P-cloud >/dev/null
docker exec $P-pg-cloud psql -U clinic -d postgres -q -c 'DROP DATABASE cloud WITH (FORCE)' -c 'CREATE DATABASE cloud'
docker exec $P-pg-cloud pg_restore -U clinic -d cloud /tmp/before-pc2.dump
docker start $P-cloud >/dev/null
wait_for 240 "the restored cloud /health" healthy
wait_for 240 "PC 2 stopped on « the cloud does not know this PC »" pc2_unknown
pc_state $P-pc2 | grep -q '"released": false' || fail "PC 2 counted itself released — its copy would be erasable"
[ "$(sql pc2 'SELECT count(*) FROM "Patients"')" = "$PATIENTS" ] || fail "PC 2 lost rows after the cloud forgot it"
echo "  ✓ PC 2 kept its $PATIENTS patients, one of them the cloud no longer has"
expect_exit 3 "a arrêté la copie" "uninstalling PC 2 refuses to erase its newer copy" \
  docker exec $P-pc2 dotnet ClinicManagement.API.dll uninstall-relay --erase
docker exec $P-pc2 sh -c 'echo not-a-code > /tmp/not-a-code.txt'
expect_exit 1 "a arrêté la copie" "re-pairing PC 2 refuses to seed over its newer copy" \
  docker exec $P-pc2 dotnet ClinicManagement.API.dll pair-relay --code-file /tmp/not-a-code.txt --cloud http://127.0.0.1:5000 --replace
[ "$(sql pc2 'SELECT count(*) FROM "Patients"')" = "$PATIENTS" ] || fail "PC 2 lost rows to an uninstall or a pairing that refused"

echo
echo "RELAY ROUNDTRIP: PASSED"
