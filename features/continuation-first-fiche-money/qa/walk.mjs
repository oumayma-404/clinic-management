// QA walk — continuation-first-fiche-money. ISOLATED stack only: web :3093 → API :5093 → database clinic_qa_cff.
// Run from the scratchpad (playwright-core lives there):
//   node <worktree>/features/continuation-first-fiche-money/qa/walk.mjs <scratchpad>/qa
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const QA = process.argv[2]
if (!QA) throw new Error('usage: node walk.mjs <scratchpad/qa>')
const require = createRequire(path.join(QA, 'package.json'))
const { chromium } = require('playwright-core')
const { totp } = await import(pathToFileURL(path.join(QA, 'totp.mjs')).href)

const HERE = path.dirname(fileURLToPath(import.meta.url))
const SHOTS = path.join(HERE, 'shots')
fs.mkdirSync(SHOTS, { recursive: true })
const FX = JSON.parse(fs.readFileSync(path.join(QA, 'fixtures.json'), 'utf8'))

const WEB = 'http://localhost:3093'
const DB = 'clinic_qa_cff'
if (!WEB.includes(':3093') || DB !== 'clinic_qa_cff') throw new Error('refusing: not the isolated stack')
const EMAIL = 'salma.benyoussef@cabinet-ibnkhaldoun.tn'
const PASSWORD = 'QaAudit2026!y'
const SECRET = '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'

// ── outcome collectors ────────────────────────────────────────────────────────────────────────────────────
const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why, skipped: true }) }
const check = (id, cond, m, d = '') => (cond ? ok(id, m) : bad(id, m, d))
const norm = (s) => (s ?? '').replace(/[  ]/g, ' ').replace(/\s+/g, ' ').trim()

// ── read-only SQL against the COPY ────────────────────────────────────────────────────────────────────────
function sql(q) {
  const out = execFileSync('docker', ['exec', 'clinic-postgres', 'psql', '-U', 'clinic_user', '-d', DB, '-At', '-F', '|', '-c', q], { encoding: 'utf8' })
  return out.trim() ? out.trim().split('\n').map((l) => l.split('|')) : []
}
const notesOf = (ficheId) => sql(`select "Id","Number","Status","TotalTtc","AmountCollected",coalesce("SupersedesInvoiceId"::text,''),coalesce("TreatmentPlanId"::text,'') from "Invoices" where "DentalRecordId"='${ficheId}' order by "CreatedAt"`)
  .map(([id, number, status, ttc, collected, supersedes, plan]) => ({ id, number, status: +status, ttc: +ttc, collected: +collected, supersedes, plan }))
const paymentsOf = (invoiceId) => sql(`select "Amount","IsVoided","PaidOn"::date from "Payments" where "InvoiceId"='${invoiceId}' order by "CreatedAt"`)
  .map(([amount, voided, paidOn]) => ({ amount: +amount, voided: voided === 't', paidOn }))
const ficheRow = (ficheId) => { const [r] = sql(`select "Cost","AmountPaid" from "DentalRecords" where "Id"='${ficheId}'`); return r ? { cost: +r[0], paid: +r[1] } : null }
const planRow = (planId) => sql(`select p."TotalPlanned", i."DesignationFr", i."PlannedCost", coalesce(i."BilledOnInvoiceId"::text,'') from "TreatmentPlanItems" i join "TreatmentPlans" p on p."Id"=i."TreatmentPlanId" where p."Id"='${planId}' order by i."SequenceNumber"`)
const S = { Issued: 1, PartiallyPaid: 2, Paid: 3, Cancelled: 4 }

// ── browser helpers ───────────────────────────────────────────────────────────────────────────────────────
const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
// The post-visit review popup swallows clicks; serve its queue empty (clinic-browser skill).
await ctx.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const page = await ctx.newPage()
const consoleErrors = []
page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(norm(m.text()).slice(0, 200)) })

async function shot(name, size) {
  if (size) await page.setViewportSize(size)
  await page.waitForTimeout(400)
  await page.bringToFront()
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`), timeout: 15000 })
}
async function toastText(timeout = 15000) {
  const t0 = Date.now()
  while (Date.now() - t0 < timeout) {
    const t = norm(await page.locator('[data-sonner-toaster]').innerText().catch(() => ''))
    if (t) return t
    await page.waitForTimeout(250)
  }
  return ''
}
async function waitToast(re, timeout = 20000) {
  const t0 = Date.now(); let last = ''
  while (Date.now() - t0 < timeout) {
    last = norm(await page.locator('[data-sonner-toaster]').innerText().catch(() => ''))
    if (re.test(last)) return last
    await page.waitForTimeout(250)
  }
  return last
}
const ficheDialog = () => page.locator('[role="dialog"]:visible').filter({ hasText: 'Actes de la séance' }).first()
async function openFiche(patientId, ficheId) {
  await page.goto(`${WEB}/patients/${patientId}?tab=medical-records&editRecord=${ficheId}`)
  await ficheDialog().waitFor({ state: 'visible', timeout: 60000 })
  await page.waitForTimeout(1500) // hydration + the plan data that decides the act's pricing
  return ficheDialog()
}
async function closeFiche() {
  await page.keyboard.press('Escape')
  // One answer to the discard prompt, never two: a second click lands on a prompt already fading out (run 1, B2).
  const discard = page.locator('[role="alertdialog"]:visible').filter({ hasText: 'Abandonner les modifications' })
  const prompted = await discard.waitFor({ state: 'visible', timeout: 1500 }).then(() => true, () => false)
  if (prompted) await discard.getByRole('button', { name: 'Abandonner', exact: true }).click({ timeout: 5000 })
  await ficheDialog().waitFor({ state: 'hidden', timeout: 10000 }).catch(() => {})
}
const submit = () => page.locator('[data-guide="record-submit"]:visible').first()
const correctionDialog = () => page.locator('[role="alertdialog"]:visible').filter({ hasText: 'Corriger la note' }).first()
async function setPaid(value) {
  const f = page.locator('#paid:visible').first()
  await f.fill('')
  await f.fill(String(value))
  await f.press('Tab')
  await page.waitForTimeout(300)
}

// ── sign in (inside this run; never a stored state) ───────────────────────────────────────────────────────
async function signIn() {
  await page.goto(`${WEB}/login`)
  await page.fill('input[type=email]', EMAIL)
  await page.fill('input[type=password]', PASSWORD)
  await page.click('button:has-text("Se connecter")')
  const code = page.locator('input[inputmode=numeric]').first()
  await code.waitFor({ timeout: 30000 })
  const s = Math.floor(Date.now() / 1000)
  await page.waitForTimeout((30 - (s % 30) + 1) * 1000) // a fresh window: a code computed late expires in flight
  await code.fill(totp(SECRET)) // auto-submits on the 6th digit
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60000 })
}

const P1 = FX.p1, P2 = FX.p2, P3 = FX.p3, P4 = FX.p4, P5 = FX.p5
const ctxState = {}

const SCENARIOS = [
  { id: 'A1', run: async () => {
    const d = await openFiche(P1.id, P1.f1.id)
    const text = norm(await d.innerText())
    const btn = norm(await submit().innerText())
    const paid = await page.locator('#paid:visible').first().inputValue().catch(() => '(absent)')
    console.log(`     footer=« ${btn} » · Payé=« ${paid} »`)
    await shot('A1-1440')
    check('A1', /Enregistrer la séance — 300,000 DT/.test(btn), 'footer offers a plain save at 300,000 DT', `got « ${btn} »`)
    check('A1', !/Sur la note n°/.test(text), 'act is not shown as « Sur la note n° »')
    check('A1', /^100([,.]0+)?$/.test(paid.trim()), '« Payé » holds 100', `got « ${paid} »`)
  } },
  { id: 'A2', run: async () => {
    await submit().click()
    const t = await waitToast(/Fiche de soins mise à jour/)
    check('A2', /Fiche de soins mise à jour/.test(t), 'save toast shown', `toast « ${t} »`)
    await ficheDialog().waitFor({ state: 'hidden', timeout: 15000 }).catch(() => {})
    const notes = notesOf(P1.f1.id), f = ficheRow(P1.f1.id)
    console.log('     notes', JSON.stringify(notes), 'fiche', JSON.stringify(f))
    check('A2', notes.length === 1 && notes[0].status === S.PartiallyPaid && notes[0].collected === 100, 'note untouched: one note, 300 / 100, PartiallyPaid')
    check('A2', paymentsOf(notes[0].id).every((p) => !p.voided), 'payment still live')
    check('A2', f.cost === 300 && f.paid === 100, 'fiche still Cost 300 / Paid 100', JSON.stringify(f))
  } },
  { id: 'A3', run: async () => {
    await openFiche(P1.id, P1.f1.id)
    await setPaid(0)
    const btn = norm(await submit().innerText())
    check('A3', /Corriger la note — 300,000 DT/.test(btn), 'footer becomes « Corriger la note — 300,000 DT »', `got « ${btn} »`)
    // F-1 (run 1): the line under « Payé » must point at the button, never say « ne se diminue pas ici ».
    const under = norm(await ficheDialog().innerText())
    check('F1', /déjà payés sur la note · « Corriger la note » la remplace/.test(under) && !/ne se diminue pas/.test(under),
      'line under « Payé » points at « Corriger la note »', under.match(/[^.]{0,40}déjà payés sur la note[^.]{0,80}/)?.[0])
    await submit().click()
    const dlg = correctionDialog()
    await dlg.waitFor({ timeout: 10000 })
    const t = norm(await dlg.innerText())
    console.log(`     dialog: ${t}`)
    check('A3', /Note de cette séance annulée · payé 100,000 DT\s*300,000 DT/.test(t), 'old note row: payé 100 · 300')
    check('A3', /Nouvelle note, séance corrigée · payé 0,000 DT\s*300,000 DT/.test(t), 'new note row: payé 0 · 300')
    check('A3', /passe de 100,000 DT à 0,000 DT/.test(t), '« passe de 100 à 0 » warning')
    check('A3', /Il reste 300,000 DT à encaisser sur la nouvelle note/.test(t), '« Il reste 300 » on the new note')
    check('A3', /sans paiement/.test(t), 'description says « sans paiement »')
    await shot('A3-1536x730', { width: 1536, height: 730 })
    // the dialog's own buttons must be reachable at the owner's laptop height
    const box = await dlg.getByRole('button', { name: 'Corriger', exact: true }).boundingBox()
    check('A3', !!box && box.y + box.height <= 730, '« Corriger » on screen at 1536×730', JSON.stringify(box))
    await shot('A3-390', { width: 390, height: 844 })
    // C1 — the 320 px floor, measured while the dialog is up
    await page.setViewportSize({ width: 320, height: 700 })
    await page.waitForTimeout(400)
    await dlg.evaluate((el) => { el.scrollTop = el.scrollHeight })
    const m = await dlg.evaluate((el) => ({ sw: el.scrollWidth, cw: el.clientWidth, docW: document.documentElement.scrollWidth }))
    const b1 = await dlg.getByRole('button', { name: 'Corriger', exact: true }).boundingBox()
    const b2 = await dlg.getByRole('button', { name: 'Annuler', exact: true }).boundingBox()
    console.log('     C1', JSON.stringify({ m, b1, b2 }))
    const inside = (b) => b && b.x >= 0 && b.x + b.width <= 320 && b.y >= 0 && b.y + b.height <= 700
    check('C1', m.sw <= m.cw && m.docW <= 320, 'no horizontal overflow at 320', JSON.stringify(m))
    check('C1', inside(b1) && inside(b2), 'both buttons fully on screen at 320×700', JSON.stringify({ b1, b2 }))
    await shot('C1-320')
    await page.setViewportSize({ width: 1440, height: 900 })
    await page.waitForTimeout(300)
  } },
  { id: 'A4', run: async () => {
    const before = notesOf(P1.f1.id)
    await correctionDialog().getByRole('button', { name: 'Corriger', exact: true }).click()
    const t = await waitToast(/émise/)
    console.log(`     toast « ${t} »`)
    check('A4', /Note n° \S+ émise — 0,000 DT payés/.test(t), 'toast « Note n° … émise — 0,000 DT payés »', `toast « ${t} »`)
    await ficheDialog().waitFor({ state: 'hidden', timeout: 15000 }).catch(() => {})
    const notes = notesOf(P1.f1.id)
    console.log('     notes', JSON.stringify(notes))
    const old = notes.find((n) => n.id === before[0].id)
    const fresh = notes.find((n) => n.id !== before[0].id)
    check('A4', old && old.status === S.Cancelled, 'old note cancelled')
    check('A4', old && paymentsOf(old.id).every((p) => p.voided), 'old payment voided (jamais reçu)')
    check('A4', fresh && fresh.ttc === 300 && fresh.collected === 0 && fresh.status === S.Issued, 'NEW note issued: 300 / 0', JSON.stringify(fresh))
    check('A4', fresh && fresh.supersedes === old?.id, 'new note supersedes the old one')
    const f = ficheRow(P1.f1.id)
    check('A4', f.cost === 300 && f.paid === 0, 'fiche Cost 300 / Paid 0', JSON.stringify(f))
    const plan = planRow(P1.devis.id)
    console.log('     devis', JSON.stringify(plan))
    check('A4', plan.length === 2 && +plan[0][0] === 200 && +plan[0][2] === 0 && +plan[1][2] === 200, 'devis unchanged: total 200, act 0, reste 200')
    ctxState.p1NewNote = fresh
  } },
  { id: 'A5', run: async () => {
    await page.goto(`${WEB}/treatment-plans/${P1.devis.id}`)
    const link = page.locator('a[aria-label^="Corriger la séance 1"]:visible').first()
    await link.waitFor({ timeout: 60000 })
    const main = norm(await page.locator('main').innerText())
    const num = ctxState.p1NewNote?.number
    const rowRe = new RegExp(`Note n° ${num}.{0,80}payé 0,000 DT · reste à payer 300,000 DT`)
    check('A5', !!num && rowRe.test(main), `row names the new note ${num} « payé 0 · reste 300 »`, main.match(/Note n°.{0,120}/)?.[0])
    // F-2 / F-3 (run 1): no cancelled note named in the devis text, no false « avec d'autres actes ».
    const oldNum = P1.f1.billing.invoiceNumber
    check('F2', !main.includes(oldNum) && /sa propre note d'honoraires/.test(main),
      `devis text names no cancelled note (${oldNum})`, main.match(/La 1re séance[^.]*\./)?.[0])
    check('F3', !/avec d'autres actes/.test(main), 'no « avec d\'autres actes » on a note billing one act',
      main.match(/.{0,60}avec d'autres actes.{0,30}/)?.[0])
    await shot('A5-1440')
    await link.click()
    await page.waitForURL(/\/patients\//, { timeout: 30000 })
    await ficheDialog().waitFor({ state: 'visible', timeout: 60000 })
    const paid = await page.locator('#paid:visible').first().inputValue().catch(() => '(absent)')
    check('A5', page.url().includes(P1.id), '« Corriger » lands on the patient page')
    check('A5', /^0([,.]0+)?$/.test(paid.trim()) || paid.trim() === '', 'the séance-1 fiche opened (Payé 0)', `Payé « ${paid} »`)
    await shot('A5-fiche-1440')
    await closeFiche()
  } },
  { id: 'A6', run: async () => {
    await page.goto(`${WEB}/patients/${P1.id}`)
    await page.waitForTimeout(4000)
    const main = norm(await page.locator('main').innerText())
    const m = main.match(/Reste à payer[^0-9]{0,30}([0-9 ]+,[0-9]{3}) DT/)
    console.log(`     header: ${m?.[0]}`)
    check('A6', /500,000 DT/.test(main), 'patient owes 500,000 DT (note 300 + devis 200)', m?.[0] ?? '(figure not found)')
    await shot('A6-1440')
  } },
  { id: 'B1', run: async () => {
    const before = notesOf(P2.f1.id)
    await openFiche(P2.id, P2.f1.id)
    await setPaid(50)
    await submit().click()
    const dlg = correctionDialog()
    await dlg.waitFor({ timeout: 10000 })
    const t = norm(await dlg.innerText())
    check('B1', /payé 100,000 DT/.test(t) && /payé 50,000 DT/.test(t), 'dialog: payé 100 → payé 50')
    check('B1', /récupère les 50,000 DT/.test(t) && /Il reste 250,000 DT/.test(t), 'dialog: 50 jamais reçu · reste 250', t)
    await dlg.getByRole('button', { name: 'Corriger', exact: true }).click()
    const toast = await waitToast(/émise/)
    check('B1', /émise — 50,000 DT payés/.test(toast), 'toast « … 50,000 DT payés »', toast)
    await page.waitForTimeout(1500)
    const notes = notesOf(P2.f1.id)
    const fresh = notes.find((n) => n.id !== before[0].id)
    check('B1', notes.find((n) => n.id === before[0].id)?.status === S.Cancelled, 'old note cancelled')
    check('B1', fresh && fresh.ttc === 300 && fresh.collected === 50, 'new note 300 / 50', JSON.stringify(fresh))
    check('B1', fresh && paymentsOf(fresh.id).some((p) => p.amount === 50 && !p.voided && p.paidOn === '2026-11-02'), 'the 50 is dated the séance day (02/11)', JSON.stringify(fresh && paymentsOf(fresh.id)))
  } },
  { id: 'B2', run: async () => {
    ctxState.b2Before = JSON.stringify({ notes: notesOf(P4.f1.id), fiche: ficheRow(P4.f1.id) })
    await openFiche(P4.id, P4.f1.id)
    await setPaid(0)
    await submit().click()
    const dlg = correctionDialog()
    await dlg.waitFor({ timeout: 10000 })
    await dlg.getByRole('button', { name: 'Annuler', exact: true }).click()
    await dlg.waitFor({ state: 'hidden', timeout: 5000 }).catch(() => {})
    await closeFiche()
    await page.waitForTimeout(800)
    // Compared with the state before the row, so a re-drive after D2 still holds.
    const after = JSON.stringify({ notes: notesOf(P4.f1.id), fiche: ficheRow(P4.f1.id) })
    check('B2', after === ctxState.b2Before, 'nothing written: notes and fiche identical to before', after)
  } },
  { id: 'B3', run: async () => {
    await page.goto(`${WEB}/patients/${P1.id}?addRecord=1&appointmentId=${P1.appt2}`)
    const d = ficheDialog()
    await d.waitFor({ state: 'visible', timeout: 60000 })
    await page.waitForTimeout(2500)
    const t = norm(await d.innerText())
    const btn = norm(await submit().innerText())
    console.log(`     footer « ${btn} »`)
    check('B3', /Inclus dans le traitement/.test(t), 'séance 2 act wears « Inclus dans le traitement »', t.slice(0, 300))
    check('B3', !/Sur la note n°/.test(t), 'séance 2 is not « Sur la note »')
    check('B3', !/— \d/.test(btn), 'footer states no séance price', btn)
    await shot('B3-1440')
    await closeFiche()
  } },
  { id: 'C2', run: async () => {
    await openFiche(P1.id, P1.f1.id)
    const paid = await page.locator('#paid:visible').first().inputValue().catch(() => '(absent)')
    const btn = norm(await submit().innerText())
    check('C2', /Enregistrer la séance — 300,000 DT/.test(btn), 'reopened: plain save at 300 (no « Corriger »)', btn)
    check('C2', /^0([,.]0+)?$/.test(paid.trim()) || paid.trim() === '', 'reopened: Payé 0', paid)
    await submit().click()
    await waitToast(/Fiche de soins mise à jour/)
    await page.waitForTimeout(1500)
    const notes = notesOf(P1.f1.id)
    const live = notes.filter((n) => n.status !== S.Cancelled)
    check('C2', notes.length === 2 && live.length === 1 && live[0].collected === 0 && live[0].ttc === 300, 'still 2 notes, the live one 300 / 0', JSON.stringify(notes))
  } },
  { id: 'C3', run: async () => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.goto(`${WEB}/treatment-plans/${P1.devis.id}`)
    const link = page.locator('a[aria-label^="Corriger la séance 1"]:visible').first()
    await link.waitFor({ timeout: 60000 })
    await link.scrollIntoViewIfNeeded()
    const m = await page.evaluate(() => { const el = document.querySelector('main'); return { sw: el.scrollWidth, cw: el.clientWidth } })
    const b = await link.boundingBox()
    check('C3', m.sw <= m.cw, 'no sideways scroll in <main> at 390', JSON.stringify(m))
    check('C3', !!b && b.x >= 0 && b.x + b.width <= 390, '« Corriger » fully on screen at 390', JSON.stringify(b))
    await shot('C3-390')
    await page.setViewportSize({ width: 1440, height: 900 })
  } },
  { id: 'D1', run: async () => {
    const before = notesOf(P3.f1.id)
    await openFiche(P3.id, P3.f1.id)
    await setPaid(0)
    await submit().click()
    const dlg = correctionDialog()
    await dlg.waitFor({ timeout: 10000 })
    const t = norm(await dlg.innerText())
    check('D1', /sans paiement/.test(t) && /Il reste 90,000 DT/.test(t), 'dialog « sans paiement » · « Il reste 90 »', t)
    await shot('D1-dialog-1440')
    await dlg.getByRole('button', { name: 'Corriger', exact: true }).click()
    const toast = await waitToast(/émise/)
    check('D1', /émise — 0,000 DT payés/.test(toast), 'toast « … 0,000 DT payés »', toast)
    await page.waitForTimeout(1500)
    const notes = notesOf(P3.f1.id)
    const fresh = notes.find((n) => n.id !== before[0].id)
    check('D1', notes.find((n) => n.id === before[0].id)?.status === S.Cancelled, 'old note cancelled')
    check('D1', fresh && fresh.ttc === 90 && fresh.collected === 0 && fresh.status === S.Issued, 'NEW unpaid note 90 / 0', JSON.stringify(notes))
  } },
  { id: 'D2', run: async () => {
    const before = notesOf(P4.f1.id)
    await openFiche(P4.id, P4.f1.id)
    await setPaid(60)
    await submit().click()
    const dlg = correctionDialog()
    await dlg.waitFor({ timeout: 10000 })
    const t = norm(await dlg.innerText())
    check('D2', /passe de 90,000 DT à 60,000 DT/.test(t), 'dialog « passe de 90 à 60 »', t)
    await dlg.getByRole('button', { name: 'Corriger', exact: true }).click()
    const toast = await waitToast(/émise/)
    check('D2', /émise — 60,000 DT payés/.test(toast), 'toast « … 60,000 DT payés »', toast)
    await page.waitForTimeout(1500)
    const fresh = notesOf(P4.f1.id).find((n) => n.id !== before[0].id)
    check('D2', fresh && fresh.ttc === 90 && fresh.collected === 60, 'new note 90 / 60', JSON.stringify(fresh))
    ctxState.p4NewNote = fresh
  } },
  { id: 'D3', run: async () => {
    const d = await openFiche(P5.id, P5.f1.id)
    const btn = norm(await submit().innerText())
    const t = norm(await d.innerText())
    check('D3', /Enregistrer la séance — 300,000 DT/.test(btn), 'attached continuation: plain save at 300', btn)
    check('D3', !/Sur la note n°/.test(t), 'act not shown as « Sur la note »')
    await submit().click()
    await waitToast(/Fiche de soins mise à jour/)
    await page.waitForTimeout(1500)
    const notes = notesOf(P5.f1.id)
    check('D3', notes.length === 1 && notes[0].collected === 100 && notes[0].plan === P5.devis.id && notes[0].status === S.PartiallyPaid, 'note still attached, 300 / 100', JSON.stringify(notes))
  } },
  { id: 'D4', run: async () => {
    await page.goto(`${WEB}/caisse`)
    const triggers = page.locator('main button[aria-label^="Corriger "]:visible')
    await triggers.first().waitFor({ timeout: 60000 })
    const n = Math.min(await triggers.count(), 15)
    let opened = false
    for (let i = 0; i < n && !opened; i++) {
      await triggers.nth(i).click()
      const item = page.getByRole('menuitem', { name: /Corriger le montant/ })
      if (await item.isVisible({ timeout: 1500 }).catch(() => false)) { await item.click(); opened = true }
      else await page.keyboard.press('Escape')
    }
    if (!opened) return skip('D4', 'no payment row offering « Corriger le montant… » on /caisse')
    const dlg = correctionDialog()
    await dlg.waitFor({ timeout: 10000 })
    const t = norm(await dlg.innerText())
    check('D4', /recopiée en brouillon/.test(t), 'dialog speaks of a draft', t)
    check('D4', !/Nouvelle note/.test(t), 'no « Nouvelle note » row', t)
    await shot('D4-1440')
    await dlg.getByRole('button', { name: 'Annuler', exact: true }).click()
  } },
  { id: 'D5', run: async () => {
    const num = ctxState.p4NewNote?.number
    if (!num) return skip('D5', 'D2 produced no new note to look up')
    await page.goto(`${WEB}/factures`)
    const search = page.locator('main input[type="search"]:visible, main input[placeholder*="echerch"]:visible').first()
    await search.waitFor({ timeout: 60000 })
    await search.fill(num)
    const trigger = page.locator(`button[aria-label^="Actions pour"]:visible`).filter({ hasNot: page.locator('xx') })
    await page.waitForTimeout(2500)
    const row = page.locator(`button[aria-label^="Actions pour"][aria-label*="${num}"]:visible`).first()
    const target = (await row.count()) ? row : trigger.first()
    await target.click()
    const item = page.getByRole('menuitem', { name: /Corriger cette note/ })
    if (!(await item.isVisible({ timeout: 3000 }).catch(() => false))) return skip('D5', '« Corriger cette note » not offered on that row')
    await item.click()
    await page.waitForURL(/\/patients\//, { timeout: 30000 })
    await ficheDialog().waitFor({ state: 'visible', timeout: 60000 })
    check('D5', page.url().includes(P4.id) || (await ficheDialog().innerText()).includes('Soin de carie'), '/factures « Corriger cette note » opens P4\'s fiche')
    await shot('D5-1440')
    await closeFiche()
  } },
]

console.log(`QA walk — ${new Date().toISOString()} — web ${WEB}, db ${DB}`)
try {
  await signIn()
  ok('login', 'signed in on the isolated stack')
} catch (e) {
  bad('login', 'sign-in failed', e.message)
}
// ONLY=B2 re-drives one row after a PROBE fix (never after a source fix — that is a full re-run).
const only = process.env.ONLY ? new Set(process.env.ONLY.split(',')) : null
for (const s of SCENARIOS.filter((x) => !only || only.has(x.id))) {
  try { await s.run() } catch (e) { bad(s.id, 'threw', norm(e.message).slice(0, 300)); await shot(`${s.id}-threw`).catch(() => {}) }
}
await browser.close()

console.log('\n── console errors (first 10) ──')
consoleErrors.slice(0, 10).forEach((e) => console.log('  ' + e))
console.log('\n── summary ──')
const real = findings.filter((f) => !f.skipped)
if (findings.length === 0) console.log('ALL CLEAR')
else findings.forEach((f) => console.log(f.skipped ? `  ⏭ ${f.id}: ${f.why}` : `  ❌ ${f.id}: ${f.m} ${f.d ?? ''}`))
console.log(`\n${real.length} failed · ${findings.length - real.length} not exercised`)
