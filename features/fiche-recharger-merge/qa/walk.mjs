// QA walk — fiche de soins « Recharger » reconciles instead of overwriting. Plan: ./plan.md
// Run: node features/fiche-recharger-merge/qa/walk.mjs   (needs e2e/node_modules + docker clinic-postgres)
import { execFileSync } from "node:child_process"
import crypto from "node:crypto"
import { mkdirSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath, pathToFileURL } from "node:url"

const HERE = dirname(fileURLToPath(import.meta.url))
const REPO = join(HERE, "..", "..", "..")
const { chromium } = await import(pathToFileURL(join(REPO, "e2e", "node_modules", "playwright-core", "index.mjs")).href)

const ORIGIN = "http://localhost:3000"
const API = "http://localhost:5000/api"
const ACC = {
  email: "salma.benyoussef@cabinet-ibnkhaldoun.tn",
  password: "QaAudit2026!y",
  secret: "4YRLT22RBPRP3RRKUKLFQERU4H62BRBO",
}
const SHOTS = join(HERE, "shots")
mkdirSync(SHOTS, { recursive: true })

// ── outcomes ────────────────────────────────────────────────────────────────────────────────────────────────
const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = "") => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }
const check = (id, cond, m, d = "") => (cond ? ok(id, m) : bad(id, m, d))

// ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────────
function totp(secret) {
  const A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
  let bits = ""
  for (const c of secret) bits += A.indexOf(c).toString(2).padStart(5, "0")
  const key = Buffer.from((bits.match(/.{8}/g) || []).map((b) => parseInt(b, 2)))
  const counter = Math.floor(Date.now() / 1000 / 30)
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0)
  buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac("sha1", key).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, "0")
}
// A code is single-use: every sign-in waits for a fresh 30-second window.
const nextWindow = () => new Promise((r) => setTimeout(r, (30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000))
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

const sql = (q) =>
  execFileSync("docker", ["exec", "clinic-postgres", "psql", "-U", "clinic_user", "-d", "clinic_management", "-At", "-F", "|", "-c", q], {
    encoding: "utf8",
  }).trim()

async function apiToken() {
  await nextWindow()
  const r = await fetch(`${API}/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: ACC.email, password: ACC.password, totpCode: totp(ACC.secret) }),
  })
  const body = await r.json()
  const t = body?.value?.accessToken
  if (!t) throw new Error(`api login failed: ${r.status} ${JSON.stringify(body).slice(0, 200)}`)
  return t
}

async function bffCookies() {
  await nextWindow()
  const r = await fetch(`${ORIGIN}/bff/auth/local-login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: ACC.email, password: ACC.password, totpCode: totp(ACC.secret) }),
  })
  if (!r.ok) throw new Error(`bff login failed: ${r.status} ${(await r.text()).slice(0, 200)}`)
  return (r.headers.getSetCookie?.() ?? [])
    .map((raw) => {
      const [pair, ...attrs] = raw.split(";").map((s) => s.trim())
      const eq = pair.indexOf("=")
      const flags = new Map(attrs.map((a) => { const i = a.indexOf("="); return i < 0 ? [a.toLowerCase(), ""] : [a.slice(0, i).toLowerCase(), a.slice(i + 1)] }))
      return {
        name: pair.slice(0, eq), value: pair.slice(eq + 1), domain: "localhost", path: flags.get("path") || "/",
        expires: flags.has("expires") ? Math.floor(new Date(flags.get("expires")).getTime() / 1000) : -1,
        httpOnly: flags.has("httponly"), secure: flags.has("secure"), sameSite: "Lax",
      }
    })
    .filter((c) => c.name && c.value)
}

let TOKEN
async function apiCall(method, path, body) {
  const r = await fetch(`${API}${path}`, {
    method, headers: { "Content-Type": "application/json", Authorization: `Bearer ${TOKEN}` },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const raw = await r.text()
  let json = null
  try { json = JSON.parse(raw) } catch {}
  if (!r.ok) throw new Error(`${method} ${path} → ${r.status} ${raw.slice(0, 300)}`)
  return json?.value ?? json
}

// ── the fiche modal ─────────────────────────────────────────────────────────────────────────────────────────
const dialog = (page) => page.locator('[role="dialog"]:visible').filter({ has: page.getByRole("button", { name: /^Enregistrer la séance/ }) })
const priceInput = (page) => dialog(page).locator('input[aria-label="Prix pour tout (DT)"]')
const NOTICE = /Modifié entre-temps par quelqu'un d'autre/

async function openFiche(page, patientId, recordId) {
  await page.goto(`${ORIGIN}/patients/${patientId}?editRecord=${recordId}`)
  await dialog(page).waitFor({ state: "visible", timeout: 60000 })
  // The open-time read lands after hydration; give it time so « opened » is what the page row + server agreed on.
  await sleep(2500)
}
async function setPrice(page, v) {
  const input = priceInput(page)
  await input.waitFor({ state: "visible", timeout: 15000 })
  await input.fill(String(v))
  await input.press("Tab")
}
async function addNote(page, text) {
  await dialog(page).getByRole("button", { name: /Ajouter une note/ }).first().click()
  await dialog(page).locator('textarea[placeholder="Saisir une note…"]').last().fill(text)
}
async function addExamen(page, text) {
  const section = dialog(page).getByRole("button", { name: /^Prescription/ }).first()
  if ((await section.getAttribute("aria-expanded")) !== "true") await section.click()
  await dialog(page).getByRole("button", { name: /^Examen$/ }).first().click()
  await dialog(page).locator('input[placeholder="Ex : Radiographie panoramique dentaire"], textarea[placeholder="Ex : Radiographie panoramique dentaire"]').last().fill(text)
}
// Press save and report what happened: "saved" (dialog closed) or "conflict" (Recharger offered).
async function save(page) {
  await dialog(page).getByRole("button", { name: /^Enregistrer la séance/ }).click()
  for (let i = 0; i < 60; i++) {
    await sleep(500)
    if (await dialog(page).getByRole("button", { name: "Recharger" }).isVisible().catch(() => false)) return "conflict"
    if (!(await dialog(page).isVisible().catch(() => false))) return "saved"
  }
  return "timeout"
}
async function reload(page) {
  await dialog(page).getByRole("button", { name: "Recharger" }).click()
  await sleep(2500)
}
const dialogText = async (page) => (await dialog(page).innerText().catch(() => "")) ?? ""
const price = async (page) => (await priceInput(page).inputValue().catch(() => "(no price field)"))
const shot = (page, name) => page.screenshot({ path: join(SHOTS, `${name}.png`) })
const recordRow = (id) => sql(`select "Cost","Notes" from "DentalRecords" where "Id"='${id}'`)

// ── run ─────────────────────────────────────────────────────────────────────────────────────────────────────
const browser = await chromium.launch({ channel: "chrome", headless: true })
let ctxA, ctxB, A, B, P, F
try {
  console.log("\n  signing in: api token, then two browser sessions (one TOTP window each)…")
  TOKEN = await apiToken()
  const cookiesA = await bffCookies()
  const cookiesB = await bffCookies()

  // Arrange: a patient and a one-act fiche of our own.
  const acts = (await apiCall("GET", "/procedure-types?includeInactive=false&pageSize=200")).items
  const act = acts.find((a) => (a.defaultSteps ?? []).length === 0 && Number(a.defaultCost) > 0 && !a.resultingCondition)
  if (!act) throw new Error("no catalogue act without steps, with a tarif and no resulting condition")
  const stamp = Date.now().toString(36)
  const patient = await apiCall("POST", "/patients", { firstName: "QA", lastName: `Merge-${stamp}`, phone: null })
  P = patient.id
  const fiche = await apiCall("POST", `/patients/${P}/dental-records`, {
    patientId: P,
    interventionDate: new Date().toISOString(),
    amountPaid: 0,
    paymentMethod: "Cash",
    isAdultTeeth: true,
    notes: ["note initiale"],
    importantNotes: [],
    acts: [{
      procedureTypeId: act.id, procedureName: act.name, cost: 40, unitCost: 40, isPerTooth: false,
      toothNumbers: [], ponticToothNumbers: [], implantPilierToothNumbers: [], isUnfinished: false,
      resultingCondition: null, surfaces: null, note: null,
    }],
    treatmentPlanId: null, treatmentPlanItemId: null, amountCollectedOnPlan: 0,
  })
  F = fiche.id
  console.log(`  arranged: patient ${P} « QA Merge-${stamp} », fiche ${F} (${act.name}, 40 DT)\n`)

  const quiet = async (ctx) => {
    await ctx.route("**/notifications/pending-reviews**", (r) => r.fulfill({ status: 200, contentType: "application/json", body: "[]" }))
  }
  ctxA = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "fr-FR" })
  ctxB = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "fr-FR" })
  await ctxA.addCookies(cookiesA)
  await ctxB.addCookies(cookiesB)
  await quiet(ctxA)
  await quiet(ctxB)
  // A is a stale tab: no realtime, so its page list keeps the row it loaded (B2 needs that).
  await ctxA.route("**/hub/clinic**", (r) => r.abort())
  A = await ctxA.newPage()
  B = await ctxB.newPage()
  for (const p of [A, B]) p.on("dialog", (d) => d.accept().catch(() => {}))

  const scenarios = [
    ["A1", async () => {
      await openFiche(A, P, F)
      check("A1", !NOTICE.test(await dialogText(A)), "no notice right after opening")
      await openFiche(B, P, F)
      await setPrice(B, 77)
      check("A1", (await save(B)) === "saved", "colleague B saves price 77")
      await addNote(A, "note de A")
      const r = await save(A)
      check("A1", r === "conflict", "A's stale save is refused with « Recharger »", r)
      await shot(A, "A1-conflict")
      await reload(A)
      const t = await dialogText(A)
      check("A1", !(await dialog(A).getByRole("button", { name: "Recharger" }).isVisible()), "banner gone after Recharger")
      check("A1", !NOTICE.test(t), "no « Modifié entre-temps » line (only B changed the acts)", t.match(NOTICE)?.[0] ?? "")
      const pr = await price(A)
      check("A1", /^77[,.]000$/.test(pr), "price shows B's 77,000", pr)
      const notes = await dialog(A).locator('textarea[placeholder="Saisir une note…"]').evaluateAll((els) => els.map((e) => e.value))
      check("A1", notes.includes("note de A"), "A's typed note is still there", JSON.stringify(notes))
      await shot(A, "A1-after-recharger")
      check("A1", (await save(A)) === "saved", "A saves after Recharger")
      const row = recordRow(F)
      check("A1s", row.startsWith("77") && row.includes("note de A") && row.includes("note initiale"), "row: cost 77, both notes", row)
    }],
    ["A2", async () => {
      await A.setViewportSize({ width: 1536, height: 730 })
      await openFiche(A, P, F)
      await openFiche(B, P, F)
      await setPrice(B, 88)
      check("A2", (await save(B)) === "saved", "colleague B saves price 88")
      await setPrice(A, 55)
      const r = await save(A)
      check("A2", r === "conflict", "A's save (55) is refused", r)
      await reload(A)
      const t = await dialogText(A)
      check("A2", NOTICE.test(t) && /«\s*Actes\s*»/u.test(t), "notice names « Actes »", t.split("\n").find((l) => NOTICE.test(l)) ?? "(none)")
      const pr = await price(A)
      check("A2", /^88[,.]000$/.test(pr), "price shows B's 88,000", pr)
      await shot(A, "A2-notice-1536x730")
      // C2 — the notice at the floor width.
      await A.setViewportSize({ width: 320, height: 700 })
      await sleep(800)
      const notice = dialog(A).getByText(NOTICE)
      await notice.scrollIntoViewIfNeeded().catch(() => {})
      await shot(A, "C2-notice-320")
      const box = await notice.boundingBox().catch(() => null)
      const dbox = await dialog(A).boundingBox().catch(() => null)
      const bodyOverflow = await dialog(A).evaluate((d) => {
        const body = d.querySelector('[data-slot="dialog-body"]') ?? d
        return body.scrollWidth - body.clientWidth
      }).catch(() => -1)
      check("C2", box && dbox && box.x >= dbox.x - 1 && box.x + box.width <= dbox.x + dbox.width + 1,
        "notice line sits inside the dialog at 320", JSON.stringify({ box, dbox }))
      check("C2", bodyOverflow <= 0, "no horizontal scroll in the dialog body at 320", `overflow ${bodyOverflow}px`)
      await A.setViewportSize({ width: 1536, height: 730 })
      await sleep(500)
      await setPrice(A, 66)
      check("A2", (await save(A)) === "saved", "A re-applies 66 and saves")
      const row = recordRow(F)
      check("A2s", row.startsWith("66"), "row: cost 66", row)
    }],
    ["C1", async () => {
      await A.setViewportSize({ width: 1440, height: 900 })
      const actsBefore = sql(`select count(*) from "DentalRecordActs" where "DentalRecordId"='${F}'`)
      await openFiche(A, P, F)
      check("C1", (await save(A)) === "saved", "unchanged re-save succeeds")
      const row = recordRow(F)
      const actsAfter = sql(`select count(*) from "DentalRecordActs" where "DentalRecordId"='${F}'`)
      check("C1", row.startsWith("66") && actsBefore === actsAfter, "cost and act count unchanged", `${row} acts ${actsBefore}→${actsAfter}`)
    }],
    ["A3", async () => {
      await openFiche(A, P, F)
      await openFiche(B, P, F)
      await addExamen(B, "Panoramique B")
      check("A3", (await save(B)) === "saved", "colleague B saves an examen « Panoramique B »")
      await addExamen(A, "Bilan A")
      const r = await save(A)
      check("A3", r === "conflict", "A's save (examen « Bilan A ») is refused", r)
      await reload(A)
      await sleep(1500) // the document read is a second request
      const t = await dialogText(A)
      check("A3", NOTICE.test(t) && /«\s*Ordonnance\s*»/u.test(t), "notice names « Ordonnance »", t.split("\n").find((l) => NOTICE.test(l)) ?? "(none)")
      const inputs = await dialog(A).locator("input, textarea").evaluateAll((els) => els.map((e) => e.value))
      const shows = t.includes("Panoramique B") || inputs.includes("Panoramique B")
      const keepsA = t.includes("Bilan A") || inputs.includes("Bilan A")
      check("A3", shows && !keepsA, "section shows B's « Panoramique B », not « Bilan A »", JSON.stringify({ shows, keepsA }))
      await shot(A, "A3-ordonnance")
    }],
    ["B1", async () => {
      await openFiche(A, P, F)
      await sleep(1000)
      check("B1", !NOTICE.test(await dialogText(A)), "no notice on an ordinary open")
      await addNote(A, "note B1")
      const r = await save(A)
      check("B1", r === "saved", "ordinary edit saves without a 409", r)
    }],
    ["B2", async () => {
      await A.goto(`${ORIGIN}/patients/${P}?tab=medical-records`)
      const edit = A.locator('button[aria-label^="Modifier la fiche du"]:visible').first()
      await edit.waitFor({ state: "visible", timeout: 60000 })
      await sleep(1500)
      await openFiche(B, P, F)
      await setPrice(B, 99)
      check("B2", (await save(B)) === "saved", "colleague B saves price 99 while A's list is stale")
      await edit.click()
      await dialog(A).waitFor({ state: "visible", timeout: 30000 })
      await sleep(3000)
      const pr = await price(A)
      check("B2", /^99[,.]000$/.test(pr), "the open-time read brings B's 99,000 in", pr)
      check("B2", !NOTICE.test(await dialogText(A)), "no notice (nothing typed yet)")
      await addNote(A, "note B2")
      const r = await save(A)
      check("B2", r === "saved", "A's save goes through with no 409", r)
      const row = recordRow(F)
      check("B2", row.startsWith("99") && row.includes("note B2"), "row: cost 99 and A's note", row)
    }],
    ["D1", async () => {
      const before = sql(`select count(*) from "DentalRecords" where "PatientId"='${P}'`)
      await A.goto(`${ORIGIN}/patients/${P}?addRecord=1`)
      await dialog(A).waitFor({ state: "visible", timeout: 60000 })
      await sleep(1500)
      const search = dialog(A).locator('input[aria-label="Rechercher un acte au catalogue"]')
      await search.waitFor({ state: "visible", timeout: 15000 })
      await search.fill(act.name)
      await sleep(500)
      await search.press("Enter")
      await sleep(800)
      const r = await save(A)
      check("D1", r === "saved", "a new fiche saves", r)
      const after = sql(`select count(*) from "DentalRecords" where "PatientId"='${P}'`)
      check("D1", Number(after) === Number(before) + 1, "one more fiche on the test patient", `${before}→${after}`)
    }],
    ["D2", async () => {
      // A carried fiche with NO note of its own: a note that represents its devis shows no tag, by design.
      await openFiche(A, "8c0e2af9-227d-4acf-a8f8-472374c308c1", "0ec0a407-d8d9-468b-9915-6db9cd781a7a")
      const t = await dialogText(A)
      check("D2", /Inclus dans le traitement|Sur la note n°/.test(t), "devis-carried act shows its tag after the new hydration",
        t.match(/Inclus dans le traitement|Sur la note n° \S+/)?.[0] ?? t.slice(0, 160).replace(/\n/g, " · "))
      check("D2", !NOTICE.test(t), "no notice on a read-only open")
      await shot(A, "D2-carried")
    }],
    ["D3", async () => {
      await openFiche(A, "4cf1c3ea-f851-4dd8-9d81-61bec64bdac4", "b7033384-02d6-462b-b78d-95288a6d4e11")
      await sleep(1000)
      const t = await dialogText(A)
      const section = await dialog(A).getByRole("button", { name: /^Prescription/ }).first().innerText().catch(() => "")
      check("D3", section.replace(/^Prescription/, "").trim().length > 0, "« Prescription » summary names what is prescribed", section.replace(/\n/g, " · "))
      check("D3", !NOTICE.test(t), "no notice on a read-only open")
      await shot(A, "D3-ordonnance")
    }],
  ]

  for (const [id, run] of scenarios) {
    try { await run() } catch (e) {
      bad(id, "threw", e.message.split("\n")[0])
      await shot(A, `${id}-threw-A`).catch(() => {})
      await shot(B, `${id}-threw-B`).catch(() => {})
    }
  }
} catch (e) {
  bad("setup", "threw", e.message)
} finally {
  await browser.close()
  console.log(`\n  test patient: ${P ?? "(none)"} · fiche ${F ?? "(none)"}`)
  console.log(findings.length ? `\n  ${findings.length} finding(s):` : "\n  ALL CLEAR")
  for (const f of findings) console.log("   ", JSON.stringify(f))
}
