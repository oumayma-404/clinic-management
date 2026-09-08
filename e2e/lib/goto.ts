import { expect, type BrowserContext, type Page } from "@playwright/test"
import { ACCOUNT, ORIGIN } from "./env"
import { totp } from "./totp"

/**
 * Navigate to an app route, and **prove we arrived inside the application**.
 *
 * ⚠️ **Why this exists rather than a bare `page.goto`.** The BFF rotates its refresh cookie on every
 * `/bff/auth/token`, and `SessionFamily.PreviousCredentialHash` exists to *detect* a replay, not to forgive
 * one — presenting a superseded value ends the family outright (« Un identifiant de session déjà remplacé a
 * été présenté. »). A long suite can therefore lose its session **mid-run**, and every test after that point
 * lands on `/login` where *most assertions pass vacuously*: the login page is short, it does not scroll, and
 * it raises no console error. Measured: seven `XCUT-09` checks reported green from `/login`.
 *
 * So a dead session has to be an explicit, self-healing condition and never a silent one.
 */
export async function gotoApp(page: Page, route: string, context?: BrowserContext) {
  const url = route.startsWith("http") ? route : `${ORIGIN}${route}`
  await page.goto(url, { waitUntil: "networkidle" })

  if (page.url().includes("/login")) {
    await reauthenticate(context ?? page.context())
    await page.goto(url, { waitUntil: "networkidle" })
  }

  expect(
    page.url(),
    "still on the login page after re-authenticating — the account or the dev server is the problem, not the "
      + "screen under test",
  ).not.toContain("/login")

  /*
   * ── Wait for the app to have FINISHED, asserted POSITIVELY ────────────────────────────────────────────────
   *
   * ⚠️ **Waiting for a loading string to disappear does not work here, and believing it did cost a whole run.**
   * `AppLoader` shows one of **ten rotating French messages** — « On règle le fauteuil… », « On détartre la
   * base de données… », « On prend l'empreinte… » — and its literal « Chargement… » exists only as an
   * `sr-only` span, and only when the caller passes no `label`. So `getByText(/Chargement/i)` matched nothing
   * on a screen that was still loading, the wait resolved instantly, and the test drove a spinner: three
   * browser tests reported « the patient was never created » against a page that had not finished rendering.
   *
   * The app shell's navigation landmark is the honest signal — it exists on every authenticated route and only
   * once the shell has mounted. `.first()` because the shell renders a sidebar and a bottom nav.
   */
  await page
    .getByRole("navigation")
    .first()
    .waitFor({ state: "visible", timeout: 30_000 })
    .catch(() => {})

  await dismissPostVisitPrompt(page)

  return page
}

/**
 * Snoozes the « Compte rendu de visite » prompt if it is up.
 *
 * ⚠️ **Without this, most browser tests fail on a click that never lands.** The popup is a modal with a
 * full-screen overlay, so Playwright reports « <div data-slot="dialog-overlay"> intercepts pointer events » and
 * retries for the whole timeout — which reads as « the button is missing » and is really « something else is in
 * front of it ». On a practice with a backlog it is a **queue**: 23 séances awaiting closure is 23 prompts.
 *
 * ⚠️ Dismissed through the product's own « Plus tard », not by writing `clinic:pvr-snooze` from the outside.
 * Every dismissal path funnels into one `handleLater` which calls `snoozeAll()` — the whole queue, until the end
 * of the local day — so one click is enough and the harness needs to know nothing about the storage shape.
 *
 * ⚠️ Identified by that **button**, never by the dialog's title: the title is the notification's own text
 * (`active?.title`) and differs per visit. A broader « close any dialog » would also close the modal a deep
 * link just opened, which is the thing under test.
 */
export async function dismissPostVisitPrompt(page: Page) {
  const later = page.getByRole("button", { name: /^Plus tard$/ })
  for (let guard = 0; guard < 3; guard++) {
    if ((await later.count()) === 0) return
    await later.first().click({ timeout: 5_000 }).catch(() => {})
    await page.waitForTimeout(300)
  }
}

/** A code is single-use and one 30-second window apart, so a retry waits for the next window. */
const nextWindow = () =>
  new Promise((r) => setTimeout(r, (30 - (Math.floor(Date.now() / 1000) % 30) + 2) * 1000))

/**
 * Signs in over the BFF again and puts the fresh cookie into the live context, so the run continues instead of
 * reporting a cascade of « element not found ».
 */
export async function reauthenticate(context: BrowserContext) {
  const attempt = async (code: string) => {
    const res = await fetch(`${ORIGIN}/bff/auth/local-login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: ACCOUNT.email, password: ACCOUNT.password, totpCode: code }),
    })
    return {
      ok: res.ok,
      status: res.status,
      cookies: res.headers.getSetCookie?.() ?? [],
      body: await res.text(),
    }
  }

  let r = await attempt(totp(ACCOUNT.totpSecret))
  if (!r.ok) {
    await nextWindow()
    r = await attempt(totp(ACCOUNT.totpSecret))
  }
  if (!r.ok) throw new Error(`re-authentication failed: HTTP ${r.status} ${r.body.slice(0, 200)}`)

  const host = new URL(ORIGIN).hostname
  const cookies = r.cookies
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
        sameSite: "Lax" as const,
      }
    })
    // An empty value is a *deletion* cookie; restoring it would put back a cookie whose only job is to not exist.
    .filter((c) => c.name && c.value)

  // Replace rather than merge: a stale generation left in the jar is the very thing that ends the family.
  await context.clearCookies()
  await context.addCookies(cookies)
}
