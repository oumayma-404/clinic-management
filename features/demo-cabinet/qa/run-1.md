# QA run 1 — demo-cabinet, story 1

**Date:** 2026-11-03 · **Plan:** [plan.md](./plan.md) · **Driver:** [walk.mjs](./walk.mjs) (one launch)
**Environment:** isolated stack — API `:5099` (`PublicDemo`, `clinic_demo_local`, `Demo__SessionMinutes=2`, built
from the worktree), web `:3099` (`next dev`, started this session). Own Chrome via `playwright-core` 1.48.2.
Peers live (ListAgents): clinic-management-77, clinic-management-7f (busy, unrelated), anakin-*, career-ops-* —
none on these ports or this database.
**Widths looked at:** 320×640 · 390×844 · 820×1180 · 1180×820 · 1440×900 · 1536×730 (PNG read: 320, 1440 dashboard).

| ID | Result | Evidence |
|---|---|---|
| A1 | ❌ | button + link OK (`https://app.apexa.tn/signup`, `_blank`); **no heading role** for « Cabinet de démonstration » |
| A2 | ✅ | entered → `/`, « Mon compte » present, no password field; dashboard « Cabinet Dentaire Démo » / Monica Geller (`shots/A2-dashboard-1440.png`) |
| A3 | ✅ | two contexts in at once; first survives the second's entry |
| A4 | ✅ (sql, pre-launch) | 3 entries → 3 distinct untrusted families « Visite de démonstration »; `LastLoginAt` NULL; expiry = +2 h |
| B1 | ✅ | signed-in visitor on `/demo` → `/` |
| B2 | ✅ | `/login`, `/signup`, `/mot-de-passe-oublie`, `/join` → `/demo` |
| B3 | ✅ | « Se déconnecter » → `/demo`, no expiry line |
| B4 | ❌ | after the 2-min cap, reload → `/demo` **without** `?expired=1` and without the line |
| B5 | ✅ (unit) | `DemoSession_404s_anywhere_but_the_public_demo` |
| C1 | ✅ | no side scroll at any width; actions above the fold at 1536×730 (509/730) |
| C2 | ✅ | coarse 320: button 44 px |
| C3 | ✅ | Tab → button, Enter → `/` |
| C4 | ✅ | title « APEXA Démo », description present |
| D1 | ⏭ | no `HostedMultiTenant` API on this stack — covered by unit tests on the unchanged mode |

## Findings

### F1 — B4 · major · the expiry message never shows
- **Repro:** enter the demo; wait past the session cap; reload.
- **Expected:** `/demo?expired=1` with « La démo a expiré. Entrez à nouveau pour continuer. » (spec FR-3, EC-1).
- **Actual:** plain `/demo`. The session cookie's lifetime IS the session's (`writeSessionCookies` uses
  `refreshExpiresAt`), so after the cap the browser has dropped it; the middleware sees no session and bounces to
  `/login?returnTo=/`, which `DemoProvider` turns into a plain `/demo` — indistinguishable from a first visit.
- **Owner:** `web/lib/demo/demo-context.tsx` (the `/login` → `/demo` redirect) + `web/app/bff/auth/demo-session/route.ts`.

### F2 — A1 · minor · the entry screen has no heading
- **Repro:** `/demo`, accessibility tree.
- **Expected:** « Cabinet de démonstration » announced as the page heading.
- **Actual:** `CardTitle` renders a `<div>`; no `h1` on the page.
- **Owner:** `web/app/demo/page.tsx` (title element).

## Observations (off-plan)
- The dashboard of the minimal cabinet reads « Agenda au repos » with all-zero chips — expected until story 4 fills it.

**Status: RED (2 findings)**

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---|---|---|---|---|
| F1 | confirmed | the session cookie's lifetime is the session's, so after the cap nothing distinguished an expired visitor from a new one | `app/bff/auth/demo-session/route.ts`, `lib/demo/demo-cookie.ts` (new), `lib/demo/demo-context.tsx`, `lib/auth/session.tsx` | new non-secret `demo_visit` cookie (24 h), read only by the demo provider; cleared on a deliberate logout; no other consumer |
| F2 | confirmed | `CardTitle` renders a `<div>` | `app/demo/page.tsx` | page-local; `CardTitle` itself untouched (shared primitive) |

**Gates after the last edit:** check:responsive 81/81 ✅ · tsc ✅ · build — run after stopping `next dev` (see progress.md)
**Re-run:** run-2.md — GREEN (21 ✅, 1 ⏭ D1)
