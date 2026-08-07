# Design — Langage visuel de l'application

**Status: APPROVED — building all five items. Three complete, two partial.**

Decisions taken in the absence of an answer: the zone eyebrow is **kept**, and sticky table headers apply **only to
paged lists** (a sticky header on a five-row table is a shadow that never earns itself).

| Item | State |
|---|---|
| 1 · The 315 blues | ✅ **0 remain** (2 deliberate: the odontogram's clinical blue) |
| 2 · `DataTable` | ✅ primitive done; adopted on `invoices-table` only |
| 3 · Status badges → tokens | ✅ all four label maps + the invoice table's inline `switch` |
| 4 · `PageHeader` / `ListToolbar` | ⚠️ `PageHeader` on **12 pages**; `ListToolbar` on **`/patients` only** |
| 5 · The card rule | ⚠️ `/caisse` done; `/factures` and the plan workspace not |

Mockup: `features/app-design-language/mockups/01-design-language.html` — audit, then each primitive before/after on real screens, light + dark.

## Deviations from `/design-ui`

| Step | What happened |
|------|---------------|
| 0 — prerequisites | **No `spec.md`.** This is a consistency pass over existing screens, not a feature. Nothing functional changes. |
| 3–4 — browser exploration | **No browser tooling in this repo.** Per the skill's fallback, grounded by reading source and by counting: every `<h1>` in `app/`, `components/ui/table.tsx`, `components/ui/card.tsx`, the toolbar in `app/patients/page.tsx`, the columns in `factures/invoices-table.tsx`, the figures in `app/caisse/page.tsx`, and a repo-wide grep of hardcoded colour utilities. |

## Audit — measured, not impressions

| Finding | Count | How it was counted |
|---|---|---|
| Hardcoded colour utilities | **928** | `grep -rEo "(text\|bg\|border\|ring\|from\|to)-(green\|red\|amber\|orange\|emerald\|yellow\|blue\|sky\|teal)-[0-9]{2,3}"` over `components/` + `app/` |
| …of which **blue/sky** | **315** | Same grep restricted to `blue\|sky` |
| Files affected | **56** | `grep -rl` of the same pattern |
| Distinct page-header treatments | **4** | Every `<h1>` in `app/`: `text-3xl font-semibold` ×10, `text-2xl font-bold` ×2, `text-xl font-semibold` ×1, plus one blue gradient clip-text |
| Card idioms in use | **4** | Dashboard shared-surface grid · patient-page `border-y` bands · `Card`+`Table` on /factures and /caisse · stacked `Card`s in the plan workspace |

## ⚠️ Urgent, and my fault

**315 of those hardcoded utilities are blue** — `text-blue-600`, `bg-blue-600`, `bg-blue-50` and friends. They were painting the *old* `--primary`, so before the teal change they roughly coincided with the theme and the duplication was invisible. Moving `--primary` orphaned them: those 315 spots now render the previous accent beside the new one, on the same screens.

This is a **regression introduced by the dashboard token change**, not pre-existing debt, and it should be fixed before anything else here. Most visible: `app/documents/page.tsx` (a blue gradient page title), `factures/invoices-table.tsx`, `user-management.tsx`, `setup-wizard.tsx`, `join-wizard.tsx`.

## The four primitives

### 1 · `PageHeader`

Fifteen pages hand-roll their header. Proposed: a monospace uppercase **zone** eyebrow (Dossiers / Argent / Clinique / Paramètres — the same register as the dashboard's section labels), one title size (26 px / 650), a subtitle carrying **a fact rather than a paraphrase** (« 1 284 dossiers · 23 ce mois », not « Consultez et gérez tous les dossiers patients », which describes the page to someone already looking at it), and the actions right-aligned with exactly one primary. No colour, no gradient on a title.

### 2 · `ListToolbar`

Every list page rewrites its search + filters. Today filters are **buttons whose label flips** (« Afficher les signalés » ↔ « Signalés affichés »), so the active state has to be read rather than seen, and a filter carries the same weight as the page's primary action.

Proposed: search, then filters as **counted chips** with `aria-pressed` — a chip tells you what the filter will cost before you click it — and the primary action moves up into the `PageHeader`. The toolbar then contains only things that *reduce* the list.

### 3 · `DataTable`

The least-designed surface and the most looked at. `ui/table.tsx` is stock shadcn: `p-2` everywhere, column heads in `text-foreground` (**the same colour as the data**, so the eye cannot tell which is content), and **no `tabular-nums` anywhere** — on the three dinar columns of `/factures` the commas do not line up.

Proposed:
- Column heads drop a level: monospace, uppercase, `muted-foreground`.
- `tabular-nums` on every numeric column, dates included.
- **The unit is stated once**, in the table footer, not fifteen times down a column where it pushes the digits out of alignment.
- A zero fades — « Reste 0,000 » is the absence of a debt, not a figure to read.
- Hairlines between rows, no border per row; sticky header (past ten rows of 128 you lose the columns).
- A cancelled row dims rather than staying black next to a red pill.

Most of this lands in `ui/table.tsx` and reaches eight list pages without touching them.

### 4 · One card rule

- **Figures → the shared-surface grid** (`KpiGrid`): one border per group, hairlines between cells.
- **Content → a `Card`**: a table, a form, a chart.
- **An entity header band → `border-y`, no card**: the patient-page pattern, reserved for a summary under a name.
- **Nothing else.** The stacked-`Card` idiom becomes one of the three above depending on what it holds.
- **Never a card inside a card** — a bordered row inside a bordered surface is the real source of "too many boxes".

Shown on `/caisse`, whose four figures become the dashboard's own treatment: one border instead of four, and « Net » — the result — takes the accent, which four identical cards could not express.

### 5 · Route the 928 colours through the theme

`--success` / `--warning` / `--destructive` + their `-wash` pairs already exist from the dashboard work. Status badge sets in `invoice-labels.ts`, `treatment-plan-labels.ts`, `appointment-labels.ts` and `lab-order` labels each pick their own green/amber/red — two greens and two ambers for the same idea — and each maintains dark mode by hand. Four `Record`s of class strings, rewritten to the tokens, and `dark:` disappears from them.

## Proposed order

| # | Work | Scope | Why here |
|---|---|---|---|
| 1 | The 315 blues | ~19 files, mechanical | A regression, not a preference. Blocks judging anything else. |
| 2 | `DataTable` | 1 primitive + 8 pages | Largest visible gain per unit of work; most lands in `ui/table.tsx`. |
| 3 | Status badges → tokens | 4 label files | Tokens exist; dark mode stops being hand-maintained. |
| 4 | `PageHeader` + `ListToolbar` | 2 primitives + 15 pages | Mechanical but broad; does the most for "this is one product". |
| 5 | The card rule | /caisse, /factures, plan workspace | Last: the only item needing a per-screen judgement (figures or content?). |

## Accessibility

Filter chips carry `aria-pressed`, so state is not colour-only. Status badges keep their text label — the wash is redundant encoding. The sticky table header stays a real `<thead>`. Column heads dropping to `muted-foreground` were checked to stay above 4.5:1 on both card surfaces. Numeric alignment is `font-variant-numeric`, not spacing hacks, so it survives zoom.

## Open questions

1. How much to build now — item 1 alone, items 1–3, or the whole list?
2. `PageHeader`'s eyebrow: the four zones above, or drop the eyebrow and keep title + subtitle only?
3. Should the sticky table header apply everywhere, or only to lists that page (invoices, patients, appointments)?


## As built

### 1 · The blues — 315 → 0

- A **`StatusTone`** scale (`components/ui/status-tone.ts`) with six tones: `neutral` · `pending` · `accepted` ·
  `active` · `positive` · `negative`. Few on purpose — a colour per status is a legend nobody learns.
- ⚠️ **`--warning-ink` was added.** `--warning` sits at L 0.62, which lands near 3.5 : 1 against its own wash — under
  the floor for badge text. The amber tone uses the darkened step; success and destructive clear it unaided.
- `/documents`' five document types are a **categorical** palette, so they route to `--chart-1…5` (already retinted),
  **not** to `--primary` — collapsing them would erase a real distinction. Its gradient-clipped title and floating
  64 px logo are gone, replaced by `PageHeader`.
- ⚠️ **One over-reach caught and reverted**: the script turned the odontogram's `Obturation` blue into `bg-primary`.
  That palette is a *clinical charting convention* mirrored by a `color` hex the SVG paints with, and teal is already
  `Bridge` — so it stays literal blue, now with a comment saying why.

### 2 · `DataTable` (`components/ui/table.tsx`)

Column heads drop to monospace uppercase `muted-foreground` (they were `text-foreground`, as black as the data);
`<TableCell numeric>` right-aligns with `tabular-nums`; `px-3 py-2.5` replaces a flat `p-2`; hairline row borders with
an accent hover; `<TableHeader sticky>` and `<TableRow muted>` are opt-in; `TableMeta` states unit + count once.
`formatAmount()` was added beside `formatDT()` so a money column can be pure number with « (DT) » in its header —
the suffix's varying width was undoing the alignment `numeric` buys.

### 3 · Primitives

`ui/page-header.tsx` — zone eyebrow, one 26 px title, fact-carrying subtitle, one primary action.
`ui/list-toolbar.tsx` — search + `FilterChip`s with **stable labels** and `aria-pressed` (they were Buttons whose
text flipped, so state had to be inferred from a sentence's tense) and optional counts.

### 4 · `/caisse` card rule

Four `Card`s → one `KpiGrid` surface with a local `CaisseFigure` cell. Deliberately not `KpiCard`: these are not
links (the statement below *is* the detail) and carry no comparison, so reusing it would mean an `href="#"`.

## Not done

- `ListToolbar` on the other list pages (invoices, stock, lab-orders, treatment-plans, waiting-list).
- `numeric` / `sticky` / `TableMeta` on tables other than invoices.
- The card rule on `/factures` and the plan workspace.
- ~682 hardcoded colour utilities remain — mostly the odontogram's deliberate clinical palette, plus inline
  amber/green/red panels that are not part of a badge map.
- ⚠️ `app/recalls/page.tsx` received a `PageHeader`, but a concurrent session is renaming that route to
  `app/rappels` (both folders currently exist). The edit may need re-applying to the new file.
