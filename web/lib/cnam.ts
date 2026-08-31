/**
 * The two closed value sets the CNAM **BS1** form's checkboxes are keyed on, client-side.
 *
 * ⚠️ **These are stored values, not display labels** — the opposite of the « English storage key + French display
 * map » convention used for appointment statuses and specialties. The server's `CnamBs1BulletinRenderer` ticks the
 * régime and lien boxes by matching these exact strings, so casing **and accents** are load-bearing:
 * « Convention bilatérale » carries one, and a single mismatch made the renderer's `switch` fall through, printing
 * an **empty** régime box while every layer reported success. Never "normalise" or re-case a value here.
 *
 * The authority is the backend value object `Domain/ValueObjects/CnamInfo` (`RegimeCnss`, `LienEnfant`, …), which
 * the renderer's two `switch`es and the write-path validation both read. This file exists so the browser has one
 * copy instead of one per screen: the strings used to live only as `<SelectItem value>` literals inside
 * `edit-patient-dialog.tsx`, so the bulletin editor — which has to tell the practitioner *which* mandatory field
 * is missing before Save — would have been the second hand-typed copy of an accented French literal whose
 * mismatch fails silently. Same lesson as the backend consts, one layer up.
 */

export const CNAM_REGIMES = ["CNSS", "CNRPS", "Convention bilatérale"] as const

export const CNAM_LIENS = ["Assuré lui-même", "Conjoint", "Enfant", "Ascendant"] as const

/**
 * The liens whose BS1 cell also carries a **rang**. « Enfant » is identified by its rang and « Ascendant » by
 * père/mère; the other two name exactly one person, so demanding a rang for them would be asking for a value that
 * does not exist. Mirrors `CnamInfo.LiensRequiringRang`.
 */
export const CNAM_LIENS_REQUIRING_RANG: readonly string[] = ["Enfant", "Ascendant"]

/**
 * How many digit cells the printed BS1 gives the identifiant unique. The renderer combs the number one digit per
 * fixed cell, so a longer value has nowhere to put its tail — it used to be dropped silently, printing a CNAM
 * identifier cut off mid-way. Mirrors `CnamInfo.IdentifiantUniqueDigits`.
 */
export const CNAM_IDENTIFIANT_DIGITS = 10

/** The digits of an identifiant unique — what the renderer actually combs, ignoring the spaces/dashes a free-text field collects. */
export function cnamIdentifiantDigitCount(value: string | null | undefined): number {
  if (!value) return 0
  return (value.match(/\d/g) ?? []).length
}

/**
 * True when the value carries between one and {@link CNAM_IDENTIFIANT_DIGITS} digits — i.e. it fits the printed
 * comb. A blank is **not** valid here: the field stays optional on the patient record (the caller checks that
 * separately), but a supplied number that cannot be printed in full is worse than none, since the paper shows a
 * plausible truncated identifier nobody re-reads. Mirrors `CnamInfo.IsValidIdentifiantUnique`.
 */
export function isValidCnamIdentifiant(value: string | null | undefined): boolean {
  const digits = cnamIdentifiantDigitCount(value)
  return digits > 0 && digits <= CNAM_IDENTIFIANT_DIGITS
}

export function isKnownCnamRegime(value: string | null | undefined): boolean {
  return value != null && (CNAM_REGIMES as readonly string[]).includes(value)
}

export function isKnownCnamLien(value: string | null | undefined): boolean {
  return value != null && (CNAM_LIENS as readonly string[]).includes(value)
}

export function cnamLienRequiresRang(value: string | null | undefined): boolean {
  return value != null && CNAM_LIENS_REQUIRING_RANG.includes(value)
}

// ── The annual ceiling (« plafond annuel »), L10 ─────────────────────────────────────────────────────
//
// ⚠️ **A display mirror of `Domain/Services/CnamPlafond`, not a second authority.** Every figure a screen
// *reports* — the ceiling, what was consumed, what remains — comes from `GET /api/patients/{id}/cnam-ceiling`,
// computed server-side against the invoices, exactly as the client-side reimbursement calculator was deleted in
// favour of `POST /dental-acts/reimbursement-estimates` (audit § 5.10). What lives here is only what the
// *input form* needs: the figure the patient dialog previews beside an empty override box, so the person typing
// can see what they are replacing. It is never sent, never persisted, and never rendered as a result.
//
// ⚠️ **The amounts are sourced but not officially confirmed** — the barème effective 1 February 2024 as reported
// by two Tunisian outlets in agreement, with no official CNAM publication retrieved. That is why the override
// field exists at all, and why the dialog says so next to it rather than presenting the number as fact.

/** The household ceiling by dependant count, index 0 = « assuré seul ». Beyond the last entry the barème stops. */
const CNAM_CEILING_BY_DEPENDANTS = [450, 675, 900, 1125, 1350] as const

/**
 * The portion dedicated to **soins dentaires externes**, added on top of the household figure. The least certain
 * amount of the set — the sources do not settle whether it sits inside the household ceiling or above it — so it is
 * shown as its own line rather than blended into one number nobody can check. Mirrors `CnamPlafond.DentalAllowance`.
 */
export const CNAM_DENTAL_ALLOWANCE = 150

/**
 * The supplements the sources report, **quoted** beside the override field rather than applied.
 *
 * Each turns on a fact this product does not record — a dependent parent, a dependent disabled child, a pregnancy —
 * and three more columns to hold facts nobody would maintain is how a setting ships with no caller. Naming the
 * amounts lets an admin work out the household's real ceiling once and type it in, which is then the one number the
 * calculation trusts. Mirrors `CnamPlafond`'s three supplement constants.
 */
export const CNAM_PLAFOND_SUPPLEMENTS: readonly { label: string; amount: number }[] = [
  { label: "par ascendant à charge", amount: 100 },
  { label: "par enfant handicapé à charge", amount: 100 },
  { label: "grossesse", amount: 150 },
]

/** The household ceiling for a dependant count. Mirrors `CnamPlafond.BaseCeiling`. */
export function cnamBaseCeiling(dependants: number): number {
  const safe = Number.isFinite(dependants) && dependants > 0 ? Math.floor(dependants) : 0
  return safe >= CNAM_CEILING_BY_DEPENDANTS.length
    ? CNAM_CEILING_BY_DEPENDANTS[CNAM_CEILING_BY_DEPENDANTS.length - 1]
    : CNAM_CEILING_BY_DEPENDANTS[safe]
}

/** What the server would compute with no override — the preview the dialog shows. Mirrors `CnamPlafond.EffectiveCeiling`'s fallback branch. */
export function cnamDefaultCeiling(dependants: number): number {
  return cnamBaseCeiling(dependants) + CNAM_DENTAL_ALLOWANCE
}
