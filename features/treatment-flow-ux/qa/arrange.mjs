// Fixtures for the treatment-flow UX walk. Throwaway patients only; writes fixtures.json beside itself.
// Usage: node features/treatment-flow-ux/qa/arrange.mjs
import { writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { totp, msToFreshWindow } from '../../devis-fiche-rdv-flexibility/qa/totp-act-onto-devis.mjs'

const here = dirname(fileURLToPath(import.meta.url))
const API = 'http://localhost:5000/api'
const SECRET = '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'
const PT = {
  couronne: 'a976d389-7593-4ba9-b87a-6b8d3bf97572',
  detartrage: '8226ce23-e8e3-4b34-9f75-2069c3b92605',
  canal: '719d83b6-9169-49ec-887d-f9e8b8a938f3',
  carie: '18285b7e-5ca1-47bb-abf3-0525a42acafd',
}

// A TOTP code is single-use per window: wait for a fresh one so a walk launched right after cannot collide.
await new Promise((r) => setTimeout(r, msToFreshWindow()))
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
const todayIso = new Date().toISOString().slice(0, 10)
const day = (offset, hour = 10) => {
  const d = new Date(); d.setDate(d.getDate() + offset); d.setHours(hour, 0, 0, 0); return d.toISOString()
}
const isoDay = (offset) => { const d = new Date(); d.setDate(d.getDate() + offset); return d.toISOString().slice(0, 10) }

const patient = (tag) => call('POST', '/patients', {
  firstName: `QAU${tag}`, lastName: `Flow ${ts}`, dateOfBirth: '1986-03-03', gender: 'Female',
  phoneNumber: `+2162${Math.floor(1000000 + Math.random() * 8999999)}`, allowDuplicate: true,
})
const devis = (patientId, acts, title) => call('POST', '/treatment-plans', {
  patientId, title,
  items: acts.map(([pt, cost, name, teeth = [16]]) => ({
    designationFr: name, plannedCost: cost, procedureTypeId: pt, toothNumbers: teeth,
  })),
  installments: [],
})
const visit = (patientId, offset, procedures, planId, hour = 10) => call('POST', '/appointments', {
  patientId, appointmentDateTime: day(offset, hour), durationMinutes: 30,
  procedures, treatmentPlanId: planId ?? null, allowOutsideWorkingHours: true, allowOverlap: true,
})
const act = (pt, name, cost = 0, extra = {}) => ({
  procedureTypeId: pt, procedureName: name, cost, unitCost: cost, isPerTooth: false, toothNumbers: [16], ...extra,
})
const fiche = (patientId, { acts, planId, itemId, stepId, appointmentId, amountPaid = 0, date = todayIso }) =>
  call('POST', `/patients/${patientId}/dental-records`, {
    interventionDate: date, amountPaid, isAdultTeeth: true, notes: [], importantNotes: [],
    acts, treatmentPlanId: planId ?? null, treatmentPlanItemId: itemId ?? null,
    treatmentPlanItemStepId: stepId ?? null, appointmentId: appointmentId ?? null,
  })

/** Cut an act's protocol to exactly the given séances; the 2nd one carries a 7-day delay (kept by every save). */
async function setSteps(planId, itemId, labels) {
  const fresh = await call('GET', `/treatment-plans/${planId}`)
  await call('PUT', `/treatment-plans/${planId}/items/${itemId}/steps`, {
    version: fresh.version,
    steps: labels.map((label, i) => ({
      id: null, label, estimatedDurationMinutes: 30, minDaysAfterPrevious: i === 1 ? 7 : null,
    })),
  })
  return (await call('GET', `/treatment-plans/${planId}`)).items.find((i) => i.id === itemId)
}

/** Number the devis — a hand-made plan is un-numbered until accepted. */
const accept = async (planId) => {
  const p = await call('GET', `/treatment-plans/${planId}`)
  if (!p.number) await call('POST', `/treatment-plans/${planId}/accept`, {})
}

const f = { ts, today: todayIso, procedureTypes: PT }
const types = await call('GET', '/procedure-types')
const list = Array.isArray(types) ? types : (types.items ?? [])
f.couronneName = list.find((t) => t.id === PT.couronne)?.name ?? 'Couronne'
f.detartrageName = list.find((t) => t.id === PT.detartrage)?.name ?? 'Détartrage'
f.carieName = list.find((t) => t.id === PT.carie)?.name ?? 'Soin de carie'
f.couronneSteps = (list.find((t) => t.id === PT.couronne)?.steps ?? []).map((s) => s.label)

// ── MAIN: couronne 16 in 3 séances on a numbered devis — séance 1 done, séance 2 booked, 50 paid ──────────
{
  const P = await patient('Main')
  const plan = await devis(P.id, [[PT.couronne, 300, 'Couronne']], 'Couronne')
  const item = await setSteps(plan.id, plan.items[0].id, ['Préparation', 'Empreinte', 'Scellement'])
  await accept(plan.id)
  const v1 = await visit(P.id, -8, [{
    procedureTypeId: PT.couronne, treatmentPlanItemId: item.id, treatmentPlanItemStepId: item.steps[0].id,
  }], plan.id, 9)
  const r1 = await fiche(P.id, {
    acts: [act(PT.couronne, f.couronneName, 0)], planId: plan.id, itemId: item.id,
    stepId: item.steps[0].id, appointmentId: v1.id, date: isoDay(-8),
  })
  const accepted = await call('GET', `/treatment-plans/${plan.id}`)
  if (accepted.installments?.[0]) {
    await call('POST', `/treatment-plans/${plan.id}/installments/${accepted.installments[0].id}/payments`, {
      amount: 50, method: 'Cash', paidOn: todayIso,
    })
  }
  const v2 = await visit(P.id, 3, [{
    procedureTypeId: PT.couronne, treatmentPlanItemId: item.id, treatmentPlanItemStepId: item.steps[1].id,
  }], plan.id, 10)
  const after = await call('GET', `/treatment-plans/${plan.id}`)
  f.main = {
    patient: P.id, name: `${P.firstName} ${P.lastName}`, plan: plan.id, number: after.number, status: after.status,
    item: item.id, steps: item.steps.map((s) => ({ id: s.id, label: s.label, minDays: s.minDaysAfterPrevious })),
    visit1: v1.id, record1: r1.id, visit2: v2.id,
    totalPlanned: after.totalPlanned, amountPaid: after.amountPaid, outstanding: after.outstanding,
  }
}

// ── TODAY: same shape, séance 2 booked TODAY — the fiche walk records it (throwaway, one save) ─────────────
{
  const P = await patient('Today')
  const plan = await devis(P.id, [[PT.couronne, 250, 'Couronne']], 'Couronne')
  const item = await setSteps(plan.id, plan.items[0].id, ['Préparation', 'Empreinte', 'Scellement'])
  await accept(plan.id)
  const v1 = await visit(P.id, -6, [{
    procedureTypeId: PT.couronne, treatmentPlanItemId: item.id, treatmentPlanItemStepId: item.steps[0].id,
  }], plan.id, 9)
  await fiche(P.id, {
    acts: [act(PT.couronne, f.couronneName, 0)], planId: plan.id, itemId: item.id,
    stepId: item.steps[0].id, appointmentId: v1.id, date: isoDay(-6),
  })
  const v2 = await visit(P.id, 0, [{
    procedureTypeId: PT.couronne, treatmentPlanItemId: item.id, treatmentPlanItemStepId: item.steps[1].id,
  }], plan.id, 8)
  const after = await call('GET', `/treatment-plans/${plan.id}`)
  f.today = {
    patient: P.id, name: `${P.firstName} ${P.lastName}`, plan: plan.id, number: after.number, item: item.id,
    steps: item.steps.map((s) => ({ id: s.id, label: s.label })), visit2: v2.id,
  }
}

// ── DRAFT: a followed treatment, un-numbered, nothing done → « À commencer » + « Pas de devis » ────────────
{
  const P = await patient('Draft')
  const started = await call('POST', '/treatment-plans/start', {
    patientId: P.id, procedureTypeId: PT.couronne, designationFr: f.couronneName, plannedCost: 400, toothNumbers: [26],
  })
  f.draft = {
    patient: P.id, name: `${P.firstName} ${P.lastName}`, plan: started.id, number: started.number,
    status: started.status, steps: (started.items[0].steps ?? []).map((s) => s.label),
  }
}

// ── CONT: a past act ticked « non terminé » → offered under « Continuer un traitement » ────────────────────
{
  const P = await patient('Cont')
  const v = await visit(P.id, -5, [{ procedureTypeId: PT.carie }], null, 11)
  const r = await fiche(P.id, {
    acts: [act(PT.carie, f.carieName, 60, { isUnfinished: true, toothNumbers: [26] })],
    appointmentId: v.id, amountPaid: 60, date: isoDay(-5),
  })
  f.cont = { patient: P.id, name: `${P.firstName} ${P.lastName}`, visit: v.id, record: r.id }
}

// ── PLAIN: an ordinary patient with nothing on file → booking a new multi-séance act from scratch ──────────
{
  const P = await patient('Plain')
  f.plain = { patient: P.id, name: `${P.firstName} ${P.lastName}` }
}

writeFileSync(join(here, 'fixtures.json'), JSON.stringify(f, null, 2))
console.log(JSON.stringify(f, null, 2))
