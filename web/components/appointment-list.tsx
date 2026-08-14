"use client"

import Link from "next/link"
import { Card, CardContent } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { EmptyState } from "@/components/ui/empty-state"
import { AlertTriangle, CalendarDays } from "lucide-react"
import { cn } from "@/lib/utils"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { actSolidStyle } from "@/lib/dashboard/act-colour"
import { formatClock, formatDuration, type DaySlot } from "@/lib/dashboard/day-summary"
import { appointmentStatusBadgeClass, appointmentStatusLabel } from "@/components/appointment-labels"

interface AppointmentListProps {
  /** Today's occupying visits, already filtered and ordered by `buildDaySummary`. */
  slots: DaySlot[]
  loading: boolean
  error: string | null
  onRetry: () => void
}

/**
 * Zone 5 of the day board — the detail behind the ribbon.
 *
 * <p>Presentational since the day board landed: the page fetches today's appointments once for the whole board
 * and hands the derived slots down, rather than this component running a second identical
 * `useAppointments(today, today)` beside the one the ribbon and the now/next cards already need.</p>
 *
 * <p>⚠️ <b>Below `sm:` the row restructures rather than shrinks.</b> Time and status share a header line, then the
 * name on its own full width, then the acts — so nothing competes for the ~230 px a 320 px phone leaves after
 * the gutters. Above the hinge the time takes its own column back and the rows align down the page.</p>
 */
export function AppointmentList({ slots, loading, error, onRetry }: AppointmentListProps) {
  return (
    <Card>
      <CardContent>
        {loading ? (
          <ul className="space-y-3" aria-label="Chargement des rendez-vous">
            {[0, 1, 2, 3].map((i) => (
              <li key={i} className="flex items-center gap-3">
                <span className="h-10 w-1 shrink-0 animate-pulse rounded-full bg-muted" />
                <span className="h-4 w-12 shrink-0 animate-pulse rounded bg-muted" />
                <span className="h-4 flex-1 animate-pulse rounded bg-muted" />
              </li>
            ))}
          </ul>
        ) : error ? (
          /* A failed read gets a retry, never « aucun rendez-vous » — on this screen those two are the same
             blank rectangle and opposite facts. */
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
        ) : slots.length === 0 ? (
          <EmptyState
            icon={CalendarDays}
            size="compact"
            title="Aucun rendez-vous aujourd'hui"
            description="La journée est libre. Ouvrez l'agenda pour planifier une visite."
            chipClassName={zoneChipClass(ZONES.daily)}
            action={
              <Button asChild size="sm">
                <Link href="/appointments">Ouvrir l&apos;agenda</Link>
              </Button>
            }
          />
        ) : (
          <ul className="divide-y">
            {slots.map((slot) => (
              <li key={slot.appointment.id}>
                <Row slot={slot} />
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}

function Row({ slot }: { slot: DaySlot }) {
  const { appointment } = slot
  const duration = formatDuration(slot.endMinutes - slot.startMinutes)
  const time = formatClock(slot.startMinutes)
  const acts = appointment.procedures ?? []

  const statusBadge = (
    <Badge
      variant="secondary"
      className={cn("shrink-0 rounded-full", appointmentStatusBadgeClass(appointment.status))}
    >
      {appointmentStatusLabel(appointment.status)}
    </Badge>
  )

  return (
    <Link
      href={`/appointments?appointmentId=${appointment.id}`}
      className={cn(
        "grid min-h-11 grid-cols-[3px_1fr] items-stretch gap-x-3 gap-y-0 py-3 transition-colors",
        "sm:grid-cols-[3px_auto_1fr_auto] sm:items-center sm:gap-x-3.5",
        "focus-visible:bg-accent/40 focus-visible:outline-none hover-hover:hover:bg-accent/40",
        slot.isCurrent && "bg-accent",
        slot.isCurrent && "rounded-lg px-2",
      )}
    >
      {/* The act's colour at full strength: a 3px rail is the small mark a pastel would erase. */}
      <span aria-hidden="true" className="rounded-full" style={actSolidStyle(slot.colorHex)} />

      {/* Above `sm:`, the time has its own column so the hours align down the page. */}
      <span className="hidden shrink-0 text-start sm:block">
        <span className="block font-mono text-sm font-semibold tabular-nums text-foreground">{time}</span>
        <span className="mt-0.5 block font-mono text-2xs text-muted-foreground">{duration}</span>
      </span>

      <span className="min-w-0">
        {/* Below `sm:` the time and the status share a header line, freeing the full width for the name. */}
        <span className="flex flex-wrap items-baseline gap-x-2.5 gap-y-1 sm:hidden">
          <span className="font-mono text-sm font-semibold tabular-nums text-foreground">{time}</span>
          <span className="font-mono text-2xs text-muted-foreground">{duration}</span>
          <span className="ms-auto">{statusBadge}</span>
        </span>

        <span className="mt-1 block text-sm font-medium text-foreground [overflow-wrap:anywhere] sm:mt-0">
          {appointment.patientName}
        </span>

        {acts.length > 0 && (
          <span className="mt-1 flex flex-wrap gap-x-3 gap-y-1">
            {acts.map((proc) => (
              <span
                key={proc.id}
                className="flex items-center gap-1.5 whitespace-nowrap text-2xs text-muted-foreground"
              >
                <span
                  aria-hidden="true"
                  className="size-2 shrink-0 rounded-[2px]"
                  style={actSolidStyle(proc.colorHex)}
                />
                {proc.name}
              </span>
            ))}
          </span>
        )}
      </span>

      <span className="hidden sm:block">{statusBadge}</span>
    </Link>
  )
}
