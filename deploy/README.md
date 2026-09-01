# `deploy/` — hosted deployments, operator guide

**One** hosted topology lives here, over a shared infrastructure base:

| File | What it is |
|---|---|
| `docker-compose.hosted.yml` | **`HostedMultiTenant`** — the deployment. The product's **own** accounts; many clinics, one backend, each clinic running the Windows desktop client and reaching it over the internet. **This is the file you bring up.** |
| `docker-compose.prod.yml` | The shared **infrastructure base**: certs, postgres, minio, caddy, backup, pitr. Not runnable on its own. |

⚠️ `docker-compose.prod.yml` used to be a deployment in its own right — `CloudBrowser`, logging in through Auth0.
That kind is **retired** with Auth0, and its `api`/`web` services are gone from the file. What remains is the
infrastructure the hosted file `extends`, which is why the file is still here and must not be deleted.

`certs`, `postgres`, `minio`, `caddy`, `backup` and `pitr` are defined **once**, in `docker-compose.prod.yml`, and
the hosted file `extends` them — so a backup schedule or a WAL-G prefix has one home. `api` and `web` are written
only in the hosted file now.

> The clinic's own Windows PC is the other topology (`SelfHostedLan`) and is **not** deployed from here — see
> [`packaging/README.md`](../packaging/README.md).

---

## Bringing up a hosted multi-tenant install

```bash
cd deploy
cp .env.hosted.example .env          # then fill it in — .env is gitignored, never commit it
# DNS for $DOMAIN must already point at this host and ports 80/443 must be open:
# Caddy obtains the Let's Encrypt certificate on first boot.
docker compose -f docker-compose.hosted.yml up -d --build
```

The **first** container to run is `certs`, a one-shot that mints this deployment's internal certificate authority
and exits; everything else waits for it. See *Transit inside the perimeter* below — the API **refuses to start** if
the internal hops are not encrypted and verified, so a cold start is where that is either right or loud.

Then check the front door before anything else:

```bash
curl -s https://$DOMAIN/health
# {"status":"Healthy","checks":{"database":"Healthy","storage":"Healthy"}}
```

`/health` answers **200 for `Healthy` and for `Degraded`**, and **503 only when the database is unreachable`. That
split is deliberate: a clinic whose object storage is down can still book appointments, record fiches and collect
money — pulling the instance out of rotation for it would turn a partial outage into a total one. So `Degraded`
means *look at the log*, not *restart*. The body carries check names and statuses only; the reason a check failed
is in the API's log and never in the response.

### The five keys that decide the profile

They are **literals in `docker-compose.hosted.yml`**, not in `.env`, because none of them is operator-tunable and
every one of them fails **quietly** if wrong. Each is commented in place; in short:

| Key | Omitting it |
|---|---|
| `Deployment__Profile=HostedMultiTenant` | **startup throws.** The fallback used to derive `CloudBrowser` from a non-`Local` `Auth:Mode`; that kind is retired, and the two survivors disagree about accounts, storage, subscriptions and the second factor — so guessing between them is refused rather than silently wrong |
| `DataProtection__KeyRingPath=/keys` **+ the `dataprotection_keys` volume** | **startup fails loud** without the key (by design). With the key but **no volume** it works — until the first redeploy, after which every clinic's reminder credentials are undecryptable and each channel reports « non configuré » with nothing in any log tying that to a deployment |
| `AUTH_MODE=local` (web) | nothing, now — `resolveAuthMode()` returns `'local'` whatever the env says. Kept as a literal because it is still a **build** ARG (`/login` is statically prerendered) and because an env var that silently stopped mattering is worth stating rather than deleting |
| `API_INTERNAL_URL=http://api:5000/api` (web) | login, refresh and change-password 500 — and only those, because only the BFF fetches server-side |
| `AUTH_COOKIE_SECURE=true` (web) | Caddy speaks plain HTTP to the container, so the handler drops `Secure` from an **internet-facing** session cookie |

⚠️ **What is backed up together, and what is kept apart.** The Data Protection key ring
(`dataprotection_keys`) is **encrypted** by the deployment's own certificate, so back it up **alongside**
`postgres_data` — a restored database without it has credentials nobody can decrypt. What must travel
**separately, never in the same archive**, is the **certificate** that decrypts the ring, together with the
backup key and the PITR key.

This reverses the rule that stood before the ring was encrypted, when the *ring* was the thing that had to travel
apart. It is stated in full, once, in **[KEY-CUSTODY.md](./KEY-CUSTODY.md)** — that file is the authority, and it
also covers what to do when each key is lost.

⚠️ Configuring the certificate protects keys the ring writes **from then on** and re-wraps nothing already on the
volume. The migration order (`reprotect-secrets --rotate` → `verify-schema` reads zero → *only then* delete the
old key files) is in KEY-CUSTODY.md § 1; doing it the other way round destroys every administrator's second
factor at once.

---

## Déploiement depuis GitHub

`.github/workflows/deploy-hosted.yml` deploys this stack to any VPS from the Actions tab. It builds `api`,
`web` and `console` **on the runner**, pushes them to GHCR, ships `deploy/` to the server, and the server only
pulls and restarts.

**Why the build does not happen on the server.** Two Next production builds and a `dotnet publish` want
gigabytes of RAM and minutes of CPU. Doing that on the box that is meanwhile serving clinics is the one avoidable
self-inflicted outage in this design — and it is what lets a modest VPS (4 vCPU) host this comfortably.

### One-time server preparation

```bash
# 1. Docker, and a user that may drive it
sudo apt-get update && sudo apt-get install -y docker.io docker-compose-v2
sudo useradd -m -s /bin/bash deploy && sudo usermod -aG docker deploy

# 2. Where the deployment lives. The workflow only ever writes INSIDE this directory.
sudo install -d -o deploy -g deploy /opt/clinic-management/deploy

# 3. The deploy key. Generate it on a machine you trust, keep the PRIVATE half for GitHub.
ssh-keygen -t ed25519 -f clinic-deploy -C "github-actions"
ssh-copy-id -i clinic-deploy.pub deploy@<host>      # or append to ~deploy/.ssh/authorized_keys

# 4. The host key GitHub will pin. Run this from a machine you trust, not from the runner.
ssh-keyscan -t ed25519 <host>
```

Then create the deployment's own identity **on the server**, once — these never come from GitHub:

```
/opt/clinic-management/deploy/.env                  # from .env.hosted.example
/opt/clinic-management/deploy/secrets/*             # per KEY-CUSTODY.md
/opt/clinic-management/deploy/rclone/rclone.conf    # the off-site remote
```

⚠️ **The workflow excludes all three from what it ships, by name.** They are also gitignored, so they are not in
the runner's checkout to begin with — the exclusion is the second statement of an invariant worth relying on.
A deploy able to overwrite them could make every administrator's second factor and every clinic's reminder
credentials undecryptable, with nothing in any log saying why.

### What to set in GitHub

| Kind | Name | Value |
|---|---|---|
| Secret | `VPS_SSH_KEY` | the **private** half of the deploy key (whole file, including the header lines) |
| Secret | `VPS_SSH_KNOWN_HOSTS` | the `ssh-keyscan` line from step 4 |
| Variable | `VPS_HOST` | the server's hostname or IP |
| Variable | `VPS_USER` | `deploy` |
| Variable | `VPS_DEPLOY_DIR` | `/opt/clinic-management/deploy` |
| Variable | `DEPLOY_DOMAIN` | the public domain, for the post-deploy `/health` check |
| Variable | `VPS_SSH_PORT` | *optional*, defaults to 22 |
| Variable | `META_APP_ID` / `META_CONFIG_ID` / `META_GRAPH_API_VERSION` | *optional* — read by Compose as `web` build args |

⚠️ **`VPS_SSH_KNOWN_HOSTS` is required and `StrictHostKeyChecking` is never disabled.** Trust-on-first-use from
a fresh runner means whichever machine answers on that address is handed a key that can deploy to a server
holding patient records.

⚠️ **No standing registry credential is left on the server.** The workflow logs it in to GHCR with the run's own
`GITHUB_TOKEN` and logs it out again in a step that runs even when the deploy fails.

The `deploy` job targets a GitHub **environment** called `production` — add required reviewers or a wait timer to
it in repo settings and every deploy needs an approval, with no change to this file.

### Ce qu'un cabinet voit pendant un déploiement

**Rien.** C'est un objectif, pas une évidence : jusqu'au 2026-09-01 un déploiement était une panne de 25
secondes, et elle a été mesurée en atteignant un cabinet en pleine séance à 10h34.

Ce qui se passe réellement : `docker compose up -d` **arrête** l'unique conteneur `clinic-api-prod` avant de
démarrer le nouveau — `container_name:` en épingle une seule instance, donc l'ancienne et la nouvelle ne se
recouvrent jamais. `web` et `console` suivent. Pendant ces secondes-là le conteneur n'existe plus, et il
disparaît même du DNS interne de Docker (`lookup api on 127.0.0.11:53: server misbehaving` dans le journal de
Caddy). Trois réglages font que personne ne le remarque :

| Où | Réglage | Ce qu'il empêche |
|---|---|---|
| `Caddyfile` | `lb_try_duration 45s` sur `/api/*`, `/hub/*`, les pages et les deux routes de la console | Caddy **retient** la requête et re-compose toutes les 250 ms au lieu de renvoyer un 502 immédiat. Le poste voit une requête lente, jamais une erreur |
| `docker-compose.hosted.yml` | `healthcheck:` + `stop_grace_period: 30s` sur `api`, `web`, `console` | Le déploiement sait distinguer « démarré » de « prêt », et une écriture en cours se termine au lieu d'être tuée à 10 s |
| `deploy-hosted.yml` | `up -d --wait --wait-timeout 180` | L'étape suivante ne s'exécute plus par-dessus une API encore en train de démarrer — c'est ainsi que la réparation de Caddy venait empiler une deuxième coupure sur la première |

⚠️ **`/health` est volontairement la seule route SANS `lb_try_duration`.** Tout le reste est la requête d'un
cabinet et vaut mieux être retenu que refusé ; celle-ci fait l'inverse. Elle existe pour dire « l'API répond,
maintenant », et une route qui attend 45 s avant d'admettre le contraire ment à la sonde du déploiement comme à
n'importe quelle supervision. Personne à un bureau ne la charge.

⚠️ **La base de données, elle, n'est pas couverte.** `postgres` n'est recréé que lorsque **sa propre définition
change** dans les fichiers compose expédiés — un déploiement qui ne change que le tag d'image la laisse
tranquille (vérifiable sans rien casser : `docker compose … up -d --dry-run` liste ce qui serait recréé). Quand
elle l'est, l'API attend qu'elle soit `service_healthy`, et c'était la moitié des 25 secondes : la sonde de
`postgres` n'avait pas de `start_period`, donc le premier test n'arrivait que 10 s après le démarrage d'une base
prête en moins d'une seconde. Elle en a un maintenant. **Un changement compose reste le déploiement le plus
coûteux — regardez le `--dry-run` avant, et préférez une heure creuse.**

### Deploying, and rolling back

**Deploy:** Actions → *Deploy — hosted VPS* → pick the branch or tag → Run. Leave `image_tag` empty.

**Roll back:** run it again from the **older tag or branch** — the server then gets that commit's compose files
*and* its images. Put that commit's full SHA in `image_tag` to skip the rebuild when its images are still in
GHCR.

⚠️ **`image_tag` alone is not a rollback.** It changes which images run and leaves the newer `deploy/` on the
server, so a compose change from the newer commit stays live under older images. Re-run from the older ref.

### Reading a failure

| Symptom | What it means |
|---|---|
| `error from registry: denied` at the pull step | that tag is not in GHCR. It fails here rather than rebuilding on the server — which is why `up` is called with `--no-build` |
| `/health did not answer 200 within 5 minutes` | the containers started but the front door did not. `docker compose logs api`; a 503 means the database is unreachable |
| `::warning::/health reports Degraded` | **not** a failure. Object storage is down; a clinic can still book, record and take money. Read the API log |
| `verify-schema found DRIFT` | migrations applied but the live schema does not match the model. The deploy IS live — fix before the next migration batch |

⚠️ **Migrations apply themselves** at startup (`DeferredStartupService`, behind a `MigrationLock` advisory lock),
so they have already run by the time `verify-schema` is asked. That step answers the different question this
repository insists on around any migration batch: does the live schema match the model? It is read-only.

### The overlay

`docker-compose.registry.yml` sets `image:` on those three services and **nothing else** — every decision about
how the deployment runs stays in `docker-compose.hosted.yml`, where it is documented. To deploy by hand:

```bash
cd /opt/clinic-management/deploy
export CLINIC_IMAGE_PREFIX=ghcr.io/<owner>/clinic CLINIC_IMAGE_TAG=<sha>
docker compose -f docker-compose.hosted.yml -f docker-compose.registry.yml pull api web console
docker compose -f docker-compose.hosted.yml -f docker-compose.registry.yml build certs postgres backup pitr
docker compose -f docker-compose.hosted.yml -f docker-compose.registry.yml up -d --no-build
```

⚠️ **The `build` line is not optional on a server that has never deployed.** `certs`, `postgres`, `backup` and
`pitr` are local builds by design (they need their `deploy/` contexts anyway and cost seconds), so nothing pushes
them — and `--no-build` then refuses to create them. Omitting it fails at the first container with
`No such image: clinic-internal-certs:1`, naming an image no registry was ever supposed to hold. `postgres` and
`pitr` share one image, so four services build three times. Cheap on every later run; the layers are cached.

⚠️ **Always `--no-build` on the `up`.** Compose merge cannot *remove* the base file's `build:` section, so a
plain `up -d` with a tag missing from the registry would quietly start a full rebuild on the production box
instead of refusing. That is why the two steps are separate: the build names the four cheap services explicitly,
which leaves `up` free to stay strict about the three expensive ones.

⚠️ **`web`'s build args are read from Compose and are never restated in the workflow**, deliberately.
`NEXT_PUBLIC_*` is substituted into the bundle by `npm run build`, so those args decide what the browser gets —
and this file already records the release where an image built with `AUTH_MODE` unset shipped an Auth0 landing
page to a deployment with no Auth0. One source of truth, which is why CI builds *through* `docker compose build`
rather than with a build action.

---

## Courrier sortant (SMTP) — the go-live value with no guard behind it

**Set this before you open the front door.** `Notification:Smtp:*` gates five flows, and every one of them
fails at the moment somebody is locked out or trying to get in:

| Flow | What happens with no SMTP host |
|---|---|
| `POST /api/auth/signup` (clinic self-signup) | a French refusal **before anything is written** — no cabinet can join |
| `POST /api/auth/password-reset` | « mot de passe oublié » is a dead end; the only route back a person can take alone |
| an administrator resetting a member of staff's password | the new password never reaches its owner |
| `platform-account --reset-password` | the vendor cannot re-credential a locked-out account |
| `platform-account --reset-second-factor` | the vendor cannot put a lost authenticator right |

```
SMTP_SERVER=smtp.example.tn      # submission relay; empty = every flow above refuses
SMTP_PORT=587                    # STARTTLS submission. Port 25 egress is blocked by most hosts
SMTP_USE_TLS=true
SMTP_USERNAME=no-reply@example.tn
SMTP_PASSWORD=…
SMTP_FROM_ADDRESS=               # only when it differs from SMTP_USERNAME
SMTP_FROM_NAME=
```

⚠️ **It shipped wired into nothing, and that is why this section exists.** `Notification:Smtp:Server` is an
empty string in `appsettings.json` and appeared in **no** compose file and **no** `.env` template, so
`SmtpConfig.Host` trimmed empty to null, `ITransactionalEmailSender.IsConfigured` read `false`, and a hosted
deployment came up `Healthy`, served every screen, and could not admit one new cabinet — with nothing in any
log tying that to a value nobody had ever been asked for.
`TransportConfigurationTests.The_Hosted_Deployment_Wires_Outbound_Email` is the derived guard, and its
sibling checks that every `Notification__Smtp__*` variable the compose file interpolates is actually named in
`.env.hosted.example` — the quieter half of the same defect.

⚠️ **Empty is honest; a placeholder is not.** Leave `SMTP_SERVER` blank until you have a mailbox: the screens
then say « non configuré » and refuse cleanly. A `CHANGE_ME` hostname reads as *configured* and fails at
connect time, which reaches the visitor as a **retryable** error over something that will never work.

⚠️ **Nothing refuses to start over this** — unlike transit, residency, the audit chain key and the key ring.
It is the one go-live value with no boot-time guard, which is the whole reason it is written down here.

⚠️ **The link inside those emails comes from `FrontendUrl`** (`https://${DOMAIN}`, already set in
`docker-compose.hosted.yml`), never from a key of its own — the same key the Google OAuth callback uses, so the
two cannot point at different hosts. Both are asked *before* a signup is accepted: with the host unset every
verification link would point at the **recipient's own machine**.

⚠️ **`SMTP_PASSWORD` is a literal in the environment, unlike this deployment's other credentials**, and
deliberately: a `*_FILE` secret naming a file that does not exist is a **startup refusal**, and Compose
validates a secret's source the moment a service lists it — so wiring one unconditionally would take every
deployment without email off the air on the next `up -d`. To move it into `./secrets/` once a mailbox exists,
follow the four-step recipe in the compose file's own comment (add the `secrets:` entry → list it on `api` →
set `Notification__Smtp__Password_FILE` → **then** drop the literal; the file wins over a literal of the same
name, so every intermediate state is safe).

> **Not on `SelfHostedLan`.** `AllowsPublicClinicSignup` and `AllowsPasswordResetByEmail` are both **false**
> there — a surgery PC has no mailbox, its login screen offers no « mot de passe oublié », and
> `reset-admin-password` on the console is that install's answer. See [`packaging/README.md`](../packaging/README.md).

---

## Résidence des données

**Where a clinic's records physically end up is a legal decision, not an ops preference.** Under Tunisia's
*loi organique 2004-63*, transferring personal data abroad requires **prior INPDP authorization** (art. 51–52),
health data is separately sensitive, and the penalty under art. 90 (one year's imprisonment plus a 5 000 DT
fine) falls on the **cabinet** — the *responsable du traitement* — not on the vendor. Choosing a foreign host
does not merely expose us; it puts every practice using the product in breach.

### The two destinations that matter, and why they are easy to miss

Hosting the *application* in Tunisia is not enough. Two sidecars ship a complete copy of the database
off-server, and neither is visible from any screen in the product:

| Variable | What leaves | How often |
|---|---|---|
| `WALG_S3_ENDPOINT` | every write to every patient record (WAL segments) | **continuously**, within seconds |
| `BACKUP_REMOTE` | a full `pg_dump` of every clinic | nightly |

⚠️ **`WALG_S3_ENDPOINT` shipped as `https://s3.us-west-002.backblazeb2.com`** — Backblaze, Oregon. An operator
who copied `.env.hosted.example` and changed only the credentials was continuously exporting every Tunisian
patient record to the United States, with every layer of the product reporting a healthy deployment. The
template now carries `CHANGE_ME_s3_endpoint_in_tunisia`, which fails loudly instead.

### The guard

Declare every host this deployment's data may reach:

```
RESIDENCY_ALLOWED_EGRESS_HOSTS_0=s3.eodatacenter.tn
RESIDENCY_ALLOWED_EGRESS_HOSTS_1=backup.dataxion.tn
```

`DataResidencyAssurance` runs at startup, beside the transit check, and **refuses to boot** naming the offending
host and the compose variable to change. Leave the list empty and the API starts but warns on **every** boot
that residency is undeclared — *undecided* is not the same as *forbidden*, but it must not be silent either.

⚠️ **It is a declaration, never a geolocation lookup.** Resolving a host to a country at startup is one DNS
hiccup away from a failed boot, and a CDN address answers honestly in a dozen jurisdictions at once.

⚠️ **`BACKUP_REMOTE` is reported, not verified — and that distinction is the point.** It names an *rclone
remote*; the real host lives in `rclone/rclone.conf`, a file the API never reads and which belongs to another
container. It is logged as « non vérifiable » on every boot rather than passed over, because converting
*unknown* into *checked* on a nightly dump of every clinic's records is worse than having no guard at all.
**Verify that one by hand.**

⚠️ **A dotless host is not egress.** `minio:9000` is a container on the compose network and never needs
allow-listing — otherwise operators would learn to paste in whatever the refusal names, and the list would
stop being a decision.

### Choosing a host

Tunisian options with a Tier III+ floor: **EO Data Center** (Enfidha, carrier-neutral, IaaS + BaaS/DRaaS),
**DataXion** (the only Tier IV in the country), **Orange Tunisie** (Kalaa Kebira), **Ooredoo Business**.

Take **two** providers, not one: primary and offsite must not share a failure domain, and both must be on the
allow-list or the guard refuses. Ask each, in writing: the **physical location** of the machine, where snapshots
live, whether they offer S3-compatible storage in Tunisia, and whether they will sign a *sous-traitant*
undertaking naming Tunisian territory. A product branded « VPS Tunisie » is not evidence — at least one
provider's own page advertises that name for a datacentre in **Lisbon**.

> **Also note** `Décret-loi 2023-17`: an **annual cybersecurity audit** by an ANCS-accredited auditor is
> mandatory for a company processing personal data over telecom networks. It applies wherever you host.

---

## Transit inside the perimeter

Caddy terminates the internet's TLS. Everything **behind** it is encrypted and verified too: the API↔PostgreSQL
hop, the API↔MinIO hop and both backup sidecars, against a certificate authority created for this deployment and
trusted by nothing else.

**A deployment that is not in that state refuses to start**, names the setting and the file, and says so on the
console, in the log and (on Windows) in the Event Log. That is deliberate — transit failing *open* is invisible,
and the whole point is that nobody is watching this console.

### How it is wired

| Piece | What it is |
|---|---|
| `certs` service (`./certs/`) | a **one-shot** container: mints a ten-year internal CA and one leaf each for `postgres` and `minio` into the `internal_certs` volume, then exits 0. **Idempotent** — an existing set that still chains is reused, so `up -d` never hands postgres an identity the API does not yet trust |
| `internal_certs` volume | `ca.crt` (the one trust anchor) · `postgres/server.{crt,key}` · `minio/{public.crt,private.key}` · `minio/CAs/`. Mounted **`:ro`** by every consumer; the authority is the only writer |
| `postgres` | `ssl=on` with its leaf, and `-c hba_file=/etc/postgresql/pg_hba.conf` — a `pg_hba.conf` baked into the image that offers **`hostssl` only** |
| `api` | `SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt` in the connection string · `MinIO__UseSSL=true` · `MinIO__RootCertificate=/certs/ca.crt` |
| `backup`, `pitr` | `PGSSLMODE=verify-full` + `PGSSLROOTCERT=/certs/ca.crt`, and each **asks PostgreSQL whether its own connection is encrypted** before it dumps anything — a run that cannot negotiate **fails**, it never skips and reports success |

⚠️ **The connection string uses Npgsql's spelling, `SSL Mode=VerifyFull` — not libpq's `sslmode=verify-full`**,
which Npgsql rejects outright. Get that wrong and the API refuses to start rather than falling back, which is the
intended outcome but reads as a puzzling refusal if you were copying a `psql` command line. The **sidecars** use
libpq directly, so *there* the value genuinely is `verify-full`.

⚠️ **`VerifyFull`, not `Require`.** `Require` encrypts and accepts *any* certificate: it stops a packet capture and
not an impostor on the bridge network. Only `verify-full` checks the server's identity — which is also why
`Host=postgres` must stay exactly that, since `postgres` is the name in the leaf's SAN.

### Cold-start order

Nothing needs doing by hand — `depends_on: { certs: { condition: service_completed_successfully } }` orders it —
but the order is worth knowing when reading a failed boot:

```
certs (runs, exits 0)  →  postgres, minio  →  api, backup, pitr  →  web, console, caddy
```

⚠️ `extends` **does not carry `depends_on`**, so `docker-compose.hosted.yml` restates that dependency on every
service that inherits from the prod file. A dropped one starts postgres before its certificate exists and fails
on a *missing file*, two containers away from the real cause. `TransportConfigurationTests` derives the set of
services that mount the volume and asserts each one waits for it.

### Verifying it by hand

```bash
# Every hop over TLS, verified against the internal root:
docker exec clinic-postgres-prod psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c '\conninfo'
# → SSL connection (protocol: TLSv1.3, cipher: TLS_AES_256_GCM_SHA384, compression: off)

# The SERVER refuses cleartext — not merely "nothing uses it":
docker run --rm --network clinic-management_internal postgres:16-alpine \
  psql "host=postgres sslmode=disable user=$POSTGRES_USER dbname=$POSTGRES_DB" -c 'select 1'
# → FATAL: no pg_hba.conf entry for host "...", no encryption

# How long the internal CA has left, on the tool you already run around a migration:
docker exec clinic-api-prod dotnet ClinicManagement.API.dll verify-schema | grep internal-certificate
```

### Two things to know about the volume

- **Back `internal_certs` up separately from `postgres_data`, or not at all.** It holds the CA's *private key*, so
  one archive containing both hands whoever has it the means to impersonate the database and the object store to
  any container that trusts this root.
- **Losing it is recoverable and cheap**: delete the volume, `up -d`, and the one-shot mints a fresh set that every
  consumer picks up from the same place. What is *not* recoverable is recreating it while containers are already
  running against the old root — they keep verifying against a CA that no longer signs anything they can reach.
  Bring the stack down first.

### Forwarded headers

`Security__TrustedProxies__0` is what makes the API believe Caddy's `X-Forwarded-For` and `X-Forwarded-Proto`.
Left unset (or holding nothing parseable) **the headers are ignored entirely and a warning says so at startup** —
never an unbounded header. The visible cost of ignoring them: every clinic in the deployment shares one
rate-limit bucket, the per-account login lockout sees one address, and the Google OAuth state cookie loses its
`Secure` flag.

⚠️ **The two loopback-only gates — first-run `setup` and `/hangfire` — are decided by the real TCP peer and never
by a header**, whatever this setting says. `curl -H 'X-Forwarded-For: 127.0.0.1' https://$DOMAIN/hangfire` is
refused. That is a property of the code (the peer is captured before the headers are honoured), not of the proxy
list being correct.

---

## Creating a clinic

There is **no self-registration in this profile** — `POST /api/auth/register` returns 404, because the only secret
it ever required was a six-character clinic code shown on a settings screen. Clinics are provisioned by the
operator, and staff accounts by the clinic's own admin.

```bash
docker exec clinic-api-prod dotnet ClinicManagement.API.dll provision-clinic \
  --name "Cabinet Dentaire Ben Salah" \
  --admin-email "owner@cabinet.tn" \
  --admin-name "Amel Ben Salah"
```

It prints a **one-time password** and the clinic's join code. The admin must change the password at first login
(`MustChangePassword`), and from « Utilisateurs » they create their colleagues' accounts themselves — each of those
is also created with a one-time password.

The clinic's desktop client needs **only the domain**: `clinics.example.tn` is accepted as a bare host or a URL.

### If a clinic's admin is locked out

```bash
docker exec clinic-api-prod dotnet ClinicManagement.API.dll reset-admin-password owner@cabinet.tn
```

---

## The maintenance verbs

The container inherits its environment, so `AddInstallLayers()` resolves the **same** connection string the running
app uses — a verb run this way is looking at exactly the database the API is looking at.

| Verb | Runs here? | Exit codes |
|---|---|---|
| `verify-schema` | **yes** | `0` clean · `1` couldn't run · `2` drift found |
| `reconcile-money` | **yes** | same three |
| `provision-clinic` | **yes** | `0` / `1` |
| `reset-admin-password` | **yes** | `0` / `1` |
| `subscription-report` | **yes** | `0` clean · `1` couldn't run · `2` findings |
| `subscription-grant` / `-cancel` / `-suspend` / `-unsuspend` | **yes** | `0` / `1` |
| `messaging-report` | **yes** | `0` clean · `1` couldn't run · `2` findings |
| `messaging-grant` / `-cancel` | **yes** | `0` / `1` |
| `reprotect-secrets [--rotate]` | **yes** | `0` all current · `1` couldn't run · `2` work remains |
| `restore-backup` | **no — refuses** | see below |

```bash
docker exec clinic-api-prod dotnet ClinicManagement.API.dll verify-schema
docker exec clinic-api-prod dotnet ClinicManagement.API.dll reconcile-money
```

Both are **read-only** and are meant to be run **before and after a migration batch and diffed**. `verify-schema`
is the only gate a schema change has anywhere in this product — nothing in the unit-test project touches a
database — and in this topology a bad migration reaches every clinic at once, so run it.

⚠️ **`restore-backup` refuses in this profile, on purpose.** Its safety interlock is « refuse while the application
is listening », enforced by looking for a listener on the machine it runs on. In Docker the API listens in a
*different* container, so from a one-off `docker exec` the check finds nothing and passes — and
`pg_restore --clean --if-exists` would then drop every table out from under a live application. Restore from the
`backup`/`pitr` services' artifacts with the stack stopped instead.

---

## Key custody and encryption at rest

Four keys hold this deployment together, and each one has a different answer to « what if it is lost? ».
**[KEY-CUSTODY.md](./KEY-CUSTODY.md) is the authority** — where each lives, who holds a copy, and how to recover.
Fill in its holder table before the deployment carries a real practice's records; it is a deliverable, not a note.

| Key | Refuses to start / run without it? | Losing it costs |
|---|---|---|
| Key-ring certificate (`DataProtection__CertificatePath`) | **yes**, the API | every second factor and every cabinet's stored credentials |
| Backup key (`BACKUP_AGE_RECIPIENT`) | **yes**, the `backup` sidecar | **every off-site backup, permanently** |
| PITR key (`WALG_LIBSODIUM_KEY`) | **yes**, the `pitr` sidecar | **every archived base backup and WAL segment, permanently** |
| LUKS keyfile | the volume will not unlock at boot | the data volume |

Each refusal is deliberate. « Encrypt if a key happens to be set » is the version that ships a complete copy of
every practice's medical records to somebody else's storage in the clear, while reporting success.

### After configuring the certificate — the order matters

```bash
# Re-encrypt every stored secret under a fresh key. Idempotent without --rotate; names any row it cannot read.
docker exec clinic-api-prod dotnet ClinicManagement.API.dll reprotect-secrets --rotate

# The figure that says it finished. BOTH lines must be clean before any key file is deleted.
docker exec clinic-api-prod dotnet ClinicManagement.API.dll verify-schema   | grep -E 'key-ring-protection|secrets-protected-under-current-ring|google-token-protected'

# No plaintext key may remain in the ring afterwards:
docker run --rm -v clinic-management_dataprotection_keys:/keys alpine grep -rl '<key ' /keys || echo "none — expected"

# And no secret should remain in the API's environment (FR-3.10):
docker exec clinic-api-prod env | grep -Ei 'password|apikey|token|secret' | grep -v '_FILE='
```

⚠️ **Deleting a plaintext key file before its ciphertext has moved destroys every administrator's second factor
at once.** `reprotect-secrets` exits `2` and *names* every row it could not decrypt — while one is listed, its
old key is exactly what is still needed.

### Backups leave encrypted, and are verified by being decrypted

The nightly dump and the object-store archive are encrypted with `age` **before** rclone touches them, and the
PITR stream with libsodium. Each run also stamps the **key-ring generation** it belongs to beside the dump, and
`backup/check-keyring.sh` refuses a restore whose generation this ring cannot read — without that, restoring
against the wrong ring produces a practice whose every second factor is silently undecryptable.

**A backup nobody can restore is not a backup.** The drill, its cadence (**quarterly, plus after each schema
batch**) and its stated pass condition are in **[RESTORE-DRILL.md](./RESTORE-DRILL.md)**. ⚠️ No drill has been run
on this deployment yet — the restore path is unproven until the log in that file has its first row.

### The data volume

LUKS on the volume holding `postgres_data` and `minio_data`, unlocked at boot by a keyfile on the host's own boot
volume so the server still **reboots unattended**. Procedure in KEY-CUSTODY.md § 4.

⚠️ **In these words:** it protects a **stolen, snapshotted or decommissioned disk**. It does **not** protect
against someone who already has root on the running host — while the machine is up the volume is mounted and
readable, and no disk encryption changes that.

---

## Cabinet subscriptions (this profile only)

A cabinet gets **30 free days** and then becomes **read-only**: every read, every CSV export and every PDF keep
working, and only writes are refused. Two halves of that are the operator's:

**1. Publish the tariff, the payment instructions and the contact details.** The `SUBSCRIPTION_*` variables in
`.env` feed « Abonnement », which is the screen a refused save points a chairside user at — and unset, it says
« Aucun tarif n'est publié » to a practice that is trying to pay you. ⚠️ There is deliberately **no
`SUBSCRIPTION_ENABLED`**: enforcement follows `Deployment__Profile` and nothing an operator can set, so a clinic's
own PC can never be one config edit away from refusing its own patient records. A blank price reads as « sur
devis », never as « 0,000 DT ».

**2. Record payments with the five verbs.** They are **verbs and not endpoints on purpose**: a cabinet able to
extend its own entitlement over HTTP would not have one, so nothing in the API grants time.

```bash
E=clinic-api-prod; D="docker exec $E dotnet ClinicManagement.API.dll"

# Who needs attention — exits 2 when any cabinet is expiring, expired or has no entitlement at all.
$D subscription-report --within 7

# One cabinet in full, INCLUDING its period ids — the only place they are printed, and what -cancel takes.
$D subscription-report --clinic owner@cabinet.tn

# Record a payment. --clinic takes an id or the e-mail of anyone who works there.
$D subscription-grant --clinic owner@cabinet.tn --months 12 \
     --amount 1200.000 --method Transfer --reference VIR-4471 --plan Cabinet

# Correct a mistake: the row is KEPT and struck through, and the end date recomputes — possibly into the past.
$D subscription-cancel --clinic owner@cabinet.tn --entry <period-id> --reason "Mauvais cabinet"

# Stop / restart a cabinet for a non-payment reason. Suspension outranks the date and paying does not lift it.
$D subscription-suspend   --clinic owner@cabinet.tn --reason "Litige commercial"
$D subscription-unsuspend --clinic owner@cabinet.tn
```

⚠️ **A grant never shortens cover.** `--until` past a date the cabinet is already covered to is a no-op the verb
says out loud; use `subscription-cancel` to take time away. ⚠️ **The cabinet's app picks a grant up on its next
re-read** — a few minutes at most — with nobody signing out.

---

## Forfait de rappels WhatsApp (this profile only)

**You buy the WhatsApp messages; a cabinet spends them.** Each practice gets a monthly allowance of WhatsApp
appointment reminders, counted one per message actually sent; past it, reminders are **held** rather than dropped —
they go out when you top the cabinet up, and nothing is lost. SMS is never counted, and an exhausted forfait never
affects anything but WhatsApp reminders.

Like subscriptions this is `HostedMultiTenant` only, decided by `Deployment__Profile` and by nothing an operator
can set. Elsewhere the whole feature is **absent**: no screen section, no notifications, no enforcement, no daily
pass, and the two clinic endpoints answer 404.

### 1. The Meta account (do this first — nothing works without it)

The cabinet-facing connection is Meta's **Embedded Signup**, so the deployment needs a Meta Business app of its own.
Five keys in `.env`, and the two `NEXT_PUBLIC_*` ones are **build args**, not runtime variables:

| Key | What it is |
|---|---|
| `META_APP_ID` / `NEXT_PUBLIC_META_APP_ID` | the Meta app, server side and browser side — the same value |
| `META_APP_SECRET` | server only; never reaches the browser |
| `META_CONFIG_ID` / `NEXT_PUBLIC_META_CONFIG_ID` | the Embedded Signup configuration |
| `META_WEBHOOK_VERIFY_TOKEN` | your own random string, echoed back to Meta on the verify handshake |
| `META_GRAPH_API_VERSION` | one key feeding **both** the server's Graph client and the browser SDK |

⚠️ **`NEXT_PUBLIC_*` is baked into the web bundle at build time**, so changing either of those needs
`docker compose … up -d --build`, not a restart. A plain restart leaves the browser on the old value with nothing
saying so.

Point Meta's webhook at **`https://$DOMAIN/api/meta/webhook`** and subscribe to `account_update` (required for
Embedded Signup at all) and `message_template_status_update` (how a cabinet's template gets approved). The endpoint
verifies `X-Hub-Signature-256` over the raw body; an unconfigured secret refuses every delivery rather than trusting
it.

### 2. Set the default allowance and the contact route

```
MESSAGING_DEFAULT_MESSAGES_PER_MONTH=200      # → Messaging__DefaultMessagesPerMonth
MESSAGING_CONTACT_EMAIL=facturation@…         # → Messaging__ContactEmail
MESSAGING_CONTACT_PHONE=+216 …                # → Messaging__ContactPhone
```

The first is what every **new** cabinet is provisioned with — read at provisioning time, so changing it does not
move any existing cabinet's figure, which is what `messaging-grant` is for. Anything unreadable, absent or out of
range falls back to **200** rather than throwing, because that value is read *while a clinic is being created* and a
typo must not abort it.

The other two are how a practice that has run out reaches you, and they appear **verbatim** on the cabinet's own
« Rappels » screen beside « forfait épuisé ». Leave them unset and the screen states the exhaustion with no route
out of it — an absent contact reads as *absent*, never as an invented address.

### 3. The three verbs

```bash
E=clinic-api-prod; D="docker exec $E dotnet ClinicManagement.API.dll"

# Who needs attention. Exits 2 on any finding, in this order of severity:
#   aucun forfait  (our bookkeeping is wrong)  ·  épuisé  ·  non mesuré  ·  template no longer UTILITY
$D messaging-report

# A CLOSED month — this is how you reconcile against Meta's bill.
$D messaging-report --month 2026-07

# One cabinet in full, INCLUDING its allocation ids — the only place they are printed, and what -cancel takes.
$D messaging-report --clinic owner@cabinet.tn

# Change the standing monthly figure. The SERVER decides which month it starts in:
#   a RAISE applies this month, a LOWERING waits for the next one. The verb says which, out loud.
$D messaging-grant --clinic owner@cabinet.tn --per-month 500

# A one-off top-up for a named month, on top of whatever standing figure covers it.
$D messaging-grant --clinic owner@cabinet.tn --top-up 300 --month 2026-08 \
     --amount 45.000 --method Transfer --reference VIR-8812

# Correct a mistake: the row is KEPT and struck through with your motif, and every month it fed recomputes —
# including the CURRENT one, possibly to « épuisé ». Messages already sent are never un-counted.
$D messaging-cancel --clinic owner@cabinet.tn --entry <allocation-id> --reason "Mauvais cabinet"
```

⚠️ **A top-up cannot name a past month** — a month that has closed cannot be given messages it could have spent —
and the refusal names the earliest legal one. ⚠️ **A standing figure of `0` is a *lowering*, so it takes effect next
month**, not this afternoon. Turning a cabinet off now is a **cancellation**, not a zero. ⚠️ **`--complimentary`**
records an allocation with no amount at all; `--amount 0` would read as a transaction that happened for nothing.

### 4. What to expect in `verify-schema`

Three checks live under **Messaging allowances**, and one of them is worth reading rather than skimming:

- **`monthly-allowance-matches-ledger`** re-derives every month's stored figure through the real fold and reports
  **both** directions — a row *above* the fold lets a cabinet send messages nobody allocated, one *below* holds
  reminders it has paid for.
- **`messaging-month-covers-every-clinic`** is the figure that says « the daily pass has not run » rather than
  « these practices are idle ». It reads « not applicable » on the other two profiles, where nothing provisions a
  month row. ⚠️ It is legitimately red for a few hours after a Tunisian month turns: the pass runs at 06:00 Tunis.
- **`messaging-allowance-entry-has-one-form`** catches an allocation the fold cannot read at all — which fails
  *silently*, leaving a cabinet looking as though it has no forfait.

⚠️ A cabinet reported as **« aucun forfait »** is not idle and is not out of messages: it has no allocation record
at all, which is our fault and not theirs, and its reminders are held under their own reason until you run
`messaging-grant --per-month`. The daily pass will **not** create a month row for it — there is nothing to fold.

---

## Watching a running install

Nobody is looking at the console in a datacentre, and two of this product's failure modes are **silent**:

- **`GET /health`** — poll it from an uptime monitor. See the status split above.
- **`GET /api/outbox`** (admin token) — the depth of the three background queues, each with the **age** of its
  oldest waiting row:

  ```bash
  curl -s https://$DOMAIN/api/outbox -H "Authorization: Bearer $ADMIN_TOKEN"
  ```

  ⚠️ **The age is the diagnosis, not the count.** « 40 pending » says nothing — a reminder for next Tuesday is
  *supposed* to be waiting. A row whose send time passed three hours ago says the dispatcher is not running, and
  that matters because a background job which fails to declare its tenant scope reads **nothing** and logs a clean
  run: reminders simply stop while every screen in the product looks perfectly normal.

  `reminders.blocked` is different in kind — it counts rows the queue is holding because the channel is off or
  unconfigured. It is an operator action, not a failure, and those rows return to the queue by themselves once the
  channel can send.

- **`/hangfire` is not reachable here, and that is not a misconfiguration.** The dashboard is loopback-only in
  every profile, and behind Caddy every request's peer address is the proxy container. `GET /api/outbox` exists
  because of that.

---

## Evidence — the audit chain, the export ledger and the content policy

Part 4 of `hosted-security-hardening`. Three things an operator has to know about, and one they have to hold.

### The audit ledger is tamper-evident

Every audit entry carries a value derived from itself **and its predecessor**, keyed by `Audit:ChainKey` — a
secret **the database does not hold**. An entry cannot be altered or removed without breaking the sequence, and
`verify-schema` is what walks it:

```bash
docker exec clinic-api-prod dotnet ClinicManagement.API.dll verify-schema | grep audit-
```

```
[  ok ] audit-chain-intact: 6 chaîne(s) intactes — 179 entrée(s) vérifiées, 1104 antérieure(s) au chaînage
[  ok ] audit-declared-gaps: 5 interruption(s) déclarée(s) — écritures de journal ayant échoué, ou restaurations
```

Three lines of that output need reading carefully:

- **« antérieures au chaînage »** — entries written before this feature shipped. They carry no hash and none can
  be invented for them, so they are **counted, never reported as tampering**. What *is* reported is an unchained
  entry appearing **after** a chained one, which is what erasing a hash to hide an edit looks like.
- **« interruptions déclarées »** — the product's own record that entries are missing here: an audit write that
  failed, or a restore. Reported **apart from breaks and never as drift**, because the product consigned them
  itself. That distinction is the whole point: « a gap we know about » is not « a break nobody declared ».
- **A break is drift** (exit 2) and names the cabinet, the sequence number and the entry id. ⚠️ **Nothing refuses
  to serve.** An audit break is an alarm, not an outage.

⚠️ **`Audit__ChainKey_FILE` is required and the API refuses to start without it.** Generate it once
(`openssl rand -base64 48 > secrets/audit-chain-key`) and keep it — see `KEY-CUSTODY.md`, key 5. Replacing it
loses no data and makes every earlier entry read as tampered.

### Exporting a cabinet's whole record is recorded, and gated

`GET /api/backup/archive` — the file containing every patient record the practice holds — now:

- **writes an attributable ledger entry before it builds anything**, and **refuses the download if it cannot**.
  Not best-effort: an unrecorded export succeeding would make the guarantee false. A second entry records whether
  the archive was **delivered** or the download was abandoned part way.
- **requires the password (or a current second-factor code) immediately beforehand**, single-use, per action.
  ⚠️ Failures there spend the **step-up's own** counter and never the login lockout — a mistyped password at the
  export card cannot lock a practice's only administrator out mid-day.
- **has its own rate limit**: 3 in 10 minutes per user (`RateLimiting:Archive:*`). It used to fall to the general
  API window, which permitted **600 full-practice exports a minute**.

The practice's staff see « Archive du cabinet exportée » in the bell. Per-list CSV exports are deliberately
**not** gated — they are already role-restricted and are a daily action, and daily friction is what gets a
control routed around.

### The content-security policy is enforcing

`Security__EnforceCsp: "true"` ships in both hosted compose files, `'unsafe-eval'` is gone, and the third-party
analytics script has been removed from the web bundle — it loaded from an external origin, so an enforcing
`script-src 'self'` could not have been switched on with it present.

Violations arrive at **`POST /api/csp-report`** and are logged as `CSP violation: …`:

```bash
docker exec clinic-api-prod grep -a "CSP violation" /app/logs/clinic-management-*.log | tail
```

⚠️ **The reported address is stripped to its route pattern** (`/patients/{id}/files`) before anything is
recorded. This application's URLs contain patient identifiers, so a violation report is itself patient data. The
endpoint is anonymous — a violation on the login page is the one that matters most — bounded per address, and
**excess is dropped rather than stored**.

The policy is stated in three places (the API middleware, `Caddyfile`'s two sites, `console/next.config.ts`) and
they are held **byte-identical** by a build-failing test. Change one and the build tells you about the others.

### Logs are durable, and carry no patient names

The `api_logs` volume keeps 30 days of daily files. Before it, logs lived on the container layer and every
restart erased them.

```bash
docker exec clinic-api-prod grep -aEi "PatientName|Patient=" /app/logs/ -r || echo "none — expected"
```

⚠️ The scrub and the volume landed in **one change**, deliberately: making logs durable persists what was
previously ephemeral, so a patient's name in a file that now survives is strictly worse than one in a file that
vanished. `LogTemplateCoverageTests` is what keeps it that way — it scans every log statement in the solution and
fails the build on a patient-identifying placeholder that is not masked.

---

## The vendor console

A private back-office where you see how much each cabinet actually uses the product and record its payments. It
exists **only on this profile**, and it is **off until you set `CONSOLE_PORT`** — absent, there is no second
listener, no reachable route, and every `/api/platform/*` path 404s. Off means absent, not present-and-refusing.

### Opening it

It is published on **loopback only**, so there is no address to reach from the internet and no DNS name for one.

> **On a platform that cannot give you a tunnel** (Render and its like: one public port per service, no SSH into a
> private one) see [`RENDER-CONSOLE.md`](RENDER-CONSOLE.md). It gives two topologies, the test that decides which
> applies, and what publishing the console costs — read its first paragraph before the rest, because where you
> have a shell the verbs below are the lower-risk answer and none of it is needed.

```bash
ssh -L 9443:127.0.0.1:9443 <host>
# then, on your own machine:
open https://console.localhost:9443
```

⚠️ **`console.localhost`, not `127.0.0.1` — the bare IP cannot work in a browser, and does not fail like a
certificate problem.** No browser sends SNI for an IP literal (there is no name to send), so Caddy has nothing to
match on this port and ends the handshake with `internal_error` — Chrome reports `ERR_SSL_PROTOCOL_ERROR`, which
is not a warning anybody can click through. `*.localhost` resolves to loopback inside Chrome and Firefox with no
hosts entry (RFC 6761); on Safari or for a command-line client, add `127.0.0.1 console.localhost` to your hosts
file. The site still answers on the IP for any client that sends it as SNI, so an existing script keeps working.

Expect a **certificate warning**. That site uses Caddy's internal CA, because a loopback name has no public
authority for Let's Encrypt to issue a certificate against. A warning on `{DOMAIN}` is a different event entirely
and should never be dismissed.

> **Verifying it from a shell needs SNI too**, which is what hid this for so long: `curl -k https://127.0.0.1:9443`
> and `wget --no-check-certificate` both report a healthy console *because* of how they handle the IP, so « the
> console is up » stayed true of every client except the one it exists for. Test with the name:
> `curl -k --resolve console.localhost:9443:127.0.0.1 https://console.localhost:9443/login`.

### Bootstrapping the first account

There is no sign-up screen, and no screen anywhere in the console that lists, creates or deactivates an account.
All three are this command:

```bash
docker exec clinic-api-prod dotnet ClinicManagement.API.dll   platform-account create --email ops@editeur.tn --name "Nom Prénom"
```

It prints **a one-time password** and **an enrolment secret**, each shown once and unrecoverable afterwards.

1. Put the enrolment secret into an authenticator app (Google Authenticator, Aegis, 1Password — any RFC 6238 one).
2. Open the console and sign in with the address and the printed password. It refuses with « ce compte doit
   d'abord enrôler son second facteur » and takes you to the enrolment step — that is the expected path, not an
   error.
3. Enter a code from the app. The response is your **recovery codes**, shown once. Write them down somewhere that
   is not the same laptop.
4. Sign in properly. The console then requires you to change the one-time password before it will do anything
   else.

### Losing the second factor

Use a recovery code on the sign-in screen. Each works **once**, and is spent even if that sign-in then fails for
another reason — so a code you typed is a code you have used.

Out of recovery codes, or the authenticator is gone with them:

```bash
docker exec clinic-api-prod dotnet ClinicManagement.API.dll   platform-account --reset-totp --email ops@editeur.tn
```

⚠️ This **invalidates the old authenticator and every remaining recovery code**, and signs the account's live
sessions out. That is what makes it safe: a re-issue that merely added a second factor would leave a lost phone
working for ever.

### Removing an account

```bash
docker exec clinic-api-prod dotnet ClinicManagement.API.dll   platform-account --deactivate --email ops@editeur.tn
```

Its live sessions are refused on their **next request**, not when the token would have expired.

### Recording a payment, correcting one, stopping a cabinet

Everything in « Cabinet subscriptions » above can also be done from the console, on the cabinet's own fiche:

| On the fiche | Does | Same as the verb |
|---|---|---|
| « Enregistrer un paiement » | Records a payment already received and extends the entitlement | `subscription-grant` |
| « Annuler cette période » (motif required) | Strikes the entry through and lets the end date recompute, **possibly into the past** | `subscription-cancel` |
| « Suspendre » / « Lever la suspension » (motif required) | Makes the cabinet read-only **regardless of what it has paid**, and releases it | `subscription-suspend` / `-unsuspend` |

Three things worth knowing before using them:

- **A repeated submission of a payment returns the first outcome, not an error.** Double-clicking records one entry.
  Two *different* payments both land — the surplus one is corrected by a cancellation, never by refusing money that
  has already arrived.
- **Nothing is ever deleted.** A cancelled entry stays visible, struck through, carrying its motif, who cancelled it
  and when.
- **A suspension is not a payment state.** Lifting one restores whatever entitlement the cabinet had — no paid day is
  spent — so a cabinet that was *also* expired stays read-only after the lift, and the console says so rather than
  claiming the practice can work again. Changing a motif means lifting and re-suspending; both halves land in
  « Journal des accès ».

### When the console is unavailable

**The verbs are the fallback, and not a degraded one.** A broken, unreachable or not-yet-configured console must never
stop you unlocking a cabinet that has paid, or correcting a grant keyed against the wrong practice — so
`subscription-grant`, `-cancel`, `-suspend`/`-unsuspend` and `-report` do everything the console's four writes do, over
`docker exec`, with no console account and no second factor in the way. The commands are in « Cabinet subscriptions »
above.

⚠️ Two differences in the verbs' favour: `subscription-report --clinic <id|email>` is the **only** place a period id is
printed, and `subscription-cancel` is what consumes one — so a mistake older than your current console session is
corrected there. And `--until` accepts an explicit end date, which the console deliberately does not offer.

⚠️ One difference the other way: a verb is recorded as `job|subscription-grant` in the cabinet's own journal, where the
console records the account that acted (`console|…`). Both are attributable; only the console names a person.

### What the console can and cannot see

**This is the paragraph to send a clinic that asks, and it is written to be true rather than reassuring.** Copy it as
it stands; the temptation to shorten it to « nous ne voyons pas vos données » is exactly what makes it false.

> **Ce que l'éditeur voit de votre cabinet**
>
> Depuis notre console d'administration, nous voyons de votre cabinet :
>
> - le **nom du cabinet**, sa ville, sa date de création, et le **nom, l'adresse e-mail et le téléphone du compte
>   administrateur** ;
> - des **nombres** : combien de patients sont enregistrés, combien de comptes utilisateurs existent, combien de
>   rendez-vous ont été pris sur 30 jours, combien d'enregistrements ont été faits sur 7 et 30 jours, sur combien de
>   jours le logiciel a servi, la date du dernier enregistrement et celle de la dernière connexion ;
> - votre **abonnement** : son état, sa date de fin, le forfait, et l'historique des paiements que vous nous avez faits ;
> - le **total encaissé par votre cabinet ce mois-ci** — un seul chiffre, celui que votre propre caisse affiche ;
> - et, si votre cabinet a été **suspendu**, le motif que nous avons écrit nous-mêmes en le suspendant.
>
> Nous ne voyons **pas** : vos patients (aucun nom, aucun dossier, aucune fiche de soins, aucune ordonnance, aucun
> document, aucun antécédent, aucune dent), vos rendez-vous eux-mêmes, vos notes, ni le détail de vos factures, de vos
> dépenses ou du solde d'un patient. Nous ne pouvons pas nous connecter à votre application avec un compte de votre
> cabinet.
>
> Ce n'est pas une promesse d'usage : la console ne dispose que d'une liste **fermée** de champs, et toute tentative
> d'en ajouter un autre — un nom de patient, par exemple — fait échouer la compilation du logiciel. Chaque
> consultation de la fiche de votre cabinet par l'un de nos comptes est par ailleurs **enregistrée** : qui, quel
> cabinet, quand.

Three notes for whoever sends it:

- **The monthly collected total is the sentence's load-bearing half.** It is one figure, it is not per-patient and it
  is not clinical — but it is the practice's turnover, and « nous ne voyons rien » is a broader claim than the truth.
  A clinic that discovers the figure afterwards has been misled by the short version, not by the product.
- **The suspension motif is free text we wrote about them.** It is the one item on the list a practice might be
  surprised is readable, and it exists because the screen that can lift a suspension has to be able to say why one
  stands. Write motifs accordingly.
- **What the clinic cannot see is our access log.** « Journal des accès » is the vendor's, not the cabinet's; showing
  a practice which of our accounts opened its file is deliberately out of scope. If a clinic asks for it, that is a
  product decision, not a configuration one.

### Failures worth recognising

- **The API stopped answering after enabling the console.** Should be impossible — `Program.cs` binds the public
  port and the console port in one call, and logs both at startup (« Bound the public API on port … and the
  vendor console on port … »). If that line names only one, that is the bug.
- **Startup refuses with a message naming `Console:Port`.** The console port collides with the port the API
  already answers on. Pick another, or set `CONSOLE_PORT=0` to switch the console off. It refuses to start rather
  than silently making either the console or the whole product unreachable.
- **« Je n'ai pas pu lire les cabinets » on the portfolio.** The database is unreachable or the API cannot read it.
  This screen is written so that a failed read can never render as an empty portfolio: **« aucun cabinet » and « je
  n'ai pas pu lire » are the same picture and opposite facts**, and a vendor who reads the first concludes the
  deployment is empty. Check `GET /health` — it grades the database `Unhealthy` (503) while a storage outage is only
  `Degraded` (200).
- **Every cabinet reads « jamais mesuré » and the « dormant » filter finds nothing.** That is not a portfolio of idle
  practices — it is the nightly counter pass never having run on this deployment. It is `count-clinic-activity`
  (03:00 UTC), and `dotnet ClinicManagement.API.dll verify-schema` reports the same thing as
  `clinic-activity-snapshot-covers-every-clinic`. **A freshly deployed console shows it until the first night has
  passed**, which is expected and says so on screen.
- **A console account still works after you deactivated it.** It must not, and as of Part 7 it does not: refusal is on
  the account's **very next request** (401), not at token expiry. If you ever see otherwise, that is the defect
  `PlatformAccountStateMiddleware` exists to prevent — it was real once, and it was silent.

## Mettre à jour l'application Windows

**There is nothing to do.** A `desktop/**` change merged to `main` releases itself: CI builds the shell, packs a
Velopack feed, uploads it to this server's `deploy/updates/`, and tags the release. Every clinic PC then downloads
a **delta** in the background — measured at **160 KB**, against 49 MB for the old full installer — and offers it:
the strip says the version is ready and « Installer et redémarrer » applies it. No UAC prompt, no download to
find, nothing to hunt for — but **the moment it is installed is the user's**, not ours.

```
merge to main  ->  CI: build + vpk pack + scp to deploy/updates/ + tag client-vX.Y.Z
                   ->  each PC: silent delta download, then « Installer et redémarrer » when the user is ready
```

The version is derived (newest `client-v*` tag, patch bumped) so nothing has to be edited. Push
`client-v1.3.0` by hand when you want a minor bump; the derivation continues from there.

### Why per-user, and what it cost

The shell used to install into `%ProgramFiles%`, and two things followed from that directory automatically:
writing to it needs elevation (a UAC prompt per release), and only an installer can write to it (so the update
unit was the whole 49 MB setup). Velopack installs under `%LocalAppData%` instead — which is exactly how Chrome,
VS Code and Slack avoid both.

⚠️ **The cost is a one-time, per-machine migration, and it cannot be automated.** No updater can move an app out
of a directory it has no rights to write. A PC still carrying the old `%ProgramFiles%` install keeps working
normally and simply never self-updates (`ShellUpdater` sees `IsInstalled == false` and leaves it alone) until
somebody uninstalls APEXA and runs `APEXA-win-Setup.exe` once.

### How to check it is working

```bash
curl -s  https://<domain>/api/meta/client-feed/releases.win.json   # the manifest, naming the newest release
curl -sI https://<domain>/api/meta/client-feed/RELEASES | head -3  # 200
curl -s  https://<domain>/api/meta/client-requirements             # currentShellVersion should match
```

On the server, `ls /opt/clinic-management/deploy/updates/` should show `releases.win.json`, `RELEASES`, the
`.nupkg` packages and `APEXA-win-Setup.exe`. The API reads that folder per request: dropping a newer feed in needs
no restart and no config change.

⚠️ **The bind mount has to exist before any of this works.** `docker-compose.hosted.yml` maps
`./updates:/app/updates:ro`, so the first deploy carrying that file must have recreated `api`. After that the
folder is just a folder.

### The two things that are still deliberate acts

- **`Clients:MinimumShellVersion` is the wall, and CI never touches it.** Leaving it alone means an update is
  *offered*; setting it means an older shell is **refused** and shows « Mise à jour requise » instead of the app.
  Never raise it in the same change that publishes a release — anyone not yet updated is locked out until they
  are, and their only way back is that same feed.
- **A check happens on connect and every two hours**, not instantly. A PC left running notices within the working
  day; one that is switched off nightly notices next morning. Raising the floor mid-day therefore degrades an
  already-open shell into failing requests (the floor is enforced per request, server-side) rather than showing
  the screen that explains why — so raise it out of hours.

---

## Two things this topology does not solve

- **Per-clinic backup and restore.** `backup`/`pitr` protect the whole cluster; restoring one clinic's data without
  touching the others has no path yet.
- **Rolling deploys are safe against migration races** (the startup migrate block holds a PostgreSQL advisory lock,
  so two instances starting together cannot both apply the same migrations) — but a migration that is not
  backwards-compatible with the *old* running instance is still a manual, stop-the-stack operation.
