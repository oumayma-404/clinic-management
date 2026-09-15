# Feature Review: treatment plans — functional behaviour audit

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-09-14
**Challenged Date:** 2026-09-14
**Parent Branch:** `feature/security-remediation`
**Scope:** NOT a diff review. The whole treatment-plan domain as it stands on `feature/security-remediation`.
**Review method:** 5 parallel agents — Lifecycle & dead-ends · Money gaps · Change paths (31 clinical
scenarios) · UX & device · Cross-surface consistency.
**Challenge method:** every finding re-read against the full source file (not the diff), plus
`features/treatment-plan-lifecycle/spec.md`, `PlanBillingRules`, `TreatmentPlanLifecycle` and the
`CLAUDE.md` traps each finding leans on.
**Driving complaint:** « once something becomes a treatment plan, I am always stuck, any modification
becomes load-bearing » — a client could not cancel an act on a plan de traitement.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 62 |
| Confirmed | 49 |
| Confirmed (adjusted) | 12 |
| Dismissed (false positive) | 1 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | **61** |

| Severity | Before | After |
|----------|--------|-------|
| Critical | 12 | 7 |
| Major | 26 | 24 |
| Minor | 17 | 21 |
| Suggestion | 7 | 9 |
| **Total** | **62** | **61** |

**Dismissed:** M21 (reorder is desktop-only) — the card list *does* render `PlanActReorderControls`
as an « Ordre » field (`plan-workspace.tsx:1338`). The reviewer saw only the `lg:` table.

⚠️ **Three findings describe behaviour `spec.md` deliberately specified. Do not revert them in
`/apply-review-fixes`** — they are kept only because verification exposed a consequence the spec did not
weigh: **C5** (AC-11), **C9** (the spec self-conflicts), **m1** (AC-11 + API contract).

---

## The one-paragraph verdict

The domain is *guarded* well and *reversible* badly. Nearly every refusal in the aggregate protects real
money or real clinical truth, and most are correct to keep. What is missing is the **other half of each
guard**: the remedy it names. Three remedies named in French refusal sentences do not exist as commands
(`détachez la note`, per-act restore, write-off); `Cancelled` is an absorbing state that **nothing in the
product can leave** — while the stop button can put a plan there *without the dentist choosing it*. That is
the literal shape of « any modification becomes load-bearing ».

On money the audit holds up in full: the `TotalPlanned < AmountPaid` rule exists on 2 of 4 writers, and
`Cancel` — the most destructive verb — has neither it nor a live-payment check. Three separate paths can make
collected cash disappear from la caisse or become unreachable, none of which errors anywhere.

---

## § 1 — CRITICAL (money loss, permanent dead-end, silent data loss)

### C1 — Cancelling a devis erases already-collected cash from la caisse, retroactively
- **Severity:** Critical · **Category:** Business Logic · **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Domain/Entities/TreatmentPlan.cs:1291` · **Anchor:** `Cancel(string reason)`
- `Cancel` has **no `AmountPaid` guard**, unlike its sibling `StopTreatment:847` («… ont déjà été
  encaissés … Remboursez la différence par un avoir »). `Cancelled ∉ DebtBearingPlanStatuses`
  (`PlanBillingRules.cs:46`), and all four caisse reads filter on it
  (`TreatmentPlanRepository.cs:519,597,644,677`).
- **Scenario:** devis 2026-0031, 500 DT cash on 3 March. On 20 March the dentist cancels. The **3 March
  extrait — a closed day — loses 500 DT**, the dashboard total loses it, a cheque paid against it vanishes
  from « Chèques à encaisser ». `GetCollectedByDentalRecordAsync:529` has no status filter (verified — it
  says so in its own comment), so the fiche still prints « Encaissé sur le traitement 500,000 DT » for money
  no ledger holds.
- **Fix:** refuse `Cancel` while any non-voided payment exists; name the avoir, as `StopTreatment` does.

### C2 — The stop button auto-branches into that cancel, and the dialog says the opposite of the truth
- **Severity:** Critical · **Category:** Business Logic · **Verdict:** Confirmed
- **File:** `api/.../TreatmentPlan.cs:756` (`StopWouldCancel`) + `web/components/treatment-plans/plan-workspace.tsx:2160`
- `StopWouldCancel = Number != null && !_items.Any(i => !i.IsWithdrawn && i.HasDeliveredWork)` — **no
  money test** — and `StopTreatmentPlanCommand.cs:109-117` takes that branch straight to `Cancel`.
- The dialog reads « Aucune séance de ce devis n'a été réalisée, il n'y a donc **rien à conserver** » —
  false whenever a deposit was taken — and never says the devis can no longer be reopened, corrected,
  amended, or have its payments voided. The *stop* branch beside it correctly names the money
  (`:2251-2260`). The one genuinely irreversible branch says neither.
- **Fix:** add `AmountPaid > 0` to `StopWouldCancel`'s negation so a devis carrying money takes the stop
  branch. State the encaissé amount and « Cette action est irréversible » on the cancel branch.

### C3 — `Cancelled` is an absorbing state; nothing in the product can leave it
- **Severity:** Critical · **Category:** Business Logic · **Verdict:** Confirmed
- **File:** `api/.../TreatmentPlan.cs:885` · **Anchor:** `Reopen`
- Every guard excludes it: `EnsureAmendable:1272`, `EnsureCorrectable:1378`, `EnsurePayable:1399`,
  `EnsureActive:1325`, `SetItemSteps:631`, `SetItemOrder:1222`, `VoidInstallmentPayment:462`, and
  `CanBeDeleted:112` (Draft only) — it cannot even be deleted.
- The workspace confirms it on screen: `canAmend`, `canCorrectActs`, `canCollectInstallments`, `canReorder`,
  `canBill` all false and `primaryAction` null. **« Devis PDF » is the only surviving control**, with no
  explanation and no route.
- **Fix:** admit `Cancelled` to `Reopen` (the number is already immutable and the motif already stored, so
  the gapless series is not at risk), or add an audited `Uncancel(reason)`.

### C4 — A cancelled devis is announced as « TRAVAIL TERMINÉ · Tous les actes sont réalisés »
- **Severity:** Major · **Category:** Business Logic · **Verdict:** Confirmed (adjusted — was Critical)
- **File:** `web/.../plan-workspace.tsx:511, 2409` · **Anchor:** `nextSchedulable` / `NextSeanceBlock`
- `nextSchedulable = isActive ? … : null` and `isActive = isPlanLive(status)` excludes `Cancelled`, so the
  plan falls into the `!nextAct` arm: the header claims the work is done beside « 0 séance sur 3 faite » and
  an « Annulé » badge. `NextSeanceBlock` has an `isStopped` arm and **no `Cancelled` arm** — verified.
- **Challenge note:** severity lowered Critical → Major and the line re-anchored (511/2409, not 513/2421).
  It is a false statement on screen, not money loss, a dead end or data loss — the three things § 1 is for.
- **Fix:** a `Cancelled` arm before `!nextAct` (« Devis annulé » + the motif).

### C5 — Once one séance is recorded, a devis can never be cancelled at all
- **Severity:** Minor · **Category:** Business Logic · **Verdict:** Confirmed (adjusted — was Critical)
- **File:** `web/.../plan-workspace.tsx:388` (`⚠️ canCancel lived here and is gone with the button`)
- `POST /{id}/cancel` is live and `treatmentPlansApi.cancel` is defined — no caller. Cancel reaches the
  browser only through the stop dialog's `StopWouldCancel` branch, which is false once anything is
  delivered. So a devis issued on the wrong patient, or a duplicate that then took one visit, has **no
  cancellation route**.
- **Challenge note:** ⚠️ **Spec-mandated — do not revert.** `spec.md` § What Changes states « **« Annuler le
  devis » is removed from the UI and becomes a branch of the stop** », AC-11 requires it « absent from every
  surface », and the API Contract says « `POST /{id}/cancel` stays, **unused by the UI** ». What the spec did
  not weigh is that the branch is unreachable once work exists. Severity lowered Critical → Minor: the
  behaviour is intended, only the uncovered case is new. Restoring a « Annuler le devis » button would
  re-break AC-11 — the right fix is to widen the stop dialog's branch, not to add the old control back.

### C6 — An act added to a billed plan is owed by the patient and appears in NO balance
- **Severity:** Major · **Category:** Business Logic · **Verdict:** Confirmed (adjusted — was Critical)
- **File:** `api/.../Commands/AmendTreatmentPlanCommand.cs:148,364` · **Anchor:** `EnsureNotBilledAsync`
- The guard is **dead code** — private, zero call sites (verified: the only other hits are same-named private
  methods in `UnmarkTreatmentPlanItemDoneCommand` / `UnmarkTreatmentPlanItemStepCommand`).
- **Scenario:** devis 2026-0044 (600 DT) billed on note 2026-0018; the dentist amends and adds
  « Couronne 450 DT ». `BilledPlanIds` drops the whole plan and the note's lines froze at issue — **450 DT
  is owed and appears in no balance at all**: not « Solde patient », not « Créances », not la caisse, not
  the dashboard, not the échéancier.
- **Challenge note:** severity lowered Critical → Major, and **the proposed fix is wrong**. The removal was a
  deliberate, documented owner decision (`:148`: « le médecin doit pouvoir tout corriger »), and `spec.md`
  AC-17 orders the method *deleted* as dead code. Re-instating a refusal reverses both. The defect is the
  **invisible balance**, not the missing refusal: the workspace does state the gap locally
  (`:1588-1640`, « Le travail restant … reste X ») but no clinic-wide money read counts it. Fix by making the
  amendment's surplus billable (a supplementary note, which `canBill:404` already anticipates), never by a
  blanket block.

### C7 — Lowering a plan's total below what was collected makes the patient's money unreachable
- **Severity:** Critical · **Category:** Business Logic · **Verdict:** Confirmed
- **File:** `api/.../TreatmentPlan.cs:932` (`RespreadSchedule`), called from `AmendTreatmentPlanCommand.cs:272`
- The `TotalPlanned < AmountPaid` refusal exists on `StopTreatment:847` and `ReviseInstallments:1160` and
  **not** on the respread branch the amend handler takes when no schedule is sent (verified line by line).
- **Scenario:** 500 DT devis, 500 DT collected; the dentist removes a 200 DT act. Total → 300, collected
  stays 500, `Outstanding` clamps at 0, and `Σ Amount` (500) no longer equals `TotalPlanned` (300) — the very
  invariant `ReviseInstallments`' docstring calls load-bearing. **200 DT of the patient's money becomes
  unreachable**: no credit line, no avoir prompt, both balances read 0.
- **Fix:** put the same throw inside `RespreadSchedule`, so all four writers share one rule.

### C8 — « Modifier le brouillon » from the plans list deletes every protocol and fiche link, silently
- **Severity:** Critical · **Category:** Business Logic · **Verdict:** Confirmed
- **File:** `web/.../treatment-plans-table.tsx:505` · **Anchor:** `openEdit(p)`
- The list renders `TreatmentPlanFormModal` **without `amendMode`** (`:679-687`), so the save takes the
  `update` branch → `UpdateTreatmentPlanCommand.cs:76` → `plan.SetItems(items, scheduleWillBeResent: true)`,
  which reuses the **id** but builds a **new `TreatmentPlanItem`** (`TreatmentPlan.cs:218-230`): empty
  `_steps`, `LinkedDentalRecordId = null`, `DoneDate = null`, `Status = Planned`.
- A Draft is a *followed treatment* today — it routinely carries steps and recorded séances — and
  `EnsureDraft()` lets it straight through. **Retyping a title deletes the protocol and every fiche link.**
  No error. The workspace path is safe (it passes `amendMode`).
- **Fix:** route the list's edit through the amend modal, and make `SetItems` refuse a plan where
  `_items.Any(i => i.HasSteps || i.HasDeliveredWork)` — the question `CanBeDeleted` already asks one method over.

### C9 — There is no route to facturation for any devis with séances still to come
- **Severity:** Major · **Category:** Breaking Change · **Verdict:** Confirmed (adjusted — was Critical)
- **File:** `web/.../plan-workspace.tsx:878-908, 1012-1071`
- « Facturer le devis » is reachable **only** when `canBill && actsRemaining === 0`, and `isStopped` is tested
  first. The ⋯ menu holds **Devis PDF · Modifier les actes et les prix · Arrêter le traitement · Supprimer le
  traitement** — verified, no Facturer item. `invoicesApi.createFromPlan` has one caller in the app.
- The code's own note at `:893` claims « It stays available in the « ⋯ » menu at every other moment — the
  capability is unchanged »; that is false. Every stopped devis and every partly-done devis has no billing route.
- **Challenge note:** severity lowered Critical → Major (capability loss; the money is still collectable on
  the échéancier, since `Stopped` is payable). ⚠️ **The spec self-conflicts here and its premise is wrong:**
  § What Changes says « the capability is unchanged and lives in the « ⋯ » menu », while **AC-11's five-entry
  menu list omits Facturer**. The implementation followed AC-11 and lost the capability. Decide deliberately;
  do not treat AC-11 as authority for keeping it out.
- **Fix:** add a `Facturer le devis` menu item gated on `canBill`.

### C10 — The devis editor holds a stale version after a 409
- **Severity:** Minor · **Category:** Code Quality · **Verdict:** Confirmed (adjusted — was Critical)
- **File:** `web/.../treatment-plan-form-modal.tsx:944, 984` · **Anchor:** `handleSubmit` catch
- It round-trips `version` and on a 409 skips `resync()`, so the version it holds never moves and every later
  « Enregistrer la révision » repeats the refusal. It renders a bare `FormErrorBanner` (`:1046`) with **no
  `action`**, unlike `PlanItemStepsDialog`, which uses `useConflict`.
- **Challenge note:** severity lowered Critical → Minor — « permanently poisoned » is not accurate. The skip
  is **deliberate and documented** (`:985`: « a real 409 is left alone, or the retry would silently overwrite
  the colleague who caused it »); `conflictMessage:252` **escalates on the second consecutive 409** to
  « Quelqu'un travaille probablement dessus en même temps — coordonnez-vous avant de réessayer »; and closing
  and reopening the modal re-reads the version through `useFreshVersion`. What is genuinely missing is the
  « Recharger » control the repo's own convention (`useConflict`) exists to provide.
- **Fix:** `useConflict` + a « Recharger » that re-reads the plan, keeping the no-silent-retry behaviour.

### C11 — `Reopen` restores the acts but never re-spreads the échéancier: two screens, two different balances
- **Severity:** Critical · **Category:** Business Logic · **Verdict:** Confirmed
- **File:** `api/.../TreatmentPlan.cs:885` · **Anchor:** `Reopen`
- `restored > 0 → RecomputeTotal(); RevisionNumber++;` with **no `RespreadSchedule`** (verified at `:899-904`).
  The plan is debt-bearing the instant `Status` is written.
- **Scenario:** 1 200 DT devis stopped with 400 DT kept and 400 DT collected; patient returns.
  `TotalPlanned` → 1 200, `Σ Amount` stays 400. « Solde patient » (`TotalPlanned − AmountPaid`) reads
  **800 DT**; « Créances », the dashboard and `PatientDebtLines` (`Σ Amount − AmountPaid`) read **0 DT**.
  Same patient, two screens, no error — and the receptionist is offered a payable room of 0.
- **Fix:** call `RespreadSchedule(ClinicToday())` inside `Reopen`, or refuse to leave the aggregate with
  `Σ Amount != TotalPlanned`.

### C12 — Voiding a payment on a bridged plan reports success and changes nothing
- **Severity:** Critical · **Category:** Business Logic · **Verdict:** Confirmed
- **File:** `api/.../Commands/VoidInstallmentPaymentCommand.cs:75`
- `RecordInstallmentPaymentCommand:111-119` refuses a bridged plan by name; the **void** path has no such
  check (verified — the handler goes straight from the tenant check to `plan.VoidInstallmentPayment`). Once
  `CarryOverPlanPaymentsAsync` copied the receipt onto the note, every installment money read excludes the
  plan, so voiding the plan-side row changes nothing: the invoice `Payment` stays live, la caisse still
  counts it, and the user is told « paiement annulé ».
- Reachable from the UI: `VoidInstallmentPayment` renders at `plan-workspace.tsx:1874` with **no `billed`
  gate**, unlike « Encaisser » (`canCollectInstallments`).
- **Fix:** mirror the record-side refusal — name the note and send the correction to the invoice's own void.

---

## § 2 — MAJOR

All verdicts **Confirmed** unless the row says otherwise.

| # | Finding | File:line | Verdict |
|---|---|---|---|
| M1 | **The literal complaint.** `EnsureItemRemovable` refuses removing an act with delivered work, and its named remedy is a **three-deep chain** the message does not disclose: détacher la fiche → refused by `DentalRecordBillingGuard` if the fiche is on a live note → whose remedy (annuler/avoir) is refused if the note has a live payment. The guard is a correct *keep*; what is missing is a per-act « mettre de côté » that preserves fiche links, drops the act from `TotalPlanned`, and needs none of the chain. | `TreatmentPlan.cs:1119` | Confirmed |
| M2 | **No per-act restore.** Parking is all-or-nothing both ways: `StopTreatment` parks every undelivered act, `Reopen` restores every one. « La patiente revient, mais seulement pour la couronne » forces reopening the whole treatment, re-inflating `TotalPlanned` with the implants she declined. `Restore()` is `internal`, one caller (`TreatmentPlan.cs:899`). | `TreatmentPlanItem.cs:402` | Confirmed |
| M3 | **The named remedy « détachez-la de ce traitement » has no route.** `TreatmentPlanBridgeRelease.DetachAsync`'s only callers are `CancelTreatmentPlanCommand:64` and `StopTreatmentPlanCommand:121` — the dentist can only detach the note by killing the treatment. | `TreatmentPlanItem.cs:240` | Confirmed |
| M4 | **`UpdateDetails` contradicts `EnsureAmendable`.** `UpdateDetails:150` admits Draft/Accepted/InProgress only; `EnsureAmendable:1272` refuses Cancelled only. So amending an act price on a `Stopped` plan succeeds, while correcting its title fails the **whole** save with « Seul un devis brouillon, accepté ou en cours peut être modifié. » — to a dentist looking at a plan badged « Arrêté ». The amend modal always sends `title`. | `TreatmentPlan.cs:147` | Confirmed |
| M5 | **A devis on the wrong patient is unfixable.** `PatientId` is ctor-only, no mutator, disabled `Input` in every edit mode. Delete works only for a Draft with no work; otherwise retype everything under a new number. A `ReassignPatientCommand` gated on « no delivered work and no linked invoice » is the safe version. | `TreatmentPlan.cs:18,127` | Confirmed |
| M6 | **The wrong dentist is invisible and permanent.** `SetDoctor`'s two call sites (`CreateTreatmentPlanCommand:95`, `ContinueRecordedActCommand:300`) are both on a plan two lines old; `TreatmentPlanDto` carries **no doctor field at all** (verified), so no screen shows it. `CreateInvoiceFromTreatmentPlanCommand:129` snapshots it onto the note — in a two-dentist cabinet every dinar is attributed to the wrong person for ever. | `TreatmentPlan.cs:48` | Confirmed |
| M7 | **The one irreversible write is the one command with no version check.** `CancelTreatmentPlanCommand` has no `SetExpectedVersion` (verified), so a cancel lands over a colleague's concurrent amendment with no 409 and no trace, on a plan that can never be recovered. | `CancelTreatmentPlanCommand.cs:59` | Confirmed |
| M8 | **Two money handlers flatten 409s.** `VoidInstallmentPaymentCommand:105` and `SetInstallmentPaymentBankedCommand:108` are the only two in the folder whose catch-all omits `when (ex is not ConflictException)` — verified against all nine commands. **Challenge note:** severity lowered Major → **Minor** — neither command declares an expected version either (M9), so a `ConflictException` is currently near-unreachable on both. Fix it *with* M9, or fixing M9 alone makes this live. | both | Confirmed (adjusted) |
| M9 | **Five money/lifecycle commands don't round-trip the version at all** — `Cancel`, `Complete`, `RecordInstallmentPayment`, `ReviseTreatmentPlanInstallments`, `SetTreatmentPlanItemOrder` (verified: no `SetExpectedVersion` in any). The fourth rewrites the whole échéancier, the first erases a debt. | command classes | Confirmed |
| M10 | **The odontogram is never re-charted when an act stops being done.** `ToothChartingRules.ChartableActs` has exactly two production callers, both fiche commands (`CreateDentalRecordCommand:287`, `UpdateDentalRecordCommand:347`). « Détacher la fiche », `UnmarkStep` and `StopTreatment`'s parking all return acts to non-`Done` — the condition under which the rule withholds the end state — and nothing re-charts. The chart keeps asserting « Implant » / « Extrait ». | `UnmarkTreatmentPlanItemDoneCommand.cs:108` | Confirmed |
| M11 | **The mirror: marking an act réalisé from the workspace charts nothing.** `MarkTreatmentPlanItemDoneCommand` completes a multi-séance act, so the withheld end state becomes chartable — and is never written. The devis says « Réalisé », the chart draws nothing. Neither errors. | `MarkTreatmentPlanItemDoneCommand.cs:12` | Confirmed |
| M12 | **« Traitements en cours » never live-refreshes after the gesture that changes it.** It subscribes to `TreatmentPlans` + `Appointments` (`:157`) and its comment claims « a fiche saved elsewhere advances a step — so both keys matter ». A fiche save is `Features.Patients.Commands`, so it emits `patients`. The comment is false. | `treatments-in-progress-list.tsx:157` | Confirmed |
| M13 | Same gap on `/treatment-plans` (`treatment-plans-table.tsx:220`) and on the plan workspace page (`app/treatment-plans/[id]/page.tsx:62`) — both take `TreatmentPlans + Appointments + Invoices` and **not** `Patients`, while act progress, status and `displayedOutstanding` all move under `patients`. And inversely the odontogram (`odontogram.tsx:297`) takes `Patients` only, missing `treatmentplans`. 3 files. | 3 files | Confirmed |
| M14 | **« Devis acceptés ce mois » shrinks as the clinic does the work.** `CountByStatusAsync(..., TreatmentPlanStatus.Accepted, ..., byAcceptedDate: true)` matches the status exactly, but the first payment or fiche moves the plan to `InProgress` while keeping `AcceptedDate`. The drill-through repeats the filter, so the list agrees with the wrong number. Correct owner: `DebtBearingPlanStatuses`, or `Number != null`. | `DashboardActivityReader.cs:55` | Confirmed |
| M15 | **A stopped treatment disappears from the patient file.** `planStatusCounts`'s `order` array is `["Draft","Accepted","InProgress","Completed","Cancelled"]` — no `Stopped` — and `isPlanLive("Stopped")` is false, so it is neither a line nor a chip. The plans list still shows it: two screens disagree. `PLAN_STATUS_LABELS` and `PLAN_STATUS_TONE` both got a `Stopped` entry; this third mirror did not. | `plan-next-action.ts:328` | Confirmed |
| M16 | **A parked act is counted as bookable.** `schedulableItems` filters `plan.items`, not `activeItems` — unlike the shared `schedulablePlanItems:516`, which excludes withdrawn acts. **Challenge note:** severity lowered Major → **Minor, latent**. It is gated on `isActive` (= `isPlanLive`), and withdrawn acts only exist on a `Stopped` (or legacy `Completed`) plan, since `Reopen` restores all of them — so no live plan can carry one today. It becomes real the moment per-act parking (M2) ships. | `plan-workspace.tsx:544` | Confirmed (adjusted) |
| M17 | **A parked act renders as an ordinary outstanding act.** Both act trees map `plan.items` (`:1420`, and the grouped card list), so a parked act shows badged « À planifier » with no button and no « mis de côté » marker. **Challenge note:** severity lowered Major → **Minor** — the header's progress line already prints « · N mis de côté » (`:2355`) and the stop dialog names them, so the information is on the page; what is missing is the per-row marker. | `plan-workspace.tsx:577,1420` | Confirmed (adjusted) |
| M18 | **A stepped act can show « À enregistrer · 12/08 » with no « Enregistrer la fiche ».** `planItemState:15` derives the badge from the next *step*'s `scheduledAt`; the `scheduled` and `to-record` action arms (`:278,255`) both gate on the *act*'s `scheduledAppointmentId`, which `actRemovalPlan` proves can be null while the step carries the booking. | `plan-act-row.tsx:239` | Confirmed |
| M19 | **A row with no action says nothing.** The `to-schedule` arm requires `planIsActive`, and after the four arms the component `return null`s (`:317`) — an empty cell, no sentence, on Completed/Cancelled devis with unrealised acts. | `plan-act-row.tsx:216,317` | Confirmed |
| M20 | **« Accepter le devis » on the patient band has no confirmation.** `handleAccept(plan.id)` fires straight off the click (`:163`). The workspace guards the same irreversible operation with a full `AlertDialog` naming the definitive number. One mis-click spends a per-clinic-per-year number and creates a live créance whose only exit is C3/C5. | `patient-plans-strip.tsx:163` | Confirmed |
| ~~M21~~ | ~~Reorder is desktop-only.~~ | — | **Dismissed (false positive)** — the card list renders `PlanActReorderControls` as an « Ordre » field, `plan-workspace.tsx:1338-1350`. |
| M22 | **The « suite de séance » dialog overflows its own sheet at 320/390 px.** The scrolling middle is `<div className="space-y-3 overflow-y-auto">` with neither `min-h-0` nor `flex-1`, and the sheet variant sets no overflow — so `overflow-y-auto` never engages and the `role="status"` warning plus « Ajouter au rendez-vous » are pushed off screen. Use `DialogBody`. | `continue-session-dialog.tsx:201` | Confirmed |
| M23 | **Échéance rows scroll horizontally at 320 px.** `flex items-end gap-2` with no `flex-wrap`, a `flex-1` `type="date"` (~120 px intrinsic min) beside a `w-36` amount and a bin — the date input cannot reach its floor in the ~90 px left. Two files, identical shape. ⚠️ Measured from source, not from a browser: confirm in the eye pass at 320 px. | `treatment-plan-form-modal.tsx:1679`, `revise-installments-modal.tsx:203` | Confirmed |
| M24 | **« Modifier le brouillon » / « Supprimer le brouillon » are offered on `isDraft` alone** (`:502-511`), while the workspace's twins add `!planHasRecordedWork(plan)` (`:384`) and document why. The delete is refused server-side (red toast); the edit is not — that is C8. | `treatment-plans-table.tsx:502` | Confirmed |
| M25 | **Three surfaces print an auto-raised échéance's date** that `InstallmentDueCell` deliberately withholds (spec AC-8: « no date and no « En retard » ») — `installment-payment-modal.tsx:144` (« Échéance du … » unconditionally), `revise-installments-modal.tsx:206` (an editable `type="date"` pre-filled with it) and `void-installment-payment.tsx:79`. One rule, one owner, three bypasses. | 3 files | Confirmed |
| M26 | **« Pourquoi Encaisser a disparu » is gated on `billed` only** (`:1650`), while `canCollectInstallments` (`:538`) also refuses Draft and Cancelled — so a « Sans devis » treatment carrying échéances shows rows with no Encaisser and no reason. **Challenge note:** severity lowered Major → **Minor** — a Draft with a hand-built échéancier is an uncommon state and nothing is lost, only unexplained. | `plan-workspace.tsx:1650` | Confirmed (adjusted) |

---

## § 3 — MINOR

| # | Finding | File:line | Verdict |
|---|---|---|---|
| m1 | `POST /{id}/complete` calls `Complete(leaveUnrealisedActs: true)` — it has no UI caller, no version check, and is the one path that closes a plan **without parking** the unrealised acts and **without** re-spreading, so the patient keeps owing for work nobody will do. **Challenge note:** severity lowered Minor → **Suggestion**, and ⚠️ **the "no UI caller" half is spec-mandated — do not revert**: `spec.md` removes « Terminer le traitement » from the UI and states « `CompleteTreatmentPlanCommand` stays (the automatic path and the API surface are unchanged) ». What is worth deciding is whether the `leaveUnrealisedActs: true` API path should exist at all now that « Arrêter » owns that case. | `CompleteTreatmentPlanCommand.cs:70` | Confirmed (adjusted) |
| m2 | `AmendTreatmentPlanCommandHandler.EnsureNotBilledAsync` is dead but **cited as live** by `TreatmentPlanItem.cs:235` and `ContinueRecordedActCommand.cs:243` — verified, and the second is now false. `spec.md` **AC-17 already orders this method deleted**; it was not. Delete it, the now-unused `IInvoiceRepository` field, and correct both comments (see C6). | 3 files | Confirmed |
| m3 | `SetItemOrder` demands an **exact** set (`itemIds.Count != _items.Count`), withdrawn acts included; any future surface reordering `ActiveItems` is refused with a message naming nothing actionable. | `TreatmentPlan.cs:1220` | Confirmed |
| m4 | « Un plan accepté ne peut pas être supprimé ; il doit être annulé. » sends the dentist to the one irreversible action without saying so — and fires identically for `Completed`/`Stopped`, where cancelling freezes a delivered-work record for ever, and where C5 leaves no cancel route anyway. | `DeleteTreatmentPlanCommand.cs:65` | Confirmed |
| m5 | A cancelled devis's fiches still render « Suivi comme traitement » with a live link — `GetPlanLinksByDentalRecordAsync` filters no plan status (verified; its comment addresses withdrawn acts only). Should ask `ContinuationTracking.Tracks`. | `TreatmentPlanRepository.cs:393` | Confirmed |
| m6 | `SetItemSteps` re-derives the open status by hand (`:661-666`) instead of `OpenStatusFromWork` — a fourth caller the docstring does not name. Latent (shielded by `StatusFollowsTheWork`), and the same shape that once turned a followed treatment into an `Accepted` devis with a null number. | `TreatmentPlan.cs:661` | Confirmed |
| m7 | Four byte-identical copies of `TreatmentPlanLifecycle.LiveStatuses` inside the aggregate — `UpdateDetails:150`, `Complete:772`, `StopTreatment:814`, `EnsureActive:1327`. A seventh status would move the repository and the recall worklist and leave all four guards admitting it. `FollowedTreatmentLifecycleTests`' scan excludes `Domain/Entities`. | `TreatmentPlan.cs:150,772,814,1327` | Confirmed |
| m8 | `RecallWorklistRules.IsUnanswered:139` tests `Status == Draft` for « a numbered quote the patient never answered » — its own docstring says the premise is false (`Accept` is the only writer of `Number`). Its dashboard twin `CountUnansweredDraftsAsync:451` answers the same question with a **different** rule (no grace, no stall yield). | `RecallWorklistRules.cs:139`, `TreatmentPlanRepository.cs:451` | Confirmed |
| m9 | `NoteCarriedActGuard` writes `ContinuationTracking.Tracks` out by hand, twice (`Status != Cancelled` / `Status == Cancelled → continue`). | `NoteCarriedActGuard.cs:65,92` | Confirmed |
| m10 | Removing an act clears only live non-`Completed` bookings; `Cancelled`/`NoShow` keep a `TreatmentPlanItemId` pointing at a deleted row, with no FK to catch it. Latent. | `AmendTreatmentPlanCommand.cs:398-408` | Confirmed |
| m11 | The plan workspace's « Détacher la fiche » confirm is a `Dialog` with a hand-styled destructive button, while every other destructive confirm in the same file is an `AlertDialog` — the file's own note at `:2110` states that convention. | `plan-workspace.tsx:2026` | Confirmed |
| m12 | The cancel-branch confirm is `disabled` until a motif is typed, with no « obligatoire » marker. **Challenge note:** severity lowered Minor → **Suggestion** — the field has a real `<Label htmlFor="plan-cancel-reason">` (`:2192`) and the disabled-until-valid pattern is deliberate and documented at `:2273` (« the rule the form can enforce should never be discovered by breaking it »). Only the required marker is missing. | `plan-workspace.tsx:2192,2280` | Confirmed (adjusted) |
| m13 | On a completed, billed treatment the filled primary button — the app's grammar for « recommended next action » — is « Reprendre le traitement » (`:900-902`). Nothing is recommended on a finished devis. | `plan-workspace.tsx:900` | Confirmed |
| m14 | `title="Ramener cet acte à « Prévu » et détacher sa fiche de soins"` is the exact false claim `detachOutcome` was written to remove (a stepped act lands on « En cours »), and a `title` is unreachable on the tablet this app runs on. | `plan-act-row.tsx:307` | Confirmed |
| m15 | The acts empty state names « Modifier le devis » (`:1235`), a control renamed « Modifier les actes et les prix » precisely because it was a homophone of « Éditer le devis » — the rename is documented at `:1021`. The form modal's own title (`treatment-plan-form-modal.tsx:1001`) carries the stale wording too. | `plan-workspace.tsx:1235` | Confirmed |
| m16 | The list says « brouillon » at `:505, 510, 647, 653, 730` while `PLAN_STATUS_LABELS.Draft` was deliberately renamed « Sans devis » and the workspace says « le traitement ». | `treatment-plans-table.tsx:505,510,647,653,730` | Confirmed |
| m17 | Ungated `grid-cols-2` on the money figures (labels wrap to three lines at 320 px); clickable `TableRow` with `cursor-pointer` and no keyboard path; three different rules for what a plan is called (`planDisplayName` exists). **Challenge note:** the fourth sub-claim is **struck as false** — `MAX_PIPS` exists once in the codebase, `plan-act-pips.tsx:14`, value 12. The remaining three were not individually re-measured; treat them as a device-pass checklist, not as three proven defects. | 3 files | Confirmed (adjusted) |

---

## § 4 — SUGGESTION (capabilities a dentist plainly wants)

All **Confirmed** as capability gaps — each is an absence verified in source, not a defect in shipped code.

| # | Capability | Today | Note |
|---|---|---|---|
| S1 | **Duplicate a plan** | absent | The same protocol for the second implant. Cheap, needs no lifecycle change: copy items + steps, start as `Draft`. The only missing capability with no counter-argument. |
| S2 | **Remise / discount** | absent | No `Remise` field anywhere on `TreatmentPlan` or `TreatmentPlanItem` (verified). A discount is entered by overtyping the tarif, so the devis prints as if that were the price — the practice cannot report what it gave away. |
| S3 | **Settle a devis in one payment** | 3 dialogs | `Installment.RecordPayment:139` refuses more than the row's remainder, and the modal is addressed to one row. `CollectChairside:395` already spreads correctly — it is only reachable from a fiche. One route, no new domain rule. |
| S4 | **Write off a créance** | absent | A patient who dies or moves leaves a live `Stopped` devis; `CarriesDebt(Stopped)` is true so archiving is refused for ever. The invoice side has the avoir; the devis side has only `Cancel`, unreachable (C5) and destructive (C1). |
| S5 | **Show the step interval where the séance is booked** | invisible | Editable, survives the save, never shown and never checked. An implant's 90-day osseointegration is bookable at 7 days with no warning. `DueFrom` exists and one surface reads it. Phrase as « pas avant », never « en retard ». Acknowledged TODO. |
| S6 | **Re-link a séance to the right act in one gesture** | 2 screens, 2 saves | Detach on the devis, then reopen the fiche and re-pick — and the right act is invisible until the wrong one is released. The Select names acts, not steps, so a wrong *step* needs the booking edited on a third screen. Offer « …et la rattacher à : [acte · étape] » from the detach confirm. |
| S7 | **Warn on a duplicated act across two live plans** | silent | Nothing detects it; both plans carry debt so the balance counts the act twice, and `BilledPlanIds` de-duplicates per *plan*, not per act. A refusal would be wrong (a second opinion legitimately re-quotes) — a notice is the fix. |

**Correctly refused, leave alone** (each would move a gapless devis number onto a different document):
un-accept, re-date acceptance, split, merge. Say so on screen rather than by absence.
⚠️ Exception worth revisiting: the patient band's « Accepter le devis » spends a number with no
confirmation (M20) — `IssueDevis` should be the only door.

---

## The shape of the fix, if you want one sentence per theme

| Theme | The one change |
|---|---|
| Money | Put `TotalPlanned < AmountPaid` and « no live payments » in **one** place each, consulted by all four writers and by `Cancel`. (C1 · C7 · C11 · C12) |
| Stuck-ness | Every refusal must name a remedy that **exists as a command**. Three currently do not. (M1 · M2 · M3) |
| Reversibility | `Cancelled` must be leavable, and the cancel branch must know about money before it takes itself. (C2 · C3) |
| Flexibility | Per-act « mettre de côté » + « reprendre » is the capability the complaint is actually asking for. It touches no fiche link and no money rule. (M2) |
| Consistency | The plan surfaces must subscribe to `patients` — a fiche save is what moves them. (M12 · M13) |
| Spec debt | AC-17's deletion never happened (m2), and AC-11 quietly removed the billing route the same spec promised to keep (C9). |
