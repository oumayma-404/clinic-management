# Salle d'attente — implementation blueprint

> Chosen 2026-11-25: **option 1, « times on the RDV »**. Mock-up (the agreed look and flow):
> https://claude.ai/artifact/Ax7Wf6kyq1tmZ4pfKHcMTn — reception kanban (drag and drop) + doctor's phone, live.
> Written against `origin/main` 031f5314. Not started.

## Summary

The appointment itself records three facts it never had: **arrived**, **called in**, **left the chair**. A walk-in
becomes a real appointment at the moment he arrives, so the agenda, the fiche's visit link, « À clôturer » and the
money all keep working with no second source of truth. The doctor's « Patient suivant » is one server command that
ends the visit in the chair, calls the next patient by the one ordering rule, and tells reception through the staff
chat's existing delivery (SignalR, chime, Windows toast, phone push). Clinics that never press « Arrivé » see no
change at all.

## What already exists (reuse, do not duplicate)

| Piece | Where | Becomes |
|---|---|---|
| « Signaler l'arrivée » — a chat message, writes nothing on the RDV | `SendArrivalNoticeCommand.cs:16-125`, `GetTodayArrivalsQuery` | « Arrivé » stamps `ArrivedAtUtc` **and** still posts the same notice; « today's arrivals » reads the column |
| `next-patient` « Faites entrer le patient suivant » quick message (removable) | `StaffQuickMessageDefaults.cs:29` | a new **built-in** `call-in` key carries the call; `next-patient` stays as typed chat |
| Chat delivery: chime, 8 s notice, `shell.notify`, FCM/WebPush 3 s later | `web/lib/staff-chat/alerts.ts`, `StaffChatPushQueue.cs` | carries the call-in notice (sticky until « OK », rings even when muted) |
| « Au fauteuil » card on the doctor's day board — derived from the **clock** | `day/now-next-cards.tsx:200-218`, `day-summary.ts:313-323` | reads `CalledInAtUtc`/`LeftChairAtUtc` when today has arrivals, else unchanged |
| « Terminer la séance » ends the visit | `FicheVisitCompletion.cs:47-51` | also stamps `LeftChairAtUtc`; its toast offers « Patient suivant : X » |
| `AppointmentProgressJob` — `InProgress` at slot start, `AwaitingClosure`/`Completed` at slot end | `AppointmentProgressJob.cs:96-284` | skips patients the waiting room owns (see Job) |
| `WaitingListEntry` — a **slot** waitlist, labelled « Salle d'attente » in 3 places | `AuditLabels.cs:51`, `WaitingListController.cs:14`, dashboard chip `dashboard-blocks.ts:104` | renamed « Liste d'attente » everywhere; the name now belongs to this feature |

## Decisions (made, reversible)

- **Queue order** = `max(slot start, arrived)` for a booked patient, `arrived` for a walk-in; ties by `arrived`.
  « Nobody goes before their RDV time; a late patient loses his place. » The doctor can always « Appeler » anyone.
  **One owner**: `WaitingRoomOrder` (Domain service) — the read and the « Suivant » handler both call it; the
  browser never re-sorts.
- **« Parti »** (left before being seen) = `Cancel` with a fixed reason. Keeps him out of « À clôturer ».
- **Encaissement ends the chair** for a visit that is in it (the user's « when payment is done we know »).
- **Secretary's home** becomes `/salle-attente` (today `/` redirects her to `/appointments`). Agenda one tap away.
- **Doctor**: a « Patient suivant » pill in the app header on every page (fiche included); the page holds the list.
- **Lock screens learn nothing**: the in-app banner names the patient; the Windows toast and phone push say only
  « Le docteur appelle le patient suivant ».
- **The agenda is unchanged** except that its existing « Signaler l'arrivée » (`ArrivalBell`, ⋯ menu) now is
  « Arrivé ». No new pill, no new colour (owner's 11-19 rule for the agenda).
- **Reception is a kanban board** (owner, 2026-11-25, « like a Jira kanban »): four lanes **À venir → En salle
  d'attente → Au fauteuil → Terminé**, cards dragged between lanes. Every drag has a button twin, so nothing is
  reachable only by a gesture (`frontend-web.md` § 9): « Arrivé », « Faire entrer », « Terminer », « ⋯ › Parti sans
  être vu ».
- **Every action is reversible** (owner, 2026-11-25: « someone marked terminé by mistake — all actions
  reversible »). Two layers, both required:
  1. **« Annuler » for 8 s** after every reception action and every doctor call (a toast with an action, never a
     modal) — restores exactly the previous state, the « Terminé » or « Parti » included.
  2. **Any card can be moved to any column, at any time**, by drag or by its ⋯ menu (« Remettre au fauteuil »,
     « Remettre en salle d'attente », « Annuler l'arrivée »). The single refusal: a walk-in cannot go back to
     « À venir » (he never had a slot). « Parti sans être vu » is not a disappearance — the card goes to
     « Terminé », marked in red, and comes back like any other.

  | To → | Means | Command |
  |---|---|---|
  | À venir | arrival undone (booked patient only); clears all three times | `UndoArrival` |
  | En salle d'attente | arrived; or back from the chair / from « Terminé » / from « Parti » (keeps `ArrivedAtUtc`) | `MarkPatientArrived` / `ReturnToWaiting` |
  | Au fauteuil | the secretary brings him in herself, or a finished visit re-opened; whoever was in the chair moves to « Terminé » | `CallIn(appointmentId)` / `ReopenChair` |
  | Terminé | visit over | `EndChair` |
  | Terminé (Parti) | left before being seen | `LeaveWaitingRoom` |

  ⚠️ Re-opening is the expensive half. `AllowedTransitions` has **no** `Completed → InProgress` and must not get one
  (the status control would offer it). `ReopenChair` / `ReturnToWaiting` are domain methods that may undo **only
  what the waiting room itself wrote** (a `Completed` reached through `LeaveChair`, a `Cancelled` reached through
  `LeaveWithoutBeingSeen`), on the clinic's today; a visit completed by the job, cancelled from the agenda or
  billed stays the RDV window's business and the menu says so by not offering it. A saved fiche or note is never
  touched by a reversal — only the presence times and the status move.
- **One search box for every arrival**, at the top of the board: the name typed finds today's RDV first (« Arrivé »),
  then the patient file (« Sans RDV »), then « Nouveau patient « … » ». No separate « sans RDV » button: the
  secretary never has to decide which kind of arrival it is.
- **The main move from « En salle d'attente » is « Au fauteuil »** (« Faire entrer »); « Parti sans être vu » is
  the rare case and lives in the card's ⋯ menu.

## Files to create

- `api/ClinicManagement.Domain/Services/WaitingRoomOrder.cs` — the ordering rule; pure, testable.
- `api/ClinicManagement.Application/Features/Appointments/Commands/MarkPatientArrivedCommand.cs`
- `…/Features/Appointments/Commands/RegisterWalkInCommand.cs` — existing patient **or** name(+phone) → patient +
  appointment now, arrived.
- `…/Features/Appointments/Commands/CallNextPatientCommand.cs` — « Patient suivant » / « Appeler » (doctor).
- `…/Features/Appointments/Commands/CallInCommand.cs` — reception's « Faire entrer » / drag to « Au fauteuil »
  (any role; no call-in notice — the doctor's list updates live).
- `…/Features/Appointments/Commands/UndoArrivalCommand.cs` — drag back to « À venir ».
- `…/Features/Appointments/Commands/UndoCallInCommand.cs` — the toast's « Annuler ».
- `…/Features/Appointments/Commands/LeaveWaitingRoomCommand.cs` — « Parti ».
- `…/Features/Appointments/Commands/EndChairCommand.cs` — reception's « Terminer ».
- `…/Features/Appointments/Queries/GetWaitingRoomQuery.cs` — today's lanes + `serverNowUtc`.
- `api/ClinicManagement.Infrastructure/Migrations/<ts>_AddAppointmentWaitingRoom.cs`
- `web/app/salle-attente/page.tsx` + `layout.tsx`
- `web/components/waiting-room/` — `waiting-room-board.tsx` (four lanes + drag and drop; below 900 px 2×2, below
  540 px one column), `arrival-search.tsx` (the one search box: RDV du jour → patients → « Nouveau patient »),
  `doctor-waiting-room.tsx` (« Au fauteuil » card, queue, thumb-zone « Patient suivant »), `call-in-banner.tsx`,
  `next-patient-pill.tsx`, `wait-badge.tsx`. Drag: reuse whatever the agenda's drag-to-reschedule uses
  (`appointment-calendar.tsx`) before adding a library; pointer events with a long-press on touch, a ghost card,
  allowed lanes outlined while dragging, the card animating into its slot on drop.
- `web/lib/api/waiting-room.ts`, `web/lib/hooks/use-waiting-room.ts` (realtime `appointments` + 30 s tick).

All commands live under `Features/Appointments` → they emit the existing `appointments` realtime key, so
`clinic-hub.ts` and `RealtimeResourceResolverTests` need no change.

## Files to modify

- `Domain/Entities/Appointment.cs` — `ArrivedAtUtc`, `CalledInAtUtc`, `LeftChairAtUtc` (`DateTime?`),
  `IsWalkIn` (`bool`); methods below.
- `Infrastructure/Persistence/Configurations/AppointmentConfiguration.cs` — four columns, **no `HasDefaultValue`**
  on `IsWalkIn` (Infrastructure `CLAUDE.md` § store-generated trap: a default-valued column is skipped by the archive).
- `AppointmentDto.cs:69` and its **four** hand-filled sites — `CreateAppointmentCommand.cs:282`,
  `UpdateAppointmentCommand.cs:710`, `GetAppointmentQuery.cs:76`, `GetAppointmentsQuery.cs:98` — plus
  `AppointmentRepository.cs:482`, `web/lib/api/types.ts:815`, `web/components/appointment-merge.ts:26,57`.
- `AppointmentRepository.cs:251-274` (start pass) and `:281-304` (elapse pass) — the skips below.
- `AppointmentProgressJob.cs` — same; fix the stale comment at `VisitClosure.cs:103-105` while there.
- `StaffQuickMessageDefaults.cs` — built-in `call-in` (renameable, not removable, like `patient-arrived`).
- `SendArrivalNoticeCommand.cs` — delegates the stamp to `Appointment.MarkArrived`; resend moves nothing.
- `FicheVisitCompletion` callers (`CreateDentalRecordCommand.cs:412`, `UpdateDentalRecordCommand.cs:574`) — stamp
  `LeftChairAtUtc` when they complete a visit that is in the chair.
- Payment writers that resolve an appointment (invoice/fiche/« Encaisser » paths) — call one owner,
  `WaitingRoomExit.EndChairIfOpen(appointment, now)`. **Enumerate them with a grep first** (`regression-safety.md`
  § 2: a shared rule with N call sites).
- `web/lib/staff-chat/alerts.ts` — `call-in`: sticky, rings when muted, OS text without the name.
- `web/components/dashboard/dashboard-page.tsx:128-150` — secretary redirect → `/salle-attente`.
- `web/lib/nav.ts`, `web/components/bottom-nav.tsx` (`BAR_HREFS`) — « Salle d'attente » first under Quotidien;
  `/waiting-list` stays « Liste d'attente ».
- `day/now-next-cards.tsx`, `lib/dashboard/day-summary.ts` — « Au fauteuil » from the columns when present.
- `components/staff-chat/arrival-action.tsx` — label « Arrivé », same endpoint behaviour.
- Rename leftovers: `AuditLabels.cs:51`, `WaitingListController.cs:14`, `dashboard-labels.ts`/`day-phrases.ts`
  chip wording, `page.tsx:287` and `bottom-nav.tsx:34` « walk-in » comments.

## Key signatures

```csharp
// Appointment (Domain)
public DateTime? ArrivedAtUtc { get; private set; }
public DateTime? CalledInAtUtc { get; private set; }
public DateTime? LeftChairAtUtc { get; private set; }
public bool IsWalkIn { get; private set; }

public bool IsWaiting  => ArrivedAtUtc != null && CalledInAtUtc == null && Status is not (Cancelled or NoShow);
public bool IsInChair  => CalledInAtUtc != null && LeftChairAtUtc == null && Status is not (Cancelled or NoShow);

public void MarkArrived(DateTime nowUtc);      // refuses if already arrived / cancelled / not today (code)
public void CallIn(DateTime nowUtc);           // requires IsWaiting; → InProgress; walk-in re-timed to now
public void LeaveChair(DateTime nowUtc);       // requires IsInChair; → Completed if CanTransition, else stamp only
public void LeaveWithoutBeingSeen(DateTime nowUtc); // requires IsWaiting; → Cancel(fixed reason)
public void UnmarkArrived();                   // requires IsWaiting and a booked slot (not a walk-in); clears the 3 times
public void ReturnToWaiting(DateTime nowUtc);  // from in chair, or from a Completed/Cancelled the waiting room wrote; keeps ArrivedAtUtc
public void ReopenChair(DateTime nowUtc);      // from a Completed the waiting room wrote (LeaveChair); clears LeftChairAtUtc

public static Appointment WalkIn(Guid clinicId, Guid patientId, Guid? doctorId, DateTime nowUtc, int durationMinutes);
```

`MarkArrived` on a visit the **job** moved to `InProgress` or `AwaitingClosure` (no `CalledInAtUtc`) puts it back
to `Scheduled` — the arrival is the fact that contradicts the clock's guess. It is a domain method, **not** a new
edge in `AllowedTransitions`, so `ManualNextStatusesFrom` (the status control) gains nothing. It clears
`LateArrivalMinutes` (only InProgress/AwaitingClosure/Completed may carry it, `Appointment.cs:395-398`); the real
lateness is now `ArrivedAtUtc − start`.

```csharp
public sealed record CallNextPatientCommand(Guid? AppointmentId, Guid? ExpectedNextId) : IRequest<Result<CallNextResult>>;
public sealed record CallNextResult(Guid? CalledAppointmentId, Guid? EndedAppointmentId);
public sealed record UndoCallInCommand(Guid? CalledAppointmentId, Guid? EndedAppointmentId) : IRequest<Result>;
public sealed record RegisterWalkInCommand(Guid? PatientId, string? FirstName, string? LastName, string? Phone,
    string? PhoneRegion, Guid? DoctorId) : IRequest<Result<AppointmentDto>>;
public sealed record GetWaitingRoomQuery(Guid? DoctorId) : IRequest<Result<WaitingRoomDto>>;
public sealed record WaitingRoomDto(DateTime ServerNowUtc, IReadOnlyList<WaitingRoomRow> Upcoming,
    IReadOnlyList<WaitingRoomRow> Waiting /* already ordered */, WaitingRoomRow? InChair, IReadOnlyList<WaitingRoomRow> SeenToday);
```

`CallNextPatientCommand` handler, one transaction:
1. Resolve the caller's practitioner (`IDoctorRepository.GetByUserIdAsync`; an admin with no `Doctor` row at a
   one-dentist clinic → that dentist).
2. Load today's (`ClinicClock.TodayRangeUtc`) appointments for that doctor **and** `DoctorId == null`.
3. `InChair?.LeaveChair(now)`.
4. Target = `AppointmentId` or `WaitingRoomOrder.First(...)`; if the button showed `ExpectedNextId` and it is no
   longer waiting → `Result.Failure(…, "waiting-room-already-called")` (client refetches silently).
5. `target.CallIn(now)`; a null `DoctorId` is assigned to the caller.
6. Post `StaffMessage` (`call-in`, `PatientId`, `AppointmentId`) to `desk:<doctorUserId>`; push after commit.
No user-supplied `Version` (the job writes appointments every minute — `CLAUDE.md` trap); the guard is the state
check in step 4 plus an idempotency key (`check:responsive` N51).

## Job

- **Start pass**: skip `ArrivedAtUtc != null` (the waiting room owns it).
- **Elapse pass**: skip `IsWaiting || IsInChair` **while the slot's clinic day is today**; on a later day they fall
  back to today's rules (fiche → Completed, else AwaitingClosure) and `LeftChairAtUtc` stays null — never a made-up
  time.
- Unchanged for every appointment with `ArrivedAtUtc == null`: a clinic that never uses the feature sees nothing new.
- `AppointmentProgressQueryTranslationTests` covers the SQL side of both filters.

## Wiring

- MediatR handlers are picked up by assembly scan; no DI change.
- New endpoints on `AppointmentsController` (`/appointments/waiting-room`, `/{id}/arrived`, `/walk-in`,
  `/call-next`, `/call-next/undo`, `/{id}/left`, `/{id}/end-chair`), `AnyClinicRole` except call-next/undo
  (doctor or admin).
- Migration: four `AddColumn`s on `Appointments`. ⚠️ Delete the differ's `AddColumn<uint>("xmin")` lines. No
  backfill. Archive, PC de secours relay and recovery points carry the columns automatically (built from the model).

## Potential pitfalls

- **Name clash**: « Salle d'attente » already labels the slot waitlist in audit, a controller and a dashboard chip
  that counts **waitlist entries**. Rename first, or the doctor's « 3 en salle d'attente » chip lies.
- **Four unguarded DTO fill sites** — the repo's dominant defect (« a correct helper wired to one call site out of
  four »). Add a test that every `AppointmentDto` construction carries the three times.
- **Payment writers are many** — one owner (`WaitingRoomExit`), enumerated by grep, plus a derived test listing them.
- **Device clocks drift** (Rawand's PC: −1 s today, others worse) — the timer counts from `ServerNowUtc` captured on
  each read, never from `Date.now()` alone.
- **Two « today »s** — server `ClinicClock.TodayRangeUtc`, client `todayLocalIso()`; never `toISOString()`.
- **Multi-doctor** (PR #116 unmerged, overlaps `edit-appointment-dialog.tsx` and the « À clôturer » doctor filter) —
  the walk-in dialog shows doctor chips only with 2+ active dentists; null-doctor rows appear in every queue and are
  claimed by whoever calls them.
- **PR #117** (séance au fauteuil, unmerged) touches « Encaisser » and « Terminer la séance » — merge it first or
  build on its branch.
- **A double tap or two devices** on « Patient suivant » — `ExpectedNextId` + idempotency key, never a second call.
- **Frontend contract** (`frontend-web.md`): lanes are a `CardList`, the walk-in dialog becomes a full-screen sheet
  below `md:`, 44 px on coarse pointers, the call-in is a **toast-style notice with an action**, never a modal;
  patient names via `PatientNameLink` (N13) but the row's big button stays the primary target.
- **No helper text** under controls (owner rule): the mock-up's labels are the whole explanation.

## Test strategy

Unit (`api/ClinicManagement.UnitTests`, xUnit + Moq, fixed UTC dates):
- `Domain/AppointmentWaitingRoomTests` — `MarkArrived` from every status (table-driven, allowed/refused), the
  job-guess revert clears `LateArrivalMinutes`, `CallIn` needs arrival, `LeaveChair` → Completed, after a fiche's
  Completed it only stamps, « Parti » → Cancelled; every move has its way back (`UnmarkArrived`,
  `ReturnToWaiting` from chair / Completed / Parti, `ReopenChair`), each refusing a status the waiting room did not
  write (job-completed, agenda-cancelled), and none touching a fiche or a note.
- `Features/Appointments/WaitingRoomReversibilityTests` — a derived test: for every lane pair the board allows,
  the move then its reverse returns the appointment to its exact previous status and times.
- `Domain/WaitingRoomOrderTests` — early RDV waits for its time, late RDV loses its place, walk-ins by arrival, ties.
- `Features/Appointments/CallNextPatientCommandTests` — ends + calls; explicit target; stale `ExpectedNextId` →
  code; null doctor claimed; message posted to the right desk; nobody waiting → ends only; undo restores both.
- `Features/Appointments/RegisterWalkInCommandTests` — new vs existing patient, no overlap refusal, default doctor.
- `Api/AppointmentProgressJobTests` — start pass skips arrived; elapse pass skips waiting/in-chair today, resolves
  them on a later day.
- `StaffQuickMessageDefaults` — `call-in` is built-in.
- `AppointmentStatusTransitionTests` stay untouched — **no new status** is the point.

e2e (`features/e2e-hot-paths/scenarios.md`, new prefix `WAIT-` added to `check-coverage.mjs:67`), tier 0:
WAIT-01 arrival shows in the doctor's list with its time · WAIT-02 walk-in = agenda RDV + waiting · WAIT-03
« Patient suivant » ends the chair, calls the next, desk banner names him · WAIT-04 order rule · WAIT-05 undo ·
WAIT-06 the job leaves a waiting patient alone · WAIT-07 « Parti » · WAIT-08 encaissement ends the chair.
`<!-- LAYER:BROWSER -->`: WAIT-03 banner, WAIT-05 toast, the live timer.

Gate: full unit suite, `check:responsive` + `tsc --noEmit` + `npm run build`, `check-coverage.mjs`, then
`/test-in-browser` at 320/390/820/1180/1440 with a secretary and a doctor session side by side.
