# Feature Specification: An import is a run, and a run can be undone

**Status:** DRAFT — awaiting approval
**Type:** Small
**Created:** 2026-09-01
**Scope:** Full
**Feature:** « Importer depuis Google » records what it created and the cabinet can undo it — deleting exactly the appointments and fiches that pass conjured and nothing has touched since, without ever touching the Google calendar itself. And, independently of any import, a row can be taken off « À clôturer » without claiming anything clinical about it.

> **Two capabilities, one spec, because they collide in one place.** The undo identifies what to delete by the
> review stamp; the dismiss control must therefore never clear that stamp (AC-34). Specified apart, one of them
> would silently break the other.

## Overview

A cabinet pressed « Importer depuis Google », and 97 days of its calendar became appointment rows in one
irreversible call. The seven days of **past** events were the damage: `AppointmentProgressJob` moved every one
of them to `AwaitingClosure`, so each landed on « À clôturer » demanding présence → fiche → encaissement, and
every unmatched title created a placeholder patient plus a notification. There was no way back.

Worse, there was no honest way *forward* either. `VisitClosureRules.IsClosable` lets a visit leave the worklist
only as `Completed`, `Cancelled` or `NoShow` — the product has no way to say « ce rendez-vous n'aurait jamais dû
exister ». So the cabinet cancelled them, which did two things nobody wanted: `DashboardActivityReader`'s
`MissedStatuses` counts `Cancelled` alongside `NoShow`, so every cancellation inflated the « taux d'absence »;
and `GoogleCalendarSyncService` deletes the Google event for a `Cancelled` appointment, so cleaning up the app
**erased entries from the practice's own Google calendar**.

This gives the import an identity and an undo. Every pass opens a `CalendarImportRun` and stamps it on each row
it creates; the cabinet's admin sees what the run did and can revert it. The revert deletes only what the run
created and nothing has touched, keeps and names the rest, and never speaks to Google.

**The statistics need no repair code.** The taux d'absence, « Rendez-vous par statut » and every activity figure
are derived reads over appointment rows. Delete the phantom rows and the arithmetic is correct again — including
the rows the cabinet already cancelled, which are in the same run.

**It works on imports that already happened.** A backfill migration synthesises runs for history, which is the
entire point: the cabinet that needs this today is the one that already pressed the button.

## What Changes

- Every Google→App import pass opens a `CalendarImportRun` (clinic, actor, window, counts) and stamps its id on
  each appointment and patient it **creates**. Rows it merely updates or links are not stamped — they existed before.
- The recurring `GoogleCalendarImportJob` opens runs too, attributed `job|GoogleCalendarImportJob`. A pass nobody
  clicked is exactly the one a cabinet needs to be able to undo.
- `POST /sync-from-google` stops returning `{ message, timestamp }` — which never said what it did — and returns
  the run with its counts.
- A cabinet admin can list recent runs and **preview** a revert: the full list of what would be deleted, and for
  every row that would be kept, the reason in words.
- Reverting deletes the run's appointments and placeholder patients that carry no other data, takes a recovery
  point first, and reports what it kept.
- The undo is offered **where the damage is visible** — a banner on both tabs of `/a-cloturer` and on the import
  panel itself — not only in a settings screen nobody in distress will find.
- The import window starts at the **clinic-local start of today** instead of `UtcNow − 7 days`, and the button
  states the counts before writing rather than after.
- **Independently of the import**, both tabs of « À clôturer » gain a control that takes a row off the list
  without claiming anything clinical about it — « Retirer de la liste » for a séance, « Ne plus afficher » for a
  patient à compléter — one row or a selection, reversible, and honoured by the dashboard figures as well as the
  worklist.

## Acceptance Criteria

### The run

- **AC-1:** Every call to `SyncGoogleCalendarToAppointmentsAsync` opens exactly one `CalendarImportRun` for the
  clinic and closes it with `AppointmentsCreated`, `PatientsCreated`, `AppointmentsUpdated`, `AppointmentsLinked`.
  A pass that creates nothing still records a run — « l'import n'a rien trouvé » is an answer, and a missing run
  is indistinguishable from a pass that never ran.
- **AC-2:** `Appointment.CalendarImportRunId` and `Patient.CalendarImportRunId` are set **only** on rows that pass
  creates. An appointment it updated or linked, and a patient it matched, are never stamped.
- **AC-3:** Counts are stored on the run, not derived. A reverted run must still be able to say what it did after
  its rows are gone.
- **AC-4:** A run opened by `GoogleCalendarImportJob` carries `TriggeredByUserId = "job|GoogleCalendarImportJob"`,
  matching `AuditEntry`'s own convention for an actorless pass, and is revertable like any other.
- **AC-5:** The run is written even when the pass throws part-way: rows already created are stamped and
  recoverable. An import that failed half-way is precisely the one worth undoing.

### The preview

- **AC-6:** `GET /api/googlecalendar/imports/{runId}/revert-preview` returns every row the revert would delete
  (patient name, date) and every row it would keep, each with its blocker **named in French**.
- **AC-7:** A visit is kept when a fiche de soins, a **non-cancelled** note d'honoraires, a devis step, a bon de
  prothèse, an edited acte or a payment names it. A **cancelled** invoice is not a blocker — it bills nothing,
  which is the same reasoning `AppointmentInvoiceLinks` already applies.
- **AC-8:** A placeholder patient is kept when anything other than this run's own appointments and its import
  notification is attached — `PatientDeletionBlockers.From(PatientLinkedDataCounts)`, exactly the test
  `MergeIntoSuggestedDuplicateCommand` takes, and taken **before anything is written**.
- **AC-9:** The preview is a read. Calling it twice changes nothing and returns the same answer.

### The revert

- **AC-10:** `POST /api/googlecalendar/imports/{runId}/revert` is **`AdminOnly`** — it deletes patient records —
  and is scoped to the caller's clinic. Another clinic's run is a 404, never a 403 that confirms it exists.
- **AC-11:** ⚠️ **The revert never calls `IGoogleCalendarService.DeleteEventAsync`, and never routes a deletion
  through `Appointment.Cancel()`.** `GoogleCalendarSyncService.cs:118-132` deletes the Google event for a
  `Cancelled` appointment; a revert that went that way would finish destroying the calendar it is meant to
  protect. Pinned by a test asserting **zero** calls.
- **AC-12:** Reminders are deleted explicitly, before the appointments. `NotificationConfiguration.cs:61-64` is
  `OnDelete(SetNull)`, so deleting an appointment otherwise leaves its scheduled reminder alive with a null
  `AppointmentId` — and the minutely dispatcher would still send it. **A patient must not receive an SMS for a
  visit that no longer exists.** `Notification`, `StaffNotification` and `PushDelivery` all count.
- **AC-13:** The whole revert is one `SaveChangesAsync`. A partially-reverted run cannot exist. (The CSV import's
  per-row commit answers the opposite question: that is an import, this is an undo.)
- **AC-14:** A recovery point is taken before the first delete, named « avant annulation import ». With no vendor
  in the loop, this is what makes a self-serve bulk delete recoverable.
- **AC-15:** A second revert of the same run returns a `Result.Failure` carrying a **code**, never a matched
  French sentence. Two admins pressing at once resolve as a 409 — so the handler's catch-all must carry
  `when (ex is not ConflictException)`.
- **AC-16:** After a revert the run holds `RevertedAtUtc`, `RevertedByUserId`, and the three counts (deleted
  appointments, deleted patients, kept rows).
- **AC-17:** The revert broadcasts **both** `appointments` and `patients`. The first comes from the command's
  namespace; the second is emitted post-commit through `IRealtimeNotifier`, resolving the key from
  `typeof(CreatePatientCommand)` rather than a literal — `AppointmentProgressJob:79-81`'s precedent, and for its
  stated reason: a typed key drifts silently and reads exactly like the feature not running.

### History

- **AC-18:** A backfill migration synthesises a run per historical import burst, grouping by `(ClinicId, burst of
  CreatedAt)` from three signals: `Patients.CalendarImportPendingReviewSince IS NOT NULL`;
  `Appointments.GoogleCalendarEventId IS NOT NULL AND DoctorId IS NULL`; and `Appointments.PatientId` in that
  burst's placeholders — the third being what recovers appointments **already cancelled**, whose event id the
  delete-push nulled.
- **AC-19:** `verify-schema` gains `calendar-import-run-backfill`. A backfill that covered nothing is the one
  class of failure no other layer can see — `dental-record-visit-links-backfill` exists for the same reason.
- **AC-20:** A patient whose review stamp was already cleared (the cabinet completed the fiche, or answered
  « C'est correct ») is **not** backfilled into a run. Confirming a record is the cabinet saying it wants it.
  A patient merely **retiré de la liste** (AC-33) *is* still backfilled — see AC-34.

### Retirer de la liste — « À clôturer », both tabs

Independent of the import, and it outlives it: today the only exits from « À clôturer » are `Completed`,
`Cancelled` and `NoShow` — three clinical claims. A cabinet needs to be able to say « cette ligne ne me
concerne pas » without forging one.

- **AC-28:** `Appointment.DisregardedAtUtc` + `DisregardedReason` + `DisregardedByUserId`, mirroring
  `NothingToBillAtUtc`'s existing trio and its withdrawal semantics exactly.
- **AC-29:** A disregarded visit is excluded in **`VisitClosureRules.IsClosable`** — one place, so the page and
  the dashboard chip cannot disagree (`VisitClosureReader` feeds both).
- **AC-30:** ⚠️ **It is also excluded from `IAppointmentRepository.CountByStatusBetweenAsync`**, the query behind
  the taux d'absence and « Rendez-vous par statut ». Without this, a cabinet retires 143 visits and its absence
  rate is **exactly as wrong as before** — the complaint this whole feature exists to answer. This is the
  difference between a control that works and one that looks like it works, and it is invisible unless the
  dashboard is checked afterwards.
- **AC-31:** Reversible, with a « N séances retirées » count and a way back. A removal nobody can see or undo is
  a black hole, and it is how the worklist stops being trusted.
- **AC-32:** Bulk: selection on the tab, **one motif for the batch**. A mandatory per-row motif across 143 rows is
  unusable; a mandatory per-batch one is not.
- **AC-33:** The patients tab gets the same, writing `Patient.CalendarReviewDismissedAtUtc`. Both
  `pending-review-block.tsx` and the `pendingCalendarReviewOnly` **SQL** filter exclude it. Bulk, reversible,
  same shape.
- **AC-34:** ⚠️ **Dismissal must not reuse `POST /patients/{id}/confirm-calendar-import`.** That clears
  `CalendarImportPendingReviewSince`, which is the signal AC-18's backfill uses to find placeholders — a bulk
  dismiss that cleared it would destroy the evidence the revert needs, the same self-inflicted loss as a
  cancellation nulling `GoogleCalendarEventId`. « Je ne veux plus voir ça » is not « j'ai vérifié cette fiche »,
  and the two must stay separate columns. The per-row « C'est correct » on the fiche stays exactly what it is.
- **AC-35:** Labels are **« Retirer de la liste »** (séances, motif attached) and **« Ne plus afficher »**
  (patients). Not « Ignorer »: beside « Rien à facturer » it reads as a softer escape hatch and staff reach for
  it first.
- **AC-36:** A disregarded visit is never counted as work done anywhere — not in « séances complétées », not in
  the procedure mix, not in any money figure.
- **AC-37:** At 320 px the selection checkbox is ≥ 44 px on a coarse pointer, the selection bar is a **sticky
  footer inside the page scroller** (never a floating toolbar), and the card form of each row keeps its height.

### Where it is offered

- **AC-21:** `/a-cloturer` shows a banner on **both** tabs while an unreverted run still has rows present:
  « 143 rendez-vous et 96 fiches ont été importés depuis Google Agenda le 31 août. Annuler cet import. » It
  disappears on its own once the run is reverted or its rows are gone — it must not become furniture.
- **AC-22:** The import panel in `appointment-calendar.tsx` reports counts and offers the undo inline.
- **AC-23:** A « Imports Google » list in settings is the durable record — date, actor, counts, state — and is no
  longer the only door.
- **AC-24:** The confirmation is an **`alertdialog`**, states both numbers and the recovery point, and puts the
  destructive action second.
- **AC-25:** At 320 px the preview is cards (a table above `lg:`), the confirmation is a sheet in `dvh`, and every
  action is ≥ 44 px on a **coarse pointer**. No horizontal page scroll at any width.

### Prevention

- **AC-26:** The import window starts at the clinic-local start of today via `ClinicClock` — never
  `DateTime.UtcNow.AddDays(-7)`. A past event can only ever become a visit nobody can honestly close.
- **AC-27:** The button states what it is about to do and asks, before the write.

## API Contract

### POST /api/googlecalendar/sync-from-google
Response 200: `{ runId: guid, appointmentsCreated: int, patientsCreated: int, appointmentsUpdated: int, appointmentsLinked: int }`
*(Replaces `{ message, timestamp }`.)*

### GET /api/googlecalendar/imports?limit=10
Response 200: `CalendarImportRunDto[]` — `{ id, startedAtUtc, triggeredBy, appointmentsCreated, patientsCreated, revertedAtUtc, rowsRemaining }`, newest first, ordered on a unique column last.

### GET /api/googlecalendar/imports/{runId}/revert-preview
Response 200: `{ willDelete: { appointments: […], patients: […] }, willKeep: [{ kind, label, date, blockers: string[] }] }`
Errors: `404 { error }` — unknown or another clinic's run

### POST /api/googlecalendar/imports/{runId}/revert
Response 200: `{ appointmentsDeleted: int, patientsDeleted: int, kept: [...], recoveryPointId: guid }`
Errors: `400 { error, code }` — already reverted · `404 { error }` — unknown or another clinic · `409 { error }` — concurrent revert

### POST /api/appointments/{id}/disregard
Request: `{ reason: string }` — required, as `nothing-to-bill` requires its motif.
Response 200: `{ disregardedAtUtc, reason, by }`
Errors: `400 { error, code }` — no motif · `404 { error }` — unknown or another clinic

### DELETE /api/appointments/{id}/disregard
Response 204. Withdrawal, mirroring `nothing-to-bill`'s.

### POST /api/appointments/disregard
Request: `{ ids: guid[], reason: string }` — AC-32, one motif for the batch.
Response 200: `{ disregarded: int, skipped: [{ id, reason }] }`

### GET /api/appointments/to-close
Adds `includeDisregarded: boolean` (default false) and returns `disregardedCount` for AC-31's « N séances retirées ».

### POST /api/patients/dismiss-calendar-review
Request: `{ ids: guid[] }` · Response 200: `{ dismissed: int }`
⚠️ Writes `CalendarReviewDismissedAtUtc` only. It must **not** touch `CalendarImportPendingReviewSince` — AC-34.

### DELETE /api/patients/{id}/dismiss-calendar-review
Response 204.

### GET /api/patients
`pendingCalendarReviewOnly=true` excludes dismissed patients in **SQL**; adds `includeDismissed` to see them again.

## Data / Schema Changes

Two migrations.

**`AddCalendarImportRuns`**
- New table `CalendarImportRuns` — `ClinicId`, `StartedAtUtc`, `CompletedAtUtc`, `TriggeredByUserId`,
  `WindowFromUtc`, `WindowToUtc`, four count columns, `RevertedAtUtc`, `RevertedByUserId`, three revert-count
  columns. Index on `(ClinicId, StartedAtUtc)`.
- `Appointments.CalendarImportRunId` — `uuid`, **nullable**, indexed.
- `Patients.CalendarImportRunId` — `uuid`, **nullable**, indexed.
- `Appointments.DisregardedAtUtc` — `timestamptz`, **nullable**, filtered index (AC-29/AC-30 read it on every
  worklist and every dashboard appointment count).
- `Appointments.DisregardedReason` — `text`, nullable, max 500 like `CancellationReason`.
- `Appointments.DisregardedByUserId` — `text`, nullable.
- `Patients.CalendarReviewDismissedAtUtc` — `timestamptz`, **nullable**, filtered index for AC-33's SQL filter.
  ⚠️ A **separate column** from `CalendarImportPendingReviewSince`, never a reuse of it — AC-34.

⚠️ Both are **plain indexed columns with no navigation and no FK**, following
`StaffNotificationConfiguration.cs:51,73`. It sidesteps every cascade question, and runs are never deleted.

**`BackfillCalendarImportRuns`** — AC-18. Required, not tidy, in `BackfillDentalRecordAppointmentLinks`' shape.

⚠️ Check both scaffolds for `AddColumn<uint>("xmin")` across all 38 entities and delete those lines, and for a
scaffolded `DropColumn` placed **above** a backfill that reads the column it drops. Run `verify-schema` before
and after the batch and diff it.

## Known residual

An imported appointment booked onto an **existing** patient and then cancelled has no event id, no placeholder
patient, and only `DoctorId IS NULL` plus `CreatedAt` to identify it. The burst window catches it; a genuine
app-created appointment made in the same minutes would be a false positive. **The preview is the mitigation** —
every row is listed and named before anything is deleted — not a tighter rule, because a tighter rule would
silently miss the rows this feature exists to remove.

## Adjacent defect — decide in or out

`DashboardActivityReader.cs:22-23` folds `Cancelled` into `MissedStatuses`, so the KPI **labelled** « taux
d'absence » counts cancellations. A rendez-vous cancelled three weeks ahead is a rebooked slot, not an empty
chair. This is a live defect for every cabinet, independent of the import, and after the revert it is what will
still make the figure wrong.

It is one constant plus the matching drill-through in `web/lib/dashboard-links.ts`. Flagged here rather than
folded in silently: **it is a change to a shipped figure's meaning, and that is the owner's call, not the
implementer's.**

## Out of scope

- Retiring the Google→App import. Decided: this ships first, retirement is a later feature.
- The push deleting the Google event when a visit is **`Completed`** (`GoogleCalendarSyncService.cs:119`), which
  makes a practice's calendar erase itself as each day is worked. A real question, a separate one.
