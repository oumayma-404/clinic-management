/**
 * The API half of `relay-roundtrip` (clinic-pc-copy D26, FR-10): acts as the cabinet's administrator on the cloud and
 * on the PC de secours. `run.sh` owns the containers, the cut and the database assertions; this file only talks HTTP.
 *
 *   node drive.mjs code <file>                 a pairing code, through the sign-in door (password + code)
 *   node drive.mjs login <cloud|pc>            a session, kept in state.json
 *   node drive.mjs work <cloud|pc> <tag>       a patient, a fiche, a numbered note with a payment
 *   node drive.mjs probe <seconds> <out.json>  one expense on EACH side every 2 s; prints what each answered
 *
 * Environment: CLOUD_API, PC_API, CLINIC_EMAIL, CLINIC_PASSWORD, CLINIC_TOTP_SECRET, RT_STATE (state.json path).
 * ⚠️ The PC serves HTTPS with its own self-signed certificate: run with NODE_TLS_REJECT_UNAUTHORIZED=0.
 * ⚠️ A TOTP code is single-use per 30 s window — every call that spends one waits for a fresh window.
 */
import crypto from "node:crypto"
import fs from "node:fs"

const [cmd, ...args] = process.argv.slice(2)
const API = { cloud: process.env.CLOUD_API, pc: process.env.PC_API }
const EMAIL = process.env.CLINIC_EMAIL
const PASSWORD = process.env.CLINIC_PASSWORD
const SECRET = process.env.CLINIC_TOTP_SECRET
const STATE = process.env.RT_STATE ?? "state.json"
const state = fs.existsSync(STATE) ? JSON.parse(fs.readFileSync(STATE, "utf8")) : {}
const save = () => fs.writeFileSync(STATE, JSON.stringify(state, null, 2))

function totp() {
  const A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
  let bits = ""
  for (const c of SECRET.replace(/[\s=]/g, "").toUpperCase()) bits += A.indexOf(c).toString(2).padStart(5, "0")
  const key = Buffer.from((bits.match(/.{8}/g) ?? []).map((b) => parseInt(b, 2)))
  const counter = Math.floor(Date.now() / 1000 / 30)
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0)
  buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac("sha1", key).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, "0")
}
const freshWindow = () => new Promise((r) => setTimeout(r, (30 - (Math.floor(Date.now() / 1000) % 30) + 2) * 1000))

async function call(side, method, path, body, token = state[side]) {
  const res = await fetch(API[side] + path, {
    method,
    headers: {
      ...(body ? { "Content-Type": "application/json" } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(20000),
  })
  const text = await res.text()
  let json = null
  try { json = text ? JSON.parse(text) : null } catch {}
  return { status: res.status, json: json?.value ?? json, text }
}
const must = (what, r, ok = [200, 201]) => {
  if (!ok.includes(r.status)) throw new Error(`${what}: HTTP ${r.status} ${r.text.slice(0, 400)}`)
  return r.json
}

const localDay = () => new Date(Date.now() + 60 * 60 * 1000).toISOString().slice(0, 10) // Tunis is UTC+1

if (cmd === "code") {
  await freshWindow()
  const r = await call("cloud", "POST", "/auth/relay-pairing-code",
    { email: EMAIL, password: PASSWORD, totpCode: totp(), label: "PC-CI" }, null)
  const code = must("relay-pairing-code", r).code
  fs.writeFileSync(args[0], code)
  console.log("pairing code issued")
} else if (cmd === "login") {
  const side = args[0]
  await freshWindow()
  const r = await call(side, "POST", "/auth/login", { email: EMAIL, password: PASSWORD, totpCode: totp() }, null)
  state[side] = must(`login on the ${side}`, r).accessToken
  save()
  console.log(`signed in on the ${side}`)
} else if (cmd === "work") {
  const [side, tag] = args
  const patient = must("patient", await call(side, "POST", "/patients",
    { firstName: "Roundtrip", lastName: `${tag}-${Date.now()}`, phone: null }))
  const acts = must("catalogue", await call(side, "GET", "/procedure-types?pageSize=200")).items
  const act = acts.find((a) => Number(a.defaultCost) > 0 && (a.defaultSteps ?? []).length === 0)
  const fiche = must("fiche", await call(side, "POST", `/patients/${patient.id}/dental-records`, {
    patientId: patient.id, interventionDate: localDay(), amountPaid: 0, isAdultTeeth: true, notes: [], importantNotes: [],
    acts: [{ procedureTypeId: act.id, procedureName: act.name, cost: 80, unitCost: null, isPerTooth: false,
      toothNumbers: [], ponticToothNumbers: [], resultingCondition: null, surfaces: null, note: null }],
  }))
  const note = must("note", await call(side, "POST", `/invoices/from-dental-record/${fiche.id}`,
    { amount: 50, method: "Cash" }))
  state.notes = [...(state.notes ?? []), { side, tag, number: note.number, patientId: patient.id }]
  save()
  console.log(`${side}: patient, fiche and note ${note.number} with 50 DT paid`)
} else if (cmd === "probe") {
  // « Never both writable » (FR-1): every 2 s, one save on each side, timed. The run's verdict is in run.sh.
  const until = Date.now() + Number(args[0]) * 1000
  const samples = []
  while (Date.now() < until) {
    for (const side of ["cloud", "pc"]) {
      const t = Date.now()
      let r
      try {
        r = await call(side, "POST", "/expenses", {
          expenseDate: new Date().toISOString(), category: "Fournitures", amount: 1, method: "Cash",
          description: `Roundtrip probe ${side} ${t}`,
        })
      } catch (e) {
        r = { status: 0, json: { code: String(e.cause?.code ?? e.name) } }
      }
      samples.push({ t, side, status: r.status, code: r.json?.code ?? null })
    }
    await new Promise((z) => setTimeout(z, 2000))
  }
  fs.writeFileSync(args[1], JSON.stringify(samples, null, 2))
  const ok = (s) => s.status >= 200 && s.status < 300
  for (const side of ["cloud", "pc"]) {
    const mine = samples.filter((s) => s.side === side)
    const accepted = mine.filter(ok)
    console.log(`${side}: ${accepted.length}/${mine.length} accepted · codes ${[...new Set(mine.map((s) => s.code ?? s.status))].join(", ")}`)
  }
} else {
  console.error(`unknown command ${cmd}`)
  process.exit(2)
}
