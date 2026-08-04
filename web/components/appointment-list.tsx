"use client"

import { useMemo } from "react"
import Link from "next/link"
import { format } from "date-fns"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Avatar, AvatarFallback } from "@/components/ui/avatar"
import { EmptyState } from "@/components/ui/empty-state"
import { AlertTriangle, CalendarDays, Clock, Loader2 } from "lucide-react"
import { cn } from "@/lib/utils"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { useAppointments } from "@/lib/hooks/use-appointments"
import {
  appointmentActsSummary, appointmentStatusBadgeClass, appointmentStatusLabel,
} from "@/components/appointment-labels"

function getInitials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  const initials = (parts[0]?.[0] ?? "") + (parts[1]?.[0] ?? "")
  return initials.toUpperCase() || "?"
}

export function AppointmentList() {
  // Stable "today" so the appointments hook doesn't refetch every render.
  const today = useMemo(() => new Date(), [])
  const { appointments, loading, error, refetch } = useAppointments(today, today)

  // Cancelled / no-show appointments aren't "today's work" — exclude them (matches the KPI counts).
  const visibleAppointments = useMemo(
    () => appointments.filter((a) => a.status !== "Cancelled" && a.status !== "NoShow"),
    [appointments],
  )

  return (
    <Card>
      <CardHeader>
        {/*
          The icon moves into a tinted chip.

          A `h-5 w-5` glyph in the same ink as the text beside it is just more text — and 43 card headers across
          the app were drawn that way, which is most of why the product read as grey. The chip is the idiom
          `/documents` already uses for its template tiles, and it is the cheapest way to give a card an anchor
          the eye can find without colouring the heading itself.
        */}
        <CardTitle className="flex items-center gap-2.5">
          <span
            aria-hidden="true"
            className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary"
          >
            <Clock className="size-4" strokeWidth={1.75} />
          </span>
          Rendez-vous du jour
        </CardTitle>
      </CardHeader>
      <CardContent>
        {loading ? (
          <div className="flex items-center justify-center py-10 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
          </div>
        ) : error ? (
          /* A failed read gets a retry, not a red sentence — the dashboard's other four sections already do
             this, and a lone error line on the day's schedule leaves the user with a browser reload as their
             only recourse. */
          <div
            role="status"
            className="flex flex-wrap items-center gap-3 rounded-lg border border-destructive/40 bg-destructive-wash p-3 text-sm"
          >
            <AlertTriangle className="size-4 shrink-0 text-destructive" aria-hidden="true" />
            <span className="min-w-0 flex-1">{error}</span>
            <Button size="sm" variant="outline" onClick={refetch}>
              Réessayer
            </Button>
          </div>
        ) : visibleAppointments.length === 0 ? (
          /* A quiet morning is the first thing a dentist sees on this screen, and « Aucun rendez-vous
             aujourd'hui » on its own offers nothing to do with it. */
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
          /*
           * One list with hairline separators, not a stack of bordered cards.
           *
           * The day is read as a **sequence**, so the rows should look like a sequence: the time leads in a
           * monospace tabular column so the hours line up down the page, and the separators are single hairlines
           * rather than a border per row. Nested cards inside a card — a bordered row inside a bordered surface —
           * was a good part of what made the page feel boxy, and it also drew the eye to the frames instead of the
           * schedule inside them.
           */
          <ul className="-mx-6 -mb-6 divide-y border-t">
            {visibleAppointments.map((appointment) => (
              /*
                The row is a link now.

                « Rendez-vous du jour » is the most-read list on the phone home screen and nothing in it
                opened — a dentist tapping a patient's name got no response at all. The deep link already
                existed and is already handled by the agenda (`?appointmentId=`); the row was one `Link` away
                from working. `min-h-11` because a 44px floor on a list row is what a finger needs, and this
                one is now the primary way into a visit.
              */
              <li key={appointment.id}>
                <Link
                  href={`/appointments?appointmentId=${appointment.id}`}
                  className="flex min-h-11 items-center gap-3 px-6 py-3 transition-colors hover-hover:hover:bg-accent/40 focus-visible:bg-accent/40 focus-visible:outline-none"
                >
                {/* Leads the row, and tabular so 09:00 and 14:15 align on the colon. */}
                <span className="w-11 shrink-0 font-mono text-sm tabular-nums text-muted-foreground">
                  {format(new Date(appointment.appointmentDateTime), "HH:mm")}
                </span>

                <Avatar className="size-8 shrink-0">
                  <AvatarFallback className="bg-accent text-xs font-semibold text-accent-foreground">
                    {getInitials(appointment.patientName)}
                  </AvatarFallback>
                </Avatar>

                <div className="min-w-0 flex-1">
                  {/* Wraps, never truncates. At 390px the name column is ~128px — about 17 characters — and a
                      row that is not expandable has nowhere for the clipped half of « Mohamed Ali Ben
                      Romdhane » to be read. Same rule `ui/card-list.tsx` states for card headings: a truncated
                      name is not a weaker label, it is a different person. */}
                  <p className="text-sm font-medium text-foreground [overflow-wrap:anywhere]">
                    {appointment.patientName}
                  </p>
                  {/* Acts as chips rather than a grey sentence: a séance is a set, and « Détartrage + Obturation »
                      as one run of muted text reads as a single act with a long name. */}
                  {(appointment.procedures ?? []).length > 0 ? (
                    <div className="mt-1 flex flex-wrap gap-1">
                      {(appointment.procedures ?? []).map((proc) => (
                        <span
                          key={proc.id}
                          className="rounded bg-muted px-1.5 py-px text-2xs text-muted-foreground"
                        >
                          {proc.name}
                        </span>
                      ))}
                    </div>
                  ) : (
                    <p className="mt-0.5 truncate text-xs text-muted-foreground">
                      {appointmentActsSummary(appointment) ?? "Rendez-vous"}
                    </p>
                  )}
                </div>

                <Badge
                  variant="secondary"
                  className={cn("shrink-0 rounded-full", appointmentStatusBadgeClass(appointment.status))}
                >
                  {appointmentStatusLabel(appointment.status)}
                </Badge>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}
