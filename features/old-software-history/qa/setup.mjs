// PR #123 QA setup on the throwaway cabinet: admin first use, then the « Import » and « Collègue » secretaries.
import { writeFileSync } from 'node:fs'
import { call, clinicSignIn } from 'file:///C:/dev/clinic-migration/lib.mjs'

const API = 'http://localhost:5095/api'
const PASSWORD = 'Qa-Pr123-Import!2026'
const [adminPassword] = process.argv.slice(2)
const unwrap = d => (d && typeof d === 'object' && 'value' in d ? d.value : d)

const admin = await clinicSignIn(API, { email: 'admin@pr123.test', password: adminPassword, newPassword: PASSWORD })
console.log('admin ready')
const state = { admin: { email: 'admin@pr123.test', password: admin.password, totp: admin.totpSecret } }

for (const [key, email, name] of [['import', 'import@pr123.test', 'Import Apexa'], ['collegue', 'collegue@pr123.test', 'Salma Collègue']]) {
  const created = await call(API, 'POST', '/users', { token: admin.token, json: { email, fullName: name, role: 'secretary' } })
  if (!created.ok) throw new Error(`create ${email}: ${created.status} ${JSON.stringify(created.data)}`)
  const v = unwrap(created.data)
  let password = v.temporaryPassword
  if (v.invitationUrl) {
    const t = decodeURIComponent(new URL(v.invitationUrl).hash.replace(/^#t=/, ''))
    const acc = await call(API, 'POST', '/auth/invitation/accept', { json: { token: t, password: PASSWORD } })
    if (!acc.ok) throw new Error(`accept ${email}: ${acc.status} ${JSON.stringify(acc.data)}`)
    password = PASSWORD
  }
  const r = await clinicSignIn(API, { email, password, newPassword: PASSWORD })
  state[key] = { email, password: r.password, totp: r.totpSecret ?? null }
  console.log(`${email} ready (totp: ${r.totpSecret ? 'yes' : 'no'})`)
}
state.clinicId = (await import('node:child_process')).execFileSync('docker', ['exec', 'clinic-postgres', 'psql', '-U', 'clinic_user', '-d', 'clinic_pr123_qa', '-tAc', 'select "Id" from "Clinics" limit 1']).toString().trim()
writeFileSync(new URL('./pr123-state.json', import.meta.url), JSON.stringify(state, null, 2))
