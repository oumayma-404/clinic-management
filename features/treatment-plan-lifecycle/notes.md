# treatment-plan-lifecycle — shipped notes

What the 61-finding remediation actually changed, and the decisions that are easy to undo by accident.
`spec.md` is what was asked for, `plan-remediation.md` is how it was built, `reviews/feature-review.md` is the
challenged review it answers, and `qa/` is the browser pass that closed it. This is what shipped.

Seven capabilities (S1–S7) landed with the 61 fixes, and two of them appended enum members — read
[« Two appended statuses »](#two-appended-statuses-and-why-appended-is-load-bearing) before touching either.

---

## A stop has THREE outcomes, and the browser knew two

`TreatmentPlan.StopTreatment` sorts a stop into **cancel** · **stop** · *refuse until the cash is refunded*.
The third arm fires on a **numbered** devis with money taken and **no act realised**, and it says so by name:

> Aucun acte de ce devis n'a été réalisé, mais 200,000 DT y ont déjà été encaissés. Remboursez-les par un
> avoir avant de clôturer ce devis.

That arm is deliberate — its own comment records why: `StopWouldCancel` now answers false for a devis carrying
a deposit (C2), and `Cancel` refuses live money outright (C1, `EnsureNoLiveMoney`), so routing the dentist to
the cancellation would « name a remedy the product would then refuse, which is the defect shape this audit
found four times ».

⚠️ **It was that shape, one door over.** `stopWouldCancelPlan` mirrored the money term faithfully and nothing
then asked whether the stop it routed to could land — so the ⋯ menu offered « Arrêter le traitement », the
dialog listed the acts under **MIS DE CÔTÉ**, stated « le traitement passe à « Arrêté » » and showed
« L'échéancier est ramené au total conservé (0,000 DT) », and the press came back refused with the devis still
« En cours ». Found by the browser pass (`qa/run-1.md` F1), on two independent plans.

**`stopNeedsRefundFirst(plan)`** is the browser's mirror of that third arm, and the stop dialog now states the
money and the avoir **before** the press, with « Retour » as the only control.

- ⚠️ **A named refusal, never a withheld control** — M26's rule (« pourquoi Encaisser a disparu »). A dentist
  who cannot find the button learns nothing; one who reads « remboursez d'abord les 200,000 DT par un avoir »
  knows what to do next. The ⋯ entry stays.
- ⚠️ **No confirm is rendered on that branch at all.** A button whose only outcome is a red toast is the
  « discover the rule by breaking it » shape the motif field one line below was already built to avoid.
- ⚠️ It mirrors the server **including `Number != null`**: an un-numbered treatment carrying money is not this
  case — there is no document, and `kept.Count == 0` is only refused on a numbered devis.
- ⚠️ « Cette action est irréversible » belongs to the **cancel** branch (C2). On the refund branch nothing
  irreversible happens — it is a refusal — so what that dialog owes is the remedy, not the word.

**Guard:** `check:responsive` **N40** fails any surface reading `stopWouldCancelPlan` without also reading
`stopNeedsRefundFirst`, and requires `plan-next-action.ts` to export all five money predicates. Comments are
masked on both sides — a first cut tested the raw source and the prose explaining the rule (« see
`stopNeedsRefundFirst` ») satisfied it, so the guard passed over a file with the call deleted. Proven red on a
deliberate violation, green after revert.

---

## Two appended statuses, and why « appended » is load-bearing

`TreatmentPlanStatus` gained **`Stopped = 5`** and **`WrittenOff = 6`**. The enum persists through
`HasConversion<int>()`, so slotting either where it belongs in reading order would repoint every stored row.
Append, never insert.

Each one has to be classified in **two** places where a miss is silent and expensive —
`PlanBillingRules.CarriesDebt` and `TreatmentPlanLifecycle.LiveStatuses` — and
`TreatmentPlanStatusCoverageTests` enumerates the enum and fails when a member is classified by neither.
`check:responsive` **N41** is the browser twin for the label/tone pair.

| | `Stopped` (« Arrêté ») | `WrittenOff` (« Créance abandonnée ») |
|---|---|---|
| Carries debt | **yes**, on its re-spread total | **no** — the practice has given the money up |
| Live | no | no |
| Tone | `negative` | `neutral` — nothing went wrong and no record is at risk |
| Way back | « Reprendre le traitement » | the same verb: `Reopen` admits it |

⚠️ **`Reopen` admits `WrittenOff`, and that is what stops it being a second absorbing state.** A write-off
entered on the wrong devis and a patient who turns up a year later with the money need the same thing — the
créance back — and the operation is identical, so it is that verb rather than a near-duplicate.

⚠️ **The write-off record is CLEARED on reopen, not kept.** Unlike a cancellation motif — which explains a
numbered document that may be in a patient's hands and stays true after `Uncancel` — `WriteOffAmount` is a
figure the practice reports as a loss, and leaving it on a plan that is collecting again double-counts the
year's pertes.

⚠️ **`canBillPlan` refuses `WrittenOff` too**: raising a note for a balance just abandoned would put the
créance straight back, on a second document, under a different number.

---

## One respread rule, and it is the only place the money invariant lives

`RespreadSchedule` is now the single owner of « `TotalPlanned` may never fall below what was collected »
(C7). It had lived on `StopTreatment` and `ReviseInstallments` and **not** on the branch the amend handler
takes when it changes a total without being sent a schedule — so removing a 200 DT act from a 500 DT devis
with 500 DT collected left `Σ Amount` at 500 against a `TotalPlanned` of 300, `Outstanding` clamped at 0, both
balances read 0, and **200 DT of the patient's money became unreachable with no error and no avoir prompt**.

⚠️ **`EnsureTotalCoversCollected` sits at the TOP of the method**, because the rebuild below it trims
collected rows to what they took — which would otherwise destroy the evidence the refusal is made of.

⚠️ **A collected row is kept and trimmed, never dropped, and `MarkAutoRaised` is re-applied.** `Revise` clears
`IsAutoRaised` (revising is normally a dentist agreeing a date) and trimming a row to what it actually took
agrees to nothing — without the re-mark, re-spreading silently promotes the auto lump-sum into an « agreed »
date and puts « En retard » back on it.

⚠️ **The visible consequence is two undated « Solde à régler » rows after a stop→reopen**, one of them a
settled receipt (100 / 100 / 0) and one the live balance (300). The arithmetic is right — `Σ Amount ==
TotalPlanned` holds, which is the invariant « Solde patient » and « Créances » agree only while it does, and
`reconcile-money`'s `plan-schedule-balances` is green. `installmentDueLabel` keys purely on `isAutoRaised`, so
both are titled the same. Recorded as `qa/run-1.md` F2 and **left alone**: the wording has one owner (the
three `installmentDueLabel`/`Sentence`/`Title` exports) and changing it is not this feature's call.

**`Reopen` re-spreads** (C11), and leaving it out had put two different balances on two screens: « Solde
patient » is `TotalPlanned − AmountPaid` while « Créances », the dashboard and `PatientDebtLines` sum
`Amount − AmountPaid` over the installment **rows** — so restoring the acts moved the first and not the
second. A 1 200 DT devis stopped at 400 kept and 400 collected reopened reading **800 DT owed on the patient's
file and 0 DT in « Créances »**, with `PatientDebtLines` then offering a payable room of 0 so the receptionist
could not take the money. ⚠️ Its caller passes `ClinicClock.ClinicToday()` — never `DateTime.Today`.

---

## The capabilities behind the driving complaint (S1–S7, M1/M2, M5, M6)

The report that started this was « removing an act is refused and the refusal's remedy is a three-deep chain ».
What was missing was not a looser guard but a way to say « cet acte ne se fera pas » that keeps the fiche
links:

- **Per-act « Mettre de côté » / « Remettre au devis »** (M1/M2). Refuses an act with delivered work and
  refuses the last active one; re-spreads and bumps the revision. ⚠️ The row control's accessible name is
  « Mettre *X* de côté — il sort du total, rien n'est supprimé »; its **visible** word is « De côté » and is
  hidden below `sm:`.
- **« Détacher la note d'honoraires »** (M3) — « détachez-la de ce traitement » is named by three server
  refusals and had no route: the detach's only callers were the cancel and the stop, so releasing a note meant
  killing the treatment.
- **« Changer de patient »** (M5) and **« Changer le praticien »** (M6). A devis on the wrong patient was
  unfixable; the praticien was invisible *and* permanent, and the note d'honoraires snapshots it — so in a
  two-dentist cabinet every dinar was attributed to the wrong person for ever.
- **« Rétablir ce devis annulé »** (C3, `Uncancel`) — requires a motif, **appends** it to
  `CancellationReason` rather than erasing the original, and leaves `Number` untouched so the series stays
  gapless.
- **S1 « Dupliquer ce devis »** → a new `Draft`, **no number**, acts and steps copied. It consumes no number,
  claims no money and changes nothing about the source, which is why it sits beside « Devis PDF » rather than
  in the destructive block.
- **S2 remise per act** — `TreatmentPlanItem.DiscountAmount`, ≤ `PlannedCost`, and **`TotalPlanned` sums the
  net**. That is what makes a remise reach the échéancier, « Créances », « Solde patient », la caisse and the
  note with nothing else learning the word; N40's fee half fails any surface still printing or summing
  `plannedCost`.
- **S3 « Régler le devis »** — one modal spreading a payment over the échéancier.
- **S4 « Passer la créance en perte »** — in the destructive block but **not** `variant="destructive"`: it
  destroys no record, keeps every encaissement and is undone by « Reprendre le traitement ». AdminOnly.
- **S5** states « pas avant le 3 juin » from `MinDaysAfterPrevious` and the booking dialog **warns, never
  refuses** — and never says « en retard », because an échéance the system raised is not a date anybody
  promised.
- **S6** the detach confirm offers « …et la rattacher à : [acte · étape] », one save.
- **S7** a **notice**, never a refusal, when the same `procedureTypeId` + tooth appears on two debt-bearing
  plans.

⚠️ **« Annuler le devis » is BACK, and that reverses `spec.md` AC-11 deliberately.** Folding it into
« Arrêter le traitement » was right for the common shape — the arithmetic decides, not the dentist — and it
left a numbered devis with one séance recorded, and every closed devis, with **no** route to an annulation
anywhere in the browser: `POST /{id}/cancel` was callerless. `canCancelPlan` offers it only where the stop
branch cannot reach.

⚠️ **« Facturer le devis » is in the ⋯ menu, which also deviates from AC-11's five-entry list.** `canBillPlan`
is deliberately wider than « is this plan active »: a plan auto-completes the instant its last step is
recorded, so gating on active withdrew the button at the exact moment the treatment became billable.

---

## Two rules that look wrong and are not

Both were written up as defects in the browser pass and both died on a source read
(`qa/run-2.md`, « plan rows wrong rather than the code »):

- **A `Completed` plan's filled primary is « Facturer le devis ».** m13's rule is narrower than it reads:
  « Reprendre le traitement » is a *capability*, so it belongs in the ⋯ menu and must not be the filled
  primary. Billing a finished treatment is exactly what the header should offer.
- **A stepless followed Draft's list row says « Modifier le brouillon », and that is correct.**
  `canUseDraftEditor` is `Draft && !planHasRecordedWork`. C8's fix is that a plan the **draft editor cannot
  take** — one with steps or delivered work, and every non-draft devis — now has an amend route in the list at
  all; before it, that door existed only inside the workspace, and the list's own button led to a refusal.

---

## Verification, and one thing it is structurally blind to

`qa/` holds the whole pass: `plan.md` (43 rows over four tiers, with the blast-radius table tier D comes from),
`walk.mjs` (the re-runnable driver), `run-1.md` (RED, 3 findings) and `run-2.md` (GREEN, 73 checks).

⚠️ **Nothing in `UnitTests` touches a database**, so the migration was diffed with
`dotnet run -- verify-schema` either side: it added exactly `TreatmentPlans.WriteOffAmount`,
`TreatmentPlans.WriteOffReason` and `TreatmentPlanItems.DiscountAmount`, emitted **no** scaffolded `xmin`
column, and the six pre-existing DRIFT lines were identical before and after. `reconcile-money` was clean and
the monthly « encaissé » baseline unchanged.

⚠️ **Three environment facts the pass measured, each of which reads as a product collapse:**

- **`FrontendUrl` is `http://localhost:3000` and `Cors:AllowedOrigins` is empty**, so serving `web` on :3100 —
  which `e2e/README.md` recommends to dodge the contested :3000 — makes every browser row fail with
  « Connexion au serveur impossible ».
- **Two browser contexts may not share one `storageState`.** The refresh exchange rotates the credential and
  rotation *is* the theft detection, so the second context signs the first out — every row after the one that
  opened a second context landed on `/login` with a 401.
- **CDP's `Emulation.setEmulatedMedia` does not deliver `(pointer: coarse)`** — `matchMedia(…).matches` stayed
  `false` with and without `media: "screen"`. `hasTouch: true` on the **context** is what does, and a
  context's touch flag cannot be changed after creation. Measuring without it reports every `coarse:`-sized
  control as a violation; all seven of ours were fine.
