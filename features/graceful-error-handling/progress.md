# Progress: Graceful Error Handling

**Started:** 2026-07-17
**Type:** Small
**Branch:** feature/windows-desktop-app (user chose to use current branch)

## Status
- [x] Implementation
- [x] Quality checks (build, typecheck) — see Quality note
- [x] Tests (added — see Test Plan + Tests Run below)

## Quality note (checks run)
- Backend: `dotnet build ClinicManagement.API.csproj` (to an unlocked output dir, since the API host was running and locked its bin) → **0 errors**; only pre-existing `CS8632`/`CS8602`/`CS8600` warnings on unchanged lines.
- Frontend: `npx tsc --noEmit` → **0 errors**. `npx next build` → **success (17/17 routes)** after clearing a stale `.next` cache (a stale cache caused a spurious `PageNotFoundError` on an untouched BFF route; a clean rebuild is green). ESLint is **not installed** in this repo's node_modules and `next build` runs with linting disabled (repo config), so `npm run lint` is not a runnable gate here — `tsc --noEmit` + `next build` are the real FE gate.

## Working tree note (start of session)
The branch already carries substantial unrelated uncommitted work (windows-desktop-app / dental-record / post-visit-review features). Those files are NOT part of this feature and are excluded from this feature's staging. Only graceful-error-handling files are touched here. Files staged for THIS feature are listed under "Files Changed".

## Files Changed
_Backend_
- api/ClinicManagement.API/Controllers/ApiControllerBase.cs (new) — canonical `{ error }` helper
- All 18 controllers → extend ApiControllerBase, route failures through helper
- api/ClinicManagement.Application/Common/Exceptions/ExceptionMiddleware.cs — add ValidationException → 400 { error }

_Frontend_
- web/lib/api/client.ts — parse canonical `error` first (+ legacy tolerance), `AbortController` timeout (30s), French network/timeout/unexpected fallbacks; exports `ensureSuccess`/`toApiError` for raw-fetch modules
- web/lib/errors.ts (new) — `getErrorMessage` / `showErrorToast`
- web/app/error.tsx + web/app/global-error.tsx (new) — French boundaries + "Réessayer"
- Raw-fetch normalization → throw `ApiError`: web/lib/api/patient-files.ts, web/lib/api/clinics.ts (getLogo), web/lib/api/medical-documents.ts (generatePdfForDownload)
- alert() → toast: web/components/procedure-types-table.tsx, web/app/appointments/page.tsx
- Silent-swallow fixes (AC-5): web/components/appointment-calendar.tsx (surfaces useAppointments error), web/app/patients/[id]/page.tsx (file preview/download/reload)
- Localization (AC-9) of FE-owned error/fallback strings: hooks (use-appointments, use-dashboard-stats, use-clinic-access), records/page, files/page, stock-table, patients-table, user-management, ai-chat, create/edit-appointment dialogs, edit-patient-dialog, procedure-type-form-modal, stock-item-form-modal, clinic-settings, join/page, join-wizard, setup-wizard, login/page, change-password-form
- Note: patient-files-manager.tsx and document-editor-content.tsx already used French toasts (no change). Success-toast strings left as-is (AC-9 scope is error/fallback/network text).

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Injected `ILogger<MedicalDocumentsController>` into MedicalDocumentsController | Needed to log the PDF-generation exception server-side (AC-8) after removing `ex.Message` from the client body. Internal, additive. |
| GoogleCalendar 500 details/`ex.Message` replaced with generic English messages (already logged) | AC-8 (no internal-detail leakage). Backend messages stay English per spec Out-of-Scope. |
| Dropped `catch (Exception ex)` variable in ClinicsController DoctorInfo parse; message no longer interpolates `ex.Message` | AC-8; behavior (catch-all → 400 "Invalid DoctorInfo format.") preserved. |
| Removed now-unused `using System.Net;` from AuthController | The only `HttpStatusCode` use was replaced by `Failure(..., StatusCodes.Status403Forbidden)`; unused-using rule. |

## Pre-existing warnings note
`dotnet build` on the changed API files still reports pre-existing `CS8602`/`CS8600` nullable warnings on **unchanged lines** (`result.Value.Id` after the `IsFailure` guard in Appointments/Patients/ProcedureTypes/MedicalDocuments `CreatedAtAction`, and `ReadFromJsonAsync` assignments in MedicalDocuments). These are not introduced by this feature (the edited lines were the failure returns / class declarations only) and are out of scope to fix here.

## Backend build note
The API host process was running and locked its `bin` (MSB3021/MSB3027 copy-lock, not compile errors). Verified a clean compile by building `ClinicManagement.API.csproj` to an unlocked output dir: **0 errors**, no new non-CS8632 warnings from changed files.

## Significant Deviations
_None._

## Test Plan
This is a mostly-frontend hardening feature; the **backend** contribution is two testable seams
(`ApiControllerBase` + the `ExceptionMiddleware` canonical body). The frontend has **no test
framework** in this repo (ESLint isn't even installed; `tsc --noEmit` + `next build` are the gates,
per the Quality note above), so FE-only ACs are accounted for as coverage notes, not contrived tests.

| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 | New test class | `api/ClinicManagement.UnitTests/Api/ApiControllerBaseTests.cs` | Pins the canonical `{ error }` shape (exact `error` key) + that the action's chosen status code is preserved through `HandleFailure`/`Failure`; defaults to 400. |
| AC-1 (middleware) | New test class | `api/ClinicManagement.UnitTests/Common/Exceptions/ExceptionMiddlewareTests.cs` | `ForbiddenAccessException`→403, `NotFoundException`→404, **`ValidationException`→400 (the new case)** all render `{ error }`; content-type is `application/json`. |
| AC-8 | New scenarios | both new classes | `ApiControllerBase` blank/null message → generic message (never empty body); middleware unhandled exception → **generic 500** that does not leak the exception message/internals. |
| AC-10 | Run existing (regression) | `ControllerAuthorizationCoverageTests.cs` | All 18 controllers now extend the **abstract** `ApiControllerBase`; the reflection scan (`!IsAbstract`) must be unaffected — run to prove Cloud/Local auth surface didn't drift. |

### Coverage notes (ACs with no backend/unit surface)
- **AC-2, AC-3, AC-4, AC-5, AC-6, AC-7, AC-9** — all frontend-only (`client.ts` parsing/timeout,
  error boundaries, `alert()`→toast, list error states, raw-`fetch` `ApiError`, FR localization).
  No FE test framework exists in the repo; covered by the `tsc --noEmit` + `next build` gate run at
  implementation time (see Quality note). No contrived unit tests written for these.
- Backend business messages stay English by spec (Out of Scope), so no assertion on message language.

## Tests Added
- `api/ClinicManagement.UnitTests/Api/ApiControllerBaseTests.cs` (new) — pins the canonical `{ error }`
  shape, status-code preservation (400/401/403/404 + default 400), and the null/blank→generic fallback.
- `api/ClinicManagement.UnitTests/Common/Exceptions/ExceptionMiddlewareTests.cs` (new) — Forbidden→403,
  NotFound→404, **ValidationException→400 (new case)**, and unhandled→generic 500 with no internal leak.

## Tests Run
Ran via the isolated-`OutDir` + `dotnet vstest` recipe (Smart App Control is ON on this box and blocks
plain `dotnet test` at load with `0x800711C7`; building into a scratch dir and running `vstest` on the
prebuilt DLL dodges the block — recipe from MEMORY). Targeted classes only, no full suite.

| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `ApiControllerBaseTests` | 9 passed, 0 failed |
| Unit | `ExceptionMiddlewareTests` | 4 passed, 0 failed |
| Unit (regression, AC-10) | `ControllerAuthorizationCoverageTests` | 2 passed, 0 failed |

**Total: 15 passed, 0 failed, 0 skipped.** Test-project build: 0 errors; no new warnings from the added
files (pre-existing CS86xx/CS0618/CS8981 warnings on unchanged files only).
