/**
 * Bootstraps a **fresh** database into a state this suite can run against, and prints the env vars to run it
 * with. Written for CI, usable by hand against a throwaway database.
 *
 *   node bootstrap.mjs --api http://localhost:5000/api
 *
 * ⚠️ **It expects the account not to exist yet.** A clinic and its first administrator come from the product's
 * own `provision-clinic` verb (run by the caller, since it needs the API's connection string, not its HTTP
 * port), and everything after that is the ordinary first-sign-in walk a real administrator does:
 *
 *   provision-clinic  →  a one-time password, and MustChangePassword set
 *          ↓
 *   POST /auth/totp/enrol {email, password}            →  the secret to scan
 *   POST /auth/totp/enrol {email, password, totpCode}  →  confirmed, recovery codes returned ONCE
 *          ↓
 *   POST /auth/login                                   →  403 « ce compte doit changer son mot de passe »
 *   POST /auth/change-password                         →  the password the suite will use
 *          ↓
 *   POST /procedure-types/initialize-defaults          →  the catalogue every scenario picks acts from
 *
 * ⚠️ **The catalogue step is not optional.** Every scenario resolves acts by NAME — « Couronne / bridge (par
 * élément) » for the 3-séance protocol, « Couronne sur implant » for the 90-day interval on step 1,
 * « Gingivectomie » for the legacy no-interval shape — and `procedureNamed` fails loudly rather than skipping,
 * because a suite that quietly tests nothing is the outcome all of this exists to prevent.
 *
 * ⚠️ **A TOTP code is single-use and the auth endpoints are rate-limited** (a tight per-account window plus a
 * per-address ceiling). So every step here waits for a fresh 30-second window rather than retrying inside one,
 * and a 429 is honoured rather than hammered.
 */
import crypto from "node:crypto"

const arg = (name, fallback) => {
  const i = process.argv.indexOf(`--${name}`)
  return i > -1 && process.argv[i + 1] ? process.argv[i + 1] : fallback
}

const API = arg("api", process.env.CLINIC_API ?? "http://localhost:5000/api")
const EMAIL = arg("email", process.env.CLINIC_EMAIL ?? "e2e.runner@cabinet-e2e.tn") // not a secret
const TEMP_PASSWORD = arg("temp-password", process.env.CLINIC_TEMP_PASSWORD ?? "")
/*
 * ⚠️ **Generated, never a constant.** A hard-coded default would give every CI-bootstrapped clinic the same
 * known administrator password, committed to a git history that outlives any one run — and this repository's
 * own convention is that a secret does not enter it (`appsettings.json`: « SECRET — do NOT commit a value
 * here »). The value is printed and written to `$GITHUB_ENV`, so the run that needs it has it and nothing else
 * does. `PasswordPolicy` wants 12+ characters with mixed classes, hence the fixed prefix.
 */
const PASSWORD =
  arg("password", process.env.CLINIC_PASSWORD) ??
  `E2e!${crypto.randomBytes(9).toString("base64url")}aA1`

if (!TEMP_PASSWORD) {
  console.error(
    "the one-time password from `provision-clinic` is required:\n" +
      "  dotnet run --project api/ClinicManagement.API -- provision-clinic \\\n" +
      '      --name "Cabinet E2E" --admin-email e2e.runner@cabinet-e2e.tn --admin-name "Dr E2E"\n' +
      "  node bootstrap.mjs --temp-password '<the printed password>'",
  )
  process.exit(1)
}

function totp(secret) {
  const A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
  let bits = ""
  for (const c of secret.replace(/[\s=]/g, "").toUpperCase()) bits += A.indexOf(c).toString(2).padStart(5, "0")
  const bytes = Buffer.from((bits.match(/.{8}/g) ?? []).map((b) => parseInt(b, 2)))
  const counter = Math.floor(Date.now() / 1000 / 30)
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0)
  buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac("sha1", bytes).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, "0")
}

/** Waits for the next 30-second TOTP window — a code already accepted is refused inside the same one. */
const nextWindow = () =>
  new Promise((r) => setTimeout(r, (30 - (Math.floor(Date.now() / 1000) % 30) + 2) * 1000))

async function post(path, body, token) {
  for (let attempt = 0; attempt < 4; attempt++) {
    const res = await fetch(`${API}${path}`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify(body ?? {}),
    })
    if (res.status !== 429) {
      const text = await res.text()
      let json = null
      try {
        json = text ? JSON.parse(text) : null
      } catch {
        /* an HTML body is a server that is unwell, and `text` says so */
      }
      return { status: res.status, json, text }
    }
    const retryAfter = Number(res.headers.get("retry-after"))
    const wait = Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter * 1000 : 30_000
    console.log(`  429 — waiting ${Math.round(wait / 1000)}s (the limiter is the authority on how long)`)
    await new Promise((r) => setTimeout(r, wait))
  }
  throw new Error(`${path}: still rate-limited after four attempts`)
}

const die = (what, r) => {
  throw new Error(`${what} failed: HTTP ${r.status} ${(r.text ?? "").slice(0, 400)}`)
}

console.log(`bootstrap → ${API}\n  account ${EMAIL}`)

// ── 1. Enrol the second factor ─────────────────────────────────────────────────────────────────────────────
// Anonymous by necessity: an account told « enrol first » has no session, that being the point.
let r = await post("/auth/totp/enrol", { email: EMAIL, password: TEMP_PASSWORD })
if (r.status !== 200) die("totp/enrol (step 1)", r)

const enrolment = r.json?.value ?? r.json
// The secret arrives either bare or inside an `otpauth://…?secret=` URI, depending on the build.
const secret =
  enrolment?.secret ??
  enrolment?.sharedKey ??
  (enrolment?.otpauthUri ?? enrolment?.uri ?? "").match(/secret=([A-Z2-7]+)/i)?.[1]
if (!secret) throw new Error(`no TOTP secret in the enrolment response: ${JSON.stringify(enrolment).slice(0, 300)}`)

/*
 * The API returns the secret **grouped in fours for a human to type** (« 44JL RTMW MGAV … »). `totp()` strips
 * whitespace so either form works here, but an env var carrying spaces has to be quoted by every consumer, and
 * the one that forgets fails with an arithmetically-correct-looking code. Normalised once, at the source.
 */
const SECRET = secret.replace(/\s/g, "").toUpperCase()
console.log(`  secret issued (${SECRET.length} chars, normalised from ${secret.length})`)

await nextWindow()
r = await post("/auth/totp/enrol", { email: EMAIL, password: TEMP_PASSWORD, totpCode: totp(SECRET) })
if (r.status !== 200) die("totp/enrol (step 2)", r)
const codes = (r.json?.value ?? r.json)?.recoveryCodes ?? []
console.log(`  second factor confirmed; ${codes.length} recovery codes issued (not stored)`)

// ── 2. Spend the one-time password ─────────────────────────────────────────────────────────────────────────
await nextWindow()
r = await post("/auth/login", { email: EMAIL, password: TEMP_PASSWORD, totpCode: totp(SECRET) })
if (r.status !== 200) die("login with the one-time password", r)
let token = (r.json?.value ?? r.json)?.accessToken
if (!token) throw new Error("login returned no accessToken")

r = await post(
  "/auth/change-password",
  { currentPassword: TEMP_PASSWORD, newPassword: PASSWORD, confirmPassword: PASSWORD },
  token,
)
if (r.status !== 200) die("change-password", r)
console.log("  password set")

await nextWindow()
r = await post("/auth/login", { email: EMAIL, password: PASSWORD, totpCode: totp(SECRET) })
if (r.status !== 200) die("login with the new password", r)
const session = r.json?.value ?? r.json
token = session.accessToken
console.log("  signed in")

// ── 3. The catalogue ──────────────────────────────────────────────────────────────────────────────────────
r = await post("/procedure-types/initialize-defaults", {}, token)
if (r.status !== 200 && r.status !== 201) die("procedure-types/initialize-defaults", r)

const list = await fetch(`${API}/procedure-types?includeInactive=true&pageSize=200`, {
  headers: { Authorization: `Bearer ${token}` },
})
const acts = (await list.json()).items ?? []
console.log(`  catalogue seeded: ${acts.length} acts`)

// Fail here rather than let 60-odd tests fail one by one on a missing act.
const required = [
  "Consultation / examen bucco-dentaire",
  "Détartrage",
  "Coiffage pulpaire",
  "Couronne / bridge (par élément)",
  "Couronne sur implant",
  "Gingivectomie",
]
const missing = required.filter((n) => !acts.some((a) => a.name === n))
if (missing.length) {
  throw new Error(
    `the seeded catalogue is missing acts every scenario resolves by name: ${missing.join(" · ")}\n` +
      `present: ${acts.map((a) => a.name).join(" · ")}`,
  )
}

// The clinic id is informational — membership is resolved server-side from the token — but printing it makes a
// run against the wrong database obvious in the first line of the log.
const clinicId = JSON.parse(Buffer.from(token.split(".")[1], "base64").toString())["clinic_id"]

console.log("\n── ready ──")
console.log(`CLINIC_EMAIL=${EMAIL}`)
console.log(`CLINIC_PASSWORD=${PASSWORD}`)
console.log(`CLINIC_TOTP_SECRET=${SECRET}`)
console.log(`CLINIC_ID=${clinicId}`)

// Machine-readable for CI: `node bootstrap.mjs … >> "$GITHUB_ENV"` would also pick up the log lines, so the
// env block is written separately and deliberately.
if (process.env.GITHUB_ENV) {
  const { appendFileSync } = await import("node:fs")
  appendFileSync(
    process.env.GITHUB_ENV,
    `CLINIC_EMAIL=${EMAIL}\nCLINIC_PASSWORD=${PASSWORD}\nCLINIC_TOTP_SECRET=${secret}\nCLINIC_ID=${clinicId}\n`,
  )
  console.log("(written to $GITHUB_ENV)")
}
