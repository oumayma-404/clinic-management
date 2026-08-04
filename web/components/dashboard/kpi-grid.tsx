import { cn } from "@/lib/utils"

interface KpiGridProps {
  /** Columns at the `xl` breakpoint. Below that the grid steps down to 2 and then 1. */
  columns?: 2 | 3 | 4
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
 * of an uneven grid gets stray edges and the rules do not line up across the wrap. The gap technique produces
 * correct hairlines for any item count at any breakpoint, which a dashboard whose figure count is now user-controlled
 * genuinely needs.</p>
 */
export function KpiGrid({ columns = 4, className, children }: KpiGridProps) {
  return (
    <div
      className={cn(
        // `shadow-sm` un-inverts the elevation ladder. Without it the three biggest surfaces on the dashboard —
        // Argent, Activité, À-traiter — sat flat on the ground while the two *smaller* cards below them (the
        // trend chart and the appointment list, both plain `Card`s) floated above it. The page read as three
        // holes with two objects over them, which is the opposite of the importance order.
        "grid gap-px overflow-hidden rounded-xl border bg-border shadow-sm",
        // TWO columns on a phone, not one. One figure per row turned the eight-figure dashboard into eight
        // screens of scrolling, and « comment va le cabinet ? » is a question you answer by comparing figures
        // — which you cannot do when only one is ever in view. Two fits at 320 px because `KpiCard` steps its
        // padding and value size down in the same breakpoint (AC-47).
        "grid-cols-2",
        "sm:grid-cols-2",
        columns === 3 && "xl:grid-cols-3",
        columns === 4 && "xl:grid-cols-4",
        className,
      )}
    >
      {children}
    </div>
  )
}
