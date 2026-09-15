// ⚠️ Two traps, both measured here.
//   · `fullPage: true` captures the DOCUMENT, and this app scrolls inside `AppShell`'s `<main>` — so a
//     full-page shot of a plan workspace is the viewport again, and everything below the fold is missing.
//   · An `xpath=ancestor::…[n]` guess picks the wrong box: [2] caught the heading row alone, [4] caught the
//     whole page. Scroll the heading to the top of the SCROLLER and shoot the viewport instead.
// M23 (the échéance rows) and m17 (the money figures) only exist below the fold at 320 px.
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

/** Puts `text` at the top of whichever element is actually scrolling. */
async function scrollTo(text) {
  return page.evaluate((needle) => {
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT)
    let target = null
    while (walker.nextNode()) {
      const el = walker.currentNode
      if (el.children.length === 0 && (el.textContent || "").trim() === needle) {
        target = el
        break
      }
    }
    if (!target) return false
    let sc = target.parentElement
    while (sc && sc !== document.body) {
      const st = getComputedStyle(sc)
      if (/(auto|scroll)/.test(st.overflowY) && sc.scrollHeight > sc.clientHeight + 4) break
      sc = sc.parentElement
    }
    const scroller = sc && sc !== document.body ? sc : document.scrollingElement
    const delta = target.getBoundingClientRect().top - scroller.getBoundingClientRect().top
    scroller.scrollTop += delta - 8
    return true
  }, text)
}

for (const key of ["stoppable", "acts"]) {
  const plan = state.plans[key]
  if (!plan) continue
  for (const w of [320, 390, 1440]) {
    await page.setViewportSize({ width: w, height: w === 1440 ? 900 : 730 })
    await page.goto(`${ORIGIN}/treatment-plans/${plan.id}`, { waitUntil: "domcontentloaded" })
    await page.getByRole("button", { name: "Autres actions sur ce devis" }).waitFor({ timeout: 25000 })
    await page.waitForTimeout(1500)
    for (const [name, text] of [
      ["actes", "Actes"],
      ["argent", "L’argent"],
      ["argent2", "L'argent"],
    ]) {
      const found = await scrollTo(text)
      if (!found) continue
      await page.waitForTimeout(500)
      const tag = `${key}-${name}-${w}`
      await page
        .screenshot({ path: join(SHOTS, `${tag}.png`), animations: "disabled", timeout: 15000 })
        .then(() => console.log(`  ${tag} ✅`))
        .catch((e) => console.log(`  ${tag} skipped: ${String(e.message).split("\n")[0]}`))
    }
  }
}

await browser.close()
