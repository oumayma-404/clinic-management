#!/usr/bin/env bash
# relay-roundtrip — clinic-pc-copy D26, FR-10: a cloud and its PC de secours, the cabinet's network cut, work on both
# sides, the network back. Asserts « never both writable », note numbers continuous, both copies equal, and
# `reconcile-money` exit 0 on both.
#
# Everything runs in containers built from api/Dockerfile: the cloud (HostedMultiTenant) on networks cloudnet +
# cabinet, the PC (ClinicRelay) on cabinet only, each with its own Postgres; MinIO beside the cloud. The cut is
# `docker network disconnect cabinet cloud` — the PC loses the cloud, its own box (the network's gateway) still
# answers, which is exactly an internet cut seen from a cabinet.
#
#   e2e/relay-roundtrip/run.sh                 build the image, run, tear down
#   RT_SKIP_BUILD=1 RT_KEEP=1 …/run.sh         reuse the image, leave the containers up afterwards
#
# Needs: docker, node ≥ 20, bash. No port is published: the steps that talk HTTP run in a small node container on
# both networks (Docker Desktop cannot forward a published port to a container on two networks — measured).
set -euo pipefail
export MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*'
exec 3>&1 # the run's own output, still reachable from inside a check whose output is silenced

HERE=$(cd "$(dirname "$0")" && (pwd -W 2>/dev/null || pwd))
ROOT=$(cd "$HERE/../.." && (pwd -W 2>/dev/null || pwd))
OUT=${RT_OUT:-$HERE/.out}
mkdir -p "$OUT"
rm -f "$OUT"/*.json "$OUT"/*.log "$OUT"/*.txt
P=rt
IMAGE=${RT_IMAGE:-clinic-api-relay-rt}
EMAIL=rt.admin@cabinet-roundtrip.tn

export RT_STATE="$OUT/state.json"

step() { echo; echo "── $* ($(date -u +%H:%M:%S))"; }
fail() { echo "::error title=relay-roundtrip::$*"; exit 1; }

teardown() {
  docker rm -f $P-cloud $P-pc $P-pg-cloud $P-pg-pc $P-minio $P-driver $P-link >/dev/null 2>&1 || true
  docker network rm $P-cloudnet $P-cabinet >/dev/null 2>&1 || true
  docker volume rm $P-pc-local $P-pc-files $P-cloud-local >/dev/null 2>&1 || true
}
cleanup() {
  docker logs $P-cloud > "$OUT/cloud.log" 2>&1 || true
  docker logs $P-pc > "$OUT/pc.log" 2>&1 || true
  [ -n "${RT_KEEP:-}" ] || teardown
}
trap cleanup EXIT

# The HTTP half (drive.mjs, bootstrap.mjs) runs in $P-driver, which sits on both networks and is never cut.
node_in() { # node_in <script under e2e/> <args…>
  docker exec -e CLOUD_API=http://$P-cloud:5000/api -e PC_API=https://$P-pc:5096/api \
    -e RT_STATE=/e2e/relay-roundtrip/.out/state.json -e NODE_TLS_REJECT_UNAUTHORIZED=0 -e NODE_NO_WARNINGS=1 \
    -e CLINIC_EMAIL="${CLINIC_EMAIL:-}" -e CLINIC_PASSWORD="${CLINIC_PASSWORD:-}" -e CLINIC_TOTP_SECRET="${CLINIC_TOTP_SECRET:-}" \
    $P-driver node "/e2e/$@"
}
drive() { node_in relay-roundtrip/drive.mjs "$@"; }

# The PC reaches its cloud at http://127.0.0.1:5000: `pair-relay` accepts plain HTTP only on loopback (a real cloud
# is HTTPS). A socat sidecar sharing the PC's network namespace carries that port to the cloud — on the cabinet's
# side of the cut, so `docker network disconnect` cuts it like any other path. Re-created whenever the PC restarts.
pc_link() {
  docker rm -f $P-link >/dev/null 2>&1 || true
  docker run -d --name $P-link --network container:$P-pc alpine/socat:1.8.0.1 \
    TCP-LISTEN:5000,fork,reuseaddr TCP:$P-cloud:5000 >/dev/null
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
digests_equal() { [ "$(sql cloud "$DIGEST")" = "$(sql pc "$DIGEST")" ]; }
pc_caught_up() {
  [ "$(sql cloud 'SELECT (r."AppliedSeq" >= c."LastSeq")::text FROM "ClinicRelays" r JOIN "ClinicChangeCursors" c USING ("ClinicId") WHERE r."Status" = 2')" = "true" ]
}
pc_armed() { [ "$(sql cloud 'SELECT "ConfirmedAckArmed"::text FROM "ClinicRelays" WHERE "Status" = 2')" = "true" ]; }
lease() { docker exec $P-pc cat /app/.local/relay-lease.json 2>/dev/null; }
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
pc_ready() { alive $P-pc && docker logs $P-pc 2>&1 | grep -q "API fully ready"; }

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
  -e MinIO__Endpoint=$P-minio:9000 -e Console__Port=0 -e Hosting__Urls=http://0.0.0.0:5000 -e FrontendUrl=http://localhost:3000 \
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
drive code /e2e/relay-roundtrip/.out/pair-code.txt
docker cp "$OUT/pair-code.txt" $P-pc:/tmp/pair-code.txt
docker exec $P-pc dotnet ClinicManagement.API.dll pair-relay --code-file /tmp/pair-code.txt --cloud http://127.0.0.1:5000 --label "PC-CI"
docker restart $P-pc >/dev/null
pc_link
wait_for 480 "the PC is armed (Prêt)" pc_armed
wait_for 120 "the PC holds the cloud's latest change" pc_caught_up
wait_for 120 "both copies equal before the cut" digests_equal
drive login pc

# ── the cut ───────────────────────────────────────────────────────────────────────────────────────────────────
step "cut the cabinet's network (docker network disconnect)"
CUT=$(node -e 'console.log(Date.now())')
docker network disconnect $P-cabinet $P-cloud
drive probe 150 /e2e/relay-roundtrip/.out/probe.json
pc_holding || fail "150 s after the cut the PC still does not hold the cabinet's saves"

CUT=$CUT PROBE="$OUT/probe.json" node -e '
  const s = require(process.env.PROBE), cut = Number(process.env.CUT)
  const ok = (x) => x.status >= 200 && x.status < 300
  const cloudOk = s.filter((x) => x.side === "cloud" && ok(x)).map((x) => x.t)
  const pcOk = s.filter((x) => x.side === "pc" && ok(x)).map((x) => x.t)
  const fenced = s.find((x) => x.side === "cloud" && x.status === 423)
  const fail = (m) => { console.log("::error title=never both writable::" + m); process.exit(1) }
  if (!pcOk.length) fail("the PC never accepted a save during the cut")
  if (!fenced) fail("the cloud never refused a save during the cut")
  const lastCloud = cloudOk.length ? Math.max(...cloudOk) : 0, firstPc = Math.min(...pcOk)
  if (lastCloud >= firstPc) fail(`the cloud accepted a save at +${(lastCloud - cut) / 1000}s, after the PC took the first at +${(firstPc - cut) / 1000}s`)
  // The cloud fences 45 s after the last ack the PC confirmed, which can be ~20 s older than the cut itself.
  if (fenced.t - cut < 20000) fail(`the cloud fenced only ${(fenced.t - cut) / 1000}s after the cut`)
  const late = s.filter((x) => x.side === "cloud" && x.t > fenced.t && ok(x))
  if (late.length) fail(`the cloud accepted ${late.length} save(s) after it had fenced`)
  console.log(`  ✓ never both writable: cloud last accepted +${lastCloud ? (lastCloud - cut) / 1000 : "—"}s, fenced +${(fenced.t - cut) / 1000}s (${fenced.code}), PC first accepted +${(firstPc - cut) / 1000}s`)
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
NUMBERS=$(sql cloud 'SELECT string_agg("Number", '"'"' '"'"' ORDER BY "Number") FROM "Invoices" WHERE "Number" IS NOT NULL')
echo "  cloud notes: $NUMBERS"
STATE="$RT_STATE" NUMBERS="$NUMBERS" node -e '
  const made = require(process.env.STATE).notes.map((n) => n.number)
  const held = process.env.NUMBERS.split(" ").filter(Boolean)
  const fail = (m) => { console.log("::error title=numbers::" + m); process.exit(1) }
  for (const n of made) if (!held.includes(n)) fail(`note ${n} made during the run is not on the cloud`)
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

echo
echo "RELAY ROUNDTRIP: PASSED"
