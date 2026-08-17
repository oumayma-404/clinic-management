"use client"

import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import type { DashboardPeriodKey } from "@/lib/api/types"
import { PERIOD_LABELS, PERIOD_LABELS_SHORT } from "./dashboard-labels"

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
      /*
       * Full-width and 3-up below `sm:`, an intrinsic pill above it.
       *
       * ⚠️ Not cosmetic: « Aujourd'hui · Cette semaine · Ce mois » at `text-sm` with `px-3.5` measures ~355 px, and
       * a 320 px viewport leaves 288 px of content — so as an `inline-flex` the track overflowed the page at the
       * one width § 0 names. Sharing the row three ways and shortening the visible words fixes it without hiding
       * anything: the accessible name stays the full French below.
       */
      className="flex w-full gap-0.5 rounded-full border bg-card p-0.5 shadow-sm sm:inline-flex sm:w-auto"
    >
      {PERIODS.map((period) => {
        const selected = period === value
        return (
          <button
            key={period}
            type="button"
            aria-pressed={selected}
            // The full label is the accessible name at every width, so the short form is a *visual* abbreviation
            // and « Jour » is never what a screen reader announces.
            aria-label={PERIOD_LABELS[period]}
            disabled={disabled}
            onClick={() => onChange(period)}
            className={cn(
              // `coarse:min-h-11` — these are plain `<button>`s rather than `ui/button.tsx`, so they inherit
              // neither `touch-target` nor any floor: `py-1.5` on a 20px line box is a 32px target, sitting
              // 2px from its neighbours. This is the dashboard's only filter — the control that rescopes every
              // figure on the page — so a mis-tap silently reads the wrong period. Growing the row is right
              // here rather than an overlay, for the same reason as a menu item: they are adjacent.
              "flex-1 rounded-full px-3.5 py-1.5 text-sm transition-colors duration-150 ease-snap coarse:min-h-11 coarse:px-4 sm:flex-none",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1 focus-visible:ring-offset-card",
              "disabled:cursor-not-allowed disabled:opacity-60 motion-reduce:transition-none",
              selected
                ? "bg-primary font-semibold text-primary-foreground"
                : "text-muted-foreground hover-hover:hover:text-foreground",
            )}
          >
            <span aria-hidden="true" className="sm:hidden">
              {PERIOD_LABELS_SHORT[period]}
            </span>
            <span aria-hidden="true" className="hidden sm:inline">
              {PERIOD_LABELS[period]}
            </span>
          </button>
        )
      })}
    </div>
  )
}
