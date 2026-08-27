# Progress: Derive & Confirm — Plan → Record prefill + auto-price plan lines

**Started:** 2026-07-23
**Type:** Small
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [ ] Tests (handled by /test-small-feature)

## Quality check results
- Backend: `dotnet build ClinicManagement.sln --no-incremental` → Build succeeded, 0 errors; no new warnings in changed files (repo baseline is pre-existing CS8618 in Domain). Application project also built clean in isolation.
- Frontend: `npx tsc --noEmit` → exit 0 (clean). `npm run build` → exit 0 (all routes compiled, incl. /patients/[id]).
- Tests deferred to /test-small-feature. Suggested new scenarios: (a) plan line with act code + zero cost seeds DefaultFee; (b) positive cost preserved; (c) free-text line untouched; (d) cross-clinic act ignored; (e) act with null DefaultFee leaves cost as sent.

## Working tree note (start of session)
Pre-existing unrelated changes present before this session (NOT part of this feature's commits):
- Modified: several `CLAUDE.md` docs (root + api/* + web/*), `desktop/CLAUDE.md`, etc.
- Untracked: `FUNCTIONAL_ADOPTION_REVIEW.md`, `api/ClinicManagement.UnitTests/CLAUDE.md`, `packaging/CLAUDE.md`.
These are excluded — only the files listed under "Files Changed" belong to this feature. Stage by path.

## Files Changed
- (P0-2) api/ClinicManagement.Application/Features/TreatmentPlans/Commands/TreatmentPlanItemPricing.cs — NEW shared helper
- (P0-2) api/ClinicManagement.Application/Features/TreatmentPlans/Commands/CreateTreatmentPlanCommand.cs — inject repo, seed via helper
- (P0-2) api/ClinicManagement.Application/Features/TreatmentPlans/Commands/UpdateTreatmentPlanCommand.cs — inject repo, seed via helper
- (P0-1) web/components/patient-record-modal.tsx — widen PlanItemOption, prefill focused act on select
- (P0-1) web/app/patients/[id]/page.tsx — populate new PlanItemOption fields

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Seed price when `PlannedCost <= 0` (can't distinguish deliberate 0 from omitted) | `TreatmentPlanItemRequest.PlannedCost` is non-nullable `decimal`; making it nullable would widen the DTO + FE contract, out of this slice's scope. Draft is editable, so a rare gratuit-with-act-code line can be re-zeroed. |
| Seed from `DentalActCode.DefaultFee` only (not `ProcedureType.DefaultCost`) | The plan-item request carries `DentalActCodeId` only, no `ProcedureTypeId`. |
| Shared static helper `TreatmentPlanItemPricing` rather than a DI service | Reusable-but-pure logic across 2 handlers; mirrors the area's co-located static-helper convention (mapping extensions); no DI registration needed, unit-testable. |

## Significant Deviations
(none)
