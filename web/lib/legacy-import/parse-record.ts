import type { PdfCell } from "./pdf-rows"

/**
 * Reads ONE old software's « dossier patient » PDF (an iText export, A4, a fixed form), learnt from its real exports:
 *
 *   « Dossier créé le mercredi 19 octobre 2022 sous le numéro 8/2022 »
 *   a two-column grid of « Label: » cells, each followed by its value cell
 *     (« Patient(e) adressé(e) par: X » is ONE cell)
 *   then sections whose header is alone on its row at x≈36 (grid labels sit at x≈38):
 *     « Histoire de la maladie: », « Diagnostic: », « Autres informations: » (Poids/Taille/IMC/GS, a grid again),
 *     « Historique consultations|préscriptions|radios: ».
 *
 * ⚠️ The three « Historique » sections are past care. They are never turned into fiches (a fiche makes money, chart
 * marks and debts); they become read-only lines of the « Ancien logiciel » history.
 */

export interface LegacyHistoryEntry {
  /** `yyyy-MM-dd`, or null when the line carries no date. */
  date: string | null
  category: "Consultation" | "Ordonnance" | "Radio" | "Diagnostic"
  title: string
  details: string | null
}

export interface LegacyRecord {
  numero: string
  lastName: string
  firstName: string
  /** As printed: `dd/mm/yyyy`. */
  birth: string
  /** `d/m/yyyy`, from the « Dossier créé le » header. Empty when the header is missing. */
  opened: string
  /** The header as printed, kept for the remark when it cannot be read. */
  openedRaw: string
  phone: string
  phone2: string
  email: string
  gender: string
  maritalStatus: string
  occupation: string
  countryOfOrigin: string
  governorate: string
  city: string
  street: string
  referredBy: string
  insurance: string
  otherContactName: string
  otherContactPhone: string
  antecedents: string
  diagnostic: string
  /** The « Historique … » sections present, by name — for the plan's count. */
  historySections: string[]
  /** Section headers this reader does not know: listed, never silently dropped. */
  unknownSections: string[]
}

const GRID = new Set([
  "Prénom", "Nom", "Date de naissance", "Sexe", "Situation familiale", "Profession", "Assurances", "Email",
  "Tél 1 Patient", "Tél 2 Patient", "Nom autre contacte", "Tél 1 autre contact", "Tél 2 autre contact",
  "Pays d'origine", "Gouvernorat", "Ville", "Adresse", "Patient(e) adressé(e) par", "Poids", "Taille", "IMC", "GS",
])
const KNOWN_SECTIONS = new Set([
  "Histoire de la maladie", "Diagnostic", "Autres informations",
  "Historique consultations", "Historique préscriptions", "Historique radios",
])
const MONTHS: Record<string, number> = {
  janvier: 1, fevrier: 2, mars: 3, avril: 4, mai: 5, juin: 6, juillet: 7, aout: 8, septembre: 9, octobre: 10,
  novembre: 11, decembre: 12,
}

/** Accents off, lower case — « Décembre » and « decembre » are one month. */
export const plain = (s: string) => s.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase()

export function parseRecord(rows: PdfCell[][]): LegacyRecord {
  const grid: Record<string, string> = {}
  const sections: Record<string, string[]> = {}
  let section: string | null = null
  for (const cells of rows) {
    // A section header is a single cell at the left margin ending with « : » that is not a grid label.
    const head = cells.length === 1 && cells[0].x <= 37 ? cells[0].s.match(/^(.+?)\s*:$/) : null
    if (head && !GRID.has(head[1])) {
      section = head[1]
      sections[section] ??= []
      continue
    }
    if (section?.startsWith("Historique")) continue
    if (section && section !== "Autres informations") {
      sections[section].push(cells.map((c) => c.s).join(" "))
      continue
    }
    let label: string | null = null
    for (const c of cells) {
      const m = c.s.match(/^(.+?)\s*:\s*(.*)$/)
      if (m && GRID.has(m[1])) {
        label = m[1]
        grid[label] = m[2] ?? ""
        continue
      }
      if (label) grid[label] = `${grid[label]} ${c.s}`.trim()
    }
  }
  const g = (k: string) => (grid[k] ?? "").trim()
  const header = rows.map((r) => r.map((c) => c.s).join(" ")).find((t) => /^Dossier cr/i.test(t)) ?? ""
  const hm = header.match(/cr[ée]{1,2} le\s+(?:\p{L}+\s+)?(\d{1,2})(?:er)?\s+(\p{L}+)\s+(\d{4})/u)
  const month = hm ? MONTHS[plain(hm[2])] : undefined

  return {
    numero: header.match(/num[ée]ro\s+(\S+)/)?.[1] ?? "",
    lastName: g("Nom"),
    firstName: g("Prénom"),
    birth: g("Date de naissance"),
    opened: hm && month ? `${hm[1]}/${month}/${hm[3]}` : "",
    openedRaw: header.replace(/^Dossier cr\S*\s*le\s*/i, ""),
    phone: g("Tél 1 Patient"),
    phone2: g("Tél 2 Patient"),
    email: g("Email"),
    gender: g("Sexe"),
    maritalStatus: g("Situation familiale"),
    occupation: g("Profession"),
    countryOfOrigin: g("Pays d'origine"),
    governorate: g("Gouvernorat"),
    city: g("Ville"),
    street: g("Adresse"),
    referredBy: g("Patient(e) adressé(e) par"),
    insurance: g("Assurances"),
    otherContactName: g("Nom autre contacte"),
    otherContactPhone: g("Tél 1 autre contact") || g("Tél 2 autre contact"),
    antecedents: (sections["Histoire de la maladie"] ?? []).join(" ; "),
    diagnostic: (sections["Diagnostic"] ?? []).join(" ; "),
    historySections: Object.keys(sections).filter((s) => s.startsWith("Historique")),
    unknownSections: Object.keys(sections).filter((s) => !KNOWN_SECTIONS.has(s)),
  }
}

/** « 19/10/2022 » or « 19.10.2022 » → « 2022-10-19 »; anything else → empty. */
export function isoDate(s: string): string {
  const m = s.trim().match(/^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$/)
  return m ? `${m[3]}-${m[2].padStart(2, "0")}-${m[1].padStart(2, "0")}` : ""
}

/** « 00218 91 234 567 » → « +21891234567 »; spaces, dots, dashes and brackets off. */
export function phoneOf(s: string): string {
  return s.replace(/[\s.\-/()]/g, "").replace(/^00/, "+")
}

/** The patient form's own four words; anything else is kept as written. */
export function maritalStatusOf(s: string): string {
  const k = plain(s).replace(/[^a-z]/g, "")
  if (!k) return ""
  if (k.startsWith("mari")) return "Marié(e)"
  if (k.startsWith("celib")) return "Célibataire"
  if (k.startsWith("divor")) return "Divorcé(e)"
  if (k.startsWith("veu")) return "Veuf / veuve"
  return s.trim()
}

/** « M » / « F » → the stored token; anything else is left out rather than guessed. */
export function genderOf(s: string): string | undefined {
  const k = plain(s).trim()
  if (["m", "h", "homme", "masculin"].includes(k)) return "Male"
  if (["f", "femme", "feminin"].includes(k)) return "Female"
  return undefined
}

/** « BEN SALAH » → « Ben Salah », « heba-ali » → « Heba-Ali ». */
export function titleCase(s: string): string {
  return s.toLowerCase().replace(/(^|[\s'-])(\p{L})/gu, (_m, a: string, b: string) => a + b.toUpperCase())
}

/**
 * The name from the FILE name, used only when the PDF has no text. A guess, always shown for review:
 *   words in capitals are the last name (« BEN SALAH Mohamed »);
 *   her export « 8-Prénom Nom.pdf » (numbered) → last word is the last name;
 *   anything else « Nom Prénom » → last word is the first name.
 */
export function splitFileName(name: string): { lastName: string; firstName: string } {
  const numbered = /^\s*\d+\s*-/.test(name)
  const clean = name.replace(/\.pdf$/i, "").replace(/^\s*\d+\s*-\s*/, "").replace(/\(\d+\)\s*$/, "").replace(/\s+/g, " ").trim()
  const words = clean.split(" ")
  const caps = words.filter((w) => w === w.toUpperCase() && /[A-Z]/.test(w))
  if (caps.length && caps.length < words.length) {
    return { lastName: caps.join(" "), firstName: words.filter((w) => !caps.includes(w)).join(" ") }
  }
  if (words.length < 2) return { lastName: clean, firstName: "" }
  return numbered
    ? { lastName: words.at(-1)!, firstName: words.slice(0, -1).join(" ") }
    : { lastName: words.slice(0, -1).join(" "), firstName: words.at(-1)! }
}

/** « Heba Ali  Faraj », « FARAJ Heba-Ali » and « 15 - Heba Ali Faraj » → one key: words, accents off, any order. */
export function nameKey(s: string): string {
  return plain(s).replace(/[^a-z0-9]+/g, " ").trim().split(" ").filter(Boolean).sort().join(" ")
}

export const withoutNumber = (s: string) => s.replace(/^\s*\d+\s*[-_.)]?\s*/, "")
export const leadingNumber = (s: string) => s.match(/^\s*(\d+)\s*[-_.)]/)?.[1] ?? null
