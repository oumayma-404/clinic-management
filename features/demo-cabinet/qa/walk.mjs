// QA walk — demo-cabinet story 1. Run: node walk.mjs <path-to-playwright-core> <shots-dir>
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'

const require = createRequire(import.meta.url)
const { chromium } = require(process.argv[2])
const SHOTS = process.argv[3]
mkdirSync(SHOTS, { recursive: true })
const BASE = 'http://localhost:3099'

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }

const browser = await chromium.launch({ channel: 'chrome' })
const fresh = (opts = {}) => browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR', ...opts })
const path = (page) => new URL(page.url()).pathname
const enter = async (page) => {
  await page.goto(`${BASE}/demo`)
  await page.getByRole('button', { name: 'Entrer dans la démo' }).click()
  await page.waitForURL((u) => !u.pathname.startsWith('/demo'), { timeout: 30000 })
  await page.getByRole('button', { name: 'Mon compte' }).waitFor({ timeout: 45000 })
}

const scenarios = [
  { id: 'A1', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await page.goto(`${BASE}/`)
    await page.waitForURL('**/demo**', { timeout: 30000 })
    const h = await page.getByRole('heading', { name: 'Cabinet de démonstration' }).isVisible().catch(() => false)
    const btn = await page.getByRole('button', { name: 'Entrer dans la démo' }).isVisible()
    const link = page.getByRole('link', { name: 'Créer mon cabinet' })
    const href = await link.getAttribute('href'); const target = await link.getAttribute('target')
    await page.screenshot({ path: `${SHOTS}/A1-entry-1440.png` })
    h && btn && href === 'https://app.apexa.tn/signup' && target === '_blank'
      ? ok('A1', `/ → ${path(page)}, heading + button + link ${href} (${target})`)
      : bad('A1', 'entry screen incomplete', JSON.stringify({ url: page.url(), h, btn, href, target }))
    await ctx.close()
  } },
  { id: 'A2+A3+B1', run: async () => {
    const one = await fresh(); const p1 = await one.newPage()
    const sawPassword = []
    p1.on('framenavigated', async () => { if (await p1.locator('input[type="password"]').count().catch(() => 0)) sawPassword.push(p1.url()) })
    await enter(p1)
    await p1.screenshot({ path: `${SHOTS}/A2-dashboard-1440.png` })
    sawPassword.length ? bad('A2', 'a password field appeared', sawPassword.join(',')) : ok('A2', `entered → ${path(p1)}, dashboard header present, no password field`)

    const two = await fresh(); const p2 = await two.newPage()
    await enter(p2)
    await p1.reload(); await p1.getByRole('button', { name: 'Mon compte' }).waitFor({ timeout: 45000 })
    path(p1).startsWith('/demo') ? bad('A3', 'first visitor was signed out by the second') : ok('A3', `two visitors in at once (${path(p1)} / ${path(p2)})`)

    await p2.goto(`${BASE}/demo`)
    await p2.waitForURL((u) => !u.pathname.startsWith('/demo'), { timeout: 15000 }).catch(() => {})
    path(p2) === '/' ? ok('B1', 'signed-in visitor on /demo is sent to /') : bad('B1', 'signed-in visitor stayed on', p2.url())
    await one.close(); await two.close()
  } },
  { id: 'B2', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    for (const door of ['/login', '/signup', '/mot-de-passe-oublie', '/join']) {
      await page.goto(`${BASE}${door}`)
      await page.waitForURL('**/demo**', { timeout: 20000 }).catch(() => {})
      path(page) === '/demo' ? ok('B2', `${door} → /demo`) : bad('B2', `${door} did not reach /demo`, page.url())
    }
    await ctx.close()
  } },
  { id: 'B3', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await page.getByRole('button', { name: 'Mon compte' }).click()
    await page.getByRole('menuitem', { name: /Se déconnecter/ }).click()
    await page.waitForURL('**/demo**', { timeout: 20000 }).catch(() => {})
    const expiredShown = await page.getByText('La démo a expiré').isVisible().catch(() => false)
    path(page) === '/demo' && !expiredShown ? ok('B3', `logout → ${page.url().replace(BASE, '')}, no expiry line`)
      : bad('B3', 'logout landing wrong', JSON.stringify({ url: page.url(), expiredShown }))
    await ctx.close()
  } },
  { id: 'C1', run: async () => {
    for (const [w, h] of [[320, 640], [390, 844], [820, 1180], [1180, 820], [1440, 900], [1536, 730]]) {
      const ctx = await fresh({ viewport: { width: w, height: h } }); const page = await ctx.newPage()
      await page.goto(`${BASE}/demo`)
      await page.getByRole('button', { name: 'Entrer dans la démo' }).waitFor()
      const m = await page.evaluate(() => {
        const r = (sel) => document.querySelector(sel)?.getBoundingClientRect()
        const link = [...document.querySelectorAll('a')].find((a) => a.textContent.includes('Créer mon cabinet'))?.getBoundingClientRect()
        return { sw: document.documentElement.scrollWidth, cw: document.documentElement.clientWidth, linkBottom: link?.bottom, vh: innerHeight }
      })
      await page.screenshot({ path: `${SHOTS}/C1-${w}x${h}.png` })
      const fits = m.sw <= m.cw && m.linkBottom <= m.vh
      fits ? ok('C1', `${w}×${h}: no side scroll, actions above the fold (${Math.round(m.linkBottom)}/${m.vh})`)
        : bad('C1', `${w}×${h} overflow or actions below fold`, JSON.stringify(m))
      await ctx.close()
    }
  } },
  { id: 'C2', run: async () => {
    const ctx = await fresh({ viewport: { width: 320, height: 640 }, hasTouch: true, isMobile: true }); const page = await ctx.newPage()
    await page.goto(`${BASE}/demo`)
    const box = await page.getByRole('button', { name: 'Entrer dans la démo' }).boundingBox()
    box && box.height >= 44 ? ok('C2', `coarse: button ${Math.round(box.height)} px tall`) : bad('C2', 'coarse button under 44 px', JSON.stringify(box))
    await ctx.close()
  } },
  { id: 'C3', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await page.goto(`${BASE}/demo`)
    await page.getByRole('button', { name: 'Entrer dans la démo' }).waitFor()
    let focused = ''
    for (let i = 0; i < 8 && !focused.includes('Entrer dans la démo'); i++) {
      await page.keyboard.press('Tab')
      focused = await page.evaluate(() => document.activeElement?.textContent ?? '')
    }
    if (!focused.includes('Entrer dans la démo')) { bad('C3', 'Tab never reached the button', focused); await ctx.close(); return }
    await page.keyboard.press('Enter')
    await page.waitForURL((u) => !u.pathname.startsWith('/demo'), { timeout: 30000 }).catch(() => {})
    path(page).startsWith('/demo') ? bad('C3', 'Enter did not enter') : ok('C3', `keyboard Tab+Enter → ${path(page)}`)
    await ctx.close()
  } },
  { id: 'C4', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await page.goto(`${BASE}/demo`)
    const title = await page.title()
    const desc = await page.locator('meta[name="description"]').getAttribute('content')
    title === 'APEXA Démo' && desc ? ok('C4', `title « ${title} », description « ${desc.slice(0, 50)}… »`) : bad('C4', 'metadata', JSON.stringify({ title, desc }))
    await ctx.close()
  } },
  { id: 'D1', run: async () => skip('D1', 'no HostedMultiTenant API on the isolated stack; unit tests cover the unchanged non-demo mode') },
]

// B4 runs last: its session must outlive the 2-minute cap set on this API.
const expiring = await fresh(); const ep = await expiring.newPage()
let expiryArmed = false
try { await enter(ep); expiryArmed = true; console.log('  … B4 session opened, waiting out its cap while the rest runs') }
catch (e) { bad('B4', 'could not open the session to expire', e.message) }

const started = Date.now()
for (const s of scenarios) {
  try { await s.run() } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]) }
}

if (expiryArmed) {
  const wait = Math.max(0, 135000 - (Date.now() - started))
  await new Promise((r) => setTimeout(r, wait))
  const toasts = []
  ep.on('console', () => {})
  await ep.reload()
  await ep.waitForURL('**/demo**', { timeout: 45000 }).catch(() => {})
  toasts.push(await ep.getByText('Reconnectez-vous').isVisible().catch(() => false))
  const line = await ep.getByText('La démo a expiré. Entrez à nouveau pour continuer.').waitFor({ timeout: 15000 }).then(() => true, () => false)
  await ep.screenshot({ path: `${SHOTS}/B4-expired-1440.png` })
  ep.url().includes('/demo?expired=1') && line && !toasts[0]
    ? ok('B4', `after the cap: ${ep.url().replace(BASE, '')} with the expiry line, no « Reconnectez-vous » toast`)
    : bad('B4', 'expiry landing wrong', JSON.stringify({ url: ep.url(), line, reconnectToast: toasts[0] }))
}
await expiring.close()
await browser.close()
console.log(findings.length ? `\nFINDINGS (${findings.length}):` : '\nALL CLEAR')
findings.forEach((f) => console.log(JSON.stringify(f)))
