// Browser checks for the six code-review fixes (fix 7) and the shared dialog guard they touched.
// One launch, fixes nothing, collects every finding. Writes only to throwaway fixtures, and the two writes
// (a séance added to F2.multi, « Empreinte » removed from F1.main) run last. Usage: node regress-fix7.mjs [run]
import { readFileSync, mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { openSignedIn, settled, toastText, WEB } from './session.mjs'
import { totp, msToFreshWindow } from '../../devis-fiche-rdv-flexibility/qa/totp-act-onto-devis.mjs'

const here = dirname(fileURLToPath(import.meta.url))
const F1 = JSON.parse(readFileSync(join(here, 'fixtures.json'), 'utf8'))
const F2 = JSON.parse(readFileSync(join(here, 'fixtures-2.json'), 'utf8'))
const RUN = process.argv[2] ?? 'fix7'
const SHOTS = join(here, 'shots', RUN)
mkdirSync(SHOTS, { recursive: true })
const API = 'http://localhost:5000/api'

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${String(d).slice(0, 300)}`); findings.push({ id, m, d: String(d).slice(0, 500) }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, skip: why }) }
const check = (id, cond, m, d = '') => (cond ? ok(id, m) : bad(id, m, d))

let TOKEN = null
{
  await new Promise((r) => setTimeout(r, msToFreshWindow()))
  const r = await fetch(`${API}/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totpCode: totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO') }),
  })
  const j = await r.json(); TOKEN = j.value?.accessToken ?? j.accessToken
}
const apiGet = async (p) => { const r = await fetch(`${API}${p}`, { headers: { Authorization: `Bearer ${TOKEN}` } }); const j = await r.json(); return j?.value ?? j }

const { browser, page, consoleErrors } = await openSignedIn({ width: 1536, height: 730 })
page.on('dialog', (d) => (d.type() === 'beforeunload' ? d.accept() : d.dismiss()).catch(() => {}))
const shot = (n) => page.screenshot({ path: join(SHOTS, `${n}.png`) }).catch(() => {})
const go = async (p) => { await page.goto(`${WEB}${p}`, { waitUntil: 'domcontentloaded' }); await settled(page) }
async function waitText(loc, re, ms = 15000) {
  const end = Date.now() + ms; let t = ''
  while (Date.now() < end) { t = (await loc.innerText().catch(() => '')) || ''; if (re.test(t)) return t; await page.waitForTimeout(250) }
  return t
}
const main = () => page.locator('main').first()
const dialogs = () => page.locator('[role="dialog"]:visible')
const top = () => page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible').last()
const discard = () => page.locator('[role="alertdialog"]:visible').filter({ hasText: /Abandonner les modifications/ })
const pause = (ms = 600) => page.waitForTimeout(ms)
async function answerDiscard(keep) {
  const d = discard()
  if (!(await d.count())) return false
  await d.getByRole('button', { name: keep ? /Continuer la saisie/ : /^Abandonner$/ }).click(); await pause(500)
  return true
}
async function closeAll() {
  for (let i = 0; i < 5; i++) {
    if (!(await top().count())) return
    if (await answerDiscard(false)) continue
    await page.keyboard.press('Escape'); await pause(450)
  }
}
async function scenario(id, fn) {
  try { await fn() } catch (e) { bad(id, 'threw', e.message.split('\n')[0]); await shot(`${id}-threw`) }
  await closeAll().catch(() => {})
}
async function bookingFromCard(patientId) {
  await go(`/patients/${patientId}`)
  await page.locator('a:has-text("Planifier un RDV"):visible, button:has-text("Planifier un RDV"):visible').first().click()
  await waitText(top(), /Continuer un traitement/)
  await top().locator('button:has-text("Planifier :")').first().click()
  await waitText(top(), /ce RDV/, 10000)
}
async function openSeancesWindow() {
  await top().getByRole('button', { name: /^Modifier les séances de/ }).first().click(); await pause(500)
  await top().getByRole('button', { name: /Ajouter une séance au traitement/ }).first().click()
  await waitText(dialogs().last(), /Enregistrer les séances/, 10000)
}

// ── G1 · the shared guard, ONE dialog: the booking asks before discarding typed input (Escape and Back) ──────
await scenario('G1', async () => {
  const patient = (F1.empty ?? F1.cont).patient
  await go(`/appointments?patientId=${patient}`)
  let t = await waitText(top(), /Nouveau rendez-vous/, 8000)
  if (!/Nouveau rendez-vous/.test(t)) { await page.locator("button[data-size='sm']:has-text('Nouveau')").first().click(); await waitText(top(), /Nouveau rendez-vous/) }
  const time = top().getByLabel(/Heure de début/).first()
  await time.fill('10:15'); await pause(300)
  await page.keyboard.press('Escape'); await pause(600)
  check('G1', (await discard().count()) === 1, 'Escape on a typed booking asks « Abandonner les modifications ? »')
  await answerDiscard(true)
  check('G1', (await dialogs().count()) === 1, '« Continuer la saisie » keeps the booking open')
  // Back on a booking closes it without asking — the same on 5b430580, before this work (probe-back-guard.mjs).
  skip('G1', 'Back on a booking/fiche never asked, before this work either — pre-existing, see probe-back-guard.mjs')
})

// ── N · fix 4: a séances window over a booking — only the upper window answers ─────────────────────────────
await scenario('N', async () => {
  await bookingFromCard(F2.multi.patient)
  const url = page.url()
  await openSeancesWindow()
  check('N', (await dialogs().count()) === 2, 'séances window open over the booking')
  const win = dialogs().last()
  await win.locator('input[placeholder="Nom de la séance"]').last().fill('Essai nested'); await pause(300)
  await page.goBack(); await pause(900)
  await shot('N-back')
  check('N', (await discard().count()) === 1, 'Back asks ONE discard question (the séances window)', `prompts=${await discard().count()}`)
  await answerDiscard(true)
  check('N', (await dialogs().count()) === 2 && page.url() === url, 'both windows still open, page unchanged', `dialogs=${await dialogs().count()} ${page.url()}`)
  await win.getByRole('button', { name: /^Annuler$/ }).first().click(); await pause(600)
  const asked = await discard().count()
  if (asked) await answerDiscard(false)
  check('N', (await dialogs().count()) === 1, 'Annuler on the séances window leaves the booking open', `asked=${asked}`)
  await page.keyboard.press('Escape'); await pause(700)
  const bookingAsked = (await discard().count()) > 0
  check('N', !bookingAsked, 'the booking was NOT marked dirty by typing in the séances window')
  await shot('N-after')
})

// ── F2 · fix 2 + 5: a REOPENED fiche puts the original act back ────────────────────────────────────────────
await scenario('F2', async () => {
  await go(`/patients/${F1.main.patient}?editRecord=${F1.main.record1}`)
  await waitText(top(), /Couronne/, 20000); await pause(800)
  const before = await top().innerText()
  const card = (t) => (t.match(/Couronne[^\n]*\n[^]*?Changer d'acte/) ?? [''])[0].replace(/\s+/g, ' ')
  await shot('F2-open')
  await top().getByRole('button', { name: /Changer d'acte/ }).first().click(); await pause(500)
  await page.keyboard.type('Couronne provisoire', { delay: 30 }); await pause(700)
  await page.getByRole('option', { name: /Couronne provisoire/ }).first().click(); await pause(800)
  const mid = await waitText(top(), /n'est plus dans cette séance/, 6000)
  check('F2', /n'est plus dans cette séance/.test(mid), 'reopened fiche: the band says the treatment act left the séance')
  await top().getByRole('button', { name: /^Remettre/ }).first().click(); await pause(900)
  const after = await top().innerText()
  await shot('F2-restored')
  check('F2', !/n'est plus dans cette séance/.test(after), 'after « Remettre » the notice is gone')
  check('F2', /Couronne provisoire/.test(after) && /Couronne \/ bridge|Couronne\b/.test(after), 'the replacement stays and the Couronne is back')
  const detailsBefore = (before.match(/Détails[^\n]*\n?[^\n]*/) ?? [''])[0]
  check('F2', after.includes(detailsBefore.trim().split('\n').pop() ?? '###'), 'the restored card carries the saved détails (état · faces)', `before « ${detailsBefore} »`)
  void card
})

// ── S6 · fix 6 + the séances save flowing back (writes a séance on F2.multi) ───────────────────────────────
await scenario('S6', async () => {
  await go(`/treatment-plans/${F2.multi.plan}`)
  await waitText(main(), /Couronne/)
  await main().getByRole('button', { name: /^Planifier\s*:/ }).first().click()
  await waitText(top(), /ce RDV/, 15000)
  await openSeancesWindow()
  await dialogs().last().locator('input[placeholder="Nom de la séance"]').last().fill('Essai flow')
  await dialogs().last().getByRole('button', { name: /Enregistrer les séances/ }).click()
  const t = await waitText(top(), /Essai flow/, 12000)
  check('S6', /Essai flow/.test(t) && (await dialogs().count()) === 1, 'saved séance shows in the booking row, booking still open')
  const add = top().getByText(/Ajouter un autre acte|Sélectionner un type d'acte/).first()
  await add.click(); await pause(800)
  const groups = await page.locator('[cmdk-group-heading], [data-slot="command-group"] [cmdk-group-heading]').allInnerTexts().catch(() => [])
  await shot('S6-picker')
  check('S6', !groups.some((g) => /Actes du devis/i.test(g)), 'workspace « Planifier » flow: no « Actes du devis » group', groups.join(' | '))
  await page.keyboard.press('Escape'); await pause(300)
})

// ── F1 · fix 1: edit RDV, remove the séance this RDV books, save — no 409 (writes on F1.main) ─────────────────
await scenario('F1', async () => {
  await go(`/appointments?appointmentId=${F1.main.visit2}`)
  await waitText(top(), /Enregistrer/, 15000)
  await openSeancesWindow()
  const win = dialogs().last()
  const del = win.getByRole('button', { name: /Supprimer la séance « Empreinte »/ }).first()
  if (!(await del.count())) return skip('F1', 'no delete control on « Empreinte » in the séances window')
  await del.click(); await pause(400)
  // « + Ajouter une séance » opened with a new empty row — name it, or the save is (rightly) refused.
  await win.locator('input[placeholder="Nom de la séance"]').last().fill('Contrôle F1')
  await win.getByRole('button', { name: /Enregistrer les séances/ }).click(); await pause(1200)
  for (let i = 0; i < 2; i++) {
    const c = page.locator('[role="alertdialog"]:visible').last()
    if (!(await c.count())) break
    await shot(`F1-confirm-${i}`)
    await c.getByRole('button').filter({ hasNotText: /^(Annuler|Retour|Non|Continuer la saisie)/ }).last().click(); await pause(1200)
  }
  await waitText(top(), /Enregistrer/, 8000)
  check('F1', (await dialogs().count()) === 1, 'back in the edit dialog after the séances save')
  const notesToggle = top().getByRole('button', { name: /Notes/ }).first()
  if (await notesToggle.count()) { await notesToggle.click(); await pause(300) }
  const notes = top().locator('textarea:visible').first()
  if (await notes.count()) await notes.fill(`note fix7 ${Date.now() % 100000}`)
  await top().getByRole('button', { name: /^Enregistrer$/ }).last().click(); await pause(1500)
  const toast = await toastText(page, 8000)
  const still = await top().innerText().catch(() => '')
  await shot('F1-after-save')
  check('F1', !/modifié par quelqu'un d'autre|Recharger/.test(still + toast), 'no 409 after the nested séances save', toast)
  const appt = await apiGet(`/appointments/${F1.main.visit2}`)
  const plan = await apiGet(`/treatment-plans/${F1.main.plan}`)
  const empId = F1.main.steps[1].id
  const steps = plan.items.find((i) => i.id === F1.main.item)?.steps ?? []
  check('F1', !steps.some((s) => s.id === empId), '« Empreinte » removed from the treatment', steps.map((s) => s.label).join(', '))
  check('F1', !(appt.procedures ?? []).some((p) => p.treatmentPlanItemStepId === empId), 'the RDV no longer books the removed séance', JSON.stringify(appt.procedures?.map((p) => p.treatmentPlanItemStepId)))
  check('F1', /note fix7/.test(appt.notes ?? ''), 'the notes edit was saved', appt.notes)
})

check('Z', consoleErrors.length === 0, 'no pageerror', JSON.stringify(consoleErrors.slice(0, 5)))
await browser.close()
console.log('\n──── FINDINGS ────')
findings.forEach((f) => console.log(JSON.stringify(f)))
console.log(findings.filter((f) => !f.skip).length ? `RED (${findings.filter((f) => !f.skip).length})` : 'ALL CLEAR')
