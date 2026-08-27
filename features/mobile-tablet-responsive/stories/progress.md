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
| **P4** | Dialogs | ✅ **complete** | `2dc3be7` | All 8 steps. `dialog-max-w` + `sheet-vh` both enforced; only `arch-clipping` (P6) still pending |
| **P5** | Agenda | ✅ **complete** | `028747b` `b775137` `5d6bb5b` `0690246` | AC-28…AC-31. `agenda-scroll` added in `b775137`, actually **enforced** only in `5d6bb5b` — see the note under the gate log |
| **P6** | Odontogram | ✅ **complete** | `97fb588` `7895681` | AC-32…AC-35. `arch-clipping` enforced → **all 9 checks enforced, 0 pending**. AC-35 needed no code (DEV-9) |
| **P7** | Platform | 🟡 **partial — 6 of 8 ACs** | `52e91e6` `bbb9143` `50c54cd` | AC-37 · AC-38 · AC-39 · AC-41 (core) · AC-42 · AC-43 done. **AC-40 (print) remains**; **AC-36's icon assets remain**; AC-41's five inline paths + the realtime re-subscribe sit in files the parallel session holds |
| **P8** | LAN device trust | 🟡 **built — AC-44 · AC-45 done; AC-46 open** | `3ca0b17` | Trust page + listener + `TrustPortGate` + packaging + 4 documented failure states. **AC-46 cannot be closed here** — it needs a physical iPhone, and shortening the leaf to hedge it would be worse than the risk (see below) |

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
| **P4** | ✅ clean | ✅ exit 0 | ✅ all enforced pass, **1** pending (P6 only) | 2026-07-31 |
| **P5** (partial) | ✅ clean | ✅ exit 0 | ✅ all enforced pass, 1 pending (P6) | 2026-07-31 |
| **P5** (calendar) | ✅ clean | ✅ exit 0 | ✅ all enforced pass, 2 pending — `agenda-scroll` added but **still PENDING** | 2026-07-31 |
| **P5** (`agenda-scroll` enforced) | ✅ clean | ✅ exit 0 | ✅ all enforced pass, **1** pending (P6 only) | 2026-07-31 |
| **P5** (complete) | ✅ clean | ✅ exit 0 | ✅ all enforced pass, 1 pending (P6 only) | 2026-07-31 |
| **P6** (AC-32 + step 5) | ✅ clean | ✅ exit 0 | ✅ **all 9 checks enforced and passing, 0 pending** | 2026-07-31 |
| **P6** (complete) | ✅ clean | ✅ exit 0 | ✅ all 9 enforced and passing, 0 pending | 2026-07-31 |
| **P7** (AC-42 timer + AC-43) | ✅ clean | ✅ exit 0 | ✅ all 9 enforced and passing, 0 pending | 2026-07-31 |
| **P7** (dark mode) | ✅ clean | ✅ exit 0 | ✅ all 9 enforced and passing, 0 pending | 2026-07-31 |
| **P7** (downloads · manifest · retour) | ✅ clean | ✅ exit 0 (`/manifest.webmanifest` emitted) | ✅ all 9 enforced and passing | 2026-07-31 |
| **P8** (backend) | n/a — no `web/` change | n/a | ✅ all 9 enforced and passing (run anyway, to prove it) | 2026-07-31 |

**P8 is the story's only backend part, so it has its own gate row.** `dotnet build ClinicManagement.sln
--no-incremental` → **0 errors, 57 warnings — byte-identical to the pre-P8 baseline**, and none of the 57 point
at a file P8 added or changed (`CS8618` ×88 / `CS8602` ×12 / `CS8981` ×4 / `CS8604` ×4 / `CS8600` ×4 /
`CS0618` ×2 occurrences, all pre-existing and untouched). The web gates are marked `n/a` rather than skipped:
P8 changes no file under `web/`, and `npm run check:responsive` was run regardless to prove the frontend gate
did not move.

⚠️ **The test suite ran, and its result needs stating carefully.** `dotnet test` is blocked on this machine by
Smart App Control, so the suite went through the recorded workaround (`dotnet build -p:OutDir=<scratch>` then
`dotnet vstest`). P8's own tests — `TrustPortGateTests`, `AppleTrustProfileTests`,
`ControllerAuthorizationCoverageTests` (both directions) and `CertificateProvisionerTests` (which covers the
`LanAddresses` extraction) — are **33 passed, 0 failed**.

The full suite is **1486 passed, 27 failed**, and *none of the 27 is P8's*. That is measured, not asserted: a
detached `git worktree` at `HEAD` was built and run to get a clean baseline, and the two failure sets diff to

- **24 failures already present at `HEAD`** — a pre-existing red baseline (catalog/search/paging handler tests
  and three tenant-isolation list tests). Nobody had noticed because every earlier part of this story was
  frontend-only and never ran the backend suite.
- **3 further failures** in the working tree, all `LiaisonRenderContentTests` — the parallel session's
  in-flight `LiaisonContent.cs`, which P8 does not touch.
- **0 failures introduced by P8**, and none fixed by it.

The 24-test `HEAD` baseline is a real finding and is **out of P8's scope** — it belongs to whoever owns the
paging/search work, and is recorded here so the next person does not re-derive it. Do not read a green P8 as a
green suite.

⚠️ **A freshly-added, freshly-passing check looks identical whether or not it is enforced — and that hid a
false claim for one commit.** `b775137` added `agenda-scroll` and its own message called it enforced; `"P5"` was
still in `PENDING_PARTS`, so a failure would have printed `PENDING` instead of failing the run. Two things
concealed it: the probe used **`--strict`**, which bypasses `PENDING_PARTS` by design and therefore proves the
check's *logic* while saying nothing about the gate acting on it; and a check with **zero hits prints `✓`**
either way, because the pending state is only rendered on a check that is currently *failing*. Fixed in
`5d6bb5b` and re-proved the right way — a deliberate `HOUR_HEIGHT` change now fails `npm run check:responsive`
with **no flags**. The rule this yields: **prove a new check by breaking the source and running the gate exactly
as CI would**, never with `--strict`.

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

### P4 — dialogs become sheets, and stay their own width — `2dc3be7`

**The one idea the part turns on: Radix's dialog *is* the sheet primitive.** `ui/sheet.tsx` imports the very
same `@radix-ui/react-dialog`, so the entire responsive behaviour is a **class list on the node that already
exists** — `DialogContent` gained `mobile="bottom" | "sheet"` and `AlertDialogContent` reuses the exported
constants. Three consequences, none of which the planned `vaul` route offers (**DEV-5**):

- **AC-24 is true by construction, everywhere.** One element means crossing a breakpoint cannot remount and
  cannot lose typed input. `vaul` has no centred-dialog mode, so any dialog using it needs a `useMediaQuery`
  swap — exactly what AC-24 forbids.
- **All 26 `AlertDialogContent` instances across 20 files were fixed by one edit.**
- **R-12 evaporates** (no `vaul` 500 ms to override against `sheet.tsx`'s deliberate 300/200 `ease-panel`), and
  there is no swipe channel for the dirty guard or the post-visit `handleLater` to have to cover.

**The clamp (AC-20), 28 sites.** `edit-patient-dialog` asked for `max-w-4xl` and rendered at **512 px** on every
desktop. ⚠️ Two sites build the class in a **template literal** (the two file previews); the check tokenises
through the braces, so it sees them.

**Everything moved to `md:`, including the base.** `sm:max-w-lg` → `md:max-w-lg`. An `sm:` override straddles
640–767 px where the mobile sheet is still in force — two `max-w` utilities in different variants, twMerge keeps
both, stylesheet order decides. That is the ambiguity AC-20 exists to remove, so **`dialog-max-w` now demands
`md:` specifically** rather than "any prefix". Re-proved with a throwaway probe: it still catches an unprefixed
token *and* an `sm:` one, and still passes both template-literal sites.

**`DialogBody` (AC-21).** `flex-1` + ⚠️ **`min-h-0`** — a flex item's default `min-height: auto` refuses to
shrink below its content, so without it the body pushes the footer off the bottom instead of scrolling, which is
the AC-25 failure exactly. Header and footer sit **outside** the scroll container rather than being
`position: sticky`, which iOS momentum scrolls past. It retired `edit-patient-dialog`'s
`max-h-[calc(90vh-200px)]` — a magic number that guessed the chrome's height *and* guessed it in `vh`.

**The dirty guard (AC-23).** `lib/hooks/use-dirty-guard.ts` + `ui/discard-changes-dialog.tsx`, wired into the
five heavy forms. It wraps the **root** `onOpenChange`, because Radix funnels the ✕, `Escape` and the outside
tap through that one function; the back gesture is the exception and gets its own history entry. Dirtiness is
**observed** from `input`/`change` events inside the open content rather than declared per form (**DEV-7**).

**Focus lands on the title, not the first field (AC-22).** Focusing an input raises the on-screen keyboard, so a
sheet opened to be *read* lost half its viewport before the user asked to type anything.

**Grids and popovers (AC-26).** 30 form grids became `grid-cols-1 sm:grid-cols-2`. The popover cap is **one line
in the primitive** — `max-w-[calc(100vw-2rem)]` on `PopoverContent` — which reaches the two `w-[384px]` document
pickers and every `w-80` at once, including files this part could not otherwise touch. ⚠️ It is a separate `cn`
argument and a `max-w`, i.e. a *different* twMerge group from the callers' `w-*`, so it survives their override
instead of racing it. The 7-column calendar header and the 5-column colour palette are deliberately untouched:
AC-26 is about *form* grids, and one swatch per row is not a fix.

**The post-visit prompt is a toast on touch (AC-27).** Keyed on `(pointer: coarse)`, **not a width** — a
dentist's tablet in landscape is 1180 px, so a width test would leave the modal exactly where it hurts most.
⚠️ Sonner's own dismiss *and* timeout are both wired to `handleLater`: without that the snooze is never written
and the prompt returns on the very next 60-second poll, which is the defect the local `dismissed` flag exists to
fix. It also suppresses itself while `data-sheet-open` or `data-scroll-locked` is on the body.

#### Working alongside a concurrent session

A parallel session (the liaison / document-email feature) was live in this tree for the whole of P4 and grew to
~16 API files plus a dozen new ones. Three consequences worth recording, because they will recur:

- **It broke the build mid-part.** Its half-applied edit in `plan-workspace.tsx` referenced `receipts`, a name
  that exists only inside P4's own card-list `actions` closure — two `tsc` errors that were **not P4's**. Work
  stopped uncommitted rather than finish someone else's feature; it resolved on its own and the tree went green.
- **Two files hold both sets of changes.** `plan-workspace.tsx` and `invoices-table.tsx` carry the email feature
  *and* P4's four `md:max-w-md` lines. Staging the whole file would have committed their work, so only P4's four
  hunks were staged, via a filtered `git apply --cached --unidiff-zero`. ⚠️ Worth knowing this is possible: the
  alternative was leaving the gate red at this commit, since those four unprefixed `max-w-md` are exactly what
  `dialog-max-w` fails on.
- **One fix rides in their file.** `send-document-email-dialog.tsx` is untracked and theirs; its
  `sm:max-w-[560px] max-h-[90vh]` was the last hit in *both* P4 checks. The two-token conformance fix was applied
  in the working tree but **left uncommitted** for its owner to carry with the file.

### P5 — the agenda (PARTIAL: AC-28 · AC-29 · AC-31) — `028747b`

Everything landed is in **`app/appointments/page.tsx`**. `appointment-calendar.tsx` is **untouched**.

**Jour is an initial value, not a rule (AC-28).** `viewDecidedRef` is claimed by whichever of three things
speaks first — the user picking a tab, the drill-through forcing Mois, or the narrow default the first time it
applies. That one-shot is what keeps a picked view across rotation; without it the default re-asserts on every
`isNarrow` change and discards the view the user just chose (`features/LEARNINGS.md`: a size heuristic must not
be the sole gate on an affordance).
⚠️ It is an **effect**, not a lazy `useState` initialiser: `useMediaQuery` is SSR-guarded and reports `false` on
the server *and* on the first client render, so an initialiser would always read "wide" and never fire.

**The drill-through outranks it (AC-29).** `?from=` now calls `selectView`, which marks the view decided.

**The two link-applied toggles are `ActiveFilterChip`s (AC-29)** — P3's primitive, not a second chip.

**The toolbar is two rows (AC-31).** View switch + « Nouveau rendez-vous » on a fixed first row; the admin
Google controls, the praticien filter and the chips wrap underneath. The label shortens to « Nouveau » rather
than going icon-only.

#### The calendar half — `b775137`

The restructure the previous session analysed was carried out as designed. Both traps were real; neither
needed re-deriving.

**One element scrolls both axes.** `overflow-auto` on the single scroll container, the day header **moved
inside** it as `sticky top-0`, and the time labels are `sticky left-0`. The nested alternative
(horizontal outside, vertical inside) was rejected for the reason recorded below — a `sticky left-0` gutter
only sticks to a scrollport that scrolls horizontally, so that arrangement cannot produce one at all.

**The `w-max` wrapper is what keeps the overlay honest.** Grid *and* overlay are its children, so
`(100% - 60px) / 7` measures the grid rather than the viewport. The `calc()` strings were **not touched**, which
is exactly what preserved the `HOUR_HEIGHT` invariant.

**`WEEK_COLS`** is now the one column template behind the header, the hour grid and the loading skeleton. Its
`96px` is an **arithmetic contract**: `60 + 7 × 96 = 732`, so the overlay's expression resolves to exactly one
column. The new check reads both numbers out of the source and fails if either moves.

**One latent bug the move exposed and fixed:** `container.scrollTop = 8 * HOUR_HEIGHT` assumed the hour grid
began at the scroller's top. With the header inside, that lands *8 AM minus the header* and cuts off the
morning — it now asks the `08:00` row for its `offsetTop`. `currentTimePosition` needed no change: it already
read `offsetTop`, which is measured from the new `relative` wrapper, i.e. already in `scrollTop` coordinates.

**Z-order, settled:** blocks `z-20` → sticky gutter `z-30` → current-time line and dot `z-40` → sticky header
`z-50`. The dot is drawn at `left: 46px`, i.e. *inside* the gutter, which is why it has to outrank it.

**Month dots (AC-28).** A chip in a ~45 px month cell is a coloured sliver holding two characters of a name —
worse than nothing, because it reads as data. Dots are `aria-hidden` with an `sr-only` count beside them, and
the cell still drops into Jour where the names are legible.

**Calendar toolbar (AC-31).** The range title truncates; the four-item legend folds into a « Légende »
disclosure below `md:` **rather than being hidden** — the grey hors-horaires shading has no other explanation
anywhere, which is the defect its legend entry was added to fix; the filters' divider border is `md:`-only,
because a lone `border-l` on a wrapped row reads as a rendering artefact.

#### The Semaine density strip — `0690246`

AC-28's last clause, and the end of P5.

Below `md:`, Semaine renders **seven tappable days with their density** instead of the time grid. The AC-30
grid is honest from `md:` up; on a 320–390 px phone that same grid is a 732 px canvas read through a 320 px
window, which is navigation rather than reading. The strip answers what a phone actually asks of a week —
*which day do I need?* — and hands off to Jour.

⚠️ **Seven rows, not seven columns (DEV-8).** « Strip » implies a horizontal band; seven cells across 320 px is
~45 px each, which fits dots and nothing else — the same unreadable sliver the month chips became — and it
would leave the rest of the screen blank, because the time grid is exactly what it replaces. Rows use the width
the phone has, so each day carries its **count** and its **first appointment time** as well as the colour dots.

⚠️ **A real render branch, not `md:hidden`.** A `display: none` scroll container reports `offsetTop: 0` for
every row, so the 8 AM positioning and the current-time line would both compute against a zero-height layout
and be wrong the moment the viewport crossed back to `md:`. Not rendering it means there is nothing to
mis-measure — and `isNarrow` joined the scroll effect's dependencies, so crossing the breakpoint *mounts* the
grid **and** positions it, rather than leaving it at midnight.

**It reuses what already existed** rather than adding a path: `onSelectDay` → the page's `handleSelectDay`,
which calls `selectView("day")` and therefore marks the view **decided**, so tapping a day cannot be undone by
the narrow-screen Jour default re-asserting. The dots follow the month cells' shape — `aria-hidden` decoration,
with the count as the accessible fact.

#### Superseded — the previous session's AC-30 analysis (kept: it was correct)

**Do not** just delete `overflow-x-hidden` at `appointment-calendar.tsx:881` and add a `min-w`. Two things break
silently, both found by reading the positioning maths rather than by any check:

**1. The overlay's `100%` stops meaning the grid.** Appointment blocks are **absolute children of the scroll
container** (`:878`), and `weekBandLeftExpr` / `weekBandWidthExpr` (`:344-345`) size them with
`(100% - 60px) / 7`. A percentage resolves against the containing block's *padding box* — the **visible** width,
not the scrollable content width. Today `overflow-x-hidden` makes those equal; the moment the grid is wider than
the container they diverge and **every week block lands in the wrong column**. The fix is an inner
`relative w-max min-w-full` wrapper holding the grid *and* the overlay, so `100%` is the grid's real width — the
`calc()` strings themselves need no change, which is what keeps the `HOUR_HEIGHT` invariant intact.

**2. The day header would desynchronise from its columns.** The 7-day header (`:814`) is a **sibling above** the
scroll container, not inside it. Scroll the grid sideways and the dates stay put over the wrong columns. It has
to move **inside the same scroller**, which then has to scroll **both** axes — because a `sticky left-0` time
gutter only sticks to a scrollport that actually scrolls horizontally, so the nested
horizontal-outside / vertical-inside arrangement cannot give a sticky gutter at all.

Three behaviours must be re-verified after that move, because all three assume the grid starts at scroll 0:
`container.scrollTop = 8 * HOUR_HEIGHT` (`:322`), `currentTimePosition` from `slotElement.offsetTop` (`:316`),
and the header's own `sticky top-0`. Z-order also needs a pass: blocks are `z-20`, the current-time line and dot
are `z-30`, and a sticky gutter has to sit **above the blocks but below the dot** (the dot is at `left: 46px`,
i.e. inside the gutter).

**Also still open:** the week **density strip** and the month **dots** (plan step 3), and the calendar's own
toolbar — the 4-item legend + 2 switches at `appointment-calendar.tsx` (plan step 6; only the *page* toolbar was
restructured).

**No P5 check was added to `check-responsive.mjs`,** and `"P5"` was correctly never in `PENDING_PARTS`. The
obvious candidate — "the calendar has no `overflow-x-hidden`" — would have to be written *with* AC-30, since
today it would fail on work that has not been done. `HOUR_HEIGHT === 48` is worth pinning at the same time.

### P6 — the odontogram (PARTIAL: AC-32 + step 5) — `97fb588`

**The clipping is fixed, and it was the real defect.** `justify-content: center` distributes overflow to
**both** sides, and the inline-start overflow is **not in the scrollable region** — so at 390 px teeth 18–15
and 48–45 were unreachable *by any means*: not by scrolling, not by dragging. Six sites across the three
charts, all the same shape.

**One `mx-auto w-max` wrapper per chart, not one per row.** `w-max` sizes the block to its content and
`mx-auto` still centres it while there is room; once the content is wider the auto margins collapse to zero
(they cannot go negative), so the arch starts at the scroll origin. Wrapping the whole block rather than each
row means the « Maxillaire »/« Mandibule » labels and the midline rule share the arch's width, instead of
centring against the viewport and drifting off it once scrolled.

⚠️ **The glyph-centring sites are untouched and still unmatched.** `odontogram.tsx` and
`odontogram-acts-chart.tsx` each keep a `flex h-9 w-7 items-center justify-center` that centres a glyph inside
its own cell — a different construct, which the check's `why` already documents. Verified after the fix, so a
later part is not tempted to "fix" a false positive.

**One FDI source (step 5).** The quadrant layout was copied in `odontogram.tsx` and `record-tooth-chart.tsx`
while the **flat** list lived in `tooth-multiselect.tsx` — three literals that happened to agree, with nothing
making them. The quadrants now live with the authority (which mirrors the backend `FdiTooth.IsAdult`) and
`ADULT_FDI`/`CHILD_FDI` are **derived** from them by flattening, so a divergence is impossible rather than
merely unlikely. The quadrant order already matched the flat order, so no value moved.
⚠️ `odontogram-acts-chart` was never a copy — it takes `teeth` as a **prop**, so the plan's "third copy" is
really two.

**`arch-clipping` is enforced, and PENDING_PARTS is now empty of active checks — the gate reports 0 pending
for the first time.** Proved by breaking the source and running `npm run check:responsive` with **no flags**,
per the rule added after `5d6bb5b`.

#### P6 finished — `7895681`

**`ToothArchLayout` (AC-34).** The scroll box, the two arch rows, the midline, the labels and the arch switch
are now one component; `record-tooth-chart`'s whole render collapses to a single line.

⚠️ **Geometry only, and that is the whole design.** It takes `teeth` + `renderTooth` (+ an optional arch
control) and **nothing else** — no `paint`, `onToggleTooth`, `disabled`, `toothTitle`, `entries`, or open/hover
state. Both contracts survive because each chart still owns its own `renderTooth`:
- `odontogram-acts-chart` keeps `tappedTooth`/`hoveredTooth` in the **parent** (`476a2e3`), so 32 cells a few
  pixels apart cannot stack a panel per tooth the pointer crosses;
- `record-tooth-chart` keeps its lack of selection chrome, which is what lets the read-only summary reuse it
  with `disabled` — and why it uses a native `title` rather than a Radix tooltip.

**The AC-32 fix moved intact.** `mx-auto w-max` is now written once, with the reasoning, inside the layout.
`arch-clipping` stayed green through the extraction and the three glyph-centring sites are still unmatched.

**One arch below `md:` (AC-33).** A Haut/Bas group switches arches; both draw at `md:` and up, where the switch
does not render at all — a control offering to fix nothing is noise. ⚠️ Keyed on **width**, not
`(pointer: coarse)`: this is about *room*, and P2 settled that space keys on width while fingers key on the
pointer. The midline only draws when both arches are showing.

**44 px teeth (AC-33)** via `touch-target` on all three tooth buttons — P2's primitive, which raises the
tappable area on a coarse pointer without changing a painted pixel, so there is no density regression on the
tablet this feature is for.

**AC-35 needed no code — see DEV-9.** The plan's step 4 rested on a premise that does not hold.

### P7 — platform (PARTIAL: AC-42's timer + AC-43) — `52e91e6`

**The inactivity timer was not enforcing anything.** It stored only the `setTimeout` handle, so the 30-minute
limit existed only while a timer was *running*. A backgrounded or frozen tab — **a phone locked in a pocket,
which is the case the limit exists for** — has its timers throttled or suspended, so the callback never fired
and the session stayed open past the limit. And `reset()` re-armed the **full** limit on every event, so the
first mousemove after coming back silently *extended* the session rather than ending it.

**Wall-clock is now the authority; the timer is only a wake-up call.** `lastActivityAtMs` is the fact, `arm()`
derives the delay from it, and every path — a real event, the tab becoming visible, the timer firing early —
re-derives instead of assuming. A timer that fires late or not at all can now only **delay** the logout to the
next wake-up, never skip it.
⚠️ `visibilitychange` is on **`document`**, not `window`, so it cannot join the existing `keyof WindowEventMap`
array — and it is **not activity**: returning to the tab re-checks the clock, never restarts it. That one
listener is what closes the hole, because it is the first thing to run when a phone is unlocked.
⚠️ **Local-only.** Cloud has no inactivity timer at all (Auth0 owns session lifetime), so this provider is the
only place the rule exists — AC-42 should be read as Local-scoped.

**A timeout remembers the screen, a deliberate logout does not** (AC-42). `/login` already honoured `returnTo`
behind an open-redirect guard, so only the caller needed changing.

**French and retryable (AC-43).** `client.ts` threw *"Network error: Unable to connect to the API. Please check
if the API is running and CORS is configured correctly."* — English, and addressed to whoever deployed the app
rather than the dentist reading it. Since `errors.ts` passes an `ApiError` message through **verbatim**, that
string *was* the toast. It is now `NETWORK_ERROR_MESSAGE`, worded to match `connectivity.tsx`'s « Serveur
injoignable » banner so the two ways the app can notice one outage do not describe it differently.
⚠️ Keyed on a new **`ApiErrorCode.Network`**, not on `status === 0`: the client also raises `status: 0` for an
unexpected throw, and offering « Réessayer » for a fault sends the user round a loop that cannot succeed.
`showErrorToast` gained an optional `onRetry`, rendered **only** when `isNetworkError`.

**Dark mode — `bbb9143`.** ⚠️ `attribute="class"` is not optional and getting it wrong **fails silently**:
next-themes defaults to `data-theme`, `globals.css` declares a class-based variant, so the default would write
`data-theme="dark"`, make the toggle *appear* to work, report `resolvedTheme === "dark"` — and apply **none**
of the 336 `dark:` utilities. Nothing errors; the page stays light.
**AC-39's exemption needed two halves.** The variant now excludes `:is(.light, .light *)` — it has to live
*there*, because `&:is(.dark *)` matches any descendant however deep, so a class on a wrapper could not stop
it. And the palette **variables** need the other half: `.dark` sets `--background`/`--foreground` for its whole
subtree, so `.light` is listed alongside `:root` to get the light values back. Custom properties resolve from
the *nearest* ancestor that sets them, so a `.light` inside `.dark` wins for its subtree with no specificity
games and without repeating fifty tokens.
The control is a radio group in the **user menu**, not `/settings`: it is a per-device preference (dark on the
chairside tablet, light at the desk) and `/settings` is shared clinic configuration. Bound to `theme`, not
`resolvedTheme`, which would collapse « système » into whichever concrete theme the OS reports and tick the
wrong row. Sonner needed its own `theme` prop — it renders in a portal and does not see the `.dark` class.
⚠️ **The visual pass is AC-51's manual walk, not this commit.** The mechanism is verified by `tsc`/build; that
all 336 utilities *look* right across 28 routes is exactly what the walk exists for.

**Downloads, manifest, « Retour » — `50c54cd`.** The sync `revokeObjectURL` was a race that loses on iOS
(a `blob:` download is handed to a viewer **asynchronously**, so revoking immediately invalidated the URL and
the receipt never arrived, silently) and on `window.open` (the navigation has not begun when the next line
runs). Now deferred 60 s. Three delivery paths: Web Share on a coarse pointer where `canShare({ files })`
agrees, else a new tab — ⚠️ `<a download>` is **ignored** for `blob:` URLs by iOS Safari, so the anchor route
silently does nothing there — else the classic anchor. ⚠️ Gated on `canShare({ files })` rather than the mere
presence of `navigator.share`, which Android Chrome exposes while refusing files; an `AbortError` is the user's
decision and does **not** fall through.
`app/manifest.ts` ships with ⚠️ **`icons: []` deliberately** — see AC-36 below. « Retour » renders only under
`(display-mode: standalone)`, the real state rather than a UA sniff, and **not** keyed on width: an installed
app on a 1440 px desktop has no browser back button either.

#### ⚠️ Still open in P7 — two items, plus what the parallel session blocks

- **AC-36 — the icon assets.** ⚠️ **Blocked, not forgotten.** `layout.tsx` declares `/icon-light-32x32.png`,
  `/icon-dark-32x32.png`, `/icon.svg` and `/apple-icon.png`; **none exists** — `public/` still holds only the
  untouched `create-next-app` SVGs. `manifest.ts` therefore ships `icons: []` **on purpose**: listing files
  that 404 would make the manifest look complete while the installed app shows a blank tile nobody can
  explain, which is worse than not being installable. A manifest without `icons` is valid and browsers fall
  back, so install already works. **Everything else in AC-36 is done** — this needs someone to produce real
  raster assets, which should not be faked with a generated placeholder.
- **AC-40 — print.** No `@media print` and no `print:` utility exists anywhere in the app; the only print CSS
  is a string inside a `window.open` document. ⚠️ While there:
  `document-editor-content.tsx`'s `document.querySelector('style')?.textContent` grabs **the first `<style>`
  in the document, whatever it is** — and prod Next ships a `<link>`, not a `<style>`, so the popup's styling
  is effectively its inline attributes plus an Arial fallback.
  **Also blocked:** that file carries the parallel session's uncommitted work.

**Blocked on the parallel session's files** (all three uncommitted at the time of writing):
- **AC-41's remaining five paths** — `document-editor-content.tsx` and `factures/invoices-table.tsx` inline the
  object-URL dance or use `file-saver`; one of them is the El Fatoora XML the spec names. `download.ts` (the
  shared core, 8 of 13 paths) is done. The two free inline sites are `mon-profil-content.tsx` and
  `patient-files-manager.tsx`.
- **The realtime re-subscribe** — `lib/realtime/clinic-hub.ts`. ⚠️ Worth doing as soon as it is free:
  `withAutomaticReconnect()` bare is **four attempts then `Disconnected` for good**, at `LogLevel.None`, so a
  permanent disconnect is completely silent.

### P8 — LAN device trust (AC-44 · AC-45 done, AC-46 open)

Taken out of order: P7's last two ACs are both blocked on files or assets that are not mine to produce, and P8
had unblocked work. It is the story's **only backend part**.

**What landed.**

1. **`GET /api/trust`** (`TrustController`) — a French, self-contained instructions page plus three assets:
   `ca.crt` (DER, what Android imports), `profile.mobileconfig` (the same DER wrapped for iOS), and `qr.png`.
   Local-only through the same runtime `IsLocalMode(...) → NotFound()` gate `ConnectivityController` uses; four
   `[AllowAnonymous]` actions. Routes sit under `/api/` because the YARP catch-all forwards everything else to
   Next, and the assets are returned as `File(...)` because this host has no `UseStaticFiles`.
2. **A third Kestrel listener** on `Hosting:TrustPort` (5080, `0` disables), plain HTTP. It has to be cleartext:
   a device cannot be asked to fetch the fix for a certificate over that certificate.
3. **`TrustPortGate`** — see the finding below; this is the part that makes (2) safe.
4. **`LanAddresses`** — one answer to "which addresses is this server reachable at", now shared by the
   certificate's SANs and the page that advertises an address.
5. **Packaging** — the installer opens `5080` (and removes the rule on uninstall), writes `TrustPort` into
   `appsettings.Production.json` explicitly, and `packaging/README.md` gains the mobile flow, a **four**-row
   failure-state table and an operator checklist block.
6. **Tests** — `TrustPortGateTests` (10 cases), `AppleTrustProfileTests` (8), and the four new actions added to
   `ControllerAuthorizationCoverageTests.ExpectedAnonymous` with a reviewed comment.

**⚠️ The finding: the plan's step 1, implemented literally, would have reopened Phase 4's Finding 2.**
Step 1 says to add `kestrel.ListenAnyIP(trustPort)`. But **a Kestrel listener is not scoped to a subset of
routes** — every endpoint the app maps answers on every port it binds. That bind *alone* therefore publishes
the entire cleartext API on the LAN, `POST /api/auth/login` included, which is precisely the exposure the
comment three lines above it explains is why `Hosting:HttpPort` stays on `ListenLocalhost`. The plan is not
wrong — its own **R-11** states the requirement ("trust port serves *only* the Local-only trust controller") —
it just never says by what mechanism, and the naive reading is unsafe. `TrustPortGate` is that mechanism:
a middleware placed after the security headers and before everything else, refusing any path outside
`/api/trust` **on that port only** (the restriction is one-way; the front door still serves the whole app).
It matches on `StartsWithSegments`, not text, so `/api/trusted-devices` cannot slip through on a prefix.
Startup additionally **refuses** a trust port colliding with the HTTP/HTTPS/web ports — that misconfiguration
would make the gate 404 the entire application on a live port, which is an outage that starts silently.

**⚠️ AC-46 is left open deliberately, and "shorten the leaf" is the wrong hedge.** `CertificateProvisioner`
mints a 5-year leaf; Apple caps TLS server certificates at 398 days but exempts certificates chaining to a
**user-installed root**, which is exactly this case. The exemption should hold — but that is a judgement, not a
verification, and verification needs a physical iPhone. What makes the decision easy is that the plan's
fallback is **not safely implementable as a standalone change**: `TryLoadExisting` checks only that the PFX
opens, never its expiry, and the CA's **private key is never persisted** (only the public `ca.crt` is exported).
So a 398-day leaf would expire in ~13 months with nothing able to renew it, and the only available
regeneration path re-mints the **CA**, which breaks trust on every device that already installed it. Shortening
the lifetime therefore requires CA-key persistence plus leaf-only renewal first — real work, outside P8. The
5-year leaf stays, the reasoning is in `packaging/README.md`, and AC-46 stays open rather than being quietly
claimed.

**⚠️ The fourth failure state the spec does not name (plan step 5, now documented).** SANs are captured from
`Dns.GetHostAddresses` **at generation time** and the certificate is then reused idempotently, so a **DHCP
lease change** gives the server an address the certificate does not claim — and HTTPS fails on every device
*even though the CA is correctly installed*, which is the most confusing possible symptom. Fix is a static or
reserved lease. Also documented: **IPv4 only, no `.local`/mDNS**. And a precision the spec's first state gets
slightly wrong — the HSTS block only bites if an operator set `Security:EnableHsts`, which defaults to
**false** in Local mode.

**Smaller decisions worth keeping.** The `.mobileconfig`'s two UUIDs are **derived from the CA** rather than
random: iOS keys a profile on its UUID, so random ones would let a second download stack a second root nobody
can tell apart, while derivation makes a re-download replace in place *and* makes a regenerated CA correctly
read as a new profile — which is what the stale-CA failure state depends on. The page's CSS lives in its own
non-interpolated constant because CSS is almost all braces and escaping every pair inside an interpolated raw
string is a transformation that silently corrupts the rules if one pair is missed.

**Not done.** AC-46 (needs hardware). Everything else in P8 is complete.

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

### DEV-9: AC-35 was already satisfied — the plan's step 4 rested on a wrong premise
**Date:** 2026-07-31 · **Story:** 1 (P6) · **Category:** Scope · **Approved:** **Yes — asked and confirmed**

**Original plan:** P6 step 4 — *"The Diagnostics tab adopts the two-channel popover (AC-35).
`odontogram.tsx:453-478` still wraps its per-tooth condition list in a hover/focus-only `Tooltip`; the
`476a2e3` fix was never applied there, so **the one place a tooth's charted diagnoses appear is unreachable by
touch**."*

**Actual implementation:** no change. AC-35 — *« a tooth's charted diagnoses are reachable by touch on the
Diagnostics tab »* — is already met.

**Justification:** the claim is checkable and false. `ToothCell`'s trigger is a real `<button>` inside a
`PopoverTrigger`, so a **tap opens the editor Popover** — and that Popover already lists every entry in more
detail than the tooltip does: condition, the Diagnostic/Réalisé badge, the date, the faces, the note, and the
« Retirer ce diagnostic » control. The `Tooltip` is a **redundant hover shortcut for mouse users**, not the
only surface. Applying the two-channel pattern literally would have made the **editor form** open on every
tooth the pointer crossed — which the file's own comment (added by an earlier part) says was the reason the
acts chart's pattern was *deliberately not* reused there: *"Here the Popover IS the editor … opening it on
hover would pop a form open for every tooth the pointer crosses."*

**Impact:** none on the AC, which passes as written. Recorded because a plan step was declined on evidence
rather than skipped, and because the plan's own text is now known to be wrong on this point — anyone
re-reading § Part P6 step 4 should read this entry with it.

### DEV-8: the Semaine density « strip » is seven rows, not seven columns
**Date:** 2026-07-31 · **Story:** 1 (P5) · **Category:** Technical · **Approved:** auto (see justification)

**Original plan:** P5 step 3 / AC-28 — *"Semaine → a 7-day density **strip** below `md:`, tappable into a day."*

**Actual implementation:** a vertical list of seven day rows — weekday + date, colour dots, the day's count and
its first appointment time, each row tapping into Jour.

**Justification:** the word implies a horizontal band, and that shape fails at the width it exists for. Seven
cells across 320 px is ~45 px each, which holds dots and nothing else — precisely the unreadable sliver that
made the month *chips* unusable and got them replaced with dots two commits earlier. Worse, it replaces the
time grid, so a ~100 px band at the top would leave the rest of the screen empty. Rows use the width the phone
actually has, which is what lets each day state a count and a first time rather than only a density.

**Impact:** presentation only, inside one component; `onSelectDay` and the accessibility shape are unchanged.
AC-28's testable content — *seven days, density, tappable into a day* — is met. Flagged because the plan named
a shape and this is a different one.

### DEV-5: the bottom sheets are CSS on the existing primitive, not `vaul`
**Date:** 2026-07-31 · **Story:** 1 (P4) · **Category:** Technical · **Approved:** **Yes — asked and confirmed**

**Original plan:** P4 step 3 — *"Bottom sheets via `vaul` for the 26 `AlertDialogContent` confirmations and the
light dialogs. Use `handleOnly` … and `repositionInputs`."*

**Actual implementation:** no `vaul`. Radix's dialog **is** the sheet primitive — `ui/sheet.tsx` imports the very
same `@radix-ui/react-dialog` — so the sheet presentation is a class list on the node that already exists.
`DialogContent` gained a `mobile` prop (`"bottom"` | `"sheet"`) and `AlertDialogContent` reuses the same exported
class constants.

**Justification:** four things fall out of it that the `vaul` route does not give. (a) **AC-24 becomes true
everywhere, not just for the six heavy dialogs** — there is one element, so crossing a breakpoint cannot
remount and cannot lose typed input; `vaul` has no centred-dialog mode, so any dialog using it would have needed
a `useMediaQuery` swap, which is exactly what AC-24 forbids. (b) **All 26 `AlertDialogContent` instances across
20 files were fixed by one edit** instead of 20 conversions. (c) **R-12 disappears** — no `vaul` 500 ms default
to override against `sheet.tsx`'s deliberate 300 in / 200 out on `ease-panel`. (d) **No swipe channel** for the
dirty guard and the post-visit `handleLater` to each have to cover — step 8 names that bypass as a hazard.

**Impact:** ⚠️ **AC-22's « in addition to swipe » is not met.** Dismissal is the ≥ 44 px control + `Escape` +
outside tap + the back gesture. This is a real, accepted gap, recorded rather than quietly claimed — drag-to-
dismiss needs `vaul` or a hand-rolled drag, and the user chose the CSS route knowing the cost.

### DEV-6: `use-dirty-guard.ts` lives in `lib/hooks/`, not `hooks/`
**Date:** 2026-07-31 · **Story:** 1 (P4) · **Category:** Technical · **Approved:** auto (trivial)

**Original plan:** the story's file table says `web/hooks/use-dirty-guard.ts`.

**Actual implementation:** `web/lib/hooks/use-dirty-guard.ts`.

**Justification:** there is no `web/hooks/` directory. Every hook in this project is under `lib/hooks/`, including
`use-media-query.ts` which P2 added for exactly this kind of need. The project convention wins over a plan's
incidental path.

**Impact:** none beyond the import path.

### DEV-7: dirtiness is observed from DOM events, not declared per form
**Date:** 2026-07-31 · **Story:** 1 (P4) · **Category:** Technical · **Approved:** auto (see justification)

**Original plan:** P4 step 5 — *"Add a shared `useDirtyGuard` and wire it into the heavy sheets on every
channel."* Silent on how dirtiness is determined.

**Actual implementation:** the hook listens for `input`/`change` at the document, counting only events whose
target is inside the open dialog content, rather than taking an `isDirty` boolean from each form.

**Justification:** the declared version needs each of the five heavy forms to derive "has the user typed
anything" from its own state — five implementations of one question, and every field added later is a chance to
forget one, producing a guard that silently stops guarding. Observing the events is the browser telling us the
user typed. ⚠️ It deliberately does **not** diff against initial values, so re-typing the original value still
counts as dirty: that errs toward asking, and a needless confirm costs a tap while a missed one costs the
visit's notes.

**Impact:** one behaviour worth knowing — only the **root** `onOpenChange` and the « Annuler » buttons route
through the guard. Every save path calls the raw `onOpenChange` prop, so a successful save closes without a
prompt with no `markClean` bookkeeping to keep in step.

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
- **When two presentations must share state, look for a primitive that is already both.** P4's plan called for
  `vaul` bottom sheets, which would have meant a `useMediaQuery` swap and therefore a remount — the exact thing
  AC-24 forbids. Radix's dialog and shadcn's `Sheet` turned out to be **the same import**, so the responsive
  behaviour became a class list on one node: no second library, no remount, and 26 call sites fixed by one edit.
  The question to ask before adding a dependency for a second presentation is *"is the thing I already have
  capable of both?"*
- ⚠️ **`min-h-0` is what makes a scrolling flex body work.** A flex item's default `min-height: auto` refuses to
  shrink below its content, so a `flex-1 overflow-y-auto` body silently pushes the footer off screen instead of
  scrolling. It looks like a sticky-footer bug and is a flexbox default.
- **A cap belongs in a different tailwind-merge group from the thing it caps.** The popover fix is a `max-w` on
  the primitive while callers set `w-*`: different groups, so it survives every override. Had it been a `w-*` it
  would have raced them — the same collision AC-20 spent 28 sites fixing.
- **Filtered staging is the answer to a shared file, not "commit it all" or "commit none".**
  `git diff -U0 | awk '<keep matching hunks>' | git apply --cached --unidiff-zero` stages your hunks out of a
  file that also holds someone else's uncommitted work. Without it the choice is committing their feature or
  leaving your own gate red — here, four unprefixed `max-w-md` that `dialog-max-w` fails on.
- ⚠️ **A percentage in an absolutely-positioned overlay resolves against the VISIBLE box, not the scrollable
  content.** The agenda's week blocks are sized `(100% - 60px) / 7` as children of a scroll container. That is
  correct only while `overflow-x-hidden` keeps the two widths equal — allowing horizontal scroll would put every
  block in the wrong column, with nothing failing and no check catching it. Before making any container
  scrollable, ask what inside it is sized in percentages.
- **A sticky offset only sticks to a scrollport that scrolls on that axis.** `sticky left-0` inside a
  vertically-scrolling child of a horizontally-scrolling parent does nothing. If you need both a sticky row and
  a sticky column, **one** element has to scroll both axes.
- **Making a container scrollable can silently break what is positioned inside it — and the fix is a wrapper,
  not a rewrite.** The agenda's whole AC-30 restructure came down to inserting one `relative w-max` div so the
  overlay's percentages measure the grid instead of the viewport. The `calc()` strings, the `HOUR_HEIGHT`
  invariant and every band expression stayed byte-identical. When a layout depends on maths you must not
  disturb, look for the change that *moves the containing block* rather than the one that edits the maths.
- ⚠️ **An arithmetic contract between two files needs a check, because neither side looks wrong alone.**
  `HOUR_HEIGHT = 48` and `repeat(7, 96px)` are each perfectly reasonable numbers; only together do they satisfy
  `(100% - 60px) / 7`. `agenda-scroll` reads both out of the source, so changing one is a failure rather than a
  drift nobody sees until blocks are a few pixels off per column.
- **Moving a sibling into a scroller invalidates every offset measured from that scroller.** `scrollTop =
  8 * HOUR_HEIGHT` was correct only while the grid started at the container's top. Prefer asking the DOM where
  a landmark actually is (`querySelector('[data-time-slot="08:00"]').offsetTop`) over arithmetic that encodes
  an assumption about what is stacked above it.
- ⚠️ **`justify-center` inside a scroll container makes content UNREACHABLE, not merely off-centre.** The
  overflow goes to both sides and the inline-start half is outside the scrollable region — no gesture reaches
  it. `w-max mx-auto` is the scroll-safe equivalent: it centres while there is room and collapses to the
  scroll origin when there is not. This is the second time this feature has hit *"a percentage or an alignment
  measured against the wrong box"* (the agenda's overlay was the first) — when something is inside an
  `overflow-*` container, ask what its geometry is measured against.
- **Two copies of a value in DIFFERENT SHAPES are still one drift risk — derive, don't re-list.** The FDI
  teeth existed as a flat list in one file and quadrant objects in two others, so they could not be compared
  by eye and nothing tied them together. Making the quadrants the source and *flattening* to get the list
  turns "they happen to agree" into "they cannot disagree".
- ⚠️ **Check a plan step's factual premise before implementing it — especially when the file already argues the
  opposite.** P6 step 4 asserted the Diagnostics diagnoses were « unreachable by touch »; they were not, and
  `odontogram.tsx` carried a comment from an earlier part explaining exactly why the pattern the plan wanted
  had been avoided there. Implementing it literally would have opened an editor form on every tooth the
  pointer crossed. A plan is a hypothesis about the code, and the code is the authority.
- **An extraction is safe when it takes LESS than you expect.** `ToothArchLayout` works precisely because it
  refuses `paint`, `disabled`, `entries` and every scrap of open/hover state — the three charts differ only in
  how they draw and react to one tooth, so the shared part is the geometry and nothing else. The temptation to
  hoist "just the disabled flag too" is what would have broken both contracts at once.
- ⚠️ **A timeout enforced only by a running timer is not enforced.** `setTimeout` is a *hint* — a backgrounded,
  frozen or suspended tab may never deliver it — so any deadline that matters must store the **wall-clock
  instant** it was set from and re-derive on every wake-up. The failure mode here was silent and in the wrong
  direction: the session stayed open *longer* than the policy, on exactly the device (a locked phone) the
  policy exists for. Ask of any timer: *what happens if this callback simply never runs?*
- **Distinguish « retry will work » from « retry cannot work » before offering a retry.** `status === 0` looked
  like the network predicate and was not — it also covers an unexpected throw. A « Réessayer » on a fault is a
  loop the user cannot win, so the retryable case earned its own code rather than borrowing a status.
- ⚠️ **A library default that is merely *wrong* rather than *invalid* fails silently.** next-themes'
  `attribute="data-theme"` would have written a real attribute, made the toggle work and reported the right
  `resolvedTheme` — while applying none of 336 class-based `dark:` utilities. Nothing to catch: no error, no
  type failure, no check. When wiring a library to an existing convention, verify the *convention* (here:
  `globals.css`'s variant selector) rather than trusting the library's default to match it.
- **`revokeObjectURL` on the next line is a race, and it only loses where you cannot see it.** The synchronous
  revoke worked on every desktop and broke exactly the two asynchronous consumers — iOS's blob viewer and
  `window.open` — with no error either time. Handing a URL to something that will read it *later* means
  revoking later too.
- **An empty list can be the honest value.** `manifest.ts` ships `icons: []` because the four declared files do
  not exist: listing them would have produced a manifest that passes every validator while installing a blank
  tile. A placeholder would have hidden a real gap behind a green check.
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
