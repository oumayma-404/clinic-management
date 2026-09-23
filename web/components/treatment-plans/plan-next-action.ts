import type { PlanActContinuation, PresetPlanAct } from "@/components/appointment-acts-picker"
import type { TreatmentPlanDto, TreatmentPlanItemDto } from "@/lib/api/types"
import { PLAN_STATUS_LABELS, planHasRecordedWork } from "./treatment-plan-labels"

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

/** What « Détacher la fiche » will actually do to one act — see {@link detachOutcome}. */
export interface DetachOutcome {
  /** The séance being released, when the act is cut into séances. Null for an act done in one sitting. */
  stepLabel: string | null
  /**
   * That séance's id — what « … et la rattacher à » (S6) sends as `fromStepId`. Null for a step-less act,
   * which is exactly what the server reads as « the act itself ».
   */
  stepId: string | null
  /**
   * The fiche that will be released. **Read before the call, because the call is what clears the link** — it
   * is the only pointer a devis surface has to that record, and re-pointing it is the correction the dentist
   * is in the middle of making.
   */
  dentalRecordId: string | null
  /** Séances still recorded afterwards, out of the act's total. Null for a step-less act. */
  remaining: { done: number; total: number } | null
}

/**
 * What « Détacher la fiche » will do to this act, stated rather than guessed.
 *
 * <p>⚠️ <b>On an act with steps it undoes the LAST séance recorded, not the whole act</b> — a three-séance
 * couronne lands on « En cours · 2 étapes sur 3 faites », never on « Prévu ». Both the confirmation and the
 * success toast said « Prévu » unconditionally, which is a false statement about the commonest case the
 * control exists for; the workspace's own help paragraph had the truth and the dialog the user reads did
 * not.</p>
 *
 * <p>This mirrors <c>TreatmentPlanItem.Unmark</c>: the step released is the last <i>done</i> one by rank, and
 * the act's own <c>linkedDentalRecordId</c> is that step's once it is complete — so the two agree by
 * construction rather than by coincidence. An act whose last step is the only one done returns to « Prévu »,
 * which is why `remaining.done` may be 0 and the caller words that case as a step-less one.</p>
 */
export function detachOutcome(item: TreatmentPlanItemDto): DetachOutcome {
  const steps = item.steps ?? []
  if (steps.length === 0) {
    return { stepLabel: null, stepId: null, dentalRecordId: item.linkedDentalRecordId, remaining: null }
  }

  const done = [...steps]
    .filter((s) => s.doneDate)
    .sort((a, b) => a.sequenceNumber - b.sequenceNumber)
  const released = done[done.length - 1] ?? null

  return {
    stepLabel: released?.label ?? null,
    stepId: released?.id ?? null,
    // The step's own link is the specific fact; the act's is the same record once it is complete, and the
    // fallback covers a response that predates per-step links.
    dentalRecordId: released?.linkedDentalRecordId ?? item.linkedDentalRecordId,
    remaining: { done: Math.max(0, done.length - 1), total: steps.length },
  }
}

/** One place a detached séance can be re-attached to — an act, or one séance of an act (S6). */
export interface RelinkTarget {
  /** Stable across a re-render; the `itemId:stepId` pair the call needs. */
  key: string
  itemId: string
  stepId: string | null
  /** « Couronne 16 · Empreinte » — the act, and the séance when the act has a protocol. */
  label: string
}

/**
 * Where the séance being detached could belong instead (S6).
 *
 * <p>⚠️ <b>Steps, not just acts</b> — and that is the half the fiche's own picker structurally could not
 * offer: its Select names acts, so a séance attached to the wrong <i>step</i> of the right act needed the
 * booking edited on a third screen. A stepped act contributes one option per séance; a step-less act
 * contributes itself.</p>
 *
 * <p>⚠️ A séance that is <b>already recorded</b> is excluded: an act (or a step) may not carry two fiches, so
 * offering one would produce a refusal from a control the product had just offered. The source itself is
 * excluded for the same reason it would be a no-op. Withdrawn acts are out — parked work is not where a
 * recorded séance belongs.</p>
 */
export function relinkTargets(
  plan: TreatmentPlanDto,
  from: { itemId: string; stepId: string | null },
): RelinkTarget[] {
  const targets: RelinkTarget[] = []

  for (const item of activeItems(plan)) {
    const steps = item.steps ?? []

    if (steps.length === 0) {
      if (item.id === from.itemId && from.stepId === null) continue
      // A step-less act already carrying a fiche cannot take another.
      if (item.linkedDentalRecordId) continue
      targets.push({ key: `${item.id}:`, itemId: item.id, stepId: null, label: item.designationFr })
      continue
    }

    for (const step of steps) {
      if (item.id === from.itemId && step.id === from.stepId) continue
      if (step.doneDate) continue
      targets.push({
        key: `${item.id}:${step.id}`,
        itemId: item.id,
        stepId: step.id,
        label: `${item.designationFr} · ${step.label}`,
      })
    }
  }

  return targets
}

/**
 * True once a note d'honoraires **represents** this devis — it hides « Facturer » and it is what
 * {@link displayedOutstanding} keys on to name the note's balance instead of the plan's.
 *
 * <p>⚠️ **`linkedInvoiceStatus`, not `linkedInvoiceId != null`.** The server sets the link for *any* attached
 * note including a `Draft` and a `Cancelled` one, and `TreatmentPlanMappingExtensions` deliberately keeps its
 * own `planIsBilled` on `PlanBillingRules.RepresentsItsPlan` for that exact reason — a draft has carried nothing
 * yet and a cancelled bridge is void, so in both cases the **plan** keeps its own balance and « Solde patient »
 * counts it (pinned by `MoneyReadConsistencyTests`'
 * `Cancelling_The_Bridge_Invoice_Returns_The_Plan_To_The_Balance`). Reading the id alone made this the one
 * client-side copy of that rule that disagreed with the server: a devis of 1 000 with 400 collected and a
 * cancelled bridge note showed « Reste sur la note 1 000,000 DT » in the plan strip while the header said
 * « Solde dû 600,000 DT » — two figures about one devis, on one screen, and the wrong document named.</p>
 */
export function isPlanBilled(plan: TreatmentPlanDto): boolean {
  const status = plan.linkedInvoiceStatus
  return plan.linkedInvoiceId != null && status !== "Draft" && status !== "Cancelled"
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

/** The visit that goes with an act being removed, and what will happen to it. */
export interface ActRemovalBooking {
  appointmentId: string
  /** ISO instant, so the caller formats it with the app's own 24-hour helpers. */
  at: string
  /** Other acts of this same devis that the visit also covers. `> 0` means the visit stands. */
  sharedWith: number
}

/** What removing one act from a devis will do — see {@link actRemovalPlan}. */
export type ActRemovalPlan =
  | { removable: false; reason: string }
  | { removable: true; booking: ActRemovalBooking | null }

/**
 * Whether an act may be taken off the devis, and what its rendez-vous will do if it is.
 *
 * <p>⚠️ <b>This, and never {@link planItemState}, is what the removal control asks.</b> That function answers
 * for the act's <i>next step</i> — deliberately, so the badge says what to do next — and reading it as
 * « is this removable? » was wrong in three directions at once: a visit that had already passed disabled the
 * bin with « un rendez-vous est prévu » while the server would happily have allowed the removal; a bridge with
 * one séance delivered and the next unbooked *enabled* the bin on work the server refuses; and a séance booked
 * out of protocol order did the same. The mirror of `TreatmentPlan.EnsureItemRemovable`, term for term.</p>
 *
 * <p>The one refusal is <b>delivered work</b> — a fiche. Everything else comes off, and the booking travels
 * with it: the handler cancels a visit this act was the only reason for and drops the act from one that
 * carries others, which is why this reports which of the two it will be rather than refusing.</p>
 */
export function actRemovalPlan(plan: TreatmentPlanDto, item: TreatmentPlanItemDto): ActRemovalPlan {
  if (item.status === "Done") {
    return {
      removable: false,
      reason: `Acte déjà réalisé — utilisez « Détacher la fiche » sur la ligne de l'acte, puis réessayez.`,
    }
  }
  if (hasDeliveredWork(item)) {
    const done = item.steps?.filter((s) => s.doneDate).length ?? 0
    return {
      removable: false,
      reason: `${done} séance(s) déjà réalisée(s) — détachez-les de leur fiche de soins, puis réessayez.`,
    }
  }

  // The act's own booking, else the earliest one any of its séances carries. Past or future alike: a slot that
  // has gone by with nobody recording anything is still a standing rendez-vous, and it is precisely the one a
  // dentist changing their mind wants gone.
  const fromSteps = (item.steps ?? [])
    .filter((s) => s.scheduledAppointmentId && s.scheduledAt)
    .sort((a, b) => (a.scheduledAt! < b.scheduledAt! ? -1 : 1))[0]
  const appointmentId = item.scheduledAppointmentId ?? fromSteps?.scheduledAppointmentId ?? null
  const at = item.scheduledAt ?? fromSteps?.scheduledAt ?? null
  if (!appointmentId || !at) return { removable: true, booking: null }

  // How many OTHER acts of this devis the same visit covers. It may also carry acts from elsewhere — a walk-in
  // détartrage — which this cannot see; the server decides for real, and it only ever errs towards keeping the
  // visit, so the sentence never promises a cancellation that does not happen.
  const sharedWith = plan.items.filter(
    (other) => other.id !== item.id && other.scheduledAppointmentId === appointmentId,
  ).length

  return { removable: true, booking: { appointmentId, at, sharedWith } }
}

/**
 * ── The plan-level permissions, in ONE place ────────────────────────────────────────────────────────────────
 *
 * <p>⚠️ Every one of these lived inline in `plan-workspace.tsx` and nowhere else, so the devis <b>list</b> —
 * the same data, the same rules — offered « Modifier le brouillon » on a followed treatment with recorded
 * séances (which the server now refuses outright), no « Facturer » at all, and no way to annuler a devis
 * issued by mistake. That is this repo's dominant defect shape: a correct rule wired to one call site.</p>
 */

/**
 * The statuses in which a devis is closed to every write — the browser twin of `EnsureAmendable`'s refusals.
 *
 * <p>⚠️ <b>`WrittenOff` had to be added HERE and to nine other tests the day the status was appended</b>, and
 * that is exactly the shape `TreatmentPlanStatusCoverageTests` exists for on the server: a status no predicate
 * names falls through to whatever the negative test happens to be. Written out once and read by the four
 * permissions below.</p>
 */
const CLOSED_TO_WRITES = ["Cancelled", "WrittenOff"]

/**
 * Is this devis closed to every write?
 *
 * <p>Exported because three controls on the workspace asked it as <code>plan.status !== "Cancelled"</code> —
 * the hand-written negative shape, which reads an appended status as OPEN. Two of the three were offers the
 * server refuses by name: « Encaisser » on a written-off devis bounces « Le plan doit être accepté pour
 * enregistrer un paiement. » (`EnsurePayable`), and correcting one of its actes bounces « Ce devis est
 * annulé » — a sentence about a state the devis is not in. N40 fails on a fourth copy.</p>
 */
export function isPlanClosedToWrites(plan: { status: string }): boolean {
  return CLOSED_TO_WRITES.includes(plan.status)
}

/** Can the acts, the fees and the échéancier still be corrected? Mirrors the server's `EnsureAmendable`. */
export function canAmendPlan(plan: TreatmentPlanDto): boolean {
  return !CLOSED_TO_WRITES.includes(plan.status)
}

/**
 * Can this plan be edited through the DRAFT editor (`PUT /treatment-plans/{id}`), which replaces the acts
 * wholesale — as opposed to the amend door, which preserves each act's id?
 *
 * <p>⚠️ <b>A `Draft` is not enough.</b> « Suivre ce traitement » creates un-numbered drafts that carry real
 * séances and links to the fiches evidencing them, and `SetItems` now throws on any act with steps or
 * delivered work. So a draft that has been worked on goes through the amend door like everything else, or the
 * dentist meets a refusal from a button the product offered them.</p>
 */
export function canUseDraftEditor(plan: TreatmentPlanDto): boolean {
  // `SetItems` also refuses any act already cut into séances — the same test here, or the button leads to a
  // refusal (F6).
  return (
    plan.status === "Draft" &&
    !planHasRecordedWork(plan) &&
    !plan.items.some((i) => (i.steps?.length ?? 0) > 0)
  )
}

/** Can the plan be destroyed outright? Mirrors `TreatmentPlan.CanBeDeleted`. */
export function canDeletePlan(plan: TreatmentPlanDto): boolean {
  return plan.status === "Draft" && !planHasRecordedWork(plan)
}

/**
 * Is « Facturer le devis » offered? Every live status except a draft, minus a devis a note already bills —
 * except where an amendment has grown the devis past what that note carries, which the server bills as a
 * supplementary note.
 *
 * <p>⚠️ Deliberately wider than « is this plan active »: a plan auto-completes the instant its last step is
 * recorded, so gating on active withdrew the button at the exact moment the treatment became billable.</p>
 */
export function canBillPlan(plan: TreatmentPlanDto): boolean {
  // ⚠️ `WrittenOff` refuses too: raising a note for a balance the practice has just decided to abandon would
  // put the créance straight back, on a second document, under a different number.
  if (plan.status === "Draft" || CLOSED_TO_WRITES.includes(plan.status)) return false
  if (!isPlanBilled(plan)) return true
  return plan.linkedInvoiceTotal != null && plan.totalPlanned - plan.linkedInvoiceTotal > 0.0005
}

/**
 * Would « Arrêter le traitement » come out as a <b>cancellation</b>? Mirrors `TreatmentPlan.StopWouldCancel`.
 *
 * <p>⚠️ The money term is load-bearing and was added with C2: a numbered devis carrying a deposit takes the
 * <i>stop</i> branch, because `Cancel` refuses live money outright and sending the dentist there would name a
 * remedy the product then refuses.</p>
 */
export function stopWouldCancelPlan(plan: TreatmentPlanDto): boolean {
  return (
    plan.number != null
    && plan.amountPaid <= 0.0005
    && !activeItems(plan).some(hasDeliveredWork)
  )
}

/**
 * Would « Arrêter le traitement » be <b>refused</b>, because money was taken and nothing was delivered?
 *
 * <p>⚠️ <b>The server has THREE outcomes and this file only knew two.</b> `TreatmentPlan.StopTreatment` sorts a
 * stop into cancel · stop · <i>refuse until the cash is refunded</i>, and that third arm
 * (`TreatmentPlan.cs`, « Aucun acte de ce devis n'a été réalisé, mais … DT y ont déjà été encaissés ») is
 * exactly the shape {@link stopWouldCancelPlan}'s money term creates: the deposit pushes the devis off the
 * cancel branch, and nothing then asked whether the stop it was pushed onto can actually land. Measured
 * 2026-09-15: the ⋯ menu offered « Arrêter le traitement », the dialog listed the acts under « Mis de côté »
 * and stated « le traitement passe à « Arrêté » », and the press was refused with the devis left « En cours ».</p>
 *
 * <p>It is a <b>named refusal, never a withheld control</b> — the same rule as « pourquoi Encaisser a
 * disparu » (M26). Since wave 3 the dialog offers « Rendre et arrêter »: the deposit is given back today on the
 * devis and the acts are parked — no avoir.</p>
 *
 * <p>⚠️ Mirrors the server condition exactly, including `Number != null`: an un-numbered treatment carrying
 * money is not this case — it stops normally, because there is no document and `kept.Count == 0` is only
 * refused on a numbered devis.</p>
 */
export function stopNeedsRefundFirst(plan: TreatmentPlanDto): boolean {
  return (
    plan.number != null
    && plan.amountPaid > 0.0005
    && !activeItems(plan).some(hasDeliveredWork)
  )
}

/**
 * Is « Annuler le devis » offered as its own entry?
 *
 * <p>⚠️ <b>Only where « Arrêter le traitement » cannot reach the cancellation itself.</b> The two were folded
 * into one control on purpose — « le patient ne poursuit pas » is one intention and the arithmetic decides the
 * outcome — but the fold left a numbered devis with delivered work, or one already closed, with <i>no</i> route
 * to an annulation at all: `POST /{id}/cancel` was reachable from nowhere in the browser. A devis issued to the
 * wrong patient, or for work that was then re-quoted, is a real case and the number has to be voided with a
 * motif rather than left standing.</p>
 *
 * <p>Refused on live money for the server's own reason (`EnsureNoLiveMoney`): cancelling drops the plan out of
 * every caisse read, so a cancellation over collected cash rewrites days that are already closed.</p>
 */
export function canCancelPlan(plan: TreatmentPlanDto): boolean {
  return (
    plan.number != null
    && !CLOSED_TO_WRITES.includes(plan.status)
    && plan.amountPaid <= 0.0005
    && !stopWouldCancelPlan(plan)
  )
}

/**
 * Is « Passer la créance en perte » offered (S4)?
 *
 * <p>A numbered devis with something still outstanding, not already abandoned and not already cancelled — and
 * <b>not one a note d'honoraires represents</b>, because there the note carries the créance and writing off
 * the plan moves nothing at all (the server refuses it by name). An un-numbered treatment claims nothing, so
 * there is nothing to abandon.</p>
 */
export function canWriteOffPlan(plan: TreatmentPlanDto): boolean {
  if (plan.number == null) return false
  if (plan.status === "Cancelled" || plan.status === "WrittenOff") return false
  if (isPlanBilled(plan)) return false
  const owed = displayedOutstanding(plan)
  return owed != null && !owed.isBilled && owed.amount > 0.0005
}

/**
 * Is « Reprendre le traitement » offered? The way back from all three closed states — and it is what stops
 * `WrittenOff` becoming a second absorbing state, which is the defect `canUncancelPlan` had to be written for.
 */
export function canReopenPlan(plan: TreatmentPlanDto): boolean {
  return plan.status === "Completed" || plan.status === "Stopped" || plan.status === "WrittenOff"
}

/**
 * What one act costs the patient — its tarif minus any remise (S2).
 *
 * <p>⚠️ <b>Read this, never `plannedCost`, wherever money for an act is printed or summed.</b> `netCost` is
 * served precisely so the browser does not re-implement the subtraction; the fallback covers an older response
 * that carries neither field, where the tarif IS the net.</p>
 */
export function itemNetCost(item: TreatmentPlanItemDto): number {
  return item.netCost ?? item.plannedCost
}

/** What was given away on this act, 0 on an ordinary line. */
export function itemDiscount(item: TreatmentPlanItemDto): number {
  return item.discountAmount ?? 0
}

/** One échéance a settlement will land on, and how much of it. */
export interface InstallmentLanding {
  installmentId: string
  dueDate: string
  isAutoRaised?: boolean
  amount: number
}

/**
 * Where a single settlement (S3) will land — the échéances it fills, in order, with the slice each takes.
 *
 * <p>⚠️ <b>The server's own order, term for term</b>: `TreatmentPlan.CollectChairside` walks
 * `OrderBy(DueDate).ThenBy(Id)` and fills each row's remaining room. This mirrors it so the dialog can SAY
 * what the press will do — and it is a display only: the server does the arithmetic that matters, and a
 * disagreement here shows a wrong preview rather than writing a wrong figure.</p>
 */
export function installmentsPayableRoom(
  plan: TreatmentPlanDto,
  amount: number,
): InstallmentLanding[] {
  const landing: InstallmentLanding[] = []
  let remaining = amount

  const ordered = [...plan.installments].sort((a, b) =>
    a.dueDate === b.dueDate ? a.id.localeCompare(b.id) : a.dueDate.localeCompare(b.dueDate),
  )

  for (const inst of ordered) {
    if (remaining <= 0.0005) break
    const room = inst.outstanding
    if (room <= 0.0005) continue
    const slice = Math.min(room, remaining)
    landing.push({
      installmentId: inst.id,
      dueDate: inst.dueDate,
      isAutoRaised: inst.isAutoRaised,
      amount: slice,
    })
    remaining -= slice
  }

  return landing
}

/** Is « Rétablir ce devis annulé » offered? The only way out of `Cancelled`, which nothing could leave. */
export function canUncancelPlan(plan: TreatmentPlanDto): boolean {
  return plan.status === "Cancelled"
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
  // WrittenOff: the balance was abandoned, so « Reste dû » and « Encaisser » would chase a loss (G8).
  if (plan.status === "Draft" || plan.status === "Cancelled" || plan.status === "WrittenOff") return null

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
  if (plan.status === "Cancelled" || plan.status === "WrittenOff") return { kind: "open" }

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

/**
 * The order the chips are read in — derived from {@link PLAN_STATUS_LABELS}' own key order, which is already
 * written « how much attention does this deserve »: sans devis, accepté, en cours, terminé, arrêté, annulé.
 *
 * <p>⚠️ <b>It was a hand-written array and it had no `Stopped`</b>, so every arrêté plan of a patient vanished
 * from the summary — the exact defect the chips were introduced to fix for `Completed`, repeated the day
 * `Stopped` was appended to the enum. Derived, a seventh status cannot be forgotten here.</p>
 */
const PLAN_STATUS_ORDER = Object.keys(PLAN_STATUS_LABELS);

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
  const counts = new Map<string, number>();

  for (const plan of plans) {
    if (plan.id === excludeId) continue;
    counts.set(plan.status, (counts.get(plan.status) ?? 0) + 1);
  }

  const seen = new Set(PLAN_STATUS_ORDER);
  return [
    ...PLAN_STATUS_ORDER.filter((status) => counts.has(status)),
    // A status the label map does not know yet still gets a chip — with its raw key, which is ugly and visible,
    // rather than being dropped and reported as a patient with no plans at all.
    ...[...counts.keys()].filter((status) => !seen.has(status)).sort(),
  ].map((status) => ({ status, count: counts.get(status)! }));
}

/**
 * The plan a patient-level surface should lead with: the most recently accepted active plan, else the most
 * recently created draft, else nothing (render no card at all rather than an empty box).
 */
export function leadPlan(plans: TreatmentPlanDto[]): TreatmentPlanDto | null {
  const active = plans
    .filter((p) => isPlanLive(p.status))
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
    /*
     * ⚠️ **A continuation names the act it FINISHES, and which séance this is.** Left as its own désignation
     * the row read « continuation (dents 11) » — a label the dentist typed, on a card that looked like an
     * independent act, with nothing tying it to the traitement de canal it is the second half of. Reported
     * from use, on the screen where the séance is booked.
     */
    label: planActLabel(item, continuationContext(plan, item)),
    // G6: the price after remise — the booking screens showed the tarif and hid the discount.
    plannedCost: itemNetCost(item),
    // ⚠️ **The whole protocol, réalisé steps included — `PlanStepOption.done` is what withholds them.** This
    // filtered them out, which is right for a chip somebody can tick and wrong for every label lookup that
    // resolves an appointment's OWN booked step against this list: once the fiche was recorded the step
    // vanished from here and the booking dialog printed « Séance : étape ».
    steps: item.steps?.map((step) => ({
      id: step.id,
      label: step.label,
      estimatedDurationMinutes: step.estimatedDurationMinutes,
      done: step.doneDate != null,
      bookedAt: step.scheduledAppointmentId ? step.scheduledAt ?? null : null,
    })),
    // ⚠️ The first séance nobody has booked yet: `nextStepId` ignores bookings, so « Planifier la suite » put a
    // second visit on a step already in the agenda and its fiche was then skipped at 0 DT (E6).
    preselectedStepId: firstUnbookedStepId(item),
    billedOnPlan: {
      planNumber: plan.number,
      actCost: itemNetCost(item),
      outstanding: plan.outstanding,
      // Which note holds this devis' money, if one does. `outstanding` above is unusable when it is set —
      // see `BilledOnPlan.billedOnInvoiceNumber` for the measured case.
      billedOnInvoiceNumber: plan.linkedInvoiceNumber ?? null,
      continuation: continuationContext(plan, item),
    },
  }
}

/** The first un-done step with no visit on it, by rank — else the server's `nextStepId`. */
function firstUnbookedStepId(item: TreatmentPlanItemDto): string | null {
  const open = [...(item.steps ?? [])]
    .filter((s) => !s.doneDate)
    .sort((a, b) => a.sequenceNumber - b.sequenceNumber)
  return open.find((s) => !s.scheduledAppointmentId)?.id ?? item.nextStepId ?? null
}

/**
 * How an act is named wherever a séance is composed — its own désignation, or, for a continuation, the act it
 * finishes and the séance's rank first.
 *
 * <p>« Traitement de canal (dévitalisation) — séance 2 sur 2 : continuation (dents 11) ». ⚠️ Long, and
 * deliberately not shortened: it is the row's <b>identity</b>, and § 10.1 forbids truncating a control's name.</p>
 */
function planActLabel(item: TreatmentPlanItemDto, continuation: PlanActContinuation | undefined): string {
  const teeth = item.toothNumbers.length > 0 ? ` (dents ${item.toothNumbers.join(", ")})` : ""
  if (!continuation) return `${item.designationFr}${teeth}`
  return (
    `${continuation.ofLabel} — séance ${continuation.seanceNumber} sur ${continuation.seanceTotal}` +
    ` : ${item.designationFr}${teeth}`
  )
}

/**
 * What this act continues, when it continues anything — the act already carried out and billed, which séance
 * this is, and what the treatment is worth across **both** documents.
 *
 * <p>⚠️ <b>The parent is the act the plan holds at 0 against a note</b> (`billedOnInvoiceId`), not « the first
 * line »: that marker is the one thing that states the arrangement, and reading position instead would call any
 * two-line devis a continuation.</p>
 *
 * <p>⚠️ <b>The séance rank is counted over real step rows, never `done + 1`.</b> A rank derived from a count is
 * only the séance being booked when the séances happen in order, and this app deliberately lets them not — so it
 * is read off the flattened protocol, where the answer is a position and not an estimate.</p>
 *
 * <p>Returns undefined for every ordinary act, so nothing downstream renders.</p>
 */
function continuationContext(
  plan: TreatmentPlanDto,
  item: TreatmentPlanItemDto,
): PlanActContinuation | undefined {
  const live = activeItems(plan)
  const parent = live.find((i) => i.billedOnInvoiceId != null && i.id !== item.id)
  if (!parent || plan.treatmentOutstanding == null) return undefined

  const note = (plan.carriedInvoices ?? []).find((c) => c.invoiceId === parent.billedOnInvoiceId)

  // The whole protocol in clinical order; this séance is where this act's next step sits in it.
  const ordered = [...live].sort((a, b) => a.sequenceNumber - b.sequenceNumber)
  const flat = ordered.flatMap((i) =>
    (i.steps?.length ?? 0) > 0
      ? i.steps!.map((s) => ({ itemId: i.id, stepId: s.id }))
      : [{ itemId: i.id, stepId: null as string | null }],
  )
  const at = flat.findIndex((s) =>
    item.nextStepId ? s.stepId === item.nextStepId : s.itemId === item.id,
  )

  return {
    ofLabel: parent.designationFr,
    seanceNumber: at >= 0 ? at + 1 : flat.length,
    seanceTotal: flat.length,
    noteNumber: note?.number ?? parent.billedOnInvoiceNumber ?? null,
    noteOutstanding: note?.outstanding ?? 0,
    treatmentTotal: plan.treatmentTotal ?? 0,
    treatmentOutstanding: plan.treatmentOutstanding,
  }
}

/**
 * Is this treatment still running — may a séance be booked against it and a fiche recorded?
 *
 * <p><b>The one test.</b> It was written out by hand as `status === "Accepted" || status === "InProgress"` in
 * four places, and when « Suivre ce traitement » made an un-numbered `Draft` a live treatment, three of the
 * four were updated and the fourth — `PlanActPrimaryAction` — was not. The act then rendered « À planifier »
 * beside no button, so the treatment the dentist had just started could not be booked. `check:responsive`'s
 * N23 fails on a fifth hand-written copy.</p>
 *
 * <p>⚠️ <b>It is a POSITIVE list, and it was phrased as « not Cancelled and not Completed » until 2026-09-09.</b>
 * That phrasing is safe only while the closed statuses are the ones that exist. Appending `Stopped` — so that
 * « Arrêter le traitement » stops writing `Completed` and a stopped treatment can be told from a finished one —
 * made a stopped plan read as <b>live</b> here: bookable, listed under « Traitements suivis », ringed on the
 * odontogramme, its acts offered in the booking dialogs. No error, nothing on screen. The earlier phrasing was
 * chosen for a real reason (listing the open ones is what left `Draft` out when it became one), and the answer
 * to both is the same: enumerate deliberately, and let a guard fail when a member is unclassified. The server's
 * twin is `TreatmentPlanLifecycle.LiveStatuses`, held by `TreatmentPlanStatusCoverageTests`.</p>
 */
export function isPlanLive(status: TreatmentPlanDto["status"]): boolean {
  return status === "Draft" || status === "Accepted" || status === "InProgress"
}

/**
 * Has this treatment been closed by « Arrêter le traitement » rather than carried to term?
 *
 * <p>Read it instead of comparing the status where the two mean different things to the reader — the header's
 * primary action, the badge, the banner. Everywhere else `isPlanLive` is the question worth asking.</p>
 */
export function isPlanStopped(status: TreatmentPlanDto["status"]): boolean {
  return status === "Stopped"
}

/** Has this devis' unpaid balance been abandoned (S4)? A different fact from « arrêté » and from « annulé ». */
export function isPlanWrittenOff(status: TreatmentPlanDto["status"]): boolean {
  return status === "WrittenOff"
}

/**
 * The acts of one plan a séance can still be booked for — planned or under way, on a treatment that is live.
 *
 * <p>⚠️ A `Done` act is excluded, and a `Cancelled` plan contributes nothing: booking either would produce a
 * visit for work that is finished or for a devis nobody is honouring.</p>
 *
 * <p>⚠️ <b>A `Draft` DOES contribute, and excluding it was a dead end.</b> « Suivre ce traitement » creates an
 * un-numbered draft — that is the whole point, so that following an implant costs no financial document — and
 * with drafts excluded here the treatment it created offered no « Planifier » on any act, and the booking that
 * created it was refused outright with « Le plan de traitement est requis pour lier l'acte. » because
 * `resolveAttachedPlanId` could not see the plan its own act belonged to. A draft is un-quoted, not inert.</p>
 */
export function schedulablePlanItems(plan: TreatmentPlanDto): TreatmentPlanItemDto[] {
  if (!isPlanLive(plan.status)) return []
  // A parked act is excluded for the same reason a Done one is: booking it would produce a visit for work the
  // patient is not coming back for, and the server refuses to record anything against it.
  return activeItems(plan).filter((item) => item.status !== "Done")
}

/** One devis act a booking dialog should offer unprompted, with the step it is waiting on. */
export interface PlanStepSuggestion {
  plan: TreatmentPlanDto
  item: TreatmentPlanItemDto
  /** The séance to book, or null for an act with no protocol — then the whole act is the séance. */
  step: ReturnType<typeof nextStepOf>
  /**
   * Is this **treatment** already under way — has any of its work been delivered?
   *
   * <p>It decides the wording and the ranking. « Ce patient a un traitement en cours » about a devis accepted
   * last week with nothing done yet would be a small lie told by the one surface whose job is to remind somebody
   * of a fact they had forgotten; « un devis accepté » is the truth and just as useful.</p>
   *
   * <p>⚠️ <b>A question about the PLAN, not about the act this suggestion names</b> — and it was the act until
   * the suggestion became one row per treatment. Measured on the live database: devis 2026-0206 holds a
   * `Done` traitement de canal and its un-started continuation, so the act picked to be booked carried no
   * delivered work and the notice announced « un devis accepté » about a treatment the patient had already sat
   * through a séance for. `hasDeliveredWork` is the reader that already answers this per act; the plan's own
   * `InProgress` is beside it because the server sets that status when work is recorded, so the two cannot
   * disagree about a plan whose evidence lives on an act this reader cannot see.</p>
   */
  continuing: boolean
  /**
   * The slot already **passed without a fiche**, when the act's next séance is one — otherwise null, which is
   * the ordinary case of an act with nothing booked at all.
   *
   * <p>It is the reason this suggestion exists at all (see {@link suggestedPlanSteps}), and it must be stated
   * rather than acted on silently: a séance whose slot has gone by with nobody saying what happened may have
   * been carried out and not written up, or may never have happened. So the notice prints the date and the
   * dentist decides — it never claims the work was or was not done.</p>
   */
  passedSlotAt: string | null
}

/**
 * How many treatments a booking dialog may name at once.
 *
 * <p>Three, measured rather than chosen: on the live database 309 of 318 patients with a live treatment have
 * exactly one bookable treatment, 7 have two, and two patients have four and six. So three rows covers 316 of
 * 318 in full, and the two outliers are honestly summarised by {@link PlanSuggestionSet.hiddenCount} instead
 * of turning the reminder into a second acts picker.</p>
 */
export const MAX_PLAN_SUGGESTIONS = 3

/** What {@link suggestedPlanSteps} found: the treatments to name, and the ones that did not fit. */
export interface PlanSuggestionSet {
  /** At most {@link MAX_PLAN_SUGGESTIONS}, **one per live treatment**, most relevant first. */
  suggestions: PlanStepSuggestion[]
  /**
   * Live treatments with a bookable act that did **not** fit the cap.
   *
   * <p>⚠️ Carried on the same object as the list, deliberately, so that a surface rendering the suggestions
   * cannot forget it: a patient with six open treatments shown three rows and no « et 3 autres » is told a
   * silent half-truth by the surface whose entire job is to stop something being overlooked.</p>
   */
  hiddenCount: number
}

/**
 * The devis acts to **suggest** when a séance is being booked for this patient — one per live treatment, most
 * relevant first — or null when there is nothing to suggest.
 *
 * <p>The dentist books from the agenda, in a hurry, for a patient whose bridge is half done. Nothing on that path
 * mentioned the devis, so the séance was booked as a loose act and the plan reported the scellement as still
 * unplanned. This is what the dialog says out loud before he picks anything.</p>
 *
 * <p>⚠️ <b>A séance whose slot has PASSED without a fiche still counts, and excluding it was silencing the
 * reminder for exactly the patients who needed it.</b> The gate used to be `planItemState === "to-schedule"`
 * alone, whose stated reason — « accepting it would book the same step twice » — is sound for a booking that is
 * still to come and false for one that is over. Measured on the live database: of 318 patients carrying a live
 * treatment, <b>47 were shown nothing at all</b>, and in every one of the 47 the cause was this — each
 * schedulable act's next step held an `AwaitingClosure` visit whose slot had gone by. Not one of the 47 was
 * blocked by a future booking. So a future séance is still withheld (`"scheduled"`), a passed one is offered
 * with its date stated, and {@link PlanStepSuggestion.passedSlotAt} is what tells the two apart.</p>
 *
 * <p>⚠️ <b>One entry per TREATMENT, never per act.</b> The question this surface asks is « lequel
 * continuez-vous ? », and a devis with four unbooked acts would otherwise fill it with four rows about one
 * answer. Within a treatment the act is chosen by the same order the list itself uses, in clinical sequence.</p>
 *
 * <p>The order, and why each key is there:</p>
 * <ol>
 *   <li><b>Work already delivered outranks an untouched devis.</b> A patient with a half-finished bridge and a
 *   freshly accepted détartrage is being asked about the bridge.</li>
 *   <li><b>Nothing booked outranks a slot that passed unclosed.</b> An act with no séance in the agenda is
 *   unambiguously waiting to be booked; one whose slot went by may simply be missing its fiche, so it is the
 *   weaker offer and belongs lower.</li>
 *   <li><b>Due soonest first</b>, from `nextStepDueFrom` — the interval a clinician typed on the protocol, so
 *   it is a real clinical date and not a système-raised échéance. An act nobody dated sorts last and is never
 *   called late (see `InstallmentLateness` for the shape of that mistake).</li>
 *   <li><b>Read order</b> breaks what is left — plans arrive most-recently-created first — and the sort is
 *   stable, so that tie-break is kept rather than scrambled.</li>
 * </ol>
 *
 * <p>⚠️ It used to return a single suggestion, chosen `find(continuing) ?? candidates[0]` over plans read
 * `OrderByDescending(CreatedAt)` — so with two live treatments the dialog named the more recently created one
 * and said nothing whatever about the other. Measured: 6 patients have bookable acts on more than one live
 * treatment. The acts were all in the picker's « Actes du devis » group, which is the defence that made this
 * look harmless; but the group lists acts, and this surface is the only one that says « ce patient a un
 * traitement en cours » — so for those patients it said it about an arbitrary half of the truth.</p>
 */
export function suggestedPlanSteps(
  plans: readonly TreatmentPlanDto[],
  now: Date = new Date(),
): PlanSuggestionSet | null {
  const perPlan: PlanStepSuggestion[] = []

  for (const plan of plans) {
    const candidates: PlanStepSuggestion[] = []

    // Asked of the whole devis, once — see `PlanStepSuggestion.continuing` for the measured case where asking
    // it of the act being booked called a treatment already under way « un devis accepté ».
    const continuing =
      plan.status === "InProgress" || activeItems(plan).some(hasDeliveredWork)

    // The same gate the picker's group uses — a Draft or Cancelled devis contributes nothing, because booking
    // against a quote nobody accepted is not a shortcut, it is a mistake with a devis number on it.
    // ⚠️ Sorted on `sequenceNumber`, the clinical order the devis states, rather than trusting the order the
    // acts happen to arrive in: the act named here is the one the dentist is told to do next.
    const items = [...schedulablePlanItems(plan)].sort((a, b) => a.sequenceNumber - b.sequenceNumber)

    for (const item of items) {
      const state = planItemState(item, now)
      // « scheduled » is the one état still withheld: a séance that is genuinely still to come is already in
      // the agenda, and offering it would book the same step twice.
      if (state !== "to-schedule" && state !== "to-record") continue
      const step = nextStepOf(item)
      candidates.push({
        plan,
        item,
        step,
        continuing,
        passedSlotAt:
          state === "to-record" ? (step ? step.scheduledAt : item.scheduledAt) ?? null : null,
      })
    }

    if (candidates.length === 0) continue
    perPlan.push([...candidates].sort(byBookingUrgency)[0])
  }

  if (perPlan.length === 0) return null

  perPlan.sort(byBookingUrgency)
  return {
    suggestions: perPlan.slice(0, MAX_PLAN_SUGGESTIONS),
    hiddenCount: Math.max(0, perPlan.length - MAX_PLAN_SUGGESTIONS),
  }
}

/** The order {@link suggestedPlanSteps} documents — one comparator, used for the acts and for the treatments. */
function byBookingUrgency(a: PlanStepSuggestion, b: PlanStepSuggestion): number {
  if (a.continuing !== b.continuing) return a.continuing ? -1 : 1

  const aBookable = a.passedSlotAt == null
  const bBookable = b.passedSlotAt == null
  if (aBookable !== bBookable) return aBookable ? -1 : 1

  const aDue = dueFromMs(a.item)
  const bDue = dueFromMs(b.item)
  if (aDue !== bDue) return aDue - bDue

  // 0, so the caller's own order survives — `Array.prototype.sort` is stable.
  return 0
}

/** When the act's next step may be carried out, as a sortable number. Undated sorts last, never early. */
function dueFromMs(item: TreatmentPlanItemDto): number {
  const due = item.nextStepDueFrom
  return due ? new Date(due).getTime() : Number.POSITIVE_INFINITY
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
