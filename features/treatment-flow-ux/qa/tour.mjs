// A guided tour of the treatment-flow redesign, for the owner to WATCH and validate. A headed Chrome, signed in;
// a panel in the page names what to look at, « Next » sets up the next screen. Nothing is saved by the script except
// the conflict step (a colleague's note on a throwaway devis). ✓ = the script read it on screen, ○ = your eye.
// Usage: node tour.mjs            (headed, waits for the panel's buttons, stays open until the window is closed)
//        node tour.mjs --dry --shots=<dir>   (headless dry run: every step once, prints the marks, screenshots)
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium } from '../../../e2e/node_modules/playwright-core/index.mjs'
import { totp } from '../../devis-fiche-rdv-flexibility/qa/totp-act-onto-devis.mjs'

const here = dirname(fileURLToPath(import.meta.url))
const F1 = JSON.parse(readFileSync(join(here, 'fixtures.json'), 'utf8'))
const F2 = JSON.parse(readFileSync(join(here, 'fixtures-2.json'), 'utf8'))
const DRY = process.argv.includes('--dry')
const SHOTS = process.argv.find((a) => a.startsWith('--shots='))?.slice(8)
if (SHOTS) mkdirSync(SHOTS, { recursive: true })
const WEB = 'http://localhost:3000'
const API = 'http://localhost:5000/api'
const ADMIN = { email: 'salma.benyoussef@cabinet-ibnkhaldoun.tn', password: 'QaAudit2026!y', secret: '4YRLT22RBPRP3RRKUKLFQERU4H62BRBO' }
const PVR = '**/notifications/pending-reviews**'
const pvr = (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

// ─── TOTP: single-use per 30 s window, so never reuse the window of the previous sign-in ───
let lastWindow = -1
async function freshCode() {
  let w
  do {
    await sleep((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
    w = Math.floor(Date.now() / 30000)
  } while (w === lastWindow)
  lastWindow = w
  return totp(ADMIN.secret)
}
let token = null
let tokenAt = 0
async function apiToken() {
  if (token && Date.now() - tokenAt < 8 * 60000) return token
  const r = await fetch(`${API}/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: ADMIN.email, password: ADMIN.password, totpCode: await freshCode() }),
  })
  const j = await r.json()
  token = j.value?.accessToken ?? j.accessToken
  tokenAt = Date.now()
  return token
}
async function api(method, path, body) {
  const r = await fetch(`${API}${path}`, {
    method, headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${await apiToken()}` },
    body: body ? JSON.stringify(body) : undefined,
  })
  const t = await r.text()
  let j
  try { j = JSON.parse(t) } catch { j = t }
  return j?.value ?? j
}

// ─── the panel, injected in every document of the main window ───
function panelInit() {
  if (window.top !== window || window.__tourInit) return
  window.__tourInit = true
  let state = null
  let host = null
  let box = null
  let mini = false
  let pos = null
  try {
    pos = JSON.parse(sessionStorage.getItem('__tourPos') || 'null')
    mini = sessionStorage.getItem('__tourMini') === '1'
  } catch {}
  const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c])
  const CSS = `
    .p{position:fixed;z-index:2147483647;width:252px;font:13px/1.42 "Segoe UI",system-ui,sans-serif;color:#eef4f7;
       background:#10212b;border:1px solid #2f4a59;border-radius:12px;box-shadow:0 10px 30px rgba(0,0,0,.38);
       pointer-events:auto;user-select:none}
    .h{display:flex;align-items:center;gap:6px;padding:7px 8px 7px 11px;cursor:grab;border-bottom:1px solid #2f4a59;
       font-size:11.5px;color:#9fb6c3}
    .h b{color:#fff;font-weight:700}
    .sp{flex:1}
    .x{all:unset;cursor:pointer;padding:0 6px;color:#9fb6c3;font-size:15px;line-height:1}
    .b{padding:9px 11px 2px}
    .t{font-weight:700;font-size:14px;color:#fff;margin-bottom:3px}
    .w{color:#94abb8;font-size:11.5px;margin-bottom:7px}
    ul{list-style:none;margin:0;padding:0;display:grid;gap:5px}
    li{display:grid;grid-template-columns:15px 1fr;gap:5px}
    .ok{color:#5fd49a;font-weight:700}.no{color:#f2b35b;font-weight:700}.look{color:#7fc8f0}
    .n{margin-top:8px;font-size:12px;color:#f2d58a}
    .f{display:flex;gap:6px;padding:9px 11px 10px}
    button.k{all:unset;cursor:pointer;border-radius:8px;padding:7px 11px;background:#1d3542;color:#fff;font-weight:600;
       font-size:12.5px;text-align:center}
    button.k.pri{flex:1;background:#1e8fc4}
    button.k[disabled]{opacity:.4;cursor:default}
    .lg{padding:0 11px 9px;font-size:10.5px;color:#7f97a4}`
  function place() {
    if (!box) return
    if (pos) {
      const x = Math.min(Math.max(0, pos.x), innerWidth - 60)
      const y = Math.min(Math.max(0, pos.y), innerHeight - 40)
      Object.assign(box.style, { left: x + 'px', top: y + 'px', bottom: 'auto' })
    } else Object.assign(box.style, { left: '12px', bottom: '12px', top: 'auto' })
  }
  function draw() {
    if (!box || !state) return
    const s = state
    const mark = { ok: '✓', no: '✗', look: '○' }
    const items = (s.items || []).map((it) => `<li><span class="${it.m}">${mark[it.m]}</span><span>${esc(it.t)}</span></li>`).join('')
    box.innerHTML = `
      <div class="h" data-drag><b>${s.i} / ${s.n}</b><span>${esc(s.sec)}</span><span class="sp"></span>
        <button class="x" data-c="mini" title="${mini ? 'Agrandir' : 'Réduire'}">${mini ? '▢' : '–'}</button></div>
      ${mini ? '' : `<div class="b"><div class="t">${esc(s.title)}</div>
        ${s.was ? `<div class="w">Before: ${esc(s.was)}</div>` : ''}<ul>${items}</ul>
        ${s.note ? `<div class="n">${esc(s.note)}</div>` : ''}</div>`}
      <div class="f">
        <button class="k" data-c="prev" title="Previous" ${s.busy || s.i <= 1 ? 'disabled' : ''}>◀</button>
        <button class="k" data-c="redo" title="Set this screen up again" ${s.busy ? 'disabled' : ''}>↻</button>
        <button class="k pri" data-c="next" ${s.busy || s.i >= s.n ? 'disabled' : ''}>${s.busy ? 'Setting up…' : s.i >= s.n ? 'Done' : 'Next ▶'}</button>
      </div>
      ${mini ? '' : '<div class="lg">✓ read on screen by the script · ○ your eye<br>Keys: Alt+→ next · Alt+← back · Alt+R redo</div>'}`
    place()
  }
  function mount() {
    if (!document.body || (host && host.isConnected)) return
    host = document.createElement('div')
    host.style.cssText = 'position:fixed;top:0;left:0;width:0;height:0;z-index:2147483647;pointer-events:auto'
    const root = host.attachShadow({ mode: 'open' })
    root.innerHTML = `<style>${CSS}</style><div class="p"></div>`
    box = root.querySelector('.p')
    // The app's dialogs close on a press « outside » them and trap focus: keep our presses to ourselves.
    for (const ev of ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click', 'touchstart', 'touchend', 'focusin', 'keydown', 'wheel']) {
      host.addEventListener(ev, (e) => { e.stopPropagation(); if (ev === 'mousedown') e.preventDefault() })
    }
    box.addEventListener('click', (e) => {
      const c = e.target.closest?.('[data-c]')?.getAttribute('data-c')
      if (!c || e.target.closest('[disabled]')) return
      if (c === 'mini') {
        mini = !mini
        try { sessionStorage.setItem('__tourMini', mini ? '1' : '0') } catch {}
        draw()
      } else window.__tourCmd?.(c)
    })
    let drag = null
    box.addEventListener('pointerdown', (e) => {
      const h = e.target.closest?.('[data-drag]')
      if (!h || e.target.closest('button')) return
      const r = box.getBoundingClientRect()
      drag = { dx: e.clientX - r.left, dy: e.clientY - r.top }
      h.setPointerCapture(e.pointerId)
    })
    box.addEventListener('pointermove', (e) => {
      if (!drag) return
      pos = { x: e.clientX - drag.dx, y: e.clientY - drag.dy }
      place()
    })
    box.addEventListener('pointerup', () => {
      if (!drag) return
      drag = null
      try { sessionStorage.setItem('__tourPos', JSON.stringify(pos)) } catch {}
    })
    document.body.appendChild(host)
    draw()
  }
  // A state older than the one drawn is ignored: a read started during a set-up must not undo the final render.
  const accept = (s) => { if (s && (!state || s.seq >= state.seq)) { state = s; mount(); draw() } }
  window.__tourRender = accept
  // Alt+→ / Alt+← / Alt+R work even when a dialog holds the focus or a layer blocks the panel.
  window.addEventListener('keydown', (e) => {
    const c = e.altKey && ({ ArrowRight: 'next', ArrowLeft: 'prev', r: 'redo', R: 'redo' })[e.key]
    if (!c) return
    e.preventDefault()
    e.stopPropagation()
    window.__tourCmd?.(c)
  }, true)
  const start = () => setTimeout(() => {
    mount()
    window.__tourGet?.().then(accept).catch(() => {})
    setInterval(mount, 700)
  }, 1200)
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start)
  else start()
}

// ─── page helpers ───
const main = (pg) => pg.locator('main').first()
const dlg = (pg) => pg.locator('[role="dialog"]:visible, [role="alertdialog"]:visible').last()
const popover = (pg) => pg.locator('[data-slot="popover-content"]:visible').last()
const menu = (pg) => pg.locator('[role="menu"]:visible').last()
async function go(pg, path) {
  await pg.goto(`${WEB}${path}`, { waitUntil: 'domcontentloaded' })
  await pg.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {})
  await pg.waitForTimeout(400)
}
async function waitText(pg, loc, re, ms = 20000) {
  const end = Date.now() + ms
  let t = ''
  while (Date.now() < end) {
    t = (await loc.innerText({ timeout: 2000 }).catch(() => '')) || ''
    if (re.test(t)) return t
    await pg.waitForTimeout(250)
  }
  return t
}
const centre = (loc) => loc.evaluate((el) => el.scrollIntoView({ block: 'center' })).catch(() => {})
const planPage = (id, re = /Couronne/) => async (pg) => { await go(pg, `/treatment-plans/${id}`); await waitText(pg, main(pg), re) }
async function openMenu(pg) {
  await main(pg).getByRole('button', { name: /Actions|Autres actions|⋯/ }).first().click()
  await pg.waitForTimeout(500)
}
async function menuItem(pg, re) {
  await openMenu(pg)
  await menu(pg).getByRole('menuitem', { name: re }).first().click()
  await waitText(pg, dlg(pg), /\S/, 6000)
}
async function strip(pg, re, wait) {
  await main(pg).getByRole('button', { name: re }).first().click()
  await waitText(pg, popover(pg), wait, 6000)
}
async function openBooking(pg, patientId) {
  await go(pg, `/appointments?patientId=${patientId}`)
  const t = await waitText(pg, dlg(pg), /Nouveau rendez-vous/, 8000)
  if (!/Nouveau rendez-vous/.test(t)) {
    await pg.locator("button[data-size='sm']:has-text('Nouveau')").first().click()
    await waitText(pg, dlg(pg), /Nouveau rendez-vous/, 10000)
  }
}
async function pickAct(pg, name) {
  await waitText(pg, dlg(pg), /Sélectionner un type d'acte/, 15000)
  await dlg(pg).getByText("Sélectionner un type d'acte").first().click()
  await pg.waitForTimeout(400)
  await pg.keyboard.type(name, { delay: 40 })
  await pg.waitForTimeout(800)
  await pg.getByRole('option', { name: new RegExp(name) }).first().click()
  await waitText(pg, dlg(pg), /ce RDV/, 8000)
}
async function openFiche(pg, patient, visit) {
  await go(pg, `/patients/${patient}?addRecord=1&appointmentId=${visit}`)
  await waitText(pg, dlg(pg), /Fiche de soins/, 20000)
  await pg.waitForTimeout(600)
}
async function addFicheAct(pg, name) {
  await dlg(pg).getByRole('button', { name: /Ajouter un autre acte/ }).first().click()
  await pg.waitForTimeout(500)
  await pg.keyboard.type(name, { delay: 40 })
  await pg.waitForTimeout(800)
  await pg.getByRole('option', { name: new RegExp(name) }).first().click().catch(() => {})
  await pg.waitForTimeout(900)
}
async function chooseCheque(pg) {
  await dlg(pg).locator('#paid-method').first().click()
  await pg.waitForTimeout(400)
  await pg.getByRole('option', { name: /Chèque/ }).first().click()
  await waitText(pg, dlg(pg), /Chèque\s*:/, 5000)
}
async function mixedFiche(pg) {
  await openFiche(pg, F2.mixed.patient, F2.mixed.visit)
  await addFicheAct(pg, 'Détartrage')
}
const noH = (pg) => pg.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)
async function minH(pg, loc) {
  const n = await loc.count()
  let min = Infinity
  for (let i = 0; i < n; i++) {
    const el = loc.nth(i)
    if (!(await el.isVisible().catch(() => false))) continue
    const h = await el.evaluate((node) => {
      const r = node.getBoundingClientRect()
      const a = getComputedStyle(node, '::after')
      const overlay = a.content !== 'none' && a.position === 'absolute' ? parseFloat(a.height) || 0 : 0
      return Math.max(r.height, overlay)
    }).catch(() => Infinity)
    min = Math.min(min, h)
  }
  return n ? min : 0
}
const visibleBtn = (re) => async (pg) => {
  const b = await dlg(pg).getByRole('button', { name: re }).last().boundingBox()
  const h = await pg.evaluate(() => innerHeight)
  return !!b && b.y >= 0 && b.y + b.height <= h
}
const has = (re, scope = 'main') => ({ re, scope })
const hasnt = (re, scope = 'main') => ({ re, scope, not: true })
const test = (fn) => ({ fn })

// ─── setups that need more than one line ───
async function conflictSetup(pg) {
  await planPage(F2.conflict.plan)(pg)
  const price = main(pg).locator('input[aria-label^="Prix de"]:visible').first()
  const cur = Math.round(Number(((await price.inputValue()) || '300').replace(/\s/g, '').replace(',', '.'))) || 300
  await price.fill(String(cur + 10))
  await price.press('Tab')
  await pg.waitForTimeout(500)
  setNote('A colleague saves the same treatment… (up to 30 s)')
  const p = await api('GET', `/treatment-plans/${F2.conflict.plan}`)
  await api('POST', `/treatment-plans/${F2.conflict.plan}/amend`, {
    addItems: [], updateItems: [], removeItemIds: [], notes: `Note d'un collègue ${new Date().toLocaleTimeString('fr-FR')}`, version: p.version,
  })
  await pg.getByRole('button', { name: /^Enregistrer$/ }).last().click()
  const end = Date.now() + 8000
  while (Date.now() < end && !(await pg.getByRole('button', { name: /Recharger/ }).count())) await pg.waitForTimeout(250)
}
async function clotureSetup(pg) {
  await go(pg, '/a-cloturer?tab=suites')
  const tab = main(pg).getByRole('tab', { name: /Suites à planifier/ })
  if (await tab.count()) { await tab.first().click(); await pg.waitForTimeout(800) }
  await waitText(pg, main(pg), new RegExp(F1.cont.name), 12000)
  const button = (scope) => scope.locator('button:visible', { hasText: /Planifier la suite/ })
  const row = main(pg).locator('*').filter({ hasText: F1.cont.name }).filter({ has: button(pg) }).last()
  await ((await row.count()) ? button(row) : button(main(pg))).first().click()
  await waitText(pg, dlg(pg), /suite du/, 25000)
}
async function footerOk(pg) {
  const btns = (await dlg(pg).getByRole('button').allInnerTexts()).map((s) => s.trim()).filter(Boolean)
  return btns.includes('Fermer') && btns.includes('Enregistrer') && !btns.includes('Supprimer') && !btns.some((b) => /^Annuler$/.test(b))
}
async function stripScrolls(pg) {
  return pg.evaluate(() => {
    const ol = document.querySelector('main ol[aria-label]')
    const box = ol?.parentElement
    return box ? box.scrollWidth > box.clientWidth : false
  })
}

// ─── the steps ───
const M = F1.main
const D = F1.draft
const T = F1.today
const C = F1.cont
const PL = F1.plain
const S = [
  { sec: 'Treatment page', title: 'Header: what, where, next', was: '« Plan 2026-… », « 1 / 3 », « PROCHAINE SÉANCE », a 45-word footnote',
    setup: planPage(M.plan),
    look: [
      ['Name « Couronne · dent 16 »', has(/Couronne\s*·\s*dent 16/)],
      [`Badge « En cours » + chip « Devis n° ${M.number} »`, has(new RegExp(`(?=[^]*En cours)(?=[^]*Devis n°\\s*${M.number})`))],
      ['Séances in words: « faite le » · « prévue le » · « à planifier »', has(/(?=[^]*faite le)(?=[^]*prévue le)(?=[^]*à planifier)/)],
      ['ONE big button « Planifier : Scellement »', has(/Planifier\s*:\s*Scellement/)],
      ['No « 1 / 3 », no « PROCHAINE SÉANCE »', hasnt(/PROCHAINE SÉANCE|\b[0-3] \/ 3\b|Retour aux plans/i)],
    ] },
  { title: 'Tap a planned séance', was: 'a separate « Étapes » window to open',
    setup: async (pg) => { await planPage(M.plan)(pg); await strip(pg, /^Empreinte/, /prévue/) },
    look: [
      ['« prévue le » + the date', has(/prévue le/, 'popover')],
      ['« Voir le RDV » · « Déplacer », right here', has(/(?=[^]*Voir le RDV)(?=[^]*Déplacer)/, 'popover')],
      ['Try « Déplacer »: the RDV opens on this page'],
    ] },
  { title: 'Tap a done séance', was: 'a separate « Étapes » window to open',
    setup: async (pg) => { await planPage(M.plan)(pg); await strip(pg, /^Préparation/, /faite/) },
    look: [
      ['« faite le » + the date', has(/faite le/, 'popover')],
      ['« Voir la fiche »', has(/Voir la fiche/, 'popover')],
      ['« Remettre à faire » (last done séance only)', has(/Remettre à faire/, 'popover')],
      ['« Modifier les séances »', has(/Modifier les séances/, 'popover')],
    ] },
  { title: 'Price and remise edited in the row', was: '« Modifier le devis » → the whole form',
    setup: async (pg) => {
      await planPage(M.plan)(pg)
      const b = main(pg).getByRole('button', { name: /remise/i }).first()
      if (await b.count()) { await b.click(); await pg.waitForTimeout(400) }
      const f = main(pg).locator('input[aria-label*="emise"]:visible, input[id*="discount"]:visible').first()
      const was = Math.round(Number(((await f.inputValue()) || '0').replace(/\s/g, '').replace(',', '.'))) || 0
      await f.fill(String(was === 20 ? 25 : 20))
      await f.press('Tab')
      await pg.waitForTimeout(500)
    },
    look: [
      ['Bar « 1 modification » right under « Actes »', has(/1 modification/)],
      ['« Enregistrer » saves it (test patient — safe)'],
      ['Leaving without saving asks first'],
    ],
    note: 'The script typed a remise. Save it or not — both fine.' },
  { title: 'Money: 3 figures, 1 button', was: '« Régler le devis » + the échéancier table always open',
    setup: async (pg) => {
      await planPage(M.plan)(pg)
      await main(pg).getByText(/L['’]argent/i).first().evaluate((el) => el.scrollIntoView({ block: 'start' })).catch(() => {})
    },
    look: [
      ['« Prix » · « Payé » · « Reste à payer »', has(/(?=[^]*Prix)(?=[^]*Payé)(?=[^]*Reste à payer)/)],
      ['One « Encaisser »', test(async (pg) => (await main(pg).getByRole('button', { name: /^Encaisser$/ }).count()) >= 1)],
      ['« Échéancier » folded — tap to open', has(/Échéancier \(\d+\)/)],
    ] },
  { title: 'The ⋯ menu, in groups', was: 'one long flat list',
    setup: async (pg) => { await planPage(M.plan)(pg); await openMenu(pg) },
    look: [['Groups « Document » · « Modifier » · « Fin du traitement »', has(/(?=[^]*Document)(?=[^]*Modifier)(?=[^]*Fin du traitement)/i, 'menu')]] },
  { title: 'A confirmation = a question + bullets', was: 'a paragraph to read before every button',
    setup: async (pg) => { await planPage(M.plan)(pg); await menuItem(pg, /Arrêter/) },
    look: [
      ['Title is a question', has(/\?/, 'dialog')],
      ['2–3 bold bullets, no paragraph'],
      ['« Retour » leaves nothing written'],
    ],
    note: 'Press « Retour ».' },
  { title: 'Several acts on one treatment', was: 'title « Plan de traitement », counts « 1 / 3 »',
    setup: planPage(F2.multi.plan),
    look: [
      ['Name built from the acts, not « Plan de traitement »'],
      ['Count in words (« … sur … faite(s) »)', has(/séances? sur \d+ faites?|séances? à faire|Toutes les séances/)],
      ['Each act: its own séances + « Planifier »'],
    ] },
  { title: 'Billed treatment', was: 'a long « why » sentence',
    setup: planPage(F2.billed.plan),
    look: [
      ['The note is named (« note n° … »)', has(/note n°|Facturé/i)],
      ['No « n’apparaîtrait ni dans la caisse… » sentence', hasnt(/n'apparaîtrait ni dans la caisse/)],
    ] },
  { title: 'Stopped treatment', was: 'left séances read « à planifier »',
    setup: planPage(F2.stopped.plan),
    look: [
      ['Badge « Arrêté »', has(/Arrêté/)],
      ['Séances left say « non faite »', has(/non faite/)],
      ['Main button « Reprendre le traitement »', has(/Reprendre le traitement/)],
    ] },
  { title: 'Not claimed (written off)', was: '« Créance abandonnée » / « Passer en perte »',
    setup: planPage(F2.writtenOff.plan),
    look: [
      ['Badge « Non réclamé »', has(/Non réclamé/)],
      ['Old words gone', hasnt(/Créance abandonnée|Passer en perte/)],
    ] },
  { title: 'Cancelled devis',
    setup: planPage(F2.cancelled.plan, /Soin/),
    look: [['Badge « Annulé »', has(/Annulé/)], ['« Rétablir » offered', has(/Rétablir/)]] },
  { title: 'Not started, no devis yet', was: 'badge « Sans devis » + « Aucun devis édité… »',
    setup: planPage(D.plan),
    look: [
      ['Badge « À commencer »', has(/À commencer/)],
      ['« Pas de devis » + « Créer le devis »', has(/(?=[^]*Pas de devis)(?=[^]*Créer le devis)/)],
      ['No « Sans devis » badge', hasnt(/Sans devis|Aucun devis édité/)],
    ] },
  { title: 'Stop, with a deposit and no work', was: 'confirm → red error after pressing',
    setup: async (pg) => { await planPage(F2.refund.plan)(pg); await menuItem(pg, /Arrêter/) },
    look: [
      ['Names the 80 DT to give back first', has(/80,000/, 'dialog')],
      ['No « irréversible »', hasnt(/irréversible/i, 'dialog')],
      ['Only « Retour » — nothing to confirm by mistake'],
    ] },
  { title: 'Someone else saved meanwhile', was: 'a red error, and every retry refused',
    setup: conflictSetup,
    look: [
      ['One line in the bar + a real « Recharger »', test(async (pg) => (await pg.getByRole('button', { name: /Recharger/ }).count()) > 0)],
      ['« Recharger » brings the fresh version'],
    ],
    note: 'The script typed a new price, then a colleague saved first.' },

  { sec: 'Patient file', title: 'One card per treatment', was: '« Plan 2026-… », « Tous les plans », no booking from it',
    setup: async (pg) => {
      await go(pg, `/patients/${M.patient}`)
      await waitText(pg, main(pg), /Couronne/)
      await centre(main(pg).getByText(/Tous les traitements/).last())
    },
    look: [
      ['Card « Couronne · dent 16 »', has(/Couronne\s*·\s*dent 16/)],
      ['Dots + « … prévue le … »', has(/prévue le/)],
      ['« Reste à payer »', has(/Reste à payer/)],
      ['Button « Planifier : Scellement »', has(/Planifier\s*:\s*Scellement/)],
    ] },
  { title: 'Book straight from the card', was: 'go to the agenda, find the patient, pick the act again',
    setup: async (pg) => {
      await go(pg, `/patients/${D.patient}`)
      await waitText(pg, main(pg), /Couronne/)
      await main(pg).getByRole('button', { name: /^Planifier\s*:/ }).first().click()
      await waitText(pg, dlg(pg), /ce RDV/, 15000)
    },
    look: [
      ['Opens here, no page change', test(async (pg) => pg.url().includes(`/patients/${D.patient}`))],
      ['The act is already in, on its séance (« ce RDV »)', has(/ce RDV/, 'dialog')],
      ['« Créer le rendez-vous » visible, no scrolling', test(visibleBtn(/Créer le rendez-vous/))],
    ] },

  { sec: 'Booking', title: 'The patient’s treatments come first', was: '« Prochaine étape… Lequel continuez-vous ? » paragraph',
    setup: async (pg) => {
      await go(pg, `/patients/${F2.multi.patient}`)
      await pg.locator('a:has-text("Planifier un RDV"):visible, button:has-text("Planifier un RDV"):visible').first().click()
      await waitText(pg, dlg(pg), /Continuer un traitement/, 15000)
    },
    look: [
      ['« Continuer un traitement » card on top', has(/Continuer un traitement/, 'dialog')],
      ['Card says « Planifier : … »', has(/Planifier\s*:/, 'dialog')],
      ['No devis number, no price on the card'],
    ] },
  { title: 'One tap adds the séance', was: 'pick the act, then the devis, then the step',
    setup: async (pg) => {
      await go(pg, `/patients/${F2.multi.patient}`)
      await pg.locator('a:has-text("Planifier un RDV"):visible, button:has-text("Planifier un RDV"):visible').first().click()
      await waitText(pg, dlg(pg), /Continuer un traitement/, 15000)
      await dlg(pg).locator('button:has-text("Planifier :")').first().click()
      await waitText(pg, dlg(pg), /ce RDV/, 10000)
    },
    look: [
      ['Act row with its séances, « ce RDV » marked', has(/ce RDV/, 'dialog')],
      ['Tag « Inclus dans le traitement » — no price to type', has(/Inclus dans le traitement/, 'dialog')],
      ['No « Chiffré sur le devis » paragraph', hasnt(/Chiffré sur le devis|L'acte entier est chiffré/, 'dialog')],
    ] },
  { title: '« Séances ▾ » on a treatment act', was: 'séances only by tapping the strip; no way to add one here',
    setup: async (pg) => {
      await go(pg, `/patients/${F2.multi.patient}`)
      await pg.locator('a:has-text("Planifier un RDV"):visible, button:has-text("Planifier un RDV"):visible').first().click()
      await waitText(pg, dlg(pg), /Continuer un traitement/, 15000)
      await dlg(pg).locator('button:has-text("Planifier :")').first().click()
      await waitText(pg, dlg(pg), /ce RDV/, 10000)
      await dlg(pg).getByRole('button', { name: /^Modifier les séances de/ }).first().click()
      await waitText(pg, dlg(pg), /Ajouter une séance au traitement/, 6000)
    },
    dryExtra: async (pg) => {
      await dlg(pg).getByRole('button', { name: /Ajouter une séance au traitement/ }).first().click()
      await waitText(pg, pg.locator('[role="dialog"]:visible').last(), /Enregistrer les séances/, 8000)
      const top = pg.locator('[role="dialog"]:visible').last()
      await top.locator('input[placeholder="Nom de la séance"]').last().fill('Contrôle tour')
      await top.getByRole('button', { name: /Enregistrer les séances/ }).click()
      const t = await waitText(pg, dlg(pg), /Contrôle tour/, 12000)
      const dialogs = await pg.locator('[role="dialog"]:visible').count()
      console.log(`   dry: new séance flows back into the booking row: ${/Contrôle tour/.test(t)} · booking still open: ${/Nouveau rendez-vous/.test(t)} · dialogs open: ${dialogs}`)
    },
    look: [
      ['Same « Séances » button as a new act', test(async (pg) => (await dlg(pg).getByRole('button', { name: /^Modifier les séances de/ }).count()) > 0)],
      ['Tick which séances this RDV does (done ones locked)'],
      ['« + Ajouter une séance au traitement »', has(/Ajouter une séance au traitement/, 'dialog')],
      ['Try it: add one, save → it appears here, booking stays open'],
    ],
    note: 'Test patient — saving a séance here is safe.' },
  { title: 'A new multi-séance act', was: '« Traitement en N séances. Ce rendez-vous est la 1re… »',
    setup: async (pg) => { await openBooking(pg, (F1.empty ?? PL).patient); await pickAct(pg, 'Couronne') },
    look: [
      ['Séances drawn, « ce RDV » on séance 1', has(/ce RDV/, 'dialog')],
      ['« Prix du traitement »', has(/Prix du traitement/, 'dialog')],
      ['« Séances » · « Tout en 1 séance »', has(/(?=[^]*Séances)(?=[^]*Tout en 1 séance)/, 'dialog')],
      ['Try « Tout en 1 séance » → folds; « Répartir en 3 séances » brings it back'],
    ] },
  { title: 'Continue an unfinished act', was: 'a « C’est la suite… ? » link → a 2nd window',
    setup: async (pg) => { await openBooking(pg, C.patient); await waitText(pg, dlg(pg), /Non terminée/, 10000) },
    look: [
      ['Card « Non terminée le … » + « Continuer »', has(/(?=[^]*Non terminée le)(?=[^]*Continuer)/, 'dialog')],
      ['Other past séances under « Autre séance précédente… »', has(/Autre séance précédente/, 'dialog')],
    ] },
  { title: '« Continuer » → the row, same window', was: 'a 2nd window over the booking',
    setup: async (pg) => {
      await openBooking(pg, C.patient)
      await waitText(pg, dlg(pg), /Non terminée/, 10000)
      await dlg(pg).getByRole('button', { name: /^Continuer/ }).first().click()
      await waitText(pg, dlg(pg), /suite du/, 8000)
    },
    look: [
      ['Row « suite du … »', has(/suite du/, 'dialog')],
      ['Inline: « Nom de la séance » · « Prix du reste »', has(/(?=[^]*Nom de la séance)(?=[^]*Prix du reste)/, 'dialog')],
      ['Still one window', test(async (pg) => (await pg.locator('[role="dialog"]:visible').count()) === 1)],
    ] },
  { title: 'Continuation door, nothing to continue', was: 'the door vanished when there was nothing to continue',
    setup: async (pg) => {
      await openBooking(pg, (F1.empty ?? PL).patient)
      await waitText(pg, dlg(pg), /Suite d'une séance précédente/, 10000)
      await dlg(pg).getByText(/Suite d'une séance précédente/).first().click()
      await waitText(pg, dlg(pg), /Aucune séance passée/, 6000)
    },
    look: [
      ['« Suite d’une séance précédente… » always there', has(/Suite d'une séance précédente/, 'dialog')],
      ['Opened: « Aucune séance passée pour ce patient. »', has(/Aucune séance passée pour ce patient/, 'dialog')],
    ] },
  { title: 'Edit a RDV: a footer with no trap', was: '« Supprimer » and « Annuler » beside « Enregistrer »',
    setup: async (pg) => { await go(pg, `/appointments?appointmentId=${M.visit2}`); await waitText(pg, dlg(pg), /Enregistrer/, 15000) },
    look: [
      ['Footer: « ⋯ » · « Fermer » · « Enregistrer »', test(footerOk)],
      ['« ⋯ » holds « Annuler le rendez-vous » · « Supprimer »'],
    ] },

  { sec: 'Fiche de soins', title: 'A treatment séance', was: '2 dropdowns « Acte planifié / Étape », « 0,000 DT » on the act, hints under fields',
    setup: (pg) => openFiche(pg, T.patient, T.visit2),
    look: [
      ['Title « Fiche de soins · … »', has(/Fiche de soins/, 'dialog')],
      ['Band « Couronne » + séances, « Empreinte » today', has(/(?=[^]*Couronne)(?=[^]*Empreinte)/, 'dialog')],
      ['Act tag « Inclus dans le traitement »', has(/Inclus dans le traitement/, 'dialog')],
      ['Footer « Payé aujourd’hui » + Prix · Déjà payé · Reste à payer', has(/(?=[^]*Payé (aujourd'hui|à cette séance))(?=[^]*Déjà payé)(?=[^]*Reste à payer)/, 'dialog')],
      ['« Changer ▾ » on the band → « Sans traitement »'],
    ] },
  { title: 'An act added during a treatment séance', was: 'no sign it lands on the devis',
    setup: async (pg) => { await openFiche(pg, T.patient, T.visit2); await addFicheAct(pg, 'Détartrage') },
    look: [
      ['Tag « Ajouté au traitement »', has(/Ajouté au traitement/, 'dialog')],
      ['« par dent · pour tout »', has(/(?=[^]*par dent)(?=[^]*pour tout)/, 'dialog')],
      ['Footer « Prix du traitement » old → new', has(/Prix du traitement/, 'dialog')],
    ],
    note: 'Nothing is saved unless you press « Enregistrer la séance ».' },
  { title: 'Mixed séance: own fee + treatment', was: 'two « Payé » fields with the same name',
    setup: mixedFiche,
    look: [
      ['« Payé (séance) » · « Payé (traitement) »', has(/(?=[^]*Payé \(séance\))(?=[^]*Payé \(traitement\))/, 'dialog')],
      ['« À continuer une autre séance » on the own-fee act', has(/À continuer une autre séance/, 'dialog')],
    ] },
  { title: 'A cheque in one row', was: '3 cheque fields open, half the footer',
    setup: async (pg) => { await mixedFiche(pg); await chooseCheque(pg) },
    look: [['« Chèque : n° · banque · date » — one row, tap to open', has(/Chèque\s*:/, 'dialog')]] },
  { title: 'Prescription: labels, no hints', was: '« Écrivez ce que vous voulez… », « Un médicament se choisit… »',
    setup: async (pg) => {
      await mixedFiche(pg)
      const presc = dlg(pg).getByRole('button', { name: /Prescription/ }).first()
      await presc.scrollIntoViewIfNeeded().catch(() => {})
      await presc.click().catch(() => {})
      await pg.waitForTimeout(500)
      const med = dlg(pg).getByRole('button', { name: /Médicament/ }).first()
      if (await med.count()) { await med.click(); await pg.waitForTimeout(400); await pg.keyboard.type('Amoxicilline', { delay: 40 }); await pg.waitForTimeout(900) }
      const ex = dlg(pg).getByRole('button', { name: /Examen/ }).first()
      if (await ex.count()) { await ex.click(); await pg.waitForTimeout(400); await pg.keyboard.type('Panoramique dentaire', { delay: 40 }); await pg.waitForTimeout(400) }
    },
    look: [
      ['A médicament line + an examen line'],
      ['No instruction sentences', hasnt(/Écrivez ce que vous voulez|Un médicament se choisit dans le catalogue/, 'dialog')],
    ] },

  { sec: 'Lists & more', title: 'Traitements en cours', was: '« étape 2 / 3 », rail « Traitements et devis »',
    setup: async (pg) => { await go(pg, '/treatment-plans'); await waitText(pg, main(pg), /Traitements en cours/i) },
    look: [
      ['Dots + words, no « étape 2 / 3 »', hasnt(/étape \d+ \/ \d+|\d+ \/ \d+ séances?\b/)],
      ['Table: treatment name + « Devis n° … »'],
      ['Rail says « Traitements »'],
    ] },
  { title: 'À clôturer → « Planifier la suite »', was: 'two windows, one after the other',
    setup: clotureSetup,
    look: [
      ['ONE « Nouveau rendez-vous », « suite du » row already in', has(/(?=[^]*Nouveau rendez-vous)(?=[^]*suite du)/, 'dialog')],
      ['Récap names the patient', hasnt(/Patient à choisir/, 'dialog')],
    ] },
  { title: 'Catalogue: protocols as séances', was: '« 3 étapes » text',
    setup: async (pg) => { await go(pg, '/procedure-types'); await waitText(pg, main(pg), /Couronne/) },
    look: [
      ['Each protocol drawn as the séance strip', test(async (pg) => (await main(pg).locator('ol[aria-label]').count()) > 0)],
      ['« Modifier les N séances » opens the séances window'],
    ] },
  { title: 'After a séance: the prompt', was: '« Compléter le dossier médical »', popup: true,
    setup: async (pg) => { await go(pg, '/patients'); await waitText(pg, dlg(pg), /Séance terminée|fiche de soins/i, 9000) },
    look: [
      ['Title « Séance terminée »', has(/Séance terminée/, 'dialog')],
      ['Button « Remplir la fiche de soins »', has(/Remplir la fiche de soins/, 'dialog')],
    ],
    note: 'Real queue (other test patients) — press « Plus tard ».' },
  { title: 'Dark theme', dark: true,
    setup: planPage(F2.multi.plan),
    look: [
      ['Dark theme on', test((pg) => pg.evaluate(() => document.documentElement.classList.contains('dark')))],
      ['Everything readable, no white blocks'],
      ['Open ⋯ or tap a séance to see the popovers'],
    ] },

  { sec: 'Phone · 390 px, touch', phone: true, title: 'Treatment page',
    setup: planPage(M.plan),
    look: [
      ['No sideways scroll', test(noH)],
      ['Séance buttons ≥ 44 px to tap', test(async (pg) => (await minH(pg, main(pg).locator('ol[aria-label] button'))) >= 43.5)],
      ['« Planifier : … » and ⋯ easy to reach'],
    ],
    note: 'Right-hand window. Scroll it with the mouse wheel.' },
  { phone: true, title: 'Six séances',
    setup: planPage(F2.long.plan, /canal/i),
    look: [
      ['The strip scrolls in its own box, faded on the hidden side', test(stripScrolls)],
      ['The page itself does not scroll sideways', test(noH)],
    ] },
  { phone: true, title: 'Patient card',
    setup: async (pg) => {
      await go(pg, `/patients/${M.patient}`)
      await waitText(pg, main(pg), /Couronne/)
      await centre(main(pg).getByText(/Tous les traitements/).last())
    },
    look: [['No sideways scroll', test(noH)], ['« Planifier : … » big, easy to tap']] },
  { phone: true, title: 'Booking a multi-séance act',
    setup: async (pg) => { await openBooking(pg, (F1.empty ?? PL).patient); await pickAct(pg, 'Couronne') },
    look: [
      ['No sideways scroll', test(noH)],
      ['Séance chips ≥ 44 px', test(async (pg) => (await minH(pg, dlg(pg).locator('ol[aria-label] button'))) >= 43.5)],
      ['« Créer le rendez-vous » reachable'],
    ] },
  { phone: true, title: 'Fiche footer', was: 'cheque fields took the acts area',
    setup: async (pg) => { await mixedFiche(pg); await chooseCheque(pg) },
    look: [
      ['No sideways scroll', test(noH)],
      ['Cheque folded to one row', has(/Chèque\s*:/, 'dialog')],
      ['Labels above « Payé », 3 figures in one row'],
      ['« Enregistrer la séance » on screen', test(visibleBtn(/Enregistrer la séance/))],
    ],
    note: 'Last step. Close the windows when you are done.' },
]
{
  let sec = ''
  for (const s of S) { sec = s.sec ?? sec; s.sec = sec }
}

// ─── browser, contexts, windows ───
const browser = await chromium.launch({ channel: 'chrome', headless: DRY, slowMo: DRY ? 0 : 90, args: DRY ? [] : ['--start-maximized'] })
const ctx = await browser.newContext(DRY ? { viewport: { width: 1536, height: 730 }, locale: 'fr-FR' } : { viewport: null, locale: 'fr-FR' })
let routed = true
await ctx.route(PVR, pvr)
let idx = 0
let busy = false
let note = ''
let items = []
let wake = null
const cmds = []
let seq = 0
const view = () => ({ seq: ++seq, i: idx + 1, n: S.length, sec: S[idx].sec, title: S[idx].title, was: S[idx].was, items, note, busy })
const log = (...a) => console.log(new Date().toLocaleTimeString('fr-FR'), ...a)
await ctx.exposeBinding('__tourGet', () => view())
await ctx.exposeBinding('__tourCmd', (_src, c) => { log(`cmd ${c}${busy ? ' (ignored: busy)' : ''}`); if (!busy) { cmds.push(c); wake?.() } })
await ctx.addInitScript(panelInit)

async function signIn(c) {
  const pg = await c.newPage()
  pg.on('dialog', (d) => (d.type() === 'beforeunload' ? d.accept() : d.dismiss()).catch(() => {}))
  await pg.goto(`${WEB}/appointments`, { waitUntil: 'domcontentloaded' })
  await pg.waitForURL(/\/login/, { timeout: 60000 })
  await pg.fill('input[type=email]', ADMIN.email)
  await pg.fill('input[type=password]', ADMIN.password)
  await pg.click("button:has-text('Se connecter')")
  await pg.waitForSelector('input[inputmode=numeric]', { timeout: 30000 })
  // « Rester connecté » — the 30-minute inactivity limit otherwise signs the owner out while she reads a step.
  const keep = pg.getByLabel(/Rester connecté sur cet appareil/).first()
  if (!DRY && (await keep.count())) await keep.check().catch(() => {})
  await pg.fill('input[inputmode=numeric]', await freshCode())
  await pg.waitForURL((u) => !/\/login/.test(u.toString()), { timeout: 45000 })
  await pg.waitForTimeout(1500)
  if (/\/login/.test(pg.url())) throw new Error('still on /login after signing in')
  return pg
}
const page = await signIn(ctx)
const pageErrors = []
page.on('pageerror', (e) => pageErrors.push(String(e).slice(0, 200)))

async function windowOf(pg) {
  const c = await pg.context().newCDPSession(pg)
  const { windowId } = await c.send('Browser.getWindowForTarget')
  return { set: (bounds) => c.send('Browser.setWindowBounds', { windowId, bounds }).catch(() => {}) }
}
const mainWin = DRY ? null : await windowOf(page)
let phone = null
let phoneShown = false
async function phoneOn() {
  if (!phone) {
    setNote('Opening a phone window and signing it in (~40 s)…')
    const pctx = await browser.newContext({ viewport: { width: 390, height: 700 }, isMobile: true, hasTouch: true, locale: 'fr-FR' })
    await pctx.route(PVR, pvr)
    const pg = await signIn(pctx)
    pg.on('pageerror', (e) => pageErrors.push(String(e).slice(0, 200)))
    phone = { ctx: pctx, page: pg, win: DRY ? null : await windowOf(pg) }
  }
  if (!DRY && !phoneShown) {
    const { w, h } = await page.evaluate(() => ({ w: screen.availWidth, h: screen.availHeight }))
    await mainWin.set({ windowState: 'normal' })
    await mainWin.set({ left: 0, top: 0, width: w - 446, height: h })
    await phone.win.set({ windowState: 'normal' })
    await phone.win.set({ left: w - 446, top: 0, width: 446, height: h })
    await phone.page.bringToFront()
  }
  phoneShown = true
}
async function phoneOff() {
  if (!phoneShown) return
  if (!DRY) {
    await phone.win.set({ windowState: 'minimized' })
    await mainWin.set({ windowState: 'maximized' })
    await page.bringToFront()
  }
  phoneShown = false
}
let darkOn = false
async function applyEnv(s) {
  if (s.popup && routed) { await ctx.unroute(PVR, pvr); routed = false }
  if (!s.popup && !routed) { await ctx.route(PVR, pvr); routed = true }
  if (s.dark && !darkOn) {
    await page.evaluate(() => { try { localStorage.setItem('theme', 'dark') } catch {} })
    await page.emulateMedia({ colorScheme: 'dark' })
    darkOn = true
  }
  if (!s.dark && darkOn) {
    await page.evaluate(() => { try { localStorage.setItem('theme', 'system') } catch {} })
    await page.emulateMedia({ colorScheme: null })
    darkOn = false
  }
  if (s.phone) await phoneOn()
  else await phoneOff()
}

const render = () => page.evaluate((s) => window.__tourRender?.(s), view()).catch(() => {})
function setNote(n) { note = n; render() }

async function marks(s, pg) {
  const cache = {}
  const scopes = { main, dialog: dlg, popover, menu, page: (p) => p.locator('body') }
  const text = async (sc) => (cache[sc] ??= (await scopes[sc](pg).innerText({ timeout: 3000 }).catch(() => '')) || '')
  const out = []
  for (const [t, c] of s.look) {
    if (!c) { out.push({ t, m: 'look' }); continue }
    let pass = false
    let seen = ''
    try {
      if (c.fn) pass = Boolean(await c.fn(pg))
      else {
        seen = await text(c.scope)
        pass = c.re.test(seen) !== Boolean(c.not)
      }
    } catch (e) { seen = String(e.message).slice(0, 120) }
    out.push({ t, m: pass ? 'ok' : 'no', seen })
  }
  return out
}

async function run(i) {
  idx = i
  busy = true
  items = S[i].look.map(([t]) => ({ t, m: 'look' }))
  note = 'Setting this screen up…'
  await render()
  const s = S[i]
  let err = ''
  try {
    await applyEnv(s)
    const pg = s.phone ? phone.page : page
    log(`step ${i + 1} setup…`)
    let timer
    await Promise.race([
      s.setup(pg),
      new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('set-up took over 75 s')), 75000) }),
    ]).finally(() => clearTimeout(timer))
    await pg.waitForTimeout(300)
    items = await marks(s, pg)
    if (DRY && s.dryExtra) await s.dryExtra(pg)
  } catch (e) {
    err = String(e.message).split('\n')[0].slice(0, 140)
    items = await marks(s, s.phone && phone ? phone.page : page).catch(() => items)
  }
  log(`step ${i + 1} ${err ? 'FAILED ' + err : 'ready'} · ${items.map((x) => x.m).join(' ')}`)
  note = err ? `The script could not finish setting this up — ↻ to retry, or look around. (${err})` : (s.note ?? '')
  busy = false
  await render()
  if (DRY) {
    const pg = s.phone ? phone.page : page
    console.log(`\n[${i + 1}] ${s.sec} · ${s.title}${err ? `  ⚠ setup: ${err}` : ''}`)
    for (const it of items) console.log(`   ${{ ok: '✓', no: '✗', look: '○' }[it.m]} ${it.t}${it.m === 'no' ? `  ← saw: ${JSON.stringify((it.seen || '').slice(0, 260))}` : ''}`)
    if (SHOTS) await pg.screenshot({ path: join(SHOTS, `${String(i + 1).padStart(2, '0')}.png`) }).catch(() => {})
  }
}

if (DRY) {
  const only = process.argv.find((a) => a.startsWith('--only='))?.slice(7).split(',').map(Number)
  for (let i = 0; i < S.length; i++) if (!only || only.includes(i + 1)) await run(i)
  console.log(`\npageerrors: ${pageErrors.length ? JSON.stringify(pageErrors.slice(0, 5)) : 'none'}`)
  await browser.close()
  process.exit(0)
}

browser.on('disconnected', () => process.exit(0))
page.on('close', () => { browser.close().catch(() => {}); setTimeout(() => process.exit(0), 1500) })
const from = Math.max(1, Math.min(S.length, Number(process.argv.find((a) => a.startsWith('--from='))?.slice(7)) || 1))
await run(from - 1)
log('READY — tour on screen')
// A second channel: write next / prev / redo / a step number into tour-cmd.txt beside this script.
const CMD_FILE = join(here, 'tour-cmd.txt')
setInterval(() => {
  let c = ''
  try { c = readFileSync(CMD_FILE, 'utf8').trim() } catch { return }
  if (!c) return
  try { writeFileSync(CMD_FILE, '') } catch {}
  log(`file cmd ${c}${busy ? ' (ignored: busy)' : ''}`)
  if (!busy) { cmds.push(c); wake?.() }
}, 1000)
for (;;) {
  if (!cmds.length) await new Promise((r) => { wake = r })
  const c = cmds.shift()
  if (c === 'next' && idx < S.length - 1) await run(idx + 1)
  else if (c === 'prev' && idx > 0) await run(idx - 1)
  else if (c === 'redo') await run(idx)
  else if (/^\d+$/.test(c) && Number(c) >= 1 && Number(c) <= S.length) await run(Number(c) - 1)
}
