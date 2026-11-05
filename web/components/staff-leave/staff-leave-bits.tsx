"use client"

import type React from "react"
import { STATUS_TONE_CLASS } from "@/components/ui/status-tone"
import type { StaffLeaveRequestDto, StaffLeaveStatus } from "@/lib/api/team"
import { cn } from "@/lib/utils"
import { STAFF_LEAVE_STATUS_TONE, staffLeaveDateTile } from "@/lib/staff-leave"

/** A congé page's section heading — the same quiet eyebrow on « Mes congés », « Utilisateurs → Congés » and a file. */
export function StaffLeaveSectionTitle({ id, children }: { id: string; children: React.ReactNode }) {
  return (
    <h2 id={id} className="text-sm font-semibold text-muted-foreground">
      {children}
    </h2>
  )
}

/** The request's status as a pill with a dot — the same shape on every congé surface. */
export function StaffLeaveStatusPill({
  status,
  label,
  className,
}: {
  status: StaffLeaveStatus
  label: string
  className?: string
}) {
  return (
    <span
      className={cn(
        "inline-flex h-6 shrink-0 items-center gap-1.5 rounded-full px-2.5 text-xs font-semibold whitespace-nowrap",
        STATUS_TONE_CLASS[STAFF_LEAVE_STATUS_TONE[status]],
        className,
      )}
    >
      <span aria-hidden="true" className="size-1.5 rounded-full bg-current opacity-70" />
      {label}
    </span>
  )
}

/** « NOV. » over « 16 » — the anchor a list of congés is scanned by. */
export function StaffLeaveDateTile({
  leave,
  muted = false,
  className,
}: {
  leave: Pick<StaffLeaveRequestDto, "startDate" | "endDate">
  muted?: boolean
  className?: string
}) {
  const { month, day } = staffLeaveDateTile(leave)
  return (
    <span
      aria-hidden="true"
      className={cn(
        "flex size-12 shrink-0 flex-col items-center justify-center rounded-lg border",
        muted ? "bg-muted/50 text-muted-foreground" : "bg-background",
        className,
      )}
    >
      <span className="text-2xs font-semibold tracking-wider text-muted-foreground">{month}</span>
      <span className="text-lg leading-none font-semibold tabular-nums">{day}</span>
    </span>
  )
}

/** Who decided, or who entered it: « Accepté par … », « Refusé par … », « Saisi par l'administrateur ». */
export function staffLeaveDecisionLine(request: StaffLeaveRequestDto): string | null {
  if (request.enteredByAdmin) return "Saisi par l'administrateur"
  if (!request.decidedByName) return null
  if (request.status === "Approved") return `Accepté par ${request.decidedByName}`
  if (request.status === "Rejected") return `Refusé par ${request.decidedByName}`
  return null
}
