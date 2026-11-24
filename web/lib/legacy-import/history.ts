import type { PdfCell } from "./pdf-rows"
import type { LegacyHistoryEntry, LegacyRecord } from "./parse-record"

/**
 * The record's past care as read-only lines of the patient's « Ancien logiciel » history — never fiches.
 *
 * Categories are the four words the history tab shows as-is: « Consultation », « Ordonnance », « Radio »,
 * « Diagnostic ». The server takes 1–500 lines per call, title ≤ 300, details ≤ 4 000, all-or-nothing.
 */
export function historyOf(rows: PdfCell[][], record: LegacyRecord): LegacyHistoryEntry[] {
  const out: LegacyHistoryEntry[] = []
  if (record.diagnostic) out.push({ date: null, category: "Diagnostic", title: "Diagnostic", details: record.diagnostic })
  out.push(...historyTables(rows))
  return out.map((e) => ({ ...e, title: e.title.slice(0, 300), details: e.details ? e.details.slice(0, 4000) : null }))
}

/*
 * The three « Historique consultations | préscriptions | radios » tables (written by the session that read the real
 * exports; byte-identical to migrate.mjs on the 25 real PDFs). Each table is a header row (Actes · Compte Rendu ·
 * Date …) then entries whose cells wrap over several rows: rows of one entry sit ≤ 18 pt apart, entries 27–28 pt
 * apart, and the date cell is vertically centred in its entry. Columns are fixed by the export. A table header
 * repeated on a new page is skipped; an entry cut by a page break is folded back together.
 */
type LegacyHistoryLine = Omit<LegacyHistoryEntry, "category"> & { category: "Consultation" | "Ordonnance" | "Radio" }

type Column = "a" | "b" | "c" | "date"
type Table = { category: LegacyHistoryLine["category"]; cols: (x: number) => Column }

const PRESCRIPTIONS: Table = { category: "Ordonnance", cols: (x) => (x < 150 ? "a" : x < 330 ? "b" : x < 470 ? "c" : "date") }
const HISTORY_TABLES: Record<string, Table> = {
  "Historique consultations": { category: "Consultation", cols: (x) => (x < 150 ? "a" : x < 470 ? "b" : "date") },
  "Historique préscriptions": PRESCRIPTIONS,
  "Historique prescriptions": PRESCRIPTIONS,
  "Historique radios": { category: "Radio", cols: (x) => (x < 150 ? "a" : x < 470 ? "b" : "date") },
}
const COLUMN_HEADERS = /^(Actes|Compte Rendu|Médicament|Medicament|Posologie|Note|Date|Radio)$/
const ENTRY_GAP = 22
const DAY = /^(\d{2})\/(\d{2})\/(\d{4})$/

function historyTables(rows: PdfCell[][]): LegacyHistoryLine[] {
  const out: LegacyHistoryLine[] = []
  let table: Table | null = null
  let cells: PdfCell[] = []
  const flush = () => {
    if (table) out.push(...tableEntries(table, cells))
    cells = []
  }
  for (const row of rows) {
    // A section header is alone on its row at x ≈ 36 and ends with « : » (grid labels sit at x ≈ 38).
    const head = row.length === 1 && row[0].x <= 37 ? row[0].s.match(/^(.+?)\s*:$/) : null
    if (head) {
      flush()
      table = HISTORY_TABLES[head[1]] ?? null
      continue
    }
    if (!table) continue
    if (row.every((c) => COLUMN_HEADERS.test(c.s))) continue // the table's header, also repeated on a new page
    cells.push(...row)
  }
  flush()
  return out
}

function tableEntries(table: Table, cells: PdfCell[]): LegacyHistoryLine[] {
  const key = (c: PdfCell) => `${c.page}|${c.y}`
  const isDate = (c: PdfCell) => table.cols(c.x) === "date" && DAY.test(c.s)

  // Cluster rows into entries: a new page, or a gap wider than a wrapped line, starts a new one.
  const rowsInOrder = [...new Map(cells.map((c) => [key(c), c])).values()].sort((a, b) => a.page - b.page || b.y - a.y)
  const groups: string[][] = []
  let prev: PdfCell | null = null
  for (const c of rowsInOrder) {
    if (!prev || c.page !== prev.page || prev.y - c.y > ENTRY_GAP) groups.push([])
    groups[groups.length - 1].push(key(c))
    prev = c
  }
  let entries = groups.map((keys) => cells.filter((c) => keys.includes(key(c))))

  // An entry cut by a page break has its date on one side only: fold a dateless piece into its neighbour.
  for (let i = 0; i < entries.length; i++) {
    if (entries[i].some(isDate) || entries.length === 1) continue
    const into = i > 0 ? i - 1 : i + 1
    entries[into] = i > 0 ? [...entries[into], ...entries[i]] : [...entries[i], ...entries[into]]
    entries.splice(i, 1)
    i--
  }

  // Two dates in one cluster (entries packed tighter than usual): each cell goes to the nearest date on its page.
  entries = entries.flatMap((e) => {
    const dates = e.filter(isDate)
    if (dates.length < 2) return [e]
    const nearest = (c: PdfCell) => {
      const samePage = dates.filter((d) => d.page === c.page)
      return (samePage.length ? samePage : dates).reduce((best, d) => (Math.abs(d.y - c.y) < Math.abs(best.y - c.y) ? d : best))
    }
    return dates.map((d) => e.filter((c) => c === d || (!dates.includes(c) && nearest(c) === d)))
  })

  const lines = entries.map((e) => {
    const col = (k: Column) =>
      e.filter((c) => table.cols(c.x) === k).sort((p, q) => p.page - q.page || q.y - p.y || p.x - q.x).map((c) => c.s)
    const d = col("date").map((s) => s.match(DAY)).find(Boolean)
    return { date: d ? `${d[3]}-${d[2]}-${d[1]}` : null, a: col("a").join(" "), b: col("b"), c: col("c").join(" ") }
  })

  if (table.category === "Ordonnance") {
    // One ordonnance per day: its médicaments, one per line (« NOM (voie) — posologie · note »).
    const byDay = new Map<string | null, string[]>()
    for (const l of lines) {
      if (!l.a && !l.b.length && !l.c) continue
      const line = l.a + (l.b.length ? ` — ${l.b.join(" ")}` : "") + (l.c ? ` · ${l.c}` : "")
      byDay.set(l.date, [...(byDay.get(l.date) ?? []), line.trim()])
    }
    return [...byDay].map(([date, meds]) => ({ date, category: "Ordonnance" as const, title: "Ordonnance", details: meds.join("\n") }))
  }
  return lines
    .filter((l) => l.a || l.b.length)
    .map((l) =>
      table.category === "Consultation"
        ? { date: l.date, category: "Consultation" as const, title: l.a || "Consultation", details: l.b.join("\n") || null }
        : { date: l.date, category: "Radio" as const, title: l.a || "Radio", details: l.b.join(" ") || null },
    )
}
