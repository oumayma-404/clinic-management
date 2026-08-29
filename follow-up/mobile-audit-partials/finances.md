# Mobile audit — FINANCES + HOME slice

Routes: `/` `/caisse` `/a-cloturer` `/factures` `/creances` `/cheques` `/abonnement` `/journal`
Measured with the shared harness (`audit.mjs`), `hasTouch: true` confirmed (`coarse: true` on every record, so
the 44 px numbers below are real coarse-pointer measurements, not fine-pointer ones). Two full passes: 390 px and
320 px. Session expired twice mid-run (`redirectedToLogin: true` on `/journal` in the first 390 px pass, then on
the whole slice at the start of the 320 px pass) — both times fixed with one `refresh-session.mjs` run and a clean
re-run; no route needed the login form walked by hand.

## Per-route table

| Route | overflowX @390 | overflowX @320 | smallTargetCount @390 | smallTargetCount @320 | Verdict |
|---|---|---|---|---|---|
| `/` (home) | 0 | 0 | 9 | 9 | clean |
| `/caisse` | 0 | 0 | 37 | 15 | degraded (see findings 1, 4a) |
| `/a-cloturer` | 0 | 0 | 80 | 80 | clean (see calibration note) |
| `/factures` | 0 | 0 | 56 | 56 | degraded (finding 1) |
| `/creances` | 0 | 0 | 1 | 1 | clean (withdrawn page, informational only) |
| `/cheques` | 0 | 0 | 19 | 17 | degraded (findings 1, 2) |
| `/abonnement` | 0 | 0 | 1 | 1 | clean |
| `/journal` | 0 | 0 | 5 | 5 | clean |

**Headline: zero horizontal overflow measured on any of the 8 routes at either width.** No `visibleTables` at
390/320 either — every list on this slice already renders as cards below the table hinge. This slice does not
reproduce the sideways-scroll defect class at all.

**Calibration note on `smallTargetCount` — read before trusting the raw numbers.** Every `<Button>` in this app
bakes `.touch-target` into its base class (`web/components/ui/button.tsx:22-25`), which grows an invisible 44 px
hit box via a `::after` pseudo-element under `(pointer: coarse)` (`web/app/globals.css:750-762`) **without**
changing the painted size. So a measured "32×81 button" is not a violation — the real tap area is 44px, just
invisible to `getBoundingClientRect()`. Likewise every `CardList` row's title link stretches over the **whole
card** via `after:absolute after:inset-0`, so a "24px-tall link" is really a card-sized target; I confirmed in
`web/components/ui/card-list.tsx:257,281-283` that the row's `primaryAction`/`actions` slots are deliberately
`relative z-10` specifically so they sit *above* that stretched overlay and are never shadowed by it (this is the
documented fix for the "touch-target siblings stealing taps" anti-pattern in `frontend-web.md` §2, not an
instance of it). Given that, on `/a-cloturer` the 80 flagged elements are almost entirely: patient-name links
(stretched, real target = whole card) + « Venu »/« Absent » buttons (touch-target already covers the shortfall).
I looked at the screenshots specifically for overlap/mis-tap risk and found none — buttons render with clean
gaps, no crowding. Same story for `/factures`' 56 (row-title buttons + row action-menu triggers, all
`touch-target`-covered) and `/caisse`'s pager/action buttons. **Only two of the flagged elements across the whole
slice are genuine, confirmed violations — finding 3 below.**

## Findings, worst first

### 1. Money figures wrap inside `StatStrip`, splitting the currency unit onto its own line — confirmed on 2 screens
- **Routes / viewport:** `/factures` and `/cheques`, at 320 px (390 px is clean on both).
- **What's wrong:** `Stat`'s value renders in a plain `<span>` with no `whitespace-nowrap`. At 320 px the base
  grid is 2 columns, and the money string (`formatDT`'s "1 700,000 DT" / "30 046,200 DT") wraps at the space
  before the unit, so "DT" drops onto its own line under the figure.
- **Evidence:** `follow-up/mobile-audit-shots/320-factures.png` — "TOTAL FACTURÉ" reads "30 046,200" / "DT" on
  two lines; "TOTAL ENCAISSÉ" the same. `follow-up/mobile-audit-shots/320-cheques.png` — "EN RETARD" reads
  "1 700,000" / "DT", "BIENTÔT" reads "1 335,000" / "DT".
- **Source:** `web/components/ui/stat-strip.tsx:129` —
  `<span className="mt-1.5 block text-xl font-semibold tabular-nums tracking-tight">{value}</span>` — no
  `whitespace-nowrap`/`text-nowrap`.
- **Note:** this directly contradicts the component's own docstring (`stat-strip.tsx:26-31`), which claims
  « 19 460,000 DT » "measures ~100 px against the ~112 px a two-column cell has at 320 px" and therefore fits.
  That may have been true for the exact string measured at the time; it is not true for the strings this slice's
  live data produced. Worth re-measuring rather than trusting the comment.
- **Likely repeats on `/caisse`'s 4-up strip** (same component, same-length money strings — "19 776,000 DT" is
  the same character class), but I could **not** confirm this visually: both 320 px captures of `/caisse` were
  cut by the viewport fold right at "ENCAISSEMENTS 19776,000…", before the wrap point would show. Flagged as
  **probable, not confirmed** — a follow-up capture scrolled ~200 px further down `/caisse` at 320 px would settle
  it.
- **Pattern:** this is the *same wrong class* (missing `whitespace-nowrap`/`text-nowrap` on a `StatStrip` value)
  on at least 2 of the 4 screens that use `ui/stat-strip.tsx` — a single fix in `stat-strip.tsx:129` fixes all of
  them at once (including the unconfirmed `/caisse` case), rather than patching each page.

### 2. A static French guillemet quote wraps its closing » onto its own line at 320 px
- **Route / viewport:** `/cheques`, 320 px only.
- **What's wrong:** the page header's subtitle sentence — "…et reste consultable sous « Encaissés »." — wraps
  so that the closing guillemet lands alone on the last line: "…sous « Encaissés" / "»." 390 px is clean.
- **Evidence:** `follow-up/mobile-audit-shots/320-cheques.png`, last line of the intro paragraph.
- **Source:** `web/app/cheques/page.tsx:34` — a plain string literal,
  `` `…reste consultable sous « Encaissés ».` ``, with ordinary breakable spaces around both guillemets, not run
  through `quoteFr()`.
- **Caveat:** `frontend-web.md` §15 documents `quoteFr()` and its `french-quote-binding` check specifically for
  **dynamic** quoted values (a search term, a filename) — this is static prose, so it may be outside that check's
  scope by design, and I'm not asserting it fails the mechanical gate. But the visual failure mode is identical
  (an ordinary space is a break opportunity, so the closing guillemet floats free at a narrow width), confirmed
  directly in the screenshot. Whether static guillemet prose should also route through `quoteFr()` (or just use a
  narrow no-break space by hand) is a judgement call for whoever owns that check.

### 3. Two genuine sub-44px coarse touch targets (not covered by `.touch-target`)
Both on `/` (home dashboard), confirmed at both 390 and 320 px.

- **3a. "Durée"/"Nombre" segmented switch, `ProcedureMixChart`** — a raw `<button>` (not the shared `Button`
  component, so no baked-in `.touch-target`) with `coarse:min-h-10`, i.e. a 40 px coarse floor, under the 44 px
  rule. Measured `h:40` in both passes.
  Source: `web/components/dashboard/procedure-mix-chart.tsx:92` —
  `"min-h-8 rounded-full px-3 text-xs font-medium transition-colors coarse:min-h-10 coarse:px-4"`.
- **3b. "Ouvrir l'agenda →" link, `DashboardSection`** — a plain `<Link>`, no `touch-target`, no coarse floor,
  painted 16 px tall. Measured `h:16` in both passes.
  Source: `web/components/dashboard/dashboard-section.tsx:83-88`.
  Low severity: a full 36 px `touch-target`-covered duplicate CTA ("Ouvrir l'agenda", no arrow) sits directly
  below it, same destination, so this is a redundant secondary link rather than the only way through.

### 4. Judgement calls — not rule violations, but worth naming (label per §"discipline": these are calls, not measurements)
- **4a. `/caisse`'s actions row is 3–4 stacked rows before any content appears**, at both 390 and 320 px: [Du] /
  [Au + Aujourd'hui] / [Exporter + Nouvelle dépense] at 390, reflowing to [Du] / [Au] / [Aujourd'hui + Exporter] /
  [Nouvelle dépense] at 320. No overlap, no clipped text, no touch-target violation — it's a correctly-built
  `flex flex-wrap` `actions` slot (`web/app/caisse/page.tsx:421`) doing exactly what §10.1 asks. But on the
  screen the user specifically called out for "filter cards," this is the closest match I found to that
  complaint: a lot of vertical space (roughly half the first viewport at 320 px) spent on date/action controls
  before the stat cards or any data appear. I'm flagging it as a density complaint, not a defect.
- **4b. `/a-cloturer`'s per-row status badges ("Venue"/"Fiche"/"Encaissement") wrap to 2 lines at 320 px**,
  leaving "Encaissement" alone on the second line (390 px fits all three on one line). Not overlapping, not
  unreadable — just visually uneven. Source: `ClosureProgress` in
  `web/components/visits/visit-closure-list.tsx` (~line 416, `<ul className="flex flex-wrap gap-1.5">`).

## Pattern across routes

The one repeating, fixable-in-one-place issue is **finding 1**: `ui/stat-strip.tsx`'s `Stat` value has no
`whitespace-nowrap`/`text-nowrap`, so any money figure long enough to approach the 2-column cell width at 320 px
wraps and splits its unit onto its own line. This hits every screen using `StatStrip` (`/caisse`, `/factures`,
`/cheques`, and — outside this slice — `/rappels`), confirmed on 2 of the 4 in this slice and suspected-but-
unconfirmed on the third (`/caisse`).

## Coverage

- **Walked, both widths, all clean loads (200), no 404/error:** all 8 routes.
- **Session hiccups:** `/journal` redirected to login on the first 390 px attempt; the entire slice redirected to
  login at the start of the 320 px pass. Both fixed with `node ~/.claude/playwright/refresh-session.mjs`, then a
  clean re-run — no residual effect on the data reported above (all numbers here are from the clean runs).
- **Empty / thin-data routes:** `/creances` is a withdrawn page (`ui/retired-page-card.tsx`) — informational only,
  nothing to audit for list layout. `/abonnement` shows the "Essai gratuit" trial state with no payment history
  rows (admin-gated section not populated in this environment). `/journal` shows the filter form only in both
  captures — the actual log rows below were not visually confirmed (viewport-clipped in both passes), so table→
  card behaviour for the log itself is **not covered** by this pass.
- **Viewport-clipped, not full-page:** the harness screenshots the initial viewport, not the full scrollable
  page — and this app uses an internal scroll container (`AppShell`'s `<main>`), so `docScrolls`/`scrollHeight`
  read as "no scroll" even on long pages; that field is not informative here. `/caisse` at 320 px was cut by the
  fold before its stat-figure row (see finding 1's caveat) and before the encaissements/dépenses tables — those
  were only reviewed at 390 px.
- **Not reached at all:** nothing — every route in the assigned slice was walked at both widths.
