import { normalizeStatus } from "@/components/appointment-labels"
import { localDayIso } from "@/lib/format"

/**
 * **The one door to a visit's fiche de soins** (`seance-au-fauteuil`).
 *
 * <p>A dentist fills the fiche while the patient is in the chair, so every surface that offers to RECORD a visit —
 * the agenda block, the RDV window, « Au fauteuil », the patient's « Rendez-vous » tab, a treatment's séance
 * strip — offers it from the visit's day, not from the end of its slot. They used to disagree: four opened at
 * the slot's start, five only after it ended, and the agenda (where the dentist actually is) had none at all.
 * The reminders — the post-visit prompt, « À clôturer » — still wait for the end: they exist to catch a fiche
 * nobody wrote, which is a question only the end of the séance can ask.</p>
 *
 * <p>⚠️ `ficheHref` is the only composer of `?addRecord=1`: the patient page reads that link, and opens the
 * visit's existing fiche when there is one, so no door can create a second fiche for a visit by building the
 * link its own way. `check:responsive`'s `fiche-door-has-one-composer` holds it.</p>
 */

/** A visit, as far as « may its fiche be written now? » needs it. */
export interface FicheDoorVisit {
  patientId: string | null
  status: string
  appointmentDateTime: string
}

const CLOSED_WITHOUT_A_SEANCE = new Set(["Cancelled", "NoShow"])

/**
 * True when the visit's fiche may be written now: a patient visit, not cancelled or missed, on today or earlier.
 * Today counts from its first minute — a patient seen early is still this visit.
 */
export function canRecordFiche(visit: FicheDoorVisit, todayIso: string): boolean {
  if (!visit.patientId) return false
  if (CLOSED_WITHOUT_A_SEANCE.has(normalizeStatus(visit.status))) return false
  const day = localDayIso(visit.appointmentDateTime)
  return day !== "" && day <= todayIso
}

/** True while the séance has not been closed: the fiche offers « Terminer la séance » rather than only « Enregistrer ». */
export function isSeanceRunning(status: string | null | undefined): boolean {
  if (!status) return false
  const normalized = normalizeStatus(status)
  return normalized === "Scheduled" || normalized === "Confirmed" || normalized === "InProgress"
}

/**
 * The patient page, opening the fiche of this visit — its existing fiche when there is one, a new one otherwise.
 * With no visit, a new fiche for the patient.
 */
export function ficheHref(patientId: string, appointmentId?: string | null): string {
  const visit = appointmentId ? `&appointmentId=${encodeURIComponent(appointmentId)}` : ""
  return `/patients/${encodeURIComponent(patientId)}?addRecord=1${visit}`
}
