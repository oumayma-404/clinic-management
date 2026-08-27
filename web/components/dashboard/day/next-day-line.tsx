"use client"

import Link from "next/link"
import { AlertTriangle, ChevronRight } from "lucide-react"

import { Button } from "@/components/ui/button"
import { formatClock, formatDuration, type DayPreview } from "@/lib/dashboard/day-summary"
import { toLocalIso } from "@/lib/format"
import { cn } from "@/lib/utils"

/**
 * Zone 6 of the day board — the clinic's next **open** day, in one row.
 *
 * <p><b>Not « demain », and that distinction is the whole reason this earns its place.</b> A practice closed on
 * Sunday would read « Demain — fermé » every Saturday evening, i.e. useless at exactly the moment somebody is
 * planning; {@link resolveNextOpenDay} answers « ma prochaine journée ouvrée » instead, which on a Friday is
 * Monday. The label says which day it landed on rather than assuming.</p>
 *
 * <p>⚠️ <b>It links with `?date=`, not `?from=`.</b> The window params drop the reader into the month grid —
 * honest for « la période que cette carte a comptée », wrong for a quick-access line, since finding tomorrow in a
 * month of cells is the work this row exists to remove.</p>
 *
 * <p>⚠️ <b>A failed read gets a retry, never absence.</b> An absent row reads as « rien de prévu », which is a
 * confident wrong answer about a day that may be full — the same rule the appointment list follows.</p>
 *
 * <p>⚠️ <b>Deliberately period-independent</b>, like every zone above the « Statistiques » rule. It answers a
 * fixed question — the next working day — so the period selector below never appears to govern it.</p>
 */
export function NextDayLine({
  preview,
  loading,
  error,
  onRetry,
}: {
  /** `null` when the clinic opens on none of the next seven days — then this renders nothing at all. */
  preview: DayPreview | null
  loading: boolean
  error: string | null
  onRetry: () => void
}) {
  if (error) {
    return (
      <div
        role="status"
        className="flex flex-wrap items-center gap-3 rounded-lg border border-destructive/40 bg-destructive-wash p-3 text-sm"
      >
        <AlertTriangle className="size-4 shrink-0 text-destructive" aria-hidden="true" />
        <span className="min-w-0 flex-1">{error}</span>
        <Button size="sm" variant="outline" onClick={onRetry}>
          Réessayer
        </Button>
      </div>
    )
  }

  if (loading) {
    return (
      <div className="flex min-h-11 items-center gap-3" aria-label="Chargement de la prochaine journée">
        <span className="h-4 w-20 shrink-0 animate-pulse rounded bg-muted" />
        <span className="h-4 w-40 animate-pulse rounded bg-muted" />
      </div>
    )
  }

  if (!preview) return null

  const facts = buildFacts(preview)

  return (
    <Link
      href={`/appointments?date=${toLocalIso(preview.day)}`}
      aria-label={accessibleLabel(preview)}
      className={cn(
        "flex min-h-11 items-center gap-3 rounded-lg px-2 py-2 transition-colors",
        "focus-visible:bg-accent/40 focus-visible:outline-none hover-hover:hover:bg-accent/40",
      )}
    >
      {/* Facts wrap rather than truncate: at 320 px the gutters leave ~230 px, and a clipped « dès 08:30 » is
          worse than a second line. */}
      <span className="flex min-w-0 flex-1 flex-wrap items-baseline gap-x-2 gap-y-0.5">
        <span className="text-sm font-semibold text-foreground">{preview.label}</span>
        <span aria-hidden="true" className="flex flex-wrap items-baseline gap-x-2 text-xs text-muted-foreground">
          {facts.map((fact, i) => (
            <span key={fact} className="whitespace-nowrap">
              {i > 0 && <span className="pe-2 text-muted-foreground/50">·</span>}
              {fact}
            </span>
          ))}
        </span>
      </span>
      <ChevronRight aria-hidden="true" className="size-4 shrink-0 text-muted-foreground" />
    </Link>
  )
}

/** « 8 RDV · dès 08:30 · 5 h 20 ». A day with nothing booked says so — never « 0 RDV ». */
function buildFacts(preview: DayPreview): string[] {
  if (preview.count === 0) return ["aucun rendez-vous"]

  const facts = [`${preview.count} RDV`]
  if (preview.firstStartMinutes !== null) facts.push(`dès ${formatClock(preview.firstStartMinutes)}`)
  if (preview.bookedMinutes > 0) facts.push(formatDuration(preview.bookedMinutes))
  return facts
}

/** Spelled out, because « 8 RDV » is read aloud as eight letters and « dès » is a fragment on its own. */
function accessibleLabel(preview: DayPreview): string {
  if (preview.count === 0) return `${preview.label} : aucun rendez-vous. Ouvrir l'agenda.`

  const visits = `${preview.count} rendez-vous`
  const start =
    preview.firstStartMinutes !== null ? `, à partir de ${formatClock(preview.firstStartMinutes)}` : ""
  return `${preview.label} : ${visits}${start}. Ouvrir l'agenda.`
}
