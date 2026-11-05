// QA walk — lost second factor: a visible way back. One launch, every scenario of qa/plan.md.
// Run: PW_DIR=<dir holding node_modules/playwright-core + otplib> node walk.mjs
// Stack: API :5097 (RateLimiting__Auth__PermitLimit raised), web :3097, database clinic_gfd_all (throwaway), shared mailpit (read only).
import { createRequire } from 'module'
import { execSync } from 'child_process'
import { mkdirSync, writeFileSync } from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const require = createRequire(path.join(process.env.PW_DIR ?? '.', 'x.js'))
const { chromium } = require('playwright-core')
const { authenticator } = require('otplib')

const API = 'http://localhost:5097/api'
const WEB = 'http://localhost:3097'
const MAIL = 'http://localhost:8025/api/v1'
const HERE = path.dirname(fileURLToPath(import.meta.url))
const SHOTS = path.join(HERE, 'shots-rescue')
mkdirSync(SHOTS, { recursive: true })

const findings = []
const results = []
const ok = (id, m) => { console.log(`  ✅ [${id}] ${m}`); results.push({ id, outcome: 'pass', m }) }
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }); results.push({ id, outcome: 'fail', m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); results.push({ id, outcome: 'skip', m: why }) }
const check = (id, cond, m, d = '') => (cond ? ok(id, m) : bad(id, m, d))
const step = async (id, fn) => {
  try { await fn() } catch (e) {
    bad(id, 'threw', String(e.message ?? e).split('\n')[0])
    // A step that threw may leave a menu or dialog open, which would block every later click.
    for (let i = 0; i < 2; i++) await page?.keyboard.press('Escape').catch(() => {})
  }
}

const sql = (q) =>
  execSync('docker exec -i clinic-postgres psql -U clinic_user -d clinic_gfd_all -tA -F "|"', { input: q }).toString().trim()
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const q = (s) => s.replace(/'/g, "''")

const stamp = Date.now().toString(36)
const admin = { email: `qa-sfr-${stamp}@clinic.local`, password: 'Un-mot-de-passe-QA-2026!', clinic: `Cabinet SFR ${stamp}`, fullName: 'Dr QA Geller', secret: null, lastCode: null }
const ross = { email: `ross-${stamp}@clinic.local`, fullName: 'Dr Ross QA', firstName: 'Ross', lastName: 'QA', role: 'doctor', password: null, newPassword: 'Mon-mot-de-passe-Ross-26', secret: null, lastCode: null }
const sami = { email: `sami-${stamp}@clinic.local`, fullName: 'Sami QA' }
const leila = { email: `leila-${stamp}@clinic.local`, fullName: 'Leila Admin QA', role: 'admin', password: null, newPassword: 'Mon-mot-de-passe-Leila-26', secret: null, lastCode: null }

async function freshCode(acct) {
  for (;;) {
    const code = authenticator.generate(acct.secret)
    if (code !== acct.lastCode) { acct.lastCode = code; return code }
    await sleep(2000)
  }
}
const shot = (page, name) => page.screenshot({ path: path.join(SHOTS, `${name}.png`), fullPage: false })
const overflowX = (page) => page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1)
const visible = (loc) => loc.isVisible().catch(() => false)

async function mails(address, subjectPart, want = 1, tries = 20) {
  for (let i = 0; i < tries; i++) {
    const res = await fetch(`${MAIL}/search?query=${encodeURIComponent(`to:"${address}"`)}`)
    const data = await res.json()
    const hits = (data.messages ?? []).filter((m) => m.Subject.includes(subjectPart))
    if (hits.length >= want) {
      return Promise.all(hits.map(async (h) => ({ meta: h, full: await (await fetch(`${MAIL}/message/${h.ID}`)).json() })))
    }
    await sleep(1500)
  }
  return []
}
async function toastWith(page, text, timeout = 15000) {
  const end = Date.now() + timeout
  while (Date.now() < end) {
    const t = await page.locator('[data-sonner-toast]').allInnerTexts().catch(() => [])
    if (t.some((x) => x.includes(text))) return true
    await sleep(250)
  }
  return false
}

async function signIn(page, email, password) {
  await page.goto(`${WEB}/login`)
  await page.locator('#email').fill(email)
  await page.locator('#password').fill(password)
  await page.getByRole('button', { name: 'Se connecter', exact: true }).click()
}

/** Which step of the sign-in the page is on. */
async function stateOf(page) {
  const p = new URL(page.url()).pathname
  if (await visible(page.locator('#new-password'))) return 'change'
  if (await visible(page.locator('#ack-codes'))) return 'codes'
  if (await visible(page.getByText('Activer le second facteur').first())) return (await visible(page.locator('code').first())) ? 'enrol' : null
  if (await visible(page.getByText('Vérification en deux étapes').first())) return 'totp'
  if (p.startsWith('/login') && (await visible(page.locator('#password')))) return 'login'
  if (!p.startsWith('/login') && !p.startsWith('/change-password') && !p.startsWith('/signup') && (await visible(page.locator('main').first()))) return 'app'
  return null
}
/** Drives a sign-in to the app whatever it asks for (enrolment, codes, forced password change, code). */
async function finishSignIn(page, acct, log = []) {
  let lastActed = null
  for (let i = 0; i < 80; i++) {
    const s = await stateOf(page)
    if (s === 'app') { log.push('app'); return true }
    if (s && s !== lastActed) log.push(s)
    if (s === 'enrol') {
      acct.secret = (await page.locator('code').first().textContent()).replace(/\s/g, '')
      await page.locator('input[autocomplete="one-time-code"]').fill(await freshCode(acct))
      await page.getByRole('button', { name: 'Confirmer' }).click()
      lastActed = s
      await page.locator('#ack-codes').waitFor({ timeout: 20000 })
    } else if (s === 'codes') {
      await page.locator('#ack-codes').click()
      await page.getByRole('button', { name: 'Continuer vers la connexion' }).click()
      lastActed = s
      await sleep(1000)
    } else if (s === 'totp' && lastActed !== 'totp') {
      await page.locator('input[autocomplete="one-time-code"]').fill(await freshCode(acct))
      lastActed = s
      await sleep(1500)
    } else if (s === 'totp') {
      // Still on the code step after a code: wait, then one more fresh code.
      await sleep(4000)
      if ((await stateOf(page)) === 'totp') lastActed = null
    } else if (s === 'change') {
      if (await visible(page.locator('#current-password'))) await page.locator('#current-password').fill(acct.password)
      await page.locator('#new-password').fill(acct.newPassword)
      await page.locator('#confirm-password').fill(acct.newPassword)
      await page.getByRole('button', { name: 'Enregistrer le nouveau mot de passe' }).click()
      acct.password = acct.newPassword
      lastActed = s
      await page.waitForURL(/\/login/, { timeout: 20000 })
    } else if (s === 'login' && (lastActed === 'change' || lastActed === 'codes')) {
      await signIn(page, acct.email, acct.password)
      lastActed = 'login'
    } else {
      await sleep(500)
    }
  }
  return false
}

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctxOf = async (viewport = { width: 1440, height: 900 }) => browser.newContext({ viewport, locale: 'fr-FR' })
const desktop = await ctxOf()
const page = await desktop.newPage()

const menuButton = (pg, person) => pg.locator(`button[aria-label="Actions pour ${person.fullName}"]:visible`).first()
async function openMenu(pg, person) {
  await menuButton(pg, person).click()
  const menu = pg.getByRole('menu')
  await menu.waitFor({ timeout: 10000 })
  return menu
}
async function closeMenu(pg) {
  await pg.keyboard.press('Escape')
  await pg.getByRole('menu').waitFor({ state: 'detached', timeout: 5000 }).catch(() => {})
}
const enrolledRow = (email) =>
  sql(`SELECT (u."TotpEnrolledAt" IS NOT NULL)::text, (SELECT count(*) FROM "UserRecoveryCodes" c WHERE c."UserId" = u."Id") FROM "Users" u WHERE lower(u."Email") = lower('${q(email)}')`)
async function gotoUsers(pg) {
  await pg.goto(`${WEB}/users`)
  await pg.locator(`button[aria-label="Actions pour ${admin.fullName}"]:visible`).first().waitFor({ timeout: 40000 })
}
/**
 * Combined branch (hosted = invite by link): « Inviter un collègue », then the colleague sets their own password on
 * /invitation — there is no temporary password any more. Returns the password chosen. `signInOnce` makes the account
 * a signed-in one (the row menu then offers « Réinitialiser le mot de passe », not « Renvoyer l'e-mail »).
 */
async function createAccount(pg, person, { signInOnce = false } = {}) {
  await pg.getByRole('button', { name: 'Inviter un collègue' }).first().click()
  const d = pg.getByRole('dialog', { name: 'Inviter un collègue' })
  await d.waitFor({ timeout: 10000 })
  await d.locator('#create-user-fullname').fill(person.fullName)
  await d.locator('#create-user-email').fill(person.email)
  const want = person.role === 'doctor' ? 'Médecin' : person.role === 'admin' ? 'Administrateur' : 'Secrétaire'
  const role = await d.locator('#create-user-role').innerText()
  if (!role.includes(want)) {
    await d.locator('#create-user-role').click()
    await pg.getByRole('option', { name: want }).click()
  }
  if (person.role === 'doctor') {
    await d.locator('#create-user-firstname').fill(person.firstName)
    await d.locator('#create-user-lastname').fill(person.lastName)
    if ((await d.locator('#create-user-specialty').count()) && !(await d.locator('#create-user-specialty').innerText()).trim()) {
      await d.locator('#create-user-specialty').click()
      await pg.getByRole('option').first().click()
    }
  }
  await d.getByRole('button', { name: "Envoyer l'invitation" }).click()
  const r = pg.getByRole('dialog').filter({ has: pg.getByRole('button', { name: /Copier le lien|Copié/ }) })
  await r.waitFor({ timeout: 15000 })
  const link = (await r.locator('code').innerText()).trim()
  await r.getByRole('button', { name: 'Terminé' }).click()
  await r.waitFor({ state: 'detached', timeout: 5000 }).catch(() => {})
  const pw = person.newPassword ?? `Mot-de-passe-${person.fullName.replace(/\W/g, '')}-26`
  const ctx = await ctxOf()
  const ip = await ctx.newPage()
  await ip.goto(link)
  await ip.locator('#invitation-password').waitFor({ timeout: 30000 })
  await ip.locator('#invitation-password').fill(pw)
  await ip.locator('#invitation-confirm').fill(pw)
  await ip.getByRole('button', { name: 'Enregistrer mon mot de passe' }).click()
  await ip.waitForURL('**/login**', { timeout: 30000 })
  if (signInOnce) {
    await ip.locator('#password').fill(pw)
    await ip.getByRole('button', { name: 'Se connecter', exact: true }).click()
    await ip.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30000 })
  }
  await ctx.close()
  return pw
}
/** Signs in up to the code step, then « Je n'ai plus accès à mon application ». Returns the recovery step's line (or null). */
async function recoveryLine(pg) {
  await signIn(pg, admin.email, admin.password)
  await pg.getByText('Vérification en deux étapes').first().waitFor({ timeout: 30000 })
  await pg.getByRole('button', { name: "Je n'ai plus accès à mon application" }).click()
  await pg.locator('#recovery-code').waitFor({ timeout: 15000 })
  await sleep(1500) // the probe answers asynchronously; the line is silent until then
  const line = pg.locator('p', { hasText: 'Pas de code de récupération ?' })
  return (await line.count()) ? line.first() : null
}

let rossPage = null

try {
  // ── Setup: a cabinet and its admin with a second factor (D5: ordinary sign-in with a code) ────────────────
  const signup = await fetch(`${API}/auth/signup`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ clinicName: admin.clinic, fullName: admin.fullName, email: admin.email, password: admin.password, city: 'Tunis' }),
  })
  console.log(`signup → ${signup.status}`)
  const verify = (await mails(admin.email, ''))[0]
  const vlink = verify && (verify.full.Text + verify.full.HTML).match(/https?:\/\/[^\s"'<>]+\/signup\/verifier#token=[^\s"'<>]+/)?.[0]
  if (!vlink) throw new Error('no verification link in mailpit')
  await page.goto(vlink)
  await page.getByText('est créé').first().waitFor({ timeout: 30000 })
  await signIn(page, admin.email, admin.password)
  const adminLog = []
  const adminIn = await finishSignIn(page, admin, adminLog)
  check('D5', adminIn, 'admin: enrolment then sign-in with a code lands in the app', adminLog.join(' → '))
  if (!adminIn) throw new Error('admin could not sign in')

  // ── A1 — auth/mode on hosted ────────────────────────────────────────────────────────────────────────────
  await step('A1', async () => {
    const mode = await (await fetch(`${API}/auth/mode`)).json()
    check('A1', mode.vendorResetsSecondFactor === true, 'vendorResetsSecondFactor: true', JSON.stringify(mode.vendorResetsSecondFactor))
    check('A1', mode.supportPhone === '+216 20 123 456', 'supportPhone « +216 20 123 456 »', JSON.stringify(mode.supportPhone))
  })

  // ── Setup: two colleagues ───────────────────────────────────────────────────────────────────────────────
  await gotoUsers(page)
  ross.password = await createAccount(page, ross)
  await createAccount(page, sami, { signInOnce: true })
  leila.password = await createAccount(page, leila)
  console.log('colleagues created')

  // A second admin: this profile requires her second factor, so her first sign-in enrols her.
  const leilaPage = await (await ctxOf()).newPage()
  await signIn(leilaPage, leila.email, leila.password)
  const leilaLog = []
  const leilaIn = await finishSignIn(leilaPage, leila, leilaLog)
  check('D5', leilaIn && enrolledRow(leila.email) === 'true|8', 'second admin: first sign-in enrols her (8 codes)', `${leilaLog.join(' → ')} · ${enrolledRow(leila.email)}`)
  await leilaPage.context().close()

  // The doctor signs in: forced password change, then turns a second factor on from « Sécurité ».
  const rossCtx = await ctxOf()
  rossPage = await rossCtx.newPage()
  await signIn(rossPage, ross.email, ross.password)
  const rossLog = []
  const rossIn = await finishSignIn(rossPage, ross, rossLog)
  check('D5', rossIn, 'doctor: first sign-in (password change) lands in the app', rossLog.join(' → '))
  // Only admins must hold one here; a doctor may (a secretary's « Sécurité » is closed — no factor to lose).
  await rossPage.goto(`${WEB}/securite`)
  await rossPage.getByRole('link', { name: 'Activer maintenant' }).waitFor({ timeout: 90000 }) // first compile of the route
  await rossPage.getByRole('link', { name: 'Activer maintenant' }).click()
  await rossPage.locator('#password').waitFor({ timeout: 30000 })
  await rossPage.locator('#email').fill(ross.email)
  await rossPage.locator('#password').fill(ross.password)
  await rossPage.getByRole('button', { name: 'Continuer', exact: true }).click()
  await rossPage.locator('code').first().waitFor({ timeout: 20000 })
  const enrolLog = []
  const enrolIn = await finishSignIn(rossPage, ross, enrolLog)
  check('D5', enrolIn, 'doctor: « Sécurité » → « Activer maintenant » → enrolment → back in the app', enrolLog.join(' → '))
  check('D5', enrolledRow(ross.email) === 'true|8', 'doctor enrolled with 8 recovery codes', enrolledRow(ross.email))

  // ── A3 / B1 / D2 — who gets the item ────────────────────────────────────────────────────────────────────
  await gotoUsers(page)
  await page.locator(`button[aria-label="Actions pour ${ross.fullName}"]:visible`).first().waitFor({ timeout: 20000 })
  await step('A3', async () => {
    const menu = await openMenu(page, ross)
    check('A3', (await menu.getByRole('menuitem', { name: 'Réinitialiser le second facteur' }).count()) === 1, 'doctor with a second factor: « Réinitialiser le second facteur » offered', (await menu.innerText()).replace(/\n/g, ' | '))
    await shot(page, 'A3-menu-1440')
    await closeMenu(page)
    const own = await openMenu(page, admin)
    check('A3', (await own.getByRole('menuitem', { name: 'Réinitialiser le second facteur' }).count()) === 0, 'own row: not offered', (await own.innerText()).replace(/\n/g, ' | '))
    const deact = own.getByRole('menuitem', { name: 'Désactiver' })
    const dis = (await deact.getAttribute('data-disabled')) !== null || (await deact.getAttribute('aria-disabled')) === 'true'
    check('D2', dis, 'own row: « Désactiver » disabled')
    await closeMenu(page)
  })
  await step('B1', async () => {
    const menu = await openMenu(page, sami)
    const text = (await menu.innerText()).replace(/\n/g, ' | ')
    check('B1', (await menu.getByRole('menuitem', { name: 'Réinitialiser le second facteur' }).count()) === 0, 'never-enrolled colleague: not offered', text)
    await closeMenu(page)
  })

  const openReset = async () => {
    const menu = await openMenu(page, ross)
    await menu.getByRole('menuitem', { name: 'Réinitialiser le second facteur' }).click()
    const d = page.getByRole('dialog', { name: 'Confirmer votre identité' })
    await d.waitFor({ timeout: 10000 })
    return d
  }

  // ── B2 — cancel ─────────────────────────────────────────────────────────────────────────────────────────
  await step('B2', async () => {
    const d = await openReset()
    await d.getByRole('button', { name: 'Annuler' }).click()
    await d.waitFor({ state: 'detached', timeout: 5000 }).catch(() => {})
    check('B2', (await d.count()) === 0, 'Annuler closes the dialog')
    check('B2', enrolledRow(ross.email) === 'true|8', 'still enrolled after Annuler', enrolledRow(ross.email))
  })

  // ── B3 — wrong code ─────────────────────────────────────────────────────────────────────────────────────
  await step('B3', async () => {
    const d = await openReset()
    let wrong = '000000'
    if (wrong === authenticator.generate(admin.secret)) wrong = '111111'
    await d.locator('input[autocomplete="one-time-code"]').fill(wrong)
    await d.getByRole('button', { name: 'Confirmer' }).click()
    const alert = d.locator('[role="alert"]')
    const shown = await alert.first().waitFor({ timeout: 10000 }).then(() => true, () => false)
    check('B3', shown, 'refusal shown in the dialog', shown ? (await alert.first().innerText()).replace(/\n/g, ' ') : '')
    check('B3', enrolledRow(ross.email) === 'true|8', 'still enrolled after a wrong code', enrolledRow(ross.email))
    await shot(page, 'B3-wrong-code-1440')
    await d.getByRole('button', { name: 'Annuler' }).click()
    await d.waitFor({ state: 'detached', timeout: 5000 }).catch(() => {})
  })

  // ── C5 — step-up at 1536 × 730 ──────────────────────────────────────────────────────────────────────────
  await step('C5', async () => {
    await page.setViewportSize({ width: 1536, height: 730 })
    const d = await openReset()
    const box = await d.getByRole('button', { name: 'Confirmer' }).boundingBox()
    check('C5', !!box && box.y + box.height <= 730, '« Confirmer » inside 730 px', JSON.stringify(box))
    await shot(page, 'C5-stepup-1536x730')
    await d.getByRole('button', { name: 'Annuler' }).click()
    await d.waitFor({ state: 'detached', timeout: 5000 }).catch(() => {})
    await page.setViewportSize({ width: 1440, height: 900 })
  })

  // ── A4 / A5 — the reset ─────────────────────────────────────────────────────────────────────────────────
  await step('A4', async () => {
    const d = await openReset()
    const text = await d.innerText()
    check('A4', text.includes(ross.email) && text.includes('codes de récupération seront supprimés') && text.includes('sessions fermées'), 'sentence names his e-mail, the codes and the sessions', text.replace(/\n/g, ' | '))
    check('A4', !text.includes('prochaine connexion'), 'no claim about his next sign-in (F-1: a doctor is not asked for a new one)', text.replace(/\n/g, ' | '))
    await sleep(500) // let the dialog finish its entrance before the screenshot (a shot mid-fade reads as translucent)
    await shot(page, 'A4-stepup-1440')
    await step('A5', async () => {
      await d.locator('input[autocomplete="one-time-code"]').fill(await freshCode(admin))
      await d.getByRole('button', { name: 'Confirmer' }).click()
      check('A5', await toastWith(page, 'Second facteur réinitialisé. Ses sessions ont été fermées.'), 'toast « Second facteur réinitialisé. Ses sessions ont été fermées. »')
      await sleep(1000)
      check('A5', enrolledRow(ross.email) === 'false|0', 'DB: not enrolled, no recovery codes', enrolledRow(ross.email))
      await page.reload()
      await page.locator(`button[aria-label="Actions pour ${ross.fullName}"]:visible`).first().waitFor({ timeout: 30000 })
      const menu = await openMenu(page, ross)
      check('A5', (await menu.getByRole('menuitem', { name: 'Réinitialiser le second facteur' }).count()) === 0, 'her menu no longer offers it', (await menu.innerText()).replace(/\n/g, ' | '))
      await closeMenu(page)
    })
  })

  // ── A7 — his open session ends ──────────────────────────────────────────────────────────────────────────
  await step('A7', async () => {
    await rossPage.goto(`${WEB}/patients`)
    const out = await rossPage.waitForURL(/\/login/, { timeout: 30000 }).then(() => true, () => false)
    check('A7', out, 'his open session is closed (next page → /login)', rossPage.url())
  })

  /** Signs in after a reset; returns what the person meets next. */
  const afterReset = async (person, shotName) => {
    const pg = await (await ctxOf()).newPage()
    await signIn(pg, person.email, person.password)
    let met = 'nothing'
    for (let i = 0; i < 60 && met === 'nothing'; i++) {
      if ((await visible(pg.locator('code').first())) && (await visible(pg.getByText('Activer le second facteur').first()))) met = 'enrolment'
      else if (await visible(pg.getByText('Vérification en deux étapes').first())) met = 'code step'
      else if (!new URL(pg.url()).pathname.startsWith('/login') && (await visible(pg.locator('main').first()))) met = 'the app, no second factor asked'
      else await sleep(500)
    }
    await shot(pg, shotName)
    await pg.context().close()
    return met
  }

  // ── A6 — the doctor signs in again: his factor is optional here, so the password alone lets him in ─────
  await step('A6', async () => {
    const met = await afterReset(ross, 'A6-doctor-after-reset-1440')
    check('A6', met === 'the app, no second factor asked', 'doctor (factor optional here): signs in with his password alone — nothing on screen claimed otherwise', `he met: ${met}`)
  })

  // ── A8 — the same rescue for a second administrator (her factor is required) ───────────────────────────
  await step('A8', async () => {
    const menu = await openMenu(page, leila)
    await menu.getByRole('menuitem', { name: 'Réinitialiser le second facteur' }).click()
    const d = page.getByRole('dialog', { name: 'Confirmer votre identité' })
    await d.waitFor({ timeout: 10000 })
    await d.locator('input[autocomplete="one-time-code"]').fill(await freshCode(admin))
    await d.getByRole('button', { name: 'Confirmer' }).click()
    check('A8', await toastWith(page, 'Second facteur réinitialisé.'), 'toast « Second facteur réinitialisé. … »')
    await sleep(1000)
    check('A8', enrolledRow(leila.email) === 'false|0', 'DB: not enrolled, no recovery codes', enrolledRow(leila.email))
    const met = await afterReset(leila, 'A8-admin-after-reset-1440')
    check('A8', met === 'enrolment', 'she signs in again and meets the enrolment', `she met: ${met}`)
  })

  // ── D1 — reset password still works from the menu ──────────────────────────────────────────────────────
  await step('D1', async () => {
    const menu = await openMenu(page, sami)
    await menu.getByRole('menuitem', { name: 'Réinitialiser le mot de passe' }).click()
    const c = page.getByRole('alertdialog')
    await c.waitFor({ timeout: 10000 })
    check('D1', (await c.innerText()).includes('Réinitialiser le mot de passe de cet utilisateur ?'), 'confirmation asks')
    await c.getByRole('button', { name: 'Confirmer' }).click()
    const t = page.getByRole('dialog', { name: 'Mot de passe temporaire' })
    const shown = await t.waitFor({ timeout: 15000 }).then(() => true, () => false)
    const pw = shown ? (await t.locator('code').innerText()).trim() : ''
    check('D1', pw.length >= 8, '« Mot de passe temporaire » with a password', `${pw.length} chars`)
    if (shown) await t.getByRole('button', { name: 'Terminé' }).click()
  })

  // ── D3 — deactivate then reactivate (an account that has signed in; a never-signed-in one reads « En attente d'activation » by design, User.IsPendingActivation) ──
  await step('D3', async () => {
    const chip = async () => {
      const row = page.locator('tr:visible', { hasText: ross.email })
      return (await row.innerText()).replace(/\n/g, ' ')
    }
    let menu = await openMenu(page, ross)
    await menu.getByRole('menuitem', { name: 'Désactiver' }).click()
    let c = page.getByRole('alertdialog')
    await c.waitFor({ timeout: 10000 })
    await c.getByRole('button', { name: 'Confirmer' }).click()
    await c.waitFor({ state: 'detached', timeout: 10000 }).catch(() => {})
    await sleep(1500)
    const off = await chip()
    check('D3', off.includes('Inactif'), 'Désactiver → « Inactif »', off)
    menu = await openMenu(page, ross)
    await menu.getByRole('menuitem', { name: 'Réactiver' }).click()
    c = page.getByRole('alertdialog')
    await c.waitFor({ timeout: 10000 })
    await c.getByRole('button', { name: 'Confirmer' }).click()
    await c.waitFor({ state: 'detached', timeout: 10000 }).catch(() => {})
    await sleep(1500)
    const on = await chip()
    check('D3', on.includes('Actif') && !on.includes('Inactif'), 'Réactiver → « Actif »', on)
  })

  // ── D4 — search ─────────────────────────────────────────────────────────────────────────────────────────
  await step('D4', async () => {
    await page.getByPlaceholder('Rechercher un utilisateur (nom, email)…').fill('Sami')
    await sleep(1500)
    const rows = await page.locator('tbody tr:visible').allInnerTexts()
    check('D4', rows.length === 1 && rows[0].includes(sami.email), 'only Sami in the table', rows.map((r) => r.replace(/\n/g, ' ')).join(' || '))
    await page.getByPlaceholder('Rechercher un utilisateur (nom, email)…').fill('')
    await sleep(1000)
  })

  // ── C1 / C2 / C3 — widths ───────────────────────────────────────────────────────────────────────────────
  for (const [id, w, h, wantTable] of [
    ['C3', 320, 640, false], ['C3', 390, 844, false], ['C1', 1024, 800, false], ['C1', 1279, 800, false],
    ['C2', 1280, 800, true], ['C2', 1440, 900, true],
  ]) {
    await step(id, async () => {
      await page.setViewportSize({ width: w, height: h })
      await gotoUsers(page)
      const table = await visible(page.locator('table').first())
      check(id, table === wantTable, `${w}px: ${wantTable ? 'table' : 'cards'}`, `table visible: ${table}`)
      check(id, !(await overflowX(page)), `${w}px: no horizontal page scroll`)
      const btn = menuButton(page, ross)
      await btn.scrollIntoViewIfNeeded()
      const box = await btn.boundingBox()
      const scroller = await btn.evaluate((el) => {
        let n = el.parentElement
        while (n && n !== document.body) {
          const s = getComputedStyle(n)
          if ((s.overflowX === 'auto' || s.overflowX === 'scroll') && n.scrollWidth > n.clientWidth + 1) return `${n.tagName}.${n.className.toString().slice(0, 40)} ${n.scrollWidth}>${n.clientWidth}`
          n = n.parentElement
        }
        return null
      })
      check(id, !!box && box.x >= 0 && box.x + box.width <= w && !scroller, `${w}px: row menu button inside the viewport, no inner scroll`, `${JSON.stringify(box)} ${scroller ?? ''}`)
      await shot(page, `${id}-users-${w}`)
    })
  }
  await page.setViewportSize({ width: 1440, height: 900 })

  // ── A2 / C4 — the line on the recovery step (hosted) ────────────────────────────────────────────────────
  await step('A2', async () => {
    const pg = await (await ctxOf()).newPage()
    const line = await recoveryLine(pg)
    if (!line) { bad('A2', 'no « Pas de code de récupération ? » line on the recovery step'); await shot(pg, 'A2-recovery-1440'); return }
    const text = (await line.innerText()).replace(/\s+/g, ' ').trim()
    check('A2', text === 'Pas de code de récupération ? Un administrateur de votre cabinet peut réinitialiser votre second facteur. Sinon, appelez le support : +216 20 123 456.', 'sentence + support number', text)
    const href = await line.locator('a').getAttribute('href').catch(() => null)
    check('A2', href === 'tel:+21620123456', 'tel link', href)
    check('A2', await visible(pg.getByRole('button', { name: 'Revenir au code de vérification' })), '« Revenir au code de vérification » still there')
    await shot(pg, 'A2-recovery-1440')
    await pg.context().close()
  })
  await step('C4', async () => {
    const pg = await (await ctxOf({ width: 320, height: 640 })).newPage()
    const line = await recoveryLine(pg)
    if (!line) return bad('C4', 'no line at 320')
    await line.scrollIntoViewIfNeeded()
    check('C4', !(await overflowX(pg)), '320: no horizontal page scroll')
    const rects = await line.locator('a').evaluate((a) => a.getClientRects().length)
    check('C4', rects === 1, '320: phone number on one line', `${rects} line boxes`)
    await shot(pg, 'C4-recovery-320')
    await pg.context().close()
  })

  // ── B4 / B5 / B6 — the other answers of auth/mode (route) ───────────────────────────────────────────────
  const withMode = async (id, edit) => {
    const ctx = await ctxOf()
    await ctx.route('**/api/auth/mode*', async (route) => {
      if (edit === 'abort') return route.abort()
      const res = await route.fetch()
      const body = await res.json()
      edit(body)
      return route.fulfill({ response: res, json: body })
    })
    const pg = await ctx.newPage()
    const line = await recoveryLine(pg)
    const text = line ? (await line.innerText()).replace(/\s+/g, ' ').trim() : null
    const back = await visible(pg.getByRole('button', { name: 'Revenir au code de vérification' }))
    await shot(pg, `${id}-recovery-1440`)
    await ctx.close()
    return { text, back }
  }
  await step('B4', async () => {
    const r = await withMode('B4', (b) => { b.vendorResetsSecondFactor = false; b.supportPhone = null })
    check('B4', r.text === "Pas de code de récupération ? Un administrateur de votre cabinet peut réinitialiser votre second facteur. Sinon, la personne qui a installé le logiciel peut le faire depuis le PC serveur du cabinet.", 'LAN: server-PC sentence, no phone', r.text)
  })
  await step('B5', async () => {
    const r = await withMode('B5', (b) => { delete b.vendorResetsSecondFactor; delete b.supportPhone })
    check('B5', r.text === 'Pas de code de récupération ? Un administrateur de votre cabinet peut réinitialiser votre second facteur.', 'older API: colleague sentence only', r.text)
  })
  await step('B6', async () => {
    const r = await withMode('B6', 'abort')
    check('B6', r.text === null, 'failed probe: no line', r.text)
    check('B6', r.back, '« Revenir au code de vérification » still there')
  })
} catch (e) {
  bad('RUN', 'walk aborted', String(e.message ?? e).split('\n')[0])
  await shot(page, 'RUN-aborted').catch(() => {})
  if (rossPage) await shot(rossPage, 'RUN-aborted-doctor').catch(() => {})
}

await browser.close()
writeFileSync(path.join(HERE, 'walk-rescue-results.json'), JSON.stringify({ stamp, results }, null, 2))
const pass = results.filter((r) => r.outcome === 'pass').length
const skips = results.filter((r) => r.outcome === 'skip').length
console.log(`\n${pass} ✅, ${findings.length} ❌, ${skips} ⏭`)
findings.length ? findings.forEach((f) => console.log(f)) : console.log('ALL CLEAR')
