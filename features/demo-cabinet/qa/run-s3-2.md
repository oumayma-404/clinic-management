# QA run — story 3, cycle 2 (full re-run)

**Plan:** `qa/plan-3.md` · **Drivers:** `walk-3.mjs` + `walk-2.mjs` (D2) + `walk.mjs` (D1), same stack as cycle 1.

| ID | Result | Evidence |
|----|--------|----------|
| A1 | ✅ | one session with two visitors in, no « Déconnecter » |
| A2 | ✅ | « Activer maintenant » refused in place |
| A3 | ✅ | `/change-password` shown, in-form notice, input kept, no request (`shots/S3-A3-password-320.png`) |
| A4 | ✅ | export → notice in place of the code field, nothing sent (`shots/S3-A4-stepup-390x844.png`) |
| A5 | ✅ | logo pick refused, nothing sent |
| A6 | ✅ | cachet pick refused, nothing sent |
| A7 | ✅ | CSV pick → notice in the dialog, nothing sent |
| A8 | ✅ | Mode discret « Masquer » → neutral toast, still « Masquer » (was disabled — fixed) |
| A9 | ✅ | dashboard toggle refused neutrally, switch restored |
| A10 | ⏭ | no conversation in an empty cabinet (story 4) |
| B1 | ✅ | 29 writes, only: demo-session · local-logout (session cap) · `clinics/money/hide` · `dashboard/preferences` (both refused, as intended) |
| B2 | ✅ | no `pending-reviews` read |
| C1 | ✅ | step-up notice at 390×844 and 1536×730; password notice at 320×640 — no h-scroll |
| D1 | ✅ | story-1 walk: all ✅, 1 ⏭ (no hosted API) |
| D2 | ✅ | story-2 walk: GREEN, 1 ⏭ (no hosted API) |
| D3 · D4 | ⏭ | unit-tested / need a patient (story 4) |

## Observations (off-plan)

- `/change-password` is a standalone page (no `AppShell`), so the demo band is absent there; the in-form notice says
  it is a demo. Its submit label fills the button edge to edge at 320 px — pre-existing.

**Status:** GREEN
