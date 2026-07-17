# Feature Specification: CNAM dental nomenclature lookup + indicative reimbursement

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-17
**Scope:** Full
**Feature:** Replace the free-text Code acte / Cotation entry in the CNAM bulletin with a searchable lookup over a curated dental nomenclature, and show an indicative reimbursement estimate in the editor.

## Overview
Extends the shipped `cnam-bulletin-soins` feature. Today the bulletin acts table has the doctor type "Code acte" and "Cotation" by hand — the #1 cause of CNAM rejecting a bulletin. This adds a curated, in-code CNAM dental nomenclature the doctor searches; picking an act fills Code acte + Cotation consistently. It also shows an **indicative** reimbursement estimate per act + total so staff can tell the patient roughly what CNAM will refund. Estimate is editor-only — never persisted, never on the PDF. French UI; Cloud + Local both work; the four other document types are untouched.

## What Changes
- A **static in-code CNAM dental nomenclature** reference (curated starter set across every category): each entry `{ codeActe, designationFr, lettreCle (CD|CDS|VD|D|RD), coefficient, category (Consultation | Soins conservateurs | Chirurgie/Extraction | Prothèse | Radiologie) }`. No DB table, no migration — a C# reference provider in Infrastructure, exposed through a read endpoint. Values are best-effort defaults, flagged in-code as pending verification against the current CNAM dental convention.
- A **static reimbursement config**: a conventional dinar value per lettre clé + a standard rate and an APCI rate (constants; admin editing is a later feature).
- A read endpoint `GET /api/cnam-nomenclature` (filter by free text + category) returning the list.
- In the bulletin editor acts table, the "Code acte" cell becomes a **searchable autocomplete** over the nomenclature (reusing the existing procedure-type Popover+search pattern). Selecting an act fills **Code acte** (= `codeActe`) and **Cotation** (= `"<lettreCle> <coefficient>"`, e.g. `D 15`). Both cells stay editable; acts not in the catalog can still be typed as free text (no regression).
- The editor shows a **per-act indicative reimbursement estimate** and a **total**, labelled *"estimation indicative — montant réel fixé par la CNAM"*. Estimate = `coefficient × valeur(lettreCle) × rate`, using the APCI rate when the bulletin's care type is APCI, else the standard rate. Only computable for catalog-backed acts (blank for free-text acts). Not saved, not on the PDF.

## Acceptance Criteria
- **AC-1:** `GET /api/cnam-nomenclature` returns the curated acts and supports filtering by free-text query and by category; it is not clinic-scoped (shared reference data) and requires an authenticated user.
- **AC-2:** In the bulletin acts table, searching and selecting an act fills Code acte and Cotation (`"<lettreCle> <coefficient>"`); both cells remain manually editable afterwards.
- **AC-3:** An act not in the catalog can still be entered as free text for Code acte and Cotation — the pre-fill-from-dental-records flow and the saved `ContentJson` shape are unchanged (only values differ).
- **AC-4:** The editor shows a per-act indicative estimate and a total, APCI-aware, clearly marked as indicative; free-text acts show no estimate.
- **AC-5:** The indicative estimate never appears on the generated BS1 PDF and is never persisted (no `ContentJson`/schema change beyond the existing act values).
- **AC-6:** All new labels are French; Cloud and Local modes both work; the four existing document types are byte-for-byte unchanged.

## API Contract
### GET /api/cnam-nomenclature?q={text}&category={category}
Response 2XX: `[{ codeActe: string, designationFr: string, lettreCle: string, coefficient: number, category: string }]`
- `q` and `category` optional; empty → full list. Auth required (returns 401 unauthenticated).

## Out of Scope
- Admin CRUD / editing of the nomenclature or the rates (values are in-code constants this slice).
- Any DB table, EF entity, or migration for the catalog/config.
- Live sync or scraping of CNAM tariffs / the convention document.
- Presenting the estimate as a guaranteed refund; claim submission; tiers-payant / bordereau / télétransmission.
- Prosthesis-specific routing, BS1 lifecycle status, CNAM identity backfill, bilingual completion, PDF fidelity — separate features.

## Edge Cases (Critical only)
- **Free-text act (no catalog match):** Code acte / Cotation accepted as typed; estimate cell blank; no error.
- **Care type not APCI:** estimate uses the standard rate; APCI code field irrelevant to the math.
- **Missing/zero coefficient or unknown lettre clé:** estimate shows blank (never a wrong number), act still selectable.
