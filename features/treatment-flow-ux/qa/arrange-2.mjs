// Fixtures for the gap pass (plan-2.md): every plan state, several acts, six séances, a mixed séance, a refund
// case, and a secretary account. Throwaway patients only; writes fixtures-2.json beside itself.
import { writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { totp, msToFreshWindow } from '../../devis-fiche-rdv-flexibility/qa/totp-act-onto-devis.mjs'

const here = dirname(fileURLToPath(import.meta.url))
const API = 'http://localhost:5000/api'
const ADMIN = { email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', secret: '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO' }
const PT = {
  couronne: 'a976d389-7593-4ba9-b87a-6b8d3bf97572',
  detartrage: '8226ce23-e8e3-4b34-9f75-2069c3b92605',
  canal: '719d83b6-9169-49ec-887d-f9e8b8a938f3',
  carie: '18285b7e-5ca1-47bb-abf3-0525a42acafd',
}

const fresh = () => new Promise((r) => setTimeout(r, msToFreshWindow()))
async function raw(method, path, body, token) {
  const r = await fetch(`${API}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await r.text()
  let json; try { json = text ? JSON.parse(text) : null } catch { json = text }
  return { ok: r.ok, status: r.status, json: json?.value ?? json }
}

await fresh()
const login = await raw('POST', '/auth/login', { email: ADMIN.email, password: ADMIN.password, totpCode: totp(ADMIN.secret) })
const token = login.json?.accessToken
if (!token) throw new Error('login failed ' + JSON.stringify(login))
async function call(method, path, body) {
  const r = await raw(method, path, body, token)
  if (!r.ok) throw new Error(`${method} ${path} → ${r.status} ${JSON.stringify(r.json).slice(0, 400)}`)
  return r.json
}

const ts = Date.now().toString().slice(-6)
const todayIso = new Date().toISOString().slice(0, 10)
const day = (offset, hour = 10) => { const d = new Date(); d.setDate(d.getDate() + offset); d.setHours(hour, 0, 0, 0); return d.toISOString() }
const isoDay = (offset) => { const d = new Date(); d.setDate(d.getDate() + offset); return d.toISOString().slice(0, 10) }
const patient = (tag) => call('POST', '/patients', {
  firstName: `QAG${tag}`, lastName: `Gap ${ts}`, dateOfBirth: '1985-02-02', gender: 'Male',
  phoneNumber: `+2162${Math.floor(1000000 + Math.random() * 8999999)}`, allowDuplicate: true,
})
const devis = (patientId, acts, title) => call('POST', '/treatment-plans', {
  patientId, title,
  items: acts.map(([pt, cost, name, teeth = [16]]) => ({ designationFr: name, plannedCost: cost, procedureTypeId: pt, toothNumbers: teeth })),
  installments: [],
})
const get = (id) => call('GET', `/treatment-plans/${id}`)
const accept = async (id) => { const p = await get(id); if (!p.number) await call('POST', `/treatment-plans/${id}/accept`, {}) }
const visit = (patientId, offset, procedures, planId, hour = 10) => call('POST', '/appointments', {
  patientId, appointmentDateTime: day(offset, hour), durationMinutes: 30, procedures, treatmentPlanId: planId ?? null,
  allowOutsideWorkingHours: true, allowOverlap: true,
})
const act = (pt, name, cost = 0, extra = {}) => ({ procedureTypeId: pt, procedureName: name, cost, unitCost: cost, isPerTooth: false, toothNumbers: [16], ...extra })
const fiche = (patientId, o) => call('POST', `/patients/${patientId}/dental-records`, {
  interventionDate: o.date ?? todayIso, amountPaid: o.amountPaid ?? 0, isAdultTeeth: true, notes: [], importantNotes: [],
  acts: o.acts, treatmentPlanId: o.planId ?? null, treatmentPlanItemId: o.itemId ?? null,
  treatmentPlanItemStepId: o.stepId ?? null, appointmentId: o.appointmentId ?? null,
})
async function setSteps(planId, itemId, labels) {
  const p = await get(planId)
  await call('PUT', `/treatment-plans/${planId}/items/${itemId}/steps`, {
    version: p.version,
    steps: labels.map((label, i) => ({ id: null, label, estimatedDurationMinutes: 30, minDaysAfterPrevious: i === 1 ? 7 : null })),
  })
  return (await get(planId)).items.find((i) => i.id === itemId)
}
/** A numbered couronne on 16 in three séances, séance 1 recorded 8 days ago. */
async function couronneWithSeance1(tag, cost = 300) {
  const P = await patient(tag)
  const plan = await devis(P.id, [[PT.couronne, cost, 'Couronne']], 'Couronne')
  const item = await setSteps(plan.id, plan.items[0].id, ['Préparation', 'Empreinte', 'Scellement'])
  await accept(plan.id)
  const v1 = await visit(P.id, -8, [{ procedureTypeId: PT.couronne, treatmentPlanItemId: item.id, treatmentPlanItemStepId: item.steps[0].id }], plan.id, 9)
  await fiche(P.id, { acts: [act(PT.couronne, 'Couronne / bridge (par élément)', 0)], planId: plan.id, itemId: item.id, stepId: item.steps[0].id, appointmentId: v1.id, date: isoDay(-8) })
  return { P, plan: await get(plan.id), item }
}
const pay = async (planId, amount) => {
  const p = await get(planId)
  await call('POST', `/treatment-plans/${planId}/installments/${p.installments[0].id}/payments`, { amount, method: 'Cash', paidOn: todayIso })
}
const out = (P, planId) => ({ patient: P.id, name: `${P.firstName} ${P.lastName}`, plan: planId })

const f = { ts, today: todayIso }
const step = async (name, fn) => { try { f[name] = await fn() } catch (e) { f[name] = { error: String(e.message).slice(0, 300) }; console.error(name, e.message) } }

await step('multi', async () => {        // two acts: a 3-séance couronne (séance 1 done) + a détartrage, 60 paid
  const P = await patient('Multi')
  const plan = await devis(P.id, [[PT.couronne, 300, 'Couronne'], [PT.detartrage, 60, 'Détartrage', []]], 'Plan de traitement')
  const item = await setSteps(plan.id, plan.items[0].id, ['Préparation', 'Empreinte', 'Scellement'])
  await accept(plan.id)
  const v1 = await visit(P.id, -8, [{ procedureTypeId: PT.couronne, treatmentPlanItemId: item.id, treatmentPlanItemStepId: item.steps[0].id }], plan.id, 9)
  await fiche(P.id, { acts: [act(PT.couronne, 'Couronne / bridge (par élément)', 0)], planId: plan.id, itemId: item.id, stepId: item.steps[0].id, appointmentId: v1.id, date: isoDay(-8) })
  await pay(plan.id, 60)
  return out(P, plan.id)
})
await step('long', async () => {         // one act in six séances → the strip scrolls in its own box
  const P = await patient('Long')
  const plan = await devis(P.id, [[PT.canal, 450, 'Traitement de canal']], 'Traitement de canal')
  await setSteps(plan.id, plan.items[0].id, ['Ouverture', 'Mise en forme', 'Médication', 'Contrôle', 'Obturation', 'Reconstitution'])
  await accept(plan.id)
  return out(P, plan.id)
})
await step('billed', async () => {       // the devis is represented by an issued note
  const { P, plan } = await couronneWithSeance1('Billed', 200)
  const inv = await call('POST', `/invoices/from-plan/${plan.id}`, {})
  await call('POST', `/invoices/${inv.id}/issue`, {})
  return { ...out(P, plan.id), invoice: inv.id }
})
await step('stopped', async () => {      // séance 1 done, then « Arrêter »
  const { P, plan } = await couronneWithSeance1('Stopped')
  const p = await get(plan.id)
  await call('POST', `/treatment-plans/${plan.id}/stop`, { version: p.version, reason: 'Patient parti' })
  return out(P, plan.id)
})
await step('writtenOff', async () => {   // séance 1 done, 50 paid, then « Ne plus réclamer »
  const { P, plan } = await couronneWithSeance1('WrittenOff')
  await pay(plan.id, 50)
  const p = await get(plan.id)
  await call('POST', `/treatment-plans/${plan.id}/stop`, { version: p.version, reason: 'Patient parti' })
  const p2 = await get(plan.id)
  await call('POST', `/treatment-plans/${plan.id}/write-off`, { reason: 'Irrécouvrable', version: p2.version })
  return out(P, plan.id)
})
await step('cancelled', async () => {    // numbered, nothing done → « Annuler le devis »
  const P = await patient('Cancelled')
  const plan = await devis(P.id, [[PT.carie, 90, 'Soin de carie', [26]]], 'Soin de carie')
  await accept(plan.id)
  const p = await get(plan.id)
  await call('POST', `/treatment-plans/${plan.id}/cancel`, { reason: 'Erreur de saisie', version: p.version })
  return out(P, plan.id)
})
await step('refund', async () => {       // numbered, 80 deposit, no work → stop needs the refund first
  const P = await patient('Refund')
  const plan = await devis(P.id, [[PT.couronne, 300, 'Couronne']], 'Couronne')
  await setSteps(plan.id, plan.items[0].id, ['Préparation', 'Empreinte', 'Scellement'])
  await accept(plan.id)
  await pay(plan.id, 80)
  return out(P, plan.id)
})
await step('fresh', async () => {        // numbered, nothing done, nothing paid → Annuler / Supprimer / Dupliquer
  const P = await patient('Fresh')
  const plan = await devis(P.id, [[PT.carie, 90, 'Soin de carie', [26]]], 'Soin de carie')
  await accept(plan.id)
  return out(P, plan.id)
})
await step('conflict', async () => {     // for the 409 « Recharger » check on the in-row editor
  const { P, plan, item } = await couronneWithSeance1('Conflict')
  return { ...out(P, plan.id), item: item.id }
})
await step('mixed', async () => {        // a followed treatment (no devis) + today's visit on séance 1
  const P = await patient('Mixed')
  const started = await call('POST', '/treatment-plans/start', {
    patientId: P.id, procedureTypeId: PT.couronne, designationFr: 'Couronne / bridge (par élément)', plannedCost: 400, toothNumbers: [26],
  })
  const it = started.items[0]
  const v = await visit(P.id, 0, [{ procedureTypeId: PT.couronne, treatmentPlanItemId: it.id, treatmentPlanItemStepId: it.steps?.[0]?.id ?? null }], started.id, 7)
  return { ...out(P, started.id), visit: v.id }
})
if (!process.env.QA_NO_SECRETARY) await step('secretary', async () => {    // a reception account, enrolled and past its forced password change
  const email = `qa.secr.${ts}@cabinet-ibnkhaldoun.tn`
  const created = await call('POST', '/users', { email, fullName: `QA Secrétaire ${ts}`, role: 'secretary' })
  const temp = created.temporaryPassword
  await fresh()
  const enrol1 = await raw('POST', '/auth/totp/enrol', { email, password: temp })
  const text = JSON.stringify(enrol1.json)
  const secret = /secret["=:\s]+([A-Z2-7]{16,})/i.exec(text)?.[1] ?? /secret=([A-Z2-7]+)/i.exec(text)?.[1]
  if (!secret) throw new Error('no secret in enrol response ' + text.slice(0, 300))
  await fresh()
  const enrol2 = await raw('POST', '/auth/totp/enrol', { email, password: temp, totpCode: totp(secret) })
  if (!enrol2.ok) throw new Error('enrol confirm ' + JSON.stringify(enrol2.json).slice(0, 300))
  await fresh()
  const l1 = await raw('POST', '/auth/login', { email, password: temp, totpCode: totp(secret) })
  const t1 = l1.json?.accessToken
  const newPassword = `QaSecr${ts}!x`
  if (t1) {
    const ch = await raw('POST', '/auth/change-password', { currentPassword: temp, newPassword }, t1)
    if (!ch.ok) throw new Error('change-password ' + JSON.stringify(ch.json).slice(0, 300))
  } else throw new Error('secretary login ' + JSON.stringify(l1.json).slice(0, 300))
  return { email, password: newPassword, secret }
})

writeFileSync(join(here, 'fixtures-2.json'), JSON.stringify(f, null, 2))
console.log(JSON.stringify(f, null, 2))
