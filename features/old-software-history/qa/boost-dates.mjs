// PR #123 QA — B3/B4: the boost's end date, on a SECOND short-lived API instance (:5094, same throwaway DB).
// B3: an end date 8 days away is refused at startup. B4: an end date ~90 s away switches the boost off by itself.
// Usage: node boost-dates.mjs <scratchpadDir>
import { execFileSync, spawn } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

const SCRATCH = process.argv[2]
const S = JSON.parse(readFileSync(join(SCRATCH, 'pr123-state.json'), 'utf8'))
const API = 'http://localhost:5094/api'
const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const sleep = ms => new Promise(r => setTimeout(r, ms))

function start(until) {
  const child = spawn('bash', [join(SCRATCH, 'pr123-api.sh'), '5094', until], { stdio: ['ignore', 'pipe', 'pipe'] })
  let log = ''
  child.stdout.on('data', d => { log += d })
  child.stderr.on('data', d => { log += d })
  return { child, log: () => log }
}
async function up() {
  for (let i = 0; i < 60; i++) {
    try { if ((await fetch('http://localhost:5094/health')).status) return true } catch {}
    await sleep(2000)
  }
  return false
}
async function boostFor() {
  const r = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: S.import.email, password: S.import.password }) })
  const t = ((d) => (d.value ?? d).accessToken)(await r.json())
  return (await (await fetch(`${API}/meta/rate-boost`, { headers: { Authorization: `Bearer ${t}` } })).json()).permitPerMinute
}
async function stop(p) {
  p.child.kill()
  // Killing the bash stub does not end dotnet on Windows: stop the process on :5094, only if it is this build.
  execFileSync('powershell', ['-NoProfile', '-Command',
    "Get-NetTCPConnection -LocalPort 5094 -State Listen -ErrorAction SilentlyContinue | ForEach-Object { $p = Get-CimInstance Win32_Process -Filter \"ProcessId=$($_.OwningProcess)\"; if ($p.CommandLine -match 'ClinicManagement.API.dll' -and (Get-Process -Id $_.OwningProcess).Path -match 'dotnet') { Stop-Process -Id $_.OwningProcess -Force } }"])
  for (let i = 0; i < 15; i++) { try { await fetch('http://localhost:5094/health'); await sleep(1000) } catch { return } }
}
const iso = ms => new Date(Date.now() + ms).toISOString().replace(/\.\d+Z$/, 'Z')

// B3
const far = start(iso(8 * 24 * 3600_000))
;(await up()) || bad('B3', 'instance did not start')
const farLine = far.log().split('\n').find(l => /Rate-limit boost/.test(l)) ?? ''
const farBoost = await boostFor()
;/NOT applied: .*more than 7 days away/.test(farLine) && farBoost === null
  ? ok('B3', `refused at startup: « ${farLine.replace(/^.*?WRN\]\s*/, '').trim()} » · rate-boost answers null`)
  : bad('B3', 'an 8-day boost', `${farLine} · rate-boost ${farBoost}`)
await stop(far)

// B4
const soonAt = Date.now() + 90_000
const soon = start(iso(90_000))
;(await up()) || bad('B4', 'instance did not start')
const before = await boostFor()
const wait = soonAt - Date.now() + 5_000
console.log(`  (waiting ${Math.round(wait / 1000)} s for the end date)`)
await sleep(Math.max(0, wait))
const after = await boostFor()
before === 600 && after === null
  ? ok('B4', 'boost 600 before its end date, null after — same process, no restart')
  : bad('B4', 'end date', `before ${before} · after ${after}`)
await stop(soon)

console.log(findings.length ? `\n${findings.length} finding(s)` : '\nALL CLEAR')
