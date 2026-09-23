// Arrange the wave-2 fixtures over the API on fresh throwaway « QA? Flex » patients. Writes fixtures-2.json.
// Usage: node arrange-2.mjs <path-to-totp.mjs>
import { writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const { totp } = await import(pathToFileURL(process.argv[2]).href)
const API = 'http://localhost:5000/api'
const PT = {
  canal: '719d83b6-9169-49ec-887d-f9e8b8a938f3',
  detartrage: '8226ce23-e8e3-4b34-9f75-2069c3b92605',
  couronne: 'a976d389-7593-4ba9-b87a-6b8d3bf97572',
}

const login = await (await fetch(`${API}/auth/login`, {
  method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO') }),
})).json()
const token = login.value?.accessToken ?? login.accessToken
if (!token) throw new Error('login failed ' + JSON.stringify(login))

async function call(method, path, body, allowFail = false) {
  const r = await fetch(`${API}${path}`, {
    method, headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await r.text()
  let json; try { json = text ? JSON.parse(text) : null } catch { json = text }
  if (!r.ok && !allowFail) throw new Error(`${method} ${path} → ${r.status} ${text.slice(0, 300)}`)
  return allowFail ? { status: r.status, body: json?.value ?? json } : json?.value ?? json
}

const ts = Date.now().toString().slice(-6)
const day = (offset, hour = 10) => { const d = new Date(); d.setDate(d.getDate() + offset); d.setHours(hour, 0, 0, 0); return d.toISOString() }
const patient = (tag) => call('POST', '/patients', {
  firstName: `QA${tag}`, lastName: `Flex ${ts}`, dateOfBirth: '1990-05-05', gender: 'Female',
  phoneNumber: `+2162${Math.floor(1000000 + Math.random() * 8999999)}`, allowDuplicate: true,
})
const devis = (patientId, acts, title) => call('POST', '/treatment-plans', {
  patientId, title,
  items: acts.map(([pt, cost, name]) => ({ designationFr: name, plannedCost: cost, procedureTypeId: pt, toothNumbers: [16] })),
  installments: [],
})

const f = { ts }
const P = await patient('W'); f.patient = P.id

// C4 — a devis with two acts, both booked into ONE visit today.
{
  const plan = await devis(P.id, [[PT.canal, 150, 'Traitement de canal'], [PT.detartrage, 80, 'Détartrage']], 'QA C4')
  const [a, b] = plan.items
  const v = await call('POST', '/appointments', {
    patientId: P.id, appointmentDateTime: day(0, 8), durationMinutes: 60, treatmentPlanId: plan.id,
    procedures: [
      { procedureTypeId: PT.canal, treatmentPlanItemId: a.id, treatmentPlanItemStepId: a.steps?.[0]?.id ?? null },
      { procedureTypeId: PT.detartrage, treatmentPlanItemId: b.id, treatmentPlanItemStepId: b.steps?.[0]?.id ?? null },
    ],
    allowOutsideWorkingHours: true, allowOverlap: true,
  })
  f.C4 = { plan: plan.id, items: [a.id, b.id], visit: v.id }
}

// F2 / F9 / F10 — a devis with a remise on one act and a 3-row schedule.
{
  const plan = await devis(P.id, [[PT.couronne, 300, 'Couronne'], [PT.detartrage, 90, 'Détartrage']], 'QA F2')
  const [crown] = plan.items
  const disc = await call('PUT', `/treatment-plans/${plan.id}/items/${crown.id}/discount`, { discountAmount: 50 }, true); if (disc.status >= 300) console.log('discount', disc)
  const fresh = await call('GET', `/treatment-plans/${plan.id}`)
  f.F2 = { plan: plan.id, total: fresh.totalPlanned, revision: fresh.revisionNumber, discount: fresh.items[0].discountAmount }
}

// E4 — the discard endpoint on an un-booked Draft (just started) and on a devis a visit books.
{
  const draft = await call('POST', '/treatment-plans/start', { patientId: P.id, procedureTypeId: PT.couronne, agreedTotal: 300, toothNumbers: [] })
  const d = await call('POST', `/treatment-plans/${draft.id}/discard-booking`, {}, true)
  const after = await call('GET', `/treatment-plans/${draft.id}`, undefined, true)
  const booked = await call('POST', `/treatment-plans/${f.C4.plan}/discard-booking`, {}, true)
  f.E4 = { draftStatus: d.status, draftAfter: after.status, bookedStatus: booked.status, bookedBody: booked.body }
}

writeFileSync(join(here, 'fixtures-2.json'), JSON.stringify(f, null, 2))
console.log(JSON.stringify(f, null, 2))
