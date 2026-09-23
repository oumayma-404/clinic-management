// Wave-2 browser walk. One launch, every row, fixes nothing.
// Usage: node walk-2.mjs <scratchpad-dir-with-node_modules-and-totp.mjs>
import { readFileSync, mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { createRequire } from 'node:module'

const here = dirname(fileURLToPath(import.meta.url))
const pad = process.argv[2]
const { chromium } = createRequire(join(pad, 'package.json'))('playwright-core')
const { totp } = await import(pathToFileURL(join(pad, 'totp.mjs')).href)
const f = JSON.parse(readFileSync(join(here, 'fixtures-2.json'), 'utf8'))
const shots = join(here, 'shots'); mkdirSync(shots, { recursive: true })
const WEB = 'http://localhost:3000'
const API = 'http://localhost:5000/api'

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, skipped: why }) }

// A separate API token for arranging concurrent writes (never the browser's session) — minted AFTER the browser
// login, in a fresh TOTP window: a code is single-use, and minting it here reused the arrange script's window, so
// `token` was undefined and every API read null (a probe bug, measured 2026-09-23).
let token = null
const api = async (method, path, body) => {
  const r = await fetch(`${API}${path}`, { method, headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` }, body: body === undefined ? undefined : JSON.stringify(body) })
  const t = await r.text(); let j; try { j = t ? JSON.parse(t) : null } catch { j = t }
  return { status: r.status, body: j?.value ?? j }
}

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
await page.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
{
  const login = await (await fetch(`${API}/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO') }),
  })).json()
  token = login.value?.accessToken ?? login.accessToken
  if (!token) throw new Error('api login failed ' + JSON.stringify(login).slice(0, 200))
}

const dialog = () => page.locator('[role=dialog]:visible').last()

async function openAmend(planId) {
  await page.goto(`${WEB}/treatment-plans/${planId}`)
  await page.waitForSelector('text=QA F2', { timeout: 20000 })
  await page.waitForTimeout(1500)
  const direct = page.getByRole('button', { name: /Modifier les actes et les prix/ })
  if (await direct.count()) await direct.first().click()
  else {
    await page.getByRole('button', { name: /Autres actions sur ce devis/ }).first().click()
    await page.getByRole('menuitem', { name: /Modifier les actes et les prix/ }).click()
  }
  const d = dialog()
  await d.waitFor({ timeout: 10000 })
  await page.waitForTimeout(1500)
  return d
}

const scenarios = [
  ['F2', async () => {
    const d = await openAmend(f.F2.plan)
    const text = await d.innerText()
    await page.screenshot({ path: join(shots, 'w2-F2-amend.png') })
    ;/remise\s*−\s*50/.test(text) ? ok('F2', 'remise shown on the line') : bad('F2', 'no remise on the line', text.slice(0, 300))
    ;/Total planifié\s*:\s*340/.test(text.replace(/ | /g, ' ')) ? ok('F2b', 'total reads 340 (net of the remise)') : bad('F2b', 'total not 340', (text.match(/Total planifié[^\n]*/) ?? [''])[0])
  }],
  ['F9', async () => {
    const d = dialog()
    await d.getByRole('button', { name: /Enregistrer la révision/ }).click()
    await page.waitForTimeout(1200)
    const t = await d.innerText()
    ;/Aucune modification demandée/.test(t) ? ok('F9', 'no-change save refused, nothing sent') : bad('F9', 'no-change save did not say « Aucune modification »', t.slice(0, 200))
    const plan = (await api('GET', `/treatment-plans/${f.F2.plan}`)).body
    plan.revisionNumber === f.F2.revision ? ok('F9b', `revision unchanged (${plan.revisionNumber})`) : bad('F9b', `revision moved ${f.F2.revision} → ${plan.revisionNumber}`)
  }],
  ['F1', async () => {
    const d = dialog()
    await d.locator('#title').fill('QA F2 retitré')
    // A colleague's write on the same devis while the form is open.
    await api('PUT', `/treatment-plans/${f.F2.plan}/items/${(await api('GET', `/treatment-plans/${f.F2.plan}`)).body.items[1].id}/discount`, { discountAmount: 10 })
    await page.waitForTimeout(4000)
    const title = await d.locator('#title').inputValue()
    title === 'QA F2 retitré' ? ok('F1', 'typed title survives a colleague save') : bad('F1', `title wiped to « ${title} »`)
  }],
  ['F3', async () => {
    const d = dialog()
    const resp = page.waitForResponse((r) => r.url().includes(`/treatment-plans/${f.F2.plan}/amend`), { timeout: 15000 })
    await d.getByRole('button', { name: /Enregistrer la révision/ }).click()
    const r = await resp
    r.status() === 409 ? ok('F3a', 'stale save refused with 409') : bad('F3a', `stale save → ${r.status()}`)
    await page.waitForTimeout(800)
    const reload = d.getByRole('button', { name: /^Recharger$/ })
    if (!(await reload.count())) return bad('F3', 'no « Recharger » on the 409 banner')
    await reload.click()
    await page.waitForTimeout(2500)
    const text = (await d.innerText()).replace(/ | /g, ' ')
    ;/remise\s*−\s*10/.test(text) ? ok('F3', 'reload re-hydrated the colleague’s remise') : bad('F3', 'reload did not re-hydrate', (text.match(/remise[^\n]*/g) ?? []).join(' | '))
  }],
  ['F10', async () => {
    const d = dialog()
    const months = d.locator('#spread-months')
    if (!(await months.count())) return bad('F10', 'no « Répartir le solde sur N mois » control')
    await months.fill('3')
    await d.getByRole('button', { name: /^Répartir$/ }).click()
    await page.waitForTimeout(500)
    const resp = page.waitForResponse((r) => r.url().includes(`/treatment-plans/${f.F2.plan}/amend`), { timeout: 15000 })
    const reqP = page.waitForRequest((r) => r.url().includes(`/treatment-plans/${f.F2.plan}/amend`))
    await d.getByRole('button', { name: /Enregistrer la révision/ }).click()
    const [r, req] = await Promise.all([resp, reqP]).catch((e) => { throw e })
    const body = JSON.parse(req.postData() || '{}')
    const sum = (body.installments ?? []).reduce((s, i) => s + i.amount, 0)
    r.status() === 200 ? ok('F10', `spread saved (${body.installments?.length} rows, Σ ${sum.toFixed(3)})`) : bad('F10', `spread save → ${r.status()}`, await r.text())
    await page.screenshot({ path: join(shots, 'w2-F10-after.png') })
  }],
  ['C4', async () => {
    await page.goto(`${WEB}/patients/${f.patient}?addRecord=1&appointmentId=${f.C4.visit}`)
    const d = dialog()
    await d.waitFor({ timeout: 20000 })
    await page.waitForTimeout(3500)
    await page.screenshot({ path: join(shots, 'w2-C4-fiche.png') })
    const reqP = page.waitForRequest((r) => r.url().includes('/dental-records') && r.method() === 'POST', { timeout: 15000 })
    const respP = page.waitForResponse((r) => r.url().includes('/dental-records') && r.request().method() === 'POST', { timeout: 15000 })
    await d.getByRole('button', { name: /^(Enregistrer|Confirmer la séance)/ }).last().click()
    const [req, resp] = await Promise.all([reqP, respP])
    const body = JSON.parse(req.postData() || '{}')
    const extras = body.additionalTreatmentPlanItems ?? []
    extras.length === 1 ? ok('C4a', 'fiche sends the second devis act') : bad('C4a', `additionalTreatmentPlanItems = ${JSON.stringify(extras)}`)
    resp.status() === 200 ? ok('C4b', 'fiche saved') : bad('C4b', `save → ${resp.status()}`, (await resp.text()).slice(0, 200))
    await page.waitForTimeout(1500)
    const plan = (await api('GET', `/treatment-plans/${f.C4.plan}`)).body
    const states = plan.items.map((i) => i.status)
    states.every((s) => s === 'Done' || s === 1) ? ok('C4', `both devis acts réalisés (${states.join(', ')})`) : bad('C4', `acts: ${states.join(', ')}`)
  }],
  ['E4', async () => {
    f.E4.draftStatus === 204 && f.E4.draftAfter === 404 ? ok('E4a', 'un-booked draft discarded (api)') : bad('E4a', JSON.stringify(f.E4))
    f.E4.bookedStatus === 400 ? ok('E4b', 'booked devis refused (api)') : bad('E4b', JSON.stringify(f.E4))
  }],
  ['E3', async () => skip('E3', 'no reception credentials on this machine — policy held by TreatmentPlansControllerAuthorizationTests')],
]

process.on('unhandledRejection', () => {})
for (const [id, run] of scenarios) {
  try { await run() } catch (e) { bad(id, 'threw', String(e).slice(0, 300)) }
}
await browser.close()
console.log('\n── findings ──')
findings.length ? findings.forEach((x) => console.log(JSON.stringify(x))) : console.log('ALL CLEAR')
