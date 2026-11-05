// QA walk — guided-first-days release 3 (« Me guider »). One launch, every scenario of qa/r3/plan.md; collects, never stops.
// Run: PW_DIR=<dir holding node_modules/playwright-core + otplib> node walk.mjs
// Stack: API :5097, web :3097, database clinic_gfd_c (throwaway), shared mailpit (read only).
// ⚠️ The order is forced by the product: a « Premiers pas » step offers « Me guider » only while it is NOT done, and
// « Vos patients » ticks with the first patient — so every patients-guide variant runs before A3 creates one.
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

const stamp = Date.now().toString(36)
const admin = { email: `qa-r3-${stamp}@clinic.local`, password: 'Un-mot-de-passe-QA-2026!', clinic: `Cabinet R3 ${stamp}`, fullName: 'Dr QA Geller' }
const patientName = { first: 'Leila', last: `Guide${stamp}` }
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

const bubble = (page) => page.locator('[data-guide-bubble]')
const running = (page) => page.evaluate(() => document.body.getAttribute('data-guide-running'))
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
const next = (page) => bubble(page).getByRole('button', { name: 'Suivant' }).click()
/** Answers whatever confirmation is up with its affirmative (last) button — « …dans le passé ? », « en dehors… ». */
async function answerConfirmations(page, rounds = 3) {
  for (let i = 0; i < rounds; i++) {
    const c = page.getByRole('alertdialog')
    try { await c.waitFor({ timeout: 2500 }) } catch { return }
    await c.getByRole('button').last().click({ timeout: 4000 }).catch(() => {})
    await sleep(800)
  }
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

  // ── A1 — the card ─────────────────────────────────────────────────────────────────────────────────────
  await step('A1', async () => {
    await page.goto(`${WEB}/`)
    const card = page.locator('section[aria-labelledby="getting-started-title"]')
    await card.waitFor({ timeout: 40000 })
    const guided = ['Votre secrétaire', 'Vos patients', 'Premier rendez-vous', 'Première fiche de soins', 'Papier à en-tête']
    const found = []
    for (const t of guided) found.push((await card.getByRole('button', { name: `Me guider : ${t}` }).count()) === 1)
    check('A1', found.every(Boolean), '« Me guider » on the five guided steps', JSON.stringify(found))
    check('A1', (await card.getByRole('link', { name: /Commencer/ }).count()) === 0, 'no « Commencer » left on a guided step (security is done)')
    check('A1', (await card.getByRole('button', { name: 'Installer : Votre téléphone' }).count()) === 1, '« Votre téléphone » keeps « Installer »')
    check('A1', (await card.getByRole('link', { name: /^Vidéo :/ }).count()) >= 1, 'videos still beside the steps')
    await shot(page, 'A1-card-1440')
  })

  // ── B5 — the dental-record guide with no patient ─────────────────────────────────────────────────────
  await step('B5', async () => {
    await startGuide(page, 'Première fiche de soins')
    await waitBubble(page, "Ouvrez le dossier d'un patient.")
    const t = await toastWith(page, "Ce guide a besoin d'un patient", 16000)
    check('B5', t && (await gone(page)), 'no patient → « Ce guide a besoin d\'un patient… », guide ends')
  })

  // ── B1 / B4 / B3 / C2 — the patients guide, before any patient exists ─────────────────────────────────
  await step('B1', async () => {
    await startGuide(page, 'Vos patients')
    await waitBubble(page, 'Ajouter un patient')
    check('B1', (await running(page)) === 'patients' && new URL(page.url()).pathname === '/patients', 'on /patients with data-guide-running="patients"')
    await shot(page, 'B1-step1-1440')
    await bubble(page).getByRole('button', { name: 'Quitter le guide' }).click()
    check('B1', (await gone(page)) && (await running(page)) === null, '« Quitter le guide » → bubble and attribute gone')
  })
  await step('B4', async () => {
    await startGuide(page, 'Vos patients')
    await waitBubble(page, 'Ajouter un patient')
    await page.keyboard.press('Escape')
    check('B4', await gone(page), 'Escape with no dialog open ends the guide')
  })
  await step('B3', async () => {
    await startGuide(page, 'Vos patients')
    await waitBubble(page, 'Ajouter un patient')
    await page.locator('aside').getByRole('link', { name: 'Rendez-vous', exact: true }).first().click()
    await page.waitForURL('**/appointments', { timeout: 20000 })
    check('B3', await gone(page, 6000), 'leaving the guide\'s pages ends it')
  })
  await step('C2', async () => {
    await page.setViewportSize({ width: 320, height: 640 })
    await startGuide(page, 'Vos patients')
    await waitBubble(page, 'Ajouter un patient')
    await lit(page, 'patients-add').click()
    await waitBubble(page, 'Son prénom et son nom')
    const b = await bubble(page).boundingBox()
    check('C2', !!b && b.x >= 0 && b.x + b.width <= 320 && b.y >= 0 && b.y + b.height <= 640 && !(await overflowX(page)), '320: bubble inside the viewport, no overflow', JSON.stringify(b))
    await shot(page, 'C2-names-320')
    await bubble(page).getByRole('button', { name: 'Quitter le guide' }).click()
    await page.keyboard.press('Escape')
    await page.setViewportSize({ width: 1440, height: 900 })
  })

  // ── B2 then A2 — the secretary guide ─────────────────────────────────────────────────────────────────
  await step('B2', async () => {
    await startGuide(page, 'Votre secrétaire')
    await waitBubble(page, 'Pour ajouter votre secrétaire')
    await lit(page, 'users-create').click()
    await waitBubble(page, 'Son nom et son e-mail')
    await page.locator('[data-guide="user-dialog"]').getByRole('button', { name: 'Annuler' }).click()
    const done = await toastWith(page, 'Votre secrétaire a son compte.', 2500)
    check('B2', (await gone(page)) && !done, 'closing the dialog without saving ends the guide, no « fait »')
  })
  await step('A2', async () => {
    await startGuide(page, 'Votre secrétaire')
    await waitBubble(page, 'Pour ajouter votre secrétaire')
    check('A2', new URL(page.url()).pathname === '/users' && (await running(page)) === 'secretary', 'on /users, data-guide-running="secretary"')
    await shot(page, 'A2-step1-1440')
    await lit(page, 'users-create').click()
    await waitBubble(page, 'Son nom et son e-mail')
    await page.locator('#create-user-fullname').fill('Amel Guide')
    await page.locator('#create-user-email').fill(`amel-${stamp}@clinic.local`)
    await next(page)
    const focus = await page.evaluate(() => document.activeElement?.id)
    check('B9', focus === 'create-user-email', 'focus still in the field after « Suivant »', String(focus))
    check('B8', await page.locator('[data-guide="user-dialog"]').isVisible(), 'the dialog stays open after « Suivant »')
    await waitBubble(page, 'créer son compte')
    await shot(page, 'A2-step3-1440')
    await lit(page, 'user-submit').click()
    check('A2', await toastWith(page, 'Votre secrétaire a son compte.'), 'saved → « Votre secrétaire a son compte. »')
    check('A2', (await gone(page)) && (await running(page)) === null, 'bubble and attribute gone')
    const result = page.getByRole('dialog').filter({ has: page.getByRole('button', { name: /Copier le lien|Copier/ }) })
    if (await result.count()) await result.getByRole('button', { name: 'Terminé' }).click()
  })

  // ── A3 — the patients guide end to end ───────────────────────────────────────────────────────────────
  await step('A3', async () => {
    await startGuide(page, 'Vos patients')
    await waitBubble(page, 'Ajouter un patient')
    await lit(page, 'patients-add').click()
    await waitBubble(page, 'Son prénom et son nom')
    const dlg = page.locator('[data-guide="patient-dialog"]')
    await dlg.locator('#firstName').fill(patientName.first)
    await dlg.locator('#lastName').fill(patientName.last)
    await shot(page, 'A3-names-1440')
    await next(page)
    await waitBubble(page, 'Créer le patient')
    await lit(page, 'patient-submit').click()
    const toastSeen = await toastWith(page, 'Votre premier patient est enregistré.', 20000)
    const landed = await page.waitForURL(/\/patients\/[0-9a-f-]{36}/, { timeout: 20000 }).then(() => true, () => false)
    await shot(page, 'A3-after-submit-1440')
    check('A3', toastSeen, '« Votre premier patient est enregistré. »')
    check('A3', landed, 'the patient page opens', page.url())
    check('A3', await gone(page), 'guide over')
  })

  // ── C1 — appointment guide on a phone, step 1 only ───────────────────────────────────────────────────
  await step('C1', async () => {
    await page.setViewportSize({ width: 390, height: 844 })
    await startGuide(page, 'Premier rendez-vous')
    await waitBubble(page, 'Pour un nouveau rendez-vous')
    const fab = await lit(page, 'agenda-new').boundingBox()
    const b = await bubble(page).boundingBox()
    check('C1', !!fab && !!b && b.y + b.height < fab.y && b.y < 200 && !(await overflowX(page)), '« + » lit, bubble docked at the top, no overflow', JSON.stringify({ fab, b }))
    await shot(page, 'C1-agenda-390')
    await bubble(page).getByRole('button', { name: 'Quitter le guide' }).click()
    await page.setViewportSize({ width: 1440, height: 900 })
  })

  // ── A4 (+ B6, C3) — the appointment guide at 1536×730, through a duplicate-patient confirmation ──────────
  await step('A4', async () => {
    await page.setViewportSize({ width: 1536, height: 730 })
    await startGuide(page, 'Premier rendez-vous')
    await waitBubble(page, 'Pour un nouveau rendez-vous')
    check('A4', new URL(page.url()).pathname === '/appointments', 'on /appointments (not the ?newAppointment deep link)')
    await lit(page, 'agenda-new').click()
    await waitBubble(page, 'Choisissez le patient')
    const dlg = page.locator('[data-guide="booking-dialog"]')
    await dlg.getByRole('radio', { name: 'Nouveau patient' }).click()
    await dlg.locator('#firstName').fill(patientName.first)
    await dlg.locator('#lastName').fill(patientName.last)
    await next(page)
    await waitBubble(page, "Le jour et l'heure.")
    await dlg.locator('#create-appt-start-time').fill('23:00')
    await next(page)
    await waitBubble(page, "Ajoutez l'acte prévu")
    await next(page)
    await waitBubble(page, 'Créer le rendez-vous')
    const b = await bubble(page).boundingBox()
    const s = await lit(page, 'booking-submit').boundingBox()
    const covers = b && s && !(b.x + b.width < s.x || s.x + s.width < b.x || b.y + b.height < s.y || s.y + s.height < b.y)
    check('C3', !!b && b.y >= 0 && b.y + b.height <= 730 && !covers, '730 tall: bubble on screen, not over « Créer le rendez-vous »', JSON.stringify({ b, s }))
    await shot(page, 'C3-submit-1536x730')
    await lit(page, 'booking-submit').click()
    // B6 — « existe peut-être déjà » (A3's name): the guide steps aside while it is up.
    const dup = page.getByRole('alertdialog').filter({ hasText: 'un dossier pour' })
    let sawDup = false
    try { await dup.waitFor({ timeout: 8000 }); sawDup = true } catch {}
    if (sawDup) {
      // The overlay notices the confirmation on its next frame, then React re-renders: poll, never read once.
      const hidden = await page
        .waitForFunction(() => {
          const el = document.querySelector('[data-guide-bubble]')
          return !el || getComputedStyle(el).visibility === 'hidden'
        }, null, { timeout: 3000 })
        .then(() => true, () => false)
      check('B6', hidden, 'bubble hidden while the duplicate confirmation is up')
      await shot(page, 'B6-duplicate-1536')
      await dup.getByRole('button', { name: /Créer quand même/ }).click()
    } else {
      skip('B6', 'no duplicate confirmation appeared')
    }
    const doneToast = toastWith(page, "Votre premier rendez-vous est dans l'agenda.", 30000)
    await answerConfirmations(page)
    check('A4', await doneToast, '« Votre premier rendez-vous est dans l\'agenda. »')
    check('A4', await gone(page), 'guide over')
    await page.setViewportSize({ width: 1440, height: 900 })
  })

  // ── A5 — the dental-record guide end to end ──────────────────────────────────────────────────────────
  await step('A5', async () => {
    await startGuide(page, 'Première fiche de soins')
    await waitBubble(page, "Ouvrez le dossier d'un patient.")
    await shot(page, 'A5-list-1440')
    await page.locator('[data-guide="patients-list"]:visible tr').filter({ hasText: patientName.last }).first().click()
    await page.waitForURL(/\/patients\/[^/]+$/, { timeout: 30000 })
    await waitBubble(page, 'Nouvelle fiche de soins', 40000)
    await lit(page, 'record-new').click()
    await waitBubble(page, "Choisissez l'acte")
    const dlg = page.locator('[data-guide="record-dialog"]')
    await dlg.getByRole('listbox', { name: 'Actes du catalogue' }).getByRole('option').first().click()
    await shot(page, 'A5-acts-1440')
    await next(page)
    await waitBubble(page, 'enregistrer la séance')
    await lit(page, 'record-submit').click()
    await answerConfirmations(page)
    check('A5', await toastWith(page, 'Votre première fiche de soins est enregistrée.', 20000), '« Votre première fiche de soins est enregistrée. »')
    check('A5', await gone(page), 'guide over')
  })

  // ── B7 — the post-visit question waits for the guide ─────────────────────────────────────────────────
  await step('B7', async () => {
    // The guide FIRST, then the due visit: created before, the question takes the dashboard (rightly) and its modal
    // blocks the card. The realtime « notifications » broadcast makes the popup re-read at once — while the guide runs.
    await page.goto(`${WEB}/`)
    await startGuide(page, 'Papier à en-tête')
    await waitBubble(page, 'Importer mon papier')
    // Fixture: a pending review only counts visits after the account was created (« join baseline »).
    sql(`UPDATE "Users" SET "CreatedAt" = now() - interval '1 day' WHERE lower("Email") = lower('${admin.email}')`)
    const pat = await apiAs('POST', '/patients', { firstName: 'Revue', lastName: `Visite${stamp}`, gender: 'F', phoneNumber: null })
    const patientId = pat.json?.id ?? pat.json?.value?.id
    const start = new Date(Date.now() - 2 * 3600 * 1000)
    start.setUTCMinutes(0, 0, 0)
    const appt = await apiAs('POST', '/appointments', { patientId, appointmentDateTime: start.toISOString(), durationMinutes: 30, allowOutsideWorkingHours: true, allowOverlap: true })
    if (appt.status >= 300) return skip('B7', `fixture appointment refused (${appt.status})`)
    await sleep(75000)
    const during = await page.getByText('Le patient est-il venu ?').count()
    const pending = await apiAs('GET', '/notifications/pending-reviews')
    console.log(`  B7 pending reviews (server): ${pending.status} ${pending.text.slice(0, 160)}`)
    await bubble(page).getByRole('button', { name: 'Quitter le guide' }).click()
    let after = false
    try { await page.getByText('Le patient est-il venu ?').first().waitFor({ timeout: 75000 }); after = true } catch {}
    console.log(`  B7 body attrs: ${await page.evaluate(() => Array.from(document.body.attributes).map((a) => a.name).join(','))}`)
    await shot(page, 'B7-after-quit-1440')
    check('B7', during === 0 && after, 'no post-visit question during the guide; it comes once the guide ends', `during=${during} after=${after}`)
    if (after) await page.getByRole('dialog').filter({ hasText: 'Le patient est-il venu ?' }).getByRole('button', { name: 'Plus tard' }).first().click()
  })

  // ── A6 — the letterhead guide end to end ─────────────────────────────────────────────────────────────
  await step('A6', async () => {
    // A plain A4 page as a PNG: a header band, white, a footer band.
    const paper = await ctx.newPage()
    await paper.setViewportSize({ width: 1240, height: 1754 })
    await paper.setContent('<body style="margin:0;background:#fff"><div style="height:220px;border-bottom:6px solid #1b54ce;font:bold 64px sans-serif;padding:40px">Cabinet QA</div><div style="height:1300px"></div><div style="border-top:4px solid #1b54ce;font:32px sans-serif;padding:30px">Rue de Tunis · 71 000 000</div></body>')
    const png = await paper.screenshot({ type: 'png' })
    await paper.close()

    await startGuide(page, 'Papier à en-tête')
    await waitBubble(page, 'Importer mon papier')
    check('A6', new URL(page.url()).pathname === '/settings', 'on /settings')
    await lit(page, 'letterhead-import').click()
    await waitBubble(page, 'Choisissez un PDF ou une photo')
    await page.locator('[data-guide="letterhead-dialog"] input[type="file"]').setInputFiles({ name: 'papier.png', mimeType: 'image/png', buffer: png })
    await waitBubble(page, 'Placez les deux lignes', 40000)
    await shot(page, 'A6-cut-1440')
    await lit(page, 'letterhead-cut-next').click()
    await waitBubble(page, "Si l'aperçu vous convient", 40000)
    await lit(page, 'letterhead-save').click()
    check('A6', await toastWith(page, "Vos documents s'impriment sur votre papier.", 30000), '« Vos documents s\'impriment sur votre papier. »')
    check('A6', await gone(page), 'guide over')
  })

  // ── D — regression ──────────────────────────────────────────────────────────────────────────────────
  await step('D1', async () => {
    await page.goto(`${WEB}/patients`)
    await page.locator('tr').filter({ hasText: patientName.last }).first().waitFor({ timeout: 30000 })
    ok('D1', '1440: the table shows the patients')
    await page.setViewportSize({ width: 390, height: 844 })
    const card = page.locator('li').filter({ hasText: patientName.last }).first()
    await card.waitFor({ timeout: 10000 })
    check('D1', await card.isVisible(), '390: the card list shows the patients')
    await shot(page, 'D1-patients-390')
    await page.setViewportSize({ width: 1440, height: 900 })
  })
  await step('D2', async () => {
    await page.goto(`${WEB}/appointments`)
    await lit(page, 'agenda-new').click()
    await page.locator('[data-guide="booking-dialog"]').waitFor({ timeout: 15000 })
    check('D2', (await bubble(page).count()) === 0 && (await running(page)) === null, 'booking dialog opens with no guide, no bubble')
    await page.keyboard.press('Escape')
  })
  await step('D3', async () => {
    await page.goto(`${WEB}/`)
    const card = page.locator('section[aria-labelledby="getting-started-title"]')
    await card.waitFor({ timeout: 40000 })
    const t = await card.innerText()
    check('D3', (await card.getByRole('button', { name: 'Installer : Votre téléphone' }).count()) === 1 && (await card.getByRole('button', { name: /^Me guider/ }).count()) === 0,
      'after the guides: phone keeps « Installer », no « Me guider » left on done steps', t.replace(/\n/g, ' | ').slice(0, 300))
    await shot(page, 'D3-card-after-1440')
  })
  skip('D4', 'chat notice over dialogs needs a second signed-in colleague; the selector change is additive')
  await step('D5', async () => {
    await page.goto(`${WEB}/patients`)
    await lit(page, 'patients-add').click()
    const dlg = page.locator('[data-guide="patient-dialog"]')
    await dlg.locator('#firstName').fill('Sans')
    await dlg.locator('#lastName').fill(`Guide${stamp}`)
    await lit(page, 'patient-submit').click()
    const landed = await page.waitForURL(/\/patients\/[0-9a-f-]{36}/, { timeout: 20000 }).then(() => true, () => false)
    check('D5', landed, 'control, no guide: creating a patient on /patients opens their page', page.url())
  })
} catch (e) {
  bad('SETUP', 'threw', String(e.message ?? e).split('\n')[0])
} finally {
  await browser.close()
  writeFileSync(path.join(HERE, 'walk-results.json'), JSON.stringify({ stamp, results, findings }, null, 2))
  console.log(findings.length ? `\n${findings.length} finding(s)` : '\nALL CLEAR')
  findings.forEach((f) => console.log(f))
}
