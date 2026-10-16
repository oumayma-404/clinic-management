# Closing a dialog reloaded the page — fixed 2026-10-15

## What was wrong

- Next.js stores `__NA` + its route tree in each history entry's state, and **reloads the whole document** on a
  Back (popstate) onto an entry without `__NA`.
- 25 call sites wrote history by hand with `{}` / `null` / a bare marker as the state, erasing it:
  deep-link cleanup (`?newAppointment=1`, `?editPatient=1`, …), `useUrlFilters`, and the two « Back closes me »
  guards (`useDirtyGuard`, the plan workspace's edits guard).
- `useDirtyGuard` closes a dialog with `history.back()`. After a **full load** of the agenda (its filters rewrite
  the URL on mount) the page entry had lost `__NA`, so every dialog close reloaded the screen. Two nested guarded
  dialogs reloaded even after a client-side move: the inner pop landed on the outer marker, which never had it.

## Fix

- `web/lib/history.ts`: `replaceUrl(url)` keeps the current state; `pushMarker(marker)` carries Next's own `__`
  keys and never another guard's marker (each guard reads its own key to know where it is).
- Every hand-written write now goes through it. `check:responsive` **N53** fails on a raw
  `pushState` / `replaceState` anywhere else, and on a helper that stops keeping the state (proved red both ways).

## Blast radius

| # | Touching | Other consumers | Verdict |
|---|---|---|---|
| 1 | 25 raw history writes | — | must change — all 25 moved |
| 2 | `useDirtyGuard` marker | 15 guarded dialogs | must re-test — A1, A3, A4, A5, N1, P1, P2 |
| 3 | plan workspace edits guard | its `history.go(-2)` leave path | must re-test — not driven (needs pending devis edits); same helper as N1 |
| 4 | `useUrlFilters` | 5 screens | must re-test — U1 (agenda) |
| 5 | token fragment cleanup (reset password, signup verify) | — | unaffected — token still removed from the URL; only the state is kept |

## Verification (demo stack, `:3099` → `:5099`)

`scratchpad/verify-dialog-reload.mjs`, one launch, 9 scenarios:

| Run | Result |
|---|---|
| Unfixed build (baseline) | A1, A3, N1, U1 **red** — the bug reproduces |
| Fixed build | **9/9 green** |

- A1 full load → open/close booking: same document · A2 client-side move: same · A3 Back closes the dialog, stays
- A4 `?newAppointment=1` consumed and cleaned · A5 dirty → « Abandonner » → no reload
- N1 edit RDV → « Facturer cette consultation » → close inner, outer survives, close outer · P1/P2 patient file
- U1 the agenda's entry keeps `__NA` after its filter write

Gate: `check:responsive` (80) · `tsc --noEmit` · `npm run build` — green.
