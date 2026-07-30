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
