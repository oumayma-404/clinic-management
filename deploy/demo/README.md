# demo.apexa.tn — the public demo cabinet, beside production

Runbook for `features/demo-cabinet/`. The demo is two extra containers (`clinic-api-demo`, `clinic-web-demo`) on the
production server, running the **same images** as production with their own database, role, keys, bucket and
memory/CPU caps. It is switched on by one line in the server's `.env` (`DEMO_DOMAIN`), and off by removing it.

⚠️ **Every step below that touches the server or DNS needs the owner's OK for that step.** Nothing here is run by CI
except the deploy itself, and the deploy does nothing demo-related until `DEMO_DOMAIN` is in `.env`.

| What | Where |
|---|---|
| Compose overlay | `deploy/docker-compose.demo.yml` |
| Caddy site | `deploy/demo/Caddyfile.demo` (mounted into Caddy only by the overlay) |
| Deploy wiring | `.github/workflows/deploy-hosted.yml` — overlay added to the three compose commands, demo site validated before the swap, « Demo cabinet » step (warnings only) |

Production paths below assume `/opt/clinic-management/deploy` (the `VPS_DEPLOY_DIR`). Run from that folder.

---

## 1. DNS — first, and wait for it

- [ ] `A` record: `demo.apexa.tn` → the server's IP (the same as `app.apexa.tn`).
- [ ] `dig +short demo.apexa.tn` from elsewhere returns that IP.

Caddy asks Let's Encrypt for the certificate at first start; with the record missing it retries and the demo site
answers nothing until it lands. Production is not affected either way.

---

## 2. Database — its own role and database, and no door into production's

```bash
set -a; . ./.env; set +a
DEMO_PW=$(openssl rand -base64 32 | tr -d '/+=')          # goes into .env below as DEMO_POSTGRES_PASSWORD
docker exec -i clinic-postgres-prod psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d postgres <<SQL
CREATE ROLE clinic_demo LOGIN PASSWORD '$DEMO_PW' NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE DATABASE clinic_demo OWNER clinic_demo;
REVOKE CONNECT ON DATABASE clinic_demo FROM PUBLIC;
-- production's database: only its own role may connect from now on
REVOKE CONNECT ON DATABASE "$POSTGRES_DB" FROM PUBLIC;
GRANT CONNECT ON DATABASE "$POSTGRES_DB" TO "$POSTGRES_USER";
SQL
```

- [ ] The demo role cannot open production's database (expect `permission denied for database`):
  `docker exec -e PGPASSWORD="$DEMO_PW" clinic-postgres-prod psql -h localhost -U clinic_demo -d "$POSTGRES_DB" -c 'select 1'`
- [ ] It can open its own: same command with `-d clinic_demo` → `1`.

The demo's migrations run as `clinic_demo` at its first start, including `CREATE EXTENSION btree_gist / unaccent`
(both are trusted extensions, so the database owner may create them — no superuser needed).

⚠️ The `REVOKE … FROM PUBLIC` on production's database is the half that matters: by default every role may connect
to every database, and `pg_hba.conf` here admits any role over TLS. Run the check above; do not assume it.

---

## 3. Object storage — its own user, limited to its own bucket

```bash
DEMO_KEY=clinic-demo
DEMO_SECRET=$(openssl rand -base64 32 | tr -d '/+=')     # goes into .env below as DEMO_MINIO_SECRET_KEY
docker exec -i clinic-minio-prod sh -s <<SH
set -e
mc --insecure alias set local https://localhost:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null
mc --insecure mb --ignore-existing local/clinic-demo
cat > /tmp/clinic-demo-policy.json <<'JSON'
{ "Version": "2012-10-17", "Statement": [
  { "Effect": "Allow", "Action": ["s3:*"], "Resource": ["arn:aws:s3:::clinic-demo", "arn:aws:s3:::clinic-demo/*"] } ] }
JSON
mc --insecure admin policy create local clinic-demo-only /tmp/clinic-demo-policy.json
mc --insecure admin user add local "$DEMO_KEY" "$DEMO_SECRET"
mc --insecure admin policy attach local clinic-demo-only --user "$DEMO_KEY"
rm /tmp/clinic-demo-policy.json
SH
```

- [ ] With the demo user, listing production's bucket is refused (`Access Denied`), listing `clinic-demo` works.

---

## 4. Secrets — four files of its own, owned by uid 1654

```bash
cd secrets
openssl rand -base64 48 > demo-auth-local-signing-key     # MUST differ from production's: a demo session is refused there
openssl rand -base64 48 > demo-audit-chain-key
openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes \
  -keyout demo-keyring.key -out demo-keyring.crt -subj "/CN=clinic-demo-keyring"
DEMO_PFX_PW=$(openssl rand -base64 24)
openssl pkcs12 -export -inkey demo-keyring.key -in demo-keyring.crt -out demo-keyring-certificate.pfx -passout pass:"$DEMO_PFX_PW"
printf '%s' "$DEMO_PFX_PW" > demo-keyring-certificate-password
shred -u demo-keyring.key demo-keyring.crt
docker run --rm -v /opt/clinic-management/deploy/secrets:/s alpine sh -c 'chown 1654:1001 /s/demo-* && chmod 0440 /s/demo-*'
cd ..
```

- [ ] `ls -ln secrets/demo-*` shows four files, `1654 1001`, mode `-r--r-----`. A file left owned by `deploy` takes
  the demo API down in a restart loop with no message naming a permission (`VPS-BRINGUP.md` § 3).

The demo holds no real patient data, so its keys need no off-server copy: losing them costs one rebuild.

---

## 5. `.env` — the switch

Add to the server's `deploy/.env`:

```bash
DEMO_DOMAIN=demo.apexa.tn
DEMO_POSTGRES_PASSWORD=<DEMO_PW from step 2>
DEMO_MINIO_ACCESS_KEY=clinic-demo
DEMO_MINIO_SECRET_KEY=<DEMO_SECRET from step 3>
# optional caps (defaults shown)
# DEMO_API_MEMORY=768m
# DEMO_API_CPUS=1.0
# DEMO_WEB_MEMORY=384m
# DEMO_WEB_CPUS=0.5
```

Before deploying, check the overlay resolves (starts nothing, errors on a missing secret or variable):

```bash
docker compose -f docker-compose.hosted.yml -f docker-compose.registry.yml -f docker-compose.demo.yml config --quiet
```

---

## 6. Deploy

Run the **Deploy hosted** workflow as usual. With `DEMO_DOMAIN` in `.env` it also:

- adds the overlay to every compose command (so `--remove-orphans` never deletes the demo);
- validates `demo/Caddyfile.demo` with the Caddyfile before the swap;
- starts `api-demo` / `web-demo` on the tag it just pulled (AC-7.4: every production deploy updates the demo);
- in the « Demo cabinet » step: `/health`, `verify-schema` on `clinic-api-demo`, and the cabinet's build day.
  These are **warnings**, never failures: the demo must not fail a production deploy.

First start builds the cabinet by itself (about 30 s after the API is up). Then:

- [ ] `https://demo.apexa.tn` shows « Entrer dans la démo »; entering lands on the Tableau de bord as Dr Monica Geller.
- [ ] `curl -s https://demo.apexa.tn/api/auth/mode` shows `"isDemo":true` and `"demoBuiltOn":"<today>"`.
- [ ] `curl -sI https://demo.apexa.tn | grep -i x-robots-tag` → `noindex, nofollow`.
- [ ] `curl -s -o /dev/null -w '%{http_code}' https://demo.apexa.tn/manifest.webmanifest` → `404`.
- [ ] A demo access token sent to `https://app.apexa.tn/api/patients` → `401`.

---

## Ongoing

| Question | Answer |
|---|---|
| Is last night's rebuild OK? | `demoBuiltOn` in `/api/auth/mode` is today (after 03:00 Tunis) or yesterday. Older = the nightly rebuild has failed; yesterday's cabinet is still being served. Read `docker logs clinic-api-demo` for « Demo: the rebuild failed at « step » ». The deploy summary warns too. |
| Rebuild now | set `DEMO_REBUILD_ON_START=true` in `.env`, run the two lines below, then remove the `.env` line (or the next restart rebuilds again) |
| Visitors inside during a rebuild | they keep yesterday's cabinet until their 2 h session ends; it is removed at the next rebuild once nobody is in it |
| Caddy did not pick up an edited demo site | the file is a single-file bind mount: `up -d --force-recreate --no-deps caddy` (the deploy's freshness step does this itself) |
| How much does it cost production? | at most `DEMO_API_MEMORY` + `DEMO_WEB_MEMORY` of RAM and `DEMO_API_CPUS` + `DEMO_WEB_CPUS` of CPU; the PDF and request limits are per visit |

« Rebuild now », by hand — on production's own image, never `latest`:

```bash
img=$(docker inspect clinic-api-prod --format '{{.Config.Image}}')
export CLINIC_IMAGE_PREFIX=${img%-api:*} CLINIC_IMAGE_TAG=${img##*:}
docker compose -f docker-compose.hosted.yml -f docker-compose.registry.yml -f docker-compose.demo.yml up -d --no-build --no-deps api-demo
```

⚠️ Without them every image line falls back to `:latest` — the newest build, which is not what production runs
after a rollback. The demo would then serve a different version from production (AC-7.4). The deploy sets both.

## Switching it off

1. Remove `DEMO_DOMAIN` (and the other `DEMO_*` lines) from `.env`.
2. Run the deploy: the overlay is no longer named, so `--remove-orphans` removes `clinic-api-demo` / `clinic-web-demo`
   and Caddy is recreated without the demo site.
3. Optional, to remove its data too (owner's OK): `DROP DATABASE clinic_demo; DROP ROLE clinic_demo;`,
   `mc rb --force local/clinic-demo`, `mc admin user remove local clinic-demo`, `rm secrets/demo-*`,
   `docker volume rm clinic-management_demo_dataprotection_keys clinic-management_demo_keyring_marker clinic-management_demo_api_logs`,
   and delete the `demo.apexa.tn` DNS record.
