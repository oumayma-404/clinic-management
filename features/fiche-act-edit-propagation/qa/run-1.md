# Run 1 — « changer l'acte d'une fiche, et que tout suive »

**Status: RED (2 findings — 1 blocker, 1 major)**

| | |
|---|---|
| Date | 2026-09-22 |
| Environment | API rebuilt from this branch (Debug, pid 43192) · `next dev` restarted fresh (pid 54712, the previous one was 21 h old and I had just overwritten `.next`) · Postgres/MinIO shared |
| Peers live | `clinic-management-a8` (busy, delete-path work), `clinic-management-af` (busy, devis audit — source-reading only), `clinic-management-20` (released api+web to me), `clinic-management-d6`, two `anakin-*` |
| Widths looked at | **1536 × 730** (the owner's real laptop page height) |
| Data | patient « abc abc » `c174d1e7`, a pre-existing dev test record. Nothing was written: every save in this run was refused or cancelled, verified in SQL. |

## Scenarios

| # | Outcome | Evidence |
|---|---|---|
| A1 · documented visit's row reads « Ouvrir la fiche » | ⏭ | **Not reachable.** Every past visit with a fiche on this database is `Completed`, and `canRecordVisit` excludes `Completed`/`Cancelled`/`NoShow` — so the two buttons never render for a documented visit. See finding 3. |
| A2 · pressing it opens that fiche | ⏭ | same reason |
| A3 · undocumented visit still composes a new fiche from the booking | ⏭ | no past non-Completed visit without a fiche on this clinic |
| A4/A5 · unbilled fiche, act changed, persists | ⏭ | every fiche on this patient is billed or devis-carried; not exercised rather than mutating another patient's record |
| **A6 · billed fiche, act swapped at the SAME money → refused** | ✅ | Gingivectomie 50,000 (note 2026-0447) → Détartrage retyped to 50,000 → save refused and the « **Corriger la note de cette séance** » dialog opened. SQL after: still `Gingivectomie @ 50.000`. **This is the case that used to save silently.** |
| **A7 · devis-carried card releases on an act change** | ✅ | « Aucun honoraire sur cette séance. » replaced by a real « Montant forfaitaire » field at **60,000** (the catalogue tarif, not the devis' locked 0); séance total « 1 acte · 60,000 DT ». `shots/A8-refusal-1536x730.png` |
| **A8 · saving that state is refused** | ✅ | toast + inline alert « Aucun acte de la séance n'est « Suite » » / « Remettez l'acte du devis, ou choisissez « Aucun » dans « Acte planifié »… ». Modal stayed open, nothing written. |
| **B1 · re-picking the SAME act does NOT release** | ✅ | « Aucun honoraire sur cette séance. » kept, no price field, « Total »/« Payé » still withheld. **But see finding 2.** |
| B2 · free text clears `billedOnPlan` | ⏭ | one line, symmetric with the verified A7 release; not driven |
| **B3 · the refusal clears when « Acte planifié » changes** | ✅ (banner) / ❌ (remedy) | the banner cleared on the Select change — **but the Select snapped straight back to « 2026-0395 · Suite ». Finding 1.** |
| **B4 · deep link on a visit that already has a fiche** | ✅ | `?addRecord=1&appointmentId=8d8f4328…` opened « **Modifier** la fiche médicale » on the existing Gingivectomie fiche, not « Ajouter » prefilled with the booked Coiffage pulpaire. `shots/B4-deeplink-opens-existing-fiche.png`. **This is how that appointment got its two fiches.** |
| B5 · server refusal over the wire | ⏭ | would need a separate session family; covered by `PlanCarriedActMismatchTests` + the derived caller guard |
| B6 · billed fiche, date only | ⏭ | covered by the three `DentalRecordCorrectionTests` re-dating cases, green |
| C1 · plan prefill stores a `ProcedureTypeId` | ⏭ | needs a fresh fiche off a devis step; the existing rows predate the fix |
| C2 · save → reopen → save again | ⏭ | requires a save, and every save in reach is refused by design |
| **C3 · 1536 × 730** | ✅ | refusal banner, act card and « Enregistrer » all on screen, no horizontal scroll |
| C4 · legacy note lines | ✅ (test) | `DentalRecordBilledActsUnchangedTests.A_Note_Whose_Stored_Lines_Differ…`; the three peer-reported `DentalRecordCorrectionTests` are green again |
| D1/D2/D5 · prefill regressions | ⏭ | not driven this run |
| **D6 · « Corriger la note » still reachable past an acts-changed refusal** | ✅ | the dialog opened from A6 with both figures (annulée 50,000 / nouvelle 50,000) |

## Findings

### 1 · blocker — the remedy my own refusal names cannot be carried out

« Aucun » is **unselectable** in « Acte planifié » on any reopened devis-carried fiche. Choosing it clears the
refusal banner and then the Select immediately shows « 2026-0395 · Suite (dents 28) » again.

*Cause:* `web/components/patient-record-modal.tsx:736-743` — the record-hydration effect has
`linkedPlanItemId` in its deps and its guard is `if (linkedPlanItemId !== NO_PLAN_ITEM) return`, so the moment
the value returns to `NO_PLAN_ITEM` the effect re-fires and puts the plan item back.

*Pre-existing*, but A8's refusal promotes it from a nuisance to a **dead end** — the same « a refusal whose
remedy is unreachable » shape `DentalRecordBillingGuard.Snapshot.StillBillsTheWork` was written to remove.

*Fix:* hydrate **once per open**, via a ref reset on open/record change (`createdPatientIdRef`'s shape), not
whenever the value happens to be `NO_PLAN_ITEM`.

### 2 · major — « Enregistrer — 30,000 DT » on a séance whose card says « Aucun honoraire sur cette séance »

Re-picking the **same** act on a devis-carried card leaves `billedOnPlan` true (correct) but pulls the
catalogue tarif into `unitCost`, because `applyProcedure` reads
`unitCostLocked ? unitCost : pt.defaultCost` and a reopened card is not `unitCostLocked`. The price field
stays hidden, so the only place the figure shows is the primary action — which then contradicts the card.

Measured: button read « Enregistrer » before « Changer d'acte », « Enregistrer — 30,000 DT » after re-picking
Coiffage pulpaire. « Total »/« Payé » stayed withheld and « Encaissé sur le traitement » stayed offered, so
nothing else moved, and `PlanCarriedActPricing` imposes the 0 server-side — **no stored money is wrong**.

*Pre-existing*; exposed by B1. *Fix:* keep the carried 0 when the act is still `billedOnPlan` after the
release test — the mirror of the release this feature added.

### 3 · observation — Fix 1's button half is unreachable on this data

`canRecordVisit` already excludes `Completed`, and saving a fiche marks the visit Completed, so the two
buttons never render for a documented visit. The label change only shows when `MarkVisitCompleted` did not
land. **The deep link (B4) is Fix 1's real door**, and it works.

### 4 · observation — with 2+ fiches on one visit the deep link opens an arbitrary one

`ficheForVisit` uses `.find()`. Better than minting a third, but not a chosen fiche. Appointment
`8d8f4328` (2 fiches) and four others already exist on this database.

### 5 · observation — the inline refusal shows the headline, the remedy is toast-only

`refuseSave(key, message, description)` puts only `message` in the banner. Consistent with the other
refusals, but this one's `description` is the actionable half and a toast is transient.

## Off-plan

None.
