# Story 7 (FE): Admin user-management UI

**Status:** APPROVED
**Layer:** FE
**Depends On:** 3, 6

## Objective
Give the admin a screen to manage local users (list, reset password, deactivate/reactivate, view/regenerate the clinic code), plus a forced-password-change screen after a reset.

_From spec:_ AC-5.1–AC-5.4, AC-3.6, AC-4.5 (UI).

## Entry criteria
- Story 6 done (user-management API).
- Story 3 done (local auth UI base).

## Steps
1. Add an admin-only **user-management page** under settings: user table (name, email, role, status) with reset-password and deactivate/reactivate actions (confirm dialogs).
2. Show the clinic code with a regenerate action.
3. Reset-password action shows the temporary password for the admin to relay.
4. Build the **force-password-change screen** shown when the logged-in user has `MustChangePassword`.
5. Hide the management page for non-admins.

## Files to create/modify
- New: user-management page + force-change-password screen.
- `web/components/dashboard-sidebar.tsx` / settings — nav entry (admin-only).
- Reuse existing table/dialog/form primitives.

## Verification steps
- Admin opens the page, resets a user's password → temp shown; that user is forced to change at next login.
- Admin deactivates a user → that user can no longer log in.
- Non-admin cannot see/reach the page.
- Regenerate code updates the code shown for self-registration.

## Exit criteria
- Admin manages users end-to-end through the UI; forced-change flow works.
