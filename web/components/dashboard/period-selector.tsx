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
export function PeriodSelector({ value, onChange, disabled = false }: PeriodSelectorProps) {
  return (
    <div role="group" aria-label="Période" className="flex flex-wrap gap-2">
      {PERIODS.map((period) => {
        const selected = period === value
        return (
          <Button
            key={period}
            type="button"
            size="sm"
            variant={selected ? "default" : "outline"}
            aria-pressed={selected}
            disabled={disabled}
            onClick={() => onChange(period)}
            className={cn(!selected && "text-muted-foreground")}
          >
            {PERIOD_LABELS[period]}
          </Button>
        )
      })}
    </div>
  )
}
