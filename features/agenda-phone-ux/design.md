# Design: Agenda — vue téléphone (Google-Calendar-grade)

**Status:** APPROVED
**Created:** 2026-07-31
**Scope:** FE only
**Mockup:** [`mockups/01-agenda-phone.html`](mockups/01-agenda-phone.html) — open in a browser; four states side by side at 390 px.

## Why this exists (read first)

`mobile-tablet-responsive` **Phase 05 (Agenda) is already complete** — AC-28…AC-31, commits `028747b`,
`b775137`, `5d6bb5b`, `0690246`, with `agenda-scroll` enforced in `check:responsive`.

Those ACs are **correctness** goals: nothing clips, wide content scrolls in its own container, no appointment
block drifts, « Nouveau rendez-vous » stays reachable. None of them asks the result to be *pleasant*. This design
targets that separate goal, so it **extends** P5 rather than fixing it.

## Method note

The `/design-ui` skill's browser exploration (`agent-browser`, `scripts/start-dev.sh`) is tooling from the repo
that skill was written in and does not exist here. Per its own documented fallback, the design system was derived
by **reading source** instead: real tokens from `web/app/globals.css` (`--primary: oklch(0.49 0.105 188)`,
`--radius: 0.75rem`, full dark-mode set) are copied verbatim into the mockup, and the layout constraints come
from `appointment-calendar.tsx`. No live screenshots were taken.

## Decisions

| # | Decision | Rationale |
|---|---|---|
| D1 | **Jour stays the phone default**, with « Prochains RDV » above it | User's choice. Honours AC-28, so **no spec amendment** is needed. |
| D2 | « Prochains RDV » is a **summary, not a duplicate** — the next few, time + name + acts on one line | Mitigates the concern raised when this option was chosen: a full copy of the grid's contents shows every appointment twice, which reads as a bug. A *summary* answers « what's next » without competing with the grid. |
| D3 | The summary **collapses** (state C) and **vanishes when empty** (state D) | It must not permanently spend the smallest screen's vertical budget. Collapsed it costs one 44 px row; with nothing to summarise it costs nothing. |
| D4 | **Swipe is Jour-only** | AC-30 just made the week grid scroll horizontally. A horizontal swipe handler on the same axis in the same view would fight it — a real conflict, avoided by scoping the gesture rather than by dropping it. |
| D5 | Three navigation affordances coexist: 7-day strip, mini-month, swipe | User selected all three. They occupy different levels (week / month / adjacent-day) so they complement rather than duplicate. |
| D6 | Red **current-time line** with a time label | Google Calendar's signature orientation cue; the code already computes `nowPosition` for the scroll target, so the value exists. |
| D7 | **FAB** for « Nouveau rendez-vous », above the bottom bar | Satisfies AC-31 at every width, and is the pattern the user asked to mirror. Offset by `--bottom-inset` (P2's token). |
| D8 | Density **dots** in the strip and mini-month | Shows where appointments cluster without chips, which do not fit at 390/7 ≈ 55 px. Matches AC-28's « Mois shows dots ». |
| D9 | Header **compacts** from « juillet 2026 » to « ven. 31 juillet » once scrolled | The selected day matters more than the month when reading the grid. |
| D10 | Cancelled appointments render struck-through at reduced opacity | Existing convention (`showCancelled` toggle); keeps them visible without competing. |

## Hard constraints honoured

- **`HOUR_HEIGHT = 48` unchanged.** Rows are exactly 48 px; blocks are absolutely positioned from it and drift
  otherwise (a documented invariant — it was once a hardcoded `35`). Mockup blocks use the real arithmetic:
  09:00 → `top:48px`, 14:30 → `top:312px`.
- **Touch targets ≥ 44 px** on every day pill, tab, icon button and the summary header (P2's `coarse:` rule).
- **AC-29 untouched.** A dashboard drill-through with `?from=&to=` still forces Mois; the status chips it flips
  stay removable. This design changes nothing about that path.
- **AC-30 untouched.** No change to the week grid's scroll container or its sticky gutter.

## States in the mockup

| State | Shows |
|---|---|
| **A** Default | Month title + Aujourd'hui + 7-day strip; summary expanded (3); grid with 3 blocks and the 13:20 line; FAB |
| **B** Mini-month | Dropped-down month grid with density dots, selected day filled; grid dimmed behind |
| **C** Scrolled | Summary collapsed to one row; header compacted to the day; a cancelled RDV struck through; swipe hint |
| **D** Empty | Summary absent entirely; empty message with a direct create action; grid still tappable |

## Resolved before implementation — D11

**The mockup's bottom tab bar is dropped.** Verified: `components/bottom-nav.tsx` exists and is rendered by
`components/app-shell.tsx` — it is the app's **global** bottom navigation. A second bottom bar on the agenda
would put two of them on one screen and regress P2.

**Therefore:** Jour / Semaine / Mois switching lives in the **header, as a segmented control**, and the global
bottom nav is left untouched. The FAB keeps its `--bottom-inset` offset so it clears that global bar. Everything
else in the mockup stands as drawn.

## Not designed here

- Tablet and desktop (unchanged — this is the `< md:` surface only).
- A **Schedule/agenda list** view, which was the recommended option and was not chosen. If the Jour grid still
  feels empty in use, that is the next thing to try, and it would supersede AC-28.
- Drag-to-reschedule, pinch-to-zoom the hour scale, multi-doctor columns on phone.
