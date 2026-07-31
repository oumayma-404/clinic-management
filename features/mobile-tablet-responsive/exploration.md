# Exploration — Mobile & tablet responsiveness

**Date:** 2026-07-30
**Method:** 8 parallel read-only exploration agents over `web/`, `desktop/`, `packaging/`, `features/`, `api/`.
No browser tooling exists in this repo (`agent-browser` is not on PATH), so findings are grounded in source
reading and counting — the same fallback `features/app-design-language/design.md` used.

---

## 0 · The headline

The app is **not** starting from zero. A deliberate, *scoped* responsive pass shipped as
`features/audit-sections-3-to-10` **US-P3b** (AC-P3.12 … AC-P3.18): the nav rail becomes a `Sheet` drawer below
`md:`, the header reflows, page gutters drop to `p-4`, and two fixed-width offenders (AI panel, document-editor
column) went viewport-relative.

That spec then fenced off everything else, twice (`spec.md:606`, `:1292`):

> **A full responsive pass.** P3 covers the navigation shell and the four named fixed-width offenders. The
> appointment calendar grid and the wide data tables are each substantial and are not attempted here.

`web/CLAUDE.md:131` repeats the fence: *"the chrome, not every screen, is responsive."*

**This feature is that deferred remainder.** It should say so in its Overview.

Also relevant — the prior pass was *"verified by class inspection, **not** by device testing"*
(`features/audit-sections-3-to-10/stories/progress.md:756`).

---

## 1 · App shell

- **One layout only** — `web/app/layout.tsx` (83 lines). No nested layouts, no `loading.tsx`, no `not-found.tsx`.
  It renders **no chrome**.
- **The shell is copy-pasted into 24+ pages**:
  `<ClinicGuard><div className="flex h-screen bg-background"><DashboardSidebar/><div …><DashboardHeader/><main …>`.
  Any shell-wide change must be made 24 times. `reviews/small-feature-prompts.md:120` already proposes
  **SF-16 — Shared responsive app shell** (never implemented).
- **`<main>` gutters diverge**: `p-4 md:p-6` on 17 pages · flat `p-4` on `/procedure-types`,
  `/cnam-nomenclature`, `/dental-acts`, `/medications`, `/appointments` · **no padding at all** on `/settings`,
  `/users`.
- **Content widths diverge**: `max-w-7xl` ×15, `max-w-5xl` (`/creances`), `max-w-3xl` (`/mon-profil`),
  `max-w-[1400px]` (`/appointments`), none at all (`/settings`, `/users`).
- **Sidebar** (`dashboard-sidebar.tsx`) renders **twice** from one component: `hidden md:flex` rail
  (`w-64`/`w-16`) + `md:hidden` `Sheet` drawer (`w-72 max-w-[85vw]`). 11 nav items non-admin, 15 admin.
  Rows are `py-1.5` = **32px tall** — below the 44px touch floor.
- **Header** (`dashboard-header.tsx`, 360 lines): hamburger (`md:hidden`, the drawer's only trigger), an
  always-visible `max-w-md` patient search combobox, connectivity badge, notification bell, user menu.
  There is **no clinic switcher** anywhere.
- **Sidebar state** (`contexts/sidebar-context.tsx`): `isCollapsed` persisted to localStorage; `isMobileOpen`
  deliberately **not** persisted, and auto-closes on `pathname` change.

## 2 · Viewport, PWA, platform metadata — **none of it exists**

- **No `export const viewport`** anywhere. Next 15 injects a default `width=device-width, initial-scale=1`, so
  pages scale — but there is no `viewportFit: "cover"` (notches), no `interactiveWidget` (keyboard), no
  `themeColor`.
- **No PWA manifest**, no service worker, no install prompt, no offline cache.
- **No safe-area handling** — zero hits for `env(safe-area-inset-*)`.
- ⚠️ **The declared icons do not exist.** `layout.tsx:20-36` points at `/icon-light-32x32.png`,
  `/icon-dark-32x32.png`, `/icon.svg`, `/apple-icon.png`. `web/public/` holds only the untouched
  `create-next-app` SVGs. **`apple-touch-icon` 404s today**, so "Add to Home Screen" has no icon.
- **`h-screen` in 36–48 files; `dvh` in exactly one** (`ai-chat.tsx`). Every page shell is `100vh`, which on iOS
  Safari is the *large* viewport — the bottom of every page sits under the URL bar.

## 3 · Breakpoints

- **Tailwind v4, CSS-config only.** No `tailwind.config.*`; `components.json` has `"tailwind.config": ""`.
  All config is in `web/app/globals.css` (309 lines).
- **No `--breakpoint-*` tokens.** Stock defaults: `sm 640 · md 768 · lg 1024 · xl 1280 · 2xl 1536`.
- Census across `app/` + `components/`: **`md:` ~76 · `sm:` ~64 · `lg:` 6 · `xl:` 2 · `2xl:` 0**.
  51 of 137 `.tsx` files (37%) contain any breakpoint prefix.
- **Consequence: the app is two-state (phone / not-phone) at 768px.** An iPad Air portrait (820px) gets the full
  desktop rail; an iPad mini portrait (744px) gets the phone drawer. There is effectively **nothing between 768
  and 1280**.
- **No `useMediaQuery`/`useIsMobile` hook, no `matchMedia`, no `window.innerWidth`** — zero hits. All
  responsiveness is CSS-only, which is why the sidebar keeps both trees in the DOM.
- `@custom-variant hover-hover (@media (hover: hover) and (pointer: fine))` **already exists**
  (`globals.css:14`) with a comment about *"a tablet — which is exactly what a dentist holds."* Used in only
  6–7 places; four `hover:scale-105` sites ignore it.
- `prefers-reduced-motion` **is** handled properly (`globals.css:286-308`). `:focus-visible` has a global floor.
  `prefers-contrast` / `forced-colors` are not handled.

## 4 · Tables — 22 surfaces, one primitive, no fallback

All 22 go through `components/ui/table.tsx`. **Zero hand-rolled `<table>` markup in the codebase.**

The primitive wraps every table in `relative w-full overflow-x-auto` — but sets **`whitespace-nowrap` on every
`TableHead` and `TableCell`**, so intrinsic width = sum of the longest untruncatable strings. There is no
`truncate`, no column priority, no card fallback, and **no table anywhere has a mobile variant**
(repo-wide, only 3 `hidden md:` / `md:hidden` usages exist, all in the chrome).

| Cols | Surface |
|---|---|
| **10** | `/lab-orders` — Patient · Travail · Prothésiste · Dent · Envoyé · Prévu · Reçu · Coût · Statut · Actions |
| **9** | `factures/invoices-table.tsx` — 3 money columns + El Fatoora + action cluster (the **only** `sticky` header in the app) |
| **8** | `caisse-ledger-table` · `stock-table` · `dental-acts-table` · `recurring-series` · `treatment-plans-table` |
| **7** | `procedure-types` · `cnam-nomenclature` · `plan-workspace` actes · patient dossiers · patient RDV · `patient-summary-modal` |
| 6 | patients · medications · users · waiting-list · caisse dépenses · plan échéancier · reminder log |

- ⚠️ `patient-summary-modal.tsx:225-232` has **8 explicit per-column `min-w-`** summing ~760px, inside a dialog
  that tailwind-merge clamps to 512px, with `overflow-x-hidden` on the wrapper — **content is clipped, not
  scrollable**.
- Row actions are consistently `flex justify-end gap-2` of icon-only buttons — the cell that makes "Actions"
  unshrinkable.
- **Pagination is already responsive** — `ui/data-table-pagination.tsx` is the only primitive that stacks
  (`flex-col … sm:flex-row`). 15 call sites. Its icon buttons are `h-8 w-8` (32px).
- Adoption of the `app-design-language` opt-ins is thin: `numeric` ×2, `sticky` ×1, **`TableMeta` ×0**.
  `ListToolbar`/`FilterChip` ×1 page (`/patients`). `PageHeader` ×11 pages; ≥5 distinct title treatments still
  coexist.

## 5 · The agenda — the hardest surface

`components/appointment-calendar.tsx` (1002 lines) has **zero breakpoint classes**.

- `HOUR_HEIGHT = 48` px, a **load-bearing constant** — appointment blocks are absolutely positioned overlays
  computed from it (`top = startMinutes/60 * 48`), with a documented invariant that every row must be exactly
  48px or blocks drift.
- Week grid: `grid-cols-[60px_repeat(7,minmax(0,1fr))]` at `:817, :862, :913`; the 60px gutter is hardcoded in
  three places **plus four CSS `calc()` strings** (`weekBandLeftExpr`, `weekBandWidthExpr`, …).
- ⚠️ `:881` — `overflow-y-auto **overflow-x-hidden**`. The week grid **cannot scroll sideways**; it only
  squashes. This is the one place that breaks the AC-P3.14 "wide content scrolls in its own container" rule.
- **At 390px:** 358px content − 60px gutter = 298px ÷ 7 = **~42.5px per day column**; blocks render at ~38.5px
  wide. A 30-min appointment is 24px tall, tripping `isVerySmall` which strips everything but a truncated name.
- Month view: `grid-cols-7 grid-rows-6`, ~55×80px cells holding a 24px date circle + up to 3 chips at
  `text-[10px]`.
- Toolbar is one `flex-wrap` row of prev/next/today + `text-xl` range + 4-item legend + 2 switches; the page
  adds Tabs + Google sync + a `w-[180px]` Select + "Nouveau rendez-vous". ~5 stacked rows at 390px.

## 6 · Dialogs — 70 modal surfaces, and a real defect

36 `DialogContent` + 32 `AlertDialogContent` + 2 `SheetContent`, across 44 files.

⚠️ **The tailwind-merge clamp** (already documented in-repo at `patient-record-modal.tsx:516-518`): the base is
`w-full max-w-[calc(100%-2rem)] … sm:max-w-lg`. A caller passing an **unprefixed** `max-w-*` removes the base
mobile gutter (same group, caller wins) but **cannot** remove `sm:max-w-lg`, which then wins at ≥640px. So for
**26 of 36 dialogs**:

- **< 640px** — the 1rem gutter is destroyed; the dialog is edge-to-edge.
- **≥ 640px** — capped at **512px** regardless of the declared `max-w-4xl` / `3xl` / `6xl`.

`edit-patient-dialog` (`max-w-4xl`, 1276 lines, 6 sections) actually renders at 512px on a desktop today.

Other dialog facts:
- `dialog.tsx` has **no `max-h`, no internal scroll region, no bottom-sheet variant, no safe-area**. Every
  dialog that scrolls adds `max-h-[90vh] overflow-y-auto` itself (`vh`, not `dvh`).
- `edit-patient-dialog.tsx:635` hardcodes `max-h-[calc(90vh-200px)]` — the 200px header allowance is wrong once
  the title wraps.
- `sheet.tsx` exists; **`side="bottom"` and `side="top"` have zero call sites** — no bottom sheet in the app.
- **`vaul@^1.1.2` is installed and imported nowhere.** So is `react-resizable-panels`, `embla-carousel-react`,
  `input-otp`.
- **13 unprefixed `grid grid-cols-2`** form rows that never collapse (`stock-item-form-modal` ×3,
  `clinic-settings` ×4, `dental-act-form-modal` ×2, `cnam-entry-form-modal` ×2,
  `create-appointment-dialog:634`, `edit-patient-dialog` ×2, `document-editor-content` ×6), plus
  `procedure-type-form-modal:312` `grid-cols-5` colour swatches.
- No `<Form>`/`FormField` primitive despite `react-hook-form` + `zod` being installed — every form is hand-rolled
  `useState`.

## 7 · Touch targets — nothing reaches 44px

| Primitive | Size |
|---|---|
| `Input` | `h-9` = **36px** (font is `text-base md:text-sm`, so **no iOS focus-zoom** — correct) |
| `Textarea` | `text-sm` = 14px ⇒ **iOS zooms on focus** (the Input's guard was not applied) |
| `TimeField` | `h-10`, `text-sm` ⇒ **iOS zooms on focus** |
| `Button` | default `h-9` 36 · `sm` `h-8` 32 · `lg` `h-10` 40 · `icon` 36 · `icon-sm` 32 · `icon-lg` 40 |
| `SelectTrigger` / `SelectItem` | 36px / ~30px rows |
| `Checkbox`, `RadioGroup` | **16×16px** |
| `DropdownMenuItem`, `CommandItem` | ~30px |
| `Dialog`/`Sheet` close button | 16px icon, **no padding** |
| Sidebar nav row | 32px |
| Calendar (`react-day-picker`) day cell | `--cell-size: 32px` |
| Odontogram tooth | `h-9 w-7` = **36×28px** |
| `record-tooth-chart` SVG tooth | **18–22 × 30px** |
| `Switch` | 24×44 — the only control hitting 44 on one axis |

`CommandInput` at `h-11` (44px) is the single compliant target in the codebase.

## 8 · Odontogram — three implementations, all clipped

| File | Rendering | Cell |
|---|---|---|
| `odontogram.tsx` | CSS boxes | `h-9 w-7` (36×28) |
| `odontogram-acts-chart.tsx` | CSS boxes, deliberately identical | `h-9 w-7` |
| `record-tooth-chart.tsx` | inline SVG glyphs | 18–22 × 30 |
| `tooth-multiselect.tsx` | popover of chips | `h-7 w-9`, `PopoverContent w-80` |

- Adult arch minimum: box charts **~597px of viewport**; SVG chart ~453px inside a modal whose content box is
  ~326px at 390px.
- ⚠️ **Clipping bug, all three**: `justify-center` on a flex row inside `overflow-x-auto`. When content
  overflows, `justify-content: center` pushes overflow to *both* sides and the **inline-start overflow is not in
  the scrollable region** — teeth 18–15 and 48–45 are unreachable at 390px.
  Sites: `odontogram.tsx:249,267` · `odontogram-acts-chart.tsx:215,225` · `record-tooth-chart.tsx:179,190`.
- **The touch precedent to follow**: commit `476a2e3` swapped the acts chart's Radix `Tooltip` for a `Popover`
  with **two independent open channels** (`tappedTooth` / `hoveredTooth`) held in the **parent**, not per-cell —
  *"32 cells a few pixels apart would otherwise stack panels as the pointer crosses them."* Hint copy became
  « Touchez ou survolez ». **The same fix was never applied to the Diagnostics tab** (`odontogram.tsx:453-478`),
  where the per-tooth condition list is still hover/focus-only.

## 9 · Other hover-only / touch-inert affordances

- `patient-files-manager.tsx:516` — the file **delete** button is `opacity-0 group-hover:opacity-100`.
  **Invisible and un-tappable on touch.**
- `clinic-settings.tsx:713`, `mon-profil-content.tsx:186` — logo / cachet replace overlays, same pattern.
- `odontogram.tsx:456-477` — per-tooth condition tooltip (see above).
- `connectivity-indicator.tsx` — the 3-state badge's explanation is tooltip-only.
- `collected-trend-chart.tsx` — hover crosshair, **mitigated** by an explicit table-view toggle (good precedent).
- Four `hover:scale-105` sites not gated behind `hover-hover:`.
- No context menus, no double-click handlers, no drag-to-move appointments. One drag-and-drop (file upload) and
  it has a `<label>` fallback.

## 10 · Charts & dense surfaces

- `recharts@2.15.4`, one chart: `collected-trend-chart.tsx`, `h-52` fixed height, `YAxis width={52}`.
  At 390px → ~238px of plot for 6 points; the `fontSize: 11` month labels will collide.
- `dashboard/hero-kpi.tsx` `Sparkline` — SVG `viewBox="0 0 180 46"` with `preserveAspectRatio="none"`, so the
  stroke distorts at narrow widths.
- **The dashboard is the least broken screen.** `KpiGrid` is the only genuinely mobile-ready grid
  (`1 → sm:2 → xl:3|4`, hairlines via `gap-px` over `bg-border` so it wraps at any count).
- **Patient detail is partly done already**: `TabsList` is `grid-cols-2 sm:grid-cols-4 lg:grid-cols-7`,
  `PatientNotesStrip` is `md:grid-cols-2`, info cards are `lg:grid-cols-3`. The odontogram card is the widest
  element and is deliberately full-width.
- **Document editor** got AC-P3.17 (`w-full … md:w-[420px]`) but keeps `min-h-[1123px]` A4 previews ×2 and two
  **`w-[384px]` popovers, ungated** — wider than a 375px viewport.
- **PDF preview** is duplicated verbatim in two files, uses `calc(100vw - 8rem)` (reserves 128px a phone lacks),
  and `#toolbar=0` removes the browser's zoom UI. **iOS Safari does not render PDFs in `<iframe>`.**
- **Print** is `window.open` + `printWindow.print()` (`document-editor-content.tsx:1399,1452`) — mobile popup
  blockers and iOS handling will break it. There is **no `@media print` and no `print:` utility anywhere** in the
  app, so printing any screen prints the sidebar, header and AI button.

## 11 · Design-system gaps

- **No type scale, no spacing tokens, no breakpoint tokens.** 121 arbitrary `text-[Npx]` values, including
  `text-[8px]` ×4 and `text-[9px]` ×13 — below the phone legibility floor.
- **Dark mode is 100% built and 0% reachable**: `.dark` tokens are complete and hand-tuned, **336 `dark:`
  utilities** are written across the app, `next-themes@^0.4.6` is installed — and **no `ThemeProvider` is
  mounted, no toggle exists, zero imports**. It is also class-based, so it does not follow the OS either.
- `next/font` loads `Geist`/`Geist_Mono` into **unused variables** (`_geist`, `_geistMono`); `font-sans` resolves
  only because next/font injected the family names as a side effect.
- Fixed widths that break a phone, beyond those already named: `w-80` ×7 popovers (320px on a 375px screen),
  `w-96`, `w-72`, `w-[384px]` ×2, `w-[180px]`, `w-20`/`w-32` numeric inputs in the invoice line editor.
- Only **1 `aria-current`** in the whole app — active nav state is effectively colour-only for screen readers.
  No skip-link.

## 12 · Deployment reality — the access wall

| | Cloud (Auth0) | Local / offline-LAN |
|---|---|---|
| How a device connects | `https://<domain>` — *"no per-device setup, no CA import, no IP"* (`DEPLOY.md:5`) | `https://192.168.x.x:5001`, **self-signed CA** |
| Phone/tablet today | ✅ Works. `features/cloud-deployment` AC-2 names *"the secretary's Android tablet"* and `progress.md:53` records it verified with a valid padlock. | ⚠️ **Full-page cert interstitial on every cold browser start.** |
| Login | Auth0 **redirect** (plain `<a href="/auth/login">`), not popup — mobile-correct. | HttpOnly cookie, `sameSite: lax`, same-origin — works fine in a mobile browser. |

⚠️ **`features/security-hardening/spec.md:415` (EC-7)** is the single most important line in the repo for this
feature:

> A tablet or phone on the clinic Wi-Fi opens the app without having imported the clinic CA (**only the desktop
> client installer imports it**). With HSTS off (the Local default) the user gets the normal bypassable
> certificate warning and can proceed. If an operator enables HSTS, that warning becomes a hard, unbypassable
> failure.

- CA distribution is **Windows-only** (`certutil -addstore Root` in the client installer). There is **no** iOS
  `.mobileconfig`, no Android instruction, no QR onboarding, no MDM path in `packaging/`.
- Android 7+ ignores user-installed CAs for app traffic; iOS needs a profile **plus** a second manual step in
  Certificate Trust Settings.
- ⚠️ **Unverified risk**: Apple caps TLS server-cert validity at 398 days. `CertificateProvisioner.cs:96` mints a
  **5-year** leaf. Apple documents an exemption for user-installed roots — but this needs a real device test.
- First-run **setup is loopback-only** (`LocalRequest.IsLoopback`), so a tablet can never bootstrap a clinic —
  only join one. That is correct and should stay.
- `desktop/` (WPF + WebView2) is a Windows-only viewer: evergreen WebView2, resizable 1280×820 default, **no UA
  override**. Irrelevant as a rendering constraint, though a Windows tablet running it does get native touch.

**30-minute inactivity auto-logout** (`web/lib/auth/session.tsx:107`). `touchstart` **is** in the reset listener
list, but there is **no `visibilitychange` handling** — a phone that locks or backgrounds will silently log out,
and mobile OSes discard backgrounded tabs aggressively. Expect many more re-logins on a phone.

## 13 · Library risks on touch

| Library | Used in | Risk |
|---|---|---|
| `file-saver` + `docx` | 1 file each | **iOS Safari does not reliably honour `Blob` + `download`** — client-side .docx export likely broken on iPhone/iPad. |
| `@microsoft/signalr` | 2 files | WebSocket drops on every network handoff / backgrounding. Reconnect policy needs review. |
| Web Speech API (`webkitSpeechRecognition`, `fr-FR`) | `ai-chat.tsx:190` | **Not supported in iOS Safari.** Voice input silently unavailable. |
| `react-day-picker` | 2 dialogs | 32px day cells. |
| `recharts` | 1 file | Hover tooltips. |
| Radix `Tooltip` | 4 files | Hover-only by design. |

## 14 · Roles & workflows (for prioritisation)

Three roles: `admin` · `doctor` · `secretary` (« Secrétaire / Assistant(e) »). **There is no dentist-only or
secretary-only route** — separation is at the *action* level only (`AdminOnly`, `AdminOrDoctor`). So device
priority cannot be derived from authorization; it must come from **where the work happens**.

The code's own comments are the best evidence:
- **At the chair** (dentist, gloved, patient present): the **odontogramme** (`patients/[id]/page.tsx:853-856` —
  *"the chart the whole consultation is read off… it needs the width"*), the **fiche de soins**
  (`features/tooth-first-record-entry`: *"click a tooth, or several teeth, and put what he did in that session
  right away"*), the alerts/allergies strip, the ordonnance.
- **At the desk** (front office, keyboard, printer, cash): la caisse, créances, factures/avoirs, El Fatoora,
  patient registration, relances, lab orders, stock, all of Configuration.
- **Both**: the agenda, la salle d'attente, patient record consultation.

Highest-frequency workflows: consulter l'agenda / booker · **the header patient search** (*"the single most-used
control on the header"*) · salle d'attente + promote · enregistrer la fiche de soins (driven by the global
`post-visit-review-popup`, polling every 60s) · encaisser · consulter le dossier · charting + devis · fermer la
caisse · relances · bons de prothèse.

⚠️ **There is no one-tap « arrivé » action.** Check-in is a `<Select>` inside `edit-appointment-dialog` —
exactly the kind of thing a tablet at reception would want.

## 15 · Spec & verification conventions

- Repo house style diverges from the skill template. Real skeleton: header block
  (`Status` / `Type` / `Scope` / `Created` / `Feature`), then `## Overview` · `## What Changes` ·
  `## Acceptance Criteria` · `## Out of Scope` · `## Edge Cases (Critical only)`.
  `## User Stories` / `## Functional Requirements` are near-dead. AC ids are flat **`AC-N`**.
- Prose is English; every user-facing string is quoted in French guillemets. Assertions carry `file.tsx:123`
  citations. Out-of-scope entries are **argued**, not listed.
- Median spec is ~62 lines; UI-heavy ones run 80–250; the audit spec ran 1501.
- Folder contents: `spec.md` (59) + `progress.md` (52) are the norm; `mockups/` ×8, `plan.md` ×6, `stories/` ×6.
  **No feature folder has a `screenshots/` directory.**
- ⚠️ **`web/` has no test runner and no working ESLint.** No vitest/jest/playwright/cypress, no
  testing-library, **no visual-regression tooling**, and **no CI** (`.github/` does not exist).
  `npm run lint` fails on a clean install (`eslint` and `eslint-config-next` are declared but not installed);
  `next.config.ts` has `eslint: { ignoreDuringBuilds: true }`.
- **The gate is therefore `npx tsc --noEmit` + `npm run build`, both clean, plus a written manual walk.**
  Precedent: `audit-sections-3-to-10` **AC-P3.48** — *"A documented manual walk covers every screen this spec
  touches or creates … at 375px and with the keyboard, and its result is recorded in `progress.md`. There is no
  automated frontend test to assert any of this, which is why the walk's scope is stated rather than assumed."*
  And `tooth-first-record-entry`'s numbered *"Manual verification — the real acceptance gate"* list, each step
  tagged with the AC it proves.

`features/LEARNINGS.md` entry to cite directly:

> **Space-based UI gating can hide a required affordance entirely.** *"Hide when there's no room" silently became
> "feature doesn't exist here."* When an affordance is required, never let a layout/size heuristic be its only
> gate.
