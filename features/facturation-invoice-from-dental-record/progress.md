# Progress: Facturation — Facturer une intervention (pré-remplir depuis un dental record)

**Started:** 2026-07-17
**Type:** Small
**Branch:** feature/facturation-invoice-from-dental-record (worktree, based on feature/windows-desktop-app HEAD 7d38c81 which includes the merged facturation-note-honoraires work; PR target = feature/windows-desktop-app)

## Status
- [x] Implementation
- [x] Quality checks (build, typecheck)
- [ ] Tests (handled by /test-small-feature)

## Quality checks run
- `npx tsc --noEmit` → clean.
- `npm run build` (`next build`) → compiled successfully, all 18 routes generated.
- FE-only change set (confirmed via git): only `web/app/patients/[id]/page.tsx` + `web/components/factures/invoice-form-modal.tsx`. No backend/schema change. (Lint not runnable in this repo — ESLint not installed; `next build` disables it — same as the parent feature.)

## Working tree note (start of session)
Fresh worktree off feature/windows-desktop-app HEAD (has the invoice UI merged). Frontend-only feature; no backend/schema change (backend already supports the DentalRecordId link).

## Files Changed
- `web/components/factures/invoice-form-modal.tsx` — new optional props `presetLines` (create-only seed) + `dentalRecordId` (persisted on the created draft); create payload now includes `dentalRecordId`. Edit path untouched (spec AC-5).
- `web/app/patients/[id]/page.tsx` — per dental-record row "Facturer cette intervention" action (Receipt icon) that opens the pre-filled InvoiceFormModal; loads the patient's invoices to build the `invoicedDentalRecordIds` guard set (non-cancelled invoices only); shows a "Facturé" badge for already-invoiced records; mounts the billing modal; success bumps `refreshKey` to reload records + guard set.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Duplicate guard computed fully client-side from `invoicesApi.list({ patientId })` → `InvoiceDto.dentalRecordId` | Spec allows this ("garde calculée côté client") — no backend filter added; `InvoiceDto` already exposes `dentalRecordId`. |
| Reused the existing `Receipt` lucide icon for the action | Consistent with the Factures tab/nav icon; no new dependency. |

## Known limitation (out of scope, pre-existing)
- Editing an existing draft via the Factures tab does not preserve its `DentalRecordId` link (the pre-existing `UpdateInvoiceCommand`/`invoicesApi.update` edit path sends no `dentalRecordId`, so the link clears on edit). Spec AC-5 scopes this feature to **create-only** and "editing unchanged", so the edit path is deliberately left as-is. Candidate future fix in the facturation follow-ups.
