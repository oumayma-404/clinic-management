// QA walk — cabinet de groupe, run 1. One launch, every browser row of features/cabinet-de-groupe/qa/plan.md.
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

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
await ctx.route('**/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const page = await ctx.newPage()
const requests = []
page.on('request', r => { if (r.url().includes(':5099/api/')) requests.push(r.url()) })

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
]

try {
  await login()
  const ONLY = process.env.ONLY?.split(',')
  for (const s of SCENARIOS.filter(x => !ONLY || ONLY.includes(x.id))) {
    try { await s.run() } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]) }
  }
} catch (e) {
  console.log('LOGIN FAILED', e.message)
} finally {
  writeFileSync('walk-results.json', JSON.stringify(results, null, 2))
  console.log(findings.length ? `\n${findings.length} finding(s)` : '\nALL CLEAR')
  await browser.close()
}
