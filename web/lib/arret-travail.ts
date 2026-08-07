/**
 * The closed value sets of the CNAM **P 061** « certificat médical d'arrêt de travail » (L11), client-side.
 *
 * ⚠️ **These are stored values, not display labels** — the same rule as `lib/cnam.ts` and the opposite of the
 * « English key + French label » convention used for statuses. `CnamArretTravailRenderer` matches these exact
 * strings to decide which checkbox to tick on the printed form, and `ArretTravailValidation` refuses anything
 * outside the set. A retyped or re-cased value would fall through the renderer's `switch` and print a form with
 * **no** box ticked while every layer reported success — the bulletin's régime defect, in a new place.
 *
 * The authority is the backend `Application/Features/Documents/ArretTravailKeys`. This file exists so the browser
 * holds one copy rather than one per component, and so the French labels live where French labels belong.
 */

/** The three mutually-exclusive traumatisme causes. Storage values; mirrors `ArretTravailKeys.AllowedTraumaCauses`. */
export const TRAUMA_CAUSES = ["voie-publique", "domestique", "violence"] as const

export type TraumaCause = (typeof TRAUMA_CAUSES)[number]

/**
 * French labels for the three causes — an exhaustive `Record`, so a cause added to the storage set with no label
 * here is a `tsc` error rather than a radio button rendering a bare `voie-publique`.
 */
export const TRAUMA_CAUSE_LABELS_FR: Record<TraumaCause, string> = {
  "voie-publique": "Accident de la voie publique",
  domestique: "Accident domestique",
  violence: "Acte de violence",
}

/**
 * The longest arrêt the form accepts, in days. Mirrors `ArretTravailKeys.MaxDays`.
 *
 * A cap exists because the field is free text and a mis-keyed « 300 » is an arrêt of ten months on a document that
 * entitles the patient to an indemnity — the kind of mistake nobody re-reads on paper. 180 is well past any dental
 * indication.
 */
export const ARRET_MAX_DAYS = 180

/**
 * `true` / `false` / `null` for the « Le patient a-t-il été hospitalisé ? » box.
 *
 * ⚠️ **Null is a real third state**: « not answered ». Defaulting it to « Non » would make the software assert a
 * clinical fact nobody entered, on a form that decides an indemnity — so the renderer ticks neither box until
 * somebody chooses.
 */
export function parseHospitalised(value: string): boolean | null {
  if (value === "true") return true
  if (value === "false") return false
  return null
}
