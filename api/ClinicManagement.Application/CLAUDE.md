# ClinicManagement.Application

> The use-case layer. Implements CQRS with **MediatR**: every API operation is a `Command` or `Query` with a co-located handler. Depends on Domain (entities + repository interfaces); defines its own infrastructure-facing interfaces (implemented in Infrastructure). Returns a `Result<T>` instead of throwing for business failures.

## Dependencies
MediatR, FluentValidation, `Microsoft.AspNetCore.*` (for `IHttpContextAccessor`, authorization, middleware), EF Core (only `using` references in a few queries). DI entry point: **`Extensions.cs`** → `services.AddApplication()`.

## DI wiring — `Extensions.cs`
```
AddApplication():
  AddMediatR(...from executing assembly)        // registers all handlers & event handlers
  AddValidatorsFromAssembly(...)                // FluentValidation (no validators currently defined)
  AddTransient(IPipelineBehavior<,>, ValidationBehavior<,>)
  AddTransient(IPipelineBehavior<,>, LoggingBehavior<,>)
  AddScoped<IClinicContext, ClinicContext>()    // needs IHttpContextAccessor (registered in API/Program.cs)
```

## MediatR pipeline (request → response)
`Send(command/query)` → **ValidationBehavior** → **LoggingBehavior** → **RealtimeBroadcastBehavior** → **Handler**. Behaviors live in `Common/Behaviors/`:

- **`ValidationBehavior<TRequest,TResponse>`** — resolves all `IValidator<TRequest>`, runs them, throws `FluentValidation.ValidationException` on failure. (No `AbstractValidator`s exist yet, so this is effectively a no-op today; handlers currently do validation inline and return `Result.Failure(...)`.)
- **`LoggingBehavior<TRequest,TResponse>`** — logs "Handling/Handled {RequestName}".
- **`RealtimeBroadcastBehavior<TRequest,TResponse>`** — the single wiring point for real-time "any edit is live". After the handler returns (i.e. after its commit), if the request is a mutating command (namespace `...Features.<Area>.Commands`, excluding `Auth`/`AI`/`Backup`/`Connectivity`) **and** the response is a successful `Result`, it resolves the caller's clinic (same `IClinicContext`→`IUserRepository` lookup handlers use) and calls `IRealtimeNotifier.NotifyEntityChangedAsync(clinicId, "<area>")` — clients of that clinic then refetch. Purely structural (no per-command marker), so new commands broadcast automatically. Additive/fail-safe: it runs after commit (a failed command's `Result` is a failure → no broadcast) and swallows any resolution/broadcast error so a broadcast can never fail the committed command. `IRealtimeNotifier` is implemented in the API layer (`SignalRRealtimeNotifier` over the `ClinicHub`).

Order is registration order (Validation → Logging → RealtimeBroadcast).

## Result pattern — `Common/Models/Result.cs`
- `Result` — `IsSuccess` / `IsFailure` / `Error`; `Result.Success()`, `Result.Failure(error)`.
- `Result<T>` — adds `Value`; `Result<T>.Success(value)`, `Result<T>.Failure(error)`.
- **Convention:** almost every handler returns `Result<TDto>`, wraps its body in `try/catch`, and converts exceptions/business errors into `Result.Failure(...)`. The API layer maps this to HTTP responses.

## Feature folders (CQRS) — `Features/<Area>/{Commands,Queries,EventHandlers}`
Each command/query file typically contains **both** the request class (`IRequest<Result<...>>`) and its handler (`IRequestHandler<...>`) in one file. Handlers inject repositories (Domain interfaces), `IClinicContext`, `IUnitOfWork`, and service interfaces.

| Area | Commands | Queries | Event handlers |
|------|----------|---------|----------------|
| **Appointments** | `CreateAppointmentCommand`, `UpdateAppointmentCommand` (both call `INotificationGenerator` post-commit) | `GetAppointmentQuery`, `GetAppointmentsQuery` | — |
| **Notifications** (in-app feed) | `MarkNotificationReadCommand`, `MarkAllNotificationsReadCommand` (both return `Result.Failure("...not found")` on tenant/missing — a **not-found convention**, mapped to 404 by the controller) | `GetNotificationsQuery` (50 newest, viewer-scoped), `GetUnreadCountQuery` | — |
| **Patients** | Create/Update `PatientCommand`; medical & family history Create/Update/Delete; dental record Create/Update/Delete | `GetPatientQuery`, `GetPatientsQuery`, `GetPatient{Medical,Family}HistoryQuery`, `GetDentalRecordsQuery` | — |
| **Clinics** | `CreateClinicCommand`, `UpdateClinicCommand`, `JoinClinicCommand`, `UpdateDoctorsCommand`, `RegenerateClinicCodeCommand` (admin-only) | `GetUserStatusQuery`, `GetClinicLogoQuery` | — |
| **ProcedureTypes** | Create/Update/Delete | `GetProcedureTypeQuery`, `GetProcedureTypesQuery` | — |
| **Files** | `CreatePatientFolderCommand`, `DeletePatientFolderCommand`, `UploadPatientFileCommand`, `DeletePatientFileCommand`, `InitializeDefaultFoldersCommand` | `GetPatientFoldersQuery`, `GetPatientFilesQuery`, `DownloadPatientFileQuery` | — |
| **Documents** | Create/Update/Delete `MedicalDocumentCommand` | `GetMedicalDocumentQuery`, `GetMedicalDocumentsQuery` | — |
| **Users** | `ResetUserPasswordCommand`, `SetUserActiveCommand` (admin-only) | `ListUsersQuery` (admin-only; users + status) | — |
| **Auth** (Local mode) | `LoginCommand` (email+password → JWT; rejects inactive/locked; generic `InvalidCredentialsError`), `ChangePasswordCommand` (clears `MustChangePassword`) | — | — |
| **AI** | `ChatCommand` (+ `ChatCommandHandler.cs` as a separate handler file) | — | — |
| **Connectivity** (Local mode) | — | `GetConnectivityStatusQuery` (probes internet egress via `IInternetProbe`; returns `ConnectivityStatusDto`; swallows probe errors into `internetReachable=false` — never a 500 for a poll) | — |
| **Backup** (Local, Phase 5) | `BackupNowCommand` (admin-only one-click backup — resolves caller, re-checks `IsAdmin()`, delegates to `IBackupService`; catches `InvalidOperationException` → `Result.Failure` with the operator message, lets other exceptions propagate to middleware) | — | — |

### Event handlers
Domain events (`IDomainEvent : INotification`) implement `INotificationHandler<TEvent>`, **but no domain-event dispatch is currently wired** — `SaveChanges` does not drain `AggregateRoot.DomainEvents`, so no handler runs. The former `AppointmentCreatedEventHandler` was removed as dead code (had it ever fired, it would have enqueued a `NotificationType.Both` reminder the `NotificationJob` dispatcher has no sender for). Appointment reminders are produced inline by `ReminderScheduler` + `NotificationGenerator` from the command handlers instead.

### Handler conventions (see `CreateAppointmentCommand.cs`, `GetPatientsQuery.cs`, `CreateClinicCommand.cs`)
1. Read current user via `IClinicContext.GetUserId()`; load `User` via `IUserRepository.GetByAuth0SubAsync` to resolve the **clinic id** (multi-tenant scoping).
2. Validate inputs / load related aggregates; return `Result.Failure` on any problem.
3. Mutate domain via aggregate methods, persist via repository `AddAsync/UpdateAsync`, then **`IUnitOfWork.SaveChangesAsync`** (one save per use case).
4. Map the aggregate to a DTO and return `Result<TDto>.Success`.

## Common interfaces — `Common/Interfaces/` (implemented in Infrastructure)
| Interface | Purpose |
|-----------|---------|
| `IUnitOfWork` | `SaveChangesAsync` + explicit `Begin/Commit/RollbackTransactionAsync`. |
| `IClinicContext` | Reads clinic id / role / user id / email from JWT claims; `BelongsToClinic`, `EnsureClinicAccess` (throws `ForbiddenAccessException`). **Implemented in this layer** (`Common/Services/ClinicContext.cs`). |
| `INotificationGenerator` | Best-effort writer for the in-app staff feed (`StaffNotification`). Methods `AppointmentCreatedAsync`/`ScheduleAppointmentReminderAsync`/`AppointmentCancelledAsync`/`AppointmentRescheduledAsync`/`LowStockAsync`, called **inline from command handlers after their own commit**. Each persists its notification(s) + broadcasts the `"notifications"` realtime key but **never throws back** — a failure must never fail/roll back the core operation. **Implemented in this layer** (`Common/Services/NotificationGenerator.cs`), not Infrastructure. |
| `IFileStorage` | Blob upload/download/delete by storage key (custom path overload). Backend is mode-branched: MinIO (Cloud) or `LocalDiskFileStorage` (Local). |
| `IGoogleCalendarService` | Low-level Google Calendar CRUD; exposes `GoogleCalendarEvent`. |
| `IGoogleCalendarSyncService` | Two-way sync of appointments ↔ Google Calendar. |
| `IPdfGenerationService` | Generate PDF from `MedicalDocumentPdfData`. |
| `IAuth0ManagementService` | Push `clinic_id`/`role` into Auth0 `app_metadata`. Local mode wires a no-op impl. |
| `ILocalAuthService` | Local-mode auth (Phase 1): `HashPassword`/`VerifyPassword` (ASP.NET `PasswordHasher`, PBKDF2), `GenerateToken` (HS256 JWT via the per-install key), `GenerateTemporaryPassword` (CSPRNG). Impl in Infrastructure. |
| `IAIActionService` | Decide & execute AI-driven actions (defines `AIActionRequest`/`AIActionResult`). |
| `IHuggingFaceAIService` | Chat completions for the AI feature (the sole wired AI backend); defines message/response/token DTOs. *(The parallel `IGoogleAIService` was removed as dead code in `reliability-and-polish`.)* |
| `IInternetProbe` | Connectivity awareness (Phase 3): `IsInternetReachableAsync()` — does the **server** have working internet egress. Impl in Infrastructure (Singleton, cached). Backs the Local-only `GET /api/connectivity` used to gate AI + Google Calendar offline. |
| `IGoogleTokenStore` | Google OAuth refresh-token persistence (Phase 4 / US-3): `GetRefreshToken()` + `SaveRefreshTokenAsync(...)`. Stores the token in a gitignored per-install `.local/` file (falling back to `GoogleCalendar:RefreshToken` config), **replacing** the old callback that rewrote the token into committed `appsettings.json`. Impl in Infrastructure (Singleton, in-memory cache for read-after-write). |
| `IBackupService` | One-click "Backup now" (Phase 5 / US-8 / FR-G): `CreateBackupAsync(destinationFolder?, ct)` → `BackupResultDto`. A seam over the `pg_dump` shell-out so the command handler stays unit-testable/mockable. Contract: surface every operator-facing failure (unwritable dest, disk full, `pg_dump` missing, dump failed) as a distinct `InvalidOperationException` — never a silent partial success. Impl `PgDumpBackupService` in Infrastructure. |

## DTOs — `DTOs/`
Plain request/response records used by handlers & controllers: `PatientDto`, `AppointmentDto` (includes `IsSyncedToGoogle`, derived from `GoogleCalendarEventId != null` — mapped in all four Create/Update/Get/GetAll handlers; drives the "not synced to Google" badge), `NotificationDto` (in-app feed row: id, category, title, message, `EffectiveFeedTime`, `IsRead`, target kind + optional appointment/stock id), `ConnectivityStatusDto` (`InternetReachable`), `BackupResultDto` (`DestinationPath`/`SizeBytes`/`TimestampUtc`, Phase 5), `ClinicDto`, `DoctorPersonalInfoDto`, `UserDto`, `UserStatusDto`, `AddressDto`, `InsuranceInfoDto`, `PatientFlagDto`, `PatientMedicalHistoryDto`, `PatientFamilyHistoryDto`, `DentalRecordDto`, `PatientFileDto`, `MedicalDocumentDto`, `ProcedureTypeDto`, plus request shapes `CreateClinicRequest`, `JoinClinicRequest`, `UpdateDoctorsRequest`. `Common/Models/MedicalDocumentPdfData.cs` is the PDF-generation model.

## Cross-cutting — `Common/`
- **Services** (`Common/Services/`): `ClinicContext` (`IClinicContext`) and **`NotificationGenerator`** (`INotificationGenerator`) — the in-app-feed writer. Lives here (not Infrastructure) because it only needs the domain repos + `IUnitOfWork` + `IRealtimeNotifier`; its `SafelyAsync` helper wraps each write so an exception is logged at **Error** and swallowed (post-commit best-effort), then broadcasts the `"notifications"` realtime key.
- **Maintenance** (`Common/Maintenance/`): `AdminPasswordRecoveryService` — the testable core of the offline admin-lockout recovery utility (find admin → temp password → `SetPassword` → persist). Deliberately **not** DI-registered (no HTTP-reachable reset path); driven only by the `reset-admin-password` CLI wrapper in the API project. Lives here because `UnitTests` references only Application.
- **Exceptions** (`Common/Exceptions/`): `NotFoundException`, `ForbiddenAccessException`, and **`ExceptionMiddleware`** (ASP.NET middleware mapping these to 404/403, everything else → 500 with a generic JSON body).
- **Authorization** (`Common/Authorization/`): policy-based.
  - `AuthorizationPolicies.cs` — policy names `DoctorOrSecretary`, `DoctorOnly`, `SecretaryOnly`, `AdminOnly` + `ConfigurePolicies(options, isLocalMode)`. In **Local** mode (Phase 4 / FR-E3 release gate) it installs a fail-closed `FallbackPolicy = RequireAuthenticatedUser()` so every endpoint without an explicit `[AllowAnonymous]` returns 401; in **Cloud** the fallback stays null (named policies only) → Cloud unchanged.
  - `Requirements/RoleRequirement.cs` — `params string[] AllowedRoles`.
  - `Handlers/RoleAuthorizationHandler.cs` — reads role claim (tries `https://clinic-management.com/role`, `role`, `ClaimTypes.Role`) and succeeds if it matches an allowed role.

## Gotchas
- **No FluentValidation validators are defined yet** — `ValidationBehavior` runs but finds none; validation is inline in handlers via `Result.Failure`.
- Clinic scoping is resolved per-request from the DB (`User.ClinicId`), not just from the JWT `clinic_id` claim.
- Some handlers swallow non-critical failures (e.g. Auth0 metadata update in `CreateClinicCommand`) so the core use case still succeeds.
- **`CreateClinicCommand` / `JoinClinicCommand` are dual-path**: a non-null `Password` on the request switches them into the Local first-run / self-registration branch (creates a password-backed `User`); a null `Password` keeps the original Cloud/Auth0 flow. Only the Local-mode `AuthController` endpoints (`setup`/`register`) ever set `Password`.
- File storage goes through the single `IFileStorage` seam (backend chosen by `Auth:Mode`); handlers that store a blob then persist a DB record clean up the blob if the save fails (FR-C3 orphan prevention). `IHuggingFaceAIService` is the wired AI chat provider (the unused `IGoogleAIService` was removed in `reliability-and-polish`).
