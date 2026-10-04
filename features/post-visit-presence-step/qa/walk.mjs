// QA walk — post-visit popup asks « venu ? » first. Plan: ./plan.md. Re-run this same file for run 2+.
// Usage: node walk.mjs <scratchpad-with-playwright-core> <shots-dir>
import crypto from 'node:crypto'
import { execSync } from 'node:child_process'
import { mkdirSync } from 'node:fs'
import { pathToFileURL } from 'node:url'

const [, , SCRATCH, SHOTS] = process.argv
mkdirSync(SHOTS, { recursive: true })
const pw = await import(pathToFileURL(`${SCRATCH}/node_modules/playwright-core/index.js`).href)
const chromium = pw.chromium ?? pw.default?.chromium

const WEB = 'http://localhost:3000'
const API = 'http://localhost:5000/api'
const EMAIL = 'salma.benyoussef@cabinet-ibnkhaldoun.tn'
const PASSWORD = 'QaAudit2026!y'
const TOTP_SECRET = '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'
const ROUTE = '/waiting-list'
const STATUS = { Scheduled: 1, Completed: 4, Cancelled: 5, NoShow: 6, AwaitingClosure: 7 }

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }

function totp() {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of TOTP_SECRET) bits += A.indexOf(c).toString(2).padStart(5, '0')
  const key = Buffer.from(bits.match(/.{8}/g).map((b) => parseInt(b, 2)))
  const counter = Math.floor(Date.now() / 1000 / 30)
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0); buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac('sha1', key).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, '0')
}
const sql = (q) => execSync(`docker exec clinic-postgres psql -U clinic_user -d clinic_management -At -c "${q.replace(/"/g, '\\"')}"`).toString().trim()
const statusOf = (id) => Number(sql(`select "Status" from "Appointments" where "Id"='${id}'`))

// ── Browser + probe controls ────────────────────────────────────────────────────────────────────────────
const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
let target = null            // the one appointment the popup is about in the current scenario
let pendingCalls = 0
await ctx.route('**/api/notifications/pending-reviews**', async (route) => {
  pendingCalls++
  const resp = await route.fetch()
  let list = []
  try { list = await resp.json() } catch { /* keep empty */ }
  await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(target ? list.filter((r) => r.appointmentId === target) : []) })
})
let token = null
const writes = []
function watch(page) {
  page.on('request', (req) => {
    const a = req.headers()['authorization']
    if (a && req.url().startsWith('http://localhost:5000')) token = a
    if (req.url().startsWith('http://localhost:5000') && req.method() !== 'GET' && req.method() !== 'OPTIONS') writes.push(`${req.method()} ${req.url()}`)
  })
  page.on('dialog', (d) => d.accept().catch(() => {}))
}
async function api(method, path, body) {
  const r = await fetch(`${API}${path}`, { method, headers: { Authorization: token, 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined })
  const text = await r.text()
  let json = null
  try { json = JSON.parse(text) } catch { /* not json */ }
  return { status: r.status, json, text }
}

const page = await ctx.newPage()
watch(page)

// ── Sign in inside the take ─────────────────────────────────────────────────────────────────────────────
await page.goto(`${WEB}${ROUTE}`)
await page.waitForURL(/\/login/, { timeout: 60_000 })
await page.fill('input[type=email]', EMAIL)
await page.fill('input[type=password]', PASSWORD)
await page.click('button:has-text("Se connecter")')
await page.waitForSelector('input[inputmode=numeric]', { timeout: 30_000 })
await page.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
await page.fill('input[inputmode=numeric]', totp())
await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60_000 })
await page.waitForLoadState('networkidle').catch(() => {})
for (let i = 0; i < 30 && !token; i++) await page.waitForTimeout(500)
if (!token) { console.log('NO TOKEN CAPTURED — abort'); await browser.close(); process.exit(1) }
console.log('signed in, token captured')

// ── Arrange: one patient, five ended slots today ────────────────────────────────────────────────────────
const stamp = Date.now().toString(36)
const pat = await api('POST', '/patients', { firstName: 'QA', lastName: `Présence ${stamp}`, gender: 'Male', allowDuplicate: true })
if (pat.status >= 300) { console.log('patient create failed', pat.status, pat.text); await browser.close(); process.exit(1) }
const patientId = pat.json.id
const lastName = pat.json.lastName
const today = new Date(); today.setHours(9, 0, 0, 0)
const ap = {}
for (const [i, key] of ['ap1', 'ap2', 'ap3', 'ap4', 'ap5'].entries()) {
  const at = new Date(today.getTime() + i * 30 * 60_000)
  const r = await api('POST', '/appointments', { patientId, appointmentDateTime: at.toISOString(), durationMinutes: 30, allowOutsideWorkingHours: true, allowOverlap: true })
  if (r.status >= 300) { console.log(`${key} create failed`, r.status, r.text); await browser.close(); process.exit(1) }
  ap[key] = r.json.id
}
const ap4Done = await api('PUT', `/appointments/${ap.ap4}`, { status: 'Completed' })
console.log('arranged', { patientId, ...ap, ap4Completed: ap4Done.status })
await page.waitForTimeout(1500)
const realPending = (await api('GET', '/notifications/pending-reviews')).json ?? []
for (const [k, id] of Object.entries(ap)) {
  if (!realPending.some((r) => r.appointmentId === id)) console.log(`  ⚠ ${k} has no pending review yet`)
}

// ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────
const dialog = () => page.locator('[role=dialog]:visible').filter({ hasText: 'Séance terminée' })
async function openFor(p, id, route = ROUTE) {
  target = id
  await p.evaluate(() => { try { localStorage.removeItem('clinic:pvr-snooze') } catch { /* */ } })
  await p.goto(`${WEB}${route}`)
}
async function footerButtons(d) {
  return (await d.locator('[data-slot="dialog-footer"] button, button').allInnerTexts()).map((t) => t.trim()).filter((t) => t && !/^(Close|Fermer)$/i.test(t))
}
async function snoozeIds(p) {
  return p.evaluate(() => { try { return Object.keys(JSON.parse(localStorage.getItem('clinic:pvr-snooze') || '{}')) } catch { return [] } })
}
const reviewIdFor = (id) => realPending.find((r) => r.appointmentId === id)?.id

const SCENARIOS = [
  { id: 'A1', run: async () => {
    await openFor(page, ap.ap1)
    const d = dialog()
    await d.waitFor({ timeout: 30_000 })
    const text = await d.innerText()
    const btns = await footerButtons(d)
    console.log('     text:', JSON.stringify(text), 'buttons:', btns)
    text.includes('Le patient est-il venu ?') && text.includes(lastName) ? ok('A1', 'question names the patient') : bad('A1', 'sentence', text)
    ;['Plus tard', 'Absent', 'Venu'].every((b) => btns.includes(b)) && !btns.some((b) => /Remplir/.test(b))
      ? ok('A1', 'step 1 buttons') : bad('A1', 'step 1 buttons', JSON.stringify(btns))
    await page.screenshot({ path: `${SHOTS}/A1-step1-1440.png` })
  } },
  { id: 'A2', run: async () => {
    const d = dialog()
    const before = writes.length
    await d.getByRole('button', { name: /^Venu$/ }).click()
    await d.getByRole('button', { name: 'Remplir la fiche de soins' }).waitFor({ timeout: 10_000 })
    const text = await d.innerText()
    const btns = await footerButtons(d)
    console.log('     text:', JSON.stringify(text), 'buttons:', btns)
    !text.includes('Le patient est-il venu ?') && btns.includes('Plus tard') && !btns.includes('Venu') && !btns.includes('Absent')
      ? ok('A2', 'step 2 shown') : bad('A2', 'step 2 content', JSON.stringify({ text, btns }))
    writes.length === before ? ok('A2', 'no write on « Venu »') : bad('A2', 'a write fired on « Venu »', writes.slice(before).join(', '))
    await page.screenshot({ path: `${SHOTS}/A2-step2-1440.png` })
  } },
  { id: 'A3', run: async () => {
    await dialog().getByRole('button', { name: 'Remplir la fiche de soins' }).click()
    await page.waitForURL(/\/patients\/.+addRecord=1/, { timeout: 20_000 })
    const u = new URL(page.url())
    u.pathname === `/patients/${patientId}` && u.searchParams.get('appointmentId') === ap.ap1
      ? ok('A3', `navigated to ${u.pathname}${u.search}`) : bad('A3', 'wrong destination', page.url())
  } },
  { id: 'A4+B5', run: async () => {
    await openFor(page, ap.ap2)
    const d = dialog()
    await d.waitFor({ timeout: 30_000 })
    const before = writes.length
    await d.getByRole('button', { name: /^Absent$/ }).dblclick()
    const toast = page.locator('[data-sonner-toast]').filter({ hasText: 'Patient marqué comme absent.' })
    await toast.waitFor({ timeout: 10_000 })
    ok('A4', 'success toast shown')
    await page.waitForTimeout(800)
    ;(await dialog().count()) === 0 ? ok('A4', 'dialog closed') : bad('A4', 'dialog still open')
    const puts = writes.slice(before).filter((w) => w.startsWith('PUT') && w.includes(ap.ap2))
    puts.length === 1 ? ok('B5', 'exactly one PUT on a double-click') : bad('B5', `${puts.length} PUTs`, puts.join(', '))
    const btnCount = await toast.locator('button[data-button]').count()
    btnCount === 0 ? ok('D3', 'success toast has no action buttons') : bad('D3', 'success toast carries buttons', String(btnCount))
    await page.screenshot({ path: `${SHOTS}/A4-absent-toast-1440.png` })
  } },
  { id: 'A5', run: async () => {
    const s = statusOf(ap.ap2)
    s === STATUS.NoShow ? ok('A5', 'ap2 is NoShow in the database') : bad('A5', 'ap2 status', String(s))
  } },
  { id: 'D2', run: async () => {
    const pend = (await api('GET', '/notifications/pending-reviews')).json ?? []
    !pend.some((r) => r.appointmentId === ap.ap2) ? ok('D2', 'review for ap2 gone from pending-reviews') : bad('D2', 'review for ap2 still pending')
    const tc = await api('GET', `/appointments/to-close?patientId=${patientId}&days=7`)
    const items = tc.json?.visits?.items ?? tc.json?.visits?.data ?? []
    const row = items.find((v) => v.appointmentId === ap.ap2)
    console.log('     to-close rows:', items.map((v) => `${v.appointmentId.slice(0, 8)}:${v.nextStep}`).join(' '), 'http', tc.status)
    tc.status === 200 && !row ? ok('D2', 'ap2 no longer in « À clôturer »') : bad('D2', 'ap2 still listed in « À clôturer »', JSON.stringify(row ?? tc.text.slice(0, 200)))
  } },
  { id: 'B1', run: async () => {
    await openFor(page, ap.ap3)
    const d = dialog()
    await d.waitFor({ timeout: 30_000 })
    const r = await api('PUT', `/appointments/${ap.ap3}`, { status: 'Completed' })
    console.log('     ap3 → Completed over the API:', r.status)
    await page.waitForTimeout(1500)
    await d.getByRole('button', { name: /^Absent$/ }).click()
    const err = page.locator('[data-sonner-toast][data-type=error]')
    await err.first().waitFor({ timeout: 10_000 })
    console.log('     error toast:', JSON.stringify(await err.first().innerText()))
    ok('B1', 'refusal shown as an error toast')
    await page.waitForTimeout(1500)
    const btns = (await dialog().count()) ? await footerButtons(dialog()) : []
    btns.includes('Venu') && btns.includes('Absent') ? ok('B1', 'dialog stayed open on step 1') : bad('B1', 'dialog after refusal', JSON.stringify(btns))
    const s = statusOf(ap.ap3)
    s === STATUS.Completed ? ok('B1', 'ap3 still Completed') : bad('B1', 'ap3 status', String(s))
    await page.screenshot({ path: `${SHOTS}/B1-refusal-1440.png` })
  } },
  { id: 'B2+D1', run: async () => {
    await openFor(page, ap.ap1)
    await dialog().waitFor({ timeout: 30_000 })
    await page.getByRole('button', { name: /^Plus tard$/ }).click()   // e2e/lib/goto.ts's exact locator
    await page.waitForTimeout(800)
    ;(await dialog().count()) === 0 ? ok('D1', '« Plus tard » found by name and closes step 1') : bad('D1', 'dialog still open')
    const ids = await snoozeIds(page)
    ids.includes(reviewIdFor(ap.ap1)) ? ok('B2', 'snooze written for the review') : bad('B2', 'snooze map', JSON.stringify(ids))
    await page.reload()
    await page.waitForTimeout(6000)
    ;(await dialog().count()) === 0 ? ok('B2', 'no prompt after reload') : bad('B2', 'prompt came back after reload')
  } },
  { id: 'B3', run: async () => {
    await openFor(page, ap.ap1)
    const d = dialog()
    await d.waitFor({ timeout: 30_000 })
    await d.getByRole('button', { name: /^Venu$/ }).click()
    await d.getByRole('button', { name: 'Remplir la fiche de soins' }).waitFor({ timeout: 10_000 })
    await page.keyboard.press('Escape')
    await page.waitForTimeout(800)
    ;(await dialog().count()) === 0 ? ok('B3', 'Escape closes step 2') : bad('B3', 'dialog still open after Escape')
    ;(await snoozeIds(page)).includes(reviewIdFor(ap.ap1)) ? ok('B3', 'snooze written') : bad('B3', 'no snooze after Escape')
  } },
  { id: 'B4', run: async () => {
    await openFor(page, ap.ap4)
    const d = dialog()
    await d.waitFor({ timeout: 30_000 })
    const text = await d.innerText()
    const btns = await footerButtons(d)
    console.log('     text:', JSON.stringify(text), 'buttons:', btns)
    btns.includes('Remplir la fiche de soins') && !btns.includes('Venu') && !text.includes('Le patient est-il venu ?')
      ? ok('B4', 'Completed visit opens on step 2') : bad('B4', 'Completed visit did not open on step 2', JSON.stringify({ text, btns }))
    await page.keyboard.press('Escape')
  } },
  { id: 'C1', run: async () => {
    await page.setViewportSize({ width: 1536, height: 730 })
    await openFor(page, ap.ap1)
    const d = dialog()
    await d.waitFor({ timeout: 30_000 })
    const boxes = []
    for (const name of [/^Plus tard$/, /^Absent$/, /^Venu$/]) boxes.push(await d.getByRole('button', { name }).boundingBox())
    console.log('     boxes:', JSON.stringify(boxes.map((b) => b && [Math.round(b.x), Math.round(b.y), Math.round(b.width), Math.round(b.height)])))
    boxes.every((b) => b && b.x >= 0 && b.y >= 0 && b.x + b.width <= 1536 && b.y + b.height <= 730)
      ? ok('C1', 'all three buttons inside 1536×730') : bad('C1', 'a button is outside the viewport')
    await page.screenshot({ path: `${SHOTS}/C1-1536x730.png` })
    await page.keyboard.press('Escape')
    await page.setViewportSize({ width: 1440, height: 900 })
  } },
  { id: 'C2+C3', run: async () => {
    const tab = await ctx.newPage()
    watch(tab)
    const cdp = await ctx.newCDPSession(tab)
    // Playwright owns the viewport (a raw CDP metrics override is reset by it); CDP only adds the touch pointer.
    await tab.setViewportSize({ width: 820, height: 1180 })
    await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 })
    await tab.goto(`${WEB}${ROUTE}`)
    await openFor(tab, ap.ap5)
    const env = await tab.evaluate(() => ({ coarse: matchMedia('(pointer: coarse)').matches, w: innerWidth }))
    env.coarse && env.w === 820 ? ok('C2', `coarse pointer at ${env.w}px`) : bad('C2', 'emulation not in force — the rest proves nothing', JSON.stringify(env))
    const t = tab.locator('[data-sonner-toast]').filter({ hasText: 'Séance terminée' })
    await t.waitFor({ timeout: 30_000 })
    ;(await tab.locator('[role=dialog]:visible').count()) === 0 ? ok('C2', 'no dialog on a tablet') : bad('C2', 'a dialog is open on a tablet')
    const venu = t.locator('button[data-action]')
    const absent = t.locator('button[data-cancel]')
    const vt = (await venu.innerText()).trim()
    const at = (await absent.innerText()).trim()
    const vb = await venu.boundingBox()
    const ab = await absent.boundingBox()
    console.log('     toast text:', JSON.stringify(await t.innerText()), 'action:', vt, vb?.height, 'cancel:', at, ab?.height)
    vt === 'Venu' && at === 'Absent' ? ok('C2', 'toast offers « Venu » and « Absent »') : bad('C2', 'toast buttons', `${vt} / ${at}`)
    vb?.height >= 44 && ab?.height >= 44 ? ok('C2', 'both ≥ 44 px tall') : bad('C2', 'touch height', `${vb?.height} / ${ab?.height}`)
    await tab.screenshot({ path: `${SHOTS}/C2-tablet-step1.png` })
    await venu.click()
    await t.locator('button[data-action]', { hasText: 'Remplir' }).waitFor({ timeout: 10_000 })
    ;(await t.locator('button[data-cancel]').count()) === 0 ? ok('C2', '« Venu » turned the toast into « Remplir », « Absent » gone') : bad('C2', '« Absent » still on step 2')
    await tab.screenshot({ path: `${SHOTS}/C2-tablet-step2.png` })
    await tab.reload()
    const t2 = tab.locator('[data-sonner-toast]').filter({ hasText: 'Séance terminée' })
    await t2.waitFor({ timeout: 30_000 })
    await t2.locator('button[data-cancel]').click()
    await tab.locator('[data-sonner-toast]').filter({ hasText: 'Patient marqué comme absent.' }).waitFor({ timeout: 10_000 })
    ok('C3', 'tablet « Absent » → success toast')
    await tab.waitForTimeout(800)
    const s = statusOf(ap.ap5)
    s === STATUS.NoShow ? ok('C3', 'ap5 is NoShow') : bad('C3', 'ap5 status', String(s))
    await tab.screenshot({ path: `${SHOTS}/C3-tablet-absent.png` })
    await tab.close()
  } },
  { id: 'C4', run: async () => {
    const phone = await ctx.newPage()
    watch(phone)
    const cdp = await ctx.newCDPSession(phone)
    await phone.setViewportSize({ width: 390, height: 844 })
    await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 })
    await phone.goto(`${WEB}${ROUTE}`)
    await openFor(phone, ap.ap1)
    const env = await phone.evaluate(() => ({ coarse: matchMedia('(pointer: coarse)').matches, w: innerWidth }))
    env.coarse && env.w === 390 ? ok('C4', `coarse pointer at ${env.w}px`) : bad('C4', 'emulation not in force — the rest proves nothing', JSON.stringify(env))
    const callsBefore = pendingCalls
    await phone.waitForTimeout(8000)
    const dlg = await phone.locator('[role=dialog]:visible').filter({ hasText: 'Séance terminée' }).count()
    const tst = await phone.locator('[data-sonner-toast]').filter({ hasText: 'Séance terminée' }).count()
    dlg === 0 && tst === 0 ? ok('C4', 'no prompt on a phone') : bad('C4', 'a prompt rendered on a phone', `dialog ${dlg} toast ${tst}`)
    console.log(`     pending-reviews calls during the phone load: ${pendingCalls - callsBefore} (observation)`)
    await phone.screenshot({ path: `${SHOTS}/C4-phone-390.png` })
    await phone.close()
  } },
]

for (const s of SCENARIOS) {
  console.log(`▶ ${s.id}`)
  try { await s.run() } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]); await page.screenshot({ path: `${SHOTS}/${s.id}-threw.png` }).catch(() => {}) }
}

// ── Revert my own test artefacts: cancel what is still open, so no peer's queue carries them ─────────────
target = null
for (const [k, id] of Object.entries(ap)) {
  const s = statusOf(id)
  if (s !== STATUS.NoShow && s !== STATUS.Cancelled) {
    const r = await api('PUT', `/appointments/${id}`, { status: 'Cancelled', cancellationReason: 'QA post-visit-presence-step' })
    console.log(`  cleanup ${k}: ${s} → Cancelled (${r.status})`)
  }
}
console.log('\nartefacts:', JSON.stringify({ patientId, ...ap }))
console.log(findings.length ? `\n${findings.length} FINDING(S):` : '\nALL CLEAR')
findings.forEach((f) => console.log(' ', JSON.stringify(f)))
await browser.close()
