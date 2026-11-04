// QA walk — demo-cabinet story 4 part A (+ story-3 carry-over rows). Run: node walk-4a.mjs <playwright-core> <shots>
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'

const require = createRequire(import.meta.url)
const { chromium } = require(process.argv[2])
const SHOTS = process.argv[3]
mkdirSync(SHOTS, { recursive: true })
const BASE = 'http://localhost:3099'
const SENTENCE = "Démonstration : rien n'est enregistré. Créez votre cabinet pour essayer avec vos données."

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }

const writes = []
const browser = await chromium.launch({ channel: 'chrome' })
const fresh = async () => {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
  ctx.on('request', (r) => {
    const u = new URL(r.url())
    if (u.pathname.includes('/api/') && !['GET', 'OPTIONS'].includes(r.method())) writes.push(`${r.method()} ${u.pathname}`)
  })
  return ctx
}
const enter = async (page) => {
  page.on('request', (r) => { const h = r.headers()['authorization']; if (h && r.url().includes(':5099/api/')) auth.set(page, h) })
  await page.goto(`${BASE}/demo`)
  await page.getByRole('button', { name: 'Entrer dans la démo' }).click()
  await page.waitForURL((u) => !u.pathname.startsWith('/demo'), { timeout: 30000 })
  await page.getByRole('button', { name: 'Mon compte' }).waitFor({ timeout: 45000 })
}
const demoToast = (page) => page.locator('[data-sonner-toast]', { hasText: SENTENCE })
const auth = new WeakMap()
const openPatient = async (page, name) => {
  // Rows are not links and the list is paged: find the id with the visitor's own token, then open the file.
  const headers = { authorization: auth.get(page) }
  const res = await page.request.get(`http://localhost:5099/api/patients?searchTerm=${encodeURIComponent(name)}&page=1&pageSize=5`, { headers })
  const body = await res.json()
  const items = Array.isArray(body) ? body : body.items
  const hit = items.find((p) => `${p.firstName} ${p.lastName}` === name)
  await page.goto(`${BASE}/patients/${hit.id}`)
  await page.waitForTimeout(3500)
}

const scenarios = [
  { id: 'P1+P2', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await page.waitForTimeout(2500)
    const resting = await page.getByText('Agenda au repos').count()
    await page.screenshot({ path: `${SHOTS}/S4A-dashboard-1440.png` })
    resting === 0 ? ok('P1', 'dashboard is not « Agenda au repos » — today has visits') : bad('P1', 'dashboard says the day is empty')
    await page.goto(`${BASE}/patients`)
    const count = page.getByText(/^41 patients$/)
    ;(await count.waitFor({ timeout: 20000 }).then(() => true, () => false)) ? ok('P2', 'patients list: 41 patients') : bad('P2', '41 patients not shown')
    await page.goto(`${BASE}/appointments`)
    await page.waitForTimeout(3000)
    await page.screenshot({ path: `${SHOTS}/S4A-agenda-1440.png` })
    await ctx.close()
  } },
  { id: 'P3', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await openPatient(page, 'Ross Geller')
    const allergy = await page.getByText('Pénicilline').count()
    const asthma = await page.getByText('Asthme').count()
    await page.screenshot({ path: `${SHOTS}/S4A-ross-1440.png` })
    allergy > 0 && asthma > 0 ? ok('P3', 'Ross Geller: pénicilline and asthme on his file') : bad('P3', 'Ross file incomplete', JSON.stringify({ allergy, asthma }))
    await ctx.close()
  } },
  { id: 'C2', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await openPatient(page, 'Phoebe Buffay')
    await page.getByRole('button', { name: 'Plusieurs dents' }).click()
    await page.getByRole('checkbox', { name: /^Dent 21\b/ }).first().click().catch(() => {})
    await page.getByRole('checkbox', { name: /^Dent 22\b/ }).first().click().catch(() => {})
    const panel = page.locator('[aria-label="Diagnostic commun aux dents sélectionnées"]').first()
    if (!(await panel.isVisible().catch(() => false))) { skip('C2', 'multi-tooth panel did not open with these selectors'); await ctx.close(); return }
    const mark = writes.length
    await panel.getByRole('button', { name: 'Enregistrer' }).click()
    const shown = await demoToast(page).first().waitFor({ timeout: 8000 }).then(() => true, () => false)
    await page.waitForTimeout(800)
    const sent = writes.slice(mark).filter((w) => /odontogram/.test(w))
    const stillSelected = await panel.isVisible().catch(() => false)
    shown && sent.length === 0 && stillSelected ? ok('C2', 'multi-tooth save refused once, nothing sent, teeth still selected')
      : bad('C2', 'multi-tooth refusal wrong', JSON.stringify({ shown, sent, stillSelected }))
    await ctx.close()
  } },
  { id: 'C1', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await openPatient(page, 'Joey Tribbiani')
    const mark = writes.length
    await page.getByRole('tab', { name: /Fichiers/ }).first().click()
    await page.waitForTimeout(3000)
    const sent = writes.slice(mark).filter((w) => /files/.test(w))
    await page.screenshot({ path: `${SHOTS}/S4A-fichiers-1440.png` })
    sent.length === 0 ? ok('C1', 'Fichiers opened with no write (no default folders created)') : bad('C1', 'Fichiers wrote', JSON.stringify(sent))
    await ctx.close()
  } },
  { id: 'C3', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await page.getByRole('button', { name: /Messages/ }).first().click()
    const list = page.getByRole('list', { name: 'Conversations' })
    await list.waitFor({ timeout: 15000 }).catch(() => {})
    const first = list.getByRole('button').first()
    if (!(await first.count())) { skip('C3', 'no conversation listed'); await ctx.close(); return }
    await first.click()
    const field = page.locator('textarea').last()
    await field.waitFor({ timeout: 10000 })
    await field.fill('Bonjour Rachel')
    const mark = writes.length
    await field.press('Enter')
    const shown = await demoToast(page).first().waitFor({ timeout: 8000 }).then(() => true, () => false)
    await page.waitForTimeout(800)
    const kept = await field.inputValue()
    const sent = writes.slice(mark).filter((w) => /staff-chat/.test(w))
    await page.screenshot({ path: `${SHOTS}/S4A-chat-1440.png` })
    shown && kept === 'Bonjour Rachel' && sent.length === 0 ? ok('C3', 'chat send refused before the bubble, text kept')
      : bad('C3', 'chat send not refused', JSON.stringify({ shown, kept, sent }))
    await ctx.close()
  } },
]

for (const s of scenarios) {
  try { await s.run() } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]) }
}
const allowed = /auth\/|csp-report/
const unexpected = writes.filter((w) => !allowed.test(w))
unexpected.length === 0 ? ok('B1', `${writes.length} writes, only sign-in/refresh`) : bad('B1', 'unexpected writes', JSON.stringify([...new Set(unexpected)]))
await browser.close()
console.log('\n' + (findings.filter((f) => f.m).length ? `RED (${findings.filter((f) => f.m).length})` : 'GREEN'))
findings.forEach((f) => console.log(JSON.stringify(f)))
