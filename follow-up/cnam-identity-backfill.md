# CNAM identity backfill for existing patients

> **Type:** enhancement
> **Priority:** low
> **Created:** 2026-07-17
> **Feature:** cnam-bulletin-soins

## Summary
CNAM identity (`CnamInfo`) is only captured via the full patient edit dialog. Existing patients start empty and are only filled if someone opens the edit dialog. Add a lightweight inline prompt to capture CNAM identity when starting a bulletin for a patient who has none, so staff aren't forced through the whole edit dialog.

## Current State
- `CnamInfo` is edited only in `edit-patient-dialog.tsx` (Identité CNAM section).
- The bulletin editor shows a read-only CNAM identity summary and a hint to fill it in the patient record, but no inline capture.

## Expected State
- When opening a `bulletin-cnam` for a patient with no `CnamInfo`, offer an inline "add CNAM identity" affordance (a compact form or a deep-link that returns to the bulletin) that persists via the existing patient update.

## Key Files
| File | Purpose |
|------|---------|
| `web/components/document-editor-content.tsx` | bulletin editor (CNAM identity readout block) |
| `web/components/edit-patient-dialog.tsx` | existing CNAM identity fields (reuse) |
| `web/lib/api/patients.ts` | `update` (cnamInfo) |

## Why Deferred
Purely a convenience; the data can already be captured today via the patient edit dialog.

## Suggested Approach
1. Inline compact CNAM form in the bulletin editor (reuse the field set), saving through `patientsApi.update`.
2. Re-read the patient so the bulletin pre-fills immediately after saving.

## Acceptance Criteria
- [ ] Staff can add CNAM identity from the bulletin editor without leaving it, and it persists to the patient.
- [ ] The bulletin pre-fills from the just-entered identity.
