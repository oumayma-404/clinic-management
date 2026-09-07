import type { TreatmentPlanDto } from "@/lib/api/types"
import { activeItems, isPlanLive, nextStepOf } from "./plan-next-action"

/**
 * A treatment that is under way on one tooth — what the odontogram had no way to say.
 *
 * <p>⚠️ <b>The chart has two readings and neither of them is « work in progress here », which is the state a
 * multi-séance act spends most of its life in.</b> « Diagnostics » keeps the « à traiter » that asked for the
 * work — correct, since it is not finished — and « Actes réalisés » colours the tooth from the FIRST séance,
 * because it is built from the fiches' own acts and a step-1 fiche names its teeth. So a couronne two séances
 * into three read as both « still to do » and « done », and nothing anywhere said « séance 2 sur 3, prochaine
 * le 12 octobre ». Verified on two patients: the words « en cours », « séance N » and « étape N » appear
 * nowhere on either tab.</p>
 */
export interface ToothTreatment {
  planId: string
  /** The act, as the devis names it — « Couronne / bridge (par élément) ». */
  designationFr: string
  stepsDone: number
  stepsTotal: number
  /** The séance that comes next, or null when every step is already carried out. */
  nextStepLabel: string | null
  /** When that séance is booked, if it is. ISO, straight from the plan. */
  nextAppointmentAt: string | null
}

/**
 * Which teeth have a treatment under way, from the patient's own plans.
 *
 * <p>Derived rather than served: every field is already on `TreatmentPlanDto`, which the patient page holds for
 * the treatment band beside this chart — so a new endpoint would be a second answer to a question the page can
 * already answer, and the two would drift.</p>
 *
 * <p>⚠️ <b>Only an act with STEPS counts.</b> An ordinary one-séance act is planned and then done; there is no
 * interval during which « en cours » is true of it, and marking every unstarted devis line would put a ring on
 * half the mouth and make the mark mean nothing. This is the multi-séance state and nothing else.</p>
 *
 * <p>⚠️ <b>The devis LINE wins when it names teeth; the treated ones are only a fallback — and the union this
 * started as was measurably wrong.</b> `treatedToothNumbers` is derived from the fiches an act's séances
 * produced, and a fiche records the teeth of the <i>whole séance</i>, not of one act inside it. Measured on the
 * live database: an « Extraction simple » quoted on 13 and 43 had its first séance recorded on a fiche naming
 * <b>13, 27, 36, 37, 43</b> — the other three belonging to work done the same afternoon — so the union put a
 * « traitement en cours » ring on three teeth with no treatment on them at all. A chart that over-claims is the
 * defect this whole mark exists to remove.</p>
 *
 * <p>The fallback still matters and is why this is not simply `toothNumbers`: a line very often carries none
 * (« Implant dentaire — acte général »), and marking nothing there would miss the longest treatments in the
 * product. ⚠️ Note the priority is the <b>opposite</b> of `openPlanItems`', deliberately: that one seeds a
 * fiche's chart, where the teeth actually worked on are the better proposal, while this answers « which teeth
 * is this treatment ON », which is what was quoted.</p>
 */
export function teethUnderTreatment(plans: TreatmentPlanDto[]): Map<number, ToothTreatment[]> {
  const byTooth = new Map<number, ToothTreatment[]>()

  for (const plan of plans) {
    if (!isPlanLive(plan.status)) continue
    // `activeItems` drops the withdrawn ones — a stopped treatment is not under way, and marking its teeth
    // would report work nobody is coming back for.
    for (const item of activeItems(plan)) {
      const steps = item.steps ?? []
      if (steps.length === 0) continue
      const done = steps.filter((s) => s.doneDate).length
      if (done >= steps.length) continue

      const teeth = item.toothNumbers.length > 0 ? item.toothNumbers : (item.treatedToothNumbers ?? [])
      if (teeth.length === 0) continue

      const next = nextStepOf(item)
      const entry: ToothTreatment = {
        planId: plan.id,
        designationFr: item.designationFr,
        stepsDone: done,
        stepsTotal: steps.length,
        // ⚠️ The STEP's own booking, never the act's `scheduledAt` and never the plan's `nextAppointmentAt`:
        // an act with séance 2 booked and séances 3–6 not is « planifié » as an act, and quoting that date for
        // the tooth would announce a visit that covers a different séance. Null here reads as « à planifier »,
        // which is the honest answer.
        nextStepLabel: next?.label ?? null,
        nextAppointmentAt: next?.scheduledAt ?? null,
      }
      for (const tooth of teeth) {
        const list = byTooth.get(tooth)
        if (list) list.push(entry)
        else byTooth.set(tooth, [entry])
      }
    }
  }

  return byTooth
}

/** « séance 2 sur 3 · prochaine le 12 oct. » — one line, used by both charts and by the tooltip. */
export function toothTreatmentSummary(t: ToothTreatment, formatDate: (iso: string) => string): string {
  const seance = `séance ${Math.min(t.stepsDone + 1, t.stepsTotal)} sur ${t.stepsTotal}`
  if (t.nextAppointmentAt) return `${seance} · prochaine le ${formatDate(t.nextAppointmentAt)}`
  if (t.nextStepLabel) return `${seance} · ${t.nextStepLabel} à planifier`
  return seance
}
