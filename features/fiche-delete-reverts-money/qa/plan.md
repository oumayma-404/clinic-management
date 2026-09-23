# QA plan — supprimer une fiche annule son argent

Change under test: `DentalRecordDeletionReversal` + the new deletion-preview warning in the fiche delete
confirmation (`web/app/patients/[id]/page.tsx`). Plan written before any browser opened.

## Environment

- API `:5000` pid 26252 (owned by `clinic-management-20`), started 23:31 — **verified serving the change**
  per-file against `bin/Debug` (only `MoneyReconciliationService.cs` is newer, and that is a console verb).
- web `:3000` pid 37080 (owned by `clinic-management-20`), started 22:55 — fresh enough, compile workers alive.
- Peers live during the run: `clinic-management-20`, `clinic-management-d6` (patient phones),
  `investigate-phone`, `clinic-management-bf`, `clinic-management-8b`, `anakin-28`, `anakin-9c`.

⚠️ **Test data is mine and only mine.** A test patient is created through the product, given a devis, a
séance and a chairside collection, and it is that fiche which is deleted. No pre-existing clinical or money
row of the dev database is mutated.

## Scenarios

### Tier A — happy path

| id | Scenario | Layer | Expected (observable) |
|---|---|---|---|
| A1 | Fiche carrying **no money** → open the delete dialog | browser | No red warning box at all. « Supprimer » enabled once the preview lands. « Annuler » reads « Annuler » |
| A2 | Fiche carrying a **devis collection** → open the dialog | browser | Red box reads « Cet argent est déjà entré dans la caisse — 80,000 DT seront annulés », one bullet naming the devis number and 80,000 DT, and a line naming the caisse day of the **payment**, not today |
| A3 | Confirm the delete on A2 | browser + sql | Toast « Fiche de soins supprimée. » · the plan's `AmountPaid` returns to 0 · the payment row still exists with `IsVoided=true`, `VoidReason='Fiche de soins du JJ/MM/AAAA supprimée'`, `VoidedByName` set |
| A4 | Fiche carrying a **numbered note** → open the dialog | browser | Paragraph « La note d'honoraires n° 2026-XXXX sera **annulée**. Elle garde son numéro… » |
| A5 | Confirm the delete on A4 | browser + sql | Invoice `Status=Cancelled`, `Number` unchanged, `AmountCollected=0`, its payments `IsVoided=true` |
| A6 | Preview endpoint figures | api | `GET …/deletion-preview` returns `totalReversed`, `plans[]`, `note`, `affectedCaisseDays[]` matching the SQL truth |

### Tier B — alternate paths

| id | Scenario | Layer | Expected |
|---|---|---|---|
| B1 | Open the dialog and look **before** the preview resolves | browser | « Supprimer » is **disabled**; it enables only once the box/paragraphs appear |
| B2 | Fiche whose collection is a **cheque marked banked** | browser | Refusal box « Cette fiche ne peut pas être supprimée pour l'instant. » + the chèque sentence · « Supprimer » disabled · the cancel button reads **« Retour »** |
| B3 | Fiche that emitted an **ordonnance** | browser | Paragraph « L'ordonnance de cette séance est conservée… ». After deletion the document still exists with `DentalRecordId = NULL` |
| B4 | Open the dialog, press « Annuler » | browser + sql | Nothing deleted, no payment voided, no invoice cancelled |
| B5 | Preview read fails (offline route) | browser | A toast names it and « Supprimer » stays disabled — never a silent fall-through to the old wording |

### Tier C — edge cases

| id | Scenario | Layer | Expected |
|---|---|---|---|
| C1 | **Eye pass** of the richest dialog (devis + note + caisse days + ordonnance) at **320 / 390 / 820 / 1180 / 1440** and **1536×730** | browser | No horizontal page scroll; nothing clipped; the red box and its bullets stay inside the dialog; both footer buttons reachable without the page scrolling away |
| C2 | Two payments on **two different days** | browser | Both days named, « La caisse **des** … seront modifiées » |
| C3 | Un-numbered treatment (Draft, no number) | browser | Bullet reads « le traitement « Titre » », never « le devis null » |
| C4 | Long devis title / long patient name | browser | Bullet wraps, no overflow at 320 px |

### Tier D — regression sweep (from the blast-radius table)

| id | Row | Layer | Expected |
|---|---|---|---|
| D1 | La caisse — extrait of the payment's day | browser | Before: the 80,000 DT movement present. After: gone from the **payment's day**, and the day's Net drops by exactly 80,000 |
| D2 | « Créances » | browser | The patient's outstanding rises by the reversed amount (they now owe it again) and the page renders |
| D3 | « Solde patient » on the patient file | browser | Agrees with D2 — the two formulas must report the same number |
| D4 | Dashboard « Encaissé » | browser | Drops by the reversed amount for the affected period |
| D5 | **Bridged note detach** — fiche whose note carries a `TreatmentPlanId` | browser + sql | After deletion: `Invoice.TreatmentPlanId IS NULL`, the plan item's `BilledOnInvoiceId IS NULL`, and « Solde patient » does **not** jump to the devis' full total |
| D6 | The fiche list and « Facturé » badges after a delete | browser | The deleted fiche is gone; the remaining rows keep correct badges |

## Not in scope for this pass

- `reconcile-money` → `no-money-without-a-fiche` — a console verb, out of a browser's reach, and the running
  API does not carry it yet. Covered by `MoneyReconciliationServiceTests`. Will be reported ⏭.
- The production data remediation (note 2026-0038) — the practitioner's, not ours.
