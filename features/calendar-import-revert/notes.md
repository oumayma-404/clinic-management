# calendar-import-revert — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident. `spec.md` is
what was asked for; this is what shipped, including the two places where it deliberately shipped something else.

Two commits: the undo (`29fe7e67`), then the retirement of the import that made it necessary.

## An import was a run, a run can be undone — and then the import was retired (`calendar-import-revert`)

A cabinet pressed « Importer depuis Google » once. One call turned **97 days** of its Google calendar into
appointment rows; the seven days of *past* events were the damage, because `AppointmentProgressJob` moved every
one of them to `AwaitingClosure`, so each landed on « À clôturer » demanding présence → fiche → encaissement, and
every unmatched event title minted a placeholder patient plus a notification. There was no way back.

There was no honest way **forward** either, and that is the part worth remembering: `VisitClosureRules.IsClosable`
let a visit leave the worklist only as `Completed`, `Cancelled` or `NoShow` — three claims about a patient. The
product had no way to say « ce rendez-vous n'aurait jamais dû exister ». So the cabinet cancelled them, and two
things happened that nobody wanted. `DashboardActivityReader`'s `MissedStatuses` counts `Cancelled` beside
`NoShow`, so every cancellation inflated the « taux d'absence » it was trying to clean up. And
`GoogleCalendarSyncService` **pushed** a delete for a `Cancelled` appointment, so **tidying the app erased entries
from the practice's own Google calendar** — permanently, and before any of this shipped. (That call is gone now; see
« Nothing deletes a Google event any more » below. The entries already destroyed stay destroyed.)

⚠️ **The statistics needed no repair code, and that was the design.** The taux d'absence, « Rendez-vous par
statut » and every activity figure are *derived reads over appointment rows*. Delete the phantom rows and the
arithmetic is correct again, including the rows the cabinet had already cancelled — they belong to the same run.
Anything that "recomputed" a stored KPI here would have been a second authority over one number.

### The run, and why the identity had to be retro-fitted

Every Google→App pass opened a `CalendarImportRun` (clinic, actor, window, four counts) and stamped its id on
each appointment and patient it **created** — never on one it merely updated or matched, because those existed
before. `Appointment.CalendarImportRunId` and `Patient.CalendarImportRunId` are plain indexed columns with **no
navigation and no FK** (`StaffNotificationConfiguration`'s shape): it sidesteps every cascade question, and a run
is never deleted.

⚠️ **A backfill migration was the entire point, not tidiness.** The cabinet that needed this was the one that had
*already* pressed the button. `20260901132503_AddCalendarImportRunsAndWorklistDismissal` synthesises a run per
historical burst from three signals that must agree: `Patients.CalendarImportPendingReviewSince IS NOT NULL`;
`Appointments.GoogleCalendarEventId IS NOT NULL AND DoctorId IS NULL`; and `Appointments.PatientId` in that
burst's placeholders. **The third recovers the appointments the cabinet had already cancelled** — cancelling
pushed the Google delete and nulled the event id, destroying the obvious signal. `verify-schema`'s
**`calendar-import-run-backfill`** exists because a backfill that covered nothing is the one failure no other
layer can see, and nothing in `UnitTests` touches a database.

⚠️ **That migration is committed inside `fbafa5a5`, not inside this feature's own commit.** Three sessions shared
the branch and the auth one swept the migrations plus the whole model snapshot in two minutes earlier. The schema
is correct and applied; but `29fe7e67` alone does **not** contain the schema its code needs, so neither commit
can be cherry-picked or reverted alone.

### The revert

`GET  /api/googlecalendar/imports` · `GET …/imports/{runId}/revert-preview` · `POST …/imports/{runId}/revert`.

- **The preview is the safety of the whole feature.** The person pressing is the cabinet, not the vendor, so the
  confirmation lists every row that will be deleted *and* every row that will survive with its blocker named in
  French. A tighter deletion rule was rejected in favour of this: a tighter rule silently misses the rows the
  feature exists to remove.
- Six blockers keep a visit: a fiche de soins, a **non-cancelled** note d'honoraires, a devis step, a bon de
  prothèse, an edited acte, a payment. A **cancelled** invoice is not a blocker — it bills nothing, the same
  reasoning `AppointmentInvoiceLinks` already applies.
- A placeholder patient is kept when anything beyond this run's own appointments and its import notification is
  attached — `PatientDeletionBlockers.From(PatientLinkedDataCounts)`, the test `MergeIntoSuggestedDuplicateCommand`
  already takes, evaluated **before anything is written**.
- ⚠️ **The revert never routes a deletion through `Appointment.Cancel()`.** Cancel *pushed* a Google delete, so a
  revert that went that way would have finished destroying the calendar it exists to protect. `DeleteEventAsync`
  has since been removed outright, so that particular hazard is gone at the root — the assertions stay because the
  undo has other reasons not to reach Google (it is a bulk write) and because « the capability was removed » is the
  kind of fact a later change can quietly reverse.
- ⚠️ **Reminders are deleted explicitly, before the appointments.** `NotificationConfiguration` is
  `OnDelete(SetNull)`, so deleting an appointment otherwise leaves its scheduled reminder alive with a null
  `AppointmentId` — and the minutely dispatcher would still send it. A patient must not get an SMS for a visit
  that no longer exists. `Notification`, `StaffNotification` and `PushDelivery` all count.
- One `SaveChangesAsync` for the whole revert; a partially-reverted run cannot exist. A **recovery point** is
  taken first (« avant annulation import ») — with no vendor in the loop, that is what makes a self-serve bulk
  delete recoverable, and no net means the revert is **refused**, not attempted.
- A second revert returns a `Result` **code**, never a matched French sentence; two admins at once resolve as a
  409, so the handler's catch-all carries `when (ex is not ConflictException)`.
- ⚠️ It broadcasts **both** `appointments` and `patients`; the second is emitted post-commit resolving the key from
  `typeof(CreatePatientCommand)` rather than a literal (`AppointmentProgressJob`'s precedent) — a typed key drifts
  silently and reads exactly like the feature not running.

### « Retirer de la liste » — the worklist's third exit

Independent of the import, and it outlives it. `Appointment.DisregardedAtUtc` + `DisregardedByUserId`, offered on
both tabs of « À clôturer » (« Retirer de la liste » for a séance, « Ne plus afficher » for a patient à
compléter), one row or a selection, reversible, with a « N séances retirées » way back.

⚠️ **The load-bearing half is `IAppointmentRepository.CountByStatusBetweenAsync`, not the worklist.** A
disregarded visit leaves `VisitClosureRules.IsClosable` **and** the appointment-status counts behind the
dashboard. Excluded from one but not the other, the list goes quiet while the absence rate stays *exactly* as
wrong as before — which is the complaint the whole feature answers, and it is invisible unless somebody checks
the dashboard afterwards.

⚠️ **The patients tab must not reuse `POST /patients/{id}/confirm-calendar-import`.** That clears
`CalendarImportPendingReviewSince`, which is the signal the backfill above uses to find placeholders. A bulk
dismiss that cleared it would destroy the evidence the revert needs — the same self-inflicted loss as a
cancellation nulling `GoogleCalendarEventId`. « Je ne veux plus voir ça » is not « j'ai vérifié cette fiche », so
`Patient.CalendarReviewDismissedAtUtc` is a **separate column** and the SQL `pendingCalendarReviewOnly` filter
excludes it.

⚠️ **It shipped demanding a motif, and that was reversed.** The spec required one on
`NothingToBillAtUtc`'s reasoning — « pourquoi ? » should stay answerable. The parallel does not hold: « rien à
facturer » is a claim about money the cabinet may be asked to justify, whereas this asserts nothing at all, so
there is nothing to justify. Over the hundred-odd rows it exists for, a mandatory sentence **priced the honest
exit above the dishonest one** — and the dishonest one is the annulation that caused the wrong absence rate. The
motif is gone from the entity, the command, both routes, the client and the dialog (which now *confirms* rather
than collects), and `DisregardedReason` was dropped in `20260901174001_RetireGoogleCalendarImport`. It had been
**write-only since the day it shipped** — nothing ever read it into a DTO or a screen, so the spec's
« le motif reste visible sur la séance » was never true. `AuditSaveChangesInterceptor` still answers who and when.

⚠️ **« Voir les fiches masquées » listed rows nobody had masked, and only a browser walk could see it.** The
repository flag was `includeDismissedReview` and it **widened** the read — pending records *plus* dismissed ones —
so the hidden view showed every patient à compléter and offered « Réafficher » on the ones that were never hidden:
a control that undoes nothing, on the one screen whose entire claim is that a dismissal is reversible. Every layer
reported success, and a mocked repository applies no predicate, so **no handler test can see which rows come
back**. It is now `dismissedReviewOnly`, and the two views are exact complements — the property the séances tab
already had by construction (« à clôturer » and « retirées » cannot overlap). Fixed **in SQL**, not by narrowing
the page: filtering an already-cut page of 20 answers « the masked ones among these 20 » and reports « aucune fiche
masquée » for a practice whose masked rows sit on page 2. `PatientRepository.PendingReviewQuery` is shared with
`PendingReviewComplementTests`, which compiles that expression rather than a copy of it.

### Nothing deletes a Google event any more

The spec listed this as out of scope — « the push deleting the Google event when a visit is **`Completed`**, which
makes a practice's calendar erase itself as each day is worked. A real question, a separate one. » It is answered,
and more broadly than the line asked.

`IGoogleCalendarService.DeleteEventAsync` fired on `Cancelled || Completed`. « Terminé » is the most ordinary action
in the product — « À clôturer » asks for it on every visit and `AppointmentProgressJob` reaches the same path — so
**every appointment a cabinet actually honoured was erased from its own Google agenda** and the event id nulled,
silently. A dentist looking back at last Tuesday found the day they had worked *emptier* than the day they had not.
The same call on cancellation is how this cabinet, tidying up the unwanted import, destroyed a hundred real entries
of its own calendar — a loss that is permanent and that no undo in this product can reach.

⚠️ **The fix removed the capability, not its call sites.** `DeleteEventAsync` is gone from the contract *and* the
client, so « never » is a compile error rather than a condition one character away from coming back. The calendar
belongs to the practice: this product adds to it and corrects what it added, and removes nothing.

⚠️ **A terminal visit keeps its event, and never gains one it never had.** It falls through to the update path,
which rewrites the description with `Status: Cancelled` / `Status: Completed` — strictly more information than
deleting gave. But a terminal visit with *no* event id returns early rather than falling through, because the
branch below it **creates**: without that guard, closing a historical visit on « À clôturer » would push a fresh
event into Google months after the fact — including for every row whose id the old delete had already nulled.

`GoogleCalendarNeverDeletesTests` guards it three ways, because each catches a different way it returns: reflection
over the contract, reflection over the client, and a **source scan** for the SDK's own `service.Events.Delete`
called inline with no method of ours wrapping it — the one shape reflection cannot see. Red-proofed by injecting
that exact shape and watching it fail.

### The retirement

`« Importer depuis Google »` is gone: the button, its confirmation dialog, `POST /sync-from-google`, the
15-minute `GoogleCalendarImportJob`, `IGoogleCalendarSyncService.SyncGoogleCalendarToAppointmentsAsync`, the
`import-settings` endpoint with its `Clinic.GoogleCalendarHoldsOnlyAppointments` gate, and ~750 lines of
event-to-patient guesswork inside `GoogleCalendarSyncService`. **Sync is one-way now, by design**: App→Google
still runs inline post-commit per appointment.

⚠️ **Deleting the `RecurringJob.AddOrUpdate` call is not enough, and this is the trap.** The entry stays in
Hangfire's storage on every already-deployed install and goes on firing every 15 minutes at a type that no longer
exists. `Program.cs` calls `RecurringJob.RemoveIfExists("import-from-google-calendar")` — the literal is the
stored job's id, so it cannot be reworded. Two precedents sit beside it (`sync-google-calendar`,
`dispatch-einvoices`).

⚠️ **The undo deliberately outlived the importer, and that asymmetry is the thing to not "tidy up".**
`CalendarImportRun`, its repository and EF config, the three `imports/…` routes, the revert command and rules,
the banner and the pending-review block all **read history** — they import nothing. A cabinet whose worklist
still holds an import it made has no other way back, and those rows are live, not archival. A cleanup that
removed « the calendar import stuff » wholesale would take a working recovery path with it and **nothing would
fail until somebody needed it**. `CalendarImportRetirementTests` is the derived guard: it pins that the sync
contract exposes only the push, that no route imports, that the job type is gone from the assembly — and, in the
other direction, that the three revert routes are still there.

⚠️ **What the clinic loses, stated plainly**: an appointment typed straight into Google Calendar will never
appear in the app again. The 15-minute job existed precisely to close that gap. That is the trade, made
knowingly.

`GoogleCalendarNotesParseTests` went with the parser it covered (`ExtractNotesFromDescription`, import-only). The
description labels `NotesLabel`/`StatusLabel` are now **write-only** — the drift risk their old comment warned
about left with the reader.

### Prevention that shipped and then became moot

`AC-26`/`AC-27` narrowed the import window to the **clinic-local start of today** via `ClinicClock` (never
`UtcNow.AddDays(-7)`) and made the button state its counts and ask before writing. Both shipped in `29fe7e67`
and both left with the button. They are recorded here because they are the right shape for *any* future bulk
import: a past event can only ever become a visit nobody can honestly close.

## Two things the spec asked for that did not ship

- **AC-23 — a « Imports Google » list in Paramètres.** Not built: there is no Google section in Paramètres to put
  it in, and the banner on « À clôturer » covers the case a cabinet in distress actually meets. `GET
  /api/googlecalendar/imports` exists and is unconsumed by the frontend — the one deliberate loose end.
- **The adjacent defect, still open and still the owner's call.** `DashboardActivityReader.cs:23` folds
  `Cancelled` into `MissedStatuses`, so the KPI *labelled* « taux d'absence » counts cancellations. A rendez-vous
  cancelled three weeks ahead is a rebooked slot, not an empty chair. It is one constant plus the drill-through in
  `web/lib/dashboard-links.ts` — but it changes a shipped figure's **meaning**, so it was flagged rather than
  folded in. **After the revert, this is what will still make the figure wrong.**

## One residual risk on real data

An imported appointment booked onto an **existing** patient and then cancelled has no event id, no placeholder
patient, and only `DoctorId IS NULL` plus `CreatedAt` to identify it. The burst window catches it; a genuine
app-created appointment made in the same minutes would be a false positive. **The preview is the mitigation** —
every row is named before anything is deleted. Rows that fall outside the burst stay cancelled and keep inflating
that cabinet's absence rate; « Retirer de la liste » is the manual way out for them.

And already lost, permanently, before any of this: every Google event behind an appointment the cabinet cancelled
was deleted from its Google calendar at the moment of cancellation. Nothing here makes that worse and nothing
fixes it.

## Deployment note

⚠️ **The `to-close` API response shape changed** (`disregardedCount`, `includeDisregarded`). Frontend and backend
must ship **together** — a new frontend against an old API renders a raw English error on that page. Not a data
risk, a visible one if a deploy splits them.

One index was dropped on `Appointments(ClinicId)`, superseded by the composite that leads with the same column.
No behaviour change.
