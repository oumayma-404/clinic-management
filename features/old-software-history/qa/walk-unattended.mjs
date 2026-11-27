// QA walk — the import runs on its own (qa/plan-unattended.md), in one launch, after walk.mjs on the same database.
// Local isolated stack only (API :5095, web :3095, DB clinic_pr123_qa). Fixes nothing: every failure is collected.
// Usage: node walk-unattended.mjs <scratchpadDir> <runLabel>
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'
import { cpSync, mkdirSync, readFileSync, readdirSync, rmSync } from 'node:fs'
import { connect } from 'node:net'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const SCRATCH = process.argv[2]
if (!SCRATCH) throw new Error('usage: node walk-unattended.mjs <scratchpadDir> <runLabel>')
const { chromium } = createRequire(join(SCRATCH, 'x.js'))('playwright-core')
const API = 'http://localhost:5095/api'
const WEB = 'http://localhost:3095'
const DATA = join(SCRATCH, 'pr123-data')
const S = JSON.parse(readFileSync(join(SCRATCH, 'pr123-state.json'), 'utf8'))
const API_LOG = join(SCRATCH, 'qa-api.log')
const CLINIC = S.clinicId
const SHOTS = fileURLToPath(new URL(`./shots/${process.argv[3] ?? 'run'}/`, import.meta.url))
mkdirSync(SHOTS, { recursive: true })

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }
const sleep = ms => new Promise(r => setTimeout(r, ms))
const sql = q => execFileSync('docker', ['exec', 'clinic-postgres', 'psql', '-U', 'clinic_user', '-d', 'clinic_pr123_qa', '-tAc', q]).toString().trim()
const q = s => `'${s.replace(/'/g, "''")}'`

const browser = await chromium.launch({ channel: 'chrome', headless: true })
async function session(who) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } })
  await ctx.route('**/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
  const page = await ctx.newPage()
  page.setDefaultTimeout(60_000)
  const seen = []
  const stamps = []
  const answers = { s401: 0, s429: 0, renewals: 0 }
  page.on('request', r => {
    if (r.url().startsWith(API)) { seen.push(r.url().slice(API.length)); stamps.push(Date.now()) }
    if (/\/bff\/auth\/token|\/auth\/refresh/.test(r.url())) answers.renewals++
  })
  page.on('response', r => {
    if (!r.url().startsWith(API)) return
    if (r.status() === 401) answers.s401++
    if (r.status() === 429) answers.s429++
  })
  await page.goto(`${WEB}/login`, { timeout: 180_000 })
  await page.fill('input[type=email]', who.email)
  await page.fill('input[type=password]', who.password)
  await page.click('button:has-text("Se connecter")')
  await page.waitForURL(u => !u.pathname.includes('/login'), { timeout: 120_000 })
  return { ctx, page, seen, stamps, answers }
}
const footer = page => page.locator('div.sticky p[role=status]').first()
async function pick(page, records, radios) {
  await page.goto(`${WEB}/import-ancien-logiciel`, { timeout: 180_000 })
  const inputs = page.locator('input[type=file][webkitdirectory]')
  await inputs.nth(0).waitFor({ state: 'attached' })
  await inputs.nth(0).setInputFiles(records)
  await inputs.nth(1).setInputFiles(radios)
  await page.getByRole('button', { name: 'Analyser' }).click()
  await page.getByText('Patients à créer', { exact: true }).waitFor({ timeout: 180_000 })
}
async function figure(page, label) {
  const li = page.locator('li', { has: page.getByText(label, { exact: true }) }).first()
  return (await li.locator('p').nth(1).evaluate(p => p.firstChild?.textContent ?? '')).trim()
}
/** Presses the run button, watches the footer until the end; returns everything the footer said. */
async function run(page, label, limitMs = 15 * 60_000) {
  await page.getByRole('button', { name: label, exact: true }).click()
  const started = Date.now()
  const said = new Set()
  for (;;) {
    await sleep(300)
    const text = await footer(page).innerText().catch(() => '')
    if (text) said.add(text.replace(/\d+ s\b/, 'N s').replace(/\d+ sur/, 'N sur'))
    const again = page.getByRole('button', { name: "Relancer l'import", exact: true })
    if ((await again.isVisible().catch(() => false)) && (await again.isEnabled().catch(() => false))) {
      // Read again once finished: the line read above may predate the last repaint (run 5 probe fix, E5).
      await sleep(700)
      const last = await footer(page).innerText().catch(() => text)
      return { text: last, said: [...said, last], seconds: Math.round((Date.now() - started) / 1000) }
    }
    if (Date.now() - started > limitMs) return { text: `TIMEOUT: ${text}`, said: [...said], seconds: Math.round(limitMs / 1000) }
  }
}
const pairsOf = dir => readdirSync(dir).filter(f => f.endsWith('.pdf')).map(f => {
  const words = f.replace(/^\d+-/, '').replace(/\.pdf$/, '').split(' ')
  return [words[0], words.slice(1).join(' ')]
})
/** Most API calls inside any 60 s window. */
const peak60 = ts => { let best = 0; for (let i = 0, j = 0; i < ts.length; i++) { while (ts[i] - ts[j] > 60_000) j++; best = Math.max(best, i - j + 1) } return best }
const inPairs = pairs => `("FirstName","LastName") in (${pairs.map(([f, l]) => `(${q(f)},${q(l)})`).join(',')})`

try {
  // ---- E1/E2/E3: batch E under injected connection faults --------------------------------------------------------
  const imp = await session(S.import)
  await pick(imp.page, join(DATA, 'E', 'dossiers-E'), join(DATA, 'E', 'radios-E'))
  const toCreate = await figure(imp.page, 'Patients à créer')
  const toSend = await figure(imp.page, 'Fichiers à envoyer')
  ;/^40\b/.test(toCreate) && /^120\b/.test(toSend)
    ? ok('E1-plan', `plan: ${toCreate} à créer · ${toSend} fichiers`)
    : bad('E1-plan', 'plan figures', `${toCreate} / ${toSend}`)

  // E2 — the first card's birth after its « Dossier créé le »: the server refuses that date.
  const card = imp.page.locator('li', { has: imp.page.getByLabel('Date de naissance') }).first()
  const e2First = await card.getByLabel('Prénom').inputValue()
  const e2Last = await card.getByLabel('Nom', { exact: true }).inputValue()
  await card.getByLabel('Date de naissance').fill('2023-06-01')
  await sleep(300)
  const warned = await card.innerText()
  ;/la date de création ira dans les notes/.test(warned)
    ? ok('E2-warn', `card warns before the run (${e2First} ${e2Last})`)
    : bad('E2-warn', 'no warning on the card', warned.slice(0, 200))

  const faults = { lostAnswer: 0, neverLeft: 0 }
  const counters = { create: 0, upload: 0, history: 0 }
  // A call that ARRIVES and whose answer is lost is the case that could create something twice.
  const loseAnswer = async route => { await route.fetch().catch(() => null); faults.lostAnswer++; await route.abort('failed') }
  const neverLeave = async route => { faults.neverLeft++; await route.abort('failed') }
  await imp.page.route(u => u.href === `${API}/patients`, async route => {
    if (route.request().method() !== 'POST') return route.continue()
    const n = ++counters.create
    if (n % 3 === 0) return loseAnswer(route)
    if (n % 5 === 0) return neverLeave(route)
    return route.continue()
  })
  await imp.page.route(/\/api\/patients\/[^/]+\/files\/upload$/, async route => {
    const n = ++counters.upload
    if (n % 4 === 0) return loseAnswer(route)
    return route.continue()
  })
  await imp.page.route(/\/api\/patients\/[^/]+\/legacy-history$/, async route => {
    if (route.request().method() !== 'POST') return route.continue()
    const n = ++counters.history
    if (n % 5 === 0) return neverLeave(route)
    return route.continue()
  })

  const e1 = await run(imp.page, 'Importer')
  console.log(`     footer said: ${e1.said.join(' | ')}`)
  console.log(`     faults injected: ${JSON.stringify(faults)} over ${JSON.stringify(counters)}`)
  e1.text.trim() === 'Import terminé.'
    ? ok('E1', `« Import terminé. » in ${e1.seconds} s despite ${faults.lostAnswer} lost answers + ${faults.neverLeft} lost calls`)
    : bad('E1', 'did not end cleanly', e1.text)
  e1.said.some(s => /Connexion perdue : nouvel essai dans N s\. L'import reprend tout seul\./.test(s))
    ? ok('E1-pause', 'footer said « Connexion perdue : nouvel essai dans N s. L\'import reprend tout seul. »')
    : bad('E1-pause', 'never showed the connection pause', e1.said.join(' | '))
  e1.said.some(s => /Import en cours : N sur 40 patients\./.test(s))
    ? ok('E1-count', 'footer counted « Import en cours : N sur 40 patients »')
    : bad('E1-count', 'no « N sur 40 » counter', e1.said.join(' | '))
  const failedRows = await imp.page.locator('li', { hasText: /Incomplet ·|Échec/ }).count()
  failedRows === 0 ? ok('E1-rows', 'no failed row') : bad('E1-rows', `${failedRows} failed rows`)
  await imp.page.mouse.move(0, 0)
  await imp.page.screenshot({ path: `${SHOTS}E1-done.png` })

  // E1-sql — nothing created twice.
  const pairs = pairsOf(join(DATA, 'E', 'dossiers-E'))
  const pats = Number(sql(`select count(*) from "Patients" where "ClinicId"='${CLINIC}' and ${inPairs(pairs)}`))
  const twins = sql(`select count(*) from (select 1 from "Patients" where "ClinicId"='${CLINIC}' and ${inPairs(pairs)} group by "FirstName","LastName" having count(*)>1) x`)
  const files = Number(sql(`select count(*) from "PatientFiles" f join "Patients" p on p."Id"=f."PatientId" where p."ClinicId"='${CLINIC}' and ${inPairs(pairs).replace(/"FirstName"/, 'p."FirstName"').replace(/"LastName"/, 'p."LastName"')}`))
  const fileTwins = sql(`select count(*) from (select 1 from "PatientFiles" f join "Patients" p on p."Id"=f."PatientId" where p."ClinicId"='${CLINIC}' group by f."PatientId", f."FileName", f."FileSize" having count(*)>1) x`)
  const histTwins = sql(`select count(*) from (select 1 from "PatientLegacyHistoryEntries" h where h."ClinicId"='${CLINIC}' group by h."PatientId", h."Date", h."Category", h."Title", h."Details" having count(*)>1) x`)
  const hist = sql(`select count(*) from "PatientLegacyHistoryEntries" h join "Patients" p on p."Id"=h."PatientId" where p."ClinicId"='${CLINIC}' and ${inPairs(pairs).replace(/"FirstName"/, 'p."FirstName"').replace(/"LastName"/, 'p."LastName"')}`)
  pats === 40 && twins === '0' && files === 120 && fileTwins === '0' && histTwins === '0' && Number(hist) > 0
    ? ok('E1-sql', `40 patients · 120 files · ${hist} history lines · nothing twice`)
    : bad('E1-sql', 'counts', JSON.stringify({ pats, twins, files, fileTwins, hist, histTwins }))

  // E2 — created without the refused date, which went to the notes.
  const e2 = sql(`select coalesce("Notes",'') || '|' || coalesce(to_char("DateOfBirth",'YYYY-MM-DD'),'') from "Patients" where "ClinicId"='${CLINIC}' and "FirstName"=${q(e2First)} and "LastName"=${q(e2Last)}`)
  const e2Card = await imp.page.locator('li', { hasText: 'Créé sans un champ refusé' }).count()
  ;/Dossier créé le \(ancien logiciel\) : \d\d\/\d\d\/\d{4}/.test(e2) && e2.endsWith('2023-06-01') && e2Card === 1
    ? ok('E2', `created with birth 2023-06-01, « Dossier créé le » kept in the notes, card says « Créé sans un champ refusé »`)
    : bad('E2', 'simplified creation', JSON.stringify({ e2: e2.slice(0, 300), e2Card }))

  // E3 — speed against run-2's 195 s for the same size without faults.
  // The plan's bar: well under run-2's 195 s, which had no faults at all (run 3 probe fix: was a stricter 120 s).
  e1.seconds < 195 ? ok('E3', `${e1.seconds} s for 40 dossiers + 80 radios with faults (run-2: 195 s, no faults)`) : bad('E3', 'not faster', `${e1.seconds} s`)

  // E4 — « Relancer » after a full run asks nothing about the 40 done rows.
  await imp.page.unrouteAll({ behavior: 'ignoreErrors' })
  const before = imp.seen.length
  const e4 = await run(imp.page, "Relancer l'import")
  const calls = imp.seen.slice(before).filter(u => /^\/patients/.test(u))
  e4.text.trim() === 'Import terminé.' && calls.length === 0 && e4.seconds <= 10
    ? ok('E4', `relaunch ended in ${e4.seconds} s with 0 patient calls`)
    : bad('E4', 'relaunch re-asked done rows', JSON.stringify({ text: e4.text, seconds: e4.seconds, calls: calls.slice(0, 5), n: calls.length }))

  // ---- E5: a file deleted from disk after it was picked ----------------------------------------------------------
  const tmp = join(SCRATCH, 'pr123-F-copy')
  rmSync(tmp, { recursive: true, force: true })
  cpSync(join(DATA, 'F'), tmp, { recursive: true })
  await pick(imp.page, join(tmp, 'dossiers-F'), join(tmp, 'radios-F'))
  const radioDirs = readdirSync(join(tmp, 'radios-F'))
  const victimDir = join(tmp, 'radios-F', radioDirs[0])
  const victim = readdirSync(victimDir)[0]
  rmSync(join(victimDir, victim))
  const e5 = await run(imp.page, 'Importer')
  // Cards only: a card's error list is a nested <li> (run 3 probe fix — it counted each failure twice).
  const unreadable = await imp.page.locator('section[aria-labelledby="li-patients"] > ul > li', { hasText: "n'a pas pu être lu sur ce PC" }).count()
  const fPairs = pairsOf(join(tmp, 'dossiers-F'))
  const fPats = Number(sql(`select count(*) from "Patients" where "ClinicId"='${CLINIC}' and ${inPairs(fPairs)}`))
  const fFiles = Number(sql(`select count(*) from "PatientFiles" f join "Patients" p on p."Id"=f."PatientId" where p."ClinicId"='${CLINIC}' and ${inPairs(fPairs).replace(/"FirstName"/, 'p."FirstName"').replace(/"LastName"/, 'p."LastName"')}`))
  unreadable === 1 && fPats === 3 && fFiles === 8 && /ligne.*incompl/.test(e5.text)
    ? ok('E5', `the vanished « ${victim} » is named on its row; 3 patients · 8 of 9 files; footer « ${e5.text} »`)
    : bad('E5', 'unreadable file', JSON.stringify({ unreadable, fPats, fFiles, text: e5.text }))
  await imp.page.mouse.move(0, 0)
  await imp.page.screenshot({ path: `${SHOTS}E5-unreadable.png` })
  rmSync(tmp, { recursive: true, force: true })

  // ---- G1–G5: the prod case of 26/11 — the page lost mid-run, then the WHOLE folder picked again on a new page -----
  const gPairs = pairsOf(join(DATA, 'G', 'dossiers-G'))
  const gWhere = inPairs(gPairs)
  const gWhereP = gWhere.replace(/"FirstName"/, 'p."FirstName"').replace(/"LastName"/, 'p."LastName"')
  const gCount = () => ({
    pats: Number(sql(`select count(*) from "Patients" where "ClinicId"='${CLINIC}' and ${gWhere}`)),
    noFile: Number(sql(`select count(*) from "Patients" p where p."ClinicId"='${CLINIC}' and ${gWhereP} and not exists (select 1 from "PatientFiles" f where f."PatientId"=p."Id")`)),
    noHist: Number(sql(`select count(*) from "Patients" p where p."ClinicId"='${CLINIC}' and ${gWhereP} and not exists (select 1 from "PatientLegacyHistoryEntries" h where h."PatientId"=p."Id")`)),
    files: Number(sql(`select count(*) from "PatientFiles" f join "Patients" p on p."Id"=f."PatientId" where p."ClinicId"='${CLINIC}' and ${gWhereP}`)),
  })
  const g1 = await session(S.import)
  await pick(g1.page, join(DATA, 'G', 'dossiers-G'), join(DATA, 'G', 'radios-G'))
  const g1Plan = await figure(g1.page, 'Patients à créer')
  ;/^300\b/.test(g1Plan) ? ok('G1-plan', `plan: ${g1Plan} à créer`) : bad('G1-plan', 'plan figure', g1Plan)
  // G0 — the plan's size on screen (run 3 found ~390 000 nodes: every folder card mounted a list of every patient).
  const g0Nodes = await g1.page.evaluate(() => document.getElementsByTagName('*').length)
  g0Nodes < 60_000 ? ok('G0', `${g0Nodes} elements for a 300-dossier plan`) : bad('G0', 'plan too heavy to run', `${g0Nodes} elements`)
  let gCreates = 0
  const cut = new Promise(resolve => g1.page.on('response', r => {
    if (r.url() === `${API}/patients` && r.request().method() === 'POST' && ++gCreates === 100) resolve()
  }))
  await g1.page.getByRole('button', { name: 'Importer', exact: true }).click()
  await Promise.race([cut, sleep(15 * 60_000)])
  await g1.ctx.close() // the page is gone mid-run — the PC slept, the window was closed
  await sleep(5000)
  const atCut = gCount()
  atCut.pats >= 100 && atCut.pats < 300
    ? ok('G1', `page closed after the 100th create: ${atCut.pats} patients in · ${atCut.noFile} without any file · ${atCut.noHist} without history`)
    : bad('G1', 'cut state', JSON.stringify({ gCreates, ...atCut }))

  const g2 = await session(S.import)
  await pick(g2.page, join(DATA, 'G', 'dossiers-G'), join(DATA, 'G', 'radios-G'))
  const g2Create = await figure(g2.page, 'Patients à créer')
  const g2Exist = await figure(g2.page, 'Déjà dans le cabinet')
  parseInt(g2Create, 10) === 300 - atCut.pats && parseInt(g2Exist, 10) === atCut.pats
    ? ok('G2', `whole folder again: ${g2Create} à créer · ${g2Exist} déjà dans le cabinet`)
    : bad('G2', 'plan after the cut', JSON.stringify({ g2Create, g2Exist, atCut: atCut.pats }))

  const g3From = g2.stamps.length
  const g3 = await run(g2.page, 'Importer', 45 * 60_000)
  const g3Stamps = g2.stamps.slice(g3From)
  console.log(`     footer said: ${g3.said.slice(0, 12).join(' | ')}${g3.said.length > 12 ? ' | …' : ''}`)
  g3.text.trim() === 'Import terminé.'
    ? ok('G3', `« Import terminé. » in ${g3.seconds} s`)
    : bad('G3', 'did not end cleanly', g3.text)
  const g3Failed = await g2.page.locator('li', { hasText: /Incomplet ·|Échec/ }).count()
  g3Failed === 0 ? ok('G3-rows', 'no failed row') : bad('G3-rows', `${g3Failed} failed rows`)
  const after = gCount()
  const gTwins = sql(`select count(*) from (select 1 from "Patients" where "ClinicId"='${CLINIC}' and ${gWhere} group by "FirstName","LastName" having count(*)>1) x`)
  const gFileTwins = sql(`select count(*) from (select 1 from "PatientFiles" f join "Patients" p on p."Id"=f."PatientId" where p."ClinicId"='${CLINIC}' group by f."PatientId", f."FileName", f."FileSize" having count(*)>1) x`)
  const gHistTwins = sql(`select count(*) from (select 1 from "PatientLegacyHistoryEntries" h where h."ClinicId"='${CLINIC}' group by h."PatientId", h."Date", h."Category", h."Title", h."Details" having count(*)>1) x`)
  after.pats === 300 && after.files === 900 && after.noFile === 0 && after.noHist === 0 && gTwins === '0' && gFileTwins === '0' && gHistTwins === '0'
    ? ok('G3-sql', '300 patients · 900 files · every patient has its PDF and history · nothing twice')
    : bad('G3-sql', 'counts', JSON.stringify({ ...after, gTwins, gFileTwins, gHistTwins }))
  atCut.noFile + atCut.noHist === 0
    ? skip('G3-heal', 'the cut left no patient without its PDF or history this time — nothing to complete')
    : after.noFile === 0 && after.noHist === 0
      ? ok('G3-heal', `the ${atCut.noFile} patient(s) cut without files and ${atCut.noHist} without history are complete now`)
      : bad('G3-heal', 'cut patients still incomplete', JSON.stringify(after))
  await g2.page.mouse.move(0, 0)
  await g2.page.screenshot({ path: `${SHOTS}G3-done.png` })

  // G4 — the run outlives several access tokens (3 min here) with 6 lanes in flight: no stop, no sign-out.
  const signedOut = g2.page.url().includes('/login')
  const sessionWords = g3.said.filter(s => /session|reconnect|identifiant|expir/i.test(s))
  !signedOut && sessionWords.length === 0 && g3.text.trim() === 'Import terminé.'
    ? ok('G4', `${g3.seconds} s ≈ ${Math.floor(g3.seconds / 180)} token lifetimes: still signed in, no session message (${g2.answers.s401} × 401 absorbed, ${g2.answers.renewals} renewals)`)
    : bad('G4', 'session trouble during the long run', JSON.stringify({ signedOut, sessionWords, s401: g2.answers.s401, renewals: g2.answers.renewals }))

  // G5 — pace: the cabinet's per-address limit on prod is 600 / 60 s.
  const g3Peak = peak60(g3Stamps)
  g3Peak <= 600 || g2.answers.s429 > 0
    ? ok('G5', `peak ${g3Peak} calls / 60 s · ${g3Stamps.length} calls · ${g2.answers.s429} × 429 · ${(g3.seconds / Math.max(1, 300 - atCut.pats)).toFixed(1)} s per new patient (incl. re-checking ${atCut.pats})`)
    : bad('G5', 'pace over the limit with no 429 seen', `${g3Peak}`)
  console.log(`     E1 pace for comparison: peak ${peak60(imp.stamps)} calls / 60 s`)
  await g2.ctx.close()
} catch (error) {
  bad('walk', 'threw', error.stack ?? String(error))
} finally {
  await browser.close()
}

// ---- E8: a body that trickles in — 408 with our sentence, logged as a warning, never an ERR ----------------------------
try {
  const { clinicSignIn } = await import('file:///C:/dev/clinic-migration/lib.mjs')
  const { token } = await clinicSignIn(API, { email: S.collegue.email, password: S.collegue.password, log: () => {} })
  const errsBefore = (readFileSync(API_LOG, 'utf8').match(/ ERR\]/g) ?? []).length
  const raw = await new Promise(resolve => {
    let buf = ''
    let settle = null
    let finished = false
    const sock = connect(5095, 'localhost', () => {
      sock.write(`POST /api/patients HTTP/1.1\r\nHost: localhost:5095\r\nAuthorization: Bearer ${token}\r\nContent-Type: application/json\r\nContent-Length: 4000\r\n\r\n{`)
    })
    const drip = setInterval(() => { if (!sock.destroyed) sock.write(' ') }, 2000) // ½ byte/s, far under Kestrel's 240
    const done = () => { if (finished) return; finished = true; clearInterval(drip); sock.destroy(); resolve(buf) }
    sock.on('data', d => { buf += d; clearTimeout(settle); settle = setTimeout(done, 1000) })
    sock.on('error', () => {})
    sock.on('close', done)
    setTimeout(done, 60_000)
  })
  await sleep(1000)
  const log = readFileSync(API_LOG, 'utf8')
  const errsAfter = (log.match(/ ERR\]/g) ?? []).length
  const status = raw.match(/^HTTP\/1\.1 (\d{3})/)?.[1]
  status === '408' && raw.includes('La connexion est trop lente') && /WRN\] Request refused before reaching a handler \(408\)/.test(log) && errsAfter === errsBefore
    ? ok('E8', '408 « La connexion est trop lente … Réessayez. », a WRN line, no new ERR')
    : bad('E8', 'slow body', JSON.stringify({ status, body: raw.slice(-200), errsBefore, errsAfter }))
} catch (error) {
  bad('E8', 'threw', error.stack ?? String(error))
}

findings.length ? (console.log(`\n${findings.length} finding(s):`), findings.forEach(f => console.log(f))) : console.log('\nALL CLEAR')
