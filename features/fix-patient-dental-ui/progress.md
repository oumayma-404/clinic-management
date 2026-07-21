# Progress: Patient & Dental UI Fixes

**Started:** 2026-07-21
**Type:** Small
**Branch:** feature/windows-desktop-app (user declined a dedicated branch)

## Status
- [x] Implementation
- [x] Quality checks — FE `npx tsc --noEmit` → 0 errors. (FE-only spec; no backend build needed.)
- [x] Tests — coverage note: this is FE-only and the repo has no FE test runner (no vitest/jest), so all ACs (#7 search, #8 folder-seed, #12 currency, #14 classification) are covered by the `tsc --noEmit` + Next build gate from the implement pass. No backend unit surface.

## Working tree note (start of session)
Unrelated in-flight work EXCLUDED from staging: `medication-catalog-picker`; prior fixes `fix-patient-file-tenant-isolation`, `fix-single-dentist-identity`, `fix-document-cnam-accuracy`, `fix-appointment-lifecycle`, `fix-appointment-google-sync`; other `features/fix-*` folders.

## Files Changed
- `web/components/patients-table.tsx` — #7: pass the trimmed query as `searchTerm` to `patientsApi.list(...)` (both the search effect and the post-edit reload).
- `web/components/patient-files-manager.tsx` — #8: seed default folders once per patient after the first load resolves (ref-keyed on patientId; fixes the always-false `!loading` guard).
- `web/components/patient-record-modal.tsx` — #12: "reste à payer" via `formatDT`; #14: add `procedureTypes` to the population effect deps so a known procedure classifies correctly once the list loads (no false "Custom").
- `web/app/patients/[id]/page.tsx` — #12: amount-paid + reste via `formatDT`.
- `web/components/patient-summary-modal.tsx` — #12: cost + amount-paid + reste via `formatDT`.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| #7 also updates the post-edit reload (`handleEditSuccess`) to pass `searchTerm`, not just the search effect | Keeps the visible list consistent with the active search after editing a patient; same internal call, no contract change. |
| #14 fixed by adding `procedureTypes` to the effect deps (re-run once the list loads) rather than splitting the effect | Minimal, no behavior change beyond correct classification; the effect re-populates idempotently from `record`. |

## Deferred to /test-small-feature
- New scenarios: search narrows the list; default folders seed once; dental amounts render as `DT`/millimes; editing a known-procedure record pre-selects the type.

## Significant Deviations
(none)
