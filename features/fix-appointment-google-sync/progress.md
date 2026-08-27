# Progress: Appointment → Google Calendar Sync on Create + Offline Gating

**Started:** 2026-07-21
**Type:** Small
**Branch:** feature/windows-desktop-app (user declined a dedicated branch)
**Note:** implemented together with `fix-appointment-lifecycle` (both touch the appointment command handlers); test-infra edits are shared between the two.

## Status
- [x] Implementation
- [x] Quality checks — API host + UnitTests projects build to a scratch dir (host `bin` locked by the running app; copy-lock ≠ compile error) → 0 errors, 0 new warnings.
- [x] Tests — coverage note: #5/#17 are a fire-and-forget background Google sync gated on `IInternetProbe`; there is no clean unit seam (needs a real OAuth round-trip + a background `Task.Run`). Covered by the existing `AppointmentSyncMappingTests` `IsSyncedToGoogle` mapping + the build; the actual push and offline-skip are integration/manual. (The connectivity-gate scope wiring is exercised indirectly by the extended `ScopeFactory()` in the appointment tests.)

## Working tree note (start of session)
Unrelated in-flight work EXCLUDED from staging: `medication-catalog-picker`; prior fixes `fix-patient-file-tenant-isolation`, `fix-single-dentist-identity`, `fix-document-cnam-accuracy`; other `features/fix-*` folders.

## Files Changed
- `api/.../Features/Appointments/Commands/CreateAppointmentCommand.cs` — #5: post-commit fire-and-forget Google sync mirroring the update path (new `IServiceScopeFactory` dep); #17: gated on `IInternetProbe`.
- `api/.../Features/Appointments/Commands/UpdateAppointmentCommand.cs` — #17: gate the existing update-path sync on `IInternetProbe` (skip the OAuth refresh when the server is offline).
- Test infra (shared with lifecycle fixes): each `ScopeFactory()` helper now also provides `ILogger<CreateAppointmentCommandHandler>` + `IInternetProbe`; `IServiceScopeFactory` passed to the 3 `CreateAppointmentCommandHandler` constructions.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Create sync duplicates the Update path's `Task.Run` scope pattern (rather than extracting a shared service) | The spec says "mirror the update path"; extracting an abstraction would be a larger, unrequested refactor. Both use the connectivity-gated fire-and-forget block. |
| Connectivity gate resolves `IInternetProbe` inside the sync scope (both create + update) | `IInternetProbe` is a registered Singleton (both modes); in Cloud it reports reachable so sync proceeds unchanged. Spec AC-3. |
| Test-infra compile fixes: `IServiceScopeFactory` added to Create constructions; `ScopeFactory()` extended | Build-required — the Create ctor gained a dependency and the sync now resolves the probe/logger. Permissive mocks preserve prior behavior. |

## Deferred to /test-small-feature
- New scenarios: a created patient appointment triggers App→Google sync when online; sync is skipped when the server is offline; patient-less slots are not synced.

## Significant Deviations
(none)
