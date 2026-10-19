#!/usr/bin/env node
/* ═══════════════════════════════════════════════════════════════════════════
   capture-screens.mjs — the captures of the image-led pages (src/img/v-*.png
   on a laptop, src/img/p-*.png on a phone): agenda, dossier patient,
   application web.

   The twin of capture-phones.mjs, and the same rules: the dev stack (web :3000,
   api :5000), playwright-core npm-installed in a scratch folder, the dev QA
   account, READ-ONLY (the one dialog it opens is closed with Escape).

   What the browser sees is NOT the database, on purpose, and nothing here is
   ever written back:
     • the test fixtures are filtered out of every API answer;
     • the demo patients are RENAMED after the cast of « Friends » (owner,
       18/10/2026: no real-looking patient names on the site) — CAST below;
     • the agenda week of 12 October 2026 is GENERATED (WEEK below): the dev
       database holds two visits a day, and an empty agenda sells nothing.

   1440x900 at 2x, one laptop screen per capture, never a full page; then an
   ffmpeg crop. ⚠️ The app's left menu is always cut off: it shows « Rappels »,
   which is not in service yet (BRIEF « Claims »). The phone shots are p-*,
   never m-*: m-* are the homepage's own, and the homepage is not touched.

   ⚠️ Not taken, deliberately: the dashboard (it counts the fixtures, « 697
   séances à clôturer ») and « Chèques à encaisser » (every row « En retard »,
   a badge computed server-side whatever the browser's clock says).

   CAPTURE_EMAIL=… CAPTURE_PASSWORD=… CAPTURE_TOTP_SECRET=… node capture-screens.mjs
   ═══════════════════════════════════════════════════════════════════════════ */
import { chromium } from 'playwright-core'
import crypto from 'node:crypto'
import { mkdirSync, existsSync } from 'node:fs'
import { execFileSync } from 'node:child_process'
import { join } from 'node:path'

const OUT = process.env.CAPTURE_OUT || 'capture-out'; mkdirSync(OUT, { recursive: true })
// The dev QA account (never a real one). Values: ~/.claude/skills/clinic-browser/SKILL.md § The account.
const { CAPTURE_EMAIL: EMAIL, CAPTURE_PASSWORD: PASS, CAPTURE_TOTP_SECRET: SECRET } = process.env
if (!EMAIL || !PASS || !SECRET) { console.error('set CAPTURE_EMAIL, CAPTURE_PASSWORD and CAPTURE_TOTP_SECRET'); process.exit(1) }
const KARIM = 'd48ed6b4-6e1e-40b5-b14a-224b33c6f512'
const KARIM_TWO_ACTS = 'f7f614ac-a0c5-4a89-be88-006fb8ee515f'   // one séance: a crown and a panoramic

const totp = () => {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'; let bits = ''
  for (const c of SECRET) bits += A.indexOf(c).toString(2).padStart(5, '0')
  const key = Buffer.from(bits.match(/.{8}/g).map(b => parseInt(b, 2)))
  const ctr = Math.floor(Date.now() / 30000), buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(ctr / 2 ** 32), 0); buf.writeUInt32BE(ctr >>> 0, 4)
  const h = crypto.createHmac('sha1', key).update(buf).digest(), o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1e6).toString().padStart(6, '0')
}

// ── the cast ───────────────────────────────────────────────────────────────
const CAST = {
  'Karim Hamdi': 'Ross Geller', 'Leila Gharbi': 'Rachel Green', 'Mehdi Bouazizi': 'Chandler Bing',
  'Nadia Jelassi': 'Monica Geller', 'Amine Rekik': 'Joey Tribbiani', 'Sonia Trabelsi': 'Phoebe Buffay',
  'Hédi Chaabane': 'Mike Hannigan', 'Youssef Mrad': 'Richard Burke', 'Amine Trabelsi': 'Jack Geller',
  'Fatma Zouari': 'Janice Litman', 'Emna Belhadj': 'Carol Willick', 'eya gharbi': 'Emily Waltham',
}
const castOf = (first, last) => CAST[`${first} ${last}`]?.split(' ')
const rename = (s) => { for (const [from, to] of Object.entries(CAST)) s = s.replaceAll(from, to); return s }

// ── the fixture filter (capture-phones.mjs, plus two leftovers it never met) ─
const JUNK = /\b(QA\w*|E2E|B\d-Test|Test Alpha|Test Patient)\b|QA-|Flex \d|Flow \d|Conflit|Retrait|oumayma|benkhalifa|Auditeur|mtx|mtu|mts|mug|muh|\bnew(est)? patient\b|^abc abc$/i
const NAMEKEY = /(name|patient|doctor|practitioner|title|label|fullname|firstname|lastname)$/i
const isJunk = (o) => {
  if (!o || typeof o !== 'object' || Array.isArray(o)) return false
  for (const [k, v] of Object.entries(o)) {
    if (typeof v === 'string' && NAMEKEY.test(k) && JUNK.test(v)) return true
    if (v && typeof v === 'object' && !Array.isArray(v) && /patient|doctor|practitioner/i.test(k) && isJunk(v)) return true
  }
  return false
}
const clean = (v) => {
  if (Array.isArray(v)) return v.filter(e => !isJunk(e)).map(clean)
  if (v && typeof v === 'object') {
    const o = {}; for (const [k, x] of Object.entries(v)) o[k] = clean(x)
    const c = typeof o.firstName === 'string' && castOf(o.firstName, o.lastName)
    if (c) [o.firstName, o.lastName] = c
    if (Array.isArray(o.items)) { o.items = o.items.slice(0, 8); for (const n of ['totalCount', 'total']) if (typeof o[n] === 'number') o[n] = o.items.length }
    return o
  }
  if (typeof v === 'string') return rename(v.replace(/Dr QA Auditeur|QA Auditeur/g, 'Dr Salma Ben Youssef'))
  return v
}

// ── the week, generated ────────────────────────────────────────────────────
const ACT = {
  detartrage: ['8226ce23-e8e3-4b34-9f75-2069c3b92605', 'Détartrage', '#6BAA75', 30],
  carie: ['18285b7e-5ca1-47bb-abf3-0525a42acafd', 'Soin de carie / obturation', '#2A9D8F', 40],
  couronne: ['a976d389-7593-4ba9-b87a-6b8d3bf97572', 'Couronne / bridge (par élément)', '#9B8EDC', 60],
  provisoire: ['cccd89c5-3a0c-48cc-901c-48c9ee17af8f', 'Couronne provisoire', '#B7A6F7', 30],
  enfant: ['a4334d43-3e24-4ca9-a5a8-ab568c244528', 'Soin dentaire enfant (dent de lait)', '#7E9E3C', 30],
  paro: ['c0ce0623-ef1c-4811-b03d-dffd55dc8018', 'Traitement parodontal (surfaçage / curetage)', '#42804E', 45],
  consult: ['d2e278b1-18aa-4161-bed9-3a0ead16a719', 'Consultation / examen bucco-dentaire', '#6C757D', 30],
  controle: ['685ce676-e5c2-46fa-be6e-ae9fd330df46', 'Contrôle / suivi', '#495057', 30],
  implant: ['8ed0e23d-9947-447c-b62d-7581e90ae623', 'Implant dentaire', '#E9A23B', 60],
  extraction: ['fa2c658d-bcca-4e8f-9080-ba8f115574fb', 'Extraction simple', '#E76F51', 30],
  chir: ['8a066152-9acf-46b4-ae65-8fad17f5f55e', 'Extraction chirurgicale (sagesse / dent incluse)', '#B44A32', 60],
  canal: ['719d83b6-9169-49ec-887d-f9e8b8a938f3', 'Traitement de canal (dévitalisation)', '#4F83CC', 60],
  pano: ['7514b77f-1e19-493d-8392-865397d39388', 'Radiographie panoramique', '#2563EB', 15],
  retro: ['a24162d8-8c34-430b-bc8b-36e6c5edc652', 'Radiographie rétro-alvéolaire', '#60A5FA', 15],
  prothese: ['f80d3e0c-efff-477a-8647-a8a3f085fa8e', 'Prothèse amovible (partielle / complète)', '#2DD4BF', 60],
  rebasage: ['8e0b1eb2-7846-498b-b5a9-736c61c1097d', 'Réparation / rebasage de prothèse', '#0F9488', 30],
  ortho: ['fc02644d-2f3b-4720-875b-14cf10b0d29a', 'Séance orthodontique (contrôle / activation)', '#BE3455', 30],
  facette: ['1d4c9e86-86af-456d-bf82-f9db1996e3ab', 'Facette', '#4338CA', 60],
  blanchiment: ['91e359bc-c1e3-46fb-91fe-238c7a29a069', 'Blanchiment dentaire', '#6366F1', 60],
  gouttiere: ['1a3e13a4-defd-46c3-9cad-d18bc38eee3f', 'Gouttière occlusale (bruxisme)', '#5EEAD4', 45],
  inlay: ['13f72d23-f1f3-40c7-875c-c7c1335693ff', 'Inlay-core (reconstitution corono-radiculaire)', '#6D48C7', 45],
  sillons: ['f9a6febb-f556-4d17-bb31-e2429dceb0b2', 'Scellement de sillons', '#5A7327', 30],
  surimplant: ['255be405-7942-43a0-819b-af188ce16dd7', 'Couronne sur implant', '#F3BE72', 45],
}
// [local start, patient, acts…] — Tunisian clinic hours, lunch 13:00–14:00.
const WEEK = {
  '2026-10-12': [['09:00', 'Rachel Green', 'detartrage'], ['09:30', 'Chandler Bing', 'carie'], ['10:15', 'Monica Geller', 'couronne'],
    ['11:15', 'Ben Geller', 'enfant'], ['11:45', 'Joey Tribbiani', 'paro'], ['12:30', 'Phoebe Buffay', 'consult'],
    ['14:00', 'Richard Burke', 'implant'], ['15:00', 'Janice Litman', 'extraction'], ['15:30', 'Mike Hannigan', 'canal'], ['16:30', 'Judy Geller', 'controle']],
  '2026-10-13': [['09:00', 'Ross Geller', 'couronne', 'pano'], ['10:15', 'Emma Geller', 'sillons'], ['10:45', 'Carol Willick', 'detartrage'],
    ['11:15', 'Frank Buffay', 'prothese'], ['12:15', 'Ursula Buffay', 'consult'], ['14:00', 'Estelle Leonard', 'carie'],
    ['14:45', 'Tag Jones', 'ortho'], ['15:15', 'Susan Bunch', 'blanchiment'], ['16:15', 'Gavin Mitchell', 'gouttiere']],
  '2026-10-14': [['09:00', 'Charlie Wheeler', 'detartrage'], ['09:30', 'Elizabeth Stevens', 'inlay'], ['10:15', 'Paul Stevens', 'chir'],
    ['11:15', 'Pete Becker', 'consult'], ['11:45', 'Barry Farber', 'paro'], ['12:30', 'Mindy Hunter', 'retro', 'controle'],
    ['14:00', 'Chandler Bing', 'provisoire'], ['14:30', 'Monica Geller', 'facette'], ['15:30', 'Rachel Green', 'carie'], ['16:15', 'Joey Tribbiani', 'canal']],
  '2026-10-15': [['09:00', 'Phoebe Buffay', 'detartrage'], ['09:30', 'Mike Hannigan', 'implant'], ['10:30', 'Ben Geller', 'enfant'],
    ['11:00', 'Janice Litman', 'gouttiere'], ['11:45', 'Richard Burke', 'carie'], ['12:30', 'Judy Geller', 'controle'],
    ['14:00', 'Emma Geller', 'enfant'], ['14:30', 'Carol Willick', 'couronne'], ['15:30', 'Frank Buffay', 'rebasage'], ['16:00', 'Ursula Buffay', 'paro']],
  '2026-10-16': [['09:00', 'Estelle Leonard', 'prothese'], ['10:00', 'Tag Jones', 'ortho'], ['10:30', 'Susan Bunch', 'carie'],
    ['11:15', 'Gavin Mitchell', 'chir'], ['12:15', 'Charlie Wheeler', 'consult'], ['14:00', 'Elizabeth Stevens', 'detartrage', 'pano'],
    ['14:45', 'Paul Stevens', 'surimplant'], ['15:30', 'Pete Becker', 'extraction'], ['16:00', 'Barry Farber', 'blanchiment']],
  '2026-10-17': [['09:00', 'Mindy Hunter', 'consult'], ['09:30', 'Ross Geller', 'controle'], ['10:00', 'Rachel Green', 'paro'], ['10:45', 'Joey Tribbiani', 'detartrage']],
}
const NOW = new Date('2026-10-14T10:20:00+01:00')
const uuid = (s) => { const h = crypto.createHash('md5').update(s).digest('hex'); return `${h.slice(0, 8)}-${h.slice(8, 12)}-4${h.slice(13, 16)}-a${h.slice(17, 20)}-${h.slice(20, 32)}` }
const NEXT = { Completed: [], InProgress: ['Completed', 'Cancelled', 'NoShow'], Confirmed: ['InProgress', 'Completed', 'Cancelled', 'NoShow'] }
function weekAppointments (template, from, to) {
  const out = []
  for (const [day, rows] of Object.entries(WEEK)) for (const [time, name, ...acts] of rows) {
    const start = new Date(`${day}T${time}:00+01:00`)
    if (start < from || start > to) continue
    const procs = acts.map((a, i) => ({ id: uuid(day + time + a), procedureTypeId: ACT[a][0], name: ACT[a][1], durationMinutes: ACT[a][3],
      colorHex: ACT[a][2], treatmentPlanItemId: null, treatmentPlanItemStepId: null, agreedCost: null, sequenceNumber: i }))
    const minutes = procs.reduce((n, p) => n + p.durationMinutes, 0)
    const end = new Date(start.getTime() + minutes * 60000)
    const status = end <= NOW ? 'Completed' : start <= NOW ? 'InProgress' : 'Confirmed'
    out.push({ ...template, id: uuid(day + time), patientId: uuid(name), patientName: name,
      appointmentDateTime: start.toISOString().replace('.000', ''), duration: `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}:00`,
      notes: null, status, allowedNextStatuses: NEXT[status], procedureTypeId: procs[0].procedureTypeId, procedureTypeName: procs[0].name,
      procedureColorHex: procs[0].colorHex, procedures: procs, treatmentPlanItemId: null, invoiceId: null, invoiceNumber: null, version: 1,
      // « true » hides the orange « non synchronisé » chip a Google-less dev clinic paints on every long block.
      isSyncedToGoogle: true })
  }
  return out
}

// ── one context's routes ───────────────────────────────────────────────────
async function dress (ctx) {
  await ctx.addInitScript(() => {
    const css = `nextjs-portal, [data-nextjs-toast], #__next-build-watcher { display: none !important; }
      ::-webkit-scrollbar { display: none !important; } * { scrollbar-width: none !important; }`
    const add = () => { const s = document.createElement('style'); s.textContent = css; document.documentElement.appendChild(s) }
    if (document.documentElement) add(); else document.addEventListener('DOMContentLoaded', add)
  })
  // ⚠️ Registered FIRST: Playwright runs the most recently registered matching route.
  await ctx.route(/localhost:5000\/api\/(?!auth)/, async (r) => {
    const u = new URL(r.request().url())
    if (u.searchParams.has('pageSize')) u.searchParams.set('pageSize', '200')
    const res = await r.fetch({ url: u.toString() })
    const type = res.headers()['content-type'] || ''
    if (!type.includes('json')) return r.fulfill({ response: res })
    let body = await res.text()
    try {
      let data = clean(JSON.parse(body))
      if (u.pathname === '/api/appointments' && u.searchParams.has('startDate') && Array.isArray(data) && data.length) {
        const week = weekAppointments(data[0], new Date(u.searchParams.get('startDate')), new Date(u.searchParams.get('endDate')))
        if (week.length) data = week
      }
      body = JSON.stringify(data)
    } catch {}
    return r.fulfill({ response: res, body })
  })
  await ctx.route('**/api/notifications/pending-reviews**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
  await ctx.route('**/api/notifications/unread-count**', r => r.fulfill({ status: 200, contentType: 'application/json', body: '{"unreadCount":0}' }))
  await ctx.route(/localhost:5000\/api\/notifications(\?|$)/, r => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
}

const settleOn = (p) => async () => { await p.waitForLoadState('networkidle').catch(() => {}); await p.waitForTimeout(3000) }
// ⚠️ The app scrolls its <main>, not the window: scroll the nearest scrolling ancestor of the element
// whose own text is exactly `txt`.
const scrollTo = (p, txt, offset) => p.evaluate(({ txt, offset }) => {
  const el = [...document.querySelectorAll('h1,h2,h3,h4,p,span,div,button')]
    .find(e => e.offsetParent && e.childElementCount <= 2 && e.textContent.trim() === txt)
  if (!el) return 'not found'
  let sc = el.parentElement
  while (sc && sc !== document.body) { const s = getComputedStyle(sc); if (/(auto|scroll)/.test(s.overflowY) && sc.scrollHeight > sc.clientHeight) break; sc = sc.parentElement }
  if (!sc || sc === document.body) sc = document.scrollingElement
  const top = sc === document.scrollingElement ? 0 : sc.getBoundingClientRect().top
  sc.scrollTop += el.getBoundingClientRect().top - top - offset
  return 'ok'
}, { txt, offset })
// CAPTURE_ONLY=w-odonto,p-odonto re-takes just those; the crops below skip what was not taken.
const ONLY = process.env.CAPTURE_ONLY ? process.env.CAPTURE_ONLY.split(',') : null
const shooter = (p, prefix) => async (name, prep) => {
  if (ONLY && !ONLY.includes(`${prefix}-${name}`)) return
  try { if (prep) await prep(); await settleOn(p)(); await p.screenshot({ path: `${OUT}/${prefix}-${name}.png` }); console.log('ok', prefix, name) }
  catch (e) { console.log('FAIL', prefix, name, e.message.split('\n')[0]) }
}
const toWeek = async (p) => {
  const week = p.locator('button:has-text("Semaine")').first()
  if (await week.getAttribute('aria-pressed') !== 'true') await week.click({ timeout: 5000 }).catch(() => {})
}
const toDay = async (p) => {
  const day = p.locator('button:has-text("Jour")').first()
  if (await day.getAttribute('aria-pressed') !== 'true') await day.click({ timeout: 5000 }).catch(() => {})
}
// The odontogram in « Symboles » — the teeth drawn — rather than « Cases » (owner, 18/10/2026).
const toSymbols = async (p) => {
  await p.locator('[role=group][aria-label="Dessin de l\'odontogramme"] button:has-text("Symboles")').first().click({ timeout: 10000 })
  await p.waitForTimeout(800)
}

const b = await chromium.launch({ channel: 'chrome' })

// ── the laptop ─────────────────────────────────────────────────────────────
const desk = await b.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2, locale: 'fr-FR', timezoneId: 'Africa/Tunis' })
await dress(desk)
const p = await desk.newPage()
await p.goto('http://localhost:3000/appointments', { waitUntil: 'domcontentloaded' })
await p.waitForURL(/login/, { timeout: 90000 })
await p.fill('input[type=email]', EMAIL)
await p.fill('input[type=password]', PASS)
await p.click('button:has-text("Se connecter")')
await p.waitForSelector('input[inputmode=numeric]', { timeout: 30000 })
await p.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
await p.fill('input[inputmode=numeric]', totp())
await p.waitForURL(u => !/login/.test(u.toString()), { timeout: 30000 })
console.log('signed in')

const shot = shooter(p, 'w')
await shot('patient', () => p.goto(`http://localhost:3000/patients/${KARIM}`, { waitUntil: 'domcontentloaded' }))
await shot('patient-tabs', async () => {
  await p.goto(`http://localhost:3000/patients/${KARIM}`, { waitUntil: 'domcontentloaded' }); await settleOn(p)()
  console.log('  scroll', await scrollTo(p, 'Fiches de soins', 90))
})
await shot('odonto', async () => {
  await p.goto(`http://localhost:3000/patients/${KARIM}`, { waitUntil: 'domcontentloaded' }); await settleOn(p)()
  console.log('  scroll', await scrollTo(p, 'Odontogramme', 24))
  await toSymbols(p)
  console.log('  scroll', await scrollTo(p, 'Odontogramme', 24))
})
// The odontogram page's own shots. Each one reopens the chart in « Symboles » first, then changes one thing.
const openOdonto = async () => {
  await p.goto(`http://localhost:3000/patients/${KARIM}`, { waitUntil: 'domcontentloaded' }); await settleOn(p)()
  await scrollTo(p, 'Odontogramme', 24); await toSymbols(p); await scrollTo(p, 'Odontogramme', 24)
}
const press = async (selector) => { await p.locator(selector).first().click({ timeout: 10000 }); await p.waitForTimeout(900) }
await shot('odonto-actes', async () => { await openOdonto(); await press(':is(button,[role=tab]):has-text("Actes faits")') })
await shot('odonto-mixte', async () => { await openOdonto(); await press('button:has-text("Mixte")') })
// One click on a tooth opens its editor; Escape closes it, nothing is saved.
await shot('odonto-dent', async () => { await openOdonto(); await press('[data-tooth="46"]') })
await p.keyboard.press('Escape')
// Last, because it leaves the chart in « Cases »: every other shot sets « Symboles » itself.
await shot('odonto-cases', async () => { await openOdonto(); await press('[role=group][aria-label="Dessin de l\'odontogramme"] button:has-text("Cases")') })
await p.clock.setFixedTime(NOW)
await shot('semaine', async () => {
  await p.goto('http://localhost:3000/appointments', { waitUntil: 'domcontentloaded' }); await settleOn(p)(); await toWeek(p)
})
await shot('cloturer', () => p.goto('http://localhost:3000/a-cloturer', { waitUntil: 'domcontentloaded' }))
if (!ONLY || ONLY.includes('w-rdv')) try {
  await p.goto(`http://localhost:3000/appointments?appointmentId=${KARIM_TWO_ACTS}`, { waitUntil: 'domcontentloaded' })
  const dlg = p.locator('[role=dialog]').first()
  await dlg.waitFor({ timeout: 30000 }); await settleOn(p)()
  await dlg.screenshot({ path: `${OUT}/w-rdv.png` }); console.log('ok w rdv')
  await p.keyboard.press('Escape')   // read-only: nothing is saved
} catch (e) { console.log('FAIL w rdv', e.message.split('\n')[0]) }
// Back on the agenda in the DAY view: the app remembers the view, and the phone below inherits it.
await p.goto('http://localhost:3000/appointments', { waitUntil: 'domcontentloaded' }); await settleOn(p)(); await toDay(p)
const state = await desk.storageState()
await desk.close()   // ⚠️ before the phone opens: two live holders of one session family end it

// ── the phone ──────────────────────────────────────────────────────────────
const tel = await b.newContext({
  viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, storageState: state,
  locale: 'fr-FR', timezoneId: 'Africa/Tunis',
  userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Mobile Safari/537.36',
})
await dress(tel)
const q = await tel.newPage()
const pshot = shooter(q, 'p')
await pshot('odonto', async () => {
  await q.goto(`http://localhost:3000/patients/${KARIM}`, { waitUntil: 'domcontentloaded' }); await settleOn(q)()
  console.log('  scroll', await scrollTo(q, 'Odontogramme', 16))
  await toSymbols(q)
  // On a phone one arch shows at a time, six teeth wide: the lower one is where this chart's work is
  // (a crown, a root canal, an extraction). Scrolled to the card's TITLE, not further: one more step and
  // the treatment card below it comes into frame, with its « Reste à payer » — no money on this page.
  await q.locator('button:has-text("Mandibule (bas)")').first().click({ timeout: 10000 })
  await q.waitForTimeout(800)
  console.log('  scroll', await scrollTo(q, 'Odontogramme', 16))
})
await q.clock.setFixedTime(NOW)
await pshot('agenda', async () => { await q.goto('http://localhost:3000/appointments', { waitUntil: 'domcontentloaded' }); await settleOn(q)(); await toDay(q) })
await pshot('semaine', async () => { await q.goto('http://localhost:3000/appointments', { waitUntil: 'domcontentloaded' }); await settleOn(q)(); await toWeek(q) })
await pshot('cloturer', () => q.goto('http://localhost:3000/a-cloturer', { waitUntil: 'domcontentloaded' }))
await b.close()

// ── the crops: [capture, output, width, height, x, y] in device pixels (2x) ─
const CROPS = [
  ['w-semaine', 'v-agenda-semaine', 2368, 1656, 512, 144],
  ['w-cloturer', 'v-cloturer', 2269, 943, 562, 497],
  ['w-odonto', 'v-odonto', 2269, 1130, 562, 115],
  ['w-odonto-actes', 'v-odonto-actes', 2269, 979, 562, 115],
  ['w-odonto-dent', 'v-odonto-dent', 892, 1238, 994, 281],
  ['w-odonto-cases', 'v-odonto-cases', 1100, 420, 1148, 450],
  ['w-odonto-mixte', 'v-odonto-mixte', 2171, 629, 611, 429],
  ['w-patient-tabs', 'v-onglets', 2269, 626, 562, 281],
  ['w-patient-tabs', 'v-ordonnance', 916, 197, 610, 684],
  ['w-patient', 'v-sante', 2269, 368, 562, 453],
  ['w-rdv', 'v-seance-actes', 530, 338, 1262, 250],
]
const local = process.env.LOCALAPPDATA
const ffw = local && join(local, 'Microsoft', 'WinGet', 'Packages', 'Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe', 'ffmpeg-9.0-full_build', 'bin', 'ffmpeg.exe')
const FF = ffw && existsSync(ffw) ? ffw : 'ffmpeg'
for (const [from, to, w, h, x, y] of CROPS) {
  if (!existsSync(`${OUT}/${from}.png`)) { console.log('skip', to, '(no', from + ')'); continue }
  execFileSync(FF, ['-y', '-loglevel', 'error', '-i', `${OUT}/${from}.png`, '-vf', `crop=${w}:${h}:${x}:${y}`, `${OUT}/${to}.png`])
  console.log('crop', to)
}
console.log(`\nLook at every ${OUT}/v-*.png and p-*.png, then copy the good ones into src/img/.`)
