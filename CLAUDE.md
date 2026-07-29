# Clinic Management — Repo Guide

Full-stack **dental/medical clinic management** system (Tunisia-targeted: French UI labels, Tunisian governorates). Multi-tenant by clinic, with patient records, appointments + Google Calendar sync, medical/dental documents, file storage, and an AI assistant.

> **Read this first.** Each major folder has its own `CLAUDE.md` with details. This file is the map — jump to the right sub-guide instead of re-reading source.

## Stack at a glance

| Layer | Tech | Location |
|-------|------|----------|
| Frontend | Next.js 15 (App Router), React 19, TypeScript, Tailwind v4, shadcn/ui, Auth0 | `web/` |
| Backend API | .NET 8, Clean Architecture, ASP.NET Core, MediatR (CQRS), Hangfire | `api/` |
| Database | PostgreSQL 16 (EF Core) | docker-compose `postgres` |
| Object storage | MinIO (S3-compatible) | docker-compose `minio` |
| Auth | **Pluggable** by `Auth:Mode`: **Cloud** = Auth0 (JWT bearer); **Local** = self-issued email+password accounts for offline LAN installs. Clinic membership resolved server-side. | both |
| External | Google Calendar (two-way sync), HuggingFace (AI chat), SMS/WhatsApp reminders, TTN « El Fatoora » e-invoicing, Meta WhatsApp onboarding | `api/...Infrastructure/Services` |

## Layout

```
clinic-management/
├── api/                          .NET 8 Clean Architecture solution (ClinicManagement.sln)
│   ├── ClinicManagement.Domain/         → CLAUDE.md  (entities, value objects, repo interfaces)
│   ├── ClinicManagement.Application/     → CLAUDE.md  (CQRS features, MediatR pipeline, DTOs, Result<T>)
│   ├── ClinicManagement.Infrastructure/  → CLAUDE.md  (EF Core, repos, external services, DI)
│   ├── ClinicManagement.API/             → CLAUDE.md  (controllers, SignalR hubs, background jobs, Program.cs startup)
│   └── ClinicManagement.UnitTests/       → CLAUDE.md  (~90 xUnit+Moq classes mirroring every layer; guard tests)
├── web/                          Next.js frontend
│   ├── (root)                            → CLAUDE.md  (stack, routing, API/auth integration)
│   ├── components/                       → CLAUDE.md  (feature components + shadcn/ui primitives)
│   └── lib/                              → CLAUDE.md  (API client layer, hooks, realtime, utils)
├── desktop/                      WPF + WebView2 thin client shell (Local mode, Phase 5) → CLAUDE.md
├── packaging/                    Local/offline-LAN publish + installers (PowerShell + Inno Setup) → CLAUDE.md (+ README.md operator guide)
├── backend/                      EMPTY (only .idea/) — ignore
├── docker-compose.yml            postgres (5432) + minio (9000 API / 9001 console)
└── *.md (root setup docs, see below)
```

The **dependency direction** in `api/` is strict Clean Architecture: `API → Application → Domain`, with `Infrastructure` implementing Application's outbound interfaces. Domain has no infrastructure dependencies.

## Where things live (quick index)

- **A REST endpoint / route** → `api/ClinicManagement.API/Controllers/` (30 controllers). Controllers are thin MediatR pass-throughs.
- **Business logic / a use case** → `api/ClinicManagement.Application/Features/<Area>/{Commands,Queries}/` (handlers).
- **An entity / business rule** → `api/ClinicManagement.Domain/Entities/`.
- **DB schema / a query implementation / EF config** → `api/ClinicManagement.Infrastructure/Persistence/` + `Repositories/`.
- **An external integration** (Google Calendar, AI, files, notifications) → `api/ClinicManagement.Infrastructure/Services/`.
- **Realtime (live refresh across clients)** → SignalR `ClinicHub` at `/hub/clinic` (`api/ClinicManagement.API/Hubs/`), fed by the Application `RealtimeBroadcastBehavior`; frontend `web/lib/realtime/`.
- **A page / screen** → `web/app/<route>/page.tsx` (App Router).
- **A UI component** → `web/components/` (feature) or `web/components/ui/` (shadcn primitives).
- **Frontend → backend calls** → `web/lib/api/` (per-resource modules over `client.ts`).
- **A backend test** → `api/ClinicManagement.UnitTests/` (xUnit + Moq, one folder per layer).

## Running locally

```bash
docker compose up -d              # postgres + minio
cd api/ClinicManagement.API && dotnet run    # API (default http://localhost:5000)
cd web && npm install && npm run dev          # frontend (http://localhost:3000)
```
Frontend talks to the API via `NEXT_PUBLIC_API_URL` (default `http://localhost:5000/api`). EF migrations live in `Infrastructure/Migrations`.

## Key architectural notes (verified, may surprise you)

- **Optimistic concurrency, solution-wide (`data-and-money-integrity`)**: `Entity<TId>.Version` is mapped in the
  `ApplicationDbContext` loop onto PostgreSQL's **`xmin` system column**, giving all 38 entities a concurrency
  token with **no schema change**. A losing write raises `DbUpdateConcurrencyException`, translated **once** in
  `UnitOfWork.SaveChangesAsync` into `ConflictException` → **HTTP 409** with the canonical `{ error }` body.
  Two things are easy to get wrong here: (a) the handler catch-alls must carry
  `when (ex is not ConflictException)` or a 409 is flattened into a generic failure — only catches that
  *return a Result* were filtered, since a log-only catch is a best-effort post-commit side effect that must
  still swallow; (b) the check must run against the version **the user was editing**, so the six round-tripped
  aggregates (Patient, Appointment, Invoice, TreatmentPlan, DentalRecord, Clinic) send `Version` back on the
  update and the handler calls `IUnitOfWork.SetExpectedVersion` — a version of `0` means "not supplied" and
  skips the check, which is what keeps the AI dispatcher, Google→App sync and the jobs working.
  ⚠️ The `AddConcurrencyToken` migration has a **deliberately empty `Up()`**: EF's differ emits 38 ×
  `AddColumn<uint>("xmin")`, which PostgreSQL rejects (`column name "xmin" conflicts with a system column
  name`). It is committed for its **model snapshot** only.
- **Money is correctable, not immutable (`data-and-money-integrity`)**: invoice payments and treatment-plan
  installment payments can be **voided** (motif + actor + moment recorded; the row is kept and struck through,
  and a reprinted receipt is stamped « REÇU ANNULÉ »). The installment side required an **event-sourced
  `InstallmentPayment` ledger** — a single cumulative `AmountPaid` has nothing to void, and dating it by one
  `LastPaidOn` also booked revenue into the wrong month. **Avoirs** are now readable, listable and printable,
  and are netted in *both* branches of the revenue read and in the dashboard KPI. Issuing a **devis→facture**
  bridge invoice **carries the plan's collected payments across**, with the read-side de-dup extended from
  outstanding to cash — the two had to land together or the money is either doubled or erased.
- **Patient records resist destruction (`data-and-money-integrity`)**: deleting a patient is **refused** when
  anything is attached (the message names the real counts), and **archiving** (`Patient.IsArchived`) is the
  escape hatch. Contact details are genuinely optional — `Email`/`PhoneNumber` are nullable and the four
  sentinel literals (`noemail@example.com`, `0000000000`, `unknown@example.com`, `000-000-0000`) are retired.
  The `PUT /api/appointments/{id}` partial-update wipe is closed by generalizing the tri-state pattern to
  `ProcedureTypeId`/`DoctorId`/`Notes`/`DoctorName`; `UpdatePatientCommand`'s contact fields use the same
  mechanism, so a field can finally be **cleared** rather than only overwritten.
- **`reconcile-money` (Local-mode console verb)**: `dotnet run -- reconcile-money` prints a per-clinic
  reconciliation — the two payment ledgers against their stored denormalizations, per-plan échéancier sums,
  monthly collected computed the **old and the new way** (the line that proves the ledger migration moved no
  closed month), orphan and sentinel counts, over-credited invoices and duplicate bridge invoices. Exit code
  **0** clean / **1** couldn't run / **2** drift found; it never mutates. Run it before and after the migration
  batch and diff.
- **`verify-schema` (Local-mode console verb)**: the sibling gate for **schema** changes, added by
  `audit-sections-3-to-10`. Nothing in the test project touches a database, so a migration is the one class of
  change unit tests structurally cannot verify — an index can be missing, an exclusion constraint can be
  non-partial, a backfill can cover zero rows, and the whole suite still passes. `dotnet run -- verify-schema`
  reads the **EF model** (its declared indexes, FKs and decimal precisions) and diffs it against PostgreSQL's own
  catalog, so a schema object added in a configuration file is verified for free — deliberately **not** a
  hand-maintained expectation list, which is the failure mode the plan's R-9/R-13/R-14 all describe. On top of the
  diff it asserts what the model cannot express: `btree_gist` is installed, the appointment exclusion constraint
  exists **and is partial** (a non-partial one makes a cancelled slot permanently unbookable), the two VAT-rate
  columns keep `(5,2)` while every other decimal is `(18,3)`, and the per-migration backfill row counts. Indexes
  are matched on **table + ordered columns, never on name** (a hand-written migration's name legitimately differs
  from EF's). Same exit codes and the same before/after-and-diff workflow as `reconcile-money`; read-only.
  Logic in `Application/Common/Maintenance/SchemaVerificationService.cs`, both-sides reader in
  `Infrastructure/Persistence/SchemaVerificationReader.cs`.

- **The dashboard is a composed read, not a KPI bag (`dashboard-insights`)**: `GET /api/dashboard?period=Today|Week|Month`
  returns four sections — comparable **Activité** (RDV honorés, nouveaux patients, **taux d'absence**, devis acceptés) and
  **Argent** (encaissé, facturé, dépenses, net), the point-in-time **créances** total, the **À-traiter** counts across the
  operational subsystems, and a 6-month collected **trend**. It closes the two items `features/live-dashboard/` explicitly
  deferred (delta computation and card drill-down). A thin handler fans out to four **section readers**; two primitives do
  the load-bearing work. **`DashboardPeriod`** is the *single* authority on period arithmetic — it derives the current
  **and** previous bounds through `ClinicClock`, because a comparison whose halves came from two different rules is not a
  comparison (the client used to send six boundaries; it now sends only a period key). ⚠️ Its `ToInclusive` is the last
  **tick** of the window, not the next midnight: `ClinicClock.EndOfLocalDayUtc` is *exclusive* while the money reads are
  inclusive on both ends, so the raw bound counts a midnight payment in **both** adjacent periods — finding #20 re-armed.
  **`PeriodComparison`** is the one shape of a comparable figure, and distinguishes a real `0` from an *undefined* value:
  a period with no appointments has **no** taux d'absence, and rendering `0 %` would assert perfect attendance.
  ⚠️ The readers are awaited **sequentially** — they share the request's `DbContext`, so `Task.WhenAll` throws.
  **Every figure is a link to the filtered records it counted**, and the KPI→route mapping lives in exactly one place
  (`web/lib/dashboard-links.ts`, an exhaustive `Record`, so a KPI with no destination is a `tsc` error). Making that true
  required nine destinations to learn filters — including a genuinely new created-date filter on `/patients`, a
  **date-range mode** on `/caisse` (day-only before), a **stage filter** on `/lab-orders` (it had none), and an expiring
  filter on `/stock`. Two links carry a trap worth knowing: « Devis acceptés » counts by `AcceptedDate` and so filters on
  `acceptedFrom`/`acceptedTo` (`from`/`to` bound *creation* — a different set of devis), and « Taux d'absence » sends
  `status=NoShow,Cancelled` because that pair *is* the rate's numerator. `MoneyReadConsistencyTests` was extended to pin
  dashboard-vs-caisse agreement, so the money section is now the **fourth** read held to the one figure.
- **Multi-tenancy**: every request is scoped to a clinic. The **authoritative** check is per-request in the handlers — the clinic is resolved from the DB user record (`ICurrentClinicResolver`/`IClinicContext` → DB lookup of the `sub`, not purely from the JWT claim) and each loaded aggregate's `ClinicId` is re-verified. Since `cloud-security-and-tenant-isolation` (PR #11) there is **also** a defense-in-depth backstop: EF Core **global query filters** on ~15 clinic-owned aggregate roots, fed the JWT `clinic_id` via `ICurrentClinicProvider` (fail-open — inactive when no clinic is in scope, so jobs/CLI/auth flows still work). See `Infrastructure/Persistence/ApplicationDbContext.cs`. Tenant-isolation is pinned by `*TenantIsolationTests` in the test project.
- **Pluggable auth (`Auth:Mode` = `Cloud` | `Local`)**: Cloud is the original Auth0 path; **Local** (for offline Windows/LAN installs) issues its own HS256 JWTs against local email+password accounts. Backend seam: `ILocalAuthService`/`LocalAuthService` (+ per-install signing key via `LocalAuthConfig`), a mode-branched JWT setup in `Program.cs`, and `AuthController` (`login`/`setup`/`register`/`mode`/`change-password`). `CreateClinicCommand`/`JoinClinicCommand` branch to a Local path when a `Password` is present. Frontend seam: a single `useSession()` context (`web/lib/auth/session.tsx`) backed by either `CloudSessionProvider` (Auth0) or `LocalSessionProvider` (HttpOnly cookie), gated on `AUTH_MODE`. All Local behavior is additive; the Cloud path is unchanged. Offline admin lockout recovery is a console command (`dotnet run -- reset-admin-password`), not a web endpoint. *All 5 phases of the offline-Windows repackaging are complete — see `features/windows-desktop-app/`.*
- **Local-disk file storage (Phase 2)**: the single `IFileStorage` seam is mode-branched — `LocalDiskFileStorage` (Local, blobs under `FileStorage:BasePath`) vs `MinioFileStorage` (Cloud). Additive; Cloud unchanged.
- **Connectivity awareness (Phase 3, Local mode)**: internet reachability is judged by the **server** (LAN clients may have no egress). `IInternetProbe`/`InternetProbe` (Singleton, cached) backs an anonymous, Local-only `GET /api/connectivity` (404 in Cloud) that the frontend polls via `ConnectivityProvider`/`useConnectivity()`. The two internet-dependent features — AI chat + Google Calendar — visibly disable offline and auto re-enable; appointments not yet pushed to Google show a "non synchronisé" badge + manual "Push to Google" (`AppointmentDto.IsSyncedToGoogle`). Cloud gets a static "online" default and behaves as before.
- **LAN hosting & security gates (Phase 4, Local mode)**: hardens the API for offline-LAN hosting, all additive/gated to Local (Cloud byte-for-byte unchanged). (a) Fail-closed authorization — `AuthorizationPolicies.ConfigurePolicies(options, isLocalMode)` installs a `FallbackPolicy = RequireAuthenticatedUser()` in Local, so anything without an explicit `[AllowAnonymous]` returns 401; the exact allow-list is pinned by `ControllerAuthorizationCoverageTests`. (b) Loopback-only `/hangfire` (`HangfireAuthorizationFilter` + `LocalRequest.IsLoopback`, shared with the `setup` gate / AC-1.2a) — since `cloud-security-and-tenant-isolation` this is loopback-only in **both** modes (Cloud no longer authorizes everyone). (c) Config-driven CORS (`CorsOrigins` — LAN origins via `Cors:AllowedOrigins`), HTTPS cert binding + guarded redirect, and Kestrel bind (`Https:*`/`Hosting:*`) in `Program.cs`. (d) The OAuth callback no longer rewrites appsettings; Google refresh tokens are stored **per-clinic on the `Clinic` entity** (`GoogleRefreshToken`/`GoogleCalendarId`, DB). *(Phase 5 closed the Phase-4 cert-downgrade gap — a set-but-missing `Https:CertPath` now fails startup loud instead of dropping to HTTP. `cloud-security-and-tenant-isolation` then closed the OAuth `state` gap: `connect` mints CSRF `state` into a server cache + HttpOnly double-submit cookie, validated on `callback`; the old singleton `IGoogleTokenStore`/`FileGoogleTokenStore` was retired for per-clinic DB storage.)*
- **Packaging, installers & manual backup (Phase 5, Local mode)**: turns the app into a self-contained offline-LAN Windows product, all additive/gated to Local (Cloud unchanged). (a) **Same-origin front door** — in Local, Kestrel is the *single* browser-facing HTTPS endpoint: `/api/*` runs in-process and a **YARP** catch-all reverse-proxies every other route (pages, `/_next/*`, `/bff/*`) to the co-located Next server on loopback; the web build ships with relative `NEXT_PUBLIC_API_URL=/api`, so TLS terminates once and no server IP is baked in. Frontend BFF auth routes moved `/api/auth/*` → **`/bff/auth/*`** to avoid colliding with the proxied `/api/*`. (b) **Self-generated HTTPS** — `CertificateProvisioner` mints a CA + SAN server cert into `.local/` on first boot (idempotent); HTTP binds loopback-only, HTTPS (5001) is the only LAN port. (c) **Windows service + startup diagnostics** — `UseWindowsService()` auto-start; `StartupDiagnostics` turns DB-down / port-in-use into clear French messages + non-zero exit (console/log/Event Log). (d) **One-click backup** — admin-only `POST /api/backup` → `PgDumpBackupService` (`pg_dump` custom-format dump + file-storage copy to a timestamped folder; fails loud, never a silent partial). (e) **`LocalInstallPaths`** anchors `.local/`, `Files/`, `logs/` to the install dir (a service's CWD is `System32`). (f) **`desktop/`** WebView2 shell + **`packaging/`** publish script and Inno Setup server/client installers (bundled PostgreSQL 16, Node, NSSM, CA-trust import) — **operator-verified (R-1)**, not CI-runnable.
- **Google Calendar sync is asymmetric + per-clinic**: each clinic uses its **own** Google connection (`Clinic.GoogleRefreshToken`/`GoogleCalendarId`) — no shared account. App→Google runs inline on appointment create/update (using the appointment's clinic). Google→App is implemented but has **no scheduled job** — the disabled `GoogleCalendarSyncJob` class + its commented recurring registration were removed as dead scaffolding (`french-localization-and-cleanup`; `Program.cs` keeps only a defensive `RemoveIfExists`). Google→App runs only via the manual `GoogleCalendarController` endpoint (scoped to the caller's clinic).
- **Background jobs**: Hangfire is wired; **two minutely recurring jobs** — **`NotificationJob`** (SMS/WhatsApp appointment-reminder dispatcher) and **`EInvoiceOutboxJob`** (TTN « El Fatoora » e-invoice outbox dispatcher), both connectivity-gated and no-op until configured (a `Reminders` channel + credentials; a clinic enabling e-invoicing) — **plus one daily**, **`StockExpiryJob`** (approaching-expiry stock alerts, `audit-sections-3-to-10`). The daily one is deliberately **not** connectivity-gated: its alert is in-app, so it must work on an offline LAN install. It exists as a *job* rather than a write hook because expiry is crossed by the **passage of time** — the box nobody has touched is exactly the case the alert is for, and no write happens on the day it enters the lead window; a write-only trigger would have been a notification that never fires for its main case. On-demand `PdfGenerationJob` also fires. (`AISummaryJob` was removed in `reliability-and-polish`; the calendar-sync job in `french-localization-and-cleanup`.)
- **Billing / CNAM / treatment plans / e-invoicing (deep, fully-wired subsystems)**: the product's richest area, largely French already. **Billing** — `Invoice`/`InvoiceLine`/`Payment` (notes d'honoraires: draft→issued→paid, VAT + stamp, per-clinic billing settings), `InvoicesController` + `BillingController` (unified per-patient `solde patient`). **Treatment plans / devis** — `TreatmentPlan`/`TreatmentPlanItem` + payment `Installment` schedule (`TreatmentPlansController`), seeded from the odontogram by a **frontend** prefill (there is no backend seed command). Since `treatment-plan-workspace` the loop is **bidirectional**: a plan derives each act's état from the appointments pointing at it and shows the invoice that bills it (`TreatmentPlanWorkflowProjection`, two batched reads per request), has its own workspace route `/treatment-plans/[id]`, orders its acts (`SequenceNumber`), and can be **amended after acceptance** (`RevisionNumber`) instead of cancelled and retyped — unless a non-cancelled invoice already represents it. **CNAM** — `CnamNomenclatureEntry`/`CnamLetterValue`/`DentalActCode` (`CnamNomenclatureController`/`DentalActsController`) back the **BS1 bulletin** reimbursement estimate + overlay renderer (`CnamBs1BulletinRenderer`, Infrastructure). **El Fatoora (TTN) e-invoicing** — invoices queue an e-invoice dispatched by the recurring `EInvoiceOutboxJob`. Also `Medication`/`MedicationActiveIngredient` (ordonnance picker), `ToothState` (odontogram), `DashboardController` (the composed dashboard read — see below). The **CNAM/medication/dental-act catalogs are per-clinic** (each clinic seeded the same defaults via `IClinicCatalogSeeder`, then edits stay private) — despite some stale "global reference data" docstrings still in those entities.
- **Clinical-workflow-depth operational features (built)**: beyond scheduling, the app has an **interactive odontogram** (chart/diagnose per tooth — `OdontogramController`, `ToothState`), **recurring appointment series** (`RecurringAppointment`, above), a **waiting list / salle d'attente** (`WaitingListController`, promote-to-appointment), **lab work orders / bons de prothèse** (`LabOrdersController`, status lifecycle), **patient recall / relance** (`RecallController` + `Clinic.RecallIntervalMonths`, SMS/WhatsApp send), a **caisse / expenses** ledger (`ExpensesController` + `BillingController` caisse summary), and a **post-visit-review** prompt (a `StaffNotification` category deep-linking staff to record a finished visit). Each is clinic-scoped with a matching page under `web/app/`.
- **Dead-code cleanup (`french-localization-and-cleanup`)**: the **domain-events pipeline was removed** — `Domain/Events/*`, `IDomainEvent`, and `AggregateRoot`'s event list were dead (`SaveChangesAsync` never drained events; zero `INotificationHandler`s). Side effects are produced inline post-commit via `INotificationGenerator`/`IReminderScheduler`. *(`RecurringAppointment` is no longer merely "reserved" — the clinical-workflow-depth feature wired it: `AppointmentsController` `GET/POST recurring` + `POST recurring/{id}/cancel`, `RecurringAppointmentRepository`, expanded into `Appointment` rows.)*
- **Clinic-scoped SignalR realtime (built)**: every mutating command auto-broadcasts a `entityChanged` event to the caller's `clinic-{id}` group via the Application `RealtimeBroadcastBehavior` → `IRealtimeNotifier` → API `ClinicHub` (`/hub/clinic`); the resource key is derived from the command's namespace (so new commands broadcast for free). The frontend subscribes with `useClinicRealtime(resource, refetch)` (`web/lib/realtime/`) so a peer's edit live-refreshes appointments, patients, stock, invoices, treatment plans, catalogs, users, notifications, la caisse, and — since `audit-sections-3-to-10` closed audit § 9.1 — the **salle d'attente, lab orders, relances, recurring series, « Mon profil » and the patient's documents tab**. The **dashboard** now subscribes to all nine keys its figures depend on (it watched only four, so the à-traiter counts and la caisse went stale under a peer's edit). Excluded areas: Auth, AI, Backup, Connectivity, and all queries. ⚠️ The two sides are now held together by a **contract test**, not by discipline: `RealtimeResourceResolverTests` reflects over every `IRequest` for the emitted set, **parses `clinic-hub.ts`** for the declared set, and fails unless they are equal **in both directions**. It replaced a hardcoded `[InlineData]` table that stayed green for the whole period five keys (`expenses`, `doctors`, `laborders`, `recall`, `waitinglist`) were broadcast with nothing listening — the derived-vs-listed lesson `verify-schema` also embodies.
- **In-app staff notification center (built)**: a real, clinic-scoped in-app feed — header bell + unread badge → panel (newest-first, per-user read/unread, mark-all-read, deep-links), live over the SignalR `"notifications"` key above. Backed by a **`StaffNotification`** aggregate (one shared row per event) + per-user **`NotificationRead`** markers (no write-time fan-out). Notifications are generated **best-effort, post-commit** by an `INotificationGenerator`/`NotificationGenerator` seam called inline from the appointment/stock command handlers (appointment created/cancelled/rescheduled, ~24h reminder, not-low→low stock crossing) — a generation failure logs at Error but **never** fails/rolls back the core operation. This is **in-app only**; the dormant email/SMS `Notification` entity + `NotificationService` stay untouched. The actor who caused an event is excluded from their own feed.
- **Real outbound SMS/WhatsApp reminders** (feature `sms-whatsapp-reminders`): the previously-dormant `Notification` outbox is now live for **SMS + WhatsApp** appointment reminders — `IReminderChannelSender` (`HttpSmsSender`/`WhatsAppSender`) + `RemindersConfig`/`ReminderSchedule`/`ReminderPhone`, enqueued best-effort post-commit by `IReminderScheduler`/`ReminderScheduler` from the appointment handlers and dispatched by the connectivity-gated minutely `NotificationJob`. Secrets come from env (or per-clinic, encrypted). Per-clinic settings (`ClinicReminderSettings` + `IReminderSettingsProvider`) override the per-install config: channel toggles, sender identity, **gateway/Graph URLs, lead-time tiers and the message wording** (all admin-editable in `reminder-settings.tsx` — `reliability-and-polish`), so a channel can be turned fully on without a server-config edit. The settings GET returns a per-channel `effectiveStatus` (`configured`/`not_configured`) that drives a "sendable vs. warning" badge (a WhatsApp OAuth "Connecté" downgrades to a warning when the resolved settings still can't send), and `GET /api/clinics/reminder-status` surfaces the recent outbox rows (sent/pending/failed + reason).
- **Patient AI summary is real**: the patient detail page's summary (`GET /api/patients/{id}/ai-summary` → `PatientAiSummaryDto`) is a live HuggingFace call (`IHuggingFaceAIService`), connectivity-gated. *(The old placeholder `PatientSummaryService`/`IPatientSummaryService` + the disabled `AISummaryJob`, the never-registered `GoogleAIService`/`IGoogleAIService`, and the dormant email `NotificationService`/`INotificationService` were removed as dead code in `reliability-and-polish` — HuggingFace is the sole wired AI backend, and the live outbound reminders go through the `IReminderChannelSender` senders below.)*
- **`ValidationBehavior` is inert**: no FluentValidation validators exist; handlers validate inline and return `Result.Failure`.
- **Frontend data-wiring**: the dashboard, `appointment-list`, the stock feature, and the notification center are all API-wired (`dashboardApi`, `useAppointments`, `stockApi`, `notificationsApi`/`useNotifications`). The header **search** is a live patient lookup (type → results → open the patient). *(The orphan `notifications-list` sample component and the redundant `dental-chart` tooth chart were removed in `reliability-and-polish`; the read-only summary chart now reuses `record-tooth-chart`.)*
- **Security posture (mostly hardened by `cloud-security-and-tenant-isolation`, PR #11)**: committed `api/.../appsettings.json` secrets were **retired** — DB connection string, `GoogleCalendar:ClientSecret`, `HuggingFace:ApiKey`, WhatsApp/SMS/Meta tokens are now empty strings + `// SECRET` comments; supply real values via env / user-secrets / `.local/` / the installer's `appsettings.Production.json`. An empty DB connection string fails startup loud (`return 1`). The Hangfire filter is loopback-only in **both** modes; the Google OAuth `state` is validated; Google refresh tokens live per-clinic in the DB. **Residual (Cloud)**: the authorization `FallbackPolicy` stays null (a Cloud controller without `[Authorize]` is still anonymous — Local fails closed), and a few non-secret real values remain in config (`Auth0:Domain/Audience`, `GoogleCalendar:ClientId`; `Auth0:ManagementApi` still has `YOUR_*` placeholders). Treat with care.

## Root-level setup / reference docs

| File | Topic |
|------|-------|
| `README.md` | (minimal) |
| `AUTH0_SETUP.md` | Auth0 tenant/app configuration |
| `GOOGLE_CALENDAR_SETUP.md` / `_FR.md` | Google Calendar OAuth setup (EN/FR) |
| `GOOGLE_CALENDAR_SYNC_ARCHITECTURE.md` | Calendar sync design |
| `SYNC_TESTING_GUIDE.md` | How to test calendar sync |
| `HUGGING_FACE_SETUP.md` | AI provider setup (HuggingFace — the sole wired AI backend) |
| `api/CLINIC_MANAGEMENT_FLOW.md`, `MULTI_CLINIC_SETUP.md`, `ROLE_ASSIGNMENT_IMPLEMENTATION.md`, `ENTITY_UPDATE_GUIDE.md`, `IMPLEMENTATION_SUMMARY.md` | Backend feature/process docs |
| `packaging/README.md` | Local/offline-LAN operator guide (Phase 5): publish + installers, bundled PostgreSQL, backup/restore, admin recovery, per-AC verification checklist |

> When code changes, update the nearest `CLAUDE.md` so this map stays accurate.
