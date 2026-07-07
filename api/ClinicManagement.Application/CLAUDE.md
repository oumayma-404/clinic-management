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
`Send(command/query)` → **ValidationBehavior** → **LoggingBehavior** → **Handler**. Behaviors live in `Common/Behaviors/`:

- **`ValidationBehavior<TRequest,TResponse>`** — resolves all `IValidator<TRequest>`, runs them, throws `FluentValidation.ValidationException` on failure. (No `AbstractValidator`s exist yet, so this is effectively a no-op today; handlers currently do validation inline and return `Result.Failure(...)`.)
- **`LoggingBehavior<TRequest,TResponse>`** — logs "Handling/Handled {RequestName}".

Order is registration order (Validation first, then Logging).

## Result pattern — `Common/Models/Result.cs`
- `Result` — `IsSuccess` / `IsFailure` / `Error`; `Result.Success()`, `Result.Failure(error)`.
- `Result<T>` — adds `Value`; `Result<T>.Success(value)`, `Result<T>.Failure(error)`.
- **Convention:** almost every handler returns `Result<TDto>`, wraps its body in `try/catch`, and converts exceptions/business errors into `Result.Failure(...)`. The API layer maps this to HTTP responses.

## Feature folders (CQRS) — `Features/<Area>/{Commands,Queries,EventHandlers}`
Each command/query file typically contains **both** the request class (`IRequest<Result<...>>`) and its handler (`IRequestHandler<...>`) in one file. Handlers inject repositories (Domain interfaces), `IClinicContext`, `IUnitOfWork`, and service interfaces.

| Area | Commands | Queries | Event handlers |
|------|----------|---------|----------------|
| **Appointments** | `CreateAppointmentCommand`, `UpdateAppointmentCommand` | `GetAppointmentQuery`, `GetAppointmentsQuery` | `AppointmentCreatedEventHandler` |
| **Patients** | Create/Update `PatientCommand`; medical & family history Create/Update/Delete; dental record Create/Update/Delete | `GetPatientQuery`, `GetPatientsQuery`, `GetPatient{Medical,Family}HistoryQuery`, `GetDentalRecordsQuery` | — |
| **Clinics** | `CreateClinicCommand`, `UpdateClinicCommand`, `JoinClinicCommand`, `UpdateDoctorsCommand` | `GetUserStatusQuery`, `GetClinicLogoQuery` | — |
| **ProcedureTypes** | Create/Update/Delete | `GetProcedureTypeQuery`, `GetProcedureTypesQuery` | — |
| **Files** | `CreatePatientFolderCommand`, `DeletePatientFolderCommand`, `UploadPatientFileCommand`, `DeletePatientFileCommand`, `InitializeDefaultFoldersCommand` | `GetPatientFoldersQuery`, `GetPatientFilesQuery`, `DownloadPatientFileQuery` | — |
| **Documents** | Create/Update/Delete `MedicalDocumentCommand` | `GetMedicalDocumentQuery`, `GetMedicalDocumentsQuery` | — |
| **Users** | — | `GetUsersQuery` | — |
| **AI** | `ChatCommand` (+ `ChatCommandHandler.cs` as a separate handler file) | — | — |

### Event handlers
Domain events (`IDomainEvent : INotification`) are handled here via `INotificationHandler<TEvent>`. Example: **`Features/Appointments/EventHandlers/AppointmentCreatedEventHandler.cs`** creates a 24h-before reminder `Notification`. (Events are dispatched by Infrastructure/EF when `SaveChanges` runs.)

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
| `IFileStorage` | MinIO-style upload/download/delete by storage key (custom path overload). |
| `IFileStorageService` | Alternative file storage contract (save/get/delete/exists by path). |
| `IGoogleCalendarService` | Low-level Google Calendar CRUD; exposes `GoogleCalendarEvent`. |
| `IGoogleCalendarSyncService` | Two-way sync of appointments ↔ Google Calendar. |
| `IPdfGenerationService` | Generate PDF from `MedicalDocumentPdfData`. |
| `IAuth0ManagementService` | Push `clinic_id`/`role` into Auth0 `app_metadata`. |
| `IAIActionService` | Decide & execute AI-driven actions (defines `AIActionRequest`/`AIActionResult`). |
| `IGoogleAIService` / `IHuggingFaceAIService` | Chat completions for the AI feature; define message/response/token DTOs. |

## DTOs — `DTOs/`
Plain request/response records used by handlers & controllers: `PatientDto`, `AppointmentDto`, `ClinicDto`, `DoctorPersonalInfoDto`, `UserDto`, `UserStatusDto`, `AddressDto`, `InsuranceInfoDto`, `PatientFlagDto`, `PatientMedicalHistoryDto`, `PatientFamilyHistoryDto`, `DentalRecordDto`, `PatientFileDto`, `MedicalDocumentDto`, `ProcedureTypeDto`, plus request shapes `CreateClinicRequest`, `JoinClinicRequest`, `UpdateDoctorsRequest`. `Common/Models/MedicalDocumentPdfData.cs` is the PDF-generation model.

## Cross-cutting — `Common/`
- **Exceptions** (`Common/Exceptions/`): `NotFoundException`, `ForbiddenAccessException`, and **`ExceptionMiddleware`** (ASP.NET middleware mapping these to 404/403, everything else → 500 with a generic JSON body).
- **Authorization** (`Common/Authorization/`): policy-based.
  - `AuthorizationPolicies.cs` — policy names `DoctorOrSecretary`, `DoctorOnly`, `SecretaryOnly`, `AdminOnly` + `ConfigurePolicies(...)`.
  - `Requirements/RoleRequirement.cs` — `params string[] AllowedRoles`.
  - `Handlers/RoleAuthorizationHandler.cs` — reads role claim (tries `https://clinic-management.com/role`, `role`, `ClaimTypes.Role`) and succeeds if it matches an allowed role.

## Gotchas
- **No FluentValidation validators are defined yet** — `ValidationBehavior` runs but finds none; validation is inline in handlers via `Result.Failure`.
- Clinic scoping is resolved per-request from the DB (`User.ClinicId`), not just from the JWT `clinic_id` claim.
- Some handlers swallow non-critical failures (e.g. Auth0 metadata update in `CreateClinicCommand`) so the core use case still succeeds.
- Two parallel file-storage interfaces exist (`IFileStorage`, `IFileStorageService`) and two AI chat providers (`IGoogleAIService`, `IHuggingFaceAIService`) — check which the relevant handler injects.
