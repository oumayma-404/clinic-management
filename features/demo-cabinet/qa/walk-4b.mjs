// QA walk — demo-cabinet story 4 part B (treatments + money). Run: node walk-4b.mjs <playwright-core> <shots>
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

const browser = await chromium.launch({ channel: 'chrome' })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
const page = await ctx.newPage()
const writes = []
page.on('request', (r) => { const u = new URL(r.url()); if (u.pathname.includes('/api/') && !['GET', 'OPTIONS'].includes(r.method())) writes.push(`${r.method()} ${u.pathname}`) })
await page.goto(`${BASE}/demo`)
await page.getByRole('button', { name: 'Entrer dans la démo' }).click()
await page.waitForURL((u) => !u.pathname.startsWith('/demo'), { timeout: 30000 })
await page.getByRole('button', { name: 'Mon compte' }).waitFor({ timeout: 45000 })

// Each screen: wait for a word that only real data puts there, then read the page text.
const screens = [
  { id: 'T1', path: '/treatment-plans', expect: ['Pete Becker', 'Ross Geller', 'Leonard Green', 'Mike Hannigan', 'Jill Green'], shot: 'S4B-traitements' },
  { id: 'M1', path: '/a-cloturer', tab: /Reste à payer/, expect: ['Eddie Menuek', 'Pete Becker'], shot: 'S4B-reste-a-payer' },
  { id: 'M2', path: '/cheques', expect: ['Chèques détenus (1)'], shot: 'S4B-cheques' },
  { id: 'M3', path: '/caisse', expect: ['Loyer'], shot: 'S4B-caisse' },
  { id: 'M4', path: '/factures', expect: ['Avoir', '2026-'], shot: 'S4B-factures', loose: true },
]

for (const s of screens) {
  try {
    await page.goto(`${BASE}${s.path}`)
    await page.waitForTimeout(3500)
    if (s.tab) { await page.getByRole('tab', { name: s.tab }).first().click(); await page.waitForTimeout(2500) }
    const text = await page.locator('main').innerText()
    const missing = s.expect.filter((w) => !text.includes(w))
    await page.screenshot({ path: `${SHOTS}/${s.shot}-1440.png` })
    if (missing.length === 0) ok(s.id, `${s.path}: ${s.expect.join(', ')}`)
    else if (s.loose && missing.length < s.expect.length) ok(s.id, `${s.path}: found ${s.expect.filter((w) => !missing.includes(w)).join(', ')} (not: ${missing.join(', ')})`)
    else bad(s.id, `${s.path} missing ${missing.join(', ')}`, text.slice(0, 200).replace(/\s+/g, ' '))
  } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]) }
}

const unexpected = writes.filter((w) => !/auth\//.test(w))
unexpected.length === 0 ? ok('B1', 'no writes beyond sign-in') : bad('B1', 'unexpected writes', JSON.stringify([...new Set(unexpected)]))
await browser.close()
console.log('\n' + (findings.length ? `RED (${findings.length})` : 'GREEN'))
findings.forEach((f) => console.log(JSON.stringify(f)))
