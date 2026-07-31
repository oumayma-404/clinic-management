"use client"

import { useMemo } from "react"
import { format } from "date-fns"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Avatar, AvatarFallback } from "@/components/ui/avatar"
import { Clock, Loader2 } from "lucide-react"
import { cn } from "@/lib/utils"
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
  const { appointments, loading, error } = useAppointments(today, today)

  // Cancelled / no-show appointments aren't "today's work" — exclude them (matches the KPI counts).
  const visibleAppointments = useMemo(
    () => appointments.filter((a) => a.status !== "Cancelled" && a.status !== "NoShow"),
    [appointments],
  )

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Clock className="h-5 w-5" />
          Rendez-vous du jour
        </CardTitle>
      </CardHeader>
      <CardContent>
        {loading ? (
          <div className="flex items-center justify-center py-10 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
          </div>
        ) : error ? (
          <p className="py-10 text-center text-sm text-destructive">{error}</p>
        ) : visibleAppointments.length === 0 ? (
          <p className="py-10 text-center text-sm text-muted-foreground">Aucun rendez-vous aujourd'hui</p>
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
              <li
                key={appointment.id}
                className="flex items-center gap-3 px-6 py-3 transition-colors hover-hover:hover:bg-accent/40"
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
                  <p className="truncate text-sm font-medium text-foreground">{appointment.patientName}</p>
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
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}
