# BS1 bulletin lifecycle status tracking

> **Type:** enhancement
> **Priority:** medium
> **Created:** 2026-07-17
> **Feature:** cnam-bulletin-soins

## Summary
Once a bulletin is generated the clinic has no way to track whether it was handed to the patient or reimbursed by CNAM. Add a lifecycle status (Généré → Remis au patient → Remboursé, plus Rejeté with an optional reason) and a filterable list so staff can follow up on outstanding bulletins.

## Current State
`bulletin-cnam` is a `MedicalDocument` with `ContentJson`; there is no status field beyond the generic `IsDraft`. No follow-up/tracking view exists.

## Expected State
- A lifecycle status on the bulletin-cnam document (new nullable column(s) on `MedicalDocument`, additive) with a transition date per state.
- Editable from the documents / patient views; a filterable list of bulletins by status (e.g. "outstanding = not yet Remboursé").

## Key Files
| File | Purpose |
|------|---------|
| `api/ClinicManagement.Domain/Entities/MedicalDocument.cs` | add status + transition dates |
| `api/.../Features/Documents/*` | status update command + query |
| `web/app/documents/`, `web/components/document-editor-content.tsx` | status UI + list |

## Why Deferred
Not needed to produce a correct bulletin; it's an operational-tracking layer on top.

## Suggested Approach
1. Add a `BulletinStatus` enum + status/date columns to `MedicalDocument` (only meaningful for `bulletin-cnam`).
2. Status-update command + a status filter on the documents list.
3. Hand-author the migration (WDAC — see `cnam-migration-ef-verify.md`).

## Acceptance Criteria
- [ ] A bulletin's status can be advanced (Généré/Remis/Remboursé/Rejeté+reason) with a recorded date.
- [ ] Bulletins are filterable by status; other document types are unaffected.
