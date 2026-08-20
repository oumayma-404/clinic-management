# Deploying the vendor console on Render

**Applies to:** the `HostedMultiTenant` deployment currently on Render (api + web + managed PostgreSQL).
**Written:** 2026-08-20.

The reference topology in [`README.md`](README.md) § « The vendor console » publishes the console on **loopback
only**, reached over an SSH tunnel. Render has no equivalent: every Render web service gets exactly one public
port and no tunnel into a private one. This file is what to do instead, and what it costs.

> ⚠️ **Read [`README.md`](README.md) § « When the console is unavailable » first.** The five console verbs
> (`subscription-grant`, `-cancel`, `-suspend`/`-unsuspend`, `-report`, and `reset-user-totp`) do everything the
> console's writes do, over a shell, with no console account and no second factor in the way — and with **no
> internet-facing surface at all**. If you have a shell on the API service, that is the lower-risk answer and
> nothing below is needed. Deploy the console because you want a *person* named in the journal and confirmation
> panels in front of destructive actions, not because the verbs are missing.

---

## The one thing that decides the architecture

`Console:Port` is a **second Kestrel listener**, and `ConsolePortGate` refuses in **both** directions: on the
console port only `/api/platform/*` answers, and everywhere else `/api/platform/*` never answers. That gate keys
on the real `Connection.LocalPort`, so it cannot be satisfied by a proxy header.

Measured on a running instance with `Console:Port=5443` beside a public `5000`:

| Path | on `5443` (console) | on `5000` (public) |
|---|---|---|
| `/api/patients` | **404** | 200 |
| `/api/appointments` | **404** | 200 |
| `/api/auth/mode` | **404** | 200 |
| `/health` | **404** | 200 |
| `/api/platform/auth/meta` | 401 | **404** |

That `/health` is 404 on the console port is why the health check path must be left unset below — and the `401`
is the signal to look for in the test that follows.

Render routes external traffic to **one** port per service (`$PORT`, default `10000`). So the question is whether
another Render service can reach a port on the API container that Render is *not* routing.

**Test it before choosing.** From a shell on the console service (or a one-off job), with the API service's
internal hostname:

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://<api-internal-host>:5443/api/platform/auth/meta
# 401  → the private port is reachable. Use Option 1.
# 000  → it is not (or your plan has no private networking). Use Option 2.
```

`401` is the success signal here: the route exists and demanded a console token. A `404` would mean you reached
the *public* listener instead, where the gate refuses console paths by design.

---

## Option 1 — console API stays private (preferred)

One API service. The console port is bound but **never routed publicly**, so the highest-privilege API surface in
the product stays unreachable from the internet exactly as the tunnel design intends. Only the console's *web app*
becomes public, and it holds no credential of its own — the token lives in an HttpOnly, `Secure`, `SameSite=Strict`
cookie that its own server-side BFF attaches.

### API service (existing) — add two variables

| Variable | Value | Why |
|---|---|---|
| `Console__Port` | `5443` | Any port that is **not** `$PORT`. `ConsoleListenerPlanning` throws at startup if it collides with the public port, naming both — a loud refusal, not a silent misbind. |
| `Console__SigningKey` | a fresh ≥32-byte random string | ⚠️ **Never a copy of `Auth__Local__SigningKey`.** The distinct key, issuer and audience are what make a clinic token fail on a console route by *signature* rather than by policy (AC-1.4). Startup fails loud if it is absent while `Console__Port > 0`. |

Generate the key with `openssl rand -base64 48`. Store it in Render as a secret, and **back it up where you back
up the key-ring certificate** — see `KEY-CUSTODY.md`.

### Console service (new)

| Setting | Value |
|---|---|
| Type | Web Service, Docker |
| Dockerfile | `console/Dockerfile` |
| Docker context | `console/` |
| `CONSOLE_API_URL` | `http://<api-internal-host>:5443/api` |

Nothing else. The image already sets `HOSTNAME=0.0.0.0` and honours Render's `PORT`, `output: "standalone"` is
configured, the CSP is `default-src 'self'` with `connect-src 'self'` (the browser only ever talks to the console's
own origin), and the session cookie is unconditionally `Secure`, which Render's HTTPS satisfies.

**Leave the health check path unset.** There is no anonymous 2xx route under `/api/platform` — `auth/meta` is
deliberately authenticated — so any path you name answers 401. Render only needs the port open.

---

## Option 2 — console API public, serving nothing but `/api/platform/*`

Use this only if the private-port test failed. A **second** API service from the same image, whose routed port
*is* the console port. `ConsoleListenerPlanning` refuses to let the two be equal, so the public listener is moved
out of the way rather than removed:

| Variable | Value | Why |
|---|---|---|
| `ASPNETCORE_URLS` | `http://+:5000` | Makes `ResolvePublicPort` answer `5000`, so it does not collide with the console port below. Bound, but Render routes nothing to it. |
| `Console__Port` | `10000` | Must equal Render's `$PORT` for this service. Set `PORT=10000` explicitly too, rather than relying on the default. |
| `Console__SigningKey` | the same key as Option 1 | |

Plus every variable the existing API service has (connection string, `Auth__Local__SigningKey`, the Data Protection
certificate, `Security__AllowUnverifiedInternalTls`, …). It is the same application; it needs the same
configuration.

Then point the console service's `CONSOLE_API_URL` at `https://<console-api-service>.onrender.com/api`.

**What this buys:** `ConsolePortGate` means that public service answers **only** `/api/platform/*`. Every clinic
route — every patient, appointment and document endpoint — 404s on it. The exposure is exactly the platform
surface and not one route more.

**What it costs, and these are real:**

- ⚠️ **Two API instances share one database.** `AddHangfireServer()` is unconditional, so both run a Hangfire
  server. Hangfire coordinates through the database, so a recurring job **fires once** rather than twice — no
  duplicate reminders — but you are paying two servers' worth of polling against a free-tier PostgreSQL with a
  low connection limit.
- ⚠️ **Both run `Database.Migrate()` at startup.** EF takes no advisory lock, so two simultaneous cold starts can
  race on a migration. Deploy the console API service **after** the clinic API has finished starting, and never
  redeploy both at once.
- ⚠️ The console API is internet-facing. See « What you are giving up » below.

---

## Bootstrapping the first console account

Unchanged from [`README.md`](README.md) § « Bootstrapping the first account » — there is no sign-up screen and no
screen anywhere in the console that lists, creates or deactivates an account. On the API service's shell:

```bash
dotnet ClinicManagement.API.dll platform-account create --email <you>@<vendor> --name "Your Name"
```

It prints a one-time password and a TOTP enrolment secret, **once**. Then, in the console:

1. Sign in → it refuses with « ce compte doit enrôler un second facteur ».
2. Enrol from that screen with a code from the secret → it returns eight recovery codes, shown once.
3. Sign in properly.
4. Change the one-time password. Every console route but that one is refused until you do.

---

## What you are giving up, and what to do about it

The console is the only **cross-tenant** surface in the product. One credential reaches every cabinet: suspend or
release any of them, grant or cancel entitlement, reset any clinic account's second factor, restore a cabinet from
a supplied archive (the one console action that writes *clinical* records — API-only, no UI), and read every
cabinet's administrator name, e-mail, activity and revenue. A compromised clinic account reaches one practice; a
compromised console account reaches the fleet.

What still protects it: a 12-character password floor, **mandatory** TOTP, per-**account** lockout, rate limiting
on `login`/`totp/enrol`/`recovery`, a distinct signing key/issuer/audience, `PlatformAccountStateMiddleware`
re-reading the account on every request, and an append-only access ledger. That is a serious stack. What
publishing removes is the layer in front of all of it: today there is **no internet-facing address at all**.

Do these, in this order:

1. **Run the two key-ring checks against production, before anything else.**
   ```bash
   dotnet ClinicManagement.API.dll verify-schema
   ```
   `key-ring-protection` and `secrets-protected-under-current-ring` have **never been run against this
   deployment** (`follow-up/render-free-tier-transit-relaxation.md`). If the ring is not actually encrypted at
   rest, a database dump discloses every console TOTP secret and the second factor above is decorative.
2. **Put an IP allowlist or an identity proxy (Cloudflare Access, Tailscale) in front of the console service** if
   your plan allows it. This is the single highest-value mitigation: it restores most of what the tunnel gave you.
3. **One console account.** Not one per person unless you need to tell them apart in the journal — every account
   is another credential that reaches every cabinet.
4. **Read `/journal`.** It names the acting account's address on every row, which it did not do before
   2026-08-20 — `PlatformSessionContext.GetEmail()` read the wrong claim name and every row had been blank since
   the console shipped. Detection was degraded for the whole life of the feature; it works now, so use it.
5. **Deactivate accounts you stop using**, with `platform-account --deactivate --email …`. Live sessions are
   refused on their next request, not at token expiry.

## The standing answer underneath this

`DataResidencyAssurance` and *loi organique 2004-63* art. 51–52 already require this deployment to move off
Render to a Tunisian or European VM — see `README.md` § « Résidence des données » and
`follow-up/render-free-tier-transit-relaxation.md` § 4. On a VM the loopback-plus-tunnel design works as written,
`Security:AllowUnverifiedInternalTls` can be deleted, and this entire trade-off disappears. Treat everything above
as an interim arrangement with a known end date, not as the topology.
