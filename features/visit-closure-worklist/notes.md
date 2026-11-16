# visit-closure-worklist — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## A séance is not finished until three things are answered, and the app now asks (`visit-closure-worklist`)

« À clôturer » — `GET /api/appointments/to-close` (`AnyClinicRole`, paged, 7 clinic-local days by default) lists
every visit whose slot has passed and which still owes one of **est-il venu · qu'a-t-on fait · combien a-t-il
payé**. Surfaced at `/a-cloturer`, as a one-line strip above the agenda, and as an « À traiter » chip on the
dashboard. `Domain/Services`-style pure rules live in `Application/Features/Appointments/VisitClosure.cs`;
`VisitClosureReader` is the one assembly of the four batched reads, called by **both** the worklist and the
dashboard count so the chip and the page it opens cannot disagree.
⚠️ **Nothing is stored.** A visit is open because a record is *absent* — `DentalRecord.AppointmentId` and
`Invoice.AppointmentId`, both already persisted and indexed. A `VisitClosureTask` table written by each write
path is the design `GetCaisseLedgerQuery` rejected in writing (« the day one write site forgets, the statement
and the totals disagree and nothing can say which is right »); a derived read cannot drift, because it *is* the
absence. It also finally drains what `AppointmentProgressJob` deliberately leaves: that pass only ever **starts**
a visit — leaving a slot is not evidence the patient came — so before this, every visit rotted in `InProgress`
for ever with nothing asking a human.
⚠️ **The gaps cascade, they do not stack, and that is the whole UX.** `VisitClosureState.NextStep` yields **one**
question — presence, then fiche, then money — because three red badges on a visit that ended an hour ago is
nagging *and* asks two questions that cannot be answered yet: a visit nobody has confirmed happened is not
« missing » a fiche, and a séance with no fiche has no acts to price. The cascade is derived server-side; the
client renders the other two as inert progress.
⚠️ **The feature could not have been built on `DentalRecord.AppointmentId` as it stood.** That column was
populated by exactly **one** door — the post-visit prompt's deep link — so a fiche charted the ordinary way from
the patient's page stored `NULL`, and the worklist's first screen would have reported « pas de fiche » for most
visits that have one. `Features/Patients/DentalRecordVisitLink` is the fix: the client's id wins, else **exactly
one** non-cancelled visit on the fiche's own **clinic-local** day is linked, and zero or several leave it null —
a missing link costs one row on a worklist, a wrong link completes another visit. `fixes-dont-propagate` again.
A **`BackfillDentalRecordAppointmentLinks`** migration applies the same rule in SQL to existing history (required,
not tidy), and `verify-schema`'s **`dental-record-visit-links-backfill`** is what makes a backfill that covered
nothing visible — the one class of failure no other layer can see.
⚠️ **A consequence worth knowing**: charting a fiche from the patient page now marks that day's visit
« Terminé » and withdraws its post-visit prompt. That *is* the loop — filling the record is the evidence the
patient came — and it stays safe because `MarkVisitCompleted` returns `Contradicted` rather than throwing for a
cancelled or missed visit, and the inference refuses every ambiguous day.
⚠️ **« Rien à facturer » is the escape hatch of LAST resort.** Three cases are derived and stay derived — a fiche
whose `Cost` is 0 (contrôle gratuit), a séance carrying a **debt-bearing** devis step (the money is on the
échéancier), and a non-cancelled note. A patient who will pay later is not one of them either: that is an issued
invoice with an outstanding balance, i.e. a créance. What is left gets `Appointment.NothingToBillAtUtc` + a
**mandatory motif** + its author (`POST /api/appointments/{id}/nothing-to-bill`, withdrawable) — because without
it a row nothing can satisfy stays flagged for ever, and *an alarm that is always on is one nobody reads*.
⚠️ **A plan link alone is not cover**: an appointment keeps it after the devis is **cancelled**, so
`ITreatmentPlanRepository.GetDebtBearingItemIdsAsync` filters through `PlanBillingRules.DebtBearingPlanStatuses`
— otherwise those visits would read « facturé » with no money behind them, the exact failure
`AppointmentInvoiceLinks` excludes cancelled notes to avoid.
⚠️ **The primary surface is deliberately NOT the dashboard.** `GET /api/dashboard` is `AdminOrDoctor` and
`app/page.tsx` redirects a secretary to `/appointments`, so a worklist living only there would be invisible to
reception — who is exactly the person who knows whether the patient came and who takes the money. Hence
`AnyClinicRole` on the read and the agenda strip; the chip is the owner's morning view of the same figure.
⚠️ **It stays under `Features/Appointments`**: a `Features/VisitClosure` folder would emit a `visitclosure`
realtime key `clinic-hub.ts` does not declare, and `RealtimeResourceResolverTests` compares the two sets in both
directions. ⚠️ The end-of-slot test runs **in memory** over a bounded window for `GetRunningNotStartedAsync`'s
reason (`Duration` is ticks behind a value converter, and `AppointmentEndDateTime` is deliberately unmapped).

## A fiche saved before its visit is linked by the save that books the visit (`fiche-visit-link-after-booking`)

⚠️ **The rule above was asked ONCE, at the fiche's creation, and a walk-in is charted first and booked after.**
Production, November 2026: a fiche saved at 12:08, its visit booked at 12:31 for that same morning. Nothing asked
again, so « À clôturer » asked for a fiche the patient already had, and `verify-schema`'s
`dental-record-visit-links-backfill` failed the next deploy (the deploy itself had gone through). The dev copy held
16 such rows.
⚠️ **The second asking lives in the SAVE, not in the handlers** — `Infrastructure/Persistence/DentalRecordVisitLinks`,
called first thing in `ApplicationDbContext.SaveChangesAsync`. The condition is created by seven kinds of write
across five handlers (a visit booked, one or a series · moved to another day · cancelled or marked absent leaving
one twin · reactivated · reassigned by the calendar-import merge) plus a fiche whose date is corrected; wiring each
is the `fixes-dont-propagate` shape. A save sees all of them, and a new writer needs no knowledge of the file.
⚠️ **The DbContext and not an interceptor**: `ClinicChangeCapture` and the audit chain read the tracker in
`SaveChangesAsync` before the base save runs the interceptors, so an interceptor's link would reach neither the PC
de secours nor the journal. `DentalRecordVisitLinksTests.The_Save_Links_Before_Anything_Else_Reads_The_Tracker`
holds the position against the file's own source (proven red by moving the call).
⚠️ **Same rule, same limits**: exactly one non-cancelled, non-missed visit on the fiche's Tunisian day
(`DentalRecordVisitLink.OnlyCandidate` / `CanBeDocumented` / `DayOf`); the column and nothing else — **no status,
no prompt withdrawn** (the backfill's rule; the presence question stays human); a fiche already linked is never
re-pointed (`DentalRecord.LinkToVisit`). It judges on the **pending** values: the two reads only bring the stored
rows into the tracker, and identity resolution hands back an already-tracked row with its pending state.
⚠️ **Free on the hot path**: `AppointmentProgressJob` writes every open visit every minute, but every status it
writes keeps the visit a candidate on the same day, so `DaysToAskAgain` is empty and nothing is read.
⚠️ **A failure costs the link, never the save**: logged as « A fiche could not be linked to its visit », and
`verify-schema` names the row — whose message now says a write bypassed the save (raw SQL) or this step failed.
The relay's row store and handback write through raw SQL, so a PC de secours mirroring the cloud derives nothing.
**`RelinkOrphanDentalRecordVisits`** reruns the original backfill's statement — character for character, held by a
test — for the rows written before this existed, which no save will ever touch again.
Measured on a copy of the dev database through the branch API: the migration linked 16 → 0; a booking after a
fiche linked it with an audit row naming the person and `AppointmentId`, status untouched; cancelling one of two
visits linked the fiche to the other; `verify-schema` read 0.
