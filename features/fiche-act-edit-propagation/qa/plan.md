# QA plan — « changer l'acte d'une fiche, et que tout suive »

Three fixes, one plan. Blast radius: `../blast-radius.md` (its rows are tier D verbatim).

⚠️ **Money/clinical writes.** The change under test IS writing a fiche, so recording and re-saving one is in
scope. Everything is done on a **test patient this pass creates**, never on a shared record
(`shared-stack.md` § 5). « Facturer » is pressed once, on that patient's own fiche, because the billing
guard's no-lines path is a `must re-test` row.

## Tier A — happy path

| # | Scenario | Expected (observable) | Layer |
|---|---|---|---|
| A1 | A past visit that already HAS a fiche: read its row on the patient page | the button reads « **Ouvrir la fiche** » | browser |
| A2 | Press it | the modal opens on the **recorded** act, not the booked one; « Enregistrer » (edit mode) | browser |
| A3 | A past visit with NO fiche | button reads « Enregistrer la fiche »; modal prefills the **booked** act | browser |
| A4 | Unbilled fiche → « Changer d'acte » → pick another → Enregistrer | toast « Fiche dentaire mise à jour »; reopening shows the new act | browser |
| A5 | Same, in the database | `DentalRecordActs.ProcedureName` = the new act; `DentalRecords.ProcedureType` follows | sql |
| A6 | Billed fiche (act swapped at the SAME price) → Enregistrer | refused: « Les actes de cette fiche sont facturés sur la note n° … » | browser |
| A7 | Devis-carried fiche → « Changer d'acte » → pick another act | the card stops reading « Chiffré sur le traitement »; a **Tarif** field appears with the catalogue price, not 0 | browser |
| A8 | …then Enregistrer | refused: « Aucun acte de la séance n'est « … » » + the remedy naming « Acte planifié » | browser |

## Tier B — alternate paths

| # | Scenario | Expected | Layer |
|---|---|---|---|
| B1 | Devis-carried card → « Changer d'acte » → re-pick the **same** act | still « Chiffré sur le traitement »; no Tarif field (the release must NOT fire) | browser |
| B2 | Devis-carried card → free text (« Utiliser ce texte ») | `billedOnPlan` cleared → Tarif field appears, empty | browser |
| B3 | After A7, set « Acte planifié » = Aucun → Enregistrer | saves; the refusal banner clears on the Select change | browser |
| B4 | Deep link `?addRecord=1&appointmentId=<visit that has a fiche>` | opens THAT fiche for editing, not a second composer | browser |
| B5 | PUT a fiche with `treatmentPlanItemId` + a non-matching catalogue act | 400, `{ error }` = the French sentence | api |
| B6 | Billed fiche, change only the **date** | saves (no acts-changed refusal) | browser |

## Tier C — edge cases

| # | Scenario | Expected | Layer |
|---|---|---|---|
| C1 | Fiche opened on a devis step, saved | the stored act carries a **`ProcedureTypeId`** (not NULL) | sql |
| C2 | Devis-carried fiche: save → reopen → save again untouched | the act, its price and its plan link are byte-identical after the 2nd save | sql |
| C3 | The record modal with the new refusal banner at **320 px** and **1536 × 730** | no horizontal scroll; the banner and « Enregistrer » both reachable | browser |
| C4 | A legacy fiche whose note lines were written by hand (designation ≠ what `For()` composes) | re-dating it is NOT refused | api |

## Tier D — regression sweep (from the blast-radius table)

| # | Row it covers | Expected | Layer |
|---|---|---|---|
| D1 | `applyProcedure` ← `applyAppointment` | a visit booked with 2 acts prefills **both**, at their agreed prices | browser |
| D2 | `applyProcedure` ← `addFromProcedure` | « aussi prévu à ce rendez-vous » chip fills the trailing blank card | browser |
| D3 | `DentalRecordBillingGuard.Check` ← `BillDentalRecordCommand` | « Facturer cette intervention » on an unbilled fiche still raises a note | browser |
| D4 | `appointmentVisitState` / the two buttons | the visit table at **820 px** keeps its Actions column with the new label | browser |
| D5 | `PlanItemPrefill` ← `handlePlanItemLink` | linking a devis act by hand still carries its désignation, teeth and 0 | browser |
| D6 | `DentalRecordBillingRefusals` | « Corriger la note » still gets past an acts-changed refusal | browser |

**Not exercised deliberately:** nothing yet. Any row that cannot be reached is reported ⏭ with its reason.
