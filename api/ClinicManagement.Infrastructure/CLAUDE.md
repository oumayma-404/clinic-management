# ClinicManagement.Infrastructure

Infrastructure layer (Clean Architecture). Implements the outbound interfaces declared in Domain
(`Domain/Repositories`) and Application (`Application/Common/Interfaces`): EF Core/PostgreSQL data access +
multi-tenant query filters, repository implementations, mode-branched file storage (MinIO vs local disk),
per-clinic Google Calendar two-way sync, HuggingFace AI + agentic action dispatch, SMS/WhatsApp reminders
(+ per-clinic settings, encrypted secrets), TTN « El Fatoora » e-invoicing, CNAM BS1 bulletin + French PDF
rendering, Auth0 management (Cloud) / local JWT auth (Local), and — for offline installs — `pg_dump` backup,
self-generated HTTPS trust material, and per-clinic reference-catalog seeding. All wiring lives in
`Extensions.cs` (`AddInfrastructure`).

> Behavior is gated by the resolved **deployment profile**, not by an auth-mode boolean:
> `Deployment/DeploymentProfile.Resolve(config)` → `SelfHostedLan` (offline LAN, self-issued JWT, local-disk
> storage) | `HostedMultiTenant` | `CloudBrowser` (Auth0, MinIO), each exposing **a capability per question**
> (`UsesLocalAccounts`, `UsesDiskStorage`, `RunsAsWindowsService`, `SelfSignsCertificate`,
> **`AllowsSelfRegistration`**, …). ⚠️ That last one (US-3) is the only capability where `HostedMultiTenant`
> parts company with `SelfHostedLan` while sharing its login provider — which is precisely why joining by clinic
> code could not stay gated on `UsesLocalAccounts`. `Deployment:Profile`
> names it; **absent, it derives from `Auth:Mode`** exactly as the old boolean did (`Local` → `SelfHostedLan`, else
> `CloudBrowser`), so existing installs need no config edit. ⚠️ `LocalAuthConfig.IsLocalMode` survives only as that
> derivation — a branch anywhere else asking it fails `DeploymentProfileCoverageTests`.

## EF Core Persistence (`Persistence/`)

- **`AuditSaveChangesInterceptor.cs`** — writes the audit ledger (`adoption-qa-i-access-control-and-audit` I6).
  One row per mutated **aggregate root**, carrying the actor (`IAuditActorProvider`), the clinic, the entity, the
  action, and for updates/deletes a compact changed-field summary.
  **Why an interceptor and not the handlers:** attribution wired into commands is attribution a new command can
  forget, and the ledger's whole value is that a missing row is indistinguishable from a mutation that never
  happened. Every write funnels through `SaveChangesAsync`, so this sees them all by construction.
  ⚠️ **The two-phase shape is forced, not stylistic.** Rows are *collected* in `SavingChangesAsync` — a `Deleted`
  entry is **gone from the change tracker** afterwards, so its id and identifying values only exist then — and
  *written* in `SavedChangesAsync` through a **separate** `ApplicationDbContext` from its own scope, because an
  audit failure must never roll back a clinical or money operation (the same contract as `INotificationGenerator`)
  and rows added to the caller's context would ride the caller's transaction. A separate context is also why it is
  **not a nested save**: it never re-enters the context it observes, which would recurse.
  ⚠️ **Known imprecision, stated rather than hidden:** `SavedChangesAsync` fires when `SaveChanges` returns, which
  for the few handlers opening an explicit `IUnitOfWork.BeginTransactionAsync` is *before* the commit — so a
  rolled-back transaction leaves its audit row behind. That is the deliberate direction of the error:
  over-recording an attempt is a reading problem, under-recording a real change is the failure the ledger exists to
  prevent.
  **Aggregate roots only**, derived from `AggregateRoot<>` rather than a name list (so a new aggregate is audited
  the day it is written) — saving one invoice touches its lines and payments, and a row per tracked entity would
  answer « qui a annulé cette facture ? » with eleven rows for one action. Two exclusions, both structural:
  `AuditEntry` itself (it would audit its own writes forever) and `Notification`, the outbound reminder outbox
  whose **minutely** dispatcher would bury a clinic's real history in machine noise within a day (it already has a
  visible delivery log on « Rappels »).

- **`ApplicationDbContext.cs`** — the single `DbContext`. Injects an optional `ICurrentClinicProvider?` (null
  at design-time / in manual construction → filters inactive). Key mechanisms:
  - **Multi-tenant global query filters** — the second isolation layer, not the authoritative check (handlers
    still verify the DB-resolved `User.ClinicId`, which remains the *only* layer for the seven `ClinicId`-less
    clinical tables). ⚠️ **They were fail-open, and therefore inert, until `multi-tenant-cloud` US-2**: no clinic
    in scope meant no filter, so every path that failed to establish one read every clinic and nothing said so.
    The two instance properties are now `IsSystemWide`/`ScopedClinicId` (read through the instance so EF treats
    them as per-query parameters, never baked into the cached model), fed by `ICurrentClinicProvider` →
    **`ITenantScope`**, and the shape is `IsSystemWide || ClinicId == ScopedClinicId`. **Only a scope that
    declared `UseSystemWide(reason)` returns everything**; an `Unset` scope compares against `Guid.Empty` and
    returns **nothing**, which is what makes a forgotten scope loud instead of a silent cross-clinic read.
    `ITenantScope` + a **floor** `ICurrentClinicProvider` are registered in `Extensions.cs` (not `AddApplication`)
    precisely so the console verbs, which build their container from `AddInfrastructure` alone, resolve both and
    have to declare themselves — before US-2 they had no provider at all and read everything by accident. A
    context constructed with **no** provider (the design-time factory, hand-built contexts in tests) still reads
    everything: that is a different case from `Unset`, and `TenantScopeFilterTests` pins both.
    `HasQueryFilter` scopes the directly-clinic-owned
    aggregate roots: `Patient`, `Appointment`, `ProcedureType`, `StaffNotification`, `Invoice`,
    `TreatmentPlan`, `ClinicReminderSettings` (by `Id` = clinic id), `CnamNomenclatureEntry`,
    `CnamLetterValue`, `Medication`, `DentalActCode`, `Expense`, `WaitingListEntry`, `LabWorkOrder`,
    `RecurringAppointment`, **`Doctor`**, **`StockItem`** (the last two were the only clinic-owned roots left unfiltered - and `StockItem`'s own child `StockMovement` was filtered while its *parent* was not). **`User`/`Clinic` are deliberately unfiltered** (auth/join flows resolve them
    before a clinic context exists). Child entities (`InvoiceLine`, `Payment`, `Installment`,
    `TreatmentPlanItem`, `MedicationActiveIngredient`, `DentalRecordTooth/Act`, `ToothState`,
    `NotificationRead`, **`StockBatch`**, **`ProcedureTypeMaterial`**) carry no filter — reached only through a filtered parent / scoped by `UserId`.
    ⚠️ **`AuditEntries` carries no filter either, and that one is not an omission**: its `ClinicId` is
    *nullable* (a job or console verb can mutate a row with no clinic derivable from it), so a filter comparing it
    to the scoped id would silently hide exactly the unattributed rows an owner most needs — and the interceptor
    writes on a context whose clinic scope belongs to the request being audited, not to the row.
    `GetAuditEntriesQuery` filters by the caller's DB-resolved clinic explicitly, which is the authoritative check
    everywhere in this codebase anyway.
    Cross-clinic paths either call `IgnoreQueryFilters()` (the per-clinic seeder does, on every read — it is
    structurally immune to the scope rather than dependent on it) or **declare `UseSystemWide(reason)`** (the five
    recurring jobs, the startup scope, the three DB-touching verbs). « Runs with no clinic in scope » is no longer
    a way to read across clinics — it is how a read returns nothing.
  - **Money precision by convention** - `ConfigureConventions` sets `HavePrecision(18, 3)` for every `decimal`, and the **26 redundant `HasColumnType("decimal(18,3)")` calls across 17 configuration files were deleted** (AC-P4.37). The deletions are load-bearing: `GetColumnType()` returns an explicit annotation verbatim and bypasses facet-derived store types, so with them in place the convention emits **zero** `AlterColumn`s and `StockItem.UnitPrice` stays at 2 decimals - the exact bug it looks like it fixes. `Clinic.VatRate` and `Invoice.VatRate` keep `(5,2)` via a retained annotation with the reason at each site: they are rates, not money (AC-P4.38). `verify-schema` asserts both halves.
  - **UTC everywhere**: `OnModelCreating` installs a global value converter forcing every `DateTime`/`DateTime?`
    to UTC (PostgreSQL `timestamp with time zone`), and `SaveChanges`/`SaveChangesAsync` re-run
    `ConvertDateTimesToUtc()` on Added/Modified entries (belt-and-suspenders). `Unspecified` is assumed UTC.
  - `OnConfiguring` ignores `PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning` (expected
    noise from filtering roots but not their children).
- **`ApplicationDbContextFactory.cs`** — `IDesignTimeDbContextFactory` for `dotnet ef` (reads the API project's
  connection string).
- **`MoneyReconciliationReader.cs`** / **`SchemaVerificationReader.cs`** — the read sides of the two console
  report verbs (`reconcile-money`, `verify-schema`). Both are read-only and cross-clinic: their verbs build a
  container from `AddInfrastructure` alone, so no `ICurrentClinicProvider` exists, the context's optional provider
  is null and every global clinic filter is inactive — no `IgnoreQueryFilters()` needed. `SchemaVerificationReader`
  is the only class here that queries **PostgreSQL's own catalog** (`pg_extension`, `pg_constraint`, `pg_index`,
  `information_schema.columns`) over raw ADO, because those views are not in the EF model at all; it also projects
  the **model's** declared indexes/FKs/precisions so the service can diff the two sides. Note it excludes owned-type
  and table-splitting identity "foreign keys" (`Patients(Id) -> Patients`) — PostgreSQL has no such constraint and
  reporting them produced 7 false positives on the first run.
- **`UnitOfWork.cs`** — `IUnitOfWork`: wraps `SaveChangesAsync` + `BeginTransaction/Commit/Rollback`.
  Repositories only stage changes; callers commit via the UoW.
- **Reference-catalog seeds + seeder** — `CnamCatalogSeed`, `MedicationCatalogSeed`, `DentalActCatalogSeed`
  (shared single-source-of-truth defaults) + **`ClinicCatalogSeeder`** (`IClinicCatalogSeeder`): clones the
  defaults into each clinic on creation and a startup backfill (`SeedAllClinicsAsync`, called from the API's
  `DeferredStartupService`/`Program.cs`). Uses the `DbContext` directly with no clinic in scope; idempotent
  per catalog; deterministic per-clinic GUIDs.
  ⚠️ **Seeding an empty catalog is no longer the whole job** (`adoption-qa-k`): a clinic seeded before a shipped
  default was found to be *wrong* still holds the wrong value in its own rows, and « the catalog already has rows,
  skip it » would leave it there forever. **`CorrectSupersededDefaultsAsync`** therefore runs after the four
  seed-if-empty blocks, on every clinic, every startup — today for the CNAM **valeurs de la lettre clé** (the seed
  shipped `Cd 7` / `Cds 10` / `D 1,200` against the convention in force since 01/01/2021, which fixes 30,000 /
  45,000 / 3,000, so **every** reimbursement figure shown to a patient was understated by 60–75 %) and the
  **Prothèse accord-préalable** flag (cleared since April 2019).
  ⚠️ **The predicate is what makes it safe, and `IsProvisional` alone is the wrong one.** All three terms are
  required: `UpdatedAt == null` (untouched since seeding), still provisional, **and** the row still holding the
  exact superseded figure (`CnamCatalogSeed.SupersededLetterValue` / `DentalActCatalogSeed.
  SupersededAccordPrealable`, both derived from the seed table so there is no second hand-written copy of the old
  numbers). `CnamLetterValue.SetValue` stamps `UpdatedAt` but does **not** clear the provisional flag — only
  `Confirm()` does — so an admin who typed their own valeur and never pressed « Confirmer » still reads
  `IsProvisional = true`, and correcting on that flag would overwrite the one entry that must never be touched.
  Divergence that survives the predicate is *offered* on `/cnam-nomenclature` instead
  (`CnamLetterValueDto.ConventionValue`), never applied silently. Self-terminating: both mutators stamp
  `UpdatedAt`, so a corrected row fails the predicate on every later startup.
  The convention's own table lives in **Domain** (`Services/CnamConventionTariffs`) because the seed
  (Infrastructure) and the letter-values read (Application) both need it; `ValueFor` returns **null** for a lettre
  clé the convention text did not settle (`Vd`/`Rd`), which is what keeps those out of the correction entirely.

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
45 migrations, applied automatically at startup (`context.Database.Migrate()` in API `Program.cs`). Early ones
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
- **`20260731235500_AddProcedureTypeCategory`** — gives `ProcedureType` a real `Category` column (100 chars,
  nullable) + a `(ClinicId, Category, Name)` index, and **moves** into it the disciplines that had been living in
  `Description` — clearing the description it copies from, because « Endodontie » was never a *description* of
  « Traitement de canal » and leaving it would preserve the original mistake behind the column added to fix it.
  Only descriptions exactly matching a canonical label are touched, so real prose survives. **Hand-written**, not
  scaffolded: `dotnet ef` cannot load a freshly-built assembly on the dev machine (Smart App Control,
  `0x800711C7`), so the model snapshot and the paired Designer were updated by hand — the shape is checked against
  PostgreSQL's catalog by `verify-schema`, which matches indexes on table + ordered columns rather than by name.
- **`20260804120000_AddChequeDetailsToPayments`** (L8) — `Payments` and `InstallmentPayments` each gain
  `ChequeNumber` (50), `ChequeBankName` (200) and `ChequeDueDate`, plus a **partial** index on the due date.
  Purely additive: six nullable columns, no backfill, no row rewritten — a cheque recorded before today
  legitimately has no number, which is different from « we have no cheques » and is why they are nullable rather
  than defaulted to `''`. ⚠️ **No CHECK constraint**: « cheque details only on a cheque » lives in
  `ChequeDetails.For`, and a second copy here would surface as a 500 instead of the French refusal — so
  `verify-schema` gained `cheque-details-only-on-cheques` to *verify* it instead, over both ledgers.
  ⚠️ Both index filters key on `"ChequeDueDate" IS NOT NULL`, **not** `"Method" = 1`: equally selective by that
  invariant, and the enum form would bake an ordinal into SQL where no compiler checks it. **Hand-written** for the
  same WDAC reason as the migration above; the delta is six columns and two indexes, small enough to verify by eye.
- **`20260807102000_AddClinicSignups`** (`clinic-self-signup`) — one new table for the pending signups: fourteen
  columns, a unique index on `Email` (« one row per address » held by the database, not by a handler winning a
  race), a unique index on `TokenHash` (the verification lookup), and a `(ConsumedAtUtc, ExpiresAtUtc)` index for
  the purge. Purely additive; nothing existing is touched. ⚠️ **No `ClinicId` and therefore no FK** — that is the
  table's whole point — so nothing in the schema cascades these rows away and only the opportunistic purge on the
  signup path deletes one; `verify-schema`'s `clinic-signup-has-no-orphans` is what makes a deployment that
  stopped trimming visible. **Hand-written** (migration + `.Designer.cs` + snapshot) for the same reason as the
  two above, here because the running API held `ClinicManagement.API/bin`; the Designer was derived mechanically
  from the updated snapshot rather than retyped.

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
| `IProcedureTypeRepository` | `ProcedureTypeRepository` (`GetFilteredAsync` gained a `category` argument — compared on the **canonical** spelling so a stale « endodontie » link still matches — and orders by category then name with `Category == null` **first in the predicate** so unfiled acts land at the *end*, a decision this read makes rather than inheriting from PostgreSQL's NULLS-LAST default; `GetCategoriesAsync` returns the clinic's distinct categories, **including those of deactivated acts**, since a discipline the practice files work under does not stop being one because one act in it was archived) |
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
| `IDeviceRegistrationRepository` | `DeviceRegistrationRepository` (P6). Its `GetByTokenAcrossClinicsAsync` is the only deliberately `IgnoreQueryFilters()` read here besides the seeder's — see the Domain guide for why that is *required* rather than lax |
| `IClinicSignupRepository` | `ClinicSignupRepository` (`clinic-self-signup`). The one repository with **no** `IgnoreQueryFilters()` and no need of one: `ClinicSignup` carries no `ClinicId`, so no filter is configured for it. Its `PurgeSpentAsync` **stages** removals rather than `ExecuteDelete`, so the trim rides the caller's single `SaveChangesAsync` instead of committing even when the signup it accompanies is refused |
| `IPushDeliveryRepository` | `PushDeliveryRepository` (P6). The due scan mirrors `NotificationRepository`'s per-clinic fairness bound predicate for predicate |

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

### OS push notifications (`mobile-native-shells` P6)
- **`OsPushAvailability`** (`IOsPushAvailability`, **Singleton**) — the one « can this install push to this
  platform? »: `DeploymentProfile.PermitsOsPush(platform)` (kind only) **AND** `PushConfig`'s credentials. It also
  owns the French *reason* a platform is unavailable, so the registration refusal, the parked row's message and the
  settings sentence are one wording rather than three.
- **`PushConfig`** + **`ResolvedPushCredentials`** — static accessors over the `Push` section, on
  `RemindersConfig`/`TtnConfig`'s pattern. **Per install, not per clinic**: one mobile app means one Firebase
  project and one Apple team. Secrets expected from env (`Push__Fcm__ServiceAccountKey`,
  `Push__Apns__PrivateKey`); `IsConfigured` is the single sendability predicate every layer reads.
- **`IPushSender`** + `FcmPushSender`/`ApnsPushSender` over a shared **`HttpPushSender`** (15 s-bounded, never
  throws). Four outcomes, and **`TokenInvalid` is load-bearing**: FCM's `UNREGISTERED` / APNs' `410 Gone` is
  terminal *per device*, so the dispatcher fails the row **and** deactivates the registration — as a transient
  failure it would burn every future notification's retry budget for an app that no longer exists. Recognising it
  is the one genuinely per-platform thing, hence the abstract `IsTokenInvalid` hook. APNs carries its topic and
  priority as **headers**, which is why the base takes an extra-headers argument.
- **`PushNotificationGeneratorDecorator`** (`INotificationGenerator`) — queues push alongside the in-app feed by
  **decorating** the generator: one hook reaches every category the feed has or will have, so a notification added
  later cannot be the one that silently never pushes (`fixes-dont-propagate`). The inner generator is awaited
  **first** and the fan-out runs inside a swallow-and-log — the whole chain is a post-commit side effect of an
  operation that already committed, so a push failure must cost neither the operation nor the feed row (AC-55).
  ⚠️ It lives **here rather than beside `NotificationGenerator`** because it reads the operator's quiet-hours
  window from configuration — the same reason `ReminderScheduler`, the other post-commit best-effort writer
  implementing an Application interface, is here. ⚠️ Its audience is the feed's rule (actor excluded, a targeted row
  only its target, else the clinic) with **one deliberate departure**: inactive accounts are dropped. The feed's SQL
  does not test `IsActive` because it does not need to — a deactivated account is refused on every request and can
  never read the feed — but its *device* stays registered, because somebody who was switched off does not sign out,
  and a banner on a former employee's phone is the difference that would be visible.
- **`ReminderSchedule.DeferPastQuietHours`** — the push floor, sharing this file's wrapping-window arithmetic.
  ⚠️ **Later only, the opposite of `ApplyQuietHours` beside it**, which prefers earlier: that one places a reminder
  about a *future* visit, so 21:00 the previous evening still reaches the patient; a push announces something that
  has *just happened*, so the only choices are 08:00 or a banner at 03:00.

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
  question). **`XadesEInvoiceSigner`** (`IEInvoiceSigner`) — enveloped XMLDSig (RSA-SHA256). It reads no
  configuration and touches no disk: the certificate arrives as **bytes** on a `ResolvedTtnIdentity`, so signing is
  a pure synchronous transform. **`QrCodeGenerator`** (`IQrCodeGenerator`, **Singleton**) — visible cachet QR.
- **`TtnIdentityProvider`** (`ITtnIdentityProvider`, scoped, `multi-tenant-cloud` US-4) — **the single answer to
  « whose certificate signs this clinic's invoices? »**: the clinic's own (`Clinic.TtnUsername` /
  `TtnApiSecretEncrypted` / `TtnCertificateKey` / `TtnCertificatePasswordEncrypted`, the PFX fetched from
  `IFileStorage`, the two secrets decrypted through `ITtnSecretProtector`), else the per-install pair — and that
  fall-back exists **only** where `DeploymentProfile.SharesInstallWideTtnIdentity` holds, i.e. `SelfHostedLan`,
  the one topology where « per install » and « per clinic » name the same thing.
  It replaced the constraint this file used to record as a fact: one `.local/teif-signing.pfx` for the whole
  install meant that in a multi-clinic deployment **every** clinic's e-invoices were signed with the same
  qualified identity — and a TEIF signature attests *who issued* the invoice, so on a hosted backend that is a
  false legal declaration, made irreversible by TTN validation.
  ⚠️ **It is a provider rather than an `if` in the signer because the identity has two consumers** — the signer
  wants the certificate, `HttpTtnClient` wants the credentials — and `EInvoiceService` resolves it **once** per
  dispatch and hands the same object to both, which is what makes « signed as clinic A, filed under clinic B »
  unreachable. ⚠️ **A clinic that HAS a certificate never silently falls back to the install's**, not even when
  the blob is missing: substituting an identity for a clinic that was explicitly given one is the exact failure
  being prevented. Every refusal is an `InvalidOperationException` with a French operator message —
  `EInvoiceService`'s existing catch turns that into a queued retry with the reason on the row, so nothing is a
  new error channel. ⚠️ **Nothing writes those four columns yet** (the admin surface is owed), so `verify-schema`'s
  **`ttn-identity-is-complete`** is the only guard a hand-populated row has.
- **`TtnSecretProtector`** (`ITtnSecretProtector`, **Singleton**) — `ReminderSecretProtector`'s twin on its own
  purpose (`ClinicManagement.TtnSecrets.v1`). Deliberately not the reminder protector: a Data-Protection *purpose*
  exists to stop one subsystem's ciphertext being read by another. Its key ring surviving a redeploy is what
  `DataProtection:KeyRingPath` guarantees (required in `HostedMultiTenant` since US-6) — which is why that step
  had to land **before** this one.
- **`SandboxTtnClient`** (`ITtnClient`, env `Sandbox` — default) — validates any signed TEIF locally, returns a
  deterministic fake TTN id + receipt (whole pipeline exercisable offline). **`HttpTtnClient`** (`ITtnClient`,
  env `Production`) — best-effort OAuth2 + REST submit driven by `Ttn:*`; unconfigured/5xx ⇒ transient (invoice
  stays Queued). Both registered as a set; `EInvoiceService` keys them by `Environment`.
- **`TtnConfig`** — static accessors: cert path/password (`.local/teif-signing.pfx`, env `TTN_CERT_PASSWORD`),
  base/token URLs per environment, `Ttn:Username`/`ApiSecret` (env `TTN_API_SECRET`), `MaxAttempts` (5),
  `BackoffBaseSeconds` (60), `DispatchBatchSize` (20).
  ⚠️ Since US-4 the four **identity** accessors describe the *per-install fall-back only* — read them anywhere but
  `TtnIdentityProvider` and you have written a second, disagreeing answer to « whose certificate is this? ». The
  endpoint and outbox accessors are unaffected: TTN is one national platform, so its URLs and the retry policy are
  per install by nature.

### File Storage (`IFileStorage`, scoped, mode-branched)
- **`Storage/ClinicStorageKey`** (`multi-tenant-cloud` US-5) — **the single composer of a new blob's key**, used by
  both backends: `clinics/{clinicId}/` then the caller's clinic-relative path or a unique `{guid}-{timestamp}` leaf.
  Before US-5 « which clinic owns this blob » had **two** answers — four upload sites prefixed a path of their own
  with a bare `{clinicId}/` (logo, cachet, e-invoice artifacts) while four wrote a flat guid with no clinic in it at
  all — and a third was one new upload away. Both `IFileStorage.UploadAsync` overloads therefore **require** a
  `Guid clinicId`, so an unprefixed key is not something a caller can write; `ClinicStorageKeyTests` derives that
  assertion off the interface rather than listing today's overloads.
  ⚠️ **The clinic is a parameter, not read off the ambient `ITenantScope`** — the e-invoice outbox uploads under
  `UseSystemWide` (no clinic in scope at all) and would have written an unattributed key, silently.
  ⚠️ **Reading is deliberately not symmetrical**: `DownloadAsync`/`DeleteAsync` pass the stored key through
  **verbatim**, so a row written before US-5 keeps resolving with **no backfill** (amendment M2). Composing on the
  read side would strand every one of them. A path that would climb out of its clinic is refused *in the composer*,
  so MinIO — which has no traversal semantics and would have stored the literal name — refuses it too.
- **`Storage/MinioFileStorage`** (Cloud + hosted) — MinIO blob store; auto-creates bucket. Uses a singleton
  `IMinioClient`.
- **`Storage/LocalDiskFileStorage`** (Local) — blobs under `FileStorage:BasePath` (resolved install-relative via
  `LocalInstallPaths`); opaque relative keys; mirrors MinIO semantics (the same composed keys, deterministic
  overwrite at a given path, seekable download, idempotent delete, path-traversal-safe).
- **`ProbeAsync` (both, multi-tenant-cloud US-6)** — the reachability check behind `/health`. MinIO asks whether the
  bucket exists (one call exercising DNS, endpoint, TLS and credentials, storing nothing); ⚠️ a **missing** bucket is
  reported as reachable-but-unusable rather than unreachable, because the two have different operator answers and
  `UploadAsync` creates it on demand anyway. Local disk creates the base folder and then **writes** a per-attempt probe
  file, deleting it in a `finally` — the write half is the point: an unmounted volume, a full disk and a folder the
  service account cannot write to all present as an existing directory, and every one of them breaks the first upload
  rather than an existence check.

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
- `ApplicationDbContext` via `UseNpgsql("DefaultConnection")` **+ `AddInterceptors(AuditSaveChangesInterceptor)`**;
  `IUnitOfWork` scoped; all repositories scoped.
  ⚠️ The interceptor is **scoped** and resolved through the `AddDbContext((provider, options) => …)` overload, not
  registered as an instance: it holds per-request state (the actor, and the rows collected between the two save
  phases), so a singleton would leak one request's actor into another's rows. It is built by an explicit factory so
  its *optional* `ICurrentClinicProvider` is resolved with `GetService`, not `GetRequiredService` — a console verb
  has none by design, and relying on the container's handling of a defaulted constructor parameter would make that
  work by accident.
- **`IAuditActorProvider` floor** — `TryAddScoped<IAuditActorProvider, ProcessAuditActorProvider>()`. A no-op in
  the API (`AddApplication` runs first in `Program.cs` and registers the real claims-reading `AuditActorProvider`);
  it exists for the console verbs, which build their container from `AddInfrastructure` alone and would otherwise
  fail to resolve a `DbContext` at all.
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
- **E-invoicing** — `ITeifXmlGenerator`, `IEInvoiceSigner`, `IEInvoiceService`, `ITtnIdentityProvider`,
  `ITtnClient` ×2 (Sandbox + Http) scoped; `IQrCodeGenerator` + **`ITtnSecretProtector`** Singleton.
- `IGoogleCalendarService`, `IGoogleCalendarSyncService`, `IPdfGenerationService`, `IHuggingFaceAIService`,
  `IAIActionService`, `IClinicCatalogSeeder`, `IBackupService` — all scoped.
- **Not registered here:** `CertificateProvisioner` (constructed manually pre-Build in `Program.cs`);
  `AdminPasswordRecoveryService` (console-only). **Retired:** `IGoogleTokenStore`/`FileGoogleTokenStore` — Google
  refresh tokens now live per-clinic on the `Clinic` entity.
- **Tenant scope (US-2)** — `AddScoped<ITenantScope, TenantScope>()` plus a `TryAddScoped` **floor** for
  `ICurrentClinicProvider`, both here rather than in `AddApplication` so the seven console verbs (container from
  this method alone) can declare their scope and have it honoured. `AuditSaveChangesInterceptor` now resolves the
  provider with `GetRequiredService`, since the floor means a missing registration is a bug rather than the
  console verbs' normal state.
- **Depends on (registered in the API layer):** `ICurrentClinicResolver`, `IClinicContext`, and the
  `TenantScopeMiddleware` that sets the per-request scope.

## Config keys consumed (names only)
`ConnectionStrings:DefaultConnection`; `FileStorage:BasePath`; `MinIO:{Endpoint,AccessKey,SecretKey,BucketName,
UseSSL}`; `GoogleCalendar:{ClientId,ClientSecret}` (per-clinic refresh token/calendar id live on `Clinic`);
`HuggingFace:{ApiKey,Model}`; `Auth0:{Domain,ManagementApi:ClientId,ManagementApi:ClientSecret}`;
**`Deployment:Profile`** (`SelfHostedLan`|`HostedMultiTenant`|`CloudBrowser`; absent ⇒ derived from `Auth:Mode`, an
unrecognised value **fails startup loud**); `Auth:Mode` (`Cloud`|`Local`);
`Auth:Local:{SigningKey,SigningKeyPath,Issuer,Audience,TokenLifetimeMinutes}`
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
