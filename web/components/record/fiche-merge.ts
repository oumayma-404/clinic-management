import { chequePaymentFields, type ChequeFieldsValue } from "@/components/factures/cheque-fields"
import { isActNamed, sessionActsOf, type SessionAct } from "@/components/record/use-session-acts"
import type { DentalRecordDto } from "@/lib/api/types"
import { prescriptionKind, type PrescriptionLine } from "@/lib/documents"
import { formatAmount, parseAmountInput, quoteFr, roundMillimes, toLocalIso } from "@/lib/format"

/**
 * Reconciling an open fiche de soins with a copy of it that somebody else saved in the meantime.
 *
 * <p>Every save round-trips the row's `xmin`, so a colleague's save turns ours into a 409 and the dialog offers
 * « Recharger ». That button used to take the server's <b>version only</b> and leave everything on screen, so the
 * next « Enregistrer » sent the old acts, prices and notes under a version that now matched — the colleague's
 * work was overwritten with a green toast, which is the lost update the token exists to stop
 * (`use-fresh-version.ts` says « do not `resync()` after a 409 » in as many words). The opposite answer — reload
 * everything, as most dialogs do — throws away a whole séance of typing on the longest form in the product.</p>
 *
 * <p>So each section is compared three ways: the copy the form was opened with (or last reconciled with), what is
 * on screen, and the server's copy now. Only a section <b>both</b> people changed, differently, loses what was
 * typed — and it is named, which is what the server's own sentence asks for (« rechargez … puis appliquez à
 * nouveau votre modification »).</p>
 */

/** A fiche with no method recorded is cash — every row written before the field existed is, and the server agrees. */
export const CASH_METHOD = "Cash"

/**
 * The parts of a fiche reconciled as one unit. Fields that only make sense together share a section, so a merge
 * can never assemble a fiche nobody wrote — the devis link decides which act is priced at 0, so it travels with
 * the acts; « Payé » is checked against the act total and the method, so they travel together.
 */
export type FicheSection = "date" | "acts" | "payment" | "notes"

/** Iteration order of the sections — and, through `Record<FicheSection, …>`, the proof that each has a loader. */
export const FICHE_SECTIONS: readonly FicheSection[] = ["date", "acts", "payment", "notes"]

/** The ordonnance is reconciled too, by its own effect: its lines live on two documents, not on the fiche row. */
export type ReconciledPart = FicheSection | "prescription"

const PART_LABEL: Record<ReconciledPart, string> = {
  date: "Date",
  acts: "Actes",
  payment: "Paiement",
  notes: "Notes",
  prescription: "Ordonnance",
}

/** The sentence shown when sections were taken over, or null when nothing was. */
export function takenOverNotice(parts: readonly ReconciledPart[]): string | null {
  if (parts.length === 0) return null
  const names = parts.map((p) => quoteFr(PART_LABEL[p])).join(", ")
  return `Modifié entre-temps par quelqu'un d'autre : ${names}. Sa version est affichée — refaites-y votre modification.`
}

/**
 * Every key of the fiche's save payload (`recordData` in `patient-record-modal.tsx`), and the section that keeps it
 * reconciled. `check:responsive` derives the payload's keys and fails on one missing here, so a field added to the
 * save cannot quietly skip the merge — skipping it is the original defect, for that one field.
 *
 * `derived` means the value is computed at save time from things that ARE reconciled, or is never sent on an edit.
 */
export const FICHE_PAYLOAD_SECTION = {
  interventionDate: "date",
  amountPaid: "payment",
  paymentMethod: "payment",
  "...chequePaymentFields": "payment",
  amountCollectedOnPlan: "payment",
  notes: "notes",
  importantNotes: "notes",
  acts: "acts",
  // Both read off `linkedPlanItemId`, which is part of the acts section.
  treatmentPlanId: "acts",
  treatmentPlanItemId: "acts",
  // A display hint computed from the acts' teeth (or the arch shown when there are none).
  isAdultTeeth: "derived",
  // The acts flagged `addToPlan`, which `markAddToPlan` derives from the devis link.
  planActAdditions: "derived",
  // Read off the booked visit, or a step picked on a NEW fiche — a reopened fiche has neither.
  treatmentPlanItemStepId: "derived",
  additionalTreatmentPlanItems: "derived",
  // Sent on create only.
  appointmentId: "derived",
  prescription: "prescription",
} as const satisfies Record<string, ReconciledPart | "derived">

/** What the form holds for the reconciled sections — read from a stored fiche, or from the form's own state. */
export interface FicheFormValues {
  interventionDate: string
  acts: SessionAct[]
  /** The devis act this fiche carries, or null. Only an act the form can offer counts — see `ficheFormValuesOf`. */
  planItemId: string | null
  amountPaid: string
  paymentMethod: string
  cheque: ChequeFieldsValue
  collectedOnPlan: string
  notes: string[]
  importantNotes: string[]
}

/**
 * A stored fiche as the form shows it — the ONE reading, used to hydrate on open and to take a section over.
 *
 * @param offeredPlanItemIds The devis acts the form can offer. A stored link to anything else is shown as no link
 *   (the « Changer » menu could not select it), so it is read as null here too — otherwise every such fiche would
 *   look edited the moment it opened.
 */
export function ficheFormValuesOf(record: DentalRecordDto, offeredPlanItemIds: ReadonlySet<string>): FicheFormValues {
  const link = record.treatmentPlanItemId ?? null
  return {
    // The stored instant was once round-tripped through UTC, so a fiche saved late in the evening reopened on the
    // previous calendar day — and re-saving wrote that day back.
    interventionDate: toLocalIso(new Date(record.interventionDate)),
    acts: sessionActsOf(record),
    planItemId: link && offeredPlanItemIds.has(link) ? link : null,
    // `formatAmount`, never `String(...)` (J8) — the field accepts the comma form the product prints with.
    amountPaid: formatAmount(record.amountPaid),
    /*
     * ⚠️ **Hydrated, and the note that used to say « deliberately NOT hydrated » was reasoning from a premise
     * that has since become false.** It argued that what a fiche collected onto a treatment lives on the
     * plan's échéancier (`InstallmentPayment.DentalRecordId`) and that this modal does not read the plan's
     * payments — true when it was written, and `DentalRecordDto.CollectedOnTreatment` has carried exactly that
     * figure on every read since (`GetDentalRecordsQuery` derives it from the ledger), so the value is in hand.
     *
     * It also called an empty field « safe rather than lossy », and that is the half that was wrong. This field
     * is **cumulative for the séance** — the server collects `typed − already` — so showing 0 on a séance that
     * took 200 DT makes every reading of it false and every edit of it wrong: type the real 200 and the delta
     * is 0, so nothing happens and the fiche appears not to save; type anything lower and the save is refused
     * with « 200,000 DT ont déjà été encaissés … ». Reported in exactly those words.
     */
    collectedOnPlan: record.collectedOnTreatment ? formatAmount(record.collectedOnTreatment) : "",
    paymentMethod: record.paymentMethod ?? CASH_METHOD,
    cheque: {
      number: record.chequeNumber ?? "",
      bankName: record.chequeBankName ?? "",
      // The stored value is a calendar day; slice rather than re-parse, so no timezone touches it.
      dueDate: record.chequeDueDate ? record.chequeDueDate.slice(0, 10) : "",
    },
    notes: [...record.notes],
    importantNotes: [...record.importantNotes],
  }
}

/**
 * Act fields that are not the dentist's statement, so comparing them would call an untouched fiche « edited ».
 * Everything else is compared — a field added to `SessionAct` is covered the day it is declared.
 */
const UNCOMPARED_ACT_FIELDS: ReadonlySet<keyof SessionAct> = new Set<keyof SessionAct>([
  // The card's own bookkeeping.
  "key",
  "picking",
  "unitCostLocked",
  "perToothLocked",
  "bridgeRolesAnswered",
  // Derived from the devis link (compared on its own) by `markBilledOnPlan` / `markCarriedOnPlan` /
  // `markAddToPlan`; `actFromDto` always reads them false, so every devis-carried fiche would differ on open.
  "billedOnPlan",
  "addToPlan",
])

/** A typed amount as the save reads it; text that does not parse stays text, so it still counts as a change. */
const typedAmount = (typed: string) => {
  const value = parseAmountInput(typed)
  return Number.isFinite(value) ? roundMillimes(value) : typed.trim()
}

/** « Payé »-style fields: the save sends `parseAmountInput(x) || 0`. */
const moneyOf = (typed: string) => roundMillimes(parseAmountInput(typed) || 0)

const keptLines = (lines: readonly string[]) => lines.map((l) => l.trim()).filter(Boolean)

function canonicalAct(act: SessionAct): Record<string, unknown> {
  const out: Record<string, unknown> = {}
  for (const field of (Object.keys(act) as (keyof SessionAct)[]).sort()) {
    if (UNCOMPARED_ACT_FIELDS.has(field)) continue
    const value = act[field]
    if (value instanceof Set) out[field] = [...value].sort()
    else if (field === "unitCost") out[field] = typedAmount(act.unitCost)
    // What the save sends as `isPerTooth`: a per-tooth price with no tooth is not one.
    else if (field === "perTooth") out[field] = act.perTooth && act.toothNumbers.length > 0
    else if (typeof value === "string") out[field] = value.trim()
    else out[field] = value
  }
  return out
}

/** One comparable string per section. Both sides of a comparison always go through this one function. */
export function ficheSnapshot(values: FicheFormValues): Record<FicheSection, string> {
  return {
    date: values.interventionDate,
    acts: JSON.stringify({
      // A blank trailing card is not saved, so it is not compared either.
      acts: values.acts.filter(isActNamed).map(canonicalAct),
      planItemId: values.planItemId,
    }),
    payment: JSON.stringify({
      amountPaid: moneyOf(values.amountPaid),
      collectedOnPlan: moneyOf(values.collectedOnPlan),
      paymentMethod: values.paymentMethod,
      // The save's own builder: cheque details typed under another method are never sent, so never compared.
      ...chequePaymentFields(values.paymentMethod, values.cheque),
    }),
    notes: JSON.stringify({ notes: keptLines(values.notes), important: keptLines(values.importantNotes) }),
  }
}

/** The ordonnance's lines as the save sends them: named lines only, blanks and empty extras ignored. */
export function prescriptionSnapshot(lines: readonly PrescriptionLine[]): string {
  return JSON.stringify(
    lines
      .filter((line) => line.name?.trim())
      .map((line) => {
        const out: Record<string, unknown> = {}
        for (const [field, value] of Object.entries({ ...line, kind: prescriptionKind(line.kind) }).sort()) {
          const kept = typeof value === "string" ? value.trim() : value
          if (kept === "" || kept == null || (Array.isArray(kept) && kept.length === 0)) continue
          out[field] = kept
        }
        return out
      }),
  )
}

/**
 * What to do with one section.
 *
 * - `keep` — the server agrees with the screen, or nobody else touched it: what is on screen stays.
 * - `take` — only the other person changed it: their version replaces an untouched section, silently.
 * - `takeOver` — both changed it, differently: theirs is shown and the section is named, so the change is re-made
 *   knowingly rather than written over theirs blind.
 */
export type MergeVerdict = "keep" | "take" | "takeOver"

export function mergeVerdict(opened: string, onScreen: string, server: string): MergeVerdict {
  if (server === onScreen || server === opened) return "keep"
  return onScreen === opened ? "take" : "takeOver"
}
