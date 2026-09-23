// Arrange the wave-1 fixtures over the API, on a fresh throwaway patient. Writes fixtures.json beside itself.
// Usage: node arrange.mjs <path-to-totp.mjs>
import { writeFileSync } from 'node:fs'
import { execSync } from 'node:child_process'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const { totp } = await import(process.argv[2])
const API = 'http://localhost:5000/api'
const SECRET = '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'
const PT = {
  canal: '719d83b6-9169-49ec-887d-f9e8b8a938f3',
  carie: '18285b7e-5ca1-47bb-abf3-0525a42acafd',
  detartrage: '8226ce23-e8e3-4b34-9f75-2069c3b92605',
  couronne: 'a976d389-7593-4ba9-b87a-6b8d3bf97572',
  implant: '8ed0e23d-9947-447c-b62d-7581e90ae623',
}

const login = await (await fetch(`${API}/auth/login`, {
  method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp(SECRET) }),
})).json()
const token = login.value?.accessToken ?? login.accessToken
if (!token) throw new Error('login failed ' + JSON.stringify(login))

async function call(method, path, body) {
  const r = await fetch(`${API}${path}`, {
    method, headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await r.text()
  let json; try { json = text ? JSON.parse(text) : null } catch { json = text }
  if (!r.ok) throw new Error(`${method} ${path} → ${r.status} ${text.slice(0, 300)}`)
  return json?.value ?? json
}

const ts = Date.now().toString().slice(-6)
const day = (offset, hour = 10) => {
  const d = new Date(); d.setDate(d.getDate() + offset); d.setHours(hour, 0, 0, 0); return d.toISOString()
}
const todayIso = new Date().toISOString().slice(0, 10)

async function patient(tag) {
  return call('POST', '/patients', {
    firstName: `QA${tag}`, lastName: `Flex ${ts}`, dateOfBirth: '1990-05-05', gender: 'Female',
    phoneNumber: `+2162${Math.floor(1000000 + Math.random() * 8999999)}`, allowDuplicate: true,
  })
}
async function devis(patientId, acts, title = 'QA devis') {
  return call('POST', '/treatment-plans', {
    patientId, title,
    items: acts.map(([pt, cost, name]) => ({ designationFr: name, plannedCost: cost, procedureTypeId: pt, toothNumbers: [16] })),
    installments: [],
  })
}
async function visit(patientId, offset, procedures, planId, hour = 10) {
  return call('POST', '/appointments', {
    patientId, appointmentDateTime: day(offset, hour), durationMinutes: 30,
    procedures, treatmentPlanId: planId ?? null, allowOutsideWorkingHours: true, allowOverlap: true,
  })
}
const act = (pt, name, cost = 0) => ({
  procedureTypeId: pt, procedureName: name, cost, unitCost: cost, isPerTooth: false, toothNumbers: [16],
})
async function fiche(patientId, { acts, planId, itemId, stepId, appointmentId }) {
  return call('POST', `/patients/${patientId}/dental-records`, {
    interventionDate: todayIso, amountPaid: 0, isAdultTeeth: true, notes: [], importantNotes: [],
    acts, treatmentPlanId: planId ?? null, treatmentPlanItemId: itemId ?? null,
    treatmentPlanItemStepId: stepId ?? null, appointmentId: appointmentId ?? null,
  })
}

const f = { ts }
const P = await patient('A'); f.patient = P.id
const P2 = await patient('B'); f.patient2 = P2.id

// F1 — A1/A2/B1: one-act devis, visit yesterday, fiche recorded against it → devis Completed.
{
  const plan = await devis(P.id, [[PT.canal, 150, 'Traitement de canal']], 'QA F1')
  const item = plan.items[0]
  const v = await visit(P.id, -1, [{ procedureTypeId: PT.canal, treatmentPlanItemId: item.id }], plan.id, 9)
  const r = await fiche(P.id, { acts: [act(PT.canal, 'Traitement de canal (dévitalisation)')], planId: plan.id, itemId: item.id, appointmentId: v.id })
  f.F1 = { plan: plan.id, item: item.id, visit: v.id, record: r.id }
}
// F2 — A3: devis + future visit; devis put in the LEGACY state (cancelled with the visit still linked) by SQL.
{
  const plan = await devis(P.id, [[PT.carie, 90, 'Soin de carie']], 'QA F2')
  const v = await visit(P.id, 3, [{ procedureTypeId: PT.carie, treatmentPlanItemId: plan.items[0].id }], plan.id, 11)
  f.F2 = { plan: plan.id, item: plan.items[0].id, visit: v.id }
}
// F3 — A4: an act created, booked on a future visit, then archived.
{
  const pt = await call('POST', '/procedure-types', { name: `QA Archive ${ts}`, defaultDurationMinutes: 30, colorHex: '#2A9D8F', defaultCost: 50 })
  const v = await visit(P.id, 4, [{ procedureTypeId: pt.id }], null, 12)
  const del = await call('DELETE', `/procedure-types/${pt.id}`)
  f.F3 = { procedureType: pt.id, visit: v.id, archived: del?.archived }
}
// F4 — E1: implant booked as ONE séance with no treatment.
{
  const v = await visit(P.id, 5, [{ procedureTypeId: PT.implant }], null, 13)
  f.F4 = { visit: v.id }
}
// F5 — B2: 3-séance couronne, walk-in fiche (no visit) → séance 1 done.
{
  const plan = await devis(P.id, [[PT.couronne, 500, 'Couronne']], 'QA F5')
  const item = plan.items[0]
  const r = await fiche(P.id, { acts: [act(PT.couronne, 'Couronne / bridge (par élément)')], planId: plan.id, itemId: item.id })
  f.F5 = { plan: plan.id, item: item.id, record: r.id, steps: item.steps.map((s) => s.id) }
}
// F6 — B3: visit booked on séance 2, then séance 2 removed from the protocol.
{
  const plan = await devis(P.id, [[PT.couronne, 500, 'Couronne']], 'QA F6')
  const item = plan.items[0]
  const s = item.steps
  const v = await visit(P.id, -1, [{ procedureTypeId: PT.couronne, treatmentPlanItemId: item.id, treatmentPlanItemStepId: s[1].id }], plan.id, 14)
  const fresh = await call('GET', `/treatment-plans/${plan.id}`)
  await call('PUT', `/treatment-plans/${plan.id}/items/${item.id}/steps`, {
    version: fresh.version,
    steps: [s[0], s[2]].map((x) => ({ id: x.id, label: x.label, estimatedDurationMinutes: x.estimatedDurationMinutes, minDaysAfterPrevious: x.minDaysAfterPrevious ?? null })),
  })
  f.F6 = { plan: plan.id, item: item.id, visit: v.id, removedStep: s[1].id }
}
// F7 — B6: fresh 3-séance couronne, nothing done.
{
  const plan = await devis(P2.id, [[PT.couronne, 500, 'Couronne']], 'QA F7')
  f.F7 = { plan: plan.id, item: plan.items[0].id, steps: plan.items[0].steps.map((s) => ({ id: s.id, label: s.label })) }
}
// F8 — C1: one-act devis done by a walk-in fiche.
{
  const plan = await devis(P2.id, [[PT.canal, 150, 'Traitement de canal']], 'QA F8')
  const r = await fiche(P2.id, { acts: [act(PT.canal, 'Traitement de canal (dévitalisation)')], planId: plan.id, itemId: plan.items[0].id })
  f.F8 = { plan: plan.id, item: plan.items[0].id, record: r.id }
}
// F10 — D1: numbered devis, nothing delivered, a future visit booked on it.
{
  const P3 = await patient('C'); f.patient3 = P3.id
  const plan = await devis(P3.id, [[PT.canal, 150, 'Traitement de canal']], 'QA F10')
  const v = await visit(P3.id, 6, [{ procedureTypeId: PT.canal, treatmentPlanItemId: plan.items[0].id }], plan.id, 9)
  f.F10 = { plan: plan.id, visit: v.id }
}
// F11 — D2: followed treatment (Draft) with a booked visit.
{
  const P4 = await patient('D'); f.patient4 = P4.id
  const plan = await call('POST', '/treatment-plans/start', { patientId: P4.id, procedureTypeId: PT.couronne, agreedTotal: null, toothNumbers: [] })
  const item = plan.items[0]
  const v = await visit(P4.id, 7, [{ procedureTypeId: PT.couronne, treatmentPlanItemId: item.id, treatmentPlanItemStepId: item.steps[0].id }], plan.id, 9)
  f.F11 = { plan: plan.id, visit: v.id }
}
// F12 — D3: two acts, one delivered, one booked → stop over the API.
{
  const plan = await devis(P.id, [[PT.canal, 150, 'Traitement de canal'], [PT.carie, 90, 'Soin de carie']], 'QA F12')
  const [done, booked] = plan.items
  await fiche(P.id, { acts: [act(PT.canal, 'Traitement de canal (dévitalisation)')], planId: plan.id, itemId: done.id })
  const v = await visit(P.id, 8, [{ procedureTypeId: PT.carie, treatmentPlanItemId: booked.id }], plan.id, 9)
  const fresh = await call('GET', `/treatment-plans/${plan.id}`)
  const stopped = await call('POST', `/treatment-plans/${plan.id}/stop`, { version: fresh.version })
  f.F12 = { plan: plan.id, visit: v.id, planStatus: stopped.status }
}
// F13 — D4: a visit on séance 1, then disregarded.
{
  const plan = await devis(P2.id, [[PT.couronne, 500, 'Couronne']], 'QA F13')
  const item = plan.items[0]
  const v = await visit(P2.id, 9, [{ procedureTypeId: PT.couronne, treatmentPlanItemId: item.id, treatmentPlanItemStepId: item.steps[0].id }], plan.id, 9)
  await call('POST', `/appointments/${v.id}/disregard`, {})
  f.F13 = { plan: plan.id, visit: v.id }
}
// R1 — ordinary visit, no devis.
{
  const v = await visit(P.id, 10, [{ procedureTypeId: PT.detartrage }], null, 9)
  f.R1 = { visit: v.id }
}
// R2 — the create path still books a live devis act (new-link validation).
{
  const plan = await devis(P2.id, [[PT.carie, 90, 'Soin de carie']], 'QA R2')
  const v = await visit(P2.id, 11, [{ procedureTypeId: PT.carie, treatmentPlanItemId: plan.items[0].id }], plan.id, 9)
  f.R2 = { plan: plan.id, visit: v.id }
}
// R3 — amend removing a booked, not-done act still cancels its visit.
{
  const plan = await devis(P2.id, [[PT.carie, 90, 'Soin de carie'], [PT.detartrage, 60, 'Détartrage']], 'QA R3')
  const v = await visit(P2.id, 12, [{ procedureTypeId: PT.detartrage, treatmentPlanItemId: plan.items[1].id }], plan.id, 9)
  const fresh = await call('GET', `/treatment-plans/${plan.id}`)
  await call('POST', `/treatment-plans/${plan.id}/amend`, { version: fresh.version, removeItemIds: [plan.items[1].id] })
  f.R3 = { plan: plan.id, visit: v.id }
}
// R4 — fiche from a booked live devis step, recorded in the browser.
{
  const plan = await devis(P2.id, [[PT.canal, 150, 'Traitement de canal']], 'QA R4')
  // ⚠️ Not today: B2 saves a walk-in fiche on P2 today, and the app ties a visit-less fiche to that day's only visit.
  const v = await visit(P2.id, -4, [{ procedureTypeId: PT.canal, treatmentPlanItemId: plan.items[0].id }], plan.id, 8)
  f.R4 = { plan: plan.id, item: plan.items[0].id, visit: v.id }
}
// A5 — a live devis on P, so the create dialog offers « Actes du devis ».
{
  const plan = await devis(P.id, [[PT.detartrage, 60, 'Détartrage devis']], 'QA A5')
  f.A5 = { plan: plan.id }
}

writeFileSync(join(here, 'fixtures.json'), JSON.stringify(f, null, 2))
console.log(JSON.stringify(f, null, 2))
