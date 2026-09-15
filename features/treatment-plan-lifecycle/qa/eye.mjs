// The eye pass (Phase 6 step 6): full-page captures at every named width, on the plans whose money rows
// M22 · M23 · m17 are only provable in a browser. Read the PNGs — a screenshot nobody opened is a file.
import { chromium } from "../../../e2e/node_modules/playwright-core/index.mjs"
import { signIn, ORIGIN } from "./auth.mjs"
import { readFileSync, mkdirSync } from "node:fs"
import { join, dirname } from "node:path"
import { fileURLToPath } from "node:url"

const HERE = dirname(fileURLToPath(import.meta.url))
const SHOTS = join(HERE, "shots", "eye")
mkdirSync(SHOTS, { recursive: true })
const state = JSON.parse(readFileSync(join(HERE, ".artifacts", "state.json"), "utf8"))

await signIn()
const browser = await chromium.launch({ channel: "chrome", headless: false })
const ctx = await browser.newContext({
  storageState: join(HERE, ".artifacts", "session.json"),
  viewport: { width: 1440, height: 900 },
  locale: "fr-FR",
})
await ctx.route("**/api/notifications/pending-reviews*", (r) =>
  r.fulfill({ status: 200, contentType: "application/json", body: "[]" }),
)
const page = await ctx.newPage()

const WIDTHS = [320, 390, 820, 1180, 1440]
// 1536 × 730 is the owner's real laptop — a footer that clears the fold at 900 can need scrolling in theirs.
const EXTRA = [[1536, 730]]

for (const key of ["stoppable", "acts", "cancelled"]) {
  const plan = state.plans[key]
  if (!plan) continue
  for (const [w, h] of [...WIDTHS.map((w) => [w, 900]), ...EXTRA]) {
    await page.setViewportSize({ width: w, height: h })
    await page.goto(`${ORIGIN}/treatment-plans/${plan.id}`, { waitUntil: "domcontentloaded" })
    await page.getByRole("button", { name: "Autres actions sur ce devis" }).waitFor({ timeout: 25000 })
    await page.waitForTimeout(1600)
    const o = await page.evaluate(() => ({
      docW: document.documentElement.scrollWidth,
      winW: window.innerWidth,
    }))
    const tag = `${key}-${w}x${h}`
    await page
      .screenshot({ path: join(SHOTS, `${tag}.png`), fullPage: true, animations: "disabled", timeout: 20000 })
      .catch((e) => console.log(`  (skip ${tag}: ${String(e.message).split("\n")[0]})`))
    console.log(`  ${tag.padEnd(22)} doc ${o.docW} / win ${o.winW} ${o.docW > o.winW + 1 ? "❌ OVERFLOW" : "✅"}`)
  }
}

await browser.close()
console.log(`\n  shots → qa/shots/eye/`)
