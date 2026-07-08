# Story 8 (BE): Admin lockout-recovery utility

**Status:** done (review skipped by user)
**Layer:** BE
**Depends On:** 1

## Objective
Provide a server-side utility, runnable on the server PC, that resets the (sole) admin's password — the offline recovery path when no admin can log in and there is no email/cloud reset.

_From spec:_ FR-B6.

## Entry criteria
- Story 1 done (local auth + `LocalAuthService` hashing).

## Steps
1. Add a console/CLI entry in the API project (e.g. a `reset-admin-password` command/arg) that runs against the local database.
2. Reuse `LocalAuthService` to hash a new password and set `MustChangePassword=true` for the target admin (identified by email, or the sole admin if unambiguous).
3. Require it to run locally on the server PC (documented); print clear success/failure.
4. Document the recovery procedure.

## Files to create/modify
- New: CLI/console entry in `ClinicManagement.API` (or a small companion tool) using the app's DbContext + `LocalAuthService`.
- Short docs note in the feature folder / README.

## Verification steps
- Run the utility on the server PC → admin password reset; admin logs in and is forced to change it.
- Utility reports a clear error if the target admin isn't found.

## Exit criteria
- A locked-out admin can regain access via the server-side utility, with no internet.
