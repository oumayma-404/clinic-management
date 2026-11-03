// QA walk — guided-first-days release 2 (parts D + E). One launch, every scenario of qa/r2/plan.md; collects, never stops.
// Run: PW_DIR=<dir holding node_modules/playwright-core + otplib> CONSOLE_ACCOUNT=<verb output file> node walk.mjs
// Stack: API :5097 (console :5094), web :3097, console :3094, database clinic_gfd_r2 (throwaway). Mutations go through the
// product; SQL reads, plus the fixtures the plan names (account age, activity days, « a week later »).
import { createRequire } from 'module'
import { execSync } from 'child_process'
import { mkdirSync, readFileSync, writeFileSync } from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const require = createRequire(path.join(process.env.PW_DIR ?? '.', 'x.js'))
const { chromium } = require('playwright-core')
const { authenticator } = require('otplib')

const API = 'http://localhost:5097/api'
const WEB = 'http://localhost:3097'
const CONSOLE = 'http://localhost:3094'
const MAIL = 'http://localhost:8025/api/v1'
const HERE = path.dirname(fileURLToPath(import.meta.url))
const SHOTS = path.join(HERE, 'shots')
mkdirSync(SHOTS, { recursive: true })

const findings = []
const results = []
const ok = (id, m) => { console.log(`  ✅ [${id}] ${m}`); results.push({ id, outcome: 'pass', m }) }
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }); results.push({ id, outcome: 'fail', m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); results.push({ id, outcome: 'skip', m: why }) }
const check = (id, cond, m, d = '') => (cond ? ok(id, m) : bad(id, m, d))
const step = async (id, fn) => {
  try { await fn() } catch (e) { bad(id, 'threw', String(e.message ?? e).split('\n')[0]) }
}

const sql = (q) =>
  execSync('docker exec -i clinic-postgres psql -U clinic_user -d clinic_gfd_r2 -tA -F "|"', { input: q })
    .toString()
    .trim()
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const q = (s) => s.replace(/'/g, "''")

const stamp = Date.now().toString(36)
const admin = {
  email: `qa-r2-${stamp}@clinic.local`,
  password: 'Un-mot-de-passe-QA-2026!',
  clinic: `Cabinet R2 ${stamp}`,
  fullName: 'Dr QA Geller',
}
const NEW_PW = 'Nouveau-mot-de-passe-QA-2026!'
const state = { secret: null, lastCode: null, token: null, clinicId: null }

async function freshCode(secret = state.secret, key = 'lastCode') {
  for (;;) {
    const code = authenticator.generate(secret)
    if (code !== state[key]) {
      state[key] = code
      return code
    }
    await sleep(2000)
  }
}

async function shot(page, name) {
  const file = path.join(SHOTS, `${name}.png`)
  await page.screenshot({ path: file, fullPage: false })
  return file
}

const overflowX = (page) => page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1)

async function mailTo(address, subjectPart, tries = 20, after = 0) {
  for (let i = 0; i < tries; i++) {
    const res = await fetch(`${MAIL}/search?query=${encodeURIComponent(`to:"${address}"`)}`)
    const data = await res.json()
    const hits = (data.messages ?? []).filter((m) => m.Subject.includes(subjectPart) && Date.parse(m.Created) >= after)
    if (hits.length > 0) {
      const full = await (await fetch(`${MAIL}/message/${hits[0].ID}`)).json()
      return { full, count: hits.length }
    }
    await sleep(1500)
  }
  return null
}

async function signIn(page, email, password) {
  await page.goto(`${WEB}/login`)
  await page.locator('#email').fill(email)
  await page.locator('#password').fill(password)
  await page.getByRole('button', { name: 'Se connecter', exact: true }).click()
}

async function toast(page, text, timeout = 8000) {
  try {
    await page.locator('[data-sonner-toast]').filter({ hasText: text }).first().waitFor({ timeout })
    return true
  } catch {
    return false
  }
}

const userId = (email) => sql(`SELECT "Id" FROM "Users" WHERE lower("Email") = lower('${q(email)}')`)
const makeEligible = (email) =>
  sql(`UPDATE "Users" SET "CreatedAt" = now() - interval '10 days' WHERE lower("Email") = lower('${q(email)}')`)
const prompt = (email) =>
  sql(`SELECT "TimesShown", coalesce(round(extract(epoch from ("LaterUntil" - now())) / 86400)::text, 'NULL'), coalesce("ClosedReason", 'NULL') FROM "UserFeedbackPrompts" WHERE "UserId" = '${q(userId(email))}'`)
const patientsNamed = (last) => Number(sql(`SELECT count(*) FROM "Patients" WHERE "LastName" = '${q(last)}'`))

const card = (page) => page.locator('section[aria-labelledby="feedback-prompt-title"]')
async function waitCard(page, timeout = 80000) {
  try {
    await card(page).waitFor({ state: 'visible', timeout })
    return true
  } catch {
    return false
  }
}
const visitPrompt = (page) => page.getByText('Le patient est-il venu ?')

async function apiAs(token, method, url, body) {
  const res = await fetch(`${API}${url}`, {
    method,
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: body ? JSON.stringify(body) : undefined,
  })
  const text = await res.text()
  let json = null
  try { json = JSON.parse(text) } catch {}
  return { status: res.status, json, text }
}

/** First sign-in of an account created by the admin: one-time password → change → sign in again. */
async function firstSignIn(page, email, temp) {
  await signIn(page, email, temp)
  await page.waitForURL('**/change-password**', { timeout: 30000 }).catch(() => {})
  await page.locator('#current-password').fill(temp).catch(() => {})
  await page.locator('#new-password').fill(NEW_PW)
  await page.locator('#confirm-password').fill(NEW_PW)
  await page.locator('form button[type="submit"]').click()
  await page.waitForURL('**/login**', { timeout: 30000 }).catch(() => {})
  await signIn(page, email, NEW_PW)
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30000 }).catch(() => {})
}

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const desktop = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
const page = await desktop.newPage()
const team = {}

try {
  // ── Setup: a cabinet, its admin with a second factor, a doctor and a secretary ────────────────────────
  const signup = await fetch(`${API}/auth/signup`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ clinicName: admin.clinic, fullName: admin.fullName, email: admin.email, password: admin.password, city: 'Tunis' }),
  })
  console.log(`signup → ${signup.status}`)
  const verifyMail = await mailTo(admin.email, '')
  const link = verifyMail && (verifyMail.full.Text + verifyMail.full.HTML).match(/https?:\/\/[^\s"'<>]+\/signup\/verifier#token=[^\s"'<>]+/)?.[0]
  if (!link) throw new Error('no verification link in mailpit')
  await page.goto(link)
  await page.getByText('est créé').first().waitFor({ timeout: 30000 })
  state.clinicId = sql(`SELECT "ClinicId" FROM "Users" WHERE lower("Email") = lower('${q(admin.email)}')`)

  await signIn(page, admin.email, admin.password)
  const key = page.locator('code').first()
  await key.waitFor({ timeout: 20000 })
  state.secret = (await key.textContent()).replace(/\s/g, '')
  await page.locator('input[autocomplete="one-time-code"]').fill(await freshCode())
  await page.getByRole('button', { name: 'Confirmer' }).click()
  await page.getByText('Second facteur activé').waitFor({ timeout: 20000 })
  await page.locator('#ack-codes').click()
  await page.getByRole('button', { name: 'Continuer vers la connexion' }).click()
  await page.getByText('Vérification en deux étapes').waitFor({ timeout: 15000 })
  await page.locator('input[autocomplete="one-time-code"]').fill(await freshCode())
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30000 })
  await page.getByRole('button', { name: 'Aide' }).waitFor({ timeout: 40000 })

  const login = await fetch(`${API}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: admin.email, password: admin.password, totpCode: await freshCode() }),
  })
  state.token = (await login.json().catch(() => null))?.value?.accessToken ?? null
  if (!state.token) throw new Error(`no admin API token (${login.status})`)

  for (const [role, name] of [['doctor', 'Dr Ross QA'], ['secretary', 'Amel QA']]) {
    const email = `${role}-${stamp}@clinic.local`
    const body = { email, fullName: name, role }
    if (role === 'doctor') body.doctorInfo = { firstName: 'Ross', lastName: 'QA', specialty: 'General' }
    const created = await apiAs(state.token, 'POST', '/users', body)
    const temp = created.json?.value?.temporaryPassword ?? created.json?.temporaryPassword
    team[role] = { email, temp, name }
    console.log(`${role} → ${created.status}`)
  }

  // ── B1 — eligibility is the server's: a week, then three days of real use ─────────────────────────────
  await step('B1', async () => {
    const read = async () => (await apiAs(state.token, 'GET', '/feedback/prompt')).json?.show
    const fresh = await read()
    makeEligible(admin.email)
    const cols = sql(`SELECT string_agg(column_name, ',') FROM information_schema.columns WHERE table_name = 'ClinicActivityDays'`)
    console.log(`  ClinicActivityDays columns: ${cols}`)
    const day = (n) => `INSERT INTO "ClinicActivityDays" ("Id","ClinicId","Day","Writes","Appointments","PatientsCreated","ComputedAt") VALUES (gen_random_uuid(), '${state.clinicId}', current_date - ${n}, 5, 1, 0, now());`
    sql(day(1) + day(2))
    const two = await read()
    sql(day(3))
    const three = await read()
    check('B1', fresh === false && two === false && three === true, 'prompt: fresh false · 2 days false · 3 days true', `${fresh} · ${two} · ${three}`)
  })

  // ── A1 — « Aide » in the header ─────────────────────────────────────────────────────────────────────
  await step('A1', async () => {
    await page.goto(`${WEB}/`)
    const trigger = page.getByRole('button', { name: 'Aide' })
    await trigger.waitFor({ timeout: 40000 })
    check('A1', (await trigger.innerText()).includes('Aide'), '« Aide » labelled at 1440')
    await trigger.click()
    const pop = page.locator('[data-slot="popover-content"]').filter({ hasText: 'Une question ?' })
    await pop.waitFor({ timeout: 5000 })
    const wa = pop.getByRole('link', { name: 'Écrire à APEXA sur WhatsApp' })
    const href = (await wa.getAttribute('href')) ?? ''
    check('A1', href.startsWith('https://wa.me/21620123456') && decodeURIComponent(href).includes(`Bonjour, ici ${admin.clinic}.`) && (await wa.getAttribute('target')) === '_blank',
      'WhatsApp row: wa.me/21620123456 with « Bonjour, ici <cabinet>. », new tab', href)
    const vid = pop.getByRole('link', { name: 'Vidéos : vos premiers pas' })
    check('A1', (await vid.getAttribute('href')) === 'https://apexa.tn/tutoriels.html' && (await vid.getAttribute('target')) === '_blank', 'videos row → apexa.tn/tutoriels.html, new tab')
    check('A1', (await pop.getByRole('button', { name: 'Donner mon avis ou une idée' }).count()) === 1, '« Donner mon avis ou une idée » row')
    const text = await pop.innerText()
    check('A1', text.includes('+216 20 123 456') && (await pop.getByRole('button', { name: 'Copier le numéro' }).count()) === 1, 'number shown with « Copier le numéro »', text.replace(/\n/g, ' | '))
    await shot(page, 'A1-aide-1440')
    await page.keyboard.press('Escape')
  })

  // ── A2 — Paramètres « Besoin d'aide ? » ─────────────────────────────────────────────────────────────
  await step('A2', async () => {
    await page.goto(`${WEB}/settings`)
    const head = page.getByText("Besoin d'aide ?")
    await head.waitFor({ timeout: 40000 })
    await head.scrollIntoViewIfNeeded()
    const box = head.locator('xpath=..')
    const t = await box.innerText()
    check('A2', t.includes('Écrire à APEXA sur WhatsApp') && t.includes('Vidéos : vos premiers pas') && t.includes('+216 20 123 456'),
      'Paramètres shows the same help rows', t.replace(/\n/g, ' | ').slice(0, 200))
    await shot(page, 'A2-settings-help')
  })

  // ── A3 — /rappels « Bientôt disponible » ────────────────────────────────────────────────────────────
  await step('A3', async () => {
    await page.goto(`${WEB}/rappels`)
    await page.getByText('Bientôt disponible').first().waitFor({ timeout: 40000 })
    const t = await page.locator('main').innerText()
    const backLink = page.getByRole('link', { name: "Retour à l'agenda" })
    const backButton = page.getByRole('button', { name: "Retour à l'agenda" })
    check('A3', t.includes('Rappels par SMS') && t.includes(admin.clinic), '« Rappels par SMS », example SMS names the cabinet', t.replace(/\n/g, ' | ').slice(0, 300))
    check('A3', (await backLink.count()) === 1 && (await backLink.getAttribute('href')) === '/appointments',
      '« Retour à l\'agenda » is a link to /appointments', `link=${await backLink.count()} button=${await backButton.count()}`)
    check('A3', !/\bDT\b/.test(t) && !/\b20\d\d\b/.test(t.replace(admin.clinic, '')), 'no price, no date of availability')
    await shot(page, 'A3-rappels-1440')
    await page.setViewportSize({ width: 390, height: 844 })
    check('A3', !(await overflowX(page)), 'rappels page: no horizontal scroll at 390')
    await shot(page, 'A3-rappels-390')
    await page.setViewportSize({ width: 1440, height: 900 })
  })

  // ── D7 / C4 — header with « Aide » at 320 ───────────────────────────────────────────────────────────
  await step('D7', async () => {
    await page.setViewportSize({ width: 320, height: 640 })
    await page.goto(`${WEB}/`)
    const trigger = page.getByRole('button', { name: 'Aide' })
    await trigger.waitFor({ timeout: 40000 })
    const box = await trigger.boundingBox()
    check('D7', !(await overflowX(page)) && !!box && box.x + box.width <= 320, 'header at 320: no horizontal scroll, « Aide » inside the screen', JSON.stringify(box))
    await shot(page, 'D7-header-320')
    await trigger.click()
    const pop = page.locator('[data-slot="popover-content"]').filter({ hasText: 'Une question ?' })
    await pop.waitFor({ timeout: 5000 })
    const pb = await pop.boundingBox()
    check('C4', !!pb && pb.x >= 0 && pb.x + pb.width <= 320, '« Aide » popover inside the screen at 320', JSON.stringify(pb))
    await shot(page, 'C4-aide-320')
    await page.keyboard.press('Escape')
    await page.setViewportSize({ width: 1440, height: 900 })
  })

  // ── B5 / A4 / A5 / A6 — « Écrire à APEXA » from « Aide » ────────────────────────────────────────────
  const consoleMailAfter = Date.now()
  await step('A4', async () => {
    await page.goto(`${WEB}/`)
    await page.getByRole('button', { name: 'Aide' }).click()
    await page.getByRole('button', { name: 'Donner mon avis ou une idée' }).click()
    const dlg = page.getByRole('dialog', { name: 'Écrire à APEXA' })
    await dlg.waitFor({ timeout: 8000 })
    const send = dlg.getByRole('button', { name: 'Envoyer' })
    check('B5', await send.isDisabled(), '« Envoyer » disabled with no message')
    const t = await dlg.innerText()
    check('A4', t.includes('Depuis : Tableau de bord') && t.includes(admin.email), '« Depuis : Tableau de bord » and the admin\'s address beside « recontacter »', t.replace(/\n/g, ' | ').slice(0, 300))
    check('A4', (await dlg.getByRole('button', { name: 'Idée' }).getAttribute('aria-pressed')) === 'true', '« Idée » preselected')
    await dlg.getByRole('button', { name: 'Problème' }).click()
    await dlg.getByLabel('Votre message').fill(`Le bouton imprimer ne répond pas ${stamp}`)
    await shot(page, 'A4-ecrire-1440')
    await send.click()
    check('A4', await toast(page, 'Merci. Votre avis est arrivé chez APEXA.'), 'toast « Merci. Votre avis est arrivé chez APEXA. »')
    const closed = await page.getByRole('dialog', { name: 'Écrire à APEXA' }).waitFor({ state: 'hidden', timeout: 5000 }).then(() => true).catch(() => false)
    check('A4', closed, 'dialog closed after sending')
    const row = sql(`SELECT "Kind","Source","FromScreen","MayContact","AuthorRole" FROM "ProductFeedback" WHERE "Message" = 'Le bouton imprimer ne répond pas ${stamp}'`)
    check('A5', row === 'probleme|aide|Tableau de bord|t|admin', 'row: probleme · aide · Tableau de bord · may contact · admin', row)
  })
  await step('A6', async () => {
    const mail = await mailTo('console-qa@apexa.local', 'Nouvel avis sur APEXA', 20, consoleMailAfter - 5000)
    const t = mail ? mail.full.Text : ''
    check('A6', !!mail && t.includes(admin.clinic) && t.includes('Administrateur') && t.includes('Problème') && t.includes(`Le bouton imprimer ne répond pas ${stamp}`) && t.includes('Tableau de bord') && t.includes(admin.email),
      'console mail: cabinet, (Administrateur), Problème, message, écran, address', t.replace(/\s+/g, ' ').slice(0, 400))
  })
  await step('B5', async () => {
    const after = Date.now()
    await page.getByRole('button', { name: 'Aide' }).click()
    await page.getByRole('button', { name: 'Donner mon avis ou une idée' }).click()
    const dlg = page.getByRole('dialog', { name: 'Écrire à APEXA' })
    await dlg.waitFor({ timeout: 8000 })
    await dlg.getByRole('checkbox').click()
    await dlg.getByLabel('Votre message').fill(`Une idée sans contact ${stamp}`)
    await dlg.getByRole('button', { name: 'Envoyer' }).click()
    await toast(page, 'Merci. Votre avis est arrivé chez APEXA.')
    const row = sql(`SELECT "Kind","MayContact" FROM "ProductFeedback" WHERE "Message" = 'Une idée sans contact ${stamp}'`)
    check('B5', row === 'idee|f', 'unticked « recontacter » → MayContact false', row)
    const mail = await mailTo('console-qa@apexa.local', 'Nouvel avis sur APEXA', 20, after - 2000)
    const t = mail ? mail.full.Text : ''
    check('B5', t.includes(`Une idée sans contact ${stamp}`) ? !t.includes(admin.email) && /Recontacter\s*:?\s*non/i.test(t.replace(/\s+/g, ' ')) : false,
      'mail says « Recontacter : non » and carries no address', t.replace(/\s+/g, ' ').slice(0, 300))
  })

  // ── Doctor: C2 (never while typing) and B3 (« Ne plus demander ») ───────────────────────────────────
  await step('B3', async () => {
    if (!team.doctor?.temp) return skip('B3', 'doctor account not created')
    makeEligible(team.doctor.email)
    const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
    const dp = await ctx.newPage()
    await firstSignIn(dp, team.doctor.email, team.doctor.temp)
    await dp.goto(`${WEB}/`)
    const loaded = Date.now()
    const search = dp.getByRole('combobox', { name: 'Rechercher un patient' }).first()
    await search.waitFor({ timeout: 40000 })
    await sleep(Math.max(0, 50000 - (Date.now() - loaded)))
    await search.click()
    await search.pressSequentially('ab', { delay: 200 })
    await sleep(Math.max(0, 72000 - (Date.now() - loaded)))
    const whileTyping = await card(dp).count()
    await dp.evaluate(() => document.activeElement?.blur())
    const after = await waitCard(dp, 12000)
    check('C2', whileTyping === 0 && after, 'no card while the search has focus, the card after blur', `typing=${whileTyping} after=${after}`)
    if (!after) return skip('B3', 'the card never appeared for the doctor')
    await card(dp).getByRole('button', { name: 'Ne plus demander' }).click()
    await sleep(1500)
    const p = prompt(team.doctor.email)
    check('B3', p.endsWith('|never') && (await card(dp).count()) === 0, '« Ne plus demander » → card gone, ClosedReason never', p)
    await dp.reload()
    await sleep(68000)
    check('B3', (await card(dp).count()) === 0, 'no card 68 s after a reload')
    await ctx.close()
  })

  // ── Secretary at 390: C4 (card above the bottom bar), B4 (star → « Fermer » keeps the star), C5 (sheet) ──
  await step('B4', async () => {
    if (!team.secretary?.temp) return skip('B4', 'secretary account not created')
    makeEligible(team.secretary.email)
    const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, locale: 'fr-FR', hasTouch: true })
    const sp = await ctx.newPage()
    await firstSignIn(sp, team.secretary.email, team.secretary.temp)
    await sp.goto(`${WEB}/appointments`)
    if (!(await waitCard(sp, 90000))) return bad('B4', 'the card never appeared for the secretary at 390')
    const cb = await card(sp).boundingBox()
    const nav = await sp.locator('nav[aria-label="Navigation rapide"]').boundingBox()
    check('C4', !!cb && !!nav && cb.y + cb.height <= nav.y + 1 && cb.width >= 390 - 24 - 1 && !(await overflowX(sp)),
      'card full width above the bottom bar at 390, no horizontal scroll', `card=${JSON.stringify(cb)} nav=${JSON.stringify(nav)}`)
    await shot(sp, 'C4-card-390')
    await card(sp).getByRole('radio', { name: '2 sur 5' }).click()
    const dlg = sp.getByRole('dialog', { name: 'Votre avis sur APEXA' })
    await dlg.waitFor({ timeout: 8000 })
    const kept = await dlg.getByRole('radio', { name: '2 sur 5' }).first().getAttribute('aria-checked')
    // The sheet slides up: measure once it has landed, not mid-animation.
    for (let i = 0; i < 20 && ((await dlg.boundingBox())?.y ?? 1) > 1; i++) await sleep(150)
    const db = await dlg.boundingBox()
    const send = await dlg.getByRole('button', { name: 'Envoyer' }).boundingBox()
    check('C5', !!db && db.width >= 389 && !!send && send.y + send.height <= 844, 'week-one form is a full sheet at 390, « Envoyer » on screen', `dlg=${JSON.stringify(db)} send=${JSON.stringify(send)}`)
    await shot(sp, 'C5-form-390')
    check('B4', kept === 'true', 'the tapped star (2) is kept in the form')
    await dlg.locator('[data-slot="dialog-footer"]').getByRole('button', { name: 'Fermer' }).click()
    check('B4', await toast(sp, 'Merci. Votre avis est arrivé chez APEXA.'), '« Fermer » still sends the star (toast)')
    await sleep(1500)
    const row = sql(`SELECT "Source","HelpRating",coalesce("EaseRating"::text,'NULL'),coalesce("LikedAreasCsv",'NULL'),coalesce("Message",'NULL') FROM "ProductFeedback" WHERE "UserId" = '${q(userId(team.secretary.email))}'`)
    check('B4', row === 'semaine-1|2|NULL|NULL|NULL', 'one row: semaine-1, help 2, nothing else', row)
    check('B4', prompt(team.secretary.email).endsWith('|answered'), 'prompt closed « answered »', prompt(team.secretary.email))
    await ctx.close()
  })

  // ── Admin: C1 (never over a dialog), A7, B2, the second showing, C3, D6, A8, C5 at 730 ────────────────
  await step('A7', async () => {
    await page.goto(`${WEB}/`)
    const loaded = Date.now()
    await page.getByRole('button', { name: 'Aide' }).waitFor({ timeout: 40000 })
    await sleep(Math.max(0, 50000 - (Date.now() - loaded)))
    await page.getByRole('button', { name: 'Aide' }).click()
    await page.getByRole('button', { name: 'Donner mon avis ou une idée' }).click()
    await page.getByRole('dialog', { name: 'Écrire à APEXA' }).waitFor({ timeout: 8000 })
    await sleep(Math.max(0, 72000 - (Date.now() - loaded)))
    const overDialog = await card(page).count()
    await page.getByRole('dialog', { name: 'Écrire à APEXA' }).getByRole('button', { name: 'Annuler' }).click()
    const after = await waitCard(page, 12000)
    check('C1', overDialog === 0 && after, 'no card while « Écrire à APEXA » is open; the card within ~6 s of closing it', `over=${overDialog} after=${after}`)
    if (!after) return bad('A7', 'the card never appeared for the admin')
    const t = await card(page).innerText()
    check('A7', t.includes('Une semaine avec APEXA') && t.includes('APEXA vous aide-t-il au cabinet ?') && (await card(page).getByRole('radio').count()) === 5 &&
      (await card(page).getByRole('button', { name: 'Plus tard' }).count()) === 1 && (await card(page).getByRole('button', { name: 'Ne plus demander' }).count()) === 1,
      'card text, five stars, « Plus tard », « Ne plus demander »', t.replace(/\n/g, ' | '))
    const cb = await card(page).boundingBox()
    check('A7', !!cb && cb.x + cb.width >= 1440 - 40 && cb.y + cb.height >= 900 - 40, 'card in the bottom-right corner', JSON.stringify(cb))
    check('A7', await page.evaluate(() => document.body.hasAttribute('data-feedback-open')), 'body carries data-feedback-open')
    await sleep(1500)
    check('A7', prompt(admin.email).startsWith('1|'), 'TimesShown = 1', prompt(admin.email))
    await shot(page, 'A7-card-1440')
  })
  await step('B2', async () => {
    if ((await card(page).count()) === 0) return skip('B2', 'no card to postpone (A7 failed)')
    await card(page).getByRole('button', { name: 'Plus tard' }).click()
    await sleep(1500)
    const p = prompt(admin.email)
    const show = (await apiAs(state.token, 'GET', '/feedback/prompt')).json?.show
    check('B2', (await card(page).count()) === 0 && p.split('|')[1] === '7' && show === false, '« Plus tard » → card gone, LaterUntil ≈ +7 days, prompt false', `${p} show=${show}`)
    sql(`UPDATE "UserFeedbackPrompts" SET "LaterUntil" = now() - interval '1 minute' WHERE "UserId" = '${q(userId(admin.email))}'`)
    await page.goto(`${WEB}/`)
    const back = await waitCard(page, 90000)
    await sleep(1500)
    check('B2', back && prompt(admin.email).startsWith('2|'), 'a week later the card comes back, TimesShown = 2', prompt(admin.email))
  })
  await step('C3', async () => {
    if ((await card(page).count()) === 0) return skip('C3', 'no card up (B2 failed)')
    // D6's fixture: a finished visit due for review, created while the card is up.
    const pat = await apiAs(state.token, 'POST', '/patients', { firstName: 'Revue', lastName: `Visite${stamp}`, gender: 'F', phoneNumber: null })
    const patientId = pat.json?.id ?? pat.json?.value?.id
    const start = new Date(Date.now() - 2 * 3600 * 1000)
    start.setUTCMinutes(0, 0, 0)
    const appt = await apiAs(state.token, 'POST', '/appointments', {
      patientId, appointmentDateTime: start.toISOString(), durationMinutes: 30, allowOutsideWorkingHours: true, allowOverlap: true,
    })
    console.log(`  D6 fixture: patient ${pat.status}, appointment ${appt.status}`)
    state.d6 = appt.status < 300
    await page.locator('aside').getByRole('link', { name: 'Patients', exact: true }).first().click()
    await page.waitForURL('**/patients', { timeout: 20000 })
    const still = await waitCard(page, 15000)
    await sleep(1500)
    const visitUp = await visitPrompt(page).count()
    await shot(page, 'C3-patients-after-screen-change')
    check('C3', still && prompt(admin.email).startsWith('2|'), 'card up on /patients after a screen change, TimesShown still 2',
      `${prompt(admin.email)} card=${await card(page).count()} visitQuestion=${visitUp}`)
    await sleep(8000)
    if (state.d6) check('D6', (await visitPrompt(page).count()) === 0 && (await card(page).count()) === 1, 'no « Séance terminée » question while the card is up',
      `visitQuestion=${await visitPrompt(page).count()} card=${await card(page).count()}`)
    else skip('D6', `fixture appointment refused (${appt.status}: ${appt.text.slice(0, 120)})`)
    // If the visit question took the screen, answer it « Plus tard » so the rest of the plan can still run.
    if ((await card(page).count()) === 0 && (await visitPrompt(page).count()) > 0) {
      state.visitSnoozed = true
      await page.getByRole('dialog').filter({ hasText: 'Le patient est-il venu ?' }).getByRole('button', { name: 'Plus tard' }).first().click()
      await waitCard(page, 15000)
    }
  })
  await step('A8', async () => {
    if ((await card(page).count()) === 0) return skip('A8', 'no card up')
    await card(page).getByRole('radio', { name: '4 sur 5' }).click()
    const dlg = page.getByRole('dialog', { name: 'Votre avis sur APEXA' })
    await dlg.waitFor({ timeout: 8000 })
    check('A8', (await dlg.getByRole('radiogroup', { name: 'APEXA vous aide au cabinet' }).getByRole('radio', { name: '4 sur 5' }).getAttribute('aria-checked')) === 'true',
      'form opens with the 4th star kept')
    await dlg.getByRole('radiogroup', { name: 'Facile à utiliser' }).getByRole('radio', { name: '3 sur 5' }).click()
    await dlg.getByRole('group', { name: 'Ce que vous aimez le plus' }).getByRole('button', { name: 'Agenda' }).click()
    await dlg.getByRole('group', { name: 'Ce qui a été difficile' }).getByRole('button', { name: 'Devis' }).click()
    await dlg.getByLabel('Un mot de plus ?').fill(`Très pratique ${stamp}`)
    await page.setViewportSize({ width: 1536, height: 730 })
    await sleep(500)
    const send = await dlg.getByRole('button', { name: 'Envoyer' }).boundingBox()
    check('C5', !!send && send.y + send.height <= 730, '« Envoyer » visible at 1536×730', JSON.stringify(send))
    await shot(page, 'C5-form-1536x730')
    await page.setViewportSize({ width: 1440, height: 900 })
    await dlg.getByRole('button', { name: 'Envoyer' }).click()
    check('A8', await toast(page, 'Merci. Votre avis est arrivé chez APEXA.'), 'toast after Envoyer')
    await sleep(1500)
    const row = sql(`SELECT "Source","HelpRating","EaseRating","LikedAreasCsv","HardAreasCsv","Message" FROM "ProductFeedback" WHERE "Message" = 'Très pratique ${stamp}'`)
    check('A8', row === `semaine-1|4|3|agenda|devis|Très pratique ${stamp}`, 'row: semaine-1 · 4 · 3 · agenda · devis · message', row)
    const show = (await apiAs(state.token, 'GET', '/feedback/prompt')).json?.show
    check('A8', prompt(admin.email).endsWith('|answered') && show === false, 'prompt answered, show false', `${prompt(admin.email)} show=${show}`)
  })
  await step('D6', async () => {
    if (!state.d6) return skip('D6', 'no due visit fixture')
    if (state.visitSnoozed) return skip('D6', 'second half not reachable: the visit question had to be snoozed to continue (see C3)')
    let opened = false
    try {
      await visitPrompt(page).first().waitFor({ timeout: 70000 })
      opened = true
    } catch {}
    check('D6', opened, '« Séance terminée » opens once the card is answered')
    await shot(page, 'D6-visit-after-card')
    const later = page.getByRole('dialog').getByRole('button', { name: 'Plus tard' })
    if (await later.count()) await later.first().click()
  })

  // ── Console « Retours » ─────────────────────────────────────────────────────────────────────────────
  await step('A9', async () => {
    const acct = readFileSync(process.env.CONSOLE_ACCOUNT, 'utf8')
    const temp = acct.match(/Temporary password:\s*(\S+)/)?.[1]
    const secret = acct.match(/Enrolment secret:\s*(\S+)/)?.[1]
    if (!temp || !secret) return skip('A9', 'console account output not readable')
    const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
    const cp = await ctx.newPage()
    const cLogin = async (pw) => {
      await cp.goto(`${CONSOLE}/login`)
      await cp.locator('#email').fill('console-qa@apexa.local')
      await cp.locator('#password').fill(pw)
      await cp.locator('#totpCode').fill(await freshCode(secret, 'consoleCode'))
      await cp.locator('form button[type="submit"]').click()
    }
    await cLogin(temp)
    await sleep(3000)
    // A re-run: the account was enrolled and its one-time password changed by an earlier drive.
    if (cp.url().includes('/login') && (await cp.getByRole('alert').count()) > 0 && (await cp.getByText('enrôler son second facteur').count()) === 0) {
      await cLogin(NEW_PW)
    }
    await cp.getByText('enrôler son second facteur').waitFor({ timeout: 5000 }).catch(() => {})
    if (await cp.getByText('enrôler son second facteur').count()) {
      await cp.locator('#totpCode').fill(await freshCode(secret, 'consoleCode'))
      await cp.locator('form button[type="submit"]').click()
      await cp.getByRole('button', { name: /J'ai noté mes codes/ }).click({ timeout: 15000 })
      await cp.locator('#totpCode').fill(await freshCode(secret, 'consoleCode'))
      await cp.locator('form button[type="submit"]').click()
    }
    await cp.waitForURL('**/mot-de-passe**', { timeout: 30000 }).catch(() => {})
    if (cp.url().includes('mot-de-passe')) {
      await cp.locator('#currentPassword').fill(temp)
      await cp.locator('#newPassword').fill(NEW_PW)
      await cp.locator('form button[type="submit"]').click()
      await cp.waitForURL('**/login**', { timeout: 30000 }).catch(() => {})
      await cLogin(NEW_PW)
    }
    await cp.waitForURL('**/cabinets**', { timeout: 30000 })
    await cp.getByRole('link', { name: 'Retours', exact: true }).click()
    await cp.waitForURL('**/retours**', { timeout: 30000 })
    await cp.getByRole('heading', { name: 'Retours' }).waitFor({ timeout: 20000 })
    const table = cp.locator('table')
    const rows = await table.locator('tbody tr').allInnerTexts()
    console.log(`  console rows: ${rows.length}`)
    const weekOne = rows.find((r) => r.includes(`Très pratique ${stamp}`)) ?? ''
    const problem = rows.find((r) => r.includes(`Le bouton imprimer ne répond pas ${stamp}`)) ?? ''
    check('A9', weekOne.includes('aide 4/5 · facile 3/5 · Aime : Agenda · Difficile : Devis') && !weekOne.includes(admin.email),
      'week-one row: notes in one line, no address', weekOne.replace(/\s+/g, ' '))
    check('A9', problem.includes('Problème') && problem.includes(admin.email), 'problem row carries the address (agreed)', problem.replace(/\s+/g, ' '))
    const idxWeek = rows.findIndex((r) => r.includes(`Très pratique ${stamp}`))
    const idxProblem = rows.findIndex((r) => r.includes(`Le bouton imprimer ne répond pas ${stamp}`))
    check('A9', idxWeek >= 0 && idxProblem >= 0 && idxWeek < idxProblem, 'newest first')
    await shot(cp, 'A9-retours-1440')

    await cp.getByRole('link', { name: 'Problèmes' }).click()
    await cp.waitForURL('**kind=probleme**', { timeout: 15000 })
    await sleep(800)
    const pr = await cp.locator('table tbody tr').allInnerTexts()
    check('B6', pr.length >= 1 && pr.every((r) => r.includes('Problème')), '« Problèmes » lists only problems', `${pr.length} rows`)
    await cp.getByRole('link', { name: 'Questions' }).click()
    await cp.waitForURL('**kind=question**', { timeout: 15000 })
    check('B6', (await cp.getByText('Aucun retour pour ces critères.').count()) === 1, '« Questions » → « Aucun retour pour ces critères. »')
    await cp.getByRole('link', { name: 'Tous', exact: true }).click()
    await cp.waitForURL((u) => !u.search.includes('kind='), { timeout: 15000 })
    await sleep(800)
    check('B6', (await cp.locator('table tbody tr').count()) >= 4, '« Tous » lists every message again')

    await cp.setViewportSize({ width: 390, height: 844 })
    await sleep(800)
    check('C7', !(await cp.locator('table').isVisible()) && !(await overflowX(cp)), 'console /retours at 390: cards, no table, no horizontal scroll')
    await shot(cp, 'C7-retours-390')
    await ctx.close()
  })

  // ── D2 — « Nouveau patient » in « Nouveau traitement » ──────────────────────────────────────────────
  const openPlanForm = async () => {
    await page.goto(`${WEB}/treatment-plans?newPlan=1`)
    const dlg = page.getByRole('dialog', { name: 'Nouveau traitement' })
    await dlg.waitFor({ timeout: 40000 })
    return dlg
  }
  const fillPlanAct = async (dlg) => {
    await dlg.getByPlaceholder("Désignation de l'acte (ou choisir au catalogue)").first().fill('Détartrage QA')
    await dlg.locator('#plan-line-cost-0').fill('80')
  }
  const newPatientIn = async (dlg, first, last, phone) => {
    await dlg.getByRole('radio', { name: 'Nouveau patient' }).click()
    await dlg.locator('#plan-firstName').fill(first)
    await dlg.locator('#plan-lastName').fill(last)
    if (phone) await dlg.locator('#plan-newPatientPhone').fill(phone)
  }
  const planLast = `Devis${stamp}`

  await step('A10', async () => {
    const dlg = await openPlanForm()
    await newPatientIn(dlg, 'Sami', planLast, '20 555 666')
    await fillPlanAct(dlg)
    await shot(page, 'A10-devis-new-patient')
    await dlg.getByRole('button', { name: 'Créer le devis' }).click()
    check('A10', await toast(page, 'Devis', 12000), 'toast « Devis … créé »')
    await sleep(1500)
    check('A10', patientsNamed(planLast) === 1, 'exactly one patient with that name', String(patientsNamed(planLast)))
    const owner = sql(`SELECT p."LastName" FROM "TreatmentPlans" t JOIN "Patients" p ON p."Id" = t."PatientId" WHERE p."LastName" = '${planLast}' LIMIT 1`)
    check('A10', owner === planLast, 'the devis belongs to that patient', owner)
    state.planPatientId = sql(`SELECT "Id" FROM "Patients" WHERE "LastName" = '${planLast}' LIMIT 1`)
    state.planId = sql(`SELECT t."Id" FROM "TreatmentPlans" t WHERE t."PatientId" = '${state.planPatientId}' LIMIT 1`)
  })
  await step('B9', async () => {
    const before = patientsNamed(`Refus${stamp}`)
    const dlg = await openPlanForm()
    await newPatientIn(dlg, 'Rami', `Refus${stamp}`)
    await dlg.getByRole('button', { name: 'Créer le devis' }).click()
    await sleep(1500)
    check('B9', (await dlg.innerText()).includes('Ajoutez au moins un acte, ou saisissez un titre.') && patientsNamed(`Refus${stamp}`) === before,
      'refused devis → message, no patient created', String(patientsNamed(`Refus${stamp}`)))
  })
  await step('B7', async () => {
    const dlg = await openPlanForm()
    await newPatientIn(dlg, 'Sami', planLast, '20 555 666')
    await fillPlanAct(dlg)
    await dlg.getByRole('button', { name: 'Créer le devis' }).click()
    const ad = page.getByRole('alertdialog')
    await ad.waitFor({ timeout: 10000 })
    const title = await ad.innerText()
    check('B7', title.includes(`Créer quand même un dossier pour Sami ${planLast} ?`), 'duplicate prompt names the person', title.replace(/\n/g, ' | ').slice(0, 200))
    await ad.getByRole('button', { name: 'Choisir un patient existant' }).click()
    await sleep(800)
    check('B7', (await dlg.getByRole('radio', { name: 'Patient existant' }).getAttribute('aria-checked')) === 'true' && (await dlg.locator('#plan-firstName').count()) === 0,
      'back to « Patient existant », fields gone')
    check('B7', patientsNamed(planLast) === 1, 'no patient created', String(patientsNamed(planLast)))
    await shot(page, 'B7-devis-duplicate-cancel')
  })
  await step('B8', async () => {
    const dlg = await openPlanForm()
    await newPatientIn(dlg, 'Sami', planLast, '20 555 666')
    await fillPlanAct(dlg)
    await dlg.getByRole('button', { name: 'Créer le devis' }).click()
    const ad = page.getByRole('alertdialog')
    await ad.waitFor({ timeout: 10000 })
    await ad.getByRole('button', { name: 'Créer quand même' }).click()
    check('B8', await toast(page, 'Devis', 12000), 'devis created after « Créer quand même »')
    await sleep(1500)
    check('B8', patientsNamed(planLast) === 2, 'that name +1 exactly (2)', String(patientsNamed(planLast)))
  })
  await step('B10', async () => {
    const last = `Retry${stamp}`
    const dlg = await openPlanForm()
    await newPatientIn(dlg, 'Nadia', last)
    await fillPlanAct(dlg)
    let failed = false
    await page.route('**/api/treatment-plans', async (route) => {
      if (!failed && route.request().method() === 'POST') {
        failed = true
        return route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ error: 'Panne simulée.' }) })
      }
      return route.continue()
    })
    await dlg.getByRole('button', { name: 'Créer le devis' }).click()
    await sleep(2500)
    const t = await dlg.innerText()
    check('B10', t.includes(`Nadia ${last}`) && t.includes('est créé, pas le devis') && (await dlg.locator('#plan-firstName').isDisabled()),
      'after the failure: « … est créé, pas le devis : réessayez », fields locked', t.replace(/\n/g, ' | ').slice(0, 250))
    await page.unroute('**/api/treatment-plans')
    await dlg.getByRole('button', { name: 'Créer le devis' }).click()
    check('B10', await toast(page, 'Devis', 12000), 'the retry creates the devis')
    await sleep(1500)
    check('B10', patientsNamed(last) === 1, 'that name exists once', String(patientsNamed(last)))
  })
  await step('C6', async () => {
    await page.setViewportSize({ width: 320, height: 640 })
    const dlg = await openPlanForm()
    await dlg.getByRole('radio', { name: 'Nouveau patient' }).click()
    await sleep(500)
    const fb = await dlg.locator('#plan-firstName').boundingBox()
    check('C6', !(await overflowX(page)) && !!fb && fb.x >= 0 && fb.x + fb.width <= 320, 'devis new patient at 320: no horizontal scroll, fields inside', JSON.stringify(fb))
    await shot(page, 'C6-devis-320')
    await page.setViewportSize({ width: 1440, height: 900 })
  })
  await step('D8', async () => {
    if (!state.planPatientId) return skip('D8', 'no patient from A10')
    await page.goto(`${WEB}/patients/${state.planPatientId}?tab=treatment-plans`)
    const btn = page.getByRole('button', { name: 'Nouveau devis' }).first()
    await btn.waitFor({ timeout: 40000 })
    await btn.click()
    const dlg = page.getByRole('dialog', { name: 'Nouveau traitement' })
    await dlg.waitFor({ timeout: 10000 })
    const t = await dlg.innerText()
    check('D8', (await dlg.getByRole('radiogroup', { name: 'Type de patient' }).count()) === 0 && t.includes(planLast),
      'from a patient page: no « Nouveau patient » switch, the patient named', t.replace(/\n/g, ' | ').slice(0, 160))
    await dlg.getByRole('button', { name: 'Annuler' }).click()
  })
  await step('D9', async () => {
    if (!state.planId) return skip('D9', 'no devis from A10')
    await page.goto(`${WEB}/treatment-plans/${state.planId}`)
    let opened = false
    const trigger = page.getByRole('button', { name: 'Autres actions sur ce traitement' }).first()
    if (await trigger.waitFor({ timeout: 40000 }).then(() => true).catch(() => false)) {
      await trigger.click()
      const item = page.getByRole('menuitem', { name: 'Tout modifier' })
      if (await item.waitFor({ timeout: 5000 }).then(() => true).catch(() => false)) { await item.click(); opened = true }
    }
    if (!opened) return skip('D9', '« Tout modifier » not found on the workspace')
    const dlg = page.getByRole('dialog', { name: 'Tout modifier' })
    await dlg.waitFor({ timeout: 10000 })
    check('D9', (await dlg.getByRole('radiogroup', { name: 'Type de patient' }).count()) === 0, 'workspace « Tout modifier »: no patient switch')
    await dlg.getByRole('button', { name: 'Annuler' }).click()
  })

  // ── Booking regression (D1–D5) ──────────────────────────────────────────────────────────────────────
  const openBooking = async () => {
    await page.goto(`${WEB}/appointments`)
    await page.locator('button[data-size="sm"]:has-text("Nouveau")').first().click({ timeout: 40000 })
    const dlg = page.locator("[data-slot='dialog-content']").filter({ has: page.locator("[data-slot='dialog-title']", { hasText: /Nouveau rendez-vous/ }) }).first()
    await dlg.waitFor({ timeout: 10000 })
    return dlg
  }
  const drain = async (max = 5) => {
    const seen = []
    for (let i = 0; i < max; i++) {
      const box = page.locator("[role='alertdialog']").first()
      const appeared = await box.waitFor({ state: 'visible', timeout: i === 0 ? 8000 : 2500 }).then(() => true).catch(() => false)
      if (!appeared) break
      const title = ((await box.locator("h2, [data-slot='alert-dialog-title']").first().textContent()) ?? '').trim()
      seen.push(title)
      const affirm = box.locator('button', { hasText: /^(Continuer|Créer quand même)$/ }).first()
      if ((await affirm.count()) === 0) break
      await affirm.click()
      await sleep(600)
    }
    return seen
  }
  const dismissVisit = async () => {
    for (let i = 0; i < 3; i++) {
      const later = page.getByRole('dialog').filter({ hasText: 'Le patient est-il venu ?' }).getByRole('button', { name: 'Plus tard' })
      if ((await later.count()) === 0) return
      await later.first().click().catch(() => {})
      await sleep(300)
    }
  }
  const bookLast = `Walkin${stamp}`

  await step('D1', async () => {
    const dlg = await openBooking()
    await dlg.getByRole('radio', { name: 'Nouveau patient' }).click()
    await dlg.locator('#firstName').fill('Hela')
    await dlg.locator('#lastName').fill(bookLast)
    await dlg.locator('#newPatientPhone').fill('22 333 444')
    await dlg.locator('button', { hasText: /^Créer le rendez-vous$/ }).first().click()
    const seen = await drain()
    const closed = await dlg.waitFor({ state: 'hidden', timeout: 20000 }).then(() => true).catch(() => false)
    await dismissVisit()
    check('D1', closed && patientsNamed(bookLast) === 1, 'booking with a new patient saved, one patient', `prompts=${seen.join(' · ')} count=${patientsNamed(bookLast)} err=${closed ? '' : (await dlg.innerText()).slice(0, 200)}`)
  })
  await step('D5', async () => {
    const dlg = await openBooking()
    await dlg.getByRole('radio', { name: 'Nouveau patient' }).click()
    await dlg.locator('#firstName').fill('Bad')
    await dlg.locator('#lastName').fill(`Phone${stamp}`)
    await dlg.locator('#newPatientPhone').fill('123')
    await dlg.locator('button', { hasText: /^Créer le rendez-vous$/ }).first().click()
    await sleep(1500)
    const t = await dlg.innerText()
    check('D5', t.includes('Numéro de téléphone invalide') && patientsNamed(`Phone${stamp}`) === 0, 'invalid phone refused before anything is created', t.match(/Numéro[^\n]*/)?.[0] ?? '')
    await page.keyboard.press('Escape')
    const discard = page.getByRole('alertdialog').getByRole('button', { name: /Abandonner|Quitter|Fermer sans/ })
    if (await discard.count()) await discard.first().click()
  })
  await step('D2', async () => {
    const dlg = await openBooking()
    await dlg.getByRole('radio', { name: 'Nouveau patient' }).click()
    await dlg.locator('#firstName').fill('Hela')
    await dlg.locator('#lastName').fill(bookLast)
    await dlg.locator('#newPatientPhone').fill('22 333 444')
    await dlg.locator('button', { hasText: /^Créer le rendez-vous$/ }).first().click()
    let dup = null
    for (let i = 0; i < 5; i++) {
      const box = page.locator("[role='alertdialog']").first()
      if (!(await box.waitFor({ state: 'visible', timeout: 8000 }).then(() => true).catch(() => false))) break
      const title = (await box.innerText()).trim()
      if (title.includes('Créer quand même un dossier')) { dup = box; break }
      await box.locator('button', { hasText: /^Continuer$/ }).first().click()
      await sleep(600)
    }
    if (!dup) return bad('D2', 'no duplicate prompt for an existing name')
    await dup.getByRole('button', { name: 'Choisir un patient existant' }).click()
    await sleep(800)
    check('D2', (await dlg.getByRole('radio', { name: 'Patient existant' }).getAttribute('aria-checked')) === 'true' && (await dlg.locator('#firstName').count()) === 0 && patientsNamed(bookLast) === 1,
      'cancel → « Patient existant », fields gone, no patient created', String(patientsNamed(bookLast)))
    await page.keyboard.press('Escape')
    const discard = page.getByRole('alertdialog').getByRole('button', { name: /Abandonner|Quitter|Fermer sans/ })
    if (await discard.count()) await discard.first().click()
  })
  await step('D3', async () => {
    const dlg = await openBooking()
    await dlg.getByRole('radio', { name: 'Nouveau patient' }).click()
    await dlg.locator('#firstName').fill('Hela')
    await dlg.locator('#lastName').fill(bookLast)
    await dlg.locator('#newPatientPhone').fill('22 333 444')
    await dlg.locator('button', { hasText: /^Créer le rendez-vous$/ }).first().click()
    const seen = await drain()
    const closed = await dlg.waitFor({ state: 'hidden', timeout: 20000 }).then(() => true).catch(() => false)
    await dismissVisit()
    check('D3', seen.some((s) => s.includes('Créer quand même')) && closed && patientsNamed(bookLast) === 2, '« Créer quand même » → saved, that name +1 exactly (2)', `prompts=${seen.join(' · ')} count=${patientsNamed(bookLast)}`)
  })
  await step('D4', async () => {
    const last = `Slot${stamp}`
    const dlg = await openBooking()
    await dlg.getByRole('radio', { name: 'Nouveau patient' }).click()
    await dlg.locator('#firstName').fill('Taken')
    await dlg.locator('#lastName').fill(last)
    await dlg.locator('button', { hasText: /^Créer le rendez-vous$/ }).first().click()
    const seen = await drain()
    const closed = await dlg.waitFor({ state: 'hidden', timeout: 20000 }).then(() => true).catch(() => false)
    await dismissVisit()
    const hadSlot = seen.some((s) => /occupé|déjà pris|chevauche|Créneau/i.test(s))
    if (!hadSlot) return skip('D4', `no taken-slot prompt (prompts: ${seen.join(' · ') || 'none'}) — create-once covered by D3/B10`)
    check('D4', closed && patientsNamed(last) === 1, 'taken slot → continue → saved, the new name exists once', `prompts=${seen.join(' · ')} count=${patientsNamed(last)}`)
  })
} catch (e) {
  bad('SETUP', 'walk aborted', String(e.message ?? e))
} finally {
  skip('B11', 'needs a second API in the SelfHostedLan profile (HTTPS front door, local accounts)')
  writeFileSync(path.join(HERE, 'walk-results.json'), JSON.stringify({ stamp, admin: admin.email, results, findings }, null, 2))
  console.log('\n──────── summary ────────')
  const pass = results.filter((r) => r.outcome === 'pass').length
  const skipped = results.filter((r) => r.outcome === 'skip').length
  console.log(`${pass} ✅ · ${findings.length} ❌ · ${skipped} ⏭`)
  findings.length ? findings.forEach((f) => console.log(f)) : console.log('ALL CLEAR')
  await browser.close()
}
