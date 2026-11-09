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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import {
  teamApi,
  type StaffLeaveHalfDayPart,
  type StaffLeaveInput,
  type StaffLeavePreviewDto,
  type StaffLeaveType,
} from "@/lib/api/team"
import { getErrorMessage } from "@/lib/errors"
import { todayLocalIso } from "@/lib/format"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import {
  MAX_STAFF_LEAVE_REASON,
  STAFF_LEAVE_TYPES,
  firstOfLabel,
  formatStaffDays,
  isOverdrawn,
  parseStaffDays,
  staffBalanceShort,
  staffLeavePeriodLabel,
  staffLeaveRangeRefusal,
  staffLeaveShortPeriod,
} from "@/lib/staff-leave"
import { cn } from "@/lib/utils"
import { StaffDaysStepper } from "./staff-leave-bits"

/**
 * Whose congé: the signed-in secretary's own request, or one an admin records for her. An admin target without a
 * `userId` (the Congés tab) picks the secretary in the form, from `people`.
 */
export type StaffLeaveFormTarget = { kind: "self" } | { kind: "admin"; userId?: string; userName?: string }

/** A secretary the admin may record an absence for. `TeamLeaveMemberDto` fits as is. */
export interface StaffLeavePerson {
  userId: string
  fullName: string
}

interface StaffLeaveRequestDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  target: StaffLeaveFormTarget
  /** Admin without a preselected person: the secretaries offered by the « Secrétaire » picker. */
  people?: ReadonlyArray<StaffLeavePerson>
  onSaved?: () => void
}

type DayPart = "Full" | StaffLeaveHalfDayPart

const DAY_PARTS: Array<{ value: DayPart; label: string }> = [
  { value: "Full", label: "Journée" },
  { value: "Morning", label: "Matin" },
  { value: "Afternoon", label: "Après-midi" },
]

const COUNT_FORMAT_ERROR = "Indiquez un nombre de jours, par demi-journée (ex. 2 ou 2,5)."

/**
 * « Demander un congé » — or, for an admin, « Ajouter une absence » (saved accepted at once, with « Jours à
 * décompter »). The day count, the balance after and any refusal of the dates come from the server's preview, under
 * Du/Au, so the form never announces a figure the save would not store.
 */
export function StaffLeaveRequestDialog({ open, onOpenChange, target, people = [], onSaved }: StaffLeaveRequestDialogProps) {
  const forAdmin = target.kind === "admin"
  const picking = forAdmin && !target.userId
  const [pickedId, setPickedId] = useState("")
  const [type, setType] = useState<StaffLeaveType>("PaidLeave")
  const [startDay, setStartDay] = useState("")
  const [endDay, setEndDay] = useState("")
  const [dayPart, setDayPart] = useState<DayPart>("Full")
  const [reason, setReason] = useState("")
  // null = follow the preview's count; a string once the admin typed or stepped it.
  const [countedText, setCountedText] = useState<string | null>(null)
  const [rangeError, setRangeError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [preview, setPreview] = useState<{ key: string; value: StaffLeavePreviewDto | null } | null>(null)
  const previewTicket = useRef(0)

  // Button-only choices fire no `input` event: the guard is told about them.
  const guard = useDirtyGuard(
    open,
    onOpenChange,
    type !== "PaidLeave" || dayPart !== "Full" || countedText !== null || (picking && pickedId !== ""),
  )

  const userId = target.kind === "admin" ? (target.userId ?? (pickedId || undefined)) : undefined
  const person = target.kind === "admin" ? (target.userId ? null : people.find((p) => p.userId === pickedId)) : null
  const userName = target.kind === "admin" ? (target.userName ?? person?.fullName ?? "") : ""

  useEffect(() => {
    if (!open) return
    const today = todayLocalIso()
    setPickedId("")
    setType("PaidLeave")
    setStartDay(today)
    setEndDay(today)
    setDayPart("Full")
    setReason("")
    setCountedText(null)
    setRangeError(null)
    setError(null)
    setPreview(null)
  }, [open])

  const singleDay = Boolean(startDay) && startDay === endDay
  const halfDay = singleDay && dayPart !== "Full"
  const halfDayPart = halfDay ? (dayPart as StaffLeaveHalfDayPart) : null
  const rangeRefusal = staffLeaveRangeRefusal(startDay, endDay, halfDay)
  const canPreview = open && !rangeRefusal && (!forAdmin || Boolean(userId))
  const previewKey = `${userId ?? ""}|${type}|${startDay}|${endDay}|${halfDay}`
  const fresh = preview?.key === previewKey ? preview.value : undefined

  // The server counts the cabinet's open days; asked again 300 ms after the last change.
  useEffect(() => {
    if (!canPreview) return
    const ticket = ++previewTicket.current
    const timer = window.setTimeout(() => {
      teamApi
        .preview({ userId, type, startDate: startDay, endDate: endDay, halfDay })
        .then((value) => {
          if (ticket === previewTicket.current) setPreview({ key: previewKey, value })
        })
        .catch(() => {
          if (ticket === previewTicket.current) setPreview({ key: previewKey, value: null })
        })
    }, 300)
    return () => window.clearTimeout(timer)
  }, [canPreview, previewKey, userId, type, startDay, endDay, halfDay])

  // What the preview's figures were for no longer holds: the count follows the next preview again.
  const resetCount = () => setCountedText(null)

  const changeStart = (value: string) => {
    setStartDay(value)
    if (value && (!endDay || endDay < value)) setEndDay(value)
    setRangeError(null)
    resetCount()
  }

  // The admin's count: typed, or the preview's while untouched.
  const shownCount = countedText ?? (fresh ? formatStaffDays(fresh.days) : "")
  const counted = shownCount.trim() ? parseStaffDays(shownCount) : null

  /** The preview's refusal of these dates, said before the send: an overlap, or no open day. */
  const blockedBy = (() => {
    if (!fresh) return null
    const whose = forAdmin ? `${userName || "Elle"} a déjà` : "Vous avez déjà"
    if (fresh.overlap) {
      const what = fresh.overlap.status === "Pending" ? "une demande en attente" : "un congé accepté"
      return `${whose} ${what} ${staffLeavePeriodLabel(fresh.overlap)}.`
    }
    if (fresh.noOpenDay) return singleDay ? "Le cabinet est fermé ce jour-là." : "Le cabinet est fermé ces jours-là."
    return null
  })()

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault()
    if (saving) return
    if (forAdmin && !userId) {
      setError("Choisissez la secrétaire.")
      return
    }
    if (rangeRefusal) {
      setRangeError(rangeRefusal)
      return
    }
    if (blockedBy) {
      setError(blockedBy)
      return
    }
    if (!reason.trim()) {
      setError("Indiquez le motif.")
      return
    }
    if (forAdmin && shownCount.trim() && counted === null) {
      setError(COUNT_FORMAT_ERROR)
      return
    }
    const input: StaffLeaveInput = {
      type,
      startDate: startDay,
      endDate: endDay,
      halfDay,
      halfDayPart,
      reason: reason.trim(),
    }
    setSaving(true)
    setError(null)
    try {
      if (forAdmin && userId) {
        await teamApi.recordAbsence(userId, counted === null ? input : { ...input, countedDays: counted })
        toast.success("Absence enregistrée", { description: `${userName} — ${staffLeavePeriodLabel(input)}` })
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
            <DialogDescription className={forAdmin && !picking ? undefined : "sr-only"}>
              {forAdmin ? (picking ? "La secrétaire, les jours et le motif de l'absence." : userName) : "Les jours où vous serez absente du cabinet."}
            </DialogDescription>
          </DialogHeader>

          {/* `DialogBody`: the fields scroll and the footer stays pinned on a phone sheet (frontend-web.md § 5). */}
          <DialogBody>
          {/* `noValidate`: « Au »'s `min` would otherwise block the submit with the browser's own (English) bubble. */}
          <form id="staff-leave-form" noValidate onSubmit={handleSubmit} className="space-y-4">
            {picking && (
              <div className="space-y-2">
                <Label htmlFor="staff-leave-person">Secrétaire</Label>
                {people.length > 0 ? (
                  <Select
                    value={pickedId}
                    onValueChange={(value) => {
                      setPickedId(value)
                      setError(null)
                      resetCount()
                    }}
                  >
                    <SelectTrigger id="staff-leave-person" className="w-full">
                      <SelectValue placeholder="Choisir une secrétaire…" />
                    </SelectTrigger>
                    <SelectContent>
                      {people.map((p) => (
                        <SelectItem key={p.userId} value={p.userId}>
                          {p.fullName}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                ) : (
                  <p className="text-sm text-muted-foreground">Aucune secrétaire active.</p>
                )}
              </div>
            )}

            <div className="space-y-2">
              <p id="staff-leave-type-label" className="text-sm leading-none font-medium">
                Type
              </p>
              {/* Two columns at every width: from `md:` this dialog is ~400 px whatever the window says. */}
              <ModeSegmented
                value={type}
                onChange={(value) => {
                  setType(value)
                  resetCount()
                }}
                options={STAFF_LEAVE_TYPES.map((option) => ({ value: option.value, label: option.label }))}
                ariaLabel="Type de congé"
                className="grid grid-cols-2 gap-1"
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
                    resetCount()
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
              <ModeSegmented
                value={dayPart}
                onChange={(value) => {
                  setDayPart(value)
                  resetCount()
                }}
                options={DAY_PARTS}
                ariaLabel="Journée ou demi-journée"
                className="[&>button]:px-1.5 sm:[&>button]:px-3"
              />
            )}

            {canPreview && (
              <CountSlot
                value={preview === null ? undefined : preview.value}
                fresh={fresh !== undefined}
                period={staffLeaveShortPeriod({ startDate: startDay, endDate: endDay, halfDay, halfDayPart })}
                type={type}
                forAdmin={forAdmin}
                blockedBy={blockedBy}
                countedShift={
                  forAdmin && fresh && counted !== null && counted !== fresh.days
                    ? startDay.slice(0, 4) === endDay.slice(0, 4)
                      ? fresh.days - counted
                      : null
                    : 0
                }
              />
            )}

            {forAdmin && canPreview && (
              <div className="space-y-2">
                <Label htmlFor="staff-leave-counted">Jours à décompter</Label>
                <StaffDaysStepper
                  id="staff-leave-counted"
                  value={shownCount}
                  onChange={(text) => setCountedText(text)}
                  fallback={fresh?.days ?? 0}
                />
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

/**
 * The slot under Du/Au: the server's day count and what it does to her balance — or why it cannot be sent, or that
 * it does not touch her balance, or that the count could not be read. Never silently empty.
 */
function CountSlot({
  value,
  fresh,
  period,
  type,
  forAdmin,
  blockedBy,
  countedShift,
}: {
  /** undefined = not read yet; null = the read failed. */
  value: StaffLeavePreviewDto | null | undefined
  fresh: boolean
  period: string
  type: StaffLeaveType
  forAdmin: boolean
  blockedBy: string | null
  /** Days the admin's count takes off the preview's (moves « Il lui restera »); null = not derivable here. */
  countedShift: number | null
}) {
  if (value === undefined) {
    return (
      <p role="status" className="rounded-xl border border-dashed p-3.5 text-sm text-muted-foreground">
        Décompte en cours…
      </p>
    )
  }
  if (value === null) {
    return (
      <p role="status" className="rounded-xl border border-dashed p-3.5 text-sm text-muted-foreground">
        Décompte indisponible
      </p>
    )
  }

  const shift = countedShift ?? 0
  const after = value.balanceAfter === null || countedShift === null ? null : value.balanceAfter + shift
  const withPending =
    value.balanceAfterWithPending === null || countedShift === null ? null : value.balanceAfterWithPending + shift

  const line = (() => {
    if (blockedBy) return <span className="text-destructive">{blockedBy}</span>
    if (type !== "PaidLeave") return forAdmin ? "Ne compte pas sur son solde" : "Ne compte pas sur votre solde"
    if (value.balanceAfter === null) return "Solde non suivi"
    if (after === null) return null
    const on = value.balanceAfterOn ? ` au ${firstOfLabel(value.balanceAfterOn)}` : ""
    return (
      <>
        {isOverdrawn(after) ? (
          <strong className="text-destructive tabular-nums">{staffBalanceShort(after)}</strong>
        ) : (
          <>
            {forAdmin ? "Il lui restera" : "Il restera"} <strong className="tabular-nums">{staffBalanceShort(after)}</strong>
          </>
        )}
        {on}
        {withPending !== null && (
          <>
            {" · "}
            <span className={cn("tabular-nums", isOverdrawn(withPending) && "text-destructive")}>
              {staffBalanceShort(withPending)}
            </span>{" "}
            {forAdmin ? "avec sa demande en attente" : "avec votre demande en attente"}
          </>
        )}
      </>
    )
  })()

  return (
    <div
      role="status"
      className={cn(
        "flex items-center gap-3.5 rounded-xl border p-3.5 transition-opacity",
        blockedBy ? "border-destructive/20 bg-destructive-wash" : "border-primary/15 bg-accent/60",
        !fresh && "opacity-60",
      )}
    >
      <span className="flex size-14 shrink-0 flex-col items-center justify-center rounded-lg border bg-card">
        <span className="text-xl leading-none font-semibold text-primary tabular-nums">{formatStaffDays(value.days)}</span>
        <span className="text-2xs text-muted-foreground">{value.days > 1 ? "jours" : "jour"}</span>
      </span>
      <span className="min-w-0 space-y-0.5">
        <span className="block font-semibold">{period}</span>
        {line && <span className="block text-sm text-foreground/80">{line}</span>}
      </span>
    </div>
  )
}
