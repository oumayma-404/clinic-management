# ClinicManagement.API

ASP.NET Core 8 Web API — the presentation/host layer of the clinic-management Clean Architecture solution. Thin controllers delegate to Application via MediatR; **pluggable JWT auth** (`Auth:Mode` = Cloud=Auth0 | Local=self-issued HS256); Hangfire (PostgreSQL) for background jobs; **SignalR** for realtime; Serilog logging; Swagger in dev. Composition root is `Program.cs` (top-level statements, **returns `int`** — a non-zero exit is a deliberate operator-facing failure). It wires Application + Infrastructure (`AddApplication()`, `AddInfrastructure(config)`) and applies EF migrations on startup (mode-branched — see below).

## Controllers (`Controllers/`) — 30 route controllers + `ApiControllerBase`
All are thin: inject `IMediator`, send a command/query, map `Result.IsFailure` → an error response. All extend **`ApiControllerBase`**, which renders every failure as the single canonical body `{ "error": "<message>" }` (via `HandleFailure(result, statusCode=400)` / `Failure(message, statusCode)`; a blank message falls back to `ErrorMessages.Generic`). The action keeps ownership of the status code (400/401/403/404). Tenant scoping (clinic id) is resolved server-side from the token (`IClinicContext` → DB lookup), never from the route.

**Local-mode release gate (FR-E3, fail-closed):** in Local mode `AuthorizationPolicies.ConfigurePolicies(options, isLocalMode: true)` installs a `FallbackPolicy = RequireAuthenticatedUser()`, so any endpoint without an explicit `[AllowAnonymous]` returns 401 — "anonymous-by-omission" cannot exist. The **only** approved anonymous endpoints are `Auth.{GetMode,Login,Setup,Register}`, `Connectivity.Get`, `GoogleCalendar.Callback`, pinned by `ControllerAuthorizationCoverageTests` (a reflection test that fails the build if any `[AllowAnonymous]` drifts from the allow-list). Cloud keeps a null fallback → unchanged.

**Role policies** (`AuthorizationPolicies`, in Application): `AdminOnly`, `AdminOrDoctor`, plus the unused-by-controllers `DoctorOrSecretary`/`DoctorOnly`/`SecretaryOnly`. Applied class- or action-level over the base `[Authorize]`.

### Core clinical / scheduling
| Route | Controller | Notable | Auth |
|---|---|---|---|
| `api/appointments` | `AppointmentsController` | GET (list, date/doctor filter), GET/{id} (single — notification deep-link target; other-clinic id → 404), POST, PUT/{id} (cancel via `status="Cancelled"`; also **moves/clears the treatment-plan act link** — `treatmentPlanId` + `treatmentPlanItemId`, validated by `AppointmentPlanLink`. **Tri-state**: omitting the field leaves the link untouched, an explicit `null` clears it — see `UpdateAppointmentCommand.TreatmentPlanItemIdSpecified`); **recurring series**: `GET/POST recurring`, `POST recurring/{id}/cancel` (scope Occurrence/Following/WholeSeries) | `[Authorize]` |
| `api/notifications` | `NotificationsController` | In-app staff feed: GET (50 newest, viewer-scoped), `GET unread-count` (`{ unreadCount }`), `GET pending-reviews` (post-visit "how was the visit" popup), `PUT {id}/read` (tenant/missing → 404), `PUT read-all` | `[Authorize]` |
| `api/patients` | `PatientsController` | GET (list), GET/{id}, POST, PUT/{id} | `[Authorize]` |
| `api/patients/{id}/medical-history` · `/family-history` · `/dental-records` · `/files` · `/odontogram` | `PatientMedicalHistoryController`, `PatientFamilyHistoryController`, `DentalRecordsController`, `PatientFilesController`, `OdontogramController` | Per-patient history, dental charting, storage-backed files, tooth-state history. **`DELETE .../dental-records/{id}` is `AdminOrDoctor`** — deleting a fiche detaches the invoice lines and devis acts built from it, and the class-level gate was previously the only one (A-12) | `[Authorize]` (+ `AdminOrDoctor` on the fiche delete) |
| `api/patients/recalls` | `RecallController` | Recall / relance: due list, `settings` GET/PUT (interval months), `{id}/contacted`, `{id}/snooze`, `{id}/send` (SMS/WhatsApp, connectivity-gated) | `[Authorize]` |
| `api/waiting-list` | `WaitingListController` | Salle d'attente CRUD + `{id}/promote` (→ real appointment) | `[Authorize]` |
| `api/lab-orders` | `LabOrdersController` | Prosthetics/lab work orders CRUD + `{id}/status` (Sent/InProgress/Received/Fitted). The status action now surfaces the domain's **transition table**: an illegal move returns the aggregate's French message as a 400, and `LabWorkOrderDto.AllowedNextStatuses` tells the UI which stages to offer | `[Authorize]` |
| `api/doctors` · `api/procedure-types` · `api/stock` | `DoctorsController`, `ProcedureTypesController`, `StockController` | Doctor profiles, procedure catalog, stock/inventory | `[Authorize]` |
| `api/dashboard` | `DashboardController` | KPIs (monthly revenue, outstanding, …) | `[Authorize]` |
| `api/medical-documents` | `MedicalDocumentsController` | Document CRUD (JSON or multipart+`IFormFile`), `POST {id}/generate-pdf` (enqueues `PdfGenerationJob`, returns `{ jobId }`), `POST generate-pdf-download` (inline PDF; cachet/ordre/city are re-resolved server-side, never trusted from the body). **`DELETE {id}` is `AdminOrDoctor`** — a signed clinical instrument, and the class-level gate was previously the only one (A-12), so any secretary could destroy an ordonnance | `[Authorize]` (class-level; PHI defense-in-depth, both modes) + `AdminOrDoctor` delete |

### Billing / CNAM / e-invoicing
| Route | Controller | Notable | Auth |
|---|---|---|---|
| `api/invoices` | `InvoicesController` | Notes d'honoraires (lines + payments), issue/pay, PDF; cancel-issued is `AdminOrDoctor` | `[Authorize]` (+ `AdminOrDoctor` action) |
| `api` | `BillingController` | Unified per-patient billing summary (`solde patient`) + caisse | `[Authorize]` |
| `api/expenses` | `ExpensesController` | Clinic expenses / caisse cash-out CRUD | `[Authorize]` |
| `api/treatment-plans` | `TreatmentPlansController` | Devis / plans (planned acts + installments). GET responses carry a **derived read-back** — per-act `scheduledAppointmentId/At/Status`, plan-level `itemsDone/itemsTotal`, `nextAppointmentAt` and the linked-invoice triple — computed per request by `TreatmentPlanWorkflowProjection` (two batched reads, never per plan). **Amendment**: `POST {id}/amend` (add/remove acts + échéancier) and `PUT {id}/installments` are **`AdminOrDoctor`** — they alter what a patient owes on a numbered document, the same class as cancelling an issued invoice; `PUT {id}/items/order` carries **no** method-level policy (reordering is cosmetic). `POST {id}/cancel` is `AdminOrDoctor` too. All four are pinned by `TreatmentPlansControllerAuthorizationTests`, which also **fails on any unclassified new action**. *(Seeding a plan from the odontogram is a **frontend** prefill — there is no backend seed command.)* | `[Authorize]` (+ `AdminOrDoctor` action) |
| `api/cnam-nomenclature` · `api/dental-acts` · `api/medications` | `CnamNomenclatureController`, `DentalActsController`, `MedicationsController` | CNAM nomenclature/letter-values (backs BS1 estimate), dental-act catalog, medication catalog (ordonnance picker). Read endpoints `[Authorize]`; **catalog-write endpoints are `AdminOnly`** | `[Authorize]` + `AdminOnly` writes |

### AI / auth / infra (Local-oriented)
| Route | Controller | Notable | Auth |
|---|---|---|---|
| `api/ai` | `AIController` | `POST chat` (HuggingFace via `ChatCommand`) | `[Authorize]` |
| `api/auth` | `AuthController` | **Local-only** (`login`/`setup`/`register` 404 in Cloud; `mode` always works). `mode`, `login` (email+pw → JWT, 401 on fail), `setup` (first-run clinic+admin — **loopback-gated**), `register` (clinic-code self-registration), `change-password` (`[Authorize]`) | mostly `[AllowAnonymous]` |
| `api/clinics` | `ClinicsController` | Clinic CRUD + settings; most settings writes + `regenerate-code` are `AdminOnly` | `[Authorize]` + `AdminOnly` writes |
| `api/users` | `UsersController` | Admin user mgmt: list, `{id}/reset-password`, `{id}/status`, **`PUT {id}/role`** (admin/doctor/secretary; validated against `User.AssignableRoles`, keeps email + full name, refuses a self-demotion leaving no other active admin, bumps `TokenVersion`) | `[Authorize(AdminOnly)]` (class) |
| `api/googlecalendar` | `GoogleCalendarController` | OAuth + manual sync. `POST connect` (mints CSRF `state` → server cache + HttpOnly cookie; **`AdminOnly`**), `GET callback` (**only** `[AllowAnonymous]`; validates state double-submit, exchanges code, persists refresh token **onto the clinic entity** — per-clinic isolation, DB — then redirects to `FrontendUrl`), **`POST disconnect`** (`AdminOnly`; the one action here that goes through **MediatR** — `DisconnectGoogleCalendarCommand` — because it is an ordinary clinic mutation, which makes it unit-testable and gets the realtime broadcast, unlike the OAuth plumbing that legitimately works the repositories directly), `sync-from-google`/`sync-appointment/{id}` (`AdminOnly`), `status`, `redirect-uri` | class `[Authorize]` |
| `api/connectivity` | `ConnectivityController` | **Local-only** (404 in Cloud). `GET` → `ConnectivityStatusDto { internetReachable }`, server-side egress probe polled by the frontend to gate AI + Google Calendar offline | `[AllowAnonymous]` |
| `api/backup` | `BackupController` | **Local-oriented.** `POST` → `BackupNowCommand` (pg_dump + file-storage copy to timestamped folder); success `{ destinationPath, sizeBytes, timestampUtc }`, failure = operator reason as 400 (never silent). Fails cleanly in Cloud (no bundled pg_dump) | `[Authorize(AdminOnly)]` (class) |

Supporting: `Models/` (request DTOs: `UploadFileRequest`, `LoginRequest`, `SetupRequest`, `RegisterRequest`, `ChangePasswordRequest`, `SetUserStatusRequest`, `BackupRequest`, `CreateClinicRequest`, `UpdateClinicRequest`, `UpdateDoctorProfileRequest`, `CreateMedicalDocumentRequest`, `CnamNomenclatureRequests`, `MedicationRequests`); `Swagger/` (`FileUploadParameterFilter` + `FileUploadOperationFilter` — registered; `FileUploadDocumentFilter.cs` exists but is **not wired**, effectively dead).

## Realtime (`Hubs/`) — SignalR
- **`ClinicHub`** (`[Authorize]`, mapped at **`/hub/clinic`**) — on connect resolves the caller's clinic (same `IUserRepository` lookup the REST handlers use; `IHttpContextAccessor` is unreliable in a hub, so it reads `HubCallerContext.User`) and joins the `clinic-{id}` group. Server→client event `"entityChanged"` carries the lowercase resource key that changed so clients refetch only affected views.
- **`SignalRRealtimeNotifier`** (`IRealtimeNotifier`, registered scoped) — broadcasts to the clinic group; a broadcast failure is logged and **swallowed** (realtime is additive, never fails the committed use case). It is the API-side implementation of the Application `RealtimeBroadcastBehavior` seam.
- **`ClinicGroups.Name(clinicId)`** = single source of the `clinic-{id}` group name.
- Hub JWT: a browser WebSocket handshake can't set an Authorization header, so the SignalR client sends the token as `?access_token=`; `CreateHubJwtEvents()` (`Program.cs`) reads it into the token **only for `/hub/*` paths** — same signed token, same mode-branched validation. Security: framework request logging is suppressed, so the token in the query string is not logged; **do not** enable HTTP request logging for `/hub/*` without scrubbing the query string.

## Background Jobs (`BackgroundJobs/`) — Hangfire
PostgreSQL storage (same `DefaultConnection`) + a Hangfire server. Dashboard at **`/hangfire`**, gated by `HangfireAuthorizationFilter` (defined in `Program.cs`): **loopback-only in BOTH modes** (`LocalRequest.IsLoopback` — server PC yes, LAN clients / proxied requests no; changed from the prior Cloud `return true;`). Jobs are plain classes resolved from DI when enqueued.

| Job | Does | Trigger |
|---|---|---|
| `NotificationJob.ProcessPendingNotifications()` | SMS/WhatsApp reminder outbox dispatcher. Connectivity-gated (`IInternetProbe` — offline sends nothing, consumes no retry). Per-clinic effective settings (`IReminderSettingsProvider`), phone → +216 E.164, per-row commit, bounded retry (`RecordFailedAttempt` → `Failed` at `Reminders:MaxRetries`). Re-checks the appointment is still active before sending. **A row that reaches `Failed` is surfaced**: an in-app `ReminderFailed` staff notification via `INotificationGenerator` (all staff, no actor exclusion), and for a **recall** — once every channel of that one send has failed — `Patient.ClearRecallSnooze()` puts the patient back on the relance list (`TryReturnPatientToRecallListAsync`; a sibling row still `Pending` defers the decision to a later tick, a sibling `Sent` means they really were reached). Deliberately **not** surfaced for the cancelled/no-show void — that is a correct suppression already announced by `AppointmentCancelledAsync`. Both are best-effort: `SurfaceFailureAsync` never throws, so a feed write can neither abort the batch nor cause a re-send. The due-row scan is **bounded** by `Reminders:DispatchBatchSize` (50) like `EInvoiceOutboxJob` (AC-P4.31), and after the dispatch loop it **purges terminal rows** older than `Reminders:RetentionDays` (90) - the table had no purge of any kind, so it grew forever. The purge runs *after* dispatch (EC-13) and never deletes a `Pending` row (AC-P4.34); a purge failure is swallowed so housekeeping cannot stop reminders going out. Served by `IX_Notifications_Status_ScheduledFor`. `[DisableConcurrentExecution(600)]` + `[AutomaticRetry(3)]` | **Recurring** — `Cron.Minutely` (`"process-notifications"`). No-op until a `Reminders` channel + credentials configured |
| `EInvoiceOutboxJob.DispatchQueuedInvoices()` | TTN « El Fatoora » e-invoice outbox dispatcher via `IEInvoiceService` (self-committing, per-invoice). Connectivity-gated; batch size from `TtnConfig`. `[DisableConcurrentExecution(600)]` + `[AutomaticRetry(3)]` | **Recurring** — `Cron.Minutely` (`"dispatch-einvoices"`). No-op until a clinic enables e-invoicing |
| `PdfGenerationJob.GenerateAndAttachPdfAsync(documentId)` | Renders a medical document to PDF (`IPdfGenerationService`) and re-saves it via MediatR. `[AutomaticRetry(3)]` | **Fire-and-forget**, enqueued on demand (`MedicalDocumentsController.GeneratePdf`) |

**Net: two active minutely recurring jobs** (`process-notifications`, `dispatch-einvoices`, both connectivity-gated) + on-demand `PdfGenerationJob`. Google→App calendar sync has **no job** — `Program.cs` keeps only a defensive `RecurringJob.RemoveIfExists("sync-google-calendar")`. App→Google runs inline on appointment create/update; Google→App runs only via the manual `GoogleCalendarController` endpoints.

## `Program.cs` startup pipeline
Reads the auth mode from an early `ConfigurationBuilder` **before** `WebApplication.CreateBuilder` (the Serilog log path + the outer startup-failure catch both need it early). In Local the log file is anchored to `LocalInstallPaths.BaseDirectory/logs/` (a Windows service's CWD is `System32`); Cloud keeps relative `logs/`.

**Console verbs are intercepted before the web host boots** (all Local-only, direct-DB/no-web, exit code):
- `reset-admin-password [email]` → `Maintenance/AdminPasswordResetCommand` (offline admin lockout recovery, wraps Application's `AdminPasswordRecoveryService`).
- `provision-cert` → `Maintenance/ProvisionCertCommand` (idempotently mints/reuses the CA + server cert into `.local/` and exits; the installer runs it before starting the service so first boot reuses the cert under the SCM timeout).
- `harden-permissions <dir>…` → `Maintenance/HardenPermissionsCommand`; `protect-credential`/`read-credential` → `Maintenance/CredentialProtectionCommand`.
- **The two read-only report verbs**, which share exit codes (`0` clean / `1` couldn't run / `2` **ran and found drift**) so they can be scripted identically, and are both meant to be run **before and after a migration batch and diffed**:
  - `reconcile-money [months]` → `Maintenance/ReconcileMoneyCommand` (wraps `MoneyReconciliationService`).
  - `verify-schema` → `Maintenance/VerifySchemaCommand` (wraps `SchemaVerificationService`). The **only** gate for a schema-level change — nothing in the test project touches a database. Both build their container from `AddInfrastructure` **only**, never `AddApplication`, so no `ICurrentClinicProvider` is registered, the global clinic query filters stay inactive, and the reads span every clinic without `IgnoreQueryFilters()`.

Service registration order:
1. **Serilog** (console + daily rolling file, 7-day retention); `UseSerilog()`.
1b. **`UseWindowsService()`** — Local only (auto-start service, content root = install dir; no-op if not launched as a service).
2. `AddControllers()` (camelCase + case-insensitive JSON).
3. Swagger — maps `IFormFile`→binary, registers the parameter + operation upload filters.
4. **JWT bearer — mode-branched.** Cloud: Auth0 (`authority https://{domain}`, validates issuer/audience/lifetime/signing key) only if `Auth0:Domain`+`Auth0:Audience` set. Local: HS256 against the per-install `LocalAuthConfig` key (issuer/audience/lifetime/signature all validated). Both use `CreateHubJwtEvents()` (hub `access_token` query param) and `ClockSkew = Zero`. When auth is configured, `AuthorizationPolicies.ConfigurePolicies(options, isLocalMode)` (Local → fail-closed fallback) + a scoped `RoleAuthorizationHandler` are added.
5. `AddHttpContextAccessor()` (for `ClinicContext`).
6. `AddApplication()` → `AddInfrastructure(config)`.
7. **SignalR** + `IRealtimeNotifier` → `SignalRRealtimeNotifier` (both modes).
8. **Hangfire** + server (PostgreSQL). The DB connection string is required — if empty the app logs Fatal and `return 1` (secrets are no longer committed; supplied out-of-band via env / `appsettings.Production.json` / `appsettings.Development.json`).
8b. **`DeferredStartupService`** — Local only (post-startup migrations, see below).
9. **CORS** `"AllowAll"`: `CorsOrigins.FromConfiguration(config)` = `FrontendUrl` unioned with optional `Cors:AllowedOrigins` (LAN origins), `AllowAnyMethod/Header` + `AllowCredentials()` (so no `AllowAnyOrigin`). Cloud collapses to `FrontendUrl`.
10. **YARP front door (Local only)** — `AddReverseProxy().LoadFromMemory(...)` registers one catch-all route (`/{**catch-all}`, least-specific) → the co-located Next server at `http://localhost:{Hosting:WebPort ?? 3000}`. Kestrel is the single browser-facing endpoint: `/api/*` controllers (more specific) run in-process; everything else proxies to Next. Cloud installs no proxy.
11. **HTTPS / Kestrel bind — mode-branched.** **Local always serves HTTPS**: if `Https:CertPath` is set it must exist (else `StartupDiagnostics.ReportFatal` + `return 1` — no silent HTTP downgrade), otherwise `CertificateProvisioner.EnsureServerCertificate()` self-generates a CA + cert into `.local/`; HTTP binds **loopback-only** on `Hosting:HttpPort` (5000, sole consumer is the Next BFF), HTTPS `ListenAnyIP` on `Hosting:HttpsPort` (5001, the only LAN port). Cloud: opt-in HTTPS only if a cert file exists (`ListenAnyIP` both), else honor `Hosting:Urls`. Cert password comes from `.local/`/env, never committed. A `certSource` (`generated`|`configured`|`cloud`) is logged.

Middleware order (after `Build()`): `UseSwagger`/`UI` (Dev only) → `UseHttpsRedirection` (**Cloud only** — Local must not redirect its loopback HTTP hop to the self-signed front door) → `UseCors` → `UseMiddleware<ExceptionMiddleware>` (from Application; before auth) → `UseAuthentication` → `UseAuthorization` → **`LocalAuthEnforcementMiddleware`** (Local only) → `MapControllers` → **`MapHub<ClinicHub>("/hub/clinic")`** → `UseHangfireDashboard("/hangfire")` → **`MapReverseProxy().AllowAnonymous()`** (Local only — the proxied Next app does its own auth; anonymous or the fail-closed fallback would 401 the login page).

**`LocalAuthEnforcementMiddleware`** (Local only): the app-issued JWT is stateless, so per authenticated request it revokes deactivated local accounts (401) and forces a pending password change (403 `{ error, code: "must_change_password" }`) except on `/api/auth/change-password`.

**EF migrations — mode-branched.** Cloud runs `context.Database.Migrate()` synchronously in `Program.cs` + `IClinicCatalogSeeder.SeedAllClinicsAsync()` backfill. Local defers both to `Startup/DeferredStartupService` (an `IHostedService` that fire-and-forgets the work after the host reports "started"), because applying migrations synchronously as a Windows service blew past the SCM ~30s start timeout (killed mid-migration). `PublishReadyToRun` speeds cold start for that window. The deferred service surfaces a DB-unreachable failure via `StartupDiagnostics.ReportFatal` + `StopApplication()`.

Outer try/catch: `catch when (startupIsLocalMode && StartupDiagnostics.IsAddressInUse(ex))` → clear "port in use" message + `return 1`; else `Log.Fatal` + rethrow; `Log.CloseAndFlush()` in `finally`.

## Startup diagnostics (`Startup/`) — Local mode
`StartupDiagnostics` (static, unit-testable) classifies the two operator-recoverable boot failures into clear French messages: `IsDatabaseConnectionFailure` (socket/timeout/bare `NpgsqlException`, but NOT `PostgresException` = server answered) and `IsAddressInUse`. `ReportFatal(message, ex?)` fans to console + Serilog + (best-effort, Windows-only) the Windows Event Log. Invoked only on the Local startup path (Cloud keeps its fatal-rethrow).

## Key configuration keys (`appsettings.json` / `appsettings.Development.json`) — names only
Secrets are **not committed** (feature cloud-security-and-tenant-isolation, AC-3): committed `appsettings.json` has empty strings + `// SECRET` comments; supply real values via env / user-secrets / `.local/` / the installer's `appsettings.Production.json`.
- `ConnectionStrings:DefaultConnection` (PostgreSQL + Hangfire; **required**, empty → fail-loud)
- `Auth:Mode` (`Cloud`|`Local`), `Auth:Local:SigningKey` (optional; else generated `.local/signing-key`)
- `Connectivity:{ProbeUrl,ProbeTimeoutSeconds,ProbeCacheSeconds}` (Local egress probe)
- `Auth0:{Domain,Audience}`, `Auth0:ManagementApi:{ClientId,ClientSecret}`
- `GoogleCalendar:{ClientId,ClientSecret,RedirectUri,RefreshToken,CalendarId}` (runtime refresh token is stored per-clinic in the DB, not here)
- `HuggingFace:{ApiKey,Model}`
- `MinIO:{Endpoint,AccessKey,SecretKey,BucketName,UseSSL}`, `FileStorage:BasePath`
- `Reminders:{Channels,LeadTimesHours,MinLeadHours,MaxRetries}` + `Reminders:Sms:{ApiUrl,SenderId,ApiKey}` + `Reminders:WhatsApp:{ApiUrl,PhoneNumberId,TemplateName,TemplateLanguage,AccessToken}` (live SMS/WhatsApp reminder outbox)
- `Meta:{AppId,AppSecret,GraphApiVersion}` (WhatsApp Embedded Signup — Cloud onboarding, 404 in Local)
- `Notification:Smtp:*` / `Notification:Sms:*` (dormant legacy)
- `FrontendUrl` (primary CORS origin + OAuth success redirect); `Cors:AllowedOrigins` (optional LAN origins)
- `Https:{CertPath,CertPassword}`, `Hosting:{Urls,HttpPort,HttpsPort,WebPort}` (LAN hosting / front door; Local self-generates the cert into `.local/` when `CertPath` empty)
- `Backup:{PgDumpPath,DefaultDestination,TimeoutSeconds}` (one-click backup)
- `Serilog:*`, `Logging:*`, `AllowedHosts`

> Cloud still carries some non-secret real values (`Auth0:Domain/Audience`, `GoogleCalendar:ClientId`). Prefer env vars / user-secrets / a vault for anything sensitive.
