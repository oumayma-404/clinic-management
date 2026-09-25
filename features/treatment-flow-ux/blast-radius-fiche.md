# Blast radius — fiche de soins (F1–F5, P1/P2)

Display only. No request, payload, reducer action or tri-state rule changes.

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `patient-record-modal.tsx` header (title, description, Patient/Date grid, two Selects) | layout + the « Acte planifié » / « Séance » Selects | none outside the modal; state `linkedPlanItemId` / `chosenStepId` + `handlePlanItemLink` are reused as-is | must change — the « Changer ▾ » menu calls the SAME `handlePlanItemLink` / `setChosenStepId`, guarded so re-picking the current value is a no-op (a Select never fires on its own value) |
| 2 | `seanceStepLine` memo → `seanceStepIds` (which séances this fiche records) | display derivation | `stepLineActKey`, ActCard `seanceStepLine` prop; e2e FICHE-20/22 asserts « étape 1 sur N » | must change — same resolution order (record → booked rows → chosen → first pending); e2e moved to the strip's `aria-current` step |
| 3 | `planNotice` node (« Chiffré sur le devis… », « Encaissement sur la note… », « Suite de ») | display | ActCard `planNotice` prop only | must change — becomes one tag per carried card (`paidOnLabel`) + « Suite de » chip |
| 4 | `ActCard` props (`planNotice`, `seanceStepLine`, `proposedFromAppointment` removed; `paidOnLabel`, `continuationOf` added) | component API | only `patient-record-modal.tsx` | must change — both files in this commit |
| 5 | ActCard body: tag instead of « Aucun honoraire », « par dent · pour tout », « ↺ tarif X », « À continuer une autre séance », link « Changer d'acte » | display | e2e `browser-hot-paths` (« Aucun honoraire », aria-label « Montant forfaitaire (DT) »); N21 (tariff difference must consult `billedOnPlan`), N44, N36 | must change e2e; N21 kept (`tariff - typedUnit` still computed for the aria-label); N36/N44 untouched (no reducer / payload edit) |
| 6 | footer money band (« Payé aujourd'hui », trio Prix · Déjà payé · Reste à payer, refusals) | display over the same fields | `#collected-on-plan`, `#paid`, `#session-total` ids used by e2e; N27 (distributeSessionTotal) | unaffected ids kept; same state, same caps, same disabled rules; N27 untouched |
| 7 | `PlanItemOption` gains `planTotal`; page `label` becomes « Désignation · dent 16 » | page → modal contract | only `app/patients/[id]/page.tsx` builds it (shared file — Edit only, re-read first) | must change — additive field; `label` only rendered by the modal |
| 8 | save button label → « Enregistrer la séance » | display | e2e: no spec presses the fiche's save (grep) | unaffected |
| 9 | toasts « Fiche dentaire … » → « Fiche de soins … » | display | e2e: no assertion on them (grep) | unaffected |
| 10 | `act-detail-fields.tsx` (« Faces (N dents) », drop « alimente ») | display | only ActCard | unaffected |
| 11 | `act-catalog-picker.tsx` (key hints, free-text row) | display | only ActCard | unaffected — `onFreeText` path identical |
| 12 | `prescription-section.tsx`, `prescription-line-row.tsx` (instruction texts) | display | only the modal; N32/N33 (line composer, type set) untouched | unaffected |
| 13 | `record-section.tsx` | shared primitive (6 callers) | appointment-acts-picker, document editor, patient form, plan-workspace | NOT touched |
| 14 | `visit-closure-list.tsx` / `post-visit-review-popup.tsx` wording | display | `/a-cloturer` page, header; server-sent notification title/message untouched | unaffected |
| 15 | `SeanceStrip` (read only) | shared component | other workers' screens | unaffected — consumed, not edited |

## Capabilities moved (none removed)

| Capability | Before | After |
|---|---|---|
| Link the fiche to another plan act | « Acte planifié » Select | « Changer ▾ » menu, same options, same handler |
| Un-link (« Aucun »), incl. the way out of the automatic devis add | « Aucun » in the Select | « Sans traitement » in the menu |
| Pick the séance on a walk-in fiche | « Séance » Select (only when > 1 pickable) | same list, « Changer ▾ » menu, same condition |
| Which séance this fiche records | « Cette séance : étape N sur M · X » | `SeanceStrip` current séance (« aujourd'hui » / « cette séance ») |
| Devis number | Select label + notice | band chip « Devis n° … » / « Pas de devis » |
| Reset a price to the tarif | « Tarif catalogue … remettre au tarif » | « ↺ tarif X » link, aria-label keeps the geste/majoration figure |
| Change the act | outline button | link beside « Détails » |
| Chart legend caption | always-on caption | inside the « Légende » fold |
