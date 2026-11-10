// QA walk — pointage review fixes F1–F36 (qa/plan-review-fixes.md). One launch, every scenario, findings collected,
// nothing fixed. My OWN headless Chrome (playwright-core `channel: 'chrome'`) — never the owner's CDP windows 9333/9334.
// Contexts: ADM = Dr Salma · T1 = throwaway secretary T on an ordinary PC · T2 = T on a second PC made borne « QA ».
// Stack: web :3096 · API :5096 · DB clinic_pointage_demo (the owner's demo — only own artefacts are written by SQL).
//   node walk-review-fixes.mjs
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
const INES = { id: 'local|22222222-2222-4222-8222-222222222222', name: 'Ines Gharbi' }
const OTHER = { id: 'local|8592a31c-22fa-436f-92a5-67bd41e021cf', name: 'QA Secrétaire 991861' }
const NADIA = { id: 'local|11111111-1111-4111-8111-111111111111', name: 'Dr Nadia Trabelsi' }
const LINUX_UA = 'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36'
const P1 = [0, 4, 8, 5] // T's first schéma
const P2 = [2, 4, 6, 7] // T's schéma after « Schéma oublié ? »
const WRONG = [6, 7, 8, 5]
const SHOTS = path.join(HERE, 'shots')
fs.mkdirSync(SHOTS, { recursive: true })

const local = (d = new Date()) => new Date(d.getTime() + 3600_000) // Tunisia, UTC+1
const hhmm = (d) => d.toISOString().slice(11, 16)
const TODAY = local().toISOString().slice(0, 10)
// The server lists the live bornes in its own order.
const BOTH_BORNES = /Bornes : «\s*(Accueil|QA)\s*», «\s*(Accueil|QA)\s*»/
const AT_BOTH = /Pointez votre départ à la borne «\s*(Accueil|QA)\s*» ou «\s*(Accueil|QA)\s*»/
const minutesOf = (hm) => Number(hm.slice(0, 2)) * 60 + Number(hm.slice(3, 5))

// ---------------------------------------------------------------- findings
const findings = []
const results = []
const ok = (id, m) => { console.log(`  ✅ [${id}] ${m}`); results.push({ id, outcome: 'pass', m }) }
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }); results.push({ id, outcome: 'fail', m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); results.push({ id, outcome: 'skip', m: why }) }
const check = (id, cond, m, d = '') => (cond ? ok(id, m) : bad(id, m, String(d).replace(/\s+/g, ' ').slice(0, 400)))
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
const norm = (s) => String(s ?? '').replace(/\s+/g, ' ').trim()
async function waitText(page, re, timeout = 10000) {
  const end = Date.now() + timeout
  while (Date.now() < end) {
    if (re.test(await bodyText(page))) return true
    await sleep(250)
  }
  return false
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
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`), timeout: 15000 }).catch((e) => console.log('   (shot failed)', name, e.message))
}
const gate = (page) => page.locator('[data-clock-in-gate]')
const gateUp = (page, timeout = 12000) => gate(page).waitFor({ state: 'visible', timeout }).then(() => true, () => false)
const gateGone = (page, timeout = 12000) => gate(page).waitFor({ state: 'detached', timeout }).then(() => true, () => false)
const mark = (page) => page.evaluate(() => { window.__qaMark = Math.random(); return window.__qaMark })
const marked = (page, v) => page.evaluate((x) => window.__qaMark === x, v).catch(() => false)
const pads = (scope) => scope.locator('svg[role="application"]:visible')

async function dragSchema(page, dots, scope = page) {
  const pad = pads(scope).last()
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
async function typeSchema(page, dots, scope = page) {
  const pad = pads(scope).last()
  await pad.waitFor({ timeout: 8000 })
  await pad.focus()
  for (const d of dots) await page.keyboard.press(String(d + 1))
  await page.keyboard.press('Enter')
}
const quiet = (context) =>
  context.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const guardFeedback = (page) =>
  page.addLocatorHandler(page.locator('[aria-labelledby="feedback-prompt-title"]'), async (c) => {
    await c.getByRole('button', { name: 'Plus tard' }).click().catch(() => {})
  })

async function signIn(page, email, withTotp, target = '/conges') {
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
  await page.getByRole('button', { name: 'Mon compte' }).click()
  await page.getByRole('menuitem', { name: 'Se déconnecter' }).click()
}
async function stepUpConfirm(page) {
  const code = page.locator('[role="dialog"]:visible input[inputmode=numeric]').last()
  await code.waitFor({ timeout: 10000 })
  await code.fill(await freshTotp())
  await openDialog(page).getByRole('button', { name: 'Confirmer' }).click()
}
async function openBell(page, re = null) {
  await page.getByRole('button', { name: 'Notifications' }).first().click()
  await sleep(1200)
  if (!re) return
  if (await waitText(page, re, 8000)) return
  // run 9, drive 3: once the row was not in the first read — close, reopen once
  await page.keyboard.press('Escape').catch(() => {})
  await sleep(800)
  await page.getByRole('button', { name: 'Notifications' }).first().click()
  await waitText(page, re, 10000)
}
async function closeOverlay(page) { await page.keyboard.press('Escape').catch(() => {}); await sleep(300) }

// CDP coarse pointer (a desktop browser never matches `(pointer: coarse)` by resizing). Touch only: the width is
// Playwright's viewport (a CDP metrics override fought it in run 9's first drive), and never navigate while it is on.
// Run 9, drive 2: detaching the CDP session reverted the emulation, so it stays attached until « off ».
const cdpOf = new Map()
async function coarse(page, on, w = 390, h = 844) {
  if (on) {
    await page.setViewportSize({ width: w, height: h })
    const cdp = await page.context().newCDPSession(page)
    cdpOf.set(page, cdp)
    await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 })
  } else {
    const cdp = cdpOf.get(page)
    cdpOf.delete(page)
    if (cdp) {
      await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: false }).catch(() => {})
      await cdp.detach().catch(() => {})
    }
  }
  await sleep(600)
  return page.evaluate(() => matchMedia('(pointer: coarse)').matches)
}
const heightOf = (loc) => loc.boundingBox().then((b) => (b ? Math.round(b.height) : -1)).catch(() => -1)
const mainOverflow = (page) => page.evaluate(() => {
  const m = document.querySelector('main#contenu-principal') ?? document.querySelector('main')
  return { main: m ? m.scrollWidth - m.clientWidth : -1, doc: document.documentElement.scrollWidth - innerWidth }
})

// API
async function api(method, url, body, token, headers = {}) {
  const r = await fetch(`${API}${url}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  let json = null
  try { json = await r.json() } catch { /* empty */ }
  return { status: r.status, json }
}
const familyOfToken = (t) => {
  try { return JSON.parse(Buffer.from((t ?? '').split('.')[1], 'base64url').toString('utf8'))['family_id'] ?? null } catch { return null }
}
async function apiLogin(email, deviceLabel, withTotp = false) {
  const body = { email, password: PASSWORD, deviceLabel }
  if (withTotp) body.totpCode = await freshTotp()
  const r = await api('POST', '/auth/login', body)
  const v = r.json?.value ?? {}
  return { token: v.accessToken, refresh: v.refreshToken, family: familyOfToken(v.accessToken), status: r.status }
}
async function apiStepUp(token) {
  const r = await api('POST', '/auth/step-up', { action: 'register-attendance-terminal', totpCode: await freshTotp() }, token)
  return r.json?.value?.confirmationToken ?? r.json?.confirmationToken ?? null
}

// ================================================================ arrange (own artefacts only)
console.log(`Today ${TODAY}, local ${hhmm(local())}`)
const walkStart = psql(`select now()`)
const throwawayFile = path.join(OUT, 'throwaway.json')
let T
{
  let known = fs.existsSync(throwawayFile) ? JSON.parse(fs.readFileSync(throwawayFile, 'utf8')) : null
  const id = known?.create?.userId
  const active = id ? psql(`select coalesce("IsActive"::text,'') from "Users" where "Id"='${id}'`) : ''
  if (active !== 'true') {
    console.log('Creating a throwaway secretary…')
    const adm = await apiLogin(ADMIN.email, 'QA arrange', true)
    const stamp = String(Date.now()).slice(-6)
    const email = `qa.secr.p${stamp}@cabinet-ibnkhaldoun.tn`
    const fullName = `QA Pointage ${stamp}`
    const c = await api('POST', '/users', { email, fullName, role: 'secretary' }, adm.token)
    const token = c.json?.invitationUrl?.split('#t=')[1]
    await api('POST', '/auth/invitation/accept', { token, password: PASSWORD })
    await api('POST', '/auth/logout', { refreshToken: adm.refresh })
    known = { email, fullName, create: c.json }
    fs.writeFileSync(throwawayFile, JSON.stringify(known, null, 2))
  }
  T = { id: known.create.userId, email: known.email, name: known.fullName }
}
console.log('T =', T.name, T.id)
const tLines = (where = '') => psql(`select count(*) from "AttendanceEntries" where "UserId"='${T.id}' ${where}`)
const tHash = () => psql(`select coalesce("AttendanceSchemaHash",'') from "Users" where "Id"='${T.id}'`)

// API sessions the « Ajouter une borne » list must classify: T's tablet, T's phone, the admin's phone.
const tIpad = await apiLogin(T.email, 'Safari sur iPad')
const tPhone = await apiLogin(T.email, 'Chrome sur Android')
const admPhone = await apiLogin(ADMIN.email, 'Chrome sur Android', true)
const admToken = admPhone.token
console.log('API sessions:', { ipad: tIpad.family, tPhone: tPhone.family, admPhone: admPhone.family })
const b6Sessions = []
const tClock = async () => (await api('GET', '/pointage/me/clock-status', undefined, tPhone.token)).json ?? {}

// ================================================================ browser
const browser = await chromium.launch({ channel: 'chrome', headless: true })
const mk = async (opts) => {
  const ctx = await browser.newContext({ locale: 'fr-FR', timezoneId: 'Africa/Tunis', ...opts })
  await quiet(ctx)
  const page = await ctx.newPage()
  await guardFeedback(page)
  return { ctx, page }
}
const A = await mk({ viewport: { width: 1440, height: 900 } })
const S1 = await mk({ viewport: { width: 1180, height: 820 } })
const S2 = await mk({ viewport: { width: 820, height: 1180 }, userAgent: LINUX_UA })
const adm = A.page
const t1 = S1.page
const t2 = S2.page
await signIn(adm, ADMIN.email, true, '/settings')
await signIn(t1, T.email, false, '/conges')
await signIn(t2, T.email, false, '/conges')
const t2Family = psql(`select "Id" from "SessionFamilies" where "UserId"='${T.id}' and "DeviceLabel"='Chrome sur Linux' and "EndedAtUtc" is null order by "CreatedAt" desc limit 1`)
const t1Family = psql(`select "Id" from "SessionFamilies" where "UserId"='${T.id}' and "DeviceLabel"='Chrome sur Windows' and "EndedAtUtc" is null order by "CreatedAt" desc limit 1`)
console.log('Signed in — adm', adm.url(), '· t1', t1.url(), t1Family, '· t2', t2.url(), t2Family)
await shot(t1, 'R9-T1-signed-in')
await t2.goto('about:blank') // T2 must not claim « QA » before the pending state is read (A5)

const escRe = (x) => x.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
const settingsRow = (page, name) => page.locator('section[aria-labelledby="bornes-title"] li')
  .filter({ has: page.locator('p.text-sm.font-medium', { hasText: new RegExp(`^${escRe(name)}(cet appareil)?$`) }) }).first()
async function gotoSettings(page) {
  await page.goto(`${WEB}/settings`)
  const sec = page.locator('section[aria-labelledby="pointage-setup-title"]')
  const ok1 = await sec.waitFor({ timeout: 20000 }).then(() => true, () => false)
  if (!ok1) {
    console.log('   (gotoSettings: section not visible after 20 s — url', page.url(), '— debug shot, one reload)')
    await shot(page, `DEBUG-settings-${Date.now()}`)
    await page.reload()
    await sec.waitFor({ timeout: 25000 })
  }
  await sleep(800)
}
async function openAddBorne(page) {
  await gotoSettings(page)
  const btn = page.getByRole('button', { name: 'Ajouter une borne' }).last()
  await btn.scrollIntoViewIfNeeded()
  await btn.click()
  const dlg = openDialog(page)
  await dlg.locator('#borne-name').waitFor({ timeout: 10000 })
  await waitText(page, /actif il y a|Aucun autre appareil connecté/, 10000)
  return dlg
}
async function closeBorneDialog(page) {
  await openDialog(page).getByRole('button', { name: 'Retour' }).click().catch(() => {})
  await sleep(400)
  const discard = alertDialog(page).getByRole('button', { name: 'Abandonner' })
  if (await discard.count()) await discard.click()
  await sleep(400)
}
const presencesUrl = `${WEB}/users?onglet=presences`
async function gotoPresences(page) {
  await page.goto(presencesUrl)
  await page.getByRole('button', { name: 'Ajouter un pointage' }).first().waitFor({ timeout: 25000 })
  await sleep(1200)
}
const todaySection = (page) => page.locator('section[aria-labelledby="presences-today"]')
const todayRow = (page, name) => todaySection(page).locator('li', { hasText: name }).first()

// ================================================================ Tier A/B — Paramètres, before anything moves
await scenario('A1', async () => {
  await gotoSettings(adm)
  const order = await adm.evaluate(() => {
    const h = document.getElementById('horaires')
    const e = document.getElementById('equipe')
    if (!h || !e) return { found: false }
    const cards = [...h.parentElement.children]
    return { found: true, same: h.parentElement === e.parentElement, hi: cards.indexOf(h), ei: cards.indexOf(e), next: h.nextElementSibling?.id ?? h.nextElementSibling?.textContent?.slice(0, 40) }
  })
  check('A1', order.found && order.same && order.ei === order.hi + 1, '« Équipe » (#equipe) is the card right after « Horaires » (#horaires)', JSON.stringify(order))
  await adm.goto(`${WEB}/settings#equipe`)
  await adm.locator('#equipe').waitFor({ timeout: 25000 })
  await sleep(2500)
  const top = await adm.locator('#equipe').evaluate((el) => Math.round(el.getBoundingClientRect().top)).catch(() => -9999)
  check('A1', top >= 0 && top < 300, 'arriving on /settings#equipe brings the card into view', `top=${top}`)
  await shot(adm, 'A1-equipe-after-horaires')
})

await scenario('A2', async () => {
  await gotoSettings(adm)
  const sec = adm.locator('section[aria-labelledby="pointage-setup-title"]')
  await sec.scrollIntoViewIfNeeded()
  const steps = (await sec.locator('ol > li').allInnerTexts()).map(norm)
  console.log('   steps:', JSON.stringify(steps))
  check('A2', steps.length === 4, 'four numbered steps', steps.length)
  check('A2', /Horaires du cabinet/.test(steps[0]) && /Définis/.test(steps[0]) && /faite/.test(steps[0]), '1 « Horaires du cabinet · Définis » done', steps[0])
  check('A2', /Borne/.test(steps[1]) && /Accueil · Ines Gharbi · Chrome sur Windows/.test(steps[1]) && /faite/.test(steps[1]), '2 « Borne · Accueil · Ines Gharbi · Chrome sur Windows » done', steps[1])
  check('A2', /Schémas/.test(steps[2]) && /1 sur 3 créé/.test(steps[2]) && steps[2].includes(T.name) && steps[2].includes(OTHER.name), `3 « Schémas · 1 sur 3 créé · à créer : ${T.name}, ${OTHER.name} »`, steps[2])
  check('A2', /Blocage de l'app/.test(steps[3]) && /Actif à partir de demain 09:00/.test(steps[3]), '4 « Blocage de l\'app » chip « Actif à partir de demain 09:00 » (F6/F8)', steps[3])
  check('A2', (await sec.locator('#team-attendance-lock').getAttribute('aria-checked')) === 'true', 'the lock switch sits in step 4, on')
  check('A2', (await sec.getByRole('button', { name: 'Me guider : pointage' }).count()) === 1, '« Me guider » offered while the setup is incomplete')
  await shot(adm, 'A2-setup-checklist-1440')
  await adm.setViewportSize({ width: 390, height: 844 })
  await sleep(800)
  await sec.scrollIntoViewIfNeeded()
  const ov = await mainOverflow(adm)
  check('A2', ov.main <= 1 && ov.doc <= 1, '390 px: the checklist does not push the page sideways', JSON.stringify(ov))
  await shot(adm, 'A2-setup-checklist-390')
  await adm.setViewportSize({ width: 1440, height: 900 })
})

await scenario('A3', async () => {
  await gotoSettings(adm)
  await adm.getByRole('button', { name: 'Me guider : pointage' }).click()
  const bubble = adm.locator('[data-guide-bubble]')
  const expected = [/pointage des arrivées et des départs se règle ici/, /quatre étapes/, /Qui pointe/, /La borne est l'appareil de l'accueil/, /appareils connectés du cabinet/]
  const seen = []
  for (let i = 0; i < expected.length; i++) {
    await bubble.waitFor({ timeout: 8000 })
    let text = ''
    for (let k = 0; k < 20 && !expected[i].test(text); k++) { text = norm(await bubble.innerText().catch(() => '')); await sleep(250) }
    seen.push(text.slice(0, 90))
    if (!expected[i].test(text)) { bad('A3', `step ${i + 1} bubble`, text); break }
    if (i === 4) {
      check('A3', (await adm.locator('[role="dialog"]:visible #borne-name').count()) === 1, 'step 4 opened « Ajouter une borne » for step 5')
      await shot(adm, 'A3-guide-devices-step')
    }
    const next = bubble.getByRole('button', { name: i === expected.length - 1 ? 'Terminer' : 'Suivant' })
    await next.click()
    await sleep(700)
  }
  console.log('   bubbles:', JSON.stringify(seen))
  const t = await toast(adm, /Vous savez mettre en place le pointage/, 8000)
  check('A3', seen.length === 5 && Boolean(t), 'five bubbles in order, « Terminer » → toast « Vous savez mettre en place le pointage. »', t ?? '(no toast)')
  check('A3', new URL(adm.url()).pathname === '/settings' && (await bubble.count()) === 0, 'the tour ends on /settings, bubble gone', adm.url())
  if (await adm.locator('[role="dialog"]:visible #borne-name').count()) await closeBorneDialog(adm)
})

await scenario('B1', async () => {
  await gotoSettings(adm)
  const accueilId = psql(`select "Id" from "AttendanceTerminals" where "ClinicId"='${CLINIC}' and "Name"='Accueil' and "RevokedAt" is null`)
  await adm.getByRole('button', { name: 'Retirer la borne Accueil', exact: true }).click()
  const dlg = alertDialog(adm)
  await dlg.waitFor({ timeout: 8000 })
  const t = norm(await dlg.innerText())
  check('B1', /L'appareil de la borne «\s*Accueil\s*» \(Ines Gharbi · Chrome sur Windows\) ne pourra plus pointer\./.test(t), 'names the device: « L\'appareil de la borne « Accueil » (Ines Gharbi · Chrome sur Windows) ne pourra plus pointer. » (F22)', t)
  check('B1', /L'app ne sera plus bloquée\./.test(t), 'last live borne with the lock on: « L\'app ne sera plus bloquée. »', t)
  await shot(adm, 'B1-retirer-last-borne')
  await dlg.getByRole('button', { name: 'Retour' }).click()
  await sleep(600)
  check('B1', psql(`select ("RevokedAt" is null)::text from "AttendanceTerminals" where "Id"='${accueilId}'`) === 'true', '« Retour » retires nothing (SQL)')
})

await scenario('A4', async () => {
  const dlg = await openAddBorne(adm)
  check('A4', (await dlg.locator('#borne-name').inputValue()) === 'Accueil 2', 'name prefilled with the next free name « Accueil 2 » (F25)', await dlg.locator('#borne-name').inputValue())
  const rep = norm(await dlg.locator('#borne-replace').innerText().catch(() => ''))
  check('A4', /Aucune : nouvelle borne/.test(rep), '« Remplace » defaults to « Aucune : nouvelle borne » (F2)', rep)
  await dlg.locator('#borne-replace').click()
  const opts = (await adm.getByRole('option').allInnerTexts()).map(norm)
  check('A4', opts.includes('Accueil'), '« Remplace » offers « Accueil »', JSON.stringify(opts))
  await adm.keyboard.press('Escape')
  await sleep(300)
  const rows = (await dlg.locator('label[for^="borne-device-"]').allInnerTexts()).map(norm)
  console.log('   rows:', JSON.stringify(rows))
  const has = (re) => rows.some((r) => re.test(r))
  check('A4', has(new RegExp(`^PC · Chrome sur Windows ${T.name} · actif`)), `« PC · Chrome sur Windows » leads T1's row (F5)`, JSON.stringify(rows))
  check('A4', has(new RegExp(`^PC · Chrome sur Linux ${T.name} · actif`)), '« PC · Chrome sur Linux » (T2)', JSON.stringify(rows))
  check('A4', has(new RegExp(`^Tablette · Safari sur iPad ${T.name}`)), '« Tablette · Safari sur iPad »', JSON.stringify(rows))
  check('A4', has(/^Téléphone · Chrome sur Android Dr Salma Ben Youssef/), 'the admin\'s phone offered as « Téléphone · Chrome sur Android »', JSON.stringify(rows))
  check('A4', !rows.some((r) => r.includes('Android') && r.includes(T.name)), 'T\'s phone (a tracked account) is not offered', JSON.stringify(rows))
  check('A4', /^Cet appareil \(Dr Salma Ben Youssef\)/.test(rows[rows.length - 1] ?? ''), '« Cet appareil (Dr Salma Ben Youssef) » is the LAST row', rows[rows.length - 1])
  check('A4', (await dlg.locator(`label[for="borne-device-${tPhone.family}"]`).count()) === 0, 'no radio for T\'s phone session')
  await shot(adm, 'A4-add-borne-1440')

  // B2 / B3 — refusals before any step-up
  await dlg.locator('#borne-name').fill('accueil')
  await dlg.getByRole('button', { name: 'Enregistrer la borne' }).click()
  await sleep(400)
  const nameErr = norm(await dlg.locator('#borne-name-error').innerText().catch(() => ''))
  check('B2', /Une borne s'appelle déjà «\s*accueil\s*»\./.test(nameErr), '« Une borne s\'appelle déjà « accueil ». »', nameErr)
  check('B2', (await adm.locator('[role="dialog"]:visible input[inputmode=numeric]').count()) === 0, 'no step-up asked for a taken name')
  await dlg.locator('#borne-name').fill('QA')
  await dlg.getByRole('button', { name: 'Enregistrer la borne' }).click()
  await sleep(400)
  const devErr = norm(await dlg.locator('#borne-device-error').innerText().catch(() => ''))
  check('B3', /Choisissez l'appareil qui sera la borne\./.test(devErr), 'no device → « Choisissez l\'appareil qui sera la borne. »', devErr)
  await closeBorneDialog(adm)
})

await scenario('C3', async () => {
  for (const [w, h] of [[320, 568], [1536, 730]]) {
    await adm.setViewportSize({ width: w, height: h })
    const dlg = await openAddBorne(adm)
    await sleep(500)
    const m = await dlg.evaluate((el) => ({
      over: Math.max(0, ...[el, ...el.querySelectorAll('*')]
        .filter((n) => n === el || ['auto', 'scroll'].includes(getComputedStyle(n).overflowX))
        .map((n) => n.scrollWidth - n.clientWidth).filter((d) => d > 1)),
      right: Math.round(el.getBoundingClientRect().right),
    }))
    const sb = await dlg.getByRole('button', { name: 'Enregistrer la borne' }).boundingBox()
    check('C3', m.over <= 1 && m.right <= w + 1, `${w}×${h}: no sideways overflow in « Ajouter une borne »`, JSON.stringify(m))
    check('C3', Boolean(sb) && sb.y + sb.height <= h && sb.x >= 0, `${w}×${h}: « Enregistrer la borne » on screen`, JSON.stringify(sb))
    await shot(adm, `C3-add-borne-${w}x${h}`)
    await closeBorneDialog(adm)
  }
  // C4 — the dialog on a coarse pointer
  await adm.setViewportSize({ width: 390, height: 844 })
  const dlg = await openAddBorne(adm)
  try {
    const isCoarse = await coarse(adm, true, 390, 844)
    const rows = await dlg.locator('label[for^="borne-device-"]').evaluateAll((els) => els.map((e) => Math.round(e.getBoundingClientRect().height)))
    check('C4', isCoarse && rows.length >= 4 && rows.every((x) => x >= 44), 'coarse: every device row is ≥ 44 px tall', JSON.stringify({ isCoarse, rows }))
    await shot(adm, 'C4-add-borne-coarse-390')
  } finally {
    await coarse(adm, false)
  }
  await closeBorneDialog(adm)
  await adm.setViewportSize({ width: 1440, height: 900 })
})

await scenario('B4', async () => {
  const before = psql(`select count(*) from "AttendanceTerminals" where "ClinicId"='${CLINIC}'`)
  const tok1 = await apiStepUp(admToken)
  const taken = await api('POST', '/team/terminals', { name: 'Accueil', stepUpToken: tok1 }, admToken)
  check('B4', taken.status === 409 && taken.json?.code === 'borne_name_taken', 'server: a live borne\'s name → 409 borne_name_taken', `${taken.status} ${JSON.stringify(taken.json)}`)
  const tok2 = await apiStepUp(admToken)
  const phone = await api('POST', '/team/terminals', { name: 'QA téléphone', stepUpToken: tok2, deviceSessionId: tPhone.family }, admToken)
  check('B4', phone.status === 400 && phone.json?.code === 'borne_device_phone', 'server: a tracked person\'s phone → 400 borne_device_phone', `${phone.status} ${JSON.stringify(phone.json)}`)
  check('B4', psql(`select count(*) from "AttendanceTerminals" where "ClinicId"='${CLINIC}'`) === before, 'nothing written', before)
})

// ================================================================ Aujourd'hui before T has a schéma (A23, C5, C7)
await scenario('A23', async () => {
  await gotoPresences(adm)
  const ines = norm(await todayRow(adm, INES.name).innerText().catch(() => ''))
  const other = norm(await todayRow(adm, OTHER.name).innerText().catch(() => ''))
  const tee = norm(await todayRow(adm, T.name).innerText().catch(() => ''))
  console.log('   rows:', JSON.stringify({ ines, other, tee }))
  check('A23', /Départ à 09:35/.test(ines), 'Ines « Départ à 09:35 » (F11, never « Pas encore arrivée »)', ines)
  check('A23', /Pas encore de pointage/.test(other) && /Schéma à créer/.test(other) && /Pointer son arrivée/.test(other), '991861 « Pas encore de pointage » + « Schéma à créer » + « Pointer son arrivée » (F12)', other)
  check('A23', /Pas encore de pointage/.test(tee) && /Schéma à créer/.test(tee), 'T « Pas encore de pointage » + « Schéma à créer »', tee)
  const all = await bodyText(adm)
  check('A23', !/Présente depuis|Pas encore arrivée|Présent depuis/.test(all), 'no gendered « Présente » / « Pas encore arrivée » left (F30)', (all.match(/Présente depuis|Pas encore arrivée/) ?? [''])[0])
  await shot(adm, 'A23-today-1440')
  // dashboard block (D4)
  await adm.goto(`${WEB}/`)
  const ok4 = await waitText(adm, /équipe aujourd.hui/i, 20000)
  await waitText(adm, /Départ à 09:35/, 10000)
  const block = norm(await bodyText(adm))
  const at4 = block.search(/équipe aujourd.hui/i)
  const seg = at4 < 0 ? '' : block.slice(at4, at4 + 600)
  check('D4', ok4 && /Départ à 09:35/.test(seg) && seg.includes(OTHER.name) && /Schéma à créer/.test(seg), 'dashboard « Équipe aujourd\'hui » says the same (Départ à 09:35, Schéma à créer)', seg)
  await shot(adm, 'D4-dashboard')
})

await scenario('C5', async () => {
  await adm.setViewportSize({ width: 320, height: 640 })
  await gotoPresences(adm)
  const names = await todaySection(adm).locator('li span.break-words').evaluateAll((els) => els.map((e) => ({ t: e.textContent, clipped: e.scrollWidth > e.clientWidth + 1, ov: getComputedStyle(e).textOverflow })))
  check('C5', names.length >= 3 && names.every((n) => !n.clipped && n.ov !== 'ellipsis'), '320 px: names wrap, none clipped (F34)', JSON.stringify(names))
  await todaySection(adm).scrollIntoViewIfNeeded()
  await shot(adm, 'C5-today-320')
})

await scenario('C7', async () => {
  for (const [w, h] of [[320, 640], [390, 844], [820, 1180], [1180, 820], [1536, 730]]) {
    await adm.setViewportSize({ width: w, height: h })
    await gotoPresences(adm)
    const ov = await mainOverflow(adm)
    const table = await adm.locator('table:visible').count()
    check('C7', ov.main <= 1 && ov.doc <= 1, `${w}×${h}: Présences tab never scrolls sideways`, JSON.stringify(ov))
    check('C7', w >= 1024 ? table >= 1 : table === 0, `${w}: ${w >= 1024 ? 'table' : 'cards'} form`, `tables=${table}`)
    if (w === 820 || w === 320) await shot(adm, `C7-presences-${w}`)
  }
  await adm.setViewportSize({ width: 1440, height: 900 })
})

// ================================================================ A5 — « QA » on T2's PC (pending → claimed), the bell rows
let qaId = null
await scenario('A5', async () => {
  const dlg = await openAddBorne(adm)
  await dlg.locator('#borne-name').fill('QA')
  const row = dlg.locator(`label[for="borne-device-${t2Family}"]`)
  check('A5', (await row.count()) === 1, 'T2\'s session offered', t2Family)
  await row.click()
  await dlg.getByRole('button', { name: 'Enregistrer la borne' }).click()
  await stepUpConfirm(adm)
  const t = await toast(adm, /en attente de l'appareil de/, 12000)
  check('A5', Boolean(t) && new RegExp(`Borne «\\s*QA\\s*» en attente de l'appareil de ${T.name}\\.`).test(t), `toast « Borne « QA » en attente de l'appareil de ${T.name}. » (F10 — never « envoyée »)`, t ?? '(none)')
  qaId = psql(`select "Id" from "AttendanceTerminals" where "ClinicId"='${CLINIC}' and "Name"='QA' and "RevokedAt" is null order by "CreatedAt" desc limit 1`)
  const sql = psql(`select ("SecretHash" is null)::text || '|' || coalesce("PendingSessionFamilyId"::text,'') from "AttendanceTerminals" where "Id"='${qaId}'`)
  check('A5', sql === `true|${t2Family}`, 'SQL: waiting for T2, no secret yet', sql)
  await sleep(1200)
  const li = norm(await settingsRow(adm, 'QA').innerText().catch(() => ''))
  check('A5', /En attente depuis \d{2}:\d{2}/.test(li) && li.includes(`${T.name} · Chrome sur Linux`), `row « En attente depuis HH:mm · ${T.name} · Chrome sur Linux »`, li)
  await settingsRow(adm, 'QA').scrollIntoViewIfNeeded()
  await shot(adm, 'A5-pending-row')
  // T2 opens the app: it claims the borne by itself
  await t2.goto(`${WEB}/conges`)
  const claimed = await toast(t2, /Cet appareil est maintenant la borne/, 25000)
  check('A5', Boolean(claimed) && /«\s*QA\s*»/.test(claimed), 'T2: « Cet appareil est maintenant la borne « QA ». »', claimed ?? '(none in 25 s)')
  const live = psql(`select ("SecretHash" is not null)::text || '|' || ("PendingSessionFamilyId" is null)::text || '|' || ("ActivatedAtUtc" is not null)::text from "AttendanceTerminals" where "Id"='${qaId}'`)
  check('A5', live === 'true|true|true', 'SQL: claimed, activated', live)
  await sleep(2500)
  const li2 = norm(await settingsRow(adm, 'QA').innerText().catch(() => ''))
  check('A5', !/En attente/.test(li2) && li2.includes(`${T.name} · Chrome sur Linux`), 'admin row updates by itself: no longer waiting', li2)
  // bells
  await openBell(adm, /Borne installée/)
  const admBell = norm(await bodyText(adm))
  check('A5', new RegExp(`Borne installée[\\s\\S]*La borne «\\s*QA\\s*» est installée sur l'appareil de ${T.name}\\.`).test(admBell), 'admin bell « Borne installée · La borne « QA » est installée sur l\'appareil de T. »', admBell.slice(admBell.indexOf('Borne install'), admBell.indexOf('Borne install') + 160))
  await shot(adm, 'A5-admin-bell-installed')
  // B13 — its click routes to Paramètres → Équipe
  await adm.locator('text=Borne installée').first().click()
  const routed = await adm.waitForURL(/\/settings#equipe/, { timeout: 8000 }).then(() => true, () => false)
  check('B13', routed, '« Borne installée » opens /settings#equipe', adm.url())
  await t1.goto(`${WEB}/conges`)
  await waitText(t1, /Mes congés/, 15000)
  await openBell(t1, /Créez votre schéma de pointage/)
  await waitText(t1, /Cet appareil est une borne/, 5000)
  const tBell = norm(await bodyText(t1))
  check('A5', /Cet appareil est une borne[\s\S]*Dr Salma Ben Youssef a fait de cet appareil la borne «\s*QA\s*»\./.test(tBell), 'T\'s bell « Cet appareil est une borne · Dr Salma Ben Youssef a fait de cet appareil la borne « QA ». »', tBell.slice(0, 400))
  check('A5', /Créez votre schéma de pointage[\s\S]*La borne «\s*QA\s*» est en service : créez votre schéma pour y pointer\./.test(tBell), 'T\'s bell « Créez votre schéma de pointage » (F3)', tBell.slice(0, 400))
  await shot(t1, 'A5-T1-bell')
  const otherRow = psql(`select count(*) from "StaffNotifications" where "Category"=25 and "TargetUserId"='${OTHER.id}' and "CreatedAt" >= '${walkStart}'`)
  check('A5', otherRow === '1', '991861 (tracked, no schéma) told too', otherRow)
})

await scenario('A6', async () => {
  await t1.locator('text=Créez votre schéma de pointage').first().click()
  const landed = await t1.waitForURL(/\/mon-profil/, { timeout: 10000 }).then(() => true, () => false)
  const dlg = openDialog(t1)
  const opened = await dlg.getByText('Tracez votre schéma').waitFor({ timeout: 10000 }).then(() => true, () => false)
  const title = norm(await dlg.locator('h2').first().innerText().catch(() => ''))
  check('A6', landed && opened && /Créer votre schéma/.test(title), 'the row opens « Créer votre schéma » on Mon profil (draw-twice pad)', `${t1.url()} | ${title}`)
  await sleep(1200)
  check('A6', !/schema=/.test(t1.url()), 'the deep link leaves the address bar clean', t1.url())
  await shot(t1, 'A6-create-schema-dialog')
  await dlg.getByRole('button', { name: 'Retour' }).click()
  await sleep(500)
})

// ================================================================ the lock goes live (own row back-dated), T has no schéma
await scenario('A7', async () => {
  psql(`update "AttendanceTerminals" set "ActivatedAtUtc"='${TODAY} 07:30:00+00' where "Id"='${qaId}'`)
  await gotoSettings(adm)
  const steps = (await adm.locator('section[aria-labelledby="pointage-setup-title"] ol > li').allInnerTexts()).map(norm)
  check('A7', /Blocage de l'app/.test(steps[3]) && /\bActif\b/.test(steps[3]) && !/à partir/.test(steps[3]), 'chip « Actif » once the lock has begun (F6/F8)', steps[3])
  const cs = await tClock()
  check('A7', cs.locked === true && cs.hasSchema === false, 'server: T locked, no schéma', JSON.stringify(cs))
  await t1.goto(`${WEB}/patients`)
  const up = await gateUp(t1, 15000)
  const g = norm(await gate(t1).innerText().catch(() => ''))
  console.log('   lock:', g)
  check('A7', up && /Pointez votre arrivée/.test(g) && BOTH_BORNES.test(g), 'lock « Pointez votre arrivée » · « Bornes : « Accueil », « QA » »', g)
  check('A7', /Créer votre schéma/.test(g) && /Tracez votre schéma/.test(g) && (await pads(gate(t1)).count()) === 1, 'the schéma is drawn in place (F14): « Créer votre schéma » + pad', g)
  check('A7', /Se déconnecter/.test(g) && /Demander un congé/.test(g) && !/Schéma oublié/.test(g), '« Se déconnecter » + « Demander un congé », no « Schéma oublié ? » yet', g)
  await shot(t1, 'A7-lock-no-schema-1180')
  // C1 — the same lock at three sizes
  for (const [w, h] of [[320, 568], [390, 844], [1536, 730]]) {
    await t1.setViewportSize({ width: w, height: h })
    await sleep(600)
    const m = await gate(t1).evaluate((el) => {
      const r = (re) => { const b = [...el.querySelectorAll('a,button')].find((x) => re.test(x.textContent)); return b ? b.getBoundingClientRect() : null }
      const pad = el.querySelector('svg[role="application"]')?.getBoundingClientRect()
      return { sw: el.scrollWidth - el.clientWidth, pad: pad ? { l: pad.left, r: pad.right, h: pad.height } : null, out: !!r(/Se déconnecter/), conge: !!r(/Demander un congé/) }
    })
    const reach = await gate(t1).getByRole('link', { name: 'Demander un congé' }).scrollIntoViewIfNeeded().then(() => true, () => false)
    check('C1', m.sw <= 1 && m.pad && m.pad.l >= 0 && m.pad.r <= w && m.out && m.conge && reach, `${w}×${h}: lock (drawing) — no sideways scroll, pad inside, both ways out reachable`, JSON.stringify(m))
    await shot(t1, `C1-lock-drawing-${w}x${h}`)
  }
  await t1.setViewportSize({ width: 1180, height: 820 })
})

await scenario('A8', async () => {
  await gate(t1).evaluate((el) => { el.scrollTop = 0 })
  await typeSchema(t1, P1, gate(t1))
  const again = await waitText(t1, /Tracez-le à nouveau/, 6000)
  await typeSchema(t1, P1, gate(t1))
  const t = await toast(t1, /Schéma enregistré/, 10000)
  check('A8', again && Boolean(t), 'drawn twice inside the lock → « Schéma enregistré. »', t ?? '(none)')
  await sleep(1500)
  const g = norm(await gate(t1).innerText().catch(() => ''))
  check('A8', (await gate(t1).count()) === 1 && /Schéma oublié/.test(g) && !/Créer votre schéma/.test(g) && (await pads(gate(t1)).count()) === 0, 'the lock stays (ordinary PC), now with « Schéma oublié ? » and no pad', g)
  check('A8', tHash() !== '', 'SQL: schéma stored')
  await shot(t1, 'A8-lock-with-schema')
})

let hashP1 = ''
await scenario('B5', async () => {
  hashP1 = tHash()
  const fails0 = psql(`select "FailedLoginAttempts" from "Users" where "Id"='${T.id}'`)
  await gate(t1).getByRole('button', { name: 'Schéma oublié ?' }).click()
  const g0 = norm(await gate(t1).innerText())
  check('B5', /Changer votre schéma/.test(g0) && /Mot de passe de votre compte/.test(g0), '« Changer votre schéma » asks the account password first (F1)', g0)
  await gate(t1).getByLabel('Mot de passe de votre compte').fill('Mauvais-Mot-De-Passe-9')
  await gate(t1).getByRole('button', { name: 'Continuer' }).click()
  await typeSchema(t1, P2, gate(t1))
  await waitText(t1, /Tracez-le à nouveau/, 6000)
  await typeSchema(t1, P2, gate(t1))
  await gate(t1).getByLabel('Mot de passe de votre compte').waitFor({ timeout: 10000 }).catch(() => {})
  await sleep(600)
  const err = norm(await gate(t1).locator('form [role="alert"]').innerText().catch(() => ''))
  check('B5', err.length > 0 && (await gate(t1).getByLabel('Mot de passe de votre compte').count()) === 1, 'refused: back on the password with the server\'s sentence', err || '(no error)')
  check('B5', tHash() === hashP1, 'SQL: schéma unchanged')
  const fails1 = psql(`select "FailedLoginAttempts" from "Users" where "Id"='${T.id}'`)
  console.log(`   FailedLoginAttempts ${fails0} → ${fails1}`)
  await shot(t1, 'B5-wrong-password')
})

await scenario('A9', async () => {
  await gate(t1).getByLabel('Mot de passe de votre compte').fill(PASSWORD)
  await gate(t1).getByRole('button', { name: 'Continuer' }).click()
  await typeSchema(t1, P2, gate(t1))
  await waitText(t1, /Tracez-le à nouveau/, 6000)
  await typeSchema(t1, P2, gate(t1))
  const t = await toast(t1, /Schéma enregistré/, 10000)
  let moved = false
  for (let i = 0; i < 40 && !moved; i++) { moved = tHash() !== hashP1 && tHash() !== ''; if (!moved) await sleep(250) }
  check('A9', Boolean(t) && moved, 'right password → « Schéma enregistré. », schéma replaced (SQL)', t ?? '(none)')
  await sleep(1000)
  check('A9', /Schéma oublié/.test(norm(await gate(t1).innerText().catch(() => ''))), 'back to the lock with « Schéma oublié ? »')
  // C1 again with a schéma
  for (const [w, h] of [[320, 568], [1536, 730]]) {
    await t1.setViewportSize({ width: w, height: h })
    await sleep(500)
    const sw = await gate(t1).evaluate((el) => el.scrollWidth - el.clientWidth)
    const out = await gate(t1).getByRole('button', { name: 'Se déconnecter' }).boundingBox()
    check('C1', sw <= 1 && out && out.y + out.height <= h, `${w}×${h}: lock with a schéma — « Se déconnecter » on screen`, JSON.stringify({ sw, out }))
    await shot(t1, `C1-lock-schema-${w}x${h}`)
  }
  await t1.setViewportSize({ width: 1180, height: 820 })
})

// ================================================================ T2 — the lock on a borne (F4, F13, F21)
await scenario('A10', async () => {
  await t2.goto(`${WEB}/patients`)
  const up = await gateUp(t2, 15000)
  const g = norm(await gate(t2).innerText().catch(() => ''))
  console.log('   borne lock:', g)
  check('A10', up && /Cet appareil est une borne du cabinet\./.test(g) && g.includes(T.name) && (await pads(gate(t2)).count()) === 1, 'on the borne: her name above the pad (F4)', g)
  check('A10', /Schéma oublié/.test(g) && /Ce n'est pas moi/.test(g), '« Schéma oublié ? » + « Ce n\'est pas moi »', g)
  await shot(t2, 'A10-borne-lock-820')
  for (const [w, h] of [[320, 568], [812, 380]]) {
    await t2.setViewportSize({ width: w, height: h })
    await sleep(600)
    const m = await gate(t2).evaluate((el) => {
      const pad = el.querySelector('svg[role="application"]')?.getBoundingClientRect()
      return { sw: el.scrollWidth - el.clientWidth, padH: pad ? Math.round(pad.height) : -1, padL: pad?.left ?? -1, padR: pad?.right ?? -1 }
    })
    const reach = await gate(t2).getByRole('link', { name: 'Demander un congé' }).scrollIntoViewIfNeeded().then(() => true, () => false)
    check('C2', m.sw <= 1 && m.padH > 0 && m.padH <= Math.ceil(0.4 * h) + 2 && m.padL >= 0 && m.padR <= w && reach, `${w}×${h}: pad ≤ 40 dvh (${Math.ceil(0.4 * h)} px), « Demander un congé » reachable (F21)`, JSON.stringify(m))
    await shot(t2, `C2-borne-lock-${w}x${h}`)
  }
  await t2.setViewportSize({ width: 820, height: 1180 })
  // C4 — coarse: the two links grow
  const isCoarse = await coarse(t2, true, 390, 844)
  const hForgot = await heightOf(gate(t2).getByRole('button', { name: 'Schéma oublié ?' }))
  const hNotMe = await heightOf(gate(t2).getByRole('link', { name: "Ce n'est pas moi" }))
  check('C4', isCoarse && hForgot >= 44 && hNotMe >= 44, 'coarse: « Schéma oublié ? » and « Ce n\'est pas moi » ≥ 44 px', JSON.stringify({ isCoarse, hForgot, hNotMe }))
  await shot(t2, 'C4-borne-lock-coarse-390')
  await coarse(t2, false)
  await t2.setViewportSize({ width: 820, height: 1180 })
  await gate(t2).getByRole('link', { name: "Ce n'est pas moi" }).click()
  const there = await t2.waitForURL(/\/pointage/, { timeout: 10000 }).then(() => true, () => false)
  const tiles = await waitText(t2, /Qui pointe|Borne QA/, 15000)
  await sleep(1500)
  check('A10', there && tiles && (await gate(t2).count()) === 0, '« Ce n\'est pas moi » opens the tiles, no lock over them', t2.url())
  // B14 / D1 — the tiles' wording
  const tileText = async (name) => norm(await t2.getByRole('button', { name: new RegExp(name) }).first().innerText().catch(() => ''))
  const ti = await tileText(INES.name), to = await tileText(OTHER.name), tt = await tileText(T.name)
  check('B14', /Départ à 09:35/.test(ti) && /Schéma à créer/.test(to) && /Pas encore de pointage/.test(tt), 'tiles: Ines « Départ à 09:35 » · 991861 « Schéma à créer » · T « Pas encore de pointage »', JSON.stringify({ ti, to, tt }))
  check('B14', await t2.getByRole('button', { name: new RegExp(OTHER.name) }).first().isDisabled(), '991861\'s tile disabled')
  await shot(t2, 'B14-tiles')
})

await scenario('A11', async () => {
  await t2.goto(`${WEB}/patients`)
  await gateUp(t2, 15000)
  await dragSchema(t2, WRONG, gate(t2))
  check('A11', await waitText(t2, /Schéma incorrect\. Encore 4 essais/, 8000), 'first wrong try: « Schéma incorrect. Encore 4 essais… »')
  for (let i = 0; i < 4; i++) {
    await sleep(900)
    await dragSchema(t2, WRONG, gate(t2))
  }
  await sleep(2500)
  const g = norm(await gate(t2).innerText().catch(() => ''))
  console.log('   after 5:', g)
  check('A11', /Schéma bloqué jusqu'à \d{2}:\d{2}/.test(g) && (await pads(gate(t2)).count()) === 0, 'the borne lock shows « Schéma bloqué jusqu\'à HH:mm » instead of the pad (F1/F13)', g)
  check('A11', !/Réessayer/.test(g), 'no « Réessayer » for a punch the server would refuse', g)
  await shot(t2, 'A11-borne-lock-blocked')
  const sql = psql(`select "SchemaFailedAttempts" || '|' || ("SchemaLockedUntilUtc" is not null)::text from "Users" where "Id"='${T.id}'`)
  check('A11', /\|true$/.test(sql), 'SQL: blocked', sql)
  await t1.reload()
  await gateUp(t1, 12000)
  const g1 = norm(await gate(t1).innerText().catch(() => ''))
  check('A11', /Schéma bloqué jusqu'à \d{2}:\d{2}/.test(g1), 'T1\'s lock says it too', g1)
  // the tile
  await t2.goto(`${WEB}/pointage`)
  await waitText(t2, /Borne QA/, 15000)
  const tt = norm(await t2.getByRole('button', { name: new RegExp(T.name) }).first().innerText().catch(() => ''))
  check('A11', /Schéma bloqué · réessayez dans 1[45] min/.test(tt) && (await t2.getByRole('button', { name: new RegExp(T.name) }).first().isDisabled()), 'tile « Schéma bloqué · réessayez dans N min », disabled', tt)
  // the admin: bell row, its click, Paramètres, Aujourd'hui
  await adm.goto(`${WEB}/settings`)
  await sleep(2000)
  await openBell(adm, /Le schéma de/)
  const bell = norm(await bodyText(adm))
  check('A11', new RegExp(`Schéma bloqué[\\s\\S]*Le schéma de ${T.name} est bloqué jusqu'à \\d{2}:\\d{2} après 5 essais\\.`).test(bell), 'admin bell « Schéma bloqué · Le schéma de T est bloqué jusqu\'à HH:mm après 5 essais. »', bell.slice(bell.indexOf('Schéma bloqué'), bell.indexOf('Schéma bloqué') + 160))
  await shot(adm, 'A11-admin-bell-blocked')
  await adm.locator('text=Le schéma de').first().click()
  check('B13', await adm.waitForURL(/\/settings#equipe/, { timeout: 8000 }).then(() => true, () => false), '« Schéma bloqué » opens /settings#equipe', adm.url())
  await adm.locator('section[aria-labelledby="pointent-title"]').waitFor({ timeout: 20000 })
  const person = norm(await adm.locator('section[aria-labelledby="pointent-title"] li', { hasText: T.name }).first().innerText().catch(() => ''))
  check('A11', /Bloqué après 5 essais, jusqu'à/.test(person) && /Débloquer/.test(person), 'Paramètres: « Bloqué après 5 essais, jusqu\'à … » + « Débloquer »', person)
  await gotoPresences(adm)
  const row = todayRow(adm, T.name)
  const rt = norm(await row.innerText().catch(() => ''))
  check('A11', /Schéma bloqué/.test(rt) && /Débloquer/.test(rt), 'Aujourd\'hui: « Schéma bloqué » + « Débloquer » (F12)', rt)
  await shot(adm, 'A11-today-blocked')
  // C4 — coarse on Aujourd'hui's buttons
  const isCoarse = await coarse(adm, true, 820, 1180)
  await sleep(600)
  const hUnlock = await heightOf(row.getByRole('button', { name: `Débloquer le schéma de ${T.name}` }))
  const hArrive = await heightOf(todayRow(adm, OTHER.name).getByRole('button', { name: `Pointer son arrivée : ${OTHER.name}` }))
  check('C4', isCoarse && hUnlock >= 44 && hArrive >= 44, 'coarse: « Débloquer » / « Pointer son arrivée » ≥ 44 px', JSON.stringify({ isCoarse, hUnlock, hArrive }))
  await coarse(adm, false)
  await adm.setViewportSize({ width: 1440, height: 900 })
  await sleep(600)
  const hashBefore = tHash()
  await todayRow(adm, T.name).getByRole('button', { name: `Débloquer le schéma de ${T.name}` }).click()
  const t = await toast(adm, new RegExp(`Schéma de ${T.name} débloqué`), 8000)
  check('A11', Boolean(t), `toast « Schéma de ${T.name} débloqué. »`, t ?? '(none)')
  const after = psql(`select "SchemaFailedAttempts" || '|' || ("SchemaLockedUntilUtc" is null)::text from "Users" where "Id"='${T.id}'`)
  check('A11', after === '0|true' && tHash() === hashBefore, 'SQL: tries cleared, lock gone, schéma kept', after)
  await t2.goto(`${WEB}/patients`)
  await gateUp(t2, 12000)
  let padBack = false
  for (let i = 0; i < 30 && !padBack; i++) { padBack = (await pads(gate(t2)).count()) === 1; await sleep(500) }
  check('A11', padBack, 'T2\'s lock shows the pad again')
})

// ================================================================ « Pointer son arrivée » prefilled, default hour, the sheet
await scenario('A12', async () => {
  await gotoPresences(adm)
  const before = hhmm(local())
  await todayRow(adm, T.name).getByRole('button', { name: `Pointer son arrivée : ${T.name}` }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#record-day').waitFor({ timeout: 8000 })
  const person = norm(await dlg.locator('#record-person').innerText())
  const day = await dlg.locator('#record-day').inputValue()
  const arr = await dlg.locator('#record-arrival').inputValue()
  const dm = Math.abs(minutesOf(arr || '00:00') - minutesOf(before))
  check('A12', person === T.name && day === TODAY && /^\d{2}:\d{2}$/.test(arr) && dm <= 2, `prefilled: ${T.name} · ${TODAY} · now (F12/F23)`, JSON.stringify({ person, day, arr, before }))
  await shot(adm, 'A12-record-prefilled')
  await dlg.getByRole('button', { name: 'Retour' }).click()
  await sleep(500)
  if (await alertDialog(adm).count()) await alertDialog(adm).getByRole('button', { name: 'Abandonner' }).click()
  // toolbar: nobody, today, now; a past day empties the hour
  await adm.getByRole('button', { name: 'Ajouter un pointage' }).first().click()
  const d2 = openDialog(adm)
  await d2.locator('#record-day').waitFor({ timeout: 8000 })
  const p2 = norm(await d2.locator('#record-person').innerText())
  const a2 = await d2.locator('#record-arrival').inputValue()
  check('A12', /Choisir/.test(p2) && (await d2.locator('#record-day').inputValue()) === TODAY && Math.abs(minutesOf(a2 || '00:00') - minutesOf(hhmm(local()))) <= 2, 'toolbar: « Choisir », today, now — never 08:00 (F23)', JSON.stringify({ p2, a2 }))
  await d2.locator('#record-day').fill('2026-11-06')
  await sleep(300)
  check('A12', (await d2.locator('#record-arrival').inputValue()) === '', 'another day: the hour empties (typed, never invented)', await d2.locator('#record-arrival').inputValue())
  await d2.locator('#record-person').click()
  await adm.getByRole('option', { name: T.name }).click()
  await sleep(300)
  await d2.locator('#record-arrival').fill('0830')
  await d2.locator('#record-arrival').blur()
  await d2.locator('#record-reason').fill('QA — jour sans pointage')
  await d2.getByRole('button', { name: 'Ajouter', exact: true }).click()
  check('A12', Boolean(await toast(adm, /Pointage ajouté/, 8000)), 'vendredi 6 added (08:30, open)')
  check('A12', tLines(`and "LocalDay"='2026-11-06' and "DepartureUtc" is null`) === '1', 'SQL: an open line on 6 nov')
})

await scenario('A13', async () => {
  await gotoPresences(adm)
  const heads = (await adm.locator('table:visible thead th').allInnerTexts()).map((h) => norm(h).toLowerCase())
  const col = heads.indexOf(T.name.toLowerCase())
  check('A13', col > 0, 'T has a column', JSON.stringify(heads))
  const link = adm.locator('table:visible thead th a', { hasText: T.name })
  check('A13', (await link.getAttribute('href')) === `/users/${encodeURIComponent(T.id)}`, 'the column header links to her file (F35)', await link.getAttribute('href'))
  const rowOf = (label) => adm.locator('table:visible tbody tr').filter({ has: adm.locator('td:first-child', { hasText: new RegExp(`^${label}$`, 'i') }) }).first()
  const cell = async (label) => norm(await rowOf(label).locator('td').nth(col).innerText().catch(() => ''))
  const mon2 = await cell('lun\\. 2'), fri6 = await cell('ven\\. 6'), sun8 = await cell('dim\\. 8')
  console.log('   cells:', JSON.stringify({ mon2, fri6, sun8 }))
  check('A13', /Aucun pointage/.test(mon2) && /Ajouter/.test(mon2), 'a past open day with nothing: « Aucun pointage » + « Ajouter » (F24)', mon2)
  check('A13', /08:30 – Départ non pointé/.test(fri6), '6 nov: « 08:30 – Départ non pointé »', fri6)
  check('A13', !/Aucun pointage/.test(sun8), 'a closed day (dimanche) is not an absence', sun8)
  const futureClass = await rowOf('mar\\. 10').getAttribute('class').catch(() => '')
  check('A13', /text-muted-foreground\/70/.test(futureClass ?? ''), 'future days lighter', futureClass)
  await rowOf('sam\\. 7').scrollIntoViewIfNeeded()
  await shot(adm, 'A13-sheet-absences')
  await rowOf('sam\\. 7').locator('td').nth(col).getByRole('button', { name: `Ajouter un pointage pour ${T.name} le samedi 7 novembre` }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#record-day').waitFor({ timeout: 8000 })
  const pre = { person: norm(await dlg.locator('#record-person').innerText()), day: await dlg.locator('#record-day').inputValue(), arr: await dlg.locator('#record-arrival').inputValue() }
  check('A13', pre.person === T.name && pre.day === '2026-11-07' && pre.arr === '', 'inline « Ajouter »: prefilled T + 2026-11-07, hour empty', JSON.stringify(pre))
  await dlg.locator('#record-arrival').fill('0845')
  await dlg.locator('#record-arrival').blur()
  await dlg.locator('#record-reason').fill('QA — jour sans pointage')
  await dlg.getByRole('button', { name: 'Ajouter', exact: true }).click()
  check('A13', Boolean(await toast(adm, /Pointage ajouté/, 8000)), 'samedi 7 added (08:45, open)')
  // C8-ish: the card form below lg says the same
  await adm.setViewportSize({ width: 820, height: 1180 })
  await gotoPresences(adm)
  const cards = norm(await bodyText(adm))
  check('A13', /Lundi 2 novembre[\s\S]*Aucun pointage/.test(cards), '820 px cards: « Lundi 2 novembre » card with « Aucun pointage »', cards.slice(cards.indexOf('Lundi 2'), cards.indexOf('Lundi 2') + 200))
  await shot(adm, 'A13-cards-820')
  await adm.setViewportSize({ width: 1440, height: 900 })
})

// ================================================================ the declaration step at the borne (F20, F28)
await scenario('A14', async () => {
  await t2.goto(`${WEB}/pointage`)
  await waitText(t2, /Borne QA/, 15000)
  await t2.getByRole('button', { name: new RegExp(T.name) }).first().click()
  const word = norm(await t2.locator('p.text-5xl').innerText().catch(() => ''))
  check('A14', word === 'Arrivée', 'panel « Arrivée »', word)
  await dragSchema(t2, P2)
  const first = await waitText(t2, /Votre départ du vendredi 6 novembre n'a pas été pointé/, 8000)
  check('A14', first, 'oldest open day first: « Votre départ du vendredi 6 novembre n\'a pas été pointé »')
  await t2.keyboard.press('Escape')
  await sleep(800)
  check('A14', await waitText(t2, /Votre départ du vendredi 6 novembre/, 1500), 'Escape does not throw the step away (F20)')
  console.log('   waiting 35 s (the old 30 s idle)…')
  await sleep(35000)
  check('A14', await waitText(t2, /Votre départ du vendredi 6 novembre/, 1500), 'still there after 35 s without input (idle 120 s on this step)')
  const f = t2.locator('#declared-departure')
  await f.fill('1700')
  await f.blur()
  await t2.getByRole('button', { name: 'Valider' }).click()
  const second = await waitText(t2, /Votre départ du samedi 7 novembre n'a pas été pointé/, 8000)
  const settled = norm(await t2.locator('ul[aria-label="Départs déjà déclarés"]').innerText().catch(() => ''))
  check('A14', second && /Vendredi 6 novembre · départ 17:00/.test(settled), 'next day asked, with « Vendredi 6 novembre · départ 17:00 » listed above (F28)', settled || '(no settled list)')
  check('A14', (await f.inputValue()) === '', 'the hour field is empty for the new day')
  await shot(t2, 'A14-declaration-settled')
  await f.fill('1730')
  await f.blur()
  await t2.getByRole('button', { name: 'Valider' }).click()
  const conf = await waitText(t2, /Arrivée enregistrée/, 8000)
  const line = (norm(await bodyText(t2)).match(/(\d{2}:\d{2}) · Borne QA/) ?? [])[0]
  check('A14', conf && Boolean(line), `« Arrivée enregistrée » · « ${line} »`, line)
  await shot(t2, 'A14-arrival-confirmed')
  const rows = psql(`select "LocalDay" || '|' || coalesce(to_char("DepartureUtc" at time zone 'UTC' + interval '1 hour','HH24:MI'),'') || '|' || coalesce("DepartureSource"::text,'')
                     from "AttendanceEntries" where "UserId"='${T.id}' and "LocalDay" in ('2026-11-06','2026-11-07') order by "LocalDay"`)
  check('A14', rows === '2026-11-06|17:00|1\n2026-11-07|17:30|1', 'SQL: both days closed as declared', rows.replace(/\n/g, ' ; '))
  check('A14', tLines(`and "LocalDay"='${TODAY}' and "DepartureUtc" is null`) === '1', 'SQL: today\'s arrival opened')
  check('A14', await gateGone(t1, 15000), 'T1\'s lock lifts by itself')
})

// ================================================================ header chip (F19) and « Pointage » over the page (F16)
await scenario('A15', async () => {
  await t1.goto(`${WEB}/patients`)
  await sleep(3000)
  const chip = t1.locator('header button', { hasText: /Arrivée à/ }).first()
  const ct = norm(await chip.innerText().catch(() => ''))
  check('A15', /^Arrivée à ?\d{2}:\d{2}$/.test(ct), 'T1 header chip « Arrivée à HH:mm »', ct)
  await chip.click()
  const dlg = openDialog(t1)
  await dlg.waitFor({ timeout: 8000 })
  const dt = norm(await dlg.innerText())
  check('A15', AT_BOTH.test(dt) && /Arrivée pointée à \d{2}:\d{2}\./.test(dt), 'off a borne: « Pointez votre départ à la borne « Accueil » ou « QA » » · « Arrivée pointée à HH:mm. »', dt)
  const btns = (await dlg.getByRole('button').allInnerTexts()).map(norm).filter(Boolean)
  check('A15', btns.includes('Fermer') && !btns.includes('Se déconnecter') && (await pads(dlg).count()) === 0, 'only « Fermer », no pad', JSON.stringify(btns))
  await shot(t1, 'A15-chip-dialog-off-borne')
  await dlg.locator('[data-slot="dialog-footer"]').getByRole('button', { name: 'Fermer' }).click()
  await sleep(400)
  await t1.setViewportSize({ width: 390, height: 844 })
  await sleep(600)
  const vis = await t1.locator('header button', { hasText: /\d{2}:\d{2}/ }).first().evaluate((b) => [...b.querySelectorAll('span')].map((s) => ({ t: s.textContent, w: Math.round(s.getBoundingClientRect().width) }))).catch(() => [])
  check('A15', vis.length === 2 && vis[0].w <= 1 && /^\d{2}:\d{2}$/.test(vis[1].t) && vis[1].w > 10, '390 px: the chip shows the hour only, the words kept for a screen reader', JSON.stringify(vis))
  await t1.setViewportSize({ width: 1180, height: 820 })
  await t2.goto(`${WEB}/patients`)
  await sleep(3000)
  check('A15', (await gate(t2).count()) === 0, 'T2: no lock once arrived')
  await t2.locator('header button', { hasText: /Arrivée à/ }).first().click()
  const d2 = openDialog(t2)
  await d2.waitFor({ timeout: 8000 })
  const t2t = norm(await d2.innerText())
  check('A15', /^Pointez votre départ\b/.test(t2t) && !/à la borne/.test(t2t.split('Arrivée')[0]) && (await pads(d2).count()) === 1, 'on the borne: « Pointez votre départ » with the pad', t2t)
  await shot(t2, 'A15-chip-dialog-on-borne')
  await d2.locator('[data-slot="dialog-footer"]').getByRole('button', { name: 'Fermer' }).click()
  await sleep(400)
})

await scenario('A16', async () => {
  await t2.setViewportSize({ width: 1180, height: 820 })
  await t2.goto(`${WEB}/patients`)
  await sleep(3000)
  const m = await mark(t2)
  const btn = t2.locator('header').getByRole('button', { name: 'Pointage', exact: true })
  check('A16', (await btn.count()) === 1, 'header « Pointage » on a signed-in borne')
  await btn.click()
  const dlg = openDialog(t2)
  await dlg.waitFor({ timeout: 8000 })
  await waitText(t2, /Qui pointe|Borne QA/, 8000)
  const dt = norm(await dlg.innerText())
  check('A16', /Pointage/.test(dt) && /Borne QA/.test(dt) && (await dlg.locator('ul[aria-label="Qui pointe ?"]').count()) === 1, 'dialog « Pointage » · « Borne QA » with the tiles', dt.slice(0, 200))
  check('A16', new URL(t2.url()).pathname === '/patients' && (await marked(t2, m)), 'opened over /patients — same page, no navigation (F16)', t2.url())
  await shot(t2, 'A16-pointage-dialog')
  await t2.keyboard.press('Escape')
  await sleep(600)
  check('A16', (await openDialog(t2).count()) === 0, 'Escape on the tiles closes it')
  await btn.click()
  await openDialog(t2).waitFor({ timeout: 8000 })
  await openDialog(t2).getByRole('button', { name: new RegExp(T.name) }).first().click()
  const word = norm(await openDialog(t2).locator('p.text-5xl').innerText().catch(() => ''))
  check('A16', word === 'Départ', 'T present → « Départ »', word)
  await t2.keyboard.press('Escape')
  await sleep(600)
  check('A16', (await openDialog(t2).count()) === 1 && (await openDialog(t2).locator('ul[aria-label="Qui pointe ?"]').count()) === 1, 'Escape on the pad returns to the tiles, dialog stays', '')
  await openDialog(t2).getByRole('button', { name: new RegExp(T.name) }).first().click()
  await dragSchema(t2, P2, openDialog(t2))
  const conf = await waitText(t2, /Départ enregistré/, 8000)
  let closed = false
  for (let i = 0; i < 20 && !closed; i++) { closed = (await openDialog(t2).count()) === 0; await sleep(300) }
  check('A16', conf && closed && (await marked(t2, m)), '« Départ enregistré », then the dialog closes itself, page kept', `conf=${conf} closed=${closed}`)
  await sleep(1500)
  const chip = norm(await t2.locator('header button', { hasText: /Départ à|Arrivée à/ }).first().innerText().catch(() => ''))
  check('A16', /^Départ à ?\d{2}:\d{2}$/.test(chip), 'chip follows: « Départ à HH:mm »', chip)
  // arrive again through the same dialog (a second line today)
  await btn.click()
  await openDialog(t2).waitFor({ timeout: 8000 })
  await openDialog(t2).getByRole('button', { name: new RegExp(T.name) }).first().click()
  const w2 = norm(await openDialog(t2).locator('p.text-5xl').innerText().catch(() => ''))
  await dragSchema(t2, P2, openDialog(t2))
  const conf2 = await waitText(t2, /Arrivée enregistrée/, 8000)
  closed = false
  for (let i = 0; i < 20 && !closed; i++) { closed = (await openDialog(t2).count()) === 0; await sleep(300) }
  await sleep(1500)
  const chip2 = norm(await t2.locator('header button', { hasText: /Départ à|Arrivée à/ }).first().innerText().catch(() => ''))
  check('A16', w2 === 'Arrivée' && conf2 && closed && /^Arrivée à ?\d{2}:\d{2}$/.test(chip2), 'after « Départ à »: « Arrivée » again, closes itself, chip « Arrivée à HH:mm »', JSON.stringify({ w2, conf2, closed, chip2 }))
  check('A16', tLines(`and "LocalDay"='${TODAY}'`) === '2', 'SQL: two lines today', tLines(`and "LocalDay"='${TODAY}'`))
})

// ================================================================ a correction, then « Mes présences » (F29, F30, F31), D2
let firstArrival = ''
let leaveRequestId = null
await scenario('A17', async () => {
  firstArrival = psql(`select to_char(min("ArrivalUtc") at time zone 'UTC' + interval '1 hour','HH24:MI') from "AttendanceEntries" where "UserId"='${T.id}' and "LocalDay"='${TODAY}'`)
  const corrected = hhmm(new Date(Date.parse(`${TODAY}T${firstArrival}:00Z`) - 60_000))
  await gotoPresences(adm)
  const heads = (await adm.locator('table:visible thead th').allInnerTexts()).map((h) => norm(h).toLowerCase())
  const col = heads.indexOf(T.name.toLowerCase())
  const todayRowTr = adm.locator('table:visible tbody tr').filter({ has: adm.locator('td:first-child', { hasText: /^lun\. 9$/i }) }).first()
  const cellText = norm(await todayRowTr.locator('td').nth(col).innerText().catch(() => ''))
  check('A17', /en cours/.test(cellText), 'sheet: today\'s open line reads « en cours »', cellText)
  await todayRowTr.locator('td').nth(col).getByRole('button', { name: new RegExp(`^Corriger le pointage de ${T.name}`) }).first().click()
  const dlg = openDialog(adm)
  await dlg.locator('#correct-arrival').waitFor({ timeout: 8000 })
  await dlg.locator('#correct-arrival').fill(corrected.replace(':', ''))
  await dlg.locator('#correct-arrival').blur()
  await dlg.locator('#correct-reason').fill('QA — heure d\'arrivée')
  await dlg.getByRole('button', { name: 'Corriger', exact: true }).click()
  const t = await toast(adm, /Pointage corrigé/, 8000)
  check('D2', Boolean(t), 'Présences « Corriger » → toast « Pointage corrigé. »', t ?? '(none)')
  await sleep(1500)
  const after = norm(await todayRowTr.locator('td').nth(col).innerText().catch(() => ''))
  check('D2', after.includes(corrected) && /Corrigé/.test(after), `the line reads ${corrected} + « Corrigé »`, after)
  // T1: the rail and the page
  await t1.goto(`${WEB}/patients`)
  await sleep(2500)
  const railT = await t1.locator('a[href="/mes-presences"]').count()
  await adm.goto(`${WEB}/patients`)
  await sleep(2500)
  const railA = await adm.locator('a[href="/mes-presences"]').count()
  check('A17', railT >= 1 && railA === 0, '« Mes présences » in T\'s rail, not in the admin\'s (untracked) (F31)', JSON.stringify({ railT, railA }))
  await t1.goto(`${WEB}/mes-presences`)
  await waitText(t1, /Schéma de pointage/, 15000)
  await waitText(t1, /Lundi 9 novembre/, 15000)
  const page = norm(await bodyText(t1))
  console.log('   mes-presences:', page.slice(page.indexOf('Schéma de pointage'), page.indexOf('Schéma de pointage') + 900))
  check('A17', /Schéma de pointage Enregistré/.test(page) && /Changer votre schéma/.test(page), 'schéma card « Enregistré » + « Changer votre schéma »', '')
  const today = page.slice(page.indexOf('Lundi 9 novembre'), page.indexOf('Lundi 9 novembre') + 300)
  check('A17', /en cours/.test(today), 'today: the open line reads « en cours »', today)
  check('A17', new RegExp(`Borne : ${firstArrival} – \\d{2}:\\d{2} · corrigé par Dr Salma Ben Youssef`).test(today), `today: « Borne : ${firstArrival} – HH:mm · corrigé par Dr Salma Ben Youssef » (F29)`, today)
  const fri = page.slice(page.indexOf('Vendredi 6 novembre'), page.indexOf('Vendredi 6 novembre') + 300)
  check('A17', /Ajouté par Dr Salma Ben Youssef/.test(fri) && /Déclaré/.test(fri), '6 nov: « Ajouté par Dr Salma Ben Youssef » + « Déclaré »', fri)
  check('A17', /sans la journée en cours/.test(page), 'hours hint « sans la journée en cours »', '')
  check('A17', !/Présente|présente/.test(page), 'no gendered wording (F30)', (page.match(/.{20}[Pp]résente.{20}/) ?? [''])[0])
  await shot(t1, 'A17-mes-presences')
})

// ================================================================ sign-out prompts (F15)
await scenario('A18', async () => {
  await t1.setViewportSize({ width: 1536, height: 730 })
  await t1.goto(`${WEB}/patients`)
  await sleep(3000)
  await signOutVia(t1)
  const dlg = openDialog(t1)
  await dlg.waitFor({ timeout: 8000 })
  const dt = norm(await dlg.innerText())
  check('A18', AT_BOTH.test(dt) && /Arrivée pointée à \d{2}:\d{2}\./.test(dt), 'off a borne: the title says where (« … à la borne « Accueil » ou « QA » »)', dt)
  const btns = (await dlg.locator('[data-slot="dialog-footer"] button').allInnerTexts()).map(norm)
  const primary = await dlg.locator('[data-slot="dialog-footer"] button', { hasText: /^Se déconnecter$/ }).getAttribute('class').catch(() => '')
  check('A18', JSON.stringify(btns) === '["Annuler","Se déconnecter"]', 'footer: exactly « Annuler » + « Se déconnecter » (no « sans pointer » off a borne)', JSON.stringify(btns))
  check('A18', /bg-primary/.test(primary ?? ''), '« Se déconnecter » is the filled button', primary)
  await shot(t1, 'A18-signout-off-borne-1536x730')
  await dlg.getByRole('button', { name: 'Annuler' }).click()
  await sleep(1200)
  check('A18', !t1.url().includes('/login'), '« Annuler » keeps her signed in', t1.url())
  await t1.setViewportSize({ width: 1180, height: 820 })

  await t2.setViewportSize({ width: 1536, height: 730 })
  await t2.goto(`${WEB}/patients`)
  await sleep(3000)
  await signOutVia(t2)
  const d2 = openDialog(t2)
  await d2.waitFor({ timeout: 8000 })
  const t2t = norm(await d2.innerText())
  const b2 = (await d2.locator('[data-slot="dialog-footer"] button').allInnerTexts()).map(norm)
  check('A18', /^Pointez votre départ Arrivée pointée à \d{2}:\d{2}\./.test(t2t) && (await pads(d2).count()) === 1, 'on the borne: « Pointez votre départ » + the pad', t2t)
  check('A18', JSON.stringify(b2) === '["Annuler","Se déconnecter sans pointer"]', 'footer « Annuler » + « Se déconnecter sans pointer »', JSON.stringify(b2))
  for (const [w, h] of [[1536, 730], [320, 568]]) {
    await t2.setViewportSize({ width: w, height: h })
    await sleep(600)
    const boxes = await d2.locator('[data-slot="dialog-footer"] button').evaluateAll((els) => els.map((e) => { const r = e.getBoundingClientRect(); return { t: e.textContent, b: Math.round(r.bottom), r: Math.round(r.right), l: Math.round(r.left) } }))
    const pb = await pads(d2).last().boundingBox()
    check('C6', boxes.length === 2 && boxes.every((x) => x.b <= h && x.l >= 0 && x.r <= w) && pb && pb.x >= 0 && pb.x + pb.width <= w, `${w}×${h}: both footer buttons and the pad on screen`, JSON.stringify({ boxes, pb }))
    await shot(t2, `C6-signout-on-borne-${w}x${h}`)
  }
  await t2.setViewportSize({ width: 1536, height: 730 })
  await sleep(500)
  await dragSchema(t2, P2, openDialog(t2))
  await openDialog(t2).getByRole('heading', { name: 'Départ enregistré' }).waitFor({ timeout: 8000 }).catch(() => {})
  const conf = norm(await openDialog(t2).innerText().catch(() => ''))
  check('A18', /Départ enregistré/.test(conf) && /À \d{2}:\d{2}\./.test(conf), '« Départ enregistré · À HH:mm. » before the sign-out', conf)
  await shot(t2, 'A18-departure-confirmed')
  const out = await t2.waitForURL(/\/login/, { timeout: 12000 }).then(() => true, () => false)
  check('A18', out, 'then signed out', t2.url())
  await sleep(2500)
  check('A18', (await t2.getByRole('link', { name: 'Pointage' }).count()) === 1, '/login on the borne offers « Pointage »')
  check('A18', tLines(`and "LocalDay"='${TODAY}' and "DepartureUtc" is null`) === '0', 'SQL: today\'s lines closed')
  await t1.goto(`${WEB}/patients`)
  await sleep(3000)
  const chip = norm(await t1.locator('header button', { hasText: /Départ à|Arrivée à/ }).first().innerText().catch(() => ''))
  check('A15', /^Départ à ?\d{2}:\d{2}$/.test(chip), 'T1 chip after the departure: « Départ à HH:mm »', chip)
})

// ================================================================ staff files, tracking a dentist (F17, F32, F35)
await scenario('A19', async () => {
  await gotoSettings(adm)
  const sw = adm.getByRole('switch', { name: `${NADIA.name} pointe` })
  await sw.scrollIntoViewIfNeeded()
  check('A19', (await sw.getAttribute('aria-checked')) === 'false', 'Dr Nadia does not pointe yet')
  await sw.click()
  const dlg = alertDialog(adm)
  await dlg.waitFor({ timeout: 8000 })
  const t = norm(await dlg.innerText())
  check('A19', /Dr Nadia Trabelsi pointe \?/.test(t) && /Dr Nadia Trabelsi devra pointer son arrivée avant de travailler\./.test(t), 'a dentist asks first: « Dr Nadia Trabelsi pointe ? … devra pointer son arrivée avant de travailler. » (F17)', t)
  await shot(adm, 'A19-track-dentist-confirm')
  await dlg.getByRole('button', { name: 'Confirmer' }).click()
  check('A19', Boolean(await toast(adm, /Dr Nadia Trabelsi pointe désormais/, 8000)), 'toast « Dr Nadia Trabelsi pointe désormais. »')
  await adm.goto(`${WEB}/users`)
  await adm.locator(`button[aria-label="Actions pour ${NADIA.name}"]:visible`).first().waitFor({ timeout: 20000 })
  await adm.locator(`button[aria-label="Actions pour ${NADIA.name}"]:visible`).first().click()
  const item = adm.getByRole('menuitem', { name: 'Voir sa fiche', exact: true })
  check('A19', (await item.count()) === 1, 'her row offers « Voir sa fiche » (F35 — every tracked account)')
  await item.click()
  await adm.waitForURL(/\/users\/local/, { timeout: 10000 }).catch(() => {})
  await waitText(adm, /Présences/, 15000)
  await sleep(1500)
  const hdr = adm.locator('section[aria-labelledby="staff-presences"]')
  const ht = norm(await hdr.innerText().catch(() => ''))
  check('A19', /Pointe/.test(ht) && /Schéma à créer/.test(ht), 'her file\'s Présences header: « Pointe » + « Schéma à créer »', ht.slice(0, 200))
  await shot(adm, 'A19-dentist-file')
  const fsw = hdr.getByRole('switch').first()
  await fsw.click()
  check('A19', Boolean(await toast(adm, /Dr Nadia Trabelsi ne pointe plus/, 8000)) && (await alertDialog(adm).count()) === 0, 'off without a confirm → « … ne pointe plus. »')
  psql(`update "Users" set "AttendanceTrackedOverride" = null where "Id"='${NADIA.id}'`) // own artefact: back to « follows her role »
})

await scenario('A20', async () => {
  await adm.goto(`${WEB}/users`)
  await adm.locator(`button[aria-label="Actions pour ${T.name}"]:visible`).first().waitFor({ timeout: 20000 })
  await adm.locator(`button[aria-label="Actions pour ${T.name}"]:visible`).first().click()
  check('A20', (await adm.getByRole('menuitem', { name: 'Voir sa fiche et ses congés' }).count()) === 1, 'T\'s row offers « Voir sa fiche et ses congés »')
  await closeOverlay(adm)
  await adm.goto(`${WEB}/users/${encodeURIComponent(T.id)}`)
  await waitText(adm, /Présences/, 20000)
  await sleep(2000)
  const hdr = adm.locator('section[aria-labelledby="staff-presences"]')
  const ht = norm(await hdr.innerText().catch(() => ''))
  check('A20', /Pointe/.test(ht) && /Schéma créé/.test(ht) && (await hdr.getByRole('button', { name: `Réinitialiser le schéma de ${T.name}` }).count()) === 1, 'Présences header: « Pointe » + « Schéma créé » + « Réinitialiser le schéma » (F35)', ht.slice(0, 200))
  check('A20', (await hdr.getByRole('switch').first().getAttribute('aria-checked')) === 'true', 'the switch is on')
  check('A20', /jours pointés/i.test(ht), 'her month below the header', ht.slice(0, 300))
  await shot(adm, 'A20-staff-file')
})

await scenario('B10', async () => {
  const fileRe = (u) => new URL(u).pathname.endsWith('/file') && u.includes('/team/users/')
  await adm.route((u) => fileRe(u.toString()), (r) => r.fulfill({ status: 500, contentType: 'application/json', body: '{"error":"QA"}' }))
  await adm.goto(`${WEB}/users/${encodeURIComponent(T.id)}`)
  await waitText(adm, /n'ont pas pu être chargés|Présences/, 20000)
  await sleep(2500)
  const page = norm(await bodyText(adm))
  check('B10', /Les congés n'ont pas pu être chargés\./.test(page) && (await adm.locator('section[aria-labelledby="staff-presences"]').count()) === 1, 'a failed congés read: « Les congés n\'ont pas pu être chargés. » and Présences still there (F32)', page.slice(0, 300))
  await shot(adm, 'B10-file-conges-failed')
  await adm.unrouteAll({ behavior: 'ignoreErrors' }).catch(() => {})
})

await scenario('A21', async () => {
  await gotoPresences(adm)
  const prev = adm.getByRole('button', { name: 'Mois précédent' }).first()
  const next = adm.getByRole('button', { name: 'Mois suivant' }).first()
  const b1 = await prev.boundingBox()
  check('A21', (await next.isDisabled()) && /novembre 2026/i.test(norm(await adm.locator('[aria-live="polite"]', { hasText: /2026/ }).first().innerText())), 'Présences: « novembre 2026 », « Mois suivant » disabled on the current month')
  await prev.click()
  check('A21', await waitText(adm, /octobre 2026/i, 8000), '‹ → « octobre 2026 »')
  await next.click()
  // T asks for one day off (API, own artefact): « Mes congés » then shows its month — the other half of F36
  const req = await api('POST', '/team/me/leave-requests', { type: 'Other', startDate: '2026-12-14', endDate: '2026-12-14', halfDay: false, reason: 'QA' }, tPhone.token)
  leaveRequestId = req.json?.id ?? req.json?.value?.id ?? null
  check('A21', req.status < 300 && Boolean(leaveRequestId), 'arranged: a congé request for T (14 déc.)', `${req.status} ${JSON.stringify(req.json).slice(0, 160)}`)
  await t1.setViewportSize({ width: 1440, height: 900 })
  await t1.goto(`${WEB}/conges`)
  const prevC = t1.getByRole('button', { name: 'Mois précédent' }).first()
  await prevC.waitFor({ timeout: 20000 })
  const b2 = await prevC.boundingBox()
  await t1.goto(`${WEB}/mes-presences`)
  const prevM = t1.getByRole('button', { name: 'Mois précédent' }).first()
  await prevM.waitFor({ timeout: 20000 })
  const b3 = await prevM.boundingBox()
  const same = (x, y) => x && y && Math.round(x.width) === Math.round(y.width) && Math.round(x.height) === Math.round(y.height)
  check('A21', same(b1, b2) && same(b2, b3) && Math.round(b1.height) === 32, '« Mes congés », « Mes présences » and Présences: one stepper, same size (32 px) (F36)', JSON.stringify({ b1, b2, b3 }))
  await adm.goto(`${WEB}/users?onglet=conges`)
  await waitText(adm, new RegExp(T.name), 20000)
  const congesOk = !/n'a pas pu|n'ont pas pu/.test(norm(await bodyText(adm)))
  check('D3', congesOk && norm(await bodyText(adm)).includes(T.name), "/users?onglet=conges renders, T's request listed, no failure", '')
  await shot(adm, 'A21-D3-conges-tab')
  await t1.goto(`${WEB}/conges`)
  await t1.getByRole('button', { name: 'Mois précédent' }).first().waitFor({ timeout: 20000 })
  const isCoarse = await coarse(t1, true, 820, 1180)
  const hc = await heightOf(t1.getByRole('button', { name: 'Mois précédent' }).first())
  check('C4', isCoarse && hc >= 44, 'coarse: the stepper buttons ≥ 44 px', JSON.stringify({ isCoarse, hc }))
  await coarse(t1, false)
  await t1.setViewportSize({ width: 1180, height: 820 })
  await adm.setViewportSize({ width: 1440, height: 900 })
})

// ================================================================ simulated reads (B7, B8, B9)
await scenario('B7', async () => {
  const todayRe = (u) => new URL(u).pathname.endsWith('/team/today')
  await adm.route((u) => todayRe(u.toString()), (r) => r.fulfill({ status: 500, contentType: 'application/json', body: '{"error":"QA"}' }))
  await adm.goto(presencesUrl)
  await waitText(adm, /L'équipe du jour n'a pas pu être chargée/, 20000)
  const page = norm(await bodyText(adm))
  check('B7', /L'équipe du jour n'a pas pu être chargée\./.test(page) && (await todaySection(adm).getByRole('button', { name: 'Réessayer' }).count()) === 1, '« L\'équipe du jour n\'a pas pu être chargée. » + « Réessayer » (F33)', page.slice(0, 200))
  check('B7', (await adm.locator('table:visible').count()) === 1, 'the month still shows')
  await shot(adm, 'B7-today-failed')
  await adm.unrouteAll({ behavior: 'ignoreErrors' }).catch(() => {})
  await todaySection(adm).getByRole('button', { name: 'Réessayer' }).click()
  check('B7', await waitText(adm, new RegExp(OTHER.name), 8000) && (await todaySection(adm).locator('li').count()) >= 3, '« Réessayer » brings the list back')
})

await scenario('B8', async () => {
  const todayRe = (u) => new URL(u).pathname.endsWith('/team/today')
  const variant = async (mut, wait) => {
    await adm.unrouteAll({ behavior: 'ignoreErrors' }).catch(() => {})
    await adm.route((u) => todayRe(u.toString()), async (r) => {
      const res = await r.fetch()
      const j = await res.json()
      await r.fulfill({ response: res, json: mut(j) })
    })
    await adm.goto(presencesUrl)
    await waitText(adm, wait, 15000)
    return norm(await todaySection(adm).innerText().catch(() => ''))
  }
  const s1 = await variant((j) => ({ ...j, setupIssue: 'no_punch_since_opening' }), /Aucun pointage depuis l'ouverture/)
  const href = await todaySection(adm).getByRole('link', { name: 'Paramètres → Équipe' }).getAttribute('href').catch(() => null)
  check('B8', /Aucun pointage depuis l'ouverture/.test(s1) && href === '/settings#equipe', 'setup line « Aucun pointage depuis l\'ouverture » + link « Paramètres → Équipe » (F12/F9)', `${s1.slice(0, 120)} | ${href}`)
  await shot(adm, 'B8-setup-line')
  const s2 = await variant((j) => ({ ...j, setupIssue: 'borne_pending' }), /Borne en attente de son appareil/)
  check('B8', /Borne en attente de son appareil/.test(s2), 'setup line « Borne en attente de son appareil »', s2.slice(0, 120))
  const s3 = await variant((j) => ({ ...j, isOpenDay: false }), /Cabinet fermé aujourd'hui/)
  check('B8', /Cabinet fermé aujourd'hui/.test(s3) && !s3.includes(OTHER.name), '« Cabinet fermé aujourd\'hui », only who came is listed (F11)', s3.slice(0, 200))
  const s4 = await variant((j) => ({ ...j, people: [] }), /Personne ne pointe dans ce cabinet/)
  const href4 = await todaySection(adm).getByRole('link').first().getAttribute('href').catch(() => null)
  check('B8', /Personne ne pointe dans ce cabinet/.test(s4) && href4 === '/settings#equipe', '« Personne ne pointe dans ce cabinet » with a real link (F9)', `${s4.slice(0, 120)} | ${href4}`)
  await adm.unrouteAll({ behavior: 'ignoreErrors' }).catch(() => {})
})

await scenario('B9', async () => {
  const statusRe = (u) => new URL(u).pathname.endsWith('/clinics/user-status')
  await adm.route((u) => statusRe(u.toString()), async (r) => {
    const res = await r.fetch()
    const j = await res.json()
    const v = j.value ?? j
    if (v?.clinic) v.clinic.leaveRequestsEnabled = false
    await r.fulfill({ response: res, json: j })
  })
  await adm.goto(`${WEB}/users`)
  await adm.getByRole('tab', { name: /Congés/ }).waitFor({ timeout: 20000 })
  const tabs = (await adm.getByRole('tab').allInnerTexts()).map(norm)
  check('B9', ['Comptes', 'Congés', 'Présences'].every((x) => tabs.some((t) => t.startsWith(x))), 'tabs Comptes · Congés · Présences with congés off (F9)', JSON.stringify(tabs))
  await adm.getByRole('tab', { name: /Congés/ }).click()
  await waitText(adm, /Les demandes de congé ne sont pas activées/, 8000)
  const href = await adm.getByRole('link', { name: 'Activer dans Paramètres' }).getAttribute('href').catch(() => null)
  check('B9', href === '/settings#equipe', '« Les demandes de congé ne sont pas activées » + « Activer dans Paramètres » → /settings#equipe', href)
  await shot(adm, 'B9-module-off-tab')
  await adm.unrouteAll({ behavior: 'ignoreErrors' }).catch(() => {})
})

// ================================================================ D3 / D5 on T1
await scenario('D3', async () => {
  await t1.goto(`${WEB}/conges`)
  const ok3 = await waitText(t1, /Mes congés/, 15000)
  await sleep(2000)
  check('D3', ok3 && (await t1.getByRole('button', { name: 'Mois précédent' }).count()) >= 1 && /En cours et à venir/i.test(norm(await bodyText(t1))) && (await gate(t1).count()) === 0, '/conges renders « Mes congés » with her request and its month (no lock: she has a line today)', '')
  await shot(t1, 'D3-conges')
})
await scenario('D5', async () => {
  await t1.goto(`${WEB}/mon-profil`)
  const ok5 = await waitText(t1, /Schéma de pointage/, 15000)
  const page = norm(await bodyText(t1))
  check('D5', ok5 && /Schéma de pointage Enregistré/.test(page) && /Changer votre schéma/.test(page) && /Mes présences/.test(page), 'Mon profil: « Schéma de pointage · Enregistré » + « Changer votre schéma » + « Mes présences »', '')
  await shot(t1, 'D5-mon-profil')
})

// ================================================================ B6 — a pending borne whose device signs out, replaced
await scenario('B6', async () => {
  const tIpad = await apiLogin(T.email, 'Safari sur iPad')
  const admPhone = await apiLogin(ADMIN.email, 'Chrome sur Android', true)
  b6Sessions.push(tIpad, admPhone)
  const dlg = await openAddBorne(adm)
  await dlg.locator('#borne-name').fill('QA tablette')
  const ipadRow = dlg.locator(`label[for="borne-device-${tIpad.family}"]`)
  if (!(await ipadRow.isVisible().catch(() => false))) {
    console.log('   rows:', JSON.stringify((await dlg.locator('label[for^="borne-device-"]').evaluateAll((els) => els.map((e) => `${e.htmlFor} ${e.textContent}`)))), 'want', tIpad.family)
    await shot(adm, 'B6-DEBUG-device-list')
  }
  await ipadRow.click({ timeout: 10000 })
  await dlg.getByRole('button', { name: 'Enregistrer la borne' }).click()
  await stepUpConfirm(adm)
  const t = await toast(adm, /QA tablette/, 12000)
  check('B6', Boolean(t) && new RegExp(`en attente de l'appareil de ${T.name}`).test(t), `toast « Borne « QA tablette » en attente de l'appareil de ${T.name}. »`, t ?? '(none)')
  await sleep(1500)
  const li = norm(await settingsRow(adm, 'QA tablette').innerText().catch(() => ''))
  check('B6', /En attente depuis \d{2}:\d{2} · /.test(li) && li.includes(`${T.name} · Safari sur iPad`), `row « En attente depuis HH:mm · ${T.name} · Safari sur iPad » (F10)`, li)
  const oldId = psql(`select "Id" from "AttendanceTerminals" where "ClinicId"='${CLINIC}' and "Name"='QA tablette' and "RevokedAt" is null`)
  await api('POST', '/auth/logout', { refreshToken: tIpad.refresh })
  let n = '0'
  for (let i = 0; i < 20 && (n = psql(`select count(*) from "StaffNotifications" where "Category"=27 and "CreatedAt" >= '${walkStart}'`)) === '0'; i++) await sleep(500)
  check('B6', Number(n) >= 1, 'SQL: « Borne non reçue » rows written on the sign-out', n)
  await gotoSettings(adm)
  const red = settingsRow(adm, 'QA tablette')
  const rt = norm(await red.innerText().catch(() => ''))
  check('B6', /Appareil déconnecté avant d'avoir reçu la borne/.test(rt) && (await red.locator('p.text-destructive').count()) === 1 && /Choisir un autre appareil/.test(rt), 'row red « Appareil déconnecté avant d\'avoir reçu la borne » + « Choisir un autre appareil » (F10)', rt)
  await red.scrollIntoViewIfNeeded()
  await shot(adm, 'B6-not-received-row')
  await openBell(adm, /Borne non reçue/)
  const bell = norm(await bodyText(adm))
  await shot(adm, 'B6-admin-bell-not-received')
  check('B6', /Borne non reçue[\s\S]*La borne «\s*QA tablette\s*» n'a pas été installée : la session de l'appareil choisi[\s\S]*a été fermée\. Choisissez un autre appareil\./.test(bell), 'admin bell « Borne non reçue · … Choisissez un autre appareil. »', bell.slice(bell.indexOf('Borne non'), bell.indexOf('Borne non') + 220))
  await adm.locator('text=Borne non reçue').first().click()
  check('B13', await adm.waitForURL(/\/settings#equipe/, { timeout: 8000 }).then(() => true, () => false), '« Borne non reçue » opens /settings#equipe', adm.url())
  await gotoSettings(adm)
  await settingsRow(adm, 'QA tablette').getByRole('button', { name: 'Choisir un autre appareil' }).click()
  const d2 = openDialog(adm)
  await d2.locator('#borne-name').waitFor({ timeout: 8000 })
  await waitText(adm, /actif il y a/, 10000)
  const title = norm(await d2.locator('h2').first().innerText())
  const rep = norm(await d2.locator('#borne-replace').innerText())
  check('B6', /Remplacer la borne «\s*QA tablette\s*»/.test(title) && /QA tablette \(en attente\)/.test(rep) && (await d2.locator('#borne-name').inputValue()) === 'QA tablette', 'dialog « Remplacer la borne « QA tablette » », Remplace preset, name kept (F2)', JSON.stringify({ title, rep }))
  check('B6', (await d2.locator(`label[for="borne-device-${tIpad.family}"]`).count()) === 0, 'the signed-out device is no longer offered')
  await d2.locator(`label[for="borne-device-${admPhone.family}"]`).click()
  await d2.getByRole('button', { name: 'Enregistrer la borne' }).click()
  await stepUpConfirm(adm)
  const t2b = await toast(adm, /en attente de l'appareil de Dr Salma Ben Youssef/, 12000)
  check('B6', Boolean(t2b), 'toast « … en attente de l\'appareil de Dr Salma Ben Youssef. »', t2b ?? '(none)')
  const rows = psql(`select ("Id"='${oldId}')::text || ':' || ("RevokedAt" is null)::text || ':' || coalesce("PendingSessionFamilyId"::text,'') from "AttendanceTerminals" where "ClinicId"='${CLINIC}' and "Name"='QA tablette' and "CreatedAt" >= '${walkStart}' order by "CreatedAt"`)
  check('B6', rows === `true:false:${tIpad.family}\nfalse:true:${admPhone.family}`, 'SQL: the old waiting row retired in the same save, the new one waits for the admin\'s phone', rows.replace(/\n/g, ' ; '))
  await sleep(1500)
  await adm.getByRole('button', { name: 'Retirer la borne QA tablette', exact: true }).click()
  const conf = alertDialog(adm)
  await conf.waitFor({ timeout: 8000 })
  const ct = norm(await conf.innerText())
  check('B6', !/L'app ne sera plus bloquée/.test(ct), 'retiring a waiting borne never says « L\'app ne sera plus bloquée »', ct)
  await conf.getByRole('button', { name: 'Retirer', exact: true }).click()
  check('B6', Boolean(await toast(adm, /Borne «\s*QA tablette\s*» retirée/, 8000)), 'toast « Borne « QA tablette » retirée. »')
})

// ================================================================ A22 — « Remplace » end to end, then retire (cleanup through the product)
await scenario('A22', async () => {
  const dlg = await openAddBorne(adm)
  await dlg.locator('#borne-replace').click()
  await adm.getByRole('option', { name: 'QA', exact: true }).click()
  await sleep(400)
  check('A22', (await dlg.locator('#borne-name').inputValue()) === 'QA' && /Remplacer la borne «\s*QA\s*»/.test(norm(await dlg.locator('h2').first().innerText())), 'choosing « QA » in « Remplace » names it « QA » and retitles the dialog', await dlg.locator('#borne-name').inputValue())
  await dlg.locator('label[for="borne-device-this"]').click()
  await dlg.getByRole('button', { name: 'Enregistrer la borne' }).click()
  await stepUpConfirm(adm)
  const t = await toast(adm, /Cet appareil est maintenant la borne/, 12000)
  check('A22', Boolean(t) && /«\s*QA\s*»/.test(t), 'toast « Cet appareil est maintenant la borne « QA ». »', t ?? '(none)')
  const rows = psql(`select ("Id"='${qaId}')::text || ':' || ("RevokedAt" is null)::text from "AttendanceTerminals" where "ClinicId"='${CLINIC}' and "Name"='QA' and "CreatedAt" >= '${walkStart}' order by "CreatedAt"`)
  check('A22', rows === 'true:false\nfalse:true', 'SQL: old « QA » retired in the same save, one live « QA »', rows.replace(/\n/g, ' ; '))
  await gotoSettings(adm)
  const li = norm(await settingsRow(adm, 'QA').innerText().catch(() => ''))
  check('A22', /cet appareil/.test(li), 'row marked « cet appareil »', li)
  await adm.getByRole('button', { name: 'Retirer la borne QA', exact: true }).click()
  const conf = alertDialog(adm)
  await conf.waitFor({ timeout: 8000 })
  const ct = norm(await conf.innerText())
  check('A22', /Cet appareil ne pourra plus pointer\./.test(ct) && !/L'app ne sera plus bloquée/.test(ct), '« Cet appareil ne pourra plus pointer. », no « L\'app ne sera plus bloquée » (Accueil stays)', ct)
  await shot(adm, 'A22-retirer-this-device')
  await conf.getByRole('button', { name: 'Retirer', exact: true }).click()
  check('A22', Boolean(await toast(adm, /Borne «\s*QA\s*» retirée/, 8000)), 'toast « Borne « QA » retirée. »')
  await sleep(1500)
  const steps = (await adm.locator('section[aria-labelledby="pointage-setup-title"] ol > li').allInnerTexts()).map(norm)
  check('A22', /Actif à partir de demain 09:00/.test(steps[3] ?? ''), 'chip back to « Actif à partir de demain 09:00 »', steps[3])
  check('A22', psql(`select count(*) from "AttendanceTerminals" where "ClinicId"='${CLINIC}' and "RevokedAt" is null`) === '1', 'only « Accueil » is live again (SQL)')
})

// ================================================================ planned not-exercised
if (!ONLY) skip('B11', '« Aucune borne active » and 409 borne_last_while_present need « Accueil » (the owner\'s borne) retired')
if (!ONLY) skip('B12', 'the « Départ non pointé » reminder fires 15 min after closing (17:15); moving the hours is the owner\'s data')

// ================================================================ summary + own-artefact cleanup
console.log('\n================ SUMMARY')
const pass = results.filter((r) => r.outcome === 'pass').length
const fail = results.filter((r) => r.outcome === 'fail').length
const skipped = results.filter((r) => r.outcome === 'skip').length
console.log(`pass ${pass} · fail ${fail} · skip ${skipped}`)
findings.forEach((f) => console.log('  ❌', f.id, '—', f.m, f.d ? `(${f.d})` : ''))
fs.writeFileSync(path.join(OUT, 'last-run-review-fixes.json'), JSON.stringify({ at: new Date().toISOString(), results }, null, 2))

if (process.env.QA_KEEP !== '1') {
  console.log('\nCleanup (own artefacts):')
  await signOutVia(t1).catch(() => {})
  await sleep(1500)
  await signOutVia(adm).catch(() => {})
  await sleep(1500)
  for (const s of [tPhone, admPhone, tIpad, ...b6Sessions]) await api('POST', '/auth/logout', { refreshToken: s.refresh }).catch(() => {})
  // deactivate T through the API, the established QA pattern
  const me = await apiLogin(ADMIN.email, 'QA cleanup', true)
  const list = await api('GET', '/users?pageSize=200', undefined, me.token)
  const users = (list.json?.value ?? list.json)?.items ?? (list.json?.value ?? list.json) ?? []
  const tu = users.find?.((u) => u.id === T.id)
  if (tu) console.log('  deactivate T:', (await api('PUT', `/users/${encodeURIComponent(T.id)}/status`, { isActive: false, version: tu.version }, me.token)).status)
  for (const id of psql(`select "Id" from "AttendanceTerminals" where "ClinicId"='${CLINIC}' and "RevokedAt" is null and "CreatedAt" >= '${walkStart}'`).split('\n').filter(Boolean)) {
    console.log('  retire leftover borne', id, (await api('DELETE', `/team/terminals/${id}`, undefined, me.token)).status)
  }
  await api('POST', '/auth/logout', { refreshToken: me.refresh })
  console.log('  ', psql(`
    delete from "NotificationReads" where "NotificationId" in (select "Id" from "StaffNotifications" where "ClinicId"='${CLINIC}' and "CreatedAt" >= '${walkStart}' and "Category" between 25 and 28);
    delete from "NotificationDismissals" where "NotificationId" in (select "Id" from "StaffNotifications" where "ClinicId"='${CLINIC}' and "CreatedAt" >= '${walkStart}' and "Category" between 25 and 28);
    delete from "StaffNotifications" where "ClinicId"='${CLINIC}' and "CreatedAt" >= '${walkStart}' and "Category" between 25 and 28;
    delete from "NotificationReads" where "NotificationId" in (select "Id" from "StaffNotifications" where "ClinicId"='${CLINIC}' and "CreatedAt" >= '${walkStart}' and "Category" between 20 and 23 and ("Title" || "Message") like '%${T.name}%');
    delete from "NotificationDismissals" where "NotificationId" in (select "Id" from "StaffNotifications" where "ClinicId"='${CLINIC}' and "CreatedAt" >= '${walkStart}' and "Category" between 20 and 23 and ("Title" || "Message") like '%${T.name}%');
    delete from "StaffNotifications" where "ClinicId"='${CLINIC}' and "CreatedAt" >= '${walkStart}' and "Category" between 20 and 23 and ("Title" || "Message") like '%${T.name}%';
    delete from "AttendanceEntries" where "UserId"='${T.id}';
    delete from "StaffLeaveRequests" where "UserId"='${T.id}';
    update "SessionFamilies" set "EndedAtUtc"=now(), "EndedReason"='qa' where "UserId"='${T.id}' and "EndedAtUtc" is null;
    update "Users" set "AttendanceTrackedOverride"=null where "Id"='${NADIA.id}';
    select 'live bornes: ' || string_agg("Name", ', ') from "AttendanceTerminals" where "ClinicId"='${CLINIC}' and "RevokedAt" is null;`))
}
await browser.close().catch(() => {})
process.exit(0)
