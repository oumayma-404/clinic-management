import { totp } from './totp.mjs'
const API = 'http://localhost:5099/api'
export async function login() {
  const s = Math.floor(Date.now() / 1000) % 30
  if (s > 25) await new Promise(r => setTimeout(r, (31 - s) * 1000))
  const res = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO') }) })
  const body = await res.json()
  if (!res.ok) throw new Error(`login ${res.status} ${JSON.stringify(body)}`)
  return body
}
if (process.argv[2] === 'keys') login().then(b => console.log(Object.keys(b), typeof b.accessToken))
