// The gap pass (plan.md § Tier E): the rows run 5 left untested. One launch. Usage: node gaps.mjs <scratchpad>
// Mutates only the QA clinic's letterhead — restored at the end to exactly what it was.
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
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
const WEB = 'http://localhost:3000'
const API = 'http://localhost:5000/api'
const ADMIN = { email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totp: '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO' }
const CLINIC = 'f64a8a75-94e1-4e08-a743-fa231abe438f'
const PATIENT = 'ceafa5f9-0218-4e20-b7b7-a7d47cef8aa6'

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why, skipped: true }) }
const settle = ms => new Promise(r => setTimeout(r, ms))
const sql = q => execFileSync('docker', ['exec', 'clinic-postgres', 'psql', '-U', 'clinic_user', '-d', 'clinic_management', '-At', '-F', '|', '-c', q]).toString().trim()
const keysSql = `select coalesce("LetterheadHeaderStorageKey",'NULL'),coalesce("LetterheadFooterStorageKey",'NULL'),coalesce("LetterheadBodyStorageKey",'NULL') from "Clinics" where "Id"='${CLINIC}'`

function totp(secret) {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'; let bits = ''
  for (const c of secret) bits += A.indexOf(c).toString(2).padStart(5, '0')
  const key = Buffer.from((bits.match(/.{8}/g) || []).map(b => parseInt(b, 2)))
  const counter = Math.floor(Date.now() / 1000 / 30); const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0); buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac('sha1', key).update(buf).digest(); const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, '0')
}
const freshWindow = () => settle((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)

let TOKEN = null
async function api(path, { method = 'GET', json, headers = {} } = {}) {
  const h = { Authorization: `Bearer ${TOKEN}`, ...headers }
  let body
  if (json !== undefined) { h['Content-Type'] = 'application/json'; body = JSON.stringify(json) }
  const res = await fetch(`${API}${path}`, { method, headers: h, body })
  const buf = Buffer.from(await res.arrayBuffer())
  const type = res.headers.get('content-type') || ''
  return { status: res.status, type, buf, json: type.includes('json') ? JSON.parse(buf.toString() || 'null') : null }
}
const pdfImage = (pdf, png) => {
  const file = join(OUT, png.replace(/\.png$/, '.pdf')); writeFileSync(file, pdf)
  return JSON.parse(execFileSync('python', ['-c', `
import fitz, json, sys
d = fitz.open(sys.argv[1]); p = d[0]
pix = p.get_pixmap(dpi=60); pix.save(sys.argv[2])
print(json.dumps({"pages": len(d), "images": len(p.get_image_info()), "corner": list(pix.pixel(6, 6))}))`, file, join(SHOTS, png)]).toString())
}

const dialog = page => page.locator('[role="dialog"]:visible').last()
async function openCard(page) {
  await page.goto(`${WEB}/settings`, { waitUntil: 'domcontentloaded' })
  const btn = page.locator('button[aria-expanded]:has-text("En-tête des documents")').first()
  await btn.waitFor({ timeout: 90000 }); await settle(1500)
  if ((await btn.getAttribute('aria-expanded')) !== 'true') await btn.click()
  await page.locator('text=/Papier du cabinet|En-tête texte/').first().waitFor({ timeout: 30000 })
  await settle(800)
}
async function openImport(page, file) {
  await page.locator('button:has-text("Importer mon papier à en-tête"), button:text-is("Remplacer")').first().click()
  await dialog(page).locator('input[type=file]').waitFor({ state: 'attached', timeout: 15000 })
  await dialog(page).locator('input[type=file]').setInputFiles(join(SRC, file))
  await dialog(page).locator('img[alt="Votre papier à en-tête"]').waitFor({ timeout: 60000 })
  await settle(600)
}
const readout = async page => {
  const t = (await dialog(page).locator('[role="status"]:has-text("En-tête")').first().innerText()).replace(/\s+/g, ' ')
  const h = /En-tête (\d+) mm/.exec(t); const f = /Pied de page (\d+) mm/.exec(t)
  return { text: t, header: h ? +h[1] : null, footer: f ? +f[1] : null }
}
const mode = async page => ((await dialog(page).locator('[role="radio"]:has-text("Page entière")').getAttribute('aria-checked')) === 'true' ? 'page' : 'bands')
async function previewPdf(page) {
  const wait = page.waitForResponse(r => r.url().includes('/letterhead/preview'), { timeout: 60000 })
  await dialog(page).locator(`button:has-text("Voir l'aperçu")`).click()
  const res = await wait
  if (res.status() !== 200) return { status: res.status(), pdf: null }
  await dialog(page).locator('button:has-text("Enregistrer")').waitFor({ timeout: 30000 })
  const b64 = await page.evaluate(async () => {
    const el = document.querySelector('[role="dialog"] iframe, [role="dialog"] object, [role="dialog"] embed')
    const src = (el?.getAttribute('src') || el?.getAttribute('data') || '').split('#')[0]
    const buf = new Uint8Array(await (await fetch(src)).arrayBuffer())
    let s = ''; for (const c of buf) s += String.fromCharCode(c); return btoa(s)
  })
  return { status: 200, pdf: Buffer.from(b64, 'base64') }
}
async function save(page) {
  await dialog(page).locator('button:has-text("Enregistrer")').click()
  const end = Date.now() + 20000
  while (Date.now() < end) {
    if (await page.locator('text=En-tête enregistré').count()) return { saved: true }
    const alert = dialog(page).locator('[role="alert"]')
    if (await alert.count()) return { saved: false, alert: (await alert.first().innerText()).trim() }
    await settle(250)
  }
  return { saved: false, alert: 'timeout' }
}

const OWNER_KEYS = sql(keysSql)
console.log('letterhead before the pass:', OWNER_KEYS)

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1536, height: 730 }, locale: 'fr-FR' })
await ctx.route('**/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const page = await ctx.newPage()
const pageErrors = []; page.on('pageerror', e => pageErrors.push(e.message))
await page.goto(`${WEB}/login`); await page.fill('input[type=email]', ADMIN.email)
await page.fill('input[type=password]', ADMIN.password); await page.click("button:has-text('Se connecter')")
const box = page.locator('input[inputmode=numeric]').first(); await box.waitFor({ timeout: 30000 })
await freshWindow(); await box.fill(totp(ADMIN.totp))
await page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 60000 })
TOKEN = await page.evaluate(async () => (await (await fetch('/bff/auth/token')).json()).accessToken)

// ── E1 — a silent write moved the clinic's version while the card was open: the save still goes through ──────
try {
  await openCard(page)
  sql(`update "Clinics" set "UpdatedAt"=now() where "Id"='${CLINIC}'`)
  await openImport(page, 'entete-imprimeur.pdf')
  const p = await previewPdf(page)
  const r = await save(page)
  if (p.status === 200 && r.saved) ok('E1', 'a version moved behind the card: the dialog re-read it on opening, saved with no 409')
  else bad('E1', 'stale card version', JSON.stringify({ preview: p.status, ...r }))
  await settle(2000)
} catch (e) { bad('E1', 'threw', e.message.split('\n')[0]) }

// ── E2 — « Page entière » on the settings card and in the document editor ────────────────────────────────────
try {
  await openCard(page)
  await openImport(page, 'entete-cadre.png')
  const m = await mode(page)
  await previewPdf(page)
  const r = await save(page)
  await settle(2500)
  const body = sql(`select coalesce("LetterheadBodyStorageKey",'NULL') from "Clinics" where "Id"='${CLINIC}'`)
  const card = page.locator('button[aria-expanded]:has-text("En-tête des documents")').first()
  if ((await card.getAttribute('aria-expanded')) !== 'true') await card.click()
  await settle(1500)
  const mini = await page.evaluate(() => [...document.querySelectorAll('[aria-hidden="true"] div[style*="background-image"]')]
    .map(d => d.style.backgroundImage.slice(0, 12)))
  await page.locator('text=Papier du cabinet').first().scrollIntoViewIfNeeded()
  await page.screenshot({ path: join(SHOTS, 'E2-card.png') })
  if (m === 'page' && r.saved && body.endsWith('/body') && mini.some(s => s.includes('blob:')))
    ok('E2', 'saved on « Page entière »; the settings card draws the frame strip between the bands')
  else bad('E2', 'card in page mode', JSON.stringify({ m, r, body, mini }))
} catch (e) { bad('E2', 'threw', e.message.split('\n')[0]) }

try {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(`${WEB}/documents/prescription?patientId=${PATIENT}`, { waitUntil: 'domcontentloaded' })
  await page.locator('button:has-text("Télécharger PDF")').first().waitFor({ timeout: 90000 })
  // The bands load after the page: wait for one, then read the A4 card that holds it (not the first `.light`).
  await page.locator('.light img[src^="blob:"]').first().waitFor({ timeout: 30000 })
  await settle(1500)
  const preview = await page.evaluate(() => {
    const card = document.querySelector('.light img[src^="blob:"]')?.parentElement
    const imgs = card ? [...card.querySelectorAll(':scope > img')].map(i => i.naturalWidth) : []
    const middle = card ? [...card.children].find(d => d.tagName === 'DIV' && d.style.backgroundImage) : null
    card?.scrollIntoView({ block: 'start' })
    return { imgs, middle: middle?.style.backgroundImage.slice(0, 12) ?? null }
  })
  await settle(600)
  await page.screenshot({ path: join(SHOTS, 'E2-editor.png') })
  if (preview.imgs.length === 2 && preview.middle?.includes('blob:')) ok('E2b', 'the editor\'s A4 preview: header + frame strip behind the text + footer')
  else bad('E2b', 'editor preview in page mode', JSON.stringify(preview))
} catch (e) { bad('E2b', 'threw', e.message.split('\n')[0]) }

// ── E3 — tablet: 820 × 1180, coarse pointer, a finger dragging the line ───────────────────────────────────────
try {
  await page.setViewportSize({ width: 820, height: 1180 })
  const cdp = await ctx.newCDPSession(page)
  await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 })
  await cdp.send('Emulation.setEmulatedMedia', { features: [{ name: 'pointer', value: 'coarse' }, { name: 'hover', value: 'none' }] })
  await openCard(page)
  const cardOverflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
  await page.screenshot({ path: join(SHOTS, 'E3-card-820.png') })
  await openImport(page, 'entete-imprimeur.pdf')
  const before = await readout(page)
  const img = await dialog(page).locator('img[alt="Votre papier à en-tête"]').boundingBox()
  const btn = await dialog(page).locator(`button:has-text("Voir l'aperçu")`).boundingBox()
  if (img && btn && img.y + img.height <= btn.y) ok('E3d', `820 × 1180: the whole page fits above the buttons (ends ${Math.round(img.y + img.height)}, buttons ${Math.round(btn.y)})`)
  else bad('E3d', '820 × 1180: page runs under the buttons', JSON.stringify({ img, btn }))
  const line = dialog(page).locator('[role="slider"][aria-label="Fin de l\'en-tête"]')
  const lb = await line.boundingBox()
  const x = Math.round(lb.x + lb.width / 3), y0 = Math.round(lb.y + lb.height / 2)
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y: y0 }] })
  for (let i = 1; i <= 10; i++) { await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x, y: y0 + i * 6 }] }); await settle(30) }
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
  await settle(500)
  const after = await readout(page)
  const targets = await dialog(page).locator('[role="slider"], [role="radio"], button:has-text("Voir l\'aperçu"), button:has-text("Changer de fichier")')
    // A Button paints 36 px and carries `.touch-target`, a 44 px hit area drawn by its ::after — measure that.
    .evaluateAll(els => els.map(e => Math.round(Math.max(e.getBoundingClientRect().height,
      e.classList.contains('touch-target') ? parseFloat(getComputedStyle(e, '::after').height) || 0 : 0))))
  const over = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
  await page.screenshot({ path: join(SHOTS, 'E3-cut-820.png') })
  if (after.header > before.header) ok('E3', `finger drag moved the line ${before.header} → ${after.header} mm`)
  else bad('E3', 'touch drag', JSON.stringify({ before, after, lb }))
  if (targets.every(h => h >= 44)) ok('E3b', `coarse pointer: every control ≥ 44 px (${targets.join('/')})`)
  else bad('E3b', 'touch targets under 44 px', targets.join('/'))
  if (cardOverflow <= 0 && over <= 0) ok('E3c', '820 px: no horizontal scroll on the card or the dialog')
  else bad('E3c', '820 px overflow', JSON.stringify({ cardOverflow, over }))
  await page.keyboard.press('Escape'); await settle(800)
  await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: false })
  await cdp.send('Emulation.setEmulatedMedia', { features: [] })
} catch (e) { bad('E3', 'threw', e.message.split('\n')[0]) }

// ── E4 — other papers: landscape, cream, watermark, a two-page PDF ───────────────────────────────────────────
await page.setViewportSize({ width: 1440, height: 900 })
const papers = [
  { id: 'E4a', file: 'entete-paysage.png', label: 'landscape page' },
  { id: 'E4b', file: 'entete-creme.jpg', label: 'cream paper — its band must print white', cream: true },
  { id: 'E4c', file: 'entete-filigrane.png', label: 'watermark, « Page entière » chosen by hand', page: true },
  { id: 'E4d', file: 'entete-deux-pages.pdf', label: 'two-page PDF (only page 1 may be used)' },
]
for (const paper of papers) {
  try {
    await openCard(page)
    await openImport(page, paper.file)
    const r = await readout(page)
    if (paper.page && (await mode(page)) === 'bands') await dialog(page).locator('[role="radio"]:has-text("Page entière")').click()
    const m = await mode(page)
    await page.screenshot({ path: join(SHOTS, `${paper.id}-cut.png`) })
    const p = await previewPdf(page)
    const info = p.pdf ? pdfImage(p.pdf, `${paper.id}-preview.png`) : null
    const whiteCorner = !paper.cream || (info?.corner ?? []).every(c => c >= 250)
    if (p.status === 200 && info?.pages === 1 && r.header > 0 && whiteCorner)
      ok(paper.id, `${paper.label}: « ${r.text.replace(/^.*?(En-tête)/, '$1')} », mode ${m}, aperçu ${info.images} image(s), corner rgb(${info.corner.join(',')}) — shots/${paper.id}-preview.png`)
    else bad(paper.id, paper.label, JSON.stringify({ readout: r, mode: m, status: p.status, info }))
    await page.keyboard.press('Escape'); await settle(800)
  } catch (e) { bad(paper.id, 'threw', e.message.split('\n')[0]) }
}

// ── E5 — the archive carries the letterhead's three bands ─────────────────────────────────────────────────────
try {
  const keys = sql(keysSql).split('|').filter(k => k !== 'NULL')
  await freshWindow()
  const step = await api('/auth/step-up', { method: 'POST', json: { action: 'download-clinic-archive', password: ADMIN.password, totpCode: totp(ADMIN.totp) } })
  const token = step.json?.value?.confirmationToken ?? step.json?.confirmationToken
  const archive = await api('/backup/archive', { headers: { 'X-Step-Up-Confirmation': token } })
  const zip = join(OUT, 'E5-archive.zip'); writeFileSync(zip, archive.buf)
  const listed = JSON.parse(execFileSync('python', ['-c', `
import zipfile, json, sys
z = zipfile.ZipFile(sys.argv[1]); names = z.namelist()
clinics = [n for n in names if n.lower().endswith('.json') and 'clinic' in n.lower() and 'subscription' not in n.lower()]
text = "".join(z.read(n).decode('utf-8', 'replace') for n in clinics)
print(json.dumps({"names": len(names), "keys": [k for k in sys.argv[2:] if any(n.endswith(k) for n in names)], "inRows": [k for k in sys.argv[2:] if k in text]}))`, zip, ...keys]).toString())
  if (archive.status === 200 && keys.length === 3 && listed.keys.length === 3 && listed.inRows.length === 3)
    ok('E5', `archive (${listed.names} entries) carries the 3 letterhead blobs, and the clinic row names them`)
  else bad('E5', 'archive', JSON.stringify({ step: step.status, status: archive.status, keys, listed }))
} catch (e) { bad('E5', 'threw', e.message.split('\n')[0]) }

if (pageErrors.length) console.log('page errors:', pageErrors.slice(0, 5))
await browser.close()

{
  const [h, f, b] = OWNER_KEYS.split('|').map(v => (v === 'NULL' ? 'NULL' : `'${v}'`))
  sql(`update "Clinics" set "LetterheadHeaderStorageKey"=${h},"LetterheadFooterStorageKey"=${f},"LetterheadBodyStorageKey"=${b} where "Id"='${CLINIC}'`)
  console.log('letterhead restored to:', sql(keysSql))
}
console.log('\n── findings ──')
findings.length ? findings.forEach(f => console.log(JSON.stringify(f))) : console.log('ALL CLEAR')
