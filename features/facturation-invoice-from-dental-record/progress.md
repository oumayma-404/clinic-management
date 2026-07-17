# Progress: Facturation — Facturer une intervention (pré-remplir depuis un dental record)

**Started:** 2026-07-17
**Type:** Small
**Branch:** feature/facturation-invoice-from-dental-record (worktree, based on feature/windows-desktop-app HEAD 7d38c81 which includes the merged facturation-note-honoraires work; PR target = feature/windows-desktop-app)

## Status
- [x] Implementation
- [x] Quality checks (build, typecheck)
- [x] Tests (no test surface — covered by the typecheck/build gate; see Test Plan)

## Test Plan
This feature is **frontend-only** (2 files: `web/components/factures/invoice-form-modal.tsx`,
`web/app/patients/[id]/page.tsx`) with **no backend change**, and the repo has **no frontend test
framework** (no vitest/jest/testing-library, no `.test`/`.spec` files, no `test` script). So there is
no C#/unit surface and no FE test runner to write against. Per `/test-small-feature`, each AC is
accounted for via a coverage note rather than a contrived test.

| AC | Coverage |
|----|----------|
| AC-1 (pre-filled editable draft) | FE typecheck (`npx tsc --noEmit`) + `next build`; the preset seeds the existing, already-shipped `InvoiceFormModal` create path. |
| AC-2 (`DentalRecordId` persisted) | Backend already covered by the parent feature's `IssueInvoiceCommandHandlerTests` / entity tests; the wire-through is a typed `CreateInvoiceRequest.dentalRecordId` field — covered by `tsc`. |
| AC-3 (dup guard; cancelled ≠ blocked) | Client-side logic in `page.tsx` (`invoicedDentalRecordIds` = non-cancelled invoices linked by `dentalRecordId`); covered by `tsc`/build. No FE unit runner to assert it in isolation. |
| AC-4 (`DentalRecord` untouched) | Read-only usage (only reads `procedureType`/`cost`/`id`); backend never receives dental-record mutations from this flow. Enforced by the parent feature's invariant + `tsc`. |
| AC-5 (create-only; edit/numbering/payment unchanged; no backend change) | Edit path left byte-for-byte; verified FE-only via git diff; no backend/schema change. |

### Coverage notes
- **No FE test framework in this repo** — installing vitest + component-testing infra would be a brand-new
  harness, which `/test-small-feature` explicitly says not to invent. The FE gate (`tsc --noEmit` + `next build`)
  is the coverage mechanism, matching the parent facturation feature's approach.
- The dedup-guard and preset logic would be the natural unit-test targets **if** a FE runner existed — flagged
  as a candidate should the repo adopt vitest later.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| FE typecheck | `npx tsc --noEmit` | clean (green-bar verification) |
| FE build | `next build` (at implementation) | compiled successfully, 18 routes |
| Backend | — | no backend change → nothing to run |

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
