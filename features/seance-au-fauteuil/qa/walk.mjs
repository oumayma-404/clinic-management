// QA walk — séance au fauteuil. One launch, every scenario of plan.md; a failure is recorded, never thrown.
//
//   PW_CORE=<…/playwright-core/index.js> RUN=1 node walk.mjs
//
// Env: WEB (default http://localhost:3098), API (http://localhost:5098/api), PG_DB (clinic_seance_qa).
// Fixtures are fresh patients « Fauteuil <tag><stamp> »; SQL is used to READ only (and B6's revert of its own mask).
import { pathToFileURL, fileURLToPath } from "node:url"
import { mkdirSync, writeFileSync } from "node:fs"
import { join, dirname } from "node:path"
import { execFileSync } from "node:child_process"
import { createHmac } from "node:crypto"

const HERE = dirname(fileURLToPath(import.meta.url))
const WEB = process.env.WEB ?? "http://localhost:3098"
const API = process.env.API ?? "http://localhost:5098/api"
const PG_DB = process.env.PG_DB ?? "clinic_seance_qa"
const RUN = process.env.RUN ?? "1"
const SHOTS = join(HERE, "shots")
mkdirSync(SHOTS, { recursive: true })
const pw = await import(pathToFileURL(process.env.PW_CORE).href)
const chromium = pw.chromium ?? pw.default?.chromium

const PASSWORD = "QaAudit2026!y"
const DOCTOR = "qa.doctor@ibnkhaldoun.test"
const DESK = "qa.secretary@ibnkhaldoun.test"
const ADMIN = "salma.benyoussef@cabinet-ibnkhaldoun.tn"
const TOTP_SECRET = "4YRLT22RBPRP3RRKUKLFQERU4H62BRBO"
const STAMP = new Date().toISOString().slice(11, 16).replace(":", "")

const results = []
const findings = []
const ok = (id, m) => { console.log(`  ✅ [${id}] ${m}`); results.push({ id, outcome: "✅", m }) }
const bad = (id, m, d = "") => {
  console.log(`  ❌ [${id}] ${m} ${d}`)
  findings.push({ id, m, d })
  results.push({ id, outcome: "❌", m: `${m} ${d}`.trim() })
}
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); results.push({ id, outcome: "⏭", m: why }) }
const money = (v) => Math.round(Number(v) * 1000) / 1000

// ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────
function sql(q) {
  return execFileSync("docker", ["exec", "clinic-postgres", "psql", "-U", "clinic_user", "-d", PG_DB, "-At", "-F", "|", "-c", q], {
    encoding: "utf8",
  }).trim()
}

function totp(secret, at = Date.now()) {
  const A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
  let bits = ""
  for (const c of secret.replace(/[\s=]/g, "").toUpperCase()) bits += A.indexOf(c).toString(2).padStart(5, "0")
  const bytes = []
  for (let i = 0; i + 8 <= bits.length; i += 8) bytes.push(parseInt(bits.slice(i, i + 8), 2))
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(Math.floor(at / 1000 / 30)))
  const h = createHmac("sha1", Buffer.from(bytes)).update(counter).digest()
  const o = h[h.length - 1] & 0xf
  const n = ((h[o] & 0x7f) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3]
  return String(n % 1_000_000).padStart(6, "0")
}

async function apiLogin(email, withTotp = false) {
  const body = { email, password: PASSWORD, ...(withTotp ? { totpCode: totp(TOTP_SECRET) } : {}) }
  const r = await fetch(`${API}/auth/login`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) })
  const j = await r.json().catch(() => null)
  const token = j?.value?.accessToken
  if (!token) throw new Error(`login ${email}: HTTP ${r.status} ${JSON.stringify(j).slice(0, 200)}`)
  return token
}

function client(token) {
  const call = async (method, path, body) => {
    const r = await fetch(`${API}${path}`, {
      method,
      headers: { Authorization: `Bearer ${token}`, ...(body !== undefined ? { "Content-Type": "application/json" } : {}) },
      ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
    })
    const raw = await r.text()
    let json = null
    try { json = raw ? JSON.parse(raw) : null } catch { /* html */ }
    return { status: r.status, body: json?.value ?? json, raw }
  }
  const okCall = async (method, path, body) => {
    const r = await call(method, path, body)
    if (r.status < 200 || r.status >= 300) throw new Error(`${method} ${path} → ${r.status} ${r.raw.slice(0, 300)}`)
    return r.body
  }
  return { call, ok: okCall, get: (p) => okCall("GET", p) }
}

const iso = (d) => d.toISOString()
const minutesFromNow = (m) => { const d = new Date(Date.now() + m * 60_000); d.setSeconds(0, 0); return d }
const localDay = (offset = 0) => {
  const d = new Date(); d.setDate(d.getDate() + offset)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}T00:00:00`
}
const yesterdayAt = (h) => { const d = new Date(); d.setDate(d.getDate() - 1); d.setHours(h, 0, 0, 0); return d }

async function shot(page, name) {
  try { await page.bringToFront(); await page.screenshot({ path: join(SHOTS, `${name}.png`), timeout: 15000 }) } catch { /* evidence only */ }
}

async function signIn(context, email, landing) {
  const page = await context.newPage()
  await page.goto(`${WEB}/login`, { waitUntil: "domcontentloaded" })
  await page.locator("input[type=email]").fill(email)
  await page.locator("input[type=password]").fill(PASSWORD)
  await page.getByRole("button", { name: /Se connecter/ }).click()
  await page.waitForURL((u) => !u.pathname.startsWith("/login"), { timeout: 45000 })
  if (landing) await page.goto(`${WEB}${landing}`, { waitUntil: "domcontentloaded" })
  return page
}

async function newContext(browser, viewport = { width: 1440, height: 900 }) {
  const ctx = await browser.newContext({ viewport, locale: "fr-FR", timezoneId: "Africa/Tunis" })
  // The post-visit prompt is an inset-0 overlay over every page; this walk is not about it.
  await ctx.route("**/notifications/pending-reviews**", (r) =>
    r.fulfill({ status: 200, contentType: "application/json", body: "[]" }))
  // Probe fix (run 1): a real `print()` on the PDF frame CRASHED the headless renderer. `printPdfBlob`'s own frame
  // (aria-hidden, titled « ….pdf ») gets a stand-in window whose `print()` is counted instead.
  await ctx.addInitScript(() => {
    const desc = Object.getOwnPropertyDescriptor(HTMLIFrameElement.prototype, "contentWindow")
    Object.defineProperty(HTMLIFrameElement.prototype, "contentWindow", {
      configurable: true,
      get() {
        if (this.getAttribute("aria-hidden") === "true" && /\.pdf$/i.test(this.title || "")) {
          const src = this.src
          return {
            location: { href: src },
            focus() {},
            print() { window.__qaPrints = (window.__qaPrints || 0) + 1 },
          }
        }
        return desc.get.call(this)
      },
    })
  })
  return ctx
}
const printsOn = (page) => page.evaluate(() => window.__qaPrints || 0).catch(() => -1)

const ficheDialog = (page) => page.locator('[role="dialog"]').filter({ hasText: /Actes de la séance/ }).last()

async function waitForToastText(page, re, ms = 15000) {
  const end = Date.now() + ms
  while (Date.now() < end) {
    const t = await page.locator("[data-sonner-toast]").allInnerTexts().catch(() => [])
    const hit = t.find((x) => re.test(x))
    if (hit) return hit
    await page.waitForTimeout(250)
  }
  return null
}

async function overflowOf(page) {
  return page.evaluate(() => {
    const main = document.querySelector("main")
    const dlg = [...document.querySelectorAll('[role="dialog"]')].pop()
    return {
      doc: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      main: main ? main.scrollWidth - main.clientWidth : 0,
      dialog: dlg ? dlg.scrollWidth - dlg.clientWidth : 0,
    }
  })
}

async function inViewport(page, locator) {
  const box = await locator.boundingBox().catch(() => null)
  if (!box) return false
  const vp = page.viewportSize()
  return box.x >= -1 && box.y >= -1 && box.x + box.width <= vp.width + 1 && box.y + box.height <= vp.height + 1
}

// ── arrange (through the product's API) ─────────────────────────────────────────────────────────────────
console.log(`QA walk run ${RUN} — ${WEB} / ${API} / ${PG_DB}`)
const doc = client(await apiLogin(DOCTOR))
const desk = client(await apiLogin(DESK))

const catalogue = (await doc.get("/procedure-types?includeInactive=false&pageSize=200")).items
const act =
  catalogue.find((a) => (a.defaultSteps ?? []).length === 0 && Number(a.defaultCost) > 0 && /Consultation/i.test(a.name)) ??
  catalogue.find((a) => (a.defaultSteps ?? []).length === 0 && Number(a.defaultCost) > 0 && !a.resultingCondition)
const tarif = money(act.defaultCost)
console.log(`  act: ${act.name} · ${tarif} DT`)

const mkPatient = async (tag) =>
  doc.ok("POST", "/patients", { firstName: "Fauteuil", lastName: `${tag}${STAMP}`, phone: null })
const mkVisit = async (patientId, start, minutes, extra = {}) =>
  doc.ok("POST", "/appointments", {
    patientId,
    appointmentDateTime: iso(start),
    durationMinutes: minutes,
    procedures: [{ procedureTypeId: act.id, agreedCost: null }],
    allowOutsideWorkingHours: true,
    allowOverlap: true,
    ...extra,
  })
const ficheBody = (visit, cost, extra = {}) => ({
  appointmentId: visit.id,
  interventionDate: localDay(new Date(visit.appointmentDateTime) < new Date(localDay(0)) ? -1 : 0),
  amountPaid: 0,
  isAdultTeeth: true,
  acts: [{ procedureTypeId: act.id, procedureName: act.name, cost, unitCost: cost, isPerTooth: false, toothNumbers: [] }],
  ...extra,
})

const P1 = await mkPatient("Un")
const V1 = await mkVisit(P1.id, minutesFromNow(-10), 60)
const P2 = await mkPatient("Deux")
const V2 = await mkVisit(P2.id, minutesFromNow(-5), 60)
const P3 = await mkPatient("Trois")
const V3 = await mkVisit(P3.id, minutesFromNow(-15), 60)
const P5 = await mkPatient("Cinq")
const V5 = await mkVisit(P5.id, yesterdayAt(10), 30)
const P6 = await mkPatient("Six")
const V6 = await mkVisit(P6.id, yesterdayAt(11), 30)
// « Venu », so « À clôturer » asks for the fiche (the cascade asks presence first).
await doc.call("PUT", `/appointments/${V6.id}`, {
  id: V6.id, appointmentDateTime: V6.appointmentDateTime, durationMinutes: 30, version: V6.version,
  allowOverlap: true, allowOutsideWorkingHours: true, status: "Completed",
}).then((r) => { if (r.status >= 300) console.log(`  arrange V6 « venu » failed: ${r.status} ${r.raw.slice(0, 160)}`) })
const P7 = await mkPatient("Sept")
const V7 = await mkVisit(P7.id, minutesFromNow(-28), 30)
const name1 = `Fauteuil Un${STAMP}`
console.log(`  patients: ${[P1, P2, P3, P5, P6, P7].map((p) => p.lastName).join(" · ")}`)

// V3: a draft note raised from the RDV window (names the visit, no fiche), then a fiche on V3.
let draftNoteId = null
let fiche3 = null
try {
  const draft = await doc.ok("POST", "/invoices", {
    patientId: P3.id, appointmentId: V3.id,
    // Typed for LESS than the work recorded: the fiche makes the note, so its amount is what is billed.
    lines: [{ designation: act.name, quantity: 1, unitPriceHt: tarif - 5 }],
  })
  draftNoteId = draft.id
  const r = await doc.call("POST", `/patients/${P3.id}/dental-records`, { patientId: P3.id, ...ficheBody(V3, tarif) })
  fiche3 = r.body
  if (r.status >= 300) throw new Error(`fiche V3 → ${r.status} ${r.raw.slice(0, 200)}`)
} catch (e) { console.log(`  arrange V3 failed: ${e.message}`) }

// V5 (yesterday, ended): a fiche, and only a DRAFT note on the visit.
let draft5Id = null
try {
  await doc.ok("POST", `/patients/${P5.id}/dental-records`, { patientId: P5.id, ...ficheBody(V5, tarif) })
  draft5Id = (await doc.ok("POST", "/invoices", {
    patientId: P5.id, appointmentId: V5.id, lines: [{ designation: act.name, quantity: 1, unitPriceHt: tarif }],
  })).id
} catch (e) { console.log(`  arrange V5 failed: ${e.message}`) }

// V7: ends in ~2 min; a fiche saved now, mid-séance. Its status is read RIGHT AFTER (probe fix, run 1: read at the
// end of the walk, the job had long since completed it and « before » said nothing).
let fiche7 = null
let v7AfterFiche = null
try {
  fiche7 = await doc.ok("POST", `/patients/${P7.id}/dental-records`, { patientId: P7.id, ...ficheBody(V7, tarif) })
  v7AfterFiche = (await doc.get(`/appointments/${V7.id}`)).status
} catch (e) { console.log(`  arrange V7 failed: ${e.message}`) }

// V4: an accepted devis with a LATE agreed échéance, a séance today on its act, and that séance's fiche.
let P4 = null, plan4 = null, fiche4 = null
try {
  P4 = await mkPatient("Quatre")
  plan4 = await doc.ok("POST", "/treatment-plans", {
    patientId: P4.id,
    title: "Devis QA fauteuil",
    items: [{ procedureTypeId: act.id, designationFr: act.name, plannedCost: 300, toothNumbers: [16] }],
    installments: [{ dueDate: localDay(-3), amount: 100 }, { dueDate: localDay(30), amount: 200 }],
  })
  const item = plan4.items[0]
  const V4 = await mkVisit(P4.id, minutesFromNow(-20), 60, {
    treatmentPlanId: plan4.id,
    procedures: [{ procedureTypeId: act.id, agreedCost: null, treatmentPlanId: plan4.id, treatmentPlanItemId: item.id }],
  })
  plan4.V4 = V4
  fiche4 = await doc.ok("POST", `/patients/${P4.id}/dental-records`, {
    patientId: P4.id,
    ...ficheBody(V4, 0),
    treatmentPlanId: plan4.id,
    treatmentPlanItemId: item.id,
  })
} catch (e) { console.log(`  arrange V4 failed: ${e.message}`) }

// ── the browser ──────────────────────────────────────────────────────────────────────────────────────────
const browser = await chromium.launch({ channel: "chrome", headless: true })
const doctorCtx = await newContext(browser)
const deskCtx = await newContext(browser)
let dp, sp
try { dp = await signIn(doctorCtx, DOCTOR) } catch (e) { bad("ENV", "doctor sign-in failed", e.message) }
try { sp = await signIn(deskCtx, DESK) } catch (e) { bad("ENV", "desk sign-in failed", e.message) }

const pdfGets = []
doctorCtx.on("request", (r) => { if (/\/medical-documents\/[^/]+\/pdf/.test(r.url())) pdfGets.push(r.url()) })
const errorToasts = async (page) =>
  (await page.locator("[data-sonner-toast][data-type=error]").allInnerTexts().catch(() => [])).join(" | ")

const S = [] // scenarios, in plan order
const ONLY = process.env.ONLY ? new Set(process.env.ONLY.split(",")) : null
const step = (id, run) => { if (!ONLY || ONLY.has(id)) S.push({ id, run }) }

let fiche1 = null
step("A1", async () => {
  await dp.goto(`${WEB}/appointments`, { waitUntil: "domcontentloaded" })
  const trigger = dp.getByRole("button", { name: new RegExp(`Actions du rendez-vous de .*Un${STAMP}`) }).first()
  await trigger.waitFor({ state: "attached", timeout: 30000 })
  await trigger.scrollIntoViewIfNeeded().catch(() => {})
  // Probe fix: overlapping blocks put a neighbour on top of this « ⋯ », so a positional click opens the wrong
  // menu. Focus + Enter opens THIS trigger's menu, wherever it is drawn.
  await trigger.focus()
  await dp.keyboard.press("Enter")
  // Probe fix: scoped to the open menu — the page holds other « Fiche de soins » doors.
  const link = dp.locator("[data-radix-popper-content-wrapper]").filter({ visible: true })
    .getByRole("link", { name: /Fiche de soins/ }).first()
  await link.waitFor({ timeout: 10000 })
  const href = await link.getAttribute("href")
  await shot(dp, `A1-quick-actions-${RUN}`)
  if (href === `/patients/${P1.id}?addRecord=1&appointmentId=${V1.id}`) ok("A1", `« Fiche de soins » → ${href}`)
  else bad("A1", "the « ⋯ » link is not the visit's fiche door", `href=${href}`)
  await link.click()
})

step("A2", async () => {
  if (!dp.url().includes("addRecord=1")) await dp.goto(`${WEB}/patients/${P1.id}?addRecord=1&appointmentId=${V1.id}`)
  const d = ficheDialog(dp)
  await d.waitFor({ timeout: 30000 })
  await d.getByText(act.name).first().waitFor({ timeout: 15000 })
  const save = d.getByRole("button", { name: /^Enregistrer$/ })
  const finish = d.getByRole("button", { name: /^Terminer la séance/ })
  const paid = await d.locator("#paid").inputValue().catch(() => "<absent>")
  await shot(dp, `A2-fiche-open-${RUN}`)
  const finishText = (await finish.innerText().catch(() => "")).replace(/\s+/g, " ")
  if ((await save.count()) === 1 && (await finish.count()) === 1 && paid === "")
    ok("A2", `footer « Enregistrer » + « ${finishText} »; « Payé » empty`)
  else bad("A2", "fiche footer / « Payé » not as planned", `save=${await save.count()} finish=${await finish.count()} paid=«${paid}»`)
})

step("A3", async () => {
  const d = ficheDialog(dp)
  await d.getByRole("button", { name: /^Enregistrer$/ }).click()
  const status = d.getByRole("status").filter({ hasText: /Enregistré à \d{2}:\d{2}/ })
  await status.waitFor({ timeout: 20000 })
  const records = await doc.get(`/patients/${P1.id}/dental-records`)
  const visit = await doc.get(`/appointments/${V1.id}`)
  fiche1 = records.find((r) => r.appointmentId === V1.id) ?? null
  const stillOpen = await d.isVisible()
  if (stillOpen && records.length === 1 && fiche1 && visit.status !== "Completed")
    ok("A3", `stays open (« ${await status.innerText()} »); 1 fiche; visit ${visit.status}`)
  else bad("A3", "in-place save", `open=${stillOpen} fiches=${records.length} visit=${visit.status}`)
})

step("A4", async () => {
  const d = ficheDialog(dp)
  const section = d.locator('[data-guide="record-prescription"]')
  const toggle = section.locator("button[aria-expanded]").first()
  if ((await toggle.getAttribute("aria-expanded")) !== "true") await toggle.click()
  await section.getByRole("button", { name: /Médicament/ }).first().click()
  await section.getByLabel("Nom du médicament").last().fill("Amoxicilline 1 g")
  const before = pdfGets.length
  const prints0 = await printsOn(dp)
  await section.getByRole("button", { name: /Imprimer l.ordonnance/ }).click()
  const end = Date.now() + 25000
  while (Date.now() < end && ((await printsOn(dp)) <= prints0 || pdfGets.length === before)) await dp.waitForTimeout(250)
  const prints = (await printsOn(dp)) - prints0
  const errors = await errorToasts(dp)
  const records = await doc.get(`/patients/${P1.id}/dental-records`)
  const r1 = records.find((r) => r.appointmentId === V1.id)
  await shot(dp, `A4-print-${RUN}`)
  if (pdfGets.length > before && prints === 1 && records.length === 1 && r1?.prescriptionDocumentId && (await d.isVisible()) && !errors)
    ok("A4", `saved, fetched the PDF, print() called once; ordonnance ${r1.prescriptionDocumentId.slice(0, 8)}…; still 1 fiche`)
  else bad("A4", "print on the spot", `pdfGets=${pdfGets.length - before} prints=${prints} fiches=${records.length} doc=${r1?.prescriptionDocumentId} open=${await d.isVisible()} errors=${errors}`)
})

step("A5", async () => {
  const d = ficheDialog(dp)
  await d.getByRole("button", { name: /^Terminer la séance/ }).click()
  await d.waitFor({ state: "detached", timeout: 20000 }).catch(() => {})
  await dp.waitForTimeout(1000)
  const visit = await doc.get(`/appointments/${V1.id}`)
  const notes = sql(`select count(*) from "Invoices" where "PatientId"='${P1.id}' and "Status"<>4`)
  if (!(await ficheDialog(dp).isVisible().catch(() => false)) && visit.status === "Completed" && notes === "0")
    ok("A5", `closed; visit Completed; no note (Payé empty)`)
  else bad("A5", "« Terminer la séance »", `visit=${visit.status} liveNotes=${notes}`)
})

let collectedAmount = null
// The séance's live notes and whether the draft still exists — the outcome the money rows assert on.
const fmt3 = (n) => money(n).toFixed(3).replace(".", ",")
function liveNotesOf(patientId) {
  return sql(`select coalesce("Number", ''), "TotalTtc" from "Invoices" where "PatientId"='${patientId}' and "Status"<>4`)
    .split("\n").filter(Boolean).map((l) => { const [number, total] = l.split("|"); return { number, total: money(total) } })
}
const draftExists = (id) => id !== null && sql(`select count(*) from "Invoices" where "Id"='${id}'`) !== "0"

let pillBefore = null
// « À encaisser » is a tab of « À clôturer » since the owner kept the agenda as it was (2026-11-19): the count is on
// the tab, the list is inline under it.
const toCollectTab = (page) => page.getByRole("tab", { name: /À encaisser/ })
const toCollectList = (page) => page.getByRole("list", { name: /Séances à encaisser/ })
async function openToCollect(page) {
  await page.goto(`${WEB}/a-cloturer`, { waitUntil: "domcontentloaded" })
  await toCollectTab(page).waitFor({ timeout: 30000 })
  await toCollectTab(page).click()
  await toCollectList(page).or(page.getByText("Rien à encaisser aujourd'hui")).first().waitFor({ timeout: 20000 })
  return toCollectList(page)
}
const pillCount = async (page) => {
  const t = await toCollectTab(page).innerText({ timeout: 2000 }).catch(() => "")
  const m = t.match(/(\d+)\s*$/)
  return m ? Number(m[1]) : 0
}
step("A6", async () => {
  const list = await openToCollect(sp)
  pillBefore = await pillCount(sp)
  const row = list.locator("li").filter({ hasText: `Un${STAMP}` })
  const text = (await row.innerText().catch(() => "")).replace(/\s+/g, " ")
  await shot(sp, `A6-to-collect-${RUN}`)
  const cost = money((await doc.get(`/patients/${P1.id}/dental-records`))[0]?.cost)
  collectedAmount = cost
  if (/à facturer/.test(text) && text.includes(cost.toFixed(3).replace(".", ",")))
    ok("A6", `row « ${text} »`)
  else bad("A6", "the séance is not listed as planned", `row=«${text}» expected cost ${cost}`)
})

step("A7", async () => {
  const list = toCollectList(sp)
  await list.getByRole("button", { name: new RegExp(`Encaisser .*Un${STAMP}`) }).click()
  const bill = sp.getByRole("dialog").filter({ hasText: /Facturer/ }).last()
  await bill.waitFor({ timeout: 15000 })
  await bill.locator("#billMethod").click()
  await sp.getByRole("option", { name: /Chèque/ }).click()
  await bill.locator("#bill-record-cheque-number").waitFor({ timeout: 5000 })
  await sp.waitForTimeout(300)
  await bill.locator("#bill-record-cheque-number").fill("7781234")
  await bill.locator("#bill-record-cheque-bank").fill("BIAT")
  await shot(sp, `A7-bill-cheque-${RUN}`)
  await bill.getByRole("button", { name: /Facturer et encaisser/ }).click()
  await bill.waitFor({ state: "detached", timeout: 20000 }).catch(() => {})
  // The list refreshes on its own (realtime / settle) — no reload: the pill count drops by one.
  const end = Date.now() + 10000
  let after = await pillCount(sp)
  while (Date.now() < end && after >= pillBefore) { await sp.waitForTimeout(400); after = await pillCount(sp) }
  const rows = sql(`select i."Number", i."Status", p."Amount", p."ChequeNumber", p."ChequeBankName" from "Invoices" i join "Payments" p on p."InvoiceId"=i."Id" where i."PatientId"='${P1.id}' and not p."IsVoided"`)
  const listed = (await desk.call("GET", "/appointments/to-collect")).body?.some?.((r) => r.appointmentId === V1.id)
  if (/7781234\|BIAT/.test(rows) && !listed && after === pillBefore - 1) ok("A7", `note + cheque: ${rows}; pill ${pillBefore} → ${after} without a reload`)
  else bad("A7", "desk collection by cheque", `rows=${rows} stillListed=${listed} pill ${pillBefore}→${after} errors=${await errorToasts(sp)}`)
})

step("A8", async () => {
  const records = await doc.get(`/patients/${P1.id}/dental-records`)
  const r1 = records[0]
  await dp.goto(`${WEB}/patients/${P1.id}?editRecord=${r1.id}`, { waitUntil: "domcontentloaded" })
  const d = ficheDialog(dp)
  await d.waitFor({ timeout: 30000 })
  await dp.waitForTimeout(1500)
  const paid = await d.locator("#paid").inputValue()
  await shot(dp, `A8-reopen-${RUN}`)
  const expected = money(r1.collectedOnNote ?? -1).toFixed(3).replace(".", ",")
  const paidOk = paid.replace(/\s/g, "") === expected
  await d.getByRole("button", { name: /^Enregistrer la séance/ }).click()
  await d.waitFor({ state: "detached", timeout: 20000 }).catch(() => {})
  await dp.waitForTimeout(2000)
  const live = sql(`select count(*) from "Payments" p join "Invoices" i on i."Id"=p."InvoiceId" where i."PatientId"='${P1.id}' and not p."IsVoided"`)
  if (paidOk && live === "1") ok("A8", `« Payé » = ${paid} (desk money); re-save added nothing (1 live payment)`)
  else bad("A8", "reopen reads the note / re-save idempotent", `paid=«${paid}» expected=${expected} livePayments=${live}`)
})

step("B1", async () => {
  await dp.goto(`${WEB}/patients/${P2.id}?addRecord=1&appointmentId=${V2.id}`, { waitUntil: "domcontentloaded" })
  const d = ficheDialog(dp)
  await d.waitFor({ timeout: 30000 })
  await d.getByText(act.name).first().waitFor({ timeout: 15000 })
  await d.getByRole("button", { name: /Tout payé/ }).click()
  const paid = await d.locator("#paid").inputValue()
  await d.getByRole("button", { name: /^Terminer la séance/ }).click()
  await d.waitFor({ state: "detached", timeout: 20000 }).catch(() => {})
  await dp.waitForTimeout(2000)
  const rows = sql(`select i."Status", p."Amount" from "Invoices" i join "Payments" p on p."InvoiceId"=i."Id" where i."PatientId"='${P2.id}' and not p."IsVoided"`)
  if (paid.replace(/\s/g, "") === tarif.toFixed(3).replace(".", ",") && rows.endsWith(`|${tarif.toFixed(3)}`))
    ok("B1", `« Tout payé » = ${paid}; note paid at the chair (${rows})`)
  else bad("B1", "« Tout payé » then « Terminer »", `paid=«${paid}» rows=${rows}`)
})

step("B2", async () => {
  if (!fiche3) return skip("B2", "arrange V3 failed")
  const r = await doc.call("POST", `/invoices/from-dental-record/${fiche3.id}`, { paidNow: null })
  const count = sql(`select count(*) from "Invoices" where "PatientId"='${P3.id}' and "Status"<>4`)
  if (r.status >= 400 && r.raw.includes("seance_has_note") && count === "1") ok("B2", `refused ${r.status}: ${JSON.parse(r.raw).error}`)
  else bad("B2", "one note per séance", `status=${r.status} body=${r.raw.slice(0, 200)} liveNotes=${count}`)
})

step("B3", async () => {
  const list = await openToCollect(sp)
  const row = list.locator("li").filter({ hasText: `Trois${STAMP}` })
  await row.waitFor({ timeout: 10000 })
  const text = (await row.innerText()).replace(/\s+/g, " ")
  // The fiche makes the note (owner, 2026-11-19): one « Encaisser », the fiche's « Facturer cette intervention », which
  // names the draft it replaces before the press. Asserted on the outcome, never on a URL.
  await row.getByRole("button", { name: /^Encaisser / }).click()
  const bill = sp.getByRole("dialog").filter({ hasText: /Facturer cette intervention/ }).last()
  await bill.waitFor({ timeout: 15000 })
  const said = (await bill.innerText()).replace(/\s+/g, " ")
  await shot(sp, `B3-bill-replaces-draft-${RUN}`)
  await bill.getByRole("button", { name: /Facturer et encaisser/ }).click()
  await bill.waitFor({ state: "detached", timeout: 20000 }).catch(() => {})
  await sp.waitForTimeout(1500)
  const notes = liveNotesOf(P3.id)
  const listed = await toCollectList(sp).locator("li").filter({ hasText: `Trois${STAMP}` }).count()
  const ok3 = /à facturer/.test(text) && text.includes(`${fmt3(tarif)} DT`)
    && said.includes(`Remplace le brouillon de note de ${fmt3(tarif - 5)} DT`)
    && notes.length === 1 && /^\d{4}-\d+$/.test(notes[0].number) && notes[0].total === tarif
    && !draftExists(draftNoteId) && listed === 0
  if (ok3) ok("B3", `« ${text} » → the dialog named the ${fmt3(tarif - 5)} DT draft; note ${notes[0].number} = the fiche's ${fmt3(tarif)} DT, paid; draft gone; row gone`)
  else bad("B3", "draft row", `row=«${text}» said=«${said.slice(0, 200)}» notes=${JSON.stringify(notes)} draftLeft=${draftExists(draftNoteId)} stillListed=${listed}`)
})

step("B4", async () => {
  const r1 = (await doc.get(`/patients/${P1.id}/dental-records`))[0]
  const r = await desk.call("PUT", `/patients/${P1.id}/dental-records/${r1.id}`, {
    id: r1.id, patientId: P1.id, version: r1.version, interventionDate: r1.interventionDate,
    amountPaid: money(r1.collectedOnNote ?? r1.amountPaid), isAdultTeeth: true,
    acts: [{ procedureTypeId: act.id, procedureName: act.name, cost: tarif + 5, unitCost: tarif + 5, isPerTooth: false, toothNumbers: [] }],
    correctionReason: "QA",
  })
  const live = sql(`select count(*) from "Invoices" where "PatientId"='${P1.id}' and "Status"<>4`)
  if (r.status >= 400 && r.raw.includes("correction_requires_doctor") && live === "1") ok("B4", `refused ${r.status} for the desk`)
  else bad("B4", "a secretary corrected the note", `status=${r.status} body=${r.raw.slice(0, 200)} liveNotes=${live}`)
})

step("B5", async () => {
  const r1 = (await doc.get(`/patients/${P1.id}/dental-records`))[0]
  await dp.goto(`${WEB}/patients/${P1.id}?editRecord=${r1.id}`, { waitUntil: "domcontentloaded" })
  const d = ficheDialog(dp)
  await d.waitFor({ timeout: 30000 })
  await d.getByText(act.name).first().click()
  const price = d.getByLabel(/Prix pour tout \(DT\)|Prix par dent \(DT\)/).first()
  await price.waitFor({ timeout: 10000 })
  await price.fill(String(tarif + 10).replace(".", ","))
  await price.blur()
  await d.getByRole("button", { name: /^Enregistrer la séance|^Corriger la note/ }).click()
  const confirm = dp.getByRole("alertdialog").filter({ hasText: /Corriger la note/ })
  await confirm.waitFor({ timeout: 20000 })
  await shot(dp, `B5-correction-${RUN}`)
  await confirm.getByRole("button", { name: /^Corriger$/ }).click()
  await confirm.waitFor({ state: "detached", timeout: 25000 }).catch(() => {})
  await dp.waitForTimeout(2500)
  const notes = sql(`select i."Number", i."Status", coalesce(string_agg(p."ChequeNumber"||':'||p."Amount"||':'||to_char(p."PaidOn",'YYYY-MM-DD'), ',') filter (where not p."IsVoided"), '') from "Invoices" i left join "Payments" p on p."InvoiceId"=i."Id" where i."PatientId"='${P1.id}' group by i."Id", i."Number", i."Status" order by i."Number"`)
  const lines = notes.split("\n")
  const cancelled = lines.filter((l) => l.split("|")[1] === "4")
  const live = lines.filter((l) => l.split("|")[1] !== "4")
  if (cancelled.length === 1 && live.length === 1 && /7781234:/.test(live[0]))
    ok("B5", `old note cancelled; new note carries the cheque — ${live[0]}`)
  else bad("B5", "correction carry", `notes=${notes.replace(/\n/g, " ; ")} errors=${await errorToasts(dp)}`)
})

step("B7", async () => {
  if (!fiche4) return skip("B7", "arrange V4 (devis séance) failed")
  const list = await openToCollect(sp)
  const row = list.locator("li").filter({ hasText: `Quatre${STAMP}` })
  await row.waitFor({ timeout: 10000 })
  const text = (await row.innerText()).replace(/\s+/g, " ")
  await row.getByRole("button", { name: /Encaisser/ }).click()
  const pay = sp.getByRole("dialog").filter({ hasText: /^Encaisser/ }).last()
  await pay.waitFor({ timeout: 15000 })
  await shot(sp, `B7-installment-${RUN}`)
  await pay.getByRole("button", { name: /^Encaisser$/ }).click()
  await pay.waitFor({ state: "detached", timeout: 20000 }).catch(() => {})
  await sp.waitForTimeout(2000)
  const tagged = sql(`select ip."Amount", ip."DentalRecordId" from "InstallmentPayments" ip join "Installments" i on i."Id"=ip."InstallmentId" where i."TreatmentPlanId"='${plan4.id}'`)
  if (/échéance du devis n°/.test(text) && text.includes("100,000") && tagged.includes(fiche4.id))
    ok("B7", `« ${text} » → payment ${tagged}`)
  else bad("B7", "devis séance at the desk", `row=«${text}» payments=${tagged} errors=${await errorToasts(sp)}`)
})

step("C1", async () => {
  const phone = await doctorCtx.newPage()
  for (const width of [320, 390]) {
    await phone.setViewportSize({ width, height: 800 })
    await phone.goto(`${WEB}/patients/${P6.id}?addRecord=1&appointmentId=${V6.id}`, { waitUntil: "domcontentloaded" })
    const d = ficheDialog(phone)
    await d.waitFor({ timeout: 30000 })
    await d.getByText(act.name).first().waitFor({ timeout: 15000 })
    await phone.waitForTimeout(800)
    const o = await overflowOf(phone)
    const save = d.getByRole("button", { name: /^Enregistrer la séance|^Terminer la séance/ })
    const tout = d.getByRole("button", { name: /Tout payé/ })
    await tout.scrollIntoViewIfNeeded().catch(() => {})
    const toutIn = await inViewport(phone, tout)
    const saveIn = await inViewport(phone, save)
    await shot(phone, `C1-fiche-${width}-${RUN}`)
    if (o.doc <= 0 && o.main <= 0 && o.dialog <= 0 && saveIn && toutIn) ok("C1", `${width}px: no overflow; save + « Tout payé » in view`)
    else bad("C1", `${width}px layout`, JSON.stringify({ ...o, saveIn, toutIn }))
  }
  await phone.close()
})

step("C2", async () => {
  const lap = await doctorCtx.newPage()
  await lap.setViewportSize({ width: 1536, height: 730 })
  await lap.goto(`${WEB}/patients/${P6.id}?addRecord=1&appointmentId=${V6.id}`, { waitUntil: "domcontentloaded" })
  const d = ficheDialog(lap)
  await d.waitFor({ timeout: 30000 })
  await d.getByText(act.name).first().waitFor({ timeout: 15000 })
  await lap.waitForTimeout(800)
  const save = d.getByRole("button", { name: /^Enregistrer la séance|^Terminer la séance/ })
  await shot(lap, `C2-fiche-1536x730-${RUN}`)
  if (await inViewport(lap, save)) ok("C2", "1536×730: the save button is on screen without scrolling")
  else bad("C2", "1536×730: the save button is below the fold", JSON.stringify(await save.boundingBox()))
  await lap.close()
})

step("C3", async () => {
  const phone = await deskCtx.newPage()
  await phone.setViewportSize({ width: 390, height: 844 })
  const list = await openToCollect(phone)
  if ((await list.count()) === 0) {
    await phone.close()
    return skip("C3", "no séance left to collect at this point of the walk")
  }
  const first = list.getByRole("button", { name: /Encaisser|Émettre et encaisser/ }).first()
  await first.scrollIntoViewIfNeeded().catch(() => {})
  const o = await overflowOf(phone)
  await shot(phone, `C3-list-390-${RUN}`)
  if ((await inViewport(phone, first)) && o.doc <= 0 && o.main <= 0) ok("C3", "390px: « À encaisser » tab, no sideways scroll, action in view")
  else bad("C3", "390px list", JSON.stringify(o))
  await phone.close()
})

step("C4", async () => {
  const r = (await doc.get(`/patients/${P1.id}/dental-records`))[0]
  const a = r?.acts?.[0]
  if (r && (r.acts ?? []).length === 1 && a.procedureTypeId === act.id) ok("C4", `after 4 saves: 1 act « ${a.procedureName} » ${a.cost}`)
  else bad("C4", "acts changed across saves", JSON.stringify(r?.acts ?? []).slice(0, 200))
})

step("D1", async () => {
  await dp.goto(`${WEB}/a-cloturer`, { waitUntil: "domcontentloaded" })
  const row = dp.locator("tr").filter({ hasText: `Cinq${STAMP}` }).first()
  await row.waitFor({ timeout: 30000 })
  await shot(dp, `D1-a-cloturer-${RUN}`)
  // One word on a draft-only séance too: « Encaisser » bills the fiche and replaces the draft.
  await row.getByRole("button", { name: /^Encaisser$/ }).click()
  const bill = dp.getByRole("dialog").filter({ hasText: /Facturer cette intervention/ }).last()
  await bill.waitFor({ timeout: 15000 })
  const said = (await bill.innerText()).replace(/\s+/g, " ")
  await bill.getByRole("button", { name: /Facturer et encaisser/ }).click()
  await bill.waitFor({ state: "detached", timeout: 20000 }).catch(() => {})
  await dp.waitForTimeout(2000)
  const notes = liveNotesOf(P5.id)
  const still = await dp.locator("tr").filter({ hasText: `Cinq${STAMP}` }).count()
  if (/Remplace le brouillon de note/.test(said) && notes.length === 1 && /^\d{4}-\d+$/.test(notes[0].number)
    && notes[0].total === tarif && !draftExists(draft5Id) && still === 0)
    ok("D1", `draft-only séance: « Encaisser » → note ${notes[0].number} from the fiche, draft replaced, row answered`)
  else bad("D1", "draft-only séance in « À clôturer »", `said=«${said.slice(0, 160)}» notes=${JSON.stringify(notes)} draftLeft=${draftExists(draft5Id)} stillListed=${still}`)
})

step("D2", async () => {
  const row = dp.locator("tr, li").filter({ hasText: `Six${STAMP}` }).filter({ visible: true }).first()
  await row.waitFor({ timeout: 15000 })
  // Probe fix: « Remplir la fiche de soins » is a button that navigates (`router.push(ficheHref(…))`).
  await row.getByRole("button", { name: /Remplir la fiche de soins/ }).click()
  await dp.waitForURL((u) => u.searchParams.get("addRecord") === "1", { timeout: 15000 }).catch(() => {})
  const href = dp.url().replace(WEB, "")
  if (href === `/patients/${P6.id}?addRecord=1&appointmentId=${V6.id}`) ok("D2", `« Remplir la fiche de soins » → ${href}`)
  else bad("D2", "closure fiche door", `landed on ${href}`)
})

step("D3", async () => {
  await dp.goto(`${WEB}/appointments?appointmentId=${V1.id}`, { waitUntil: "domcontentloaded" })
  const d = dp.getByRole("dialog").filter({ hasText: new RegExp(`Un${STAMP}`) }).first()
  await d.waitFor({ timeout: 30000 })
  await dp.waitForTimeout(2000)
  const text = (await d.innerText()).replace(/\s+/g, " ")
  const door = await d.getByRole("button", { name: /Fiche de soins/ }).or(d.getByRole("link", { name: /Fiche de soins/ })).count()
  await shot(dp, `D3-rdv-window-${RUN}`)
  // After B5 the corrected séance owes the 10 DT its new price added: the window says so, on the new note.
  if (door > 0 && /À encaisser\s*10,000\s*DT/.test(text) && /note n°/.test(text))
    ok("D3", "RDV window: « Fiche de soins » + « À encaisser 10,000 DT · note n° … »")
  else bad("D3", "RDV window money line", `door=${door} text=${text.slice(0, 300)}`)
  await dp.keyboard.press("Escape").catch(() => {})
})

step("D4", async () => {
  await dp.goto(`${WEB}/patients/${P1.id}`, { waitUntil: "domcontentloaded" })
  const open = dp.getByRole("button", { name: /Ouvrir l.ordonnance/ }).filter({ visible: true }).first()
  if (!(await open.waitFor({ timeout: 30000 }).then(() => true).catch(() => false))) return skip("D4", "no « Ouvrir l'ordonnance » on the séance row")
  const before = pdfGets.length
  const prints0 = await printsOn(dp)
  await open.click()
  const dialog = dp.getByRole("dialog").filter({ hasText: /Ordonnance|ordonnance/ }).last()
  await dialog.waitFor({ timeout: 15000 })
  const print = dialog.getByRole("button", { name: /Imprimer/ }).first()
  await print.waitFor({ timeout: 20000 })
  await dp.waitForTimeout(1500)
  await print.click()
  await dp.waitForTimeout(3000)
  const prints = (await printsOn(dp)) - prints0
  const errors = await errorToasts(dp)
  await shot(dp, `D4-preview-print-${RUN}`)
  if (pdfGets.length > before && prints === 1 && !errors) ok("D4", "preview → « Imprimer »: PDF fetched, print() called once")
  else bad("D4", "preview print", `pdfGets=${pdfGets.length - before} prints=${prints} errors=${errors}`)
  await dp.keyboard.press("Escape").catch(() => {})
})

step("D5", async () => {
  const number = sql(`select "Number" from "Invoices" where "PatientId"='${P1.id}' and "Status"<>4 limit 1`)
  if (!number) return skip("D5", "no live note for P1")
  await dp.goto(`${WEB}/factures?search=${encodeURIComponent(number)}`, { waitUntil: "domcontentloaded" })
  const menu = dp.getByRole("button", { name: `Actions pour la facture ${number}` }).filter({ visible: true }).first()
  if (!(await menu.waitFor({ timeout: 30000 }).then(() => true).catch(() => false))) return skip("D5", `note ${number} not listed for the doctor`)
  await menu.click()
  await dp.getByRole("menuitem", { name: /Voir le PDF/ }).click()
  const dialog = dp.getByRole("dialog").last()
  const print = dialog.getByRole("button", { name: /Imprimer/ }).first()
  if (!(await print.waitFor({ timeout: 20000 }).then(() => true).catch(() => false))) return skip("D5", "no « Imprimer » in the note's PDF dialog")
  await dp.waitForTimeout(1500)
  const prints0 = await printsOn(dp)
  await print.click()
  await dp.waitForTimeout(3000)
  const prints = (await printsOn(dp)) - prints0
  const errors = await errorToasts(dp)
  await shot(dp, `D5-invoice-print-${RUN}`)
  if (!errors && prints === 1) ok("D5", `note ${number} PDF « Imprimer »: print() called once`)
  else bad("D5", "invoice PDF print", `prints=${prints} errors=${errors}`)
  await dp.keyboard.press("Escape").catch(() => {})
})

step("D6", async () => {
  await dp.goto(`${WEB}/`, { waitUntil: "domcontentloaded" })
  // Probe fix: wait for the day board's chair card, never a fixed delay.
  const card = dp.locator('a[aria-label^="Au fauteuil"]').first()
  if (!(await card.waitFor({ timeout: 30000 }).then(() => true).catch(() => false))) {
    await shot(dp, `D6-dashboard-${RUN}`)
    return skip("D6", "no « Au fauteuil » card on the day board right now")
  }
  const href = await card.getAttribute("href")
  const label = await card.getAttribute("aria-label")
  await shot(dp, `D6-dashboard-${RUN}`)
  if (/^\/patients\/[0-9a-f-]{36}\?addRecord=1&appointmentId=[0-9a-f-]{36}$/.test(href ?? "") && /Ouvrir la fiche de soins/.test(label ?? ""))
    ok("D6", `« Au fauteuil » opens ${href}`)
  else bad("D6", "chair card door", `href=${href} label=${label}`)
})

step("D7", async () => {
  if (!fiche7) return skip("D7", "arrange V7 failed")
  const endsAt = new Date(new Date(V7.appointmentDateTime).getTime() + 30 * 60_000)
  const deadline = endsAt.getTime() + 150_000
  let status = (await doc.get(`/appointments/${V7.id}`)).status
  while (Date.now() < deadline && status !== "Completed") {
    await new Promise((r) => setTimeout(r, 10_000))
    status = (await doc.get(`/appointments/${V7.id}`)).status
  }
  if (v7AfterFiche !== "Completed" && status === "Completed")
    ok("D7", `mid-séance fiche left it ${v7AfterFiche}; the job completed it after ${endsAt.toLocaleTimeString("fr-FR")}`)
  else bad("D7", "the minute job", `afterFiche=${v7AfterFiche} later=${status} (end ${endsAt.toISOString()})`)
})

step("D8", async () => {
  const found = []
  for (const [who, page] of [["desk", sp], ["doctor", dp]]) {
    await page.goto(`${WEB}/appointments`, { waitUntil: "domcontentloaded" })
    await page.getByRole("button", { name: /Actions du rendez-vous de/ }).first().waitFor({ state: "attached", timeout: 30000 })
    await page.waitForTimeout(2000)
    const pills = await page.getByRole("button", { name: /À encaisser/ }).count()
    const marks = await page.locator('[aria-label*="à encaisser"]').count()
    found.push(`${who}: pills=${pills} marks=${marks}`)
  }
  await shot(sp, `D8-agenda-${RUN}`)
  if (found.every((f) => f.endsWith("pills=0 marks=0"))) ok("D8", `agenda as it was — ${found.join(" · ")}`)
  else bad("D8", "the agenda still carries the desk's money", found.join(" · "))
})

step("D9", async () => {
  await dp.goto(`${WEB}/appointments?appointmentId=${V6.id}`, { waitUntil: "domcontentloaded" })
  const d = dp.getByRole("dialog").filter({ hasText: new RegExp(`Six${STAMP}`) }).first()
  await d.waitFor({ timeout: 30000 })
  await dp.waitForTimeout(1500)
  const text = (await d.innerText()).replace(/\s+/g, " ")
  const offer = await d.getByRole("button", { name: /Facturer cette consultation/ }).count()
  await shot(dp, `D9-rdv-window-no-draft-${RUN}`)
  await dp.keyboard.press("Escape").catch(() => {})
  if (/Non facturé/.test(text) && offer === 0) ok("D9", "RDV window of a séance with no fiche: « Non facturé », no « Facturer cette consultation »")
  else bad("D9", "the RDV window still raises a draft", `offer=${offer} text=${text.slice(0, 200)}`)
})

step("B6", async () => {
  const admin = client(await apiLogin(ADMIN, true))
  const hide = await admin.call("POST", "/clinics/money/hide", {})
  if (hide.status >= 300) return skip("B6", `could not hide money: ${hide.status} ${hide.raw.slice(0, 120)}`)
  const r = await desk.call("GET", "/appointments/to-collect")
  await sp.goto(`${WEB}/a-cloturer`, { waitUntil: "domcontentloaded" })
  await sp.getByRole("tab", { name: /Séances à clôturer/ }).waitFor({ timeout: 30000 }).catch(() => {})
  await sp.waitForTimeout(3000)
  const pill = await toCollectTab(sp).count()
  await shot(sp, `B6-mode-discret-${RUN}`)
  // Revert this walk's own mask on the QA copy (SQL, stated in the report).
  sql(`update "Clinics" set "IsMoneyHidden"=false where "IsMoneyHidden"=true`)
  if (r.status === 403 && pill === 0) ok("B6", "Mode discret: to-collect 403, no « À encaisser » tab")
  else bad("B6", "Mode discret", `to-collect=${r.status} pill=${pill}`)
})

// Probe fix (run 1): a crashed tab took every later row with it. A crash is reported, and the next row gets a new
// tab in the same signed-in context.
let crashed = { dp: false, sp: false }
const watch = () => {
  dp?.on("crash", () => { crashed.dp = true; console.log("  ⚠️  doctor tab crashed") })
  sp?.on("crash", () => { crashed.sp = true; console.log("  ⚠️  desk tab crashed") })
}
watch()
for (const s of S) {
  if (crashed.dp || dp?.isClosed()) { dp = await doctorCtx.newPage(); crashed.dp = false; watch() }
  if (crashed.sp || sp?.isClosed()) { sp = await deskCtx.newPage(); crashed.sp = false; watch() }
  try { await s.run() } catch (e) { bad(s.id, "threw", String(e.message).split("\n")[0].slice(0, 300)) }
}

await browser.close()
console.log("\n──────── summary")
for (const r of results) console.log(`${r.outcome} ${r.id} ${r.m}`)
console.log(findings.length ? `RED (${findings.length})` : "ALL CLEAR")
writeFileSync(join(HERE, `run-${RUN}.json`), JSON.stringify({ results, findings, stamp: STAMP }, null, 2))
