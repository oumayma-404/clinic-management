// QA walk for « Congés du praticien » — one launch, every browser row of plan.md. Fixes nothing.
// Run: node walk.mjs <scratchpad>  (playwright-core + totp.mjs live in the scratchpad; stack = :5099 / :3098).
import { createRequire } from 'node:module'
import { execSync } from 'node:child_process'
import { mkdirSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const SCRATCH = process.argv[2]
const require = createRequire(path.join(SCRATCH, 'package.json'))
const { chromium } = require('playwright-core')
const { totp } = await import(pathToFileURL(path.join(SCRATCH, 'totp.mjs')).href)

const BASE = 'http://localhost:3098'
const SHOTS = path.join(path.dirname(fileURLToPath(import.meta.url)), 'shots')
mkdirSync(SHOTS, { recursive: true })
const SALMA = '9ed3d280-e3a1-4f11-ad19-8c993dd0da93'
const SALMA_NAME = 'Salma Ben Youssef'
const W = { mon: '2026-11-02', tue: '2026-11-03', wed: '2026-11-04', thu: '2026-11-05', sun: '2026-11-08' }

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }
const sql = (q) => execSync(`docker exec clinic-postgres psql -U clinic_user -d clinic_conges_qa -tAc "${q.replace(/"/g, '\\"')}"`).toString().trim()
const leaveRows = () => sql(`select "StartDate"||'|'||"EndDate"||'|'||coalesce("Note",'') from "DoctorLeaves" where "DoctorId"='${SALMA}' order by 1`)
const salmaWeekVisits = () => sql(`select count(*) from "Appointments" where "DoctorId"='${SALMA}' and "Status"=1 and "AppointmentDateTime" >= '2026-11-01 23:00+00' and "AppointmentDateTime" < '2026-11-04 23:00+00'`)

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR', timezoneId: 'Africa/Tunis' })
await ctx.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
const page = await ctx.newPage()
page.setDefaultTimeout(15000)
const consoleErrors = []
page.on('pageerror', (e) => consoleErrors.push(String(e)))

const shot = async (name, opts = {}) => { await page.screenshot({ path: path.join(SHOTS, `${name}.png`), ...opts }) }
const size = async (w, h) => { await page.setViewportSize({ width: w, height: h }); await page.waitForTimeout(400) }
const pollText = async (re, ms = 8000) => {
  const end = Date.now() + ms
  while (Date.now() < end) {
    const t = await page.evaluate(() => document.body.innerText)
    const m = t.match(re)
    if (m) return m[0]
    await page.waitForTimeout(150)
  }
  return null
}
const agenda = async (qs) => {
  const leaves = page.waitForResponse((r) => r.url().includes('/doctors/leaves') && r.request().method() === 'GET', { timeout: 20000 }).catch(() => null)
  await page.goto(`${BASE}/appointments?${qs}`)
  await leaves
  await page.waitForSelector('[data-agenda-day], [aria-current="date"]', { timeout: 20000 }).catch(() => {})
  await page.waitForFunction(() => !document.querySelector('[aria-label="Chargement des rendez-vous"]'), null, { timeout: 20000 }).catch(() => {})
  await page.waitForTimeout(500)
}
const dialog = () => page.locator('[role="dialog"]:visible').last()
const alertDialog = () => page.locator('[role="alertdialog"]:visible').last()
const chips = () => page.locator(`button[aria-label^="Congé de ${SALMA_NAME}"]:visible`)
const congeBtn = () => page.locator('button[aria-label="Ajouter un congé"]:visible')
const hatched = (day, hour) => page.evaluate(([d, h]) => {
  const el = document.querySelector(`[data-agenda-day="${d}"][data-agenda-hour="${h}"]`)
  return el ? getComputedStyle(el).backgroundImage.includes('repeating-linear-gradient') : null
}, [day, hour])
const weekHeaderText = (day) => page.evaluate((d) => {
  const label = new Date(`${d}T12:00:00`).toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long' })
  const b = [...document.querySelectorAll('button[aria-label^="Voir le "]')].find((x) => x.getAttribute('aria-label').toLowerCase().includes(label.toLowerCase()) && x.offsetParent)
  return b ? `${b.innerText.replace(/\s+/g, ' ')} || ${b.getAttribute('aria-label')}` : null
}, day)

async function signIn() {
  await page.goto(`${BASE}/appointments`)
  await page.waitForURL(/\/login/, { timeout: 30000 })
  await page.fill('input[type=email]', 'salma.benyoussef@cabinet-ibnkhaldoun.tn')
  await page.fill('input[type=password]', 'QaAudit2026!y')
  await page.click('button:has-text("Se connecter")')
  await page.waitForSelector('input[inputmode=numeric]', { timeout: 20000 })
  await page.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
  await page.fill('input[inputmode=numeric]', totp('4YRLT22RBPRP3RRKUKLFQERU4H62BRBO'))
  await page.waitForURL((u) => !/\/login/.test(u.toString()), { timeout: 30000 })
}

const S = []
const scenario = (id, run) => S.push({ id, run })

// ── Tier A ───────────────────────────────────────────────────────────────────────────────────────────────────
scenario('A1', async () => {
  await size(1440, 900)
  await agenda(`date=${W.mon}&view=week`)
  const btn = congeBtn()
  const n = await btn.count()
  if (n !== 1) return bad('A1', `expected 1 visible « Ajouter un congé » at 1440, got ${n}`)
  const text = (await btn.innerText()).trim()
  const nouveau = page.locator('button:visible', { hasText: /^\s*Nouveau\s*$/ }).first()
  const [b, nb] = [await btn.boundingBox(), await nouveau.boundingBox()]
  const barH = await btn.evaluate((el) => el.parentElement.getBoundingClientRect().height)
  await shot('A1-1440', { clip: { x: 0, y: 0, width: 1440, height: 220 } })
  // Since run 1's F1 the label hinges on the bar (76rem): icon-only at 1440 with the rail open, labelled at 1536.
  nb && b.x + b.width <= nb.x + 1 && Math.abs(b.y - nb.y) < 6 && barH <= 60
    ? ok('A1', `1440: « ${text || '(icône)'} » left of « Nouveau » (x ${Math.round(b.x)} < ${Math.round(nb.x)}), bar ${Math.round(barH)} px`)
    : bad('A1', '1440 bar', `text=« ${text} » box=${JSON.stringify(b)} nouveau=${JSON.stringify(nb)} barH=${barH}`)
  await size(1536, 864)
  const t15 = (await congeBtn().innerText()).trim()
  t15 === 'Congé' ? ok('A1', '1536: labelled « Congé »') : bad('A1', '1536 label', `text=« ${t15} »`)
  await size(1440, 900)
  await size(820, 1180)
  const t2 = (await congeBtn().innerText()).trim()
  const [b2, nb2] = [await congeBtn().boundingBox(), await nouveau.boundingBox()]
  const barH2 = await congeBtn().evaluate((el) => el.parentElement.getBoundingClientRect().height)
  const pageOverflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
  await shot('A1-820', { clip: { x: 0, y: 0, width: 820, height: 220 } })
  // Since the review fix the bar gives the title its own line under a 40rem bar (820 with the rail open): two rows, never overflow.
  t2 === '' && nb2 && Math.abs(b2.y - nb2.y) < 6 && barH2 <= 100 && pageOverflow <= 0
    ? ok('A1', `820: icon-only, same row as « Nouveau », bar ${Math.round(barH2)} px (title on its own line), no page overflow`)
    : bad('A1', '820 bar', `text=« ${t2} » y=${b2?.y}/${nb2?.y} barH=${barH2} overflow=${pageOverflow}`)
})

scenario('A2', async () => {
  await size(390, 844)
  await agenda(`date=${W.mon}&view=week`)
  const btn = congeBtn()
  const n = await btn.count()
  const b = n ? await btn.first().boundingBox() : null
  await shot('A2-390', { clip: { x: 0, y: 0, width: 390, height: 200 } })
  n === 1 && b.height >= 44 ? ok('A2', `phone header button ${Math.round(b.width)}×${Math.round(b.height)} px`) : bad('A2', 'phone header button', `count=${n} box=${JSON.stringify(b)}`)
  await size(1440, 900)
})

scenario('A3', async () => {
  await agenda(`date=${W.mon}&view=week`)
  await congeBtn().click()
  const d = dialog()
  await d.waitFor()
  const title = (await d.locator('h2').first().innerText()).trim()
  const start = await page.inputValue('#leave-start')
  const end = await page.inputValue('#leave-end')
  const doctor = (await page.locator('#leave-doctor').innerText()).trim()
  const focused = await page.evaluate(() => document.activeElement?.tagName + ':' + (document.activeElement?.id || document.activeElement?.textContent?.slice(0, 20)))
  await shot('A3-dialog')
  title === 'Nouveau congé' && start === W.mon && end === W.mon
    ? ok('A3', `« ${title} », Du=${start}, Au=${end}, Praticien=« ${doctor} », focus=${focused}`)
    : bad('A3', 'new dialog prefill', `title=${title} start=${start} end=${end} doctor=${doctor}`)
})

scenario('A4', async () => {
  const doctor = (await page.locator('#leave-doctor').innerText()).trim()
  if (!doctor.includes(SALMA_NAME)) {
    await page.click('#leave-doctor')
    await page.getByRole('option', { name: SALMA_NAME }).click()
  }
  await page.fill('#leave-end', W.wed)
  const count = await page.locator('[role=dialog] p:has-text("jours")').first().innerText().catch(() => '')
  await page.fill('#leave-note', 'Vacances')
  await dialog().getByRole('button', { name: 'Enregistrer' }).click()
  const toast = await pollText(/Congé enregistré/)
  await page.waitForSelector('#leave-start', { state: 'detached', timeout: 5000 }).catch(() => {})
  const closed = await page.locator('#leave-start').count() === 0
  toast && closed ? ok('A4', `saved, dialog closed, live count read « ${count} »`) : bad('A4', 'save', `toast=${toast} closed=${closed}`)
})

scenario('A5', async () => {
  const rows = leaveRows()
  rows === `${W.mon}|${W.wed}|Vacances` ? ok('A5', `row ${rows}`) : bad('A5', 'DoctorLeaves row', rows)
})

scenario('A6', async () => {
  const desc = await pollText(/\d+ rendez-vous (sont déjà prévus|est déjà prévu) pendant ce congé\./, 4000)
  const voir = page.locator('[data-sonner-toast] button', { hasText: 'Voir' })
  await shot('A6-toast')
  const visits = salmaWeekVisits()
  desc === '6 rendez-vous sont déjà prévus pendant ce congé.' && (await voir.count()) === 1 && visits === '6'
    ? ok('A6', `« ${desc} » + « Voir »; 6 visits untouched in SQL`)
    : bad('A6', 'warning toast', `desc=${desc} voir=${await voir.count()} visits=${visits}`)
})

scenario('A7', async () => {
  const voir = page.locator('[data-sonner-toast] button', { hasText: 'Voir' })
  if (!(await voir.count())) return skip('A7', 'toast already dismissed before « Voir » could be pressed')
  await voir.click()
  await page.waitForTimeout(1200)
  const monCells = await page.locator(`[data-agenda-day="${W.mon}"]`).count()
  const tueCells = await page.locator(`[data-agenda-day="${W.tue}"]`).count()
  const filterText = await page.evaluate((n) => [...document.querySelectorAll('button[role="combobox"]')].filter((b) => b.offsetParent).map((b) => b.innerText).join(' | '), SALMA_NAME)
  const ribbon = await page.locator(`button[aria-label^="Congé de ${SALMA_NAME}"]:visible`).count()
  const hatch = await hatched(W.mon, 10)
  await shot('A7-day')
  monCells > 0 && tueCells === 0 && filterText.includes(SALMA_NAME) && ribbon === 1
    ? ok('A7', `Jour on ${W.mon}, filter « ${filterText} », ribbon shown, 10h hatched=${hatch}`)
    : bad('A7', 'Voir → day', `mon=${monCells} tue=${tueCells} filter=« ${filterText} » ribbon=${ribbon} hatch=${hatch}`)
})

scenario('A8', async () => {
  for (const [w, h] of [[1440, 900], [820, 1180]]) {
    await size(w, h)
    await agenda(`date=${W.mon}&view=week`)
    const n = await chips().count()
    const label = n ? (await chips().first().innerText()).trim() : ''
    const thu = await page.evaluate((d) => {
      const cell = document.querySelector(`[data-agenda-day="${d}"]`)
      return cell ? null : 'no-cell'
    }, W.thu)
    const hatchMon = await hatched(W.mon, 10)
    const header = await weekHeaderText(W.mon)
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
    await shot(`A8-${w}`)
    n === 3 && /Congé — Salma Ben Youssef/.test(label) && hatchMon === false && header && /RDV/.test(header) && overflow <= 0 && !thu
      ? ok('A8', `${w}: 3 ribbons « ${label} », Mon not hatched, header « ${header.split(' || ')[0]} »`)
      : bad('A8', `${w} unfiltered week`, `chips=${n} label=« ${label} » hatchMon=${hatchMon} header=${header} overflow=${overflow}`)
  }
  await size(1440, 900)
})

scenario('A9', async () => {
  await agenda(`date=${W.mon}&view=week&doctorId=${SALMA}`)
  const heads = await Promise.all([W.mon, W.tue, W.wed, W.thu].map(weekHeaderText))
  const hatch = await Promise.all([W.mon, W.tue, W.wed, W.thu].map((d) => hatched(d, 10)))
  const drawn = await page.getByText('Mehdi Bouazizi').filter({ visible: true }).count().catch(() => 0)
  await shot('A9-filtered')
  const congeHeads = heads.slice(0, 3).every((h) => h && /congé/.test(h.split(' || ')[0]))
  congeHeads && !/congé/.test(heads[3].split(' || ')[0]) && hatch.slice(0, 3).every(Boolean) && hatch[3] === false && drawn > 0
    ? ok('A9', `Mon–Wed headers « congé », hatched; Thu open; booked visit still drawn`)
    : bad('A9', 'filtered week', `heads=${JSON.stringify(heads.map((h) => h?.split(' || ')[0]))} hatch=${hatch} drawn=${drawn}`)
})

scenario('A10', async () => {
  await agenda(`date=${W.mon}&view=week`)
  await chips().nth(1).click()
  await dialog().waitFor()
  const title = (await dialog().locator('h2').first().innerText()).trim()
  const v = [await page.inputValue('#leave-start'), await page.inputValue('#leave-end'), await page.inputValue('#leave-note')]
  const doctorLocked = await page.locator('#leave-doctor').isDisabled()
  await shot('A10-edit')
  title === 'Modifier le congé' && v.join('|') === `${W.mon}|${W.wed}|Vacances` && doctorLocked
    ? ok('A10', `« ${title} » ${v.join(' · ')}, praticien locked`)
    : bad('A10', 'edit dialog', `title=${title} values=${v} locked=${doctorLocked}`)
})

scenario('A11', async () => {
  await page.fill('#leave-end', W.tue)
  await dialog().getByRole('button', { name: 'Enregistrer' }).click()
  const toast = await pollText(/Congé modifié/)
  const desc = await pollText(/\d+ rendez-vous (sont déjà prévus|est déjà prévu) pendant ce congé\./, 3000)
  await page.waitForTimeout(1500)
  const n = await chips().count()
  const rows = leaveRows()
  toast && n === 2 && rows === `${W.mon}|${W.tue}|Vacances`
    ? ok('A11', `« Congé modifié » (${desc}), 2 ribbons, SQL ${rows}`)
    : bad('A11', 'shrink to Mon–Tue', `toast=${toast} desc=${desc} chips=${n} rows=${rows}`)
})

scenario('A12+D6', async () => {
  for (const [w, h] of [[1440, 900], [320, 640]]) {
    await size(w, h)
    await page.goto(`${BASE}/mon-profil`)
    const title = await pollText(/Mes congés/, 15000)
    const line = await pollText(/du 2 novembre au 3 novembre 2026[^\n]*/, 8000)
    const body = await page.evaluate(() => document.body.innerText)
    const trigger = page.locator('button[aria-label^="Modifier ou supprimer le congé"]:visible')
    const hours = await page.evaluate(() => {
      const els = [...document.querySelectorAll('h1,h2,h3,[data-slot="card-title"],div')].filter((e) => e.childElementCount <= 2 && e.offsetParent)
      const y = (re) => { const el = els.find((e) => re.test(e.textContent?.trim() ?? '') && (e.textContent?.length ?? 0) < 60); return el ? el.getBoundingClientRect().top + scrollY : null }
      return { hours: y(/^Mes horaires/), conges: y(/^Mes congés/) }
    })
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
    let menu = ''
    if (await trigger.count()) {
      const tb = await trigger.first().boundingBox()
      await trigger.first().click()
      menu = (await page.locator('[role="menu"]:visible').innerText().catch(() => '')).replace(/\s+/g, ' ')
      await page.keyboard.press('Escape')
      menu += ` (trigger ${Math.round(tb.width)}×${Math.round(tb.height)})`
    }
    await page.locator('text=Mes congés').first().scrollIntoViewIfNeeded().catch(() => {})
    await shot(`A12-${w}`)
    title && line && /2 jours/.test(body) && /Vacances/.test(line + body) && /Modifier le congé/.test(menu) && /Supprimer le congé/.test(menu) && overflow <= 0
      ? ok('A12', `${w}: « ${line.trim()} », menu « ${menu} »`)
      : bad('A12', `${w} Mon profil card`, `title=${title} line=${line} menu=${menu} overflow=${overflow}`)
    if (w === 1440) {
      hours.hours !== null && hours.conges !== null && hours.hours < hours.conges
        ? ok('D6', `« Mes horaires » (y ${Math.round(hours.hours)}) above « Mes congés » (y ${Math.round(hours.conges)})`)
        : bad('D6', 'card order', JSON.stringify(hours))
    }
  }
  await size(1440, 900)
})

scenario('A13', async () => {
  await page.goto(`${BASE}/settings`)
  await pollText(/Congés de ce praticien/, 20000)
  const summaries = page.locator('summary', { hasText: 'Congés de ce praticien' })
  const n = await summaries.count()
  // The doctor's own card: walk up from each summary to the first ancestor naming a practitioner.
  const index = await page.evaluate((name) => {
    const list = [...document.querySelectorAll('summary')].filter((s) => s.textContent.includes('Congés de ce praticien'))
    return list.findIndex((s) => {
      let el = s.parentElement
      while (el && !/Salma Ben Youssef|Nadia Trabelsi/.test(el.textContent)) el = el.parentElement
      return el ? el.textContent.includes(name) && !el.textContent.includes('Nadia Trabelsi') : false
    })
  }, SALMA_NAME)
  let opened = false, text = ''
  if (index >= 0) {
    await summaries.nth(index).click()
    await page.waitForTimeout(1500)
    text = (await summaries.nth(index).locator('xpath=..').innerText()).replace(/\s+/g, ' ')
    opened = true
    await summaries.nth(index).scrollIntoViewIfNeeded()
  }
  await shot('A13-settings')
  opened && /du 2 novembre au 3 novembre 2026/.test(text) && /Ajouter un congé/.test(text)
    ? ok('A13', `« ${text.slice(0, 140)} »`)
    : bad('A13', 'settings congés', `summaries=${n} opened=${opened} text=« ${text.slice(0, 200)} »`)
})

// ── Tier B (browser) ─────────────────────────────────────────────────────────────────────────────────────────
scenario('B1', async () => {
  await agenda(`date=${W.thu}&view=week`)
  await congeBtn().click()
  await dialog().waitFor()
  await page.fill('#leave-start', W.wed)
  await page.fill('#leave-end', W.mon)
  await dialog().getByRole('button', { name: 'Enregistrer' }).click()
  await page.waitForTimeout(600)
  const err = await page.locator('#leave-range-error').innerText().catch(() => null)
  const stillOpen = await page.locator('#leave-start').count()
  const native = await page.evaluate(() => { const el = document.querySelector('#leave-end'); return { value: el.value, min: el.min, underflow: el.validity.rangeUnderflow, message: el.validationMessage } })
  const rows = leaveRows()
  await shot('B1-range')
  err === 'Le dernier jour doit être le même jour ou après le premier.' && stillOpen && rows === `${W.mon}|${W.tue}|Vacances`
    ? ok('B1', `field error « ${err} », dialog open, nothing written`)
    : bad('B1', 'inverted range', `err=${err} open=${stillOpen} rows=${rows} native=${JSON.stringify(native)}`)
})

scenario('B7', async () => {
  await page.fill('#leave-note', 'test brouillon')
  await dialog().getByRole('button', { name: 'Annuler' }).click()
  const ad = alertDialog()
  const t = await ad.innerText().catch(() => '')
  await shot('B7-discard')
  if (/Abandonner les modifications/.test(t)) {
    ok('B7', `discard confirm « ${t.split('\n')[0]} »`)
    await ad.getByRole('button').filter({ hasNotText: 'Continuer la saisie' }).first().click()
  } else bad('B7', 'no discard confirm', t)
  await page.waitForTimeout(400)
  if (await page.locator('#leave-start').count()) await page.keyboard.press('Escape')
})

// ── Tier C ───────────────────────────────────────────────────────────────────────────────────────────────────
scenario('C1', async () => {
  await size(320, 640)
  await agenda(`date=${W.thu}&view=day`)
  await congeBtn().first().click()
  await dialog().waitFor()
  await page.waitForTimeout(500)
  const box = await dialog().boundingBox()
  const mobile = await dialog().getAttribute('data-mobile')
  const save = await dialog().getByRole('button', { name: 'Enregistrer' }).boundingBox()
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
  await shot('C1-320')
  mobile === 'bottom' && Math.abs(box.y + box.height - 640) <= 2 && save && save.y >= 0 && save.y + save.height <= 640 && save.x >= 0 && save.x + save.width <= 320 && overflow <= 0
    ? ok('C1', `bottom sheet ${Math.round(box.height)} px tall, « Enregistrer » inside, no overflow`)
    : bad('C1', '320 sheet', `mobile=${mobile} box=${JSON.stringify(box)} save=${JSON.stringify(save)} overflow=${overflow}`)
  await dialog().getByRole('button', { name: 'Annuler' }).click()
  await page.waitForTimeout(300)
})

scenario('C2', async () => {
  await size(1536, 730)
  await agenda(`date=${W.thu}&view=week`)
  await congeBtn().click()
  await dialog().waitFor()
  await page.waitForTimeout(400)
  const save = await dialog().getByRole('button', { name: 'Enregistrer' }).boundingBox()
  const scroll = await dialog().evaluate((el) => el.scrollHeight - el.clientHeight)
  await shot('C2-1536x730')
  save && save.y + save.height <= 730 && scroll <= 1
    ? ok('C2', `footer at y ${Math.round(save.y)} in 730, no inner scroll`)
    : bad('C2', '730 tall', `save=${JSON.stringify(save)} scroll=${scroll}`)
  await dialog().getByRole('button', { name: 'Annuler' }).click()
  await size(1440, 900)
})

scenario('C3', async () => {
  // innerText reports the whole word even when it is painted « C… » (run 2's eye pass) — so measure the clip too.
  for (const [w, h] of [[390, 844], [320, 640]]) {
    await size(w, h)
    await agenda(`date=${W.mon}&view=week`)
    const n = await chips().count()
    const labels = n ? await chips().allInnerTexts() : []
    const clipped = await page.evaluate(() => [...document.querySelectorAll('button[aria-label^="Congé de"]')].filter((b) => b.offsetParent)
      .map((b) => { const s = b.querySelector('span'); return s.scrollWidth - s.clientWidth }))
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
    await shot(`C3-${w}-week`)
    n === 2 && labels.every((l) => /^congé$/i.test(l.trim())) && clipped.every((c) => c <= 0) && overflow <= 0
      ? ok('C3', `${w}: 2 compact « ${labels[0].trim()} » pills, word not clipped, no page overflow`)
      : bad('C3', `${w} phone week`, `chips=${n} labels=${JSON.stringify(labels)} clipped=${JSON.stringify(clipped)} overflow=${overflow}`)
  }
})

scenario('C4', async () => {
  await agenda(`date=${W.mon}&view=day`)
  const n = await chips().count()
  const label = n ? (await chips().first().innerText()).trim() : ''
  await shot('C4-390-day')
  n === 1 && label === `Congé — ${SALMA_NAME}` ? ok('C4', `ribbon « ${label} » in phone Jour`) : bad('C4', 'phone day ribbon', `chips=${n} label=${label}`)
})

scenario('C5', async () => {
  await agenda(`date=${W.mon}&view=month`)
  const res = await page.evaluate(() => {
    const out = []
    for (const b of document.querySelectorAll('button[aria-label*="en congé"]')) {
      const cell = b.parentElement
      const cb = cell.getBoundingClientRect()
      const clipped = [...cell.children].filter((c) => c !== b).some((c) => c.getBoundingClientRect().bottom > cb.bottom + 0.5)
      out.push({ label: b.getAttribute('aria-label'), marker: cell.innerText.includes('congé'), clipped })
    }
    return out
  })
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
  await shot('C5-390-month')
  const okCells = res.length === 2 && res.every((r) => r.marker && !r.clipped && /en congé : Salma Ben Youssef/.test(r.label))
  okCells && overflow <= 0 ? ok('C5', `2 cells marked « congé », named in aria-label, nothing clipped`) : bad('C5', 'phone month', `${JSON.stringify(res)} overflow=${overflow}`)
  await size(1440, 900)
})

// ── Tier D ───────────────────────────────────────────────────────────────────────────────────────────────────
scenario('D1', async () => {
  await agenda(`date=${W.mon}&view=week`)
  const cell = page.locator(`[data-agenda-day="${W.mon}"][data-agenda-hour="15"]`).first()
  await cell.scrollIntoViewIfNeeded()
  await cell.click()
  await dialog().waitFor()
  await page.click('#patient')
  await page.locator('[cmdk-input]:visible').fill('QAMain')
  await page.waitForTimeout(800)
  await page.locator('[cmdk-item]:visible').first().click()
  await page.click('#doctor')
  await page.getByRole('option', { name: new RegExp(SALMA_NAME) }).click()
  await page.waitForTimeout(300)
  const submit = dialog().getByRole('button', { name: /^(Créer|Enregistrer|Créer le rendez-vous)/ }).last()
  await submit.click()
  const ad = alertDialog()
  await ad.waitFor({ timeout: 10000 }).catch(() => {})
  const t = (await ad.innerText().catch(() => '')).replace(/\n+/g, ' ⏎ ')
  await shot('D1-confirm')
  ;/est en congé du 02\/11 au 03\/11/.test(t) && !/heures d.ouverture/.test(t) ? ok('D1', `confirm shown: « ${t} »`) : bad('D1', 'congé confirm on create', t)
  if (t) await ad.getByRole('button', { name: 'Annuler' }).click().catch(() => {})
  await page.keyboard.press('Escape')
  await page.waitForTimeout(400)
  const discard = alertDialog()
  if (await discard.count()) await discard.getByRole('button').filter({ hasNotText: 'Continuer la saisie' }).first().click().catch(() => {})
  const leaked = sql(`select count(*) from "Appointments" where "DoctorId"='${SALMA}' and "AppointmentDateTime"='2026-11-02 14:00+00'`)
  leaked === '0' ? ok('D1', 'nothing booked after « Annuler »') : bad('D1', 'a visit was booked', leaked)
})

scenario('D2', async () => {
  await agenda(`date=${W.mon}&view=week`)
  const head = await weekHeaderText(W.sun)
  const hatch = await hatched(W.sun, 10)
  head && /fermé/.test(head.split(' || ')[0]) && hatch === true ? ok('D2', `Sunday « ${head.split(' || ')[0]} », hatched`) : bad('D2', 'closed Sunday', `head=${head} hatch=${hatch}`)
})

scenario('D3', async () => {
  // At 900 px the 09–17 grid fits without scrolling; a shorter window makes the scroller real.
  await size(1440, 560)
  await agenda(`date=${W.mon}&view=week`)
  const scroller = await page.evaluate(() => {
    const cell = document.querySelector('[data-agenda-day]')
    let el = cell
    while (el && !(el.scrollHeight > el.clientHeight + 10 && /auto|scroll/.test(getComputedStyle(el).overflowY))) el = el.parentElement
    if (!el) return null
    const chip = [...document.querySelectorAll('button[aria-label^="Congé de"]')].find((b) => b.offsetParent)
    const before = chip?.getBoundingClientRect().top
    el.scrollTop = 600
    return { before, scrolled: el.scrollTop, top: el.getBoundingClientRect().top }
  })
  await page.waitForTimeout(300)
  const after = await page.evaluate(() => [...document.querySelectorAll('button[aria-label^="Congé de"]')].find((b) => b.offsetParent)?.getBoundingClientRect().top)
  await shot('D3-scrolled')
  scroller && scroller.scrolled > 0 && after !== undefined && Math.abs(after - scroller.before) < 2
    ? ok('D3', `scrolled ${scroller.scrolled} px, ribbon stayed at y ${Math.round(after)}`)
    : bad('D3', 'sticky header', `${JSON.stringify(scroller)} after=${after}`)
  await size(1440, 900)
})

// Run 1's F1: the agenda bar fits its card at every desktop width, and the week's title keeps room.
scenario('F1-bar', async () => {
  const measure = () => page.evaluate(() => {
    const btn = [...document.querySelectorAll('button[aria-label="Ajouter un congé"]')].find((x) => x.offsetParent)
    const bar = btn.parentElement
    const title = [...bar.children].find((e) => /truncate/.test(e.className) && /\d/.test(e.textContent))
    const google = [...bar.querySelectorAll('button')].find((x) => /Google/.test(x.getAttribute('aria-label') ?? ''))
    return { over: bar.scrollWidth - bar.clientWidth, title: Math.round(title.getBoundingClientRect().width), need: title.scrollWidth,
      conge: btn.innerText.trim(), google: google ? google.innerText.trim() : null, main: document.querySelector('main').scrollWidth - document.querySelector('main').clientWidth }
  })
  for (const w of [768, 820, 1024, 1280, 1366, 1440, 1536, 1920]) {
    await size(w, 900)
    for (const q of [`date=${W.mon}&view=week`, `date=${W.mon}&view=week&doctorId=${SALMA}`]) {
      await agenda(q)
      const m = await measure()
      // The title before « Congé » existed (bar-probe, run 1): the feature may not take room from it.
      // 768–1024: run 1's review found the bar spilling there; no title baseline was measured, so only « fits » is asserted.
      const BASELINE = { 768: [0, 0], 820: [0, 0], 1024: [0, 0], 1280: [0, 0], 1366: [41, 17], 1440: [115, 91], 1536: [211, 187], 1920: [379, 355] }
      const minTitle = BASELINE[w][q.includes('doctorId') ? 1 : 0]
      const tag = `${w}${q.includes('doctorId') ? ' filtré' : ''}`
      m.over <= 0 && m.main <= 0 && m.title >= Math.min(minTitle, m.need)
        ? ok('F1-bar', `${tag}: fits, title ${m.title}/${m.need} px, Congé « ${m.conge || 'icône'} », Google « ${m.google || 'icône'} »`)
        : bad('F1-bar', tag, JSON.stringify(m))
      if (w === 1366 && !q.includes('doctorId')) await shot('F1-1366', { clip: { x: 0, y: 0, width: 1366, height: 200 } })
      if (w === 1024 && !q.includes('doctorId')) await shot('F1-1024', { clip: { x: 0, y: 0, width: 1024, height: 200 } })
      if (w === 768 && q.includes('doctorId')) await shot('F1-768-filtre', { clip: { x: 0, y: 0, width: 768, height: 220 } })
      if (w === 1536 && !q.includes('doctorId')) await shot('F1-1536', { clip: { x: 0, y: 0, width: 1536, height: 200 } })
    }
  }
  await size(1440, 900)
})

// Review fixes (feature-review.md): each row proves one fix in the browser.
scenario('RV-blocks', async () => {
  // Paging onto a congé week adds the ribbon after the first load; blocks must stay on their hour.
  await size(1440, 900)
  await agenda(`date=2026-10-26&view=week`)
  const leaves = page.waitForResponse((r) => r.url().includes('/doctors/leaves') && r.url().includes('2026-11-02'), { timeout: 20000 }).catch(() => null)
  await page.locator('button[aria-label="Période suivante"]:visible').click()
  await leaves
  await page.waitForTimeout(1500)
  const offset = await page.evaluate(() => {
    const name = [...document.querySelectorAll('*')].find((e) => e.childElementCount === 0 && e.textContent?.trim() === 'Mehdi Bouazizi' && e.offsetParent)
    let block = name
    while (block && !(block.style && block.style.top)) block = block.parentElement
    const cell = document.querySelector('[data-agenda-day="2026-11-02"][data-agenda-hour="10"]')
    return block && cell ? Math.round(block.getBoundingClientRect().top - cell.getBoundingClientRect().top) : null
  })
  const ribbon = await chips().count()
  await shot('RV-blocks-paged')
  ribbon > 0 && offset !== null && Math.abs(offset) <= 2
    ? ok('RV-blocks', `paged onto the congé week: ribbon shown, 10:00 block ${offset} px from its hour line`)
    : bad('RV-blocks', 'block misplaced after paging', `ribbon=${ribbon} offset=${offset}`)
})

scenario('RV-leaves-failed', async () => {
  await page.route('**/doctors/leaves?**', (r) => r.fulfill({ status: 500, contentType: 'application/json', body: '{"error":"boom"}' }))
  await page.goto(`${BASE}/appointments?date=${W.mon}&view=week`)
  const banner = await pollText(/Les congés des praticiens n.ont pas pu être chargés/, 15000)
  await shot('RV-leaves-failed')
  await page.unroute('**/doctors/leaves?**')
  banner ? ok('RV-leaves-failed', `banner « ${banner} »`) : bad('RV-leaves-failed', 'no banner when the congé read fails')
})

scenario('RV-strip', async () => {
  await size(390, 844)
  await agenda(`date=${W.wed}&view=day&doctorId=${SALMA}`)
  const labels = await page.evaluate(() => [...document.querySelectorAll('button[aria-label]')].filter((b) => b.offsetParent && /^(lundi|mardi|mercredi) [234] novembre/.test(b.getAttribute('aria-label'))).map((b) => b.getAttribute('aria-label')))
  await shot('RV-strip-390')
  const mon = labels.find((l) => l.startsWith('lundi')), tue = labels.find((l) => l.startsWith('mardi')), wed = labels.find((l) => l.startsWith('mercredi'))
  ;/praticien en congé/.test(mon ?? '') && /praticien en congé/.test(tue ?? '') && !/congé|fermé/.test(wed ?? '')
    ? ok('RV-strip', 'Jour on Wed: Mon and Tue read « praticien en congé », Wed open')
    : bad('RV-strip', 'phone strip', JSON.stringify(labels))
  await size(1440, 900)
})

scenario('RV-mois', async () => {
  await size(1536, 730)
  await agenda(`date=${W.mon}&view=month`)
  const res = await page.evaluate(() => [...document.querySelectorAll('button[aria-label*="en congé"]')].filter((b) => b.offsetParent).map((b) => {
    const cell = b.parentElement, cb = cell.getBoundingClientRect()
    const clipped = [...cell.querySelectorAll('*')].filter((c) => c !== b && c.offsetParent).some((c) => c.getBoundingClientRect().bottom > cb.bottom + 0.5 && c.getBoundingClientRect().top < cb.bottom)
    return { label: b.getAttribute('aria-label'), marker: cell.innerText.includes('Congé — Salma Ben Youssef'), clipped }
  }))
  await shot('RV-mois-1536x730')
  res.length === 2 && res.every((r) => r.marker && !r.clipped)
    ? ok('RV-mois', 'desktop Mois: 2 congé days labelled, named in the cell, nothing half-drawn')
    : bad('RV-mois', 'desktop Mois', JSON.stringify(res))
  await size(1440, 900)
})

scenario('RV-profil', async () => {
  // A congé saved elsewhere must not reload « Mon profil » under somebody typing.
  await page.goto(`${BASE}/mon-profil`)
  await page.waitForSelector('#ordre', { timeout: 20000 })
  await page.fill('#ordre', 'QA-TYPED-123')
  const other = await ctx.newPage()
  await other.goto(`${BASE}/appointments?date=2026-11-09&view=week`)
  await other.locator('button[aria-label="Ajouter un congé"]:visible').click()
  await other.locator('[role="dialog"]').waitFor()
  await other.click('#leave-doctor')
  await other.getByRole('option', { name: /Nadia/ }).click()
  await other.fill('#leave-end', '2026-11-10')
  await other.locator('[role="dialog"]').getByRole('button', { name: 'Enregistrer' }).click()
  await other.waitForSelector('#leave-start', { state: 'detached', timeout: 10000 }).catch(() => {})
  await page.waitForTimeout(3000)
  const value = await page.inputValue('#ordre').catch(() => null)
  const reloaded = await page.locator('text=Chargement du profil').count()
  await other.close()
  const created = sql(`select count(*) from "DoctorLeaves" where "StartDate"='2026-11-09'`)
  created === '1' && value === 'QA-TYPED-123' && reloaded === 0
    ? ok('RV-profil', 'congé saved in another tab; Mon profil kept the typed ordre, no reload')
    : bad('RV-profil', 'Mon profil reloaded', `created=${created} value=${value} loader=${reloaded}`)
  sql(`delete from "DoctorLeaves" where "StartDate"='2026-11-09'`)
})

scenario('RV-all-leaves', async () => {
  // Owner's call on review #13: reception's list of every congé, from the agenda « ⋯ » (and the phone panel).
  await size(1440, 900)
  await agenda(`date=${W.mon}&view=week`)
  await page.locator('button[aria-label^="Autres actions de l"]:visible').click()
  await page.getByRole('button', { name: 'Tous les congés' }).click()
  const d = page.locator('[role="dialog"]').filter({ hasText: 'Congés des praticiens' })
  await d.waitFor()
  const listed = await pollText(/du 2 novembre au 3 novembre 2026/, 10000)
  const sections = await d.locator('section').count()
  await shot('RV-all-leaves-1440')
  await page.keyboard.press('Escape')
  await size(390, 844)
  await agenda(`date=${W.mon}&view=week`)
  await page.locator('button[aria-expanded]:visible', { hasText: /nov/ }).first().click()
  await page.getByRole('button', { name: 'Tous les congés' }).click()
  await page.locator('[role="dialog"]').filter({ hasText: 'Congés des praticiens' }).waitFor()
  const phone = await pollText(/du 2 novembre au 3 novembre 2026/, 10000)
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
  await shot('RV-all-leaves-390')
  await page.keyboard.press('Escape')
  await size(1440, 900)
  listed && sections >= 2 && phone && overflow <= 0
    ? ok('RV-all-leaves', `« Tous les congés »: ${sections} practitioners listed, Salma's congé shown at 1440 and 390`)
    : bad('RV-all-leaves', 'list of every congé', `listed=${listed} sections=${sections} phone=${phone} overflow=${overflow}`)
})

scenario('A14', async () => {
  await agenda(`date=${W.mon}&view=week`)
  await chips().first().click()
  await dialog().waitFor()
  await dialog().getByRole('button', { name: 'Supprimer' }).click()
  const ad = alertDialog()
  await ad.waitFor()
  const title = (await ad.locator('h2').innerText()).trim()
  await shot('A14-confirm')
  await ad.getByRole('button', { name: 'Supprimer le congé' }).click()
  const toast = await pollText(/Congé supprimé/)
  await page.waitForTimeout(1200)
  const n = await chips().count()
  const rows = leaveRows()
  title === `Supprimer le congé de ${SALMA_NAME} du 2 novembre au 3 novembre 2026 ?` && toast && n === 0 && rows === ''
    ? ok('A14', `« ${title} » → deleted, ribbons gone, SQL empty`)
    : bad('A14', 'delete', `title=« ${title} » toast=${toast} chips=${n} rows=${rows}`)
})

// ── Run ──────────────────────────────────────────────────────────────────────────────────────────────────────
try { await signIn(); console.log('signed in') } catch (e) { console.log('SIGN-IN FAILED', e.message); await shot('sign-in-failed'); process.exit(1) }
for (const s of S) {
  try { await s.run() } catch (e) { bad(s.id, 'threw', e.message.split('\n')[0]); await shot(`${s.id}-threw`).catch(() => {}) }
}
if (consoleErrors.length) console.log('PAGE ERRORS', consoleErrors.slice(0, 5))
console.log(findings.length ? `\n${findings.length} FINDING(S)` : '\nALL CLEAR')
findings.forEach((f) => console.log(JSON.stringify(f)))
await browser.close()
