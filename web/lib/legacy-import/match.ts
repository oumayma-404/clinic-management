import { leadingNumber, plain, withoutNumber } from "./parse-record"

/**
 * Which patient a folder of radios/photos belongs to, when the folder was named by hand: « Mohamed Ben Salah »,
 * « BENSALAH Mohammed », « 8 - Ben Salah Med ». The rule is the camera import's: **never sure and wrong** — a
 * folder is filed on its own only when one patient is clearly it; when two are close, or the best is only close,
 * the screen asks.
 */

export type MatchVerdict =
  /** One patient, clearly. Filed unless somebody changes it. */
  | { kind: "sure"; key: string; score: number }
  /** Close but not certain: these candidates, best first. Nothing is filed until somebody picks. */
  | { kind: "ask"; candidates: Array<{ key: string; score: number }> }
  /** Nobody close. Not filed unless somebody picks a patient by hand. */
  | { kind: "none" }

export interface MatchTarget {
  key: string
  /** Every name this patient is known by: the PDF's « Prénom Nom », the PDF's file name. */
  names: string[]
  /** « 8 » for dossier « 8/2022 ». */
  number: string | null
}

const SURE = 0.9
const MARGIN = 0.08
const CLOSE = 0.55

function words(s: string): string[] {
  return plain(withoutNumber(s)).replace(/[^a-z0-9]+/g, " ").trim().split(" ").filter(Boolean)
}

function levenshtein(a: string, b: string): number {
  if (a === b) return 0
  let prev = Array.from({ length: b.length + 1 }, (_, i) => i)
  for (let i = 1; i <= a.length; i++) {
    const cur = [i]
    for (let j = 1; j <= b.length; j++) {
      cur[j] = Math.min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1))
    }
    prev = cur
  }
  return prev[b.length]
}

const similarity = (a: string, b: string) => 1 - levenshtein(a, b) / Math.max(a.length, b.length, 1)

/** Word by word, any order: each word takes its closest unused partner. « Mohamed » / « Mohammed » = 0,88. */
function wordScore(a: string[], b: string[]): number {
  const free = [...b]
  let total = 0
  for (const w of a) {
    let best = 0
    let at = -1
    free.forEach((v, i) => {
      const s = similarity(w, v)
      if (s > best) {
        best = s
        at = i
      }
    })
    if (at >= 0) free.splice(at, 1)
    total += best
  }
  return total / Math.max(a.length, b.length, 1)
}

/** Letter pairs of the whole name, order-free — catches « Ben Salah » against « Bensalah ». */
function pairScore(a: string[], b: string[]): number {
  const pairs = (ws: string[]) => {
    const s = [...ws].sort().join("")
    const out = new Map<string, number>()
    for (let i = 0; i < s.length - 1; i++) out.set(s.slice(i, i + 2), (out.get(s.slice(i, i + 2)) ?? 0) + 1)
    return out
  }
  const pa = pairs(a)
  const pb = pairs(b)
  let shared = 0
  let total = 0
  for (const [k, n] of pa) {
    shared += Math.min(n, pb.get(k) ?? 0)
    total += n
  }
  for (const n of pb.values()) total += n
  return total ? (2 * shared) / total : 0
}

/** 0…1: how much folder name `a` reads like patient name `b`. */
export function nameScore(a: string, b: string): number {
  const wa = words(a)
  const wb = words(b)
  if (!wa.length || !wb.length) return 0
  if (wa.slice().sort().join(" ") === wb.slice().sort().join(" ")) return 1
  return Math.max(wordScore(wa, wb), pairScore(wa, wb))
}

/**
 * The verdict for one folder. A leading number on the folder is a dossier number: equal to the patient's, it makes a
 * close name sure; different, it makes even an identical name a question.
 */
export function matchFolder(folder: string, targets: MatchTarget[]): MatchVerdict {
  const folderNumber = leadingNumber(folder)
  // Two records of one person (about to be merged) share a key: one candidate, not a tie.
  const byKey = new Map<string, { key: string; score: number; numberAgrees: boolean | null }>()
  for (const t of targets) {
    const score = Math.max(...t.names.map((n) => nameScore(folder, n)))
    const numberAgrees = folderNumber != null && t.number != null ? folderNumber === t.number : null
    const held = byKey.get(t.key)
    if (!held || score > held.score) byKey.set(t.key, { key: t.key, score, numberAgrees: numberAgrees ?? held?.numberAgrees ?? null })
  }
  const scored = [...byKey.values()].filter((c) => c.score >= CLOSE).sort((x, y) => y.score - x.score)

  if (!scored.length) return { kind: "none" }
  const ask = { kind: "ask" as const, candidates: scored.slice(0, 3).map(({ key, score }) => ({ key, score })) }

  // The dossier number decides between close names: « 3 - Mohamed Ben Salah » is n° 3, not his namesake n° 4.
  const numbered = scored.filter((c) => c.numberAgrees === true)
  if (numbered.length === 1 && numbered[0].score >= 0.6) return { kind: "sure", key: numbered[0].key, score: numbered[0].score }
  if (numbered.length > 1) return ask

  const [best, second] = scored
  const clearlyAhead = !second || best.score - second.score >= MARGIN
  if (best.numberAgrees !== false && clearlyAhead && best.score >= SURE) return { kind: "sure", key: best.key, score: best.score }
  return ask
}
