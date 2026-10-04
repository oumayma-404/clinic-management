# QA plan — Reste à payer worklist

**Written:** 2026-10-03
**Change under test:** working tree on `feature/security-remediation` (uncommitted)
**Rendering files in the diff:** 6 — `app/a-cloturer/page.tsx`, `app/patients/[id]/page.tsx`, `components/visits/reste-a-payer-list.tsx`, `components/visits/reste-a-payer-patient-sheet.tsx`, `components/visits/contact-actions.tsx`, `components/patient/use-debt-line-collection.tsx`
**Blast-radius table:** `progress.md` § Blast Radius (3 rows `must re-test`: #2, #8, and #7/#9 as changed)
**Budget:** small feature, money → A + B + C (reachable) + D · 20 scenarios
**Test data:** one patient created through the product for the payment row — « QA RestePaiement », no phone, one issued note of 450 DT. Reverted through the product afterwards (payment voided, note cancelled).

## Tier A — happy path

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| A1 | `/a-cloturer` | open the page | a 4th tab « Reste à payer » with a badge equal to the API's `dueCount` | browser | 1440 |
| A2 | tab « Reste à payer » | open it | two cards: « À relancer » with `dueTotal` + « N patients », « En cours » with `runningTotal`; « À relancer » selected | browser | 1440 |
| A3 | same | read the first rows | each row: name (link to fiche), reason pill, date + « il y a N jours », amount, « + X en cours » when the patient also has running money | browser | 1440 |
| A4 | same | click « En cours » | the list switches (header « Traitement » / « Prochaine étape »), rows show « N acte(s) fait(s) sur M » and a next step | browser | 1440 |
| A5 | same | inspect Appeler / WhatsApp on a row with a phone | `tel:+216…` and `https://wa.me/216…` (target `_blank`, no `?text=`) | browser | 1440 |
| A6 | panel | « Détail » on a row | sheet opens; split bar + « Solde dû »; groups « À relancer » then « En cours »; acts dated oldest first; Total / Payé / reste; « Encaisser » | browser | 1440 |
| A7 | panel → Encaisser on the QA note | open Encaisser, type 200, Enregistrer | dialog prefilled 450,000; after save the panel reads reste 250,000, the row 250,000 and the card total drops by 200 — no reload | browser | 1440 |
| A8 | — | each row = that patient's « Solde dû » | 0 mismatches on a 131-patient sample; card totals = Σ rows | api | — |
| A9 | — | sort / paging / search | amount desc, age asc, no duplicate across pages, totals not page-scoped, accent-free search narrows | api | — |

## Tier B — alternate paths

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| B1 | search box | type « zzzznope » | « Aucun patient ne correspond à « zzzznope » » + « Effacer la recherche »; clearing restores rows | browser | 1440 |
| B2 | QA patient (no phone) | find its row | no Appeler / WhatsApp control on the row or in the panel | browser | 1440 |
| B3 | Encaisser dialog | type 999 on the 450 note | refused « Le paiement dépasse le reste dû … », dialog stays open, nothing written | browser | 1440 |
| B4 | Mode discret | user-status intercepted with `isMoneyHidden: true` (this browser only) | no « Reste à payer » tab; the other three tabs still there | browser | 1440 |
| B5 | secretary role | — | ⏭ no secretary credentials on the dev database; policy is `AnyClinicRole` (source) | — | — |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | 320 px floor | tab at 320 | cards, not a table; no horizontal scroll on the page; Appeler/WhatsApp full width on a card | browser | 320 |
| C2 | tablet portrait | tab at 820 | cards (table only from `lg:`); tab strip wraps with all four labels whole | browser | 820 |
| C3 | phone panel | open a patient at 390 | sheet takes the full width; Encaisser reachable by scrolling the panel | browser | 390 |
| C4 | owner's laptop | panel at 1536×730 | header + first document visible, panel scrolls inside itself | browser | 1536×730 |
| C5 | split devis | open a patient whose devis has one late échéance | panel shows that devis under « À relancer » with « À régler maintenant » < reste; row shows both parts | browser | 1440 |

## Tier D — regression sweep

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | #7 payment hook moved | patient file « Reste à payer » → Encaisser | the payment dialog opens prefilled with the document's reste; Annuler closes it, nothing written | browser | 1440 |
| D2 | #2 `PatientDebtLines.Project` | `billing-summary` | `totalOutstanding` = Σ line `outstanding`; new fields present on every line | api | — |
| D3 | #8 À clôturer tabs | Séances / À compléter / Suites | each still renders its content and its count | browser | 1440 |

## Not covered, and why

| Area | Why not |
|------|---------|
| Paying a real patient's document | money on shared data; A7 uses the QA patient only |
| « Ouvrir le devis » fallback (devis with no payable échéance) | no such document on the dev database to point at; branch is a plain link |
| `money_hidden` 403 on the endpoint | needs the clinic-wide toggle (affects every peer); held by `MoneyMaskGateMiddlewareTests` |
