# ClinicManagement.Domain

> The innermost Clean Architecture layer: pure C# domain model (entities, aggregates, value objects, domain events, enums) plus repository/service **interfaces**. No EF Core, no ASP.NET, no infrastructure dependencies. Only outside dependency is **MediatR** (used by `IDomainEvent : INotification`).

## Folder map

| Folder | Contents |
|--------|----------|
| `Common/` | Base classes: `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `IDomainEvent` |
| `Entities/` | All domain entities & aggregates |
| `ValueObjects/` | Immutable value objects |
| `Events/` | Domain events (implement `IDomainEvent`) |
| `Enums/` | Domain enums |
| `Repositories/` | Repository interfaces (implemented in Infrastructure) |
| `Services/` | Domain service interfaces |

## Base classes & patterns (`Common/`)

- **`Entity<TId>`** (`Common/Entity.cs`) — identity-based equality (`==`, `!=`, `Equals`, `GetHashCode` by `Id`). `Id` has a `protected set`. Generic over the id type (most use `Guid`, `User` uses `string`).
- **`AggregateRoot<TId> : Entity<TId>`** (`Common/AggregateRoot.cs`) — adds a private `_domainEvents` list, exposes `DomainEvents` (read-only), `AddDomainEvent(...)` (protected), `ClearDomainEvents()`. Aggregate roots are the transactional consistency boundary and the only entities that raise events.
- **`ValueObject`** (`Common/ValueObject.cs`) — structural equality via abstract `GetEqualityComponents()`. Subclasses `yield return` their components.
- **`IDomainEvent : MediatR.INotification`** (`Common/IDomainEvent.cs`) — marker with `DateTime OccurredOn`. Because it extends `INotification`, events are dispatched through MediatR (handlers live in the Application layer).

### Conventions every domain type follows
- Private parameterless ctor `private X() { }` **for EF Core**; a public ctor that sets `Id` and timestamps (`CreatedAt = DateTime.UtcNow`).
- All setters are `private set;` — state changes only through intention-revealing methods (`Confirm()`, `AddStock()`, `UpdatePersonalInfo()`...).
- Invariants enforced in ctors/methods via `ArgumentNullException` / `ArgumentException` / `InvalidOperationException`.
- `CreatedAt` / nullable `UpdatedAt` timestamp fields are pervasive; `UpdatedAt` is bumped on mutation.
- Child collections are `private readonly List<>` exposed as `IReadOnlyCollection<>`.

## Aggregate Roots (`AggregateRoot<...>`)

| Aggregate | File | Key responsibilities / relationships |
|-----------|------|--------------------------------------|
| `Patient` | `Entities/Patient.cs` | Central aggregate. Holds `Email`, `PhoneNumber`, optional `Address`, `InsuranceInfo` (value objects). Owns child collections: `Flags` (`PatientFlag`), `Files` (`PatientFile`), `Appointments`, `MedicalHistoryEntries`, `FamilyHistoryEntries`. **Raises `PatientFlagAddedEvent`** in `AddFlag`. Belongs to a `Clinic` (`ClinicId`). |
| `Appointment` | `Entities/Appointment.cs` | State machine over `AppointmentStatus` via `Confirm/Start/Complete/Cancel/MarkAsNoShow/Reschedule`. Optional `PatientId`, free-text `DoctorId`/`DoctorName`, optional `ProcedureTypeId` (+ snapshot duration/color). Holds `GoogleCalendarEventId` for sync. **Raises `AppointmentCreatedEvent`** (ctor, if patient set), **`AppointmentConfirmedEvent`** (`Confirm`), **`AppointmentRescheduledEvent`** (`Reschedule`). |
| `Clinic` | `Entities/Clinic.cs` | Tenant root. `Name`, contact info, unique join `Code`, `LogoUrl` (MinIO key). Owns `Users`, `Patients`, `Appointments`. |
| `Doctor` | `Entities/Doctor.cs` | Doctor profile in a clinic; `LinkToUser(userId)` ties it to an Auth0 `User`. `FullName` computed. |
| `User` | `Entities/User.cs` | **`AggregateRoot<string>`** — Id is the **Auth0 `sub`** (Cloud) or `local\|{guid}` (Local mode). `Role` string ("doctor"/"secretary"/"admin") with `IsDoctor/IsSecretary/IsAdmin` helpers. Belongs to a `Clinic`. **Local-auth fields (Phase 1):** nullable `PasswordHash`, `MustChangePassword`, `IsActive`, plus lockout state (`FailedLoginAttempts`, `LockoutEnd`). Factory `CreateLocalUser(...)` (trims + lowercases the email); `SetPassword(hash, mustChangePassword)` also clears lockout/failed-attempt state; `Activate()`/`Deactivate()`; failed-login + lockout methods. Cloud users leave `PasswordHash` null. |
| `ProcedureType` | `Entities/ProcedureType.cs` | Catalog of procedures: `Name`, `DefaultDurationMinutes` (1–479), `DefaultCost`, `Color` (`ColorHex` VO), active flag. `IsUsedByFutureAppointments(...)` guards deletion. |
| `StockItem` | `Entities/StockItem.cs` | Inventory item. `AddStock/RemoveStock` (with guards), `UpdateStockLevels`, `IsLowStock()/IsOutOfStock()`. |
| `StaffNotification` | `Entities/StaffNotification.cs` | In-app staff notification feed record (bell/panel). **One shared clinic-scoped row per event** — per-user read state lives in the separate `NotificationRead` (no write-time fan-out). `Category`, `Title`/`Message` (French), `EffectiveFeedTime` (UTC; the ordering + due-ness `<= now` + late-joiner baseline anchor — creation time for immediate categories, due time for `Reminder`), `ActorUserId` (the user who caused it, excluded from their own feed; null for reminders/low-stock), `TargetKind` + optional `AppointmentId`/`StockItemId` for deep-links. `MoveReminder(newDueTime, title, message)` repoints a pending reminder on reschedule. Deliberately **separate** from the dormant email/SMS `Notification`. |

## Non-root Entities (`Entity<Guid>`)

| Entity | File | Notes |
|--------|------|-------|
| `PatientFlag` | `Entities/PatientFlag.cs` | Flag of `PatientFlagType` on a patient; activate/deactivate. |
| `PatientMedicalHistory` | `Entities/PatientMedicalHistory.cs` | Patient's own medical history entry. |
| `PatientFamilyHistory` | `Entities/PatientFamilyHistory.cs` | Family history (relationship + condition). |
| `DentalRecord` | `Entities/DentalRecord.cs` | Dental intervention: procedure, cost/amount paid, notes & important notes lists, owns `Teeth` (`DentalRecordTooth`). `AddTooth/RemoveTooth`. |
| `DentalRecordTooth` | `Entities/DentalRecordTooth.cs` | A tooth on a record, validated against **FDI notation** (11–48 adult, 51–85 child). `IsAdultTooth(...)` helper. |
| `PatientFile` | `Entities/PatientFile.cs` | File metadata: `StorageKey` (MinIO), `ContentType`, `FileSize`, `FileType`, optional `FolderId`. |
| `PatientFolder` | `Entities/PatientFolder.cs` | Nestable folder (`ParentFolderId`) holding sub-folders & files; enforces same-patient invariant. |
| `MedicalDocument` | `Entities/MedicalDocument.cs` | Generated document (prescription / liaison / honoraires / certificat). Stores `ContentJson` plus **snapshots** of patient/clinic/doctor info; `IsDraft`, optional `FileId`. |
| `RecurringAppointment` | `Entities/RecurringAppointment.cs` | Recurrence template (pattern, interval, start/end) for a patient. |
| `Notification` | `Entities/Notification.cs` | Scheduled **email/SMS** reminder (dormant outbound pipeline). Status lifecycle `MarkAsSent/MarkAsFailed/Retry`; tracks `RetryCount`. Not the in-app feed — that is `StaffNotification`. |
| `NotificationRead` | `Entities/NotificationRead.cs` | Per-user read marker for a `StaffNotification` (`NotificationId` + `UserId`, timestamp). Existence = "this user read it". Scoped by `UserId` only (a user belongs to one clinic) — no `ClinicId` column, so it is **not** in the global clinic query filter; every query filters it by the current `UserId`. |

## Value Objects (`ValueObjects/`)

| VO | File | Validation / behavior |
|----|------|-----------------------|
| `Email` | `ValueObjects/Email.cs` | Validates via `MailAddress`, stores lowercased. |
| `PhoneNumber` | `ValueObjects/PhoneNumber.cs` | Non-empty, trimmed. |
| `Address` | `ValueObjects/Address.cs` | Street/City/State/Zip required, optional Country. |
| `InsuranceInfo` | `ValueObjects/InsuranceInfo.cs` | Provider + policy number required. |
| `ColorHex` | `ValueObjects/ColorHex.cs` | `#RRGGBB`, **must be in a curated palette** that mirrors the frontend. `IsValid`, `FromString`, `GetAvailableColors`. Used by `ProcedureType`. |

## Domain Events (`Events/`)

All implement `IDomainEvent` (carry `OccurredOn = DateTime.UtcNow`). Handlers are in `ClinicManagement.Application/Features/.../EventHandlers`.

| Event | File | Raised by |
|-------|------|-----------|
| `AppointmentCreatedEvent` | `Events/AppointmentCreatedEvent.cs` | `Appointment` ctor (when a patient is attached) |
| `AppointmentConfirmedEvent` | `Events/AppointmentConfirmedEvent.cs` | `Appointment.Confirm()` |
| `AppointmentRescheduledEvent` | `Events/AppointmentRescheduledEvent.cs` | `Appointment.Reschedule()` |
| `PatientFlagAddedEvent` | `Events/PatientFlagAddedEvent.cs` | `Patient.AddFlag()` |

## Enums (`Enums/`)

| Enum | Values |
|------|--------|
| `AppointmentStatus` | Scheduled, Confirmed, InProgress, Completed, Cancelled, NoShow |
| `PatientFlagType` | HighPriority, SpecialCondition, Alert, Critical, Allergy |
| `FileType` | LabResult, Scan, Prescription, MedicalRecord, Insurance, Other |
| `NotificationType` | Email, SMS, Both (outbound `Notification` only) |
| `NotificationStatus` | Pending, Sent, Failed (outbound `Notification` only) |
| `NotificationCategory` | AppointmentCreated, AppointmentCancelled, AppointmentRescheduled, Reminder, LowStock (in-app `StaffNotification`) |
| `NotificationTargetKind` | None, Appointment, StockItem (drives the panel deep-link) |

## Repository interfaces (`Repositories/`)

Async, `CancellationToken`-aware contracts implemented in the Infrastructure layer. Persistence is committed via the Application's `IUnitOfWork` (most repos do **not** save themselves).

| Interface | Notable methods |
|-----------|-----------------|
| `IPatientRepository` | `GetByIdWithAppointmentsAsync`, `GetByClinicIdAsync`, `GetFlaggedPatientsAsync`, `AddMedical/FamilyHistoryEntryAsync` |
| `IAppointmentRepository` | `GetByClinicIdAsync(date range)`, `GetUpcomingAppointmentsAsync`, `GetAppointmentsForDateAsync`, `GetByProcedureTypeIdAsync` |
| `IClinicRepository` | `GetByCodeAsync`, `GetByNameAsync`, `CodeExistsAsync` |
| `IUserRepository` | `GetByAuth0SubAsync`, `GetByEmailAsync` (Local login; filters `PasswordHash IS NOT NULL`), clinic scoping; sync `Update`/`Remove` |
| `IDoctorRepository` | `GetByClinicIdAsync`, `GetByUserIdAsync` |
| `IProcedureTypeRepository` | `GetActiveAsync`, `GetByNameAsync`, `ExistsByNameAsync` |
| `IDentalRecordRepository` | `GetByPatientIdAsync` |
| `IPatientFileRepository` | by patient / folder / root |
| `IPatientFolderRepository` | by patient / root / sub-folders / name |
| `IMedicalDocumentRepository` | by patient / document type |
| `IStockItemRepository` | `GetLowStockItemsAsync`, `GetOutOfStockItemsAsync` |
| `INotificationRepository` | `GetPendingNotificationsAsync`, `GetByAppointmentIdAsync` (outbound email/SMS `Notification`) |
| `IStaffNotificationRepository` | In-app feed. `GetRecentForUserAsync` (newest-first, actor-excluded, 50-cap), `CountUnreadAsync`/`GetUnreadForUserAsync` (due + not-actor + at/after the viewer's join time + no read marker), `GetReadNotificationIdsAsync`, `ReadMarkerExistsAsync`/`AddReadMarkerAsync`, `GetReminderByAppointmentAsync`. Due-ness/actor-exclusion/late-joiner-baseline/cap all live in the query impls. |

## Domain services (`Services/`)

- **`IPatientSummaryService`** (`Services/IPatientSummaryService.cs`) — `GenerateSummaryAsync(Patient, Appointment, ...)`; produces an AI summary (implemented in Infrastructure).

## Gotchas
- `User` is keyed by **string** — the Auth0 `sub` in Cloud mode, or `local|{guid}` in Local mode — unlike all other `Guid`-keyed types.
- `Appointment.PatientId` and `DoctorId` are optional — appointments can be "blocked"/occupied slots without a patient; events only fire when a patient is present.
- `Appointment` keeps **denormalized snapshots** of procedure duration/color; `MedicalDocument` snapshots patient/clinic/doctor data — these are intentional, not normalized FKs.
- `ColorHex` palette must stay in sync with the frontend `COLOR_PALETTE`.
