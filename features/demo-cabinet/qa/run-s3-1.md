# QA run — story 3, cycle 1

**Plan:** `qa/plan-3.md` · **Driver:** `qa/walk-3.mjs`
**Environment:** isolated stack — API :5099 `PublicDemo` on a fresh `clinic_demo_local` (`Demo__SessionMinutes=2`),
web :3099 `next dev`; fresh Chrome contexts via `playwright-core`.

| ID | Result | Evidence |
|----|--------|----------|
| A1 | ✅ | one session with two visitors in, no « Déconnecter » (`shots/S3-A1-devices-1440.png`) |
| A2 | ✅ | « Activer maintenant » stays on `/securite`, demo toast |
| A3 | ✅ | `/change-password` shown, in-form notice, input kept, no request |
| A4 | ❌ → probe | notice shown, no code field, nothing sent; the check expected one « Fermer » and found two (dialog ✕ + footer) |
| A5 | ✅ | logo pick: toast, nothing sent |
| A6 | ✅ | cachet pick: toast, nothing sent |
| A7 | ✅ | CSV pick: notice in the dialog, nothing sent |
| A8 | ⏭ → finding | card counted before the page loaded (probe); source read shows the button is **disabled** on the demo |
| A9 | ✅ | dashboard toggle refused neutrally, switch restored |
| A10 | ⏭ | no conversation in an empty cabinet (story 4) |
| B1 | ❌ → probe | only unexpected write: `POST /bff/auth/local-logout`, a session reaching its 2-min cap |
| B2 | ✅ | no `pending-reviews` read |
| C1a | ❌ → probe | ambiguous « Fermer » locator (same cause as A4) |
| C1b | ✅ | password notice at 320 px, no h-scroll |
| D3 · D4 | ⏭ | unit-tested / need a patient (story 4) |

## Findings

1. **major — Mode discret cannot be pressed on the demo.** `money-discreet-card.tsx` disables « Masquer » when the
   account has no authenticator and shows « Activez l'authentificateur dans Sécurité » — an instruction the demo
   refuses. FR-4: « the switch is visible; using it is refused per US-3 ». Owner: `money-discreet-card.tsx:50`.

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| Mode discret disabled | confirmed | `noAuthenticator` ignores the demo | `money-discreet-card.tsx` | one card; unchanged off the demo |
| A4 / C1a | probe | two buttons named « Fermer » | `walk-3.mjs` | — |
| A8 | probe | counted before load | `walk-3.mjs` | — |
| B1 | probe | session-cap logout is the BFF side of leaving | `walk-3.mjs` (allowed list) | — |

**Status:** RED (1 product finding)
