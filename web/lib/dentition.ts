/**
 * Which set of teeth a patient is charted on.
 *
 * English storage keys + a French display map, the standing convention for a closed persisted set (see
 * `lib/specialties.ts`, `components/appointment-labels.ts`). The keys are what `Patient.Dentition` stores and what
 * crosses the wire, so they are never renamed.
 *
 * ⚠️ **Three values, and they describe the mouth, not the patient.** Real dentition passes through a *mixed* stage —
 * a seven-year-old has baby and permanent teeth at once — which this field used to be unable to express. `Mixed`
 * is that stage. The chart's arch is still a `DentitionView` the user picks (below), seeded from this field but
 * never locked to it, and `DentalRecord.IsAdultTeeth` stays what the server always said it was — a display hint,
 * not a constraint (`DentalRecordActParser`). Already-stored records are split by each tooth's own FDI range
 * (`isAdultTooth` in `tooth-multiselect.tsx`), never by a record-level flag.
 *
 * ⚠️ **The labels name dentitions, never ages.** « Adulte » / « Enfant » is gone from the interface: it described
 * the patient rather than the mouth, and there is no third patient. The *stored* keys stay `Child` / `Adult` — a
 * rename would break every stored row for a caption change — and `Mixed` is appended, so `DENTITIONS`' order here
 * is the **display** order (temporaire → mixte → définitive) rather than the enum's numbering.
 */
export const DENTITIONS = ["Child", "Mixed", "Adult"] as const

export type Dentition = (typeof DENTITIONS)[number]

export const DENTITION_LABELS_FR: Record<Dentition, string> = {
  Child: "Denture temporaire",
  Mixed: "Denture mixte",
  Adult: "Denture définitive",
}

/** Short form, for badges and tight rows. */
export const DENTITION_SHORT_FR: Record<Dentition, string> = {
  Child: "Temporaire",
  Mixed: "Mixte",
  Adult: "Définitive",
}

/**
 * The age band each value is the default for, shown *inside* the control as each option's caption.
 *
 * ⚠️ It replaces the « proposé d'après l'âge » sentence that used to sit under the radio group: the rule is what
 * the reader needs, and printed beside the options it is read, whereas printed underneath it was not. The strings
 * must stay consistent with {@link MIXED_FROM_AGE_YEARS} / {@link ADULT_FROM_AGE_YEARS} — `DentitionBandsTests`'
 * client twin is `check:responsive`'s `dentition-bands-match-the-rule`.
 */
export const DENTITION_BANDS_FR: Record<Dentition, string> = {
  Child: "0 – 5 ans",
  Mixed: "6 – 12 ans",
  Adult: "13 ans et plus",
}

/** Unknown values pass through verbatim, so an older row can never render as blank. */
export function dentitionLabel(value: string | null | undefined): string {
  if (!value) return "—"
  return DENTITION_LABELS_FR[value as Dentition] ?? value
}

/**
 * ⚠️ **`Mixed` answers `true` here, and that is a deliberate narrowing, not an oversight.**
 *
 * This asks one question — "may permanent teeth be charted?" — and for a mixed dentition the answer is yes. It is
 * emphatically *not* "which arch should the chart open on": a mixed mouth also has deciduous teeth, so routing the
 * chart through this would hide half of it. That question is {@link dentitionViewFor}, which has a third answer.
 *
 * It has **no call sites** outside this file today, and it should acquire one only for the permanent-teeth question.
 * Anything about *rendering an arch* goes through `dentitionViewFor` / `dentitionViewForTeeth`.
 */
export function isAdultDentition(value: string | null | undefined): boolean {
  // Anything that is not explicitly the deciduous-only chart may hold permanent teeth — the majority case, and the
  // safer default for an unrecognised value since the adult chart is a superset of what most patients need.
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

/**
 * The view a stored `Dentition` opens on. Never a lock — see `DENTITION_VIEWS`.
 *
 * ⚠️ **This is the reader that had to learn `Mixed`, and the only one.** Routed through `isAdultDentition` it would
 * have opened a mixed mouth on the permanent arch and made its remaining baby teeth unreachable — no error
 * anywhere, just half a chart. The three stored values now map one-to-one onto the three views.
 */
export function dentitionViewFor(value: string | null | undefined): DentitionView {
  if (value === "Child") return "child"
  if (value === "Mixed") return "mixed"
  return "adult"
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
 * Age at which the first permanent molars arrive and the mouth becomes « denture mixte ».
 *
 * ⚠️ **Mirrors `DentitionRules.MixedFromAgeYears`**, the same way {@link ADULT_FROM_AGE_YEARS} mirrors its own
 * constant, and for the same reason: a drift here changes what the form *suggests*, never what the server stores.
 */
export const MIXED_FROM_AGE_YEARS = 6

/**
 * The dentition to assume at a whole-year age — the one place the three bands are written on the client, mirroring
 * `DentitionRules.FromAgeYears`.
 *
 * ⚠️ Both entry points below answer through this, so a typed age and a date of birth cannot disagree about the same
 * person. That was already the rule with two bands; with three there are two boundaries to keep aligned instead of
 * one, which is exactly the shape that drifts when it is written twice.
 */
function dentitionForAgeYears(age: number): Dentition {
  if (age >= ADULT_FROM_AGE_YEARS) return "Adult"
  if (age >= MIXED_FROM_AGE_YEARS) return "Mixed"
  return "Child"
}

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

  return dentitionForAgeYears(age)
}

/**
 * Whole years elapsed since a `yyyy-MM-dd` birthdate, or null when it is empty/unparseable.
 *
 * <p>Exported because the form now prints the computed age *beside* the date rather than on a help line under it —
 * « 12/04/1984 · 42 ans » — which is one fewer line and puts the derived value where the reader is already looking.
 * Same arithmetic as {@link dentitionFromBirthdate}, which is why it lives here rather than in a component.</p>
 */
export function ageFromBirthdate(birthdate: string): number | null {
  if (!birthdate) return null
  const dob = new Date(`${birthdate}T00:00:00`)
  if (Number.isNaN(dob.getTime())) return null

  const today = new Date()
  let age = today.getFullYear() - dob.getFullYear()
  const monthDelta = today.getMonth() - dob.getMonth()
  if (monthDelta < 0 || (monthDelta === 0 && today.getDate() < dob.getDate())) {
    age--
  }

  return age >= 0 ? age : null
}

/**
 * The dentition to pre-select from an age in whole years, or null when the box is empty or unreadable.
 *
 * <p>The other half of {@link dentitionFromBirthdate}, for the patient who does not know their date of birth —
 * a walk-in, an elderly patient, a child brought in by a neighbour. It is the **same** {@link ADULT_FROM_AGE_YEARS}
 * threshold rather than a second copy of the rule, because the two answers must never disagree about the same
 * person.</p>
 *
 * <p>⚠️ The age itself is a form input and nothing else: it seeds this default and is never stored. A patient with
 * no date of birth keeps no date of birth — see `Patient.DateOfBirth`, which says why an invented one is worse
 * than none.</p>
 */
export function dentitionFromAge(age: string): Dentition | null {
  if (!age.trim()) return null
  const years = Number(age)
  if (!Number.isFinite(years) || years < 0) return null
  return dentitionForAgeYears(years)
}
