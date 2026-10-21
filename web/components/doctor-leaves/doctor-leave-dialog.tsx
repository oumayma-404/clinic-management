"use client"

import type React from "react"

import { useEffect, useMemo, useRef, useState } from "react"
import { toast } from "sonner"
import { Trash2 } from "lucide-react"
import { Button } from "@/components/ui/button"
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
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { doctorsApi, type DoctorLeaveDto, type DoctorLeaveSaveResultDto } from "@/lib/api/doctors"
import { canEditAnyLeave } from "@/lib/auth/can"
import { useSession } from "@/lib/auth/session"
import { showErrorToast } from "@/lib/errors"
import { todayLocalIso } from "@/lib/format"
import { useConflict } from "@/lib/hooks/use-conflict"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { useFreshVersion } from "@/lib/hooks/use-fresh-version"
import {
  MAX_LEAVE_NOTE_LENGTH,
  dayCountLabel,
  leavePeriodLabel,
  leaveRangeRefusal,
  rangeDayCount,
} from "@/lib/doctor-leaves"

interface DoctorLeaveDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** The congé to show or change; null opens a new one. */
  leave: DoctorLeaveDto | null
  /** A new congé's practitioner when the caller knows it (the agenda's filter, the card's owner). */
  defaultDoctorId?: string | null
  /** A new congé's first day, `YYYY-MM-DD`; today when absent. */
  defaultStartDay?: string
  /** Takes the user to the first booked visit the congé covers; a full navigation to the agenda by default. */
  onShowDay?: (dayKey: string, doctorId: string) => void
  /** After a save or a delete — the caller refreshes its own list rather than waiting for the broadcast. */
  onChanged?: () => void
}

/**
 * Add, change or delete one congé. A doctor looking at a colleague's sees it read-only; the server enforces
 * the same rule. Visits already booked inside the congé are counted on save and never touched.
 */
export function DoctorLeaveDialog({
  open,
  onOpenChange,
  leave,
  defaultDoctorId,
  defaultStartDay,
  onShowDay,
  onChanged,
}: DoctorLeaveDialogProps) {
  const { user } = useSession()
  const { doctors, currentUserDoctor } = useDoctors()
  const staffWide = canEditAnyLeave(user?.role)
  const guard = useDirtyGuard(open, onOpenChange)
  const conflict = useConflict()

  const [doctorId, setDoctorId] = useState("")
  const [startDay, setStartDay] = useState("")
  const [endDay, setEndDay] = useState("")
  const [note, setNote] = useState("")
  const [rangeError, setRangeError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

  const { source: freshLeave, resync } = useFreshVersion(open, leave?.id, leave, async () => {
    const rows = await doctorsApi.getLeaves(leave!.doctorId)
    return rows.find((row) => row.id === leave!.id) ?? null
  })

  const ownDoctorId = currentUserDoctor?.id ?? null
  const readOnly = Boolean(leave) && !staffWide && leave?.doctorId !== ownDoctorId
  const rosterWithIds = doctors.filter((d): d is typeof d & { id: string } => Boolean(d.id))
  const pickableDoctors = staffWide ? rosterWithIds : rosterWithIds.filter((d) => d.id === ownDoctorId)
  const ownDoctorMissing = !staffWide && !ownDoctorId
  const doctorName = (id: string) => rosterWithIds.find((d) => d.id === id)?.name ?? leave?.doctorName ?? ""

  // The agenda's filter, else the user's own record, else the cabinet's only practitioner.
  const preferredDoctorId = () =>
    [defaultDoctorId, ownDoctorId].find((id) => id && pickableDoctors.some((d) => d.id === id)) ??
    (pickableDoctors.length === 1 ? pickableDoctors[0].id : "")

  const hydrate = (from: DoctorLeaveDto | null) => {
    if (from) {
      setDoctorId(from.doctorId)
      setStartDay(from.startDate)
      setEndDay(from.endDate)
      setNote(from.note ?? "")
      return
    }
    const first = defaultStartDay || todayLocalIso()
    setDoctorId(preferredDoctorId())
    setStartDay(first)
    setEndDay(first)
    setNote("")
  }
  // Read through refs so the effects below fire on the open edge only and never on a re-render of the roster.
  const latest = useRef({ hydrate, preferredDoctorId, leave })
  latest.current = { hydrate, preferredDoctorId, leave }
  const resetConflict = conflict.reset
  const leaveId = leave?.id ?? null

  useEffect(() => {
    if (!open) return
    latest.current.hydrate(latest.current.leave)
    setRangeError(null)
    setConfirmDelete(false)
    resetConflict()
  }, [open, leaveId, resetConflict])

  // The roster can arrive after the dialog opened: fill an empty practitioner then, nothing else.
  const rosterReady = pickableDoctors.length > 0
  useEffect(() => {
    if (open && !leaveId && rosterReady) setDoctorId((current) => current || latest.current.preferredDoctorId())
  }, [open, leaveId, rosterReady])

  const dayCount = useMemo(() => rangeDayCount(startDay, endDay), [startDay, endDay])

  const changeStart = (value: string) => {
    setStartDay(value)
    if (value && (!endDay || endDay < value)) setEndDay(value)
    setRangeError(null)
  }

  const announce = (result: DoctorLeaveSaveResultDto, verb: string) => {
    const { leave: saved, appointmentsDuring, firstAppointmentDay } = result
    if (appointmentsDuring > 0 && firstAppointmentDay) {
      toast.warning(`Congé ${verb}`, {
        description:
          appointmentsDuring === 1
            ? "1 rendez-vous est déjà prévu pendant ce congé."
            : `${appointmentsDuring} rendez-vous sont déjà prévus pendant ce congé.`,
        duration: 12_000,
        action: {
          label: "Voir",
          onClick: () =>
            onShowDay
              ? onShowDay(firstAppointmentDay, saved.doctorId)
              : window.location.assign(
                  `/appointments?date=${firstAppointmentDay}&view=day&doctorId=${saved.doctorId}`,
                ),
        },
      })
      return
    }
    toast.success(`Congé ${verb}`, { description: `${saved.doctorName} — ${leavePeriodLabel(saved)}` })
  }

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault()
    if (readOnly || saving) return
    if (!doctorId) {
      conflict.setError("Choisissez le praticien.")
      return
    }
    const refusal = leaveRangeRefusal(startDay, endDay)
    if (refusal) {
      setRangeError(refusal)
      return
    }
    const input = { startDate: startDay, endDate: endDay, note: note.trim() || null }
    setSaving(true)
    conflict.clearMessage()
    try {
      if (leave) {
        const result = await doctorsApi.updateLeave(leave.doctorId, leave.id, {
          ...input,
          version: freshLeave?.version ?? leave.version,
        })
        announce(result, "modifié")
      } else {
        announce(await doctorsApi.createLeave(doctorId, input), "enregistré")
      }
      guard.markClean()
      onOpenChange(false)
      onChanged?.()
    } catch (err) {
      if (!conflict.capture(err, "Le congé n'a pas pu être enregistré.")) await resync()
    } finally {
      setSaving(false)
    }
  }

  const reload = async () => {
    if (!leave) return
    try {
      const rows = await doctorsApi.getLeaves(leave.doctorId)
      const current = rows.find((row) => row.id === leave.id)
      if (!current) {
        toast.info("Ce congé a été supprimé entre-temps.")
        guard.markClean()
        onOpenChange(false)
        return
      }
      hydrate(current)
      await resync()
      conflict.reset()
    } catch (err) {
      showErrorToast(err)
    }
  }

  const title = readOnly ? "Congé" : leave ? "Modifier le congé" : "Nouveau congé"

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>{title}</DialogTitle>
            <DialogDescription className="sr-only">
              Les jours où le praticien est absent du cabinet.
            </DialogDescription>
          </DialogHeader>

          {readOnly && leave ? (
            <div className="space-y-1 text-sm">
              <p className="font-medium">{leave.doctorName}</p>
              <p>
                {leavePeriodLabel(leave)} · {dayCountLabel(leave.dayCount)}
              </p>
              {leave.note && <p className="text-muted-foreground">{leave.note}</p>}
            </div>
          ) : (
            // `noValidate`: « Au »'s `min` would otherwise block the submit with the browser's own (English) bubble.
            <form id="doctor-leave-form" noValidate onSubmit={handleSubmit} className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="leave-doctor">Praticien</Label>
                {ownDoctorMissing ? (
                  <p role="status" className="text-sm text-muted-foreground">
                    Votre compte n&apos;est lié à aucun praticien.
                  </p>
                ) : (
                  <Select
                    value={doctorId}
                    onValueChange={setDoctorId}
                    disabled={Boolean(leave) || pickableDoctors.length <= 1}
                  >
                    <SelectTrigger id="leave-doctor">
                      <SelectValue placeholder="Choisir le praticien">{doctorId ? doctorName(doctorId) : undefined}</SelectValue>
                    </SelectTrigger>
                    <SelectContent>
                      {pickableDoctors.map((d) => (
                        <SelectItem key={d.id} value={d.id}>
                          {d.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              </div>

              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="leave-start">Du</Label>
                  <Input id="leave-start" type="date" value={startDay} onChange={(e) => changeStart(e.target.value)} />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="leave-end">Au</Label>
                  <Input
                    id="leave-end"
                    type="date"
                    value={endDay}
                    min={startDay || undefined}
                    aria-invalid={Boolean(rangeError)}
                    aria-describedby={rangeError ? "leave-range-error" : undefined}
                    onChange={(e) => {
                      setEndDay(e.target.value)
                      setRangeError(null)
                    }}
                  />
                </div>
              </div>
              {rangeError ? (
                <p id="leave-range-error" className="-mt-2 text-xs text-destructive">
                  {rangeError}
                </p>
              ) : (
                dayCount !== null &&
                dayCount >= 1 && <p className="-mt-2 text-xs text-muted-foreground">{dayCountLabel(dayCount)}</p>
              )}

              <div className="space-y-2">
                <Label htmlFor="leave-note">Motif</Label>
                <Input
                  id="leave-note"
                  value={note}
                  maxLength={MAX_LEAVE_NOTE_LENGTH}
                  placeholder="Vacances, formation…"
                  onChange={(e) => setNote(e.target.value)}
                />
              </div>

              <FormErrorBanner
                message={conflict.error}
                action={conflict.isConflict ? { label: "Recharger", onClick: () => void reload(), disabled: saving } : undefined}
              />
            </form>
          )}

          <DialogFooter className="gap-2 md:justify-between">
            {leave && !readOnly ? (
              <Button
                type="button"
                variant="ghost"
                className="gap-2 text-destructive hover:text-destructive"
                onClick={() => setConfirmDelete(true)}
                disabled={saving}
              >
                <Trash2 className="size-4" aria-hidden="true" />
                Supprimer
              </Button>
            ) : (
              <span className="hidden md:block" />
            )}
            <div className="flex flex-col-reverse gap-2 md:flex-row">
              <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
                {readOnly ? "Fermer" : "Annuler"}
              </Button>
              {!readOnly && (
                <Button type="submit" form="doctor-leave-form" disabled={saving || ownDoctorMissing}>
                  {saving ? "Enregistrement…" : "Enregistrer"}
                </Button>
              )}
            </div>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <DeleteLeaveConfirm
        leave={leave}
        open={confirmDelete}
        onOpenChange={setConfirmDelete}
        onDeleted={() => {
          guard.markClean()
          onOpenChange(false)
          onChanged?.()
        }}
      />

      <DiscardChangesDialog guard={guard} />
    </>
  )
}

interface DeleteLeaveConfirmProps {
  leave: DoctorLeaveDto | null
  open: boolean
  onOpenChange: (open: boolean) => void
  onDeleted?: () => void
}

/** The one delete confirmation, shared by the dialog and the card's row menu. It names the congé it removes. */
export function DeleteLeaveConfirm({ leave, open, onOpenChange, onDeleted }: DeleteLeaveConfirmProps) {
  const [deleting, setDeleting] = useState(false)

  const handleDelete = async () => {
    if (!leave || deleting) return
    setDeleting(true)
    try {
      await doctorsApi.deleteLeave(leave.doctorId, leave.id)
      toast.success("Congé supprimé", { description: `${leave.doctorName} — ${leavePeriodLabel(leave)}` })
      onOpenChange(false)
      onDeleted?.()
    } catch (err) {
      showErrorToast(err)
    } finally {
      setDeleting(false)
    }
  }

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            {leave ? `Supprimer le congé de ${leave.doctorName} ${leavePeriodLabel(leave)} ?` : "Supprimer le congé ?"}
          </AlertDialogTitle>
          <AlertDialogDescription>Les rendez-vous déjà prévus ne changent pas.</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={deleting}>Retour</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            disabled={deleting}
            onClick={(event) => {
              event.preventDefault()
              void handleDelete()
            }}
          >
            {deleting ? "Suppression…" : "Supprimer le congé"}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
