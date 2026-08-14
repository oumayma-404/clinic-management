/**
 * Which set of teeth a patient is charted on.
 *
 * English storage keys + a French display map, the standing convention for a closed persisted set (see
 * `lib/specialties.ts`, `components/appointment-labels.ts`). The keys are what `Patient.Dentition` stores and what
 * crosses the wire, so they are never renamed.
 *
 * ⚠️ **Two values, and they describe the patient, not the chart.** Real dentition passes through a *mixed* stage —
 * a seven-year-old has baby and permanent teeth at once — which this field cannot express. That used to make the
 * mixed stage **unchartable**, because the charts read the arch straight off this value. It no longer does: the
 * chart's arch is a `DentitionView` the user picks (below), seeded from this field but never locked to it, and
 * `DentalRecord.IsAdultTeeth` stays what the server always said it was — a display hint, not a constraint
 * (`DentalRecordActParser`). Already-stored records are split by each tooth's own FDI range (`isAdultTooth` in
 * `tooth-multiselect.tsx`), never by a record-level flag.
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
 * Which arch a tooth chart is **currently showing** — a view, not a patient attribute.
 *
 * ⚠️ This is deliberately a third value set rather than a reuse of `Dentition`. `Dentition` is persisted on the
 * patient and has two values; the chart needs a third, `mixed`, because that is what a 6–12-year-old's mouth
 * actually is *and* what the official CNAM BS1 odontogram prints (permanent 11–48 **and** deciduous 51–85, with
 * the instruction that naming the treated tooth is « indispensable »). Nothing stores a `DentitionView`: it is
 * seeded from the patient (or, when editing, from the fiche's own acts) and then belongs to the user.
 */
export const DENTITION_VIEWS = ["adult", "child", "mixed"] as const

export type DentitionView = (typeof DENTITION_VIEWS)[number]

/** Short French captions for the chart's arch switch. */
export const DENTITION_VIEW_LABELS_FR: Record<DentitionView, string> = {
  adult: "Adulte",
  child: "Enfant",
  mixed: "Mixte",
}

/** The view a stored `Dentition` opens on. Never a lock — see `DENTITION_VIEWS`. */
export function dentitionViewFor(value: string | null | undefined): DentitionView {
  return isAdultDentition(value) ? "adult" : "child"
}

/**
 * The narrowest view that can display **every** one of these teeth, or `null` for an empty list (in which case the
 * caller must fall back to what it knows about the patient rather than guessing).
 *
 * This is what makes reopening a fiche safe: a record charted on baby teeth reopens on `child`, one that genuinely
 * spans both reopens on `mixed`, and neither can open on an arch that hides its own acts. `isAdult` here mirrors
 * `isAdultTooth` — quadrants 1–4 are permanent, 5–8 deciduous — kept as a parameter so this file stays free of
 * component imports.
 */
export function dentitionViewForTeeth(
  teeth: readonly number[],
  isAdult: (tooth: number) => boolean,
): DentitionView | null {
  let permanent = false
  let deciduous = false
  for (const tooth of teeth) {
    if (isAdult(tooth)) permanent = true
    else deciduous = true
  }
  if (permanent && deciduous) return "mixed"
  if (permanent) return "adult"
  if (deciduous) return "child"
  return null
}

/**
 * Age at which the permanent set is assumed complete.
 *
 * ⚠️ **Mirrors `DentitionRules.AdultFromAgeYears` on the server** — same rule, deliberately duplicated. (The agenda
 * palette used to be cited here as the other example of that; it no longer is one — `ColorHex` is served, not
 * mirrored.) It is only a *form default*: the value the user sees pre-selected and
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
