# Story 1: The app works, and looks finished, on every device

**Status:** APPROVED
**Story Status:** in-progress — **P0, P1, P2 complete; P3 partial (3/19 surfaces)**
(`920571a`, `de07bfb`, `e11abc8`, `25c97ae`, `ad533b0`). See [progress.md](./progress.md).
**Layer:** Full — 7 of 8 parts are frontend-only; **P8** touches the API and packaging
**Depends On:** None
**Blocks:** None

**Spec:** [../spec.md](../spec.md) · **Plan:** [../plan.md](../plan.md) · **Exploration:** [../exploration.md](../exploration.md)

> **One story, by explicit decision.** `plan.md` already encodes one story in eight ordered parts (risk **R-1**),
> and `/break-plan` was invoked with *"the fewest amount of user stories possible"*. Granularity was not
> re-opened. **Test authoring is out of scope by instruction — implementation only.**

## Objective

**As a** dentist at the chair with a tablet, a secretary at the front desk, and an owner checking the day from a
phone
**I want** every screen to fit the device I am holding, every control to be reachable with a thumb, and every
document to arrive
**so that** the software is not a desktop application I am borrowing on the wrong machine.

Concretely: 22 wide tables become card lists on a phone, 26 dialogs stop being clamped to 512 px and become
sheets below `md:`, the agenda opens on a view that works at 390 px, every tooth in the odontogram becomes
reachable and tappable, the app installs to a home screen, dark mode renders for the first time, printing stops
including the sidebar, receipts arrive on an iPad, and a phone can trust a LAN install's certificate.

## The steps live in the plan

This story is worked through **eight ordered parts**. Do not duplicate the steps here — read them from the plan so
there is one source of truth.

| Part | Plan section | Covers |
|---|---|---|
| **P1** Foundations + `AppShell` | [plan.md § Part P1](../plan.md#part-p1--foundations-and-one-appshell) | tokens · type scale (116 sites) · one shell over 30 instances · `dvh` · viewport · skip-link · `lib/nav.ts` |
| **P2** Nav, touch, bottom token | [plan.md § Part P2](../plan.md#part-p2--navigation-touch-targets-and-the-bottom-edge) | bottom bar · one bottom-offset token · toasts · 44 px hit areas · hover→touch paths |
| **P3** Tables → `CardList` | [plan.md § Part P3](../plan.md#part-p3--tables-become-cards) | 22 surfaces · 3 argued exceptions · 13 empty states · removable filter chips |
| **P4** Dialogs | [plan.md § Part P4](../plan.md#part-p4--dialogs) | the `max-w` clamp on 26 sites · full-screen + bottom sheets · dismissal contract · dirty guard |
| **P5** Agenda | [plan.md § Part P5](../plan.md#part-p5--the-agenda) | Jour default · drill-through override · week horizontal scroll · toolbar |
| **P6** Odontogram | [plan.md § Part P6](../plan.md#part-p6--the-odontogram) | clipping at 6 sites · `ToothArchLayout` · arch toggle · Diagnostics touch |
| **P7** Platform | [plan.md § Part P7](../plan.md#part-p7--platform-install-dark-mode-print-delivery-resume) | manifest + icons · dark mode · print · 13 download paths · resume |
| **P8** LAN device trust | [plan.md § Part P8](../plan.md#part-p8--lan-device-trust) | third listener · trust controller · `.mobileconfig` + QR · packaging |

**A part boundary is the commit point and the resume point** (plan risk **R-1**).

⚠️ **Do not stop mid-part in P1** — the shell conversion touches 30 instances plus the sidebar's `<aside>`, and
`h-screen → h-dvh` must land on **both** in the same commit or the rail and the shell disagree on height and the
app grows a second scrollbar.

## Acceptance Criteria

_From spec (all 51):_

- [ ] **P1** — AC-1 (four breakpoint states) · AC-2 (type scale, ≥11 px for information) · AC-3 (one `AppShell`,
      one gutter, one width) · AC-4 (no `h-screen`) · AC-5 (skip-link, named landmarks, `aria-current`) ·
      AC-6 (logical directional utilities) · AC-36 *viewport half only*
- [ ] **P2** — AC-7 (bottom bar, no new state) · AC-8 (one bottom-offset token + z-order; bar hides under a
      sheet) · AC-9 (toasts) · AC-10 (44 px hit areas on coarse pointer) · AC-11 (hover→touch paths) ·
      AC-12 (no iOS focus-zoom)
- [ ] **P3** — AC-13 (no h-scroll at 320 px) · AC-14 (fields announced with labels) · AC-15 (one action menu) ·
      AC-16 (priority rule + 3 exceptions) · AC-17 (truncate / omit empty) · AC-18 (empty vs filtered vs
      loading) · AC-19 (removable filter chips)
- [ ] **P4** — AC-20 (clamp fixed) · AC-21 (sheets below `md:`) · AC-22 (dismissal contract) · AC-23 (dirty
      guard) · AC-24 (data survives a breakpoint crossing) · AC-25 (keyboard) · AC-26 (grids, popovers) ·
      AC-27 (post-visit prompt is a toast on touch)
- [ ] **P5** — AC-28 (Jour initial, choice sticks) · AC-29 (drill-through still lands on Mois) · AC-30 (week
      scrolls; `HOUR_HEIGHT` unchanged) · AC-31 (« Nouveau rendez-vous » always reachable)
- [ ] **P6** — AC-32 (nothing clipped at 320 px) · AC-33 (one arch, 44 px teeth) · AC-34 (shared layout, both
      contracts intact) · AC-35 (Diagnostics reachable by touch)
- [ ] **P7** — AC-36 (manifest, real icons) · AC-37 (in-app back) · AC-38 (Système/Clair/Sombre) · AC-39
      (documents stay light) · AC-40 (print) · AC-41 (every artefact arrives) · AC-42 (resume) · AC-43 (French
      retryable network state)
- [ ] **P8** — AC-44 (LAN trust page) · AC-45 (four failure states documented) · AC-46 (398-day cap verified on
      a real device)
- [ ] **Cross-cutting** — AC-47 (320 px / 380 px height / 200 % zoom) · AC-48 (no capability removed by layout) ·
      AC-49 (`tsc` + `build`) · AC-50 (mechanical checks) · AC-51 (documented manual walk)

_Story-specific:_

- [ ] The mechanical-check script exists and passes **before** P1's bulk edits begin (see Notes).
- [ ] No test files are authored. P8's `ExpectedAnonymous` update is implementation, not a test.
- [ ] Each part ends in a committable state: `tsc --noEmit` and `npm run build` both clean.

## Entry Criteria

- [x] `spec.md` **APPROVED** — 51 ACs, 8 phases
- [x] `plan.md` **APPROVED** — 8 parts, 15 risks, 6 verified corrections to the spec's counts
- [ ] Working tree clean, or the in-flight `feature/audit-sections-3-to-10` changes committed —
      ⚠️ `git status` at plan time showed ~60 modified files on that branch
- [ ] A branch for this feature exists off the current head
- [ ] `cd web && npm install` succeeds and `npx tsc --noEmit` + `npm run build` are **already clean on the base**,
      so any failure during the story is attributable
- [ ] **P8 only:** a physical iPhone and a physical Android tablet are available on the same LAN as a Local-mode
      install (AC-46 cannot be satisfied otherwise)

## Files to Create/Modify

Indicative — the plan's part sections carry the authoritative per-file detail.

### Files to Create

| File | Purpose | Part |
|------|---------|------|
| `web/scripts/check-responsive.mjs` | Zero-dependency mechanical checks, exits non-zero (AC-50) | P0 |
| `web/components/app-shell.tsx` | The one shell: sidebar + header + `<main>` + bottom bar | P1 |
| `web/lib/nav.ts` | Hoisted `NavItem`/`NavSection`/`baseSections`/`configItems`/`HIDDEN_PATHS` | P1 |
| `web/components/bottom-nav.tsx` | Four destinations + « Plus », flex sibling of `<main>` | P2 |
| `web/components/ui/card-list.tsx` | Semantic card list, the below-`md:` half of every table | P3 |
| `web/components/ui/drawer.tsx` | `vaul` bottom sheet (⚠️ fix the CLI's umbrella import) | P4 |
| `web/hooks/use-dirty-guard.ts` | Confirm-before-discard across swipe / back / outside tap / ✕ | P4 |
| `web/components/tooth-arch-layout.tsx` | Shared arch **geometry only** for all three charts | P6 |
| `web/app/manifest.ts` + real icon assets | Home-screen install; the four declared icons do not exist today | P7 |
| `web/components/theme-provider.tsx` | `"use client"` wrapper — ⚠️ `attribute="class"` | P7 |
| `api/…/Controllers/TrustController.cs` | Local-only anonymous CA / `.mobileconfig` / QR | P8 |

### Files to Modify

| File | Changes | Part |
|------|---------|------|
| `web/app/layout.tsx` | `viewport` export, `suppressHydrationWarning`, manifest, `ThemeProvider`, `Toaster` position | P1, P7 |
| `web/app/globals.css` | `--text-*` scale, `--bottom-inset`, `coarse` variant, print stylesheet | P1, P2, P7 |
| **30 shell instances across 24 `app/**/page.tsx`** | Adopt `AppShell`; resolve 3 gutters, 6 widths, 4 overflow variants | P1 |
| ~40 files carrying `text-[Npx]` | 116 sites → scale tokens; primitives first | P1 |
| `web/components/dashboard-sidebar.tsx` · `dashboard-header.tsx` · `contexts/sidebar-context.tsx` | Nav export, `h-dvh`, hamburger removal + orphan cleanup, `matchMedia` close | P1, P2 |
| `web/components/ui/table.tsx` | `containerClassName`; type tokens | P1, P3 |
| **22 table call sites** | Add the `CardList` half + empty/loading/filter states | P3 |
| **26 `DialogContent` + 26 `AlertDialogContent` call sites** | Prefix `max-w-*`, `vh`→`dvh`, sheet conversion | P4 |
| `web/components/appointment-calendar.tsx` | Remove `overflow-x-hidden`, Jour default, dot month, toolbar | P5 |
| `odontogram.tsx` · `odontogram-acts-chart.tsx` · `record-tooth-chart.tsx` | Adopt `ToothArchLayout`; clipping at 6 sites; Diagnostics popover | P6 |
| `web/lib/download.ts` **+ 5 non-helper paths** | Coarse-pointer open-in-tab / share (incl. the El Fatoora XML) | P7 |
| `web/lib/auth/session.tsx` · `realtime/clinic-hub.ts` · `use-clinic-realtime.ts` · `api/client.ts` · `lib/errors.ts` | Absolute-timestamp resume, foreground re-subscribe, French retryable network state | P7 |
| `api/…/Program.cs` · `CertificateProvisioner.cs` · `ControllerAuthorizationCoverageTests.cs` · `packaging/**` | Third listener, leaf lifetime, **`ExpectedAnonymous` entries**, firewall + operator docs | P8 |

## Verification Steps

No tests are authored. Verification is the plan's three-part gate.

- [ ] **Types and build** — `npx tsc --noEmit` and `npm run build` clean, at **every part boundary**, not only at
      the end
- [ ] **Mechanical checks** — `npm run check:responsive` exits 0
- [ ] **Documented manual walk** — all **28 routes** (using **`/rappels`**; ⚠️ `/recalls` no longer exists) at
      **320 / 390 / 820 / 1180 / 1440 px**, plus landscape phone, plus keyboard-only, plus the device in **OS dark
      mode**. Recorded in [progress.md](./progress.md), each step tagged with the AC it proves.
- [ ] **P8 only** — `dotnet vstest` green, including `ControllerAuthorizationCoverageTests` and
      `CertificateProvisionerTests`; then a physical iPhone and Android tablet reach the trust page over the LAN,
      install the CA, and load the app with no interstitial.

**Verification commands:**

```bash
cd web
npx tsc --noEmit          # must be clean
npm run build             # must be clean
npm run check:responsive  # AC-50 mechanical checks

# P8 only — note: `dotnet test` fails at assembly load on this machine
# (Smart App Control, 0x800711C7). Use vstest against pre-built DLLs.
dotnet build api/ClinicManagement.sln
dotnet vstest api/ClinicManagement.UnitTests/bin/Debug/net8.0/ClinicManagement.UnitTests.dll
```

## Exit Criteria

- [ ] All 51 spec ACs satisfied, or explicitly deferred with a reason recorded in `progress.md`
- [ ] All eight parts committed; no part left half-landed
- [ ] `tsc --noEmit`, `npm run build` and `npm run check:responsive` all clean
- [ ] The manual walk is recorded in `progress.md` with its result per route and width — not asserted, *recorded*
- [ ] No page scrolls horizontally at the body level at 320 px, on any of the 28 routes
- [ ] Desktop at 1440 px is unchanged except the intended repairs (26 dialog widths, toast position, `/settings`
      and `/users` gaining a gutter) — every one of those is listed in the plan's Breaking Changes
- [ ] Docs updated per [plan.md § Documentation to update on completion](../plan.md#documentation-to-update-on-completion)

## Notes

**Write the mechanical-check script first (P0).** It is listed under P1 in the plan but is most useful *before*
the bulk edits: it turns the invisible defect classes into a failing command while the work is in progress, rather
than a check run once at the end. It exists precisely because the 26-dialog `max-w` collision survived undetected
across the whole codebase — a defect nobody could see and no type could catch.

**Six corrections the plan verified against the spec** — use the plan's numbers, not the spec's: **27** `<Table>`
sites (22 converted, 5 argued exclusions) · **26** `AlertDialogContent` (not 32) · **116** `text-[Npx]` (not 121) ·
**30** shell instances across 24 files · `lg:` used **5×** (one apparent hit is a CVA size key) · the `viewport`
export is a **P1 prerequisite**, not P7 — without `viewportFit: "cover"`, `env(safe-area-inset-bottom)` is `0px`
and P2's bar sits under the home indicator.

**The four traps most likely to cost a session:**

1. ⚠️ **`next-themes` defaults to `attribute="data-theme"`.** `globals.css:4` declares a class-based dark variant.
   Mount it wrong and **none** of the 336 `dark:` utilities fire — while the provider looks correctly wired.
2. ⚠️ **`tsc` does not flag an unused destructured binding.** Removing the header hamburger orphans
   `setMobileOpen` and the `Menu` import, and with lint broken, nothing catches it. Grep after every removal.
3. ⚠️ **P8's anonymous endpoints break the build** until each is added to
   `ControllerAuthorizationCoverageTests.ExpectedAnonymous` — the guard pins the set by equality in **both**
   directions. Expected, cheap, and the most likely first failure of that part.
4. ⚠️ **Two `DialogContent` sites pass their class as a template literal** (`patient-files-manager.tsx:662`,
   `patients/[id]/page.tsx:2075`), so a naive `className="…"` grep misses them in both the fix and the check.

**Three things not to change:** `HOUR_HEIGHT = 48` (blocks are positioned from it via four `calc()` strings with a
documented drift invariant) · the odontogram charts' *interaction* (geometry only — pulling it up breaks either
the `476a2e3` two-channel touch fix or `record-tooth-chart`'s read-only reuse contract) · `--warning-ink` (it
exists because `--warning` lands near 3.5:1 on its own wash).

**Scope boundary.** The unfinished half of `features/app-design-language` sits one file away at all times
(`ListToolbar` on 1 of 12 pages, the card rule on 1 of 3, ~520 hardcoded colours). Only two fragments are in
scope — the type scale and filter-chip visibility — because responsiveness requires them. Everything else goes to
`follow-up/`, not into this story (plan risk **R-13**).
