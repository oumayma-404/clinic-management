// One launch over the whole plan (qa/plan.md). Collects, never stops. Run from a folder holding playwright-core.
import { readFileSync, mkdirSync } from 'node:fs'
import { execFileSync } from 'node:child_process'
import { openSignedIn, settled, toastText, WEB } from './session.mjs'

const f = JSON.parse(readFileSync(new URL('./fixtures.json', import.meta.url), 'utf8'))
const SHOTS = process.env.SHOTS ?? new URL('./shots/', import.meta.url).pathname.replace(/^\/([A-Z]:)/, '$1')
mkdirSync(SHOTS, { recursive: true })

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }

const sql = (q) => execFileSync('docker', ['exec', '-i', 'clinic-postgres', 'psql', '-U', 'clinic_user',
  '-d', 'clinic_management', '-At', '-F', '|', '-c', q], { encoding: 'utf8' }).trim()

const { browser, page, consoleErrors } = await openSignedIn({ width: 1440, height: 900 })
const D = '[data-slot="dialog-content"]'
const dlgText = async () => (await page.locator(D).first().innerText()).replace(/\s+/g, ' ')
const saveBtn = () => page.locator(`${D} button:has-text('Confirmer la séance'), ${D} button:has-text('Enregistrer')`).first()
const frDate = (isoDay) => isoDay.split('-').reverse().join('/')

// ⚠️ The patient page strips `?addRecord` with its own router.replace, which interrupts the NEXT goto; and the
// first compile of /patients/[id] on a fresh dev server outlasts 30 s. Both are the probe, not the product.
async function go(url) {
  for (let i = 0; i < 3; i++) {
    try { await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 90000 }); return }
    catch (e) { if (i === 2) throw e; await page.waitForTimeout(1500) }
  }
}
async function openNewFiche(patientId, visitId) {
  await go(`${WEB}/patients/${patientId}?addRecord=1&appointmentId=${visitId}`)
  await settled(page)
  await page.waitForSelector(D, { timeout: 20000 })
  await page.waitForTimeout(1200)
}
async function openExistingFiche(patientId, isoDay) {
  await go(`${WEB}/patients/${patientId}`)
  await settled(page)
  const btn = page.locator(`button[aria-label="Modifier la fiche du ${frDate(isoDay)}"]:visible`).first()
  await btn.waitFor({ timeout: 20000 })
  await btn.click()
  await page.waitForSelector(D, { timeout: 20000 })
  await page.waitForTimeout(1500)
}
let lastSave = null
page.on('response', async (r) => {
  if (/\/dental-records(\/[^/]+)?$/.test(new URL(r.url()).pathname) && ['POST', 'PUT'].includes(r.request().method())) {
    lastSave = { status: r.status(), body: await r.json().catch(() => null) }
  }
})
async function saveAndReadToast() {
  const save = saveBtn()
  if (await save.isDisabled()) return { disabled: true, toast: '' }
  lastSave = null
  // Poll CONCURRENTLY with the click — sonner can dismiss a toast before a poll started afterwards lands.
  const polling = toastText(page, 15000)
  await save.click()
  const polled = await polling
  await page.waitForTimeout(1200)
  const toast = (await allToasts()) || polled
  const stillOpen = await page.locator(D).count()
  return { disabled: false, toast, stillOpen }
}

// Every sonner toast ever shown, captured by an observer — polling can miss a toast shown during a navigation.
await page.addInitScript(() => {
  window.__toasts = []
  new MutationObserver(() => {
    for (const el of document.querySelectorAll('[data-sonner-toast]')) {
      const t = el.innerText.replace(/\s+/g, ' ')
      if (t && !window.__toasts.includes(t)) window.__toasts.push(t)
    }
  }).observe(document, { subtree: true, childList: true, characterData: true })
})
const allToasts = () => page.evaluate(() => (window.__toasts ?? []).join(' | ')).catch(() => '')

// Warm the dynamic route once so the first scenario is not measuring a cold compile.
await go(`${WEB}/patients/${f.plain.patient}`).catch(() => {})
await settled(page, 60000)

const scenarios = []
const scenario = (id, run) => scenarios.push({ id, run })

// ══ A — the reported case ══════════════════════════════════════════════════════════════════════════
scenario('A1', async () => {
  await openNewFiche(f.main.patient, f.main.visit2)
  await page.screenshot({ path: `${SHOTS}/A1-1440.png` }).catch(() => {})
  const t = await dlgText()
  const field = page.locator(`${D} #collected-on-plan`)
  const p = []
  if (await field.count() !== 1) p.push('no #collected-on-plan field')
  if (!/Prix du traitement\s*160,000/.test(t)) p.push('Prix ≠ 160,000')
  if (!/Déjà payé\s*100,000/.test(t)) p.push('Déjà payé ≠ 100,000')
  if (!/Reste à payer\s*60,000/.test(t)) p.push('Reste ≠ 60,000')
  if (/Payé \(séance\)/.test(t)) p.push('a séance « Payé » is still offered')
  const m = t.match(/Prix du traitement.{0,80}/)
  p.length ? bad('A1', p.join(' · '), m?.[0] ?? t.slice(0, 300)) : ok('A1', m?.[0])
})

scenario('D2', async () => {
  const count = await page.locator(`${D} :text("Ajouter au devis")`).count()
  count === 0 ? ok('D2', '« Ajouter au devis » absent on the bridged séance') : bad('D2', '« Ajouter au devis » offered', '')
})

scenario('A2', async () => {
  await page.locator(`${D} #collected-on-plan`).fill('60')
  await page.waitForTimeout(500)
  const r = await saveAndReadToast()
  await page.screenshot({ path: `${SHOTS}/A2-after-save.png` }).catch(() => {})
  if (r.disabled) return bad('A2', 'save disabled with 60 typed', await dlgText())
  const tc = (lastSave?.body?.value ?? lastSave?.body)?.treatmentCollection
  console.log('     A2 save response:', lastSave?.status, JSON.stringify(tc))
  if (tc?.outcome !== 'Collected' || tc?.noteNumber !== f.main.linkedInvoice)
    return bad('A2', 'save did not report a collection on the note', JSON.stringify(tc))
  if (!new RegExp(`note n° ${f.main.linkedInvoice}`).test(r.toast)) return bad('A2', 'toast does not name the note', r.toast)
  if (/refus|dépasse|échoué/i.test(r.toast)) return bad('A2', 'refusal in the toast', r.toast)
  ok('A2', r.toast.slice(0, 160))
})

scenario('A3', async () => {
  const note = sql(`SELECT "Status","AmountCollected" FROM "Invoices" WHERE "Number"='${f.main.linkedInvoice}'`)
  const tagged = sql(`SELECT count(*), coalesce(sum(p."Amount"),0) FROM "Payments" p JOIN "Invoices" i ON i."Id"=p."InvoiceId"
    WHERE i."Number"='${f.main.linkedInvoice}' AND p."DentalRecordId" IS NOT NULL AND NOT p."IsVoided"`)
  const ech = sql(`SELECT coalesce(sum("AmountPaid"),0) FROM "Installments" WHERE "TreatmentPlanId"='${f.main.plan}'`)
  ;(note === '3|160.000' && tagged === '1|60.000' && ech === '0.000')
    ? ok('A3', `note ${note} · tagged ${tagged} · échéancier ${ech}`)
    : bad('A3', 'stored state', `note ${note} · tagged ${tagged} · échéancier ${ech}`)
})

scenario('A4', async () => {
  await openExistingFiche(f.main.patient, f.today)
  const v = await page.locator(`${D} #collected-on-plan`).inputValue().catch(() => null)
  if (v === null) return bad('A4', 'field absent on reopen', (await dlgText()).slice(0, 300))
  if (!/^60/.test(v)) return bad('A4', 'reopened field does not read 60', v)
  const r = await saveAndReadToast()
  const tagged = sql(`SELECT count(*) FROM "Payments" p JOIN "Invoices" i ON i."Id"=p."InvoiceId"
    WHERE i."Number"='${f.main.linkedInvoice}' AND p."DentalRecordId" IS NOT NULL AND NOT p."IsVoided"`)
  if (r.disabled) return bad('A4', 're-save disabled', '')
  if (/refus|dépasse|diminue/i.test(r.toast)) return bad('A4', 'refusal on re-save', r.toast)
  tagged === '1' ? ok('A4', `field ${v}; re-save took nothing (${r.toast.slice(0, 80)})`) : bad('A4', 'second payment', tagged)
})

scenario('A5', async () => {
  await openExistingFiche(f.main.patient, f.main.record1Date)
  const r = await saveAndReadToast()
  const t = r.stillOpen ? await dlgText() : ''
  if (r.disabled) return bad('A5', 'séance 1 re-save disabled', t.slice(0, 300))
  if (/diminue|Corriger la note/i.test(r.toast + t)) return bad('A5', 'séance 1 refused as lowered', (r.toast + ' ' + t).slice(0, 300))
  r.stillOpen ? bad('A5', 'dialog still open after save', t.slice(0, 300)) : ok('A5', `saved (${r.toast.slice(0, 80)})`)
})

scenario('D3', async () => {
  await go(`${WEB}/patients/${f.main.patient}`)
  await settled(page)
  // Delete lives behind the row's « ⋯ » menu (a safety decision), so open the menu first.
  await page.locator(`button[aria-label="Autres actions sur la fiche du ${frDate(f.today)}"]:visible`).first().click()
  await page.locator('[role="menuitem"]', { hasText: `Supprimer la fiche de soins du ${frDate(f.today)}` }).click()
  const ad = page.locator('[role="alertdialog"]')
  await ad.waitFor({ timeout: 10000 })
  await page.waitForTimeout(2500)
  const t = (await ad.innerText()).replace(/\s+/g, ' ')
  await page.screenshot({ path: `${SHOTS}/D3-preview.png` }).catch(() => {})
  await ad.locator('button:has-text("Annuler")').click()
  new RegExp(`60,000 DT encaissés à cette séance sur la note n° ${f.main.linkedInvoice}`).test(t)
    ? ok('D3', 'preview lists the note collection') : bad('D3', 'preview does not list it', t.slice(0, 400))
})

// ══ C — 390 px ═════════════════════════════════════════════════════════════════════════════════════
scenario('C1', async () => {
  await page.setViewportSize({ width: 390, height: 844 })
  await openNewFiche(f.over.patient, f.over.visit2)
  const field = page.locator(`${D} #collected-on-plan`)
  if (await field.count() !== 1) return bad('C1', 'field absent at 390', '')
  await field.scrollIntoViewIfNeeded()
  await page.screenshot({ path: `${SHOTS}/C1-390.png` }).catch(() => {})
  const over = await page.evaluate(() => {
    const d = document.querySelector('[data-slot="dialog-content"]')
    const dl = document.getElementById('collected-on-plan-hint')
    const r = dl?.getBoundingClientRect(), dr = d?.getBoundingClientRect()
    return { dl: r && [Math.round(r.left), Math.round(r.right)], dlg: dr && [Math.round(dr.left), Math.round(dr.right)],
      page: document.documentElement.scrollWidth }
  })
  const fits = over.dl && over.dl[0] >= over.dlg[0] && over.dl[1] <= over.dlg[1] && over.page <= 390
  fits ? ok('C1', JSON.stringify(over)) : bad('C1', 'money row overflows at 390', JSON.stringify(over))
  await page.setViewportSize({ width: 1440, height: 900 })
})

// ══ B — more than the note awaits ══════════════════════════════════════════════════════════════════
scenario('B1', async () => {
  await openNewFiche(f.over.patient, f.over.visit2)
  await page.locator(`${D} #collected-on-plan`).fill('70')
  await page.waitForTimeout(500)
  const t = await dlgText()
  const disabled = await saveBtn().isDisabled()
  ;/Il ne reste que 60,000 DT/.test(t) && disabled
    ? ok('B1', 'refusal named, save disabled')
    : bad('B1', 'over-collection not stopped', `disabled=${disabled} · ${(t.match(/Il ne reste.{0,40}/) ?? [''])[0]}`)
  await page.keyboard.press('Escape')
})

// ══ D — an ordinary devis still collects on its échéancier ═══════════════════════════════════════════
scenario('D1', async () => {
  await openNewFiche(f.plain.patient, f.plain.visit2)
  const t = await dlgText()
  const p = []
  if (await page.locator(`${D} #collected-on-plan`).count() !== 1) p.push('no treatment field')
  if (!/Reste à payer\s*200,000/.test(t)) p.push('Reste ≠ 200,000')
  if (/note n°/.test(t)) p.push('mentions a note')
  p.length ? bad('D1', p.join(' · '), (t.match(/Prix du traitement.{0,80}/) ?? [''])[0]) : ok('D1', (t.match(/Prix du traitement.{0,80}/) ?? [''])[0])
})

for (const s of scenarios) {
  try { await s.run() } catch (e) { bad(s.id, 'threw', String(e.message).slice(0, 300)) }
}
const errs = consoleErrors.filter((e) => !/favicon|DevTools/i.test(e))
console.log('\nconsole errors:', errs.length, errs.slice(0, 5))
console.log(findings.length ? '\nFINDINGS:\n' + findings.map((x) => JSON.stringify(x)).join('\n') : '\nALL CLEAR')
await browser.close()
