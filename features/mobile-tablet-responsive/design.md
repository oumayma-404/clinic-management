# Design — Refonte mobile, page par page

**Status:** APPROVED
**Approved:** 2026-07-31 (user, after reviewing the mockups)
**Mockups:** [`mockups/mobile-redesign.html`](./mockups/mobile-redesign.html) ·
published artifact: https://claude.ai/code/artifact/7a14631f-f092-4617-a92d-a678db3d8083
**Scope:** every route **except `/appointments`** — the agenda is handled separately
(`features/agenda-phone-ux/`).

## Why this exists

The story's cross-cutting criteria — **AC-47** (usable at 320 px), **AC-48** (no capability removed by a
layout decision) and **AC-51** (the documented manual walk) — were never verified on a device. The walk was
the only gate that could have caught what the user then hit in real use: inputs overflowing their container,
an odontogram that takes four gestures to read, actions that fall off the right edge. This design pass is
that walk, done as a redesign.

⚠️ **Exploration was source-based, not browser-based.** This repo has no `agent-browser`, no dev-server
scripts and no screenshots, so per the skill's documented fallback the design system was extracted by reading
`web/app/globals.css` and the `components/ui/` primitives. The mockups therefore carry the app's **real**
token values rather than an approximation.

## Decisions taken (interview)

| # | Decision | Chosen | Consequence |
|---|---|---|---|
| 1 | What "keep the web experience" means | **Same capability, phone-native layout** | Nothing is cut. Tables become cards, forms stack, dialogs become sheets — but every desktop action stays reachable. Rejected: literal desktop layout with pan/zoom. |
| 2 | Sequencing | **Straight page-by-page, nav order** | No shared foundations mockup. The five rules below are re-applied per page rather than centralised — more surface, but it is what was asked for, so they are written down here to stop them drifting. |
| 3 | Patient-summary odontogram | **Treated teeth as chips, chart on demand** | The summary leads with the answer; the full chart is one tap away. |

## The five rules, applied to every page

Numbered because implementation order within a page follows them — the overflow rule first, since it is the
one that makes a page *wrong* rather than merely dense.

1. **Nothing pushes the page.** Every `flex`/`grid` child that can hold long text gets `min-w-0`; the value
   itself gets `overflow-wrap: anywhere`. ⚠️ This is the direct cause of the reported overflow: a flex child
   defaults to `min-width: auto` and therefore refuses to shrink below its content. And because
   `AppShell`'s `<main>` carries `overflow-y-auto`, CSS computes its `overflow-x` to `auto` too — so an
   over-wide child *scrolls the content area sideways* instead of being clipped.
2. **One field per line.** Inputs are full-width and stacked below `sm:`. Two side-by-side fields at 320 px
   leave ~130 px each before padding.
3. **44 px on every target**, including card `⋯` buttons and filter chips.
4. **No action is lost.** Every desktop row action appears in the card's `⋯` menu. One deliberate exception:
   a page's single primary action may become a full-width button (salle d'attente's « Placer en RDV »).
5. **The answer first.** A dense read-only visualisation leads with its conclusion; the detail stays one tap
   away. This is the rule that settles the odontogram.

## Page queue (nav order)

| # | Route | Page | Status |
|---|---|---|---|
| 1 | `/` | Tableau de bord | designed |
| 2 | `/recurring-series` | RDV récurrents | designed |
| 3 | `/waiting-list` | Salle d'attente | designed |
| 4 | `/patients` | Patients | designed |
| 5 | `/patients/[id]` | Fiche patient | designed — carries all three reported defects |
| 6 | `/documents` | Documents | designed |
| 7–23 | see the artifact's queue view | Plans/Devis · Laboratoire · Factures · Caisse · Créances · Stock · Rappels · 3 catalogues · Utilisateurs · Paramètres · Mon profil · Fichiers patient · Auth (×4) | swept — see below |

## Implementation log

| Page | Commit | What actually changed |
|---|---|---|
| 1 · Tableau de bord | `da238ec` | Two KPI columns below `sm:` (was one — eight figures were eight screens); padding, value size and hero size step down with it; KPI label wraps instead of truncating. |
| 2 · RDV récurrents | `4096b58` | **Page needed nothing** — it already had full parity. Exposed that every `CardList` heading was `truncate`d across all 19 card surfaces; headings now wrap, subtitles `line-clamp-2`, field values wrap. |
| 3 · Salle d'attente | `87e282c` | New `CardList.primaryAction` slot; « Promouvoir » moves out of the header row (it and the `⋯` took ~150 px of a 288 px card) onto its own full-width row. |
| 4 · Patients | `e41e587` | **The reported overflow, found**: `ListToolbar` pinned the search box at an unprefixed `min-w-[190px]`. Now `sm:`-only; the search takes its own row below that. |
| 5 · Fiche patient | `d877ecd` | Odontogram leads with treated-teeth chips, schema behind one tap; 7-tab strip becomes one scrolling row below `sm:` instead of four rows of chrome. |
| 6–23 · sweep | `91e7d52` | A 44 px floor for data-entry controls in **one** CSS rule (56 controls across settings/reminders); doctors editor grid collapses; user-management role select full-width on the card side. |

### What the sweep deliberately did not do

Two files carry the last unprefixed `grid-cols-2` form grids — `document-editor-content.tsx` and
`factures/invoices-table.tsx` — and **both are held uncommitted by a parallel session**. They are left
untouched rather than edited under someone else's in-flight work. They are the only known remaining instances
of that class.

⚠️ **The gate could not go fully green, for a reason outside this work.** `tsc` reports errors in
`appointment-calendar.tsx` (205 uncommitted insertions) and `agenda-phone-header.tsx` (untracked) — the
agenda, which the user is redesigning separately, mid-refactor. Every file touched here typechecks clean and
`npm run check:responsive` passes all ten checks, but `npm run build` cannot succeed until the agenda work
lands.

## Defects the design fixes (verified in source, not inferred)

| Defect | Where | Cause |
|---|---|---|
| Odontogram needs ~4 gestures to read | `patient-summary-modal.tsx:191-205` | Stacks **two** full charts (adult + child); each shows one arch with a Haut/Bas toggle below `md:`. FDI numbering already distinguishes the two dentitions, so two charts were never needed. |
| Patient tabs push content below the fold | `patients/[id]/page.tsx:1071` | 7 tabs in `grid-cols-2` = 4 rows of tabs on every open. |
| Search field pushes the page sideways | `/patients` header row | Flex child without `min-w-0`. |
| Long patient names widen the card | patient cards | No `overflow-wrap`. |
| Row actions fall off the right edge | `/recurring-series` and others | Three inline row actions with no card equivalent. |

**Not a defect, checked and cleared:** fixed pixel widths (the P4 `popover.tsx` clamp already bounds the
`w-[384px]` cases), and non-collapsing grids (6 in total, 5 of them agenda, 1 a 5-swatch colour picker that
fits 320 px).
