#!/usr/bin/env node
/**
 * `check-coverage` — the hot-path suite's derived guard.
 *
 * ⚠️ **Why this exists.** On 2026-09-11 the catalogue held 271 scenarios and the suite 119 tests, and nobody
 * could say which rows those tests covered. The answer, measured by this script's first run: **159 scenarios
 * had no test at all, 48 of them tier-0** — and of the 119 tests, only 21 ever opened a browser. Both numbers
 * were invisible because coverage was a *claim* in a markdown table, updated by hand, and a hand-maintained
 * list drifts from the thing it describes. (`verify-schema`'s own column list did exactly this and missed the
 * one column that was breaking every fiche save — `findings.md` § 2.)
 *
 * So nothing here is maintained by hand. Both sides are **parsed**:
 *
 *   - the catalogue  `features/e2e-hot-paths/scenarios.md` → every row's id and tier
 *   - the suite      `e2e/specs/*.spec.ts`                 → every id a test names, and whether that test
 *                                                            opens a browser
 *
 * and the three failures below are the disagreements between them.
 *
 * ## The three failures
 *
 * | # | Fails when | Why it is worth a red build |
 * |---|---|---|
 * | 1 | a **tier-0** row has no test | tier 0 is defined as « money or clinical fact is wrong and nothing says so ». An untested one is the product's whole risk surface |
 * | 2 | a row listed in § « Layer » as **browser** is covered only by a wire test | ⚠️ the expensive one — see below |
 * | 3 | a test names an id **whose base row** the catalogue does not have | a renamed or deleted row leaves a test asserting something nobody can look up, which is how a suite grows tests that assert the opposite of the product |
 *
 * ⚠️ A **sub-lettered** id (`FICHE-43a`, `CONT-05b`) is a legitimate refinement of its base row, not an
 * unknown one — `CONT-05b` is itself a catalogue row, added when the pass found the defect it names. So it
 * counts as covering its base (`FICHE-43`) and is only *reported*, so the catalogue can catch up. What still
 * fails is a variant whose **base** row does not exist: that is a renamed row, and the test attached to it is
 * asserting something nobody can look up.
 *
 * ## Why failure 2 is the one that matters
 *
 * A wire test posts a body **the test author wrote**. It proves the handler is right about that body. It
 * proves nothing about the body the product sends — and in this product a great deal of the money logic is
 * client-side: `distributeSessionTotal`, `act-card`'s withholding of a carried act's price, `agreedCostOf`,
 * the fiche's prefill, `materialiseTreatments`, `useConflict`'s recovery from a 409.
 *
 * Measured, and the reason this check is here: every one of the five `fix(...)` commits between the
 * 2026-09-08 pass and 2026-09-11 was a defect no wire test could see. The sharpest is `24f2883e` — « le total
 * d'un acte modifié depuis le rendez-vous part enfin au serveur »: a price the dentist typed **never reached
 * the server**. That is money, it is scenario BOOK-48, and a wire test of BOOK-48 would have passed for the
 * whole time it was broken, because the wire test sends the price itself.
 *
 * Usage:
 *   node e2e/scripts/check-coverage.mjs            # report + exit 1 on any failure
 *   node e2e/scripts/check-coverage.mjs --report   # report only, always exit 0 (what CI runs first)
 *   node e2e/scripts/check-coverage.mjs --write    # also write features/e2e-hot-paths/coverage.tsv
 *
 * ⚠️ `coverage.tsv` is **generated, never edited**. It exists so a human can read the current state without
 * running node; editing it changes nothing, which is precisely why the ledger is not the source of truth.
 */

import { readFileSync, writeFileSync, readdirSync } from "node:fs"
import { fileURLToPath } from "node:url"
import { dirname, join } from "node:path"

const HERE = dirname(fileURLToPath(import.meta.url))
const REPO = join(HERE, "..", "..")
const CATALOGUE = join(REPO, "features", "e2e-hot-paths", "scenarios.md")
const SPECS_DIR = join(REPO, "e2e", "specs")
const LEDGER = join(REPO, "features", "e2e-hot-paths", "coverage.tsv")

/** The id shape every scenario uses. Kept in one place so the catalogue and the specs are read the same way. */
const ID = "(?:CAT|BOOK|EDIT|STEP|FICHE|FEDIT|PLAN|CONT|DONE|MONEY|ODO|HIST|XCUT|DEL|STOP|INACH|REDO)-[0-9]+[a-z]?"

// ── The catalogue ────────────────────────────────────────────────────────────────────────────────────────────

/**
 * Every scenario row, as `{ id, tier, section }`.
 *
 * ⚠️ Rows are read from the **tables**, not from a count written underneath them. The catalogue's own
 * « Coverage count » § said 268 while the tables held 271 — three rows had been added without touching it,
 * which is what a hand-kept number does.
 */
function readCatalogue() {
  const text = readFileSync(CATALOGUE, "utf8")
  const rowRe = new RegExp(`^\\|\\s*\\*{0,2}(${ID})\\*{0,2}\\s*\\|\\s*\\*{0,2}([012])\\*{0,2}\\s*\\|`)
  const rows = new Map()
  let section = "(before any section)"

  for (const line of text.split(/\r?\n/)) {
    const heading = /^#{1,2}\s+(.+?)\s*$/.exec(line)
    if (heading) section = heading[1]
    const m = rowRe.exec(line)
    if (m && !rows.has(m[1])) rows.set(m[1], { id: m[1], tier: Number(m[2]), section })
  }
  return rows
}

/**
 * The ids § « Layer » names as browser-only, i.e. the rows whose rule lives in `.tsx` or `web/lib/`.
 *
 * ⚠️ **An opt-IN list, deliberately, and not a column on every row.** Marking all 271 rows would mean 271
 * judgements to keep true, and the ones that are merely « wire is fine » carry no risk when they drift. What
 * carries risk is the opposite mistake — a client-side rule tested on the wire — so that is the only thing
 * stated, and stating it is cheap enough that nobody is tempted to skip it.
 */
function readBrowserLayer() {
  const text = readFileSync(CATALOGUE, "utf8")
  const start = text.indexOf("<!-- LAYER:BROWSER:BEGIN -->")
  const end = text.indexOf("<!-- LAYER:BROWSER:END -->")
  if (start === -1 || end === -1) return new Set()
  const block = text.slice(start, end)
  return new Set(block.match(new RegExp(ID, "g")) ?? [])
}

// ── The suite ────────────────────────────────────────────────────────────────────────────────────────────────

/**
 * Every id a test names, mapped to `{ files:Set, browser:boolean }`.
 *
 * A test counts as a **browser** test when its own block drives a page — `gotoApp(`, `page.` or a `{ page }`
 * fixture. Read per `test(` block rather than per file, because a spec file may legitimately hold both:
 * `concurrency.spec.ts` arranges two sessions on the wire and acts in two browsers.
 */
function readSpecs() {
  /*
   * ⚠️ **A compound title names more than one row, and reading only the first under-reports coverage.**
   * The suite's own convention is `MONEY-03/04 · a PARTIAL avoir leaves the note billing` and
   * `FICHE-30/32 · the chart waits for the act to be Done` — one test, two scenarios, because the two rows are
   * the same gesture asserted at two moments. A bare id scan sees `MONEY-03` and calls `MONEY-04` untested,
   * which is a false red on the one list this script exists to make true. So a trailing `/NN` run is expanded
   * back onto the id's own prefix.
   */
  const idRe = new RegExp(`${ID}(?:/[0-9]+[a-z]?)*`, "g")
  const expand = (match) => {
    const [head, ...rest] = match.split("/")
    const prefix = head.slice(0, head.lastIndexOf("-") + 1)
    return [head, ...rest.map((n) => prefix + n)]
  }
  const covered = new Map()
  const files = readdirSync(SPECS_DIR).filter((f) => f.endsWith(".ts"))

  for (const file of files) {
    const text = readFileSync(join(SPECS_DIR, file), "utf8")

    // Split on the start of each `test(` / `test.describe(` declaration. Index 0 is the file's imports and
    // helpers, which name no scenario and are dropped by the id scan anyway.
    const blocks = text.split(/\n(?=\s*test(?:\.\w+)*\s*\()/)
    for (const block of blocks) {
      const matches = block.match(idRe)
      if (!matches) continue
      const ids = matches.flatMap(expand)
      const browser = /\bgotoApp\s*\(|\bpage\s*\.|\{\s*page\s*[,}]/.test(block)
      for (const id of new Set(ids)) {
        // A sub-lettered id also covers its base row — `FICHE-43a` is a refinement of `FICHE-43`, not a
        // different scenario. Both are recorded so the un-catalogued variant can still be reported.
        const base = /[a-z]$/.test(id) ? id.slice(0, -1) : null
        for (const key of base ? [id, base] : [id]) {
          const entry = covered.get(key) ?? { files: new Set(), browser: false, variants: new Set() }
          entry.files.add(file)
          entry.browser = entry.browser || browser
          if (base && key === base) entry.variants.add(id)
          covered.set(key, entry)
        }
      }
    }
  }
  return covered
}

// ── The report ───────────────────────────────────────────────────────────────────────────────────────────────

const argv = new Set(process.argv.slice(2))
const reportOnly = argv.has("--report")
const write = argv.has("--write")

const catalogue = readCatalogue()
const browserLayer = readBrowserLayer()
const covered = readSpecs()

const untestedT0 = []
const untestedT1or2 = []
const wireOnlyButBrowserRule = []
const unknownIds = []

for (const [id, row] of catalogue) {
  const hit = covered.get(id)
  if (!hit) {
    ;(row.tier === 0 ? untestedT0 : untestedT1or2).push(row)
    continue
  }
  if (browserLayer.has(id) && !hit.browser) {
    wireOnlyButBrowserRule.push({ ...row, files: [...hit.files].join(" ") })
  }
}

const uncataloguedVariants = []
for (const id of covered.keys()) {
  if (catalogue.has(id)) continue
  const base = /[a-z]$/.test(id) ? id.slice(0, -1) : null
  if (base && catalogue.has(base)) uncataloguedVariants.push(id)
  else unknownIds.push(id)
}

const tiers = [0, 1, 2].map((t) => {
  const rows = [...catalogue.values()].filter((r) => r.tier === t)
  const done = rows.filter((r) => covered.has(r.id))
  const inBrowser = done.filter((r) => covered.get(r.id).browser)
  return { tier: t, total: rows.length, done: done.length, inBrowser: inBrowser.length }
})

const line = "─".repeat(94)
console.log(line)
console.log("  Hot-path scenario coverage — catalogue vs suite")
console.log(line)
for (const t of tiers) {
  const pct = t.total ? Math.round((t.done / t.total) * 100) : 100
  console.log(
    `  tier ${t.tier}   ${String(t.done).padStart(3)} / ${String(t.total).padEnd(3)} covered (${String(pct).padStart(3)}%)` +
      `   ${String(t.inBrowser).padStart(3)} of those in a browser`,
  )
}
console.log(
  `  total    ${[...catalogue.values()].filter((r) => covered.has(r.id)).length} / ${catalogue.size}` +
    `   ·   ${browserLayer.size} row(s) declared browser-layer`,
)
console.log(line)

const fail = (title, rows, render) => {
  if (rows.length === 0) return false
  console.log(`\n  ❌ ${title} — ${rows.length}`)
  for (const r of rows) console.log(`     ${render(r)}`)
  return true
}

let failed = false
failed = fail("tier-0 scenarios with NO test", untestedT0, (r) => `${r.id.padEnd(10)} ${r.section}`) || failed
failed =
  fail(
    "declared browser-layer, covered only on the wire",
    wireOnlyButBrowserRule,
    (r) => `${r.id.padEnd(10)} covered by ${r.files} — none of them opens a browser`,
  ) || failed
failed =
  fail(
    "tests naming an id whose BASE row the catalogue does not have",
    unknownIds,
    (id) => `${id} — renamed or deleted row?`,
  ) || failed

if (uncataloguedVariants.length) {
  console.log(`\n  ·  variants a spec refines but the catalogue does not list — ${uncataloguedVariants.length}`)
  console.log(`     ${uncataloguedVariants.sort().join(" ")}`)
}

if (untestedT1or2.length) {
  console.log(`\n  ·  tier-1/2 scenarios with no test — ${untestedT1or2.length} (not a failure)`)
  console.log(`     ${untestedT1or2.map((r) => r.id).join(" ")}`)
}

if (!failed) console.log("\n  ✅ every tier-0 scenario has a test, at the layer its rule lives in.")

if (write) {
  const rows = [...catalogue.values()]
    .sort((a, b) => a.id.localeCompare(b.id, "en", { numeric: true }))
    .map((r) => {
      const hit = covered.get(r.id)
      const layer = browserLayer.has(r.id) ? "browser" : "wire-or-unit"
      const status = !hit ? "none" : hit.browser ? "browser" : "wire"
      return [r.id, r.tier, layer, status, hit ? [...hit.files].join(",") : "", r.section].join("\t")
    })
  writeFileSync(
    LEDGER,
    ["# GENERATED by e2e/scripts/check-coverage.mjs — do not edit; edit scenarios.md and the specs instead.",
      "id\ttier\tlayer_required\tstatus\tspecs\tsection",
      ...rows].join("\n") + "\n",
    "utf8",
  )
  console.log(`\n  wrote ${LEDGER}`)
}

process.exit(failed && !reportOnly ? 1 : 0)
