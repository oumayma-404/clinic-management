// QA walk — congés des secrétaires. One launch, every scenario of qa/plan.md, findings collected, nothing fixed.
// Two VISIBLE Chrome windows (admin left, secretary right), each its own Chrome over CDP so both stay open at the end.
// Stack: API :5096 + web :3096 on DB clinic_staffleave_qa (isolated). Re-runnable: it resets its own artefacts first.
//   node walk.mjs            (PW_DIR = folder holding node_modules/playwright-core)
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
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe'
const CLINIC = 'f64a8a75-94e1-4e08-a743-fa231abe438f'
const PASSWORD = 'QaAudit2026!y'
const ADMIN = { email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', totp: '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO', name: 'Dr Salma Ben Youssef' }
const INES = { email: 'qa.secretary@ibnkhaldoun.test', id: 'local|22222222-2222-4222-8222-222222222222', name: 'Ines Gharbi' }
const OTHER = { id: 'local|8592a31c-22fa-436f-92a5-67bd41e021cf', name: 'QA Secrétaire 991861' }
const SHOTS = path.join(HERE, 'shots')
const DB = process.env.QA_DB ?? 'clinic_staffleave_qa'
// The two Chromes; another pair (QA_PORTS=9335,9336) leaves a demo's windows on 9333/9334 alone.
const [A_PORT, S_PORT] = (process.env.QA_PORTS ?? '9333,9334').split(',').map(Number)
fs.mkdirSync(SHOTS, { recursive: true })

// ---------------------------------------------------------------- findings
const findings = []
const results = []
const ok = (id, m) => { console.log(`  ✅ [${id}] ${m}`); results.push({ id, outcome: 'pass', m }) }
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }); results.push({ id, outcome: 'fail', m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); results.push({ id, outcome: 'skip', m: why }) }
const check = (id, cond, m, d = '') => (cond ? ok(id, m) : bad(id, m, d))

// ONLY=D3 re-drives one row against the state the last full run left (a probe fix, same run — no reset).
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

const bodyText = (page) => page.locator('body').innerText().catch(() => '')
async function waitText(page, re, timeout = 10000) {
  const end = Date.now() + timeout
  let last = ''
  while (Date.now() < end) {
    last = await bodyText(page)
    if (re.test(last)) return true
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
const shot = (page, name) => page.screenshot({ path: path.join(SHOTS, `${name}.png`) }).catch(() => {})
// A relevé line (« solde 21,5 j ») is not a request card — the « Droit à congé » relevé shares the page (run 6).
const RELEVE_LINE = /solde -?[\d,]+ j/
const card = (page, re) => page.locator('li:visible').filter({ hasText: re }).filter({ hasNotText: RELEVE_LINE }).first()
const sectionCards = (page, heading) =>
  page.locator('section', { has: page.locator(`h2:has-text("${heading}")`) }).locator('li')

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
  // The post-visit review popup swallows clicks; serve its queue empty.
  await context.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
  const page = context.pages().find((p) => !p.url().startsWith('devtools')) ?? (await context.newPage())
  return { browser, context, page }
}

async function signIn(page, who, withTotp) {
  await page.goto(`${WEB}/settings`, { waitUntil: 'domcontentloaded' })
  await page.waitForTimeout(1500)
  if (!page.url().includes('/login')) return
  await page.fill('input[type=email]', who.email)
  await page.fill('input[type=password]', PASSWORD)
  await page.click("button:has-text('Se connecter')")
  if (withTotp) {
    await page.waitForSelector('input[inputmode=numeric]', { timeout: 20000 })
    await page.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
    await page.fill('input[inputmode=numeric]', totp(who.totp))
  }
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60000 })
}

async function apiToken(email) {
  const r = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password: PASSWORD }) })
  return (await r.json()).value.accessToken
}

async function mails(to, since) {
  const r = await fetch(`http://localhost:8025/api/v1/search?query=${encodeURIComponent(`to:${to}`)}&limit=50`)
  const j = await r.json()
  const out = []
  for (const m of j.messages ?? []) {
    if (new Date(m.Created) < since) continue
    const full = await (await fetch(`http://localhost:8025/api/v1/message/${m.ID}`)).json()
    out.push({ subject: m.Subject, text: full.Text ?? '' })
  }
  return out
}

// Fill the « Demander un congé » / « Ajouter une absence » form (already open).
async function fillLeave(page, { type, start, end, half, reason }) {
  const dlg = openDialog(page)
  if (type) await dlg.getByRole('radio', { name: type, exact: true }).click()
  await dlg.locator('#staff-leave-start').fill(start)
  await dlg.locator('#staff-leave-end').fill(end ?? start)
  if (half) await dlg.locator('#staff-leave-half').click()
  if (reason !== undefined) await dlg.locator('#staff-leave-reason').fill(reason)
}
async function previewText(page) {
  const status = openDialog(page).locator('[role="status"]').first()
  await status.waitFor({ state: 'visible', timeout: 6000 }).catch(() => {})
  await page.waitForTimeout(700) // the debounced read may still be landing
  return (await status.innerText().catch(() => '')).replace(/\s+/g, ' ').trim()
}
async function closeDialogDiscarding(page) {
  await openDialog(page).getByRole('button', { name: 'Annuler', exact: true }).click().catch(() => page.keyboard.press('Escape'))
  const discard = page.getByRole('alertdialog').getByRole('button', { name: 'Abandonner' })
  if (await discard.isVisible().catch(() => false)) await discard.click()
  await page.waitForTimeout(300)
}
// The post-visit « Séance terminée » prompt is modal (the rest of the page goes aria-hidden) — a real visit of the
// copied DB can raise it mid-walk despite the route above. Answer « Plus tard », which writes nothing.
async function dismissReview(page) {
  const later = page.getByRole('dialog').filter({ hasText: 'Séance terminée' }).getByRole('button', { name: 'Plus tard' })
  if (await later.isVisible().catch(() => false)) {
    console.log('     (post-visit prompt answered « Plus tard »)')
    await later.click()
    await page.waitForTimeout(400)
  }
}
async function askLeave(sec, data) {
  await sec.getByRole('button', { name: 'Demander un congé' }).first().click()
  await openDialog(sec).locator('#staff-leave-start').waitFor()
  await fillLeave(sec, data)
}

// ================================================================ run
const runStart = new Date(Date.now() - 2000)
if (!ONLY) console.log(`Resetting this walk's own artefacts on ${DB}…`)
if (!ONLY) psql(`delete from "StaffLeaveAllowanceEntries" where "ClinicId" = '${CLINIC}'`)
if (!ONLY) psql(`delete from "NotificationReads" where "NotificationId" in (select "Id" from "StaffNotifications" where "Category" between 20 and 23);
      delete from "NotificationDismissals" where "NotificationId" in (select "Id" from "StaffNotifications" where "Category" between 20 and 23);
      delete from "StaffNotifications" where "Category" between 20 and 23;
      delete from "StaffLeaveRequests";
      update "Clinics" set "LeaveRequestsEnabled" = false where "Id" = '${CLINIC}';`)

const A = await chromeOn(A_PORT, `chrome-admin-${A_PORT}`, 0, 0, 1000, 830)
const S = await chromeOn(S_PORT, `chrome-secr-${S_PORT}`, 990, 0, 560, 830)
const adm = A.page
const sec = S.page
// main's « Une semaine avec APEXA » corner card (fixed, bottom-right, after a minute) sits over whatever is beneath it.
const guardFeedback = (page) =>
  page.addLocatorHandler(page.locator('[aria-labelledby="feedback-prompt-title"]'), async (card) => {
    await card.getByRole('button', { name: 'Plus tard' }).click()
  })
await guardFeedback(adm)
await guardFeedback(sec)
await signIn(adm, ADMIN, true)
await signIn(sec, INES, false)
console.log('Signed in — admin:', adm.url(), ' secretary:', sec.url())

const inesFile = `${WEB}/users/${encodeURIComponent(INES.id)}`
const otherFile = `${WEB}/users/${encodeURIComponent(OTHER.id)}`
const isoOf = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
const NOW_D = new Date()
const ABS_START_D = new Date(NOW_D.getFullYear(), NOW_D.getMonth(), NOW_D.getDate() - 2)
const ABS_START = isoOf(ABS_START_D)
const ABS_END = isoOf(NOW_D)
const MONTHS_FR = ['janvier', 'février', 'mars', 'avril', 'mai', 'juin', 'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre']
const ABS_LABEL = ABS_START_D.getMonth() === NOW_D.getMonth()
  ? `${ABS_START_D.getDate()} → ${NOW_D.getDate()} ${MONTHS_FR[NOW_D.getMonth()]}`
  : `${ABS_START_D.getDate()} ${MONTHS_FR[ABS_START_D.getMonth()].slice(0, 4)}`
const leaveId = (start) => psql(`select "Id" from "StaffLeaveRequests" where "UserId"='${INES.id}' and "StartDate"='${start}' order by "CreatedAt" desc limit 1`)

// ---------------------------------------------------------------- Tier A
// run 10: warm every route the walk opens — a fresh `next dev` compiles each on first visit, past a row's 8 s wait.
if (!ONLY) {
  for (const route of ['/', '/settings', '/users', '/users?onglet=conges', inesFile, otherFile]) {
    await adm.goto(`${WEB}${route.startsWith('http') ? route.slice(WEB.length) : route}`, { waitUntil: 'load', timeout: 120000 }).catch(() => {})
    await adm.waitForTimeout(1500)
  }
  for (const route of ['/conges', '/settings']) {
    await sec.goto(`${WEB}${route}`, { waitUntil: 'load', timeout: 120000 }).catch(() => {})
    await sec.waitForTimeout(1500)
  }
}

await scenario('A1', async () => {
  await adm.goto(`${WEB}/settings`)
  const sw = adm.locator('#team-leave-requests')
  await sw.waitFor({ timeout: 20000 })
  await sw.scrollIntoViewIfNeeded()
  check('A1', (await sw.getAttribute('aria-checked')) === 'false', 'switch « Demandes de congé des secrétaires » is off by default', await sw.getAttribute('aria-checked'))
  await shot(adm, 'A1-settings-off')
})

await scenario('A2', async () => {
  await sec.goto(`${WEB}/conges`)
  await waitText(sec, /pas activées|Mes congés/, 15000)
  const entries = await sec.locator('a[href="/conges"]').count()
  check('A2', entries === 0, 'no « Congés » menu entry for the secretary while off', `entries=${entries}`)
  check('A2', await waitText(sec, /Les demandes de congé ne sont pas activées/, 5000), '/conges says the module is off')
  await adm.goto(`${WEB}/users`)
  await waitText(adm, /Utilisateurs/, 15000)
  await adm.waitForTimeout(1500)
  const tabs = await adm.getByRole('tab', { name: /Congés/ }).count()
  check('A2', tabs === 0, '/users has no « Congés » tab while off', `tabs=${tabs}`)
})

await scenario('A3', async () => {
  await adm.goto(`${WEB}/settings`)
  const sw = adm.locator('#team-leave-requests')
  await sw.waitFor()
  await sw.scrollIntoViewIfNeeded()
  await sw.click()
  check('A3', Boolean(await toast(adm, /Demandes de congé activées/)), 'toast « Demandes de congé activées. »')
  // AC-13: the secretary's open page follows without a reload.
  const end = Date.now() + 10000
  let entries = 0
  while (Date.now() < end && (entries = await sec.locator('a[href="/conges"]').count()) === 0) await sleep(250)
  check('A3', entries > 0, 'secretary menu gains « Congés » without a reload', `entries=${entries}`)
  check('A3', await waitText(sec, /Demander un congé/, 10000), 'secretary /conges turns into her page without a reload')
  await shot(sec, 'A3-secretary-on')
})

await scenario('A4', async () => {
  await adm.goto(inesFile)
  await waitText(adm, /Ines Gharbi/, 15000)
  // droit-conges-blueprint: « Droit annuel » is a rule now — 24 j par an from 1 January of this year.
  await adm.getByRole('button', { name: 'Définir la règle' }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#leave-rule-days').fill('24')
  await dlg.locator('#leave-rule-since').click()
  await adm.getByRole('option', { name: new RegExp(`^1er janvier ${new Date().getFullYear()}$`) }).click()
  await dlg.getByRole('button', { name: 'Enregistrer la règle' }).click()
  check('A4', Boolean(await toast(adm, /Règle enregistrée/)), 'toast « Règle enregistrée »')
  check('A4', await waitText(adm, /24\s*jours restants[\s\S]*Acquis\s*24 j[\s\S]*Pris\s*0 j/), 'file reads « 24 jours restants », Acquis 24 j, Pris 0 j')
  check('A4', await waitText(sec, /24\s*jours restants/, 10000), "secretary's balance follows without a reload (24 jours restants)")
  await shot(adm, 'A4-file')
})

// ---------------------------------------------------------------- Tier B on the form, then A5
await scenario('B4', async () => {
  await sec.goto(`${WEB}/conges`)
  await waitText(sec, /Demander un congé/, 15000)
  await askLeave(sec, { start: '2026-11-16', end: '2026-11-18' })
  const withRange = await openDialog(sec).locator('#staff-leave-half').count()
  await openDialog(sec).locator('#staff-leave-end').fill('2026-11-16')
  await sec.waitForTimeout(200)
  const single = await openDialog(sec).locator('#staff-leave-half').count()
  check('B4', withRange === 0 && single === 1, '« Demi-journée » only when Du = Au', `range=${withRange} single=${single}`)
})

await scenario('B1', async () => {
  const dlg = openDialog(sec)
  await dlg.locator('#staff-leave-reason').fill('Test')
  await dlg.locator('#staff-leave-start').fill('2026-11-20')
  await dlg.locator('#staff-leave-end').fill('2026-11-18')
  await dlg.getByRole('button', { name: 'Envoyer la demande' }).click()
  check('B1', await waitText(sec, /Le dernier jour doit être le même jour ou après le premier/, 4000), '« Au » before « Du » refused on the field')
  await shot(sec, 'B1-range')
})

await scenario('B2', async () => {
  const dlg = openDialog(sec)
  await dlg.locator('#staff-leave-start').fill('2026-11-20')
  await dlg.locator('#staff-leave-end').fill('2027-11-22')
  await dlg.getByRole('button', { name: 'Envoyer la demande' }).click()
  check('B2', await waitText(sec, /ne peut pas dépasser 366 jours/, 4000), 'more than 366 days refused')
})

await scenario('B3', async () => {
  const dlg = openDialog(sec)
  await dlg.locator('#staff-leave-start').fill('2026-11-16')
  await dlg.locator('#staff-leave-end').fill('2026-11-18')
  await dlg.locator('#staff-leave-reason').fill('')
  await dlg.getByRole('button', { name: 'Envoyer la demande' }).click()
  const said = await waitText(sec, /Indiquez le motif\./, 4000)
  const stillOpen = await openDialog(sec).isVisible()
  check('B3', said && stillOpen, 'empty motif refused, dialog stays open')
})

await scenario('A5', async () => {
  const dlg = openDialog(sec)
  await dlg.locator('#staff-leave-reason').fill('Mariage de ma sœur')
  const preview = await previewText(sec)
  check('A5', /^3 jours/.test(preview) && /Il restera 21 jours/.test(preview), 'form states « 3 jours … Il restera 21 jours » before sending', `got « ${preview} »`)
  await shot(sec, 'A5-form')
  await dlg.getByRole('button', { name: 'Envoyer la demande' }).click()
  check('A5', Boolean(await toast(sec, /Demande envoyée/)), 'toast « Demande envoyée »')
  const c = card(sec, /16 → 18 novembre/)
  await c.waitFor({ timeout: 8000 })
  check('A5', /En attente/.test(await c.innerText()), 'her row reads « 16 → 18 novembre · En attente »', await c.innerText())
})

await scenario('A6', async () => {
  const rows = psql(`select "TargetRole" || ' | ' || coalesce("TargetUserId",'-') || ' | ' || "Message" from "StaffNotifications" where "Category" = 20`)
  const lines = rows ? rows.split('\n') : []
  check('A6', lines.length === 1 && /^admin \| - \| Ines Gharbi demande un congé du 16\/11 au 18\/11 \(3 jours\)\.$/.test(lines[0]),
    'exactly one admin-role notification, worded as AC-5', JSON.stringify(lines))
})

await scenario('A7', async () => {
  await adm.goto(`${WEB}/`)
  await adm.waitForTimeout(2500)
  const railBadge = (await adm.locator('a[href="/users"]:visible').first().innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('A7', /\b1\b/.test(railBadge), '« Utilisateurs » rail entry carries the pending count 1', `« ${railBadge} »`)
  await adm.getByRole('button', { name: 'Notifications' }).click()
  const row = adm.locator('li').filter({ hasText: 'Ines Gharbi demande un congé' }).first()
  await row.waitFor({ timeout: 8000 })
  const text = (await row.innerText()).replace(/\s+/g, ' ')
  check('A7', /Demande de congé/.test(text), 'bell row « Demande de congé » with the request sentence', text)
  await shot(adm, 'A7-bell')
  await row.locator('button').first().click()
  await adm.waitForURL(/\/users\?onglet=conges/, { timeout: 8000 }).catch(() => {})
  check('A7', /\/users\?onglet=conges/.test(adm.url()), 'clicking it lands on /users?onglet=conges', adm.url())
  const tab = adm.getByRole('tab', { name: /Congés/ })
  await tab.waitFor({ timeout: 8000 })
  // The badge is its own read, landing a moment after the tab — poll rather than read once.
  let tabText = ''
  for (let i = 0; i < 24 && !/1/.test(tabText); i++) {
    tabText = (await tab.innerText()).replace(/\s+/g, ' ')
    if (!/1/.test(tabText)) await sleep(250)
  }
  check('A7', (await tab.getAttribute('aria-selected')) === 'true' && /1/.test(tabText), '« Congés » tab selected, badge 1', `« ${tabText} »`)
})

await scenario('A8', async () => {
  const c = sectionCards(adm, 'À décider').filter({ hasText: /16 → 18 novembre/ }).first()
  await c.waitFor({ timeout: 8000 })
  const t = (await c.innerText()).replace(/\s+/g, ' ')
  check('A8', /Ines Gharbi/.test(t) && /Congé payé/.test(t) && /Jours 3 jours/.test(t) && /Mariage de ma sœur/.test(t) && /Solde après 21 jours/.test(t),
    'decision card: name, type, « Jours 3 jours », motif, « Solde après 21 jours »', t)
  await shot(adm, 'A8-card')
})

await scenario('A9', async () => {
  const c = sectionCards(adm, 'À décider').filter({ hasText: /16 → 18 novembre/ }).first()
  await c.getByRole('button', { name: 'Accepter' }).click()
  const days = openDialog(adm).locator('#accept-leave-days')
  await days.waitFor()
  check('A9', (await days.inputValue()) === '3', '« Décompter » prefilled with 3', await days.inputValue())
  await days.fill('2,5')
  await openDialog(adm).getByRole('button', { name: 'Accepter', exact: true }).click()
  check('A9', Boolean(await toast(adm, /Congé accepté/)), 'toast « Congé accepté »')
  const done = sectionCards(adm, 'Toutes les demandes').filter({ hasText: /16 → 18 novembre/ }).first()
  await done.waitFor({ timeout: 8000 })
  const t = (await done.innerText()).replace(/\s+/g, ' ')
  check('A9', /2,5 jours décomptés \(3 jours demandés\)/.test(t) && /Accepté par Dr Salma Ben Youssef/.test(t), 'moved to « Toutes les demandes » with 2,5 décomptés and who decided', t)
  check('A9', psql(`select "CountedDays"::text from "StaffLeaveRequests" where "StartDate"='2026-11-16'`) === '2.500', 'stored CountedDays = 2.5')
})

await scenario('A10', async () => {
  const c = card(sec, /16 → 18 novembre/)
  const end = Date.now() + 10000
  let t = ''
  while (Date.now() < end && !/Accepté/.test((t = await c.innerText().catch(() => '')))) await sleep(250)
  check('A10', /Accepté/.test(t), 'her card turns « Accepté » without a reload', t.replace(/\s+/g, ' '))
  check('A10', await waitText(sec, /21,5\s*jours restants/, 6000), 'balance « 21,5 jours restants »')
  await sec.getByRole('button', { name: 'Notifications' }).click()
  const row = sec.locator('li').filter({ hasText: 'Congé accepté' }).first()
  check('A10', await row.waitFor({ timeout: 8000 }).then(() => true, () => false), 'her bell has « Congé accepté »')
  await shot(sec, 'A10-bell')
  await sec.keyboard.press('Escape')
})

await scenario('A11', async () => {
  await sleep(1500)
  const m = (await mails(INES.email, runStart)).filter((x) => /Congé accepté/.test(x.subject))
  check('A11', m.length === 1, 'one acceptance e-mail', `count=${m.length}`)
  if (m[0]) check('A11', /du 16\/11 au 18\/11/.test(m[0].subject) && /2,5 jours/.test(m[0].text), 'subject names the dates, body the 2,5 jours', `${m[0].subject}`)
  // run 9: what is left, from the « Droit à congé » fold — 24 j par an − these 2,5 days
  if (m[0]) check('A11', /Congé payé restant en 2026\s*:?\s*21,5 jours/.test(m[0].text), 'the e-mail states « Congé payé restant en 2026 : 21,5 jours »', m[0].text.replace(/\s+/g, ' ').slice(0, 400))
})

await scenario('B5', async () => {
  await askLeave(sec, { start: '2026-11-17', reason: 'Chevauchement' })
  await openDialog(sec).getByRole('button', { name: 'Envoyer la demande' }).click()
  check('B5', await waitText(sec, /Vous avez déjà une demande du 16\/11 au 18\/11\./, 6000), 'overlap refused naming the other request')
  await shot(sec, 'B5-overlap')
  await closeDialogDiscarding(sec)
})

await scenario('A12', async () => {
  await askLeave(sec, { type: 'Maladie', start: '2026-11-25', half: true, reason: 'Rendez-vous médical' })
  const preview = await previewText(sec)
  check('A12', /^0,5 jour\b/.test(preview) && /demi-journée/.test(preview), 'half day states « 0,5 jour … demi-journée »', `got « ${preview} »`)
  await openDialog(sec).getByRole('button', { name: 'Envoyer la demande' }).click()
  await toast(sec, /Demande envoyée/)
  const c = sectionCards(adm, 'À décider').filter({ hasText: /25 novembre/ }).first()
  await c.waitFor({ timeout: 10000 })
  await c.getByRole('button', { name: 'Refuser' }).click()
  await openDialog(adm).locator('#reject-leave-reason').waitFor()
  await openDialog(adm).getByRole('button', { name: 'Refuser la demande' }).click()
  check('A12', await waitText(adm, /Indiquez le motif du refus\./, 4000), 'empty refusal motif refused on the field')
  await shot(adm, 'A12-reject-empty')
  await openDialog(adm).locator('#reject-leave-reason').fill('Période chargée')
  await openDialog(adm).getByRole('button', { name: 'Refuser la demande' }).click()
  check('A12', Boolean(await toast(adm, /Demande refusée/)), 'toast « Demande refusée »')
})

await scenario('A13', async () => {
  const c = card(sec, /Mercredi 25 novembre/)
  const end = Date.now() + 10000
  let t = ''
  while (Date.now() < end && !/Refusé/.test((t = await c.innerText().catch(() => '')))) await sleep(250)
  t = t.replace(/\s+/g, ' ')
  check('A13', /Refusé/.test(t) && /Motif du refus : « ?Période chargée ?»/.test(t), 'her card « Refusé » quoting the motif', t)
  await sleep(1000)
  const m = (await mails(INES.email, runStart)).filter((x) => /Congé refusé/.test(x.subject))
  check('A13', m.length === 1 && /Période chargée/.test(m[0]?.text ?? ''), 'refusal e-mail quotes the motif word for word', m[0]?.subject ?? 'none')
})

await scenario('A14', async () => {
  await adm.goto(otherFile)
  await waitText(adm, /QA Secrétaire 991861/, 15000)
  await adm.getByRole('button', { name: 'Ajouter une absence' }).click()
  await openDialog(adm).locator('#staff-leave-start').waitFor()
  await fillLeave(adm, { start: '2026-12-30', end: '2026-12-31', reason: 'Congé annuel' })
  await openDialog(adm).getByRole('button', { name: "Enregistrer l'absence" }).click()
  check('A14', Boolean(await toast(adm, /Absence enregistrée/)), 'toast « Absence enregistrée »')
  const c = card(adm, /30 → 31 décembre/)
  await c.waitFor({ timeout: 8000 })
  const t = (await c.innerText()).replace(/\s+/g, ' ')
  check('A14', /Accepté/.test(t) && /Saisi par l'admin/.test(t), 'her file: « Accepté » + « Saisi par l\'admin »', t)
  const n = psql(`select count(*) from "StaffNotifications" where "Category"=21 and "TargetUserId"='${OTHER.id}'`)
  check('A14', n === '1', 'she is notified (one row addressed to her)', `rows=${n}`)
})

let r3Preview = ''
await scenario('A15', async () => {
  await askLeave(sec, { start: '2026-12-30', end: '2027-01-02', reason: "Fêtes de fin d'année" })
  r3Preview = await previewText(sec)
  check('A15', /^4 jours/.test(r3Preview), 'form states « 4 jours » for 30 déc → 2 janv', `got « ${r3Preview} »`)
  check('A15', /Il restera 19,5 jours/.test(r3Preview), 'balance after counts only the 2026 share (24 − 2,5 − 2 = 19,5), per AC-12', `got « ${r3Preview} »`)
  await openDialog(sec).getByRole('button', { name: 'Envoyer la demande' }).click()
  await toast(sec, /Demande envoyée/)
  await adm.goto(`${WEB}/users?onglet=conges`)
  const c = sectionCards(adm, 'À décider').filter({ hasText: /30 déc. → 2 janv/ }).first()
  await c.waitFor({ timeout: 10000 })
  const t = (await c.innerText()).replace(/\s+/g, ' ')
  check('A15', /Déjà en congé sur ces dates : QA Secrétaire 991861/.test(t), 'admin card names who else is off', t)
  check('A15', /Solde après 19,5 jours/.test(t), 'admin card « Solde après » counts only the 2026 share', t)
  await shot(adm, 'A15-others-off')
})

await scenario('A16', async () => {
  const c = sectionCards(adm, 'À décider').filter({ hasText: /30 déc. → 2 janv/ }).first()
  await c.getByRole('button', { name: 'Accepter' }).click()
  await openDialog(adm).locator('#accept-leave-days').waitFor()
  await openDialog(adm).getByRole('button', { name: 'Accepter', exact: true }).click()
  await toast(adm, /Congé accepté/)
  // Run 6: both congés are still to come today, so they read « Prévus », not « Pris ».
  check('A16', await waitText(sec, /19,5\s*jours restants[\s\S]*Prévus\s*4,5 j/, 10000), 'her 2026 balance: « 19,5 jours restants », Prévus 4,5 j',
    ((await bodyText(sec)).match(/[\d,]+\s*jours? restants[\s\S]{0,80}/) ?? [''])[0].replace(/\s+/g, ' '))
})

await scenario('B6', async () => {
  // Standalone (ONLY=B6) the two windows start on /settings.
  if (!sec.url().endsWith('/conges')) { await sec.goto(`${WEB}/conges`); await waitText(sec, /Demander un congé/, 15000) }
  if (!adm.url().includes('onglet=conges')) await adm.goto(`${WEB}/users?onglet=conges`)
  // R5 first, so the « À décider » list stays mounted when R4 disappears from it.
  await askLeave(sec,{ type: 'Autre', start: '2026-12-14', reason: 'Déménagement' })
  await openDialog(sec).getByRole('button', { name: 'Envoyer la demande' }).click()
  await toast(sec, /Demande envoyée/)
  await askLeave(sec, { start: '2026-12-07', end: '2026-12-08', reason: 'Voyage' })
  await openDialog(sec).getByRole('button', { name: 'Envoyer la demande' }).click()
  await toast(sec, /Demande envoyée/)
  const r4 = sectionCards(adm, 'À décider').filter({ hasText: /7 → 8 décembre/ }).first()
  await r4.waitFor({ timeout: 10000 })
  await r4.getByRole('button', { name: 'Accepter' }).click()
  await openDialog(adm).locator('#accept-leave-days').waitFor()
  // Meanwhile, she withdraws it.
  await card(sec, /7 → 8 décembre/).getByRole('button', { name: /Annuler le congé/ }).click()
  await sec.getByRole('alertdialog').getByRole('button', { name: 'Annuler le congé' }).click()
  await toast(sec, /Congé annulé/)
  await adm.waitForTimeout(1500)
  const stillOpen = await adm.locator('#accept-leave-days').isVisible()
  if (!stillOpen) { bad('B6', 'the admin\'s Accepter dialog vanished before it could be pressed'); return }
  await openDialog(adm).getByRole('button', { name: 'Accepter', exact: true }).click()
  const banner = await waitText(adm, /Cette demande a déjà reçu une réponse\./, 6000)
  const reload = openDialog(adm).getByRole('button', { name: 'Recharger' })
  check('B6', banner && (await reload.isVisible()), 'banner « Cette demande a déjà reçu une réponse. » with « Recharger »')
  await shot(adm, 'B6-conflict')
  await reload.click()
  const t0 = Date.now()
  let dialogGone = false
  while (Date.now() - t0 < 5000 && !(dialogGone = !(await adm.locator('#accept-leave-days').isVisible()))) await sleep(100)
  console.log(`     (dialog closed after ${Date.now() - t0} ms: ${dialogGone})`)
  const r4done = sectionCards(adm, 'Toutes les demandes').filter({ hasText: /7 → 8 décembre/ }).first()
  const t = await r4done.innerText().catch(() => '')
  check('B6', dialogGone && /Annulé/.test(t), '« Recharger » closes and R4 reads « Annulé »', t.replace(/\s+/g, ' '))
  check('B6', psql(`select "Status" from "StaffLeaveRequests" where "StartDate"='2026-12-07'`) === '3', 'stored status Cancelled, no decision written')
})

await scenario('A17', async () => {
  const r5 = sectionCards(adm, 'À décider').filter({ hasText: /14 décembre/ }).first()
  await r5.getByRole('button', { name: 'Accepter' }).click()
  await openDialog(adm).locator('#accept-leave-days').waitFor()
  await openDialog(adm).getByRole('button', { name: 'Accepter', exact: true }).click()
  await toast(adm, /Congé accepté/)
  const c = card(sec, /Lundi 14 décembre/)
  const end = Date.now() + 10000
  while (Date.now() < end && !/Accepté/.test(await c.innerText().catch(() => ''))) await sleep(250)
  await c.getByRole('button', { name: /Annuler le congé/ }).click()
  const confirm = sec.getByRole('alertdialog')
  const ct = (await confirm.innerText()).replace(/\s+/g, ' ')
  check('A17', /Annuler le congé le 14 décembre 2026 \?/.test(ct) && /Ce congé était accepté\./.test(ct), 'confirm names the congé and says it was accepted', ct)
  await shot(sec, 'A17-confirm')
  await confirm.getByRole('button', { name: 'Annuler le congé' }).click()
  check('A17', Boolean(await toast(sec, /Congé annulé/)), 'toast « Congé annulé »')
  // The row moves from « à venir » to « Historique » after the refetch — poll, it is a new element.
  check('A17', await waitText(sec, /Lundi 14 décembre\s*Annulé/, 8000), 'her row reads « Lundi 14 décembre · Annulé »')
  let n = ''
  for (let i = 0; i < 20 && !/a annulé/.test(n); i++) {
    n = psql(`select "Message" from "StaffNotifications" where "Category"=23 and "TargetRole"='admin' order by "CreatedAt" desc limit 1`)
    if (!/a annulé/.test(n)) await sleep(250)
  }
  check('A17', /Ines Gharbi a annulé son congé le 14\/12\./.test(n), 'admins notified of the cancellation', n)
  await adm.getByRole('button', { name: 'Notifications' }).click()
  check('A17', await adm.locator('li').filter({ hasText: 'a annulé son congé' }).first().waitFor({ timeout: 8000 }).then(() => true, () => false), 'admin bell shows « Congé annulé »')
  await adm.keyboard.press('Escape')
})

await scenario('A18', async () => {
  await adm.goto(inesFile)
  await waitText(adm, /Ines Gharbi/, 15000)
  await adm.getByRole('button', { name: 'Ajouter une absence' }).click()
  await openDialog(adm).locator('#staff-leave-start').waitFor()
  await fillLeave(adm, { type: 'Sans solde', start: ABS_START, end: ABS_END, reason: 'Absence non prévenue' })
  await openDialog(adm).getByRole('button', { name: "Enregistrer l'absence" }).click()
  check('A18', Boolean(await toast(adm, /Absence enregistrée/)), 'toast « Absence enregistrée »')
  const c = card(sec, new RegExp(ABS_LABEL))
  await c.waitFor({ timeout: 10000 })
  const t = (await c.innerText()).replace(/\s+/g, ' ')
  const cancels = await c.getByRole('button', { name: /Annuler/ }).count()
  check('A18', /Saisi par l'admin/.test(t) && cancels === 0, 'her card: « Saisi par l\'admin », no « Annuler » on a started congé', `${t} · cancel buttons=${cancels}`)
  const token = await apiToken(INES.email)
  const id = leaveId(ABS_START)
  const r = await fetch(`${API}/team/leave-requests/${id}/cancel`, { method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, body: JSON.stringify({ version: 0 }) })
  const body = await r.text()
  check('A18', r.status >= 400 && /commencé/.test(body), 'the API refuses to cancel a started congé', `${r.status} ${body.slice(0, 120)}`)
  const n = psql(`select count(*) from "StaffNotifications" where "Category"=21 and "TargetUserId"='${INES.id}' and "Message" like 'Une absence%'`)
  check('A18', n === '1', 'she is notified of the absence', `rows=${n}`)
})

// A pending request for the device checks and for the end state.
await scenario('setup-R6', async () => {
  await askLeave(sec, { start: '2026-12-21', reason: 'Rendez-vous administratif' })
  await openDialog(sec).getByRole('button', { name: 'Envoyer la demande' }).click()
  check('setup-R6', Boolean(await toast(sec, /Demande envoyée/)), 'R6 (21 déc) left pending for the device checks')
})

// ---------------------------------------------------------------- Redesign surfaces (2026-11-05)
await scenario('A19', async () => {
  await adm.goto(`${WEB}/users?onglet=conges`)
  const team = adm.locator('section', { has: adm.locator('h2:has-text("Équipe")') })
  await team.waitFor({ timeout: 15000 })
  const t = (await team.innerText()).replace(/\s+/g, ' ')
  check('A19', /Ines Gharbi 19,5 \/ 24 j/.test(t) && new RegExp(`Prochain congé : ${ABS_LABEL}`).test(t), 'team strip: Ines « 19,5 / 24 j », next congé named', t)
  check('A19', /QA Secrétaire 991861 2 jours prévus/.test(t) && /Prochain congé : 30 → 31 décembre/.test(t), 'team strip: a secretary with no droit shows what she took or has planned', t)
  await shot(adm, 'A19-team')
})

await scenario('A20', async () => {
  const history = adm.locator('section', { has: adm.locator('h2:has-text("Toutes les demandes")') })
  await history.getByRole('radio', { name: 'Refusées' }).click()
  await adm.waitForTimeout(1500)
  const rows = history.locator('li:visible')
  const n = await rows.count()
  const t = n ? (await rows.first().innerText()).replace(/\s+/g, ' ') : ''
  check('A20', n === 1 && /25 novembre/.test(t) && /Refusé/.test(t), '« Refusées » lists only the refused request (server-side filter)', `rows=${n} · ${t}`)
  await history.getByRole('radio', { name: 'Toutes' }).click()
  await adm.waitForTimeout(1000)
  check('A20', (await history.locator('li:visible').count()) >= 6, '« Toutes » brings every decided request back')
})

await scenario('A21', async () => {
  await adm.goto(inesFile)
  const byType = adm.locator('section', { has: adm.locator('h2:has-text("Absences acceptées")') })
  await byType.waitFor({ timeout: 15000 })
  const t = (await byType.innerText()).replace(/\s+/g, ' ')
  check('A21', /Congé payé 4,5 j/.test(t) && /Sans solde 3 j/.test(t) && /Maladie 0 j/.test(t) && /Autre 0 j/.test(t),
    'file: accepted absences by type for 2026 (refused and cancelled ones not counted)', t)
  await shot(adm, 'A21-file')
})

await scenario('A22', async () => {
  await sec.goto(`${WEB}/conges`)
  const cal = sec.locator('table', { hasText: 'Congés de novembre 2026' })
  await cal.waitFor({ timeout: 15000 })
  const t = (await cal.innerText()).replace(/\s+/g, ' ')
  check('A22', /16 — congé accepté/.test(t) && /18 — congé accepté/.test(t) && !/25 — congé accepté/.test(t), 'month: accepted days marked, the refused 25 is not', t.slice(0, 200))
  // Run 6: today is read, not assumed (the walk was written on 5 November; the absence runs 4 → 6).
  const day = new Date().getDate()
  check('A22', new RegExp(`\\b${day} — congé accepté — aujourd'hui`).test(t), `month: today (${day}) ringed, inside the started absence`, t.slice(0, 260))
  check('A22', await waitText(sec, new RegExp(`Prochain congé\\s*${ABS_LABEL} · en cours`), 4000), '« Prochain congé » names the one under way')
  await shot(sec, 'A22-conges')
})

// ---------------------------------------------------------------- Tier C — devices
async function phoneTab(ctx, width, height, coarse) {
  const p = await ctx.newPage()
  const cdp = await ctx.newCDPSession(p)
  await cdp.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: coarse ? 3 : 1, mobile: coarse })
  if (coarse) await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 })
  await guardFeedback(p)
  return { p, cdp }
}
// `<main>` is the page scroller: the document never widens, so a sideways scroll lives in `main`, not on `html`.
const hScroll = (p) => p.evaluate(() => {
  const main = document.getElementById('contenu-principal')
  const page = document.documentElement.scrollWidth - window.innerWidth
  return Math.max(page, main ? main.scrollWidth - main.clientWidth : 0)
})

await scenario('C1', async () => {
  const { p } = await phoneTab(S.context, 320, 640, true)
  await p.goto(`${WEB}/conges`)
  await waitText(p, /Demander un congé/, 20000)
  await p.waitForTimeout(1500)
  check('C1', await p.evaluate(() => matchMedia('(pointer: coarse)').matches), 'coarse pointer emulated')
  const over = await hScroll(p)
  check('C1', over <= 0, 'no horizontal scroll at 320', `overflow=${over}px`)
  const btn = card(p, /Lundi 21 décembre/).getByRole('button', { name: /Annuler le congé/ })
  const h = (await btn.boundingBox())?.height ?? 0
  check('C1', h >= 43.5, '« Annuler » on a card ≥ 44 px on a coarse pointer', `${h}px`)
  await shot(p, 'C1-320-conges')
  // C2 — the request form as a bottom sheet.
  await p.getByRole('button', { name: 'Demander un congé' }).first().click()
  const dlg = openDialog(p)
  await dlg.locator('#staff-leave-start').waitFor()
  await p.waitForTimeout(600)
  const box = await dlg.boundingBox()
  const send = await dlg.getByRole('button', { name: 'Envoyer la demande' }).boundingBox()
  check('C2', box && Math.abs(box.y + box.height - 640) <= 2, 'request form is a bottom sheet at 320', JSON.stringify(box))
  check('C2', send && send.y + send.height <= 640 && send.y >= 0, '« Envoyer la demande » inside the viewport', JSON.stringify(send))
  await shot(p, 'C2-320-sheet')
  await p.keyboard.press('Escape')
  await p.close()
})

await scenario('C3', async () => {
  const { p } = await phoneTab(A.context, 320, 640, true)
  await p.goto(`${WEB}/users?onglet=conges`)
  const c = sectionCards(p, 'À décider').filter({ hasText: /21 décembre/ }).first()
  await c.waitFor({ timeout: 20000 })
  await p.waitForTimeout(1000)
  const over = await hScroll(p)
  check('C3', over <= 0, 'admin congés tab: no horizontal scroll at 320', `overflow=${over}px`)
  const acc = await c.getByRole('button', { name: 'Accepter' }).boundingBox()
  const ref = await c.getByRole('button', { name: 'Refuser' }).boundingBox()
  check('C3', acc && ref && acc.height >= 43.5 && ref.height >= 43.5 && acc.x + acc.width <= 320, 'Accepter / Refuser visible, ≥ 44 px', `acc=${JSON.stringify(acc)} ref=${JSON.stringify(ref)}`)
  await c.scrollIntoViewIfNeeded()
  await shot(p, 'C3-320-admin')
  await p.close()
})

await scenario('C4', async () => {
  const { p } = await phoneTab(A.context, 1536, 730, false)
  await p.goto(`${WEB}/users?onglet=conges`)
  const c = sectionCards(p, 'À décider').filter({ hasText: /21 décembre/ }).first()
  await c.waitFor({ timeout: 20000 })
  await c.getByRole('button', { name: 'Refuser' }).click()
  const b = await openDialog(p).getByRole('button', { name: 'Refuser la demande' }).boundingBox()
  check('C4', b && b.y + b.height <= 730, '« Refuser la demande » inside a 730 px tall window', JSON.stringify(b))
  await shot(p, 'C4-1536x730-reject')
  await p.keyboard.press('Escape')
  await p.close()
})

// ---------------------------------------------------------------- Review fixes (feature-review.md)
await scenario('F1', async () => {
  // iPad portrait with the rail open: the waiting card's three buttons stay inside the card.
  const { p } = await phoneTab(A.context, 820, 1024, true)
  await p.goto(`${WEB}/users?onglet=conges`)
  const c = sectionCards(p, 'À décider').filter({ hasText: /21 décembre/ }).first()
  await c.waitFor({ timeout: 20000 })
  await p.waitForTimeout(800)
  const cardBox = await c.boundingBox()
  const boxes = await Promise.all(['Annuler', 'Refuser', 'Accepter'].map((n) => c.getByRole('button', { name: new RegExp(`^${n}`) }).boundingBox()))
  const inside = cardBox && boxes.every((b) => b && b.x >= cardBox.x - 0.5 && b.x + b.width <= cardBox.x + cardBox.width + 0.5)
  check('F1', inside && (await hScroll(p)) <= 0, 'at 820: Annuler / Refuser / Accepter inside the card, no sideways scroll', JSON.stringify({ cardBox, boxes }))
  await shot(p, 'F1-820-cards')
  await p.close()
})

await scenario('F2', async () => {
  // A typed refusal motif asks before it is thrown away.
  const c = sectionCards(adm, 'À décider').filter({ hasText: /21 décembre/ }).first()
  if (!(await c.isVisible().catch(() => false))) { await adm.goto(`${WEB}/users?onglet=conges`); await c.waitFor({ timeout: 20000 }) }
  await c.getByRole('button', { name: /^Refuser/ }).click()
  await openDialog(adm).locator('#reject-leave-reason').fill('Période chargée')
  await adm.keyboard.press('Escape')
  const ask = adm.getByRole('alertdialog').filter({ hasText: 'Abandonner les modifications' })
  check('F2', await ask.isVisible({ timeout: 3000 }).catch(() => false), 'Escape on a typed motif asks « Abandonner les modifications ? »')
  await ask.getByRole('button', { name: 'Continuer la saisie' }).click()
  check('F2', (await openDialog(adm).locator('#reject-leave-reason').inputValue()) === 'Période chargée', 'the motif is still there after « Continuer la saisie »')
  await adm.keyboard.press('Escape')
  await adm.getByRole('alertdialog').getByRole('button', { name: /Abandonner/ }).last().click()
})

await scenario('F3', async () => {
  // The fixed « Demander un congé » never covers the last line of the page.
  const { p } = await phoneTab(S.context, 320, 640, true)
  await p.goto(`${WEB}/conges`)
  await waitText(p, /Demander un congé/, 20000)
  await p.waitForTimeout(1200)
  const m = await p.evaluate(() => {
    const main = document.getElementById('contenu-principal')
    main.scrollTop = main.scrollHeight
    const kids = [...main.querySelectorAll('section, aside, ul')].filter((e) => e.offsetParent !== null)
    const last = Math.max(...kids.map((e) => e.getBoundingClientRect().bottom))
    const cta = [...document.querySelectorAll('button')].filter((b) => /Demander un congé/.test(b.textContent) && b.offsetParent !== null)
      .map((b) => b.getBoundingClientRect()).sort((a, b) => b.top - a.top)[0]
    return { last, ctaTop: cta?.top ?? null }
  })
  check('F3', m.ctaTop !== null && m.last <= m.ctaTop + 0.5, 'scrolled to the bottom, the last block ends above the fixed button', JSON.stringify(m))
  await shot(p, 'F3-320-bottom')
  await p.close()
})

await scenario('F4', async () => {
  // A refusal tells her the days too (AC-9).
  const msg = psql(`select "Message" from "StaffNotifications" where "Category" = 22 order by "CreatedAt" desc limit 1`)
  // The walk's refusal is a half day: its period says « (demi-journée) », and the count is not repeated beside it.
  check('F4', /\((demi-journée|\d+(,\d)? jours?)\) est refusée/.test(msg) && !/\) \(/.test(msg), 'the « refusée » bell row names the days once', msg)
})

await scenario('G1', async () => {
  // main's « Votre secrétaire » tour lights « Créer un compte », which now lives in the « Comptes » tab.
  await adm.goto(`${WEB}/`)
  const go = adm.getByRole('button', { name: 'Me guider : Votre secrétaire' })
  if (!(await go.isVisible({ timeout: 15000 }).catch(() => false))) {
    skip('G1', '« Premiers pas » shows no « Votre secrétaire » step for this cabinet (already done or card hidden)')
    return
  }
  await dismissReview(adm)
  await go.click()
  const lit = await waitText(adm, /Ici, vous ajoutez votre secrétaire\./, 15000)
  const comptes = await adm.getByRole('tab', { name: 'Comptes', selected: true }).isVisible().catch(() => false)
  check('G1', lit && comptes, 'the tour opens /users on « Comptes » and lights « Créer un compte »', `lit=${lit} comptes=${comptes}`)
  await shot(adm, 'G1-guide')
  await adm.keyboard.press('Escape')
})

// ---------------------------------------------------------------- Tier D
await scenario('D1', async () => {
  for (const [w, h] of [[1440, 900], [820, 1024]]) {
    const { p } = await phoneTab(A.context, w, h, false)
    await p.goto(`${WEB}/users`)
    await p.getByRole('tab', { name: 'Comptes' }).waitFor({ timeout: 20000 })
    await waitText(p, /Ines Gharbi/, 15000)
    await p.waitForTimeout(800)
    let reach = await p.getByRole('link', { name: /Fiche et congés de Ines Gharbi/ }).isVisible().catch(() => false)
    if (!reach) {
      // Card form: the ⋯ menu on Ines's card.
      const row = p.locator(':is(li, [role="listitem"]):visible').filter({ hasText: 'Ines Gharbi' }).first()
      await row.getByRole('button').last().click().catch(() => {})
      reach = await p.getByRole('menuitem', { name: 'Voir sa fiche et ses congés' }).isVisible().catch(() => false)
    }
    const over = await hScroll(p)
    check('D1', reach && over <= 0, `« Comptes » at ${w}: accounts listed, Ines's file reachable, no horizontal scroll`, `reach=${reach} overflow=${over}`)
    await shot(p, `D1-${w}-comptes`)
    await p.close()
  }
})

await scenario('D2', async () => {
  for (const [w, h] of [[1440, 900], [390, 844]]) {
    const { p } = await phoneTab(A.context, w, h, w < 500)
    await p.goto(`${WEB}/settings`)
    const sw = p.locator('#team-leave-requests')
    await sw.waitFor({ timeout: 20000 })
    await sw.scrollIntoViewIfNeeded()
    const over = await hScroll(p)
    const box = await p.locator('div', { has: sw }).filter({ hasText: 'Équipe' }).last().boundingBox()
    check('D2', over <= 0 && box && box.x >= 0 && box.x + box.width <= w, `« Équipe » card fits at ${w}`, `overflow=${over} box=${JSON.stringify(box)}`)
    await shot(p, `D2-${w}-settings`)
    await p.close()
  }
})

await scenario('D3', async () => {
  await adm.goto(`${WEB}/`)
  await adm.waitForTimeout(2500)
  const expanded = (await adm.locator('a[href="/users"]:visible').first().innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('D3', /\b1\b/.test(expanded), 'rail badge on « Utilisateurs » (expanded)', `« ${expanded} »`)
  await dismissReview(adm)
  const collapse = adm.getByRole('button', { name: 'Réduire la barre latérale' })
  if (await collapse.isVisible().catch(() => false)) {
    await collapse.click()
    await adm.waitForTimeout(600)
    await dismissReview(adm)
    const collapsed = (await adm.locator('a[href="/users"]:visible').first().innerText().catch(() => '')).replace(/\s+/g, ' ')
    check('D3', /\b1\b/.test(collapsed), 'rail badge on « Utilisateurs » (collapsed)', `« ${collapsed} »`)
    await shot(adm, 'D3-collapsed-rail')
    // `next dev`'s « N » indicator sits on top of this footer button (dev only) — click it from the DOM.
    await adm.getByRole('button', { name: 'Développer la barre latérale' }).evaluate((el) => el.click())
  } else skip('D3', 'collapse control not visible at this window width')
  const { p } = await phoneTab(A.context, 390, 844, true)
  await p.goto(`${WEB}/`)
  const opener = p.getByRole('button', { name: 'Ouvrir la navigation' })
  await opener.waitFor({ state: 'visible', timeout: 60000 }) // a cold dashboard compile took > 30 s on run 1
  await dismissReview(p)
  await opener.click()
  await p.waitForTimeout(800)
  const drawer = (await p.locator('[role="dialog"] a[href="/users"]').first().innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('D3', /\b1\b/.test(drawer), 'badge in the phone drawer at 390', `« ${drawer} »`)
  await shot(p, 'D3-390-drawer')
  await p.close()
})

// ---------------------------------------------------------------- C5 — off and on again (AC-1)
await scenario('C5', async () => {
  // Standalone (ONLY=C5) her window starts elsewhere: the checks below read her /conges.
  if (!sec.url().endsWith('/conges')) { await sec.goto(`${WEB}/conges`); await waitText(sec, /Demander un congé/, 15000) }
  await adm.goto(`${WEB}/settings`)
  const sw = adm.locator('#team-leave-requests')
  await sw.waitFor()
  await sw.scrollIntoViewIfNeeded()
  await sw.click()
  await toast(adm, /désactivées/)
  const end = Date.now() + 10000
  let entries = 1
  while (Date.now() < end && (entries = await sec.locator('a[href="/conges"]').count()) > 0) await sleep(250)
  check('C5', entries === 0, 'off: the secretary loses « Congés » without a reload', `entries=${entries}`)
  check('C5', await waitText(sec, /pas activées/, 8000), 'off: her /conges says the module is off')
  await adm.goto(`${WEB}/users`)
  await waitText(adm, /Utilisateurs/, 15000)
  await adm.waitForTimeout(1500)
  check('C5', (await adm.getByRole('tab', { name: /Congés/ }).count()) === 0, 'off: /users has no tabs')
  await adm.goto(`${WEB}/settings`)
  // Run 7: wait for the switch to be read (OFF) before pressing, and read its state back — « /activées/ » also
  // matched the « désactivées » toast still on screen, so a click landing before hydration passed unnoticed.
  const on = adm.locator('#team-leave-requests')
  await on.waitFor({ timeout: 20000 })
  await on.scrollIntoViewIfNeeded()
  await adm.waitForFunction(() => document.querySelector('#team-leave-requests')?.getAttribute('aria-checked') === 'false', null, { timeout: 10000 }).catch(() => {})
  await adm.waitForTimeout(500)
  await on.click()
  await adm.waitForFunction(() => document.querySelector('#team-leave-requests')?.getAttribute('aria-checked') === 'true', null, { timeout: 10000 }).catch(() => {})
  await toast(adm, /congé activées/)
  check('C5', psql(`select "LeaveRequestsEnabled"::text from "Clinics" where "Id" = '${CLINIC}'`) === 'true', 'on again: the switch is ON in the database')
  await waitText(sec, /Demander un congé/, 10000)
  await sec.waitForTimeout(1500)
  // the balance card's two lines (« Droit : … », « Total pris depuis le début … ») are not congés either
  const items = await sec.locator('main li').filter({ hasNotText: RELEVE_LINE }).filter({ hasNotText: /Droit :|Aucun droit à congé|Total pris depuis le début/ }).count()
  check('C5', items === 7 && psql('select count(*) from "StaffLeaveRequests"') === '8', 'on again: her 7 congés are all back (8 rows in the cabinet)', `cards=${items}`)
})

// ---------------------------------------------------------------- end state, left open for the owner
await adm.goto(`${WEB}/users?onglet=conges`).catch(() => {})
await sec.goto(`${WEB}/conges`).catch(() => {})
await adm.waitForTimeout(2000)
await shot(adm, 'END-admin')
await shot(sec, 'END-secretary')

// ================================================================ « Droit à congé » (droit-conges-blueprint, 2026-11-06)
// Ines carries 24 j par an since 1 January (A4) and 4,5 days of accepted congé payé after today (R1 + R3's 2026 share).
const account = () => adm.locator('section[aria-labelledby="staff-leave-account"]')
const relevé = (page) => page.locator('section[aria-labelledby="staff-leave-statement"], section[aria-labelledby="leave-statement"]').first()
async function openFile() {
  await adm.bringToFront().catch(() => {})
  await adm.goto(inesFile)
  await account().waitFor({ timeout: 20000 })
  await account().scrollIntoViewIfNeeded().catch(() => {})
}
const remainingOn = async (page) =>
  ((await bodyText(page)).match(/(-?[\d,]+)\s*jours? restants?/) ?? [null, ''])[1]
const nextMonthFirst = (() => {
  const d = new Date()
  const y = d.getMonth() === 11 ? d.getFullYear() + 1 : d.getFullYear()
  const m = (d.getMonth() + 1) % 12 + 1
  return `${y}-${String(m).padStart(2, '0')}-01`
})()

await scenario('K5', async () => {
  await openFile()
  await account().getByRole('button', { name: /Modifier la règle|Définir la règle/ }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#leave-rule-days').waitFor()
  await dlg.getByRole('radio', { name: 'par mois' }).click()
  await dlg.locator('#leave-rule-days').fill('40')
  await dlg.getByRole('button', { name: 'Enregistrer la règle' }).click()
  check('K5', await waitText(adm, /Par demi-journée, entre 0 et 31 jours\./, 4000), 'a monthly rule of 40 days is refused before sending')
  check('K5', psql(`select count(*) from "StaffLeaveAllowanceEntries" where "UserId"='${INES.id}' and "Accrual"=2`) === '0', 'nothing written')
  await dlg.getByRole('button', { name: 'Retour' }).click()
  await openDialog(adm).getByRole('button', { name: /Quitter|Abandonner/ }).click().catch(() => {})
  await adm.waitForTimeout(400)
  await account().getByRole('button', { name: 'Ajouter ou retirer des jours' }).click()
  const adj = openDialog(adm)
  await adj.locator('#leave-adjust-days').fill('2')
  await adj.getByRole('button', { name: 'Ajouter', exact: true }).click()
  check('K5', await waitText(adm, /Indiquez le motif\./, 4000), 'days added with no motif → « Indiquez le motif. »')
  await adj.getByRole('button', { name: 'Retour' }).click()
  await openDialog(adm).getByRole('button', { name: /Quitter|Abandonner/ }).click().catch(() => {})
  await adm.waitForTimeout(400)
})

await scenario('K1', async () => {
  await openFile()
  const before = await remainingOn(adm)
  check('K1', before === '19,5', 'the file reads « 19,5 jours restants » (24 − 4,5 accepted after today)', String(before))
  await account().getByRole('button', { name: 'Modifier la règle' }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#leave-rule-days').waitFor()
  await dlg.getByRole('radio', { name: 'par mois' }).click()
  await dlg.locator('#leave-rule-days').fill('2')
  const since = (await dlg.locator('#leave-rule-since').innerText()).trim()
  check('K1', /^1er /.test(since), 'starts on « 1er … » of next month by default', since)
  await shot(adm, 'K1-rule-dialog')
  await dlg.getByRole('button', { name: 'Enregistrer la règle' }).click()
  check('K1', Boolean(await toast(adm, /Règle enregistrée/)), 'toast « Règle enregistrée »')
  await adm.waitForTimeout(1200)
  const card = (await adm.locator('section[aria-label^="Congé payé"]').first().innerText()).replace(/\s+/g, ' ')
  check('K1', /prochain : \+2 j le 1er /.test(card), 'the card announces the next credit « +2 j le 1er … »', card)
  check('K1', (await remainingOn(adm)) === '19,5', 'nothing credited before its day — still 19,5')
  const rows = psql(`select "Days" || '|' || "Accrual" || '|' || "OnDay" from "StaffLeaveAllowanceEntries" where "UserId"='${INES.id}' and "Kind"=1 order by "CreatedAt"`)
  check('K1', rows.split('\n').length === 2 && rows.endsWith(`2.0|2|${nextMonthFirst}`), 'SQL: the yearly rule kept, the monthly one from next month appended', rows.replace(/\n/g, ' ; '))
})

await scenario('K2', async () => {
  await openFile()
  await account().getByRole('button', { name: 'Solde de départ' }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#leave-opening-days').fill('10')
  await dlg.getByRole('button', { name: 'Enregistrer', exact: true }).click()
  check('K2', Boolean(await toast(adm, /Solde de départ enregistré/)), 'toast « Solde de départ enregistré »')
  await adm.waitForTimeout(1200)
  check('K2', (await remainingOn(adm)) === '5,5', 'opening 10 today − 4,5 accepted after today = « 5,5 jours restants »', String(await remainingOn(adm)))
  const r = (await relevé(adm).innerText()).replace(/\s+/g, ' ')
  check('K2', /Solde de départ/.test(r) && /Congé prévu/.test(r) && /solde 5,5 j$/.test(r.trim()), 'relevé: « Solde de départ », then « Congé prévu » lines, ending « solde 5,5 j »', r.slice(-200))
  check('K2', !/Droit 2026/.test(r), 'the 1 January credit is inside the opening, not listed', r.slice(0, 120))
  await shot(adm, 'K2-opening-releve')
})

await scenario('K3', async () => {
  await openFile()
  await account().getByRole('button', { name: 'Ajouter ou retirer des jours' }).click()
  const dlg = openDialog(adm)
  await dlg.getByRole('radio', { name: 'Retirer' }).click()
  await dlg.locator('#leave-adjust-days').fill('1,5')
  await dlg.locator('#leave-adjust-reason').fill('Rattrapage')
  await dlg.getByRole('button', { name: 'Retirer', exact: true }).click()
  check('K3', Boolean(await toast(adm, /Jours retirés/)), 'toast « Jours retirés »')
  await adm.waitForTimeout(1200)
  check('K3', (await remainingOn(adm)) === '4', '5,5 − 1,5 = « 4 jours restants »', String(await remainingOn(adm)))
  const r = (await relevé(adm).innerText()).replace(/\s+/g, ' ')
  check('K3', /Jours retirés · Rattrapage/.test(r) && /−1,5 j/.test(r), 'relevé line « Jours retirés · Rattrapage » −1,5 j', r.slice(0, 240))
})

await scenario('K4', async () => {
  await openFile()
  await relevé(adm).getByRole('button', { name: /^Annuler la ligne .*Jours retirés/ }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#leave-cancel-reason').waitFor()
  const title = (await dlg.locator('h2').first().innerText()).replace(/\s+/g, ' ')
  check('K4', /Annuler « Jours retirés · Rattrapage » \?/.test(title), 'the confirm names the line', title)
  await dlg.getByRole('button', { name: 'Annuler la ligne' }).click()
  check('K4', await waitText(adm, /Indiquez le motif de l'annulation\./, 4000), 'no motif → « Indiquez le motif de l\'annulation. »')
  await dlg.locator('#leave-cancel-reason').fill('Erreur de saisie')
  await dlg.getByRole('button', { name: 'Annuler la ligne' }).click()
  check('K4', Boolean(await toast(adm, /Ligne annulée/)), 'toast « Ligne annulée »')
  await adm.waitForTimeout(1200)
  check('K4', (await remainingOn(adm)) === '5,5', 'balance back to « 5,5 jours restants »', String(await remainingOn(adm)))
  const struck = await relevé(adm).locator('p.line-through', { hasText: 'Jours retirés' }).count()
  check('K4', struck >= 1 && /annulée : Erreur de saisie/.test(await relevé(adm).innerText()), 'the line stays, struck through, « annulée : Erreur de saisie »')
  const lines = (await account().locator('ul li').allInnerTexts()).map((t) => t.replace(/\s+/g, ' '))
  check('K4', lines.some((l) => /Retrait de 1,5 j · Rattrapage/.test(l) && /annulée par Dr Salma Ben Youssef : Erreur de saisie/.test(l)),
    'the account lists it « annulée par Dr Salma Ben Youssef : Erreur de saisie »', lines.join(' | '))
  await shot(adm, 'K4-cancelled')
})

await scenario('K6', async () => {
  await sec.bringToFront().catch(() => {})
  await sec.goto(`${WEB}/conges`)
  await waitText(sec, /Demander un congé/, 15000)
  check('K6', (await remainingOn(sec)) === '5,5', 'her /conges reads « 5,5 jours restants » — the same fold', String(await remainingOn(sec)))
  const card = (await sec.locator('section[aria-label^="Congé payé"]').first().innerText()).replace(/\s+/g, ' ')
  check('K6', /Total pris depuis le début/.test(card) && /prochain : \+2 j le 1er /.test(card), 'her card: « Total pris depuis le début » and the next credit', card)
  check('K6', (await relevé(sec).count()) === 1 && (await relevé(sec).getByRole('button').count()) === 0, 'her relevé is read-only (no « Annuler »)')
  await shot(sec, 'K6-secretary')
})

await scenario('K7', async () => {
  // the rule dialog and the relevé at the edges
  for (const [w, h] of [[320, 640], [1536, 730]]) {
    await adm.setViewportSize({ width: w, height: h })
    await openFile()
    const scroller = await adm.evaluate(() => {
      const main = document.querySelector('main')
      return main ? main.scrollWidth - main.clientWidth : -1
    })
    check('K7', scroller <= 1, `${w}×${h}: the file does not scroll sideways`, String(scroller))
    await account().getByRole('button', { name: /Modifier la règle|Définir la règle/ }).click()
    const dlg = openDialog(adm)
    await dlg.locator('#leave-rule-days').waitFor()
    await adm.waitForTimeout(500)
    const m = await dlg.evaluate((el) => ({
      over: Math.max(0, ...[el, ...el.querySelectorAll('*')]
        .filter((n) => n === el || ['auto', 'scroll'].includes(getComputedStyle(n).overflowX))
        .map((n) => n.scrollWidth - n.clientWidth).filter((d) => d > 1)),
    }))
    const sb = await dlg.getByRole('button', { name: 'Enregistrer la règle' }).boundingBox()
    check('K7', m.over <= 1, `${w}×${h}: the rule dialog has no sideways overflow`, JSON.stringify(m))
    check('K7', Boolean(sb) && sb.y + sb.height <= h, `${w}×${h}: « Enregistrer la règle » on screen`, JSON.stringify(sb))
    await shot(adm, `K7-rule-${w}x${h}`)
    await dlg.getByRole('button', { name: 'Retour' }).click()
    await adm.waitForTimeout(400)
  }
  await adm.setViewportSize({ width: 1000, height: 830 })
})


// ---------------------------------------------------------------- run 9: the paths a single day cannot show
const months = new Date().getMonth() + 1 // monthly credits from 1 January up to this month's 1st
const fileOf = async (userId, year) => adm.evaluate(async ({ api, userId, year }) => {
  const t = await (await fetch('/bff/auth/token', { credentials: 'include' })).json()
  const token = t.accessToken ?? t.token
  const r = await fetch(`${api}/team/users/${encodeURIComponent(userId)}/file${year ? `?year=${year}` : ''}`, { headers: { Authorization: `Bearer ${token}` } })
  return r.json()
}, { api: API, userId, year })
async function openOther() {
  await adm.bringToFront().catch(() => {})
  await adm.goto(otherFile)
  await account().waitFor({ timeout: 20000 })
  await account().scrollIntoViewIfNeeded().catch(() => {})
}
// the relevé's lines only (its heading « Relevé 2026 » is not a line)
const releveLines = async (page) => (await relevé(page).locator('ul').first().innerText()).replace(/\s+/g, ' ').trim()
const otherRemaining = async () => ((await adm.locator('section[aria-label^="Congé payé"]').first().innerText()).match(/(-?[\d,]+)\s*jours? restants?/) ?? [null, null])[1]
const insertRule = (days, accrual, since, carry) => psql(`insert into "StaffLeaveAllowanceEntries"
  ("Id","ClinicId","UserId","Kind","Days","Accrual","CarryOver","OnDay","CreatedByUserId","CreatedByName","CreatedAt")
  values (gen_random_uuid(), '${CLINIC}', '${OTHER.id}', 1, ${days}, ${accrual}, ${carry}, '${since}', 'qa', 'QA', now()) returning "Id"`).split('\n')[0]
const insertTaken2025 = (start, end, days) => psql(`insert into "StaffLeaveRequests"
  ("Id","ClinicId","UserId","Type","StartDate","EndDate","HalfDay","Reason","Status","RequestedDays","CountedDays","DecidedByUserId","DecidedAt","EnteredByAdmin","CreatedAt")
  values (gen_random_uuid(), '${CLINIC}', '${OTHER.id}', 0, '${start}', '${end}', false, 'QA 2025', 1, ${days}, ${days}, 'qa', now(), true, now()) returning "Id"`).split('\n')[0]

await scenario('K8', async () => {
  await openOther()
  await account().getByRole('button', { name: 'Définir la règle' }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#leave-rule-days').fill('2')
  await dlg.getByRole('radio', { name: 'par mois' }).click()
  await dlg.locator('#leave-rule-since').click()
  await adm.getByRole('option', { name: new RegExp(`^1er janvier ${new Date().getFullYear()}$`) }).click()
  await dlg.getByRole('button', { name: 'Enregistrer la règle' }).click()
  check('K8', Boolean(await toast(adm, /Règle enregistrée/)), 'a monthly rule from 1 January is saved')
  await adm.waitForTimeout(1500)
  const card = (await adm.locator('section[aria-label^="Congé payé"]').first().innerText()).replace(/\s+/g, ' ')
  check('K8', new RegExp(`Acquis ${2 * months} j`).test(card), `${months} credits already landed: « Acquis ${2 * months} j »`, card)
  check('K8', (await otherRemaining()) === String(2 * months - 2), `minus her 30–31 Dec congé: « ${2 * months - 2} jours restants »`, String(await otherRemaining()))
  const credits = await relevé(adm).locator('li', { hasText: /Acquis — / }).count()
  check('K8', credits === months, `the relevé lists ${months} « Acquis — <mois> » lines`, String(credits))
  await shot(adm, 'K8-monthly-from-january')
})

await scenario('K9', async () => {
  await openOther()
  await account().getByRole('button', { name: /^Annuler « Règle : 2 j par mois/ }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#leave-cancel-reason').fill('Mauvaise règle')
  await dlg.getByRole('button', { name: 'Annuler la ligne' }).click()
  check('K9', Boolean(await toast(adm, /Ligne annulée/)), 'a rule is cancelled like any line')
  await adm.waitForTimeout(1500)
  const card = (await adm.locator('section[aria-label^="Congé payé"]').first().innerText()).replace(/\s+/g, ' ')
  check('K9', /Aucun droit à congé défini/.test(card) && !/jours? restants?/.test(card), 'no droit left: the card claims no balance', card)
  const struck = await account().locator('p.line-through', { hasText: 'Règle : 2 j par mois' }).count()
  check('K9', struck === 1, 'the rule stays listed, struck through')
})

let rule2025 = null
await scenario('K10', async () => {
  // A year that has ended (own artefacts, by SQL): 20 j par an from 1 January 2025, 5 days taken in March 2025.
  rule2025 = insertRule(20, 1, '2025-01-01', false)
  insertTaken2025('2025-03-03', '2025-03-07', 5)
  const y2025 = await fileOf(OTHER.id, 2025)
  const b = y2025.balance ?? {}
  check('K10', b.acquired === 20 && b.taken === 5 && b.expired === 15 && b.remaining === 0, '2025 read back: acquis 20, pris 5, perdus 15, reste 0', JSON.stringify(b))
  const last = (y2025.statement ?? []).at(-1) ?? {}
  check('K10', last.kind === 'Expired' && last.days === -15 && last.balance === 0, "2025's relevé ends « Non pris en 2025 : perdus » −15", JSON.stringify(last))
  await openOther()
  const r = await releveLines(adm)
  check('K10', /^Droit 2026/.test(r.trim()) && !/Report de 2025/.test(r), '2026 starts at « Droit 2026 », nothing carried', r.slice(0, 160))
  check('K10', (await otherRemaining()) === '18', '2026: 20 − her 2 planned days = 18', String(await otherRemaining()))
})

await scenario('K11', async () => {
  psql(`update "StaffLeaveAllowanceEntries" set "CarryOver" = true where "Id" = '${rule2025}'`)
  await openOther()
  const r = await releveLines(adm)
  check('K11', /^Report de 2025/.test(r.trim()) && /15 j/.test(r.slice(0, 60)), 'carried over: the relevé opens « Report de 2025 » 15 j', r.slice(0, 160))
  check('K11', (await otherRemaining()) === '33', '15 carried + 20 − 2 planned = 33', String(await otherRemaining()))
  await shot(adm, 'K11-carried')
})

await scenario('K12', async () => {
  // Took more than she had in 2025: a debt is carried, never wiped by 31 December — even under « perdus ».
  psql(`update "StaffLeaveAllowanceEntries" set "CarryOver" = false where "Id" = '${rule2025}'`)
  insertTaken2025('2025-06-02', '2025-06-25', 18)
  await openOther()
  const r = await releveLines(adm)
  check('K12', /^Dépassement reporté de 2025/.test(r.trim()) && /−3 j|-3 j/.test(r.slice(0, 80)), 'the relevé opens « Dépassement reporté de 2025 » −3 j', r.slice(0, 160))
  check('K12', (await otherRemaining()) === '15', '−3 + 20 − 2 planned = 15', String(await otherRemaining()))
  const y2025 = await fileOf(OTHER.id, 2025)
  check('K12', (y2025.balance ?? {}).expired === 0, 'nothing « perdu » in 2025 when she ended it owing', JSON.stringify(y2025.balance))
})

await scenario('K13', async () => {
  // Her screen follows a change made on the admin's, with no reload.
  await sec.bringToFront().catch(() => {})
  await sec.goto(`${WEB}/conges`)
  await waitText(sec, /Demander un congé/, 15000)
  const before = await remainingOn(sec)
  const marker = await sec.evaluate(() => { window.__qaMark = Math.random(); return window.__qaMark })
  await openFile()
  await account().getByRole('button', { name: 'Ajouter ou retirer des jours' }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#leave-adjust-days').fill('1')
  await dlg.locator('#leave-adjust-reason').fill('Prime de fin d\'année')
  await dlg.getByRole('button', { name: 'Ajouter', exact: true }).click()
  await toast(adm, /Jours ajoutés/)
  const expected = String(Number(String(before).replace(',', '.')) + 1).replace('.', ',')
  const end = Date.now() + 10000
  let now = before
  while (Date.now() < end && (now = await remainingOn(sec)) !== expected) await sleep(300)
  const same = await sec.evaluate((m) => window.__qaMark === m, marker).catch(() => false)
  check('K13', now === expected && same, `her « jours restants » moves ${before} → ${expected} with no reload`, `${before} → ${now} · same page ${same}`)
})

await scenario('K14', async () => {
  // A tablet held with a finger: every control of the droit ≥ 44 px under (pointer: coarse).
  await adm.setViewportSize({ width: 820, height: 1180 })
  // run 10: `setEmulatedMedia` does not move (pointer) — touch emulation does; and the run must prove it took.
  const cdp = await adm.context().newCDPSession(adm)
  await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 })
  await openFile()
  const coarse = await adm.evaluate(() => matchMedia('(pointer: coarse)').matches)
  check('K14', coarse, 'the page really reads (pointer: coarse)', String(coarse))
  // the tappable height: the control, or its `.touch-target` overlay where it has one (a 36 px Button carries a 44 px ::after)
  const heights = async (loc) => loc.evaluateAll((els) => els.filter((e) => e.offsetParent).map((e) => {
    const after = getComputedStyle(e, '::after')
    const overlay = after.content && after.content !== 'none' ? Math.max(parseFloat(after.height) || 0, parseFloat(after.minHeight) || 0) : 0
    return Math.round(Math.max(e.getBoundingClientRect().height, overlay))
  }))
  const buttons = await heights(account().getByRole('button'))
  const xs = await heights(relevé(adm).getByRole('button'))
  check('K14', buttons.length >= 3 && buttons.every((h) => h >= 44), 'account buttons ≥ 44 px', JSON.stringify(buttons))
  check('K14', xs.every((h) => h >= 44), 'relevé « Annuler » buttons ≥ 44 px', JSON.stringify(xs))
  await account().getByRole('button', { name: /Modifier la règle|Définir la règle/ }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#leave-rule-days').waitFor()
  await adm.waitForTimeout(400)
  const controls = await heights(dlg.locator('input, [role="radio"], [data-slot="select-trigger"], button'))
  check('K14', controls.length >= 6 && controls.every((h) => h >= 44), 'the rule dialog: fields, choices and buttons ≥ 44 px', JSON.stringify(controls))
  await shot(adm, 'K14-touch-820')
  await dlg.getByRole('button', { name: 'Retour' }).click()
  await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: false })
  await adm.setViewportSize({ width: 1000, height: 830 })
})


console.log('\n================ SUMMARY')
const fails = results.filter((r) => r.outcome === 'fail')
const skips = results.filter((r) => r.outcome === 'skip')
console.log(`checks: ${results.length} · pass ${results.length - fails.length - skips.length} · fail ${fails.length} · skip ${skips.length}`)
fails.forEach((f) => console.log(`  FAIL ${f.id}: ${f.m} — ${f.d}`))
skips.forEach((f) => console.log(`  SKIP ${f.id}: ${f.m}`))
console.log(`R3 preview captured: « ${r3Preview} »`)
fs.writeFileSync(path.join(HERE, 'last-walk.json'), JSON.stringify({ at: new Date().toISOString(), results }, null, 2))
console.log(fails.length ? 'RED' : 'ALL CLEAR')
process.exit(0) // leaves both Chrome windows open
