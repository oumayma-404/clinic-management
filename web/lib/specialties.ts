// The dental specialties a practitioner can hold, and their French display labels.
//
// AC-P2.44: this replaces three byte-identical `specialties` arrays (clinic-settings, setup-wizard,
// join-wizard) that were guaranteed to drift.
//
// AC-P2.43: the KEYS stay English and are deliberately **not** migrated. They are the values already stored in
// `Doctor.Specialty` (free text, no enum, no backend validation) and snapshotted onto every
// `MedicalDocument.DoctorSpecialty` ever issued. Renaming them would leave every existing row matching no
// option — the Select would fall back to its placeholder and an unrelated save would silently rewrite the
// doctor's specialty. A display-time map is the fix; a data migration is not. Precedent: `weekdayLabelsFr` in
// setup-wizard.tsx, which keeps English weekday keys as state keys and maps them for display.

export const DOCTOR_SPECIALTIES = [
  "Dentist",
  "Orthodontist",
  "Prosthodontist",
  "Endodontist",
  "Periodontist",
  "Oral Surgeon",
  "Pediatric Dentist",
] as const

export type DoctorSpecialty = (typeof DOCTOR_SPECIALTIES)[number]

/** French labels for the stored (English) specialty keys. Display-only — never sent to the API. */
export const SPECIALTY_LABELS_FR: Record<string, string> = {
  Dentist: "Médecin dentiste",
  Orthodontist: "Orthodontiste",
  Prosthodontist: "Prothodontiste",
  Endodontist: "Endodontiste",
  Periodontist: "Parodontiste",
  "Oral Surgeon": "Chirurgien buccal",
  "Pediatric Dentist": "Pédodontiste",
}

/**
 * The French label for a stored specialty, or the stored value verbatim when it has none (AC-P2.45).
 *
 * Rendering verbatim rather than blank matters twice over: a clinic that typed a custom specialty keeps it, and
 * documents issued before this map existed already hold French snapshots (« Médecin dentiste »,
 * « Chirurgien maxillo-facial ») that must pass straight through.
 */
export function specialtyLabel(specialty: string | null | undefined): string {
  if (!specialty) return ""
  const trimmed = specialty.trim()
  return SPECIALTY_LABELS_FR[trimmed] ?? trimmed
}
