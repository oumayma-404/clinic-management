// The gap pass (plan-2.md) — one launch, every row, fixes nothing. Usage: node walk-2.mjs [runN]
import { readFileSync, mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium } from '../../../e2e/node_modules/playwright-core/index.mjs'
import { totp, msToFreshWindow } from '../../devis-fiche-rdv-flexibility/qa/totp-act-onto-devis.mjs'

const here = dirname(fileURLToPath(import.meta.url))
const F1 = JSON.parse(readFileSync(join(here, 'fixtures.json'), 'utf8'))
const F = JSON.parse(readFileSync(join(here, 'fixtures-2.json'), 'utf8'))
const RUN = process.argv[2] ?? 'gap1'
const SHOTS = join(here, 'shots', RUN)
mkdirSync(SHOTS, { recursive: true })
const WEB = 'http://localhost:3000'
const API = 'http://localhost:5000/api'
const ADMIN = { email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', secret: '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO' }

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${String(d).slice(0, 300)}`); findings.push({ id, m, d: String(d).slice(0, 500) }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, skip: why }) }
const check = (id, cond, m, d = '') => (cond ? ok(id, m) : bad(id, m, d))

const fresh = () => new Promise((r) => setTimeout(r, msToFreshWindow()))
let TOKEN = null
{
  await fresh()
  const r = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: ADMIN.email, password: ADMIN.password, totpCode: totp(ADMIN.secret) }) })
  const j = await r.json(); TOKEN = j.value?.accessToken ?? j.accessToken
}
async function api(method, path, body) {
  const r = await fetch(`${API}${path}`, { method, headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${TOKEN}` }, body: body ? JSON.stringify(body) : undefined })
  const t = await r.text(); let j; try { j = JSON.parse(t) } catch { j = t }
  return j?.value ?? j
}

const browser = await chromium.launch({ channel: 'chrome', headless: true })
async function signIn(user, { intercept = true } = {}) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
  if (intercept) await ctx.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
  const page = await ctx.newPage()
  page.on('pageerror', (e) => pageErrors.push({ url: page.url(), e: String(e).slice(0, 200) }))
  await page.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' })
  await page.waitForURL(/\/login/, { timeout: 60000 })
  await page.fill('input[type=email]', user.email)
  await page.fill('input[type=password]', user.password)
  await page.click("button:has-text('Se connecter')")
  await page.waitForSelector('input[inputmode=numeric]', { timeout: 30000 })
  await page.waitForTimeout(msToFreshWindow())
  await page.fill('input[inputmode=numeric]', totp(user.secret))
  await page.waitForURL((u) => !/\/login/.test(u.toString()), { timeout: 45000 })
  return { ctx, page }
}
const pageErrors = []
let { ctx, page } = await signIn(ADMIN)

const shot = async (name) => page.screenshot({ path: join(SHOTS, `${name}.png`) }).catch(() => {})
const size = async (w, h) => { await page.setViewportSize({ width: w, height: h }); await page.waitForTimeout(350) }
const settle = async () => { await page.waitForLoadState('networkidle', { timeout: 20000 }).catch(() => {}); await page.waitForTimeout(500) }
const go = async (path) => { await page.goto(`${WEB}${path}`, { waitUntil: 'domcontentloaded' }); await settle() }
async function waitText(loc, re, ms = 20000) {
  const end = Date.now() + ms; let t = ''
  while (Date.now() < end) { t = (await loc.innerText().catch(() => '')) || ''; if (re.test(t)) return t; await page.waitForTimeout(250) }
  return t
}
const main = () => page.locator('main').first()
const scrollMain = (y) => page.evaluate((top) => { const m = document.querySelector('main'); if (m) m.scrollTo(0, top); window.scrollTo(0, top) }, y)
const scrollIntoCenter = (loc) => loc.evaluate((el) => el.scrollIntoView({ block: 'center' })).catch(() => {})
const dialog = () => page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible').last()
const alertD = () => page.locator('[role="alertdialog"]:visible').last()
const noHScroll = () => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)
async function closeAll() {
  for (let i = 0; i < 4; i++) {
    if (!(await dialog().count())) return
    const back = dialog().getByRole('button', { name: /^(Retour|Annuler|Fermer)$/ }).first()
    if (await back.count()) await back.click().catch(() => {}); else await page.keyboard.press('Escape')
    await page.waitForTimeout(350)
    const discard = page.getByRole('button', { name: /^(Abandonner|Quitter|Fermer sans enregistrer|Oui, fermer)/ })
    if (await discard.count()) { await discard.first().click().catch(() => {}); await page.waitForTimeout(300) }
  }
}
async function openMenu() {
  await main().getByRole('button', { name: /Actions|Autres actions|⋯/ }).first().click()
  await page.waitForTimeout(400)
  return page.locator('[role="menu"]:visible').last()
}
async function menuDialog(id, re, shotName) {
  const menu = await openMenu()
  const item = menu.getByRole('menuitem', { name: re }).first()
  if (!(await item.count())) { await page.keyboard.press('Escape'); return skip(id, `menu has no ${re}`) }
  if ((await item.getAttribute('aria-disabled')) === 'true' || (await item.getAttribute('data-disabled')) !== null) {
    const txt = await item.innerText(); await page.keyboard.press('Escape'); return bad(id, `menu item disabled: ${txt}`)
  }
  await item.click(); await page.waitForTimeout(700)
  const d = dialog()
  const t = (await d.count()) ? await d.innerText() : ''
  await shot(shotName)
  return t
}
const words = (t) => t.split(/\s+/).filter(Boolean).length
async function scenario(id, fn) {
  try { await fn() } catch (e) { bad(id, 'threw', e.message.split('\n')[0]); await shot(`${id}-threw`) }
  await closeAll().catch(() => {})
}
const minH = async (loc) => {
  const n = await loc.count(); let min = Infinity, name = ''
  for (let i = 0; i < n; i++) {
    const el = loc.nth(i); if (!(await el.isVisible().catch(() => false))) continue
    const h = await el.evaluate((node) => {
      const r = node.getBoundingClientRect(); const a = getComputedStyle(node, '::after')
      const overlay = a.content !== 'none' && a.position === 'absolute' ? parseFloat(a.height) || 0 : 0
      return Math.max(r.height, overlay)
    }).catch(() => Infinity)
    if (h < min) { min = h; name = (await el.innerText().catch(() => '')).slice(0, 40) }
  }
  return { min, name, n }
}

// ───────────────── T — tablet widths ─────────────────
for (const [w, h] of [[820, 1024], [1180, 820]]) {
  const tag = `${w}`
  await scenario(`T1-${tag}`, async () => {
    await size(w, h); await go(`/treatment-plans/${F.multi.plan}`)
    const t = await waitText(main(), /Couronne/)
    await shot(`T1-plan-multi-${tag}`)
    check(`T1-${tag}`, await noHScroll(), 'plan (several acts): no h-scroll')
    check(`T1-${tag}`, /séances? sur \d+ faites?|séances? à faire|Toutes les séances/.test(t), 'header count in words', t.slice(0, 300))
  })
  await scenario(`T2-${tag}`, async () => {
    await go(`/treatment-plans/${F1.main.plan}`); await waitText(main(), /Couronne/)
    await shot(`T2-plan-one-act-${tag}`)
    check(`T2-${tag}`, await noHScroll(), 'plan (one act): no h-scroll')
    await scrollMain(99999); await page.waitForTimeout(400)
    await shot(`T2-plan-one-act-bottom-${tag}`)
  })
  await scenario(`T3-${tag}`, async () => {
    await go(`/patients/${F.multi.patient}`)
    await scrollIntoCenter(main().getByText(/Tous les traitements/).last()); await page.waitForTimeout(500)
    await shot(`T3-patient-band-${tag}`)
    check(`T3-${tag}`, await noHScroll(), 'patient page: no h-scroll')
  })
  await scenario(`T4-${tag}`, async () => {
    await go('/treatment-plans'); await waitText(main(), /Traitements en cours/)
    await shot(`T4-lists-${tag}`)
    check(`T4-${tag}`, await noHScroll(), 'lists: no h-scroll')
  })
  await scenario(`T5-${tag}`, async () => {
    await go(`/appointments?patientId=${F1.plain.patient}`)
    await waitText(dialog(), /Sélectionner un type d'acte/, 15000)
    await dialog().getByText("Sélectionner un type d'acte").first().click(); await page.waitForTimeout(400)
    await page.keyboard.type('Couronne'); await page.waitForTimeout(800)
    await page.getByRole('option', { name: /Couronne/ }).first().click(); await page.waitForTimeout(700)
    await shot(`T5-booking-${tag}`)
    check(`T5-${tag}`, await noHScroll(), 'booking: no h-scroll')
    const create = dialog().getByRole('button', { name: /Créer le rendez-vous/ })
    const b = await create.boundingBox()
    check(`T5-${tag}`, b && b.y + b.height <= h, 'booking primary visible', JSON.stringify(b))
  })
  await scenario(`T6-${tag}`, async () => {
    await go(`/patients/${F.mixed.patient}?addRecord=1&appointmentId=${F.mixed.visit}`)
    await waitText(dialog(), /Fiche de soins/, 20000)
    await shot(`T6-fiche-${tag}`)
    check(`T6-${tag}`, await noHScroll(), 'fiche: no h-scroll')
    const save = dialog().getByRole('button', { name: /Enregistrer la séance/ })
    const b = await save.boundingBox()
    check(`T6-${tag}`, b && b.y + b.height <= h, 'fiche save visible', JSON.stringify(b))
  })
  await scenario(`T7-${tag}`, async () => {
    await go(`/appointments?appointmentId=${F1.main.visit2}`)
    await waitText(dialog(), /Enregistrer/, 15000)
    await shot(`T7-edit-rdv-${tag}`)
    check(`T7-${tag}`, await noHScroll(), 'edit RDV: no h-scroll')
  })
}

// ───────────────── C — touch (coarse pointer) ─────────────────
async function switchContext(opts) {
  const state = await ctx.storageState(); await ctx.close()
  ctx = await browser.newContext({ storageState: state, locale: 'fr-FR', ...opts })
  await ctx.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
  page = await ctx.newPage(); page.on('pageerror', (e) => pageErrors.push({ url: page.url(), e: String(e).slice(0, 200) }))
}
for (const [w, h] of [[390, 844], [820, 1024]]) {
  const tag = `${w}`
  await switchContext({ viewport: { width: w, height: h }, isMobile: true, hasTouch: true, deviceScaleFactor: 2 })
  await scenario(`C1-${tag}`, async () => {
    await go(`/treatment-plans/${F.multi.plan}`); await waitText(main(), /Couronne/)
    const coarse = await page.evaluate(() => matchMedia('(pointer: coarse)').matches)
    check(`C1-${tag}`, coarse, 'pointer: coarse matches (emulation live)')
    const strip = await minH(main().locator('ol[aria-label] button'))
    check(`C1-${tag}`, strip.n > 0 && strip.min >= 43.5, `séance buttons ≥ 44 px (min ${strip.min} « ${strip.name} », n=${strip.n})`)
    const more = await minH(main().getByRole('button', { name: /Actions|Autres actions/ }))
    check(`C1-${tag}`, more.min >= 43.5, `⋯ ≥ 44 px (${more.min})`)
    const first = main().locator('ol[aria-label] button').first()
    await first.tap().catch(() => first.click()); await page.waitForTimeout(900)
    const pop = await minH(page.locator('[data-slot="popover-content"]:visible button'))
    await shot(`C1-popover-coarse-${tag}`)
    check(`C1-${tag}`, pop.n > 0 && pop.min >= 43.5, `popover buttons ≥ 44 px (min ${pop.min} « ${pop.name} »)`)
    await page.keyboard.press('Escape')
    const row = await minH(main().getByRole('button', { name: /Remise|Ajouter un acte|Modifier/ }))
    check(`C1-${tag}`, row.min >= 43.5, `row controls ≥ 44 px (min ${row.min} « ${row.name} »)`)
    await shot(`C1-plan-coarse-${tag}`)
  })
  await scenario(`C2-${tag}`, async () => {
    await go(`/patients/${F.mixed.patient}?addRecord=1&appointmentId=${F.mixed.visit}`)
    await waitText(dialog(), /Fiche de soins/, 20000)
    const ctrl = await minH(dialog().getByRole('button', { name: /Changer|Enregistrer la séance|Ajouter un autre acte|Changer d'acte/ }))
    await shot(`C2-fiche-coarse-${tag}`)
    check(`C2-${tag}`, ctrl.min >= 43.5, `fiche controls ≥ 44 px (min ${ctrl.min} « ${ctrl.name} »)`)
    const strip = await minH(dialog().locator('ol[aria-label] button'))
    if (strip.n) check(`C2-${tag}`, strip.min >= 43.5, `fiche strip séances ≥ 44 px (min ${strip.min})`)
  })
  await scenario(`C3-${tag}`, async () => {
    await go(`/appointments?patientId=${F1.plain.patient}`)
    await waitText(dialog(), /Sélectionner un type d'acte/, 15000)
    await dialog().getByText("Sélectionner un type d'acte").first().click(); await page.waitForTimeout(400)
    await page.keyboard.type('Couronne'); await page.waitForTimeout(800)
    await page.getByRole('option', { name: /Couronne/ }).first().click(); await page.waitForTimeout(700)
    const strip = await minH(dialog().locator('ol[aria-label] button'))
    const btns = await minH(dialog().getByRole('button', { name: /^(Séances|Tout en 1 séance)/ }))
    await shot(`C3-booking-coarse-${tag}`)
    check(`C3-${tag}`, strip.n > 0 && strip.min >= 43.5, `booking séances ≥ 44 px (min ${strip.min})`)
    check(`C3-${tag}`, btns.n > 0 && btns.min >= 43.5, `« Séances » / « Tout en 1 séance » ≥ 44 px (min ${btns.min} « ${btns.name} »)`)
  })
  await scenario(`C4-${tag}`, async () => {
    await go(`/patients/${F1.draft.patient}`)
    const b = main().getByRole('button', { name: /^Planifier\s*:/ }).first()
    await b.scrollIntoViewIfNeeded().catch(() => {})
    const m = await minH(main().getByRole('button', { name: /^Planifier\s*:/ }))
    await shot(`C4-patient-card-coarse-${tag}`)
    check(`C4-${tag}`, m.n > 0 && m.min >= 43.5, `card « Planifier : » ≥ 44 px (${m.min})`)
  })
}
await switchContext({ viewport: { width: 1440, height: 900 } })

// ───────────────── D — every rewritten confirmation ─────────────────
const confirmCheck = (id, t, { destructive, max = 45 } = {}) => {
  check(id, t && /\?/.test(t.split('\n')[0] + t.split('\n')[1]), 'title is a question', t.slice(0, 120))
  const body = t.split('\n').filter((l) => !/^(Retour|Annuler|Fermer)$/.test(l.trim())).join(' ')
  check(id, words(body) <= max, `short (${words(body)} words)`, t)
  if (destructive) check(id, true, 'shot taken for the button colour')
}
await scenario('D1', async () => { await go(`/treatment-plans/${F.fresh.plan}`); await waitText(main(), /Soin/); const t = await menuDialog('D1', /Arrêter/, 'D1-arreter-annule'); if (t) { confirmCheck('D1', t, { max: 70 }); check('D1', /Réversible par Rétablir/.test(t), 'cancel branch states it can be undone (« Rétablir » exists)', t) } })
await scenario('D2', async () => { await go(`/treatment-plans/${F1.draft.plan}`); await waitText(main(), /Couronne/); const t = await menuDialog('D2', /Supprimer/, 'D2-supprimer'); if (t) confirmCheck('D2', t) })
await scenario('D3', async () => { await go(`/treatment-plans/${F.fresh.plan}`); await waitText(main(), /Soin/); const t = await menuDialog('D3', /Dupliquer/, 'D3-dupliquer'); if (t) confirmCheck('D3', t) })
await scenario('D4', async () => { await go(`/treatment-plans/${F.fresh.plan}`); await waitText(main(), /Soin/); const t = await menuDialog('D4', /Changer de patient/, 'D4-changer-patient'); if (t) check('D4', /patient/i.test(t), 'dialog opens', t.slice(0, 200)) })
await scenario('D5', async () => { await go(`/treatment-plans/${F.fresh.plan}`); await waitText(main(), /Soin/); const t = await menuDialog('D5', /praticien/i, 'D5-praticien'); if (t) check('D5', /praticien/i.test(t), 'dialog opens', t.slice(0, 200)) })
await scenario('D6', async () => { await go(`/treatment-plans/${F.multi.plan}`); await waitText(main(), /Couronne/); const t = await menuDialog('D6', /Ne plus réclamer/, 'D6-ne-plus-reclamer'); if (t) confirmCheck('D6', t, { max: 60 }) })
await scenario('D7', async () => { await go(`/treatment-plans/${F.multi.plan}`); await waitText(main(), /Couronne/); const t = await menuDialog('D7', /Facturer/, 'D7-facturer'); if (t) confirmCheck('D7', t) })
await scenario('D8', async () => {
  await go(`/treatment-plans/${F.multi.plan}`); await waitText(main(), /Couronne/)
  await main().getByRole('button', { name: /^Préparation/ }).first().click(); await page.waitForTimeout(500)
  await page.locator('[data-slot="popover-content"]:visible').getByRole('button', { name: /Remettre à faire/ }).click(); await page.waitForTimeout(600)
  const t = await dialog().innerText(); await shot('D8-remettre-a-faire')
  confirmCheck('D8', t, { max: 60 })
})
await scenario('D9', async () => { await go(`/treatment-plans/${F.billed.plan}`); await waitText(main(), /Couronne/); const t = await menuDialog('D9', /Détacher la note/, 'D9-detacher-note'); if (t) confirmCheck('D9', t, { max: 60 }) })
await scenario('D10', async () => {
  await go(`/treatment-plans/${F.stopped.plan}`); const t0 = await waitText(main(), /Couronne/); await shot('S2-stopped')
  check('S2', /Arrêté/.test(t0), 'badge « Arrêté »', t0.slice(0, 200))
  await main().getByRole('button', { name: /Reprendre le traitement/ }).first().click(); await page.waitForTimeout(600)
  const t = await dialog().innerText(); await shot('D10-reprendre'); confirmCheck('D10', t)
})
await scenario('D11', async () => {
  await go(`/treatment-plans/${F.writtenOff.plan}`); const t0 = await waitText(main(), /Couronne/); await shot('S3-written-off')
  check('S3', /Non réclamé/.test(t0) && !/Créance abandonnée|Passer en perte/.test(t0), 'badge « Non réclamé »', t0.slice(0, 300))
  await main().getByRole('button', { name: /Reprendre le traitement/ }).first().click(); await page.waitForTimeout(600)
  const t = await dialog().innerText(); await shot('D11-reprendre-non-reclame'); confirmCheck('D11', t)
})
await scenario('D12', async () => {
  await go(`/treatment-plans/${F.cancelled.plan}`); const t0 = await waitText(main(), /Soin/); await shot('S4-cancelled')
  check('S4', /Annulé/.test(t0), 'badge « Annulé »', t0.slice(0, 200))
  await main().getByRole('button', { name: /Rétablir/ }).first().click(); await page.waitForTimeout(600)
  const t = await dialog().innerText(); await shot('D12-retablir'); check('D12', /Rétablir/i.test(t) && words(t) <= 60, `rétablir dialog short (${words(t)})`, t)
})
await scenario('D13', async () => {
  await go(`/treatment-plans/${F.refund.plan}`); await waitText(main(), /Couronne/)
  const t = await menuDialog('D13', /Arrêter/, 'D13-arreter-rendu')
  if (t) { check('D13', /80,000/.test(t) && /(rend|Rend)/.test(t), 'refund-first branch names the 80 DT', t); check('D13', !/irréversible/i.test(t), 'no « irréversible » on the refund branch') }
})
await scenario('D14', async () => {
  await go(`/treatment-plans/${F1.draft.plan}`); await waitText(main(), /Couronne/)
  await main().getByRole('button', { name: /Créer le devis/ }).first().click(); await page.waitForTimeout(600)
  const t = await dialog().innerText(); await shot('D14-creer-devis'); confirmCheck('D14', t)
})
await scenario('D-390', async () => {
  await size(390, 844); await go(`/treatment-plans/${F.fresh.plan}`); await waitText(main(), /Soin/)
  await menuDialog('D-390', /Arrêter/, 'D1-arreter-annule-390')
  check('D-390', await noHScroll(), 'confirm at 390: no h-scroll'); await closeAll()
  await go(`/treatment-plans/${F.refund.plan}`); await waitText(main(), /Couronne/)
  await menuDialog('D-390', /Arrêter/, 'D13-arreter-rendu-390'); await closeAll()
  await size(1440, 900)
})

// ───────────────── W — the windows ─────────────────
await scenario('W1', async () => {
  await go(`/treatment-plans/${F.multi.plan}`); await waitText(main(), /Couronne/)
  await main().getByRole('button', { name: /^Empreinte/ }).first().click(); await page.waitForTimeout(500)
  await page.locator('[data-slot="popover-content"]:visible').getByRole('button', { name: /Modifier la séance/ }).click(); await page.waitForTimeout(800)
  const t = await dialog().innerText(); await shot('W1-seances-dialog')
  check('W1', /Séances · Couronne/.test(t), 'séances dialog opens on the act', t.slice(0, 300))
  await size(1536, 730); await shot('W1-seances-dialog-730')
  const sv = dialog().getByRole('button', { name: /Enregistrer/ }).last(); const b = await sv.boundingBox()
  check('W1', b && b.y + b.height <= 730, 'save visible at 730', JSON.stringify(b))
  await size(390, 844); await shot('W1-seances-dialog-390'); check('W1', await noHScroll(), '390: no h-scroll')
  await size(1440, 900)
})
await scenario('W1b', async () => {
  await go(`/treatment-plans/${F.multi.plan}`); await waitText(main(), /Couronne/)
  await main().getByRole('button', { name: /Ajouter une séance/ }).first().click(); await page.waitForTimeout(800)
  const t = await dialog().innerText(); await shot('W1b-add-seance')
  check('W1b', /Séances/.test(t), '« + » opens the séances dialog with a new row', t.slice(0, 300))
})
await scenario('W2', async () => {
  await go(`/treatment-plans/${F.multi.plan}`); await waitText(main(), /Couronne/)
  await main().getByText(/Tout modifier/).first().click(); await page.waitForTimeout(900)
  const t = await dialog().innerText(); await shot('W2-tout-modifier')
  check('W2', !/Le devis garde son numéro|Seul le patient n'est pas modifiable/.test(t), 'form without the long subtitle', t.slice(0, 300))
  await size(1536, 730); await shot('W2-tout-modifier-730')
  await size(390, 844); await shot('W2-tout-modifier-390'); check('W2', await noHScroll(), '390: no h-scroll')
  await size(1440, 900)
})
await scenario('W3', async () => {
  await go(`/treatment-plans/${F.multi.plan}`); await waitText(main(), /Couronne/)
  await main().getByRole('button', { name: /^Encaisser$/ }).first().click(); await page.waitForTimeout(800)
  const t = await dialog().innerText(); await shot('W3-encaisser')
  check('W3', /Encaisser|Enregistrer/.test(t), 'settle modal opens', t.slice(0, 200))
})
await scenario('W4', async () => {
  await go(`/treatment-plans/${F.multi.plan}`); await waitText(main(), /Couronne/)
  const fold = main().getByRole('button', { name: /Échéancier/ }).first()
  await fold.click(); await page.waitForTimeout(600)
  await fold.scrollIntoViewIfNeeded().catch(() => {}); await shot('W4-echeancier-open')
  const mod = main().getByRole('button', { name: /Modifier l'échéancier/ }).first()
  check('W4', (await mod.count()) > 0, '« Modifier l\'échéancier » inside the fold')
  await mod.click(); await page.waitForTimeout(800)
  const t = await dialog().innerText(); await shot('W5-modifier-echeancier')
  check('W5', /échéancier/i.test(t), 'échéancier modal opens', t.slice(0, 200))
  await closeAll()
  const enc = main().locator('button:visible', { hasText: /^Encaisser$/ })
  const n = await enc.count()
  if (n > 1) { await enc.nth(n - 1).click(); await page.waitForTimeout(800); await shot('W6-encaisser-echeance'); check('W6', /Encaisser|Enregistrer/.test(await dialog().innerText()), 'échéance payment modal opens') }
  else skip('W6', 'no per-row « Encaisser » visible in the fold')
})
await scenario('W7', async () => {
  await go(`/treatment-plans/${F1.main.plan}`); await waitText(main(), /Couronne/)
  await main().getByRole('button', { name: /^Empreinte/ }).first().click(); await page.waitForTimeout(500)
  await page.locator('[data-slot="popover-content"]:visible').getByRole('button', { name: /Déplacer/ }).click(); await page.waitForTimeout(1200)
  const t = await waitText(dialog(), /Enregistrer/, 10000); await shot('W7-deplacer')
  check('W7', /Rendez-vous/.test(t) && page.url().includes('/treatment-plans/'), 'edit RDV opens on the treatment page', page.url())
})
await scenario('W8', async () => {
  await go('/procedure-types'); await waitText(main(), /Incision/)
  await main().getByRole('button', { name: /Modifier les \d+ séances/ }).first().click(); await page.waitForTimeout(800)
  const t = await dialog().innerText(); await shot('W8-catalogue-seances')
  check('W8', /séance/i.test(t), 'catalogue séances dialog opens', t.slice(0, 200))
})

// ───────────────── S — plan states ─────────────────
await scenario('S1', async () => {
  await go(`/treatment-plans/${F.billed.plan}`); const t = await waitText(main(), /Couronne/); await shot('S1-billed')
  check('S1', /note n°|Facturé/i.test(t), 'billed: the note is named', t.slice(0, 500))
  check('S1', !/n'apparaîtrait ni dans la caisse/.test(t), 'long « why » sentence gone')
})
await scenario('S5', async () => {
  await go(`/treatment-plans/${F.multi.plan}`); await waitText(main(), /Couronne/); await shot('S5-multi-1440')
  await scrollMain(700); await page.waitForTimeout(400); await shot('S5-multi-1440-acts')
  await size(390, 844); await scrollMain(0); await shot('S5-multi-390')
  await scrollMain(800); await page.waitForTimeout(400); await shot('S5-multi-390-acts')
  check('S5', await noHScroll(), '390: no h-scroll'); await size(1440, 900)
})
await scenario('S6', async () => {
  await go(`/treatment-plans/${F.long.plan}`); await waitText(main(), /canal/)
  await shot('S6-long-1440')
  await size(390, 844); await shot('S6-long-390')
  check('S6', await noHScroll(), 'six séances at 390: page does not scroll sideways')
  const scrolls = await page.evaluate(() => { const ol = document.querySelector('main ol[aria-label]'); const box = ol?.parentElement; return box ? box.scrollWidth > box.clientWidth : false })
  check('S6', scrolls, 'the strip scrolls in its own box')
  await size(1440, 900)
})
for (const [id, key] of [['S2-390', 'stopped'], ['S3-390', 'writtenOff'], ['S4-390', 'cancelled'], ['S1-390', 'billed']]) {
  await scenario(id, async () => { await size(390, 844); await go(`/treatment-plans/${F[key].plan}`); await page.waitForTimeout(800); await shot(`${id}-${key}`); check(id, await noHScroll(), 'no h-scroll'); await size(1440, 900) })
}

// ───────────────── F — the fiche, mixed séance ─────────────────
await scenario('F1', async () => {
  await go(`/patients/${F.mixed.patient}?addRecord=1&appointmentId=${F.mixed.visit}`)
  await waitText(dialog(), /Fiche de soins/, 20000)
  await dialog().getByRole('button', { name: /Ajouter un autre acte/ }).first().click(); await page.waitForTimeout(500)
  await page.keyboard.type('Détartrage'); await page.waitForTimeout(700)
  await page.getByRole('option', { name: /Détartrage/ }).first().click().catch(() => {}); await page.waitForTimeout(800)
  const t = await dialog().innerText(); await shot('F1-mixed')
  check('F1', /\(traitement\)/.test(t) || /Payé à cette séance|Payé aujourd'hui/.test(t), 'two « Payé » told apart', t.slice(-600))
  check('F1', /À continuer une autre séance/.test(t), '« À continuer une autre séance » on an own-fee act')
  check('F1', !/tapez sur le schéma|glissez pour en sélectionner|Il faudra une autre séance/.test(t), 'no helper captions')
  // F2 — cheque fields
  const mode = dialog().locator('#paid-method').first()
  if (await mode.count()) {
    await mode.click(); await page.waitForTimeout(300)
    await page.getByRole('option', { name: /Chèque/ }).first().click().catch(() => {}); await page.waitForTimeout(500)
    const t2 = await dialog().innerText(); await shot('F2-cheque')
    check('F2', /banque|n° du chèque|Numéro|encaissable/i.test(t2), 'cheque fields appear', t2.slice(-500))
  } else skip('F2', 'no séance « Mode » field on this fiche')
  // F3 — prescription
  const presc = dialog().getByRole('button', { name: /Prescription/ }).first()
  await presc.scrollIntoViewIfNeeded().catch(() => {}); await presc.click().catch(() => {}); await page.waitForTimeout(500)
  const addMed = dialog().getByRole('button', { name: /Médicament/ }).first()
  if (await addMed.count()) {
    await addMed.click(); await page.waitForTimeout(400); await page.keyboard.type('Amoxicilline'); await page.waitForTimeout(900)
  }
  const addEx = dialog().getByRole('button', { name: /Examen/ }).first()
  if (await addEx.count()) { await addEx.click(); await page.waitForTimeout(400); await page.keyboard.type('Panoramique dentaire'); await page.waitForTimeout(400) }
  const t3 = await dialog().innerText(); await shot('F3-prescription')
  check('F3', /Amoxicilline/.test(t3) || /Médicament/.test(t3), 'médicament line present', t3.slice(-600))
  check('F3', !/Écrivez ce que vous voulez|Un médicament se choisit dans le catalogue/.test(t3), 'no prescription instructions')
  await size(390, 844); await shot('F-mixed-390'); check('F', await noHScroll(), 'fiche mixed at 390: no h-scroll'); await size(1440, 900)
})

// ───────────────── X — 409 « Recharger » on the in-row editor ─────────────────
await scenario('X1', async () => {
  await go(`/treatment-plans/${F.conflict.plan}`); await waitText(main(), /Couronne/)
  const price = main().locator('input[aria-label^="Prix de"]:visible').first()
  await price.fill('310'); await price.press('Tab'); await page.waitForTimeout(300)
  const p = await api('GET', `/treatment-plans/${F.conflict.plan}`)
  await api('POST', `/treatment-plans/${F.conflict.plan}/amend`, { addItems: [], updateItems: [], removeItemIds: [], notes: 'Note d\'un collègue', version: p.version })
  await page.getByRole('button', { name: /^Enregistrer$/ }).last().click(); await page.waitForTimeout(1500)
  const t = await main().innerText(); await shot('X1-conflict')
  check('X1', (await page.getByRole('button', { name: /Recharger/ }).count()) > 0, 'refusal offers « Recharger »', t.slice(-400))
})

// ───────────────── L — lists and the patient file on a phone ─────────────────
await scenario('L1', async () => {
  await size(390, 844); await go('/treatment-plans'); await waitText(main(), /Traitements en cours/); await shot('L1-lists-390-top')
  await main().getByText(/Devis et échéanciers|Tous les devis|Devis$/i).last().evaluate((el) => el.scrollIntoView({ block: 'start' })).catch(() => scrollMain(2600)); await page.waitForTimeout(600); await shot('L1-lists-390-devis')
  const t = await main().innerText()
  check('L1', !/étape \d+ \/ \d+|\b\d+\/\d+ séances?\b/.test(t), 'no bare fractions', '')
  check('L1', await noHScroll(), 'no h-scroll')
})
await scenario('L3', async () => {
  await go(`/patients/${F.multi.patient}`)
  await scrollIntoCenter(main().getByText(/Tous les traitements/).last()); await page.waitForTimeout(500)
  await shot('L3-patient-band-390')
  const chip = main().getByRole('button', { name: /^Reste à payer .*encaisser/ }).first()
  if (await chip.count()) { await chip.click(); await page.waitForTimeout(1200); await shot('L3-reste-a-payer-390') } else skip('L3', 'no « Reste à payer » chip')
  const t = await main().innerText()
  check('L3', !/Solde dû|Plan de traitement\b|Tous les plans/.test(t), 'vocabulary on the patient page', '')
  await size(1440, 900); await page.waitForTimeout(300); await shot('L3-reste-a-payer-1440')
})
await scenario('L4', async () => {
  for (const w of [1440, 390]) {
    await size(w, w === 390 ? 844 : 900); await go('/a-cloturer'); await page.waitForTimeout(800); await shot(`L4-cloturer-seances-${w}`)
    for (const name of [/Patients à compléter/, /Suites à planifier/]) {
      const tab = main().getByRole('tab', { name }).first()
      if (await tab.count()) { await tab.click(); await page.waitForTimeout(900); await shot(`L4-cloturer-${String(name).replace(/[^a-z]/gi, '')}-${w}`) }
    }
    check(`L4-${w}`, await noHScroll(), 'À clôturer: no h-scroll')
  }
  await size(1440, 900)
})

// ───────────────── K — dark theme ─────────────────
await scenario('K1', async () => {
  await page.evaluate(() => { try { localStorage.setItem('theme', 'dark') } catch {} })
  await page.emulateMedia({ colorScheme: 'dark' })
  await go(`/treatment-plans/${F.multi.plan}`); await waitText(main(), /Couronne/); await shot('K1-plan-dark')
  const dark = await page.evaluate(() => document.documentElement.classList.contains('dark') || matchMedia('(prefers-color-scheme: dark)').matches)
  check('K1', dark, 'dark theme applied')
  await go(`/patients/${F.mixed.patient}?addRecord=1&appointmentId=${F.mixed.visit}`); await waitText(dialog(), /Fiche de soins/, 20000); await shot('K2-fiche-dark')
  await closeAll()
  await go(`/appointments?patientId=${F1.plain.patient}`); await waitText(dialog(), /Sélectionner un type d'acte/, 15000)
  await dialog().getByText("Sélectionner un type d'acte").first().click(); await page.waitForTimeout(400)
  await page.keyboard.type('Couronne'); await page.waitForTimeout(800)
  await page.getByRole('option', { name: /Couronne/ }).first().click(); await page.waitForTimeout(700); await shot('K3-booking-dark')
  await closeAll()
  await page.evaluate(() => { try { localStorage.setItem('theme', 'light') } catch {} })
  await page.emulateMedia({ colorScheme: 'light' })
})

// ───────────────── P — the post-visit prompt (real queue) ─────────────────
await scenario('P1', async () => {
  await ctx.unroute('**/notifications/pending-reviews**')
  await go('/patients'); await page.waitForTimeout(3500)
  const d = page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible')
  if (!(await d.count())) return skip('P1', 'no pending review in the queue')
  const t = await d.last().innerText(); await shot('P1-post-visit')
  check('P1', /Remplir la fiche de soins/.test(t) && !/dossier médical/.test(t.split('\n').slice(-3).join(' ')), 'wording « Remplir la fiche de soins »', t)
  const later = d.last().getByRole('button', { name: /Plus tard/ }); if (await later.count()) await later.click()
})
await ctx.close()

// ───────────────── R — reception (secretary) ─────────────────
if (F.secretary?.email) {
  ;({ ctx, page } = await signIn({ email: F.secretary.email, password: F.secretary.password, secret: F.secretary.secret }))
  await scenario('R1', async () => {
    await go(`/patients/${F.multi.patient}`)
    await scrollIntoCenter(main().getByText(/Tous les traitements/).last()); await page.waitForTimeout(500)
    await shot('R1-secretary-band'); const t = await main().innerText()
    check('R1', /Couronne/.test(t), 'secretary sees the treatment card')
  })
  await scenario('R2', async () => {
    await go(`/treatment-plans/${F.multi.plan}`); await waitText(main(), /Couronne/); await shot('R2-secretary-plan')
    const menu = await openMenu(); const m = await menu.innerText(); await shot('R2-secretary-menu'); await page.keyboard.press('Escape')
    check('R2', m.length > 0, 'secretary menu opens', m.replace(/\n/g, ' | '))
  })
  await scenario('R3', async () => {
    await go(`/appointments?patientId=${F1.plain.patient}`)
    await waitText(dialog(), /Sélectionner un type d'acte/, 15000)
    await dialog().getByText("Sélectionner un type d'acte").first().click(); await page.waitForTimeout(400)
    await page.keyboard.type('Couronne'); await page.waitForTimeout(800)
    await page.getByRole('option', { name: /Couronne/ }).first().click(); await page.waitForTimeout(700)
    const time = dialog().locator('input[inputmode="numeric"]').first()
    await shot('R3-secretary-booking')
    const before = await api('GET', `/treatment-plans?patientId=${F1.plain.patient}`)
    const countBefore = (before.items ?? before ?? []).length
    await dialog().getByRole('button', { name: /Créer le rendez-vous/ }).click(); await page.waitForTimeout(1500)
    for (let i = 0; i < 3; i++) { // slot / hours / past-time confirmations
      const c = page.locator('[role="alertdialog"]:visible').last()
      if (!(await c.count())) break
      await shot(`R3-confirm-${i}`)
      await c.getByRole('button').filter({ hasNotText: /^(Annuler|Retour|Non)/ }).last().click().catch(() => {}); await page.waitForTimeout(1500)
    }
    const errs = await page.locator('[data-sonner-toast]').allInnerTexts().catch(() => [])
    await page.waitForTimeout(1500)
    const after = await api('GET', `/treatment-plans?patientId=${F1.plain.patient}`)
    const countAfter = (after.items ?? after ?? []).length
    check('R3', countAfter === countBefore + 1 && !errs.some((e) => /droits|403|refus/i.test(e)), `secretary booked a multi-séance act: a treatment was created (${countBefore}→${countAfter})`, errs.join(' | '))
  })
  await ctx.close()
} else skip('R', 'no secretary fixture')

check('Z', pageErrors.length === 0, 'no pageerror anywhere', JSON.stringify(pageErrors.slice(0, 5)))
await browser.close()
console.log('\n──── FINDINGS ────')
findings.forEach((f) => console.log(JSON.stringify(f)))
console.log(findings.filter((f) => !f.skip).length ? `RED (${findings.filter((f) => !f.skip).length})` : 'ALL CLEAR')
