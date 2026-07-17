# Progress: Post-Visit Review → Patient Dental-Record Modal

**Started:** 2026-07-17
**Type:** Small
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [x] Tests (added — see Test Plan / Tests Run below)

## Test Plan
| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 | Coverage note | — | FE-only (popup navigation target). No unit surface; covered by the `tsc --noEmit` gate run at implementation time. |
| AC-2 | Coverage note | — | FE-only (patient page auto-opens `PatientRecordModal` on `?addRecord=1`). No unit surface; covered by the `tsc --noEmit` gate. |
| AC-3 | New test class | `Features/Patients/DentalRecordPostVisitCompletionTests.cs` | `Create_With_AppointmentId_Completes_Appointment_And_Cancels_Review` + `..._Echoes_It_In_The_Dto`. |
| AC-4 | New test class | same | `Create_Succeeds_Even_If_Completion_SideEffect_Throws` (best-effort, post-commit). |
| AC-5 | New test class | same | `Create_With_CrossClinic_AppointmentId_Leaves_It_Unchanged` + `Create_With_Unknown_AppointmentId_Is_No_Op`. |
| AC-6 | New test class | same | `Create_Without_AppointmentId_Runs_No_Completion_SideEffect`. |
| AC-7 | New test class | same | Asserted inside the AC-3 test (broadcasts the `"appointments"` realtime key). |

Coverage notes:
- The `Appointment.MarkVisitCompleted` domain transition (active→Completed / terminal no-op) is already covered by the sibling `PostVisitReviewCompletionTests` — not re-tested here; the new class exercises the `CreateDentalRecordCommandHandler` orchestration only.
- New test class chosen (not "add scenarios to a sibling") because the completion side-effect now lives on a different handler (`CreateDentalRecordCommand`, Patients area) than the sibling `CreateMedicalDocumentCommand` (Documents area); mirrors the sibling `DocHarness` shape.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `FullyQualifiedName~DentalRecordPostVisitCompletionTests` | 6 passed, 0 failed |
| Unit (regression) | `PostVisitReviewCompletionTests` + `PatientHardeningTests` + `NotificationGenerationTests` | 33 passed, 0 failed |

### Environment note (test run)
- **Running API locks the default `bin`.** Building the UnitTests project into its normal output fails with MSB3021/MSB3027 file-copy locks (the dev API, PID from `ClinicManagement.API`, holds `Domain/Application/Infrastructure.dll` in `API\bin`) — **not compile errors**. Worked around by building into an isolated `OutDir` (scratchpad); compile was clean (**0 errors**, 13 pre-existing convention warnings).
- **Smart App Control is ON** (`VerifiedAndReputablePolicyState = 1`). The isolated-output build ran cleanly via `dotnet vstest` on the built DLL, so all runs above are real green-bars — no SAC `0x800711C7` block was hit on this path.
- Exact reproduce command (from `api/`):
  `dotnet build ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -p:OutDir=<scratch>/utbuild/` then
  `dotnet vstest <scratch>/utbuild/ClinicManagement.UnitTests.dll --TestCaseFilter:"FullyQualifiedName~DentalRecordPostVisitCompletionTests"`

## Working tree note (start of session)
Unrelated pre-existing uncommitted/untracked files present at start — excluded from this feature's changes:
`.claude/skills/start-clinic/*`, `.gitignore`, `CLAUDE.md`, `api/.../ClinicManagement.API.csproj`,
`api/.../appsettings.json`, `define-small-feature-prompt.md`, `features/LEARNINGS.md`,
`features/notification-center/*`, `packaging/server/clinic-server.iss`, `web/Dockerfile`,
`web/lib/realtime/use-clinic-realtime.ts`, `CLINIC-FEATURES-OVERVIEW.md`.

## Files Changed
### Backend
- `api/ClinicManagement.Domain/Entities/DentalRecord.cs` — added optional `Guid? AppointmentId` + ctor param (AC data/schema).
- `api/ClinicManagement.Infrastructure/Persistence/Configurations/DentalRecordConfiguration.cs` — mapped `AppointmentId` column + index (mirror `MedicalDocument`).
- `api/ClinicManagement.Application/DTOs/DentalRecordDto.cs` — added `Guid? AppointmentId` echo.
- `api/ClinicManagement.Application/Features/Patients/Commands/CreateDentalRecordCommand.cs` — added `AppointmentId`; injected `IAppointmentRepository`/`INotificationGenerator`/`IRealtimeNotifier`/`ILogger`; added best-effort post-commit `CompleteReviewedAppointmentAsync` (AC-3/4/5/7); echo in DTO.
- `api/ClinicManagement.Application/Features/Patients/Queries/GetDentalRecordsQuery.cs` — map `AppointmentId` in DTO.
- `api/ClinicManagement.Application/Features/Patients/Commands/UpdateDentalRecordCommand.cs` — map `AppointmentId` in DTO (update never sets it — out-of-scope respected).
- `api/ClinicManagement.Infrastructure/Migrations/20260717113419_AddDentalRecordAppointmentId.cs` (+ `.Designer.cs`) and `ApplicationDbContextModelSnapshot.cs` — additive nullable column + index.

### Frontend
- `web/lib/api/dental-records.ts` — added optional `appointmentId` to `CreateDentalRecordRequest`.
- `web/lib/api/types.ts` — added optional `appointmentId` to `DentalRecordDto`.
- `web/components/patient-record-modal.tsx` — new optional `appointmentId` prop; sent only on create (AC-3/6).
- `web/app/patients/[id]/page.tsx` — reads `?addRecord=1&appointmentId=...` from the URL, auto-opens the modal in create mode, carries `appointmentId`, strips the query (AC-2).
- `web/components/post-visit-review-popup.tsx` — "Ajouter le dossier médical" resolves `patientId` via the appointment and navigates to `/patients/{patientId}?addRecord=1&appointmentId=...` instead of `/documents`; keeps the review pending if the appointment fetch fails / has no patient (AC-1 + edge case).

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Hand-authored the EF migration (`AddDentalRecordAppointmentId`) + `.Designer.cs` + snapshot instead of `dotnet ef migrations add`. | `dotnet ef` can't run: the app is running (locks the API `bin` output) and the machine's WDAC/Smart App Control policy blocks loading freshly-built design-time DLLs (`0x800711C7`). Change is a single additive nullable column + index, mirrored on the `AddPostVisitReview`/`MedicalDocuments.AppointmentId` migration. **Must be regenerated/verified with the EF tool in an unrestricted environment before merge.** |
| Echoed `AppointmentId` in `DentalRecordDto` across Create/Get/Update mappers. | Spec permits an optional echo ("appointmentId echo optional"); mirrors the sibling `MedicalDocumentDto`. Additive optional field — no consumer breaks. |
| Read the `?addRecord=1` query via `window.location.search` in an effect rather than `useSearchParams()`. | Avoids Next's "wrap `useSearchParams` in Suspense" build requirement on this client page; same result, client-only. |

## Quality Checks
- **Backend build** (`ClinicManagement.Infrastructure` → transitively Domain + Application): **0 errors**, no new warnings in changed files. (The lone CS8618 on `DentalRecord.cs` `ProcedureType` is pre-existing — the EF private ctor + non-nullable string, untouched by this change.) Full-solution build's only errors were file-copy locks from the running API, not compile errors.
- **Frontend typecheck** (`npx tsc --noEmit`): **0 errors**.
- **Frontend lint**: ESLint is not a dependency in this repo and `next.config.ts` sets `eslint.ignoreDuringBuilds: true` — no ESLint gate exists here. Full `next build` skipped to avoid clobbering the running dev server's `.next`; `tsc` (the stricter type gate) passed.

## Significant Deviations
(none)
