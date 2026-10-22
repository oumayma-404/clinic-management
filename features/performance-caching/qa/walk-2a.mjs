// QA walk for plan-2a.md (RT-0..RT-5). Usage: node walk-2a.mjs <scratchpad>
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const [SCRATCH] = process.argv.slice(2)
const { chromium } = createRequire(join(SCRATCH, 'package.json'))('playwright-core')
const HERE = dirname(fileURLToPath(import.meta.url))
const SHOTS = join(HERE, 'shots'); mkdirSync(SHOTS, { recursive: true })

const NEW_WEB = 'http://localhost:3099', NEW_API = 'http://localhost:5099/api', OLD_WEB = 'http://localhost:3000'
const PATIENT = 'd3220000-0000-4000-8000-000000000003'
const DOCTOR = { email: 'qa.doctor@ibnkhaldoun.test', password: 'QaAudit2026!y' }
const SECRETARY = { email: 'qa.secretary@ibnkhaldoun.test', password: 'QaAudit2026!y' }

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }
const info = (id, m) => console.log(`  ℹ  [${id}] ${m}`)
const settle = ms => new Promise(r => setTimeout(r, ms))

// Every hub WebSocket a page opens or closes, as CDP sees it.
async function watchSockets(context, page) {
  const cdp = await context.newCDPSession(page)
  await cdp.send('Network.enable')
  const s = { created: 0, open: new Set() }
  cdp.on('Network.webSocketCreated', e => { if (e.url.includes('/hub/clinic')) { s.created++; s.open.add(e.requestId) } })
  cdp.on('Network.webSocketClosed', e => s.open.delete(e.requestId))
  return s
}

async function signIn(page, web, who) {
  await page.goto(`${web}/login`, { waitUntil: 'domcontentloaded' })
  await page.fill('input[type=email]', who.email)
  await page.fill('input[type=password]', who.password)
  await page.click("button:has-text('Se connecter')")
  await page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 60000 })
}
async function closeOverlays(page) {
  for (let i = 0; i < 3; i++) {
    if (await page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible').count() === 0) return
    await page.keyboard.press('Escape'); await settle(500)
  }
}
// Returns how many hub sockets THIS page load created. CDP reports no close for a socket torn down by a full
// navigation, so an « open » set accumulates across loads; per-load creation is the honest count.
async function openAndSettle(page, url, marker, sock) {
  const before = sock ? sock.created : 0
  await page.goto(url, { waitUntil: 'domcontentloaded' })
  await page.locator(`${marker} >> visible=true`).first().waitFor({ timeout: 90000 })
  await closeOverlays(page)
  await settle(5000) // let every subscriber mount and the connection settle
  return sock ? sock.created - before : 0
}
async function apiLogin(who) {
  const r = await fetch(`${NEW_API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(who) })
  return (await r.json()).value.accessToken
}

const browser = await chromium.launch({ channel: 'chrome', headless: true })

// RT-0 — baseline on the peer's stack (old code), read-only
try {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
  const page = await ctx.newPage()
  const sock = await watchSockets(ctx, page)
  await signIn(page, OLD_WEB, DOCTOR)
  const agenda = await openAndSettle(page, `${OLD_WEB}/appointments`, 'text=Rendez-vous', sock)
  const patient = await openAndSettle(page, `${OLD_WEB}/patients/${PATIENT}`, 'text=Emna', sock)
  info('RT-0', `old code: loading /appointments created ${agenda} hub sockets; loading the patient page created ${patient}`)
  await ctx.close()
} catch (e) { skip('RT-0', `baseline unavailable: ${e.message.split('\n')[0]}`) }

// RT-1 .. RT-3, RT-5 — new code, doctor
const ctxA = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
const pageA = await ctxA.newPage()
const sockA = await watchSockets(ctxA, pageA)
try {
  await signIn(pageA, NEW_WEB, DOCTOR)
  const n = await openAndSettle(pageA, `${NEW_WEB}/appointments`, 'text=Rendez-vous', sockA)
  if (n === 1) ok('RT-1', 'loading /appointments created 1 hub socket')
  else bad('RT-1', '/appointments socket count', `created=${n}`)
} catch (e) { bad('RT-1', 'threw', e.message) }

try {
  const createdBefore = sockA.created
  for (const label of ['Patients', "Liste d'attente"]) {
    await closeOverlays(pageA)
    await pageA.locator(`nav a:has-text("${label}") >> visible=true`).first().click()
    await settle(4000)
  }
  const path = new URL(pageA.url()).pathname
  if (sockA.created === createdBefore && path === '/waiting-list') ok('RT-2', `two sidebar navigations → ${path}: 0 new sockets`)
  else bad('RT-2', 'client-side navigation', `path=${path} new=${sockA.created - createdBefore}`)
} catch (e) { bad('RT-2', 'threw', e.message) }

try {
  const n = await openAndSettle(pageA, `${NEW_WEB}/patients/${PATIENT}`, 'text=Emna', sockA)
  await pageA.screenshot({ path: join(SHOTS, 'RT-3.png') })
  if (n === 1) ok('RT-3', 'loading the patient page created 1 hub socket')
  else bad('RT-3', 'patient page socket count', `created=${n}`)
} catch (e) { bad('RT-3', 'threw', e.message) }

// RT-4 — live update reaches another user's tab without a reload
let entryId = null
try {
  const ctxB = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
  const pageB = await ctxB.newPage()
  await signIn(pageB, NEW_WEB, SECRETARY)
  await openAndSettle(pageB, `${NEW_WEB}/waiting-list`, "text=Liste d'attente")
  const marker = `QA-2a ${Date.now()}`
  const token = await apiLogin(DOCTOR)
  const created = await fetch(`${NEW_API}/waiting-list`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify({ patientId: PATIENT, desiredTimeframe: marker }),
  })
  const body = await created.json()
  entryId = body?.value?.id ?? body?.id ?? null
  const appeared = await pageB.locator(`text=${marker} >> visible=true`).first().waitFor({ timeout: 10000 }).then(() => true, () => false)
  await pageB.screenshot({ path: join(SHOTS, 'RT-4.png') })
  let gone = null
  if (entryId) {
    await fetch(`${NEW_API}/waiting-list/${entryId}`, { method: 'DELETE', headers: { Authorization: `Bearer ${token}` } })
    gone = await pageB.locator(`text=${marker}`).first().waitFor({ state: 'detached', timeout: 10000 }).then(() => true, () => false)
  }
  if (created.ok && appeared && gone) ok('RT-4', 'entry appeared in the other tab without a reload, and left it after the delete')
  else bad('RT-4', 'live update', JSON.stringify({ status: created.status, entryId, appeared, gone }))
  await ctxB.close()
} catch (e) { bad('RT-4', 'threw', e.message) }

// RT-5 — signing out closes the socket, and /login opens none
try {
  await pageA.evaluate(() => fetch('/bff/auth/local-logout', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' }))
  await pageA.goto(`${NEW_WEB}/login`, { waitUntil: 'domcontentloaded' })
  const createdAtLogin = sockA.created
  await settle(11000)
  if (sockA.created === createdAtLogin) ok('RT-5', 'signed out: no hub socket attempted on /login over 11 s')
  else bad('RT-5', 'after sign-out', `newAttempts=${sockA.created - createdAtLogin}`)
} catch (e) { bad('RT-5', 'threw', e.message) }

skip('RT-6', 'reconnect catch-up needs the API taken down mid-walk; the onreconnected handler moved verbatim')

await browser.close()
if (entryId) console.log(`  (test entry ${entryId} created and deleted by the walk)`)
const real = findings.filter(f => f.m)
console.log(real.length ? `\nFINDINGS:\n${real.map(f => JSON.stringify(f)).join('\n')}` : '\nALL CLEAR (RT-6 not exercised)')
