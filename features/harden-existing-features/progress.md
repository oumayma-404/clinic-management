# Progress: Harden & Fix Existing Features (Cleanup Pass)

**Started:** 2026-07-10
**Type:** Small
**Branch:** feature/windows-desktop-app (per user — spec hardens Phase 4/5 code that only exists here)

## Status
- [x] Implementation
- [x] Quality checks (backend build 0/0; frontend `tsc --noEmit` 0 errors)
- [x] Tests (added — see Test Plan + Tests Run below)

## Test Plan
Targeted xUnit + Moq unit tests (the repo's only test project, `ClinicManagement.UnitTests`; no
integration/Testcontainers project). Each AC traced to at least one scenario; FE/doc ACs (AC-9, AC-10)
are deletions verified by `tsc` at implementation time (no unit surface).

| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 (patient) | New class | `Features/Patients/PatientHardeningTests.cs` | Update + medical-history cross-clinic → not found |
| AC-8 | New scenarios | `Features/Patients/PatientHardeningTests.cs` | Insurance clears on null, sets when provided |
| AC-1 (appt), AC-2 | New class | `Features/Appointments/AppointmentTenantIsolationTests.cs` | Update cross-clinic; Create foreign patient/procedure-type |
| AC-1 (procedure) | New class | `Features/ProcedureTypes/ProcedureTypeTenantIsolationTests.cs` | Update/Delete cross-clinic; Create stamps caller clinic |
| AC-1 (docs), AC-7 | New class | `Features/Documents/MedicalDocumentTenantIsolationTests.cs` | Get/Delete cross-clinic; GetAll scoping; delete removes file row + blob |
| AC-3 | New class | `Common/CurrentClinicProviderTests.cs` | Backstop provider returns clinic id / null when none |
| AC-4, AC-5 | New class | `Api/GoogleCalendarControllerHardeningTests.cs` | Auth attributes; OAuth `state` reject missing/unknown, Authorize stores state |
| AC-6 | New class | `Infrastructure/Services/GoogleCalendarNotesParseTests.cs` | `ExtractNotesFromDescription` parses only Notes line, no metadata nesting |

**Coverage notes:**
- AC-4 is *also* pinned structurally by the pre-existing `ControllerAuthorizationCoverageTests`; the new
  test adds an explicit per-endpoint assertion.
- AC-5 positive path (a matching `state` succeeds end-to-end) can't be unit-tested without a real Google
  token-exchange HTTP call; covered by asserting `Authorize` persists exactly one state entry + the two
  reject paths. Full OAuth round-trip is manual/operator-level.
- AC-3's EF global-query-filter behavior is integration-level (needs a DbContext); the unit test covers
  the `ICurrentClinicProvider` backstop the filter reads from.
- AC-9 / AC-10 are frontend deletions + doc edits (no backend/unit surface) — verified by `tsc` clean at
  implementation time.
- **Test-class count (8 new files) exceeds the small-feature "~5" soft heuristic**, but this is a
  hardening pass fanning across many existing handlers (not a new feature needing E2E). Escalating a
  bug-fix pass to the full pipeline would add no value, so kept in the small-feature flow per the
  heuristic's intent.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit (new) | 7 hardening classes (see Test Plan) | 26 passed, 0 failed |
| Unit (full project) | `ClinicManagement.UnitTests` | 201 passed, 0 failed, 0 skipped |

- Command: `dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj --no-build`.
- Build: 0 errors, 0 new warnings. Smart App Control did **not** block this run (tests loaded + passed).

## Working tree note (start of session)
- `define-small-feature-prompt.md` (untracked, unrelated) — excluded from this feature's commits.

## Quality checks
- **Backend:** `dotnet build ClinicManagement.sln` → 0 errors, 0 new warnings. The ~13 solution-wide
  warnings (CS8618/CS8602/CS0618) are pre-existing and NOT in any file I changed (verified).
- **Frontend typecheck:** `npx tsc --noEmit` → 0 errors.
- **Frontend lint:** ESLint is **not installed** in `web/node_modules` and is disabled during the Next
  build (`next.config.ts`), so `npm run lint` cannot run locally. All FE edits are deletions/
  simplifications; `tsc` (strict) is clean. Lint is the CI/build gate.
- **Migration:** `dotnet ef migrations add AddProcedureTypeClinicId` generated + backfill SQL added by hand.

## Files Changed
### Backend
- `Application/Common/Interfaces/ICurrentClinicProvider.cs` (new) — backstop clinic source for the filter.
- `Application/Common/Services/CurrentClinicProvider.cs` (new).
- `Application/Extensions.cs` — register `ICurrentClinicProvider`.
- `Domain/Entities/ProcedureType.cs` — add `ClinicId` + ctor param.
- `Infrastructure/Persistence/Configurations/ProcedureTypeConfiguration.cs` — composite unique (ClinicId, Name).
- `Infrastructure/Persistence/ApplicationDbContext.cs` — clinic-provider ctor, global query filters, warning ignore.
- `Infrastructure/Migrations/20260710163519_AddProcedureTypeClinicId.*` (new) — ClinicId column + backfill + index swap.
- `Infrastructure/Services/GoogleCalendarSyncService.cs` — Notes round-trip parse fix (§4).
- `API/Controllers/GoogleCalendarController.cs` — class `[Authorize]` (§2) + OAuth state validation (§3).
- Handlers with explicit clinic checks: `UpdatePatientCommand` (+ insurance-clear §4), `CreateAppointmentCommand`,
  `UpdateAppointmentCommand`, `GetMedicalDocumentQuery`, `GetMedicalDocumentsQuery`, `UpdateMedicalDocumentCommand`,
  `DeleteMedicalDocumentCommand` (+ blob/orphan delete §4), medical-history + family-history + dental-record
  Create/Update/Delete, `CreateProcedureTypeCommand` (+ assign ClinicId), `UpdateProcedureTypeCommand`, `DeleteProcedureTypeCommand`.
- `UnitTests/Features/Appointments/AppointmentSyncMappingTests.cs` — build-required ctor fix + resolver mock.

### Frontend
- `web/lib/hooks/use-clinic-access.ts` — no /setup redirect on transient error; removed debug logs (§5).
- `web/lib/api/appointments.ts` — removed dead `get`/`delete` + unused import (§5).
- `web/lib/api/medical-documents.ts`, `web/components/{setup-wizard,join-wizard,clinic-settings}.tsx` — removed console.log (§5).
- `web/app/page.tsx` — removed `NotificationsList` import + render (§5).

### Docs (§6)
- `CLAUDE.md`, `web/CLAUDE.md`, `web/components/CLAUDE.md`, `web/app/change-password/page.tsx` — corrected
  stale "sample data" claims + `/api/auth/*` → `/bff/auth/*`.

## Review Fixes Applied (post-challenge, 2026-07-10)
`/apply-review-fixes` on `reviews/feature-review.md` — 9 of 12 findings fixed, 3 skipped (challenged/spec-mandated).

| # | Sev | Fix |
|---|-----|-----|
| 1 | Critical | `GetDentalRecordsQuery` — resolve clinic + verify owning patient (cross-tenant dental read closed, AC-1). |
| 2 | Major | `GetProcedureTypesQuery` + `GetProcedureTypeQuery` — explicit clinic scope (no longer rely on fail-open filter). |
| 3 | Major | Documented the backstop invariant in `CurrentClinicProvider` (fail-open is spec §1-mandated; handlers authoritative). No behavior change. |
| 4 | Major | `ClinicGuard` consumes `error`/`refresh` — distinct "Connexion au serveur impossible" + Réessayer on transient blip (AC-9 intent). |
| 5 | Minor | `IMedicalDocumentRepository.GetByClinicIdAsync` (SQL-scoped); no-arg branch no longer materializes all tenants' PHI. |
| 6 | Minor | OAuth `state`: CSPRNG (`RandomNumberGenerator`) + HttpOnly double-submit cookie bound to the browser (login-CSRF). |
| 7 | Minor | `DeleteMedicalDocumentCommand` — inject `ILogger`, log swallowed blob-cleanup failure, don't swallow cancellation. |
| 8 | Minor | `GoogleCalendarSyncService` — shared `NotesLabel`/`StatusLabel` consts (reader/writer can't drift → AC-6). |
| 9 | Minor | Migration `Down()` recreates `Name` index NON-unique (can't throw on cross-clinic dup names). |

**Skipped:** #10 (Suggestion — resolver consolidation is a refactor risking DEV-1 semantics), #11 & #12 (Suggestion — spec-mandated behavior; challenge said do-not-fix).

**Tests:** +5 new (dental cross-clinic read; procedure-type single-Get + list scoping; OAuth state-without-cookie + cookie-mismatch). Updated `MedicalDocumentTenantIsolationTests` (new logger arg + `GetByClinicIdAsync` mock). Backend build 0 errors / 0 new warnings; `tsc` clean; **206 passed, 0 failed** (was 201).

**Committed & pushed** — commit `5a126f3` on `feature/windows-desktop-app`, pushed to origin.

## Pull Request

**Status:** pending manual creation (gh CLI account mismatch — see below)
**Date:** 2026-07-10
**Branch:** feature/windows-desktop-app -> main
**Commit:** 5a126f3
**Created by:** Claude
**Note:** Branch pushed via git (identity `oumayma-404`). `gh pr create` is blocked because the
`gh` CLI is authenticated as `o-benkhalifa`, which cannot resolve `oumayma-404/clinic-management`.
Open the PR via the compare URL below, or `gh auth switch` to `oumayma-404` and re-run.
**Compare URL:** https://github.com/oumayma-404/clinic-management/compare/main...feature/windows-desktop-app?expand=1

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|

## Significant Deviations
- **DEV-1 (approved):** Background-job-reachable handlers (`GetMedicalDocumentQuery`, `UpdateMedicalDocumentCommand`) apply the clinic-ownership check only when a user/clinic is resolvable, and skip it for no-context (Hangfire) calls — mirroring the global filter's AC-3 "inactive when no clinic in scope" rule. Prevents `PdfGenerationJob` from breaking. User approved.
- **DEV-2 (approved):** `ProcedureType` global `UNIQUE(Name)` index changed to composite `UNIQUE(ClinicId, Name)` so each clinic has its own name namespace (spec only stated "add ClinicId column"). Required for correct tenant scoping of Create. User approved.
