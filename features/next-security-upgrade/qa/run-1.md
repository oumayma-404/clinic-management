# Run 1 — Next.js 16.3.8 smoke

**Date:** 2026-10-05 · **Branch:** `fix/next-security-upgrade` (from `feature/security-remediation` `22e03725`) · **Plan:** `plan.md` · **Driver:** `walk.mjs` (three drives; the third is reported)

**Change:** `next` ^16.3.1 → ^16.3.8 in `web/` and `console/`, plus `npm audit fix` (`sharp` 0.35.5, `browserslist`).
`npm audit --audit-level=high` → **0 vulnerabilities** in both (was 1 critical + 2 high in `web/`, 1 critical + 1
high in `console/`). No source file changed.

**Gate:** `web` — `check:responsive` 72/72, `tsc --noEmit` clean, `npm run build` clean. `console` — `tsc --noEmit`
clean, `next build` clean.

**Environment:** the production build served by `node .next/standalone/server.js` on `:3099`; isolated API on
`:5099` (the API is unchanged between this base and the one it was built from). Own headless Chrome. **Live peers:**
`quick-fix-notes`, `clinic-management-0b` — their stack untouched.

| ID | Result | Evidence |
|---|---|---|
| S-1 | ✅ | signed out, `/patients` → `/login` (the proxy) |
| S-2 | ✅ | signed in → `/`; `local_session` set |
| S-3 | ✅ | dashboard rendered « L’activité » — `shots/S-3.png` |
| S-4 | ✅ | agenda « Nouveau » dialog listed 44 acts — `shots/S-4.png` |
| S-5 | ✅ | patient page rendered; file manager painted 16/16 thumbnails — `shots/S-5.png` |
| S-6 | ✅ | `/bff/pdf/invoice/…` → 200, `application/pdf`, `%PDF-`, 48 415 bytes |
| S-7 | ✅ | no security headers from Next under `AUTH_MODE=local` — unchanged, and no duplicate CSP |
| S-8 | ✅ | signed out → a protected route redirects to `/login` again |
| S-9 | ⏭ | `console/` runtime not exercised (console account + TOTP + its own listener); `tsc` + `next build` are its gate |
| runtime | ✅ | no uncaught page error during the walk |

## Findings

None.

## Probe fixes made during the run (not product changes)

| What looked broken | Real cause |
|---|---|
| S-3 timed out | the heading is « L’activité » with U+2019, not `'` |
| S-7 « no CSP » | by design: `next.config.ts` emits none under `AUTH_MODE=local` (the API front door and Caddy own them) — the row's expectation was wrong and is corrected |
| S-8 `ERR_ABORTED` on its second drive | the app starts its own redirect after sign-out and aborts a racing `goto`; it passed on the first drive with unchanged code |

**Status: GREEN** (S-9 not exercised)
