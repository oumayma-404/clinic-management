"use client"

import { Children, type ReactNode } from "react"
import type { LucideIcon } from "lucide-react"

import { KpiGrid } from "@/components/dashboard/kpi-grid"
import { STATUS_TONE_INK, STATUS_TONE_RAIL, type StatusTone } from "@/components/ui/status-tone"
import { cn } from "@/lib/utils"

/**
 * The one **summary strip** — the row of figures that sits under a page header and totals the list below it.
 *
 * <p>Four screens drew this same object four different ways, and the difference was visible from across a room:
 * la caisse and « Factures » put `text-2xl` figures on white cells behind a meaningless blue dot; « Chèques »
 * used a small uppercase label, an icon and a `text-xl` figure; « Rappels » filled every tile edge-to-edge with
 * its own pastel — a green, an azure, an amber and a red row that read as a traffic light rather than as a
 * clinic's software. They did not even break to the same number of columns: 4-up arrived at 640 px on one
 * screen, 1024 px on another and 1280 px on a third.</p>
 *
 * <p><b>One shape, and colour only where it is a fact.</b> A cell is a `--card` surface with a quiet uppercase
 * label, a `text-xl` figure and an optional hint. A `tone` colours <i>the figure and its glyph</i> and nothing
 * else — never the cell — so « Échecs 2 » still reads as the alarm it is while the strip as a whole stays the
 * neutral object it should be. That is the whole of the correction: the information the fills were carrying was
 * real, the fills were not the way to carry it.</p>
 *
 * <p><b>Why `text-xl` and not the dashboard's `text-xl sm:text-2xl`.</b> These are not the same object and the
 * repo already says so — a `KpiCard` is a link with a period comparison and an icon chip, and it earns its extra
 * step. A summary strip is four raw figures with nothing to click, and at `text-2xl` it is the heaviest thing on
 * a page whose content is the table underneath. Flat `text-xl` is also what keeps the base width safe:
 * « 19 460,000 DT » measures ~100 px against the ~112 px a two-column cell has at 320 px, and the `text-2xl`
 * la caisse used to ship overflowed it.</p>
 *
 * @see Stat — one cell.
 */
export function StatStrip({
  children,
  columns,
  className,
}: {
  /** {@link Stat} cells. A `null` child (a figure this deployment does not have) is dropped, not counted. */
  children: ReactNode
  /**
   * Override the column count. Normally omitted — it is the number of cells, which is what every caller wants
   * and is one fewer place for the grid and its contents to disagree.
   */
  columns?: 1 | 2 | 3 | 4
  className?: string
}) {
  // `Children.toArray` drops `null`/`false`, so a conditionally-absent figure does not reserve a column.
  const count = Children.toArray(children).length
  const cols = columns ?? (Math.min(4, Math.max(1, count)) as 1 | 2 | 3 | 4)

  return (
    <KpiGrid
      columns={cols}
      className={cn(
        /*
         * ⚠️ One breakpoint ladder for every strip, because the four had three between them. Two columns at the
         * base width — `KpiGrid`'s own note argues that one figure per row is a screen of scrolling for a
         * question you answer by *comparing* figures — then the full row at `lg:`, which is a tablet in
         * landscape. `KpiGrid` also emits its own `xl:` rule for 3 and 4; this simply arrives earlier.
         */
        cols === 3 && "sm:grid-cols-3",
        cols === 4 && "lg:grid-cols-4",
        className,
      )}
    >
      {children}
    </KpiGrid>
  )
}

interface StatProps {
  /** What the figure counts. Wraps rather than truncates — see the JSX. */
  label: string
  /** Pre-formatted: money through `formatDT`, counts through `toLocaleString("fr-TN")`. */
  value: ReactNode
  /** One short line under the figure naming its period or scope — « 7 derniers jours », « brut, hors avoirs ». */
  hint?: string
  /** Optional glyph beside the label. It takes the `tone` too, so an alarm is coloured twice and shouted once. */
  icon?: LucideIcon
  /**
   * The figure's semantic ink, through the app's one status palette.
   *
   * <p>Omitted for an ordinary figure, which is `--foreground`: a strip where every cell is coloured has no way
   * to say which one is news. `"neutral"` is the deliberate *quiet* case — a bucket at zero, an alarm with
   * nothing in it — and renders muted rather than plain.</p>
   */
  tone?: StatusTone
  /** Skeletons the figure only. The label and hint stay, so the strip does not change height on load. */
  loading?: boolean
  /**
   * Makes the cell a filter control. With it, {@link selected} says whether this cell's filter is the active
   * one; without it the cell is inert markup.
   */
  onSelect?: () => void
  selected?: boolean
}

/**
 * One figure inside a {@link StatStrip}.
 *
 * <p>⚠️ `bg-card` is load-bearing: the enclosing grid is a `bg-border` container showing through `gap-px`, so a
 * cell that does not paint its own background renders as a solid border block.</p>
 */
export function Stat({ label, value, hint, icon: Icon, tone, loading, onSelect, selected = false }: StatProps) {
  const ink = tone === undefined ? "text-foreground" : STATUS_TONE_INK[tone]

  const body = (
    <>
      {/*
        ⚠️ The label stays `--muted-foreground` whatever the tone is. Colouring it too was tried and it puts the
        hue back at the scale that caused the complaint: four coloured labels above four coloured figures is
        eight coloured things in a row, which is the filled tile again with the fill taken out. The tone belongs
        on the figure — the part that IS the news — and on the glyph that points at it.
      */}
      <span className="flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {Icon && <Icon aria-hidden="true" className={cn("size-3.5 shrink-0", ink)} strokeWidth={1.75} />}
        {/*
          Wraps rather than truncates, which is `KpiCard`'s rule for the same reason: at 320 px a two-column
          cell is narrower than « Avoirs remboursés », and « Avoirs rembour… » names nothing — the label IS the
          figure's name, and it is what a screen reader announces.
        */}
        <span className="min-w-0 [overflow-wrap:anywhere]">{label}</span>
      </span>
      {loading ? (
        <span className="mt-1.5 block h-7 w-24 max-w-full animate-pulse rounded bg-muted" aria-label="Chargement" />
      ) : (
        /* `whitespace-nowrap`: the label above deliberately wraps, but a FIGURE must not — at 320 px
           « 30 046,200 DT » broke after the number and left « DT » alone on the next line, which reads as a
           second value. The figure is short enough to never need the wrap. */
        <span
          className={cn(
            "mt-1.5 block whitespace-nowrap text-xl font-semibold tabular-nums tracking-tight",
            ink
          )}
        >
          {value}
        </span>
      )}
      {hint && <span className="mt-0.5 block text-xs text-muted-foreground">{hint}</span>}
    </>
  )

  // `py-3.5` rather than the `p-4` all four strips used to carry. Twelve pixels a row does not sound like a
  // design decision until four figures, their labels and their hints are the first thing on a money screen.
  const surface = "relative block w-full bg-card px-4 py-3.5 text-start"

  if (!onSelect) return <div className={surface}>{body}</div>

  return (
    <button
      type="button"
      aria-pressed={selected}
      onClick={onSelect}
      className={cn(
        surface,
        "transition-colors duration-[160ms] ease-snap",
        // No `touch-target`: the cell is its own ~96 × 112 px hit area, comfortably past the 44 px floor.
        // A background hover may stay ungated by § 9, but not here — this cell HAS a selected state drawn in
        // `bg-accent`, so a hover that lingers after a tap would claim a filter that is not applied.
        selected ? "bg-accent/60" : "hover-hover:hover:bg-accent/40",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring",
      )}
    >
      {/*
        The selected rail — 2 px in the cell's own tone, the idiom `KpiCard` already uses for an urgent figure.
        Deliberately not a filled cell and not a ring: a fill is what made « Rappels » read as a traffic light,
        and a ring inside a `gap-px` hairline grid doubles up with the hairline it sits on.

        ⚠️ `STATUS_TONE_RAIL`, not a `ring-*` utility, because this is an inline `backgroundColor` — and it is
        the raw `--success` / `--warning` tokens rather than the `--color-*` aliases for the reason recorded on
        that map: a `@theme inline` alias is emitted only when Tailwind judges it used, so `var(--color-…)` in
        an inline style paints transparent whenever a build decides otherwise, silently.
      */}
      {selected && (
        <span
          aria-hidden="true"
          className="absolute inset-y-0 start-0 w-[2px]"
          style={{ backgroundColor: STATUS_TONE_RAIL[tone ?? "accepted"] }}
        />
      )}
      {body}
    </button>
  )
}
