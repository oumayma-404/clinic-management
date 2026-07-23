"use client"

import { useMemo } from "react"
import { format } from "date-fns"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Avatar, AvatarFallback } from "@/components/ui/avatar"
import { Clock, Loader2 } from "lucide-react"
import { useAppointments } from "@/lib/hooks/use-appointments"

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
          <div className="space-y-4">
            {visibleAppointments.map((appointment) => (
              <div
                key={appointment.id}
                className="flex items-center justify-between rounded-lg border border-border bg-card p-4 transition-colors hover:bg-accent/50"
              >
                <div className="flex items-center gap-4">
                  <Avatar className="h-10 w-10">
                    <AvatarFallback className="bg-accent text-accent-foreground">
                      {getInitials(appointment.patientName)}
                    </AvatarFallback>
                  </Avatar>
                  <div>
                    <p className="font-medium text-foreground">{appointment.patientName}</p>
                    <p className="text-sm text-muted-foreground">
                      {appointment.procedureTypeName ?? "Rendez-vous"}
                    </p>
                  </div>
                </div>
                <div className="flex items-center gap-4">
                  <span className="text-sm font-medium text-foreground">
                    {format(new Date(appointment.appointmentDateTime), "HH:mm")}
                  </span>
                  <Badge
                    variant={appointment.status === "Confirmed" ? "default" : "secondary"}
                    className="capitalize"
                  >
                    {appointment.status}
                  </Badge>
                </div>
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}
