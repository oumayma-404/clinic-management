/**
 * French labels for `PatientFlagType` — the display half of the repo's standing **English storage key, French
 * display map** convention (`lib/specialties.ts`, `components/appointment-labels.ts`,
 * `components/factures/invoice-labels.ts`).
 *
 * <p>Without it the raw backend enum was rendered verbatim inside a red `destructive` Badge, so a dentist opening
 * a file read « HighPriority » next to the patient's name — in the one place on the page whose whole job is to be
 * understood at a glance, on a screen that is otherwise entirely in French. It appeared in three places at once
 * (the patients table, its mobile card, and the patient page's title row), which is exactly why this is a shared
 * map and not three inline ternaries.</p>
 *
 * <p>⚠️ **Never rename a key.** `PatientFlag.FlagType` is persisted as the enum's name, so the keys below are what
 * is already in the database; this maps at display time only. An unknown value is **passed through verbatim**
 * rather than replaced with a placeholder — a clinic on an older row, or a flag type added server-side before this
 * map catches up, must still show *something* a human can act on. A patient safety marker is the last badge that
 * should ever render as « Inconnu ».</p>
 */
const PATIENT_FLAG_LABELS_FR: Record<string, string> = {
  HighPriority: "Priorité haute",
  SpecialCondition: "Situation particulière",
  Alert: "Alerte",
  Critical: "Critique",
  Allergy: "Allergie",
}

/** The French label for a stored flag type, or the raw value when it is not one we know. */
export function patientFlagLabel(flagType: string | null | undefined): string {
  if (!flagType) return ""
  return PATIENT_FLAG_LABELS_FR[flagType] ?? flagType
}
