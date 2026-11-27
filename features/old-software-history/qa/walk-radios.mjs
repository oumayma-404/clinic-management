// QA walk — the radios/photos pass (qa/plan-radios.md), in one launch, FIRST on a fresh database.
// Local isolated stack only (API :5095, web :3095, DB clinic_pr123_qa). Fixes nothing: every failure is collected.
// Usage: node walk-radios.mjs <scratchpadDir> <runLabel>
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const SCRATCH = process.argv[2]
if (!SCRATCH) throw new Error('usage: node walk-radios.mjs <scratchpadDir> <runLabel>')
const { chromium } = createRequire(join(SCRATCH, 'x.js'))('playwright-core')
const sharp = createRequire(new URL('../../../web/', import.meta.url))('sharp')
const API = 'http://localhost:5095/api'
const WEB = 'http://localhost:3095'
const S = JSON.parse(readFileSync(join(SCRATCH, 'pr123-state.json'), 'utf8'))
const CLINIC = S.clinicId
const SHOTS = fileURLToPath(new URL(`./shots/${process.argv[3] ?? 'run'}/`, import.meta.url))
mkdirSync(SHOTS, { recursive: true })

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const sleep = ms => new Promise(r => setTimeout(r, ms))
const sql = q => execFileSync('docker', ['exec', 'clinic-postgres', 'psql', '-U', 'clinic_user', '-d', 'clinic_pr123_qa', '-tAc', q]).toString().trim()

// ---- R0: the cabinet, through the API ---------------------------------------------------------------------------
const FIRST = ['Amira', 'Sami', 'Leila', 'Karim', 'Nour', 'Hedi', 'Fatma', 'Ines', 'Walid', 'Rania', 'Mehdi', 'Salma', 'Youssef',
  'Sarra', 'Anis', 'Mariem', 'Bilel', 'Olfa', 'Firas', 'Hiba', 'Omar', 'Asma', 'Ghassen', 'Emna', 'Riadh', 'Dorra', 'Nizar',
  'Yasmine', 'Fares', 'Khadija', 'Slim', 'Wafa', 'Aziz', 'Lina', 'Moez', 'Rim', 'Tarek', 'Houda', 'Zied', 'Sonia', 'Hamza',
  'Nadia', 'Jalel', 'Amel', 'Chokri', 'Hela', 'Lotfi', 'Imen', 'Marwen', 'Ons', 'Sofiene', 'Afef', 'Habib', 'Kaouther', 'Nabil',
  'Sihem', 'Raouf', 'Faten', 'Adel', 'Monia', 'Kamel']
const LAST = ['Akrout', 'Baklouti', 'Chaari', 'Dammak', 'Elloumi', 'Frikha', 'Ghorbel', 'Hentati', 'Jallouli', 'Kallel', 'Loukil',
  'Mhiri', 'Neji', 'Ouali', 'Rouissi', 'Sellami', 'Taktak', 'Abdelmoula', 'Bouaziz', 'Charrada', 'Derbel', 'Fendri',
  'Gargouri', 'Hadj Taieb', 'Jarraya', 'Karoui', 'Lajmi', 'Mnif', 'Siala']
const HOMONYM_FIRST = ['Malek', 'Eya', 'Chaima', 'Yassine', 'Oussama']

const people = [] // { idx, first, last, birth, number, folder, files, expect: 'sure' | 'ask' | 'none' }
for (let j = 0; j < 395; j++) {
  people.push({ first: FIRST[j % FIRST.length], last: LAST[(j + Math.floor(j / FIRST.length)) % LAST.length], birth: `19${50 + (j % 50)}-0${1 + (j % 9)}-1${j % 9}`, style: j % 7 })
}
for (let k = 0; k < 10; k++) people.push({ first: 'Mohamed', last: LAST[k], birth: `1975-03-1${k}`, style: 'med' })
for (let k = 0; k < 5; k++) {
  people.push({ first: HOMONYM_FIRST[k], last: LAST[k], birth: '1980-01-01', style: 'homonym-a' })
  people.push({ first: HOMONYM_FIRST[k], last: LAST[k], birth: '1990-01-01', style: 'homonym-b' })
}
people.forEach((p, i) => { p.idx = i; p.number = String(i + 1) })

const folderName = (p) => {
  const L = p.last.toUpperCase()
  switch (p.style) {
    case 0: return `${L} ${p.first}`
    case 1: return `${p.first} ${p.last}`
    case 2: return `${p.last} ${p.first}`.toLowerCase()
    case 3: return `${L} ${p.first} 2023`
    case 4: return `${p.number} - ${p.first} ${p.last}`
    case 5: return `${p.first} ${p.last} radio`
    case 6: return `${L}_${p.first}`
    case 'med': return `Med ${L}`
    case 'homonym-a': return `${L} ${p.first}`
    case 'homonym-b': return `${L} ${p.first} (2)`
  }
}

async function apiLogin(who) {
  const r = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: who.email, password: who.password }) })
  const d = await r.json()
  return (d.value ?? d).accessToken
}

const token = await apiLogin(S.import)
{
  const queue = [...people]
  let failed = 0
  await Promise.all(Array.from({ length: 6 }, async () => {
    while (queue.length) {
      const p = queue.shift()
      const r = await fetch(`${API}/patients`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
        body: JSON.stringify({ firstName: p.first, lastName: p.last, dateOfBirth: p.birth, notes: `Importé de l'ancien logiciel (dossier n° ${p.number}/2023).`, allowDuplicate: true }),
      })
      if (r.ok) { const d = await r.json(); p.id = (d.value ?? d).id } else { failed++; console.log(`     create ${p.first} ${p.last}: ${r.status} ${(await r.text()).slice(0, 120)}`) }
    }
  }))
  failed === 0 && people.every((p) => p.id) ? ok('R0', `${people.length} patients created through the API`) : bad('R0', 'arrangement', `${failed} failed`)
}

// The radios folder: one per person, the misspelt second folder of person 0, and a folder of nobody.
const DIR = join(SCRATCH, 'pr123-data', 'R', 'radios-R')
rmSync(join(SCRATCH, 'pr123-data', 'R'), { recursive: true, force: true })
const jpeg = (seed) => sharp({ create: { width: 160, height: 120, channels: 3, background: { r: (seed * 37) % 255, g: (seed * 91) % 255, b: 120 } } }).jpeg().toBuffer()
const folders = [] // { name, owner (idx | null), files }
for (const p of people) folders.push({ name: folderName(p), owner: p.idx, files: [`pano-${p.idx}.jpg`, `retro-${p.idx}.jpg`] })
const P = people[0]
folders.push({ name: 'AKROUTT Amra', owner: P.idx, files: [`pano2-${P.idx}.jpg`, `retro2-${P.idx}.jpg`], second: true })
folders.push({ name: 'Sans nom 12', owner: null, files: ['pano-none.jpg', 'retro-none.jpg'] })
for (const [i, f] of folders.entries()) {
  mkdirSync(join(DIR, f.name), { recursive: true })
  for (const [k, file] of f.files.entries()) writeFileSync(join(DIR, f.name, file), await jpeg(i * 2 + k))
}
// A name one letter from another patient's of the same family (« Amel » / « Kamel » Rouissi) is ASKED, by design: run 5
// measured that filing an exact name over such a neighbour misfiles 17 typo'd folders in 600 against 6 (probe fix).
const lev = (a, b) => { let prev = Array.from({ length: b.length + 1 }, (_, i) => i); for (let i = 1; i <= a.length; i++) { const cur = [i]; for (let j = 1; j <= b.length; j++) cur[j] = Math.min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1)); prev = cur } return prev[b.length] }
const hasNeighbour = (p) => people.some((q) => q !== p && q.last === p.last && lev(q.first.toLowerCase(), p.first.toLowerCase()) === 1)
const near = people.filter((p) => (typeof p.style === 'number' || p.style === 'med') && hasNeighbour(p))
const homonymFolders = people.filter((p) => String(p.style).startsWith('homonym'))
// Set from what the plan shows once R1 has held its invariants: R2–R4 count on the same figure.
let WAITING = homonymFolders.length + 1
let sure = people.filter((p) => typeof p.style === 'number' || p.style === 'med')
const plural = (n, one, many) => (n > 1 ? many : one)
console.log(`     ${folders.length} folders written (${sure.length} expected sure, ${WAITING} to confirm incl. ${near.length} near names, 1 of nobody)`)

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } })
await ctx.route('**/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const page = await ctx.newPage()
page.setDefaultTimeout(60_000)
const seen = []
page.on('request', r => { if (r.url().startsWith(API)) seen.push(`${r.method()} ${r.url().slice(API.length)}`) })
const footer = () => page.locator('div.sticky p[role=status]').first()
const cards = () => page.locator('section[aria-labelledby="li-folders"] > ul > li')
const cardOf = (name) => cards().filter({ has: page.getByText(name, { exact: true }) }).first()
async function run(label, limitMs = 20 * 60_000) {
  await page.getByRole('button', { name: label, exact: true }).click()
  const started = Date.now()
  const said = new Set()
  for (;;) {
    await sleep(300)
    const text = await footer().innerText().catch(() => '')
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

try {
  await page.goto(`${WEB}/login`, { timeout: 180_000 })
  await page.fill('input[type=email]', S.import.email)
  await page.fill('input[type=password]', S.import.password)
  await page.click('button:has-text("Se connecter")')
  await page.waitForURL(u => !u.pathname.includes('/login'), { timeout: 120_000 })
  await page.goto(`${WEB}/import-ancien-logiciel`, { timeout: 180_000 })
  const inputs = page.locator('input[type=file][webkitdirectory]')
  await inputs.nth(1).waitFor({ state: 'attached' })
  await inputs.nth(1).setInputFiles(DIR)

  // ---- R1-plan -----------------------------------------------------------------------------------------------
  const t0 = Date.now()
  await page.getByRole('button', { name: 'Analyser' }).click()
  await page.getByText('Fichiers à envoyer', { exact: true }).waitFor({ timeout: 300_000 })
  const analyseS = (Date.now() - t0) / 1000
  const texts = await cards().evaluateAll(lis => lis.map(li => li.innerText))
  const nSure = texts.filter(t => /\n→ /.test(t) || /^→ /m.test(t)).length
  const nAsk = texts.filter(t => /Le nom ressemble/.test(t)).length
  const nNone = texts.filter(t => /Ne sera pas importé\./.test(t)).length
  const elements = await page.evaluate(() => document.getElementsByTagName('*').length)
  const askNames = texts.filter(t => /Le nom ressemble/.test(t)).map(t => t.split('\n')[0])
  console.log(`     analysed in ${analyseS.toFixed(1)} s · ${texts.length} cards · ${elements} elements · asked: ${askNames.join(' | ')}`)
  const byName = new Map(folders.map((f) => [f.name, f]))
  const mustAsk = new Set([...homonymFolders.map(folderName), 'AKROUTT Amra'])
  const mayAsk = new Set(near.map(folderName))
  const missedAsk = [...mustAsk].filter((n) => !askNames.includes(n))
  const strayAsk = askNames.filter((n) => !mustAsk.has(n) && !mayAsk.has(n))
  // Every folder filed on its own must name its own patient (first, last and birth date).
  const fr = (iso) => `${iso.slice(8, 10)}/${iso.slice(5, 7)}/${iso.slice(0, 4)}`
  const misfiledPlan = texts.filter((t) => /^→ /m.test(t)).filter((t) => {
    const owner = people[byName.get(t.split('\n')[0])?.owner]
    const to = t.split('\n').find((l) => l.startsWith('→ ')) ?? ''
    return !owner || !to.includes(`${owner.first} ${owner.last}`) || !to.includes(fr(owner.birth))
  })
  const nearAsked = askNames.filter((n) => mayAsk.has(n))
  texts.length === folders.length && !missedAsk.length && !strayAsk.length && !misfiledPlan.length && nSure + nAsk + nNone === folders.length && nNone === 1 && analyseS < 30 && elements < 60_000
    ? ok('R1-plan', `${nSure} filed on their own, each on its own patient · ${nAsk} to confirm (homonyms, the misspelt folder, ${nearAsked.length} one-letter neighbours: ${nearAsked.join(', ')}) · ${nNone} not imported · ${analyseS.toFixed(1)} s · ${elements} elements`)
    : bad('R1-plan', 'verdicts', JSON.stringify({ cards: texts.length, nSure, nAsk, nNone, analyseS, elements, missedAsk, strayAsk, misfiled: misfiledPlan.slice(0, 3) }))
  WAITING = nAsk
  sure = people.filter((p) => (typeof p.style === 'number' || p.style === 'med') && !askNames.includes(folderName(p)))
  await page.screenshot({ path: `${SHOTS}R1-plan.png` })

  // ---- R2 ----------------------------------------------------------------------------------------------------
  const importBtn = page.getByRole('button', { name: 'Importer', exact: true })
  const r2Text = (await footer().innerText()).trim()
  ;(await importBtn.isEnabled()) && r2Text === `${WAITING} dossiers à confirmer : ils attendront votre choix, le reste peut être importé.`
    ? ok('R2', `« Importer » enabled · « ${r2Text} »`)
    : bad('R2', 'undecided folders still block', JSON.stringify({ enabled: await importBtn.isEnabled(), r2Text }))

  // ---- R3 ----------------------------------------------------------------------------------------------------
  const r3 = await run('Importer')
  console.log(`     footer said: ${r3.said.slice(0, 6).join(' | ')}`)
  const counted = r3.said.some(s => s.startsWith(`Import en cours : N sur ${sure.length} patients.`))
  const filesNow = Number(sql(`select count(*) from "PatientFiles" where "ClinicId"='${CLINIC}'`))
  const twins = sql(`select count(*) from (select 1 from "PatientFiles" where "ClinicId"='${CLINIC}' group by "PatientId", "FileName", "FileSize" having count(*)>1) x`)
  const onWaiting = Number(sql(`select count(*) from "PatientFiles" f where f."ClinicId"='${CLINIC}' and f."PatientId" in (${homonymFolders.map(p => `'${p.id}'`).join(',')})`))
  const r3Done = r3.text.trim() === `Import terminé. ${WAITING} dossiers attendent votre choix : choisissez, puis relancez.`
  r3Done && counted && filesNow === 2 * sure.length && twins === '0' && onWaiting === 0
    ? ok('R3', `« ${r3.text.trim()} » in ${r3.seconds} s · progress counted cabinet patients · ${filesNow} files · none on the ${WAITING} waiting · nothing twice`)
    : bad('R3', 'radios run', JSON.stringify({ text: r3.text, counted, filesNow, twins, onWaiting, seconds: r3.seconds }))
  await page.mouse.move(0, 0)
  await page.screenshot({ path: `${SHOTS}R3-done.png` })

  // ---- R5 + R4: confirm two folders, relaunch ----------------------------------------------------------------
  const second = cardOf('AKROUTT Amra')
  const t5 = Date.now()
  await second.getByText(`${P.first} ${P.last}`, { exact: false }).first().click()
  await second.getByText(/^→ /).waitFor({ timeout: 10_000 })
  const clickMs = Date.now() - t5
  clickMs < 500 ? ok('R5', `the card shows its destination ${clickMs} ms after the click (${folders.length} cards on the page)`) : bad('R5', 'slow click', `${clickMs} ms`)

  const H = homonymFolders[0] // folder « LAST First », born 1980
  const hCard = cardOf(folderName(H))
  await hCard.getByText('01/01/1980', { exact: false }).first().click()
  await hCard.getByText(/^→ /).waitFor({ timeout: 10_000 })

  const before = seen.length
  const r4 = await run("Relancer l'import")
  const reads = seen.slice(before).filter(s => /^GET \/patients\/[^/]+\/files\/folders/.test(s)).length
  const pFiles = Number(sql(`select count(*) from "PatientFiles" where "PatientId"='${P.id}'`))
  const hFiles = Number(sql(`select count(*) from "PatientFiles" where "PatientId"='${H.id}'`))
  const total = Number(sql(`select count(*) from "PatientFiles" where "ClinicId"='${CLINIC}'`))
  const twins4 = sql(`select count(*) from (select 1 from "PatientFiles" where "ClinicId"='${CLINIC}' group by "PatientId", "FileName", "FileSize" having count(*)>1) x`)
  reads === 2 && pFiles === 4 && hFiles === 2 && total === 2 * sure.length + 4 && twins4 === '0' && r4.text.includes(`${WAITING - 2} ${plural(WAITING - 2, 'dossier attend', 'dossiers attendent')} votre choix`)
    ? ok('R4', `relaunch asked only the 2 confirmed (${reads} folder reads); ${P.first} ${P.last} 4 files, the homonym 2; ${total} in all; « ${r4.text.trim()} »`)
    : bad('R4', 'relaunch after confirming', JSON.stringify({ reads, pFiles, hFiles, total, twins4, text: r4.text }))

  // ---- R6 ----------------------------------------------------------------------------------------------------
  const rows = sql(`select "FileName" || '|' || "PatientId" from "PatientFiles" where "ClinicId"='${CLINIC}'`).split('\n').filter(Boolean)
  const misfiled = rows.filter(r => {
    const [name, pid] = r.split('|')
    const m = name.match(/^(?:pano|retro)2?-(\d+)\.jpg$/)
    return !m || people[Number(m[1])].id !== pid
  })
  misfiled.length === 0 ? ok('R6', `${rows.length} files, each on the patient its folder was made for`) : bad('R6', 'misfiled', misfiled.slice(0, 5).join(' ; '))
} catch (error) {
  bad('walk', 'threw', error.stack ?? String(error))
} finally {
  await browser.close()
}
findings.length ? (console.log(`\n${findings.length} finding(s):`), findings.forEach(f => console.log(f))) : console.log('\nALL CLEAR')
