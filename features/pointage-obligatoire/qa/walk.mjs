// QA walk — pointage obligatoire (part 3). One launch, every scenario of qa/plan.md, findings collected, nothing fixed.
// Chrome 9333 = Dr Salma (admin) + the borne tab + context BS (Ines signed in ON the borne device, cookie copied);
// Chrome 9334 = Ines on an ordinary device. Stack: API :5096 + web :3096 on DB clinic_pointage_qa (isolated).
// Re-runnable: it resets its own artefacts first.   node walk.mjs   (ONLY=A5,A6 re-drives rows without a reset)
import { createRequire } from 'node:module'
import { execFileSync, spawn } from 'node:child_process'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const SCRATCH = process.env.SCRATCH ?? 'C:/Users/OUMAYM~1/AppData/Local/Temp/claude/6ab72d215ac88a62/0bea09ef-3a73-43b2-a09f-b045eee34842/scratchpad'
const PW_DIR = process.env.PW_DIR ?? `${SCRATCH}/pw`
const { chromium } = createRequire(`${PW_DIR}/package.json`)('playwright-core')

const WEB = 'http://localhost:3096'
const API = 'http://localhost:5096/api'
const HANGFIRE = 'http://localhost:5096/hangfire/recurring'
const DB = process.env.QA_DB ?? 'clinic_pointage_qa'
// The two Chromes; another pair (QA_PORTS=9335,9336) leaves a demo's windows on 9333/9334 alone.
const [A_PORT, S_PORT] = (process.env.QA_PORTS ?? '9333,9334').split(',').map(Number)
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe'
const CLINIC = 'f64a8a75-94e1-4e08-a743-fa231abe438f'
const PASSWORD = 'QaAudit2026!y'
const ADMIN = { email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', totp: '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO', name: 'Dr Salma Ben Youssef', id: 'local|cdc1c7a5-9326-4892-a049-598cb07eea6b' }
const INES = { email: 'qa.secretary@ibnkhaldoun.test', id: 'local|22222222-2222-4222-8222-222222222222', name: 'Ines Gharbi' }
const SCHEMA = [0, 1, 2, 5] // keys 1-2-3-6
const SHOTS = path.join(HERE, 'shots')
fs.mkdirSync(SHOTS, { recursive: true })

// The cabinet's own clock (Tunisia, UTC+1, no DST) — the walk runs just after midnight on Friday 6 November.
const local = (d = new Date()) => new Date(d.getTime() + 3600_000)
const hhmm = (d) => d.toISOString().slice(11, 16)
const TODAY = local().toISOString().slice(0, 10)
const YDAY = local(new Date(Date.now() - 86400_000)).toISOString().slice(0, 10)
const WEEKDAY = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'][local().getUTCDay()]

// ---------------------------------------------------------------- findings
const findings = []
const results = []
const ok = (id, m) => { console.log(`  ✅ [${id}] ${m}`); results.push({ id, outcome: 'pass', m }) }
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }); results.push({ id, outcome: 'fail', m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); results.push({ id, outcome: 'skip', m: why }) }
const check = (id, cond, m, d = '') => (cond ? ok(id, m) : bad(id, m, d))

const ONLY = process.env.ONLY ? process.env.ONLY.split(',') : null
async function scenario(id, fn) {
  if (ONLY && !ONLY.includes(id)) return
  console.log(`\n— ${id}`)
  try { await fn() } catch (e) { bad(id, 'threw', String(e?.message ?? e).split('\n')[0]) }
}

// ---------------------------------------------------------------- helpers
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const psql = (sql) =>
  execFileSync('docker', ['exec', 'clinic-postgres', 'psql', '-U', 'clinic_user', '-d', DB, '-Atc', sql], { encoding: 'utf8' }).trim()

function totp(secret) {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of secret.toUpperCase()) bits += A.indexOf(c).toString(2).padStart(5, '0')
  const key = Buffer.from(bits.match(/.{8}/g).map((b) => parseInt(b, 2)))
  const counter = Math.floor(Date.now() / 30000)
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0)
  buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac('sha1', key).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, '0')
}
const nextWindow = () => sleep((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)

const bodyText = (page) => page.locator('body').innerText().catch(() => '')
async function waitText(page, re, timeout = 10000) {
  const end = Date.now() + timeout
  while (Date.now() < end) {
    if (re.test(await bodyText(page))) return true
    await sleep(250)
  }
  return false
}
async function toast(page, re, timeout = 8000) {
  const end = Date.now() + timeout
  while (Date.now() < end) {
    const all = (await page.locator('[data-sonner-toast]').allInnerTexts().catch(() => [])).join(' | ')
    if (re.test(all)) return all
    await sleep(150)
  }
  return null
}
const openDialog = (page) => page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible').last()
// A hidden tab draws no frame: a screenshot of it hangs 30 s, long enough for the borne's idle timer to fire (run 6).
const shot = async (page, name) => {
  await page.bringToFront().catch(() => {})
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`), timeout: 10000 }).catch(() => {})
}
const gate = (page) => page.locator('[data-clock-in-gate]')
const gateUp = (page, timeout = 10000) => gate(page).waitFor({ state: 'visible', timeout }).then(() => true, () => false)
const gateGone = (page, timeout = 10000) => gate(page).waitFor({ state: 'detached', timeout }).then(() => true, () => false)
// A marker on window: still there after a wait means the page was not reloaded.
const mark = (page) => page.evaluate(() => { window.__qaMark = Math.random(); return window.__qaMark })
const marked = (page, v) => page.evaluate((x) => window.__qaMark === x, v).catch(() => false)

async function chromeOn(port, profile, x, y, w, h) {
  const up = async () => { try { return (await fetch(`http://localhost:${port}/json/version`)).ok } catch { return false } }
  if (!(await up())) {
    spawn(CHROME, [
      `--remote-debugging-port=${port}`, `--user-data-dir=${SCRATCH}/${profile}`, '--no-first-run',
      '--no-default-browser-check', `--window-position=${x},${y}`, `--window-size=${w},${h}`, 'about:blank',
    ], { detached: true, stdio: 'ignore' }).unref()
    for (let i = 0; i < 40 && !(await up()); i++) await sleep(250)
  }
  const browser = await chromium.connectOverCDP(`http://localhost:${port}`)
  const context = browser.contexts()[0]
  await quiet(context)
  const page = context.pages().find((p) => !p.url().startsWith('devtools')) ?? (await context.newPage())
  return { browser, context, page }
}
const quiet = (context) =>
  context.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const guardFeedback = (page) =>
  page.addLocatorHandler(page.locator('[aria-labelledby="feedback-prompt-title"]'), async (c) => {
    await c.getByRole('button', { name: 'Plus tard' }).click()
  })

async function signIn(page, who, withTotp) {
  await page.goto(`${WEB}/settings`, { waitUntil: 'domcontentloaded' })
  await page.waitForTimeout(1500)
  if (!page.url().includes('/login')) return
  await page.fill('input[type=email]', who.email)
  await page.fill('input[type=password]', PASSWORD)
  await page.click("button:has-text('Se connecter')")
  if (withTotp) {
    await page.waitForSelector('input[inputmode=numeric]', { timeout: 20000 })
    await nextWindow()
    await page.fill('input[inputmode=numeric]', totp(who.totp))
  }
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60000 })
}

async function dragSchema(page, dots) {
  const pad = page.locator('svg[role="application"]:visible').last()
  await pad.waitFor({ timeout: 8000 })
  await pad.scrollIntoViewIfNeeded().catch(() => {})
  const box = await pad.boundingBox()
  const at = (d) => ({ x: box.x + ((50 + 100 * (d % 3)) / 300) * box.width, y: box.y + ((50 + 100 * Math.floor(d / 3)) / 300) * box.height })
  const first = at(dots[0])
  await page.mouse.move(first.x, first.y)
  await page.mouse.down()
  for (const d of dots.slice(1)) {
    const p = at(d)
    await page.mouse.move(p.x, p.y, { steps: 6 })
  }
  await page.mouse.up()
}
async function typeSchema(page, dots) {
  const pad = page.locator('svg[role="application"]:visible').last()
  await pad.waitFor({ timeout: 8000 })
  await pad.focus()
  for (const d of dots) await page.keyboard.press(String(d + 1))
  await page.keyboard.press('Enter')
}
const tile = (page, name) => page.getByRole('button', { name: new RegExp(name) })
async function pickPerson(page, dlg, name) {
  await dlg.locator('#record-person').click()
  await page.getByRole('option', { name }).click()
  await page.waitForFunction(() => document.activeElement?.id === 'record-person', null, { timeout: 3000 }).catch(() => {})
}
async function signOutVia(page) {
  await page.getByRole('button', { name: 'Mon compte' }).click()
  await page.getByRole('menuitem', { name: 'Se déconnecter' }).click()
}

// Friday's hours, every other day 09:00–17:00 (Sunday closed). `fri` = { enabled, from, to }.
function hoursJson(fri) {
  const days = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
  return JSON.stringify(days.map((d) => d === WEEKDAY
    ? { Day: d, Enabled: fri.enabled, From: fri.from, To: fri.to, BreakFrom: null, BreakTo: null }
    : { Day: d, Enabled: d !== 'Sunday', From: '09:00', To: '17:00', BreakFrom: null, BreakTo: null }))
}
const setHours = (fri) => psql(`update "Clinics" set "WorkingHoursJson" = '${hoursJson(fri)}' where "Id" = '${CLINIC}'`)
const OPEN_ALL_DAY = { enabled: true, from: '00:00', to: '23:59' }
const clearInesToday = () => psql(`delete from "AttendanceEntries" where "UserId"='${INES.id}' and "LocalDay"='${TODAY}'`)

// Ines over the wire — a session family of its own (the browser's is never touched).
let inesToken = null
async function api(method, url, body, token = inesToken) {
  const r = await fetch(`${API}${url}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  let json = null
  try { json = await r.json() } catch { /* empty */ }
  return { status: r.status, json }
}
const clockStatus = async () => (await api('GET', '/pointage/me/clock-status')).json ?? {}

// ================================================================ arrange (own DB, own artefacts)
console.log(`Today ${TODAY} (${WEEKDAY}), yesterday ${YDAY}, local ${hhmm(local())}`)
if (!ONLY) {
  console.log(`Arranging ${DB}…`)
  psql(`delete from "AttendanceEntries";
        delete from "AttendanceTerminals";
        delete from "StaffLeaveRequests";
        update "Users" set "AttendanceSchemaHash" = null, "AttendanceTrackedOverride" = null, "SchemaFailedAttempts" = 0, "SchemaLockedUntilUtc" = null where "ClinicId" = '${CLINIC}';
        update "Clinics" set "AttendanceEnabled" = true, "AttendanceLockEnabled" = true, "LeaveRequestsEnabled" = true where "Id" = '${CLINIC}';
        delete from "NotificationReads" where "NotificationId" in (select "Id" from "StaffNotifications" where "Category" between 20 and 24);
        delete from "NotificationDismissals" where "NotificationId" in (select "Id" from "StaffNotifications" where "Category" between 20 and 24);
        delete from "StaffNotifications" where "Category" between 20 and 24;`)
  setHours(OPEN_ALL_DAY)
}

const login = await fetch(`${API}/auth/login`, {
  method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: INES.email, password: PASSWORD }),
})
inesToken = (await login.json())?.value?.accessToken
console.log('Ines API token:', inesToken ? 'ok' : 'MISSING')

const A = await chromeOn(A_PORT, `chrome-admin-${A_PORT}`, 0, 0, 1000, 830)
const S = await chromeOn(S_PORT, `chrome-secr-${S_PORT}`, 990, 0, 600, 830)
const adm = A.page
const sec = S.page
if (!ONLY) await A.context.clearCookies({ name: 'apexa_borne' }).catch(() => {})
await guardFeedback(adm)
await guardFeedback(sec)
await signIn(adm, ADMIN, true)
await signIn(sec, INES, false)
await sec.setViewportSize({ width: 560, height: 760 })
console.log('Signed in — admin:', adm.url(), ' secretary:', sec.url())
const borne = A.context.pages().find((p) => p !== adm && p.url().includes('/pointage')) ?? (await A.context.newPage())
await borne.setViewportSize({ width: 820, height: 1180 })
const sheetUrl = `${WEB}/users?onglet=presences`
const inesLines = (where = '') => psql(`select count(*) from "AttendanceEntries" where "UserId"='${INES.id}' ${where}`)

async function bornePunch(id, expectWord) {
  await borne.bringToFront()
  await borne.goto(`${WEB}/pointage`)
  await waitText(borne, /Borne Accueil/, 15000)
  await tile(borne, INES.name).first().click()
  const word = (await borne.locator('p.text-5xl').innerText().catch(() => '')).trim()
  if (expectWord && word !== expectWord) bad(id, `borne announces « ${expectWord} »`, word)
  await dragSchema(borne, SCHEMA)
}

// ================================================================ Tier A — the switch, the borne, the schéma
await scenario('A1', async () => {
  await adm.setViewportSize({ width: 1440, height: 900 })
  await adm.goto(`${WEB}/settings`)
  const sw = adm.locator('#team-attendance-lock')
  await sw.waitFor({ timeout: 20000 })
  await sw.scrollIntoViewIfNeeded()
  const label = await adm.locator('label[for="team-attendance-lock"]').innerText().catch(() => '')
  check('A1', /Bloquer l'app tant que l'arrivée n'est pas pointée/.test(label), 'sub-switch label', label)
  check('A1', (await sw.getAttribute('aria-checked')) === 'true', 'sub-switch ON by default', await sw.getAttribute('aria-checked'))
  await shot(adm, 'A1-settings-lock-switch')
})

await scenario('A2', async () => {
  await adm.setViewportSize({ width: 1000, height: 830 })
  await adm.getByRole('button', { name: 'Ajouter une borne' }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#borne-name').waitFor()
  // Run 7: the dialog asks which device; « Cet appareil » is the admin's own (the cookie lands here).
  await dlg.locator('label[for="borne-device-this"]').click()
  await dlg.getByRole('button', { name: 'Enregistrer la borne' }).click()
  const code = adm.locator('[role="dialog"]:visible input[inputmode=numeric]').last()
  await code.waitFor({ timeout: 8000 })
  await nextWindow()
  await code.fill(totp(ADMIN.totp))
  await openDialog(adm).getByRole('button', { name: 'Confirmer' }).click()
  const t = await toast(adm, /maintenant la borne/, 10000)
  check('A2', Boolean(t) && /Accueil/.test(t), 'borne « Accueil » registered', t ?? '')
})

await scenario('A3', async () => {
  await sec.goto(`${WEB}/patients`)
  const up = await gateUp(sec, 20000)
  const t = (await gate(sec).innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('A3', up && /Pointez votre arrivée/.test(t), 'full-screen « Pointez votre arrivée »', t.slice(0, 160))
  check('A3', /Créer mon schéma/.test(t) && /Se déconnecter/.test(t), 'no schéma yet: « Créer mon schéma » + « Se déconnecter »', t.slice(0, 160))
  await shot(sec, 'A3-lock-no-schema')
})

await scenario('A4', async () => {
  await gate(sec).getByRole('link', { name: 'Créer mon schéma' }).click()
  await sec.waitForURL(/\/mon-profil/, { timeout: 10000 })
  check('A4', await gateGone(sec, 8000), '« Mon profil » is not covered while she has no schéma')
  await sec.getByRole('button', { name: 'Créer mon schéma' }).click()
  await openDialog(sec).getByText('Tracez votre schéma').waitFor()
  await typeSchema(sec, SCHEMA)
  await waitText(sec, /Tracez-le à nouveau/, 5000)
  await typeSchema(sec, SCHEMA)
  check('A4', Boolean(await toast(sec, /Schéma de pointage enregistré/)), 'toast « Schéma de pointage enregistré. » (exempt write)')
})

await scenario('A5', async () => {
  await sec.goto(`${WEB}/patients`)
  const up = await gateUp(sec, 15000)
  const t = (await gate(sec).innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('A5', up && /Borne : Accueil/.test(t), 'lock names « Borne : Accueil »', t.slice(0, 160))
  const buttons = (await gate(sec).getByRole('button').allInnerTexts()).map((s) => s.trim())
  const links = (await gate(sec).getByRole('link').allInnerTexts()).map((s) => s.trim())
  // Run 5: the review's « Congés stays open » adds the link; the only BUTTON is still « Se déconnecter ».
  check('A5', buttons.length === 1 && /Se déconnecter/.test(buttons[0]), 'its only button is « Se déconnecter »', JSON.stringify(buttons))
  check('A5', links.length === 1 && /Demander un congé/.test(links[0]), 'one link « Demander un congé » (congés on)', JSON.stringify(links))
  await shot(sec, 'A5-lock-named-borne')
})

// ---------------------------------------------------------------- run 5: focus, the modal layer, « Congés » open
await scenario('C7', async () => {
  await sec.goto(`${WEB}/patients`)
  await gateUp(sec, 15000)
  await sec.waitForTimeout(600)
  const f = await sec.evaluate(() => ({
    text: document.activeElement?.textContent?.trim() ?? '', tag: document.activeElement?.tagName ?? '',
    scrollLocked: document.body.hasAttribute('data-scroll-locked'),
  }))
  check('C7', f.text === 'Pointez votre arrivée', 'focus on the title, not « Se déconnecter »', JSON.stringify(f))
  check('C7', f.scrollLocked, 'body[data-scroll-locked] set — the other popups see a modal', JSON.stringify(f))
  await sec.keyboard.press('Enter')
  await sec.waitForTimeout(1500)
  check('C7', !sec.url().includes('/login') && (await gate(sec).count()) === 1, 'Enter does not sign her out', sec.url())
})

await scenario('B13', async () => {
  await gate(sec).getByRole('link', { name: 'Demander un congé' }).click()
  const there = await sec.waitForURL(/\/conges/, { timeout: 10000 }).then(() => true, () => false)
  check('B13', there, '« Demander un congé » lands on /conges', sec.url())
  check('B13', await waitText(sec, /Mes congés/, 10000), '« Mes congés » shown')
  await sec.waitForTimeout(2500)
  check('B13', (await gate(sec).count()) === 0, 'no lock over « Mes congés »')
  await shot(sec, 'B13-conges-open')
  await sec.goto(`${WEB}/patients`)
  check('B13', await gateUp(sec, 15000), 'the lock returns on /patients')
})

// ---------------------------------------------------------------- API rows while locked (A6, B1, B2, D3)
await scenario('A6', async () => {
  const before = psql(`select count(*) from "Patients" where "ClinicId"='${CLINIC}'`)
  const r = await api('POST', '/patients', { firstName: 'Qa', lastName: 'Verrou', phoneNumber: '+21698000111', dateOfBirth: '1990-01-01', gender: 'F' })
  const after = psql(`select count(*) from "Patients" where "ClinicId"='${CLINIC}'`)
  check('A6', r.status === 423 && r.json?.code === 'clock_in_required', '423 clock_in_required on a write', `${r.status} ${JSON.stringify(r.json)}`)
  check('A6', /Pointez votre arrivée/.test(r.json?.error ?? ''), 'French sentence', r.json?.error ?? '')
  check('A6', before === after, 'no patient written', `${before} → ${after}`)
  const cs = await clockStatus()
  check('A6', cs.locked === true && JSON.stringify(cs.bornes) === '["Accueil"]' && cs.canClockOutHere === false && cs.hasSchema === true,
    'clock-status: locked, bornes [Accueil], not a borne here', JSON.stringify(cs))
})

await scenario('B14', async () => {
  psql(`update "AttendanceTerminals" set "RevokedAt" = now() where "ClinicId"='${CLINIC}' and "RevokedAt" is null`)
  const cs = await clockStatus()
  const w = await api('POST', '/patients', {})
  check('B14', cs.locked === false && w.status !== 423, 'no active borne: not locked, a write is not 423', `${JSON.stringify(cs)} | write ${w.status}`)
  psql(`update "AttendanceTerminals" set "RevokedAt" = null where "ClinicId"='${CLINIC}' and "Name"='Accueil'`)
  check('B14', (await clockStatus()).locked === true, 'borne restored (SQL, own artefact) → locked again')
})

await scenario('B1', async () => {
  const schema = await api('PUT', '/team/me/schema', { pattern: SCHEMA })
  const read = await api('PUT', '/notifications/read-all')
  const leave = await api('POST', '/team/me/leave-requests', {})
  check('B1', schema.status !== 423 && schema.status < 300, 'PUT /team/me/schema passes', String(schema.status))
  check('B1', read.status !== 423, 'PUT /notifications/read-all passes', String(read.status))
  check('B1', leave.status !== 423, 'POST /team/me/leave-requests reaches its handler (not 423)', `${leave.status} ${JSON.stringify(leave.json)?.slice(0, 120)}`)
})

await scenario('B2', async () => {
  const p = await api('GET', '/patients?page=1&pageSize=5')
  const a = await api('GET', '/appointments')
  check('B2', p.status === 200 && a.status === 200, 'reads stay open', `${p.status} / ${a.status}`)
})

await scenario('D3', async () => {
  const r = await api('GET', '/clinics/user-status')
  const clinic = r.json?.value?.clinic ?? r.json?.clinic
  check('D3', clinic?.attendanceLockEnabled === true, 'user-status carries attendanceLockEnabled', JSON.stringify(clinic ?? r.json ?? {}).slice(0, 200))
})

// ---------------------------------------------------------------- the lock lifts by itself
let arrivedAt = ''
await scenario('A7', async () => {
  const m = await mark(sec)
  await bornePunch('A7', 'Arrivée')
  const conf = await waitText(borne, /Arrivée enregistrée/, 8000)
  arrivedAt = ((await bodyText(borne)).match(/(\d{2}:\d{2}) · Borne Accueil/) ?? [])[1] ?? ''
  check('A7', conf, '« Arrivée enregistrée » at the borne (D1)')
  check('A7', await gateGone(sec, 10000), 'Ines\'s lock closes on its own')
  check('A7', await marked(sec, m), '…with no reload')
})

await scenario('A8', async () => {
  await signOutVia(sec)
  const dlg = openDialog(sec)
  await dlg.waitFor({ timeout: 8000 })
  const t = (await dlg.innerText()).replace(/\s+/g, ' ')
  check('A8', /Pointer votre départ \?/.test(t), 'prompt « Pointer votre départ ? »', t.slice(0, 200))
  check('A8', new RegExp(`Vous êtes présente depuis ${arrivedAt}`).test(t) && /Borne : Accueil/.test(t), `« présente depuis ${arrivedAt} » + « Borne : Accueil »`, t.slice(0, 200))
  check('A8', (await dlg.getByRole('button', { name: 'Se déconnecter sans pointer' }).count()) === 1, '« Se déconnecter sans pointer » offered')
  await shot(sec, 'A8-signout-prompt')
  await dlg.getByRole('button', { name: 'Annuler' }).click()
  await sec.waitForTimeout(1500)
  check('A8', !sec.url().includes('/login'), '« Annuler » keeps her signed in', sec.url())
})

await scenario('B4', async () => {
  await bornePunch('B4', 'Départ')
  check('B4', await waitText(borne, /Départ enregistré/, 8000), '« Départ enregistré » (D1)')
  const cs = await clockStatus()
  check('B4', cs.locked === false && cs.presentSince === null, 'after her departure: not locked, not present', JSON.stringify(cs))
  await sec.reload()
  await sec.waitForTimeout(3000)
  check('B4', (await gate(sec).count()) === 0, 'no lock after reload')
})

// ---------------------------------------------------------------- device states of the lock (C1, C2)
await scenario('C1', async () => {
  clearInesToday()
  await sec.setViewportSize({ width: 320, height: 568 })
  await sec.goto(`${WEB}/patients`)
  check('C1', await gateUp(sec, 15000), 'lock back once her line is gone')
  const m = await gate(sec).evaluate((el) => {
    const b = [...el.querySelectorAll('button')].find((x) => /Se déconnecter/.test(x.textContent))
    const r = b?.getBoundingClientRect()
    return { sh: el.scrollHeight, ch: el.clientHeight, b: r ? { top: r.top, bottom: r.bottom, left: r.left, right: r.right, h: r.height } : null, vw: innerWidth }
  })
  check('C1', m.sh <= m.ch + 1, 'no scroll at 320×568', JSON.stringify(m))
  check('C1', m.b && m.b.bottom <= 568 && m.b.left >= 0 && m.b.right <= 320, '« Se déconnecter » fully on screen', JSON.stringify(m.b))
  await shot(sec, 'C1-lock-320')
})

await scenario('C2', async () => {
  await sec.setViewportSize({ width: 812, height: 380 })
  await sec.waitForTimeout(600)
  const top = await gate(sec).evaluate((el) => { el.scrollTop = 0; return el.querySelector('h2')?.getBoundingClientRect().top ?? -1 })
  check('C2', top >= 0, 'title not clipped at 380 px tall', `top=${top}`)
  const btn = gate(sec).getByRole('button', { name: /Se déconnecter/ })
  await btn.scrollIntoViewIfNeeded()
  const r = await btn.boundingBox()
  check('C2', r && r.y >= 0 && r.y + r.height <= 380, '« Se déconnecter » reachable', JSON.stringify(r))
  await shot(sec, 'C2-lock-380')
  await sec.setViewportSize({ width: 560, height: 760 })
})

// ---------------------------------------------------------------- AC-5: the admin's « Ajouter un pointage » lifts it
await scenario('A16', async () => {
  await adm.setViewportSize({ width: 1440, height: 900 })
  await adm.goto(sheetUrl)
  await adm.getByRole('button', { name: 'Ajouter un pointage' }).waitFor({ timeout: 20000 })
  await adm.waitForTimeout(1000)
  const m = await mark(sec)
  check('A16', (await gate(sec).count()) === 1, 'Ines locked before')
  await adm.getByRole('button', { name: 'Ajouter un pointage' }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#record-day').waitFor()
  await pickPerson(adm, dlg, INES.name)
  await dlg.locator('#record-day').fill(TODAY)
  await dlg.locator('#record-arrival').fill('0000')
  await dlg.locator('#record-arrival').blur()
  await dlg.locator('#record-reason').fill('Borne en panne')
  await dlg.getByRole('button', { name: 'Ajouter', exact: true }).click()
  check('A16', Boolean(await toast(adm, /Pointage ajouté/)), 'toast « Pointage ajouté. »')
  check('A16', await gateGone(sec, 10000), 'Ines\'s lock lifts on its own')
  check('A16', await marked(sec, m), '…with no reload')
})

// ---------------------------------------------------------------- B3: the sub-switch
await scenario('B3', async () => {
  clearInesToday()
  await sec.reload()
  check('B3', await gateUp(sec, 15000), 'locked (no line today)')
  const m = await mark(sec)
  await adm.goto(`${WEB}/settings`)
  const sw = adm.locator('#team-attendance-lock')
  await sw.waitFor({ timeout: 20000 })
  await sw.scrollIntoViewIfNeeded()
  const t0 = Date.now()
  await sw.click()
  check('B3', Boolean(await toast(adm, /n'attend plus/)), 'toast « L\'app n\'attend plus l\'arrivée pointée. »')
  check('B3', psql(`select "AttendanceLockEnabled" from "Clinics" where "Id"='${CLINIC}'`) === 'f', 'stored off')
  const cs = await clockStatus()
  const w = await api('POST', '/patients', {})
  check('B3', cs.locked === false && w.status !== 423, 'not locked, a write is not refused 423', `${JSON.stringify(cs)} | write ${w.status}`)
  const lifted = await gateGone(sec, 70000)
  const secs = Math.round((Date.now() - t0) / 1000)
  const sameLoad = await marked(sec, m)
  if (lifted && sameLoad) ok('B3', 'the open screen unlocks within a minute, no reload')
  else bad('B3', 'the open screen unlocks within a minute, no reload', `lifted=${lifted} sameLoad=${sameLoad}`)
  check('B3', lifted && secs <= 10, `…at once, on the \`clinics\` broadcast (${secs} s)`, `${secs} s`)
  await sec.reload()
  await sec.waitForTimeout(3000)
  check('B3', (await gate(sec).count()) === 0, 'no lock after reload with the switch off')
  await sw.click()
  check('B3', Boolean(await toast(adm, /attend désormais/)), 'switch back on')
  await sec.reload()
  check('B3', await gateUp(sec, 15000), 'locked again')
})

// ---------------------------------------------------------------- B5–B7: hours, closed day, congé
await scenario('B5', async () => {
  clearInesToday()
  // Opening an hour from now, whatever the wall clock says when the walk runs.
  const opens = hhmm(new Date(local().getTime() + 60 * 60_000))
  const closes = hhmm(new Date(local().getTime() + 120 * 60_000))
  setHours({ enabled: true, from: opens, to: closes })
  const cs = await clockStatus()
  check('B5', cs.locked === false, `before opening (${hhmm(local())} < ${opens}): not locked`, JSON.stringify(cs))
  await sec.reload()
  await sec.waitForTimeout(3000)
  check('B5', (await gate(sec).count()) === 0, 'no lock on screen')
  setHours(OPEN_ALL_DAY)
  check('B5', (await clockStatus()).locked === true, 'hours restored → locked again')
})

await scenario('B6', async () => {
  setHours({ enabled: false, from: '00:00', to: '23:59' })
  check('B6', (await clockStatus()).locked === false, 'closed day: not locked')
  setHours(OPEN_ALL_DAY)
})

await scenario('B7', async () => {
  const id = crypto.randomUUID()
  psql(`insert into "StaffLeaveRequests" ("Id","ClinicId","UserId","Type","StartDate","EndDate","HalfDay","Reason","Status","RequestedDays","CountedDays","DecidedByUserId","DecidedAt","EnteredByAdmin","CreatedAt")
        values ('${id}','${CLINIC}','${INES.id}',0,'${TODAY}','${TODAY}',true,'QA congé',1,0.5,0.5,'${ADMIN.id}',now(),true,now())`)
  check('B7', (await clockStatus()).locked === false, 'accepted half-day congé today: not locked (DEV-7)')
  psql(`delete from "StaffLeaveRequests" where "Id"='${id}'`)
  check('B7', (await clockStatus()).locked === true, 'congé removed → locked')
})

// ---------------------------------------------------------------- B8: an admin is never locked
await scenario('B8', async () => {
  await adm.goto(`${WEB}/settings`)
  const sw = adm.getByRole('switch', { name: `${ADMIN.name} pointe` })
  await sw.waitFor({ timeout: 20000 })
  await sw.scrollIntoViewIfNeeded()
  if ((await sw.getAttribute('aria-checked')) !== 'true') await sw.click()
  await adm.waitForTimeout(1500)
  const tracked = psql(`select coalesce("AttendanceTrackedOverride"::text,'') from "Users" where "Id"='${ADMIN.id}'`)
  check('B8', tracked === 'true', 'admin tracked by override', tracked)
  await adm.goto(`${WEB}/patients`)
  await adm.waitForTimeout(4000)
  check('B8', (await gate(adm).count()) === 0, 'admin tracked, no line today: no lock (DEV-9)')
  await adm.goto(`${WEB}/settings`)
  await sw.waitFor({ timeout: 20000 })
  await sw.scrollIntoViewIfNeeded()
  await sw.click()
  await adm.waitForTimeout(1500)
})

// ---------------------------------------------------------------- BS: Ines signed in ON the borne device
let BS = null
await scenario('C5', async () => {
  clearInesToday()
  const ctx = await A.browser.newContext()
  await quiet(ctx)
  const cookie = (await A.context.cookies('http://localhost:5096/api/pointage/status')).find((k) => k.name === 'apexa_borne')
  if (!cookie) { bad('C5', 'borne cookie found on the admin context'); return }
  await ctx.addCookies([cookie])
  const page = await ctx.newPage()
  await guardFeedback(page)
  await page.setViewportSize({ width: 820, height: 1000 })
  await signIn(page, INES, false)
  BS = { ctx, page }
  await page.goto(`${WEB}/pointage`)
  check('C5', await waitText(page, /Borne Accueil/, 15000), '/pointage renders the borne for a signed-in, locked secretary')
  await page.waitForTimeout(2500)
  check('C5', (await gate(page).count()) === 0, 'no lock over /pointage (chrome-less)')
})

await scenario('A9', async () => {
  if (!BS) return skip('A9', 'BS context not set up (C5)')
  const page = BS.page
  await page.goto(`${WEB}/patients`)
  check('A9', await gateUp(page, 15000), 'locked on the borne device')
  const t = (await gate(page).innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('A9', /Cet appareil est une borne du cabinet/.test(t) && (await gate(page).locator('svg[role="application"]').count()) === 1,
    'the pad is inside the lock', t.slice(0, 160))
  await shot(page, 'A9-lock-with-pad')
  await dragSchema(page, SCHEMA)
  const at = await toast(page, /Arrivée enregistrée à \d{2}:\d{2}/, 8000)
  check('A9', Boolean(at), 'toast « Arrivée enregistrée à HH:mm. » (cycle 1 fix)', at ?? '')
  check('A9', await gateGone(page, 10000), 'punching from the lock closes it')
  check('A9', inesLines(`and "LocalDay"='${TODAY}' and "DepartureUtc" is null`) === '1', 'one open line today')
})

await scenario('A10', async () => {
  if (!BS) return skip('A10', 'BS context not set up (C5)')
  const page = BS.page
  await page.setViewportSize({ width: 1536, height: 730 })
  await page.goto(`${WEB}/patients`)
  await page.waitForTimeout(2500)
  await signOutVia(page)
  const dlg = openDialog(page)
  await dlg.waitFor({ timeout: 8000 })
  const t = (await dlg.innerText()).replace(/\s+/g, ' ')
  check('A10', /Pointer votre départ \?/.test(t) && (await dlg.locator('svg[role="application"]').count()) === 1, 'prompt carries the pad', t.slice(0, 200))
  // Owner's polish (after run 3): one headline, and « Annuler » sits in the pinned footer beside the sign-out.
  check('A10', !/Ines Gharbi/.test(t) && (await dlg.locator('p.text-5xl').count()) === 0, 'no second headline under the title', t.slice(0, 200))
  const footer = (await dlg.locator('[data-slot="dialog-footer"]').innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('A10', /Annuler/.test(footer) && /Se déconnecter sans pointer/.test(footer), '« Annuler » + « Se déconnecter sans pointer » in the footer', footer)
  const r = await dlg.getByRole('button', { name: 'Se déconnecter sans pointer' }).boundingBox()
  check('C3', r && r.y + r.height <= 730, '« Se déconnecter sans pointer » on screen at 1536×730', JSON.stringify(r))
  await shot(page, 'A10-C3-signout-pad-730')
  await page.route('**/pointage/punch', async (r) => { await sleep(2500); await r.continue() })
  await dragSchema(page, SCHEMA)
  await page.waitForTimeout(400)
  const held = {
    annuler: await openDialog(page).getByRole('button', { name: 'Annuler' }).isDisabled().catch(() => null),
    sans: await openDialog(page).getByRole('button', { name: 'Se déconnecter sans pointer' }).isDisabled().catch(() => null),
  }
  await page.keyboard.press('Escape')
  await page.waitForTimeout(250)
  const stillOpen = await openDialog(page).count()
  check('C8', held.annuler === true && held.sans === true, 'both footer buttons held while the departure is in flight', JSON.stringify(held))
  check('C8', stillOpen === 1, 'Escape does not close it mid-flight', `dialogs=${stillOpen}`)
  // F1 (run 1): the confirmation is shown in the dialog, before the sign-out reload.
  await openDialog(page).getByRole('heading', { name: 'Départ enregistré' }).waitFor({ timeout: 8000 }).catch(() => {})
  const conf = (await openDialog(page).innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('A10', /Départ enregistré/.test(conf) && /À \d{2}:\d{2}\./.test(conf), '« Départ enregistré · À HH:mm. » readable before sign-out', conf)
  // F2 (run 2): nothing in the dialog still asks the question or calls her present.
  check('A10', !/Pointer votre départ|Vous êtes présente/.test(conf), 'the header no longer asks or says « présente »', conf)
  await shot(page, 'A10-departure-confirmed')
  check('A10', await page.waitForURL(/\/login/, { timeout: 10000 }).then(() => true, () => false), 'then signed out', page.url())
  await page.unroute('**/pointage/punch').catch(() => {})
  check('A10', inesLines(`and "LocalDay"='${TODAY}' and "DepartureUtc" is null`) === '0', 'today\'s line closed')
})

// ---------------------------------------------------------------- B12 / D5: not present → no prompt
await scenario('B12', async () => {
  await sec.goto(`${WEB}/patients`)
  await sec.waitForTimeout(3000)
  const cs = await clockStatus()
  if (cs.presentSince) return skip('B12', `precondition: she is present (${cs.presentSince})`)
  await signOutVia(sec)
  const out = await sec.waitForURL(/\/login/, { timeout: 10000 }).then(() => true, () => false)
  check('B12', out, 'not present: « Se déconnecter » signs out at once', sec.url())
})
await scenario('D5', async () => {
  await sec.locator('input[type=email]').waitFor({ timeout: 15000 }).catch(() => {})
  check('D5', (await sec.locator('input[type=email]').count()) === 1 && (await gate(sec).count()) === 0, '/login renders, no lock')
  await signIn(sec, INES, false)
})

// ---------------------------------------------------------------- AC-8: the next-morning declaration
let expectedLastSave = null
await scenario('A11', async () => {
  await adm.setViewportSize({ width: 1440, height: 900 })
  await adm.goto(sheetUrl)
  await adm.getByRole('button', { name: 'Ajouter un pointage' }).waitFor({ timeout: 20000 })
  await adm.waitForTimeout(800)
  await adm.getByRole('button', { name: 'Ajouter un pointage' }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#record-day').waitFor()
  await pickPerson(adm, dlg, INES.name)
  await dlg.locator('#record-day').fill(YDAY)
  await dlg.locator('#record-arrival').fill('0800')
  await dlg.locator('#record-arrival').blur()
  await dlg.locator('#record-reason').fill('Borne en panne hier')
  await dlg.getByRole('button', { name: 'Ajouter', exact: true }).click()
  check('A11', Boolean(await toast(adm, /Pointage ajouté/)), 'toast « Pointage ajouté. »')
  check('A11', await waitText(adm, /08:00 – Départ non pointé/, 8000), `${YDAY}: « 08:00 – Départ non pointé »`)
  const raw = psql(`select coalesce(to_char(max("OccurredAt") at time zone 'UTC' + interval '1 hour','HH24:MI'),'') from "AuditEntries"
                    where "ClinicId"='${CLINIC}' and "UserId"='${INES.id}'
                      and "OccurredAt" >= '${YDAY}T00:00:00+01:00' and "OccurredAt" < '${TODAY}T00:00:00+01:00'
                      and "EntityType" not in ('AttendanceEntry','AttendanceTerminal','User','DeviceRegistration','StaffLeaveRequest')`)
  expectedLastSave = raw || null
  console.log('    journal: Ines\'s last save on', YDAY, '=', expectedLastSave ?? 'aucune')
})

await scenario('A12', async () => {
  await bornePunch('A12', 'Arrivée')
  check('A12', await waitText(borne, /Votre départ d'hier n'a pas été pointé/, 8000), 'step « Votre départ d\'hier n\'a pas été pointé »')
  const t = (await borne.locator('form').innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('A12', /Arrivée 08:00/.test(t), 'arrival 08:00 shown', t.slice(0, 200))
  const want = expectedLastSave ?? 'aucune'
  check('A12', new RegExp(`Dernière action dans l'app ${want}`).test(t), `last save « ${want} » (journal)`, t.slice(0, 200))
  check('A12', inesLines(`and "LocalDay"='${YDAY}' and "DepartureUtc" is null`) === '1', 'nothing written yet (schéma alone)')
  await shot(borne, 'A12-declaration-step')
})

await scenario('B9', async () => {
  const f = borne.locator('#declared-departure')
  await f.fill('0700')
  await f.blur()
  const err = (await borne.locator('#declared-departure-error').innerText().catch(() => '')).trim()
  check('B9', /après l'arrivée \(08:00\)/.test(err), 'refusal « … après l\'arrivée (08:00) »', err)
  check('B9', await borne.getByRole('button', { name: 'Valider' }).isDisabled(), '« Valider » disabled')
  check('B9', inesLines(`and "LocalDay"='${YDAY}' and "DepartureUtc" is null`) === '1', 'nothing written')
})

await scenario('B10', async () => {
  await borne.locator('form').getByRole('button', { name: 'Retour' }).click()
  check('B10', (await borne.locator('svg[role="application"]:visible').count()) === 1, 'back to the pad')
  check('B10', inesLines(`and "LocalDay"='${TODAY}'`) === '1' && inesLines(`and "LocalDay"='${YDAY}' and "DepartureUtc" is null`) === '1', 'nothing written')
  await dragSchema(borne, SCHEMA)
  check('B10', await waitText(borne, /Votre départ d'hier n'a pas été pointé/, 8000), 'schéma again → the step again')
})

await scenario('C4', async () => {
  await borne.setViewportSize({ width: 820, height: 600 })
  await borne.waitForTimeout(500)
  const field = borne.locator('#declared-departure')
  await field.scrollIntoViewIfNeeded()
  const fb = await field.boundingBox()
  const vb = await borne.getByRole('button', { name: 'Valider' }).boundingBox()
  check('C4', fb && fb.y >= 0 && fb.y + fb.height <= 600 && vb && vb.y + vb.height <= 600, 'field + « Valider » on screen at 820×600 (keyboard open)', JSON.stringify({ fb, vb }))
  await shot(borne, 'C4-declaration-820x600')
})

let todayArrival = ''
await scenario('A13', async () => {
  const f = borne.locator('#declared-departure')
  await f.fill('1730')
  await f.blur()
  await borne.getByRole('button', { name: 'Valider' }).click()
  check('A13', await waitText(borne, /Arrivée enregistrée/, 8000), '« Arrivée enregistrée »')
  todayArrival = ((await bodyText(borne)).match(/(\d{2}:\d{2}) · Borne Accueil/) ?? [])[1] ?? ''
  const r = psql(`select to_char("DepartureUtc" at time zone 'UTC' + interval '1 hour','HH24:MI') || '|' || "DepartureSource" || '|' ||
                         coalesce(to_char("DeclaredLastSaveUtc" at time zone 'UTC' + interval '1 hour','HH24:MI'),'')
                  from "AttendanceEntries" where "UserId"='${INES.id}' and "LocalDay"='${YDAY}'`)
  check('A13', r === `17:30|1|${expectedLastSave ?? ''}`, 'yesterday closed at 17:30, DeclaredBySelf, last save stored as shown', r)
  check('A13', inesLines(`and "LocalDay"='${TODAY}' and "DepartureUtc" is null`) === '1', 'today\'s arrival opened')
  await borne.setViewportSize({ width: 820, height: 1180 })
})

await scenario('A14', async () => {
  await adm.goto(sheetUrl)
  await waitText(adm, /novembre 2026/i, 15000)
  await adm.waitForTimeout(1200)
  const row = adm.locator('tr:visible', { hasText: /jeu\.? 5/ }).first()
  const t = (await row.innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('A14', /08:00 – 17:30/.test(t) && /Déclaré/.test(t), '« 08:00 – 17:30 » + « Déclaré »', t)
  const want = expectedLastSave ? `Dernière action dans l'app : ${expectedLastSave}` : "Aucune action dans l'app ce jour"
  check('A14', t.includes(want), `« ${want} » beside it`, t)
  const amber = await row.locator('span.text-warning-ink', { hasText: '17:30' }).count()
  check('A14', amber > 0, '17:30 in amber (text-warning-ink)', `n=${amber}`)
  // An admin-added line is « Corrigé » from birth; declared since, it carries both chips.
  const iC = t.indexOf('Corrigé'), iD = t.indexOf('Déclaré'), iR = t.indexOf('Borne en panne hier')
  check('C9', iC >= 0 && iD > iC && iR > iD, '« Corrigé » then « Déclaré » side by side, before the motif', t)
  await row.scrollIntoViewIfNeeded()
  await shot(adm, 'A14-sheet-declared')
})

await scenario('A15', async () => {
  const total = (await adm.locator('tr:visible', { hasText: 'Total' }).first().innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('A15', /1 départ déclaré/.test(total), 'total says « 1 départ déclaré »', total)
  const minutes = psql(`select coalesce(sum(extract(epoch from ("DepartureUtc" - "ArrivalUtc"))/60)::int,0) from "AttendanceEntries" where "UserId"='${INES.id}' and "DepartureUtc" is not null`)
  check('A15', Number(minutes) >= 570, 'declared hours counted (08:00–17:30 = 570 min)', `closed minutes=${minutes}`)
})

// ---------------------------------------------------------------- AC-6: the reminder
async function triggerReminder() {
  await adm.goto(HANGFIRE)
  const row = adm.locator('tr', { hasText: 'remind-missing-departures' }).first()
  await row.waitFor({ timeout: 15000 })
  await row.locator('input[type=checkbox]').check()
  await adm.locator('button', { hasText: /Trigger now|Déclencher/ }).first().click()
}
const reminders = () => psql(`select count(*) from "StaffNotifications" where "Category"=24 and "TargetUserId"='${INES.id}'`)

await scenario('A17', async () => {
  let now = local()
  const minutes = now.getUTCHours() * 60 + now.getUTCMinutes()
  if (minutes < 17) { await sleep((17 - minutes) * 60_000 + 5000); now = local() }
  const closing = new Date(now.getTime() - 16 * 60_000)
  setHours({ enabled: true, from: '00:00', to: hhmm(closing) })
  const xmin = psql(`select xmin from "AttendanceEntries" where "UserId"='${INES.id}' and "LocalDay"='${TODAY}' and "DepartureUtc" is null`)
  try { await triggerReminder() } catch (e) { return skip('A17', `Hangfire dashboard not reachable: ${String(e.message).split('\n')[0]}`) }
  let n = '0'
  for (let i = 0; i < 40 && (n = reminders()) === '0'; i++) await sleep(1000)
  check('A17', n === '1', 'one reminder row for Ines', `n=${n}`)
  const row = psql(`select "Title" || '|' || "Message" || '|' || "TargetKind" from "StaffNotifications" where "Category"=24 and "TargetUserId"='${INES.id}'`)
  check('A17', row === `Départ non pointé|Vous êtes toujours présente depuis ${todayArrival}.|11`, 'title, message, target « Attendance »', row)
  const others = psql(`select count(*) from "StaffNotifications" where "Category"=24 and "TargetUserId"<>'${INES.id}'`)
  check('A17', others === '0', 'nobody else reminded', `others=${others}`)
  const after = psql(`select xmin from "AttendanceEntries" where "UserId"='${INES.id}' and "LocalDay"='${TODAY}' and "DepartureUtc" is null`)
  check('A17', after === xmin, 'the reminder wrote no pointage row (xmin unchanged)', `${xmin} → ${after}`)
})

await scenario('B11', async () => {
  try { await triggerReminder() } catch (e) { return skip('B11', 'Hangfire dashboard not reachable') }
  await sleep(15000)
  check('B11', reminders() === '1', 'second pass: still one reminder (once per line)', `n=${reminders()}`)
  setHours(OPEN_ALL_DAY)
})

await scenario('D4', async () => {
  await sec.goto(`${WEB}/patients`)
  await sec.waitForTimeout(2500)
  await sec.getByRole('button', { name: 'Notifications' }).click()
  const found = await waitText(sec, /Départ non pointé/, 8000)
  check('D4', found, 'the bell lists « Départ non pointé »')
  await shot(sec, 'D4-bell-reminder')
  await sec.getByText('Départ non pointé').first().click()
  check('D4', await sec.waitForURL(/\/mon-profil/, { timeout: 8000 }).then(() => true, () => false), 'clicking it lands on « Mon profil »', sec.url())
})

// ---------------------------------------------------------------- C6: correcting a declared line
await scenario('C6', async () => {
  await adm.goto(sheetUrl)
  await waitText(adm, /novembre 2026/i, 15000)
  await adm.waitForTimeout(1200)
  const row = adm.locator('tr:visible', { hasText: /jeu\.? 5/ }).first()
  await row.getByRole('button', { name: /^Corriger le pointage de Ines Gharbi/ }).first().click()
  const dlg = openDialog(adm)
  await dlg.locator('#correct-departure').waitFor()
  await dlg.locator('#correct-departure').fill('1745')
  await dlg.locator('#correct-departure').blur()
  await dlg.locator('#correct-reason').fill('Parti à 17 h 45')
  await dlg.getByRole('button', { name: 'Corriger', exact: true }).click()
  check('C6', Boolean(await toast(adm, /Pointage corrigé/)), 'toast « Pointage corrigé. »')
  await adm.waitForTimeout(1200)
  const t = (await adm.locator('tr:visible', { hasText: /jeu\.? 5/ }).first().innerText()).replace(/\s+/g, ' ')
  check('C6', /08:00 – 17:45/.test(t) && /Corrigé/.test(t) && !/Déclaré/.test(t), 'reads « Corrigé », « Déclaré » gone', t)
  const total = (await adm.locator('tr:visible', { hasText: 'Total' }).first().innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('C6', !/départ déclaré/.test(total), 'total no longer counts it as declared', total)
  check('C6', psql(`select "DepartureSource" from "AttendanceEntries" where "UserId"='${INES.id}' and "LocalDay"='${YDAY}'`) === '2', 'DepartureSource 2 (corrected)')
})

// ---------------------------------------------------------------- D2: the congés switch beside it
await scenario('D2', async () => {
  await adm.goto(`${WEB}/settings`)
  const sw = adm.locator('#team-leave-requests')
  await sw.waitFor({ timeout: 20000 })
  await sw.scrollIntoViewIfNeeded()
  await sw.click()
  check('D2', Boolean(await toast(adm, /Demandes de congé désactivées/)), 'congés off')
  await adm.waitForTimeout(800)
  await sw.click()
  check('D2', Boolean(await toast(adm, /Demandes de congé activées/)), 'congés on')
  check('D2', (await adm.locator('#team-attendance-lock').getAttribute('aria-checked')) === 'true', 'lock switch untouched')
})

// ---------------------------------------------------------------- run 5: several forgotten days, oldest first
const dayMinus = (n) => local(new Date(Date.now() - n * 86400_000)).toISOString().slice(0, 10)
const frDay = (iso) => new Intl.DateTimeFormat('fr-FR', { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'UTC' })
  .format(new Date(`${iso}T00:00:00Z`))
async function adminAdd(day, arrivalDigits, reason) {
  await adm.goto(sheetUrl)
  await adm.getByRole('button', { name: 'Ajouter un pointage' }).waitFor({ timeout: 20000 })
  await adm.waitForTimeout(800)
  await adm.getByRole('button', { name: 'Ajouter un pointage' }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#record-day').waitFor()
  await pickPerson(adm, dlg, INES.name)
  await dlg.locator('#record-day').fill(day)
  await dlg.locator('#record-arrival').fill(arrivalDigits)
  await dlg.locator('#record-arrival').blur()
  await dlg.locator('#record-reason').fill(reason)
  await dlg.getByRole('button', { name: 'Ajouter', exact: true }).click()
  return Boolean(await toast(adm, /Pointage ajouté/))
}

await scenario('A18', async () => {
  clearInesToday()
  const d3 = dayMinus(3)
  const d4 = dayMinus(2)
  await adm.setViewportSize({ width: 1440, height: 900 })
  check('A18', await adminAdd(d3, '0810', 'Borne en panne'), `${d3} added open (08:10)`)
  check('A18', await adminAdd(d4, '0820', 'Borne en panne'), `${d4} added open (08:20)`)
  await bornePunch('A18', 'Arrivée')
  const first = new RegExp(`Votre départ du ${frDay(d3)} n'a pas été pointé`)
  check('A18', await waitText(borne, first, 8000), `asked for « du ${frDay(d3)} » first (the oldest)`, ((await bodyText(borne)).match(/Votre départ[^\n]*/) ?? [''])[0])
  await shot(borne, 'A18-oldest-first')
  const f = borne.locator('#declared-departure')
  await f.fill('1700')
  await f.blur()
  await borne.getByRole('button', { name: 'Valider' }).click()
  const second = new RegExp(`Votre départ du ${frDay(d4)} n'a pas été pointé`)
  check('A18', await waitText(borne, second, 8000), `then « du ${frDay(d4)} »`, ((await bodyText(borne)).match(/Votre départ[^\n]*/) ?? [''])[0])
  check('A18', inesLines(`and "LocalDay"='${TODAY}'`) === '0', 'no arrival yet while a day is still open')
  await f.click()
  await f.pressSequentially('930')
  const enabled = !(await borne.getByRole('button', { name: 'Valider' }).isDisabled())
  check('A18', enabled, '« Valider » enabled on « 930 » before the field commits')
  await borne.keyboard.press('Enter')
  check('A18', await waitText(borne, /Arrivée enregistrée/, 8000), '« 930 » + Enter → « Arrivée enregistrée »')
  const rows = psql(`select "LocalDay" || '|' || coalesce(to_char("DepartureUtc" at time zone 'UTC' + interval '1 hour','HH24:MI'),'') || '|' || coalesce("DepartureSource"::text,'')
                     from "AttendanceEntries" where "UserId"='${INES.id}' and "LocalDay" in ('${d3}','${d4}') order by "LocalDay"`)
  check('A18', rows === `${d3}|17:00|1\n${d4}|09:30|1`, 'both days closed as declared (17:00, 09:30)', rows.replace(/\n/g, ' ; '))
  check('A18', inesLines(`and "LocalDay"='${TODAY}' and "DepartureUtc" is null`) === '1', 'today\'s arrival opened')
})

// ================================================================ run 7: « Ajouter une borne » chooses the device (DEV-12)
// The session a page belongs to — read off the token the app itself fetches on every call.
const familyOfPage = (page) => page.evaluate(async () => {
  const r = await fetch('/bff/auth/token', { credentials: 'include' })
  const j = await r.json().catch(() => ({}))
  const t = j.accessToken ?? j.token ?? ''
  const part = t.split('.')[1] ?? ''
  try { return JSON.parse(atob(part.replace(/-/g, '+').replace(/_/g, '/')))['family_id'] ?? null } catch { return null }
})
const familyOfToken = (t) => {
  try { return JSON.parse(Buffer.from((t ?? '').split('.')[1], 'base64url').toString('utf8'))['family_id'] ?? null } catch { return null }
}
async function openAddBorne(page) {
  // a background tab runs no animation: the bottom sheet would be measured still below the screen
  await page.bringToFront().catch(() => {})
  await page.goto(`${WEB}/settings`)
  const btn = page.getByRole('button', { name: 'Ajouter une borne' })
  await btn.waitFor({ timeout: 20000 })
  await btn.scrollIntoViewIfNeeded()
  await btn.click()
  const dlg = openDialog(page)
  await dlg.locator('#borne-name').waitFor()
  await dlg.locator('label[for="borne-device-this"]').waitFor()
  // the other devices are read on open: wait for the list or its empty line
  await waitText(page, /actif il y a|Aucun autre appareil connecté/, 10000)
  return dlg
}
async function stepUpConfirm(page) {
  const code = page.locator('[role="dialog"]:visible input[inputmode=numeric]').last()
  await code.waitFor({ timeout: 8000 })
  await nextWindow()
  await code.fill(totp(ADMIN.totp))
  await openDialog(page).getByRole('button', { name: 'Confirmer' }).click()
}
const settingsRow = (page, name) => page.locator('section[aria-labelledby="bornes-title"] li', { hasText: name }).first()

let secFamily = null
await scenario('B17', async () => {
  await adm.setViewportSize({ width: 1000, height: 830 })
  const dlg = await openAddBorne(adm)
  await dlg.getByRole('button', { name: 'Enregistrer la borne' }).click()
  await adm.waitForTimeout(400)
  const banner = (await dlg.innerText()).replace(/\s+/g, ' ')
  check('B17', /Choisissez l'appareil qui sera la borne\./.test(banner), 'no device picked → « Choisissez l\'appareil qui sera la borne. »', banner.slice(-160))
  check('B17', (await adm.locator('[role="dialog"]:visible input[inputmode=numeric]').count()) === 0, 'no step-up asked')
  await dlg.getByRole('button', { name: 'Retour' }).click()
  await adm.waitForTimeout(500)
})

await scenario('A19', async () => {
  // her ordinary device, signed in through the screen (so it carries its name), on a page the lock leaves open
  await signIn(sec, INES, false)
  await sec.goto(`${WEB}/conges`)
  await waitText(sec, /Mes congés/, 15000)
  secFamily = await familyOfPage(sec)
  check('A19', Boolean(secFamily), 'the secretary page\'s session id read', String(secFamily))
  const dlg = await openAddBorne(adm)
  const text = (await dlg.innerText()).replace(/\s+/g, ' ')
  check('A19', /Cet appareil/.test(text), '« Cet appareil » offered', text.slice(0, 200))
  const row = dlg.locator(`label[for="borne-device-${secFamily}"]`)
  const rowText = (await row.innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('A19', /Ines Gharbi/.test(rowText) && /Chrome sur Windows/.test(rowText) && /actif il y a/.test(rowText),
    'her device listed « Ines Gharbi · Chrome sur Windows · actif il y a … »', rowText)
  const adminFamily = await familyOfPage(adm)
  check('A19', Boolean(adminFamily) && (await dlg.locator(`label[for="borne-device-${adminFamily}"]`).count()) === 0,
    'the admin\'s own session is « Cet appareil », not a second row', String(adminFamily))
  await dlg.locator('#borne-name').fill('Accueil 2')
  await row.click()
  await shot(adm, 'A19-add-borne-pick-device')
  await dlg.getByRole('button', { name: 'Enregistrer la borne' }).click()
  await stepUpConfirm(adm)
  const t = await toast(adm, /envoyée à l'appareil de Ines Gharbi/, 10000)
  check('A19', Boolean(t) && /Accueil 2/.test(t), 'toast « Borne « Accueil 2 » envoyée à l\'appareil de Ines Gharbi. »', t ?? '')
  const row2 = psql(`select ("SecretHash" is null)::text || '|' || coalesce("PendingSessionFamilyId"::text,'') from "AttendanceTerminals" where "Name"='Accueil 2' and "RevokedAt" is null`)
  check('A19', row2 === `true|${secFamily}` || row2.startsWith('false|'), 'SQL: stored waiting for her session (or already claimed)', row2)
})

await scenario('A20', async () => {
  // her open screen claims it on the `team` broadcast — no reload, no click
  const t = await toast(sec, /maintenant la borne/, 20000)
  check('A20', Boolean(t) && /Accueil 2/.test(t), 'her screen says « Cet appareil est maintenant la borne « Accueil 2 » »', t ?? '(none in 20 s)')
  const row = psql(`select ("SecretHash" is not null)::text || '|' || ("PendingSessionFamilyId" is null)::text || '|' || coalesce("DeviceDescription",'') from "AttendanceTerminals" where "Name"='Accueil 2' and "RevokedAt" is null`)
  check('A20', row === 'true|true|Ines Gharbi · Chrome sur Windows', 'SQL: claimed (secret set, no longer waiting), description kept', row)
  const ck = (await S.context.cookies()).find((c) => c.name === 'apexa_borne')
  check('A20', Boolean(ck) && ck.httpOnly && ck.path === '/api/pointage', 'her device holds the HttpOnly borne cookie (path /api/pointage)', JSON.stringify(ck ? { httpOnly: ck.httpOnly, path: ck.path } : null))
  await adm.waitForTimeout(2000)
  const li = (await settingsRow(adm, 'Accueil 2').innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('A20', /Accueil 2/.test(li) && !/En attente/.test(li) && /Ines Gharbi · Chrome sur Windows/.test(li), 'admin list updates by itself: no longer « En attente »', li)
  await shot(adm, 'A20-admin-list-claimed')
  await sec.goto(`${WEB}/pointage`)
  check('A20', await waitText(sec, /Borne Accueil 2/, 15000), 'her device opens as « Borne Accueil 2 »')
  await shot(sec, 'A20-her-device-is-the-borne')
})

await scenario('B15', async () => {
  // a second device of hers, signed in but never opened: the borne waits for it
  const r = await fetch(`${API}/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: INES.email, password: PASSWORD, deviceLabel: 'Firefox sur Windows' }),
  })
  const ffToken = (await r.json())?.value?.accessToken
  const ff = familyOfToken(ffToken)
  check('B15', Boolean(ff), 'a second session of hers (« Firefox sur Windows »)', String(ff))
  const dlg = await openAddBorne(adm)
  await dlg.locator('#borne-name').fill('Salle 3')
  await dlg.locator(`label[for="borne-device-${ff}"]`).click()
  await dlg.getByRole('button', { name: 'Enregistrer la borne' }).click()
  await stepUpConfirm(adm)
  check('B15', Boolean(await toast(adm, /Salle 3/, 10000)), 'toast for « Salle 3 »')
  await adm.waitForTimeout(1500)
  const li = (await settingsRow(adm, 'Salle 3').innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('B15', /En attente de l'appareil · Ines Gharbi · Firefox sur Windows/.test(li), 'list: « En attente de l\'appareil · Ines Gharbi · Firefox sur Windows »', li)
  check('B15', psql(`select ("SecretHash" is null)::text from "AttendanceTerminals" where "Name"='Salle 3' and "RevokedAt" is null`) === 'true', 'SQL: no secret while waiting')
  const st = await clockStatus()
  check('B15', Array.isArray(st.bornes) && !st.bornes.includes('Salle 3'), 'a waiting borne is not offered as a place to pointer', JSON.stringify(st.bornes))
  check('B15', !st.borneWaiting, 'another session of hers is not told it was chosen', String(st.borneWaiting))
  const ffStatus = (await api('GET', '/pointage/me/clock-status', undefined, ffToken)).json ?? {}
  check('B15', ffStatus.borneWaiting === 'Salle 3', 'the chosen session is told « Salle 3 » waits', String(ffStatus.borneWaiting))

  // the lock: with every claimed borne retired, a waiting one alone does not lock her (own artefacts, by SQL)
  clearInesToday()
  const ids = psql(`select string_agg('''' || "Id" || '''', ',') from "AttendanceTerminals" where "RevokedAt" is null and "SecretHash" is not null`)
  if (ids) psql(`update "AttendanceTerminals" set "RevokedAt" = now() where "Id" in (${ids})`)
  const alone = await clockStatus()
  check('B16', alone.locked === false && (alone.bornes ?? []).length === 0, 'only a waiting borne → not locked, no borne named', JSON.stringify({ locked: alone.locked, bornes: alone.bornes }))
  const write = await api('POST', '/patients', {})
  check('B16', write.status !== 423, 'a write is not refused 423 while the only borne waits', String(write.status))
  if (ids) psql(`update "AttendanceTerminals" set "RevokedAt" = null where "Id" in (${ids})`)
  const back = await clockStatus()
  check('B16', back.locked === true, 'claimed bornes restored → locked again', JSON.stringify({ locked: back.locked, bornes: back.bornes }))

  // the chosen device signs out before claiming: the list says so, and « Retirer » clears it
  psql(`update "SessionFamilies" set "EndedAtUtc" = now(), "EndedReason" = 'qa' where "Id" = '${ff}'`)
  await adm.goto(`${WEB}/settings`)
  await waitText(adm, /Salle 3/, 15000)
  const gone = settingsRow(adm, 'Salle 3')
  const goneText = (await gone.innerText().catch(() => '')).replace(/\s+/g, ' ')
  const red = await gone.locator('p.text-destructive').count()
  check('B15', /Appareil déconnecté avant d'avoir reçu la borne : retirez-la et recommencez\./.test(goneText) && red === 1,
    'signed out before claiming → « Appareil déconnecté … » in red', goneText)
  await gone.scrollIntoViewIfNeeded().catch(() => {})
  await shot(adm, 'B15-device-signed-out')
  const dlg2 = await openAddBorne(adm)
  check('B15', (await dlg2.locator(`label[for="borne-device-${ff}"]`).count()) === 0, 'the signed-out device is no longer offered')
  await dlg2.getByRole('button', { name: 'Retour' }).click()
  await adm.waitForTimeout(500)
  await adm.getByRole('button', { name: 'Retirer la borne Salle 3' }).click()
  await openDialog(adm).getByRole('button', { name: 'Retirer', exact: true }).click()
  check('B15', Boolean(await toast(adm, /Salle 3/)), '« Retirer » clears it')
})

await scenario('C10', async () => {
  for (const [w, h] of [[320, 568], [1536, 730]]) {
    await adm.setViewportSize({ width: w, height: h })
    const dlg = await openAddBorne(adm)
    await adm.waitForTimeout(400)
    // run 7: the dialog itself never widens — its scrolling body does; measure every box inside it
    const m = await dlg.evaluate((el) => ({
      // scrollers only: a truncated line also reports scrollWidth > clientWidth, and that is the intended look
      over: Math.max(0, ...[el, ...el.querySelectorAll('*')]
        .filter((n) => n === el || ['auto', 'scroll'].includes(getComputedStyle(n).overflowX))
        .map((n) => n.scrollWidth - n.clientWidth).filter((d) => d > 1)),
      right: Math.round(el.getBoundingClientRect().right),
    }))
    const sb = await dlg.getByRole('button', { name: 'Enregistrer la borne' }).boundingBox()
    check('C10', m.over <= 1 && m.right <= w + 1, `${w}×${h}: no sideways overflow`, JSON.stringify(m))
    check('C10', Boolean(sb) && sb.y + sb.height <= h && sb.x >= 0, `${w}×${h}: « Enregistrer la borne » on screen`, JSON.stringify(sb))
    const rows = await dlg.locator('label[for^="borne-device-"]').evaluateAll((els) => els.map((e) => Math.round(e.getBoundingClientRect().height)))
    check('C10', rows.length >= 2 && rows.every((x) => x >= 40), `${w}×${h}: each device row is a whole-row target`, JSON.stringify(rows))
    await shot(adm, `C10-add-borne-${w}x${h}`)
    await dlg.getByRole('button', { name: 'Retour' }).click()
    await adm.waitForTimeout(400)
  }
  await adm.setViewportSize({ width: 1000, height: 830 })
})

// ================================================================ summary
console.log('\n================ SUMMARY')
const pass = results.filter((r) => r.outcome === 'pass').length
const fail = results.filter((r) => r.outcome === 'fail').length
const skipped = results.filter((r) => r.outcome === 'skip').length
console.log(`pass ${pass} · fail ${fail} · skip ${skipped}`)
findings.forEach((f) => console.log('  ❌', f.id, '—', f.m, f.d ? `(${f.d})` : ''))
fs.writeFileSync(path.join(HERE, 'last-run.json'), JSON.stringify({ at: new Date().toISOString(), results }, null, 2))
if (BS) await BS.ctx.close().catch(() => {})
await A.browser.close().catch(() => {})
await S.browser.close().catch(() => {})
process.exit(0)
