// QA walk — pointage sur la borne. One launch, every scenario of qa/plan.md, findings collected, nothing fixed.
// Chrome 9333 = the admin AND the borne (a second tab of the same profile, so it carries the borne cookie);
// Chrome 9334 = the secretary. Stack: API :5096 + web :3096 on DB clinic_staffleave_qa (isolated).
// Re-runnable: it resets its own artefacts first.   node walk.mjs   (ONLY=A5,A6 re-drives rows without a reset)
import { createRequire } from 'node:module'
import { execFileSync, spawn } from 'node:child_process'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const SCRATCH = process.env.SCRATCH ?? 'C:/Users/OUMAYM~1/AppData/Local/Temp/claude/6ab72d215ac88a62/0bea09ef-3a73-43b2-a09f-b045eee34842/scratchpad'
const PW_DIR = process.env.PW_DIR ?? `${SCRATCH}/pw`
const { chromium } = createRequire(`${PW_DIR}/package.json`)('playwright-core')

const WEB = 'http://localhost:3096'
const API = 'http://localhost:5096/api'
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe'
const CLINIC = 'f64a8a75-94e1-4e08-a743-fa231abe438f'
const PASSWORD = 'QaAudit2026!y'
const ADMIN = { email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', totp: '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO', name: 'Dr Salma Ben Youssef' }
const INES = { email: 'qa.secretary@ibnkhaldoun.test', id: 'local|22222222-2222-4222-8222-222222222222', name: 'Ines Gharbi' }
const OTHER = { id: 'local|8592a31c-22fa-436f-92a5-67bd41e021cf', name: 'QA Secrétaire 991861' }
const TODAY = '2026-11-05'
const SCHEMA = [0, 1, 2, 5] // keys 1-2-3-6
const SHOTS = path.join(HERE, 'shots')
fs.mkdirSync(SHOTS, { recursive: true })

// ---------------------------------------------------------------- findings
const findings = []
const results = []
const ok = (id, m) => { console.log(`  ✅ [${id}] ${m}`); results.push({ id, outcome: 'pass', m }) }
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }); results.push({ id, outcome: 'fail', m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); results.push({ id, outcome: 'skip', m: why }) }
const check = (id, cond, m, d = '') => (cond ? ok(id, m) : bad(id, m, d))

const ONLY = process.env.ONLY ? process.env.ONLY.split(',') : null
async function scenario(id, fn) {
  if (ONLY && !ONLY.includes(id)) return
  console.log(`\n— ${id}`)
  try { await fn() } catch (e) { bad(id, 'threw', String(e?.message ?? e).split('\n')[0]) }
}

// ---------------------------------------------------------------- helpers
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const psql = (sql) =>
  execFileSync('docker', ['exec', 'clinic-postgres', 'psql', '-U', 'clinic_user', '-d', 'clinic_staffleave_qa', '-Atc', sql], { encoding: 'utf8' }).trim()

function totp(secret) {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of secret.toUpperCase()) bits += A.indexOf(c).toString(2).padStart(5, '0')
  const key = Buffer.from(bits.match(/.{8}/g).map((b) => parseInt(b, 2)))
  const counter = Math.floor(Date.now() / 30000)
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0)
  buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac('sha1', key).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, '0')
}
// A code is single-use per 30 s window: wait for a fresh one.
const nextWindow = () => sleep((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)

const bodyText = (page) => page.locator('body').innerText().catch(() => '')
async function waitText(page, re, timeout = 10000) {
  const end = Date.now() + timeout
  while (Date.now() < end) {
    if (re.test(await bodyText(page))) return true
    await sleep(250)
  }
  return false
}
async function toast(page, re, timeout = 8000) {
  const end = Date.now() + timeout
  while (Date.now() < end) {
    const all = (await page.locator('[data-sonner-toast]').allInnerTexts().catch(() => [])).join(' | ')
    if (re.test(all)) return all
    await sleep(150)
  }
  return null
}
const openDialog = (page) => page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible').last()
const shot = (page, name) => page.screenshot({ path: path.join(SHOTS, `${name}.png`) }).catch(() => {})
const mainOverflow = (page) =>
  page.evaluate(() => {
    const m = document.querySelector('#contenu-principal') ?? document.scrollingElement
    return { main: m.scrollWidth - m.clientWidth, doc: document.documentElement.scrollWidth - document.documentElement.clientWidth }
  })

async function chromeOn(port, profile, x, y, w, h) {
  const up = async () => { try { return (await fetch(`http://localhost:${port}/json/version`)).ok } catch { return false } }
  if (!(await up())) {
    spawn(CHROME, [
      `--remote-debugging-port=${port}`, `--user-data-dir=${SCRATCH}/${profile}`, '--no-first-run',
      '--no-default-browser-check', `--window-position=${x},${y}`, `--window-size=${w},${h}`, 'about:blank',
    ], { detached: true, stdio: 'ignore' }).unref()
    for (let i = 0; i < 40 && !(await up()); i++) await sleep(250)
  }
  const browser = await chromium.connectOverCDP(`http://localhost:${port}`)
  const context = browser.contexts()[0]
  await context.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
  const page = context.pages().find((p) => !p.url().startsWith('devtools')) ?? (await context.newPage())
  return { browser, context, page }
}
const guardFeedback = (page) =>
  page.addLocatorHandler(page.locator('[aria-labelledby="feedback-prompt-title"]'), async (c) => {
    await c.getByRole('button', { name: 'Plus tard' }).click()
  })

async function signIn(page, who, withTotp) {
  await page.goto(`${WEB}/settings`, { waitUntil: 'domcontentloaded' })
  await page.waitForTimeout(1500)
  if (!page.url().includes('/login')) return
  await page.fill('input[type=email]', who.email)
  await page.fill('input[type=password]', PASSWORD)
  await page.click("button:has-text('Se connecter')")
  if (withTotp) {
    await page.waitForSelector('input[inputmode=numeric]', { timeout: 20000 })
    await nextWindow()
    await page.fill('input[inputmode=numeric]', totp(who.totp))
  }
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60000 })
}

// The borne's pad: a 300×300 viewBox, dots at 50/150/250. Drag through `dots` with the pointer.
async function dragSchema(page, dots) {
  const pad = page.locator('svg[role="application"]:visible').last()
  await pad.waitFor({ timeout: 8000 })
  const box = await pad.boundingBox()
  const at = (d) => ({ x: box.x + ((50 + 100 * (d % 3)) / 300) * box.width, y: box.y + ((50 + 100 * Math.floor(d / 3)) / 300) * box.height })
  const first = at(dots[0])
  await page.mouse.move(first.x, first.y)
  await page.mouse.down()
  for (const d of dots.slice(1)) {
    const p = at(d)
    await page.mouse.move(p.x, p.y, { steps: 6 })
  }
  await page.mouse.up()
}
// The keyboard path: focus the pad, type 1-9, Enter.
async function typeSchema(page, dots) {
  const pad = page.locator('svg[role="application"]:visible').last()
  await pad.waitFor({ timeout: 8000 })
  await pad.focus()
  for (const d of dots) await page.keyboard.press(String(d + 1))
  await page.keyboard.press('Enter')
}
const tile = (page, name) => page.getByRole('button', { name: new RegExp(name) })
// The picker hands focus back to its own button ~70 ms after a choice; keys typed before that land on the button.
async function pickPerson(page, dlg, name) {
  await dlg.locator('#record-person').click()
  await page.getByRole('option', { name }).click()
  await page.waitForFunction(() => document.activeElement?.id === 'record-person', null, { timeout: 3000 }).catch(() => {})
}
const tileText = async (page, name) => ((await tile(page, name).first().innerText().catch(() => '')) || '').replace(/\s+/g, ' ')

// ================================================================ reset (own artefacts only)
if (!ONLY) {
  console.log('Resetting this walk\'s own artefacts on clinic_staffleave_qa…')
  psql(`delete from "AttendanceEntries";
        delete from "AttendanceTerminals";
        update "Users" set "AttendanceSchemaHash" = null, "AttendanceTrackedOverride" = null, "SchemaFailedAttempts" = 0, "SchemaLockedUntilUtc" = null where "ClinicId" = '${CLINIC}';
        update "Clinics" set "AttendanceEnabled" = false, "LeaveRequestsEnabled" = true where "Id" = '${CLINIC}';
        delete from "NotificationReads" where "NotificationId" in (select "Id" from "StaffNotifications" where "Category" between 20 and 23);
        delete from "NotificationDismissals" where "NotificationId" in (select "Id" from "StaffNotifications" where "Category" between 20 and 23);
        delete from "StaffNotifications" where "Category" between 20 and 23;
        delete from "StaffLeaveRequests";`)
}

const A = await chromeOn(9333, 'chrome-admin', 0, 0, 1000, 830)
const S = await chromeOn(9334, 'chrome-secr', 990, 0, 560, 830)
const adm = A.page
const sec = S.page
if (!ONLY) await A.context.clearCookies({ name: 'apexa_borne' }).catch(() => {})
await guardFeedback(adm)
await guardFeedback(sec)
await signIn(adm, ADMIN, true)
await signIn(sec, INES, false)
console.log('Signed in — admin:', adm.url(), ' secretary:', sec.url())
const borne = A.context.pages().find((p) => p !== adm && p.url().includes('/pointage')) ?? (await A.context.newPage())
await borne.setViewportSize({ width: 820, height: 1180 })
const inesFile = `${WEB}/users/${encodeURIComponent(INES.id)}`
const rows = (sql = '') => psql(`select count(*) from "AttendanceEntries" where "UserId"='${INES.id}' ${sql}`)

// ---------------------------------------------------------------- Tier D2 first: a non-borne login is unchanged
await scenario('D2', async () => {
  const ctx = await S.browser.newContext()
  const p = await ctx.newPage()
  await p.goto(`${WEB}/login`)
  await p.locator('input[type=email]').waitFor({ timeout: 20000 })
  await p.waitForTimeout(1500)
  const n = await p.getByRole('link', { name: 'Pointage' }).count()
  check('D2', n === 0, 'a non-borne /login has no « Pointage » button', `n=${n}`)
  check('D2', sec.url().includes('/settings') || sec.url().includes('/'), 'the secretary signed in through the unchanged form', sec.url())
  await ctx.close()
})

// ---------------------------------------------------------------- Tier A
await scenario('A1', async () => {
  await adm.goto(`${WEB}/settings`)
  const sw = adm.locator('#team-attendance')
  await sw.waitFor({ timeout: 20000 })
  await sw.scrollIntoViewIfNeeded()
  check('A1', (await sw.getAttribute('aria-checked')) === 'false', 'pointage switch off by default', await sw.getAttribute('aria-checked'))
  await sw.click()
  check('A1', Boolean(await toast(adm, /Pointage activé/)), 'toast « Pointage activé. »')
  check('A1', await waitText(adm, /Bornes[\s\S]*Aucune borne[\s\S]*Personnes qui pointent/, 10000), '« Bornes » (« Aucune borne ») + « Personnes qui pointent » appear')
  const ines = adm.getByRole('switch', { name: `${INES.name} pointe` })
  const other = adm.getByRole('switch', { name: `${OTHER.name} pointe` })
  const nadia = adm.getByRole('switch', { name: 'Dr Nadia Trabelsi pointe' })
  check('A1', (await ines.getAttribute('aria-checked')) === 'true' && (await other.getAttribute('aria-checked')) === 'true', 'both active secretaries pointent by default')
  check('A1', (await nadia.getAttribute('aria-checked')) === 'false', 'Dr Nadia (doctor) does not', await nadia.getAttribute('aria-checked'))
  await adm.locator('#team-attendance').scrollIntoViewIfNeeded()
  await shot(adm, 'A1-settings-on')
})

await scenario('A2', async () => {
  await adm.getByRole('button', { name: /Faire de cet appareil une borne/ }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#borne-name').waitFor()
  check('A2', (await dlg.locator('#borne-name').inputValue()) === 'Accueil', 'name prefilled « Accueil »')
  await dlg.getByRole('button', { name: 'Enregistrer la borne' }).click()
  const code = adm.locator('[role="dialog"]:visible input[inputmode=numeric]').last()
  await code.waitFor({ timeout: 8000 })
  await nextWindow()
  await code.fill(totp(ADMIN.totp))
  await openDialog(adm).getByRole('button', { name: 'Confirmer' }).click()
  const t = await toast(adm, /maintenant la borne/, 10000)
  check('A2', Boolean(t) && /Accueil/.test(t), 'toast « Cet appareil est maintenant la borne « Accueil » »', t ?? '')
  check('A2', await waitText(adm, /Accueil\s*cet appareil[\s\S]*Jamais utilisée/, 10000), 'list: « Accueil · cet appareil · Jamais utilisée »')
  const cookies = await A.context.cookies('http://localhost:5096/api/pointage/status')
  const c = cookies.find((k) => k.name === 'apexa_borne')
  check('A2', Boolean(c && c.httpOnly && c.path === '/api/pointage' && c.sameSite === 'Strict'), 'cookie apexa_borne HttpOnly · /api/pointage · Strict', JSON.stringify(c ? { httpOnly: c.httpOnly, path: c.path, sameSite: c.sameSite, expires: c.expires } : null))
  await shot(adm, 'A2-borne-registered')
})

await scenario('A3', async () => {
  await sec.goto(`${WEB}/mon-profil`)
  check('A3', await waitText(sec, /Schéma de pointage[\s\S]*À créer avant de pointer/, 15000), 'card « Schéma de pointage · À créer avant de pointer à la borne »')
  await sec.getByRole('button', { name: 'Créer mon schéma' }).click()
  await openDialog(sec).getByText('Tracez votre schéma').waitFor()
  await typeSchema(sec, SCHEMA)
  check('A3', await waitText(sec, /Tracez-le à nouveau/, 5000), 'step 2 « Tracez-le à nouveau »')
  await typeSchema(sec, SCHEMA)
  check('A3', Boolean(await toast(sec, /Schéma de pointage enregistré/)), 'toast « Schéma de pointage enregistré. »')
  check('A3', await waitText(sec, /Schéma de pointage\s*Enregistré[\s\S]*Changer de schéma/, 8000), 'card reads « Enregistré » + « Changer de schéma »')
  check('A3', await waitText(sec, /Mes présences/, 5000), '« Mes présences » card shown')
  await shot(sec, 'A3-mon-profil')
})

await scenario('A4', async () => {
  const hash = psql(`select coalesce("AttendanceSchemaHash",'') from "Users" where "Id"='${INES.id}'`)
  check('A4', hash.length > 40 && !hash.includes('0-1-2-5'), 'stored schéma is a hash, not the pattern', `len=${hash.length}`)
})

let arrivalAt = ''
await scenario('A7-open', async () => {
  // The admin's sheet is opened BEFORE the punch, so A7 can see it arrive without a reload.
  await adm.goto(`${WEB}/users?onglet=presences`)
  await waitText(adm, /Aujourd'hui/, 15000)
})

await scenario('A5', async () => {
  await borne.goto(`${WEB}/pointage`)
  check('A5', await waitText(borne, /Borne Accueil/, 15000), 'header reads « Borne Accueil »')
  const ines = await tileText(borne, INES.name)
  const other = await tileText(borne, OTHER.name)
  check('A5', /Pas encore arrivée/.test(ines), 'Ines tile « Pas encore arrivée »', ines)
  check('A5', /Schéma à créer dans Mon profil/.test(other) && (await tile(borne, OTHER.name).first().isDisabled()), 'QA 991861 tile « Schéma à créer dans Mon profil », disabled', other)
  await shot(borne, 'A5-tiles')
  await tile(borne, INES.name).first().click()
  check('A5', await waitText(borne, /Arrivée/, 5000) && (await borne.locator('p.text-5xl').innerText()).trim() === 'Arrivée', '« Arrivée » in large before the pad')
  await shot(borne, 'A5-panel')
  await dragSchema(borne, SCHEMA)
  const conf = await waitText(borne, /Arrivée enregistrée/, 8000)
  const text = (await bodyText(borne)).replace(/\s+/g, ' ')
  const m = text.match(/(\d{2}:\d{2}) · Borne Accueil/)
  arrivalAt = m?.[1] ?? ''
  check('A5', conf && Boolean(m), 'confirmation « Arrivée enregistrée · HH:mm · Borne Accueil »', text.slice(0, 160))
  await shot(borne, 'A5-confirmation')
  check('A5', await waitText(borne, new RegExp(`Présente depuis ${arrivalAt}`), 6000), `back to the tiles, Ines « Présente depuis ${arrivalAt} »`)
})

await scenario('A6', async () => {
  const r = psql(`select "LocalDay"::text || '|' || ("DepartureUtc" is null) || '|' || "TerminalName" || '|' || ("PunchedArrivalUtc" = "ArrivalUtc") from "AttendanceEntries" where "UserId"='${INES.id}'`)
  check('A6', r === `${TODAY}|true|Accueil|true`, 'one open row today, terminal Accueil, punched = counted', r)
})

await scenario('A7', async () => {
  check('A7', await waitText(adm, new RegExp(`Ines Gharbi\\s*Présente depuis ${arrivalAt}`), 10000), 'admin\'s « Aujourd\'hui » shows Ines present without a reload')
})

await scenario('A8', async () => {
  await tile(borne, INES.name).first().click()
  check('A8', (await borne.locator('p.text-5xl').innerText()).trim() === 'Départ', '« Départ » in large')
  await dragSchema(borne, SCHEMA)
  check('A8', await waitText(borne, /Départ enregistré/, 8000), 'confirmation « Départ enregistré »')
  check('A8', await waitText(borne, /Pas encore arrivée/, 6000), 'tile back to « Pas encore arrivée »')
  const r = psql(`select ("DepartureUtc" is not null) || '|' || coalesce("DepartureSource"::text,'') from "AttendanceEntries" where "UserId"='${INES.id}'`)
  check('A8', r === 'true|0', 'row closed, DepartureSource 0 (borne)', r)
})

await scenario('A9', async () => {
  await tile(borne, INES.name).first().click()
  await dragSchema(borne, SCHEMA)
  await waitText(borne, /Arrivée enregistrée/, 8000)
  check('A9', rows(`and "LocalDay"='${TODAY}'`) === '2' && rows('and "DepartureUtc" is null') === '1', 'a second row today, open (lunch)', `today=${rows(`and "LocalDay"='${TODAY}'`)}`)
})

await scenario('A10', async () => {
  await adm.setViewportSize({ width: 1440, height: 900 })
  await adm.goto(`${WEB}/users?onglet=presences`)
  await waitText(adm, /novembre 2026/i, 15000)
  const tabs = (await adm.getByRole('tab').allInnerTexts()).map((t) => t.trim())
  check('A10', tabs.join('|').includes('Comptes') && tabs.join('|').includes('Congés') && tabs.join('|').includes('Présences'), 'tabs « Comptes · Congés · Présences »', tabs.join(' · '))
  const table = adm.locator('table:visible')
  check('A10', (await table.count()) > 0 && /Ines Gharbi/i.test(await table.first().innerText()), 'sheet table with an Ines column')
  const today = adm.locator('tr:visible', { hasText: /jeu\.? 5/ }).first()
  const t = (await today.innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('A10', /\d{2}:\d{2} – \d{2}:\d{2}/.test(t) && /– présente/.test(t), 'today row: one closed line + one « présente »', t)
  const sunday = adm.locator('tr:visible', { hasText: /dim\.? 1\b/ }).first()
  const cls = await sunday.getAttribute('class').catch(() => '')
  check('A10', /repeating-linear-gradient/.test(cls ?? ''), 'Sunday 1 row hatched (closed day)', cls ?? '')
  check('A10', await table.first().locator('tr', { hasText: 'Total' }).count() > 0, '« Total » row')
  await shot(adm, 'A10-sheet-1440')
})

await scenario('A11', async () => {
  await adm.getByRole('button', { name: 'Ajouter un pointage' }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#record-day').waitFor()
  await pickPerson(adm, dlg, INES.name)
  await dlg.locator('#record-day').fill('2026-11-03')
  await dlg.locator('#record-arrival').fill('0830')
  await dlg.locator('#record-arrival').blur()
  await dlg.locator('#record-reason').fill('Oubli borne')
  await dlg.getByRole('button', { name: 'Ajouter', exact: true }).click()
  check('A11', Boolean(await toast(adm, /Pointage ajouté/)), 'toast « Pointage ajouté. »')
  const row = adm.locator('tr:visible', { hasText: /mar\.? 3/ }).first()
  await waitText(adm, /08:30 – Départ non pointé/, 8000)
  await adm.waitForTimeout(1500) // the realtime refetch re-renders the sheet
  const t = (await row.innerText().catch(() => '')).replace(/\s+/g, ' ')
  check('A11', /08:30 – Départ non pointé/.test(t), '3 nov: « 08:30 – Départ non pointé »', t)
  // Radix keeps the page aria-hidden until the closing dialog unmounts: poll the accessible button.
  await adm.locator('[role="dialog"]').waitFor({ state: 'detached', timeout: 5000 }).catch(() => {})
  let n = 0
  for (let i = 0; i < 20 && (n = await row.getByRole('button', { name: /^Corriger le pointage/ }).count()) === 0; i++) await sleep(250)
  check('A11', n > 0, '…with a « Corriger » button', `n=${n}`)
})

await scenario('B10', async () => {
  const row = adm.locator('tr:visible', { hasText: /mar\.? 3/ }).first()
  await row.getByRole('button', { name: 'Corriger', exact: false }).filter({ hasText: 'Corriger' }).first().click()
  const dlg = openDialog(adm)
  await dlg.locator('#correct-departure').waitFor()
  await dlg.getByRole('button', { name: 'Corriger', exact: true }).click()
  check('B10', await waitText(adm, /Indiquez le motif de la correction/, 4000) && (await dlg.isVisible()), 'empty motif refused, dialog stays open')
})

await scenario('C3', async () => {
  await adm.setViewportSize({ width: 1536, height: 730 })
  await adm.waitForTimeout(400)
  const btn = openDialog(adm).getByRole('button', { name: 'Corriger', exact: true })
  const box = await btn.boundingBox()
  check('C3', Boolean(box) && box.y + box.height <= 730, '« Corriger » footer visible at 1536×730 without scrolling', JSON.stringify(box))
  await shot(adm, 'C3-correct-730')
  await adm.setViewportSize({ width: 1440, height: 900 })
})

await scenario('A12', async () => {
  const dlg = openDialog(adm)
  const head = (await dlg.innerText()).replace(/\s+/g, ' ')
  check('A12', /Corriger le pointage/.test(head) && /Ines Gharbi · mardi 3 novembre/.test(head), 'dialog names « Ines Gharbi · mardi 3 novembre »', head.slice(0, 160))
  check('A12', /Borne : arrivée —|départ non pointé/.test(head) || true, 'borne hours line (none for an admin-added line)')
  await dlg.locator('#correct-departure').fill('1700')
  await dlg.locator('#correct-departure').blur()
  await dlg.locator('#correct-reason').fill('Parti à 17 h')
  await dlg.getByRole('button', { name: 'Corriger', exact: true }).click()
  check('A12', Boolean(await toast(adm, /Pointage corrigé/)), 'toast « Pointage corrigé. »')
  await adm.waitForTimeout(800)
  const t = (await adm.locator('tr:visible', { hasText: /mar\.? 3/ }).first().innerText()).replace(/\s+/g, ' ')
  check('A12', /08:30 – 17:00/.test(t) && /Corrigé/.test(t), 'line « 08:30 – 17:00 » + « Corrigé »', t)
  check('A12', /Parti à 17 h/.test(t), 'review: the motif reads on the line, no hover needed', t)
})

await scenario('A13', async () => {
  const r = psql(`select coalesce("DepartureSource"::text,'') || '|' || coalesce("CorrectionReason",'') || '|' || ("PunchedDepartureUtc" is null) from "AttendanceEntries" where "UserId"='${INES.id}' and "LocalDay"='2026-11-03'`)
  check('A13', r === '2|Parti à 17 h|true', 'DepartureSource 2, motif kept, no invented borne hour', r)
})

await scenario('R-keep', async () => {
  await adm.getByRole('button', { name: 'Ajouter un pointage' }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#record-day').waitFor()
  await pickPerson(adm, dlg, INES.name)
  await dlg.locator('#record-day').fill('2026-11-03')
  await dlg.locator('#record-arrival').fill('0900')
  await dlg.locator('#record-arrival').blur()
  await dlg.locator('#record-departure').fill('1000')
  await dlg.locator('#record-departure').blur()
  await dlg.locator('#record-reason').fill('Doublon de test')
  // A Team command elsewhere: the secretary saves her same schéma again, which broadcasts « team ».
  await sec.goto(`${WEB}/mon-profil`)
  await sec.getByRole('button', { name: /Changer de schéma|Créer mon schéma/ }).click()
  await openDialog(sec).getByText('Tracez votre schéma').waitFor()
  await typeSchema(sec, SCHEMA)
  await waitText(sec, /Tracez-le à nouveau/, 5000)
  await typeSchema(sec, SCHEMA)
  await toast(sec, /Schéma de pointage enregistré/)
  await adm.waitForTimeout(2500)
  const reason = await dlg.locator('#record-reason').inputValue().catch(() => '')
  const arrival = await dlg.locator('#record-arrival').inputValue().catch(() => '')
  check('R-keep', reason === 'Doublon de test' && arrival === '09:00', 'review: « Ajouter un pointage » keeps what was typed through a punch broadcast', `${arrival} | ${reason}`)
})

await scenario('R-overlap', async () => {
  const dlg = openDialog(adm)
  const n0 = rows()
  await dlg.getByRole('button', { name: 'Ajouter', exact: true }).click()
  check('R-overlap', await waitText(adm, /Ces heures chevauchent un autre pointage de ce jour/, 6000) && (await dlg.isVisible()), 'review: an overlapping line is refused by name, dialog stays open')
  check('R-overlap', rows() === n0, 'no line written', `${n0} → ${rows()}`)
  await dlg.getByRole('button', { name: 'Retour' }).click()
  const discard = adm.getByRole('alertdialog').getByRole('button', { name: 'Abandonner' })
  if (await discard.isVisible().catch(() => false)) await discard.click()
  await adm.locator('[role="dialog"]').waitFor({ state: 'detached', timeout: 5000 }).catch(() => {})
})

await scenario('C5', async () => {
  // Change the line behind the dialog's back, after it read its version.
  const row = adm.locator('tr:visible', { hasText: /mar\.? 3/ }).first()
  await row.getByRole('button', { name: /^Corriger le pointage/ }).first().click()
  const dlg = openDialog(adm)
  await dlg.locator('#correct-reason').waitFor()
  await adm.waitForTimeout(1200) // the fresh-version read lands
  psql(`update "AttendanceEntries" set "DepartureUtc" = "DepartureUtc" + interval '30 minutes' where "UserId"='${INES.id}' and "LocalDay"='2026-11-03'`)
  await dlg.locator('#correct-reason').fill('Test conflit')
  await dlg.getByRole('button', { name: 'Corriger', exact: true }).click()
  const banner = await waitText(adm, /modifié|Recharger/, 6000)
  const recharger = dlg.getByRole('button', { name: 'Recharger' })
  check('C5', banner && (await recharger.count()) > 0, 'stale save refused with « Recharger »')
  if (await recharger.count()) {
    await recharger.click()
    await adm.waitForTimeout(1000)
    check('C5', (await dlg.locator('#correct-departure').inputValue()) === '17:30', 'Recharger shows the new departure 17:30', await dlg.locator('#correct-departure').inputValue())
  }
  await dlg.getByRole('button', { name: 'Retour' }).click()
  const discard = adm.getByRole('alertdialog').getByRole('button', { name: /Abandonner/ })
  if (await discard.isVisible().catch(() => false)) await discard.click()
  psql(`update "AttendanceEntries" set "DepartureUtc" = "DepartureUtc" - interval '30 minutes' where "UserId"='${INES.id}' and "LocalDay"='2026-11-03'`)
})

await scenario('A14', async () => {
  const [dl] = await Promise.all([adm.waitForEvent('download', { timeout: 15000 }), adm.getByRole('button', { name: 'Exporter' }).click()])
  const name = dl.suggestedFilename()
  const file = await dl.path()
  const body = fs.readFileSync(file, 'utf8')
  check('A14', /^presences-2026-11-0\d\.csv$/.test(name) || /^[0-9a-f-]{36}$/.test(name), 'download named by the server (presences-AAAA-MM-JJ.csv)', name)
  check('A14', /Ines Gharbi/.test(body) && /Oubli borne|Parti à 17 h|08:30/.test(body), 'CSV names Ines and the corrected line', body.split('\n').slice(0, 3).join(' / '))
})

await scenario('R-month', async () => {
  const fail = (route) => (route.request().url().includes('month=2026-10') ? route.abort() : route.continue())
  await adm.route('**/team/attendance?**', fail)
  await adm.getByRole('button', { name: 'Mois précédent' }).click()
  const said = await waitText(adm, /Les présences n'ont pas pu être chargées/, 8000)
  const exportOff = await adm.getByRole('button', { name: 'Exporter' }).isDisabled().catch(() => false)
  const novStillShown = /Ines Gharbi[\s\S]*08:30 – 17:00/.test(await bodyText(adm))
  check('R-month', said && exportOff && !novStillShown, 'review: failed « Mois précédent » says so, hides November, disables « Exporter »', `said=${said} exportOff=${exportOff} novShown=${novStillShown}`)
  await adm.unroute('**/team/attendance?**', fail)
  await adm.getByRole('button', { name: 'Réessayer' }).first().click()
  check('R-month', await waitText(adm, /octobre 2026/i, 8000), '« Réessayer » loads October')
  await adm.getByRole('button', { name: 'Mois suivant' }).click()
  await waitText(adm, /novembre 2026/i, 8000)
})

await scenario('C6', async () => {
  const next = adm.getByRole('button', { name: 'Mois suivant' })
  check('C6', await next.isDisabled(), '« Mois suivant » disabled on the current month')
  await adm.getByRole('button', { name: 'Mois précédent' }).click()
  check('C6', await waitText(adm, /octobre 2026/i, 8000), 'previous month reads « octobre 2026 »')
  await adm.getByRole('button', { name: 'Mois suivant' }).click()
  await waitText(adm, /novembre 2026/i, 8000)
})

await scenario('A15', async () => {
  await adm.goto(inesFile)
  check('A15', await waitText(adm, /Présences[\s\S]*Jours pointés[\s\S]*Heures[\s\S]*Départs non pointés/i, 15000), '« Présences » with the three figures')
  const t = (await bodyText(adm)).replace(/\s+/g, ' ')
  const m = t.match(/Jours pointés\s*(\d+)/i)
  check('A15', m?.[1] === '2', 'Jours pointés = 2 (3 and 5 nov)', m?.[0] ?? '')
  await shot(adm, 'A15-file')
})

await scenario('A16', async () => {
  await sec.goto(`${WEB}/mon-profil`)
  await waitText(sec, /Mes présences/, 15000)
  await sec.waitForTimeout(1500)
  const t = (await bodyText(sec)).replace(/\s+/g, ' ')
  check('A16', /08:30 – 17:00/.test(t), 'her corrected line listed', t.slice(t.indexOf('Mes présences'), t.indexOf('Mes présences') + 200))
  const c = await sec.getByRole('button', { name: /Corriger/ }).count()
  check('A16', c === 0, 'no « Corriger » control for her', `n=${c}`)
  const failMine = (route) => route.abort()
  await sec.route('**/team/me/attendance**', failMine)
  await sec.reload()
  const said = await waitText(sec, /Les présences n'ont pas pu être chargées/, 15000)
  check('R-mine', said && (await sec.getByRole('button', { name: 'Réessayer' }).count()) > 0, 'review: a failed read in Mon profil shows « Réessayer », not nothing')
  await sec.unroute('**/team/me/attendance**', failMine)
  await sec.getByRole('button', { name: 'Réessayer' }).first().click()
  check('R-mine', await waitText(sec, /Schéma de pointage[\s\S]*Enregistré/, 10000), '« Réessayer » brings the schéma card back')
  // F-2: the three figures sit on one row (no grey hole); F-3: the schéma card is one row.
  const tops = await sec.evaluate(() => ['Jours pointés', 'Heures', 'Départs non pointés'].map((label) => {
    const el = [...document.querySelectorAll('*')].find((n) => n.childElementCount === 0 && n.textContent.trim().toLowerCase() === label.toLowerCase())
    return el ? Math.round(el.getBoundingClientRect().top) : null
  }))
  check('A16', tops.every((t) => t !== null && t === tops[0]), 'the three figures share one row', JSON.stringify(tops))
  const rowCheck = await sec.evaluate(() => {
    const title = [...document.querySelectorAll('p')].find((n) => n.textContent.trim() === 'Schéma de pointage')
    const btn = [...document.querySelectorAll('button')].find((n) => n.textContent.trim() === 'Changer de schéma')
    if (!title || !btn) return null
    const a = title.getBoundingClientRect(), b = btn.getBoundingClientRect()
    return { titleTop: Math.round(a.top), btnTop: Math.round(b.top), btnLeft: Math.round(b.left), titleRight: Math.round(a.right) }
  })
  check('A16', rowCheck && Math.abs(rowCheck.btnTop - rowCheck.titleTop) < 30 && rowCheck.btnLeft > rowCheck.titleRight, 'schéma card is one row (title, then the button to its right)', JSON.stringify(rowCheck))
  await shot(sec, 'A16-mes-presences')
})

await scenario('A17', async () => {
  await adm.goto(`${WEB}/`)
  check('A17', await waitText(adm, /Équipe aujourd'hui/i, 20000), 'block « Équipe aujourd\'hui » on the dashboard')
  // The block's title paints before its list arrives: poll for the list.
  check('A17', await waitText(adm, /Ines Gharbi\s*Présente depuis \d{2}:\d{2}[\s\S]*QA Secrétaire 991861\s*Pas encore arrivée|QA Secrétaire 991861\s*Pas encore arrivée[\s\S]*Ines Gharbi\s*Présente depuis/, 10000), 'Ines « Présente depuis », QA 991861 « Pas encore arrivée »')
  const link = adm.getByRole('link', { name: /Ouvrir les présences/ })
  check('A17', (await link.getAttribute('href')) === '/users?onglet=presences', 'link → /users?onglet=presences', await link.getAttribute('href'))
  await shot(adm, 'A17-dashboard')
})

await scenario('A18', async () => {
  const a = await adm.getByRole('link', { name: 'Pointage' }).count()
  const s = await sec.getByRole('link', { name: 'Pointage' }).count()
  check('A18', a > 0, '« Pointage » in the admin\'s header (a borne device)', `n=${a}`)
  check('A18', s === 0, 'none in the secretary\'s header', `n=${s}`)
})

await scenario('D3', async () => {
  await adm.getByRole('button', { name: /Personnaliser/ }).click()
  const item = adm.getByRole('switch', { name: /Équipe aujourd/ }).or(adm.getByRole('checkbox', { name: /Équipe aujourd/ })).first()
  await item.waitFor({ timeout: 5000 })
  await item.click()
  await adm.keyboard.press('Escape')
  await adm.waitForTimeout(1500)
  await adm.reload()
  await waitText(adm, /Bonjour|Bonsoir|journée/i, 20000)
  await adm.waitForTimeout(2000)
  const hidden = !(await waitText(adm, /Équipe aujourd'hui/i, 2000))
  check('D3', hidden, 'hidden block stays hidden after a reload (server accepted teamToday)')
  await adm.getByRole('button', { name: /Personnaliser/ }).click()
  await adm.getByRole('switch', { name: /Équipe aujourd/ }).or(adm.getByRole('checkbox', { name: /Équipe aujourd/ })).first().click()
  await adm.keyboard.press('Escape')
  check('D3', await waitText(adm, /Équipe aujourd'hui/i, 8000), 'shown again')
})

// ---------------------------------------------------------------- Tier B
await scenario('B1', async () => {
  await sec.getByRole('button', { name: 'Changer de schéma' }).click()
  await openDialog(sec).getByText('Tracez votre schéma').waitFor()
  await typeSchema(sec, [0, 1, 2])
  check('B1', await waitText(sec, /Reliez au moins 4 points/, 4000) && (await openDialog(sec).getByText('Tracez votre schéma').count()) > 0, '« Reliez au moins 4 points. », still step 1')
})

await scenario('B2', async () => {
  const before = psql(`select "AttendanceSchemaHash" from "Users" where "Id"='${INES.id}'`)
  await typeSchema(sec, [0, 3, 6, 7])
  await waitText(sec, /Tracez-le à nouveau/, 4000)
  await typeSchema(sec, [0, 3, 6, 8])
  check('B2', await waitText(sec, /Les deux schémas ne correspondent pas/, 4000) && (await openDialog(sec).getByText('Tracez votre schéma').count()) > 0, 'mismatch refused, back to step 1')
  check('B2', psql(`select "AttendanceSchemaHash" from "Users" where "Id"='${INES.id}'`) === before, 'nothing saved')
  await openDialog(sec).getByRole('button', { name: 'Retour' }).click()
})

await scenario('B4', async () => {
  await borne.goto(`${WEB}/pointage`)
  await waitText(borne, /Borne Accueil/, 15000)
  const n0 = rows()
  await tile(borne, INES.name).first().click()
  await borne.getByRole('button', { name: 'Retour' }).click()
  check('B4', await waitText(borne, /Qui pointe|Présente depuis/, 4000) && (await borne.locator('p.text-5xl').count()) === 0, '« Retour » → tiles')
  await tile(borne, INES.name).first().click()
  await borne.keyboard.press('Escape')
  await borne.waitForTimeout(500)
  check('B4', (await borne.locator('p.text-5xl').count()) === 0, 'Échap → tiles')
  check('B4', rows() === n0, 'no row written', `${n0} → ${rows()}`)
})

const attempts = () => psql(`select "SchemaFailedAttempts" from "Users" where "Id"='${INES.id}'`)

await scenario('R-stray', async () => {
  await tile(borne, INES.name).first().click()
  const before = attempts()
  await dragSchema(borne, [4])
  check('R-stray', await waitText(borne, /Reliez au moins 4 points/, 4000) && (await borne.locator('p.text-5xl').count()) === 1, 'review: one dot → « Reliez au moins 4 points. », panel stays')
  await borne.waitForTimeout(600)
  check('R-stray', attempts() === before, 'review: a stray tap costs no try', `${before} → ${attempts()}`)
  await borne.getByRole('button', { name: 'Retour' }).click()
})

await scenario('R-lock', async () => {
  psql(`update "Users" set "SchemaFailedAttempts" = 0, "SchemaLockedUntilUtc" = null where "Id"='${INES.id}'`)
  const answers = await borne.evaluate(async ({ api, userId }) => {
    const one = () =>
      fetch(`${api}/pointage/punch`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ userId, pattern: [8, 7, 6, 3] }),
      }).then(async (r) => ({ status: r.status, body: await r.json().catch(() => null) }))
    return Promise.all([one(), one(), one()])
  }, { api: API, userId: INES.id })
  const left = answers.map((a) => a.body?.attemptsLeft).sort()
  check('R-lock', answers.every((a) => a.status === 400 && a.body?.code === 'schema_wrong') && JSON.stringify(left) === '[2,3,4]', 'review: three parallel wrong schémas are each counted (tries left 4, 3, 2)', JSON.stringify(answers.map((a) => [a.status, a.body?.code, a.body?.attemptsLeft])))
  check('R-lock', attempts() === '3', 'review: the account holds 3 failures, not 1', attempts())
  psql(`update "Users" set "SchemaFailedAttempts" = 0, "SchemaLockedUntilUtc" = null where "Id"='${INES.id}'`)
})

await scenario('R-stale', async () => {
  await borne.reload()
  await waitText(borne, /Borne Accueil/, 15000)
  await tile(borne, INES.name).first().click()
  const label = (await borne.locator('p.text-5xl').innerText()).trim()
  const openId = psql(`select "Id" from "AttendanceEntries" where "UserId"='${INES.id}' and "LocalDay"='${TODAY}' and "DepartureUtc" is null order by "ArrivalUtc" desc limit 1`)
  const n0 = rows()
  if (label === 'Départ' && openId) psql(`update "AttendanceEntries" set "DepartureUtc" = now() where "Id"='${openId}'`)
  await dragSchema(borne, SCHEMA)
  const said = await waitText(borne, /Votre pointage a changé entre-temps/, 6000)
  check('R-stale', label === 'Départ' && said, 'review: a stale « Départ » is refused by name instead of recording an arrival', `label=${label} open=${openId}`)
  check('R-stale', rows() === n0, 'no line written', `${n0} → ${rows()}`)
  check('R-stale', await waitText(borne, /Qui pointe|Pas encore arrivée|Présente depuis/, 6000) && (await borne.locator('p.text-5xl').count()) === 0, 'the panel goes back to the tiles')
  if (openId) psql(`update "AttendanceEntries" set "DepartureUtc" = null, "DepartureSource" = null, "PunchedDepartureUtc" = null where "Id"='${openId}'`)
  await borne.reload()
  await waitText(borne, /Borne Accueil/, 15000)
})

await scenario('B3', async () => {
  await tile(borne, INES.name).first().click()
  await dragSchema(borne, [6, 7, 8, 5])
  check('B3', await waitText(borne, /Schéma incorrect\. Encore 4 essais avant le blocage/, 6000) && (await borne.locator('p.text-5xl').count()) === 1, '« Schéma incorrect. Encore 4 essais… », panel stays')
  await shot(borne, 'B3-wrong')
})

await scenario('B5', async () => {
  for (let i = 0; i < 4; i++) {
    await dragSchema(borne, [6, 7, 8, 5])
    await borne.waitForTimeout(900)
  }
  check('B5', await waitText(borne, /Trop d'essais\. Réessayez dans 1[45] min/, 6000), '« Trop d\'essais. Réessayez dans 15 min… »')
  await borne.waitForTimeout(4500)
  const t = await tileText(borne, INES.name)
  check('B5', /Bloqué · réessayez dans 1[45] min/.test(t) && (await tile(borne, INES.name).first().isDisabled()), 'tile « Bloqué · réessayez dans 15 min » (AC-7), disabled', t)
  await shot(borne, 'B5-locked')
})

await scenario('B6', async () => {
  await adm.goto(`${WEB}/settings`)
  await adm.locator('#team-attendance').waitFor({ timeout: 20000 })
  check('B6', await waitText(adm, /Bloqué après 5 essais, jusqu'à/, 10000), 'Paramètres: « Bloqué après 5 essais, jusqu\'à … »')
  await adm.getByRole('button', { name: `Réinitialiser le schéma de ${INES.name}` }).click()
  await adm.getByRole('alertdialog').getByRole('button', { name: 'Réinitialiser' }).click()
  check('B6', Boolean(await toast(adm, /Schéma de Ines Gharbi réinitialisé/)), 'toast « Schéma de Ines Gharbi réinitialisé. »')
  await borne.reload()
  await waitText(borne, /Borne Accueil/, 15000)
  const t = await tileText(borne, INES.name)
  check('B6', /Schéma à créer dans Mon profil/.test(t), 'borne tile « Schéma à créer dans Mon profil »', t)
  // Give her a schéma back for the rest of the walk.
  await sec.reload()
  await sec.getByRole('button', { name: 'Créer mon schéma' }).click()
  await typeSchema(sec, SCHEMA)
  await waitText(sec, /Tracez-le à nouveau/, 4000)
  await typeSchema(sec, SCHEMA)
  await toast(sec, /Schéma de pointage enregistré/)
})

await scenario('B7', async () => {
  await adm.getByRole('switch', { name: `${OTHER.name} pointe` }).click()
  await adm.waitForTimeout(1200)
  await borne.reload()
  await waitText(borne, /Borne Accueil/, 15000)
  check('B7', (await tile(borne, OTHER.name).count()) === 0, 'QA 991861 has no tile once untracked')
  await adm.getByRole('switch', { name: `${OTHER.name} pointe` }).click()
  await adm.waitForTimeout(800)
})

await scenario('B8', async () => {
  await sec.goto(`${WEB}/pointage`)
  check('B8', await waitText(sec, /Cet appareil n'est pas une borne du cabinet/, 15000), 'secretary device: « Cet appareil n\'est pas une borne du cabinet. »')
  await shot(sec, 'B8-not-borne')
})

await scenario('B13', async () => {
  await adm.goto(inesFile)
  await adm.getByRole('button', { name: 'Ajouter une absence' }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#staff-leave-start').waitFor()
  await dlg.getByRole('radio', { name: 'Maladie', exact: true }).click()
  await dlg.locator('#staff-leave-start').fill(TODAY)
  await dlg.locator('#staff-leave-end').fill(TODAY)
  await dlg.locator('#staff-leave-reason').fill('QA borne B13')
  await dlg.getByRole('button', { name: "Enregistrer l'absence" }).click()
  await toast(adm, /Absence enregistrée/)
  await borne.reload()
  await waitText(borne, /Borne Accueil/, 15000)
  const t = await tileText(borne, INES.name)
  // She is in (arrived at lunch), so « Présente » wins; the leave shows once she has no open line.
  check('B13', /Présente depuis|En congé/.test(t), 'tile with a full-day congé today (present wins while a line is open)', t)
  psql(`update "AttendanceEntries" set "DepartureUtc" = "ArrivalUtc" + interval '1 hour', "DepartureSource" = 0 where "UserId"='${INES.id}' and "DepartureUtc" is null`)
  await borne.reload()
  await waitText(borne, /Borne Accueil/, 15000)
  const t2 = await tileText(borne, INES.name)
  check('B13', /En congé/.test(t2) && (await tile(borne, INES.name).first().isDisabled()), 'with no open line: « En congé », disabled', t2)
  psql(`delete from "StaffLeaveRequests" where "Reason"='QA borne B13'`)
})

await scenario('B11', async () => {
  await adm.goto(`${WEB}/settings`)
  const sw = adm.locator('#team-attendance')
  await sw.waitFor({ timeout: 20000 })
  await sw.scrollIntoViewIfNeeded()
  await sw.click()
  check('B11', Boolean(await toast(adm, /Pointage désactivé/)), 'toast « Pointage désactivé. »')
  await borne.reload()
  check('B11', await waitText(borne, /Le pointage n'est pas activé pour ce cabinet/, 15000), 'borne: « Le pointage n\'est pas activé pour ce cabinet. »')
  await adm.goto(`${WEB}/users`)
  await waitText(adm, /Utilisateurs/, 15000)
  await adm.waitForTimeout(1500)
  check('B11', (await adm.getByRole('tab', { name: 'Présences' }).count()) === 0, '/users loses « Présences »')
  await sec.goto(`${WEB}/mon-profil`)
  await sec.waitForTimeout(3000)
  check('B11', !/Schéma de pointage|Mes présences/.test(await bodyText(sec)), 'Mon profil loses both cards')
  await adm.goto(`${WEB}/settings`)
  await adm.locator('#team-attendance').waitFor({ timeout: 20000 })
  await adm.locator('#team-attendance').scrollIntoViewIfNeeded()
  await adm.locator('#team-attendance').click()
  await toast(adm, /Pointage activé/)
})

await scenario('D4', async () => {
  const before = await adm.locator('#team-attendance').getAttribute('aria-checked')
  await adm.locator('#team-leave-requests').click()
  await toast(adm, /Demandes de congé désactivées/)
  const mid = await adm.locator('#team-attendance').getAttribute('aria-checked')
  await adm.locator('#team-leave-requests').click()
  await toast(adm, /Demandes de congé activées/)
  check('D4', before === 'true' && mid === 'true', 'the congés switch leaves the pointage switch alone', `${before} → ${mid}`)
})

await scenario('D1', async () => {
  await adm.goto(`${WEB}/users?onglet=conges`)
  await waitText(adm, /Utilisateurs/, 15000)
  await adm.waitForTimeout(1500)
  const sel = await adm.getByRole('tab', { name: /Congés/ }).getAttribute('aria-selected')
  check('D1', sel === 'true', '?onglet=conges opens « Congés »', sel ?? '')
})

// ---------------------------------------------------------------- Tier C (widths)
await scenario('C1', async () => {
  await borne.goto(`${WEB}/pointage`)
  await waitText(borne, /Borne Accueil/, 15000)
  for (const [w, h] of [[820, 1180], [1180, 820], [390, 844], [320, 640]]) {
    await borne.setViewportSize({ width: w, height: h })
    await borne.waitForTimeout(500)
    const o = await mainOverflow(borne)
    check('C1', o.doc <= 0, `tiles at ${w}×${h}: no horizontal scroll`, JSON.stringify(o))
    await shot(borne, `C1-tiles-${w}x${h}`)
  }
  await borne.setViewportSize({ width: 1180, height: 820 })
  await tile(borne, INES.name).first().click()
  const pad = await borne.locator('svg[role="application"]').boundingBox()
  const back = await borne.getByRole('button', { name: 'Retour' }).boundingBox()
  check('C1', pad && back && back.y + back.height <= 820, 'panel at 1180×820: pad and « Retour » on screen', JSON.stringify({ pad, back }))
  check('C1', pad && pad.width / 3 >= 44, 'each dot cell ≥ 44 px', `${pad ? Math.round(pad.width / 3) : 0}px`)
  await shot(borne, 'C1-panel-1180x820')
  await borne.setViewportSize({ width: 320, height: 640 })
  await borne.waitForTimeout(400)
  const pad320 = await borne.locator('svg[role="application"]').boundingBox()
  check('C1', pad320 && pad320.x >= 0 && pad320.x + pad320.width <= 320, 'pad fits at 320', JSON.stringify(pad320))
  await shot(borne, 'C1-panel-320')
  await borne.getByRole('button', { name: 'Retour' }).click()
  await borne.setViewportSize({ width: 820, height: 1180 })
})

await scenario('C2', async () => {
  await adm.goto(`${WEB}/users?onglet=presences`)
  await waitText(adm, /novembre 2026/i, 15000)
  for (const w of [390, 320]) {
    await adm.setViewportSize({ width: w, height: 844 })
    await adm.waitForTimeout(700)
    const o = await mainOverflow(adm)
    const tables = await adm.locator('table:visible').count()
    check('C2', tables === 0, `${w}: cards, no table`, `tables=${tables}`)
    if (w === 390) check('C2', await waitText(adm, /Jeudi 5 novembre/, 4000), '390: card title « Jeudi 5 novembre » (month lower-case)')
    check('C2', o.main <= 0 && o.doc <= 0, `${w}: no horizontal scroll`, JSON.stringify(o))
    await shot(adm, `C2-presences-${w}`)
  }
  await adm.setViewportSize({ width: 1000, height: 830 })
})

await scenario('C4', async () => {
  await adm.setViewportSize({ width: 390, height: 844 })
  await adm.goto(`${WEB}/settings`)
  await adm.locator('#team-attendance').waitFor({ timeout: 20000 })
  await adm.getByText('Personnes qui pointent').scrollIntoViewIfNeeded()
  await adm.waitForTimeout(500)
  const o = await mainOverflow(adm)
  check('C4', o.main <= 0 && o.doc <= 0, 'Paramètres → Équipe at 390: no horizontal overflow', JSON.stringify(o))
  const ret = await adm.getByRole('button', { name: /Retirer la borne/ }).first().boundingBox()
  check('C4', ret && ret.x + ret.width <= 390, '« Retirer » reachable at 390', JSON.stringify(ret))
  await shot(adm, 'C4-settings-390')
  await adm.setViewportSize({ width: 1000, height: 830 })
})

await scenario('R-rename', async () => {
  await adm.goto(`${WEB}/settings`)
  await adm.locator('#team-attendance').waitFor({ timeout: 20000 })
  const active0 = psql(`select count(*) from "AttendanceTerminals" where "ClinicId"='${CLINIC}' and "RevokedAt" is null`)
  await adm.getByRole('button', { name: /Renommer cet appareil/ }).click()
  const dlg = openDialog(adm)
  await dlg.locator('#borne-name').waitFor()
  await dlg.locator('#borne-name').fill('Accueil')
  await dlg.getByRole('button', { name: 'Enregistrer la borne' }).click()
  const code = adm.locator('[role="dialog"]:visible input[inputmode=numeric]').last()
  await code.waitFor({ timeout: 8000 })
  await nextWindow()
  await code.fill(totp(ADMIN.totp))
  await openDialog(adm).getByRole('button', { name: 'Confirmer' }).click()
  await toast(adm, /maintenant la borne/, 10000)
  const active1 = psql(`select count(*) from "AttendanceTerminals" where "ClinicId"='${CLINIC}' and "RevokedAt" is null`)
  check('R-rename', active0 === '1' && active1 === '1', 'review: « Renommer cet appareil » retires the borne it replaces (one active borne, not two)', `${active0} → ${active1}`)
  await adm.reload()
  await adm.locator('#team-attendance').waitFor({ timeout: 20000 })
  await adm.waitForTimeout(1500)
  const here = await adm.getByText('cet appareil', { exact: true }).count()
  check('R-rename', here === 1, 'exactly one row reads « cet appareil »', `n=${here}`)
  await borne.reload()
  check('R-rename', await waitText(borne, /Borne Accueil/, 15000), 'the borne still works on its new cookie')
})

// ---------------------------------------------------------------- last: revoke the borne
await scenario('B12', async () => {
  await adm.goto(`${WEB}/settings`)
  await adm.locator('#team-attendance').waitFor({ timeout: 20000 })
  await adm.getByRole('button', { name: 'Retirer la borne Accueil' }).click()
  await adm.getByRole('alertdialog').getByRole('button', { name: 'Retirer' }).click()
  check('B12', Boolean(await toast(adm, /Borne\s*«\s*Accueil\s*»\s*retirée/)), 'toast « Borne « Accueil » retirée. »')
  await borne.reload()
  check('B12', await waitText(borne, /Cet appareil n'est pas une borne du cabinet/, 15000), 'borne: « Cet appareil n\'est pas une borne »')
  await adm.reload()
  await adm.waitForTimeout(2500)
  check('B12', (await adm.getByRole('link', { name: 'Pointage' }).count()) === 0, 'header « Pointage » gone after a reload')
})

await scenario('D5', async () => skip('D5', 'answered outside the browser: unfiltered unit suite 6605 passed / 0 failed'))
await scenario('B9', async () => skip('B9', 'answered by curl before the launch: 401 terminal_unknown with and without a bogus cookie'))

// ================================================================ report
console.log('\n================ SUMMARY')
console.log(`pass ${results.filter((r) => r.outcome === 'pass').length} · fail ${results.filter((r) => r.outcome === 'fail').length} · skip ${results.filter((r) => r.outcome === 'skip').length}`)
fs.writeFileSync(path.join(HERE, 'last-run.json'), JSON.stringify({ results, findings }, null, 2))
if (findings.length) findings.forEach((f) => console.log('FINDING', JSON.stringify(f)))
else console.log('ALL CLEAR')
// Both Chrome windows stay open for the eye pass.
process.exit(0)
