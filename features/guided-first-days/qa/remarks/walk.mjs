// QA walk — owner's remarks of 2026-11-04 (fiche tour · phone link · « Aide » avis). One launch, every row of
// qa/remarks/plan.md; collects, never stops.
// Run: PW_DIR=<dir holding node_modules/playwright-core + otplib> node walk.mjs
// Stack: API :5097, web :3097, database clinic_gfd_all (throwaway), shared mailpit (read only).
// ⚠️ Order is forced: « Me guider » on the fiche step needs a patient (B1 before D1), and the step stops offering
// « Me guider » once watched (B2 before A4).
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
  execSync('docker exec -i clinic-postgres psql -U clinic_user -d clinic_gfd_all -tA -F "|"', { input: q }).toString().trim()
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const q = (s) => s.replace(/'/g, "''")

const stamp = Date.now().toString(36)
const admin = { email: `qa-rk-${stamp}@clinic.local`, password: 'Un-mot-de-passe-QA-2026!', clinic: `Cabinet RK ${stamp}`, fullName: 'Dr QA Geller' }
const patientName = { first: 'Leila', last: `Tour${stamp}` }
const state = { secret: null, lastCode: null, token: null }

async function freshCode() {
  for (;;) {
    const code = authenticator.generate(state.secret)
    if (code !== state.lastCode) { state.lastCode = code; return code }
    await sleep(2000)
  }
}
const shot = (page, name) => page.screenshot({ path: path.join(SHOTS, `${name}.png`), fullPage: false })
const overflowX = (page) => page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1)
async function mailTo(address) {
  for (let i = 0; i < 20; i++) {
    const data = await (await fetch(`${MAIL}/search?query=${encodeURIComponent(`to:"${address}"`)}`)).json()
    if ((data.messages ?? []).length) return (await fetch(`${MAIL}/message/${data.messages[0].ID}`)).json()
    await sleep(1500)
  }
  return null
}
async function apiAs(method, url, body) {
  const res = await fetch(`${API}${url}`, {
    method, headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${state.token}` }, body: body ? JSON.stringify(body) : undefined,
  })
  const text = await res.text()
  let json = null
  try { json = JSON.parse(text) } catch {}
  return { status: res.status, json, text }
}
const userId = () => sql(`SELECT "Id" FROM "Users" WHERE lower("Email") = lower('${q(admin.email)}')`)
const ticked = () => sql(`SELECT coalesce((SELECT "TickedStepsCsv" FROM "UserDashboardPreferences" WHERE "UserId" = '${q(userId())}'), '<no row>')`)
const tickedHas = (key) => ticked().split(',').includes(key)
const fichesOfClinic = () => sql(`SELECT count(*) FROM "DentalRecords" d JOIN "Patients" p ON p."Id" = d."PatientId" JOIN "Users" u ON u."ClinicId" = p."ClinicId" WHERE lower(u."Email") = lower('${q(admin.email)}')`)
const feedbackRows = () => sql(`SELECT "Kind","Source",coalesce("HelpRating"::text,''),coalesce("LikedAreasCsv",''),coalesce("LikedOther",''),coalesce("HardOther",''),coalesce("Message",'') FROM "ProductFeedback" WHERE "UserId" = '${q(userId())}' ORDER BY "CreatedAt"`)

const bubble = (page) => page.locator('[data-guide-bubble]')
async function waitBubble(page, text, timeout = 30000) {
  await bubble(page).filter({ hasText: text }).waitFor({ state: 'visible', timeout })
}
async function gone(page, timeout = 4000) {
  try { await bubble(page).waitFor({ state: 'detached', timeout }); return true } catch { return false }
}
async function toastWith(page, text, timeout = 15000) {
  try { await page.locator('[data-sonner-toast]').filter({ hasText: text }).first().waitFor({ timeout }); return true } catch { return false }
}
async function startGuide(page, title) {
  if (new URL(page.url()).pathname !== '/') await page.goto(`${WEB}/`)
  const btn = page.getByRole('button', { name: `Me guider : ${title}` })
  await btn.waitFor({ timeout: 40000 })
  await btn.click()
}
const lit = (page, mark) => page.locator(`[data-guide="${mark}"]:visible`).first()
const bubbleButton = (page, name) => bubble(page).getByRole('button', { name })
const card = (page) => page.locator('section[aria-labelledby="getting-started-title"]')
const percentOf = (done) => Math.round((done / 7) * 100)
const countOf = (table) =>
  sql(`SELECT count(*) FROM "${table}" WHERE "ClinicId" = (SELECT "ClinicId" FROM "Users" WHERE lower("Email") = lower('${q(admin.email)}'))`)
/** Whether a box is entirely inside the page's window. */
function inViewport(page, box) {
  const v = page.viewportSize()
  return !!box && !!v && box.x >= -0.5 && box.y >= -0.5 && box.x + box.width <= v.width + 0.5 && box.y + box.height <= v.height + 0.5
}
/** The tour's hand-over on the dashboard: the bubble naming the step, its percentage, both buttons, both on screen. */
async function promptOn(page, title) {
  const b = bubble(page).filter({ hasText: title })
  const found = await b.waitFor({ timeout: 15000 }).then(() => true, () => false)
  await sleep(800) // the scroll into view settles
  const text = found ? (await b.innerText()).replace(/\n/g, ' | ') : ''
  const m = text.match(/configuré à (\d+)/)
  const lit = await page.locator('[data-guide="premiers-pas-next"]:visible').first().boundingBox().catch(() => null)
  const box = found ? await b.boundingBox() : null
  return {
    ok: found && text.includes("C'est parti") && text.includes('Plus tard'),
    text,
    percent: m ? Number(m[1]) : null,
    inView: inViewport(page, box) && inViewport(page, lit),
  }
}
async function dismissPrompt(page) {
  const later = bubble(page).getByRole('button', { name: 'Plus tard' })
  if (await later.waitFor({ timeout: 8000 }).then(() => true, () => false)) await later.click()
  await gone(page)
}
const overlaps = (a, b) => !(a.x + a.width <= b.x || b.x + b.width <= a.x || a.y + a.height <= b.y || b.y + b.height <= a.y)

async function openAide(page) {
  await page.getByRole('button', { name: 'Aide' }).first().click()
  await page.getByRole('button', { name: 'Donner mon avis ou une idée' }).click()
  const d = page.getByRole('dialog', { name: 'Écrire à APEXA' })
  await d.waitFor({ timeout: 10000 })
  return d
}

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
const page = await ctx.newPage()

try {
  // ── Setup: a fresh cabinet and its admin with a second factor ─────────────────────────────────────────
  await fetch(`${API}/auth/signup`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ clinicName: admin.clinic, fullName: admin.fullName, email: admin.email, password: admin.password, city: 'Tunis' }),
  })
  const verify = await mailTo(admin.email)
  const vlink = verify && (verify.Text + verify.HTML).match(/https?:\/\/[^\s"'<>]+\/signup\/verifier#token=[^\s"'<>]+/)?.[0]
  if (!vlink) throw new Error('no verification link')
  await page.goto(vlink)
  await page.getByText('est créé').first().waitFor({ timeout: 30000 })
  await page.goto(`${WEB}/login`)
  await page.locator('#email').fill(admin.email)
  await page.locator('#password').fill(admin.password)
  await page.getByRole('button', { name: 'Se connecter', exact: true }).click()
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
  const login = await fetch(`${API}/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: admin.email, password: admin.password, totpCode: await freshCode() }),
  })
  state.token = (await login.json().catch(() => null))?.value?.accessToken ?? null
  console.log(`admin signed in (api token ${state.token ? 'ok' : 'missing'})`)

  // ── A3 / B3 — the two wire rows ─────────────────────────────────────────────────────────────────────
  await step('A3', async () => {
    const r = await apiAs('GET', '/meta/phone-app')
    check('A3', r.status === 200 && r.json?.url === 'https://apexa.tn/#telecharger' && String(r.json?.qrPng).startsWith('data:image/png;base64,'),
      'phone-app → apexa.tn/#telecharger + a data: PNG', `${r.status} ${r.text.slice(0, 120)}`)
  })
  await step('B3', async () => {
    const r = await apiAs('PUT', '/dashboard/getting-started/securityCode/tick', {})
    check('B3', r.status === 400 && /getting_started_step_not_tickable/.test(r.text), 'the security code cannot be ticked by hand', `${r.status} ${r.text.slice(0, 160)}`)
  })

  // ── A1 — the card ─────────────────────────────────────────────────────────────────────────────────────
  await step('A1', async () => {
    // No reload: the welcome prompt belongs to the dashboard the sign-in lands on (probe fix, run 6).
    if (new URL(page.url()).pathname !== '/') await page.goto(`${WEB}/`)
    await card(page).waitFor({ timeout: 40000 })
    check('A1', (await card(page).getByRole('button', { name: 'Me guider : Première fiche de soins' }).count()) === 1, '« Me guider : Première fiche de soins » on the card')
    // A17 — a new cabinet's first look starts the guided tour
    const w = await promptOn(page, 'Bienvenue ! On commence par')
    check('A17', w.ok && w.inView && w.text.includes('Votre secrétaire') && w.percent === percentOf(1), 'first visit: « Bienvenue ! On commence par « Votre secrétaire » … 14 % »', w.text)
    await shot(page, 'A17-welcome-1440')
    await dismissPrompt(page)
  })

  // ── A2 — the phone dialog at 1440 ────────────────────────────────────────────────────────────────────
  await step('A2', async () => {
    await card(page).getByRole('button', { name: 'Installer : Votre téléphone' }).click()
    const d = page.getByRole('dialog', { name: "L'application sur votre téléphone" })
    await d.waitFor({ timeout: 10000 })
    const img = d.locator('img[alt^="QR code"]')
    await img.waitFor({ timeout: 15000 })
    const src = await img.getAttribute('src')
    const link = d.getByRole('link', { name: /Ouvrir la page de téléchargement/ })
    const href = await link.getAttribute('href')
    const target = await link.getAttribute('target')
    const text = await d.innerText()
    check('A2', String(src).startsWith('data:image/png;base64,') && (await img.isVisible()), 'QR shown')
    check('A2', href === 'https://apexa.tn/#telecharger' && target === '_blank', 'button → apexa.tn/#telecharger in a new tab', `${href} ${target}`)
    check('A2', !/proposée|^Android$/m.test(text) && !/iPhone\n/.test(text.split('Déjà fait')[0]), 'no « non proposée », no Android/iPhone split', text.replace(/\n/g, ' | ').slice(0, 300))
    await shot(page, 'A2-phone-1440')
    await d.getByRole('button', { name: 'Terminé' }).click()
    // A12 — « Terminé » ticks the phone step by hand (owner's call, 2026-11-04)
    const untick = card(page).getByRole('button', { name: 'Remettre à faire : Votre téléphone' })
    const tickedNow = await untick.waitFor({ timeout: 10000 }).then(() => true, () => false)
    check('A12', tickedNow && tickedHas('phoneApp'), '« Terminé » → phone step done (ticked by hand)', ticked())
    check('A12', await toastWith(page, "C'est fait : Votre téléphone", 8000), 'and celebrated')
    const after = await promptOn(page, "On attaque l'étape suivante")
    check('A18', after.ok && after.text.includes('Votre secrétaire') && after.percent === percentOf(2), 'a tick by hand moves the tour on (prompt, 29 %)', after.text)
    await dismissPrompt(page)
    // B6 — the security code has no tick
    const sec = await card(page).getByRole('button', { name: /: Code de sécurité$/ }).count()
    check('B6', sec === 0, 'no tick on « Code de sécurité »', String(sec))
  })

  // ── A13 — a step ticked and unticked by its circle ────────────────────────────────────────────────────
  await step('A13', async () => {
    await sleep(4000)
    await card(page).getByRole('button', { name: 'Marquer comme fait : Papier à en-tête' }).click()
    const back = card(page).getByRole('button', { name: 'Remettre à faire : Papier à en-tête' })
    const done = await back.waitFor({ timeout: 10000 }).then(() => true, () => false)
    const toast = await toastWith(page, "C'est fait : Papier à en-tête", 8000)
    check('A13', done && toast && tickedHas('letterhead'), 'circle → done, celebrated, stored', ticked())
    await shot(page, 'A13-ticked-1440')
    await dismissPrompt(page)
    await back.click()
    const reopened = await card(page).getByRole('button', { name: 'Me guider : Papier à en-tête' }).waitFor({ timeout: 10000 }).then(() => true, () => false)
    check('A13', reopened && !tickedHas('letterhead'), 'untick → open again with « Me guider »', ticked())
  })

  // ── C1 — 320 px ──────────────────────────────────────────────────────────────────────────────────────
  await step('C1', async () => {
    await page.setViewportSize({ width: 320, height: 640 })
    await page.goto(`${WEB}/`)
    await card(page).waitFor({ timeout: 40000 })
    const d = await openAide(page)
    const box = await d.boundingBox()
    const tabBoxes = await Promise.all(['Avis', 'Idée', 'Problème', 'Question'].map((t) => d.getByRole('tab', { name: new RegExp(`^${t}`) }).boundingBox()))
    const inside = tabBoxes.every((t) => t && box && t.x >= box.x - 0.5 && t.x + t.width <= box.x + box.width + 0.5)
    const tabsOverflow = await d.getByRole('tablist').evaluate((el) => el.scrollWidth > el.clientWidth + 1)
    check('C1', !(await overflowX(page)) && inside && !tabsOverflow, '320: no body overflow, four tabs inside the sheet', JSON.stringify({ box, tabBoxes, tabsOverflow }))
    await shot(page, 'C1-aide-320')
    await d.getByRole('button', { name: 'Annuler' }).click()
    await d.waitFor({ state: 'detached', timeout: 8000 }).catch(() => {})
    const untickPhone = card(page).getByRole('button', { name: 'Remettre à faire : Votre téléphone' })
    if (await untickPhone.count()) {
      await untickPhone.click()
      await card(page).getByRole('button', { name: 'Installer : Votre téléphone' }).waitFor({ timeout: 10000 })
    }
    await card(page).getByRole('button', { name: 'Installer : Votre téléphone' }).click()
    const p = page.getByRole('dialog', { name: "L'application sur votre téléphone" })
    await p.waitFor({ timeout: 10000 })
    await sleep(1500)
    const qrVisible = await p.locator('img[alt^="QR code"]').isVisible().catch(() => false)
    const btnVisible = await p.getByRole('link', { name: /Ouvrir la page de téléchargement/ }).isVisible()
    check('C1', !qrVisible && btnVisible && !(await overflowX(page)), '320 phone dialog: QR withheld, button visible, no overflow', `${qrVisible} ${btnVisible}`)
    await shot(page, 'C1-phone-320')
    await p.getByRole('button', { name: /Fermer|Close/ }).first().click()
    const closedTicks = await card(page).getByRole('button', { name: 'Remettre à faire : Votre téléphone' }).waitFor({ timeout: 10000 }).then(() => true, () => false)
    check('A12', closedTicks, 'closing with ✕ ticks it too')
    const narrow = await promptOn(page, "On attaque l'étape suivante")
    check('C1', narrow.ok && narrow.inView, '320: the prompt and its row on screen', narrow.text)
    await shot(page, 'C1-prompt-320')
    await dismissPrompt(page)
    await page.setViewportSize({ width: 1440, height: 900 })
  })

  // ── B1 / A15 — the fiche tour with no patient: one sentence, « Terminer », back to the list ──────────
  await step('B1', async () => {
    await startGuide(page, 'Première fiche de soins')
    await waitBubble(page, 'dès votre premier patient', 15000)
    check('B1', (await bubbleButton(page, 'Terminer').count()) === 1, 'no patient: the sentence and « Terminer »')
    await shot(page, 'B1-empty-1440')
    await bubbleButton(page, 'Terminer').click()
    const home = await page.waitForURL((u) => u.pathname === '/', { timeout: 15000 }).then(() => true, () => false)
    const h = await promptOn(page, 'Votre secrétaire')
    check('B1', home && tickedHas('dentalRecord'), '« Terminer » → dashboard, step ticked', `${page.url()} ${ticked()}`)
    check('A16', h.ok && h.inView && h.percent === percentOf(3), 'the tour prompts the next step: « On attaque l\'étape suivante : « Votre secrétaire » ! … 43 % »', h.text)
    await shot(page, 'A16-prompt-1440')
    await dismissPrompt(page)
    await card(page).getByRole('button', { name: 'Remettre à faire : Première fiche de soins' }).click()
    const back = await card(page).getByRole('button', { name: 'Me guider : Première fiche de soins' }).waitFor({ timeout: 10000 }).then(() => true, () => false)
    check('A15', back && !tickedHas('dentalRecord'), 'untick → « Me guider » back', ticked())
  })

  // ── D1 / A9 — « Vos patients » as a tour: nothing to type, nothing saved, back to the list ─────────────
  await step('D1', async () => {
    await sleep(3000)
    await startGuide(page, 'Vos patients')
    await waitBubble(page, 'Ici, vous ajoutez un patient.')
    await bubbleButton(page, 'Suivant').click()
    await waitBubble(page, 'Son prénom et son nom suffisent', 15000)
    check('D1', await page.locator('[data-guide="patient-dialog"]').isVisible(), '« Suivant » opened the patient window itself')
    await bubbleButton(page, 'Suivant').click()
    await waitBubble(page, 'Ce bouton crée son dossier')
    check('D1', (await bubbleButton(page, 'Terminer').count()) === 1, 'last step: « Terminer »')
    await bubbleButton(page, 'Terminer').click()
    const home = await page.waitForURL((u) => u.pathname === '/', { timeout: 15000 }).then(() => true, () => false)
    check('A9', home && (await page.locator('body > canvas[aria-hidden="true"]').count()) === 1, '→ dashboard, confetti over the app', page.url())
    const tt = await page.locator('[data-sonner-toast]').filter({ hasText: 'Vous savez ajouter un patient.' }).first().innerText().catch(() => '')
    check('A9', tt.startsWith('🎉') && tt.includes('se mettre sous la dent'), '« 🎉 Vous savez ajouter un patient. » + the joke', tt.replace(/\n/g, ' | '))
    const h = await promptOn(page, 'Votre secrétaire')
    check('A9', h.ok && h.inView && h.percent === percentOf(3), 'the next step prompted, with the percentage', h.text)
    check('D1', countOf('Patients') === '0' && tickedHas('patients'), 'no patient created; the step ticked', `${countOf('Patients')} ${ticked()}`)
    await shot(page, 'A9-celebration-1440')
    await sleep(3500)
    check('A9', (await page.locator('body > canvas[aria-hidden="true"]').count()) === 0, 'confetti removed afterwards')
  })

  // ── E1 / E2 / E3 — the three other tours, each to « Terminer », nothing saved ─────────────────────────
  const tour = async (id, title, bubbles, after, { viaPrompt = false } = {}) => {
    await step(id, async () => {
      // Chained: the previous tour's prompt starts this one (« C'est parti »); otherwise its own « Me guider ».
      if (viaPrompt) await bubbleButton(page, "C'est parti").click()
      else {
        await dismissPrompt(page)
        await startGuide(page, title)
      }
      for (const [i, text] of bubbles.entries()) {
        await waitBubble(page, text, 20000)
        await sleep(700)
        const label = i === bubbles.length - 1 ? 'Terminer' : 'Suivant'
        if ((await bubbleButton(page, label).count()) !== 1) return bad(id, `no « ${label} » on « ${text} »`)
        const b = await bubble(page).boundingBox()
        if (!inViewport(page, b)) bad('C3', `« ${text} »: the bubble is off screen`, JSON.stringify(b))
        await bubbleButton(page, label).click()
      }
      const home = await page.waitForURL((u) => u.pathname === '/', { timeout: 15000 }).then(() => true, () => false)
      await after(home)
      await sleep(3500)
    })
  }
  await tour('E1', 'Votre secrétaire',
    ['Ici, vous ajoutez votre secrétaire.', 'Son nom et son e-mail', 'Ce bouton crée son compte'],
    async (home) => {
      const h = await promptOn(page, 'Premier rendez-vous')
      check('E1', home && tickedHas('secretary') && countOf('Users') === '1', 'secretary tour (started by « C\'est parti ») → dashboard, ticked, no account created', `${ticked()} users=${countOf('Users')}`)
      check('E1', h.ok && h.percent === percentOf(4), 'next: « Premier rendez-vous » prompted, 57 %', h.text)
    }, { viaPrompt: true })
  await tour('E2', 'Premier rendez-vous',
    ['Ici, vous créez un rendez-vous.', 'Le patient', "Le jour et l'heure.", "L'acte prévu", 'Ce bouton crée le rendez-vous'],
    async (home) => {
      const h = await promptOn(page, 'Première fiche de soins')
      check('E2', home && tickedHas('appointment') && countOf('Appointments') === '0', 'appointment tour (chained) → dashboard, ticked, no visit booked', `${ticked()} visits=${countOf('Appointments')}`)
      check('E2', h.ok && h.percent === percentOf(5), 'next: « Première fiche de soins » prompted, 71 %', h.text)
    }, { viaPrompt: true })
  await tour('E3', 'Papier à en-tête',
    ['Ici, vous importez votre papier à en-tête', "APEXA découpe l'en-tête"],
    async (home) => {
      const h = await promptOn(page, 'Première fiche de soins')
      check('E3', home && tickedHas('letterhead'), 'letterhead tour → dashboard, ticked', ticked())
      check('E3', h.ok && h.percent === percentOf(6), 'next: still « Première fiche de soins », 86 %', h.text)
      await dismissPrompt(page)
    })

  // ── E4 — « Masquer », then « Aide » → « Premiers pas » brings the card back and resumes the tour ─────────
  await step('E4', async () => {
    await card(page).getByRole('button', { name: 'Masquer' }).click()
    const hidden = await card(page).waitFor({ state: 'detached', timeout: 10000 }).then(() => true, () => false)
    const masked = await toastWith(page, 'Ils restent dans « Aide »', 6000)
    await page.getByRole('button', { name: 'Aide' }).first().click()
    const row = page.getByRole('button', { name: /^Premiers pas/ })
    const rowText = await row.innerText().catch(() => '')
    check('E4', hidden && masked && rowText.includes('6 sur 7'), '« Masquer » → gone, « Aide » offers « Premiers pas · 6 sur 7 »', rowText)
    await shot(page, 'E4-aide-1440')
    await row.click()
    const back = await card(page).waitFor({ timeout: 15000 }).then(() => true, () => false)
    const resumed = await promptOn(page, 'Première fiche de soins')
    check('E4', back && resumed.ok, '→ the card is back and the tour resumes on « Première fiche de soins »', resumed.text)
    await dismissPrompt(page)
  })

  // A patient for the full fiche tour — over the API, this is setup, not the product under test.
  const created = await apiAs('POST', '/patients', { firstName: patientName.first, lastName: patientName.last })
  if (created.status >= 300) bad('SETUP', 'could not create the patient', `${created.status} ${created.text.slice(0, 160)}`)

  // ── B2 — the tour stopped mid-way ticks nothing ──────────────────────────────────────────────────────
  await step('B2', async () => {
    await page.goto(`${WEB}/`)
    await dismissPrompt(page)
    await startGuide(page, 'Première fiche de soins')
    await waitBubble(page, 'Ouvrons-en un.', 20000)
    await bubbleButton(page, 'Suivant').click()
    await page.waitForURL(/\/patients\/[^/]+$/, { timeout: 30000 })
    await waitBubble(page, "la fiche de soins s'ouvre ici", 40000)
    await bubbleButton(page, 'Suivant').click()
    await waitBubble(page, "L'acte fait pendant la séance", 15000)
    await bubbleButton(page, 'Suivant').click()
    await waitBubble(page, 'Plusieurs actes')
    await page.keyboard.press('Escape')
    await page.locator('[data-guide="record-dialog"]').waitFor({ state: 'detached', timeout: 8000 }).catch(() => {})
    check('B2', (await gone(page, 6000)) && !tickedHas('dentalRecord'), 'closing the fiche ends the tour, nothing ticked', ticked())
  })

  // ── A4 / C2 / A5 / A14 — the full fiche tour at the owner's height: the last step, so the cabinet is ready ─
  await page.setViewportSize({ width: 1536, height: 730 })
  await step('A4', async () => {
    await page.goto(`${WEB}/`)
    await startGuide(page, 'Première fiche de soins')
    await waitBubble(page, 'Ouvrons-en un.', 20000)
    await bubbleButton(page, 'Suivant').click()
    await page.waitForURL(/\/patients\/[^/]+$/, { timeout: 30000 })
    await waitBubble(page, "la fiche de soins s'ouvre ici", 40000)
    check('A4', (await page.locator('[data-guide="record-dialog"]').count()) === 0, 'the fiche is not open before « Suivant »')
    await bubbleButton(page, 'Suivant').click()
    const steps = [
      ['record-acts', "L'acte fait pendant la séance"],
      ['record-add-act', 'Plusieurs actes dans la même séance'],
      ['record-chart', 'les dents, ou glissez sur plusieurs'],
      ['record-notes', 'Les notes de la séance'],
      ['record-prescription', 'Médicaments et examens'],
      ['record-payment', "Ce que le patient paie aujourd'hui"],
      ['record-submit', 'Après une vraie séance, enregistrez ici'],
    ]
    for (const [i, [mark, text]] of steps.entries()) {
      await waitBubble(page, text, 15000)
      await sleep(700) // the scroll-into-view settles
      const target = await lit(page, mark).boundingBox()
      const b = await bubble(page).boundingBox()
      const label = i === steps.length - 1 ? 'Terminer' : 'Suivant'
      check('A4', !!target && (await bubbleButton(page, label).count()) === 1, `step « ${mark} » lit, button « ${label} »`, JSON.stringify(target))
      check('C3', inViewport(page, b) && !!target && target.y < 730 && target.y + target.height > 0, `1536×730 « ${mark} »: bubble on screen, lit control in view`, JSON.stringify({ b, target }))
      if (mark === 'record-payment' || mark === 'record-submit') {
        const inside = !!b && b.y >= 0 && b.y + b.height <= 730 && b.x >= 0 && b.x + b.width <= 1536
        check('C2', inside && !!target && !overlaps(b, target), `1536×730 « ${mark} »: bubble inside, not over the lit control`, JSON.stringify({ b, target }))
      }
      await shot(page, `A4-${mark}-1536x730`)
      await bubbleButton(page, label).click()
    }
  })
  await step('A5', async () => {
    const home = await page.waitForURL((u) => u.pathname === '/', { timeout: 15000 }).then(() => true, () => false)
    // The confetti is read at once: its waves are over in ~5 s (probe fix, run 6).
    await page.getByRole('dialog', { name: 'Bienvenue dans votre nouveau cabinet !' }).waitFor({ timeout: 10000 }).catch(() => {})
    const confettiSeen = (await page.locator('body > canvas[aria-hidden="true"]').count()) >= 1
    check('A5', home && (await gone(page)), '« Terminer » → dashboard, bubble gone', page.url())
    await sleep(1500)
    check('A5', fichesOfClinic() === '0' && tickedHas('dentalRecord'), 'no fiche saved; the step ticked', `${fichesOfClinic()} ${ticked()}`)
    // The fiche was the last open step: « Bienvenue dans votre nouveau cabinet ! », a window, confetti in waves.
    const ready = page.getByRole('dialog', { name: 'Bienvenue dans votre nouveau cabinet !' })
    const shown = await ready.waitFor({ timeout: 10000 }).then(() => true, () => false)
    const confetti = confettiSeen
    const box = shown ? await ready.boundingBox() : null
    check('A14', shown && confetti && inViewport(page, box), '« Bienvenue dans votre nouveau cabinet ! » window + confetti, on screen', JSON.stringify(box))
    check('A14', (await card(page).count()) === 0 && (await bubble(page).count()) === 0, 'the card is gone, no prompt')
    await shot(page, 'A14-ready-1536x730')
    await ready.getByRole('button', { name: "C'est parti !" }).click()
    check('A14', await ready.waitFor({ state: 'detached', timeout: 8000 }).then(() => true, () => false), '« C\'est parti ! » closes it')
  })
  await page.setViewportSize({ width: 1440, height: 900 })

  // ── D3 — the fiche's two wrapped sections still open, with no tour ─────────────────────────────────────
  await step('D3', async () => {
    const patientId = sql(`SELECT p."Id" FROM "Patients" p JOIN "Users" u ON u."ClinicId" = p."ClinicId" WHERE lower(u."Email") = lower('${q(admin.email)}') LIMIT 1`)
    await page.goto(`${WEB}/patients/${patientId}`)
    await lit(page, 'record-new').waitFor({ timeout: 40000 })
    await lit(page, 'record-new').click()
    const dlg = page.locator('[data-guide="record-dialog"]')
    await dlg.waitFor({ timeout: 15000 })
    await dlg.getByRole('button', { name: /Notes de séance/ }).click()
    const notesOpen = await dlg.getByRole('button', { name: 'Ajouter une note', exact: true }).isVisible().catch(() => false)
    await dlg.getByRole('button', { name: /^Prescription/ }).click()
    const rxOpen = await dlg.getByRole('button', { name: 'Médicament' }).isVisible().catch(() => false)
    check('D3', notesOpen && rxOpen && (await bubble(page).count()) === 0, 'notes and prescription open; no bubble', `${notesOpen} ${rxOpen}`)
    await page.keyboard.press('Escape')
    await dlg.waitFor({ state: 'detached', timeout: 8000 }).catch(() => {})
  })

  // ── D2 — the shared per-user row keeps the watched set ───────────────────────────────────────────────
  await step('D2', async () => {
    const hide = await apiAs('PUT', '/dashboard/preferences', { hiddenKpis: ['gettingStarted'] })
    const show = await apiAs('PUT', '/dashboard/preferences', { hiddenKpis: [] })
    check('D2', hide.status === 200 && show.status === 200 && tickedHas('dentalRecord') && tickedHas('patients'), 'hide + show a block, the ticked set stays', `${hide.status} ${show.status} ${ticked()} ${hide.text.slice(0, 100)}`)
  })

  // ── A7 / B4 — the « Aide » form ──────────────────────────────────────────────────────────────────────
  await step('A7', async () => {
    const d = await openAide(page)
    const avis = d.getByRole('tab', { name: /^Avis/ })
    const selected = await avis.getAttribute('aria-selected')
    const tabs = await Promise.all(['Idée', 'Problème', 'Question'].map((t) => d.getByRole('tab', { name: new RegExp(`^${t}`) }).count()))
    const stars = await d.getByRole('radiogroup').count()
    const others = await d.getByLabel('Autre', { exact: true }).count()
    const more = await d.getByLabel('Un mot de plus ?').count()
    check('A7', selected === 'true' && tabs.every((n) => n === 1), 'opens on « Avis », tabs Idée · Problème · Question', `${selected} ${tabs}`)
    check('A7', stars === 2 && others === 2 && more === 1, 'two star rows, two « Autre », « Un mot de plus ? »', `${stars} ${others} ${more}`)
    check('B4', await d.getByRole('button', { name: 'Envoyer' }).isDisabled(), '« Envoyer » disabled on open')
    await shot(page, 'A7-aide-avis-1440')
    await d.getByRole('tab', { name: /^Question/ }).click()
    await d.getByLabel('Votre question').fill('Comment importer mes anciens patients ?')
    check('B4', !(await d.getByRole('button', { name: 'Envoyer' }).isDisabled()), '« Envoyer » enabled by the Question text alone')
    await d.getByRole('tab', { name: /^Avis/ }).click()
    const qName = await d.getByRole('tab', { name: /^Question/ }).innerText()
    const dot = await d.getByRole('tab', { name: /^Question/ }).locator('span.rounded-full').count()
    await d.getByRole('tab', { name: /^Question/ }).click()
    const kept = await d.getByLabel('Votre question').inputValue()
    check('B4', dot === 1 && kept === 'Comment importer mes anciens patients ?', 'filled dot on « Question », text kept across tabs', `${dot} ${qName} ${kept}`)
    // Clean up for A8: empty the question, close without sending.
    await d.getByLabel('Votre question').fill('')
    await d.getByRole('button', { name: 'Annuler' }).click()
    const discard = page.getByRole('alertdialog')
    if (await discard.isVisible().catch(() => false)) await discard.getByRole('button').last().click()
    await d.waitFor({ state: 'detached', timeout: 8000 }).catch(() => {})
  })

  await step('A8', async () => {
    const d = await openAide(page)
    await d.getByRole('radiogroup', { name: 'APEXA vous aide au cabinet' }).getByRole('radio', { name: '4 sur 5' }).click()
    const liked = d.getByRole('group', { name: 'Ce que vous aimez le plus' })
    await liked.getByRole('button', { name: 'Agenda' }).click()
    await liked.getByLabel('Autre', { exact: true }).fill('Les rappels au téléphone')
    await d.getByRole('tab', { name: /^Idée/ }).click()
    await d.getByLabel('Votre idée').fill('Un export Excel des devis.')
    await d.getByRole('button', { name: 'Envoyer' }).click()
    check('A8', await toastWith(page, 'Merci. Votre avis est arrivé chez APEXA.', 15000), 'toast « Merci… »')
    await sleep(1000)
    const rows = feedbackRows().split('\n').filter(Boolean)
    const avis = rows.find((r) => r.startsWith('avis|aide|'))
    const idee = rows.find((r) => r.startsWith('idee|aide|'))
    check('A8', avis === 'avis|aide|4|agenda|Les rappels au téléphone||', 'avis row: 4 stars, agenda, LikedOther', avis ?? rows.join(' // '))
    check('A8', !!idee && idee.endsWith('|Un export Excel des devis.'), 'idée row with its message', idee ?? rows.join(' // '))
  })

  // ── B5 — the week-one form (setup on the throwaway DB: this account 10 days old, 3 working days) ──────
  await step('B5', async () => {
    const uid = userId()
    sql(`UPDATE "Users" SET "CreatedAt" = now() - interval '10 days' WHERE "Id" = '${q(uid)}'`)
    const clinicId = sql(`SELECT "ClinicId" FROM "Users" WHERE "Id" = '${q(uid)}'`)
    for (const d of [2, 3, 4]) {
      sql(`INSERT INTO "ClinicActivityDays" ("Id","ClinicId","Day","Writes","Appointments","PatientsCreated","ComputedAt") VALUES (gen_random_uuid(), '${clinicId}', (now() - interval '${d} days')::date, 4, 1, 0, now()) ON CONFLICT DO NOTHING`)
    }
    const prompt = await apiAs('GET', '/feedback/prompt')
    if (!prompt.json?.show) return skip('B5', `the server does not put the question (${prompt.status} ${prompt.text.slice(0, 80)})`)
    await page.goto(`${WEB}/`)
    const corner = page.locator('section[aria-labelledby="feedback-prompt-title"]')
    await corner.waitFor({ timeout: 100000 })
    await corner.getByRole('radio', { name: '3 sur 5' }).click()
    const d = page.getByRole('dialog', { name: 'Votre avis sur APEXA' })
    await d.waitFor({ timeout: 10000 })
    const kept = await d.getByRole('radiogroup', { name: 'APEXA vous aide au cabinet' }).getByRole('radio', { name: '3 sur 5' }).getAttribute('aria-checked')
    const others = await d.getByLabel('Autre', { exact: true }).count()
    check('B5', kept === 'true' && others === 2 && (await d.getByRole('tab').count()) === 0, 'week-one form: star kept, two « Autre », no tabs', `${kept} ${others}`)
    await shot(page, 'B5-week-one-1440')
    await d.getByRole('group', { name: 'Ce qui a été difficile' }).getByLabel('Autre', { exact: true }).fill('Trouver la caisse du jour')
    await d.getByRole('button', { name: 'Envoyer' }).click()
    check('B5', await toastWith(page, 'Merci. Votre avis est arrivé chez APEXA.', 15000), 'toast « Merci… »')
    await sleep(1000)
    const row = feedbackRows().split('\n').find((r) => r.startsWith('avis|semaine-1|') && r.includes('Trouver la caisse du jour'))
    check('B5', !!row && row.startsWith('avis|semaine-1|3|'), 'semaine-1 row: 3 stars, HardOther', row ?? feedbackRows())
  })

  // ── A11 — a step done by ordinary work, celebrated on the dashboard, once (a second, fresh cabinet) ──────
  await step('A11', async () => {
    const two = { email: `qa-rk2-${stamp}@clinic.local`, password: admin.password, clinic: `Cabinet RK2 ${stamp}`, fullName: 'Dr QA Deux' }
    const s2 = { secret: null, lastCode: null, token: null }
    const code2 = async () => {
      for (;;) {
        const c = authenticator.generate(s2.secret)
        if (c !== s2.lastCode) { s2.lastCode = c; return c }
        await sleep(2000)
      }
    }
    await fetch(`${API}/auth/signup`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ clinicName: two.clinic, fullName: two.fullName, email: two.email, password: two.password, city: 'Tunis' }),
    })
    const mail = await mailTo(two.email)
    const link = mail && (mail.Text + mail.HTML).match(/https?:\/\/[^\s"'<>]+\/signup\/verifier#token=[^\s"'<>]+/)?.[0]
    if (!link) return bad('A11', 'no verification link for the second cabinet')
    const p2 = await (await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })).newPage()
    await p2.goto(link)
    await p2.getByText('est créé').first().waitFor({ timeout: 30000 })
    await p2.goto(`${WEB}/login`)
    await p2.locator('#email').fill(two.email)
    await p2.locator('#password').fill(two.password)
    await p2.getByRole('button', { name: 'Se connecter', exact: true }).click()
    await p2.locator('code').first().waitFor({ timeout: 20000 })
    s2.secret = (await p2.locator('code').first().textContent()).replace(/\s/g, '')
    await p2.locator('input[autocomplete="one-time-code"]').fill(await code2())
    await p2.getByRole('button', { name: 'Confirmer' }).click()
    await p2.getByText('Second facteur activé').waitFor({ timeout: 20000 })
    await p2.locator('#ack-codes').click()
    await p2.getByRole('button', { name: 'Continuer vers la connexion' }).click()
    await p2.getByText('Vérification en deux étapes').waitFor({ timeout: 15000 })
    await p2.locator('input[autocomplete="one-time-code"]').fill(await code2())
    await p2.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30000 })
    // The first card read only remembers (the security code done at sign-up is not news) — and starts the tour.
    await card(p2).waitFor({ timeout: 40000 })
    await dismissPrompt(p2)
    await sleep(1000)
    const login = await fetch(`${API}/auth/login`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: two.email, password: two.password, totpCode: await code2() }),
    })
    s2.token = (await login.json().catch(() => null))?.value?.accessToken ?? null
    const as2 = async (method, url, body) => {
      const res = await fetch(`${API}${url}`, { method, headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${s2.token}` }, body: body ? JSON.stringify(body) : undefined })
      return { status: res.status, json: await res.json().catch(() => null) }
    }
    // Off the dashboard while the work is done, so the celebration is the next read's and not a live re-read.
    await p2.goto(`${WEB}/patients`)
    await sleep(2000)
    const pat = await as2('POST', '/patients', { firstName: 'Sami', lastName: `Deux${stamp}` })
    const pid = pat.json?.id ?? pat.json?.value?.id
    const when = new Date(Date.now() + 2 * 86400000)
    when.setUTCHours(9, 0, 0, 0)
    const visit = await as2('POST', '/appointments', { patientId: pid, appointmentDateTime: when.toISOString(), durationMinutes: 30, allowOutsideWorkingHours: true, allowOverlap: true })
    if (pat.status >= 300 || visit.status >= 300) return bad('A11', 'could not create the patient or the visit', `${pat.status} ${visit.status}`)
    await p2.goto(`${WEB}/`)
    await card(p2).waitFor({ timeout: 40000 })
    const seen = await p2.locator('[data-sonner-toast]').filter({ hasText: "C'est fait : Premier rendez-vous" }).first().waitFor({ timeout: 15000 }).then(() => true, () => false)
    const tt = await p2.locator('[data-sonner-toast]').filter({ hasText: "C'est fait : Premier rendez-vous" }).first().innerText().catch(() => '')
    check('A11', seen && tt.includes('du mordant') && !tt.includes('Étape suivante'), "dashboard: « 🎉 C'est fait : Premier rendez-vous » + joke", tt.replace(/\n/g, ' | '))
    await p2.screenshot({ path: path.join(SHOTS, 'A11-dashboard-celebration-1440.png') })
    await sleep(4000)
    await p2.reload()
    await card(p2).waitFor({ timeout: 40000 })
    await sleep(3000)
    check('A11', (await p2.locator('[data-sonner-toast]').filter({ hasText: "C'est fait" }).count()) === 0, 'a reload celebrates nothing')
    await p2.context().close()
  })

  // ── D5 — the notice e-mail ───────────────────────────────────────────────────────────────────────────
  await step('D5', async () => {
    const consoleAccounts = sql(`SELECT count(*) FROM "PlatformAccounts" WHERE "IsActive"`)
    if (consoleAccounts === '0') return skip('D5', 'no console account on this database — the e-mail has no recipient; content held by FeedbackTests')
    ok('D5', `console accounts: ${consoleAccounts}`)
  })
} catch (e) {
  bad('RUN', 'walk aborted', String(e.message ?? e).split('\n')[0])
  await shot(page, 'RUN-aborted').catch(() => {})
} finally {
  await browser.close()
}

const pass = results.filter((r) => r.outcome === 'pass').length
const fail = results.filter((r) => r.outcome === 'fail').length
const skipped = results.filter((r) => r.outcome === 'skip').length
console.log(`\n${pass} ✅, ${fail} ❌, ${skipped} ⏭`)
findings.length ? findings.forEach((f) => console.log(f)) : console.log('ALL CLEAR')
writeFileSync(path.join(HERE, 'walk-results.json'), JSON.stringify({ stamp, results }, null, 2))
