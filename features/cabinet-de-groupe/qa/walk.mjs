// QA walk — cabinet de groupe (runs 1–3). One launch, every browser row of features/cabinet-de-groupe/qa/plan.md.
import { chromium } from 'playwright-core'
import { execSync } from 'node:child_process'
import { readFileSync, mkdirSync, writeFileSync } from 'node:fs'
import { totp } from './totp.mjs'

const WEB = 'http://localhost:3099'
const SHOTS = 'C:/Users/Oumayma Benkhalifa/Desktop/clinic-management/.claude/worktrees/cabinet-de-groupe/features/cabinet-de-groupe/qa/shots'
mkdirSync(SHOTS, { recursive: true })
const A = JSON.parse(readFileSync('arrange.json', 'utf8'))
const PATIENT = 'ceafa5f9-0218-4e20-b7b7-a7d47cef8aa6'
const OLD_UNASSIGNED = 'fef1bf18-6b4a-4c63-852a-3e5762ee285e'
const STAMP = Date.now() % 100000

const findings = [], results = []
const ok = (id, m) => { console.log(`  ✅ [${id}] ${m}`); results.push({ id, r: 'pass', m }) }
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); results.push({ id, r: 'fail', m: `${m} ${d}` }); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); results.push({ id, r: 'skip', m: why }) }
const sql = q => execSync(`docker exec clinic-postgres psql -U clinic_user -d cdg_qa -At -F"|" -c "${q.replace(/"/g, '\\"')}"`).toString().trim()
const shot = async (page, name) => { await page.bringToFront(); await page.screenshot({ path: `${SHOTS}/${name}.png`, timeout: 15000 }) }
const settle = ms => new Promise(r => setTimeout(r, ms))

const API = 'http://localhost:5099/api'
const CLINIC = 'f64a8a75-94e1-4e08-a743-fa231abe438f'
const SALMA_DOC = '9ed3d280-e3a1-4f11-ad19-8c993dd0da93'
const SECRETARY_USER = 'local|22222222-2222-4222-8222-222222222222'

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const requests = []
async function newPage() {
  const c = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
  await c.route('**/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
  const p = await c.newPage()
  p.on('request', r => { if (r.url().includes(':5099/api/')) requests.push(r.url()) })
  return p
}
let page = await newPage()

async function login() {
  await page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' })
  await page.waitForSelector('input[type=email]', { timeout: 60000 })
  await page.fill('input[type=email]', 'salma.benyoussef@cabinet-ibnkhaldoun.tn')
  await page.fill('input[type=password]', 'QaAudit2026!y')
  await page.click('button:has-text("Se connecter")')
  await page.waitForSelector('input[inputmode=numeric]', { timeout: 30000 })
  await settle((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
  await page.fill('input[inputmode=numeric]', totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'))
  await page.waitForURL(u => !u.toString().includes('/login'), { timeout: 60000 })
}

async function loginSecretary() {
  await page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' })
  await page.waitForSelector('input[type=email]', { timeout: 60000 })
  await page.fill('input[type=email]', 'qa.secretary@ibnkhaldoun.test')
  await page.fill('input[type=password]', 'QaAudit2026!y')
  await page.click('button:has-text("Se connecter")')
  await page.waitForURL(u => !u.toString().includes('/login'), { timeout: 60000 })
}

/** Press « Créer » / « Enregistrer » and accept the confirmations a booking may raise (slot taken, hours, past). */
async function submitDialog(label) {
  await page.locator(`[role=dialog] button:has-text("${label}")`).last().click()
  for (let i = 0; i < 4; i++) {
    await settle(1500)
    const alert = page.locator('[role=alertdialog]:visible')
    if (await alert.count()) { await alert.locator('button').last().click() } else break
  }
  await settle(2000)
}

/** The agenda bar's praticien Select (desktop, from xl:). */
const doctorCombo = () => page.locator('button[role=combobox]:visible').filter({ hasText: /Tous les praticiens|Salma Ben Youssef|Nadia Trabelsi/ }).first()
async function pickOption(name) {
  await page.getByRole('option', { name }).first().click()
}
async function agendaReady() {
  await page.waitForSelector('[data-guide="agenda-new"]', { timeout: 60000 })
  await settle(2500)
}
const blockLabels = () => page.$$eval('[aria-label]', els => els.map(e => e.getAttribute('aria-label')).filter(l => / min · /.test(l)))

const SCENARIOS = [
  { id: 'A4', run: async () => {
      await agendaReady()
      const text = (await doctorCombo().innerText()).trim()
      const chip = await page.getByText(/Praticien\s*:\s*Salma Ben Youssef/).count()
      await shot(page, 'A4-first-visit')
      text.includes('Salma Ben Youssef') && chip > 0 ? ok('A4', `filter « ${text} », chip present`) : bad('A4', 'first visit not on own RDV', `filter « ${text} », chip ${chip}`)
  } },
  { id: 'A2', run: async () => {
      await doctorCombo().click(); await pickOption('Tous les praticiens'); await settle(2500)
      const labels = await blockLabels()
      const salma = labels.filter(l => l.includes('avec Salma Ben Youssef')).length
      const nadia = labels.filter(l => l.includes('avec Nadia Trabelsi')).length
      const chips = await page.$$eval('span[aria-hidden=true]', els => els.map(e => e.textContent?.trim()).filter(t => /^(SB|NT|KH)$/.test(t)))
      await shot(page, 'A2-week-1440')
      salma > 0 && nadia > 0 && chips.includes('SB') && chips.includes('NT')
        ? ok('A2', `blocks avec Salma ${salma}, avec Nadia ${nadia}; chips ${[...new Set(chips)].join(',')}`)
        : bad('A2', 'dentist not shown on blocks', `salma ${salma} nadia ${nadia} chips ${chips.join(',')} · sample ${labels.slice(0, 3).join(' | ')}`)
  } },
  { id: 'A3', run: async () => {
      await agendaReady()
      await page.click('button[aria-label="Filtres de l\'agenda"]')
      await settle(800)
      const pop = page.locator('[data-radix-popper-content-wrapper]').last()
      const txt = await pop.innerText()
      await shot(page, 'A3-legend')
      ;/Praticien/i.test(txt) && txt.includes('Salma Ben Youssef') && txt.includes('Nadia Trabelsi') ? ok('A3', 'legend « Praticien » lists the dentists') : bad('A3', 'legend has no dentist section', txt.slice(0, 300))
      await page.keyboard.press('Escape')
  } },
  { id: 'A5', run: async () => {
      await page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' }); await agendaReady()
      const text = (await doctorCombo().innerText()).trim()
      text.includes('Tous les praticiens') ? ok('A5', 'choice remembered after a reload') : bad('A5', 'choice not remembered', `filter « ${text} »`)
  } },
  { id: 'A6', run: async () => {
      await doctorCombo().click(); await pickOption('Nadia Trabelsi'); await settle(3000)
      const labels = await blockLabels()
      const blocks = labels.filter(l => /Occup|QA bloc/i.test(l))
      const salmas = labels.filter(l => l.includes('avec Salma Ben Youssef'))
      await shot(page, 'A6-filtered-nadia')
      blocks.length > 0 && salmas.length === 0 ? ok('A6', `cabinet-wide block visible while filtered on Nadia (${blocks.length}); no Salma RDV`) : bad('A6', 'filtered view wrong', `blocks ${blocks.length}, salma ${salmas.length} · sample ${labels.slice(0, 4).join(' | ')}`)
  } },
  { id: 'A7', run: async () => {
      await page.click('[data-guide="agenda-new"]')
      await page.waitForSelector('[role=dialog] #doctor', { timeout: 20000 }); await settle(1500)
      const text = (await page.locator('[role=dialog] #doctor').innerText()).trim()
      text.includes('Nadia Trabelsi') ? ok('A7', `new booking opens on « ${text} »`) : bad('A7', 'agenda filter not pre-filled', `« ${text} »`)
  } },
  { id: 'C2', run: async () => {
      await page.setViewportSize({ width: 1536, height: 730 }); await settle(800)
      const btn = page.locator('[role=dialog] button:has-text("Créer le rendez-vous")')
      const box = await btn.boundingBox()
      await shot(page, 'C2-dialog-1536x730')
      box && box.y + box.height <= 730 ? ok('C2', `footer at y=${Math.round(box.y)}`) : bad('C2', 'footer off screen at 730 px', JSON.stringify(box))
      await page.setViewportSize({ width: 1440, height: 900 })
  } },
  { id: 'B1', run: async () => {
      await page.locator('[role=dialog] [role=radio]', { hasText: 'Créneau occupé' }).click(); await settle(500)
      await page.locator('[role=dialog] #doctor').click(); await settle(400)
      const options = await page.getByRole('option').allInnerTexts()
      if (!options.some(o => o.includes('Tout le cabinet'))) { bad('B1', '« Tout le cabinet » not offered', options.join(' | ')); await page.keyboard.press('Escape'); return }
      await pickOption('Tout le cabinet'); await settle(400)
      const before = sql(`select count(*) from "Appointments" where "ClinicId"='f64a8a75-94e1-4e08-a743-fa231abe438f' and "PatientId" is null`)
      await page.locator('[role=dialog] button:has-text("Créer le rendez-vous")').click()
      for (let i = 0; i < 4; i++) {
        await settle(1500)
        const alert = page.locator('[role=alertdialog]:visible')
        if (await alert.count()) { await alert.locator('button').last().click() } else break
      }
      await settle(2000)
      const after = sql(`select count(*), sum(case when "DoctorId" is null then 1 else 0 end) from "Appointments" where "ClinicId"='f64a8a75-94e1-4e08-a743-fa231abe438f' and "PatientId" is null and "CreatedAt" > now() - interval '2 minutes'`)
      const [n, nulls] = after.split('|').map(Number)
      n >= 1 && nulls === n ? ok('B1', `« Tout le cabinet » saved with no dentist (${n} new block)`) : bad('B1', 'block not saved cabinet-wide', `before ${before}, new ${after}`)
      if (await page.locator('[role=dialog]:visible').count()) await page.keyboard.press('Escape')
  } },
  { id: 'D1', run: async () => {
      await doctorCombo().click(); await pickOption('Tous les praticiens'); await settle(1500)
      await page.goto(`${WEB}/appointments?patientId=${PATIENT}`, { waitUntil: 'domcontentloaded' })
      await page.waitForSelector('[role=dialog] #doctor', { timeout: 30000 }); await settle(2000)
      const text = (await page.locator('[role=dialog] #doctor').innerText()).trim()
      text.includes('Salma Ben Youssef') ? ok('D1', `booking from the patient page opens on « ${text} »`) : bad('D1', 'own dentist not pre-picked', `« ${text} »`)
      await page.keyboard.press('Escape'); await settle(800)
      if (await page.locator('[role=alertdialog]:visible').count()) await page.locator('[role=alertdialog] button').first().click()
  } },
  { id: 'B2', run: async () => {
      await page.goto(`${WEB}/appointments?appointmentId=${OLD_UNASSIGNED}`, { waitUntil: 'domcontentloaded' })
      await page.waitForSelector('[role=dialog] #doctor', { timeout: 30000 }); await settle(2500)
      await page.locator('[role=dialog] #doctor').click(); await settle(500)
      const options = await page.getByRole('option').allInnerTexts()
      await page.keyboard.press('Escape'); await settle(300)
      await page.locator('[role=dialog] button:has-text("Enregistrer")').last().click(); await settle(1500)
      const txt = await page.locator('[role=dialog]').last().innerText()
      await shot(page, 'B2-edit-unassigned')
      const noNone = !options.some(o => o.includes('Aucun praticien'))
      const refused = txt.includes('Choisissez le praticien de ce rendez-vous')
      noNone && refused ? ok('B2', 'no « Aucun praticien », save refused with the sentence') : bad('B2', 'edit dialog rule', `options ${options.join(' | ')} · refused ${refused}`)
      const doc = sql(`select coalesce("DoctorId"::text,'NULL') from "Appointments" where "Id"='${OLD_UNASSIGNED}'`)
      if (doc !== 'NULL') bad('B2', 'the refused save still wrote a dentist', doc)
      await page.keyboard.press('Escape'); await settle(600)
      if (await page.locator('[role=alertdialog]:visible').count()) await page.locator('[role=alertdialog] button').last().click()
  } },
  { id: 'A11', run: async () => {
      requests.length = 0
      await page.goto(`${WEB}/a-cloturer`, { waitUntil: 'domcontentloaded' })
      await page.waitForSelector('#closure-doctor', { timeout: 30000 }); await settle(2500)
      const before = (await page.locator('#closure-doctor').innerText()).trim()
      await page.locator('#closure-doctor').click(); await pickOption('Nadia Trabelsi'); await settle(3000)
      const sent = requests.some(u => u.includes('to-close') && u.includes('doctorId=9ed3d280-e3a1-4f11-ad19-8c993dd0da94'))
      await shot(page, 'A11-a-cloturer')
      sent ? ok('A11', `select present (opened on « ${before} »), list re-read for Nadia`) : bad('A11', 'filter not sent', requests.filter(u => u.includes('to-close')).join(' '))
      await page.locator('#closure-doctor').click(); await pickOption('Tous les praticiens'); await settle(1000)
  } },
  { id: 'A8', run: async () => {
      await page.goto(`${WEB}/users`, { waitUntil: 'domcontentloaded' })
      await page.locator('button:has-text("Inviter un collègue"), button:has-text("Créer un compte")').first().click()
      await page.waitForSelector('#create-user-email', { timeout: 20000 })
      await page.fill('#create-user-fullname', `Dr Hela Mansour ${STAMP}`)
      await page.fill('#create-user-email', `hela.${STAMP}@ibnkhaldoun.test`)
      await page.locator('#create-user-role').click(); await pickOption('Administrateur'); await settle(400)
      await page.locator('#create-user-admin-dentist').click(); await settle(400)
      const fieldsShown = await page.locator('#create-user-firstname').count()
      await page.fill('#create-user-firstname', 'Hela'); await page.fill('#create-user-lastname', `Mansour${STAMP}`)
      await shot(page, 'A8-invite-admin-dentist')
      await page.click('[data-guide="user-submit"]'); await settle(3000)
      await page.keyboard.press('Escape'); await settle(1500)
      const linked = sql(`select count(*) from "Doctors" d join "Users" u on u."Id"=d."UserId" where u."Email"='hela.${STAMP}@ibnkhaldoun.test'`)
      const row = await page.getByText(new RegExp(`Praticien : Hela Mansour${STAMP}`)).count()
      fieldsShown && linked === '1' && row > 0 ? ok('A8', 'admin invited as a dentist: record linked, row says so') : bad('A8', 'admin-dentist invite', `fields ${fieldsShown}, linked ${linked}, row ${row}`)
  } },
  { id: 'A9', run: async () => {
      await page.locator('button:has-text("Inviter un collègue"), button:has-text("Créer un compte")').first().click()
      await page.waitForSelector('#create-user-email', { timeout: 20000 })
      await page.fill('#create-user-fullname', `Dr ${A.unlinkedFirst} ${A.unlinkedLast}`)
      await page.fill('#create-user-email', A.unlinkedEmail)
      await page.locator('#create-user-role').click(); await pickOption('Médecin'); await settle(600)
      const choice = (await page.locator('#create-user-practitioner').innerText().catch(() => '')).trim()
      const namesHidden = (await page.locator('#create-user-firstname').count()) === 0
      await shot(page, 'A9-invite-existing')
      await page.click('[data-guide="user-submit"]'); await settle(3000)
      await page.keyboard.press('Escape'); await settle(1500)
      const recs = sql(`select count(*), sum(case when "UserId" is not null then 1 else 0 end) from "Doctors" where "ClinicId"='f64a8a75-94e1-4e08-a743-fa231abe438f' and "FirstName"='${A.unlinkedFirst}' and "LastName"='${A.unlinkedLast}'`)
      choice.includes(`${A.unlinkedFirst} ${A.unlinkedLast}`) && namesHidden && recs === '1|1' ? ok('A9', `pre-picked « ${choice} », one record, linked`) : bad('A9', 'existing practitioner not linked', `choice « ${choice} », names hidden ${namesHidden}, records ${recs}`)
  } },
  { id: 'B4', run: async () => {
      await page.locator('button:has-text("Inviter un collègue"), button:has-text("Créer un compte")').first().click()
      await page.waitForSelector('#create-user-email', { timeout: 20000 })
      await page.fill('#create-user-fullname', 'Dr Sans Fiche')
      await page.fill('#create-user-email', `sans.fiche.${STAMP}@ibnkhaldoun.test`)
      await page.locator('#create-user-role').click(); await pickOption('Médecin'); await settle(500)
      const disabled = await page.locator('[data-guide="user-submit"]').isDisabled()
      disabled ? ok('B4', 'send disabled until the practitioner fields are filled') : bad('B4', 'send enabled with empty practitioner fields')
      await page.keyboard.press('Escape'); await settle(800)
      if (await page.locator('[role=alertdialog]:visible').count()) await page.locator('[role=alertdialog] button').last().click()
  } },
  { id: 'A10', run: async () => {
      await page.goto(`${WEB}/users`, { waitUntil: 'domcontentloaded' }); await settle(3000)
      const search = page.locator('input[type=search]:visible, input[placeholder*="Rechercher" i]:visible').last()
      if (await search.count()) { await search.fill(A.adminNoDocName); await settle(2000) }
      await page.locator(`button[aria-label="Actions pour ${A.adminNoDocName}"]:visible`).first().click(); await settle(400)
      await page.getByRole('menuitem', { name: 'Lier à un praticien' }).click()
      await page.waitForSelector('#link-practitioner-firstname', { timeout: 20000 })
      await page.fill('#link-practitioner-firstname', 'Sami'); await page.fill('#link-practitioner-lastname', `Ben Ali${STAMP}`)
      await shot(page, 'A10-link-dialog')
      await page.click('[role=dialog] button:has-text("Lier")')
      const toast = await page.getByText('Fiche praticien liée.').waitFor({ timeout: 10000 }).then(() => true, () => false)
      await settle(2500)
      const row = await page.getByText(new RegExp(`Praticien : Sami Ben Ali${STAMP}`)).count()
      toast && row > 0 ? ok('A10', 'linked; row reads « Praticien : … »') : bad('A10', 'link action', `toast ${toast}, row ${row}`)
  } },
  { id: 'D2', run: async () => {
      await page.goto(`${WEB}/documents/prescription`, { waitUntil: 'domcontentloaded' }); await settle(5000)
      // The prescriber is not a control: the editor prints the signed-in dentist into the document itself.
      const body = await page.locator('main').innerText()
      const values = await page.$$eval('input, textarea', els => els.map(e => e.value))
      const named = body.includes('Salma Ben Youssef') || values.some(v => v.includes('Salma Ben Youssef'))
      await shot(page, 'D2-prescription')
      named ? ok('D2', 'the ordonnance names the signed-in dentist') : bad('D2', 'prescriber default', body.slice(0, 200))
  } },
  { id: 'D3', run: async () => {
      await page.goto(`${WEB}/patients/${PATIENT}`, { waitUntil: 'domcontentloaded' })
      const named = await page.getByText('Amine Trabelsi').first().waitFor({ timeout: 30000 }).then(() => true, () => false)
      await settle(2500)
      const alerts = await page.locator('[role=alert]').allInnerTexts()
      named && alerts.filter(a => a.trim()).length === 0 ? ok('D3', 'patient page renders, no failure notice') : bad('D3', 'patient page', `named ${named}, alerts ${alerts.join(' | ')}`)
  } },
  { id: 'C1', run: async () => {
      for (const width of [390, 320]) {
        await page.setViewportSize({ width, height: 844 })
        await page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' }); await settle(5000)
        const over = await page.evaluate(() => { const m = document.querySelector('main'); return m ? m.scrollWidth - m.clientWidth : -1 })
        await shot(page, `C1-agenda-${width}`)
        if (over > 1) bad('C1', `sideways scroll at ${width}`, `${over}px`)
        if (width === 390) {
          // The agenda's own panel toggle is the date title in its phone header — not the « Aide » menu.
          await page.locator('button[aria-expanded]:visible').filter({ hasText: /nov|déc|janv|lun|mar|mer|jeu|ven|sam|dim/i }).first().click(); await settle(800)
          const hasSelect = await page.locator('#agenda-doctor-phone').count()
          await shot(page, 'C1-phone-panel-390')
          hasSelect ? ok('C1', 'phone panel offers « Praticien »') : bad('C1', 'phone panel has no dentist select')
        }
      }
  } },
  { id: 'C3', run: async () => {
      await page.setViewportSize({ width: 820, height: 1180 })
      await page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' }); await settle(5000)
      const over = await page.evaluate(() => { const m = document.querySelector('main'); return m ? m.scrollWidth - m.clientWidth : -1 })
      await shot(page, 'C3-agenda-820')
      over <= 1 ? ok('C3', 'no sideways scroll at 820') : bad('C3', 'sideways scroll at 820', `${over}px`)
  } },
  // ── run 3 — as Salma, two dentists ──
  { id: 'S5', run: async () => {
      await page.setViewportSize({ width: 1440, height: 900 })
      await page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' }); await agendaReady()
      if (!(await doctorCombo().innerText()).includes('Tous les praticiens')) { await doctorCombo().click(); await pickOption('Tous les praticiens'); await settle(1500) }
      // The view toggles carry a full and a short label, so match the aria-label, never the text.
      await page.locator('button[aria-label="Mois"]:visible').first().click(); await settle(3500)
      const chips = await page.$$eval('span[aria-hidden=true]', els => els.filter(e => e.offsetParent).map(e => e.textContent?.trim()).filter(t => /^(SB|NT)$/.test(t)))
      await shot(page, 'S5-month-1440')
      chips.includes('SB') && chips.includes('NT') ? ok('S5', `month entries carry ${[...new Set(chips)].join(',')}`) : bad('S5', 'no initials in the month view', chips.join(','))
      await page.locator('button[aria-label="Semaine"]:visible').first().click(); await settle(1000)
  } },
  { id: 'S6', run: async () => {
      await page.goto(`${WEB}/waiting-list`, { waitUntil: 'domcontentloaded' })
      const row = page.locator('tr:visible', { hasText: A.waitLast }).first()
      await row.waitFor({ timeout: 30000 }); await settle(1000)
      await row.locator('button:has-text("Promouvoir")').click()
      await page.waitForSelector('[role=dialog] #doctor', { timeout: 20000 }); await settle(1500)
      const text = (await page.locator('[role=dialog] #doctor').innerText()).trim()
      await shot(page, 'S6-waiting-list-booking')
      text.includes('Nadia Trabelsi') ? ok('S6', `booking from the waiting list opens on « ${text} »`) : bad('S6', 'preferred dentist not pre-picked', `« ${text} »`)
      await page.keyboard.press('Escape'); await settle(800)
      if (await page.locator('[role=alertdialog]:visible').count()) await page.locator('[role=alertdialog] button').first().click()
  } },
  { id: 'S4', run: async () => {
      await page.goto(`${WEB}/users`, { waitUntil: 'domcontentloaded' }); await settle(3000)
      const search = page.locator('input[type=search]:visible, input[placeholder*="Rechercher" i]:visible').last()
      if (await search.count()) { await search.fill(A.promoName); await settle(2000) }
      const row = page.locator('tr:visible', { hasText: A.promoName }).first()
      await row.locator('button[aria-label="Rôle"]').click(); await settle(400)
      await pickOption('Médecin'); await settle(600)
      await page.locator('[role=alertdialog] button:has-text("Confirmer")').click(); await settle(3500)
      const recs = sql(`select count(*), sum(case when d."UserId" = u."Id" then 1 else 0 end) from "Doctors" d, "Users" u where d."ClinicId"='${CLINIC}' and lower(d."Email")=lower('${A.promoEmail}') and u."Email"='${A.promoEmail}'`)
      const line = await page.getByText(new RegExp(`Praticien : Yosra ${A.promoLast}`)).count()
      await shot(page, 'S4-promote-to-medecin')
      recs === '1|1' && line > 0 ? ok('S4', 'promoted: the roster record was linked, no second one') : bad('S4', 'promotion did not link the existing record', `records|linked ${recs}, row ${line}`)
  } },
]

// ── run 3 — as the secretary, two dentists ──
const SECRETARY_SCENARIOS = [
  { id: 'S1', run: async () => {
      await agendaReady()
      const text = (await doctorCombo().innerText()).trim()
      await shot(page, 'S1-secretary-first-visit')
      text.includes('Tous les praticiens') ? ok('S1', `secretary opens on « ${text} »`) : bad('S1', 'secretary did not open on every dentist', `« ${text} »`)
  } },
  { id: 'S2', run: async () => {
      await page.goto(`${WEB}/appointments?patientId=${PATIENT}`, { waitUntil: 'domcontentloaded' })
      await page.waitForSelector('[role=dialog] #doctor', { timeout: 30000 }); await settle(2000)
      const start = (await page.locator('[role=dialog] #doctor').innerText()).trim()
      await page.locator('[role=dialog] button:has-text("Créer le rendez-vous")').click(); await settle(1500)
      const refused = (await page.locator('[role=dialog]').last().innerText()).includes('Choisissez le praticien de ce rendez-vous')
      await shot(page, 'S2-secretary-no-dentist')
      await page.locator('[role=dialog] #doctor').click(); await pickOption('Nadia Trabelsi'); await settle(400)
      const t0 = sql('select now()')
      await submitDialog('Créer le rendez-vous')
      const row = sql(`select coalesce(a."DoctorId"::text,'NULL'), coalesce(n."AlsoTargetUserId",'NULL') from "Appointments" a left join "StaffNotifications" n on n."AppointmentId"=a."Id" and n."Category"=6 where a."PatientId"='${PATIENT}' and a."CreatedAt" > '${t0}' order by a."CreatedAt" desc limit 1`)
      refused && row === `9ed3d280-e3a1-4f11-ad19-8c993dd0da94|${SECRETARY_USER}`
        ? ok('S2', `opened on « ${start} », refused without a dentist, saved for Nadia, prompt also to the secretary`)
        : bad('S2', 'secretary booking', `start « ${start} », refused ${refused}, saved ${row || 'nothing'}`)
  } },
]

// ── run 3 — one dentist (every practitioner but Salma retired on the copy), as the secretary ──
const ONE_DENTIST_SCENARIOS = [
  { id: 'O1', run: async () => {
      const lr = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: 'qa.secretary@ibnkhaldoun.test', password: 'QaAudit2026!y' }) })
      const lj = JSON.parse((await lr.text()) || '{}'); const tok = lj.value?.accessToken ?? lj.accessToken
      const at = new Date(Date.now() + 2 * 86400e3); at.setUTCHours(14, 0, 0, 0)
      const r = await fetch(`${API}/appointments`, { method: 'POST', headers: { Authorization: `Bearer ${tok}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({ patientId: PATIENT, appointmentDateTime: at.toISOString(), durationMinutes: 30, allowOverlap: true, allowOutsideWorkingHours: true }) })
      const raw = await r.text(); let j = null; try { j = JSON.parse(raw) } catch {}
      const doc = (j?.value ?? j)?.doctorId
      r.status < 300 && doc === SALMA_DOC ? ok('O1', `${r.status}, the only dentist was named`) : bad('O1', 'booking without a dentist at a one-dentist cabinet', `${r.status} doctorId=${doc} ${raw.slice(0, 160)}`)
  } },
  { id: 'O2', run: async () => {
      await page.goto(`${WEB}/appointments?patientId=${PATIENT}`, { waitUntil: 'domcontentloaded' })
      await page.waitForSelector('[role=dialog] #doctor', { timeout: 30000 }); await settle(2000)
      const start = (await page.locator('[role=dialog] #doctor').innerText()).trim()
      await shot(page, 'O2-one-dentist-booking')
      const t0 = sql('select now()')
      await submitDialog('Créer le rendez-vous')
      const doc = sql(`select coalesce("DoctorId"::text,'NULL') from "Appointments" where "PatientId"='${PATIENT}' and "CreatedAt" > '${t0}' order by "CreatedAt" desc limit 1`)
      start.includes('Salma Ben Youssef') && doc === SALMA_DOC ? ok('O2', `opened on « ${start} », saved for Salma untouched`) : bad('O2', 'one-dentist booking', `start « ${start} », saved ${doc || 'nothing'}`)
  } },
  { id: 'O3', run: async () => {
      await page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' }); await agendaReady()
      const chips = await page.$$eval('span[aria-hidden=true]', els => els.filter(e => e.offsetParent).map(e => e.textContent?.trim()).filter(t => /^(SB|NT|KH)$/.test(t)))
      await page.click('button[aria-label="Filtres de l\'agenda"]'); await settle(800)
      const txt = await page.locator('[data-radix-popper-content-wrapper]').last().innerText()
      await shot(page, 'O3-one-dentist-agenda')
      await page.keyboard.press('Escape')
      chips.length === 0 && !/Praticien/i.test(txt) && /Statut|Légende|Type/i.test(txt) ? ok('O3', 'no initials, no « Praticien » legend') : bad('O3', 'one-dentist agenda still shows dentist marks', `chips ${chips.join(',')} · legend ${txt.slice(0, 200)}`)
  } },
  { id: 'O4', run: async () => {
      await page.goto(`${WEB}/a-cloturer`, { waitUntil: 'domcontentloaded' })
      await page.getByText(/Période/).first().waitFor({ timeout: 30000 }); await settle(2500)
      const select = await page.locator('#closure-doctor').count()
      await shot(page, 'O4-one-dentist-a-cloturer')
      select === 0 ? ok('O4', 'no « Praticien » select beside « Période »') : bad('O4', 'dentist select shown at a one-dentist cabinet')
  } },
  { id: 'O5', run: async () => {
      await page.goto(`${WEB}/appointments?appointmentId=${OLD_UNASSIGNED}`, { waitUntil: 'domcontentloaded' })
      await page.waitForSelector('[role=dialog] #doctor', { timeout: 30000 }); await settle(2500)
      const start = (await page.locator('[role=dialog] #doctor').innerText()).trim()
      await submitDialog('Enregistrer')
      const refused = (await page.locator('[role=dialog]:visible').count()) > 0
        && (await page.locator('[role=dialog]').last().innerText()).includes('Choisissez le praticien')
      await shot(page, 'O5-one-dentist-old-rdv')
      const doc = sql(`select coalesce("DoctorId"::text,'NULL') from "Appointments" where "Id"='${OLD_UNASSIGNED}'`)
      !refused && doc === SALMA_DOC ? ok('O5', `opened on « ${start} », saved with Salma`) : bad('O5', 'old RDV at a one-dentist cabinet', `start « ${start} », refused ${refused}, DoctorId ${doc}`)
  } },
]

try {
  await login()
  const ONLY = process.env.ONLY?.split(',')
  const runAll = async list => {
    for (const s of list.filter(x => !ONLY || ONLY.includes(x.id))) {
      try { await s.run() } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]) }
    }
  }
  await runAll(SCENARIOS)
  page = await newPage()                                   // a fresh browser profile: her first visit
  await loginSecretary()
  await runAll(SECRETARY_SCENARIOS)
  const retired = sql(`update "Doctors" set "IsActive"=false where "ClinicId"='${CLINIC}' and "Id"<>'${SALMA_DOC}' and "IsActive" returning "Id"`)
  console.log(`  · one-dentist cabinet: ${retired.split('\n').filter(Boolean).length} practitioner(s) retired on the copy`)
  await runAll(ONE_DENTIST_SCENARIOS)
} catch (e) {
  console.log('LOGIN FAILED', e.message)
} finally {
  writeFileSync('walk-results.json', JSON.stringify(results, null, 2))
  console.log(findings.length ? `\n${findings.length} finding(s)` : '\nALL CLEAR')
  await browser.close()
}
