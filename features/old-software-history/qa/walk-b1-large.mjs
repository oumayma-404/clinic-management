// QA re-drive of B1 (plan.md) with a batch large enough to cross the ordinary limit. Run 3 found batch B (15 dossiers)
// now finishes in ~27 s with under 120 calls in all — the 6-lane page makes fewer calls — so B1 never met a 429.
// Batch H = 60 dossiers + 120 radios, imported by the colleague (no boost, 120 / 60 s per address) at 390 px.
// Usage: node walk-b1-large.mjs <scratchpadDir> <runLabel>
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'
import { mkdirSync, readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const SCRATCH = process.argv[2]
const { chromium } = createRequire(join(SCRATCH, 'x.js'))('playwright-core')
const API = 'http://localhost:5095/api'
const WEB = 'http://localhost:3095'
const DATA = join(SCRATCH, 'pr123-data')
const S = JSON.parse(readFileSync(join(SCRATCH, 'pr123-state.json'), 'utf8'))
const SHOTS = fileURLToPath(new URL(`./shots/${process.argv[3] ?? 'run'}/`, import.meta.url))
mkdirSync(SHOTS, { recursive: true })
const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const sleep = ms => new Promise(r => setTimeout(r, ms))
const sql = q => execFileSync('docker', ['exec', 'clinic-postgres', 'psql', '-U', 'clinic_user', '-d', 'clinic_pr123_qa', '-tAc', q]).toString().trim()
const q = s => `'${s.replace(/'/g, "''")}'`

const browser = await chromium.launch({ channel: 'chrome', headless: true })
try {
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 } })
  await ctx.route('**/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
  const page = await ctx.newPage()
  page.setDefaultTimeout(60_000)
  let r429 = 0
  page.on('response', r => { if (r.url().startsWith(API) && r.status() === 429) r429++ })
  await page.goto(`${WEB}/login`, { timeout: 180_000 })
  await page.fill('input[type=email]', S.collegue.email)
  await page.fill('input[type=password]', S.collegue.password)
  await page.click('button:has-text("Se connecter")')
  await page.waitForURL(u => !u.pathname.includes('/login'), { timeout: 120_000 })

  await page.goto(`${WEB}/import-ancien-logiciel`, { timeout: 180_000 })
  const inputs = page.locator('input[type=file][webkitdirectory]')
  await inputs.nth(0).waitFor({ state: 'attached' })
  await inputs.nth(0).setInputFiles(join(DATA, 'H', 'dossiers-H'))
  await inputs.nth(1).setInputFiles(join(DATA, 'H', 'radios-H'))
  await page.getByRole('button', { name: 'Analyser' }).click()
  await page.getByText('Patients à créer', { exact: true }).waitFor({ timeout: 180_000 })

  const login = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: S.collegue.email, password: S.collegue.password }) })
  const burstToken = ((d) => (d.value ?? d).accessToken)(await login.json())
  await page.getByRole('button', { name: 'Importer', exact: true }).click()
  // Arranged 429: a second caller spends the address's minute while the page imports (the page alone stays under it).
  const burst = (async () => {
    await sleep(4000)
    await Promise.all(Array.from({ length: 10 }, async () => {
      for (let i = 0; i < 13; i++) await fetch(`${API}/patients?page=1&pageSize=1`, { headers: { Authorization: `Bearer ${burstToken}` } }).catch(() => {})
    }))
  })()
  const footer = page.locator('div.sticky p[role=status]').first()
  const started = Date.now()
  let paused = null
  let text = ''
  for (;;) {
    await sleep(300)
    text = await footer.innerText().catch(() => '')
    if (!paused && /Pause de/.test(text)) {
      paused = text
      await page.screenshot({ path: `${SHOTS}B1L-pause-390.png` }).catch(() => {})
    }
    const again = page.getByRole('button', { name: "Relancer l'import", exact: true })
    if ((await again.isVisible().catch(() => false)) && (await again.isEnabled().catch(() => false))) break
    if (Date.now() - started > 20 * 60_000) { text = `TIMEOUT: ${text}`; break }
  }
  await burst
  const seconds = Math.round((Date.now() - started) / 1000)
  const pairs = readdirSync(join(DATA, 'H', 'dossiers-H')).filter(f => f.endsWith('.pdf')).map(f => {
    const w = f.replace(/^\d+-/, '').replace(/\.pdf$/, '').split(' ')
    return [w[0], w.slice(1).join(' ')]
  })
  const where = `("FirstName","LastName") in (${pairs.map(([f, l]) => `(${q(f)},${q(l)})`).join(',')})`
  const pats = Number(sql(`select count(*) from "Patients" where "ClinicId"='${S.clinicId}' and ${where}`))
  const files = Number(sql(`select count(*) from "PatientFiles" f join "Patients" p on p."Id"=f."PatientId" where p."ClinicId"='${S.clinicId}' and ${where.replace(/"FirstName"/, 'p."FirstName"').replace(/"LastName"/, 'p."LastName"')}`))
  const failed = await page.locator('li', { hasText: /Incomplet ·|Échec/ }).count()
  paused && r429 > 0 && text.trim() === 'Import terminé.' && pats === 60 && files === 180 && failed === 0
    ? ok('B1-large', `paused (« ${paused} »), ${r429} × 429 absorbed, « Import terminé. » in ${seconds} s · 60 patients · 180 files · 0 failed row`)
    : bad('B1-large', 'unboosted large run', JSON.stringify({ paused, r429, text, pats, files, failed, seconds }))
  await page.screenshot({ path: `${SHOTS}B1L-done-390.png` })
} catch (error) {
  bad('B1-large', 'threw', error.stack ?? String(error))
} finally {
  await browser.close()
}
findings.length ? (console.log(`\n${findings.length} finding(s):`), findings.forEach(f => console.log(f))) : console.log('\nALL CLEAR')
