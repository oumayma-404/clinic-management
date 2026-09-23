// Arrange the wave-4 fixtures over the API on a fresh throwaway « QAH Flex » patient. Writes fixtures-4.json.
// Usage: node arrange-4.mjs <path-to-totp.mjs>
import { writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const { totp } = await import(pathToFileURL(process.argv[2]).href)
const API = 'http://localhost:5000/api'
const PT = {
  couronne: 'a976d389-7593-4ba9-b87a-6b8d3bf97572',
  detartrage: '8226ce23-e8e3-4b34-9f75-2069c3b92605',
  canal: '719d83b6-9169-49ec-887d-f9e8b8a938f3',
}

const login = await (await fetch(`${API}/auth/login`, {
  method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO') }),
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
  if (!r.ok) throw new Error(`${method} ${path} → ${r.status} ${text.slice(0, 400)}`)
  return json?.value ?? json
}

const ts = Date.now().toString().slice(-6)
const localDay = (offset) => { const d = new Date(); d.setDate(d.getDate() + offset); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}` }
const at = (offset, hour) => { const d = new Date(); d.setDate(d.getDate() + offset); d.setHours(hour, 0, 0, 0); return d.toISOString() }

const P = await call('POST', '/patients', {
  firstName: 'QAH', lastName: `Flex ${ts}`, dateOfBirth: '1987-03-03', gender: 'Female',
  phoneNumber: `+2162${Math.floor(1000000 + Math.random() * 8999999)}`, allowDuplicate: true,
})
const f = { ts, patient: P.id, patientName: `QAH Flex ${ts}` }

const types = await call('GET', '/procedure-types')
const list = Array.isArray(types) ? types : (types.items ?? [])
const nameOf = (id) => list.find((t) => t.id === id)?.name
f.names = { couronne: nameOf(PT.couronne), detartrage: nameOf(PT.detartrage), canal: nameOf(PT.canal) }

const devis = (title, acts) => call('POST', '/treatment-plans', {
  patientId: P.id, title,
  items: acts.map(([pt, cost]) => ({ designationFr: nameOf(pt), plannedCost: cost, procedureTypeId: pt, toothNumbers: [16] })),
  installments: [],
})
const visit = (offset, hour, procedures, planId = null) => call('POST', '/appointments', {
  patientId: P.id, appointmentDateTime: at(offset, hour), durationMinutes: 30,
  procedures, treatmentPlanId: planId, allowOutsideWorkingHours: true, allowOverlap: true,
})
const act = (pt, cost = 0) => ({ procedureTypeId: pt, procedureName: nameOf(pt), cost, unitCost: cost, isPerTooth: false, toothNumbers: [16] })
const fiche = (body) => call('POST', `/patients/${P.id}/dental-records`, {
  interventionDate: body.date ?? localDay(0), amountPaid: 0, isAdultTeeth: true, notes: [], importantNotes: [],
  treatmentPlanId: null, treatmentPlanItemId: null, treatmentPlanItemStepId: null, appointmentId: null, ...body,
})
const setStatus = (id, status) => call('PUT', `/appointments/${id}`, { status })
async function setSteps(planId, itemId, steps) {
  const fresh = await call('GET', `/treatment-plans/${planId}`)
  await call('PUT', `/treatment-plans/${planId}/items/${itemId}/steps`, {
    version: fresh.version,
    steps: steps.map(([label, minDays]) => ({ id: null, label, estimatedDurationMinutes: 30, minDaysAfterPrevious: minDays })),
  })
  return (await call('GET', `/treatment-plans/${planId}`)).items.find((i) => i.id === itemId)
}

// H1 — a finished visit (3 days ago, 09:00) whose start time is corrected in the edit dialog.
{ const v = await visit(-3, 9, [{ procedureTypeId: PT.detartrage }]); await setStatus(v.id, 'Completed'); f.H1 = { visit: v.id } }
// H1b / J4 — a cancelled visit ahead: its date is locked, and a move over the API is refused by name.
{ const v = await visit(5, 10, [{ procedureTypeId: PT.detartrage }]); await setStatus(v.id, 'Cancelled'); f.H1b = { visit: v.id, at: at(5, 10) } }
// H10 — « En cours » today, moved to tomorrow over the API.
// ⚠️ Not today: a fiche saved with no visit is tied to that day's only visit by the app, which would close it.
{ const v = await visit(2, 8, [{ procedureTypeId: PT.detartrage }]); await setStatus(v.id, 'InProgress'); f.H10 = { visit: v.id } }
// H11 — an ordinary visit ahead with no devis act.
{ const v = await visit(6, 11, [{ procedureTypeId: PT.detartrage }]); f.H11 = { visit: v.id } }
// H6 — a finished visit (2 days ago) with its fiche billed on a numbered note.
{
  const v = await visit(-2, 10, [{ procedureTypeId: PT.detartrage }])
  const r = await fiche({ date: localDay(-2), appointmentId: v.id, acts: [act(PT.detartrage, 60)] })
  const inv = await call('POST', `/invoices/from-dental-record/${r.id}`, null)
  f.H6 = { visit: v.id, record: r.id, invoice: inv.id, invoiceNumber: inv.number, day: localDay(-2) }
}
// H7 — a visit whose fiche was deleted: « Terminé », no fiche.
{
  const v = await visit(-1, 9, [{ procedureTypeId: PT.detartrage }])
  const r = await fiche({ date: localDay(-1), appointmentId: v.id, acts: [act(PT.detartrage, 60)] })
  await call('DELETE', `/patients/${P.id}/dental-records/${r.id}`)
  f.H7 = { visit: v.id }
}
// J3 — booked « Couronne », the fiche recorded « Détartrage », not billed: « À clôturer » and the history say which.
{
  const v = await visit(-1, 15, [{ procedureTypeId: PT.couronne }])
  const r = await fiche({ date: localDay(-1), appointmentId: v.id, acts: [act(PT.detartrage, 60)] })
  f.J3 = { visit: v.id, record: r.id }
}
// H8 / J5 — a numbered 300 DT devis, 2 séances, séance 1 recorded with 100 collected chairside.
{
  const plan = await devis(`QA H8 ${ts}`, [[PT.couronne, 300]])
  await call('POST', `/treatment-plans/${plan.id}/accept`, { version: (await call('GET', `/treatment-plans/${plan.id}`)).version }).catch(() => null)
  const item = await setSteps(plan.id, plan.items[0].id, [['Préparation', null], ['Pose', null]])
  const r = await fiche({ date: localDay(-10), treatmentPlanId: plan.id, treatmentPlanItemId: item.id, treatmentPlanItemStepId: item.steps[0].id, acts: [act(PT.couronne, 0)], amountCollectedOnPlan: 100 })
  const after = await call('GET', `/treatment-plans/${plan.id}`)
  f.H8 = { plan: plan.id, number: after.number, record: r.id, status: after.status, amountPaid: after.amountPaid, title: `QA H8 ${ts}` }
}
// H9 — a 3-séance couronne whose séance 2 was recorded first.
{
  const plan = await devis(`QA H9 ${ts}`, [[PT.couronne, 450]])
  const item = await setSteps(plan.id, plan.items[0].id, [['Préparation', null], ['Empreinte', 30], ['Pose', 14]])
  await fiche({ date: localDay(-10), treatmentPlanId: plan.id, treatmentPlanItemId: item.id, treatmentPlanItemStepId: item.steps[1].id, acts: [act(PT.couronne, 0)] })
  f.H9 = { plan: plan.id, item: item.id, title: `QA H9 ${ts}` }
}
// C4b — one visit carrying TWO acts of one devis, its fiche saved over the API.
{
  const plan = await devis(`QA C4 ${ts}`, [[PT.canal, 150], [PT.couronne, 300]])
  const [a, b] = plan.items
  const v = await visit(0, 16, [
    { procedureTypeId: PT.canal, treatmentPlanItemId: a.id }, { procedureTypeId: PT.couronne, treatmentPlanItemId: b.id },
  ], plan.id)
  const r = await fiche({
    appointmentId: v.id, treatmentPlanId: plan.id, treatmentPlanItemId: a.id,
    additionalTreatmentPlanItems: [{ treatmentPlanItemId: b.id, treatmentPlanItemStepId: null }],
    acts: [act(PT.canal, 0), act(PT.couronne, 0)],
  })
  f.C4 = { plan: plan.id, visit: v.id, record: r.id }
}
// H2 — a fiche on a devis that is then cancelled; deleting the fiche must succeed.
{
  const plan = await devis(`QA H2 ${ts}`, [[PT.detartrage, 60]])
  const r = await fiche({ date: localDay(-10), treatmentPlanId: plan.id, treatmentPlanItemId: plan.items[0].id, acts: [act(PT.detartrage, 0)] })
  const fresh = await call('GET', `/treatment-plans/${plan.id}`)
  await call('POST', `/treatment-plans/${plan.id}/cancel`, { reason: 'QA H2', version: fresh.version })
  f.H2 = { plan: plan.id, record: r.id, status: (await call('GET', `/treatment-plans/${plan.id}`)).status }
}
// J5 — a numbered devis cancelled with nothing on it: its list menu must say why « Modifier » is not there.
{
  const plan = await devis(`QA J5 ${ts}`, [[PT.detartrage, 60]])
  const fresh = await call('GET', `/treatment-plans/${plan.id}`)
  await call('POST', `/treatment-plans/${plan.id}/cancel`, { reason: 'QA J5', version: fresh.version })
  f.J5 = { plan: plan.id, number: fresh.number, title: `QA J5 ${ts}` }
}
// I1 — an act used by a visit ahead, deleted ⇒ archived.
{
  const pt = await call('POST', '/procedure-types', { name: `QA Acte ${ts}`, defaultDurationMinutes: 30, defaultCost: 40, colorHex: '#4F83CC' })
  await visit(7, 9, [{ procedureTypeId: pt.id }])
  const del = await call('DELETE', `/procedure-types/${pt.id}`)
  f.I1 = { id: pt.id, name: `QA Acte ${ts}`, deletion: del }
}
// I4 — an act only a devis line names: the UI delete must archive it and say « ligne de devis ».
{
  const pt = await call('POST', '/procedure-types', { name: `QA Devis ${ts}`, defaultDurationMinutes: 30, defaultCost: 40, colorHex: '#4F83CC' })
  await call('POST', '/treatment-plans', { patientId: P.id, title: `QA I4 ${ts}`, items: [{ designationFr: `QA Devis ${ts}`, plannedCost: 40, procedureTypeId: pt.id, toothNumbers: [] }], installments: [] })
  f.I4 = { id: pt.id, name: `QA Devis ${ts}` }
}
// I3 — a throwaway practitioner added to the roster, then left off it ⇒ retired.
{
  const status = await call('GET', '/clinics/user-status')
  const roster = (status.doctors ?? []).filter((d) => d.isActive !== false)
    .map((d) => Object.fromEntries(Object.entries({ id: d.id, name: d.name, specialty: d.specialty, phone: d.phone, email: d.email, codeProfessionnelSante: d.codeProfessionnelSante }).filter(([, v]) => v != null && v !== '')))
  const added = await call('PUT', '/clinics/doctors', { doctors: [...roster, { name: `QA Retrait ${ts}`, specialty: 'GeneralDentist' }] })
  const qa = added.find((d) => d.name === `QA Retrait ${ts}`)
  await call('PUT', '/clinics/doctors', { doctors: roster })
  f.I3 = { id: qa.id, name: `QA Retrait ${ts}`, rosterSize: roster.length }
}

writeFileSync(join(here, 'fixtures-4.json'), JSON.stringify(f, null, 2))
console.log(JSON.stringify(f, null, 2))
