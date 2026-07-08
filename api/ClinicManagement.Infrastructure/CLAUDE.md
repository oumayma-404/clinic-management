# ClinicManagement.Infrastructure

Infrastructure layer (Clean Architecture). Implements the persistence + external-integration concerns declared as interfaces in the Domain and Application layers: EF Core/PostgreSQL data access, repository implementations, file storage (MinIO + local), Google Calendar two-way sync, AI services (HuggingFace, Google AI, AI action dispatch), notifications, PDF generation, and Auth0 management. All wiring lives in `Extensions.cs` (`AddInfrastructure`).

## EF Core Persistence

- **`Persistence/ApplicationDbContext.cs`** — single `DbContext`. `DbSet`s: Clinics, Users, Doctors, Patients, Appointments, Notifications, PatientFiles, PatientFlags, RecurringAppointments, StockItems, ProcedureTypes, PatientMedicalHistories, PatientFamilyHistories, DentalRecords, DentalRecordTeeth, PatientFolders, MedicalDocuments.
  - `OnModelCreating` calls `ApplyConfigurationsFromAssembly` (auto-discovers all `IEntityTypeConfiguration`s), then installs a **global value converter** forcing every `DateTime`/`DateTime?` to UTC (PostgreSQL `timestamp with time zone` requires UTC).
  - `SaveChanges`/`SaveChangesAsync` are overridden to run `ConvertDateTimesToUtc()` on Added/Modified entries — belt-and-suspenders UTC normalization. **Convention: all dates are UTC everywhere.**
- **`Persistence/ApplicationDbContextFactory.cs`** — `IDesignTimeDbContextFactory` for `dotnet ef` CLI. Reads connection string from the API project's `appsettings.json` (path `../ClinicManagement.API`).
- **`Persistence/UnitOfWork.cs`** — implements `IUnitOfWork` (Application layer). Wraps `SaveChangesAsync` + `BeginTransaction/Commit/Rollback`. Repositories do NOT call `SaveChanges`; callers (handlers/services) commit via the UoW.

### Entity Configurations (`Persistence/Configurations/`)
One `IEntityTypeConfiguration<T>` per aggregate. Common conventions: `Id` uses `ValueGeneratedNever()` (Guids assigned in domain ctors); enums stored via `HasConversion<int>()`; value objects (Email, PhoneNumber) mapped as owned/converted columns. Notable: `AppointmentConfiguration` stores `Duration` as ticks (`HasConversion`), makes `PatientId` nullable (busy slots) with `OnDelete(SetNull)`, and `ProcedureTypeId` FK `SetNull`. Files: `AppointmentConfiguration`, `PatientConfiguration`, `PatientFlagConfiguration`, `PatientFileConfiguration`, `PatientFolderConfiguration`, `PatientMedicalHistoryConfiguration`, `PatientFamilyHistoryConfiguration`, `NotificationConfiguration`, `StockItemConfiguration`, `ProcedureTypeConfiguration`, `DentalRecordConfiguration`, `DentalRecordToothConfiguration`, `MedicalDocumentConfiguration`, `ClinicConfiguration`, `UserConfiguration`, `DoctorConfiguration`.

### Migrations (`Migrations/`)
Migrations exist and are applied automatically at startup (`context.Database.Migrate()` in API `Program.cs`). Schema evolution in order:
- `InitialCreate` — base schema (patients, appointments, notifications, files, flags, recurring appts, stock items).
- `AddGoogleCalendarEventId` — `Appointment.GoogleCalendarEventId` column (link to a Google event).
- `AddProcedures` / `AddProcedureDefaultCost` — `ProcedureType` table + appointment procedure fields + default cost.
- `AddMedicalHistory`, `AddMedicalrecords` — patient medical/family history + dental records/teeth.
- `AddNotes` / `AddNotesConfig` — notes columns.
- `AddstorageFolders` — `PatientFolder` table.
- `AddMedicalDocuments` / `AddMedicalDocumentsFiles` — `MedicalDocument` table + PDF/file fields.
- `MakeAppointmentPatientIdNullable` — patient-less "busy slot" appointments.
- `addclinics`, `addUsers`, `addDoctors`, `addLogoUrl`, `updateDoctorConfig` — multi-tenant clinic/user/doctor model (Clinic, User, Doctor entities; logo URL).
- `AddLocalAuthUserFields` — Local-auth `User` columns (`PasswordHash`, `MustChangePassword`, `IsActive`, lockout fields) + a **partial unique index on lowercased email** (filtered to `PasswordHash IS NOT NULL`, so Cloud rows are unaffected). Additive + defaulted → safe for existing Cloud DBs.

## Repositories (`Repositories/`)
Concrete EF Core implementations of Domain repo interfaces. Pattern: ctor-inject `ApplicationDbContext`, `GetById*` uses `.Include(...)` for needed graphs, mutations (`AddAsync`/`UpdateAsync`/`DeleteAsync`) only stage changes (no `SaveChanges` — UoW commits).

| Domain interface | Implementation |
|---|---|
| `IPatientRepository` | `PatientRepository` |
| `IAppointmentRepository` | `AppointmentRepository` |
| `INotificationRepository` | `NotificationRepository` |
| `IStockItemRepository` | `StockItemRepository` |
| `IProcedureTypeRepository` | `ProcedureTypeRepository` |
| `IDentalRecordRepository` | `DentalRecordRepository` |
| `IPatientFolderRepository` | `PatientFolderRepository` |
| `IPatientFileRepository` | `PatientFileRepository` |
| `IMedicalDocumentRepository` | `MedicalDocumentRepository` |
| `IUserRepository` | `UserRepository` |
| `IClinicRepository` | `ClinicRepository` |
| `IDoctorRepository` | `DoctorRepository` |

Note: `PatientRepository.UpdateAsync` is careful with tracked vs detached entities (only marks `UpdatedAt` modified when already tracked to avoid full-column updates).

## External Services (`Services/`, `Storage/`)

### Google Calendar (two-way sync)
- **`GoogleCalendarService`** (`IGoogleCalendarService`) — low-level Google Calendar v3 client. Lazily builds a `CalendarService` by refreshing an OAuth access token from configured `ClientId`/`ClientSecret`/`RefreshToken` (helper `GoogleAuthUtils.RefreshAccessTokenAsync` POSTs to `oauth2.googleapis.com/token`). Methods: `CreateEventAsync`, `UpdateEventAsync`, `DeleteEventAsync`, `GetEventsAsync`. All times forced to UTC. Throws `InvalidOperationException("...not configured")` when creds/refresh token missing (callers treat this as "skip silently").
- **`GoogleCalendarSyncService`** (`IGoogleCalendarSyncService`) — business orchestration over the low-level client + repos + UoW:
  - **App → Google** (`SyncAppointmentToGoogleCalendarAsync(appointmentId)`): creates/updates a Google event for an appointment, persisting the returned event id onto `Appointment.GoogleCalendarEventId`. Cancelled/Completed appointments → delete the Google event and clear the id. Patient-less "busy slots" are skipped. Failures are logged, not thrown (don't break the appointment flow). This is the actively-used direction, triggered on appointment create/update.
  - **Google → App** (`SyncGoogleCalendarToAppointmentsAsync()`): pulls events in a -7..+90 day window, then for each event: updates the linked appointment if the Google event is newer, OR links an unlinked matching appointment (by patient name + time within 30 min), OR creates a new appointment (and even auto-creates a Patient with placeholder email/phone if `IsClinicAppointment` heuristics match). Patient name parsing uses `ExtractPatientNameFromSummary` (formats like `"Appointment: John Doe"`). **Currently disabled** as a recurring job (see API `Program.cs`); only reachable via the manual `GoogleCalendarController` endpoint.

### AI
- **`HuggingFaceAIService`** (`IHuggingFaceAIService`) — chat completions via HuggingFace router (`router.huggingface.co/v1/chat/completions`, OpenAI-compatible). Model from `HuggingFace:Model`. Injects a clinic-context system prompt; retries once if model is loading.
- **`GoogleAIService`** (`IGoogleAIService`) — Gemini (`generativelanguage.googleapis.com`). Tries a fallback list of models. **Note: not registered in `Extensions.cs`** (HuggingFace is the wired chat backend).
- **`AIActionService`** (`IAIActionService`) — agentic layer: asks the AI to classify intent + extract params (JSON), with regex fallback, then dispatches to MediatR commands/repos for `create_appointment`, `search_patient`, `view_patient`, `list_appointments`, `cancel_appointment`. Scopes everything to the current user's clinic via `IClinicContext` + `IUserRepository`.
- **`PatientSummaryService`** (`Domain.Services.IPatientSummaryService`) — generates a textual patient/appointment summary. **Placeholder** (string template, no real AI call yet; `// TODO` to integrate OpenAI/Azure).

### Notifications
- **`NotificationService`** (`INotificationService`) — email + SMS dispatch. **Placeholder/stub**: logs instead of actually sending (commented MailKit/HTTP examples). Returns false when SMTP/SMS config absent. Constructed with explicit config args in `Extensions.cs`.

### File Storage
- **`Storage/MinioFileStorage`** (`IFileStorage`) — primary blob store. Upload/Download/Delete against MinIO; auto-creates bucket; buffers non-seekable streams to learn size. Storage key = custom path or `{guid}-{timestamp}`.
- **`LocalFileStorageService`** (`IFileStorageService`) — local-disk fallback storage (sanitizes filenames, ensures uniqueness). Registered as singleton.

### Other
- **`PdfGenerationService`** (`IPdfGenerationService`) — QuestPDF (Community license). Renders French medical documents: `prescription` (ORDONNANCE), `liaison`, `honoraires`, `certificat`. Parses `MedicalDocumentPdfData.Content` (handles both JSON-array and legacy string formats).
- **`Auth0ManagementService`** (`IAuth0ManagementService`) — calls Auth0 Management API to set user `app_metadata` (`clinic_id`, `role`). Non-critical: failures are logged and swallowed (user already exists in DB).

### Local auth (`Auth/`) — Phase 1, Local mode only
- **`LocalAuthService`** (`ILocalAuthService`) — password hash/verify via ASP.NET `PasswordHasher<User>` (PBKDF2-HMAC-SHA256, per-user salt, constant-time verify), HS256 JWT issuance (claims `sub`/`clinic_id`/`role`/`email`/`jti`, 12h default lifetime), and CSPRNG temp-password generation (`RandomNumberGenerator`, unambiguous alphabet).
- **`LocalAuthConfig`** — resolves the per-install signing key: explicit `Auth:Local:SigningKey`, else a generated 512-bit key persisted to a gitignored `.local/signing-key`. The **same** path is used by issuer and validator (in API `Program.cs`) so they can't drift. Also `IsLocalMode(config)`. (Signing key is never committed / never in `appsettings.json`.)
- **`NoOpAuth0ManagementService`** — the `IAuth0ManagementService` wired in Local mode (no Auth0 tenant to update).

## DI Registration (`Extensions.cs` — `AddInfrastructure(services, configuration)`)
- `ApplicationDbContext` via `UseNpgsql("DefaultConnection")`; `IUnitOfWork` scoped.
- All repositories scoped (table above).
- `AddHttpClient()` (used by HuggingFace/GoogleAI/Auth0).
- `IFileStorageService` → `LocalFileStorageService` (**singleton**, base path `FileStorage:BasePath`).
- `IFileStorage` → `MinioFileStorage` (**scoped**) only if `MinIO:Endpoint/AccessKey/SecretKey` present; otherwise a stub that throws on use. `IMinioClient` registered as singleton when configured.
- `INotificationService` → `NotificationService` (scoped, config injected explicitly).
- `IPatientSummaryService`, `IGoogleCalendarService`, `IGoogleCalendarSyncService`, `IPdfGenerationService`, `IHuggingFaceAIService`, `IAIActionService` — all scoped.
- **Auth-mode-branched** (`Auth:Mode`): Local → `ILocalAuthService` → `LocalAuthService` and `IAuth0ManagementService` → `NoOpAuth0ManagementService`; Cloud → the real `Auth0ManagementService`.
- **Not registered here:** `GoogleAIService` (dead/optional). `AdminPasswordRecoveryService` is intentionally left out (console-only, no injectable reset path).

## Config keys consumed (names only)
`ConnectionStrings:DefaultConnection`, `FileStorage:BasePath`, `MinIO:{Endpoint,AccessKey,SecretKey,BucketName,UseSSL}`, `Notification:Smtp:{Server,Port,Username,Password}`, `Notification:Sms:{ApiKey,ApiUrl}`, `GoogleCalendar:{ClientId,ClientSecret,RefreshToken,CalendarId,RedirectUri}`, `HuggingFace:{ApiKey,Model}`, `GoogleAI:{ApiKey,Model,ApiVersion}`, `Auth0:{Domain,ManagementApi:ClientId,ManagementApi:ClientSecret}`, `Auth:Mode` (`Cloud`|`Local`), `Auth:Local:SigningKey` (optional; else generated `.local/signing-key`).
