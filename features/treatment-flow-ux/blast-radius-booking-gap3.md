# Blast radius — booking gap 3 (run3 A6/A7/A9/A15/D3 · gap2 C3/K3/W8)

Written before the first edit. Items numbered as in the task.

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `PaidOnTreatment` lock icon (`appointment-acts-picker.tsx`) | tag under a booking act's price | picker rows only (devis act, split act, continuation « Sur la note ») | unaffected — the fiche's `record/act-card.tsx` tag is its own component |
| 2 | `planActLabel` teeth shape (`plan-next-action.ts`) | `PresetPlanAct.label` for every devis act | create dialog patient chip · picker « Actes du devis » list + cmdk value · `presetToSelectedAct.fallbackName` → `actDisplayName` → row title, aria-labels, `actLabelsOf` → récapitulatif | must change with #4 (row title splits the teeth off the name) |
| 3 | `continuationToSelectedAct` teeth shape (picker) | continuation row's `fallbackName` | same readers as #2 | must change with #4 |
| 4 | `splitTeeth` (picker) | parses the teeth off a row name into a chip | row title only | must change — reads « · dent(s) N » AND the legacy « (dents N) » (stored appointment names) |
| 5 | récap act line (`appointment-recap.tsx` `RecapBlock`) | `actNames.join(" · ")` | both dialogs' rail; the bar shows a count only | must change — « · dent 26 » inside a « · »-joined list is ambiguous → one line per act |
| 6 | `PlanCard` / `PastCard` button variant | « Planifier : … » / « Continuer » | both dialogs' `ContinueTreatmentList`; e2e `booking-browser.spec.ts:410` finds « Continuer » by text | unaffected — text, size, action unchanged |
| 7 | `PRESELECTED_NEXT_LABEL` → « Séance suivante » | default séance name from « Planifier la suite » | `visits/unfinished-acts-list.tsx` (not mine, keeps the import) → sent as `nextStepLabel` on save | must re-test — /a-cloturer « Planifier la suite » → row name + saved step label |
| 8 | acts badge + row chair time → `formatDurationFr` | « 1 acte · 1 h » | picker only; `formatDurationFr` imported from `appointment-recap` (recap imports nothing from the picker — no cycle) | unaffected |
| 9 | `procedure-types-table.tsx` Actions / Durée / Prix cells, header « Prix par défaut », card label « Prix » | catalogue table | `/procedure-types` only | must re-test 1180 / 1440 + card form |
| 10 | `ProcedureStepsCell` strip wrapper (`procedure-type-steps-dialog.tsx`) | the strip no longer sizes the name column | both trees of `procedure-types-table.tsx` only | must re-test (same pass as #9) |
| 11 | rail label « Traitements et devis » → « Traitements » (`lib/nav.ts`) | nav item name | rail + drawer (`dashboard-sidebar`), bottom bar (not in `BAR_HREFS`), `treatment-plans-table.tsx` `ariaLabel` copy | must change — the table's `ariaLabel` copy (Edit only); e2e has no copy |

Capabilities moved: none. Nothing removed.
