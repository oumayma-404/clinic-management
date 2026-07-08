# Story 3 (FE): Local login + first-run setup UI

**Status:** implemented
**Layer:** FE
**Depends On:** 1, 2

## Objective
Let a user, from a client machine, complete first-run setup (where applicable) and **log in offline** through the UI, reaching the dashboard — using a cookie-backed session that reuses the existing Bearer seam.

_From spec:_ AC-1.2 (UI), AC-3.1/AC-3.2 (UI side), AC-3.5, AC-3.6.

## Entry criteria
- Stories 1 & 2 done (login + first-run APIs exist).
- Next.js server can read `AUTH_MODE`.

## Steps
1. Mode plumbing: Next server reads `AUTH_MODE`; browser fetches `GET /api/auth/mode` at bootstrap (small provider/hook) to render the right auth UI.
2. Local-login route handler: posts creds to `POST /api/auth/login`, sets the JWT in an **HttpOnly session cookie**.
3. `app/api/auth/token/route.ts` (Local mode): read that cookie, return the JWT to `getAccessToken()`.
4. `middleware.ts` (Local mode): gate protected routes on the cookie; redirect to the local login screen; skip Auth0 `/auth/*` mounting.
5. `app/layout.tsx`: mount `Auth0Provider` only in Cloud mode; a lightweight local auth context in Local mode.
6. Build the **local login screen**; add password fields to the **setup wizard**; add inactivity auto-logout (default 30 min) and logout that preserves the configured server address.

## Files to create/modify
- New: local-login Next.js route handler; login screen; mode provider/hook.
- `web/middleware.ts`, `web/app/api/auth/token/route.ts`, `web/lib/api/client.ts` (`getAccessToken`), `web/app/layout.tsx` — mode branching.
- `web/components/setup-wizard.tsx` — email/password fields (Local mode).

## Verification steps
- `AUTH_MODE=Local`: fresh install → run setup (localhost) → log in from a client browser → dashboard loads; API calls carry the Bearer token.
- Closing/reopening the app keeps the session; inactivity → auto-logout; logout keeps server address.
- `AUTH_MODE=Cloud`: Auth0 login flow unchanged.

## Exit criteria
- End-to-end offline login works from a client through the UI, with Cloud mode UI unaffected.
