// PR #123 QA walk — the plan in qa/plan.md, in one launch. Local isolated stack only (API :5095, web :3095,
// DB clinic_pr123_qa). Fixes nothing: every failure is collected and printed at the end.
// Usage: node walk.mjs <scratchpadDir> <runLabel>   (screenshots go to shots/<runLabel>/)
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const SCRATCH = process.argv[2]
if (!SCRATCH) throw new Error('usage: node walk.mjs <scratchpadDir>')
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
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }
const sleep = ms => new Promise(r => setTimeout(r, ms))
const sql = q => execFileSync('docker', ['exec', 'clinic-postgres', 'psql', '-U', 'clinic_user', '-d', 'clinic_pr123_qa', '-tAc', q]).toString().trim()

async function apiLogin(who) {
  const r = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: who.email, password: who.password }) })
  const d = await r.json()
  return (d.value ?? d).accessToken
}

const browser = await chromium.launch({ channel: 'chrome', headless: true })
async function session(who, viewport = { width: 1440, height: 900 }) {
  const ctx = await browser.newContext({ viewport })
  await ctx.route('**/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
  const page = await ctx.newPage()
  page.setDefaultTimeout(60_000)
  const stats = { api: 0, r429: 0, times: [] }
  page.on('response', r => {
    if (!r.url().startsWith(API)) return
    stats.api++
    stats.times.push(Date.now())
    if (r.status() === 429) stats.r429++
  })
  await page.goto(`${WEB}/login`, { timeout: 180_000 })
  await page.fill('input[type=email]', who.email)
  await page.fill('input[type=password]', who.password)
  await page.click('button:has-text("Se connecter")')
  await page.waitForURL(u => !u.pathname.includes('/login'), { timeout: 120_000 })
  return { ctx, page, stats }
}

const footer = page => page.locator('div.sticky p[role=status]').first()
async function pick(page, batch) {
  await page.goto(`${WEB}/import-ancien-logiciel`, { timeout: 180_000 })
  const inputs = page.locator('input[type=file][webkitdirectory]')
  await inputs.nth(0).waitFor({ state: 'attached' })
  await inputs.nth(0).setInputFiles(join(DATA, batch, `dossiers-${batch}`))
  await inputs.nth(1).setInputFiles(join(DATA, batch, `radios-${batch}`))
  await page.getByRole('button', { name: 'Analyser' }).click()
  await page.getByText('Patients à créer', { exact: true }).waitFor({ timeout: 180_000 })
}
async function figure(page, label) {
  const li = page.locator('li', { has: page.getByText(label, { exact: true }) }).first()
  // The value's own text node only: the size hint is a child <span> and innerText glues it on (« 120139,9 Ko »).
  return (await li.locator('p').nth(1).evaluate(p => p.firstChild?.textContent ?? '')).trim()
}
/** Presses the run button and waits for the end; returns what the footer said, whether it ever paused, the time. */
async function run(page, label) {
  await page.getByRole('button', { name: label, exact: true }).click()
  const started = Date.now()
  let paused = null
  for (;;) {
    await sleep(400)
    const text = await footer(page).innerText().catch(() => '')
    if (/Pause de \d+ s/.test(text)) paused ??= text
    const again = await page.getByRole('button', { name: "Relancer l'import", exact: true }).isVisible().catch(() => false)
    const enabled = again && (await page.getByRole('button', { name: "Relancer l'import", exact: true }).isEnabled())
    if (enabled) return { text, paused, seconds: Math.round((Date.now() - started) / 1000) }
    if (Date.now() - started > 15 * 60_000) return { text: `TIMEOUT: ${text}`, paused, seconds: 900 }
  }
}

// ---------------------------------------------------------------------------------------------------------------
const listDir = d => execFileSync('cmd', ['/c', 'dir', '/b', d]).toString().trim().split(/\r?\n/)
const namesOf = b => listDir(join(DATA, b, `dossiers-${b}`)).map(f => f.replace(/^\d+-/, '').replace(/\.pdf$/, '').split(' ').slice(1).join(' '))
const FILES_BY = b => Number(sql(`select count(*) from "PatientFiles" f join "Patients" p on p."Id"=f."PatientId" where p."ClinicId"='${CLINIC}' and p."LastName" in (${[...new Set(namesOf(b))].map(n => `'${n.replace(/'/g, "''")}'`).join(',')})`))
const PATIENTS_BY = b => Number(sql(`select count(*) from "Patients" where "ClinicId"='${CLINIC}' and "LastName" in (${[...new Set(namesOf(b))].map(n => `'${n.replace(/'/g, "''")}'`).join(',')})`))
const HISTORY_BY = b => Number(sql(`select count(*) from "PatientLegacyHistoryEntries" h join "Patients" p on p."Id"=h."PatientId" where p."ClinicId"='${CLINIC}' and p."LastName" in (${[...new Set(namesOf(b))].map(n => `'${n.replace(/'/g, "''")}'`).join(',')})`))

try {
  // ---- A: the import account, batch A, with a colleague working beside it -------------------------------------
  const imp = await session(S.import)
  await pick(imp.page, 'A')
  const toCreate = await figure(imp.page, 'Patients à créer')
  const toSend = await figure(imp.page, 'Fichiers à envoyer')
  ;/^40\b/.test(toCreate) && /^120\b/.test(toSend)
    ? ok('A3-plan', `plan: ${toCreate} à créer · ${toSend.replace(/\s+/g, ' ')} fichiers`)
    : bad('A3-plan', 'plan figures', `${toCreate} / ${toSend}`)

  // C4 — the plan at every width, before anything runs.
  for (const [w, h] of [[320, 640], [390, 844], [820, 1180], [1180, 820], [1440, 900], [1536, 730]]) {
    await imp.page.setViewportSize({ width: w, height: h })
    await sleep(600)
    const m = await imp.page.evaluate(() => {
      const main = document.querySelector('main')
      const btn = [...document.querySelectorAll('button')].find(b => b.textContent.trim() === 'Importer')
      const r = btn?.getBoundingClientRect()
      // F-1 (run 1): a number rendered by `n && <…>` shows as a bare text node straight inside a layout <div>.
      const strayZero = [...document.querySelectorAll('main div')].some(el => [...el.childNodes].some(n => n.nodeType === 3 && n.textContent.trim() === '0'))
      return { main: main ? main.scrollWidth - main.clientWidth : -1, doc: document.documentElement.scrollWidth - innerWidth, btnInView: !!r && r.bottom <= innerHeight && r.top >= 0 && r.right <= innerWidth, strayZero }
    })
    m.main <= 0 && m.doc <= 0 && m.btnInView && !m.strayZero
      ? ok(`C4-${w}x${h}`, 'no sideways scroll, « Importer » on screen, no stray « 0 »')
      : bad(`C4-${w}x${h}`, 'layout', JSON.stringify(m))
    await imp.page.mouse.move(0, 0)
    await imp.page.screenshot({ path: `${SHOTS}C4-plan-${w}x${h}.png` })
  }
  await imp.page.setViewportSize({ width: 1440, height: 900 })

  // A4 — the colleague's screen, in the same browser (same address), one call a second during the import.
  const col = await session(S.collegue)
  const probe = { stop: false, statuses: [] }
  const colToken = await col.page.evaluate(async () => (await (await fetch('/bff/auth/token')).json()).accessToken)
  const probeLoop = (async () => {
    while (!probe.stop) {
      const s = await col.page.evaluate(async ([api, t]) => (await fetch(`${api}/patients?page=1&pageSize=5`, { headers: { Authorization: `Bearer ${t}` } })).status, [API, colToken]).catch(() => 0)
      probe.statuses.push(s)
      await sleep(1000)
    }
  })()

  const before = { api: imp.stats.api, r429: imp.stats.r429 }
  const a = await run(imp.page, 'Importer')
  probe.stop = true
  await probeLoop
  const calls = imp.stats.api - before.api
  const n429 = imp.stats.r429 - before.r429
  const peak = Math.max(...imp.stats.times.map(t => imp.stats.times.filter(u => u >= t && u < t + 60_000).length))
  ;/Import terminé\.$/.test(a.text) && !a.paused && n429 === 0
    ? ok('A3', `« ${a.text} » in ${a.seconds} s · ${calls} API calls · peak ${peak} in 60 s (ordinary limit 120) · 0 × 429 · never paused`)
    : bad('A3', 'boosted run', `${a.text} · paused=${a.paused} · 429=${n429} · calls=${calls} · peak=${peak} · ${a.seconds}s`)
  peak > 120 ? ok('A3-rate', `the import really went past the ordinary 120/min (${peak} in a minute)`) : bad('A3-rate', 'never exceeded the ordinary limit, so the boost was not exercised', `peak ${peak}`)
  const refused = probe.statuses.filter(s => s !== 200)
  probe.statuses.length > 10 && refused.length === 0
    ? ok('A4', `colleague: ${probe.statuses.length} calls during the import, all 200`)
    : bad('A4', 'colleague refused during the import', `${refused.length}/${probe.statuses.length}: ${[...new Set(refused)].join(',')}`)
  await imp.page.screenshot({ path: `${SHOTS}A3-done.png`, fullPage: false })

  const aPatients = PATIENTS_BY('A'), aFiles = FILES_BY('A'), aHistory = HISTORY_BY('A')
  aPatients === 40 && aFiles === 120 && aHistory === 200
    ? ok('A5', `40 patients · 120 files · 200 history lines`)
    : bad('A5', 'counts after batch A', `${aPatients} patients · ${aFiles} files · ${aHistory} history`)
  const amira = sql(`select to_char("CreatedAt" at time zone 'UTC','YYYY-MM-DD HH24:MI') from "Patients" where "ClinicId"='${CLINIC}' and "FirstName"='Amira' and "LastName"='Trabelsi'`)
  amira === '2015-03-07 11:00' ? ok('A5-date', `« Dossier créé le » kept: Amira Trabelsi CreatedAt ${amira} UTC (noon Tunis)`) : bad('A5-date', 'creation date', amira)
  const sharedPhone = sql(`select string_agg("FirstName", ',' order by "FirstName") from "Patients" where "ClinicId"='${CLINIC}' and "LastName"='Ben Ali' and "PhoneNumberE164"='+21698765432'`)
  sharedPhone === 'Fatma,Ines' ? ok('C3', 'mother and daughter on one phone: both created') : bad('C3', 'shared phone', sharedPhone)

  // A6 — run it again: nothing twice.
  const a2 = await run(imp.page, "Relancer l'import")
  const again = [PATIENTS_BY('A'), FILES_BY('A'), HISTORY_BY('A')]
  ;/Import terminé\.$/.test(a2.text) && again.join() === [aPatients, aFiles, aHistory].join()
    ? ok('A6', `re-run in ${a2.seconds} s, counts unchanged (${again.join(' / ')})`)
    : bad('A6', 're-run', `${a2.text} · ${again.join(' / ')}`)

  // ---- B1: the colleague (no boost) imports batch B at the default pace, over a 120/min limit ------------------
  await col.page.setViewportSize({ width: 390, height: 844 })
  await sleep(60_000) // let the cabinet's shared minute empty, so B1 starts from a clean budget
  await pick(col.page, 'B')
  const b0 = col.stats.r429
  let pauseShot = false
  const watcher = (async () => {
    for (let i = 0; i < 1500 && !pauseShot; i++) {
      await sleep(400)
      if (/Pause de/.test(await footer(col.page).innerText().catch(() => ''))) {
        await col.page.screenshot({ path: `${SHOTS}B1-pause-390.png` }).catch(() => {})
        pauseShot = true
      }
    }
  })()
  // Arranged 429 (run 3 probe fix): the page now paces itself under the ordinary limit, so without a second caller
  // spending the address's minute it never meets a 429 and the pause path goes unexercised.
  const burstToken = await apiLogin(S.collegue)
  const burst = (async () => {
    await sleep(4000)
    await Promise.all(Array.from({ length: 10 }, async () => {
      for (let i = 0; i < 13; i++) await fetch(`${API}/patients?page=1&pageSize=1`, { headers: { Authorization: `Bearer ${burstToken}` } }).catch(() => {})
    }))
  })()
  const b = await run(col.page, 'Importer')
  await burst
  pauseShot = true
  await watcher
  const b429 = col.stats.r429 - b0
  const bPatients = PATIENTS_BY('B'), bFiles = FILES_BY('B')
  b.paused && /Import terminé\.$/.test(b.text) && bPatients === 15 && bFiles === 45
    ? ok('B1', `paused (« ${b.paused} »), ${b429} × 429 absorbed, ended « ${b.text} » in ${b.seconds} s · 15 patients · 45 files`)
    : bad('B1', 'unboosted run', `paused=${b.paused} · 429=${b429} · ${b.text} · ${bPatients} patients · ${bFiles} files · ${b.seconds}s`)
  await col.page.screenshot({ path: `${SHOTS}B1-done-390.png` })

  // ---- C1/C2: storage full, then room again ---------------------------------------------------------------------
  const impToken = await apiLogin(S.import)
  const pol = await (await fetch(`${API}/meta/upload-policy?profile=patient-file`, { headers: { Authorization: `Bearer ${impToken}` } })).json()
  const used = (pol.value ?? pol).storageUsedBytes
  sql(`update "Clinics" set "StorageQuotaBytes"=${used + 1_300_000} where "Id"='${CLINIC}'`) // arrangement on the throwaway DB
  console.log(`  (arranged: quota = used ${used} + 1 300 000 bytes, by SQL on the throwaway DB)`)
  await pick(imp.page, 'C')
  const warn = await imp.page.getByText("L'espace du cabinet ne suffit pas", { exact: false }).isVisible()
  warn ? ok('C1-plan', 'the plan warns the space will not suffice') : bad('C1-plan', 'no space warning on the plan')
  const c = await run(imp.page, 'Importer')
  const waiting = await imp.page.getByText(/en attente d'espace/).count()
  await imp.page.screenshot({ path: `${SHOTS}C1-full.png` })
  const cPatients = PATIENTS_BY('C'), cHistory = HISTORY_BY('C'), cFiles = FILES_BY('C')
  ;/fichiers restants attendent/.test(c.text) && waiting > 0 && cPatients === 6 && cHistory === 30 && cFiles < 18
    ? ok('C1', `stopped sending files: « ${c.text.slice(0, 90)}… » · ${waiting} lines « en attente d'espace » · 6 patients · 30 history · ${cFiles}/18 files`)
    : bad('C1', 'storage full', `${c.text} · waiting=${waiting} · ${cPatients} patients · ${cHistory} history · ${cFiles} files`)
  sql(`update "Clinics" set "StorageQuotaBytes"=null where "Id"='${CLINIC}'`)
  const c2 = await run(imp.page, "Relancer l'import")
  const cFiles2 = FILES_BY('C'), cPatients2 = PATIENTS_BY('C')
  const dupes = Number(sql(`select count(*) from (select f."PatientId", f."FileName" from "PatientFiles" f where f."ClinicId"='${CLINIC}' group by 1,2 having count(*)>1) x`))
  ;/Import terminé\.$/.test(c2.text) && cFiles2 === 18 && cPatients2 === 6 && dupes === 0
    ? ok('C2', `room again → re-run sent the rest: 18/18 files, 6 patients, no file twice`)
    : bad('C2', 'after freeing space', `${c2.text} · ${cFiles2} files · ${cPatients2} patients · ${dupes} duplicated names`)

  // ---- D3: an imported patient's « Ancien logiciel » tab ------------------------------------------------------
  const fatma = sql(`select "Id" from "Patients" where "ClinicId"='${CLINIC}' and "FirstName"='Fatma' and "LastName"='Ben Ali'`)
  await imp.page.goto(`${WEB}/patients/${fatma}`, { timeout: 180_000 })
  await imp.page.getByRole('tab', { name: /Ancien logiciel/ }).click()
  await imp.page.getByText('AMOXICILLINE', { exact: false }).first().waitFor({ timeout: 30_000 }).catch(() => {})
  const tab = await imp.page.locator('main').innerText()
  const has = ['Détartrage', 'AMOXICILLINE', 'Panoramique', 'Carie'].filter(s => tab.includes(s))
  has.length === 4 ? ok('D3', `« Ancien logiciel » lists ${has.join(', ')}`) : bad('D3', 'history tab', `found ${has.join(', ')}`)
  const folderFiles = sql(`select count(*) from "PatientFiles" f join "PatientFolders" d on d."Id"=f."FolderId" where f."PatientId"='${fatma}' and d."Name"='Ancien logiciel'`)
  folderFiles === '3' ? ok('D3-files', 'folder « Ancien logiciel » holds the PDF + 2 radios') : bad('D3-files', 'files folder', folderFiles)
  await imp.page.screenshot({ path: `${SHOTS}D3-history.png` })
} catch (e) {
  bad('run', 'threw', e.stack ?? e.message)
}
console.log(findings.length ? `\n${findings.length} finding(s)\n${JSON.stringify(findings, null, 1)}` : '\nALL CLEAR')
writeFileSync(join(SCRATCH, 'pr123-walk-result.json'), JSON.stringify(findings, null, 1))
await browser.close()
