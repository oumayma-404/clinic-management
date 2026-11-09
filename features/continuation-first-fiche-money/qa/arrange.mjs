// Arrange the QA fixtures over the API of the ISOLATED stack (:5093, database clinic_qa_cff). Never prod.
import fs from 'node:fs'
import { freshTotp } from './totp.mjs'

const API = 'http://localhost:5093/api'
const EMAIL = 'salma.benyoussef@cabinet-ibnkhaldoun.tn'
const PASSWORD = 'QaAudit2026!y'
const SECRET = '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'
const DOCTOR = '9ed3d280-e3a1-4f11-ad19-8c993dd0da93'
const CANAL = '719d83b6-9169-49ec-887d-f9e8b8a938f3'
const CARIE = '18285b7e-5ca1-47bb-abf3-0525a42acafd'
const SEANCE1 = '2026-11-02T09:00:00Z'
const OUT = new URL('./fixtures.json', import.meta.url)

if (!API.includes(':5093')) throw new Error('refusing: not the isolated stack')

let token
async function call(method, path, body) {
  const res = await fetch(API + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  })
  const text = await res.text()
  let json; try { json = JSON.parse(text) } catch { json = text }
  if (!res.ok) throw new Error(`${method} ${path} → ${res.status} ${text.slice(0, 400)}`)
  return json && typeof json === "object" && "value" in json ? json.value : json
}

await new Promise(r => setTimeout(r, (31 - (Math.floor(Date.now() / 1000) % 30)) * 1000))
const login = await call("POST", "/auth/login", { email: EMAIL, password: PASSWORD, totpCode: await freshTotp(SECRET) })
token = login.accessToken
if (!token) throw new Error('no token: ' + JSON.stringify(login).slice(0, 300))

const stamp = Date.now().toString().slice(-5)
async function patient(first) {
  const p = await call('POST', '/patients', {
    firstName: first, lastName: `QaCff${stamp}`, gender: 'Male', dateOfBirth: '1980-05-05T00:00:00Z',
  })
  return p.id
}

async function fiche(patientId, { procedureTypeId, name, cost, paid, tooth }) {
  const r = await call('POST', `/patients/${patientId}/dental-records`, {
    patientId, interventionDate: SEANCE1, amountPaid: paid, paymentMethod: 'Cash', isAdultTeeth: true,
    doctorId: DOCTOR,
    acts: [{ procedureTypeId, procedureName: name, cost, unitCost: cost, isPerTooth: false, toothNumbers: [tooth] }],
  })
  return { id: r.id, actId: r.acts[0].id, billing: r.billing }
}

async function continueAct(f, remaining) {
  const plan = await call('POST', '/treatment-plans/continue-recorded-act', {
    dentalRecordId: f.id, actId: f.actId, nextStepLabel: 'Séance suivante',
    remainingWorkCost: remaining,
  })
  return plan
}

const fx = { stamp }
const canal = { procedureTypeId: CANAL, name: 'Traitement de canal (dévitalisation)', cost: 300, paid: 100, tooth: 46 }
const carie = { procedureTypeId: CARIE, name: 'Soin de carie / obturation', cost: 90, paid: 90, tooth: 36 }

// P1 — Hamdane's case: 300 paid 100, continued with a priced reste of 200.
fx.p1 = { id: await patient('Hamdane') }
fx.p1.f1 = await fiche(fx.p1.id, canal)
const d1 = await continueAct(fx.p1.f1, 200)
fx.p1.devis = { id: d1.id, number: d1.number, items: d1.items.map(i => ({ id: i.id, name: i.designationFr, cost: i.plannedCost, steps: (i.steps ?? []).map(s => ({ id: s.id, label: s.label, done: s.doneDate })) })) }

// P1 séance 2 booked on the « Séance suivante » act (for B3 — the second séance stays priced by the devis).
const next = fx.p1.devis.items.find(i => i.name === 'Séance suivante')
const appt = await call('POST', '/appointments', {
  patientId: fx.p1.id, doctorId: DOCTOR, appointmentDateTime: '2026-11-20T10:00:00Z', durationMinutes: 30,
  allowOutsideWorkingHours: true, allowOverlap: true, treatmentPlanId: d1.id,
  procedures: [{ procedureTypeId: CANAL, treatmentPlanItemId: next.id, treatmentPlanItemStepId: next.steps[0]?.id ?? null }],
})
fx.p1.appt2 = appt.id

// P2 — same arrangement, corrected while keeping money (100 → 50).
fx.p2 = { id: await patient('Garde') }
fx.p2.f1 = await fiche(fx.p2.id, canal)
const d2 = await continueAct(fx.p2.f1, 200)
fx.p2.devis = { id: d2.id, number: d2.number }

// P3 — ordinary fiche, Payé 90 → 0.
fx.p3 = { id: await patient('Ordinaire') }
fx.p3.f1 = await fiche(fx.p3.id, carie)

// P4 — ordinary fiche, price and Payé lowered 90 → 80.
fx.p4 = { id: await patient('Prix') }
fx.p4.f1 = await fiche(fx.p4.id, carie)

// P5 — continuation WITHOUT a prix du reste (the note is attached to the devis).
fx.p5 = { id: await patient('Attache') }
fx.p5.f1 = await fiche(fx.p5.id, canal)
const d5 = await continueAct(fx.p5.f1, null)
fx.p5.devis = { id: d5.id, number: d5.number, linkedInvoiceNumber: d5.linkedInvoiceNumber }

fs.writeFileSync(OUT, JSON.stringify(fx, null, 2))
console.log(JSON.stringify(fx, null, 2))
