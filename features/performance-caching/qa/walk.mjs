// QA walk for plan.md (BR-1..BR-5). Usage: node walk.mjs <scratchpad> <api-stdout-file>
import { createRequire } from 'node:module'
import { createHmac } from 'node:crypto'
import { readFileSync, writeFileSync, statSync, existsSync, mkdirSync, openSync, readSync, closeSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const [SCRATCH, API_LOG] = process.argv.slice(2)
const { chromium } = createRequire(join(SCRATCH, 'package.json'))('playwright-core')
const HERE = dirname(fileURLToPath(import.meta.url))
const SHOTS = join(HERE, 'shots'); mkdirSync(SHOTS, { recursive: true })

const WEB = 'http://localhost:3099'
const PATIENT = 'd3220000-0000-4000-8000-000000000003'
const ADMIN = { email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', totp: '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO' }
const SECRETARY = { email: 'qa.secretary@ibnkhaldoun.test', password: 'QaAudit2026!y' }
const WITH_PREVIEW = 16
const NO_PREVIEW_FILE = 'coupe-jpeg-2000.dcm'
const NO_PREVIEW_ID = 'a2c82851-fe5e-44d9-99ce-ecee675547b1'
const VIEWER_FILE = 'beb03531-bb1b-4114-9f5e-4787d3ae6029.jpg'

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }

// ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────
const b32 = s => { const a = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'; let bits = ''; for (const c of s) bits += a.indexOf(c).toString(2).padStart(5, '0'); return Buffer.from(bits.match(/.{8}/g).map(b => parseInt(b, 2))) }
const totp = (secret, w) => { const buf = Buffer.alloc(8); buf.writeBigUInt64BE(BigInt(w)); const h = createHmac('sha1', b32(secret)).update(buf).digest(); const o = h[h.length - 1] & 15; return String((h.readUInt32BE(o) & 0x7fffffff) % 1e6).padStart(6, '0') }
const usedFile = join(SCRATCH, 'totp-last-window.txt')
async function freshWindow() {
  const last = existsSync(usedFile) ? Number(readFileSync(usedFile, 'utf8')) : 0
  let w = Math.floor(Date.now() / 30000)
  if (w <= last || (Date.now() / 1000) % 30 > 25) { await new Promise(r => setTimeout(r, ((Math.max(last, w) + 1) * 30000 - Date.now()) + 1000)); w = Math.floor(Date.now() / 30000) }
  writeFileSync(usedFile, String(w)); return w
}
const LINE = /GET (\S+) \S+ (\d+) in (\d+) ms \S+ (\d+) queries/
const logMark = () => statSync(API_LOG).size
function previewsSince(mark) {
  const size = statSync(API_LOG).size, buf = Buffer.alloc(size - mark), fd = openSync(API_LOG, 'r')
  readSync(fd, buf, 0, buf.length, mark); closeSync(fd)
  return buf.toString('utf8').split(/\r?\n/).map(l => l.match(LINE)).filter(m => m && m[1].endsWith('/preview')).map(m => Number(m[2]))
}
const tally = codes => codes.reduce((t, c) => ({ ...t, [c]: (t[c] || 0) + 1 }), {})
const settle = ms => new Promise(r => setTimeout(r, ms))
// The list renders a table AND a card form, one hidden by a breakpoint class — always address the visible one.
const visibleText = (page, t) => page.locator(`text=${t} >> visible=true`).first()

async function signIn(page, who) {
  await page.goto(`${WEB}/login`, { waitUntil: 'domcontentloaded' })
  await page.fill('input[type=email]', who.email)
  await page.fill('input[type=password]', who.password)
  if (who.totp) {
    await page.click("button:has-text('Se connecter')")
    await page.waitForSelector('input[inputmode=numeric]', { timeout: 30000 })
    const w = await freshWindow()
    await page.fill('input[inputmode=numeric]', totp(who.totp, w)) // auto-submits on the 6th digit
  } else {
    await page.click("button:has-text('Se connecter')")
  }
  await page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 60000 })
}
async function closeOverlays(page) {
  for (let i = 0; i < 3; i++) {
    const d = page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible')
    if (await d.count() === 0) return
    console.log(`     (closing an overlay: ${(await d.first().innerText()).slice(0, 60).replace(/\n/g, ' ')}…)`)
    await page.keyboard.press('Escape'); await settle(500)
  }
}
async function openFiles(page) {
  await page.goto(`${WEB}/patients/${PATIENT}/files`, { waitUntil: 'domcontentloaded' })
  await visibleText(page, VIEWER_FILE).waitFor({ timeout: 90000 })
  await closeOverlays(page)
}
async function paintedTiles(page) {
  // Tiles load lazily (IntersectionObserver), so bring every row into view before counting.
  for (let i = 0; i < 12; i++) { await page.mouse.wheel(0, 600); await settle(250) }
  await settle(2500)
  return page.$$eval('img[src^="blob:"]', imgs => ({ total: imgs.length, painted: imgs.filter(i => i.complete && i.naturalWidth > 0).length }))
}

// ── run ──────────────────────────────────────────────────────────────────────────────────────────────────
const browser = await chromium.launch({ channel: 'chrome', headless: true })
const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
const page = await context.newPage()
const cdp = await context.newCDPSession(page)
await cdp.send('Network.enable')
const net = []
const urlById = {}
cdp.on('Network.requestWillBeSent', e => { urlById[e.requestId] = e.request.url })
cdp.on('Network.requestWillBeSentExtraInfo', e => { const inm = e.headers['If-None-Match'] || e.headers['if-none-match']; if (inm) net.push({ id: e.requestId, inm }) })
const statusById = {}
cdp.on('Network.responseReceived', e => { if (e.response.url.includes('/preview')) statusById[e.requestId] = e.response.status })
const previewUrls = []
page.on('request', r => { if (r.url().includes('/preview')) previewUrls.push(r.url()) })

try {
  await signIn(page, ADMIN)
  ok('login', `admin signed in → ${new URL(page.url()).pathname}`)
} catch (e) { bad('login', 'admin sign-in failed', e.message) }

// BR-1 + BR-5 — first load
let firstCount = 0
try {
  const mark = logMark(); previewUrls.length = 0
  await openFiles(page)
  const t = await paintedTiles(page)
  await page.screenshot({ path: join(SHOTS, 'BR-1.png'), fullPage: false })
  await settle(1000)
  const codes = previewsSince(mark); firstCount = codes.length
  console.log(`     tiles: ${t.painted}/${t.total} painted · server previews: ${JSON.stringify(tally(codes))}`)
  if (t.total > 0 && t.painted === t.total && codes.length >= t.total && codes.every(c => c === 200)) ok('BR-1', `${t.painted} thumbnails painted, every preview answered 200`)
  else bad('BR-1', 'first load', JSON.stringify({ ...t, codes: tally(codes) }))
  if (t.total > WITH_PREVIEW) bad('BR-1', `more images than files with a stand-in (${t.total} > ${WITH_PREVIEW})`)
  const zipAsked = previewUrls.some(u => u.includes(NO_PREVIEW_ID))
  const zipRow = visibleText(page, NO_PREVIEW_FILE)
  if (!zipAsked && await zipRow.isVisible()) ok('BR-5', `${NO_PREVIEW_FILE} listed, no preview request made for it`)
  else bad('BR-5', 'file with no stand-in', `previewRequested=${zipAsked}`)
} catch (e) { bad('BR-1', 'threw', e.message) }

// BR-2 — full reload revalidates
try {
  const mark = logMark(); net.length = 0
  await page.reload({ waitUntil: 'domcontentloaded' })
  await visibleText(page, VIEWER_FILE).waitFor({ timeout: 90000 }); await closeOverlays(page)
  const t = await paintedTiles(page)
  await page.screenshot({ path: join(SHOTS, 'BR-2.png'), fullPage: false })
  await settle(1000)
  const codes = previewsSince(mark)
  const conditional = net.filter(n => (urlById[n.id] || '').includes('/preview')).length
  const cdpCodes = tally(net.map(n => statusById[n.id]).filter(Boolean))
  console.log(`     tiles: ${t.painted}/${t.total} painted · server previews: ${JSON.stringify(tally(codes))} · browser sent If-None-Match on ${conditional} · CDP statuses ${JSON.stringify(cdpCodes)}`)
  if (t.total > 0 && t.painted === t.total && codes.length >= t.total && codes.every(c => c === 304) && conditional >= t.total) ok('BR-2', `${t.painted} painted again; all ${codes.length} previews revalidated with 304`)
  else bad('BR-2', 'reload', JSON.stringify({ ...t, codes: tally(codes), conditional }))
} catch (e) { bad('BR-2', 'threw', e.message) }

// BR-3 — viewer
try {
  await visibleText(page, VIEWER_FILE).click()
  const dlg = page.locator('[role="dialog"]:visible').first()
  await dlg.waitFor({ timeout: 20000 }); await settle(3000)
  const imgs = await dlg.locator('img').evaluateAll(xs => xs.map(i => ({ painted: i.complete && i.naturalWidth > 0, w: i.naturalWidth })))
  const text = (await dlg.innerText()).slice(0, 160).replace(/\n/g, ' | ')
  await page.screenshot({ path: join(SHOTS, 'BR-3.png'), fullPage: false })
  console.log(`     viewer images: ${JSON.stringify(imgs)} · text: ${text}`)
  if (imgs.some(i => i.painted && i.w > 40) && !/erreur|impossible|échec/i.test(text)) ok('BR-3', 'viewer opened and painted the image')
  else bad('BR-3', 'viewer', JSON.stringify(imgs))
  await page.keyboard.press('Escape'); await settle(500)
} catch (e) { bad('BR-3', 'threw', e.message) }

// BR-4 — another user in the same browser (same HTTP cache)
try {
  await page.evaluate(() => fetch('/bff/auth/local-logout', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' }))
  await signIn(page, SECRETARY)
  const mark = logMark(); net.length = 0
  await openFiles(page)
  const t = await paintedTiles(page)
  await page.screenshot({ path: join(SHOTS, 'BR-4.png'), fullPage: false })
  await settle(1000)
  const codes = previewsSince(mark)
  console.log(`     secretary tiles: ${t.painted}/${t.total} painted · server previews: ${JSON.stringify(tally(codes))}`)
  if (t.total > 0 && t.painted === t.total && codes.length >= t.total) ok('BR-4', `secretary: ${t.painted} painted, all ${codes.length} previews reached the server (${JSON.stringify(tally(codes))})`)
  else bad('BR-4', 'second user', JSON.stringify({ ...t, codes: tally(codes) }))
} catch (e) { bad('BR-4', 'threw', e.message) }

await browser.close()
console.log(findings.length ? `\nFINDINGS:\n${findings.map(f => JSON.stringify(f)).join('\n')}` : '\nALL CLEAR')
