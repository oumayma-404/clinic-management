"use client"

import Link from "next/link"
import { format, parseISO } from "date-fns"
import { CalendarClock, User } from "lucide-react"
import type { StaffMessageAppointmentLink, StaffMessagePatientLink } from "@/lib/api/staff-chat"
import { formatChatTime } from "@/lib/staff-chat/format"
import { cn } from "@/lib/utils"

const CHIP = "inline-flex max-w-full items-center gap-1 rounded-full border px-2 py-0.5 text-xs coarse:min-h-9"
const LINK = "border-border bg-card text-foreground underline-offset-2 hover:underline focus-visible:underline"

/**
 * A message's patient and appointment as they are now (FR-3, EC-8). Real links — so a page guarding unsaved work
 * can intercept them — except a removed record, which reads « introuvable » and leads nowhere.
 */
export function PatientLinkChips({
  patient,
  appointment,
  className,
}: {
  patient: StaffMessagePatientLink | null
  appointment: StaffMessageAppointmentLink | null
  className?: string
}) {
  if (!patient && !appointment) return null

  return (
    <span className={cn("flex flex-wrap gap-1", className)}>
      {patient &&
        (patient.missing || !patient.name ? (
          <span className={cn(CHIP, "border-dashed border-border text-muted-foreground")}>
            <User className="h-3 w-3 shrink-0" aria-hidden="true" />
            Patient introuvable
          </span>
        ) : (
          <Link href={`/patients/${patient.id}`} className={cn(CHIP, LINK)}>
            <User className="h-3 w-3 shrink-0" aria-hidden="true" />
            <span className="min-w-0 truncate">{patient.name}</span>
            {patient.archived && <span className="shrink-0 text-muted-foreground">· Patient archivé</span>}
          </Link>
        ))}
      {appointment &&
        (appointment.missing || !appointment.startsAt ? (
          <span className={cn(CHIP, "border-dashed border-border text-muted-foreground")}>
            <CalendarClock className="h-3 w-3 shrink-0" aria-hidden="true" />
            RDV introuvable
          </span>
        ) : appointment.cancelled ? (
          // A cancelled visit opens the agenda on its day rather than a dialog for a visit that no longer stands.
          <Link
            href={`/appointments?date=${format(parseISO(appointment.startsAt), "yyyy-MM-dd")}`}
            className={cn(CHIP, LINK, "text-muted-foreground")}
          >
            <CalendarClock className="h-3 w-3 shrink-0" aria-hidden="true" />
            <span className="tabular-nums">{formatChatTime(appointment.startsAt)}</span>· RDV annulé
          </Link>
        ) : (
          <Link
            href={`/appointments?appointmentId=${appointment.id}`}
            className={cn(CHIP, LINK)}
            onClick={(event) => {
              // Already on the agenda, a same-route link does not remount it: hand the id to its deep-link listener.
              if (window.location.pathname !== "/appointments" || event.metaKey || event.ctrlKey) return
              event.preventDefault()
              window.dispatchEvent(new CustomEvent("clinic:deeplink", { detail: { appointmentId: appointment.id } }))
            }}
          >
            <CalendarClock className="h-3 w-3 shrink-0" aria-hidden="true" />
            <span className="tabular-nums">RDV {formatChatTime(appointment.startsAt)}</span>
            {appointment.dentistName && <span className="min-w-0 truncate">· {appointment.dentistName}</span>}
          </Link>
        ))}
    </span>
  )
}
