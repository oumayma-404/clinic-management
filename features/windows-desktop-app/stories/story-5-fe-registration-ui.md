# Story 5 (FE): Staff registration UI

**Status:** implemented
**Layer:** FE
**Depends On:** 3, 4

## Objective
Let staff register their own account through the UI (clinic code + credentials + role) and then log in.

_From spec:_ AC-4.1.

## Entry criteria
- Story 4 done (self-registration API).
- Story 3 done (local auth UI base: mode plumbing, login screen, session cookie).

## Steps
1. Extend the join wizard with email + password (+ confirm) fields and role selection (doctor/secretary), reusing existing shadcn/form + zod patterns and French labels.
2. Make registration reachable from the local login screen ("Create account with clinic code").
3. On success, route to login (or auto-login) and into the app.
4. Surface API errors (invalid code, duplicate email) as inline/toast messages.

## Files to create/modify
- `web/components/join-wizard.tsx` — credentials + role (Local mode).
- `web/app/join/page.tsx` and/or login screen — entry point to registration.

## Verification steps
- In Local mode: register with a valid code → account created → log in → dashboard.
- Invalid code / duplicate email → clear error shown.
- Cloud-mode onboarding unaffected.

## Exit criteria
- A new staff member can self-register and log in entirely through the UI, offline.
