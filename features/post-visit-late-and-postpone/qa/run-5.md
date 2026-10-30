# QA run 5 — free times in « Reporter », after the owner asked again for the blast radius

**Date:** 2026-10-30 (11:25–11:40 Tunis)
**Cycle:** 1 of the stale-list follow-up (the re-check found: a listed time can pass on screen and the server accepts
a past time; a listed time taken after the read left the list stale)
**Driver:** walk.mjs, whole plan, plus F5 · F6 · F7 (reads edited in flight: a start 1–2 min ahead, R8's taken
09:00, every day `Unrestricted`)
**Environment:** isolated — API :5098 · web :3098 standalone rebuilt · postgres `clinic_pvl_qa` (owner's window open)
**Status:** RED (1 finding, from the eye pass — 58/58 scripted checks passed)

| ID | Outcome | Evidence |
|----|---------|----------|
| F5 | ✅ (script) | listed 11:29 went past while open → pressed: **no PUT**, list re-read, Reporter disabled |
| F6 | ✅ (script) | taken since the read → « Ce créneau est déjà réservé pour ce praticien (31/10 09:00–09:30). » + « Reporter quand même »; list re-read, 09:00 gone; not moved |
| F7 | ✅ | no hours saved → « Aucun horaire n'est enregistré pour ce cabinet. », calendar + typed time, Reporter usable · shots/run5-F7-no-hours.png |
| every other row | ✅ | as run 4 |

## Findings

### 1 · major — the answer to a press is off screen in the list mode
- **Where:** popup → Reporter → a **listed** time → press → refused (F6) or gone past (F5), at 1536×730.
- **Observed:** shots/run5-F5-time-passed.png and run5-F6-taken-since-read.png show no message at all: it was rendered
  after the time grid, inside the scrolling body, below the fold. The user sees a disabled « Reporter » or a
  « Reporter quand même » with no reason on screen.
- **Why the script passed:** `waitFor()` counts an element scrolled out of its container as visible — a DOM query is
  not an eye pass. The typed mode never showed it (no grid above the message).
- **Owner:** `web/components/appointment-postpone-dialog.tsx`.
- **Fix (after this run):** what a press answered (the refusal, « Cette heure est déjà passée. », the 409 banner) moves
  to a pinned strip between the scrolling body and the buttons; a listed time that went past is cleared and said so
  until another is picked. New checks `B1-seen` · `F2-seen` · `B7-seen` and F5/F6 now require the message to be the
  topmost element at its own centre, inside the viewport.
