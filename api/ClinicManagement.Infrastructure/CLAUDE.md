# ClinicManagement.Infrastructure

Infrastructure layer (Clean Architecture). Implements the outbound interfaces declared in Domain
(`Domain/Repositories`) and Application (`Application/Common/Interfaces`): EF Core/PostgreSQL data access +
multi-tenant query filters, repository implementations, mode-branched file storage (MinIO vs local disk),
per-clinic Google Calendar two-way sync, HuggingFace AI + agentic action dispatch, SMS/WhatsApp reminders
(+ per-clinic settings, encrypted secrets), TTN « El Fatoora » e-invoicing, CNAM BS1 bulletin + French PDF
rendering, Auth0 management (Cloud) / local JWT auth (Local), and — for offline installs — `pg_dump` backup,
self-generated HTTPS trust material, and per-clinic reference-catalog seeding. All wiring lives in
`Extensions.cs` (`AddInfrastructure`).

> Two auth modes gate behavior: `Auth:Mode` = `Cloud` (Auth0, MinIO) | `Local` (offline LAN, self-issued
> JWT, local-disk storage). Resolved via `LocalAuthConfig.IsLocalMode(config)`. Local additions are additive;
> Cloud is unchanged unless noted.

## EF Core Persistence (`Persistence/`)

- **`ApplicationDbContext.cs`** — the single `DbContext`. Injects an optional `ICurrentClinicProvider?` (null
  at design-time / in manual construction → filters inactive). Key mechanisms:
  - **Multi-tenant global query filters** (defense-in-depth backstop, NOT the authoritative check — handlers
    still verify the DB-resolved `User.ClinicId`). `IsClinicScoped`/`ScopedClinicId` are read through the
    instance so EF treats them as per-query parameters. `HasQueryFilter` scopes the directly-clinic-owned
    aggregate roots: `Patient`, `Appointment`, `ProcedureType`, `StaffNotification`, `Invoice`,
    `TreatmentPlan`, `ClinicReminderSettings` (by `Id` = clinic id), `CnamNomenclatureEntry`,
    `CnamLetterValue`, `Medication`, `DentalActCode`, `Expense`, `WaitingListEntry`, `LabWorkOrder`,
    `RecurringAppointment`. **`User`/`Clinic` are deliberately unfiltered** (auth/join flows resolve them
    before a clinic context exists). Child entities (`InvoiceLine`, `Payment`, `Installment`,
    `TreatmentPlanItem`, `MedicationActiveIngredient`, `DentalRecordTooth/Act`, `ToothState`,
    `NotificationRead`) carry no filter — reached only through a filtered parent / scoped by `UserId`.
    Cross-clinic paths (per-clinic seeder, reminder dispatcher, Google→App sync when no scope) call
    `IgnoreQueryFilters()` or run with no clinic in scope so the filter is inactive.
  - **UTC everywhere**: `OnModelCreating` installs a global value converter forcing every `DateTime`/`DateTime?`
    to UTC (PostgreSQL `timestamp with time zone`), and `SaveChanges`/`SaveChangesAsync` re-run
    `ConvertDateTimesToUtc()` on Added/Modified entries (belt-and-suspenders). `Unspecified` is assumed UTC.
  - `OnConfiguring` ignores `PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning` (expected
    noise from filtering roots but not their children).
- **`ApplicationDbContextFactory.cs`** — `IDesignTimeDbContextFactory` for `dotnet ef` (reads the API project's
  connection string).
- **`UnitOfWork.cs`** — `IUnitOfWork`: wraps `SaveChangesAsync` + `BeginTransaction/Commit/Rollback`.
  Repositories only stage changes; callers commit via the UoW.
- **Reference-catalog seeds + seeder** — `CnamCatalogSeed`, `MedicationCatalogSeed`, `DentalActCatalogSeed`
  (shared single-source-of-truth defaults) + **`ClinicCatalogSeeder`** (`IClinicCatalogSeeder`): clones the
  defaults into each clinic on creation and a startup backfill (`SeedAllClinicsAsync`, called from the API's
  `DeferredStartupService`/`Program.cs`). Uses the `DbContext` directly with no clinic in scope; idempotent
  per catalog; deterministic per-clinic GUIDs.

### Entity Configurations (`Persistence/Configurations/`)
One `IEntityTypeConfiguration<T>` per aggregate, auto-discovered via `ApplyConfigurationsFromAssembly`.
Conventions: `Id` `ValueGeneratedNever()` (GUIDs from domain ctors); enums `HasConversion<int>()`; value
objects (Email, PhoneNumber) owned/converted. `AppointmentConfiguration` stores `Duration` as ticks, makes
`PatientId` nullable (busy slots, `SetNull`), `ProcedureTypeId`/`DoctorId` FKs `SetNull`. Files: Appointment,
Patient, PatientFlag, PatientFile, PatientFolder, PatientMedicalHistory, PatientFamilyHistory, Notification,
StaffNotification (indexes `(ClinicId, EffectiveFeedTime)` + `AppointmentId`), NotificationRead (PK
`(NotificationId, UserId)`), StockItem, ProcedureType, RecurringAppointment, DentalRecord, DentalRecordTooth,
DentalRecordAct, ToothState, MedicalDocument, Clinic, User, Doctor, Invoice, InvoiceLine, Payment,
Installment, TreatmentPlan, TreatmentPlanItem, ClinicReminderSettings, CnamNomenclatureEntry, CnamLetterValue,
DentalActCode, Medication, MedicationActiveIngredient, Expense, WaitingListEntry, LabWorkOrder.

### Migrations (`Migrations/`)
44 migrations, applied automatically at startup (`context.Database.Migrate()` in API `Program.cs`). Early ones
build the base schema, Google-event id, procedures, medical/dental records, notes, storage folders, medical
documents, nullable-patient appointments, and the multi-tenant clinic/user/doctor model. Notable later ones:
`AddLocalAuthUserFields` (Local-auth `User` columns + partial unique index on lowercased email filtered to
`PasswordHash IS NOT NULL`), `AddStaffNotifications`, `AddPostVisitReview`, `AddCnamBulletinFields`,
`AddInvoicesAndClinicBilling`, `AddEInvoicing`, `AddPerClinicReminderSettings` + `AddPerClinicReminderOverrides`,
`AddDoctorCachetAndOrdre`, `AddCnamCatalog`, `AddMedicationCatalog`, `AddWhatsAppConnectionFields`,
`AddDentalCore` / `ToothStateRecordLink` / `DentalRecordActsAndProcedureResultingCondition` /
`BackfillDentalResultingConditions` / `AddClinicalLoopLinks` (clinical-workflow-depth), `AddInvoiceLineCnamActCode`,
`AddClinicWorkingHours`, `AddClinicGoogleCalendarConnection` (per-clinic Google refresh token/calendar id),
`AddPerClinicCatalogs`.
- **`20260723120623_MergeSnapshotReconcile`** (latest) — a snapshot-reconcile catch-up: `RecurringAppointment`
  gains `ClinicId` (FK cascade + index), `DoctorId`, `OccurrenceCount`, `ProcedureTypeId`, tightens
  `RecurrencePattern`/`DoctorName` lengths, and converts `Duration` `interval`→`bigint` ticks via raw SQL
  (`EXTRACT(EPOCH…) * 10_000_000`); `Appointment.DoctorId` `text`→`uuid` FK to `Doctors` (`SetNull`) via a
  guarded regex SQL cast (non-GUID legacy values → null); adds `Patient` recall fields (`LastRecallContactedAt`,
  `RecallReason`, `RecallSnoozedUntil`), `Doctor.WorkingHoursJson`, `Clinic.RecallIntervalMonths` (default 6);
  creates the `Expenses`, `LabWorkOrders`, `WaitingListEntries` tables.

## Repositories (`Repositories/`)
Concrete EF Core impls of Domain repo interfaces. Pattern: ctor-inject `ApplicationDbContext`; `GetById*` uses
`.Include(...)` for needed graphs; mutations only stage (UoW commits). All registered Scoped.

| Domain interface | Implementation |
|---|---|
| `IPatientRepository` | `PatientRepository` (careful tracked-vs-detached `UpdateAsync` to avoid full-column updates) |
| `IAppointmentRepository` | `AppointmentRepository` |
| `INotificationRepository` | `NotificationRepository` (reminder outbox rows) |
| `IStaffNotificationRepository` | `StaffNotificationRepository` (in-app feed: newest-first/actor-excluded/50-cap, unread gated on viewer join time, read-marker existence+insert, reminder-by-appointment lookup) |
| `IStockItemRepository` | `StockItemRepository` |
| `IProcedureTypeRepository` | `ProcedureTypeRepository` |
| `IDentalRecordRepository` | `DentalRecordRepository` |
| `IToothStateRepository` | `ToothStateRepository` (persistent odontogram) |
| `IPatientFolderRepository` | `PatientFolderRepository` |
| `IPatientFileRepository` | `PatientFileRepository` |
| `IMedicalDocumentRepository` | `MedicalDocumentRepository` |
| `IUserRepository` | `UserRepository` |
| `IClinicRepository` | `ClinicRepository` |
| `IDoctorRepository` | `DoctorRepository` |
| `IInvoiceRepository` | `InvoiceRepository` (billing + e-invoice outbox) |
| `ITreatmentPlanRepository` | `TreatmentPlanRepository` (devis + installments) |
| `IClinicReminderSettingsRepository` | `ClinicReminderSettingsRepository` |
| `ICnamCatalogRepository` | `CnamCatalogRepository` (nomenclature + lettre-clé values) |
| `IMedicationCatalogRepository` | `MedicationCatalogRepository` |
| `IDentalActCodeRepository` | `DentalActCodeRepository` |
| `IExpenseRepository` | `ExpenseRepository` (caisse) |
| `IWaitingListRepository` | `WaitingListRepository` (salle d'attente) |
| `ILabWorkOrderRepository` | `LabWorkOrderRepository` (dental-lab) |
| `IRecurringAppointmentRepository` | `RecurringAppointmentRepository` |

## External Services (`Services/`, `Storage/`, `Security/`, `Auth/`)

### Google Calendar (per-clinic two-way sync)
- **`GoogleCalendarService`** (`IGoogleCalendarService`, scoped) — low-level Google Calendar v3 client. Every
  method takes a `GoogleCalendarConnection` (refresh token + calendar id). Client id/secret come from
  `GoogleCalendar:ClientId`/`ClientSecret` config; the access token is refreshed per connection
  (`GoogleAuthUtils.RefreshAccessTokenAsync` → `oauth2.googleapis.com/token`); the built `CalendarService` is
  cached keyed on the refresh token. Times forced to UTC. Missing creds/token → `InvalidOperationException("…not
  configured")` (callers skip silently). Methods: Create/Update/Delete/GetEvents.
- **`GoogleCalendarSyncService`** (`IGoogleCalendarSyncService`, scoped) — business orchestration.
  `ResolveConnectionAsync(clinicId)` loads a clinic's own `Clinic.GoogleRefreshToken`/`GoogleCalendarId`
  (returns null → skip; **no shared cross-clinic account**).
  - **App → Google** (`SyncAppointmentToGoogleCalendarAsync`) — create/update/delete a Google event using the
    appointment's own clinic connection; persists/clears `Appointment.GoogleCalendarEventId`; skips busy slots;
    failures logged not thrown. This is the actively-used direction (inline on appointment create/update).
  - **Google → App** (`SyncGoogleCalendarToAppointmentsAsync`) — resolves the **caller's** clinic via
    `ICurrentClinicResolver` and scopes all reads/writes to it; pulls a -7..+90-day window; updates/links/creates
    appointments (may auto-create a placeholder patient). **No scheduled job** — reachable only via the manual
    `GoogleCalendarController` endpoint (runs with a clinic in scope).

### AI
- **`HuggingFaceAIService`** (`IHuggingFaceAIService`, scoped) — chat completions via the HuggingFace router
  (`router.huggingface.co/v1/chat/completions`, OpenAI-compatible). Model `HuggingFace:Model` (default
  `microsoft/Phi-3-mini-4k-instruct`); injects a clinic-context system prompt; retries once if the model is
  loading. Sole wired chat/AI backend (also backs the patient AI-summary endpoint).
- **`AIActionService`** (`IAIActionService`, scoped) — agentic layer: asks the AI to classify intent + extract
  params (JSON), with a regex fallback, then dispatches to MediatR commands/repos for `create_appointment`,
  `search_patient`, `view_patient`, `list_appointments`, `cancel_appointment`. Scoped to the caller's clinic via
  `IClinicContext` + `IUserRepository`.
- *(The Gemini `GoogleAIService` and the placeholder `PatientSummaryService` were removed as dead code.)*

### Connectivity (Local-mode offline UX)
- **`InternetProbe`** (`IInternetProbe`, **Singleton**) — judges the **server's** internet egress (LAN clients
  can't). `GET` to `Connectivity:ProbeUrl` with a linked timeout; 2xx/3xx ⇒ reachable. A shared `IMemoryCache`
  + `SemaphoreSlim` double-checked lock collapses a burst of pollers to one probe per TTL. Uses
  `IHttpClientFactory` (safe in a singleton). Registered unconditionally (harmless in Cloud).
- **`ConnectivityConfig`** — static accessors: `ProbeUrl` (default `https://www.google.com/generate_204`),
  `ProbeTimeoutSeconds` (3), `ProbeCacheSeconds` (5).

### Reminders — SMS/WhatsApp + recall (live outbound)
- **`IReminderChannelSender`** + `HttpSmsSender` (config-driven HTTP gateway, alphanumeric sender id) and
  `WhatsAppSender` (WhatsApp Business Graph API, pre-approved utility template — never free-text) over a shared
  **`HttpReminderChannelSender`** base (15s-bounded JSON POST → `Sent`/`TransientFailure`/`NotConfigured`).
  Senders read endpoint/identity/secret/template from the resolved settings, never config directly. All scoped;
  the API `NotificationJob` matches each due row to the sender whose `Channel` == the row's `NotificationType`.
- **`ReminderScheduler`** (`IReminderScheduler`, scoped) — enqueues/voids `Notification` outbox rows best-effort
  **post-commit** from the appointment handlers (swallow-and-log; never fails the core op). Also
  `ScheduleRecallAsync` (patient recall nudge — `Notification` with null appointmentId, distinct subject). French
  wording, Tunisia-local time formatting, per-clinic message template placeholders (`{patient}/{date}/{clinic}`).
- **`ReminderSettingsProvider`** (`IReminderSettingsProvider`, scoped) — resolves effective per-clinic settings
  (`ClinicReminderSettings` override ?? per-install `RemindersConfig`): channel toggles (`bool?` = inherit),
  identity, endpoint URLs, lead-time tiers, wording. Secrets decrypted in-process; a broken/rotated key ⇒
  channel treated as **not configured** (logged once per scope, never thrown). Memoized per-clinic per scope.
- **`ReminderSecretProtector`** (`IReminderSecretProtector`, **Singleton**) — purpose-scoped `IDataProtector`
  encrypts/decrypts per-clinic reminder secrets at rest.
- **`RemindersConfig`** — static accessors over the `Reminders` section (channels, lead times, min-lead,
  max-retries, SMS/WhatsApp URLs + template flags). Secrets (`Sms:ApiKey`, `WhatsApp:AccessToken`) expected from
  env, not committed config.
- **`ReminderSchedule`** (pure tiered send-time calc), **`ReminderPhone`** (+216 E.164 normalization + PII
  masking).
- **`WhatsAppOnboardingService`** (`IWhatsAppOnboardingService`, scoped, Cloud) — Meta Embedded-Signup: code→token
  exchange + app-subscribe + phone-register via Graph API, using `MetaConfig` (`Meta:AppId`/`AppSecret`,
  `Meta:GraphApiVersion`). Failures thrown as categorized `WhatsAppOnboardingException`.

### E-invoicing — TTN « El Fatoora »
- **`EInvoiceService`** (`IEInvoiceService`, scoped) — orchestrates one dispatch attempt for a `Queued` invoice:
  TEIF → sign → store signed XML → submit → persist outcome (Validated + QR cachet payload / permanent Rejected /
  bounded transient retry with backoff). **Best-effort, self-committing, never throws** (safe from a command or
  the outbox job). Picks the `ITtnClient` by the clinic's environment, falling back to sandbox.
- **`TeifXmlGenerator`** (`ITeifXmlGenerator`) — builds TEIF XML (version pinned in-code; exact XSD is an open
  question). **`XadesEInvoiceSigner`** (`IEInvoiceSigner`) — enveloped XMLDSig (RSA-SHA256) with the signing cert
  from the per-install `.local/` store (`TtnConfig`); **single cert per install** (not per clinic — noted as a
  multi-tenant constraint). **`QrCodeGenerator`** (`IQrCodeGenerator`, **Singleton**) — visible cachet QR.
- **`SandboxTtnClient`** (`ITtnClient`, env `Sandbox` — default) — validates any signed TEIF locally, returns a
  deterministic fake TTN id + receipt (whole pipeline exercisable offline). **`HttpTtnClient`** (`ITtnClient`,
  env `Production`) — best-effort OAuth2 + REST submit driven by `Ttn:*`; unconfigured/5xx ⇒ transient (invoice
  stays Queued). Both registered as a set; `EInvoiceService` keys them by `Environment`.
- **`TtnConfig`** — static accessors: cert path/password (`.local/teif-signing.pfx`, env `TTN_CERT_PASSWORD`),
  base/token URLs per environment, `Ttn:Username`/`ApiSecret` (env `TTN_API_SECRET`), `MaxAttempts` (5),
  `BackoffBaseSeconds` (60), `DispatchBatchSize` (20).

### File Storage (`IFileStorage`, scoped, mode-branched)
- **`Storage/MinioFileStorage`** (Cloud) — MinIO blob store; auto-creates bucket; key = custom path or
  `{guid}-{timestamp}`. Uses a singleton `IMinioClient`.
- **`Storage/LocalDiskFileStorage`** (Local) — blobs under `FileStorage:BasePath` (resolved install-relative via
  `LocalInstallPaths`); opaque relative keys; mirrors MinIO semantics (guid keys, deterministic custom-path
  overwrite, seekable download, idempotent delete, path-traversal-safe).

### PDF & CNAM
- **`PdfGenerationService`** (`IPdfGenerationService`, scoped) — QuestPDF (Community license). Renders French
  documents: `prescription` (ORDONNANCE), `liaison`, `certificat` (+ optional practitioner-cachet image loaded
  from storage, falling back to a plain signature line). **`honoraires` is rejected** (retired — issue an Invoice
  instead). **`bulletin-cnam` branches out** to the BS1 overlay renderer. Helpers: `CertificatTextBuilder`,
  `LiaisonContent`.
- **`CnamBs1BulletinRenderer`** (internal) — stamps `bulletin-cnam` data onto the genuine CNAM **BS1** form
  (`Assets/BS1.pdf`) at calibrated coordinates (2-page A4-landscape) using **PdfSharp**; fills only the
  dentist-relevant regions (IDU comb, régime/lien ticks, assuré/malade identity, the 6-row dental acts table incl.
  per-row `Doctor.CodeProfessionnelSante`, page-2 Cadre-de-soins ticks); >6 acts append extra pages; a
  missing/unreadable asset **fails fast** (French message). **`Bs1FontResolver`** (internal,
  `PdfSharp.Fonts.IFontResolver`) — process-wide sans-serif resolver installed idempotently before first render;
  fails fast if no OS font found.

### Auth
- **`Auth0ManagementService`** (`IAuth0ManagementService`, Cloud) — sets user `app_metadata` (`clinic_id`,
  `role`) via the Auth0 Management API; failures logged + swallowed.
- **`Auth/LocalAuthService`** (`ILocalAuthService`, Local) — PBKDF2 hash/verify via `PasswordHasher<User>`; HS256
  JWT issuance (claims `sub`/`clinic_id`/`role`/`jti` + optional `email`/`name`, 12h default); CSPRNG
  temp-password (unambiguous 12-char alphabet).
- **`Auth/LocalAuthConfig`** — resolves issuer/audience/lifetime and the per-install signing key: explicit
  `Auth:Local:SigningKey` (≥256-bit) else a generated 512-bit key persisted to `.local/signing-key`
  (`Auth:Local:SigningKeyPath` override); cached. `IsLocalMode(config)`. Same path used by issuer and the
  `Program.cs` validator so they can't drift.
- **`Auth/NoOpAuth0ManagementService`** (Local) — no-op `IAuth0ManagementService`.

### LAN hosting / install helpers (root namespace)
Static, unit-testable (referenced from API `Program.cs`/controllers; in the root namespace so `UnitTests` can
exercise them without the API):
- **`LocalRequest.IsLoopback(HttpContext)`** — "did this request come from the server machine?" (null
  `RemoteIpAddress` ⇒ true). Gates the first-run `setup` endpoint and the loopback-only Hangfire dashboard.
- **`CorsOrigins.Assemble/FromConfiguration`** — builds the credentialed CORS origin list (deduped,
  order-preserving). Cloud = single `FrontendUrl`; Local unions in `Cors:AllowedOrigins`.
- **`LocalInstallPaths`** — resolves `.local/`, `Files/`, `logs/` against the **install dir**
  (`AppContext.BaseDirectory`), not the CWD (a Windows service's CWD is `System32`). `Resolve`, `LocalDir`,
  `LocalFile(name)`.

### Certificate provisioning (`Security/CertificateProvisioner`) — Local, Phase 5
Self-generates LAN HTTPS trust material on first boot: a self-signed CA (10y) + a server leaf it signs (5y) whose
SANs cover the hostname, `localhost`, `127.0.0.1`, and every non-loopback IPv4. `EnsureServerCertificate()` is
**idempotent** (an existing loadable set is reused so the trusted CA is stable). Writes `.local/server.pfx`
(random password in `.local/server-cert-password`) for Kestrel + `.local/ca.crt` for the client installer.
**Not DI-registered** — constructed manually pre-`builder.Build()` in `Program.cs` (Kestrel needs the cert before
the container exists) and by the `ProvisionCertCommand` CLI.

### Backup (`Services/PgDumpBackupService`, `IBackupService`, scoped) — Local, Phase 5
One-click "Backup now": `pg_dump.exe` custom-format (`-Fc`, `pg_restore`-able) then a recursive file-storage copy,
both into a **unique** timestamped `clinic-backup-<yyyyMMdd-HHmmss>[-N]` folder (DB first). The codebase's only
`Process` shell-out: argument **list** (no injection), password via `PGPASSWORD` env (never on the cmdline/logs),
bounded timeout that kills the process tree. **Fails loud** (missing `pg_dump`, unwritable dest, insufficient free
space — pre-check factors the live DB size via `pg_database_size` — or a non-zero exit → `InvalidOperationException`
with a French operator message); a partial folder is deleted before rethrow. Registered unconditionally; on Cloud
a call fails cleanly ("pg_dump introuvable").

## DI Registration (`Extensions.cs` — `AddInfrastructure(services, configuration)`)
- `ApplicationDbContext` via `UseNpgsql("DefaultConnection")`; `IUnitOfWork` scoped; all repositories scoped.
- `AddHttpClient()`; `AddMemoryCache()`; `IInternetProbe → InternetProbe` (**Singleton**).
- **`IFileStorage` (scoped), mode-branched**: Local → `LocalDiskFileStorage` (base path via
  `LocalInstallPaths.Resolve(FileStorage:BasePath ?? "Files")`); Cloud → `MinioFileStorage` if `MinIO:*` present
  (with a singleton `IMinioClient`), else a stub that throws on use.
- **Auth-mode-branched**: Local → `ILocalAuthService`/`LocalAuthService` + `IAuth0ManagementService`/`NoOp…`;
  Cloud → real `Auth0ManagementService`. (`ILocalAuthService` is registered in both modes.)
- **Data Protection** — `AddDataProtection().SetApplicationName("ClinicManagement")`, key ring persisted to a
  mode-resolved dir (Local → `.local/dataprotection-keys`, DPAPI machine-scoped on Windows; Cloud →
  `DataProtection:KeyRingPath` if set, else framework default) + `IReminderSecretProtector` (**Singleton**).
- **Reminders** — `IReminderSettingsProvider`, `IReminderScheduler` (scoped); `IReminderChannelSender` ×2
  (`HttpSmsSender`, `WhatsAppSender`, scoped); `IWhatsAppOnboardingService` (scoped).
- **E-invoicing** — `ITeifXmlGenerator`, `IEInvoiceSigner`, `IEInvoiceService`, `ITtnClient` ×2 (Sandbox + Http)
  scoped; `IQrCodeGenerator` **Singleton**.
- `IGoogleCalendarService`, `IGoogleCalendarSyncService`, `IPdfGenerationService`, `IHuggingFaceAIService`,
  `IAIActionService`, `IClinicCatalogSeeder`, `IBackupService` — all scoped.
- **Not registered here:** `CertificateProvisioner` (constructed manually pre-Build in `Program.cs`);
  `AdminPasswordRecoveryService` (console-only). **Retired:** `IGoogleTokenStore`/`FileGoogleTokenStore` — Google
  refresh tokens now live per-clinic on the `Clinic` entity.
- **Depends on (registered in the API layer):** `ICurrentClinicProvider` (DbContext query filters),
  `ICurrentClinicResolver`, `IClinicContext`.

## Config keys consumed (names only)
`ConnectionStrings:DefaultConnection`; `FileStorage:BasePath`; `MinIO:{Endpoint,AccessKey,SecretKey,BucketName,
UseSSL}`; `GoogleCalendar:{ClientId,ClientSecret}` (per-clinic refresh token/calendar id live on `Clinic`);
`HuggingFace:{ApiKey,Model}`; `Auth0:{Domain,ManagementApi:ClientId,ManagementApi:ClientSecret}`;
`Auth:Mode` (`Cloud`|`Local`); `Auth:Local:{SigningKey,SigningKeyPath,Issuer,Audience,TokenLifetimeMinutes}`
(all optional; key else generated `.local/signing-key`); `Connectivity:{ProbeUrl,ProbeTimeoutSeconds,
ProbeCacheSeconds}`; `Cors:AllowedOrigins` (Local); `Reminders:{Channels,LeadTimesHours,MinLeadHours,MaxRetries,
Sms:{ApiUrl,SenderId,ApiKey},WhatsApp:{ApiUrl,PhoneNumberId,TemplateName,AccessToken,TemplateLanguage,
TemplateHasBodyParam}}`; `Ttn:{CertPath,CertPassword,Username,ApiSecret,MaxAttempts,BackoffBaseSeconds,
DispatchBatchSize,Sandbox:{BaseUrl,TokenUrl},Production:{BaseUrl,TokenUrl}}`; `Meta:{AppId,AppSecret,
GraphApiVersion}`; `DataProtection:KeyRingPath` (Cloud); `Backup:{PgDumpPath,DefaultDestination,TimeoutSeconds}`.
Secrets are expected from env, not committed config (e.g. `Reminders__Sms__ApiKey`, `Reminders__WhatsApp__
AccessToken`, `Meta__AppSecret`, `TTN_CERT_PASSWORD`, `TTN_API_SECRET`). *(HTTPS/Kestrel hosting keys —
`Https:*`, `Hosting:*` — are read in API `Program.cs`, not here.)*

> When code changes, update this file so the map stays accurate.
