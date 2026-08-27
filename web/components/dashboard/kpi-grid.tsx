import { Children } from "react"
import { cn } from "@/lib/utils"

interface KpiGridProps {
  /**
   * Columns at the `xl` breakpoint. Below that the grid holds 2, except `columns={1}`, which is 1 everywhere.
   *
   * <p>`1` is a section's single lead figure — the dashboard's recut zones put the lead in its own one-cell grid
   * above the subordinate ones, rather than spanning cells inside theirs, because a `text-3xl` money figure does not
   * fit half of a 320 px grid. `3` and `4` are still used by `/caisse`, `/factures`, `/rappels` and the cheques
   * table.</p>
   */
  columns?: 1 | 2 | 3 | 4
  className?: string
  children: React.ReactNode
}

/**
 * One bordered surface holding several figures, separated by hairlines.
 *
 * <p>This is the de-boxing. The dashboard used to render every figure as its own `Card`: sixteen bordered,
 * shadowed, equally-weighted rectangles in a four-column grid, which gave every number the same importance and so
 * gave none of them any. Grouping a section's figures into a single surface drops the border count from sixteen to
 * three and lets the *section* be the object on the page, which is what it always was conceptually.</p>
 *
 * <p>The hairlines are `gap-px` over a `bg-border` container, with each child painting its own `bg-card` — not
 * `divide-x`/`divide-y`. That matters: `divide-*` in a wrapping grid draws dividers from DOM order, so the last row
 * of an uneven grid gets stray edges and the rules do not line up across the wrap.</p>
 *
 * <p>⚠️ <b>The same technique makes an unfilled last row visible, which is why this component pads it.</b> A grid
 * area with no child in it shows the <i>container's</i> background — and the container is `bg-border` — so a count
 * that does not fill its last row painted a grey block where a cell would be. On the dashboard the figure count is
 * <b>user-controlled</b> (« Personnaliser » hides « Facturé » and « Avoirs remboursés » by default), so that is the
 * normal case rather than an edge one. The fillers are `aria-hidden` empty cells and cost nothing to assistive
 * tech.</p>
 *
 * <p>⚠️ Padding is to a multiple of the <b>`xl` count</b>, which is exact for 1, 2 and 4 (4 and 2 both divide the
 * two-column base) and can still leave one gap at the base width for `columns={3}`. That is the pre-existing
 * behaviour for the one caller using 3, not a new defect — closing it would need a column count that divides both
 * breakpoints.</p>
 */
export function KpiGrid({ columns = 2, className, children }: KpiGridProps) {
  // `Children.toArray` already drops the `null`s that `page.tsx`'s `kpi()` returns for a hidden block, so this is
  // the count of cells that will actually paint.
  const cells = Children.toArray(children)
  const remainder = cells.length % columns
  const fillers = remainder === 0 ? 0 : columns - remainder

  return (
    <div
      className={cn(
        // `shadow-sm` un-inverts the elevation ladder. Without it the biggest surfaces on the dashboard sat flat on
        // the ground while the smaller cards beside them (the charts, the appointment list, all plain `Card`s)
        // floated above it — the page read as holes with objects over them, the opposite of the importance order.
        "grid gap-px overflow-hidden rounded-xl border bg-border shadow-sm",
        // TWO columns on a phone, not one. One figure per row turned the dashboard into screens of scrolling, and
        // « comment va le cabinet ? » is a question you answer by comparing figures — which you cannot do when only
        // one is ever in view. Two fits at 320 px because `KpiCard` steps its padding and value size down there.
        columns === 1 ? "grid-cols-1" : "grid-cols-2",
        columns === 3 && "xl:grid-cols-3",
        columns === 4 && "xl:grid-cols-4",
        className,
      )}
    >
      {cells}
      {Array.from({ length: fillers }, (_, i) => (
        <div key={`filler-${i}`} aria-hidden="true" className="bg-card" />
      ))}
    </div>
  )
}
