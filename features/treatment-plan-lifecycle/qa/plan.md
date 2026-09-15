# QA plan — treatment-plan lifecycle remediation (Phase 6 step 5)

**Change under test:** the uncommitted Phases 1–5 of `plan-remediation.md` — 61 review findings plus seven
capabilities (S1–S7). 24 rendering files moved (`git diff HEAD --stat -- web/`), plus the whole
`TreatmentPlan` aggregate and 20 handlers.

**Size:** large · money · clinical · shared surfaces → **all four tiers, tier D exhaustive.**

**Environment**
- API :5000 — rebuilt from this working tree, `AddTreatmentPlanDiscountAndWriteOff` applied, owned by `phase6`.
- web :3000 — `.next/standalone/server.js` from the build that passed the gate. **Not `next dev`.**
  ⚠️ `FrontendUrl` is `http://localhost:3000`, so :3100 is refused by CORS and every browser row dies as
  « Connexion au serveur impossible ». That is an environment result, not a product one.
- Account: the e2e QA admin (`e2e/credentials.local.json`), clinic `f64a8a75…` (1366 patients, 706 plans).
- **Test data is minted by this walk**, under a patient named `QA-PHASE6-<runid>`. Nothing in the shared
  1366-patient dataset is written (`shared-stack.md` § 5).

---

## Blast radius (written before the walk — tier D comes from here)

| Symbol touched | Who else consumes it | Verdict |
|---|---|---|
| `TreatmentPlanStatus` (+`Stopped`, +`WrittenOff`) | `PlanBillingRules.CarriesDebt`, `TreatmentPlanLifecycle.IsLive`, `PLAN_STATUS_LABELS`/`_TONE`, `planStatusCounts` | **must re-test** — D2 D3 D5 D8 |
| `PlanBillingRules.DebtBearingPlanStatuses` | « Créances », « Solde patient », la caisse, dashboard money, chèques | **must re-test** — D1 D2 D3 D4 D5 |
| `TreatmentPlan.TotalPlanned` (now sums **net** of `DiscountAmount`) | échéancier, devis PDF, every balance read | **must re-test** — D1 D2 D3 |
| `ContinuationTracking` | « Suite d'une séance précédente », the booking dialog, « Suites à planifier » | **must re-test** — D9 |
| `NoteCarriedActGuard` / `CreateInvoiceFromTreatmentPlanCommand` | the fiche's note d'honoraires, `PlanCarriedActPricing` | **must re-test** — D7 |
| `RecallWorklistRules` | the recall worklist reads | re-test on the wire only (no screen) |
| `DashboardActivityReader` / `DashboardAlertsReader` | the dashboard | **must re-test** — D5 |
| `odontogram.tsx` (+`TreatmentPlans` realtime key) | both drawings on « État dentaire » | **must re-test** — D6 |
| `patient-record-modal.tsx` | the fiche de soins editor | **must re-test** — D7 |
| `treatments-in-progress-list.tsx` | « Traitements suivis » | **must re-test** — D8 |
| `plan-next-action.ts` (+287 lines) | the workspace header, the ⋯ menu, the patient band, the list row | **must re-test** — A1 A2 B7 B8 |

---

## Scenarios

Layer: `sql` = a read-only query · `api` = one curl · `browser` = only a browser can see it.
⚠️ Anything living in `.tsx` or `web/lib/*` is always `browser`.

### Tier A — happy path (one per capability / acceptance criterion)

| id | Scenario | Expected (observable) | Layer |
|---|---|---|---|
| A1 | Open a **Draft** plan's workspace | header has **exactly one** filled button and it reads « Éditer le devis » | browser |
| A2 | Open the ⋯ menu on a numbered live plan | entries include « Facturer le devis », « Annuler le devis », « Dupliquer », « Régler le devis », « Passer en perte » — and **not** « Terminer le traitement » | browser |
| A3 | « Arrêter le traitement » on a plan with a deposit | status chip reads « Arrêté »; acts show « mis de côté »; total re-spread | browser |
| A4 | « Reprendre le traitement » on that plan | status returns to the état its steps derive; parked acts restored; **balance identical on the workspace AND on « Créances »** | browser + sql |
| A5 | « Annuler le devis » on a plan holding money | the dialog **states the encaissé amount** and « Cette action est irréversible » | browser |
| A6 | Open a **Cancelled** plan | primary action is « Reprendre ce devis annulé »; pressing it returns the plan to its prior état | browser |
| A7 | « Mettre de côté » one act | an `AlertDialog` names what is kept; the row then carries a « mis de côté » marker | browser |
| A8 | « Remettre au devis » that act | the marker clears; `TotalPlanned` returns to its pre-park figure | browser + sql |
| A9 | « Modifier les actes et les prix » from the **list** row of a followed Draft | the **amend** modal opens (not the create form) | browser |
| A10 | « Facturer le devis » from the ⋯ menu | a note d'honoraires is raised and the plan leaves « Créances » | browser + sql |
| A11 | « Dupliquer » a devis (S1) | a new plan, `Draft`, **no number**, same acts and steps | browser + api |
| A12 | Set a remise on one act (S2) | the act's fee reads net; the devis total drops by exactly the remise | browser + sql |
| A13 | « Régler le devis » (S3) | one modal; the amount spreads over the échéancier; « Reste » reaches 0 | browser |
| A14 | « Passer en perte » (S4) | status « Passé en perte »; the plan leaves « Créances » and « Solde patient » | browser + sql |
| A15 | An act with `MinDaysAfterPrevious` | the row states « pas avant le … » — and **never** the word « en retard » | browser |
| A16 | « Détacher la fiche » with « …et la rattacher à » (S6) | one save; the fiche lands on the named act · étape | browser |
| A17 | Same `procedureTypeId` + tooth on two debt-bearing plans | a **notice**, never a refusal | browser |
| A18 | The workspace header's praticien | named, and editable | browser |

### Tier B — alternate paths (the other branch of every `if`)

| id | Scenario | Expected | Layer |
|---|---|---|---|
| B1 | « Arrêter » a **numbered** devis with nothing delivered | it takes the **cancel** branch: motif required, confirm disabled with an `aria-describedby` hint, result `Cancelled` | browser |
| B2 | `POST /reopen` on a live plan | refused with « Seul un traitement terminé ou arrêté peut être repris. » | api |
| B3 | A remise greater than the act's `PlannedCost` | refused | api |
| B4 | Cancel with an empty motif | confirm stays disabled; label reads « Motif d'annulation (obligatoire) » | browser |
| B5 | A plan whose only échéance is auto-raised | renders « Solde à régler », **no date**, no « En retard »; no dated table | browser |
| B6 | A plan where « Encaisser » is withheld | a notice naming the actual reason (facturé · brouillon · annulé) | browser |
| B7 | A **Completed** plan | **no** filled primary; « Reprendre le traitement » sits in the ⋯ menu | browser |
| B8 | A **Cancelled** plan's « Prochaine séance » block | « Devis annulé » + the motif | browser |
| B9 | A plan with zero acts | a muted sentence naming why there is nothing to do — never a blank | browser |
| B10 | A **Stopped** plan | present in « Créances » (AC-3) and **absent** from « Traitements suivis » (AC-4) | browser + sql |

### Tier C — this repo's standing edge cases

| id | Scenario | Expected | Layer |
|---|---|---|---|
| C1 | Stale version → 409 on the devis form | `FormErrorBanner` offers a **« Recharger »** action; a second press is not silently refused | browser |
| C2 | Save the devis form, reopen, save again | the acts, prices and steps are unchanged after the **second** save | browser + sql |
| C3 | Every échéance surface at **320 px** | no horizontal scroll; rows wrap; no control below 44 px on a coarse pointer | browser |
| C4 | The workspace + the three dialogs at **1536 × 730** | the footer buttons are reachable without the dialog clipping | browser |
| C5 | A plan update DTO with the acts key **absent** | the acts are unchanged — « omitted » still means « unchanged » | api |
| C6 | A plan with many acts (≥ 8) and one with one | both render; the card tree appears below ~900 px of container width | browser |

### Tier D — regression sweep (one row per blast-radius `must re-test`)

| id | Screen | Expected | Layer |
|---|---|---|---|
| D1 | La caisse — day + extrait | totals unchanged against the `reconcile-money` baseline | browser + sql |
| D2 | Créances | a `Stopped` plan present, a `WrittenOff` and a `Cancelled` one absent | browser |
| D3 | Patient page — « Solde dû » breakdown + the plans strip | the breakdown sums to the figure above it | browser |
| D4 | Chèques à encaisser | renders; dates read through `installmentDueLabel` | browser |
| D5 | Dashboard | money reads and alerts render with no console error | browser |
| D6 | Odontogramme — « État dentaire », **both** drawings | rings from live plans only; the Cases/Symboles switch drives both | browser |
| D7 | Fiche de soins modal — a devis-carried act | the price field is withheld; « Aucun honoraire sur cette séance » | browser |
| D8 | « Traitements suivis » | a followed Draft present; a Stopped plan absent | browser |
| D9 | Booking dialog — a continuation | « Suite d'une séance précédente » still offered and still creates exactly one devis | browser |

---

## Budget

18 A + 10 B + 6 C + 9 D = **43 rows**, of which 6 are `api`/`sql` and answered before the browser opens.
37 browser rows in one launch.

## Status

`PLANNED` — no browser opened yet.
