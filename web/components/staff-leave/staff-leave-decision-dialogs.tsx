"use client"

import type React from "react"

import { useEffect, useState } from "react"
import { Check } from "lucide-react"
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
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { InitialsAvatar } from "@/components/ui/initials-avatar"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { teamApi, type StaffLeaveRequestDto } from "@/lib/api/team"
import { showErrorToast } from "@/lib/errors"
import { todayLocalIso } from "@/lib/format"
import { useConflict } from "@/lib/hooks/use-conflict"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import {
  MAX_STAFF_LEAVE_REASON,
  balanceAfterLabel,
  firstOfLabel,
  formatStaffDays,
  isOverdrawn,
  parseStaffDays,
  staffBalanceLabel,
  staffDaysLabel,
  staffLeaveCancelWording,
  staffLeavePeriodLabel,
  staffLeaveShortPeriod,
} from "@/lib/staff-leave"
import { StaffDaysStepper } from "./staff-leave-bits"
import { cn } from "@/lib/utils"

interface DecisionDialogProps {
  request: StaffLeaveRequestDto | null
  open: boolean
  onOpenChange: (open: boolean) => void
  /** After the decision — or after « Recharger » on a request somebody else answered first. */
  onChanged?: () => void
}

const ALREADY_ANSWERED = "Cette demande a déjà reçu une réponse."

/**
 * The decide flow both dialogs share: re-read the request (the row in hand is as old as the last refetch), refuse by
 * name when it is no longer waiting, send the fresh version, then close. Typed input asks before it is discarded.
 */
function useLeaveDecision(
  request: StaffLeaveRequestDto | null,
  open: boolean,
  onOpenChange: (open: boolean) => void,
  onChanged?: () => void,
  alsoDirty = false,
) {
  const guard = useDirtyGuard(open, onOpenChange, alsoDirty)
  const conflict = useConflict()
  const [saving, setSaving] = useState(false)
  const resetConflict = conflict.reset

  useEffect(() => {
    if (open) resetConflict()
  }, [open, resetConflict])

  const decide = async (
    body: { approve: boolean; countedDays?: number; rejectionReason?: string },
    successTitle: string,
    failure: string,
  ) => {
    if (!request || saving) return
    setSaving(true)
    conflict.clearMessage()
    try {
      const fresh = await teamApi.getLeaveRequest(request.id)
      if (fresh.status !== "Pending") {
        conflict.raise(ALREADY_ANSWERED)
        return
      }
      await teamApi.decide(request.id, { ...body, version: fresh.version })
      toast.success(successTitle, { description: `${request.userName} — ${staffLeavePeriodLabel(request)}` })
      guard.markClean()
      onOpenChange(false)
      onChanged?.()
    } catch (err) {
      conflict.capture(err, failure)
    } finally {
      setSaving(false)
    }
  }

  const reload = () => {
    guard.markClean()
    onOpenChange(false)
    onChanged?.()
  }

  return { guard, conflict, saving, decide, reload }
}

/** Who, when and what — the request being answered, above the decision. */
function LeaveRecap({ request }: { request: StaffLeaveRequestDto }) {
  return (
    <div className="flex items-center gap-3 rounded-lg border bg-muted/30 p-3">
      <InitialsAvatar name={request.userName} className="size-10 text-xs" />
      <div className="min-w-0">
        <p className="font-medium [overflow-wrap:anywhere]">{request.userName}</p>
        <p className="text-sm text-muted-foreground">
          {staffLeaveShortPeriod(request)} · {request.typeLabel}
        </p>
      </div>
    </div>
  )
}

/**
 * The balance once these days are counted. The server's figure assumes the requested days; a same-year congé moves
 * by the difference, while a congé across the new year is split on the server — so there it is shown only while the
 * count is the one requested, never re-derived here.
 */
function balanceAfterCounting(request: StaffLeaveRequestDto, counted: number | null): number | null {
  if (request.balanceAfter === null || counted === null) return null
  if (counted === request.requestedDays) return request.balanceAfter
  const sameYear = request.startDate.slice(0, 4) === request.endDate.slice(0, 4)
  return sameYear ? request.balanceAfter + request.requestedDays - counted : null
}

/** « Accepter » — « Jours à décompter » prefilled with the counted days, in half days, editable for a jour férié. */
export function AcceptLeaveDialog({ request, open, onOpenChange, onChanged }: DecisionDialogProps) {
  const [days, setDays] = useState("")
  const [seed, setSeed] = useState("")
  // « − » / « + » fire no input event: a count moved off the prefill is an edit the guard must ask about.
  const { guard, conflict, saving, decide, reload } = useLeaveDecision(request, open, onOpenChange, onChanged, days !== seed)

  useEffect(() => {
    if (!open || !request) return
    const prefill = formatStaffDays(request.requestedDays)
    setDays(prefill)
    setSeed(prefill)
  }, [open, request])

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault()
    const counted = parseStaffDays(days)
    if (counted === null) {
      conflict.setError("Indiquez un nombre de jours, par demi-journée (ex. 2 ou 2,5).")
      return
    }
    void decide({ approve: true, countedDays: counted }, "Congé accepté", "Le congé n'a pas pu être accepté.")
  }

  const after = request ? balanceAfterCounting(request, parseStaffDays(days)) : null

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Accepter le congé</DialogTitle>
            <DialogDescription className="sr-only">
              {request ? `${request.userName} · ${request.typeLabel} · ${staffLeavePeriodLabel(request)}` : ""}
            </DialogDescription>
          </DialogHeader>
          <DialogBody>
            <form id="accept-leave-form" noValidate onSubmit={handleSubmit} className="space-y-5">
              {request && <LeaveRecap request={request} />}
              <div className="space-y-2">
                <Label htmlFor="accept-leave-days">Jours à décompter</Label>
                <div className="flex flex-wrap items-center gap-3">
                  <StaffDaysStepper
                    id="accept-leave-days"
                    value={days}
                    onChange={setDays}
                    fallback={request?.requestedDays ?? 0}
                  />
                  {request && (
                    <span className="text-sm text-muted-foreground">sur {staffDaysLabel(request.requestedDays)} demandés</span>
                  )}
                </div>
              </div>
              {request && after !== null && (
                <div role="status" className="flex items-baseline justify-between gap-3 border-t pt-3 text-sm">
                  <span className="text-muted-foreground">
                    {balanceAfterLabel(request.startDate)}
                    {request.balanceAfterOn && request.balanceAfterOn > todayLocalIso() && ` au ${firstOfLabel(request.balanceAfterOn)}`}
                  </span>
                  <span className={cn("font-semibold tabular-nums", isOverdrawn(after) && "text-destructive")}>
                    {staffBalanceLabel(after)}
                  </span>
                </div>
              )}
              <FormErrorBanner
                message={conflict.error}
                action={conflict.isConflict ? { label: "Recharger", onClick: reload, disabled: saving } : undefined}
              />
            </form>
          </DialogBody>
          <DialogFooter className="flex-col-reverse gap-2 md:flex-row">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
              Retour
            </Button>
            <Button type="submit" form="accept-leave-form" className="gap-1.5" disabled={saving}>
              {!saving && <Check className="size-4" aria-hidden="true" />}
              {saving ? "Enregistrement…" : "Accepter"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DiscardChangesDialog guard={guard} />
    </>
  )
}

/** « Refuser » — the motif is required; she reads it word for word. */
export function RejectLeaveDialog({ request, open, onOpenChange, onChanged }: DecisionDialogProps) {
  const { guard, conflict, saving, decide, reload } = useLeaveDecision(request, open, onOpenChange, onChanged)
  const [reason, setReason] = useState("")
  const [reasonError, setReasonError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setReason("")
    setReasonError(null)
  }, [open])

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault()
    if (!reason.trim()) {
      setReasonError("Indiquez le motif du refus.")
      return
    }
    void decide({ approve: false, rejectionReason: reason.trim() }, "Demande refusée", "Le refus n'a pas pu être enregistré.")
  }

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Refuser la demande</DialogTitle>
            <DialogDescription className="sr-only">
              {request
                ? `${request.userName} · ${request.typeLabel} · ${staffLeavePeriodLabel(request)} · ${staffDaysLabel(request.requestedDays)}`
                : ""}
            </DialogDescription>
          </DialogHeader>
          <DialogBody>
            <form id="reject-leave-form" noValidate onSubmit={handleSubmit} className="space-y-5">
              {request && <LeaveRecap request={request} />}
              <div className="space-y-2">
                <Label htmlFor="reject-leave-reason">Motif du refus</Label>
                <Textarea
                  id="reject-leave-reason"
                  value={reason}
                  rows={4}
                  maxLength={MAX_STAFF_LEAVE_REASON}
                  aria-invalid={Boolean(reasonError)}
                  aria-describedby={reasonError ? "reject-leave-reason-error" : undefined}
                  onChange={(e) => {
                    setReason(e.target.value)
                    setReasonError(null)
                  }}
                />
                {reasonError && (
                  <p id="reject-leave-reason-error" className="text-xs text-destructive">
                    {reasonError}
                  </p>
                )}
              </div>
              <FormErrorBanner
                message={conflict.error}
                action={conflict.isConflict ? { label: "Recharger", onClick: reload, disabled: saving } : undefined}
              />
            </form>
          </DialogBody>
          <DialogFooter className="flex-col-reverse gap-2 md:flex-row">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
              Retour
            </Button>
            <Button type="submit" form="reject-leave-form" variant="destructive" disabled={saving}>
              {saving ? "Enregistrement…" : "Refuser la demande"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DiscardChangesDialog guard={guard} />
    </>
  )
}

/** A waiting request is withdrawn; an accepted congé is cancelled. */
const isWaiting = (request: StaffLeaveRequestDto) => request.status === "Pending"
const wording = (request: StaffLeaveRequestDto | null) => staffLeaveCancelWording(request?.status ?? "Approved")

/**
 * « le congé de Ines Gharbi du 16 au 18 novembre » for the admin, « la demande du … » on her own page — the object of
 * « Annuler » / « Retirer » and of the row menu's name.
 */
export function leaveCancelSubject(request: StaffLeaveRequestDto, showName: boolean): string {
  const what = isWaiting(request) ? "la demande" : "le congé"
  return showName
    ? `${what} de ${request.userName} ${staffLeavePeriodLabel(request)}`
    : `${what} ${staffLeavePeriodLabel(request)}`
}

/** « Retirer » a waiting request, « Annuler » an accepted congé. */
export function leaveCancelVerb(request: StaffLeaveRequestDto): string {
  return isWaiting(request) ? "Retirer" : "Annuler"
}

/** What cancelling an accepted congé does: a congé payé's days come back; any other type just says what it was. */
function cancelConsequence(request: StaffLeaveRequestDto, whose: "votre" | "son"): string {
  const days = request.countedDays ?? request.requestedDays
  if (request.type !== "PaidLeave") return `${request.typeLabel} · ${staffDaysLabel(days)}`
  return `${staffDaysLabel(days)} ${days > 1 ? "reviennent" : "revient"} sur ${whose} solde.`
}

/**
 * The secretary withdraws her own waiting request, or cancels her accepted congé not yet begun. Names what it
 * withdraws; nothing is said in the past tense before it happens.
 */
export function CancelLeaveConfirm({ request, open, onOpenChange, onChanged }: DecisionDialogProps) {
  const [cancelling, setCancelling] = useState(false)
  const waiting = request ? isWaiting(request) : false

  const handleCancel = async () => {
    if (!request || cancelling) return
    setCancelling(true)
    try {
      const fresh = await teamApi.getLeaveRequest(request.id)
      if (fresh.status !== request.status) {
        toast.error("Cette demande a changé entre-temps", { description: "La liste est rechargée : vérifiez-la avant d'annuler." })
        onOpenChange(false)
        onChanged?.()
        return
      }
      await teamApi.cancel(request.id, fresh.version)
      toast.success(wording(request).done, { description: staffLeavePeriodLabel(request) })
      onOpenChange(false)
      onChanged?.()
    } catch (err) {
      showErrorToast(err, waiting ? "La demande n'a pas pu être retirée." : "Le congé n'a pas pu être annulé.")
      onChanged?.()
    } finally {
      setCancelling(false)
    }
  }

  const { action } = wording(request)

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            {request ? `${leaveCancelVerb(request)} ${leaveCancelSubject(request, false)} ?` : `${action} ?`}
          </AlertDialogTitle>
          <AlertDialogDescription>
            {request ? (waiting ? `${request.typeLabel} · ${staffDaysLabel(request.requestedDays)}` : cancelConsequence(request, "votre")) : ""}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={cancelling}>Retour</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            disabled={cancelling}
            onClick={(event) => {
              event.preventDefault()
              void handleCancel()
            }}
          >
            {cancelling ? (waiting ? "Retrait…" : "Annulation…") : action}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}

/**
 * The admin cancels a secretary's accepted congé or absence — whatever its date, so a wrong type or a past day can be
 * corrected. The motif is required: she reads it on her congé and in the notification.
 */
export function AdminCancelLeaveDialog({ request, open, onOpenChange, onChanged }: DecisionDialogProps) {
  const guard = useDirtyGuard(open, onOpenChange)
  const conflict = useConflict()
  const resetConflict = conflict.reset
  const [reason, setReason] = useState("")
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!open) return
    setReason("")
    setReasonError(null)
    resetConflict()
  }, [open, resetConflict])

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault()
    if (!request || saving) return
    if (!reason.trim()) {
      setReasonError("Indiquez le motif de l'annulation.")
      return
    }
    setSaving(true)
    conflict.clearMessage()
    try {
      const fresh = await teamApi.getLeaveRequest(request.id)
      if (fresh.status !== request.status) {
        conflict.raise("Ce congé a changé entre-temps.")
        return
      }
      await teamApi.cancel(request.id, fresh.version, reason.trim())
      toast.success(wording(request).done, {
        description: `${request.userName} — ${staffLeavePeriodLabel(request)}`,
      })
      guard.markClean()
      onOpenChange(false)
      onChanged?.()
    } catch (err) {
      conflict.capture(err, "Le congé n'a pas pu être annulé.")
    } finally {
      setSaving(false)
    }
  }

  const reload = () => {
    guard.markClean()
    onOpenChange(false)
    onChanged?.()
  }

  const { action } = wording(request)
  const returnsDays = Boolean(request && !isWaiting(request) && request.type === "PaidLeave")

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>{request ? `${leaveCancelVerb(request)} ${leaveCancelSubject(request, true)} ?` : `${action} ?`}</DialogTitle>
            {/* Said aloud only when it adds to the recap below: the days a congé payé gives back. */}
            <DialogDescription className={returnsDays ? undefined : "sr-only"}>
              {request ? (returnsDays ? cancelConsequence(request, "son") : `${request.typeLabel} · ${staffLeavePeriodLabel(request)}`) : ""}
            </DialogDescription>
          </DialogHeader>
          <DialogBody>
            <form id="cancel-leave-form" noValidate onSubmit={handleSubmit} className="space-y-5">
              {request && <LeaveRecap request={request} />}
              <div className="space-y-2">
                <Label htmlFor="cancel-leave-reason">Motif</Label>
                <Textarea
                  id="cancel-leave-reason"
                  value={reason}
                  rows={3}
                  maxLength={MAX_STAFF_LEAVE_REASON}
                  aria-invalid={Boolean(reasonError)}
                  aria-describedby={reasonError ? "cancel-leave-reason-error" : undefined}
                  onChange={(e) => {
                    setReason(e.target.value)
                    setReasonError(null)
                  }}
                />
                {reasonError && (
                  <p id="cancel-leave-reason-error" className="text-xs text-destructive">
                    {reasonError}
                  </p>
                )}
              </div>
              <FormErrorBanner
                message={conflict.error}
                action={conflict.isConflict ? { label: "Recharger", onClick: reload, disabled: saving } : undefined}
              />
            </form>
          </DialogBody>
          <DialogFooter className="flex-col-reverse gap-2 md:flex-row">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
              Retour
            </Button>
            <Button type="submit" form="cancel-leave-form" variant="destructive" disabled={saving}>
              {saving ? "Annulation…" : action}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DiscardChangesDialog guard={guard} />
    </>
  )
}
