// Wave-4 browser walk (H · I · J · C4b). One launch, every row, fixes nothing.
// Usage: node walk-4.mjs <scratchpad-dir-with-node_modules-and-totp.mjs>
import { readFileSync, mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { createRequire } from 'node:module'

const here = dirname(fileURLToPath(import.meta.url))
const pad = process.argv[2]
const { chromium } = createRequire(join(pad, 'package.json'))('playwright-core')
const { totp } = await import(pathToFileURL(join(pad, 'totp.mjs')).href)
const f = JSON.parse(readFileSync(join(here, 'fixtures-4.json'), 'utf8'))
const shots = join(here, 'shots'); mkdirSync(shots, { recursive: true })
const WEB = 'http://localhost:3000'
const API = 'http://localhost:5000/api'
const SECRET = '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, skipped: why }) }
const norm = (s) => (s ?? '').replace(/[  ]/g, ' ')
const pad2 = (n) => String(n).padStart(2, '0')
const localDate = (offset) => { const d = new Date(); d.setDate(d.getDate() + offset); return d }
const frDate = (d) => `${pad2(d.getDate())}/${pad2(d.getMonth() + 1)}/${d.getFullYear()}`
const isoLocal = (d) => `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`

let token = null
const api = async (method, path, body) => {
  const r = await fetch(`${API}${path}`, { method, headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` }, body: body === undefined ? undefined : JSON.stringify(body) })
  const t = await r.text(); let j; try { j = t ? JSON.parse(t) : null } catch { j = t }
  return { status: r.status, body: j?.value ?? j, raw: j }
}
const appt = async (id) => (await api('GET', `/appointments/${id}`)).body

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
await ctx.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const page = await ctx.newPage()

await page.goto(`${WEB}/appointments`)
await page.waitForURL(/\/login/)
await page.fill('input[type=email]', 'salma.benyoussef@cabinet-ibnkhaldoun.tn')
await page.fill('input[type=password]', 'QaAudit2026!y')
await page.click("button:has-text('Se connecter')")
await page.waitForSelector('input[inputmode=numeric]')
await page.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
await page.fill('input[inputmode=numeric]', totp(SECRET))
await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30000 })
console.log('signed in')
await page.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
{
  const login = await (await fetch(`${API}/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp(SECRET) }),
  })).json()
  token = login.value?.accessToken ?? login.accessToken
  if (!token) throw new Error('api login failed ' + JSON.stringify(login).slice(0, 200))
}

const dialog = () => page.locator('[role=dialog]:visible').last()
const alert = () => page.locator('[role=alertdialog]:visible').last()
const toastText = async (re, ms = 8000) => {
  const end = Date.now() + ms
  while (Date.now() < end) {
    const t = norm(await page.locator('[data-sonner-toast]').allInnerTexts().then((a) => a.join(' | ')).catch(() => ''))
    if (re.test(t)) return t
    await page.waitForTimeout(250)
  }
  return null
}
async function openEditDialog(visitId) {
  await page.goto(`${WEB}/appointments?appointmentId=${visitId}`)
  const d = dialog(); await d.waitFor({ timeout: 25000 })
  await page.waitForTimeout(1500)
  return d
}
async function openFiche(recordId) {
  await page.goto(`${WEB}/patients/${f.patient}?editRecord=${recordId}`)
  const d = dialog(); await d.waitFor({ timeout: 25000 })
  await page.waitForTimeout(2500)
  return d
}
const overflowX = () => page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)

const scenarios = [
  // ── H: status and date edits ────────────────────────────────────────────────────────────────────
  ['H1', async () => {
    const d = await openEditDialog(f.H1.visit)
    const time = d.locator('#edit-appt-start-time')
    await time.click(); await time.fill('11:00'); await time.press('Tab')
    await d.getByRole('button', { name: /^Enregistrer/ }).last().click()
    // A past time asks for confirmation — confirming it is the ordinary path.
    try { const a = alert(); await a.waitFor({ timeout: 3000 }); await a.getByRole('button').last().click() } catch {}
    await page.waitForTimeout(2500)
    const v = await appt(f.H1.visit)
    const hour = new Date(v.appointmentDateTime).getHours()
    hour === 11 && v.status === 'Completed'
      ? ok('H1', `« Terminé » visit moved to 11:00 and stays « Terminé »`)
      : bad('H1', 'finished visit not moved', `hour=${hour} status=${v.status}`)
  }],
  ['H1b', async () => {
    const d = await openEditDialog(f.H1b.visit)
    const disabled = await d.locator('#edit-appt-start-time').isDisabled()
    const text = norm(await d.innerText())
    disabled && /Rendez-vous annulé : repassez-le en « Planifié »/.test(text)
      ? ok('H1b', 'cancelled visit: time locked, reason stated')
      : bad('H1b', 'cancelled visit date not locked with a reason', `disabled=${disabled}`)
    await page.keyboard.press('Escape')
  }],
  ['J4', async () => {
    const moved = new Date(f.H1b.at); moved.setDate(moved.getDate() + 1)
    const r = await api('PUT', `/appointments/${f.H1b.visit}`, { appointmentDateTime: moved.toISOString() })
    const err = JSON.stringify(r.raw)
    r.status >= 400 && /annulé ne peut pas être déplacé/.test(err)
      ? ok('J4', `move of a cancelled visit refused by name (${r.status})`)
      : bad('J4', 'cancelled move not refused by name', `${r.status} ${err.slice(0, 160)}`)
  }],
  ['H10', async () => {
    const v0 = await appt(f.H10.visit)
    const tomorrow = new Date(v0.appointmentDateTime); tomorrow.setDate(tomorrow.getDate() + 1)
    const r = await api('PUT', `/appointments/${f.H10.visit}`, { appointmentDateTime: tomorrow.toISOString(), status: v0.status, allowOverlap: true, allowOutsideWorkingHours: true })
    const v = await appt(f.H10.visit)
    r.status < 300 && v.status === 'Scheduled'
      ? ok('H10', '« En cours » moved to tomorrow is « Planifié »')
      : bad('H10', 'status after move to another day', `${r.status} ${v?.status} ${JSON.stringify(r.raw).slice(0, 120)}`)
  }],
  ['H6', async () => {
    await page.goto(`${WEB}/appointments?date=${f.H6.day}&view=day`)
    await page.waitForTimeout(4000)
    const trigger = page.getByRole('button', { name: `Actions du rendez-vous de ${f.patientName}` }).first()
    if (await trigger.count() === 0) return skip('H6', 'no « ⋯ » found on the agenda day view (block too small or selector)')
    await trigger.click()
    const cancel = page.getByRole('button', { name: /^Annulé$/ }).first()
    if (await cancel.count() === 0) return bad('H6', '« Annulé » not offered on the finished visit')
    await cancel.click()
    const a = alert(); await a.waitFor({ timeout: 5000 }).catch(() => {})
    const text = norm(await a.innerText().catch(() => ''))
    await page.screenshot({ path: join(shots, 'w4-H6-confirm.png') })
    const named = /Annuler ce rendez-vous/.test(text) && (!f.H6.invoiceNumber || text.includes(f.H6.invoiceNumber))
    if (!named) return bad('H6', 'no confirmation naming the note', text.slice(0, 160))
    await a.getByRole('button', { name: /Non, conserver/ }).click()
    await page.waitForTimeout(1500)
    const v = await appt(f.H6.visit)
    v.status === 'Completed'
      ? ok('H6', `confirmation names note ${f.H6.invoiceNumber}; « Non, conserver » changes nothing`)
      : bad('H6', 'status moved after « Non, conserver »', v.status)
  }],
  ['H7', async () => {
    await page.goto(`${WEB}/patients/${f.patient}?tab=appointments`)
    await page.waitForTimeout(4000)
    const when = `${frDate(localDate(-1))}`
    const row = page.locator('tr', { hasText: when }).filter({ hasText: '09:00' }).first()
    if (await row.count() === 0) return skip('H7', `history row for ${when} 09:00 not found`)
    const btn = row.getByRole('button', { name: /Enregistrer la fiche/ })
    ;(await btn.count()) > 0
      ? ok('H7', '« Terminé » visit whose fiche was deleted offers « Enregistrer la fiche »')
      : bad('H7', 'no « Enregistrer la fiche » on a finished visit with no fiche', norm(await row.innerText()).slice(0, 160))
  }],
  ['H8', async () => {
    const d = await openFiche(f.H8.record)
    const lab = d.locator('label:has-text("Encaissé sur le traitement")').first()
    const id = await lab.getAttribute('for').catch(() => null)
    if (!id) return skip('H8', '« Encaissé sur le traitement » field not found')
    await d.locator(`#${id}`).fill('50')
    await page.waitForTimeout(800)
    const link = d.locator(`a[href="/treatment-plans/${f.H8.plan}"]`)
    const text = norm(await d.innerText())
    ;(await link.count()) > 0 && /un encaissement ne se diminue pas ici/.test(text)
      ? ok('H8', 'lowering a chairside collection names the way out with a link to the devis')
      : bad('H8', 'no link to the devis on the lowered collection', text.match(/déjà encaissés[^·]*·[^\n]*/)?.[0] ?? '')
    await page.keyboard.press('Escape')
  }],
  ['H9', async () => {
    const r = await api('GET', `/treatment-plans/treatments-in-progress?search=${encodeURIComponent(f.patientName)}`)
    const items = r.body?.items ?? r.body ?? []
    const row = items.find((i) => i.planId === f.H9.plan)
    if (!row) return bad('H9', 'H9 treatment missing from « Traitements en cours »', `status ${r.status}`)
    const wire = JSON.stringify(row.doneStepNumbers) === '[2]' && row.nextStepDueFrom == null && row.nextStepNumber === 1
    await page.goto(`${WEB}/treatment-plans`)
    await page.fill('#treatments-search', f.patientName)
    await page.waitForTimeout(3500)
    const dots = await page.evaluate((label) => {
      const rows = [...document.querySelectorAll('tr, li, [data-slot=card]')].filter((el) => el.innerText.includes(label))
      for (const el of rows) {
        const pips = [...el.querySelectorAll('span.rounded-full.size-2\\.5')]
        if (pips.length === 3) return pips.map((p) => p.className.includes('bg-success'))
      }
      return null
    }, f.names.couronne)
    wire && dots && dots.join() === 'false,true,false'
      ? ok('H9', 'séance 2 done first: only the 2nd dot is green; no due date invented from the latest séance')
      : (await page.screenshot({ path: join(shots, 'w4-H9-list.png') }), bad('H9', 'dots or due date', `wire=${wire} dots=${JSON.stringify(dots)} row=${JSON.stringify({ d: row.doneStepNumbers, due: row.nextStepDueFrom, n: row.nextStepNumber })} pips=${await page.locator('span.rounded-full.size-2\.5').count()}`))
  }],
  ['H11', async () => {
    const d = await openEditDialog(f.H11.visit)
    const link = d.getByRole('button', { name: /C'est la suite d'une séance précédente/ })
    if (await link.count() === 0) return bad('H11', 'edit dialog has no « C\'est la suite… » door')
    await link.click()
    const title = page.getByRole('heading', { name: /Suite d'une séance précédente/ })
    await title.waitFor({ timeout: 6000 }).catch(() => {})
    ;(await title.count()) > 0 ? ok('H11', 'existing visit can open « Suite d\'une séance précédente »') : bad('H11', 'continuation dialog did not open')
    await page.keyboard.press('Escape'); await page.waitForTimeout(400); await page.keyboard.press('Escape')
  }],
  ['H2', async () => {
    const r = await api('DELETE', `/patients/${f.patient}/dental-records/${f.H2.record}`)
    r.status < 300 ? ok('H2', `fiche on a cancelled devis deleted (${r.status})`) : bad('H2', 'delete refused', `${r.status} ${JSON.stringify(r.raw).slice(0, 160)}`)
  }],
  ['H3', async () => {
    // A 1-act devis finished by its fiche, then an act added.
    const plan = (await api('POST', '/treatment-plans', { patientId: f.patient, title: `QA H3 ${f.ts}`, items: [{ designationFr: 'Détartrage', plannedCost: 60, toothNumbers: [] }], installments: [] })).body
    await api('POST', `/patients/${f.patient}/dental-records`, { interventionDate: isoLocal(localDate(-12)), amountPaid: 0, isAdultTeeth: true, notes: [], importantNotes: [], treatmentPlanId: plan.id, treatmentPlanItemId: plan.items[0].id, acts: [{ procedureName: 'Détartrage', cost: 0, unitCost: 0, isPerTooth: false, toothNumbers: [] }] })
    const done = (await api('GET', `/treatment-plans/${plan.id}`)).body
    const r = await api('POST', `/treatment-plans/${plan.id}/amend`, { version: done.version, addItems: [{ designationFr: 'Obturation', plannedCost: 80, toothNumbers: [] }] })
    const after = (await api('GET', `/treatment-plans/${plan.id}`)).body
    done.status === 'Completed' && r.status < 300 && after.status === 'InProgress'
      ? ok('H3', 'an act added to a « Terminé » devis reopens it (« En cours »)')
      : bad('H3', 'status after adding an act', `${done.status} → ${after?.status} (${r.status} ${JSON.stringify(r.raw).slice(0, 120)})`)
  }],
  ['H4', async () => {
    const plan = (await api('POST', '/treatment-plans', { patientId: f.patient, title: `QA H4 ${f.ts}`, items: [{ designationFr: 'Couronne', plannedCost: 300, toothNumbers: [] }], installments: [] })).body
    const rec = (await api('POST', `/patients/${f.patient}/dental-records`, { interventionDate: isoLocal(localDate(-12)), amountPaid: 0, isAdultTeeth: true, notes: [], importantNotes: [], treatmentPlanId: plan.id, treatmentPlanItemId: plan.items[0].id, acts: [{ procedureName: 'Couronne', cost: 0, unitCost: 0, isPerTooth: false, toothNumbers: [] }] })).body
    const fresh = (await api('GET', `/treatment-plans/${plan.id}`)).body
    const r = await api('PUT', `/treatment-plans/${plan.id}/items/${plan.items[0].id}/steps`, { version: fresh.version, steps: ['Préparation', 'Empreinte', 'Pose'].map((label) => ({ id: null, label, estimatedDurationMinutes: 30, minDaysAfterPrevious: null })) })
    const item = (await api('GET', `/treatment-plans/${plan.id}`)).body.items[0]
    const s1 = item.steps?.find((s) => s.sequenceNumber === 0)
    r.status < 300 && s1?.linkedDentalRecordId === rec.id && item.status === 'InProgress'
      ? ok('H4', 'done act cut into 3 séances: séance 1 keeps the fiche, act « en cours »')
      : bad('H4', 'split of a done act', `${r.status} ${JSON.stringify(r.raw).slice(0, 140)} step1=${s1?.linkedDentalRecordId} item=${item.status}`)
  }],
  ['H5', async () => {
    const all = (await api('GET', `/patients/${f.patient}/dental-records`)).body
    const rec = (all?.items ?? all ?? []).find((r) => r.id === f.H6.record)
    if (!rec?.acts) return skip('H5', 'billed fiche not found in the patient fiches')
    const body = {
      interventionDate: rec.interventionDate?.slice(0, 10), amountPaid: rec.amountPaid, isAdultTeeth: rec.isAdultTeeth,
      notes: rec.notes ?? [], importantNotes: rec.importantNotes ?? [], appointmentId: rec.appointmentId, version: rec.version,
      acts: rec.acts.map((a) => ({ procedureTypeId: a.procedureTypeId, procedureName: a.procedureName, cost: a.cost, unitCost: a.unitCost, isPerTooth: a.isPerTooth, toothNumbers: [26], surfaces: a.surfaces, resultingCondition: a.resultingCondition })),
    }
    const r = await api('PUT', `/patients/${f.patient}/dental-records/${f.H6.record}`, body)
    const all2 = (await api('GET', `/patients/${f.patient}/dental-records`)).body
    const after = (all2?.items ?? all2 ?? []).find((r) => r.id === f.H6.record)
    r.status < 300 && after?.acts?.[0]?.toothNumbers?.[0] === 26
      ? ok('H5', 'billed fiche: tooth 16 → 26 on a flat act saves')
      : bad('H5', 'tooth fix on a billed fiche', `${r.status} ${JSON.stringify(r.raw).slice(0, 200)}`)
  }],

  // ── C4b ─────────────────────────────────────────────────────────────────────────────────────────
  ['C4b', async () => {
    const list = (await api('GET', `/patients/${f.patient}/dental-records`)).body
    const rec = (list?.items ?? list ?? []).find((r) => r.id === f.C4.record)
    const d = await openFiche(f.C4.record)
    const text = norm(await d.innerText())
    const acts = text.slice(text.indexOf('Actes de la séance'), text.indexOf('Ajouter un autre acte'))
    const paid = await d.locator('label:has-text("Payé")').count()
    await page.screenshot({ path: join(shots, 'w4-C4b-reopened.png') })
    rec?.treatmentPlanItemIds?.length === 2 && !/DT/.test(acts) && paid === 0
      ? ok('C4b', 'reopened fiche with two devis acts: both cards carried (no price), no « Payé »')
      : bad('C4b', 'reopened two-act séance', `ids=${JSON.stringify(rec?.treatmentPlanItemIds)} acts=${JSON.stringify(acts.slice(0, 200))} payé=${paid}`)
    await page.keyboard.press('Escape')
  }],

  // ── I: « Types de procédures » ─────────────────────────────────────────────────────────────────
  ['I1b', async () => {
    const r = await api('POST', '/procedure-types', { name: f.I1.name, defaultDurationMinutes: 30, defaultCost: 40, colorHex: '#4F83CC' })
    const s = JSON.stringify(r.raw)
    f.I1.deletion?.archived && r.status >= 400 && /procedure-type-archived-name/.test(s) && /archivé/.test(s)
      ? ok('I1b', 'creating the name of an archived act is refused by name, with its way back')
      : bad('I1b', 'archived-name refusal', `archived=${f.I1.deletion?.archived} ${r.status} ${s.slice(0, 160)}`)
  }],
  ['I1', async () => {
    await page.goto(`${WEB}/procedure-types`)
    await page.fill('#procedure-types-search', f.I1.name)
    await page.click('#procedure-types-archived')
    await page.getByRole('option', { name: 'Afficher les archivés' }).click()
    await page.waitForTimeout(2500)
    const row = page.locator('tr', { hasText: f.I1.name }).first()
    if (await row.count() === 0) return bad('I1', 'archived act not listed with « Afficher les archivés »')
    const archivedBadge = /Archivé/.test(norm(await row.innerText()))
    await row.getByRole('button', { name: /Réactiver/ }).click()
    const t = await toastText(/Acte réactivé/)
    const pt = (await api('GET', `/procedure-types/${f.I1.id}`)).body
    archivedBadge && t && pt?.isActive
      ? ok('I1', 'archived act listed « Archivé », « Réactiver » brings it back')
      : bad('I1', 'reactivation', `badge=${archivedBadge} toast=${!!t} active=${pt?.isActive}`)
  }],
  ['I4', async () => {
    await page.goto(`${WEB}/procedure-types`)
    await page.fill('#procedure-types-search', f.I4.name)
    await page.waitForTimeout(2500)
    const row = page.locator('tr', { hasText: f.I4.name }).first()
    if (await row.count() === 0) return skip('I4', 'act row not found')
    await row.getByRole('button', { name: /Supprimer/ }).click()
    const a = alert(); await a.waitFor({ timeout: 5000 })
    const text = norm(await a.innerText())
    await a.getByRole('button', { name: /^Supprimer$/ }).click()
    const t = await toastText(/Acte archivé/)
    const pt = (await api('GET', `/procedure-types/${f.I4.id}`)).body
    ;/ni aucun devis/.test(text) && t && /1 ligne de devis/.test(t) && pt?.isActive === false
      ? ok('I4', 'dialog names devis; an act a devis line names is archived and the toast says so')
      : bad('I4', 'delete of a devis-used act', `dialog=${/ni aucun devis/.test(text)} toast=${t} active=${pt?.isActive}`)
  }],
  ['I3', async () => {
    await page.goto(`${WEB}/settings`)
    await page.waitForTimeout(4000)
    const section = page.locator('div', { hasText: /^Praticiens retirés/ }).last()
    const row = page.locator('div.flex', { hasText: f.I3.name }).filter({ has: page.getByRole('button', { name: 'Réactiver' }) }).last()
    if (await row.count() === 0) return bad('I3', 'retired practitioner not listed with « Réactiver »', `section=${await section.count()}`)
    await row.getByRole('button', { name: 'Réactiver' }).click()
    const t = await toastText(/de nouveau dans l'équipe/)
    const doctors = (await api('GET', '/clinics/user-status')).body?.doctors ?? []
    const me = doctors.find((d) => d.id === f.I3.id)
    t && me?.isActive === true
      ? ok('I3', 'retired practitioner kept, listed and reinstated with the same record')
      : bad('I3', 'reinstatement', `toast=${!!t} isActive=${me?.isActive}`)
    // Clean-up: retire the throwaway practitioner again.
    const roster = doctors.filter((d) => d.isActive !== false && d.id !== f.I3.id)
      .map((d) => Object.fromEntries(Object.entries({ id: d.id, name: d.name, specialty: d.specialty, phone: d.phone, email: d.email, codeProfessionnelSante: d.codeProfessionnelSante }).filter(([, v]) => v != null && v !== '')))
    await api('PUT', '/clinics/doctors', { doctors: roster })
  }],

  // ── J: sentences ────────────────────────────────────────────────────────────────────────────────
  ['J3a', async () => {
    const r = await api('GET', `/appointments/to-close?patientId=${f.patient}`)
    const visits = r.body?.visits?.items ?? r.body?.visits ?? []
    const v = visits.find((x) => x.appointmentId === f.J3.visit)
    await page.goto(`${WEB}/patients/${f.patient}?tab=medical-records`)
    await page.waitForTimeout(4500)
    const text = norm(await page.locator('main').innerText())
    const strip = text.slice(text.indexOf('Travail non facturé'), text.indexOf('Travail non facturé') + 400)
    v && JSON.stringify(v.recordedProcedures) === JSON.stringify([f.names.detartrage]) && strip.includes(`Réalisé : ${f.names.detartrage}`)
      ? ok('J3a', '« Travail non facturé » / « À clôturer » name the act the fiche recorded (« Réalisé »)')
      : (await page.screenshot({ path: join(shots, 'w4-J3a.png'), fullPage: true }), bad('J3a', 'act line', `wire=${JSON.stringify(v?.recordedProcedures)} strip=${JSON.stringify(strip.slice(0, 200))}`))
  }],
  ['J3b', async () => {
    await page.goto(`${WEB}/patients/${f.patient}?tab=appointments`)
    await page.waitForTimeout(4000)
    const row = page.locator('tr', { hasText: frDate(localDate(-1)) }).filter({ hasText: '15:00' }).first()
    const text = norm(await row.innerText().catch(() => ''))
    text.includes(`Réalisé : ${f.names.detartrage}`)
      ? ok('J3b', 'patient history: the fiche\'s act « Réalisé », not the booked couronne')
      : bad('J3b', 'history act line', text.slice(0, 160))
  }],
  ['J3c', async () => {
    await page.goto(`${WEB}/appointments?date=${isoLocal(localDate(-1))}&view=day`)
    await page.waitForTimeout(4000)
    const titled = await page.locator(`[title*="prévu : ${f.names.couronne}"], [aria-label*="prévu : ${f.names.couronne}"]`).count()
    titled > 0 ? ok('J3c', 'agenda: a finished visit\'s act reads « prévu »') : bad('J3c', 'no « prévu » on the finished agenda block')
  }],
  ['J5a', async () => {
    await page.goto(`${WEB}/treatment-plans`)
    await page.fill('#treatments-search', f.patientName)
    await page.waitForTimeout(3500)
    const row = page.locator('tr', { hasText: f.J5.number }).first()
    if (await row.count() === 0) return skip('J5a', `devis ${f.J5.number} row not found`)
    await row.getByRole('button', { name: 'Actions du plan' }).click()
    const menu = page.locator('[role=menu]:visible').last(); await menu.waitFor({ timeout: 5000 })
    const text = norm(await menu.innerText())
    ;/Modifier les actes et les prix/.test(text) && /Devis annulé : rétablissez-le pour le modifier/.test(text)
      ? ok('J5a', 'cancelled devis menu keeps « Modifier » and says why it is off')
      : bad('J5a', 'list menu refusals', text.slice(0, 200))
    await page.keyboard.press('Escape')
  }],
  ['J5b', async () => {
    await page.goto(`${WEB}/treatment-plans/${f.H8.plan}`)
    await page.waitForSelector(`text=${f.H8.title}`, { timeout: 25000 }).catch(() => {})
    await page.waitForTimeout(1500)
    await page.getByRole('button', { name: 'Autres actions sur ce devis' }).click()
    const menu = page.locator('[role=menu]:visible').last(); await menu.waitFor({ timeout: 5000 })
    const text = norm(await menu.innerText())
    ;/Facturer le devis/.test(text) ? ok('J5b', 'devis « ⋯ » offers « Facturer le devis » while séances remain') : bad('J5b', '« Facturer » missing from the devis menu', text.slice(0, 200))
    await page.keyboard.press('Escape')
  }],

  // ── D: regression ───────────────────────────────────────────────────────────────────────────────
  ['R1', async () => {
    const d = await openEditDialog(f.H11.visit)
    await d.getByRole('button', { name: /Notes et options/ }).click()
    const notes = d.locator('#edit-appt-notes textarea').first()
    await notes.fill(`QA notes ${f.ts}`)
    await d.getByRole('button', { name: /^Enregistrer/ }).last().click()
    await page.waitForTimeout(2500)
    const v = await appt(f.H11.visit)
    v.notes === `QA notes ${f.ts}` ? ok('R1', 'ordinary visit edit still saves') : bad('R1', 'notes not saved', v.notes)
  }],

  // ── C: widths ───────────────────────────────────────────────────────────────────────────────────
  ['C320', async () => {
    await page.setViewportSize({ width: 320, height: 720 })
    const out = []
    await page.goto(`${WEB}/patients/${f.patient}?tab=appointments`); await page.waitForTimeout(3500)
    out.push(['patient history', await overflowX()]); await page.screenshot({ path: join(shots, 'w4-C320-history.png') })
    await page.goto(`${WEB}/procedure-types`); await page.waitForTimeout(3000)
    out.push(['procedure types', await overflowX()]); await page.screenshot({ path: join(shots, 'w4-C320-procedure-types.png') })
    await page.goto(`${WEB}/a-cloturer`); await page.waitForTimeout(3000)
    out.push(['à clôturer', await overflowX()])
    const d = await openEditDialog(f.H1b.visit)
    const box = await d.boundingBox()
    out.push(['edit dialog', box && box.x >= 0 && box.x + box.width <= 321 ? 0 : 99])
    await page.screenshot({ path: join(shots, 'w4-C320-edit-locked.png') })
    const over = out.filter(([, v]) => v > 1)
    over.length === 0 ? ok('C320', `no horizontal overflow at 320 px (${out.map(([n]) => n).join(', ')})`) : bad('C320', 'overflow at 320 px', JSON.stringify(over))
    await page.keyboard.press('Escape')
    await page.setViewportSize({ width: 1440, height: 900 })
  }],
  ['C730', async () => {
    await page.setViewportSize({ width: 1536, height: 730 })
    const d = await openEditDialog(f.H1b.visit)
    const btn = d.getByRole('button', { name: /^Enregistrer/ }).last()
    const b = await btn.boundingBox()
    b && b.y + b.height <= 730 ? ok('C730', 'edit dialog footer reachable at 1536×730') : bad('C730', 'footer off screen at 730', JSON.stringify(b))
    await page.screenshot({ path: join(shots, 'w4-C730-edit.png') })
    await page.keyboard.press('Escape')
    await page.setViewportSize({ width: 1440, height: 900 })
  }],
]

for (const [id, fn] of scenarios) {
  try { await fn() } catch (err) {
    bad(id, 'threw', String(err).split('\n')[0].slice(0, 220))
    await page.screenshot({ path: join(shots, `w4-${id}-threw.png`) }).catch(() => {})
    await page.keyboard.press('Escape').catch(() => {})
  }
}

await browser.close()
console.log('\n── findings ──')
findings.length ? findings.forEach((x) => console.log(JSON.stringify(x))) : console.log('ALL CLEAR')
