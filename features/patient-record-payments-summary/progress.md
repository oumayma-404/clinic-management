# Progress: Partial Payments, Patient-Page Reorder & Real AI Summary

**Started:** 2026-07-17
**Type:** Small
**Branch:** feature/windows-desktop-app (active umbrella dev branch; feature branched off it would just fork the shared uncommitted work)

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [ ] Tests (handled by /test-small-feature)

## Quality check results
- Backend `dotnet build ClinicManagement.API.csproj`: **0 errors**. The API process was running and held the
  host `bin`, so a normal build ended in `MSB3021`/`MSB3027` copy-lock (NOT compile) errors — every project
  compiled. Re-built to a fresh scratch output dir for a clean signal: **Build succeeded, 0 Error(s)**; the only
  warning in a changed file is a pre-existing `CS8602` on `PatientsController.cs:70` (the `CreatePatient`
  `CreatedAtAction`, not the new `GetAiSummary` action) — no new warning introduced.
- Frontend `npx tsc --noEmit`: **0 errors**. `npm run build` (`next build`, which runs "Checking validity of
  types"): **success**, all 17 routes generated.
- ESLint: **not installed in this repo** (Next build runs with ESLint disabled per `next.config.ts`); no lint
  harness to run — not a coverage gap for this skill.

## Acceptance criteria coverage
- AC-1: `DentalRecordDto.Balance = Cost − AmountPaid` mapped in Get/Create/Update handlers; FE type already had it.
- AC-2: Reste column added to the patient-page dental table + patient-summary modal (amber Badge when > 0, "$0.00" otherwise).
- AC-3: Removed the forced `amountPaid = cost` effect; full payment prefilled only when amountPaid empty; live "Reste à payer" readout in the record modal.
- AC-4: Tabbed medical-records section moved directly beneath the AI card, above the three info cards.
- AC-5/6/7: real `GET /patients/{id}/ai-summary` (HuggingFace) auto-loads on page open, loading state, "Régénérer" button, French fallback on error, and offline "connexion requise" note (skips call, auto-retries when internet returns).
- AC-8: query is clinic-scoped — cross-clinic/missing patient throws `NotFoundException` → 404, never returns another clinic's data.

## Working tree note (start of session)
The working tree carries a large volume of pre-existing uncommitted changes from other in-flight
small features on this umbrella branch (graceful-error-handling, post-visit-review,
post-visit-review-patient-record, notification-center). Several files this feature also touches were
**already modified by those features** — notably `DentalRecordDto.cs`, `CreateDentalRecordCommand.cs`,
`UpdateDentalRecordCommand.cs`, `GetDentalRecordsQuery.cs` (the `AppointmentId` post-visit field),
`patient-record-modal.tsx`, `patients/[id]/page.tsx`, `dental-records.ts`. Those unrelated changes are
NOT part of this feature and must be excluded from this feature's eventual commit (stage by path).
The frontend `DentalRecordDto.balance` was already declared in `types.ts` (ahead of the API) — this
feature backs it with real API data.

## Files Changed
### Backend
- `api/ClinicManagement.Application/DTOs/DentalRecordDto.cs` — add derived `Balance`.
- `api/ClinicManagement.Application/DTOs/PatientAiSummaryDto.cs` — NEW `{ Summary }`.
- `api/ClinicManagement.Application/Features/Patients/Queries/GetDentalRecordsQuery.cs` — map `Balance`.
- `api/ClinicManagement.Application/Features/Patients/Commands/CreateDentalRecordCommand.cs` — map `Balance`.
- `api/ClinicManagement.Application/Features/Patients/Commands/UpdateDentalRecordCommand.cs` — map `Balance`.
- `api/ClinicManagement.Application/Features/Patients/Queries/GetPatientAiSummaryQuery.cs` — NEW query+handler (real HuggingFace call).
- `api/ClinicManagement.API/Controllers/PatientsController.cs` — add `GET {patientId}/ai-summary`.

### Frontend
- `web/lib/api/patients.ts` — add `getAiSummary`.
- `web/components/patient-record-modal.tsx` — live "Reste à payer"; stop force-overwriting amountPaid=cost.
- `web/components/patient-summary-modal.tsx` — add Reste column (amber when > 0).
- `web/app/patients/[id]/page.tsx` — reorder tabs under AI card; real AI summary (load/Régénérer/offline); Reste column.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Patient-not-found path throws `NotFoundException` (→404 via `ExceptionMiddleware`) rather than `Result.Failure` | The spec's API contract pins 404 (patient) vs 400 (AI unavailable); a single `Result<string>` can't drive both codes at the thin controller. `NotFoundException`→404 `{error}` is the purpose-built mechanism already in the pipeline. Internal detail, honors the contract exactly. |

## Significant Deviations
None.
