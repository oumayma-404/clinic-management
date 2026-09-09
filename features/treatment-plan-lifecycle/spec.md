# Feature Specification: Un traitement qu'on arrête n'est pas un traitement qu'on a fini

**Status:** APPROVED
**Type:** Full
**Created:** 2026-09-09
**Scope:** Backend (domain + application) · Frontend (`plan-workspace`, labels, `isPlanLive`) · Tests + derived guards
**Feature:** `Arrêter le traitement` gets a status of its own, the five lifecycle verbs fold to three, the
auto-raised échéance stops being created, and the plan workspace is rebuilt around the next séance.

## Overview

`StopTreatment` ends with `Complete(leaveUnrealisedActs: true)` (`TreatmentPlan.cs:770`), so a stopped
treatment wears the badge « Terminé » — indistinguishable from one carried to term, in the database and on
screen. And `primaryAction` tests `canBill` before `Completed` (`plan-workspace.tsx:749-761`), so
« Reprendre le traitement » is unreachable anywhere in the product; on a followed treatment every act is
parked, `ActiveItems` is empty and the server refuses the facturation too — a dead end in both directions.

Behind that sit two more inventions the practice does not use. Five plan statuses answer three different
questions at once (*is there a quote · does this owe money · is the work going*), so five verbs are needed to
move within them and two of them land on the same value. And `Accept` raises a lump-sum échéance dated at the
signing instant so a payment has somewhere to attach — **159 of 184 installment rows** on the dev database,
**133 of them never paid** — which is late from the next day and needs a seven-parameter rule to suppress the
badge it lights itself.

Measured on the dev database, 2026-09-09: **0** acts have ever been parked (`Withdrawn`), the **6** cancelled
plans are all E2E fixtures, and **42 of 51** accepted devis show the patient owing the whole quote before the
first act is placed.

## What Changes

### Lifecycle
- `TreatmentPlanStatus.Stopped = 5`, **appended**. No migration: `Status` is already `integer` with no check
  constraint.
- `StopTreatment` writes `Stopped` instead of calling `Complete`. `Complete` keeps writing `Completed` and
  stays the automatic close fired by `AdvanceAfterWorkRecorded`.
- `Reopen` accepts `Completed` **and** `Stopped`.
- **« Terminer le traitement » is removed from the UI.** Completion is derived; its only unique case — closing
  with acts unrealised — is what « Arrêter » means, and « Arrêter » parks them reversibly instead of
  abandoning them. `CompleteTreatmentPlanCommand` stays (the automatic path and the API surface are unchanged).
- **« Annuler le devis » is removed from the UI and becomes a branch of the stop.** `StopTreatmentPlanCommand`
  gains an optional `Reason`; when nothing has been delivered on a **numbered** devis it calls `Cancel(reason)`
  instead of `StopTreatment`, and refuses with the existing sentence when no motif was supplied. The dialog
  already flips its own button to « Annuler le devis… » — the branch is computed, not a user decision.
- « Reprendre le traitement » becomes the primary action on `Stopped`, **ahead of** `canBill`.
- « Facturer le devis » drops out of the primary slot while any act is unrealised; the capability is unchanged
  and lives in the « ⋯ » menu.

### Échéancier

⚠️ **`Accept` keeps raising the container row, and the first draft of this spec was wrong to remove it.**
« Créances », « Solde patient », la caisse and the dashboard do **not** read `TreatmentPlan.Outstanding`
(`TotalPlanned − AmountPaid`); they sum `i.Amount - i.AmountPaid` **over the installment rows**
(`TreatmentPlanRepository.GetInstallmentOutstandingByPatientAsync:711-718`, and the same shape in the caisse
and dashboard aggregates). Delete the row and every accepted devis reports a **zero receivable** — the whole
ledger empties with no error anywhere. That is Option A's change of basis wearing a UI costume, and it is out
of scope here.

What changes is what the row **is rendered as**, which is where the confusion actually lives:

- An **auto-raised** row is never drawn as a dated échéance. It renders as « **Solde à régler** » with no date
  and no « En retard » badge — today it prints a due date nobody agreed *and* a badge saying so, in one row.
- The dated échéancier **table** renders only when a dentist-typed row exists
  (`installments.some(i => !i.isAutoRaised)`); otherwise the panel is the balance, the payments, and an
  « Échelonner le paiement » link — which is `ReviseInstallments`, unchanged, and replaces the container row
  with a real schedule.
- `InstallmentLateness.IsLate` drops the auto-raised branch's date arithmetic. Two sentences, one per row kind:
  - **échéance convenue** (`!IsAutoRaised`) — the agreed day has passed and the row is unpaid;
  - **solde à régler** (`IsAutoRaised`) — the work is finished and the balance is unpaid. No date test, so the
    fabricated date stops being load-bearing for anything a human reads.

### The workspace (`plan-workspace.tsx`)
- Header: title is the **treatment**; one meta line carries patient · praticien · dents · date; the devis
  number moves to the right in small mono. Below it, two blocks side by side — **avancement** (one pip per
  séance + « 3 séances sur 6 faites ») and **prochaine séance** (named, with its button).
- Order: **Le traitement** (acts) before **L'argent**. Single full-width column.
- The acts table is **unchanged** — `Ordre · Désignation · Dents · Coût · État · Action`, with `PlanStepStrip`
  in the Désignation cell. Two additions, both inside existing cells: the **État** cell states
  « dès le 3 juin » when the protocol interval has not elapsed and « N séances non faites » on a stopped plan;
  the mute « Étapes » icon becomes the word « **Séances** ».
- L'argent: three figures (Total convenu · Encaissé · **Reste à encaisser** / **Reste dû**) and the list of
  payments. The dated échéancier table renders **only when a schedule exists**; otherwise a
  « Échelonner le paiement » link.
- « Parcours » folds to the bottom as « Historique du devis », collapsed. Nothing removed.
- Menu « ⋯ »: Devis PDF · Envoyer par e-mail · Modifier les actes et les prix · **Arrêter le traitement** ·
  Supprimer le traitement. Five entries, down from seven.

### Wording
- `PLAN_STATUS_LABELS.Stopped = "Arrêté"`, `PLAN_STATUS_TONE.Stopped = "negative"`.
- The stop dialog states, in words, that **an act with delivered work is kept at its full price** — a
  four-séance implant stopped after two is billed 1 500 DT. That is what `StopTreatment` does today; nothing
  about it changes here except that it is now said out loud.

## Acceptance Criteria

- **AC-1:** `StopTreatment` leaves the plan `Stopped`. A plan stopped and then reopened returns to
  `OpenStatusFromWork`, with every parked act restored to the état its own steps derive.
- **AC-2:** `Reopen` accepts `Stopped` and `Completed` and refuses every other status with
  « Seul un traitement terminé ou arrêté peut être repris. »
- **AC-3:** A `Stopped` plan **carries debt**: it appears in « Créances », « Solde patient », la caisse, the
  dashboard money reads and « Chèques à encaisser », on its re-spread total. `DebtBearingPlanStatuses` and
  `CarriesDebt` both include it.
- **AC-4:** A `Stopped` plan is **not live**: it is absent from « Traitements suivis », the odontogramme rings,
  the booking dialogs' devis acts and the patient band's lead plan. `TreatmentPlanLifecycle.LiveStatuses` and
  `isPlanLive` are **positive lists**, not `!= Cancelled && != Completed`.
- **AC-5:** A `Stopped` plan is payable (`EnsurePayable`), correctable (`EnsureCorrectable`), amendable and
  billable; it may not receive a new séance (`EnsureActive`) and may not be stopped or completed again.
- **AC-6:** `POST /{id}/stop` with nothing delivered on a **numbered** devis cancels it, requires a motif, and
  returns the plan `Cancelled`. With nothing delivered on an un-numbered Draft it stops as before, no motif.
- **AC-7:** `Accept` still raises the container row — a test pins it, with the reason, so nobody removes it
  again. « Créances » for a freshly accepted devis is unchanged, to the millime.
- **AC-8:** An auto-raised row renders as « Solde à régler », with **no date** and no « En retard ». The dated
  table appears only when `installments.some(i => !i.isAutoRaised)`.
- **AC-9:** `InstallmentLateness.IsLate` returns false for an auto-raised row while any act is unrealised, and
  true once every act is Done and the row is unpaid — **with no reference to `dueDate`**. A dentist-typed row
  is late on the day after its agreed date, whatever the clinical progress.
- **AC-10:** The workspace header offers **exactly one** filled button, and it is: Draft → « Éditer le devis » ·
  Stopped → « Reprendre le traitement » · billable and no unrealised act → « Facturer le devis » · otherwise
  none. « Prochaine séance » carries « Planifier » separately.
- **AC-11:** The « ⋯ » menu holds five entries. « Terminer le traitement » and « Annuler le devis » are absent
  from every surface.
- **AC-12:** The acts table keeps its six columns and its step strip. The **État** cell adds the interval hint
  and the stopped count; the **Action** cell's step control reads « Séances ».
- **AC-13:** L'argent renders three figures and the payment list. The dated table appears only when
  `installments.some(i => !i.isAutoRaised)`; otherwise the « Échelonner le paiement » link.
- **AC-14 (guard):** `TreatmentPlanStatusCoverageTests` enumerates `TreatmentPlanStatus` and fails when a
  member is classified by neither `CarriesDebt` nor `LiveStatuses` — so the next appended member cannot be
  missed at the two places where a miss is silent and expensive.
- **AC-15 (guard):** `check:responsive` N23 (`a-live-treatment-has-one-test`) is extended to fail on a
  hand-written negative status test (`!== "Completed"`, `!== "Cancelled"`) as well as on the positive
  comparisons it already catches.
- **AC-16 (device):** At 320 px the header's two blocks stack, the acts table becomes the card tree at
  < 900 px of container width, and every control clears 44 px on a coarse pointer.
- **AC-17:** Dead code removed: `confirmAccept` (workspace), `AmendTreatmentPlanCommandHandler.EnsureNotBilledAsync`,
  the `isBeforeToday` import in `plan-workspace.tsx`.

## API Contract

- `TreatmentPlanDto.status` gains the string `"Stopped"`.
- `POST /api/treatment-plans/{id}/stop` gains an optional `reason` (string, ≤ 1000). Returns `Cancelled` when
  it took the cancel branch, `Stopped` otherwise.
- `POST /api/treatment-plans/{id}/reopen` now accepts a `Stopped` plan.
- `POST /api/treatment-plans/{id}/accept` and `/issue-devis` no longer create an installment.
- `POST /api/treatment-plans/{id}/cancel` stays, unused by the UI.

## Data / Schema Changes

**None.** `TreatmentPlans."Status"` is `integer NOT NULL` with no check constraint, so appending `Stopped = 5`
needs no migration. ⚠️ Run `dotnet run -- verify-schema` either side to prove it.

## Out of Scope

- **Option A — debt on a delivered basis.** `CarriesDebt` keying on `Number` rather than on the status, and
  the outstanding computed from delivered work. It is the right end state and it moves reported receivables,
  which is a management decision. This spec is a step toward it, not a substitute.
- Archiving the devis PDF at issuance (it re-renders live from current state under the same number).
- Giving the recall worklist a screen (`recallsApi` has zero callers).
- Pro-rata billing of a kept-but-unfinished act.
- Any change to the booking dialogs, the fiche de soins, or the patient page.

## Edge Cases (Critical only)

- **A stopped plan with a live note d'honoraires.** `planIsBilled` still suppresses lateness and the
  échéancier's « Encaisser »; the workspace's billed notice is unchanged.
- **A stopped un-numbered Draft.** All acts park, `TotalPlanned` is 0, `Number` stays null → `Stopped` with no
  debt (`CarriesDebt(Stopped)` is true but the total is 0, and the plan has no number). « Reprendre » is the
  primary action, which is the dead end this feature exists to remove.
- **Legacy rows already `Completed` by a stop.** None exist (0 parked acts on the dev database) and none are
  backfilled: a `Completed` plan stays `Completed`. ⚠️ Never infer a past stop from `RevisionNumber`.
- **`Version == 0` means "not supplied".** `StopTreatmentPlanCommand` already declares
  `SetExpectedVersion`; the cancel branch must declare it too or the fold loses the concurrency check.
- **A handler catch-all must carry `when (ex is not ConflictException)`.**
