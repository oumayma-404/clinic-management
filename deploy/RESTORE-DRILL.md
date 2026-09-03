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

```bash
# The three things you need, from wherever KEY-CUSTODY.md says they are kept:
#   - backup-identity.txt              (the age private key)
#   - keyring-certificate.pfx + its password
#   - the archive to restore
```

### 1. Fetch and decrypt

```bash
# The dated run holds the database dump, the key-ring stamp and the object manifest — all small.
rclone copy "${BACKUP_REMOTE}/<timestamp>" ./drill --config ./rclone/rclone.conf
cd drill
age --decrypt --identity backup-identity.txt --output db.dump db-<timestamp>.dump.age
pg_restore --list db.dump | head -20        # ⚠️ non-empty, or STOP: pass condition 1 failed

# The OBJECTS are a mirror, not a dated archive — one `.age` file per stored object, under `objects/`.
# Fetch it and decrypt each file back into the shape MinIO expects.
rclone copy "${BACKUP_REMOTE}/objects" ./objects --config ./rclone/rclone.conf
mkdir -p ./minio-data
find ./objects -name '*.age' | while read -r ENC; do
  REL="${ENC#./objects/}"; REL="${REL%.age}"
  mkdir -p "./minio-data/$(dirname "${REL}")"
  age --decrypt --identity backup-identity.txt --output "./minio-data/${REL}" "${ENC}"
done

# ⚠️ Count what you decrypted against the manifest that travelled with the dump. They must agree — a
# short mirror restores a practice whose records are all present and whose radiographs are silently missing.
age --decrypt --identity backup-identity.txt --output objects.manifest objects-<timestamp>.manifest.age
echo "manifest: $(wc -l < objects.manifest)   restored: $(find ./minio-data -type f | wc -l)"
```

⚠️ **The mirror is the CURRENT state, not the state at `<timestamp>`.** A file deleted since that dump was
taken is not in `objects/` — it is in `attic/<the night it went>/`, kept for `BACKUP_RETENTION_DAYS`. If the
counts above disagree, that is where the difference is, and a point-in-time restore has to pull the missing
paths from the attic as well.

### 2. Check the key-ring generation **before** restoring anything

```bash
# The stamp travelled with the backup; the marker is written by the API at every startup.
./check-keyring.sh keyring-<timestamp>.txt /path/to/live/keyring_marker/generation
```

⚠️ Exit **2** means **do not restore**. Restoring against a ring that cannot read this backup produces a
practice whose every second factor is undecryptable, with **no error anywhere** — the exact failure FR-3.9
exists to prevent. Restore the matching key ring first (KEY-CUSTODY.md § 1).

### 3. Bring up an isolated stack and restore

```bash
docker compose -f docker-compose.hosted.yml up -d postgres minio
pg_restore --clean --if-exists --no-owner --dbname "$PGCONN" db.dump
cp -a ./minio-data/. /var/lib/docker/volumes/<drill>_minio_data/_data/
# Restore the key-ring volume from its own separate copy, and the certificate from its own place.
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
shred -u backup-identity.txt db.dump objects.manifest
rm -rf ./objects ./minio-data          # ⚠️ decrypted radiographs — remove the whole tree, not just the dump
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
> is filled in. It is stated here rather than left as an empty table, because an empty table reads as « nothing to
> report » and what it actually means is « we do not know whether we can get a practice back ».
