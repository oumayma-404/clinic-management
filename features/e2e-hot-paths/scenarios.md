# Hot-path end-to-end scenarios

**Status:** EXERCISED — the 2026-09-08 pass ran ~95 of these as `e2e/`; see [`findings.md`](findings.md)
**Created:** 2026-09-08
**Companions:** [`coupling-matrix.md`](coupling-matrix.md) — read it first; every *Expected* below is a row in
it. [`findings.md`](findings.md) — what the pass found, including **four rows below that were wrong** and are
corrected in place.

> ⚠️ **Where a row disagrees with the code, the code won.** Four expectations here were written from
> `CLAUDE.md` or from the feature notes and measured wrong: they are struck through and corrected inline,
> each with the reason. A scenario list that keeps its original guess is how a suite grows tests that assert
> the opposite of the product.

Every scenario is written to be executed **twice**: once by hand in a browser (§ « the pass »), then once as
code in the deploy gate. So each one names its route, its trigger and its assertions in a form a Playwright
step can consume without re-deriving anything.

---

## How to read a row

| Column | Meaning |
|---|---|
| **T** | Tier. **0** = money or clinical fact is wrong and nothing says so. **1** = a capability is unreachable or a figure is wrong on one surface. **2** = edge, polish, or a refusal's wording. |
| **Pre** | The state that must exist first. `∅` = any patient will do. |
| **Do** | The gesture. |
| **Expect** | What must be true — **on every coupled surface**, not just the one touched. |
| **Defends** | The named rule or the recorded defect this exists to catch. |

**Mutation discipline** (`.claude/rules/verification.md` § 7): money and clinical records are written in this
pass **because the money paths are what is under test** — but only on the dedicated fixture patients below.
No existing patient's ledger is touched.

---

## § Layer — the rows a wire test cannot hold

> **A scenario is tested where its rule lives.** A wire test posts a body *the test author wrote*, so it proves
> the handler is right about that body and **nothing** about the body the product sends.

Measured 2026-09-11: of the 119 tests in `e2e/`, **21 open a browser**. And every one of the five `fix(...)`
commits landed between the 2026-09-08 pass and 2026-09-11 was a defect no wire test could see. The sharpest is
`24f2883e` — « le total d'un acte modifié depuis le rendez-vous part enfin au serveur »: a price the dentist
typed **never reached the server**. That is money, it is `BOOK-48`, and a wire test of `BOOK-48` would have
passed for every day it was broken, because the wire test sends the price itself.

The criterion for this list is one question: **would a wire test of this row pass while the product was
broken?** If yes, the row is browser-layer. `e2e/scripts/check-coverage.mjs` fails when one of these has a
test and none of its tests drives a page. Rows *not* listed are held perfectly well on the wire — the server
is their authority and the client only displays what it is given.

<!-- LAYER:BROWSER:BEGIN -->

**1 · The value the dentist typed must reach the server.** The client composes the body; the server never sees
what was on screen.
`CAT-08` `CAT-13` · `BOOK-23` `BOOK-24` `BOOK-25` `BOOK-26` `BOOK-27` `BOOK-28` `BOOK-29` `BOOK-41` `BOOK-48` ·
`EDIT-03` `EDIT-04` · `STEP-01` `STEP-08` · `FICHE-06` `FICHE-07` `FICHE-15` `FICHE-18` `FICHE-19` ·
`FEDIT-11` `FEDIT-14` `FEDIT-15`

**2 · A control the client withholds, derives or arms.** Its absence is the assertion, and absence is
invisible on the wire.
`CAT-02` `CAT-14` · `BOOK-30` `BOOK-31` `BOOK-32` `BOOK-33` `BOOK-37` `BOOK-38` `BOOK-43` `BOOK-47` ·
`FICHE-05` `FICHE-12` `FICHE-13` `FICHE-16` `FICHE-17` `FICHE-20` `FICHE-22` `FICHE-23` `FICHE-24` `FICHE-25`
`FICHE-28` · `FEDIT-12` `FEDIT-13` · `PLAN-02` `PLAN-03` · `CONT-08` `CONT-09` · `DONE-04`

**3 · A client-side guard against a duplicate, or a refusal that re-enters the save.** Each advisory prompt
re-runs `performCreate` **from the top**; the refs that stop it duplicating live only in the dialog.
`BOOK-05` `BOOK-06` `BOOK-07` `BOOK-10` `BOOK-11` `BOOK-12` `BOOK-13` `BOOK-14` `BOOK-15` `BOOK-16` `BOOK-17`
`BOOK-18` `BOOK-19` `BOOK-20` `BOOK-21` `BOOK-22` `BOOK-35` `BOOK-36` `BOOK-49` `BOOK-50` ·
`EDIT-06` `EDIT-07` `EDIT-09` `EDIT-10` · `FEDIT-18`

**4 · The client resolves which plan or step a gesture attaches to.** `planIdByItem`, `schedulablePlanItems`,
`resolveAttachedPlanId` — and the refusal a wrong resolution produces is the *same sentence* a correct one
produces from a missing field.
`BOOK-39` `BOOK-40` `BOOK-45` `BOOK-46` · `CONT-10` `CONT-11` `CONT-12`

**5 · A rendered read — a figure, a badge, a ring, a sentence.** The DTO can be perfect and the screen still
wrong; `PLAN-01` is the proof (`tsc`, `check:responsive` and `next build` were all green while **every**
`Completed` plan's workspace threw during render).
`PLAN-01` `PLAN-18` `PLAN-19` `PLAN-26` `PLAN-29` `PLAN-30` · `HIST-02` `HIST-03` `HIST-04` `HIST-08` ·
`ODO-03` `ODO-04` `ODO-05` `ODO-06` `ODO-07` · `MONEY-05` `MONEY-09` · `DONE-02` · `STEP-16` ·
`XCUT-05` `XCUT-08` `XCUT-09`

**6 · The client is the only guard there is.** No server fallback exists; the row is a claim about the prefill.
`CAT-03` `CAT-04` · `FICHE-33` `FICHE-34` `FICHE-40` `FICHE-43` `FICHE-44` · `PLAN-27` ·
`STEP-09` `STEP-10` `STEP-11` `STEP-12` `STEP-13`

<!-- LAYER:BROWSER:END -->

⚠️ **The markers are load-bearing** — `check-coverage.mjs` reads the block between them, so a row moved outside
silently stops being enforced.

### Fixtures the pass creates

**One patient per test**, named `E2E <label>-<base36 stamp>` (`e2e/lib/fixtures.ts`), so no two tests ever
share a ledger — which matters because half of what is asserted here is a *clinic-wide* total.

They are **not deleted by the run**: a patient carrying money cannot be deleted at all
(`PatientDeletionBlockers`), so a cleanup step would fail on exactly the scenarios that mattered, and a run
that tidies up destroys the evidence a red result needs. On a **shared developer database** they are removed
afterwards with [`e2e/cleanup.sql`](../../e2e/cleanup.sql) — a full pass creates ~65 patients with their
invoices, payments and plans, and those land in the same caisse and « Créances » the developer reads all day.
CI bootstraps an empty database per run and throws it away, so it never needs this.

### Acts used — **found by shape, never named**

⚠️ **This is the correction that cost the most.** The first draft named acts and hard-coded their tarifs, from
the developer's catalogue. Seven tests then failed against a **freshly seeded** one — « Détartrage » was not
60, « Couronne / bridge » had no step called « Empreinte », and « Gingivectomie » *did* carry a 7-day interval.
None of that is a product defect and all of it reads as one.

So a scenario asks the catalogue for a *shape* (`e2e/lib/api.ts`):

| Helper | Asks for |
|---|---|
| `protocolAct(n)` | an act with at least `n` catalogue steps |
| `singleSeanceAct()` | an act with **no** protocol and a non-zero tarif |
| `procedureWhere(what, pred)` | anything else — « carries an interval on step 1 », « stores no interval at all » — failing loudly, naming what it searched for |

A scenario whose shape no act on the database satisfies is reported as **not exercised, with the reason**
(§ 1's third outcome) rather than passing.

For the record, the dev catalogue as measured on 2026-09-08 — 16 of 37 acts carry a protocol:

| Act | Tarif | Protocol |
|---|---|---|
| Consultation / examen bucco-dentaire | 40,000 | `[]` — single séance |
| Détartrage | 60,000 | `[]` |
| Coiffage pulpaire | 30,000 | `[]` — the 30 DT act from the recorded bridge defect |
| Couronne / bridge (par élément) | 500,000 | Préparation · Empreinte · Scellement |
| Implant dentaire | 1 500,000 | Bilan · Pose · … (6 steps) |
| Couronne sur implant | 500,000 | Désenfouissement (**MinDays = 90 on step 1**) · Empreinte · … |
| Gingivectomie | 50,000 | steps with **no `MinDaysAfterPrevious` key** (legacy shape) — ⚠️ **but a freshly seeded catalogue gives it 7 days**, which is exactly why the suite no longer names it |

---

# HP-1 · Types de procédure — the catalogue every protocol comes from

Route `/procedure-types`. Upstream of everything: a protocol here is what makes an act split **by default**.

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| CAT-01 | 1 | ∅ | Create an act with a tarif and no steps | Saved `DefaultSteps = []`; booking it offers **no** split notice | baseline |
| CAT-02 | 1 | ∅ | Create an act, then add a 3-step protocol via « Étapes » | Booking it now shows « Traitement en 3 séances » **and the editor**, unprompted | `resolvePlannedProtocols` default |
| CAT-03 | 0 | CAT-02 | Reopen « Étapes », **rename step 2 only**, save | Steps 1 and 3 keep their `MinDaysAfterPrevious`; step 2 keeps **its own** | **the 4-argument copy** — a 3-arg copy erases the interval from *every* step |
| CAT-04 | 0 | « Couronne sur implant » | Open « Étapes », change nothing, save | The 90-day interval on **step 1** survives | same, and MinDays on the *first* step is the case a rank-based rule drops |
| CAT-05 | 1 | « Gingivectomie » (no MinDays key) | Open « Étapes », save unchanged | No interval invented; steps still have none | legacy JSON shape must round-trip |
| CAT-06 | 1 | CAT-02 | Delete a step so 3 → 2 | Catalogue says 2; **an already-booked** appointment's protocol is unchanged | the catalogue is a template, not a live link |
| CAT-07 | 1 | CAT-02 | Add a step so 3 → 4 | Same: existing bookings unchanged, new bookings get 4 | same |
| CAT-08 | 1 | ∅ | Set every step's duration; book the act split | The **booking's** duration = the *first* step's, not the sum of all | duration comes from the séance being booked |
| CAT-09 | 2 | ∅ | Save a protocol with a **blank** step label | Refused, in the dialog, before anything is written | `SetSteps` refuses a blank name |
| CAT-10 | 2 | ∅ | Set a step's `MinDaysAfterPrevious` to 0, and to a negative | 0 accepted (no wait); negative refused | — |
| CAT-11 | 1 | an act **in use** by a live devis | Deactivate it | Still resolves on existing bookings and fiches; absent from new pickers | `list(false)` = active only |
| CAT-12 | 1 | ∅ | Set `ResultingCondition` on an act, book + record it | The odontogramme charts that condition — **only when the act is `Done`** | `ToothChartingRules` |
| CAT-13 | 2 | ∅ | Set the act « per tooth »; record it on 3 teeth | Fiche total = unit × 3; the `/dent · forfait` switch is present | `derivePerTooth` |
| CAT-14 | 2 | ∅ | Kill the API, open `/procedure-types` and the booking dialog | Booking dialog shows the **inline explanation** next to the empty list — not a silent empty catalogue | AC-P3.31 |

---

# HP-2 · Créer un rendez-vous — every scenario

Route `/appointments` → `button[data-size="sm"]:has-text('Nouveau')`.
⚠️ A bare `:has-text('Nouveau')` is a strict-mode violation — a second hidden « Nouveau RDV » exists.

## 2a — The patient half

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| BOOK-01 | 1 | ∅ | Existing patient, one act, valid slot → save | Appointment on the grid; act row with its price; duration = act's | baseline |
| BOOK-02 | 1 | ∅ | « Nouveau patient »: first + last name only, no phone | Patient created, appointment created | the walk-in door: a name alone is enough |
| BOOK-03 | 1 | ∅ | « Nouveau patient » with a **malformed** phone | Refused with `PHONE_ERROR_FR` **before** the patient is created | a number given must be deliverable |
| BOOK-04 | 1 | ∅ | « Nouveau patient » with an international number (+33…) | Accepted; the country is shown | `international-phone-numbers` |
| BOOK-05 | 0 | a patient named « Ahmed Ben Ali » exists | « Nouveau patient » with the same name → the duplicate prompt → **confirm** | **Exactly one** new patient; exactly one appointment | `createdPatientIdRef` — reuse before create |
| BOOK-06 | 0 | BOOK-05 | Same, but the slot is **also** taken → confirm duplicate, then confirm overlap | Still **one** patient, **one** appointment | the same ref, across *two* re-entries of `performCreate` |
| BOOK-07 | 1 | BOOK-05 | Duplicate prompt → **cancel** | No patient created; the form is intact and re-submittable | a question, not a dead end |
| BOOK-08 | 1 | ∅ | Leave the patient unselected → save | « Sélectionnez un patient. » | sync validation |
| BOOK-09 | 2 | ∅ | Open the dialog from a patient's page (« Planifier un rendez-vous ») | That patient is preselected, not the new-patient form | — |
| BOOK-10 | 0 | ∅ | Book a « créneau occupé » (busy slot), then open the dialog again | The second opening offers a **patient** — the switch does not persist | `isBusySlot` was the field missing from the reset list |

## 2b — The slot half (three advisory refusals, each re-entering the save)

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| BOOK-11 | 1 | ∅ | Book in the **past** → the past-time confirm → confirm | Created; the form was never lost | AC-2 |
| BOOK-12 | 1 | ∅ | Book in the past → **cancel** | Nothing created; form intact; time editable | — |
| BOOK-13 | 2 | ∅ | Book the **current** minute | **No** past-time prompt (now is floored to the minute) | booking the current slot is not the past |
| BOOK-14 | 1 | a closed day for the doctor | Book on it → out-of-hours prompt | The prompt **names the closed period** the server objected to | `outsideHoursPrompt` holds the message, not a boolean |
| BOOK-15 | 1 | same-practitioner clash | Try to save | **Blocked** — mirrors the server guard | — |
| BOOK-16 | 1 | other-practitioner overlap | Try to save | Advisory amber hint; save allowed via « Continuer quand même » | — |
| BOOK-17 | 0 | a taken slot **and** a closed day | Confirm the overlap, then the hours | Created **once**; the overlap is **not** asked a second time | grants **merge**, never replace — else a loop |
| BOOK-18 | 1 | ∅ | End time before start time | « L'heure de fin doit être postérieure à l'heure de début » | — |
| BOOK-19 | 1 | ∅ | Duration 0 | « La durée doit être supérieure à 0 » | — |
| BOOK-20 | 1 | ∅ | No date | « Sélectionnez une date. » | — |
| BOOK-21 | 2 | ∅ | Open the dialog at 00:30 Tunisian time | The date pre-fills **today**, not yesterday | `todayLocalIso()` |
| BOOK-22 | 1 | ∅ | Book a busy slot « pour tout le cabinet » vs « pour ce praticien » | The notice states the right scope; a later booking there prompts | — |

## 2c — The acts half

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| BOOK-23 | 1 | ∅ | Add three acts | Duration = the **sum**; total = sum of prices | `durationTouched` false |
| BOOK-24 | 1 | BOOK-23 | Hand-edit the duration, then add a fourth act | The typed duration **survives** | `durationTouched` |
| BOOK-25 | 0 | ∅ | Override an act's price to `120,000` → save → open the fiche | The fiche prefills **120,000**, not the 40,000 tarif | `AgreedCost` must be threaded — the fiche re-prices from the **catalogue** |
| BOOK-26 | 0 | ∅ | Type `12O` (letter O) as a price → save | Refused: « Corrigez le prix d'un acte… » — **not** booked at the tarif | `agreedCostOf` reads it as null and would silently use the tarif |
| BOOK-27 | 1 | ∅ | Type a price, then **change the act** | The price follows the **new** act's tarif unless it was hand-typed | `applyProcedure`'s « ce n'est pas cet acte » bug |
| BOOK-28 | 1 | ∅ | Add a **custom** (non-catalogue) act name | Accepted; no catalogue provenance; price left to the dentist | — |
| BOOK-29 | 1 | ∅ | Remove all acts | Save allowed (a visit may have no act yet) | — |
| BOOK-30 | 2 | busy slot | — | The acts section is **absent** (no patient ⇒ no clinical act) | — |

## 2d — Multi-séance acts: the split-by-default path

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| BOOK-31 | 0 | ∅ | Pick « Couronne / bridge » **before** picking the patient | « Traitement en 3 séances » **and** the séance editor both render | the reported field defect: the sentence rendered above **no control** |
| BOOK-32 | 0 | ∅ | « Nouveau patient » (walk-in) + a protocol act | Same — the editor renders with no patient id yet | that mode has no id until save |
| BOOK-33 | 0 | an existing appointment | Open **« Modifier »** with a protocol act | The editor renders — the edit dialog never passed the prop, for everyone, always | the third situation of the same defect |
| BOOK-34 | 1 | BOOK-31 | Save | A **`Draft`, un-numbered** treatment exists with 3 steps; the appointment holds **step 1** | `materialiseTreatments` at save |
| BOOK-35 | 0 | BOOK-31 | Save, hit the slot-taken confirm, confirm | **One** treatment, not two | `createdPlansRef` — both dialogs re-run the save from the top |
| BOOK-36 | 0 | BOOK-31 | Same with the **out-of-hours** confirm; then with **past-time** | One treatment each time | same ref, all three confirmations |
| BOOK-37 | 1 | BOOK-31 | « Tout faire en une séance » → save | **No** treatment created; one plain appointment | `plannedProtocol = null` |
| BOOK-38 | 1 | BOOK-31 | Delete every step in the editor | That **is** « une seule séance » — `[]` is never stored | `plannedProtocol` tri-state |
| BOOK-39 | 0 | ∅ | Two protocol acts (crown **and** implant) on one booking | Only the **first** is followed; the other's card **says which act holds the slot** | one `TreatmentPlanId` per appointment; `resolveAttachedPlanId` refuses two |
| BOOK-40 | 0 | BOOK-39 | Say « une seule séance » on the crown | The **implant** becomes followable — the crown's answer did not freeze it | `setPlannedProtocol` writes over **`value`**, not the resolved list |
| BOOK-41 | 1 | BOOK-31 | Rename a séance in the editor, then save | The treatment's step carries the typed label | — |
| BOOK-42 | 1 | BOOK-31 | Blank a séance's name → save | Refused **in the dialog**, before the walk-in has a record | else `SetSteps` refuses on the *treatment*, half-made |
| BOOK-43 | 1 | BOOK-31 | « Réinitialiser » after editing | Back to the catalogue's protocol; the reset control is then hidden | `canReset` |
| BOOK-44 | 0 | BOOK-31 | Merely **open** the edit dialog on a protocol act and close it | The appointment's **duration is unchanged** | derived on render, **never** seeded by an effect — the edit dialog's `onChange` also resets `durationTouched` |
| BOOK-45 | 1 | a devis with outstanding acts | Use « Actes du devis » | Every outstanding act offered; picking one links the **step** | — |
| BOOK-46 | 0 | a plan whose **first** act is `Done` (a priced continuation) | Book from « Actes du devis » | The **bookable** act is offered, not `items[0]`; the save is **not** refused | `schedulablePlanItems` / `planIdByItem` — never `plan.items[0]` |
| BOOK-47 | 1 | a plan step suggestion applies | Open the dialog | **At most one** notice renders (plan suggestion wins) | AC-5 of the continuation spec |
| BOOK-48 | 1 | ∅ | Edit an act's price inside « Actes du devis » | It saves to the **treatment**; the échéancier re-spreads server-side | the act is priced once |
| BOOK-49 | 0 | a fiche billed on a note, partly paid | « c'est la suite d'une séance précédente » → pick → price the remaining work → **Annuler** the booking | **No devis**, no appointment, and the séance is still offered as continuable | the devis is minted by the SAVE (`materialiseTreatments`), never by the dialog's own press |
| BOOK-50 | 0 | BOOK-49's arrange | Same, but **save** the booking | Exactly one devis, numbered + `Accepted`, and the appointment carries its id | `planIdByItem` merged from the materialiser, else « Le plan de traitement est requis pour lier l'acte. » |

---

# HP-3 · Le modal de rendez-vous (édition)

Route `/appointments?appointmentId=<guid>` — opens the modal directly.

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| EDIT-01 | 1 | any appointment | Change the time → save | Moved; acts untouched | baseline |
| EDIT-02 | 0 | an appointment with 3 acts | Change **only the status** (to `InProgress` — ⚠️ **not** `Confirmed`, which is legacy-only and refused by design: « withdrawing a confirmation » has no clinical meaning) | **All three acts survive** | `{ status }` alone would drop every act — tri-state DTO |
| EDIT-03 | 0 | an appointment with 3 acts | Remove one act → save → reopen | Exactly two; prices intact | `SetProcedures` replaces the whole list |
| EDIT-04 | 0 | an act with a negotiated `AgreedCost` | Reopen, save with no change | The negotiated price **survives** | a saved `procedures` list omitting prices restores every act to its tarif |
| EDIT-05 | 0 | an appointment at its slot's start minute | Open the modal, wait past the minute boundary (`AppointmentProgressJob` writes), press Enregistrer | Saved — **no** « modifié par quelqu'un d'autre » | `VersionBeforeAutoAdvance` + `AutomaticWriteInterceptor` |
| EDIT-06 | 0 | a real 409 (two browsers) | Save in the stale one → refusal → **click « Recharger »** → save again | The second save **succeeds** | `useConflict` — a bare `setError` is poisoned for good (6 refusals over 81 min, recorded) |
| EDIT-07 | 1 | EDIT-06 | After the 409, press Enregistrer again **without** reloading | Still refused, and the message still offers « Recharger » | the refusal must not go silent |
| EDIT-08 | 1 | an appointment linked to a devis step | Change the act to a different one | The devis link is dropped or moved coherently — never left naming the old step | — |
| EDIT-09 | 1 | ∅ | Reschedule to a taken slot / closed day / the past | The same three advisory prompts as BOOK-11/14/15 | one guard set, two dialogs |
| EDIT-10 | 0 | BOOK-33 | Confirm an overlap in the **edit** dialog | The escape hatch does **not** close the window; a second refusal **is** said | `bd524516` |
| EDIT-11 | 1 | ∅ | Open the modal on a phone (390) | **One** scroller, not two | `9fa55b9b` — and the guard that stops the next one |
| EDIT-12 | 1 | a « Séance passée » appointment | Open it | Editable; status shown correctly | `appointment-elapsed-status` |
| EDIT-13 | 1 | ∅ | Cancel an appointment | Off the agenda; the linked devis step is released, not consumed | `SetNull`, not cascade |
| EDIT-14 | 2 | an appointment with a Google-synced event | Move it | The sync pushes (asymmetric) | — |

---

# HP-4 · Éditer les sous-étapes — **from all four places**

There are exactly four editors of a step list, and they must not disagree.

| # | Editor | Reached from | Edits |
|---|---|---|---|
| A | `ProcedureTypeStepsDialog` | `/procedure-types` → « Étapes » | the **catalogue template** |
| B | `AppointmentProtocolEditor` | inside the acts picker, in **both** booking dialogs | the **planned** séances of one booking |
| C | `PlanItemStepsDialog` | `/treatment-plans/[id]` → the act row's control | the steps of a **live devis act** |
| D | `ContinueSessionDialog` | the booking dialog's continuation notice | the **two** generated steps of a continuation |

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| STEP-01 | 0 | a live devis act with 6 steps, intervals set | **C**: rename step 3, save | Every other step's `MinDaysAfterPrevious` survives | `SetTreatmentPlanItemStepsCommand`'s recorded defect — the client sent the field all along |
| STEP-02 | 0 | STEP-01 | Reload; check the recall worklist | The implant is **not** reported abandoned | the symptom the wiped interval produced |
| STEP-03 | 0 | a devis act with steps 1–2 `Done` | **C**: try to delete step 1 | Refused — recorded work cannot be removed | `HasDeliveredWork` per act |
| STEP-04 | 1 | same | **C**: add a step 7 | Added; progress bar re-reads; « Traitements en cours » updates | — |
| STEP-05 | 1 | same | **C**: reorder steps | Order persists; the *done* ones stay done and keep their fiches | — |
| STEP-06 | 0 | a step whose fiche is on a **live** note | **C**: detach that step | Refused with `Snapshot.Remedy` — **the remedy that exists** (avoir for the whole remaining amount if money was collected, else cancel) | the refusal that survived the avoir it demanded |
| STEP-07 | 0 | STEP-06 | Issue the full avoir, retry the detach | **Succeeds** | `IsSpent`, not `RepresentsItsPlan` alone |
| STEP-08 | 1 | a plan act with steps | **C**: change an interval to 0, then to 400 days | Both accepted; the échéance/recall reads follow | — |
| STEP-09 | 0 | ∅ | **B** in create, **B** in edit, **C**, and **A** on the same act | All four show the **same** step list shape and the same field set | four editors, one contract |
| STEP-10 | 1 | ∅ | **B**: edit séances, then « Réinitialiser » | Back to the catalogue's protocol — **A**'s content is the source | — |
| STEP-11 | 1 | ∅ | **A**: change the catalogue after **B** has planned a booking | The booking's plan is **unchanged**; the next booking gets the new one | template, not link |
| STEP-12 | 1 | a continuation | **D**: leave the label blank | « Séance suivante » — never inferred from a catalogue protocol | nothing knows how the finished work was divided |
| STEP-13 | 1 | a continuation | **D**: type a label → then edit it in **C** afterwards | Editable in the ordinary steps dialog | both steps are editable afterwards |
| STEP-14 | 0 | a **cancelled** plan | **C**: try to edit steps | Refused: « Les étapes d'un plan annulé ne peuvent pas être modifiées. » | — |
| STEP-15 | 1 | a `Completed` plan | **C**: open the steps dialog | Read-only or refused — never a silent no-op | — |
| STEP-16 | 2 | **B** with 6 séances at 320 px | — | The frise is readable, one line per séance; **no** disclosure mode | 515 px at 1440 / 843 px at 390 was the defect |

---

# HP-5 · La fiche de séance — création

Route `/patients/<id>?addRecord=1&appointmentId=<id>`.

## 5a — A simple fiche (no devis)

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| FICHE-01 | 1 | booking with 1 act, no plan | Open the fiche | The act is prefilled: name, teeth, **price** | `applyAppointment` |
| FICHE-02 | 0 | booking with **3** acts | Open the fiche | **All three** cards are proposed | one card showed one act's money for a visit booked for three — the others were never billed |
| FICHE-03 | 1 | FICHE-02 | Delete one card | Deleted, and **offered back** (it is an ordinary card) | an act not performed is removed, not never mentioned |
| FICHE-04 | 1 | FICHE-02 | Delete **all** cards | A blank card remains — never a surface with no way to start | `REMOVE_ACT` |
| FICHE-05 | 2 | ∅ | Press « Ajouter un acte » twice quickly | **One** blank card, not two | a trailing blank **is** the card being asked for |
| FICHE-06 | 1 | ∅ | Type « Total = 600 » with 3 blank acts | Split **evenly**, 200 each, exact to the millime | `distributeSessionTotal`, integer millimes |
| FICHE-07 | 1 | 3 priced acts | Type a new « Total » | Split **proportionally**; largest-remainder handles the odd millimes | ties go to the bigger act |
| FICHE-08 | 0 | ∅ | Set « Payé » **greater** than the acts total | Refused `dental_record_payment_exceeds_cost` — **the save does not happen** | it once saved 999 DT against a 40 DT act, no invoice, and displayed the money as collected |
| FICHE-09 | 1 | ∅ | Set « Payé » = total → save | Note d'honoraires raised; success toast **names the number and the amount** | `Billed` outcome |
| FICHE-10 | 0 | ∅ | Set « Payé » = 0 → save | Fiche saved; **no** invoice; toast says nothing was collected | `NotCollected` is a legitimate outcome, not an error |
| FICHE-11 | 0 | ∅ | Set « Payé » = **half** the total → save | Note raised for the **full** total, payment for the half; « Créances » shows the rest | the partial-payment path |
| FICHE-12 | 0 | ∅ | Record the fiche with `InterventionDate` = **two days ago**, « Payé » > 0 | The caisse movement lands on **that day**, not today | `PaidOn = record.InterventionDate` |
| FICHE-13 | 0 | ∅ | Pay by **cheque** (number, bank, due date) | Appears in « Chèques à encaisser »; **not** under « dont espèces » | it was a hard-coded `Cash` |
| FICHE-14 | 1 | ∅ | Pay by cheque with no number | Refused or flagged — a cheque has an identity that travels with the money | L8 slice A |
| FICHE-15 | 1 | ∅ | Save with a **custom** act name and no price | Saved at 0 **with a warning**, not blocked | — |
| FICHE-16 | 1 | ∅ | Tap teeth on the chart with **one** act card | That card takes them (it is armed on arrival) | — |
| FICHE-17 | 1 | ∅ | Tap a tooth with **several** cards and none armed | The chart **asks for a card** — nothing is guessed | no unambiguous owner |
| FICHE-18 | 1 | a per-tooth act on 3 teeth | — | Total = unit × 3; the `/dent · forfait` switch is shown | `derivePerTooth` |
| FICHE-19 | 1 | FICHE-18 | Switch to « forfait » | Total = the unit alone; the intent is **locked** | touching the switch locks intent |

## 5b — A fiche of a sub-step (the devis-carried path)

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| FICHE-20 | 0 | booking on step 1/3 of a 500 DT crown | Open the fiche | ~~the field is `readOnly`~~ → **there is no price field at all**; the card reads « Aucun honoraire sur cette séance. Cet acte est chiffré une fois, sur le traitement. » | the act is priced once, on the treatment. `act-card.tsx:278` **withholds** the input — a card reading « 0,000 DT » « is the third of the séance's zeros and it says nothing ». **`CLAUDE.md` is stale on this** |
| FICHE-21 | 0 | FICHE-20 | Overtype the 0 via the DOM / the API | The server **imposes 0** anyway (`Cost` **and** `UnitCost`) | `PlanCarriedActPricing` — the fiche is the screen that creates the money and asked nobody |
| FICHE-22 | 0 | FICHE-20 | — | « Total » and « Payé » are **withheld** — the séance is wholly on the treatment | `seanceIsWhollyOnTreatment`, structural (**all named acts carried**), never `grandTotal === 0` |
| FICHE-23 | 0 | a **mixed** séance: crown step 1 (carried) + a 60 DT détartrage | Type « Total = 150 » | The **carried** act stays 0; only the détartrage takes the money — it is **not** under-billed by a share given to an act that cannot hold it | `distributeSessionTotal` filtered on `isActNamed` alone |
| FICHE-24 | 0 | FICHE-23 | — | « Total » / « Payé » **are** shown (not every act is carried); the crown's price field alone is withheld | the display rule is **per act**, the total rule is **per séance** |
| FICHE-25 | 0 | FICHE-20 | Enter « Encaissé sur le traitement » = 200 → save | 200 on the **devis** échéancier, one caisse movement; « Payé » untouched and **not** folded in | a **second** money field |
| FICHE-26 | 0 | a **`Draft`** followed treatment | Collect 200 on it | The devis **number is minted** (a Draft has no échéancier and la caisse cannot see one) | `CollectOnTreatmentCommand` |
| FICHE-27 | 0 | FICHE-26 | Check « Solde patient » and « Créances » | The minted plan carries debt for the **quoted** total — not for a total nobody quoted | `OpenStatusFromWork` keys on `Number is null` |
| FICHE-28 | 0 | FICHE-25 | Enter more than the treatment's remaining | **Disabled**, not turned into « Corriger » | `overCollectedOnPlan` disables — unlike `overpaid` |
| FICHE-29 | 0 | step 1/3 | Save | The devis step is marked done; the plan goes `InProgress`; « Traitements en cours » lists it | `AdvanceAfterWorkRecorded` |
| FICHE-30 | 0 | step 1/3 | Save | **The odontogramme charts NOTHING** — the act is not `Done` | seven live rows charted from a step 1 of 2, three claiming implants that did not exist |
| FICHE-31 | 0 | step 1/3 with an open « à traiter » on those teeth | Save | The diagnosis is **still there** — the work is not finished | `ClearDiagnosesForTreatedTeethAsync` moved with the charting rule |
| FICHE-32 | 0 | step **3/3** | Save | **Now** the chart asserts the end state; the diagnosis clears; the plan auto-`Completed` | the two changes are one change |
| FICHE-33 | 0 | teeth 16, 36 entered on **step 1**, absent from step 3's fiche | Save step 3 | The chart still names 16 and 36 — **but only because the fiche's own prefill carried them**. The server charts what the last fiche *sends* and has **no fallback**, so this must be exercised in a **browser**, never on the wire | `TreatedToothNumbers` — with the chart written at the END, teeth entered early would chart **nothing at all**. ⚠️ The whole guard is client-side |
| FICHE-34 | 1 | step 2/3 | Open the fiche | The chart is **prefilled with the teeth the last séance treated**, not with the devis line's (often empty) | a devis line is very often an « acte général » |
| FICHE-35 | 0 | a séance closing **two** steps | Save | Both marked; the « is this the last step » question is answered by the **aggregate** | never derived from the step's rank |
| FICHE-36 | 1 | an act booked **whole** (no steps) | Save its first fiche | It finishes immediately and charts | an act with no protocol goes Planned → Done |
| FICHE-37 | 0 | a **hand-typed** devis line (no `ProcedureTypeId`) on a **single-act** fiche | Save | The act is zeroed (unambiguous) | — |
| FICHE-38 | 0 | a hand-typed devis line on a **multi-act** fiche | Save | **Nothing** is zeroed — left for the dentist, not guessed | silently un-billing the wrong act is worse |
| FICHE-39 | 0 | a crown carried by a devis **and a second independent crown** the same day on another tooth | Save | Only **one** act is zeroed | `PlanCarriedActPricing`'s deliberate bound; `markBilledOnPlan` matches the same way |

## 5c — Le bridge (le seul acte qui charte deux états)

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| FICHE-40 | 0 | « Couronne / bridge » on 14, 15, 16 | Mark **15** as pontique → save (act `Done`) | 14 and 16 chart as **piliers**, 15 as **pontique** | one act, one devis line, right total — and three abutments with no pontic is anatomically impossible |
| FICHE-41 | 0 | FICHE-40 | — | The odontogramme draws the travée over the **marked** span only | it used to draw one across the run, which made it look deliberate |
| FICHE-42 | 0 | the same act with **no** pontique marked | Save | Charts **exactly as before** the feature | what makes it safe against every existing row |
| FICHE-43 | 0 | a **cantilever** (pontique past the last abutment) and a **pier** (crowned mid-span) | Mark them | Correct in both — roles are **never** inferred from position | 12·11·21 sorts to 11·12·21, so « the middle one » is the wrong tooth |
| FICHE-44 | 1 | FICHE-40 | Reopen the fiche | The pontique marks are restored | `ponticToothNumbers` round-trips |

---

# HP-6 · Éditer une fiche

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| FEDIT-01 | 1 | an unbilled fiche | Change a tooth → save | Saved; chart and history follow | baseline |
| FEDIT-02 | 0 | an unbilled fiche, « Payé » 50 of 100 | Raise « Payé » to 100 | The existing note is **topped up** — **no second note** | idempotent by delegation; a fiche is re-saved routinely |
| FEDIT-03 | 0 | a **billed** fiche | Change an act's price | Refused `dental_record_acts_changed_after_billing`, **pre-commit** — the save did **not** happen | a refusal after a post-commit save leaves the fiche permanently disagreeing with its note |
| FEDIT-04 | 0 | FEDIT-03 | Press « Corriger » on the refusal | Opens the avoir path; after it, the séance re-bills | `IsCorrectable` — exactly these two codes |
| FEDIT-05 | 0 | a billed fiche that collected 80 | **Lower** « Payé » to 50 | Refused `dental_record_payment_lowered`, naming the note and the 80 | money on a numbered document cannot be un-received by retyping |
| FEDIT-06 | 1 | FEDIT-05 | Press « Corriger » | Offered (this code **is** correctable) | — |
| FEDIT-07 | 0 | a fiche whose note is **cancelled** | Re-save with a payment | Refused `dental_record_invoice_not_live`; **no second note** is raised silently | A-1 — two notes for one séance and nobody can say which the patient holds |
| FEDIT-08 | 1 | FEDIT-07 | Use « Facturer cette intervention » | A fresh note **is** raised (this is the non-silent path) | `IsAutomatic = supersedesInvoiceId is null` |
| FEDIT-09 | 0 | a fiche whose note is **fully credited** | Re-save with a payment | Same refusal — `IsSpent` covers the avoir case | a credit note leaves the invoice `Paid` |
| FEDIT-10 | 0 | a fiche paid by cheque, **cheque banked** | Change the séance's date | Refused `dental_record_payment_banked` | L4 |
| FEDIT-11 | 0 | a 3-act fiche | Save after editing **only the note text** | All three acts and their prices survive | `SetActs` regenerates every act id — there is no before/after identity to diff |
| FEDIT-12 | 1 | a reopened fiche with **one** act | Open it | The act is **armed** so the chart can edit its teeth on arrival | — |
| FEDIT-13 | 1 | a reopened fiche with several acts | Open it | **Nothing** armed | — |
| FEDIT-14 | 0 | a reopened per-tooth act | Open it | Pricing intent is **read, never re-derived** from the teeth | a cost of 0 is also how a courtesy act is recorded |
| FEDIT-15 | 1 | a reopened act at 90,500 | Open it | The field reads « 90,500 » (not « 90.5 ») and accepts that form back | `formatAmount` |
| FEDIT-16 | 0 | a billed fiche | Delete it | Refused, or the note is dealt with first — never an orphaned note | `PatientDeletionBlockers` / `DeleteDentalRecordCommand` |
| FEDIT-17 | 0 | a fiche linked to a devis step | Delete it | The step is un-marked; the plan's status recomputes; the chart's assertion is withdrawn | — |
| FEDIT-18 | 1 | ∅ | Trigger a 409 on a fiche save | « Recharger » offered; the second save succeeds | `useConflict`, every version-round-tripping form |
| FEDIT-19 | 2 | after a successful « Corriger » | — | The « Corriger » link **clears once it has arrived**, not before | `9c9e2f6d` |

---

# HP-7 · La page traitement (le workspace du devis)

Route `/treatment-plans/[id]`.

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| PLAN-01 | 0 | a **`Completed`** plan | Open the workspace | It **renders** | `primaryAction` is a `useMemo` running during render and calling the `const` confirm openers — declared above them, every `Completed` plan threw and the workspace failed entirely, invisible to `tsc`, `check:responsive` and the build |
| PLAN-02 | 1 | each of Draft / Accepted / InProgress / Completed / Cancelled | Open each | **One** primary action + a « ⋯ » menu — never seven controls of equal weight | `primaryAction` derives the one act (mint → bill → resume) |
| PLAN-03 | 0 | a **followed** (Draft, un-numbered) treatment | — | « Annuler » is **absent** — it was refused every time it was pressed | `Cancel` throws « Un brouillon se supprime… », and the remedy lived only on `/treatment-plans` |
| PLAN-04 | 0 | a Draft with **recorded séances** | Press « Supprimer le brouillon » | **Refused** — the cascade takes the acts and their step rows while the fiches survive attached to nothing | `CanBeDeleted` asked only the status; `RemoveItem` had refused `HasDeliveredWork` per act all along |
| PLAN-05 | 1 | a Draft with **no** recorded work | Delete it | Deleted; no number consumed | — |
| PLAN-06 | 1 | a Draft | « Éditer le devis » / mint | Number assigned, gapless; status `Accepted` | `DevisNumbering` |
| PLAN-07 | 0 | PLAN-06 | — | **One** lump-sum échéance dated at the acceptance instant, `IsAutoRaised = true` | a ledger container, not a promise |
| PLAN-08 | 0 | PLAN-07 | Look at the échéance the next day | **Not** « En retard » | 25 of 27 unpaid rows were flagged, cancelled and already-invoiced ones included |
| PLAN-09 | 0 | a hand-typed échéancier dated **today** | Look at it tomorrow | It **is** late — a date a dentist typed is a promise | the distinction is **stored**, not guessed |
| PLAN-10 | 0 | PLAN-08 | « Réviser l'échéancier » | `IsAutoRaised` **cleared** — revising is a dentist settling on the dates | `Revise` clears it; `RespreadSchedule` sets it |
| PLAN-11 | 1 | an Accepted plan | Revise to a total ≠ the plan's | Refused: « Le total des échéances doit être égal au coût total planifié » | — |
| PLAN-12 | 1 | an Accepted plan | Empty the échéancier | Refused: « L'échéancier ne peut pas être vide sur un devis accepté. » | — |
| PLAN-13 | 0 | an Accepted plan | « Modifier les actes et les prix » (amend) | Named for what it edits — **not** « Éditer le devis » beside « Modifier le devis » | two near-identical French labels for two very different acts |
| PLAN-14 | 0 | a plan **bridged** to a live note | Amend to add an act | **Refused** in as many words: « acts added afterwards would be invisible in every balance » | `AmendTreatmentPlanCommand` |
| PLAN-15 | 0 | a plan act already `Done` | Remove it from the devis | Refused: « Cet acte est déjà réalisé et ne peut plus être retiré du devis. » | — |
| PLAN-16 | 0 | a followed treatment | « Arrêter le traitement » then « Reprendre le traitement » | It comes back as a **`Draft`** — **not** an `Accepted` devis with a null number and a live créance | the recorded defect, exactly |
| PLAN-17 | 1 | a stopped treatment | Check « Traitements en cours » and the recall worklist | Absent from both; not reported as abandoned | — |
| PLAN-18 | 0 | a plan with 3 fiches of one implant | Read the séance history | « Implant dentaire / Pose de l'implant · étape 3 / 6 » — **not** three identical rows | three fiches read as three implants |
| PLAN-19 | 0 | a séance of that implant that collected **nothing** | Read its row | « — », **with the treatment badge kept** | keying on `collectedOnTreatment > 0` left it reading « 0,000 DT · 0,000 DT », indistinguishable from a free visit while the treatment showed 1 500 outstanding |
| PLAN-20 | 1 | a plan with several payments on one échéance | Read the échéancier | **One line per payment** | `ae78cce4` |
| PLAN-21 | 0 | ∅ | Open « Traitements en cours » **the day** a treatment is created | It is **listed** — `item.Status == Planned` **with steps** counts | a filter on `InProgress` hid it on exactly the day it was created |
| PLAN-22 | 0 | PLAN-21 | — | An act with **no** protocol and status `Planned` is **not** listed | `Steps.Any()` is what keeps the widening honest |
| PLAN-23 | 0 | a mix of booked / unbooked / due steps | Read the list | The three groups are **contiguous**, not interleaved | the « is this séance booked? » subquery must answer **exactly** what `TreatmentsInProgressReader` answers — per **step**, with **no** « from today » floor (an `AwaitingClosure` visit is still a standing booking) |
| PLAN-24 | 0 | a followed treatment older than 14 days | Read the recall worklist | **Not** « devis présenté, jamais répondu » — a quote that cannot exist | `Accept` is the only writer of `Number`; `RecallWorklistRules.IsUnanswered` |
| PLAN-25 | 0 | this week's followed treatments | Read the dashboard | **Not** counted under « Devis en attente de réponse » | `CountUnansweredDraftsAsync`; the identical premise was corrected in `RecallWorklistRules` and this copy was missed |
| PLAN-26 | 0 | an « Extraction simple » quoted on **13 and 43**, first séance recorded on a fiche naming **13, 27, 36, 37, 43** | Read the odontogramme | « Traitement en cours » rings **13 and 43 only** | `teethUnderTreatment` reads the devis **line** first; the union put rings on three teeth with no treatment |
| PLAN-27 | 1 | PLAN-26 | Open the next fiche | Its chart seed prefers the teeth actually **worked on** — the **opposite** priority, deliberately | `openPlanItems` |
| PLAN-28 | 1 | a devis | « Devis PDF » and « Envoyer par e-mail » | Both in the « ⋯ » menu; the PDF carries the number and the acts | — |
| PLAN-29 | 2 | a plan act row | The steps control and the row's own 44 px overlay | Neither covers the other | `plan-act-row` z-order note |
| PLAN-30 | 2 | the workspace at 320/390/820 | — | One scroller; the header's one action + menu fit | — |

---

# HP-8 · La suite d'un acte non terminé

`ContinueRecordedActCommand` — « cette séance est la suite de celle du 12 août ».

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| CONT-01 | 1 | a recorded act, **no** note | Continue it, no extra money | A 2-step plan: « 1re séance » **marked done against the fiche that evidences it**, « Séance suivante » open. The plan owns the act's fee | — |
| CONT-02 | 0 | a recorded act on a **live** note, **no** extra money | Continue it | The note is **attached** to the plan (`AttachToTreatmentPlan`) | without the link a 1 000 DT bridge already invoiced is claimed **twice** — 200 by the note, 1 000 by the devis |
| CONT-03 | 0 | a **30 DT coiffage on a live note**, continued at **10 DT** | Continue with `RemainingWorkCost = 10` | The note is **NOT attached**; the billed act sits on the devis at **0**; the devis owes the **10** alone | the measured defect: the balance read **0** |
| CONT-04 | 0 | CONT-03 | Read « Solde patient », « Créances », la caisse, the dashboard | The 30 is on the note, the 10 on the devis, **and both are visible** | `BilledPlanIds` drops a bridged plan **whole** |
| CONT-05 | 0 | a recorded act on a **`Draft`** note | Continue it | ~~not attached~~ → it **is** attached, and correctly: `RepresentsItsPlan(Draft)` is false, so the plan keeps the whole balance and pricing the first act 0 there would lose the 30 to save the 10 | gated on `RepresentsItsPlan`, **never** on « is there a note » |
| **CONT-05b** | **0** | CONT-05 | **Issue that Draft note** | The 10 DT of new work is **still readable** in « Solde patient » and « Créances » | ❌ **CONFIRMED DEFECT** — the balance drops 40 → 30 while the plan still reports 40. The status is read at continuation time and a Draft's is not final; issuing it makes `BilledPlanIds` drop the plan whole. See [`findings.md`](findings.md) § 1 |
| CONT-06 | 0 | CONT-05 | — | The 30 is **not** lost to a 0-pricing | the Draft case would lose the 30 instead of saving the 10 |
| CONT-07 | 0 | an act with **800 collected of 1 000** | Continue it | The 800 is **never replayed** onto the plan; the 200 stays on its note | a plan installment posts its own caisse movement — 1 600 in the till for 800 received |
| CONT-08 | 1 | CONT-07 | Read the continuation notice | It **states** the money: « Acte prévu à 1 000,000 DT · 200,000 DT restent dus sur la note F-… » | stated, never moved |
| CONT-09 | 0 | CONT-07 | — | **Nothing** is prefilled into « Montant du travail restant » | that field keeps its one meaning: **new work only** |
| CONT-10 | 1 | a continuation plan | Book its second séance | Bookable; `planIdByItem` includes it even though act 1 is `Done` | BOOK-46's twin |
| CONT-11 | 0 | a continuation with a **priced** remainder | Book the continuation | The **second** act is offered, not `items[0]`; the save is not refused with « Le plan de traitement est requis pour lier l'acte. » | `attachPlanAct`'s own doc said « never `plan.items[0]` » since the day it was written |
| CONT-12 | 1 | ∅ | Continue the same act **twice** | Refused / excluded — an act already on a devis raises no second offer | else a second devis over the same work |
| CONT-13 | 1 | a multi-act fiche | Continue **one** act | Only that act; the others are untouched | the flag is per act |
| CONT-14 | 1 | ∅ | Cancel the irreversibility confirmation | **No** devis created | the notice alone creates nothing |
| CONT-15 | 2 | `GetContinuableActsQuery` | Open the list | Every recent act offered (so a forgotten tick is not a dead end) | — |
| CONT-16 | 1 | ∅ | Tick « Acte non terminé » on an act card, save | ~~`isUnfinished` is SPEC ONLY~~ → **it shipped on 2026-09-11.** `DentalRecordAct.IsUnfinished`, `GetUnfinishedActsQuery`, `ContinuableActDto.IsUnfinished`, migration `20260911161617_AddDentalRecordActIsUnfinished`. The card reads « Il faudra une autre séance. L'acte passe dans « Suites à planifier » ; **aucun montant n'est modifié**. » | the row said « not implemented » for three days after it was |
| CONT-17 | 0 | CONT-16 | Save the fiche | The act's money is **unchanged** — ticking « non terminé » is a *flag*, never a re-pricing | the card promises it in as many words |
| CONT-18 | 1 | CONT-16 | Read « Suites à planifier » | The act is listed; `GetUnfinishedActsQuery` is its one reader | — |
| CONT-19 | 1 | CONT-16 | Continue that act | Offered; the continuation path prices only the **new** work | `CONT-09` |
| CONT-20 | 1 | CONT-16 | Untick it and re-save | It leaves « Suites à planifier »; nothing else moves | a flag is reversible |
| CONT-21 | 0 | an act already continued onto a devis | Tick « non terminé » | ❓ Offered twice, or excluded? `ContinuationTracking` counts an act on any non-cancelled plan as taken — the two lists must not disagree | `CONT-12`'s twin |

⚠️ `features/unfinished-act-continuation/` still has **no `notes.md`**, so the spec's nine acceptance criteria
are not yet catalogued here. CONT-17…21 are the rows derivable from the code as it stands; the rest is the
next section to derive.

---

# HP-9 · Clôturer un traitement

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| DONE-01 | 0 | a plan with **unrealised** acts | « Terminer » | **Succeeds**, leaving them unrealised — exactly what the confirmation has always said in words | the aggregate used to refuse in precisely the case the dialog bothered to explain; **no case could be built from the UI in which it succeeded** |
| DONE-02 | 1 | DONE-01 | Read the confirmation | « Les N actes non réalisés resteront non réalisés — la clôture ne les valide pas » | — |
| DONE-03 | 0 | a plan whose **last step** just landed | Save that fiche | Auto-`Completed`, and that path **does** assert everything is done | `Complete(leaveUnrealisedActs: false)` |
| DONE-04 | 0 | DONE-03 | — | The « Terminer » button is **not rendered** (already completed) | why DONE-01 had no reachable success case |
| DONE-05 | 0 | a plan with money outstanding | « Terminer » | Money **untouched**; the créance survives | « Terminé » means the work is over, not that the patient has paid |
| DONE-06 | 1 | an already-`Completed` plan | « Terminer » again | « Ce traitement est déjà clôturé. » | — |
| DONE-07 | 1 | a `Completed` plan | « Reprendre » | Back to `InProgress`/`Accepted` **with its number** | `Reopen` — only a terminated devis may be resumed |
| DONE-08 | 0 | DONE-07 | — | A **followed** (un-numbered) treatment resumed does **not** become `Accepted` | PLAN-16's rule, on the other door |
| DONE-09 | 1 | a `Completed` plan | Try to add a séance | « Ce devis est clôturé : il ne peut plus recevoir de séance. » | — |
| DONE-10 | 1 | a `Cancelled` plan | Try to amend / pay / re-step | Refused, each with its own sentence | — |
| DONE-11 | 0 | DONE-03 | Read the odontogramme | **Now** the end states are asserted | charting is gated on the act, not the plan — verify both |
| DONE-12 | 1 | DONE-03 | Read « Traitements en cours » and the dashboard | Gone from both | — |

---

# HP-10 · Les chemins de l'argent

## 10a — Facture / note d'honoraires

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| MONEY-01 | 0 | 3 fiches billed in sequence | Read `/factures` | Numbers are **gapless** and sequential | `DevisNumbering` / invoice numbering retry |
| MONEY-02 | 0 | a note with a payment | Try to cancel it | **Refused** — `Invoice.CanCancel` refuses a note carrying a live payment | and this is why the avoir is the only remedy |
| MONEY-03 | 0 | MONEY-02 | Issue an avoir for the **whole** collected amount | The note is `IsSpent`; the work becomes correctable | a **partial** avoir leaves the note still billing |
| MONEY-04 | 0 | MONEY-02 | Issue a **partial** avoir, then retry the correction | Still refused — and the message says a partial avoir does not suffice | `Snapshot.Remedy` |
| MONEY-05 | 1 | a note | « Corriger » (`correct-invoice-dialog`) | Goes **through the fiche**; a lowered amount is **not** refused | `6ce9b31d` |
| MONEY-06 | 1 | a Draft invoice | — | It **represents nothing**: the plan keeps its own balance | `RepresentsItsPlan(Draft) == false` |
| MONEY-07 | 1 | ∅ | Bill a fiche manually via « Facturer cette intervention » | Same guards as the automatic path (its own backstop) | two implementations, **one** sentence and one code each |
| MONEY-08 | 0 | a fiche billed **and** its plan bridged | Read « Solde patient » | Counted **once**, through the invoice | `BilledPlanIds` |
| MONEY-09 | 1 | ∅ | Read an invoice PDF / detail modal | Lines name the fiche's acts and the séance | `DentalRecordInvoiceLines` |
| MONEY-10 | 2 | a séance re-billed after a cancellation | — | It legitimately has both notes; the **live** one speaks for it | `LoadAsync`'s « a live note beats a cancelled one » |

## 10b — La caisse

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| MONEY-11 | 0 | fiche payment + installment payment + expense, same day | Read `/caisse` | All three as movements; the day's total is their sum | one authority |
| MONEY-12 | 0 | a payment at **00:15 Tunisian time** | Read that day and the previous | It appears in **exactly one** | `LastTickOfLocalDayUtc`, not `EndOfLocalDayUtc` |
| MONEY-13 | 0 | a payment at **23:45** | Same | Exactly one | same |
| MONEY-14 | 0 | cash / cheque / virement / carte | Read « dont espèces » and the by-method breakdown | Each under its own method | L8 slice B |
| MONEY-15 | 0 | a cheque payment | Read « Chèques à encaisser » | Present, with its number, bank and due date | a cheque has somewhere to be chased |
| MONEY-16 | 1 | MONEY-15 | Mark it banked | Leaves the chase list; the séance can no longer be redated | `SetInstallmentPaymentBanked` / `dental_record_payment_banked` |
| MONEY-17 | 1 | ∅ | Void an installment payment | Off the caisse and the échéancier; the plan's outstanding rises | `VoidInstallmentPaymentCommand` |
| MONEY-18 | 1 | ∅ | Read « Extrait de caisse » | A **read**, matching the ledger row for row | `caisse-extrait` |
| MONEY-19 | 0 | ∅ | Run `dotnet run -- reconcile-money` before and after this whole pass | Exit **0** both times (or the same pre-existing drift) | the class of defect it exists to find |

## 10c — Créances, solde patient, paiement partiel

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| MONEY-20 | 0 | fiche total 100, « Payé » 60 | Read « Créances » and « Solde patient » | **40** owed, in both, identically | one rule, every read |
| MONEY-21 | 0 | MONEY-20 | Re-save the fiche with « Payé » 100 | The note is **topped up**; the créance clears; **one** note | FEDIT-02 |
| MONEY-22 | 0 | a devis 1 000, échéancier 4×250, 2 paid | Read the outstanding | 500 | `Outstanding` derived from the schedule |
| MONEY-23 | 0 | MONEY-22, then the plan is bridged to a note | Read every money surface | The plan is dropped **whole**; the note carries all of it | the all-or-nothing bridge |
| MONEY-24 | 0 | a **`Draft`** plan with a hand-built échéancier | Read « Créances » | **Zero** — a Draft carries no debt, including its échéancier | `CarriesDebt(Draft) == false` |
| MONEY-25 | 0 | a **`Cancelled`** plan with payments | Read « Créances » | Zero; the payments are not editable | « Ce devis est annulé : ses paiements ne peuvent plus être modifiés. » |
| MONEY-26 | 0 | a bridged plan whose collections were carried onto the invoice | Read la caisse | Counted **once**, on the invoice track | the deliberate reversal — counting the plan too would double them |
| MONEY-27 | 1 | ∅ | Record an installment payment of 0 or negative | Refused: « Le montant encaissé doit être supérieur à 0. » | — |
| MONEY-28 | 1 | ∅ | Record an installment payment on a **non-accepted** plan | Refused: « Le plan doit être accepté pour enregistrer un paiement. » | — |
| MONEY-29 | 0 | a patient with a note **and** an independent devis | Read « Solde patient » | The sum of the two, each once | — |
| MONEY-30 | 1 | ∅ | Read the reimbursement estimate (CNAM) | It knows what is **left** | L10 |

---

# HP-11 · L'odontogramme

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| ODO-01 | 0 | every FICHE-30/32/40/42/43 case | Read the chart after each | As specified there | the charting rules |
| ODO-02 | 1 | a diagnosed tooth | Diagnose « à traiter », then record the treating act | The diagnosis clears **when the act is `Done`** | — |
| ODO-03 | 1 | a treatment in progress | — | The « traitement en cours » ring is on the **devis line's** teeth | PLAN-26 |
| ODO-04 | 1 | ∅ | Switch dentition (adult / child) | Numbering and layout follow | `DentitionRules`, `dentition-view-switch` |
| ODO-05 | 1 | ∅ | Switch the odontogramme view (acts / conditions) | Both read the same underlying rows | `odontogram-view-switch` |
| ODO-06 | 2 | a tooth with many acts | Open the tooth editor popover | The panel **fits and scrolls**; its heading is on screen | 394 px of room, 590.8 px allowed, a 428 px panel at `y = -35` — `popover.tsx` lacked the cap `select`/`dropdown-menu` had, for 77 call sites |
| ODO-07 | 2 | the odontogramme card at 320/390 | — | The card **fits in a screen** | `a1f51fbb` |
| ODO-08 | 1 | ∅ | Delete the fiche that charted a state | The assertion is withdrawn | FEDIT-17 |

---

# HP-12 · L'historique des actes / la fiche patient

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| HIST-01 | 0 | 3 fiches of one implant | Read the history | Three rows naming their **step and rank**, one treatment | PLAN-18 |
| HIST-02 | 0 | a séance that collected nothing on a 6-visit implant | — | « — » with the treatment badge | PLAN-19 |
| HIST-03 | 1 | a mixed séance | — | Both acts listed with their own money | — |
| HIST-04 | 1 | a patient with a live treatment | Open the patient file | The treatment is **at the head** of the file | `49743aba` |
| HIST-05 | 1 | ∅ | Read « Actes du devis » and the patient plans strip | Same acts, same statuses as the workspace | — |
| HIST-06 | 1 | an undocumented visit | Read the patient file | Listed under « visites non documentées » | `patient-undocumented-visits` |
| HIST-07 | 1 | a visit with a fiche | Read « À clôturer » | It has left the worklist | `VisitClosure` |
| HIST-08 | 2 | the actions column at tablet width | — | Present; a strikethrough means **one** thing | `59b5392e` |

---

# HP-13 · Transversal

| Id | T | Do | Expect | Defends |
|---|---|---|---|---|
| XCUT-01 | 0 | Every paged list: page 1 → 2 → 1 | No row shown twice, none skipped | every paged read orders on a unique column last (`.ThenBy(x => x.Id)`) |
| XCUT-02 | 0 | Search with an accent and without (« Gharbi » / « gharbi ») | Both find the row — the search is a **database** question | `SearchTerm` + `SqlSearch`'s `unaccent`; filtering an already-cut page reports « aucun résultat » |
| XCUT-03 | 0 | Open a picker / a money **total** | Reads everything — `paging: null` is a first-class case, not a very large page | — |
| XCUT-04 | 0 | Two browsers on the agenda; save in one | The other refreshes (SignalR `/hub/clinic`) | `RealtimeBroadcastBehavior`; a new `Features/<Area>` emits a key `clinic-hub.ts` must declare |
| XCUT-05 | 1 | Apply a filter, copy the URL, open it fresh | The screen **seeds the same keys** it emits | `useUrlFilters` writes and never reads — 3 instances, invisible to `tsc` and `check:responsive` |
| XCUT-06 | 1 | Take an API error on each hot path | French in the UI, branch on a **code** | `graceful-error-handling`; never recover an outcome by matching French prose |
| XCUT-07 | 0 | The whole pass, then `dotnet run -- verify-schema` | Exit 0, no drift | — |
| XCUT-08 | 1 | Every screen touched, at 320 / 390 / 820 / 1180 / 1440 | Usable; 44 px targets on a **coarse pointer**; tables have a card form; heavy dialogs become sheets in `dvh` | `.claude/rules/frontend-web.md` |
| XCUT-09 | 1 | Any page, scrolled below the fold | **No third scrollbar** onto blank space | a scroll container must be `relative` or it does not clip its `absolute` children — `sr-only` **is** `position: absolute` (1168 px on the dashboard at 1440×900) |

---

# HP-14 · **Défaire** — the inverse of every writer

> Added 2026-09-11. **The generator this section comes from:** every one of the eight writers in
> `coupling-matrix.md` § 1 has an undo, and on 2026-09-11 **not one of them had a test**. HP-1…HP-13 are
> entirely a catalogue of things going *forward*.
>
> The rule this section applies: **for every forward scenario, everything `coupling-matrix.md` says the writer
> moved must move back — or the undo must be refused with a remedy that exists.** A surface that moved forward
> and does not move back is the same silent-failure shape as one that never moved at all, and it is worse,
> because the screen that shows it was right five minutes ago.

## 14a — Supprimer une fiche de soins (`DeleteDentalRecordCommand`)

The widest blast radius in the product (§ 2, eleven surfaces) run backwards. `AdminOrDoctor` only — a
secretary cannot reach any of these.

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| DEL-01 | 1 | an unbilled fiche, one act, no devis | Delete it | Gone from the historique; the visit returns to « À clôturer »; the caisse is unchanged | baseline for the whole section |
| DEL-02 | 0 | DEL-01's fiche charted « Couronne » on 16 | Delete it | The odontogramme **no longer asserts the crown** — `ToothState` is child-of-record and the FK cascades | the chart must not outlive its evidence |
| DEL-03 | 0 | tooth 16 carried « à traiter », the fiche treated it | Delete the fiche | ❓ **OPEN QUESTION, and the likeliest real defect in this section.** `DentalRecordLinker.ClearDiagnosesForTreatedTeethAsync` **deletes** the diagnosis row on save (`DentalRecordLinker.cs:80`); nothing restores it. So the tooth reads **healthy**: the treatment assertion is withdrawn by cascade and the request for the work is gone with it. Measure it, then decide whether the right shape is a soft-delete of the diagnosis or a refusal | the fiche save destroys a row the delete cannot rebuild |
| DEL-04 | 0 | a fiche carrying **step 2 of 3** of a devis act | Delete it | The step returns to « à planifier »; the act recomputes to `InProgress` with 1 of 3 done; the plan is **not** left `Completed` | `DetachPlanActsAsync` walks **steps first** — a stepped act only takes its own `LinkedDentalRecordId` on the last step, so an act-level loop alone never sees it |
| DEL-05 | 0 | a fiche carrying the **last** step, plan auto-`Completed` | Delete it | The plan **reopens** — a devis must never stay closed against evidence that is gone | the handler's own comment; `UnmarkItemStep` → `OpenStatusFromWork` |
| DEL-06 | 0 | a **`Stopped`** plan, one of whose fiches is deleted | Delete it | It stays `Stopped`, and « Reprendre le traitement » stays on the header | `StatusFollowsTheWork` — re-deriving here strands the acts the stop parked, with no route back |
| DEL-07 | 0 | a **`Draft`** (followed) treatment | Delete one of its fiches | It stays a `Draft` — never promoted to `Accepted` by a correction | the same predicate; an un-numbered plan must never wear a debt-bearing status |
| DEL-08 | 0 | a **billed** fiche, note issued, 60 of 120 collected | Delete it | **It SUCCEEDS, and that is correct** — ~~refused, or the note dealt with first~~. The note keeps its number, its lines, its amount, its caisse movement and its créance, and loses only the `DentalRecordId` provenance. ⚠️ **This row originally said the opposite and the code won.** Refusing would force an **avoir** — a fiscal document — to correct a *clinical* mistake, which is the heavier outcome, not the lighter one; the money really was received; and the note keeps its own line text, so nothing claims money nobody owes. What must hold is the **coupling** — DEL-09 | `DeleteDentalRecordCommand.cs:163`, stated at the call site: « deleting a clinical record must never alter a fiscal document » |
| DEL-09 | 0 | DEL-08 | Read la caisse, « Créances », « Solde patient », the dashboard | **All four still agree** after the delete: 60 owed on both balances, the 60 taken still in the till on its own day, the note untouched and its line still naming what it billed | the silent shape every money defect here has had — one surface moves and the other three do not |
| DEL-10 | 0 | a fiche whose note was paid by **cheque, already banked** | Delete it | ❓ Refused, or the cheque leaves « Chèques à encaisser » — never a banked cheque chasing a séance that is gone | `dental_record_payment_banked`'s premise |
| DEL-11 | 0 | a fiche that collected on a **treatment** (`AmountCollectedOnPlan`) | Delete it | ❓ Does the échéancier's collected amount come back down? The money reached the plan through `CollectOnTreatmentCommand`, which the delete does not consult | a second money field, and a second ledger |
| DEL-12 | 1 | a fiche linked to an **appointment** | Delete it | The visit is « à documenter » again, not « documentée » | `VisitClosure` |
| DEL-13 | 1 | a fiche emitting an **ordonnance** | Delete it | The ordonnance **survives** — the paper may be in the patient's hand, and `DeleteMedicalDocumentCommand` is its own gesture | « elle n'efface jamais » |
| DEL-14 | 2 | ∅ | Delete as a **secretary** | Refused — 403 | `AdminOrDoctor`, audit defect A-12 |
| DEL-15 | 0 | a **bridge** fiche on 14·15·16 with a pontique on 15 | Delete it | All three rows go, and the travée with them — never a half-drawn bar | `BridgeGroupId`; one bridge mark per tooth |

## 14b — Annuler un rendez-vous

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| DEL-16 | 0 | a booking that **materialised** a 3-séance `Draft` treatment | Cancel it | The treatment **survives** (the patient still needs the crown) and its step returns to « à planifier » — `TreatmentPlanWorkflowProjection.IsLive` excludes a cancelled booking | verified in `TreatmentsInProgressReader.cs:48` — the reader asks the rule rather than re-stating it |
| DEL-17 | 0 | DEL-16 | Read « Traitements en cours » and the devis workspace | **Both** say « à planifier » for that step — they must not disagree about the same visit | the reader's own stated reason for existing |
| DEL-18 | 1 | DEL-16 | Re-book that step | Bookable; no second treatment is created | `createdPlansRef` / `schedulablePlanItems` |
| DEL-19 | 1 | a booking with reminders queued | Cancel it | Unsent reminders **voided**; the post-visit review removed | `VoidForAppointmentAsync`, `CancelPostVisitReviewAsync` |
| DEL-20 | 1 | a **cancelled** booking | Reactivate it | Reminders re-enqueued; the step is booked again | `becameReactivated` |
| DEL-21 | 0 | a booking whose fiche is already recorded | Cancel it | ❓ Refused, or the fiche stands and the money with it — a cancelled visit that produced a note is a contradiction somebody must own | — |
| DEL-22 | 1 | a booking carrying a **negotiated** `AgreedCost` | Cancel, then reactivate | The negotiated price survives both | `SetProcedures` replaces the whole list |

## 14c — Défaire l'argent

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| DEL-23 | 0 | an échéance payment of 250 | Void it | Off la caisse **and** off the échéancier; the plan's outstanding rises by 250; « Créances » and « Solde patient » both follow | `VoidInstallmentPaymentCommand` — « this was never received » |
| DEL-24 | 0 | DEL-23 | Read the day's caisse total | It **falls** by 250 — a voided row is not merely flagged | one authority |
| DEL-25 | 0 | an issued note with a live payment | Cancel it | **Refused** — `Invoice.CanCancel` excludes an invoice holding a non-voided payment | and this is why the avoir is the only remedy |
| DEL-26 | 0 | an issued note with **no** payment | Cancel it | Cancelled; number and lines kept; « Créances » drops it; the séance may be re-billed from « Facturer cette intervention » | `IsAutomatic = supersedesInvoiceId is null` |
| DEL-27 | 0 | a note bridged to a plan, then cancelled | Read every money surface | The plan comes **back** into « Créances » and « Solde patient » — `BilledPlanIds` drops only a *live* note | the bridge is all-or-nothing **in both directions** |
| DEL-28 | 0 | a **Draft** note | Delete it | Deleted, not cancelled; no number consumed; nothing else moves | « un brouillon se supprime » |
| DEL-29 | 0 | a full avoir over a note | — | The note is `IsSpent`; the work is correctable; the plan it bridged is **still** dropped (the note exists) | `IsSpent` ≠ `RepresentsItsPlan` |
| DEL-30 | 1 | a voided payment | Void it again | Refused, or inert — never a second reversal of one row | — |

## 14d — Défaire le clinique

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| DEL-31 | 0 | a stepped act with 2 of 3 séances done | « Détacher la fiche » | The **last** done step alone is released; the act reads « En cours · 1 sur 3 faite »; the dialog and the toast say **which séance** was released — never a flat « Prévu » | `detachOutcome`; `TreatmentPlanItem.Unmark` undoes by rank |
| DEL-32 | 0 | DEL-31 on a fiche a **live** note bills | Detach | Refused with `Snapshot.Remedy` — the avoir for the whole remaining amount, or cancel | a refusal whose remedy is unreachable is worse than none |
| DEL-33 | 0 | DEL-32, after the full avoir | Retry the detach | **Succeeds** | `IsSpent`, not `RepresentsItsPlan` alone — the guard that survived the avoir it demanded |
| DEL-34 | 0 | DEL-31 | Read the `dentalRecordId` the row offers next | It points at the **same** fiche, captured **before** the call — not « Enregistrer la fiche », which opens a *second* fiche for one visit | the pointer the detach clears is the only one the devis had |
| DEL-35 | 1 | a charted tooth condition | Remove it from the odontogramme | Removed; any act that asserted it is untouched | `RemoveToothConditionCommand` |
| DEL-36 | 1 | a diagnosis « à traiter » | Remove it, then record the treating act | Nothing is double-cleared and nothing errors | — |

---

# HP-15 · **Arrêter** — `Stopped`, the status the catalogue barely had

> `TreatmentPlanStatus.Stopped` exists because « Arrêter » used to write `Completed`, so a stopped treatment
> wore the badge « Terminé » and « Reprendre » was unreachable everywhere. It is **closed clinically and open
> financially** — the delivered work is owed. That pair is why it needs its own section: every money read and
> every worklist has to classify it, and a status neither `IsLive` nor `CarriesDebt` names drops out of both
> with no error.

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| STOP-01 | 0 | an `InProgress` plan, 2 of 5 acts done, 400 DT outstanding | « Arrêter le traitement » | Status `Stopped`; the 3 unrealised acts are **parked**; `TotalPlanned` re-spreads to the delivered work | `Stop` |
| STOP-02 | 0 | STOP-01 | Read « Créances » and « Solde patient » | The delivered work is **still owed** — `CarriesDebt(Stopped)` is true | closed clinically, open financially |
| STOP-03 | 0 | STOP-01 | Read « Traitements en cours » and the recall worklist | **Absent from both**; not reported abandoned | `IsLive(Stopped)` is false |
| STOP-04 | 0 | STOP-01 | « Reprendre le traitement » | Back to `InProgress`/`Accepted` **with its number**, and the parked acts **restored** | `Reopen` restores before it asks — and deliberately does **not** consult `StatusFollowsTheWork` |
| STOP-05 | 0 | STOP-01 | « Détacher la fiche » on one of the delivered séances | It stays `Stopped` and « Reprendre » stays on the header | ⚠️ the recorded defect: it wrote `InProgress`, withdrew « Reprendre » in the same breath, and left the parked acts outside `ActiveItems`, `TotalPlanned` and every count with **no route back** |
| STOP-06 | 0 | STOP-01 | Save a protocol on one of its acts (`SetItemSteps`) | Same — `StatusFollowsTheWork` is consulted by **three** writers, and this is the save button of the dialog hosting the step-level « Détacher » | the third writer is the one that hides |
| STOP-07 | 0 | a **followed** (`Draft`, un-numbered) treatment | « Arrêter » then « Reprendre » | It comes back a **`Draft`** — not an `Accepted` devis with a null number and a live créance for a total nobody quoted | `OpenStatusFromWork` keys on `Number is null` |
| STOP-08 | 1 | a `Stopped` plan | Try to add a séance | Refused — the aggregate refuses a new séance on a closed treatment | — |
| STOP-09 | 1 | a `Stopped` plan | Record an échéance payment | ❓ Allowed (the money is owed) or refused (« le plan doit être accepté ») — the two rules disagree on their face, and `MONEY-28` asserts the refusal for a non-accepted plan | the pair that has to be settled |
| STOP-10 | 1 | a `Stopped` plan | Read the dashboard's « Devis en attente de réponse » | Not counted | `CountUnansweredDraftsAsync` |
| STOP-11 | 1 | a `Stopped` plan with a booked future séance | — | ❓ Is the booking cancelled, kept, or left claiming a step of a stopped plan? | — |

---

# HP-16 · **Inachevé** — the visit that never happened, the quote nobody answered

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| INACH-01 | 0 | a booking whose slot has passed, no fiche | Wait for `AppointmentProgressJob` | `AwaitingClosure` — « séance passée », the presence question and nothing else | a `Completed` visit can still legitimately be « à clôturer » |
| INACH-02 | 0 | INACH-01 | Read « Traitements en cours » | The step still counts as **booked** — an `AwaitingClosure` visit is a standing booking, and there is **no « from today » floor** | the subquery must answer exactly what the reader answers, or the list groups by one rule and sorts by another |
| INACH-03 | 1 | INACH-01 | Read « À clôturer » | Listed, with what it still owes: a fiche, an encaissement | `VisitClosureRules` |
| INACH-04 | 1 | INACH-01 | Mark « rien à facturer » | Leaves the worklist claiming nothing about the clinical record | `MarkNothingToBillCommand`; « une séance quitte la liste sans rien prétendre » |
| INACH-05 | 1 | several elapsed visits | « Ne plus suivre ces visites » | `DisregardVisitsCommand` — they leave, the money is untouched | — |
| INACH-06 | 0 | a `Draft` devis presented 20 days ago, never answered | Read the recall worklist and the dashboard | « devis présenté, jamais répondu » — **only for a numbered quote**. A **followed** treatment older than 14 days must not appear | `Accept` is the only writer of `Number`; the identical premise was fixed in `RecallWorklistRules` and missed in `CountUnansweredDraftsAsync` |
| INACH-07 | 0 | a 6-step implant whose step 3 was done 90 days ago, nothing since | Read the recall worklist | Reported as stalled — **and only because the interval is intact** | the 4-argument copy: a wiped `MinDaysAfterPrevious` made a healthy implant read as abandoned |
| INACH-08 | 1 | a continuation started (`ContinueRecordedActCommand`), its second séance never booked | Read every surface | The devis exists and owes its remainder; the act is offered as bookable; it is **not** offered as continuable again | `ContinuationTracking` counts an act on any non-cancelled plan as taken |
| INACH-09 | 1 | a fiche saved with **no act at all** | Save | Allowed (a visit may have no act); no note; the visit leaves « à documenter » | `FICHE-29`'s twin on the empty side |
| INACH-10 | 1 | a patient with a live treatment who never returns | Read the patient file | The treatment is at the head of the file, with its next step named | `49743aba` |

---

# HP-17 · **Refaire** — the second time

| Id | T | Pre | Do | Expect | Defends |
|---|---|---|---|---|---|
| REDO-01 | 0 | a crown on 16 recorded and charted | Record a **second** crown on 16 months later | Both fiches stand; the odontogramme shows the **newest**; the historique shows two | one bridge mark per tooth, the newest, resolved **before** grouping |
| REDO-02 | 0 | a bridge on 14·15·16, then redone | Record the second | Two `BridgeGroupId`s, **one** travée drawn — never a merged five-unit bar | the adjacency scan must not re-merge grouped teeth |
| REDO-03 | 0 | a séance billed, its note cancelled, then re-billed | Read the patient's money | It legitimately has **two** notes; the **live** one speaks for it; « Solde patient » counts it once | `LoadAsync`'s « a live note beats a cancelled one » |
| REDO-04 | 0 | an act already carried by a devis | Continue it a **second** time | Refused or excluded — an act already on a devis raises no second offer | else a second devis over the same work |
| REDO-05 | 1 | a treatment `Completed`, then `Reopen`ed, then completed again | Read « Traitements en cours » and the dashboard | Correct after each transition; no row stuck from a previous state | — |
| REDO-06 | 1 | the same patient booked twice the same day | Record both fiches | Two visits, two fiches, two notes; la caisse shows both on that day | — |
| REDO-07 | 1 | « Ajouter un acte » pressed twice quickly on a fiche | — | **One** blank card — a trailing blank *is* the card being asked for | `FICHE-05` |

---

## Not implemented — do not assert

| Thing | Status |
|---|---|
| ~~`DentalRecordAct.IsUnfinished`~~ | **SHIPPED 2026-09-11** — entity, DTO, `GetUnfinishedActsQuery`, act-card control and migration `20260911161617_AddDentalRecordActIsUnfinished` all present. This row said « spec only » for three days after it stopped being true, which is the failure mode a « do not assert » list has: nobody re-reads it. CONT-16…21. |
| Any AI / inference surface | Deleted. `features/adoption-qa-i-access-control-and-audit/notes.md`. |
| `Auth:Mode=Cloud` / `CloudBrowser` | Retired. |

## Known catalogue artefacts on the dev database

Not defects — do not report them as such: `« Facette »` **and** `« Facette (par élément) »` both exist with
different protocol shapes, and `« B2-Test-Protocole »` is a test row.

---

## Coverage count

| Path | Scenarios | T0 |
|---|---|---|
| HP-1 Types de procédure | 14 | 3 |
| HP-2 Créer un RDV | 48 | 14 |
| HP-3 Modal RDV | 14 | 5 |
| HP-4 Sous-étapes (×4 places) | 16 | 6 |
| HP-5 Fiche (simple · sous-étape · bridge) | 44 | 27 |
| HP-6 Éditer une fiche | 19 | 11 |
| HP-7 Page traitement | 30 | 17 |
| HP-8 La suite d'un acte | 16 | 8 |
| HP-9 Clôturer | 12 | 6 |
| HP-10 Argent | 30 | 19 |
| HP-11 Odontogramme | 8 | 2 |
| HP-12 Historique | 8 | 2 |
| HP-13 Transversal | 9 | 5 |
| **Total** | **268** | **125** |


---

# Appendix · the probe traps this pass walked into

**Nine of the first fifteen failures were the measurement, not the product.** Every one produced a convincing
false defect report. They are listed here because the next batch of scenarios will meet the same ones — and
each is also recorded at its call site in `e2e/`.

| Writing this | Measures nothing / the wrong thing | Because |
|---|---|---|
| `chart.items ?? []` over `/odontogram` | **passes vacuously** | it is a **bare array**; there is no `items`, so every lookup answers `null` and « nothing is charted » is always true |
| `?fromDate=…&toDate=…` on `/billing/caisse*` | silently returns **today** | the parameters are `fromDay`/`toDay`; **an unknown query parameter is ignored, never refused** |
| `ledger.items` | empty | the field is `movements` |
| `treatmentPlanItemId` alone on a booking | « Le plan de traitement est requis pour lier l'acte. » | `treatmentPlanId` is required beside it. ⚠️ **The same sentence the real `plan.items[0]` defect produces** |
| `appt.treatmentPlanId` | `undefined` | **no such field** — not on the entity, the DTO, or the table. One-treatment-per-booking is derived from `TreatmentPlanItemId` |
| counting price inputs in the fiche | measures **how many cards are open** | only the *armed* card renders an input; a collapsed one shows a figure |
| `getByText(/Traitement en \d+ séances/)` | not found | the string carries a **non-breaking space** and is split across spans — read `textContent` and normalise `\u00a0` |
| `Tout faire en une séance` | not found | the label is « Tout faire en une **seule** séance » |
| any assertion after the session dies | **green on the login page** | it is short, does not scroll, and raises no console error — so it satisfies most checks. **Prove you are in the app first** |
| hard-coding a tarif, a step label, or an act name | green on one database, red on another | the developer's catalogue is hand-edited; a freshly seeded one has different tarifs, different protocols and different step names. Derive from the catalogue (`procedureWhere`, `protocolAct`, `singleSeanceAct`) |
| a `next dev` server left up for hours | eight tests fail as product crashes | it degrades — measured at 11½ hours, serving `Jest worker encountered 2 child process exceptions` on every route |

## Round two — the traps the second batch walked into

Added while coding the tier-0 batch of 2026-09-08 (bridge charting · money boundaries · booking · concurrency).
Every one produced a red test against correct product code.

| Writing this | Measures nothing / the wrong thing | Because |
|---|---|---|
| waiting for `/Chargement/i` to disappear | **resolves instantly on a page that is still loading** | `AppLoader` shows one of **ten rotating French messages** — « On règle le fauteuil… », « On détartre la base de données… » — and its literal « Chargement… » is an `sr-only` span rendered only when no `label` is passed. Wait for the app shell's **navigation landmark** instead: a positive signal that exists only once the shell has mounted |
| `page.locator("[data-slot='dialog-content']").first()` | **silently re-binds to whichever dialog is open** | it made a *successful* booking look refused: the create dialog had closed and the locator had latched onto the « Compte rendu de visite » popup that fires the instant a past-dated visit is saved. Scope by the dialog's own **title** |
| a fixed *sequence* of confirmations | the wrong one is answered, or none is | which prompts fire is a fact about the **calendar**, not the scenario. Drain them in a loop and assert the expected one was **among** those seen (`lib/dialogs.ts`) |
| any first click on an app route | **intercepted by an invisible overlay** | the post-visit review prompt is modal, and on a practice with a backlog it is a *queue*. Playwright reports « `dialog-overlay` intercepts pointer events » and retries for the whole timeout, which reads as « the button is missing ». Dismissed through the product's own « Plus tard », which snoozes the **whole queue** |
| counting price inputs to check a locked act | measures **how many cards are open** | only the *armed* act card renders an input; a collapsed one shows a figure |
| `getByRole("button")` on the dialog's mode toggles | not found | « Patient existant » / « Nouveau patient » carry a **tab/radio** role, not `button` |
| a long browser run against a default-configured API | 429s in its tail, then a cascade of « element not found » | the auth window is **30 attempts per five minutes per account** and the app exchanges a token on **every navigation**. `RateLimiting__Auth__PermitLimit` etc. exist for this — see `e2e/README.md` |
| booking at a past time to force a confirmation | **also** raises the review prompt afterwards | a past-dated visit is « terminée » on arrival. Correct behaviour; drain it or it sits over the next test |

### And one that was not a probe bug at all

**A model-only column halts every fiche save, and the unit suite cannot see it.** On 2026-09-08 an in-flight
change added `DentalRecordAct.ImplantPilierToothNumbers` and `ToothState.BridgeGroupId` — entity, DTO, parser
and EF configuration all present, **no migration** — and `POST /patients/{id}/dental-records` answered
`42703: column "ImplantPilierToothNumbers" of relation "DentalRecordActs" does not exist`, flattened to
« Erreur lors de l'enregistrement de la fiche de soins. Veuillez réessayer. »

**4307 unit tests were green throughout.** That is the gap `api/CLAUDE.md` names in as many words — nothing in
`UnitTests` touches a database — and it is why the `e2e` job exists. ⚠️ `verify-schema` caught
`ToothStates(BridgeGroupId)` and **not** `DentalRecordActs(ImplantPilierToothNumbers)`: its column list is
hand-maintained, so it drifts from the model it checks. Resolved by
`20260908122028_AddBridgeIdentityAndImplantPilier`.

### The one that bit three times — **an unknown query parameter is ignored, never refused**

Worth pulling out of the tables above, because it is the same mistake on three different endpoints and it is
**silent in all three**: the read succeeds, returns plausible-looking rows, and answers a different question.

| Endpoint | Wrong | Right | What the test then reported |
|---|---|---|---|
| `/billing/caisse*` | `fromDate` / `toDate` | **`fromDay` / `toDay`** | « the payment never reached la caisse » — it had returned *today's* window |
| `/appointments` | `fromDate` / `toDate` | **`startDate` / `endDate`** | — (caught before it cost a test) |
| `/patients` | `search` | **`searchTerm`** | « Found 0 — the patient was never created », about a patient created seconds earlier. All 266 patients came back in alphabetical order and the surname under test sat past the first page |

**The durable guard is to check that the answer is consistent with the question.** A dropped filter is invisible
in the response, so `ClinicApi.patientsNamed` now throws when *none* of the rows it got back plausibly matches
the term it asked for. That converts the silent version of this mistake into a message that names it.

### And the subtlest of the eighteen: **waiting for a dialog is not testing whether one is up**

A confirmation exists only after the server has answered, so `isVisible()` the instant after clicking
Enregistrer is a race the harness loses — and losing it is indistinguishable from the product failing to
prompt. It failed **four tests at once**, each with a screenshot showing « Créneau déjà occupé » on screen and
the form still open. `drainConfirmations` now *waits* for the first prompt (8 s, because a cold API is slow) and
2.5 s for each one after it.

⚠️ **The screenshot is what settled it, in this case and in five others.** `EDIT-06`'s showed « Modifié par un
collègue » sitting in the reloaded form — proof that « Recharger » had worked perfectly — while the test was
reporting that it had not. `.claude/rules/verification.md` § 2 says to `Read` the PNG rather than trust the text
dump; every one of these was diagnosed that way and none of them could have been diagnosed without it.
