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
  /**
   * What decision this copy is standing next to.
   *
   * <p><b>`clinical`</b> (the default, and byte-identical to what every existing caller already renders) is the
   * panel read before touching the patient: allergies, tabac, maladies, médicaments.</p>
   *
   * <p><b>`prescribing`</b> is the copy inside the fiche's « Prescription » section, asked for because the
   * alerts sit at the top of a dialog that scrolls — by the time a dentist is typing « Augmentin » the block
   * naming a penicillin allergy is off screen, and this panel's own doc already says an ordonnance is exactly
   * where it belongs. It carries the three facts that change <i>what may be prescribed</i> and titles itself
   * with the reason.</p>
   *
   * <p>⚠️ <b>Tabac is dropped there deliberately, and it is the only thing dropped.</b> It earns its place in
   * the clinical panel for healing, implant survival and periodontal work — none of which is a question about
   * a prescription — and a warning that fires on facts the reader cannot act on is how the eye learns to skip
   * the whole block, which would cost the allergy line beside it. It stays in full in the panel above and in
   * the patient's file.</p>
   */
  purpose?: "clinical" | "prescribing"
}

/** True when the prescribing copy would show anything — the three facts that bear on what may be prescribed. */
export function hasPrescribingAlerts(patient: PatientDto | null | undefined): boolean {
  if (!patient) return false
  return (
    Boolean(patient.allergies?.trim()) ||
    Boolean(patient.medicalHistory?.trim()) ||
    Boolean(patient.medications?.trim())
  )
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
export function PatientAlertPanel({
  patient,
  className,
  purpose = "clinical",
}: PatientAlertPanelProps) {
  const prescribing = purpose === "prescribing"
  const allergies = patient.allergies?.trim()
  const medicalHistory = patient.medicalHistory?.trim()
  /*
   * ⚠️ « Médicaments » had to reach THIS panel, not only the patient's file. This is the block the fiche de
   * soins, the document editor and the summary modal each render before work is recorded — it is where « sous
   * anticoagulants » was being written by hand into `importantNotes` because there was no field for it. Adding
   * it to the record without adding it here would have left the note as the only thing these three surfaces see.
   */
  const medications = patient.medications?.trim()
  /*
   * ⚠️ A **current** smoker only. « Non-fumeur » is reassurance and « Ancien fumeur » is history, and a warning
   * panel that fires on every patient is a panel the eye learns to skip — both stay readable in the patient's
   * own file. It replaced the retired « signalement » badges here: those were a second, weaker mechanism for the
   * same job as `importantNotes`, while this is a fact that bears on the decision being taken on these three
   * surfaces (healing, implant survival, periodontal work).
   */
  const tobacco =
    !prescribing && isActiveSmoker(patient.tobaccoUse) ? tobaccoSummary(patient.tobaccoUse) : null

  if (!allergies && !medicalHistory && !medications && !tobacco) return null

  return (
    <div
      className={cn(
        "rounded-lg border border-amber-300 bg-amber-50 p-3 dark:border-amber-800 dark:bg-amber-950/40",
        className,
      )}
    >
      {/* ⚠️ The prescribing copy names the DECISION, not the category. « Alertes médicales » twice in one
          scrolling dialog reads as the same block repeated — the complaint this product has already answered
          once, on the fiche's own banner — while « À vérifier avant de prescrire » says why this copy is
          here and is the sentence a dentist actually needs at that moment. */}
      <p className="flex items-center gap-1.5 text-sm font-semibold text-amber-800 dark:text-amber-200">
        <AlertTriangle className="h-4 w-4" aria-hidden="true" />{" "}
        {prescribing ? "À vérifier avant de prescrire" : "Alertes médicales"}
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
        {/* ⚠️ « Maladies », not « Antécédents ». This label was the third name one column carried — the patient
            file called it « Maladies chroniques / affections », the form called it that too, and here it was
            « Antécédents », which is ALSO the name of a different list from a different table rendered a few
            centimetres away on that page. One column, one word. */}
        {medicalHistory && (
          <p className="text-amber-800 dark:text-amber-200">
            <span className="font-semibold">Maladies :</span> {medicalHistory}
          </p>
        )}
        {medications && (
          <p className="text-amber-800 dark:text-amber-200">
            <span className="font-semibold">Médicaments :</span> {medications}
          </p>
        )}
      </div>
    </div>
  )
}
