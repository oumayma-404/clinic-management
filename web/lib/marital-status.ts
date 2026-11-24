/**
 * « Situation familiale » — what the patient form offers. The stored value IS the French word, so there is no
 * key/label pair to keep in step with the server. A word imported from a previous software outside these four
 * (« Marie », « Concubinage ») is kept as written; `maritalStatusOptions` offers it back so a save never drops it.
 */
export const MARITAL_STATUS_OPTIONS = ["Célibataire", "Marié(e)", "Divorcé(e)", "Veuf / veuve"] as const

/** Radix Select has no empty-string item, so « Non renseigné » travels as this and saves as "". */
export const MARITAL_STATUS_NONE = "__none__"

/** The four, plus the stored value when it is none of them. */
export function maritalStatusOptions(current: string): string[] {
  const value = current.trim()
  const known: readonly string[] = MARITAL_STATUS_OPTIONS
  return value && !known.includes(value) ? [...known, value] : [...known]
}
