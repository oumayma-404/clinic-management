import { login } from './login.mjs'
const API = 'http://localhost:5099/api'
const SALMA = '9ed3d280-e3a1-4f11-ad19-8c993dd0da93', NADIA = '9ed3d280-e3a1-4f11-ad19-8c993dd0da94'
const PATIENT = '31c335d7-9d78-46fa-a19a-90a4c76e5735'
const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const { value } = await login()
const token = value.accessToken
console.log('token keys', Object.keys(value))
const call = async (method, path, body) => {
  const r = await fetch(API + path, { method, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined })
  let j = null; try { j = await r.json() } catch {}
  return { s: r.status, j }
}
const created = { leaves: [], appts: [] }
// setup: congé Mon 2 → Wed 4 Nov
const c = await call('POST', `/doctors/${SALMA}/leaves`, { startDate: '2026-11-02', endDate: '2026-11-04', note: 'API probe' })
console.log('setup create', c.s, JSON.stringify(c.j))
if (c.s >= 300) { console.log('cannot set up'); process.exit(1) }
const leaveId = c.j.leave.id; created.leaves.push(leaveId)
c.j.appointmentsDuring === 6 ? ok('A6-api', `appointmentsDuring=6, firstAppointmentDay=${c.j.firstAppointmentDay}`) : bad('A6-api', 'appointmentsDuring', JSON.stringify(c.j))
// B2 overlap
const b2 = await call('POST', `/doctors/${SALMA}/leaves`, { startDate: '2026-11-04', endDate: '2026-11-06' })
b2.s === 400 && /est déjà en congé/.test(b2.j?.error) ? ok('B2', `400 « ${b2.j.error} »`) : bad('B2', 'overlap', `${b2.s} ${JSON.stringify(b2.j)}`)
// B3 367 days
const b3 = await call('POST', `/doctors/${SALMA}/leaves`, { startDate: '2027-01-01', endDate: '2028-01-02' })
b3.s === 400 && /366/.test(b3.j?.error) ? ok('B3', `400 « ${b3.j.error} »`) : bad('B3', '367 days', `${b3.s} ${JSON.stringify(b3.j)}`)
// B3b 366 days accepted? (boundary) 2027-01-01..2028-01-01 = 366 days
const b3b = await call('POST', `/doctors/${NADIA}/leaves`, { startDate: '2027-01-01', endDate: '2028-01-01' })
b3b.s < 300 ? (ok('B3b', '366 days accepted'), created.leaves.push([NADIA, b3b.j.leave.id])) : bad('B3b', '366 refused', `${b3b.s} ${JSON.stringify(b3b.j)}`)
// B4 booking on congé day
const appt = (doctorId, iso, extra = {}) => ({ patientId: PATIENT, doctorId, appointmentDateTime: iso, durationMinutes: 30, ...extra })
const b4 = await call('POST', '/appointments', appt(SALMA, '2026-11-03T14:00:00Z'))
b4.s === 400 && b4.j?.code === 'outside_working_hours' && /est en congé/.test(b4.j?.error) ? ok('B4', `400 ${b4.j.code} « ${b4.j.error} »`) : bad('B4', 'congé booking', `${b4.s} ${JSON.stringify(b4.j)}`)
const b5 = await call('POST', '/appointments', appt(SALMA, '2026-11-03T14:00:00Z', { allowOutsideWorkingHours: true }))
b5.s < 300 ? (ok('B5', `${b5.s} booked ${b5.j?.id}`), created.appts.push(b5.j?.id)) : bad('B5', 'override', `${b5.s} ${JSON.stringify(b5.j)}`)
const b6 = await call('POST', '/appointments', appt(NADIA, '2026-11-03T14:30:00Z'))
b6.s < 300 ? (ok('B6', `${b6.s} Nadia booked`), created.appts.push(b6.j?.id)) : bad('B6', 'Nadia refused', `${b6.s} ${JSON.stringify(b6.j)}`)
// AC-6 no practitioner
const b6b = await call('POST', '/appointments', appt(null, '2026-11-03T15:00:00Z'))
b6b.s < 300 ? (ok('B6b', `${b6b.s} no-practitioner booked`), created.appts.push(b6b.j?.id)) : bad('B6b', 'no-practitioner refused', `${b6b.s} ${JSON.stringify(b6b.j)}`)
// D5 Salma Thu 5 Nov (outside congé) create then move
const d5c = await call('POST', '/appointments', appt(SALMA, '2026-11-05T14:00:00Z'))
if (d5c.s < 300) {
  created.appts.push(d5c.j.id)
  const d5 = await call('PUT', `/appointments/${d5c.j.id}`, { id: d5c.j.id, version: d5c.j.version, appointmentDateTime: '2026-11-05T14:30:00Z', durationMinutes: 30 })
  d5.s < 300 ? ok('D5', `${d5.s} moved within Thu`) : bad('D5', 'move refused', `${d5.s} ${JSON.stringify(d5.j)}`)
  // D5b move it INTO the congé → refused
  const v = (await call('GET', `/appointments/${d5c.j.id}`)).j?.version
  const d5b = await call('PUT', `/appointments/${d5c.j.id}`, { id: d5c.j.id, version: v, appointmentDateTime: '2026-11-04T14:30:00Z', durationMinutes: 30 })
  d5b.s === 400 && d5b.j?.code === 'outside_working_hours' ? ok('D5b', `move into congé refused « ${d5b.j.error} »`) : bad('D5b', 'move into congé', `${d5b.s} ${JSON.stringify(d5b.j)}`)
} else bad('D5', 'create Thu', `${d5c.s} ${JSON.stringify(d5c.j)}`)
// clinic read
const r = await call('GET', '/doctors/leaves?from=2026-11-01&to=2026-11-30')
r.s === 200 && r.j.some(l => l.id === leaveId && l.doctorName) ? ok('R1', `clinic read ${r.j.length} row(s), name « ${r.j[0].doctorName} »`) : bad('R1', 'clinic read', `${r.s} ${JSON.stringify(r.j)}`)
const r2 = await call('GET', '/doctors/leaves?from=2026-01-01&to=2027-06-01')
r2.s === 400 ? ok('R2', `window >400 days refused « ${r2.j?.error} »`) : bad('R2', 'window', `${r2.s}`)
// cleanup congés via API delete
const del = await call('DELETE', `/doctors/${SALMA}/leaves/${leaveId}`)
del.s < 300 ? ok('DEL', `${del.s} deleted`) : bad('DEL', 'delete', `${del.s} ${JSON.stringify(del.j)}`)
for (const x of created.leaves.filter(Array.isArray)) await call('DELETE', `/doctors/${x[0]}/leaves/${x[1]}`)
console.log('APPTS', JSON.stringify(created.appts))
findings.length ? findings.forEach(f => console.log(JSON.stringify(f))) : console.log('API ROWS ALL CLEAR')
