#!/usr/bin/env node
/* ═══════════════════════════════════════════════════════════════════════════
   capture-phones.mjs — the six phone captures in « Mobile » (src/img/m-*.png).

   Needs the dev stack (web :3000, api :5000) and playwright-core (npm i it in
   a scratch folder, it is not a dependency of this site). 390x844 at 2x,
   fr-FR, Africa/Tunis, a touch device so no scrollbar is painted.

   READ-ONLY. Nothing is written to the app. The dev database is mostly test
   fixtures (« QAH Flex 539732 », « E2E Conflit-… »), so their rows are
   filtered out of the API's JSON inside the browser, a page's count follows
   the rows that are left, the notification bell reads 0, and the Next dev
   badge is hidden. The agenda, the week, « à clôturer » and la caisse are shot
   on 3–4 September 2026, the last days before the fixtures start.

   ⚠️ Look at every capture before shipping it: a name that slipped the filter
   is a real person, or a test, on the public page.

   CAPTURE_EMAIL=… CAPTURE_PASSWORD=… CAPTURE_TOTP_SECRET=… node capture-phones.mjs [names]
   ═══════════════════════════════════════════════════════════════════════════ */
import { chromium } from 'playwright-core'
import crypto from 'node:crypto'
import { mkdirSync } from 'node:fs'

const only = process.argv[2] ? process.argv[2].split(',') : null
const OUT = process.env.CAPTURE_OUT || 'capture-out'; mkdirSync(OUT, { recursive: true })
// The dev QA account (never a real one). Values: ~/.claude/skills/clinic-browser/SKILL.md § The account.
const { CAPTURE_EMAIL: EMAIL, CAPTURE_PASSWORD: PASS, CAPTURE_TOTP_SECRET: SECRET } = process.env
if (!EMAIL || !PASS || !SECRET) { console.error('set CAPTURE_EMAIL, CAPTURE_PASSWORD and CAPTURE_TOTP_SECRET'); process.exit(1) }
const KARIM = 'd48ed6b4-6e1e-40b5-b14a-224b33c6f512'

const totp = () => {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'; let bits = ''
  for (const c of SECRET) bits += A.indexOf(c).toString(2).padStart(5, '0')
  const key = Buffer.from(bits.match(/.{8}/g).map(b => parseInt(b, 2)))
  const ctr = Math.floor(Date.now() / 30000), buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(ctr / 2 ** 32), 0); buf.writeUInt32BE(ctr >>> 0, 4)
  const h = crypto.createHmac('sha1', key).update(buf).digest(), o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1e6).toString().padStart(6, '0')
}

// ── the fixture filter ─────────────────────────────────────────────────────
const JUNK = /\b(QA\w*|E2E|B\d-Test|Test Alpha|Test Patient)\b|QA-|Flex \d|Flow \d|Conflit|Retrait|oumayma|benkhalifa|Auditeur|mtx|mtu|mts|mug|muh/i
const NAMEKEY = /(name|patient|doctor|practitioner|title|label|fullname|firstname|lastname)$/i
const isJunk = (o) => {
  if (!o || typeof o !== 'object' || Array.isArray(o)) return false
  for (const [k, v] of Object.entries(o)) {
    if (typeof v === 'string' && NAMEKEY.test(k) && JUNK.test(v)) return true
    if (v && typeof v === 'object' && !Array.isArray(v) && /patient|doctor|practitioner/i.test(k) && isJunk(v)) return true
  }
  return false
}
const clean = (v) => {
  if (Array.isArray(v)) return v.filter(e => !isJunk(e)).map(clean)
  if (v && typeof v === 'object') {
    const o = {}; for (const [k, x] of Object.entries(v)) o[k] = clean(x)
    // a page's count follows the rows that are left, or « 1–25 sur 615 » tells on the fixtures
    if (Array.isArray(o.items)) { o.items = o.items.slice(0, 8); for (const c of ['totalCount', 'total']) if (typeof o[c] === 'number') o[c] = o.items.length }
    return o
  }
  if (typeof v === 'string') return v.replace(/Dr QA Auditeur|QA Auditeur/g, 'Dr Salma Ben Youssef')
  return v
}

const b = await chromium.launch({ channel: 'chrome' })
const ctx = await b.newContext({
  viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true,
  locale: 'fr-FR', timezoneId: 'Africa/Tunis',
  userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Mobile Safari/537.36',
})
await ctx.addInitScript(() => {
  const css = `nextjs-portal, [data-nextjs-toast], #__next-build-watcher { display: none !important; }
    ::-webkit-scrollbar { display: none !important; } * { scrollbar-width: none !important; }`
  const add = () => { const s = document.createElement('style'); s.textContent = css; document.documentElement.appendChild(s) }
  if (document.documentElement) add(); else document.addEventListener('DOMContentLoaded', add)
})
// ⚠️ Registered FIRST: Playwright runs the most recently registered matching route, so the two
// notification stubs below must come after this catch-all or it swallows them (« 99+ » on the bell).
await ctx.route(/localhost:5000\/api\/(?!auth)/, async (r) => {
  // a first page of 25 is mostly fixtures; ask for enough rows that real ones survive the filter
  const u = new URL(r.request().url())
  if (u.searchParams.has('pageSize')) u.searchParams.set('pageSize', '200')
  const res = await r.fetch({ url: u.toString() })
  const type = res.headers()['content-type'] || ''
  if (!type.includes('json')) return r.fulfill({ response: res })
  let body = await res.text()
  try { body = JSON.stringify(clean(JSON.parse(body))) } catch {}
  return r.fulfill({ response: res, body })
})
await ctx.route('**/api/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
await ctx.route('**/api/notifications/unread-count**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '{"unreadCount":0}' }))
await ctx.route(/localhost:5000\/api\/notifications(\?|$)/, r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))

const p = await ctx.newPage()
// ── sign in, once ──────────────────────────────────────────────────────────
await p.goto('http://localhost:3000/appointments', { waitUntil: 'domcontentloaded' })
await p.waitForURL(/login/, { timeout: 60000 })
await p.fill('input[type=email]', EMAIL)
await p.fill('input[type=password]', PASS)
await p.click('button:has-text("Se connecter")')
await p.waitForSelector('input[inputmode=numeric]', { timeout: 30000 })
await p.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
await p.fill('input[inputmode=numeric]', totp())
await p.waitForURL(u => !/login/.test(u.toString()), { timeout: 30000 })
console.log('signed in')

const settle = async () => {
  await p.waitForLoadState('networkidle').catch(() => {})
  await p.waitForTimeout(2500)
}
const shot = async (name, prep) => {
  if (only && !only.includes(name)) return
  try {
    if (prep) await prep()
    await settle()
    await p.screenshot({ path: `${OUT}/m-${name}.png` })
    console.log('✅', name)
  } catch (e) { console.log('❌', name, e.message.split('\n')[0]) }
}

// ⚠️ The app scrolls its <main>, not the window, so window.scrollBy moves nothing: scroll the nearest
// scrolling ancestor of the visible element whose own text is exactly `txt`.
const scrollTo = (txt, offset) => p.evaluate(({ txt, offset }) => {
  const el = [...document.querySelectorAll('h1,h2,h3,h4,p,span,div,button')]
    .find(e => e.offsetParent && e.childElementCount <= 2 && e.textContent.trim() === txt)
  if (!el) return 'not found'
  let sc = el.parentElement
  while (sc && sc !== document.body) { const s = getComputedStyle(sc); if (/(auto|scroll)/.test(s.overflowY) && sc.scrollHeight > sc.clientHeight) break; sc = sc.parentElement }
  if (!sc || sc === document.body) sc = document.scrollingElement
  const top = sc === document.scrollingElement ? 0 : sc.getBoundingClientRect().top
  sc.scrollTop += el.getBoundingClientRect().top - top - offset
  return 'ok'
}, { txt, offset })

await shot('dashboard', () => p.goto('http://localhost:3000/', { waitUntil: 'domcontentloaded' }))
await shot('odonto', async () => {
  await p.goto(`http://localhost:3000/patients/${KARIM}`, { waitUntil: 'domcontentloaded' }); await settle()
  console.log('  scroll', await scrollTo('Odontogramme', 16))
})
// Every other screen on a day whose rendez-vous and takings are all real patients: the fixtures
// that fill the dev database are dated from 8 September onwards.
await p.clock.setFixedTime(new Date('2026-09-03T08:40:00+01:00'))
await shot('agenda', () => p.goto('http://localhost:3000/appointments', { waitUntil: 'domcontentloaded' }))
await shot('semaine', async () => {
  await p.goto('http://localhost:3000/appointments', { waitUntil: 'domcontentloaded' }); await settle()
  await p.locator('button:has-text("Semaine"), [role=tab]:has-text("Semaine")').first().click()
})
await p.clock.setFixedTime(new Date('2026-09-04T18:30:00+01:00'))
await shot('cloturer', () => p.goto('http://localhost:3000/a-cloturer', { waitUntil: 'domcontentloaded' }))
await shot('caisse', () => p.goto('http://localhost:3000/caisse', { waitUntil: 'domcontentloaded' }))
await b.close()
