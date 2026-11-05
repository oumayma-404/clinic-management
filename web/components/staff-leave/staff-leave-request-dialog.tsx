"use client"

import type React from "react"

import { useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { Button } from "@/components/ui/button"
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { ModeSegmented } from "@/components/ui/mode-segmented"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { teamApi, type StaffLeavePreviewDto, type StaffLeaveType } from "@/lib/api/team"
import { getErrorMessage } from "@/lib/errors"
import { todayLocalIso } from "@/lib/format"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import {
  MAX_STAFF_LEAVE_REASON,
  STAFF_LEAVE_TYPES,
  formatStaffDays,
  staffDaysLabel,
  staffLeavePeriodLabel,
  staffLeaveRangeRefusal,
  staffLeaveShortPeriod,
} from "@/lib/staff-leave"

/** Whose congé: the signed-in secretary's own request, or one an admin records for her. */
export type StaffLeaveFormTarget = { kind: "self" } | { kind: "admin"; userId: string; userName: string }

interface StaffLeaveRequestDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  target: StaffLeaveFormTarget
  onSaved?: () => void
}

/**
 * « Demander un congé » — or, for an admin, « Ajouter une absence » (saved accepted at once). The day count and the
 * balance after come from the server, so the form never announces a figure the save would not store.
 */
export function StaffLeaveRequestDialog({ open, onOpenChange, target, onSaved }: StaffLeaveRequestDialogProps) {
  const guard = useDirtyGuard(open, onOpenChange)
  const [type, setType] = useState<StaffLeaveType>("PaidLeave")
  const [startDay, setStartDay] = useState("")
  const [endDay, setEndDay] = useState("")
  const [halfDay, setHalfDay] = useState(false)
  const [reason, setReason] = useState("")
  const [rangeError, setRangeError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [preview, setPreview] = useState<StaffLeavePreviewDto | null>(null)
  const previewTicket = useRef(0)

  const forAdmin = target.kind === "admin"
  const targetUserId = target.kind === "admin" ? target.userId : undefined

  useEffect(() => {
    if (!open) return
    const today = todayLocalIso()
    setType("PaidLeave")
    setStartDay(today)
    setEndDay(today)
    setHalfDay(false)
    setReason("")
    setRangeError(null)
    setError(null)
    setPreview(null)
  }, [open])

  const singleDay = Boolean(startDay) && startDay === endDay
  const effectiveHalfDay = halfDay && singleDay

  // The server counts the cabinet's open days; asked again 300 ms after the last change.
  useEffect(() => {
    if (!open || staffLeaveRangeRefusal(startDay, endDay, effectiveHalfDay)) {
      setPreview(null)
      return
    }
    const ticket = ++previewTicket.current
    const timer = window.setTimeout(() => {
      teamApi
        .preview({ userId: targetUserId, type, startDate: startDay, endDate: endDay, halfDay: effectiveHalfDay })
        .then((result) => {
          if (ticket === previewTicket.current) setPreview(result)
        })
        .catch(() => {
          if (ticket === previewTicket.current) setPreview(null)
        })
    }, 300)
    return () => window.clearTimeout(timer)
  }, [open, startDay, endDay, effectiveHalfDay, type, targetUserId])

  const changeStart = (value: string) => {
    setStartDay(value)
    if (value && (!endDay || endDay < value)) setEndDay(value)
    setRangeError(null)
  }

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault()
    if (saving) return
    const refusal = staffLeaveRangeRefusal(startDay, endDay, effectiveHalfDay)
    if (refusal) {
      setRangeError(refusal)
      return
    }
    if (!reason.trim()) {
      setError("Indiquez le motif.")
      return
    }
    const input = { type, startDate: startDay, endDate: endDay, halfDay: effectiveHalfDay, reason: reason.trim() }
    setSaving(true)
    setError(null)
    try {
      if (target.kind === "admin") {
        await teamApi.recordAbsence(target.userId, input)
        toast.success("Absence enregistrée", { description: `${target.userName} — ${staffLeavePeriodLabel(input)}` })
      } else {
        await teamApi.submit(input)
        toast.success("Demande envoyée", { description: staffLeavePeriodLabel(input) })
      }
      guard.markClean()
      onOpenChange(false)
      onSaved?.()
    } catch (err) {
      setError(getErrorMessage(err, forAdmin ? "L'absence n'a pas pu être enregistrée." : "La demande n'a pas pu être envoyée."))
    } finally {
      setSaving(false)
    }
  }

  const title = forAdmin ? "Ajouter une absence" : "Demander un congé"

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>{title}</DialogTitle>
            <DialogDescription className={forAdmin ? undefined : "sr-only"}>
              {forAdmin ? target.userName : "Les jours où vous serez absente du cabinet."}
            </DialogDescription>
          </DialogHeader>

          {/* `DialogBody`: the fields scroll and the footer stays pinned on a phone sheet (frontend-web.md § 5). */}
          <DialogBody>
          {/* `noValidate`: « Au »'s `min` would otherwise block the submit with the browser's own (English) bubble. */}
          <form id="staff-leave-form" noValidate onSubmit={handleSubmit} className="space-y-4">
            <div className="space-y-2">
              <p id="staff-leave-type-label" className="text-sm leading-none font-medium">
                Type
              </p>
              <ModeSegmented
                value={type}
                onChange={setType}
                options={STAFF_LEAVE_TYPES.map((option) => ({ value: option.value, label: option.label }))}
                ariaLabel="Type de congé"
                className="grid grid-cols-2 gap-1 sm:flex"
              />
            </div>

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="staff-leave-start">Du</Label>
                <Input id="staff-leave-start" type="date" value={startDay} onChange={(e) => changeStart(e.target.value)} />
              </div>
              <div className="space-y-2">
                <Label htmlFor="staff-leave-end">Au</Label>
                <Input
                  id="staff-leave-end"
                  type="date"
                  value={endDay}
                  min={startDay || undefined}
                  aria-invalid={Boolean(rangeError)}
                  aria-describedby={rangeError ? "staff-leave-range-error" : undefined}
                  onChange={(e) => {
                    setEndDay(e.target.value)
                    setRangeError(null)
                  }}
                />
              </div>
            </div>
            {rangeError && (
              <p id="staff-leave-range-error" className="-mt-2 text-xs text-destructive">
                {rangeError}
              </p>
            )}

            {singleDay && (
              <div className="flex items-center justify-between gap-3">
                <Label htmlFor="staff-leave-half">Demi-journée</Label>
                <Switch id="staff-leave-half" checked={halfDay} onCheckedChange={setHalfDay} />
              </div>
            )}

            <div className="space-y-2">
              <div className="flex items-baseline justify-between gap-3">
                <Label htmlFor="staff-leave-reason">Motif</Label>
                <span className="text-xs text-muted-foreground tabular-nums" aria-hidden="true">
                  {reason.length} / {MAX_STAFF_LEAVE_REASON}
                </span>
              </div>
              <Textarea
                id="staff-leave-reason"
                value={reason}
                rows={3}
                maxLength={MAX_STAFF_LEAVE_REASON}
                onChange={(e) => setReason(e.target.value)}
              />
            </div>

            {preview && (
              <div role="status" className="flex items-center gap-3.5 rounded-xl border border-primary/15 bg-accent/60 p-3.5">
                <span className="flex size-14 shrink-0 flex-col items-center justify-center rounded-lg border bg-card">
                  <span className="text-xl leading-none font-semibold text-primary tabular-nums">
                    {formatStaffDays(preview.days)}
                  </span>
                  <span className="text-2xs text-muted-foreground">{preview.days > 1 ? "jours" : "jour"}</span>
                </span>
                <span className="min-w-0 space-y-0.5">
                  <span className="block font-semibold">
                    {staffLeaveShortPeriod({ startDate: startDay, endDate: endDay, halfDay: effectiveHalfDay })}
                  </span>
                  {preview.balanceAfter !== null && (
                    <span className="block text-sm text-foreground/80">
                      Il restera <strong className="tabular-nums">{staffDaysLabel(preview.balanceAfter)}</strong>
                      {startDay && Number(startDay.slice(0, 4)) !== new Date().getFullYear() && ` en ${startDay.slice(0, 4)}`}
                    </span>
                  )}
                </span>
              </div>
            )}

            <FormErrorBanner message={error} />
          </form>
          </DialogBody>

          <DialogFooter className="flex-col-reverse gap-2 md:flex-row">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
              Annuler
            </Button>
            <Button type="submit" form="staff-leave-form" disabled={saving}>
              {saving ? "Envoi…" : forAdmin ? "Enregistrer l'absence" : "Envoyer la demande"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <DiscardChangesDialog guard={guard} />
    </>
  )
}
