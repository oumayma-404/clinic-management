import type { SmokingStatus, TobaccoUnit, TobaccoUse } from "@/lib/api/types"

/**
 * French labels for « Tabac » — the display half of the repo's standing **English storage key, French display
 * map** convention (`lib/specialties.ts`, `components/appointment-labels.ts`, `lib/tunisia.ts`).
 *
 * ⚠️ **Never rename a key.** `Patient.TobaccoUse.Status` is persisted as the enum member's own name, so the keys
 * below are what is already in the database; this maps at display time only. An unknown value is **passed
 * through verbatim** rather than replaced with a placeholder — a status added server-side before this map
 * catches up must still show something a human can act on.
 *
 * ⚠️ **A null `tobaccoUse` is « jamais renseigné », not « Non-fumeur ».** The two are different clinical facts
 * and every reader here keeps them apart: `tobaccoSummary` answers `null` for the first, so a caller renders
 * nothing rather than asserting the patient does not smoke.
 */
const SMOKING_STATUS_LABELS_FR: Record<string, string> = {
  NonSmoker: "Non-fumeur",
  Smoker: "Fumeur",
  FormerSmoker: "Ancien fumeur",
}

/** Singular / plural, because « 1 cigarettes » is the kind of detail that makes a record look machine-written. */
const UNIT_LABELS_FR: Record<string, { one: string; many: string }> = {
  Cigarettes: { one: "cigarette", many: "cigarettes" },
  Packs: { one: "paquet", many: "paquets" },
}

/** The French label for a stored status, or the raw value when it is not one we know. */
export function smokingStatusLabel(status: SmokingStatus | string | null | undefined): string {
  if (!status) return ""
  return SMOKING_STATUS_LABELS_FR[status] ?? status
}

/** « 20 cigarettes/j » · « 1 paquet/j », or `null` when no figure was given. */
export function tobaccoPerDayLabel(tobacco: TobaccoUse | null | undefined): string | null {
  const perDay = tobacco?.perDay
  if (!perDay || perDay <= 0) return null

  const unit = UNIT_LABELS_FR[tobacco?.unit ?? "Cigarettes"] ?? UNIT_LABELS_FR.Cigarettes
  return `${perDay} ${perDay > 1 ? unit.many : unit.one}/j`
}

/**
 * The one line a strip or a badge shows — « Fumeur · 20 cigarettes/j », « Non-fumeur », or `null`.
 *
 * `null` for an unanswered block **and** for an unknown status, so a caller renders nothing rather than an
 * empty chip. « Fumeur » with no figure is a real answer and returns just the status.
 */
export function tobaccoSummary(tobacco: TobaccoUse | null | undefined): string | null {
  if (!tobacco?.status) return null

  const status = smokingStatusLabel(tobacco.status)
  if (!status) return null

  const perDay = tobaccoPerDayLabel(tobacco)
  return perDay ? `${status} · ${perDay}` : status
}

/**
 * Whether this answer is one a clinician should see at a glance beside the allergies.
 *
 * Only a **current** smoker: « Non-fumeur » is reassurance and « Ancien fumeur » is history, and a warning strip
 * that fires on every patient is a strip the eye learns to skip. Both remain visible in the patient's file.
 */
export function isActiveSmoker(tobacco: TobaccoUse | null | undefined): boolean {
  return tobacco?.status === "Smoker"
}

/**
 * The largest daily quantity the form accepts, mirroring `TobaccoUse.MaxPerDay` server-side.
 *
 * A ceiling, not a clinical claim: it refuses a mis-typed « 200 » at the field instead of printing an
 * implausible figure beside the patient's name. Kept equal to the server's so the two refusals cannot disagree.
 */
export const MAX_TOBACCO_PER_DAY = 200

/** The three statuses, in the order the form offers them. */
export const SMOKING_STATUSES: readonly SmokingStatus[] = ["NonSmoker", "Smoker", "FormerSmoker"] as const

/** The two units, in the order the switch offers them. */
export const TOBACCO_UNITS: readonly TobaccoUnit[] = ["Cigarettes", "Packs"] as const

/** « cigarettes » / « paquets » for the unit switch itself — plural, since it labels a mode not a quantity. */
export function tobaccoUnitLabel(unit: TobaccoUnit): string {
  return UNIT_LABELS_FR[unit]?.many ?? unit
}
