# `deploy/` — hosted deployments, operator guide

Two hosted topologies live here, sharing all their infrastructure and differing only in **who issues tokens**:

| File | Deployment profile | Login | Who it is for |
|---|---|---|---|
| `docker-compose.prod.yml` | `CloudBrowser` | Auth0 | one backend reached by a browser |
| `docker-compose.hosted.yml` | **`HostedMultiTenant`** | the product's **own** accounts | **many clinics, one backend** — each clinic runs the Windows desktop client and reaches it over the internet |

`postgres`, `minio`, `caddy`, `backup` and `pitr` are defined **once**, in `docker-compose.prod.yml`, and the hosted
file `extends` them — so a backup schedule or a WAL-G prefix has one home. Only `api` and `web` are written out
twice, because only they differ.

> The clinic's own Windows PC is a third topology (`SelfHostedLan`) and is **not** deployed from here — see
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
| `Deployment__Profile=HostedMultiTenant` | the profile is derived from `Auth:Mode` → `CloudBrowser` → Auth0, and no `Auth0__*` is set, so **every request is anonymous-or-401** |
| `DataProtection__KeyRingPath=/keys` **+ the `dataprotection_keys` volume** | **startup fails loud** without the key (by design). With the key but **no volume** it works — until the first redeploy, after which every clinic's reminder credentials are undecryptable and each channel reports « non configuré » with nothing in any log tying that to a deployment |
| `AUTH_MODE=local` (web) | the app expects Auth0 and there is **no way to log in at all** |
| `API_INTERNAL_URL=http://api:5000/api` (web) | login, refresh and change-password 500 — and only those, because only the BFF fetches server-side |
| `AUTH_COOKIE_SECURE=true` (web) | Caddy speaks plain HTTP to the container, so the handler drops `Secure` from an **internet-facing** session cookie |

⚠️ **Back up the `dataprotection_keys` volume alongside `postgres_data`.** A restored database whose key ring is
gone has credentials nobody can decrypt.

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

## Two things this topology does not solve

- **Per-clinic backup and restore.** `backup`/`pitr` protect the whole cluster; restoring one clinic's data without
  touching the others has no path yet.
- **Rolling deploys are safe against migration races** (the startup migrate block holds a PostgreSQL advisory lock,
  so two instances starting together cannot both apply the same migrations) — but a migration that is not
  backwards-compatible with the *old* running instance is still a manual, stop-the-stack operation.
