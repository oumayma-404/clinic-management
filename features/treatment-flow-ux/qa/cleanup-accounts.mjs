// Deactivates the throwaway « QA Secrétaire » accounts arrange-2.mjs creates. Usage: node cleanup-accounts.mjs
import { totp, msToFreshWindow } from '../../devis-fiche-rdv-flexibility/qa/totp-act-onto-devis.mjs'

const API = 'http://localhost:5000/api'
let token = null
for (let attempt = 0; attempt < 2 && !token; attempt++) {
  await new Promise((r) => setTimeout(r, msToFreshWindow()))
  const r = await fetch(`${API}/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO') }),
  })
  const text = await r.text()
  let j = null
  try { j = JSON.parse(text) } catch {}
  token = j?.value?.accessToken ?? j?.accessToken ?? null
  if (!token) console.log(`login ${r.status} ${text.slice(0, 160)}`)
}
if (!token) process.exit(1)
const h = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` }
const page = await (await fetch(`${API}/users?pageSize=200`, { headers: h })).json()
const users = (page.value ?? page).items ?? (page.value ?? page)
for (const u of users.filter((x) => /^qa\.secr\./.test(x.email ?? '') && x.isActive)) {
  const r = await fetch(`${API}/users/${u.id}/status`, { method: 'PUT', headers: h, body: JSON.stringify({ isActive: false, version: u.version }) })
  console.log(u.email, r.status)
}
