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
  if (!item.scheduledAt) return "to-schedule"
  return new Date(item.scheduledAt).getTime() > now.getTime() ? "scheduled" : "to-record"
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
