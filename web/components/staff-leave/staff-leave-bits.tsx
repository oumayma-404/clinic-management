"use client"

import type React from "react"
import { Minus, Plus } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Input } from "@/components/ui/input"
import { STATUS_TONE_CLASS } from "@/components/ui/status-tone"
import type { StaffLeaveRequestDto, StaffLeaveStatus } from "@/lib/api/team"
import { quoteFr } from "@/lib/format"
import { cn } from "@/lib/utils"
import {
  MAX_STAFF_DAYS,
  STAFF_LEAVE_STATUS_TONE,
  formatStaffDays,
  parseStaffDays,
  staffLeaveDateTile,
  type StaffLeaveReader,
} from "@/lib/staff-leave"

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
    <Badge
      variant="secondary"
      className={cn("h-6 gap-1.5 px-2.5 font-semibold", STATUS_TONE_CLASS[STAFF_LEAVE_STATUS_TONE[status]], className)}
    >
      <span aria-hidden="true" className="size-1.5 rounded-full bg-current opacity-70" />
      {label}
    </Badge>
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

/**
 * Who decided, entered or cancelled it: « Accepté par … », « Refusé par … », « Saisi par l'administrateur »,
 * « Annulé par Dr X · « motif » », « Demande retirée par vous ». `reader` "self" is her own screen.
 */
export function staffLeaveDecisionLine(request: StaffLeaveRequestDto, reader: StaffLeaveReader = "other"): string | null {
  if (request.status === "Cancelled") {
    const name = request.cancelledByName ?? (request.cancelledBySelf ? request.userName : null)
    const by = request.cancelledBySelf && reader === "self" ? "vous" : name
    if (!by) return null
    const withdrawn = request.cancelledBySelf && request.decidedAt === null && !request.enteredByAdmin
    const line = `${withdrawn ? "Demande retirée" : "Annulé"} par ${by}`
    return request.cancelReason ? `${line} · ${quoteFr(request.cancelReason)}` : line
  }
  if (request.enteredByAdmin) return "Saisi par l'administrateur"
  if (!request.decidedByName) return null
  if (request.status === "Approved") return `Accepté par ${request.decidedByName}`
  if (request.status === "Rejected") return `Refusé par ${request.decidedByName}`
  return null
}

const STEPPER_BUTTON =
  "flex w-11 items-center justify-center bg-muted/50 text-foreground transition-colors hover:bg-muted focus-visible:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset"

/** « − [ 3 ] + » — a day count in half days, typed or stepped; `fallback` is where a step starts from an empty field. */
export function StaffDaysStepper({
  id,
  value,
  onChange,
  fallback,
  describedBy,
}: {
  id: string
  value: string
  onChange: (text: string) => void
  fallback: number
  describedBy?: string
}) {
  const step = (delta: number) => {
    const current = parseStaffDays(value) ?? fallback
    onChange(formatStaffDays(Math.min(MAX_STAFF_DAYS, Math.max(0, current + delta))))
  }
  return (
    <div className="inline-flex items-stretch overflow-hidden rounded-lg border">
      <button type="button" aria-label="Une demi-journée de moins" onClick={() => step(-0.5)} className={STEPPER_BUTTON}>
        <Minus className="size-4" aria-hidden="true" />
      </button>
      <Input
        id={id}
        inputMode="decimal"
        value={value}
        aria-describedby={describedBy}
        onChange={(e) => onChange(e.target.value)}
        className="h-11 w-20 rounded-none border-0 border-x text-center font-semibold tabular-nums shadow-none md:text-base"
      />
      <button type="button" aria-label="Une demi-journée de plus" onClick={() => step(0.5)} className={STEPPER_BUTTON}>
        <Plus className="size-4" aria-hidden="true" />
      </button>
    </div>
  )
}
