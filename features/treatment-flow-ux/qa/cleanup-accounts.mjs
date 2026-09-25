// Deactivates the throwaway « QA Secrétaire » accounts arrange-2.mjs creates. Usage: node cleanup-accounts.mjs
import { totp, msToFreshWindow } from '../../devis-fiche-rdv-flexibility/qa/totp-act-onto-devis.mjs'

const API = 'http://localhost:5000/api'
await new Promise((r) => setTimeout(r, msToFreshWindow()))
const login = await (await fetch(`${API}/auth/login`, {
  method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO') }),
})).json()
const token = login.value?.accessToken ?? login.accessToken
const h = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` }
const page = await (await fetch(`${API}/users?pageSize=200`, { headers: h })).json()
const users = (page.value ?? page).items ?? (page.value ?? page)
for (const u of users.filter((x) => /^qa\.secr\./.test(x.email ?? '') && x.isActive)) {
  const r = await fetch(`${API}/users/${u.id}/status`, { method: 'PUT', headers: h, body: JSON.stringify({ isActive: false, version: u.version }) })
  console.log(u.email, r.status)
}
