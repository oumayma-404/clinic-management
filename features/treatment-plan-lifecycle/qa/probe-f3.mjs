// F3 — is the 20 px patient link a real coarse-pointer gap, or did the emulation not take?
// `PatientNameLink` already declares `coarse:min-h-11`, so this decides between « the rule is missing » and
// « my probe never entered coarse mode ».
import { chromium } from "../../../e2e/node_modules/playwright-core/index.mjs"
import { signIn, ORIGIN } from "./auth.mjs"
import { readFileSync } from "node:fs"
import { join, dirname } from "node:path"
import { fileURLToPath } from "node:url"

const HERE = dirname(fileURLToPath(import.meta.url))
const state = JSON.parse(readFileSync(join(HERE, ".artifacts", "state.json"), "utf8"))

await signIn()
const browser = await chromium.launch({ channel: "chrome", headless: false })
const ctx = await browser.newContext({
  storageState: join(HERE, ".artifacts", "session.json"),
  viewport: { width: 320, height: 730 },
  locale: "fr-FR",
  // `hasTouch` is what actually makes `(pointer: coarse)` match; CDP's setEmulatedMedia features alone did not.
  hasTouch: true,
  isMobile: true,
})
await ctx.route("**/api/notifications/pending-reviews*", (r) =>
  r.fulfill({ status: 200, contentType: "application/json", body: "[]" }),
)
const page = await ctx.newPage()
const cdp = await ctx.newCDPSession(page)

const measure = async (label) => {
  const out = await page.evaluate(() => {
    const a = [...document.querySelectorAll('a[href^="/patients/"]')].find((e) => e.getBoundingClientRect().height > 0)
    return {
      coarseMatches: matchMedia("(pointer: coarse)").matches,
      anyCoarse: matchMedia("(any-pointer: coarse)").matches,
      found: !!a,
      h: a ? Math.round(a.getBoundingClientRect().height) : null,
      minH: a ? getComputedStyle(a).minHeight : null,
      cls: a ? String(a.className).slice(0, 160) : null,
    }
  })
  console.log(`  ${label.padEnd(18)} coarse=${out.coarseMatches} any=${out.anyCoarse} h=${out.h} minHeight=${out.minH}`)
  return out
}

await page.goto(`${ORIGIN}/treatment-plans/${state.plans.cancelled.id}`, { waitUntil: "domcontentloaded" })
await page.getByRole("button", { name: "Autres actions sur ce devis" }).waitFor({ timeout: 25000 })
await page.waitForTimeout(1500)
const before = await measure("fine pointer")

await cdp.send("Emulation.setEmulatedMedia", {
  media: "screen",
  features: [
    { name: "pointer", value: "coarse" },
    { name: "any-pointer", value: "coarse" },
  ],
})
await page.reload({ waitUntil: "domcontentloaded" })
await page.getByRole("button", { name: "Autres actions sur ce devis" }).waitFor({ timeout: 25000 })
await page.waitForTimeout(1500)
const after = await measure("coarse emulated")
console.log(`  class: ${after.cls}`)

console.log(
  after.coarseMatches
    ? after.h >= 44
      ? "\n  VERDICT: probe bug in walk.mjs — the link IS 44 px on a real coarse pointer."
      : "\n  VERDICT: CONFIRMED — coarse matches and the link is still under 44 px."
    : "\n  VERDICT: the emulation never took (coarse=false) — every earlier target measurement is void.",
)

await browser.close()
