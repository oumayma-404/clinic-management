"use client"

import { useCallback, useRef, useState, type ReactNode } from "react"
import type { PlanStepOption } from "@/components/appointment-acts-picker"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { TreatmentPlanDto } from "@/lib/api/types"
import { useSession } from "@/lib/auth/session"
import { showErrorToast } from "@/lib/errors"
import { isAdminOrDoctor } from "@/lib/nav"
import { PlanItemStepsDialog } from "./plan-item-steps-dialog"
import { isPlanLive, planItemStepOptions } from "./plan-next-action"

export interface BookingPlanSteps {
  /** For the picker; undefined for a role the steps endpoint refuses (`AdminOrDoctor`), so nothing is offered. */
  onEditPlanSteps?: (treatmentPlanItemId: string) => Promise<PlanStepOption[] | null>
  /** False when the act's treatment is unknown or not live — the button is withheld rather than refused. */
  canEditPlanSteps: (treatmentPlanItemId: string) => boolean
  /** The séances window, to mount inside the booking dialog. */
  stepsDialog: ReactNode
}

/**
 * « + Ajouter une séance au traitement » from a booking dialog: the treatment page's own séances window, opened
 * over the booking, with the act's fresh séances handed back to the row. One owner for both booking dialogs.
 *
 * <p>⚠️ <b>It resolves on CLOSE, never on save</b>: the window also detaches a séance and reloads on a 409 without
 * closing, and each of those changes the séances too — so the last read wins, and a cancel with nothing written
 * resolves null. The close waits for a read still in flight, because the window calls `onSaved` and closes in the
 * same tick.</p>
 *
 * <p>⚠️ Two dirty guards are open at once here; `useDirtyGuard` lets only the top one (this window) count a
 * keystroke or answer the back gesture, so a back closes this window alone and typing here never dirties the
 * booking.</p>
 *
 * @param fallbackPlanId the devis a workspace « Planifier » opened the dialog for — its plan read is skipped then.
 */
export function useBookingPlanSteps(
  plans: readonly TreatmentPlanDto[],
  register: (plan: TreatmentPlanDto) => void,
  fallbackPlanId?: string | null,
): BookingPlanSteps {
  const { user } = useSession()
  const [target, setTarget] = useState<{ plan: TreatmentPlanDto; itemId: string } | null>(null)
  const targetRef = useRef(target)
  targetRef.current = target
  const resolveRef = useRef<((steps: PlanStepOption[] | null) => void) | null>(null)
  const latestRef = useRef<PlanStepOption[] | null>(null)
  const pendingRef = useRef<Promise<void> | null>(null)

  const planOf = useCallback(
    (itemId: string) => plans.find((p) => p.items.some((i) => i.id === itemId)),
    [plans],
  )

  const canEditPlanSteps = useCallback(
    (itemId: string) => {
      const plan = planOf(itemId)
      return plan ? isPlanLive(plan.status) : !!fallbackPlanId
    },
    [planOf, fallbackPlanId],
  )

  const onEditPlanSteps = useCallback(
    async (itemId: string) => {
      const planId = planOf(itemId)?.id ?? fallbackPlanId
      if (!planId) return null
      let plan: TreatmentPlanDto
      try {
        // Fresh, so the window saves on the current version rather than on the dialog's snapshot.
        plan = await treatmentPlansApi.get(planId)
      } catch (err) {
        showErrorToast(err, "Traitement non chargé.")
        return null
      }
      if (!plan.items.some((i) => i.id === itemId)) return null
      register(plan)
      latestRef.current = null
      pendingRef.current = null
      return new Promise<PlanStepOption[] | null>((resolve) => {
        resolveRef.current = resolve
        setTarget({ plan, itemId })
      })
    },
    [planOf, fallbackPlanId, register],
  )

  const refresh = useCallback(async () => {
    const current = targetRef.current
    if (!current) return
    try {
      const fresh = await treatmentPlansApi.get(current.plan.id)
      register(fresh)
      const item = fresh.items.find((i) => i.id === current.itemId)
      latestRef.current = item ? planItemStepOptions(item) ?? [] : null
      // Only while still open: a detach or « Recharger » re-seeds the window from this copy.
      if (targetRef.current?.itemId === current.itemId) setTarget({ plan: fresh, itemId: current.itemId })
    } catch (err) {
      showErrorToast(err, "Séances enregistrées. Rouvrez le rendez-vous pour les voir.")
    }
  }, [register])

  const close = useCallback(async () => {
    setTarget(null)
    const resolve = resolveRef.current
    resolveRef.current = null
    await pendingRef.current
    resolve?.(latestRef.current)
  }, [])

  const item = target?.plan.items.find((i) => i.id === target.itemId) ?? null
  const stepsDialog = target ? (
    <PlanItemStepsDialog
      plan={target.plan}
      item={item}
      open
      addOnOpen
      onOpenChange={(open) => {
        if (!open) void close()
      }}
      onSaved={() => {
        pendingRef.current = refresh()
      }}
    />
  ) : null

  return {
    onEditPlanSteps: isAdminOrDoctor(user?.role) ? onEditPlanSteps : undefined,
    canEditPlanSteps,
    stepsDialog,
  }
}
