import { quoteFr } from "@/lib/format"

/**
 * Reconciling an open form with the server's copy, section by section, three ways — the fiche de soins' approach
 * (`components/record/fiche-merge.ts`), generalised so « Recharger » keeps the typing on every form that saves a
 * version (`clinic-pc-copy` D23, AC-3.2).
 *
 * For each section: the copy the form was opened with (or last reconciled with), what is on screen, the server now.
 * Only a section BOTH sides changed, differently, loses what was typed — and it is named.
 */

/**
 * - `keep` — the server agrees with the screen, or nobody else touched it: what is on screen stays.
 * - `take` — only the other person changed it: their version replaces an untouched section, silently.
 * - `takeOver` — both changed it, differently: theirs is shown and the section is named.
 */
export type MergeVerdict = "keep" | "take" | "takeOver"

export function mergeVerdict(opened: string, onScreen: string, server: string): MergeVerdict {
  if (server === onScreen || server === opened) return "keep"
  return onScreen === opened ? "take" : "takeOver"
}

/** Each section's verdict, given three snapshots keyed by section (each a canonical string). */
export function mergeSections<K extends string>(
  sections: readonly K[],
  opened: Record<K, string>,
  onScreen: Record<K, string>,
  server: Record<K, string>,
): { take: K[]; takenOver: K[] } {
  const take: K[] = []
  const takenOver: K[] = []
  for (const section of sections) {
    const verdict = mergeVerdict(opened[section], onScreen[section], server[section])
    if (verdict === "keep") continue
    take.push(section)
    if (verdict === "takeOver") takenOver.push(section)
  }
  return { take, takenOver }
}

/** The sentence shown when sections were taken over, or null when nothing was. */
export function takenOverSentence(labels: readonly string[]): string | null {
  if (labels.length === 0) return null
  return `Modifié entre-temps par quelqu'un d'autre : ${labels.map(quoteFr).join(", ")}. Sa version est affichée — refaites-y votre modification.`
}
