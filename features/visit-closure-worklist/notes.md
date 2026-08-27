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
