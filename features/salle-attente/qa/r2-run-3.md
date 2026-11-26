# QA run 3 — salle d'attente round 2 (last cycle)

Plan: `qa/r2-plan.md` · driver: `qa/r2-walk.mjs` (same file + the stronger C3 and A14 checks from cycle 2).
Environment: API `:5098` unchanged (no backend fix in cycles 1–2); web `:3098` rebuilt after the cycle-2 fixes.
Two drives of the same file: `e9ds` (one probe bug in the new C3 check — an inline span reports `clientWidth 0`),
then `gf4w` after fixing that probe. No source changed between the two.

## Fixes applied

| Finding | Verdict | Root cause | Files | Blast radius |
|---|---|---|---|---|
| F1 (run 1) | confirmed | the call-in notice — where the desk answers the ring, always on top — had no « Pas là », and the banner's own buttons were pushed under the notice by `flex-1` | `staff-chat-notices.tsx`, `call-in-banner.tsx`, `lib/waiting-room.ts` (`notHereSentence`, one sentence for 4 doors) | notices: gated on `isCallIn`, other kinds unchanged |
| F2 (run 2) | confirmed | `computeOverlapLanes` measured the booked length while the block draws the live one | `appointment-calendar.tsx` | both lane call sites (day + week) go through the one packer |
| F3 (run 2) | **partly fixed** | the detail no longer truncates, but at 320 the text column is 44 px and `break-words` cannot shrink a grid track below its longest word | `arrival-search.tsx` | — |

## Scenarios (drive `gf4w`)

44 ✅. Red:

| ID | Result | Evidence |
|---|---|---|
| C3 | ❌ product | at 320 the name/detail column is 44 px wide and holds a 62 px word: « Qagf4we » runs past its box toward « Recevoir » (DOM: `scrollWidth 62 > clientWidth 44`). The text itself now wraps and reads in full (`shots/r2-C3-other-day-row-320.png`) |
| A4 | ❌ probe (believed) | every A4 check passed; the throw is the **cleanup** click on the switch after the viewport went 390 → 1440. Green in runs 1–2 on the same code |
| A6b | ❌ cascade | Nadia stayed on « 2 fauteuils » after the A4 cleanup click failed |
| A7 | ❌ cascade | A6b's p22 was left waiting with « Urgent », so p6 became second |

Everything else — A1–A3, A5, A6, A8–A16, B1–B11, C1, C2, C4, C5, D1–D8, A14's new « side by side » check — ✅.

## Still failing, and what I would do next

- **C3 (minor, 320 px only):** let the row's pill wrap under the text below `sm:` (`flex-wrap` on the button, a
  `basis-32` text column) and use `[overflow-wrap:anywhere]` on the name and detail.
- **A4 cleanup / A6b / A7:** make the walk turn « 2 fauteuils » off through the API instead of a click after a
  resize, then re-drive. Not a product change.

## Status

RED (1 product finding, minor) — stopped at cycle 3 as the skill requires.
