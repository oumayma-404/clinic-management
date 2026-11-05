// QA walk — guided-first-days part C (colleague invited by e-mail). One launch, every scenario of qa/c/plan.md.
// Run: PW_DIR=<dir holding node_modules/playwright-core + otplib> node walk.mjs
// Stack: API :5097, web :3097, database clinic_gfd_c (throwaway), shared mailpit (read only).
// ⚠️ One drive makes ~20 anonymous auth calls and the limit is 30 per 5 minutes per address: leave 5 minutes between drives.
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
  execSync('docker exec -i clinic-postgres psql -U clinic_user -d clinic_gfd_c -tA -F "|"', { input: q }).toString().trim()
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const q = (s) => s.replace(/'/g, "''")

const stamp = Date.now().toString(36)
const admin = {
  email: `qa-c-${stamp}@clinic.local`,
  password: 'Un-mot-de-passe-QA-2026!',
  clinic: `Cabinet C ${stamp}`,
  fullName: 'Dr QA Geller',
}
const amel = { email: `amel-${stamp}@clinic.local`, name: 'Amel Ben Salah', password: 'Mon-mot-de-passe-Amel-26' }
const ross = { email: `ross-${stamp}@clinic.local`, name: 'Dr Ross QA' }
const sami = { email: `sami-${stamp}@clinic.local`, name: 'Sami QA' }
const state = { secret: null, lastCode: null }

async function freshCode() {
  for (;;) {
    const code = authenticator.generate(state.secret)
    if (code !== state.lastCode) { state.lastCode = code; return code }
    await sleep(2000)
  }
}
const shot = (page, name) => page.screenshot({ path: path.join(SHOTS, `${name}.png`), fullPage: false })
const overflowX = (page) => page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1)

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
const linkIn = (m) => (m.full.Text + m.full.HTML).match(/https?:\/\/[^\s"'<>]+\/invitation#t=[A-Za-z0-9_%-]+/)?.[0] ?? null

async function signIn(page, email, password) {
  await page.goto(`${WEB}/login`)
  await page.locator('#email').fill(email)
  await page.locator('#password').fill(password)
  await page.getByRole('button', { name: 'Se connecter', exact: true }).click()
}

const userRow = (email) => sql(`SELECT "MustChangePassword", coalesce("LastLoginAt"::text,'NULL') FROM "Users" WHERE lower("Email") = lower('${q(email)}')`)
const invitationRow = (email) =>
  sql(`SELECT round(extract(epoch from (i."ExpiresAtUtc" - now())) / 86400), coalesce(i."ConsumedAtUtc"::text,'NULL') FROM "StaffInvitations" i JOIN "Users" u ON u."Id" = i."UserId" WHERE lower(u."Email") = lower('${q(email)}')`)

const usersRow = (page, email) => page.locator('tr').filter({ hasText: email })
const dialog = (page, name) => page.getByRole('dialog', { name })

/** Opens « Inviter un collègue », fills it, sends; returns the link shown in the result dialog (or null). */
async function invite(page, person, role = 'secretary') {
  await page.getByRole('button', { name: 'Inviter un collègue' }).click()
  const d = dialog(page, 'Inviter un collègue')
  await d.waitFor({ timeout: 10000 })
  await d.locator('#create-user-fullname').fill(person.name)
  await d.locator('#create-user-email').fill(person.email)
  if (role === 'doctor') {
    await d.locator('#create-user-role').click()
    await page.getByRole('option', { name: 'Médecin' }).click()
    await d.locator('#create-user-firstname').fill('Ross')
    await d.locator('#create-user-lastname').fill('QA')
  }
  await d.getByRole('button', { name: "Envoyer l'invitation" }).click()
}
async function resultLink(page) {
  const d = page.getByRole('dialog').filter({ has: page.getByRole('button', { name: /Copier le lien|Copié/ }) })
  await d.waitFor({ timeout: 15000 })
  return { d, link: (await d.locator('code').innerText()).trim(), title: (await d.getByRole('heading').first().innerText()).trim(), text: await d.innerText() }
}

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const desktop = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
const page = await desktop.newPage()
const fresh = async (viewport = { width: 1440, height: 900 }) => (await browser.newContext({ viewport, locale: 'fr-FR' })).newPage()

const links = {}

try {
  // ── Setup: a cabinet and its admin with a second factor ───────────────────────────────────────────────
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
  console.log('admin signed in')

  // ── D5 — `?invite=1` opens the dialog in invite wording ─────────────────────────────────────────────────
  await step('D5', async () => {
    await page.goto(`${WEB}/users?invite=1`)
    const d = dialog(page, 'Inviter un collègue')
    await d.waitFor({ timeout: 40000 })
    const role = await d.locator('#create-user-role').innerText()
    check('D5', role.includes('Secrétaire'), '?invite=1 opens « Inviter un collègue » on Secrétaire', role)
    check('D5', !page.url().includes('invite='), '?invite removed from the address', page.url())
    await d.getByRole('button', { name: 'Annuler' }).click()
  })

  // ── A1 — wording ────────────────────────────────────────────────────────────────────────────────────────
  await step('A1', async () => {
    await page.goto(`${WEB}/users`)
    const btn = page.getByRole('button', { name: 'Inviter un collègue' })
    await btn.waitFor({ timeout: 40000 })
    ok('A1', 'header button « Inviter un collègue »')
    await btn.click()
    const d = dialog(page, 'Inviter un collègue')
    await d.waitFor({ timeout: 10000 })
    const text = await d.innerText()
    check('A1', text.includes('APEXA lui envoie un e-mail'), 'description says APEXA e-mails her', text.replace(/\n/g, ' | '))
    check('A1', (await d.getByRole('button', { name: "Envoyer l'invitation" }).count()) === 1, 'submit « Envoyer l\'invitation »')
    await shot(page, 'A1-invite-dialog-1440')
    await d.getByRole('button', { name: 'Annuler' }).click()
  })

  // ── A2 — invite Amel ────────────────────────────────────────────────────────────────────────────────────
  await step('A2', async () => {
    await invite(page, amel)
    const r = await resultLink(page)
    links.amelShown = r.link
    check('A2', r.title === 'Invitation envoyée', 'title « Invitation envoyée »', r.title)
    check('A2', r.text.includes(amel.email), 'names the address')
    check('A2', r.link.startsWith(`${WEB}/invitation#t=`), 'link to /invitation#t=…', r.link)
    check('A2', (await page.getByRole('dialog', { name: 'Mot de passe temporaire' }).count()) === 0, 'no temporary-password dialog')
    await shot(page, 'A2-invitation-sent-1440')
    await r.d.getByRole('button', { name: 'Terminé' }).click()
  })

  // ── A3 — rows ───────────────────────────────────────────────────────────────────────────────────────────
  await step('A3', async () => {
    const u = userRow(amel.email)
    check('A3', u === 'f|NULL', 'MustChangePassword false, never signed in', u)
    const i = invitationRow(amel.email)
    check('A3', i === '7|NULL', 'one invitation, ~7 days, not consumed', i)
  })

  // ── A4 — the e-mail ─────────────────────────────────────────────────────────────────────────────────────
  await step('A4', async () => {
    const m = (await mails(amel.email, 'vous invite sur APEXA'))[0]
    if (!m) return bad('A4', 'no invitation e-mail')
    const body = m.full.Text
    check('A4', m.meta.Subject === `${admin.fullName} vous invite sur APEXA`, 'subject « <admin> vous invite sur APEXA »', m.meta.Subject)
    check('A4', body.includes('Choisir mon mot de passe') && body.includes(amel.email) && body.includes('valable 7 jours'), 'action, identifiant, 7 days', body.slice(0, 400).replace(/\n/g, ' | '))
    links.amel = linkIn(m)
    check('A4', links.amel === links.amelShown, 'mailed link = link shown', `${links.amel} vs ${links.amelShown}`)
  })

  // ── A5 — the row ───────────────────────────────────────────────────────────────────────────────────────
  await step('A5', async () => {
    const row = usersRow(page, amel.email)
    await row.waitFor({ timeout: 10000 })
    const text = await row.innerText()
    check('A5', text.includes('Invitation envoyée'), 'chip « Invitation envoyée »', text.replace(/\n/g, ' | '))
    check('A5', (await row.getByRole('button', { name: "Renvoyer l'e-mail" }).count()) === 1 && (await row.getByRole('button', { name: /Réinitialiser/ }).count()) === 0,
      '« Renvoyer l\'e-mail » replaces « Réinitialiser »')
    check('A5', text.includes('Jamais'), 'last sign-in « Jamais »')
  })

  // ── C1 — the page at three sizes (read only, nothing spent) — uses a separate invite ──────────────────
  await step('C1', async () => {
    await invite(page, sami)
    const r = await resultLink(page)
    await r.d.getByRole('button', { name: 'Terminé' }).click()
    for (const vp of [{ width: 320, height: 640 }, { width: 390, height: 844 }, { width: 1536, height: 730 }]) {
      const p = await fresh(vp)
      await p.goto(r.link)
      await p.getByText('Bienvenue, Sami QA').waitFor({ timeout: 30000 })
      const btn = p.getByRole('button', { name: 'Enregistrer mon mot de passe' })
      const box = await btn.boundingBox()
      check('C1', !(await overflowX(p)) && box && box.x >= 0 && box.x + box.width <= vp.width, `${vp.width}×${vp.height}: no overflow, button inside`, JSON.stringify(box))
      await shot(p, `C1-invitation-${vp.width}`)
      await p.context().close()
    }
    check('C1', invitationRow(sami.email).endsWith('|NULL'), 'reading the page spent nothing')
  })

  // ── B2 / B3 ─────────────────────────────────────────────────────────────────────────────────────────────
  await step('B2', async () => {
    const p = await fresh()
    await p.goto(`${WEB}/invitation`)
    await p.getByText('Lien incomplet').waitFor({ timeout: 30000 })
    ok('B2', 'no fragment → « Lien incomplet »')
    await p.context().close()
  })
  await step('B3', async () => {
    const p = await fresh()
    await p.goto(`${WEB}/invitation#t=garbage-token`)
    await p.getByText('Lien expiré').waitFor({ timeout: 30000 })
    const t = await p.locator('body').innerText()
    check('B3', t.includes("n'est plus valable"), 'garbage token → « Lien expiré » + the server sentence', t.slice(0, 200))
    await p.context().close()
  })

  // ── A6 / B4 / A7 — Amel chooses her password ────────────────────────────────────────────────────────────
  const amelPage = await fresh()
  await step('A6', async () => {
    await amelPage.goto(links.amel)
    await amelPage.getByText(`Bienvenue, ${amel.name}`).waitFor({ timeout: 30000 })
    ok('A6', `greets « Bienvenue, ${amel.name} »`)
    const desc = await amelPage.locator('body').innerText()
    check('A6', desc.includes(`${admin.clinic} vous a créé un compte APEXA`), 'names the cabinet')
    const idf = amelPage.locator('#invitation-email')
    check('A6', (await idf.inputValue()) === amel.email && (await idf.getAttribute('readonly')) !== null, 'identifiant = address, read-only')
    check('A6', desc.includes('au moins 12 caractères'), 'label states the served floor')
    check('A6', !amelPage.url().includes('#t='), 'token erased from the address bar', amelPage.url())
    await shot(amelPage, 'A6-invitation-1440')
  })
  await step('B4', async () => {
    await amelPage.locator('#invitation-password').fill(amel.password)
    await amelPage.locator('#invitation-confirm').fill(amel.password + 'x')
    await amelPage.getByRole('button', { name: 'Enregistrer mon mot de passe' }).click()
    await amelPage.getByText('Les deux mots de passe ne correspondent pas.').waitFor({ timeout: 5000 })
    ok('B4', 'mismatch refused')
    await amelPage.locator('#invitation-password').fill('court')
    await amelPage.locator('#invitation-confirm').fill('court')
    await amelPage.getByRole('button', { name: 'Enregistrer mon mot de passe' }).click()
    await amelPage.getByText('Le mot de passe doit contenir au moins 12 caractères.').waitFor({ timeout: 5000 })
    check('B4', (await amelPage.locator('#invitation-password').inputValue()) === 'court', 'too short refused, value kept')
    check('B4', invitationRow(amel.email).endsWith('|NULL'), 'link not consumed by the refusals')
  })
  await step('A7', async () => {
    await amelPage.locator('#invitation-password').fill(amel.password)
    await amelPage.locator('#invitation-confirm').fill(amel.password)
    await amelPage.getByRole('button', { name: 'Enregistrer mon mot de passe' }).click()
    await amelPage.waitForURL('**/login**', { timeout: 30000 })
    await amelPage.getByText('Mot de passe enregistré').waitFor({ timeout: 15000 })
    check('A7', (await amelPage.locator('#email').inputValue()) === amel.email, 'login pre-filled with her address')
    await shot(amelPage, 'A7-login-prefilled')
    await amelPage.locator('#password').fill(amel.password)
    await amelPage.getByRole('button', { name: 'Se connecter', exact: true }).click()
    await amelPage.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30000 })
    ok('A7', `signed in → ${new URL(amelPage.url()).pathname}`)
  })
  await amelPage.context().close()

  // ── A8 — after her first sign-in ───────────────────────────────────────────────────────────────────────
  await step('A8', async () => {
    check('A8', invitationRow(amel.email).split('|')[1] !== 'NULL', 'invitation consumed', invitationRow(amel.email))
    await page.goto(`${WEB}/users`)
    const row = usersRow(page, amel.email)
    await row.waitFor({ timeout: 30000 })
    const text = await row.innerText()
    check('A8', !text.includes('Invitation envoyée') && !text.includes('Jamais'), 'no chip, a last sign-in', text.replace(/\n/g, ' | '))
    check('A8', (await row.getByRole('button', { name: /Réinitialiser le mot de passe/ }).count()) === 1, '« Réinitialiser » back')
  })

  // ── B1 — the spent link ─────────────────────────────────────────────────────────────────────────────────
  await step('B1', async () => {
    const p = await fresh()
    await p.goto(links.amel)
    await p.getByText('Lien expiré').waitFor({ timeout: 30000 })
    const login = p.getByRole('link', { name: /se connecter/ })
    check('B1', (await login.getAttribute('href')) === '/login', 'spent link → « Lien expiré » + « se connecter »')
    await shot(p, 'B1-spent-link')
    await p.context().close()
  })

  // ── D3 — reset still offered once she has signed in ───────────────────────────────────────────────────
  await step('D3', async () => {
    const row = usersRow(page, amel.email)
    await row.getByRole('button', { name: /Réinitialiser le mot de passe/ }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Confirmer' }).click()
    const d = page.getByRole('dialog', { name: 'Mot de passe temporaire' })
    await d.waitFor({ timeout: 15000 })
    const pw = (await d.locator('code').innerText()).trim()
    check('D3', pw.length >= 8, 'reset → « Mot de passe temporaire » with a password')
    await d.getByRole('button', { name: 'Terminé' }).click()
  })

  // ── B5 / B6 / C2 — doctor invite, then « Renvoyer l'e-mail » at 390 ────────────────────────────────────
  await step('B5', async () => {
    await invite(page, ross, 'doctor')
    const first = await resultLink(page)
    links.rossOld = first.link
    await first.d.getByRole('button', { name: 'Terminé' }).click()
    check('B6', sql(`SELECT count(*) FROM "Doctors" d JOIN "Users" u ON u."Id" = d."UserId" WHERE lower(u."Email") = lower('${q(ross.email)}')`) === '1', 'the doctor has a practitioner record')

    await page.setViewportSize({ width: 390, height: 844 })
    await page.reload()
    // C3 — card tree at 390
    const card = page.locator('li').filter({ hasText: ross.email }).first()
    await card.waitFor({ timeout: 30000 })
    check('C3', (await card.innerText()).includes('Invitation envoyée'), 'card shows the chip at 390')
    await card.getByRole('button', { name: /Actions pour/ }).click()
    const item = page.getByRole('menuitem', { name: "Renvoyer l'e-mail" })
    check('C3', (await item.count()) === 1 && (await page.getByRole('menuitem', { name: /Réinitialiser/ }).count()) === 0, 'menu offers « Renvoyer l\'e-mail », not « Réinitialiser »')
    await item.click()
    const confirm = page.getByRole('alertdialog')
    await confirm.waitFor({ timeout: 5000 })
    const ct = await confirm.innerText()
    check('B5', ct.includes("Renvoyer l'invitation ?") && ct.includes(ross.email) && ct.includes("L'ancien lien ne fonctionnera plus."), 'confirm names the address and the old link', ct.replace(/\n/g, ' | '))
    await confirm.getByRole('button', { name: 'Confirmer' }).click()
    const second = await resultLink(page)
    links.rossNew = second.link
    check('B5', second.link !== links.rossOld && second.title === 'Invitation envoyée', 'a new link', `${second.link === links.rossOld}`)
    // C2 — the dialog at 390
    const box = await second.d.getByRole('button', { name: /Copier le lien/ }).boundingBox()
    const dbox = await second.d.boundingBox()
    check('C2', !(await overflowX(page)) && box && dbox && box.x + box.width <= dbox.x + dbox.width + 1, '390: link wraps, « Copier le lien » inside, no overflow', JSON.stringify({ box, dbox }))
    await shot(page, 'C2-resend-390')
    await second.d.getByRole('button', { name: 'Terminé' }).click()
    await page.setViewportSize({ width: 1440, height: 900 })

    const p = await fresh()
    await p.goto(links.rossOld)
    await p.getByText('Lien expiré').waitFor({ timeout: 30000 })
    ok('B5', 'old link → « Lien expiré »')
    await p.goto('about:blank')
    await p.goto(links.rossNew)
    await p.getByText(`Bienvenue, ${ross.name}`).waitFor({ timeout: 30000 })
    ok('B5', `new link greets « Bienvenue, ${ross.name} »`)
    await p.context().close()
    const ms = await mails(ross.email, 'vous invite sur APEXA', 2)
    check('B5', ms.length === 2, 'two invitation e-mails', String(ms.length))
  })

  // ── B9 — duplicate address ─────────────────────────────────────────────────────────────────────────────
  await step('B9', async () => {
    await page.goto(`${WEB}/users`)
    await page.getByRole('button', { name: 'Inviter un collègue' }).waitFor({ timeout: 30000 })
    await invite(page, { name: 'Amel bis', email: amel.email })
    const d = dialog(page, 'Inviter un collègue')
    await d.getByRole('alert').first().waitFor({ timeout: 10000 })
    const msg = await d.getByRole('alert').first().innerText()
    check('B9', (await d.locator('#create-user-email').inputValue()) === amel.email, 'refused in the dialog, fields kept', msg)
    await d.getByRole('button', { name: 'Annuler' }).click()
  })

  // ── B7 — the mail did not leave (response rewritten) ───────────────────────────────────────────────────
  await step('B7', async () => {
    await page.route('**/api/users', async (route) => {
      if (route.request().method() !== 'POST') return route.continue()
      const res = await route.fetch()
      const json = await res.json()
      if (json && typeof json === 'object') json.invitationEmailed = false
      await route.fulfill({ response: res, json })
    })
    await invite(page, { name: 'Nour QA', email: `nour-${stamp}@clinic.local` })
    const r = await resultLink(page)
    check('B7', r.title === "L'e-mail n'est pas parti" && r.text.includes('existe') && r.text.includes('WhatsApp') && r.link.includes('/invitation#t='),
      'title « L\'e-mail n\'est pas parti », the account exists, copy the link', r.text.replace(/\n/g, ' | '))
    await shot(page, 'B7-not-sent-1440')
    await r.d.getByRole('button', { name: 'Terminé' }).click()
    await page.unroute('**/api/users')
  })

  // ── B8 — mode probe fails: wording falls back, the response still decides ─────────────────────────────
  await step('B8', async () => {
    const p = await desktop.newPage()
    await p.route('**/api/auth/mode', (route) => route.fulfill({ status: 500, body: '{}' }))
    await p.goto(`${WEB}/users`)
    const btn = p.getByRole('button', { name: 'Créer un compte' })
    await btn.waitFor({ timeout: 40000 })
    ok('B8', 'probe failed → « Créer un compte »')
    await btn.click()
    const d = p.getByRole('dialog', { name: 'Créer un compte' })
    await d.locator('#create-user-fullname').fill('Lina QA')
    await d.locator('#create-user-email').fill(`lina-${stamp}@clinic.local`)
    await d.getByRole('button', { name: 'Créer le compte' }).click()
    const r = await resultLink(p)
    check('B8', r.link.includes('/invitation#t='), 'the create still opened the invitation dialog', r.title)
    await p.close()
  })

  skip('B10', 'needs a second API (LAN profile, or no FrontendUrl); held by StaffInvitationTests')

  // ── B11 — a refused read names the server's reason, not « vérifiez votre connexion » (run 2's F-2) ──────
  await step('B11', async () => {
    const p = await fresh()
    await p.route('**/api/auth/invitation/preview', (route) =>
      route.fulfill({ status: 429, contentType: 'application/json', body: JSON.stringify({ error: 'Trop de tentatives. Réessayez dans 2 minutes.' }) }))
    await p.goto(`${WEB}/invitation#t=whatever`)
    await p.getByText("L'invitation n'a pas pu être lue.").waitFor({ timeout: 30000 })
    const t = await p.locator('body').innerText()
    check('B11', t.includes('Trop de tentatives') && !t.includes('Vérifiez votre connexion') && (await p.getByRole('button', { name: /Réessayer/ }).count()) === 1,
      '429 → the server sentence + « Réessayer »', t.slice(0, 200).replace(/\n/g, ' | '))
    await p.context().close()
  })

  // ── D1 / D2 / D4 — signed out ─────────────────────────────────────────────────────────────────────────
  await step('D1', async () => {
    const p = await fresh()
    await p.goto(`${WEB}/login`)
    await p.locator('#email').waitFor({ timeout: 30000 })
    const t = await p.locator('body').innerText()
    check('D1', (await p.locator('#email').inputValue()) === '' && !t.includes('Mot de passe enregistré'), 'plain /login: empty, no banner')
    await p.goto(`${WEB}/login?email=x%40y.tn`)
    await p.locator('#email').waitFor({ timeout: 30000 })
    await sleep(500)
    check('D2', (await p.locator('#email').inputValue()) === 'x@y.tn' && (await p.locator('#password').count()) === 1, '?email= prefilled in password mode')
    await p.goto(`${WEB}/reinitialiser-mot-de-passe`)
    await sleep(2000)
    check('D4', new URL(p.url()).pathname === '/reinitialiser-mot-de-passe', 'reset page stays public', p.url())
    await p.context().close()
  })
} catch (e) {
  bad('SETUP', 'threw', String(e.message ?? e).split('\n')[0])
} finally {
  await browser.close()
  writeFileSync(path.join(HERE, 'walk-results.json'), JSON.stringify({ stamp, results, findings }, null, 2))
  console.log(findings.length ? `\n${findings.length} finding(s)` : '\nALL CLEAR')
  findings.forEach((f) => console.log(f))
}
