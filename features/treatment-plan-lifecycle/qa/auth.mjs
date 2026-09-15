// Mints the two credentials the walk needs: the BFF cookie (what the browser restores) and an API bearer
// token (what the arrange/assert helpers use). A plain-JS twin of `e2e/lib/global-setup.ts`.
//
// ⚠️ Never fetch `/bff/auth/token` from here — that exchange rotates the refresh credential the browser is
// holding, so the next navigation lands on /login. `POST /api/auth/login` mints an independent one.
import crypto from "node:crypto"
import { readFileSync, writeFileSync, mkdirSync } from "node:fs"
import { join, dirname } from "node:path"
import { fileURLToPath } from "node:url"

const HERE = dirname(fileURLToPath(import.meta.url))
const REPO = join(HERE, "..", "..", "..")

export const ORIGIN = process.env.CLINIC_ORIGIN ?? "http://localhost:3000"
export const API = process.env.CLINIC_API ?? "http://localhost:5000/api"

const cred = JSON.parse(readFileSync(join(REPO, "e2e", "credentials.local.json"), "utf8"))
export const ACCOUNT = cred

function totp(secret, skew = 0) {
  const A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
  let bits = ""
  for (const c of secret.replace(/[\s=]/g, "").toUpperCase()) bits += A.indexOf(c).toString(2).padStart(5, "0")
  const bytes = Buffer.from((bits.match(/.{8}/g) ?? []).map((b) => parseInt(b, 2)))
  const counter = Math.floor(Date.now() / 1000 / 30) + skew
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0)
  buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac("sha1", bytes).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, "0")
}

// A TOTP code is single-use, so a retry waits for the NEXT window rather than resending inside this one.
const nextWindow = () =>
  new Promise((r) => setTimeout(r, (30 - (Math.floor(Date.now() / 1000) % 30) + 2) * 1000))

export async function signIn({ name = "" } = {}) {
  // ⚠️ `name` mints a SEPARATE session family. Two browser contexts sharing one storageState is not a
  // shortcut — the refresh exchange ROTATES the credential, and rotation is the theft detection, so the
  // second context silently signs the first out. Measured 2026-09-15: every row after the one that opened a
  // second context landed on /login with a 401, which reads as six product defects.
  const out = join(HERE, ".artifacts")
  mkdirSync(out, { recursive: true })
  const sessionFile = name ? `session-${name}.json` : "session.json"
  const tokenFile = name ? `token-${name}.txt` : "token.txt"

  const bffLogin = async (code) => {
    const res = await fetch(`${ORIGIN}/bff/auth/local-login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: ACCOUNT.email, password: ACCOUNT.password, totpCode: code }),
    })
    return { ok: res.ok, status: res.status, cookies: res.headers.getSetCookie?.() ?? [], body: await res.text() }
  }

  let bff = await bffLogin(totp(ACCOUNT.totpSecret))
  if (!bff.ok) {
    await nextWindow()
    bff = await bffLogin(totp(ACCOUNT.totpSecret))
  }
  if (!bff.ok) {
    throw new Error(
      `BFF sign-in failed: HTTP ${bff.status}. ` +
        (bff.body.trimStart().startsWith("<")
          ? "The server returned HTML — web is down or holding a build error. NOT a credential problem."
          : bff.body),
    )
  }

  const host = new URL(ORIGIN).hostname
  const cookies = bff.cookies
    .map((raw) => {
      const [pair, ...attrs] = raw.split(";").map((s) => s.trim())
      const eq = pair.indexOf("=")
      const flags = new Map(
        attrs.map((a) => {
          const i = a.indexOf("=")
          return i === -1 ? [a.toLowerCase(), ""] : [a.slice(0, i).toLowerCase(), a.slice(i + 1)]
        }),
      )
      return {
        name: pair.slice(0, eq),
        value: pair.slice(eq + 1),
        domain: host,
        path: flags.get("path") || "/",
        expires: flags.has("expires") ? Math.floor(new Date(flags.get("expires")).getTime() / 1000) : -1,
        httpOnly: flags.has("httponly"),
        secure: flags.has("secure"),
        sameSite:
          ({ lax: "Lax", strict: "Strict", none: "None" })[(flags.get("samesite") || "lax").toLowerCase()] ??
          "Lax",
      }
    })
    // An empty value is a DELETION cookie — saving it restores a cookie whose only job is to not exist.
    .filter((c) => c.name && c.value)

  if (!cookies.length) throw new Error("BFF sign-in returned no Set-Cookie — nothing to restore.")
  writeFileSync(join(out, sessionFile), JSON.stringify({ cookies, origins: [] }, null, 2))

  const apiLogin = async (code) => {
    const res = await fetch(`${API}/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: ACCOUNT.email, password: ACCOUNT.password, totpCode: code }),
    })
    return { ok: res.ok, status: res.status, body: await res.text() }
  }

  await nextWindow() // the code above is spent
  let api = await apiLogin(totp(ACCOUNT.totpSecret))
  if (!api.ok) {
    await nextWindow()
    api = await apiLogin(totp(ACCOUNT.totpSecret))
  }
  if (!api.ok) throw new Error(`API sign-in failed: HTTP ${api.status} ${api.body}`)

  const token = JSON.parse(api.body)?.value?.accessToken
  if (!token) throw new Error(`API sign-in returned no accessToken: ${api.body.slice(0, 300)}`)
  writeFileSync(join(out, tokenFile), token)

  return { sessionPath: join(out, sessionFile), token }
}

export function bearer(token) {
  return async (method, path, body) => {
    const res = await fetch(`${API}${path}`, {
      method,
      headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
    const text = await res.text()
    let json
    try {
      json = text ? JSON.parse(text) : null
    } catch {
      json = null
    }
    return { ok: res.ok, status: res.status, json, text }
  }
}
