// PR #123 QA — the wire-level rows (B2, D1, D2) against the test API on :5095. Run AFTER walk.mjs: it spends the
// address bucket on purpose. Usage: node api-checks.mjs <scratchpadDir>
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

const API = 'http://localhost:5095/api'
const S = JSON.parse(readFileSync(join(process.argv[2], 'pr123-state.json'), 'utf8'))
const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const sleep = ms => new Promise(r => setTimeout(r, ms))
const login = async w => {
  const r = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: w.email, password: w.password }) })
  const d = await r.json()
  return (d.value ?? d).accessToken
}
const get = (token, path = '/patients?page=1&pageSize=1') => fetch(API + path, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
async function burst(token, n) {
  const res = await Promise.all(Array.from({ length: n }, () => get(token)))
  const by = {}
  for (const r of res) by[r.status] = (by[r.status] ?? 0) + 1
  const refused = res.find(r => r.status === 429)
  return { by, message: refused ? await refused.text() : '' }
}

const imp = await login(S.import)
const col = await login(S.collegue)
console.log('  (waiting 61 s so the shared minute starts empty)')
await sleep(61_000)

// B2 — a forged token naming the import account: signature replaced, so the limiter must not believe it.
const forged = imp.slice(0, imp.lastIndexOf('.')) + '.' + 'A'.repeat(43)
const f = await burst(forged, 150)
const fPassed = 150 - (f.by[429] ?? 0)
fPassed <= 120 && f.by[429] > 0
  ? ok('B2', `forged token: ${fPassed} let through (all ${JSON.stringify(f.by)}), the rest 429 — the address bucket, not the boost`)
  : bad('B2', 'forged token was not held to the ordinary limit', JSON.stringify(f.by))

const colNow = (await get(col)).status
colNow === 429
  ? ok('B2-shared', 'right after, the colleague is refused too: the forged calls spent the cabinet\'s shared minute')
  : bad('B2-shared', 'colleague not refused after the forged burst', String(colNow))

// 10 at a time: 150 at once exhausts the shared local Postgres (« too many clients »), an environment limit.
const i = { by: {} }
for (let k = 0; k < 15; k++) for (const [s, n] of Object.entries((await burst(imp, 10)).by)) i.by[s] = (i.by[s] ?? 0) + n
;(i.by[200] ?? 0) === 150
  ? ok('A4-wire', 'meanwhile the import account: 150/150 accepted — its own bucket')
  : bad('A4-wire', 'import account refused while the cabinet was at its limit', JSON.stringify(i.by))

const health = await fetch('http://localhost:5095/health')
health.status === 200 ? ok('D2', '/health 200 while the cabinet is at its limit') : bad('D2', '/health', String(health.status))

console.log('  (waiting 61 s again)')
await sleep(61_000)
const c = await burst(col, 150)
const cOk = c.by[200] ?? 0
cOk >= 110 && cOk <= 120 && c.by[429] > 0 && /Trop de tentatives/.test(c.message)
  ? ok('D1', `colleague burst: ${cOk} accepted, ${c.by[429]} × 429 « ${JSON.parse(c.message).error ?? c.message} »`)
  : bad('D1', 'ordinary limit', `${JSON.stringify(c.by)} · ${c.message}`)

console.log(findings.length ? `\n${findings.length} finding(s)` : '\nALL CLEAR')
