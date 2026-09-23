// Wave-3 (money) browser walk. One launch, every row, fixes nothing.
// Usage: node walk-3.mjs <scratchpad-dir-with-node_modules-and-totp.mjs>
import { readFileSync, mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { createRequire } from 'node:module'

const here = dirname(fileURLToPath(import.meta.url))
const pad = process.argv[2]
const { chromium } = createRequire(join(pad, 'package.json'))('playwright-core')
const { totp } = await import(pathToFileURL(join(pad, 'totp.mjs')).href)
const f = JSON.parse(readFileSync(join(here, 'fixtures-3.json'), 'utf8'))
const shots = join(here, 'shots'); mkdirSync(shots, { recursive: true })
const WEB = 'http://localhost:3000'
const API = 'http://localhost:5000/api'
const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}` })()

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, skipped: why }) }
const norm = (s) => s.replace(/[  ]/g, ' ')

let token = null
const api = async (method, path, body) => {
  const r = await fetch(`${API}${path}`, { method, headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` }, body: body === undefined ? undefined : JSON.stringify(body) })
  const t = await r.text(); let j; try { j = t ? JSON.parse(t) : null } catch { j = t }
  return { status: r.status, body: j?.value ?? j }
}
const plan = async (id) => (await api('GET', `/treatment-plans/${id}`)).body
const payments = (p) => p.installments.flatMap((i) => i.payments)

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
await ctx.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const page = await ctx.newPage()

await page.goto(`${WEB}/appointments`)
await page.waitForURL(/\/login/)
await page.fill('input[type=email]', 'salma.benyoussef@cabinet-ibnkhaldoun.tn')
await page.fill('input[type=password]', 'QaAudit2026!y')
await page.click("button:has-text('Se connecter')")
await page.waitForSelector('input[inputmode=numeric]')
await page.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
await page.fill('input[inputmode=numeric]', totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'))
await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30000 })
console.log('signed in')
// The API token is minted AFTER the browser login, in a fresh TOTP window: a code is single-use, and reusing
// the arrange script's (or the browser's) one left `token` undefined and every read null — a probe bug.
await page.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
{
  const login = await (await fetch(`${API}/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO') }),
  })).json()
  token = login.value?.accessToken ?? login.accessToken
  if (!token) throw new Error('api login failed ' + JSON.stringify(login).slice(0, 200))
}

const alert = () => page.locator('[role=alertdialog]:visible').last()
const dialog = () => page.locator('[role=dialog]:visible').last()

async function openWorkspace(fx) {
  await page.goto(`${WEB}/treatment-plans/${fx.plan}`)
  await page.waitForSelector(`text=${fx.title}`, { timeout: 25000 })
  await page.waitForTimeout(1500)
}
async function openDiscount(fx, amount) {
  await openWorkspace(fx)
  await page.getByRole('button', { name: /Accorder une remise sur/ }).first().click()
  const d = dialog(); await d.waitFor({ timeout: 10000 })
  await d.locator('#plan-item-discount').fill(String(amount))
  await d.getByRole('button', { name: /Enregistrer la remise/ }).click()
}

const scenarios = [
  ['G3a', async () => {
    await openDiscount(f.G3a, 100)
    const a = alert(); await a.waitFor({ timeout: 10000 })
    const text = norm(await a.innerText())
    await page.screenshot({ path: join(shots, 'w3-G3a-rendu-dialog.png') })
    ;/Rendre au patient/.test(text) && /100,000 DT sont à rendre/.test(text)
      ? ok('G3a', 'remise below collected asks « Rendre au patient ? » naming 100 DT')
      : bad('G3a', 'rendu dialog text', text.slice(0, 300))
    ;/avoir/i.test(text) ? bad('G3a2', 'dialog still mentions an avoir', text) : ok('G3a2', 'no « avoir » in the dialog')
    await a.getByRole('button', { name: /Rendre et enregistrer/ }).click()
    await page.waitForTimeout(2500)
    const p = await plan(f.G3a.plan)
    const rendu = payments(p).find((x) => x.amount < 0)
    const receipt = payments(p).find((x) => x.amount > 0)
    rendu && Math.abs(rendu.amount + 100) < 0.001 && rendu.paidOn.startsWith(today) && rendu.method === 'Cash'
      ? ok('G3a3', `rendu −100 today in cash; total ${p.totalPlanned}, paid ${p.amountPaid}`)
      : bad('G3a3', 'no rendu row', JSON.stringify(payments(p)))
    receipt && !receipt.paidOn.startsWith(today) ? ok('G3a4', `original receipt keeps its day (${receipt.paidOn.slice(0, 10)})`) : bad('G3a4', 'receipt day moved', JSON.stringify(receipt))
    const ws = norm(await page.locator('main').innerText())
    await page.screenshot({ path: join(shots, 'w3-G3a-after.png'), fullPage: false })
    ;/rendu au patient/i.test(ws) ? ok('G3a5', 'échéancier shows « rendu au patient »') : bad('G3a5', 'no rendu line on the workspace')
  }],
  ['G3b', async () => {
    await openDiscount(f.G3b, 100)
    const a = alert(); await a.waitFor({ timeout: 10000 })
    await a.getByRole('button', { name: /^Retour$/ }).click()
    await page.waitForTimeout(1500)
    const p = await plan(f.G3b.plan)
    p.totalPlanned === 300 && p.amountPaid === 300 && !payments(p).some((x) => x.amount < 0)
      ? ok('G3b', 'Retour saved nothing (300 / 300)') : bad('G3b', 'something was saved', `${p.totalPlanned} / ${p.amountPaid}`)
    ;(await dialog().count()) && (await dialog().locator('#plan-item-discount').count())
      ? ok('G3b2', 'remise dialog stays open with its input') : bad('G3b2', 'remise dialog closed on Retour')
  }],
  ['G3c', async () => {
    await openWorkspace(f.G3c)
    await page.getByRole('button', { name: /Autres actions sur ce devis/ }).first().click()
    await page.getByRole('menuitem', { name: /Arrêter le traitement/ }).click()
    const a = alert(); await a.waitFor({ timeout: 10000 })
    const text = norm(await a.innerText())
    await page.screenshot({ path: join(shots, 'w3-G3c-stop.png') })
    ;/Rendre 100,000 DT et arrêter/.test(text) ? ok('G3c', 'stop dialog offers the rendu') : bad('G3c', 'stop dialog title', text.slice(0, 200))
    ;/avoir/i.test(text) ? bad('G3c2', 'stop dialog mentions an avoir') : ok('G3c2', 'no « avoir »')
    const btn = a.getByRole('button', { name: /Rendre et arrêter/ })
    if (!(await btn.count())) return bad('G3c3', 'no « Rendre et arrêter » button')
    await btn.click()
    await page.waitForTimeout(2500)
    const p = await plan(f.G3c.plan)
    p.status === 'Stopped' && p.amountPaid === 0 ? ok('G3c3', 'stopped, 100 DT given back') : bad('G3c3', `status ${p.status} paid ${p.amountPaid}`)
  }],
  ['G3d', async () => {
    await page.goto(`${WEB}/caisse`)
    await page.waitForSelector('text=Extrait', { timeout: 25000 })
    await page.waitForTimeout(2500)
    const text = norm(await page.locator('main').innerText())
    await page.screenshot({ path: join(shots, 'w3-G3d-caisse.png') })
    ;/Rendu au patient/.test(text) ? ok('G3d', 'la caisse lists the rendu') : bad('G3d', 'no « Rendu au patient » movement in the extrait')
  }],
  ['G2', async () => {
    await openDiscount(f.G2, 50)
    await page.waitForTimeout(2500)
    const p = await plan(f.G2.plan)
    const rows = p.installments.map((i) => [i.dueDate.slice(0, 10), i.amount]).sort()
    const dates = rows.map((r) => r[0])
    JSON.stringify(dates) === JSON.stringify(f.G2.dates.map((d) => d.slice(0, 10))) && rows[2][1] === 50
      ? ok('G2', `dates kept, last row 50 (${JSON.stringify(rows)})`) : bad('G2', 'schedule rewritten', JSON.stringify(rows))
  }],
  ['G8', async () => {
    await openWorkspace(f.G8)
    const text = norm(await page.locator('main').innerText())
    await page.screenshot({ path: join(shots, 'w3-G8-writtenoff.png') })
    ;/Reste (dû|à encaisser)/.test(text) ? bad('G8', 'written-off devis still shows a « Reste » figure') : ok('G8', 'no « Reste » figure')
    ;(await page.getByRole('button', { name: /^Encaisser/ }).count()) ? bad('G8b', '« Encaisser » still offered') : ok('G8b', 'no « Encaisser »')
  }],
  ['G1', async () => {
    await openWorkspace(f.G1)
    const direct = page.getByRole('button', { name: /Modifier les actes et les prix/ })
    if (await direct.count()) await direct.first().click()
    else {
      await page.getByRole('button', { name: /Autres actions sur ce devis/ }).first().click()
      await page.getByRole('menuitem', { name: /Modifier les actes et les prix/ }).click()
    }
    const d = dialog(); await d.waitFor({ timeout: 10000 }); await page.waitForTimeout(1500)
    const inputs = d.locator('input')
    let price = null
    for (let i = 0; i < await inputs.count(); i++) {
      const v = await inputs.nth(i).inputValue().catch(() => '')
      if (/^250[,.]000$/.test(v)) { price = inputs.nth(i); break }
    }
    if (!price) return bad('G1', 'price input 250,000 not found')
    await price.fill('0')
    const respP = page.waitForResponse((r) => r.url().includes(`/treatment-plans/${f.G1.plan}/amend`), { timeout: 15000 })
    await d.getByRole('button', { name: /Enregistrer la révision/ }).click()
    const r = await respP
    await page.waitForTimeout(1200)
    const p = await plan(f.G1.plan)
    r.status() === 200 && p.items[0].plannedCost === 0
      ? ok('G1', 'a typed 0 is saved as 0') : bad('G1', `amend ${r.status()} → plannedCost ${p.items[0].plannedCost}`, (await r.text()).slice(0, 200))
  }],
  ['G6', async () => {
    await openWorkspace(f.G6)
    const book = page.getByRole('button', { name: /^Planifier/ }).first()
    if (!(await book.count())) return bad('G6', 'no « Planifier » on the act')
    await book.click()
    const d = dialog(); await d.waitFor({ timeout: 15000 }); await page.waitForTimeout(2500)
    const values = []
    const inputs = d.locator('input')
    for (let i = 0; i < await inputs.count(); i++) values.push(await inputs.nth(i).inputValue().catch(() => ''))
    const text = norm(await d.innerText())
    await page.screenshot({ path: join(shots, 'w3-G6-booking.png') })
    const shows250 = values.some((v) => /^250[,.]000$/.test(v)) || /250,000/.test(text)
    const shows300 = values.some((v) => /^300[,.]000$/.test(v))
    shows250 && !shows300 ? ok('G6', 'booking shows the price after remise (250)') : bad('G6', 'booking price', JSON.stringify(values.filter(Boolean)))
    await page.keyboard.press('Escape')
  }],
  ['G5', async () => skip('G5', 'server rule (fiche redate moves devis money) — held by the aggregate unit test; no fixture with a chairside collection')],
  ['G4', async () => skip('G4', 'client pricing helper — the devis form tooth picker is not scripted; eye pass pending')],
  ['R1', async () => {
    // Regression: an ordinary devis workspace still loads and states its figures.
    await openWorkspace(f.G1)
    const text = norm(await page.locator('main').innerText())
    ;/Total convenu/.test(text) && /Encaissé/.test(text) ? ok('R1', 'workspace figures render') : bad('R1', 'workspace figures missing')
  }],
]

process.on('unhandledRejection', () => {})
for (const [id, run] of scenarios) {
  try { await run() } catch (e) { bad(id, 'threw', String(e).slice(0, 300)) }
}
await browser.close()
console.log('\n── findings ──')
findings.length ? findings.forEach((x) => console.log(JSON.stringify(x))) : console.log('ALL CLEAR')
