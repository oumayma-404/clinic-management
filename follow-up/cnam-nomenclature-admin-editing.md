# Admin editing of CNAM nomenclature + reimbursement rates

> **Type:** enhancement
> **Priority:** medium
> **Created:** 2026-07-17
> **Feature:** cnam-nomenclature-lookup

## Summary
The CNAM dental nomenclature and the reimbursement config (per-lettre-clé value, standard rate, APCI rate) ship as **static in-code constants** in the `cnam-nomenclature-lookup` feature. CNAM revises codes/coefficients/tariffs over time, so a clinic admin needs a way to edit them without a code change. This promotes the in-code reference to editable data + an admin UI.

## Current State (after cnam-nomenclature-lookup ships)
- Nomenclature = a C# reference provider in Infrastructure, served read-only via `GET /api/cnam-nomenclature`.
- Reimbursement values/rates = in-code constants used only for the indicative estimate.

## Expected State
- Move the catalog + rate config into DB tables (EF entities + migration) seeded from today's in-code values.
- Admin-only CRUD (Local admin surface): add/edit/deactivate acts, edit lettre-clé values + standard/APCI rates.
- The read endpoint + editor autocomplete + estimate switch to the DB source with no behavior change for non-admins.

## Key Files
| File | Purpose |
|------|---------|
| `api/.../Infrastructure/...` (nomenclature provider from the lookup feature) | source to migrate to DB |
| `web/components/user-management.tsx`, `web/components/backup-settings.tsx` | Local admin-only UI conventions to mirror |
| `web/components/document-editor-content.tsx` | consumer (autocomplete + estimate) — should be source-agnostic |

## Why Deferred
Kept the first slice small: static data + read endpoint is enough to remove free-text errors now. Editability is a clean, separable increment once the values need maintenance.

## Suggested Approach
1. `/define-small-feature` (or fold into the CNAM Phase-2 `/define-feature`): DentalActNomenclature + CnamTariffConfig entities, migration seeded from the in-code list, admin CRUD.
2. Keep the read endpoint contract stable so the editor doesn't change.

## Acceptance Criteria
- [ ] Admin can add/edit/deactivate nomenclature acts and edit lettre-clé values + rates.
- [ ] Existing editor autocomplete + estimate work unchanged against the DB source.
- [ ] Seed migration reproduces the current in-code starter set.
