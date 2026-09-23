import { readFileSync } from 'node:fs'
import { join, dirname } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { createRequire } from 'node:module'
const here = dirname(fileURLToPath(import.meta.url)); const pad = process.argv[2]
const { chromium } = createRequire(join(pad, 'package.json'))('playwright-core')
const { totp } = await import(pathToFileURL(join(pad, 'totp.mjs')).href)
const e = JSON.parse(readFileSync(join(here, 'eye-fixtures.json'), 'utf8'))
const b = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await b.newContext({ viewport: { width: 390, height: 844 }, locale: 'fr-FR' })
await ctx.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const p = await ctx.newPage()
await p.goto('http://localhost:3000/appointments'); await p.waitForURL(/\/login/)
await p.fill('input[type=email]', 'salma.benyoussef@cabinet-ibnkhaldoun.tn'); await p.fill('input[type=password]', 'QaAudit2026!y')
await p.click("button:has-text('Se connecter')"); await p.waitForSelector('input[inputmode=numeric]')
await p.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
await p.fill('input[inputmode=numeric]', totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO')); await p.waitForURL((u) => !u.pathname.startsWith('/login'))
for (const [id, name, menu] of [[e.stopPlan, 'stop', /Arrêter le traitement/], [e.draft, 'delete', /Supprimer le traitement/]]) {
  await p.goto(`http://localhost:3000/treatment-plans/${id}`); await p.waitForTimeout(3500)
  await p.getByRole('button', { name: 'Autres actions sur ce devis' }).filter({ visible: true }).first().click()
  await p.getByRole('menuitem', { name: menu }).click()
  const d = p.locator('[role=alertdialog]:visible'); await d.waitFor(); await p.waitForTimeout(500)
  console.log(name, (await d.innerText()).match(/[^.]*libéré[^.]*\./)?.[0])
  await p.screenshot({ path: join(here, 'shots', 'eye', `${name}-dialog-390-v2.png`) })
  await d.getByRole('button', { name: 'Retour' }).click()
}
await b.close()
