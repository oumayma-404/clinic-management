# Implementation Plan: Multi-Tenant Cloud (desktop clients, hosted data)

**Status:** APPROVED
**Challenged:** Yes — **two passes.** (1) `/challenge-plan` in-session, 8 lenses, 3 Critical + 4 Major + 1 Minor;
all Criticals changed the plan (see [Amendments](#amendments-from-the-challenge)), RLS was deferred, two items
became owed decisions. (2) `/challenge-plan` re-run against the code, 3 Critical + 5 Major + 4 Minor, **all 12
applied**: the missing `/hub/*` route (realtime silently dead), C3 superseded by **C3′** (the scope is resolved
from the DB in async middleware, not from a JWT claim), the `SystemWide` caller set restated from a criterion
(`DocumentEmailJob` was missing, `ClinicCatalogSeeder` did not belong), the hosted `deploy/` assets, the seven
unfilterable PHI tables named and R-5 corrected, `UseClinic` over `SystemWide` where the clinic is known,
`RunsStartupBackfills` split out of `DefersMigrations`, the rate limiter re-aimed at the anonymous policy, and an
outbox-depth read since `/hangfire` is unreachable in this topology.
**Created:** 2026-08-05
**Spec:** none — this plan was authored directly from the challenged Option A. ⚠️ No `spec.md` means there are no
acceptance criteria to verify against; if this grows past US-2, write one.
**Predecessor:** `features/cloud-deployment/` (APPROVED, implemented) — supplies the hosted front door this plan
depends on and does **not** re-build.
**Branch:** **`feature/audit-sections-3-to-10`** — work continues on the existing branch, by decision.
⚠️ This reverses the plan's original « cut off `main` » instruction, and the reason that instruction existed is
**gone**: the unrelated in-flight work it warned about (agenda density, the duplicate-patient fix, the secretary
clinical-record access) was committed as `90660b9`, so the tree is clean. Two things remain true and worth
knowing: the branch is **259 commits ahead of `main`**, so this work will reach `main` in the same PR as
everything else on it (a review-scope consequence, not a correctness one) — and per the repo's own standing rule,
still run `git diff HEAD --numstat` before any `git add`, because this branch has repeatedly carried 40+ dirty
files and a blanket stage would swallow in-flight work.

---

## Overview

Turn the product into: **each clinic installs a small Windows desktop client; all data lives in one hosted
backend; it is safe for an arbitrary number of clinics.**

Three topologies exist in the code's design space. Two are built:

| | Front door | Data | Login | Built? |
|---|---|---|---|---|
| `SelfHostedLan` | Kestrel + YARP on the clinic's own PC | that PC | own JWT | ✅ |
| `CloudBrowser` | Caddy → Next + API | hosted | Auth0 | ✅ (`cloud-deployment`) |
| **`HostedMultiTenant`** | **Caddy → Next + API** | **hosted** | **own JWT** | ❌ **this plan** |

The third is not a new deployment — it is the **second one's infrastructure with the first one's authentication**.
That single sentence is the whole plan.

### What already exists and is reused unchanged

- **The hosted same-origin front door.** `deploy/Caddyfile` terminates TLS once and routes `/api/*` → `api:5000`,
  everything else → `web:3000`. The desktop shell's one stored address therefore serves both pages and the API.
  ⚠️ **Reused *plus one route*, not unchanged: `/hub/*` is missing and realtime is dead in the hosted profile
  today.** The SignalR hub is mapped at the **host root** (`/hub/clinic`), not under `/api`, and
  `clinic-hub.ts:64` resolves it as `new URL("/hub/clinic", apiOrigin)` — which behind Caddy's catch-all lands on
  **`web:3000`** and 404s. The failure is **totally silent**: `use-clinic-realtime.ts:51` is a bare `catch {}` on
  `connection.start()` and the server-side broadcast is swallowed by design, so every clinic's staff simply see
  stale screens. `features/cloud-deployment` never mentions the hub (zero hits for `hub`/`signalr`/`realtime` in
  its spec *and* its progress), so this is a latent defect of the predecessor that this plan would inherit. Fixed
  in **US-6**.
- **The desktop client.** `desktop/ServerConfig.ParseAddress` accepts a bare host, `host:port` or a **full URL**
  and persists it per user. Pointing it at `https://clinics.example.tn` needs **no code change**.
- **Login, refresh and revocation.** `LocalAuthService` (HS256, `clinic_id` + `role` in the token),
  `RefreshTokenCommand` + `User.TokenVersion`, and `LocalAuthEnforcementMiddleware` (per-request revocation of a
  deactivated account, forced password change). This is a *stronger* posture than the Auth0 path, which has no
  revocation at all.
- **The authoritative tenant check.** `ICurrentClinicResolver` → DB-resolved `User.ClinicId`, re-verified per
  aggregate, in ~128 handlers, pinned by `*TenantIsolationTests`.
- **Fail-closed authorization**, named policies on all 32 controllers, `SecurityHeadersMiddleware` (HSTS on),
  anonymous-auth rate limiting, loopback-only Hangfire, the audit ledger, `xmin` concurrency.

### Amendments from the challenge

| # | Original Option A said | Amended to | Why |
|---|---|---|---|
| **C1** | Use Cloud's hosting answer (no proxy) | **Caddy is the front door — already built.** No YARP in the hosted profile | Cloud maps no proxy and expects two origins; the shell stores one address. `cloud-deployment` already solved this with Caddy, so the "Critical" is mostly *already fixed* |
| **C2** | Flip the query filter fail-closed | **Three-valued `ITenantScope`**: `Clinic(id)` · `SystemWide` · unset. Only *unset* refuses | Fail-open is load-bearing: 4 jobs, the seeder and 2 report readers iterate every clinic with no clinic in scope. Fail-closed alone makes them silently process **nothing** — no error, reminders just stop |
| **C3** | Feed the filter the DB-resolved clinic | ~~Keep the JWT `clinic_id`~~ → **superseded by C3′ below** | `ICurrentClinicProvider.ClinicId` is a *synchronous* property evaluated inside the EF filter expression; a DB lookup there is sync-over-async on the context being queried |
| **C3′** | *(C3 as first amended)* | **The *filter* stays sync; the *scope* is set from the DB in async middleware** | C3's premise is right about the filter and does not constrain the middleware, which **is** async. Setting the scope from a claim keeps a documented live risk — `CurrentClinicProvider`'s own docstring states the invariant « the claim must always equal the DB-resolved `User.ClinicId` » and that a token minted before a user's clinic changes **diverges until refreshed**. Worse, with US-2 making `Unset` refuse, a legitimately absent claim stops being fail-open (right rows via the handler check) and becomes **zero rows with no error** — and in Cloud the claim is the *namespaced* `https://clinic-management.com/clinic_id`, emitted only by an Auth0 tenant Action that **does not live in this repo** and cannot be verified from it. The DB read is already paid for in Local: `LocalAuthEnforcementMiddleware.cs:37` loads the `User` row on every authenticated request |
| **M1** | Add Postgres RLS | **Deferred to Phase 2** | Three traps, one silent (a table owner *bypasses* RLS unless `FORCE ROW LEVEL SECURITY`). Two working layers is already a large gain over today's one |
| **M2** | Prefix storage keys by clinic | **New keys only; readers tolerate old** | Existing rows hold flat `StorageKey`s, and `DoctorCachetKey` is dereferenced by the **unauthenticated** `PdfGenerationJob` |
| **M3** | *(not mentioned)* | **Ungate the three console verbs** | `verify-schema`, `reconcile-money`, `restore-backup` all `return 1` outside Local mode — and `verify-schema` is the *only* gate for a migration, since no test touches a DB |
| **M4** | Replace clinic codes with invitations | **Admin creates the account + one-time password** | An invitation must be sent *before* the person has a clinic, but SMTP is configured **per clinic**. `ResetUserPasswordCommand` already mints a CSPRNG temp password |

### Settled decisions (not open)

| Decision | Choice |
|---|---|
| Tenancy | **One database, one schema, row-level `ClinicId`** |
| Isolation layers | Handler check (exists) + `ITenantScope`-driven query filter (US-2). RLS = Phase 2 |
| Login | Own accounts, hosted. Auth0 stays supported but is not the multi-tenant path |
| Config shape | One `Deployment:Profile`, resolved into **named capabilities** (below) |
| Onboarding | Operator/admin-provisioned; the 6-char clinic code stops being a self-service door |
| Offline | **Out of scope** — decision owed (see [Owed decisions](#owed-decisions)) |
| Per-clinic restore | **Out of scope** — decision owed |

---

## The core refactor: name the six jobs the mode switch is doing

`LocalAuthConfig.IsLocalMode(config)` has **~35 call sites** deciding unrelated things: login provider, storage
backend, authorization fallback, certificate self-signing, Windows-service hosting, migration timing, HSTS
default, connectivity probe, YARP, log path, and every console verb's gate.

⚠️ **Do not replace it with three booleans.** Replace it with one resolved profile exposing a **capability per
question**, so each site asks what it actually means:

```csharp
// Infrastructure/Deployment/DeploymentProfile.cs  (new)
public enum DeploymentKind { SelfHostedLan, HostedMultiTenant, CloudBrowser }

public sealed class DeploymentProfile
{
    public DeploymentKind Kind { get; }

    public bool UsesLocalAccounts    { get; }  // own JWT vs Auth0
    public bool FailClosedAuthz      { get; }  // FallbackPolicy = RequireAuthenticatedUser
    public bool EnforcesTokenState   { get; }  // LocalAuthEnforcementMiddleware
    public bool UsesDiskStorage      { get; }  // LocalDiskFileStorage vs Minio
    public bool SelfHostsFrontDoor   { get; }  // YARP -> co-located Next
    public bool SelfSignsCertificate { get; }  // CertificateProvisioner
    public bool RunsAsWindowsService { get; }  // UseWindowsService + install-relative paths
    public bool DefersMigrations     { get; }  // DeferredStartupService (SCM start timeout)
    public bool RunsStartupBackfills { get; }  // inline seed + admin backfill (see below)
    public bool ExposesTrustEndpoints{ get; }  // TrustController + connectivity probe
    public bool HasLocalDbTooling    { get; }  // pg_dump / pg_restore present

    public static DeploymentProfile Resolve(IConfiguration configuration);
}
```

`HostedMultiTenant` = `UsesLocalAccounts` ✓ · `FailClosedAuthz` ✓ · `EnforcesTokenState` ✓ ·
`UsesDiskStorage` ✗ · `SelfHostsFrontDoor` ✗ · `SelfSignsCertificate` ✗ · `RunsAsWindowsService` ✗ ·
`DefersMigrations` ✗ · **`RunsStartupBackfills` ✓** · `ExposesTrustEndpoints` ✗ · `HasLocalDbTooling` ✗.

⚠️ **`DefersMigrations` and `RunsStartupBackfills` are two questions that today share one `if`, and that is why
they must be split.** `Program.cs:580–596` runs `Database.Migrate()`, `IClinicCatalogSeeder.SeedAllClinicsAsync()`
and `IClinicAdminBackfill.BackfillAsync()` in one `if (!isLocalAuthMode)` block, while `Program.cs:319` registers
`DeferredStartupService` in the other. But *when* migrations run is an **SCM start-timeout** question, whereas the
two backfills are **data** obligations that every profile owes on every boot. Mapped through a single flag,
`HostedMultiTenant` gets them right only **by accident** — and this is precisely the block US-2 must scope
(`UseSystemWide`) and US-6 must wrap (`pg_advisory_lock`), so "correct by luck" is not good enough. Two named
capabilities make the mapping a decision a test can assert.

**Config:** `Deployment:Profile` = `SelfHostedLan` | `HostedMultiTenant` | `CloudBrowser`. When absent, derive it
from `Auth:Mode` exactly as today (`Local` → `SelfHostedLan`, else `CloudBrowser`) so **every existing install and
every console verb keeps working with no config edit** — that back-compat is what makes this refactor safe to land
in one story.

---

## Stories

### US-1 — Resolve the deployment profile, and retire `IsLocalMode` from the call sites

**Goal:** every one of the ~35 branches asks a named capability; behaviour for the two existing profiles is
byte-identical.

*Create*
- `Infrastructure/Deployment/DeploymentProfile.cs` — the type above + `Resolve`.
- `UnitTests/Infrastructure/Deployment/DeploymentProfileTests.cs` — the capability matrix per kind, and the
  `Auth:Mode` back-compat derivation.
- `UnitTests/Common/DeploymentProfileCoverageTests.cs` — **derived guard**: scan the solution's sources for
  `IsLocalMode(` and assert the only remaining occurrence is inside `DeploymentProfile.Resolve` (and
  `LocalAuthConfig` itself). Reflection/`[CallerFilePath]` in the style of `RealtimeResourceResolverTests`.
  ⚠️ **Derived, never a hand-maintained allow-list** — that is the repo's own documented lesson.

*Modify*
- `API/Program.cs` — 17 branches → capability questions. `Resolve` runs on the early
  `ConfigurationBuilder().AddInstallLayers()` config, before `CreateBuilder`, exactly where `startupIsLocalMode`
  is read today.
- `Infrastructure/Extensions.cs` — storage + auth-service branches.
- `Infrastructure/Security/LocalDataProtection.cs`, `API/Middleware/SecurityHeadersMiddleware.cs`,
  `API/Controllers/{Auth,Connectivity,Trust,Clinics}Controller.cs`, the 7 `API/Maintenance/*Command.cs`.

*Pitfalls*
- `Auth:Mode` is read in **three** places with different config layers (host, console verbs, startup). All must go
  through `Resolve`, or a verb resolves a different profile from the app it maintains.
- `AuthController`'s **four** `if (!IsLocalMode) return NotFound()` guards (`login`, `refresh`, `setup`,
  `register`) become `UsesLocalAccounts` — they must stay 404 in `CloudBrowser`, or Auth0 installs grow an
  unauthenticated login endpoint. ⚠️ The **fifth** `IsLocalMode` read in that file is `GET mode`, which *returns*
  the mode and must keep answering in **both** profiles — converting it to a guard breaks the frontend's mode
  probe. (Aside worth a look while in the file: `refresh` is a 6th `[AllowAnonymous]` action, and the allow-list
  `ControllerAuthorizationCoverageTests` pins is documented in `API/CLAUDE.md` as four Auth actions.)
- ⚠️ **`LEARNINGS.md` argues the opposite of this story, and the difference must be stated.** « Gate
  mode-invariant guards on the **mode** flag, not a capability flag » was written after the `httpsConfigured`
  incident, where a flag that merely *correlated* with the mode silently changed Cloud behaviour. US-1 is safe
  because each capability is **derived from the resolved profile**, never from a config value that happens to
  co-occur with it — a genuinely different construction. Say so in the code, and have `DeploymentProfileTests`
  assert the two existing kinds reproduce today's `IsLocalMode` truth table **exactly**, which is the only thing
  that makes the distinction more than a claim.
- **Docs debt is part of this story, not after it.** Three `CLAUDE.md` files describe the filter contract US-2
  inverts — Infrastructure's « fail-open is deliberate », Application's « not an isolation guard », the root
  guide's « fail-open — inactive when no clinic is in scope » — plus `CurrentClinicProvider`'s own docstring and
  its claim-equals-DB invariant (which C3′ retires). Update all four in the same change, or the repo's map
  contradicts the code.

---

### US-2 — `ITenantScope`: make the query filter mean something *(the Critical fix)*

**Goal:** the EF global filter refuses when the scope was never set, while jobs and CLI keep reading every clinic
by saying so explicitly.

*Create*
- `Application/Common/Interfaces/ITenantScope.cs`:
  ```csharp
  public enum TenantScopeKind { Unset, Clinic, SystemWide }
  public interface ITenantScope
  {
      TenantScopeKind Kind { get; }
      Guid? ClinicId { get; }              // non-null iff Kind == Clinic
      void UseClinic(Guid clinicId);       // set once per scope; second call with a different id throws
      void UseSystemWide(string reason);   // reason is logged — "who read across clinics, and why"
  }
  ```
- `Application/Common/Services/TenantScope.cs` — scoped, single-assignment.
- `UnitTests/Common/TenantScopeFilterTests.cs` — unset ⇒ no rows; `Clinic` ⇒ only that clinic; `SystemWide` ⇒ all.
- `UnitTests/Common/SystemWideCallerCoverageTests.cs` — **derived guard** over the criterion below, not over a
  hand-picked folder: every type under `API/BackgroundJobs/`, every `API/Maintenance/*Command`, every
  `API/Startup/*` hosted service, and every `IServiceScopeFactory.CreateScope()` site in the solution must either
  contain a `UseSystemWide`/`UseClinic` call or be a named, reasoned exemption. Fails on a *new* job that forgets,
  which is the exact shape of the silent failure this story exists to prevent.

**The criterion — state it before enumerating.** A path needs the scope set iff it **reads a filtered entity with
no HTTP context**. Reading it off "is it a job?" is what produced the wrong list twice, in both directions:

| Path | Needs it? | Why |
|---|---|---|
| `NotificationJob`, `EInvoiceOutboxJob`, `StockExpiryJob`, `BackupJob`, **`DocumentEmailJob`** | **yes** — all five | Every one reads through a **repository**, and no repository calls `IgnoreQueryFilters()` (`DocumentEmailJob:77`, `EInvoiceOutboxJob:58`, `BackupJob:76`, …). `DocumentEmailJob` is the **fifth recurring job and was missing from this list** — it drains the *filtered* `DocumentEmail` outbox, so « Envoyer par email » would stop silently for every clinic |
| **`PdfGenerationJob`** | **yes** | Runs unauthenticated on demand and re-saves through MediatR. Was only a pitfall note; it belongs in the list |
| **`API/Program.cs`'s startup scope** (`Program.cs:582–596`) | **yes** | The non-Local branch resolves a `DbContext`, migrates, then runs `IClinicCatalogSeeder.SeedAllClinicsAsync()` **and `IClinicAdminBackfill.BackfillAsync()`** in that scope. Unlisted before |
| `Maintenance/*Command` (7) | **yes** | Build their container from `AddInfrastructure` alone |
| `GoogleCalendarSyncService` App→Google | **yes**, but as `UseClinic` — see below | The dispatcher opens a child scope; the appointment's clinic is known |
| ~~`ClinicCatalogSeeder`~~ | **no — drop it** | Every read already calls `IgnoreQueryFilters()` (`ClinicCatalogSeeder.cs:50, 62, 74, 86, 138, 165`) and `_context.Clinics` is unfiltered. It is structurally immune; listing it implies a dependency that does not exist |

*Modify*
- `Infrastructure/Persistence/ApplicationDbContext.cs` — replace `IsClinicScoped`/`ScopedClinicId` with the scope.
  All 21 filters become `IsSystemWide || ClinicId == ScopedClinicId`. ⚠️ Both must stay **instance properties**, so
  EF re-evaluates them per query and never bakes them into the cached model.
- `Application/Common/Services/CurrentClinicProvider.cs` → reads the scope for the filter instead of the claim.
- `API/Program.cs` — one **async** middleware after `UseAuthentication` that resolves the clinic through
  `ICurrentClinicResolver` (**DB-resolved `User.ClinicId`**, not the claim — C3′) and calls `UseClinic(id)`. In
  Local this is free: `LocalAuthEnforcementMiddleware` already loads the same `User` row per request, so the two
  should share one lookup rather than issue two.
- The startup scope in `Program.cs`, the five recurring jobs and the console verbs → `UseSystemWide("<reason>")`
  per the table above.

**`SystemWide` is the widest thing in the design — spend it only on real iteration.** It switches the backstop
**off for the whole scope**, so a missing `WHERE` in the job's own query leaks across clinics with nothing left to
catch it. Three of the callers are single-clinic operations and must say so instead:

| Caller | Scope | Because |
|---|---|---|
| `PdfGenerationJob` | `UseClinic(document's clinic)` | It renders **one** document |
| App→Google dispatcher's child scope | `UseClinic(appointment.ClinicId)` | It pushes **one** appointment; the clinic is already loaded to resolve the connection |
| `provision-clinic` (US-3) | `UseClinic(created.Id)` after the clinic exists | It provisions **one** clinic |
| The five outbox/scan jobs · the console report verbs · the startup scope | `UseSystemWide(reason)` | Genuinely enumerate every clinic |

⚠️ **Settle the semantics the interface leaves open**, since they are now load-bearing: `UseClinic(x)` then
`UseSystemWide(...)` in the same scope must **throw**, not silently widen — a widening call is how a
single-clinic path quietly becomes a cross-clinic one. `UseSystemWide` then `UseClinic(x)` (narrowing) is
likewise refused: one scope, one answer. A per-clinic *narrowing* inside an iterating job therefore requires a
child scope, which is the deliberately-deferred option below.

*Deferred, deliberately:* opening an `IServiceScopeFactory` child scope per clinic **inside** the iterating jobs
would keep the backstop on even during enumeration. Rejected for this plan — it restructures five jobs that share
one `DbContext` across their loop (`NotificationJob` commits per row), which is its own regression surface. Revisit
with Phase 2's RLS, where the same child-scope shape is needed for `SET LOCAL` anyway.

*Pitfalls*
- ⚠️ **`GET /api/auth/mode`, login, register and the Next-proxied pages run with no clinic** and must keep working.
  They touch `User`/`Clinic`, which carry **no** query filter by design — verify that stays true, or onboarding
  breaks with an empty result rather than an error.
- ⚠️ `AuditEntries` has a **nullable** `ClinicId` and no filter, deliberately. Leave it; `GetAuditEntriesQuery`
  filters explicitly.
- ⚠️ **The refusal must not regress a request whose principal has no resolvable clinic** — that is the whole
  reason C3′ moves the resolution to the DB. `CreateClinicCommand`/`JoinClinicCommand` and `user-status` are
  reached by a principal who is not yet in a clinic; they must land in `Unset` and still work, which they do only
  because `User`/`Clinic` are unfiltered. Assert it, don't assume it.
- ⚠️ **SignalR: HTTP middleware does not run per hub invocation.** `ClinicHub` is safe today because
  `OnConnectedAsync` reads only `User` (unfiltered) — but a future hub method touching a filtered entity would
  land in `Unset` and read nothing, silently. Pin it with a note at the hub and a test, or the next hub method is
  finding #R-1 again.

#### What the backstop does **not** cover — the clinical record

The filter reaches **21 clinic-owned aggregate roots**, and that is the whole of it. Seven tables — including
every one that holds PHI — **have no `ClinicId` column at all**, so no filter is possible and US-2 changes
nothing for them:

| Table | Holds | Reached by |
|---|---|---|
| `MedicalDocument` | ordonnances, certificats, lettres de liaison, BS1, arrêt de travail | `GET /api/medical-documents/{id}` |
| `DentalRecord` (+ `…Tooth`, `…Act`) | fiches de soins | `DentalRecordsController` |
| `PatientMedicalHistory` / `PatientFamilyHistory` | **allergies**, antécédents | their own controllers |
| `PatientFile` / `PatientFolder` | uploaded scans and their blobs | `PatientFilesController` |
| `ToothState` | the odontogram | `OdontogramController` |

Each is a child of `Patient` and is *designed* to be reached through it — but each is also **fetched by its own
id** by its own controller, where the parent is loaded *after* the child. So for the clinical record the
**per-handler DB check is the only layer**, before and after this story. `features/fix-patient-file-tenant-isolation`
exists because this exact class already leaked once.

**Accepted for this plan, stated rather than implied**, and R-5 is corrected accordingly: US-2's gain is real but
it is a gain over *administrative and financial* rows. The mitigation is test coverage, not a schema change —
`*TenantIsolationTests` is extended to cover all seven by-id reads, so the one layer they have is asserted per
table rather than assumed. (Two rejected alternatives, for the record: parent-navigation filters
`d => d.Patient.ClinicId == …` add a subquery to the hottest clinical reads; denormalising `ClinicId` onto seven
more aggregates is the write-path obligation `CLAUDE.md` rejects for `CreatedBy`/`ModifiedBy` — any writer that
forgets produces a row indistinguishable from a legitimate one.)

---

### US-3 — Provisioning and onboarding over the internet

**Goal:** an operator can create clinic #N and its first admin; staff accounts are created *by* that admin.

*Modify*
- `API/Controllers/AuthController.Setup` — the loopback gate (`LocalRequest.IsLoopback`) is correct for a LAN box
  and impossible over the internet. In `HostedMultiTenant`, replace it with a **console verb**
  `provision-clinic --name … --admin-email …` (new `API/Maintenance/ProvisionClinicCommand.cs`, wrapping
  `CreateClinicCommand` + `ResetUserPasswordCommand`), printing the one-time password. Keep `setup` loopback-gated
  in `SelfHostedLan`, unchanged.
- `API/Controllers/AuthController.Register` — in `HostedMultiTenant`, **404**. Self-registration by 6-char clinic
  code is a LAN-scale gate; on the internet the clinic code is known to everyone who ever worked at the practice.
  Staff onboarding becomes: admin creates the user in `/users` → hands over the temp password → forced change on
  first login (`must_change_password` already exists).
- `web/components/join-wizard.tsx` / `app/join/page.tsx` — hide the join path in this profile; the page must say
  *what to do instead*, not 404 (§ 0: never remove a capability silently).

*Pitfalls*
- `/users` has **no create-user action** today — verified: `UsersController` exposes exactly `GET`,
  `POST {id}/reset-password`, `PUT {id}/status`, `PUT {id}/role`. US-3 must add `CreateClinicUserCommand`
  (`AdminOnly`).
- ⚠️ **Say how a console verb is invoked here.** `Program.cs` intercepts verbs before the web host boots, so in
  the hosted topology that is
  `docker exec clinic-api-prod dotnet ClinicManagement.API.dll provision-clinic …` — env is inherited, so
  `AddInstallLayers()` resolves the same connection string as the running app. Applies equally to US-6's three
  report verbs.
- ⚠️ **`restore-backup`'s port guard silently passes in a container.** Its documented ordering guarantee is
  « refuse while the app's ports are listening » — but the API listens in a *different* container, so nothing
  stops `pg_restore --clean --if-exists` running against a live application. This is not covered by owed
  decision #2 (which is about *per-clinic* restore); it is a separate hosted-topology hazard and belongs beside it.

---

### US-4 — Per-clinic secrets, and a key ring that survives a redeploy

*Modify*
- `Infrastructure/Security/LocalDataProtection.cs` — in `HostedMultiTenant`, `DataProtection:KeyRingPath` is
  **required**; fail loud at startup if unset. Today it silently falls back to the framework default, which is
  per-instance and ephemeral — and the symptom is every clinic's reminder channel reporting
  « non configuré » after a deploy.
- `Domain/Entities/Clinic.cs` + config + migration — per-clinic TTN identity: `TtnUsername`, `TtnApiSecret`
  (protected), `TtnCertificateKey` (storage key), `TtnCertificatePassword` (protected).
- `Infrastructure/Services/XadesEInvoiceSigner.cs` + `TtnConfig` — take the clinic's cert; fall back to the
  per-install one only in `SelfHostedLan`.

*Pitfalls*
- ⚠️ Storing a PFX password protected by Data Protection makes e-invoicing depend on the key ring — so this story
  **must** land after the `KeyRingPath` requirement, or a rotated key silently breaks signing for every clinic.

---

### US-5 — Clinic-prefixed storage keys (new keys only)

*Modify*
- `Infrastructure/Storage/MinioFileStorage.cs` — default key becomes `clinics/{clinicId}/{guid}-{timestamp}`.
- Readers stay unchanged: they resolve whatever `StorageKey` the row holds, so **old flat keys keep working with no
  backfill**.

*Pitfalls*
- ⚠️ `DoctorCachetKey` is dereferenced by the **unauthenticated** `PdfGenerationJob`. Any key-format assumption
  there breaks cachet rendering *silently* — the renderer falls back to a plain signature line. Verify the job
  reads the stored key verbatim.
- Custom-path callers (`patient-files`, cachet upload) must be passed the clinic id; check every `IFileStorage`
  call site that supplies its own path.

---

### US-6 — Operations

*Create*
- **`deploy/docker-compose.hosted.yml` + `deploy/.env.hosted.example`** — the profile is un-runnable without them,
  and every one of the four keys below fails *quietly* if omitted. Reuses `postgres`, `minio`, `caddy`, `backup`
  and `pitr` from the Cloud compose; only `api` and `web` differ.

  | Key | Service | Omitting it does what |
  |---|---|---|
  | `Deployment__Profile: HostedMultiTenant` | api | Falls back to the `Auth:Mode` derivation → **`CloudBrowser`**, i.e. Auth0. No `Auth0__*` is set in this profile, so JWT bearer is never configured and every request is anonymous-or-401 |
  | `DataProtection__KeyRingPath` **+ a named volume** | api | US-4 makes the key **required** and fails startup loud — but a path with no volume behind it is worse than none: it works, then every redeploy loses the ring and each clinic's reminder channel reports « non configuré » |
  | `AUTH_MODE: local` | web | Mounts `CloudSessionProvider` → the app expects Auth0 and the `/bff/auth/*` routes stay gated off |
  | **`API_INTERNAL_URL: http://api:5000/api`** | web | The BFF handlers fetch **server-side** and default to `http://localhost:5000/api` — inside the web container that is *Next itself*. Login, refresh and change-password all 500 (`local-login/route.ts:11`, `change-password/route.ts:11`, `token/route.ts:8`) |
  | **`AUTH_COOKIE_SECURE: "true"`** | web | The handler derives the scheme from its own plain-HTTP hop behind Caddy and drops `Secure` from an **internet-facing** session cookie. The inverse of the `LEARNINGS.md` pitfall « `secure` cookie keyed on `NODE_ENV` », which is why that flag exists at all |

  Also: **no `Auth0__*` / `AUTH0_*` at all** (a half-configured Auth0 block is how a Cloud path stays reachable),
  and `FrontendUrl: https://${DOMAIN}` as today.

*Modify*
- **`deploy/Caddyfile` — add `handle /hub/*` → `api:5000`, ordered before the catch-all.** The hub is mapped at
  the host root, so today `/hub/clinic` proxies to the Next container and 404s, killing realtime for every hosted
  clinic with no error anywhere (see the Overview bullet). Caddy forwards the WebSocket upgrade automatically, so
  the change is three lines — but it must land in a story, and the manual verification list gains a
  **two-browser live-refresh check**, since nothing in either test suite can see this.
- The three console verbs → gate on `HasLocalDbTooling`/"has a direct DB connection", not on the profile (**M3**).
  `verify-schema` must run against the hosted DB — it is the only migration gate in the product.
- `API/Program.cs` — wrap `context.Database.Migrate()` in a **PostgreSQL advisory lock** (`pg_advisory_lock`);
  EF Core 8 takes none, so two instances starting together can race.
- Add `MapHealthChecks("/health")` (DB + storage), exempt from the rate limiter and from auth.
- **An outbox-depth read, because the hosted profile is otherwise blind.** `/hangfire` is loopback-only in
  **both** modes (`HangfireAuthorizationFilter` → `LocalRequest.IsLoopback`), and behind Caddy every request's
  `RemoteIpAddress` is the proxy container — so the dashboard is unreachable by design. Correct as security,
  total as blindness: **R-1's failure mode is a job that reads nothing and logs success**, the derived guard
  catches that at *build* time, and nothing catches it at *run* time. Add an `AdminOnly` aggregate of queue depth
  — pending / **blocked** / failed reminder rows, queued e-invoices, queued document emails. The per-clinic
  figures already exist (`GET /api/clinics/reminder-status`, and L3's « N rappels bloqués » counter), so this is
  an aggregate over data the product already computes, not a new subsystem.
- `RateLimiting` — **re-key `AnonymousAuthPolicy` on the submitted email (with the client address as a second
  dimension)**. ⚠️ Not "add a per-clinic partition": the global limiter *already* keys per authenticated user
  (`RateLimiting.cs:165`, `user:{subject}`), so the NAT-sharing problem does not exist for authenticated traffic —
  and a per-clinic partition is **unbuildable** for the anonymous policy, since no clinic is known before login.
  The real exposure is the one the existing comment reasons about **for a LAN** (`RateLimiting.cs:86–89`, « every
  login arrives from loopback, so keying on the peer would bucket the entire clinic »): on the internet every
  login from one practice arrives from **one public NAT address**, so one colleague's typos consume the bucket for
  all of them. The email is the only tenant-ish key that exists pre-auth; keeping the address as a second
  dimension is what still bounds credential-stuffing across many emails from one source.
- `SecurityHeadersMiddleware` — promote CSP from `Report-Only` to enforcing (behind a config flag; verify against
  Next's own policy first, since two headers intersect).

---

## Phase 2 (separate plan)

**Postgres RLS**, with all three traps handled explicitly:
1. `ALTER TABLE … FORCE ROW LEVEL SECURITY` — the app connects as `clinic_user`, which owns the tables, and **a
   table owner bypasses RLS**. Without this, RLS looks installed and enforces nothing.
2. `SET LOCAL` **inside a transaction** — a bare `SET` leaks the tenant to the next request on that pooled
   connection, in the "wrong clinic's data" direction.
3. A bypass path for migrations, the 4 jobs and the console verbs — a second role, or `SystemWide` setting the
   bypass flag.

---

## Owed decisions (blocking a launch, not this plan)

1. **Offline behaviour.** Today an unreachable server means no agenda and no patient file. Local mode's probe is
   *server*-side (`/api/connectivity`, 404 in Cloud) so it is inverted for this topology. Options: online-only and
   say so in the client · read-only cache · full sync (**not** recommended — gapless per-year invoice numbering
   cannot be reconciled after a partition without gaps or duplicates, and both are legally significant).
2. **Per-clinic backup/restore.** `restore-backup` does `pg_restore --clean` on the **whole database** and bumps
   every user's `TokenVersion` — restoring one clinic's mistake rolls back all of them.
3. **Client auto-update + API version compatibility.** The Inno Setup client installer has no updater and the
   shell has no version check; a cloud API that moves under N pinned clients breaks them silently.
4. **HuggingFace PHI.** One shared key, no per-clinic consent, no audit of what was sent.
5. **Compliance.** INPDP declaration (loi 2004-63), a DPA per clinic, residency, retention/erasure, audit-ledger
   retention.

---

## Testing strategy

There is **no database in the test project** and **no test runner in `web/`**, so:

- **Unit (xUnit + Moq):** `DeploymentProfileTests` (capability matrix + back-compat), `TenantScopeFilterTests`
  (three states), and the **two derived guards** (`DeploymentProfileCoverageTests`,
  `SystemWideCallerCoverageTests`) — both must fail on a *new* call site, never on a list someone forgot to edit.
- **Extend** `*TenantIsolationTests` twice: an `Unset`-scope case per filtered aggregate, **and** a cross-clinic
  by-id case for each of the **seven unfilterable clinical tables** (`MedicalDocument`, `DentalRecord`,
  `PatientMedicalHistory`, `PatientFamilyHistory`, `PatientFile`, `PatientFolder`, `ToothState`) — for those the
  handler check is the only layer, so it is the only thing a test can hold.
- **Schema:** `verify-schema` before and after the US-4 migration, diffed. It is the only gate.
- **Money:** `reconcile-money` before and after, diffed — US-4 touches nothing financial, so the diff must be empty.
- ⚠️ **Smart App Control blocks the test runner intermittently** on this machine (`0x800711C7`). Build to a scratch
  `-p:BaseOutputPath` and run via `dotnet vstest`; a red run is not evidence until `bin/`+`obj/` are cleared and
  `dotnet build-server shutdown` has run.
- **Manual, operator-verified (not CI-runnable):** point a real desktop client at a hosted domain; log in; confirm
  two clinics cannot see each other's patients; confirm reminders still dispatch (that is the `SystemWide` path);
  **and open the same clinic in two browsers and edit an appointment in one** — the second must refresh by itself,
  which is the only check that sees the `/hub/*` route (R-6: SignalR failure is a bare `catch {}`, so a broken
  hub and a working one look identical on one screen).

---

## Risks

| # | Risk | Mitigation |
|---|---|---|
| **R-1** | US-2 is the whole point and its failure mode is **silence** — a job with no scope reads nothing and logs success | `SystemWideCallerCoverageTests`, derived from the stated criterion (**not** from "is it a job?", which produced a wrong list in both directions) + the outbox-depth read in US-6 + the manual reminder-dispatch check |
| **R-2** | US-1 touches ~35 sites; a single mis-mapped capability changes an existing install's behaviour | Back-compat derivation from `Auth:Mode` + byte-identical capability matrix for the two existing kinds, asserted against today's `IsLocalMode` truth table. ⚠️ The promise must extend to **US-2**, not just US-1: making the filter refuse on `Unset` is a behaviour change for *every* profile, which is why C3′ resolves the scope from the DB |
| **R-3** | No `spec.md`, so no acceptance criteria | Write one if scope grows past US-2 |
| **R-4** | The owed decisions (offline, per-clinic restore) may change the architecture | Neither touches the tenancy seam; both are additive to it |
| **R-5** | RLS deferral leaves leak-by-omission possible | US-2 restores the second layer that is inert today — **for the 21 clinic-owned roots**. ⚠️ Corrected: this is *not* "the majority of the protection" for clinical data. The seven PHI tables carry no `ClinicId` and gain nothing (see US-2's own subsection); there the per-handler check remains the only layer, mitigated by extending `*TenantIsolationTests` to all seven by-id reads |
| **R-6** | The hosted profile's failure modes are overwhelmingly **silent**: a missing `/hub/*` route kills realtime behind a bare `catch {}`, a missing `API_INTERNAL_URL` 500s only on login, a key ring with no volume works until the first redeploy | Every one is a *deploy asset* rather than code, so none is reachable by any test — hence the enumerated key table in US-6 and a two-browser live-refresh check in the manual list |
