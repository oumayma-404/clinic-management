"use client"

import { ChevronLeft, ChevronRight } from "lucide-react"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"

/**
 * ‹ novembre 2026 › — the one month navigator of the team screens (Congés' month, the Présences sheet), so two
 * adjacent tabs step through months with the same control at the same size.
 */
export function MonthStepper({
  label,
  onShift,
  nextDisabled = false,
  previousDisabled = false,
  className,
}: {
  /** « novembre 2026 » — capitalised here. */
  label: string
  onShift: (delta: -1 | 1) => void
  nextDisabled?: boolean
  previousDisabled?: boolean
  className?: string
}) {
  return (
    <div className={cn("flex items-center gap-1", className)}>
      {/* Grown, not overlaid: two buttons a few pixels apart would steal each other's taps. */}
      <Button
        variant="outline"
        size="icon"
        className="size-8 coarse:size-11"
        onClick={() => onShift(-1)}
        disabled={previousDisabled}
        aria-label="Mois précédent"
      >
        <ChevronLeft className="size-4" aria-hidden="true" />
      </Button>
      <p className="min-w-36 text-center text-sm font-semibold capitalize" aria-live="polite">
        {label}
      </p>
      <Button
        variant="outline"
        size="icon"
        className="size-8 coarse:size-11"
        onClick={() => onShift(1)}
        disabled={nextDisabled}
        aria-label="Mois suivant"
      >
        <ChevronRight className="size-4" aria-hidden="true" />
      </Button>
    </div>
  )
}
