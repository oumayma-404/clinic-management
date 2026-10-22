# Restore drill — a backup nobody can restore is not a backup

**Requirement:** FR-3.7. **Cadence:** **quarterly, and after every schema-migration batch.**

Pairing it with the migration batch is deliberate: that is already the moment `verify-schema` is run before and
after and the outputs diffed, so the drill costs one extra half-hour on a day the team is already looking at the
database — rather than a date in a calendar that slips for a year.

---

## Why this exists

Every automated check in this deployment answers *"did the backup run?"*. None of them answers *"could we get the
practice back?"* — and the two come apart quietly:

- a dump that uploads perfectly can be **truncated**;
- an archive that decrypts can be **empty**;
- a restore can succeed against a **key ring that never held its keys**, producing a practice whose second
  factors, reminder credentials and calendar tokens are *all* silently undecryptable — discovered days later,
  when nobody can sign in and the working ring has already been overwritten.

The nightly run does verify what it just wrote *when* the age identity is mounted (see `backup.sh` step 5). Most
deployments deliberately do not mount it — the key that opens every archive should not sit beside the archives —
and in that case **this drill is the verification**, not a supplement to it.

**What is automated, and what is not.** `deploy/backup/test/roundtrip.sh` runs **both** restore paths below on
fake data, on every change to `deploy/` (`.github/workflows/backup.yml`): a nightly run restored into empty servers
and compared row for row, checksum for checksum and key for key, and a database rebuilt from the WAL stream alone.
That proves the **procedure**. It proves nothing about **this deployment's** bucket, keys or data — only this
drill does, which is why the log below still says what it says.

There are **two** off-site copies, and they restore differently:

| Path | What it holds | Loss window | Restores |
|---|---|---|---|
| **A — nightly run** (`BACKUP_REMOTE`) | the database dump, every stored file (incremental mirror), the key ring, the key-ring stamp | up to a day | the whole practice |
| **B — PITR** (`WALG_S3_PREFIX`) | the database only: daily base backups + every WAL segment (≤ `PITR_ARCHIVE_TIMEOUT`, 300 s) | ≤ 5 minutes | the database — files and key ring still come from path A |

A lost server is rebuilt from **B for the database** and **A for everything else**. Until server-loss-recovery
(2026-10-06) path A had never reached the off-site bucket once — see `features/server-loss-recovery/`.

---

## Pass condition — state it before you start

The drill **passes** when, on a scratch host with nothing carried over from production:

1. the archive **decrypts**, and `pg_restore --list` on the dump is **non-empty**;
2. `check-keyring.sh` reports the key ring can read the backup;
3. the restored stack **starts**, `GET /health` answers `200`;
4. a real administrator **signs in with their second factor** — this is the step that proves the key ring
   travelled correctly, and no other step in this list can;
5. one clinic's **reminder settings** show « configuré » rather than « non configuré » — the second proof of the
   same thing, from a different family;
6. `verify-schema` exits **0**;
7. row counts for `Patients`, `Appointments` and `Invoices` **match the source** within the drill's time window.

Anything short of all seven is a **fail**, and a fail is written down here with what was done about it. A drill
that "mostly worked" is the one that teaches the wrong lesson.

---

## The drill

### 0. Prepare — on a scratch host, never on production

A fresh VPS (or any Linux box with Docker), the repository's `deploy/` directory, and from wherever
KEY-CUSTODY.md says they are kept:

- `backup-identity.txt` — the age **private** key (path A);
- the `.env` — for `BACKUP_REMOTE` + `BACKUP_S3_*` (path A) and `WALG_*` incl. `WALG_LIBSODIUM_KEY` (path B);
- `keyring-certificate.pfx` + its password, into `secrets/` — the ring in the backup is encrypted by it.

⚠️ Set `COMPOSE_PROJECT_NAME=clinic-drill` (or pass `-p clinic-drill`) for every command below, so nothing can be
mistaken for the production project even on a host that once ran it.

`docker-compose.hosted.yml` is written short below. Add `-f docker-compose.registry.yml` with `CLINIC_IMAGE_TAG` set
to the deployed tag, exactly as `deploy-hosted.yml` does, so `api`/`web`/`console` are pulled rather than built.

### 1. Path A — fetch, decrypt and prove the nightly run is whole

```bash
cd deploy
docker compose -f docker-compose.hosted.yml build backup
mkdir -p drill
docker compose -f docker-compose.hosted.yml run --rm --no-deps \
  -v "$PWD/drill:/drill" -v "$PWD/backup-identity.txt:/identity:ro" \
  --entrypoint /usr/local/bin/restore-drill.sh backup --identity /identity --out /drill   # [--run <timestamp>]
echo $?    # 0 = whole · 2 = NOT whole, do not restore from it · 1 = could not run
```

`restore-drill.sh` fetches the newest dated run and the object mirror, decrypts them, and checks — before anything
is restored — that:

- the dump **parses** (`pg_restore --list` non-empty — pass condition 1);
- every stored object the run's manifest names is **present at its recorded size** (a short mirror restores a
  practice whose records are all there and whose radiographs are silently missing);
- the archived **key ring holds the active key** the run's stamp names (pass condition 2 — the FR-3.9 question,
  answered offline; `check-keyring.sh` still answers it against a *live* marker when a dump is restored into a
  deployment whose ring was kept).

It leaves `drill/db.dump`, `drill/minio-data/` (the volume as MinIO lays it out), `drill/keyring/` and the stamp.

⚠️ **The mirror is the CURRENT state, not the state at `<timestamp>`.** A file deleted since that run was taken is
not in `objects/` — it is in `attic/<the night it went>/`, kept for `BACKUP_RETENTION_DAYS`. Restoring an older run
means pulling those paths from the attic as well; the drill names how many are missing.

### 2. Path B — rebuild the database from the WAL stream

The database to within ≤ 5 minutes of the loss. For a **drill**, into a standalone container whose configuration
has **no `archive_command`** — a drill that archived would write its own WAL into the production bucket.

```bash
set -a; . ./.env; set +a          # the custody copy of .env
docker volume create clinic-drill_pitr

# ⚠️ wal-g reads AWS_*, not the WALG_S3_* names .env uses — docker-compose.prod.yml maps them, so a bare
# `docker run --env-file .env` (what DEPLOY.md used to say) cannot reach the bucket. Map them here the same way.
WALG_ENV="-e WALG_S3_PREFIX -e WALG_LIBSODIUM_KEY -e AWS_ENDPOINT=$WALG_S3_ENDPOINT -e AWS_REGION=$WALG_S3_REGION \
  -e AWS_ACCESS_KEY_ID=$WALG_S3_ACCESS_KEY -e AWS_SECRET_ACCESS_KEY=$WALG_S3_SECRET_KEY \
  -e AWS_S3_FORCE_PATH_STYLE=$WALG_S3_FORCE_PATH_STYLE"
docker compose -f docker-compose.hosted.yml build postgres     # postgres:16 + wal-g

docker run --rm $WALG_ENV clinic-postgres-pitr:16 wal-g backup-list
docker run --rm $WALG_ENV -v clinic-drill_pitr:/var/lib/postgresql/data --entrypoint sh clinic-postgres-pitr:16 -c '
  wal-g backup-fetch /var/lib/postgresql/data LATEST &&
  echo "restore_command = '\''wal-g wal-fetch %f %p'\''" >> /var/lib/postgresql/data/postgresql.auto.conf &&
  touch /var/lib/postgresql/data/recovery.signal'
#   To stop BEFORE an accident rather than at the end of the stream, also append
#   recovery_target_time = '2026-07-13 14:29:59+00' and recovery_target_action = 'promote', and fetch the
#   base backup that ENDS before that time (by name from backup-list) instead of LATEST.

docker run -d --name clinic-drill-pitr $WALG_ENV -e POSTGRES_PASSWORD=unused \
  -v clinic-drill_pitr:/var/lib/postgresql/data clinic-postgres-pitr:16
# Replays every archived segment, then opens. Done when this answers « f »:
docker exec clinic-drill-pitr psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc 'SELECT pg_is_in_recovery()'
```

⚠️ **On a REAL rebuild** (the server is gone, not a drill) run the same fetch into the new deployment's
`postgres_data` volume and start the compose `postgres` service on it: it *should* archive, onto a new timeline in
the same bucket, from the moment it is promoted.

### 3. Bring up an isolated stack on the restored data

```bash
# Volumes first, nothing started: the data goes in before any service reads it.
docker compose -f docker-compose.hosted.yml create
docker run --rm -v "$PWD/drill:/drill:ro" -v clinic-drill_minio_data:/data alpine cp -a /drill/minio-data/. /data/
docker run --rm -v "$PWD/drill:/drill:ro" -v clinic-drill_dataprotection_keys:/keys alpine cp -a /drill/keyring/. /keys/
docker compose -f docker-compose.hosted.yml up -d certs postgres
# Path A's database:
docker compose -f docker-compose.hosted.yml exec -T postgres \
  pg_restore --clean --if-exists --no-owner --no-privileges -U "$POSTGRES_USER" -d "$POSTGRES_DB" < drill/db.dump
#   — or path B's: compare its row counts with path A's; the newer one is the database to keep.
docker compose -f docker-compose.hosted.yml up -d
```

### 4. Verify — all seven, in order

```bash
curl -fsS https://<drill-host>/health                                          # → 200          (3)
# sign in as a real administrator, with a real TOTP code                       # → succeeds     (4)
# open « Paramètres → Rappels » for one clinic                                 # → « configuré »(5)
docker exec <drill-api> dotnet ClinicManagement.API.dll verify-schema; echo $? # → 0            (6)
psql "$PGCONN" -c 'SELECT
  (SELECT COUNT(*) FROM "Patients")     AS patients,
  (SELECT COUNT(*) FROM "Appointments") AS appointments,
  (SELECT COUNT(*) FROM "Invoices")     AS invoices'                           # → matches      (7)
```

### 5. Tear down — completely

```bash
docker compose -f docker-compose.hosted.yml down -v
docker rm -f clinic-drill-pitr; docker volume rm clinic-drill_pitr
find drill -type f -exec shred -u {} +   # ⚠️ decrypted radiographs, the dump AND the key ring
rm -rf drill
shred -u backup-identity.txt .env secrets/*
```

⚠️ **`down -v` and `shred`, not `down`.** A drill host that keeps a decrypted copy of every practice's medical
records is a second production database that nobody is protecting — and it is where the next breach comes from.

---

## Log — one row per drill

Fill this in **during** the drill, not afterwards. « Passed » with no date and no name is not a record.

| Date | Run by | Archive restored | Result | Notes / what was fixed |
|---|---|---|---|---|
| _(YYYY-MM-DD)_ | _(name)_ | _(timestamp)_ | pass / **fail** | |

> **⚠️ No drill has been performed yet.** This deployment's restore path is **unproven** until the first row above
> is filled in. (The automated round trip proves the procedure on fake data — see « What is automated » above. It is
> not a row here, and must never be written as one.) It is stated here rather than left as an empty table, because an empty table reads as « nothing to
> report » and what it actually means is « we do not know whether we can get a practice back ».
