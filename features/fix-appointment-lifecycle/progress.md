# Progress: Appointment Lifecycle Correctness

**Started:** 2026-07-21
**Type:** Small
**Branch:** feature/windows-desktop-app (user declined a dedicated branch)
**Note:** implemented together with `fix-appointment-google-sync` (both touch the appointment command handlers); test-infra edits are shared between the two.

## Status
- [x] Implementation
- [x] Quality checks — API host + UnitTests projects build to a scratch dir (host `bin` is locked by the running app — copy-lock is not a compile error) → 0 errors, 0 new warnings. FE `npx tsc --noEmit` → 0 errors.
- [x] Tests — new `AppointmentReactivationTests.cs` (3, #4) + `NotificationJobTests.cs` +1 (#11: a cancelled appointment at dispatch time is not sent). #9 (timezone) is FE → `tsc`/build-covered. Green — **but the tests caught a real implementation bug first; see below.**

## Working tree note (start of session)
Unrelated in-flight work EXCLUDED from staging: `medication-catalog-picker` (untracked `Medication*` + migration, `Infrastructure/Extensions.cs`, `Persistence/ApplicationDbContext.cs`, `ApplicationDbContextModelSnapshot.cs`, `appsettings.Development.json`); prior fixes `fix-patient-file-tenant-isolation`, `fix-single-dentist-identity`, `fix-document-cnam-accuracy`; other `features/fix-*` folders.

## Files Changed
- `api/.../Features/Appointments/Commands/UpdateAppointmentCommand.cs` — #4: reactivate a cancelled appointment before rescheduling; skip the reschedule (don't 400) when a cancelled appointment stays cancelled or the change is a spurious seconds-diff; never reschedule a completed appointment.
- `web/lib/hooks/use-appointments.ts` — #9: send day/week bounds as UTC instants (`toISOString()`) so the query matches the intended local day.
- `api/ClinicManagement.API/BackgroundJobs/NotificationJob.cs` — #11: re-check appointment status at send time; drop the reminder if the appointment is Cancelled/NoShow/missing.
- Test infra (shared with sync fixes): `NotificationJobTests.cs`, `AppointmentSyncMappingTests.cs`, `AppointmentTenantIsolationTests.cs`, `NotificationGenerationTests.cs`.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `NotificationJob` recheck marks a stale row terminal via the existing `FailAsync` ("Rendez-vous annulé…") | Spec AC-4 requires only "do not send"; leaving it Pending would retry forever. Failed is the only terminal non-sent state; reuses the existing helper, no contract change. |
| Test-infra compile fixes: added `IAppointmentRepository` (returns an active appointment) to the 3 `NotificationJob` constructions | Build-required — the ctor gained a dependency. Permissive mock preserves prior behavior (recheck passes → row sends as before). |

## Deferred to /test-small-feature
- New scenarios this enables: editing/reactivating a cancelled appointment succeeds; the near-midnight day-window boundary; the dispatcher dropping a reminder for a cancelled appointment at send time.

## Bug found & fixed by the tests
- **Caught:** `AppointmentReactivationTests` reactivate-to-Scheduled cases failed — reactivating a cancelled appointment returned a failure (implement-small-feature never ran tests, so it was missed).
- **Root cause:** both the new date-block reactivation and the pre-existing status-block reactivation called `Appointment.Reschedule(...)`, but `Reschedule` throws on a cancelled appointment (its own guard) — so reactivation always threw → caught → `Result.Failure`. #4 never actually worked end-to-end.
- **Fix (minimal, localized):** added a domain `Appointment.Reactivate(DateTime)` (Cancelled→Scheduled, clears cancellation fields) and routed both handler reactivation sites to it instead of `Reschedule`. Files: `Domain/Entities/Appointment.cs`, `UpdateAppointmentCommand.cs`. Re-ran — all green (163 tests across affected areas, 0 failures).

## Significant Deviations
(none)
