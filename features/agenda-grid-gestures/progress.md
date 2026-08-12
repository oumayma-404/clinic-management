# Progress: Agenda Grid Gestures

**Started:** 2026-08-12
**Type:** Small
**Branch:** feature/windows-desktop-app (user chose to stay on the current branch)

## Status
- [x] Implementation
- [x] Mechanical quality checks — `npm run check:responsive` **15/15**, `npx tsc --noEmit` **0 errors**,
      `npm run build` **clean**. (`npm run lint` is not the gate: `eslint` is named in the script but absent from
      `devDependencies`, and `next.config.ts` sets `eslint.ignoreDuringBuilds`.)
- [x] Eye pass — **320 · 390 · 820 · 1180 · 1440 px, plus a 844 × 390 landscape phone**, walked in Chrome
      against the running app. No body-level horizontal scroll at any width; no overlay over the grid; the
      footer sentence renders at all six. Captures committed under `screenshots/` (via the repo's own
      `npm run shots`, which reported « Aucun débordement horizontal »). Keyboard: the hour cells carry **0**
      `tabindex` attributes, so the 168-cell grid still does not sit between the toolbar and the appointments.
- [x] Tests — see **Test Plan** below. `web/` has **no test runner**, so the deliverable is a derived
      mechanical check plus per-AC coverage notes, not a test class.

## Test Plan

⚠️ **There is no frontend test runner in this repo, and one was not added.** `web/package.json` has no
`vitest` / `jest` / `@testing-library`, there is not one `*.test.ts(x)` or `*.spec.ts(x)` under `web/`, `lint`
names an `eslint` that is not in `devDependencies`, and `next.config.ts` sets `eslint.ignoreDuringBuilds`. The
one `playwright-core` dependency belongs to the `shots` **screenshot** harness, not to a test framework. The
only test project in the solution is `api/ClinicManagement.UnitTests`, and this feature is **FE-only** — it
touches no backend file, no DTO and no endpoint, so there is nothing there to test. `web/CLAUDE.md` and
`.claude/rules/frontend-web.md` § 14 both say to treat the missing runner as a fact to work with rather than a
gap to close mid-feature, so introducing one is deliberately **not** done here.

What the test pass delivers instead is the thing this feature genuinely lacked: an automated check for its one
**silent-failure** contract.

| AC | Action | Target | Notes |
|----|--------|--------|-------|
| AC-3, AC-5 (the DOM contract underneath both) | **New derived check** | `web/scripts/check-responsive.mjs` → **`agenda-gestures`** | The gestures resolve their target through `elementFromPoint` + `dataset`, so the attribute names are a contract between two files that `tsc` cannot see. Proven red four ways before being trusted. |

### Coverage notes — every other AC, and where it is actually covered

Nothing is dropped; each AC is either a row above or a note here.

- **AC-1, AC-2** (no overlay; the footer states the empty sentence and is absent while `loading`/`error`) —
  covered by the **eye pass at all six widths** (captures in `screenshots/`) and by the `emptySentence` /
  `overlayCard` DOM assertions run at each width. No unit surface: it is a render condition on a component in a
  repo with no component-test runner.
- **AC-3, AC-4** (dragged span → dialog pre-filled; acts must not overwrite the duration) — **exercised end to
  end in the browser**: the band read « 13:00 – 15:00 · 120 min », the dialog opened on 14/08/2026 · 13:00 · 2h,
  and picking a 30-minute act left it at 2h. Recorded in « Behaviour verified in the browser » above.
- **AC-5, AC-6, AC-7** (persisted move; the three confirmations; same-slot no-op) — **exercised in the browser**,
  including the one that is measurable rather than visual: a drop back onto the appointment's own slot issued
  **0** `PUT /appointments/*`, counted on the network layer. The past-time confirmation was driven through to a
  persisted move.
- **AC-8** (409 → the server's sentence + refetch) — **not reproduced.** It needs two clients racing the same
  appointment, which this environment cannot stage. It is a three-line branch on `err.status === 409` beside the
  two advisory codes that *were* exercised, and the « never shows an unsaved time » half is structural: nothing
  is optimistically moved, so the block only ever renders its stored time. Flagged for review rather than
  claimed.
- **AC-9** (cancelled/completed not draggable, still clickable) — a pure predicate over `normalizeStatus`, gating
  whether `onPointerDown` is attached at all. No runner to unit-test it in; verified by reading the diff.
- **AC-10** (touch long-press; scroll and the Jour day-swipe survive) — the **coarse-pointer path is not
  reachable from a desktop Chrome mouse driver**, so the long-press branch is unexercised. What was verified is
  the structural half: the day-swipe bails on `didConsumeGesture()`, and the hour cells still carry **0**
  `tabindex`. **A real finger on a real phone is still owed** — the same standing caveat `desktop/` and
  `mobile/android/` carry.
- **AC-11** (the non-gesture path untouched) — a plain click still opens the create dialog on the hour, and the
  block still opens its edit dialog; both were used repeatedly while driving the other ACs.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Mechanical device gate | `npm run check:responsive` | **16 of 16 passed** (15 pre-existing + the new `agenda-gestures`) |
| Type check | `npx tsc --noEmit` | 0 errors |
| Build | `npm run build` | clean (its one warning is the pre-existing Auth0/Edge-Runtime one) |
| Backend | — | **not run: this feature changes no backend file** |

### The new check was proven red before it was trusted
`.claude/rules/frontend-web.md` § 14: *« Then prove it fails … a too-tight check is noisy and you notice; a
too-loose one is silent and indistinguishable from passing. »* All four of its arms were driven red against
backed-up copies and the files restored byte-identically afterwards (`diff -q` clean, `tsc` still 0):

| Injected defect | Reported |
|---|---|
| `data-agenda-day` renamed on the emitting side | `components/appointment-calendar.tsx  data-agenda-day` |
| `data-time-slot` dropped from the helper while still queried | `components/appointment-calendar.tsx  data-time-slot` |
| One grid branch starts a gesture but is not labelled | `2 gesture cell(s), 1 labelled` |
| The hook file removed | `components/agenda-grid-drag.ts  missing` |

⚠️ **The required attribute list is derived from the hook's own `dataset` reads, not hand-maintained** — a new
`dataset.x` in `agenda-grid-drag.ts` is covered the day it is written, which is the failure mode a listed
expectation hides (`card-fallback` and `RealtimeResourceResolverTests` are the repo's precedents). And the
gesture-cell count is checked **against itself** rather than against a literal `2`, so a third grid branch that
forgets the props fails instead of passing.

## Behaviour verified in the browser (not just compiled)
| Checked | Result |
|---|---|
| Drag 13:00 → 15:00 in a day column | Dashed band reads « 13:00 – 15:00 · 120 min »; the dialog opens on **14/08/2026 · 13:00 · 2h**. |
| Then pick « Détartrage » (a 30-min act) | Duration **stays 2h** — AC-4. Without `defaultDurationMinutes` it would have dropped to 30m. |
| Drag a block MER 10:00 → VEN 15:00 | Ghost follows the pointer; toast « Rendez-vous déplacé · vendredi 14 août à 15:00 »; the day counts update; the 60-min height is unchanged. |
| Drag a block away and back onto its own slot | **0** `PUT /appointments/*` — AC-7. |
| Drop onto a past slot | « Heure dans le passé » confirm; « Déplacer quand même » persists it. |
| Native text selection during a mouse drag | None. See the finding below. |

⚠️ **One defect was found by the walk and fixed, and it would not have shown up any other way.** A mouse drag
across the grid was anchoring a **native text selection** — a blue smear over the hour labels, the blocks and the
footer beneath them. `select-none` gated on the drag being *armed* does not fix it: the browser establishes the
selection anchor on `mousedown`, before any movement has told us this is a drag rather than a click, so the class
lands after the smear exists. It is now unconditional on the scroll container, with the reasoning recorded there.

## Dev-environment note
`npm run build` writes into the same `web/.next` a running `next dev` owns, so building while the dev server was
up left it answering **500** until it was restarted on a clean `.next`. The dev server was restarted afterwards
and is running. The appointment used for the move test (« oumayma benkhalifa », MER 12/08 10:00) was **moved back
to its original slot** — the dev data is as it was found.

The build's one warning is pre-existing and not from this change: `@auth0/nextjs-auth0` loads Node's `crypto` in
the Edge Runtime, traced through `lib/auth0.ts`, which this feature does not touch.

## Not mine — arrived in the working tree mid-session
`.gitignore`, `web/package.json`, `web/package-lock.json` and `web/scripts/shots.mjs` are the **`shots`
screenshot harness** somebody added while this feature was being written (it pulls in `playwright-core`). They
are not part of this change and must not be staged with it. This feature's own `screenshots/` folder *was*
produced with that tool, which is what its `.gitignore` note asks for.

## Acceptance criteria → where they are met
| AC | Where |
|---|---|
| AC-1 | The `inset-0` overlay is deleted; nothing covers the grid. |
| AC-2 | The footer strip now renders on `isTrimmed \|\| showFullDay \|\| (!loading && !error && emptyRange)`. |
| AC-3 | `useAgendaGridDrag` → `onCreateSpan`; a span inside one 15-min unit falls through to `onCellClick`. |
| AC-4 | `CreateAppointmentDialog.defaultDurationMinutes` seeds `durationTouched: true`. |
| AC-5 | `submitMove` sends `appointmentDateTime` + `version` **only** — no `durationMinutes`, no `procedures`, no `status`. |
| AC-6 | One `AlertDialog`; `moveGrantsRef` merges the three grants so a re-send never un-grants the last. |
| AC-7 | `handleMoveDrop` compares against the stored start floored to the minute and returns before any request. |
| AC-8 | `err.status === 409` → the server's sentence via `showErrorToast` + `refetch()`. Nothing is optimistically moved, so the block never shows an unsaved time. |
| AC-9 | `normalizeStatus` ≠ `Cancelled`/`Completed` gates `onPointerDown`; `onClick` is untouched. |
| AC-10 | `AGENDA_LONG_PRESS_MS` + `LONG_PRESS_SLOP_PX`; the day-swipe bails on `didConsumeGesture()`. |
| AC-11 | Cells still call `onTimeSlotClick(day, "HH:00")` on a plain press; blocks still open the edit dialog. |

## Working tree note (start of session)
29 files were already dirty at the start of this session, all of them the **vendor-whatsapp-messaging-quota**
backend work (`api/**` — messaging allowance, WhatsApp templates, Meta webhook) plus
`features/hosted-security-hardening/` and `features/landing-website/agent-prompt.md`. None of them overlap this
FE-only feature. They are excluded from this feature's commits; files are staged explicitly by path.

## Files Changed
| File | What |
|---|---|
| `web/components/agenda-grid-drag.ts` | **New.** `useAgendaGridDrag` — the pointer state machine, the 350 ms long-press gate, 15-minute snapping and cell hit-testing. |
| `web/components/appointment-calendar.tsx` | Empty-state overlay removed → footer strip; both gestures wired; the move's persistence + its three confirmations. |
| `web/components/create-appointment-dialog.tsx` | `defaultDurationMinutes` prop — a dragged span is an explicit statement about length, so the acts' sum no longer overwrites it. |
| `web/app/appointments/page.tsx` | Threads the dragged duration from the calendar into the dialog. |
| `web/scripts/check-responsive.mjs` | **Test pass.** The new derived **`agenda-gestures`** check — the gate is now 16. |
| `web/components/CLAUDE.md` | **Test pass.** Documents `agenda-grid-drag.ts`, the grid's new empty state and `defaultDurationMinutes`, per the root guide's « update the nearest `CLAUDE.md` ». ⚠️ `web/CLAUDE.md` was **deliberately not touched** — it is dirty with somebody else's in-flight vendor-messaging work. |

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| The hour cells lost their `onClick`; the gesture hook now decides press-versus-drag on release and calls back. | Internal to the same element, same behaviour for a plain click. Keeping both would have fired the click *and* the span on one gesture. The cells are deliberately not tab stops (168 of them in Semaine), so no keyboard path was affected. |
| The appointment block **kept** its `onClick`, guarded by `didConsumeGesture()`, instead of the hook claiming the tap. | The phone block is a real `<button>`, so Enter/Space produce a `click` and no pointer events — claiming the tap would have left the keyboard route working only by accident. |
| `AGENDA_NO_DRAG_ATTR` on the statut popover and the sync-controls row inside a block. | Those already `stopPropagation` on **click**, which does nothing for `pointerdown`; without it, a long press on the statut trigger would start carrying the appointment. Same file, no API change. |
| Past-time is a **client-side** pre-check with our own French sentence, not the server's. | No backend refuses a past time — both booking dialogs already ask it client-side, so this is a third caller of the same question. AC-6 asks for « the past-time acknowledgement » and names no code, unlike the two it does name. |
| `openCreateDialog` on the page clears the held duration for the bar button and the FAB. | The page keeps one long-lived dialog, so a duration left over from the last drag would silently apply to the next click-booked appointment. |

## Significant Deviations
### DEV-1 — the gesture engine is a sibling module, not inline in `appointment-calendar.tsx`
- **Spec said:** « Three changes to `web/components/appointment-calendar.tsx` ».
- **Implemented:** the pointer state machine, long-press gate, snapping and hit-testing live in a new
  `web/components/agenda-grid-drag.ts`; the calendar keeps the rendering and the wiring.
- **Justification:** the calendar is already 2651 lines and the engine is ~350 more. The repo's own convention is
  to colocate such a hook beside its component (`record/use-session-acts.ts`,
  `patients/files/use-file-preview.ts`), and flat non-component modules already sit next to it
  (`appointment-labels.ts`, `procedure-categories.ts`).
- **Impact:** none on behaviour. The spec's file list describes the feature surface, not the module layout.
- **Approved:** Y (asked, user chose "Extract a sibling hook module").
