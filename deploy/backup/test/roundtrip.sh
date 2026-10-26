#!/usr/bin/env bash
# The backup, end to end, on fake data: back up → fetch from the « off-site » bucket → decrypt → load into a
# fresh database and a fresh object store → compare with what went in. Then the failures that must stay loud.
#
#   bash deploy/backup/test/roundtrip.sh
#
# Why it exists: backup.sh aborted every night for 33 nights on the live server (a mirror call with literal
# `\n` for line continuations) and nothing ran the script end to end to notice. Every assertion here is about
# an outcome — a row count, a checksum, a status line — never about a log line alone.
#
# Collects every failure and reports them together (.claude/rules/verification.md § 1): one dead step does not
# end the run. Exit 0 = all clear · 1 = at least one check failed.
#
# Isolated: project `backup-roundtrip`, no published port, no container_name, named volumes only, all removed
# on exit. Safe beside a running dev stack.
set -uo pipefail
export MSYS_NO_PATHCONV=1   # Git Bash on Windows: never rewrite /usr/local/bin/... into a Windows path

cd "$(dirname "$0")" || exit 1
PROJECT=backup-roundtrip
DC=(docker compose -p "${PROJECT}" -f docker-compose.roundtrip.yml)

FAILURES=()
ok()   { echo "  ✅ [$1] $2"; }
bad()  { echo "  ❌ [$1] $2 ${3:-}"; FAILURES+=("[$1] $2 ${3:-}"); }

cleanup() { "${DC[@]}" down -v --remove-orphans > /dev/null 2>&1 || true; }
trap cleanup EXIT

# `tool` runs a shell command with every volume mounted; its stdout is the answer.
tool() { "${DC[@]}" run --rm --no-deps -T tool "$1"; }
# the backup sidecar, as deployed
backup_exec() { "${DC[@]}" exec -T backup sh -c "$1"; }

ORIG_RUN_COUNT=0
dated_runs() { tool 'ls -1 /backups | grep -c "^20" || true' | tr -d '\r'; }
status_field() { tool "grep '^$1=' /status/backup.status | cut -d= -f2-" | tr -d '\r'; }

echo "── setup"
cleanup
"${DC[@]}" build --quiet backup tool certs postgres pitr postgres-pitr-restored > /dev/null || { echo "build failed"; exit 1; }
# The « off-site » bucket first: postgres starts archiving WAL to it the moment it is up.
"${DC[@]}" up -d minio offsite > /dev/null || { echo "minio did not come up"; exit 1; }
for _ in $(seq 1 30); do
	"${DC[@]}" run --rm --no-deps -T tool 'rclone --config /dev/null mkdir offsite:pitr' > /dev/null 2>&1 && break
	sleep 2
done
"${DC[@]}" up -d --wait postgres postgres-restored > /dev/null || { echo "postgres did not come up"; exit 1; }

# Fake key ring + the marker the API would write for it. The guid ↔ id pair is .NET's documented layout:
# Guid("01234567-89ab-cdef-0123-456789abcdef").ToByteArray() → 67452301ab89efcd0123456789abcdef.
tool '
	set -e
	printf "<key id=\"01234567-89ab-cdef-0123-456789abcdef\"><encryptedSecret>fake</encryptedSecret></key>\n" > /keyring/key-01234567-89ab-cdef-0123-456789abcdef.xml
	printf "<key id=\"11111111-2222-3333-4444-555555555555\"><encryptedSecret>older</encryptedSecret></key>\n" > /keyring/key-11111111-2222-3333-4444-555555555555.xml
	printf "active=67452301ab89efcd0123456789abcdef\nreadable=67452301ab89efcd0123456789abcdef\nreadable=11111111222233334444555555555555\n" > /keyring-marker/generation
	age-keygen -o /fixtures/identity.txt 2> /dev/null
	mkdir -p /fixtures/originals
	head -c 300000 /dev/urandom > /fixtures/originals/panoramique.bin
	head -c 4096   /dev/urandom > /fixtures/originals/ordonnance.pdf
	head -c 70000  /dev/urandom > /fixtures/originals/photo.jpg
	( cd /fixtures/originals && sha256sum * ) > /fixtures/originals.sha256

	# The leftovers of the 33 failed nights, exactly as they sit on the live disk:
	: > /backups/objects                                               # the empty FILE at the mirror path
	mkdir -p /backups/20200101T020000Z /backups/20991231T020000Z /backups/20200102T020000Z
	echo plaintext > /backups/20200101T020000Z/db-20200101T020000Z.dump     # an old plaintext dump
	echo plaintext > /backups/20991231T020000Z/db-20991231T020000Z.dump     # a recent one (mtime now)
	echo cipher    > /backups/20200102T020000Z/db-20200102T020000Z.dump.age # an encrypted one: must survive
' || { echo "fixtures failed"; exit 1; }

# MinIO may take a second to accept connections after `up`.
for _ in $(seq 1 30); do
	tool 'rclone --config /dev/null --ca-cert /certs/ca.crt lsd src: > /dev/null 2>&1 && rclone --config /dev/null lsd offsite: > /dev/null 2>&1' && break
	sleep 2
done
tool '
	set -e
	R="rclone --config /dev/null --ca-cert /certs/ca.crt"
	$R mkdir src:clinic-files
	$R copy /fixtures/originals src:clinic-files/clinics/5f5ca735-accf-4548-97d6-b75a5030808e
	rclone --config /dev/null mkdir offsite:backups
	export PGHOST=postgres PGUSER=clinic PGDATABASE=clinic PGSSLMODE=verify-full PGSSLROOTCERT=/certs/ca.crt
	psql -q -v ON_ERROR_STOP=1 <<SQL
CREATE TABLE "Patients"     (id serial PRIMARY KEY, name text);
CREATE TABLE "Appointments" (id serial PRIMARY KEY, patient int);
CREATE TABLE "Invoices"     (id serial PRIMARY KEY, amount numeric(12,3));
INSERT INTO "Patients"(name)       SELECT '\''Patient '\'' || g FROM generate_series(1, 318) g;
INSERT INTO "Appointments"(patient) SELECT (g % 318) + 1 FROM generate_series(1, 1204) g;
INSERT INTO "Invoices"(amount)      SELECT g * 1.5 FROM generate_series(1, 566) g;
SQL
' || { echo "seeding failed"; exit 1; }

RECIPIENT="$(tool 'age-keygen -y /fixtures/identity.txt' | tr -d '\r')"
export ROUNDTRIP_AGE_RECIPIENT="${RECIPIENT}"

echo "── A. a bad setting is recorded at container start, and never takes the container down"
# deploy-hosted.yml brings the stack up with --wait: a sidecar that exits on a bad setting fails every deploy.
ROUNDTRIP_REMOTE=offsite:no-such-bucket "${DC[@]}" up -d backup > /dev/null
sleep 5
RUNNING="$("${DC[@]}" ps --status running --services 2>/dev/null | grep -c '^backup$')"
[ "${RUNNING}" = "1" ] && ok A1 "an unreachable remote leaves the container running" \
	|| bad A1 "the container is not running with a bad remote" "$("${DC[@]}" logs backup 2>&1 | tail -5)"
[ "$(status_field outcome)" = "failed" ] && [ "$(status_field stage)" = "remote" ] \
	&& ok A2 "…and is recorded at once: outcome=failed, stage=remote" || bad A2 "start-up refusal not recorded" "$(tool 'cat /status/backup.status 2>&1')"
"${DC[@]}" up -d backup > /dev/null   # the good remote: compose recreates on the changed environment
sleep 5
if "${DC[@]}" logs backup 2>&1 | grep -q "preflight: .* reachable" && "${DC[@]}" logs backup 2>&1 | grep -q "scheduled:"; then
	ok A3 "with a reachable remote the preflight passes and the nightly run is scheduled"
else
	bad A3 "entrypoint did not schedule" "$("${DC[@]}" logs backup 2>&1 | tail -5)"
fi

echo "── B. first run"
RUN1_OUT="$(backup_exec /usr/local/bin/backup.sh 2>&1)"; RUN1_RC=$?
echo "${RUN1_OUT}" | sed 's/^/     | /'
[ "${RUN1_RC}" -eq 0 ] && ok B1 "backup.sh exits 0" || bad B1 "backup.sh exit ${RUN1_RC}"
echo "${RUN1_OUT}" | grep -q "removed the empty file left at /backups/objects" \
	&& ok B2 "the empty /backups/objects file left by the bug is cleared" || bad B2 "stray /backups/objects not cleared"
echo "${RUN1_OUT}" | grep -Eq "objects tracked: [1-9][0-9]* encrypted" \
	&& ok B3 "the mirror reads the real store" || bad B3 "mirror tracked no objects"
[ "$(status_field outcome)" = "succeeded" ] && ok B4 "status file says succeeded" || bad B4 "status outcome" "$(status_field outcome)"
[ -n "$(status_field lastSuccess)" ] && ok B5 "status file carries lastSuccess" || bad B5 "no lastSuccess"
PLAIN="$(tool 'find /backups -name "*.dump" -type f' | tr -d '\r')"
[ -z "${PLAIN}" ] && ok B6 "no plaintext dump left anywhere in /backups" || bad B6 "plaintext left" "${PLAIN}"
tool 'test -f /backups/20200102T020000Z/db-20200102T020000Z.dump.age' \
	&& ok B7 "an encrypted dump from an earlier run is untouched by the sweep" || bad B7 "the sweep removed a .dump.age"
REMOTE_RUN="$(tool 'rclone --config /dev/null lsf -R offsite:backups/hosted | grep "^20" | grep -v "/$" | sort' | tr -d '\r')"
for WANT in "db-.*\.dump\.age" "objects-.*\.manifest\.age" "keyring-ring-.*\.tar\.age" "keyring-2.*\.txt"; do
	echo "${REMOTE_RUN}" | grep -q "${WANT}" && ok B8 "off-site run holds ${WANT}" || bad B8 "off-site run lacks ${WANT}" "${REMOTE_RUN}"
done
UNENCRYPTED_REMOTE="$(echo "${REMOTE_RUN}" | grep -v '\.age$' | grep -v '/keyring-2[0-9TZ]*\.txt$' || true)"
[ -z "${UNENCRYPTED_REMOTE}" ] && ok B9 "nothing readable reached the off-site bucket" || bad B9 "unencrypted files off-site" "${UNENCRYPTED_REMOTE}"
# MinIO stores an object as a directory (xl.meta, plus part files for a large one), so look for each NAME.
REMOTE_OBJECTS="$(tool 'rclone --config /dev/null lsf -R --files-only offsite:backups/hosted/objects' | tr -d '\r')"
for NAME in panoramique.bin ordonnance.pdf photo.jpg; do
	echo "${REMOTE_OBJECTS}" | grep -q "clinic-files/clinics/.*/${NAME}/.*\.age$" \
		&& ok B10 "${NAME} is in the off-site mirror, encrypted" || bad B10 "${NAME} missing from the off-site mirror"
done

echo "── C. restore from the off-site copy, into empty servers"
DRILL_OUT="$(backup_exec '/usr/local/bin/restore-drill.sh --identity /fixtures/identity.txt --out /drill/run1' 2>&1)"; DRILL_RC=$?
echo "${DRILL_OUT}" | sed 's/^/     | /'
[ "${DRILL_RC}" -eq 0 ] && ok C1 "restore-drill.sh: the run is whole" || bad C1 "restore-drill.sh exit ${DRILL_RC}"

tool '
	export PGHOST=postgres-restored PGUSER=clinic PGDATABASE=clinic PGSSLMODE=disable
	pg_restore --no-owner --no-privileges --dbname clinic /drill/run1/db.dump
' > /dev/null 2>&1
COUNTS_SQL='SELECT (SELECT count(*) FROM \"Patients\"), (SELECT count(*) FROM \"Appointments\"), (SELECT count(*) FROM \"Invoices\")'
SRC_COUNTS="$(tool "PGHOST=postgres PGUSER=clinic PGDATABASE=clinic PGSSLMODE=verify-full PGSSLROOTCERT=/certs/ca.crt psql -tA -F, -c \"${COUNTS_SQL}\"" | tr -d '\r')"
DST_COUNTS="$(tool "PGHOST=postgres-restored PGUSER=clinic PGDATABASE=clinic PGSSLMODE=disable psql -tA -F, -c \"${COUNTS_SQL}\"" | tr -d '\r')"
[ -n "${SRC_COUNTS}" ] && [ "${SRC_COUNTS}" = "${DST_COUNTS}" ] \
	&& ok C2 "restored database matches: patients/appointments/invoices = ${DST_COUNTS}" \
	|| bad C2 "row counts differ" "source='${SRC_COUNTS}' restored='${DST_COUNTS}'"

tool 'cp -a /drill/run1/minio-data/. /minio-restored/' > /dev/null
"${DC[@]}" up -d minio-restored > /dev/null
for _ in $(seq 1 30); do tool 'rclone --config /dev/null lsd restored: > /dev/null 2>&1' && break; sleep 2; done
READBACK="$(tool '
	rm -rf /fixtures/readback && mkdir -p /fixtures/readback
	rclone --config /dev/null copy restored:clinic-files/clinics/5f5ca735-accf-4548-97d6-b75a5030808e /fixtures/readback
	cd /fixtures/readback && sha256sum -c /fixtures/originals.sha256
' 2>&1 | tr -d '\r')"
[ "$(echo "${READBACK}" | grep -c ': OK$')" = "3" ] \
	&& ok C3 "all 3 files read back from the restored object store, byte-identical" \
	|| bad C3 "restored files differ" "${READBACK}"

RING_DIFF="$(tool 'diff -r /keyring /drill/run1/keyring && echo SAME' | tr -d '\r')"
[ "${RING_DIFF}" = "SAME" ] && ok C4 "restored key ring is identical to the live one" || bad C4 "key ring differs" "${RING_DIFF}"

echo "── D. the next nights"
# MinIO rewrites its own .minio.sys metadata on its own; the patient OBJECTS are immutable and must not be redone.
backup_exec 'touch /tmp/before-run2' > /dev/null
sleep 1
RUN2_OUT="$(backup_exec /usr/local/bin/backup.sh 2>&1)"; RUN2_RC=$?
REDONE="$(backup_exec 'find /backups/objects/clinic-files -type f -newer /tmp/before-run2' | tr -d '\r')"
[ "${RUN2_RC}" -eq 0 ] && [ -z "${REDONE}" ] \
	&& ok D1 "second run is incremental: no patient file re-encrypted" || bad D1 "second run" "rc=${RUN2_RC} redone=${REDONE} $(echo "${RUN2_OUT}" | tail -3)"
tool 'rclone --config /dev/null --ca-cert /certs/ca.crt deletefile src:clinic-files/clinics/5f5ca735-accf-4548-97d6-b75a5030808e/photo.jpg' > /dev/null
sleep 2   # a run in the same second would reuse the timestamp
RUN3_OUT="$(backup_exec /usr/local/bin/backup.sh 2>&1)"; RUN3_RC=$?
[ "${RUN3_RC}" -eq 0 ] && echo "${RUN3_OUT}" | grep -q "moved to the attic" && ! echo "${RUN3_OUT}" | grep -q " 0 moved to the attic" \
	&& ok D2 "a deleted file moves to the attic, not into nothing" || bad D2 "attic" "rc=${RUN3_RC} $(echo "${RUN3_OUT}" | grep objects)"
ATTIC="$(tool 'rclone --config /dev/null lsf -R --files-only offsite:backups/hosted/attic | grep -c "photo.jpg/"' | tr -d '\r')"
[ "${ATTIC}" -ge 1 ] 2> /dev/null && ok D3 "the deleted file is in the off-site attic" || bad D3 "off-site attic lacks the deleted file" "${ATTIC}"

echo "── E. failures stay loud, leave nothing behind, and are recorded"
LAST_SUCCESS="$(status_field lastSuccess)"
ORIG_RUN_COUNT="$(dated_runs)"
FAIL_OUT="$(backup_exec 'mkdir -p /tmp/empty-ring && KEYRING_DIR=/tmp/empty-ring /usr/local/bin/backup.sh' 2>&1)"; FAIL_RC=$?
[ "${FAIL_RC}" -ne 0 ] && ok E1 "a ring that did not mount fails the run" || bad E1 "empty ring accepted" "$(echo "${FAIL_OUT}" | tail -3)"
[ "$(status_field outcome)" = "failed" ] && [ "$(status_field stage)" = "keyring" ] \
	&& ok E2 "status records outcome=failed, stage=keyring" || bad E2 "status after failure" "$(tool 'cat /status/backup.status')"
[ "$(status_field lastSuccess)" = "${LAST_SUCCESS}" ] && ok E3 "a failure keeps the last success time" || bad E3 "lastSuccess changed on failure"
[ "$(dated_runs)" = "${ORIG_RUN_COUNT}" ] && ok E4 "the failed run left no directory behind" || bad E4 "failed run left a directory"
PLAIN="$(tool 'find /backups -name "*.dump" -type f' | tr -d '\r')"
[ -z "${PLAIN}" ] && ok E5 "the failed run's plaintext dump is gone" || bad E5 "plaintext left after a failure" "${PLAIN}"
FAIL2_OUT="$(backup_exec 'BACKUP_REMOTE=offsite:no-such-bucket /usr/local/bin/backup.sh' 2>&1)"; FAIL2_RC=$?
[ "${FAIL2_RC}" -ne 0 ] && [ "$(status_field stage)" = "remote" ] \
	&& ok E6 "an unreachable remote fails before anything is dumped (stage=remote)" || bad E6 "remote failure" "rc=${FAIL2_RC} stage=$(status_field stage) $(echo "${FAIL2_OUT}" | tail -2)"
FAIL3_OUT="$(backup_exec 'BACKUP_REMOTE= /usr/local/bin/backup.sh' 2>&1)"; FAIL3_RC=$?
[ "${FAIL3_RC}" -ne 0 ] && echo "${FAIL3_OUT}" | grep -q "BACKUP_REMOTE is not set" \
	&& ok E7 "no remote is a refusal, not a « local only » success" || bad E7 "empty remote" "rc=${FAIL3_RC}"

echo "── F. the drill refuses a damaged copy"
tool 'rclone --config /dev/null delete offsite:backups/hosted/objects --include "**/ordonnance.pdf/**"' > /dev/null
DAMAGED_OUT="$(backup_exec '/usr/local/bin/restore-drill.sh --identity /fixtures/identity.txt --out /drill/damaged' 2>&1)"; DAMAGED_RC=$?
[ "${DAMAGED_RC}" -eq 2 ] && ok F1 "a mirror missing a file fails the drill (exit 2)" || bad F1 "damaged mirror accepted" "rc=${DAMAGED_RC} $(echo "${DAMAGED_OUT}" | tail -3)"
WRONG_ID_OUT="$(backup_exec 'age-keygen -o /tmp/other.txt 2>/dev/null; /usr/local/bin/restore-drill.sh --identity /tmp/other.txt --out /drill/wrongkey' 2>&1)"; WRONG_RC=$?
[ "${WRONG_RC}" -eq 2 ] && ok F2 "the wrong private key fails the drill (exit 2)" || bad F2 "wrong key accepted" "rc=${WRONG_RC} $(echo "${WRONG_ID_OUT}" | tail -3)"

echo "── G. the database rebuilt from the continuous WAL stream alone (RESTORE-DRILL.md path B)"
# Production's only working off-site copy until this change. Its restore had never been run.
SRC_PG="PGHOST=postgres PGUSER=clinic PGDATABASE=clinic PGSSLMODE=verify-full PGSSLROOTCERT=/certs/ca.crt"
PITR_OUT="$("${DC[@]}" run --rm --no-deps -T pitr 2>&1)"; PITR_RC=$?
[ "${PITR_RC}" -eq 0 ] && echo "${PITR_OUT}" | grep -q "base backup complete" \
	&& ok G1 "a base backup reached the off-site bucket, through the TLS hop" || bad G1 "base backup" "rc=${PITR_RC} $(echo "${PITR_OUT}" | tail -3)"

# Written AFTER the base backup: only WAL replay can bring these rows back.
SWITCHED="$(tool "${SRC_PG} psql -tA -c \"INSERT INTO \\\"Patients\\\"(name) SELECT 'after base ' || g FROM generate_series(1, 7) g\" -c \"SELECT pg_walfile_name(pg_switch_wal())\"" | tail -n 1 | tr -d '\r')"
ARCHIVED=""
for _ in $(seq 1 60); do
	ARCHIVED="$(tool "${SRC_PG} psql -tAc \"SELECT last_archived_wal >= '${SWITCHED}' FROM pg_stat_archiver\"" | tr -d '\r')"
	[ "${ARCHIVED}" = "t" ] && break
	sleep 2
done
[ "${ARCHIVED}" = "t" ] && ok G2 "the WAL holding the later writes was archived off-site (${SWITCHED})" \
	|| bad G2 "WAL ${SWITCHED} never archived" "$(tool "${SRC_PG} psql -tAc 'SELECT * FROM pg_stat_archiver'")"

"${DC[@]}" run --rm --no-deps -T --entrypoint sh postgres-pitr-restored -c '
	set -e
	wal-g backup-fetch /var/lib/postgresql/data LATEST
	echo "restore_command = '\''wal-g wal-fetch %f %p'\''" >> /var/lib/postgresql/data/postgresql.auto.conf
	touch /var/lib/postgresql/data/recovery.signal
' > /dev/null 2>&1 || bad G3 "wal-g backup-fetch failed"
"${DC[@]}" up -d postgres-pitr-restored > /dev/null
PROMOTED=""
for _ in $(seq 1 60); do
	PROMOTED="$("${DC[@]}" exec -T postgres-pitr-restored psql -U clinic -d clinic -tAc 'SELECT pg_is_in_recovery()' 2> /dev/null | tr -d '\r')"
	[ "${PROMOTED}" = "f" ] && break
	sleep 2
done
[ "${PROMOTED}" = "f" ] && ok G3 "the rebuilt server replayed the WAL and opened for writes" \
	|| bad G3 "rebuilt server never left recovery" "$("${DC[@]}" logs --tail 8 postgres-pitr-restored 2>&1)"
SRC_COUNTS="$(tool "${SRC_PG} psql -tA -F, -c \"${COUNTS_SQL}\"" | tr -d '\r')"
PITR_COUNTS="$("${DC[@]}" exec -T postgres-pitr-restored psql -U clinic -d clinic -tA -F, -c "$(echo "${COUNTS_SQL}" | tr -d '\\')" 2> /dev/null | tr -d '\r')"
[ -n "${SRC_COUNTS}" ] && [ "${SRC_COUNTS}" = "${PITR_COUNTS}" ] \
	&& ok G4 "rebuilt database matches the live one, including the 7 rows written after the base backup (${PITR_COUNTS})" \
	|| bad G4 "PITR row counts differ" "source='${SRC_COUNTS}' rebuilt='${PITR_COUNTS}'"

echo
if [ "${#FAILURES[@]}" -eq 0 ]; then
	echo "ALL CLEAR — backup → off-site → restore round trip verified."
	exit 0
fi
echo "${#FAILURES[@]} FAILURE(S):"
printf '  %s\n' "${FAILURES[@]}"
exit 1
