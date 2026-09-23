// Eye pass: the surfaces wave 1 changed, at 320 / 390 / 820 / 1180 / 1440 and 1536×730. Screenshots only —
// opens dialogs, presses « Retour », saves nothing. Usage: node eyepass.mjs <scratchpad>
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'
import { execSync } from 'node:child_process'
import { dirname, join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { createRequire } from 'node:module'

const here = dirname(fileURLToPath(import.meta.url))
const pad = process.argv[2]
const { chromium } = createRequire(join(pad, 'package.json'))('playwright-core')
const { totp } = await import(pathToFileURL(join(pad, 'totp.mjs')).href)
const f = JSON.parse(readFileSync(join(here, 'fixtures.json'), 'utf8'))
const out = join(here, 'shots', 'eye'); mkdirSync(out, { recursive: true })
const WEB = 'http://localhost:3000', API = 'http://localhost:5000/api'
const SECRET = '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'
const sql = (q) => execSync(`docker exec clinic-postgres psql -U clinic_user -d clinic_management -At -c "${q.replace(/"/g, '\\"')}"`, { encoding: 'utf8' }).trim()

// ── fresh fixtures for the two confirmation dialogs (never confirmed) ─────────────────────────────────────
const waitWindow = () => new Promise((r) => setTimeout(r, (30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000))
const login = await (await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp(SECRET) }) })).json()
const token = login.value.accessToken
const call = async (m, p, b) => { const r = await fetch(`${API}${p}`, { method: m, headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` }, body: b === undefined ? undefined : JSON.stringify(b) }); const t = await r.text(); if (!r.ok) throw new Error(`${m} ${p} ${r.status} ${t}`); const j = t ? JSON.parse(t) : null; return j?.value ?? j }
const when = (d) => { const x = new Date(); x.setDate(x.getDate() + d); x.setHours(15, 0, 0, 0); return x.toISOString() }
const canal = '719d83b6-9169-49ec-887d-f9e8b8a938f3', couronne = 'a976d389-7593-4ba9-b87a-6b8d3bf97572'
const stopPlan = await call('POST', '/treatment-plans', { patientId: f.patient3, title: 'QA eye stop', items: [{ designationFr: 'Traitement de canal', plannedCost: 150, procedureTypeId: canal, toothNumbers: [26] }], installments: [] })
await call('POST', '/appointments', { patientId: f.patient3, appointmentDateTime: when(14), durationMinutes: 30, procedures: [{ procedureTypeId: canal, treatmentPlanItemId: stopPlan.items[0].id }], treatmentPlanId: stopPlan.id, allowOutsideWorkingHours: true, allowOverlap: true })
const draft = await call('POST', '/treatment-plans/start', { patientId: f.patient4, procedureTypeId: couronne, agreedTotal: null, toothNumbers: [] })
await call('POST', '/appointments', { patientId: f.patient4, appointmentDateTime: when(15), durationMinutes: 30, procedures: [{ procedureTypeId: couronne, treatmentPlanItemId: draft.items[0].id, treatmentPlanItemStepId: draft.items[0].steps[0].id }], treatmentPlanId: draft.id, allowOutsideWorkingHours: true, allowOverlap: true })
const r4Record = sql(`select "Id" from "DentalRecords" where "AppointmentId"='${f.R4.visit}' limit 1`)
writeFileSync(join(here, 'eye-fixtures.json'), JSON.stringify({ stopPlan: stopPlan.id, draft: draft.id, r4Record }, null, 2))

// ── browser ──────────────────────────────────────────────────────────────────────────────────────────────
const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
await ctx.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const page = await ctx.newPage()
await page.goto(`${WEB}/appointments`); await page.waitForURL(/\/login/)
await page.fill('input[type=email]', 'salma.benyoussef@cabinet-ibnkhaldoun.tn')
await page.fill('input[type=password]', 'QaAudit2026!y')
await page.click("button:has-text('Se connecter')")
await page.waitForSelector('input[inputmode=numeric]'); await waitWindow()
await page.fill('input[inputmode=numeric]', totp(SECRET))
await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30000 })

const report = []
const measure = async (label) => page.evaluate((label) => {
  const d = [...document.querySelectorAll('[role=dialog],[role=alertdialog]')].pop()
  const r = d?.getBoundingClientRect()
  return { label, docOverflow: document.documentElement.scrollWidth - window.innerWidth, dialogRight: r ? Math.round(r.right - window.innerWidth) : null }
}, label)
const widths = [[320, 780], [390, 844], [820, 1024], [1180, 820], [1440, 900], [1536, 730]]
const dialog = () => page.locator('[role=dialog]:visible').last()

for (const [w, h] of widths) {
  await page.setViewportSize({ width: w, height: h })
  const tag = `${w}x${h}`

  // 1. New fiche, stepped devis act linked, « Séance » picker shown.
  try {
    await page.goto(`${WEB}/patients/${f.patient2}`)
    await page.getByRole('button', { name: /Ajouter un acte dentaire/ }).filter({ visible: true }).first().click()
    const d = dialog(); await d.waitFor({ timeout: 20000 }); await page.waitForTimeout(2500)
    const number = sql(`select "Number" from "TreatmentPlans" where "Id"='${f.F7.plan}'`)
    await d.locator('#plan-item').click()
    await page.locator('[role=listbox] [role=option]').filter({ hasText: number }).first().click()
    await page.waitForTimeout(1000)
    await d.locator('#plan-item-step').scrollIntoViewIfNeeded()
    await page.screenshot({ path: join(out, `fiche-picker-${tag}.png`) })
    report.push(await measure(`fiche-picker ${tag}`))
    await page.keyboard.press('Escape'); await page.waitForTimeout(500)
    await page.locator('[role=alertdialog]:visible').getByRole('button', { name: /Quitter|Abandonner|Oui/ }).first().click({ timeout: 2000 }).catch(() => {})
  } catch (e) { report.push({ label: `fiche-picker ${tag}`, error: e.message.split('\n')[0] }) }

  // 2. Reopened devis-carried fiche → « Aucun » → the card gets its price back.
  try {
    await page.goto(`${WEB}/patients/${f.patient2}?editRecord=${r4Record}`)
    const d = dialog(); await d.waitFor({ timeout: 20000 }); await page.waitForTimeout(2500)
    await d.locator('#plan-item').click()
    await page.locator('[role=listbox] [role=option]').filter({ hasText: /^Aucun$/ }).first().click()
    await page.waitForTimeout(1000)
    await d.getByText(/Actes de la séance/).scrollIntoViewIfNeeded()
    await page.screenshot({ path: join(out, `fiche-aucun-${tag}.png`) })
    report.push(await measure(`fiche-aucun ${tag}`))
    await page.keyboard.press('Escape'); await page.waitForTimeout(500)
    await page.locator('[role=alertdialog]:visible').getByRole('button', { name: /Quitter|Abandonner|Oui/ }).first().click({ timeout: 2000 }).catch(() => {})
  } catch (e) { report.push({ label: `fiche-aucun ${tag}`, error: e.message.split('\n')[0] }) }

  // 3. « Arrêter le traitement » on a devis with nothing delivered and a booked visit (cancel branch).
  try {
    await page.goto(`${WEB}/treatment-plans/${stopPlan.id}`)
    await page.waitForSelector('text=/Traitement de canal/', { timeout: 20000 }); await page.waitForTimeout(1200)
    await page.getByRole('button', { name: 'Autres actions sur ce devis' }).filter({ visible: true }).first().click()
    await page.getByRole('menuitem', { name: /Arrêter le traitement/ }).click()
    await page.locator('[role=alertdialog]:visible').waitFor({ timeout: 10000 }); await page.waitForTimeout(500)
    await page.screenshot({ path: join(out, `stop-dialog-${tag}.png`) })
    report.push(await measure(`stop-dialog ${tag}`))
    await page.locator('[role=alertdialog]:visible').getByRole('button', { name: 'Retour' }).click()
  } catch (e) { report.push({ label: `stop-dialog ${tag}`, error: e.message.split('\n')[0] }) }

  // 4. « Supprimer le traitement » on a followed treatment with a booked visit.
  try {
    await page.goto(`${WEB}/treatment-plans/${draft.id}`)
    await page.waitForSelector('text=/Couronne/', { timeout: 20000 }); await page.waitForTimeout(1200)
    const direct = page.getByRole('button', { name: /Supprimer le traitement/ }).filter({ visible: true })
    if (await direct.count()) await direct.first().click()
    else {
      await page.getByRole('button', { name: 'Autres actions sur ce devis' }).filter({ visible: true }).first().click()
      await page.getByRole('menuitem', { name: /Supprimer le traitement/ }).click()
    }
    await page.locator('[role=alertdialog]:visible').waitFor({ timeout: 10000 }); await page.waitForTimeout(500)
    await page.screenshot({ path: join(out, `delete-dialog-${tag}.png`) })
    report.push(await measure(`delete-dialog ${tag}`))
    await page.locator('[role=alertdialog]:visible').getByRole('button', { name: 'Retour' }).click()
  } catch (e) { report.push({ label: `delete-dialog ${tag}`, error: e.message.split('\n')[0] }) }
}

await browser.close()
for (const r of report) console.log(JSON.stringify(r))
