# Feature Specification: Medication Catalog Picker (Ordonnance)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-21
**Scope:** Full
**Feature:** Replace the free-text medication name in the ordonnance editor with a picker backed by a new admin-managed drug catalog.

## Overview
Today a prescription's medication name is free text. This adds a global, admin-managed `Medication` catalog (Tunisian brand → DCI molecule(s), form, strength), seeded with a starter set, and turns the editor's medication-name field into a catalog picker. Structured selection is the goal now; it also lays the data foundation (DCI on each line) for drug-interaction checking in a later slice. The catalog clones the existing CNAM nomenclature pattern; nothing about the document storage model changes.

## What Changes
- New **global** `Medication` catalog (no `ClinicId`, shared across clinics), each entry carrying brand name, form, strength, and one-or-more DCI molecules; seeded with a provisional ("à vérifier") starter set of common Tunisian drugs.
- Admin-only management: create / update / deactivate / confirm catalog entries, via a new admin-gated `/medications` page.
- The ordonnance editor's medication "Nom" field becomes a combobox over the active catalog; a free-text fallback is preserved when the drug isn't listed.
- On selection, the line stores `medicationId` + a snapshot of the drug's `dci[]`, and the printed/displayed name becomes "Brand Strength Form".

## Acceptance Criteria
- **AC-1:** A `Medication` entity exists as global reference data (no `ClinicId`, not clinic-filtered), with `BrandName`, `Form`, `Strength`, `IsActive`, `IsProvisional`, and ≥1 active-ingredient DCI; the seeded starter set loads with `IsProvisional = true`.
- **AC-2:** `GET /api/medications` returns active entries (each with its DCIs) for any authenticated user; `q` filters, `includeInactive=true` includes deactivated.
- **AC-3:** Create / Update / Deactivate / Confirm endpoints require the `AdminOnly` policy; a non-admin call returns 403.
- **AC-4:** `/medications` lists the catalog and lets an admin add / edit / deactivate / confirm entries; a non-admin sees the "réservé aux administrateurs" lock card (same gate as `/cnam-nomenclature`).
- **AC-5:** In the ordonnance editor the medication-name field is a combobox of catalog entries; picking one fills the line, stores `medicationId` + `dci[]`, and sets the line name to "Brand Strength Form".
- **AC-6:** Typing a name not in the catalog still saves the line as free text (`medicationId` null) — no regression to the current flow.
- **AC-7:** Existing prescriptions (legacy single-string and current array `content.medications`) still load and render unchanged.
- **AC-8:** Creating a duplicate entry (same brand + strength + form) is rejected with a French message.

## API Contract
### GET /api/medications?q={string}&includeInactive={bool}
Response 2XX: `MedicationDto[]` — `{ id, brandName, form, strength, dcis: string[], isActive, isProvisional }`
### POST /api/medications  *(AdminOnly)*
Request: `{ brandName, form, strength, dcis: string[] }` → Response 2XX: `MedicationDto`
### PUT /api/medications/{id}  *(AdminOnly)*
Request: `{ brandName, form, strength, dcis: string[] }` → Response 2XX: `MedicationDto`
### DELETE /api/medications/{id}  *(AdminOnly)*  → 204 (404 if id missing)
### POST /api/medications/confirm  *(AdminOnly)*  → 204 (clears provisional on all entries)
Errors: `400` French message (validation / duplicate); `403` non-admin; `404` deactivate of a missing id.

## Data / Schema Changes
- New tables `Medications` (global) + `MedicationActiveIngredients` (child, cascade delete, unique `(MedicationId, Dci)`); no clinic query filter. Migration `AddMedicationCatalog` + `MedicationCatalogSeed` (deterministic GUIDs, starter set provisional).
- `content.medications` line gains **optional** `medicationId` + `dci: string[]` — additive inside the existing `ContentJson`; **no change** to the `MedicalDocument` entity/schema.

## Out of Scope
- Drug-interaction checking (slice 2).
- Full national-formulary import and bulk/CSV import — single-entry admin create only.
- ATC / drug-class modelling.
- DCI-first / generic-name printing (brand label was chosen).

## Edge Cases (Critical only)
- Combination drug (>1 DCI): all molecules are stored in the line's `dci[]` snapshot.
- A deactivated medication drops out of the picker, but historical prescriptions keep their snapshotted `name` + `dci[]` unchanged (snapshot, not FK).
- Empty catalog or no combobox match → free-text entry still works (AC-6).
