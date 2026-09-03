import type { PresetPlanAct } from "@/components/appointment-acts-picker"
import type { TreatmentPlanDto, TreatmentPlanItemDto } from "@/lib/api/types"

/** Derived workflow état of one planned act. */
export type PlanItemState = "to-schedule" | "scheduled" | "to-record" | "done"

/**
 * État of an act, derived from the scheduling read-back the API supplies.
 *
 * The backend decides *which* appointment speaks for an act and excludes cancelled / no-show ones, so a
 * null `scheduledAt` genuinely means "nothing booked" and the act can be scheduled again. All this adds is
 * the upcoming-vs-already-passed split, deliberately evaluated client-side so the badge flips from
 * « Planifié » to « À enregistrer » as the visit time passes, without waiting for a refetch.
 */
export function planItemState(item: TreatmentPlanItemDto, now: Date = new Date()): PlanItemState {
  if (item.status === "Done") return "done"

  // ⚠️ A stepped act answers for its NEXT STEP, not for the act as a whole — and that is the decision this
  // feature turns on. A bridge with two of three séances done carries an appointment that already happened, so
  // reading the ACT's `scheduledAt` would report « À enregistrer » forever while the scellement sits unbooked.
  // Keying on the next step makes the badge say what to do about the part that is still open.
  //
  // The four états are unchanged, deliberately. A fifth « En cours » badge was the alternative: it costs a
  // label and a tone, and it says less — « À planifier » names an action, « En cours » names a condition. The
  // strip beside the badge is what carries how far along the act is.
  const next = nextStepOf(item)
  const scheduledAt = next ? next.scheduledAt : item.scheduledAt
  if (!scheduledAt) return "to-schedule"
  return new Date(scheduledAt).getTime() > now.getTime() ? "scheduled" : "to-record"
}

/**
 * The step this act is waiting on, or null when it has none (or none left).
 *
 * <p>Prefers the server's own `nextStepId` and falls back to the first un-done step by rank, so a response
 * predating that field still resolves. Returns null for an act with no steps, which is what keeps every
 * step-less act on exactly the behaviour it had.</p>
 */
export function nextStepOf(item: TreatmentPlanItemDto) {
  const steps = item.steps
  if (!steps || steps.length === 0) return null
  if (item.nextStepId) return steps.find((s) => s.id === item.nextStepId) ?? null
  return steps.filter((s) => !s.doneDate).sort((a, b) => a.sequenceNumber - b.sequenceNumber)[0] ?? null
}

/** True once a non-cancelled invoice already bills this devis (hides "Facturer", blocks amending). */
export function isPlanBilled(plan: TreatmentPlanDto): boolean {
  return plan.linkedInvoiceId != null
}

/** The one thing a dentist should do next on this plan. */
export type PlanNextAction =
  | { kind: "accept" }
  | { kind: "record"; itemId: string }
  | { kind: "schedule"; itemId: string }
  | { kind: "collect" }
  | { kind: "open" }

/**
 * Ordered by urgency: a devis waiting for acceptance blocks everything; a visit that already happened
 * without a fiche is the most overdue clinical action; then booking what is left; then the money.
 */
export function planNextAction(plan: TreatmentPlanDto, now: Date = new Date()): PlanNextAction {
  if (plan.status === "Draft") return { kind: "accept" }
  if (plan.status === "Cancelled") return { kind: "open" }

  const toRecord = plan.items.find((i) => planItemState(i, now) === "to-record")
  if (toRecord) return { kind: "record", itemId: toRecord.id }

  const toSchedule = plan.items.find((i) => planItemState(i, now) === "to-schedule")
  if (toSchedule) return { kind: "schedule", itemId: toSchedule.id }

  if (plan.outstanding > 0) return { kind: "collect" }
  return { kind: "open" }
}

/**
 * The headline for a patient-level summary: the next action, phrased with its **count**.
 *
 * <p>« 1 acte à enregistrer » / « 4 actes à planifier » rather than a bare button label, because the count is what
 * makes the line worth reading — it is the difference between "there is work" and "there is one thing". Derived,
 * never stored, so it re-reads itself as visit times pass (see {@link planItemState}).</p>
 *
 * <p>Kept beside {@link planNextAction} on purpose: the two must agree about what comes next, and the urgency
 * ordering below is that function's, not a second opinion.</p>
 */
export function planHeadline(plan: TreatmentPlanDto, now: Date = new Date()): string {
  if (plan.status === "Draft") return "À accepter";
  if (plan.status === "Cancelled") return "Plan annulé";

  const toRecord = plan.items.filter((i) => planItemState(i, now) === "to-record").length;
  if (toRecord > 0) return `${toRecord} acte${toRecord > 1 ? "s" : ""} à enregistrer`;

  const toSchedule = plan.items.filter((i) => planItemState(i, now) === "to-schedule").length;
  if (toSchedule > 0) return `${toSchedule} acte${toSchedule > 1 ? "s" : ""} à planifier`;

  if (plan.outstanding > 0) return "Reste à encaisser";

  const scheduled = plan.items.filter((i) => planItemState(i, now) === "scheduled").length;
  if (scheduled > 0) return `${scheduled} séance${scheduled > 1 ? "s" : ""} à venir`;

  return "Rien à faire";
}

/** One status group of a patient's plans, for the summary chips. */
export interface PlanStatusCount {
  status: string;
  count: number;
}

/**
 * The patient's plans grouped by statut, excluding the one already shown in full.
 *
 * <p>This exists because the card it replaces could not express the question the user actually asked. Its
 * « +N autres » counted `p.status !== "Cancelled" && p.status !== "Completed"` — so a patient with three finished
 * plans and nothing running showed **no trace of them at all**. Finished treatment is information; it just is not
 * *actionable* information, which is why it belongs in a chip rather than the headline.</p>
 *
 * <p>Ordered by how much attention the status deserves rather than alphabetically or by count.</p>
 */
export function planStatusCounts(plans: TreatmentPlanDto[], excludeId?: string): PlanStatusCount[] {
  const order = ["Draft", "Accepted", "InProgress", "Completed", "Cancelled"];
  const counts = new Map<string, number>();

  for (const plan of plans) {
    if (plan.id === excludeId) continue;
    counts.set(plan.status, (counts.get(plan.status) ?? 0) + 1);
  }

  return order
    .filter((status) => counts.has(status))
    .map((status) => ({ status, count: counts.get(status)! }));
}

/**
 * The plan a patient-level surface should lead with: the most recently accepted active plan, else the most
 * recently created draft, else nothing (render no card at all rather than an empty box).
 */
export function leadPlan(plans: TreatmentPlanDto[]): TreatmentPlanDto | null {
  const active = plans
    .filter((p) => p.status === "Accepted" || p.status === "InProgress")
    .sort((a, b) => byDateDesc(a.acceptedDate ?? a.createdAt, b.acceptedDate ?? b.createdAt))
  if (active.length > 0) return active[0]

  const drafts = plans
    .filter((p) => p.status === "Draft")
    .sort((a, b) => byDateDesc(a.createdAt, b.createdAt))
  return drafts[0] ?? null
}

function byDateDesc(a: string, b: string): number {
  return new Date(b).getTime() - new Date(a).getTime()
}

/**
 * A devis act as a bookable preset. **The one builder**, used by the devis workspace's « Planifier » and by the
 * edit dialog's « Actes du devis » group.
 *
 * <p>⚠️ Only the steps still to carry out are offered: a réalisé step has nothing to book, and offering it would
 * invite the one thing this feature refuses — a second fiche against a step already evidenced by one.</p>
 *
 * <p>⚠️ `billedOnPlan` carries **this act's** fee and the **devis'** outstanding, two different scopes. Passing
 * the devis total as the act's fee is a mistake this pair has already made once.</p>
 */
export function planItemToPreset(
  plan: TreatmentPlanDto,
  item: TreatmentPlanItemDto,
  resolveProcedureTypeId: (item: TreatmentPlanItemDto) => string | undefined,
): PresetPlanAct {
  return {
    planItemId: item.id,
    procedureTypeId: resolveProcedureTypeId(item),
    label:
      item.toothNumbers.length > 0
        ? `${item.designationFr} (dents ${item.toothNumbers.join(", ")})`
        : item.designationFr,
    plannedCost: item.plannedCost,
    steps: item.steps
      ?.filter((step) => !step.doneDate)
      .map((step) => ({
        id: step.id,
        label: step.label,
        estimatedDurationMinutes: step.estimatedDurationMinutes,
      })),
    preselectedStepId: item.nextStepId ?? null,
    billedOnPlan: {
      planNumber: plan.number,
      actCost: item.plannedCost,
      outstanding: plan.outstanding,
    },
  }
}

/**
 * The acts of one plan a séance can still be booked for — planned or under way, on a live devis.
 *
 * <p>⚠️ A `Done` act is excluded and a Draft/Cancelled plan contributes nothing: booking either would produce a
 * visit for work that is finished or for a quote nobody accepted.</p>
 */
export function schedulablePlanItems(plan: TreatmentPlanDto): TreatmentPlanItemDto[] {
  if (plan.status !== "Accepted" && plan.status !== "InProgress") return []
  return plan.items.filter((item) => item.status !== "Done")
}

/**
 * How far through the plan the work actually is, counting an act's **steps**.
 *
 * <p>⚠️ `itemsDone / itemsTotal` counts whole acts only, so a bridge two thirds carried out contributes
 * **nothing**: the devis header read « 0 / 2 » and an empty progress bar on a patient who had already sat
 * through two séances. Each act contributes the fraction of its steps that are done — a step-less act is all or
 * nothing, exactly as before — so the bar moves when the work moves.</p>
 *
 * <p>`actsDone` stays the honest whole-act count: a bridge is not « réalisé » until it is scellé, and rounding
 * that up would be a claim about a patient's mouth. `actsInProgress` is what the header names instead.</p>
 */
export interface PlanWorkProgress {
  /** 0…1 over the whole plan, weighted by each act's steps. */
  fraction: number
  actsDone: number
  actsTotal: number
  /** Acts started and not finished — « 1 acte en cours ». */
  actsInProgress: number
  /** The steps of those acts, as « 2 / 3 étapes » when exactly one act is under way. */
  soleInProgressSteps: { done: number; total: number } | null
}

export function planWorkProgress(plan: TreatmentPlanDto): PlanWorkProgress {
  const items = plan.items
  let credit = 0
  let actsDone = 0
  const started: TreatmentPlanItemDto[] = []

  for (const item of items) {
    const total = item.steps?.length ?? 0
    const done = item.steps?.filter((s) => s.doneDate).length ?? 0

    if (item.status === "Done") {
      credit += 1
      actsDone += 1
      continue
    }
    if (total > 0 && done > 0) {
      credit += done / total
      started.push(item)
    }
  }

  return {
    fraction: items.length > 0 ? credit / items.length : 0,
    actsDone,
    actsTotal: items.length,
    actsInProgress: started.length,
    soleInProgressSteps:
      started.length === 1
        ? {
            done: started[0].steps?.filter((s) => s.doneDate).length ?? 0,
            total: started[0].steps?.length ?? 0,
          }
        : null,
  }
}
