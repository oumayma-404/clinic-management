// A signed-in Chrome left open on séance 2's fiche of the rebuilt case, for the owner to test by hand.
import { readFileSync } from 'node:fs'
import { chromium } from 'playwright-core'
import { totp, msToFreshWindow } from './totp.mjs'

const f = JSON.parse(readFileSync(new URL('./fixtures.json', import.meta.url), 'utf8'))
const WEB = 'http://localhost:3000'

// The window's own size, never an emulated viewport: a 900-tall viewport in a 730-tall window hides modal footers.
const browser = await chromium.launch({ channel: 'chrome', headless: false, args: ['--start-maximized'] })
const ctx = await browser.newContext({ viewport: null, locale: 'fr-FR' })
const page = await ctx.newPage()

await page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' })
await page.waitForURL(/\/login/, { timeout: 30000 })
await page.fill('input[type=email]', 'salma.benyoussef@cabinet-ibnkhaldoun.tn')
await page.fill('input[type=password]', 'QaAudit2026!y')
await page.click("button:has-text('Se connecter')")
await page.waitForSelector('input[inputmode=numeric]', { timeout: 30000 })
await page.waitForTimeout(msToFreshWindow())
await page.fill('input[inputmode=numeric]', totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'))
await page.waitForURL((u) => !/\/login/.test(u.toString()), { timeout: 45000 })

await page.goto(`${WEB}/patients/${f.main.patient}?addRecord=1&appointmentId=${f.main.visit2}`,
  { waitUntil: 'domcontentloaded', timeout: 90000 })
console.log('READY', f.main.patient)

// Stay open until the owner closes the window.
await new Promise((resolve) => browser.on('disconnected', resolve))
