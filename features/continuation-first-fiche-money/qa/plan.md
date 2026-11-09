# QA plan — Continuation's first séance: price kept, « le patient n'a pas payé » correctable

**Written:** 2026-11-07
**Change under test:** `fix/continuation-first-fiche-money` (3ef3bf2b + 831ec26c) vs `origin/main`
**Rendering files in the diff:** 4 — `patient-record-modal.tsx`, `factures/correct-invoice-dialog.tsx`,
`treatment-plans/plan-workspace.tsx`, `caisse/caisse-row-actions.tsx`
**Blast-radius table:** `features/continuation-first-fiche-money/notes.md` § Blast radius
**Budget:** money → all four tiers · 17 scenarios
**Environment:** ISOLATED — API :5093 + web :3093 from this worktree, database `clinic_qa_cff` (a copy of the local
`clinic_pvl_qa`). Never prod, never the shared `clinic_management`.

## Fixtures (arranged over the API, `scratchpad/qa/arrange.mjs`; ids in `qa/fixtures.json`)

| Patient | Arrangement |
|---|---|
| P1 « Hamdane » | fiche 02/11 « Traitement de canal » 300 DT, Payé 100 → note issued; continued with « Prix du reste » 200 → devis: act at 0 « sur la note » + « Séance suivante » 200; séance 2 booked on 20/11 |
| P2 « Garde » | same as P1 (no séance 2) |
| P3 « Ordinaire » | ordinary fiche « Soin de carie » 90 DT, Payé 90 → note paid |
| P4 « Prix » | same as P3 |
| P5 « Attache » | canal 300, Payé 100, continued with NO prix du reste → note attached to the devis |

## Tier A — happy path (Dr Hamdane's case)

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| A1 | `/patients/P1?tab=medical-records&editRecord=F1` | open séance 1's fiche | the act reads 300,000 DT, « Payé » holds 100, the footer reads « Enregistrer la séance — 300,000 DT »; no « Corriger la note », no « Sur la note n° » tag | browser | 1440 |
| A2 | same | « Enregistrer » with nothing changed | toast « Fiche de soins mise à jour »; note still 300 / 100 collected, not cancelled; payment live; fiche Cost 300 Paid 100; still ONE note on F1 | browser + sql | — |
| A3 | same | reopen, « Payé » → 0, press the footer | button reads « Corriger la note — 300,000 DT »; alertdialog: « Note de cette séance annulée · payé 100,000 DT » 300,000 · « Nouvelle note, séance corrigée · payé 0,000 DT » 300,000 · « passe de 100,000 DT à 0,000 DT » · « Il reste 300,000 DT à encaisser sur la nouvelle note » · « sans paiement » | browser | 1536×730, 390 |
| A4 | same | « Corriger » | toast « Note n° … émise — 0,000 DT payés »; old note Cancelled + payment voided; NEW note Issued 300 / 0, supersedes the old, on F1; F1 Cost 300 Paid 0; devis TotalPlanned 200 and both lines unchanged | browser + sql | — |
| A5 | `/treatment-plans/D1` | read the séance-1 note row, press « Corriger » | the row names the NEW note « payé 0,000 DT · reste à payer 300,000 DT »; « Corriger » lands on the patient page with F1's fiche open | browser | 1440 |
| A6 | — | P1 billing summary | TotalOutstanding = 500 (new note 300 + devis 200) | api | — |

## Tier B — alternate paths

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| B1 | P2 fiche | « Payé » 100 → 50, « Corriger la note », « Corriger » | dialog « payé 100 » → « payé 50 », « récupère les 50,000 DT », « Il reste 250,000 DT »; toast « … 50,000 DT payés »; old note cancelled, new 300 / 50, payment dated 02/11 | browser + sql | 1440 |
| B2 | P4 fiche | « Payé » → 0, « Corriger la note », then « Annuler », then leave without saving | nothing written: note still Paid 90/90, fiche still Paid 90 | browser + sql | 1440 |
| B3 | `/patients/P1?addRecord=1&appointmentId=A2` | open séance 2's fiche (not saved) | the act wears « Inclus dans le traitement », no « Sur la note », and the footer states no séance price | browser | 1440 |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | 320 px floor | A3's dialog at 320 × 700 | dialog fits: no horizontal scroll, « Corriger » and « Annuler » fully on screen | browser | 320 |
| C2 | save, reopen, save again | after A4 reopen F1 | « Payé » 0, footer « Enregistrer la séance — 300,000 DT » (no « Corriger »); plain save → still exactly 2 notes on F1, the new one untouched | browser + sql | 1440 |
| C3 | devis page on a phone | `/treatment-plans/D1` at 390 | no sideways scroll in `<main>`; « Corriger » on screen | browser | 390 |

## Tier D — regression sweep

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | #2 auto-billing at 0 | P3 ordinary fiche, « Payé » 90 → 0, correct | dialog « sans paiement », « Il reste 90,000 DT »; old cancelled, NEW note 90 / 0 (this used to raise none) | browser + sql | 1440 |
| D2 | #2 | P4 ordinary fiche, « Payé » 90 → 60, correct | dialog « passe de 90,000 DT à 60,000 DT »; new note 90 / 60 | browser + sql | 1440 |
| D3 | #1 attached shape | P5 fiche | act reads 300, footer « Enregistrer la séance — 300,000 DT »; plain save leaves the note attached to the devis, 300 / 100 | browser + sql | 1440 |
| D4 | #4 `caisse-row-actions` | `/caisse` « Corriger le montant… » on a payment | the dialog speaks of a draft (« recopiée en brouillon ») and shows NO « Nouvelle note » row; « Annuler » writes nothing | browser | 1440 |
| D5 | #3 entry from /factures | « Corriger cette note » on P4's note | lands on the patient page with that fiche open | browser | 1440 |

## Fix rows (added for cycle 2 — run-1 findings)

| ID | Finding | Expected (observable) | Layer | Widths |
|----|---------|-----------------------|-------|--------|
| F1 | F-1 | in A3, the line under « Payé » reads « … déjà payés sur la note · « Corriger la note » la remplace », never « ne se diminue pas ici » | browser | 1440 |
| F2 | F-2 | in A5, the devis text says « … sur sa propre note d'honoraires » and names no note number (the original one is cancelled) | browser | 1440 |
| F3 | F-3 | in A5/C3, the corrected note's row and the headline carry no « avec d'autres actes » | browser | 1440, 390 |

## Not covered, and why

| Area | Why not |
|------|---------|
| The live continuation dialog (« Suite d'une séance précédente » in the booking dialog) | not changed; arranged over the API instead |
| Prod data repair for Dr Hamdane | owner's decision, not a test |
