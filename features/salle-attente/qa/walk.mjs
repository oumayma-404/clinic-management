// QA walk — salle d'attente. ONE launch, every scenario of qa/plan.md. Run from the scratchpad that holds
// playwright-core:  node <this file>   (env PW=<path to playwright-core/index.js>)
import { pathToFileURL } from 'node:url'
import { execFileSync } from 'node:child_process'
import { mkdirSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

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
  const j = await r.json()
  return j.value.accessToken
}
async function api(token, p, { method = 'GET', json } = {}) {
  const r = await fetch(`${API}${p}`, { method, headers: { Authorization: `Bearer ${token}`, ...(json ? { 'Content-Type': 'application/json' } : {}) }, body: json ? JSON.stringify(json) : undefined })
  let body = null
  try { body = await r.json() } catch { }
  return { status: r.status, body }
}

// ── Arrange, through the product's API ─────────────────────────────────────────────────────────────────────
const deskToken = await apiLogin(DESK)
const chairToken = await apiLogin(CHAIR)
// Earlier runs leave their test visits on today's board; a stale queue makes « Patient suivant » call THEM first.
// Close them through the product (a lane move — no money, no clinical record), only those of a QA run.
{
  const board = (await api(deskToken, '/appointments/waiting-room')).body
  for (const v of [...(board?.waiting ?? []), ...(board?.inChair ?? [])]) {
    if (!/ Qa[0-9a-z]{4}[a-g]$/.test(v.patientName)) continue
    const r = await api(deskToken, `/appointments/${v.appointmentId}/waiting-room`, { method: 'POST', json: { lane: 'Done' } })
    console.log(`  (arrange) closed leftover ${v.patientName}: ${r.status}`)
  }
}
const P = {}
for (const [key, first, suffix] of [['p1', 'Amira', 'a'], ['p2', 'Karim', 'b'], ['p3', 'Leila', 'c'], ['p4', 'Youssef', 'd'], ['p5', 'Sonia', 'e'], ['p7', 'Hedi', 'g']]) {
  const last = `Qa${RUN}${suffix}`
  const r = await api(deskToken, '/patients', { method: 'POST', json: { firstName: first, lastName: last, gender: 'Female', allowDuplicate: true } })
  if (r.status >= 300) throw new Error(`arrange patient ${key}: ${r.status} ${JSON.stringify(r.body)}`)
  P[key] = { id: r.body.id ?? r.body.value?.id, name: `${first} ${last}`, last }
}
P.p6 = { name: `Zaid Qa${RUN}f`, last: `Qa${RUN}f` }
const slot = minutesFromNow => { const d = new Date(Date.now() + minutesFromNow * 60000); d.setUTCSeconds(0, 0); d.setUTCMinutes(Math.round(d.getUTCMinutes() / 5) * 5); return d.toISOString() }
for (const [key, doctorId, at] of [['p1', NADIA, -10], ['p2', NADIA, 20], ['p3', NADIA, 40], ['p4', SALMA, 25]]) {
  const r = await api(deskToken, '/appointments', { method: 'POST', json: { patientId: P[key].id, doctorId, appointmentDateTime: slot(at), durationMinutes: 30, allowOutsideWorkingHours: true, allowOverlap: true } })
  if (r.status >= 300) throw new Error(`arrange RDV ${key}: ${r.status} ${JSON.stringify(r.body)}`)
  P[key].appt = r.body.id ?? r.body.value?.id
}
console.log(`run ${RUN}:`, Object.fromEntries(Object.entries(P).map(([k, v]) => [k, v.name])))

// ── Browser ────────────────────────────────────────────────────────────────────────────────────────────────
const browser = await chromium.launch({ channel: 'chrome', headless: true })
async function contextFor(email) {
  const pending = await api(email === DESK ? deskToken : chairToken, '/notifications/pending-reviews')
  const snooze = Object.fromEntries((Array.isArray(pending.body) ? pending.body : []).map(r => [r.id ?? r.reviewId, Date.now() + 864e5]))
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR', timezoneId: 'Africa/Tunis' })
  await ctx.addInitScript(s => { try { localStorage.setItem('clinic:pvr-snooze', s) } catch { } }, JSON.stringify(snooze))
  const page = await ctx.newPage()
  page.on('pageerror', e => console.log(`    [${email.split('@')[0]} pageerror] ${e.message.slice(0, 160)}`))
  await page.goto(`${WEB}/login`, { waitUntil: 'domcontentloaded' })
  await page.fill('input[type=email]', email)
  await page.fill('input[type=password]', PASSWORD)
  await page.click("button:has-text('Se connecter')")
  await page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 90000 })
  return { ctx, page }
}
const desk = await contextFor(DESK)
const chair = await contextFor(CHAIR)

async function closeStrays(page) {
  const later = page.locator('[role=dialog] button:has-text("Plus tard"):visible')
  if (await later.count()) { await later.first().click().catch(() => { }); await settle(400) }
}
async function shot(page, name) { await page.bringToFront(); await page.screenshot({ path: path.join(SHOTS, `${name}.png`), timeout: 20000 }).catch(e => console.log(`    shot ${name} failed: ${e.message}`)) }
const laneOf = (page, lane) => page.locator(`section[data-lane="${lane}"]`)
const cardIn = (page, lane, name) => laneOf(page, lane).locator('article', { hasText: name })
async function waitFor(fn, timeout = 20000, step = 500) {
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
const arrivalInput = page => page.getByPlaceholder('Qui arrive ? Nom ou téléphone')
async function typeArrival(page, text) {
  const box = arrivalInput(page)
  await box.fill('')
  await box.fill(text)
}
const toastWith = (page, re) => waitFor(async () => {
  const texts = await page.locator('[data-sonner-toast]').allInnerTexts()
  return texts.some(t => re.test(t))
}, 10000, 250)

const SCENARIOS = []
const S = (id, run) => SCENARIOS.push({ id, run })

// B9 first: the dentist's panel before anyone arrives.
S('B9', async () => {
  await openBoard(chair.page)
  const panel = chair.page.locator('section[aria-label="Mon fauteuil"]')
  if (!(await waitFor(async () => (await panel.count()) > 0, 20000))) return bad('B9', '« Mon fauteuil » panel absent for the dentist')
  const text = await panel.innerText()
  const idle = chair.page.locator('section[aria-label="Mon fauteuil"] button:has-text("Personne n\'attend")')
  if ((await idle.count()) && (await idle.first().isDisabled())) ok('B9', `idle panel: « Personne n'attend » disabled · « ${text.replace(/\s+/g, ' ').slice(0, 90)} »`)
  else bad('B9', 'idle panel does not show a disabled « Personne n\'attend »', text.replace(/\s+/g, ' ').slice(0, 160))
  await shot(chair.page, 'B9-chair-idle-1440')
})

S('A1', async () => {
  await openBoard(desk.page)
  const ok4 = await waitFor(async () => {
    for (const k of ['p1', 'p2', 'p3', 'p4']) if (!(await cardIn(desk.page, 'Upcoming', P[k].name).count())) return false
    return true
  }, 30000)
  const headings = (await desk.page.locator('section[data-lane] h2').allInnerTexts()).map(t => t.replace(/\s*\d+\s*$/, '').trim())
  const late = await cardIn(desk.page, 'Upcoming', P.p1.name).innerText().catch(() => '')
  if (ok4 && headings.join('|') === "À venir|En salle d'attente|Au fauteuil|Terminé" && /en retard/.test(late)) ok('A1', `lanes ${headings.join(' · ')}; P1 « ${late.replace(/\s+/g, ' ').slice(0, 80)} »`)
  else bad('A1', 'board does not show the 4 lanes with P1–P4 upcoming and P1 late', `headings=${headings} p1="${late.replace(/\s+/g, ' ')}" allFour=${ok4}`)
  await shot(desk.page, 'A1-desk-1440')
})

S('B5', async () => {
  const n = await desk.page.locator('section[aria-label="Mon fauteuil"]').count()
  n === 0 ? ok('B5', 'no « Mon fauteuil » for the secretary') : bad('B5', 'secretary sees the dentist panel')
})

S('A2', async () => {
  await typeArrival(desk.page, P.p1.name)
  const row = desk.page.locator(`button[aria-label="Arrivé : ${P.p1.name}"]`)
  if (!(await waitFor(async () => (await row.count()) > 0, 10000))) return bad('A2', 'RDV row not offered for P1 in « Qui arrive ? »')
  await row.first().click()
  const toast = await toastWith(desk.page, new RegExp(`${P.p1.name}.*En salle d'attente`))
  const moved = await inLane(desk.page, 'Waiting', P.p1.name, 10000)
  if (toast && moved) ok('A2', 'P1 → En salle d\'attente, toast shown')
  else bad('A2', 'arrival from the search did not land', `toast=${toast} moved=${moved}`)
  const undo = await desk.page.locator('[data-sonner-toast] button:has-text("Annuler")').count()
  undo ? ok('A2b', 'toast carries « Annuler »') : bad('A2b', 'toast has no « Annuler »')
})

S('A3', async () => {
  const seen = await waitFor(async () => {
    const t = await chair.page.locator('[data-staff-chat-notice]').allInnerTexts()
    return t.some(x => /arriv/i.test(x))
  }, 20000)
  const texts = await chair.page.locator('[data-staff-chat-notice]').allInnerTexts()
  seen ? ok('A3', `dentist notified: « ${texts.join(' / ').replace(/\s+/g, ' ').slice(0, 100)} »`) : bad('A3', 'no arrival notice reached the dentist', texts.join(' / '))
  await shot(chair.page, 'A3-chair-arrival-notice')
})

S('A4', async () => {
  await typeArrival(desk.page, P.p5.last)
  const row = desk.page.locator(`button[aria-label="Sans RDV : ${P.p5.name}"]`)
  if (!(await waitFor(async () => (await row.count()) > 0, 15000))) return bad('A4', '« Sans RDV » row not offered for P5')
  await row.first().click()
  const chip = desk.page.locator('[role=group][aria-label="Choisir le praticien"] button:has-text("Dr Nadia Trabelsi")')
  if (!(await waitFor(async () => (await chip.count()) > 0, 5000))) return bad('A4', 'group practice: no dentist chips after picking a walk-in')
  await shot(desk.page, 'A4-desk-dentist-chips')
  await chip.first().click()
  const moved = await inLane(desk.page, 'Waiting', P.p5.name, 10000)
  const tag = moved ? await cardIn(desk.page, 'Waiting', P.p5.name).innerText() : ''
  moved && /Sans RDV/.test(tag) ? ok('A4', 'walk-in P5 waiting, tagged « Sans RDV »') : bad('A4', 'walk-in did not land as « Sans RDV »', tag)
})

S('A4b', async () => {
  const seen = await waitFor(async () => {
    const t = await chair.page.locator('[data-staff-chat-notice]').allInnerTexts()
    return t.some(x => /Patient arrivé · Sans RDV/.test(x))
  }, 20000)
  const texts = await chair.page.locator('[data-staff-chat-notice]').allInnerTexts()
  seen ? ok('A4b', 'walk-in notice « Patient arrivé · Sans RDV »') : bad('A4b', 'walk-in notice does not say « Sans RDV »', texts.join(' / ').replace(/\s+/g, ' '))
})

S('A5', async () => {
  const row = sql(`select "IsWalkIn","ArrivedAtUtc" is not null,"DoctorId","BookedWithOverlap" from "Appointments" where "PatientId"='${P.p5.id}'`)
  row === `t|t|${NADIA}|t` ? ok('A5', `sql ${row}`) : bad('A5', 'walk-in row not as expected', row)
})

S('B3', async () => {
  const menu = desk.page.locator(`button[aria-label="Plus d'actions pour ${P.p5.name}"]`)
  await menu.first().click()
  await settle(400)
  const items = await desk.page.locator('[role=menuitem]').allInnerTexts()
  await desk.page.keyboard.press('Escape')
  !items.some(i => /Annuler l'arrivée/.test(i)) && items.some(i => /Parti sans être vu/.test(i))
    ? ok('B3', `walk-in menu: ${items.join(' · ')}`) : bad('B3', 'walk-in menu wrong', items.join(' · '))
})

S('B4', async () => {
  await typeArrival(desk.page, P.p5.last)
  const row = desk.page.locator(`button[aria-label="Sans RDV : ${P.p5.name}"]`)
  const found = await waitFor(async () => (await row.count()) > 0, 10000)
  const text = found ? await row.first().innerText() : ''
  found && (await row.first().isDisabled()) && /Déjà dans la salle d'attente/.test(text)
    ? ok('B4', 'already-here row disabled') : bad('B4', 'already-here row not disabled', text)
  await arrivalInput(desk.page).fill('')
})

S('A6', async () => {
  await typeArrival(desk.page, P.p6.name)
  const create = desk.page.locator('button', { hasText: `Nouveau patient « ${P.p6.name} »` })
  const createAny = desk.page.locator('button', { hasText: 'Nouveau patient' })
  if (!(await waitFor(async () => (await createAny.count()) > 0, 15000))) return bad('A6', '« Nouveau patient » not offered')
  await (await create.count() ? create : createAny).first().click()
  const submit = desk.page.locator('form button[type=submit]', { hasText: /^Créer/ })
  if (!(await waitFor(async () => (await submit.count()) > 0, 5000))) return bad('A6', 'new-patient form did not open')
  const first = await desk.page.locator('form input').nth(0).inputValue()
  const last = await desk.page.locator('form input').nth(1).inputValue()
  await shot(desk.page, 'A6-desk-new-patient')
  await submit.first().click()
  const chip = desk.page.locator('[role=group][aria-label="Choisir le praticien"] button:has-text("Dr Nadia Trabelsi")')
  if (!(await waitFor(async () => (await chip.count()) > 0, 10000))) return bad('A6', 'no dentist chips after creating', `first=${first} last=${last}`)
  await chip.first().click()
  const moved = await inLane(desk.page, 'Waiting', P.p6.name, 10000)
  const count = sql(`select count(*) from "Patients" where "LastName"='${P.p6.last}'`)
  P.p6.id = sql(`select "Id" from "Patients" where "LastName"='${P.p6.last}' limit 1`)
  moved && count === '1' ? ok('A6', `new patient waiting (prefill ${first} / ${last}; 1 row)`) : bad('A6', 'new walk-in not as expected', `moved=${moved} rows=${count}`)
})

S('A7', async () => {
  const card = cardIn(desk.page, 'Upcoming', P.p2.name).first()
  const name = card.locator('span.truncate.text-sm.font-semibold').first()
  const from = await name.boundingBox()
  const to = await laneOf(desk.page, 'Waiting').boundingBox()
  if (!from || !to) return bad('A7', 'no geometry for the drag')
  await desk.page.mouse.move(from.x + 10, from.y + 5)
  await desk.page.mouse.down()
  await desk.page.mouse.move(from.x + 30, from.y + 20, { steps: 4 })
  await desk.page.mouse.move(to.x + to.width / 2, to.y + 60, { steps: 12 })
  await shot(desk.page, 'A7-desk-dragging')
  await desk.page.mouse.up()
  const moved = await inLane(desk.page, 'Waiting', P.p2.name, 10000)
  moved ? ok('A7', 'drag À venir → En salle d\'attente moved P2') : bad('A7', 'drag did not move P2')
})

S('A8', async () => {
  const btn = chair.page.locator('section[aria-label="Mon fauteuil"] button[aria-label^="Patient suivant"]')
  const ready = await waitFor(async () => (await btn.count()) > 0 && /Amira/.test(await btn.first().getAttribute('aria-label')), 20000)
  const panel = await chair.page.locator('section[aria-label="Mon fauteuil"]').innerText()
  const queue = await chair.page.locator('section[aria-label="Mon fauteuil"] ol li').count()
  ready && /Fauteuil libre/.test(panel) && queue >= 3
    ? ok('A8', `next = P1, queue ${queue}`) : bad('A8', 'dentist panel not as expected', `ready=${ready} queue=${queue} « ${panel.replace(/\s+/g, ' ').slice(0, 200)} »`)
  await shot(chair.page, 'A8-chair-queue-1440')
})

S('A9', async () => {
  await closeStrays(chair.page)
  await chair.page.locator('section[aria-label="Mon fauteuil"] button[aria-label^="Patient suivant"]').first().click()
  const toast = await toastWith(chair.page, /est appelé — la réception est prévenue/)
  const inChair = await waitFor(async () => /Amira/.test(await chair.page.locator('section[aria-label="Mon fauteuil"] .bg-success-wash').innerText()), 10000)
  toast && inChair ? ok('A9', 'P1 called, panel shows P1 au fauteuil') : bad('A9', 'call did not land', `toast=${toast} inChair=${inChair}`)
})

S('A10', async () => {
  const notice = await waitFor(async () => {
    const t = await desk.page.locator('[data-staff-chat-notice]').allInnerTexts()
    return t.some(x => /Faites entrer le patient suivant/.test(x) && x.includes(P.p1.name) && /Voir la salle d'attente/.test(x))
  }, 20000)
  const banner = await waitFor(async () => /faites entrer/i.test(await desk.page.locator('main').innerText()) && (await desk.page.locator('main [role=status]', { hasText: 'faites entrer' }).innerText()).includes(P.p1.name), 15000)
  const texts = await desk.page.locator('[data-staff-chat-notice]').allInnerTexts()
  notice ? ok('A10', `desk rung: « ${texts.join(' / ').replace(/\s+/g, ' ').slice(0, 120)} »`) : bad('A10', 'desk not rung with the patient name', texts.join(' / ').replace(/\s+/g, ' '))
  banner ? ok('A10b', 'board banner « faites entrer P1 »') : bad('A10b', 'no call banner on the desk board')
  await shot(desk.page, 'A10-desk-rung-1440')
})

S('A11', async () => {
  await closeStrays(chair.page)
  await chair.page.locator('section[aria-label="Mon fauteuil"] button[aria-label^="Patient suivant"]').first().click()
  const done = await inLane(chair.page, 'Done', P.p1.name, 15000)
  const nextIn = await waitFor(async () => (await chair.page.locator('section[aria-label="Mon fauteuil"] .bg-success-wash').innerText()).includes(P.p5.name), 10000)
  done && nextIn ? ok('A11', 'P1 → Terminé, P5 au fauteuil') : bad('A11', 'second call did not end P1 / seat P5', `done=${done} nextIn=${nextIn}`)
})

S('A12', async () => {
  const row = sql(`select "Status","LeftChairAtUtc" is not null from "Appointments" where "Id"='${P.p1.appt}'`)
  row === '4|t' ? ok('A12', `P1 sql ${row}`) : bad('A12', 'P1 not Completed with LeftChairAtUtc', row)
})

S('A13', async () => {
  await desk.page.locator(`button[aria-label="${P.p3.name} est arrivé·e"]`).first().click()
  const toast = await toastWith(desk.page, new RegExp(`${P.p3.name}`))
  const undo = desk.page.locator('[data-sonner-toast]', { hasText: P.p3.name }).locator('button:has-text("Annuler")')
  if (!toast || !(await undo.count())) return bad('A13', 'no toast with « Annuler » after arrival of P3')
  await undo.first().click()
  const back = await inLane(desk.page, 'Upcoming', P.p3.name, 10000)
  await settle(800)
  const row = sql(`select "ArrivedAtUtc" is null from "Appointments" where "Id"='${P.p3.appt}'`)
  back && row === 't' ? ok('A13', 'undo put P3 back in « À venir », ArrivedAtUtc null') : bad('A13', 'undo did not restore P3', `back=${back} arrivedNull=${row}`)
})

S('D8', async () => {
  const row = sql(`select "Status","ArrivedAtUtc" is not null,"AppointmentDateTime" < now() from "Appointments" where "PatientId"='${P.p6.id}'`)
  row.startsWith('1|t|t') ? ok('D8', `waiting walk-in whose slot started stays Scheduled (${row})`) : bad('D8', 'waiting visit flipped by the job', row)
})

S('B1', async () => {
  await desk.page.locator(`button[aria-label="Plus d'actions pour ${P.p6.name}"]`).first().click()
  await desk.page.locator('[role=menuitem]', { hasText: 'Parti sans être vu' }).first().click()
  const left = await inLane(desk.page, 'Done', P.p6.name, 10000)
  const tag = left ? await cardIn(desk.page, 'Done', P.p6.name).innerText() : ''
  await settle(500)
  const row = sql(`select "Status","CancellationReason" from "Appointments" where "PatientId"='${P.p6.id}'`)
  left && /Parti sans être vu/.test(tag) && row === '5|Parti sans être vu' ? ok('B1', `left: ${row}`) : bad('B1', '« Parti sans être vu » wrong', `left=${left} tag=${tag} sql=${row}`)
})

S('B2', async () => {
  await desk.page.locator(`button[aria-label="Plus d'actions pour ${P.p6.name}"]`).first().click()
  await desk.page.locator('[role=menuitem]', { hasText: "Remettre en salle d'attente" }).first().click()
  const back = await inLane(desk.page, 'Waiting', P.p6.name, 10000)
  await settle(500)
  const row = sql(`select "Status",coalesce("CancellationReason",'∅') from "Appointments" where "PatientId"='${P.p6.id}'`)
  back && row === '1|∅' ? ok('B2', `restored: ${row}`) : bad('B2', 'restore from « Parti » wrong', `back=${back} sql=${row}`)
})

S('A14', async () => {
  await chair.page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' })
  const link = chair.page.locator('header a[aria-label^="Salle d\'attente :"]')
  const call = chair.page.locator('header button[aria-label^="Patient suivant"]')
  const seen = await waitFor(async () => (await link.count()) > 0 && (await call.count()) > 0, 30000)
  seen ? ok('A14', `header pill: « ${await link.first().getAttribute('aria-label')} » + Suivant`) : bad('A14', 'no « Patient suivant » pill in the header')
  await shot(chair.page, 'A14-chair-header-pill')
  await openBoard(chair.page)
})

S('A15', async () => {
  const btn = desk.page.locator(`button[aria-label="Terminer la visite de ${P.p5.name}"]`)
  if (!(await waitFor(async () => (await btn.count()) > 0, 10000))) return bad('A15', 'no « Terminer » on P5 au fauteuil')
  await btn.first().click()
  const done = await inLane(desk.page, 'Done', P.p5.name, 10000)
  const text = done ? await cardIn(desk.page, 'Done', P.p5.name).innerText() : ''
  done && /\d\d:\d\d – \d\d:\d\d/.test(text) ? ok('A15', `P5 terminé « ${text.replace(/\s+/g, ' ').slice(0, 70)} »`) : bad('A15', '« Terminer » wrong', text)
})

S('B6', async () => {
  const r = await api(chairToken, '/appointments/waiting-room/call-next', { method: 'POST', json: { expectedNextId: P.p1.appt } })
  r.status === 409 && JSON.stringify(r.body).includes('waiting-room-already-called') ? ok('B6', '409 waiting-room-already-called') : bad('B6', 'stale call not refused as expected', `${r.status} ${JSON.stringify(r.body)}`)
})
S('B7', async () => {
  const r = await api(deskToken, '/appointments/waiting-room/walk-in', { method: 'POST', json: { patientId: P.p7.id, doctorId: null } })
  JSON.stringify(r.body).includes('waiting-room-needs-dentist') ? ok('B7', `${r.status} needs-dentist`) : bad('B7', 'walk-in without dentist not refused', `${r.status} ${JSON.stringify(r.body)}`)
})
S('B8', async () => {
  const id = sql(`select "Id" from "Appointments" where "PatientId"='${P.p5.id}' limit 1`)
  const r = await api(deskToken, `/appointments/${id}/waiting-room`, { method: 'POST', json: { lane: 'Upcoming' } })
  JSON.stringify(r.body).includes('waiting-room-walk-in-has-no-slot') ? ok('B8', `${r.status} walk-in-has-no-slot`) : bad('B8', 'walk-in to Upcoming not refused', `${r.status} ${JSON.stringify(r.body)}`)
})

// ── Regression sweep ─────────────────────────────────────────────────────────────────────────────────────
S('D6', async () => {
  const link = desk.page.locator('nav a[href="/salle-attente"]:visible')
  ;(await link.count()) ? ok('D6', 'rail lists « Salle d\'attente »') : bad('D6', 'no « Salle d\'attente » in the rail')
})
S('D1', async () => {
  await desk.page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' })
  const seen = await waitFor(async () => (await desk.page.locator('main').innerText()).includes(P.p2.last), 40000)
  seen ? ok('D1', 'agenda shows P2') : bad('D1', 'agenda does not show today\'s P2', (await desk.page.locator('main').innerText()).slice(0, 200))
  await shot(desk.page, 'D1-agenda')
})
S('D2', async () => {
  await desk.page.goto(`${WEB}/a-cloturer`, { waitUntil: 'domcontentloaded' })
  const seen = await waitFor(async () => (await desk.page.locator('h1').allInnerTexts()).some(t => /À clôturer/.test(t)), 40000)
  const alert = await desk.page.locator('main [role=alert]').count()
  seen && !alert ? ok('D2', '« À clôturer » renders') : bad('D2', '« À clôturer » broken', `heading=${seen} alerts=${alert}`)
})
S('D3', async () => {
  await desk.page.goto(`${WEB}/waiting-list`, { waitUntil: 'domcontentloaded' })
  const seen = await waitFor(async () => (await desk.page.locator('h1').allInnerTexts()).some(t => /Liste d'attente/.test(t)), 40000)
  seen ? ok('D3', '/waiting-list heading « Liste d\'attente »') : bad('D3', 'waitlist heading wrong', (await desk.page.locator('h1').allInnerTexts()).join('|'))
})
S('D4', async () => {
  await desk.page.goto(`${WEB}/`, { waitUntil: 'domcontentloaded' })
  const moved = await waitFor(async () => desk.page.url().includes('/salle-attente'), 30000)
  moved ? ok('D4', 'secretary / → /salle-attente') : bad('D4', 'secretary not redirected', desk.page.url())
})
S('D7', async () => {
  await chair.page.goto(`${WEB}/`, { waitUntil: 'domcontentloaded' })
  await settle(5000)
  const url = chair.page.url()
  new URL(url).pathname === '/' ? ok('D7', 'dentist stays on the dashboard') : bad('D7', 'dentist redirected away from /', url)
  await openBoard(chair.page)
})

// ── Widths ───────────────────────────────────────────────────────────────────────────────────────────────
S('C1', async () => {
  await desk.page.setViewportSize({ width: 320, height: 640 })
  await openBoard(desk.page)
  await settle(1500)
  const m = await desk.page.evaluate(() => {
    const main = document.querySelector('main')
    const lanes = [...document.querySelectorAll('section[data-lane]')].map(s => Math.round(s.getBoundingClientRect().left))
    const input = document.querySelector('input[placeholder^="Qui arrive"]')?.getBoundingClientRect()
    return { over: main.scrollWidth - main.clientWidth, lanes, inputRight: input ? Math.round(input.right) : null }
  })
  const oneCol = new Set(m.lanes).size === 1
  m.over <= 1 && oneCol && m.inputRight !== null && m.inputRight <= 320 ? ok('C1', `320: overflow ${m.over}, one column, input right ${m.inputRight}`) : bad('C1', '320 px layout wrong', JSON.stringify(m))
  await shot(desk.page, 'C1-desk-320')
  await desk.page.evaluate(() => window.scrollTo(0, 0))
})
S('D5', async () => {
  await desk.page.setViewportSize({ width: 390, height: 844 })
  await settle(800)
  const tabs = await desk.page.locator('nav[aria-label="Navigation rapide"] a').evaluateAll(as => as.map(a => `${a.getAttribute('href')}=${a.innerText.trim()}`))
  tabs[0] === '/salle-attente=Attente' && !tabs.some(t => /Accueil/.test(t)) ? ok('D5', tabs.join(' · ')) : bad('D5', 'phone bar wrong', tabs.join(' · '))
  await shot(desk.page, 'C2-desk-390')
})
S('C2', async () => {
  for (const [w, h] of [[820, 1180], [1180, 820], [1440, 900], [1536, 730]]) {
    await desk.page.setViewportSize({ width: w, height: h }); await settle(900); await shot(desk.page, `C2-desk-${w}x${h}`)
    const over = await desk.page.evaluate(() => { const m = document.querySelector('main'); return m.scrollWidth - m.clientWidth })
    if (over > 1) bad('C2', `desk ${w}: horizontal overflow ${over}`)
  }
  for (const [w, h] of [[390, 844], [820, 1180], [1440, 900]]) {
    await chair.page.setViewportSize({ width: w, height: h }); await settle(900); await shot(chair.page, `C2-chair-${w}x${h}`)
    const over = await chair.page.evaluate(() => { const m = document.querySelector('main'); return m.scrollWidth - m.clientWidth })
    if (over > 1) bad('C2', `chair ${w}: horizontal overflow ${over}`)
  }
  ok('C2', 'screenshots taken (read them)')
})
S('C3', async () => skip('C3', 'long-press touch drag needs a coarse pointer the dev browser cannot emulate reliably; buttons cover every move'))
S('C4', async () => skip('C4', 'tones at 20/40 min need a real wait or SQL backdating (arranging through SQL is banned)'))
S('D9', async () => skip('D9', 'fiche « Terminer la séance » writes a clinical record — unit-tested (FicheVisitCompletionTests)'))
S('D10', async () => skip('D10', 'a payment writes money — unit-tested (interceptor tests)'))

for (const s of SCENARIOS) {
  try { await s.run() } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]) }
}

console.log('\n── findings ──')
const real = findings.filter(f => !f.skip)
real.length ? real.forEach(f => console.log(JSON.stringify(f))) : console.log('ALL CLEAR (skips aside)')
findings.filter(f => f.skip).forEach(f => console.log(`skip ${f.id}: ${f.skip}`))
console.log(JSON.stringify({ RUN, P }))
await browser.close()
