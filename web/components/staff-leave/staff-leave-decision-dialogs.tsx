"use client"

import type React from "react"

import { useEffect, useState } from "react"
import { Check, Minus, Plus } from "lucide-react"
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
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { teamApi, type StaffLeaveRequestDto } from "@/lib/api/team"
import { showErrorToast } from "@/lib/errors"
import { useConflict } from "@/lib/hooks/use-conflict"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import {
  MAX_STAFF_DAYS,
  MAX_STAFF_LEAVE_REASON,
  balanceAfterLabel,
  formatStaffDays,
  parseStaffDays,
  staffDaysLabel,
  staffLeavePeriodLabel,
  staffLeaveShortPeriod,
} from "@/lib/staff-leave"

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
) {
  const guard = useDirtyGuard(open, onOpenChange)
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

const STEPPER_BUTTON =
  "flex w-11 items-center justify-center bg-muted/50 text-foreground transition-colors hover:bg-muted focus-visible:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset"

/** « Accepter » — « Jours à décompter » prefilled with the counted days, in half days, editable for a jour férié. */
export function AcceptLeaveDialog({ request, open, onOpenChange, onChanged }: DecisionDialogProps) {
  const { guard, conflict, saving, decide, reload } = useLeaveDecision(request, open, onOpenChange, onChanged)
  const [days, setDays] = useState("")

  useEffect(() => {
    if (open && request) setDays(formatStaffDays(request.requestedDays))
  }, [open, request])

  const step = (delta: number) => {
    const current = parseStaffDays(days) ?? request?.requestedDays ?? 0
    setDays(formatStaffDays(Math.min(MAX_STAFF_DAYS, Math.max(0, current + delta))))
  }

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
                  <div className="inline-flex items-stretch overflow-hidden rounded-lg border">
                    <button
                      type="button"
                      aria-label="Une demi-journée de moins"
                      onClick={() => step(-0.5)}
                      className={STEPPER_BUTTON}
                    >
                      <Minus className="size-4" aria-hidden="true" />
                    </button>
                    <Input
                      id="accept-leave-days"
                      inputMode="decimal"
                      value={days}
                      onChange={(e) => setDays(e.target.value)}
                      className="h-11 w-20 rounded-none border-0 border-x text-center font-semibold tabular-nums shadow-none md:text-base"
                    />
                    <button
                      type="button"
                      aria-label="Une demi-journée de plus"
                      onClick={() => step(0.5)}
                      className={STEPPER_BUTTON}
                    >
                      <Plus className="size-4" aria-hidden="true" />
                    </button>
                  </div>
                  {request && (
                    <span className="text-sm text-muted-foreground">sur {staffDaysLabel(request.requestedDays)} demandés</span>
                  )}
                </div>
              </div>
              {request && after !== null && (
                <div className="flex items-center justify-between gap-3 border-t pt-3 text-sm">
                  <span className="text-muted-foreground">{balanceAfterLabel(request.startDate)}</span>
                  <span className="font-semibold tabular-nums">{staffDaysLabel(after)}</span>
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

/** « Annuler le congé de Ines Gharbi du 16 au 18 novembre » for the admin; without the name on her own page. */
export function leaveCancelSubject(request: StaffLeaveRequestDto, showName: boolean): string {
  return showName
    ? `le congé de ${request.userName} ${staffLeavePeriodLabel(request)}`
    : `le congé ${staffLeavePeriodLabel(request)}`
}

/** Withdraws a waiting request, or an accepted congé not yet begun. Names what — and whose — it withdraws. */
export function CancelLeaveConfirm({
  request,
  open,
  onOpenChange,
  onChanged,
  showName = false,
}: DecisionDialogProps & { showName?: boolean }) {
  const [cancelling, setCancelling] = useState(false)

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
      toast.success("Congé annulé", {
        description: showName ? `${request.userName} — ${staffLeavePeriodLabel(request)}` : staffLeavePeriodLabel(request),
      })
      onOpenChange(false)
      onChanged?.()
    } catch (err) {
      showErrorToast(err, "Le congé n'a pas pu être annulé.")
      onChanged?.()
    } finally {
      setCancelling(false)
    }
  }

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            {request ? `Annuler ${leaveCancelSubject(request, showName)} ?` : "Annuler le congé ?"}
          </AlertDialogTitle>
          <AlertDialogDescription>
            {request?.status === "Approved" ? "Ce congé était accepté." : "La demande est retirée."}
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
            {cancelling ? "Annulation…" : "Annuler le congé"}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
