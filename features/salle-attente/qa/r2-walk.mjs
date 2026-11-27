// QA walk — salle d'attente ROUND 2 (qa/r2-plan.md). ONE launch, every scenario.
// Run:  PW=<path to playwright-core/index.js> node r2-walk.mjs
import { pathToFileURL, fileURLToPath } from 'node:url'
import { execFileSync } from 'node:child_process'
import { mkdirSync } from 'node:fs'
import path from 'node:path'

const pw = await import(pathToFileURL(process.env.PW).href)
const chromium = pw.chromium ?? pw.default?.chromium

const WEB = 'http://localhost:3098'
const API = 'http://localhost:5098/api'
const DB = 'clinic_sa_qa'
const SHOTS = path.join(path.dirname(fileURLToPath(import.meta.url)), 'shots')
mkdirSync(SHOTS, { recursive: true })
const RUN = Date.now().toString(36).slice(-4)
const PASSWORD = 'QaAudit2026!y'
const DESK = 'qa.secretary@ibnkhaldoun.test'
const CHAIR = 'qa.doctor@ibnkhaldoun.test'
const NADIA = '9ed3d280-e3a1-4f11-ad19-8c993dd0da94'
const SALMA = '9ed3d280-e3a1-4f11-ad19-8c993dd0da93'

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, skip: why }) }
const settle = ms => new Promise(r => setTimeout(r, ms))
const sql = q => execFileSync('docker', ['exec', 'clinic-postgres', 'psql', '-U', 'clinic_user', '-d', DB, '-tAc', q]).toString().trim()

async function apiLogin(email) {
  const r = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password: PASSWORD }) })
  return (await r.json()).value.accessToken
}
async function api(token, p, { method = 'GET', json } = {}) {
  const r = await fetch(`${API}${p}`, { method, headers: { Authorization: `Bearer ${token}`, ...(json ? { 'Content-Type': 'application/json' } : {}) }, body: json ? JSON.stringify(json) : undefined })
  let body = null
  try { body = await r.json() } catch { }
  return { status: r.status, body }
}
const tunisDay = (offsetDays = 0) => {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Africa/Tunis' }).format(new Date())
  const [y, m, d] = today.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d + offsetDays)).toISOString().slice(0, 10)
}
// Late in the Tunisian day a « +130 min » slot is TOMORROW, and every today-only rule rightly refuses it (run 4 at
// 23:11 lost 10 rows to that). Squeeze the positive offsets into what is left of today, keeping their order, and
// round them UP so a « later » RDV never lands in the past.
const tunisMinutesNow = (() => {
  const [h, m] = new Intl.DateTimeFormat('en-GB', { timeZone: 'Africa/Tunis', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(new Date()).split(':').map(Number)
  return h * 60 + m
})()
const roomToday = 24 * 60 - tunisMinutesNow - 12   // the 5-minute rounding + the run's own length
const squeeze = m => (m <= 0 || roomToday >= 200) ? m : Math.max(1, Math.round(m * roomToday / 200))
if (roomToday < 200) console.log(`  (arrange) ${roomToday} min left today in Tunis: future slots squeezed ×${(roomToday / 200).toFixed(2)}`)
const slot = minutesFromNow => {
  const m = squeeze(minutesFromNow)
  const d = new Date(Date.now() + m * 60000); d.setUTCSeconds(0, 0)
  d.setUTCMinutes((m > 0 ? Math.ceil : Math.round)(d.getUTCMinutes() / 5) * 5)
  return d.toISOString()
}
const otherDaySlot = `${tunisDay(2)}T09:00:00.000Z`  // 10:00 in Tunis, two days on

// ── Arrange, through the product's API ─────────────────────────────────────────────────────────────────────
const deskToken = await apiLogin(DESK)
const chairToken = await apiLogin(CHAIR)
const move = (token, id, lane, doctorId = null) => api(token, `/appointments/${id}/waiting-room`, { method: 'POST', json: { lane, doctorId } })
const urgent = (id, on = true) => api(deskToken, `/appointments/${id}/waiting-room/urgent`, { method: 'POST', json: { urgent: on } })
const board = async (token = deskToken) => (await api(token, '/appointments/waiting-room')).body

// Earlier QA runs leave cards on today's board: close only ours, and put Nadia back on one chair.
{
  await api(chairToken, '/appointments/waiting-room/chairs', { method: 'PUT', json: { twoChairs: false } })
  const b = await board()
  for (const v of [...(b?.waiting ?? []), ...(b?.inChair ?? [])]) {
    if (!/ Qa[0-9a-z]{4}[a-z]$/.test(v.patientName)) continue
    const r = await move(deskToken, v.appointmentId, 'Done')
    console.log(`  (arrange) closed leftover ${v.patientName}: ${r.status}`)
  }
}

const P = {}
const KEYS = 'p1 p2 p3 p4 p5 p6 p7 p8 p9 p10 p11 p12 p13 p14 p15 p16 p17 p18 p19 p20 p21 p22 p23 p24 p25'.split(' ')
const FIRST = ['Amira', 'Karim', 'Leila', 'Youssef', 'Sonia', 'Hedi', 'Nabil', 'Rania', 'Fares', 'Ines', 'Omar', 'Salma', 'Tarek', 'Dora', 'Walid', 'Mouna', 'Sami', 'Hela', 'Bilel', 'Asma', 'Zied', 'Lotfi', 'Nesrine', 'Mehdi', 'Rim']
const phone6 = `9${String(Math.floor(Math.random() * 1e7)).padStart(7, '0')}`
for (const [i, key] of KEYS.entries()) {
  const last = `Qa${RUN}${String.fromCharCode(97 + i)}`
  const r = await api(deskToken, '/patients', {
    method: 'POST',
    json: { firstName: FIRST[i], lastName: last, gender: 'Female', allowDuplicate: true, ...(key === 'p6' ? { phoneNumber: phone6, phoneRegion: 'TN' } : {}) },
  })
  if (r.status >= 300) throw new Error(`arrange patient ${key}: ${r.status} ${JSON.stringify(r.body)}`)
  P[key] = { id: r.body.id ?? r.body.value?.id, name: `${FIRST[i]} ${last}` }
}
async function book(key, doctorId, when, field = 'appt') {
  const r = await api(deskToken, '/appointments', { method: 'POST', json: { patientId: P[key].id, doctorId, appointmentDateTime: when, durationMinutes: 30, allowOutsideWorkingHours: true, allowOverlap: true } })
  if (r.status >= 300) throw new Error(`arrange RDV ${key}: ${r.status} ${JSON.stringify(r.body)}`)
  P[key][field] = r.body.id ?? r.body.value?.id
}
await book('p1', NADIA, slot(-10)); await book('p2', NADIA, slot(20)); await book('p3', NADIA, slot(30))
await book('p4', NADIA, slot(35)); await book('p5', NADIA, otherDaySlot); await book('p6', NADIA, slot(50))
await book('p7', null, slot(15)); await book('p8', SALMA, slot(20)); await book('p9', NADIA, slot(-60))
await book('p10', NADIA, slot(40)); await book('p11', NADIA, slot(-30)); await book('p12', NADIA, slot(60))
await book('p13', NADIA, slot(65)); await book('p14', NADIA, slot(70)); await book('p14', NADIA, slot(200), 'appt2')
await book('p15', NADIA, slot(80)); await book('p16', NADIA, slot(85)); await book('p17', NADIA, slot(90))
await book('p18', NADIA, slot(95)); await book('p19', NADIA, slot(100)); await book('p20', NADIA, slot(110))
await book('p21', NADIA, slot(-45)); await book('p22', NADIA, slot(130)); await book('p23', NADIA, slot(135))
await book('p24', NADIA, slot(-20)); await book('p25', NADIA, slot(-25))
const p21CreatedAt = Date.now()
console.log(`run ${RUN}: p1=${P.p1.name} … p21=${P.p21.name}; p6 phone ${phone6}`)

// Start, length and planned time of a RDV — the three facts « Terminé » rewrites and « Annuler » / reopening put back.
const timeRow = id => {
  const [status, start, minutes, planned] = sql(`SELECT "Status", to_char("AppointmentDateTime" AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI'), "Duration" / 600000000, coalesce(to_char("PlannedStartUtc" AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI'), '') FROM "Appointments" WHERE "Id"='${id}'`).split('|')
  return { status, start, minutes: Number(minutes), planned }
}
const apptRow = id => sql(`SELECT "Status", "ArrivedAtUtc" IS NULL, "CalledInAtUtc" IS NULL, "LeftChairAtUtc" IS NULL, to_char("AppointmentDateTime" AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI'), to_char("PlannedStartUtc" AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI'), "DoctorId" FROM "Appointments" WHERE "Id"='${id}'`).split('|')

// ── API / SQL rows, before the browser ─────────────────────────────────────────────────────────────────────
{ // B1 — one live card per patient
  await move(deskToken, P.p14.appt, 'Waiting')
  const r = await move(deskToken, P.p14.appt2, 'Waiting')
  r.status === 409 && r.body?.code === 'waiting-room-already-here' ? ok('B1', `second RDV refused 409 ${r.body.code}`) : bad('B1', 'second arrival not refused', `${r.status} ${JSON.stringify(r.body)}`)
}
{ // B10 — « Pas là » on a waiting patient
  const r = await api(deskToken, `/appointments/${P.p14.appt}/waiting-room/not-here`, { method: 'POST', json: {} })
  r.status === 409 && r.body?.code === 'waiting-room-not-in-chair' ? ok('B10', '409 waiting-room-not-in-chair') : bad('B10', 'not-here on a waiting patient', `${r.status} ${JSON.stringify(r.body)}`)
}
{ // B4 — moved to tomorrow, presence cleared ; D2 — same day keeps it
  await move(deskToken, P.p15.appt, 'Waiting')
  const tomorrow = new Date(Date.parse(slot(80)) + 864e5).toISOString()
  const r = await api(deskToken, `/appointments/${P.p15.appt}`, { method: 'PUT', json: { appointmentDateTime: tomorrow, allowOutsideWorkingHours: true, allowOverlap: true } })
  const row = apptRow(P.p15.appt)
  r.status === 200 && r.body?.waitingRoom?.lane === 'Upcoming' && row[1] === 't'
    ? ok('B4', `moved to tomorrow → lane Upcoming, arrival cleared`)
    : bad('B4', 'presence survived a move to another day', `${r.status} lane=${r.body?.waitingRoom?.lane} arrivalNull=${row[1]}`)
  await move(deskToken, P.p16.appt, 'Waiting')
  const r2 = await api(deskToken, `/appointments/${P.p16.appt}`, { method: 'PUT', json: { appointmentDateTime: slot(120), allowOutsideWorkingHours: true, allowOverlap: true } })
  r2.status === 200 && r2.body?.waitingRoom?.lane === 'Waiting' ? ok('D2', 'same-day move keeps « En salle d\'attente »') : bad('D2', 'same-day move', `${r2.status} lane=${r2.body?.waitingRoom?.lane}`)
}
{ // B5 — agenda cancel of an arrived patient leaves the board ; B6 — « Absent » on a waiting one reads « Parti »
  await move(deskToken, P.p17.appt, 'Waiting')
  const c = await api(deskToken, `/appointments/${P.p17.appt}`, { method: 'PUT', json: { status: 'Cancelled', cancellationReason: 'QA' } })
  await move(deskToken, P.p18.appt, 'Waiting')
  const n = await api(deskToken, `/appointments/${P.p18.appt}`, { method: 'PUT', json: { status: 'NoShow' } })
  const b = await board()
  const p17 = b.done.find(v => v.appointmentId === P.p17.appt)
  const p18 = b.done.find(v => v.appointmentId === P.p18.appt)
  c.status === 200 && !p17 ? ok('B5', 'agenda cancel → off the board') : bad('B5', 'agenda cancel still on the board', `${c.status} lane=${p17?.lane}`)
  n.status === 200 && p18?.lane === 'Left' && p18?.leftAtUtc ? ok('B6', `« Absent » on a waiting patient → Left at ${p18.leftAtUtc}`) : bad('B6', '« Absent » on a waiting patient', `${n.status} ${JSON.stringify(p18)}`)
}
{ // B7 — « À venir » → « Terminé » invents nothing
  const before = apptRow(P.p19.appt)
  await move(deskToken, P.p19.appt, 'Done')
  const row = apptRow(P.p19.appt)
  row[0] === '4' && row[1] === 't' && row[2] === 't' && row[3] === 't' && row[4] === before[4] && row[5] === ''
    ? ok('B7', `Completed, no times, RDV ${row[4]} unchanged`)
    : bad('B7', 'C8: times invented or RDV moved', row.join('|'))
}
{ // B9 — a secretary cannot set a dentist's chairs
  const r = await api(deskToken, '/appointments/waiting-room/chairs', { method: 'PUT', json: { doctorId: NADIA, twoChairs: true } })
  r.status === 403 && r.body?.code === 'waiting-room-not-your-chairs' ? ok('B9', '403 waiting-room-not-your-chairs') : bad('B9', 'secretary set the chairs', `${r.status} ${JSON.stringify(r.body)}`)
}
{ // D1 — an ordinary agenda cancellation
  const r = await api(deskToken, `/appointments/${P.p20.appt}`, { method: 'PUT', json: { status: 'Cancelled', cancellationReason: 'QA' } })
  r.status === 200 && r.body?.status === 'Cancelled' ? ok('D1', 'agenda cancel 200') : bad('D1', 'agenda cancel', `${r.status} ${JSON.stringify(r.body)?.slice(0, 200)}`)
}

// ── Browser ────────────────────────────────────────────────────────────────────────────────────────────────
const browser = await chromium.launch({ channel: 'chrome', headless: true })
const pageErrors = []
async function contextFor(email, token) {
  const pending = await api(token, '/notifications/pending-reviews')
  const snooze = Object.fromEntries((Array.isArray(pending.body) ? pending.body : []).map(r => [r.id ?? r.reviewId, Date.now() + 864e5]))
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR', timezoneId: 'Africa/Tunis' })
  await ctx.addInitScript(s => { try { localStorage.setItem('clinic:pvr-snooze', s) } catch { } }, JSON.stringify(snooze))
  const page = await ctx.newPage()
  page.on('pageerror', e => { pageErrors.push(`${email.split('@')[0]}: ${e.message.slice(0, 160)}`); console.log(`    [${email.split('@')[0]} pageerror] ${e.message.slice(0, 160)}`) })
  await page.goto(`${WEB}/login`, { waitUntil: 'domcontentloaded' })
  await page.fill('input[type=email]', email)
  await page.fill('input[type=password]', PASSWORD)
  await page.click("button:has-text('Se connecter')")
  await page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 90000 })
  return { ctx, page }
}
const desk = await contextFor(DESK, deskToken)
const chair = await contextFor(CHAIR, chairToken)

async function closeStrays(page) {
  const later = page.locator('[role=dialog] button:has-text("Plus tard"):visible')
  if (await later.count()) { await later.first().click().catch(() => { }); await settle(400) }
}
async function shot(page, name) { await page.bringToFront(); await page.screenshot({ path: path.join(SHOTS, `r2-${name}.png`), timeout: 20000 }).catch(e => console.log(`    shot ${name} failed: ${e.message}`)) }
const laneOf = (page, lane) => page.locator(`section[data-lane="${lane}"]`)
const cardIn = (page, lane, name) => laneOf(page, lane).locator('article', { hasText: name })
async function waitFor(fn, timeout = 20000, step = 400) {
  const end = Date.now() + timeout
  while (Date.now() < end) { try { if (await fn()) return true } catch { } await settle(step) }
  return false
}
const inLane = (page, lane, name, timeout) => waitFor(async () => (await cardIn(page, lane, name).count()) > 0, timeout)
async function openBoard(page) {
  await page.goto(`${WEB}/salle-attente`, { waitUntil: 'domcontentloaded' })
  await laneOf(page, 'Upcoming').waitFor({ timeout: 90000 })
  await closeStrays(page)
}
const panelOf = page => page.locator('section[aria-label="Mon fauteuil"]')
const toastWith = (page, re, timeout = 10000) => waitFor(async () => (await page.locator('[data-sonner-toast]').allInnerTexts()).some(t => re.test(t)), timeout, 200)
const toastTexts = async page => (await page.locator('[data-sonner-toast]').allInnerTexts()).map(t => t.replace(/\s+/g, ' ')).join(' / ')
const flat = t => t.replace(/\s+/g, ' ').trim()

const SCENARIOS = []
const S = (id, run) => SCENARIOS.push({ id, run })

// A1 · D8 — Q1: arrival → urgent → « Patient suivant » → desk « Terminer »; the RDV takes the real time.
S('A1', async () => {
  await openBoard(desk.page)
  await openBoard(chair.page)
  await desk.page.locator(`button[aria-label="${P.p1.name} est arrivé·e"]`).click()
  if (!(await inLane(desk.page, 'Waiting', P.p1.name))) return bad('A1', 'p1 never reached « En salle d\'attente »')
  await urgent(P.p1.appt)
  const next = panelOf(chair.page).locator('button[aria-label^="Patient suivant"]')
  if (!(await waitFor(async () => ((await next.getAttribute('aria-label')) ?? '').includes(P.p1.name), 20000))) return bad('A1', '« Patient suivant » does not name the urgent p1', await next.getAttribute('aria-label').catch(() => ''))
  await next.click()
  if (!(await inLane(desk.page, 'InChair', P.p1.name))) return bad('A1', 'p1 not « Au fauteuil » at the desk')
  // D8 — the desk is told whom to bring in.
  const banner = desk.page.locator('[role=status]', { hasText: 'faites entrer' }).filter({ hasText: P.p1.name })
  ;(await waitFor(async () => (await banner.count()) > 0, 10000)) ? ok('D8', 'desk banner « faites entrer » names p1') : bad('D8', 'no « faites entrer » banner at the desk')
  await shot(desk.page, 'D8-desk-banner-1440')
  await desk.page.locator(`button[aria-label="Terminer la visite de ${P.p1.name}"]`).click()
  if (!(await inLane(desk.page, 'Done', P.p1.name))) return bad('A1', 'p1 not « Terminé »')
  const card = flat(await cardIn(desk.page, 'Done', P.p1.name).innerText())
  const row = apptRow(P.p1.appt)
  const booked = new Date(slot(-10)).toISOString().slice(0, 16)
  if (/\d\d:\d\d – \d\d:\d\d/.test(card) && row[5] && row[5] !== row[4]) ok('A1', `card « ${card.slice(0, 80)} » · RDV ${row[4]} (prévu ${row[5]})`)
  else if (/\d\d:\d\d – \d\d:\d\d/.test(card) && row[5] && row[5] === row[4]) ok('A1', `RDV ${row[4]} = booked ${row[5]} (call fell in the booked minute) · card « ${card.slice(0, 60)} »`)
  else bad('A1', 'real time not recorded', `card « ${card} » row ${row.join('|')} booked≈${booked}`)
})

// A3 — Q2: the stat is the server's figure and does not tick.
S('A3', async () => {
  const b = await board()
  const stat = desk.page.locator('text=Attente moyenne').locator('xpath=../..')
  const first = flat(await stat.innerText())
  if (b.averageWaitMinutes == null) return bad('A3', 'API average is null after a call-in', JSON.stringify(b.averageWaitMinutes))
  const expect = b.averageWaitMinutes < 60 ? `${b.averageWaitMinutes} min` : null
  if (expect && !first.includes(expect)) return bad('A3', `stat « ${first} » ≠ API ${b.averageWaitMinutes} min`)
  await settle(35000)
  const later = flat(await stat.innerText())
  later === first ? ok('A3', `« ${first} » = API, unchanged after 35 s`) : bad('A3', 'average moved with the clock', `« ${first} » → « ${later} »`)
})

// G3a — Q1 undo: « Annuler » on « Terminer » puts the booked time back (and the patient back in the chair).
S('G3a', async () => {
  const booked = timeRow(P.p24.appt)
  await move(deskToken, P.p24.appt, 'Waiting'); await move(deskToken, P.p24.appt, 'InChair')
  if (!(await inLane(desk.page, 'InChair', P.p24.name))) return bad('G3a', 'p24 not « Au fauteuil » at the desk')
  await desk.page.locator(`button[aria-label="Terminer la visite de ${P.p24.name}"]`).click()
  if (!(await inLane(desk.page, 'Done', P.p24.name))) return bad('G3a', 'p24 not « Terminé »')
  const done = timeRow(P.p24.appt)
  const settled = done.start !== booked.start && done.planned === booked.start && done.minutes !== booked.minutes
  let undo = desk.page.locator('[data-sonner-toast]', { hasText: P.p24.name }).locator('button', { hasText: 'Annuler' })
  if (!(await waitFor(async () => (await undo.count()) > 0, 4000))) undo = desk.page.locator('[data-sonner-toast] button', { hasText: 'Annuler' })
  if (!(await waitFor(async () => (await undo.count()) > 0, 4000))) return bad('G3a', 'no « Annuler » on the « Terminé » toast', await toastTexts(desk.page))
  await undo.last().click()
  const seatedAgain = await inLane(desk.page, 'InChair', P.p24.name, 10000)
  const back = await waitFor(async () => { const r = timeRow(P.p24.appt); return r.start === booked.start && r.minutes === booked.minutes && r.planned === '' && r.status === '3' }, 10000)
  const after = timeRow(P.p24.appt)
  settled && seatedAgain && back
    ? ok('G3a', `Terminé → RDV ${done.start} · ${done.minutes} min (prévu ${done.planned}); « Annuler » → ${after.start} · ${after.minutes} min, au fauteuil`)
    : bad('G3a', '« Annuler » after « Terminé »', `settled=${settled} seatedAgain=${seatedAgain} booked=${JSON.stringify(booked)} done=${JSON.stringify(done)} after=${JSON.stringify(after)}`)
  await move(deskToken, P.p24.appt, 'Done')
})

// G3b — Q1 reopen: « Remettre au fauteuil » on a finished card puts the booked time back.
S('G3b', async () => {
  const booked = timeRow(P.p25.appt)
  await move(deskToken, P.p25.appt, 'Waiting'); await move(deskToken, P.p25.appt, 'InChair'); await move(deskToken, P.p25.appt, 'Done')
  if (!(await inLane(desk.page, 'Done', P.p25.name))) return bad('G3b', 'p25 not « Terminé » at the desk')
  const done = timeRow(P.p25.appt)
  const settled = done.start !== booked.start && done.planned === booked.start
  await desk.page.locator(`button[aria-label="Plus d'actions pour ${P.p25.name}"]`).click()
  await desk.page.locator('[role=menuitem]', { hasText: 'Remettre au fauteuil' }).click()
  const seated = await inLane(desk.page, 'InChair', P.p25.name, 10000)
  const back = await waitFor(async () => { const r = timeRow(P.p25.appt); return r.start === booked.start && r.minutes === booked.minutes && r.planned === '' && r.status === '3' }, 10000)
  const after = timeRow(P.p25.appt)
  settled && seated && back
    ? ok('G3b', `Terminé → RDV ${done.start} (prévu ${done.planned}); « Remettre au fauteuil » → ${after.start} · ${after.minutes} min, au fauteuil`)
    : bad('G3b', 'reopen after « Terminé »', `settled=${settled} seated=${seated} booked=${JSON.stringify(booked)} done=${JSON.stringify(done)} after=${JSON.stringify(after)}`)
  await move(deskToken, P.p25.appt, 'Done')
})

// A6 — E1: the dentist calls p4 (urgent), the desk answers « Pas là », p2 (next urgent) is seated.
S('A6', async () => {
  await move(deskToken, P.p4.appt, 'Waiting'); await move(deskToken, P.p2.appt, 'Waiting')
  await urgent(P.p4.appt); await settle(300); await urgent(P.p2.appt)
  const next = panelOf(chair.page).locator('button[aria-label^="Patient suivant"]')
  if (!(await waitFor(async () => ((await next.getAttribute('aria-label')) ?? '').includes(P.p4.name), 20000))) return bad('A6', '« Patient suivant » does not name p4')
  await next.click()
  const notHere = desk.page.locator(`button[aria-label="${P.p4.name} n'est pas là"]`)
  if (!(await waitFor(async () => (await notHere.count()) > 0, 15000))) return bad('A6', 'no « Pas là » on the desk banner for p4')
  await shot(desk.page, 'A6-banner-pas-la-1440')
  await notHere.first().click()
  const back = await inLane(desk.page, 'Waiting', P.p4.name)
  const nextSeated = await inLane(desk.page, 'InChair', P.p2.name)
  const tag = back ? flat(await cardIn(desk.page, 'Waiting', P.p4.name).innerText()) : ''
  const toast = await toastWith(desk.page, /n'est pas là/)
  back && nextSeated && /Pas là · \d\d:\d\d/.test(tag) && toast
    ? ok('A6', `p4 back waiting « ${tag.slice(0, 70)} », p2 seated, toast « ${(await toastTexts(desk.page)).slice(0, 80)} »`)
    : bad('A6', '« Pas là » outcome', `back=${back} p2Seated=${nextSeated} tag=« ${tag} » toast=${toast}`)
})

// A4 · A5 · B8 · C5 — Q3: « 2 fauteuils ».
S('A4', async () => {
  const sw = panelOf(chair.page).getByRole('switch')
  await sw.click()
  if (!(await waitFor(async () => (await panelOf(chair.page).innerText()).includes('Mes 2 fauteuils'), 15000))) return bad('A4', 'switch did not turn « 2 fauteuils » on')
  await move(deskToken, P.p3.appt, 'Waiting')
  const call = panelOf(chair.page).locator(`button[aria-label="Appeler ${P.p3.name} avant les autres"]`)
  if (!(await waitFor(async () => (await call.count()) > 0, 15000))) return bad('A4', 'no « Appeler » for p3 in the dentist queue')
  await call.click()
  const ends2 = await waitFor(async () => (await panelOf(chair.page).locator('button[aria-label^="Terminer la séance de"]').count()) === 2, 15000)
  const labels = await panelOf(chair.page).locator('button[aria-label^="Terminer la séance de"]').evaluateAll(b => b.map(x => x.getAttribute('aria-label')).join(' / '))
  const both = ends2 && labels.includes(P.p2.name) && labels.includes(P.p3.name)
  const ends = ends2 ? 2 : 1
  both && ends === 2 ? ok('A4', 'p2 and p3 both seated, two « Terminer »') : bad('A4', 'two chairs', `both=${both} terminer=${ends}`)
  // A5 — p4 (urgent, « pas là ») still waits: the button says both chairs are taken.
  const full = panelOf(chair.page).locator('button:has-text("Les deux fauteuils sont occupés")')
  ;(await waitFor(async () => (await full.count()) > 0 && (await full.isDisabled()), 10000)) ? ok('A5', '« Les deux fauteuils sont occupés » disabled') : bad('A5', 'no full-chairs state', flat(await panelOf(chair.page).innerText()).slice(0, 200))
  const r = await move(deskToken, P.p4.appt, 'InChair')
  r.status === 409 && r.body?.code === 'waiting-room-chairs-full' ? ok('B8', '409 chairs-full') : bad('B8', 'third seat', `${r.status} ${JSON.stringify(r.body)}`)
  await shot(chair.page, 'A4-two-chairs-1440')
  await chair.page.setViewportSize({ width: 390, height: 844 })
  await settle(600)
  const ov = await chair.page.evaluate(() => { const m = document.querySelector('main'); return m ? m.scrollWidth - m.clientWidth : -1 })
  await panelOf(chair.page).scrollIntoViewIfNeeded()
  await shot(chair.page, 'C5-two-chairs-390')
  ov <= 0 ? ok('C5', `panel at 390: no sideways scroll (${ov})`) : bad('C5', `panel at 390 overflows ${ov}px`)
  await chair.page.setViewportSize({ width: 1440, height: 900 })
  // Back to one chair, chairs emptied.
  await move(deskToken, P.p2.appt, 'Done'); await move(deskToken, P.p3.appt, 'Done'); await move(deskToken, P.p4.appt, 'Done')
  // A4b — the switch turns « 2 fauteuils » back off. Run 3: this click timed out after the 390 → 1440 resize and
  // left Nadia on two chairs (A6b / A7 cascaded). Now: say what covers the switch, then reset through the API.
  await chair.page.bringToFront(); await settle(600)
  let offErr = ''
  try { await sw.scrollIntoViewIfNeeded({ timeout: 5000 }); await sw.click({ timeout: 8000 }) } catch (e) {
    offErr = e.message.split('\n')[0]
    const cover = await sw.evaluate(el => {
      const r = el.getBoundingClientRect()
      const hit = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)
      const what = hit ? `${hit.tagName}.${String(hit.className).slice(0, 80)} « ${(hit.textContent ?? '').trim().slice(0, 60)} »` : 'none'
      return `switch ${Math.round(r.x)},${Math.round(r.y)} ${Math.round(r.width)}x${Math.round(r.height)} · hit ${what} · inViewport=${r.bottom <= innerHeight && r.top >= 0}`
    }).catch(x => `evaluate failed: ${x.message.split('\n')[0]}`)
    offErr += ` · ${cover}`
  }
  const off = !offErr && await waitFor(async () => !(await panelOf(chair.page).innerText()).includes('Mes 2 fauteuils'), 10000)
  if (off) ok('A4b', 'the switch turns « 2 fauteuils » back off')
  else {
    bad('A4b', 'the switch did not turn « 2 fauteuils » off', offErr)
    await shot(chair.page, 'A4b-switch-off-1440')
    await api(chairToken, '/appointments/waiting-room/chairs', { method: 'PUT', json: { twoChairs: false } })
    await openBoard(chair.page)
  }
})

// A6b — F1: « Pas là » on the staff-chat call-in notice, where the desk answers the ring.
S('A6b', async () => {
  await move(deskToken, P.p22.appt, 'Waiting'); await move(deskToken, P.p23.appt, 'Waiting')
  await urgent(P.p22.appt); await settle(300); await urgent(P.p23.appt)
  const next = panelOf(chair.page).locator('button[aria-label^="Patient suivant"]')
  if (!(await waitFor(async () => ((await next.getAttribute('aria-label')) ?? '').includes(P.p22.name), 20000))) return bad('A6b', '« Patient suivant » does not name p22')
  await next.click()
  const notice = desk.page.locator('[data-staff-chat-notice]', { hasText: P.p22.name })
  const press = notice.locator(`button[aria-label="${P.p22.name} n'est pas là"]`)
  if (!(await waitFor(async () => (await press.count()) > 0, 20000))) return bad('A6b', 'no « Pas là » on the call-in notice for p22')
  await shot(desk.page, 'A6b-notice-pas-la-1440')
  await press.click()
  const back = await inLane(desk.page, 'Waiting', P.p22.name)
  const nextSeated = await inLane(desk.page, 'InChair', P.p23.name)
  const toast = await toastWith(desk.page, /n'est pas là · .+ est appelé/)
  const noticeGone = await waitFor(async () => (await notice.count()) === 0, 8000)
  back && nextSeated && toast && noticeGone
    ? ok('A6b', `notice « Pas là »: p22 back waiting, p23 seated, notice answered, toast « ${(await toastTexts(desk.page)).slice(0, 80)} »`)
    : bad('A6b', 'notice « Pas là »', `back=${back} p23Seated=${nextSeated} toast=${toast} noticeGone=${noticeGone}`)
  await move(deskToken, P.p22.appt, 'Done'); await move(deskToken, P.p23.appt, 'Done')
})

// A11 — C7: « Faire entrer » an unassigned patient asks « Quel praticien ? ».
S('A11', async () => {
  await move(deskToken, P.p7.appt, 'Waiting')
  if (!(await inLane(desk.page, 'Waiting', P.p7.name))) return bad('A11', 'p7 not waiting')
  await desk.page.locator(`button[aria-label="Faire entrer ${P.p7.name}"]`).click()
  const dialog = desk.page.locator('[role=dialog]', { hasText: 'Quel praticien ?' })
  if (!(await waitFor(async () => (await dialog.count()) > 0, 8000))) return bad('A11', 'no « Quel praticien ? » dialog')
  await shot(desk.page, 'A11-quel-praticien-1440')
  await dialog.locator('button', { hasText: 'Dr Nadia Trabelsi' }).click()
  const seated = await inLane(desk.page, 'InChair', P.p7.name)
  const row = apptRow(P.p7.appt)
  seated && row[6] === NADIA ? ok('A11', 'p7 seated with Dr Nadia (DoctorId set)') : bad('A11', 'seat after pick', `seated=${seated} doctor=${row[6]}`)
  await move(deskToken, P.p7.appt, 'Done')
})

// A12 · C4 — C10: a congé for Dr Salma; her waiting patient says so and changes dentist.
S('A12', async () => {
  const leave = await api(deskToken, `/doctors/${SALMA}/leaves`, { method: 'POST', json: { doctorId: SALMA, startDate: tunisDay(0), endDate: tunisDay(0), note: `QA ${RUN}` } })
  const leaveId = leave.body?.leave?.id ?? leave.body?.value?.leave?.id
  if (leave.status >= 300 || !leaveId) return bad('A12', 'could not arrange the congé', `${leave.status} ${JSON.stringify(leave.body)?.slice(0, 200)}`)
  try {
    await move(deskToken, P.p8.appt, 'Waiting')
    await desk.page.reload({ waitUntil: 'domcontentloaded' }); await laneOf(desk.page, 'Waiting').waitFor()
    const card = () => cardIn(desk.page, 'Waiting', P.p8.name)
    if (!(await waitFor(async () => /absent aujourd'hui/.test(await card().innerText()), 15000))) return bad('A12', 'no « absent aujourd\'hui » on p8\'s card', flat(await card().innerText().catch(() => '')))
    ok('A12', `card: « ${flat(await card().innerText()).slice(0, 110)} »`)
    await card().locator('button', { hasText: 'Changer de praticien' }).click()
    const dialog = desk.page.locator('[role=dialog]', { hasText: 'Changer de praticien' })
    await dialog.waitFor({ timeout: 8000 })
    const salmaOffered = await dialog.locator('button', { hasText: 'Salma' }).count()
    salmaOffered === 0 ? ok('A12', 'Dr Salma (absent, current) not offered') : bad('A12', 'the absent dentist is offered')
    // C4 — the picker at 390 and on the owner's 1536×730.
    for (const [w, h] of [[390, 844], [1536, 730]]) {
      await desk.page.setViewportSize({ width: w, height: h }); await settle(500)
      const cancel = dialog.locator('button', { hasText: 'Annuler' })
      const box = await cancel.boundingBox()
      await shot(desk.page, `C4-picker-${w}x${h}`)
      box && box.y + box.height <= h && box.x >= 0 ? ok('C4', `picker at ${w}×${h}: « Annuler » on screen`) : bad('C4', `picker at ${w}×${h}: « Annuler » off screen`, JSON.stringify(box))
    }
    await desk.page.setViewportSize({ width: 1440, height: 900 }); await settle(400)
    await dialog.locator('button', { hasText: 'Dr Nadia Trabelsi' }).click()
    const gone = await waitFor(async () => !/absent aujourd'hui/.test(await card().innerText()), 15000)
    const row = apptRow(P.p8.appt)
    gone && row[6] === NADIA ? ok('A12', 'changed to Dr Nadia, warning gone') : bad('A12', 'change of dentist', `gone=${gone} doctor=${row[6]}`)
  } finally {
    await api(deskToken, `/doctors/${SALMA}/leaves/${leaveId}`, { method: 'DELETE' })
  }
})

// A10 — C5: today's RDV found by phone.
S('A10', async () => {
  const box = desk.page.getByPlaceholder('Qui arrive ? Nom ou téléphone')
  await box.fill(phone6.slice(-5))
  const todayRow = desk.page.locator(`button[aria-label="Arrivé : ${P.p6.name}"]`)
  const walkRow = desk.page.locator(`button[aria-label="Sans RDV : ${P.p6.name}"]`)
  const found = await waitFor(async () => (await todayRow.count()) > 0, 12000)
  await settle(800)
  const asWalkIn = await walkRow.count()
  await shot(desk.page, 'A10-phone-search-1440')
  if (!found || asWalkIn) return bad('A10', 'phone search', `today=${found} asWalkIn=${asWalkIn}`)
  ok('A10', 'p6 found by phone under « Rendez-vous d\'aujourd\'hui », not « Sans RDV »')
  await todayRow.click()
  await inLane(desk.page, 'Waiting', P.p6.name)
})

// A7 — E2: « Passer en premier » from the card menu, and back.
S('A7', async () => {
  const menu = () => desk.page.locator(`button[aria-label="Plus d'actions pour ${P.p6.name}"]`)
  await menu().click()
  await desk.page.locator('[role=menuitem]', { hasText: 'Passer en premier' }).click()
  const first = async () => flat(await laneOf(desk.page, 'Waiting').locator('article').first().innerText())
  const top = await waitFor(async () => (await first()).includes(P.p6.name) && (await first()).includes('Urgent'), 12000)
  top ? ok('A7', `p6 first, « ${(await first()).slice(0, 70)} »`) : bad('A7', 'urgent not first', await first())
  await shot(desk.page, 'A7-urgent-1440')
  await menu().click()
  await desk.page.locator('[role=menuitem]', { hasText: 'Retirer « Urgent »' }).click()
  const off = await waitFor(async () => !(await cardIn(desk.page, 'Waiting', P.p6.name).innerText()).includes('Urgent'), 12000)
  off ? ok('A7', '« Retirer « Urgent » » removes the mark') : bad('A7', 'urgent mark stayed')
})

// A16 — E5: an early patient stays grey; a late-slot one turns amber (arrival backdated by SQL on our own rows).
S('A16', async () => {
  await move(deskToken, P.p10.appt, 'Waiting'); await move(deskToken, P.p11.appt, 'Waiting')
  sql(`UPDATE "Appointments" SET "ArrivedAtUtc" = now() - interval '25 minutes' WHERE "Id" IN ('${P.p10.appt}','${P.p11.appt}')`)
  console.log('    (arrange) backdated p10/p11 arrival by 25 min — SQL on this run\'s own test rows')
  await desk.page.reload({ waitUntil: 'domcontentloaded' }); await laneOf(desk.page, 'Waiting').waitFor()
  await inLane(desk.page, 'Waiting', P.p11.name)
  const pill = name => cardIn(desk.page, 'Waiting', name).locator('span[aria-label^="Attend depuis"]')
  const early = await pill(P.p10.name).getAttribute('class')
  const late = await pill(P.p11.name).getAttribute('class')
  const t10 = flat(await pill(P.p10.name).innerText())
  ;/bg-muted/.test(early) && /bg-warning-wash/.test(late)
    ? ok('A16', `early p10 « ${t10} » neutral, p11 amber`)
    : bad('A16', 'wait colours', `p10=${early} p11=${late}`)
})

// A8 · C3 · A9 — C6: « Le recevoir aujourd'hui », and its undo.
S('A8', async () => {
  const booked = apptRow(P.p5.appt)[4]
  const box = desk.page.getByPlaceholder('Qui arrive ? Nom ou téléphone')
  const receive = desk.page.locator(`button[aria-label="Le recevoir aujourd'hui : ${P.p5.name}"]`)
  await desk.page.setViewportSize({ width: 320, height: 720 }); await settle(500)
  await box.fill(P.p5.name.split(' ')[1])
  if (!(await waitFor(async () => (await receive.count()) > 0, 12000))) { await desk.page.setViewportSize({ width: 1440, height: 900 }); return bad('A8', 'no « Le recevoir aujourd\'hui » row for p5') }
  const shortVisible = await receive.locator('span.sm\\:hidden').isVisible()
  const sansRdv = await desk.page.locator(`button[aria-label="Sans RDV : ${P.p5.name}"]`).isVisible()
  const detail = flat(await receive.innerText())
  // Run-2 F3: the other day's date must be READABLE, not merely present in the DOM — nothing in the row clipped.
  // Inline spans report clientWidth 0 whatever they hold: only boxes that can clip are measured.
  const clipped = await receive.evaluate(b => [...b.querySelectorAll('span')].filter(s => getComputedStyle(s).display !== 'inline' && s.offsetParent).some(s => s.scrollWidth > s.clientWidth + 1))
  const dated = /RDV \S+ \d{1,2} \S+ \d\d:\d\d/.test(detail)
  await shot(desk.page, 'C3-other-day-row-320')
  shortVisible && sansRdv && !clipped && dated ? ok('C3', `320: « ${detail} », nothing clipped, « Sans RDV » beside it`) : bad('C3', '320 row', `short=${shortVisible} sansRdv=${sansRdv} clipped=${clipped} dated=${dated} « ${detail} »`)
  await desk.page.setViewportSize({ width: 1440, height: 900 }); await settle(400)
  const oneLine = await receive.evaluate(b => {
    const mids = [...b.children].map(k => { const r = k.getBoundingClientRect(); return Math.round(r.top + r.height / 2) })
    return Math.max(...mids) - Math.min(...mids) <= 4
  })
  await shot(desk.page, 'C3-other-day-row-1440')
  oneLine ? ok('C3', "1440: icon, text and « Le recevoir aujourd'hui » on one line") : bad('C3', '1440: the row wrapped on a desktop')
  await receive.click()
  const waiting = await inLane(desk.page, 'Waiting', P.p5.name)
  const moved = apptRow(P.p5.appt)[4]
  const today = tunisDay(0)
  waiting && moved.slice(0, 10) <= today && moved !== booked ? ok('A8', `p5 received today (${moved}, was ${booked})`) : bad('A8', 'receive today', `waiting=${waiting} ${booked}→${moved}`)
  const undo = desk.page.locator('[data-sonner-toast] button', { hasText: 'Annuler' })
  if (!(await waitFor(async () => (await undo.count()) > 0, 5000))) return bad('A9', 'no « Annuler » on the toast')
  await undo.last().click()
  const back = await waitFor(async () => apptRow(P.p5.appt)[4] === booked, 10000)
  const offBoard = await waitFor(async () => (await cardIn(desk.page, 'Waiting', P.p5.name).count()) === 0, 10000)
  back && offBoard ? ok('A9', `undo: RDV back on ${booked}, off today's board`) : bad('A9', 'undo of receive-today', `back=${back} offBoard=${offBoard} now ${apptRow(P.p5.appt)[4]}`)
})

// B2 · B3 — C9: « Annuler » after somebody moved the card, and with nothing moved.
S('B2', async () => {
  await desk.page.locator(`button[aria-label="${P.p12.name} est arrivé·e"]`).click()
  const undo = desk.page.locator('[data-sonner-toast]', { hasText: P.p12.name }).locator('button', { hasText: 'Annuler' })
  if (!(await waitFor(async () => (await undo.count()) > 0, 6000))) return bad('B2', 'no toast « Annuler » after « Arrivé »')
  await move(deskToken, P.p12.appt, 'Left')
  await undo.first().click()
  const refused = await toastWith(desk.page, /déjà été déplacé/)
  const b = await board()
  const p12 = b.done.find(v => v.appointmentId === P.p12.appt)
  refused && p12?.lane === 'Left' ? ok('B2', `refused: « ${(await toastTexts(desk.page)).slice(0, 90)} »; card stays « Parti »`) : bad('B2', 'undo overwrote a later move', `toast=${refused} lane=${p12?.lane}`)
})
S('B3', async () => {
  await desk.page.locator(`button[aria-label="${P.p13.name} est arrivé·e"]`).click()
  const undo = desk.page.locator('[data-sonner-toast]', { hasText: P.p13.name }).locator('button', { hasText: 'Annuler' })
  if (!(await waitFor(async () => (await undo.count()) > 0, 6000))) return bad('B3', 'no toast « Annuler »')
  await undo.first().click()
  ;(await inLane(desk.page, 'Upcoming', P.p13.name, 10000)) ? ok('B3', 'plain undo puts p13 back in « À venir »') : bad('B3', 'plain undo')
})

// A13 · A14 · A2 · D6 — the agenda: presence chips, live overrun, « prévu ».
S('A13', async () => {
  await move(deskToken, P.p9.appt, 'Waiting'); await move(deskToken, P.p9.appt, 'InChair')
  await desk.page.goto(`${WEB}/appointments?view=day`, { waitUntil: 'domcontentloaded' })
  const block = name => desk.page.locator(`[aria-label^="${name} ·"]:visible`).first()
  if (!(await waitFor(async () => (await block(P.p9.name).count()) > 0, 30000))) return bad('A13', 'agenda day view shows no block for p9')
  ok('D6', 'agenda day view renders blocks')
  const l9 = await block(P.p9.name).getAttribute('aria-label')
  const l6 = await block(P.p6.name).getAttribute('aria-label')
  const l1 = await block(P.p1.name).getAttribute('aria-label')
  ;/au fauteuil/.test(l9) && /en salle/.test(l6) ? ok('A13', `« ${l9.slice(0, 60)} » · « ${l6.slice(0, 60)} »`) : bad('A13', 'presence words', `p9 «${l9}» p6 «${l6}»`)
  ;/prévu \d\d:\d\d/.test(l1) ? ok('A2', `« ${l1.slice(0, 70)} »`) : bad('A2', 'no « prévu » on the finished p1', l1)
  const h9 = (await block(P.p9.name).boundingBox())?.height ?? 0
  const h6 = (await block(P.p6.name).boundingBox())?.height ?? 0
  h9 > h6 * 1.5 ? ok('A14', `overrun block ${Math.round(h9)}px vs 30-min ${Math.round(h6)}px`) : bad('A14', 'overrun not drawn', `p9 ${h9} p6 ${h6}`)
  // Run-2 F2: the overrun is laned BESIDE what it now overlaps — no other block's box intersects it.
  const hits = await block(P.p9.name).evaluate(self => {
    const box = self.getBoundingClientRect()
    return [...document.querySelectorAll('[aria-label*=" · "]')]
      .filter(el => el !== self && !self.contains(el) && !el.contains(self) && el.offsetParent)
      .map(el => el.getBoundingClientRect())
      .filter(r => r.width > 0 && r.left < box.right - 1 && r.right > box.left + 1 && r.top < box.bottom - 1 && r.bottom > box.top + 1).length
  })
  hits === 0 ? ok('A14', 'overrun shares its column side by side — no block painted over it') : bad('A14', `${hits} block(s) painted over the overrun`)
  await block(P.p9.name).scrollIntoViewIfNeeded()
  await shot(desk.page, 'A13-agenda-day-1440')
})

// A15 · D7 — the dentist's dashboard « Au fauteuil » reads the board.
S('A15', async () => {
  await chair.page.goto(`${WEB}/`, { waitUntil: 'domcontentloaded' })
  const card = chair.page.locator('a', { hasText: 'Au fauteuil' }).first()
  if (!(await waitFor(async () => (await card.count()) > 0, 30000))) return bad('A15', 'no « Au fauteuil » card on the dashboard')
  const text = flat(await card.innerText())
  const seated = (await board(chairToken)).inChair.map(v => v.patientName)
  seated.some(n => text.includes(n)) ? ok('A15', `« ${text.slice(0, 90)} »`) : bad('A15', 'card names nobody seated', `« ${text} » seated=${seated.join(', ')}`)
  await shot(chair.page, 'A15-dashboard-1440')
})

// D4 — the dentist's header pill.
S('D4', async () => {
  await chair.page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' })
  const pill = chair.page.locator('a[aria-label^="Salle d\'attente :"]').first()
  ;(await waitFor(async () => (await pill.count()) > 0, 30000)) ? ok('D4', `pill « ${await pill.getAttribute('aria-label')} »`) : bad('D4', 'no header pill for the dentist')
})

// C1 · C2 — the board at four widths.
S('C1', async () => {
  await openBoard(desk.page)
  for (const [w, h] of [[320, 720], [390, 844], [820, 1180], [1440, 900]]) {
    await desk.page.setViewportSize({ width: w, height: h }); await settle(700)
    const ov = await desk.page.evaluate(() => { const m = document.querySelector('main'); return m ? m.scrollWidth - m.clientWidth : -1 })
    await shot(desk.page, `C1-board-${w}`)
    ov <= 0 ? ok(w === 320 ? 'C1' : 'C2', `board at ${w}: no sideways scroll`) : bad(w === 320 ? 'C1' : 'C2', `board at ${w} overflows ${ov}px`)
  }
  await desk.page.setViewportSize({ width: 1440, height: 900 })
})

for (const s of SCENARIOS) {
  try { await s.run() } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]) }
}

// ── The job rows, last: a job minute has passed since p9 was seated and p21 was booked ──────────────────────
{
  const wait = Math.max(0, 75000 - (Date.now() - p21CreatedAt))
  if (wait) { console.log(`    waiting ${Math.round(wait / 1000)} s for the progress job`); await settle(wait) }
  const s9 = apptRow(P.p9.appt)[0]
  s9 === '3' ? ok('B11', 'seated overrun p9 still InProgress after a job minute') : bad('B11', 'the job moved a seated patient', `status ${s9}`)
  const s21 = apptRow(P.p21.appt)[0]
  s21 === '7' ? ok('D3', 'past RDV nobody marked → AwaitingClosure (7)') : bad('D3', 'job no longer closes a non-board RDV', `status ${s21}`)
}

pageErrors.length ? bad('PAGE', `${pageErrors.length} page error(s)`, pageErrors.join(' || ')) : ok('PAGE', 'no page errors')
await browser.close()
console.log('\n──────── RESULT')
const real = findings.filter(f => !f.skip)
findings.forEach(f => console.log(f))
console.log(real.length ? `RED (${real.length})` : 'ALL CLEAR')
