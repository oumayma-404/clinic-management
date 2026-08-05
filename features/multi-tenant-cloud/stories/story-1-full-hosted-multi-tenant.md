# Story 1: Full — Hosted multi-tenant profile (desktop clients, hosted data)

**Status:** APPROVED
**Story Status:** in-progress — **Parts A, B and C implemented** (code gate; 2026-08-05). Parts D–F not started.
See [../progress.md](../progress.md) for the part table, eight deviations and the gate results. ⚠️ Part B's session
found a **pre-existing 24-test red baseline** from earlier features, invisible until then because Part A's runner
was blocked; proven unchanged by Part B and awaiting a decision (see progress.md § Part B).
**Layer:** Full — ⚠️ **a deliberate departure from the skill's BE/FE separation rule**, chosen because the plan is
one coherent topology change: the third profile is « the second deployment's infrastructure with the first's
authentication », and splitting it would produce a backend story that cannot be exercised (no login path) and a
frontend story with two one-line diffs. The frontend surface really is two files (`join-wizard.tsx`,
`app/join/page.tsx`) plus five environment keys. Steps are grouped into ordered **parts A–F** mapping 1:1 onto the
plan's US-1…US-6, and the internal ordering is load-bearing — see [Ordering](#ordering-is-load-bearing).
**Depends On:** None
**Blocks:** Phase 2 (Postgres RLS — separate plan)

## Objective

Turn the product into a hosted multi-tenant service: **each clinic installs the existing small Windows desktop
client, all data lives in one hosted backend, and it is safe for an arbitrary number of clinics.** Concretely, when
this story is done an operator can provision clinic #N from the command line, that clinic's staff log in over the
internet with the product's own accounts (not Auth0), every request is scoped to their clinic by two independent
layers instead of one, and the background jobs that must read *across* clinics say so explicitly rather than
relying on a filter being switched off.

The load-bearing change is **not** new infrastructure — the hosted front door and the desktop client already
exist and need no code change. It is that `LocalAuthConfig.IsLocalMode(config)`, one boolean answering ten
unrelated questions across 33 call sites, becomes a resolved **deployment profile** exposing a capability per
question; and that the EF Core global query filter, which is **fail-open and therefore inert today**, starts
refusing when no tenant scope was ever set.

## Acceptance Criteria

_From spec:_ **none — there is no `spec.md`.** The plan was authored directly from the challenged Option A and
says so (`plan.md` → **Spec: none**, and **R-3**). The criteria below are therefore derived from the plan's own
per-story goals and its amendment table, and are the only acceptance criteria this story has. ⚠️ Per R-3, if
scope grows beyond parts A–B, write a `spec.md` first.

_Story-specific:_

**Part A — the profile** — all four met. ⚠️ The two new test classes **compile but could not be run**
(Smart App Control, `0x800711C7`); their assertions were re-executed through a scratch harness instead — see
`../progress.md` § Quality gate, which also records the two defects that found in the guard itself.
- [x] `Deployment:Profile` resolves to one of `SelfHostedLan` | `HostedMultiTenant` | `CloudBrowser`, and when the
      key is **absent** it derives from `Auth:Mode` exactly as today (`Local` → `SelfHostedLan`, else
      `CloudBrowser`) — so every existing install and all seven console verbs keep working with **no config edit**
- [x] Every one of the ~33 `IsLocalMode` branches asks a **named capability**; the only surviving `IsLocalMode(`
      occurrences are inside `DeploymentProfile.Resolve` and `LocalAuthConfig` itself, asserted by a derived guard
      — **30 occurrences across 16 files → 2**
- [x] The capability matrix for `SelfHostedLan` and `CloudBrowser` reproduces today's `IsLocalMode` truth table
      **exactly** (R-2) — asserted against `LocalAuthConfig.IsLocalMode` itself, not a retyped table
- [x] `GET /api/auth/mode` still answers in **both** profiles; `login`/`refresh`/`setup`/`register` still 404 in
      `CloudBrowser`
- [x] ⚠️ **Twelfth capability added** — `ExposesMetaOnboarding`, for `ClinicsController`'s two Meta/WhatsApp
      guards, which had no capability among the plan's eleven (DEV-1, approved)

**Part B — the tenant scope** — all five met, and every one of them **run** (the runner worked this session).
- [x] `ITenantScope` has three states; **only `Unset` refuses**. `Clinic(id)` returns that clinic's rows,
      `SystemWide` returns all — asserted over **every** filtered root derived from `db.Model`, plus the separate
      no-provider-at-all case the design-time factory needs
- [x] The scope is set by **async middleware from the DB-resolved `User.ClinicId`**, never from the JWT claim
      (amendment **C3′**). ⚠️ Resolved through `IUserRepository` + a shared per-request accessor rather than
      `ICurrentClinicResolver`, so the `User` lookup is shared with `LocalAuthEnforcementMiddleware` as the same
      plan bullet requires — same DB-resolved value (DEV-7, approved)
- [x] `UseClinic` then `UseSystemWide` in one scope **throws**; so does the reverse. One scope, one answer
- [x] Every path that reads a filtered entity with no HTTP context sets a scope, enforced by a **derived** guard —
      candidates by reflection over the API assembly + a `CreateScope()` source scan, five named exemptions, and
      the guard is **proven to go red**
- [x] Onboarding still works with **no** clinic in scope (`Unset`) — asserted via `User`/`Clinic` carrying no
      filter, which is the only reason it works
- [x] ⚠️ Three call sites could not take the plan's literal wording, because **what sets the scope was itself
      behind the filter**: the Google dispatcher now takes the clinic from its caller (DEV-5), `PdfGenerationJob`
      resolves its document's clinic through one `IgnoreQueryFilters` projection (DEV-6), and `AddInfrastructure`
      gained a provider floor without which the console verbs' declarations would be decoration (DEV-8)

**Part C — provisioning** — all three met, all run.
- [x] `provision-clinic --name … --admin-email … --admin-name …` creates a clinic + its first admin and prints a
      one-time password; `setup`'s loopback gate and its one-time bootstrap are unchanged in `SelfHostedLan`.
      ⚠️ **The plan's « wrapping `CreateClinicCommand` + `ResetUserPasswordCommand` » was impossible** — the
      reset command needs an HTTP caller and an existing target, and `CreateClinicCommand`'s Local branch refuses
      once *any* user exists, i.e. exactly when clinic #2 is provisioned. The construction was **moved** into
      `LocalClinicProvisioning`, shared with `setup` (DEV-9, approved). ⚠️ A third flag, `--admin-name`, is
      required: `User.CreateLocalUser` throws without one and it is printed on documents
- [x] `register` returns 404 in `HostedMultiTenant`, and `/join` explains **what to do instead** rather than
      404ing. ⚠️ Two additions the plan did not name: a **13th capability** `AllowsSelfRegistration`, because the
      old `UsesLocalAccounts` guard is true in hosted too (DEV-10), and **`selfRegistrationEnabled` on
      `GET /api/auth/mode`**, because `AUTH_MODE` reads `local` in both profiles so the browser could not tell
      them apart (DEV-11). R-2 holds — both shipped profiles behave exactly as before
- [x] An admin can create a staff account (`CreateClinicUserCommand`, `AdminOnly`) → temp password → forced
      change. ⚠️ Shipped **with its UI** (DEV-12): with self-registration closed and no « Créer un compte »
      dialog, a hosted clinic would have had no way to add a colleague at all

**Part D — secrets** · **Part E — storage** · **Part F — operations**
- [ ] `DataProtection:KeyRingPath` is **required** in `HostedMultiTenant` and fails startup loud when unset
- [ ] TTN identity is per-clinic; the per-install cert is a fall-back **only** in `SelfHostedLan`
- [ ] New storage keys are `clinics/{clinicId}/…`; **old flat keys still resolve, with no backfill**
- [ ] `verify-schema`, `reconcile-money` and `restore-backup` run in the hosted profile
- [ ] `/hub/*` reaches the API, so realtime works
- [ ] `Database.Migrate()` is wrapped in a `pg_advisory_lock`
- [ ] `deploy/docker-compose.hosted.yml` + `.env.hosted.example` exist and set all five keys from the plan's table

## Entry Criteria

- [ ] Work happens **on `feature/audit-sections-3-to-10`** — by decision, and no new branch is cut. ⚠️ The plan
      originally said « off `main`, not this branch, which carries unrelated in-flight work »; that reason is
      **gone** — the agenda-density, duplicate-patient and secretary-access work was committed as `90660b9` and
      the tree is clean. The branch is 259 commits ahead of `main`, so this lands in the same PR as the rest of it
- [ ] `git diff HEAD --numstat` is clean, or every dirty file is knowingly excluded before any `git add` — this
      branch has repeatedly carried 40+ dirty files, and a blanket stage swallows in-flight work
- [ ] `plan.md` reads **Status: APPROVED** and **Challenged: Yes** (both passes)
- [ ] `dotnet build api/ClinicManagement.sln` succeeds on the untouched branch — the baseline must be green before
      ~33 call sites move
- [ ] `docker compose up -d` (postgres + minio) is healthy, so `verify-schema`/`reconcile-money` can take their
      **before** snapshots
- [ ] `dotnet run -- verify-schema` and `dotnet run -- reconcile-money` **before** snapshots are captured to files
      — Part D adds a migration, and these two verbs are the only gate that can see it

## Ordering is load-bearing

Parts run **in order**. Three of the dependencies are not stylistic:

```
A (profile)  ──▶ B (tenant scope)   B's middleware + the filter need DeploymentProfile to exist
             └─▶ C (provisioning)   the console verb + the 404 branch are capability questions
A ──▶ F(KeyRingPath) ──▶ D (TTN secrets)
        └── a PFX password protected by Data Protection makes e-invoicing depend on the key
            ring; landing D first means a rotated key silently breaks signing for every clinic
```

- **A before B** — the filter and the scope middleware both branch on the resolved profile.
- **F's `KeyRingPath` requirement before D** — the plan's explicit pitfall.
- **B before C** — `provision-clinic` runs with no HTTP context and must call `UseClinic(created.Id)`; writing it
  before the scope exists means writing it twice.

## Steps

### Part A — Resolve the deployment profile, retire `IsLocalMode` from the call sites

1. **Add the profile type**
   - Create `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs` — `DeploymentKind` enum +
     eleven capability properties + `static DeploymentProfile Resolve(IConfiguration)`.
   - Capabilities: `UsesLocalAccounts`, `FailClosedAuthz`, `EnforcesTokenState`, `UsesDiskStorage`,
     `SelfHostsFrontDoor`, `SelfSignsCertificate`, `RunsAsWindowsService`, **`DefersMigrations`**,
     **`RunsStartupBackfills`**, `ExposesTrustEndpoints`, `HasLocalDbTooling`.
   - ⚠️ `DefersMigrations` and `RunsStartupBackfills` are **two** capabilities on purpose: `Program.cs:319` and
     `Program.cs:580–596` share one `if (isLocalAuthMode)` today, but *when migrations run* is an SCM
     start-timeout question while the catalog + admin backfills are data obligations every profile owes on every
     boot. `HostedMultiTenant` gets them right only by accident under a single flag.
   - Back-compat: `Deployment:Profile` absent ⇒ derive from `Auth:Mode`.

2. **Rewire `Program.cs`'s 17 branches**
   - `Resolve` runs on the early `new ConfigurationBuilder().AddInstallLayers().Build()` config
     (`Program.cs:103`), exactly where `startupIsLocalMode` is read today — the Serilog log path and the outer
     startup-failure catch both need it before `CreateBuilder`.
   - Replace each branch with the capability it actually means. `AuthorizationPolicies.ConfigurePolicies` keeps its
     `bool` parameter (it lives in **Application**, which cannot reference Infrastructure) — pass
     `profile.FailClosedAuthz`.

3. **Rewire the remaining call sites**
   - `Infrastructure/Extensions.cs` (storage + auth-service branches),
     `Infrastructure/Security/LocalDataProtection.cs`, `API/Middleware/SecurityHeadersMiddleware.cs`,
     `API/Controllers/{Auth,Connectivity,Trust,Clinics}Controller.cs`, and all seven `API/Maintenance/*Command.cs`.
   - ⚠️ `Auth:Mode` is read in **three** places with different config layers (host, console verbs, startup). All
     three must go through `Resolve`, or a verb resolves a different profile from the app it maintains.
   - ⚠️ `AuthController` has **four** `if (!IsLocalMode) return NotFound()` guards (`login`, `refresh`, `setup`,
     `register`) → `UsesLocalAccounts`. The **fifth** `IsLocalMode` read is `GET mode`, which *returns* the mode
     and must keep answering in both profiles — converting it to a guard breaks the frontend's mode probe.

4. **Write the two Part-A tests**
   - `UnitTests/Infrastructure/Deployment/DeploymentProfileTests.cs` — the capability matrix per kind + the
     `Auth:Mode` back-compat derivation, asserted against today's `IsLocalMode` truth table.
   - `UnitTests/Common/DeploymentProfileCoverageTests.cs` — **derived guard**: scan the solution's sources for
     `IsLocalMode(` and assert the only occurrences are in `DeploymentProfile.Resolve` and `LocalAuthConfig`.
     Follow `RealtimeResourceResolverTests`' shape (`[CallerFilePath]` → walk to the repo root → read sources).
     ⚠️ **Derived, never a hand-maintained allow-list** — the repo's own documented lesson.
   - ⚠️ `LEARNINGS.md` argues the *opposite* of this part (« gate mode-invariant guards on the **mode** flag, not
     a capability flag », from the `httpsConfigured` incident). This is safe because each capability is derived
     **from the resolved profile**, never from a config value that merely co-occurs with it. Say so in a comment;
     the truth-table assertion is what makes it more than a claim.

### Part B — `ITenantScope`: make the query filter mean something *(the critical fix)*

5. **Add the scope**
   - Create `Application/Common/Interfaces/ITenantScope.cs` (`TenantScopeKind { Unset, Clinic, SystemWide }`,
     `Kind`, `ClinicId`, `UseClinic(Guid)`, `UseSystemWide(string reason)`) and
     `Application/Common/Services/TenantScope.cs` — **scoped**, single-assignment, `reason` logged.
   - ⚠️ Settle the semantics the interface leaves open, because they are now load-bearing: `UseClinic(x)` then
     `UseSystemWide(…)` **throws** rather than silently widening, and `UseSystemWide` then `UseClinic(x)` is
     refused too. A widening call is how a single-clinic path quietly becomes cross-clinic.

6. **Make the filter mean it**
   - `Infrastructure/Persistence/ApplicationDbContext.cs` — replace `IsClinicScoped`/`ScopedClinicId` (lines
     27–28); all **21** `HasQueryFilter`s become `IsSystemWide || ClinicId == ScopedClinicId`.
   - ⚠️ Both must stay **instance properties** so EF re-evaluates them per query and never bakes them into the
     cached model. The existing comment at lines 24–26 says exactly this — keep it true.
   - `Application/Common/Services/CurrentClinicProvider.cs` → reads the **scope** for the filter instead of the
     claim. Rewrite its docstring: the « fail-open is deliberate » paragraph and the claim-equals-DB invariant are
     both retired by C3′.

7. **Set the scope per request — from the DB, in async middleware**
   - One middleware after `UseAuthentication` resolving through `ICurrentClinicResolver`
     (**DB-resolved `User.ClinicId`**, not the JWT claim — amendment **C3′**) → `UseClinic(id)`.
   - ⚠️ Why not the claim: `ClinicContext.GetClinicId()` reads the *namespaced* `https://clinic-management.com/clinic_id`
     in Cloud, emitted only by an Auth0 tenant Action that **does not live in this repo**; and a token minted
     before a user's clinic changed diverges until refreshed. Fail-open made that harmless; a refusing filter
     turns it into **zero rows with no error**.
   - In Local this costs nothing: `LocalAuthEnforcementMiddleware.cs:37` already loads the same `User` row per
     request — share one lookup rather than issue two.

8. **Declare the cross-clinic readers, and narrow the ones that are not**
   - `UseSystemWide(reason)`: the **five** recurring jobs (`NotificationJob`, `EInvoiceOutboxJob`,
     `StockExpiryJob`, `BackupJob`, **`DocumentEmailJob`**), the seven `Maintenance/*Command`s, and the startup
     scope at `Program.cs:582–596`.
   - `UseClinic(id)` — because `SystemWide` switches the backstop **off for the whole scope**, and these three
     handle exactly one clinic: `PdfGenerationJob` (one document), the App→Google dispatcher's child scope (one
     appointment; the clinic is already loaded to resolve the connection), `provision-clinic` (one clinic).
   - ⚠️ **Do not** add a scope call to `ClinicCatalogSeeder`: every read there already calls `IgnoreQueryFilters()`
     (`ClinicCatalogSeeder.cs:50, 62, 74, 86, 138, 165`) and `_context.Clinics` is unfiltered. It is structurally
     immune, and listing it implies a dependency that does not exist.

9. **Write the two Part-B tests, and extend the isolation suite**
   - `UnitTests/Common/TenantScopeFilterTests.cs` — `Unset` ⇒ no rows; `Clinic` ⇒ only that clinic; `SystemWide`
     ⇒ all.
   - `UnitTests/Common/SystemWideCallerCoverageTests.cs` — **derived from the criterion**, not from a folder list:
     every type under `API/BackgroundJobs/`, every `API/Maintenance/*Command`, every `API/Startup/*` hosted
     service and every `IServiceScopeFactory.CreateScope()` site must contain a `UseSystemWide`/`UseClinic` call
     or be a named, reasoned exemption. ⚠️ The criterion is « reads a filtered entity with no HTTP context » —
     reading it off « is it a job? » is what produced a wrong list in both directions during the challenge.
   - Extend `*TenantIsolationTests` twice: an `Unset`-scope case per filtered aggregate, **and** a cross-clinic
     by-id case for each of the **seven unfilterable clinical tables** (`MedicalDocument`, `DentalRecord`,
     `PatientMedicalHistory`, `PatientFamilyHistory`, `PatientFile`, `PatientFolder`, `ToothState`) — those have
     no `ClinicId` column, so the per-handler check is their only layer and is the only thing a test can hold.

10. **Assert the two silent-failure edges**
    - Onboarding under `Unset`: `auth/mode`, login, register, `POST /clinics`, `POST /clinics/join`,
      `user-status` — they work only because `User`/`Clinic` are unfiltered. Assert it, don't assume it.
    - `AuditEntries` stays unfiltered (nullable `ClinicId`; `GetAuditEntriesQuery` filters explicitly). Leave it.
    - ⚠️ **SignalR**: HTTP middleware does **not** run per hub invocation. `ClinicHub` is safe today because
      `OnConnectedAsync` reads only `User`, but a future hub method touching a filtered entity lands in `Unset`
      and reads nothing, silently. Leave a note at the hub + a test.

### Part C — Provisioning and onboarding over the internet

11. **Add the `provision-clinic` console verb**
    - Create `API/Maintenance/ProvisionClinicCommand.cs` wrapping `CreateClinicCommand` + `ResetUserPasswordCommand`
      (which already mints a CSPRNG temp password); print the one-time password. Calls `UseClinic(created.Id)`
      once the clinic exists.
    - ⚠️ The `setup` endpoint's `LocalRequest.IsLoopback` gate is right for a LAN box and **impossible** over the
      internet — hence a verb. Keep `setup` loopback-gated and unchanged in `SelfHostedLan`.
    - ⚠️ Document the hosted invocation:
      `docker exec clinic-api-prod dotnet ClinicManagement.API.dll provision-clinic …` — env is inherited, so
      `AddInstallLayers()` resolves the same connection string as the running app.

12. **Close self-registration, add admin-created staff accounts**
    - `AuthController.Register` → 404 in `HostedMultiTenant`: a 6-character clinic code is a LAN-scale gate, and
      on the internet it is known to everyone who ever worked at the practice.
    - Add `CreateClinicUserCommand` (`AdminOnly`) — ⚠️ verified absent: `UsersController` exposes exactly `GET`,
      `POST {id}/reset-password`, `PUT {id}/status`, `PUT {id}/role`. Onboarding becomes: admin creates the user →
      hands over the temp password → forced change on first login (`must_change_password` already exists).

13. **Say what to do instead in the UI**
    - `web/components/join-wizard.tsx` + `web/app/join/page.tsx` — hide the join path in this profile and
      **explain the alternative**. ⚠️ § 0 of the device contract: never remove a capability silently. Not a 404.
    - Frontend gate applies: `npx tsc --noEmit` + `npm run check:responsive` + `npm run build`, then an eye pass at
      320/390/820/1180/1440 px.

### Part D — Per-clinic secrets *(after step 17's `KeyRingPath` requirement)*

14. **Per-clinic TTN identity**
    - `Domain/Entities/Clinic.cs` + its configuration + a migration: `TtnUsername`, `TtnApiSecret` (protected),
      `TtnCertificateKey` (storage key), `TtnCertificatePassword` (protected).
    - `Infrastructure/Services/XadesEInvoiceSigner.cs` + `TtnConfig` — take the **clinic's** cert; fall back to
      the per-install one **only** in `SelfHostedLan`.
    - ⚠️ Hand-write the migration if `dotnet ef` cannot load a freshly-built assembly (Smart App Control,
      `0x800711C7`) — the two most recent migrations in this repo were written that way for exactly this reason.
      Never `--no-build`; scaffold with `-p:BaseOutputPath` if the API is running and holding `bin/Debug`.

### Part E — Clinic-prefixed storage keys (new keys only)

15. **Prefix new keys, leave readers alone**
    - `Infrastructure/Storage/MinioFileStorage.cs` — default key becomes `clinics/{clinicId}/{guid}-{timestamp}`.
    - Readers stay unchanged: they resolve whatever `StorageKey` the row holds, so **old flat keys keep working
      with no backfill** (amendment M2).
    - ⚠️ `DoctorCachetKey` is dereferenced by the **unauthenticated** `PdfGenerationJob`, and a key-format
      assumption there breaks cachet rendering *silently* — the renderer falls back to a plain signature line.
      Verify the job reads the stored key **verbatim**.
    - Pass the clinic id to every custom-path caller (`patient-files`, cachet upload); check **every**
      `IFileStorage` call site that supplies its own path.

### Part F — Operations

16. **Route the hub, and make the profile runnable**
    - `deploy/Caddyfile` — add `handle /hub/* { reverse_proxy api:5000 }` **before** the catch-all. ⚠️ The hub is
      mapped at the **host root** (`/hub/clinic`), so today it lands on `web:3000` and 404s; realtime is silently
      dead in the hosted deployment **already**, because `use-clinic-realtime.ts:51` is a bare `catch {}` and the
      server-side broadcast is swallowed by design. Caddy forwards the WebSocket upgrade automatically.
    - Create `deploy/docker-compose.hosted.yml` + `deploy/.env.hosted.example`, reusing `postgres`, `minio`,
      `caddy`, `backup` and `pitr` from the Cloud compose; only `api` and `web` differ. All five keys from the
      plan's table are required, and **each fails quietly if omitted**: `Deployment__Profile`,
      `DataProtection__KeyRingPath` + a **named volume**, `AUTH_MODE=local`,
      `API_INTERNAL_URL=http://api:5000/api`, `AUTH_COOKIE_SECURE=true`. **No `Auth0__*`/`AUTH0_*` at all.**

17. **Harden the hosted runtime**
    - `LocalDataProtection.cs` — `DataProtection:KeyRingPath` **required** in `HostedMultiTenant`, fail loud at
      startup. Today it falls back to the framework default, which is per-instance and ephemeral; the symptom is
      every clinic's reminder channel reporting « non configuré » after a deploy. **This step gates Part D.**
    - Ungate the three console verbs on `HasLocalDbTooling` / « has a direct DB connection », not on the profile
      (amendment M3) — `verify-schema` must run against the hosted DB, since it is the only migration gate in the
      product.
    - Wrap `context.Database.Migrate()` in a **`pg_advisory_lock`**: EF Core 8 takes none, so two instances
      starting together race.
    - `MapHealthChecks("/health")` (DB + storage), exempt from the rate limiter and from auth.
    - **Outbox-depth read** (`AdminOnly`): pending / **blocked** / failed reminder rows, queued e-invoices, queued
      document emails. ⚠️ `/hangfire` is loopback-only in **both** modes and behind Caddy every request's
      `RemoteIpAddress` is the proxy container — correct as security, total as blindness, and **R-1's failure mode
      is a job that reads nothing and logs success**. The per-clinic figures already exist
      (`GET /api/clinics/reminder-status`).
    - `RateLimiting.cs` — re-key **`AnonymousAuthPolicy`** on the submitted email (+ address as a second
      dimension). ⚠️ Not a per-clinic partition: the global limiter already keys per authenticated user
      (`RateLimiting.cs:165`), and no clinic is known *before* login. The real exposure is one practice's whole
      staff sharing one login bucket behind one public NAT address.
    - `SecurityHeadersMiddleware.cs` — promote CSP from `Report-Only` to enforcing behind a config flag, after
      checking it against Next's own policy (the two headers intersect).

18. **Update the repo's own map**
    - Three `CLAUDE.md` files describe the filter contract Part B inverts: Infrastructure's « fail-open is
      deliberate », Application's « not an isolation guard », the root guide's « fail-open — inactive when no
      clinic is in scope ». Update all three plus `CurrentClinicProvider`'s docstring **in this story**, or the
      repo's map contradicts the code.
    - Add the third row to the root guide's topology table (`HostedMultiTenant` → built) and document
      `Deployment:Profile` in the API guide's config-keys list.
    - `packaging/README.md` / a `deploy/` note: the hosted operator view — how to provision a clinic, how to run
      the three verbs via `docker exec`.

## Files to Create/Modify

### Files to Create

| File | Purpose |
|------|---------|
| `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs` | The resolved profile + eleven capabilities + `Resolve` |
| `api/ClinicManagement.Application/Common/Interfaces/ITenantScope.cs` | Three-valued tenant scope contract |
| `api/ClinicManagement.Application/Common/Services/TenantScope.cs` | Scoped, single-assignment implementation |
| `api/ClinicManagement.API/Maintenance/ProvisionClinicCommand.cs` | `provision-clinic` console verb |
| `api/ClinicManagement.Application/Features/Users/CreateClinicUserCommand.cs` | Admin-created staff accounts (`AdminOnly`) |
| `api/ClinicManagement.Infrastructure/Migrations/*_AddPerClinicTtnIdentity.cs` | Four `Clinic` columns; hand-written if `dotnet ef` cannot load the assembly |
| `api/ClinicManagement.UnitTests/Infrastructure/Deployment/DeploymentProfileTests.cs` | Capability matrix + `Auth:Mode` back-compat |
| `api/ClinicManagement.UnitTests/Common/DeploymentProfileCoverageTests.cs` | Derived guard: `IsLocalMode` retired from call sites |
| `api/ClinicManagement.UnitTests/Common/TenantScopeFilterTests.cs` | Unset / Clinic / SystemWide behaviour |
| `api/ClinicManagement.UnitTests/Common/SystemWideCallerCoverageTests.cs` | Derived guard over the no-HTTP-context criterion |
| `deploy/docker-compose.hosted.yml` | The hosted multi-tenant stack |
| `deploy/.env.hosted.example` | Its five required keys, each with the consequence of omitting it |

### Files to Modify

| File | Changes |
|------|---------|
| `api/ClinicManagement.API/Program.cs` | 17 branches → capabilities; async tenant-scope middleware; `UseSystemWide` on the startup scope; `pg_advisory_lock` around `Migrate()`; `/health` |
| `api/ClinicManagement.Infrastructure/Extensions.cs` | Storage + auth-service branches → capabilities; register `ITenantScope` |
| `api/ClinicManagement.Infrastructure/Persistence/ApplicationDbContext.cs` | All 21 filters → `IsSystemWide \|\| ClinicId == ScopedClinicId`; instance properties kept |
| `api/ClinicManagement.Application/Common/Services/CurrentClinicProvider.cs` | Reads the scope, not the claim; docstring rewritten (C3′ retires its invariant) |
| `api/ClinicManagement.Infrastructure/Security/LocalDataProtection.cs` | `KeyRingPath` required in `HostedMultiTenant`, fail loud |
| `api/ClinicManagement.API/Middleware/SecurityHeadersMiddleware.cs` | Capability branch; CSP enforcing behind a flag |
| `api/ClinicManagement.API/Startup/RateLimiting.cs` | `AnonymousAuthPolicy` re-keyed on the submitted email + address |
| `api/ClinicManagement.API/Controllers/AuthController.cs` | Four guards → `UsesLocalAccounts`; `GET mode` left answering; `register` 404 in hosted |
| `api/ClinicManagement.API/Controllers/{Connectivity,Trust,Clinics}Controller.cs` | Capability branches |
| `api/ClinicManagement.API/Maintenance/*Command.cs` (7) | Gate on `HasLocalDbTooling`; `UseSystemWide` |
| `api/ClinicManagement.API/BackgroundJobs/*.cs` (6) | `UseSystemWide` on the five scans; `UseClinic` in `PdfGenerationJob` |
| `api/ClinicManagement.Application/Common/Services/AppointmentGoogleSyncDispatcher.cs` | `UseClinic(appointment.ClinicId)` in the child scope |
| `api/ClinicManagement.Domain/Entities/Clinic.cs` + its configuration | Four TTN fields |
| `api/ClinicManagement.Infrastructure/Services/XadesEInvoiceSigner.cs` + `TtnConfig` | Clinic cert; per-install fall-back only in `SelfHostedLan` |
| `api/ClinicManagement.Infrastructure/Storage/MinioFileStorage.cs` | Default key → `clinics/{clinicId}/…` |
| `api/ClinicManagement.UnitTests/**/*TenantIsolationTests.cs` | `Unset` case per aggregate + 7 PHI by-id cases |
| `deploy/Caddyfile` | `handle /hub/*` → `api:5000`, before the catch-all |
| `web/components/join-wizard.tsx`, `web/app/join/page.tsx` | Hide the join path in this profile; say what to do instead |
| `CLAUDE.md`, `api/…/{API,Application,Infrastructure}/CLAUDE.md` | Third topology row; the filter contract is no longer fail-open |

## Verification Steps

**Code gate — runnable in this repo:**

- [ ] Solution builds with **0 errors, 0 warnings**
- [ ] `DeploymentProfileTests` green — matrix + `Auth:Mode` back-compat
- [ ] `DeploymentProfileCoverageTests` green — no `IsLocalMode(` outside `Resolve`/`LocalAuthConfig`
- [ ] `TenantScopeFilterTests` green — all three states
- [ ] `SystemWideCallerCoverageTests` green — and **verify it fails** when you temporarily delete one job's
      `UseSystemWide` call. A derived guard that has never gone red is not yet a guard
- [ ] `*TenantIsolationTests` green, including the seven PHI by-id cases
- [ ] Frontend gate: `npx tsc --noEmit`, `npm run check:responsive`, `npm run build`, then an eye pass at
      320/390/820/1180/1440 px

```bash
# Backend — build to a scratch path so a running API can't lock bin/Debug
dotnet build api/ClinicManagement.sln -p:BaseOutputPath=%TEMP%\cm-build\

# If the runner dies at load with 0x800711C7 (Smart App Control), this is
# environmental, NOT a failing test. Clear and retry before believing red:
dotnet build-server shutdown
#   ...remove bin/ + obj/, rebuild, then:
dotnet test api/ClinicManagement.UnitTests --filter "FullyQualifiedName~DeploymentProfile|FullyQualifiedName~TenantScope|FullyQualifiedName~SystemWideCaller|FullyQualifiedName~TenantIsolation"

# Frontend
cd web && npx tsc --noEmit && npm run check:responsive && npm run build
```

**Operator gate — needs a live DB and a hosted deploy:**

- [ ] `dotnet run -- verify-schema` **before and after** the Part-D migration, diffed — the only gate that can see
      a schema change, since nothing in the test project touches a database
- [ ] `dotnet run -- reconcile-money` before and after, diffed — this story touches nothing financial, so the
      **diff must be empty**
- [ ] Both verbs run in the hosted profile (`docker exec …`), i.e. M3 actually landed
- [ ] A real desktop client, pointed at the hosted domain by address alone, logs in
- [ ] Two clinics cannot see each other's patients
- [ ] **Two browsers, same clinic:** edit an appointment in one → the other refreshes by itself. The only check
      that sees the `/hub/*` route (R-6 — a broken hub and a working one look identical on one screen)
- [ ] Reminders still dispatch — that is the `SystemWide` path, and R-1's failure mode is silence
- [ ] « Envoyer par email » still sends — the `DocumentEmailJob` path the challenge found missing
- [ ] `provision-clinic` creates clinic #2 and prints a usable one-time password
- [ ] An admin-created staff account logs in and is forced to change its password
- [ ] Startup **fails loud** when `DataProtection:KeyRingPath` is unset in `HostedMultiTenant`
- [ ] A file uploaded after this story has a `clinics/{clinicId}/…` key; a document uploaded **before** it still
      downloads, and a practitioner cachet still renders on a PDF

## Exit Criteria

**Closes at `implemented` (code gate):**

- [ ] Every Part-A…F acceptance criterion that does not require a deploy is checked
- [ ] All code-gate verification steps pass
- [ ] `plan.md`'s docs-debt step is done — the three `CLAUDE.md` files and the `CurrentClinicProvider` docstring no
      longer describe a fail-open filter
- [ ] Reviewed (`/review-story`)

**Moves to `done` (operator gate):**

- [ ] All operator-gate verification steps pass on a real hosted deployment
- [ ] The `verify-schema` and `reconcile-money` before/after diffs are recorded in `progress.md`

## Notes

- **Sizing, honestly.** This is far outside the skill's guidelines (3–7 steps, 2–8 files): 18 steps, ~35 files.
  That is the granularity you asked for and the plan's own shape, but it will not fit one session — treat the
  **parts** as the resumable unit, commit at each part boundary, and keep `progress.md` current so a fresh
  session can pick up mid-story. Parts A and B alone are the plan's whole security thesis; D, E and F are
  additive and could be dropped without invalidating A–C.
- **What is deliberately not here.** RLS is Phase 2 (a separate plan, with all three of its traps named). Five
  decisions are **owed and out of scope**: offline behaviour, per-clinic backup/restore, client auto-update + API
  version compatibility, HuggingFace PHI, and compliance (INPDP declaration, DPA per clinic, residency,
  retention). None touches the tenancy seam; all are additive to it.
- **One hazard that belongs to the topology, not to a part.** `restore-backup`'s documented ordering guarantee is
  « refuse while the app's ports are listening » — but in Docker the API listens in a *different* container, so
  nothing stops `pg_restore --clean --if-exists` running against a live application. Separate from owed decision
  #2 (which is about *per-clinic* restore).
- **Why one story and not six — settled, do not re-open.** The question was raised and the alternatives (six
  stories matching US-1…US-6, three, or two) were costed and declined. The technical reason to keep it whole: the
  profile type (A) is a **compile-time** dependency of the scope middleware (B) and of the capability branches in
  C, D, E and F, and every part shares one migration-and-verify cycle. ⚠️ Stated plainly, because the file should
  not oversell itself: that argument justifies the *ordering* more strongly than it justifies the single boundary —
  a split would have kept the ordering as `Depends On` edges. What the single story genuinely buys is one review of
  one coherent topology change and one verify cycle; what it costs is that `/review-story` sees ~35 files at once
  and `/implement-story` cannot report partial completion. Hence the part table above as the resumable unit.
