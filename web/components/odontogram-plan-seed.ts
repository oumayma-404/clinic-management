/**
 * What « Créer un plan depuis l'odontogramme » hands the devis editor.
 *
 * <p>Its own module rather than an export of `odontogram.tsx`, so the plan form can read the shape and the
 * pricing rule without importing a chart component — and so the two cannot drift into two answers about what a
 * grouped line costs.</p>
 */

/** One act the catalogue offers for a charted diagnosis. */
export interface SeedCandidate {
  procedureTypeId: string
  name: string
  /** Unit tariff. Multiplied by the tooth count only when the act charts a state — see {@link seedCost}. */
  defaultCost?: number
  /** True when the act leaves an odontogram state, i.e. it is priced per tooth rather than per session. */
  perTooth: boolean
  /** Its place among this diagnosis' options — 0 is the first choice, and several acts may share a rank. */
  rank: number
}

/**
 * One draft plan line seeded from an open diagnosis.
 *
 * ⚠️ **One per diagnosis, not one per tooth.** Charting a carie on 18 and 48 used to produce two identical lines
 * with two empty designation fields; they arrive as one line carrying both teeth, and « Séparer par dent » in the
 * editor puts them back when the teeth will be treated in different sessions.
 */
export interface OdontogramPlanSeed {
  toothNumbers: number[]
  /**
   * The act to perform — a PROCEDURE, never the diagnosis. Pre-filled from the first-choice treatment when the
   * catalogue offers exactly one, and left empty when it offers several: choosing between a simple and a
   * surgical extraction is a judgement about access, and the app guessing it silently mis-quotes the devis.
   */
  designationFr: string
  /**
   * What was charted, for display only — « Carie — dents 18, 48 ». Shown under the designation field so the
   * dentist can see what they are treating while choosing the act. Never persisted: a diagnosis is not a
   * billable line, and medical secrecy keeps it off the devis.
   */
  diagnosisLabel: string
  /** The charted condition itself, so the UI can colour the hint with that condition's own palette. */
  diagnosisCondition: string
  /** Prefilled planned cost, present only when the designation was. */
  plannedCost?: number
  /** The pre-filled procedure, when unambiguous. */
  procedureTypeId?: string
  /** Every act that treats this diagnosis, best first — what the editor offers as alternatives. */
  candidates: SeedCandidate[]
}

/**
 * What a line costs: the unit tariff times the teeth **only for an act that charts a state**.
 *
 * <p>⚠️ An act with no resulting condition is a session fee, not a per-tooth one — the same rule the fiche de
 * soins applies through `derivePerTooth`. Without it a « Traitement parodontal » grouped over six teeth would be
 * quoted at 720 DT for a 120 DT act, and the devis would go to the patient that way.</p>
 */
export function seedCost(candidate: SeedCandidate, toothCount: number): number | undefined {
  if (candidate.defaultCost == null || candidate.defaultCost <= 0) return undefined
  return candidate.perTooth && toothCount > 0 ? candidate.defaultCost * toothCount : candidate.defaultCost
}
