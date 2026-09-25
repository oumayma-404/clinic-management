import type {
  AmendTreatmentPlanRequest,
  TreatmentPlanInstallmentInput,
  TreatmentPlanItemInput,
  TreatmentPlanItemStepInput,
} from "@/lib/api/treatment-plans"
import type { ProcedureTypeDto, TreatmentPlanDto, TreatmentPlanItemDto } from "@/lib/api/types"
import { catalogueLineCost, type SeedCandidate } from "@/components/odontogram-plan-seed"
import { formatAmount, formatDT, parseAmountInput, quoteFr } from "@/lib/format"
import { installmentDueLabel } from "./treatment-plan-labels"
import { actRemovalPlan } from "./plan-next-action"

/**
 * **The one builder of a devis amendment** — shared by the full form (« Tout modifier ») and the page's in-place
 * price editor (T3), so the two send the same payload for the same edit: every act echoed with its id, every
 * step with its id and `minDaysAfterPrevious`, the teeth, the échéancier only when it was edited, the version the
 * edit was read at. It was the body of `treatment-plan-form-modal`'s submit; nothing here is new logic.
 */

/** One proposed séance of a line. `duration` / `interval` are strings because they are typed into inputs. */
export interface PlanStepRow {
  /**
   * The existing step this row stands for, echoed back on an amendment so it keeps its id — and with it its
   * `doneDate`, its fiche link and any appointment booked for it. Null (or absent) means a new séance.
   */
  id?: string | null
  label: string
  duration: string
  /**
   * Calendar days to wait after the previous séance. A different quantity from `duration`: that one sizes the
   * appointment, this one decides when it is due — its absence is why the worklist alarmed on a flat fortnight.
   */
  interval?: string
  /** Unticked = not part of this devis. Kept in the list rather than removed, so it can be re-ticked. */
  include: boolean
  /** Already carried out. It cannot be unticked or removed — the aggregate refuses it, and rightly. */
  done?: boolean
}

/** One act line of the editor. */
export interface PlanLineRow {
  /**
   * The existing act this row stands for. Echoed back on save so the server keeps that act's id — otherwise
   * every edit re-issues the ids and silently orphans the appointment and fiche links pointing at them.
   */
  id: string | null
  /** The procedure this act is performed as — kept so booking the act preselects it. */
  procedureTypeId: string | null
  designationFr: string
  /** The charted diagnosis this row was seeded from. Display only — never sent. */
  diagnosisLabel?: string
  diagnosisCondition?: string
  /** The other acts that treat this line's diagnosis, best first. */
  candidates?: SeedCandidate[]
  plannedCost: string
  /**
   * Has the dentist typed this fee themselves? Until they do, it follows whichever act the row is set to — a
   * stored fee arrives `true`, because it is the number agreed with the patient.
   */
  costTouched: boolean
  /** The remise already granted on this act (its own command, read-only here) — the total is the fee minus it. */
  discount?: number
  toothNumbers: number[]
  /**
   * The séances this act is carried out over. ⚠️ Absent means « no protocol »; `[]` is a decision — « une seule
   * séance » — and is sent as `[]` so the server does not re-apply the protocol the dentist just declined.
   */
  steps?: PlanStepRow[]
  /** Has the dentist touched this act's séances? Until they do, the list follows the act chosen. */
  stepsTouched?: boolean
}

/** One échéance row of the editor. */
export interface PlanInstallmentRow {
  /**
   * The existing échéance this row revises. ⚠️ An échéance that has collected money must be echoed back by id or
   * the server refuses the amendment outright.
   */
  id: string | null
  dueDate: string
  amount: string
  /** Cash already collected on this échéance. 0 for a new row. Drives the « locked » affordance (AC-P2.6). */
  amountPaid: number
}

export type Built<T> = { ok: true; value: T } | { ok: false; error: string }

/**
 * A line's séances as one comparable string, over the fields that are actually sent — so « did the steps
 * change? » and « what will be saved » cannot answer differently. `null` for no protocol.
 */
export function stepSignature(steps: TreatmentPlanItemStepInput[] | undefined): string | null {
  // ⚠️ `[]` and « no steps » are the same stored fact — "" here made every step-less act look edited (F9).
  if (!steps || steps.length === 0) return null
  return steps
    .map((st) => `${st.id ?? ""}|${st.label.trim()}|${st.estimatedDurationMinutes ?? ""}|${st.minDaysAfterPrevious ?? ""}`)
    .join("~")
}

/** The same signature, computed from the stored act — the other half of the comparison. */
export function storedStepSignature(item: TreatmentPlanItemDto): string | null {
  const steps = item.steps ?? []
  if (steps.length === 0) return null
  return steps
    .map((st) => `${st.id}|${st.label.trim()}|${st.estimatedDurationMinutes ?? ""}|${st.minDaysAfterPrevious ?? ""}`)
    .join("~")
}

/** An act's protocol as proposed séances, all ticked — `undefined` when the act has none (most acts). */
export function proposedStepsFor(pt: ProcedureTypeDto | undefined): PlanStepRow[] | undefined {
  if (!pt?.defaultSteps || pt.defaultSteps.length === 0) return undefined
  return pt.defaultSteps.map((step) => ({
    id: null,
    label: step.label,
    duration: step.durationMinutes != null ? String(step.durationMinutes) : "",
    // The clinical interval travels with the label — see `ProcedureStepTemplateDto.minDaysAfterPrevious`.
    interval: step.minDaysAfterPrevious != null ? String(step.minDaysAfterPrevious) : "",
    include: true,
  }))
}

export const emptyPlanLine = (): PlanLineRow => ({
  id: null,
  procedureTypeId: null,
  designationFr: "",
  plannedCost: "",
  costTouched: false,
  toothNumbers: [],
})

/**
 * The fee a line shows for `pt` — a fee the dentist typed survives; otherwise it follows the act, per tooth
 * where the act charts a state (G4). An act with no default clears the field rather than keeping a stale one.
 */
export function repricedCost(line: PlanLineRow, pt: ProcedureTypeDto | undefined): string {
  if (line.costTouched) return line.plannedCost
  const cost = pt ? catalogueLineCost(pt, line.toothNumbers.length) : undefined
  return cost != null && cost > 0 ? formatAmount(cost) : ""
}

/** A new line for a catalogue act — the form's own pick on an empty line: the tarif, and its protocol proposed. */
export function lineFromProcedure(pt: ProcedureTypeDto): PlanLineRow {
  const base = emptyPlanLine()
  return {
    ...base,
    procedureTypeId: pt.id,
    designationFr: pt.name,
    plannedCost: repricedCost(base, pt),
    steps: proposedStepsFor(pt),
  }
}

/**
 * A stored plan's acts as editor lines. ⚠️ A parked act is not part of the devis' total and cannot be amended —
 * it comes back via « Remettre au devis » (F2). Every stored séance is hydrated `stepsTouched`: a stored protocol
 * is somebody's decision, and re-picking the act to fix its name must not re-propose the catalogue's.
 */
export function planLinesFromPlan(plan: TreatmentPlanDto): PlanLineRow[] {
  return plan.items
    .filter((it) => !it.isWithdrawn)
    .map((it) => ({
      id: it.id,
      procedureTypeId: it.procedureTypeId,
      designationFr: it.designationFr,
      plannedCost: formatAmount(it.plannedCost),
      // A stored fee is the number agreed with the patient, whatever the catalogue says today.
      costTouched: true,
      discount: it.discountAmount ?? 0,
      toothNumbers: it.toothNumbers,
      steps: (it.steps ?? []).map((st) => ({
        id: st.id,
        label: st.label,
        duration: st.estimatedDurationMinutes != null ? String(st.estimatedDurationMinutes) : "",
        interval: st.minDaysAfterPrevious != null ? String(st.minDaysAfterPrevious) : "",
        include: true,
        // A done séance cannot be dropped or re-ordered — the row holds the only link to its fiche.
        done: st.doneDate != null,
      })),
      stepsTouched: true,
    }))
}

/** A stored plan's échéancier as editor rows. */
export function planInstallmentRows(plan: TreatmentPlanDto): PlanInstallmentRow[] {
  return plan.installments.map((inst) => ({
    id: inst.id,
    dueDate: inst.dueDate.slice(0, 10),
    amount: formatAmount(inst.amount),
    amountPaid: inst.amountPaid,
  }))
}

/** What the patient owes for these lines: each fee minus the remise already granted on it (F2). */
export function planLinesTotal(lines: PlanLineRow[]): number {
  return (
    Math.round(
      lines.reduce((sum, l) => {
        const cost = parseAmountInput(l.plannedCost)
        return Number.isFinite(cost) ? sum + Math.max(0, cost - (l.discount ?? 0)) : sum
      }, 0) * 1000,
    ) / 1000
  )
}

/**
 * The title a devis takes when none is typed — the act's name, or « Plan de traitement » for several. A stored
 * « … + 1 acte » would go stale the day a third act is added; `treatmentName` derives the display name instead.
 */
export function derivedPlanTitle(lines: PlanLineRow[]): string {
  const named = lines.filter((l) => l.designationFr.trim() !== "")
  if (named.length === 0) return ""
  return named.length === 1 ? named[0].designationFr.trim() : "Plan de traitement"
}

/**
 * The lines as the server takes them — refusing a blank existing act (removal is the bin's job, F8) and an
 * invalid fee, and dropping a blank new line (a row added and left empty is not a decision).
 */
export function parsePlanLines(lines: PlanLineRow[]): Built<TreatmentPlanItemInput[]> {
  if (lines.some((l) => l.id && l.designationFr.trim() === "")) {
    return { ok: false, error: "Donnez un nom à chaque acte — pour en retirer un, utilisez la corbeille." }
  }

  const parsed: TreatmentPlanItemInput[] = lines
    .map((l) => ({
      id: l.id,
      procedureTypeId: l.procedureTypeId,
      designationFr: l.designationFr.trim(),
      plannedCost: parseAmountInput(l.plannedCost),
      toothNumbers: l.toothNumbers,
      /*
       * Tri-state at every point: no protocol → `undefined` (the server applies the procedure's own); some
       * ticked → the ticked ones, in order; none ticked → `[]`, an explicit « une seule séance ».
       */
      steps: l.steps
        ? l.steps
            .filter((st) => st.include && st.label.trim() !== "")
            .map((st) => ({
              // Echoed back so an existing séance keeps its identity — its date, its fiche, its booking.
              id: st.id ?? null,
              label: st.label.trim(),
              estimatedDurationMinutes: st.duration.trim() === "" ? null : Number(st.duration),
              minDaysAfterPrevious: !st.interval || st.interval.trim() === "" ? null : Number(st.interval),
            }))
        : undefined,
    }))
    .filter((l) => l.designationFr !== "")

  if (parsed.length === 0) return { ok: false, error: "Ajoutez au moins un acte." }
  for (const l of parsed) {
    if (!Number.isFinite(l.plannedCost) || l.plannedCost < 0) {
      return { ok: false, error: `Prix invalide pour ${quoteFr(l.designationFr)}.` }
    }
  }
  return { ok: true, value: parsed }
}

/**
 * The échéancier as the server takes it — or `[]` when it is not sent (an amendment sends it only when edited:
 * re-sending it unchanged bumped the révision on a no-op save, F9, and a changed total is re-spread server-side
 * with the agreed dates kept). The last row absorbs the remainder so the schedule sums exactly to the total.
 */
export function parsePlanInstallments(
  rows: PlanInstallmentRow[],
  total: number,
  sendSchedule: boolean,
): Built<TreatmentPlanInstallmentInput[]> {
  if (!sendSchedule) return { ok: true, value: [] }

  // Lowering the total can leave nothing for the later rows: unpaid rows at the end give way (F10).
  const working = [...rows]
  const typedSum = (list: PlanInstallmentRow[]) =>
    list.slice(0, -1).reduce((sum, r) => {
      const a = parseAmountInput(r.amount)
      return sum + (Number.isFinite(a) ? a : 0)
    }, 0)
  while (working.length > 1 && working[working.length - 1].amountPaid <= 0 && total - typedSum(working) < -0.0005) {
    working.pop()
  }
  if (working.length === 0) return { ok: true, value: [] }

  for (const r of working) {
    if (!r.dueDate) return { ok: false, error: "Chaque échéance doit avoir une date." }
  }
  const amounts = working.map((r) => parseAmountInput(r.amount))
  for (let i = 0; i < amounts.length - 1; i++) {
    if (!Number.isFinite(amounts[i]) || amounts[i] < 0) return { ok: false, error: "Montant d'échéance invalide." }
  }
  const allButLast = amounts.slice(0, -1).reduce((s, a) => s + (Number.isFinite(a) ? a : 0), 0)
  const lastAmount = Math.round((total - allButLast) * 1000) / 1000
  if (lastAmount < 0) return { ok: false, error: "Le total des échéances dépasse le montant du plan." }

  let parsed: TreatmentPlanInstallmentInput[] = working.map((r, i) => ({
    // Echoing the id is what lets the server REVISE an échéance — mandatory for one that has collected money.
    id: r.id,
    dueDate: `${r.dueDate}T00:00:00`,
    amount: i === amounts.length - 1 ? lastAmount : parseAmountInput(r.amount),
  }))

  // AC-P2.6: refuse locally what the server refuses anyway, but name the row.
  for (let i = 0; i < working.length; i++) {
    const row = working[i]
    if (row.amountPaid > 0 && parsed[i].amount < row.amountPaid - 0.0005) {
      return {
        ok: false,
        error:
          `${installmentDueLabel({ dueDate: row.dueDate })} : déjà payé ${formatDT(row.amountPaid)} — `
          + "son montant ne peut pas être ramené en dessous.",
      }
    }
  }
  // An unpaid row left at 0 is not an échéance — the aggregate refuses one, so it is dropped (F10).
  const kept = parsed.filter((r, i) => r.amount > 0.0005 || working[i].amountPaid > 0)
  if (kept.length > 0) parsed = kept
  return { ok: true, value: parsed }
}

/** What an amendment is built from — the form's state, or the page's lines with its in-place edits applied. */
export interface PlanAmendDraft {
  lines: PlanLineRow[]
  installments: PlanInstallmentRow[]
  installmentsTouched: boolean
  title: string
  notes: string
  /** The page's in-place editor: a blank stored title is left blank (the amend reads an omitted title as unchanged). */
  inPlace?: boolean
}

/**
 * The amendment for `draft` against `plan`: additions, in-place corrections (only CHANGED lines — re-sending an
 * unchanged act would bump the révision a patient's printout is identified by), removals, the échéancier when
 * edited, the title, the notes and the version. Refuses, in the form's own order and words, what the server
 * would refuse.
 */
export function buildAmendRequest(
  plan: TreatmentPlanDto,
  draft: PlanAmendDraft,
  version: number,
): Built<Omit<AmendTreatmentPlanRequest, "refundMethod">> {
  // The title is derived rather than demanded — it was once the sole required field.
  const effectiveTitle = draft.title.trim() || derivedPlanTitle(draft.lines)
  if (!effectiveTitle) return { ok: false, error: "Ajoutez au moins un acte, ou saisissez un titre." }

  const lines = parsePlanLines(draft.lines)
  if (!lines.ok) return lines
  const parsedLines = lines.value

  const sendSchedule = draft.installmentsTouched
  const schedule = parsePlanInstallments(draft.installments, planLinesTotal(draft.lines), sendSchedule)
  if (!schedule.ok) return schedule

  const originalIds = new Set(plan.items.filter((i) => !i.isWithdrawn).map((i) => i.id))
  const keptIds = new Set(parsedLines.map((l) => l.id).filter((id): id is string => !!id))
  const removeItemIds = [...originalIds].filter((id) => !keptIds.has(id))

  // `actRemovalPlan` is the single mirror of `TreatmentPlan.EnsureItemRemovable` (N38).
  for (const id of removeItemIds) {
    const item = plan.items.find((i) => i.id === id)
    const removal = item ? actRemovalPlan(plan, item) : null
    if (removal && !removal.removable) return { ok: false, error: removal.reason }
  }

  // Rows with no id are additions; rows with one are corrections IN PLACE — the id preserved, so every
  // appointment and fiche link survives (remove-then-add cannot, and is refused for a done or booked act).
  const addItems = parsedLines.filter((l) => !l.id)
  const updateItems = parsedLines.filter((l) => {
    if (!l.id) return false
    const before = plan.items.find((i) => i.id === l.id)
    if (!before) return false
    return (
      l.designationFr.trim() !== before.designationFr.trim() ||
      Math.abs(l.plannedCost - before.plannedCost) > 0.0005 ||
      (l.procedureTypeId ?? null) !== (before.procedureTypeId ?? null) ||
      l.toothNumbers.join(",") !== before.toothNumbers.join(",") ||
      // ⚠️ The steps too, compared on the shape actually sent — a steps-only edit was once dropped.
      stepSignature(l.steps) !== storedStepSignature(before)
    )
  })

  const retitling = draft.title.trim() !== "" && draft.title.trim() !== plan.title
  const renoting = (draft.notes.trim() || null) !== (plan.notes ?? null)

  if (
    addItems.length === 0 &&
    updateItems.length === 0 &&
    removeItemIds.length === 0 &&
    !(sendSchedule && draft.installments.length > 0) &&
    !retitling &&
    !renoting
  ) {
    return { ok: false, error: "Aucune modification demandée." }
  }

  // A paid échéance dropped from an edited schedule would erase that cash; the server refuses it.
  const droppedPaidRow =
    sendSchedule &&
    plan.installments.some((inst) => inst.amountPaid > 0 && !draft.installments.some((r) => r.id === inst.id))
  if (droppedPaidRow) {
    return {
      ok: false,
      error: "Une échéance déjà payée ne peut pas être supprimée de l'échéancier. Conservez-la et ajustez les autres.",
    }
  }

  return {
    ok: true,
    value: {
      addItems,
      updateItems,
      removeItemIds,
      installments: schedule.value,
      title: draft.inPlace && !draft.title.trim() ? undefined : effectiveTitle,
      // Tri-state server-side and always sent: an unchanged note is not counted as an amendment.
      notes: draft.notes.trim() || null,
      // The row's version as last read, so a peer's edit 409s instead of being overwritten.
      version,
    },
  }
}
