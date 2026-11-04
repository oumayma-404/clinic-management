# Story 6 Review Report

**Story:** 6 - The cabinet renews itself every night without interrupting anyone (BE)
**Review Date:** 2026-11-03
**Reviewer:** Claude

## Implementation Quality Score: 100/100

### Spec Coherence: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| User stories implemented | 15/15 | FR-7: built aside, checked, opened, old cabinets removed once nobody is inside |
| Acceptance criteria met | 15/15 | AC-7.5 (nightly 03:00 Tunis, dated to the new day), AC-7.6 (money + schema checks before opening; failure keeps yesterday's and shows as a stale `demoBuiltOn`) |
| Functional requirements | 10/10 | « La démo se met à jour » only with no cabinet at all (locator unchanged); visitors never meet a half-built cabinet — now true from the first write (review fix) |
| Edge cases handled | 5/5 | EC-2 visitor at 03:00 (real run: 14/14 reads during the build); EC-7 failure (5 failure points unit-tested); two rebuilds within 2 h; a demo profile on a real database (guard) |
| No scope creep | 5/5 | DEV-8 (`verify-schema` admin-factor scope) is a masking defect the real run exposed |

### Code Quality: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| Follows codebase patterns | 10/10 | purge = `DeleteClinicFromConsoleCommand`'s path; recurring job registered/removed by capability like its siblings; every scope declares itself |
| No dead code | 5/5 | — |
| No duplication (DRY) | 10/10 | `LocalClinicProvisioning` reused with one optional flag rather than a demo copy |
| Clean solutions (no hacks) | 10/10 | order lives in one testable function; no production switch to force a failure |
| Unit tests | 8/8 | `DemoCabinetRebuildTests` (10), `DemoCabinetRetirementTests` (8), `AdminFactorCheckScopeTests` (6), `LocalClinicProvisioningTests` +2, `SelfRegistrationGateTests` +3 |
| Integration tests | 7/7 | N/A as a project; 4 real rebuilds + 1 after the review, `verify-schema`, `reconcile-money`, Hangfire registration read |

## Test Traceability Matrix

| AC / FR | Description | Unit Test | Real stack | Status |
|---|---|---|---|---|
| AC-7.5 | rebuilt nightly, dated to the new day | — (cron registration) | `recurring-job:rebuild-demo-cabinet` = `0 2 * * *` UTC; builder takes `ClinicClock.ClinicToday()` | ✓ |
| AC-7.6 | checks pass before the switch, else yesterday's stays and it is visible | `A_Failed_Check_Keeps_Yesterdays_Cabinet`, `Money_Drift_Fails_The_Check`, `Only_Schema_Drift_The_Build_Added_Fails_The_Check`, `GetMode_reports_the_day_the_demo_cabinet_was_built` | `verify-schema` key-ring only; `reconcile-money` 0 | ✓ |
| FR-7 | built aside, switched only when complete | `A_Rebuild_Opens_Only_After_The_Checks_And_Retires_Last`, `The_admin_starts_inactive_only_when_asked` | admin observed off during the build | ✓ |
| FR-7 | old cabinet removed once no session can use it | `An_Empty_Old_Cabinet_Is_Retired…`, `A_Cabinet_Someone_Is_Still_Inside_Is_Kept`, purge order tests | run 3: B purged (rows + 7 files), A kept | ✓ |
| EC-2 | visitor present at 03:00 keeps walking | `A_Cabinet_Someone_Is_Still_Inside_Is_Kept` | 14/14 reads during the build, 200 after | ✓ |
| EC-7 | failed rebuild keeps yesterday's | `A_Failed_Build…`, `A_Failed_Provision_Is_Cleaned_Up_Too`, `A_Failed_Cleanup_Does_Not_Hide…`, `A_Failed_Retirement…` | — (no production failure switch, DEV-9) | ✓ |

**Coverage:** 6/6 (100 %)

## Auto-Approved Deviations

| Deviation | Reason | User Feedback |
|-----------|--------|---------------|
| No `RebuildDemoCabinetJob.cs` | the wrapper would only forward one call | Accepted |
| `demoBuiltOn` held in memory | keeps the anonymous probe free of a DB read | Accepted |
| `demoBuiltOn` not in web `AuthModeDto` | no screen reads it | Accepted |

## Significant Deviations

| ID | Title | Justified | Approved | Score Impact |
|----|-------|-----------|----------|--------------|
| DEV-7 | Retire when nobody is inside, not « older than previous » | Yes — spec wording; survives two rebuilds within 2 h | Decided (reversible) | 0 |
| DEV-8 | `verify-schema` admin-factor check scoped to deployments that require a factor | Yes — always red on the demo, false positive on LAN | Decided (reversible) | 0 |
| DEV-9 | No forced-failure switch in production | Yes — failure path unit-tested | Decided | 0 |

## Scope Creep Review

No scope creep detected.

## Findings Summary

| Category | Count |
|----------|-------|
| Critical | 0 |
| Major | 1 |
| Minor | 2 |
| Suggestions | 0 |
| **Total** | 3 |

## Fixed Issues

1. **Major — an empty new cabinet was enterable for a moment.** Provisioning saved the new dentist **active** and the rebuild switched her off in a second save; a visitor entering in between landed in an empty cabinet for 2 h. `LocalClinicRequest.AdminStartsInactive` (default false, the other three callers unchanged) creates her off in the same save; observed off during a real build.
2. **Minor — a provisioning failure part-way left its rows behind.** The cabinet id is now known before anything is written (`IDemoRebuildSteps.ClinicId`), and any failure from provisioning onward removes it (purging a cabinet that was never written is a no-op).
3. **Minor — one test asserted the same case twice.** `A_Failed_Cleanup_Does_Not_Hide_The_Original_Failure` trimmed to the case it names; a baseline-failure case added.

## Skipped Issues

None.

## Learnings & Observations

- « Switched off last » is only true if it is also switched off **first** — the two-save shape opened a window the locator could see.
- A check that runs inside a job inherits the job's deployment: `verify-schema` carried a check that was red by design on the demo.
- Build baseline: 8 × NU1903 (pre-existing), no warning in a story file.

## Quality Check Results

| Check | Result |
|-------|--------|
| Linting | N/A (none for api/) |
| Type checking | Pass (0 errors, no story warnings) |
| Unit tests | Pass — 6 194 passed, 0 failed, unfiltered |
| Integration tests | N/A (no project); real rebuild after the review fixes: opened, 0 ERR, `verify-schema` key-ring only, `reconcile-money` 0 |
| E2E tests | N/A (backend-only) |
| Device check (mechanical) | N/A (backend-only story) |
| Device review (static, 12a) | N/A (backend-only story) |
| Widths verified in browser | N/A (no rendering change) |
