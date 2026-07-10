# Progress: Real-Time Updates (Appointments slice)

**Started:** 2026-07-10
**Type:** Small
**Branch:** feature/windows-desktop-app (user chose to reuse current branch)

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [x] Tests (added — see Test Plan + Tests Run below)

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `FullyQualifiedName~ClinicManagement.UnitTests.Hubs` + `...Features.Appointments` | **18 passed, 0 failed, 0 skipped** |

- New: `ClinicGroupsTests` (2), `SignalRRealtimeNotifierTests` (2), `ClinicHubTests` (3), `AppointmentRealtimeBroadcastTests` (5) = 12 new tests.
- Existing `AppointmentSyncMappingTests` (6) re-ran green — no regression.
- Test project build: **0 errors, 0 warnings**. No Smart App Control block this run.
- Frameworks: xUnit + Moq (repo has no integration/Testcontainers project; no FE test runner → AC-4 reconnect not unit-testable here).

### Quality check results
- Backend `dotnet build ClinicManagement.sln`: **0 errors, 0 warnings** (57 pre-existing warnings unchanged; none in new/changed files).
- Frontend `npx tsc --noEmit`: **clean**.
- Frontend `npm run build`: **succeeds** (`/appointments` builds with the real-time hook). ESLint not installed in this repo → FE gate is tsc + build (per LEARNINGS).

## Working tree note (start of session)
Unrelated uncommitted files belonging to the windows-desktop-app (Phase 5) work — EXCLUDE from any real-time-updates commit:
- `.gitignore`, `api/ClinicManagement.API/Program.cs` (will ALSO be edited here — see note), `desktop/ClinicManagement.DesktopShell/MainWindow.xaml.cs`, `features/LEARNINGS.md`, `packaging/client/clinic-client.iss`, `packaging/publish-server.ps1`, `packaging/server/clinic-server.iss`, `web/middleware.ts`
- untracked: `features/windows-desktop-app/retrospective.md`, `features/windows-desktop-app/reviews/feature-review.md`, `packaging/fetch-build-tools.ps1`

> NOTE: `Program.cs` is pre-modified by Phase 5 AND edited by this feature. When committing, the two concerns are interleaved in one file; separate manually or commit together with a clear message.

## Files Changed
Backend:
- `api/ClinicManagement.Application/Common/Interfaces/IRealtimeNotifier.cs` (new) — outbound seam
- `api/ClinicManagement.API/Hubs/ClinicHub.cs` (new) — SignalR hub, clinic-scoped groups
- `api/ClinicManagement.API/Hubs/ClinicGroups.cs` (new) — shared group-name helper (single source of truth)
- `api/ClinicManagement.API/Hubs/SignalRRealtimeNotifier.cs` (new) — IRealtimeNotifier impl over IHubContext
- `api/ClinicManagement.Application/Features/Appointments/Commands/CreateAppointmentCommand.cs` — broadcast after commit
- `api/ClinicManagement.Application/Features/Appointments/Commands/UpdateAppointmentCommand.cs` — broadcast after commit
- `api/ClinicManagement.API/Program.cs` — AddSignalR, register notifier, JWT access_token query for /hub, MapHub

Frontend:
- `web/package.json` — add @microsoft/signalr
- `web/lib/realtime/clinic-hub.ts` (new) — connection factory + hub URL resolution
- `web/lib/realtime/use-clinic-realtime.ts` (new) — React hook: connect + onAppointmentsChanged
- `web/app/appointments/page.tsx` — subscribe; refetch (bump refreshKey) on event

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Added `new Mock<IRealtimeNotifier>().Object` to the two handler constructions in `ClinicManagement.UnitTests/.../AppointmentSyncMappingTests.cs` | Build-required compile fix: the new `IRealtimeNotifier` ctor param broke the test project's compile. Mechanical only (Moq returns a completed Task by default so the awaited broadcast is a no-op). Behavior/assertion tests for the broadcast are DEFERRED to /test-small-feature. |

## Significant Deviations

## Test Plan
| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 | New class | `ClinicManagement.UnitTests/Features/Appointments/AppointmentRealtimeBroadcastTests.cs` | Create/Update broadcast to the clinic **after commit**; NOT on a pre-commit failure; cancellation (status=Cancelled) broadcasts |
| AC-2 | New class | `ClinicManagement.UnitTests/Hubs/ClinicGroupsTests.cs` | `ClinicGroups.Name(id)` == `clinic-{id}` (single source of truth), distinct per clinic |
| AC-2 / AC-5 | New class | `ClinicManagement.UnitTests/Hubs/SignalRRealtimeNotifierTests.cs` | Sends `appointmentsChanged` to `clinic-{id}` group only; swallows hub failures (never throws) |
| AC-2 / AC-3 | New class | `ClinicManagement.UnitTests/Hubs/ClinicHubTests.cs` | `[Authorize]` present (AC-3); connection joins its own clinic group; no group when unauthenticated |
| AC-4 | Not unit-tested | — | Reconnect (`withAutomaticReconnect` in `web/lib/realtime/clinic-hub.ts`) is a frontend concern; the repo has no FE test runner (FE gate is tsc + build per LEARNINGS) — DEFERRED to manual/operator verification |
