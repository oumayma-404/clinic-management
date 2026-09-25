// Browser harness for the treatment-flow walk: a fresh Chrome, a fresh sign-in, the review popup served empty.
import { chromium } from '../../../e2e/node_modules/playwright-core/index.mjs'
import { totp, msToFreshWindow } from '../../devis-fiche-rdv-flexibility/qa/totp-act-onto-devis.mjs'

export const WEB = 'http://localhost:3000'
const EMAIL = 'salma.benyoussef@cabinet-ibnkhaldoun.tn'
const PASSWORD = 'QaAudit2026!y'
const SECRET = '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'

export async function openSignedIn({ width = 1440, height = 900, headless = true } = {}) {
  const browser = await chromium.launch({ channel: 'chrome', headless })
  const ctx = await browser.newContext({ viewport: { width, height }, locale: 'fr-FR' })
  await ctx.route('**/notifications/pending-reviews**', (r) =>
    r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
  const page = await ctx.newPage()
  const consoleErrors = []
  page.on('pageerror', (e) => consoleErrors.push({ url: page.url(), e: String(e).slice(0, 300) }))

  await page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' })
  await page.waitForURL(/\/login/, { timeout: 60000 })
  await page.fill('input[type=email]', EMAIL)
  await page.fill('input[type=password]', PASSWORD)
  await page.click("button:has-text('Se connecter')")
  await page.waitForSelector('input[inputmode=numeric]', { timeout: 30000 })
  await page.waitForTimeout(msToFreshWindow())
  await page.fill('input[inputmode=numeric]', totp(SECRET))
  await page.waitForURL((u) => !/\/login/.test(u.toString()), { timeout: 45000 })
  return { browser, ctx, page, consoleErrors }
}

export async function settled(page, timeout = 25000) {
  await page.waitForLoadState('networkidle', { timeout }).catch(() => {})
  await page.waitForTimeout(500)
}

export async function toastText(page, ms = 9000) {
  const end = Date.now() + ms
  while (Date.now() < end) {
    const t = await page.locator('[data-sonner-toast]').allInnerTexts().catch(() => [])
    if (t.length) return t.join(' | ')
    await page.waitForTimeout(150)
  }
  return ''
}
