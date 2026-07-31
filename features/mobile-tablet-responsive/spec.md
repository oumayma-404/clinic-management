# Feature Specification: Mobile & tablet — the app on every device

**Status:** APPROVED
**Challenged:** Yes — 6-lens completeness pass (user flow · edge cases · error/recovery · a11y/UX · scope boundary · consistency)
**Type:** Large (8 phases, each independently shippable)
**Scope:** FE + packaging
**Created:** 2026-07-30
**Exploration:** `features/mobile-tablet-responsive/exploration.md`
**Feature:** Finish the responsive pass that `audit-sections-3-to-10` US-P3b deliberately stopped short of — the
wide data tables, the agenda grid, the 70 dialogs, the odontogram and the tablet range — and take the result to a
native-app standard: coarse-pointer touch targets, sheets instead of squeezed modals, one owner for the bottom
edge, home-screen install, a reachable dark mode, a print stylesheet, and a way for a phone to trust a LAN
install's certificate.

## Overview

The app is not starting from zero and must not pretend to. A **scoped** responsive pass shipped as
`features/audit-sections-3-to-10` US-P3b (AC-P3.12 … AC-P3.18): below `md:` the nav rail becomes a `Sheet`
drawer, the header reflows, page gutters drop to `p-4`, and two fixed-width offenders went viewport-relative.
That spec then fenced off the rest, twice (`spec.md:606`, `:1292`):

> **A full responsive pass.** P3 covers the navigation shell and the four named fixed-width offenders. The
> appointment calendar grid and the wide data tables are each substantial and are not attempted here.

`web/CLAUDE.md:131` repeats the fence: *"the chrome, not every screen, is responsive."* **This feature is that
deferred remainder**, the way `dashboard-insights` opened by closing what `live-dashboard` deferred.

Three facts set the shape of the work.

**One, the app is two-state at 768 px.** `lg:` is used 6 times and `xl:` twice in the whole codebase, so there is
effectively nothing between a phone and a 1280 px desktop. An iPad Air portrait (820 px) gets the full desktop
rail beside a 10-column table; an iPad mini portrait (744 px) gets the phone drawer. The tablet — the device a
dentist actually holds at the chair — is the one form factor with no layout at all.

**Two, the remaining surfaces are the ones that carry the work.** 22 table surfaces (widest: `/lab-orders` at 10
columns, `factures/invoices-table` at 9), all routed through one primitive that sets `whitespace-nowrap` on every
cell; 70 modal surfaces; a 1002-line calendar with **zero** breakpoint classes and `overflow-x-hidden` on the one
grid that cannot fit; three separate tooth charts whose teeth are 28 px wide and whose outer teeth are clipped
beyond reach at 390 px.

**Three, "professional" here is mostly repair, not redesign.** The audit turned up defects that are not phone
defects at all: **26 of 36 dialogs** lose their declared width to a tailwind-merge collision and render at 512 px
on a desktop today; the four icons `layout.tsx` declares do not exist, so `apple-touch-icon` 404s; dark mode is
100 % built and 0 % reachable (336 `dark:` utilities, `next-themes` installed, no provider ever mounted); there is
no `@media print` anywhere, so printing `/factures` prints the sidebar and the AI button. Fixing those is what
makes the app look finished — on every device, not only small ones.

The governing rule, stated once and used to settle every scope question below:

> **No capability is removed by a layout decision.** Platform and security limits are an enumerated exception
> list, and each exception shows an explicit French message rather than failing silently.

## What Changes

### Phase 01 — Foundations: tokens, breakpoints, and one app shell

**A tablet breakpoint exists.** Today `md:` (768 px) is the single switch. `globals.css` gains explicit
`--breakpoint-*` tokens so the app has four states — phone (< 640) · tablet portrait (640–1023) · tablet
landscape / small laptop (1024–1279) · desktop (1280+) — and the currently-unused `lg:` becomes the tablet-landscape
hinge rather than a rounding error.

**A type scale.** 121 arbitrary `text-[Npx]` values exist, including `text-[8px]` ×4 and `text-[9px]` ×13 on
meaning-bearing text — every tooth number in all three charts, the odontogram's « +N » overflow count, the
calendar's « non synchronisé » badge. They are px-locked, so they neither scale with the user's text size nor
respect zoom. A `--text-*` scale in `@theme` replaces them, with a **floor of 11 px for any text that carries
information**. This is in scope because illegibility on a phone *is* a responsiveness defect; the rest of
`features/app-design-language`'s remainder is not (see Out of Scope).

**One `AppShell`.** `flex h-screen bg-background` + `DashboardSidebar` + `DashboardHeader` + `<main>` is
copy-pasted into 24 pages, with **three divergent gutters** (`p-4 md:p-6` ×17 · flat `p-4` ×5 · none on
`/settings` and `/users`) and **five divergent content widths** (`max-w-7xl` ×15 · `max-w-5xl` · `max-w-3xl` ·
`max-w-[1400px]` · none). It is extracted into one component — proposed once already as
`reviews/small-feature-prompts.md:120` SF-16 and never done. Without it every shell change in phases 02–07 must be
made 24 times.

`h-screen` (36–48 files) becomes `dvh`-based. `100vh` on iOS Safari is the *large* viewport, so the bottom of
every page currently sits under the URL bar; `ai-chat.tsx` is the only file in the repo that already knows this.

The shell also gains what only exists once today: a « Aller au contenu » skip-link, named `<nav>` landmarks, and
`aria-current="page"` (there is exactly **one** `aria-current` in the entire app).

**Directional utilities become logical.** Code this pass writes or rewrites uses `ps-`/`pe-`/`ms-`/`me-`/
`text-start`. RTL is out of scope, but not precluding it costs nothing while these 24 files are open and would
otherwise mean a second sweep of the same files.

### Phase 02 — Navigation and touch targets

**A bottom bar on phone.** Below `sm:`, four destinations — Tableau de bord · Rendez-vous · Salle d'attente ·
Patients — plus « Plus », safe-area padded. The drawer stays, reached from « Plus », and keeps all five titled
groups and all **15 destinations (19 for an admin)**. Nothing is removed; the bar exists because the daily screens
were two taps behind a hamburger in the top-left, the hardest corner to reach one-handed.

⚠️ The bar **supersedes AC-P3.12's wording** ("opened by a header control"). The header hamburger is removed below
`sm:` and « Plus » becomes the drawer's only trigger, reusing the existing `isMobileOpen` channel — a third state
would re-open the AC-P3.18 question the sidebar context was split to answer (`isCollapsed` persisted, `isMobileOpen`
not, *"so a phone session never overwrites the desktop rail preference"*). EC-11 is re-armed and re-answered below.

**One bottom-offset token.** Four things already want the phone's bottom edge: the AI FAB (`fixed bottom-4 right-4
z-50`) and its open panel (`bottom-4 left-4 right-4 h-[70dvh]` — that geometry **is** shipped AC-P3.16), the pager
under 15 lists, the new sheet footers, and toasts. A single token (bar height + `env(safe-area-inset-bottom)`) is
consumed by all of them, with a stated z-order. **The bottom bar hides while a full-screen sheet is open**, so a
sheet's primary action is never covered. AC-P3.16 is re-opened deliberately and restated, not silently broken.

**Toasts move.** `Toaster` is `position="top-right" expand visibleToasts={5}` with 8-second errors. At 390 px that
is effectively full-width over the header, and over an open sheet's title and close control — and the error toast
is *the only place the failure reason exists*. On a coarse pointer it becomes bottom-anchored above the bottom-bar
token, capped at 3.

**44 px targets, keyed to pointer coarseness, applied to the hit area.** Not to viewport width — an iPad landscape
at 1180 px is a chairside device with gloved hands and would keep 36 px buttons under a width rule. Applying it to
the tappable area rather than the painted control means `ui/table.tsx`'s tuned `px-3 py-2.5` density survives on
the desk machine. Affected: every `Button` variant (36/32/40), `Input`/`SelectTrigger` (36), `SelectItem` and menu
items (~30), `Checkbox`/`RadioGroup` (**16**), the `Dialog`/`Sheet` close buttons (16 px icon, **no padding**),
sidebar rows (32), `react-day-picker` cells (32), and the pager's icon buttons (32).

**Every hover-revealed affordance gets a touch path.** These are two different rules and the spec carries both:
(a) *movement* hovers are gated behind the existing `hover-hover:` variant per its stated policy — four
`hover:scale-105` sites are not; (b) affordances only **reachable** by hover get a real touch path —
`patient-files-manager.tsx:516` (the file **delete** button is `opacity-0 group-hover:opacity-100`, i.e. invisible
and un-tappable on touch), `clinic-settings.tsx:713` and `mon-profil-content.tsx:186` (logo / cachet replace
overlays), and the connectivity badge's tooltip-only explanation. ⚠️ Applying (a) to a (b) case would make the
affordance *permanently* invisible on touch — `features/LEARNINGS.md`'s « space-based UI gating can hide a required
affordance entirely », in a new place.

**Textarea and TimeField stop zooming iOS.** `Input` already carries `text-base md:text-sm`; `textarea.tsx` and
`time-field.tsx` do not, so focusing them zooms the page on iOS and never zooms back.

### Phase 03 — Tables become cards

Below `md:`, a table renders as a **list of cards**: identity as the title, secondary fields as labelled pairs,
actions collapsed into one menu. No horizontal scroll on a phone.

**Two trees, not a CSS reflow.** A `display:block` reflow strips the implicit table roles in every browser, so a
card would announce « Ben Salah 45,000 12/03 Payée » with no field names — across 22 surfaces including money and
clinical data. Above `md:` a real `<table>`; below, a semantic list where every field is announced with its label.
The doubled DOM is bounded: every list that matters is already paged.

**One priority rule, exceptions named.** Card title = the row's identity · then status · then money · then date ·
actions to the menu. Written once so 22 tables do not become 22 judgements. Three genuine exceptions:

| Surface | Why it differs |
|---|---|
| `caisse/caisse-ledger-table.tsx` | `RunningBalance` is **window-relative** and order-dependent (« Solde de la période »). Per-card it asserts something it cannot mean — it is dropped from the card and stated once in the list footer. |
| `treatment-plans/plan-workspace.tsx` actes | Carries the « séance de N actes » grouping, which is a *grouping of rows*, not a row field. The card list keeps the grouping as a section header. |
| `patient-summary-modal.tsx` | A table **inside a dialog**, with 8 explicit `min-w-` summing ~760 px and `overflow-x-hidden` on the wrapper — content is currently **clipped, not scrollable**. It adopts the card list at every width inside the dialog. |

**Card content contract**, defined in the primitive because 22 surfaces adopt it: a long primary value truncates to
one line with its full value available on tap; a labelled pair with **no value is omitted**, not rendered as
« — » (`Email`/`PhoneNumber` are genuinely nullable and the sentinels were retired); and every existing
`<TableCell colSpan={N}>` empty state — meaningless in a card list — gets a card-list equivalent that **keeps the
filter-vs-empty distinction** two tables already draw (« aucun résultat pour ce filtre » vs « la liste est vide »).

**Loading state.** The repo has one `Skeleton` usage and no `loading.tsx` anywhere, so today every phone
navigation is chrome plus a blank rectangle — and a card list has no header row, making "empty", "loading" and
"your filter is wrong" indistinguishable. The card list gets a skeleton.

**Filter visibility.** Nine dashboard links land on a *filtered* list. A card list with no visible chip is a
filtered list that looks unfiltered, so any active filter is shown as a removable chip on every list, phone and
desktop. This adopts `ListToolbar`/`FilterChip` on the list pages — which is `app-design-language` item 2 — **only
as far as filter visibility requires**; the rest of that item stays out.

### Phase 04 — Dialogs

**Fix the clamp first — it is a desktop defect.** `DialogContent`'s base is
`w-full max-w-[calc(100%-2rem)] … sm:max-w-lg`. A caller passing an **unprefixed** `max-w-*` removes the base
mobile gutter (same tailwind-merge group, caller wins) but cannot remove `sm:max-w-lg`, which then wins at ≥ 640 px.
So for **26 of 36 dialogs**: edge-to-edge below 640 px, and **capped at 512 px above it** regardless of the declared
width. `edit-patient-dialog` asks for `max-w-4xl` and renders at 512 px today. `patient-record-modal.tsx:516-518`
already documents the trap for itself; the fix generalises it.

**Sheets below `md:`.** Heavy dialogs (patient form, devis editor, fiche de soins, invoice form, revise
installments) become **full-screen sheets** with a sticky header and a sticky footer holding the primary action.
Light confirmations — including all 32 `AlertDialogContent` — become **bottom sheets**. `vaul` is already a
dependency and imported nowhere.

**The dismissal contract.** A sheet is dismissible by a visible ≥ 44 px control **and** by `Escape` (a reception
tablet has an attached keyboard), in addition to swipe. Focus lands on the **sheet's title**, not its first field —
autofocusing a field raises the keyboard over the content the user opened the sheet to read. The page behind
scroll-locks. Focus returns to the trigger on close, and where the trigger was a table row that the card conversion
re-rendered, to the card.

**Unsaved work is guarded.** No form in the app has any dirty-state check. Swipe-to-dismiss and Android's back
gesture are two new discard channels on the longest data-entry surfaces in the product — `edit-patient-dialog`
(1276 lines, 6 sections) and the fiche de soins, filled at the chair one-handed. A sheet with entered data confirms
before discarding, on every channel.

**Presentation changes, the component does not.** Crossing 768 px — rotation, iPad Split View, Stage Manager —
must not remount a dialog as a different component and lose typed input.

**The keyboard must not bury the primary action.** The shell's `overflow-hidden` means the browser cannot scroll
the page to reveal a focused field, and every scrolling dialog caps on `vh`, not `dvh`, so the cap does not shrink
when the keyboard opens. Sheets size to `dvh`, and the focused field plus the primary action stay visible.

**Ungated grids collapse.** 13 `grid grid-cols-2` form rows have no breakpoint prefix, plus
`procedure-type-form-modal.tsx:312`'s `grid-cols-5` colour swatches. Popovers wider than a phone —
seven `w-80`, two `w-[384px]`, the `w-72` Radix default — become viewport-relative.

**The post-visit prompt is not a sheet.** `PostVisitReviewPopup` is mounted on all 24 pages and polls every 60 s.
As a bottom sheet it is an unrequested full-screen takeover that can fire mid-payment or mid-charting, and `vaul`'s
swipe channel would bypass its single `handleLater` dismissal path — the exact bug the local `dismissed` flag was
added to fix, in a new shape. On a coarse pointer it degrades to a **toast with an action**, and it suppresses
itself while any dialog or sheet is open.

### Phase 05 — The agenda

Below `md:` the agenda **opens on Jour** — the only one of the three views that works at 390 px. Semaine becomes a
compact 7-day density strip you tap to enter a day; Mois keeps its grid with **dots instead of chips**. Both stay
reachable, and the choice is the user's: Jour is the *initial* view, not a lock, and it survives rotation.

⚠️ **A dashboard drill-through overrides it.** `app/appointments/page.tsx:168-201` deliberately forces Mois for any
`?from=&to=` link because *"the calendar has no arbitrary-range view, so the window is honoured by switching to the
widest view — month — which is the closest honest rendering of 'the period the card counted'."* Two of the fifteen
entries in `dashboard-links.ts` route here. If Jour won, « RDV honorés — Ce mois » would land on one day and assert
a number its destination contradicts — the precise failure that file was written to prevent. The two status
toggles such a link flips (`showCancelled`/`showCompleted`) surface as visible removable chips.

The week grid's `overflow-x-hidden` (`appointment-calendar.tsx:881`) is removed — it is the one place in the app
that violates AC-P3.14's "wide content scrolls inside its own container", and it is why the week view squashes to
~42 px columns instead of scrolling. The toolbar (prev/next/today + range + a 4-item legend + 2 switches, one
`flex-wrap` row that becomes ~5 stacked rows) is restructured, and **« Nouveau rendez-vous » keeps a stable home**
at every width. Tapping an empty row in Jour books at that hour, as it already does on desktop.

⚠️ `HOUR_HEIGHT = 48` is load-bearing — appointment blocks are absolutely positioned from it, with a documented
invariant that rows must be exactly 48 px or blocks drift. It is not changed; the day view's readability comes from
the full width, not from a shorter hour.

### Phase 06 — The odontogram

Below `md:`, **one arch at a time** (Haut / Bas) at full width with **44 px** teeth; tablet portrait and up shows
both. An adult arch currently needs ~597 px of viewport at 28 px per tooth.

⚠️ **The clipping bug is the real defect and it is not a phone-only one.** All three charts use `justify-center` on
a flex row inside `overflow-x-auto`. When content overflows, `justify-content: center` pushes overflow to *both*
sides and **the inline-start overflow is not in the scrollable region** — teeth 18–15 and 48–45 are unreachable at
390 px, by scrolling or otherwise. Six sites: `odontogram.tsx:249,267` · `odontogram-acts-chart.tsx:215,225` ·
`record-tooth-chart.tsx:179,190`.

**Consolidation is geometry only.** The three charts are not interchangeable and the differences are load-bearing:
`odontogram-acts-chart` carries the `476a2e3` touch fix (`tappedTooth` + `hoveredTooth` held **in the parent**,
because *"32 cells a few pixels apart would otherwise stack panels as the pointer crosses them"*), while
`record-tooth-chart` *"deliberately has no selection chrome of its own so the read-only summary modal can reuse
it."* One shared layout; interaction stays per chart.

**Separately**, the Diagnostics tab adopts the same parent-held two-channel popover. `odontogram.tsx:453-478` still
wraps its per-tooth condition list in a hover/focus-only `Tooltip` — the `476a2e3` fix was never applied there, so
the one place a tooth's charted diagnoses appear is unreachable by touch.

### Phase 07 — Platform: install, dark mode, print, and resume

**Home-screen install.** A manifest, real icons, `theme-color`, and `display: standalone`. ⚠️ `layout.tsx:20-36`
declares `/icon-light-32x32.png`, `/icon-dark-32x32.png`, `/icon.svg` and `/apple-icon.png`; **none of them
exist** — `web/public/` holds only the untouched `create-next-app` SVGs, so `apple-touch-icon` 404s today.
An explicit `viewport` export adds `viewportFit: "cover"` and `interactiveWidget`. Standalone mode removes the
browser's back button, so the shell gains an in-app back affordance. **No service worker and no offline** — the app
still requires the server (argued in Out of Scope).

**Dark mode becomes reachable.** `next-themes` is installed, the `.dark` tokens are complete and hand-tuned, 336
`dark:` utilities are written — and no `ThemeProvider` is ever mounted, so none of it renders. A three-way
Système / Clair / Sombre control, following the OS by default, stored per device. ⚠️ **Documents are exempt**: the
A4 previews, the PDF preview, the CNAM BS1 overlay, the print surface and uploaded cachet/logo images are authored
on white. A dark note d'honoraires is a document defect, not a theme preference.

**A print stylesheet.** There is no `@media print` and no `print:` utility anywhere in the app — the only print CSS
in the repo is a string injected into a `window.open` document at `document-editor-content.tsx:1423`. Printing
`/factures`, `/caisse`, a patient record or a devis currently prints the sidebar, the header and the floating AI
button.

**Documents reach the device.** Every money artefact — the reçu after « Encaisser », the note d'honoraires, the
devis PDF, the avoir, the reçu d'échéance, the El Fatoora XML, patient files — goes through one helper doing
`a.download` plus a **synchronous** `URL.revokeObjectURL`, across 8 call sites. **iOS ignores `download` and can
kill the navigation**, so on an iPad the payment records and the receipt never appears, with no error and nothing
to retry. On a coarse pointer the artefact **opens in a new tab**, with « Partager » offered where the Web Share
API supports files. Desktop keeps the direct download.

**The app survives being backgrounded.** This is a behaviour change, included because the form factor causes it —
and installing as a PWA makes backgrounding *more* aggressive, not less. Three parts:
`session.tsx`'s 30-minute inactivity timer is re-evaluated against an **absolute timestamp** on
`visibilitychange` (today a frozen tab's `setTimeout` never fires, so a locked phone stays logged in *past* the
limit — a security regression — while a discarded tab cold-boots somewhere the user did not choose); live refresh
re-subscribes and refetches on foreground rather than relying on `withAutomaticReconnect()`'s default **four
attempts, after which it stops for good**; and a connection loss becomes its own French, retryable state.
⚠️ Today `client.ts:167` throws `'Network error: Unable to connect to the API. Please check if the API is running
and CORS is configured correctly.'` and `getErrorMessage` passes it through **verbatim** — so every wifi handoff
puts an English sentence mentioning CORS in front of a dentist, indistinguishable from a business refusal.

### Phase 08 — LAN device trust

In Cloud a tablet already loads with a valid padlock and no setup (`features/cloud-deployment` AC-2 names *"the
secretary's Android tablet"* and `progress.md:53` records it verified). In Local/offline-LAN the server mints its
own CA and **only the Windows client installer imports it** (`certutil -addstore Root`) — a phone gets a full-page
interstitial on every cold browser start, and `packaging/` has no iOS, Android, QR or MDM path.

⚠️ **The onboarding page cannot live behind the certificate it fixes.** `Program.cs:414` binds HTTP to
`ListenLocalhost` and HTTPS to `ListenAnyIP`, so a phone's only reachable endpoint is the untrusted one. This phase
adds a LAN-reachable, anonymous, **Local-mode-only** trust page serving the CA, an iOS `.mobileconfig`, Android
instructions and a QR code, plus a stated operator path for the three failure states: interstitial hard-blocked
(HSTS on — `features/security-hardening/spec.md:415` records that this is unbypassable), profile installed but the
second iOS *Certificate Trust Settings* toggle not (the app looks broken and the user believes they finished), and
a device trusting a CA the server has since regenerated.

⚠️ **`CertificateProvisioner.cs:96` mints a 5-year leaf. Apple caps TLS server-cert validity at 398 days.** Apple
documents an exemption for user-installed roots, but this is unverified and needs a real device. If the exemption
does not hold, the leaf lifetime changes; if it cannot be made to hold, Local-mode phone support falls back to
Cloud and this spec says so rather than claiming a capability it does not have.

## Acceptance Criteria

**Foundations (Phase 01)**

- **AC-1:** `globals.css` defines explicit `--breakpoint-*` tokens giving four states — phone · tablet portrait ·
  tablet landscape · desktop. No screen relies on `md:` as its only switch.
- **AC-2:** A `--text-*` scale exists in `@theme`. **No text that carries information renders below 11 px** at
  default zoom — including every tooth number, the odontogram « +N » count and the calendar sync badge. Arbitrary
  `text-[Npx]` values are gone from `app/` and `components/`.
- **AC-3:** One `AppShell` component renders the sidebar, header and `<main>`. All 24 protected pages use it. One
  gutter rule and one content-width rule; `/settings` and `/users` are no longer the two pages with neither.
- **AC-4:** No page shell uses `h-screen`. The bottom of every page is reachable on iOS Safari with the URL bar
  shown and hidden.
- **AC-5:** The shell provides a « Aller au contenu » skip-link, distinctly named `<nav>` landmarks, and
  `aria-current="page"` on the active destination — marked on **one** navigation only when a destination appears in
  both the bottom bar and the drawer.
- **AC-6:** Code written or rewritten in this feature uses logical directional utilities
  (`ps-`/`pe-`/`ms-`/`me-`/`text-start`).

**Navigation and touch (Phase 02)**

- **AC-7:** Below `sm:` a bottom bar shows four destinations plus « Plus », padded by
  `env(safe-area-inset-bottom)`. « Plus » opens the drawer, which retains all five groups and all 15 destinations
  (19 for an admin). The header hamburger is removed below `sm:`; « Plus » reuses `isMobileOpen` and adds no new
  state, so AC-P3.18's guarantee (a phone session never writes the desktop rail preference) still holds.
- **AC-8:** One bottom-offset token (bar height + safe-area inset) is consumed by the AI FAB and panel, toasts,
  bottom sheets and page footers, with a stated z-order. **The bottom bar is hidden while a full-screen sheet is
  open.** AC-P3.16's geometry is restated in terms of the token, not silently overridden.
- **AC-9:** On a coarse pointer, toasts are bottom-anchored above the token, capped at 3, and legible over an open
  sheet. They never cover the header controls or a sheet's primary action.
- **AC-10:** On a coarse pointer, every interactive control has a **tappable area** of at least 44 × 44 px. The
  painted control may stay smaller. On a fine pointer, desktop density is unchanged — `ui/table.tsx`'s
  `px-3 py-2.5` is not loosened.
- **AC-11:** Every affordance currently revealed by hover has a touch path: the file delete button, the logo and
  cachet replace overlays, the connectivity badge's explanation. **None of them is gated behind `hover-hover:`** —
  that variant is applied only to *movement* hovers, per its stated policy, and the four ungated `hover:scale-105`
  sites adopt it.
- **AC-12:** Focusing a `Textarea` or a `TimeField` does not zoom the page on iOS.

**Tables (Phase 03)**

- **AC-13:** Below `md:`, all 22 table surfaces render as a semantic card list. **No page scrolls horizontally at
  the body level, and no table scrolls horizontally, at 320 px.**
- **AC-14:** A screen reader announces every card field with its column name. The `<table>` is not present below
  `md:`; a real list is.
- **AC-15:** Row actions are reachable from one menu per card, each action keeping its accessible name.
- **AC-16:** Card content follows the stated rule (identity → status → money → date), with the three named
  exceptions behaving as described: the caisse ledger's running balance stated once in the footer rather than
  per card; the plan actes list keeping « séance de N actes » as a section header; `patient-summary-modal`
  adopting the card list at every width, so its content is no longer clipped.
- **AC-17:** A long primary value truncates to one line with its full value available on tap. A field with no
  value is omitted, not rendered as « — ».
- **AC-18:** Every list has a card-list empty state that preserves the filter-vs-empty distinction, and a loading
  skeleton distinct from both.
- **AC-19:** An active filter is shown as a removable chip on every list, at every width.

**Dialogs (Phase 04)**

- **AC-20:** No `DialogContent` call site passes an unprefixed `max-w-*`. Every dialog renders at its declared
  width at ≥ 640 px and keeps a gutter below it. `edit-patient-dialog` renders at `max-w-4xl`, not 512 px.
- **AC-21:** Below `md:`, heavy dialogs are full-screen sheets with a sticky header and footer; light
  confirmations, including all 32 `AlertDialogContent`, are bottom sheets.
- **AC-22:** Every sheet is dismissible by a visible ≥ 44 px control **and** by `Escape`, in addition to swipe.
  Focus lands on the sheet title on open and returns to the trigger on close. The page behind does not scroll.
- **AC-23:** A sheet or dialog with entered data confirms before discarding — on swipe, back gesture, outside tap
  and close control alike.
- **AC-24:** Crossing a breakpoint (rotation, Split View, window resize) with a dialog open **preserves entered
  data**. The presentation changes; the component does not remount.
- **AC-25:** With the on-screen keyboard open, the focused field **and** the primary action remain visible without
  dismissing the keyboard, in portrait and landscape.
- **AC-26:** No form grid renders two or more columns below `sm:`. No popover is wider than the viewport at 320 px.
- **AC-27:** On a coarse pointer the post-visit review prompt is a toast with an action, not a sheet; it records a
  snooze on every dismissal channel, and does not appear while another dialog or sheet is open.

**Agenda (Phase 05)**

- **AC-28:** Below `md:` the agenda's initial view is Jour. Semaine is a tappable 7-day density strip; Mois shows
  dots. A view the user chooses is kept, including across rotation.
- **AC-29:** A dashboard drill-through carrying a date range still lands on Mois, and the status filters it
  applies are shown as removable chips.
- **AC-30:** The week grid no longer sets `overflow-x-hidden`; it scrolls horizontally inside its own container
  with a sticky time gutter. `HOUR_HEIGHT` is unchanged and no appointment block drifts.
- **AC-31:** « Nouveau rendez-vous » is reachable at every width, and tapping an empty row in Jour books at that
  hour.

**Odontogram (Phase 06)**

- **AC-32:** No tooth is clipped or unreachable at 320 px in any of the three charts. `justify-center` inside a
  scroll container is gone from all six sites.
- **AC-33:** Below `md:` one arch shows at a time with a Haut / Bas control; teeth have 44 px tappable areas.
  Tablet portrait and up shows both arches.
- **AC-34:** The three charts share one layout. `record-tooth-chart` keeps its read-only reuse contract and
  `odontogram-acts-chart` keeps its parent-held two-channel popover.
- **AC-35:** A tooth's charted diagnoses are reachable by touch on the Diagnostics tab.

**Platform (Phase 07)**

- **AC-36:** A manifest, real icons at every declared size, `theme-color` and `display: standalone` exist. No
  icon URL 404s. An explicit `viewport` export sets `viewportFit: "cover"`.
- **AC-37:** In standalone mode an in-app back affordance exists wherever the browser's would have been needed.
- **AC-38:** A Système / Clair / Sombre control exists, follows the OS by default and persists per device.
- **AC-39:** Document previews, the PDF preview, the CNAM BS1 overlay, the print surface and uploaded cachet/logo
  images render on white regardless of theme.
- **AC-40:** Printing any app screen prints its content — no sidebar, no header, no bottom bar, no AI button.
- **AC-41:** On a coarse pointer, every downloadable artefact opens in a new tab, with « Partager » offered where
  the Web Share API supports files. No action ends in silence.
- **AC-42:** Backgrounding and returning does not lose the session before the 30-minute limit, does log out after
  it, restores live refresh, and returns the user to the screen they left.
- **AC-43:** A connection loss shows a French, retryable state distinct from a business refusal. No English
  string, and no mention of CORS, reaches a user.

**LAN device trust (Phase 08)**

- **AC-44:** In Local mode a LAN-reachable, anonymous trust page serves the CA, an iOS `.mobileconfig`, Android
  instructions and a QR code — reachable **before** the device trusts the server. It 404s in Cloud.
- **AC-45:** The three trust failure states — hard-blocked by HSTS, half-installed on iOS, stale CA after
  regeneration — each produce a stated operator action, documented in `packaging/README.md`.
- **AC-46:** The server leaf's validity is verified against Apple's 398-day cap on a real iOS device. If the
  user-installed-root exemption does not hold, the lifetime is changed; if it cannot hold, the spec records
  Local-mode phone support as Cloud-only.

**Cross-cutting**

- **AC-47:** Every screen is usable at **320 px** wide and at a **380 px viewport height** (landscape phone), and
  at **200 % browser zoom**. The agenda's fixed hour grid may be exempted from the zoom criterion with its reason
  stated.
- **AC-48:** No capability is removed by a layout decision. Where a platform limit prevents one, an explicit French
  message says so — never a silent failure.
- **AC-49:** `npx tsc --noEmit` and `npm run build` are both clean.
- **AC-50:** Mechanical checks pass and are runnable on demand: no unprefixed `max-w-*` on a `DialogContent`, no
  `h-screen` in a page shell, no ungated `hover:scale`, no `text-[Npx]`, no `justify-center` inside an
  `overflow-x-auto`, no `100vh`/`vh` in a sheet height.
- **AC-51:** A documented manual walk covers **every route** — all 28, using `/rappels` (the `/recalls` route no
  longer exists) — at **320 / 390 / 820 / 1180 / 1440 px**, plus landscape phone, with a keyboard, and with the
  device in OS dark mode. Its result is recorded in `progress.md`. There is no automated frontend test to assert
  any of this, which is why the walk's scope is stated rather than assumed.

## Data / Schema Changes

**None.** No migration, no entity change, no API contract change. Phase 08 touches
`CertificateProvisioner.cs` (leaf lifetime) and adds one anonymous Local-only route; neither reads or writes
clinic data.

## Out of Scope

- **The rest of `features/app-design-language`.** Its items 2, 4 and 5 remain open: `DataTable`'s
  `numeric`/`sticky`/`TableMeta` on tables beyond invoices, `ListToolbar`/`PageHeader` adoption everywhere, the card
  rule on `/factures` and the plan workspace, ~520 hardcoded colours, ≥5 coexisting page-title treatments. Two
  fragments are pulled in here **only because responsiveness requires them** — the type scale (px-locked 8–9 px
  text is a legibility defect on a phone) and filter chips (an invisible filter on a card list reads as an
  unfiltered list). Everything else is consistency work, not device work, and folding it in would roughly double
  this spec. It stays where it is, and this feature must not contradict it.
- **A service worker and offline operation.** "Install" here means manifest + icons + standalone. A cache layer is
  a different feature class: stale bundles against a migrated API on a LAN install nobody can remotely debug, plus
  interactions with SignalR, the inactivity logout and the Auth0 redirect. The app still requires the server, and
  the spec says so rather than implying otherwise by using the word "PWA" loosely.
- **A native application.** `COMPETITIVE_ANALYSIS.md:39` scores mobile-friendliness ❌ against two competitors with
  native apps. This feature closes that gap as **responsive web plus home-screen install**, not as a Capacitor or
  React Native shell. If one is wanted later, nothing here precludes it.
- **Arabic and RTL.** The app is French end to end — `<html lang="fr">` is hardcoded and there is no `dir`
  anywhere. RTL is a localisation feature, not a responsive one. AC-6 keeps this pass from making a future Arabic
  pass a second sweep of the same 24 files, which is the whole of what is owed here.
- **Composing a document, or filling a BS1 bulletin, at phone width.** `/documents/[type]` is readable, printable
  and sendable at 390 px, and its A4 preview scrolls at true size in its own container rather than being scaled to
  illegibility. **Authoring** targets tablet portrait and up. The BS1 is an overlay against an official CNAM form
  whose geometry is legally fixed; a 390 px viewport is not a defensible place to enter reimbursement data, and
  making it one would be the hardest surface in the spec after the agenda.
- **`/setup`.** First-run is loopback-only by design (`LocalRequest.IsLoopback`), so a tablet can never bootstrap a
  clinic — only join one. That is a security boundary, not a layout one, and it stays. `/login`, `/join` and
  `/change-password` **are** in scope, in both Cloud and Local variants.
- **`desktop/` (WPF + WebView2).** A Windows-only viewer on evergreen WebView2, resizable from 1280×820, with no UA
  override. It renders whatever the browser renders, so it inherits every improvement here and imposes no
  constraint of its own. It is not a separate verification target.
- **A one-tap « arrivé ».** Check-in is currently a `<Select>` inside `edit-appointment-dialog` — exactly what a
  reception tablet would want as a single gesture. It is new workflow behaviour, not a device adaptation, and
  belongs in its own spec. Captured as a follow-up.
- **Windows High Contrast / `forced-colors`.** `prefers-reduced-motion` is already handled properly and stays; the
  odontogram's inline `backgroundColor` and the agenda's per-procedure colours would each need a non-colour channel
  to survive forced colors. That is an accessibility feature of its own and is not attempted here.
- **Standing up a frontend test runner.** Argued under Verification.

## Edge Cases (Critical only)

- **EC-1 — The drawer or a sheet open across a breakpoint.** `isMobileOpen` resets only on `pathname` change, and
  nothing in the repo reads viewport width in JS. An iPad rotated portrait → landscape with the drawer open keeps
  the state and re-opens it on rotating back. **Expected:** the drawer closes when the layout switches to the rail;
  an open sheet keeps its data (AC-24) and re-presents itself as a dialog.
- **EC-2 — iPad Split View at ~320 px.** All responsiveness is viewport-based CSS, so a third-width pane renders
  the phone layout on a 1024 pt device. **Expected:** the phone layout, fully usable — this is why 320 px is the
  floor rather than 390 px.
- **EC-3 — Landscape phone, ~250 px of content height.** The header, bottom bar and safe area consume most of an
  844 × 390 viewport, and the agenda's day grid is 1152 px tall. **Expected:** content scrolls; the sticky footer
  does not eat the last of it; the primary action stays reachable.
- **EC-4 — The keyboard opens over a full-screen sheet's footer.** **Expected:** the sheet sizes to the visual
  viewport; the focused field and the primary action remain visible (AC-25).
- **EC-5 — The tab is discarded by the OS mid-fiche.** **Expected:** on return, the session is honest about the
  absolute inactivity limit and the user lands on the screen they left. Losing an unsaved sheet's contents to an OS
  discard is accepted; losing it to a stray swipe is not (AC-23).
- **EC-6 — « Encaisser » succeeds and the receipt cannot be delivered.** **Expected:** the payment is recorded (it
  already is) and the artefact opens in a new tab; where the platform cannot, an explicit French message says the
  document must be retrieved from the desk machine. Never silence.
- **EC-7 — A phone opens a Local install without the CA.** **Expected:** with HSTS off, the bypassable warning and
  a route to the trust page. With HSTS on the block is unbypassable — documented as requiring CA import first
  (`features/security-hardening/spec.md:415`).
- **EC-8 — A card list of 500 rows.** Every paged list is already capped by the pager; the two reads that page in
  memory (« Créances », the extrait de caisse) are unaffected by presentation. **Expected:** the card list renders
  one page, and the pager remains reachable above the bottom-bar token.
- **EC-9 — Zero rows, and zero rows under a filter.** **Expected:** two distinct messages, as the tables draw today
  (AC-18). `colSpan` empty rows have no meaning in a card list and are replaced, not dropped.
- **EC-10 — A 60-character patient name as a card title.** **Expected:** one truncated line, full value on tap
  (AC-17). No card grows to fit, and no title wraps to three lines.

## Verification

`web/` has **no test runner, no working ESLint** (`eslint` and `eslint-config-next` are declared but not installed,
so `npm run lint` fails on a clean install; `next.config.ts` sets `eslint: { ignoreDuringBuilds: true }`), **no
visual-regression tooling and no CI** — there is no `.github/`. Standing a runner up is not attempted here: it is a
prerequisite-sized piece of work of its own, and baselines over 28 routes × 5 widths are a maintenance burden this
repo has never taken on.

The gate is therefore three things, and the third is the load-bearing one:

1. **`npx tsc --noEmit` + `npm run build`, both clean** (AC-49).
2. **Mechanical checks** (AC-50) for the classes of defect the eye misses. This exists because the 26-dialog
   `max-w` collision survived undetected across the whole codebase — a defect nobody could see and no type could
   catch. Each check is a grep with a stated intent, runnable on demand.
3. **A documented manual walk** (AC-51), recorded in `progress.md`, following the `audit-sections-3-to-10` AC-P3.48
   precedent and `tooth-first-record-entry`'s numbered *"Manual verification — the real acceptance gate"* form,
   each step tagged with the AC it proves.

Phase 08's AC-46 additionally requires a **physical iOS device and a physical Android device**. Nothing in this
environment can substitute for that, and the phase is sequenced last so it never blocks the other seven.
