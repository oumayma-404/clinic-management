// QA walk — congés review fixes F1–F40 (qa/plan-review-fixes.md). One launch, every scenario, findings collected,
// nothing fixed. My OWN headless Chrome (playwright-core `channel: 'chrome'`) — never the owner's CDP windows 9333/9334.
// People: ADM = Dr Salma · T/T2/T3/T5/T6 = throwaway secretaries made for this run (deactivated + their rows removed at
// the end) · INES = the owner's secretary, READ-ONLY.
// Stack: web :3096 · API :5096 · DB clinic_pointage_demo.      node walk-review-fixes.mjs
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PW_DIR = process.env.PW_DIR ?? 'C:/Users/OUMAYM~1/AppData/Local/Temp/claude/6ab72d215ac88a62/0bea09ef-3a73-43b2-a09f-b045eee34842/scratchpad/pw'
const OUT = process.env.QA_OUT ?? 'C:/Users/OUMAYM~1/AppData/Local/Temp/claude/6ab72d215ac88a62/add10e54-9b47-4b03-a536-d9bbae23bb2e/scratchpad'
const { chromium } = createRequire(`${PW_DIR}/package.json`)('playwright-core')

const WEB = 'http://localhost:3096'
const API = 'http://localhost:5096/api'
const DB = process.env.QA_DB ?? 'clinic_pointage_demo'
const CLINIC = 'f64a8a75-94e1-4e08-a743-fa231abe438f'
const PASSWORD = 'QaAudit2026!y'
const ADMIN = { email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', totp: '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO', name: 'Dr Salma Ben Youssef', id: 'local|cdc1c7a5-9326-4892-a049-598cb07eea6b' }
const INES = { email: 'qa.secretary@ibnkhaldoun.test', id: 'local|22222222-2222-4222-8222-222222222222', name: 'Ines Gharbi' }
const NADIA = { id: 'local|11111111-1111-4111-8111-111111111111', name: 'Dr Nadia Trabelsi' }
const SHOTS = path.join(HERE, 'shots')
fs.mkdirSync(SHOTS, { recursive: true })

const local = (d = new Date()) => new Date(d.getTime() + 3600_000) // Tunisia, UTC+1
const TODAY = local().toISOString().slice(0, 10)
if (TODAY !== '2026-11-09' && process.env.QA_ANY_DAY !== '1') {
  console.log(`The expectations are hand-computed for 2026-11-09; today is ${TODAY}. Set QA_ANY_DAY=1 to run anyway.`)
  process.exit(2)
}

// ---------------------------------------------------------------- findings
const findings = []
const results = []
const norm = (s) => String(s ?? '').replace(/\s+/g, ' ').trim()
const ok = (id, m) => { console.log(`  ✅ [${id}] ${m}`); results.push({ id, outcome: 'pass', m }) }
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} — ${norm(d).slice(0, 500)}`); findings.push({ id, m, d: norm(d).slice(0, 500) }); results.push({ id, outcome: 'fail', m, d: norm(d).slice(0, 500) }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); results.push({ id, outcome: 'skip', m: why }) }
const check = (id, cond, m, d = '') => (cond ? ok(id, m) : bad(id, m, typeof d === 'string' ? d : JSON.stringify(d)))
async function scenario(id, fn) {
  console.log(`\n— ${id}`)
  try { await fn() } catch (e) { bad(id, 'threw', String(e?.message ?? e).split('\n')[0]) }
}

// ---------------------------------------------------------------- helpers
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const psql = (sql) =>
  execFileSync('docker', ['exec', 'clinic-postgres', 'psql', '-U', 'clinic_user', '-d', DB, '-Atc', sql], { encoding: 'utf8' }).trim()

function totpAt(secret, counter) {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of secret.toUpperCase()) bits += A.indexOf(c).toString(2).padStart(5, '0')
  const key = Buffer.from(bits.match(/.{8}/g).map((b) => parseInt(b, 2)))
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0)
  buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac('sha1', key).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, '0')
}
// A code is single-use per 30 s window: wait for a window never used, early enough in it to land.
let lastCounter = 0
async function freshTotp() {
  for (;;) {
    const now = Date.now()
    const c = Math.floor(now / 30000)
    if (c > lastCounter && now % 30000 < 18000) { lastCounter = c; return totpAt(ADMIN.totp, c) }
    await sleep(400)
  }
}

const bodyText = (page) => page.locator('body').innerText().catch(() => '')
async function waitText(page, re, timeout = 10000) {
  const end = Date.now() + timeout
  while (Date.now() < end) {
    if (re.test(await bodyText(page))) return true
    await sleep(250)
  }
  return false
}
async function pollText(get, re, timeout = 8000) {
  const end = Date.now() + timeout
  let last = ''
  while (Date.now() < end) {
    last = norm(await get().catch(() => ''))
    if (re.test(last)) return last
    await sleep(250)
  }
  return last
}
async function toast(page, re, timeout = 10000) {
  const end = Date.now() + timeout
  while (Date.now() < end) {
    const all = (await page.locator('[data-sonner-toast]').allInnerTexts().catch(() => [])).join(' | ')
    if (re.test(all)) return norm(all)
    await sleep(150)
  }
  return null
}
const openDialog = (page) => page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible').last()
const alertDialog = (page) => page.locator('[role="alertdialog"]:visible').last()
const shot = async (page, name) => {
  await page.bringToFront().catch(() => {})
  await page.screenshot({ path: path.join(SHOTS, `RF-${name}.png`), timeout: 15000 }).catch((e) => console.log('   (shot failed)', name, e.message))
}
const quiet = (context) =>
  context.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const guardFeedback = (page) =>
  page.addLocatorHandler(page.locator('[aria-labelledby="feedback-prompt-title"]'), async (c) => {
    await c.getByRole('button', { name: 'Plus tard' }).click().catch(() => {})
  })
const mainOverflow = (page) => page.evaluate(() => {
  const m = document.querySelector('main#contenu-principal') ?? document.querySelector('main')
  return { main: m ? m.scrollWidth - m.clientWidth : -1, doc: document.documentElement.scrollWidth - innerWidth }
})
// the tappable height: the control, or its `.touch-target` ::after overlay (run 10's K14 probe)
const heights = (loc) => loc.evaluateAll((els) => els.filter((e) => e.offsetParent).map((e) => {
  const after = getComputedStyle(e, '::after')
  const overlay = after.content && after.content !== 'none' ? Math.max(parseFloat(after.height) || 0, parseFloat(after.minHeight) || 0) : 0
  return Math.round(Math.max(e.getBoundingClientRect().height, overlay))
}))
const cdpOf = new Map()
async function coarse(page, on) {
  if (on) {
    const cdp = await page.context().newCDPSession(page)
    cdpOf.set(page, cdp)
    await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 })
  } else {
    const cdp = cdpOf.get(page)
    cdpOf.delete(page)
    if (cdp) { await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: false }).catch(() => {}); await cdp.detach().catch(() => {}) }
  }
  await sleep(600)
  return page.evaluate(() => matchMedia('(pointer: coarse)').matches)
}

async function signIn(page, email, withTotp, target) {
  await page.goto(`${WEB}${target}`, { waitUntil: 'domcontentloaded' })
  await page.waitForTimeout(1500)
  if (!page.url().includes('/login')) return
  await page.fill('input[type=email]', email)
  await page.fill('input[type=password]', PASSWORD)
  await page.click("button:has-text('Se connecter')")
  if (withTotp) {
    await page.waitForSelector('input[inputmode=numeric]', { timeout: 20000 })
    await page.fill('input[inputmode=numeric]', await freshTotp())
  }
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60000 })
}
async function signOutVia(page) {
  await page.getByRole('button', { name: 'Mon compte' }).first().click()
  await page.getByRole('menuitem', { name: 'Se déconnecter' }).click()
  await sleep(800)
  const confirm = alertDialog(page)
  if (await confirm.isVisible().catch(() => false)) {
    console.log('   (sign-out asked:', norm(await confirm.innerText()).slice(0, 120), ')')
    await confirm.getByRole('button').last().click().catch(() => {})
  }
  await page.waitForURL(/\/login/, { timeout: 15000 }).catch(() => {})
}
async function openBell(page, re = null) {
  await page.getByRole('button', { name: 'Notifications' }).first().click()
  await sleep(1200)
  if (!re) return
  if (await waitText(page, re, 8000)) return
  await page.keyboard.press('Escape').catch(() => {})
  await sleep(800)
  await page.getByRole('button', { name: 'Notifications' }).first().click()
  await waitText(page, re, 10000)
}

// API (arrangement and cleanup only — the walk itself goes through the screens)
async function api(method, url, body, token) {
  const r = await fetch(`${API}${url}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  let json = null
  try { json = await r.json() } catch { /* empty */ }
  return { status: r.status, json }
}
async function apiLogin(email, deviceLabel, withTotp = false) {
  const body = { email, password: PASSWORD, deviceLabel }
  if (withTotp) body.totpCode = await freshTotp()
  const r = await api('POST', '/auth/login', body)
  const v = r.json?.value ?? {}
  return { token: v.accessToken, refresh: v.refreshToken, status: r.status }
}
const apiLogout = (s) => api('POST', '/auth/logout', { refreshToken: s.refresh }).catch(() => {})
async function mails(to, since) {
  const r = await fetch(`http://localhost:8025/api/v1/search?query=${encodeURIComponent(`to:${to}`)}&limit=50`)
  const j = await r.json()
  const out = []
  for (const m of j.messages ?? []) {
    if (new Date(m.Created) < since) continue
    const full = await (await fetch(`http://localhost:8025/api/v1/message/${m.ID}`)).json()
    out.push({ subject: m.Subject, text: norm(full.Text ?? '') })
  }
  return out
}

// ---------------------------------------------------------------- screen readers
const fileUrl = (id) => `${WEB}/users/${encodeURIComponent(id)}`
const balanceCard = (page) => page.locator('section[aria-label^="Congé payé "]').first()
const cardFacts = (page) => balanceCard(page).evaluate((sec) => {
  const t = (n) => (n?.textContent ?? '').replace(/\s+/g, ' ').trim()
  const bigP = sec.querySelector('p.items-baseline')
  const terms = {}
  let lis = sec.querySelectorAll('ul[aria-label="Calcul du solde"] > li')
  if (!lis.length) lis = sec.querySelectorAll('ul.gap-x-6 > li')
  for (const li of lis) {
    const ps = li.querySelectorAll('p')
    terms[t(ps[0])] = { value: t(ps[1]), note: t(ps[2]), red: (ps[1]?.className ?? '').includes('text-destructive') }
  }
  const lines = [...sec.querySelectorAll('ul.space-y-1 > li')].map(t)
  const pendingP = [...sec.querySelectorAll('p')].find((p) => /^En attente :/.test(t(p)))
  return {
    big: bigP ? [...bigP.children].map(t).join(' ') : '',
    bigRed: (bigP?.className ?? '').includes('text-destructive'),
    terms, lines, pending: t(pendingP), text: t(sec),
  }
})
const termIs = (f, label, value) => f.terms[label]?.value === value
const releveLines = (scope) => scope.locator('ul.divide-y').first().evaluate((ul) => [...ul.children].map((li) => {
  const t = (n) => (n?.textContent ?? '').replace(/\s+/g, ' ').trim()
  const ps = li.querySelectorAll('p')
  return {
    label: t(ps[0]), day: t(ps[1]), amount: t(ps[2]), bal: t(ps[3]),
    quiet: (ps[0]?.className ?? '').includes('text-muted-foreground'),
    info: (ps[0]?.className ?? '').includes('text-warning-ink'),
    balRed: (ps[3]?.className ?? '').includes('text-destructive'),
  }
}))
const showLines = (ls) => ls.map((l) => `${l.label} [${l.day}] ${l.amount} ${l.bal}${l.quiet ? ' (grey)' : ''}${l.info ? ' (info)' : ''}`).join(' | ')
const stepperYear = (scope) => scope.locator('p[aria-live="polite"]').first().textContent().then(norm)
const fileStatement = (page) => page.locator('section[aria-labelledby="staff-leave-statement"]')
const congesStatement = (page) => page.locator('#leave-statement-panel')
const strip = (page) => page.locator('section[aria-labelledby="leave-team-heading"]')
const stripCard = (page, name) => strip(page).locator('a').filter({ hasText: name }).first()
const pendingSection = (page) => page.locator('section[aria-labelledby="leave-pending-heading"]')
const pendingCard = (page, name, period) => pendingSection(page).locator('li').filter({ hasText: name }).filter({ hasText: period }).first()
const myRow = (page, re) => page.locator('section[aria-labelledby="leave-upcoming"] li, section[aria-labelledby="leave-history"] li').filter({ hasText: re }).first()
const fileHistoryRow = (page, re) => page.locator('section[aria-labelledby="staff-leave-history"] tr:visible').filter({ hasText: re }).first()

async function gotoConges(page) {
  await page.goto(`${WEB}/conges`)
  await balanceCard(page).waitFor({ timeout: 25000 })
  await sleep(600)
}
async function gotoTab(page) {
  await page.goto(`${WEB}/users?onglet=conges`)
  await pendingSection(page).waitFor({ timeout: 25000 })
  await waitText(page, /Toutes les demandes/, 15000)
  await sleep(1200)
}
async function gotoFile(page, id) {
  await page.goto(fileUrl(id))
  await balanceCard(page).waitFor({ timeout: 25000 })
  await sleep(1000)
}
async function discardPromptAfterEscape(page) {
  await page.keyboard.press('Escape')
  const prompt = alertDialog(page)
  const up = await prompt.waitFor({ state: 'visible', timeout: 3000 }).then(() => true, () => false)
  const title = up ? norm(await prompt.innerText()) : ''
  return { up, title, prompt }
}

// Request form (« Demander un congé » / « Ajouter une absence »), already open.
const slot = (dlg) => dlg.locator('[role="status"]').first()
async function slotText(dlg, re, timeout = 8000) {
  const end = Date.now() + timeout
  let last = ''
  while (Date.now() < end) {
    const s = slot(dlg)
    last = norm(await s.innerText().catch(() => ''))
    const stale = await s.evaluate((e) => e.className.includes('opacity-60')).catch(() => false)
    if (!stale && re.test(last) && !/Décompte en cours/.test(last)) return last
    await sleep(250)
  }
  return last
}
async function setDates(dlg, start, end = start) {
  await dlg.locator('#staff-leave-start').fill(start)
  await dlg.locator('#staff-leave-end').fill(end)
}
async function openAsk(page) {
  await page.locator('button:visible', { hasText: 'Demander un congé' }).first().click()
  const dlg = openDialog(page)
  await dlg.locator('#staff-leave-start').waitFor({ timeout: 10000 })
  await sleep(500)
  return dlg
}
async function closeForm(page) {
  await openDialog(page).getByRole('button', { name: 'Annuler', exact: true }).click().catch(() => page.keyboard.press('Escape'))
  await sleep(400)
  const discard = alertDialog(page).getByRole('button', { name: 'Abandonner' })
  if (await discard.isVisible().catch(() => false)) await discard.click()
  await sleep(400)
}
const countRequests = (userId) => psql(`select count(*) from "StaffLeaveRequests" where "UserId"='${userId}'`)

// ================================================================ arrange (own artefacts only)
const walkStart = psql(`select now()`)
const walkStartDate = new Date(Date.now() - 5000)
console.log(`Today ${TODAY} · walk start ${walkStart}`)
const P = {}
P.flags = psql(`select "LeaveRequestsEnabled"::text||','||"AttendanceEnabled"::text||','||"AttendanceLockEnabled"::text||','||coalesce("AttendanceLockArmedAtUtc"::text,'null') from "Clinics" where "Id"='${CLINIC}'`)
const inesHash = () => psql(`select md5(coalesce((select string_agg(concat_ws('|',"Id","Type","StartDate","EndDate","Status","RequestedDays","CountedDays","HalfDay","HalfDayPart","CancelledAt","Reason","RejectionReason","CancelReason"), ';' order by "Id") from "StaffLeaveRequests" where "UserId"='${INES.id}'),'') || coalesce((select string_agg(concat_ws('|',"Id","Kind","Days","Accrual","CarryOver","OnDay","CancelledAt"), ';' order by "Id") from "StaffLeaveAllowanceEntries" where "UserId"='${INES.id}'),''))`)
P.ines = inesHash()
console.log('P3 flags before:', P.flags, '· P2 Ines hash before:', P.ines)

const stamp = String(Date.now()).slice(-6)
const people = {
  T: { name: `QA Congés ${stamp}`, email: `qa.conges.t${stamp}@cabinet-ibnkhaldoun.tn` },
  T2: { name: `QA Congés Historique ${stamp}`, email: `qa.conges.h${stamp}@cabinet-ibnkhaldoun.tn` },
  T3: { name: `QA Congés Perdus ${stamp}`, email: `qa.conges.p${stamp}@cabinet-ibnkhaldoun.tn` },
  T5: { name: `QA Congés Règle ${stamp}`, email: `qa.conges.r${stamp}@cabinet-ibnkhaldoun.tn` },
  T6: { name: `QA Congés Solde ${stamp}`, email: `qa.conges.s${stamp}@cabinet-ibnkhaldoun.tn` },
}
const { T, T2, T3, T5, T6 } = people
const arrangeLog = []
const expect200 = (what, r) => {
  const okr = r.status >= 200 && r.status < 300
  arrangeLog.push(`${what}: ${r.status}${okr ? '' : ' ' + JSON.stringify(r.json).slice(0, 200)}`)
  if (!okr) console.log('   ARRANGE FAILED', what, r.status, JSON.stringify(r.json).slice(0, 300))
  return okr
}
{
  console.log('Arranging throwaway secretaries over the API…')
  const adm = await apiLogin(ADMIN.email, 'QA arrange congés', true)
  if (!adm.token) { console.log('admin API login failed', adm.status); process.exit(1) }
  for (const p of Object.values(people)) {
    const c = await api('POST', '/users', { email: p.email, fullName: p.name, role: 'secretary' }, adm.token)
    expect200(`create ${p.name}`, c)
    p.id = c.json?.userId
    const token = c.json?.invitationUrl?.split('#t=')[1]
    expect200(`accept ${p.name}`, await api('POST', '/auth/invitation/accept', { token, password: PASSWORD }))
  }
  fs.writeFileSync(path.join(OUT, 'conges-throwaways.json'), JSON.stringify({ stamp, people }, null, 2))
  const rule = (id, days, accrual, since) => api('PUT', `/team/users/${encodeURIComponent(id)}/leave-rule`, { days, accrual, carryOver: false, since }, adm.token)
  const absence = (id, startDate, endDate, reason) => api('POST', `/team/users/${encodeURIComponent(id)}/absences`, { type: 'PaidLeave', startDate, endDate, halfDay: false, halfDayPart: null, reason }, adm.token)
  expect200('T rule 2/mois from 2026-01-01', await rule(T.id, 2, 'Month', '2026-01-01'))
  expect200('T2 absence 2→6 mars', await absence(T2.id, '2026-03-02', '2026-03-06', 'QA historique avant la règle'))
  expect200('T2 rule 2/mois from 2026-06-01', await rule(T2.id, 2, 'Month', '2026-06-01'))
  expect200('T3 opening 10 j au 2025-11-06', await api('POST', `/team/users/${encodeURIComponent(T3.id)}/leave-opening`, { days: 10, onDay: '2025-11-06' }, adm.token))
  expect200('T3 absence 17→20 nov 2025', await absence(T3.id, '2025-11-17', '2025-11-20', 'QA congé 2025'))
  expect200('T3 rule 20/an from 2026-01-01', await rule(T3.id, 20, 'Year', '2026-01-01'))
  expect200('T6 absence 2→5 mars', await absence(T6.id, '2026-03-02', '2026-03-05', 'QA congé de mars'))
  expect200('T6 absence 14→15 déc', await absence(T6.id, '2026-12-14', '2026-12-15', 'QA congé de décembre'))
  await apiLogout(adm)
  for (const [who, start, end, reason] of [[T3, '2025-12-29', '2025-12-31', 'QA fin d\'année 2025'], [T5, '2026-12-21', '2026-12-21', 'QA même semaine']]) {
    const s = await apiLogin(who.email, 'QA arrange congés')
    expect200(`${who.name} submits ${start}`, await api('POST', '/team/me/leave-requests', { type: 'PaidLeave', startDate: start, endDate: end, halfDay: false, halfDayPart: null, reason }, s.token))
    await apiLogout(s)
  }
  console.log(arrangeLog.join('\n'))
}
const ids = Object.values(people).map((p) => `'${p.id}'`).join(',')
psql(`update "SessionFamilies" set "EndedAtUtc"=now(), "EndedReason"='qa' where "UserId" in (${ids}) and "EndedAtUtc" is null`)

// ================================================================ browser
const browser = await chromium.launch({ channel: 'chrome', headless: true })
const mk = async (viewport) => {
  const ctx = await browser.newContext({ locale: 'fr-FR', timezoneId: 'Africa/Tunis', viewport })
  await quiet(ctx)
  const page = await ctx.newPage()
  await guardFeedback(page)
  return { ctx, page }
}
const ownFamilies = []
const captureFamily = (userId, since) => {
  const f = psql(`select "Id" from "SessionFamilies" where "UserId"='${userId}' and "CreatedAt" >= '${since}' and "EndedAtUtc" is null order by "CreatedAt" desc limit 1`)
  if (f) ownFamilies.push(f)
}
const A = await mk({ width: 1440, height: 900 })
const TC = await mk({ width: 1180, height: 820 })
const T3C = await mk({ width: 1180, height: 820 })
const adm = A.page
const t = TC.page
const t3 = T3C.page
let since = psql('select now()')
await signIn(adm, ADMIN.email, true, '/settings')
captureFamily(ADMIN.id, since)
since = psql('select now()')
await signIn(t, T.email, false, '/conges')
await signIn(t3, T3.email, false, '/conges')
// A first sign-in may raise a welcome / first-days dialog: say what it was, close it, and go on.
async function dismissStray(page, who) {
  await sleep(2500)
  for (let i = 0; i < 3; i++) {
    const d = page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible').last()
    if (!(await d.isVisible().catch(() => false))) return
    console.log(`   (stray dialog for ${who}: ${norm(await d.innerText().catch(() => '')).slice(0, 160)})`)
    await shot(page, `STRAY-${who}-${i}`)
    await page.keyboard.press('Escape').catch(() => {})
    await sleep(800)
  }
}
await t.goto(`${WEB}/conges`); await dismissStray(t, 'T')
await t3.goto(`${WEB}/conges`); await dismissStray(t3, 'T3')
console.log('Signed in — adm', adm.url(), '· T', t.url(), '· T3', t3.url())

try {
  // warm every route the walk opens (a dev server compiles on first visit)
  for (const route of ['/', '/settings', '/users?onglet=conges', '/users?onglet=presences', `/users/${encodeURIComponent(T.id)}`]) {
    await adm.goto(`${WEB}${route}`, { waitUntil: 'load', timeout: 120000 }).catch(() => {})
    await adm.waitForTimeout(1500)
  }

  // ============================================================ T, first visit (A1–A3)
  await scenario('A1', async () => {
    await gotoConges(t)
    const f = await cardFacts(t)
    check('A1', /^22 jours restants$/.test(f.big), 'card « 22 jours restants »', f.big)
    check('A1', await waitText(t, /Aucune demande pour le moment/, 5000), 'empty state « Aucune demande pour le moment »')
    const visibleAsk = await t.locator('button:visible', { hasText: 'Demander un congé' }).count()
    check('A1', visibleAsk === 1, '1180: one « Demander un congé » visible (the header one) before any request', `visible=${visibleAsk}`)
    const askBox = await t.locator('button:visible', { hasText: 'Demander un congé' }).first().boundingBox()
    check('A1', askBox && askBox.y < 200, 'it sits in the page header', JSON.stringify(askBox))
    const relBtn = t.getByRole('button', { name: 'Voir le relevé' })
    check('A1', (await relBtn.count()) === 1 && (await relBtn.getAttribute('aria-expanded')) === 'false' && (await t.locator('#leave-statement-panel').count()) === 0, '« Voir le relevé » present and folded')
    await shot(t, 'A1-first-visit-1180')
    await t.setViewportSize({ width: 390, height: 844 })
    await sleep(800)
    const fixed = t.locator('div.fixed button:visible', { hasText: 'Demander un congé' })
    const fb = await fixed.first().boundingBox().catch(() => null)
    const visible390 = await t.locator('button:visible', { hasText: 'Demander un congé' }).count()
    check('A1', fb && fb.y + fb.height <= 844 && fb.y > 844 - 140 && visible390 === 1, '390: the fixed bottom « Demander un congé » is the one on screen', JSON.stringify({ fb, visible390 }))
    await shot(t, 'A1-first-visit-390')
    await t.setViewportSize({ width: 1180, height: 820 })
  })

  await scenario('A2', async () => {
    await t.getByRole('button', { name: 'Voir le relevé' }).click()
    const panel = congesStatement(t)
    await panel.waitFor({ timeout: 5000 })
    await sleep(500)
    check('A2', (await stepperYear(panel)) === 'Relevé 2026' || (await stepperYear(panel)) === '2026' || /2026/.test(await stepperYear(panel)), 'stepper reads 2026', await stepperYear(panel))
    check('A2', await panel.getByRole('button', { name: 'Année suivante' }).isDisabled(), '« Année suivante » disabled on the current year')
    const lines = await releveLines(panel)
    const credits = lines.filter((l) => /^Acquis — /.test(l.label))
    check('A2', credits.length === 11 && lines.at(-1)?.bal === 'solde 22 j', '11 « Acquis — <mois> » lines, last « solde 22 j »', showLines(lines))
    await panel.getByRole('button', { name: 'Année précédente' }).click()
    check('A2', await waitText(t, /Aucun mouvement en 2025/, 8000), '« Année précédente » → « Aucun mouvement en 2025 »', await stepperYear(panel))
    await panel.getByRole('button', { name: 'Année suivante' }).click()
    await sleep(800)
  })

  await scenario('A3', async () => {
    const f = await cardFacts(t)
    console.log('     card:', JSON.stringify(f.terms), f.lines)
    check('A3', termIs(f, 'Acquis', '22 j') && termIs(f, 'Pris', '0 j') && termIs(f, 'Reste', '22 j') && !f.terms['Prévus'], 'Acquis 22 j − Pris 0 j = Reste 22 j (no Prévus)', JSON.stringify(f.terms))
    check('A3', f.lines.includes('À venir d\'ici le 31/12 : +2 j'), '« À venir d\'ici le 31/12 : +2 j »', f.lines.join(' | '))
    check('A3', f.lines.includes('À prendre avant le 31/12 : 24 j'), '« À prendre avant le 31/12 : 24 j »', f.lines.join(' | '))
    check('A3', f.lines.includes('Droit : 2 j par mois · non pris perdus au 31/12 · prochain : +2 j le 1er décembre'), 'rule line with « prochain : +2 j le 1er décembre »', f.lines.join(' | '))
  })

  // ============================================================ T3 — F2 / F9 on both surfaces, before F4's decision (A19, A20)
  await scenario('A19', async () => {
    await gotoFile(adm, T3.id)
    const f = await cardFacts(adm)
    console.log('     T3 card:', f.big, JSON.stringify(f.terms), f.lines)
    check('A19', f.big === '20 jours restants' && termIs(f, 'Acquis', '20 j'), '« 20 jours restants », Acquis 20 j (not 26)', `${f.big} ${JSON.stringify(f.terms)}`)
    check('A19', f.lines.includes('6 j non pris en 2025 : perdus'), '« 6 j non pris en 2025 : perdus » on the card', f.lines.join(' | '))
    check('A19', f.lines.includes('À prendre avant le 31/12 : 20 j'), '« À prendre avant le 31/12 : 20 j »', f.lines.join(' | '))
    const st = fileStatement(adm)
    const lines = await releveLines(st)
    check('A19', lines[0]?.label === '6 j non pris en 2025 : perdus' && lines[0]?.info && lines[1]?.label === 'Droit 2026' && lines[1]?.bal === 'solde 20 j', 'relevé 2026 opens « 6 j non pris en 2025 : perdus », then « Droit 2026 » solde 20 j', showLines(lines))
    check('A19', !lines.some((l) => /^Report de 2025|Dépassement reporté/.test(l.label)), 'no « Report de 2025 » line', showLines(lines))
    await adm.locator('section[aria-labelledby="staff-leave-statement"]').scrollIntoViewIfNeeded()
    await shot(adm, 'A19-T3-2026')
    await st.getByRole('button', { name: 'Année précédente' }).click()
    await pollText(() => stepperYear(st), /2025/, 5000)
    await pollText(() => st.textContent(), /Solde de départ/, 10000)
    const l25 = await releveLines(st)
    const want = [
      ['Solde de départ', '10 j', 'solde 10 j'],
      ['Congé · 17 → 20 novembre 2025', '−4 j', 'solde 6 j'],
      ['Non pris en 2025 : perdus', '−6 j', 'solde 0 j'],
    ]
    const okLines = want.every(([label, amount, bal], i) => l25[i]?.label === label && l25[i]?.amount === amount && l25[i]?.bal === bal) && l25.length === 3
    check('A19', okLines, '2025: « Solde de départ » 10 · « Congé · 17 → 20 novembre 2025 » −4 · « Non pris en 2025 : perdus » −6', showLines(l25))
    await shot(adm, 'A19-T3-2025')
    await st.getByRole('button', { name: 'Année suivante' }).click()
    await sleep(800)
  })

  await scenario('A20', async () => {
    await gotoConges(t3)
    const f = await cardFacts(t3)
    check('A20', f.lines.includes('6 j non pris en 2025 : perdus'), 'her card « 6 j non pris en 2025 : perdus »', f.lines.join(' | '))
    await t3.getByRole('button', { name: 'Voir le relevé' }).click()
    const panel = congesStatement(t3)
    await panel.waitFor({ timeout: 5000 })
    await sleep(500)
    const lines = await releveLines(panel)
    check('A20', lines[0]?.label === '6 j non pris en 2025 : perdus', 'her relevé opens on « 6 j non pris en 2025 : perdus »', showLines(lines))
    await panel.getByRole('button', { name: 'Année précédente' }).click()
    const txt = await pollText(() => panel.textContent(), /Non pris en 2025 : perdus/, 10000)
    check('A20', /Solde de départ/.test(txt) && /Non pris en 2025 : perdus/.test(txt), 'her 2025 relevé is readable', txt.slice(0, 300))
    await shot(t3, 'A20-T3-conges-2025')
  })

  // ============================================================ admin — the tab before T asks anything (A9a, A10a, A21a)
  await scenario('A9', async () => {
    await gotoTab(adm)
    const tc = norm(await stripCard(adm, T.name).textContent())
    check('A9', /22 \/ 22 j/.test(tc) && (await stripCard(adm, T.name).getByRole('progressbar').count()) === 1, `T « 22 / 22 j » with a progress bar`, tc)
    const t5 = norm(await stripCard(adm, T5.name).textContent())
    check('A9', /Droit à définir/.test(t5) && /1 j en attente/.test(t5) && (await stripCard(adm, T5.name).getByRole('progressbar').count()) === 0, 'T5 « Droit à définir » + « 1 j en attente », no bar', t5)
    const t6 = norm(await stripCard(adm, T6.name).textContent())
    check('A9', /Droit à définir/.test(t6) && /4 jours pris · 2 j prévus/.test(t6) && /Prochain congé : 14 → 15 décembre/.test(t6), 'T6 « Droit à définir » + « 4 jours pris · 2 j prévus » + « Prochain congé : 14 → 15 décembre »', t6)
    const ines = norm(await stripCard(adm, INES.name).textContent())
    check('A9', /19 \/ 23 j/.test(ines) && /Prochain congé : 21 → 23 décembre/.test(ines), 'Ines « 19 / 23 j » + « Prochain congé : 21 → 23 décembre »', ines)
    await strip(adm).scrollIntoViewIfNeeded()
    await shot(adm, 'A9-strip-before')
  })

  await scenario('A10', async () => {
    const c5 = pendingCard(adm, T5.name, 'Lundi 21 décembre')
    const link = c5.getByRole('link', { name: `Droit non défini : ouvrir la fiche de ${T5.name}` })
    check('A10', (await link.count()) === 1 && (await link.getAttribute('href')) === `/users/${encodeURIComponent(T5.id)}`, 'T5 card « Droit non défini » links to her file', await c5.textContent().catch(() => ''))
    const txt = norm(await c5.textContent())
    check('A10', /Déjà en congé sur ces dates : Ines Gharbi/.test(txt), 'T5 card « Déjà en congé sur ces dates : Ines Gharbi »', txt)
    const names = await pendingSection(adm).getByRole('button').allTextContents()
    check('A10', names.every((n) => !/^\s*Annuler\s*$/.test(n)) && names.some((n) => /Refuser/.test(n)) && names.some((n) => /Accepter/.test(n)), 'waiting cards offer Refuser · Accepter, no « Annuler »', names.map(norm).join(','))
  })

  await scenario('A21', async () => {
    const c3 = pendingCard(adm, T3.name, '29 → 31 décembre 2025')
    const txt = norm(await c3.textContent())
    check('A21', /Solde 2025 après\s*3 jours/.test(txt), 'T3 card « Solde 2025 après · 3 jours » (2025 read on its 31/12, not 2026\'s 17)', txt)
    await c3.scrollIntoViewIfNeeded()
    await shot(adm, 'A21-T3-card-2025')
  })

  await scenario('B9', async () => {
    await adm.goto(fileUrl(NADIA.id))
    await waitText(adm, /Présences/, 20000)
    await sleep(2500)
    const body = await bodyText(adm)
    check('B9', !/Droit à congé/.test(body) && !/Congé payé · 2026/.test(body) && /Présences/.test(body), 'Dr Nadia\'s file: no congé section, « Présences » present', body.slice(0, 300))
    await shot(adm, 'B9-doctor-file')
  })

  // ============================================================ Paramètres (A22)
  await scenario('A22', async () => {
    await adm.goto(`${WEB}/settings#equipe`)
    const rights = adm.locator('section[aria-labelledby="team-leave-rights"]')
    await rights.locator('li').first().waitFor({ timeout: 20000 })
    await sleep(800)
    const line = async (name) => norm(await rights.locator('li').filter({ hasText: name }).first().textContent().catch(() => ''))
    check('A22', /Ines Gharbi\s*— 2 j par mois/.test(await line(INES.name)), 'Ines « — 2 j par mois »', await line(INES.name))
    check('A22', /— 2 j par mois$/.test(await line(T.name)) && /— 2 j par mois$/.test(await line(T2.name)), 'T and T2 « — 2 j par mois »', `${await line(T.name)} / ${await line(T2.name)}`)
    check('A22', /— 20 j par an$/.test(await line(T3.name)), 'T3 « — 20 j par an »', await line(T3.name))
    const t5li = rights.locator('li').filter({ hasText: T5.name }).first()
    const def = t5li.getByRole('link', { name: `Définir le droit à congé de ${T5.name}` })
    check('A22', /Droit à définir/.test(await line(T5.name)) && (await def.getAttribute('href')) === `/users/${encodeURIComponent(T5.id)}`, 'T5 « Droit à définir » + « Définir » → her file', await line(T5.name))
    await rights.scrollIntoViewIfNeeded()
    await shot(adm, 'A22-settings-1440')
    await adm.getByRole('button', { name: 'Me guider : congés' }).click()
    const bubble = adm.locator('[data-guide-bubble]')
    const bt = await pollText(() => bubble.textContent(), /Les demandes de congé des secrétaires s'activent ici\./, 8000)
    check('A22', /Les demandes de congé des secrétaires s'activent ici\./.test(bt), '« Me guider » opens the tour on the switch', bt)
    await shot(adm, 'A22-guide')
    await bubble.getByRole('button', { name: /Quitter le guide/ }).click().catch(() => adm.keyboard.press('Escape'))
    await sleep(600)
    await adm.setViewportSize({ width: 390, height: 844 })
    await sleep(800)
    await rights.scrollIntoViewIfNeeded()
    const o = await mainOverflow(adm)
    check('A22', o.main <= 1 && o.doc <= 1, '390: no sideways scroll on Paramètres', JSON.stringify(o))
    await shot(adm, 'A22-settings-390')
    await adm.setViewportSize({ width: 1440, height: 900 })
  })

  // ============================================================ « Ajouter une absence » from the tab (A4, B8 picker)
  await scenario('A4', async () => {
    await gotoTab(adm)
    await strip(adm).getByRole('button', { name: 'Ajouter une absence' }).click()
    const dlg = openDialog(adm)
    await dlg.locator('#staff-leave-person').waitFor({ timeout: 8000 })
    check('A4', /Choisir une secrétaire…/.test(norm(await dlg.locator('#staff-leave-person').textContent())) && (await slot(dlg).count()) === 0, 'picker « Choisir une secrétaire… », no count before a pick')
    await dlg.locator('#staff-leave-person').click()
    await adm.getByRole('option', { name: T.name, exact: true }).click()
    await sleep(500)
    const g = await discardPromptAfterEscape(adm)
    check('B8', g.up && /Abandonner les modifications/.test(g.title), 'absence dialog: a picked secretary + Escape → « Abandonner les modifications ? »', g.title)
    if (g.up) await g.prompt.getByRole('button', { name: 'Continuer la saisie' }).click()
    await sleep(400)
    await setDates(dlg, '2026-11-02', '2026-11-04')
    const s1 = await slotText(dlg, /Il lui restera/)
    check('A4', /^3 jours\s*2 → 4 novembre\s*Il lui restera 19 j au 9 novembre$/.test(s1), 'slot « 3 jours · 2 → 4 novembre · Il lui restera 19 j au 9 novembre »', s1)
    const counted = dlg.locator('#staff-leave-counted')
    check('A4', (await counted.inputValue()) === '3', '« Jours à décompter » prefilled 3', await counted.inputValue())
    await dlg.getByRole('button', { name: 'Une demi-journée de moins' }).click()
    await dlg.getByRole('button', { name: 'Une demi-journée de moins' }).click()
    const s2 = await slotText(dlg, /20 j/)
    check('A4', (await counted.inputValue()) === '2' && /Il lui restera 20 j au 9 novembre/.test(s2), 'count 3 → 2 moves it to « Il lui restera 20 j »', `${await counted.inputValue()} · ${s2}`)
    await dlg.locator('#staff-leave-reason').fill('QA absence saisie après coup')
    await shot(adm, 'A4-absence-dialog')
    await dlg.getByRole('button', { name: 'Enregistrer l\'absence' }).click()
    const tt = await toast(adm, /Absence enregistrée/)
    check('A4', Boolean(tt) && tt.includes(`${T.name} — du 2 au 4 novembre`), `toast « Absence enregistrée · ${T.name} — du 2 au 4 novembre »`, tt ?? '(none)')
    const row = psql(`select "Status"||'|'||"CountedDays"::text||'|'||"RequestedDays"::text||'|'||"EnteredByAdmin"::text from "StaffLeaveRequests" where "UserId"='${T.id}' and "StartDate"='2026-11-02'`)
    check('A4', row === '1|2.000|3.000|true', 'SQL: accepted, counted 2 of 3, entered by admin', row)
  })

  // ============================================================ T's request form (B4, A5, C2, B3, B1, A6, B2, A7, B8, B5)
  let dlg
  await scenario('B4', async () => {
    await gotoConges(t)
    dlg = await openAsk(t)
    await t.route('**/team/leave-preview**', (r) => r.fulfill({ status: 500, contentType: 'application/json', body: '{"error":"QA simulated"}' }))
    await setDates(dlg, '2026-11-12', '2026-11-12')
    const s = await slotText(dlg, /Décompte indisponible/)
    check('B4', s === 'Décompte indisponible', 'a failed count reads « Décompte indisponible », never an empty slot', s)
    await shot(t, 'B4-decompte-indisponible')
    await t.unroute('**/team/leave-preview**')
  })

  await scenario('A5', async () => {
    await setDates(dlg, '2026-12-14', '2026-12-18')
    const s = await slotText(dlg, /Il restera/)
    check('A5', /^5 jours\s*14 → 18 décembre\s*Il restera 17 j au 14 décembre$/.test(s), 'slot « 5 jours · 14 → 18 décembre · Il restera 17 j au 14 décembre »', s)
    const sy = (await slot(dlg).boundingBox())?.y ?? 0
    const ey = (await dlg.locator('#staff-leave-end').boundingBox())?.y ?? 0
    const ry = (await dlg.locator('#staff-leave-reason').boundingBox())?.y ?? 0
    check('A5', ey < sy && sy < ry, 'the count sits under Du/Au and above Motif', JSON.stringify({ au: ey, slot: sy, motif: ry }))
    check('A5', (await dlg.getByRole('radiogroup', { name: 'Journée ou demi-journée' }).count()) === 0, 'Du ≠ Au: no « Journée · Matin · Après-midi »')
    // C2 at 1180 (dialog ~448 px): Type = 2 columns, one line each
    const radios = await dlg.getByRole('radiogroup', { name: 'Type de congé' }).getByRole('radio').evaluateAll((els) => els.map((e) => { const r = e.getBoundingClientRect(); return { x: Math.round(r.x), y: Math.round(r.y), h: Math.round(r.height), text: e.textContent.trim() } }))
    const xs = new Set(radios.map((r) => r.x)); const hs = radios.map((r) => r.h)
    check('C2', radios.length === 4 && xs.size === 2 && Math.max(...hs) - Math.min(...hs) <= 1 && Math.max(...hs) <= 40, '1180: Type in 2 columns, equal heights, one line each', JSON.stringify(radios))
    await dlg.locator('#staff-leave-reason').fill('QA R1 vacances')
    await shot(t, 'A5-form-R1-1180')
    await dlg.getByRole('button', { name: 'Envoyer la demande' }).click()
    const tt = await toast(t, /Demande envoyée/)
    check('A5', Boolean(tt) && /du 14 au 18 décembre/.test(tt), 'toast « Demande envoyée · du 14 au 18 décembre »', tt ?? '(none)')
  })

  await scenario('B3', async () => {
    dlg = await openAsk(t)
    await setDates(dlg, '2026-11-12', '2026-11-13')
    await dlg.getByRole('radio', { name: 'Maladie', exact: true }).click()
    const s = await slotText(dlg, /Ne compte pas/)
    check('B3', /^2 jours\s*12 → 13 novembre\s*Ne compte pas sur votre solde$/.test(s), 'Maladie: « Ne compte pas sur votre solde »', s)
    await dlg.getByRole('radio', { name: 'Congé payé', exact: true }).click()
  })

  await scenario('B1', async () => {
    const before = countRequests(T.id)
    await setDates(dlg, '2026-11-15', '2026-11-15')
    const s = await slotText(dlg, /fermé/)
    const red = await slot(dlg).locator('.text-destructive').count()
    check('B1', /Le cabinet est fermé ce jour-là\./.test(s) && red === 1, 'dimanche 15 nov: « Le cabinet est fermé ce jour-là. » in red, before sending', s)
    await dlg.locator('#staff-leave-reason').fill('QA dimanche')
    await dlg.getByRole('button', { name: 'Envoyer la demande' }).click()
    await sleep(1200)
    const banner = norm(await dlg.locator('[role="alert"]').textContent().catch(() => ''))
    check('B1', /Le cabinet est fermé ce jour-là\./.test(banner) && countRequests(T.id) === before, 'send refused by name, nothing written', `${banner} · ${before}→${countRequests(T.id)}`)
    await shot(t, 'B1-ferme')
  })

  await scenario('A6', async () => {
    await setDates(dlg, '2026-12-21', '2026-12-23')
    const s = await slotText(dlg, /avec votre demande en attente/)
    check('A6', /^3 jours\s*21 → 23 décembre\s*Il restera 19 j au 21 décembre · 14 j avec votre demande en attente$/.test(s), '« Il restera 19 j au 21 décembre · 14 j avec votre demande en attente »', s)
    await dlg.locator('#staff-leave-reason').fill('QA R2 fêtes')
    await dlg.getByRole('button', { name: 'Envoyer la demande' }).click()
    check('A6', Boolean(await toast(t, /du 21 au 23 décembre/)), 'R2 sent')
  })

  await scenario('B2', async () => {
    const before = countRequests(T.id)
    dlg = await openAsk(t)
    await setDates(dlg, '2026-12-16', '2026-12-17')
    const s = await slotText(dlg, /Vous avez déjà/)
    check('B2', /Vous avez déjà une demande en attente du 14 au 18 décembre\./.test(s) && (await dlg.locator('#staff-leave-reason').inputValue()) === '', 'overlap named under Du/Au before any motif', s)
    await dlg.locator('#staff-leave-reason').fill('QA chevauchement')
    await dlg.getByRole('button', { name: 'Envoyer la demande' }).click()
    await sleep(1200)
    const banner = norm(await dlg.locator('[role="alert"]').textContent().catch(() => ''))
    check('B2', /Vous avez déjà une demande en attente du 14 au 18 décembre\./.test(banner) && countRequests(T.id) === before, 'refused on send, nothing written', `${banner} · ${before}→${countRequests(T.id)}`)
    await shot(t, 'B2-overlap')
    await closeForm(t)
  })

  await scenario('A7', async () => {
    dlg = await openAsk(t)
    await setDates(dlg, '2026-11-26', '2026-11-26')
    const seg = dlg.getByRole('radiogroup', { name: 'Journée ou demi-journée' })
    const labels = norm(await seg.textContent().catch(() => ''))
    check('A7', /Journée\s*Matin\s*Après-midi/.test(labels) && (await seg.getByRole('radio', { name: 'Journée' }).getAttribute('aria-checked')) === 'true', 'Du = Au → « Journée · Matin · Après-midi », Journée chosen', labels)
    await seg.getByRole('radio', { name: 'Matin' }).click()
    const s = await slotText(dlg, /matin/)
    check('A7', /^0,5 jour\s*Jeudi 26 novembre · matin\s*Il restera 19,5 j au 26 novembre · 11,5 j avec votre demande en attente$/.test(s), 'Matin: « 0,5 jour · Jeudi 26 novembre · matin · Il restera 19,5 j au 26 novembre · 11,5 j avec … »', s)
    const g = await discardPromptAfterEscape(t)
    check('B8', g.up && /Abandonner les modifications/.test(g.title), 'request form: « Matin » (a button) + Escape → « Abandonner les modifications ? »', g.title)
    if (g.up) await g.prompt.getByRole('button', { name: 'Continuer la saisie' }).click()
    await sleep(400)
    check('B8', (await seg.getByRole('radio', { name: 'Matin' }).getAttribute('aria-checked')) === 'true', '« Continuer la saisie » keeps « Matin »')
    await dlg.locator('#staff-leave-reason').fill('QA R3 rendez-vous du matin')
    await shot(t, 'A7-form-matin')
    await dlg.getByRole('button', { name: 'Envoyer la demande' }).click()
    const t1 = await toast(t, /le 26 novembre \(matin\)/)
    check('A7', Boolean(t1), 'toast « le 26 novembre (matin) »', t1 ?? '(none)')
    dlg = await openAsk(t)
    await setDates(dlg, '2026-11-27', '2026-11-27')
    await dlg.getByRole('radiogroup', { name: 'Journée ou demi-journée' }).getByRole('radio', { name: 'Après-midi' }).click()
    const s4 = await slotText(dlg, /après-midi/)
    check('A7', /^0,5 jour\s*Vendredi 27 novembre · après-midi\s*Il restera 19,5 j au 27 novembre · 11 j avec votre demande en attente$/.test(s4), 'Après-midi: « … Il restera 19,5 j au 27 novembre · 11 j avec … »', s4)
    await dlg.locator('#staff-leave-reason').fill('QA R4 après-midi')
    await dlg.getByRole('button', { name: 'Envoyer la demande' }).click()
    check('A7', Boolean(await toast(t, /le 27 novembre \(après-midi\)/)), 'R4 sent')
    const parts = psql(`select string_agg("StartDate"::text||':'||coalesce("HalfDayPart"::text,'-'), ',' order by "StartDate") from "StaffLeaveRequests" where "UserId"='${T.id}' and "HalfDay"`)
    check('A7', parts === '2026-11-26:1,2026-11-27:2', 'SQL: HalfDayPart 1 (matin) on 26, 2 (après-midi) on 27', parts)
  })

  await scenario('B5', async () => {
    dlg = await openAsk(t)
    await setDates(dlg, '2027-01-04', '2027-01-15')
    const s = await slotText(dlg, /dépassement/)
    const strongRed = await slot(dlg).locator('strong.text-destructive').count()
    check('B5', /^11 jours\s*4 → 15 janvier 2027\s*9 j de dépassement au 4 janvier 2027$/.test(s) && strongRed === 1 && !/Il restera/.test(s), '« 9 j de dépassement au 4 janvier 2027 » in red, no « Il restera »', `${s} · red=${strongRed}`)
    await shot(t, 'B5-depassement-form')
    await dlg.locator('#staff-leave-reason').fill('QA R5 janvier')
    await dlg.getByRole('button', { name: 'Envoyer la demande' }).click()
    check('B5', Boolean(await toast(t, /du 4 au 15 janvier 2027/)), 'R5 sent (beyond the balance is allowed)')
  })

  // ============================================================ T's page with requests (A8, B11, D2)
  await scenario('A8', async () => {
    await gotoConges(t)
    await waitText(t, /En cours et à venir/, 10000)
    const y = async (loc) => (await loc.boundingBox())?.y ?? -1
    const order = [
      await y(balanceCard(t)),
      await y(t.locator('#leave-upcoming')),
      await y(t.getByRole('button', { name: 'Voir le relevé' })),
      await y(t.locator('#leave-history')),
    ]
    check('A8', order.every((v, i) => v >= 0 && (i === 0 || v > order[i - 1])), 'order: card → « En cours et à venir » → « Voir le relevé » → « Historique »', JSON.stringify(order))
    const up = (await t.locator('section[aria-labelledby="leave-upcoming"] li').allTextContents()).map(norm)
    const want = [/^.*Jeudi 26 novembre · matin/, /Vendredi 27 novembre · après-midi/, /14 → 18 décembre/, /21 → 23 décembre/, /4 → 15 janvier 2027/]
    check('A8', up.length === 5 && want.every((re, i) => re.test(up[i])), 'upcoming in date order: 26 nov matin, 27 nov après-midi, 14→18 déc, 21→23 déc, 4→15 janv 2027', up.map((u) => u.slice(0, 40)).join(' || '))
    const waitingButtons = await t.locator('section[aria-labelledby="leave-upcoming"] li button').allTextContents()
    check('A8', waitingButtons.length === 5 && waitingButtons.every((b) => norm(b) === 'Retirer la demande'), 'every waiting card says « Retirer la demande » (never « Annuler »)', waitingButtons.map(norm).join(','))
    const f = await cardFacts(t)
    check('A8', termIs(f, 'Acquis', '22 j') && termIs(f, 'Pris', '2 j') && termIs(f, 'Reste', '20 j') && f.pending === 'En attente : 9 j', 'card Acquis 22 − Pris 2 = Reste 20, « En attente : 9 j » apart', `${JSON.stringify(f.terms)} · ${f.pending}`)
    const abs = myRow(t, /2 → 4 novembre/)
    const absText = norm(await abs.textContent())
    check('B11', (await abs.getByRole('button').count()) === 0 && /Saisi par l'administrateur/.test(absText) && /2 j retirés de votre solde/.test(absText), 'the started absence: no button for her, « 2 j retirés de votre solde », « Saisi par l\'administrateur »', absText)
    const cell = async (d) => norm(await t.locator('aside[aria-label="Ce mois-ci"] td > span').filter({ hasText: new RegExp(`^${d}\\b`) }).first().textContent().catch(() => ''))
    const c26 = await cell(26)
    check('A7', /en attente, matin/.test(c26), 'calendar 26: « en attente, matin »', c26)
    await shot(t, 'A8-mes-conges-1180')
  })

  // ============================================================ admin — strip + cards with T's requests (A9b, A10b, B5b)
  await scenario('A9', async () => {
    await gotoTab(adm)
    const tc = norm(await stripCard(adm, T.name).textContent())
    check('A9', /20 \/ 22 j/.test(tc), 'T « 20 / 22 j » after the absence', tc)
    check('A9', /9 j en attente/.test(tc), 'T « 9 j en attente » — the same figure as her card\'s « En attente : 9 j »', tc)
  })

  await scenario('A10', async () => {
    const r2 = norm(await pendingCard(adm, T.name, '21 → 23 décembre').textContent())
    check('A10', new RegExp(`Demande aussi ces dates : ${T5.name} \\(en attente\\)`).test(r2) && /Déjà en congé sur ces dates : Ines Gharbi/.test(r2), 'R2 card: « Demande aussi ces dates : T5 (en attente) » + « Déjà en congé … Ines Gharbi »', r2)
    const c5 = norm(await pendingCard(adm, T5.name, 'Lundi 21 décembre').textContent())
    check('A10', new RegExp(`Demande aussi ces dates : ${T.name} \\(en attente\\)`).test(c5), 'T5 card: « Demande aussi ces dates : T (en attente) »', c5)
    const r1 = norm(await pendingCard(adm, T.name, '14 → 18 décembre').textContent())
    check('A10', /Solde après\s*17 jours\s*au 14 décembre/.test(r1), 'R1 card « Solde après · 17 jours · au 14 décembre »', r1)
    const r3 = norm(await pendingCard(adm, T.name, 'Jeudi 26 novembre · matin').textContent())
    check('A10', /Jeudi 26 novembre · matin/.test(r3), 'R3 card names the half: « Jeudi 26 novembre · matin »', r3)
    const r5card = pendingCard(adm, T.name, '4 → 15 janvier 2027')
    const r5 = norm(await r5card.textContent())
    const red = await r5card.locator('dd .text-destructive').count()
    check('B5', /Solde 2027 après\s*9 jours de dépassement/.test(r5) && red === 1, 'R5 card « Solde 2027 après · 9 jours de dépassement » in red', `${r5} · red=${red}`)
    await r5card.scrollIntoViewIfNeeded()
    await shot(adm, 'A10-cards')
  })

  // ============================================================ dashboard with pointage off (A23)
  let armedTouched = false
  await scenario('A23', async () => {
    await adm.goto(`${WEB}/settings#equipe`)
    const sw = adm.locator('#team-attendance')
    await sw.waitFor({ timeout: 20000 })
    if ((await sw.getAttribute('aria-checked')) !== 'true') { bad('A23', 'pointage was not on before the check', await sw.getAttribute('aria-checked')); return }
    await sw.scrollIntoViewIfNeeded()
    await sw.click()
    armedTouched = true
    check('A23', Boolean(await toast(adm, /Pointage désactivé/)), 'pointage switched off (toast)')
    await adm.goto(`${WEB}/`)
    const pending = Number(psql(`select count(*) from "StaffLeaveRequests" where "ClinicId"='${CLINIC}' and "Status"=0`))
    const link = adm.locator(`a[href="/users?onglet=conges"]`).filter({ hasText: /demandes? de congé en attente/ })
    const found = await link.first().waitFor({ timeout: 20000 }).then(() => true, () => false)
    const lt = found ? norm(await link.first().textContent()) : ''
    check('A23', found && lt === `${pending} demande${pending > 1 ? 's' : ''} de congé en attente` && await waitText(adm, /Équipe aujourd'hui/i, 3000), `pointage off: « Équipe aujourd'hui » « ${pending} demandes de congé en attente »`, lt || '(no link)')
    await shot(adm, 'A23-dashboard-pointage-off')
    if (found) {
      await link.first().click()
      check('A23', await adm.waitForURL(/\/users\?onglet=conges/, { timeout: 10000 }).then(() => true, () => false), 'the link opens /users?onglet=conges', adm.url())
    }
  })
  // put the pointage back exactly as found (also when the scenario above threw)
  if (armedTouched) {
    await adm.goto(`${WEB}/settings#equipe`)
    const sw = adm.locator('#team-attendance')
    await sw.waitFor({ timeout: 20000 }).catch(() => {})
    if ((await sw.getAttribute('aria-checked').catch(() => null)) !== 'true') {
      await sw.scrollIntoViewIfNeeded().catch(() => {})
      await sw.click().catch(() => {})
      await toast(adm, /Pointage activé/)
    }
    psql(`update "Clinics" set "AttendanceLockArmedAtUtc"=null where "Id"='${CLINIC}'`)
    const flags = psql(`select "AttendanceEnabled"::text||','||coalesce("AttendanceLockArmedAtUtc"::text,'null') from "Clinics" where "Id"='${CLINIC}'`)
    check('A23', flags === 'true,null', 'pointage back ON; AttendanceLockArmedAtUtc reset to null (own artefact)', flags)
  }

  // ============================================================ decisions (A11, B8, accept R2/R3, B6)
  await scenario('A11', async () => {
    await gotoTab(adm)
    await pendingCard(adm, T.name, '14 → 18 décembre').getByRole('button', { name: /^Accepter la demande/ }).click()
    const d = openDialog(adm)
    await d.locator('#accept-leave-days').waitFor({ timeout: 8000 })
    await d.getByRole('button', { name: 'Une demi-journée de moins' }).click()
    const g = await discardPromptAfterEscape(adm)
    check('B8', g.up && /Abandonner les modifications/.test(g.title), 'accept dialog: « − » + Escape → « Abandonner les modifications ? »', g.title)
    if (g.up) await g.prompt.getByRole('button', { name: 'Continuer la saisie' }).click()
    await sleep(400)
    const v = await d.locator('#accept-leave-days').inputValue()
    const st = await pollText(() => d.locator('[role="status"]').first().textContent(), /17,5 jours/, 4000)
    check('A11', v === '4,5' && /Solde après au 14 décembre\s*17,5 jours/.test(st), 'count 4,5 kept; « Solde après au 14 décembre · 17,5 jours »', `${v} · ${st}`)
    await shot(adm, 'A11-accept-dialog')
    await d.getByRole('button', { name: 'Accepter', exact: true }).click()
    check('A11', Boolean(await toast(adm, /Congé accepté/)), 'toast « Congé accepté »')
    await sleep(1500)
  })

  await scenario('D1', async () => {
    for (const period of ['21 → 23 décembre', 'Jeudi 26 novembre · matin']) {
      await pendingCard(adm, T.name, period).getByRole('button', { name: /^Accepter la demande/ }).click()
      const d = openDialog(adm)
      await d.locator('#accept-leave-days').waitFor({ timeout: 8000 })
      await d.getByRole('button', { name: 'Accepter', exact: true }).click()
      check('D1', Boolean(await toast(adm, /Congé accepté/)), `accepted « ${period} »`)
      await sleep(1500)
    }
  })

  await scenario('B6', async () => {
    await pendingCard(adm, T.name, '4 → 15 janvier 2027').getByRole('button', { name: /^Refuser la demande/ }).click()
    const d = openDialog(adm)
    await d.locator('#reject-leave-reason').waitFor({ timeout: 8000 })
    await d.getByRole('button', { name: 'Refuser la demande' }).click()
    check('B6', await pollText(() => d.textContent(), /Indiquez le motif du refus\./, 4000).then((x) => /Indiquez le motif du refus\./.test(x)), 'empty motif → « Indiquez le motif du refus. »')
    await d.locator('#reject-leave-reason').fill('Période chargée')
    await d.getByRole('button', { name: 'Refuser la demande' }).click()
    check('B6', Boolean(await toast(adm, /Demande refusée/)), 'toast « Demande refusée »')
    await sleep(1500)
  })

  await scenario('A11', async () => {
    await gotoFile(adm, T.id)
    const row = norm(await fileHistoryRow(adm, '14 → 18 décembre').textContent())
    check('A11', /4,5 j retirés du solde\s*\(5 jours demandés\)/.test(row) && /Accepté par Dr Salma Ben Youssef/.test(row), 'file row « 4,5 j retirés du solde (5 jours demandés) » · « Accepté par Dr Salma Ben Youssef »', row)
  })

  // ============================================================ admin cancels (B7, A12, A13)
  await scenario('A12', async () => {
    await adm.getByRole('button', { name: `Actions pour le congé de ${T.name} du 21 au 23 décembre` }).click()
    await adm.getByRole('menuitem', { name: 'Annuler le congé' }).click()
    const d = openDialog(adm)
    await d.locator('#cancel-leave-reason').waitFor({ timeout: 8000 })
    const head = norm(await d.locator('h2').first().textContent())
    const desc = norm(await d.textContent())
    check('A12', head === `Annuler le congé de ${T.name} du 21 au 23 décembre ?` && /3 jours reviennent sur son solde\./.test(desc), 'title names her congé; « 3 jours reviennent sur son solde. »', `${head} · ${desc.slice(0, 200)}`)
    await d.getByRole('button', { name: 'Annuler le congé', exact: true }).click()
    const e = await pollText(() => d.textContent(), /Indiquez le motif de l'annulation\./, 4000)
    check('B7', /Indiquez le motif de l'annulation\./.test(e), 'empty motif → « Indiquez le motif de l\'annulation. », nothing sent', e.slice(0, 200))
    check('B7', psql(`select "Status" from "StaffLeaveRequests" where "UserId"='${T.id}' and "StartDate"='2026-12-21'`) === '1', 'SQL: still accepted after the refused send')
    await d.locator('#cancel-leave-reason').fill('Erreur de planning')
    await shot(adm, 'A12-admin-cancel')
    await d.getByRole('button', { name: 'Annuler le congé', exact: true }).click()
    check('A12', Boolean(await toast(adm, /Congé annulé/)), 'toast « Congé annulé »')
    await sleep(1500)
  })

  await scenario('A13', async () => {
    const btn = adm.getByRole('button', { name: `Actions pour le congé de ${T.name} du 2 au 4 novembre` })
    check('A13', (await btn.count()) === 1, 'the PAST absence (2 → 4 nov, started) still offers the admin a ⋯ menu')
    await btn.click()
    await adm.getByRole('menuitem', { name: 'Annuler le congé' }).click()
    const d = openDialog(adm)
    await d.locator('#cancel-leave-reason').waitFor({ timeout: 8000 })
    await d.locator('#cancel-leave-reason').fill('Saisie en double')
    await d.getByRole('button', { name: 'Annuler le congé', exact: true }).click()
    check('A13', Boolean(await toast(adm, /Congé annulé/)), 'past congé cancelled (toast « Congé annulé »)')
    await sleep(1500)
    const row = psql(`select "Status"||'|'||coalesce("CancelReason",'') from "StaffLeaveRequests" where "UserId"='${T.id}' and "StartDate"='2026-11-02'`)
    check('A13', row === '3|Saisie en double', 'SQL: cancelled with its motif', row)
  })

  // ============================================================ F4 — the 2025 congé decided now (A21)
  await scenario('A21', async () => {
    await gotoTab(adm)
    await pendingCard(adm, T3.name, '29 → 31 décembre 2025').getByRole('button', { name: /^Accepter la demande/ }).click()
    const d = openDialog(adm)
    await d.locator('#accept-leave-days').waitFor({ timeout: 8000 })
    const st = await pollText(() => d.locator('[role="status"]').first().textContent(), /Solde 2025 après/, 4000)
    check('A21', /^Solde 2025 après\s*3 jours$/.test(st), 'accept dialog « Solde 2025 après · 3 jours »', st)
    await d.getByRole('button', { name: 'Accepter', exact: true }).click()
    check('A21', Boolean(await toast(adm, /Congé accepté/)), 'accepted')
    await gotoFile(adm, T3.id)
    const f = await cardFacts(adm)
    check('A21', f.lines.includes('3 j non pris en 2025 : perdus') && f.big === '20 jours restants', 'after: « 3 j non pris en 2025 : perdus », 2026 still 20', `${f.big} · ${f.lines.join(' | ')}`)
  })

  // ============================================================ T sees the decisions; she withdraws R4 (A11, A12, A13, B6, A14, D2)
  await scenario('A14', async () => {
    await gotoConges(t)
    const r1 = norm(await myRow(t, /14 → 18 décembre/).textContent())
    check('A11', /Congé payé · 4,5 j retirés de votre solde/.test(r1) && /Accepté par Dr Salma Ben Youssef/.test(r1), 'her R1 « Congé payé · 4,5 j retirés de votre solde » · « Accepté par … »', r1)
    const r3 = norm(await myRow(t, /Jeudi 26 novembre · matin/).textContent())
    check('A11', /0,5 j retiré de votre solde/.test(r3), 'her R3 « 0,5 j retiré de votre solde »', r3)
    const r2 = norm(await myRow(t, /21 → 23 décembre/).textContent())
    check('A12', /Annulé par Dr Salma Ben Youssef · «\s*Erreur de planning\s*»/.test(r2), 'her R2 « Annulé par Dr Salma Ben Youssef · « Erreur de planning » »', r2)
    const abs = norm(await myRow(t, /2 → 4 novembre/).textContent())
    check('A13', /Annulé par Dr Salma Ben Youssef · «\s*Saisie en double\s*»/.test(abs), 'her absence « Annulé par … · « Saisie en double » »', abs)
    const r5 = norm(await myRow(t, /4 → 15 janvier 2027/).textContent())
    check('B6', /Motif du refus : «\s*Période chargée\s*»/.test(r5), 'her R5 « Motif du refus : « Période chargée » »', r5)
    const f = await cardFacts(t)
    check('D5', termIs(f, 'Acquis', '22 j') && termIs(f, 'Pris', '0 j') && termIs(f, 'Prévus', '5 j') && termIs(f, 'Reste', '17 j') && f.pending === 'En attente : 0,5 j', 'her card Acquis 22 − Pris 0 − Prévus 5 = Reste 17, « En attente : 0,5 j »', `${JSON.stringify(f.terms)} · ${f.pending}`)
    check('A3', f.lines.includes('À prendre avant le 31/12 : 19 j'), '« À prendre avant le 31/12 : 19 j » (17 + 2)', f.lines.join(' | '))
    await shot(t, 'A14-after-decisions')
    const row = myRow(t, /Vendredi 27 novembre · après-midi/)
    const btn = row.getByRole('button')
    check('A14', norm(await btn.textContent()) === 'Retirer la demande', 'R4 button « Retirer la demande »', await btn.textContent())
    await btn.click()
    const conf = alertDialog(t)
    await conf.waitFor({ timeout: 8000 })
    const ct = norm(await conf.textContent())
    check('A14', /Retirer la demande le 27 novembre \(après-midi\) \?/.test(ct) && !/était accepté/.test(ct), 'confirm « Retirer la demande le 27 novembre (après-midi) ? », nothing in the past tense', ct)
    await conf.getByRole('button', { name: 'Retirer la demande' }).click()
    check('A14', Boolean(await toast(t, /Demande retirée/)), 'toast « Demande retirée »')
    await sleep(1500)
    await gotoConges(t)
    const r4 = norm(await myRow(t, /Vendredi 27 novembre · après-midi/).textContent())
    check('A14', /Demande retirée par vous/.test(r4), 'her row « Demande retirée par vous »', r4)
    const cell = async (d) => norm(await t.locator('aside[aria-label="Ce mois-ci"] td > span').filter({ hasText: new RegExp(`^${d}\\b`) }).first().textContent().catch(() => ''))
    const c26 = await cell(26); const c27 = await cell(27); const c9 = t.locator('aside[aria-label="Ce mois-ci"] td > span').filter({ hasText: /aujourd'hui/ })
    const legend = norm(await t.locator('aside[aria-label="Ce mois-ci"]').textContent())
    check('D2', /congé accepté, matin/.test(c26) && !/en attente|accepté/.test(c27) && (await c9.count()) === 1 && /Congé accepté/.test(legend) && /En attente/.test(legend), 'calendar: 26 « congé accepté, matin », 27 cleared, today ringed, legend', `${c26} / ${c27}`)
    await shot(t, 'A14-withdrawn')
  })

  await scenario('A24', async () => {
    await openBell(t, /Congé annulé/)
    const b = norm(await bodyText(t))
    const has = (re, m) => check('A24', re.test(b), m, b.slice(Math.max(0, b.indexOf('Notifications')), b.indexOf('Notifications') + 900))
    has(/Votre congé du 14 au 18 décembre est accordé · 4,5 j retirés de votre solde\./, 'bell « Votre congé du 14 au 18 décembre est accordé · 4,5 j retirés de votre solde. »')
    has(/Votre congé le 26 novembre matin est accordé · 0,5 j retiré de votre solde\./, 'bell « Votre congé le 26 novembre matin est accordé · 0,5 j retiré de votre solde. »')
    has(/Votre demande de congé du 4 au 15 janvier( 2027)? \(11 jours\) est refusée : «\s*Période chargée\s*»\./, 'bell refusal quotes the motif « Période chargée »')
    has(/Votre congé du 21 au 23 décembre a été annulé par Dr Salma Ben Youssef : «\s*Erreur de planning\s*»\./, 'bell « … a été annulé par Dr Salma Ben Youssef : « Erreur de planning ». »')
    await shot(t, 'A24-bell')
    await t.keyboard.press('Escape').catch(() => {})
    await sleep(3000)
    const ms = await mails(T.email, walkStartDate)
    const subjects = ms.map((m) => m.subject)
    console.log('     mails to T:', subjects)
    const m1 = ms.find((m) => m.subject === 'Congé accepté : du 14 au 18 décembre')
    check('A24', Boolean(m1) && /Retirés de votre solde[\s:|]*4,5 jours/.test(m1.text), 'e-mail « Congé accepté : du 14 au 18 décembre » with « Retirés de votre solde 4,5 jours »', subjects.join(' | '))
    check('A24', subjects.includes('Congé accepté : le 26 novembre matin'), 'e-mail « Congé accepté : le 26 novembre matin »', subjects.join(' | '))
    const m3 = ms.find((m) => /^Congé refusé : du 4 au 15 janvier/.test(m.subject))
    check('A24', Boolean(m3) && /Période chargée/.test(m3.text), 'e-mail « Congé refusé : du 4 au 15 janvier … » carrying « Période chargée »', m3?.subject ?? subjects.join(' | '))
    if (m3 && !/2027/.test(m3.subject)) console.log('     OBSERVATION: the 2027 refusal subject names no year:', m3.subject)
    const m4 = ms.find((m) => m.subject === 'Congé annulé : du 21 au 23 décembre')
    check('A24', Boolean(m4) && /Erreur de planning/.test(m4.text), 'e-mail « Congé annulé : du 21 au 23 décembre » carrying « Erreur de planning »', subjects.join(' | '))
  })

  // ============================================================ T5 — the rule dialog (A15, A16, B8)
  await scenario('A15', async () => {
    await gotoFile(adm, T5.id)
    await adm.getByRole('button', { name: 'Définir la règle' }).click()
    let d = openDialog(adm)
    await d.locator('#leave-rule-days').waitFor({ timeout: 8000 })
    await sleep(800)
    const since = norm(await d.locator('#leave-rule-since').textContent())
    const freq = await d.getByRole('radio', { name: 'par an' }).getAttribute('aria-checked')
    const lost = await d.getByRole('radio', { name: 'Perdus' }).getAttribute('aria-checked')
    check('A15', since === '1er janvier 2026' && freq === 'true' && lost === 'true' && (await d.locator('#leave-rule-days').inputValue()) === '', 'a first rule opens on « 1er janvier 2026 », « par an », « Perdus », days empty', `${since} ${freq} ${lost}`)
    await d.getByRole('button', { name: 'Retour' }).click()
    await sleep(900)
    check('A15', (await alertDialog(adm).count()) === 0 && (await adm.locator('#leave-rule-days').count()) === 0, 'untouched « Retour » closes with no « Abandonner ? »')
    await adm.getByRole('button', { name: 'Définir la règle' }).click()
    d = openDialog(adm)
    await d.locator('#leave-rule-days').waitFor({ timeout: 8000 })
    await d.locator('#leave-rule-days').fill('20')
    const live = await pollText(() => d.locator('[role="status"]').first().textContent(), /Acquis aujourd'hui/, 6000)
    check('A15', live === 'Acquis aujourd\'hui : 20 j · prochain +20 j le 1er janvier 2027', 'live « Acquis aujourd\'hui : 20 j · prochain +20 j le 1er janvier 2027 »', live)
    await shot(adm, 'A15-rule-first')
    await d.getByRole('button', { name: 'Enregistrer la règle' }).click()
    check('A15', Boolean(await toast(adm, /Règle enregistrée/)), 'toast « Règle enregistrée »')
    await sleep(1500)
    const f = await cardFacts(adm)
    check('A15', f.big === '20 jours restants' && f.pending === 'En attente : 1 j', 'T5 « 20 jours restants », « En attente : 1 j »', `${f.big} · ${f.pending}`)
    await gotoTab(adm)
    const c5 = norm(await pendingCard(adm, T5.name, 'Lundi 21 décembre').textContent())
    check('A15', /Solde après\s*19 jours\s*au 21 décembre/.test(c5), 'her card now « Solde après · 19 jours · au 21 décembre »', c5)
    const s5 = norm(await stripCard(adm, T5.name).textContent())
    check('A15', !/Droit à définir/.test(s5) && /20 \/ 20 j/.test(s5), 'strip: « Droit à définir » gone, « 20 / 20 j »', s5)
  })

  await scenario('A16', async () => {
    await gotoFile(adm, T5.id)
    await adm.getByRole('button', { name: 'Modifier la règle' }).click()
    let d = openDialog(adm)
    await d.locator('#leave-rule-days').waitFor({ timeout: 8000 })
    await sleep(800)
    const seed = `${await d.locator('#leave-rule-days').inputValue()} · ${await d.getByRole('radio', { name: 'par an' }).getAttribute('aria-checked')} · ${norm(await d.locator('#leave-rule-since').textContent())}`
    check('A16', seed === '20 · true · 1er janvier 2026', '« Modifier » opens on the latest rule: 20 · par an · 1er janvier 2026', seed)
    await d.getByRole('button', { name: 'Retour' }).click()
    await sleep(900)
    check('A16', (await alertDialog(adm).count()) === 0 && (await adm.locator('#leave-rule-days').count()) === 0, '« Modifier » untouched → Retour, no prompt')
    await adm.getByRole('button', { name: 'Modifier la règle' }).click()
    d = openDialog(adm)
    await d.locator('#leave-rule-days').waitFor({ timeout: 8000 })
    await d.getByRole('radio', { name: 'Reportés' }).click()
    const g = await discardPromptAfterEscape(adm)
    check('B8', g.up && /Abandonner les modifications/.test(g.title), 'rule dialog: « Reportés » + Escape → « Abandonner les modifications ? »', g.title)
    if (g.up) await g.prompt.getByRole('button', { name: 'Abandonner' }).click()
    await sleep(800)
    await adm.getByRole('button', { name: 'Modifier la règle' }).click()
    d = openDialog(adm)
    await d.locator('#leave-rule-days').waitFor({ timeout: 8000 })
    await d.getByRole('radio', { name: 'par mois' }).click()
    await sleep(500)
    const since = norm(await d.locator('#leave-rule-since').textContent())
    await d.locator('#leave-rule-days').fill('2')
    const live = await pollText(() => d.locator('[role="status"]').first().textContent(), /prochain \+2 j/, 6000)
    check('A16', since === '1er décembre 2026' && live === 'Acquis aujourd\'hui : 20 j · prochain +2 j le 1er janvier 2027', 'par mois → « 1er décembre 2026 »; live « Acquis aujourd\'hui : 20 j · prochain +2 j le 1er janvier 2027 » (no December credit)', `${since} · ${live}`)
    await shot(adm, 'A16-rule-monthly')
    await d.getByRole('button', { name: 'Enregistrer la règle' }).click()
    check('A16', Boolean(await toast(adm, /Règle enregistrée/)), 'saved')
    await sleep(1500)
    const f = await cardFacts(adm)
    console.log('     T5 card:', JSON.stringify(f.terms), f.lines)
    check('A16', termIs(f, 'Acquis', '20 j') && !f.lines.some((l) => /^À venir/.test(l)), 'Acquis 20 j (not 22), no « À venir d\'ici le 31/12 »', `${JSON.stringify(f.terms)} · ${f.lines.join(' | ')}`)
    check('A16', f.lines.includes('Droit : 20 j par an · non pris perdus au 31/12 → 2 j par mois à partir du 1er décembre · prochain : +2 j le 1er janvier 2027'), 'rule line « 20 j par an · non pris perdus au 31/12 → 2 j par mois à partir du 1er décembre · prochain : +2 j le 1er janvier 2027 »', f.lines.join(' | '))
    const acc = norm(await adm.locator('section[aria-labelledby="staff-leave-account"]').textContent())
    check('A16', /Règle20 j par an · non pris perdus au 31\/12 → 2 j par mois à partir du 1er décembre/.test(acc) && /Historique des règles/.test(acc), 'account card « Règle » line shows both, « Historique des règles » lists them', acc.slice(0, 300))
    await adm.getByRole('button', { name: 'Modifier la règle' }).click()
    d = openDialog(adm)
    await d.locator('#leave-rule-days').waitFor({ timeout: 8000 })
    await sleep(600)
    const seed2 = `${await d.locator('#leave-rule-days').inputValue()} · ${await d.getByRole('radio', { name: 'par mois' }).getAttribute('aria-checked')} · ${norm(await d.locator('#leave-rule-since').textContent())}`
    check('A16', seed2 === '2 · true · 1er décembre 2026', 'reopened on the latest rule: 2 · par mois · 1er décembre 2026', seed2)
    await d.getByRole('button', { name: 'Retour' }).click()
    await sleep(600)
    await shot(adm, 'A16-T5-file')
  })

  // ============================================================ T6 — Fixer le solde, overdraft (A17, B10)
  await scenario('A17', async () => {
    await gotoFile(adm, T6.id)
    const before = await cardFacts(adm)
    check('A17', before.big === '4 jours pris', 'no droit yet: « 4 jours pris »', before.big)
    await adm.getByRole('button', { name: 'Fixer le solde', exact: true }).click()
    const d = openDialog(adm)
    await d.locator('#leave-opening-days').waitFor({ timeout: 8000 })
    const head = norm(await d.locator('h2').first().textContent())
    const sentence = norm(await d.locator('form#leave-opening-form > div').first().textContent())
    const descW = await d.locator('[data-slot="dialog-description"]').evaluateAll((els) => els.map((e) => Math.round(e.getBoundingClientRect().width)))
    const desc = descW.filter((w) => w > 2).length
    check('A17', head === `Fixer le solde de ${T6.name}` && /^Il lui reste\s*Jours restants\s*j au\s*Au$/.test(sentence) && desc === 0, 'title « Fixer le solde de T6 », sentence « Il lui reste [ ] j au [date] », no visible description', `${head} · ${sentence} · desc=${desc}`)
    check('A17', (await d.locator('#leave-opening-day').inputValue()) === TODAY, 'the date defaults to today', await d.locator('#leave-opening-day').inputValue())
    await d.locator('#leave-opening-days').fill('10')
    const live = await pollText(() => d.locator('[role="status"]').first().textContent(), /Après les congés déjà acceptés/, 6000)
    check('A17', live === 'Après les congés déjà acceptés : 8 j', 'live « Après les congés déjà acceptés : 8 j » (from the server)', live)
    await shot(adm, 'A17-fixer-le-solde')
    await d.getByRole('button', { name: 'Fixer le solde', exact: true }).click()
    check('A17', Boolean(await toast(adm, /Solde fixé/)), 'toast « Solde fixé »')
    await sleep(1500)
    const f = await cardFacts(adm)
    console.log('     T6 card:', f.big, JSON.stringify(f.terms))
    check('A17', f.big === '8 jours restants' && termIs(f, 'Acquis', '10 j') && termIs(f, 'Pris', '4 j') && f.terms['Pris']?.note === 'dont 4 j inclus dans le solde de départ' && termIs(f, 'Prévus', '2 j') && termIs(f, 'Reste', '8 j'), 'Acquis 10 · Pris 4 « dont 4 j inclus dans le solde de départ » · Prévus 2 · Reste 8', `${f.big} ${JSON.stringify(f.terms)}`)
    const box = norm(await adm.locator('section[aria-labelledby="staff-leave-by-type"] li').filter({ hasText: 'Congé payé' }).first().textContent())
    check('A17', /dont 4 j inclus dans le solde de départ/.test(box), `« Absences acceptées » box: ${box}`, box)
    const lines = await releveLines(fileStatement(adm))
    const march = lines.find((l) => /^Congé · 2 → 5 mars/.test(l.label))
    check('A17', march && march.quiet && /sans effet sur le solde/.test(march.day) && !march.bal, 'March line greyed « sans effet sur le solde », no balance under it', showLines(lines))
    check('A17', lines.some((l) => l.label === 'Solde de départ' && l.amount === '10 j') && lines.at(-1)?.label === 'Congé prévu · 14 → 15 décembre' && lines.at(-1)?.bal === 'solde 8 j', '« Solde de départ » 10 j … « Congé prévu · 14 → 15 décembre » solde 8 j', showLines(lines))
    await adm.locator('section[aria-labelledby="staff-leave-statement"]').scrollIntoViewIfNeeded()
    await shot(adm, 'A17-T6-releve')
  })

  await scenario('B10', async () => {
    await adm.getByRole('button', { name: 'Ajouter ou retirer des jours' }).click()
    const d = openDialog(adm)
    await d.locator('#leave-adjust-days').waitFor({ timeout: 8000 })
    await d.getByRole('radio', { name: 'Retirer' }).click()
    await d.locator('#leave-adjust-days').fill('9')
    await d.locator('#leave-adjust-reason').fill('QA dépassement')
    await d.getByRole('button', { name: 'Retirer', exact: true }).click()
    check('B10', Boolean(await toast(adm, /Jours retirés/)), 'toast « Jours retirés »')
    await sleep(1500)
    const f = await cardFacts(adm)
    check('B10', f.big === '1 jour de dépassement' && f.bigRed && f.terms['Dépassement']?.value === '1 j' && f.terms['Dépassement']?.red, 'card « 1 jour de dépassement » red, « = Dépassement 1 j » red', `${f.big} red=${f.bigRed} ${JSON.stringify(f.terms)}`)
    const lines = await releveLines(fileStatement(adm))
    check('B10', lines.at(-1)?.bal === 'dépassement 1 j' && lines.at(-1)?.balRed, 'last relevé line « dépassement 1 j » in red', showLines(lines))
    await shot(adm, 'B10-T6-overdraft')
    await gotoTab(adm)
    const card = stripCard(adm, T6.name)
    const s = norm(await card.textContent())
    const red = await card.locator('strong.text-destructive').count()
    const barClass = await card.getByRole('progressbar').getAttribute('class').catch(() => '')
    check('B10', /1 j de dépassement/.test(s) && red === 1 && /\[&>div\]:bg-destructive/.test(barClass ?? ''), 'strip « 1 j de dépassement » red with a red bar', `${s} · red=${red} · bar=${barClass}`)
    await card.scrollIntoViewIfNeeded()
    await shot(adm, 'B10-strip-overdraft')
  })

  // ============================================================ T2 — F1 (A18)
  await scenario('A18', async () => {
    await gotoFile(adm, T2.id)
    const f = await cardFacts(adm)
    check('A18', f.big === '12 jours restants' && termIs(f, 'Acquis', '12 j') && termIs(f, 'Pris', '5 j') && f.terms['Pris']?.note === 'dont 5 j avant le début du droit à congé' && termIs(f, 'Reste', '12 j') && !f.bigRed, 'Acquis 12 · Pris 5 « dont 5 j avant le début du droit à congé » · Reste 12 (no debt)', `${f.big} ${JSON.stringify(f.terms)}`)
    const lines = await releveLines(fileStatement(adm))
    const march = lines.find((l) => /^Congé · 2 → 6 mars/.test(l.label))
    check('A18', march && march.quiet && /sans effet sur le solde/.test(march.day) && !lines.some((l) => /Dépassement reporté/.test(l.label)) && lines.at(-1)?.bal === 'solde 12 j', 'March greyed « sans effet sur le solde », relevé ends « solde 12 j »', showLines(lines))
    const box = norm(await adm.locator('section[aria-labelledby="staff-leave-by-type"] li').filter({ hasText: 'Congé payé' }).first().textContent())
    check('A18', /dont 5 j avant le début du droit à congé/.test(box), '« Absences acceptées » box carries the same note', box)
    await shot(adm, 'A18-T2-file')
  })

  // ============================================================ Ines — the owner's data, read-only (A25)
  await scenario('A25', async () => {
    const IC = await mk({ width: 1536, height: 730 })
    const ip = IC.page
    const s0 = psql('select now()')
    await signIn(ip, INES.email, false, '/conges')
    captureFamily(INES.id, s0)
    await gotoConges(ip)
    await waitText(ip, /En cours et à venir/, 10000)
    const f = await cardFacts(ip)
    console.log('     Ines card:', f.big, JSON.stringify(f.terms), f.lines)
    check('A25', f.big === '19 jours restants' && termIs(f, 'Acquis', '23 j') && termIs(f, 'Pris', '1 j') && termIs(f, 'Prévus', '3 j') && termIs(f, 'Reste', '19 j'), 'Ines: Acquis 23 − Pris 1 − Prévus 3 = Reste 19', `${f.big} ${JSON.stringify(f.terms)}`)
    check('A25', f.lines.includes('À venir d\'ici le 31/12 : +2 j') && f.lines.includes('À prendre avant le 31/12 : 21 j') && f.lines.includes('Total pris depuis le début : 1 j'), '« À venir … +2 j », « À prendre avant le 31/12 : 21 j », « Total pris … 1 j »', f.lines.join(' | '))
    const up = norm(await myRow(ip, /21 → 23 décembre/).textContent())
    check('A25', /Congé payé · 3 j retirés de votre solde/.test(up), '« 21 → 23 décembre » « Congé payé · 3 j retirés de votre solde »', up)
    const y = async (loc) => (await loc.boundingBox())?.y ?? -1
    const order = [await y(balanceCard(ip)), await y(ip.locator('#leave-upcoming')), await y(ip.getByRole('button', { name: 'Voir le relevé' })), await y(ip.locator('#leave-history'))]
    check('A25', order.every((v, i) => v >= 0 && (i === 0 || v > order[i - 1])), 'order: card → upcoming → relevé → history', JSON.stringify(order))
    const o = await mainOverflow(ip)
    check('C1', o.main <= 1 && o.doc <= 1, 'Ines /conges at 1536×730: no sideways scroll', JSON.stringify(o))
    await shot(ip, 'A25-ines-1536x730')
    await signOutVia(ip).catch((e) => console.log('   (ines sign-out failed)', e.message))
    await IC.ctx.close()
  })

  // ============================================================ device sweep (C1–C5)
  const widths = [[320, 640], [390, 844], [820, 1180], [1180, 820], [1536, 730]]
  await scenario('C1', async () => {
    const surfaces = [
      ['conges', t, async (p) => gotoConges(p)],
      ['tab', adm, async (p) => gotoTab(p)],
      ['file-T6', adm, async (p) => gotoFile(p, T6.id)],
      ['settings', adm, async (p) => { await p.goto(`${WEB}/settings#equipe`); await p.locator('section[aria-labelledby="team-leave-rights"] li').first().waitFor({ timeout: 20000 }); await sleep(800) }],
    ]
    for (const [name, page, open] of surfaces) {
      for (const [w, h] of widths) {
        await page.setViewportSize({ width: w, height: h })
        await open(page)
        const o = await mainOverflow(page)
        check('C1', o.main <= 1 && o.doc <= 1, `${name} ${w}×${h}: no sideways scroll`, JSON.stringify(o))
        await shot(page, `C1-${name}-${w}x${h}`)
        if (name === 'tab' && w === 320) {
          const names = await strip(page).locator('a span.min-w-0').evaluateAll((els) => els.map((e) => ({ t: e.textContent, w: Math.round(e.getBoundingClientRect().width) })))
          check('C4', names.length >= 5 && names.every((n) => n.w >= 90), 'team strip at 320: every name ≥ 90 px wide', JSON.stringify(names))
          await strip(page).scrollIntoViewIfNeeded()
          await shot(page, 'C4-strip-320')
        }
      }
    }
    await adm.setViewportSize({ width: 1440, height: 900 })
    await t.setViewportSize({ width: 1180, height: 820 })
  })

  await scenario('C2', async () => {
    for (const [w, h] of [[320, 640], [820, 1180], [1536, 730]]) {
      await t.setViewportSize({ width: w, height: h })
      await gotoConges(t)
      const d = await openAsk(t)
      const radios = await d.getByRole('radiogroup', { name: 'Type de congé' }).getByRole('radio').evaluateAll((els) => els.map((e) => { const r = e.getBoundingClientRect(); return { x: Math.round(r.x), h: Math.round(r.height) } }))
      const hs = radios.map((r) => r.h)
      check('C2', radios.length === 4 && new Set(radios.map((r) => r.x)).size === 2 && Math.max(...hs) - Math.min(...hs) <= 1 && Math.max(...hs) <= 40, `${w}×${h}: Type in 2 columns, one line each`, JSON.stringify(radios))
      const send = await d.getByRole('button', { name: 'Envoyer la demande' }).boundingBox()
      const box = await d.boundingBox()
      if (w === 320) check('C3', box && Math.abs(box.y + box.height - h) <= 2 && send && send.y + send.height <= h, '320: a bottom sheet, « Envoyer la demande » on screen', JSON.stringify({ box, send }))
      else check('C3', send && send.y + send.height <= h, `${w}×${h}: « Envoyer la demande » on screen`, JSON.stringify(send))
      const o = await mainOverflow(t)
      check('C1', o.doc <= 1, `${w}×${h} with the form open: no sideways scroll`, JSON.stringify(o))
      await shot(t, `C2-form-${w}x${h}`)
      await closeForm(t)
    }
    await t.setViewportSize({ width: 1180, height: 820 })
  })

  await scenario('C5', async () => {
    await t.setViewportSize({ width: 390, height: 844 })
    await gotoConges(t)
    await t.getByRole('button', { name: 'Voir le relevé' }).click()
    await congesStatement(t).waitFor({ timeout: 5000 })
    const isCoarse = await coarse(t, true)
    check('C5', isCoarse, 'the page reads (pointer: coarse)', String(isCoarse))
    const step = await heights(congesStatement(t).getByRole('button'))
    check('C5', step.length >= 2 && step.every((v) => v >= 44), 'year stepper ≥ 44 px', JSON.stringify(step))
    const rows = await heights(t.locator('section[aria-labelledby="leave-upcoming"] li button, section[aria-labelledby="leave-history"] li button'))
    check('C5', rows.every((v) => v >= 44), 'her row buttons ≥ 44 px', JSON.stringify(rows))
    await t.locator('div.fixed button', { hasText: 'Demander un congé' }).click()
    const d = openDialog(t)
    await d.locator('#staff-leave-start').waitFor({ timeout: 8000 })
    await sleep(600)
    const radios = await heights(d.getByRole('radio'))
    const fallbackButtons = await heights(d.getByRole('button', { name: /^(Annuler|Envoyer la demande)$/ }))
    const inputs = await heights(d.locator('input, textarea'))
    console.log('     coarse inputs (printed, not asserted):', JSON.stringify(inputs))
    check('C5', radios.length >= 7 && radios.every((v) => v >= 44), 'request form radios (type + journée) ≥ 44 px', JSON.stringify(radios))
    check('C5', fallbackButtons.length >= 2 && fallbackButtons.every((v) => v >= 44), 'request form footer buttons ≥ 44 px', JSON.stringify(fallbackButtons))
    await sleep(1200) // run 11: the shot caught the sheet mid slide-in
    await shot(t, 'C5-form-coarse-390')
    await closeForm(t)
    await coarse(t, false)
    await t.setViewportSize({ width: 1180, height: 820 })
    await adm.setViewportSize({ width: 820, height: 1180 })
    await gotoTab(adm)
    const admCoarse = await coarse(adm, true)
    const cardButtons = await heights(pendingSection(adm).locator('li button'))
    check('C5', admCoarse && cardButtons.length >= 2 && cardButtons.every((v) => v >= 44), '820 coarse: decision card buttons ≥ 44 px', JSON.stringify(cardButtons))
    await shot(adm, 'C5-cards-coarse-820')
    await coarse(adm, false)
    await adm.setViewportSize({ width: 1440, height: 900 })
  })

  // ============================================================ regression (D3, D4) and the last decision (B12)
  await scenario('D3', async () => {
    await adm.goto(`${WEB}/users?onglet=presences`)
    const okp = await adm.getByRole('button', { name: 'Ajouter un pointage' }).first().waitFor({ timeout: 25000 }).then(() => true, () => false)
    check('D3', okp, '« Présences » tab renders (« Ajouter un pointage »)')
    await shot(adm, 'D3-presences')
  })

  await scenario('D4', async () => {
    await adm.goto(`${WEB}/`)
    const okd = await waitText(adm, /Équipe aujourd'hui/i, 25000)
    const pending = Number(psql(`select count(*) from "StaffLeaveRequests" where "ClinicId"='${CLINIC}' and "Status"=0`))
    const link = pending > 0 ? await waitText(adm, new RegExp(`${pending} demandes? de congé en attente`), 8000) : true
    check('D4', okd && link, `pointage on: « Équipe aujourd'hui » renders with « ${pending} demande(s) de congé en attente »`)
    await shot(adm, 'D4-dashboard')
  })

  await scenario('B12', async () => {
    await gotoTab(adm)
    await pendingCard(adm, T5.name, 'Lundi 21 décembre').getByRole('button', { name: /^Refuser la demande/ }).click()
    const d = openDialog(adm)
    await d.locator('#reject-leave-reason').waitFor({ timeout: 8000 })
    await d.locator('#reject-leave-reason').fill('QA refus')
    await d.getByRole('button', { name: 'Refuser la demande' }).click()
    check('B12', Boolean(await toast(adm, /Demande refusée/)), 'T5 refused (toast)')
  })

  await scenario('D1', async () => {
    const s = psql(`select string_agg(to_char("StartDate",'MM-DD')||':'||"Status", ',' order by "StartDate") from "StaffLeaveRequests" where "UserId"='${T.id}'`)
    check('D1', s === '11-02:3,11-26:1,11-27:3,12-14:1,12-21:3,01-04:2', 'SQL T: absence cancelled · R3 accepted · R4 withdrawn · R1 accepted · R2 cancelled · R5 refused', s)
    const s3 = psql(`select string_agg("StartDate"::text||':'||"Status", ',' order by "StartDate") from "StaffLeaveRequests" where "UserId"='${T3.id}'`)
    check('D1', s3 === '2025-11-17:1,2025-12-29:1', 'SQL T3: both accepted', s3)
    const s5 = psql(`select "Status" from "StaffLeaveRequests" where "UserId"='${T5.id}'`)
    check('D1', s5 === '2', 'SQL T5: refused', s5)
    const r1 = psql(`select "CountedDays"::text from "StaffLeaveRequests" where "UserId"='${T.id}' and "StartDate"='2026-12-14'`)
    check('D1', r1 === '4.500', 'SQL R1 counted 4,5', r1)
  })
} finally {
  // ============================================================ summary + own-artefact cleanup
  console.log('\n================ SUMMARY')
  const pass = results.filter((r) => r.outcome === 'pass').length
  const fail = results.filter((r) => r.outcome === 'fail').length
  const skipped = results.filter((r) => r.outcome === 'skip').length
  console.log(`pass ${pass} · fail ${fail} · skip ${skipped}`)
  findings.forEach((f) => console.log('  ❌', f.id, '—', f.m, f.d ? `(${f.d})` : ''))
  fs.writeFileSync(path.join(OUT, 'last-run-conges-review-fixes.json'), JSON.stringify({ at: new Date().toISOString(), stamp, people, arrangeLog, results }, null, 2))

  if (process.env.QA_KEEP !== '1') {
    console.log('\nCleanup (own artefacts):')
    await signOutVia(t).catch(() => {})
    await signOutVia(t3).catch(() => {})
    await signOutVia(adm).catch(() => {})
    await sleep(1000)
    const me = await apiLogin(ADMIN.email, 'QA cleanup congés', true)
    const list = await api('GET', '/users?pageSize=200', undefined, me.token)
    const users = (list.json?.value ?? list.json)?.items ?? (list.json?.value ?? list.json) ?? []
    for (const p of Object.values(people)) {
      const u = users.find?.((x) => x.id === p.id)
      if (u) console.log('  deactivate', p.name, (await api('PUT', `/users/${encodeURIComponent(p.id)}/status`, { isActive: false, version: u.version }, me.token)).status)
    }
    await apiLogout(me)
    const fam = ownFamilies.map((f) => `'${f}'`).join(',') || "'00000000-0000-0000-0000-000000000000'"
    console.log('  ', psql(`
      delete from "NotificationReads" where "NotificationId" in (select "Id" from "StaffNotifications" where "ClinicId"='${CLINIC}' and "CreatedAt" >= '${walkStart}' and ("TargetUserId" in (${ids}) or ("Title" || "Message") like '%${stamp}%'));
      delete from "NotificationDismissals" where "NotificationId" in (select "Id" from "StaffNotifications" where "ClinicId"='${CLINIC}' and "CreatedAt" >= '${walkStart}' and ("TargetUserId" in (${ids}) or ("Title" || "Message") like '%${stamp}%'));
      delete from "StaffNotifications" where "ClinicId"='${CLINIC}' and "CreatedAt" >= '${walkStart}' and ("TargetUserId" in (${ids}) or ("Title" || "Message") like '%${stamp}%');
      delete from "StaffLeaveRequests" where "UserId" in (${ids});
      delete from "StaffLeaveAllowanceEntries" where "UserId" in (${ids});
      update "SessionFamilies" set "EndedAtUtc"=now(), "EndedReason"='qa' where ("UserId" in (${ids}) or "Id" in (${fam})) and "EndedAtUtc" is null;
      select 'left: requests ' || (select count(*) from "StaffLeaveRequests" where "UserId" in (${ids})) || ' · entries ' || (select count(*) from "StaffLeaveAllowanceEntries" where "UserId" in (${ids})) || ' · active throwaways ' || (select count(*) from "Users" where "Id" in (${ids}) and "IsActive");`))
    const flagsAfter = psql(`select "LeaveRequestsEnabled"::text||','||"AttendanceEnabled"::text||','||"AttendanceLockEnabled"::text||','||coalesce("AttendanceLockArmedAtUtc"::text,'null') from "Clinics" where "Id"='${CLINIC}'`)
    const inesAfter = inesHash()
    console.log(`  P3 flags after: ${flagsAfter} (before ${P.flags}) · P2 Ines hash after: ${inesAfter} (${inesAfter === P.ines ? 'unchanged' : 'CHANGED'})`)
  }
  await browser.close().catch(() => {})
}
process.exit(0)
