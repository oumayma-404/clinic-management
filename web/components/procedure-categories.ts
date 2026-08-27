import type { ProcedureTypeDto } from "@/lib/api/types"

/**
 * How the act catalogue is grouped and ordered for display — shared by the fiche's `act-catalog-picker` and the
 * agenda's `appointment-acts-picker`, which must not disagree about where an act lives.
 *
 * ⚠️ **This is display only. The category itself is data**, filed per act on the server and canonicalised there
 * (`ProcedureTypeCategories`). What lives here is the *order* the disciplines are shown in, and the bucket for
 * acts that have none.
 */

/**
 * Clinical order — the order a course of treatment runs, mirroring the backend's canonical list and the catalog
 * seed's row order. Deliberately **not** alphabetical: these pickers are opened to find the act about to be
 * performed, and « Consultation » before « Chirurgie » is how a session actually goes.
 *
 * ⚠️ A mirror of `ProcedureTypeCategories.Canonical`, and it degrades safely rather than breaking if the two drift:
 * a discipline added server-side and not here is simply sorted with the clinic-authored ones (alphabetically, after
 * these twelve) instead of vanishing. That is why this is a display hint and not a source of truth — the
 * *authoritative* list for input is `procedureTypesApi.getCategories()`, which is served.
 *
 * ⚠️ Note the `/procedure-types` **table** orders alphabetically by category instead, because it is server-paged
 * and reproducing this order in SQL would mean a twelve-branch CASE in the repository. Do not "fix" one to match
 * the other: a paged management table and a clinical picker are answering different questions.
 */
export const PROCEDURE_CATEGORY_ORDER: readonly string[] = [
  "Consultation",
  "Radiologie",
  "Soins conservateurs",
  "Endodontie",
  "Parodontologie",
  "Chirurgie/Extraction",
  "Prothèse fixe",
  "Prothèse amovible",
  "Implantologie",
  "Orthodontie",
  "Esthétique",
  "Pédodontie",
]

/**
 * The heading for acts with no category.
 *
 * « Sans catégorie », not « Autres » — the distinction became real when categories opened up. « Autres » used to
 * absorb two different things: an act nobody filed, and an act filed under a label this file did not recognise.
 * The second is now a legitimate clinic-authored discipline and gets its **own** heading with its own name; only
 * the genuinely unfiled land here. A practice that created « Occlusodontie » wants to read the word, not « Autres ».
 */
export const UNCATEGORIZED_LABEL = "Sans catégorie"

/** A category's rank: the canonical twelve first in clinical order, everything else after, alphabetically. */
function categoryRank(category: string): number {
  const index = PROCEDURE_CATEGORY_ORDER.indexOf(category)
  return index === -1 ? PROCEDURE_CATEGORY_ORDER.length : index
}

export interface ProcedureCategoryGroup {
  /** Heading to render — a real category, or {@link UNCATEGORIZED_LABEL}. */
  label: string
  items: ProcedureTypeDto[]
}

/**
 * Buckets a catalogue into display groups: canonical disciplines in clinical order, then the clinic's own
 * alphabetically, then the unfiled acts last.
 *
 * Within a group the incoming order is preserved, so a caller that sorted by name or kept the server's order keeps
 * it. Empty groups are never emitted — a clinic doing no orthodontics should not read an « Orthodontie » heading.
 */
export function groupProceduresByCategory(
  procedures: readonly ProcedureTypeDto[],
): ProcedureCategoryGroup[] {
  const buckets = new Map<string, ProcedureTypeDto[]>()

  for (const procedure of procedures) {
    // `?.trim() ||` and not `??`: a category that reached the client as `""` (an older row, or a cleared field
    // echoed back) is unfiled, and treating it as its own group would render a heading with no text in it.
    const label = procedure.category?.trim() || UNCATEGORIZED_LABEL
    const bucket = buckets.get(label)
    if (bucket) bucket.push(procedure)
    else buckets.set(label, [procedure])
  }

  return [...buckets.entries()]
    .map(([label, items]) => ({ label, items }))
    .sort((a, b) => {
      // The unfiled bucket is always last, whatever it is called.
      if (a.label === UNCATEGORIZED_LABEL) return 1
      if (b.label === UNCATEGORIZED_LABEL) return -1

      const rankDelta = categoryRank(a.label) - categoryRank(b.label)
      if (rankDelta !== 0) return rankDelta
      // Same rank means both are clinic-authored (rank = length), so fall back to the alphabet. `localeCompare`
      // rather than `<`, since French labels carry accents and « Éclaircissement » must not sort after « Z ».
      return a.label.localeCompare(b.label, "fr")
    })
}
