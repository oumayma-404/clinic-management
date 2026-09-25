# Blast radius — patient file + treatment lists (L1 · P1/P2/P3)

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `PatientPlansStrip` props (+ optional `onRecordVisit`) | patient band | `app/patients/[id]/page.tsx` only | must change — page passes `openVisitRecord` (additive prop, absent = old navigation) |
| 2 | `PatientPlansStrip` booking (mounts `CreateAppointmentDialog` with `planItemToPreset`) | new door onto the existing booking dialog | same preset builder as `treatments-in-progress-list` / `treatment-plans-table` | must re-test — same payload, `defaultDay` (N20), `presetPlanId` |
| 3 | Draft « Créer le devis » (accept) moves to the card's « ⋯ » | `treatmentPlansApi.accept` + confirm | only this strip calls it on the patient page; workspace has its own | must re-test — same call, same confirm before it |
| 4 | `planHeadline`, `leadPlan` | helpers in `plan-next-action.ts` (not mine) | only the strip read them | unaffected — left in place, now unused by the strip |
| 5 | `PlanActPips` visible text + `PlanActPipsLegend` words; new `seanceCountLabel` export | progress pips | strip (pips), table + strip (label); legend has no caller | unaffected — additive export, text only |
| 6 | `planSeanceProgress().label` | « 2 / 5 séances » string (not mine) | `plan-workspace.tsx` (other worker), table stops reading `.label` | unaffected — not edited; table reads `done/total` |
| 7 | `TreatmentsInProgressList` cells/labels | worklist | `app/treatment-plans/page.tsx` | must re-test — props unchanged, « Planifier la séance » still opens booking |
| 8 | `TreatmentPlansTable` labels, menu, dialogs | devis list | `/treatment-plans` + patient tab « Plan de traitement » | must re-test both hosts — same calls, same confirms, same refusals |
| 9 | `app/treatment-plans/page.tsx` heading/hint/subtitle | page chrome | url seeding `?status` `?acceptedFrom/To` from `lib/dashboard-links.ts` | unaffected — seeding code untouched |
| 10 | `PatientOutstandingStrip` labels | « Reste à payer » section | patient page; `PATIENT_OUTSTANDING_SECTION_ID/TAB`, `patientOutstandingHref` (e2e PLAN-04) | unaffected — ids/exports unchanged |
| 11 | page « Solde dû X › » chip → « Reste à payer » | header control | none (e2e greps: comments only) | unaffected |
| 12 | `ProcedureStepsCell` display → `SeanceStrip` | catalogue protocol cell | `procedure-types-table` (card `underTitle` + table cell) | must re-test — still opens the same dialog |
| 13 | `ProcedureTypeStepsDialog` labels | protocol editor | same | unaffected — payload `{ defaultSteps, version }` unchanged |
| 14 | `odontogram.tsx` words (Réalisé → Fait, « Créer un plan » → « Créer un devis ») | chart strings | N37 anchors on code, not the tab label | unaffected |
| 15 | N31 (`step-counter-says-done-or-to-do`) tripwire | guard | fires if NO step counter is left repo-wide | must re-test — run gate |

## Capabilities moved (nothing removed)

| Capability | Before | After |
|---|---|---|
| Accept a Draft (« Créer le devis ») from the patient file | headline button « À accepter » | card « ⋯ » menu → same confirm → same call |
| Book the next séance | « Planifier la suite → » navigated to the treatment page | « Planifier : {séance} » opens the booking dialog directly (fallback: treatment page) |
| Record the fiche of a passed séance | button navigated to the treatment page | « Enregistrer la fiche » opens the fiche on the page (fallback: deep link) |
| Open the treatment | name + button | name is a link + « ⋯ » « Voir le traitement » |
| « Facturé — N » link to /factures | line 1 | card row 1 (same link) |
| « révision N » | line 1 | card row 1 |
| Other treatments' statuses (chips → plans tab) | line 2 | under the cards |
| Per-act detail (état, next séance, last séance) | fold | fold (same content, words not fractions) |
