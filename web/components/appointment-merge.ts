import type { SelectedAct } from "@/components/appointment-acts-picker"
import { parseAmountInput, roundMillimes } from "@/lib/format"

/**
 * The parts of an RDV reconciled as one unit by « Recharger » (`lib/forms/form-merge.ts`). Fields that only make sense
 * together share a section: the date, the start and the length are one decision, as are the acts and their prices.
 */
export type AppointmentSection = "when" | "status" | "doctor" | "acts" | "notes"

export const APPOINTMENT_SECTIONS: readonly AppointmentSection[] = ["when", "status", "doctor", "acts", "notes"]

export const APPOINTMENT_SECTION_LABEL: Record<AppointmentSection, string> = {
  when: "Date et heure",
  status: "Statut",
  doctor: "Praticien",
  acts: "Actes",
  notes: "Notes",
}

export interface AppointmentValues {
  day: string
  start: string
  minutes: number
  status: string
  doctorId: string
  acts: readonly SelectedAct[]
  notes: string
}

/** A typed price as the save reads it; text that does not parse stays text, so it still counts as a change. */
function priceOf(typed: string | undefined): number | string | null {
  if (typed === undefined || typed.trim() === "") return null
  const value = parseAmountInput(typed)
  return Number.isFinite(value) ? roundMillimes(value) : typed.trim()
}

/**
 * An act as the user stated it. Derived fields (`billedOnPlan`, `stepOptions`, labels) are left out — they are filled
 * in later by the devis read, so comparing them would call an untouched visit « edited ».
 */
function canonicalAct(act: SelectedAct) {
  return {
    procedureTypeId: act.procedureTypeId ?? null,
    treatmentPlanItemId: act.treatmentPlanItemId ?? null,
    treatmentPlanItemStepId: act.treatmentPlanItemStepId ?? null,
    agreedCost: priceOf(act.agreedCost),
    plannedProtocol: act.plannedProtocol ? act.plannedProtocol.map((s) => s.label) : null,
    continuation: act.pendingContinuation ? JSON.stringify(act.pendingContinuation) : null,
  }
}

export function appointmentSnapshot(values: AppointmentValues): Record<AppointmentSection, string> {
  return {
    when: JSON.stringify([values.day, values.start, values.minutes]),
    status: values.status.toLowerCase(),
    doctor: values.doctorId || "",
    acts: JSON.stringify(values.acts.map(canonicalAct)),
    notes: values.notes.trim(),
  }
}
