# Design — Tableau de bord : refonte visuelle

**Status: APPROVED — « Menthe clinique » (deep teal), implemented.**

⚠️ **The change is app-wide, not dashboard-only.** The cause is in `globals.css` tokens, so retinting them moves
every page: a teal dashboard on a tinted ground beside blue-on-white pages would look broken. That was the correct
scope but it is worth knowing before reviewing other screens.

Mockup: `features/dashboard-redesign/mockups/01-dashboard.html` (before/after, live variant switcher, light + dark).

## Deviations from `/design-ui`

| Step | What happened |
|------|---------------|
| 0 — prerequisites | **No `spec.md`.** The brief is aesthetic and arrived directly: *« pas moderne, trop délavé, un pop de couleur, smooth et invitant »*. Nothing about the information architecture is in question, so there is no functional spec to write first. |
| 3–4 — browser exploration | **No browser tooling in this repo** (no `agent-browser`, no `scripts/start-dev.sh`). Per the skill's fallback, grounded by reading source: `app/page.tsx`, `components/dashboard/*`, `lib/dashboard-links.ts`, `app/globals.css`. The « Avant » block in the mockup is a faithful re-render of those exact tokens and class strings, which is what the browser step exists to establish. |

## Diagnosis — « délavé » is structural, not a matter of taste

Read off `app/globals.css`:

| Cause | Current value | Consequence |
|---|---|---|
| Ground vs card | `--background: oklch(0.99 0 0)` vs `--card: oklch(1 0 0)` | **1 % apart.** Nothing lifts off the page; the whole screen is one sheet with hairlines drawn on it. |
| Surface chroma | `--muted`, `--secondary`, `--border`, `--accent`: **0.005 → 0.01** | Technically grey. Neutrals may be near-grey, but with a white ground and no accent surface, nothing has any colour identity at all. |
| Accent deployment | `--primary: oklch(0.52 0.14 245)` | Exists, and appears **only** on one small active button and the chart line. **Zero filled accent surfaces.** |
| Radius | `--radius: 0.5rem` on everything | Tight and utilitarian — the opposite of the "smooth" asked for. |
| Deltas | bare coloured text (`text-green-700` / `text-destructive`) | The least modern treatment available for a comparison. |
| Hierarchy channel | size only (`text-4xl` / `2xl` / `xl`) | With no colour or elevation, rank is carried by one channel doing all the work. |

**What is *not* wrong:** the information architecture. Argent first with « Net » as hero, Activité, À traiter compact, Tendance, RDV du jour; every figure a link built from the server's own bounds; the customiser; the hold-previous-render-on-refetch behaviour. All of it is kept unchanged.

## Design plan

**Colour** — one accent, three candidate hues, switchable live in the mockup. Semantic state colours are deliberately **outside** the variant: a state that takes the brand hue stops being a state.

| Role | Menthe clinique *(recommended)* | Bleu profond | Ambre chaud |
|---|---|---|---|
| accent | `oklch(0.60 0.115 185)` | `oklch(0.545 0.155 250)` | `oklch(0.665 0.135 62)` |
| hero fill | `oklch(0.455 0.095 190)` → `oklch(0.38 0.085 195)` | `0.435 0.145 253` → `0.34 0.12 256` | `0.50 0.115 55` → `0.40 0.095 50` |
| ground | `oklch(0.975 0.008 210)` | `oklch(0.975 0.008 250)` | `oklch(0.978 0.012 82)` |
| ink | `oklch(0.20 0.015 230)` | `0.20 0.015 255` | `0.21 0.015 60` |

Constant across all three: `--good oklch(0.55 0.13 155)`, `--bad oklch(0.55 0.19 25)`, `--warn oklch(0.68 0.15 72)`, each with a `-wash` for pill backgrounds.

**Type** — the app's Geist stays. Figures carry the page: `tabular-nums`, tight tracking (−0.02 → −0.035em at hero size). A monospace utility face joins for section eyebrows, chart axis and clock times — it encodes "data" and gives the second role real work. *(The mockup falls back to the system sans stack: the Artifact CSP blocks font CDNs and inlining Geist as a data URI is not worth the weight for a layout study.)*

**Layout** — unchanged order. What changes is treatment: a tinted ground so surfaces lift, 16 px radius, one soft shadow **per surface** (not per cell), and a single filled accent panel for « Net » with the trend sparkline inside it.

## The seven changes

1. **The ground is tinted** — `0.975` at chroma `0.008`, not neutral `0.99`. A visible ground/card gap is what makes surfaces exist.
2. **One filled surface** — « Net » becomes an accent panel with its sparkline inside. The pop comes from *one* surface, not six accents competing.
3. **Deltas become tinted pills** — wash background + coloured text. Meaning still rests on the arrow *and* the sign, never colour alone.
4. **16 px radius, one soft shadow per surface** — the "smooth". Sixteen shadows would be noise.
5. **Sections differentiate by type, not by hue** — a monospace uppercase eyebrow. A coloured rail per section would be a carnival and would also spend the accent where it earns nothing.
6. **A zero must look calm** — « Salle d'attente 0 » recedes; « Prothèses en retard 2 » tints red with a small chip. Live counts read, empty ones stay quiet.
7. **State colour is not the accent** — green/amber/red are identical in all three variants.

## Risk taken

The filled hero panel with an inline sparkline. It is the one bold move, and everything around it is deliberately quiet. It also relocates the trend's *shape* to where the money question is asked, while the full six-month chart stays below for reading values.

**Ambre chaud's specific risk:** it shares its hue with the patient page's amber alert band (`features/patient-notes-strip`). At that point "warm and inviting" and "warning" look alike — the only real hazard among the three.

## What this costs to build

No data component changes. `KpiGrid`, `KpiCard`, `DashboardSection`, `CollectedTrendChart` keep their APIs. The work is:

- `app/globals.css` — token values (ground, chroma on neutrals, accent, `--radius`, new `--good/--warn` + wash pairs).
- `kpi-card.tsx` — the `hero` emphasis paints an accent surface; `DeltaBadge` becomes a pill; a small accent dot replaces the dropped icon tiles on normal cells.
- `dashboard-section.tsx` — eyebrow typography.
- `collected-trend-chart.tsx` — gradient area, emphasised + direct-labelled endpoint, monospace axis.
- `period-selector.tsx` — three buttons become one segmented track.
- `appointment-list.tsx` — rows gain a tabular time column, initials avatar, act chips, status pill.

Unchanged: the period selector's behaviour, the customiser, every drill-down link, the loading / « Indisponible » + retry states, and the realtime refetch.

## Accessibility

Delta meaning is carried by arrow + sign + accessible label, not colour. The hero's white-on-accent is checked in both themes for each variant. `:focus-visible` on every cell (inset ring, since cells share a surface). The chart carries a full `role="img"` + `aria-label` naming every month and value; « À traiter » chips are text, not colour-only. `prefers-reduced-motion` disables the variant/theme transitions.

## As built

| File | Change |
|---|---|
| `app/globals.css` | The palette. Ground `oklch(0.975 0.008 215)` against white cards (was a 1 % gap); neutrals biased to hue **215**, between the accent 188 and the old blue 250, so grey reads as chosen; `--primary oklch(0.49 0.105 188)`. **0.49, not the 0.60 the accent reads as elsewhere** — it is a button fill carrying near-white type, and 0.60 lands near 3.3:1, under the floor. `--chart-1` takes the brighter 0.60 step, where contrast with type is not the constraint. New `--success` / `--warning` / `--destructive-wash` families and `--hero-*`. `--radius` 0.5 → 0.75rem. Dark widens the ground/card gap to 5 points, since elevation is harder to read on a dark ground. |
| `components/dashboard/hero-kpi.tsx` | **New.** The one filled accent surface: radial glow over a diagonal ramp (a flat fill at this size reads as a colour swatch), a translucent-white delta pill, an icon watermark at 10 %, and the inline sparkline. Its own file because it shares no markup with a grid cell beyond being a `Link` — folding it into `KpiCard` is what produced an `isHero` ternary on nearly every line there. |
| `kpi-card.tsx` | Delegates `emphasis="hero"` wholesale. Cells gain a 6 px accent dot on the label row (identity without the 40 px tiles). `DeltaBadge` becomes a **tinted pill** on `--success-wash` / `--destructive-wash` instead of bare `text-green-700`, and the baseline moves to `sr-only` — repeated sixteen times it was more ink than the figures, and the section header states it once. |
| `app/page.tsx` | The hero moves **outside** `KpiGrid` into its own column (`lg:grid-cols-[1.05fr_2fr]`); the grid drops to 3 columns. A filled cell inside a hairline grid reads as a rendering fault and the shared border would cut across the panel's edge. `netCard` is hoisted so the row can go full-width when the user has « Net » hidden. |
| `dashboard-section.tsx` | Title becomes a monospace uppercase eyebrow. Sections differentiate by **type, not hue** — the accent stays reserved for the hero. |
| `period-selector.tsx` | Three bordered buttons → one segmented track with a filled thumb. Six edges for one decision was a real share of the boxiness. |
| `collected-trend-chart.tsx` | Wash 0.16 → 0.26 (at 0.16 against the new tinted ground the fill was nearly invisible), 2.4 px stroke, monospace axis, and a **permanent endpoint marker** — the last month is the figure the hero reports, so it earns an anchor the eye can find without hovering. |
| `appointment-list.tsx` | Bordered rows → one hairline-separated list; monospace tabular time leads each row so the hours align; acts render as chips rather than one grey sentence. |

Unchanged: every drill-down link, the period behaviour and URL sync, the customiser, loading / « Indisponible » + retry, the realtime refetch, and `KpiGrid`'s hairline technique.

## Still open

Whether « À traiter » should pick up the accent wash on a live count. Shipped restrained — the hero is the only filled surface.
