# Retard et report — what shipped

A client reported that « Séance terminée » asks only « Absent / Venu », while two other answers happen every day: the
patient **came late**, or **moved the visit**. Both are now one tap from the popup, the agenda ⋯ and « À clôturer ».

## What a user sees

| Where | What |
|---|---|
| Popup, step 1 | **Plus tard · Reporter · Absent · Venu**, one line at 448 px (words only — with icons the four measured ≈ 411 px in a 400 px footer and wrapped) |
| Popup, « Reporter » | A step of the same dialog: the practitioner's free times (day chips « N libres » / « Congé » / « Complet », then the day's starts as « Matin » / « Après-midi »), a calendar, « Autre heure… » for a typed 24 h time, and « Mettre en liste d'attente ». Same form in the agenda ⋯ and « À clôturer » |
| Popup, after « Venu » | « Noter un retard » → 5 · 10 · 15 · 20 · 30 · Autre. Saved at once, writes no statut, « Aucun » removes it |
| Tablet | The toast carries Venu · Absent · Reporter at 44 px; « Reporter » closes it and opens the form as a dialog |
| Agenda ⋯ | « Reporter… » (Planifié · Confirmé · En cours · Séance passée) and a « Retard » row (En cours · Séance passée · Terminé) |
| Agenda block | A « +15 » pill; the block's accessible name says « arrivé avec 15 min de retard » |
| « À clôturer » | Venu · Absent · **Reporter** · ⋯ — the ⋯ holds only « Venu en retard » (statut Terminé + retard, one write). « Reporter » left the ⋯ on the owner's call (2026-10-30) |
| Edit dialog | The retard chips under the statut while the chosen statut can carry one |
| Export CSV | A « Retard (min) » column |

## The rules that are easy to undo by accident

### A retard never moves the RDV, and never grows the slot
`Appointment.LateArrivalMinutes` is a fact about the visit. The **booked slot** — the trigger column
`AppointmentEndDateTime`, the exclusion constraint `EX_Appointments_NoDoubleBooking`, `FindCollisionAsync` — is
untouched, because the slot is what blocks the chair: grown by 15 min, it collides with the next patient.

What a retard *does* move is **« when is the séance over? »**, and `Appointment.ExpectedEnd` is the one answer:
the job's two in-memory residuals (`HasEndedBy`), the review timing in Create / CreateRecurringSeries / Update, and
`VisitClosureInput.EndUtc`. So a 15-min retard noted during the séance pushes « Séance passée », « À clôturer » and
the popup back 15 min — **once**, and the popup then opens on the fiche step. Noted after the slot (« Séance
passée » already), it moves nothing: the review is already due and a second popup is the nag the client reported.

### Who may carry one, and what erases it
`LateArrivalStatuses` = En cours · Séance passée · Terminé (the patient is, or was, there). `Cancel`, `MarkAsNoShow`,
`Reactivate` and `Reschedule` erase it, so the invariant « a retard ⇒ a statut that admits one » holds after every
move (`No_Move_Leaves_A_Retard_On_A_Status_That_Cannot_Carry_One` runs every statut × every mutator).
⚠️ `Reschedule` compares the start **to the minute**: a form rebuilds the instant without seconds, and re-saving a
visit unchanged must not erase its retard.

### The handler applies it LAST
After the date move and the statut switch, so `{ status: "Completed", lateArrivalMinutes: 15 }` from « Séance
passée » lands in one write, and `{ status: "NoShow", lateArrivalMinutes: 15 }` is refused instead of landing and
being erased. The field is tri-state: omitted = unchanged (every existing caller), `null` = cleared.

### A recorded retard answers « venu ? » — without writing a statut
The popup's « Venu » writes nothing (the fiche marks the visit done), so a retard doesn't either; the popup simply
treats `lateArrivalMinutes != null` as « venu » and opens on the fiche step. « À clôturer »'s « Venu » writes
`Completed`, so its « Venu en retard » does too. Each surface keeps the presence semantics it already had.

### Versions: the chips send none, « Reporter » does
A retard is a one-field fact, like the popup's « Absent »; a 409 on a one-tap chip would be noise. « Reporter »
moves a booking a colleague may be moving too, so it sends the version and goes through `useConflict`.

### « Reporter » is the ordinary reschedule
Statut back to « Planifié », the review follows the new slot, the rappel follows. It is **never** counted as an
absence. A taken slot or a closed hour is the server's named refusal (`slot_taken` / `outside_working_hours`) with
« Reporter quand même »; the overrides are **merged** across confirmations, as in the create dialog.
« Mettre en liste d'attente » adds the entry **first**, then cancels with the motif « Reporté par le patient » —
the safe half lands first, never a cancelled RDV with nobody on the list. A ref holds the created entry, so a retry
after a failed cancel never adds a second one.

### The free times are the save's own rules, computed on the server
`GET /appointments/{id}/free-slots` (`FreeSlotFinder`) lists, per clinic day, the starts the save would accept
**without** an override: the doctor's hours or the clinic's (`WorkingHoursResolver` — doctor hours replace, never
intersect), the break, the congé (`LocalDaysOf` + `DoctorLeave.Covers`, the booking guard's own day rule), every
visit holding a slot (`OccupiesSlot`, read clinic-wide so a doctor-less « créneau occupé » blocks everyone), the
visit's own length, the visit itself excluded. Quarter-hour starts; today only from now on.
⚠️ **Never re-derive it in the browser.** The client copy this replaced (`nextOpenDays` / `isOpenOn`) read the
clinic's hours only — it offered a doctor's day off and a congé, and the save then refused them.
⚠️ `FreeSlotFinderTests`' agreement test is the guard: every quarter hour of a fixture, « listed » ⇔ the save's
collision check is null **and** its hours check passes.
⚠️ Read on open and on a window change only, never on a realtime broadcast: the minute job alone would reshuffle the
grid under the finger, and the save re-checks anyway (a time taken in between is the ordinary named refusal).
⚠️ **So the list can go stale, and two things cover it.** A listed time refused by the save (taken or closed since the
read) re-reads the list. And « Reporter » re-checks the clock **at the press**, not at the last render: the server
accepts a past time, and a time picked at 11:59 and pressed at 12:01 would otherwise have moved the visit into the past.
⚠️ A failed read is a failure (`LoadFailureNotice` + « Réessayer », typed time shown directly), never an empty day.
⚠️ « Congé » and « Complet » chips stay pickable: they say why, and « Autre heure… » keeps the override path — a
disabled chip would state nothing (§ 0).

### « Absent » can become « Terminé »
The very late patient who turns up after being marked absent. Regaining the slot (from Absent or Annulé to any
slot-holding statut) now runs the **named** collision check with its override — reactivating a cancelled RDV into
a slot given away used to reach the database's bare 409. No working-hours re-check there: the time did not move.
⚠️ The devis links `PlanBookingRelease` cleared at « Absent » are **not** restored: the fiche links the act by hand.
⚠️ A fiche alone still does not overturn « Absent » (`MarkVisitCompleted` answers `Contradicted`): a person's
answer is only changed by a person.

### Small decisions
- The popup's link reads « Noter un retard », not « Signaler » — « Signaler l'arrivée » already exists in the agenda ⋯
  (staff chat) and means « tell the dentist ».
- « Reporter » is not offered on « Terminé » (that is a date correction, in the edit dialog), « Absent » (it keeps its
  absence) or « Annulé » (it reactivates).
- The tablet toast's buttons grow (`h-11`) rather than take `.touch-target`: three siblings side by side.
- sonner 1.7.4 does not call `onDismiss` on a programmatic `toast.dismiss`, so « Reporter » on a tablet does not
  snooze; closing the dialog without an answer brings the toast back.
- The form shows no « Créneau libre » line: the overlap hook reports nothing both for a free slot and a failed read.
- What a press answered (refusal, « déjà passée », the 409 banner) sits in a pinned strip just above the buttons: at
  the end of the body it fell below the time grid, off screen at 730 px.
- The button reads « Reporter », not « Reporter à 10:30 »: the popup's footer (398 px) could not hold « Retour » +
  « Mettre en liste d'attente » + a time in the label, and « Retour » was clipped. The footer wraps from `md:`.

## Guards
- `AppointmentLateArrivalTests` — the statuts, the 1–240 range, every eraser, `ExpectedEnd`, the invariant, and the
  browser mirror of `LATE_ARRIVAL_STATUSES` (both directions, red-proved).
- `UpdateAppointmentLateArrivalTests` — tri-state, one-write « Venu en retard », the refusals, the review timing
  both ways, « Absent → Terminé » free / taken / forced, reactivation into a taken slot, « À clôturer » held until
  the séance really ended, and a source scan that every `AppointmentDto` projection carries the field.
- `FreeSlotFinderTests` — hours replace / break / back-to-back / doctor-less block / cancelled-absent-retired free /
  the moved visit / congé on the Tunis day / closed, full, unrestricted / today from now on, plus the **agreement
  test** against `FindCollisionAsync` + `CheckWorkingHoursAsync` (red-proved), and `GetFreeSlotsQuery`'s clinic,
  doctor, length and clamp.
- Not reachable by a unit test: the job's two residuals are in-memory filters after SQL in `AppointmentRepository`
  (no database here). The browser pass covers them.
