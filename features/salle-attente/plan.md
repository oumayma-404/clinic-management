# Salle d'attente — the plan to feed `/define-small-feature`

> **How to use.** Six slices, run **in order**, one `/define-small-feature` each:
> `/define-small-feature Salle d'attente — slice S1, see features/salle-attente/plan.md § S1`.
> Each slice below is already written in that skill's spec shape, so the skill's job is to **verify the claims
> against the code**, ask only what is still open (expected: nothing — see § 2), and write
> `features/salle-attente-s<N>-<slug>/spec.md`. Then `/implement-small-feature` → `/test-small-feature` →
> `/review-feature` → `/test-in-browser`, per slice.
>
> Look and flow (agreed 2026-11-25): https://claude.ai/artifact/Ax7Wf6kyq1tmZ4pfKHcMTn ·
> Technical blueprint: [`blueprint.md`](blueprint.md) · Written against `origin/main` 031f5314.
>
> ⚠️ Several slices sit at the top edge of « small » (≈ 10–20 files). If the skill proposes `/define-feature`,
> answer « stay small, the design is in plan.md » — splitting further would ship half-states to real clinics.

## 1. What we are building — in one paragraph

A patient who walks into the cabinet (with or without a RDV) is marked « Arrivé » in **one gesture**; the app
counts his wait; the doctor sees who is waiting and presses **« Patient suivant »**, which rings at reception with
the name; the visit ends on its own (the next « Patient suivant », « Terminer la séance », or the encaissement).
Reception works on a **4-lane board — À venir → En salle d'attente → Au fauteuil → Terminé** — with buttons
and drag-and-drop, and **every action can be undone**. A cabinet that never presses « Arrivé » sees no change.

## 2. Decisions already taken — the skill must not re-ask these

| # | Question | Decision |
|---|---|---|
| D1 | Where does the state live? | On the **appointment**: `ArrivedAtUtc`, `CalledInAtUtc`, `LeftChairAtUtc`, `IsWalkIn`. No new status, no new table. |
| D2 | A patient with no RDV? | Becomes a **real appointment** at his arrival time (default duration 30 min, dentist = the only one, or picked when 2+). |
| D3 | Queue order | `max(RDV time, arrival)` for a booked patient, `arrival` for a walk-in; ties by arrival. **Nobody goes before his RDV time; a late patient loses his place.** The doctor can still « Appeler » anyone. Computed **server-side only**. |
| D4 | Wait colours | Neutral < 20 min · orange 20–39 · red ≥ 40. One constant pair. |
| D5 | « Parti sans être vu » | The appointment is cancelled; its card goes to « Terminé » in red (arrived + cancelled = left). Never disappears. |
| D6 | Reversibility | Every action: an **« Annuler » toast for 8 s** + the card can be moved to **any** lane later (drag or ⋯ menu). Only refusal: a walk-in to « À venir ». A reversal undoes only what the waiting room wrote and never touches a fiche or a note. |
| D7 | What ends a visit | « Patient suivant » (ends the one in the chair), « Terminer la séance » in the fiche, reception's « Terminer », the encaissement (S6). |
| D8 | « Arrivé » and the chat | « Arrivé » **replaces** « Signaler l'arrivée » everywhere and still posts the same arrival notice to the dentist (today's behaviour kept). |
| D9 | Doctor → reception signal | A **built-in** quick message `call-in` (renameable, not removable) in the dentist's desk thread: chime, banner **with the name** that stays until « OK », Windows/phone notification **without the name** (lock screens learn nothing). |
| D10 | Secretary's home | `/salle-attente` (today she lands on the agenda). Agenda one tap away. |
| D11 | Agenda | **Unchanged**, except « Signaler l'arrivée » now reads « Arrivé » (owner's 11-19 rule: no new pill or colour on the agenda). |
| D12 | Two dentists (cabinet de groupe) | One chair per dentist; each dentist's « Patient suivant » takes **his** queue + unassigned patients (and assigns them to himself); the walk-in asks which dentist only when 2+ are active. |
| D13 | Days | Everything is **today** (`ClinicClock`). Yesterday's leftovers (still waiting / still in chair) are resolved by the existing job the next day, with no invented time. |
| D14 | Naming | The slot waitlist (`/waiting-list`) is « **Liste d'attente** » everywhere; « Salle d'attente » belongs to this feature only. |

## 3. Rules every slice obeys (the review checks these)

- **Inert by default**: every behaviour change is keyed on `ArrivedAtUtc != null`. A test per slice proves an
  appointment that never arrived behaves exactly as on `main` (job, agenda, « À clôturer »).
- **No new `AppointmentStatus`**, no new edge in `AllowedTransitions` (the status control would offer it). Reversals
  are domain methods guarded by « written by the waiting room, today ».
- **One owner per rule**: `WaitingRoomOrder` (queue), `WaitingRoomLane` (which lane a visit is in), `WaitingRoomExit`
  (what ends the chair). The browser never re-sorts or re-derives a lane.
- **`AppointmentDto` is filled in four places** (`CreateAppointmentCommand:282`, `UpdateAppointmentCommand:710`,
  `GetAppointmentQuery:76`, `GetAppointmentsQuery:98`) — S1 adds a test that all four carry the new fields.
- **The job writes appointments every minute**: waiting-room commands take **no user `Version`**; they guard on state
  (« is he still waiting? ») + an idempotency key (N51). Catch-alls carry `when (ex is not ConflictException)`.
- **Migration**: delete the scaffolded `AddColumn<uint>("xmin")` lines; no `HasDefaultValue` on `IsWalkIn`
  (the archive skips store-generated columns); no backfill. Run `verify-schema` before/after.
- **Commands live under `Features/Appointments`** → existing realtime key `appointments`; no `clinic-hub.ts` change.
- **Frontend contract** (`.claude/rules/frontend-web.md`, `~/.claude/skills/DEVICE-CONTRACT.md`): 320 px, 44 px on a
  coarse pointer, every drag has a button twin, a toast-with-action never a modal for interruptions, **no helper
  text under controls** (owner rule), French labels from the mock-up verbatim.
- **Time**: server `ClinicClock.TodayRangeUtc`; client `todayLocalIso()`; the wait timer counts from the read's
  `serverNowUtc`, never from the device clock alone (cabinet PCs drift).
- **Gate per slice**: full unit suite (never `--filter`), `check:responsive` + `tsc --noEmit` + `npm run build`,
  `node e2e/scripts/check-coverage.mjs`, then `/test-in-browser` with a secretary and a doctor session side by side.
- **Branch from `origin/main`** in a worktree (the Desktop checkout is a different branch). Never push or merge
  without the owner's OK.

## 4. The slices

### S1 — L'arrivée est enregistrée
**Scope:** Full (mostly BE) · **Depends on:** nothing

**Overview.** « Arrivé » stops being only a chat message: it records the arrival time on the appointment, can be
undone, and the minute job stops guessing « En cours » for a patient who is sitting in the salle d'attente.

**What changes**
- `Appointment` gains `ArrivedAtUtc`, `CalledInAtUtc`, `LeftChairAtUtc` (nullable), `IsWalkIn` (bool) — one
  migration for the whole feature.
- « Signaler l'arrivée » (agenda block bell + ⋯ menu + chat) is renamed « Arrivé »; it stamps `ArrivedAtUtc` **and**
  posts the existing notice (D8). « Signaler à nouveau » re-sends the notice only.
- Marking arrived a visit the **job** had moved to « En cours » / « Séance passée » puts it back to « Prévu » and
  clears `LateArrivalMinutes` (the arrival contradicts the clock's guess).
- `UndoArrival` clears the three times (booked patients only).
- Job: start pass skips arrived visits; elapse pass skips waiting / in-chair visits while their day is today.
- `GetTodayArrivalsQuery` reads the column instead of chat messages.
- Rename the slot waitlist to « Liste d'attente » (D14): `AuditLabels.cs:51`, `WaitingListController.cs:14`, the
  dashboard chip, two stale « walk-in » comments.

**Acceptance criteria**
- **S1-AC1:** « Arrivé » on today's RDV sets its arrival time and the dentist receives the same notice as before.
- **S1-AC2:** A second « Arrivé » is refused with a named code; « Signaler à nouveau » re-notifies without moving the time.
- **S1-AC3:** Arriving a visit the job put « En cours » returns it to « Prévu »; its « Retard » chip is cleared.
- **S1-AC4:** The job never starts a visit whose patient has arrived and not been called, and never moves a waiting
  or in-chair visit to « Séance passée » the same day; the next day it resolves them by today's rules.
- **S1-AC5:** An appointment that never arrived behaves exactly as on `main` (job, agenda, « À clôturer »).
- **S1-AC6:** `AppointmentDto` carries the four new fields from all four fill sites (derived test).
- **S1-AC7:** Arrival on a cancelled / no-show / other-day appointment is refused with a named code.
- **S1-AC8:** No screen says « Salle d'attente » for the slot waitlist.

**Device:** desk-led; the only UI change is a label — at 320 px the agenda's ⋯ menu shows « Arrivé » in full.
**Out of scope:** the board (S2), calling in (S3/S5).
**Edge cases:** archive / PC de secours carry the columns automatically — one restore round-trip test.

### S2 — Le tableau de l'accueil
**Scope:** Full (mostly FE) · **Depends on:** S1

**Overview.** Reception gets its screen: one search box for every arrival, and the 4-lane board. In this slice
the two left lanes are live; « Au fauteuil » and « Terminé » are **read-only** reflections (they fill from the fiche's
« Terminer la séance » and the job) until S3 adds the moves.

**What changes**
- `GET /appointments/waiting-room?doctorId=` → `{ serverNowUtc, upcoming, waiting (ordered), inChair[], finished[] }`;
  lanes decided by `WaitingRoomLane`, order by `WaitingRoomOrder` (D3).
- `POST /appointments/walk-in` (`RegisterWalkIn`): existing patient **or** first+last name (phone optional, same
  rules and duplicate check as the booking dialog's « Nouveau patient ») → appointment now, arrived, `IsWalkIn`.
- Page `/salle-attente`: header figures (En attente · Attente moyenne · Vus · À venir); **one search box** — typing
  shows today's RDV first (« Arrivé »), then patients (« Sans RDV »), then « Nouveau patient « … » »; Enter takes
  the first; lanes as in the mock-up; « À venir » cards carry « Arrivé »; waiting cards carry position, RDV/Sans RDV,
  « Suivant » on the first, the live wait badge (D4); a RDV whose time has passed shows « en retard ».
- « Annuler » toast (8 s) after every action of this slice.
- Nav: « Salle d'attente » first under Quotidien and in the bottom bar, with the waiting count; secretary home →
  `/salle-attente` (D10).
- With 2+ active dentists: the walk-in asks which dentist (chips, one tap); the board gets a dentist filter.

**Acceptance criteria**
- **S2-AC1:** Typing 2 letters of a booked patient's name and pressing Enter marks him arrived (1 gesture).
- **S2-AC2:** Typing an unknown name offers « Nouveau patient « … » »; choosing it creates the patient and a walk-in
  appointment at the current time, visible in the agenda.
- **S2-AC3:** The waiting lane is in D3 order, the first card says « Suivant », and each badge counts from the arrival
  and turns orange at 20 min, red at 40 (from `serverNowUtc`).
- **S2-AC4:** « Annuler » within 8 s restores the previous state exactly (arrival and walk-in both).
- **S2-AC5:** A change made on another device appears within one realtime refresh, without reload.
- **S2-AC6:** A secretary opening the app lands on « Salle d'attente »; a doctor's home is unchanged.
- **S2-AC7:** At 320 px the lanes stack in one column, every card action is reachable, the search box stays on top;
  at ≥ 900 px the four lanes sit side by side.
- **S2-AC8:** With 2+ dentists, a walk-in cannot be created without a dentist; with one, nothing is asked.

**Device:** leading = reception PC (Windows app); < 900 px two lanes per row; < 540 px one lane, cards full width;
coarse pointer: 44 px buttons.
**Out of scope:** moving into / out of the chair (S3), drag (S4), the doctor's view (S5).
**Edge cases:** a walk-in overlapping booked RDV is never refused for overlap or opening hours; an empty day shows
each lane's empty state, a failed read shows an error state (never an empty board).

### S3 — Faire entrer, terminer, et tout se corrige
**Scope:** Full · **Depends on:** S2

**Overview.** Reception can bring a patient in, end the visit, mark him gone — and undo any of it, at once or later.

**What changes**
- Commands: `CallIn(id)` (« Faire entrer »), `EndChair(id)` (« Terminer »), `LeaveWaitingRoom(id)` (« Parti sans être
  vu »), `ReturnToWaiting(id)`, `ReopenChair(id)`, `UndoArrival(id)` (from S1).
- One chair **per dentist** (D12): bringing a patient in ends the chair of **his** dentist only.
- `CallIn` moves the visit to « En cours »; `EndChair` to « Terminé » (status unchanged if a fiche already completed it).
- Every card has a ⋯ menu with its ways back: waiting → « Faire entrer · Annuler l'arrivée · Parti sans être vu »;
  chair → « Remettre en salle d'attente »; finished → « Remettre au fauteuil · Remettre en salle d'attente ».
- « Terminer la séance » in the fiche stamps `LeftChairAtUtc` when the visit is in the chair.
- « Annuler » toast after every action (as S2).

**Acceptance criteria**
- **S3-AC1:** « Faire entrer » moves the card to « Au fauteuil », the visit becomes « En cours », and whoever was in
  that dentist's chair moves to « Terminé ».
- **S3-AC2:** « Terminer » moves the card to « Terminé » with its chair time (« 10:03 – 10:25 · 22 min »).
- **S3-AC3:** « Parti sans être vu » cancels the visit and shows it in « Terminé » in red; it never leaves « À clôturer »
  asking a presence question.
- **S3-AC4:** Each move has its reverse, and the reverse returns status and times to exactly what they were (derived
  test over every lane pair).
- **S3-AC5:** A reversal never deletes or changes a fiche, a note or a payment; a visit completed by the job or
  cancelled from the agenda offers no « Remettre… » (the menu does not show it).
- **S3-AC6:** In a two-dentist cabinet, bringing in Dr A's patient leaves Dr B's chair untouched.
- **S3-AC7:** « Terminer la séance » on an in-chair visit moves its card to « Terminé ».

**Device:** as S2; the ⋯ menu is a menu on desk and a bottom sheet below 640 px.
**Out of scope:** drag (S4), the doctor's « Patient suivant » (S5).
**Edge cases:** two people acting on the same card at once — the second gets a named refusal and the board refreshes
(never a silent double move).

### S4 — Glisser-déposer
**Scope:** FE · **Depends on:** S3

**Overview.** The board moves like a Jira kanban: cards are dragged between lanes; each drop calls the S3 command
its button calls.

**What changes**
- Drag on any card except from a button; the card leaves a faded placeholder, a ghost follows the pointer, **allowed
  lanes are outlined** while dragging, the hovered lane highlights, the card animates into place on drop; an invalid
  drop snaps back.
- Touch: press-and-hold (~280 ms) starts the drag; a quick swipe still scrolls.
- Reuse the agenda's drag mechanism (`appointment-calendar.tsx`) if it fits before adding a library.

**Acceptance criteria**
- **S4-AC1:** Dragging a card to a lane performs exactly the move its button would, with the same « Annuler » toast.
- **S4-AC2:** While dragging, only the lanes the card may go to are outlined; dropping elsewhere changes nothing.
- **S4-AC3:** On a coarse pointer, scrolling the board never starts a drag; holding a card does.
- **S4-AC4:** Every drag has a button twin (no capability is drag-only); keyboard users reach every move through
  buttons and menus.
- **S4-AC5:** A board refresh arriving mid-drag does not drop the card or lose the gesture.

**Device:** mouse-led on the reception PC; touch via long-press; at < 540 px (one lane) drag still works vertically.
**Out of scope:** reordering inside a lane (the order is D3's, not manual).
**Edge cases:** dropping on the lane the card came from does nothing and shows no toast.

### S5 — « Patient suivant » chez le docteur
**Scope:** Full · **Depends on:** S3

**Overview.** The doctor calls the next patient from his phone, his chair PC or any screen of the app; reception is
told at once, with the name.

**What changes**
- `POST /appointments/call-next` `{ appointmentId?, expectedNextId?, idempotencyKey }` → ends his chair, calls the
  first of his queue (or the one named), assigns an unassigned patient to him, posts `call-in` to his desk thread.
  `POST /appointments/call-next/undo`.
- Built-in quick message `call-in` (D9): at reception a **banner with the name, a chime, until « OK »** — even with
  the chat muted; the OS notification (Windows / phone) says only « Le docteur appelle le patient suivant ».
- Doctor's « Salle d'attente » page, phone-first: « Au fauteuil » card with its live minutes; « En attente » list with
  wait badges and « Appeler »; « Plus tard aujourd'hui »; the **« Patient suivant » button at the bottom, in thumb
  reach**, naming the patient and his wait; « Terminer la séance » when nobody waits; « Annuler » toast.
- A « Patient suivant » pill in the app header for doctors, on every screen (the fiche included).
- The fiche's « Terminer la séance » toast offers « Patient suivant : X » when someone waits.
- The doctor's day board « Au fauteuil » reads the real times when the day has arrivals, the clock otherwise.

**Acceptance criteria**
- **S5-AC1:** « Patient suivant » ends the doctor's current chair, calls the first of **his** queue (D3), and within
  one realtime refresh reception shows the banner with the name and plays the chime.
- **S5-AC2:** A double tap, or two devices pressing at once, calls **one** patient (`expectedNextId` + idempotency).
- **S5-AC3:** « Annuler » within 8 s restores both visits and withdraws the banner.
- **S5-AC4:** The OS notification never contains a patient's name; the in-app banner always does.
- **S5-AC5:** The banner stays until « OK » and rings even when the chat sound is off.
- **S5-AC6:** With nobody waiting the button reads « Terminer la séance »; with nobody waiting and nobody in the chair it
  is disabled and says « Personne n'attend ».
- **S5-AC7:** At 390 px the « Patient suivant » button sits above the bottom bar without covering the last list row.
- **S5-AC8:** A secretary account sees no « Patient suivant » (she has « Faire entrer »).

**Device:** leading = doctor's phone (Android / iOS shell); also the chair PC; header pill on every screen ≥ 320 px.
**Out of scope:** a TV screen calling patients by name in the room (not asked).
**Edge cases:** a clinic that deleted or renamed `next-patient` is unaffected — `call-in` is separate and built-in.

### S6 — L'encaissement libère le fauteuil
**Scope:** BE · **Depends on:** S3, **and PR #117 merged** (it reshapes « Encaisser »)

**Overview.** When reception takes the money for a visit still « Au fauteuil », the visit ends — the payment proves
the patient has left the chair.

**What changes**
- `WaitingRoomExit.EndChairIfOpen(appointment, now)` — one owner, called after commit by every payment writer that
  resolves an appointment (directly, through its note, or through its fiche). **Enumerate them by grep first**
  (`regression-safety.md` § 2: a shared rule with N call sites).
- A derived test listing every payment command and asserting each one calls the owner or is declared exempt.

**Acceptance criteria**
- **S6-AC1:** Collecting on an in-chair visit moves its card to « Terminé » with the payment time as end.
- **S6-AC2:** Collecting on a visit not in the chair changes nothing on the board.
- **S6-AC3:** A failure to end the chair never fails or rolls back the payment.
- **S6-AC4:** The derived test fails on a new payment command that does neither.

**Device:** no UI change.
**Out of scope:** money rules of any kind.
**Edge cases:** a payment voided afterwards does not put the patient back in the chair (use « Remettre au fauteuil »).

## 5. Order, overlaps, and what each slice must re-test

| Slice | Overlaps | Re-test (regression sweep) |
|---|---|---|
| S1 | PR #116 (« À clôturer » dentist filter) — none in the same lines | agenda status control, « À clôturer », progress job, chat arrival, CSV export of appointments |
| S2 | PR #116 (nav, dentist chips) | agenda, `/waiting-list`, secretary + doctor homes, bottom bar at 320 px |
| S3 | PR #117 (« Terminer la séance ») | fiche save + « Terminer la séance », « À clôturer », visit closure job |
| S4 | — | the board's buttons (S2/S3) still work unchanged |
| S5 | PR #117 (fiche toast), staff chat | staff chat sound / mute / push, notification bell, doctor day board |
| S6 | **needs PR #117 merged** | la caisse, « À encaisser », every payment flow, `reconcile-money` |

Recommended: merge PR #116 and #117 before S3. S1–S2 can start now.

## 6. What could go wrong — and the slice that prevents it

| Risk | Where it is stopped |
|---|---|
| A clinic not using the feature sees its RDV behave differently | Rule « inert by default » + S1-AC5 |
| Two screens disagree on who is next | D3 server-only owner + S2-AC3 |
| A double tap calls two patients | S5-AC2 |
| A mis-click ends a visit with no way back | D6, S2-AC4, S3-AC4, S5-AC3 |
| A reversal undoes money or a fiche | S3-AC5 |
| A patient's name on a locked phone | S5-AC4 |
| The job writes « Séance passée » on someone still waiting | S1-AC4 |
| A field forgotten in one of four DTO fill sites | S1-AC6 |
| Two dentists sharing one « Au fauteuil » | D12, S3-AC6 |
| The device clock makes the timer lie | `serverNowUtc` rule, S2-AC3 |
| A prod migration surprise | one migration in S1, `verify-schema` before/after, `/check-prod-integrity` at deploy |

## 7. Done means

All six slices merged; one QA walk per slice GREEN with a secretary and a doctor side by side at
320 / 390 / 820 / 1180 / 1440 px; e2e rows `WAIT-01…08` (blueprint § Test strategy) tier 0 and covered; the owner
has tried the real build at a cabinet — the mock-up was the promise.
