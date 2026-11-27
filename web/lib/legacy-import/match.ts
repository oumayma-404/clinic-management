import { plain } from "./parse-record"

/**
 * Which patient a folder of radios/photos belongs to, when the folder was named by hand: « Mohamed Ben Salah »,
 * « BENSALAH Mohammed », « 8 - Ben Salah Med ». The rule is the camera import's: **never sure and wrong** — a
 * folder is filed on its own only when one patient is clearly it; when two are close, or the best is only close,
 * the screen asks.
 *
 * ⚠️ A folder name is read for its NAME only: a date, a year or a word like « radio » is not part of anybody's name,
 * and scored as one it sent 1 033 of 1 045 folders to « à vérifier » (measured on a 1 045-patient cabinet whose
 * folders read « TRABELSI Mohamed 2023 »). « Med » is read as « Mohamed », the way it is written in Tunisia.
 *
 * ⚠️ Each name is prepared once ({@link prepare}): comparing 1 000 folders with 1 000 patients rebuilt both sides a
 * million times and held the page for ~12 s on a fast PC.
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

/** Words a folder name carries that are never part of a person's name. */
const NOT_A_NAME = new Set([
  "radio", "radios", "rx", "pano", "panoramique", "panoramiques", "retro", "retroalveolaire", "retroalveolaires",
  "cbct", "scanner", "scan", "photo", "photos", "image", "images", "img", "dossier", "patient", "mr", "mme", "mlle", "dr",
])
/** Short forms written in place of a full first name. */
const SHORT_FORM = new Map([
  ["med", "mohamed"],
  ["mohd", "mohamed"],
  ["mhd", "mohamed"],
])

/** A year is not a dossier number: « 2023 - Ben Ali » was filed in 2023. */
const isYear = (n: string) => /^(19|20)\d\d$/.test(n)

/** The dossier number a folder name starts with: « 152 - Nom », « 152_Nom », « 152 Nom » — never a year. */
export function folderNumber(name: string): string | null {
  const n = name.match(/^\s*(\d+)(?:\s*[-_.)]\s*|\s+)/)?.[1] ?? null
  return n && !isYear(n) ? n : null
}

interface Prepared {
  words: string[]
  /** The words sorted: equal keys are the same name in any order. */
  key: string
  pairs: Map<string, number>
  pairTotal: number
}

/** A name's words, without numbers, dates or words like « radio », short forms written out. */
function words(s: string): string[] {
  return plain(s)
    .replace(/[^a-z0-9]+/g, " ")
    .trim()
    .split(" ")
    .filter((w) => w && !/\d/.test(w) && !NOT_A_NAME.has(w))
    .map((w) => SHORT_FORM.get(w) ?? w)
}

function prepare(s: string): Prepared {
  const ws = words(s)
  const joined = [...ws].sort().join("")
  const pairs = new Map<string, number>()
  for (let i = 0; i < joined.length - 1; i++) pairs.set(joined.slice(i, i + 2), (pairs.get(joined.slice(i, i + 2)) ?? 0) + 1)
  return { words: ws, key: [...ws].sort().join(" "), pairs, pairTotal: Math.max(joined.length - 1, 0) }
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

/** Names repeat (Mohamed, Ben, Ali…): each pair of words is measured once per page, not once per folder × patient. */
const similarities = new Map<string, number>()
function similarity(a: string, b: string): number {
  if (a === b) return 1
  const k = a < b ? `${a}|${b}` : `${b}|${a}`
  let s = similarities.get(k)
  if (s === undefined) {
    if (similarities.size > 500_000) similarities.clear()
    s = 1 - levenshtein(a, b) / Math.max(a.length, b.length, 1)
    similarities.set(k, s)
  }
  return s
}

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
function pairScore(a: Prepared, b: Prepared): number {
  const total = a.pairTotal + b.pairTotal
  if (!total) return 0
  let shared = 0
  for (const [k, n] of a.pairs) shared += Math.min(n, b.pairs.get(k) ?? 0)
  return (2 * shared) / total
}

// ⚠️ No « skip the word measure when the letter pairs are far apart » shortcut: tried, it changed 44 verdicts in 3 000.
function score(a: Prepared, b: Prepared): number {
  if (!a.words.length || !b.words.length) return 0
  if (a.key === b.key) return 1
  return Math.max(wordScore(a.words, b.words), pairScore(a, b))
}

/** 0…1: how much folder name `a` reads like patient name `b`. */
export function nameScore(a: string, b: string): number {
  return score(prepare(a), prepare(b))
}

/** Each target's names, prepared once however many folders are matched against them. */
const preparedNames = new WeakMap<MatchTarget, Prepared[]>()
function namesOf(t: MatchTarget): Prepared[] {
  let held = preparedNames.get(t)
  if (!held) {
    held = t.names.map(prepare)
    preparedNames.set(t, held)
  }
  return held
}

/**
 * The verdict for one folder. A leading number on the folder is a dossier number: equal to the patient's, it makes a
 * close name sure; different, it makes even an identical name a question.
 */
export function matchFolder(folder: string, targets: MatchTarget[]): MatchVerdict {
  const number = folderNumber(folder)
  const name = prepare(folder)
  if (!name.words.length) return { kind: "none" }
  // Two records of one person (about to be merged) share a key: one candidate, not a tie.
  const byKey = new Map<string, { key: string; score: number; numberAgrees: boolean | null }>()
  for (const t of targets) {
    let best = 0
    for (const n of namesOf(t)) best = Math.max(best, score(name, n))
    const numberAgrees = number != null && t.number != null ? number === t.number : null
    const held = byKey.get(t.key)
    if (!held || best > held.score) byKey.set(t.key, { key: t.key, score: best, numberAgrees: numberAgrees ?? held?.numberAgrees ?? null })
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
