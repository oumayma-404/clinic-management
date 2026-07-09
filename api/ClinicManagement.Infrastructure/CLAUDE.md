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
- **`GoogleCalendarService`** (`IGoogleCalendarService`) — low-level Google Calendar v3 client. Lazily builds a `CalendarService` by refreshing an OAuth access token from configured `ClientId`/`ClientSecret` + the refresh token **read via `IGoogleTokenStore`** (which falls back to `GoogleCalendar:RefreshToken` config) — helper `GoogleAuthUtils.RefreshAccessTokenAsync` POSTs to `oauth2.googleapis.com/token`. Methods: `CreateEventAsync`, `UpdateEventAsync`, `DeleteEventAsync`, `GetEventsAsync`. All times forced to UTC. Throws `InvalidOperationException("...not configured")` when creds/refresh token missing (callers treat this as "skip silently").
- **`FileGoogleTokenStore`** (`Services/`, `IGoogleTokenStore`, **Singleton**) — persists the Google OAuth refresh token to a gitignored `.local/google-refresh-token` (atomic temp-file + `File.Move`), serving an in-memory cache for immediate read-after-write, and falling back to `GoogleCalendar:RefreshToken` config when no file exists (R-5). **Phase 4 (US-3):** replaces the old `GoogleCalendarController.Callback` behavior that regex-rewrote the token back into committed `appsettings.json`. Optional `GoogleCalendar:RefreshTokenPath` overrides the file location (test seam).
- **`GoogleCalendarSyncService`** (`IGoogleCalendarSyncService`) — business orchestration over the low-level client + repos + UoW:
  - **App → Google** (`SyncAppointmentToGoogleCalendarAsync(appointmentId)`): creates/updates a Google event for an appointment, persisting the returned event id onto `Appointment.GoogleCalendarEventId`. Cancelled/Completed appointments → delete the Google event and clear the id. Patient-less "busy slots" are skipped. Failures are logged, not thrown (don't break the appointment flow). This is the actively-used direction, triggered on appointment create/update.
  - **Google → App** (`SyncGoogleCalendarToAppointmentsAsync()`): pulls events in a -7..+90 day window, then for each event: updates the linked appointment if the Google event is newer, OR links an unlinked matching appointment (by patient name + time within 30 min), OR creates a new appointment (and even auto-creates a Patient with placeholder email/phone if `IsClinicAppointment` heuristics match). Patient name parsing uses `ExtractPatientNameFromSummary` (formats like `"Appointment: John Doe"`). **Currently disabled** as a recurring job (see API `Program.cs`); only reachable via the manual `GoogleCalendarController` endpoint.

### AI
- **`HuggingFaceAIService`** (`IHuggingFaceAIService`) — chat completions via HuggingFace router (`router.huggingface.co/v1/chat/completions`, OpenAI-compatible). Model from `HuggingFace:Model`. Injects a clinic-context system prompt; retries once if model is loading.
- **`GoogleAIService`** (`IGoogleAIService`) — Gemini (`generativelanguage.googleapis.com`). Tries a fallback list of models. **Note: not registered in `Extensions.cs`** (HuggingFace is the wired chat backend).
- **`AIActionService`** (`IAIActionService`) — agentic layer: asks the AI to classify intent + extract params (JSON), with regex fallback, then dispatches to MediatR commands/repos for `create_appointment`, `search_patient`, `view_patient`, `list_appointments`, `cancel_appointment`. Scopes everything to the current user's clinic via `IClinicContext` + `IUserRepository`.
- **`PatientSummaryService`** (`Domain.Services.IPatientSummaryService`) — generates a textual patient/appointment summary. **Placeholder** (string template, no real AI call yet; `// TODO` to integrate OpenAI/Azure).

### Connectivity (Phase 3, Local mode) — `Services/`
- **`InternetProbe`** (`IInternetProbe`) — judges whether the **server** has internet egress (the LAN clients can't, so the server is the source of truth). Registered as a **Singleton** over a shared `IMemoryCache`: a `SemaphoreSlim` double-checked lock collapses a herd of pollers to **one probe per TTL**. Sends a `GET` to `Connectivity:ProbeUrl` with a linked timeout token; a 2xx/3xx ⇒ reachable, any request failure ⇒ not. Uses `IHttpClientFactory` (safe in a singleton). Backs `GetConnectivityStatusQuery`.
- **`ConnectivityConfig`** — parallel static-accessor helper (mirrors `LocalAuthConfig`'s idiom) resolving `Connectivity:ProbeUrl` (default `https://www.google.com/generate_204`), `ProbeTimeoutSeconds` (3), `ProbeCacheSeconds` (5). Kept separate from auth config on purpose.

### Notifications
- **`NotificationService`** (`INotificationService`) — email + SMS dispatch. **Placeholder/stub**: logs instead of actually sending (commented MailKit/HTTP examples). Returns false when SMTP/SMS config absent. Constructed with explicit config args in `Extensions.cs`.

### File Storage (`IFileStorage`, mode-branched)
The single storage seam is `IFileStorage`; the concrete backend is chosen by `Auth:Mode` (see DI below).
- **`Storage/MinioFileStorage`** (Cloud mode) — blob store against MinIO; auto-creates bucket; buffers non-seekable streams to learn size. Storage key = custom path or `{guid}-{timestamp}`.
- **`Storage/LocalDiskFileStorage`** (Local/offline mode) — stores blobs under `FileStorage:BasePath`, returning an opaque relative storage key. Mirrors MinIO semantics: guid-based keys, deterministic custom-path overwrite, seekable (`MemoryStream`) download, idempotent delete (missing key is not an error), and download-missing throws (surfaced by handlers as a clean failure). Keys are resolved and constrained within the base folder (path-traversal safe).

### Other
- **`PdfGenerationService`** (`IPdfGenerationService`) — QuestPDF (Community license). Renders French medical documents: `prescription` (ORDONNANCE), `liaison`, `honoraires`, `certificat`. Parses `MedicalDocumentPdfData.Content` (handles both JSON-array and legacy string formats).
- **`Auth0ManagementService`** (`IAuth0ManagementService`) — calls Auth0 Management API to set user `app_metadata` (`clinic_id`, `role`). Non-critical: failures are logged and swallowed (user already exists in DB).

### Local auth (`Auth/`) — Phase 1, Local mode only
- **`LocalAuthService`** (`ILocalAuthService`) — password hash/verify via ASP.NET `PasswordHasher<User>` (PBKDF2-HMAC-SHA256, per-user salt, constant-time verify), HS256 JWT issuance (claims `sub`/`clinic_id`/`role`/`email`/`jti`, 12h default lifetime), and CSPRNG temp-password generation (`RandomNumberGenerator`, unambiguous alphabet).
- **`LocalAuthConfig`** — resolves the per-install signing key: explicit `Auth:Local:SigningKey`, else a generated 512-bit key persisted to a gitignored `.local/signing-key`. The **same** path is used by issuer and validator (in API `Program.cs`) so they can't drift. Also `IsLocalMode(config)`. (Signing key is never committed / never in `appsettings.json`.)
- **`NoOpAuth0ManagementService`** — the `IAuth0ManagementService` wired in Local mode (no Auth0 tenant to update).

### LAN hosting / security helpers (root namespace) — Phase 4, Local mode
Static, unit-testable helpers (referenced from API `Program.cs` and controllers), deliberately in the root `ClinicManagement.Infrastructure` namespace so `ClinicManagement.UnitTests` (references Infrastructure, not the API) can exercise them:
- **`LocalRequest.IsLoopback(HttpContext)`** — "did this request originate from the server machine itself?" Extracted **verbatim** from the old `AuthController.IsLocalRequest` (null `RemoteIpAddress` ⇒ `true`, preserved per R-8). Gates the first-run `setup` endpoint (AC-1.2a) **and** the loopback-only Hangfire dashboard in Local mode.
- **`CorsOrigins.Assemble(frontendUrl, additional)` / `FromConfiguration(config)`** — builds the credentialed CORS policy's exact origin list (can't use `AllowAnyOrigin()` with `AllowCredentials()`). Deduped case-insensitively, order-preserving, empty/whitespace dropped. Cloud collapses to the single `FrontendUrl`; Local unions in `Cors:AllowedOrigins` (LAN client origins) with no code change.

## DI Registration (`Extensions.cs` — `AddInfrastructure(services, configuration)`)
- `ApplicationDbContext` via `UseNpgsql("DefaultConnection")`; `IUnitOfWork` scoped.
- All repositories scoped (table above).
- `AddHttpClient()` (used by HuggingFace/GoogleAI/Auth0).
- `IFileStorage` (**scoped**) is **mode-branched** on `Auth:Mode`: **Local** → `LocalDiskFileStorage` (base path `FileStorage:BasePath`, no MinIO); **Cloud** → `MinioFileStorage` if `MinIO:Endpoint/AccessKey/SecretKey` present (with `IMinioClient` singleton), otherwise a stub that throws on use.
- `INotificationService` → `NotificationService` (scoped, config injected explicitly).
- `IPatientSummaryService`, `IGoogleCalendarService`, `IGoogleCalendarSyncService`, `IPdfGenerationService`, `IHuggingFaceAIService`, `IAIActionService` — all scoped.
- `IInternetProbe` → `InternetProbe` (**singleton**) + `AddMemoryCache()` (idempotent) — connectivity awareness (Phase 3).
- `IGoogleTokenStore` → `FileGoogleTokenStore` (**singleton**) — Google OAuth refresh-token file store (Phase 4).
- **Auth-mode-branched** (`Auth:Mode`): Local → `ILocalAuthService` → `LocalAuthService` and `IAuth0ManagementService` → `NoOpAuth0ManagementService`; Cloud → the real `Auth0ManagementService`.
- **Not registered here:** `GoogleAIService` (dead/optional). `AdminPasswordRecoveryService` is intentionally left out (console-only, no injectable reset path).

## Config keys consumed (names only)
`ConnectionStrings:DefaultConnection`, `FileStorage:BasePath`, `MinIO:{Endpoint,AccessKey,SecretKey,BucketName,UseSSL}`, `Notification:Smtp:{Server,Port,Username,Password}`, `Notification:Sms:{ApiKey,ApiUrl}`, `GoogleCalendar:{ClientId,ClientSecret,RefreshToken,CalendarId,RedirectUri}`, `HuggingFace:{ApiKey,Model}`, `GoogleAI:{ApiKey,Model,ApiVersion}`, `Auth0:{Domain,ManagementApi:ClientId,ManagementApi:ClientSecret}`, `Auth:Mode` (`Cloud`|`Local`), `Auth:Local:SigningKey` (optional; else generated `.local/signing-key`), `Connectivity:{ProbeUrl,ProbeTimeoutSeconds,ProbeCacheSeconds}` (all optional; defaults `https://www.google.com/generate_204` / 3 / 5), `Cors:AllowedOrigins` (optional LAN origins, Phase 4), `GoogleCalendar:RefreshTokenPath` (optional `.local/` file override for the token store, Phase 4). *(HTTPS/Kestrel hosting keys — `Https:*`, `Hosting:*` — are read in API `Program.cs`, not here.)*
