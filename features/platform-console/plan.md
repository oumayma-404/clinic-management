# Implementation Plan: Console éditeur (vendor back-office — usage et abonnements)

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-08-10
**Approved:** 2026-08-10
**Spec:** [features/platform-console/spec.md](./spec.md)
**Depends on:** [features/clinic-subscription/spec.md](../clinic-subscription/spec.md) — ⚠️ **NOT YET IMPLEMENTED** (spec only; verified at challenge time). Parts 1–3 are buildable now; **Parts 4–7 are blocked** — see *Assumed dependency surface*

---

## Overview

The console is a **fourth surface** on an existing product: a second identity population, a second listener, a
second web application, and exactly one narrow cross-cabinet read. Nothing about it reuses the clinic app's
session, its front door or its tenant scope — and each of those non-reuses is what makes US-7's promise
structural rather than aspirational.

Four decisions were taken during planning and everything below follows from them:

1. **The console UI is its own Next 15 app** (`console/`), built and shipped as its own container. The spec forbids
   console screens living in the clinic bundle (`FR-2`), because the hosted front door proxies everything outside
   `/api` to that bundle and a proxy rule would then be the only thing hiding them.
2. **One private address serves both halves.** `deploy/Caddyfile` gains a second site bound to
   `127.0.0.1:9443` (published loopback-only, reached over an SSH tunnel): `/api/platform/*` → the API, everything
   else → the console container.
   ⚠️ **The API must bind a *second Kestrel listener* for this, and that is the synthesis the two choices force.**
   Behind Caddy every request reaches `api:5000`, so `HttpContext.Connection.LocalPort` cannot distinguish a
   console request from a public one — a header set by the proxy could, and would make the spec's refusal a proxy
   rule, exactly what FR-2 rejects. So the API binds `Console:Port` (5443) in addition to 5000, the **private**
   Caddy site proxies `/api/platform/*` to `api:5443`, the **public** site has no route to 5443 at all, and
   `ConsolePortGate` refuses on the real local port — in code, unforgeable, and testable as a pure predicate the
   way `TrustPortGate` already is.
3. **The second factor is `Otp.NET`** (RFC 6238), used from Infrastructure. Domain keeps its zero references.
4. **Activity is stored twice, on purpose.** `ClinicActivityDay` (one row per cabinet per day) is the durable
   history the six-month trend reads and the thing that survives any later retention policy on the audit ledger;
   `ClinicActivitySnapshot` (one row per cabinet) is what the list JOINs, so filtering and sorting **on activity**
   happen before the page is cut (AC-2.4a) in one bounded query.

The rest is the product's existing grammar applied to a new area: MediatR features under
`Application/Features/Platform/`, a controller family under `Controllers/Platform/`, `PagedResult`/`PageRequest`
from `Domain/Common/Paging.cs`, `ClinicClock` for every day boundary, `SearchTerm` + `SqlSearch` for the free-text
match, `IAuditActorProvider` for attribution, and — the load-bearing one — **derived checks rather than
hand-kept lists** for the three invariants that would otherwise rot silently (the closed read shape, the
system-wide declaration, and the two-way path refusal).

### The single user story

The user asked for **one** user story. It is honoured: `US-1` below is the whole feature. It is structured into
**seven ordered, dependency-respecting parts**, each a vertical increment (DB → service → API → UI) rather than a
technical layer, so `/implement-story` can land and commit part by part, and a part boundary is the natural split
point if the session runs out. The oversize is recorded as **R-1**.

### Assumed dependency surface (`features/clinic-subscription/`)

⚠️ **Verified at challenge time: that feature has NOT shipped.** `features/clinic-subscription/` contains
`spec.md` and nothing else — no plan, no stories — and `api/` returns **zero** hits for `ClinicSubscription`,
`SubscriptionPeriod`, `GrantSubscriptionPeriodCommand` and `SuspendClinicCommand`. So the table below is not a
list of names that might have been spelled differently; it is a list of things that **do not exist yet**. Two
consequences, both binding on the sequencing:

- **Parts 1–3 are buildable today.** They touch only `Clinic`, `User`, `AuditEntry` and the five new tables of
  this feature, and none of them reads or writes an entitlement. The one thing they must do is render the
  subscription **state** column — until the companion ships, that column reads « — » from a single, clearly
  named placeholder resolver, and the placeholder is deleted (not extended) when the real fold arrives.
  This promotes R-1's contingency (« Parts 1–3 alone deliver a read-only console, which is already useful »)
  from a fallback into the plan's actual order of work.
- **Parts 4–7 are BLOCKED and must not be started.** Part 4 opens with a **pre-flight check** (its step 0
  below): the six rows of this table are looked up in the source, by name, before a line of console write code
  is written. If any is missing, Part 4 stops there — it does **not** improvise a local equivalent, because a
  console-side arithmetic or a console-side state fold is precisely the FR-4 violation this feature exists
  without.

Every one of these is *its* deliverable; this feature adds no commercial rule and no second arithmetic (FR-4).
If a name merely differs at implementation time, adapt the call site — never re-implement the rule (**R-2**).

| Assumed | Used here for |
|---|---|
| `ClinicSubscription` aggregate (plan, derived end date, suspension flag) + `SubscriptionPeriod` ledger entries, both aggregate roots | list/detail state, the payment history |
| One authority computing `Essai` / `Actif` / `Expiré` / `Suspendu` + `EndsOn` (inclusive) + `DaysRemaining` (0 on the last working day) | every state and date the console shows |
| `GrantSubscriptionPeriodCommand`, `CancelSubscriptionPeriodCommand`, `SuspendClinicCommand` / `UnsuspendClinicCommand` (the handlers behind its FR-6 verbs) | the console's three writes call these, unchanged |
| Its write-refusal gate with an **explicit** allow-list (its FR-3) | `/api/platform/*` must be **named on that allow-list**, or the endpoint that unlocks a practice is refused by the lapse it exists to clear |
| Its subscription re-read (its FR-15) | how a clinic learns of a console write (AC-4.4). The console adds no second mechanism |
| A declared realtime resource key for subscription (e.g. `subscription`) | the optional targeted notification of AC-4.4a reuses it; a **new** key would fail `RealtimeResourceResolverTests` |

---

## Files to Modify/Create

### Files to Create — Domain

| File | Purpose |
|------|---------|
| `api/ClinicManagement.Domain/Entities/PlatformAccount.cs` | The console identity: email, password hash, `IsActive`, lockout (mirrors `User`'s 15-min shape), encrypted TOTP secret, `TotpEnrolledAt`, `TokenVersion`. Aggregate root, **no `ClinicId`** |
| `api/ClinicManagement.Domain/Entities/PlatformRecoveryCode.cs` | One hashed single-use recovery code (child of `PlatformAccount`). Stored so it can be checked, never read back (FR-1) |
| `api/ClinicManagement.Domain/Entities/PlatformAccessEntry.cs` | The console's **own** append-only ledger (FR-5): account, action, target clinic, moment. Deliberately not `AuditEntry` — a read performs no save and has no place in a journal whose vocabulary is Insert/Update/Delete |
| `api/ClinicManagement.Domain/Entities/ClinicActivityDay.cs` | One cabinet, one clinic-local day: writes, appointments booked, patients created, collected DT |
| `api/ClinicManagement.Domain/Entities/ClinicActivitySnapshot.cs` | One cabinet's rolled-up figures + `ComputedAt` — what the list sorts, filters and pages on. Carries the **point-in-time** figures too (`patients`, `users`, `lastLoginAt`), which are not per-day quantities and so have no place on `ClinicActivityDay` |
| `api/ClinicManagement.Domain/Enums/PlatformAccessAction.cs` | `ViewedClinic`, `GrantedPeriod`, `CancelledPeriod`, `Suspended`, `Unsuspended` |
| `api/ClinicManagement.Domain/Repositories/IPlatformAccountRepository.cs` | By email (normalised), by id |
| `api/ClinicManagement.Domain/Repositories/IPlatformAccessEntryRepository.cs` | Append-only: `AddAsync` **plus one paged read** (`GetPageAsync`, filterable by account and by cabinet). **No update, no delete** — append-only is about what cannot be *changed*, not about being unreadable |
| `api/ClinicManagement.Domain/Repositories/IClinicActivityRepository.cs` | The counter pass's writes + the console's two reads (`GetPortfolioPageAsync`, `GetTrendAsync`) |

### Files to Create — Application

| File | Purpose |
|------|---------|
| `Application/Features/Platform/PlatformReadShape.cs` | **The declared, closed set of scalar fields** a console read may return (AC-7.2 / FR-3). The one place the guarantee is written down |
| `Application/Features/Platform/Dtos/PlatformClinicRowDto.cs` | The list row exactly as the spec's API section states it |
| `Application/Features/Platform/Dtos/PlatformSummaryDto.cs` | The six portfolio figures + `vendorCollectedThisMonthDt` |
| `Application/Features/Platform/Dtos/PlatformClinicDetailDto.cs` | Row + admin contact + 6-month trend + the payment ledger |
| `Application/Features/Platform/Queries/ListPlatformClinicsQuery.cs` | Paged portfolio read: filters, sort, search, `countersAsOf` |
| `Application/Features/Platform/Queries/GetPlatformSummaryQuery.cs` | One bounded read (EC-11) |
| `Application/Features/Platform/Queries/GetPlatformClinicDetailQuery.cs` | Detail; **writes a `PlatformAccessEntry`** (AC-3.5) |
| `Application/Features/Platform/Queries/GetPlatformAccessLogQuery.cs` | The **reader** of the console's own ledger (FR-5, AC-7.3): paged, newest first, filterable by console account and by cabinet. Without it the ledger is write-only and « qui a regardé quoi » is answerable only by `psql` |
| `Application/Features/Platform/Dtos/PlatformAccessEntryDto.cs` | Account, action, target cabinet, moment — and its fields go into `PlatformReadShape` like every other console read |
| `Application/Features/Platform/Commands/RecordSubscriptionPeriodCommand.cs` | Wraps the companion's grant handler + idempotency + access entry + optional targeted notify |
| `Application/Features/Platform/Commands/CancelSubscriptionPeriodFromConsoleCommand.cs` | Wraps the companion's cancel handler |
| `Application/Features/Platform/Commands/SuspendClinicFromConsoleCommand.cs` / `UnsuspendClinicFromConsoleCommand.cs` | Wrap the companion's suspension handlers |
| `Application/Features/Platform/Auth/*` (`PlatformLoginCommand`, `EnrolPlatformTotpCommand`, `RedeemPlatformRecoveryCodeCommand`, `ChangePlatformPasswordCommand`) | The four auth actions of the spec's API section |
| `Application/Features/Platform/PlatformAccountProvisioning.cs` | Shared by the two console verbs (create / deactivate), the way `LocalClinicProvisioning` is shared by three callers |
| `Application/Common/Interfaces/IPlatformSessionContext.cs` | « which console account is acting » — the console's analogue of `IClinicContext` |
| `Application/Common/Interfaces/ITotpService.cs` | Generate secret / verify code. Implemented in Infrastructure over `Otp.NET` |
| `Application/Common/Maintenance/PlatformCounterPass.cs` | The pure counter derivation (which audit rows count, which are excluded, how a clinic-local day is bounded) — unit-testable with no database |

### Files to Create — Infrastructure

| File | Purpose |
|------|---------|
| `Infrastructure/Auth/TotpService.cs` | `Otp.NET` behind `ITotpService`, `VerificationWindow(previous: 1, future: 1)` |
| `Infrastructure/Auth/PlatformAuthService.cs` | PBKDF2 hash/verify (reusing `LocalAuthService`'s hasher) + **HS256 JWT with its own key, issuer and audience** |
| `Infrastructure/Auth/PlatformAuthConfig.cs` | Resolves `Console:SigningKey` / issuer / audience / lifetime. **Never** the clinic signing key (AC-1.4) |
| `Infrastructure/Security/PlatformSecretProtector.cs` | Purpose-scoped `IDataProtector` for the TOTP secret at rest (FR-1) |
| `Infrastructure/Repositories/PlatformAccountRepository.cs`, `PlatformAccessEntryRepository.cs`, `ClinicActivityRepository.cs` | EF impls |
| `Infrastructure/Persistence/Configurations/PlatformAccountConfiguration.cs`, `PlatformRecoveryCodeConfiguration.cs`, `PlatformAccessEntryConfiguration.cs`, `ClinicActivityDayConfiguration.cs`, `ClinicActivitySnapshotConfiguration.cs` | Mapping + indexes (unique lowered email; `(ClinicId, Day)` unique; snapshot PK = `ClinicId`) |
| `Infrastructure/Migrations/*_AddPlatformConsole.cs` | Five tables. Hand-written if `dotnet ef` cannot load a fresh assembly (the machine's standing WDAC issue) |

### Files to Create — API

| File | Purpose |
|------|---------|
| `API/Controllers/Platform/PlatformAuthController.cs` | `login` / `totp/enrol` / `recovery` / `password` |
| `API/Controllers/Platform/PlatformClinicsController.cs` | list · summary · detail · subscription · cancel · suspend · unsuspend |
| `API/Controllers/Platform/PlatformAccessLogController.cs` | `GET /api/platform/access-log` — the ledger's read (FR-5's stated audience is console accounts). Read-only: there is no action that creates, edits or deletes an entry, the same construction `AuditController` uses |
| `API/Startup/ConsolePortGate.cs` | The **two-way** refusal: console paths only on the console port, and nothing else there |
| `API/Middleware/PlatformAccountStateMiddleware.cs` | The console's per-request account-state check (AC-1.6): a deactivated `PlatformAccount` or a stale `TokenVersion` → **401 on the next request**. It exists because console requests skip `AccountStateMiddleware` **and** `LocalAuthEnforcementMiddleware`, the product's only two live-state readers, and caches the loaded row on `HttpContext.Items` so the session context reuses one query |
| `API/Startup/PlatformTenantScope.cs` | Declares `UseSystemWide("platform console")` for console requests, and **fails loud** if a console read runs `Unset` (EC-12) |
| `API/BackgroundJobs/ClinicActivityCounterJob.cs` | The daily pass (FR-3) |
| `API/Maintenance/PlatformAccountCommand.cs` | `platform-account create --email … --name …` / `--deactivate` / `--reset-totp` (AC-8.1, AC-8.2, AC-8.5) |

### Files to Create — `console/` (new Next 15 app)

| File | Purpose |
|------|---------|
| `console/package.json`, `tsconfig.json`, `next.config.ts`, `postcss.config.mjs`, `Dockerfile`, `scripts/check-responsive.mjs` | The app itself + the same responsive gate `web/` runs |
| `console/app/layout.tsx`, `globals.css` | French, Tailwind v4, **no** clinic chrome — no nav, no search, no bell, no assistant (FR-7) |
| `console/app/login/page.tsx` | Email + password + one-time code; recovery-code path; **works fully at 320 px** (the likeliest phone use) |
| `console/app/cabinets/page.tsx` | Summary + portfolio list (table ≥ 1024 px, **card list below**, per the device table) |
| `console/app/cabinets/[clinicId]/page.tsx` | Detail: state → activity → trend → ledger → actions |
| `console/app/mot-de-passe/page.tsx` | AC-8.6, the only account action on the web |
| `console/app/journal/page.tsx` | The console's own access log (FR-5): who opened which cabinet and who wrote what, newest first, filterable by account and cabinet. Card list on phone, table on desktop, like the portfolio |
| `console/components/*` (`clinic-table.tsx`, `clinic-card-list.tsx`, `portfolio-summary.tsx`, `activity-trend.tsx`, `record-payment-sheet.tsx`, `cancel-period-dialog.tsx`, `suspend-dialog.tsx`, `counters-freshness.tsx`, `state-badge.tsx`) | Feature components |
| `console/components/ui/*` | The shadcn primitives this app uses, copied from `web/components/ui` |
| `console/lib/api/client.ts`, `console/lib/session.ts`, `console/lib/format.ts` | Console API client (canonical `{ error }` parsing), HttpOnly-cookie session, TND/date formatting |

### Files to Create — Tests

| File | Purpose |
|------|---------|
| `UnitTests/Features/Platform/PlatformReadShapeTests.cs` | **The derived check of AC-7.2** — reflects over every `PlatformClinicsController` action return type, recurses into nested types, and fails on any leaf field not in `PlatformReadShape` |
| `UnitTests/Api/ConsolePortGateTests.cs` | Two-way boundary cases, including the `/api/platform-ish` prefix trap `TrustPortGate` already documents |
| `UnitTests/Api/PlatformTokenIsolationTests.cs` | A console token on a clinic route and a clinic token on a console route are both **401, not 403** (AC-1.4) |
| `UnitTests/Api/PlatformAccountStateTests.cs` | Deactivation and a `TokenVersion` bump each refuse the **next** request with 401 (AC-1.6); an active account passes; a non-console request is untouched. Includes a source-level guard on the ordering (after `UseAuthentication`), as `AccountStateEnforcementTests` does for its own |
| `UnitTests/Api/PlatformRateLimitingTests.cs` | `/api/platform/auth/*` is recognised by `IsAnonymousAuthPath` and by the account capture (AC-1.5) |
| `UnitTests/Features/Platform/PlatformCounterPassTests.cs` | Background actors and console actors excluded (AC-2.2 / EC-10); a zero-activity cabinet yields a row of zeros, not no row (EC-8) |
| `UnitTests/Features/Platform/PlatformAuthTests.cs` | Password alone yields no secret, no codes, no session (AC-1.3 / EC-2); a recovery code is consumed even on a failed sign-in (AC-1.3b) |
| `UnitTests/Features/Platform/PlatformAccessLedgerTests.cs` | Detail read and all three writes recorded; **list reads are not** (AC-3.5) |
| `UnitTests/Features/Platform/PlatformIdempotencyTests.cs` | The same `idempotencyKey` twice yields one entry (AC-4.6 / EC-5) |

### Files to Modify

| File | Changes |
|------|---------|
| `Infrastructure/Deployment/DeploymentProfile.cs` | 16th capability **`ServesPlatformConsole`** — `HostedMultiTenant` ✓, the other two ✗ (FR-2's last bullet). `DeploymentProfileTests` + `DeploymentProfileCoverageTests` extended |
| `API/Program.cs` | Bind `Console:Port` **together with the resolved public port in one `ConfigureKestrel` call** where the capability allows and the port is > 0 — the hosted branch binds nothing today and relies on `ASPNETCORE_URLS`, which an explicit endpoint would override and unbind (see Part 1 step 6); register `ConsolePortGate` in the pipeline **before** everything else, as the trust gate is; add the second JWT bearer scheme + `PlatformConsole` policy; register the counter job; intercept the `platform-account` verb; **fail startup loud** if the console port collides with `Hosting:HttpPort`/`HttpsPort`/`WebPort` (EC-4) |
| `Application/Common/Authorization/AuthorizationPolicies.cs` | A fifth public constant, **`PlatformConsole`**, registered by `ConfigurePolicies` with `AddAuthenticationSchemes(PlatformConsoleScheme)` — declared here and not in `Program.cs` because `ControllerAuthorizationCoverageTests` derives its vocabulary from this class and asserts *defined == applied* in both directions, and separately that every constant is registered by this method (Part 1 step 4) |
| `API/Startup/RateLimiting.cs` | `IsAnonymousAuthPath` recognises `/api/platform/auth` as well as `/api/auth` — a prefix the limiter does not know gets the loose API ceiling, which FR-1 names as the thing nobody would choose deliberately |
| `API/Startup/AuthAttemptAccount.cs` | Same widening, so the limiter and the capture cannot disagree about what an auth attempt is |
| `API/Middleware/…` (`AccountStateMiddleware`, `LocalAuthEnforcementMiddleware`, `TenantScopeMiddleware`) | Skip console requests: a console principal has **no `User` row**, so `RequestAccount` finds nothing and the scope would land `Unset` (which now returns zero rows from every filtered table — EC-12's second half) |
| `Infrastructure/Persistence/ApplicationDbContext.cs` | Register the five new entities. `ClinicActivityDay`/`ClinicActivitySnapshot` carry a `ClinicId`, so they must be an explicit **named decision** in `TenantScopeFilterTests` (read system-wide by the pass and by the console; never by a clinic request) |
| `Infrastructure/Persistence/AuditSaveChangesInterceptor.cs` | Exclude `PlatformAccount`, `PlatformRecoveryCode`, `PlatformAccessEntry`, `ClinicActivityDay`, `ClinicActivitySnapshot` — as `AuditEntry` and `Notification` already are. A console sign-in must not write rows into a clinic's « Journal d'activité » |
| `Infrastructure/Extensions.cs` | Register the new repositories, `ITotpService`, `PlatformAuthService`, `PlatformSecretProtector`, `IPlatformSessionContext` |
| `Application/Common/Interfaces/IAuditActorProvider.cs` | **`AuditActor.Console(accountId)`** + a `ConsolePrefix` constant beside `ProcessPrefix` — the one literal both the grant's attribution and the counter pass's exclusion read (Part 1 step 7a) |
| `Application/Common/Services/AuditActorProvider.cs` | Consult `IPlatformSessionContext` **before** `IClinicContext`, so a console principal is never recorded as a bare clinic user id. Resolve-once caching unchanged; the clinic path unchanged |
| `Application/Common/Maintenance/SchemaVerificationService.cs` | Three named checks: `platform-account-has-totp-or-unenrolled`, `clinic-activity-day-unique-per-clinic-day`, `clinic-activity-snapshot-covers-every-clinic` |
| `deploy/Caddyfile` | The second site on `127.0.0.1:9443`; the public site gains **no** `/api/platform` route |
| `deploy/docker-compose.hosted.yml`, `deploy/.env.hosted.example` | `console` service (build `../console`), `Console__Port`, `Console__SigningKey`, `ports: ["127.0.0.1:9443:9443"]` on caddy |
| `deploy/README.md` | The operator runbook: how the tunnel is opened, how the first account is bootstrapped, what the vendor can and cannot see |
| `.github/workflows/ci.yml` | Fifth job **console** — `tsc --noEmit` + `check:responsive` + `next build`, exactly the `web` job's gate |
| `CLAUDE.md`, `api/ClinicManagement.API/CLAUDE.md`, `api/ClinicManagement.Infrastructure/CLAUDE.md`, `api/ClinicManagement.Domain/CLAUDE.md` | Keep the maps accurate |

---

## Implementation Stories

### US-1: The vendor runs the practice portfolio from a private console

**Goal:** The vendor reaches a private console over a tunnel, signs in with a password and a one-time code, sees
every cabinet's subscription state beside its real activity, opens one, records a payment that unlocks the
practice within minutes, corrects a mistake and suspends for abuse — and cannot read a single patient record, by
construction and with a check that fails the build.
**Blocked by:** `features/clinic-subscription/` — **spec only, not implemented** (verified). Parts 1–3 are
independent of it and may proceed; **Parts 4–7 are blocked** on it and open with a pre-flight name check.
**Layers:** DB · Domain · Application · API · Jobs · Deploy · UI (new app) · Tests

> Delivered in **seven ordered parts**. Each is a vertical increment and a natural commit boundary.
> **The dependency splits them into two shippable slices:** Parts 1–3 = the private, read-only console
> (buildable now); Parts 4–7 = the write surface (blocked until the companion ships). The split is where the
> plan stops if the companion is still absent — not a smaller version of the same work.

---

#### Part 1 — Reach the console and sign in *(US-1, AC-1.x, AC-8.x, EC-1, EC-2, EC-3, EC-4)*

**Increment:** the vendor opens an SSH tunnel, loads the console at `https://127.0.0.1:9443`, signs in with the
bootstrapped account, its enrolment secret and a one-time code, and lands on an (empty) « Cabinets » shell. The
same paths on the public domain 404.

1. Add `ServesPlatformConsole` to `DeploymentProfile` (`HostedMultiTenant` only) and extend
   `DeploymentProfileTests` + `DeploymentProfileCoverageTests`.
   ⚠️ The capability decides *may this exist*; `Console:Port` decides *is it bound*. **Off means absent** — no
   listener, no routes mapped, 404 — not present-and-refusing (AC-1.8), the same shape as `Hosting:TrustPort = 0`.
2. Create `PlatformAccount` + `PlatformRecoveryCode` (Domain), their configurations and the migration. Unique
   index on the lowered email through `EmailNormalization`. Lockout mirrors `User`'s 15-minute shape (AC-1.5).
3. Add `PlatformSecretProtector` (TOTP secret encrypted at rest) and `TotpService` over `Otp.NET`.
4. Add `PlatformAuthConfig` + `PlatformAuthService`: PBKDF2 through the existing `PasswordHasher`, HS256 JWT with
   **its own signing key, issuer and audience**. Register a second `JwtBearer` scheme (`PlatformConsole`) and a
   matching authorization policy; console controllers carry that policy and nothing else.
   ⚠️ Distinct issuer/audience is what makes AC-1.4's « refused as unauthenticated, not merely unauthorised »
   true: each scheme fails signature/issuer validation on the other's token, which is a 401 by construction.
   ⚠️ **Where the policy is declared is not a style question — three existing guards decide it.**
   a. Declare **`AuthorizationPolicies.PlatformConsole`** as a public constant on `AuthorizationPolicies`
      (Application) and register it **inside `ConfigurePolicies`**, not in `Program.cs`.
      `ControllerAuthorizationCoverageTests` reads the vocabulary off that class's own constants and asserts
      *defined == applied* **in both directions**, and `Every_Defined_Policy_Is_Registered` resolves each constant
      out of `ConfigurePolicies` — so a policy applied on a console controller but declared at the host fails the
      first, and one declared there but registered at the host fails the second. Declared correctly, both pass
      with no exemption list, which is a property that guard's docstring is explicit about wanting to keep.
   b. **Pin the authentication scheme:** `RequireAuthenticatedUser().AddAuthenticationSchemes(PlatformConsoleScheme)`.
      A policy with no explicit scheme authenticates against the **default** one — the clinic's — so the obvious
      version rejects every console token and the console is unusable from the first request.
      `Microsoft.AspNetCore.Authorization` is already referenced by the Application project, so this belongs there.
   c. **The clinic policies keep no explicit scheme**, deliberately: they continue to use the default, so a console
      token presented to a clinic route fails that scheme's validation and is a 401. That asymmetry — console pins,
      clinic defaults — is what makes AC-1.4 true in *both* directions rather than only one.
   d. Add `PlatformAuth.Login`, `PlatformAuth.EnrolTotp` and `PlatformAuth.Recovery` to
      `ControllerAuthorizationCoverageTests`' **`ExpectedAnonymous`** set (asserted equal in both directions, so a
      new `[AllowAnonymous]` fails until it is reviewed). `PlatformAuth.ChangePassword` stays **out** — it requires
      a console session (AC-8.6).
5. Add `ConsolePortGate` (`API/Startup/`) — a pure static predicate over `(localPort, consolePort, path)`,
   refusing **both** directions, matched with `StartsWithSegments` so `/api/platform-ish` cannot slip through.
   Wire it first in the pipeline, as the trust gate is.
6. `Program.cs`: bind the console listener — and **bind the public port in the same call**.
   ⚠️ **This is not "add one line".** In `HostedMultiTenant` there is no certificate file, so the existing code
   takes the `else` branch and **never calls `ConfigureKestrel` at all**: the only thing binding port 5000 is
   `ASPNETCORE_URLS: http://+:5000` from `deploy/docker-compose.hosted.yml`. Kestrel's explicit endpoints
   **override** the URLs configuration wholesale — so a bare `ConfigureKestrel(k => k.ListenAnyIP(consolePort))`
   would unbind 5000, Caddy's `/api/*` → `api:5000` would stop resolving, and the entire product would go dark
   while the console itself worked perfectly. The failure is loud in Kestrel's log ("Overriding address(es)…")
   and silent everywhere an operator looks.
   So, where the capability allows **and** `Console:Port > 0`:
   a. Resolve the **public** port first, in this order: `Hosting:Urls` → `ASPNETCORE_URLS` → `Hosting:HttpPort`
      → `5000`. This is the port the deployment is already answering on.
   b. Call `ConfigureKestrel` **once**, binding `ListenAnyIP(publicPort)` **and** `ListenAnyIP(consolePort)`
      together — the override then replaces the URLs config deliberately and completely, rather than partially.
   c. **Assert the public port is among the bound endpoints** after configuring, and log both endpoints at
      startup (`publicPort` / `consolePort`), so « what is this process listening on » is answerable from the
      log rather than from `ss -ltnp` inside a container.
   d. **Fail startup loud** naming both settings on a collision (EC-4) — and derive that check from the ports
      **actually resolved for binding** in (a)/(b), *not* from `Hosting:HttpPort` / `HttpsPort` / `WebPort`.
      None of those three keys is set in the hosted compose file, so a check written against them passes
      cheerfully while the two listeners genuinely collide — an EC-4 guard that cannot fire in the one profile
      the console exists on.
   e. Where the capability is off or the port is `0`, **touch none of this**: the `else` branch keeps its
      current `UseUrls`/`ASPNETCORE_URLS` behaviour byte for byte, so `CloudBrowser` and `SelfHostedLan` are
      provably unaffected.
7. Make the three per-request middlewares (`AccountStateMiddleware`, `LocalAuthEnforcementMiddleware`,
   `TenantScopeMiddleware`) **skip console requests**, and add `PlatformTenantScope` declaring
   `UseSystemWide("platform console")` — with a guard that a console read running `Unset` **throws** rather than
   returning zeros (EC-12).
   ⚠️ **Skipping those two leaves a hole that must be filled in the same step, not later.**
   `AccountStateMiddleware` and `LocalAuthEnforcementMiddleware` are the *only* per-request readers of live
   account state, so a console request that skips both never re-reads `PlatformAccount.IsActive` or
   `TokenVersion` — and the AC-8.5 deactivation command would leave a revoked account with full cross-cabinet
   access until its token expired, which is precisely what AC-1.6 forbids and what the Testing Strategy claims
   to assert. Add **`PlatformAccountStateMiddleware`** (`API/Middleware/`), the console's own analogue of the
   pair it replaces:
   a. Runs **after `UseAuthentication`**, on console requests only, and only when a console principal is present.
   b. Loads the `PlatformAccount` **once** and refuses **401** on a deactivated account or a `TokenVersion`
      below the token's — 401 rather than 403 for the same reason the clinic side does it: the credential is no
      longer valid, not merely insufficient.
   c. Caches the loaded row on `HttpContext.Items`, so `IPlatformSessionContext` and the access-ledger writer
      reuse that one query instead of issuing their own — the same `RequestAccount` shape that already lets
      `TenantScopeMiddleware` and `LocalAuthEnforcementMiddleware` share a single account lookup.
   d. Pinned by tests that deactivate an account and bump its `TokenVersion` mid-session and assert the **next**
      request is refused — the assertions AC-1.6 and AC-1.5's revocation half already promised.
7a. **Make the audit actor console-aware here, not in Part 4.** Add `AuditActor.Console(accountId)` with a
    `ConsolePrefix` constant beside `ProcessPrefix`, and make `AuditActorProvider` consult
    `IPlatformSessionContext` **before** `IClinicContext`. It belongs in Part 1 because this is where the console
    principal starts existing: Part 2's counter-pass exclusion reads the constant and Part 4's grant writes it, so
    both later parts consume something that is already correct rather than each patching it. See Part 4 step 4 for
    why the existing seam cannot produce the string on its own.
8. Widen `RateLimiting.IsAnonymousAuthPath` and `AuthAttemptAccount` to `/api/platform/auth` (AC-1.5).
9. Build `PlatformAuthController` with the four actions and the exact status/body shapes of the spec's API
   section. ⚠️ The 403 on a not-yet-enrolled account carries **nothing else** — no secret, no codes, no session.
10. Add the `platform-account` console verb (create / deactivate / reset-totp) sharing
    `PlatformAccountProvisioning`, printing the enrolment secret and a one-time password (AC-8.1/8.2/8.5).
    Gate it on a configured connection string (`MaintenanceDatabase`), not on `HasLocalDbTooling` — amendment M3's
    reasoning applies unchanged.
11. Scaffold `console/`: Next 15 + Tailwind v4 + the shadcn primitives used, `login` page (email, password, code,
    recovery link), `mot-de-passe` page, an empty `cabinets` shell, HttpOnly-cookie session, the canonical
    `{ error }` parsing, French throughout, **no clinic chrome**.
12. `deploy/`: the second Caddy site, the `console` service, `Console__*` env, loopback-only port publication,
    `.env.hosted.example`, and the runbook section in `deploy/README.md`.
13. CI: the fifth `console` job (`tsc --noEmit` + `check:responsive` + `next build`).
14. Tests: `ConsolePortGateTests`, `PlatformTokenIsolationTests`, `PlatformRateLimitingTests`,
    `PlatformAuthTests`, and the `ControllerAuthorizationCoverageTests` update.

**Validation:**
- [ ] Tunnelled `https://127.0.0.1:9443/login` signs in with password + code; `https://{DOMAIN}/api/platform/summary` and `https://{DOMAIN}/cabinets` both 404
- [ ] A clinic token on `/api/platform/*` → 401; a console token on `/api/patients` → 401
- [ ] Password-only sign-in on an unenrolled account returns 403 with no secret and no session
- [ ] A recovery code signs in once and cannot be reused, including when the sign-in it accompanied failed
- [ ] Deactivating a console account mid-session refuses its **very next** request (401), not at token expiry
- [ ] Setting the console port to the web port refuses startup, naming both keys
- [ ] **With the console enabled, the public API is still reachable**: `https://{DOMAIN}/api/auth/mode` answers and the startup log names both bound endpoints (the regression a bare `ConfigureKestrel` would cause)
- [ ] Setting `Console:Port` equal to the port in `ASPNETCORE_URLS` refuses startup — i.e. EC-4 fires in the hosted profile, where `Hosting:HttpPort` is unset
- [ ] `dotnet test` green; `console` CI job green; login usable at 320 px

---

#### Part 2 — The portfolio, and the counters behind it *(AC-2.x, AC-7.2, AC-7.2a, EC-8, EC-9, EC-11, EC-12, EC-15)*

**Increment:** the « Cabinets » screen lists every practice with its state beside its real activity, filterable,
sortable, searchable and paged, under a summary — with the counters' freshness stated on screen.

1. Create `ClinicActivityDay` + `ClinicActivitySnapshot`, configurations, indexes and migration. Register both as
   **named decisions** in `TenantScopeFilterTests` (system-wide reads only).
2. Write `PlatformCounterPass` (Application, pure): given audit rows and a clinic-local day window, derive
   writes / appointments / patients-created / active-days.
   ⚠️ **Not every figure on the row is audit-derived, and one of them cannot be.** AC-2.1 also asks for
   `patients`, `users`, `lastLoginAt` and `clinicCollectedThisMonthDt`. Each source is named here so the job does
   not invent one:
   | Figure | Source |
   |---|---|
   | `writes7d` · `writes30d` · `appointments30d` · patients **created** in the window · `activeDays30d` · `lastWriteAt` | **audit rows**, through the pure pass — windowed, actor-filtered |
   | `patients` (total) | `COUNT` over the clinic's `Patients`. ⚠️ **Never counted from audit `Insert` rows**: the ledger only exists since `adoption-qa-i`, so every patient recorded before it has no row and an established cabinet would read as nearly empty — a figure that is wrong in the direction of « this practice is barely used », i.e. exactly the churn signal the list exists to give |
   | `users` (staff accounts) | `COUNT` over the clinic's `Users` |
   | `lastLoginAt` | `MAX(User.LastLoginAt)` for the clinic (the column exists) |
   | `clinicCollectedThisMonthDt` | the **same repository predicates `GetCaisseSummaryQuery` uses**, through `PlanBillingRules.BilledPlanIds` — `Payment` + `InstallmentPayment` − `CreditNote`, with the bridged-plan de-dup |
   ⚠️ **The money figure makes the console the *fifth* money read**, and the other four (la caisse, l'extrait, le
   dashboard, « Solde patient »/« Créances ») are held equal by `MoneyReadConsistencyTests`. It therefore reuses
   those predicates rather than writing its own `SUM` — a hand-written total here would drift the first time the
   de-dup changed, and the vendor quoting a cabinet its own turnover from a figure the cabinet's own caisse
   contradicts is the worst possible place for that. **Extend `MoneyReadConsistencyTests` so the console's figure
   is pinned to the same value**, exactly as `dashboard-insights` made the dashboard the fourth.
   The point-in-time figures (`patients`, `users`, `lastLoginAt`) live on `ClinicActivitySnapshot` only — they are
   not per-day quantities and `ClinicActivityDay` does not carry them.
   ⚠️ **Two exclusions, both silent failures otherwise (AC-2.2 / EC-10):** actors of the `job|…` kind (backups,
   reminder dispatch, expiry passes) **and** the console's own `console|…` actor — granting a dormant cabinet a
   subscription must not make it read as active the next morning, on exactly the cabinet the « dormant » filter
   just surfaced.
   ⚠️ Both exclusions match on the **prefix constants declared on `AuditActor`** (`ProcessPrefix`, and the new
   `ConsolePrefix` added in Part 4 step 4a) — never on a retyped `"job|"` / `"console|"` literal here. A second
   copy of the prefix is a filter that keeps passing while the writer moves.
3. Add `ClinicActivityCounterJob` (daily, **not** connectivity-gated — its output is a database row), declaring
   `UseSystemWide`, writing yesterday's `ClinicActivityDay` for every cabinet and recomputing every
   `ClinicActivitySnapshot` including `ComputedAt`. A cabinet with nothing to count gets a **row of zeros**, never
   no row (EC-8).
4. Write `PlatformReadShape` — the declared, closed set of scalar field names the console may return — and
   `PlatformReadShapeTests`, which reflects over the console controller's action return types, recurses into
   nested DTOs and **fails the build** on any leaf outside the set. This is the whole enforcement of US-7
   (AC-7.2); the tenant filter explicitly is not (AC-7.2a).
5. Add `ListPlatformClinicsQuery` (`state` · `expiringWithin` · `dormant` · `q` · `sort` · `page` · `pageSize`)
   over `PagedResult`/`PageRequest`, JOINing the snapshot so filter + sort + page are one bounded query
   (AC-2.4a / EC-11). Free-text through `SearchTerm` + `SqlSearch` (`unaccent`, escaped wildcards). Order on a
   unique column last (`.ThenBy(c => c.Id)`), or `OFFSET` shows a cabinet twice.
6. Add `GetPlatformSummaryQuery` — one bounded read, and `vendorCollectedThisMonthDt` is the **vendor's**
   revenue, never a sum of AC-2.1's per-cabinet figure. Label both so they cannot be read as the same quantity
   (AC-2.7).
7. `countersAsOf` on the list response; the screen states it beside the figures **on every width** (AC-2.8), and a
   portfolio whose counters were never written says so rather than reading as all-dormant (EC-15).
8. UI: `portfolio-summary` (2 / 3 / 6 columns, each figure a link to the list it filters), `clinic-table`
   (≥ 1024 px) and `clinic-card-list` (below), filters in a sheet on phone with the active filter as a removable
   chip, row actions in an explicit menu on every width — nothing hover-only.
9. Tests: `PlatformCounterPassTests`, `PlatformReadShapeTests`, and a paging/sorting test over the snapshot JOIN.

**Validation:**
- [ ] The list filters and sorts on activity across the **portfolio**, not the current page
- [ ] A cabinet that has never worked appears with zeros and a « dormant » marker
- [ ] Adding a patient name to any console DTO fails `PlatformReadShapeTests` (verify by trying it)
- [ ] Console writes and background jobs do not move any activity figure
- [ ] A cabinet whose patients predate the audit ledger still shows its **real** patient count, not the number created since
- [ ] The console's « encaissé par le cabinet » equals that cabinet's own caisse figure for the same month
- [ ] Freshness is on screen at 320 / 390 / 820 / 1180 / 1440 px
- [ ] `verify-schema` passes its three new checks

---

#### Part 3 — One cabinet's detail *(AC-3.x, AC-7.3, EC-13, EC-14)*

**Increment:** clicking a cabinet opens its detail — the same figures plus a six-month trend, the full payment
ledger including cancelled entries, and the administrator's contact — and the read is recorded.

1. `GetPlatformClinicDetailQuery`: the list row + admin name/email + staff count + the six-month trend from
   `ClinicActivityDay` + the `SubscriptionPeriod` ledger (cancelled entries included, with reason, canceller and
   moment).
2. Write a `PlatformAccessEntry` for the read (AC-7.3). ⚠️ **List reads are deliberately not recorded** (AC-3.5):
   one list read touches every cabinet, and a row per cabinet per page load would drown every reading anyone
   wants.
3. A cabinet with **no end date** says so in words; never a computed date (EC-14). An unknown cabinet renders a
   French « ce cabinet n'existe plus » state with a way back, not an error page (EC-13).
4. UI: single column on phone/tablet, two on desktop; the trend scrolls in its own container and **its values are
   also given as text** (Non-Functional Hints); the ledger is a card list below its hinge with a cancelled entry
   marked in **words** as well as struck through.
5. **Make the console's own ledger readable** — `GetPlatformAccessLogQuery` + `GET /api/platform/access-log` +
   `console/app/journal/page.tsx`. It lands here, beside the write that produces the rows, rather than being left
   implicit: FR-5 names console accounts as the ledger's audience and AC-7.3 says « qui a regardé quoi » is
   answerable, and an `AddAsync`-only repository makes both answerable **only by `psql` against production**.
   Paged through `PageRequest`, newest first, ordered on a unique column last, filterable by console account and
   by cabinet; the DTO's fields join `PlatformReadShape` so the derived check covers this read too.
   ⚠️ It stays a **console** read. Showing a *clinic* who from the vendor looked at its cabinet is named out of
   scope in the spec — a second read surface with its own audience, deliberately a separate decision.
   ⚠️ Read-only by construction, like `AuditController`: no action creates, edits or deletes an entry.
6. Tests: `PlatformAccessLedgerTests` (detail recorded, list not, **and the read returns what the write recorded**),
   and a `PlatformReadShapeTests` run over the detail and access-log DTOs.

**Validation:**
- [ ] The detail shows the trend, the ledger and the admin's contact, and no clinical or per-patient value
- [ ] Opening a detail writes exactly one ledger row; loading the list writes none
- [ ] « Journal » lists that row, filterable by account and by cabinet, and offers no way to edit or delete one
- [ ] A never-expiring cabinet says so rather than showing a date
- [ ] Greyscale: « Expiré » and « Suspendu » remain distinguishable

---

#### Part 4 — Record a payment and unlock the cabinet *(AC-4.x, EC-5, EC-6, EC-14, FR-4, FR-6)*

**Increment:** the vendor records a received payment from a cabinet's detail; the console shows the new state
immediately and the clinic's own app is working again without anyone signing out.

> ⚠️ **BLOCKED until `features/clinic-subscription/` ships.** Parts 4–7 have no buildable form before it.

0. **Pre-flight (do this before writing anything).** Look up, by name in the source, each of the six rows of
   *Assumed dependency surface*: the `ClinicSubscription` aggregate + `SubscriptionPeriod` ledger, the single
   state/`EndsOn`/`DaysRemaining` authority, the four write handlers, the write-refusal gate's allow-list, the
   subscription re-read, and the declared realtime resource key. **A name that merely differs is adapted; a name
   that is missing stops Part 4.** Do not write a console-side end-date computation, a console-side state fold or
   a console-owned period entity as a stand-in — that is the second arithmetic FR-4 exists to forbid, and it
   would then have to be un-written rather than adapted. Replace the Part 2/3 placeholder state resolver here,
   deleting it rather than layering over it.
1. `RecordSubscriptionPeriodCommand`: validates the cabinet exists and the duration is positive (AC-4.5), then
   delegates to the companion's grant handler — **no second arithmetic** (AC-4.2). Supports « offert » with no
   amount (AC-4.8) and a never-expiring cabinet (EC-14).
2. **Idempotency on `idempotencyKey`**: a repeated submission of the same action yields one entry and the same
   response (AC-4.6 / EC-5). ⚠️ Deliberately **no conflict response** — two *different* grants landing together
   are two entries in an append-only ledger and the surplus one is cancelled (EC-6); reporting a 409 would promise
   an outcome this ledger cannot produce.
3. Name `/api/platform/*` on the companion feature's write-refusal **allow-list**, and assert it in a test.
   ⚠️ Without it, a console caller has no cabinet of its own, the gate finds no entitlement and refuses under the
   *missing-entitlement* code — on the one endpoint whose purpose is to end the refusal, against precisely the
   cabinets that have lapsed.
4. Write the `PlatformAccessEntry` and attribute the audit row to **`console|{accountId}`** so the grant appears in
   that cabinet's own journal, distinguishably from a clinic user (AC-4.7) — and is excluded from its activity
   figures (Part 2, step 2).
   ⚠️ **The existing seam cannot produce that string, and getting it wrong is silent.**
   `AuditActorProvider.Resolve()` returns the token's `sub` **first**, so a console request would be recorded as a
   bare GUID — indistinguishable from a clinic user in « Journal d'activité », and invisible to the counter pass's
   `console|` exclusion, which would then match nothing. `RunAs` is no help: it is a no-op once the actor has
   resolved (and the token always resolves first), and `AuditActor.Process` prefixes **`job|`**, so it would yield
   `job|console|…`. The fix is two small, deliberate changes, **both landed in Part 1 (step 7a)** and merely
   consumed here:
   a. **`AuditActor.Console(accountId)`**, beside `Process()`, with its own **`ConsolePrefix`** constant declared
      next to `ProcessPrefix`. The counter pass's exclusion reads **that same constant** — never a retyped
      `"console|"` literal — so the writer and the filter cannot drift (the `fixes-dont-propagate` shape).
   b. **`AuditActorProvider` becomes console-aware**: it asks `IPlatformSessionContext` (introduced in Part 1)
      **before** `IClinicContext`, and returns the console actor when a console account is acting. A platform
      principal must never be read as a clinic user id. The clinic path is otherwise untouched, and the
      resolve-once caching contract is unchanged.
   c. Two tests, and they are what make the silent version fail the build: a console-principal scope resolves to
      a `console|`-prefixed actor, and `PlatformCounterPassTests` excludes it **by the shared constant**.
5. **AC-4.4 is the companion's re-read, and this feature adds no second mechanism.** Optionally (AC-4.4a) notify
   the **target** cabinet explicitly via `IRealtimeNotifier` with the companion's **existing** declared resource
   key. ⚠️ The pipeline behaviour's defaults are both wrong here: its audience is the *acting user's* clinic
   (a console account has none, so the signal reaches nobody, invisibly) and its key is derived from the
   namespace (a new key nothing subscribes to fails `RealtimeResourceResolverTests`). Exclude the console commands
   from the automatic broadcast and address the clinic group by hand.
6. UI: `record-payment-sheet` — full-screen `dvh` sheet on phone with the primary action pinned and visible with
   the keyboard open, dismissible by a visible control and Escape, confirming before discarding typed input;
   dialog on desktop. The control is disabled in flight.

**Validation:**
- [ ] Recording a payment on an expired cabinet succeeds and the console shows the new state and end date at once
- [ ] The clinic's banner clears without anyone signing out, within the companion feature's stated delay
- [ ] Double-clicking « Enregistrer le paiement » produces one entry
- [ ] A non-positive duration and an unknown cabinet are refused with a message naming which
- [ ] The grant appears in the cabinet's journal attributed to the console account, and moves no activity figure

---

#### Part 5 — Correct a mistake *(AC-5.x, EC-4/EC-7 of the companion, EC-7 here)*

**Increment:** the vendor cancels an entry recorded wrongly; it stays visible struck through with its reason, and
the end date recomputes.

1. `CancelSubscriptionPeriodFromConsoleCommand` — mandatory written reason (AC-5.1), delegating to the companion's
   cancel handler. The entry is never edited and never deleted (AC-5.2).
2. The confirmation **names the cabinet and the amount** (AC-5.4) and states, before committing, that the cabinet
   will become read-only and from which date (AC-5.3 / EC-7). Compute that consequence **from the companion's own
   fold**, not from a console-side estimate.
3. `PlatformAccessEntry` + the cabinet's journal row, as in Part 4.
4. UI: bottom sheet on phone, dialog on desktop; the cancelled entry is marked in words as well as struck through.

**Validation:**
- [ ] Cancelling a three-week-old grant recomputes the end date, possibly into the past, and the cabinet's banner appears without a reload
- [ ] The reason is mandatory; a blank one is refused in French
- [ ] The confirmation names cabinet and amount, so two open tabs cannot cancel the wrong one

---

#### Part 6 — Suspend for abuse *(AC-6.x, EC-11 of the companion)*

**Increment:** the vendor suspends a cabinet independently of its payment state, and unsuspends it without
consuming paid days.

1. `SuspendClinicFromConsoleCommand` / `UnsuspendClinicFromConsoleCommand` — mandatory reason on suspension
   (AC-6.1), delegating to the companion's handlers. Unsuspending restores whatever entitlement the cabinet had
   (AC-6.4).
2. Suspension is **visually distinct from expiry throughout** the console and is never presented as a payment
   state (AC-6.3) — text and shape, never colour alone.
3. The confirmation names the cabinet and warns the practice will be unable to record new work (AC-6.5).
4. `PlatformAccessEntry` + journal rows.

**Validation:**
- [ ] A suspended cabinet is read-only and its own « Abonnement » screen says *suspended*, never *expired*
- [ ] Unsuspending restores the previous end date exactly; no paid day is lost
- [ ] « Expiré » and « Suspendu » are distinguishable in greyscale, in the list and the detail

---

#### Part 7 — Verification, operator runbook and the promise in plain language *(AC-7.4, FR-8, EC-12, EC-15)*

**Increment:** the deployment is documented, the guarantees are checkable, and the sentence the vendor may say to
a clinic is written down and true.

1. Run `verify-schema` before/after the migration batch and diff, as that command's workflow prescribes.
2. Write the operator runbook in `deploy/README.md`: opening the tunnel, bootstrapping the first account,
   enrolling the factor, storing the recovery codes, deactivating an account, and the four companion commands that
   keep working when the console does not (AC-8.3).
3. Write **what the vendor sees** in plain French, for the clinic: counts, dates, subscription state — **and the
   cabinet's own monthly collected total**. ⚠️ A promise phrased as « nous ne voyons pas vos données » would be
   broader than the truth (AC-7.4); the sentence must include that figure.
4. Confirm the two « cannot look the same » requirements by trying them: an unreachable database reports « je n'ai
   pas pu lire », never an empty portfolio (EC-12); counters that never ran report staleness, never a portfolio of
   dormant cabinets (EC-15).
5. Update the four `CLAUDE.md` maps.

**Validation:**
- [ ] `verify-schema` clean, before-and-after diff shows only the intended objects
- [ ] Killing the database renders « je n'ai pas pu lire », not « aucun cabinet »
- [ ] A deployment with no counter pass yet reports its staleness explicitly
- [ ] The runbook is followable by someone who has not read this plan

---

## Testing Strategy

The backend unit suite is the **only** automated check the API has, and `console/`'s gate is
`tsc --noEmit` + `check:responsive` + `next build` plus an eye pass — there is no test runner in either web
project. Everything below is therefore either an xUnit test or a stated manual check.

### Unit tests (xUnit + Moq, `api/ClinicManagement.UnitTests/`)

- `ConsolePortGate`: console path on the public port refused; clinic path on the console port refused; the
  `/api/platform-ish` prefix trap; console port `0` (off) refuses nothing and maps nothing.
- `PlatformAuthService` / `PlatformLoginCommand`: password alone yields no secret, no recovery codes and no
  session (AC-1.3, EC-2); enrolment requires a valid generated code and binds nothing on failure; a recovery code
  is single-use and consumed even when the sign-in fails (AC-1.3b); lockout after repeated failures (AC-1.5).
- `PlatformAccountStateMiddleware`: deactivation and a `TokenVersion` bump each take effect on the **next
  request**, as 401 (AC-1.6) — the mechanism added in Part 1 step 7, without which this assertion has nothing to
  test and a revoked console session stays live for its token's whole lifetime.
- `TotpService`: RFC 6238 vectors, and the drift window's edges.
- `MoneyReadConsistencyTests` (extended): the console's `clinicCollectedThisMonthDt` equals la caisse's and the
  dashboard's for the same clinic and window — the console becomes the **fifth** read held to the one figure.
- `PlatformCounterPass`: `job|…` and `console|…` actors excluded (AC-2.2, EC-10); a clinic-local day boundary
  through `ClinicClock` (a 00:30 Tunis write belongs to the new day); a cabinet with no rows yields zeros (EC-8);
  eight same-day trials with near-zero saves are visible as such (EC-9).
- `ListPlatformClinicsQuery`: filter/sort applied before the page is cut; a unique tie-break in the order;
  wildcard escaping in the search term.
- `PlatformReadShapeTests`: **derived** — reflects over the console controller's action return types and fails on
  any leaf field outside `PlatformReadShape`. Verified by temporarily adding a patient name and watching it fail.
- `PlatformAccessLedgerTests`: a detail read and all three writes recorded; a list read recorded **not at all**;
  and `GetPlatformAccessLogQuery` returns what was recorded, filtered by account and by cabinet, paged with a
  unique tie-break — the read half without which FR-5's audience sentence is unimplemented.
- `PlatformIdempotencyTests`: same key twice → one entry, same response (AC-4.6/EC-5); two *different* grants →
  two entries, no conflict (EC-6).
- `PlatformTokenIsolationTests`: both directions refused as **401** (AC-1.4) — which holds only because the
  console policy pins its scheme while the clinic policies keep the default (Part 1 step 4b/4c).
- `ControllerAuthorizationCoverageTests` (extended): `PlatformConsole` is defined **and** applied (both
  directions), is registered by `ConfigurePolicies` in both modes, and the three anonymous console auth actions
  are on `ExpectedAnonymous` — the guard has no exemption list and must keep none.
- `SubscriptionWriteGateTests` (extended): `/api/platform/*` is on the companion's allow-list (FR-4).
- `TenantScopeFilterTests` / `SystemWideCallerCoverageTests` (extended): the two counter tables are a named
  decision; the counter job and the console reads declare `UseSystemWide`.
- `DeploymentProfileTests` / `DeploymentProfileCoverageTests` (extended): `ServesPlatformConsole` is ✓ only for
  `HostedMultiTenant`, and no branch asks `IsLocalMode`.
- `RealtimeResourceResolverTests` (unchanged, must stay green): the console's optional notification introduces no
  new key.

### Schema verification (`dotnet run -- verify-schema`)

The only gate a migration has anywhere in this product. Three named checks:
`platform-account-has-totp-or-unenrolled`, `clinic-activity-day-unique-per-clinic-day`,
`clinic-activity-snapshot-covers-every-clinic`. Run before and after the migration batch and diff.

### Manual / operator verification (no runner exists for these)

- The tunnel walk: public domain 404s every console path and page; the private address serves both halves.
- The device pass at **320 / 390 / 820 / 1180 / 1440 px** on login, list, detail, « Journal », the payment sheet
  and both confirmations — the list must be a **card list** at 820 px, not a table (14 columns).
- Greyscale check on « Expiré » vs « Suspendu ».
- A real end-to-end unlock: expire a test cabinet, record a payment from the console, watch the clinic's banner
  clear without a reload.
- Pull the database and confirm « je n'ai pas pu lire » rather than an empty portfolio.

---

## Risk Register

| ID | Risk | Likelihood | Impact | Part | Mitigation |
|----|------|------------|--------|------|------------|
| R-1 | One story is far past a session's capacity | High | Med | all | Seven ordered parts, each a commit boundary; resume at a part |
| R-2 | **The companion feature does not exist yet** (spec only — verified), so Parts 4–7 have nothing to call | **Certain today** | **High** | 4–7 | Parts 1–3 ship as a read-only console with a named placeholder state resolver; Part 4 opens with a pre-flight name check and **stops** rather than improvising. Adapt call sites only; never re-implement the rule |
| R-3 | The console's paths reachable on the public front door | Low | **High** | 1 | In-code two-way gate on the real local port, never a proxy rule; tested both directions |
| R-3a | **Binding the console listener unbinds the public one**, taking the whole product offline while the console works | **High** if written naively | **High** | 1 | One `ConfigureKestrel` call binding *both* the resolved public port and the console port, a post-configure assertion that the public port is bound, both endpoints logged, and EC-4's collision check derived from the ports actually bound (Part 1 step 6) |
| R-4 | A console request lands `Unset` and reads zeros | Med | **High** | 1–2 | Explicit `UseSystemWide` + a guard that throws; EC-12 stated as a test |
| R-5 | The companion's write-refusal blocks the unlock endpoint | Med | **High** | 4 | Named on the allow-list on both sides, with a test |
| R-6 | Counter pass pollutes activity with machine or vendor writes | **High** (the vendor half cannot work without the Part 1 step 7a change) | High | 1–2 | Actor-kind exclusion in a pure, tested function, matched on `AuditActor`'s own `ProcessPrefix`/`ConsolePrefix` constants; the console actor is made distinguishable in Part 1 rather than assumed |
| R-7 | A future console read leaks clinical data | Low | **High** | 2 | Derived `PlatformReadShapeTests`, verified by deliberately breaking it |
| R-8 | Console login gets the loose API rate ceiling | Med | High | 1 | Widen `IsAnonymousAuthPath` **and** the account capture together, with a test |
| R-9 | A console write's realtime signal reaches nobody | Med | Low | 4 | Address the target clinic explicitly; the re-read is the contract, not this |
| R-10 | Losing the Data Protection key ring now costs sign-in | Low | **High** | 1 | Durable volume already required; the runbook says the bootstrap command is the recovery |
| R-11 | The migration cannot be scaffolded on this machine | High | Low | 1–2 | Hand-write migration + Designer + snapshot, as three prior migrations did; `verify-schema` is the check |
| R-12 | `console/` drifts from `web/`'s copied primitives | Med | Low | 1 | Copy only what is used; CI runs the same three checks |

### R-1: The story is larger than one session
- **Description:** The user chose one user story deliberately. It spans a new identity population, a new listener, a new Next application, five tables, a job and seven endpoints — well past the ~10–12 file heuristic.
- **Likelihood:** High · **Impact:** Medium (schedule, not correctness)
- **Mitigation:** Parts 1–7 are ordered, dependency-respecting and each is a vertical increment; `/implement-story` lands and commits per part.
- **Contingency:** Split at a part boundary. **This is no longer only a contingency:** because the companion feature is not implemented, Parts 1–3 (the read-only console) are the first shippable slice by construction, and Parts 4–7 wait on it (R-2).

### R-2: The companion feature is not there to call
- **Description:** `features/clinic-subscription/` is a spec with no implementation — verified: no plan file, and no `ClinicSubscription`, `SubscriptionPeriod` or grant/suspend handler anywhere in `api/`. Parts 4–6 are written as thin wrappers over handlers that do not exist, and R-5's allow-list has nothing to be named on.
- **Likelihood:** Certain today · **Impact:** High (the whole write half)
- **Mitigation:** The plan is split into two shippable slices at the Part 3/4 boundary. Parts 1–3 depend on `Clinic`, `User` and `AuditEntry` only, and render the state column from one clearly named placeholder resolver. Part 4 begins with a pre-flight lookup of the six assumed names and **stops** if any is absent.
- **Contingency:** None is needed for the read half. For the write half the only correct move is to wait — a console-side entitlement fold would be the FR-4 violation this feature is defined around, and it would have to be deleted rather than adapted once the real one lands.

### R-3: Console paths reachable on the public front door
- **Description:** FR-2 requires a **two-way** refusal, while the product's precedent (`TrustPortGate`) is one-way. Behind Caddy every request arrives on `api:5000`, so a naïve port check cannot tell the two apart and a proxy-set header would make the refusal a configuration rule.
- **Likelihood:** Low · **Impact:** High — the highest-privilege surface in the product, published.
- **Mitigation:** The API binds a **second listener** (`Console:Port`); the private Caddy site proxies `/api/platform/*` there; the public site has no route to it. `ConsolePortGate` keys on the real `Connection.LocalPort`. Tested both directions, including the prefix trap.
- **Contingency:** If a second listener proves impossible in the hosted topology, fall back to a **separate API container** for the console (option 3 of the address question) — accepting that AC-4.4a's optional push is then unavailable.

### R-4: A console request reads nothing and says nothing
- **Description:** `TenantScopeMiddleware` resolves the clinic from the caller's `User` row. A console principal has none, so the scope lands `Unset` — where every filtered table returns **zero rows, with no error**. Every cabinet would show zeros, indistinguishable from a genuinely idle portfolio (EC-8 is a real answer, which is what makes this dangerous).
- **Likelihood:** Medium · **Impact:** High
- **Mitigation:** Console requests skip that middleware and declare `UseSystemWide("platform console")` explicitly; a guard **throws** if a console read runs `Unset`. `SystemWideCallerCoverageTests` is extended to the console reads and the counter job.
- **Contingency:** The thrown fault surfaces as « je n'ai pas pu lire » (EC-12), which is the correct visible outcome.

### R-5: The unlock endpoint is refused by the lapse it exists to clear
- **Description:** The companion feature refuses every write under `/api` for a lapsed cabinet, over an explicit allow-list. A console caller has no cabinet, so the gate resolves no entitlement and refuses under the *missing-entitlement* code — on exactly the endpoint that ends the refusal, against exactly the cabinets that have lapsed. Both specs name it because it is discovered on a real expired cabinet, the moment a customer has just paid.
- **Likelihood:** Medium · **Impact:** High
- **Mitigation:** `/api/platform/*` named on the allow-list in Part 4, with a test asserting it stays named.
- **Contingency:** Recover with the companion's `grant` command (AC-8.3) while the allow-list is fixed.

### R-7: A future change exposes clinical data
- **Description:** The whole of US-7 rests on one narrow read surface. A future field added to a console DTO — a patient name in a « dernière activité » label, a per-patient balance in a debt figure — would defeat it silently.
- **Likelihood:** Low · **Impact:** High
- **Mitigation:** `PlatformReadShape` (declared) + `PlatformReadShapeTests` (derived over the controller's return types, recursing into nested DTOs). Verified by deliberately adding a forbidden field and watching the build fail. Derived rather than a hand-kept list, because a list is a second place to remember and the first thing silently added to it would be the one that matters.
- **Contingency:** The check fails before merge; the field is removed or the set is widened deliberately and reviewed.

---

## Breaking Changes

None for the clinic-facing product. Every change is additive and gated on `ServesPlatformConsole`, which is ✗ on
`SelfHostedLan` and `CloudBrowser` — those two behave byte for byte as today.

Two changes touch shared code and are worth naming:

### Change 1: `IsAnonymousAuthPath` widens to `/api/platform/auth`
- **What changes:** requests to that prefix move from the API rate ceiling (600 / 60 s) to the tight auth window.
- **Who is affected:** nobody today — the prefix does not exist before this feature.
- **Handling:** none needed; the test asserts both predicates moved together.

### Change 2: Five entity types excluded from the audit interceptor
- **What changes:** `PlatformAccount`, `PlatformRecoveryCode`, `PlatformAccessEntry` and the two counter tables write no rows into the clinic activity journal.
- **Who is affected:** nobody — none of these existed before.
- **Handling:** the exclusion is stated beside `AuditEntry`'s and `Notification`'s, with the reason (a console sign-in is not a clinic's history; the counter pass would bury it).

---

## Migrations

### Migration 1: `AddPlatformConsole`
- **What:** five new tables — `PlatformAccounts`, `PlatformRecoveryCodes`, `PlatformAccessEntries`,
  `ClinicActivityDays`, `ClinicActivitySnapshots`. Unique index on the lowered platform-account email; unique
  `(ClinicId, Day)` on the daily counters; `ClinicId` as the snapshot's primary key; an index on
  `(ClinicId, OccurredAt)` for the access ledger.
- **When:** with the deployment, under the existing startup advisory lock (`MigrationLock`).
- **Backfill:** **none.** The counters are written by the first run of the daily pass, and until then the console
  reports its own staleness (AC-2.8 / EC-15) rather than a portfolio of dormant cabinets.
- **Rollback:** purely additive — dropping the five tables restores the previous schema exactly. No existing table
  is altered and no existing row is rewritten.
- **Steps:**
  1. `dotnet run -- verify-schema` **before**, and keep the output.
  2. Apply the migration (startup, under the advisory lock).
  3. `dotnet run -- verify-schema` **after**; diff against step 1 — only the intended objects and the three new
     named checks should differ.
  4. Bootstrap the first console account: `platform-account create --email … --name …`; store the enrolment
     secret and the one-time password, then enrol the factor and store the recovery codes.
  5. Let the counter pass run once (or trigger it), and confirm `countersAsOf` moves on screen.

### Migration 2 (configuration, not schema): the private address
- **What:** `Console__Port`, `Console__SigningKey`, the `console` service and the loopback-published `9443` in
  `docker-compose.hosted.yml`; the second site in `deploy/Caddyfile`.
- **When:** with the same deployment.
- **Rollback:** unset `Console__Port` — the console becomes **absent** (no listener, no routes), not
  present-and-refusing.
- ⚠️ A port collision with `Hosting:HttpPort` / `HttpsPort` / `WebPort` **refuses startup** naming both settings
  (EC-4). Starting would silently make either the console or the whole product unreachable.

---

## Deviations from `/plan-feature`

- **No parallel exploration agents**, matching both specs in this family and this session's standing instruction:
  the relevant seams (`DeploymentProfile`, `TrustPortGate`, `RateLimiting` + `AuthAttemptAccount`, the middleware
  order, `AuditEntry` + its interceptor, the console-verb pattern, the tenant scope, `Paging`, the Caddy/compose
  topology and `web/`'s gate) were read directly against the source during planning.
- **No browser exploration** — this repository has no browser tooling.
- **One user story rather than several**, at the user's explicit direction; structured into seven ordered parts
  and recorded as **R-1** rather than re-litigated.
