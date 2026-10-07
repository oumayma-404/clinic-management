# QA plan — Next.js 16.3.1 → 16.3.8 (security)

**Change under test:** `next` 16.3.1 → 16.3.8 in `web/` and `console/` (plus `sharp` 0.35.5 and `browserslist`
through `npm audit fix`). Closes GHSA-p293-qw3h-jr36 (RCE, Windows-hosted servers), GHSA-2xp9-vwfh-vxw4 (RCE,
image optimisation + AVIF), GHSA-vcvr-r3jv-pc5j (RCE, `next/og`), GHSA-rgj7-g3m4-5g8c (`sharp`/libheif). No source
line changed, so this is a smoke pass over what a framework patch can move: the proxy (middleware), the BFF route
handlers, the session cookies, rendering, and the standalone server production runs.

**Environment:** the PRODUCTION build served by `node .next/standalone/server.js` on `:3099` (built with
`NEXT_PUBLIC_API_URL=http://localhost:5099/api`), against an isolated API on `:5099` (the `performance-caching`
worktree's build — the API is byte-identical between this branch's base and that one, plus additive changes).
Own headless Chrome. The peer's `:3000`/`:5000` are untouched.

| ID | Scenario | Expected (observable) |
|---|---|---|
| S-1 | open `/patients` signed out | the proxy redirects to `/login` |
| S-2 | sign in (doctor, password only) | lands off `/login`; the session cookie is set |
| S-3 | `/` (dashboard) | the bilan renders (« L'activité »), no error boundary |
| S-4 | `/appointments`, open « Nouveau », open the acts picker | the dialog opens and lists acts |
| S-5 | a patient page, then its file manager | the page renders (« Odontogramme »); thumbnails paint |
| S-6 | `/bff/pdf/invoice/{id}/…` through the page | 200, `application/pdf`, body starts `%PDF-` |
| S-7 | response headers on a page | **none** from Next under `AUTH_MODE=local` — the API front door and Caddy own them, and a second CSP would make the browser enforce the intersection (`next.config.ts`). Corrected during run 1: the row first expected them |
| S-8 | sign out | back on `/login`; a protected route redirects again |
| S-9 | `console/` at runtime | ⏭ not exercised: needs a console account with TOTP and its own listener; `tsc` + `next build` are its gate |

**Widths:** 1440 only — no layout changed. **Mutations:** none.
