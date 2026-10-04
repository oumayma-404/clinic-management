// QA walk — Mode discret. Run: node features/money-discreet-mode/qa/walk.mjs <scratchpad-with-playwright-core>
// One launch, every scenario of qa/plan.md. Fixes nothing; collects findings.
import { pathToFileURL, fileURLToPath } from 'node:url'
import { execSync } from 'node:child_process'
import path from 'node:path'
import fs from 'node:fs'

const SCRATCH = process.argv[2]
const pw = await import(pathToFileURL(path.join(SCRATCH, 'node_modules/playwright-core/index.js')).href)
const chromium = pw.chromium ?? pw.default?.chromium
const { totp } = await import(pathToFileURL(path.join(SCRATCH, 'totp.mjs')).href)

const WEB = 'http://localhost:3000'
const API = 'http://localhost:5000/api'
const EMAIL = 'salma.benyoussef@cabinet-ibnkhaldoun.tn'
const PASSWORD = 'QaAudit2026!y'
const SECRET = '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'
const CLINIC = 'f64a8a75-94e1-4e08-a743-fa231abe438f'
const PATIENT = 'd48ed6b4-6e1e-40b5-b14a-224b33c6f512'
const SHOTS = path.join(path.dirname(fileURLToPath(import.meta.url)), 'shots')
fs.mkdirSync(SHOTS, { recursive: true })

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }
const sql = (q) => execSync(`docker exec clinic-postgres psql -U clinic_user -d clinic_management -At -c "${q.replace(/"/g, '\\"')}"`).toString().trim()
const MONEY_RE = /\d[\d\s  ]*,\d{3}\s*DT/
const startedAt = new Date().toISOString()

async function step(id, fn) {
  try { await fn() } catch (e) { bad(id, 'threw', e.message.split('\n')[0]) }
}

const waitFreshWindow = async () => {
  const s = Math.floor(Date.now() / 1000) % 30
  await new Promise(r => setTimeout(r, (30 - s + 1) * 1000))
}

const browser = await chromium.launch({ channel: 'chrome', headless: true })

// ---- D5 — /login signed out reads nothing ---------------------------------------------------------------
await step('D5', async () => {
  const ctx0 = await browser.newContext({ viewport: { width: 1440, height: 900 } })
  const p0 = await ctx0.newPage()
  const hits = []
  p0.on('request', r => { if (r.url().includes('/clinics/user-status')) hits.push(r.url()) })
  await p0.goto(`${WEB}/login`)
  await p0.locator('input[type=email]').waitFor({ timeout: 30000 })
  await p0.waitForTimeout(2500)
  hits.length === 0 ? ok('D5', 'login form, no user-status request') : bad('D5', 'user-status requested while signed out', hits.join(' '))
  await ctx0.close()
})

// ---- sign in ---------------------------------------------------------------------------------------------
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } })
await ctx.route('**/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
let bearer = null
ctx.on('request', r => {
  const a = r.headers()['authorization']
  if (a?.startsWith('Bearer ') && r.url().startsWith(API)) bearer = a
})
const page = await ctx.newPage()
await page.goto(`${WEB}/appointments`)
await page.locator('input[type=email]').fill(EMAIL)
await page.locator('input[type=password]').fill(PASSWORD)
await page.getByRole('button', { name: /Se connecter/ }).click()
await page.locator('input[inputmode=numeric]').waitFor({ timeout: 30000 })
await waitFreshWindow()
await page.locator('input[inputmode=numeric]').fill(totp(SECRET))
try {
  await page.waitForURL(u => !u.pathname.includes('/login'), { timeout: 30000 })
} catch (e) {
  await page.screenshot({ path: path.join(SHOTS, 'login-fail.png') })
  console.log('login failed:', (await page.locator('body').innerText()).slice(0, 300))
  process.exit(1)
}
await page.waitForTimeout(3000)
if (!bearer) { console.log('no bearer captured — aborting'); process.exit(1) }
console.log('signed in')

const api = async (method, p, body) => {
  const res = await fetch(`${API}${p}`, {
    method, headers: { Authorization: bearer, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  let json = null
  const text = await res.text()
  try { json = JSON.parse(text) } catch { /* csv or empty */ }
  return { status: res.status, json, text }
}

const visibleLinks = (pg, href) => pg.locator(`a[href="${href}"]:visible`).count()
const settled = async (pg) => { await pg.waitForLoadState('networkidle', { timeout: 20000 }).catch(() => {}) }

// precondition
const pre = sql(`select "IsMoneyHidden" from "Clinics" where "Id"='${CLINIC}'`)
console.log(`precondition IsMoneyHidden=${pre}`)

// ---- D1–D3, D6, A0 — shown ------------------------------------------------------------------------------
await step('D1', async () => {
  await page.goto(`${WEB}/caisse`); await settled(page)
  await page.getByText('Net', { exact: true }).first().waitFor({ timeout: 20000 })
  const body = await page.locator('main').innerText()
  MONEY_RE.test(body) && !/Section masquée/.test(body) ? ok('D1', `caisse shows figures (${body.match(MONEY_RE)[0]})`) : bad('D1', 'caisse figures missing', body.slice(0, 200))
})
await step('D2', async () => {
  await page.goto(`${WEB}/factures`); await settled(page)
  await page.getByText(/Total encaissé/).first().waitFor({ timeout: 20000 })
  const body = await page.locator('main').innerText()
  MONEY_RE.test(body) ? ok('D2', 'factures shows « Total encaissé » with a DT figure') : bad('D2', 'no DT figure', body.slice(0, 200))
})
await step('D3', async () => {
  await page.goto(`${WEB}/cheques`); await settled(page)
  await page.waitForTimeout(1500)
  const body = await page.locator('main').innerText()
  ;/Chèques/.test(body) && !/Section masquée/.test(body) ? ok('D3', 'chèques page renders') : bad('D3', 'chèques not rendered', body.slice(0, 200))
})
await step('A0', async () => {
  await page.goto(`${WEB}/settings`); await settled(page)
  const btn = page.getByRole('button', { name: 'Masquer', exact: true })
  await btn.waitFor({ timeout: 20000 })
  const enabled = await btn.isEnabled()
  const lastCardText = await page.locator('main [data-slot="card"]').last().innerText()
  enabled && /Mode discret/.test(lastCardText) ? ok('A0', 'card last, « Masquer l\'argent » enabled') : bad('A0', 'card not last or disabled', `enabled=${enabled} last="${lastCardText.slice(0, 60)}"`)
  await page.screenshot({ path: path.join(SHOTS, 'A0-1440.png') })
})
await step('D6', async () => {
  const n = await page.getByText('Informations du cabinet').count()
  n > 0 ? ok('D6', 'other settings cards still render') : bad('D6', '« Informations du cabinet » missing')
})

// ---- B5 — no authenticator ------------------------------------------------------------------------------
await step('B5', async () => {
  await page.route('**/api/auth/totp', async r => {
    const res = await r.fetch(); const j = await res.json()
    if (j?.value) { j.value.isEnrolled = false; j.value.enrolledAt = null }
    await r.fulfill({ response: res, json: j })
  })
  await page.reload(); await settled(page)
  const btn = page.getByRole('button', { name: 'Masquer', exact: true })
  await btn.waitFor({ timeout: 20000 })
  await page.waitForTimeout(800)
  const disabled = await btn.isDisabled()
  const link = await page.locator('main a[href="/securite"]').count()
  disabled && link > 0 ? ok('B5', 'disabled + link to Sécurité') : bad('B5', 'not disabled or no link', `disabled=${disabled} link=${link}`)
  await page.screenshot({ path: path.join(SHOTS, 'B5-1440.png') })
  await page.unroute('**/api/auth/totp')
})

// ---- B4 — a doctor sees no card -------------------------------------------------------------------------
await step('B4', async () => {
  await page.route('**/bff/auth/session', async r => {
    const res = await r.fetch(); const j = await res.json()
    if (j?.user) j.user.role = 'doctor'
    await r.fulfill({ response: res, json: j })
  })
  await page.reload(); await settled(page)
  await page.getByText('Informations du cabinet').first().waitFor({ timeout: 20000 })
  const card = await page.getByText('Mode discret').count()
  const note = await page.getByText(/en lecture seule/).count()
  card === 0 && note > 0 ? ok('B4', 'doctor: no card, read-only note') : bad('B4', 'card shown to a doctor', `card=${card} note=${note}`)
  await page.unroute('**/bff/auth/session')
  await page.reload(); await settled(page)
})

// ---- C1 — 320 ------------------------------------------------------------------------------------------
await step('C1', async () => {
  await page.setViewportSize({ width: 320, height: 640 })
  await page.getByRole('button', { name: 'Masquer', exact: true }).waitFor({ timeout: 20000 })
  const m = await page.evaluate(() => {
    const btn = [...document.querySelectorAll('button')].find(b => b.textContent.trim() === 'Masquer')
    const card = btn.closest('[data-slot="card"]')
    const b = btn.getBoundingClientRect(), c = card.getBoundingClientRect()
    return { sw: document.documentElement.scrollWidth, cw: document.documentElement.clientWidth, bl: b.left, br: b.right, cl: c.left, cr: c.right }
  })
  await page.screenshot({ path: path.join(SHOTS, 'C1-320.png') })
  m.sw <= m.cw && m.bl >= m.cl && m.br <= m.cr ? ok('C1', `no h-scroll (${m.sw}/${m.cw}), button inside card`) : bad('C1', 'overflow at 320', JSON.stringify(m))
  await page.setViewportSize({ width: 1440, height: 900 })
})

// ---- A1 + B7 — hide, rail moves, another page follows ----------------------------------------------------
const page2 = await ctx.newPage()
await step('B7-pre', async () => {
  await page2.goto(`${WEB}/caisse`); await settled(page2)
  await page2.getByText('Net', { exact: true }).first().waitFor({ timeout: 20000 })
})
await step('A1', async () => {
  await page.goto(`${WEB}/settings`); await settled(page)
  const before = await visibleLinks(page, '/caisse')
  await page.getByRole('button', { name: 'Masquer', exact: true }).click()
  await page.getByText('Mode discret activé.').first().waitFor({ timeout: 10000 })
  await page.getByRole('button', { name: 'Afficher', exact: true }).waitFor({ timeout: 10000 })
  const after = { f: await visibleLinks(page, '/factures'), c: await visibleLinks(page, '/caisse'), q: await visibleLinks(page, '/cheques') }
  await page.screenshot({ path: path.join(SHOTS, 'A1-hidden-1440.png') })
  before > 0 && after.f + after.c + after.q === 0 ? ok('A1', `rail lost Finances (caisse links ${before}→0), no reload`) : bad('A1', 'rail still shows money', JSON.stringify({ before, after }))
})
await step('B7', async () => {
  const t0 = Date.now()
  await page2.getByText('Section masquée').first().waitFor({ timeout: 10000 })
  ok('B7', `other page swapped in ${Date.now() - t0} ms`)
  await page2.screenshot({ path: path.join(SHOTS, 'B7-page2.png') })
})

// ---- API rows while hidden ---------------------------------------------------------------------------
await step('A2', async () => {
  const paths = ['/billing/caisse', '/billing/caisse/ledger', '/billing/cheques', '/billing/receivables', '/expenses',
    '/expenses/recurring', '/invoices', '/invoices/revenue', '/invoices/export', '/billing/cheques/export',
    '/billing/receivables/export', '/billing/caisse/ledger/export', '/expenses/export']
  const wrong = []
  for (const p of paths) {
    const r = await api('GET', p)
    if (!(r.status === 403 && r.json?.code === 'money_hidden')) wrong.push(`${p}→${r.status} ${r.text.slice(0, 60)}`)
  }
  wrong.length === 0 ? ok('A2', `${paths.length} reads → 403 money_hidden`) : bad('A2', 'not refused', wrong.join(' | '))
})
await step('A2b', async () => {
  const r = await api('GET', `/invoices?patientId=${PATIENT}`)
  r.status === 200 ? ok('A2b', 'invoices?patientId → 200') : bad('A2b', `status ${r.status}`, r.text.slice(0, 100))
})
await step('A3', async () => {
  const r = await api('GET', '/dashboard')
  const v = r.json?.value ?? r.json
  v && v.money === null && v.receivables === null && Array.isArray(v.trend) && v.trend.length === 0 && v.activity && v.alerts
    ? ok('A3', 'dashboard: money/receivables null, trend [], activity+alerts present')
    : bad('A3', 'dashboard still carries money', r.text.slice(0, 200))
})
await step('C5', async () => {
  const r = await api('POST', '/clinics/money/hide', {})
  r.status === 200 && r.json?.isMoneyHidden === true ? ok('C5', 'second hide → 200 true') : bad('C5', `status ${r.status}`, r.text.slice(0, 120))
})
await step('B2', async () => {
  const r = await api('POST', '/clinics/money/show', {})
  const still = sql(`select "IsMoneyHidden" from "Clinics" where "Id"='${CLINIC}'`)
  r.status === 403 && still === 't' ? ok('B2', 'show without token → 403, still hidden') : bad('B2', `status ${r.status} hidden=${still}`, r.text.slice(0, 120))
})
await step('B1', async () => {
  const r = await api('POST', '/auth/step-up', { action: 'show-money', password: PASSWORD })
  const token = r.json?.value?.confirmationToken
  r.status >= 400 && !token ? ok('B1', `password refused for show-money (${r.status}: ${r.json?.error})`) : bad('B1', 'password minted a show-money token', r.text.slice(0, 120))
})
await step('D7', async () => {
  const r = await api('POST', '/auth/step-up', { action: 'patients-export', password: PASSWORD })
  r.status === 200 && r.json?.value?.confirmationToken ? ok('D7', 'password still confirms another action') : bad('D7', `status ${r.status}`, r.text.slice(0, 120))
})

// ---- browser rows while hidden ------------------------------------------------------------------------
await step('A1b', async () => {
  await page.goto(`${WEB}/`); await settled(page)
  await page.getByText(/L’activité|L'activité/).first().waitFor({ timeout: 20000 })
  await page.waitForTimeout(1500)
  const money = await page.getByText(/^L’argent$|^L'argent$/).count()
  await page.screenshot({ path: path.join(SHOTS, 'A1b-dashboard-hidden.png'), fullPage: false })
  money === 0 ? ok('A1b', 'dashboard: « L\'activité » present, no « L\'argent »') : bad('A1b', '« L\'argent » still on the dashboard')
})
await step('A8', async () => {
  const p3 = await ctx.newPage()
  const moneyReqs = []
  p3.on('request', r => { if (/\/api\/(billing|expenses|invoices)/.test(r.url())) moneyReqs.push(r.url().replace(API, '')) })
  await p3.addInitScript(() => {
    window.__moneySeen = []
    const re = /\d[\d\s  ]*,\d{3}\s*DT/
    const tick = () => { const t = document.body?.innerText ?? ''; const m = t.match(re); if (m) window.__moneySeen.push(m[0]) }
    setInterval(tick, 50)
  })
  await p3.goto(`${WEB}/caisse`)
  await p3.getByText('Section masquée').first().waitFor({ timeout: 20000 })
  await p3.waitForTimeout(1500)
  const seen = await p3.evaluate(() => window.__moneySeen)
  await p3.screenshot({ path: path.join(SHOTS, 'A8-caisse-hidden.png') })
  seen.length === 0 && moneyReqs.length === 0 ? ok('A8', 'card, no amount painted, no money request') : bad('A8', 'money leaked on first load', JSON.stringify({ seen: seen.slice(0, 3), moneyReqs }))
  await p3.close()
})
await step('B6', async () => {
  const out = []
  for (const r of ['/factures', '/cheques']) {
    await page.goto(`${WEB}${r}`)
    try { await page.getByText('Section masquée').first().waitFor({ timeout: 15000 }); out.push(`${r}:card`) } catch { out.push(`${r}:NO CARD`) }
  }
  out.every(x => x.endsWith(':card')) ? ok('B6', out.join(' ')) : bad('B6', 'card missing', out.join(' '))
})
await step('D4', async () => {
  const errors = []
  const onResp = r => { if (r.url().startsWith(API) && r.status() === 403) errors.push(r.url().replace(API, '')) }
  page.on('response', onResp)
  await page.goto(`${WEB}/patients/${PATIENT}?tab=factures`); await settled(page)
  await page.waitForTimeout(2000)
  const body = await page.locator('main').innerText()
  page.off('response', onResp)
  await page.screenshot({ path: path.join(SHOTS, 'D4-patient-factures-hidden.png') })
  errors.length === 0 && !/Section masquée/.test(body) && MONEY_RE.test(body) ? ok('D4', 'patient Factures tab loads with figures, no 403') : bad('D4', 'patient factures tab broken', JSON.stringify({ errors, hasMoney: MONEY_RE.test(body) }))
})
await step('C4', async () => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto(`${WEB}/appointments`); await settled(page)
  await page.getByRole('button', { name: 'Ouvrir la navigation' }).click()
  await page.waitForTimeout(800)
  const n = (await visibleLinks(page, '/factures')) + (await visibleLinks(page, '/caisse')) + (await visibleLinks(page, '/cheques'))
  const control = await visibleLinks(page, '/patients')
  await page.screenshot({ path: path.join(SHOTS, 'C4-390-drawer.png') })
  n === 0 && control > 0 ? ok('C4', 'bar + drawer: no Finances entries') : bad('C4', 'money entries on phone', `money=${n} patientsLink=${control}`)
  await page.keyboard.press('Escape')
  await page.setViewportSize({ width: 1440, height: 900 })
})

// ---- C2, C3 — the code dialog -------------------------------------------------------------------------
const openShow = async () => {
  await page.goto(`${WEB}/settings`); await settled(page)
  await page.getByRole('button', { name: 'Afficher', exact: true }).click()
  await page.getByRole('dialog').waitFor({ timeout: 10000 })
  // the sheet slides in: wait for its enter animation to have run, not for two equal rects
  await page.waitForFunction(() => {
    const d = document.querySelector('[role="dialog"]')
    const a = d?.getAnimations() ?? []
    return d && a.length > 0 && a.every(x => x.playState === 'finished')
  }, null, { timeout: 5000 }).catch(() => {})
  await page.waitForTimeout(200)
}
await step('C2', async () => {
  await page.setViewportSize({ width: 320, height: 640 })
  await openShow()
  const m = await page.evaluate(() => {
    const d = document.querySelector('[role="dialog"]').getBoundingClientRect()
    const f = document.querySelector('[role="dialog"] input').getBoundingClientRect()
    const c = [...document.querySelectorAll('[role="dialog"] button')].find(b => b.textContent.trim() === 'Confirmer').getBoundingClientRect()
    return { vh: innerHeight, vw: innerWidth, dBottom: d.bottom, dTop: d.top, fBottom: f.bottom, fTop: f.top, cBottom: c.bottom, sw: document.documentElement.scrollWidth }
  })
  await page.screenshot({ path: path.join(SHOTS, 'C2-320-sheet.png') })
  const sheet = Math.abs(m.dBottom - m.vh) < 2
  sheet && m.fTop >= 0 && m.fBottom <= m.vh && m.cBottom <= m.vh && m.sw <= m.vw ? ok('C2', `bottom sheet, field+Confirmer inside 320×640 (${JSON.stringify(m)})`) : bad('C2', 'sheet layout wrong', JSON.stringify(m))
  await page.keyboard.press('Escape')
})
await step('C3', async () => {
  await page.setViewportSize({ width: 1536, height: 730 })
  await openShow()
  const m = await page.evaluate(() => {
    const c = [...document.querySelectorAll('[role="dialog"] button')].find(b => b.textContent.trim() === 'Confirmer').getBoundingClientRect()
    return { vh: innerHeight, cBottom: c.bottom }
  })
  await page.screenshot({ path: path.join(SHOTS, 'C3-1536x730.png') })
  m.cBottom <= m.vh ? ok('C3', `Confirmer at ${Math.round(m.cBottom)} of ${m.vh}`) : bad('C3', 'Confirmer below the fold', JSON.stringify(m))
  await page.keyboard.press('Escape')
  await page.setViewportSize({ width: 1440, height: 900 })
})

// ---- B3 then A4 — wrong code, then the right one -----------------------------------------------------
await step('B3', async () => {
  await openShow()
  const wrong = totp(SECRET) === '000000' ? '111111' : '000000'
  await page.locator('[role="dialog"] input').first().fill(wrong)
  await page.getByRole('button', { name: 'Confirmer' }).click()
  await page.getByText('Code de vérification incorrect.').first().waitFor({ timeout: 10000 })
  const open = await page.getByRole('dialog').count()
  const still = sql(`select "IsMoneyHidden" from "Clinics" where "Id"='${CLINIC}'`)
  open > 0 && still === 't' ? ok('B3', 'wrong code: banner, dialog open, still hidden') : bad('B3', 'unexpected', `open=${open} hidden=${still}`)
  await page.screenshot({ path: path.join(SHOTS, 'B3-wrong-code.png') })
})
await step('A4', async () => {
  const wire = []
  page.on('response', async r => { if (/step-up|money\/show/.test(r.url())) wire.push(`${r.status()} ${r.url().replace(API, '')} ${(await r.text().catch(() => '')).slice(0, 120)}`) })
  page.on('request', r => { if (/step-up|money\/show/.test(r.url())) wire.push(`REQ ${r.url().replace(API, '')} ${r.postData()}`) })
  setTimeout(() => console.log('     A4 wire:', wire), 25000).unref?.()
  await waitFreshWindow()
  const field = page.locator('[role="dialog"] input').first()
  await field.fill('')
  await field.fill(totp(SECRET))
  console.log('     A4 field value before submit:', await field.inputValue())
  await page.getByRole('button', { name: 'Confirmer' }).click()
  try {
    await page.getByText('Mode discret désactivé.').first().waitFor({ timeout: 10000 })
  } catch (e) {
    console.log('     A4 wire:', wire, 'dialog:', (await page.locator('[role="dialog"]').innerText().catch(() => '-')).slice(0, 200))
    await page.screenshot({ path: path.join(SHOTS, 'A4-fail.png') })
    throw e
  }
  const live = await visibleLinks(page, '/caisse')
  await page.reload(); await settled(page)
  await page.getByRole('button', { name: 'Masquer', exact: true }).waitFor({ timeout: 20000 })
  const afterReload = await visibleLinks(page, '/caisse')
  const db = sql(`select "IsMoneyHidden" from "Clinics" where "Id"='${CLINIC}'`)
  live > 0 && afterReload > 0 && db === 'f' ? ok('A4', 'shown live, after reload, and in the database') : bad('A4', 'not shown', JSON.stringify({ live, afterReload, db }))
  await page.screenshot({ path: path.join(SHOTS, 'A4-shown.png') })
})

// ---- A10 + C5 — the journal --------------------------------------------------------------------------
await step('A10', async () => {
  const rows = sql(`select "UserEmail" || ' | ' || coalesce("ChangedFields",'') from "AuditEntries" where "EntityType"='Clinic' and "EntityId"='${CLINIC}' and "OccurredAt" >= '${startedAt}' order by "OccurredAt"`)
  const lines = rows ? rows.split('\n') : []
  console.log('     journal:', lines)
  const hide = lines.filter(l => /IsMoneyHidden/.test(l) && /true/i.test(l.split('IsMoneyHidden')[1] ?? ''))
  lines.length === 2 && lines.every(l => l.startsWith(EMAIL) && /IsMoneyHidden/.test(l))
    ? ok('A10', 'two rows (one hide despite the double POST, one show), both naming the account')
    : bad('A10', `expected 2 rows, got ${lines.length}`, lines.join(' || '))
})

await browser.close()
console.log('\n' + (findings.length ? `FINDINGS ${findings.length}` : 'ALL CLEAR'))
findings.forEach(f => console.log(JSON.stringify(f)))
