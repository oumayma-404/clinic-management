"use client"

import { useState } from "react"
import { ChevronDown, Loader2 } from "lucide-react"
import { toast } from "sonner"

import { Button } from "@/components/ui/button"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { appointmentStatusLabel } from "@/components/appointment-labels"
import { appointmentsApi } from "@/lib/api/appointments"
import { showErrorToast } from "@/lib/errors"
import { cn } from "@/lib/utils"
import type { AppointmentDto } from "@/lib/api/types"
import { quoteFr } from "@/lib/format"

/**
 * « Arrivé », « En cours », « Terminé », « Absent » — advancing a visit's statut **without opening the edit
 * dialog** (AC-24).
 *
 * <p><b>The options are the server's, never re-derived.</b> `AppointmentDto.allowedNextStatuses` comes from
 * `Appointment.AllowedTransitions`, the single authority, and it is already served at all four read sites. That
 * matters more than it looks: the legal set is not intuitive — `Completed → { Cancelled }` **alone**, because a
 * visit is auto-completed by saving its fiche and the only honest exit is voiding it — so a client-side guess
 * would offer transitions the server then refuses, which is exactly the dead end the DTO field exists to avoid.
 * An empty list renders nothing at all rather than a disabled control that explains nothing.</p>
 *
 * <p>⚠️ <b>The options carry the touch floor, not the trigger.</b> Each is a full-width row at
 * `coarse:min-h-11`; the trigger adapts to whatever surface hosts it (on the agenda that is a block as short as
 * 24 px, where a 44 px child is not representable at all). The thing a finger must not miss is the *choice*,
 * since picking « Absent » when you meant « Arrivé » is a wrong-action bug on a patient's record.</p>
 *
 * <p>⚠️ <b>Never hover-revealed</b> (§ 9.2): the trigger is always painted. An affordance reachable only by hover
 * is unreachable on the device this product is used on most.</p>
 */
export function AppointmentQuickStatus({
  appointment,
  onChanged,
  triggerClassName,
  compact = false,
}: {
  appointment: AppointmentDto
  onChanged?: () => void
  triggerClassName?: string
  /** A tight host (an agenda block): paint a bare chevron instead of the status word. */
  compact?: boolean
}) {
  const [open, setOpen] = useState(false)
  const [saving, setSaving] = useState(false)

  const options = appointment.allowedNextStatuses ?? []
  if (options.length === 0) return null

  const apply = async (status: string) => {
    if (saving) return
    setSaving(true)
    try {
      // Only `status` travels. Every other field is tri-state server-side, so omitting them leaves the acts, the
      // praticien and the notes alone — sending a fuller payload from here is how a quick action silently
      // rewrites a séance.
      await appointmentsApi.update(appointment.id, { status })
      toast.success(`Rendez-vous marqué ${quoteFr(appointmentStatusLabel(status))}`)
      setOpen(false)
      onChanged?.()
    } catch (err) {
      // The popover stays open on failure, so the user can retry without finding the appointment again.
      showErrorToast(err)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <button
          type="button"
          aria-label={`Changer le statut du rendez-vous de ${appointment.patientName ?? "ce patient"}`}
          className={cn(
            "inline-flex shrink-0 items-center justify-center gap-1 rounded transition-colors",
            compact
              ? "size-5 hover:bg-black/10"
              : "h-8 px-2 text-xs hover:bg-muted coarse:h-11 coarse:px-3 coarse:text-sm",
            triggerClassName,
          )}
          // The host block is itself clickable (it opens the edit dialog) — without this the popover would open
          // and the dialog would open on top of it.
          onClick={(e) => e.stopPropagation()}
        >
          {!compact && <span>Statut</span>}
          <ChevronDown className={compact ? "size-3.5" : "size-3.5"} aria-hidden="true" />
        </button>
      </PopoverTrigger>
      {/* Never a fixed `w-80`: at 320 px that is the whole viewport with no gutter (§ 10). */}
      <PopoverContent
        align="end"
        className="w-[min(14rem,calc(100vw-2rem))] p-1"
        onClick={(e) => e.stopPropagation()}
      >
        <p className="px-2 py-1.5 text-2xs font-medium uppercase tracking-wide text-muted-foreground">
          Marquer comme
        </p>
        {options.map((status) => (
          <Button
            key={status}
            variant="ghost"
            className="w-full justify-start coarse:min-h-11"
            disabled={saving}
            onClick={() => void apply(status)}
          >
            {saving && <Loader2 className="size-4 animate-spin" aria-hidden="true" />}
            {appointmentStatusLabel(status)}
          </Button>
        ))}
      </PopoverContent>
    </Popover>
  )
}
