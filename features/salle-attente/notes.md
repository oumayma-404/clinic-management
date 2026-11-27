# Salle d'attente — what shipped

Branch `feature/salle-attente`. Plan: [`plan.md`](plan.md) · blueprint: [`blueprint.md`](blueprint.md) · QA:
[`qa/run-2.md`](qa/run-2.md) (GREEN, 38 rows, 320 → 1536 px) · e2e: HP-21 `WAIT-01…08`
(`e2e/specs/waiting-room.spec.ts`).

## The shape

| Question | Answer |
|---|---|
| Where is the state? | On the **appointment**: `ArrivedAtUtc` · `CalledInAtUtc` · `LeftChairAtUtc` · `IsWalkIn`. No table, no new status. |
| Who says which lane? | `Appointment.WaitingRoomLane`, derived — `Upcoming · Waiting · InChair · Done · Left · None`. Never stored. |
| Who orders the queue? | `Domain/Services/WaitingRoomOrder` — ready time = walk-in ? arrival : max(slot, arrival). The browser never re-sorts. |
| One chair per…? | Per **dentist**: `DoctorId`, else the cabinet's only dentist. A cabinet de groupe's unassigned RDV joins every queue; the first dentist to call him takes him. |
| Who may call next? | A dentist's own account, an admin linked to a practitioner, or anyone non-secretary in a one-dentist cabinet (`WaitingRoomCaller`). |
| Screens | `/salle-attente` (4 lanes + « Qui arrive ? » + the dentist's panel) · the header's « Patient suivant » pill · the desk's urgent notice + call banner. |

## Decisions easy to undo by accident

- **Every move is reversible, so nothing asks « êtes-vous sûr ? ».** Each action answers a
  `WaitingRoomChangeDto` whose `before` snapshots are what « Annuler » posts back — the browser never composes an
  undo. A walk-in the undo removes is **disregarded**, never deleted, and only while still waiting.
- **A walk-in is a real RDV booked at the minute he arrived, `BookedWithOverlap`.** That is what lets it sit beside a
  booked slot without tripping `EX_Appointments_NoDoubleBooking`, and what puts it on the agenda for free. It cannot
  go back to « À venir » (`waiting-room-walk-in-has-no-slot`): it never had a slot.
- **« Sans RDV » for a patient who HAS an RDV today marks that RDV arrived instead** (`UsedTodaysAppointment`). Two
  visits for one person in one day is the defect this avoids.
- **The progress job leaves an arrived patient alone.** Its « En cours » pass skips `ArrivedAtUtc != null`, and its
  « Séance passée » pass skips a visit still waiting or in the chair today. Without that, the job wrote « En cours »
  on someone sitting in the waiting room.
- **Three things end the chair, and the payment is an interceptor.** « Terminer » / the next « Patient suivant » ·
  a fiche saved with « Terminer la séance » (`FicheVisitCompletion`) · **any** payment on the visit's note or devis
  (`WaitingRoomPaymentInterceptor`). An interceptor, not a call in each payment command, because there are many
  payment writers and a missed one is silent. ⚠️ It must stay **first** in `AddInterceptors`, or the appointment it
  closes is missing from the audit rows of that save (`WaitingRoomCoverageTests`).
- **« Arrivé » also announces the arrival to the dentist** (the existing « Patient arrivé » chat message), and the
  agenda's « Signaler l'arrivée » also marks the arrival. One press, one fact. The label « Signaler l'arrivée » was
  **kept** on the agenda: `CHAT-02` targets it by name, and it says what it does there.
- **« Patient suivant » rings the desk URGENT** (`CallInKey = "call-in"`, `IsUrgent = true`): the notice stays until
  answered and rings even on a muted desk. In the app it names the patient and offers « Voir la salle d'attente »; the
  OS / phone notification keeps the label alone (AC-4.1 — never a name on a lock screen). Not rung when the dentist's
  desk thread has no secretary.
- **A double press never calls two patients.** The button sends the patient it was showing (`expectedNextId`); a
  second press for someone already called is a **409**, refreshed quietly, never « the one after ».
- **A walk-in's chat link says « Sans RDV »**, not « RDV 20:40 » — its start is the arrival minute
  (`StaffMessageAppointmentDto.WalkIn`). Found by QA run 1.
- **The waits count from the server's clock** (`serverNowUtc` offset): a cabinet PC's clock drifts.
- **One shared read** (`queryKeys.waitingRoom`) feeds the page and the header pill, refreshed by `appointments`,
  `patients` (a fiche), `invoices` and `treatmentplans` (a payment) — each ends a chair under its own command's area.
- **« Salle d'attente » was the slot waitlist's old name.** That screen is « Liste d'attente » everywhere now (rail,
  journal, deletion blockers, dashboard chip); « Salle d'attente » means this board only.
- **Reception lands here**: a secretary's `/` redirects to `/salle-attente` (was `/appointments`), and the phone bar's
  first tab is « Attente » (was « Accueil »).
- **Drag is pointer events, long-press on touch.** HTML5 drag-and-drop does not exist on a finger. Every drag has a
  button or menu twin — no move is gesture-only.

## Round 2 — a real clinic day (2026-11-26)

Review: https://claude.ai/artifact/LtCbGxRafwfaHdZxmQuSPq · plan + blast radius: [`round-2.md`](round-2.md) · QA:
[`qa/r2-run-4.md`](qa/r2-run-4.md) (GREEN, 53 rows, + e2e HP-21 7/7 on the round-2 build) · migration `20261126151948_AddWaitingRoomRound2` (5 nullable appointment
columns + `Doctors.WorksTwoChairs`, no default — same reason as `IsWalkIn`).

- **The RDV takes the real chair time** (owner, Q1): a visit ended with a known call AND departure moves to
  call → leave; the booking is kept in `PlannedStartUtc`/`PlannedDuration` (« prévu 10:00 ») and anything that
  re-opens the visit puts it back. `Appointment.SettleChairTime` is the one owner, called by every status write.
  ⚠️ The real interval is `BookedWithOverlap`: a late séance overlaps the next booked RDV and the exclusion
  constraint would otherwise 409 « Patient suivant ». A user's agenda move or resize makes the new time the plan.
- **A move never invents a time** (C8): « Faire entrer » from « À venir » records no arrival, a drag to « Terminé »
  from outside the chair records no call and no departure. Such visits stay out of the average and keep their booking.
- **« Parti sans être vu » is `LeftUnseenAtUtc`, not a cancellation** (C3): an agenda cancel leaves the board;
  « Absent » on a patient who was waiting reads « Parti ». Never branch on `CancellationReason` (French prose).
- **Another day clears presence** (C2) inside `Reschedule`, so every caller is covered.
- **One live card per patient** (C4), **« Quel praticien ? » before seating an unassigned patient** in a cabinet
  de groupe (C7 — `WaitingRoomSeating.SeatAsync` is the one seat rule for « Faire entrer », « Patient suivant » and
  « Pas là »'s next call).
- **« Annuler » compares before it restores** (C9): every change carries `After`; a card that moved since refuses
  with `waiting-room-moved-since` (409). `WaitingRoomState.SameAs` compares to the millisecond and on the LANE, not
  the status — PostgreSQL keeps microseconds and the job may write « En cours » on a card nobody moved.
- **« 2 fauteuils »** (owner, Q3) on `Doctor`: « Patient suivant » ends nobody, a third patient is refused
  (`waiting-room-chairs-full`); set by the dentist on his panel (an admin may set anyone's).
- **« Attente moyenne »** (owner, Q2) is the server's `AverageWaitMinutes`: arrival → called in, today's called-in.
- **Extras** (owner: all): « Pas là » (E1 — the banner, the card menu, the dentist's chair, AND the staff-chat
  call-in notice, which is where the desk answers and which sits on top of the banner), « Passer en premier » (E2,
  `UrgentAtUtc`, first in `WaitingRoomOrder`), agenda « en salle » / « au fauteuil » + live overrun + dashboard from
  the board (E3/E4 — the lane packer uses the live length too), an early patient's colour counts from his RDV (E5).
- **« Le recevoir aujourd'hui »** (C6) moves the other-day RDV to now (`RegisterWalkInCommand.AppointmentId`); its
  « Annuler » puts it back and the reminders/notices follow both ways.
- **« Dr X absent aujourd'hui »** (C10) on the card with « Changer de praticien »; congé read by the board only —
  never a rule a move obeys.

## Not done (owner's call later)

- The dashboard's « Au fauteuil » is cabinet-wide: a dentist may see a colleague's seated patient.
- The fiche's « Terminer la séance » does not offer « Patient suivant » in its toast — the header pill covers it.
- Touch long-press drag and the 20 / 40 min colours were not exercised in a browser (QA C3, C4).
- Phone push of the ring was not checked on a device.
