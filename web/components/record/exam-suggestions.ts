/**
 * The examens a dentist sends a patient for, offered as suggestions on a prescription line.
 *
 * ⚠️ **Suggestions, never a closed list.** The field they fill is free text and stays free text — what a
 * practitioner writes on an ordonnance is their clinical judgement, and a `Select` here would be the product
 * telling a dentist which examens exist. The list is here to save typing the four that get typed every week,
 * and to spell them the same way each time so the patient's history reads consistently.
 *
 * ⚠️ **Not a catalogue and deliberately not data.** A `Medication` is a record with a DCI, a form and a
 * strength that a prescription snapshots; an examen request is a sentence. Filing these in the database would
 * buy an admin screen nobody asked for and a migration to add « cone beam ».
 *
 * Grouped only so the dropdown is scannable; the group name is never written into the field.
 */
export interface ExamSuggestionGroup {
  label: string
  items: string[]
}

export const EXAM_SUGGESTIONS: readonly ExamSuggestionGroup[] = [
  {
    label: "Imagerie",
    items: [
      "Radiographie panoramique dentaire",
      "Radiographie rétro-alvéolaire",
      "Téléradiographie de profil",
      "Cone beam (CBCT)",
      "Radiographie rétro-coronaire",
    ],
  },
  {
    label: "Biologie",
    items: [
      "Bilan sanguin : NFS, plaquettes",
      "Bilan d'hémostase : TP, TCA, INR",
      "Glycémie à jeun, HbA1c",
      "Bilan pré-opératoire",
    ],
  },
  {
    label: "Avis",
    items: [
      "Avis cardiologique avant soins",
      "Avis ORL",
      "Avis stomatologique",
    ],
  },
]
