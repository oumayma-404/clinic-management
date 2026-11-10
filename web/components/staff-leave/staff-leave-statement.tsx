"use client"

import { format } from "date-fns"
import { fr } from "date-fns/locale"
import { ChevronLeft, ChevronRight, ReceiptText, X } from "lucide-react"

import { AppLoader } from "@/components/ui/app-loader"
import { Button } from "@/components/ui/button"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import type { StaffLeaveMovementDto } from "@/lib/api/team"
import { dayFromKey } from "@/lib/doctor-leaves"
import { quoteFr } from "@/lib/format"
import { isOverdrawn, leaveMovementLabel, staffDaysShort, staffDaysSigned } from "@/lib/staff-leave"
import { cn } from "@/lib/utils"

/** ‹ 2026 › — the relevé's year; `MonthStepper`'s look, one year per step. */
function YearStepper({ year, maxYear, onShift }: { year: number; maxYear?: number; onShift: (delta: -1 | 1) => void }) {
  return (
    <div className="flex items-center gap-1">
      {/* Grown, not overlaid: two buttons a few pixels apart would steal each other's taps. */}
      <Button
        variant="outline"
        size="icon"
        className="size-8 coarse:size-11"
        onClick={() => onShift(-1)}
        aria-label="Année précédente"
      >
        <ChevronLeft className="size-4" aria-hidden="true" />
      </Button>
      <p className="min-w-20 text-center text-sm font-semibold tabular-nums" aria-live="polite">
        <span className="sr-only">Relevé </span>
        {year}
      </p>
      <Button
        variant="outline"
        size="icon"
        className="size-8 coarse:size-11"
        onClick={() => onShift(1)}
        disabled={maxYear !== undefined && year >= maxYear}
        aria-label="Année suivante"
      >
        <ChevronRight className="size-4" aria-hidden="true" />
      </Button>
    </div>
  )
}

/** The informational « N j non pris en 2026 : perdus » opens the year, wherever the server placed it. */
function inReadingOrder(movements: StaffLeaveMovementDto[]): StaffLeaveMovementDto[] {
  return [...movements.filter((m) => m.kind === "LostLastYear"), ...movements.filter((m) => m.kind !== "LostLastYear")]
}

/**
 * The relevé of a year: every line that moved her balance, in order, with the balance after it — so the last line of
 * this year is the « jours restants » of the card above. A cancelled opening or adjustment stays where it stood,
 * struck through; a line that moved nothing (history before her account, or inside a « Solde de départ ») is greyed.
 * A list rather than a table: four short facts per line read the same at 320 px and at 1440 px. With `year` and
 * `onYearChange` it carries its own year stepper, and `loading` / `failed` describe that year's read.
 */
export function StaffLeaveStatement({
  movements,
  onCancel,
  year,
  maxYear,
  onYearChange,
  loading = false,
  failed = false,
  onRetry,
}: {
  movements: StaffLeaveMovementDto[]
  /** Admin only: offered on a live opening or adjustment line. */
  onCancel?: (movement: StaffLeaveMovementDto) => void
  /** The year shown; the stepper renders only with `onYearChange`. */
  year?: number
  /** The last year the stepper may reach (the page's own). */
  maxYear?: number
  onYearChange?: (year: number) => void
  loading?: boolean
  failed?: boolean
  onRetry?: () => void
}) {
  const stepper =
    year !== undefined && onYearChange ? (
      <YearStepper year={year} maxYear={maxYear} onShift={(delta) => onYearChange(year + delta)} />
    ) : null

  const body = () => {
    if (failed) {
      return (
        <LoadFailureNotice
          variant="inline"
          message={year !== undefined ? `Le relevé ${year} n'a pas pu être chargé.` : "Le relevé n'a pas pu être chargé."}
          onRetry={onRetry}
        />
      )
    }
    if (loading) return <AppLoader label={year !== undefined ? `Chargement du relevé ${year}…` : undefined} />
    if (movements.length === 0) {
      return (
        <EmptyState
          size="compact"
          icon={ReceiptText}
          title={year !== undefined ? `Aucun mouvement en ${year}` : "Aucun mouvement"}
        />
      )
    }
    return (
      <ul className="divide-y rounded-xl border bg-card">
        {inReadingOrder(movements).map((m, index) => {
          const label = leaveMovementLabel(m)
          const day = format(dayFromKey(m.day), "d MMM yyyy", { locale: fr })
          const info = m.kind === "LostLastYear"
          const quiet = m.cancelled || m.absorbed || info
          const cancellable = Boolean(
            onCancel && m.entryId && !m.cancelled && (m.kind === "Opening" || m.kind === "Adjustment"),
          )
          const amount = m.kind === "Opening" || m.kind === "CarriedIn" ? staffDaysShort(m.days) : staffDaysSigned(m.days)
          const overdrawn = isOverdrawn(m.balance)
          return (
            <li key={`${m.day}-${m.kind}-${m.entryId ?? m.requestId ?? index}`} className="flex items-start gap-3 px-4 py-3">
              <div className="min-w-0 flex-1">
                <p
                  className={cn(
                    "text-sm [overflow-wrap:anywhere]",
                    quiet && "text-muted-foreground",
                    m.cancelled && "line-through",
                    info && "font-medium text-warning-ink",
                  )}
                >
                  {label}
                </p>
                <p className="text-xs text-muted-foreground">
                  {day}
                  {m.absorbed && !m.cancelled && " · sans effet sur le solde"}
                  {m.cancelled && ` · annulée${m.cancelReason ? ` : ${quoteFr(m.cancelReason)}` : ""}`}
                </p>
              </div>
              {!info && (
                <div className="shrink-0 text-end">
                  <p
                    className={cn(
                      "text-sm font-semibold tabular-nums",
                      quiet && "font-normal text-muted-foreground",
                      m.cancelled && "line-through",
                      !quiet && m.days < 0 && m.kind !== "Opening" && "text-destructive",
                      !quiet && m.days > 0 && (m.kind === "Credit" || m.kind === "Adjustment") && "text-success",
                    )}
                  >
                    {amount}
                  </p>
                  {!m.absorbed && (
                    <p className={cn("text-xs tabular-nums", overdrawn ? "text-destructive" : "text-muted-foreground")}>
                      {overdrawn ? `dépassement ${staffDaysShort(-m.balance)}` : `solde ${staffDaysShort(m.balance)}`}
                    </p>
                  )}
                </div>
              )}
              {onCancel &&
                (cancellable ? (
                  <Button
                    variant="ghost"
                    size="icon"
                    className="size-8 shrink-0 coarse:size-11"
                    onClick={() => onCancel(m)}
                    aria-label={`Annuler la ligne ${quoteFr(label)} du ${day}`}
                  >
                    <X className="size-4" aria-hidden="true" />
                  </Button>
                ) : (
                  <span aria-hidden="true" className="size-8 shrink-0 coarse:size-11" />
                ))}
            </li>
          )
        })}
      </ul>
    )
  }

  if (!stepper) return body()
  return (
    <div className="space-y-3">
      {stepper}
      {body()}
    </div>
  )
}
