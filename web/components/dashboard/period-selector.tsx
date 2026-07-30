"use client"

import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import type { DashboardPeriodKey } from "@/lib/api/types"
import { PERIOD_LABELS } from "./dashboard-labels"

const PERIODS: DashboardPeriodKey[] = ["Today", "Week", "Month"]

interface PeriodSelectorProps {
  value: DashboardPeriodKey
  onChange: (period: DashboardPeriodKey) => void
  disabled?: boolean
}

/**
 * The dashboard's one filter, in one row above everything it scopes.
 *
 * <p>Deliberately not per-section: every figure and the trend re-read against the same window, which is what lets the
 * numbers on the page be compared with each other. A per-card range would let two cards describe different months
 * while sitting side by side.</p>
 */
/**
 * One **segmented track**, not three separate buttons.
 *
 * <p>Three bordered pills side by side read as three independent actions; a single track with one filled thumb reads
 * as one control with three positions, which is what this is. It also stops the two unselected periods carrying a
 * border each — six edges for one decision was a meaningful share of the "boxy" feel.</p>
 */
export function PeriodSelector({ value, onChange, disabled = false }: PeriodSelectorProps) {
  return (
    <div
      role="group"
      aria-label="Période"
      className="inline-flex gap-0.5 rounded-full border bg-card p-0.5 shadow-sm"
    >
      {PERIODS.map((period) => {
        const selected = period === value
        return (
          <button
            key={period}
            type="button"
            aria-pressed={selected}
            disabled={disabled}
            onClick={() => onChange(period)}
            className={cn(
              "rounded-full px-3.5 py-1.5 text-sm transition-colors duration-150 ease-out",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1 focus-visible:ring-offset-card",
              "disabled:cursor-not-allowed disabled:opacity-60 motion-reduce:transition-none",
              selected
                ? "bg-primary font-semibold text-primary-foreground"
                : "text-muted-foreground hover-hover:hover:text-foreground",
            )}
          >
            {PERIOD_LABELS[period]}
          </button>
        )
      })}
    </div>
  )
}
