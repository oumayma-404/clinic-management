# Blast radius — treatment page (T1–T5, P1/P2)

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `plan-workspace.tsx` header / menu / dialogs | page body of `/treatment-plans/[id]` | only `app/treatment-plans/[id]/page.tsx` | must re-test — every dialog, every menu item, the 3 stop branches |
| 2 | `plan-act-row.tsx` exports (`PlanActRow`, `PlanActPrimaryAction`, `planActCardFields`, `PlanActStepsAction`…) | act row, both trees | workspace only (grep) | must change together with the workspace |
| 3 | `planActCost` | N34 owner (exactly one declaration) | N34 tripwire | unaffected — kept, still the one reader for read-only cells |
| 4 | `plan-step-strip.tsx` | old strip | workspace/row only; N31 candidate source | must re-test N31 tripwire (other counters must still exist) |
| 5 | `plan-item-steps-dialog.tsx` | `setItemSteps` replace-whole-list | workspace only | must re-test — payload keeps `minDaysAfterPrevious` for every row; new optional `focusStepId`/`addOnOpen` props are additive |
| 6 | `treatment-plan-form-modal.tsx` amend builder | shared with create/update | `app/patients/[id]/page.tsx` (create + odontogram seed), `treatment-plans-table.tsx` (create/edit), workspace (amend) | must re-test all three hosts — builder extracted to `plan-amend-payload.ts`, same fields |
| 7 | new `plan-amend-payload.ts` | the one amend builder | form modal + workspace inline editor | must change — both use it, nobody re-lists fields |
| 8 | `setItemDiscount` (remise) | own command, not in amend payload | was the Remise dialog | must change — inline remise saved through the same command, after the amend, with the returned version |
| 9 | `installment-payment-modal.tsx` | échéance payment | patient page (`PatientOutstandingStrip` Encaisser), workspace | must re-test patient page Encaisser |
| 10 | `settle-plan-modal.tsx`, `revise-installments-modal.tsx`, `void-installment-payment.tsx`, `plan-refund-confirm.tsx` | money dialogs | workspace only (refund confirm: form modal too) | must re-test — same payloads, text only |
| 11 | `plan-timeline.tsx` | history feed | workspace only | unaffected — additive « révision » entry, empty state text dropped |
| 12 | `plan-progress-bar.tsx` | bar | `plan-act-pips.tsx` (patient band) | unaffected — label text only |
| 13 | `plan-next-action.ts` `actRemovalPlan` reason strings | named refusal | form modal (bin), N38 | must re-test — wording only (« Remettre à faire »), condition untouched |
| 14 | `EditAppointmentDialog` hosted for « Déplacer » | other worker's file, imported only | `/appointments` | unaffected — mounted as-is, fed by `appointmentsApi.get` |
| 15 | visible strings in e2e | `^Annuler$` header count, `Encaisser sur la note` link | `browser-hot-paths.spec.ts` | must re-test — both kept verbatim |

## Capabilities moved (none removed)

| Capability | Before | After |
|---|---|---|
| Planifier the next séance | NextSeanceBlock button + row « Planifier la séance » | header primary « Planifier : X » + séance popover « Planifier » (step-less act keeps row button) |
| Voir le RDV / Enregistrer la fiche / Voir la fiche | row primary action | séance popover (step-less act: row, as before) |
| Détacher la fiche (last séance) + relink | row « Détacher » on a Done act | popover « Remettre à faire » on the LAST done séance (same dialog, same relink); step-less act: row « Remettre à faire » |
| Détacher any done séance | steps dialog | unchanged (steps dialog) |
| Edit séances (rename, durée, délai, order, add, delete) | row « Séances » | popover « Modifier la séance » + « + » on the strip → same dialog; step-less act: « Découper en séances » |
| Modifier un acte (désignation, dents) | row « Modifier » → amend dialog | « Tout modifier » (menu + link under the list) |
| Prix d'un acte | amend dialog | inline field (amend) + « Tout modifier » |
| Remise | row « Remise » → dialog | inline field / « Remise » toggle, same `setItemDiscount` |
| Ajouter un acte | amend dialog | inline « + Ajouter un acte » (catalogue) + « Tout modifier » (free text) |
| Régler le devis | « L'argent » button | « Encaisser » (same settle modal) |
| Échéancier table, per-row Encaisser / Reçu / Annuler, Modifier l'échéancier | open card | behind « Échéancier (N) » fold, same controls |
| Devis PDF | menu | devis chip + menu |
| Éditer le devis (numbering) | header primary on a Draft | « Pas de devis · Créer le devis » chip (same confirm) |
| rév. N | identity line | history feed |
| 12 menu actions | flat | same, grouped Document · Modifier · Fin du traitement |
