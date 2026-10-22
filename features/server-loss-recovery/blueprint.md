# Server loss recovery — blueprint (Option 1: fix and finish what is built)

**Question it answers:** if the server dies today, what do the clinics lose — and how do we make the answer
« nothing older than a few minutes, and we have proven it ».

**Status (2026-10-06):** Phase A (code, local proof) **done** for Parts 1–4 and the first slice of Part 6 — see
`notes.md` for what shipped, what is still open, and the Phase B prod runbook. Part 5 is a Backblaze setting (B3).
Nothing has been deployed. Chosen over « managed DB + storage » (monthly cost, live data move, still leaves
keys/alerts/drill open) and « OVH snapshots only » (same provider, daily, untested).

⚠️ Two deviations from the plan below, both recorded in `notes.md`: the `backup` container does **not** refuse
to start on a bad remote (it records the failure and stays up — `deploy-hosted.yml`'s `up --wait` would fail every
deploy otherwise), and there is **no `pitr` status file** (its script is baked into the database's own image, so
changing it restarts the live database; `pg_stat_archiver` is read instead).

---

## What is true today (2026-10-06)

Live server facts come from read-only checks on the VPS; the rest from the repo.

| Data | Survives a lost VPS today? | Evidence |
|---|---|---|
| Database | ✅ to ~5 min — **only if `WALG_LIBSODIUM_KEY` exists off the server** | WAL-G → Backblaze B2 `us-east-005`; `pg_stat_archiver` last archived 20:55 UTC, 0 failures; base backups 2–6 Oct |
| Patient files (MinIO, 168 files / 91 MB) | ❌ lost | nightly run aborts every night since `383154aa` (2026-09-03); `rclone/rclone.conf` absent on the server |
| Key ring (2FA, SMS/WhatsApp/SMTP creds, Google tokens) | ❌ lost | `dataprotection_keys` volume backed up by nothing |
| Restore works? | ❓ never run | `deploy/RESTORE-DRILL.md:143`, `features/postgres-pitr/progress.md:25` |
| Anyone told when a backup fails? | ❌ no | no healthcheck on `backup`/`pitr`, no alert path; the failure ran silently for 33 nights |
| Plaintext dumps on the server | ⚠️ 40 nights of them, never pruned | the abort happens before step 4 (encrypt + `rm` plaintext) and step 7 (prune) |
| Offline LAN clinic, default install | ❌ everything lost | backup on the same disk; the archive cannot be restored into a fresh install |

**Root cause of the nightly failure** — `deploy/backup/backup.sh:94`: the `mirror-objects.sh` call has literal
`\n` where line-continuation backslashes belong, so `sh` passes `n` / `/minio-data` / `n` / `/backups/objects` …
as the five arguments. The mirror `cd n`s into nothing, writes a 0-byte **file** at `/backups/objects` (where the
mirror directory should be), `cp /backups/objects.manifest` fails, and `set -eu` aborts the run before encryption,
upload and pruning. `features/large-file-transfer/notes.md` already says the mirror was only tested alone.

---

## Part 0 — Today, by hand (owner, no code)

The working database backup is useless without its key. Copy these **off the server**, into a password manager
or vault that is **not** the B2 account and **not** only this laptop:

| File on the VPS (`/opt/clinic-management/deploy/`) | Opens |
|---|---|
| `.env` → `WALG_LIBSODIUM_KEY` (and the B2 keys, `POSTGRES_PASSWORD`) | every database backup on B2 |
| `secrets/keyring-certificate.pfx` + `secrets/keyring-certificate-password` | 2FA, SMS/WhatsApp/SMTP creds, Google tokens |
| `secrets/audit-chain-key` | proof the audit log was not edited |
| `/etc/clinic/luks.key` — only if LUKS was applied (`KEY-CUSTODY.md` key 4) | the encrypted data disk |
| `backup-identity.txt` (age private key) — should already be off-server; if nobody has it, Part 1 mints a new pair | the nightly encrypted copies |

Then fill the holder table in `deploy/KEY-CUSTODY.md:24-30` (real names, real places).

---

## Part 1 — The nightly copy works again, and cannot silently stop

### Files to modify
- `deploy/backup/backup.sh`
  - **:94** — real `\` line continuations.
  - **Self-heal the bug's leftover:** before the mirror, if `${OBJECTS_DIR}` is a *0-byte regular file*, remove
    it; any other non-directory → fail with a sentence. No manual prod step needed.
  - **No plaintext left behind, ever:** a `trap … EXIT` that deletes `${DB_FILE}` (plaintext) whenever its `.age`
    sibling does not exist; plus a sweep that removes any `db-*.dump` (plaintext) under `/backups/20*/` — this
    cleans the 40 existing ones on the first good run.
  - **Hosted is never local-only:** `BACKUP_REMOTE` empty → `exit 1` with a sentence (today: a WARNING nobody
    reads). Keep the WARNING path only behind an explicit `BACKUP_ALLOW_LOCAL_ONLY=1` for dev.
  - Write `/status/backup.json` (`lastSuccessUtc`, `lastFailureUtc`, `lastError`, `dumpBytes`, `objects`) on
    success and from the EXIT trap on failure (Part 3 reads it).
- `deploy/backup/entrypoint.sh` — at container start, `rclone lsd "${BACKUP_REMOTE}"`; unreachable → exit
  non-zero so `docker compose up` / the deploy workflow shows it **at deploy time**, not at 02:00.
- `deploy/docker-compose.prod.yml` (backup service)
  - **Remote from env, not a gitignored file.** `RCLONE_CONFIG_OFFSITE_TYPE=b2`,
    `RCLONE_CONFIG_OFFSITE_ACCOUNT=${BACKUP_B2_KEY_ID}`, `RCLONE_CONFIG_OFFSITE_KEY=${BACKUP_B2_APP_KEY}` —
    rclone reads `RCLONE_CONFIG_<NAME>_<OPT>` natively. The `./rclone` mount goes: the file vanished from the
    server (directory recreated 2026-10-04) and nothing noticed.
  - `backup_status:/status` volume (rw here, ro in the API — Part 3).
- `deploy/.env.example`, `deploy/.env.hosted.example` — the new `BACKUP_B2_*` names; drop the `rclone.conf`
  instructions.
- `.github/workflows/ci.yml`
  - **`shellcheck deploy/**/*.sh`** — SC1001-class warnings flag a literal `\n` outside quotes.
  - **New job `backup-roundtrip`** (path-filtered on `deploy/**`): compose up postgres + minio + a *second*
    minio standing in for B2 (rclone `s3` remote via env); seed rows + 3 objects; run `backup.sh` once; assert
    `[backup] … done`, the `.age` dump and the objects exist on the « remote », **no plaintext `.dump` remains**;
    then run Part 4's `restore-drill.sh` against fresh volumes and assert row counts + objects + ring. The script
    shipped broken because nothing ever ran it end to end — this is the derived guard.

### Pitfalls
- Changing env/mounts of `backup` needs `--force-recreate` on deploy (single-file bind mounts keep the old inode).
- Deploying this is a **prod change → owner's explicit OK** (memory: never-touch-prod-without-consent).
- `rclone sync` of `objects/` against an empty remote uploads the whole store once (~91 MB today) — fine, say so.

---

## Part 2 — The key ring travels with the backup (the certificate does not)

`KEY-CUSTODY.md` § « What travels together » (FR-3.11) is the one voice: since FR-3.1 the ring is **encrypted by
the certificate**, so it may travel with the data; the **certificate** travels apart. Two places still say the
old rule and are why nothing backs the ring up:

- `deploy/docker-compose.prod.yml:221-223` (« the MARKER, never the key ring ») and `backup.sh` step 3 comment —
  rewrite to FR-3.11.
- Mount `dataprotection_keys:/keyring:ro` into `backup`; step 3b: `tar` the ring → `keyring-<TS>.tar` →
  age-encrypt with the run (double-wrapped: cert + age).
- `deploy/backup/check-keyring.sh` — verify the archived ring's key ids match the stamp.
- `RESTORE-DRILL.md` step 3 « restore the ring from its own separate copy » → « from `keyring-<TS>.tar.age` in
  the same run ».
- PITR path: base backups do not carry the ring; a PITR restore takes the **newest** nightly ring (rings only
  grow, so the newest reads every older ciphertext). Write that in the runbook.

**Never** put `keyring-certificate.pfx`, `internal_certs` or `WALG_LIBSODIUM_KEY` in any archive.

---

## Part 3 — Someone is told the same day a backup fails

### New
- **Capability** `MonitorsSidecarBackups` on `DeploymentProfile` (HostedMultiTenant true, SelfHostedLan false) —
  a named capability, never `!BacksUpItsOwnData`. Pin it in `DeploymentProfileTests`.
- `api/ClinicManagement.Application/Features/Platform/BackupHealth/GetBackupHealthQuery.cs` → `BackupHealthDto`
  (`nightly`: last success / last error / age; `pitr`: `pg_stat_archiver` last archived, failed count, last
  failure; `verdict`: Ok · Stale · Failing).
  - `IBackupStatusReader` (Application interface) + `SidecarBackupStatusReader` (Infrastructure) reads
    `/backup-status/backup.json` and `pg_stat_archiver` (readable by any role).
  - Thresholds: nightly stale > 26 h; WAL stale > 15 min **or** `failed_count` grew since last success.
- `PlatformBackupHealthController` → `GET /api/platform/backup-health` (console port only, like the others).
- `BackupHealthJob` (Hangfire, registered only `if (profile.MonitorsSidecarBackups)`): daily 08:00 Tunis
  (`ClinicClock`), emails every console account via `ITransactionalEmailSender` while the verdict is not Ok.
  Stateless — a daily nag while broken, no table, no migration.
- `console/` home: a status strip « Sauvegarde hors serveur — dernière réussie il y a 3 h » (red when stale).
- `deploy-hosted.yml`: after `/health`, call the backup-health read through the console tunnel path or a
  `docker exec` read; **Failing** fails the deploy job.
- `docker-compose.prod.yml`: `healthcheck` on `backup` (status file age) so `docker ps` says `unhealthy`.

### Pitfalls
- The job has no HTTP context → `UseSystemWide(reason)`, or it reads zero rows silently.
- `Features/Platform` already exists → no new realtime key. A new `Features/<Area>` folder would need one.
- `ClinicClock`, never `DateTime.UtcNow`.
- `ITransactionalEmailSender.IsConfigured == false` → the console strip is the only signal; say so in the strip.

---

## Part 4 — The restore is proven, not assumed

- `deploy/restore-drill.sh` — the drill as a script (fetch → decrypt → `pg_restore` → ring + cert → stack up →
  `/health` 200 → `verify-schema` 0 → row counts → objects present). Takes `--source local|b2`. CI runs it in
  `backup-roundtrip` (Part 1) on every `deploy/**` change.
- **PITR path B** moves from `DEPLOY.md:222-271` into `RESTORE-DRILL.md`, fixed:
  - `docker run --env-file .env` passes `WALG_S3_*` names but wal-g reads `AWS_*` (compose maps them at
    `prod.yml:87-90`) — use the same mapping.
  - add the « switch production to the restored instance » step it lacks.
- `deploy/REBUILD.md` — « the VPS is gone »: new VPS (`VPS-BRINGUP.md`) → keys from Part 0 → path A or B →
  DNS → clinics re-trust nothing (hosted CA is internal). Target: back up in < 4 h, data loss ≤ 5 min.
- **One real drill now**, on a scratch VPS with real B2 data — owner's OK first (real patient data leaves prod
  onto a temp host; `down -v` + `shred` after). Log the row in `RESTORE-DRILL.md`. Then quarterly + after every
  migration batch, as the doc already says.
- `DEPLOY.md:139-173` (MinIO tar.gz restore, Auth0) — mark retired, point to `RESTORE-DRILL.md`.

---

## Part 5 — Whoever breaks into the server cannot delete the backups

`follow-up/security-remediation-outstanding.md` § 2.8: the server holds full read-write B2 keys plus a
`wal-g delete` path, so ransomware deletes the backups with keys it already found.

- B2 **Object Lock** on both buckets, lock period **shorter than** the retention window (so `wal-g delete
  retain 7` and the attic prune only ever touch unlocked objects) — e.g. lock 6 days, retain 7 bases / 14 days.
  Check B2's rules for enabling lock on an existing bucket before planning the numbers.
- Separate B2 application keys per sidecar, scoped to one bucket each.

**Open — owner + lawyer, not code:** both buckets are in B2 `us-east-005` (USA) while the VPS is in London —
two countries outside Tunisia, no INPDP transfer authorisation (memory: compliance-status-tunisia). B2 has an EU
region; moving buckets is a Part 1 env change once decided.

---

## Part 6 — Offline (LAN) clinics

**Owned here.** The parallel « cloud primary + clinic-PC live copy » design (option 3, 2026-10-06) covers *new*
hosted clinics with a PC replica; it does **not** cover existing SelfHostedLan installs, so their backups stay
in this blueprint.

| Gap | Change |
|---|---|
| Scheduled backup destination cannot be changed in-app | `SetBackupScheduleCommand` gains `Destination` — **tri-state**: omitted = unchanged, `null` = default. `web/components/backup-settings.tsx` gets the field |
| Same-disk backup is a one-time installer warning | `BackupJob` raises a standing bell alert « La sauvegarde est sur le même disque que les données » until the destination is another volume (reuse the same-volume check in `PgDumpBackupService.cs:160-170`) |
| Nothing leaves the server PC by default | Desktop auto-copy of the archive to the **poste** PC: on by default for the poste installer role; its file handling (write, rename, prune, ACL) gets the tests `features/clinic-archive-auto-copy/progress.md` says are missing |
| An archive cannot be restored into a fresh install | `restore-archive <zip>` console verb on SelfHostedLan, **empty database only**, reusing `RestoreClinicFromArchiveCommand` (the vendor's re-create-at-original-id path) |
| README says backing up `.local` saves the key ring | It cannot: DPAPI machine-scope. Fix `packaging/README.md:355-361`; document `reset-user-totp` + re-enter SMS creds + reconnect Google after a new-PC restore |
| Installer default `{app}\api\Backups` vs `LegacyBackupRelocation` moving it on every start | Verify on a real install first; then one default (`LocalInstallPaths.DefaultBackupRoot`) |
| L4 rehearsal never done | backup → wipe → `restore-backup` → sign in → counts, on a spare Windows VM; log it in `packaging/README.md` checklist |

---

## Blast radius (write the full table before the first edit — `regression-safety.md`)

| Touched | Also consumed by | Verdict |
|---|---|---|
| `docker-compose.prod.yml` backup service | `docker-compose.hosted.yml` `extends` it | must re-test (CI roundtrip + deploy dry-run) |
| `backup.sh` / `entrypoint.sh` | `RESTORE-DRILL.md`, `check-keyring.sh` | must re-test |
| `DeploymentProfile` new capability | `DeploymentProfileTests`, `Program.cs` job registration | must re-test |
| `SetBackupScheduleCommand` (tri-state field) | `backup-settings.tsx`, `BackupController` | must re-test — omitted key must not clear |
| Desktop auto-copy default | poste installer role, `ArchiveCopyService` | must re-test |

## Test strategy

| Rule lives in | Test |
|---|---|
| `backup.sh`, mirror, ring, plaintext sweep, remote refusal | CI `backup-roundtrip` (real postgres + 2 minio) |
| restore path | `restore-drill.sh` in the same job; one real drill on a scratch VPS |
| `GetBackupHealthQuery` verdicts (fresh / stale / failing / file missing / WAL failures grew) | unit tests |
| `BackupHealthJob` (sends while not Ok, not when Ok, not when SMTP absent) | unit tests |
| capability values | `DeploymentProfileTests` |
| `SetBackupScheduleCommand` tri-state | unit tests: omitted / null / value |
| `restore-archive` verb (empty DB only, refuses a live one) | unit + CI `local-mode` job |
| console strip | browser pass at 320/1440 |
