# ClinicManagement.API

ASP.NET Core 8 Web API — the presentation/host layer of the clinic-management Clean Architecture solution. Thin controllers that delegate to the Application layer via MediatR; Auth0 JWT bearer auth; Hangfire (PostgreSQL) for background jobs; Serilog logging; Swagger in dev. Composition root is `Program.cs` (top-level statements). It wires Application + Infrastructure (`AddApplication()`, `AddInfrastructure(config)`) and runs EF migrations on startup.

## Controllers (`Controllers/`)
All are thin: inject `IMediator`, send a command/query, map `Result.IsFailure` → `BadRequest`/`NotFound`. Most are `[Authorize]` (Auth0 JWT). Tenant scoping (clinic id) is resolved server-side from the token via `IClinicContext`, not from the route.

**Local-mode release gate (Phase 4 / FR-E3):** in Local mode a fail-closed `FallbackPolicy` (`RequireAuthenticatedUser()`) means every endpoint without an explicit `[AllowAnonymous]` returns 401 — so "anonymous-by-omission" no longer exists. The exact anonymous allow-list is `Auth.{GetMode,Login,Setup,Register}`, `Connectivity.Get`, `GoogleCalendar.{Authorize,Callback}`, and it is pinned by `ControllerAuthorizationCoverageTests` (a reflection test that fails the build if a new/renamed/removed anonymous endpoint drifts from the list). Cloud keeps a null fallback → unchanged.

| Route | Controller | Responsibility | Auth |
|---|---|---|---|
| `api/appointments` | `AppointmentsController` | GET (list, date filter), POST (create), PUT/{id} (update; cancel via status="Cancelled") | `[Authorize]` |
| `api/patients` | `PatientsController` | GET (list), GET/{id}, POST (create), PUT/{id} | `[Authorize]` |
| `api/googlecalendar` | `GoogleCalendarController` | OAuth flow + manual sync: `authorize`, `callback` (code→refresh token, **persisted via `IGoogleTokenStore` to `.local/`** — no longer rewrites appsettings.json), `sync-from-google`, `sync-appointment/{id}`, `status`, `redirect-uri` | `authorize`/`callback` `[AllowAnonymous]` (Google browser-redirect, no bearer); AJAX endpoints covered by the Local fallback policy |
| `api/auth` | `AuthController` | **Local mode only** (each action 404s in Cloud, except `mode`). `login` (email+password → JWT), `mode` (which auth mode), `setup` (first-run clinic+admin — **localhost-gated** + "no admin yet"), `register` (staff self-registration, gated by clinic code), `change-password` (`[Authorize]`) | mostly `[AllowAnonymous]` |
| `api/clinics` | `ClinicsController` | Clinic CRUD / settings; `regenerate-code` (AdminOnly, Local) | `[Authorize]` |
| `api/users` | `UsersController` | Admin user management: list users + status, `{id}/reset-password`, `{id}/status` (deactivate/reactivate) | `[Authorize(AdminOnly)]` |
| `api/ai` | `AIController` | AI chat + agentic actions (`IAIActionService`) | `[Authorize]` |
| `api/procedure-types` | `ProcedureTypesController` | Procedure type CRUD | `[Authorize]` |
| `api/patients/{patientId}/dental-records` | `DentalRecordsController` | Dental charting | `[Authorize]` |
| `api/patients/{patientId}/medical-history` | `PatientMedicalHistoryController` | Patient medical history entries | `[Authorize]` |
| `api/patients/{patientId}/family-history` | `PatientFamilyHistoryController` | Patient family history entries | `[Authorize]` |
| `api/patients/{patientId}/files` | `PatientFilesController` | Patient file upload/download (storage-backed) | `[Authorize]` |
| `api/medical-documents` | `MedicalDocumentsController` | Medical document CRUD + PDF generation | `[Authorize]` (class-level, added Phase 4 — defense-in-depth for PHI; tightens both modes) |
| `api/connectivity` | `ConnectivityController` | **Local mode only** (404s in Cloud). `GET` → `{ internetReachable }` — server-side internet-egress probe (`IInternetProbe`) polled by the frontend to gate AI + Google Calendar offline (Phase 3) | `[AllowAnonymous]` |
| `api/backup` | `BackupController` | **Local-oriented (Phase 5 / US-8 / FR-G).** `POST` → one-click "Backup now": `BackupNowCommand` dumps the DB + copies file storage to a timestamped folder; success returns `{ destinationPath, sizeBytes, timestampUtc }`, failure returns the operator-facing reason as 400 (never a silent success). On Cloud a call fails cleanly (no bundled `pg_dump`); the frontend only surfaces it in Local mode. | `[Authorize(AdminOnly)]` |

Supporting: `Models/` (request DTOs: `UploadFileRequest`, `CreateClinicRequest`, `UpdateClinicRequest`, `CreateMedicalDocumentRequest`), `Swagger/` (`FileUploadOperationFilter`, `FileUploadParameterFilter`, `FileUploadDocumentFilter` — make `IFormFile` upload work in Swagger UI).

## Background Jobs (`BackgroundJobs/`) — Hangfire
Hangfire is configured with PostgreSQL storage (same `DefaultConnection`) + a Hangfire server. Dashboard at **`/hangfire`**, gated by `HangfireAuthorizationFilter` (in `Program.cs`, ctor-injected `isLocalMode`): in **Local** mode it authorizes **loopback only** (via `LocalRequest.IsLoopback` — server PC yes, LAN clients no); in **Cloud** it is unchanged (Phase 4 replaced the prior `return true;`). Jobs are plain classes with `[AutomaticRetry]`; resolved from DI when enqueued.

| Job | Does | Schedule / trigger |
|---|---|---|
| `GoogleCalendarSyncJob.SyncFromGoogleCalendar()` | Calls `IGoogleCalendarSyncService.SyncGoogleCalendarToAppointmentsAsync()` (Google → App). Retry 3. | **Recurring job currently DISABLED.** `Program.cs` calls `RecurringJob.RemoveIfExists("sync-google-calendar")`; the `AddOrUpdate(...Cron.Hourly)` registration is commented out. App→Google sync is instead triggered inline on appointment create/update (not by this job). |
| `NotificationJob.ProcessPendingNotifications()` | Loads pending notifications, sends email/SMS via `INotificationService`, marks Sent/Failed. Retry 3. | Registration commented out in `Program.cs` (was `Cron.Minutely`). |
| `AISummaryJob.GenerateSummariesForUpcomingAppointments()` | For appointments 15–30 min out, generates a patient summary via `IPatientSummaryService` (currently just logs it). Retry 2. | Registration commented out in `Program.cs` (was `Cron.Minutely`). |
| `PdfGenerationJob.GenerateAndAttachPdfAsync(documentId)` | Renders a medical document to PDF (`IPdfGenerationService`) and re-saves it onto the document via MediatR. Retry 3. | **Fire-and-forget**, enqueued on demand (e.g. from `MedicalDocumentsController`), not recurring. |

**Net effect: no recurring jobs are active.** Hangfire is used mainly for the on-demand `PdfGenerationJob`; calendar sync runs synchronously/manually.

## `Program.cs` startup pipeline
`Program.cs` **returns `int`** — a non-zero exit code is a deliberate, operator-facing failure (bad CLI verb, unreachable DB, port-in-use, missing configured cert), never an opaque crash. It configures `Serilog` and reads the auth mode **before** `WebApplication.CreateBuilder` (the log-file path + startup-failure handling both need the mode early). In Local mode the log file is anchored to `LocalInstallPaths.BaseDirectory/logs/` (a Windows service's CWD is `System32`); Cloud keeps its relative `logs/` path.

Service registration order:
1. **Serilog** configured first (console + daily rolling file, 7-day retention); `builder.Host.UseSerilog()`.
1b. **Windows service hosting (Phase 5 S2, Local only)** — `builder.Host.UseWindowsService()` when `isLocalAuthMode`, so the API auto-starts as a service and roots content at the install dir. No-op when not launched as a service; Cloud/dev unaffected.
2. `AddControllers()` with camelCase + case-insensitive JSON.
3. Swagger (`AddSwaggerGen`) — maps `IFormFile`→binary, registers the file-upload param/operation filters.
4. **JWT bearer — mode-branched on `Auth:Mode`.** Cloud: Auth0 (authority `https://{domain}`, validates issuer/audience/lifetime/signing key) — only if `Auth0:Domain`+`Auth0:Audience` set. Local: symmetric HS256 validation against the per-install key from `LocalAuthConfig` (issuer/audience/lifetime/signature all validated; **do not** set `UseSecurityTokenValidators = true` — the modern `JsonWebTokenHandler` is required). Both add the authorization policies via `AuthorizationPolicies.ConfigurePolicies(options, isLocalMode)` — Local passes `true` to install the fail-closed `FallbackPolicy` (Phase 4), Cloud passes `false` (null fallback) — plus a scoped `RoleAuthorizationHandler`. `ClockSkew = Zero`.
5. `AddHttpContextAccessor()` (needed by `ClinicContext`).
6. `AddApplication()` then `AddInfrastructure(builder.Configuration)`.
7. **Hangfire** + Hangfire server (PostgreSQL storage).
8. **CORS** policy `"AllowAll"`: origins come from `CorsOrigins.FromConfiguration(config)` (Infrastructure helper) — `FrontendUrl` (default `http://localhost:3000`) unioned with the optional `Cors:AllowedOrigins` array (Phase 4; lets a Local install add LAN client origins with no code change), `AllowAnyMethod/Header` + `AllowCredentials()` (so it can't use `AllowAnyOrigin`). Cloud collapses to the single `FrontendUrl`.
9. **Same-origin front door (Phase 5 S4, Local only)** — before `builder.Build()`, `AddReverseProxy().LoadFromMemory(...)` registers a single **YARP** catch-all route (`/{**catch-all}`, least-specific) forwarding to the co-located Next server on `http://localhost:{Hosting:WebPort ?? 3000}`. Kestrel becomes the *only* browser-facing endpoint: `/api/*` controllers (more specific, win endpoint selection) run in-process; everything else (pages, `/_next/*`, `/bff/*`) is proxied to Next. TLS terminates once, inside the audited .NET app. Cloud installs no proxy (absolute API URL, separate origins) — unchanged.
10. **Config-driven HTTPS / bind — mode-branched (Phase 5 S3)**, before `builder.Build()`. **Local: always serves HTTPS.** If `Https:CertPath` is set it **must exist** or the server calls `StartupDiagnostics.ReportFatal` and `return 1` (no silent HTTP downgrade); otherwise `CertificateProvisioner.EnsureServerCertificate()` self-generates a CA + server cert into `.local/` (see Infrastructure `Security/`). Kestrel then binds HTTP **loopback-only** on `Hosting:HttpPort` (5000 — the sole consumer is the co-located Next BFF; keeping cleartext off the LAN) and HTTPS on `Hosting:HttpsPort` (5001, `ListenAnyIP` + cert) — 5001 is the only LAN-facing port. **Cloud: byte-for-byte unchanged** — opt-in HTTPS only when a cert file exists (`ListenAnyIP` both ports), else honor `Hosting:Urls`. A `certSource` (`generated`|`configured`|`cloud`) is logged in the Local transport-posture line.

Middleware order (after `builder.Build()`):
`UseSwagger`/`UseSwaggerUI` (Development only) → `UseHttpsRedirection` (**Cloud only** — Local must not redirect its loopback HTTP hop to the self-signed HTTPS front door, or Node rejects the untrusted cert as "cannot reach the clinic server") → `UseCors("AllowAll")` → `UseMiddleware<ExceptionMiddleware>` (from Application; before auth) → `UseAuthentication` → `UseAuthorization` → **`LocalAuthEnforcementMiddleware`** (Local only — the app-issued JWT is stateless, so this revokes deactivated accounts + gates forced-password-change per request) → `MapControllers` → `UseHangfireDashboard("/hangfire")` → **`MapReverseProxy().AllowAnonymous()`** (Local only — the proxied Next app handles its own auth; it must be anonymous or the fail-closed `FallbackPolicy` would 401 the login page itself).

On startup it runs `context.Database.Migrate()` inside a `catch … when (isLocalAuthMode && StartupDiagnostics.IsDatabaseConnectionFailure(ex))` that reports a clear "DB unreachable" message and `return 1` (Cloud keeps the fatal-rethrow, `when` false), then the recurring-job (de)registration. The **outer** try/catch has a `catch … when (startupIsLocalMode && StartupDiagnostics.IsAddressInUse(ex))` → clear "port in use" message + `return 1`; else `Log.Fatal` + rethrow, with `Log.CloseAndFlush()` in `finally`.

## Startup diagnostics (`Startup/`) — Phase 5 S2, Local mode
`StartupDiagnostics` (static, unit-testable) classifies the two operator-recoverable boot failures into clear French messages instead of a stack trace: `IsDatabaseConnectionFailure` (socket/timeout/bare `NpgsqlException` — but NOT a `PostgresException`, which means the server answered) and `IsAddressInUse` (`AddressInUseException` / `SocketError.AddressAlreadyInUse`). `ReportFatal(message, ex?)` fans the message to the console, Serilog, and — best-effort, Windows-only — the Windows **Event Log**. Only invoked on the Local startup path (Cloud keeps its fatal-rethrow, R-9).

**CLI branch (before the web host boots):** `Program.cs` returns `int` and intercepts `reset-admin-password [email]` at the top — the offline admin lockout-recovery utility (`Maintenance/AdminPasswordResetCommand.cs`, wrapping the Application-layer `AdminPasswordRecoveryService`). Local-mode-only, direct-DB, one-shot with an exit code; no web endpoint. Usage: `dotnet run --project ClinicManagement.API -- reset-admin-password [admin-email]`. See `features/windows-desktop-app/ADMIN_RECOVERY.md`.

## Key configuration keys (`appsettings.json` / `appsettings.Development.json`) — names only
- `ConnectionStrings:DefaultConnection` (PostgreSQL; also Hangfire storage)
- `Auth:Mode` (`Cloud`|`Local`), `Auth:Local:SigningKey` (optional per-install HS256 key; else generated `.local/signing-key`)
- `Connectivity:{ProbeUrl,ProbeTimeoutSeconds,ProbeCacheSeconds}` (Local-mode internet-egress probe; all optional)
- `Auth0:{Domain,Audience}`, `Auth0:ManagementApi:{ClientId,ClientSecret}`
- `GoogleCalendar:{ClientId,ClientSecret,RedirectUri,RefreshToken,CalendarId}`
- `HuggingFace:{ApiKey,Model}` (and optional `GoogleAI:*`)
- `MinIO:{Endpoint,AccessKey,SecretKey,BucketName,UseSSL}`
- `FileStorage:BasePath`
- `Notification:Smtp:{Server,Port,Username,Password}`, `Notification:Sms:{ApiKey,ApiUrl}`
- `FrontendUrl` (primary CORS origin + OAuth success redirect); `Cors:AllowedOrigins` (optional extra LAN origins, Phase 4)
- `Https:{CertPath,CertPassword}`, `Hosting:{Urls,HttpPort,HttpsPort,WebPort}` (LAN hosting / front door — all optional, inert in Cloud; Local self-generates the cert into `.local/` when `CertPath` is empty, `WebPort` is the localhost port the YARP front door proxies non-`/api` routes to, Phase 5)
- `Backup:{PgDumpPath,DefaultDestination,TimeoutSeconds}` (Phase 5 one-click backup — path to bundled `pg_dump.exe`, default backup folder, dump timeout in seconds; consumed by `PgDumpBackupService`)
- `Serilog:*`, `Logging:*`, `AllowedHosts`

> Security note: the committed `appsettings.json` contains real-looking secret values (Google/HuggingFace/Auth0/DB). **Phase 4 removed the OAuth `callback` appsettings-rewrite** — the refresh token now persists via `IGoogleTokenStore` to a gitignored `.local/` file. (An empty `Https:CertPassword` slot was added to committed appsettings — review Finding 5 flags it should route through `.local/`/env/user-secrets, not be pasted into tracked config.) Treat all these as secrets — do not echo values; prefer user-secrets/env vars/a vault in real deployments.
