/**
 * Which set of teeth a patient is charted on.
 *
 * English storage keys + a French display map, the standing convention for a closed persisted set (see
 * `lib/specialties.ts`, `components/appointment-labels.ts`). The keys are what `Patient.Dentition` stores and what
 * crosses the wire, so they are never renamed.
 *
 * ⚠️ **Two values, with a known limitation.** Real dentition passes through a *mixed* stage — a seven-year-old has
 * baby and permanent teeth at once — so a patient marked `Child` cannot be charted on a permanent molar until their
 * record is switched to `Adult`, and the remaining baby teeth then become unchartable. Chosen knowingly; the escape
 * hatch is that the field is editable on the patient. Note this governs what can be charted *next* — already-stored
 * records are still split by each tooth's own FDI range (`isAdultTooth` in `tooth-multiselect.tsx`), so history
 * containing both dentitions keeps rendering correctly.
 */
export const DENTITIONS = ["Child", "Adult"] as const

export type Dentition = (typeof DENTITIONS)[number]

export const DENTITION_LABELS_FR: Record<Dentition, string> = {
  Child: "Enfant — dents de lait",
  Adult: "Adulte — dents définitives",
}

/** Short form, for badges and tight rows. */
export const DENTITION_SHORT_FR: Record<Dentition, string> = {
  Child: "Enfant",
  Adult: "Adulte",
}

/** Unknown values pass through verbatim, so an older row can never render as blank. */
export function dentitionLabel(value: string | null | undefined): string {
  if (!value) return "—"
  return DENTITION_LABELS_FR[value as Dentition] ?? value
}

export function isAdultDentition(value: string | null | undefined): boolean {
  // Anything that is not explicitly the child chart reads as adult — the majority case, and the safer default for
  // an unrecognised value since the adult chart is a superset of what most patients need.
  return value !== "Child"
}

/**
 * Age at which the permanent set is assumed complete.
 *
 * ⚠️ **Mirrors `DentitionRules.AdultFromAgeYears` on the server** — same rule, deliberately duplicated, the same way
 * `ColorHex`'s palette mirrors `COLOR_PALETTE`. It is only a *form default*: the value the user sees pre-selected and
 * is free to change. The server applies its own copy when a caller sends no dentition at all, so a drift here changes
 * what the form suggests, never what gets stored behind the user's back. Keep the two in sync anyway.
 */
export const ADULT_FROM_AGE_YEARS = 13

/**
 * The dentition to pre-select for a `yyyy-MM-dd` birthdate, or null when it is empty/unparseable — in which case the
 * form must not guess, it must keep asking.
 */
export function dentitionFromBirthdate(birthdate: string): Dentition | null {
  if (!birthdate) return null
  const dob = new Date(`${birthdate}T00:00:00`)
  if (Number.isNaN(dob.getTime())) return null

  const today = new Date()
  let age = today.getFullYear() - dob.getFullYear()
  const monthDelta = today.getMonth() - dob.getMonth()
  if (monthDelta < 0 || (monthDelta === 0 && today.getDate() < dob.getDate())) {
    age--
  }

  return age >= ADULT_FROM_AGE_YEARS ? "Adult" : "Child"
}
