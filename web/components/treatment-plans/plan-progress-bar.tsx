"use client"

interface PlanProgressBarProps {
  done: number
  total: number
  /**
   * 0…1, step-weighted. Overrides `done / total` for the *painted* width only — the announced value stays the
   * whole-act count, since a part-finished act is not a finished one.
   */
  fraction?: number
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
export function PlanProgressBar({ done, total, fraction, className }: PlanProgressBarProps) {
  if (total <= 0) return null

  /*
   * ⚠️ `fraction` wins when given, and it exists because `done / total` counts whole acts: a bridge two thirds
   * carried out left this bar empty on a patient who had already had two séances. The caller passes the
   * step-weighted figure (`planWorkProgress`); `done`/`total` stay for the callers that have no steps to weigh
   * and for the accessible value, which must remain a count of *acts* — « 0,67 actes réalisés » is not a
   * sentence anyone should hear.
   */
  const pct = Math.round((fraction ?? done / total) * 100)

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
