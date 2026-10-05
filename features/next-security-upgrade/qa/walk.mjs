// Smoke walk for plan.md (S-1..S-8) against the standalone production build. Usage: node walk.mjs <scratchpad>
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const [SCRATCH] = process.argv.slice(2)
const { chromium } = createRequire(join(SCRATCH, 'package.json'))('playwright-core')
const HERE = dirname(fileURLToPath(import.meta.url))
const SHOTS = join(HERE, 'shots'); mkdirSync(SHOTS, { recursive: true })

const WEB = 'http://localhost:3099'
const PATIENT = 'd3220000-0000-4000-8000-000000000003'
const INVOICE = '192ce18d-92a9-4e6a-a114-9bc621a486bd'
const DOCTOR = { email: 'qa.doctor@ibnkhaldoun.test', password: 'QaAudit2026!y' }

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }
const settle = ms => new Promise(r => setTimeout(r, ms))
async function closeOverlays(page) {
  for (let i = 0; i < 3; i++) {
    if (await page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible').count() === 0) return
    await page.keyboard.press('Escape'); await settle(500)
  }
}
async function open(page, url, marker) {
  const resp = await page.goto(url, { waitUntil: 'domcontentloaded' })
  await page.locator(`${marker} >> visible=true`).first().waitFor({ timeout: 90000 })
  await settle(3000)
  await closeOverlays(page)
  return resp
}
const boundary = page => page.locator('text=/Une erreur est survenue|Réessayer la page|Application error/i').count()

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
const page = await ctx.newPage()
const errors = []
page.on('pageerror', e => errors.push(e.message))

try {
  await page.goto(`${WEB}/patients`, { waitUntil: 'domcontentloaded' })
  const path = new URL(page.url()).pathname
  if (path === '/login') ok('S-1', 'signed out: /patients redirected to /login by the proxy')
  else bad('S-1', 'no redirect', path)
} catch (e) { bad('S-1', 'threw', e.message) }

try {
  await page.goto(`${WEB}/login`, { waitUntil: 'domcontentloaded' })
  await page.fill('input[type=email]', DOCTOR.email)
  await page.fill('input[type=password]', DOCTOR.password)
  await page.click("button:has-text('Se connecter')")
  await page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 60000 })
  const cookies = (await ctx.cookies()).map(c => c.name)
  if (cookies.some(n => /session/i.test(n))) ok('S-2', `signed in → ${new URL(page.url()).pathname}; cookies: ${cookies.join(', ')}`)
  else bad('S-2', 'no session cookie', cookies.join(', '))
} catch (e) { bad('S-2', 'threw', e.message) }

try {
  await open(page, `${WEB}/`, 'text=L’activité')
  await page.screenshot({ path: join(SHOTS, 'S-3.png') })
  if (await boundary(page) === 0) ok('S-3', 'dashboard rendered « L\'activité »')
  else bad('S-3', 'error boundary on the dashboard')
} catch (e) { bad('S-3', 'threw', e.message) }

try {
  await open(page, `${WEB}/appointments`, 'button:text-is("Nouveau")')
  await page.locator('button:text-is("Nouveau") >> visible=true').first().click()
  const trigger = page.locator("[role='dialog'] button:has-text(\"Sélectionner un type d'acte\") >> visible=true").first()
  await trigger.waitFor({ timeout: 15000 }); await settle(500); await trigger.click()
  await page.locator('[cmdk-item] >> visible=true').first().waitFor({ timeout: 15000 })
  const acts = await page.locator('[cmdk-item] >> visible=true').count()
  await page.screenshot({ path: join(SHOTS, 'S-4.png') })
  await page.keyboard.press('Escape'); await settle(300); await page.keyboard.press('Escape'); await settle(500)
  if (acts > 0) ok('S-4', `agenda dialog opened, picker listed ${acts} acts`)
  else bad('S-4', 'picker empty')
} catch (e) { bad('S-4', 'threw', e.message) }

try {
  await open(page, `${WEB}/patients/${PATIENT}`, 'text=Odontogramme')
  const pageOk = await boundary(page) === 0
  await open(page, `${WEB}/patients/${PATIENT}/files`, 'text=coupe-jpeg-2000.dcm')
  for (let i = 0; i < 8; i++) { await page.mouse.wheel(0, 600); await settle(250) }
  await settle(2500)
  const t = await page.$$eval('img[src^="blob:"]', imgs => ({ total: imgs.length, painted: imgs.filter(i => i.complete && i.naturalWidth > 0).length }))
  await page.screenshot({ path: join(SHOTS, 'S-5.png') })
  if (pageOk && t.total > 0 && t.painted === t.total) ok('S-5', `patient page rendered; file manager painted ${t.painted}/${t.total} thumbnails`)
  else bad('S-5', 'patient / files', JSON.stringify({ pageOk, ...t }))
} catch (e) { bad('S-5', 'threw', e.message) }

try {
  const r = await page.evaluate(async (id) => {
    const res = await fetch(`/bff/pdf/invoice/${id}/note.pdf`, { credentials: 'include' })
    const buf = new Uint8Array(await res.arrayBuffer())
    return { status: res.status, type: res.headers.get('content-type'), head: String.fromCharCode(...buf.slice(0, 5)), size: buf.length }
  }, INVOICE)
  if (r.status === 200 && /pdf/.test(r.type ?? '') && r.head === '%PDF-') ok('S-6', `PDF through the BFF: ${r.size} bytes`)
  else bad('S-6', 'PDF route', JSON.stringify(r))
} catch (e) { bad('S-6', 'threw', e.message) }

try {
  const resp = await page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' })
  const h = resp.headers()
  // Under AUTH_MODE=local Next emits NO security headers on purpose: the API front door / Caddy own them, and a second
  // CSP header makes the browser enforce the intersection (next.config.ts). The upgrade must not start emitting them.
  const csp = h['content-security-policy'] || h['content-security-policy-report-only']
  if (!csp && !h['x-content-type-options']) ok('S-7', 'Next still emits no security headers of its own under AUTH_MODE=local (no duplicate CSP)')
  else bad('S-7', 'Next now emits security headers itself', JSON.stringify({ csp: Boolean(csp), nosniff: h['x-content-type-options'] }))
} catch (e) { bad('S-7', 'threw', e.message) }

try {
  await page.evaluate(() => fetch('/bff/auth/local-logout', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' }))
  // The app notices the ended session and starts its own redirect; a goto racing it is aborted, so let it land first.
  await settle(3000)
  await page.goto(`${WEB}/patients`, { waitUntil: 'domcontentloaded' }).catch(() => page.waitForURL(/\/login/, { timeout: 15000 }))
  await settle(1000)
  const path = new URL(page.url()).pathname
  if (path === '/login') ok('S-8', 'signed out: protected route redirects to /login again')
  else bad('S-8', 'still in after sign-out', path)
} catch (e) { bad('S-8', 'threw', e.message) }

skip('S-9', 'console runtime needs a console account with TOTP and its own listener; tsc + next build are its gate')
if (errors.length) bad('runtime', `${errors.length} uncaught page error(s)`, errors.slice(0, 3).join(' | '))
else ok('runtime', 'no uncaught page error during the walk')

await browser.close()
const real = findings.filter(f => f.m)
console.log(real.length ? `\nFINDINGS:\n${real.map(f => JSON.stringify(f)).join('\n')}` : '\nALL CLEAR (S-9 not exercised)')
