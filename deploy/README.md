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
| `messaging-report` | **yes** | `0` clean · `1` couldn't run · `2` findings |
| `messaging-grant` / `-cancel` | **yes** | `0` / `1` |
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

## The vendor console

A private back-office where you see how much each cabinet actually uses the product and record its payments. It
exists **only on this profile**, and it is **off until you set `CONSOLE_PORT`** — absent, there is no second
listener, no reachable route, and every `/api/platform/*` path 404s. Off means absent, not present-and-refusing.

### Opening it

It is published on **loopback only**, so there is no address to reach from the internet and no DNS name for one.

```bash
ssh -L 9443:127.0.0.1:9443 <host>
# then, on your own machine:
open https://127.0.0.1:9443
```

Expect a **certificate warning**. That site uses Caddy's internal CA, because `127.0.0.1` has no public name for
Let's Encrypt to issue a certificate against. A warning on `{DOMAIN}` is a different event entirely and should
never be dismissed.

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

## Two things this topology does not solve

- **Per-clinic backup and restore.** `backup`/`pitr` protect the whole cluster; restoring one clinic's data without
  touching the others has no path yet.
- **Rolling deploys are safe against migration races** (the startup migrate block holds a PostgreSQL advisory lock,
  so two instances starting together cannot both apply the same migrations) — but a migration that is not
  backwards-compatible with the *old* running instance is still a manual, stop-the-stack operation.
