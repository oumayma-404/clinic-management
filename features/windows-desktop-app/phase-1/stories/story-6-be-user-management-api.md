# Story 6 (BE): Admin user-management API

**Status:** implemented
**Layer:** BE
**Depends On:** 1

## Objective
Give an admin the API to list clinic users, reset a user's password (forcing a change at next login), deactivate/reactivate users, and let any user change their own password. Login must reject deactivated users and honor the forced-change flag.

_From spec:_ AC-3.4, AC-3.5, AC-5.1–AC-5.4, AC-4.5; FR-B5.

## Entry criteria
- Story 1 done (local auth, `IsActive`, `MustChangePassword` fields).

## Steps
1. `ListUsersQuery` (admin-only) → users in the clinic (name, email, role, status).
2. `ResetUserPasswordCommand` (admin-only) → set a temporary password (returned once for the admin to relay), set `MustChangePassword=true`.
3. `SetUserActiveCommand` (admin-only) → deactivate/reactivate; `LoginCommand` rejects inactive users.
4. `ChangePasswordCommand` → user sets a new password; clears `MustChangePassword`; add regenerate-clinic-code (admin) for AC-4.5.
5. Enforce admin-only via role check on these endpoints (first real use of the role).

## Files to create/modify
- New: `ListUsersQuery`, `ResetUserPasswordCommand`, `SetUserActiveCommand`, `ChangePasswordCommand` (+handlers).
- `api/.../API/Controllers/AuthController.cs` (or a `UsersController`) — admin-only endpoints + `change-password`.
- `LoginCommand` — enforce `IsActive`; surface `MustChangePassword` to the client.

## Verification steps
- Admin lists users; resets a password → temp password returned, target user forced to change at next login.
- Deactivated user cannot log in; reactivated can.
- Non-admin calling admin endpoints → 403.
- Records created by a deactivated user are retained.

## Exit criteria
- Admin can fully manage local accounts via the API; login honors active/forced-change state.
