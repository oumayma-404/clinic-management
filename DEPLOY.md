# Cloud Deployment — Single-VPS Runbook (Cloud mode + Auth0)

Run the existing app (Cloud mode: Auth0 + PostgreSQL + MinIO) on one internet-reachable Linux
VPS via `docker compose`, behind **Caddy** with automatic Let's Encrypt TLS. Every device — the
doctor's laptop, the secretary's Android tablet, the doctor from home — just opens
`https://<your-domain>` in a browser. No per-device setup, no CA import, no IP.

This replaces the "no always-on machine at the clinic" problem: the always-on host is the VPS.

> This is **not** the offline/LAN build (that stays `packaging/` + Local mode). This is Cloud mode
> packaged for a public server. All deployment artifacts live in [`deploy/`](deploy/).

---

## Architecture

```
  Browser (laptop / tablet / phone)
        │  https://<domain>   (443, valid Let's Encrypt cert)
        ▼
  ┌───────────────────────────── VPS ─────────────────────────────┐
  │  caddy  ── the ONLY published ports: 80, 443                   │
  │    ├─ /api/*  ─────────────▶ api  :5000   (.NET 8)             │
  │    └─ /*      ─────────────▶ web  :3000   (Next.js)            │
  │                                                                │
  │  postgres :5432   minio :9000/9001   ── internal network only  │
  │  backup   (nightly pg_dump + MinIO archive ▶ off-server)       │
  │  pitr     (WAL-G: continuous WAL + base backups ▶ off-site S3) │
  └────────────────────────────────────────────────────────────────┘
```

Only Caddy is reachable from the internet. Postgres, MinIO, the API and the web app have **no
published ports** — they talk over a private Docker network. The frontend calls the API
**same-origin** at `https://<domain>/api` (the web image bakes the relative `/api`), so TLS
terminates once at Caddy and no server IP/domain is baked into any image.

---

## Prerequisites (operator)

1. **A VPS** running Linux with Docker Engine + the Compose plugin installed.
2. **A domain name** you control, with an **A record** pointing at the VPS's public IP.
3. **Ports 80 and 443 open** to the internet on the VPS firewall/security group.
   (ACME/Let's Encrypt validation needs 80/443 reachable *before* first boot — see Troubleshooting.)
4. **An Auth0 production tenant** (or app) — see `AUTH0_SETUP.md`. You need a Regular Web App
   (client id/secret) and, for user-management features, a Machine-to-Machine app authorized for
   the Management API.
5. **A Google Cloud OAuth client** for Calendar (see `GOOGLE_CALENDAR_SETUP.md`) if you use sync.
6. **An off-server backup destination** reachable by [rclone](https://rclone.org) (S3, Backblaze
   B2, SFTP, Google Drive, …).

The repo produces the compose stack, reverse proxy, backup job, and secret externalization.
Provisioning the VPS/DNS/Auth0/Google and running the commands below is operator work.

---

## 1. Get the code onto the VPS

```bash
git clone <this-repo> clinic-management
cd clinic-management/deploy
```

## 2. Fill in secrets

```bash
cp .env.example .env
$EDITOR .env          # fill every value; see comments in the file
```

`.env` is **gitignored** and is the single source of every secret. `docker compose` loads it
automatically. Nothing sensitive lives in a tracked file or an image.

**Generate strong values:**

```bash
openssl rand -base64 24   # POSTGRES_PASSWORD, MINIO_ROOT_PASSWORD
openssl rand -hex 32      # AUTH0_SECRET
```

> **Rotate everything that was ever committed** before going live (see the rotation checklist
> below). Never reuse `clinic_password` or `minioadmin`.

## 3. Configure Auth0 for the production domain

In the Auth0 dashboard, on your Regular Web App (per `AUTH0_SETUP.md`):

- **Allowed Callback URLs:** `https://<domain>/auth/callback`
- **Allowed Logout URLs:** `https://<domain>`
- **Allowed Web Origins:** `https://<domain>`

Set `AUTH0_DOMAIN`, `AUTH0_AUDIENCE`, `AUTH0_CLIENT_ID`, `AUTH0_CLIENT_SECRET`, `AUTH0_SECRET`
(and the `AUTH0_MGMT_*` M2M pair) in `.env`. `DOMAIN` drives `AUTH0_BASE_URL`/`APP_BASE_URL`
and the backend `FrontendUrl` automatically.

## 4. (Optional) Configure Google Calendar redirect

If using Calendar sync, add `https://<domain>/api/googlecalendar/callback` as an authorized
redirect URI on your Google OAuth client, and put the **rotated** client id/secret/refresh token
in `.env`.

## 5. Configure off-server backup

```bash
$EDITOR deploy/rclone/rclone.conf     # define the `offsite` remote (see rclone/.gitkeep)
```

Set `BACKUP_REMOTE=offsite:<bucket-or-path>` in `.env`. `rclone.conf` is gitignored.

## 6. Bring the stack up

```bash
docker compose -f docker-compose.prod.yml up -d --build
```

On first boot:
- The API auto-applies **EF migrations** against the empty database and comes up ready (AC-8).
- MinIO auto-creates the `clinic-files` bucket on first upload.
- Caddy obtains a Let's Encrypt certificate for `<domain>` (needs 80/443 reachable + DNS live).

Watch logs:

```bash
docker compose -f docker-compose.prod.yml logs -f caddy api
```

## 7. Verify

- `https://<domain>` loads with a valid padlock (no cert warning) on a laptop **and** an Android
  tablet — no per-device setup.
- Log in via Auth0; you reach the dashboard.
- From outside the VPS, confirm only 80/443 answer:
  ```bash
  nmap -Pn <domain>          # 5432 / 9000 / 9001 / 5000 / 3000 must NOT be open
  ```

---

## Backup & restore

**Nightly (automatic):** the `backup` container runs `BACKUP_CRON` (default `0 2 * * *`),
producing a custom-format `pg_dump` + a `tar.gz` of the MinIO data, and uploading both to
`BACKUP_REMOTE`. Local staged copies live in the `backups` volume and are pruned after
`BACKUP_RETENTION_DAYS`.

**On demand:**

```bash
docker compose -f docker-compose.prod.yml run --rm backup /usr/local/bin/backup.sh
```

The script fails loud (non-zero exit) on any error — never a silent partial.

**Restore the database** (into a running stack):

```bash
# copy a dump back onto the VPS, then:
cat db-<TS>.dump | docker compose -f docker-compose.prod.yml exec -T postgres \
  pg_restore --clean --if-exists --no-owner --no-privileges \
  -U "$POSTGRES_USER" -d "$POSTGRES_DB"
```

**Restore MinIO objects:** stop the stack, extract the archive into the `minio_data` volume, restart.

```bash
docker compose -f docker-compose.prod.yml down
docker run --rm -v clinic-management_minio_data:/data -v "$PWD":/backup alpine \
  sh -c "tar xzf /backup/minio-<TS>.tar.gz -C /data"
docker compose -f docker-compose.prod.yml up -d
```

> **Test your restore.** After the first backup, do a dry-run `pg_restore` into a throwaway
> database to confirm the dump is loadable (schema + data).

---

## Point-in-Time Recovery (PITR)

The nightly `pg_dump` above caps worst-case loss at ~24 h and can only rewind to a dump. **PITR**
closes that gap: Postgres continuously ships its write-ahead log (WAL) to a **dedicated off-site
S3-compatible bucket** via **WAL-G**, and a `pitr` sidecar takes periodic physical base backups —
so you can restore to *any second* (e.g. the instant before an accidental delete). PITR
**complements** the nightly dump; the dump keeps running unchanged.

**How it fits the stack:**

- `postgres` runs the custom image (`deploy/postgres/Dockerfile` = `postgres:16` + `wal-g`) with
  `archive_mode=on` and `archive_command=wal-g wal-push %p`. Every completed WAL segment is pushed
  off-site. `archive_timeout` (`PITR_ARCHIVE_TIMEOUT`, default 300 s) forces a WAL switch so a
  low-write clinic still ships recent changes promptly.
- `pitr` (same image) takes a physical base backup on `PITR_BASE_BACKUP_CRON` (default 01:00 daily)
  and prunes to `PITR_RETAIN_BASE_BACKUPS` (default 7). On first boot it takes an initial base
  backup if none exists yet, so PITR is usable immediately.

### Enable it

1. **Provision a dedicated off-site S3 bucket** — on a provider **other than this VPS** (Backblaze
   B2, Wasabi, AWS S3, an off-VPS MinIO…), so it survives total server loss. It must be **distinct**
   from the on-VPS MinIO and from the `pg_dump` `BACKUP_REMOTE`.
2. **Fill the `WALG_*` / `PITR_*` keys in `.env`** (see `.env.example`): `WALG_S3_PREFIX` (drives
   both WAL and base backups — keep it one prefix), `WALG_S3_ENDPOINT` (empty for real AWS S3),
   `WALG_S3_REGION`, `WALG_S3_ACCESS_KEY`, `WALG_S3_SECRET_KEY`, `WALG_S3_FORCE_PATH_STYLE`
   (`true` for most S3-compatible providers, `false` for real AWS S3).
3. **Bring the stack up with a build** so the custom image is built:
   ```bash
   docker compose -f docker-compose.prod.yml up -d --build
   ```
4. **Verify archiving** (after some write activity):
   ```bash
   docker compose -f docker-compose.prod.yml logs -f postgres | grep -i archiv   # no repeated failures
   docker compose -f docker-compose.prod.yml run --rm --no-deps --entrypoint wal-g pitr backup-list
   docker compose -f docker-compose.prod.yml run --rm --no-deps --entrypoint wal-g pitr wal-verify timeline
   ```
   `backup-list` shows at least the initial base backup; `wal-verify` confirms an unbroken WAL chain.

**On-demand base backup:**

```bash
docker compose -f docker-compose.prod.yml run --rm --entrypoint /usr/local/bin/pitr-backup.sh pitr
```

### Restore to a point in time

Restores go into a **throwaway instance on a fresh volume** — never over the live `PGDATA` — so you
can inspect the result before deciding how to cut over.

```bash
cd deploy    # .env here supplies the WALG_S3_* creds (same off-site prefix)

# 1. A fresh, empty volume for the restore target.
docker volume create clinic_pitr_restore

# 2. List base backups, then fetch the one that PRECEDES your target time. For the headline
#    case (restore to just before an accidental delete), the delete may be OLDER than the most
#    recent nightly base backup — e.g. deleted 10:00 yesterday, base backup ran 01:00 today — so
#    LATEST would be AFTER your target and Postgres refuses recovery ("recovery_target_time is
#    before backup end"). Pick the specific base_... name from backup-list that ends before your
#    target time and pass it explicitly:
docker compose -f docker-compose.prod.yml run --rm --no-deps --entrypoint wal-g pitr backup-list
docker compose -f docker-compose.prod.yml run --rm --no-deps \
  -v clinic_pitr_restore:/restore --entrypoint wal-g pitr \
  backup-fetch /restore base_000000010000000000000005   # ← the base that PRECEDES your target
#    (LATEST is a valid shortcut ONLY when your target time is newer than the most recent base backup.)

# 3. Configure recovery to the exact instant; Postgres will replay WAL from the off-site
#    archive (restore_command) up to the target, then promote.
docker compose -f docker-compose.prod.yml run --rm --no-deps \
  -v clinic_pitr_restore:/restore --entrypoint sh pitr -c '
    {
      echo "restore_command = '\''wal-g wal-fetch %f %p'\''"
      echo "recovery_target_time = '\''2026-07-13 14:29:59+00'\''"
      echo "recovery_target_action = '\''promote'\''"
    } >> /restore/postgresql.auto.conf
    touch /restore/recovery.signal
  '

# 4. Start a throwaway Postgres on the restored volume (needs .env for wal-fetch to reach S3).
docker run --rm --name clinic-pitr-verify --env-file .env \
  -v clinic_pitr_restore:/var/lib/postgresql/data \
  clinic-postgres-pitr:16 postgres
#    Watch the logs for: "recovery stopping before ..." then "database system is ready to accept
#    connections". In another shell:

# 5. Verify — data written AFTER the target time is absent, data before it is present.
docker exec -it clinic-pitr-verify \
  psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "select max(created_at) from appointments;"

# 6. Tear down when satisfied.
docker rm -f clinic-pitr-verify
docker volume rm clinic_pitr_restore
```

### PITR operational notes

- **Off-site bucket unreachable for a long stretch** — Postgres cannot recycle WAL until
  `archive_command` succeeds, so `pg_wal` grows and can eventually fill the disk. Failures are
  **not silent**: they appear in `docker compose logs postgres` (Postgres logs and retries the
  failed push). Watch that log / disk usage and fix credentials or reachability promptly.
- **WAL alone can't restore** — PITR is only in effect *after the first base backup* completes
  (the sidecar takes one on first boot). Confirm with `wal-g backup-list`.
- **Shared prefix is mandatory** — base backups and WAL must live under the same `WALG_S3_PREFIX`
  or WAL-G can't correlate the chain. One key drives both.
- **Never restore in place** — always restore into a fresh/throwaway volume as above; do not point
  recovery at a running primary's `PGDATA`.
- **Migrating an existing volume from the alpine image** — the PITR image is `postgres:16`
  (Debian, glibc) rather than `postgres:16-alpine` (musl). A fresh deploy is unaffected (it
  `initdb`s once under Debian). But if you reuse a `postgres_data` volume that was first created
  under the alpine image, glibc and musl sort text differently, so existing indexes are ordered by
  the old collation — run `REINDEX DATABASE "<POSTGRES_DB>";` once after the switch to avoid wrong
  range/`ORDER BY` results and unique-constraint mis-enforcement.

---

## Secret-rotation checklist (do this before go-live)

Everything below was present in tracked config / git history at some point. Rotation — not a
history rewrite — is the fix here (git-history scrubbing is an optional separate follow-up).

- [ ] **Google** — regenerate the OAuth **client secret**; re-consent to mint a fresh **refresh
      token**; put both in `.env` (`GOOGLE_CLIENT_SECRET`, `GOOGLE_REFRESH_TOKEN`).
- [ ] **HuggingFace** — revoke the old token, create a new one (`HUGGINGFACE_API_KEY`).
- [ ] **Auth0** — rotate the web app client secret and the M2M client secret; generate a fresh
      `AUTH0_SECRET`.
- [ ] **PostgreSQL** — strong `POSTGRES_PASSWORD` (never `clinic_password`).
- [ ] **MinIO** — strong `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` (never `minioadmin`).

---

## Operations

| Task | Command (from `deploy/`) |
|------|--------------------------|
| Start / rebuild | `docker compose -f docker-compose.prod.yml up -d --build` |
| Stop | `docker compose -f docker-compose.prod.yml down` |
| Logs | `docker compose -f docker-compose.prod.yml logs -f <service>` |
| Update to new code | `git pull && docker compose -f docker-compose.prod.yml up -d --build` |
| On-demand backup | `docker compose -f docker-compose.prod.yml run --rm backup /usr/local/bin/backup.sh` |
| On-demand PITR base backup | `docker compose -f docker-compose.prod.yml run --rm --entrypoint /usr/local/bin/pitr-backup.sh pitr` |
| List PITR backups | `docker compose -f docker-compose.prod.yml run --rm --no-deps --entrypoint wal-g pitr backup-list` |

## Troubleshooting

- **Caddy can't get a certificate** — the domain's A record must resolve to the VPS and 80/443
  must be reachable from the internet *before* Caddy starts. Check `logs -f caddy`. DNS
  propagation can take a while.
- **Auth0 login loops / "callback mismatch"** — the callback/logout/web-origin URLs in Auth0 must
  exactly match `https://<domain>` (scheme + host, no trailing slash surprises), and `DOMAIN` in
  `.env` must equal the real domain (drives `AUTH0_BASE_URL`).
- **API 500 on startup** — usually the DB isn't ready or `ConnectionStrings` is wrong; the API
  depends on a healthy `postgres`. Check `logs -f api`.
- **Backup "kept LOCAL ONLY" warning** — `BACKUP_REMOTE` is empty or `rclone.conf` is missing the
  named remote.

---

## Out of scope (by design)

- Offline/LAN packaging (Local mode) — unchanged; see `packaging/`.
- Managed Postgres / managed S3 — MinIO stays containerized here.
- CI/CD pipelines.
- Rewriting git history to purge already-committed secrets — rotation is the fix.
