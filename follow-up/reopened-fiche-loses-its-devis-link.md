# A reopened fiche de soins forgets which devis act it carries — and offers a discount nobody granted

**Feature:** `multi-seance-treatment-steps` (adjacent to « Deux surfaces annonçaient l'étape SUIVANTE… »)
**Type:** bug — money-adjacent
**Priority:** high
**Created:** 2026-09-08
**Found:** while verifying the step-naming fix in the browser at 1440 px, on the dev database

## What was observed

Opening a saved fiche of a multi-séance act — `/patients/047360d5-127f-42d0-af42-ee056f6f72d2?editRecord=84f7f853-1659-41b9-9363-d049b829fb0e`,
a « Couronne / bridge (par élément) » whose devis prices it at 500,000 DT — renders:

- **« Acte planifié : Aucun »**, though the record is linked to that devis act on the server.
- No « Suivi comme traitement. » / « Déjà facturé. » banner at all.
- The act card showing a locked **0,000 DT** beside
  **« Tarif catalogue 500,000 DT — geste de 500,000 DT »** and a **« remettre au tarif »** link.
- No « Encaissé sur le traitement » field, so the séance cannot take the patient's money.

The third one is the dangerous one. `patient-record-modal.tsx`'s own `markBilledOnPlan` back-fill exists in as
many words to prevent it — « without it the reopened card reads its stored 0 against the catalogue tarif and
announces « geste de 500,000 DT » with a « remettre au tarif » link, which is a discount nobody granted and one
press from re-charging the devis » — and it **cannot fire on this path**.

## Why (verified in source)

`linkedPlanItemId` has exactly three writers (`web/components/patient-record-modal.tsx`):

1. `useState(NO_PLAN_ITEM)`,
2. the open effect, which **resets it to `NO_PLAN_ITEM`** (`setLinkedPlanItemId(NO_PLAN_ITEM)`),
3. the appointment pre-select effect, which is guarded by `if (!open || record || …) return` — i.e. it
   **never runs when a record is being edited**,
4. the user picking from the « Acte planifié » Select by hand.

So on the edit path nothing hydrates it, `billedPlanItem` is null, and therefore `carriedByDevis` is false,
`collectsOnTreatment` is false, and the back-fill effect (keyed on `carriedByAppointment`, which needs
`billedPlanItem`) is dead. The page reinforces it: `recordAppointment` is deliberately null for
`editingRecord` (`app/patients/[id]/page.tsx`), on its own stated reasoning — a record being edited is never
re-proposed.

The record itself carries what is needed: `DentalRecordDto.treatmentPlanId` and the step read-back
(`treatmentStepLabel`/`Number`/`Total`), from `GetPlanLinksByDentalRecordAsync`. The **act id** is what is
missing from the DTO — `treatmentActDesignation` is a name, not an id — so the hydration needs either
`TreatmentPlanItemId` added to that projection, or a resolve of designation + plan against `planItems`.

## What was already fixed, and what was deliberately left

The step line **is** correct on this path: `seanceStepLine` reads the record's own `treatmentStepLabel` first,
and it is gated on itself rather than on `carriedByDevis` precisely because that flag is false here. That was
in scope (the reported bug) and is done.

The rest was **not** folded in: turning `carriedByDevis` true on the edit path switches on money controls
(« Encaissé sur le traitement », the `PlanCarriedActPricing` 0, the note-vs-devis wording), and that belongs in
a change that can be verified against `MoneyReadConsistencyTests` and a real collection walk — not in a
wording fix.

## The fix, when it is taken

1. Serve the act id: add `TreatmentPlanItemId` to the plan-link row `GetDentalRecordsQuery` merges
   (`DentalRecordPlanLinkRow` already carries the designation and the step).
2. Hydrate `linkedPlanItemId` from it in the open effect, next to the existing `dispatch({ type: "reset", record })`
   — **only** when that act is in `planItems`, matching the appointment effect's own rule (an act on a
   cancelled plan must still fall through).
3. Then `markBilledOnPlan` needs its guard widened: it is keyed on `carriedByAppointment`, and its comment
   says why it must not key on `carriedByDevis` (« the latter now reads the very flag this dispatch sets »).
   A separate « the record says it is carried » condition is the honest third input.
4. Verify: reopen a fiche of a carried act and confirm the card shows « Aucun honoraire sur cette séance »
   rather than a tarif gesture; confirm « Encaissé sur le traitement » appears and that re-saving the fiche
   collects nothing by itself; run the unfiltered `UnitTests` suite plus `reconcile-money`.

## Second, smaller finding from the same walk

`web/components/treatment-plans/treatment-plan-form-modal.tsx`'s « Séances » panel scrolls **horizontally**
inside the dialog body at 320 px (a grey scrollbar under the acts block). Pre-existing — present before this
change and visible in the before/after captures alike — and it is the `Coût (DT)` row rather than the step
list. § 11 says wide content scrolls in its own container, so it is not a violation as such, but the container
here is the whole dialog body, which is the shape that hides a control.
