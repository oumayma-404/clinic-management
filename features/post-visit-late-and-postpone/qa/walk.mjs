// QA walk — features/post-visit-late-and-postpone. One launch, every scenario of qa/plan.md; fixes nothing.
// Re-run THIS file for run 2/3. Env: PW_PATH (playwright-core index.js), API, WEB, RUN (report suffix).
import { pathToFileURL, fileURLToPath } from "node:url"
import crypto from "node:crypto"
import fs from "node:fs"
import path from "node:path"

const pw = await import(pathToFileURL(process.env.PW_PATH).href)
const chromium = pw.chromium ?? pw.default?.chromium

const API = process.env.API ?? "http://localhost:5098/api"
const WEB = process.env.WEB ?? "http://localhost:3098"
const RUN = process.env.RUN ?? "1"
const HERE = path.dirname(fileURLToPath(import.meta.url))
const SHOTS = path.join(HERE, "shots")
fs.mkdirSync(SHOTS, { recursive: true })

const EMAIL = "salma.benyoussef@cabinet-ibnkhaldoun.tn"
const PASSWORD = "QaAudit2026!y"
const TOTP_SECRET = "4YRLT22RBPRP3RRKUKLFQERU4H62BRBO"
const DOCTOR = "9ed3d280-e3a1-4f11-ad19-8c993dd0da93" // Salma Ben Youssef, the QA admin's own doctor record

// ---------------------------------------------------------------- outcomes
const results = []
const ok = (id, m) => { console.log(`  ✅ [${id}] ${m}`); results.push({ id, outcome: "pass", m }) }
const bad = (id, m, d = "") => { console.log(`  ❌ [${id}] ${m} ${d}`); results.push({ id, outcome: "fail", m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); results.push({ id, outcome: "skip", m: why }) }
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
async function poll(fn, { timeout = 15000, every = 400 } = {}) {
  const t0 = Date.now()
  while (Date.now() - t0 < timeout) {
    try { const v = await fn(); if (v) return v } catch { /* keep polling */ }
    await sleep(every)
  }
  return null
}

// ---------------------------------------------------------------- time (Tunis = UTC+1, no DST)
const tunisDay = (d) => new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Tunis" }).format(d)
const TODAY = tunisDay(new Date())
const TOMORROW = tunisDay(new Date(Date.now() + 86400000))
// Visits that must already be over are booked YESTERDAY, so the walk holds at any hour (a 00:10 run broke « today »).
const PAST = tunisDay(new Date(Date.now() - 86400000))
const at = (day, hhmm) => new Date(`${day}T${hhmm}:00+01:00`)
const iso = (d) => d.toISOString()
const parseApiDate = (s) => new Date(/[zZ]|[+-]\d\d:\d\d$/.test(s) ? s : `${s}Z`)
const sameInstant = (a, b) => Math.abs(parseApiDate(a).getTime() - b.getTime()) < 1000

// ---------------------------------------------------------------- TOTP + API session (its own session family)
let lastCounter = -1
function totp() {
  const A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
  let bits = ""
  for (const c of TOTP_SECRET) bits += A.indexOf(c).toString(2).padStart(5, "0")
  const key = Buffer.from(bits.match(/.{8}/g).map((b) => parseInt(b, 2)))
  const counter = Math.floor(Date.now() / 1000 / 30)
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0)
  buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac("sha1", key).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, "0")
}
async function freshWindow() {
  // A code is single-use, and one computed late in its window expires in flight.
  for (;;) {
    const s = Date.now() / 1000
    const counter = Math.floor(s / 30)
    if (counter !== lastCounter && s % 30 < 18) { lastCounter = counter; return }
    await sleep((30 - (s % 30) + 1) * 1000)
  }
}

let token = null
async function apiLogin() {
  await freshWindow()
  const res = await fetch(`${API}/auth/login`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ email: EMAIL, password: PASSWORD, totpCode: totp() }),
  })
  const body = await res.json().catch(() => ({}))
  const v = body.value ?? body
  if (!v.accessToken) throw new Error(`API login failed ${res.status} ${JSON.stringify(body).slice(0, 200)}`)
  token = v.accessToken
}
async function api(method, p, body) {
  const res = await fetch(`${API}${p}`, {
    method,
    headers: { authorization: `Bearer ${token}`, ...(body ? { "content-type": "application/json" } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  })
  const text = await res.text()
  let json = null
  try { json = JSON.parse(text) } catch { /* csv or empty */ }
  return { status: res.status, json, text }
}
const getAppt = async (id) => (await api("GET", `/appointments/${id}`)).json
const pendingIds = async () => ((await api("GET", "/notifications/pending-reviews")).json ?? []).map((r) => r.appointmentId)

// ---------------------------------------------------------------- arrangement (own records only)
const STAMP = String(Date.now()).slice(-4)
const R = {} // key -> { id, patientId, name }
async function patient(key) {
  const r = await api("POST", "/patients", { firstName: "Test", lastName: `${key}-${STAMP}`, gender: "Female" })
  if (r.status >= 300) throw new Error(`patient ${key}: ${r.status} ${r.text.slice(0, 200)}`)
  return r.json
}
async function book(key, start, minutes, extra = {}) {
  const p = await patient(key)
  const r = await api("POST", "/appointments", {
    patientId: p.id, appointmentDateTime: iso(start), durationMinutes: minutes, doctorId: DOCTOR,
    allowOutsideWorkingHours: true, ...extra,
  })
  if (r.status >= 300) throw new Error(`book ${key}: ${r.status} ${r.text.slice(0, 200)}`)
  R[key] = { id: r.json.id, patientId: p.id, name: r.json.patientName ?? `Test ${key}-${STAMP}` }
  return R[key]
}
const put = (key, body) => api("PUT", `/appointments/${R[key].id}`, body)

// ---------------------------------------------------------------- browser
const browser = await chromium.launch({ channel: "chrome", headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "fr-FR", timezoneId: "Africa/Tunis" })
const page = await ctx.newPage()
page.setDefaultTimeout(15000)

// The pending-review queue is served filtered to the one visit a scenario is about.
let popupTarget = new Set()
await ctx.route("**/api/notifications/pending-reviews**", async (route) => {
  try {
    const resp = await route.fetch()
    let json = []
    try { json = await resp.json() } catch { /* empty */ }
    await route.fulfill({ response: resp, json: Array.isArray(json) ? json.filter((r) => popupTarget.has(r.appointmentId)) : [] })
  } catch { await route.continue().catch(() => {}) }
})

// Every navigation waits for the page to go quiet: leaving a page while its session refresh is in flight drops the
// rotated cookie, the next load presents the spent one, and the server revokes the whole session (run 1's lesson).
const settle = () => page.waitForLoadState("networkidle", { timeout: 20000 }).catch(() => {})
const go = async (url) => { await page.goto(url).catch(() => {}); await settle() }
const reload = async () => { await page.reload().catch(() => {}); await settle() }

const shot = async (name) => { const f = path.join(SHOTS, `run${RUN}-${name}.png`); await page.screenshot({ path: f }); return f }
const dialogs = () => page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible')
// The prompt's title changes on its « Reporter » step, so one locator must match both.
const popup = () => page.locator('[role="dialog"]').filter({ hasText: /Séance terminée|Reporter la séance/ })
const toastText = (re) => poll(async () => {
  const all = await page.locator("[data-sonner-toast]").allInnerTexts()
  return all.find((t) => re.test(t)) ?? null
}, { timeout: 10000, every: 200 })

async function browserLogin() {
  await go(`${WEB}/appointments`)
  await page.waitForURL(/\/login/, { timeout: 30000 })
  await page.fill("input[type=email]", EMAIL)
  await page.fill("input[type=password]", PASSWORD)
  await page.getByRole("button", { name: /Se connecter/ }).click()
  const code = page.locator("input[inputmode=numeric]")
  await code.waitFor({ timeout: 20000 })
  await freshWindow()
  await code.fill(totp()) // auto-submits on the 6th digit
  await page.waitForURL((u) => !u.pathname.startsWith("/login"), { timeout: 30000 })
  await settle()
  const session = await page.evaluate(async () => (await fetch("/bff/auth/session")).status)
  if (session !== 200) throw new Error(`no session after sign-in (bff/auth/session ${session})`)
}

async function openPopupFor(key) {
  popupTarget = new Set([R[key].id])
  await go(`${WEB}/appointments?date=${PAST}&view=day`)
  await page.evaluate(() => { try { localStorage.removeItem("clinic:pvr-snooze") } catch { /* none */ } })
  await reload()
  const dlg = popup()
  await dlg.waitFor({ state: "visible", timeout: 25000 })
  return dlg
}

async function rowOnOneLine(dlg, names) {
  const box = await dlg.boundingBox()
  const boxes = []
  for (const n of names) boxes.push(await dlg.getByRole("button", { name: n, exact: true }).boundingBox())
  const tops = boxes.map((b) => Math.round(b.y))
  const inside = boxes.every((b) => b.x >= box.x - 1 && b.x + b.width <= box.x + box.width + 1)
  return { oneLine: Math.max(...tops) - Math.min(...tops) <= 2, inside, tops, dialogWidth: Math.round(box.width) }
}

// Since the free-slot list (2026-10-30) a typed time sits behind « Autre heure… », and the form opens on the first
// day with a free time — which may be today — so the rows that expect « Demain » pick it first.
const pickDemain = (scope) => scope.getByRole("radio", { name: /^Demain/ }).click()

// Every footer button inside the footer's own box — run 3 found « Retour » cut to « tour » in the refusal state.
const footerInside = (dlg) => dlg.evaluate((el) => {
  const f = el.querySelector('[data-slot="dialog-footer"]')
  const fr = f.getBoundingClientRect(), cs = getComputedStyle(f)
  const left = fr.left + parseFloat(cs.paddingLeft), right = fr.right - parseFloat(cs.paddingRight)
  const btns = [...f.children].filter((b) => b.offsetParent).map((b) => { const r = b.getBoundingClientRect(); return { t: b.innerText.trim(), l: Math.round(r.left), r: Math.round(r.right), top: Math.round(r.top) } })
  return { ok: btns.length > 0 && btns.every((b) => b.l >= left - 0.5 && b.r <= right + 0.5), rows: new Set(btns.map((b) => b.top)).size, btns, box: [Math.round(left), Math.round(right)] }
})

// Truly on screen: inside the viewport AND the topmost element at its own centre. Run 5's « visible » passed for a
// message scrolled out of the dialog's body — a DOM query is not an eye pass.
const onScreen = (loc) => loc.evaluate((el) => {
  const r = el.getBoundingClientRect()
  if (r.width === 0 || r.height === 0 || r.top < 0 || r.bottom > innerHeight) return false
  const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2)
  return !!hit && (hit === el || el.contains(hit))
}).catch(() => false)

async function chooseTime(scope, hhmm) {
  const input = scope.locator('input[id^="postpone-time-"]')
  if (!(await input.isVisible().catch(() => false))) await scope.getByRole("button", { name: "Autre heure…" }).click()
  await input.click()
  await input.fill(hhmm)
  await input.press("Tab")
}

async function editDialog(key, day = PAST) {
  await go(`${WEB}/appointments?date=${day}&view=day&appointmentId=${R[key].id}`)
  const dlg = page.locator('[role="dialog"]').filter({ has: page.getByRole("radiogroup", { name: "Statut du rendez-vous" }) })
  await dlg.waitFor({ state: "visible", timeout: 25000 })
  return dlg
}
async function submitEdit(dlg, key) {
  const req = page.waitForRequest((r) => r.method() === "PUT" && r.url().endsWith(`/appointments/${R[key].id}`), { timeout: 15000 })
  await dlg.locator('button[type="submit"]').click()
  const r = await req
  let body = {}
  try { body = JSON.parse(r.postData() ?? "{}") } catch { /* none */ }
  return body
}

const envNotes = []
// `ONLY=D5,D6` drives just those rows (a re-run of the whole plan omits it).
const ONLY = process.env.ONLY ? new Set(process.env.ONLY.split(",")) : null
async function scenario(id, fn) {
  if (ONLY && !ONLY.has(id)) return
  // A lost session turns every later row red for one environment reason; say so and sign in again.
  if (new URL(page.url()).pathname.startsWith("/login")) {
    envNotes.push(`session lost before ${id} — signed in again`)
    console.log(`  ⚠ session lost before ${id}; signing in again`)
    await browserLogin().catch((e) => console.log("  ⚠ re-login failed:", e.message))
  }
  try { await fn() } catch (e) { bad(id, "threw", String(e.message ?? e).split("\n")[0]) ; await shot(`${id}-threw`).catch(() => {}) }
  // Leave no overlay behind for the next scenario.
  for (let i = 0; i < 3 && (await dialogs().count()) > 0; i++) { await page.keyboard.press("Escape").catch(() => {}); await sleep(250) }
}

// ================================================================ SETUP
console.log(`\nQA walk run ${RUN} — today ${TODAY}, tomorrow ${TOMORROW}, stamp ${STAMP}`)
await apiLogin()
// A re-run frees the slots an earlier run's test visits hold (only our own « Test R…-NNNN » records).
{
  const after = tunisDay(new Date(Date.now() + 2 * 86400000))
  const list = (await api("GET", `/appointments?startDate=${PAST}&endDate=${after}`)).json ?? []
  const mine = list.filter((a) => /^Test R\w+-\d{4}$/.test(a.patientName ?? "") && !["Cancelled", "NoShow"].includes(a.status))
  for (const a of mine) await api("PUT", `/appointments/${a.id}`, { status: "Cancelled", cancellationReason: "QA — run précédent" })
  console.log(`  freed ${mine.length} slot(s) held by earlier runs`)
}
const now = new Date()
const minuteFloor = new Date(Math.floor(now.getTime() / 60000) * 60000)
await book("R1", at(PAST, "09:00"), 30)
await book("RD", at(PAST, "09:30"), 30)
await book("R2", at(PAST, "12:00"), 60)
await book("R3", at(PAST, "13:00"), 30)
await book("R4", at(PAST, "13:30"), 30)
await book("R6", at(PAST, "14:00"), 30)
await book("R7", at(PAST, "14:30"), 30)
await book("R9", at(PAST, "15:00"), 60)
await book("R10", at(PAST, "16:00"), 30)
await book("R14", at(PAST, "17:00"), 60)
await book("R15", at(PAST, "18:00"), 30)
await book("RT", at(PAST, "08:00"), 30)
await book("R16", at(PAST, "08:30"), 30)
await book("RF2", at(PAST, "10:00"), 30, { allowOverlap: true })
await book("RF3", at(PAST, "10:30"), 30, { allowOverlap: true })
await book("RF5", at(PAST, "11:00"), 30, { allowOverlap: true })
await book("RF6", at(PAST, "11:30"), 30, { allowOverlap: true })
// Live visits land on TODAY's agenda (where somebody may be testing by hand), so only when a row needs them.
const needsLive = !ONLY || ["A5", "A7", "A7-save"].some((id) => ONLY.has(id))
let R13_DAY = TODAY, r5Retard = { status: "—" }, r5BookedEnd = new Date(), r5ExpectedEnd = new Date()
if (needsLive) {
  await book("R13", new Date(minuteFloor.getTime() - 10 * 60000), 60, { allowOverlap: true })
  R13_DAY = tunisDay(new Date(minuteFloor.getTime() - 10 * 60000))
  await book("R5", new Date(minuteFloor.getTime() - 2 * 60000), 5, { allowOverlap: true })
}
await book("R8", at(TOMORROW, "09:00"), 30)
await book("R12", at(TOMORROW, "15:00"), 60)
// States the scenarios start from.
await put("R10", { status: "Cancelled", cancellationReason: "QA" })
await book("R11", at(PAST, "16:00"), 30)
if (needsLive) {
  await put("R13", { status: "InProgress" })
  await put("R5", { status: "InProgress" })
  r5Retard = await put("R5", { lateArrivalMinutes: 5 })
  const r5Start = parseApiDate((await getAppt(R.R5.id)).appointmentDateTime)
  r5BookedEnd = new Date(r5Start.getTime() + 5 * 60000)
  r5ExpectedEnd = new Date(r5Start.getTime() + 10 * 60000)
}
await put("R14", { status: "Completed" })
await put("R14", { lateArrivalMinutes: 10 })
console.log(`  setup: ${Object.keys(R).length} visits · R5 retard PUT ${r5Retard.status} · booked end ${r5BookedEnd.toLocaleTimeString("fr-FR")} · expected end ${r5ExpectedEnd.toLocaleTimeString("fr-FR")}`)

// A5's two server-side checks fire on their own clock while the browser walks.
const a5 = { check1: null, check2: null }
const runA5 = !ONLY || ONLY.has("A5")
const a5Check1 = !runA5 ? Promise.resolve() : (async () => {
  await sleep(Math.max(0, r5BookedEnd.getTime() + 70000 - Date.now()))
  const a = await getAppt(R.R5.id)
  const toClose = (await api("GET", "/appointments/to-close?days=7&pageSize=100")).json
  const listed = (toClose?.visits?.items ?? []).some((v) => v.appointmentId === R.R5.id)
  const pending = (await pendingIds()).includes(R.R5.id)
  a5.check1 = { status: a.status, late: a.lateArrivalMinutes, listed, pending, at: new Date().toLocaleTimeString("fr-FR") }
})()
const a5Check2 = !runA5 ? Promise.resolve() : (async () => {
  await sleep(Math.max(0, r5ExpectedEnd.getTime() + 75000 - Date.now()))
  const a = await getAppt(R.R5.id)
  const pending = (await pendingIds()).includes(R.R5.id)
  a5.check2 = { status: a.status, pending, at: new Date().toLocaleTimeString("fr-FR") }
})()

// Past visits must have been closed by the job before the presence questions mean anything.
const settled = await poll(async () => (await getAppt(R.R1.id)).status === "AwaitingClosure" && (await getAppt(R.R4.id)).status === "AwaitingClosure", { timeout: 120000, every: 3000 })
console.log(`  job closed the past visits: ${settled ? "yes" : "NO"}`)

await browserLogin()
console.log("  signed in\n")

// ================================================================ TIER A + B + C on the popup
await scenario("A1", async () => {
  await page.setViewportSize({ width: 1536, height: 730 })
  const dlg = await openPopupFor("R1")
  const r = await rowOnOneLine(dlg, ["Plus tard", "Reporter", "Absent", "Venu"])
  const f = await shot("A1-1536x730")
  r.oneLine && r.inside ? ok("A1", `4 answers on one line inside a ${r.dialogWidth}px dialog (tops ${r.tops}) · ${path.basename(f)}`)
    : bad("A1", "answers not on one line / outside the dialog", JSON.stringify(r))
  await page.setViewportSize({ width: 820, height: 1024 })
  await sleep(400)
  const r2 = await rowOnOneLine(popup(), ["Plus tard", "Reporter", "Absent", "Venu"])
  await shot("A1-820x1024")
  r2.oneLine && r2.inside ? ok("A1@820", `one line at 820 (fine pointer), dialog ${r2.dialogWidth}px`) : bad("A1@820", "not one line at 820", JSON.stringify(r2))
})

await scenario("B3", async () => {
  await page.setViewportSize({ width: 1536, height: 730 })
  const dlg = await openPopupFor("R1")
  await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
  await dlg.getByText("Jour", { exact: true }).waitFor()
  await dlg.getByRole("button", { name: "Retour" }).click()
  const back = await poll(async () => (await dlg.getByRole("button", { name: "Venu", exact: true }).isVisible()) && (await dlg.getByRole("button", { name: "Absent", exact: true }).isVisible()))
  back ? ok("B3", "« Retour » brings the four answers back") : bad("B3", "« Retour » did not return to step 1")
})

await scenario("C4", async () => {
  await page.setViewportSize({ width: 1536, height: 730 })
  const dlg = await openPopupFor("R1")
  await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
  await dlg.getByText("Jour", { exact: true }).waitFor()
  const btn = dlg.getByRole("button", { name: "Reporter", exact: true })
  const b = await btn.boundingBox()
  await shot("C4-reporter-1536x730")
  b && b.y + b.height <= 730 ? ok("C4", `« Reporter » at y=${Math.round(b.y)} inside a 730px window`) : bad("C4", "« Reporter » below the fold at 730px", JSON.stringify(b))
})

await scenario("A2", async () => {
  await page.setViewportSize({ width: 1536, height: 730 })
  const dlg = await openPopupFor("R1")
  await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
  await dlg.getByText("Jour", { exact: true }).waitFor()
  // A listed time, picked from the server's free-slot grid — no typing, and the save must take it as is.
  const demain = dlg.getByRole("radio", { name: /^Demain/ })
  await demain.click()
  const checked = await demain.getAttribute("aria-checked")
  const chip = (await demain.innerText()).replace(/\s+/g, " ")
  const twelve = dlg.getByRole("radio", { name: "12:00", exact: true })
  await twelve.click()
  const picked = await twelve.getAttribute("aria-checked")
  const footer = await footerInside(dlg)
  await page.mouse.move(5, 5)
  await sleep(400) // the pressed cell's colour transition: run 3's shot caught it half-way and looked disabled
  await shot("A2-free-slots")
  await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
  const t = await toastText(/Séance reportée/)
  const refused = await dlg.locator('[role="alert"]').count().catch(() => 0)
  const closed = await poll(async () => !(await popup().isVisible()), { timeout: 8000 })
  const a = await getAppt(R.R1.id)
  const pending = (await pendingIds()).includes(R.R1.id)
  const moved = sameInstant(a.appointmentDateTime, at(TOMORROW, "12:00"))
  if (t && closed && moved && a.status === "Scheduled" && a.lateArrivalMinutes == null && !pending && checked === "true" && picked === "true" && /libres?/.test(chip))
    ok("A2", `chip « ${chip} » → listed 12:00 (checked) → Reporter → moved to ${TOMORROW} 12:00, no refusal, review gone`)
  else bad("A2", "Reporter outcome", JSON.stringify({ chip, picked, toast: t, refused, closed, demainChecked: checked, at: a.appointmentDateTime, status: a.status, late: a.lateArrivalMinutes, pending }))
  footer.ok && footer.rows === 1
    ? ok("A2-footer", `footer one row, every button inside (${footer.btns.map((b) => `${b.t} ${b.l}–${b.r}`).join(" · ")}; box ${footer.box.join("–")})`)
    : bad("A2-footer", "footer", JSON.stringify(footer))
})

await scenario("B6+A3", async () => {
  await page.setViewportSize({ width: 1440, height: 900 })
  const dlg = await openPopupFor("R3")
  await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
  await dlg.getByText("Jour", { exact: true }).waitFor()
  let failOnce = true
  const apptUrl = `**/api/appointments/${R.R3.id}`
  await page.route(apptUrl, async (route) => {
    if (route.request().method() === "PUT" && failOnce) {
      failOnce = false
      return route.fulfill({ status: 500, contentType: "application/json", headers: { "access-control-allow-origin": WEB, "access-control-allow-credentials": "true" }, body: JSON.stringify({ error: "Panne simulée (QA)." }) })
    }
    return route.continue()
  })
  await dlg.getByRole("button", { name: "Mettre en liste d'attente" }).click()
  const err = await toastText(/Panne simulée|Échec/)
  const stillOpen = await dlg.getByText("Jour", { exact: true }).isVisible()
  await dlg.getByRole("button", { name: "Mettre en liste d'attente" }).click()
  const t = await toastText(/Ajouté à la liste d'attente/)
  await page.unroute(apptUrl)
  const a = await getAppt(R.R3.id)
  const wl = (await api("GET", "/waiting-list?activeOnly=true&pageSize=200")).json
  const entries = (wl?.items ?? wl ?? []).filter((e) => e.patientId === R.R3.patientId)
  err && stillOpen ? ok("B6", `failed cancel → error toast, form kept open (« ${err.slice(0, 50)} »)`) : bad("B6", "failure path", JSON.stringify({ err, stillOpen }))
  entries.length === 1 ? ok("B6-dup", "retry added no second waiting-list entry (1)") : bad("B6-dup", `waiting-list entries for the patient: ${entries.length}`)
  // `AppointmentDto` carries no motif (run 1's probe bug): the cheapest decisive read is the row itself.
  const { execSync } = await import("node:child_process")
  const reason = execSync(`docker exec clinic-postgres psql -U clinic_user -d clinic_pvl_qa -At -c "select \\"CancellationReason\\" from \\"Appointments\\" where \\"Id\\"='${R.R3.id}'"`).toString().trim()
  t && a.status === "Cancelled" && reason === "Reporté par le patient"
    ? ok("A3", `Annulé, motif « ${reason} » (sql), toast shown`)
    : bad("A3", "liste d'attente outcome", JSON.stringify({ toast: t, status: a.status, reason }))
})

await scenario("A4", async () => {
  await page.setViewportSize({ width: 1536, height: 730 })
  const dlg = await openPopupFor("R2")
  await dlg.getByRole("button", { name: "Venu", exact: true }).click()
  await dlg.getByRole("button", { name: "Noter un retard" }).click()
  await dlg.getByRole("radio", { name: "15 min" }).click()
  const t1 = await toastText(/Retard de 15 min noté/)
  const a1 = await poll(async () => { const a = await getAppt(R.R2.id); return a.lateArrivalMinutes === 15 ? a : null })
  await shot("A4-retard-noted")
  const pill = await dlg.getByText("Retard de 15 min").isVisible()
  await dlg.getByRole("button", { name: "Modifier" }).click()
  await dlg.getByRole("radio", { name: "Aucun" }).click()
  const t2 = await toastText(/Retard retiré/)
  const a2 = await poll(async () => { const a = await getAppt(R.R2.id); return a.lateArrivalMinutes == null ? a : null })
  await dlg.getByRole("button", { name: "Noter un retard" }).click()
  await dlg.getByRole("radio", { name: "15 min" }).click()
  const a3 = await poll(async () => { const a = await getAppt(R.R2.id); return a.lateArrivalMinutes === 15 ? a : null })
  a1 && a1.status === "AwaitingClosure" && t1 && pill ? ok("A4", `retard 15 saved at once, statut unchanged (${a1.status}), pill shown`) : bad("A4", "retard 15", JSON.stringify({ t1, pill, status: a1?.status }))
  a2 && t2 ? ok("A4-aucun", "« Aucun » cleared it") : bad("A4-aucun", "clear", JSON.stringify({ t2, late: a2?.lateArrivalMinutes }))
  a3 ? ok("A4-again", "15 recorded again for A6") : bad("A4-again", "re-record failed")
  const reviews = (await pendingIds()).filter((x) => x === R.R2.id).length
  reviews === 1 ? ok("C3", "exactly one review for R2 after the retard") : bad("C3", `reviews for R2: ${reviews}`)
})

await scenario("B1", async () => {
  await page.setViewportSize({ width: 1440, height: 900 })
  const dlg = await openPopupFor("R16")
  await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
  await dlg.getByText("Jour", { exact: true }).waitFor()
  await pickDemain(dlg)
  // 09:00 is R8's: the list does not offer it, so the deliberate overlap goes through « Autre heure… ».
  const offered = await dlg.getByRole("radio", { name: "09:00", exact: true }).count()
  await chooseTime(dlg, "09:00")
  await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
  const alert = await poll(async () => (await dlg.locator('[role="alert"]').count()) > 0 ? dlg.locator('[role="alert"]').first().innerText() : null)
  const again = dlg.getByRole("button", { name: "Reporter quand même" })
  const hasAgain = await again.isVisible().catch(() => false)
  const b1Seen = await onScreen(dlg.locator('[role="alert"]').first())
  await shot("B1-slot-taken")
  offered === 0 ? ok("B1-list", "the taken 09:00 is not offered in the list") : bad("B1-list", "a taken time was listed as free")
  b1Seen ? ok("B1-seen", "the clash is on screen beside the buttons") : bad("B1-seen", "the clash is not on screen")
  if (hasAgain) await again.click()
  const t = await toastText(/Séance reportée/)
  const a = await getAppt(R.R16.id)
  alert && hasAgain && t && sameInstant(a.appointmentDateTime, at(TOMORROW, "09:00"))
    ? ok("B1", `clash named (« ${alert.replace(/\s+/g, " ").slice(0, 80)} ») → « Reporter quand même » → moved`)
    : bad("B1", "slot-taken path", JSON.stringify({ alert, hasAgain, toast: t, at: a.appointmentDateTime }))
})

await scenario("B2", async () => {
  const dlg = await openPopupFor("R15")
  await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
  await dlg.getByText("Jour", { exact: true }).waitFor()
  await pickDemain(dlg)
  // The booked 18:00 is outside the hours, so only « Autre heure… » (prefilled with it) can ask for it.
  await chooseTime(dlg, "18:00")
  await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
  const alert = await poll(async () => (await dlg.locator('[role="alert"]').count()) > 0 ? dlg.locator('[role="alert"]').first().innerText() : null)
  const again = dlg.getByRole("button", { name: "Reporter quand même" })
  const hasAgain = await again.isVisible().catch(() => false)
  if (hasAgain) await again.click()
  const t = await toastText(/Séance reportée/)
  const a = await getAppt(R.R15.id)
  alert && hasAgain && t && sameInstant(a.appointmentDateTime, at(TOMORROW, "18:00"))
    ? ok("B2", `closed hour named (« ${alert.replace(/\s+/g, " ").slice(0, 80)} ») → moved anyway`)
    : bad("B2", "hours path", JSON.stringify({ alert, hasAgain, toast: t, at: a.appointmentDateTime }))
})

// ================================================================ free slots (2026-10-30)
const frLongDay = (iso) => new Intl.DateTimeFormat("fr-FR", { weekday: "long", day: "numeric", month: "long", timeZone: "UTC" }).format(new Date(`${iso}T00:00:00Z`))

await scenario("F2", async () => {
  await page.setViewportSize({ width: 1536, height: 730 })
  // A congé among the five chips: the copy's own, else one recorded here on the next open day after tomorrow.
  const offeredNow = async () => ((await api("GET", `/appointments/${R.RF2.id}/free-slots`)).json?.days ?? []).filter((d) => d.state !== "Closed" && d.state !== "Unrestricted")
  let chips = (await offeredNow()).slice(0, 5)
  let leaveId = null
  let target = chips.find((d) => d.state === "Leave")
  if (!target) {
    const open = chips.find((d) => d.date > TOMORROW && d.state === "Open")
    if (!open) return skip("F2", `no chip day to put a congé on (${chips.map((d) => `${d.date}:${d.state}`).join(" ")})`)
    const r = await api("POST", `/doctors/${DOCTOR}/leaves`, { startDate: open.date, endDate: open.date, note: `QA ${STAMP}` })
    if (r.status >= 300) return bad("F2", "could not record the congé", `${r.status} ${r.text.slice(0, 160)}`)
    leaveId = r.json?.leave?.id
    chips = (await offeredNow()).slice(0, 5)
    target = chips.find((d) => d.date === open.date)
  }
  try {
    const dlg = await openPopupFor("RF2")
    await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
    await dlg.getByText("Jour", { exact: true }).waitFor()
    const chip = dlg.getByRole("radio", { name: new RegExp(`${frLongDay(target.date)} · Congé$`) })
    const chipText = (await chip.innerText()).replace(/\s+/g, " ")
    await chip.click()
    const checked = await chip.getAttribute("aria-checked")
    const sentence = await dlg.getByText("Le praticien est en congé ce jour-là.").isVisible()
    const times = await dlg.getByRole("radiogroup", { name: /^Heures libres/ }).count()
    const submitOff = await dlg.getByRole("button", { name: "Reporter", exact: true }).isDisabled()
    await shot("F2-conge-chip")
    // The override path stays: a typed time on a congé day is refused by name, never silently.
    await chooseTime(dlg, "14:00")
    await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
    const alert = await poll(async () => (await dlg.locator('[role="alert"]').count()) > 0 ? dlg.locator('[role="alert"]').first().innerText() : null)
    const again = await dlg.getByRole("button", { name: "Reporter quand même" }).isVisible().catch(() => false)
    const alertSeen = await onScreen(dlg.locator('[role="alert"]').first())
    const footer = await footerInside(dlg)
    await shot("F2-conge-typed")
    alertSeen ? ok("F2-seen", "the refusal is on screen beside the buttons") : bad("F2-seen", "the refusal is not on screen")
    footer.ok
      ? ok("F2-footer", `refusal footer: ${footer.rows} row(s), every button inside (${footer.btns.map((b) => `${b.t} ${b.l}–${b.r}`).join(" · ")}; box ${footer.box.join("–")})`)
      : bad("F2-footer", "a footer button outside the dialog", JSON.stringify(footer))
    const a = await getAppt(R.RF2.id)
    const unmoved = sameInstant(a.appointmentDateTime, at(PAST, "10:00"))
    target.state === "Leave" && /Congé/.test(chipText) && checked === "true" && sentence && times === 0 && submitOff
      ? ok("F2", `chip « ${chipText} » → « Le praticien est en congé ce jour-là. », no time listed, Reporter disabled`)
      : bad("F2", "congé day", JSON.stringify({ state: target.state, chipText, checked, sentence, times, submitOff }))
    alert && again && unmoved
      ? ok("F2-typed", `typed 14:00 on the congé → refused « ${alert.replace(/\s+/g, " ").slice(0, 80)} » + « Reporter quand même »; not moved`)
      : bad("F2-typed", "override path", JSON.stringify({ alert, again, at: a.appointmentDateTime }))
  } finally {
    if (leaveId) await api("DELETE", `/doctors/${DOCTOR}/leaves/${leaveId}`)
  }
})

await scenario("F3", async () => {
  await page.setViewportSize({ width: 1536, height: 730 })
  const dlg = await openPopupFor("RF3")
  // The real answer, served as a 500 with the server's own CORS headers, until « Réessayer ».
  let failing = true
  const pattern = "**/api/appointments/*/free-slots**"
  await page.route(pattern, async (route) => {
    if (!failing) return route.continue()
    const resp = await route.fetch()
    const headers = { ...resp.headers() }
    delete headers["content-length"]
    delete headers["content-encoding"]
    await route.fulfill({ status: 500, headers: { ...headers, "content-type": "application/json" }, body: JSON.stringify({ error: "Panne simulée (QA)." }) })
  })
  try {
    await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
    const notice = await dlg.getByText("Les heures libres n'ont pas pu être chargées.").waitFor({ timeout: 10000 }).then(() => true).catch(() => false)
    const field = await dlg.locator('input[id^="postpone-time-"]').isVisible().catch(() => false)
    const other = await dlg.getByRole("button", { name: "Autre heure…" }).count()
    const dateBtn = (await dlg.getByRole("button", { name: "Choisir une autre date" }).innerText().catch(() => "")).trim()
    await shot("F3-failed-read")
    failing = false
    await dlg.getByRole("button", { name: "Réessayer" }).click()
    const grid = await dlg.getByRole("radiogroup", { name: /^Heures libres/ }).first().waitFor({ timeout: 10000 }).then(() => true).catch(() => false)
    const noticeGone = !(await dlg.getByText("Les heures libres n'ont pas pu être chargées.").isVisible().catch(() => false))
    const fieldGone = !(await dlg.locator('input[id^="postpone-time-"]').isVisible().catch(() => false))
    const dayChips = await dlg.getByRole("radiogroup", { name: "Jour" }).getByRole("radio").count()
    notice && field && other === 0
      ? ok("F3", `500 → « Les heures libres n'ont pas pu être chargées. » + Réessayer; the typed time stays usable (day ${dateBtn || "?"})`)
      : bad("F3", "failed read", JSON.stringify({ notice, field, other, dateBtn }))
    grid && noticeGone && fieldGone && dayChips > 0
      ? ok("F3-retry", `Réessayer → ${dayChips} day chips and the free-time grid`)
      : bad("F3-retry", "retry", JSON.stringify({ grid, noticeGone, fieldGone, dayChips }))
  } finally {
    await page.unroute(pattern)
  }
})

await scenario("F4", async () => {
  await page.setViewportSize({ width: 1536, height: 730 })
  // A day past the 14-day window, picked in the calendar, loads a window starting there (AC-14).
  const far = new Date(Date.now() + 21 * 86400000)
  while (far.getUTCDay() === 0) far.setUTCDate(far.getUTCDate() + 1)
  const farIso = tunisDay(far)
  const [y, m, d] = farIso.split("-").map(Number)
  const reads = []
  const onReq = (r) => { if (r.url().includes("/free-slots")) reads.push(r.url()) }
  page.on("request", onReq)
  try {
    const dlg = await openPopupFor("RF3")
    await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
    await dlg.getByText("Jour", { exact: true }).waitFor()
    await dlg.getByRole("button", { name: "Choisir une autre date" }).click()
    const pop = page.locator('[data-slot="popover-content"]:visible').first()
    await pop.waitFor()
    const monthName = new Intl.DateTimeFormat("fr-FR", { month: "long", timeZone: "UTC" }).format(new Date(Date.UTC(y, m - 1, 1)))
    const dayRe = new RegExp(`\\b${d} ${monthName} ${y}`)
    for (let i = 0; i < 2 && (await pop.getByRole("button", { name: dayRe }).count()) === 0; i++) {
      await pop.locator('.rdp-button_next, button[aria-label*="next" i], button[aria-label*="suivant" i]').first().click()
    }
    await pop.getByRole("button", { name: dayRe }).first().click()
    const chipLabel = new RegExp(`^${new Intl.DateTimeFormat("fr-FR", { weekday: "short", timeZone: "UTC" }).format(far).replace(".", "\\.")} ${d},`, "i")
    const chip = dlg.getByRole("radio", { name: chipLabel })
    const shown = await chip.waitFor({ timeout: 10000 }).then(() => true).catch(() => false)
    const checked = shown ? await chip.getAttribute("aria-checked") : null
    const heading = await dlg.getByText(`Heures libres · ${frLongDay(farIso)}`).isVisible().catch(() => false)
    const grid = await dlg.getByRole("radiogroup", { name: /^Heures libres/ }).count()
    const asked = reads.some((u) => u.includes(`from=${farIso}`))
    await shot("F4-far-day")
    shown && checked === "true" && heading && grid > 0 && asked
      ? ok("F4", `calendar ${farIso} → read from=${farIso}, its chip checked, « Heures libres · ${frLongDay(farIso)} » listed`)
      : bad("F4", "far day", JSON.stringify({ farIso, shown, checked, heading, grid, reads }))
  } finally {
    page.off("request", onReq)
  }
})

// The list is read once: a listed time can go past, or be taken, while the form stays open. Both are forced by
// editing the read in flight (the clock cannot be waited out for long, and a colleague cannot be summoned).
async function withEditedSlots(edit, fn) {
  const pattern = "**/api/appointments/*/free-slots**"
  const state = { reads: 0 }
  await page.route(pattern, async (route) => {
    state.reads++
    const resp = await route.fetch()
    let json = null
    try { json = await resp.json() } catch { /* not json */ }
    if (json && state.reads === 1) edit(json)
    await route.fulfill({ response: resp, json })
  })
  try { return await fn(state) } finally { await page.unroute(pattern) }
}

await scenario("F5", async () => {
  await page.setViewportSize({ width: 1536, height: 730 })
  // A start 1–2 minutes ahead, injected into today's read; it is picked while still future and pressed once past.
  const target = new Date(Math.ceil((Date.now() + 60000) / 60000) * 60000)
  const hhmm = new Intl.DateTimeFormat("fr-FR", { hour: "2-digit", minute: "2-digit", timeZone: "Africa/Tunis" }).format(target)
  const puts = []
  const onReq = (r) => { if (r.method() === "PUT" && r.url().endsWith(`/appointments/${R.RF5.id}`)) puts.push(r.url()) }
  page.on("request", onReq)
  try {
    await withEditedSlots((json) => {
      const today = json.days.find((d) => d.date === TODAY)
      if (today) { today.state = "Open"; today.starts = [hhmm, ...today.starts.filter((s) => s !== hhmm)] }
    }, async (state) => {
      const dlg = await openPopupFor("RF5")
      await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
      await dlg.getByText("Jour", { exact: true }).waitFor()
      await dlg.getByRole("radio", { name: /^Aujourd'hui/ }).click()
      await dlg.getByRole("radio", { name: hhmm, exact: true }).click()
      const enabledBefore = await dlg.getByRole("button", { name: "Reporter", exact: true }).isEnabled()
      await sleep(Math.max(0, target.getTime() + 1500 - Date.now()))
      const readsBefore = state.reads
      // Usually nothing re-rendered since the pick, so the button is still enabled and the press meets the save's
      // own check. If something did re-render (the prompt's poll), the render-time guard already disabled it.
      const button = dlg.getByRole("button", { name: "Reporter", exact: true })
      const pressed = await button.isEnabled() ? await button.click({ timeout: 3000 }).then(() => true).catch(() => false) : false
      const message = dlg.getByText("Cette heure est déjà passée.")
      const notice = await message.waitFor({ timeout: 8000 }).then(() => onScreen(message)).catch(() => false)
      const reread = pressed ? await poll(async () => state.reads > readsBefore, { timeout: 8000 }) : true
      const disabled = await button.isDisabled()
      await shot("F5-time-passed")
      const a = await getAppt(R.RF5.id)
      enabledBefore && notice && reread && disabled && puts.length === 0 && sameInstant(a.appointmentDateTime, at(PAST, "11:00"))
        ? ok("F5", `listed ${hhmm} went past while open → ${pressed ? "pressed: no PUT, list re-read" : "already disabled on re-render"}, « Cette heure est déjà passée. », Reporter disabled`)
        : bad("F5", "time passed on screen", JSON.stringify({ hhmm, enabledBefore, pressed, notice, reread: !!reread, disabled, puts: puts.length, at: a.appointmentDateTime }))
    })
  } finally {
    page.off("request", onReq)
  }
})

await scenario("F6", async () => {
  await page.setViewportSize({ width: 1536, height: 730 })
  // 09:00 tomorrow is R8's: listed anyway by an edited first read, as if a colleague booked it after the read.
  await withEditedSlots((json) => {
    const tomorrow = json.days.find((d) => d.date === TOMORROW)
    if (tomorrow) { tomorrow.state = "Open"; tomorrow.starts = ["09:00", ...tomorrow.starts.filter((s) => s !== "09:00")] }
  }, async (state) => {
    const dlg = await openPopupFor("RF6")
    await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
    await dlg.getByText("Jour", { exact: true }).waitFor()
    await pickDemain(dlg)
    await dlg.getByRole("radio", { name: "09:00", exact: true }).click()
    const readsBefore = state.reads
    await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
    const alert = await poll(async () => (await dlg.locator('[role="alert"]').count()) > 0 ? dlg.locator('[role="alert"]').first().innerText() : null)
    const again = await dlg.getByRole("button", { name: "Reporter quand même" }).isVisible().catch(() => false)
    const reread = await poll(async () => state.reads > readsBefore, { timeout: 8000 })
    const stillListed = await poll(async () => (await dlg.getByRole("radio", { name: "09:00", exact: true }).count()) === 0 ? "gone" : null, { timeout: 8000 })
    const seen = await onScreen(dlg.locator('[role="alert"]').first())
    await shot("F6-taken-since-read")
    const a = await getAppt(R.RF6.id)
    alert && seen && again && reread && stillListed === "gone" && sameInstant(a.appointmentDateTime, at(PAST, "11:30"))
      ? ok("F6", `taken since the read → « ${alert.replace(/\s+/g, " ").slice(0, 70)} » + « Reporter quand même »; list re-read, 09:00 no longer listed; not moved`)
      : bad("F6", "taken since the read", JSON.stringify({ alert, seen, again, reread: !!reread, stillListed, at: a.appointmentDateTime }))
  })
})

await scenario("F7", async () => {
  await page.setViewportSize({ width: 1536, height: 730 })
  // A cabinet with no hours saved anywhere: the read says « Unrestricted » for every day (edited in flight).
  await withEditedSlots((json) => {
    for (const d of json.days) { d.state = "Unrestricted"; d.starts = [] }
  }, async () => {
    const dlg = await openPopupFor("RF6")
    await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
    await dlg.getByText("Jour", { exact: true }).waitFor()
    const sentence = await dlg.getByText("Aucun horaire n'est enregistré pour ce cabinet.").waitFor({ timeout: 8000 }).then(() => true).catch(() => false)
    const field = await dlg.locator('input[id^="postpone-time-"]').isVisible().catch(() => false)
    const chips = await dlg.getByRole("radiogroup", { name: "Jour" }).getByRole("radio").count()
    const calendar = await dlg.getByRole("button", { name: "Choisir une autre date" }).isVisible()
    const grid = await dlg.getByRole("radiogroup", { name: /^Heures libres/ }).count()
    const back = await dlg.getByRole("button", { name: "Voir les heures libres" }).count()
    const enabled = await dlg.getByRole("button", { name: "Reporter", exact: true }).isEnabled()
    await shot("F7-no-hours")
    sentence && field && chips === 0 && calendar && grid === 0 && back === 0 && enabled
      ? ok("F7", "no hours saved → « Aucun horaire n'est enregistré pour ce cabinet. », calendar + typed time, no grid, Reporter usable")
      : bad("F7", "unrestricted", JSON.stringify({ sentence, field, chips, calendar, grid, back, enabled }))
  })
})

await scenario("D2", async () => {
  const dlg = await openPopupFor("RD")
  await dlg.getByRole("button", { name: "Plus tard", exact: true }).click()
  const closed = await poll(async () => !(await popup().isVisible()), { timeout: 5000 })
  await reload()
  await sleep(6000)
  const back = await popup().isVisible()
  const snooze = await page.evaluate(() => localStorage.getItem("clinic:pvr-snooze"))
  closed && !back && snooze ? ok("D2", "« Plus tard » closes and the prompt stays away after a reload (snoozed)") : bad("D2", "snooze", JSON.stringify({ closed, back, snooze }))
})

// ================================================================ agenda
await scenario("A6", async () => {
  popupTarget = new Set()
  await page.setViewportSize({ width: 1440, height: 900 })
  await go(`${WEB}/appointments?date=${PAST}&view=day`)
  const block = page.locator(`[aria-label^="${R.R2.name} ·"]:visible`).first()
  await block.waitFor({ timeout: 20000 })
  await block.scrollIntoViewIfNeeded()
  const label = await block.getAttribute("aria-label")
  const text = await block.innerText()
  await shot("A6-block-1440")
  label.includes("arrivé avec 15 min de retard") && /\+15/.test(text)
    ? ok("A6", `block shows « +15 », name « …${label.slice(label.indexOf("arrivé"), label.indexOf("arrivé") + 30)}… »`)
    : bad("A6", "pill/name", JSON.stringify({ label, text: text.slice(0, 120) }))
  await page.setViewportSize({ width: 390, height: 844 })
  await go(`${WEB}/appointments?date=${PAST}&view=day`)
  const pblock = page.locator(`[aria-label^="${R.R2.name} ·"]:visible`).first()
  await pblock.waitFor({ timeout: 20000 })
  await pblock.scrollIntoViewIfNeeded()
  const ptext = await pblock.innerText()
  await shot("A6-block-390")
  ;/\+15/.test(ptext) ? ok("A6@390", "phone block shows « +15 »") : bad("A6@390", "no pill on the phone block", ptext.slice(0, 120))
})

async function menuFor(key, day) {
  await go(`${WEB}/appointments?date=${day}&view=day`)
  const trigger = page.getByRole("button", { name: `Actions du rendez-vous de ${R[key].name}` }).first()
  await trigger.waitFor({ timeout: 20000 })
  await trigger.scrollIntoViewIfNeeded()
  await trigger.click()
  const menu = page.locator('[data-slot="popover-content"]:visible').first()
  await menu.waitFor()
  const hasReporter = await menu.getByRole("button", { name: "Reporter…" }).isVisible().catch(() => false)
  const hasRetard = await menu.getByRole("radiogroup", { name: "Retard" }).isVisible().catch(() => false)
  return { menu, hasReporter, hasRetard }
}

await scenario("A7", async () => {
  await page.setViewportSize({ width: 1440, height: 900 })
  const enCours = await menuFor("R13", R13_DAY)
  await shot("A7-menu-en-cours")
  await page.keyboard.press("Escape")
  const termine = await menuFor("R14", PAST)
  await page.keyboard.press("Escape")
  const planifie = await menuFor("R12", TOMORROW)
  await page.keyboard.press("Escape")
  enCours.hasReporter && enCours.hasRetard ? ok("A7-en-cours", "En cours: « Reporter… » + « Retard »") : bad("A7-en-cours", "menu", JSON.stringify(enCours))
  termine.hasRetard && !termine.hasReporter ? ok("A7-termine", "Terminé: « Retard », no « Reporter… »") : bad("A7-termine", "menu", JSON.stringify(termine))
  planifie.hasReporter && !planifie.hasRetard ? ok("A7-planifie", "Planifié: « Reporter… », no « Retard »") : bad("A7-planifie", "menu", JSON.stringify(planifie))
})

await scenario("A7-save", async () => {
  // The agenda ⋯ retard row saves at once, with no version.
  const { menu } = await menuFor("R13", R13_DAY)
  const req = page.waitForRequest((r) => r.method() === "PUT" && r.url().endsWith(`/appointments/${R.R13.id}`))
  await menu.getByRole("radio", { name: "20 min" }).click()
  const body = JSON.parse((await req).postData() ?? "{}")
  const a = await poll(async () => { const x = await getAppt(R.R13.id); return x.lateArrivalMinutes === 20 ? x : null })
  a && body.lateArrivalMinutes === 20 && !("version" in body) && Object.keys(body).length === 1
    ? ok("A7-save", "⋯ « 20 min » → PUT { lateArrivalMinutes: 20 } only, saved")
    : bad("A7-save", "agenda retard write", JSON.stringify({ body, late: a?.lateArrivalMinutes }))
})

// ================================================================ edit dialog
await scenario("C2", async () => {
  const dlg = await editDialog("R14")
  const tenChecked = await dlg.getByRole("radiogroup", { name: "Retard" }).getByRole("radio", { name: "10 min" }).getAttribute("aria-checked")
  await dlg.getByRole("button", { name: "45 min", exact: true }).click()
  const body = await submitEdit(dlg, "R14")
  const a = await poll(async () => { const x = await getAppt(R.R14.id); return x.durationMinutes === 45 || /00:45/.test(String(x.duration)) ? x : null })
  !("lateArrivalMinutes" in body) && a && a.lateArrivalMinutes === 10 && tenChecked === "true"
    ? ok("C2", "an ordinary save sends no retard key; retard 10 kept")
    : bad("C2", "tri-state", JSON.stringify({ keys: Object.keys(body), late: a?.lateArrivalMinutes, tenChecked, duration: a?.duration ?? a?.durationMinutes }))
})

await scenario("A9", async () => {
  let dlg = await editDialog("R2")
  const fifteen = await dlg.getByRole("radiogroup", { name: "Retard" }).getByRole("radio", { name: "15 min" }).getAttribute("aria-checked")
  await dlg.getByRole("radiogroup", { name: "Retard" }).getByRole("radio", { name: "20 min" }).click()
  const b1 = await submitEdit(dlg, "R2")
  const a1 = await poll(async () => { const x = await getAppt(R.R2.id); return x.lateArrivalMinutes === 20 ? x : null })
  dlg = await editDialog("R2")
  await dlg.getByRole("radiogroup", { name: "Retard" }).getByRole("radio", { name: "Aucun" }).click()
  const b2 = await submitEdit(dlg, "R2")
  const a2 = await poll(async () => { const x = await getAppt(R.R2.id); return x.lateArrivalMinutes == null ? x : null })
  await api("PUT", `/appointments/${R.R2.id}`, { lateArrivalMinutes: 15 })
  dlg = await editDialog("R2")
  await dlg.getByRole("radiogroup", { name: "Statut du rendez-vous" }).getByRole("radio", { name: "Absent" }).click()
  const chipsGone = (await dlg.getByRole("radiogroup", { name: "Retard" }).count()) === 0
  await shot("A9-absent-no-chips")
  const b3 = await submitEdit(dlg, "R2")
  const a3 = await poll(async () => { const x = await getAppt(R.R2.id); return x.status === "NoShow" ? x : null })
  fifteen === "true" && a1 && b1.lateArrivalMinutes === 20 ? ok("A9-change", "15 shown, 20 saved") : bad("A9-change", "change", JSON.stringify({ fifteen, b1, late: a1?.lateArrivalMinutes }))
  a2 && b2.lateArrivalMinutes === null ? ok("A9-aucun", "« Aucun » sent null, cleared") : bad("A9-aucun", "clear", JSON.stringify({ b2 }))
  chipsGone && a3 && a3.lateArrivalMinutes == null && !("lateArrivalMinutes" in b3)
    ? ok("A9-absent", "Absent hides the chips; save → NoShow, retard erased")
    : bad("A9-absent", "absent", JSON.stringify({ chipsGone, status: a3?.status, late: a3?.lateArrivalMinutes, keys: Object.keys(b3) }))
})

await scenario("A10", async () => {
  await put("R6", { status: "NoShow" })
  let dlg = await editDialog("R6")
  await dlg.getByRole("radiogroup", { name: "Statut du rendez-vous" }).getByRole("radio", { name: "Terminé" }).click()
  await submitEdit(dlg, "R6")
  const a6 = await poll(async () => { const x = await getAppt(R.R6.id); return x.status === "Completed" ? x : null })
  a6 ? ok("A10-free", "Absent → Terminé on a free slot") : bad("A10-free", "R6 did not become Terminé")
  await put("R7", { status: "NoShow" })
  await book("R7b", at(PAST, "14:30"), 30)
  dlg = await editDialog("R7")
  await dlg.getByRole("radiogroup", { name: "Statut du rendez-vous" }).getByRole("radio", { name: "Terminé" }).click()
  await submitEdit(dlg, "R7")
  const confirm = page.locator('[role="alertdialog"]').filter({ hasText: "créneau déjà pris" })
  const shown = await confirm.waitFor({ timeout: 10000 }).then(() => true).catch(() => false)
  const sentence = shown ? (await confirm.innerText()).replace(/\s+/g, " ").slice(0, 160) : null
  await shot("A10-slot-taken")
  if (shown) await confirm.getByRole("button", { name: "Continuer" }).click()
  const a7 = await poll(async () => { const x = await getAppt(R.R7.id); return x.status === "Completed" ? x : null })
  shown && a7 ? ok("A10-taken", `clash named (« ${sentence} ») → Continuer → Terminé`) : bad("A10-taken", "taken slot", JSON.stringify({ shown, status: a7?.status }))
})

// ================================================================ « À clôturer »
await scenario("C1", async () => {
  await page.setViewportSize({ width: 320, height: 640 })
  await go(`${WEB}/a-cloturer`)
  const more = page.locator(`button[aria-label="Noter un retard pour ${R.R4.name}"]:visible`).first()
  await more.waitFor({ timeout: 25000 })
  await more.scrollIntoViewIfNeeded()
  await more.click()
  const pop = page.locator('[data-slot="popover-content"]:visible').first()
  await pop.waitFor()
  const pb = await pop.boundingBox()
  const sw1 = await page.evaluate(() => document.scrollingElement.scrollWidth)
  await shot("C1-popover-320")
  // Since 2026-10-30 « Reporter » is a row button beside Venu · Absent; the ⋯ holds only the retard.
  const row = await page.locator(`button[aria-label="Reporter la séance de ${R.R4.name}"]:visible`).first().evaluate((el) => {
    const r = el.getBoundingClientRect(); return { l: Math.round(r.left), r: Math.round(r.right), h: Math.round(r.height) }
  })
  await page.keyboard.press("Escape")
  await page.locator(`button[aria-label="Reporter la séance de ${R.R4.name}"]:visible`).first().click()
  const dlg = page.locator('[role="dialog"]').filter({ hasText: "Reporter la séance" })
  await dlg.getByText("Jour", { exact: true }).waitFor({ timeout: 15000 })
  const db = await dlg.boundingBox()
  const btns = await dlg.locator("button:visible").evaluateAll((els) => els.map((e) => { const r = e.getBoundingClientRect(); return { t: e.innerText.trim().slice(0, 30), l: r.left, r: r.right } }))
  const outside = btns.filter((b) => b.l < -1 || b.r > 321)
  const sw2 = await page.evaluate(() => document.scrollingElement.scrollWidth)
  await shot("C1-reporter-320")
  await page.keyboard.press("Escape")
  pb.x >= 0 && pb.x + pb.width <= 320 && sw1 <= 320 ? ok("C1-popover", `⋯ popover ${Math.round(pb.width)}px inside 320, scrollWidth ${sw1}`) : bad("C1-popover", "overflow", JSON.stringify({ pb, sw1 }))
  row.l >= 0 && row.r <= 320 ? ok("C1-button", `« Reporter » row button inside 320 (${row.l}–${row.r}, ${row.h}px tall)`) : bad("C1-button", "row button outside 320", JSON.stringify(row))
  db.x >= -1 && db.x + db.width <= 321 && outside.length === 0 && sw2 <= 320
    ? ok("C1-dialog", `Reporter sheet ${Math.round(db.width)}px, every button inside, scrollWidth ${sw2}`)
    : bad("C1-dialog", "overflow", JSON.stringify({ db, outside, sw2 }))
})

await scenario("A8", async () => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await go(`${WEB}/a-cloturer`)
  const more = page.locator(`button[aria-label="Noter un retard pour ${R.R4.name}"]:visible`).first()
  await more.waitFor({ timeout: 25000 })
  await more.click()
  const pop = page.locator('[data-slot="popover-content"]:visible').first()
  const req = page.waitForRequest((r) => r.method() === "PUT" && r.url().endsWith(`/appointments/${R.R4.id}`))
  await pop.getByRole("radiogroup", { name: `Retard de ${R.R4.name}` }).getByRole("radio", { name: "10 min" }).click()
  const body = JSON.parse((await req).postData() ?? "{}")
  const a = await poll(async () => { const x = await getAppt(R.R4.id); return x.status === "Completed" ? x : null })
  const toClose = (await api("GET", "/appointments/to-close?days=7&pageSize=100")).json
  const row = (toClose?.visits?.items ?? []).find((v) => v.appointmentId === R.R4.id)
  a && a.lateArrivalMinutes === 10 && body.status === "Completed" && body.lateArrivalMinutes === 10 && row?.nextStep !== "Presence"
    ? ok("A8", `one PUT { status: Completed, lateArrivalMinutes: 10 }; row now asks « ${row?.nextStep ?? "—"} »`)
    : bad("A8", "Venu en retard", JSON.stringify({ body, status: a?.status, late: a?.lateArrivalMinutes, nextStep: row?.nextStep }))
})

await scenario("B7", async () => {
  await go(`${WEB}/a-cloturer`)
  const reporter = page.locator(`button[aria-label="Reporter la séance de ${R.R9.name}"]:visible`).first()
  await reporter.waitFor({ timeout: 25000 })
  await reporter.click()
  const dlg = page.locator('[role="dialog"]').filter({ hasText: "Reporter la séance" })
  await dlg.getByText("Jour", { exact: true }).waitFor({ timeout: 15000 })
  const before = await dlg.locator('[id^="radix-"][id$="-description"], p').first().innerText().catch(() => "")
  // A colleague moves the visit while the form is open.
  const moved = await api("PUT", `/appointments/${R.R9.id}`, { appointmentDateTime: iso(at(PAST, "15:30")), allowOverlap: true, allowOutsideWorkingHours: true })
  await pickDemain(dlg)
  await chooseTime(dlg, "14:00")
  await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
  const reload = dlg.getByRole("button", { name: "Recharger" })
  const banner = await reload.waitFor({ timeout: 10000 }).then(() => true).catch(() => false)
  const bannerText = banner ? (await dlg.innerText()).match(/[^\n]*modifi[^\n]*/i)?.[0] ?? "" : ""
  const bannerSeen = banner ? await onScreen(reload) : false
  bannerSeen ? ok("B7-seen", "« Recharger » is on screen beside the buttons") : bad("B7-seen", "« Recharger » is not on screen")
  await shot("B7-conflict")
  const stillDemain = await dlg.getByRole("radio", { name: /^Demain/ }).getAttribute("aria-checked")
  if (banner) await reload.click()
  const subject = await poll(async () => { const t = await dlg.innerText(); return /15:30/.test(t) ? t : null }, { timeout: 8000 })
  const kept = await dlg.locator('input[id^="postpone-time-"]').inputValue()
  await dlg.getByRole("button", { name: "Reporter", exact: true }).click()
  const t = await toastText(/Séance reportée/)
  const a = await getAppt(R.R9.id)
  banner && subject && kept === "14:00" && stillDemain === "true" && t && sameInstant(a.appointmentDateTime, at(TOMORROW, "14:00"))
    ? ok("B7", `colleague's move → banner « ${bannerText.slice(0, 70)} » + Recharger → subject 15:30, typed 14:00 kept → moved`)
    : bad("B7", "409 path", JSON.stringify({ apiMove: moved.status, before: before.slice(0, 60), banner, bannerText, subject: !!subject, kept, stillDemain, toast: t, at: a.appointmentDateTime }))
})

await scenario("D3", async () => {
  const dlg = await editDialog("R12", TOMORROW)
  const input = dlg.locator("#edit-appt-start-time")
  await input.click()
  await input.fill("09:00")
  await input.press("Tab")
  await submitEdit(dlg, "R12")
  const confirm = page.locator('[role="alertdialog"]').filter({ hasText: "créneau déjà pris" })
  const shown = await confirm.waitFor({ timeout: 10000 }).then(() => true).catch(() => false)
  if (shown) await confirm.getByRole("button", { name: "Continuer" }).click()
  const a = await poll(async () => { const x = await getAppt(R.R12.id); return sameInstant(x.appointmentDateTime, at(TOMORROW, "09:00")) ? x : null })
  shown && a ? ok("D3", "edit dialog: taken slot → existing confirmation → saved") : bad("D3", "overlap confirmation", JSON.stringify({ shown, at: a?.appointmentDateTime }))
})

// ================================================================ API rows
await scenario("D1", async () => {
  const refused = await api("PUT", `/appointments/${R.R10.id}`, { status: "Scheduled" })
  const forced = await api("PUT", `/appointments/${R.R10.id}`, { status: "Scheduled", allowOverlap: true })
  const code = refused.json?.code
  refused.status === 400 && code === "slot_taken" && forced.status === 200
    ? ok("D1", `reactivation into a taken slot → 400 slot_taken (« ${String(refused.json?.error).slice(0, 60)} »); with override → 200`)
    : bad("D1", "reactivation", JSON.stringify({ s: refused.status, code, err: refused.json?.error, forced: forced.status }))
})

await scenario("B4", async () => {
  const x = await book("RB4", at(PAST, "07:00"), 30)
  await poll(async () => (await getAppt(x.id)).status === "AwaitingClosure", { timeout: 90000, every: 3000 })
  const r = await api("PUT", `/appointments/${x.id}`, { status: "NoShow", lateArrivalMinutes: 15 })
  const a = await getAppt(x.id)
  r.status === 400 && a.lateArrivalMinutes == null && a.status !== "NoShow"
    ? ok("B4", `{ NoShow, 15 } refused 400 « ${String(r.json?.error).slice(0, 70)} »`)
    : bad("B4", "contradiction", JSON.stringify({ s: r.status, err: r.json?.error, status: a.status, late: a.lateArrivalMinutes }))
})

await scenario("B5", async () => {
  const sched = await api("PUT", `/appointments/${R.R8.id}`, { lateArrivalMinutes: 10 })
  const big = await api("PUT", `/appointments/${R.R6.id}`, { lateArrivalMinutes: 241 })
  sched.status === 400 && big.status === 400
    ? ok("B5", `Planifié + retard → 400; 241 min → 400 (« ${String(big.json?.error).slice(0, 60)} »)`)
    : bad("B5", "range/statut", JSON.stringify({ sched: sched.status, big: big.status }))
})

await scenario("D4", async () => {
  const r = await api("GET", `/appointments/export?startDate=${PAST}&endDate=${TOMORROW}`)
  const lines = r.text.replace(/^\uFEFF/, "").split(/\r?\n/)
  const header = lines[0].split(";")
  const col = header.findIndex((h) => /Retard \(min\)/.test(h))
  const row = lines.find((l) => l.includes(`R14-${STAMP}`))
  const value = row ? row.split(";")[col] : undefined
  r.status === 200 && col >= 0 && value === "10"
    ? ok("D4", `CSV column « Retard (min) » at #${col}; R14 → ${value}`)
    : bad("D4", "csv", JSON.stringify({ s: r.status, header: header.slice(-6), col, value, row: row?.slice(0, 120) }))
})

// ================================================================ tier D, added after the blast-radius review
await scenario("D5", async () => {
  // The new ⋯ widened the presence row's « À faire » cell; a tablet is where the Actions column falls out first.
  const cdp = await ctx.newCDPSession(page)
  const measures = []
  for (const [w, h] of [[1024, 768], [1180, 820]]) {
    await cdp.send("Emulation.setDeviceMetricsOverride", { width: w, height: h, deviceScaleFactor: 1, mobile: false })
    await cdp.send("Emulation.setTouchEmulationEnabled", { enabled: true, maxTouchPoints: 5 })
    await cdp.send("Emulation.setEmitTouchEventsForMouse", { enabled: true })
    await go(`${WEB}/a-cloturer`)
    const more = page.locator(`button[aria-label="Noter un retard pour ${R.R11.name}"]:visible`).first()
    await more.waitFor({ timeout: 25000 })
    const m = await more.evaluate((el) => {
      const row = el.closest("tr")
      const scroller = el.closest('[data-slot="table-container"]') ?? row?.closest("table")?.parentElement
      const btns = [...row.querySelectorAll("button")].filter((b) => b.offsetParent).map((b) => b.getBoundingClientRect())
      const box = scroller.getBoundingClientRect()
      return {
        coarse: matchMedia("(pointer: coarse)").matches,
        right: Math.round(Math.max(...btns.map((b) => b.right))),
        boxRight: Math.round(box.right),
        vw: innerWidth,
        sideScroll: scroller.scrollWidth - scroller.clientWidth,
        moreSize: Math.round(el.getBoundingClientRect().height),
      }
    })
    await shot(`D5-a-cloturer-${w}`)
    measures.push({ w, ...m })
  }
  await cdp.send("Emulation.clearDeviceMetricsOverride")
  await cdp.send("Emulation.setTouchEmulationEnabled", { enabled: false })
  await cdp.send("Emulation.setEmitTouchEventsForMouse", { enabled: false })
  const fits = measures.every((m) => m.right <= m.boxRight + 1 && m.right <= m.vw && m.sideScroll <= 1)
  fits ? ok("D5", measures.map((m) => `${m.w}: actions end ${m.right} ≤ box ${m.boxRight}, ⋯ ${m.moreSize}px`).join(" · "))
    : bad("D5", "actions outside the table box", JSON.stringify(measures))
})

await scenario("D6", async () => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await book("RX1", at(PAST, "19:00"), 60)
  await book("RX2", at(PAST, "20:00"), 60)
  await poll(async () => (await getAppt(R.RX2.id)).status === "AwaitingClosure", { timeout: 90000, every: 3000 })
  await put("RX1", { status: "NoShow" })
  await put("RX2", { status: "NoShow" })
  await book("RX3", at(PAST, "20:00"), 60)
  const free = await menuFor("RX1", PAST)
  const offered = await free.menu.getByRole("button", { name: "Terminé", exact: true }).isVisible().catch(() => false)
  if (offered) await free.menu.getByRole("button", { name: "Terminé", exact: true }).click()
  const a1 = await poll(async () => { const x = await getAppt(R.RX1.id); return x.status === "Completed" ? x : null })
  await page.keyboard.press("Escape").catch(() => {})
  const taken = await menuFor("RX2", PAST)
  await taken.menu.getByRole("button", { name: "Terminé", exact: true }).click()
  const t = await toastText(/déjà réservé/)
  await shot("D6-taken-toast")
  await page.keyboard.press("Escape").catch(() => {})
  const a2 = await getAppt(R.RX2.id)
  offered && a1 ? ok("D6-free", "agenda ⋯ offers « Terminé » on an Absent block; free slot → Terminé") : bad("D6-free", "free slot", JSON.stringify({ offered, status: a1?.status }))
  t && a2.status === "NoShow" ? ok("D6-taken", `slot given away → toast « ${t.replace(/\s+/g, " ").slice(0, 80)} », statut stays Absent`) : bad("D6-taken", "taken slot", JSON.stringify({ toast: t, status: a2.status }))
})

await scenario("D7", async () => {
  await book("RY", at(PAST, "21:00"), 60)
  await poll(async () => (await getAppt(R.RY.id)).status === "AwaitingClosure", { timeout: 90000, every: 3000 })
  await put("RY", { lateArrivalMinutes: 15 })
  const dlg = await editDialog("RY")
  await dlg.getByRole("radiogroup", { name: "Retard" }).getByRole("radio", { name: "20 min" }).click()
  // A colleague records 30 while this form is open.
  await put("RY", { lateArrivalMinutes: 30 })
  await submitEdit(dlg, "RY")
  const reload = dlg.getByRole("button", { name: "Recharger" })
  const conflict = await reload.waitFor({ timeout: 10000 }).then(() => true).catch(() => false)
  if (conflict) await reload.click()
  const named = await poll(async () => { const t = await dlg.innerText(); return /Modifié entre-temps[^.]*Statut/.test(t) ? t : null }, { timeout: 8000 })
  const thirty = await dlg.getByRole("radiogroup", { name: "Retard" }).getByRole("radio", { name: "30 min" }).getAttribute("aria-checked")
  await shot("D7-recharger-merge")
  const body = await submitEdit(dlg, "RY")
  const closed = await poll(async () => !(await dlg.isVisible()), { timeout: 10000 })
  const a = await getAppt(R.RY.id)
  conflict && thirty === "true" && closed && a.lateArrivalMinutes === 30
    ? ok("D7", `409 → Recharger → 30 shown (colleague's), section ${named ? "named" : "NOT named"}; save lands (sent ${"lateArrivalMinutes" in body ? body.lateArrivalMinutes : "no retard key"})`)
    : bad("D7", "reconcile", JSON.stringify({ conflict, thirty, named: !!named, closed, late: a.lateArrivalMinutes, body }))
  if (conflict && !named) bad("D7-named", "the « Statut » section was taken over silently", "")
})

// ================================================================ A5 — the retard pushes the end of the séance
await scenario("A5", async () => {
  await a5Check1
  await a5Check2
  const c1 = a5.check1, c2 = a5.check2
  c1 && c1.status === "InProgress" && !c1.listed && !c1.pending
    ? ok("A5-held", `at booked end +70 s (${c1.at}): still En cours, not on « À clôturer », no prompt`)
    : bad("A5-held", "pushed end", JSON.stringify(c1))
  c2 && c2.status === "AwaitingClosure" && c2.pending
    ? ok("A5-due", `at booked end + retard +75 s (${c2.at}): Séance passée, prompt due`)
    : bad("A5-due", "after the retard", JSON.stringify(c2))
  await page.setViewportSize({ width: 1440, height: 900 })
  const dlg = await openPopupFor("R5")
  const fiche = await dlg.getByRole("button", { name: "Remplir la fiche de soins" }).isVisible()
  const venu = await dlg.getByRole("button", { name: "Venu", exact: true }).count()
  const pill = await dlg.getByText("Retard de 5 min").isVisible()
  await shot("A5-popup-fiche-step")
  fiche && venu === 0 && pill ? ok("A5-popup", "prompt opens on the fiche step with « Retard de 5 min »") : bad("A5-popup", "step", JSON.stringify({ fiche, venu, pill }))
})

// ================================================================ A11 — tablet toast, phone silence
await scenario("A11", async () => {
  const cdp = await ctx.newCDPSession(page)
  await cdp.send("Emulation.setDeviceMetricsOverride", { width: 1180, height: 820, deviceScaleFactor: 1, mobile: true })
  await cdp.send("Emulation.setTouchEmulationEnabled", { enabled: true, maxTouchPoints: 5 })
  popupTarget = new Set([R.RT.id])
  await go(`${WEB}/appointments?date=${PAST}&view=day`)
  await page.evaluate(() => { try { localStorage.removeItem("clinic:pvr-snooze") } catch { /* none */ } })
  await reload()
  const coarse = await page.evaluate(() => matchMedia("(pointer: coarse)").matches)
  const toast = page.locator("[data-sonner-toast]").filter({ hasText: "Séance terminée" })
  const shown = await toast.waitFor({ timeout: 25000 }).then(() => true).catch(() => false)
  const sizes = {}
  if (shown) for (const n of ["Venu", "Absent", "Reporter"]) sizes[n] = Math.round((await toast.getByRole("button", { name: n, exact: true }).boundingBox())?.height ?? 0)
  await shot("A11-toast-1180-coarse")
  if (shown) await toast.getByRole("button", { name: "Reporter", exact: true }).click()
  const dlg = page.locator('[role="dialog"]').filter({ hasText: "Reporter la séance" })
  const opened = await dlg.getByText("Jour", { exact: true }).waitFor({ timeout: 15000 }).then(() => true).catch(() => false)
  // The free-slot list on a finger: day chips and time buttons grow to 44 px (grown, not overlaid).
  if (opened) await dlg.getByRole("radiogroup", { name: /^Heures libres/ }).first().waitFor({ timeout: 10000 }).catch(() => {})
  const touch = opened ? await dlg.evaluate((el) => {
    const h = (sel) => [...el.querySelectorAll(sel)].filter((b) => b.offsetParent).map((b) => Math.round(b.getBoundingClientRect().height))
    return { chips: h('[role="radiogroup"][aria-label="Jour"] [role="radio"]'), times: h('[role="radiogroup"][aria-label^="Heures libres"] [role="radio"]') }
  }) : null
  await shot("A11-reporter-dialog-coarse")
  await page.keyboard.press("Escape")
  coarse && shown && Object.values(sizes).every((h) => h >= 44) && opened
    ? ok("A11", `coarse toast: Venu ${sizes.Venu} · Absent ${sizes.Absent} · Reporter ${sizes.Reporter} px; Reporter → dialog`)
    : bad("A11", "tablet", JSON.stringify({ coarse, shown, sizes, opened }))
  touch && touch.chips.length > 0 && touch.times.length > 0 && [...touch.chips, ...touch.times].every((h) => h >= 44)
    ? ok("A11-slots", `coarse: ${touch.chips.length} day chips ≥ ${Math.min(...touch.chips)} px, ${touch.times.length} times ≥ ${Math.min(...touch.times)} px`)
    : bad("A11-slots", "touch sizes", JSON.stringify(touch))
  await cdp.send("Emulation.setDeviceMetricsOverride", { width: 390, height: 844, deviceScaleFactor: 3, mobile: true })
  await page.evaluate(() => { try { localStorage.removeItem("clinic:pvr-snooze") } catch { /* none */ } })
  await reload()
  await sleep(7000)
  const phoneToast = await toast.count()
  const phoneDialog = await popup().count()
  phoneToast === 0 && phoneDialog === 0 ? ok("A11-phone", "phone: no toast, no dialog") : bad("A11-phone", "phone prompted", JSON.stringify({ phoneToast, phoneDialog }))
  await cdp.send("Emulation.clearDeviceMetricsOverride")
  await cdp.send("Emulation.setTouchEmulationEnabled", { enabled: false })
})

// ================================================================ report
await browser.close()
const failed = results.filter((r) => r.outcome === "fail")
const skipped = results.filter((r) => r.outcome === "skip")
console.log(`\n${results.length} checks · ${results.length - failed.length - skipped.length} pass · ${failed.length} fail · ${skipped.length} not exercised`)
fs.writeFileSync(path.join(HERE, `run-${RUN}-results.json`), JSON.stringify({ today: TODAY, stamp: STAMP, records: R, a5, envNotes, results }, null, 2))
envNotes.forEach((n) => console.log("  env:", n))
if (failed.length) failed.forEach((f) => console.log(" -", f.id, f.m, f.d ?? "")); else console.log("ALL CLEAR")

// Leave the stack as found for whoever tests by hand on it: our own « Test R…-NNNN » visits out of the prompt
// queue, « À clôturer » and the agenda (cancelled — there is no delete).
{
  const from = tunisDay(new Date(Date.now() - 8 * 86400000))
  const to = tunisDay(new Date(Date.now() + 15 * 86400000))
  const list = (await api("GET", `/appointments?startDate=${from}&endDate=${to}`)).json ?? []
  const mine = list.filter((a) => /^Test R\w+-\d{4}$/.test(a.patientName ?? "") && !["Cancelled", "NoShow"].includes(a.status))
  for (const a of mine) await api("PUT", `/appointments/${a.id}`, { status: "Cancelled", cancellationReason: "QA — fin du passage", allowOverlap: true })
  console.log(`  cleanup: cancelled ${mine.length} test visit(s)`)
}
