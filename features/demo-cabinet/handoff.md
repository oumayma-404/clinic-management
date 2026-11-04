# Handoff — the public demo cabinet

> **Start here, new session.** This file is the whole brief. Written 2026-11-03 at the end of the
> « why trials don't stick » session. Read it top to bottom, then the files in § 9, then start at § 10.

## 1. What the owner asked for

> « The example demo app that users can open and walk through, read-only, that will be added in the website. »

- A **real APEXA cabinet, already full of realistic data**, that any visitor of **apexa.tn** opens with one click.
- **No account, no sign-up, no security code**. The visitor walks every screen.
- **Read-only**: nothing a visitor does is saved, and they are told so kindly, with a way to create their own
  cabinet.
- Linked from the website: an « Essayer la démo » button.

### Why it matters (measured, not assumed)

Production read of 3 Nov 2026 (memory `trial-dropoff-findings`; page
https://claude.ai/artifact/1oag7VWiFRgLTD7L89SHdt):

- 12 self-serve trials left, 3 cabinets stayed.
- **5 of the 12 got in and entered nothing.** They signed up at 20:00–22:00, alone, with no patient in front
  of them, and met a dashboard full of zeros. 3 came back on later days and still entered nothing.
- A demo answers « what does this do for me? » **before** sign-up, with real screens full of real-looking
  work: agenda, odontogram, devis, caisse, documents.

It is **option 2** of that analysis. Option 1, « Guided first days », is being planned in parallel (memory
`guided-first-days-plan`, page https://claude.ai/artifact/JEYyUxrWyNS34xoNGa3v4z). The demo does not replace
it: the demo sells **before** sign-up, the guided first days help **after**.

## 2. Hard constraints

| Constraint | Source |
|---|---|
| Never put anonymous visitors on the server process and database that hold real patients | security + compliance: nothing filed with INPDP, and patient data sits in London (memory `compliance-status-tunisia`) |
| Fictional data only, names from « Friends », no real-looking phone numbers | site rule (memory `site-pages-are-image-led`), and the « Premiers pas » videos use the same cabinet |
| No marketing claim the product does not keep: no CNAM, no SMS/WhatsApp reminders as working, no « sans internet » | `site/BRIEF.md` banned claims |
| The website cannot embed the app: the app sends `X-Frame-Options: SAMEORIGIN` and `frame-ancestors 'self'` | `SecurityHeadersMiddleware.cs:115,167`, `deploy/Caddyfile:131` → the demo opens in **its own tab** |
| Nothing touches production (VPS, DNS, deploy) without the owner's explicit OK for that exact step | memories `never-touch-prod-without-consent`, `never-push-without-consent` |
| The website ships only when the built page reaches the live repo: two pushes, and diff origin first | memory `website-work-needs-two-pushes` |
| Every deployment difference is a **named capability** on `DeploymentProfile`, never a boolean read twice | root `CLAUDE.md`, `features/multi-tenant-cloud/notes.md` |
| Frontend device contract: 320 px, 44 px coarse targets, sheets on phones | `.claude/rules/frontend-web.md` |

## 3. The architecture options (decide in the first hour)

| # | Option | Verdict |
|---|---|---|
| **A** | **A separate demo deployment** (`demo.apexa.tn`): the same images as production, a new profile `DeploymentKind.PublicDemo`, its own database, refusing writes by capability, reseeded every night from a scenario dated relative to today | ✅ **recommended** |
| B | A flagged demo cabinet **inside** production (app.apexa.tn), read-only by a per-clinic flag, anonymous sessions on the production API | ❌ anonymous access on the server holding real patients; and every per-clinic job and metric (console portfolio, activity counter, reminders outbox, backups, archives, subscription jobs) must learn the flag. That is this repo's commonest defect shape (memory `fixes-dont-propagate`) |
| C | A static clickable mock-up on the website (screenshots with hot spots) | ⚠️ zero server risk, but it is not the app, it goes stale with every UI change, and the odontogram, lists and documents cannot really be explored |
| D | A later variant of A: **each visitor gets a private writable copy**, purged after 24 h | ⚠️ the most convincing demo, but not what was asked (read-only). It costs a seed per visitor and abuse handling. Keep as phase 2 of A |

**Why A:** it is the only option with no path from the internet to real patient data, it always shows the
current product (same images, same deploy), and the architecture already has the right seam: a deployment
kind is a set of named capabilities, and `ClinicRelay` proved a third kind can run the same binaries with
different answers.

## 4. Facts already verified (origin/main @ `66ecc657`, 30 Oct)

⚠️ The checked-out branch `feature/security-remediation` is ~184 commits behind `origin/main`. **Branch
from `origin/main`**, and read with `git show origin/main:<path>` until you have your own branch.

**Deployment profiles**
- `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs`
  - `enum DeploymentKind { SelfHostedLan, HostedMultiTenant, ClinicRelay }` at :11.
  - Capability matrix in `For(kind)` at :413; `Resolve(IConfiguration)` at :379. Config key is
    `Deployment:Profile`; an absent profile with a non-Local auth mode **throws**.
  - `DeploymentProfileTests` holds the matrix: **every capability needs an explicit answer for the new kind**.
- Capabilities today:
  - Accounts and auth: `UsesLocalAccounts`, `FailClosedAuthz`, `EnforcesTokenState`, `AllowsSelfRegistration`,
    `AllowsPublicClinicSignup`, `AllowsPasswordResetByEmail`, `RequiresAdminSecondFactor`.
  - Storage and hosting: `UsesDiskStorage`, `SelfHostsFrontDoor`, `SelfSignsCertificate`, `RunsAsWindowsService`,
    `ExposesTrustEndpoints`, `HasLocalDbTooling`.
  - Startup: `DefersMigrations`, `RunsStartupBackfills`.
  - Vendor and business: `ExposesMetaOnboarding`, `ServesPlatformConsole`, `RequiresSubscription`,
    `BacksUpItsOwnData`, `SellsVendorMessaging`.
  - PC de secours: `PublishesChangeFeed`, `MirrorsCloudClinic`.
  - Background work: `RunsClinicJobs`, `DispatchesOutboxes`, `RunsCutJobs`.

**What the demo must switch off, and the capability that already does it**

| Must not happen in the demo | Capability (value for `PublicDemo`) |
|---|---|
| SMS / WhatsApp / e-mail / push leaving the server | `DispatchesOutboxes = false` |
| Security code demanded from the demo admin | `RequiresAdminSecondFactor = false` (the check runs on every request in `LocalAuthEnforcementMiddleware.cs:76-87`) |
| Sign-up, password reset, self-registration | `AllowsPublicClinicSignup`, `AllowsPasswordResetByEmail`, `AllowsSelfRegistration` = false |
| Vendor console, subscriptions, vendor messaging, backups | `ServesPlatformConsole`, `RequiresSubscription`, `SellsVendorMessaging`, `BacksUpItsOwnData` = false |
| PC de secours feed and cut jobs | `PublishesChangeFeed`, `MirrorsCloudClinic`, `RunsCutJobs` = false |
| Statuses moving with the clock (« En cours », « Séance passée ») | `RunsClinicJobs = true`: `AppointmentProgressJob` (`start-running-appointments`, every minute) is what makes « today » look alive |

**New capabilities to add:** `RefusesWrites` (the read-only gate) and `IssuesDemoSessions` (the anonymous
entry). Each is false for every existing kind.

**Recurring jobs** (`Program.cs:1250-1500`): `process-notifications`, `start-running-appointments`,
`flag-expiring-stock`, `post-monthly-expenses`, `purge-expired-sessions`, `run-scheduled-backups`,
`count-clinic-activity`, `take-recovery-points`, `dispatch-os-push`, `warn-subscription-expiry`,
`review-messaging-allowances`, `watch-relays`, `prune-clinic-changes`, `sync-google-calendar`. Check each one
against the demo profile: most are already gated by a capability; any that is not is a bug to fix here.

**Read-only precedent to copy, not reuse:** `SubscriptionGateMiddleware.cs:74,104-126`.
- It answers every non-GET of an expired cabinet with **402** `subscription_required`.
- Exemptions are marked by `[AllowsWithoutSubscription]`, and `SubscriptionExemptionCoverageTests` classifies
  every endpoint.
- The demo needs its **own** gate, attribute and coverage test. Reusing the subscription one would show
  « Votre abonnement a expiré » to a visitor, and couples two different questions.

**Sessions**
- A session is a `SessionFamily` per device. Refresh rotation **is** the theft detection (memory
  `second-browser-ends-the-owners-session`), so every visitor must get **their own family**. Never hand out a
  shared refresh token.
- Login writes `User.LastLoginAt`, through a POST. The demo-session endpoint mints a family directly: look at
  `LoginCommand.cs:252-277` for the mint, and `TradeRelayAssertionCommand.cs:76` for a non-password mint
  precedent.

**Console verbs** (`api/ClinicManagement.API/Maintenance/`) run before the web host boots, with the container's
environment. `ProvisionClinicCommand.cs` (`provision-clinic`) is the pattern for a `seed-demo` verb. Others:
`verify-schema`, `reconcile-money`, `restore-backup`, `subscription-*`, `platform-account`.

**Demo assets that already exist**
- `site/src/img/pano.jpg`, a panoramic radiograph usable as a patient file.
- The « Premiers pas » video cabinet (« Cabinet Dr Monica Geller », patient Ross Geller):
  - letterhead and cachet images, demo DB dumps `raw/tuto/after-N.dump` and scripts, all in
    `C:\dev\clinic-film` (not git);
  - see memory `premiers-pas-videos`.
- Reuse the names and the cabinet so the videos and the demo tell one story.

**VPS capacity** (read-only, 3 Nov):
- 6 vCPU, 11.6 GB RAM (9.8 GB available), 47 GB disk free.
- Production uses about 1.3 GB (api 670 MB, minio 280 MB, web 90 MB, console 100 MB, postgres 85 MB).
- A demo api + web pair (~0.8 GB) fits beside it; a separate small VPS is the alternative (§ 8).

## 5. The recommended design (option A), piece by piece

### 5.1 Profile
- `DeploymentKind.PublicDemo` + its column in `For()`, every capability answered explicitly with a one-line
  reason, as the other columns do.
- Storage: hosted path (MinIO) with a **separate bucket and keys** (`clinic-demo`), never the production
  bucket.
- `FrontendUrl = https://demo.apexa.tn`.
- No SMTP, Meta, Google or push configuration in its environment at all.

### 5.2 The read-only gate
- `DemoReadOnlyMiddleware`, active only when `RefusesWrites`. Every non-GET/HEAD API request gets **403**
  `demo_read_only`: « Démonstration : rien n'est enregistré. Créez votre cabinet pour essayer avec vos
  données. »
- Allow-list by attribute `[AllowedInDemo]`: the demo-session endpoint, token refresh, logout. Nothing else to
  start with; a visitor's dashboard customisation is refused too, because the demo user is shared and one
  visitor's choices would change everybody's screen.
- `DemoWriteExemptionCoverageTests`: every non-GET endpoint is either refused or explicitly allowed. It is
  derived by reflection, like `SubscriptionExemptionCoverageTests`.
- Server-side writes during a GET stay allowed: the record-access audit (`Action = 3 Read`) and the jobs.

### 5.3 The entry
- `demo.apexa.tn` with no session → `/demo`, a one-screen page: « Cabinet de démonstration APEXA », one button
  « Entrer dans la démo ». Optional later: two personas, « Je suis dentiste » / « Je suis secrétaire ».
- The button calls `POST /api/auth/demo-session`:
  - anonymous, `AnonymousAuthPolicy` rate limit plus a per-IP limit, `[AllowedInDemo]`, 404 unless
    `IssuesDemoSessions`;
  - mints a **new session family** for the demo admin, with a short lifetime (2 h);
  - the BFF sets the same cookies as a login.
- **Separate signing key, Data Protection key ring and DB role from production**, so a demo token can never be
  valid on app.apexa.tn. Prove it with a test or a manual probe.
- On the demo host, the login page is `/demo`. « Mot de passe oublié » and « Créer mon cabinet » point to
  **app.apexa.tn/signup**.

### 5.4 What the visitor sees
- A slim, permanent band at the top: « Cabinet de démonstration · rien n'est enregistré ·
  [Créer mon cabinet gratuitement] » → `https://app.apexa.tn/signup`. It must respect the 320 px contract and
  the safe areas.
- One client-side handler for `demo_read_only` (in `web/lib/api/client.ts`): a toast with the same CTA, and the
  dialog stays open with its input (frontend rule § 13). It must **not** be the generic error banner.
- Surfaces to switch off or adapt for `RefusesWrites`:
  - the « Séance terminée » post-visit popup: it would ask the visitor to record fiches all day;
  - the « Premiers pas » card if option 1 ships first: seed `Clinic.CreatedAt` months back, or gate it;
  - « Abonnement » and « Sécurité » (no subscription; TOTP management would be refused);
  - « Rappels » shows its « bientôt disponible » page, which is fine: the owner wants it kept and explained.
- `X-Robots-Tag: noindex` on the demo host. It is a tool, not a page to rank.

### 5.5 The data: the long pole
A verb `seed-demo` (and a nightly job calling the same code) that builds « Cabinet Dr Monica Geller, Tunis »
**through the application's own commands**, never raw SQL. Every invariant then holds (numbering, money
ledger, chart derivation, note d'honoraires), and a model change breaks the seed at compile time instead of
silently.

**Scenario, dated relative to the Tunisian « today » (`ClinicClock`):**

| Area | Content |
|---|---|
| Team | Dr Monica Geller (admin, dentist), a second dentist, Rachel Green (secrétaire) |
| Patients | ~40, « Friends » names, antécédents and allergies on a few (one penicillin allergy to show the alert) |
| Agenda | the last 2 weeks done (each with a fiche); today: a few done, one « Au fauteuil », the rest to come; the next 2 weeks booked; one « créneau occupé »; a congé day |
| Clinical | odontograms with caries, obturations, couronnes, a bridge (pontique + piliers), an implant, absent teeth, « à traiter » marks |
| Treatments | devis at every state: followed draft, accepted with échéancier, a multi-séance couronne in progress, an implant waiting osseointegration, completed, one stopped |
| Money | payments cash / cheque / card across the month, one cheque to deposit, notes d'honoraires, monthly expenses, one patient owing (« Reste à payer »), one avoir |
| Documents | ordonnances and a certificat on the cabinet's letterhead and cachet |
| Files | the panoramic (`site/src/img/pano.jpg`), a few photos |
| Stock and lab | one item low, one lot expiring, two fournisseurs, one prothèse late at the lab |
| Worklists | 1–2 séances passées without a fiche so « À clôturer » is not empty; a « suite à planifier » |

**Freshness:** dates drift every day, so the cabinet is **rebuilt every night** (03:00 Tunis). Decide between:
- (a) drop and recreate the demo database, then `seed-demo`. Simplest, but needs a role that may drop it;
- (b) seed a fresh cabinet each night, repoint the demo user, delete yesterday's through the existing removal
  path (`features/clinic-account-removal/`).

**Check the seed with the read-only verbs**: `verify-schema` and `reconcile-money` on the demo database must
both exit 0, every night. A seed that leaves money drift would show a visitor a wrong caisse.

### 5.6 Infrastructure (each step needs the owner's OK)
- DNS: `demo.apexa.tn` A record → the VPS (OVH DNS, her account).
- `deploy/docker-compose.demo.yml`:
  - `clinic-api-demo` and `clinic-web-demo` on the **same images** as production, extending the prod infra
    base: shared Postgres container with **its own database `clinic_demo` and its own role**, shared MinIO
    with its own bucket and keys;
  - env `Deployment__Profile=PublicDemo`.
- Caddy: a site block for `demo.apexa.tn` (automatic TLS). See memory `vps-config-edits-need-force-recreate`:
  a single-file bind mount needs `--force-recreate`, and `caddy validate` lies against stale content.
- `deploy-hosted.yml`: bring the demo services up with the same image tag, so the demo can never lag the
  product.
- Secret files on the VPS must be owned by uid 1654 (memory `vps-secret-files-must-be-uid-1654`).

### 5.7 The website
- « Essayer la démo » in the nav, the hero, and on each feature page (agenda, dossier patient, odontogramme).
  It opens `https://demo.apexa.tn` in a new tab.
- Umami event `essayer-demo`. The capture listener keys on the link's href (memory
  `counting-downloads-and-visits`).
- `site/` changes go through the owner (two pushes, diff origin first).

## 6. Tests and checks
- `DeploymentProfileTests`: the new column, every capability.
- `DemoReadOnlyMiddleware` unit tests: GET passes; POST/PUT/DELETE refused with `demo_read_only`; allowed
  endpoints pass; inert when the capability is off.
- `DemoWriteExemptionCoverageTests`: derived, both directions.
- Demo-session tests:
  - 404 when the capability is off;
  - always the demo user, never another;
  - a new family per call;
  - lifetime cap;
  - rate limit.
- Existing coverage guards to keep green: `ControllerAuthorizationCoverageTests`,
  `RelayOnlineOnlyCoverageTests`, `SubscriptionExemptionCoverageTests` (the new endpoint must be classified).
- The seed: run locally against a throwaway DB, then `verify-schema` and `reconcile-money` → exit 0. No unit
  test touches a database here (`CLAUDE.md`).
- Browser pass (`/test-in-browser`): every sidebar screen at 320 / 390 / 820 / 1180 / 1440 and 730 px tall;
  every save refused with the toast; nothing sends; the band never covers a control.
- Full unfiltered `dotnet test -c Release` to a temp `BaseOutputPath`, plus `check:responsive`, `tsc --noEmit`
  and `npm run build`.

## 7. Traps that will bite (from the repo's own record)
- **A capability read as another one** is a security decision made by accident. `RefusesWrites` is not
  « expired subscription ».
- **An `Unset` tenant scope reads zero rows with no error.** The seed verb and the nightly job have no HTTP
  context: they must declare `UseClinic(id)` / `UseSystemWide(reason)`.
- **`Version == 0` skips the concurrency check**; seed through commands that do not need a version, or pass the
  real one.
- **Never `DateTime.UtcNow` / `DateTime.Today`** for dates in the scenario: `ClinicClock`. Use
  `LastTickOfLocalDayUtc` for inclusive money ranges.
- **A multi-séance act is split by default** and both booking doors materialise treatments on save
  (`materialiseTreatments`). Seed treatments through the same commands the UI uses, or the plans will not
  look like real ones.
- **Money for a multi-séance act goes on the treatment, never the séance's note**: see the `CLAUDE.md` bullets
  before seeding money.
- **`AppointmentProgressJob` writes an appointment every minute.** Seed past visits as completed, with their
  fiches, or « À clôturer » fills up.
- **Shared Chrome profile and shared stack:** test locally on the isolated stack (`:5099` / `:3099`, memory
  `isolated-test-stack`), with `stack-lease.ps1 status` first (`.claude/rules/shared-stack.md`).
- **Several sessions edit this checkout.** Work in a git worktree off `origin/main`; `git diff HEAD --numstat`
  before staging.

## 8. Decisions only the owner can make (ask once, with a recommendation)

| # | Question | Recommendation |
|---|---|---|
| 1 | Address: `demo.apexa.tn`? | yes |
| 2 | Same VPS, or a separate small one (~5–10 €/month)? | same VPS: capacity is there and isolation comes from the database, role, bucket and keys. A separate VPS is safer still and costs money |
| 3 | One persona (dentist) or two (dentist + secretary)? | dentist first, secretary in phase 2 |
| 4 | Read-only, or later a private writable copy per visitor (option D)? | read-only now, D as phase 2 |

## 9. Read before writing code
- Root `CLAUDE.md` (map + « Traps that bite »), `ARCHITECTURE.md`.
- `features/multi-tenant-cloud/notes.md` (profiles and capabilities).
- `features/clinic-self-signup/notes.md`.
- `features/clinic-account-removal/notes.md`.
- `deploy/README.md` (hosted operator guide, compose layout, Caddy, deploy workflow).
- `.claude/rules/frontend-web.md`, `.claude/rules/verification.md`, `.claude/rules/regression-safety.md`,
  `.claude/rules/shared-stack.md`, `.claude/rules/response-style.md` (how to talk to the owner: short bullets).
- `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs`.
- `api/ClinicManagement.API/Middleware/SubscriptionGateMiddleware.cs`.
- `api/ClinicManagement.API/Maintenance/ProvisionClinicCommand.cs`.
- `api/ClinicManagement.Application/Features/Auth/Commands/LoginCommand.cs`.
- `site/BRIEF.md` and `site/src/pages/index.html`.

## 10. First steps for the new session
1. `stack-lease.ps1 status`, `ListAgents`, then `git worktree add ../wt-demo origin/main -b feature/demo-cabinet`.
2. Run `/define-feature` with this file as the brief. Ask the owner the 4 questions of § 8 in one go.
3. Build in this order:
   1. profile and capabilities (+ tests);
   2. read-only gate (+ coverage test);
   3. demo session and `/demo` page;
   4. the band and the refusal toast.
   Run it on the isolated local stack with `Deployment__Profile=PublicDemo` and a hand-made cabinet.
4. Then the seed scenario, reusing the « Premiers pas » cabinet's names and images. Check it with
   `verify-schema` and `reconcile-money`.
5. Then the compose/Caddy/DNS/deploy pieces, **prepared locally and shown to the owner before anything
   touches the VPS**.
6. Then the website button (owner's OK, two pushes).
