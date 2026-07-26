"use client"

interface PlanProgressBarProps {
  done: number
  total: number
  className?: string
}

/**
 * Actes-réalisés progress, hand-rolled: `components/ui/` has no `progress.tsx` and the project does not
 * depend on `@radix-ui/react-progress`, so this is two divs carrying the ARIA contract a progress role needs.
 *
 * Renders **nothing** when the plan has no acts — a zero-width bar would read as "0 % done" to a sighted user
 * and as `aria-valuemax="0"` (undefined progress) to a screen reader, when the truth is "there is nothing to
 * track yet". This also keeps the percentage arithmetic away from a divide-by-zero.
 */
export function PlanProgressBar({ done, total, className }: PlanProgressBarProps) {
  if (total <= 0) return null

  const pct = Math.round((done / total) * 100)

  return (
    <div
      className={`h-2 w-full overflow-hidden rounded-full bg-muted ${className ?? ""}`}
      role="progressbar"
      aria-valuenow={done}
      aria-valuemin={0}
      aria-valuemax={total}
      aria-label="Actes réalisés"
    >
      <div className="h-2 rounded-full bg-primary transition-all" style={{ width: `${pct}%` }} />
    </div>
  )
}
