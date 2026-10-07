// QA walk for plan-2b.md (C-0..C-7). Usage: node walk-2b.mjs <scratchpad>
import { createRequire } from 'node:module'
import { createHmac } from 'node:crypto'
import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const [SCRATCH] = process.argv.slice(2)
const { chromium } = createRequire(join(SCRATCH, 'package.json'))('playwright-core')
const HERE = dirname(fileURLToPath(import.meta.url))
const SHOTS = join(HERE, 'shots'); mkdirSync(SHOTS, { recursive: true })

const NEW_WEB = 'http://localhost:3099', NEW_API = 'http://localhost:5099/api', OLD_WEB = 'http://localhost:3000'
const PATIENT = 'd3220000-0000-4000-8000-000000000003'
const PLAN = 'c657da46-bf5d-486c-adcc-6e8a84775e86'
const DOCTOR = { email: 'qa.doctor@ibnkhaldoun.test', password: 'QaAudit2026!y' }
const SECRETARY = { email: 'qa.secretary@ibnkhaldoun.test', password: 'QaAudit2026!y' }
const ADMIN = { email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totp: '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO' }
// A 1×1 PNG — the upload door checks the signature, not the size.
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64')

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }
const info = (id, m) => console.log(`  ℹ  [${id}] ${m}`)
const settle = ms => new Promise(r => setTimeout(r, ms))

// ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────
const b32 = s => { const a = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'; let bits = ''; for (const c of s) bits += a.indexOf(c).toString(2).padStart(5, '0'); return Buffer.from(bits.match(/.{8}/g).map(b => parseInt(b, 2))) }
const totp = (secret, w) => { const buf = Buffer.alloc(8); buf.writeBigUInt64BE(BigInt(w)); const h = createHmac('sha1', b32(secret)).update(buf).digest(); const o = h[h.length - 1] & 15; return String((h.readUInt32BE(o) & 0x7fffffff) % 1e6).padStart(6, '0') }
const usedFile = join(SCRATCH, 'totp-last-window.txt')
async function freshWindow() {
  const last = existsSync(usedFile) ? Number(readFileSync(usedFile, 'utf8')) : 0
  let w = Math.floor(Date.now() / 30000)
  if (w <= last || (Date.now() / 1000) % 30 > 25) { await settle(((Math.max(last, w) + 1) * 30000 - Date.now()) + 1000); w = Math.floor(Date.now() / 30000) }
  writeFileSync(usedFile, String(w)); return w
}
async function adminToken() {
  const w = await freshWindow()
  const r = await fetch(`${NEW_API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: ADMIN.email, password: ADMIN.password, totpCode: totp(ADMIN.totp, w) }) })
  return (await r.json()).value.accessToken
}
const api = (token, path, init = {}) => fetch(`${NEW_API}${path}`, { ...init, headers: { ...(init.headers || {}), Authorization: `Bearer ${token}` } })
const unwrap = async r => { const b = await r.json().catch(() => null); return b?.value ?? b }

function countRequests(page) {
  const seen = []
  page.on('request', r => { const u = r.url(); if (u.includes('/api/')) seen.push(new URL(u).pathname) })
  return {
    mark: () => seen.length,
    since: (m, part) => seen.slice(m).filter(p => p.includes(part)).length,
  }
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
async function open(page, url, marker) {
  await page.goto(url, { waitUntil: 'domcontentloaded' })
  await page.locator(`${marker} >> visible=true`).first().waitFor({ timeout: 90000 })
  await settle(4000)
  await closeOverlays(page)
}
const pickerTrigger = page => page.locator("[role='dialog'] button:has-text(\"Sélectionner un type d'acte\") >> visible=true").first()
async function openDialogAndPicker(page) {
  await page.locator('button:text-is("Nouveau") >> visible=true').first().click()
  await page.locator("[role='dialog'] >> visible=true").first().waitFor({ timeout: 15000 })
  await pickerTrigger(page).waitFor({ timeout: 15000 })
  await settle(500)
  await pickerTrigger(page).click()
  await page.locator('[cmdk-item] >> visible=true').first().waitFor({ timeout: 15000 })
  return page.locator('[cmdk-item] >> visible=true').count()
}
async function closeDialog(page) {
  await page.keyboard.press('Escape'); await settle(300) // the picker
  await page.keyboard.press('Escape'); await settle(800) // the dialog
  const confirm = page.locator('[role="alertdialog"] >> visible=true')
  if (await confirm.count()) { await confirm.locator('button:has-text("Quitter"), button:has-text("Fermer"), button:has-text("Abandonner")').first().click().catch(() => {}) }
}

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const viewport = { width: 1440, height: 900 }

// C-0 — baseline on the peer's stack (old code), read-only
try {
  const ctx = await browser.newContext({ viewport, locale: 'fr-FR' })
  const page = await ctx.newPage()
  const req = countRequests(page)
  await signIn(page, OLD_WEB, DOCTOR)
  const m0 = req.mark()
  await open(page, `${OLD_WEB}/appointments`, 'button:text-is("Nouveau")')
  const statusOnLoad = req.since(m0, '/clinics/user-status')
  info('C-0', `old code: /appointments load → ${statusOnLoad} × user-status, ${req.since(m0, '/procedure-types')} × procedure-types`)
  const m1 = req.mark()
  try {
    await openDialogAndPicker(page); await closeDialog(page)
    await openDialogAndPicker(page); await closeDialog(page)
    info('C-0', `old code: two dialog opens → ${req.since(m1, '/procedure-types')} × procedure-types`)
  } catch (e) { info('C-0', `old code: dialog half not measured (${e.message.split('\n')[0]})`) }
  await ctx.close()
} catch (e) { skip('C-0', `baseline unavailable: ${e.message.split('\n')[0]}`) }

// Context A — doctor on the new code
const ctxA = await browser.newContext({ viewport, locale: 'fr-FR' })
const pageA = await ctxA.newPage()
const reqA = countRequests(pageA)
try {
  await signIn(pageA, NEW_WEB, DOCTOR)
  const m = reqA.mark()
  await open(pageA, `${NEW_WEB}/appointments`, 'button:text-is("Nouveau")')
  const n = reqA.since(m, '/clinics/user-status')
  if (n === 1) ok('C-1', '/appointments load → 1 × user-status')
  else bad('C-1', '/appointments user-status count', `n=${n}`)
} catch (e) { bad('C-1', 'threw', e.message) }

try {
  const m = reqA.mark()
  const first = await openDialogAndPicker(pageA); await closeDialog(pageA)
  const second = await openDialogAndPicker(pageA)
  await pageA.screenshot({ path: join(SHOTS, 'C-2.png') })
  await closeDialog(pageA)
  const n = reqA.since(m, '/procedure-types')
  if (first > 0 && second > 0 && n <= 1) ok('C-2', `picker listed ${first} then ${second} acts; ${n} × procedure-types over two opens`)
  else bad('C-2', 'dialog catalogue', JSON.stringify({ first, second, n }))
} catch (e) { bad('C-2', 'threw', e.message) }

try {
  const m = reqA.mark()
  for (const label of ['Patients', 'Rendez-vous']) {
    await closeOverlays(pageA)
    await pageA.locator(`nav a:has-text("${label}") >> visible=true`).first().click()
    await settle(4000)
  }
  const status = reqA.since(m, '/clinics/user-status'), unread = reqA.since(m, '/notifications/unread-count')
  if (status === 0 && unread === 0) ok('C-3', 'two sidebar navigations → 0 × user-status, 0 × unread-count')
  else bad('C-3', 'navigation re-read', JSON.stringify({ status, unread }))
} catch (e) { bad('C-3', 'threw', e.message) }

try {
  await open(pageA, `${NEW_WEB}/patients/${PATIENT}`, 'text=Emna')
  const patientText = await pageA.locator('main').innerText()
  await open(pageA, `${NEW_WEB}/treatment-plans/${PLAN}`, 'main')
  const planText = await pageA.locator('main').innerText()
  const failures = [patientText, planText].filter(t => /n'a pas pu être chargé|ne peut pas être chargé/i.test(t))
  if (failures.length === 0) ok('C-7', 'patient page and devis workspace: no load-failure notice')
  else bad('C-7', 'load-failure notice shown', failures.map(t => t.match(/.{0,60}n'a pas pu être chargé.{0,40}/i)?.[0]).join(' | '))
} catch (e) { bad('C-7', 'threw', e.message) }

// C-4 / C-5 — live updates reach another user's open screens
const token = await adminToken()
const ctxB = await browser.newContext({ viewport, locale: 'fr-FR' })
const pageB = await ctxB.newPage()
let actId = null, fileId = null
try {
  await signIn(pageB, NEW_WEB, SECRETARY)
  await open(pageB, `${NEW_WEB}/appointments`, 'button:text-is("Nouveau")')
  await openDialogAndPicker(pageB)
  const name = `QA-2b acte ${Date.now()}`
  const existing = await unwrap(await api(token, '/procedure-types?includeInactive=false'))
  const colorHex = (existing.items ?? existing)[0].colorHex
  const created = await api(token, '/procedure-types', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name, defaultDurationMinutes: 30, defaultCost: 10, colorHex }) })
  actId = (await unwrap(created))?.id ?? null
  await pageB.locator('[cmdk-input] >> visible=true').first().fill(name.slice(0, 14))
  const appeared = await pageB.locator(`[cmdk-item]:has-text("${name}") >> visible=true`).first().waitFor({ timeout: 10000 }).then(() => true, () => false)
  await pageB.screenshot({ path: join(SHOTS, 'C-4.png') })
  let gone = null
  if (actId) {
    await api(token, `/procedure-types/${actId}`, { method: 'DELETE' })
    await settle(3000)
    gone = await pageB.locator(`[cmdk-item]:has-text("${name}")`).count() === 0
  }
  if (created.ok && appeared && gone) ok('C-4', 'new act offered in the other user\'s open picker without a reload, and gone after the delete')
  else bad('C-4', 'live catalogue', JSON.stringify({ status: created.status, actId, appeared, gone }))
  await closeDialog(pageB)
} catch (e) { bad('C-4', 'threw', e.message) }

try {
  await open(pageB, `${NEW_WEB}/patients/${PATIENT}?tab=files`, 'text=Emna')
  const fname = `qa-2b-${Date.now()}.png`
  const form = new FormData()
  form.append('File', new Blob([PNG], { type: 'image/png' }), fname)
  const up = await api(token, `/patients/${PATIENT}/files/upload`, { method: 'POST', body: form })
  fileId = (await unwrap(up))?.id ?? null
  const appeared = await pageB.locator(`text=${fname} >> visible=true`).first().waitFor({ timeout: 10000 }).then(() => true, () => false)
  await pageB.screenshot({ path: join(SHOTS, 'C-5.png') })
  let gone = null
  if (fileId) {
    await api(token, `/patients/${PATIENT}/files/${fileId}`, { method: 'DELETE' })
    gone = await pageB.locator(`text=${fname}`).first().waitFor({ state: 'detached', timeout: 10000 }).then(() => true, () => false)
  }
  if (up.ok && appeared && gone) ok('C-5', 'uploaded file appeared on the other user\'s Fichiers tab without a reload, and left it after the delete')
  else bad('C-5', 'live file list', JSON.stringify({ status: up.status, fileId, appeared, gone }))
} catch (e) { bad('C-5', 'threw', e.message) }
await ctxB.close()

// C-6 — another user in the same tab reads nothing cached for the first
try {
  await pageA.evaluate(() => fetch('/bff/auth/local-logout', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' }))
  await signIn(pageA, NEW_WEB, SECRETARY)
  const m = reqA.mark()
  await open(pageA, `${NEW_WEB}/appointments`, 'button:text-is("Nouveau")')
  const n = reqA.since(m, '/clinics/user-status')
  const factures = await pageA.locator('nav a:has-text("Factures") >> visible=true').count()
  if (n >= 1 && factures === 0) ok('C-6', `secretary in the same tab: ${n} × user-status read for her, no « Factures » in the rail`)
  else bad('C-6', 'user switch', JSON.stringify({ n, factures }))
} catch (e) { bad('C-6', 'threw', e.message) }

skip('C-8', '« Mode discret » is clinic-wide on a shared database with a live peer; its state machine is unchanged')
skip('C-9', 'a new cabinet right after /setup needs a fresh account; hasClinic:false has staleTime 0 by construction')

await browser.close()
console.log(`  (test act ${actId}, test file ${fileId} — created and deleted by the walk)`)
const real = findings.filter(f => f.m)
console.log(real.length ? `\nFINDINGS:\n${real.map(f => JSON.stringify(f)).join('\n')}` : '\nALL CLEAR (C-8, C-9 not exercised)')
