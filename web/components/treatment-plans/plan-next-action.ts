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

/**
 * True once any of this act's work has been delivered — the act is réalisé, or one of its steps carries a date.
 *
 * <p>⚠️ <b>This, and never {@link planItemState}, is what decides whether an act may be dropped.</b> That
 * function answers for the act's *next step*, deliberately, so a bridge with two of three séances carried out
 * returns `"to-schedule"` — and « Arrêter le traitement » filtered on it, offering to delete two delivered
 * séances, their step rows and the links to the fiches that evidenced them, under a dialog promising « ce qui a
 * déjà été fait est conservé ». The stop is a server command now and asks this question itself; this reader is
 * what the dialog uses to *show* the same answer before the press.</p>
 */
export function hasDeliveredWork(item: TreatmentPlanItemDto): boolean {
  return item.status === "Done" || (item.steps?.some((s) => s.doneDate) ?? false)
}

/** An act parked by « Arrêter le traitement »: not treatment any more, and nothing about it is lost. */
export function isItemWithdrawn(item: TreatmentPlanItemDto): boolean {
  return item.isWithdrawn === true || item.status === "Withdrawn"
}

/**
 * The acts that still count as this plan's treatment. Every count, total and « what is next » reads this — a
 * parked act contributes nothing while keeping its own history.
 */
export function activeItems(plan: TreatmentPlanDto): TreatmentPlanItemDto[] {
  return plan.items.filter((i) => !isItemWithdrawn(i))
}

/**
 * What a screen should print as « Reste » for this devis — and **null when there is nothing honest to print**.
 *
 * <p>⚠️ <b>One reader, because seven surfaces disagreed.</b> A plan's own `outstanding` is
 * `totalPlanned − Σ its own installments`, and a plan bridged into a note d'honoraires has an auto-raised
 * échéance that will never see a payment, because the money went to the note. So the figure reports the *whole*
 * devis as unpaid: measured on 4 of 4 bridged plans in a live database, two of them fully settled — one patient
 * shown « Solde dû 31,000 DT » in their file header and « Reste 120,000 DT » in the plan strip on the same page,
 * another shown a red « Reste » with an « En retard » badge on a treatment they had paid in full.</p>
 *
 * <p>The note's own balance is returned instead when the DTO carries it, so the number stays *a number* rather
 * than disappearing. `isBilled` says which document it is about, which is what lets a caller word it — « reste
 * sur la note 2026-0087 » is a different sentence from « reste sur le devis ».</p>
 *
 * <p>A **Draft** returns null for the reason that was already recorded for it and applied in exactly one place:
 * a draft contributes 0 to « Solde patient », so printing a « Reste » there « would contradict the balance the
 * rest of the app reports ». That argument is verbatim the argument for a billed plan.</p>
 */
export interface DisplayedOutstanding {
  amount: number
  /** The figure belongs to the linked note, not to the devis. */
  isBilled: boolean
  /** The note it belongs to, when it is one — for the wording beside the figure. */
  invoiceNumber?: string | null
}

export function displayedOutstanding(plan: TreatmentPlanDto): DisplayedOutstanding | null {
  if (plan.status === "Draft" || plan.status === "Cancelled") return null

  if (isPlanBilled(plan)) {
    // The note's balance where the server sent it; otherwise nothing at all, never the plan's own figure —
    // withholding a number is recoverable, printing the wrong one is what sends somebody to collect money the
    // patient has already handed over.
    if (plan.linkedInvoiceOutstanding == null) return null
    return {
      amount: plan.linkedInvoiceOutstanding,
      isBilled: true,
      invoiceNumber: plan.linkedInvoiceNumber ?? null,
    }
  }

  return { amount: plan.outstanding, isBilled: false }
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

  const live = activeItems(plan)

  const toRecord = live.find((i) => planItemState(i, now) === "to-record")
  if (toRecord) return { kind: "record", itemId: toRecord.id }

  const toSchedule = live.find((i) => planItemState(i, now) === "to-schedule")
  if (toSchedule) return { kind: "schedule", itemId: toSchedule.id }

  // ⚠️ `displayedOutstanding`, not `plan.outstanding`: on a bridged devis that figure is the untouched
  // auto-échéance, so this pointed the dentist at an échéancier the server then refuses to collect on.
  const owed = displayedOutstanding(plan)
  if (owed && !owed.isBilled && owed.amount > 0) return { kind: "collect" }
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

  const live = activeItems(plan);

  const toRecord = live.filter((i) => planItemState(i, now) === "to-record").length;
  if (toRecord > 0) return `${toRecord} acte${toRecord > 1 ? "s" : ""} à enregistrer`;

  const toSchedule = live.filter((i) => planItemState(i, now) === "to-schedule").length;
  if (toSchedule > 0) return `${toSchedule} acte${toSchedule > 1 ? "s" : ""} à planifier`;

  // Same reader as `planNextAction`, and for its reason: « Reste à encaisser » on a devis the note already
  // collected is the headline sending somebody to an échéancier that refuses them.
  const owed = displayedOutstanding(plan);
  if (owed && !owed.isBilled && owed.amount > 0) return "Reste à encaisser";

  const scheduled = live.filter((i) => planItemState(i, now) === "scheduled").length;
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
      // Which note holds this devis' money, if one does. `outstanding` above is unusable when it is set —
      // see `BilledOnPlan.billedOnInvoiceNumber` for the measured case.
      billedOnInvoiceNumber: plan.linkedInvoiceNumber ?? null,
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
  // A parked act is excluded for the same reason a Done one is: booking it would produce a visit for work the
  // patient is not coming back for, and the server refuses to record anything against it.
  return activeItems(plan).filter((item) => item.status !== "Done")
}

/** The one devis act a booking dialog should offer unprompted, with the step it is waiting on. */
export interface PlanStepSuggestion {
  plan: TreatmentPlanDto
  item: TreatmentPlanItemDto
  /** The séance to book, or null for an act with no protocol — then the whole act is the séance. */
  step: ReturnType<typeof nextStepOf>
  /**
   * Is this act already **under way** — at least one séance carried out?
   *
   * <p>It decides the wording and nothing else. « Ce patient a un traitement en cours » about a devis accepted
   * last week with nothing done yet would be a small lie told by the one surface whose job is to remind somebody
   * of a fact they had forgotten; « un devis accepté » is the truth and just as useful.</p>
   */
  continuing: boolean
}

/**
 * The devis act to **suggest** when a séance is being booked for this patient — or null when there is nothing to
 * suggest.
 *
 * <p>The dentist books from the agenda, in a hurry, for a patient whose bridge is half done. Nothing on that path
 * mentioned the devis, so the séance was booked as a loose act and the plan reported the scellement as still
 * unplanned. This is what the dialog says out loud before he picks anything.</p>
 *
 * <p>⚠️ <b>Only an act with nothing booked</b> (`planItemState === "to-schedule"`). An act whose next séance is
 * already in the agenda must not be offered: accepting it would book the same step twice, which is the one thing
 * the whole multi-séance feature refuses. That test is `planItemState`'s, keyed on the next STEP rather than on
 * the act — a bridge two thirds done carries an appointment that already happened.</p>
 *
 * <p>⚠️ <b>An act under way outranks an untouched one</b>, and a plan's own act order breaks the tie. A patient
 * with a half-finished bridge and a freshly accepted détartrage is being asked about the bridge; the détartrage is
 * still in the picker's « Actes du devis » group one click away, so nothing is hidden by choosing.</p>
 *
 * <p>⚠️ <b>One suggestion, never a list.</b> Several would be a second acts picker rendered above the acts picker,
 * and the picker already offers every one of them. This surface answers « avez-vous oublié ? », which is one
 * question.</p>
 */
export function suggestedPlanStep(
  plans: readonly TreatmentPlanDto[],
  now: Date = new Date(),
): PlanStepSuggestion | null {
  const candidates: PlanStepSuggestion[] = []

  for (const plan of plans) {
    // The same gate the picker's group uses — a Draft or Cancelled devis contributes nothing, because booking
    // against a quote nobody accepted is not a shortcut, it is a mistake with a devis number on it.
    for (const item of schedulablePlanItems(plan)) {
      if (planItemState(item, now) !== "to-schedule") continue
      candidates.push({
        plan,
        item,
        step: nextStepOf(item),
        continuing:
          item.status === "InProgress" || (item.steps?.some((step) => step.doneDate != null) ?? false),
      })
    }
  }

  if (candidates.length === 0) return null
  return candidates.find((c) => c.continuing) ?? candidates[0]
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
  const items = activeItems(plan)
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

/**
 * How far along a treatment is, counted in **séances** — the one figure every progress surface prints.
 *
 * <p>⚠️ « AVANCEMENT » and « Actes réalisés » counted whole *acts*, and a stepped act is only Done when every
 * step is — so a six-visit implant read « 0 / 1 actes » from its first appointment to its last, and a bridge
 * with two of three séances delivered read « 0 / 2 actes ». On the list a dentist scans daily, the feature's
 * whole subject had no progress signal at all. The workspace's progress *bar* was already step-weighted; the
 * number beside it was not, which is what made the two disagree on one screen.</p>
 *
 * <p>A step-less act counts as one séance, so a devis of ordinary single-visit acts reads exactly as it did.</p>
 */
export interface PlanSeanceProgress {
  done: number
  total: number
  /** 0…1, for a bar. `total === 0` yields 0 rather than NaN. */
  fraction: number
  /** « 2 / 5 séances », ready to print. */
  label: string
}

export function planSeanceProgress(plan: TreatmentPlanDto): PlanSeanceProgress {
  let done = 0
  let total = 0

  for (const item of activeItems(plan)) {
    const steps = item.steps?.length ?? 0
    if (steps === 0) {
      total += 1
      if (item.status === "Done") done += 1
      continue
    }
    total += steps
    done += item.steps?.filter((s) => s.doneDate).length ?? 0
  }

  return {
    done,
    total,
    fraction: total > 0 ? done / total : 0,
    label: `${done} / ${total} séance${total > 1 ? "s" : ""}`,
  }
}
