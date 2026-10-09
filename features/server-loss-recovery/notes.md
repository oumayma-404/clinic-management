# Server loss recovery — what shipped (Phase A), and how it reaches prod (Phase B)

**Question:** if the server dies today, what do the clinics lose? Measured on 2026-10-06 (read-only checks on the
live VPS): the database was safe to ~5 min (WAL-G → Backblaze), **every stored file had no off-site copy**, the
**key ring was backed up by nothing**, no restore had ever been run, and nothing alerted anybody. Phase A fixes all
of that in code and proves it on fake data. **Nothing in Phase A touched production.**

Branch `feature/server-loss-recovery` (worktree `.claude/worktrees/server-loss-recovery`). Blueprint:
`blueprint.md`. Blast radius: `blast-radius.md`.

---

## What was broken, exactly

| Defect | Cause | Since |
|---|---|---|
| Nightly run aborted every night | `backup.sh:94` called `mirror-objects.sh` with literal `\n` instead of line continuations, so `sh` passed `n` as the object store; the mirror wrote its manifest over the mirror directory's path (a 0-byte FILE at `/backups/objects`) and the next `cp` killed the run before encryption and upload | 2026-09-03 (`383154aa`) |
| No nightly copy ever left the VPS | `BACKUP_REMOTE` named an rclone remote defined in a gitignored `rclone/rclone.conf` the bring-up never wrote | first deploy |
| Plaintext dumps piling up on the VPS | every aborted run left `db-*.dump` unencrypted and was never pruned (prune runs at the end) | 2026-09-03 |
| Key ring backed up by nothing | three docs still said « the ring is never mounted into the sidecar » — the pre-FR-3.1 rule KEY-CUSTODY.md had already retired | always |
| PITR restore steps could not work | `DEPLOY.md` ran the restore with `--env-file .env`; wal-g reads `AWS_*`, `.env` holds `WALG_S3_*` | always |
| On-demand backup command did nothing | `run --rm backup /usr/local/bin/backup.sh` — the entrypoint ignores arguments and starts crond | always |
| Nobody told | no healthcheck, no alert, failures only in `docker logs` | always |
| LAN default backup folder defeated itself | installer default `{app}\api\Backups` is the folder `LegacyBackupRelocation` moves out at every start (it breaks PDF generation), so default installs scattered backups into `legacy-install-dir-N` folders retention never prunes | L4b |

---

## What changed

### Nightly run (`deploy/backup/`)
- `backup.sh`: the line fixed; the leftover 0-byte file self-heals (that exact shape only — anything else at
  that path is a refusal); **no remote = refusal**, never « kept locally »; the remote is listed before anything
  is dumped; the **key ring is archived** (`keyring-ring-<ts>.tar.age`) and the run refuses if it does not hold
  the active key the marker names; nothing but `.age` (and the key-id stamp) may be uploaded; plaintext dumps from
  earlier failed runs are swept **only after** tonight's encrypted copy has reached the remote; every run writes
  `/status/backup.status` (`outcome`, `stage`, `finished`, `lastSuccess`); a failed run removes its directory whole.
- The remote is configured from `.env` (`BACKUP_S3_*` → `RCLONE_CONFIG_OFFSITE_*`, `rclone --config /dev/null`);
  `deploy/rclone/` is gone.
- `entrypoint.sh` runs `backup.sh --preflight` at container start and **records a failure in the status file but
  keeps the container up** — `deploy-hosted.yml` uses `up --wait`, so a sidecar in a restart loop would fail every
  deploy over a backup setting.
- `mirror-objects.sh`: skips `.minio.sys/tmp` and `multipart` (transient; a file vanishing between listing and
  encryption failed the whole night at random) and leaves a vanished file out of the manifest.
- `restore-drill.sh` (new): fetch + decrypt + **prove the run is whole** — dump parses, every stored object present
  at its recorded size, archived ring holds the stamp's active key. Exit 0 / 1 / 2.
- `keyring-id.sh` (new): the guid ↔ marker-id rule, shared by both scripts.

### Proof — `deploy/backup/test/roundtrip.sh` + `.github/workflows/backup.yml`
TLS PostgreSQL (the real `deploy/postgres` image, archiving WAL) + TLS MinIO + a second MinIO as the off-site
bucket, fake data only, isolated project, no ports. **42 checks**: back up → restore into empty servers →
row counts, file checksums and key ring identical; incremental second night; a deleted file reaches the attic;
failures stay loud and leave nothing behind; the drill refuses a damaged copy and a wrong key; **the database
rebuilt from the WAL stream alone matches, including rows written after the last base backup**.
Verified red: with the original `\n` line restored, 23 checks fail. Shellcheck flags that line as SC1012.

### Alert (`api/`, `console/`)
- Capability `MonitorsSidecarBackups` (HostedMultiTenant only).
- `HostBackupStatusReader` reads `backup.status` (read-only `backup_status` volume) and `pg_stat_archiver`.
  ⚠️ Not a status file from the `pitr` sidecar: it runs the database's own image, so changing its script would
  restart the live database on the next deploy (tried, reverted — `blast-radius.md` row 10).
- `BackupHealthRules`: `Ok` / `Stale` / `Failing`; a missing record is never `Ok`.
- `GET /api/platform/backup-health` (console only, 404 elsewhere) + a status strip on the console's portfolio page.
- `BackupHealthJob`, daily 06:00 UTC: e-mails `BACKUP_ALERT_EMAIL`, else every active console account, every
  morning a copy is not healthy.
- `DataResidencyAssurance` now checks `BACKUP_S3_ENDPOINT` like the PITR endpoint.

### Clinic PC (`packaging/`)
- Installer default backup folder → `C:\ProgramData\ClinicManagement\Backups`.
- README: a copy of `.local` does not survive a new PC (DPAPI) — what to redo after a restore there.

### Verified
| Gate | Result |
|---|---|
| API unit suite (unfiltered) | 4 954 passed · 0 failed |
| Round trip (local Docker) | 42 / 42 |
| Shellcheck (all deploy scripts, warning level) | clean |
| Console: tsc · check:responsive · build | green |
| Console eye pass: 5 states × 320/390/820/1180/1440 | clean, no horizontal scroll |
| `docker compose config`: only `backup` changes in Parts 1–2 | confirmed |

### On merge with `feature/clinic-pc-copy`
That branch adds `IPlatformAccountRepository.GetActiveEmailsAsync` byte-identical to this one, and
`Application/Features/Platform/VendorAlertRecipients.ResolveAsync` — the same « `Backup:AlertEmail`, else every
active console account » rule as `BackupHealthJob.RecipientsAsync`. After the merge, switch the job to it and
delete the private copy, so the recipient rule lives once.

### Not done / not exercised
- **Installer compile** — Inno Setup is not installed here; CI's `installers` job compiles it.
- **Clinic PC, still open** (needs a decision): an in-app « dossier de sauvegarde » setting (a DB column + UI), a
  standing bell alert when backups share the data's disk (today only the server log says so for scheduled runs),
  the poste auto-copy on by default (never exercised on real PCs), restoring an archive into a fresh install.
- **No real restore drill** — needs a temporary server and real data (Phase B4).

---

## Phase B — prod, one step at a time, each with the owner's explicit yes

⚠️ Real clinics are live. Read-only checks first; nothing below has been run.

### B0 — the owner, by hand
1. Copy off the server: `.env` (holds `WALG_LIBSODIUM_KEY`), `secrets/keyring-certificate.pfx` +
   `secrets/keyring-certificate-password`, `secrets/audit-chain-key`. Fill `deploy/KEY-CUSTODY.md`'s table.
2. Create a **second** Backblaze bucket for the nightly copy (not the PITR one) and an application key limited to
   it. Pick the region knowing both buckets are currently in the USA (`us-east-005`).
3. If nobody holds the age private key: `age-keygen -o backup-identity.txt` on a laptop, keep it off the server.

### B1 — the backup container only (the app is not restarted)
```bash
# on the VPS, in /opt/clinic-management/deploy
# .env: BACKUP_REMOTE=offsite:<bucket>, BACKUP_S3_ENDPOINT/_REGION/_ACCESS_KEY/_SECRET_KEY, BACKUP_AGE_RECIPIENT=<age1…>
# ship deploy/ from the branch (the workflow's own tar step, by hand)
export CLINIC_IMAGE_TAG=<the live tag>          # or every service falsely reads « Recreate »
C="docker compose -f docker-compose.hosted.yml -f docker-compose.registry.yml"
$C build backup
$C up -d --no-deps --no-build backup --dry-run   # MUST list `backup` and nothing else
$C up -d --no-deps --no-build backup
$C logs backup | tail                             # « preflight: … reachable », « scheduled: »
$C exec backup /usr/local/bin/backup.sh           # one run now; ~1 min
$C exec backup cat /status/backup.status          # outcome=succeeded
```
Risk: a wrong command recreating more than `backup` — the dry run is the guard. The first run uploads every
stored file once (~91 MB) and deletes the ~40 plaintext dumps after the upload succeeds.

### B2 — the alert (restarts the app ~1 min, so at night)
Pre-check (read-only): if `RESIDENCY_ALLOWED_EGRESS_HOSTS_*` is non-empty, the new bucket's host must be in it, or
**the API refuses to start**. Then the normal `deploy-hosted.yml` run from this branch's ref. Verify the console
strip and that `check-backup-health` is registered.

### B3 — Backblaze Object Lock (owner, in Backblaze), lock shorter than the retention window.

### B4 — one real restore drill on a temporary server, then `down -v` + `shred` (`deploy/RESTORE-DRILL.md`).
