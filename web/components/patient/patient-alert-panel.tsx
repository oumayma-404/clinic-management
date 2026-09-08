"use client"

import { AlertTriangle } from "lucide-react"
import { cn } from "@/lib/utils"
import { isActiveSmoker, tobaccoSummary } from "@/lib/tobacco"
import type { PatientDto } from "@/lib/api/types"

/** True when this patient carries anything the panel would show — so a caller can decide layout before rendering. */
export function hasPatientAlerts(patient: PatientDto | null | undefined): boolean {
  if (!patient) return false
  return (
    Boolean(patient.allergies?.trim()) ||
    Boolean(patient.medicalHistory?.trim()) ||
    isActiveSmoker(patient.tobaccoUse)
  )
}

interface PatientAlertPanelProps {
  patient: PatientDto
  className?: string
}

/**
 * « Alertes médicales » — allergies, tabac and chronic conditions, in one read-only panel.
 *
 * ## Why it is shared
 *
 * This existed exactly once, inline in `patient-record-modal`, and the two other surfaces where the same decision
 * is taken did not have it:
 *
 * - the **document editor**, which is where an ordonnance is written. It read `patient.allergies` nowhere at all,
 *   so prescribing Clamoxyl or Augmentin — both carrying `Amoxicilline` as a structured DCI in the seeded
 *   catalogue — to a penicillin-allergic patient raised nothing;
 * - the **patient summary modal**, the one-click quick look from the patients list and the phone ⋯ menu, which
 *   omitted allergies and antécédents entirely while the full page and the fiche modal both showed them.
 *
 * That is this codebase's dominant defect shape — a correct answer wired to one call site — so the answer is a
 * component, not a third copy. Anything that shows a patient in a clinical context renders this.
 *
 * ## What it is not
 *
 * Not a check and not a gate. It surfaces the free-text the practitioner wrote; a real DCI-vs-allergy block needs
 * structured allergies and is deliberately out of scope. It is also read-only everywhere: allergies are corrected
 * in the patient's file, and an editable copy on three surfaces is three ways to disagree.
 *
 * Allergies are `text-destructive` while the antécédents stay amber — within one warning panel the two are not the
 * same weight, and the allergy is the line that stops an injection.
 */
export function PatientAlertPanel({ patient, className }: PatientAlertPanelProps) {
  const allergies = patient.allergies?.trim()
  const medicalHistory = patient.medicalHistory?.trim()
  /*
   * ⚠️ A **current** smoker only. « Non-fumeur » is reassurance and « Ancien fumeur » is history, and a warning
   * panel that fires on every patient is a panel the eye learns to skip — both stay readable in the patient's
   * own file. It replaced the retired « signalement » badges here: those were a second, weaker mechanism for the
   * same job as `importantNotes`, while this is a fact that bears on the decision being taken on these three
   * surfaces (healing, implant survival, periodontal work).
   */
  const tobacco = isActiveSmoker(patient.tobaccoUse) ? tobaccoSummary(patient.tobaccoUse) : null

  if (!allergies && !medicalHistory && !tobacco) return null

  return (
    <div
      className={cn(
        "rounded-lg border border-amber-300 bg-amber-50 p-3 dark:border-amber-800 dark:bg-amber-950/40",
        className,
      )}
    >
      <p className="flex items-center gap-1.5 text-sm font-semibold text-amber-800 dark:text-amber-200">
        <AlertTriangle className="h-4 w-4" aria-hidden="true" /> Alertes médicales
      </p>
      <div className="mt-2 space-y-1.5 text-xs">
        {allergies && (
          <p className="text-destructive">
            <span className="font-semibold">Allergies :</span> {allergies}
          </p>
        )}
        {tobacco && (
          <p className="text-amber-800 dark:text-amber-200">
            <span className="font-semibold">Tabac :</span> {tobacco}
          </p>
        )}
        {medicalHistory && (
          <p className="text-amber-800 dark:text-amber-200">
            <span className="font-semibold">Antécédents :</span> {medicalHistory}
          </p>
        )}
      </div>
    </div>
  )
}
