// QA walk — sms-rappels-tunisie-sms. One launch, every scenario; findings collected, nothing fixed.
// Run: node walk.mjs <scratchpad dir holding node_modules/playwright-core and totp.mjs>
import { createRequire } from 'node:module'
import { execSync } from 'node:child_process'
import path from 'node:path'
import fs from 'node:fs'
import { fileURLToPath, pathToFileURL } from 'node:url'

const SCRATCH = process.argv[2]
const require = createRequire(path.join(SCRATCH, 'package.json'))
const { chromium } = require('playwright-core')
const { totp } = await import(pathToFileURL(path.join(SCRATCH, 'totp.mjs')).href)

const HERE = path.dirname(fileURLToPath(import.meta.url))
const SHOTS = path.join(HERE, 'shots')
fs.mkdirSync(SHOTS, { recursive: true })

const WEB = 'http://localhost:3097'
const CLINIC = 'f64a8a75-94e1-4e08-a743-fa231abe438f'
const DEFAULT = 'Rappel : {patient}, vous avez un rendez-vous le {date} chez {clinic}.'

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, skipped: why }) }

const sql = (q) => execSync(
  `docker exec clinic-postgres psql -U clinic_user -d clinic_management -At -F "|" -c "${q.replace(/"/g, '\\"')}"`,
  { encoding: 'utf8' }).trim()
const settingsRow = () => sql(
  `select coalesce("SmsEnabled"::text,'null'), coalesce("LeadTimeHours",'null'), coalesce("MessageTemplateBody",'null') from "ClinicReminderSettings" where "ClinicId"='${CLINIC}'`)

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
await ctx.route('**/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const page = await ctx.newPage()

// ── Sign in (fresh 30-second window: the API checks spent the previous code) ─────────────────────────
await page.goto(`${WEB}/settings`)
await page.waitForURL(/\/login/, { timeout: 60000 })
await page.fill('input[type=email]', 'salma.benyoussef@cabinet-ibnkhaldoun.tn')
await page.fill('input[type=password]', 'QaAudit2026!y')
await page.click('button:has-text("Se connecter")')
await page.waitForSelector('input[inputmode=numeric]', { timeout: 30000 })
await page.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
await page.fill('input[inputmode=numeric]', totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'))
await page.waitForURL(u => !/\/login/.test(u.toString()), { timeout: 60000 })
console.log('signed in →', page.url())

const card = () => page.locator('#rappels-sms')
const textarea = () => page.locator('#sms-reminder-message')
const save = () => card().getByRole('button', { name: 'Enregistrer', exact: true })
const chip = (name) => card().getByRole('button', { name, exact: true })
const preview = () => card().locator('[aria-labelledby="sms-reminder-preview-label"]')
const count = () => card().locator('#sms-reminder-count')

async function openCard() {
  await page.goto(`${WEB}/settings`)
  await card().waitFor({ timeout: 60000 })
  const expanded = await card().locator('button[aria-expanded]').first().getAttribute('aria-expanded')
  if (expanded !== 'true') await card().locator('button[aria-expanded]').first().click()
  await page.getByLabel('Envoyer un SMS aux patients avant leur rendez-vous').waitFor({ timeout: 30000 })
  await page.waitForFunction(() => /caractères/.test(document.querySelector('#sms-reminder-count')?.textContent ?? '')
    && !/^…/.test(document.querySelector('#sms-reminder-count')?.textContent ?? ''), null, { timeout: 15000 })
}
async function setMessage(value) {
  await textarea().fill(value)
  await page.waitForTimeout(900) // debounce 300 ms + round trip
}
async function toast(re, timeout = 10000) {
  try { await page.getByText(re).first().waitFor({ timeout }); return true } catch { return false }
}

const scenarios = [
  ['A1', async () => {
    await page.goto(`${WEB}/settings`)
    await card().waitFor({ timeout: 60000 })
    const badge = await card().locator('button[aria-expanded] >> text=/Activés|Désactivés/').first().textContent().catch(() => null)
    badge === 'Désactivés' ? ok('A1', 'badge « Désactivés »') : bad('A1', 'badge', String(badge))
    await openCard()
    const sw = await page.getByLabel('Envoyer un SMS aux patients avant leur rendez-vous').getAttribute('aria-checked')
    sw === 'false' ? ok('A1', 'switch off') : bad('A1', 'switch', sw)
    const veille = await chip('La veille').getAttribute('aria-pressed')
    veille === 'true' ? ok('A1', '« La veille » pressed') : bad('A1', 'La veille', veille)
    const value = await textarea().inputValue()
    value === DEFAULT ? ok('A1', 'default wording in the editor') : bad('A1', 'editor value', value)
    const p = await preview().innerText()
    ;/^Rappel : Mohamed Ben Abdallah, vous avez un rendez-vous le \d\d\/\d\d\/\d{4} à 14:30/.test(p)
      ? ok('A1', `preview « ${p} »`) : bad('A1', 'preview', p)
    const c = await count().innerText()
    ;/^\d+ \/ 157 caractères$/.test(c) ? ok('A1', `count « ${c} »`) : bad('A1', 'count', c)
    await page.screenshot({ path: path.join(SHOTS, 'A1-card-1440.png'), fullPage: false })
  }],
  ['B1', async () => {
    await chip('2 h avant').click()
    const pressed = await chip('2 h avant').getAttribute('aria-pressed')
    pressed === 'true' ? ok('B1', '« 2 h avant » pressed') : bad('B1', '2 h avant', pressed)
    const others = ['2 jours avant', '12 h avant', '6 h avant', '3 h avant']
    const disabled = await Promise.all(others.map(n => chip(n).isDisabled()))
    disabled.every(Boolean) ? ok('B1', 'the 4 others disabled at 2') : bad('B1', 'others disabled', JSON.stringify(disabled))
  }],
  ['B4', async () => {
    await setMessage(`${DEFAULT}\nMerci de venir en avance.`)
    const p = await preview().innerText()
    ;/14:30 chez .*\.\nMerci de venir en avance\./s.test(p) || /\nMerci de venir en avance\./.test(p)
      ? ok('B4', 'line break shown in the preview') : bad('B4', 'preview', JSON.stringify(p))
  }],
  ['A2', async () => {
    await textarea().click()
    await textarea().press('Control+End')
    await card().getByRole('button', { name: 'Insérer Acte' }).click()
    const v = await textarea().inputValue()
    v.includes('{acte}') ? ok('A2', 'variable inserted') : bad('A2', 'insert', v)
    // F2 (run 1) — inserted as a word, never glued to the previous one.
    ;/avance\. \{acte\}$/.test(v) ? ok('F2', 'space added before the variable') : bad('F2', 'glued insert', JSON.stringify(v.slice(-30)))
    await page.waitForTimeout(900)
    await page.getByLabel('Envoyer un SMS aux patients avant leur rendez-vous').click()
    await save().click()
    ;(await toast(/Rappels SMS activés/)) ? ok('A2', 'toast « Rappels SMS activés »') : bad('A2', 'no success toast')
    const badge = await card().locator('button[aria-expanded] >> text=/Activés|Désactivés/').first().textContent()
    badge === 'Activés' ? ok('A2', 'badge « Activés »') : bad('A2', 'badge', badge)
    const row = settingsRow()
    const [sms, lead, tpl] = row.split('|')
    sms === 'true' && lead === '24,2' && tpl.includes('{acte}') && tpl.includes('\n')
      ? ok('A2', `SQL ${sms} · ${lead} · template with {acte} and a line break`) : bad('A2', 'SQL', row)
  }],
  ['C2', async () => {
    await openCard()
    const sw = await page.getByLabel('Envoyer un SMS aux patients avant leur rendez-vous').getAttribute('aria-checked')
    const v = await textarea().inputValue()
    const p24 = await chip('La veille').getAttribute('aria-pressed')
    const p2 = await chip('2 h avant').getAttribute('aria-pressed')
    sw === 'true' && v.includes('{acte}') && p24 === 'true' && p2 === 'true'
      ? ok('C2', 'reopened with everything saved') : bad('C2', 'reopen', JSON.stringify({ sw, v, p24, p2 }))
    await save().click()
    const okToast = await toast(/Rappels SMS activés/)
    const conflict = await card().getByText(/modifié|Recharger/).count()
    okToast && conflict === 0 ? ok('C2', 'second save succeeds, no 409') : bad('C2', 'second save', JSON.stringify({ okToast, conflict }))
  }],
  ['B2', async () => {
    const original = await textarea().inputValue()
    // The textarea caps raw input at 157, so the worst case is pushed over with variables rather than letters.
    await setMessage(`${'{acte} '.repeat(10)}{date} ${'x'.repeat(30)}`)
    const c = await count().innerText()
    const n = Number(c.split('/')[0].trim())
    const red = await count().evaluate(e => e.className.includes('text-destructive'))
    const refusal = await card().getByRole('alert').filter({ hasText: /dépasse 157/ }).count()
    const disabled = await save().isDisabled()
    n > 157 && red && refusal > 0 && disabled
      ? ok('B2', `over the limit: « ${c} », refused, save disabled`) : bad('B2', 'over limit', JSON.stringify({ c, red, refusal, disabled }))
    await setMessage(original)
  }],
  ['B3', async () => {
    const original = await textarea().inputValue()
    await setMessage('Bonjour {patient}, a bientot.')
    const refusal = await card().getByRole('alert').filter({ hasText: /doit contenir \{date\}/ }).count()
    const disabled = await save().isDisabled()
    refusal > 0 && disabled ? ok('B3', 'missing {date} refused, save disabled') : bad('B3', 'missing date', JSON.stringify({ refusal, disabled }))
    await setMessage(original)
  }],
  ['B5', async () => {
    await chip('La veille').click(); await chip('2 h avant').click()
    const msg = await card().getByText("Choisissez au moins un moment d'envoi.").count()
    const disabled = await save().isDisabled()
    msg > 0 && disabled ? ok('B5', 'no send time → stated, save disabled') : bad('B5', 'no time', JSON.stringify({ msg, disabled }))
    await chip('La veille').click(); await chip('2 h avant').click()
  }],
  ['C1', async () => {
    for (const [w, h] of [[320, 740], [390, 844], [820, 1180], [1440, 900], [1536, 730]]) {
      await page.setViewportSize({ width: w, height: h })
      await page.waitForTimeout(500)
      await card().scrollIntoViewIfNeeded()
      const over = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
      const saveBox = await save().boundingBox()
      await page.screenshot({ path: path.join(SHOTS, `C1-card-${w}x${h}.png`), fullPage: false })
      over <= 0 ? ok('C1', `${w}×${h} no horizontal overflow`) : bad('C1', `${w}×${h} overflow ${over}px`)
      saveBox && saveBox.x + saveBox.width <= w ? ok('C1', `${w}×${h} Enregistrer inside the screen`) : bad('C1', `${w}×${h} Enregistrer`, JSON.stringify(saveBox))
    }
    await page.setViewportSize({ width: 1440, height: 900 })
  }],
  ['D3', async () => {
    (await page.getByText('En-tête des documents').count()) > 0 ? ok('D3', 'letterhead card still there') : bad('D3', 'letterhead card missing')
  }],
  ['A3', async () => {
    await page.goto(`${WEB}/rappels`)
    await page.getByRole('heading', { name: 'Rappels' }).first().waitFor({ timeout: 60000 })
    const parked = await page.getByText('Bientôt disponible').count()
    const link = await page.getByRole('link', { name: 'Message et horaires' }).count()
    parked === 0 && link > 0 ? ok('A3', 'log screen rendered, link present') : bad('A3', 'rappels', JSON.stringify({ parked, link }))
    // F1 (run 1) — SMS is on (A2), so the empty log must not say no channel is active.
    await page.waitForTimeout(1500)
    const falseClaim = await page.getByText(/Aucun canal n'est actif|Ni SMS ni WhatsApp n'est configuré/).count()
    falseClaim === 0 ? ok('F1', 'no « nothing is active » claim with SMS on') : bad('F1', 'empty state still claims no channel')
    await page.screenshot({ path: path.join(SHOTS, 'A3-rappels-1440.png'), fullPage: false })
  }],
  ['A5', async () => {
    try { await page.getByText('Forfait de rappels').first().waitFor({ timeout: 15000 }) } catch {}
    const title = await page.getByText('Forfait de rappels', { exact: true }).count()
    const line = await page.getByText(/Chaque SMS et chaque message WhatsApp envoyé compte pour un rappel/).count()
    const old = await page.getByText(/ne concerne que les rappels/).count()
    if (title === 0 && line === 0) { skip('A5', 'no forfait section rendered (allowance read 404 or failed) — see screenshot'); return }
    title > 0 && line > 0 && old === 0 ? ok('A5', 'forfait wording updated') : bad('A5', 'forfait wording', JSON.stringify({ title, line, old }))
  }],
  ['D2', async () => {
    const tiles = await page.getByText(/Envoyés|En attente|Échecs|Bloqués/).count()
    tiles >= 4 ? ok('D2', `counters render (${tiles} labels)`) : bad('D2', 'counters', String(tiles))
  }],
  ['A4', async () => {
    await page.getByRole('link', { name: 'Message et horaires' }).click()
    await page.waitForURL(/\/settings#rappels-sms/, { timeout: 30000 })
    try {
      await page.getByLabel('Envoyer un SMS aux patients avant leur rendez-vous').waitFor({ timeout: 30000 })
      ok('A4', 'landed on /settings#rappels-sms with the card expanded')
    } catch { bad('A4', 'card not expanded after the link', page.url()) }
  }],
  ['D1', async () => {
    const before = settingsRow()
    await page.goto(`${WEB}/rappels`)
    await page.locator('button:visible', { hasText: 'Configurer les canaux' }).first().click()
    const sheet = page.getByRole('dialog')
    await sheet.getByRole('heading', { name: 'Canaux de rappel' }).waitFor({ timeout: 15000 })
    await sheet.getByRole('button', { name: 'Enregistrer' }).waitFor({ timeout: 30000 })
    const programmation = await sheet.getByText(/Programmation/).count()
    const smsUrl = await sheet.getByText('URL de la passerelle SMS').count()
    programmation === 0 && smsUrl === 0 ? ok('D1', 'no SMS block, no « Programmation & message »') : bad('D1', 'sheet content', JSON.stringify({ programmation, smsUrl }))
    await page.screenshot({ path: path.join(SHOTS, 'D1-sheet-1440.png'), fullPage: false })
    await sheet.getByRole('button', { name: 'Enregistrer' }).click()
    const saved = await toast(/Paramètres de rappel enregistrés|Échec de l'enregistrement/)
    const failed = await page.getByText(/Échec de l'enregistrement/).count()
    const after = settingsRow()
    saved && failed === 0 && after === before
      ? ok('D1', 'sheet save kept the card\'s settings') : bad('D1', 'sheet save', JSON.stringify({ saved, failed, before, after }))
    await page.keyboard.press('Escape')
  }],
  ['B6', async () => {
    await openCard()
    await card().getByRole('button', { name: 'Message par défaut' }).click()
    const v = await textarea().inputValue()
    v === DEFAULT ? ok('B6', 'default wording restored in the editor') : bad('B6', 'editor', v)
    await page.waitForTimeout(900)
    await save().click()
    await toast(/Rappels SMS activés/)
    const tpl = settingsRow().split('|')[2]
    tpl === 'null' ? ok('B6', 'stored as null (inherits the default)') : bad('B6', 'SQL template', tpl)
  }],
  ['X1', async () => skip('X1', 'needs an appointment on the shared DB (clinical write) — covered by NotificationJobRenderAtSendTests')],
]

for (const [id, run] of scenarios) {
  try { await run() } catch (e) { bad(id, 'threw', e.message.split('\n')[0]) }
}

await browser.close()
console.log('\n── findings ──')
const real = findings.filter(f => !f.skipped)
real.length ? real.forEach(f => console.log(f)) : console.log('ALL CLEAR (skips listed above)')
