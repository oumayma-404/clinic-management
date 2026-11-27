// QA walk — rôle « Gestionnaire » (plan.md). Isolated stack only: API :5095, web :3095, DB clinic_role_qa.
// Fixes nothing: every failure is collected and printed at the end.  Usage: node walk.mjs <scratchpadDir> <runLabel>
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { call, clinicSignIn, freshCode, stepUp } from 'file:///C:/dev/clinic-migration/lib.mjs'

const SCRATCH = process.argv[2]
if (!SCRATCH) throw new Error('usage: node walk.mjs <scratchpadDir> <runLabel>')
const { chromium } = createRequire(join(SCRATCH, 'x.js'))('playwright-core')
const API = 'http://localhost:5095/api'
const WEB = 'http://localhost:3095'
const DB = 'clinic_role_qa'
const PASSWORD = 'Qa-Pr123-Import!2026'
const SHOTS = fileURLToPath(new URL(`./shots/${process.argv[3] ?? 'run'}/`, import.meta.url))
mkdirSync(SHOTS, { recursive: true })
const STATE_FILE = join(SCRATCH, 'role-state.json')

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }
const check = (id, cond, m, d = '') => (cond ? ok(id, m) : bad(id, m, d))
const sql = q => execFileSync('docker', ['exec', 'clinic-postgres', 'psql', '-U', 'clinic_user', '-d', DB, '-tAc', q]).toString().trim()
const unwrap = d => (d && typeof d === 'object' && 'value' in d ? d.value : d)
const items = d => { const v = unwrap(d); return Array.isArray(v) ? v : v?.items ?? [] }

// ── Setup over the API: the admin, a gestionnaire and a dentist ─────────────────────────────────────────────
const pr = JSON.parse(readFileSync(join(SCRATCH, 'pr123-state.json'), 'utf8'))
const state = existsSync(STATE_FILE) ? JSON.parse(readFileSync(STATE_FILE, 'utf8')) : {}
state.admin = pr.admin
state.secretary = pr.collegue
const admin = await clinicSignIn(API, { email: pr.admin.email, password: pr.admin.password, totpSecret: pr.admin.totp, log: () => {} })

async function ensureAccount(key, email, fullName, role, extra = {}, onFirstLogin) {
  if (state[key]) return
  const created = await call(API, 'POST', '/users', { token: admin.token, json: { email, fullName, role, ...extra } })
  if (!created.ok) throw new Error(`create ${email}: ${created.status} ${JSON.stringify(created.data)}`)
  const v = unwrap(created.data)
  let password = v.temporaryPassword
  if (v.invitationUrl) {
    const t = decodeURIComponent(new URL(v.invitationUrl).hash.replace(/^#t=/, ''))
    const acc = await call(API, 'POST', '/auth/invitation/accept', { json: { token: t, password: PASSWORD } })
    if (!acc.ok) throw new Error(`accept ${email}: ${acc.status} ${JSON.stringify(acc.data)}`)
    password = PASSWORD
  }
  if (onFirstLogin) await onFirstLogin(email, password)
  const r = await clinicSignIn(API, { email, password, newPassword: PASSWORD, log: () => {} })
  state[key] = { email, password: r.password, totp: r.totpSecret ?? null }
  writeFileSync(STATE_FILE, JSON.stringify(state, null, 2))
}

console.log('\n── Setup')
await ensureAccount('manager', 'gestion@pr123.test', 'Gina Gestion', 'manager', {}, async (email, password) => {
  const first = await call(API, 'POST', '/auth/login', { json: { email, password, trustDevice: false } })
  const code = first.data?.code ?? first.data?.Code
  check('A2', code === 'totp_enrolment_required', 'a new gestionnaire is asked to enrol an authenticator', `${first.status} ${JSON.stringify(first.data)}`)
})
if (!findings.some(f => f.id === 'A2')) console.log('  (A2 asserted on the first run only)')
await ensureAccount('doctor', 'dentiste@pr123.test', 'Dora Dentiste', 'doctor', {
  doctorInfo: { firstName: 'Dora', lastName: 'Dentiste', specialty: 'Dentist' },
})
check('A2b', !!state.manager.totp, 'the gestionnaire account holds an authenticator after first sign-in')

const tokenOf = async who => (await clinicSignIn(API, { email: who.email, password: who.password, totpSecret: who.totp ?? undefined, log: () => {} })).token
const mgr = await tokenOf(state.manager)
const sec = await tokenOf(state.secretary)
const doc = await tokenOf(state.doctor)
const users = items((await call(API, 'GET', '/users?pageSize=100', { token: admin.token })).data)
const adminRow = users.find(u => u.email === pr.admin.email)
const secRow = users.find(u => u.email === pr.collegue.email)

// ── B: the server, as the gestionnaire ──────────────────────────────────────────────────────────────────────
console.log('\n── B / D5 — server rules')
const today = new Date().toISOString().slice(0, 10)
for (const [path, method] of [
  ['/dashboard?period=Today', 'GET'], ['/billing/caisse', 'GET'], ['/billing/cheques', 'GET'], ['/audit', 'GET'],
  ['/invoices/revenue', 'GET'], ['/invoices/export', 'GET'], ['/clinics/money/hide', 'POST'],
]) {
  const r = await call(API, method, path, { token: mgr, json: method === 'POST' ? {} : undefined })
  check('B1', r.status === 403, `gestionnaire ${method} ${path} → 403`, `got ${r.status}`)
}
for (const path of ['/users', '/billing/receivables', '/expenses', '/team/overview', '/backup/history', '/outbox', '/news/summary']) {
  const r = await call(API, 'GET', path, { token: mgr })
  // Reached = past authorization: a 200, or a business answer (« congés pas activés ») — never a 403.
  check('B2', r.status !== 403 && r.status !== 401 && r.status < 500, `gestionnaire GET ${path} → ${r.status} (reached)`, JSON.stringify(r.data).slice(0, 160))
}
const totpState = unwrap((await call(API, 'GET', '/auth/totp', { token: mgr })).data)
check('B5', totpState?.isRequired === true, 'the authenticator is required for a gestionnaire', JSON.stringify(totpState))

// B4: a secretary created by the gestionnaire, then made gestionnaire by him.
let aide = items((await call(API, 'GET', '/users?pageSize=100', { token: mgr })).data).find(u => u.email === 'aide@pr123.test')
if (!aide) {
  const c = await call(API, 'POST', '/users', { token: mgr, json: { email: 'aide@pr123.test', fullName: 'Aïda Aide', role: 'secretary' } })
  check('B4', c.ok, 'the gestionnaire creates a secretary', `${c.status} ${JSON.stringify(c.data).slice(0, 200)}`)
  aide = items((await call(API, 'GET', '/users?pageSize=100', { token: mgr })).data).find(u => u.email === 'aide@pr123.test')
}
if (aide) {
  const target = aide.role === 'manager' ? 'secretary' : 'manager'
  const r = await call(API, 'PUT', `/users/${aide.id}/role`, { token: mgr, json: { role: target, version: aide.version } })
  const stored = sql(`select "Role" from "Users" where "Email"='aide@pr123.test'`)
  check('B4', r.ok && stored === target, `the gestionnaire changes a colleague's role to ${target}`, `${r.status} stored=${stored}`)
} else skip('B4', 'no aide account')

for (const [path, who, tok, want] of [
  ['/users', 'secretary', sec, 403], ['/billing/receivables', 'secretary', sec, 403],
  ['/billing/caisse', 'doctor', doc, 200], ['/users', 'doctor', doc, 403],
]) {
  const r = await call(API, 'GET', path, { token: tok })
  check('D5', r.status === want, `${who} GET ${path} → ${want}`, `got ${r.status}`)
}

// ── Browser ─────────────────────────────────────────────────────────────────────────────────────────────────
const browser = await chromium.launch({ channel: 'chrome', headless: true })
async function session(who, viewport = { width: 1440, height: 900 }) {
  const ctx = await browser.newContext({ viewport })
  await ctx.route('**/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
  const page = await ctx.newPage()
  page.setDefaultTimeout(45_000)
  await page.goto(`${WEB}/login`, { timeout: 120_000 })
  await page.fill('#email', who.email)
  await page.fill('#password', who.password)
  await page.click('button:has-text("Se connecter")')
  if (who.totp) {
    const field = page.locator('input[inputmode=numeric]').first()
    await field.waitFor({ timeout: 30_000 })
    await field.fill(await freshCode(who.totp))
    // The field submits itself at six digits; press « Vérifier » only if it is still waiting.
    await page.waitForTimeout(1500)
    const verify = page.locator('button:has-text("Vérifier")')
    if (new URL(page.url()).pathname.includes("/login") && await verify.isVisible().catch(() => false)) await verify.click()
  }
  await page.waitForURL(u => !u.pathname.includes('/login'), { timeout: 60_000 })
  await page.waitForLoadState('networkidle').catch(() => {})
  return { ctx, page }
}
const railHrefs = page => page.$$eval('aside a[href]', as => as.filter(a => a.offsetParent !== null).map(a => a.getAttribute('href')))
async function openPage(page, path) {
  await page.goto(`${WEB}${path}`)
  await page.waitForLoadState('networkidle').catch(() => {})
  await page.waitForTimeout(600)
  return page.locator('main').innerText().catch(() => '')
}
async function roleOptions(page, scope) {
  const trigger = scope.locator('[aria-label="Rôle"]:visible').first()
  if (!(await trigger.count())) return null
  await trigger.click()
  const opts = await page.locator('[role=option]').allInnerTexts()
  await page.keyboard.press('Escape')
  return opts.map(s => s.trim())
}
async function safely(id, fn) { try { await fn() } catch (e) { bad(id, 'threw', e.message.split('\n')[0]) } }

console.log('\n── A — gestionnaire in the browser')
{
  const { ctx, page } = await session(state.manager)
  await safely('A3', async () => {
    const hrefs = await railHrefs(page)
    for (const h of ['/users', '/medications', '/settings', '/securite', '/stock', '/procedure-types'])
      check('A3', hrefs.includes(h), `rail has ${h}`, hrefs.join(' '))
    check('A3', hrefs.includes('/abonnement'), 'rail has /abonnement (where subscriptions exist)', hrefs.join(' '))
    for (const h of ['/', '/factures', '/caisse', '/cheques', '/journal'])
      check('A3', !hrefs.includes(h), `rail hides ${h}`)
    const aside = await page.locator('aside').first().innerText()
    check('A3', /Gestionnaire/.test(aside), 'rail names the role « Gestionnaire »')
    await page.screenshot({ path: join(SHOTS, 'A3-manager-rail-1440.png') })
  })
  await safely('A4', async () => {
    await page.goto(`${WEB}/`)
    await page.waitForURL(u => u.pathname !== '/', { timeout: 20_000 }).catch(() => {})
    const p = new URL(page.url()).pathname
    check('A4', p !== '/', `« / » sends the gestionnaire elsewhere (${p})`)
  })
  for (const path of ['/factures', '/caisse', '/cheques', '/journal']) await safely('A5', async () => {
    const text = await openPage(page, path)
    check('A5', /Accès restreint|Réservé/.test(text), `${path} shows a refusal`, text.slice(0, 120).replace(/\s+/g, ' '))
  })
  await safely('A6', async () => {
    const text = await openPage(page, '/settings')
    check('A6', !/Accès restreint/.test(text), '/settings opens')
    check('A6', !/lecture seule/.test(text), 'no « lecture seule » notice')
    const modifier = await page.getByRole('button', { name: /Modifier/ }).count()
    check('A6', modifier > 0, `« Modifier » offered (${modifier})`)
    check('A6', !/Mode discret/.test(text), 'no « Mode discret » card')
    await page.screenshot({ path: join(SHOTS, 'A6-manager-settings.png'), fullPage: false })
  })
  await safely('A7', async () => {
    await openPage(page, '/users')
    const adminTr = page.locator('tr', { hasText: pr.admin.email })
    check('A7', (await adminTr.count()) === 1, 'admin row listed')
    check('A7', (await adminTr.locator('[aria-label="Rôle"]').count()) === 0, 'admin row: no role select')
    check('A7', (await adminTr.getByRole('button', { name: /Actions pour/ }).count()) === 0, 'admin row: no ⋯ menu')
    const opts = await roleOptions(page, page.locator('tr', { hasText: pr.collegue.email }))
    check('A7', !!opts && opts.includes('Gestionnaire') && !opts.includes('Administrateur'), 'secretary row: role list without « Administrateur »', JSON.stringify(opts))
    await page.screenshot({ path: join(SHOTS, 'A7-manager-users.png') })
    const create = page.getByRole('button', { name: /Créer un compte|Inviter un collègue/ }).first()
    await create.click()
    await page.locator('#create-user-role').click()
    const createOpts = (await page.locator('[role=option]').allInnerTexts()).map(s => s.trim())
    await page.keyboard.press('Escape'); await page.keyboard.press('Escape')
    check('A7', createOpts.length > 0 && !createOpts.includes('Administrateur'), 'create dialog: no « Administrateur »', JSON.stringify(createOpts))
  })
  for (const path of ['/medications', '/procedure-types', '/securite', '/abonnement']) await safely('A8', async () => {
    const text = await openPage(page, path)
    check('A8', !/Accès restreint|Réservé aux/.test(text), `${path} opens`, text.slice(0, 100).replace(/\s+/g, ' '))
  })
  await safely('A8', async () => {
    await openPage(page, '/procedure-types')
    const add = await page.getByRole('button', { name: /Ajouter|Nouveau/ }).count()
    check('A8', add > 0, 'procedure types editable (« Ajouter » present)')
  })
  await ctx.close()
}

console.log('\n── C — gestionnaire at 390 px')
{
  const { ctx, page } = await session(state.manager, { width: 390, height: 844 })
  await safely('C1', async () => {
    await openPage(page, '/appointments')
    const over = await page.evaluate(() => { const m = document.querySelector('main'); return m ? m.scrollWidth - m.clientWidth : -1 })
    check('C1', over <= 0, 'no sideways scroll at 390', `overflow=${over}`)
    const bar = await page.$$eval('nav a[href]', as => as.filter(a => a.offsetParent !== null).map(a => a.getAttribute('href')))
    check('C1', !bar.includes('/') && !bar.includes('/caisse'), 'bottom bar has no dashboard / caisse', bar.join(' '))
    await page.screenshot({ path: join(SHOTS, 'C1-manager-390.png') })
  })
  await safely('C2', async () => {
    await openPage(page, '/users')
    const card = page.locator('li, article, [data-slot=card]', { hasText: pr.collegue.email }).last()
    const opts = await roleOptions(page, card)
    check('C2', !!opts && !opts.includes('Administrateur') && opts.includes('Gestionnaire'), 'card role list without « Administrateur »', JSON.stringify(opts))
    await page.screenshot({ path: join(SHOTS, 'C2-manager-users-390.png') })
  })
  await ctx.close()
}

console.log('\n── D — admin and secretary unchanged')
{
  const { ctx, page } = await session(state.admin)
  await safely('D1', async () => {
    const hrefs = await railHrefs(page)
    for (const h of ['/', '/factures', '/caisse', '/cheques', '/journal', '/users', '/settings'])
      check('D1', hrefs.includes(h), `admin rail has ${h}`)
  })
  await safely('D2', async () => {
    const text = await openPage(page, '/settings')
    check('D2', /Mode discret/.test(text), 'admin still sees « Mode discret »')
  })
  await safely('D3', async () => {
    await openPage(page, '/users')
    const opts = await roleOptions(page, page.locator('tr', { hasText: pr.collegue.email }))
    check('D3', !!opts && ['Administrateur', 'Gestionnaire', 'Médecin', 'Secrétaire'].every(o => opts.includes(o)), 'admin role list has all four', JSON.stringify(opts))
    const create = page.getByRole('button', { name: /Créer un compte|Inviter un collègue/ }).first()
    await create.click()
    await page.locator('#create-user-role').click()
    const createOpts = (await page.locator('[role=option]').allInnerTexts()).map(s => s.trim())
    await page.keyboard.press('Escape'); await page.keyboard.press('Escape')
    check('A1', createOpts.includes('Gestionnaire') && createOpts.includes('Administrateur'), 'admin create dialog offers « Gestionnaire »', JSON.stringify(createOpts))
    await page.screenshot({ path: join(SHOTS, 'D3-admin-users.png') })
  })
  await ctx.close()
}
{
  const { ctx, page } = await session(state.secretary)
  await safely('D4', async () => {
    const hrefs = await railHrefs(page)
    for (const h of ['/', '/factures', '/settings', '/users'])
      check('D4', !hrefs.includes(h), `secretary rail hides ${h}`)
  })
  await ctx.close()
}
await browser.close()

// ── B3 last: if a guard were broken these would damage the admin account the browser half signs in with ──
console.log('\n── B3 — the gestionnaire never touches an admin')
const before = sql(`select "Role"||'|'||"IsActive"||'|'||("TotpEnrolledAt" is not null)||'|'||"PasswordHash" from "Users" where "Email"='${pr.admin.email}'`)
const attempts = [
  ['create an admin', 'POST', '/users', { email: 'x-admin@pr123.test', fullName: 'X Admin', role: 'admin' }],
  ["change the admin's role", 'PUT', `/users/${adminRow.id}/role`, { role: 'secretary', version: adminRow.version }],
  ["reset the admin's password", 'POST', `/users/${adminRow.id}/reset-password`, {}],
  ['switch the admin off', 'PUT', `/users/${adminRow.id}/status`, { isActive: false, version: adminRow.version }],
  // Identity confirmed first, so the refusal can only be the gestionnaire rule (without it the step-up refuses).
  ["reset the admin's authenticator", 'POST', `/users/${adminRow.id}/totp/reset`,
    { confirmationToken: await stepUp(API, mgr, 'reset-user-totp', state.manager.totp) }],
]
for (const [what, method, path, json] of attempts) {
  const r = await call(API, method, path, { token: mgr, json })
  const why = JSON.stringify(r.data)
  check('B3', !r.ok && /Seul un administrateur/.test(why), `refused by the gestionnaire rule: ${what}`, `${r.status} ${why.slice(0, 160)}`)
}
const after = sql(`select "Role"||'|'||"IsActive"||'|'||("TotpEnrolledAt" is not null)||'|'||"PasswordHash" from "Users" where "Email"='${pr.admin.email}'`)
check('B3', before === after, 'admin account unchanged in the database')
check('B3', sql(`select count(*) from "Users" where "Email"='x-admin@pr123.test'`) === '0', 'no admin was created')

console.log(`\n${findings.length ? `${findings.length} finding(s):` : 'ALL CLEAR'}`)
for (const f of findings) console.log(' ', JSON.stringify(f))
