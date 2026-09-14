# QA plan — treatment-plan act corrections

**Change under test.** Five things, all reachable from one devis workspace:

1. `TreatmentPlan.RemoveItem` refuses **only** on delivered work. A booked act is removable; the handler
   cancels a visit the act was the only reason for (nested `UpdateAppointmentCommand`) or drops the act from a
   visit that carries others (`SetProcedures`).
2. `actRemovalPlan()` (`plan-next-action.ts`) replaces `planItemState` as the amend modal's removability rule,
   and reports the booking so the confirmation can name it.
3. Changing the act in the catalogue picker clears `costTouched` + `stepsTouched`, so the fee **and** the
   protocol follow the new act — only when no séance of that act is done. Same for the « Traitements
   proposés » chips.
4. The devis act picker is grouped by discipline, with a colour dot, the price and an « N séances » pill.
5. `PlanActEditAction` (« Modifier ») on every act row and card, opening the amend modal with `focusItemId`.

**Budget.** Large change: shared primitive (`plan-next-action`), money (a devis total), clinical (a booked
séance). All four tiers, D exhaustive.

---

## Blast radius (built here — no prior table)

| # | Touching | What it is | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `TreatmentPlan.RemoveItem` | aggregate method, signature changed | `AmendTreatmentPlanCommand` (1), 3 unit tests | must change — done |
| 2 | `AmendTreatmentPlanCommandHandler` ctor | +`IMediator` | 2 test fixtures, DI | must change — done |
| 3 | `planItemState` | derived état | 8 call sites; only the modal's use changed | must re-test — the act row badge, the card list, the treatments worklist |
| 4 | `Appointment.SetProcedures` | replace-whole-list setter | now called from the amend handler | must re-test — a shared séance keeps its other acts **and their agreed prices** |
| 5 | `selectProcedureType` / `applyCandidate` | modal only | — | must re-test — the draft/creation path, where `costTouched` starts false |
| 6 | devis act picker JSX | modal only | — | must re-test — 320 px |
| 7 | `PlanActRow` | 2 mount sites (table + card) | plan-workspace only | must re-test — both trees |
| 8 | `groupProceduresByCategory` | shared, read-only | fiche picker, booking picker | unaffected — no edit to it |

---

## Scenarios

Legend — layer: `sql` = a read-only query · `api` = curl · `browser` = only a browser can see it.

### Tier A — happy path

| id | Scenario | Expected (observable) | Layer |
|---|---|---|---|
| A1 | On a devis with ≥2 acts, press « Modifier » on the **second** act row | The amend modal opens **and that act's line is ringed** (`ring-primary/40`), visible without scrolling | browser |
| A2 | In the amend modal, open the catalogue picker on a line | Items are under **discipline headings**, each with a colour dot and its price | browser |
| A3 | A multi-séance act in that picker | Carries an « N séances » pill | browser |
| A4 | On an existing act line, pick a **different** act from the picker | The désignation, **the fee** and the séances all change to the new act's; the modal's « Total » moves with it | browser |
| A5 | Save that amendment | Toast; the act row shows the new name **and the new price**; the plan total matches | browser |
| A6 | Bin on an act with **no** booking and no fiche | Line removed with **no** confirmation dialog | browser |
| A7 | Bin on an act with a **future** RDV that carries only it | Confirmation naming the RDV date and saying it will be cancelled | browser |
| A8 | Confirm A7 and save | Act gone from the devis; the appointment is **Cancelled** in the agenda | browser + sql |

### Tier B — alternate paths

| id | Scenario | Expected | Layer |
|---|---|---|---|
| B1 | Bin on an act that is **réalisé** (a fiche exists) | Bin **disabled**, and the line states « Acte déjà réalisé — utilisez « Détacher la fiche »… » | browser |
| B2 | Bin on an act with **1 of N séances** delivered | Bin disabled, reason names the séance count | browser |
| B3 | Bin on an act whose RDV **has already passed** and no fiche | Bin **enabled** (this is the old false refusal) | browser |
| B4 | Bin on an act sharing its RDV with another act of the devis | Confirmation says the visit **stays** for the other acts | browser |
| B5 | Cancel the removal confirmation | Line still present, nothing changed | browser |
| B6 | Re-pick the **same** act from the picker (typo-fix case) | Fee and séances **unchanged** — a stored fee is the agreed one | browser |
| B7 | On a **new** plan (create path), pick act 1 then act 2 | Fee follows both times (`costTouched` starts false) | browser |
| B8 | Server refusal: remove a réalisé act via the API directly | 400 naming « fiche de soins » | api |
| B9 | « Modifier » hidden on a **Cancelled** plan | No « Modifier » control on any act row | browser |

### Tier C — edge cases (only what this change reaches)

| id | Scenario | Expected | Layer |
|---|---|---|---|
| C1 | The amend modal at **320 px** | No horizontal page scroll; the picker popover inside the gutter; the bin reachable | browser |
| C2 | The amend modal at **1536 × 730** (owner's laptop) | Footer « Enregistrer la révision » reachable | browser |
| C3 | Act row + card at **820 px** | « Modifier » present in **both** trees and not pushed out of the box | browser |
| C4 | Save, reopen, save again with no edit | « Aucune modification demandée. » — the reprice must not fire on a reopen | browser |
| C5 | Swap an act on a line whose séance 1 is **done** | The séances are **left alone** (the save would otherwise be refused by `SetSteps`) | browser |
| C6 | A devis with **one** act only | Bin disabled (`lines.length === 1`), as before | browser |

### Tier D — regression sweep (from the table above)

| id | Row | Scenario | Expected | Layer |
|---|---|---|---|---|
| D1 | #3 | The act row's **état badge** on a booked act | Still « Planifié » with its date — `planItemState` untouched there | browser |
| D2 | #3 | `/treatment-plans` « Traitements suivis » | Loads, rows grouped as before | browser |
| D3 | #4 | Remove one act of a **shared** séance, then open that appointment | The other act is still on it **with its agreed price** | browser + sql |
| D4 | #7 | The devis workspace **card** tree at 390 px | « Séances » and « Modifier » both in the card header | browser |
| D5 | #1/#2 | Unfiltered unit suite | 0 failures | (done — 4646 passed) |
| D6 | #3 | Patient page « Plan de traitement » tab | Pips + headline render as before | browser |
| D7 | — | `/caisse` and « Solde patient » after A8 | The removed act's fee is out of the devis total, and the two reads agree | browser |

---

## Not exercised on purpose

- **Money confirms.** « Facturer », « Encaisser », « Arrêter le traitement » are not pressed — none is under
  test, and `verification.md` § 7 makes them read-only in a QA pass.
- **A cancelled appointment is a write**, but it is the change under test (A8), so it is exercised on a devis
  and a séance created for this run and reverted afterwards.
