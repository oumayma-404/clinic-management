// Arrange the wave-3 (money) fixtures over the API on a fresh throwaway « QAG Flex » patient. Writes fixtures-3.json.
// Usage: node arrange-3.mjs <path-to-totp.mjs>
import { writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const { totp } = await import(pathToFileURL(process.argv[2]).href)
const API = 'http://localhost:5000/api'
const PT = { couronne: 'a976d389-7593-4ba9-b87a-6b8d3bf97572', detartrage: '8226ce23-e8e3-4b34-9f75-2069c3b92605' }

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
  if (!r.ok) throw new Error(`${method} ${path} → ${r.status} ${text.slice(0, 300)}`)
  return json?.value ?? json
}

const ts = Date.now().toString().slice(-6)
const isoDay = (offset) => { const d = new Date(); d.setDate(d.getDate() + offset); return d.toISOString().slice(0, 10) }
const P = await call('POST', '/patients', {
  firstName: 'QAG', lastName: `Flex ${ts}`, dateOfBirth: '1990-05-05', gender: 'Female',
  phoneNumber: `+2162${Math.floor(1000000 + Math.random() * 8999999)}`, allowDuplicate: true,
})
const devis = (title, cost, installments = []) => call('POST', '/treatment-plans', {
  patientId: P.id, title,
  items: [{ designationFr: 'Couronne', plannedCost: cost, procedureTypeId: PT.couronne, toothNumbers: [16] }],
  installments,
})
const pay = async (plan, amount) => {
  const inst = plan.installments[0]
  return call('POST', `/treatment-plans/${plan.id}/installments/${inst.id}/payments`, {
    amount, method: 'Cash', paidOn: `${isoDay(-3)}T00:00:00`, version: plan.version,
  })
}

const f = { ts, patient: P.id, patientName: `QAG Flex ${ts}` }

// G3a — fully paid 300; a 100 DT remise must ask « Rendre au patient ? ».
{ const p = await devis(`QA G3a ${ts}`, 300); await pay(p, 300); f.G3a = { plan: p.id, title: `QA G3a ${ts}` } }
// G3b — same shape, for « Retour » (nothing saved).
{ const p = await devis(`QA G3b ${ts}`, 300); await pay(p, 300); f.G3b = { plan: p.id, title: `QA G3b ${ts}` } }
// G3c — deposit only (100 of 300), nothing done; « Arrêter » must offer « Rendre et arrêter ».
{ const p = await devis(`QA G3c ${ts}`, 300); await pay(p, 100); f.G3c = { plan: p.id, title: `QA G3c ${ts}` } }
// G2 — agreed schedule 100 · 100 · 100; a 50 DT remise must keep the three dates.
{
  const p = await devis(`QA G2 ${ts}`, 300, [
    { dueDate: isoDay(30), amount: 100 }, { dueDate: isoDay(60), amount: 100 }, { dueDate: isoDay(90), amount: 100 },
  ])
  f.G2 = { plan: p.id, title: `QA G2 ${ts}`, dates: p.installments.map((i) => i.dueDate).sort() }
}
// G8 — 50 collected then written off.
{
  const p = await devis(`QA G8 ${ts}`, 300); const paid = await pay(p, 50)
  await call('POST', `/treatment-plans/${p.id}/write-off`, { reason: 'QA — patient parti', version: paid.version })
  f.G8 = { plan: p.id, title: `QA G8 ${ts}` }
}
// G1 — a 250 DT line to be retyped to 0 in the devis form.
{ const p = await devis(`QA G1 ${ts}`, 250); f.G1 = { plan: p.id, title: `QA G1 ${ts}`, item: p.items[0].id } }
// G6 — a 300 DT act with a 50 DT remise, to be booked from the workspace.
{
  const p = await devis(`QA G6 ${ts}`, 300)
  await call('PUT', `/treatment-plans/${p.id}/items/${p.items[0].id}/discount`, { discountAmount: 50, version: p.version })
  f.G6 = { plan: p.id, title: `QA G6 ${ts}` }
}

writeFileSync(join(here, 'fixtures-3.json'), JSON.stringify(f, null, 2))
console.log(JSON.stringify(f, null, 2))
