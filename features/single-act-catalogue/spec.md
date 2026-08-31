# Feature Specification: One act catalogue

**Status:** APPROVED
**Type:** Small
**Created:** 2026-08-31
**Scope:** Full
**Feature:** Retire the invented CNAM nomenclature table; `DentalActCode` (the real DCH codes) becomes the only act catalogue, and a missing cotation is stated rather than computed around.

## Overview
The app carries two act catalogues built a day apart — `dental-core`'s spec calls its own a "clone of CNAM
nomenclature". `DentalActCode` holds the 100 genuine `DCH######` codes from the CNAM « Liste des actes »
(verified against `RBactes.pdf` pp. 33–37: 100 codes, exact match, and **no coefficient column** in the source).
`CnamNomenclatureEntry` holds 26 invented codes carrying hand-assigned coefficients, and is read by nothing but
its own admin screen — `CnamBillingCalculator` already resolves acts through `IDentalActCodeRepository`, and the
estimate endpoints take `(lettreCle, coefficient)` as parameters. This merges the two, keeps the lettres clés
(official, per NGAP arrêté 01/06/2006 art. 4), and makes an act with no cotation say so.

## What Changes
- `/dental-acts` becomes the only act-catalogue screen and gains the « Valeurs de la lettre clé » card.
- `CnamNomenclatureEntry`, its CRUD, `/cnam-nomenclature` and the `CnamNomenclature` realtime key are removed; the table is dropped with **no data preserved**.
- The two consultation acts move into the catalogue as rows with lettre clé `Cd` / `Cds` and coefficient 1 — that is how the convention itself prices them (Cd 30,000 · Cds 45,000 dès 01/01/2021, unchanged in the Dec-2022 table).
- `VIS-DOM` and the `VD` letter value are deleted: `VD` appears in neither the NGAP arrêté nor any CNAM tariff table. `RD` stays, valueless and « à vérifier ».
- The reimbursement-estimate and letter-value endpoints move under `api/dental-acts`.
- An act with no coefficient is labelled « cotation manquante » in the catalogue, and its estimate renders as « — » with that reason — never as 0, and never invented.

## Acceptance Criteria
- **AC-1:** `/cnam-nomenclature` no longer exists, the nav shows one catalogue entry, and `CnamNomenclatureEntries` is dropped. `RealtimeResourceResolverTests` passes in both directions with the `CnamNomenclature` key gone.
- **AC-2:** The VLC card is on `/dental-acts`, still `AdminOnly` to write and readable by any clinic role, and still offers the convention correction (« la convention en vigueur fixe cette valeur à 30,000 DT »).
- **AC-3:** A `Cd` consultation act exists; its estimate for a 30-year-old is `1 × 30,000 × 0.60`, and for a 10-year-old `1 × 30,000 × 0.70`.
- **AC-4:** A DCH act with `Coefficient = null` renders « cotation manquante » in the list, its estimate is « — », and the BS1 editor names the act and points at the catalogue. No surface shows 0 DT for it.
- **AC-5:** The estimate endpoints answer on their new `api/dental-acts` routes; the `api/cnam-nomenclature` routes are gone (404, not 401).
- **AC-6:** No `VD` letter value remains; `RD` is present, valueless, flagged « à vérifier ».
- **AC-7:** At 320 px the catalogue is cards with no horizontal scroll, the VLC card stacks below it, and every row action is reachable from one menu at 44 px on a coarse pointer (floor: `~/.claude/skills/DEVICE-CONTRACT.md`).

## API Contract
### Removed
`GET|POST|PUT|DELETE /api/cnam-nomenclature*` — every route, including `letter-values` and both estimate routes.
### Moved (shape unchanged)
- `GET /api/dental-acts/letter-values` · `PUT /api/dental-acts/letter-values/{id}`
- `GET /api/dental-acts/reimbursement-estimate` · `POST /api/dental-acts/reimbursement-estimates`
### Changed
`ReimbursementEstimateDto` gains `unavailableReason: "MissingCoefficient" | "NoLetterValue" | null` so a null estimate says which of the two it is.

## Data / Schema Changes
- **Drop** table `CnamNomenclatureEntries` (and `ICnamCatalogRepository`'s entry members). No archive — accepted data loss.
- **`CnamLetterValues`:** delete the `VD` row. `CD` / `CDS` / `D` / `RD` unchanged.
- **`DentalActCodes`:** insert 2 rows — `Cd` consultation and `Cds` consultation spécialiste, coefficient 1, category « Consultation », `IsProvisional = false` (both figures are sourced from the convention). DCH acts keep `Coefficient = null`.

## Device Behaviour
- **Leading device:** desk (an admin reference screen), but fully usable on a phone.
- **Narrow width (< 640):** the acts table becomes cards (code, désignation, lettre clé + cotation or « cotation manquante »); the VLC card's rows stack label-over-field.
- **Touch:** the row's edit/deactivate actions come from one 44 px menu button, not a hover-revealed icon strip.

## Out of Scope
- **Sourcing the 100 NGAP cotations** — blocked on the arrêté's annex, which is not published online. This feature makes the gap visible and editable; filling it is a data task.
- **Re-attaching acts to invoice lines.** `InvoiceLine.DentalActCodeId` is set by no surface today, so the reimbursable split stays inert after this change. Separate defect, separate spec.
- Any change to `CnamPlafond`, the ceiling read, or the 70/60 rates (all verified correct).

## Edge Cases (Critical only)
- A clinic that edited or confirmed CNAM entries loses that work — accepted per AC-1, and the screen was admin-only and 5 weeks old.
- An act whose lettre clé has no VLC (`RD`) estimates as « — » with `NoLetterValue`, distinct from a missing cotation.
- An invoice line still holding a `dentalActCodeId` keeps it; nothing about existing invoices changes.
