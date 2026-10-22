/**
 * QA walk for features/clinic-pc-copy/qa/plan.md — « Retirer ce PC » behind an authenticator code.
 * Isolated stack only: the relay e2e cloud published on :5098 (prefix rtb) and the worktree's next dev on :3099.
 *
 *   PW_DIR=<folder holding node_modules/playwright-core> SHOTS=<dir> node walk.mjs
 */
import crypto from "node:crypto"
import fs from "node:fs"
import path from "node:path"
import { execSync } from "node:child_process"
import { createRequire } from "node:module"
import { fileURLToPath } from "node:url"

const { chromium } = createRequire(path.join(process.env.PW_DIR, "x.js"))("playwright-core")
const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, "../../../e2e/relay-roundtrip/.out-rtb")
const SHOTS = process.env.SHOTS ?? path.join(HERE, "shots")
fs.mkdirSync(SHOTS, { recursive: true })
const WEB = "http://localhost:3099"

const env = Object.fromEntries(fs.readFileSync(path.join(OUT, "bootstrap.txt"), "utf8").split(/\r?\n/)
  .filter((l) => l.includes("=")).map((l) => [l.slice(0, l.indexOf("=")), l.slice(l.indexOf("=") + 1)]))
const EMAIL = env.CLINIC_EMAIL, PASSWORD = env.CLINIC_PASSWORD, SECRET = env.CLINIC_TOTP_SECRET
const state = JSON.parse(fs.readFileSync(path.join(OUT, "state.json"), "utf8"))
const patientId = state.notes.find((n) => n.side === "cloud").patientId

const sql = (q) => execSync(`docker exec rtb-pg-cloud psql -U clinic -d cloud -tAc "${q.replace(/"/g, '\\"')}"`,
  { encoding: "utf8" }).trim()
const retiredAt = () => sql(`SELECT coalesce("RetiredAtUtc"::text,'') FROM "ClinicRelays" WHERE "Label" = 'PC-CI'`)

function totp() {
  const A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
  let bits = ""
  for (const c of SECRET.replace(/[\s=]/g, "").toUpperCase()) bits += A.indexOf(c).toString(2).padStart(5, "0")
  const key = Buffer.from((bits.match(/.{8}/g) ?? []).map((b) => parseInt(b, 2)))
  const counter = Math.floor(Date.now() / 1000 / 30)
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0)
  buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac("sha1", key).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, "0")
}
const freshWindow = () => new Promise((r) => setTimeout(r, (30 - (Math.floor(Date.now() / 1000) % 30) + 2) * 1000))

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = "") => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }

const browser = await chromium.launch({ channel: "chrome", headless: true })
const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "fr-FR" })
const page = await context.newPage()
const shot = (name) => page.screenshot({ path: path.join(SHOTS, `${name}.png`) })

// ── sign in (password, then the authenticator) ──
await page.goto(`${WEB}/login`)
await page.locator("#email").fill(EMAIL)
await page.locator("#password").fill(PASSWORD)
await page.locator("#password").press("Enter")
await page.locator('input[autocomplete="one-time-code"]').waitFor({ timeout: 30000 })
await freshWindow()
await page.locator('input[autocomplete="one-time-code"]').fill(totp())
await page.locator('input[autocomplete="one-time-code"]').press("Enter")
await page.waitForURL((u) => !u.pathname.startsWith("/login"), { timeout: 30000 })
console.log("signed in")

const card = () => page.locator("#pc-de-secours")
const openSettings = async () => {
  await page.goto(`${WEB}/settings#pc-de-secours`)
  await card().getByText("PC-CI").first().waitFor({ timeout: 30000 })
  await card().scrollIntoViewIfNeeded()
}
const stepUp = () => page.getByRole("dialog").filter({ hasText: "Confirmer votre identité" })
const confirm = () => page.getByRole("alertdialog")
const toConfirm = async () => {
  await card().getByRole("button", { name: "Retirer ce PC" }).click()
  await confirm().waitFor()
}
// A dialog slides in: measure and photograph it only once every animation has finished.
const settled = () => page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== "running"))
const toStepUp = async () => {
  await toConfirm()
  await confirm().getByRole("button", { name: "Retirer ce PC" }).click()
  await stepUp().waitFor()
  await settled()
}

const SCENARIOS = [
  { id: "A1", run: async () => {
    await openSettings()
    const has = await card().getByRole("button", { name: "Retirer ce PC" }).isVisible()
    await shot("A1-card")
    has ? ok("A1", "card shows « PC-CI » with « Retirer ce PC »") : bad("A1", "no « Retirer ce PC » on the card")
  } },
  { id: "B1", run: async () => {
    await openSettings()
    await toConfirm()
    const title = (await confirm().getByRole("heading").first().innerText()).replace(/\s/g, " ")
    await shot("B1-confirm")
    await confirm().getByRole("button", { name: "Annuler" }).click()
    await confirm().waitFor({ state: "hidden" })
    const stepUpShown = await stepUp().isVisible()
    const r = retiredAt()
    if (/Retirer « PC-CI » \?/.test(title) && !stepUpShown && r === "") ok("B1", `confirm « ${title} » cancelled, nothing retired`)
    else bad("B1", "cancel at the confirm", `title=${title} stepUp=${stepUpShown} retiredAt=${r}`)
  } },
  { id: "B2", run: async () => {
    await openSettings()
    await toStepUp()
    const text = await stepUp().innerText()
    const codeField = await stepUp().locator('input[autocomplete="one-time-code"]').count()
    const pwField = await stepUp().locator('input[type="password"]').count()
    await shot("B2-stepup")
    await stepUp().getByRole("button", { name: "Annuler" }).click()
    await stepUp().waitFor({ state: "hidden" })
    const r = retiredAt()
    if (text.includes("Vous allez retirer le PC de secours : sa copie s'arrête.") && codeField === 1 && pwField === 0 && r === "")
      ok("B2", "step-up names the retire, asks a code (no password), cancel retires nothing")
    else bad("B2", "step-up dialog", `code=${codeField} pw=${pwField} retiredAt=${r} text=${text.slice(0, 200)}`)
  } },
  { id: "B3", run: async () => {
    await openSettings()
    await toStepUp()
    await stepUp().locator('input[autocomplete="one-time-code"]').fill("000000")
    await stepUp().getByRole("button", { name: "Confirmer" }).click()
    await stepUp().getByRole("alert").first().waitFor({ timeout: 15000 }).catch(() => {})
    const still = await stepUp().isVisible()
    const err = still ? await stepUp().getByRole("alert").first().innerText().catch(() => "") : ""
    await shot("B3-wrong-code")
    const r = retiredAt()
    if (still && err && r === "") ok("B3", `wrong code refused in place: « ${err} »`)
    else bad("B3", "wrong code", `open=${still} error=${err} retiredAt=${r}`)
    if (still) { await stepUp().getByRole("button", { name: "Annuler" }).click(); await stepUp().waitFor({ state: "hidden" }) }
  } },
  { id: "C1", run: async () => {
    const sizes = [[320, 640, "320"], [1536, 730, "1536x730"]]
    const out = []
    for (const [w, h, name] of sizes) {
      await page.setViewportSize({ width: w, height: h })
      await openSettings()
      await toStepUp()
      const vw = await page.evaluate(() => [window.innerWidth, document.documentElement.scrollWidth])
      const inView = async (label) => {
        const b = await stepUp().getByRole("button", { name: label }).boundingBox()
        return b && b.x >= 0 && b.y >= 0 && b.x + b.width <= w && b.y + b.height <= h
      }
      const fits = (await inView("Confirmer")) && (await inView("Annuler")) && vw[1] <= vw[0]
      await shot(`C1-stepup-${name}`)
      out.push(`${name}:${fits ? "fits" : `OVERFLOW scroll=${vw[1]} vw=${vw[0]}`}`)
      await stepUp().getByRole("button", { name: "Annuler" }).click()
      await stepUp().waitFor({ state: "hidden" })
    }
    await page.setViewportSize({ width: 1440, height: 900 })
    out.every((s) => s.endsWith("fits")) ? ok("C1", out.join(" · ")) : bad("C1", "step-up off screen", out.join(" · "))
  } },
  { id: "D1", run: async () => {
    const city = `Ville QA ${Date.now() % 100000}`
    await page.goto(`${WEB}/patients/${patientId}`)
    await page.getByRole("button", { name: "Modifier le patient" }).first().click()
    const dlg = page.getByRole("dialog").first()
    await dlg.waitFor()
    const address = dlg.getByLabel(/^Adresse/).first()
    await address.fill(city)
    await dlg.getByRole("button", { name: /^Enregistrer/ }).first().click()
    await dlg.waitFor({ state: "hidden", timeout: 15000 }).catch(() => {})
    await shot("D1-after-save")
    const closed = !(await dlg.isVisible())
    const stored = sql(`SELECT count(*) FROM "Patients" WHERE "Id" = '${patientId}' AND row_to_json("Patients")::text LIKE '%${city}%'`)
    if (closed && stored === "1") ok("D1", `ordinary patient save went through and kept « ${city} »`)
    else bad("D1", "ordinary save", `closed=${closed} stored=${stored}`)
  } },
  { id: "D2", run: async () => {
    await openSettings()
    await card().getByRole("button", { name: "Déclarer perdu ou volé" }).click()
    await confirm().waitFor()
    await confirm().getByRole("button", { name: "Continuer" }).click()
    await stepUp().waitFor()
    const text = await stepUp().innerText()
    await shot("D2-lost-stepup")
    await stepUp().getByRole("button", { name: "Annuler" }).click()
    await stepUp().waitFor({ state: "hidden" })
    const r = retiredAt()
    if (text.includes("déclarer ce PC perdu ou volé") && !text.includes("Vous allez retirer") && r === "")
      ok("D2", "« Déclarer perdu ou volé » keeps its own step-up purpose; cancel declares nothing")
    else bad("D2", "lost step-up", `retiredAt=${r} text=${text.slice(0, 200)}`)
  } },
  { id: "A2", run: async () => {
    await openSettings()
    await toStepUp()
    await freshWindow()
    await stepUp().locator('input[autocomplete="one-time-code"]').fill(totp())
    await stepUp().getByRole("button", { name: "Confirmer" }).click()
    const toast = await page.getByText("PC de secours retiré.").first().waitFor({ timeout: 20000 }).then(() => true, () => false)
    await page.waitForTimeout(1000)
    const stillOffered = await card().getByRole("button", { name: "Retirer ce PC" }).isVisible().catch(() => false)
    await shot("A2-retired")
    const r = retiredAt()
    const journal = sql(`SELECT count(*) FROM "AuditEntries" WHERE "EntityType" = 'ClinicRelay' AND "ChangedFields" LIKE 'PC de secours retiré%'`)
    if (toast && !stillOffered && r !== "" && journal === "1") ok("A2", `retired with a code: toast, card updated, journal row (at ${r})`)
    else bad("A2", "retire with the right code", `toast=${toast} stillOffered=${stillOffered} retiredAt=${r} journal=${journal}`)
  } },
]

for (const s of SCENARIOS) {
  try { await s.run() } catch (e) {
    bad(s.id, "threw", e.message.split("\n")[0])
    await page.keyboard.press("Escape").catch(() => {})
    await page.keyboard.press("Escape").catch(() => {})
  }
}
skip("X1", "the « maybe already saved » switch refusal needs the Windows/Android shell and a real server switch")
skip("B4", "API row — covered by e2e/relay-roundtrip drive.mjs retire (403 without a code)")

await browser.close()
console.log()
const real = findings.filter((f) => !f.why)
real.length ? real.forEach((f) => console.log(f)) : console.log("ALL CLEAR (browser rows)")
