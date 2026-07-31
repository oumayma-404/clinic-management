# Progress — Mobile & tablet

**Story:** [story-1-mobile-tablet-responsive.md](./story-1-mobile-tablet-responsive.md) ·
**Plan:** [../plan.md](../plan.md) · **Spec:** [../spec.md](../spec.md)

Resume state for the one story. A **part boundary is the commit point and the resume point** — record what landed
before stopping.

**Branch:** `feature/audit-sections-3-to-10` (the user asked for the work to land in place, not on a new branch).

**Working tree note (start of session):** `features/landing-website/` was untracked and unrelated to this story.
It was left alone and excluded from every commit; staging was by explicit path throughout.

**Working tree note (P3-final session, 2026-07-31):** three more unrelated changes appeared in the tree *during*
the session — `api/…/Services/LiaisonContent.cs`, `api/…/Services/PdfGenerationService.cs` (both modified) and
`features/liaison-norms-and-document-email/` (untracked). None was touched or staged. ⚠️ This is why staging is by
explicit path and never `git add -A`: the tree is not guaranteed to be quiet for the length of a part.

## Part status

| Part | Covers | Status | Commit | Notes |
|---|---|---|---|---|
| **P0** | Mechanical-check script | ✅ **complete** | `920571a` | 8 checks; 4 still PENDING for later parts |
| **P1** | Foundations + `AppShell` | ✅ **complete** | `de07bfb` | 24 files / 28 shells; see below |
| **P2** | Nav, touch, bottom token | ✅ **complete** | `e11abc8` | Bottom bar, `--bottom-inset`, `coarse:`, EC-1 fixed |
| **P3** | Tables → `CardList` | ✅ **complete — 19 / 19 files** | `25c97ae` `ad533b0` `953a55a` `976b6e6` `e8b257c` `574fb3c` `80fbb41` | `card-fallback` is out of `PENDING_PARTS` and **enforced**. Next part (P4) inherits `dialog-max-w` + `sheet-vh`, still pending |
| **P4** | Dialogs | not-started | — | |
| **P5** | Agenda | not-started | — | |
| **P6** | Odontogram | not-started | — | |
| **P7** | Platform | not-started | — | |
| **P8** | LAN device trust | not-started | — | Needs physical iOS + Android devices |

## Quality gate log

| Part | `tsc --noEmit` | `npm run build` | `npm run check:responsive` | Date |
|---|---|---|---|---|
| baseline (pre-P0) | ✅ clean | ✅ clean | n/a | 2026-07-30 |
| **P0** | ✅ clean | ✅ clean | 2 failing (both P1's), 4 pending — **intended** | 2026-07-30 |
| **P1** | ✅ clean | ✅ clean | ✅ all enforced pass, 4 pending | 2026-07-31 |
| **P2** | ✅ clean | ✅ clean | ✅ all enforced pass, 3 pending | 2026-07-31 |
| **P3** (partial) | ✅ clean | ✅ clean | ✅ all enforced pass, 4 pending (`card-fallback` deliberately still pending) | 2026-07-31 |
| **P3** (17/19) | ✅ clean | ✅ clean | ✅ all enforced pass, 4 pending | 2026-07-31 |
| **P3** (19/19) | ✅ clean | ✅ exit 0 | ✅ all enforced pass, **3** pending — `card-fallback` now enforced | 2026-07-31 |

⚠️ **There is no lint gate in `web/`, and this was re-verified rather than assumed**: `npm run lint` fails with
*"'eslint' is not recognized"* — the package is not installed, and `next.config.ts` disables linting during the
build. `tsc --noEmit` + `npm run build` + `npm run check:responsive` are the whole automated gate, which is the
reason P0 exists at all.

⚠️ **A stale `.next` breaks the build at the page-data step, not at compile.** The first P3-final build printed
`Cannot find module '../chunks/ssr/[turbopack]_runtime.js'` **after** « Compiled successfully » — a cache
artefact, not a code defect. `rm -rf .next && npm run build` was clean. Do not start reading the diff for this.

## Session log

### P0 — the mechanical gate

`web/scripts/check-responsive.mjs` + `npm run check:responsive`. Zero dependencies, exits non-zero.

Eight checks, each tagged with the part that fixes it: `dialog-max-w` (P4) · `viewport-height` (P1) ·
`type-scale` (P1) · `breakpoint-tokens` (P1) · `sheet-vh` (P4) · `hover-movement` (P2) · `arch-clipping` (P6) ·
`header-orphans` (P2).

Written **before** P1's bulk edits, per the story's Notes — the point is a failing command while the work is in
flight, not a check run once at the end.

Two design notes worth keeping:

- **Staged enablement (`PENDING_PARTS`).** A check whose part has not landed reports `PENDING` and does not fail
  the run. Without it the gate would be red from the day it was written, which is how a check gets ignored.
  Removing an id from the set is one visible line of maintenance per part; when the set empties, everything is
  enforced. Deliberately **no per-file exemptions** — an allow-list that grows is a check that has stopped
  working.
- **Comment masking.** Several of this codebase's comments quote the exact classes these checks ban, explaining
  why they are banned. The first version only skipped lines *starting* with a comment marker, which still flagged
  the continuation lines of `/* … */` blocks (`dashboard-sidebar.tsx:129,131`). Replaced with a real block-comment
  mask.

### P1 — foundations and one `AppShell`

**Viewport (`app/layout.tsx`).** Added the missing `viewport` export — `viewportFit: "cover"` (without it
`env(safe-area-inset-bottom)` is `0px` and P2's bar sits under the home indicator) and
`interactiveWidget: "resizes-content"` (what keeps a sheet's sticky footer visible while typing, AC-25).
`maximumScale`/`userScalable` deliberately not set. `suppressHydrationWarning` added to `<html>` now so P7's
theme provider does not require a second edit of this file.

**Type scale (`app/globals.css`).** Two steps added to the plain `@theme` block (**not** `@theme inline`, which
exists so colour utilities emit literal values for the runtime light/dark swap): `--text-2xs` (11px, the floor)
and `--text-title` (26px, the page-title size `page-header.tsx` had as an arbitrary value). Both carry the paired
`--text-*--line-height`; without it the utility emits a font-size with no leading.

**No `--breakpoint-*` declared.** Verified against the installed `tailwindcss@4.1.18`: the four device states are
exactly the stock `sm`/`lg`/`xl` boundaries, so there is nothing to add — and *redefining* an existing key would
silently re-point all 75 `md:` utilities. A check now pins this.

**121 `text-[Npx]` → 0.** Primitives first (`ui/table.tsx` reaches 22 tables, `ui/page-header.tsx` 11 pages,
`ui/list-toolbar.tsx`). Mapping: 8/9/9.5/10/10.5/11 → `text-2xs` · 13 → `text-sm` · 15 → `text-base` ·
22 → `text-2xl` · 26 → `text-title`. Every sub-11px value rose to the floor, so no information-bearing text
renders below 11px (AC-2).

**`AppShell` (`components/app-shell.tsx`).** Replaces 28 hand-copied shells across 24 files. Props:
`width` (`7xl` default · `5xl` · `3xl` · `wide` · `none`), `gutter`, `mainClassName`, `contentClassName`.
Two deliberate non-features: it is **not** a client component (so `app/documents/[type]/page.tsx` stays `async`
for `await params`), and it does **not** render `ClinicGuard` (four shells render outside the guard on purpose so
a patient's skeleton and its « introuvable » state are visible instead of the guard's spinner).

**Divergence resolved.** 3 gutters → 1 (`p-4 md:p-6`); 4 overflow variants → `overflow-y-auto` by default with
`mainClassName` as the override; 6 content widths → 5 named variants. `/appointments` keeps `overflow-hidden` +
`h-full flex flex-col` — the one genuine escape hatch, because the calendar scrolls its own grid.

**`h-screen` → `h-dvh` everywhere**, including `dashboard-sidebar.tsx`'s `<aside>` in the same commit (the two
disagreeing is what grows a second scrollbar), the onboarding routes, `clinic-guard.tsx`, and
`document-editor-content.tsx`'s nested full-viewport div, which became `flex-1 min-h-0` now that it sits inside
a bounded `<main>`.

**Skip-link and landmarks (AC-5).** `AppShell` renders « Aller au contenu » as the first focusable element,
targeting `<main id="contenu-principal">`. The two elements both named « Navigation principale » were
disambiguated: the rail's `<nav>` keeps the name, the drawer's is « Navigation du menu », and `SheetContent`'s
redundant `aria-label` (which overrode its own `SheetTitle`) was removed.

**`lib/nav.ts`.** `NavItem` / `NavSection` / `baseSections` / `buildConfigItems` / `buildNavSections` /
`HIDDEN_PATHS` / `isChromeLessPath`, hoisted out of `dashboard-sidebar.tsx` and `ai-chat.tsx`. P2's bottom bar
consumes the same data; a second copy is how the two drift on exactly the device the bar exists for.

### P2 — navigation, touch targets, the bottom edge

**Bottom bar (`components/bottom-nav.tsx`).** Four destinations + « Plus », read from `lib/nav.ts` so it cannot
drift from the rail. Mounted by `AppShell` as a **flex sibling of `<main>`, not `fixed`** — `<main className="flex-1">`
then shrinks around it automatically, which is what EC-3's ~250px landscape content height needs, and it means
the bar needs no z-index and no scroll compensation.

**The header hamburger is gone**, superseding AC-P3.12's wording. « Plus » reuses `isMobileOpen`, so no third
piece of sidebar state and AC-P3.18 still holds. `useSidebar()` and the `Menu` import were removed from
`dashboard-header.tsx` — the `header-orphans` check exists precisely because `tsc` does not flag an unused
destructured binding and lint is broken here.

**One owner for the bottom edge.** `--bottom-bar-h` / `--bottom-inset` in `@theme`. Four things wanted that edge
and each carried its own `bottom-4`, so they overlapped. The AI panel's **AC-P3.16 geometry is deliberately
re-opened** and restated in terms of the token — the same 1rem gap, measured from above the bar, with the
available height reduced by the same amount so the panel still ends 1rem below the header.

**Toasts → `components/app-toaster.tsx`.** A client wrapper because `position` and `visibleToasts` are *props*,
not CSS. ⚠️ sonner's own `mobileOffset` keys on a hardcoded **600px viewport width**, which is the wrong
question — a 1180px iPad is the touch device and a 600px desktop window is not. On a coarse pointer:
bottom-centre, above the token, capped at 3.

**`coarse:` variant + `touch-target` utility.** Keyed on the **pointer**, not a breakpoint: an iPad landscape is
1180px and still a gloved hand at the chair, so a `md:`-based rule would miss the device this feature is for.
`touch-target` overlays a 44px pseudo-element and changes **no paint** — growing the controls would have grown
every row of 22 tables on a tablet.

⚠️ **Stacked list rows are the exception and grow their own height instead** (`coarse:py-3` on `SelectItem`,
`DropdownMenuItem`, the sidebar nav row). A 44px overlay on adjacent rows in a vertical stack overlaps, and the
overlap selects the wrong item — the opposite of the fix. `calendar.tsx` likewise raises `--cell-size` rather
than overlaying, since its days are a 7-across grid.

**Two hover rules, kept apart.** *Movement* hovers gated behind `hover-hover:` (4 sites). *Hover-revealed*
affordances got the **opposite** treatment: the file-delete button and the logo/cachet overlays were the only way
to perform those actions and did not exist at all on touch, so gating them behind `hover-hover:` would have made
that permanent — the `features/LEARNINGS.md` « space-based UI gating can hide a required affordance entirely »
failure. See DEV-2 for the image overlays.

**EC-1 fixed — a live bug, not a hypothetical.** `SheetContent`'s `md:hidden` hid the drawer's *content* while
Radix's overlay, scroll lock and focus trap stayed mounted, so rotating a tablet to landscape with the drawer
open left the page untouchable with nothing on screen to explain it, escapable only by Escape. Closed with
`lib/hooks/use-media-query.ts` — the first `matchMedia` in the codebase, SSR-guarded, and the narrow case that
earns one.

**`data-sheet-open` on `<body>`.** Written by `SheetContent` via a mount counter (a counter, not a boolean:
`/rappels` has a settings sheet on a page that also has the nav drawer). A body attribute rather than context
because the consumer — the bar — is not a descendant of the sheet, and Radix already communicates this way with
`data-scroll-locked`. Deliberately **not** in `SidebarContext`, which is persistence-adjacent.

**iOS focus zoom.** `Textarea` and `TimeField` gained `text-base md:text-sm`; `Input` already had the guard, so
every notes field and every booking time input zoomed the page on focus and never zoomed back.

### P3 — tables become cards (PARTIAL: 3 of 19 surfaces)

**`components/ui/card-list.tsx`.** A **replacement**, not a reflow. The obvious `display:block` version strips the
implicit row/cell roles in every browser, so a screen reader reads « Ben Salah 45,000 12/03 Payée » with no idea
which number is the money — not an acceptable trade on 22 surfaces, several of them clinical and financial. Each
card is a **description list**: `<dt>` carries the column name the `<th>` used to, `<dd>` the value.

The card is **one** interactive element — the title, stretched over the whole card by `after:absolute
after:inset-0`, with the action menu at `relative z-10` above it. A clickable container full of buttons would
either swallow the menu's taps or nest a menu inside a button.

⚠️ **AC-17 is enforced in the primitive, not at 19 call sites** — a field whose value is nullish or blank is
dropped. `0` and `false` are deliberately kept: a zero balance is a fact, not an absence.

**`Table` gained `containerClassName`.** The scroll container was a hardcoded inner div no caller could address,
so "the table is absent below `md:`" was literally unsatisfiable — hiding the `<table>` leaves its scrolling
wrapper behind. `CARDS_ONLY` / `TABLE_ONLY` are exported beside `CardList` so a call site reads as one decision.

**`ActiveFilterChip`** (AC-19), deliberately **not** a variant of `FilterChip`. That one is a *toggle*
(`aria-pressed`, the control **is** the filter); this is a *statement plus a dismiss*. It exists because a card
list has no header row, so a filtered list and a short list look identical — and nine dashboard links land on a
filtered list the user did not choose.

**`DataTablePagination`** now derives its `id` from `useId()`. A page renders two pagers once the card list and
the table are both present, and duplicate ids make `htmlFor` point at whichever the browser finds first.

**New `card-fallback` check (P3), derived not hand-listed.** P3 had no mechanical check at all, which would have
left AC-13 resting entirely on the deferred manual walk. It reflects over the source — every file rendering
`<Table>` must render `<CardList>` — so a table added next month is covered the day it is written. The **5
exclusions are the one thing the source cannot express** ("this table *is* a chart's accessible fallback"), so
each carries its reason, and a stale exemption naming a file that no longer renders a table is itself reported.

**The first three**, whose shapes the rest followed: `creances/receivables-table` (no action cell — the row *is*
the navigation, so the card is a link), `patients-table` (four icon buttons → one menu; « Non renseigné » removed
per AC-17), and `medication-catalog-table` (the plain shape the two other admin catalogs share verbatim).

**Converted (19 files, 25 tables):** receivables · patients · medication-catalog · cnam-nomenclature ·
dental-acts · procedure-types · user-management · treatment-plans-table · waiting-list · recurring-series ·
caisse-ledger · caisse dépenses · reminder-log · stock · invoices · lab-orders · patient-summary-modal ·
plan-workspace (2) · patients/[id] (4).

**Decisions worth not re-litigating**, each forced by the surface rather than chosen:

| Surface | What it does differently, and why |
|---|---|
| `caisse-ledger` | **Exception 1.** `runningBalance` is not a card field — « Solde de la période » is a fact about a row's *position in an ordered list*, and a card is read on its own. A footer states the closing balance once. Entrée/Sortie collapse to one signed amount: two columns where one is always empty is how a *table* shows direction. |
| `patient-summary-modal` | **Exception 3.** The table is **removed**, not hidden. Seven `min-w` columns summed ~760 px inside a dialog capped at 95vw with `overflow-x-hidden`, so the last columns were **clipped, not scrollable** — there is no width at which it worked, so a desktop copy would be a copy of the defect. |
| `invoices` | A **draft has no number**, so the title falls back to patient + date. Both server-supplied gates (`canCancel`, `canCreateAvoir`) are passed through, never re-derived — this file already fixed that defect once. |
| `lab-orders`, `user-management` | Their `<select>` stays a **control as a field's value**. Each is simultaneously the status display and the action; a menu would hide the current value and double the taps. |
| `waiting-list` | « Promouvoir » stays a visible button — it is the point of the screen. |
| `stock` | `itemRef` carries the low-stock deep-link target, so the notification still scrolls to the right card. The ref widened to `HTMLElement` and both trees set it via callback — one ref, no cast, only one tree ever mounted. |
| `reminder-log` | `STRIPE` became a CSS-colour map so the row's 2 px rule and the card's accent read **one** source. `failureReason` stays in the card, as the file's own comment demands. |
| `procedure-types` | The colour column is decoration → the card's accent, not a field whose value is a swatch. |
| `patients` | « Non renseigné » removed — AC-17 omits an absent field rather than printing a placeholder. |
| `plan-workspace` actes | **Exception 2.** « séance de N actes » is a **section header**, not a per-card badge; the tick box is `leading`; the reorder arrows are the « Ordre » field's value; the cards carry their own « Tout sélectionner ». The **only** converted surface whose action is a bare button rather than a menu — there is exactly one action per act, and hiding one button behind a menu costs a tap for nothing. |
| `plan-workspace` échéancier | The **date is the title**, against « date last »: an échéance has no other identity. Takes the menu precisely because its actions are variable-length — « Encaisser » plus one « Reçu » per payment. |
| `patients/[id]` dossiers | « Facturé » is the **status badge**, which also replaces the money cell's hover-only `title=`. « Reste » is **omitted** on an invoiced fiche rather than printed as « Facturé » — the facture owns that money. |
| `patients/[id]` rendez-vous | `borderLeft` → `accent`, cancelled → `muted`. The notes are **untruncated** in the card: the table hid them behind a `title=` tooltip no finger can reach. |

Two hover-only affordances were also inlined as text while converting, because no touch device can reach a
`title=`: stock's « périmé / expire bientôt » and the patient-files name tooltip.

#### The last two files (19/19) — `80fbb41`

Both multi-table, and left whole rather than half-done because the counting `card-fallback` check made a partial
conversion visible rather than green.

**`plan-workspace.tsx` (2 tables).**

- **Exception 2, resolved as a section header.** `actGroups` groups the acts by `scheduledAppointmentId` (only
  where more than one act shares it) and renders « Séance de N actes · date » over the cards that belong to it;
  the per-row badge is dropped from the card and kept in the table. ⚠️ A séance's acts are pulled to the séance's
  **first** position instead of being left where plan order puts them. Plan order is otherwise preserved, but a
  séance split across the order would print its header **twice, each time claiming a count larger than the cards
  under it** — a header that lies about what it heads is worse than a reordered one.
- **`CardList` gained `leading`.** The tick box is neither an action nor a field: it is the row's state *and* the
  control that changes it, and « tick, tick, planifier ensemble » cannot open a menu three times. Additive and
  optional; no existing caller changed. Logged as **DEV-3**.
- **The reorder arrows are a field's value** (« Ordre »), the pattern `lab-orders` set with its status `<select>`.
  Beside the title they would eat the désignation's only line at 320 px.
- **The cards get their own « Tout sélectionner ».** A card list has no header row, so the table's select-all
  checkbox had nowhere to live — without it, ticking eight acts on a phone is eight taps and the gesture the
  selection exists for stops being worth making.
- **The échéancier takes the date as its title** — an échéance has no other identity — and it is the one card here
  that takes the **menu**, because its actions are the feature's only variable-length set: « Encaisser » plus one
  « Reçu » **per payment**.

**`app/patients/[id]/page.tsx` (4 tables: dossiers, documents, rendez-vous, fichiers).**

- The expand/collapse notes cell became **`DentalRecordNotes`**, one implementation behind both trees, with one
  `expandedNotes` set so expanding on a phone sticks.
- « Facturé » moves to the card's **status badge** — which is also what replaces the money cell's hover-only
  `title=`, unreachable on touch. An invoiced fiche's « Reste » field is omitted rather than printed as
  « Facturé »: the facture owns that money and the struck-through amount already says so.
- The rendez-vous row's per-procedure `borderLeft` becomes the card **accent**, cancelled rows `muted`, and the
  notes stop being truncated behind a `title=` tooltip.
- `fileName` is the title, so AC-17's truncation is the primitive's; the full value is reached by tapping the
  card, which opens the preview.

**Three rules were shared rather than copied**, because two implementations of one rule is how the halves drift:
`PlanActPrimaryAction` / `PlanActStateBadge` / `PlanActSelectionBox` / `PlanActReorderControls` (out of
`plan-act-row.tsx`), **`appointmentVisitState`** (the end-of-visit + Cancelled/NoShow rule, which is subtle enough
that a copy would rot), and **`openMedicalDocument`** (the retired-`honoraires` redirect). Both patient lists also
stopped calling `.sort()` on their state array **in place** — harmless with one tree, two mutations per paint with
two, and the two could disagree about order.

Per-surface card titles, the three argued exceptions and the four controls-in-a-data-column are in
[plan.md § Part P3](../plan.md#part-p3--tables-become-cards).

The conversion shape, for the next table anyone adds: import `CardList, CARDS_ONLY, TABLE_ONLY`, put
`<CardList className={CARDS_ONLY} …/>` immediately above the table, and add `containerClassName={TABLE_ONLY}` to
the `<Table>`. ⚠️ Where the table sits in a ternary branch, the `CardList` becomes a second child of a slot that
takes one — wrap both in a fragment.

## Deviations

### DEV-1: `/settings` and `/users` keep their content exemption
**Date:** 2026-07-31 · **Story:** 1 (P1) · **Category:** Technical · **Approved:** Yes

**Original plan:** P1 step 5 — *"`/settings` and `/users` gain the gutter and the `max-w-7xl` wrapper they lack
(AC-3 says they lose their exemption)."*

**Actual implementation:** `<AppShell width="none" gutter={false}>` on both, with a comment stating why.

**Justification:** `ClinicSettings` (1174 lines) and `UserManagement` (428 lines) each paint a **full-bleed
tinted background** (`min-h-full bg-gray-50 dark:bg-slate-950`) and centre their **own** `max-w-5xl` at `p-3`/`p-4`.
Following the plan literally would have double-padded them, nested one max-width inside another, turned the
full-bleed background into a floating tinted rectangle, and broken `min-h-full` — which resolves against `<main>`
and would have started resolving against an auto-height wrapper. Both components carry an explicit comment
explaining that `min-h-full` is deliberate. The alternative (strip the background/padding/width out of both
components) is a redesign of two admin screens inside a part whose stated promise is *"nothing looks different on
a desktop"*.

**Impact:** AC-3's substance holds — one `AppShell` renders the sidebar, header and `<main>` for all 28 instances,
and one gutter/width rule governs every page that does not opt out. The difference is that the exemption is now
two visible props plus a comment rather than a hand-rolled `<main>`. No visual change. If the two components are
ever reworked to let the shell own their chrome, these props come off.

### DEV-2: the two image-replace overlays become corner buttons on touch, not always-on overlays
**Date:** 2026-07-31 · **Story:** 1 (P2) · **Category:** Technical · **Approved:** auto (see justification)

**Original plan:** P2 step 7 — *"Revealed affordances get a real touch path: … `clinic-settings.tsx:713` and
`mon-profil-content.tsx:186` (logo/cachet replace overlays)."*

**Actual implementation:** each renders **two** controls — the existing full-bleed hover overlay, now gated to
`hover-hover:`, plus a small always-visible corner button gated to `coarse:`.

**Justification:** the literal reading — make the existing overlay always visible on touch — has two failures the
plan's one-line description does not anticipate. The overlay is `absolute inset-0 bg-black/50`, so leaving it on
would (a) permanently obscure the very logo or cachet it is previewing, and (b) turn the whole thumbnail into an
**unconfirmed delete target**, so a stray tap while scrolling destroys a practitioner's signature image. The
third-party file-delete case (`patient-files-manager.tsx:516`) *is* a small corner button already, so it simply
inverts — the difference is the overlay geometry, not the rule.

**Impact:** the action now exists on touch, which it did not before, without either regression. Two elements
rather than one; both call the same handler and carry the same `aria-label`. Classified as significant (external
scope, adds an element) but implemented without asking, because the plan's *intent* — "give the affordance a real
touch path" — is unambiguous and the literal version is unsafe rather than merely different. Flagged here for
review.

### DEV-3: `CardList` gained a `leading` slot
**Date:** 2026-07-31 · **Story:** 1 (P3) · **Category:** Technical · **Approved:** auto (see justification)

**Original plan:** P3, `plan-act-row.tsx` — *"The selection checkbox and reorder controls are row-level, **not**
menu actions."* The plan states the constraint; it does not say where a row-level control lives on a card.

**Actual implementation:** `CardList` gained an optional `leading?: (item: T) => React.ReactNode`, rendered
before the title at `relative z-10` (the same escape from the stretched-title overlay that `actions` already
uses). The acts card puts its tick box there; the reorder arrows became the « Ordre » field's value instead.

**Justification:** the primitive had exactly two slots for a control — `actions`, which is the menu the constraint
forbids, and `fields`, whose values are read left-to-right as data. With neither, the only way to keep the tick
box was to nest it in `title`, which truncates and would put an interactive element inside the card's single
accessible name. `leading` is additive, optional, and changes nothing for the eighteen existing callers.

**Impact:** one new optional prop on a shared primitive P3 itself authored. Classified as significant (it touches
a file every converted surface imports) but implemented without asking, on the same reasoning as **DEV-2**: the
plan's intent is unambiguous and the alternatives are all worse rather than merely different. Flagged for review.

### DEV-4: the two patient lists stopped sorting their state array in place
**Date:** 2026-07-31 · **Story:** 1 (P3) · **Category:** Technical · **Approved:** auto (trivial)

**Original plan:** silent — P3 converts tables, it says nothing about sorting.

**Actual implementation:** `appointments.sort(…)` and `currentFiles.sort(…)`, both called inline in the JSX,
became `appointmentsNewestFirst` / `filesNewestFirst` computed once from a copy.

**Justification:** forced by the conversion rather than chosen. `.sort()` mutates, so those calls were sorting
React state **in place during render**; with two trees rendering the same data it becomes two mutations per
paint, and the card list and the table could disagree about order. Internal to the file, same output.

**Impact:** none visible. Recorded because it is a behaviour-adjacent edit the plan did not ask for.

## Corrections to the plan's counts

Measured during implementation; the plan's own numbers were themselves corrections to the spec's.

| Plan said | Actual | Note |
|---|---|---|
| 116 `text-[Npx]` | **121** | Plan under-counted; all 121 replaced |
| 30 shell instances | **28** across 24 files | Recount from the `h-screen` grep |

## Learnings

For `features/LEARNINGS.md` on completion:

- The tailwind-merge `max-w` collision on `DialogContent` (unfixed until P4, now pinned by a check).
- `next-themes` defaults to `attribute="data-theme"`; against a class-based `dark` variant it renders none of the
  `dark:` utilities while looking correctly wired. (P7.)
- **Dropping an import after moving code out of a file needs a grep for every symbol it provided, not just the
  ones you moved.** Removing the nav data from `dashboard-sidebar.tsx` took `Stethoscope` with it — still used by
  `brandHeader` as the brand mark. `tsc` caught this one; an unused *destructured binding* it would not have.
- **A JSX comment cannot be the first thing inside `return (` or inside a `&&` expression.** Two build breaks this
  session came from inserting an explanatory comment in front of the element it explains; both needed the comment
  moved above the `return` / outside the conditional.
- **A scripted source edit must normalise indentation from the file's actual state, not an assumed base.** A fixed
  dedent produced 2-space content in `documents/page.tsx`, whose JSX was already mis-indented before this session.
  Deriving the shift from the block's own minimum indent, then aligning the appended trailing block separately, is
  what made it correct.
- **A JSX comment cannot lead a `&&` branch either** — the same trap that broke P1 recurred in P2 on
  `sheet.tsx`'s `{showCloseButton && ( … )}`. The rule is: the comment goes *above* the expression, never inside
  it. Recorded twice now because it cost a build both times.
- **A regex lookbehind is the wrong anchor for a CSS class check.** P0's `hover-movement` check used
  `(?<!hover-hover:)` and reported two correctly-gated classes as violations: in
  `hover-hover:group-hover:scale-105` the inner `hover:scale-` is preceded by `group-`, so the lookbehind passed.
  Class checks must anchor on a **class boundary** (start, whitespace, quote). ⚠️ The failure mode that matters
  is the other direction — a check that quietly stops matching — so after fixing it, prove it still catches a
  real violation by feeding it one. Done here with a throwaway probe file.
- ⚠️ **A presence check passes as soon as the FIRST instance is fixed — count instead.** `card-fallback`
  originally asked "does this file that renders `<Table>` also render `<CardList>`?". `plan-workspace` has two
  tables and `patients/[id]` has four, so converting one would have turned the file green while the rest went on
  scrolling sideways. It was two conversions away from reporting success over unfinished work — the exact
  failure the guard-rot rule warns about, in its counting form rather than its listing form. Fixed to require
  one card list *per table*.
- ⚠️ **`CardList` drops a field on an empty *value*, and a React element is never empty.** The dossiers' notes
  field passes `<DentalRecordNotes …/>`, which renders `null` when there are no notes — and the card still drew
  an « NOTES » label over nothing, because `isEmptyValue` sees an element object. AC-17's omission has to be
  decided by the **caller**, from the data, not delegated to a component that renders nothing. Caught by reading
  the diff, not by `tsc`: both branches type-check identically.
- **A converted surface must be checked for what the table header carried, not only what the rows carried.** The
  acts table's « Sélectionner tous les actes » lives in a `<TableHead>`, and a card list has no header row — so
  the conversion silently dropped it while every row-level control survived. Row-by-row review does not see this;
  the question to ask each table is *what is in the header, the footer and the caption?*
- **Keying touch rules to a breakpoint would have missed the target device.** `md:` is 768px; the tablet a
  dentist holds in landscape is 1180px. Anything about *fingers* keys on `(pointer: coarse)`, anything about
  *space* keys on width — and this feature needs both, separately.

## Manual walk — the real acceptance gate (AC-51)

**Not started.** Deferred to the end of the feature, as the plan sequences it: it covers all 28 routes at
320 / 390 / 820 / 1180 / 1440 px, plus landscape phone, keyboard-only, and OS dark mode — and dark mode is not
reachable until P7 mounts the theme provider, so walking now would have to be repeated.

P1's own verifiable claim — *"every route renders identically at 1440px"* — rests on `tsc` + `build` + the
mechanical checks, and is **not** a substitute for that walk.

| Route | 320 | 390 | 820 | 1180 | 1440 | Landscape | Keyboard | Dark | ACs proved |
|---|---|---|---|---|---|---|---|---|---|
| _pending_ | | | | | | | | | |
