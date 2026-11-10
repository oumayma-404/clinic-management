# Blueprint: Pointage et congés des secrétaires

**Status:** DECIDED (2026-11-05). Not built. Split into three small specs, built in order:
[`conges-secretaires`](../conges-secretaires/spec.md) → [`pointage-borne`](../pointage-borne/spec.md) →
[`pointage-obligatoire`](../pointage-obligatoire/spec.md).
**Visuals:** https://claude.ai/artifact/LJQDQvKRaLqCLmXvRJrrsF
**Owner's choices:** a **borne du cabinet** proves where she is (not a QR code, an app button or an IP check) ·
**the admin decides** congés (not every doctor) · **arrival enforced**, departure caught by three nets.

> ⚠️ **Build from `main`, not from `feature/security-remediation`.** That branch is 184 commits behind and lacks
> three things this design depends on: `DoctorLeave` (doctor congés), `StaffNotification.TargetRole`, and the
> PC de secours (`ClinicRelay`, `ClinicRelayScope`). The first exploration read that branch and missed all three.

## Summary

- One new area, `Features/Team`, with one realtime key: `team`.
- Three new aggregates:
  - `AttendanceTerminal` is the borne.
  - `AttendanceEntry` is one arrival/departure pair.
  - `StaffLeaveRequest` is a congé request.
- New fields go on `User` (the schéma and her settings) and on `Clinic` (two opt-in switches and the lock).
- The **borne** proves *where* she is: the device holds a long-lived secret, and only its SHA-256 is stored. This
  is the same shape as the PC de secours's relay secret. The **schéma** proves *who* she is, hashed like a
  password.
- Congés are decided by the admin. The request reaches admins through `TargetRole = admin`. The decision reaches
  the requester in the app (pushed to her phone by the existing decorator) and by e-mail through
  `ITransactionalEmailSender`.
- The lock that enforces arrival is **server-side**, a write gate shaped like `SubscriptionGateMiddleware`. The
  screen-side gate is only its presentation.

## Domain — `api/ClinicManagement.Domain`

### `Entities/AttendanceTerminal.cs` (`AggregateRoot<Guid>`)

- **Fields:** `ClinicId`, `Name` (« Accueil · PC réception », ≤ 60), `SecretHash` (SHA-256 hex of 32 random
  bytes; not PBKDF2, because the secret is high-entropy), `CreatedByUserId`, `CreatedAt`, `LastUsedAt?`,
  `RevokedAt?`.
- **Methods:** `Register(id, clinicId, name, secretHash, byUserId, nowUtc)`, `Revoke(nowUtc)`, `Touch(nowUtc)`,
  `IsActive`.

### `Entities/AttendanceEntry.cs` (`AggregateRoot<Guid>`)

- **Who and where:** `ClinicId`, `UserId`, `LocalDay` (`DateOnly`, from `ClinicClock` at arrival), `TerminalId?`.
  `TerminalId` is **not a foreign key**; see Pitfalls. `TerminalName` is a snapshot of the borne's name.
- **What the borne recorded (immutable):** `PunchedArrivalUtc?`, `PunchedDepartureUtc?`.
- **The hours that count:** `ArrivalUtc`, `DepartureUtc?`.
- **How the departure was obtained:** `DepartureSource` (`Borne = 0`, `DeclaredBySelf = 1`, `CorrectedByAdmin = 2`).
  It is append-only and stored with `HasConversion<int>`.
- **Correction:** `CorrectedByUserId?`, `CorrectionReason?`, `CorrectedAt?`.
- **Methods:**
  - `Arrive(...)`.
  - `Depart(nowUtc, terminal)`, which refuses when `LocalDay` is not today.
  - `DeclareDeparture(localTime, nowUtc)`, which refuses a time before arrival or after the end of that day.
  - `Correct(arrival, departure?, reason, byUserId, nowUtc)`. The reason is required and the departure must be
    later than the arrival.
  - Static `RecordByAdmin(userId, day, arrival, departure?, reason, byUserId)`. It sets no punched values and is
    the « borne en panne » path.

### `Entities/StaffLeaveRequest.cs` (`AggregateRoot<Guid>`)

The name keeps it apart from `DoctorLeave`.

- **The request:** `ClinicId`, `UserId`, `Type`, `StartDate` / `EndDate` (`DateOnly`, inclusive), `HalfDay`
  (only when start equals end), `Reason`.
- **Days:** `RequestedDays` (decimal, counted at submission), `CountedDays?` (decimal, set on approval and
  editable then).
- **The decision:** `Status`, `DecidedByUserId?`, `DecidedAt?`, `RejectionReason?`, `CancelledAt?`,
  `CancelledByUserId?`.
- **Other:** `EnteredByAdmin`, `CreatedAt`, `UpdatedAt`.
- **Transitions** (a table, like `LabWorkOrder.AllowedTransitions`):
  - `Pending → Approved | Rejected | Cancelled`.
  - `Approved → Cancelled`, but only before `StartDate`.
- **Methods:** `Submit`, `Approve(byUserId, countedDays, nowUtc)`, `Reject(byUserId, reason, nowUtc)` (reason
  required), `Cancel(byUserId, today)`, static `RecordByAdmin(...)` (created as `Approved`).

### Enums (append-only, `HasConversion<int>`)

- `StaffLeaveType { PaidLeave = 0, Sick = 1, Unpaid = 2, Other = 3 }`
- `StaffLeaveStatus { Pending = 0, Approved = 1, Rejected = 2, Cancelled = 3 }`
- `AttendanceDepartureSource` (above).

### `User` (fields and methods)

- `AttendanceSchemaHash?`
- `AttendanceTrackedOverride` (`bool?`), with `IsAttendanceTracked => AttendanceTrackedOverride ?? Role == RoleSecretary`.
  This is one derived predicate, so there is no write site to forget when a role changes. It avoids the
  fixes-don't-propagate shape.
- `AnnualLeaveAllowanceDays?` (decimal).
- `SchemaFailedAttempts`, `SchemaLockedUntilUtc?`.
- `SetAttendanceSchema(hash)` · `ResetAttendanceSchema()` (also clears the lock) · `RegisterSchemaFailure(nowUtc)`
  (5 failures lock it for 15 min) · `ClearSchemaFailures()`.

### `Clinic` (fields and methods)

- `AttendanceEnabled`, `AttendanceLockEnabled` (default **true**, but only meaningful when attendance is on),
  `LeaveRequestsEnabled`. All three are off by default.
- Methods modelled on `HideMoney()` / `ShowMoney()`.

### Repositories

`IAttendanceTerminalRepository`, `IAttendanceEntryRepository`, `IStaffLeaveRequestRepository`.

## Application — `Features/Team`

### Commands

| Command | Policy | Notes |
|---|---|---|
| `SetTeamModulesCommand(bool? Attendance, bool? Lock, bool? Leave, uint Version)` | AdminOnly | Tri-state: an omitted value means unchanged. |
| `RegisterAttendanceTerminalCommand(Name)` | AdminOnly + **step-up** (like `relay-pairing`) | Returns the raw secret **once**; the controller puts it in the cookie. |
| `RevokeAttendanceTerminalCommand(Id, Version)` | AdminOnly | |
| `SetMyAttendanceSchemaCommand(Pattern)` | AnyClinicRole, own account | 4–9 distinct dots out of 0–8, no repeats; drawn twice on the client. |
| `ResetAttendanceSchemaCommand(UserId, Version)` | AdminOnly | |
| `SetAttendanceProfileCommand(UserId, bool? Tracked, decimal? Allowance, Version)` | AdminOnly | Tri-state. |
| `PunchCommand(UserId, Pattern, TimeOnly? DeclaredPreviousDeparture)` | **Borne** policy | Arrival or departure is decided by `AttendancePunchRules`. Arrival with an open entry from a previous day is refused with code `previous_departure_missing` unless the declared time is supplied, which declares it and then opens the new line. |
| `CorrectAttendanceEntryCommand(Id, Arrival, Departure?, Reason, Version)` | AdminOnly | |
| `RecordAttendanceEntryCommand(UserId, Day, Arrival, Departure?, Reason)` | AdminOnly | The « borne en panne » path; it also opens the lock. |
| `SubmitStaffLeaveRequestCommand(Type, Start, End, HalfDay, Reason)` | AnyClinicRole, own account | Refuses when the module is off or the request overlaps another non-cancelled request; snapshots `RequestedDays`. |
| `DecideStaffLeaveRequestCommand(Id, Approve, CountedDays?, RejectionReason?, Version)` | AdminOnly | |
| `CancelStaffLeaveRequestCommand(Id, Version)` | The requester, or AdminOnly | Notifies admins when an accepted request is withdrawn. |
| `RecordStaffAbsenceCommand(UserId, Type, Start, End, HalfDay, Reason)` | AdminOnly | Stored as `Approved` + `EnteredByAdmin`. |

### Queries

| Query | Policy | Notes |
|---|---|---|
| `GetTerminalStatusQuery` | Borne policy | Is this device a borne, its name, and the tiles (tracked active users: name, initials, today's state). Nothing else about anyone. |
| `GetAttendanceSheetQuery(MonthKey, UserId?)` | AdminOnly | Rows per local day: entries, leave, closed days, totals. `paging: null`, since a month is bounded. |
| `GetTeamTodayQuery` | AdminOnly | Also feeds the dashboard block. |
| `GetStaffFileQuery(UserId, Year)` | AdminOnly | KPIs + leave history. |
| `GetMyAttendanceQuery(MonthKey)` · `GetMyStaffLeaveQuery(Year)` · `GetMyClockStatusQuery` | Own account | The last one feeds the arrival gate and the logout prompt. |
| `ListStaffLeaveRequestsQuery(Status?, paging)` | AdminOnly | Ordered by `CreatedAt DESC`, then `Id`. |

### Shared rules (one owner each)

- `AttendancePunchRules.NextAction(openEntryToday, openEntryEarlier)`: arrival, departure, or a declaration
  needed first.
- `StaffLeaveDayCounter.Count(start, end, halfDay, workingHours)`:
  - It counts open days from `Clinic.WorkingHoursJson` (`WorkingHoursSerializer`).
  - With no hours set it falls back to Monday–Saturday.
  - It reuses the period wording of `DoctorLeaveRules.Period`; extract that into a shared formatter rather than
    copying it.
- `StaffLeaveBalance.For(allowance, approvedPaidDaysThisYear)`: only `PaidLeave` uses up the allowance.
- `AttendanceLockRules.IsLocked(user, clinic, nowUtc, entriesToday, approvedLeaveToday, relayHolding)`:
  - It is the **one** predicate behind both the middleware and `GetMyClockStatusQuery`.
  - The lock is off when the module or lock is off, the user is untracked, it is a closed day or outside opening
    hours, she is on leave today, she has already arrived today, or the cabinet is running on the PC de secours.
- `TeamRefusals` (sentence and code together): `team_module_off`, `terminal_unknown`, `schema_wrong`,
  `schema_locked`, `schema_missing`, `previous_departure_missing`, `clock_in_required`, `leave_overlap`,
  `leave_not_pending`, `leave_already_started`, `rejection_reason_required`.
- `TeamLabels`: the French labels for the enums, server-side (the `AuditLabels` pattern).

### Notifications

- **Categories:** append `StaffLeaveRequested`, `StaffLeaveApproved`, `StaffLeaveRejected`,
  `StaffLeaveCancelled` and `AttendanceDepartureMissing` to `NotificationCategory`.
- **Target kind:** append `StaffLeaveRequest` to `NotificationTargetKind`.
- **Requests and cancellations:** one row with `TargetRole = admin`. Never one row per admin; the main-branch
  `StaffNotification.TargetRole` doc forbids that.
- **Decisions and the departure reminder:** `TargetUserId = requester`. The push to her phone comes for free
  through `PushNotificationGeneratorDecorator`.

### E-mail

- Built with a `StaffLeaveDecisionEmail` composer on `EmailContent` / `EmailLayout`, in French. The subject names
  the cabinet.
- Sent through `ITransactionalEmailSender`, **after commit**, best-effort. Its catch only logs, so it needs no
  `ConflictException` filter.
- Skipped when the sender is not configured or she has no address. Widen the interface's doc comment on purpose,
  because it currently says « belongs to no clinic ».

### Job

`AttendanceDepartureReminderJob` runs on Hangfire every 15 minutes.

- **Selection:** clinics with attendance on, today's open entries, and now past today's closing time
  (`WorkingHours.To`) + 15 min.
- **Required by guard tests:** it starts with `RunAs` + `UseSystemWide`, uses one try/catch per clinic, and takes
  « today » as a parameter.

## Infrastructure

- **Configurations:** three `*Configuration.cs` files.
  - Enums use `HasConversion<int>`; `DateOnly` columns use `HasColumnType("date")`.
  - `AttendanceEntry` indexes: `(ClinicId, UserId, LocalDay)` and `(ClinicId, LocalDay)`.
  - `StaffLeaveRequest` indexes: `(ClinicId, Status)` and `(ClinicId, UserId, StartDate)`.
  - `AttendanceTerminal`: unique `SecretHash`.
- **Tenant filters:** add three `HasQueryFilter(... IsSystemWide || ClinicId == ScopedClinicId)` lines to
  `ApplicationDbContext` (checked by `TenantScopeFilterTests`).
- **Repositories:** add them, plus `Infrastructure/Extensions.cs` `AddScoped`.
- **Migration** `AddTeamAttendanceAndLeave`: delete the scaffolded `xmin` columns, and **backfill nothing**. The
  tracked flag is derived.
- **Archive and PC de secours:** the scope is derived automatically (`ClinicArchiveScope` and `ClinicRelayScope`).
  Decide the two secret columns explicitly:
  - **Archive:** redact `AttendanceTerminal.SecretHash` and `User.AttendanceSchemaHash`.
  - **PC de secours:** copy both, the way `User`'s password hash is copied, or the schéma check fails during a cut.

## API

- **`TeamController`** (`/api/team/...`) carries an explicit policy on every action
  (`ControllerAuthorizationCoverageTests`, `AdminSurfaceCoverageTests`).
- **`AttendanceTerminalController`** (`/api/pointage/...`) uses a new `AttendanceTerminal` policy.
  - Its handler reads the `apexa_borne` cookie, hashes it, loads the active terminal system-wide, and then calls
    **`ITenantScope.UseClinic(terminal.ClinicId)`**.
  - The cookie is `HttpOnly`, `Secure` on hosted, `SameSite=Strict`, `Path=/api/pointage`, with
    `Max-Age = 400 days`.
  - The cookie is **re-issued on every successful punch**, because Chrome caps cookie lifetime at 400 days.
- **Punch audit:** the punch names its actor with `IAuditActorProvider.AuthenticatedAs(userId, email)` once the
  schéma checks out. That is the sign-in method, not `RunAs`, which would journal « Tâche automatique ».
- **Punch rate limit:** per terminal, using the per-account limiter pattern of `/api/auth`.
- **`AttendanceLockMiddleware`**, placed after authentication:
  - On non-GET `/api` requests it refuses with `423 { error, code: "clock_in_required" }` whenever
    `AttendanceLockRules.IsLocked` holds.
  - Endpoints that stay open carry `[AllowsBeforeClockIn("reason")]`: pointage, congés, own schéma, sign-out,
    notifications read.
  - It needs a coverage test that compares the applied set with the reviewed set in both directions, mirroring
    `SubscriptionExemptionCoverageTests`.
- **Subscription:** there is **no** `[AllowsWithoutSubscription]` on the punch. A lapsed Cloud cabinet's borne
  reads « Abonnement expiré », like every other save (owner-approved in the visuals).

## Web

- **API and realtime:** `web/lib/api/team.ts` and `web/lib/hooks/use-team.ts`. Declare `team` in
  `web/lib/realtime/clinic-hub.ts`, or `RealtimeResourceResolverTests` fails.
- **`/pointage`** (the borne, kiosk layout, no sign-in required):
  - It must bypass `session-lock-gate` and the idle limit.
  - Components: `components/team/borne-grid.tsx` and `schema-pad.tsx`. The pad uses pointer events, dots of at
    least 44 px on a coarse pointer, and keys 1–9 as a **keyboard fallback**.
  - The next-morning declaration dialog appears before the pad.
- **Entry points on a borne:** a « Pointage » button on `/login` and in the top bar, shown when
  `GET /api/pointage/status` says this device is a borne.
- **Arrival gate:** a full-screen layer that reads `GetMyClockStatusQuery` and closes itself on the `team`
  realtime event. On a borne it embeds the pad.
- **Logout:** the user menu's « Se déconnecter » asks « Pointer votre départ ? » when she is clocked in.
- **`/users`:**
  - Becomes tabs: `Comptes` (today's `user-management.tsx`), `Présences` and `Congés`, with the pending count on
    the tab and on the rail entry.
  - The month sheet table must have its card form below `lg` (`card-fallback`, `table-hinge-fits-its-box`).
  - New route `/users/[id]` for the staff file, plus the correction dialog and the refusal dialog. Both use
    `DialogBody` and become a sheet on a phone, in `dvh`.
- **`/conges`:** the requester's page with the tabs « Demandes » and « Mes présences ». Its rail entry shows only
  when `LeaveRequestsEnabled`, through `isNavItemVisible`.
- **Schéma setup:** in **« Mon profil »**, not `/securite`, because `/securite` is in `SECRETARY_HIDDEN_HREFS`.
- **`clinic-settings.tsx`:** add a `TeamModulesCard` (admin only) with the switches, the lock sub-switch, the list
  of bornes and « Faire de cet appareil une borne ».
- **Dashboard:** an `equipe` block in `DASHBOARD_BLOCKS` (section `journee`), filled by a reader in
  `GetDashboardQuery` only when the module is on and the caller is an admin.
- **`ClinicDto`:** gains three flags, and it is mapped **by hand at 7 sites** (Create ×2, Join ×2,
  RegenerateCode, Update, GetUserStatus). Update every one, or move the mapping into a single extension first.

## Pitfalls

- **Wrong base branch.** See the banner at the top.
- **Borne requests have no user.** Without `UseClinic` they read zero rows silently (the « Unset tenant scope »
  trap).
- **Arrival gate and the PC de secours:** during an internet cut the browser borne's cookie is for the cloud's
  origin and is not sent to the PC. The lock therefore **lifts while the PC de secours holds the saves**, and the
  missing punches are declared the next morning, the same net as a forgotten departure. A borne that works through
  a cut (an assertion handed to the PC, like `relay-session`) is a separate piece of work. Do not fake it.
- **The reminder job must not write `AttendanceEntry`.** Every write ages `xmin` and would 409 an admin's open
  correction dialog (the `AppointmentProgressJob` trap). Dedupe on the existing notification instead.
- **`TerminalId` is not a foreign key.** A borne can be revoked and the archive may treat it differently from the
  entries, so the entry keeps `TerminalName` as a snapshot.
- **Never invent an hour.** A missing departure stays null until she declares it or the admin corrects it. Totals
  count only closed lines and say « + aujourd'hui » for the open one.
- **Every « today » goes through `ClinicClock`.** The client uses `todayLocalIso()` and `TimeField` (24 h), never
  `<input type="time">`.
- **Forms that send a `version`** (correction, decision, cancel) go through `useConflict`.
- **Never branch on French text.** Branch on `TeamRefusals` codes only.

## Test strategy

- **Domain (unit):**
  - `AttendanceEntry`: arrive and depart; departure refused on another day; declaration bounds; correction needs a
    reason; punched values never change.
  - `StaffLeaveRequest`: every legal and illegal transition from the table; refusal needs a reason; an accepted
    request can be cancelled only before it starts.
  - `StaffLeaveStatusCoverageTests`: each status is classified.
- **Rules (unit):**
  - `StaffLeaveDayCounter`: closed days, half-day, across a month boundary, no hours set.
  - `AttendancePunchRules`: lunch break, open entry from yesterday, today already closed.
  - `AttendanceLockRules`: every lifting condition, one test each, plus the PC-de-secours case.
- **Handlers (unit):**
  - `PunchCommand`: unknown or revoked borne; module off; untracked or inactive user; 5 wrong schémas lock it;
    an admin reset clears the lock; `UseClinic` and `AuthenticatedAs` are both called.
  - Submit: overlap refused; module off; days snapshot taken.
  - Decide:
    - Admin only.
    - The notification targets the requester; the request notification uses `TargetRole = admin`.
    - E-mail is sent when configured, skipped when not, and a send failure does not fail the decision.
    - A stale version answers 409.
- **Guard tests (they will fail by themselves; satisfy them, do not skip them):** `RealtimeResourceResolverTests`,
  `TenantScopeFilterTests`, `ControllerAuthorizationCoverageTests`, `AdminSurfaceCoverageTests`,
  `ExceptionLeakCoverageTests`, `ClinicArchiveScopeTests` (secret columns), `ClinicPurgePlanTests`,
  `SystemWideCallerCoverageTests` (the job), plus the new lock-exemption coverage test.
- **Wire (`e2e/`):** the month sheet around midnight (UTC+1), and an approved congé removing that day from
  « Aucun pointage ».
- **Browser (`/test-in-browser`, required by `verification.md` § 4b because these rules live in `.tsx`):**
  - The borne punch flow, including the next-morning declaration.
  - The arrival gate opening on the realtime event.
  - The logout prompt.
  - The refusal dialog refusing an empty motif.
  - The correction dialog.
  - The sheet's card form at 390 px.
- **Frontend gate:** `check:responsive` + `tsc --noEmit` + `npm run build`, then an eye pass at
  320/390/820/1180/1440 px.
