// QA walk — demo-cabinet story 3. Run: node walk-3.mjs <path-to-playwright-core> <shots-dir>
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'

const require = createRequire(import.meta.url)
const { chromium } = require(process.argv[2])
const SHOTS = process.argv[3]
mkdirSync(SHOTS, { recursive: true })
const BASE = 'http://localhost:3099'
const SENTENCE = "Démonstration : rien n'est enregistré. Créez votre cabinet pour essayer avec vos données."

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }

const PNG = join(tmpdir(), 'demo-qa-logo.png')
writeFileSync(PNG, Buffer.from('89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C4890000000D4944415478DA6364F8FFBF1E000500020001F5D1A2E10000000049454E44AE426082', 'hex'))
const CSV = join(tmpdir(), 'demo-qa-patients.csv')
writeFileSync(CSV, 'Prénom;Nom\nRoss;Geller\n')

const writes = [] // every non-GET to the API, across every context
const gets = []
const browser = await chromium.launch({ channel: 'chrome' })
const fresh = async (opts = {}) => {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR', ...opts })
  ctx.on('request', (r) => {
    const u = new URL(r.url())
    if (!u.pathname.includes('/api/') && !u.pathname.startsWith('/bff/')) return
    if (r.method() === 'GET') gets.push(u.pathname)
    else if (r.method() !== 'OPTIONS') writes.push(`${r.method()} ${u.pathname}`)
  })
  return ctx
}
const enter = async (page) => {
  await page.goto(`${BASE}/demo`)
  await page.getByRole('button', { name: 'Entrer dans la démo' }).click()
  await page.waitForURL((u) => !u.pathname.startsWith('/demo'), { timeout: 30000 })
  await page.getByRole('button', { name: 'Mon compte' }).waitFor({ timeout: 45000 })
}
const demoToast = (page) => page.locator('[data-sonner-toast]', { hasText: SENTENCE })
const toastShown = (page) => demoToast(page).first().waitFor({ timeout: 8000 }).then(() => true, () => false)
const writesSince = (mark, re) => writes.slice(mark).filter((w) => re.test(w))

const scenarios = [
  { id: 'A1', run: async () => {
    const one = await fresh(); const p1 = await one.newPage()
    await enter(p1)
    await p1.goto(`${BASE}/securite`)
    const card = p1.locator('div', { has: p1.getByText('Mes appareils', { exact: true }) }).last()
    await p1.getByText('Mes appareils', { exact: true }).waitFor({ timeout: 20000 })
    await p1.waitForTimeout(1500)
    const two = await fresh(); const p2 = await two.newPage()
    await enter(p2)
    await p1.reload(); await p1.getByText('Mes appareils', { exact: true }).waitFor({ timeout: 20000 }); await p1.waitForTimeout(1500)
    const rows = await p1.getByText('Visite de démonstration').count()
    const disconnect = await p1.getByRole('button', { name: 'Déconnecter', exact: true }).count()
    await p1.screenshot({ path: `${SHOTS}/S3-A1-devices-1440.png` })
    rows === 1 && disconnect === 0 ? ok('A1', 'one session listed with two visitors in, no « Déconnecter »')
      : bad('A1', 'devices card wrong', JSON.stringify({ rows, disconnect }))
    void card
    // A2 on the same page
    const before = writes.length
    const activate = p1.getByRole('link', { name: 'Activer maintenant' })
    if (await activate.count()) {
      await activate.click()
      const shown = await toastShown(p1)
      new URL(p1.url()).pathname === '/securite' && shown
        ? ok('A2', '« Activer maintenant » stays on /securite with the demo toast')
        : bad('A2', 'activate not refused in place', JSON.stringify({ url: p1.url(), shown }))
    } else skip('A2', '« Activer maintenant » not rendered (account already enrolled?)')
    void before
    await one.close(); await two.close()
  } },
  { id: 'A3+C1b', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await page.goto(`${BASE}/change-password`)
    await page.waitForTimeout(2500)
    if (new URL(page.url()).pathname !== '/change-password') { bad('A3', 'password page redirected', page.url()); await ctx.close(); return }
    const fields = page.locator('input[type="password"]')
    const n = await fields.count()
    for (let i = 0; i < n; i++) await fields.nth(i).fill(i === 0 ? 'ancienMotDePasse1' : 'NouveauMotDePasse!2026')
    const mark = writes.length
    await page.locator('form button[type="submit"]').first().click()
    const notice = page.locator('[data-demo-refusal]')
    const shown = await notice.waitFor({ timeout: 8000 }).then(() => true, () => false)
    const kept = await fields.nth(0).inputValue()
    const sent = writesSince(mark, /change-password/)
    await page.setViewportSize({ width: 320, height: 640 }); await page.waitForTimeout(300)
    const scrollX = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    await page.screenshot({ path: `${SHOTS}/S3-A3-password-320.png` })
    shown && kept === 'ancienMotDePasse1' && sent.length === 0
      ? ok('A3', `in-form notice, input kept, no request (${n} fields)`)
      : bad('A3', 'password refusal wrong', JSON.stringify({ shown, kept, sent }))
    scrollX <= 0 ? ok('C1b', 'password notice at 320 px: no horizontal scroll') : bad('C1b', 'h-scroll at 320', String(scrollX))
    await ctx.close()
  } },
  { id: 'A4+C1a', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await page.goto(`${BASE}/patients`)
    const mark = writes.length
    await page.getByRole('button', { name: 'Exporter patients' }).click()
    const dialog = page.getByRole('dialog', { name: 'Confirmer votre identité' })
    const open = await dialog.waitFor({ timeout: 10000 }).then(() => true, () => false)
    const notice = await dialog.locator('[data-demo-refusal]').count()
    const pw = await dialog.locator('input[type="password"], input[inputmode="numeric"]').count()
    const close = await dialog.getByRole('button', { name: 'Fermer', exact: true }).count()
    for (const [w, h] of [[390, 844], [1536, 730]]) {
      await page.setViewportSize({ width: w, height: h }); await page.waitForTimeout(300)
      // The dialog's own ✕ is also named « Fermer »; the footer button is the last one.
      const box = await dialog.getByRole('button', { name: 'Fermer', exact: true }).last().boundingBox().catch(() => null)
      const scrollX = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
      await page.screenshot({ path: `${SHOTS}/S3-A4-stepup-${w}x${h}.png` })
      box && box.y + box.height <= h && scrollX <= 0 ? ok('C1a', `${w}×${h}: notice + « Fermer » on screen`) : bad('C1a', `${w}×${h} step-up layout`, JSON.stringify({ box, scrollX }))
    }
    const sent = writesSince(mark, /step-up|export/)
    open && notice === 1 && pw === 0 && close >= 1 && sent.length === 0
      ? ok('A4', 'export: demo notice in place of the code field, no request')
      : bad('A4', 'step-up not refused', JSON.stringify({ open, notice, pw, close, sent }))
    await ctx.close()
  } },
  { id: 'A5+A8', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await page.goto(`${BASE}/settings`)
    await page.getByRole('button', { name: 'Modifier', exact: true }).first().click()
    const input = page.locator('input[type="file"]').first()
    await input.waitFor({ state: 'attached', timeout: 10000 })
    const mark = writes.length
    await input.setInputFiles(PNG)
    const shown = await toastShown(page)
    await page.waitForTimeout(800)
    const sent = writesSince(mark, /logo|clinics/i)
    shown && sent.length === 0 ? ok('A5', 'logo pick: demo toast, nothing sent') : bad('A5', 'logo pick not refused', JSON.stringify({ shown, sent }))

    await page.goto(`${BASE}/settings`)
    const hide = page.getByRole('button', { name: 'Masquer', exact: true })
    await hide.waitFor({ timeout: 20000 }).catch(() => {})
    await page.waitForTimeout(1500)
    if (!(await hide.count())) { skip('A8', '« Mode discret » card not rendered'); await ctx.close(); return }
    await hide.click()
    const t = demoToast(page).first()
    const shown2 = await t.waitFor({ timeout: 8000 }).then(() => true, () => false)
    const type = await t.getAttribute('data-type').catch(() => null)
    await page.waitForTimeout(500)
    const still = await page.getByRole('button', { name: 'Masquer', exact: true }).count()
    shown2 && type !== 'error' && still === 1 ? ok('A8', 'Mode discret: neutral toast, still « Masquer »') : bad('A8', 'Mode discret refusal wrong', JSON.stringify({ shown2, type, still }))
    await ctx.close()
  } },
  { id: 'A6', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await page.goto(`${BASE}/mon-profil`)
    const input = page.locator('input[type="file"]').first()
    const there = await input.waitFor({ state: 'attached', timeout: 15000 }).then(() => true, () => false)
    if (!there) { skip('A6', 'no cachet picker on /mon-profil for this account'); await ctx.close(); return }
    const mark = writes.length
    await input.setInputFiles(PNG)
    const shown = await toastShown(page)
    await page.waitForTimeout(800)
    const sent = writesSince(mark, /doctors/i)
    shown && sent.length === 0 ? ok('A6', 'cachet pick: demo toast, nothing sent') : bad('A6', 'cachet pick not refused', JSON.stringify({ shown, sent }))
    await ctx.close()
  } },
  { id: 'A7', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await page.goto(`${BASE}/patients`)
    await page.getByRole('button', { name: 'Importer des patients depuis un fichier CSV' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.waitFor({ timeout: 10000 })
    const mark = writes.length
    await dialog.locator('input[type="file"]').setInputFiles(CSV)
    const shown = await dialog.locator('[data-demo-refusal]').waitFor({ timeout: 8000 }).then(() => true, () => false)
    const sent = writesSince(mark, /import/)
    await page.screenshot({ path: `${SHOTS}/S3-A7-import-1440.png` })
    shown && sent.length === 0 ? ok('A7', 'CSV pick: notice inside the dialog, nothing sent') : bad('A7', 'import pick not refused', JSON.stringify({ shown, sent }))
    await ctx.close()
  } },
  { id: 'A9', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await page.getByRole('button', { name: /Personnaliser/ }).first().click()
    const sw = page.getByRole('switch').first()
    await sw.waitFor({ timeout: 10000 })
    const before = await sw.getAttribute('aria-checked')
    await sw.click()
    const t = demoToast(page).first()
    const shown = await t.waitFor({ timeout: 8000 }).then(() => true, () => false)
    const type = await t.getAttribute('data-type').catch(() => null)
    await page.waitForTimeout(800)
    const after = await sw.getAttribute('aria-checked')
    shown && type !== 'error' && after === before ? ok('A9', `toggle refused neutrally, switch back to ${before}`) : bad('A9', 'dashboard toggle wrong', JSON.stringify({ shown, type, before, after }))
    await ctx.close()
  } },
  { id: 'A10', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await page.getByRole('button', { name: /Messages/ }).first().click()
    await page.waitForTimeout(2000)
    const first = page.locator('[role="dialog"] li button, [aria-label*="Conversation"] button, [data-conversation]').first()
    if (!(await first.count())) { skip('A10', 'no conversation to open in an empty cabinet (story 4 adds colleagues)'); await ctx.close(); return }
    await first.click()
    const field = page.locator('textarea').last()
    if (!(await field.count())) { skip('A10', 'no composer in the opened panel'); await ctx.close(); return }
    await field.fill('Bonjour Rachel')
    const bubblesBefore = await page.getByText('Bonjour Rachel', { exact: true }).count()
    const mark = writes.length
    await field.press('Enter')
    const shown = await toastShown(page)
    const kept = await field.inputValue()
    const sent = writesSince(mark, /staff-chat/)
    const bubblesAfter = await page.getByText('Bonjour Rachel', { exact: true }).count()
    shown && kept === 'Bonjour Rachel' && sent.length === 0 && bubblesAfter === bubblesBefore
      ? ok('A10', 'send refused before the bubble, text kept') : bad('A10', 'chat send not refused', JSON.stringify({ shown, kept, sent, bubblesBefore, bubblesAfter }))
    await ctx.close()
  } },
]

for (const s of scenarios) {
  try { await s.run() } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]) }
}

// B1 / B2 over everything the walk did
const ALLOWED = /auth\/demo-session|auth\/refresh|auth\/logout|auth\/local-logout|csp-report|clinics\/money\/hide|clinics\/hide-money|dashboard\/preferences/i
const unexpected = writes.filter((w) => !ALLOWED.test(w))
unexpected.length === 0 ? ok('B1', `${writes.length} writes, all expected (${[...new Set(writes)].join(' · ')})`)
  : bad('B1', 'unexpected write requests', JSON.stringify([...new Set(unexpected)]))
const reviews = gets.filter((g) => /pending-reviews/.test(g))
reviews.length === 0 ? ok('B2', 'no pending-reviews read: the popup is withdrawn') : bad('B2', 'popup still polls', String(reviews.length))
skip('D3', 'hub presence — ClinicHubTests.On_The_Demo_A_Visitor_Arriving_Or_Leaving_Signals_Nobody')
skip('D4', 'Fichiers default folders + odontogram multi-tooth need a patient (story 4)')

await browser.close()
console.log('\n' + (findings.filter((f) => f.m).length ? `RED (${findings.filter((f) => f.m).length})` : 'GREEN'))
findings.forEach((f) => console.log(JSON.stringify(f)))
