"use client"

import Link from "next/link"
import { actSolidStyle } from "@/lib/dashboard/act-colour"
import {
  formatClock,
  formatDuration,
  type DaySlot,
  type DaySummary,
} from "@/lib/dashboard/day-summary"
import { appointmentActsSummary } from "@/components/appointment-labels"
import { cn } from "@/lib/utils"

interface NowNextCardsProps {
  summary: DaySummary
  /** Minutes from local midnight, so « dans 1 h 50 » is computed against the same instant the summary was. */
  nowMinutes: number
}

/**
 * Zone 2 of the day board — who is in the chair, and who is next.
 *
 * <p>The single most actionable thing on the screen, and the reason the day zones exist at all. Both cards are
 * light surfaces with a 4 px top edge in the act's own colour; the live one additionally takes the accent wash.
 * Nothing here is a dark panel: a filled surface is designed to carry <i>one</i> number, and these carry a name,
 * an act, a time and a delay.</p>
 *
 * <p>⚠️ <b>The internals are stacked at every width, deliberately.</b> A name-then-time row is what clipped
 * « Yassine Chaabane » at 320 px, and a patient's name is an identity — it wraps
 * (`[overflow-wrap:anywhere]`), it never receives an ellipsis. The time moves to a foot row behind a hairline,
 * where it has the width to be large.</p>
 *
 * <p>⚠️ <b>Both cards show a delay, not only a clock time.</b> « dans 1 h 50 » is what a dentist reads; « 13:30 »
 * alone makes them do the subtraction. The clock stays because it is what they will say out loud to the patient.</p>
 */
export function NowNextCards({ summary, nowMinutes }: NowNextCardsProps) {
  const { current, next } = summary
  if (!current && !next) return null

  return (
    <div className="grid gap-3 sm:grid-cols-2 sm:gap-4">
      {current && <SlotCard slot={current} nowMinutes={nowMinutes} live />}
      {next && <SlotCard slot={next} nowMinutes={nowMinutes} />}
    </div>
  )
}

function SlotCard({ slot, nowMinutes, live = false }: { slot: DaySlot; nowMinutes: number; live?: boolean }) {
  const { appointment } = slot
  const acts = appointmentActsSummary(appointment)
  const duration = slot.endMinutes - slot.startMinutes
  const delta = live ? nowMinutes - slot.startMinutes : slot.startMinutes - nowMinutes

  return (
    <Link
      href={`/appointments?appointmentId=${appointment.id}`}
      className={cn(
        "group relative block overflow-hidden rounded-xl border p-4 shadow-sm transition-shadow sm:p-5",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background",
        "hover-hover:hover:shadow-md",
        live ? "border-primary/25 bg-accent" : "border-border bg-card",
      )}
      aria-label={`${live ? "Au fauteuil" : "Prochain rendez-vous"} : ${appointment.patientName} à ${formatClock(slot.startMinutes)}. Ouvrir la fiche.`}
    >
      {/* The act's colour, at full strength — a 4px edge is exactly the small mark that a pastel would erase. */}
      <span aria-hidden="true" className="absolute inset-x-0 top-0 h-1" style={actSolidStyle(slot.colorHex)} />

      <p
        className={cn(
          "flex items-center gap-2 font-mono text-2xs font-medium uppercase tracking-[0.12em]",
          live ? "text-primary" : "text-muted-foreground",
        )}
      >
        {live && (
          <span
            aria-hidden="true"
            className="size-1.5 shrink-0 rounded-full bg-success ring-3 ring-success/25 motion-safe:animate-pulse"
          />
        )}
        {live ? "Au fauteuil" : "Ensuite"}
      </p>

      {/* Wraps, never truncates. At 320px this column is ~230px — about 30 characters — and a clipped name is
          not a weaker label, it is a different person. */}
      <p className="mt-2 text-lg font-semibold leading-snug tracking-tight text-foreground [overflow-wrap:anywhere]">
        {appointment.patientName}
      </p>

      <p className="mt-1.5 flex items-center gap-2 text-sm text-muted-foreground">
        <span
          aria-hidden="true"
          className="size-2.5 shrink-0 rounded-[3px]"
          style={actSolidStyle(slot.colorHex)}
        />
        <span className="min-w-0 [overflow-wrap:anywhere]">
          {acts ?? "Rendez-vous"} · {formatDuration(duration)}
        </span>
      </p>

      <p
        className={cn(
          "mt-3 flex items-baseline justify-between gap-3 border-t pt-3",
          live ? "border-primary/20" : "border-border",
        )}
      >
        <span className="font-mono text-xl font-semibold tabular-nums tracking-tight text-foreground">
          {formatClock(slot.startMinutes)}
        </span>
        <span className="text-end text-xs text-muted-foreground">{relativeLabel(delta, live)}</span>
      </p>
    </Link>
  )
}

/**
 * « dans 1 h 50 » / « depuis 25 min ».
 *
 * <p>A visit that should have started is « en retard de … » rather than « dans −5 min »: the clock does not run
 * backwards, and a negative delay is exactly the case a practice needs named.</p>
 */
function relativeLabel(deltaMinutes: number, live: boolean): string {
  const rounded = Math.round(deltaMinutes)
  if (live) {
    if (rounded < 1) return "commence maintenant"
    return `depuis ${formatDuration(rounded)}`
  }
  if (rounded <= 0) return `en retard de ${formatDuration(Math.abs(rounded))}`
  if (rounded < 1) return "dans un instant"
  return `dans ${formatDuration(rounded)}`
}
