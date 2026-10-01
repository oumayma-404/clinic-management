// Shared browser harness: a fresh Chrome, a fresh sign-in inside the take, overlays suppressed.
import { chromium } from 'playwright-core'
import { totp, msToFreshWindow } from './totp.mjs'

export const WEB = 'http://localhost:3000'
const EMAIL = 'salma.benyoussef@cabinet-ibnkhaldoun.tn'
const PASSWORD = 'QaAudit2026!y'
const SECRET = '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'

/**
 * A signed-in page. Signs in INSIDE the take — a stored storageState is good for one run, because refresh
 * rotation is the theft detection, and reusing the owner's would sign them out.
 */
export async function openSignedIn({ width = 1440, height = 900, headless = true } = {}) {
  const browser = await chromium.launch({ channel: 'chrome', headless })
  const ctx = await browser.newContext({ viewport: { width, height }, locale: 'fr-FR' })

  // ⚠️ The post-visit review popup swallows every click on the page beneath it. Serving the queue empty is
  // cleaner than pre-snoozing ids we do not know in advance.
  await ctx.route('**/notifications/pending-reviews**', (r) =>
    r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))

  const page = await ctx.newPage()
  const consoleErrors = []
  page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text().slice(0, 300)) })
  page.on('pageerror', (e) => consoleErrors.push('pageerror: ' + String(e).slice(0, 300)))

  await page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' })
  await page.waitForURL(/\/login/, { timeout: 30000 })
  await page.fill('input[type=email]', EMAIL)
  await page.fill('input[type=password]', PASSWORD)
  await page.click("button:has-text('Se connecter')")

  // ⚠️ Wait for a FRESH 30-second window before filling: a code computed seconds before submission expires
  // in flight and the form resets to an empty /login, which reads exactly like a wrong password.
  await page.waitForSelector('input[inputmode=numeric]', { timeout: 30000 })
  await page.waitForTimeout(msToFreshWindow())
  // ⚠️ Do NOT click « Vérifier » — the 6-digit box auto-submits on the last digit.
  await page.fill('input[inputmode=numeric]', totp(SECRET))
  await page.waitForURL((u) => !/\/login/.test(u.toString()), { timeout: 45000 })

  return { browser, ctx, page, consoleErrors }
}

/** Wait for real content rather than for `load` — a skeleton is not a screen. */
export async function settled(page, timeout = 20000) {
  await page.waitForLoadState('networkidle', { timeout }).catch(() => {})
  await page.waitForTimeout(400)
}

/** Poll for a toast's text; sonner may dismiss it before a single waitForSelector lands. */
export async function toastText(page, ms = 9000) {
  const end = Date.now() + ms
  let seen = ''
  while (Date.now() < end) {
    const t = await page.locator('[data-sonner-toast]').allInnerTexts().catch(() => [])
    if (t.length) { seen = t.join(' | '); break }
    await page.waitForTimeout(150)
  }
  return seen
}
