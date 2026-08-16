# Séance passée — a visit whose slot has ended says so

**Status:** APPROVED
**Type:** Small
**Created:** 2026-08-16

## Problem

`AppointmentProgressJob` moves a booked visit to `InProgress` once its slot has begun, and **nothing ever moves
it on** — « Terminé » and « Absent » are human decisions and a saved fiche is the only automatic close. Two
consequences, both reported from the agenda:

1. **A « créneau occupé » is started too.** `RunningCandidateQuery` filters on status and time only, never on
   `PatientId`, so a blocked hour acquires « En cours » and keeps it for ever. Three commits have already
   worked around this at the *rendering* layer — `b76067a` (the day board's « Au fauteuil » card), `ddb7bd8`
   (`chairClaim`'s ranking), `f441d1d` (every day figure) — and a fourth workaround is uncommitted in
   `appointment-calendar.tsx` right now (`statusEdgePaint` suppressing the status strip for a blocked slot).
   Four surfaces defending against one wrong row.

2. **A real visit reads « En cours » at 18h.** `VisitClosureRules` already names this in a comment —
   *"Scheduled / Confirmed / InProgress past their own slot all mean the same thing — nobody has said"* — but
   the agenda badge does not, so the one screen the desk reads all day states something false.

## What changes

A seventh `AppointmentStatus`, **`AwaitingClosure = 7`**, French **« Séance passée »**, written only by
`AppointmentProgressJob`. The same tick gains a second pass:

| Row, slot ended, still open | Becomes |
|---|---|
| has a `PatientId` | `AwaitingClosure` |
| no `PatientId` (« créneau occupé ») | `Completed` — a block has nothing to close |

The status answers **one** question — *la présence* — and `VisitClosureRules` keeps answering the other two
(*fiche*, *encaissement*). One authority per question: the **badge** says whether anyone has confirmed the
patient came; the **worklist** says what is still owed.

### Backend

- `AppointmentStatus.AwaitingClosure = 7` — appended; the column is an `int` and members are never reordered.
- `Appointment`: transition table rows, `FrenchLabel`, `MarkAwaitingClosure()`, and **`Reschedule()` drops it to
  `Scheduled`** beside the existing `NoShow` branch.
- `IAppointmentRepository.GetElapsedOpenAsync` + `ElapsedCandidateQuery` (static, so the translation test
  compiles the production expression tree, as `RunningCandidateQuery` already is).
- `AppointmentConfiguration`: `HasIndex(Status, AppointmentDateTime)` + its migration. There is no index on
  `Status` today and this pass runs every minute over 30 days.
- `AppointmentProgressJob.CloseElapsedAppointments(nowUtc)`, 30-day lookback, same actor / tenant scope /
  per-clinic save / broadcast as the start pass.
- `TreatmentPlanWorkflowProjection.LiveStatuses` += `AwaitingClosure`.
- `ExportTables.Appointments`: the `Statut` column writes the **French** label. Adjacent defect — it emits the
  raw English enum name today, the same defect L5 fixed for the « Sexe » column.

### Frontend

- `appointment-labels.ts`: the status in all three maps, tone `pending`, plus a
  **`MANUALLY_SETTABLE_STATUSES`** export.
- `edit-appointment-dialog.tsx`: the `allowedNextStatuses` fallback uses that new export — nothing may set
  « Séance passée » by hand.
- `appointment-calendar.tsx`: a blocked slot is exempt from the « Terminés » switch, and a « Séance passée »
  visit is not draggable.

### Deliberately unchanged

`VisitClosureRules` (`presenceAnswered == Completed` must stay, because the pass may not have run yet),
`/a-cloturer`, `visit-closure-strip`, `dashboard-links`, `DashboardActivityReader`, `PatientRepository`,
`ProcedureType.IsInUse`, `GoogleCalendarSyncService`, and the PostgreSQL exclusion constraint
(`"Status" NOT IN (5, 6)` — a status-7 row correctly keeps holding its slot, so **no constraint migration**).

## Acceptance criteria

- **AC-1** A visit with a patient whose slot has ended and which is `Scheduled`/`Confirmed`/`InProgress` reads
  « Séance passée » on the agenda within a minute.
- **AC-2** A « créneau occupé » whose slot has ended reads « Terminé », and is still visible on the agenda
  without turning the « Terminés » switch on.
- **AC-3** Saving a fiche against a « Séance passée » visit closes it to « Terminé ».
- **AC-4** « Venu » / « Absent » on `/a-cloturer` still work from the new status.
- **AC-5** Rescheduling a « Séance passée » visit to a future date leaves it « Planifié ».
- **AC-6** « Séance passée » is never offered as a manual choice in the edit dialog.
- **AC-7** A visit already `Completed`, `Cancelled` or `NoShow` is untouched, and the pass issues **no save**
  for a clinic with nothing to change.
- **AC-8** A plan act booked into a visit that ended yesterday does not revert to « À planifier ».
- **AC-9** The agenda CSV's `Statut` column is French.

## Residual, stated not hidden

A visit older than the 30-day lookback keeps whatever status it holds. It still appears on « À clôturer »,
which is where an unanswered visit belongs; the pass corrects the window a practice actually looks at.
