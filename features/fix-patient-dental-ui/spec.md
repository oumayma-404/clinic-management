# Feature Specification: Patient & Dental UI Fixes

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-21
**Scope:** FE
**Feature:** Make patient search work, seed default patient folders, show dental amounts in DT/millimes, and fix dental-record edit classification.

## Overview
Four frontend defects on the patient screens: (1) the patients-table search box re-fetches on every keystroke but never passes `searchTerm`, so it never filters; (2) the per-patient files manager's default-folder seeding effect never runs because its `!loading` guard is always false on its only run; (3) dental amounts render with a hardcoded `$` and 2 decimals instead of the Tunisian `DT`/millimes format the rest of the app uses; (4) editing a dental record shows a known procedure as "Custom" on first open because classification reads `procedureTypes` before its async load resolves.

## What Changes
- `patients-table` passes the typed query as `searchTerm` to `patientsApi.list(...)` so results narrow as the user types.
- `patient-files-manager` reliably seeds default folders once when a patient has none (fix the effect guard/dependencies).
- Dental cost / amount-paid / "reste à payer" use `formatDT` (DT, 3-decimal millimes) on the patient detail dental tab, the patient summary modal, and the record modal.
- The edit dental-record modal waits until `procedureTypes` is loaded before deciding standard-vs-custom, so a known procedure pre-selects correctly on first open.

## Acceptance Criteria
- **AC-1:** Typing a name or phone in the patients search narrows the table to matching patients.
- **AC-2:** Opening a patient's files with no folders seeds the default folder structure exactly once.
- **AC-3:** Dental amounts display as e.g. `120,500 DT`, never `$120.50`, on the dental tab, summary modal, and record modal.
- **AC-4:** Editing a dental record whose procedure matches a defined procedure type pre-selects that type (not "Custom") on first open.

## Out of Scope
- Backend search/paging behavior (already supports `searchTerm`).
- Any restyle beyond the currency format fix.
