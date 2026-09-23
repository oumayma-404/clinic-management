// Wave-1 browser walk. One launch, every scenario, fixes nothing.
// Usage: node walk.mjs <scratchpad-dir-with-node_modules-and-totp.mjs>
import { readFileSync, mkdirSync } from 'node:fs'
import { execSync } from 'node:child_process'
import { dirname, join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { createRequire } from 'node:module'

const here = dirname(fileURLToPath(import.meta.url))
const pad = process.argv[2]
const { chromium } = createRequire(join(pad, 'package.json'))('playwright-core')
const { totp } = await import(pathToFileURL(join(pad, 'totp.mjs')).href)
const f = JSON.parse(readFileSync(join(here, 'fixtures.json'), 'utf8'))
const shots = join(here, 'shots'); mkdirSync(shots, { recursive: true })
const WEB = 'http://localhost:3000'

const sql = (q) => execSync(
  `docker exec clinic-postgres psql -U clinic_user -d clinic_management -At -F"|" -c "${q.replace(/"/g, '\\"')}"`,
  { encoding: 'utf8' }).trim()

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, skipped: why }) }

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
// The post-visit review popup swallows clicks — serve its queue empty.
await ctx.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const page = await ctx.newPage()

// ── sign in ──────────────────────────────────────────────────────────────────────────────────────────────
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

const dialog = () => page.locator('[role=dialog]:visible').last()
const toastText = async (re, ms = 8000) => {
  const end = Date.now() + ms
  while (Date.now() < end) {
    const t = await page.locator('[data-sonner-toaster]').innerText().catch(() => '')
    if (re.test(t)) return t
    await page.waitForTimeout(200)
  }
  return await page.locator('[data-sonner-toaster]').innerText().catch(() => '')
}

/** Open a visit's edit dialog and press Enregistrer; returns the PUT's status and the dialog text. */
async function saveVisit(id, shot) {
  await page.goto(`${WEB}/appointments?appointmentId=${id}`)
  const d = dialog()
  await d.waitFor({ timeout: 20000 })
  await page.waitForTimeout(2500) // devis + catalogue reads land and back-fill
  const text = await d.innerText()
  if (shot) await page.screenshot({ path: join(shots, shot) })
  const resp = page.waitForResponse((r) => r.url().includes(`/appointments/${id}`) && r.request().method() === 'PUT', { timeout: 15000 })
  await d.getByRole('button', { name: /^Enregistrer/ }).last().click()
  const r = await resp
  let body = ''; try { body = await r.text() } catch {}
  await page.waitForTimeout(800)
  return { status: r.status(), text, body }
}

/** Open a saved fiche and press its primary save; returns the PUT's status. */
async function resaveFiche(patientId, recordId) {
  await page.goto(`${WEB}/patients/${patientId}?editRecord=${recordId}`)
  const d = dialog()
  await d.waitFor({ timeout: 20000 })
  await page.waitForTimeout(2500)
  const resp = page.waitForResponse((r) => r.url().includes(`/dental-records/${recordId}`) && r.request().method() === 'PUT', { timeout: 15000 })
  await d.getByRole('button', { name: /^(Enregistrer|Corriger la note)/ }).last().click()
  const r = await resp
  let body = ''; try { body = await r.text() } catch {}
  await page.waitForTimeout(800)
  return { status: r.status(), body }
}

const scenarios = [
  ['A1', async () => {
    const { status, text, body } = await saveVisit(f.F1.visit, 'A1-visit-done-act.png')
    status === 200 ? ok('A1', 'visit on a recorded devis act saves (200)') : bad('A1', `PUT → ${status}`, body.slice(0, 200))
    ;/remettre au tarif/i.test(text) ? bad('A2', 'price field unlocked (« remettre au tarif » shown)') : ok('A2', 'no « remettre au tarif » on the devis act')
  }],
  ['A3', async () => {
    const { status, body } = await saveVisit(f.F2.visit)
    status === 200 ? ok('A3', 'visit held on a cancelled devis saves (200)') : bad('A3', `PUT → ${status}`, body.slice(0, 200))
  }],
  ['A4', async () => {
    const { status, body } = await saveVisit(f.F3.visit)
    status === 200 ? ok('A4', 'visit carrying an archived act saves (200)') : bad('A4', `PUT → ${status}`, body.slice(0, 200))
    const rows = sql(`select count(*) from "AppointmentProcedures" where "AppointmentId"='${f.F3.visit}' and "ProcedureTypeId"='${f.F3.procedureType}'`)
    rows === '1' ? ok('A4', 'archived act still on the visit') : bad('A4', 'archived act lost from the visit', rows)
  }],
  ['E1', async () => {
    const before = sql(`select count(*) from "TreatmentPlans" where "PatientId"='${f.patient}'`)
    const { status, body } = await saveVisit(f.F4.visit)
    const after = sql(`select count(*) from "TreatmentPlans" where "PatientId"='${f.patient}'`)
    status === 200 ? ok('E1', 'implant visit saves') : bad('E1', `PUT → ${status}`, body.slice(0, 200))
    before === after ? ok('E1', `no treatment created (${before} → ${after})`) : bad('E1', 'saving the visit created a treatment', `${before} → ${after}`)
  }],
  ['R1', async () => {
    const { status, body } = await saveVisit(f.R1.visit)
    status === 200 ? ok('R1', 'ordinary visit saves') : bad('R1', `PUT → ${status}`, body.slice(0, 200))
  }],
  ['C-730', async () => {
    await page.setViewportSize({ width: 1536, height: 730 })
    await page.goto(`${WEB}/appointments?appointmentId=${f.F1.visit}`)
    const d = dialog(); await d.waitFor({ timeout: 20000 }); await page.waitForTimeout(2000)
    const btn = d.getByRole('button', { name: /^Enregistrer/ }).last()
    const box = await btn.boundingBox()
    await page.screenshot({ path: join(shots, 'C-730-edit-dialog.png') })
    box && box.y + box.height <= 730 ? ok('C-730', `Enregistrer in view (bottom ${Math.round(box.y + box.height)} px)`) : bad('C-730', 'Enregistrer below the fold', JSON.stringify(box))
    await page.setViewportSize({ width: 1440, height: 900 })
  }],
  ['B1', async () => {
    const { status, body } = await resaveFiche(f.patient, f.F1.record)
    status === 200 ? ok('B1', 'fiche on a completed devis re-saves (200)') : bad('B1', `PUT → ${status}`, body.slice(0, 200))
    const st = sql(`select "Status" from "TreatmentPlans" where "Id"='${f.F1.plan}'`)
    st === '3' ? ok('B1', 'devis still Completed') : bad('B1', 'devis status moved', st)
  }],
  ['B2', async () => {
    const a = await resaveFiche(f.patient, f.F5.record)
    const b = await resaveFiche(f.patient, f.F5.record)
    a.status === 200 && b.status === 200 ? ok('B2', 'walk-in fiche re-saved twice') : bad('B2', `PUT → ${a.status}/${b.status}`, (a.body + b.body).slice(0, 200))
    const done = sql(`select count(*) from "TreatmentPlanItemSteps" where "TreatmentPlanItemId"='${f.F5.item}' and "DoneDate" is not null`)
    done === '1' ? ok('B2', 'exactly 1 séance done') : bad('B2', 'extra séances marked done', `done=${done}`)
  }],
  ['B3', async () => {
    await page.goto(`${WEB}/patients/${f.patient}?addRecord=1&appointmentId=${f.F6.visit}`)
    const d = dialog(); await d.waitFor({ timeout: 20000 }); await page.waitForTimeout(3000)
    const resp = page.waitForResponse((r) => /\/dental-records$/.test(new URL(r.url()).pathname) && r.request().method() === 'POST', { timeout: 15000 })
    await d.getByRole('button', { name: /^(Confirmer la séance|Créer la fiche|Enregistrer)/ }).last().click()
    const r = await resp; const body = await r.text().catch(() => '')
    r.status() === 200 ? ok('B3', 'fiche for a visit whose séance was removed saves') : bad('B3', `POST → ${r.status()}`, body.slice(0, 200))
    const link = sql(`select count(*) from "AppointmentProcedures" where "AppointmentId"='${f.F6.visit}' and "TreatmentPlanItemId"='${f.F6.item}' and "TreatmentPlanItemStepId" is null`)
    link === '1' ? ok('B3', 'visit kept on the act, dead séance dropped') : bad('B3', 'visit link wrong', link)
    await page.waitForTimeout(800)
  }],
  ['B6', async () => {
    await page.goto(`${WEB}/patients/${f.patient2}`)
    await page.getByRole('button', { name: /Ajouter un acte dentaire/ }).first().click()
    const d = dialog(); await d.waitFor({ timeout: 20000 }); await page.waitForTimeout(2500)
    await d.locator('#plan-item').click()
    // Scoped to the Select's own listbox: the inline act catalogue behind it also renders role=option rows.
    const number = sql(`select "Number" from "TreatmentPlans" where "Id"='${f.F7.plan}'`)
    await page.locator('[role=listbox] [role=option]').filter({ hasText: number }).first().click()
    await page.waitForTimeout(800)
    const picker = d.locator('#plan-item-step')
    if (!(await picker.count())) { bad('B6', 'no « Séance » picker for a stepped devis act'); return }
    await picker.click()
    const labels = await page.locator('[role=listbox] [role=option]').allInnerTexts()
    labels.length === 3 ? ok('B6', `picker lists ${labels.length} pending séances`) : bad('B6', 'picker options', labels.join(' | '))
    await page.locator('[role=listbox] [role=option]').last().click()
    await page.waitForTimeout(500)
    await page.setViewportSize({ width: 320, height: 780 }); await page.waitForTimeout(800)
    const overflow = await page.evaluate(() => {
      const c = [...document.querySelectorAll('[role=dialog]')].pop()
      return c ? Math.round(c.getBoundingClientRect().right - window.innerWidth) : 'no dialog'
    })
    await page.screenshot({ path: join(shots, 'C-320-fiche-seance-picker.png') })
    overflow <= 0 ? ok('C-320', 'fiche dialog inside 320 px') : bad('C-320', 'fiche dialog overflows at 320', String(overflow))
    await page.setViewportSize({ width: 1440, height: 900 }); await page.waitForTimeout(500)
    const resp = page.waitForResponse((r) => /\/dental-records$/.test(new URL(r.url()).pathname) && r.request().method() === 'POST', { timeout: 15000 })
    await d.getByRole('button', { name: /^(Créer la fiche|Confirmer la séance|Enregistrer)/ }).last().click()
    const r = await resp; const body = await r.text().catch(() => '')
    if (r.status() !== 200) { bad('B6', `POST → ${r.status()}`, body.slice(0, 200)); return }
    const last = f.F7.steps[f.F7.steps.length - 1].id
    const done = sql(`select "Id" from "TreatmentPlanItemSteps" where "TreatmentPlanItemId"='${f.F7.item}' and "DoneDate" is not null`)
    done === last ? ok('B6', 'the chosen séance (the last) is the one marked done') : bad('B6', 'wrong séance marked', done)
    await page.waitForTimeout(800)
  }],
  ['C1', async () => {
    await page.goto(`${WEB}/patients/${f.patient2}?editRecord=${f.F8.record}`)
    const d = dialog(); await d.waitFor({ timeout: 20000 }); await page.waitForTimeout(3000)
    const beforeText = await d.innerText()
    ;/Aucun honoraire sur cette séance/.test(beforeText) ? ok('C1', 'reopened devis-carried card is locked') : bad('C1', 'reopened card not marked as devis-carried')
    await d.locator('#plan-item').click()
    await page.locator('[role=option]').filter({ hasText: /^Aucun$/ }).first().click()
    await page.waitForTimeout(800)
    const afterText = await d.innerText()
    await page.screenshot({ path: join(shots, 'C1-aucun.png') })
    ;!/Aucun honoraire sur cette séance/.test(afterText) ? ok('C1', '« Aucun » gives the card its price back') : bad('C1', 'card still says « Aucun honoraire »')
    const resp = page.waitForResponse((r) => r.url().includes(`/dental-records/${f.F8.record}`) && r.request().method() === 'PUT', { timeout: 15000 })
    await d.getByRole('button', { name: /^(Enregistrer|Corriger la note)/ }).last().click()
    const r = await resp; const body = await r.text().catch(() => '')
    if (r.status() !== 200) { bad('C1', `PUT → ${r.status()}`, body.slice(0, 200)); return }
    const item = sql(`select "Status", coalesce("LinkedDentalRecordId"::text,'-') from "TreatmentPlanItems" where "Id"='${f.F8.item}'`)
    const plan = sql(`select "Status" from "TreatmentPlans" where "Id"='${f.F8.plan}'`)
    item.endsWith('|-') && plan !== '3' ? ok('C1', `act detached (${item}), devis reopened (${plan})`) : bad('C1', 'devis still holds the act', `${item} plan=${plan}`)
    await page.waitForTimeout(800)
  }],
  ['C3', async () => {
    await page.goto(`${WEB}/patients/${f.patient2}`)
    await page.getByRole('button', { name: /Ajouter un acte dentaire/ }).first().click()
    const d = dialog(); await d.waitFor({ timeout: 20000 }); await page.waitForTimeout(2500)
    // Pick the act first, from the inline catalogue.
    const search = d.locator('input[placeholder*="acte" i], input[placeholder*="Rechercher" i]').first()
    if (!(await search.count())) { skip('C3', 'catalogue search box not found'); return }
    await search.fill('Soin de carie')
    await page.waitForTimeout(500)
    await d.getByText(/^Soin de carie/).first().click()
    await page.waitForTimeout(500)
    await d.locator('#plan-item').click()
    await page.locator('[role=option]').filter({ hasText: /Soin de carie/ }).first().click()
    await page.waitForTimeout(1000)
    const text = await d.innerText()
    await page.screenshot({ path: join(shots, 'C3-hand-link.png') })
    ;/Aucun honoraire sur cette séance/.test(text) ? ok('C3', 'hand-linked card locks its price') : bad('C3', 'card kept its tarif after a hand link')
    await page.keyboard.press('Escape')
  }],
  ['D4', async () => {
    await page.goto(`${WEB}/treatment-plans/${f.F13.plan}`)
    await page.waitForSelector('text=/Couronne/', { timeout: 20000 }); await page.waitForTimeout(2000)
    const text = await page.locator('main').innerText()
    ;/Planifier/.test(text) && !/Voir le RDV/.test(text) ? ok('D4', 'disregarded visit no longer counts as a booking') : bad('D4', 'devis still reads the removed visit as booked', text.slice(0, 300))
  }],
  ['R4', async () => {
    await page.goto(`${WEB}/patients/${f.patient2}?addRecord=1&appointmentId=${f.R4.visit}`)
    const d = dialog(); await d.waitFor({ timeout: 20000 }); await page.waitForTimeout(3000)
    const text = await d.innerText()
    ;/Aucun honoraire sur cette séance/.test(text) ? ok('R4', 'booked devis act locked at 0 on the fiche') : bad('R4', 'booked devis act not locked')
    const resp = page.waitForResponse((r) => /\/dental-records$/.test(new URL(r.url()).pathname) && r.request().method() === 'POST', { timeout: 15000 })
    await d.getByRole('button', { name: /^(Confirmer la séance|Créer la fiche)/ }).last().click()
    const r = await resp; const body = await r.text().catch(() => '')
    if (r.status() !== 200) { bad('R4', `POST → ${r.status()}`, body.slice(0, 200)); return }
    const st = sql(`select "Status" from "TreatmentPlanItems" where "Id"='${f.R4.item}'`)
    st === '1' ? ok('R4', 'devis act marked done') : bad('R4', 'devis act not marked done', st)
    await page.waitForTimeout(800)
  }],
  ['D1', async () => {
    await page.goto(`${WEB}/treatment-plans/${f.F10.plan}`)
    await page.waitForSelector('text=/Traitement de canal/', { timeout: 20000 }); await page.waitForTimeout(1500)
    await page.getByRole('button', { name: 'Autres actions sur ce devis' }).first().click()
    await page.getByRole('menuitem', { name: /Arrêter le traitement/ }).click()
    const d = page.locator('[role=alertdialog]:visible').last(); await d.waitFor({ timeout: 10000 })
    const text = await d.innerText()
    await page.screenshot({ path: join(shots, 'D1-cancel-dialog.png') })
    ;/rendez-vous prévus seront libérés/.test(text) ? ok('D1', 'cancel dialog says the booked visit is freed') : bad('D1', 'cancel dialog is silent about the booking', text.slice(0, 300))
    await d.locator('#plan-cancel-reason').fill('QA — patient a renoncé')
    const resp = page.waitForResponse((r) => r.url().includes(`/treatment-plans/${f.F10.plan}/stop`), { timeout: 15000 })
    await d.getByRole('button', { name: /Annuler le devis/ }).click()
    const r = await resp
    if (r.status() !== 200) { bad('D1', `stop → ${r.status()}`, (await r.text()).slice(0, 200)); return }
    await page.waitForTimeout(1500)
    const v = sql(`select a."Status", (select count(*) from "AppointmentProcedures" p where p."AppointmentId"=a."Id" and p."TreatmentPlanItemId" is not null) from "Appointments" a where a."Id"='${f.F10.visit}'`)
    v === '5|0' ? ok('D1', 'booked visit cancelled and unlinked') : bad('D1', 'booked visit left as it was', v)
  }],
  ['D2', async () => {
    await page.goto(`${WEB}/treatment-plans/${f.F11.plan}`)
    await page.waitForSelector('text=/Couronne/', { timeout: 20000 }); await page.waitForTimeout(1500)
    const direct = page.getByRole('button', { name: /Supprimer le traitement/ })
    if (await direct.count()) await direct.first().click()
    else {
      await page.getByRole('button', { name: 'Autres actions sur ce devis' }).first().click()
      await page.getByRole('menuitem', { name: /Supprimer le traitement/ }).click()
    }
    const d = page.locator('[role=alertdialog]:visible').last(); await d.waitFor({ timeout: 10000 })
    const text = await d.innerText()
    ;/rendez-vous prévus seront libérés/.test(text) ? ok('D2', 'delete dialog says the booked visit is freed') : bad('D2', 'delete dialog is silent about the booking', text.slice(0, 300))
    const resp = page.waitForResponse((r) => r.url().includes(`/treatment-plans/${f.F11.plan}`) && r.request().method() === 'DELETE', { timeout: 15000 })
    await d.getByRole('button', { name: /^Supprimer$/ }).click()
    const r = await resp
    if (r.status() >= 300) { bad('D2', `delete → ${r.status()}`); return }
    await page.waitForTimeout(1500)
    const v = sql(`select a."Status", (select count(*) from "AppointmentProcedures" p where p."AppointmentId"=a."Id" and p."TreatmentPlanItemId" is not null) from "Appointments" a where a."Id"='${f.F11.visit}'`)
    v === '5|0' ? ok('D2', 'booked visit cancelled and unlinked') : bad('D2', 'booked visit left as it was', v)
  }],
  ['A5', async () => {
    await page.goto(`${WEB}/appointments?patientId=${f.patient}`)
    const d = dialog(); await d.waitFor({ timeout: 20000 }); await page.waitForTimeout(3000)
    const row = d.getByRole('button', { name: /Détartrage devis/ })
    if (!(await row.count())) { skip('A5', 'suggestion row for the devis act not found'); return }
    await row.first().click(); await page.waitForTimeout(800)
    const withAct = await d.innerText()
    if (!/Détartrage devis/.test(withAct)) { skip('A5', 'devis act not added'); return }
    await d.getByRole('combobox').first().click()
    await page.locator('[cmdk-input]').last().fill(`QAB Flex ${f.ts}`)
    await page.waitForTimeout(1500)
    await page.locator('[cmdk-item]').filter({ hasText: /QAB/ }).first().click()
    await page.waitForTimeout(1500)
    const after = await d.innerText()
    await page.screenshot({ path: join(shots, 'A5-patient-switch.png') })
    ;!/Détartrage devis/.test(after) ? ok('A5', 'switching patient drops the first patient’s devis act') : bad('A5', 'first patient’s devis act survives the switch')
    await page.keyboard.press('Escape')
  }],
]

const only = process.argv[3] ? process.argv[3].split(',') : null
for (const [id, run] of scenarios.filter(([id]) => !only || only.includes(id))) {
  try { await run() } catch (e) { bad(id, 'threw', e.message.split('\n')[0]); await page.screenshot({ path: join(shots, `${id}-threw.png`) }).catch(() => {}) }
}

await browser.close()
console.log('\n' + (findings.filter((x) => !x.skipped).length ? 'FINDINGS' : 'ALL CLEAR'))
for (const x of findings) console.log(JSON.stringify(x))
