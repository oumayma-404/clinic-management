# Staff chat — codebase context (pointers, not contents)

**Last verified:** 2026-10-15 @ 8c2b24de (+ Part 1A working tree)
**Staleness check:** `git diff --stat 8c2b24de..HEAD -- api/ClinicManagement.API/Hubs api/ClinicManagement.Application/Common/Behaviors/RealtimeResourceResolver.cs api/ClinicManagement.Application/Features/Notifications api/ClinicManagement.Infrastructure/Persistence/ApplicationDbContext.cs web/lib/realtime web/lib/query/keys.ts web/components/dashboard-header.tsx web/components/bottom-nav.tsx web/app/layout.tsx desktop/ClinicManagement.DesktopShell/VaultBridge.cs desktop/ClinicManagement.DesktopShell/MainWindow.xaml.cs`

## Where to imitate

| Need | Look at |
|---|---|
| Command + handler + caller resolution | `api/ClinicManagement.Application/Features/Notifications/Commands/MarkNotificationReadCommand.cs` |
| Controller (`AnyClinicRole`, `HandleFailure`, subscription exemption) | `api/ClinicManagement.API/Controllers/NotificationsController.cs`, `ApiControllerBase.cs` |
| Status from a `Result.Code` | `ExpensesController.cs` (`NotFoundOrBadRequest`) |
| Post-commit best-effort side effect | `Application/Common/Services/NotificationGenerator.cs` (`SafelyAsync`) |
| Factory entity | `Domain/Entities/DeviceRegistration.cs` |
| Per-user marker | `Domain/Entities/NotificationRead.cs` + its configuration |
| Owned collection (Part 2) | `Patient.AdditionalPhoneNumbers` → `PatientConfiguration.cs` |
| Free-text limits + French refusals | `Application/Features/WaitingList/WaitingListLimits.cs` |
| Handler tests / tenant isolation | `UnitTests/Features/Notifications/NotificationTenantIsolationTests.cs`, `NotificationDismissalTests.cs` |
| Clinic roster for a non-admin | `IUserRepository.GetByClinicIdAsync(clinicId, paging: null)` (filter `IsActive` yourself); precedent `Features/Relay/Commands/DeclareRelayLostCommand.cs` |
| Dentist ↔ user | `StaffNotificationRules.ResolveDoctorUserIdAsync`, `IDoctorRepository.GetByClinicIdAsync` |
| Today in Tunis | `Application/Common/ClinicClock.cs` |
| Web api module / hook / cached query | `web/lib/api/notifications.ts`, `web/lib/hooks/use-notifications.ts`, `web/lib/query/keys.ts` |
| Current user id (web) | `useUserStatus().data?.user?.id` (`web/lib/hooks/use-user-status.ts`) — `SessionUser` has no id |
| Realtime provider (the one connection) | `web/lib/realtime/clinic-realtime-provider.tsx`, `clinic-hub.ts` |
| Toast with actions (precedent only) | `web/components/post-visit-review-popup.tsx` |
| Short ordered list editor | `web/components/procedure-type-steps-dialog.tsx` |
| Bridge method + feature-detect | `desktop/.../VaultBridge.cs` (`Script`), `web/types/clinic-shell.d.ts`, `mobile/shared/bridge.md` |

## Authorities this feature routes through

- Who reads / who rings a thread → `Domain/ValueObjects/StaffConversation.cs` (only there).
- Tenant filter → `ApplicationDbContext.OnModelCreating` (`IsSystemWide || ClinicId == ScopedClinicId`).
- Archive / PC de secours scopes → `Infrastructure/Persistence/ClinicArchiveScope.cs` `Excluded`, `ClinicRelayScope.cs` `Added`.
- Patient-bearing entity not journaled → `UnitTests/Common/ClinicalRecordAuditCoverageTests.cs` `NotClinicalByDesign`.
- Realtime key contract → `Application/Common/Behaviors/RealtimeResourceResolver.cs` `ExcludedAreas` + `RealtimeResourceResolverTests`.
- Hub constructor deps → `UnitTests/Hubs/ClinicHubTenantScopeTests.cs` `UnfilteredDependencies`.

## Gates (verified 2026-10-15)

| Gate | Command (from the worktree) | Notes |
|---|---|---|
| Backend suite | `cd api && dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release -p:BaseOutputPath=<scratch>/build-sc/` | unfiltered; ~30 s after build; 5 756 tests at Part 1A |
| Migration scaffold | `cd api/ClinicManagement.Infrastructure && BaseOutputPath=<scratch>/build-sc/ dotnet ef migrations add <Name> --configuration Release` | same `BaseOutputPath` as the test build (shared `obj/`); delete scaffolded `xmin` |
| Schema | `<scratch>/schema-check.sh <migration|-> <out>` — isolated DB `clinic_staffchat` on the shared postgres | exit 0 clean / 2 drift |
| Frontend | `cd web && npm run check:responsive && npx tsc --noEmit && npm run build` | needs `npm ci` in the worktree first |
| Docker engine | Rancher Desktop (`C:\Program Files\Rancher Desktop\Rancher Desktop.exe`), `docker.exe` under `resources\resources\win32\bin` | not Docker Desktop |

**Gates that do NOT exist here:** no web unit-test runner, no working ESLint, no DB-touching backend tests.

## ⚠️ Volatile

| Fact | Re-check |
|---|---|
| Shared stack owner | `stack-lease.ps1 status -Session staff-chat` |
| `staffchat` must NOT be in the `RealtimeResource` block | `grep -n staffchat web/lib/realtime/clinic-hub.ts` |
| Latest `check:responsive` N-number (plan says N52) | `grep -n "N5[0-9]" web/scripts/check-responsive.mjs \| tail -3` |
| Shell shipped version comes from the next `client-v*` tag | `git tag -l "client-v*" \| tail -1` |
