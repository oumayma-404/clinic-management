# Implementation Plan: Public demo cabinet

**Status:** APPROVED (2026-11-03)
**Challenged:** Yes (2026-11-03 — seed moved into the API host, half-built-cabinet guard, purge transaction, login side doors, operator warning)
**Created:** 2026-11-03
**Spec:** [spec.md](./spec.md) · facts: [exploration.md](./exploration.md) · brief: [handoff.md](./handoff.md)
**Branch:** `feature/demo-cabinet` (worktree `.claude/worktrees/demo-cabinet`, off `origin/main` @ `66ecc657`)

## Overview

A fourth `DeploymentKind`, **`PublicDemo`**, runs the production images at `demo.apexa.tn` against its own database,
role, MinIO bucket, signing key and key ring. Two new capabilities carry everything the demo does differently:

| Capability | True only for `PublicDemo` | What reads it |
|---|---|---|
| `RefusesWrites` | the read-only gate, the PDF watermark, the quiet frontend paths | `IDemoPolicy` (Application), `DemoReadOnlyMiddleware`, `PdfGenerationService`, `GET /api/auth/mode` → `isDemo` |
| `IssuesDemoSessions` | the anonymous entry, the 2 h session cap, the nightly rebuild job | `AuthController.DemoSession`, `RefreshTokenCommand`, `RebuildDemoCabinetJob` registration |

Every other capability gets an explicit `PublicDemo` answer in `DeploymentProfile.For()` (§ US-1 table). Nothing
reads `Kind == PublicDemo` directly — the two kind comparisons in `LocalDataProtection` become a capability
(`ProtectsKeyRingWithCertificate`) so the demo gets the same key-ring protection as hosted production.

**Key design decisions**

| # | Decision | Why | Rejected |
|---|---|---|---|
| D1 | Separate deployment, same images | no path from the internet to real patients; demo never lags the product | flagged cabinet in prod (handoff § 3 B) |
| D2 | Read-only = **one HTTP gate**, per action, before `IdempotencyMiddleware` | in-process jobs and the seed still write; GET exports are refused too; nothing writes an idempotency claim | DB-level read-only role (GET downloads write an audit row and fail closed — exploration § 2) |
| D3 | Refusal UX lives in **two primitives**: `FormErrorBanner` (inside dialogs) and `showErrorToast` (outside), both keyed on `code === "demo_read_only"` | ~52 dialog call sites + every toast get it without being touched; a dialog's own message slot is announced and keyboard-reachable (Radix traps focus) | a global listener toast (invisible inside a modal) |
| D4 | Session cap from `SessionFamily.CreatedAt + 2 h`, enforced in refresh when `IssuesDemoSessions` | no migration; every family on that deployment is a demo one | new column |
| D5 | Seed = **in-process** `DemoCabinetBuilder` (Application) sending the real MediatR commands, acting as each team member through a synthetic `HttpContext.User` | every invariant (numbering, money, chart, journal chain) holds; a model change breaks the seed at compile time | HTTP seeding (the gate refuses it), raw SQL |
| D6 | **Creation stamps read the rebuild day** (owner, 2026-11-03) | no clock abstraction exists (387 `DateTime.UtcNow` in Domain + Application) | app clock refactor (~170 files), libfaketime, SQL back-dating (breaks the audit HMAC chain) |
| D7 | Nightly rebuild **builds a new cabinet beside the old one**; « current » = newest clinic whose admin is active; admin activated only after the build and money check pass; cabinets older than the previous one purged with `ClinicPurge` | atomic from a visitor's view with no shared transaction (audit rows commit on their own context); nobody inside is cut off; failure leaves yesterday's cabinet current | one big transaction (audit interceptor writes outside it), two databases + restart |
| D8 | Demo frontend flag = `isDemo` on `GET /api/auth/mode` + a `DemoProvider` (shape of `SubscriptionProvider`) | one existing anonymous read; works on `/demo` before sign-in | runtime env in middleware (inlined at build) |
| D9 | No install, noindex: done **at the demo's Caddy block** (`/manifest.webmanifest` → 404, `X-Robots-Tag: noindex`) | manifest and layout metadata are build-time; same image must serve both hosts | dynamic manifest |
| D10 | In-app tab titles stay « … · APEXA »; only `/demo` is titled « APEXA Démo » | layout metadata is static; the band carries the demo signal on every screen | per-host metadata (makes `/login` dynamic) |

## Files to Modify/Create

### Files to Create
| File | Purpose |
|---|---|
| `api/ClinicManagement.Application/Common/Interfaces/IDemoPolicy.cs` | `RefusesWrites`, `IssuesDemoSessions`, `SessionLifetime` for Application/API |
| `api/ClinicManagement.Infrastructure/Services/DemoPolicy.cs` | implementation from `DeploymentProfile` |
| `api/ClinicManagement.Application/Common/Authorization/AllowedInDemoAttribute.cs` | non-GET actions the demo lets through (mandatory reason) |
| `api/ClinicManagement.Application/Common/Authorization/RefusedInDemoAttribute.cs` | GET actions the demo refuses (exports, archive, OAuth callback) |
| `api/ClinicManagement.Application/Features/Demo/DemoRefusals.cs` | the French sentences + codes `demo_read_only`, `demo_rate_limited`, `demo_rebuilding` |
| `api/ClinicManagement.API/Middleware/DemoReadOnlyMiddleware.cs` | the gate |
| `api/ClinicManagement.Application/Features/Auth/Commands/StartDemoSessionCommand.cs` | mints an untrusted family for the current demo admin; never writes `User` |
| `api/ClinicManagement.Application/Features/Demo/DemoCabinetBuilder.cs` (+ `DemoScenario*.cs` parts) | the scenario, through MediatR |
| `api/ClinicManagement.Application/Features/Demo/DemoCabinetLocator.cs` | « current cabinet » = newest clinic with an active admin |
| `api/ClinicManagement.Infrastructure/Services/DemoSeedContext.cs` | scope that sets `HttpContext.User` + `ITenantScope.UseClinic` + `IAuditActorProvider.AuthenticatedAs` per acting member |
| `api/ClinicManagement.API/BackgroundJobs/DemoCabinetRebuilder.cs` | the one rebuild routine: provision → deactivate admin → build → check → activate → purge; run by the job and by startup |
| `api/ClinicManagement.API/BackgroundJobs/RebuildDemoCabinetJob.cs` | Hangfire wrapper (nightly) around the rebuilder |
| `api/ClinicManagement.Infrastructure/Services/DocumentWatermark.cs` | `IDocumentWatermark` seam read by `DocumentPage.Compose` |
| `api/ClinicManagement.UnitTests/Api/DemoReadOnlyMiddlewareTests.cs` | gate behaviour + pipeline order |
| `api/ClinicManagement.UnitTests/Api/DemoExemptionCoverageTests.cs` | every action classified, both directions |
| `api/ClinicManagement.UnitTests/Features/Auth/StartDemoSessionCommandTests.cs` | mint rules |
| `api/ClinicManagement.UnitTests/Features/Demo/DemoCabinetBuilderTests.cs` | scenario shape against mocks (dates on working days, every area present) |
| `web/app/demo/page.tsx` | entry / expired / rebuilding screen |
| `web/app/bff/auth/demo-session/route.ts` | calls the API, writes the session cookies like `local-login` |
| `web/lib/demo/demo-context.tsx` | `DemoProvider`, `useDemo()` |
| `web/lib/demo/refuse-in-demo.ts` | `refuseUploadInDemo(isDemo)` — the pick-time guard |
| `web/components/demo/demo-band.tsx` | the permanent band |
| `deploy/docker-compose.demo.yml` | `api-demo`, `web-demo` overlay |
| `deploy/demo/README.md` (or a § in `deploy/README.md`) | one-time setup: role, DB, MinIO user/policy/bucket, secrets (uid 1654), DNS |
| `features/demo-cabinet/notes.md` | what shipped (at the end) |

### Files to Modify
| File | Changes |
|---|---|
| `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs` | `PublicDemo` kind + column; `RefusesWrites`, `IssuesDemoSessions`, `ProtectsKeyRingWithCertificate`; `PermitsOsPush` arm (else boot throws) |
| `api/ClinicManagement.Infrastructure/Security/LocalDataProtection.cs` | `:55`, `:142` read the new capability instead of `Kind == HostedMultiTenant` |
| `api/ClinicManagement.Infrastructure/Services/OutboundEndpointPolicy.cs` | explicit `PublicDemo` arm |
| `api/ClinicManagement.Infrastructure/Extensions.cs` | register `IDemoPolicy`, `IDocumentWatermark` |
| `api/ClinicManagement.Infrastructure/Auth/LocalAuthService.cs` | `GenerateRefreshToken(..., DateTime? notAfterUtc = null)` clamps expiry |
| `api/ClinicManagement.Application/Features/Auth/Commands/RefreshTokenCommand.cs` | refuse past `CreatedAt + SessionLifetime` when `IssuesDemoSessions`; clamp rotation |
| `api/ClinicManagement.API/Controllers/AuthController.cs` | `POST demo-session` (`[AllowAnonymous]`, `[EnableRateLimiting(AnonymousAuthPolicy)]`, `[OnlineOnly]`, `[AllowedInDemo]`, 404 unless capability); `mode` gains `isDemo`, `demoSignupUrl`, `demoBuiltOn`; `[AllowedInDemo]` on refresh + logout |
| `api/ClinicManagement.API/Controllers/MedicalDocumentsController.cs` | `[AllowedInDemo]` on `generate-pdf-download`, `fiche-ordonnance-preview`; PDF rate-limit policy on its PDF actions |
| `api/ClinicManagement.API/Controllers/{Invoices,Billing,TreatmentPlans,Clinics}Controller.cs` | PDF rate-limit policy on PDF actions |
| `api/ClinicManagement.API/Controllers/{Patients,Appointments,Backup,GoogleCalendar}Controller.cs` | `[RefusedInDemo]` on exports, dossier, archive, OAuth callback |
| `api/ClinicManagement.API/Controllers/CspReportController.cs` | `[AllowedInDemo]` |
| `api/ClinicManagement.API/Startup/RateLimiting.cs` | `DemoPdfPolicy` keyed on `family_id`, no limiter outside the demo |
| `api/ClinicManagement.API/Program.cs` | gate after `LocalAuthEnforcementMiddleware` (≈1092), before `IdempotencyMiddleware`; `rebuild-demo-cabinet` recurring job gated on `IssuesDemoSessions`; on a demo boot with no current cabinet (or `Demo:RebuildOnStart=true`) enqueue one rebuild |
| `api/ClinicManagement.API/Hubs/ClinicHub.cs` | no presence broadcast when `RefusesWrites` |
| `api/ClinicManagement.Application/Features/Auth/Queries/GetMySessionsQuery.cs` | in the demo, only the caller's own family |
| `api/ClinicManagement.Infrastructure/Services/DocumentPage.cs` + `PdfGenerationService.cs` | diagonal « SPÉCIMEN — DÉMONSTRATION » foreground + footer line when the watermark is on |
| `api/ClinicManagement.UnitTests/Infrastructure/Deployment/DeploymentProfileTests.cs` | 4-tuple matrix, new rows, new InlineData |
| `api/ClinicManagement.UnitTests/Api/{ControllerAuthorizationCoverageTests,RelayOnlineOnlyCoverageTests,SubscriptionExemptionCoverageTests,SelfRegistrationGateTests}.cs` | classify the new endpoint / pin new `mode` fields |
| `web/lib/api/auth.ts` | `AuthModeDto.isDemo?`, `demoSignupUrl?`, `demoBuiltOn?`; `authApi.startDemo()` not needed (BFF) |
| `web/lib/api/client.ts` | `ApiErrorCode.DemoReadOnly`; keep it off the token-refresh 401/403 path |
| `web/components/ui/form-error-banner.tsx` | neutral « notice » tone + CTA link when the error is a demo refusal (takes the `ApiError` or a `tone` prop) |
| `web/lib/hooks/use-conflict.ts`, `web/lib/errors.ts` | carry the `code` through `capture`; `showErrorToast` → demo toast (`id: "demo-read-only"`, CTA action) |
| `web/components/app-shell.tsx` | mount `<DemoBand />` above `DashboardHeader` (shell column, like the other banners) |
| `web/app/layout.tsx` | mount `DemoProvider` |
| `web/lib/auth/public-routes.ts`, `web/lib/nav.ts` | `/demo` public + chrome-less |
| `web/app/login/page.tsx`, `mot-de-passe-oublie`, `signup`, `join` pages | on `isDemo` → `router.replace('/demo')` |
| `web/lib/auth/session.tsx` | session end / idle on demo → `/demo?expired=1` instead of `/login` |
| `web/components/post-visit-review-popup.tsx` | return null when `isDemo` |
| `web/components/security/step-up-dialog.tsx` | demo notice instead of the code field (one choke point for every code-gated action) |
| `web/components/patient-files-manager.tsx` (default folders), `web/lib/hooks/use-staff-chat.ts` (mark read), `web/lib/push/web-push.ts` (register), `web/lib/hooks/use-notifications.ts` (mark read) | skip the call when `isDemo` (no request, no message) |
| the 10 `<input type="file">` sites (exploration § 5 list) | call `refuseUploadInDemo` before reading the file |
| `web/scripts/check-responsive.mjs` | `demo-upload-guard`: every `type="file"` file references `refuseUploadInDemo`; `demo-refusal-one-owner`: only the two primitives compare `demo_read_only` |
| `deploy/Caddyfile` | `{$DEMO_DOMAIN}` block (copy of the app block → `api-demo`/`web-demo`, `X-Robots-Tag: noindex`, manifest 404) |
| `deploy/docker-compose.prod.yml` | `DEMO_DOMAIN` into caddy's env |
| `.github/workflows/deploy-hosted.yml` | when `DEMO_DOMAIN` is set: include the demo overlay, pull `api-demo web-demo`, health + `verify-schema` on the demo |
| `site/src/partials/nav.html`, `site/src/pages/index.html`, `site/src/pages/odontogramme.html`, `site/src/layout.html` | « Essayer la démo » + Umami `essayer-demo` (US-8, owner's OK) |
| `CLAUDE.md` (root map), `api/*/CLAUDE.md`, `web/CLAUDE.md`, `deploy/README.md`, `features/multi-tenant-cloud/notes.md` | the fourth kind, the two capabilities, the traps |

## Implementation Stories

Order: US-1 → US-2 → US-3 → US-4 → US-5 → US-6 → US-7 → US-8. Each is shippable to the local isolated stack
(`:5099` / `:3099`, `Deployment__Profile=PublicDemo`, throwaway DB `clinic_demo_local`) — never the shared stack's
database.

### US-1: A visitor enters a demo cabinet with one click

**Goal:** on a `PublicDemo` deployment, `/` with no session shows the entry screen; « Entrer dans la démo » lands on
the dashboard as Dr Monica Geller, no password or code; the session ends after 2 h or 30 min idle, back on the entry
screen. Covers spec US-1, FR-1, FR-3, AC-7.2.
**Blocked by:** None
**Layers:** Infrastructure (profile), Application (command), API (endpoint), Web (BFF, page, provider)

**Steps:**
1. `DeploymentProfile.cs`: add `PublicDemo` + its `For()` column, one-line reason per capability:

   | Capability | PublicDemo | Capability | PublicDemo |
   |---|---|---|---|
   | UsesLocalAccounts | true | ExposesMetaOnboarding | false |
   | FailClosedAuthz | true | ServesPlatformConsole | false |
   | EnforcesTokenState | true | RequiresSubscription | false |
   | UsesDiskStorage | false (MinIO, own bucket) | BacksUpItsOwnData | false |
   | SelfHostsFrontDoor | false | SellsVendorMessaging | false |
   | SelfSignsCertificate | false | RequiresAdminSecondFactor | false |
   | RunsAsWindowsService | false | PublishesChangeFeed / MirrorsCloudClinic | false / false |
   | DefersMigrations | false (else `pg_dump` before migrate) | RunsClinicJobs | **true** — see R-3 |
   | RunsStartupBackfills | true | DispatchesOutboxes | false |
   | ExposesTrustEndpoints / HasLocalDbTooling | false / false | RunsCutJobs | false |
   | AllowsSelfRegistration / AllowsPublicClinicSignup / AllowsPasswordResetByEmail | false | **RefusesWrites / IssuesDemoSessions** | **true / true** |
   | **ProtectsKeyRingWithCertificate** | true (HostedMultiTenant true, others false) | `PermitsOsPush(*)` | false |
2. `LocalDataProtection.cs:55,142` and `OutboundEndpointPolicy.cs` read capabilities / an explicit arm.
3. `IDemoPolicy` + `DemoPolicy` (`SessionLifetime = 2 h`), registered in `Extensions.cs`.
4. `LocalAuthService.GenerateRefreshToken(..., notAfterUtc)`; `RefreshTokenCommand` refuses past
   `family.CreatedAt + SessionLifetime` (ends the family `EndedReason = "demo-expired"`) and clamps each rotation.
5. `StartDemoSessionCommand`: `DemoCabinetLocator` → current demo admin (none → `demo_rebuilding`); copy the
   `TradeRelayAssertionCommand` mint (untrusted family, no `RecordSuccessfulLogin`, no `User` update), plus
   `AuthenticatedAs`; returns `LoginResultDto`.
6. `AuthController`: `POST demo-session` (attributes in the table above; 404 before the mediator when
   `!IssuesDemoSessions`, like `RelaySession`); `mode` gains `isDemo`, `demoSignupUrl` (config
   `Demo:SignupUrl`, default `https://apexa.tn/#telecharger`), `demoBuiltOn`.
7. `DemoCabinetRebuilder` **inside the API host** (not a console verb: a verb's container has no
   `IRealtimeNotifier` and no `IHttpContextAccessor` — both registered in `Program.cs:558,569` — so MediatR's
   `RealtimeBroadcastBehavior` and `ClinicContext` cannot resolve there). First version: `UseSystemWide` for the
   locator; `LocalClinicProvisioning` (Dr Geller, `MustChangePassword = false`, doctor with cachet) → **immediately
   `Deactivate()` the admin** → `UseClinic(id)` + `DemoSeedContext` → team (Dr Chandler Bing, Rachel Green),
   letterhead → `Activate()`. Runs on a demo boot with no current cabinet, or when `Demo:RebuildOnStart=true`
   (the operator's « rebuild now »). US-4 grows the build; US-6 adds the schedule, check and purge.
   `DemoCabinetLocator` = newest clinic whose admin is active **and** that has at least one patient (covers the
   milliseconds between provisioning and deactivation). Job declares its tenant scopes so
   `SystemWideCallerCoverageTests` stays green.
8. Web: `AuthModeDto` fields; `DemoProvider` in `layout.tsx`; `/demo` page (entry / `?expired=1` / rebuilding
   variants, « Créer mon cabinet » link; a visitor already signed in is sent to `/` — spec AC-1.6; its own
   metadata title « APEXA Démo » + description for link previews — spec EC-9); `bff/auth/demo-session/route.ts` (forwards `x-forwarded-for`, writes cookies
   with `writeSessionCookies`, passes 429/503 through); `/demo` in `PUBLIC_ROUTES` + `HIDDEN_PATHS`;
   login / reset / signup / join pages replace to `/demo` on `isDemo`; `session.tsx` ends to `/demo?expired=1` on
   the demo.
9. Tests: `DeploymentProfileTests` (4-tuple + new rows), `StartDemoSessionCommandTests` (404 when off; always the
   current demo admin; new family per call; no `User` write; untrusted; `demo_rebuilding` when no cabinet),
   refresh-cap tests, coverage guards (`ControllerAuthorizationCoverageTests`, `RelayOnlineOnlyCoverageTests`,
   `SubscriptionExemptionCoverageTests`, `SelfRegistrationGateTests` wire names).

**Validation:**
- [ ] Full `dotnet test -c Release` to a temp `BaseOutputPath` — green, unfiltered.
- [ ] Isolated stack with `PublicDemo`: entry → dashboard, no code; 2nd browser profile enters independently;
      `/login` → `/demo`; a demo refresh token replayed against a `HostedMultiTenant` API with another key → 401.
- [ ] Boot with `HostedMultiTenant` unchanged (`POST demo-session` → 404, `mode.isDemo` absent/false).

### US-2: A save is refused kindly, and the visitor always knows it is a demo

**Goal:** every write is refused with the demo sentence, inside the dialog that made it (neutral, with « Créer mon
cabinet ») or as one toast outside a dialog; typed input stays; a permanent band sits on every screen. Covers spec
US-3, US-4, FR-2, FR-5.
**Blocked by:** US-1
**Layers:** API (gate, attributes), Web (primitives, band)

**Steps:**
1. `AllowedInDemoAttribute`, `RefusedInDemoAttribute` (mandatory reasons), `DemoRefusals`.
2. `DemoReadOnlyMiddleware`: when `RefusesWrites` and path under `/api`: refuse non-GET/HEAD/OPTIONS unless the
   endpoint carries `[AllowedInDemo]`; refuse GET when it carries `[RefusedInDemo]`; unroutable → pass to 404. Writes
   403 `{ error, code: "demo_read_only" }`.
3. `Program.cs`: insert after `LocalAuthEnforcementMiddleware`, before `IdempotencyMiddleware`.
4. Classify: `[AllowedInDemo]` on `Auth.DemoSession`, `Auth.Refresh`, `Auth.Logout`, `CspReport.Receive`
   (US-3 adds the two PDF POSTs); `[RefusedInDemo]` on `Patients.Export`, `Patients.ExportDossier`,
   `Appointments.Export`, `Backup.Archive`, `GoogleCalendar.Callback`.
5. `DemoExemptionCoverageTests`: reviewed sets for both attributes, both directions; plus « every GET whose handler
   writes a `ListExportLedger`/`ArchiveAccessLedger`/dossier ledger row is `[RefusedInDemo]` » as a source scan.
6. `DemoReadOnlyMiddlewareTests` (copy `SubscriptionGateMiddlewareTests` shape): GET passes; POST/PUT/PATCH/DELETE
   refused with body; allowed passes; refused GET refused; inert when capability off; pipeline order by `IndexOf`.
7. Web: `ApiErrorCode.DemoReadOnly`; `useConflict.capture` keeps the `ApiError`; `FormErrorBanner` renders the
   neutral notice + link for a demo refusal; `showErrorToast` → toast with id `demo-read-only` + action
   « Créer mon cabinet » (new tab). `check:responsive` `demo-refusal-one-owner`.
8. `DemoBand` in `app-shell.tsx` (shell column, `shrink-0`, `print:hidden`, short text `< sm`, CTA ≥ 44 px on
   coarse, `pt-[env(safe-area-inset-top)]` when it is the top-most element on phones, `role="status"` announced
   once).

**Validation:**
- [ ] Full API suite green; `check:responsive` + `tsc --noEmit` + `npm run build` green.
- [ ] Browser (isolated stack): new patient, edit appointment, fiche save, payment, settings save → each refused in
      its dialog, input kept; a refusal outside a dialog → one toast; band at 320/390/820/1180/1440 and 730 px tall
      never covers a control.
- [ ] SQL read: `IdempotencyRecords` has no row from the walk.

### US-3: Nothing in the demo misbehaves around the read-only rule

**Goal:** no screen nags, prompts for a code, uploads a file only to refuse it, leaks other visitors, or reloads
when someone arrives. Covers spec FR-4, AC-3.3c/d, EC-3, EC-6.
**Blocked by:** US-2
**Layers:** API (sessions list, hub), Web (surfaces)

**Steps:**
1. `refuseUploadInDemo` called first in each of the 10 file-pick/drop handlers; `check:responsive`
   `demo-upload-guard`.
2. Quiet paths skip the request when `isDemo`: default folders (`patient-files-manager.tsx:225-240`), staff-chat
   mark-read, web-push register, notification mark-read. Notification dismiss / clear-all keep the request so the
   refusal toast explains the row coming back.
3. `step-up-dialog.tsx`: on `isDemo`, the demo notice instead of the code field (covers exports, « Afficher
   l'argent », TOTP reset, recovery codes, PC de secours, archive restore).
4. `post-visit-review-popup.tsx`: null on `isDemo`.
4b. Side doors that bypass `client.ts`: `change-password-form.tsx` (raw fetch; its BFF flattens every status to 400
    and drops `code`) shows the demo notice on submit without sending; Sécurité's « Activer maintenant » /
    « Remplacer » links (they go through `/login?enrol=1|replace=1`, which the demo replaces with `/demo`) show the
    demo notice instead of navigating.
5. `GetMySessionsQuery`: demo → caller's family only; « Mes appareils » hides « Déconnecter » on `isDemo`.
6. `ClinicHub`: no `StaffChatChanged` presence broadcast when `RefusesWrites`.
7. Optimistic controls: verify each rolls back on error (agenda drag, tooth mark, status quick action, stock ±,
   dashboard toggle) — fix any that do not; they then show the one deduped toast.

**Validation:**
- [ ] Browser: every row of spec FR-4 in one planned `/test-in-browser` pass (desktop + 390 px + coarse emulation).
- [ ] Network log: no request on opening a patient's Fichiers, a chat thread, or at sign-in.

### US-4: The cabinet is full of believable work

**Goal:** the rebuilder builds « Cabinet Dentaire Démo » with the spec FR-6 scenario through the real commands, and
`reconcile-money` + `verify-schema` exit 0 on it. Covers spec US-2, FR-6.
**Blocked by:** US-1 (US-2/3 not required, but walk after them)
**Layers:** Application (builder), Infrastructure (seed context), API (rebuilder)

**Steps:**
1. `DemoSeedContext`: per acting member, a synthetic `HttpContext.User` (`sub`, `clinic_id`, `role`), `UseClinic`,
   `AuthenticatedAs` — so `IClinicContext`, tenant scope and journal author all agree.
2. `DemoCabinetBuilder` in parts, each a method sending real commands, dates from `ClinicClock.ClinicToday()`:
   team & settings (hours, lunch break, letterhead + cachet from `C:\dev\clinic-film\public\tuto\assets\` copied into
   `api/.../Features/Demo/Assets/`) → patients (~40 Friends names, Ross Geller as in the videos, a « denture mixte »
   child, no real-looking phones) → agenda (−14…+14 working days, both dentists, congé for Dr Bing, a recurring
   series, a waiting-list entry) → fiches on past visits with acts (bridge with piliers + pontique, implant,
   caries/obturations, absent teeth, « à traiter ») → treatments at every state (followed draft, accepted with
   échéancier, multi-séance couronne in progress, implant waiting osseointegration, completed, stopped) → money
   (cash/cheque/card on fiches and échéances, one cheque to deposit, notes d'honoraires, monthly expenses, one
   patient owing, one avoir) → documents (ordonnance médicaments + demande d'examens, certificat, arrêt de travail)
   → files (`site/src/img/pano.jpg` + 3 photos, embedded assets) → stock (one low, one lot expiring), two
   fournisseurs, lab orders (one late) → worklists (1–2 past visits left without a fiche, a « suite à planifier »).
3. Past visits are created **Completed with their fiche** so `AppointmentProgressJob` does not flood « À clôturer »;
   today: ~3 visits per dentist.
4. `DemoCabinetRebuilder` runs the whole builder (restart with `Demo:RebuildOnStart=true` to rebuild locally).
5. `DemoCabinetBuilderTests` against mocked `ISender`: every area sends at least one command; every visit date is a
   working day inside hours; no `DateTime.UtcNow`/`Today` in the builder (source scan).

**Validation:**
- [ ] Local: demo API boots on `clinic_demo_local` and builds; then `verify-schema` and `reconcile-money` → exit 0.
- [ ] Browser: every sidebar screen has content (spec AC-2.1), figures agree across solde / À clôturer / caisse /
      dashboard (AC-2.5).
- [ ] Re-run on a simulated Sunday and 1 January (`--today` override of the scenario anchor only) — still coherent.

### US-5: Documents open, print, and say « spécimen »

**Goal:** every PDF opens / downloads / prints with the letterhead and a diagonal « SPÉCIMEN — DÉMONSTRATION » and a
footer line; PDF generation is limited per session. Covers spec US-5.
**Blocked by:** US-4 (documents to look at)
**Layers:** Infrastructure (renderer), API (attributes, rate limit)

**Steps:**
1. `IDocumentWatermark` (on when `RefusesWrites`) injected into `PdfGenerationService` as an optional parameter;
   `DocumentPage.Compose` draws `page.Foreground()` + the footer line — the one choke point for all five renderers.
2. `[AllowedInDemo]` on `MedicalDocuments.GeneratePdfForDownload` and `PreviewFicheOrdonnance`.
3. `RateLimiting.DemoPdfPolicy` keyed on `family_id` (no limiter when not demo) on the 9 PDF actions.
4. Tests: watermark present only when on (render a document, assert text in the PDF); existing render tests keep
   passing; coverage test lists the two new allowed POSTs.

**Validation:**
- [ ] Browser: ordonnance, certificat, note d'honoraires, devis, reçu, avoir — preview + download + print show the
      mark; aperçu inside a fiche works; on `HostedMultiTenant` no mark.

### US-6: The cabinet renews itself every night without interrupting anyone

**Goal:** at 03:00 Tunis a new cabinet is built beside the current one, checked, then made current; yesterday's is
removed once no session can use it; a failure keeps the current one and is visible. Covers spec FR-7, AC-7.5/7.6,
EC-2, EC-7.
**Blocked by:** US-4
**Layers:** API (job), Application (locator, purge use)

**Steps:**
1. `RebuildDemoCabinetJob` (Hangfire `rebuild-demo-cabinet`, `0 2 * * *` UTC = 03:00 Tunis, gated on
   `IssuesDemoSessions`, `DisableConcurrentExecution`) calls `DemoCabinetRebuilder`: provision → deactivate admin →
   builder → money check (`MoneyReconciliationService.RunAsync` over the demo database — only demo cabinets live
   there) → activate the admin → purge every demo cabinet older than the previous one.
2. Purge exactly as `DeleteClinicFromConsoleCommand` does it (`:130-176`): `BeginTransactionAsync` →
   `IClinicPurge.PurgeAsync` → commit → `IFileStorage.DeleteByClinicAsync` **after** the commit (an object store has
   no rollback). One cabinet per transaction.
3. On any failure: purge the half-built clinic (same path), log an error naming the step, leave the current one.
4. `demoBuiltOn` (from the current cabinet's `CreatedAt`) in `GET /api/auth/mode` — the operator's check.
5. Tests: locator picks newest active-with-patients; rebuilder ordering (activation only after the check; a failed
   check purges the new cabinet and leaves the current one); purge keeps current + previous; transaction + blob
   sweep order.

**Validation:**
- [ ] Local: trigger the job twice with a session open in between — the open session keeps working; a new entry
      lands in the new cabinet; the third run purges the first.
- [ ] Force a failing check (test seam) — current cabinet unchanged, error logged.

### US-7: demo.apexa.tn runs beside production (prepared locally, owner applies)

**Goal:** a ready-to-apply overlay, Caddy block, workflow step and runbook, rehearsed on a throwaway local compose
project; **nothing on the VPS or DNS without the owner's OK for each step**. Covers spec AC-7.1–7.4, AC-7.7, EC-9.
**Blocked by:** US-1…US-6
**Layers:** Deploy, CI

**Steps:**
1. `deploy/docker-compose.demo.yml`: `api-demo` / `web-demo` (`container_name: clinic-api-demo`, `clinic-web-demo`),
   same images; `Deployment__Profile=PublicDemo`, own `ConnectionStrings__DefaultConnection` (`clinic_demo`, role
   `clinic_demo`), own `Auth__Local__SigningKey_FILE`, own `Auth__Local__Issuer/Audience`, own
   `DataProtection__KeyRingPath` volume + certificate, `MinIO__BucketName=clinic-demo` + own keys,
   `FrontendUrl=https://${DEMO_DOMAIN}`, `Demo__SignupUrl`; **no** SMTP / Meta / Google / Push / Reminders env;
   `mem_limit` + `cpus` on both; `web-demo` `API_INTERNAL_URL=http://api-demo:5000/api`.
2. `deploy/Caddyfile`: `{$DEMO_DOMAIN}` block copied from the app block → demo upstreams, `X-Robots-Tag: noindex`,
   `/manifest.webmanifest` → 404; `DEMO_DOMAIN` in caddy env.
3. `deploy-hosted.yml`: when the server `.env` has `DEMO_DOMAIN`, add `-f docker-compose.demo.yml` to `$COMPOSE`
   (all three places, so `--remove-orphans` never deletes the demo), pull `api-demo web-demo`, `/health` +
   `verify-schema` on `clinic-api-demo`, and a **warning** in the run summary when `demoBuiltOn` is older than
   yesterday (spec AC-7.6: a failed rebuild is visible).
4. Runbook: create role + DB, MinIO user + bucket policy, secrets owned by uid 1654, DNS A record, first boot
   (builds the cabinet by itself), `Demo__RebuildOnStart=true` to force one, how to read `demoBuiltOn`.
5. Rehearse locally under a throwaway compose project name; tear it down (`down -v` on **that** project only) and
   remove its images / build cache afterwards.

**Validation:**
- [ ] Local rehearsal: two hosts via `/etc/hosts`-style names, demo token refused by the « prod » API, demo cannot
      reach the « prod » DB with its role, noindex header present, no install offered.
- [ ] Owner reviews the diff + runbook before any VPS step.

### US-8: « Essayer la démo » on apexa.tn (owner's OK, two pushes)

**Goal:** the website opens the demo in a new tab and counts the click. Covers spec US-6.
**Blocked by:** US-7 live
**Layers:** Site

**Steps:**
1. Bring the Umami listener commit (`587ad9ea`, `layout.html:96-101`) into the site branch first (owner's call
   which branch).
2. Hero second action, phone drawer, odontogramme CTA; desktop top-bar text link only if it fits beside the one
   button.
3. Extend the capture listener: `a[href^="https://demo.apexa.tn"]` → `umami.track('essayer-demo')`.
4. `check-tokens.mjs` passes; local preview shown to the owner; then the two pushes (diff origin first).

**Validation:**
- [ ] Local preview at 320/390/1440; owner approves; event visible in Umami after the push.

## Testing Strategy

### Unit Tests (API — `dotnet test -c Release`, unfiltered, temp `BaseOutputPath`)
- `DeploymentProfileTests`: the `PublicDemo` column, every capability; `PermitsOsPush` no longer throws.
- `DemoReadOnlyMiddlewareTests`: methods, attributes, inert off, pipeline order.
- `DemoExemptionCoverageTests`: both attributes, both directions; export-ledger scan.
- `StartDemoSessionCommandTests` + refresh cap.
- `DemoCabinetBuilderTests`: shape, working-day dates, no raw clock.
- Watermark render test; existing render, coverage and CSP-agreement tests stay green.

### Integration (database) — the part unit tests cannot see
- Demo API boot builds on a throwaway DB, then `verify-schema` + `reconcile-money` → exit 0.
- Rebuild job twice + purge on the same DB.

### Browser (`/test-in-browser`, isolated stack, planned single pass)
- Tier A entry/expiry; tier B every FR-4 row; tier C every sidebar screen with content and agreeing figures; tier D
  regression on a `HostedMultiTenant` boot (login, save, PDF without mark). Widths 320/390/820/1180/1440, 730 px
  tall, coarse emulation.

### Frontend gate
- `npm run check:responsive` (+ the two new rules), `npx tsc --noEmit`, `npm run build`.

## Risk Register

| ID | Risk | Likelihood | Impact | Story | Mitigation |
|---|---|---|---|---|---|
| R-1 | A write path the gate misses (new endpoint, a GET that writes) leaks a visitor's change to every visitor | Med | High | US-2 | reflection coverage both directions + export-ledger scan; nightly rebuild bounds the damage to one day |
| R-2 | Demo token or key ring valid on production | Low | High | US-1, US-7 | separate signing key **and** issuer/audience; separate key ring + cert; manual replay probe in US-1 and US-7 |
| R-3 | `RunsClinicJobs = true` also runs stock-expiry, monthly-expense, activity-counter, recovery-point jobs on the demo | High | Low | US-1 | accepted: they only touch the demo DB and make it look alive; recovery points / activity counter are harmless there. Not split into a new capability (YAGNI) — revisit if one writes something visible and wrong |
| R-4 | Seed breaks after a product change (a command's shape moves) | High | Med | US-4, US-6 | compile-time (real commands); nightly failure keeps yesterday's cabinet + `demoBuiltOn` stale → visible (deploy summary warns); add a demo boot to CI's `local-mode`-style job on a throwaway DB (follow-up if CI time is tight) |
| R-12 | A half-built cabinet becomes the one visitors enter | Low | Med | US-1, US-6 | admin deactivated right after provisioning; locator needs active admin **and** ≥ 1 patient; activation is the last step |
| R-5 | Seed through commands is slow or trips a guard (lockouts, rate limits, past-date refusals, « créneau occupé ») | Med | Med | US-4 | in-process (no HTTP limits); scenario respects `PaymentDateRules` (no future payments) and slot overlap; build it incrementally area by area |
| R-6 | Purging the previous cabinet while a late visitor still uses it | Low | Low | US-6 | purge only cabinets older than the previous one (≥ 24 h ≫ 2 h cap) |
| R-7 | Shared-tree / shared-stack collisions with 6 live sessions | Med | Med | all | worktree; isolated stack `:5099/:3099` + own DB; `stack-lease.ps1` before any start; announce before touching shared primitives (`FormErrorBanner`, `client.ts`, `app-shell.tsx`) |
| R-8 | `FormErrorBanner` / `showErrorToast` change alters production dialogs | Low | Med | US-2 | the new branch is keyed on `code === demo_read_only` only; production never emits it |
| R-9 | Demo starves production on the same VPS (a Facebook burst) | Low | High | US-7 | `mem_limit` + `cpus` on demo containers; anonymous-auth limiter + global per-IP ceiling; PDF limiter |
| R-10 | Audit rows of a failed build linger (written on their own context) | Med | Low | US-6 | failed build's clinic is purged, its chain with it; `verify-schema` audit checks on the demo after each deploy |
| R-11 | Plan is large (8 stories, ~60 files) | High | Med | all | stories are independent vertical slices with commits per story; US-7/US-8 wait for the owner anyway |

## Breaking Changes

None for existing deployments — additive:
- `GenerateRefreshToken` gains an optional parameter (two other callers unchanged).
- `DeploymentProfileTests` matrix becomes a 4-tuple (test-only).
- `FormErrorBanner` / `showErrorToast` gain a branch no production response triggers.

## Migrations

None (session cap from `CreatedAt`; « current cabinet » from admin activation; no new table or column).
One-time **operator** steps on the VPS (US-7 runbook, owner's OK each): role + database, MinIO user + bucket,
secret files, DNS record, Caddy reload with `--force-recreate`.
