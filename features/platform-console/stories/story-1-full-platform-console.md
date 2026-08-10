# Story 1: [Full] The vendor runs the practice portfolio from a private console

**Status:** APPROVED
**Story Status:** in-progress — **Part 1 implemented** (`0fafe42`); Parts 2–3 buildable next, Parts 4–7 blocked
**Progress:** [progress.md](./progress.md) — gate results, deviations and what is owed
**Layer:** Full — ⚠️ see *Notes* for why the BE/FE separation rule is deliberately overridden
**Depends On:** `features/clinic-subscription/` — **Parts 4–7 only** (Parts 1–3 have no dependency on it)
**Blocks:** None

## Objective

The vendor reaches a private console over an SSH tunnel, signs in with a password **and** a one-time code, sees
every cabinet's subscription state beside its real activity — filterable, sortable, searchable and paged — opens
one cabinet, records a payment that unlocks the practice within minutes, corrects a mis-keyed entry and suspends
for abuse. And cannot read a single patient record: not by policy but by construction, through one narrow read
surface whose returned shape is a closed declared set, held by a check that **fails the build**.

Delivered as **seven ordered parts**, each a vertical increment (DB → service → API → UI) and a natural commit
boundary. Parts 1–3 stand alone as a read-only console, which is already useful.

## Acceptance Criteria

_From spec:_

**US-1 — sign-in (Part 1)**
- [ ] AC-1.1 — console accounts are a separate population; no clinic account can sign in here and no console account into a clinic
- [ ] AC-1.2 — sign-in requires e-mail, password **and** a time-based one-time code
- [ ] AC-1.3 — the enrolment secret comes from the bootstrap command, never from a password-only response; that response carries no secret, no codes, no session
- [ ] AC-1.3a — enrolment is a separate action carrying password **and** a valid generated code; recovery codes shown once
- [ ] AC-1.3b — a recovery code is single-use and consumed whether or not the sign-in completes
- [ ] AC-1.4 — console and clinic sessions are not interchangeable in either direction, refused as **unauthenticated**
- [ ] AC-1.5 — lockout after repeated failures, **and** the console's sign-in is inside the anonymous-auth rate limits, per account and per address
- [ ] AC-1.6 — deactivation takes effect on the **next request**, not at session expiry
- [ ] AC-1.7 — console paths are absent from the public address and clinic paths absent from the console's — **pages included**
- [ ] AC-1.8 — the console can be switched off, and is then absent rather than present-and-refusing

**US-2 — the portfolio (Part 2)**
- [ ] AC-2.1 — the list shows every named figure per cabinet, including saves over 7/30 days and the cabinet's own monthly collected total
- [ ] AC-2.2 — « saves » counts only people at the cabinet: background work **and** the vendor's own console writes excluded
- [ ] AC-2.3 — filters: en essai · expire sous N jours · expiré · suspendu · dormant
- [ ] AC-2.4 — sortable by end date, activity and creation date, and paged
- [ ] AC-2.4a — every activity figure exists for every cabinet **before** a page is cut; figures come from scheduled counters, never per-request derivation
- [ ] AC-2.5 — free-text search matches name, city or the administrator's e-mail
- [ ] AC-2.6 — every figure is a count, a date or a total; no patient, appointment, document, note or per-patient amount anywhere
- [ ] AC-2.7 — a summary of six figures, with the **vendor's** revenue never a sum of the cabinets' own, and both labelled so they cannot be confused
- [ ] AC-2.8 — the screen says when the counters last ran

**US-3 — one cabinet (Part 3)**
- [ ] AC-3.1 — the same figures plus a six-month trend
- [ ] AC-3.2 — the full payment history including cancelled entries, struck through with their reason
- [ ] AC-3.3 — the administrator's name, e-mail and the staff count
- [ ] AC-3.4 — no clinical or per-patient information of any kind
- [ ] AC-3.5 — opening a detail is recorded; **listing cabinets is not**

**US-4 — record a payment (Part 4)**
- [ ] AC-4.1 — duration or explicit end date, plan, amount, method, reference, optional note
- [ ] AC-4.2 — the end date follows the existing rule; the console introduces no second arithmetic
- [ ] AC-4.3 — the console shows the new state and end date immediately
- [ ] AC-4.4 — the clinic's app reflects it without signing out, via the **companion's re-read**
- [ ] AC-4.4a — any direct notification is an optimisation only, addressed to the **target** cabinet explicitly
- [ ] AC-4.5 — refused with a named reason for a non-positive duration or an unknown cabinet
- [ ] AC-4.6 — a double-click produces **one** entry
- [ ] AC-4.7 — every grant records which console account made it and appears in that cabinet's own journal
- [ ] AC-4.8 — a complimentary period (no amount) can be recorded, and the entry says which it is

**US-5 — correct a mistake (Part 5)**
- [ ] AC-5.1 — any entry can be cancelled with a **mandatory written reason**
- [ ] AC-5.2 — never edited, never deleted: it stays listed, struck through, with reason, canceller and moment
- [ ] AC-5.3 — the end date recomputes and may move into the past, which the confirmation says before committing
- [ ] AC-5.4 — the confirmation names the cabinet and the amount

**US-6 — suspend (Part 6)**
- [ ] AC-6.1 — suspension requires a written reason, recorded with author and moment
- [ ] AC-6.2 — a suspended cabinet is read-only as an expired one is, and is told it is **suspended**
- [ ] AC-6.3 — visually distinct from expiry throughout; not a payment state
- [ ] AC-6.4 — unsuspending restores the entitlement; suspension never consumes paid days
- [ ] AC-6.5 — confirmed with the cabinet named and the consequence stated

**US-7 — privacy (Parts 2, 3, 7)**
- [ ] AC-7.1 — no console screen, endpoint or export exposes a patient, appointment, note, document, file, diagnosis or per-patient amount
- [ ] AC-7.2 — enforced structurally and **checked automatically** by a derived check over a closed declared shape
- [ ] AC-7.2a — the tenant filter is **not** the mechanism, and the console says so
- [ ] AC-7.3 — every detail read and every write is recorded — who, which cabinet, which action, when
- [ ] AC-7.4 — the promise is stated in plain French **including** what the vendor does see (the cabinet's own monthly collected total)

**US-8 — bootstrap and recovery (Parts 1, 7)**
- [ ] AC-8.1 — **every** console account is created by a command, never a web page; it prints the enrolment secret and a one-time password
- [ ] AC-8.2 — a lost second factor is recovered by a recovery code, or by the command re-issuing a secret and invalidating the old one
- [ ] AC-8.3 — the companion's four subscription commands keep working, so a broken console never blocks a paying clinic
- [ ] AC-8.4 — more than one console account may exist, each acting under its own identity
- [ ] AC-8.5 — deactivating is also a command; there is no console screen listing, creating or deactivating accounts
- [ ] AC-8.6 — a signed-in console account can change **its own** password in the console

**Edge cases**
- [ ] EC-1 · EC-2 · EC-3 (Part 1) — a leaked clinic password grants nothing; a leaked console password without the factor fails, **including on an account that has never signed in**; a lost factor is recoverable
- [ ] EC-4 (Part 1) — a console/clinic port collision **refuses startup**, naming both settings
- [ ] EC-5 · EC-6 (Part 4) — a double-click is one entry; two *different* simultaneous grants both succeed with **no conflict response**
- [ ] EC-7 (Part 5) — a cancellation that puts a working cabinet back into read-only says so before committing
- [ ] EC-8 · EC-9 (Part 2) — a cabinet with no activity appears with zeros and a « dormant » marker; a cluster of same-day trials is visible as such
- [ ] EC-10 (Part 2) — activity is polluted by neither machine work **nor the vendor**
- [ ] EC-11 (Part 2) — a large portfolio stays paged and bounded by the **number** of cabinets, not by any one cabinet's history
- [ ] EC-12 (Parts 2, 7) — « je n'ai pas pu lire » and « zéro cabinet » never look the same; an unscoped read is a **fault**
- [ ] EC-13 · EC-14 (Part 3) — a vanished cabinet renders a French state with a way back; a never-expiring cabinet says so in words
- [ ] EC-15 (Parts 2, 7) — counters that never ran report their staleness, never a portfolio of dormant cabinets

_Story-specific (added by the challenge pass):_

- [ ] The public API is **still reachable** with the console enabled — binding the console listener must not unbind the public one
- [ ] EC-4's collision check fires in `HostedMultiTenant`, where `Hosting:HttpPort` is unset
- [ ] A console write's audit actor is `console|…` — distinguishable from a clinic user and matched by the counter pass's exclusion **through a shared constant**
- [ ] `PlatformAccountStateMiddleware` refuses a deactivated account or stale `TokenVersion` on the next request
- [ ] `patients` is a real count, not counted from audit `Insert` rows (which post-date most patients)
- [ ] The console's « encaissé par le cabinet » equals that cabinet's own caisse figure — pinned by `MoneyReadConsistencyTests`
- [ ] `PlatformConsole` is declared on `AuthorizationPolicies`, registered by `ConfigurePolicies`, and pins its authentication scheme
- [ ] The access ledger is **readable** by console accounts, not write-only

## Entry Criteria

Before starting this story, ensure:

- [ ] `plan.md` is `Status: APPROVED` and `Challenged: Yes` (it is)
- [ ] `spec.md` is `Status: APPROVED` and `Challenged: Yes` (it is)
- [ ] The working tree is clean for the files this story touches — run `git diff HEAD --numstat` first; this branch carries 20+ dirty files from other work and a careless `git add` would swallow them
- [ ] `dotnet build` passes on `api/ClinicManagement.sln`
- [ ] The backend unit suite is green **before** any change, so a new failure is attributable: `BaseOutputPath=<temp> dotnet test` (build outside the repo — Smart App Control refuses freshly-built in-repo test assemblies on this machine)
- [ ] `docker compose up -d` brings up postgres + minio, and `dotnet run -- verify-schema` reports clean — its output is **kept**, as the before-half of the migration diff
- [ ] Node 20+ available for scaffolding `console/`

**Before Parts 4–7 only:**

- [ ] ⚠️ **`features/clinic-subscription/` has shipped.** Verified spec-only today: no plan file, and zero hits in `api/` for `ClinicSubscription`, `SubscriptionPeriod`, `GrantSubscriptionPeriodCommand`, `SuspendClinicCommand`
- [ ] The Part 4 **pre-flight name check** (step 4.0) has passed for all six rows of the plan's *Assumed dependency surface*

## Steps

> Grouped by the plan's seven parts. Each part is a commit boundary; finish its validation checklist before
> moving on. Full detail lives in [`../plan.md`](../plan.md) — these steps are the executable ordering.

### Part 1 — Reach the console and sign in *(AC-1.x, AC-8.1/8.2/8.5/8.6, EC-1–EC-4)*

1. **Add the 16th capability**
   - `Infrastructure/Deployment/DeploymentProfile.cs` — `ServesPlatformConsole`: `HostedMultiTenant` ✓, the other two ✗
   - Extend `DeploymentProfileTests` + `DeploymentProfileCoverageTests`
   - ⚠️ The capability decides *may this exist*; `Console:Port` decides *is it bound*. **Off means absent** — no listener, no routes, 404 — never present-and-refusing (AC-1.8)

2. **Create the console identity**
   - `PlatformAccount` + `PlatformRecoveryCode` (Domain), configurations, migration
   - Unique index on the **lowered** email through `EmailNormalization`; lockout mirrors `User`'s 15-minute shape
   - Recovery codes stored so they can be checked, never read back

3. **Add the second factor**
   - `Infrastructure/Security/PlatformSecretProtector.cs` — purpose-scoped `IDataProtector` for the TOTP secret at rest
   - `Infrastructure/Auth/TotpService.cs` — `Otp.NET` behind `ITotpService`, `VerificationWindow(previous: 1, future: 1)`

4. **Issue the console's own tokens, and declare its policy in the right place**
   - `PlatformAuthConfig` + `PlatformAuthService` — PBKDF2 via the existing hasher, HS256 JWT with **its own signing key, issuer and audience**
   - Register a second `JwtBearer` scheme (`PlatformConsole`)
   - ⚠️ Declare **`AuthorizationPolicies.PlatformConsole`** as a public constant on `AuthorizationPolicies` and register it in **`ConfigurePolicies`** — not in `Program.cs`. `ControllerAuthorizationCoverageTests` derives its vocabulary from that class and asserts *defined == applied* both ways, plus that every constant resolves from `ConfigurePolicies`
   - ⚠️ **Pin the scheme:** `RequireAuthenticatedUser().AddAuthenticationSchemes(PlatformConsoleScheme)`. Without it the policy authenticates against the **default** (clinic) scheme and rejects every console token
   - ⚠️ Clinic policies keep **no** explicit scheme — that asymmetry is what makes AC-1.4 true in both directions

5. **Add the two-way port gate**
   - `API/Startup/ConsolePortGate.cs` — a pure static predicate over `(localPort, consolePort, path)`, refusing **both** directions, matched with `StartsWithSegments` so `/api/platform-ish` cannot slip through
   - Wire it first in the pipeline, as `TrustPortGate` is

6. **Bind the listener — and the public port in the same call**
   - ⚠️ **Not one line.** In `HostedMultiTenant` there is no cert file, so `Program.cs` never calls `ConfigureKestrel`; only `ASPNETCORE_URLS: http://+:5000` binds the public port, and explicit Kestrel endpoints **override** it. A bare `ListenAnyIP(consolePort)` would unbind 5000 and take the whole product down while the console worked
   - Resolve the public port: `Hosting:Urls` → `ASPNETCORE_URLS` → `Hosting:HttpPort` → `5000`
   - One `ConfigureKestrel` call binding **both**; assert the public port is among the bound endpoints; log both
   - **Fail startup loud** on a collision (EC-4), derived from the ports **actually bound** — not from `Hosting:HttpPort`/`HttpsPort`/`WebPort`, none of which is set in hosted
   - Capability off or port `0` ⇒ touch none of this

7. **Fit the console into the request pipeline**
   - Make `AccountStateMiddleware`, `LocalAuthEnforcementMiddleware` and `TenantScopeMiddleware` **skip** console requests (a console principal has no `User` row)
   - `API/Startup/PlatformTenantScope.cs` — declares `UseSystemWide("platform console")`, and **throws** if a console read runs `Unset` (EC-12)
   - ⚠️ **Fill the hole the skip creates:** `API/Middleware/PlatformAccountStateMiddleware.cs` — after `UseAuthentication`, console requests only: load the `PlatformAccount` once, **401** on deactivated or stale `TokenVersion` (AC-1.6), cache the row on `HttpContext.Items` so the session context reuses one query

8. **Make the audit actor console-aware (here, not in Part 4)**
   - `AuditActor.Console(accountId)` + a `ConsolePrefix` constant beside `ProcessPrefix`
   - `AuditActorProvider` consults `IPlatformSessionContext` **before** `IClinicContext`
   - ⚠️ Without this the token's `sub` wins and a console write records a bare GUID — silently breaking AC-4.7 and the AC-2.2/EC-10 exclusion at once

9. **Bring the console's sign-in inside the auth rate limits**
   - Widen `RateLimiting.IsAnonymousAuthPath` **and** `AuthAttemptAccount` to `/api/platform/auth` together (AC-1.5)

10. **Build the auth endpoints**
    - `API/Controllers/Platform/PlatformAuthController.cs` — `login` / `totp/enrol` / `recovery` / `password`, with the exact status/body shapes of the spec's API section
    - ⚠️ The 403 on a not-yet-enrolled account carries **nothing else** — no secret, no codes, no session (AC-1.3, EC-2)
    - Add `login`, `totp/enrol`, `recovery` to `ExpectedAnonymous`; `password` stays out (session required)

11. **Add the bootstrap verb**
    - `API/Maintenance/PlatformAccountCommand.cs` — `platform-account create --email … --name …` / `--deactivate` / `--reset-totp`, sharing `PlatformAccountProvisioning`, printing the enrolment secret and a one-time password
    - Gate on a configured connection string (`MaintenanceDatabase`), **not** `HasLocalDbTooling` — amendment M3's reasoning applies unchanged

12. **Scaffold `console/`**
    - Next 15 + Tailwind v4 + only the shadcn primitives actually used, copied from `web/components/ui`
    - `login` (email · password · code · recovery link), `mot-de-passe`, an empty `cabinets` shell
    - HttpOnly-cookie session, canonical `{ error }` parsing, French throughout, **no clinic chrome** (FR-7)
    - Login must work fully at **320 px** — the likeliest phone use

13. **Wire the deployment**
    - `deploy/Caddyfile` — the second site on `127.0.0.1:9443`; the public site gains **no** `/api/platform` route
    - `deploy/docker-compose.hosted.yml` + `.env.hosted.example` — the `console` service, `Console__Port`, `Console__SigningKey`, `ports: ["127.0.0.1:9443:9443"]` on caddy
    - The runbook section in `deploy/README.md`

14. **Add the fifth CI job**
    - `.github/workflows/ci.yml` — `console`: `tsc --noEmit` + `check:responsive` + `next build`, exactly the `web` job's gate

15. **Tests**
    - `ConsolePortGateTests`, `PlatformTokenIsolationTests`, `PlatformRateLimitingTests`, `PlatformAuthTests`, `PlatformAccountStateTests`, `ControllerAuthorizationCoverageTests` (extended)

### Part 2 — The portfolio, and the counters behind it *(AC-2.x, AC-7.2, AC-7.2a, EC-8–EC-12, EC-15)*

16. **Create the two counter tables**
    - `ClinicActivityDay` (one cabinet, one clinic-local day) + `ClinicActivitySnapshot` (one cabinet, plus the point-in-time figures), configurations, indexes, migration
    - Register both as **named decisions** in `TenantScopeFilterTests` — system-wide reads only

17. **Write the pure counter derivation**
    - `Application/Common/Maintenance/PlatformCounterPass.cs` — audit rows + a clinic-local day window → writes / appointments / patients-created / active-days
    - ⚠️ **Two exclusions, both silent failures otherwise:** `job|…` actors and the console's own `console|…` actor, matched on `AuditActor`'s own prefix **constants**
    - ⚠️ **Name every non-audit figure's source:** `patients` = `COUNT` over `Patients` (**never** audit `Insert` rows — the ledger post-dates most patients and an established cabinet would read as nearly empty); `users` = `COUNT` over `Users`; `lastLoginAt` = `MAX(User.LastLoginAt)`; `clinicCollectedThisMonthDt` = **la caisse's own predicates** through `PlanBillingRules.BilledPlanIds`, never a hand-written `SUM`
    - Extend `MoneyReadConsistencyTests` — the console becomes the **fifth** read held to the one figure

18. **Add the daily pass**
    - `API/BackgroundJobs/ClinicActivityCounterJob.cs` — declares `UseSystemWide`, writes yesterday's day row for every cabinet, recomputes every snapshot including `ComputedAt`
    - **Not** connectivity-gated — its output is a database row
    - A cabinet with nothing to count gets a **row of zeros**, never no row (EC-8)

19. **Write the closed read shape and its derived check**
    - `Application/Features/Platform/PlatformReadShape.cs` — the declared, closed set of scalar field names
    - `PlatformReadShapeTests` — reflects over the console controllers' action return types, recurses into nested DTOs, **fails the build** on any leaf outside the set. This is the whole of AC-7.2; the tenant filter explicitly is not (AC-7.2a)

20. **Add the paged portfolio read**
    - `ListPlatformClinicsQuery` (`state` · `expiringWithin` · `dormant` · `q` · `sort` · `page` · `pageSize`) over `PagedResult`/`PageRequest`, JOINing the snapshot so filter + sort + page are **one bounded query** (AC-2.4a, EC-11)
    - Free text through `SearchTerm` + `SqlSearch` (`unaccent`, escaped wildcards), matching name, city **and the administrator's e-mail** (AC-2.5 — needs the `Users` join)
    - Order on a unique column last (`.ThenBy(c => c.Id)`) or `OFFSET` shows a cabinet twice
    - ⚠️ Until the companion ships, the subscription **state** column reads « — » from a single **clearly named placeholder resolver**, deleted (not extended) in Part 4

21. **Add the summary**
    - `GetPlatformSummaryQuery` — one bounded read; `vendorCollectedThisMonthDt` is the **vendor's** revenue, never a sum of the per-cabinet figure. Label both (AC-2.7)

22. **Put freshness on screen**
    - `countersAsOf` on the list response, stated beside the figures **on every width** (AC-2.8); a portfolio whose counters were never written says so rather than reading as all-dormant (EC-15)

23. **Build the portfolio UI**
    - `portfolio-summary` (2 / 3 / 6 columns, each figure a link to the list it filters)
    - `clinic-table` (≥ 1024 px) and `clinic-card-list` (below — 14 columns needs a card list even on a tablet)
    - Filters in a sheet on phone with the active filter as a removable chip; row actions in an explicit menu on **every** width, nothing hover-only

24. **Tests** — `PlatformCounterPassTests`, `PlatformReadShapeTests`, a paging/sorting test over the snapshot JOIN, `MoneyReadConsistencyTests` (extended)

25. **Add the three schema checks**
    - `SchemaVerificationService`: `platform-account-has-totp-or-unenrolled`, `clinic-activity-day-unique-per-clinic-day`, `clinic-activity-snapshot-covers-every-clinic`

### Part 3 — One cabinet's detail *(AC-3.x, AC-7.3, EC-13, EC-14)*

26. **Add the detail read**
    - `GetPlatformClinicDetailQuery` — the list row + admin name/e-mail + staff count + the six-month trend from `ClinicActivityDay` + the payment ledger (cancelled entries included, with reason, canceller and moment)

27. **Record the read**
    - Write a `PlatformAccessEntry` for the detail (AC-7.3)
    - ⚠️ **List reads are deliberately not recorded** (AC-3.5): one list read touches every cabinet, and a row per cabinet per page load would drown every reading anyone wants

28. **Handle the two absent cases in words**
    - A cabinet with **no end date** says so; never a computed date (EC-14)
    - An unknown cabinet renders « ce cabinet n'existe plus » with a way back, not an error page (EC-13)

29. **Make the access ledger readable**
    - `GetPlatformAccessLogQuery` + `GET /api/platform/access-log` + `console/app/journal/page.tsx` — paged, newest first, filterable by account and by cabinet, ordered on a unique column last
    - Its DTO's fields join `PlatformReadShape`
    - ⚠️ Stays a **console** read — showing a clinic who looked at its cabinet is named out of scope. Read-only by construction, like `AuditController`

30. **Build the detail UI**
    - Single column on phone/tablet, two on desktop; the trend scrolls in its own container and **its values are given as text too**; the ledger is a card list below its hinge, a cancelled entry marked in **words** as well as struck through

31. **Tests** — `PlatformAccessLedgerTests` (detail recorded, list not, and the read returns what the write recorded), `PlatformReadShapeTests` over the detail and access-log DTOs

### Part 4 — Record a payment and unlock the cabinet *(AC-4.x, EC-5, EC-6, EC-14, FR-4, FR-6)*

> ⚠️ **BLOCKED until `features/clinic-subscription/` ships.**

32. **Pre-flight — do this before writing anything**
    - Look up, by name in the source, each of the six rows of the plan's *Assumed dependency surface*
    - A name that merely differs is adapted; **a name that is missing stops Part 4**
    - Do **not** write a console-side end-date computation, state fold or period entity as a stand-in — that is the second arithmetic FR-4 forbids, and it would have to be un-written rather than adapted
    - Delete the Part 2 placeholder state resolver here rather than layering over it

33. **Add the grant command** — `RecordSubscriptionPeriodCommand`: validate the cabinet exists and the duration is positive (AC-4.5), then delegate to the companion's grant handler — **no second arithmetic** (AC-4.2). Supports « offert » with no amount (AC-4.8) and a never-expiring cabinet (EC-14)

34. **Make a repeated submission one entry** — idempotency on `idempotencyKey` (AC-4.6, EC-5). ⚠️ Deliberately **no conflict response**: two *different* grants landing together are two entries in an append-only ledger and the surplus one is cancelled (EC-6)

35. **Name `/api/platform/*` on the companion's write-refusal allow-list** — and assert it in a test. ⚠️ Without it the gate finds no entitlement for a caller with no cabinet and refuses under the *missing-entitlement* code, on the one endpoint whose purpose is to end the refusal, against precisely the cabinets that have lapsed

36. **Record and attribute** — write the `PlatformAccessEntry`, and the audit row carries `console|{accountId}` (the mechanism landed in step 8), so the grant appears in that cabinet's journal distinguishably from a clinic user (AC-4.7) and is excluded from its activity figures

37. **Leave the live effect to the companion's re-read** — AC-4.4 is that re-read; this feature adds no second mechanism. Optionally (AC-4.4a) notify the **target** cabinet via `IRealtimeNotifier` with the companion's **existing** declared key. ⚠️ Both pipeline defaults are wrong here: the audience is the *acting user's* clinic (a console account has none → reaches nobody, invisibly) and the key is derived from the namespace (a new key fails `RealtimeResourceResolverTests`). Exclude the console commands from the automatic broadcast and address the clinic group by hand

38. **Build the payment sheet** — full-screen `dvh` sheet on phone with the primary action pinned and visible with the keyboard open, dismissible by a visible control **and** Escape, confirming before discarding typed input; dialog on desktop; disabled in flight

39. **Tests** — `PlatformIdempotencyTests`, `SubscriptionWriteGateTests` (extended)

### Part 5 — Correct a mistake *(AC-5.x, EC-7)*

40. **Add the cancel command** — `CancelSubscriptionPeriodFromConsoleCommand`, mandatory written reason (AC-5.1), delegating to the companion's handler. The entry is never edited and never deleted (AC-5.2)
41. **State the consequence before committing** — the confirmation names the cabinet **and** the amount (AC-5.4) and says the cabinet will become read-only and from which date (AC-5.3, EC-7), computed **from the companion's own fold**, never a console-side estimate
42. **Record it** — `PlatformAccessEntry` + the cabinet's journal row, as in Part 4
43. **Build the confirmation** — bottom sheet on phone, dialog on desktop; the cancelled entry marked in words as well as struck through

### Part 6 — Suspend for abuse *(AC-6.x)*

44. **Add the suspension commands** — `SuspendClinicFromConsoleCommand` / `UnsuspendClinicFromConsoleCommand`, mandatory reason on suspension (AC-6.1), delegating to the companion's handlers; unsuspending restores whatever entitlement the cabinet had (AC-6.4)
45. **Keep suspension distinct from expiry throughout** — text and shape, never colour alone (AC-6.3), and never presented as a payment state
46. **Confirm with consequence** — names the cabinet and warns the practice will be unable to record new work (AC-6.5)
47. **Record it** — `PlatformAccessEntry` + journal rows

### Part 7 — Verification, operator runbook and the promise *(AC-7.4, AC-8.3, FR-8, EC-12, EC-15)*

48. **Run the schema gate** — `verify-schema` before/after the migration batch and **diff**, as that command's workflow prescribes
49. **Write the operator runbook** (`deploy/README.md`) — opening the tunnel, bootstrapping the first account, enrolling the factor, storing the recovery codes, deactivating an account, and the four companion commands that keep working when the console does not (AC-8.3)
50. **Write what the vendor sees, in plain French** — counts, dates, subscription state **and the cabinet's own monthly collected total**. ⚠️ « nous ne voyons pas vos données » would be broader than the truth (AC-7.4); the sentence must include that figure
51. **Confirm the two « cannot look the same » requirements by trying them** — an unreachable database reports « je n'ai pas pu lire », never an empty portfolio (EC-12); counters that never ran report staleness, never a portfolio of dormant cabinets (EC-15)
52. **Update the four `CLAUDE.md` maps**

## Files to Create/Modify

### Files to Create — Domain

| File | Purpose |
|------|---------|
| `Domain/Entities/PlatformAccount.cs` | The console identity: email, password hash, `IsActive`, lockout, encrypted TOTP secret, `TotpEnrolledAt`, `TokenVersion`. Aggregate root, **no `ClinicId`** |
| `Domain/Entities/PlatformRecoveryCode.cs` | One hashed single-use recovery code (child of `PlatformAccount`) |
| `Domain/Entities/PlatformAccessEntry.cs` | The console's own append-only ledger (FR-5) — deliberately not `AuditEntry` |
| `Domain/Entities/ClinicActivityDay.cs` | One cabinet, one clinic-local day: writes, appointments, patients created, collected DT |
| `Domain/Entities/ClinicActivitySnapshot.cs` | One cabinet's rolled-up figures + the point-in-time ones + `ComputedAt` |
| `Domain/Enums/PlatformAccessAction.cs` | `ViewedClinic`, `GrantedPeriod`, `CancelledPeriod`, `Suspended`, `Unsuspended` |
| `Domain/Repositories/IPlatformAccountRepository.cs` | By normalised email, by id |
| `Domain/Repositories/IPlatformAccessEntryRepository.cs` | `AddAsync` + one paged read. No update, no delete |
| `Domain/Repositories/IClinicActivityRepository.cs` | The pass's writes + `GetPortfolioPageAsync` / `GetTrendAsync` |

### Files to Create — Application

| File | Purpose |
|------|---------|
| `Features/Platform/PlatformReadShape.cs` | The closed, declared set of scalar fields a console read may return (AC-7.2) |
| `Features/Platform/Dtos/PlatformClinicRowDto.cs` | The list row exactly as the spec states it |
| `Features/Platform/Dtos/PlatformSummaryDto.cs` | The six portfolio figures + `vendorCollectedThisMonthDt` |
| `Features/Platform/Dtos/PlatformClinicDetailDto.cs` | Row + admin contact + 6-month trend + payment ledger |
| `Features/Platform/Dtos/PlatformAccessEntryDto.cs` | Account, action, target cabinet, moment |
| `Features/Platform/Queries/ListPlatformClinicsQuery.cs` | The paged portfolio read |
| `Features/Platform/Queries/GetPlatformSummaryQuery.cs` | One bounded read (EC-11) |
| `Features/Platform/Queries/GetPlatformClinicDetailQuery.cs` | Detail; **writes a `PlatformAccessEntry`** |
| `Features/Platform/Queries/GetPlatformAccessLogQuery.cs` | The ledger's reader (FR-5, AC-7.3) |
| `Features/Platform/Commands/RecordSubscriptionPeriodCommand.cs` | Wraps the companion's grant + idempotency + access entry |
| `Features/Platform/Commands/CancelSubscriptionPeriodFromConsoleCommand.cs` | Wraps the companion's cancel |
| `Features/Platform/Commands/SuspendClinicFromConsoleCommand.cs` · `UnsuspendClinicFromConsoleCommand.cs` | Wrap the companion's suspension handlers |
| `Features/Platform/Auth/*` | `PlatformLoginCommand`, `EnrolPlatformTotpCommand`, `RedeemPlatformRecoveryCodeCommand`, `ChangePlatformPasswordCommand` |
| `Features/Platform/PlatformAccountProvisioning.cs` | Shared by the create/deactivate verbs, as `LocalClinicProvisioning` is by three callers |
| `Common/Interfaces/IPlatformSessionContext.cs` | « which console account is acting » |
| `Common/Interfaces/ITotpService.cs` | Generate secret / verify code |
| `Common/Maintenance/PlatformCounterPass.cs` | The pure counter derivation — unit-testable with no database |

### Files to Create — Infrastructure

| File | Purpose |
|------|---------|
| `Auth/TotpService.cs` | `Otp.NET` behind `ITotpService` |
| `Auth/PlatformAuthService.cs` | PBKDF2 + HS256 JWT with its own key, issuer and audience |
| `Auth/PlatformAuthConfig.cs` | `Console:SigningKey` / issuer / audience / lifetime. **Never** the clinic key |
| `Security/PlatformSecretProtector.cs` | The TOTP secret at rest (FR-1) |
| `Repositories/PlatformAccountRepository.cs` · `PlatformAccessEntryRepository.cs` · `ClinicActivityRepository.cs` | EF impls |
| `Persistence/Configurations/Platform*.cs` · `ClinicActivity*.cs` | Mapping + indexes (unique lowered email; `(ClinicId, Day)` unique; snapshot PK = `ClinicId`) |
| `Migrations/*_AddPlatformConsole.cs` | Five tables. Hand-written if `dotnet ef` cannot load a fresh assembly |

### Files to Create — API

| File | Purpose |
|------|---------|
| `Controllers/Platform/PlatformAuthController.cs` | `login` / `totp/enrol` / `recovery` / `password` |
| `Controllers/Platform/PlatformClinicsController.cs` | list · summary · detail · subscription · cancel · suspend · unsuspend |
| `Controllers/Platform/PlatformAccessLogController.cs` | `GET /api/platform/access-log`, read-only by construction |
| `Startup/ConsolePortGate.cs` | The two-way refusal |
| `Startup/PlatformTenantScope.cs` | `UseSystemWide("platform console")`, throws on `Unset` (EC-12) |
| `Middleware/PlatformAccountStateMiddleware.cs` | Deactivation / stale `TokenVersion` → **401 next request** (AC-1.6) |
| `BackgroundJobs/ClinicActivityCounterJob.cs` | The daily pass |
| `Maintenance/PlatformAccountCommand.cs` | `platform-account create` / `--deactivate` / `--reset-totp` |

### Files to Create — `console/` (new Next 15 app)

| File | Purpose |
|------|---------|
| `package.json`, `tsconfig.json`, `next.config.ts`, `postcss.config.mjs`, `Dockerfile`, `scripts/check-responsive.mjs` | The app + the same responsive gate `web/` runs |
| `app/layout.tsx`, `globals.css` | French, Tailwind v4, **no clinic chrome** |
| `app/login/page.tsx` | Email + password + code; recovery path; fully usable at 320 px |
| `app/cabinets/page.tsx` | Summary + portfolio list |
| `app/cabinets/[clinicId]/page.tsx` | Detail: state → activity → trend → ledger → actions |
| `app/journal/page.tsx` | The console's own access log |
| `app/mot-de-passe/page.tsx` | AC-8.6 |
| `components/*` | `clinic-table`, `clinic-card-list`, `portfolio-summary`, `activity-trend`, `record-payment-sheet`, `cancel-period-dialog`, `suspend-dialog`, `counters-freshness`, `state-badge` |
| `components/ui/*` | Only the shadcn primitives used, copied from `web/components/ui` |
| `lib/api/client.ts`, `lib/session.ts`, `lib/format.ts` | Console API client, HttpOnly-cookie session, TND/date formatting |

### Files to Create — Tests

| File | Purpose |
|------|---------|
| `UnitTests/Features/Platform/PlatformReadShapeTests.cs` | **The derived check of AC-7.2** |
| `UnitTests/Api/ConsolePortGateTests.cs` | Two-way boundary cases + the `/api/platform-ish` prefix trap |
| `UnitTests/Api/PlatformTokenIsolationTests.cs` | Both directions **401, not 403** (AC-1.4) |
| `UnitTests/Api/PlatformRateLimitingTests.cs` | Recognised by `IsAnonymousAuthPath` **and** the account capture (AC-1.5) |
| `UnitTests/Api/PlatformAccountStateTests.cs` | Deactivation / `TokenVersion` refuse the next request (AC-1.6) + an ordering guard |
| `UnitTests/Features/Platform/PlatformCounterPassTests.cs` | Background and console actors excluded; zero-activity yields a row of zeros |
| `UnitTests/Features/Platform/PlatformAuthTests.cs` | Password alone yields nothing; a recovery code is consumed even on a failed sign-in |
| `UnitTests/Features/Platform/PlatformAccessLedgerTests.cs` | Detail + writes recorded, list not, and the read returns what was written |
| `UnitTests/Features/Platform/PlatformIdempotencyTests.cs` | Same key twice → one entry (AC-4.6, EC-5) |

### Files to Modify

| File | Changes |
|------|---------|
| `Infrastructure/Deployment/DeploymentProfile.cs` | 16th capability `ServesPlatformConsole` (+ both profile tests) |
| `API/Program.cs` | Bind console **and** public port in one `ConfigureKestrel`; register the gate first; the second JWT scheme; the counter job; the `platform-account` verb; fail loud on a collision derived from the bound ports |
| `Application/Common/Authorization/AuthorizationPolicies.cs` | `PlatformConsole` constant, registered by `ConfigurePolicies`, scheme pinned |
| `Application/Common/Interfaces/IAuditActorProvider.cs` | `AuditActor.Console(accountId)` + `ConsolePrefix` |
| `Application/Common/Services/AuditActorProvider.cs` | Consult `IPlatformSessionContext` before `IClinicContext` |
| `API/Startup/RateLimiting.cs` · `AuthAttemptAccount.cs` | Recognise `/api/platform/auth` — both, together |
| `API/Middleware/…` | `AccountStateMiddleware`, `LocalAuthEnforcementMiddleware`, `TenantScopeMiddleware` skip console requests |
| `Infrastructure/Persistence/ApplicationDbContext.cs` | Register the five entities; the two counter tables as named decisions in `TenantScopeFilterTests` |
| `Infrastructure/Persistence/AuditSaveChangesInterceptor.cs` | Exclude the new aggregate roots — a console sign-in is not a clinic's history |
| `Infrastructure/Extensions.cs` | Register the repositories, `ITotpService`, `PlatformAuthService`, `PlatformSecretProtector`, `IPlatformSessionContext` |
| `Application/Common/Maintenance/SchemaVerificationService.cs` | Three named checks |
| `deploy/Caddyfile` | The second site on `127.0.0.1:9443`; the public site gains **no** `/api/platform` route |
| `deploy/docker-compose.hosted.yml` · `.env.hosted.example` | The `console` service, `Console__*`, loopback-only `9443` |
| `deploy/README.md` | The operator runbook |
| `.github/workflows/ci.yml` | The fifth `console` job |
| `CLAUDE.md` + the three `api/**/CLAUDE.md` | Keep the maps accurate |

## Verification Steps

### Per part (the plan's own checklists — the commit gate for each)

**Part 1**
- [ ] Tunnelled `https://127.0.0.1:9443/login` signs in with password + code; `https://{DOMAIN}/api/platform/summary` and `https://{DOMAIN}/cabinets` both **404**
- [ ] A clinic token on `/api/platform/*` → **401**; a console token on `/api/patients` → **401**
- [ ] Password-only sign-in on an unenrolled account → 403 with no secret and no session
- [ ] A recovery code signs in once and cannot be reused, including when the sign-in it accompanied failed
- [ ] Deactivating an account mid-session refuses its **very next** request (401), not at token expiry
- [ ] Setting the console port to the web port refuses startup, naming both keys
- [ ] Setting `Console:Port` equal to the port in `ASPNETCORE_URLS` refuses startup — EC-4 fires where `Hosting:HttpPort` is unset
- [ ] **With the console enabled, `https://{DOMAIN}/api/auth/mode` still answers**, and the startup log names both bound endpoints
- [ ] `dotnet test` green; `console` CI job green; login usable at 320 px

**Part 2**
- [ ] The list filters and sorts on activity across the **portfolio**, not the current page
- [ ] A cabinet that has never worked appears with zeros and a « dormant » marker
- [ ] Adding a patient name to any console DTO **fails** `PlatformReadShapeTests` — verify by trying it
- [ ] Console writes and background jobs move **no** activity figure
- [ ] A cabinet whose patients predate the audit ledger shows its **real** patient count
- [ ] The console's « encaissé par le cabinet » equals that cabinet's own caisse figure for the month
- [ ] Freshness is on screen at 320 / 390 / 820 / 1180 / 1440 px
- [ ] `verify-schema` passes its three new checks

**Part 3**
- [ ] The detail shows the trend, the ledger and the admin's contact, and **no** clinical or per-patient value
- [ ] Opening a detail writes exactly one ledger row; loading the list writes none
- [ ] « Journal » lists that row, filterable by account and cabinet, with no way to edit or delete one
- [ ] A never-expiring cabinet says so rather than showing a date
- [ ] Greyscale: « Expiré » and « Suspendu » remain distinguishable

**Part 4**
- [ ] Recording a payment on an expired cabinet succeeds; the console shows the new state and end date at once
- [ ] The clinic's banner clears without anyone signing out, within the companion's stated delay
- [ ] Double-clicking « Enregistrer le paiement » produces **one** entry
- [ ] A non-positive duration and an unknown cabinet are refused with a message naming which
- [ ] The grant appears in the cabinet's journal attributed to the console account, and moves no activity figure

**Part 5**
- [ ] Cancelling a three-week-old grant recomputes the end date, possibly into the past, and the cabinet's banner appears without a reload
- [ ] The reason is mandatory; a blank one is refused in French
- [ ] The confirmation names cabinet and amount, so two open tabs cannot cancel the wrong one

**Part 6**
- [ ] A suspended cabinet is read-only and its own « Abonnement » screen says *suspended*, never *expired*
- [ ] Unsuspending restores the previous end date exactly; no paid day is lost
- [ ] « Expiré » and « Suspendu » are distinguishable in greyscale, in the list and the detail

**Part 7**
- [ ] `verify-schema` clean; the before/after diff shows only the intended objects
- [ ] Killing the database renders « je n'ai pas pu lire », **not** « aucun cabinet »
- [ ] A deployment with no counter pass yet reports its staleness explicitly
- [ ] The runbook is followable by someone who has not read the plan

**Verification commands:**

```bash
# Backend unit suite — build OUTSIDE the repo (Smart App Control refuses freshly-built in-repo test DLLs)
cd api && BaseOutputPath=/c/Users/OUMAYM~1/AppData/Local/Temp/claude/.testrun/ dotnet test ClinicManagement.sln

# The only gate a migration has anywhere in this product — run BEFORE and AFTER, and diff
cd api/ClinicManagement.API && dotnet run -- verify-schema

# Bootstrap the first console account (prints the enrolment secret + a one-time password)
cd api/ClinicManagement.API && dotnet run -- platform-account create --email "…" --name "…"

# The console's whole gate (there is no test runner in console/, as there is none in web/)
cd console && npx tsc --noEmit && npm run check:responsive && npm run build

# The clinic frontend must stay untouched
cd web && npx tsc --noEmit && npm run check:responsive && npm run build

# Confirm the public API survived the second listener
curl -sS https://{DOMAIN}/api/auth/mode
```

## Exit Criteria

This story is complete when:

- [ ] All seven parts' validation checklists above pass
- [ ] Every acceptance criterion in this file is ticked, or is explicitly recorded as out of scope with a reason
- [ ] `PlatformReadShapeTests` fails when a forbidden field is deliberately added — **verified by trying it**, not assumed
- [ ] `dotnet test` green, with the derived guards passing: `PlatformReadShapeTests`, `ConsolePortGateTests`, `PlatformTokenIsolationTests`, `PlatformAccountStateTests`, `TenantScopeFilterTests`, `SystemWideCallerCoverageTests`, `ControllerAuthorizationCoverageTests`, `DeploymentProfileTests`, `DeploymentProfileCoverageTests`, `RealtimeResourceResolverTests`, `MoneyReadConsistencyTests`
- [ ] `verify-schema` clean before and after, and the diff shows only the intended objects
- [ ] The `console` CI job is green, and the `api` / `web` / `desktop` / `android` jobs are unaffected
- [ ] The eye pass is done at 320 / 390 / 820 / 1180 / 1440 px on login, list, detail, journal, the payment sheet and both confirmations
- [ ] `SelfHostedLan` and `CloudBrowser` behave byte for byte as before — the capability is ✗ on both
- [ ] The four `CLAUDE.md` maps are updated
- [ ] Code reviewed and approved

## Notes

### Why this is one story, and `Layer: Full`

The user asked for **one** user story; `plan.md` honours that and records the oversize as **R-1** rather than
re-litigating it, and `/break-plan` materialises that decision as it stands. Two consequences, both deliberate:

- **The BE/FE separation rule is overridden.** This story spans Domain → Application → Infrastructure → API →
  Hangfire → deploy → a whole new Next app. Steps are grouped by **part** (each a vertical increment) rather
  than by layer.
- **The sizing guidelines are knowingly exceeded** (52 steps, ~60 files). The mitigation is the part boundaries:
  `/implement-story` lands and commits **per part**, and a part boundary is the resumption point if a session
  runs out.

### The blocked half — read this before starting Part 4

`features/clinic-subscription/` is a **spec with no implementation**, verified at challenge time. Parts 4–7
therefore cannot start, and this story will sit at `implemented`-for-Parts-1-3 until that feature ships. That is
a known property of materialising the plan as one story rather than two slices; the user chose it explicitly.

Parts 1–3 are genuinely independent of it and deliver a **read-only console**, which is already useful: the
private address, the second identity population with its second factor, the portfolio with real activity
counters, one cabinet's detail, and the access log. The only concession is the subscription **state** column,
which reads « — » from one clearly named placeholder resolver that Part 4 **deletes**.

### Traps this story is built around

Each of these was verified against the source during the challenge pass; none is hypothetical.

1. **Binding the console listener can take the whole product offline.** Hosted has no cert file, so `Program.cs`
   never calls `ConfigureKestrel` — `ASPNETCORE_URLS` is the only thing binding 5000, and explicit Kestrel
   endpoints override it wholesale. Bind both in one call (step 6).
2. **`console|…` attribution is not free.** The token's `sub` wins in `AuditActorProvider`, `RunAs` is a no-op
   once resolved, and `Process` prefixes `job|`. Without step 8, AC-4.7 is false *and* the counter pass's
   exclusion silently matches nothing — so granting a dormant cabinet makes it read as active next morning, on
   exactly the cabinet the « dormant » filter surfaced.
3. **Skipping the state middlewares removes revocation.** Console requests skip the product's only two live-state
   readers; `PlatformAccountStateMiddleware` (step 7) is what keeps AC-1.6 true.
4. **`patients` cannot come from audit rows** — the ledger post-dates most patients, and the error points the
   wrong way (« this practice is barely used » is the churn signal the list exists to give).
5. **The collected total is a fifth money read.** Reuse la caisse's predicates through `PlanBillingRules`; the
   vendor quoting a cabinet a turnover its own caisse contradicts is the worst place for a drift.
6. **A policy declared in `Program.cs` fails two derived guards**, and one with no pinned scheme rejects every
   console token.
7. **The migration may need hand-writing** (migration + `.Designer.cs` + snapshot) — `dotnet ef` cannot load a
   freshly-built assembly on this machine, as three prior migrations already worked around. `verify-schema` is
   the check that it was done right.
8. **A port bind is not a scoped surface.** Every mapped route answers on every bound port; `ConsolePortGate` is
   what does the work, and unlike `TrustPortGate` it must refuse **both** directions.
