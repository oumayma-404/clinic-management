"use client"

import { useCallback, useEffect, useMemo, useState } from "react"
import { toast } from "sonner"
import type { SelectedAct, PresetPlanAct } from "@/components/appointment-acts-picker"
import {
  agreedCostOf, followedProtocolActs, presetToSelectedAct, resolvePlannedProtocols,
} from "@/components/appointment-acts-picker"
import { showErrorToast } from "@/lib/errors"
import { formatDT } from "@/lib/format"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { ProcedureTypeDto, TreatmentPlanDto } from "@/lib/api/types"
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

  /**
   * Re-price one treatment act — the act's total for the whole treatment, not a price for one séance.
   *
   * <p><b>Here rather than in each dialog</b> because both booking surfaces offer the edit and this hook is
   * already the one that holds the plans: two implementations would be two answers to « what does changing a
   * treatment's price do to its échéancier ».</p>
   *
   * <p>⚠️ It sends only the act's own line. The server re-spreads the schedule when a total changes with none
   * supplied, so a dentist correcting a figure from the agenda — where no échéancier is on screen — is never
   * asked for one. Resolves `false` on a refusal so the field keeps what was typed.</p>
   */
  saveActTotal: (treatmentPlanItemId: string, total: number) => Promise<boolean>
}

const EMPTY: Omit<PatientPlanActs, "register" | "saveActTotal"> = {
  plans: [], planActs: [], planIdByItem: {}, loading: false,
}

/**
 * A patient's outstanding devis acts, for a booking dialog.
 *
 * <p><b>One loader for both dialogs.</b> The edit dialog had this as an inline effect and the create dialog had
 * nothing at all — which is why booking from the agenda could not see a devis, the gap this hook closes. A second
 * copy of the derivation is the shape of defect this repository produces most: `schedulablePlanItems` already
 * encodes « a Done act and a Cancelled/Completed plan contribute nothing » — a DRAFT does contribute, since
 * « Suivre ce traitement » creates one — and a hand-rolled filter beside it drifts silently.</p>
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

  const saveActTotal = useCallback(async (treatmentPlanItemId: string, total: number) => {
    /*
     * ⚠️ **`plans` is a SNAPSHOT, and « the act is not in it » used to mean « do nothing, say nothing ».**
     * The read happens once, when the patient is picked. A devis created after that — the ordinary case, since
     * two surfaces mint one from inside the booking dialog and a dentist may have written it a minute earlier
     * in another tab — is simply absent, so a re-priced act hit `return false` and **no request was ever
     * sent**. Measured on the live database: « Couronne sur implant » 2026-0203 typed from 500 to 700, zero
     * `AmendTreatmentPlanCommand` in the API log for the whole day and `RevisionNumber` still 0. The field kept
     * showing 700 because the draft is only cleared on success, so it looked saved until the dialog was
     * reopened. Reported as « I changed the total and it did not persist », twice.
     *
     * So: re-read before giving up, and if it is still not there, SAY SO. A money edit that silently does
     * nothing is the worst outcome available here — worse than a refusal, because nothing invites a retry.
     */
    let plan = plans.find((p) => p.items.some((i) => i.id === treatmentPlanItemId))
    if (!plan && patientId) {
      try {
        const fresh = await treatmentPlansApi.list({ patientId })
        setPlans(fresh)
        plan = fresh.find((p) => p.items.some((i) => i.id === treatmentPlanItemId))
      } catch {
        /* fall through to the refusal below — the toast names it */
      }
    }
    const item = plan?.items.find((i) => i.id === treatmentPlanItemId)
    if (!plan || !item) {
      toast.error("Le devis de cet acte n'a pas pu être relu — le total n'a pas été modifié. Rechargez la page.")
      return false
    }
    try {
      const saved = await treatmentPlansApi.amend(plan.id, {
        updateItems: [
          {
            id: item.id,
            designationFr: item.designationFr,
            plannedCost: total,
            procedureTypeId: item.procedureTypeId ?? undefined,
            toothNumbers: item.toothNumbers,
          },
        ],
        version: plan.version,
      })
      register(saved)
      toast.success(`Total mis à jour — ${formatDT(total)}`)
      return true
    } catch (err) {
      showErrorToast(err, "Le total n'a pas pu être modifié.")
      return false
    }
  }, [plans, register, patientId])

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
    if (plans.length === 0) return { ...EMPTY, loading, register, saveActTotal }
    const planActs: PresetPlanAct[] = []
    const planIdByItem: Record<string, string> = {}
    for (const plan of plans) {
      for (const item of schedulablePlanItems(plan)) {
        planActs.push(planItemToPreset(plan, item, (i) => i.procedureTypeId ?? undefined))
        planIdByItem[item.id] = plan.id
      }
    }
    return { plans, planActs, planIdByItem, loading, register, saveActTotal }
  }, [plans, loading, register, saveActTotal])
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

/**
 * What {@link materialisePlannedProtocols} produced: the acts to send, and the plans they were just given.
 */
export interface MaterialisedProtocols {
  /** The act rows, with every followed act rewritten as a devis act — exactly the shape « Actes du devis » makes. */
  acts: SelectedAct[]
  /** The plan each freshly-created act belongs to, to be merged into `planIdByItem` before resolving. */
  planIdByItem: Record<string, string>
  /** The plans created, so the dialog can `register` them and the caller can report what it did. */
  plans: TreatmentPlanDto[]
}

/**
 * Turn every act the dentist left split into a real treatment, **at save time**.
 *
 * <p>This is the half that makes « split by default » safe. The button it replaces created the plan the moment
 * it was pressed, which had three consequences and only the first was visible: it needed a patient id, so it
 * was hidden on a booking for a « Nouveau patient » and before a patient had been chosen; abandoning the
 * dialog afterwards left an un-numbered treatment behind with no appointment; and the decision could not be
 * taken back without deleting a server aggregate. Deferring it to the save costs one round trip and removes
 * all three.</p>
 *
 * <p>⚠️ <b>`created` is the caller's, and it MUST outlive one attempt.</b> Both dialogs re-run their save from
 * the top on every confirmation the server asks for — slot taken, out of hours, past time — so a plan created
 * on the first attempt must be reused on the second. This is `createdPatientIdRef`'s reason, one object over:
 * without it, one « créer quand même » leaves two identical treatments on the patient. Keyed on the catalogue
 * act, which the picker already refuses to list twice.</p>
 *
 * <p>⚠️ The rewritten row goes through `planItemToPreset` + `presetToSelectedAct`, never a hand-built object:
 * a row attached here and a row attached from « Actes du devis » must be the same thing, down to the
 * `billedOnPlan` block that locks the price and the preselected first step.</p>
 */
export async function materialisePlannedProtocols(
  acts: readonly SelectedAct[],
  procedureTypes: ProcedureTypeDto[],
  patientId: string,
  created: Map<string, TreatmentPlanDto>,
): Promise<MaterialisedProtocols> {
  const resolved = resolvePlannedProtocols(acts, procedureTypes)
  const followed = followedProtocolActs(resolved)
  if (followed.length === 0) return { acts: resolved, planIdByItem: {}, plans: [] }

  const next = [...resolved]
  const planIdByItem: Record<string, string> = {}
  const plans: TreatmentPlanDto[] = []

  for (const { act, index, steps } of followed) {
    if (!act.procedureTypeId) continue
    let plan = created.get(act.procedureTypeId)
    if (!plan) {
      plan = await treatmentPlansApi.startTreatment({
        patientId,
        procedureTypeId: act.procedureTypeId,
        // The figure typed in the row is the treatment's TOTAL, not this séance's — the field says so.
        agreedTotal: agreedCostOf(act),
        toothNumbers: [],
        /*
         * ⚠️ Sent even when untouched, and that is deliberate: this list is what the dentist saw and
         * approved. Omitting it would let the server re-read the catalogue, so a protocol edited in
         * « Types de procédures » between the dialog opening and the save would silently replace it.
         */
        steps: steps.map((s) => ({
          label: s.label.trim(),
          estimatedDurationMinutes: s.durationMinutes,
          minDaysAfterPrevious: s.minDaysAfterPrevious ?? null,
        })),
      })
      created.set(act.procedureTypeId, plan)
    }
    // ⚠️ The same reader `planIdByItem` is built from, and never `plan.items[0]`. « Suivre ce traitement »
    // does create a one-act plan, so the two agree here today — but only one of them is the gate that decides
    // whether the act is registrable, and the moment they disagree the booking is refused with « Le plan de
    // traitement est requis pour lier l'acte. » That is not hypothetical: it is precisely what `items[0]` did
    // to the continuation door, where a priced « travail restant » makes the first act Done on creation.
    const item = schedulablePlanItems(plan)[0]
    if (!item) continue
    plans.push(plan)
    planIdByItem[item.id] = plan.id
    next[index] = {
      ...presetToSelectedAct(planItemToPreset(plan, item, (i) => i.procedureTypeId ?? undefined), procedureTypes),
      // Decided and done — the act carries a `treatmentPlanItemId` now, and the card stops offering the split.
      plannedProtocol: null,
    }
  }

  return { acts: next, planIdByItem, plans }
}
