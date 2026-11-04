// QA walk — demo-cabinet story 2. Run: node walk-2.mjs <path-to-playwright-core> <shots-dir>
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'

const require = createRequire(import.meta.url)
const { chromium } = require(process.argv[2])
const SHOTS = process.argv[3]
mkdirSync(SHOTS, { recursive: true })
const BASE = 'http://localhost:3099'
const API = 'http://localhost:5099/api'
const SENTENCE = "Démonstration : rien n'est enregistré. Créez votre cabinet pour essayer avec vos données."
const SIGNUP = 'https://app.apexa.tn/signup'

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }

const browser = await chromium.launch({ channel: 'chrome' })
const fresh = (opts = {}) => browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR', ...opts })
const enter = async (page) => {
  await page.goto(`${BASE}/demo`)
  await page.getByRole('button', { name: 'Entrer dans la démo' }).click()
  await page.waitForURL((u) => !u.pathname.startsWith('/demo'), { timeout: 30000 })
  await page.getByRole('button', { name: 'Mon compte' }).waitFor({ timeout: 45000 })
}
const band = (page) => page.getByRole('complementary', { name: 'Démonstration' })
const demoToasts = (page) => page.locator('[data-sonner-toast]', { hasText: SENTENCE })

const scenarios = [
  { id: 'A1+A2+C1', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    const b = band(page)
    const text = (await b.innerText()).replace(/\s+/g, ' ').trim()
    const link = b.getByRole('link')
    const href = await link.getAttribute('href'); const target = await link.getAttribute('target')
    text.includes("Cabinet de démonstration · rien n'est enregistré") && text.includes('Créer mon cabinet gratuitement') && href === SIGNUP && target === '_blank'
      ? ok('A1', `band « ${text} » → ${href} (${target})`)
      : bad('A1', 'band wrong at 1440', JSON.stringify({ text, href, target }))

    for (const [w, h] of [[320, 640], [390, 844], [820, 1180], [1180, 820], [1440, 900], [1536, 730]]) {
      await page.setViewportSize({ width: w, height: h })
      await page.waitForTimeout(400)
      const box = await b.boundingBox()
      const header = await page.getByRole('button', { name: 'Mon compte' }).boundingBox()
      const scrollX = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
      const shown = (await b.innerText()).replace(/\s+/g, ' ').trim()
      await page.screenshot({ path: `${SHOTS}/C1-band-${w}x${h}.png` })
      const fine = box && box.height <= 72 && header && header.y >= box.y + box.height - 1 && header.y + header.height <= h && scrollX <= 0
      fine ? ok('C1', `${w}×${h}: band ${Math.round(box.height)} px « ${shown} », header below it, no h-scroll`)
        : bad('C1', `${w}×${h} band layout`, JSON.stringify({ box, header, scrollX, shown }))
      if (w === 390) {
        shown.startsWith("Démo · rien n'est enregistré") && shown.endsWith('Créer mon cabinet') && !shown.includes('gratuitement')
          ? ok('A2', `390: « ${shown} »`) : bad('A2', 'short band text wrong at 390', shown)
      }
    }
    await ctx.close()
  } },
  { id: 'C2', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await page.emulateMedia({ media: 'print' })
    const visible = await band(page).isVisible().catch(() => false)
    !visible ? ok('C2', 'band hidden in print media') : bad('C2', 'band printed')
    await ctx.close()
  } },
  { id: 'C3', run: async () => {
    const ctx = await fresh({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true })
    const page = await ctx.newPage()
    await enter(page)
    const coarse = await page.evaluate(() => matchMedia('(pointer: coarse)').matches)
    const box = await band(page).getByRole('link').boundingBox()
    await page.screenshot({ path: `${SHOTS}/C3-band-coarse-390.png` })
    !coarse ? skip('C3', 'context did not report a coarse pointer')
      : box && box.height >= 44 ? ok('C3', `coarse CTA ${Math.round(box.height)} px`) : bad('C3', 'coarse CTA under 44 px', JSON.stringify(box))
    await ctx.close()
  } },
  { id: 'B1-B3', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    let auth = null
    page.on('request', (r) => { const h = r.headers()['authorization']; if (!auth && h && r.url().startsWith(API)) auth = h })
    await enter(page)
    await page.waitForTimeout(1500)
    if (!auth) { skip('B1-B3', 'no Authorization header seen on an API call'); await ctx.close(); return }
    const headers = { authorization: auth, 'content-type': 'application/json' }
    const post = await page.request.post(`${API}/patients`, { headers, data: { firstName: 'Probe', lastName: 'Demo' } })
    const body = await post.json().catch(() => ({}))
    post.status() === 403 && body.code === 'demo_read_only' && body.error === SENTENCE
      ? ok('B1', `POST /patients → 403 ${body.code}`) : bad('B1', 'write not refused as demo', `${post.status()} ${JSON.stringify(body)}`)
    const exp = await page.request.get(`${API}/patients/export`, { headers })
    const expBody = await exp.json().catch(() => ({}))
    exp.status() === 403 && expBody.code === 'demo_read_only'
      ? ok('B2', 'GET /patients/export → 403 demo_read_only') : bad('B2', 'export not refused', `${exp.status()} ${JSON.stringify(expBody)}`)
    const list = await page.request.get(`${API}/patients`, { headers })
    list.status() === 200 ? ok('B3', 'GET /patients → 200') : bad('B3', 'ordinary read refused', String(list.status()))
    await ctx.close()
  } },
  { id: 'A3+A4+D1', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await page.goto(`${BASE}/patients`)
    await page.getByRole('button', { name: 'Ajouter un patient' }).first().click()
    const dialog = page.getByRole('dialog')
    await dialog.waitFor({ timeout: 15000 })

    // D1 — the ordinary path: an empty save still reads as a validation problem, not as the demo notice.
    await dialog.getByRole('button', { name: /^(Créer|Enregistrer)/ }).last().click()
    await page.waitForTimeout(800)
    const demoNoticeEarly = await dialog.locator('[data-demo-refusal]').count()
    const invalid = await dialog.locator('[aria-invalid="true"], [role="alert"], .text-destructive').count()
    demoNoticeEarly === 0 && invalid > 0 ? ok('D1', `empty save → ${invalid} validation marks, no demo notice`)
      : bad('D1', 'ordinary validation path changed', JSON.stringify({ demoNoticeEarly, invalid }))

    await dialog.locator('#firstName').fill('Rachel')
    await dialog.locator('#lastName').fill('Green')
    await dialog.getByRole('button', { name: /^(Créer|Enregistrer)/ }).last().click()
    const notice = dialog.locator('[data-demo-refusal]')
    await notice.waitFor({ timeout: 15000 }).catch(() => {})
    await page.waitForTimeout(600)
    const noticeText = (await notice.innerText().catch(() => '')).replace(/\s+/g, ' ')
    const role = await notice.getAttribute('role').catch(() => null)
    const href = await notice.getByRole('link', { name: 'Créer mon cabinet' }).getAttribute('href').catch(() => null)
    const kept = [await dialog.locator('#firstName').inputValue(), await dialog.locator('#lastName').inputValue()]
    const stillOpen = await dialog.isVisible()
    const redWithSentence = await dialog.locator('[role="alert"]', { hasText: SENTENCE }).count()
    await page.screenshot({ path: `${SHOTS}/A3-patient-refused-1440.png` })
    noticeText.includes(SENTENCE) && role === 'status' && href === SIGNUP && stillOpen && kept.join(' ') === 'Rachel Green' && redWithSentence === 0
      ? ok('A3', `in-dialog notice (${role}) + link; input kept « ${kept.join(' ')} »`)
      : bad('A3', 'in-dialog refusal wrong', JSON.stringify({ noticeText, role, href, stillOpen, kept, redWithSentence }))

    await dialog.getByRole('button', { name: /^(Créer|Enregistrer)/ }).last().click()
    await page.waitForTimeout(1500)
    const notices = await dialog.locator('[data-demo-refusal]').count()
    const toasts = await demoToasts(page).count()
    notices === 1 && toasts === 0 ? ok('A4', 'second press: one notice, no toast')
      : bad('A4', 'repeat refusal stacked', JSON.stringify({ notices, toasts }))
    await ctx.close()
  } },
  { id: 'A5+A6', run: async () => {
    const ctx = await fresh(); const page = await ctx.newPage()
    await enter(page)
    await page.goto(`${BASE}/settings`)
    await page.getByRole('button', { name: 'Modifier', exact: true }).first().click()
    const save = page.getByRole('button', { name: 'Enregistrer les modifications' }).first()
    await save.waitFor({ timeout: 15000 })
    await save.click()
    const toast = demoToasts(page).first()
    await toast.waitFor({ timeout: 15000 }).catch(() => {})
    const type = await toast.getAttribute('data-type').catch(() => null)
    const action = await toast.getByRole('button', { name: 'Créer mon cabinet' }).count().catch(() => 0)
    await page.screenshot({ path: `${SHOTS}/A5-settings-toast-1440.png` })
    ;(await toast.isVisible().catch(() => false)) && type !== 'error' && action === 1
      ? ok('A5', `one neutral toast (data-type=${type}) with « Créer mon cabinet »`)
      : bad('A5', 'settings refusal toast wrong', JSON.stringify({ type, action }))
    for (let i = 0; i < 2; i++) { await save.click().catch(() => {}); await page.waitForTimeout(700) }
    await page.waitForTimeout(800)
    const count = await demoToasts(page).count()
    const stillEditing = await save.isVisible().catch(() => false)
    count === 1 && stillEditing ? ok('A6', 'three presses → one toast, form still in edit mode')
      : bad('A6', 'toasts stacked or form closed', JSON.stringify({ count, stillEditing }))
    await ctx.close()
  } },
]

for (const s of scenarios) {
  try { await s.run() } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]) }
}
skip('D4', 'no HostedMultiTenant stack here — covered by DemoReadOnlyMiddlewareTests.Another_Deployment_Is_Untouched')
await browser.close()
console.log('\n' + (findings.filter((f) => f.m).length ? `RED (${findings.filter((f) => f.m).length})` : 'GREEN'))
findings.forEach((f) => console.log(JSON.stringify(f)))
