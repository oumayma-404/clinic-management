"use client"

import { useCallback, useEffect, useMemo, useState } from "react"
import type { SelectedAct, PresetPlanAct } from "@/components/appointment-acts-picker"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { TreatmentPlanDto } from "@/lib/api/types"
import { planItemToPreset, schedulablePlanItems } from "./plan-next-action"

export interface PatientPlanActs {
  /** Every live devis of this patient, as read. Empty while loading, on a failure, and for a patient with none. */
  plans: TreatmentPlanDto[]
  /** The acts a séance can still be booked for, ready for the picker's « Actes du devis » group. */
  planActs: PresetPlanAct[]
  /** Which devis each of those acts belongs to — see {@link resolveAttachedPlanId}. */
  planIdByItem: Record<string, string>
  loading: boolean
  /**
   * Fold a devis **this dialog just created** into the derived sets, without a re-read.
   *
   * ⚠️ Load-bearing, not a convenience. Two surfaces mint a plan from inside the booking dialog — « Créer le
   * devis et planifier la 1re séance » and « C'est la suite d'une séance précédente ? » — and both then put its
   * act on the séance. That act carries a `treatmentPlanItemId`, so the save MUST send the appointment's own
   * `treatmentPlanId` — see {@link resolveAttachedPlanId} — or the server refuses the booking outright with
   * « Le plan de traitement est requis pour lier l'acte. » The plan is seconds old and cannot be in the read
   * this hook did when the patient was picked, so it is handed in instead.
   */
  register: (plan: TreatmentPlanDto) => void
}

const EMPTY: Omit<PatientPlanActs, "register"> = { plans: [], planActs: [], planIdByItem: {}, loading: false }

/**
 * A patient's outstanding devis acts, for a booking dialog.
 *
 * <p><b>One loader for both dialogs.</b> The edit dialog had this as an inline effect and the create dialog had
 * nothing at all — which is why booking from the agenda could not see a devis, the gap this hook closes. A second
 * copy of the derivation is the shape of defect this repository produces most: `schedulablePlanItems` already
 * encodes « a Done act and a Draft plan contribute nothing », and a hand-rolled filter beside it drifts silently.</p>
 *
 * <p>⚠️ <b>A failure is swallowed to an empty set, deliberately.</b> The devis shortcut is an accelerator: taking
 * the whole booking dialog down because one extra read failed would be a poor trade, and the picker's catalogue
 * is unaffected. It is the one place in this product where a failed read renders as « nothing » on purpose —
 * because « nothing » here withholds a shortcut rather than asserting a fact about the patient's record.</p>
 *
 * <p>⚠️ Loads only for a <b>real patient</b>: a « créneau occupé » has nobody to have a devis, and a not-yet-saved
 * new patient has no id to ask about.</p>
 */
export function usePatientPlanActs(
  patientId: string | null | undefined,
  /** False while the dialog is shut, or when the caller already holds the acts (the devis workspace's own « Planifier »). */
  enabled = true,
): PatientPlanActs {
  const [plans, setPlans] = useState<TreatmentPlanDto[]>([])
  const [loading, setLoading] = useState(false)

  // Replaces by id rather than appending, so registering the same plan twice (a retried press) cannot make one
  // devis contribute its acts to the picker's group twice.
  const register = useCallback((plan: TreatmentPlanDto) => {
    setPlans((prev) => [plan, ...prev.filter((p) => p.id !== plan.id)])
  }, [])

  useEffect(() => {
    if (!enabled || !patientId) {
      setPlans([])
      setLoading(false)
      return
    }
    let cancelled = false
    setLoading(true)
    void (async () => {
      try {
        const result = await treatmentPlansApi.list({ patientId })
        if (!cancelled) setPlans(result)
      } catch {
        if (!cancelled) setPlans([])
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [enabled, patientId])

  return useMemo(() => {
    if (plans.length === 0) return { ...EMPTY, loading, register }
    const planActs: PresetPlanAct[] = []
    const planIdByItem: Record<string, string> = {}
    for (const plan of plans) {
      for (const item of schedulablePlanItems(plan)) {
        planActs.push(planItemToPreset(plan, item, (i) => i.procedureTypeId ?? undefined))
        planIdByItem[item.id] = plan.id
      }
    }
    return { plans, planActs, planIdByItem, loading, register }
  }, [plans, loading, register])
}

/**
 * The devis a séance's acts belong to, as the appointment payload's single `treatmentPlanId`.
 *
 * <p>⚠️ <b>The server REQUIRES this the moment any act carries a plan link</b> — `AppointmentPlanLink.ValidateManyAsync`
 * answers « Le plan de traitement est requis pour lier l'acte. » without it — and an appointment records exactly
 * one. So the id has to be derived from whatever the user actually attached, and it cannot be the id the dialog
 * was opened with: a devis act picked from inside the dialog belongs to a devis the caller never named.</p>
 *
 * <p>⚠️ <b>Two devis in one séance is refused here, in French</b>, rather than reaching the server as a validation
 * error on a save the user thought had worked. It is a real possibility — a patient may have several live devis and
 * the picker offers all of their acts in one group.</p>
 *
 * <p>Returns `{ planId: undefined }` for a séance with no devis act at all, which is the ordinary case: the
 * payload then omits the key and a visit that never had a devis link keeps not having one.</p>
 */
export function resolveAttachedPlanId(
  selectedActs: readonly SelectedAct[],
  planIdByItem: Record<string, string>,
): { planId?: string; error?: string } {
  const attached = Array.from(
    new Set(
      selectedActs
        .map((a) => (a.treatmentPlanItemId ? planIdByItem[a.treatmentPlanItemId] : null))
        .filter((id): id is string => !!id),
    ),
  )

  if (attached.length > 1) {
    return {
      error:
        "Les actes de ce rendez-vous appartiennent à deux devis différents. Une séance ne peut être rattachée qu'à un seul devis.",
    }
  }
  return { planId: attached[0] }
}
