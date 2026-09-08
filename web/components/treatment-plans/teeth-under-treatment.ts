import type { TreatmentPlanDto } from "@/lib/api/types"
import { activeItems, isPlanLive } from "./plan-next-action"

/**
 * A treatment that is under way on one tooth — what the odontogram had no way to say.
 *
 * <p>⚠️ <b>The chart has two readings and neither of them is « work in progress here », which is the state a
 * multi-séance act spends most of its life in.</b> « Diagnostics » keeps the « à traiter » that asked for the
 * work — correct, since it is not finished — and « Actes réalisés » colours the tooth from the FIRST séance,
 * because it is built from the fiches' own acts and a step-1 fiche names its teeth. So a couronne two séances
 * into three read as both « still to do » and « done », and nothing anywhere said how far along it was.</p>
 *
 * <p>⚠️ <b>It says what HAS BEEN DONE, and it deliberately says nothing about what comes next.</b> It shipped
 * the other way round — « séance 2 sur 3 · essai de l'armature à planifier » on a treatment whose only
 * delivered séance was the préparation — and a dentist read it as a claim that séance 2 had happened. Two
 * things were wrong at once and each is worth naming. The rank was <code>stepsDone + 1</code>, i.e. the rank of
 * the step still to come, printed with no word saying so; and the odontogram is a chart of the <i>mouth</i>, so
 * a planning instruction on it is a fact about the diary in the one place a reader is looking for a fact about
 * the teeth. « Prochaine étape » already has three homes that say so in as many words — the devis row's
 * `PlanStepStrip`, `patient-plans-strip` and « Traitements en cours » — and this is not a fourth.</p>
 *
 * <p>⚠️ The same defect had already been found and fixed one screen over: « Traitements en cours » prints
 * « étape 2 / 2 <b>à faire</b> » because « three reviewers read « étape 2 / 2 » cold on a treatment with one of
 * two séances done and all three took it for finished ». That fix never reached here. Held now by
 * `check:responsive`'s `step-counter-says-done-or-to-do`.</p>
 */
export interface ToothTreatment {
  planId: string
  /** The act, as the devis names it — « Couronne / bridge (par élément) ». */
  designationFr: string
  /** How many of the act's séances are carried out. A count, never a rank — see {@link toothTreatmentSummary}. */
  stepsDone: number
  stepsTotal: number
  /**
   * The last séance actually carried out, in the protocol's own order — null while none has been.
   *
   * <p>⚠️ By `sequenceNumber` and not by `doneDate`, matching what the aggregate does when a fiche closes two
   * steps at once (« the act's stored DoneDate/link end up on the LAST of them »). And it is the last <b>done</b>
   * one, never {@link nextStepOf}'s: naming a step the patient has not had is the defect above.</p>
   */
  lastDoneStepLabel: string | null
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
      const doneSteps = steps.filter((s) => s.doneDate)
      if (doneSteps.length >= steps.length) continue

      const teeth = item.toothNumbers.length > 0 ? item.toothNumbers : (item.treatedToothNumbers ?? [])
      if (teeth.length === 0) continue

      const lastDone = [...doneSteps].sort((a, b) => a.sequenceNumber - b.sequenceNumber).at(-1)
      const entry: ToothTreatment = {
        planId: plan.id,
        designationFr: item.designationFr,
        stepsDone: doneSteps.length,
        stepsTotal: steps.length,
        lastDoneStepLabel: lastDone?.label ?? null,
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

/**
 * « 1 étape sur 3 faite : Préparation » — one line, and every word of it is about work already carried out.
 *
 * <p>⚠️ <b>A count, phrased as a count.</b> « étape 1 sur 3 » is a rank and a rank invites the reader to ask
 * « done, or next? »; « 1 étape sur 3 faite » cannot be read two ways, and it stays true even when the séances
 * are carried out out of order — which they legitimately are, since a dentist may book the scellement before
 * the essayage (see `DentalRecordLinker`). The step's own name follows because « 1 sur 3 » does not say what
 * the patient actually had done.</p>
 */
export function toothTreatmentSummary(t: ToothTreatment): string {
  if (t.stepsDone === 0) return `aucune étape faite sur ${t.stepsTotal}`
  const plural = t.stepsDone > 1
  const count = `${t.stepsDone} étape${plural ? "s" : ""} sur ${t.stepsTotal} faite${plural ? "s" : ""}`
  return t.lastDoneStepLabel ? `${count} : ${t.lastDoneStepLabel}` : count
}
