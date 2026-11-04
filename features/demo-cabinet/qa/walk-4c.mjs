// QA walk — demo-cabinet story 4 part C (documents, files, stock, lab, worklists). Run: node walk-4c.mjs <playwright-core> <shots>
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
const auth = { h: null }
page.on('request', (r) => { const h = r.headers()['authorization']; if (h && r.url().includes(':5099/api/')) auth.h = h })
await page.goto(`${BASE}/demo`)
await page.getByRole('button', { name: 'Entrer dans la démo' }).click()
await page.waitForURL((u) => !u.pathname.startsWith('/demo'), { timeout: 30000 })
await page.getByRole('button', { name: 'Mon compte' }).waitFor({ timeout: 45000 })

// Each screen: poll until every word only real data puts there is painted, then shoot it.
const screens = [
  // /documents is the templates page; a cabinet's documents are read on each patient's own file (C7–C9).
  { id: 'C2', path: '/fichiers', chip: 'Avec fichiers', expect: ['Geller Ross', 'Green Leonard', 'Green Jill'], shot: 'S4C-fichiers', narrow: true },
  { id: 'C3', path: '/stock', expect: ['Gants nitrile taille M', 'Articaïne'], shot: 'S4C-stock' },
  { id: 'C4', path: '/fournisseurs', expect: ['Dentalex Tunisie', 'Laboratoire Dentalab'], shot: 'S4C-fournisseurs' },
  { id: 'C5', path: '/lab-orders', expect: ['Ross Geller', 'Jack Geller'], shot: 'S4C-laboratoire' },
  { id: 'C6', path: '/a-cloturer', tab: 'Suites à planifier', expect: ['Gunther Centralperk'], shot: 'S4C-suites' },
]

async function textHas(words, ms = 20000) {
  const end = Date.now() + ms
  let text = ''
  while (Date.now() < end) {
    text = await page.locator('main').innerText().catch(() => '')
    if (words.every((w) => text.includes(w))) return { all: true, text }
    await page.waitForTimeout(500)
  }
  return { all: false, text }
}

for (const s of screens) {
  try {
    await page.setViewportSize({ width: 1440, height: 900 })
    await page.goto(`${BASE}${s.path}`)
    if (s.tab) { await page.getByRole('tab', { name: s.tab }).first().click() }
    if (s.chip) { await page.getByRole('button', { name: s.chip }).first().click() }
    const { all, text } = await textHas(s.expect)
    await page.screenshot({ path: `${SHOTS}/${s.shot}-1440.png` })
    if (all) ok(s.id, `${s.path}: ${s.expect.join(', ')}`)
    else bad(s.id, `${s.path} missing ${s.expect.filter((w) => !text.includes(w)).join(', ')}`, text.slice(0, 240).replace(/\s+/g, ' '))
    if (s.narrow) {
      await page.setViewportSize({ width: 390, height: 844 })
      await page.waitForTimeout(800)
      await page.screenshot({ path: `${SHOTS}/${s.shot}-390.png` })
    }
  } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]) }
}

// Each patient's file: the fiche's ordonnance + demande d'examens, a certificat, an arrêt, the panoramique.
async function openPatient(name) {
  const res = await page.request.get(`http://localhost:5099/api/patients?searchTerm=${encodeURIComponent(name)}&page=1&pageSize=5`, { headers: { authorization: auth.h } })
  const body = await res.json()
  const hit = (Array.isArray(body) ? body : body.items).find((p) => `${p.firstName} ${p.lastName}` === name)
  await page.goto(`${BASE}/patients/${hit.id}`)
}
const files = [
  { id: 'C7', patient: 'Ross Geller', tab: 'Documents', value: 'documents', expect: [/ordonnance/i, /examens/i], shot: 'S4C-ross-documents' },
  { id: 'C8', patient: 'Joey Tribbiani', tab: 'Documents', value: 'documents', expect: [/certificat/i], shot: 'S4C-joey-documents' },
  { id: 'C9', patient: 'Frank Buffay', tab: 'Documents', value: 'documents', expect: [/arr[êe]t de travail/i], shot: 'S4C-frank-documents' },
  { id: 'C10', patient: 'Ross Geller', tab: 'Fichiers', value: 'files', expect: [/Panoramique/], shot: 'S4C-ross-fichiers' },
]
await page.setViewportSize({ width: 1440, height: 900 })
for (const s of files) {
  try {
    await openPatient(s.patient)
    await page.getByRole('tab', { name: s.tab }).first().click()
    const end = Date.now() + 20000
    let panel = ''
    while (Date.now() < end) {
      panel = await page.locator(`[role="tabpanel"][id$="-content-${s.value}"]`).innerText().catch(() => '')
      if (s.expect.every((r) => r.test(panel))) break
      await page.waitForTimeout(500)
    }
    await page.screenshot({ path: `${SHOTS}/${s.shot}-1440.png` })
    s.expect.every((r) => r.test(panel)) ? ok(s.id, `${s.patient} › ${s.tab}: ${s.expect.join(', ')}`)
      : bad(s.id, `${s.patient} › ${s.tab} missing ${s.expect.filter((r) => !r.test(panel)).join(', ')}`, panel.slice(0, 240).replace(/\s+/g, ' '))
  } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]) }
}

// [AC-2.1] the rest of the side menu: each opens with real content (Rappels keeps « Bientôt disponible »).
const NAMES = /Geller|Green|Buffay|Bing|Tribbiani|Becker|Hannigan|Menuek|Centralperk|Bergin/
const sweep = [
  { id: 'S1', path: '/', expect: [/Encaiss/] },
  { id: 'S2', path: '/appointments', expect: [NAMES] },
  { id: 'S3', path: '/a-cloturer', expect: [NAMES] },
  { id: 'S4', path: '/waiting-list', expect: [/Pete Becker|Becker Pete/] },
  { id: 'S5', path: '/patients', expect: [NAMES] },
  { id: 'S6', path: '/factures', expect: [/2026-\d{4}|\d{4}-\d{4}/] },
  { id: 'S7', path: '/rappels', expect: [/Bientôt disponible/] },
  { id: 'S8', path: '/procedure-types', expect: [/Détartrage/] },
  { id: 'S9', path: '/medications', expect: [/Paracétamol/] },
  { id: 'S10', path: '/users', expect: [/Rachel Green/, /Chandler Bing/] },
  { id: 'S11', path: '/journal', expect: [/rachel.green|monica.geller|chandler.bing/i] }, // the journal names an account by its e-mail
  { id: 'S12', path: '/settings', expect: [/Cabinet Dentaire Démo/], inputs: true }, // the name sits in an input's value
]
for (const s of sweep) {
  try {
    await page.goto(`${BASE}${s.path}`)
    const end = Date.now() + 20000
    let text = ''
    while (Date.now() < end) {
      text = await page.locator('main').innerText().catch(() => '')
      if (s.inputs) text += ' ' + (await page.locator('main input').evaluateAll((els) => els.map((e) => e.value).join(' ')).catch(() => ''))
      if (s.expect.every((r) => r.test(text))) break
      await page.waitForTimeout(500)
    }
    await page.screenshot({ path: `${SHOTS}/S4C-sweep-${s.id}-1440.png` })
    s.expect.every((r) => r.test(text)) ? ok(s.id, `${s.path}: ${s.expect.join(', ')}`)
      : bad(s.id, `${s.path} missing ${s.expect.filter((r) => !r.test(text)).join(', ')}`, text.slice(0, 200).replace(/\s+/g, ' '))
  } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]) }
}

const unexpected = writes.filter((w) => !/auth\//.test(w))
unexpected.length === 0 ? ok('B1', 'no writes beyond sign-in') : bad('B1', 'unexpected writes', JSON.stringify([...new Set(unexpected)]))
await browser.close()
console.log('\n' + (findings.length ? `RED (${findings.length})` : 'GREEN'))
findings.forEach((f) => console.log(JSON.stringify(f)))
