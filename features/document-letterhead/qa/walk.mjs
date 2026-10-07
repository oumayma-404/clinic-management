// The whole plan.md in one launch. Usage: node walk.mjs <scratchpad>
// Mutates only the QA clinic's letterhead (restored at the end to what it was) and one test ordonnance (deleted by C3).
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { execFileSync } from 'node:child_process'
import crypto from 'node:crypto'

const [SCRATCH] = process.argv.slice(2)
const { chromium } = createRequire(join(SCRATCH, 'package.json'))('playwright-core')
const HERE = dirname(fileURLToPath(import.meta.url))
const SHOTS = join(HERE, 'shots'); mkdirSync(SHOTS, { recursive: true })
const SRC = join(SCRATCH, 'letterhead')
const OUT = join(SRC, 'out'); mkdirSync(OUT, { recursive: true })
writeFileSync(join(SRC, 'notes.txt'), 'pas une image')

const WEB = 'http://localhost:3000'
const API = 'http://localhost:5000/api'
const ADMIN = { email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totp: '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO' }
const DOCTOR = { email: 'qa.doctor@ibnkhaldoun.test', password: 'QaAudit2026!y' }
const IDS = {
  invoice: '4974b09e-805e-4e2a-b53a-8eb52b620d82',
  payment: 'e907d704-c6e2-4433-9769-15b557120175',
  devis: 'aa752507-1912-4553-8cf7-c9ba647130d2',
  plan: 'eea01758-d65e-4443-8fe3-194f00706f2f',
  installment: '9fa29bcd-3fa9-49ad-9848-5d47363d3b32',
  instPayment: 'c58ef478-6d25-4b91-9556-ad0f2e0fd86c',
  avoir: '5604f44b-b800-4dd7-b8d0-4a11bc46eea6',
  savedDoc: 'ce3bfc60-ce27-46a4-b7c5-592f8586c4f7',
  patient: 'ceafa5f9-0218-4e20-b7b7-a7d47cef8aa6',
  clinic: 'f64a8a75-94e1-4e08-a743-fa231abe438f',
}

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why, skipped: true }) }
const settle = ms => new Promise(r => setTimeout(r, ms))

function totp(secret) {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of secret.toUpperCase()) bits += A.indexOf(c).toString(2).padStart(5, '0')
  const key = Buffer.from((bits.match(/.{8}/g) || []).map(b => parseInt(b, 2)))
  const counter = Math.floor(Date.now() / 1000 / 30)
  const buf = Buffer.alloc(8); buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0); buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac('sha1', key).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, '0')
}

const sql = q => execFileSync('docker', ['exec', 'clinic-postgres', 'psql', '-U', 'clinic_user', '-d', 'clinic_management', '-At', '-F', '|', '-c', q]).toString().trim()

let TOKEN = null
async function api(path, { method = 'GET', json, form } = {}) {
  const headers = { Authorization: `Bearer ${TOKEN}` }
  let body
  if (json !== undefined) { headers['Content-Type'] = 'application/json'; body = JSON.stringify(json) }
  if (form) body = form
  const res = await fetch(`${API}${path}`, { method, headers, body })
  const buf = Buffer.from(await res.arrayBuffer())
  const type = res.headers.get('content-type') || ''
  return { status: res.status, type, buf, json: type.includes('json') ? JSON.parse(buf.toString() || 'null') : null }
}

function inspect(name, buf) {
  const file = join(OUT, `${name}.pdf`)
  writeFileSync(file, buf)
  return JSON.parse(execFileSync('python', [join(HERE, 'pdfcheck.py'), file, join(SHOTS, name)]).toString())
}

const meds = n => JSON.stringify(Array.from({ length: n }, (_, i) => ({
  name: ['Amoxicilline', 'Paracétamol', 'Ibuprofène', 'Chlorhexidine'][i % 4] + (i >= 4 ? ` ${i}` : ''),
  dosage: '1 g', dose: '1 comprimé', timesPerDay: '2', duration: '7',
})))
const ordonnanceBody = (n, extra = {}) => ({
  documentType: 'prescription', documentDate: new Date().toISOString(),
  patientName: 'Amine Trabelsi', patientAge: '12/03/1994',
  clinicName: 'Cabinet QA', clinicAddress: '1 rue de Test, Tunis', clinicPhone: '71 000 000',
  doctorName: 'Dr. Test', doctorSpecialty: 'Chirurgien-dentiste',
  content: { medications: meds(n) }, ...extra,
})

// Every document kind the letterhead reaches, rendered through the real endpoints.
async function renderAll(tag) {
  const out = {}
  const add = async (name, req) => {
    const r = await req
    out[name] = r.status === 200 && r.type.includes('pdf') ? inspect(`${tag}-${name}`, r.buf) : { error: `${r.status} ${r.buf.toString().slice(0, 120)}` }
  }
  await add('ordonnance', api('/medical-documents/generate-pdf-download', { method: 'POST', json: ordonnanceBody(3) }))
  await add('invoice', api(`/invoices/${IDS.invoice}/pdf`))
  await add('devis', api(`/treatment-plans/${IDS.devis}/devis-pdf`))
  await add('receipt', api(`/payments/${IDS.payment}/receipt-pdf`))
  await add('plan-receipt', api(`/treatment-plans/${IDS.plan}/installments/${IDS.installment}/payments/${IDS.instPayment}/receipt-pdf`))
  await add('avoir', api(`/invoices/avoirs/${IDS.avoir}/pdf`))
  await add('saved-doc', api(`/medical-documents/${IDS.savedDoc}/pdf`))
  return out
}
const summary = r => Object.entries(r).map(([k, v]) => v.error ? `${k}:ERR(${v.error})` : `${k}:p${v.pages.length} top=${v.pages[0].topBand} bottom=${v.pages[0].bottomBand}`).join(' · ')

async function signIn(page, acct) {
  await page.goto(`${WEB}/login`, { waitUntil: 'domcontentloaded' })
  await page.fill('input[type=email]', acct.email)
  await page.fill('input[type=password]', acct.password)
  await page.click("button:has-text('Se connecter')")
  if (acct.totp) {
    const box = page.locator('input[inputmode=numeric]').first()
    await box.waitFor({ timeout: 30000 })
    await settle((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
    await box.fill(totp(acct.totp))
  }
  await page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 60000 })
}

const dialog = page => page.locator('[role="dialog"]:visible').last()
async function openCard(page) {
  await page.goto(`${WEB}/settings`, { waitUntil: 'domcontentloaded' })
  const btn = page.locator('button:has-text("En-tête des documents")').first()
  await btn.waitFor({ timeout: 90000 }); await settle(2000)
  if ((await btn.getAttribute('aria-expanded')) !== 'true') await btn.click()
  await page.locator('text=/Papier du cabinet|En-tête texte/').first().waitFor({ timeout: 30000 })
  await settle(800)
}
async function openImport(page) {
  await page.locator('button:has-text("Importer mon papier à en-tête"), button:text-is("Remplacer")').first().click()
  await dialog(page).locator('text=Importer mon papier à en-tête').first().waitFor({ timeout: 15000 })
  await settle(500)
}
async function pick(page, file) {
  await dialog(page).locator('input[type=file]').setInputFiles(join(SRC, file))
}
async function waitCut(page) {
  await dialog(page).locator('[role="status"]:has-text("En-tête")').first().waitFor({ timeout: 60000 })
  await settle(500)
}
async function readout(page) {
  const t = await dialog(page).locator('[role="status"]:has-text("En-tête")').first().innerText()
  const h = /En-tête\s*(\d+)\s*mm/.exec(t); const f = /Pied de page\s*(\d+)\s*mm/.exec(t)
  return { text: t, header: h ? +h[1] : null, footer: f ? +f[1] : null }
}
const banner = async page => (await dialog(page).locator('[role="alert"]').count())
  ? (await dialog(page).locator('[role="alert"]').first().innerText()).trim() : ''
async function waitText(page, re, ms = 20000) {
  const end = Date.now() + ms
  while (Date.now() < end) { if (await page.locator(`text=${re}`).count()) return true; await settle(250) }
  return false
}
async function pageFits(page) {
  const img = await dialog(page).locator('img[alt="Votre papier à en-tête"]').boundingBox()
  const btn = await dialog(page).locator('button:has-text("Voir l\'aperçu")').boundingBox()
  return { fits: !!img && !!btn && img.y >= 0 && img.y + img.height <= btn.y, img, btnTop: btn?.y }
}
async function clickFooter(page, label) { await dialog(page).locator(`button:has-text("${label}")`).last().click() }
async function toPreview(page) {
  const wait = page.waitForResponse(r => r.url().includes('/letterhead/preview'), { timeout: 60000 })
  await clickFooter(page, "Voir l'aperçu")
  const res = await wait
  await settle(1500)
  return res
}

function bandForm(version) {
  const form = new FormData()
  form.append('header', new Blob([readFileSync(join(SRC, 'band-header.png'))], { type: 'image/png' }), 'entete.png')
  form.append('footer', new Blob([readFileSync(join(SRC, 'band-footer.png'))], { type: 'image/png' }), 'pied.png')
  form.append('version', String(version))
  return form
}

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR', acceptDownloads: true })
await ctx.route('**/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const page = await ctx.newPage()
const pageErrors = []
page.on('pageerror', e => pageErrors.push(e.message))

await signIn(page, ADMIN)
TOKEN = await page.evaluate(async () => (await (await fetch('/bff/auth/token')).json()).accessToken)
console.log('signed in as admin; token', TOKEN ? 'ok' : 'MISSING')

// Whatever letterhead the clinic has now is somebody's — put back at the end, whatever the walk did to it.
const OWNER_KEYS = sql(`select coalesce("LetterheadHeaderStorageKey",'NULL'),coalesce("LetterheadFooterStorageKey",'NULL'),coalesce("LetterheadBodyStorageKey",'NULL') from "Clinics" where "Id"='${IDS.clinic}'`)
console.log('letterhead before the walk:', OWNER_KEYS)

// Start from a clinic with no letterhead.
{
  const lh = (await api('/clinics/letterhead')).json
  if (lh?.hasHeader) await api(`/clinics/letterhead?version=${lh.version}`, { method: 'DELETE' })
}

// ── D1 / D2 / D4 / B8 — every PDF without a letterhead ──────────────────────────────────────────────────────
try {
  const r = await renderAll('no-lh')
  const errs = Object.entries(r).filter(([, v]) => v.error)
  const banded = Object.entries(r).filter(([, v]) => !v.error && (v.pages[0].topBand || v.pages[0].bottomBand))
  if (!errs.length && !banded.length) ok('D1', `7 PDFs, no band anywhere · ${summary(r)}`)
  else bad('D1', 'PDFs without a letterhead', JSON.stringify({ errs, banded: banded.map(([k]) => k) }))
  if (!r['saved-doc'].error) ok('D2', `saved ordonnance re-renders: ${r['saved-doc'].pages[0].head.slice(0, 90)}`)
  else bad('D2', 'saved document', r['saved-doc'].error)
  const dob = Object.entries(r).filter(([, v]) => !v.error && v.naissance).map(([k]) => k)
  if (!dob.length) ok('D4', 'no PDF contains « naissance »')
  else bad('D4', '« naissance » printed on', dob.join(', '))
} catch (e) { bad('D1', 'threw', e.message) }

try {
  const forged = await api('/medical-documents/generate-pdf-download', {
    method: 'POST', json: ordonnanceBody(3, { letterheadHeaderKey: `clinics/${IDS.clinic}/letterhead/forged/header` }),
  })
  const c = inspect('B8-forged', forged.buf)
  if (forged.status === 200 && !c.pages[0].topBand) ok('B8', 'a forged letterhead key in the body is ignored')
  else bad('B8', 'forged key', JSON.stringify({ status: forged.status, top: c.pages?.[0]?.topBand }))
} catch (e) { bad('B8', 'threw', e.message) }

// ── A1 — the card ───────────────────────────────────────────────────────────────────────────────────────────
try {
  await openCard(page)
  await page.screenshot({ path: join(SHOTS, 'A1.png') })
  const texte = await page.locator('text=En-tête texte').count()
  const btn = await page.locator('button:has-text("Importer mon papier à en-tête")').count()
  if (texte && btn) ok('A1', 'badge « En-tête texte » + « Importer mon papier à en-tête »')
  else bad('A1', 'card', JSON.stringify({ texte, btn }))
} catch (e) { bad('A1', 'threw', e.message) }

// ── B3 / B1 / A2 / B4 / B2 / A3 / A4 — the import dialog ─────────────────────────────────────────────────────
let suggested = null
try {
  await openImport(page)
  await pick(page, 'notes.txt'); await settle(1500)
  const msg = await banner(page)
  if (/ni un PDF ni une image/.test(msg)) ok('B3', `refused: « ${msg.slice(0, 70)}… »`)
  else bad('B3', 'unsupported file', msg)
} catch (e) { bad('B3', 'threw', e.message) }

try {
  await pick(page, 'entete-photo.jpg'); await waitCut(page)
  const warn = dialog(page).locator('[role="status"]:has-text("faible résolution")')
  const warning = (await warn.count()) ? (await warn.first().innerText()).replace(/\s+/g, ' ') : ''
  const res = await toPreview(page)
  await page.screenshot({ path: join(SHOTS, 'B1.png') })
  if (/900 px/.test(warning) && res.status() === 200) ok('B1', `phone photo enlarged, warned and previewed: « ${warning.slice(0, 70)}… »`)
  else bad('B1', 'phone photo', JSON.stringify({ warning, status: res.status(), banner: await banner(page) }))
  await clickFooter(page, 'Ajuster les lignes'); await settle(500)
  await clickFooter(page, 'Changer de fichier'); await settle(500)
} catch (e) { bad('B1', 'threw', e.message) }

try {
  await pick(page, 'entete-minuscule.png'); await settle(1500)
  const msg = await banner(page)
  if (/illisible/.test(msg)) ok('B1b', `300 px image refused: « ${msg.slice(0, 70)}… »`)
  else bad('B1b', 'tiny image', msg)
} catch (e) { bad('B1b', 'threw', e.message) }

try {
  await pick(page, 'entete-imprimeur.pdf'); await waitCut(page)
  suggested = await readout(page)
  await page.screenshot({ path: join(SHOTS, 'A2.png') })
  const sw = await dialog(page).locator('[role="switch"]').getAttribute('aria-checked')
  if (suggested.header >= 25 && suggested.header <= 60 && sw === 'true' && suggested.footer >= 8 && suggested.footer <= 30)
    ok('A2', `suggested « ${suggested.text} »`)
  else bad('A2', 'suggested lines', JSON.stringify({ ...suggested, sw }))
  if (suggested.header >= 45) ok('A2b', `the header line (${suggested.header} mm) falls below the letterhead's rule at 44.5 mm`)
  else bad('A2b', 'the suggested header line cuts above the rule at 44.5 mm', `${suggested.header} mm`)
  const fit = await pageFits(page)
  if (fit.fits) ok('A2c', `1440x900: the whole page fits above the buttons (page ends ${Math.round(fit.img.y + fit.img.height)}, buttons at ${Math.round(fit.btnTop)})`)
  else bad('A2c', '1440x900: the page runs below the dialog fold', JSON.stringify(fit))
} catch (e) { bad('A2', 'threw', e.message) }

try {
  const line = dialog(page).locator('[role="slider"][aria-label="Fin de l\'en-tête"]')
  await line.focus()
  for (let i = 0; i < 4; i++) await page.keyboard.press('ArrowDown')
  await settle(300)
  const after = await readout(page)
  if (after.header > suggested.header) ok('B4', `keyboard moved the line: ${suggested.header} → ${after.header} mm`)
  else bad('B4', 'keyboard', JSON.stringify({ before: suggested.header, after: after.header }))
  for (let i = 0; i < 20; i++) await page.keyboard.press('Shift+ArrowDown')
  await settle(300)
  const tall = await readout(page)
  const res = await toPreview(page)
  const msg = await banner(page)
  if (res.status() === 400 && /plus d'un tiers/.test(msg)) ok('B2', `header at ${tall.header} mm refused: « ${msg.slice(0, 60)}… »`)
  else bad('B2', 'tall header', JSON.stringify({ header: tall.header, status: res.status(), msg }))
} catch (e) { bad('B2', 'threw', e.message) }

try {
  await clickFooter(page, 'Changer de fichier'); await settle(500)
  await pick(page, 'entete-imprimeur.pdf'); await waitCut(page)
  const res = await toPreview(page)
  const frame = await dialog(page).locator('iframe').count()
  await page.screenshot({ path: join(SHOTS, 'A3.png') })
  if (res.status() === 200 && /pdf/.test(res.headers()['content-type'] || '') && frame) ok('A3', 'aperçu: 200 application/pdf, frame shown')
  else bad('A3', 'aperçu', JSON.stringify({ status: res.status(), type: res.headers()['content-type'], frame }))
  const preview = await api('/clinics/letterhead/preview', { method: 'POST', form: bandForm(0) })
  const c = inspect('A3-preview', preview.buf)
  if (c.pages[0].topBand && c.pages[0].bottomBand) ok('A3', `preview PDF carries both bands (${c.pages.length} page)`)
  else bad('A3', 'preview PDF bands', JSON.stringify(c.pages[0]))
} catch (e) { bad('A3', 'threw', e.message) }

try {
  await clickFooter(page, 'Enregistrer')
  const toast = await waitText(page, '/En-tête enregistré/')
  await settle(2500)
  const closed = await page.locator('[role="dialog"]:visible').count() === 0
  const paper = await page.locator('text=Papier du cabinet').count()
  const mini = await page.$$eval('img[src^="blob:"]', imgs => imgs.map(i => ({ w: i.naturalWidth, ok: i.complete })))
  await page.screenshot({ path: join(SHOTS, 'A4.png') })
  const expanded = await page.locator('button:has-text("En-tête des documents")').first().getAttribute('aria-expanded')
  if (toast && closed && paper && expanded === 'true' && mini.some(m => m.ok && m.w > 0)) ok('A4', `saved; badge « Papier du cabinet »; mini page images ${JSON.stringify(mini)}`)
  else bad('A4', 'save — the result is not shown', JSON.stringify({ toast, closed, paper, expanded, mini }))
  if (expanded !== 'true') {
    await openCard(page); await settle(2000)
    const again = await page.$$eval('img[src^="blob:"]', imgs => imgs.map(i => ({ w: i.naturalWidth, ok: i.complete })))
    console.log('     after reopening the card, mini page images:', JSON.stringify(again))
  }
} catch (e) { bad('A4', 'threw', e.message) }

let paperA = null
try {
  const row = sql(`select "LetterheadHeaderStorageKey","LetterheadFooterStorageKey" from "Clinics" where "Id"='${IDS.clinic}'`)
  const [h, f] = row.split('|')
  paperA = h
  const g = /letterhead\/([0-9a-f]{32})\/header$/.exec(h || '')
  if (g && f === h.replace('/header', '/footer') && h.startsWith(`clinics/${IDS.clinic}/`)) ok('A5', `keys ${h} + …/footer`)
  else bad('A5', 'stored keys', row)
} catch (e) { bad('A5', 'threw', e.message) }

// ── A6 / A7 — the PDFs on the cabinet's paper ───────────────────────────────────────────────────────────────
try {
  const r = await renderAll('paper')
  const wrong = Object.entries(r).filter(([k, v]) => k !== 'saved-doc' && (v.error || !v.pages[0].topBand || !v.pages[0].bottomBand))
  const saved = r['saved-doc']
  if (!saved.error && !saved.pages[0].topBand) ok('D2', 'an ordonnance issued before the letterhead keeps its text header')
  else bad('D2', 'pre-letterhead document picked up the new paper', JSON.stringify(saved.error ?? saved.pages[0]))
  if (!wrong.length) ok('A6', `6 PDFs carry both bands · ${summary(r)}`)
  else bad('A6', 'PDFs on paper', JSON.stringify(wrong.map(([k, v]) => [k, v.error ?? v.pages[0]])))
  const dob = Object.entries(r).filter(([, v]) => !v.error && v.naissance).map(([k]) => k)
  if (dob.length) bad('D4', '« naissance » printed on paper PDFs', dob.join(', '))
} catch (e) { bad('A6', 'threw', e.message) }

try {
  const long = await api('/medical-documents/generate-pdf-download', { method: 'POST', json: ordonnanceBody(26) })
  const c = inspect('A7-long', long.buf)
  if (c.pages.length >= 2 && c.pages.every(p => p.topBand && p.bottomBand)) ok('A7', `${c.pages.length} pages, both bands on every page`)
  else bad('A7', 'long ordonnance', JSON.stringify(c.pages.map(p => ({ top: p.topBand, bottom: p.bottomBand }))))
} catch (e) { bad('A7', 'threw', e.message) }

// ── A8 / D4 / A9 — the editor ───────────────────────────────────────────────────────────────────────────────
try {
  await page.goto(`${WEB}/documents/prescription?patientId=${IDS.patient}`, { waitUntil: 'domcontentloaded' })
  await page.locator('button:has-text("Télécharger PDF")').first().waitFor({ timeout: 90000 })
  await settle(4000)
  const head = await page.$$eval('.light img[src^="blob:"]', imgs => imgs.map(i => ({ w: i.naturalWidth, top: i.getBoundingClientRect().top, cardTop: i.parentElement.getBoundingClientRect().top })))
  const cardTop = head[0]?.cardTop ?? -1
  const textBlock = await page.locator('.light h1').count()
  const dobText = await page.locator('.light >> text=Date de naissance').count()
  await page.screenshot({ path: join(SHOTS, 'A8.png') })
  if (head.length >= 1 && head[0].w > 0 && Math.abs(head[0].top - cardTop) < 4 && textBlock === 0) ok('A8', `preview starts with the band (${head.length} band images), no text header`)
  else bad('A8', 'editor preview', JSON.stringify({ head, cardTop, textBlock }))
  if (dobText === 0) ok('D4', 'editor preview: no « Date de naissance »')
  else bad('D4', 'editor preview shows « Date de naissance »')
} catch (e) { bad('A8', 'threw', e.message) }

// A9 — « Télécharger Word » is withdrawn app-wide by the owner (`WORD_EXPORT_OFFERED = false`, commit 85dfd995).
// The row asserts that state; the docx branch below only runs if the button ever comes back.
try {
  const word = page.locator('button:has-text("Télécharger Word"):visible').first()
  if (!(await word.count())) skip('A9', 'Word export withdrawn app-wide by the owner (WORD_EXPORT_OFFERED = false) — no button to press; the letterhead docx path compiles but is unreachable')
  else {
    const dl = page.waitForEvent('download', { timeout: 60000 })
    await word.click()
    const file = join(OUT, 'A9.docx'); await (await dl).saveAs(file)
    const info = execFileSync('python', ['-c', `
import zipfile,json,sys
z=zipfile.ZipFile(sys.argv[1]); n=z.namelist()
h=[x for x in n if x.startswith('word/header')]; f=[x for x in n if x.startswith('word/footer')]
d=lambda xs:any(b'drawing' in z.read(x) for x in xs)
print(json.dumps({'header':d(h),'footer':d(f),'media':[x for x in n if x.startswith('word/media')]}))`, file]).toString()
    const w = JSON.parse(info)
    if (w.header && w.footer && w.media.length >= 2) ok('A9', `docx: header + footer drawings, media ${w.media.join(', ')}`)
    else bad('A9', 'docx', info)
  }
} catch (e) { bad('A9', 'threw', e.message) }

// ── C3 (part 1) — a document issued on paper A ──────────────────────────────────────────────────────────────
let snapDoc = null
try {
  const created = await api('/medical-documents', {
    method: 'POST', json: {
      patientId: IDS.patient, documentType: 'prescription', documentDate: new Date().toISOString(),
      contentJson: JSON.stringify({ medications: JSON.parse(meds(2)) }),
      clinicName: 'Cabinet QA', clinicAddress: '1 rue de Test', clinicPhone: '71 000 000',
      doctorName: 'Dr. Test', doctorSpecialty: 'Chirurgien-dentiste',
    },
  })
  snapDoc = created.json?.id ?? null
  if (!snapDoc) bad('C3', 'could not create the test ordonnance', `${created.status} ${created.buf.toString().slice(0, 200)}`)
} catch (e) { bad('C3', 'threw', e.message) }

// ── C4 / B5 — replace with the scan, footer off, against a stale version ───────────────────────────────────
try {
  await openCard(page)
  await openImport(page)
  await pick(page, 'entete-scan-300dpi.jpg'); await waitCut(page)
  await dialog(page).locator('[role="switch"]').click(); await settle(300)
  const noFooter = await readout(page)
  await toPreview(page)
  // Somebody else's save moves the clinic row's version under the open dialog.
  const lh = (await api('/clinics/letterhead')).json
  const peer = await api('/clinics/letterhead', { method: 'PUT', form: bandForm(lh.version) })
  await settle(2500)
  const stillOpen = await dialog(page).locator('button:has-text("Enregistrer")').count()
  if (!stillOpen) bad('C4', "the open dialog was reset by a colleague's save (back to « Fichier »)")
  await clickFooter(page, 'Enregistrer'); await settle(2500)
  const msg = await banner(page)
  const reload = dialog(page).locator('button:has-text("Recharger")')
  if (peer.status === 200 && (await reload.count())) {
    await reload.click(); await settle(1500)
    await clickFooter(page, 'Enregistrer')
    const toast = await waitText(page, '/En-tête enregistré/')
    if (toast) ok('C4', `409 offered « Recharger » (« ${msg.slice(0, 60)}… »), then the save went through`)
    else bad('C4', 'save after Recharger', await banner(page))
  } else bad('C4', 'stale version', JSON.stringify({ peer: peer.status, msg }))
} catch (e) { bad('C4', 'threw', e.message) }


// ── B5 — the scan, footer off, on its own (no concurrent writer) ──────────────────────────────────────────
try {
  await page.keyboard.press('Escape'); await settle(800)
  await openCard(page)
  await openImport(page)
  await pick(page, 'entete-scan-300dpi.jpg'); await waitCut(page)
  await dialog(page).locator('[role="switch"]').click(); await settle(300)
  const noFooter = await readout(page)
  await toPreview(page)
  await clickFooter(page, 'Enregistrer')
  await waitText(page, '/En-tête enregistré/')
  await settle(2500)
  const row = sql(`select "LetterheadHeaderStorageKey",coalesce("LetterheadFooterStorageKey",'NULL') from "Clinics" where "Id"='${IDS.clinic}'`)
  const r = await api('/medical-documents/generate-pdf-download', { method: 'POST', json: ordonnanceBody(3) })
  const c = inspect('B5-nofooter', r.buf)
  if (noFooter.footer === null && row.endsWith('|NULL') && c.pages[0].topBand && !c.pages[0].bottomBand) ok('B5', 'footer off: saved without footer; PDF has the header band only')
  else bad('B5', 'footer off', JSON.stringify({ readout: noFooter.text, row, top: c.pages[0].topBand, bottom: c.pages[0].bottomBand }))
} catch (e) { bad('B5', 'threw', e.message) }

// ── C5 — « Page entière »: a framed paper keeps its frame on every page ───────────────────────────────────────
try {
  await openCard(page)
  await openImport(page)
  await pick(page, 'entete-cadre.png'); await waitCut(page)
  const fullPage = await dialog(page).locator('[role="radio"]:has-text("Page entière")').getAttribute('aria-checked')
  await page.screenshot({ path: join(SHOTS, 'C5-cut.png') })
  if (fullPage === 'true') ok('C5', 'framed paper opens on « Page entière »')
  else bad('C5', 'framed paper did not preselect « Page entière »', fullPage)
  const fit = await pageFits(page)
  if (fit.fits) ok('C5b', `1440x900 with the warning and the choice: the page fits (ends ${Math.round(fit.img.y + fit.img.height)}, buttons at ${Math.round(fit.btnTop)})`)
  else bad('C5b', 'page runs below the buttons', JSON.stringify(fit))
  const res = await toPreview(page)
  await clickFooter(page, 'Enregistrer')
  await waitText(page, '/En-tête enregistré/')
  await settle(2500)
  const body = sql(`select coalesce("LetterheadBodyStorageKey",'NULL') from "Clinics" where "Id"='${IDS.clinic}'`)
  const long = await api('/medical-documents/generate-pdf-download', { method: 'POST', json: ordonnanceBody(26) })
  const c = inspect('C5-page', long.buf)
  const bodyOn = pg => pg.images.some(i => i.x0 <= 1 && i.x1 >= pg.width - 1 && i.y0 > 1 && i.y1 < pg.height - 1 && i.y1 - i.y0 > pg.height * 0.3)
  const every = c.pages.every(pg => pg.topBand && pg.bottomBand && bodyOn(pg))
  if (res.status() === 200 && body.endsWith('/body') && c.pages.length >= 2 && every)
    ok('C5c', `saved with a body band; ${c.pages.length}-page ordonnance carries header + frame + footer on every page`)
  else bad('C5c', 'page entière', JSON.stringify({ status: res.status(), body, pages: c.pages.map(pg => ({ top: pg.topBand, bottom: pg.bottomBand, body: bodyOn(pg) })) }))
  const inv = await api(`/invoices/${IDS.invoice}/pdf`)
  const ci = inspect('C5-invoice', inv.buf)
  if (ci.pages.every(pg => pg.topBand && bodyOn(pg))) ok('C5d', 'the note d\'honoraires carries the frame too')
  else bad('C5d', 'invoice', JSON.stringify(ci.pages.map(pg => pg.images)))
} catch (e) { bad('C5', 'threw', e.message) }

// ── C3 (part 2) — the document kept paper A ─────────────────────────────────────────────────────────────────
try {
  if (snapDoc) {
    const json = sql(`select "ContentJson" from "MedicalDocuments" where "Id"='${snapDoc}'`)
    const now = sql(`select "LetterheadHeaderStorageKey" from "Clinics" where "Id"='${IDS.clinic}'`)
    const kept = JSON.parse(json).letterheadHeaderKey
    const pdf = await api(`/medical-documents/${snapDoc}/pdf`)
    const c = pdf.status === 200 ? inspect('C3-snapshot', pdf.buf) : null
    if (kept === paperA && kept !== now && c?.pages[0].topBand) ok('C3', 'the document kept the paper it was issued on and still renders it')
    else bad('C3', 'snapshot', JSON.stringify({ kept, paperA, now, status: pdf.status, top: c?.pages[0].topBand }))
    const del = await api(`/medical-documents/${snapDoc}`, { method: 'DELETE' })
    console.log(`     (test ordonnance ${snapDoc} deleted: ${del.status})`)
  }
} catch (e) { bad('C3', 'threw', e.message) }

skip('D3', 'the QA clinic\'s name is blank, so « Informations du cabinet » refuses to save for a reason unrelated to this change')

// ── C1 — 320 px ─────────────────────────────────────────────────────────────────────────────────────────────
try {
  await page.setViewportSize({ width: 320, height: 640 })
  await openCard(page)
  const cardOverflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
  await page.screenshot({ path: join(SHOTS, 'C1-card.png'), fullPage: false })
  await openImport(page)
  await pick(page, 'entete-imprimeur.pdf'); await waitCut(page)
  const over = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
  const btns = await dialog(page).locator('button:has-text("Voir l\'aperçu"), button:has-text("Changer de fichier")').evaluateAll(
    els => els.map(e => { const r = e.getBoundingClientRect(); return { l: Math.round(r.left), r: Math.round(r.right), b: Math.round(r.bottom) } }))
  await page.screenshot({ path: join(SHOTS, 'C1-cut.png') })
  const pills = await dialog(page).locator('ol[aria-label="Étapes"] li > span:last-child').evaluateAll(els => els.map(e => Math.round(e.getBoundingClientRect().height)))
  const nowrap = await dialog(page).locator('h2 span.whitespace-nowrap:has-text("en-tête")').count()
  if (pills.length === 3 && pills.every(h => h <= 26) && nowrap) ok('C1b', `320 px: steps on one line each (${pills.join('/')} px), « en-tête » kept whole`)
  else bad('C1b', '320 px title / steps wrap', JSON.stringify({ pills, nowrap }))
  const fit320 = await pageFits(page)
  if (fit320.fits) ok('C1c', '320 px: the whole page fits above the buttons')
  else bad('C1c', '320 px: page runs below the buttons', JSON.stringify(fit320))
  if (cardOverflow <= 0 && over <= 0 && btns.length === 2 && btns.every(b => b.l >= 0 && b.r <= 320 && b.b <= 640))
    ok('C1', `320 px: no horizontal scroll; buttons ${JSON.stringify(btns)}`)
  else bad('C1', '320 px', JSON.stringify({ cardOverflow, over, btns }))
  await page.keyboard.press('Escape'); await settle(800)
} catch (e) { bad('C1', 'threw', e.message) }

// ── C2 — the owner's laptop, 1536 × 730 ─────────────────────────────────────────────────────────────────────
try {
  await page.setViewportSize({ width: 1536, height: 730 })
  await openCard(page)
  await openImport(page)
  await pick(page, 'entete-imprimeur.pdf'); await waitCut(page)
  const cut = await dialog(page).locator('button:has-text("Voir l\'aperçu")').boundingBox()
  await page.screenshot({ path: join(SHOTS, 'C2-cut.png') })
  const fit730 = await pageFits(page)
  if (fit730.fits) ok('C2b', `1536x730: the whole page fits (ends ${Math.round(fit730.img.y + fit730.img.height)}, buttons at ${Math.round(fit730.btnTop)})`)
  else bad('C2b', '1536x730: page runs below the buttons', JSON.stringify(fit730))
  await toPreview(page)
  const save = await dialog(page).locator('button:has-text("Enregistrer")').boundingBox()
  await page.screenshot({ path: join(SHOTS, 'C2-preview.png') })
  if (cut && save && cut.y + cut.height <= 730 && save.y + save.height <= 730) ok('C2', `footer buttons inside 730 px (cut ${Math.round(cut.y + cut.height)}, preview ${Math.round(save.y + save.height)})`)
  else bad('C2', '1536×730', JSON.stringify({ cut, save }))
  await page.keyboard.press('Escape'); await settle(800)
} catch (e) { bad('C2', 'threw', e.message) }

// ── B6 — withdraw ───────────────────────────────────────────────────────────────────────────────────────────
try {
  await page.setViewportSize({ width: 1440, height: 900 })
  await openCard(page)
  await page.locator('button:text-is("Retirer")').first().click()
  const alert = page.locator('[role="alertdialog"]')
  await alert.waitFor({ timeout: 10000 })
  const title = await alert.locator('h2').first().innerText()
  await page.screenshot({ path: join(SHOTS, 'B6-confirm.png') })
  await alert.locator('button:has-text("Retirer l\'en-tête")').click()
  const toast = await waitText(page, '/En-tête retiré/')
  await settle(1500)
  const texte = await page.locator('text=En-tête texte').count()
  const row = sql(`select coalesce("LetterheadHeaderStorageKey",'NULL'),coalesce("LetterheadFooterStorageKey",'NULL'),coalesce("LetterheadBodyStorageKey",'NULL') from "Clinics" where "Id"='${IDS.clinic}'`)
  if (/Retirer le papier à en-tête/.test(title) && toast && texte && row === 'NULL|NULL|NULL') ok('B6', `withdrawn: « ${title} » → badge « En-tête texte », keys null`)
  else bad('B6', 'withdraw', JSON.stringify({ title, toast, texte, row }))
} catch (e) { bad('B6', 'threw', e.message) }

// ── B7 — a doctor sees the card without its controls ───────────────────────────────────────────────────────
try {
  const dctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
  await dctx.route('**/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
  const dpage = await dctx.newPage()
  await signIn(dpage, DOCTOR)
  await openCard(dpage)
  const controls = await dpage.locator('button:has-text("Importer mon papier à en-tête"), button:text-is("Retirer"), button:text-is("Remplacer")').count()
  const note = await dpage.locator('text=Modifiable par un administrateur.').count()
  await dpage.screenshot({ path: join(SHOTS, 'B7.png') })
  if (controls === 0 && note) ok('B7', 'doctor: no import/remove controls, « Modifiable par un administrateur. »')
  else bad('B7', 'doctor view', JSON.stringify({ controls, note }))
  await dctx.close()
} catch (e) { bad('B7', 'threw', e.message) }

if (pageErrors.length) console.log('page errors:', pageErrors.slice(0, 5))
await browser.close()

// Put back the letterhead the clinic had before the walk (our own artefact being reverted, so SQL is right here).
{
  const [h, f, b] = OWNER_KEYS.split('|').map(v => (v === 'NULL' ? 'NULL' : `'${v}'`))
  sql(`update "Clinics" set "LetterheadHeaderStorageKey"=${h},"LetterheadFooterStorageKey"=${f},"LetterheadBodyStorageKey"=${b} where "Id"='${IDS.clinic}'`)
  console.log('letterhead restored to:', sql(`select coalesce("LetterheadHeaderStorageKey",'NULL'),coalesce("LetterheadFooterStorageKey",'NULL'),coalesce("LetterheadBodyStorageKey",'NULL') from "Clinics" where "Id"='${IDS.clinic}'`))
}
console.log('\n── findings ──')
findings.length ? findings.forEach(f => console.log(JSON.stringify(f))) : console.log('ALL CLEAR')
