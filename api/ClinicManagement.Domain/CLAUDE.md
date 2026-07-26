# ClinicManagement.Domain

> The innermost Clean Architecture layer: pure C# domain model (entities, aggregates, value objects, enums, a couple of pure domain services) plus repository **interfaces**. No EF Core, no ASP.NET, no infrastructure dependencies. The `.csproj` has **zero package/project references** — the dead domain-events pipeline (its only reason for a `MediatR.Contracts` ref) was removed in `french-localization-and-cleanup`.

## Folder map

| Folder | Contents |
|--------|----------|
| `Common/` | Base classes (`Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`) + `FdiTooth` tooth-notation helper |
| `Entities/` | All domain entities & aggregates |
| `ValueObjects/` | Immutable value objects |
| `Enums/` | Domain enums |
| `Services/` | Pure domain services (currently `InvoiceCalculator`) |
| `Repositories/` | Repository interfaces (implemented in Infrastructure) |

## Base classes & patterns (`Common/`)

- **`Entity<TId>`** (`Common/Entity.cs`) — identity-based equality (`==`/`!=`/`Equals`/`GetHashCode` by `Id`, with a runtime-type check). `Id` has a `protected set`. Generic over the id type (most use `Guid`; `User` uses `string`).
- **`AggregateRoot<TId> : Entity<TId>`** (`Common/AggregateRoot.cs`) — a bare marker base for aggregate roots (the transactional consistency boundary). Carries **no** domain-events list (the dead pipeline was removed).
- **`ValueObject`** (`Common/ValueObject.cs`) — structural equality via abstract `GetEqualityComponents()` (subclasses `yield return` their components).
- **`FdiTooth`** (`Common/FdiTooth.cs`) — static FDI tooth-number validator (`IsValid`/`IsAdult`; adult 11–48, child 51–85). Shared by `DentalRecordAct`, `TreatmentPlanItem`, and `ToothState`.

### Conventions every domain type follows
- Private parameterless ctor `private X() { }` **for EF Core**; a public ctor (or static factory) that sets `Id` and `CreatedAt = DateTime.UtcNow`.
- All setters are `private set;` — state changes only through intention-revealing methods (`Issue()`, `RecordPayment()`, `AddStock()`, `Confirm()`…).
- Invariants enforced in ctors/methods via `ArgumentNullException` / `ArgumentException` / `InvalidOperationException` (newer entities throw **French** messages).
- `CreatedAt` / nullable `UpdatedAt` are pervasive; `UpdatedAt` is bumped on mutation (often via a private `Touch()`).
- Child collections are `private readonly List<>` exposed as `IReadOnlyCollection<>`.
- **Money is TND millimes** (`decimal`, 3 decimals, away-from-zero). `InvoiceCalculator` is the single arithmetic authority; billing entities round through it.

## Aggregate Roots (`AggregateRoot<...>`)

**Core / scheduling**
| Aggregate | File | Key responsibilities |
|-----------|------|----------------------|
| `Patient` | `Entities/Patient.cs` | Central aggregate. VOs: `Email`, `PhoneNumber`, optional `Address`, `InsuranceInfo`, `CnamInfo` (CNAM identity). Owns `Flags`, `Files`, `Appointments`, `MedicalHistoryEntries`, `FamilyHistoryEntries`. Recall/relance fields (`RecallSnoozedUntil`/`RecallReason`/`LastRecallContactedAt`) + emergency-contact fields. Belongs to a `Clinic`. |
| `Appointment` | `Entities/Appointment.cs` | State machine over `AppointmentStatus` (`Confirm/Start/Complete/MarkVisitCompleted/Cancel/MarkAsNoShow/Reschedule/Reactivate`). Optional `PatientId`; `DoctorId` is a **FK to `Doctor`** (nav property); optional `ProcedureTypeId` (+ snapshot duration/color), `RecurringAppointmentId`, `TreatmentPlanItemId`. Holds `GoogleCalendarEventId` for sync. |
| `Clinic` | `Entities/Clinic.cs` | Tenant root. `Name`, `City`, contact info, unique join `Code`, `LogoUrl`. **Billing settings** (`MatriculeFiscal`, `VatApplicable`/`VatRate`, `StampDutyEnabled`/`StampDutyAmount`) frozen onto each invoice at issue. **TTN e-invoicing** toggle + `TtnEnvironment` (Sandbox/Production consts). Per-clinic **Google Calendar** (`GoogleRefreshToken`/`GoogleCalendarId`). `WorkingHoursJson` (opaque). `RecallIntervalMonths`. Owns `Users`/`Patients`/`Appointments`. |
| `Doctor` | `Entities/Doctor.cs` | Practitioner profile in a clinic. `LinkToUser(userId)` ties it to a `User`. `FullName` computed. CNAM/official-doc fields: `CodeProfessionnelSante`, `OrdreNumberCnomdt` (CNOMDT), cachet blob (`CachetStorageKey`/`CachetContentType`). Optional per-dentist `WorkingHoursJson`. |
| `User` | `Entities/User.cs` | **`AggregateRoot<string>`** — Id is the Auth0 `sub` (Cloud) or `local\|{guid}` (Local). `Role` string ("doctor"/"secretary"/"admin") + `IsDoctor/IsSecretary/IsAdmin`. Belongs to a `Clinic`. **Local-auth:** nullable `PasswordHash`, `IsActive`, `MustChangePassword`, `LastLoginAt`, lockout state (`FailedLoginAttempts`/`LockoutEnd`; `MaxFailedLoginAttempts=5`, 15-min `LockoutDuration`). Factory `CreateLocalUser(...)`; `SetPassword`/`UpgradePasswordHash`, `RecordSuccessful/FailedLogin`, `Activate`/`Deactivate`. Cloud users leave `PasswordHash` null. |
| `ProcedureType` | `Entities/ProcedureType.cs` | Priced procedure catalog: `Name`, `DefaultDurationMinutes` (1–479), `DefaultCost`, `Color` (`ColorHex` VO), optional `ResultingCondition` (odontogram state a matching act produces), active flag. `IsUsedByFutureAppointments(...)` guards deletion. |
| `StockItem` | `Entities/StockItem.cs` | Inventory item. `AddStock/RemoveStock/SetCurrentStock`, `UpdateStockLevels`, `IsLowStock()/IsOutOfStock()`. |

**Billing / dental core / catalogs**
| Aggregate | File | Key responsibilities |
|-----------|------|----------------------|
| `Invoice` | `Entities/Invoice.cs` | Tunisian "note d'honoraires". Draft → `Issue()` (assigns `AAAA-NNNN`, freezes VAT/stamp, recomputes totals) → `RecordPayment()` → PartiallyPaid/Paid, or `Cancel()`. Owns `InvoiceLine`s + `Payment`s. `Outstanding`, `CanBeDeleted`, `CanSubmitToElFatoora`. Full TTN e-invoice state machine over `EInvoiceStatus` (`QueueForElFatoora`/`MarkEInvoiceSigned/Submitted/Validated/Rejected`/`RecordEInvoiceFailure`) with bounded retry (`EInvoiceAttemptCount`/`EInvoiceNextAttemptAt`) + stored artifact keys/QR payload. |
| `TreatmentPlan` | `Entities/TreatmentPlan.cs` | Devis / planned-care spine. Draft holds `TreatmentPlanItem`s + optional `Installment` schedule (must sum to total). `Accept()` numbers it (own `AAAA-NNNN` sequence, separate from invoices) → InProgress (first `MarkItemDone`/`RecordInstallmentPayment`) → `Complete()`, or `Cancel()`. Not a fiscal doc (no VAT/timbre/TTN). |
| `CnamNomenclatureEntry` | `Entities/CnamNomenclatureEntry.cs` | **Per-clinic** CNAM nomenclature act (code, `LettreCle`, `Coefficient`, category). Seeded provisionally ("à vérifier"); `Confirm()`/`Activate`/`Deactivate`. Backs BS1 reimbursement estimates. |
| `CnamLetterValue` | `Entities/CnamLetterValue.cs` | **Per-clinic** valeur de la lettre clé (VLC) — dinar value per `LettreCle`, used in the indicative CNAM reimbursement estimate. Seeded provisionally; `SetValue`/`Confirm`. |
| `DentalActCode` | `Entities/DentalActCode.cs` | **Per-clinic** Tunisian dental act catalog (chapitre DCH, e.g. `DCH020030`). Optional `Coefficient`/`DefaultFee`, `RequiresAccordPrealable`. Seeded provisionally. Linked from `TreatmentPlanItem`/`InvoiceLine` for the CNAM-reimbursable vs. out-of-pocket split. |
| `Medication` | `Entities/Medication.cs` | **Per-clinic** drug catalog (`BrandName`/`Form`/`Strength`) + one-or-more `MedicationActiveIngredient` (DCI/INN). Backs the ordonnance picker. Seeded provisionally; `Update`/`ReplaceActiveIngredients`/`Confirm`. |
| `Expense` | `Entities/Expense.cs` | Clinic caisse cash-out (loyer/salaires/…): date, category, positive `Amount`, `PaymentMethod`, description. Combined with collected payments for the daily caisse/net. |
| `LabWorkOrder` | `Entities/LabWorkOrder.cs` | Bon de laboratoire/prothèse: work sent to an external prothésiste, tracked over `LabOrderStatus` (Sent→…→Fitted). Clinic- + patient-scoped; optional `ToothNumber`/`Cost`. |
| `WaitingListEntry` | `Entities/WaitingListEntry.cs` | Liste d'attente entry: patient waiting for a slot, `WaitingListPriority`, optional `PreferredDoctorId`/`DesiredTimeframe`. `Promote(appointmentId)` / `Cancel()`; `WaitingListStatus`. |
| `StaffNotification` | `Entities/StaffNotification.cs` | In-app staff feed record (bell/panel). **One shared row per event**; per-user read state lives in `NotificationRead` (no write-time fan-out). `Category`, French `Title`/`Message`, `EffectiveFeedTime` (UTC ordering + due-ness + late-joiner anchor), `ActorUserId` (excluded from own feed), optional `TargetUserId` (doctor-targeted post-visit review), `TargetKind` + `AppointmentId`/`StockItemId` deep-links. `MoveReminder(...)` / `MovePostVisitReview(...)` repoint on reschedule. Separate from the outbound `Notification`. |

## Non-root & child Entities (`Entity<Guid>`, unless noted)

| Entity | File | Notes |
|--------|------|-------|
| `PatientFlag` | `Entities/PatientFlag.cs` | `PatientFlagType` flag on a patient; activate/deactivate. |
| `PatientMedicalHistory` | `Entities/PatientMedicalHistory.cs` | Patient's own medical-history entry. |
| `PatientFamilyHistory` | `Entities/PatientFamilyHistory.cs` | Family history (relationship + condition). |
| `PatientFile` | `Entities/PatientFile.cs` | File metadata: `StorageKey`, `ContentType`, `FileSize`, `FileType`, optional `FolderId`. |
| `PatientFolder` | `Entities/PatientFolder.cs` | Nestable folder (`ParentFolderId`); `AddSubFolder`/`AddFile` enforce the same-patient invariant. |
| `DentalRecord` | `Entities/DentalRecord.cs` | A session: owns `Acts` (`DentalRecordAct`); `SetActs(...)` **derives** the `ProcedureType` summary, `Cost`, and flat `Teeth` list. `Notes`/`ImportantNotes` string lists, `AmountPaid`, `IsAdultTeeth`. |
| `DentalRecordAct` | `Entities/DentalRecordAct.cs` | One act in a session: snapshotted procedure (from `ProcedureType` or free-text) + `Cost`, applied to FDI teeth, optional `ResultingCondition` (feeds the odontogram) + `Surfaces` (MODVL). Carries the **pricing provenance** `UnitCost`/`IsPerTooth` (is `Cost` unit × teeth, or a flat fee?) so the editor reopens an act with its intent intact and the invoice bridge can bill quantity × unit price; `Cost` is authoritative and never recomputed from them. `IsPerTooth` is forced false with no teeth. Built from a `DentalRecordActInput` parameter object (same file) rather than a positional tuple. |
| `DentalRecordTooth` | `Entities/DentalRecordTooth.cs` | A tooth on a record; validates FDI (its own `IsValidToothNumber`/`IsAdultTooth`, mirrored by `FdiTooth`). |
| `ToothState` | `Entities/ToothState.cs` | Persistent odontogram entry (many-per-tooth), child-of-patient (**no `ClinicId`**). `ToothCondition` + `ToothStateSource` (Treatment from a record, or Diagnosis charted directly), optional `Surfaces`/`Note`, `TreatmentDate`. |
| `MedicalDocument` | `Entities/MedicalDocument.cs` | Generated document (prescription / liaison / honoraires / certificat). `ContentJson` + **snapshots** of patient/clinic/doctor. `IsDraft`, optional `FileId`, optional `AppointmentId` (creating the doc marks that appointment Completed). |
| `RecurringAppointment` | `Entities/RecurringAppointment.cs` | Active series template (clinical-workflow-depth): `RecurrencePattern`/`Interval` + end condition (`EndDate` and/or `OccurrenceCount`), optional `DoctorId`/`ProcedureTypeId`; expands into `Appointment` rows via `Appointment.RecurringAppointmentId`. Clinic-scoped. |
| `InvoiceLine` | `Entities/InvoiceLine.cs` | Billable act line (child of `Invoice`): `Designation`/`Quantity`/`UnitPriceHt`/`LineTotalHt`; optional soft links `DentalRecordId`/`DentalActCodeId` (+ `CodeActe` snapshot). Never a diagnosis (medical secrecy). |
| `Payment` | `Entities/Payment.cs` | Immutable payment against an `Invoice`: `Amount`, `PaymentMethod`, `PaidOn`. |
| `TreatmentPlanItem` | `Entities/TreatmentPlanItem.cs` | Planned act (child of `TreatmentPlan`): `DesignationFr` (+ optional `DentalActCode` link/`CodeActe`), `PlannedCost`, FDI `ToothNumbers`, `TreatmentPlanItemStatus`; `MarkDone(...)` links the recording `DentalRecord`. |
| `Installment` | `Entities/Installment.cs` | Échéance (child of `TreatmentPlan`): `DueDate`/`Amount`, cumulative `AmountPaid` (`RecordPayment` refuses overpayment; keeps only latest method/date). |
| `MedicationActiveIngredient` | `Entities/MedicationActiveIngredient.cs` | One DCI/INN molecule of a `Medication` (normalized, case-insensitively deduped). `NormalizeDci` static helper. |
| `ClinicReminderSettings` | `Entities/ClinicReminderSettings.cs` | **`Id` IS the clinic id** (1:1, shared PK). Per-clinic SMS/WhatsApp channel toggles (`bool?` = inherit per-install), sender identity, endpoint URLs, lead-time CSV, message template, **write-only encrypted secrets**, and WhatsApp Embedded-Signup metadata (`WhatsAppConnectionStatus`). Static `Parse/FormatLeadTimeHours`. |
| `Notification` | `Entities/Notification.cs` | Outbound **SMS/WhatsApp** reminder outbox (Email still dormant). Nullable `ClinicId` (dispatcher resolves that clinic's credentials). Status lifecycle `MarkAsSent`/`MarkAsFailed` (terminal) / `RecordFailedAttempt` (transient, retries until cap) / `Retry`. Not the in-app feed — that's `StaffNotification`. |
| `NotificationRead` | `Entities/NotificationRead.cs` | **Plain class** (composite key `NotificationId`+`UserId`, does not extend `Entity`). Presence = "this user read it". No `ClinicId` — scoped by `UserId` only (a user belongs to one clinic), so it is never in the clinic query filter. |

## Value Objects (`ValueObjects/`)

| VO | File | Validation / behavior |
|----|------|-----------------------|
| `Email` | `Email.cs` | Validates via `MailAddress`, stores lowercased. |
| `PhoneNumber` | `PhoneNumber.cs` | Non-empty, trimmed. Static `ToE164`/`IsDeliverable` normalize Tunisian numbers to `+216` E.164 — the single source of truth shared by entry validation (Application) and reminder dispatch (Infrastructure). |
| `Address` | `Address.cs` | Street/City/State/Zip required, optional Country. |
| `InsuranceInfo` | `InsuranceInfo.cs` | Provider + policy number required. |
| `CnamInfo` | `CnamInfo.cs` | Optional CNAM identity owned by `Patient` (identifiant/régime/assuré/lien…). Every field optional; `IsEmpty` lets the handler clear it. Pre-fills the BS1 bulletin. |
| `ColorHex` | `ColorHex.cs` | `#RRGGBB`, **must be in a curated palette that mirrors the frontend** `COLOR_PALETTE`. `IsValid`/`FromString`/`GetAvailableColors`. Used by `ProcedureType`. |

## Enums (`Enums/`)

| Enum | Values |
|------|--------|
| `AppointmentStatus` | Scheduled, Confirmed, InProgress, Completed, Cancelled, NoShow |
| `PatientFlagType` | HighPriority, SpecialCondition, Alert, Critical, Allergy |
| `FileType` | LabResult, Scan, Prescription, MedicalRecord, Insurance, Other |
| `InvoiceStatus` | Draft, Issued, PartiallyPaid, Paid, Cancelled |
| `PaymentMethod` | Cash, Cheque, Card, Transfer |
| `EInvoiceStatus` | NotSubmitted, Queued, Signed, Submitted, Validating, Valid, Rejected, Failed (TTN state, independent of `InvoiceStatus`) |
| `TreatmentPlanStatus` | Draft, Accepted, InProgress, Completed, Cancelled |
| `TreatmentPlanItemStatus` | Planned, Done |
| `ToothCondition` | Sain (implicit default), Carie, Obturation, Couronne, TraitementDeCanal, Bridge, Implant, ExtraitAbsent, ATraiter |
| `ToothStateSource` | Treatment (from a dental record), Diagnosis (charted on the odontogram) |
| `LabOrderStatus` | Sent, InProgress, Received, Fitted |
| `WaitingListPriority` | Low, Normal, High |
| `WaitingListStatus` | Waiting, Promoted, Cancelled |
| `RecurrenceFrequency` | Daily, Weekly, Monthly (stored by name on `RecurringAppointment.RecurrencePattern`) |
| `RecurringSeriesScope` | Occurrence, Following, WholeSeries (edit/cancel scope) |
| `WhatsAppConnectionStatus` | NotConnected, Connected, Error (on `ClinicReminderSettings`) |
| `NotificationType` | Email, SMS, Both, WhatsApp (outbound `Notification`; SMS/WhatsApp live, Email/Both dormant) |
| `NotificationStatus` | Pending, Sent, Failed (outbound `Notification`) |
| `NotificationCategory` | AppointmentCreated, AppointmentCancelled, AppointmentRescheduled, Reminder, LowStock, PostVisitReview (in-app `StaffNotification`) |
| `NotificationTargetKind` | Appointment, StockItem (drives the panel deep-link) |

## Domain services (`Services/`)

- **`InvoiceCalculator`** (`Services/InvoiceCalculator.cs`) — pure, testable Tunisian money arithmetic (no persistence). `RoundMoney` (millime, away-from-zero), `LineTotal`, and `Compute(totalHt, vatApplicable, vatRate, stampDutyAmount) → InvoiceTotals(HT, VAT, TTC)`. The single rounding authority reused by `Invoice`, `TreatmentPlan`, `Installment`, `DentalRecord`, and their lines.

## Repository interfaces (`Repositories/`)

Async, `CancellationToken`-aware contracts implemented in Infrastructure. Persistence is committed via the Application `IUnitOfWork` (most repos only stage changes; a few `User`/`Doctor` ones expose sync `Update`/`Remove`).

| Interface | Notable methods |
|-----------|-----------------|
| `IPatientRepository` | `GetByIdWithAppointmentsAsync`, `GetByClinicIdAsync`, `Count*ByClinicIdAsync`, `GetFlaggedPatientsAsync`, `AddMedical/FamilyHistoryEntryAsync` |
| `IAppointmentRepository` | `GetByClinicIdAsync(date range, doctorId)`, `CountByClinicIdAsync(status filters)`, `GetUpcomingAppointmentsAsync`, `GetAppointmentsForDateAsync`, `GetByProcedureTypeIdAsync` |
| `IClinicRepository` | `GetByCodeAsync`, `GetByNameAsync`, `CodeExistsAsync` |
| `IUserRepository` | `GetByAuth0SubAsync`, `GetByEmailAsync` (Local login), `AnyUserExistsAsync` (closes first-run setup), sync `Update`/`Remove` |
| `IDoctorRepository` | `GetByClinicIdAsync`, `GetByUserIdAsync` |
| `IProcedureTypeRepository` | `GetActiveAsync`, `GetByNameAsync`, `ExistsByNameAsync` |
| `IDentalRecordRepository` | `GetByPatientIdAsync` |
| `IToothStateRepository` | odontogram: `GetByPatientIdAsync`, `GetByDentalRecordIdAsync` |
| `IPatientFileRepository` / `IPatientFolderRepository` | by patient / folder / root / name |
| `IMedicalDocumentRepository` | by patient / document type / clinic |
| `IStockItemRepository` | `GetByClinicIdAsync(lowStockOnly)`, `GetLowStockItemsAsync`, `GetOutOfStockItemsAsync` |
| `IInvoiceRepository` | `GetFilteredAsync`, `GetMaxSequenceForYearAsync` (gapless per-clinic-per-year), `GetCollectedBetweenAsync`, `GetOutstandingByPatientAsync`, `GetByPaymentIdAsync`, `GetDueForElFatooraDispatchAsync` (outbox) |
| `ITreatmentPlanRepository` | `GetFilteredAsync`, `GetMaxSequenceForYearAsync` (separate sequence), `GetInstallmentCollectedBetweenAsync`, `GetInstallmentOutstandingByPatientAsync` (with oldest-overdue) |
| `ICnamCatalogRepository` | nomenclature entries + VLC letter values (`CodeActeExistsAsync`, `GetLetterValueByCleAsync`) |
| `IDentalActCodeRepository` | DCH catalog (`CodeActeExistsAsync`, `AnyProvisionalAsync`, `GetProvisionalAsync`) |
| `IMedicationCatalogRepository` | drug catalog (`BrandExistsAsync`) |
| `IExpenseRepository` | `GetByClinicIdAsync(date range)`, `GetTotalBetweenAsync` |
| `ILabWorkOrderRepository` | by clinic / patient |
| `IWaitingListRepository` | `GetByClinicIdAsync(activeOnly)` (priority then oldest-first) |
| `IRecurringAppointmentRepository` | `GetByClinicIdAsync(activeOnly)` |
| `IClinicReminderSettingsRepository` | `GetByClinicIdAsync` (1:1, keyed by clinic id) |
| `INotificationRepository` | `GetPendingNotificationsAsync`, `GetByAppointmentIdAsync`, `GetRecentByClinicIdAsync` (delivery-status surface) |
| `IStaffNotificationRepository` | In-app feed. `GetRecentForUserAsync` (newest-first, actor-excluded, 50-cap), `CountUnreadAsync`/`GetUnread(Ids)ForUserAsync` (due + not-actor + at/after viewer join + no read marker), `GetReadNotificationIdsAsync`, `ReadMarkerExistsAsync`/`AddReadMarkerAsync`, `GetReminderByAppointmentAsync`, `GetPostVisitReviewByAppointmentAsync`, `GetPendingReviewsForUserAsync`. Due-ness/actor-exclusion/late-joiner-baseline/cap live in the query impls. |

## Domain Events — removed

The `Events/` folder, `IDomainEvent`, and `AggregateRoot`'s event list were removed (`french-localization-and-cleanup`): a dead "parallel-universe" pipeline (`SaveChangesAsync` never drained events; zero handlers). Side effects (in-app `StaffNotification`s, SMS/WhatsApp reminders) are produced **inline, post-commit** from the command handlers via `INotificationGenerator`/`IReminderScheduler`.

## Gotchas
- `User` is keyed by **string** — the Auth0 `sub` (Cloud) or `local\|{guid}` (Local) — unlike all other `Guid`-keyed types.
- `Appointment.PatientId` and `DoctorId` are optional: an appointment can be a "blocked"/occupied slot with no patient. `DoctorId` is a real FK to `Doctor` (not free-text). In-app notifications are only generated for patient-bearing events and never for the actor.
- **Denormalized snapshots are intentional, not FKs**: `Appointment` snapshots procedure duration/color; `DentalRecord` derives its summary/cost/teeth from its acts; `InvoiceLine`/`TreatmentPlanItem` snapshot `CodeActe`; `MedicalDocument` snapshots patient/clinic/doctor.
- **CNAM/DCH/medication catalogs are per-clinic** (each has `ClinicId` and a `HasQueryFilter`; every clinic is seeded the same default set, then edits stay private). ⚠️ Their in-source class/interface docstrings still say "global reference data (no ClinicId)" — that wording is stale; trust the `ClinicId` property + query filter.
- Money everywhere is TND **millimes** (3 decimals). Round only through `InvoiceCalculator`.
- `ColorHex`'s curated palette must stay in sync with the frontend `COLOR_PALETTE`.
- `ClinicReminderSettings` and `NotificationRead` are the two non-standard keys: the former's `Id` **is** the clinic id (1:1 shared PK); the latter is a plain composite-key class, not an `Entity`.
