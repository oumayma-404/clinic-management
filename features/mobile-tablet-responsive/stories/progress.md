# Progress — Mobile & tablet

**Story:** [story-1-mobile-tablet-responsive.md](./story-1-mobile-tablet-responsive.md) ·
**Plan:** [../plan.md](../plan.md) · **Spec:** [../spec.md](../spec.md)

Resume state for the one story. A **part boundary is the commit point and the resume point** — record what landed
before stopping.

**Branch:** `feature/audit-sections-3-to-10` (the user asked for the work to land in place, not on a new branch).

**Working tree note (start of session):** `features/landing-website/` was untracked and unrelated to this story.
It was left alone and excluded from every commit; staging was by explicit path throughout.

## Part status

| Part | Covers | Status | Commit | Notes |
|---|---|---|---|---|
| **P0** | Mechanical-check script | ✅ **complete** | `920571a` | 8 checks; 4 still PENDING for later parts |
| **P1** | Foundations + `AppShell` | ✅ **complete** | `<P1>` | 24 files / 28 shells; see below |
| **P2** | Nav, touch, bottom token | not-started | — | Next. Remove `"P2"` from `PENDING_PARTS` when it lands |
| **P3** | Tables → `CardList` | not-started | — | |
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

## Manual walk — the real acceptance gate (AC-51)

**Not started.** Deferred to the end of the feature, as the plan sequences it: it covers all 28 routes at
320 / 390 / 820 / 1180 / 1440 px, plus landscape phone, keyboard-only, and OS dark mode — and dark mode is not
reachable until P7 mounts the theme provider, so walking now would have to be repeated.

P1's own verifiable claim — *"every route renders identically at 1440px"* — rests on `tsc` + `build` + the
mechanical checks, and is **not** a substitute for that walk.

| Route | 320 | 390 | 820 | 1180 | 1440 | Landscape | Keyboard | Dark | ACs proved |
|---|---|---|---|---|---|---|---|---|---|
| _pending_ | | | | | | | | | |
