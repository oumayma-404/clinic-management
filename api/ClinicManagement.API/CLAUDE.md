# ClinicManagement.API

ASP.NET Core 8 Web API — the presentation/host layer of the clinic-management Clean Architecture solution. Thin controllers that delegate to the Application layer via MediatR; Auth0 JWT bearer auth; Hangfire (PostgreSQL) for background jobs; Serilog logging; Swagger in dev. Composition root is `Program.cs` (top-level statements). It wires Application + Infrastructure (`AddApplication()`, `AddInfrastructure(config)`) and runs EF migrations on startup.

## Controllers (`Controllers/`)
All are thin: inject `IMediator`, send a command/query, map `Result.IsFailure` → `BadRequest`/`NotFound`. Most are `[Authorize]` (Auth0 JWT). Tenant scoping (clinic id) is resolved server-side from the token via `IClinicContext`, not from the route.

| Route | Controller | Responsibility | Auth |
|---|---|---|---|
| `api/appointments` | `AppointmentsController` | GET (list, date filter), POST (create), PUT/{id} (update; cancel via status="Cancelled") | `[Authorize]` |
| `api/patients` | `PatientsController` | GET (list), GET/{id}, POST (create), PUT/{id} | `[Authorize]` |
| `api/googlecalendar` | `GoogleCalendarController` | OAuth flow + manual sync: `authorize`, `callback` (code→refresh token, **writes token back into appsettings.json**), `sync-from-google`, `sync-appointment/{id}`, `status`, `redirect-uri` | **none** (anonymous) |
| `api/auth` | `AuthController` | **Local mode only** (each action 404s in Cloud, except `mode`). `login` (email+password → JWT), `mode` (which auth mode), `setup` (first-run clinic+admin — **localhost-gated** + "no admin yet"), `register` (staff self-registration, gated by clinic code), `change-password` (`[Authorize]`) | mostly `[AllowAnonymous]` |
| `api/clinics` | `ClinicsController` | Clinic CRUD / settings; `regenerate-code` (AdminOnly, Local) | `[Authorize]` |
| `api/users` | `UsersController` | Admin user management: list users + status, `{id}/reset-password`, `{id}/status` (deactivate/reactivate) | `[Authorize(AdminOnly)]` |
| `api/ai` | `AIController` | AI chat + agentic actions (`IAIActionService`) | `[Authorize]` |
| `api/procedure-types` | `ProcedureTypesController` | Procedure type CRUD | `[Authorize]` |
| `api/patients/{patientId}/dental-records` | `DentalRecordsController` | Dental charting | `[Authorize]` |
| `api/patients/{patientId}/medical-history` | `PatientMedicalHistoryController` | Patient medical history entries | `[Authorize]` |
| `api/patients/{patientId}/family-history` | `PatientFamilyHistoryController` | Patient family history entries | `[Authorize]` |
| `api/patients/{patientId}/files` | `PatientFilesController` | Patient file upload/download (storage-backed) | `[Authorize]` |
| `api/medical-documents` | `MedicalDocumentsController` | Medical document CRUD + PDF generation | **none** (anonymous) |

Supporting: `Models/` (request DTOs: `UploadFileRequest`, `CreateClinicRequest`, `UpdateClinicRequest`, `CreateMedicalDocumentRequest`), `Swagger/` (`FileUploadOperationFilter`, `FileUploadParameterFilter`, `FileUploadDocumentFilter` — make `IFormFile` upload work in Swagger UI).

## Background Jobs (`BackgroundJobs/`) — Hangfire
Hangfire is configured with PostgreSQL storage (same `DefaultConnection`) + a Hangfire server. Dashboard at **`/hangfire`** (auth filter `HangfireAuthorizationFilter` in `Program.cs` currently allows everyone — TODO for prod). Jobs are plain classes with `[AutomaticRetry]`; resolved from DI when enqueued.

| Job | Does | Schedule / trigger |
|---|---|---|
| `GoogleCalendarSyncJob.SyncFromGoogleCalendar()` | Calls `IGoogleCalendarSyncService.SyncGoogleCalendarToAppointmentsAsync()` (Google → App). Retry 3. | **Recurring job currently DISABLED.** `Program.cs` calls `RecurringJob.RemoveIfExists("sync-google-calendar")`; the `AddOrUpdate(...Cron.Hourly)` registration is commented out. App→Google sync is instead triggered inline on appointment create/update (not by this job). |
| `NotificationJob.ProcessPendingNotifications()` | Loads pending notifications, sends email/SMS via `INotificationService`, marks Sent/Failed. Retry 3. | Registration commented out in `Program.cs` (was `Cron.Minutely`). |
| `AISummaryJob.GenerateSummariesForUpcomingAppointments()` | For appointments 15–30 min out, generates a patient summary via `IPatientSummaryService` (currently just logs it). Retry 2. | Registration commented out in `Program.cs` (was `Cron.Minutely`). |
| `PdfGenerationJob.GenerateAndAttachPdfAsync(documentId)` | Renders a medical document to PDF (`IPdfGenerationService`) and re-saves it onto the document via MediatR. Retry 3. | **Fire-and-forget**, enqueued on demand (e.g. from `MedicalDocumentsController`), not recurring. |

**Net effect: no recurring jobs are active.** Hangfire is used mainly for the on-demand `PdfGenerationJob`; calendar sync runs synchronously/manually.

## `Program.cs` startup pipeline
Service registration order:
1. **Serilog** configured first (console + daily rolling file `logs/clinic-management-.log`, 7-day retention); `builder.Host.UseSerilog()`.
2. `AddControllers()` with camelCase + case-insensitive JSON.
3. Swagger (`AddSwaggerGen`) — maps `IFormFile`→binary, registers the file-upload param/operation filters.
4. **JWT bearer — mode-branched on `Auth:Mode`.** Cloud: Auth0 (authority `https://{domain}`, validates issuer/audience/lifetime/signing key) — only if `Auth0:Domain`+`Auth0:Audience` set. Local: symmetric HS256 validation against the per-install key from `LocalAuthConfig` (issuer/audience/lifetime/signature all validated; **do not** set `UseSecurityTokenValidators = true` — the modern `JsonWebTokenHandler` is required). Both add the same authorization policies (`AuthorizationPolicies.ConfigurePolicies`) + scoped `RoleAuthorizationHandler`. `ClockSkew = Zero`.
5. `AddHttpContextAccessor()` (needed by `ClinicContext`).
6. `AddApplication()` then `AddInfrastructure(builder.Configuration)`.
7. **Hangfire** + Hangfire server (PostgreSQL storage).
8. **CORS** policy `"AllowAll"`: restricted to `FrontendUrl` origin (default `http://localhost:3000`), `AllowAnyMethod/Header` + `AllowCredentials()` (so it can't use `AllowAnyOrigin`).

Middleware order (after `builder.Build()`):
`UseSwagger`/`UseSwaggerUI` (Development only) → `UseHttpsRedirection` → `UseCors("AllowAll")` → `UseMiddleware<ExceptionMiddleware>` (from Application; before auth) → `UseAuthentication` → `UseAuthorization` → `MapControllers` → `UseHangfireDashboard("/hangfire")`.

On startup it runs `context.Database.Migrate()` (auto-applies EF migrations), then the recurring-job (de)registration described above. Whole thing wrapped in try/catch with `Log.Fatal` + `Log.CloseAndFlush()`.

**CLI branch (before the web host boots):** `Program.cs` returns `int` and intercepts `reset-admin-password [email]` at the top — the offline admin lockout-recovery utility (`Maintenance/AdminPasswordResetCommand.cs`, wrapping the Application-layer `AdminPasswordRecoveryService`). Local-mode-only, direct-DB, one-shot with an exit code; no web endpoint. Usage: `dotnet run --project ClinicManagement.API -- reset-admin-password [admin-email]`. See `features/windows-desktop-app/ADMIN_RECOVERY.md`.

## Key configuration keys (`appsettings.json` / `appsettings.Development.json`) — names only
- `ConnectionStrings:DefaultConnection` (PostgreSQL; also Hangfire storage)
- `Auth:Mode` (`Cloud`|`Local`), `Auth:Local:SigningKey` (optional per-install HS256 key; else generated `.local/signing-key`)
- `Auth0:{Domain,Audience}`, `Auth0:ManagementApi:{ClientId,ClientSecret}`
- `GoogleCalendar:{ClientId,ClientSecret,RedirectUri,RefreshToken,CalendarId}`
- `HuggingFace:{ApiKey,Model}` (and optional `GoogleAI:*`)
- `MinIO:{Endpoint,AccessKey,SecretKey,BucketName,UseSSL}`
- `FileStorage:BasePath`
- `Notification:Smtp:{Server,Port,Username,Password}`, `Notification:Sms:{ApiKey,ApiUrl}`
- `FrontendUrl` (CORS origin + OAuth success redirect)
- `Serilog:*`, `Logging:*`, `AllowedHosts`

> Security note: the committed `appsettings.json` contains real-looking secret values (Google/HuggingFace/Auth0/DB). The Google OAuth `callback` endpoint rewrites the `RefreshToken` back into `appsettings.json` at runtime. Treat these as secrets — do not echo values; prefer user-secrets/env vars/a vault in real deployments.
