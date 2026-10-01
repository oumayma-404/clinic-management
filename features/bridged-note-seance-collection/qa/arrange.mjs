// Fixtures for the bridged-note walk: the production case (devis 2026-0011) rebuilt twice, plus an ordinary
// devis for the regression row. Throwaway patients only. Writes fixtures.json beside the script it runs from.
import { writeFileSync } from 'node:fs'
import { totp } from './totp.mjs'

const API = 'http://localhost:5000/api'
const SECRET = '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'
const PT_COURONNE = 'a976d389-7593-4ba9-b87a-6b8d3bf97572'

const login = await (await fetch(`${API}/auth/login`, {
  method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp(SECRET),
  }),
})).json()
const token = login.value?.accessToken ?? login.accessToken
if (!token) throw new Error('login failed ' + JSON.stringify(login))

async function call(method, path, body) {
  const r = await fetch(`${API}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await r.text()
  let json; try { json = text ? JSON.parse(text) : null } catch { json = text }
  if (!r.ok) throw new Error(`${method} ${path} → ${r.status} ${text.slice(0, 400)}`)
  return json?.value ?? json
}

const ts = Date.now().toString().slice(-6)
const day = (offset, hour = 10) => {
  const d = new Date(); d.setDate(d.getDate() + offset); d.setHours(hour, 0, 0, 0); return d
}
const iso = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

const patient = (tag) => call('POST', '/patients', {
  firstName: `QABridge${tag}`, lastName: `Note ${ts}`, dateOfBirth: '1980-02-02', gender: 'Male',
  phoneNumber: `+2162${Math.floor(1000000 + Math.random() * 8999999)}`, allowDuplicate: true,
})
const visit = (patientId, when, procedures, planId) => call('POST', '/appointments', {
  patientId, appointmentDateTime: when.toISOString(), durationMinutes: 30,
  procedures, treatmentPlanId: planId ?? null, allowOutsideWorkingHours: true, allowOverlap: true,
})

/** The production case: séance 1 an ordinary fiche (160, 100 paid → note), then « c'est la suite ». */
async function bridged(tag, hour) {
  const P = await patient(tag)
  const d1 = day(-4, hour)
  const v1 = await visit(P.id, d1, [{ procedureTypeId: PT_COURONNE, agreedCost: 160 }], null)
  const r1 = await call('POST', `/patients/${P.id}/dental-records`, {
    interventionDate: iso(d1), amountPaid: 100, paymentMethod: 'Cash', isAdultTeeth: true, notes: [], importantNotes: [],
    acts: [{ procedureTypeId: PT_COURONNE, procedureName: 'Prothèse amovible', cost: 160, unitCost: 160,
      isPerTooth: false, toothNumbers: [11] }],
    appointmentId: v1.id,
  })
  const plan = await call('POST', '/treatment-plans/continue-recorded-act', {
    dentalRecordId: r1.id, actId: r1.acts[0].id,
  })
  const item = plan.items.find((i) => (i.steps ?? []).some((s) => !s.doneDate)) ?? plan.items[0]
  const step = item.steps.find((s) => !s.doneDate)
  const v2 = await visit(P.id, day(0, hour), [{
    procedureTypeId: PT_COURONNE, treatmentPlanItemId: item.id, treatmentPlanItemStepId: step.id,
  }], plan.id)
  return {
    patient: P.id, record1: r1.id, record1Date: iso(d1), billing1: r1.billing?.outcome ?? null,
    note: r1.billing?.invoice?.id ?? null, noteNumber: r1.billing?.invoice?.number ?? null,
    plan: plan.id, planNumber: plan.number, linkedInvoice: plan.linkedInvoiceNumber ?? null,
    item: item.id, step: step.id, visit2: v2.id,
  }
}

const f = { ts, today: iso(day(0)) }
f.main = await bridged('Main', 9)
f.over = await bridged('Over', 11)

// D1 — an ordinary devis (no note): 300 in two séances, 100 collected on the échéance.
{
  const P = await patient('Plain')
  const plan = await call('POST', '/treatment-plans', {
    patientId: P.id, title: `QA Plain ${ts}`, installments: [],
    items: [{ designationFr: 'Prothèse provisoire', plannedCost: 300, procedureTypeId: PT_COURONNE, toothNumbers: [16] }],
  })
  const fresh = await call('GET', `/treatment-plans/${plan.id}`)
  await call('PUT', `/treatment-plans/${plan.id}/items/${plan.items[0].id}/steps`, {
    version: fresh.version,
    steps: ['Préparation', 'Pose'].map((label) => ({ id: null, label, estimatedDurationMinutes: 30, minDaysAfterPrevious: null })),
  })
  const withSteps = await call('GET', `/treatment-plans/${plan.id}`)
  const ech = withSteps.installments[0]
  await call('POST', `/treatment-plans/${plan.id}/installments/${ech.id}/payments`, {
    amount: 100, method: 'Cash', paidOn: f.today,
  })
  const item = withSteps.items[0]
  const v = await visit(P.id, day(0, 13), [{
    procedureTypeId: PT_COURONNE, treatmentPlanItemId: item.id, treatmentPlanItemStepId: item.steps[1].id,
  }], plan.id)
  f.plain = { patient: P.id, plan: plan.id, visit2: v.id }
}

writeFileSync(new URL('./fixtures.json', import.meta.url), JSON.stringify(f, null, 2))
console.log(JSON.stringify(f, null, 2))
