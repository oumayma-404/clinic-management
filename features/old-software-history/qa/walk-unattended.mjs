// QA walk — the import runs on its own (qa/plan-unattended.md), in one launch, after walk.mjs on the same database.
// Local isolated stack only (API :5095, web :3095, DB clinic_pr123_qa). Fixes nothing: every failure is collected.
// Usage: node walk-unattended.mjs <scratchpadDir> <runLabel>
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'
import { cpSync, mkdirSync, readFileSync, readdirSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const SCRATCH = process.argv[2]
if (!SCRATCH) throw new Error('usage: node walk-unattended.mjs <scratchpadDir> <runLabel>')
const { chromium } = createRequire(join(SCRATCH, 'x.js'))('playwright-core')
const API = 'http://localhost:5095/api'
const WEB = 'http://localhost:3095'
const DATA = join(SCRATCH, 'pr123-data')
const S = JSON.parse(readFileSync(join(SCRATCH, 'pr123-state.json'), 'utf8'))
const CLINIC = S.clinicId
const SHOTS = fileURLToPath(new URL(`./shots/${process.argv[3] ?? 'run'}/`, import.meta.url))
mkdirSync(SHOTS, { recursive: true })

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
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
  page.on('request', r => { if (r.url().startsWith(API)) seen.push(r.url().slice(API.length)) })
  await page.goto(`${WEB}/login`, { timeout: 180_000 })
  await page.fill('input[type=email]', who.email)
  await page.fill('input[type=password]', who.password)
  await page.click('button:has-text("Se connecter")')
  await page.waitForURL(u => !u.pathname.includes('/login'), { timeout: 120_000 })
  return { ctx, page, seen }
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
async function run(page, label) {
  await page.getByRole('button', { name: label, exact: true }).click()
  const started = Date.now()
  const said = new Set()
  for (;;) {
    await sleep(300)
    const text = await footer(page).innerText().catch(() => '')
    if (text) said.add(text.replace(/\d+ s\b/, 'N s').replace(/\d+ sur/, 'N sur'))
    const again = page.getByRole('button', { name: "Relancer l'import", exact: true })
    if ((await again.isVisible().catch(() => false)) && (await again.isEnabled().catch(() => false))) {
      return { text, said: [...said], seconds: Math.round((Date.now() - started) / 1000) }
    }
    if (Date.now() - started > 15 * 60_000) return { text: `TIMEOUT: ${text}`, said: [...said], seconds: 900 }
  }
}
const pairsOf = dir => readdirSync(dir).filter(f => f.endsWith('.pdf')).map(f => {
  const words = f.replace(/^\d+-/, '').replace(/\.pdf$/, '').split(' ')
  return [words[0], words.slice(1).join(' ')]
})
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
  e1.seconds < 120 ? ok('E3', `${e1.seconds} s for 40 dossiers + 80 radios with faults (run-2: 195 s, no faults)`) : bad('E3', 'not faster', `${e1.seconds} s`)

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
  const unreadable = await imp.page.locator('li', { hasText: "n'a pas pu être lu sur ce PC" }).count()
  const fPairs = pairsOf(join(tmp, 'dossiers-F'))
  const fPats = Number(sql(`select count(*) from "Patients" where "ClinicId"='${CLINIC}' and ${inPairs(fPairs)}`))
  const fFiles = Number(sql(`select count(*) from "PatientFiles" f join "Patients" p on p."Id"=f."PatientId" where p."ClinicId"='${CLINIC}' and ${inPairs(fPairs).replace(/"FirstName"/, 'p."FirstName"').replace(/"LastName"/, 'p."LastName"')}`))
  unreadable === 1 && fPats === 3 && fFiles === 8 && /ligne.*incompl/.test(e5.text)
    ? ok('E5', `the vanished « ${victim} » is named on its row; 3 patients · 8 of 9 files; footer « ${e5.text} »`)
    : bad('E5', 'unreadable file', JSON.stringify({ unreadable, fPats, fFiles, text: e5.text }))
  await imp.page.mouse.move(0, 0)
  await imp.page.screenshot({ path: `${SHOTS}E5-unreadable.png` })
  rmSync(tmp, { recursive: true, force: true })
} catch (error) {
  bad('walk', 'threw', error.stack ?? String(error))
} finally {
  await browser.close()
}

findings.length ? (console.log(`\n${findings.length} finding(s):`), findings.forEach(f => console.log(f))) : console.log('\nALL CLEAR')
