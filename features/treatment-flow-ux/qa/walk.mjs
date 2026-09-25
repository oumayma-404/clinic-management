// The treatment-flow UX walk — every row of plan.md in one launch. Fixes nothing; collects every finding.
// Usage: node features/treatment-flow-ux/qa/walk.mjs [runN]
import { readFileSync, mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { openSignedIn, settled, toastText, WEB } from './session.mjs'

const here = dirname(fileURLToPath(import.meta.url))
const F = JSON.parse(readFileSync(join(here, 'fixtures.json'), 'utf8'))
const RUN = process.argv[2] ?? 'run1'
const SHOTS = join(here, 'shots', RUN)
mkdirSync(SHOTS, { recursive: true })
const API = 'http://localhost:5000/api'

let TOKEN = null
const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d: String(d).slice(0, 600) }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, skip: why }) }
const check = (id, cond, m, d = '') => (cond ? ok(id, m) : bad(id, m, d))

// An API token of our own, minted in a fresh TOTP window BEFORE the browser signs in — never
// /bff/auth/token, whose exchange rotates the page's own credential.
{
  const { totp, msToFreshWindow } = await import('../../devis-fiche-rdv-flexibility/qa/totp-act-onto-devis.mjs')
  await new Promise((r) => setTimeout(r, msToFreshWindow()))
  const r = await fetch(`${API}/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO') }),
  })
  const j = await r.json(); TOKEN = j.value?.accessToken ?? j.accessToken
}
const { browser, page, consoleErrors } = await openSignedIn({ width: 1440, height: 900 })

async function shot(name) { await page.screenshot({ path: join(SHOTS, `${name}.png`) }).catch(() => {}) }
async function size(w, h) { await page.setViewportSize({ width: w, height: h }); await page.waitForTimeout(400) }
async function go(path) { await page.goto(`${WEB}${path}`, { waitUntil: 'domcontentloaded' }); await settled(page) }
/** Poll a locator's visible text until it matches — a skeleton is not a screen. */
async function waitText(loc, re, ms = 20000) {
  const end = Date.now() + ms
  let t = ''
  while (Date.now() < end) {
    t = (await loc.innerText().catch(() => '')) || ''
    if (re.test(t)) return t
    await page.waitForTimeout(250)
  }
  return t
}
const main = () => page.locator('main').first()
const dialog = () => page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible').last()
const noHScroll = async () => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)
async function closeDialogs() {
  for (let i = 0; i < 3; i++) {
    if (!(await dialog().count())) return
    await page.keyboard.press('Escape'); await page.waitForTimeout(350)
    const discard = page.getByRole('button', { name: /^(Quitter|Abandonner|Fermer sans enregistrer|Oui)/ })
    if (await discard.count()) { await discard.first().click().catch(() => {}); await page.waitForTimeout(300) }
  }
}
async function apiGet(path) {
  const r = await fetch(`${API}${path}`, { headers: { Authorization: `Bearer ${TOKEN}` } })
  const j = await r.json(); return j?.value ?? j
}

async function scenario(id, fn) {
  try { await fn() } catch (e) { bad(id, 'threw', e.message.split('\n')[0]); await shot(`${id}-threw`) }
  await closeDialogs().catch(() => {})
}

// ───────────────────────────── Tier A ─────────────────────────────
await scenario('A1', async () => {
  await size(1536, 730)
  await go(`/treatment-plans/${F.main.plan}`)
  const t = await waitText(main(), /Couronne/)
  await shot('A1-plan-1536x730')
  check('A1', /Couronne\s*·\s*dent 16/.test(t), 'title « Couronne · dent 16 »', t.slice(0, 300))
  check('A1', /En cours/.test(t), 'badge « En cours »')
  check('A1', new RegExp(`Devis n°\\s*${F.main.number}`).test(t), `chip « Devis n° ${F.main.number} »`)
  check('A1', /faite le/.test(t) && /prévue le/.test(t) && /à planifier/.test(t), 'strip words faite / prévue / à planifier')
  check('A1', !/Retour aux plans|PROCHAINE SÉANCE|séance sur 3 faite|0 \/ 3|1 \/ 3/i.test(t), 'old header texts gone')
  check('A1', !/Un acte passe à « Réalisé »/.test(t), '45-word footnote gone')
  await size(390, 844); await shot('A1-plan-390')
  check('A1', await noHScroll(), 'no horizontal scroll at 390')
})

await scenario('A2', async () => {
  await size(1440, 900)
  await go(`/treatment-plans/${F.main.plan}`)
  await waitText(main(), /Empreinte/)
  const emp = main().getByRole('button', { name: /^Empreinte/ }).first()
  if (!(await emp.count())) return bad('A2', 'Empreinte séance is not a button')
  await emp.click(); await page.waitForTimeout(500)
  const pop = page.locator('[data-slot="popover-content"]:visible').last()
  const p1 = await waitText(pop, /prévue/, 5000)
  await shot('A2-popover-booked')
  check('A2', /prévue le/.test(p1) && /Voir le RDV/.test(p1), 'booked séance popover: « prévue le » + « Voir le RDV »', p1)
  await page.keyboard.press('Escape'); await page.waitForTimeout(300)
  const prep = main().getByRole('button', { name: /^Préparation/ }).first()
  await prep.click(); await page.waitForTimeout(500)
  const p2 = await waitText(page.locator('[data-slot="popover-content"]:visible').last(), /faite/, 5000)
  await shot('A2-popover-done')
  check('A2', /faite le/.test(p2) && /Voir la fiche/.test(p2) && /Remettre à faire/.test(p2), 'done séance popover: « Voir la fiche » + « Remettre à faire »', p2)
  check('A2', /Modifier les séances/.test(p2), 'done séance popover also opens the séances dialog', p2)
  await page.keyboard.press('Escape')
})

await scenario('A3', async () => {
  const t = await main().innerText()
  const money = t.slice(Math.max(0, t.search(/L'argent|Argent/)))
  check('A3', /Prix/.test(money) && /Payé/.test(money) && /Reste à payer/.test(money), 'figures Prix · Payé · Reste à payer', money.slice(0, 300))
  check('A3', /300,000/.test(money) && /50,000/.test(money) && /250,000/.test(money), 'figures 300 / 50 / 250')
  const enc = main().getByRole('button', { name: /^Encaisser$/ })
  check('A3', (await enc.count()) >= 1, 'one « Encaisser » button')
  check('A3', !/Régler le devis/.test(t), '« Régler le devis » renamed')
  check('A3', /Échéancier/.test(money), 'échéancier fold present')
  await shot('A3-money')
})

await scenario('A4', async () => {
  const more = main().getByRole('button', { name: /Actions|Autres actions|⋯/ }).first()
  await more.click(); await page.waitForTimeout(400)
  const menu = page.locator('[role="menu"]:visible').last()
  const m = await menu.innerText()
  await shot('A4-menu')
  check('A4', /Document/i.test(m) && /Modifier/i.test(m) && /Fin du traitement/i.test(m), 'menu groups Document / Modifier / Fin du traitement', m)
  const stop = menu.getByRole('menuitem', { name: /Arrêter/ }).first()
  if (!(await stop.count())) { await page.keyboard.press('Escape'); return bad('A4', 'no « Arrêter » item') }
  await stop.click(); await page.waitForTimeout(600)
  const d = await dialog().innerText()
  await shot('A4-stop-dialog')
  const body = d.split('\n').filter((l) => !/^(Retour|Arrêter|Rendre|Annuler|Motif|Espèces)/.test(l)).join(' ')
  check('A4', body.split(/\s+/).length <= 45, `stop dialog short (${body.split(/\s+/).length} words)`, d)
  const back = dialog().getByRole('button', { name: /^Retour$/ })
  if (await back.count()) await back.click()
})

await scenario('A5', async () => {
  await go(`/treatment-plans/${F.main.plan}`)
  await waitText(main(), /Couronne/)
  // the remise field lives in the act row
  const btn = main().getByRole('button', { name: /remise/i }).first()
  if (await btn.count()) { await btn.click(); await page.waitForTimeout(400) }
  const field = main().locator('input[aria-label*="emise"]:visible, input[id*="discount"]:visible').first()
  if (!(await field.count())) { await shot('A5-no-field'); return bad('A5', 'no in-row remise field') }
  await field.fill('20'); await field.press('Tab'); await page.waitForTimeout(400)
  const t = await main().innerText()
  await shot('A5-bar')
  check('A5', /1 modification/.test(t), 'sticky bar « 1 modification »', t.slice(-400))
  // The browser's Back with an unsaved edit must ask first (review M2)
  await page.evaluate(() => history.back()); await page.waitForTimeout(900)
  const guard = page.locator('[role="alertdialog"]:visible')
  const gtxt = (await guard.count()) ? await guard.innerText() : ''
  await shot('A5-back-guard')
  check('A5', /Abandonner/.test(gtxt), 'browser Back with an unsaved edit asks first', gtxt)
  await guard.getByRole('button', { name: /^Retour$/ }).click().catch(() => {})
  await page.waitForTimeout(500)
  check('A5', page.url().includes(F.main.plan) && (await main().getByText(/1 modification/).count()) > 0, 'after « Retour » the page and the edit are still there', page.url())
  const save = page.getByRole('button', { name: /^Enregistrer$/ }).last()
  await save.click(); await page.waitForTimeout(800)
  const conf = dialog()
  if (await conf.count()) { const txt = await conf.innerText(); bad('A5', 'unexpected dialog after save', txt.slice(0, 200)); return }
  const toast = await toastText(page)
  await settled(page)
  const plan = await apiGet(`/treatment-plans/${F.main.plan}`)
  const it = plan.items.find((i) => i.id === F.main.item)
  check('A5', (it.discountAmount ?? 0) === 20, `remise saved as 20 (got ${it.discountAmount})`, toast)
  check('A5', it.steps.length === 3 && it.steps[1].minDaysAfterPrevious === 7, 'steps kept, séance 2 delay still 7', JSON.stringify(it.steps.map((s) => s.minDaysAfterPrevious)))
  check('A5', plan.totalPlanned === 280, `total 280 (got ${plan.totalPlanned})`)
})

await scenario('A6', async () => {
  await size(1440, 900)
  await go(`/patients/${F.main.patient}`)
  const t = await waitText(main(), /Couronne/)
  await main().getByText(/Tous les traitements/).last().evaluate((el) => el.scrollIntoView({ block: 'center' })).catch(() => {})
  await page.waitForTimeout(500)
  await shot('A6-patient-card')
  check('A6', /Couronne\s*·\s*dent 16/.test(t), 'card name « Couronne · dent 16 »')
  check('A6', /Reste à payer/.test(t), '« Reste à payer » on the card')
  check('A6', !/Plan 20\d\d-/.test(t) && !/Tous les plans/.test(t), 'no « Plan N° » / « Tous les plans »')
  await go(`/patients/${F.draft.patient}`)
  await waitText(main(), /Couronne/)
  const book = main().getByRole('button', { name: /^Planifier\s*:/ }).first()
  if (!(await book.count())) return bad('A6', 'no « Planifier : … » button on the draft card')
  const before = page.url()
  await book.click(); await page.waitForTimeout(1200)
  const d0 = await waitText(dialog(), /ce RDV/, 15000)
  await shot('A6-booking-open')
  check('A6', page.url().split('?')[0] === before.split('?')[0], 'stayed on the patient page')
  check('A6', /Nouveau rendez-vous/.test(d0) && /Couronne/.test(d0) && /ce RDV/.test(d0), 'booking dialog open with the act on its séance', d0.slice(0, 400))
  await size(1536, 730); await shot('A6-booking-1536x730')
  const create = dialog().getByRole('button', { name: /Créer le rendez-vous/ })
  const box = await create.boundingBox().catch(() => null)
  check('C2', box && box.y + box.height <= 730, 'booking primary button inside 730 px', JSON.stringify(box))
})

await scenario('A8', async () => {
  await size(1440, 900)
  await go(`/appointments?patientId=${F.plain.patient}`)
  let d = await dialog().count() ? await dialog().innerText() : ''
  if (!/Nouveau rendez-vous/.test(d)) {
    await page.locator("button[data-size='sm']:has-text('Nouveau')").first().click()
    d = await waitText(dialog(), /Nouveau rendez-vous/, 10000)
  }
  check('A8', new RegExp(F.plain.name.split(' ')[0]).test(d), 'patient preselected from ?patientId')
  await waitText(dialog(), /Sélectionner un type d'acte/, 15000)
  await dialog().getByText("Sélectionner un type d'acte").first().click(); await page.waitForTimeout(500)
  await page.keyboard.type('Couronne'); await page.waitForTimeout(900)
  await page.getByRole('option', { name: /Couronne/ }).first().click(); await page.waitForTimeout(800)
  d = await dialog().innerText()
  await shot('A8-new-multiseance')
  check('A8', /ce RDV/.test(d), 'strip marks séance 1 « ce RDV »', d.slice(0, 800))
  check('A8', /Prix du traitement/.test(d), '« Prix du traitement » field')
  check('A8', /Séances/.test(d) && /Tout en 1 séance/.test(d), '« Séances » and « Tout en 1 séance » offered')
  check('A8', !/Ce rendez-vous est la 1re|Touchez une séance|Aucun devis, aucun numéro/.test(d), 'explanations gone')
  // B3
  const one = dialog().getByRole('button', { name: /Tout en 1 séance/ }).first()
  await one.click(); await page.waitForTimeout(500)
  const d2 = await dialog().innerText()
  check('B3', /Répartir en \d séances/.test(d2) && !/ce RDV/.test(d2), '« Tout en 1 séance » folds the strip, « Répartir en N séances » offered back', d2.slice(0, 400))
  await one.isVisible().catch(() => {})
  await dialog().getByRole('button', { name: /Répartir en \d séances/ }).first().click().catch(() => {})
  await size(320, 720); await page.waitForTimeout(400); await shot('C1-booking-320')
  check('C1', await noHScroll(), 'booking at 320: no horizontal scroll')
  await size(1440, 900)
})

await scenario('A9', async () => {
  await go(`/appointments?patientId=${F.cont.patient}`)
  let d = await dialog().count() ? await dialog().innerText() : ''
  if (!/Nouveau rendez-vous/.test(d)) {
    await page.locator("button[data-size='sm']:has-text('Nouveau')").first().click()
    d = await waitText(dialog(), /Nouveau rendez-vous/, 10000)
  }
  d = await waitText(dialog(), /Continuer/, 10000)
  await shot('A9-continue-list')
  check('A9', /Non terminée le/.test(d) && /Continuer/.test(d), 'card « Non terminée le … » + « Continuer »', d.slice(0, 600))
  check('A9', !/C'est la suite d'une séance précédente/.test(d), 'old link gone')
  const cont = dialog().getByRole('button', { name: /^Continuer/ }).first()
  await cont.click(); await page.waitForTimeout(700)
  const dialogs = await page.locator('[role="dialog"]:visible').count()
  d = await dialog().innerText()
  await shot('A9-continuation-row')
  check('A9', dialogs === 1, 'still one dialog')
  check('A9', /suite du/.test(d) && /Nom de la séance/.test(d) && /Prix du reste/.test(d), 'row « suite du » with the two inline fields', d.slice(0, 800))
})

await scenario('A10', async () => {
  await size(1536, 730)
  await go(`/appointments?appointmentId=${F.main.visit2}`)
  const d = await waitText(dialog(), /Enregistrer/, 15000)
  await shot('A10-edit-footer')
  const btns = (await dialog().getByRole('button').allInnerTexts()).map((s) => s.trim()).filter(Boolean)
  check('A10', btns.includes('Fermer') && btns.includes('Enregistrer'), 'footer Fermer + Enregistrer', btns.join(' | '))
  check('A10', !btns.includes('Supprimer') && !btns.some((b) => /^Annuler$/.test(b)), 'no bare « Supprimer » / « Annuler » in the footer')
  const more = dialog().getByRole('button', { name: /Autres actions/ }).first()
  if (!(await more.count())) return bad('A10', 'no « ⋯ » in the footer')
  await more.click(); await page.waitForTimeout(400)
  const m = await page.locator('[role="menu"]:visible').last().innerText()
  check('A10', /Annuler le rendez-vous/.test(m) && /Supprimer/.test(m), '⋯ menu: « Annuler le rendez-vous » + « Supprimer »', m)
  // B4
  await page.getByRole('menuitem', { name: /Annuler le rendez-vous/ }).first().click(); await page.waitForTimeout(500)
  const ad = page.locator('[role="alertdialog"]:visible').last()
  check('B4', (await ad.count()) === 1, 'cancel asks first (alertdialog)')
  await ad.getByRole('button', { name: /^(Non|Retour|Non, conserver)/ }).first().click().catch(() => {})
  await size(1440, 900)
})

await scenario('A11', async () => {
  await size(1536, 730)
  await go(`/patients/${F.today.patient}?addRecord=1&appointmentId=${F.today.visit2}`)
  const d = await waitText(dialog(), /Fiche de soins/, 20000)
  await shot('A11-fiche-1536x730')
  check('A11', /Fiche de soins/.test(d) && !/Ajouter une fiche médicale|Confirmez ce qui a été réalisé/.test(d), 'title « Fiche de soins · … », no description', d.slice(0, 300))
  check('A11', /Couronne/.test(d) && /Empreinte/.test(d) && /(aujourd'hui|cette séance)/.test(d), 'band + strip with the current séance')
  check('A11', !/Acte planifié|Cette séance : étape|ACTE PRÉVU AU RENDEZ-VOUS/i.test(d), 'old selects / step line / eyebrow gone')
  check('A11', /Inclus dans le traitement/.test(d), 'act tag « Inclus dans le traitement »')
  check('A11', /Payé (aujourd'hui|à cette séance)/.test(d) && /Déjà payé/.test(d) && /Reste à payer/.test(d), 'footer Payé aujourd\'hui + 3 figures')
  const save = dialog().getByRole('button', { name: /Enregistrer la séance/ })
  check('A11', (await save.count()) === 1, 'one save « Enregistrer la séance »')
  const box = await save.boundingBox().catch(() => null)
  check('C2', box && box.y + box.height <= 730, 'fiche save button inside 730 px', JSON.stringify(box))
  const change = dialog().getByRole('button', { name: /Changer/ }).first()
  if (await change.count()) {
    await change.click(); await page.waitForTimeout(400)
    const m = await page.locator('[role="menu"]:visible').last().innerText().catch(() => '')
    check('A11', /Sans traitement/.test(m), '« Changer » menu offers « Sans traitement »', m)
    await page.keyboard.press('Escape')
  } else bad('A11', 'no « Changer » control on the band')
  await size(390, 844); await shot('A11-fiche-390')
  check('C1', await noHScroll(), 'fiche at 390: no horizontal scroll')
  await size(320, 720); await shot('C1-fiche-320')
  check('C1', await noHScroll(), 'fiche at 320: no horizontal scroll')
  const bodyH = await page.evaluate(() => document.querySelector('[data-slot="dialog-body"]')?.clientHeight ?? 0)
  check('C1', bodyH >= 180, `fiche at 320×720: the acts area keeps room (${bodyH}px)`)
  await size(1440, 900)
  // A12 — add an act onto the treatment, read, then take it off again
  const add = dialog().getByRole('button', { name: /Ajouter un autre acte/ }).first()
  await add.click(); await page.waitForTimeout(500)
  await page.keyboard.type('Détartrage'); await page.waitForTimeout(700)
  await page.getByRole('option', { name: /Détartrage/ }).first().click().catch(() => {})
  await page.waitForTimeout(700)
  const d2 = await dialog().innerText()
  await shot('A12-added-act')
  check('A12', /Ajouté au traitement/.test(d2), 'tag « Ajouté au traitement »', d2.slice(0, 900))
  check('A12', /par dent/.test(d2) && /pour tout/.test(d2), 'switch « par dent · pour tout »')
  check('A12', /Prix du traitement/.test(d2), 'footer « Prix du traitement » old → new')
  check('A12', !/Aucun honoraire sur cette séance/.test(d2), 'no « Aucun honoraire » beside a price')
  const del = dialog().getByRole('button', { name: /Supprimer.*Détartrage|Retirer.*Détartrage/ }).first()
  if (await del.count()) { await del.click(); await page.waitForTimeout(400) } else bad('A13', 'cannot find the delete control of the added act')
  // A13 — record séance 2 (throwaway fixture)
  await dialog().getByRole('button', { name: /Enregistrer la séance/ }).click()
  const t = await toastText(page, 12000)
  await settled(page)
  const plan = await apiGet(`/treatment-plans/${F.today.plan}`)
  const s2 = plan.items[0].steps[1]
  check('A13', Boolean(s2.doneDate) && plan.items[0].steps.length === 3 && plan.items.length === 1, 'séance 2 recorded, still 3 séances, no extra act', `${t} · ${JSON.stringify(plan.items[0].steps.map((s) => s.doneDate))}`)
})

await scenario('A7', async () => {
  await size(1440, 900)
  // A7 — main patient, header « Planifier un RDV », then the « Continuer un traitement » card
  await go(`/patients/${F.today.patient}`)
  await page.locator('a:has-text("Planifier un RDV"):visible, button:has-text("Planifier un RDV"):visible').first().click()
  let d = await waitText(dialog(), /Continuer un traitement/, 15000)
  await shot('A7-continue-list')
  check('A7', /Continuer un traitement/.test(d) && !/Prochaine étape|Lequel continuez-vous/.test(d), '« Continuer un traitement » list, old notice gone', d.slice(0, 500))
  check('A7', !new RegExp(F.today.number).test(d.slice(0, d.indexOf('Date') > 0 ? d.indexOf('Date') : 400)), 'no devis number in the list')
  const cont = dialog().locator('button:has-text("Planifier :")').first()
  if (await cont.count()) { await cont.click(); await page.waitForTimeout(900) } else bad('A7', 'no « Planifier : » card in the list')
  d = await waitText(dialog(), /ce RDV/, 10000)
  await shot('A7-devis-act-row')
  check('A7', /ce RDV/.test(d), 'act row strip marks « ce RDV »', d.slice(0, 700))
  check('A7', /Inclus dans le traitement/.test(d), 'tag « Inclus dans le traitement »')
  check('A7', !/Chiffré sur le devis|L'acte entier est chiffré|cette séance n'ajoute/.test(d), 'devis paragraph gone')
  await size(1536, 730); await shot('A7-booking-1536x730')
  const create7 = dialog().getByRole('button', { name: /Créer le rendez-vous/ })
  const box7 = await create7.boundingBox().catch(() => null)
  check('C2', box7 && box7.y + box7.height <= 730, 'booking (devis act) primary button inside 730 px', JSON.stringify(box7))
  await size(1440, 900)
})

await scenario('A14', async () => {
  await go('/treatment-plans')
  const t = await waitText(main(), /Traitements/)
  await shot('A14-lists')
  check('A14', !/étape \d+ \/ \d+|\d+ \/ \d+ séances?\b/.test(t), 'no bare « étape N / M » or « N / M séances »', t.slice(0, 500))
  check('A14', /Traitements en cours/i.test(t), 'heading « Traitements en cours »')
  await size(390, 844); await shot('A14-lists-390')
  check('A14', await noHScroll(), 'lists at 390: no horizontal scroll')
  await size(1440, 900)
})

await scenario('A15', async () => {
  await go('/a-cloturer?tab=suites')
  let t = await waitText(main(), /Suites à planifier/)
  const tab = main().getByRole('tab', { name: /Suites à planifier/ })
  if (await tab.count()) { await tab.click(); await page.waitForTimeout(800) }
  t = await waitText(main(), new RegExp(F.cont.name.split(' ')[0]), 15000)
  const row = main().locator(`text=${F.cont.name.split(' ')[0]}`).first()
  if (!(await row.count())) return skip('A15', 'cont patient not listed in « Suites à planifier »')
  const btn = main().getByRole('button', { name: /Planifier la suite/ }).first()
  await btn.click(); await page.waitForTimeout(1500)
  const d = await waitText(dialog(), /suite du/, 25000)
  const n = await page.locator('[role="dialog"]:visible').count()
  await shot('A15-one-dialog')
  check('A15', n === 1 && /Nouveau rendez-vous/.test(d) && /suite du/.test(d), 'one booking dialog with the continuation row', d.slice(0, 500))
  const d15 = await waitText(dialog(), /RÉCAPITULATIF[^]{0,40}QAU/i, 8000)
  check('A15', !/Patient à choisir/.test(d15), 'récap names the patient', d15.slice(d15.indexOf('RÉCAPITULATIF'), d15.indexOf('RÉCAPITULATIF') + 120))
})

// ───────────────────────────── Tier B ─────────────────────────────
await scenario('B1', async () => {
  await go(`/treatment-plans/${F.draft.plan}`)
  const t = await waitText(main(), /Couronne/)
  await shot('B1-draft')
  check('B1', /À commencer/.test(t), 'badge « À commencer »', t.slice(0, 300))
  check('B1', /Pas de devis/.test(t), 'chip « Pas de devis »')
  check('B1', /Créer le devis/.test(t) || (await main().getByRole('button', { name: /Créer le devis/ }).count()) > 0, '« Créer le devis » reachable')
  check('B1', !/Sans devis|Aucun devis édité/.test(t), 'no « Sans devis » badge / caption')
})

await scenario('B2', async () => {
  await go(`/patients/${F.draft.patient}`)
  const t = await waitText(main(), /Couronne/)
  await main().getByText(/Tous les traitements/).last().evaluate((el) => el.scrollIntoView({ block: 'center' })).catch(() => {})
  await page.waitForTimeout(500)
  await shot('B2-draft-card')
  check('B2', !/À accepter/.test(t), 'no « À accepter » headline')
  check('B2', (await main().getByRole('button', { name: /^Planifier\s*:\s*Préparation/ }).count()) > 0, 'primary « Planifier : Préparation »', t.slice(0, 400))
})

await scenario('B5', async () => {
  await go(`/treatment-plans/${F.main.plan}`)
  await waitText(main(), /Couronne/)
  await main().getByRole('button', { name: /Actions|Autres actions|⋯/ }).first().click(); await page.waitForTimeout(400)
  const item = page.getByRole('menuitem', { name: /Annuler le devis|Supprimer/ }).first()
  if (!(await item.count())) { await page.keyboard.press('Escape'); return skip('B5', 'neither Annuler nor Supprimer offered on this plan') }
  await item.click(); await page.waitForTimeout(600)
  const d = await dialog().innerText()
  await shot('B5-destructive')
  check('B5', d.split(/\s+/).length <= 60, `destructive confirm short (${d.split(/\s+/).length} words)`, d)
  await dialog().getByRole('button', { name: /^Retour$/ }).first().click().catch(() => {})
})

// ───────────────────────────── Tier C ─────────────────────────────
await scenario('C1', async () => {
  await size(320, 720)
  await go(`/treatment-plans/${F.main.plan}`)
  await waitText(main(), /Couronne/)
  await shot('C1-plan-320')
  check('C1', await noHScroll(), 'treatment page at 320: no horizontal scroll')
  await size(1440, 900)
})

await scenario('C3', async () => {
  const plan = await apiGet(`/treatment-plans/${F.main.plan}`)
  const it = plan.items.find((i) => i.id === F.main.item)
  check('C3', (it.discountAmount ?? 0) === 20 && it.steps[1].minDaysAfterPrevious === 7, 'after reload: remise 20, delay 7')
})

// ───────────────────────────── Tier D ─────────────────────────────
await scenario('D1', async () => {
  await go('/appointments')
  await page.waitForTimeout(1500)
  const dots = page.locator('button[aria-label^="Actions du rendez-vous"]:visible').first()
  if (!(await dots.count())) return skip('D1', 'no RDV « ⋯ » on today\'s agenda view')
  await dots.click(); await page.waitForTimeout(600)
  await shot('D1-quick-actions')
  const items = await page.getByRole('menuitem').allInnerTexts().catch(() => [])
  check('D1', items.some((x) => /Supprimer/i.test(x)), 'agenda quick actions open', items.join(' | '))
})

await scenario('D2', async () => {
  await go(`/patients/${F.main.patient}?tab=plans`)
  const tab = main().getByRole('tab', { name: /Traitements/ }).first()
  if (await tab.count()) { await tab.click(); await page.waitForTimeout(1000) }
  const t = await main().innerText()
  await shot('D2-patient-tab')
  check('D2', /Couronne/.test(t) && !/Plan de traitement/.test(t), 'patient « Traitements » tab renders the plan', t.slice(0, 300))
})

await scenario('D3', async () => {
  await go('/procedure-types')
  const t = await waitText(main(), /Couronne/)
  await shot('D3-catalogue')
  check('D3', /Couronne/.test(t), 'catalogue renders')
  const strip = main().locator('ol[aria-label]').first()
  check('D3', (await strip.count()) > 0, 'a protocol is drawn as the séance strip')
})

await scenario('D4', async () => {
  await go(`/patients/${F.main.patient}`)
  await waitText(main(), /Reste à payer/)
  const chip = main().getByRole('button', { name: /^Reste à payer .*encaisser/ }).first()
  if (await chip.count()) { await chip.click(); await page.waitForTimeout(1000) }
  const enc = main().locator('button[aria-label^="Encaisser sur"]:visible').first()
  if (!(await enc.count())) return skip('D4', 'no « Encaisser » on the patient page')
  await enc.click(); await page.waitForTimeout(700)
  const d = await dialog().innerText().catch(() => '')
  await shot('D4-payment-modal')
  check('D4', /Enregistrer|Encaisser/.test(d), 'payment modal opens', d.slice(0, 200))
})

check('D5', consoleErrors.length === 0, 'no pageerror on any page walked', JSON.stringify(consoleErrors.slice(0, 5)))

await browser.close()
console.log('\n──── FINDINGS ────')
const real = findings.filter((f) => !f.skip)
findings.forEach((f) => console.log(JSON.stringify(f)))
console.log(real.length ? `RED (${real.length})` : 'ALL CLEAR')
