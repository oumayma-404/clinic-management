# Hand-off prompt — the browser verification `hosted-security-hardening` still owes

Copy everything below the line into a **fresh session**. It is self-contained.

⚠️ **Open the session with `.claude/worktrees/hosted-security-hardening/` as the working directory.** The four
parts live on `feature/hosted-security-hardening`; the main checkout is on `feature/windows-desktop-app` and does
**not** contain this work.

---

## What you are doing

`features/hosted-security-hardening/` is **fully implemented and merged into one PR (#18)**. Every automated gate
is green — 2974 backend tests, `web` 15/15, `console` 14/14, `verify-schema` before/after diffed, `caddy validate`
clean. What it has **never had is a browser**. Your job is exactly the items `stories/progress.md` lists as
*owed*, and nothing else: **do not implement anything**, do not "improve" screens you pass through. You are
verifying, and recording what you find.

Read first, in this order:

1. `features/hosted-security-hardening/stories/progress.md` — the « Still owed » sections of Parts A and D
2. `features/hosted-security-hardening/spec.md` — the **Part 4 gate** and the **Device & Interface Behaviour** table
3. `features/hosted-security-hardening/stories/context.md` — run its staleness diff first, per its own instructions
4. `.claude/rules/frontend-web.md` § 14 — the eye-pass widths and what a "result" has to say

## The two things that make this awkward, stated up front

**1. The API refuses to start on this machine, by design.** `appsettings.Development.json` selects
`HostedMultiTenant`, and Part B's `Startup/TransportAssurance` refuses to boot any deployment that does not host
its own front door unless the database hop is verified-TLS and the object store is TLS. Locally neither is. You
will see:

> `Démarrage refusé : les échanges internes de ce déploiement ne sont pas chiffrés et vérifiés.`

That is the guard working. **Do not disable it and do not edit `TransportAssurance`.**

**2. The pages carry no policy in ordinary dev.** `web/next.config.ts` emits **no CSP** — verified: `curl -D -
http://localhost:3000/login` shows none. The page-side policy comes from `deploy/Caddyfile` in the hosted
topology, and from `SecurityHeadersMiddleware` only where **Kestrel is itself the front door**. So a plain
`next dev` proves nothing about the thing under test.

### Route A — `SelfHostedLan` (recommended: start here)

`TransportAssurance` gates on `!SelfHostsFrontDoor`, so `SelfHostedLan` is exempt — and in that profile Kestrel
**is** the browser-facing endpoint, proxying every non-`/api` route to Next, which means
`SecurityHeadersMiddleware` covers **the pages too** (its own docstring says so, AC-12.5). Combined with
`Security__EnforceCsp=true` that gives you the **real middleware serving the real policy on the real page
bundle** — which is what the requirement is about.

```bash
# Kill whatever is already listening first — the running API (PID on :5000/:5443) is the MAIN CHECKOUT's
# pre-Part-D build. Its headers are the "before" picture, not this work.
#   before:  Content-Security-Policy-Report-Only: … 'unsafe-eval' …   (no Permissions-Policy, no COOP/CORP)
#   after :  Content-Security-Policy: … (no 'unsafe-eval') … report-to csp-endpoint

cd api/ClinicManagement.API
Deployment__Profile=SelfHostedLan \
Security__EnforceCsp=true \
Hosting__WebPort=3000 \
dotnet run -c Release -p:BaseOutputPath=<somewhere outside the repo>/api/
```

Then `cd web && npm run build && npm start` on 3000. Kestrel self-signs into `.local/` on first boot, so the
browser-facing origin is **https://localhost:5001** and Playwright needs `ignoreHTTPSErrors: true`.

⚠️ **What Route A does and does not prove.** It proves the **policy string** does not break the app, and the
string is byte-identical across the middleware, both `Caddyfile` sites and `console/next.config.ts` — held by
`ContentSecurityPolicyAgreementTests`, so testing one tests the text of all three. It does **not** exercise
Caddy's own header emission, the console site, or any `HostedMultiTenant`-only surface (the second factor at
login, enrolment, recovery codes — `RequiresAdminSecondFactor` is false on `SelfHostedLan`).

### Route B — the real hosted stack, for what Route A cannot reach

`docker compose -f deploy/docker-compose.hosted.yml` under a scratch project name (Part B used `-p hshb`). You
will need to supply `deploy/.env.hosted` from `.env.hosted.example` — including the **two new required secrets**,
either of which fails startup loudly if absent:

```bash
mkdir -p deploy/secrets
openssl rand -base64 48 > deploy/secrets/audit-chain-key        # FR-4.1
# plus a PKCS#12 for DataProtection__CertificatePath — deploy/KEY-CUSTODY.md § 1 has the two openssl lines
```

For a local run, replace the public site's automatic TLS with `tls internal` in `deploy/Caddyfile` **in your
working copy only, and revert it** — ACME needs a real domain. Take Route B only after Route A is done; it is the
only way to see the console's brand-new policy and the `HostedMultiTenant` login flow.

## Credentials

A test cabinet exists on the dev database, created so the audit chain had genuinely chained rows to tamper with:

- clinic **« Cabinet Chaîne Test »**, admin **`chain-proof@example.test`**, temporary password **`sRZ5dreCshDu`**
- it carries `MustChangePassword`, so the first sign-in routes to `/change-password` — walk that, it is a real
  flow and Part A touched it

Other clinics exist on that database from earlier work. The dev database already has Part D's migration applied.

## What to verify, and what a "result" looks like

### 1. The enforcing policy — the highest-value item

The failure mode is **a broken screen for a clinic**, and it is the one thing no test in the repo can see.

Collect violations properly — a `console` listener misses the ones the page reports natively:

```js
page.on('console', m => { if (m.text().includes('Content Security Policy')) violations.push(m.text()) })
await page.addInitScript(() => {
  window.__csp = []
  document.addEventListener('securitypolicyviolation', e =>
    window.__csp.push(`${e.violatedDirective} blocked ${e.blockedURI} on ${location.pathname}`))
})
// …then read window.__csp after each navigation
```

Walk **every** route in `web/app/` (the table in `web/CLAUDE.md` lists them), and specifically the four `blob:`
paths the spec names, because those are what `object-src`/`frame-src`/`img-src blob:` exist for:

- a **PDF preview** on a patient file
- a **document print** (`/documents/[type]` — its Print goes through the preview iframe)
- a **CSV export** from any of the nine lists carrying the button
- a **patient-file download**

**Expected: zero violations.** A violation is a real finding — record it, do not widen the policy to make it go
away without saying so.

Also confirm on the wire what changed:

```bash
curl -sk -D - -o /dev/null https://localhost:5001/login | grep -iE "content-security|permissions|cross-origin|reporting"
```

`Content-Security-Policy` (not `-Report-Only`), **no `'unsafe-eval'`**, plus `Permissions-Policy`,
`Reporting-Endpoints`, `Cross-Origin-Opener-Policy`, `Cross-Origin-Resource-Policy`.

⚠️ And check `POST /api/csp-report` actually receives something — trigger a deliberate violation (inject an
`<img src="https://example.com/x.png">`) and confirm a `CSP violation:` line in the log **with the address
reduced to its route pattern** (`/patients/{id}/files`, never a real id). That last part is FR-4.4 and is the
whole reason the endpoint scrubs.

### 2. The eye pass — owed for Parts A **and** D

At **320 / 390 / 820 / 1180 / 1440**, plus a landscape phone (~844×390), plus with the on-screen keyboard up:

| Surface | Part |
|---|---|
| `/login` — all four modes: password, code, enrolment, recovery codes | A |
| `/securite` — « Sécurité » | A |
| The **step-up sheet** (a `Sheet` below `md:`, sized in `dvh`) | A / D |
| The **archive card** in « Paramètres » → Sauvegarde, including the `coarse:`-only « Téléchargez l'archive depuis un ordinateur » note | D |

Check the § 13 floor as you go: `Escape` closes, focus returns to the trigger, nothing is trapped, typed input
survives a resize across the `md:` hinge, every control ≥ 44 px on a coarse pointer.

**Name the widths you actually checked.** "Responsive ✓" with no widths is not a result.

### 3. Two flows Part A could not walk

Both were left owed because they need real credentials:

- the **Google Calendar OAuth callback** — a `SameSite=Strict` session cookie is not sent on a top-level
  navigation *into* the app, which is why the OAuth `state` cookie is deliberately relaxed. If it breaks, the
  relaxed form stays and the reason gets recorded (that is what FR-1.7 says).
- the **e-mailed signup verification link** — same question. `clinic-mailpit` is running on :8025, so the mail is
  readable.

## Recording it

Append to `features/hosted-security-hardening/stories/progress.md` under a new
**« Browser verification (session of <date>) »** heading, and move the corresponding rows out of each part's
« Still owed » list. State results as facts with numbers, and **if something could not be run, say so** rather
than leaving it ambiguous — that is the standard the rest of that file is held to.

If the walk finds a real defect, the honest move is to **capture it and report it**, not to fix it quietly inside
a verification session — PR #18 is already open and a silent change to it is worse than a named finding.

## Housekeeping

- Kill anything you start; the API and web on :5000/:5443/:3000 were **not** started by you.
- Part B may have left a scratch stack: `docker compose -p hshb -f deploy/docker-compose.hosted.yml down -v`.
- The dev database is the main checkout's `clinic-postgres` on 5432 (`clinic_user` / `clinic_password` /
  `clinic_management`).
- Playwright is available (`npx playwright`, 1.62.1; `playwright-core` is in `web/package.json`). There is **no**
  Playwright MCP tool and no `agent-browser` on PATH — drive it from a Node script.
