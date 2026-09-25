// Diagnostic: does Back ask before discarding a typed dialog? Five doors, one launch. Usage: node probe-back-guard.mjs [label]
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { openSignedIn, settled, WEB } from './session.mjs'

const here = dirname(fileURLToPath(import.meta.url))
const F1 = JSON.parse(readFileSync(join(here, 'fixtures.json'), 'utf8'))
const LABEL = process.argv[2] ?? 'current'
const ONLY = process.argv[3]
const { browser, page } = await openSignedIn({ width: 1536, height: 730 })
page.on('dialog', (d) => (d.type() === 'beforeunload' ? d.accept() : d.dismiss()).catch(() => {}))
const go = async (p) => { await page.goto(`${WEB}${p}`, { waitUntil: 'domcontentloaded' }); await settled(page) }
const top = () => page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible').last()
const discard = () => page.locator('[role="alertdialog"]:visible').filter({ hasText: /Abandonner les modifications/ })
const state = () => page.evaluate(() => ({ len: history.length, st: JSON.stringify(history.state ?? null).slice(0, 80), url: location.pathname + location.search }))
async function waitText(loc, re, ms = 15000) {
  const end = Date.now() + ms; let t = ''
  while (Date.now() < end) { t = (await loc.innerText().catch(() => '')) || ''; if (re.test(t)) return t; await page.waitForTimeout(250) }
  return t
}
async function reset() {
  for (let i = 0; i < 4; i++) {
    if (await discard().count()) { await discard().getByRole('button', { name: /^Abandonner$/ }).click(); await page.waitForTimeout(400); continue }
    if (!(await top().count())) return
    await page.keyboard.press('Escape'); await page.waitForTimeout(400)
  }
}
async function variant(name, open, type, { escapeFirst = false } = {}) {
  if (ONLY && !name.includes(ONLY)) return
  try {
    await open()
    await page.waitForTimeout(1200)
    const before = await state()
    await type()
    await page.waitForTimeout(300)
    if (escapeFirst) {
      await page.keyboard.press('Escape'); await page.waitForTimeout(600)
      const asked = await discard().count()
      if (asked) { await discard().getByRole('button', { name: /Continuer la saisie/ }).click(); await page.waitForTimeout(500) }
    }
    const mid = await state()
    await page.goBack().catch(() => {}); await page.waitForTimeout(1200)
    const asked = await discard().count()
    const open2 = (await page.locator('[role="dialog"]:visible').count()) > 0
    const after = await state()
    console.log(`${asked ? '✅' : '❌'} [${LABEL}] ${name}: Back ${asked ? 'ASKS' : 'does NOT ask'} · dialog ${open2 ? 'still open' : 'closed'}`)
    console.log(`     before ${JSON.stringify(before)}\n     typed  ${JSON.stringify(mid)}\n     after  ${JSON.stringify(after)}`)
  } catch (e) { console.log(`⚠️ [${LABEL}] ${name}: threw ${e.message.split('\n')[0]}`) }
  await reset()
}
const typeTime = async () => { await top().getByLabel(/Heure de début/).first().fill('10:15') }
const patient = (F1.empty ?? F1.cont).patient

await variant('booking via « Nouveau » (no deep link)', async () => {
  await go('/appointments'); await page.waitForTimeout(800)
  await page.locator("button[data-size='sm']:has-text('Nouveau')").first().click(); await waitText(top(), /Nouveau rendez-vous/)
}, typeTime)
await variant('booking via ?patientId deep link', async () => {
  await go(`/appointments?patientId=${patient}`); await waitText(top(), /Nouveau rendez-vous/)
}, typeTime)
await variant('booking via « Nouveau », Escape → Continuer first', async () => {
  await go('/appointments'); await page.waitForTimeout(800)
  await page.locator("button[data-size='sm']:has-text('Nouveau')").first().click(); await waitText(top(), /Nouveau rendez-vous/)
}, typeTime, { escapeFirst: true })
await variant('fiche via ?addRecord deep link', async () => {
  await go(`/patients/${F1.today.patient}?addRecord=1&appointmentId=${F1.today.visit2}`); await waitText(top(), /Fiche de soins/, 20000)
}, async () => {
  const byLabel = top().getByLabel(/Payé/).first()
  if (await byLabel.count()) await byLabel.fill('1')
  else await top().locator('input[inputmode="decimal"]:visible').first().fill('1')
})
await variant('patient edit form (no deep link)', async () => {
  await go(`/patients/${patient}`); await page.waitForTimeout(1000)
  await page.getByRole('button', { name: /^Modifier$|Modifier le patient|Modifier la fiche patient/ }).first().click(); await waitText(top(), /Prénom|Nom/)
}, async () => { await top().getByLabel(/Motif de consultation/).first().fill('probe') })
await browser.close()
