# Progress: Live Dashboard

**Started:** 2026-06-25
**Type:** Small
**Branch:** feature/live-dashboard

## Status
- [x] Implementation
- [x] Quality checks (build, typecheck) — see notes
- [x] Tests (added xUnit project — see Test Plan / Tests Run)

## Test Plan
This repo had NO test infrastructure (no test project, no FE test deps). Per user decision, created a new backend xUnit unit-test project and unit-tested the new query handler with mocked dependencies. Frontend ACs (AC-5/6/7 UI states) not automated — no FE harness; covered by manual verification.

| AC | Action | Target | Notes |
|----|--------|--------|-------|
| AC-1 | New test class | `ClinicManagement.UnitTests/Features/Dashboard/GetDashboardStatsQueryHandlerTests.cs` | Counts mapped from repos; clinic scoping verified |
| AC-2 | New test class | same | Verifies Pending counted via `AppointmentStatus.Scheduled` |
| AC-3 | New test class | same | Today vs week counts mapped from distinct repo calls |
| AC-4 | New test class | same | Urgent = `CountFlaggedByClinicIdAsync` mapped |
| AC-7 | New test class | same | Failure (no fabricated data) when no token user / user not found |
| AC-5/6 | Manual | web/components/appointment-list.tsx, stats-card.tsx | FE loading/empty/error states — no FE test harness in repo |

New test project: `api/ClinicManagement.UnitTests` (xUnit + Moq, net8.0), added to `ClinicManagement.sln`.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `ClinicManagement.UnitTests` (GetDashboardStatsQueryHandlerTests) | 5 passed, 0 failed, 0 skipped |

Final `dotnet build ClinicManagement.sln`: 0 errors, 0 new (non-CS8632) warnings.

## Quality check notes
- **Backend `dotnet build`**: 0 errors, 0 new non-CS8632 warnings in changed files. (Pre-existing solution-wide CS8632 nullable warnings unchanged. Note: had to stop the running dev API (port 5000) first — it held DLL locks causing MSB3026/3027 copy errors, not compile errors.)
- **Frontend `tsc --noEmit`**: my 8 changed + 5 new files compile clean. ONE pre-existing error remains in `web/components/document-editor-content.tsx(721,27)` — a file NOT touched by this feature (it's the doc-editor tech debt flagged in the UX review, slated for SF-06). Out of scope here.
- **Lint**: not runnable in this project — `eslint` is not installed (absent from devDependencies) and `next.config.ts` disables ESLint during build. No lint gate available.

## Working tree note (start of session)
Pre-existing unrelated modified/untracked files exist (build artifacts under api/.../bin & obj, docs, .idea, the `reviews/` and other `features/` folders, and the `.claude/skills/start-clinic` skill). These are NOT part of this feature and are excluded from staging. Only files listed under "Files Changed" belong to this feature.

## Files Changed
Backend:
- api/ClinicManagement.Application/DTOs/DashboardStatsDto.cs (new)
- api/ClinicManagement.Application/Features/Dashboard/Queries/GetDashboardStatsQuery.cs (new)
- api/ClinicManagement.Domain/Repositories/IAppointmentRepository.cs (add CountByClinicIdAsync)
- api/ClinicManagement.Domain/Repositories/IPatientRepository.cs (add CountByClinicIdAsync, CountFlaggedByClinicIdAsync)
- api/ClinicManagement.Infrastructure/Repositories/AppointmentRepository.cs (impl)
- api/ClinicManagement.Infrastructure/Repositories/PatientRepository.cs (impl)
- api/ClinicManagement.API/Controllers/DashboardController.cs (new)

Frontend:
- web/lib/api/types.ts (add DashboardStats)
- web/lib/api/dashboard.ts (new)
- web/lib/hooks/use-dashboard-stats.ts (new)
- web/app/page.tsx (wire KPI cards, loading/error)
- web/components/stats-card.tsx (add loading state)
- web/components/appointment-list.tsx (real today's appointments + loading/empty)

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| "Pending" mapped to `AppointmentStatus.Scheduled` | Domain enum has no `Pending`; `Scheduled` is the awaiting-confirmation state (matches the card's existing "Awaiting confirmation" label). Same intended behavior. |
| Stats endpoint accepts optional `todayStart/todayEnd/weekStart/weekEnd` query params | Fulfils the spec's stated edge case ("boundaries follow the same local-day handling as useAppointments so card counts match the list"). Brand-new endpoint, no other consumers, response shape unchanged. Defaults to UTC-now ranges when omitted. |
| Added efficient `Count*` repo methods instead of counting loaded lists | Spec asked for "one efficient query"; uses EF `CountAsync`. Internal, additive to existing repos. |

## Significant Deviations
(none)
