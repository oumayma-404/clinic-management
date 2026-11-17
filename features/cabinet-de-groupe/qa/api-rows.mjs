// API rows + arrange for the cabinet-de-groupe QA walk. Each run arranges its OWN records (stamped), so a re-run
// never meets a practitioner or an account a previous run already linked.
import { totp } from './totp.mjs'
import { writeFileSync } from 'node:fs'
import { execSync } from 'node:child_process'
const API = 'http://localhost:5099/api'
const CLINIC = 'f64a8a75-94e1-4e08-a743-fa231abe438f'
const SALMA_DOC = '9ed3d280-e3a1-4f11-ad19-8c993dd0da93', NADIA_DOC = '9ed3d280-e3a1-4f11-ad19-8c993dd0da94'
const SALMA_USER = 'local|cdc1c7a5-9326-4892-a049-598cb07eea6b', NADIA_USER = 'local|11111111-1111-4111-8111-111111111111'
const PATIENT = 'ceafa5f9-0218-4e20-b7b7-a7d47cef8aa6'
const OLD_UNASSIGNED = 'fef1bf18-6b4a-4c63-852a-3e5762ee285e'
const STAMP = Date.now() % 100000
const sql = q => execSync(`docker exec clinic-postgres psql -U clinic_user -d cdg_qa -At -F"|" -c "${q.replace(/"/g, '\\"')}"`).toString().trim()
const out = { stamp: STAMP }
const res = []
const log = (id, ok, m) => { res.push({ id, ok, m }); console.log(`${ok ? '✅' : '❌'} [${id}] ${m}`) }

await new Promise(r => setTimeout(r, (30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000))
const lres = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO') }) })
const lj = JSON.parse((await lres.text()) || '{}')
const token = lj.value?.accessToken ?? lj.accessToken ?? lj.token
if (!token) { console.log('login failed', lres.status, JSON.stringify(lj).slice(0, 300)); process.exit(1) }
const call = async (method, path, body) => {
  const r = await fetch(`${API}${path}`, { method, headers: { Authorization: `Bearer ${token}`, ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined })
  const raw = await r.text(); let j = null; try { j = JSON.parse(raw) } catch {}
  return { status: r.status, body: j?.value ?? j, raw }
}
// A re-run starts from two dentists again (run 3's last phase leaves the copy with one).
sql(`update "Doctors" set "IsActive"=true where "Id"='${NADIA_DOC}'`)

// The secretary signs in with no second factor; her token drives S3 and is the booker of record.
const SECRETARY_USER = 'local|22222222-2222-4222-8222-222222222222'
const sres = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'qa.secretary@ibnkhaldoun.test', password: 'QaAudit2026!y' }) })
const sj = JSON.parse((await sres.text()) || '{}')
const secToken = sj.value?.accessToken ?? sj.accessToken
if (!secToken) { console.log('secretary login failed', sres.status, JSON.stringify(sj).slice(0, 300)); process.exit(1) }
const callAs = async (tok, method, path, body) => {
  const r = await fetch(`${API}${path}`, { method, headers: { Authorization: `Bearer ${tok}`, ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined })
  const raw = await r.text(); let j = null; try { j = JSON.parse(raw) } catch {}
  return { status: r.status, body: j?.value ?? j, raw }
}

const d = new Date(); d.setUTCDate(d.getUTCDate() + 1); const day = d.toISOString().slice(0, 10)
const at = (h, m = 0) => `${day}T${String(h - 1).padStart(2, '0')}:${String(m).padStart(2, '0')}:00Z`
out.day = day

// A1
const a1 = await call('POST', '/appointments', { patientId: PATIENT, appointmentDateTime: at(15), durationMinutes: 30, allowOverlap: true, allowOutsideWorkingHours: true })
log('A1', a1.status === 400 && a1.body?.code === 'practitioner_required', `${a1.status} ${a1.raw.slice(0, 160)}`)

// Arrange: a cabinet-wide block, an RDV for Salma, an RDV for Nadia (booked by Salma → A12)
const block = await call('POST', '/appointments', { appointmentDateTime: at(12), durationMinutes: 60, notes: 'QA bloc cabinet', allowOverlap: true, allowOutsideWorkingHours: true })
const forSalma = await call('POST', '/appointments', { patientId: PATIENT, doctorId: SALMA_DOC, appointmentDateTime: at(9), durationMinutes: 30, allowOverlap: true, allowOutsideWorkingHours: true })
const forNadia = await call('POST', '/appointments', { patientId: PATIENT, doctorId: NADIA_DOC, appointmentDateTime: at(10), durationMinutes: 30, allowOverlap: true, allowOutsideWorkingHours: true })
out.block = block.body?.id; out.forSalma = forSalma.body?.id; out.forNadia = forNadia.body?.id
log('arrange', [block, forSalma, forNadia].every(x => x.status < 300), `block ${block.status} doc=${block.body?.doctorId ?? 'null'} · salma ${forSalma.status} · nadia ${forNadia.status}`)

// A12 — the post-visit review of Nadia's RDV booked by Salma
const a12 = sql(`select coalesce("TargetUserId",'NULL'), coalesce("AlsoTargetUserId",'NULL') from "StaffNotifications" where "AppointmentId"='${out.forNadia}' and "Category"=6`)
log('A12', a12 === `${NADIA_USER}|${SALMA_USER}`, `review row target|also = ${a12}`)

// B3 — a drag on an old RDV with no dentist (time only)
const b3 = await call('PUT', `/appointments/${OLD_UNASSIGNED}`, { appointmentDateTime: '2026-10-08T15:00:00Z', allowOverlap: true, allowOutsideWorkingHours: true })
log('B3', b3.status === 200 && b3.body?.doctorId == null, `${b3.status} doctorId=${b3.body?.doctorId ?? 'null'} ${b3.status !== 200 ? b3.raw.slice(0, 160) : ''}`)

// Arrange A10: an admin account with no practitioner (stamped name)
out.adminNoDocName = `QA Admin Sans Fiche ${STAMP}`
const adm = await call('POST', '/users', { email: `qa.admin.sansfiche.${STAMP}@ibnkhaldoun.test`, fullName: out.adminNoDocName, role: 'admin' })
log('arrange-A10', adm.status < 300, `${adm.status} ${adm.raw.slice(0, 100)}`)

// Arrange A9: a practitioner on the roster with no account (SQL, on the copy only)
out.unlinkedFirst = 'Rania'; out.unlinkedLast = `Ferchichi${STAMP}`; out.unlinkedEmail = `rania.${STAMP}@qa.test`
sql(`insert into "Doctors" ("Id","ClinicId","FirstName","LastName","Specialty","Email","CreatedAt","IsActive") values (gen_random_uuid(),'${CLINIC}','${out.unlinkedFirst}','${out.unlinkedLast}','General','${out.unlinkedEmail}', now(), true)`)

// B5 — Salma already has a record
const b5 = await call('PUT', `/users/${encodeURIComponent(SALMA_USER)}/practitioner`, { doctorInfo: { firstName: 'X', lastName: 'Y', specialty: 'General' } })
log('B5', b5.status === 400 && b5.body?.code === 'practitioner_already_linked', `${b5.status} ${b5.raw.slice(0, 160)}`)

// D4 — the strict filter for other readers; the widened one for the agenda (whole-day bounds)
const q = `startDate=${day}T00:00:00Z&endDate=${day}T23:59:59Z&doctorId=${NADIA_DOC}`
const strict = await call('GET', `/appointments?${q}`), wide = await call('GET', `/appointments?${q}&includeUnassigned=true`)
const ids = (wide.body ?? []).map(x => x.id)
log('D4', strict.status === 200 && (strict.body ?? []).every(x => x.doctorId === NADIA_DOC) && ids.includes(out.block) && ids.includes(out.forNadia) && !ids.includes(out.forSalma),
  `strict ${strict.body?.length} rows (all Nadia) · wide ${ids.length}: block ${ids.includes(out.block)}, Nadia's ${ids.includes(out.forNadia)}, Salma's ${ids.includes(out.forSalma)}`)

// S3 — the post-visit prompt reaches whoever booked. Two visits that ended an hour or more ago.
const ago = h => { const t = new Date(Date.now() - h * 3600e3); t.setUTCSeconds(0, 0); return t.toISOString() }
const salmasOwn = await call('POST', '/appointments', { patientId: PATIENT, doctorId: SALMA_DOC, appointmentDateTime: ago(3), durationMinutes: 30, allowOverlap: true, allowOutsideWorkingHours: true })
const secBooked = await callAs(secToken, 'POST', '/appointments', { patientId: PATIENT, doctorId: SALMA_DOC, appointmentDateTime: ago(2), durationMinutes: 30, allowOverlap: true, allowOutsideWorkingHours: true })
const secList = await callAs(secToken, 'GET', '/notifications/pending-reviews')
const salmaList = await call('GET', '/notifications/pending-reviews')
const apptIds = l => (Array.isArray(l.body) ? l.body : []).map(x => x.appointmentId)
const secSees = apptIds(secList), salmaSees = apptIds(salmaList)
log('S3', salmasOwn.status < 300 && secBooked.status < 300 && secSees.includes(secBooked.body?.id) && !secSees.includes(salmasOwn.body?.id)
    && salmaSees.includes(secBooked.body?.id) && salmaSees.includes(salmasOwn.body?.id),
  `booked ${salmasOwn.status}/${secBooked.status} · secretary sees hers ${secSees.includes(secBooked.body?.id)}, Salma's ${secSees.includes(salmasOwn.body?.id)} · Salma sees both ${salmaSees.includes(secBooked.body?.id) && salmaSees.includes(salmasOwn.body?.id)} ${secBooked.status >= 300 ? secBooked.raw.slice(0, 160) : ''}`)

// Arrange S4: a secretary whose e-mail matches a practitioner already on the roster with no account.
out.promoEmail = `promo.${STAMP}@qa.test`; out.promoName = `QA Promo ${STAMP}`; out.promoLast = `Promo${STAMP}`
const promo = await call('POST', '/users', { email: out.promoEmail, fullName: out.promoName, role: 'secretary' })
sql(`insert into "Doctors" ("Id","ClinicId","FirstName","LastName","Specialty","Email","CreatedAt","IsActive") values (gen_random_uuid(),'${CLINIC}','Yosra','${out.promoLast}','General','${out.promoEmail}', now(), true)`)
log('arrange-S4', promo.status < 300, `${promo.status} ${promo.raw.slice(0, 100)}`)

// Arrange S6: a waiting-list entry that prefers Nadia, on a patient of its own so the row is findable.
out.waitLast = `Attente${STAMP}`
const wp = await call('POST', '/patients', { firstName: 'QA', lastName: out.waitLast, phone: null })
const we = await call('POST', '/waiting-list', { patientId: wp.body?.id, preferredDoctorId: NADIA_DOC, priority: 'Normal' })
log('arrange-S6', wp.status < 300 && we.status < 300, `patient ${wp.status} · entry ${we.status} ${we.status >= 300 ? we.raw.slice(0, 160) : ''}`)

writeFileSync('arrange.json', JSON.stringify(out, null, 2))
writeFileSync('api-results.json', JSON.stringify(res, null, 2))
