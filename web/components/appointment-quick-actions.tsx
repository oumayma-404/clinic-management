"use client"

import { useState } from "react"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { Loader2, MoreHorizontal, Trash2 } from "lucide-react"
import { toast } from "sonner"

import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Button } from "@/components/ui/button"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { appointmentStatusLabel } from "@/components/appointment-labels"
import { appointmentsApi } from "@/lib/api/appointments"
import { showErrorToast } from "@/lib/errors"
import { cn } from "@/lib/utils"
import type { AppointmentDto } from "@/lib/api/types"
import { quoteFr } from "@/lib/format"

/**
 * The agenda block's own actions — advance the visit's statut, or delete a séance booked by mistake, **without
 * opening the edit dialog** (AC-24).
 *
 * <p><b>The statut options are the server's, never re-derived.</b> `AppointmentDto.allowedNextStatuses` comes
 * from `Appointment.AllowedTransitions`, the single authority, and it is already served at all four read sites.
 * That matters more than it looks: the legal set is not intuitive — `Completed → { Cancelled }` **alone**,
 * because a visit is auto-completed by saving its fiche and the only honest exit is voiding it — so a
 * client-side guess would offer transitions the server then refuses, which is exactly the dead end the DTO field
 * exists to avoid.</p>
 *
 * <p>⚠️ <b>It is a « ⋯ » and no longer a chevron, because it is no longer only the statut.</b> One trigger and
 * one menu rather than two controls: the host is a block sized by DURATION — a 15-minute visit is 12 px at
 * `HOUR_HEIGHT` — and a second affordance there would crowd the patient's name off the only line it has.</p>
 *
 * <p>⚠️ <b>The options carry the touch floor, not the trigger.</b> Each is a full-width row at
 * `coarse:min-h-11`; the trigger adapts to whatever surface hosts it (on the agenda a 44 px child is simply not
 * representable). The thing a finger must not miss is the *choice*, since picking « Absent » when you meant
 * « Arrivé » is a wrong-action bug on a patient's record — and « Supprimer » is behind a confirmation besides.</p>
 *
 * <p>⚠️ <b>Never hover-revealed</b> (§ 9.2): the trigger is always painted. An affordance reachable only by hover
 * is unreachable on the device this product is used on most.</p>
 */
export function AppointmentQuickActions({
  appointment,
  onChanged,
  triggerClassName,
  compact = false,
}: {
  appointment: AppointmentDto
  onChanged?: () => void
  triggerClassName?: string
  /** A tight host (an agenda block): paint bare dots instead of the word. */
  compact?: boolean
}) {
  const [open, setOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

  const options = appointment.allowedNextStatuses ?? []

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

  /**
   * « Supprimer » from the agenda — the same mark the edit dialog writes, so a séance booked by mistake never has
   * to be annulée (which counts in the taux d'absence). The server refuses one carrying a fiche or a note
   * d'honoraires with `visit_has_work`; that sentence reaches the toast verbatim.
   */
  const remove = async () => {
    setSaving(true)
    try {
      await appointmentsApi.disregardVisit(appointment.id)
      setConfirmDelete(false)
      toast.success("Rendez-vous supprimé", {
        description:
          `Il ne compte pas comme une annulation. Vous pouvez le récupérer dans ${quoteFr("À clôturer")}.`,
      })
      onChanged?.()
    } catch (err) {
      setConfirmDelete(false)
      showErrorToast(err, "Échec de la suppression du rendez-vous")
    } finally {
      setSaving(false)
    }
  }

  /*
   * What the confirmation names (§ 13). Branches on `patientId`, not on the name: `patientName` is the server's
   * « Occupé » for a blocked slot, so the naive template reads « Le rendez-vous de Occupé ».
   */
  const target =
    `${appointment.patientId ? `Le rendez-vous de ${appointment.patientName}` : "Le créneau occupé"} du `
    + format(parseISO(appointment.appointmentDateTime), "d MMMM à HH:mm", { locale: fr })

  return (
    <>
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <button
            type="button"
            aria-label={`Actions du rendez-vous de ${appointment.patientName ?? "ce patient"}`}
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
            {!compact && <span>Actions</span>}
            <MoreHorizontal className="size-3.5" aria-hidden="true" />
          </button>
        </PopoverTrigger>
        {/* Never a fixed `w-80`: at 320 px that is the whole viewport with no gutter (§ 10). */}
        <PopoverContent
          align="end"
          className="w-[min(15rem,calc(100vw-2rem))] p-1"
          onClick={(e) => e.stopPropagation()}
        >
          {/* An empty transition set renders no heading either — a lone « Marquer comme » over nothing reads as a
              failed load, where the truth is that this visit has nowhere left to go. */}
          {options.length > 0 && (
            <>
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
              <div role="separator" className="my-1 h-px bg-border" />
            </>
          )}
          <Button
            variant="ghost"
            className="w-full justify-start text-destructive hover:bg-destructive/10 hover:text-destructive coarse:min-h-11"
            disabled={saving}
            onClick={() => {
              // Close the popover FIRST: a Radix AlertDialog opened from inside an open Popover leaves two focus
              // traps fighting, and the confirmation is rendered as this component's sibling for the same reason.
              setOpen(false)
              setConfirmDelete(true)
            }}
          >
            <Trash2 className="size-4" aria-hidden="true" />
            Supprimer
          </Button>
        </PopoverContent>
      </Popover>

      <AlertDialog open={confirmDelete} onOpenChange={setConfirmDelete}>
        {/*
          ⚠️ `stopPropagation`, exactly as the popover above needs it, and for a reason that is easy to get wrong:
          Radix portals this content to `document.body`, but a React portal still bubbles through the **React**
          tree — so a click on « Oui, supprimer » reached the agenda block's own `onClick` and opened the edit
          dialog for the séance that had just been deleted, on top of the success toast.
        */}
        <AlertDialogContent onClick={(e) => e.stopPropagation()}>
          <AlertDialogHeader>
            <AlertDialogTitle>Supprimer ce rendez-vous ?</AlertDialogTitle>
            <AlertDialogDescription>
              {target} quittera l&apos;agenda
              {appointment.patientId ? " et le dossier du patient" : ""}, et ne comptera pas comme une annulation
              dans le taux d&apos;absence. Vous pourrez le récupérer dans {quoteFr("À clôturer")} › séances
              retirées.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={saving}>Non, conserver</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                // Radix closes the dialog on action-click by default; the async call must own the close so a
                // refusal (a billed séance) does not read as a completed deletion.
                e.preventDefault()
                void remove()
              }}
              disabled={saving}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {saving ? "Suppression…" : "Oui, supprimer"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
