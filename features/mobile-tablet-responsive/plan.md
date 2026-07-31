# Implementation Plan: Mobile & tablet — the app on every device

**Status:** APPROVED
**Created:** 2026-07-30
**Spec:** `features/mobile-tablet-responsive/spec.md` (APPROVED, 51 ACs, 8 phases)
**Exploration:** `features/mobile-tablet-responsive/exploration.md`, plus a second implementation-level pass
recorded inline below
**Scope:** FE (`web/`) + packaging/API for P8. No backend business logic, no DB migration, no API contract change.

## Overview

The spec's eight phases become **one story worked in eight ordered parts**. Everything below is grounded in a
second exploration pass; where it corrects the spec, the correction is called out and the spec's number is
treated as wrong.

### Corrections to the spec, verified

| Spec says | Actually | Consequence |
|---|---|---|
| 22 tables | **27 `<Table>` render sites** | The plan names the 22 and argues the 5 exclusions (P3) |
| 32 `AlertDialogContent` | **26**, across 20 files | AC-21's count; no behaviour change |
| 121 `text-[Npx]` | **116** | AC-2's target list |
| 24 pages duplicate the shell | 24 **files**, **30 shell instances** (6 files have loading/not-found copies) | P1 touches 30 sites, not 24 |
| `lg:` used 6× | **5** — `ui/button.tsx:41`'s `lg:` is a CVA size key, not a breakpoint | Nothing; the point stands |
| The viewport export is Phase 07 | **It is a P1/P2 prerequisite** — `env(safe-area-inset-bottom)` evaluates to `0px` without `viewportFit: "cover"`, so the bottom bar would sit under the home indicator | Moved to P1 |

### Approach decisions (settled in the interview, not open)

1. **`AppShell` is a component, not a route-group layout.** Four shell instances render *outside* `ClinicGuard`
   deliberately — `app/treatment-plans/[id]/page.tsx:57-58` says why: *"the guard would otherwise show its own
   spinner over a page that has already failed, and the user would never see why."* A layout would put the guard
   above those states. AC-3 says "component". A layout stays available as a later feature.
2. **One hinge at `md:` (768 px).** The spec's `sm:` for the bottom bar would leave 640–767 px with no rail, no
   bar and no hamburger. Bar + drawer below `md:`, rail at `md:` and up — unchanged from shipped US-P3b.
3. **`CardList` is a sibling primitive**, and `Table` gains only `containerClassName`. All 27 tables pass raw
   `<TableHeader>`/`<TableBody>` children, so a card tree cannot be derived from them without a column-model
   refactor of every call site and every custom cell.
4. **`vaul` for bottom sheets only.** It brings swipe-to-dismiss, `handleOnly` and — load-bearing for AC-25 —
   `repositionInputs`. The nav drawer and full-screen sheets stay on the existing `sheet.tsx`, whose 300/200 ms
   `ease-panel` timing is deliberate and must not regress to vaul's 500 ms.
5. **No new breakpoint tokens.** Verified against the installed `tailwindcss@4.1.18`: the spec's four states are
   exactly the stock `sm`/`lg`/`xl` boundaries. AC-1 is met by *using* them.
6. **Mechanical checks are a committed node script**, not ESLint — repairing lint on a codebase that has never
   linted is its own project.
7. **A dedicated LAN HTTP listener for the trust page.** Port 5000 stays `ListenLocalhost`.

### Four exploration findings that change the work

- ⚠️ **`--breakpoint-*` in Tailwind v4 *adds*; it does not replace.** Verified in the merge implementation
  (`node_modules/tailwindcss/dist/chunk-CT46QCH7.mjs`): a new key is a `Map.set` into the populated default
  theme; only the explicit wildcard `--breakpoint-*: initial` clears it. **The real risk is redefinition** —
  `--breakpoint-md: 52rem` silently re-points all 75 `md:` utilities. Decision 5 avoids the question entirely.
  Also: the sort comparator compares the *unit string* before the number, so `"px" < "rem"` and any px-valued
  breakpoint sorts before every rem one regardless of magnitude. If a token is ever added, it must be `rem`.
- ⚠️ **`next-themes` defaults to `attribute="data-theme"`.** `globals.css:4` declares
  `@custom-variant dark (&:is(.dark *))` — class-based. Mounting the provider without `attribute="class"` writes
  `data-theme="dark"` and **not one of the 336 `dark:` utilities fires**. It looks wired and does nothing.
  `suppressHydrationWarning` on `<html>` is also required (the pre-hydration script mutates it).
- ⚠️ **Four download paths bypass `downloadBlob`** and inline the same `a.download` dance:
  `invoices-table.tsx:207-224` (note d'honoraires), `:251-269` (**the El Fatoora XML the spec names**),
  `patient-files-manager.tsx:236-258`, `document-editor-content.tsx:1323-1330`, plus `:1268-1270` using
  `file-saver`. AC-41 says *every* artefact, so a fix confined to `download.ts` misses five of thirteen.
- ⚠️ **`QRCoder` is already a dependency.** `IQrCodeGenerator`/`QrCodeGenerator` is registered Singleton
  (`Infrastructure/Extensions.cs:225`) and used by the El Fatoora invoice PDF. P8 needs **no new package**, and
  the trust QR must be server-rendered anyway — the page has to render before the device trusts the server.

## Story shape

The user chose **all eight phases in one plan**. This plan honors that and does not re-propose a split. The story
is worked through in **eight ordered, dependency-respecting parts**, each a vertical increment ending in a
working, committable state. `/implement-story` should land and commit part by part; a part boundary is the
natural split point if the story proves too large in one session (risk **R-1**). This matches the
`audit-sections-3-to-10` precedent exactly.

| Part | Covers | Spec ACs | Verifiable by | Depends on |
|---|---|---|---|---|
| **P1** Foundations + `AppShell` | Phase 01 | AC-1…AC-6, AC-36 (viewport only) | `tsc` + `build` + walk | — |
| **P2** Nav, touch, bottom token | Phase 02 | AC-7…AC-12 | `tsc` + `build` + walk | **P1** |
| **P3** Tables → `CardList` | Phase 03 | AC-13…AC-19 | `tsc` + `build` + walk | **P1** |
| **P4** Dialogs | Phase 04 | AC-20…AC-27 | `tsc` + `build` + walk | **P2** (token), **P3** (2 in-dialog tables) |
| **P5** Agenda | Phase 05 | AC-28…AC-31 | `tsc` + `build` + walk | **P1** |
| **P6** Odontogram | Phase 06 | AC-32…AC-35 | `tsc` + `build` + walk | **P1** |
| **P7** Platform | Phase 07 | AC-36…AC-43 | `tsc` + `build` + dark walk | **P2** (token), **P1** (viewport) |
| **P8** LAN device trust | Phase 08 | AC-44…AC-46 | `dotnet vstest` + **physical devices** | last |
| | Cross-cutting | AC-47…AC-51 | script + walk | continuous |

**Ordering rules, load-bearing:**

- **P1 is first and blocks everything.** Every later part edits the shell or a primitive P1 creates.
- **P2 before P4 and P7.** P2 defines the single bottom-offset token; the dialogs' bottom sheets, the toasts and
  the AI panel all consume it. Defining it twice is how they drift.
- **P3 before P4.** Two of the 22 card conversions live *inside* dialogs (`patient-summary-modal`,
  `invoice-detail-modal` is excluded but `patient-summary-modal` is not) — `patient-summary-modal` is the one
  that is *clipped, not scrollable* today, and P4 rewrites the same file's `DialogContent`.
- **P7 late**, so the dark walk runs against final layouts rather than mid-flight ones.
- **P8 last.** AC-46 requires a physical iPhone and Android tablet; nothing in this environment substitutes.
  Sequencing it last means the other seven parts never block on device access.
- P5 and P6 are independent of P2/P3/P4 and may be reordered among themselves.

## Conventions every part must follow

Extracted from the codebase; these are how this repo works, not choices.

### The frontend quality gate

`web/` has **no test runner, no working ESLint** (`eslint` + `eslint-config-next` are declared but not installed,
so `npm run lint` fails on a clean install; `next.config.ts` sets `eslint: { ignoreDuringBuilds: true }`), **no
visual-regression tooling and no CI** (`.github/` does not exist). Per `features/LEARNINGS.md`, the gate is
`npx tsc --noEmit` + `npm run build`, both clean — plus this feature's two additions: the mechanical script and
the documented manual walk.

⚠️ **`tsc` does not flag an unused destructured binding.** Removing the header hamburger (P2) leaves
`setMobileOpen` and the `Menu` import dead in `dashboard-header.tsx` and **nothing catches it**. Every part that
removes a call site must grep for the symbols it orphaned — the `features/LEARNINGS.md` entry *"Grep for every
symbol a namespace provides before dropping its import"* applies verbatim.

### Guard tests that fail the build

| Guard | Auto-covers new code? | What P8 must do |
|---|---|---|
| `ControllerAuthorizationCoverageTests` | **Yes — fully reflective**, and it pins the set by **equality** in both directions | The trust endpoints are `[AllowAnonymous]`, so each must be added to `ExpectedAnonymous` (`:18`, entries around `:30`) with a reviewed comment, or **the build breaks**. The `Connectivity.Get` entry is the precedent. |
| `CertificateProvisionerTests` | Pins the CA's actual CN | If P8 changes the leaf lifetime, assert the new value here |

### Frontend conventions

- **New primitives**: `cd web && npx shadcn@latest add drawer`. ⚠️ The CLI emits the `radix-ui` **umbrella**
  import; this repo uses the scoped packages. `web/components/CLAUDE.md` records that `sheet` and `radio-group`
  both needed hand-fixing for exactly this. Same treatment.
- **`vaul/style.css` is not auto-imported** by the ESM entry in 1.1.2. Either import it once or reimplement — and
  if imported, override the 500 ms curve, because `sheet.tsx:63-66` deliberately argues for 300 in / 200 out.
- **Label maps** are `.ts`, `Record<string, string>`, every accessor `MAP[k] ?? k`.
- **One shared constant for a policy value enforced in multiple places** (`features/LEARNINGS.md`). This applies
  to the bottom-offset token, the 44 px floor and the `HIDDEN_PATHS` list.
- **Guard browser globals.** `document.visibilityState` and `matchMedia` are used for the first time in this
  feature; every module holding them can be imported server-side. Use
  `typeof document !== "undefined"` guards.

### Edit-risk ranking for the most-touched files

| File | Lines | Risk |
|---|--:|---|
| `components/document-editor-content.tsx` | 2623 | **Highest.** Branches on `documentType` in ~20 places; owns the only print path, two A4 previews, two ungated `w-[384px]` popovers and its own download |
| `app/patients/[id]/page.tsx` | 2160 | High by size; **4 of the 22 tables**, two shell instances outside the guard, the expand/collapse notes cell |
| `components/appointment-calendar.tsx` | 1002 | **High.** `HOUR_HEIGHT` is load-bearing with a documented drift invariant; zero breakpoint classes today |
| `components/edit-patient-dialog.tsx` | 1276 | High — 6 sections, the `max-h-[calc(90vh-200px)]` magic number, the `max-w-4xl` flagship |
| `components/treatment-plans/plan-workspace.tsx` | 852 | Moderate — two tables, but rows live in `plan-act-row.tsx` |
| `components/odontogram.tsx` | ~540 | Moderate — `ToothCell` owns the editor popover |
| `components/dashboard-sidebar.tsx` | 252 | Low, but it is where the nav data must be exported from |

---

# US-1: The app works, and looks finished, on every device

**As a** dentist at the chair with a tablet, a secretary at the front desk, and an owner checking the day from a
phone
**I want** every screen to fit the device I am holding, every control to be reachable with a thumb, and every
document to arrive
**so that** the software is not a desktop application I am borrowing on the wrong machine.

---

## Part P1 — Foundations and one `AppShell`

**Delivers:** every page rendered by one shell, one gutter, one width rule, correct viewport height on iOS, a
skip-link, and a type scale. Nothing looks different on a desktop; a phone stops being clipped at the bottom.

1. **`web/app/layout.tsx`** — add the missing `viewport` export: `width: "device-width"`, `initialScale: 1`,
   `viewportFit: "cover"`, `interactiveWidget: "resizes-content"`. Without `viewportFit`,
   `env(safe-area-inset-bottom)` is `0px` and P2's bar sits under the home indicator. Add
   `suppressHydrationWarning` to `<html>` now (P7 needs it; adding it here avoids a second edit).
2. **`web/app/globals.css`** — add the `--text-*` steps to the existing plain `@theme` block (lines 167–171),
   **not** `@theme inline` (that block exists so colour utilities emit literal values for the runtime
   light/dark swap; sizes do not swap). Each added size needs its paired `--text-<name>--line-height` or the
   utility emits a font-size with no leading. Add a comment naming the four device states and the stock
   breakpoints they map to — no `--breakpoint-*` tokens.
3. **Replace all 116 `text-[Npx]`** with scale utilities. **Primitives first** — `ui/table.tsx:119` (`10.5px`
   head) and `:182` (`TableMeta` 11px) reach 22 tables; `ui/page-header.tsx:47` (`26px`) reaches 11 pages;
   `ui/list-toolbar.tsx:90`. Then the 17 below the floor (all `8px`/`9px` — tooth numbers in all three charts,
   the « +N » counts, the « non synchronisé » badge, the Push-to-Google label), then the 53 `10px` and 34 `11px`.
   **No information-bearing text below 11 px** (AC-2).
4. **Create `web/components/app-shell.tsx`.** Props, derived from the 30 instances' real divergence:
   `width?: "7xl" | "5xl" | "3xl" | "wide" | "none"` (default `"7xl"`), `gutter?: boolean` (default `true`),
   `mainClassName?: string`, `children`. It renders
   `<div className="flex h-dvh bg-background"><DashboardSidebar/><div className="flex flex-1 flex-col overflow-hidden"><DashboardHeader/><main …>{children}</main></div></div>`.
   It does **not** render `ClinicGuard` — pages keep it.
5. **Convert all 30 shell instances.** Resolve the divergence:
   - Gutter: the 5 flat-`p-4` pages (`/appointments`, `/cnam-nomenclature`, `/dental-acts`, `/medications`,
     `/procedure-types`) adopt `p-4 md:p-6`; `/settings` and `/users` gain the gutter and the `max-w-7xl`
     wrapper they lack (AC-3 says they lose their exemption).
   - Overflow: `overflow-auto` (6 sites) → `overflow-y-auto`. **`/appointments` keeps `overflow-hidden` and its
     `h-full flex flex-col` wrapper** via `mainClassName` — the calendar needs a bounded flex column. This is
     the one genuine escape hatch.
   - Width: `max-w-[1400px]` (`/appointments`) becomes the named `"wide"` variant. `/creances` `5xl` and
     `/mon-profil` `3xl` keep theirs. `/documents`' nested `max-w-6xl` grid (`:89`) stays — it is inside the
     content, not the shell.
   - Spacing: `space-y-6` is the shell's default; `/` keeps `space-y-8` and `/rappels` keeps
     `flex flex-col gap-6` via children, not the shell.
   - ⚠️ Four instances render **outside** `ClinicGuard` (`patients/[id]:476`, `:504`,
     `treatment-plans/[id]:62`, `:72` via its local `Shell` helper at `:101-111`). They use `AppShell` too; the
     local `Shell` helper is deleted and replaced by it.
6. **`h-screen` → `h-dvh`.** 30 shell instances plus `dashboard-sidebar.tsx:201`'s `<aside>` — ⚠️ these must
   change **together**, or the rail and the shell disagree on height and the app grows a second scrollbar (the
   reason `overflow-hidden` is on the aside is documented at `:189-198`).
7. **Skip-link and landmarks** (AC-5). `AppShell` renders a « Aller au contenu » link as its first focusable
   child, targeting the `<main>`'s id. Disambiguate the two elements currently both named
   « Navigation principale » (`dashboard-sidebar.tsx:167` the `<nav>`, and `:233` the `SheetContent`).
8. **Hoist the nav data.** Create `web/lib/nav.ts` exporting `NavItem`, `NavSection`, `baseSections` and the
   `configItems` builder (currently module-private in `dashboard-sidebar.tsx:16-59,88-99`), plus `HIDDEN_PATHS`
   (currently private in `ai-chat.tsx:39`). P2's bar consumes both; duplicating either is how they drift.
9. **Logical directional utilities** (AC-6) in everything this part rewrites.

**Verifiable:** `tsc` + `build` clean; every route renders identically at 1440 px; on an iOS-sized viewport the
bottom of every page is reachable; Tab from the top of any page reaches « Aller au contenu » first.

---

## Part P2 — Navigation, touch targets, and the bottom edge

**Delivers:** a thumb-reachable bottom bar, one owner for the bottom edge, and 44 px targets on touch.

1. **`web/components/bottom-nav.tsx`**, rendered by `AppShell` as a **flex sibling of `<main>`, below `md:`** —
   *not* `fixed`. As a flex item, `<main className="flex-1 overflow-y-auto">` shrinks around it automatically,
   which is exactly what EC-3 (a ~250 px content height in landscape) needs and what a fixed bar would break.
   Four destinations from `lib/nav.ts` — Tableau de bord · Rendez-vous · Salle d'attente · Patients, all four
   already in `baseSections[0]` — plus « Plus », which calls `setMobileOpen(true)`.
2. **Remove the header hamburger** (`dashboard-header.tsx:200-208`) below `md:`; « Plus » becomes the drawer's
   only trigger. This **supersedes AC-P3.12's wording** and the plan says so. ⚠️ Then grep for the orphaned
   `setMobileOpen` destructure (`:33`) and `Menu` import (`:27`) — `tsc` will not flag either.
   No new state: « Plus » reuses `isMobileOpen`, so AC-P3.18 still holds and the existing
   `useEffect(… , [pathname])` at `sidebar-context.tsx:40-42` still closes the drawer on navigation.
3. **The bottom-offset token.** Declare `--bottom-inset: calc(var(--bottom-bar-h) + env(safe-area-inset-bottom))`
   in `globals.css`. The bar itself pads with `env(safe-area-inset-bottom)`. Everything `fixed` that is not in
   the flex flow consumes the token below `md:`:
   - `ai-chat.tsx:700` (FAB) and `:719` (panel — **this is shipped AC-P3.16 geometry; the plan re-opens it
     deliberately and restates it**)
   - the Sonner `<Toaster>` — and it moves to bottom-anchored on a coarse pointer, capped at 3 (AC-9). Note
     Sonner injects at `z-index: 999999999`, above every `z-50` in the app.
   - P4's bottom sheets.
   Every other `fixed` in the app is `z-50` — 8 sites, no other value. The plan states the z-order explicitly.
4. **The bar hides while a full-screen sheet is open** (AC-8). The bar is not a descendant of the sheet, so the
   signal is a `data-sheet-open` attribute on `<body>` written by the sheet wrapper — Radix already sets
   `data-scroll-locked` there, so this follows an existing idiom. It does **not** go in `SidebarContext`, which
   is persistence-adjacent.
5. **EC-1 — the drawer open across a breakpoint.** Nothing in the repo reads viewport width in JS. Add a
   `matchMedia` listener (guarded for SSR) that closes `isMobileOpen` when the layout crosses to the rail. Today
   `SheetContent`'s `md:hidden` hides the *content* while Radix's overlay, scroll lock and focus trap stay
   mounted — a live bug.
6. **44 px hit areas on a coarse pointer** (AC-10). Add a `@custom-variant coarse (@media (pointer: coarse))`
   beside the existing `hover-hover` and apply to the **tappable area**, not the paint, so desktop density is
   untouched. Targets: every `Button` variant (36/32/40), `Input`/`SelectTrigger` (36), `SelectItem` and menu
   items (~30), `Checkbox`/`RadioGroup` (**16**), the `Dialog`/`Sheet`/`AlertDialog` close buttons (**16 px
   icon, zero padding**), sidebar rows (32), `react-day-picker` cells (`--cell-size: 32px`),
   `data-table-pagination`'s four icon buttons (32), `FilterChip` (~30).
7. **Hover-revealed affordances get a touch path** (AC-11) — and these are **two different rules**:
   - *Movement* hovers gate behind `hover-hover:` per its stated policy: `odontogram.tsx:431`,
     `record-tooth-chart.tsx:152`, `patient-files-manager.tsx:498`, `procedure-type-form-modal.tsx:320`.
   - *Revealed* affordances get a real touch path: `patient-files-manager.tsx:516` (the file **delete** button is
     `opacity-0 group-hover:opacity-100` — invisible and un-tappable on touch), `clinic-settings.tsx:713` and
     `mon-profil-content.tsx:186` (logo/cachet replace overlays), the connectivity badge's tooltip-only text.
   ⚠️ **Applying the first rule to the second class makes the affordance permanently invisible on touch** — the
   `features/LEARNINGS.md` « space-based UI gating can hide a required affordance entirely » failure.
8. **`textarea.tsx:13` and `time-field.tsx:75`** get `text-base md:text-sm` (AC-12). `Input` already has it; these
   two do not, so focusing them zooms iOS and never zooms back.

**Verifiable:** at 390 px the bar is present and thumb-reachable; opening a sheet hides it; the AI FAB clears it;
rotating an open drawer past 768 px leaves no stuck overlay; every control is ≥ 44 px tappable on a touch device
and unchanged with a mouse.

---

## Part P3 — Tables become cards

**Delivers:** no horizontal scroll on a phone, on 22 surfaces, with field labels a screen reader can hear.

1. **`web/components/ui/card-list.tsx`** — a semantic `<ul>`/`<li>` list (AC-14: the `<table>` is *absent* below
   `md:`, not reflowed). Props: `items`, `title`, `subtitle?`, `status?`, `fields` (label/value pairs),
   `actions?`, `accent?`, `onSelect?`, `empty`, `loading`, `skeleton`. Content contract per AC-17/AC-18:
   a long title truncates to one line with the full value on tap; **a field with no value is omitted**, not
   « — ».
2. **`Table` gains `containerClassName`.** Its scroll container is a hardcoded inner div callers cannot address —
   AC-13's "no table scrolls horizontally at 320 px" is unsatisfiable from a call site without this.
3. **Convert 22 surfaces.** Rule: title = identity → status → money → date; actions to one menu.
   `treatment-plans-table.tsx:277` is the **only** table already using a `DropdownMenu` for row actions — it is
   the template for AC-15; the other 21 render 1–8 inline buttons.

   Per-surface identity, where the rule needs help:

   | Surface | Card title | Note |
   |---|---|---|
   | `factures/invoices-table` (9 col) | `Numéro` | ⚠️ **`"—"` on a draft** — falls back to patient + date. Two statuses (`Statut` + `El Fatoora`); `Encaissé` carries a « −X avoir » subline |
   | `app/lab-orders` (10 col) | `Patient` + `Travail` subtitle | Title alone repeats across a busy list. ⚠️ Its status cell is a **native `<select>`** transition — an action *and* a status; it cannot go in a menu unchanged |
   | `caisse-ledger-table` | `Libellé` | **Exception 1** — `runningBalance` is window-relative, dropped from the card, stated once in the footer. Voided rows keep `opacity-60` |
   | `plan-act-row.tsx` | `Désignation` | **Exception 2** — « séance de N actes » becomes a section header. The selection checkbox and reorder controls are row-level, **not** menu actions. Convert in `plan-act-row.tsx`, not the workspace |
   | `patient-summary-modal` | `Date` + `Type d'acte` | **Exception 3** — adopts the card list at **every** width; its 8 `min-w-*` sum ~760 px inside a dialog with `overflow-x-hidden`, so content is **clipped, not scrollable** today |
   | `patients-table` | `Nom` | `Téléphone`/`Email` render « Non renseigné » — AC-17 says omit instead |
   | `receivables-table` | `Patient` | **No action cell** — the row *is* a link. The card is a link, not a card with a menu |
   | `plan-workspace` échéancier | `Échéance` (the date) | The identity *is* the date here — an argued exception to "date last". Variable-length actions: « Encaisser » plus one « Reçu » per payment |
   | `user-management` | `Nom` (falls back to `-`) | Email is the real identity when `fullName` is blank. ⚠️ `Rôle` is an inline `<Select>` — an action in a data column |
   | `stock-table` | `Nom de l'article` | ⚠️ Row carries a deep-link highlight + a `ref` for scroll-into-view — the card must keep the ref target |
   | `rappels/reminder-log-table` | `Patient` | ⚠️ 2 px status stripe → card accent. `failureReason` is rendered **in-row** on purpose (its comment says a tooltip is unreachable on tablet) and must survive |
   | `patients/[id]` dossiers | `Type d'acte`, date as eyebrow | ⚠️ The `Notes` cell is an expand/collapse with its own state — the most complex cell in the repo |
   | `patients/[id]` rendez-vous | `Date et heure` | Row carries a per-procedure `borderLeft` colour → card accent |
   | `patients/[id]` fichiers | `fileName` | Already the AC-17 truncate case, currently via a hover-only `title` |
   | `cnam-nomenclature`, `dental-acts` | `Désignation` | `Code acte` as a mono eyebrow. Status cell holds 0–3 badges |
   | `procedure-types-table` | `Nom de l'acte` | The colour swatch (col 1) is decoration → card accent, not a field |
   | `caisse` dépenses | `Catégorie` + `Montant` | `Description` is nullable and truncated — a poor title |
   | `waiting-list`, `recurring-series`, `medication-catalog`, `treatment-plans-table` | `Patient` / `Numéro ?? title` | Straightforward |

   **The 5 excluded, argued:** `collected-trend-chart` (2 cols — it *is* a chart's accessible fallback),
   `cnam-letter-values-card` (a form in a table: the value cell is an editable `<Input>`),
   `invoice-detail-modal` lines (4 read-only cols in a dialog), `stock-table`'s mouvements history (4 cols in a
   dialog), and — kept **in** — `patient-summary-modal`, because it is the clipped one the spec names.

4. **13 `colSpan` empty rows** get card-list equivalents, preserving the filter-vs-empty distinction **8 tables
   already draw** (not two, as the spec says). `caisse-ledger-table` already has the only skeleton in the repo
   (`:59`) and `patients/[id]`'s `EmptyOrLoading` helper (`:963`) already draws the distinction — reuse both.
5. **Removable filter chips** (AC-19). ⚠️ `FilterChip` is a **toggle** with `aria-pressed`, not a removable chip;
   it has no dismiss affordance. Add a variant or an `onRemove` prop rather than mis-adopting it.
6. ⚠️ `data-table-pagination.tsx:84` hardcodes `id="page-size"` — two pagers on one page collide. The card
   conversion could double-render; make the id unique.

**Verifiable:** at 320 px, no body-level and no table-level horizontal scroll on any of the 22; a screen reader
announces each field with its label; empty, filtered-empty and loading are three distinct states.

---

## Part P4 — Dialogs

**Delivers:** every dialog at its intended width on desktop, and a real sheet on a phone that does not eat work.

1. **Fix the clamp — 26 of 36 call sites** (AC-20). `DialogContent`'s base is
   `w-full max-w-[calc(100%-2rem)] … sm:max-w-lg`; an unprefixed caller `max-w-*` is the same tailwind-merge
   group so it wins and kills the mobile gutter, while `sm:max-w-lg` is a *different* group, survives, and wins
   at ≥ 640 px. `edit-patient-dialog` asks for `max-w-4xl` and renders at **512 px** today.
   `patient-record-modal.tsx:516-518` already documents the trap for itself — generalise it.
   ⚠️ **17 of the 26 also carry `max-h-[Nvh]`** — the same edit converts those to `dvh`.
   ⚠️ Two sites pass the class as a **template literal** (`patient-files-manager.tsx:662`,
   `patients/[id]/page.tsx:2075`), so the AC-50 check must match `className={\`…\`}` too, not just `className="…"`.
   `AlertDialogContent` (26 sites) has the same base and the same bug.
2. **Full-screen sheets below `md:`** for the heavy dialogs — `edit-patient-dialog`, `treatment-plan-form-modal`,
   `patient-record-modal`, `invoice-form-modal`, `revise-installments-modal`, `patient-summary-modal` — with a
   **sticky** header and footer. ⚠️ `SheetHeader` is `p-4` and `SheetFooter` is `mt-auto` — neither is sticky
   today. `SheetContent`'s `h-full` on left/right is the large viewport (same class of bug as `h-screen`) and
   `h-auto` on top/bottom has **no cap at all**.
3. **Bottom sheets via `vaul`** for the 26 `AlertDialogContent` confirmations and the light dialogs. Use
   `handleOnly` so a scrolling body does not fight the drag, and **`repositionInputs`** — it directly serves
   AC-25. ⚠️ **Never `dismissible={false}`**: it disables drag *and* outside-click *and* Escape, which AC-22
   forbids. The unsaved-work guard intercepts `onOpenChange` instead.
4. **The dismissal contract** (AC-22): a visible ≥ 44 px close control **and** `Escape`, in addition to swipe;
   focus lands on the **title**, not the first field (`autoFocus` off) — autofocusing raises the keyboard over
   the content the sheet was opened to read; focus returns to the trigger, and where P3 replaced that trigger
   with a card, to the card.
5. **Dirty-state guard** (AC-23). No form in the app has one; `grep` for `beforeunload` returns nothing. Add a
   shared `useDirtyGuard` and wire it into the heavy sheets on every channel — swipe, back gesture, outside tap,
   close control.
6. **Presentation changes, the component does not** (AC-24). One component that renders as a dialog or a sheet by
   breakpoint — not two components swapped by a media query, which remounts and loses typed input on an iPad
   Split View resize.
7. **Ungated grids and popovers** (AC-26): 13 unprefixed `grid grid-cols-2` form rows,
   `procedure-type-form-modal.tsx:312`'s `grid-cols-5` swatches, the two `w-[384px]` popovers
   (`document-editor-content.tsx:242,1788`) and the seven `w-80` ones (320 px exactly — at the floor).
   ⚠️ `ui/popover.tsx:33`'s `w-72` default applies to every popover that does not override.
8. **The post-visit prompt is not a sheet** (AC-27). It is mounted in the header, so on all 24 pages, and polls
   every 60 s. ⚠️ Its every dismissal funnels through one `handleLater` and a local `dismissed` flag — added
   *because* deriving visibility from the snooze map alone made the ✕ do nothing. `vaul`'s swipe channel would
   bypass it and the prompt would return on the next poll. On a coarse pointer it degrades to a **toast with an
   action**, and suppresses itself while any dialog or sheet is open.

**Verifiable:** `edit-patient-dialog` measures 896 px at 1440 px wide; at 390 px it is a full-screen sheet whose
footer stays visible with the keyboard open; a swipe on a dirty sheet asks before discarding; rotating mid-form
keeps the data.

---

## Part P5 — The agenda

**Delivers:** a usable day view on a phone without breaking the dashboard's drill-through contract.

1. **Below `md:`, the initial view is Jour.** Initial, not enforced — a view the user picks is kept, including
   across rotation (AC-28). ⚠️ `features/LEARNINGS.md`: a size heuristic must not be the sole gate on an
   affordance.
2. ⚠️ **A drill-through overrides it** (AC-29). `app/appointments/page.tsx:168-201` deliberately forces Mois for
   any `?from=&to=` link — *"the calendar has no arbitrary-range view, so the window is honoured by switching to
   the widest view — month — which is the closest honest rendering of 'the period the card counted'."* Two of the
   fifteen entries in `dashboard-links.ts` route here. Jour must not win over that, or « RDV honorés — Ce mois »
   lands on one day. The two status toggles such a link flips become visible removable chips.
3. **Semaine → a 7-day density strip** below `md:`, tappable into a day. **Mois → dots instead of chips.**
4. **Remove `overflow-x-hidden`** (`appointment-calendar.tsx:881`) and give the week grid horizontal scroll with
   a sticky time gutter. It is the one place in the app that violates AC-P3.14's "wide content scrolls inside its
   own container".
5. ⚠️ **`HOUR_HEIGHT = 48` is not changed.** Blocks are absolutely positioned from it via four `calc()` strings
   (`dayBandLeftExpr`, `weekBandLeftExpr`, …) with a documented invariant that rows must be exactly 48 px or
   blocks drift. The day view's readability comes from the full width, not a shorter hour.
6. **Restructure the toolbar** (prev/next/today + range + 4-item legend + 2 switches in one `flex-wrap` row,
   ~5 stacked rows at 390 px) and give **« Nouveau rendez-vous » a stable home at every width** (AC-31).
   Tapping an empty row in Jour books at that hour, as it already does.

**Verifiable:** at 390 px the agenda opens on Jour and is readable; Semaine and Mois remain reachable; a
dashboard « RDV honorés » link still lands on Mois with a visible filter chip.

---

## Part P6 — The odontogram

**Delivers:** teeth that are tappable and, more importantly, teeth that are *reachable*.

1. ⚠️ **Fix the clipping — this is not a phone-only bug.** All three charts use `flex justify-center` inside
   `overflow-x-auto`. When content overflows, `justify-content: center` pushes overflow to **both** sides and
   **the inline-start overflow is not in the scrollable region** — teeth 18–15 and 48–45 are unreachable at
   390 px by any means. Six sites: `odontogram.tsx:249,267`, `odontogram-acts-chart.tsx:215,225`,
   `record-tooth-chart.tsx:179,190`. Replace with a scroll-safe centring (`w-max mx-auto`, or `justify-start`
   with an inner `w-fit`). ⚠️ `odontogram.tsx:389` and `odontogram-acts-chart.tsx:134` are a **different**
   `justify-center` — they centre a glyph inside its cell and must not be touched by the fix or the check.
2. **Extract `ToothArchLayout`** — the container, two arch rows, midline and labels, which are **byte-identical**
   across the three files today. It accepts `teeth`, `renderTooth: (n) => ReactNode`, `arch`, `onArchChange`,
   `labels?`. It must **not** accept `paint`, `onToggleTooth`, `disabled`, `toothTitle`, `entries`, or any
   open/hover state.
   ⚠️ **Geometry only** (AC-34). Pulling interaction up breaks one of two contracts: the acts chart's
   parent-held `tappedTooth`/`hoveredTooth` (the `476a2e3` fix — *"32 cells a few pixels apart would otherwise
   stack panels as the pointer crosses them"*), or `record-tooth-chart`'s deliberate lack of selection chrome so
   the read-only summary can reuse it with `disabled` (documented at `:122-130`, which is also why it uses a
   native `title` rather than a Radix tooltip — a disabled button fires no pointer events).
3. **One arch at a time below `md:`** with a Haut/Bas control, **44 px** teeth; both arches at tablet portrait and
   up (AC-33). An adult arch needs ~597 px at today's 28 px cells.
4. **The Diagnostics tab adopts the two-channel popover** (AC-35). `odontogram.tsx:453-478` still wraps its
   per-tooth condition list in a hover/focus-only `Tooltip`; the `476a2e3` fix was never applied there, so the
   one place a tooth's charted diagnoses appear is unreachable by touch.
5. **Fold the third FDI copy.** `record-tooth-chart.tsx:6-17` duplicates `ADULT_TEETH`/`CHILD_TEETH`; a fourth
   copy is in `tooth-multiselect.tsx`. One source.

**Verifiable:** at 320 px every tooth in both arches is reachable and tappable in all three charts; the read-only
summary still renders; the acts chart's popover still opens on tap and on hover without stacking.

---

## Part P7 — Platform: install, dark mode, print, delivery, resume

1. **Manifest and icons** (AC-36). ⚠️ All four icons `layout.tsx:20-36` declares — `/icon-light-32x32.png`,
   `/icon-dark-32x32.png`, `/icon.svg`, `/apple-icon.png` — **do not exist**; `web/public/` holds only the five
   untouched `create-next-app` SVGs. Create real assets, add `app/manifest.ts`, `theme-color`, and
   `display: standalone`. Add an in-app back affordance (AC-37) — standalone removes the browser's.
2. **Dark mode** (AC-38/AC-39). A `"use client"` wrapper around `next-themes`' provider, mounted outermost inside
   `<body>` above `SessionProvider`. ⚠️ **`attribute="class"` is mandatory** — the library default is
   `data-theme` and `globals.css:4` is class-based, so the default renders **none** of the 336 `dark:` utilities
   while looking wired. Also `disableTransitionOnChange` (the `*` selector carries transitions, so the whole page
   would cross-fade). The three-way control goes in the existing user `DropdownMenu`
   (`dashboard-header.tsx:333-335`, between « Paramètres » and the local-only password item) as a
   `DropdownMenuRadioGroup` — note the file does not yet import `DropdownMenuRadioGroup`/`RadioItem`.
   ⚠️ It sits inside `{!isLoading && …}`. Sonner takes a `theme` prop and should follow `resolvedTheme`.
   **Document surfaces are exempt** via a `.light` scope: the two A4 previews, the PDF preview iframes, the CNAM
   BS1 overlay, the print surface, and uploaded cachet/logo images.
   ⚠️ `status-tone.ts`'s `--warning-ink` exists *because* `--warning` at L 0.62 is ~3.5:1 on its own wash — a
   dark pass must not "simplify" it away.
3. **Print** (AC-40). There is no `@media print` and no `print:` utility anywhere in the app; the only print CSS
   is a string inside a `window.open` document. Add a print stylesheet hiding the sidebar, header, bottom bar and
   AI FAB. ⚠️ While there, `document-editor-content.tsx:1438` does
   `document.querySelector('style')?.textContent` — **the first `<style>` in the document, whatever it happens to
   be**, which under Next + Tailwind v4 is not a stable handle on the app's CSS (prod ships a `<link>`, not a
   `<style>`). The popup's styling is effectively the inline attributes plus an Arial fallback.
4. **Documents reach the device** (AC-41). On a coarse pointer, open in a new tab with « Partager » where the Web
   Share API supports files. ⚠️ Fixing `download.ts` alone covers **8 of 13** paths — the other five inline the
   dance or use `file-saver`, and one of them is the El Fatoora XML the spec names. ⚠️ `download.ts:10` revokes
   the object URL **synchronously**, which kills a `window.open` navigation the same way it kills the iOS
   download; the two preview flows that hold the URL in state and revoke on close are the precedent to follow.
5. **Resume** (AC-42). ⚠️ `session.tsx:159`'s `reset()` stores only an opaque timer handle, so a frozen tab's
   `setTimeout` never fires and a locked phone stays logged in **past** the limit — a security regression, not
   just an inconvenience. Store `lastActivityAtMs`, and on `visibilitychange` compare against it and re-arm with
   the **remaining** time (today `reset()` restores the full 30 minutes, so any resume event silently extends the
   session). `visibilitychange` is on `document`, not `window`, so it does not fit the existing
   `keyof WindowEventMap` array. Logout is a full `window.location.href` navigation with no `returnTo` — AC-42's
   "returns to the screen they left" needs one added. **Cloud has no inactivity timer at all**, so this is
   Local-only; the plan states that rather than leaving AC-42 reading mode-agnostic.
   Realtime: `clinic-hub.ts:129`'s bare `withAutomaticReconnect()` is **four attempts then `Disconnected` for
   good**, with logging at `LogLevel.None` — a permanent disconnect is completely silent. Add a
   `visibilitychange` re-subscribe reusing the existing `onreconnected` catch-up path
   (`use-clinic-realtime.ts:46`).
6. **A French retryable network state** (AC-43). ⚠️ `client.ts:167` throws
   `'Network error: Unable to connect to the API. Please check if the API is running and CORS is configured
   correctly.'` and `errors.ts:13-15` passes an `ApiError` message through **verbatim**. `:172` is a second
   English `status: 0` path. Add an `ApiErrorCode.Network`, an `isNetworkError` predicate beside the existing
   `isConflictError`, and a « Réessayer » toast action (the shape already exists in `payment-modal.tsx:68-78`).
   Match `connectivity.tsx:106-120`'s existing French wording.
   ⚠️ One edit at `handleRequest` covers every JSON call — but **not** the raw-`fetch` Blob/multipart modules,
   which are exactly the money-artefact downloads of item 4.

**Verifiable:** installs to the home screen with a real icon; every route walked in OS dark with no document
surface inverted; printing any screen prints only its content; a receipt arrives on an iPad; locking the phone
for 40 minutes logs out, for 2 minutes does not, and returns to the same screen.

---

## Part P8 — LAN device trust

**Delivers:** a phone or tablet that trusts a Local install, and an operator who knows what to do when it does not.

1. **A third Kestrel listener.** `Program.cs:408-416` binds HTTP `ListenLocalhost(httpPort)` and HTTPS
   `ListenAnyIP(httpsPort)`; the comment at `:410-413` states why HTTP is loopback-only (*"the cleartext API
   incl. POST /api/auth/login"*), and `features/LEARNINGS.md` records the finding that made it so. Add
   `kestrel.ListenAnyIP(trustPort)` (`Hosting:TrustPort`) — **do not** widen 5000.
2. **A Local-only anonymous `TrustController`** under `/api/trust/*`, following `ConnectivityController` exactly:
   `[AllowAnonymous]` on the actions and `if (!LocalAuthConfig.IsLocalMode(_configuration)) return NotFound();`
   as the first statement (runtime gate, not conditional registration). ⚠️ Routes must be under `/api/…` — the
   YARP catch-all (`Program.cs:508`) forwards everything else to Next. ⚠️ There is **no `UseStaticFiles`**
   anywhere, so assets are returned via `File(bytes, contentType, downloadName)`.
   It serves: `ca.crt` (already exported DER, which iOS/Android accept), a **generated** `.mobileconfig`
   (`application/x-apple-aspen-config`, the same DER base64'd into the plist), Android instructions, and a QR of
   the trust URL — using the **already-registered** `IQrCodeGenerator` (no new dependency; and it must be
   server-rendered because the page renders before trust exists).
   Read the CA from `LocalInstallPaths.LocalFile("ca.crt")`, **not** by injecting `CertificateProvisioner` (it is
   deliberately not DI-registered and is constructed pre-`builder.Build()`).
3. ⚠️ **Add every new action to `ControllerAuthorizationCoverageTests.ExpectedAnonymous`.** The test pins the set
   by equality in both directions, so the build breaks until each is consciously listed with a comment. This is
   the single most likely way P8 fails first.
4. **The 398-day question** (AC-46). `CertificateProvisioner.cs:96` mints a **5-year** leaf; Apple caps TLS
   server certs at 398 days with a documented exemption for user-installed roots. Verify on a real iPhone. If the
   exemption does not hold, shorten the leaf and pin the new value in `CertificateProvisionerTests`; if it cannot
   hold, record Local-mode phone support as Cloud-only.
5. ⚠️ **A field failure mode the spec does not name.** SANs are captured **at generation time** from
   `Dns.GetHostAddresses` and the cert is then reused idempotently — so a **DHCP lease change** gives the server a
   LAN IP that is not in the SAN set, and trust breaks even with the CA installed. There is also **no IPv6 and no
   `.local`/mDNS name**. Add this as a fourth documented failure state alongside the three in AC-45.
6. **Packaging.** Open `trustPort` in `server/clinic-server.iss`'s `OpenFirewall` (it currently opens only 5001,
   `README.md:243`). Document the mobile flow in `packaging/README.md` between the client-installer section
   (`:341`) and the verification checklist (`:362`), with a matching checklist block. ⚠️ Inno `{ }` comments must
   not contain `{app}`/`{sys}` — use `//` (recorded in `packaging/CLAUDE.md` and in memory).
   The `.mobileconfig` is **generated at runtime, not staged at build time**: on a reinstall the CA is reused, on
   a fresh install it is newly minted, so a build-time artefact is stale by construction.

**Verifiable:** `dotnet vstest` green including the authorization coverage test; a physical iPhone and a physical
Android tablet each reach the trust page over the LAN, install the CA, and then load the app with no interstitial.

---

## Testing Strategy

### The honest constraint

There is no frontend test runner, no ESLint, no visual-regression tooling and no CI. Standing a runner up is not
attempted here — it is prerequisite-sized work of its own, and screenshot baselines over 28 routes × 5 widths are
a maintenance burden this repo has never carried. That is stated rather than papered over (**R-2**).

### 1 · `npx tsc --noEmit` + `npm run build`

Both clean, every part (AC-49).

### 2 · `npm run check:responsive` — a committed node script

`web/scripts/check-responsive.mjs`, zero dependencies, exits non-zero. It exists because the 26-dialog `max-w`
collision survived undetected across the whole codebase — a defect nobody could see and no type could catch. Same
spirit as `verify-schema` and `reconcile-money`: derived checks, not a hand-maintained list.

| Check | Rule | Notes |
|---|---|---|
| Dialog clamp | No unprefixed `max-w-*` on a `DialogContent`/`AlertDialogContent` | ⚠️ Must match `className={\`…\`}` template literals — 2 of the 26 use them |
| Page height | No `h-screen` in a page shell or `AppShell` | |
| Sheet height | No `vh` in a sheet/dialog max-height | `dvh` only |
| Type scale | No `text-[Npx]` in `app/` or `components/` | |
| Movement hover | No ungated `hover:scale` / `group-hover:scale` | |
| Tooth clipping | No `justify-center` inside an `overflow-x-auto` container | ⚠️ Must not flag `odontogram.tsx:389` / `odontogram-acts-chart.tsx:134`, which centre a glyph inside its cell |
| Orphans | No unused `setMobileOpen` / `Menu` import after P2 | `tsc` does not flag an unused destructure |

### 3 · The documented manual walk — the real gate

AC-51, following `audit-sections-3-to-10`'s AC-P3.48 and `tooth-first-record-entry`'s numbered
*"Manual verification — the real acceptance gate"* form, each step tagged with the AC it proves. Recorded in
`progress.md`.

Coverage: **all 28 routes** — using **`/rappels`** (⚠️ `/recalls` no longer exists; `web/CLAUDE.md` and the
exploration doc are both stale) — at **320 / 390 / 820 / 1180 / 1440 px**, plus **landscape phone**, plus with a
**keyboard**, plus with the device in **OS dark mode**.

### 4 · Backend tests (P8 only)

`dotnet vstest` for the trust controller, including `ControllerAuthorizationCoverageTests` and
`CertificateProvisionerTests`. ⚠️ Per memory, Smart App Control on this machine fails `dotnet test` at assembly
load with `0x800711C7` — environmental, not a defect; use `dotnet vstest` against pre-built DLLs or record the
blocker.

## Risk Register

| ID | Risk | L | I | Part | Mitigation |
|----|------|---|---|------|------------|
| **R-1** | **The story is oversized for one session.** 8 parts, 30 shell instances, 22 tables, 36 dialogs, 3 charts, a .NET controller. The user chose one plan over a split, deliberately. | **High** | **Med** | all | Parts are ordered, dependency-respecting vertical increments; land and commit **part by part**. A part boundary is the split point. Do not re-propose a split. |
| **R-2** | **No automated frontend verification.** Every AC here is visual; nothing prevents regression. | High | High | all | The mechanical script (AC-50) covers the invisible classes; the documented walk (AC-51) covers the rest. Stated, not hidden. |
| **R-3** | **`next-themes` mounted with the default `attribute`** writes `data-theme` and renders none of the 336 `dark:` utilities — looking wired while doing nothing. | Med | High | P7 | `attribute="class"` is called out in the part text; the dark walk would catch it, but only if someone looks. |
| **R-4** | **336 `dark:` utilities go live at once** on surfaces nobody has seen dark — including money and document previews. | High | Med | P7 | Document surfaces explicitly exempt via `.light`; every route walked in OS dark (AC-51). |
| **R-5** | **The clamp fix changes desktop layout on 26 dialogs.** They render at 512 px today; afterwards `edit-patient-dialog` is 896 px. Users have been looking at the broken version. | **High** | Med | P4 | It is the AC and it is a repair, not a change. Walk all 26 at 1440 px; flag any that were *designed* around 512 px. |
| **R-6** | **`AppShell` touches 30 instances including 4 outside `ClinicGuard` and one server component.** A mistake here breaks every page at once. | Med | **High** | P1 | Guard stays per-page (decision 1). `/documents/[type]` must stay async — `AppShell` must not be `"use client"` at its root, or the page loses `await params`. Convert and verify in small batches. |
| **R-7** | **`HOUR_HEIGHT` drift.** Blocks are positioned from it via four `calc()` strings with a documented invariant. | Low | **High** | P5 | Do not change it. Touch only the grid's overflow and the toolbar. |
| **R-8** | **Consolidating the tooth charts silently discards `476a2e3`** or breaks the read-only reuse contract. | Med | **High** | P6 | Geometry only (decision, AC-34). `renderTooth` is a `ReactNode` callback, never a props object. |
| **R-9** | **P8's anonymous endpoints break the build** via `ControllerAuthorizationCoverageTests`' equality pin. | **High** | Low | P8 | Expected and cheap — add each to `ExpectedAnonymous` with a reviewed comment. Called out as the most likely first failure. |
| **R-10** | **The 398-day cap cannot be verified here.** No physical iOS device in this environment. | **High** | Med | P8 | P8 is sequenced last so it blocks nothing. If it fails, Local-mode phone support falls back to Cloud and the spec records it (AC-46 already allows this). |
| **R-11** | **A new LAN port widens the attack surface.** | Med | Med | P8 | Trust port serves *only* the Local-only trust controller; 5000 stays `ListenLocalhost`. Follows the "make a loopback guarantee a property of the bind" learning. |
| **R-12** | **`vaul` reintroduces animation lag.** Its default is 500 ms; `sheet.tsx:63-66` deliberately runs 300 in / 200 out on `ease-panel`. | Med | Low | P4 | Bottom sheets only; override the timing to match. |
| **R-13** | **Scope creep.** Eight parts across FE and packaging invites "while we're here" — the adjacent `app-design-language` remainder is one file away at all times. | **High** | Med | all | The spec's Out of Scope is the boundary. Anything new goes to `follow-up/`. |
| **R-14** | **Cards drop information silently.** 22 surfaces × "which fields make the card" is 22 chances to omit something a user relied on. | Med | Med | P3 | One stated rule + three argued exceptions; every field still reachable (AC-48: no capability removed by a layout decision). |
| **R-15** | **`AppShell` changes remount behaviour** if it later becomes a layout — `PostVisitReviewPopup`'s 60 s poll and its `dismissed` flag depend on remounting. | Low | Med | P1 | Component, not layout (decision 1). Recorded so a future layout conversion knows to check. |

## Breaking Changes

- **26 dialogs change width on desktop** (R-5) — a repair of AC-20, but visible.
- **The header hamburger disappears below `md:`**; « Plus » in the bottom bar replaces it. Supersedes AC-P3.12's
  wording.
- **The AI panel's bottom geometry moves** to clear the bar — re-opens shipped AC-P3.16, deliberately, restated
  in terms of the shared token.
- **`/settings` and `/users` gain a gutter and a max-width** they have never had.
- **Toasts move from top-right to bottom** on a coarse pointer, capped at 3.
- **Dark mode becomes reachable for the first time** — every screen can now render in a palette no user has seen.
- **A new LAN port** in Local installs, with a firewall rule (P8).

## Migrations

**None.** No EF migration, no schema change, no data backfill. P8 touches `CertificateProvisioner.cs` (possibly
the leaf lifetime) and adds a controller; neither reads nor writes clinic data.

## Documentation to update on completion

- `web/CLAUDE.md` — the "Responsive shell — the chrome, not every screen" paragraph is the thing this feature
  falsifies; rewrite it. Also fix the stale `/recalls` reference and the claimed `patients/loading.tsx` (it does
  not exist).
- `web/components/CLAUDE.md` — the stale sidebar item list; add `AppShell`, `BottomNav`, `CardList`,
  `ToothArchLayout`, `lib/nav.ts`.
- Root `CLAUDE.md` — a bullet for the responsive pass, in the style of the existing architectural notes.
- `packaging/README.md` — the mobile trust flow and its four failure states (P8).
- `features/LEARNINGS.md` — at minimum the tailwind-merge `max-w` collision and the `next-themes`
  `attribute="class"` trap; both are the kind of silent, look-wired-do-nothing defect that file exists for.
