# VPS bring-up — exact steps

HostedMultiTenant on a fresh Ubuntu 26.04 VPS. Run top to bottom. Reference detail lives in
`README.md` (§ Déploiement depuis GitHub), `.env.hosted.example`, `KEY-CUSTODY.md`.

Placeholders: `<host>` = VPS IP, `<domain>` = public domain.

---

## No domain yet? Everything but the deploy can be done now

`DOMAIN` is consumed **only at runtime** — Caddy's env injection, plus the API's `FrontendUrl` and
`GoogleCalendar__RedirectUri`. The web bundle is built with `NEXT_PUBLIC_API_URL: /api` (relative), so the
domain is **not baked into any image**. Setting it later is an `.env` edit and `up -d`, never a rebuild.

- **Do now:** phases 2, 3, 4 (all but `DOMAIN` / `ACME_EMAIL`), 5, 6 (all but `DEPLOY_DOMAIN`), and the phase-0 externals.
- **Blocked:** phase 7 onward — Caddy requests an ACME cert on first boot and the workflow polls `https://<domain>/health`.
- **Do not** substitute `nip.io` / `sslip.io`: both are on the Public Suffix List and Let's Encrypt rate-limits them, risking your quota before the real domain is live.

**Pre-flight while you wait.** Ship `deploy/` by hand once `.env` and `secrets/` exist:

```bash
# from your machine
tar -czf - --exclude='.env' --exclude='secrets' --exclude='rclone/rclone.conf' -C deploy . \
  | ssh deploy@<host> "tar -xzf - -C /opt/clinic-management/deploy"
```

```bash
# on the server — resolves every interpolation, errors on a missing secret file, starts nothing
cd /opt/clinic-management/deploy
docker compose -f docker-compose.hosted.yml -f docker-compose.registry.yml config >/dev/null && echo OK
```

---

## 0. Before the VPS arrives

- [ ] Buy/choose `<domain>`. Get DNS panel access.
- [ ] Get an SMTP relay on **port 587 + STARTTLS** (host, username, password). Port 25 is blocked on this VPS.
- [ ] Create an **off-site S3 bucket** for PITR. Must not be the on-VPS MinIO. Note endpoint, region, access key, secret key.
- [ ] Create a **second** off-site destination for the nightly dump (rclone remote — S3, B2, SFTP, anything rclone speaks).
- [ ] Install `age` locally, generate the backup keypair:
  ```bash
  age-keygen -o backup-identity.txt        # keep this file OFF the server
  grep 'public key' backup-identity.txt    # → age1...  (this is BACKUP_AGE_RECIPIENT)
  ```
- [ ] Generate the deploy SSH key on your own machine:
  ```bash
  ssh-keygen -t ed25519 -f clinic-deploy -C "github-actions"
  ```
- [ ] Decide subscription prices (`SUBSCRIPTION_*_MONTHLY_DT` / `_ANNUAL_DT`), payment instructions, contact email + phone.
- [ ] Push `feature/windows-desktop-app` and merge to `main`. **The deploy workflow builds whatever ref you run it on** — main must contain this code.

---

## 1. DNS — do this first, before any `up -d`

- [ ] `A` record: `<domain>` → `<host>`
- [ ] Verify from elsewhere: `dig +short <domain>` returns `<host>`
- [ ] Ports 80 and 443 open inbound

Caddy requests a Let's Encrypt cert on first boot. Wrong or missing DNS = failed boot.

---

## 2. Server prep (SSH as root)

```bash
apt-get update && apt-get install -y docker.io docker-compose-v2
useradd -m -s /bin/bash deploy && usermod -aG docker deploy
install -d -o deploy -g deploy /opt/clinic-management/deploy
install -d -o deploy -g deploy -m 700 /opt/clinic-management/deploy/secrets
install -d -o deploy -g deploy /opt/clinic-management/deploy/rclone
```

- [ ] Authorise the deploy key:
  ```bash
  install -d -o deploy -g deploy -m 700 ~deploy/.ssh
  cat >> ~deploy/.ssh/authorized_keys   # paste clinic-deploy.pub
  chown deploy:deploy ~deploy/.ssh/authorized_keys && chmod 600 ~deploy/.ssh/authorized_keys
  ```
- [ ] Confirm `ssh deploy@<host>` works and `docker ps` runs without sudo.
- [ ] From **your own** machine (not a runner), capture the host key:
  ```bash
  ssh-keyscan -t ed25519 <host>
  ```

---

## 3. Secrets — 6 files, all mandatory, all non-empty

As `deploy`, in `/opt/clinic-management/deploy/secrets/`:

```bash
cd /opt/clinic-management/deploy/secrets

openssl rand -base64 48 > auth-local-signing-key
openssl rand -base64 48 > audit-chain-key
openssl rand -base64 48 > console-signing-key      # MUST differ from auth-local-signing-key

# key-ring certificate (PKCS#12) + its password
openssl rand -base64 24 > keyring-certificate-password
openssl req -x509 -newkey rsa:4096 -nodes -keyout k.pem -out k.crt -days 3650 -subj "/CN=clinic-keyring"
openssl pkcs12 -export -inkey k.pem -in k.crt -out keyring-certificate.pfx \
  -passout file:keyring-certificate-password
rm k.pem k.crt

# required even with no Google Calendar — an empty file throws at startup
echo "unused" > google-client-secret

# ⚠️ OWNERSHIP IS NOT COSMETIC — the API container runs as uid 1654 and reads these through
#    /run/secrets, which preserves the host file's owner and mode. `chmod 600` as `deploy` leaves
#    them readable by uid 1001 ONLY, and the API then exits at startup on the first one it opens:
#      « GoogleCalendar__ClientSecret_FILE désigne « /run/secrets/… », illisible … Permission denied »
#    The container dies with code 139 in a restart loop, /health answers 502, and a deploy fails its
#    health gate five minutes later — with nothing in the workflow log naming a permission.
#    `deploy` cannot chown to another uid, so borrow root from the docker group it is already in:
docker run --rm -v /opt/clinic-management/deploy/secrets:/s alpine \
  sh -c 'chown 1654:1001 /s/* && chmod 0440 /s/*'
cd ..
```

⚠️ **This applies every time a secret file is REPLACED, not just at bring-up.** A file recreated later
(`rm` + `nano` to set the real Google client secret, say) comes back owned by `deploy` and takes the
whole API down on its next restart — which may be hours later, on a deploy nobody connects to the edit.
Re-run the `chown`/`chmod` above after touching anything in `secrets/`, and check with `ls -ln secrets/`:
every file must read `1654 1001`. One row that does not match is the outage.

- [ ] `ls secrets/` shows exactly 6 files, none 0 bytes.
- [ ] `ls -ln secrets/` shows `1654 1001` and mode `-r--r-----` on **all six**.
- [ ] **Copy `keyring-certificate.pfx`, `keyring-certificate-password` and `audit-chain-key` off the server now.** Store them separately from any database backup — one archive holding both the ciphertext and its key protects nothing. See `KEY-CUSTODY.md`.

---

## 4. `.env`

- [ ] Get the template onto the server (from your machine):
  ```bash
  scp deploy/.env.hosted.example deploy@<host>:/opt/clinic-management/deploy/.env
  ```
- [ ] Edit `/opt/clinic-management/deploy/.env`. Every `CHANGE_ME` must go:

| Key | Value |
|---|---|
| `DOMAIN` | `<domain>` |
| `ACME_EMAIL` | your address |
| `POSTGRES_PASSWORD` | `openssl rand -base64 24` |
| `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` | `openssl rand -base64 24` — never `minioadmin` |
| `SMTP_SERVER` / `SMTP_USERNAME` / `SMTP_PASSWORD` | the 587 relay. Leave **empty** rather than a placeholder host |
| `SMTP_FROM_ADDRESS` / `SMTP_FROM_NAME` | if the sender differs from the username |
| `BACKUP_AGE_RECIPIENT` | the `age1...` public key from step 0 |
| `WALG_LIBSODIUM_KEY` | `openssl rand -hex 32` |
| `WALG_S3_PREFIX` / `_ENDPOINT` / `_REGION` / `_ACCESS_KEY` / `_SECRET_KEY` | the PITR bucket |
| `BACKUP_REMOTE` | `offsite:<bucket>` — matches the rclone remote name in step 5 |
| `RESIDENCY_ALLOWED_EGRESS_HOSTS_0` / `_1` | **hostnames of the two off-site destinations** |
| `SUBSCRIPTION_*` | prices (write `120.500`, never `120,500`), payment instructions, contact email + phone |
| `MESSAGING_CONTACT_EMAIL` / `_PHONE` | how a cabinet out of WhatsApp allowance reaches you |

- [ ] `chmod 600 .env`
- [ ] `grep CHANGE_ME .env` returns nothing.

⚠️ The API **refuses to start** if it can see an egress destination not listed in `RESIDENCY_ALLOWED_EGRESS_HOSTS_*`. Leave them empty and it starts but warns on every boot.

⚠️ Leave `Deployment__Profile`, `AUTH_MODE`, `DataProtection__KeyRingPath`, `API_INTERNAL_URL`, `AUTH_COOKIE_SECURE` alone — they are literals in the compose file, not operator-tunable.

---

## 5. rclone

- [ ] Write `/opt/clinic-management/deploy/rclone/rclone.conf` with a remote **named `offsite`** matching `BACKUP_REMOTE`.
- [ ] Generate it interactively elsewhere (`rclone config`) and copy the stanza in, or hand-write it.
- [ ] `chmod 600 rclone/rclone.conf`

---

## 6. GitHub — 2 secrets, 4 variables

Settings → Secrets and variables → Actions.

| Kind | Name | Value |
|---|---|---|
| Secret | `VPS_SSH_KEY` | private half of `clinic-deploy`, whole file including header lines |
| Secret | `VPS_SSH_KNOWN_HOSTS` | the `ssh-keyscan` line from step 2 |
| Variable | `VPS_HOST` | `<host>` |
| Variable | `VPS_USER` | `deploy` |
| Variable | `VPS_DEPLOY_DIR` | `/opt/clinic-management/deploy` |
| Variable | `DEPLOY_DOMAIN` | `<domain>` |

Optional: `VPS_SSH_PORT` (default 22), `META_APP_ID`, `META_CONFIG_ID`, `META_GRAPH_API_VERSION`.

- [ ] Optionally create a `production` environment with required reviewers to gate deploys.

---

## 7. Deploy

- [ ] Actions → **Deploy — hosted VPS** → branch `main` → Run. Leave `image_tag` empty.
- [ ] Watch for: images build → push to GHCR → `deploy/` shipped → `pull` → `build` (the four local ones) → `up -d` → `/health` passes.

⚠️ **The whole `deploy/` tree must be owned by `deploy`.** The workflow extracts its tarball as that user, and
`tar` cannot set the mode or mtime of a directory owned by someone else — it exits non-zero on
`./backup`, `./certs`, `./postgres`, `./rclone`. If an earlier step was run as root, fix it before deploying:

```bash
chown -R deploy:deploy /opt/clinic-management/deploy
chmod 700 /opt/clinic-management/deploy/secrets
chown 1654:deploy /opt/clinic-management/deploy/secrets/*
chmod 0440 /opt/clinic-management/deploy/secrets/*
chown deploy:deploy /opt/clinic-management/deploy/.env && chmod 600 /opt/clinic-management/deploy/.env
```

Boot order (automatic): `certs` → `postgres`, `minio` → `api`, `backup`, `pitr` → `web`, `console`, `caddy`.

If `/health` never passes:
```bash
ssh deploy@<host>
cd /opt/clinic-management/deploy
docker compose -f docker-compose.hosted.yml -f docker-compose.registry.yml ps
docker compose -f docker-compose.hosted.yml -f docker-compose.registry.yml logs api --tail=80
```
Startup refusals name the exact variable and file. Read them literally.

⚠️ **`Restarting (139)` on `clinic-api-prod` is almost always a secret file's OWNER, not its contents.**
139 is SIGSEGV's exit code and reads like a crash; the actual line is a few screens up in the log and says
« illisible … Permission denied ». Check `ls -ln secrets/` for a row that is not `1654 1001` — see § 3.
Observed once on this deployment, after the real Google client secret was written with `nano` as `deploy`:
the file was correct, the value was correct, and the API could not open it.

---

## 8. Verify

```bash
curl -s https://<domain>/health

docker exec clinic-api-prod dotnet ClinicManagement.API.dll verify-schema
docker exec clinic-api-prod dotnet ClinicManagement.API.dll verify-schema | grep -E "audit-chain|secrets-protected|internal-certificate"

# TLS on the internal hops
docker exec clinic-postgres-prod psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c '\conninfo'
# expect: SSL connection (protocol: TLSv1.3, ...)
```

- [ ] `verify-schema` exits 0.
- [ ] `https://<domain>` serves the login page with a valid cert.

---

## 9. Vendor console

- [ ] Create the first account:
  ```bash
  docker exec clinic-api-prod dotnet ClinicManagement.API.dll \
    platform-account create --email ops@editeur.tn --name "Nom Prénom"
  ```
  Prints a one-time password **and** an enrolment secret, each shown once.
- [ ] Put the enrolment secret in an authenticator app.
- [ ] Open the tunnel from your workstation:
  ```bash
  ssh -L 9443:127.0.0.1:9443 deploy@<host>
  ```
  Then **`https://console.localhost:9443`** — the name, not `127.0.0.1`: a browser sends no SNI for an IP
  literal, so the bare address fails the TLS handshake outright (`ERR_SSL_PROTOCOL_ERROR`, not a dismissable
  warning). See `README.md` § « Opening it ». Certificate warning is expected (internal CA on a loopback name).
- [ ] Sign in → it sends you to enrolment → enter a code → **write down the recovery codes** → change the password.
- [ ] Changing the password signs you out and **invalidates every session**. Sign in again: the portfolio should
      now load. Until that change, the API refuses every console read — the console routes you to
      `/mot-de-passe` for it rather than reporting an unreadable portfolio.

---

## 10. First clinic

```bash
docker exec clinic-api-prod dotnet ClinicManagement.API.dll provision-clinic \
  --name "Cabinet Dentaire Ben Salah" \
  --admin-email "owner@cabinet.tn" \
  --admin-name "Amel Ben Salah"
```

- [ ] Prints a one-time password + join code. Give them to the admin.
- [ ] Admin logs in at `https://<domain>`, changes the password, creates staff from « Utilisateurs ».
- [ ] Desktop/mobile clients need only `<domain>`.

If locked out: `docker exec clinic-api-prod dotnet ClinicManagement.API.dll reset-admin-password owner@cabinet.tn`

---

## 11. Prove the backups work — same day, not later

- [ ] Wait for or trigger a backup run; confirm an encrypted archive lands at `BACKUP_REMOTE`.
- [ ] Confirm WAL segments are arriving at `WALG_S3_PREFIX`.
- [ ] Run the restore drill in `RESTORE-DRILL.md`.

⚠️ Lose the age private key or `WALG_LIBSODIUM_KEY` and **every archive taken with them is unrecoverable**. Verify you can decrypt before you rely on it.

---

## Ongoing

- Deploy: re-run the workflow from `main`.
- Roll back: re-run the workflow from the **older ref** — not `image_tag` alone, which leaves newer compose files under older images.
- `.env`, `secrets/`, `rclone/rclone.conf` are excluded from what the workflow ships and are gitignored. They live only on the server and in your off-site custody.
