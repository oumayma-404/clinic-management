# Story 6: BE - The cabinet renews itself every night without interrupting anyone

**Status:** APPROVED
**Story Status:** reviewed (2026-11-03, 100/100)
**Layer:** BE
**Depends On:** 4
**Blocks:** 7

## Objective

At 03:00 Tunis a new cabinet is built beside the current one, money-checked, then made current; visitors inside keep
yesterday's until their session ends; cabinets older than the previous one are removed safely; a failure keeps the
current cabinet and shows up as a stale `demoBuiltOn`.

## Acceptance Criteria

_From spec:_

- [x] AC-7.5 rebuilt nightly with dates moved to the new today
- [x] AC-7.6 money check passes before the switch; otherwise yesterday's stays and the failure is visible
- [x] FR-7 built aside, switched only when complete; old one removed once no session can use it; rebuilding screen only with no cabinet at all
- [x] EC-2 visitor at 03:00 keeps walking; EC-7 failed rebuild keeps yesterday's

_Story-specific:_

- [x] Admin activated only after the check (ordering test)
- [x] Purge mirrors `DeleteClinicFromConsoleCommand:130-176` — transaction, `IClinicPurge.PurgeAsync`, commit, blob sweep after commit
- [x] Job declares its tenant scopes (`SystemWideCallerCoverageTests` green)

## Entry Criteria

- [ ] Story 4 implemented (full builder)

## Steps

1. **Job** — `RebuildDemoCabinetJob` (Hangfire `rebuild-demo-cabinet`, `0 2 * * *` UTC, gated on
   `IssuesDemoSessions`, `DisableConcurrentExecution`) → `DemoCabinetRebuilder`.
2. **Check before switch** — `MoneyReconciliationService.RunAsync` over the demo DB after the build; non-clean →
   failure path.
3. **Switch + purge** — `Activate()` the new admin; then purge every demo cabinet older than the previous one, one
   per transaction, blobs after commit.
4. **Failure path** — purge the half-built cabinet the same way; log an error naming the step.
5. **Tests** — locator (newest active-with-patients); ordering; failure keeps current; purge keeps current +
   previous; transaction/blob order.

## Files to Create/Modify

### Files to Create

| File | Purpose |
|------|---------|
| `api/ClinicManagement.API/BackgroundJobs/RebuildDemoCabinetJob.cs` | nightly wrapper |
| `api/ClinicManagement.UnitTests/Api/DemoCabinetRebuilderTests.cs` | ordering + purge |

### Files to Modify

| File | Changes |
|------|---------|
| `DemoCabinetRebuilder.cs` | check, activate, purge, failure path |
| `Program.cs` | recurring job registration |

## Verification Steps

- [ ] API suite green, unfiltered
- [ ] Local: trigger the rebuild twice with a session open between — open session keeps working; new entry lands in the new cabinet; third run purges the first (SQL count of demo clinics = 2)
- [ ] Forced failing check (test seam) → current cabinet unchanged, error logged, half-built purged
- [ ] `verify-schema` → 0 after the runs (audit checks included)

## Exit Criteria

- [ ] All ACs checked; gates green; committed; README status → `implemented`
