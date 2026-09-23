# fiche-act-edit-propagation — shipped notes

« Quand on modifie l'acte d'une fiche liée à un rendez-vous, l'acte reste l'ancien. »

Three defects behind one report, plus two the browser pass found in the fix itself. What follows is what
shipped and the decisions that are easy to undo by accident.

## The fiche itself was never the problem

`DentalRecord.SetActs` replaces the whole act list from the payload, so a saved fiche cannot keep an old act.
Everything below is what **failed to follow** it.

## 1 · Rouvrir une séance ouvre SA fiche, elle n'en compose pas une deuxième

`openVisitRecord` cleared `editingRecord` and threaded the appointment id, so pressing it on a visit that
already had a fiche composed a **new** one prefilled from `appointment.procedures` — i.e. the act that was
*booked*, not the one that was *recorded*. Saving it produced a second fiche for one visit; five appointments
on the dev database already carry 2 to 4.

`ficheForVisit(appointmentId)` is the one answer, read by both buttons (whose labels follow: « Ouvrir la
fiche » vs « Enregistrer la fiche ») and by a deferred effect that covers the
`?addRecord=1&appointmentId=…` deep link — that one fires on mount, before the phase-2 batch has delivered
the fiches, so the decision cannot be taken there.

⚠️ **The button half is nearly unreachable and the deep link is the real door.** `canRecordVisit` already
excludes `Completed`, and saving a fiche marks the visit Completed, so the two buttons are withheld for a
documented visit anyway — the label change only shows when `MarkVisitCompleted` (post-commit, best-effort)
did not land. The deep link checks nothing, which is how appointment `8d8f4328` got its two fiches, and it is
what the browser pass verified.

⚠️ **`Appointment.SetProcedures` is deliberately NOT called.** The booking records what was *agreed* — the
act, `AgreedCost`, `TreatmentPlanItemId`, `TreatmentPlanItemStepId` — and rewriting it from the fiche would
destroy the devis link `TreatmentsInProgressReader` and `PlanBookingRelease` read, and the negotiated price.
If the agenda should show what was *done* rather than what was booked, that is a read-layer change.

## 2 · Les actes d'une fiche facturée ne changent plus en douce

`DentalRecordBillingGuard.Check` compared `proposedCost` against the note's billed total **and nothing else**,
on the stated grounds that `SetActs` regenerates every act id so there is no before/after identity to diff.
True of the ids, and the wrong question: the note's identity is its **lines**. Swap « Détartrage » for
« Gingivectomie » at the same 60 DT and the guard passed, the fiche was rewritten, and the numbered document
went on billing an act the séance no longer records — with a green toast, and this guard's own refusal
(« les actes … ne peuvent plus être modifiés ») printed nowhere.

`Check` now takes `actsBefore`/`actsAfter` — `DentalRecordInvoiceLines.For(record)` on each side of the edit,
the one authority on how a fiche becomes lines — and refuses with the existing `ActsChangedCode`, which is in
`Correctable`, so « Corriger la note » opens rather than a dead end.

⚠️ **The comparison is between the two sides of ONE EDIT, never against the note's stored lines.** That was
the first shape and it is wrong in production: a note raised before `DentalRecordInvoiceLines` existed, one
edited by hand, or a legacy fiche billed from its `ProcedureType` summary all carry text this class would no
longer compose — so re-dating such a séance, **changing nothing**, would be refused for ever. It reddened
three committed `DentalRecordCorrectionTests` within minutes of being written (a peer session spotted it), and
`A_Note_Whose_Stored_Lines_Differ_Does_Not_Refuse_An_Unrelated_Edit` is that regression pinned. **Do not
re-try it.**

⚠️ Lines are compared as a **multiset**: `Invoice.Lines` has no ordering configured, so an order-sensitive
test would refuse an edit that changed nothing.

## 3 · Une fiche ne peut plus prétendre porter un acte du devis qu'elle ne contient pas

« Changer d'acte » on a devis-carried card rewrote the act and left `TreatmentPlanItemId` pointing at the line
the fiche was opened on. Three things followed, none of them an error:

- `PlanCarriedAct.IndexIn` answered `-1`, so **no 0 was imposed** and `ToothChartingRules` withheld nothing;
- `DentalRecordLinker` marked the **old** act's step done against a fiche that does not record it;
- the card kept `billedOnPlan`, so the price field stayed hidden and the new act saved at **0 DT**.

Three fixes, one per layer:

| Where | What |
|---|---|
| `use-session-acts.ts` | changing the act **releases** the card — `billedOnPlan`, the lock and the 0 all go, and the catalogue tarif appears. `useFreeText` clears it too |
| `patient-record-modal.tsx` | the save is refused before the round trip, naming « Acte planifié » as the remedy; `planItemPrefill` now carries `procedureTypeId` |
| `PlanCarriedActPricing.ImposeAsync` | returns `Imposed(Acts, Refusal)`; both fiche commands read the refusal |

⚠️ **`PlanCarriedAct.NamesAnActTheFicheDoesNotHold` is narrower than « IndexIn returned -1 », and every clause
of the narrowing is load-bearing.** `-1` has two innocent causes: a devis line naming no catalogue act, and a
fiche whose act is itself hand-typed. The second is not hypothetical — **every** fiche opened on a devis step
before `planItemPrefill` carried a `procedureTypeId` recorded a free-text act (live row `a0b5d12d`,
« Bridge — scellement », `ProcedureTypeId` NULL, on a séance booked for « Couronne / bridge (par élément) »);
41 acts on the dev database have a null id, 16 of them on a plan-linked fiche. Refusing those would make an
old plan fiche impossible to re-save to fix a typo. **Production has 0**, so the prefill hole is latent there.

⚠️ `PlanCarriedActMismatchTests.Every_Caller_Of_ImposeAsync_Reads_Its_Refusal` scans `api/` with comments
masked and fails on a third caller that takes only `.Acts`.

## What the browser found that nothing else could

Both of these were **in the fix**, and the whole mechanical gate was green while they were live.

- **« Aucun » was unselectable in « Acte planifié » on any reopened devis-carried fiche.** The
  record-hydration effect had `linkedPlanItemId` in its deps with `if (… !== NO_PLAN_ITEM) return`, so
  choosing « Aucun » re-fired it and put the devis act straight back. Pre-existing and merely annoying —
  until fix 3's refusal named that control as the remedy, at which point the fiche became **uneditable** with
  the message telling the dentist to do something the form would not let them do. That is the shape
  `DentalRecordBillingGuard.Snapshot.StillBillsTheWork` was written to remove, one screen over. It hydrates
  once per open now, tracked in a ref keyed on the record id.
- **« Enregistrer — 30,000 DT » above a card reading « Aucun honoraire sur cette séance ».** Re-picking the
  **same** act on a carried card fell through to `pt.defaultCost`, because `actFromDto` sets
  `unitCostLocked: false` on every reopened act (deliberately: « whether the stored amount was typed or taken
  from a tariff is not recorded »). The price field is withheld on such a card, so the only place the figure
  surfaced was the primary action. `PlanCarriedActPricing` imposed the 0 server-side, so no stored money was
  ever wrong — the screen simply contradicted itself on the one control the dentist presses.
  `check:responsive`'s **N44** requires `applyProcedure` to read `.billedOnPlan` **twice** (the release test
  and the pricing test); ⚠️ its first version asked only whether the identifier appeared and **passed a
  deliberate violation**, satisfied by the release's own `billedOnPlan: false` assignment.

- **The refusal was 19 px below the fold at 320 px, and the toast had already gone.** `refuseSave` scrolls to
  the acts pile, which is right for every pre-existing refusal — they all name an **act key**, so the message
  renders *on a card* the scroll centres. The devis-mismatch refusal names none, and its banner renders
  **after** the pile. Measured: banner at y 456 in a scrollport ending at 437, `toastPresent: false` — so the
  press looked like it had done nothing, which is exactly the class of defect this feature removes. The
  pile-level banner now has its own ref and is the scroll target when no card is named, deferred one frame
  because it does not exist until the state has rendered. ⚠️ The general form is worth keeping: **in this
  modal, a refusal that names no card needs its own scroll target.**

## Where things live

| | |
|---|---|
| Blast radius | `blast-radius.md` |
| QA | `qa/plan.md` · `qa/run-1.md` (RED, 2 findings) · `qa/run-2.md` (GREEN) · `qa/shots/` |
| Server | `DentalRecordBillingGuard` · `PlanCarriedAct` · `PlanCarriedActPricing` · both fiche commands |
| Browser | `use-session-acts.ts` · `patient-record-modal.tsx` · `app/patients/[id]/page.tsx` |
| Guards | `DentalRecordBilledActsUnchangedTests` · `PlanCarriedActMismatchTests` · `check:responsive` N44 |
