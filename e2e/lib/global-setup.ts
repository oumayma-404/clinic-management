import { mkdirSync, writeFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { ACCOUNT, API, CLINIC_ID, ORIGIN } from "./env"
import { totp } from "./totp"

/**
 * Signs in once, two ways, before any test runs.
 *
 * **Two credentials, because the product has two front doors and this suite uses both.** The browser talks to
 * Next's BFF, which holds an httpOnly cookie — that is what `storageState` restores, and no amount of
 * `document.cookie` can produce it. The arrange/assert helpers talk to the API directly with a bearer token,
 * because arranging 260-odd preconditions through the UI would be unusably slow and because a *coupled-surface*
 * assertion is far sharper against the wire than against a rendered figure.
 *
 * ⚠️ **`/bff/auth/token` is deliberately not used.** Fetching it from a script rotates the credential the
 * browser is holding, so the very next navigation lands on `/login`. The API token is obtained from
 * `POST /api/auth/login`, which mints an independent one.
 *
 * ⚠️ **A TOTP code is single-use**, so the sign-in retries in the *next* 30-second window rather than
 * immediately — an arithmetically correct code submitted twice in one window is refused, which reads as a bad
 * password.
 *
 * ⚠️ **A 500 from the BFF is not a credential problem.** Next dev serves `/_error` HTML for every route while
 * any file has a build error, `/bff/auth/*` included, and the login page renders that as « Identifiants
 * invalides. » The failure below says so, because that message has sent people resetting a working password.
 */
export default async function globalSetup() {
  const artifacts = join(__dirname, "..", ".artifacts")
  mkdirSync(artifacts, { recursive: true })

  console.log(`\n  e2e → web ${ORIGIN} · api ${API} · clinic ${CLINIC_ID}`)
  console.log(`  account ${ACCOUNT.email}\n`)

  const nextWindow = () =>
    new Promise((r) => setTimeout(r, (30 - (Math.floor(Date.now() / 1000) % 30) + 2) * 1000))

  // ── The browser's cookie, via the BFF ──────────────────────────────────────────────────────────────────
  const bffLogin = async (code: string) => {
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
          ? "The server returned HTML — `npm run dev` is down or holding a build error. This is NOT a credential problem."
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
        expires: flags.has("expires")
          ? Math.floor(new Date(flags.get("expires")!).getTime() / 1000)
          : -1,
        httpOnly: flags.has("httponly"),
        secure: flags.has("secure"),
        sameSite: ({ lax: "Lax", strict: "Strict", none: "None" } as const)[
          (flags.get("samesite") || "lax").toLowerCase() as "lax" | "strict" | "none"
        ] ?? "Lax",
      }
    })
    // An empty value is a *deletion* cookie — the BFF clears `must_change_password` on a normal sign-in, and
    // saving it would restore a cookie whose only job is to not exist.
    .filter((c) => c.name && c.value)

  if (cookies.length === 0) {
    throw new Error("BFF sign-in succeeded but returned no Set-Cookie — nothing to restore.")
  }

  writeFileSync(join(artifacts, "session.json"), JSON.stringify({ cookies, origins: [] }, null, 2))

  // ── The wire's bearer token, via the API ───────────────────────────────────────────────────────────────
  const apiLogin = async (code: string) => {
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

  writeFileSync(join(artifacts, "token.txt"), token)
  console.log("  signed in: browser cookie + api token\n")
}

// Keeps `dirname` honest if this file is ever moved.
void dirname
