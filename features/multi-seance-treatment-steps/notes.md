# multi-seance-treatment-steps — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
`README.md` is what was asked for; this is what shipped, after the audit pass of 2026-09-07.

---

## An échéance nobody agreed to is not late (`InstallmentLateness`, `Installment.IsAutoRaised`)

**Every devis in every database read « En retard » from the day after it was signed.** Measured on the dev
database: **25 of 27** unpaid échéances flagged, across every status — including devis a note d'honoraires
already billed (whose échéancier can no longer take money at all, as the same card says in as many words) and
devis that had been **cancelled**. A badge that fires on almost every row is a badge a practice stops reading,
which is exactly what had happened.

The cause was never the badge. `TreatmentPlan.Accept` raises **one lump-sum échéance for the whole total, dated
at the acceptance instant**, when the dentist supplied no schedule — and that row is a **ledger container, not a
promise**: a payment needs an échéance to attach to and `Outstanding` is derived from the schedule. Nobody agreed
its date. The create form already said so (« le total est dû à la signature… qui apparaîtra en retard dès
demain »), but the two paths that mint a devis *without* that form — « Éditer le devis » and collecting on a
treatment — had no warning and no way out.

⚠️ **The distinction had to be STORED, not guessed.** A rule like « one row, dated at acceptance » also matches a
schedule a dentist deliberately typed for the signature day, and silencing that one destroys the only thing an
échéancier is for. Hence `Installment.IsAutoRaised`, false by default so every row a human enters is an agreed
date without any caller saying so, and set by the **two** system writers only (`Accept`, `RespreadSchedule`).
⚠️ `Revise` **clears** it — revising is a dentist looking at the dates and settling on them — which is why
`RespreadSchedule` re-marks the rows it trims: trimming a collected row to what it took agrees to nothing.

`InstallmentLateness.IsLate` is the one rule, and it needs five facts a row does not have: the plan's status
(through `PlanBillingRules.CarriesDebt`, so Draft and Cancelled fall out for free), whether a note represents the
plan, whether any act is still unrealised, the clinic's own calendar day, and whether the date was ever agreed.
**Served as `InstallmentDto.IsOverdue`** — the client cannot answer it, and the two surfaces that used to
(`plan-workspace`'s table and its card list) each wrote `isBeforeToday(dueDate)`.

⚠️ **The « unrealised work » term applies to an auto-raised row ONLY.** The dentist's own rule and the right one:
an act billed once and delivered over six visits is paid as the visits happen, so a balance on a treatment still
under way is not yet due. But a schedule somebody typed says when the money is due *whatever* the clinical state
— `InstallmentLatenessTests.An_Agreed_Date_Is_Late_Even_With_Work_Outstanding` is what stops the fix widening
into « nothing is ever late ».

**The migration's backfill is the half that matters** — without it the column only starts meaning something for
devis accepted after the deploy. Its discriminator is exact rather than heuristic: `Accept` writes
`AcceptedDate.Value` verbatim into the row, so the two timestamps are byte-identical, while every hand-entered
échéance comes from a `YYYY-MM-DD` field and lands at midnight. Three clean groups on the dev database, no
overlap. Deliberately conservative — a row it misses stays « agreed » and can still go red, which is at worst
yesterday's behaviour; claiming a typed date was auto-raised is the error nobody can see.

---

## An act's end state is charted when the act is FINISHED (`ToothChartingRules`)

**The odontogram asserted finished work from the first séance of every multi-séance act.** `ToothCondition`'s own
documentation draws the line — a restoration « records work already done » — and
`DentalRecordActParser.BuildToothStates` wrote one on every fiche save with no notion of whether the act was
over. Measured on the live database, seven rows, **every one written from a step 1 of 2**:

| Patient | Teeth | Charted | From |
|---|---|---|---|
| Nadia Jelassi | 16, 36, 46 | **Implant** | step 1 / 2 |
| Karim Hamdi | 13, 43 | **Extrait/absent** | step 1 / 2 |
| Leila Gharbi | 26, 27 | **Obturation** | step 1 / 2 |

Three teeth claiming implants that did not exist; two recorded as *gone* while the extraction was half done. No
error anywhere — the chart simply described a mouth the patient did not have, for the length of the treatment.

⚠️ **Its twin is `DentalRecordLinker.ClearDiagnosesForTreatedTeethAsync`, and the two had to move together.** That
helper deletes the open « à traiter » diagnosis on every tooth the new states name, so séance 1 both claimed the
work was finished *and* forgot it had ever been needed. It is driven off the states this rule produces, so
withholding them withholds the deletion in the same breath — which is why `ChartableActs` returns a filtered
**act list** rather than filtering the states afterwards.

**Both commands now link the plan step BEFORE charting**, because only the aggregate — after the step is marked —
can say whether the act is finished (`DentalRecordLinker.PlanActLink.ItemIsComplete`). Never derive it from the
step's rank: one séance can close two steps, a protocol can be re-cut mid-treatment, and an act booked whole has
no steps and finishes on its first fiche.

⚠️ **`PlanCarriedAct` was extracted for this**, not for tidiness. « Which act of this fiche does the devis carry »
is asked by the fee rule (`PlanCarriedActPricing`, which imposes 0) and now by this one; a re-typed copy prices
the implant and charts the filling. A mixed séance — an implant's step 1 plus a filling done the same day — is
the case that makes it visible, and `ToothChartingRulesTests` pins it.

---

## A séance remembers the teeth the last one treated (`TreatedToothNumbers`)

The fiche's chart selection was prefilled from the **devis line**'s teeth, and a devis line is very often an
« acte général » with none — so séance 2 of an implant opened on a blank chart. Measured on a real implant: its
three fiches recorded the teeth **once between them**, on whichever séance the dentist happened to fill in.

⚠️ It stopped being a convenience the moment the odontogram fix landed: the chart is written when the act
**finishes**, so teeth entered early and absent from the last fiche would chart **nothing at all**. The two
changes are one change and must not be separated.

Derived, never stored: `IDentalRecordRepository.GetTreatedTeethAsync` over the fiches linked to the act's steps
**and** to the act itself — the step link is the important half, since a stepped act takes its own
`LinkedDentalRecordId` only when its last step lands.

---

## A séance says what it WAS, not just which act it belonged to

Three fiches of one implant read « Implant dentaire · Implant dentaire · Implant dentaire » in the patient's
history — which says the patient had three implants — with nothing saying the three were one treatment. The step
label and its rank were on record and never read back. `DentalRecordPlanLinkRow` carries them now, and the row
reads « Implant dentaire / Pose de l'implant · étape 3 / 6 ».

⚠️ **The money cells key on the LINK, not on money having moved.** Branching on `collectedOnTreatment > 0` left a
séance that collected nothing reading « 0,000 DT · 0,000 DT » — indistinguishable from an ordinary free visit
while the treatment showed 1 500 DT outstanding — and on a six-visit implant most séances collect nothing. Zero
renders as « — » with the treatment badge kept, for the reason « Reste » already did.

---

## The header is one action and a menu

Seven controls of equal weight, measured identical at 320 / 390 / 820 / 1180 / 1440: « Facturer le devis ·
Modifier le devis · Arrêter le traitement · Terminer · Devis PDF · Envoyer par e-mail · Annuler ». Two of them,
side by side on a followed treatment, were **« Éditer le devis » and « Modifier le devis »** — near-identical
French for minting a gapless numbered financial document and for correcting an act's price. The file already
argued against a « second door » for `Accepter`/`Éditer` and then grew one when `canAmend` widened to Draft.

`primaryAction` derives the one act from the state (mint → bill → resume); everything else is in a « ⋯ » menu,
the pattern the plans list and the échéancier already use. The amend action is **« Modifier les actes et les
prix »** — named for what it edits.

⚠️ `primaryAction` is a `useMemo` that RUNS DURING RENDER and calls the `const` confirm openers, so it must be
declared **below** them. Placed above, every `Completed` plan threw « Cannot access 'confirmReopen' before
initialization » and the workspace failed to render entirely — invisible to `tsc`, to `check:responsive` and to
the build; only the browser walk saw it.

---

## Two dead controls on a followed treatment

- **« Annuler » was refused every time.** `isActive` is `isPlanLive`, which includes `Draft`; `TreatmentPlan.Cancel`
  throws « Un brouillon se supprime, il ne s'annule pas. » on exactly that status. Browser-proven end to end: the
  motif filled in, the confirmation pressed, the server refusing — and the remedy the sentence names lived only on
  `/treatment-plans`. A devis is cancelled because its **number** must be accounted for; a treatment that never had
  one is deleted or stopped.
- **« Supprimer le brouillon » destroyed recorded séances.** `CanBeDeleted` asked only the status, and the cascade
  takes the acts and their step rows (`DeleteBehavior.Cascade`) while the appointments' links are `SetNull` — so
  the fiches survive attached to nothing, the exact wreckage `StopTreatment` was written to avoid. The dialog
  reassured: « Aucun numéro n'a été consommé ; cette action est irréversible. » `RemoveItem` had refused
  `HasDeliveredWork` **per act** all along; the whole-plan question was simply never asked.

---

## The dashboard counted treatments as unanswered quotes

`DashboardAlertsReader` counted `Status == Draft` for « Devis en attente de réponse ». Since « Suivre ce
traitement » an un-numbered plan is usually a treatment being carried out right now, so the figure put this
week's séances under a heading inviting the practice to chase patients with nothing to answer — a Draft has never
been handed a devis, `Accept` being the only writer of `Number`. `CountUnansweredDraftsAsync` narrows it to
Drafts with no recorded work. ⚠️ The identical premise was already corrected in `RecallWorklistRules.IsUnanswered`
and this copy was missed — the `fixes-dont-propagate` shape, one layer over.
