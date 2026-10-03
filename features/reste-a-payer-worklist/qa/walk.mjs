// QA walk — Reste à payer worklist. One launch, every browser row of qa/plan.md.
// Run: node features/reste-a-payer-worklist/qa/walk.mjs <scratchpad-dir-with-token.txt-seed.json-totp.mjs>
import { createRequire } from 'node:module'
import fs from 'node:fs'
import path from 'node:path'
import { execSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..')
const require = createRequire(path.join(ROOT, 'e2e', 'package.json'))
const { chromium } = require('playwright-core')

const SCRATCH = process.argv[2]
const SEED = JSON.parse(fs.readFileSync(path.join(SCRATCH, 'seed.json'), 'utf8'))
const TOKEN = fs.readFileSync(path.join(SCRATCH, 'token.txt'), 'utf8').trim()
const SHOTS = path.join(ROOT, 'features/reste-a-payer-worklist/qa/shots')
fs.mkdirSync(SHOTS, { recursive: true })
const BASE = 'http://localhost:3000'
const SECRET = '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why, skipped: true }) }
const api = async (p) => (await fetch(`http://localhost:5000/api${p}`, { headers: { Authorization: `Bearer ${TOKEN}` } })).json()
const shot = (page, name) => page.screenshot({ path: path.join(SHOTS, `${name}.png`) })
const dt = (n) => n.toLocaleString('fr-FR', { minimumFractionDigits: 3, maximumFractionDigits: 3 }).replace(/ | /g, ' ')
const norm = (s) => (s ?? '').replace(/[  ]/g, ' ').replace(/\s+/g, ' ').trim()

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
await ctx.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const page = await ctx.newPage()

async function login() {
  await page.goto(`${BASE}/appointments`, { waitUntil: 'domcontentloaded' })
  await page.waitForURL(/\/login/, { timeout: 60000 })
  await page.fill('input[type=email]', 'salma.benyoussef@cabinet-ibnkhaldoun.tn')
  await page.fill('input[type=password]', 'QaAudit2026!y')
  await page.click('button:has-text("Se connecter")')
  // A code is single-use per 30 s window on a shared account: on a refusal, try once more on the next window.
  for (let attempt = 0; attempt < 2; attempt++) {
    await page.waitForSelector('input[inputmode=numeric]', { timeout: 30000 })
    await page.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
    const code = execSync(`node "${path.join(SCRATCH, 'totp.mjs')}" ${SECRET}`).toString().trim()
    await page.fill('input[inputmode=numeric]', code)
    const done = await page.waitForURL((u) => !/\/login/.test(u.toString()), { timeout: 20000 }).then(() => true, () => false)
    if (done) return
    console.log('     2FA code refused, retrying on the next window')
  }
  throw new Error('2FA refused twice')
}

async function openTab() {
  await page.goto(`${BASE}/a-cloturer`, { waitUntil: 'domcontentloaded' })
  const tab = page.getByRole('tab', { name: 'Reste à payer' })
  await tab.waitFor({ timeout: 90000 })
  return tab
}


// How many lines a piece of text actually takes — box height says nothing when a pill is fixed-height.
const LINES = `(el) => { const r = document.createRange(); r.selectNodeContents(el); return new Set([...r.getClientRects()].map(x => Math.round(x.top))).size }`

async function tabStripFits(id) {
  const m = await page.locator('[role=tablist]').first().evaluate((l) => { const r = l.getBoundingClientRect(); return { overflow: l.scrollWidth - l.clientWidth, outside: [...l.querySelectorAll('[role=tab]')].filter(t => { const b = t.getBoundingClientRect(); return b.right > r.right + 1 || b.left < r.left - 1 }).map(t => t.getAttribute('aria-label')) } })
  if (m.overflow > 1 || m.outside.length) bad(id, 'tab strip overflows', JSON.stringify(m)); else ok(id, 'all four tabs inside the strip')
}
const cards = () => page.locator('[role=radiogroup][aria-label="Type de reste à payer"] [role=radio]')
const sheet = () => page.locator('[data-slot="sheet-content"]')
async function sheetGeometry() {
  await page.waitForTimeout(700) // the slide-in is 300 ms; measure after it settles
  return sheet().evaluate((el) => {
    const r = el.getBoundingClientRect()
    const over = [...el.querySelectorAll('*')].filter(n => { const b = n.getBoundingClientRect(); return b.width > 0 && (b.right > r.right + 1 || b.left < r.left - 1) }).slice(0, 3).map(n => n.tagName + '.' + (n.className?.toString?.() ?? '').slice(0, 40))
    return { left: Math.round(r.left), right: Math.round(r.right), width: Math.round(r.width), vw: innerWidth, over }
  })
}
async function waitRows() {
  await page.waitForFunction(() => document.querySelectorAll('table tbody tr, [aria-label="Patients à relancer"] li, [aria-label="Patients en cours de traitement"] li').length > 0 || /Personne à relancer|Aucun patient ne correspond|Aucun traitement en cours/.test(document.body.innerText), null, { timeout: 60000 })
}
async function search(term) {
  await page.fill('#reste-a-payer-search', term)
  await page.waitForTimeout(400)
  await page.waitForFunction((t) => {
    const rows = [...document.querySelectorAll('table tbody tr, [aria-label="Patients à relancer"] li, [aria-label="Patients en cours de traitement"] li')].filter(r => r.offsetParent)
    const empty = /Aucun patient ne correspond|Personne à relancer|Aucun traitement en cours/.test(document.body.innerText)
    if (!t) return rows.length > 0
    return (rows.length > 0 && rows.every(r => r.innerText.toLowerCase().includes(t.toLowerCase()))) || empty
  }, term, { timeout: 30000 })
}

const due = await api('/billing/reste-a-payer?list=due')
const noteRest = (await api(`/invoices/${SEED.iid}`)).outstanding
const payNow = noteRest > 200 ? 200 : Math.round(noteRest * 500) / 1000
const afterPay = Math.round((noteRest - payNow) * 1000) / 1000
console.log('     QA note outstanding at start:', noteRest)

const SCENARIOS = [
  { id: 'login', run: async () => { await login(); ok('login', 'signed in') } },

  { id: 'A1', run: async () => {
    const tab = await openTab()
    await page.waitForFunction(() => { const t = [...document.querySelectorAll('[role=tab]')].find(x => x.getAttribute('aria-label') === 'Reste à payer'); return t && /[1-9]/.test(t.innerText) }, null, { timeout: 60000 })
    const badge = norm(await tab.innerText()).match(/[\d\s]+$/)?.[0].replace(/\s/g, '')
    const tabs = await page.getByRole('tab').count()
    if (tabs === 4 && Number(badge) === due.dueCount) ok('A1', `4 tabs, badge ${badge} = dueCount ${due.dueCount}`)
    else bad('A1', 'tab/badge', `tabs=${tabs} badge=${badge} dueCount=${due.dueCount}`)
  } },

  { id: 'A2', run: async () => {
    await page.getByRole('tab', { name: 'Reste à payer' }).click()
    await waitRows()
    const c = cards()
    const t0 = norm(await c.nth(0).innerText()), t1 = norm(await c.nth(1).innerText())
    const sel = await c.nth(0).getAttribute('aria-checked')
    await tabStripFits('A2-tabs')
    await shot(page, 'A2-1440')
    const seg = await page.locator('[role=radiogroup][aria-label="Trier la liste"] [role=radio]').evaluateAll((bs, L) => { const lines = eval(L); return bs.map(lines) }, LINES)
    if (seg.some(n => n > 1)) bad('A2-sort', 'sort control wraps at 1440', JSON.stringify(seg)); else ok('A2-sort', `sort options on ${seg.join('/')} line(s)`)
    const okDue = t0.includes('À relancer') && t0.includes(dt(due.dueTotal)) && t0.includes(`${due.dueCount.toLocaleString('fr-TN').replace(/ | /g, ' ')} patients`)
    const okRun = t1.includes('En cours') && t1.includes(dt(due.runningTotal))
    if (okDue && okRun && sel === 'true') ok('A2', `cards « ${t0} » / « ${t1} »`)
    else bad('A2', 'cards', `due="${t0}" run="${t1}" selected=${sel} expected ${dt(due.dueTotal)} / ${dt(due.runningTotal)}`)
  } },

  { id: 'A3', run: async () => {
    const rows = page.locator('table:visible tbody tr')
    const n = await rows.count()
    const first = norm(await rows.nth(0).innerText())
    const r0 = due.items[0]
    const link = await rows.nth(0).locator('a[href^="/patients/"]').count()
    const hasCross = due.items.slice(0, n).some(r => r.runningAmount > 0)
    const crossShown = hasCross ? (await page.locator('table:visible tbody').innerText()).includes('en cours') : true
    console.log('     row0:', first)
    if (n > 0 && first.includes(r0.patientName) && first.includes(r0.dueReason) && /il y a \d+ jours|aujourd'hui|il y a 1 jour/.test(first) && first.includes(dt(r0.dueAmount)) && link === 1 && crossShown)
      ok('A3', `${n} rows; first row « ${r0.patientName} », reason, age, amount, fiche link; cross amount shown`)
    else bad('A3', 'row content', `n=${n} link=${link} cross=${crossShown} row="${first}"`)
  } },

  { id: 'A5', run: async () => {
    const row = page.locator('table:visible tbody tr').filter({ has: page.locator('a[href^="tel:"]') }).first()
    const tel = await row.locator('a[href^="tel:"]').getAttribute('href')
    const wa = row.locator('a[href^="https://wa.me/"]')
    const waHref = await wa.getAttribute('href'), target = await wa.getAttribute('target')
    if (/^tel:\+216\d{8}$/.test(tel) && /^https:\/\/wa\.me\/216\d{8}$/.test(waHref) && target === '_blank') ok('A5', `${tel} · ${waHref} · _blank`)
    else bad('A5', 'contact links', `tel=${tel} wa=${waHref} target=${target}`)
  } },

  { id: 'A4', run: async () => {
    await cards().nth(1).click()
    await page.waitForFunction(() => [...document.querySelectorAll('table thead th')].some(th => th.offsetParent && th.innerText.toLowerCase().includes('prochaine étape')), null, { timeout: 30000 })
    const body = norm(await page.locator('table:visible tbody').innerText())
    const sel = await cards().nth(1).getAttribute('aria-checked')
    await shot(page, 'A4-1440')
    if (sel === 'true' && /\d+ actes? faits? sur \d+/.test(body) && /Séance le|Échéance le|Aucune séance prévue/.test(body)) ok('A4', 'En cours list: acts-done count + next step')
    else bad('A4', 'running list', `selected=${sel} body="${body.slice(0, 200)}"`)
  } },

  { id: 'reset-due', run: async () => {
    await cards().nth(0).click()
    await page.waitForFunction(() => [...document.querySelectorAll('table thead th')].some(th => th.offsetParent && th.innerText.toLowerCase().includes('depuis')), null, { timeout: 30000 })
  } },

  { id: 'B1', run: async () => {
    await search('zzzznope')
    const txt = norm(await page.locator('main').innerText())
    const okEmpty = txt.includes('Aucun patient ne correspond à « zzzznope »')
    await page.click('button:has-text("Effacer la recherche")')
    await page.waitForFunction(() => [...document.querySelectorAll('table tbody tr')].filter(r => r.offsetParent).length > 0, null, { timeout: 30000 }).catch(() => {})
    const n = await page.locator('table:visible tbody tr').count()
    if (okEmpty && n > 0) ok('B1', `filtered empty state, cleared back to ${n} rows`)
    else bad('B1', 'search empty state', `empty=${okEmpty} rowsAfterClear=${n}`)
  } },

  { id: 'B2', run: async () => {
    await search('RestePaiement')
    const row = page.locator('table:visible tbody tr').first()
    const text = norm(await row.innerText())
    const contacts = await row.locator('a[href^="tel:"], a[href^="https://wa.me/"]').count()
    if (text.includes('QA RestePaiement') && text.includes(dt(noteRest)) && contacts === 0) ok('B2', `QA patient row, ${dt(noteRest)} DT, no call/WhatsApp control`)
    else bad('B2', 'no-phone row', `contacts=${contacts} text="${text}"`)
  } },

  { id: 'D1', run: async () => {
    await page.goto(`${BASE}/patients/${SEED.pid}?tab=medical-records`, { waitUntil: 'domcontentloaded' })
    const btn = page.locator(`button[aria-label^="Encaisser sur"]:visible`).first()
    await btn.waitFor({ timeout: 90000 })
    await btn.click()
    const dlg = page.getByRole('dialog').filter({ hasText: 'Enregistrer un paiement' })
    await dlg.waitFor({ timeout: 30000 })
    const amount = await dlg.locator('#amount').inputValue()
    await dlg.getByRole('button', { name: 'Annuler' }).click()
    await page.waitForTimeout(500)
    const discard = page.getByRole('alertdialog')
    if (await discard.count()) await discard.getByRole('button').first().click().catch(() => {})
    const stillOpen = await dlg.isVisible().catch(() => false)
    if (norm(amount) === dt(noteRest) && !stillOpen) ok('D1', `patient file Encaisser opens prefilled ${dt(noteRest)}; Annuler closes`)
    else bad('D1', 'patient-file payment dialog', `amount="${amount}" stillOpen=${stillOpen}`)
  } },

  { id: 'A6', run: async () => {
    await openTab(); await page.getByRole('tab', { name: 'Reste à payer' }).click(); await waitRows()
    await search('RestePaiement')
    await page.locator('table:visible tbody tr').first().getByRole('button', { name: /Détail/ }).click()
    await sheet().waitFor({ timeout: 30000 })
    await page.waitForFunction(() => /reste à payer/.test(document.querySelector('[data-slot="sheet-content"]')?.innerText ?? ''), null, { timeout: 30000 })
    const g = await sheetGeometry()
    const t = norm(await sheet().innerText())
    await shot(page, 'A6-1440')
    if (g.right > g.vw + 1 || g.over.length) bad('A6-geometry', 'panel content outside the panel', JSON.stringify(g)); else ok('A6-geometry', `panel ${g.left}–${g.right} of ${g.vw}, nothing outside`)
    const okHead = t.includes('QA RestePaiement') && t.includes('Solde dû') && t.includes('À relancer')
    const okDoc = t.includes(`Note d'honoraires n° ${SEED.number}`) && t.includes('Détartrage QA') && t.includes('Total') && t.includes('Payé') && t.includes(dt(noteRest))
    const enc = await sheet().locator(`button[aria-label="Encaisser sur Note d'honoraires n° ${SEED.number}"]`).count()
    if (okHead && okDoc && enc === 1) ok('A6', 'panel: name, split, À relancer group, note with its act, Total/Payé/reste, Encaisser')
    else bad('A6', 'panel content', `head=${okHead} doc=${okDoc} enc=${enc} text="${t.slice(0, 300)}"`)
  } },

  { id: 'B3', run: async () => {
    await sheet().locator(`button[aria-label="Encaisser sur Note d'honoraires n° ${SEED.number}"]`).click()
    const dlg = page.getByRole('dialog').filter({ hasText: 'Enregistrer un paiement' })
    await dlg.waitFor({ timeout: 30000 })
    const pre = await dlg.locator('#amount').inputValue()
    await dlg.locator('#amount').fill('999')
    await dlg.getByRole('button', { name: 'Enregistrer' }).click()
    await page.waitForTimeout(800)
    const txt = norm(await dlg.innerText())
    if (norm(pre) === dt(noteRest) && /dépasse le reste dû/.test(txt) && await dlg.isVisible()) ok('B3', `prefilled ${pre}; 999 refused « ${txt.match(/Le paiement dépasse[^.]*\.?/)?.[0]} », dialog open`)
    else bad('B3', 'over-payment', `pre=${pre} txt="${txt.slice(0, 200)}"`)
  } },

  { id: 'A7', run: async () => {
    const dlg = page.getByRole('dialog').filter({ hasText: 'Enregistrer un paiement' })
    await dlg.locator('#amount').fill(String(payNow).replace('.', ','))
    await dlg.getByRole('button', { name: 'Enregistrer' }).click()
    await dlg.waitFor({ state: 'hidden', timeout: 30000 })
    await page.waitForFunction((n) => (document.querySelector('[data-slot="sheet-content"]')?.innerText ?? '').replace(/[  ]/g, ' ').includes(n), dt(afterPay), { timeout: 30000 }).catch(() => {})
    const st = norm(await sheet().innerText())
    await shot(page, 'A7-1440')
    await page.keyboard.press('Escape')
    await sheet().waitFor({ state: 'hidden', timeout: 10000 }).catch(() => {})
    await page.waitForFunction((n) => [...document.querySelectorAll('table tbody tr')].some(r => r.offsetParent && r.innerText.replace(/[  ]/g, ' ').includes(n)), dt(afterPay), { timeout: 30000 }).catch(() => {})
    const row = norm(await page.locator('table:visible tbody tr').first().innerText())
    const card = norm(await cards().nth(0).innerText())
    // The search « RestePaiement » is still active, so the card is scoped to that one patient (AC-4).
    const expectedCard = dt((await api('/billing/reste-a-payer?list=due&search=RestePaiement')).dueTotal)
    if (st.includes(dt(afterPay)) && row.includes(dt(afterPay)) && card.includes(expectedCard) && expectedCard === dt(afterPay)) ok('A7', `paid ${payNow}: panel ${dt(afterPay)}, row ${dt(afterPay)}, card ${expectedCard}, no reload`)
    else bad('A7', 'partial payment refresh', `panel=${st.includes(dt(afterPay))} row="${row}" card="${card}" expected card ${expectedCard}`)
  } },

  { id: 'C5', run: async () => {
    await search('Reviser-mtsp3dy00')
    const n = await page.locator('table:visible tbody tr').count()
    if (!n) return skip('C5', 'split-devis patient not in the due list')
    const row = norm(await page.locator('table:visible tbody tr').first().innerText())
    await page.locator('table:visible tbody tr').first().getByRole('button', { name: /Détail/ }).click()
    await sheet().waitFor({ timeout: 30000 })
    await page.waitForFunction(() => /reste à payer/.test(document.querySelector('[data-slot="sheet-content"]')?.innerText ?? ''), null, { timeout: 30000 })
    await sheetGeometry()
    const t = norm(await sheet().innerText())
    await shot(page, 'C5-1440')
    await page.keyboard.press('Escape')
    if (t.includes('À régler maintenant') && t.includes('Échéance du') && row.includes('en cours')) ok('C5', 'split devis: « À régler maintenant » in the panel, both parts on the row')
    else bad('C5', 'split devis', `row="${row}" panel="${t.slice(0, 300)}"`)
  } },

  { id: 'D3', run: async () => {
    await search('')
    const res = []
    for (const [name, rx] of [['Séances à clôturer', /séance|Rien à clôturer/i], ['Patients à compléter', /patient|Aucun/i], ['Suites à planifier', /suite|Planifier la suite/i]]) {
      const tab = page.getByRole('tab', { name })
      await tab.click()
      await page.waitForTimeout(1200)
      const panel = page.locator('[role=tabpanel][data-state=active]')
      const txt = norm(await panel.innerText())
      const failed = /n'a pas pu|Réessayer/.test(txt)
      res.push(`${name}:${rx.test(txt) && !failed ? 'ok' : 'BAD'}`)
    }
    await page.getByRole('tab', { name: 'Reste à payer' }).click()
    if (res.every(r => r.endsWith('ok'))) ok('D3', res.join(' · '))
    else bad('D3', 'other tabs', res.join(' · '))
  } },

  { id: 'C2', run: async () => {
    await page.setViewportSize({ width: 820, height: 1024 })
    await page.waitForTimeout(800)
    const table = await page.locator('table:visible').count()
    const cardsN = await page.locator('[aria-label="Patients à relancer"]:visible li').count()
    const labels = await page.getByRole('tab').evaluateAll(ts => ts.map(t => ({ t: t.innerText.replace(/\s+/g, ' ').trim(), clipped: t.scrollWidth > t.clientWidth + 1 })))
    const sw = await page.evaluate(() => document.documentElement.scrollWidth)
    const overlap = await cards().evaluateAll((cs, L) => { const lines = eval(L); return cs.map(c => { const label = c.querySelector('.font-semibold'), amt = c.querySelector('.tabular-nums'), hint = [...c.querySelectorAll('.text-muted-foreground')].pop(); const rr = (el) => { const r = document.createRange(); r.selectNodeContents(el); return [...r.getClientRects()] }; const a = amt.getBoundingClientRect(); const hit = rr(hint).some(h => h.right > a.left + 1 && h.left < a.right - 1 && h.bottom > a.top + 1 && h.top < a.bottom - 1); return { overlap: hit, labelLines: lines(label), hintLines: lines(hint) } }) }, LINES)
    if (overlap.some(o => o.overlap || o.labelLines > 1 || o.hintLines > 2)) bad('C2-cards', 'list cards cramped at 820', JSON.stringify(overlap)); else ok('C2-cards', 'list cards: no overlap, labels on one line')
    const segLines = await page.locator('[role=radiogroup][aria-label="Trier la liste"] [role=radio]').evaluateAll((bs, L) => { const lines = eval(L); return bs.map(lines) }, LINES)
    if (segLines.some(n => n > 1)) bad('C2-sort', 'sort control wraps', JSON.stringify(segLines)); else ok('C2-sort', `sort options on ${segLines.join('/')} line(s)`)
    await tabStripFits('C2-tabs')
    await shot(page, 'C2-820')
    if (table === 0 && cardsN > 0 && labels.every(l => !l.clipped) && sw <= 820) ok('C2', `cards (${cardsN}), tabs whole: ${labels.map(l => l.t).join(' | ')}`)
    else bad('C2', '820 layout', `table=${table} cards=${cardsN} sw=${sw} labels=${JSON.stringify(labels)}`)
  } },

  { id: 'C1', run: async () => {
    await page.setViewportSize({ width: 320, height: 730 })
    await page.waitForTimeout(800)
    const sw = await page.evaluate(() => document.documentElement.scrollWidth)
    const main = await page.evaluate(() => { const m = document.querySelector('main'); return m ? m.scrollWidth - m.clientWidth : -1 })
    const firstCard = page.locator('[aria-label="Patients à relancer"]:visible li').first()
    const callW = await firstCard.locator('a[href^="tel:"]').evaluateAll(as => as.map(a => Math.round(a.getBoundingClientRect().width)))
    const ov1 = await cards().evaluateAll((cs, L) => { const lines = eval(L); return cs.map(c => lines(c.querySelector('.font-semibold'))) }, LINES)
    if (ov1.some(l => l > 1)) bad('C1-cards', 'list card label wraps at 320', JSON.stringify(ov1)); else ok('C1-cards', 'labels on one line at 320')
    await tabStripFits('C1-tabs')
    await shot(page, 'C1-320')
    if (sw <= 320 && main <= 0) ok('C1', `no horizontal scroll (doc ${sw}, main overflow ${main}); call button widths ${callW.join(',') || 'none on first card'}`)
    else bad('C1', '320 overflow', `docScrollWidth=${sw} mainOverflow=${main}`)
  } },

  { id: 'C3', run: async () => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.waitForTimeout(500)
    const card = page.locator('[aria-label="Patients à relancer"]:visible li').first()
    await card.locator('button').first().click()
    await sheet().waitFor({ timeout: 30000 })
    await page.waitForFunction(() => /reste à payer/.test(document.querySelector('[data-slot="sheet-content"]')?.innerText ?? ''), null, { timeout: 30000 })
    const w = await sheet().evaluate(el => Math.round(el.getBoundingClientRect().width))
    const enc = sheet().locator('button[aria-label^="Encaisser sur"], a:has-text("Ouvrir le devis")').first()
    await enc.scrollIntoViewIfNeeded()
    const inView = await enc.isVisible()
    await shot(page, 'C3-390')
    await page.keyboard.press('Escape')
    if (w >= 388 && inView) ok('C3', `panel ${w}px wide at 390; Encaisser reachable`)
    else bad('C3', 'phone panel', `width=${w} encaisserVisible=${inView}`)
  } },

  { id: 'C4', run: async () => {
    await page.setViewportSize({ width: 1536, height: 730 })
    await page.waitForTimeout(500)
    await page.locator('table:visible tbody tr').first().getByRole('button', { name: /Détail/ }).click()
    await sheet().waitFor({ timeout: 30000 })
    await page.waitForFunction(() => /reste à payer/.test(document.querySelector('[data-slot="sheet-content"]')?.innerText ?? ''), null, { timeout: 30000 })
    const g4 = await sheetGeometry()
    if (g4.right > g4.vw + 1 || g4.over.length) bad('C4-geometry', 'panel content outside the panel', JSON.stringify(g4)); else ok('C4-geometry', `panel ${g4.left}–${g4.right} of ${g4.vw}`)
    const m = await sheet().evaluate(el => { const sc = el.querySelector('.overflow-y-auto'); const r = el.getBoundingClientRect(); return { h: Math.round(r.height), scroller: !!sc, firstDocTop: Math.round(el.querySelector('article')?.getBoundingClientRect().top ?? 9999) } })
    await shot(page, 'C4-1536x730')
    await page.keyboard.press('Escape')
    if (m.h <= 730 && m.scroller && m.firstDocTop < 730) ok('C4', `panel ${m.h}px, own scroller, first document at y=${m.firstDocTop}`)
    else bad('C4', '730 tall', JSON.stringify(m))
  } },

  { id: 'B4', run: async () => {
    await page.setViewportSize({ width: 1440, height: 900 })
    const flip = (o) => { if (o && typeof o === 'object') for (const k of Object.keys(o)) { if (k === 'isMoneyHidden') o[k] = true; else flip(o[k]) } return o }
    await page.route('**/clinics/user-status**', async (r) => { const res = await r.fetch(); const j = await res.json(); await r.fulfill({ response: res, json: flip(j) }) })
    await page.goto(`${BASE}/a-cloturer`, { waitUntil: 'domcontentloaded' })
    await page.getByRole('tab', { name: 'Séances à clôturer' }).waitFor({ timeout: 60000 })
    await page.waitForTimeout(2500)
    const tabs = await page.getByRole('tab').evaluateAll(ts => ts.map(t => t.getAttribute('aria-label')))
    await shot(page, 'B4-1440')
    await page.unroute('**/clinics/user-status**')
    if (tabs.length === 3 && !tabs.includes('Reste à payer')) ok('B4', `money hidden → tabs: ${tabs.join(' | ')}`)
    else bad('B4', 'mode discret', `tabs=${JSON.stringify(tabs)}`)
  } },

  { id: 'B5', run: async () => skip('B5', 'no secretary credentials on the dev database; endpoint policy is AnyClinicRole (source)') },
]

for (const s of SCENARIOS) {
  try { await s.run() } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]) ; await shot(page, `${s.id}-threw`).catch(() => {}) }
}
await browser.close()
console.log('\n' + (findings.filter(f => !f.skipped).length ? 'FINDINGS:' : 'ALL CLEAR (skips below)'))
findings.forEach(f => console.log(JSON.stringify(f)))
