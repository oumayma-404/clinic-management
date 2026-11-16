// QA walk — features/camera-photo-import/qa/plan.md (run 2 and later). One headless launch, every scenario, every
// failure collected: nothing throws out of a scenario and nothing returns early. Re-run THIS file after a fix.
//
// Needs the isolated stack (web :3099 → API :5099, DB clinic_camera_sd) and the fixtures built by the scratchpad's
// sd-cards.mjs / sd-setup.mjs / qa-cards.mjs. Only Windows' « choose a folder » dialog is stood in for: the page
// reads the folders through its own reader.
//   QA_HOME=<scratchpad> QA_EMAIL=… QA_PASSWORD=… QA_TOTP=… node walk.mjs
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HOME = process.env.QA_HOME;                       // the scratchpad holding playwright-core, the ids, the cards
if (!HOME || !process.env.QA_EMAIL || !process.env.QA_PASSWORD || !process.env.QA_TOTP) {
  console.error('Set QA_HOME, QA_EMAIL, QA_PASSWORD and QA_TOTP.'); process.exit(2);
}
const { chromium } = createRequire(path.join(HOME, 'noop.js'))('playwright-core');
const HERE = path.dirname(fileURLToPath(import.meta.url));
const RUN = process.env.QA_RUN || '2';
const SHOTS = path.join(HERE, 'shots', `run-${RUN}`);
const BASE = 'http://localhost:3099';
const API = 'http://localhost:5099/api';
const DB = 'clinic_camera_sd';
const DESKTOP = 'C:\\Users\\Oumayma Benkhalifa\\Desktop';
const CARDS = {
  canon: path.join(DESKTOP, 'Carte SD démo'),
  nikon: path.join(DESKTOP, 'Carte SD démo 2 (Nikon)'),
  wrong: path.join(DESKTOP, 'Scans ordonnances (pas une carte)'),
  rawonly: path.join(HOME, 'qa-cards', 'raw-only'),
  emptydcim: path.join(HOME, 'qa-cards', 'empty-dcim'),
  clockreset: path.join(HOME, 'qa-cards', 'clock-reset'),
};
const SAMPLES = path.join(DESKTOP, 'clinic-management', 'follow-up', 'decoder-samples');
const NADIA = '9ed3d280-e3a1-4f11-ad19-8c993dd0da94';
const ids = JSON.parse(fs.readFileSync(path.join(HOME, 'sd-ids.json'), 'utf8')).ids;
fs.rmSync(SHOTS, { recursive: true, force: true });
fs.mkdirSync(SHOTS, { recursive: true });

// ── Outcomes ───────────────────────────────────────────────────────────────────────────────────────────────────
const results = [];
const failed = new Set();
const ok = (id, m) => { console.log(`  ✅ [${id}] ${m}`); results.push({ id, outcome: '✅', m }); };
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); results.push({ id, outcome: '❌', m, d }); failed.add(id); };
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); results.push({ id, outcome: '⏭', m: why }); failed.add(id); };
const check = (id, cond, m, d = '') => (cond ? ok(id, m) : bad(id, m, d));
let failShot = async () => {};
// QA_ONLY=D6,D5 re-drives named rows after a probe fix, on the data a full drive left behind. Never the gate.
const ONLY = process.env.QA_ONLY ? process.env.QA_ONLY.split(',') : null;
async function scenario(id, needs, fn) {
  if (ONLY && !ONLY.includes(id)) return;
  const blocked = needs.filter((n) => failed.has(n));
  if (blocked.length) return skip(id, `blocked by ${blocked.join(', ')}`);
  console.log(`— ${id}`);
  const before = results.filter((r) => r.outcome === '❌').length;
  try { await fn(); } catch (e) { bad(id, 'threw', String(e?.message || e).split('\n')[0]); }
  if (results.filter((r) => r.outcome === '❌').length > before) await failShot(id).catch(() => {});
}
const sql = (q) => execSync(`docker exec clinic-postgres psql -U clinic_user -d ${DB} -Atc ${JSON.stringify(q)}`).toString().trim();

function totp() {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = '';
  for (const c of process.env.QA_TOTP) bits += A.indexOf(c).toString(2).padStart(5, '0');
  const bytes = Buffer.from((bits.match(/.{8}/g) || []).map((b) => parseInt(b, 2)));
  const counter = Math.floor(Date.now() / 1000 / 30);
  const buf = Buffer.alloc(8);
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0); buf.writeUInt32BE(counter >>> 0, 4);
  const h = crypto.createHmac('sha1', bytes).update(buf).digest();
  const o = h[h.length - 1] & 0xf;
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, '0');
}

// The folder dialog's stand-in: a handle onto a folder served by the route below.
const INIT = String.raw`(() => {
  const real = window.showDirectoryPicker;
  const fileHandle = (card, rel, node) => ({ kind: 'file', name: node.name, async getFile() {
    const res = await fetch('/__qa-card/' + card + '/' + rel.split('/').map(encodeURIComponent).join('/'));
    return new File([await res.blob()], node.name, { type: node.type, lastModified: node.mtime });
  } });
  const dirHandle = (card, rel, node) => ({ kind: 'directory', name: node.name, async *values() {
    for (const child of node.children) {
      const childRel = rel ? rel + '/' + child.name : child.name;
      yield child.kind === 'directory' ? dirHandle(card, childRel, child) : fileHandle(card, childRel, child);
    }
  } });
  window.showDirectoryPicker = async function (options) {
    const card = window.__qaPickNext;
    if (!card) return real.call(window, options);
    window.__qaPickNext = null;
    return dirHandle(card, '', await (await fetch('/__qa-card/' + card + '/__tree')).json());
  };
})();`;

function treeOf(dir) {
  const stat = fs.statSync(dir);
  return { kind: 'directory', name: path.basename(dir), mtime: stat.mtimeMs, children: fs.readdirSync(dir).sort().map((name) => {
    const full = path.join(dir, name);
    const s = fs.statSync(full);
    if (s.isDirectory()) return treeOf(full);
    return { kind: 'file', name, mtime: Math.round(s.mtimeMs), type: /\.jpe?g$/i.test(name) ? 'image/jpeg' : '' };
  }) };
}

(async () => {
  const profile = path.join(HOME, 'qa-profile');
  fs.rmSync(profile, { recursive: true, force: true });
  const ctx = await chromium.launchPersistentContext(profile, { channel: 'chrome', headless: true, viewport: { width: 1536, height: 730 } });
  await ctx.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await ctx.route('**/api/feedback/prompt', (r) => (r.request().method() === 'GET' ? r.fulfill({ status: 404, contentType: 'application/json', body: '{"error":"none"}' }) : r.continue()));
  await ctx.route('**/__qa-card/**', async (route) => {
    const parts = new URL(route.request().url()).pathname.split('/').slice(2).map(decodeURIComponent);
    const root = CARDS[parts[0]];
    if (!root) return route.fulfill({ status: 404, body: '' });
    if (parts[1] === '__tree') return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(treeOf(root)) });
    const file = path.join(root, ...parts.slice(1));
    if (!file.startsWith(root) || !fs.existsSync(file)) return route.fulfill({ status: 404, body: '' });
    return route.fulfill({ status: 200, path: file, contentType: /\.jpe?g$/i.test(file) ? 'image/jpeg' : 'application/octet-stream' });
  });
  let cutUploads = 0;
  await ctx.route('**/files/upload', (route) => {
    if (cutUploads > 0 && route.request().method() === 'POST') { cutUploads--; return route.abort('internetdisconnected'); }
    return route.continue();
  });
  await ctx.addInitScript(INIT);
  const page = ctx.pages()[0] || (await ctx.newPage());
  const consoleErrors = [];
  page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text().slice(0, 200)); });
  page.on('pageerror', (e) => consoleErrors.push('pageerror: ' + String(e.message).slice(0, 200)));

  // ── Helpers ──
  failShot = async (id) => { await page.screenshot({ path: path.join(SHOTS, `FAIL-${id}-${Date.now() % 100000}.png`), timeout: 15000 }); };
  const shot = async (name) => { await page.mouse.move(2, 2).catch(() => {}); await page.waitForTimeout(350); await page.screenshot({ path: path.join(SHOTS, `${name}.png`), timeout: 20000 }); };
  const size = async (w, h) => { await page.setViewportSize({ width: w, height: h }); await page.waitForTimeout(500); };
  const group = (range) => page.locator(`li[aria-label="Groupe de ${range}"]`);
  const groupWith = (text) => page.locator('li[aria-label^="Groupe de "]', { hasText: text });
  const ranger = page.getByRole('button', { name: /^Ranger \d+ photos?$/ });
  const pickCard = async (card) => {
    await page.evaluate((c) => { window.__qaPickNext = c; }, card);
    await page.getByRole('button', { name: /Choisir le dossier de la carte/ }).click();
  };
  const backToSource = async () => {
    for (const name of ['Choisir une autre carte', 'Autre carte']) {
      const b = page.getByRole('button', { name, exact: true });
      if (await b.count() && await b.first().isVisible()) {
        await b.first().click();
        const ask = page.getByRole('alertdialog');
        if (await ask.waitFor({ timeout: 1500 }).then(() => true).catch(() => false)) await ask.getByRole('button', { name: 'Abandonner' }).click();
        break;
      }
    }
    await page.getByRole('button', { name: /Choisir le dossier de la carte/ }).waitFor({ timeout: 20000 });
  };
  /** Sideways scroll of the page scroller (`main`) and of the document, in px. */
  const overflow = () => page.evaluate(() => {
    const main = document.querySelector('main');
    return { main: main ? main.scrollWidth - main.clientWidth : -1, doc: document.documentElement.scrollWidth - document.documentElement.clientWidth };
  });
  let cdp = null;
  const setCoarse = async (on, w = 820, h = 1180) => {
    cdp = cdp || await ctx.newCDPSession(page);
    if (on) {
      await cdp.send('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 2, mobile: true });
      await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 });
    } else {
      await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: false });
      await cdp.send('Emulation.clearDeviceMetricsOverride');
    }
    await page.waitForTimeout(600);
    const coarse = await page.evaluate(() => matchMedia('(pointer: coarse)').matches);
    if (coarse !== on) throw new Error(`coarse emulation did not take (pointer: coarse = ${coarse})`);
  };
  /** Every visible button in `scope` whose tappable box (its own, or its `.touch-target` overlay) is under 44 px. */
  const smallTargets = (scope) => scope.evaluateAll((els) => els.flatMap((root) => [...root.querySelectorAll('button, a[href], [role="button"], input:not([type=hidden]), [role="combobox"]')].map((el) => {
    const r = el.getBoundingClientRect();
    if (!r.width || !r.height || getComputedStyle(el).visibility === 'hidden') return null;
    const after = getComputedStyle(el, '::after');
    const ow = parseFloat(after.width) || 0, oh = parseFloat(after.height) || 0;
    const w = Math.max(r.width, el.classList.contains('touch-target') ? ow : 0), h = Math.max(r.height, el.classList.contains('touch-target') ? oh : 0);
    const name = (el.getAttribute('aria-label') || el.textContent || el.getAttribute('placeholder') || el.tagName).trim().replace(/\s+/g, ' ').slice(0, 40);
    return w < 43.5 || h < 43.5 ? `${name} ${Math.round(w)}×${Math.round(h)}` : null;
  }).filter(Boolean)));
  const inViewport = (loc) => loc.evaluate((el) => { const r = el.getBoundingClientRect(); return r.top >= 0 && r.bottom <= window.innerHeight + 0.5 && r.left >= 0 && r.right <= window.innerWidth + 0.5; });
  const topmostAt = (loc) => loc.evaluate((el) => {
    const box = el.closest('.sticky') || el;
    const r = el.getBoundingClientRect();
    const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
    return Boolean(hit && (hit === el || el.contains(hit) || box.contains(hit)));
  });
  const groupText = async (loc) => (await loc.first().innerText()).replace(/\s+/g, ' ');
  let token = null;
  const api = async (method, url, body, form) => {
    if (!token) token = await page.evaluate(async () => (await (await fetch('/bff/auth/token')).json()).accessToken);
    const headers = { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const res = await fetch(API + url, { method, headers, body: form ?? (body !== undefined ? JSON.stringify(body) : undefined) });
    const text = await res.text();
    let json = null; try { json = text ? JSON.parse(text) : null; } catch {}
    return { status: res.status, json, text };
  };

  // ── Sign in once ──
  await page.goto(`${BASE}/photos/import`);
  if (page.url().includes('/login')) {
    await page.fill('input[type=email]', process.env.QA_EMAIL);
    await page.fill('input[type=password]', process.env.QA_PASSWORD);
    await page.click('button:has-text("Se connecter")');
    await page.waitForSelector('input[inputmode=numeric]', { timeout: 30000 });
    await page.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000);
    await page.fill('input[inputmode=numeric]', totp());
    await page.waitForURL((u) => !u.pathname.includes('/login'), { timeout: 60000 });
    await page.goto(`${BASE}/photos/import`);
  }
  await page.getByRole('button', { name: /Choisir le dossier de la carte/ }).waitFor({ timeout: 60000 });
  console.log('signed in, on', page.url());
  const filesBefore = Number(sql(`select count(*) from "PatientFiles" where "CaptureKey" is not null`));

  // ═══ Tier B — the three empty states ═══
  await scenario('B1', [], async () => {
    await size(320, 640);
    await pickCard('wrong');
    await page.getByText('Aucune photo JPEG sur la carte').waitFor({ timeout: 60000 });
    const t = await page.locator('main').innerText();
    check('B1', /Les fichiers lus ne sont pas des photos/.test(t), 'PDF folder → « Aucune photo JPEG sur la carte » + its reason');
    const o = await overflow();
    check('B1', o.main <= 1 && o.doc <= 1, `no sideways scroll at 320 (main ${o.main}, doc ${o.doc})`);
    await shot('B1-wrong-folder-320');
    await size(1536, 730);
    await backToSource();
  });
  await scenario('B2', [], async () => {
    await pickCard('rawonly');
    await page.getByText('Aucune photo JPEG sur la carte').waitFor({ timeout: 60000 });
    const t = await page.locator('main').innerText();
    check('B2', /RAW/.test(t), 'RAW-only card → « Aucune photo JPEG sur la carte », RAW named', t.match(/[^\n]*RAW[^\n]*/)?.[0] || t.slice(0, 160));
    await backToSource();
  });
  await scenario('B3', [], async () => {
    await pickCard('emptydcim');
    await page.getByText(/Aucune photo (dans ce dossier|JPEG sur la carte)/).waitFor({ timeout: 60000 });
    const t = await page.locator('main').innerText();
    check('B3', /Aucune photo dans ce dossier/.test(t) && !/ignoré/.test(t), 'DCIM with only CANONMSC → « Aucune photo dans ce dossier », bookkeeping silent', t.slice(0, 200).replace(/\n/g, ' | '));
    await backToSource();
  });

  // ═══ Tier A — the Canon card ═══
  let canonLoaded = false;
  await scenario('A1', [], async () => {
    const t0 = Date.now();
    await pickCard('canon');
    await ranger.waitFor({ timeout: 180000 });
    canonLoaded = true;
    const t = await page.locator('main').innerText();
    ok('A1', `review in ${((Date.now() - t0) / 1000).toFixed(1)} s`);
    check('A1', /6 photos déjà rangées/.test(t), '« 6 photos déjà rangées »');
    check('A1', /Afficher aussi 6 photos plus anciennes/.test(t), '« Afficher aussi 6 photos plus anciennes »');
    const skippedLine = (t.match(/[^\n]*(RAW|ignoré)[^\n]*/g) || []).join(' | ');
    check('A1', /2 RAW/.test(skippedLine) && !/37 RAW/.test(skippedLine) && !/autres? ignorés?/.test(skippedLine), 'skipped line counts the 2 lone RAW only', skippedLine);
    await shot('A1-overview-1536x730');
  });
  await scenario('B7', ['A1'], async () => {
    check('B7', await ranger.isDisabled(), '« Ranger » disabled while groups ask');
    const bar = await page.getByText(/Choisissez le patient de \d+ groupes?/).first().innerText().catch(() => '');
    check('B7', /Choisissez le patient de \d+ groupes?/.test(bar), 'the bar names the groups still asking', bar);
    await page.getByRole('button', { name: 'Voir', exact: true }).click();
    await page.waitForTimeout(600);
    ok('B7', '« Voir » pressed (scrolls to the first group asking)');
    await page.evaluate(() => document.querySelector('main')?.scrollTo(0, 0));
  });
  await scenario('C6', ['A1'], async () => {
    await page.evaluate(() => document.querySelector('main')?.scrollTo(0, 0));
    await page.waitForTimeout(300);
    check('C6', await inViewport(ranger) && await topmostAt(ranger), '« Ranger » on screen and uncovered at 1536×730 from the top of the list');
    await size(390, 844);
    await page.evaluate(() => document.querySelector('main')?.scrollTo(0, 0));
    await page.waitForTimeout(300);
    check('C6', await inViewport(ranger) && await topmostAt(ranger), '« Ranger » on screen and uncovered at 390×844 (bottom bar)');
    await shot('C6-ranger-390');
    await size(1536, 730);
  });
  await scenario('C5', ['A1'], async () => {
    await setCoarse(true);
    const small = await smallTargets(page.locator('ol[aria-label="Groupes de photos"] > li').filter({ hasText: /À vérifier/ }).first());
    const smallSure = await smallTargets(group('08:36 – 08:39'));
    check('C5', small.length === 0 && smallSure.length === 0, 'burst cards: every control ≥ 44 px on a coarse pointer', [...small, ...smallSure].join(' · '));
    await page.getByRole('button', { name: 'Voir la photo de 08:36' }).first().click();
    await page.getByRole('dialog').waitFor({ timeout: 20000 });
    // Run 3 probe fix: the dialog opens with a zoom-in from 95 %, so a box measured mid-animation reads 44 × 0,95 ≈ 42.
    await page.getByRole('dialog').evaluate((el) => Promise.all(el.getAnimations({ subtree: true }).map((a) => a.finished)).catch(() => {}));
    const smallViewer = await smallTargets(page.getByRole('dialog'));
    check('C5', smallViewer.length === 0, 'photo viewer: every control ≥ 44 px on a coarse pointer', smallViewer.join(' · '));
    await shot('C5-viewer-820-coarse');
    await page.keyboard.press('Escape');
    await setCoarse(false);
    await size(1536, 730);
  });
  await scenario('D4', ['A1'], async () => {
    await page.getByRole('button', { name: /Afficher aussi 6 photos plus anciennes/ }).click();
    await page.locator('li[aria-label^="Groupe de 10:12"]').first().waitFor({ timeout: 30000 });
    ok('D4', 'older photos shown (the 24 October visit appears)');
    await page.getByRole('button', { name: 'Masquer les photos plus anciennes' }).click();
    await page.waitForTimeout(800);
    check('D4', (await page.locator('li[aria-label^="Groupe de 10:12"]').count()) === 0, 'and hidden again');
  });
  await scenario('A2', ['A1'], async () => {
    const t = await groupText(group('08:36 – 08:39'));
    check('A2', /Amira Ben Salah/.test(t) && /Sûr/.test(t), '08:36 – 08:39 → Amira Ben Salah, « Sûr »', t.slice(0, 160));
  });
  await scenario('A3', ['A1'], async () => {
    await page.getByRole('button', { name: 'Voir la photo de 08:37' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.waitFor({ timeout: 20000 });
    check('A3', /Photo de 08:37/.test(await dialog.innerText()), 'viewer opens on « Photo de 08:37 »');
    const img = await dialog.locator('img').first().evaluate((el) => ({ w: el.naturalWidth, h: el.naturalHeight })).catch(() => ({ w: 0, h: 0 }));
    check('A3', img.w > 0 && img.h > 0, `the photo is painted (${img.w}×${img.h})`);
    await dialog.getByRole('button', { name: 'Écarter cette photo' }).click();
    await dialog.getByRole('button', { name: 'Reprendre cette photo' }).waitFor({ timeout: 10000 });
    ok('A3', '« Écarter » → the button turns into « Reprendre cette photo »');
    await shot('A3-viewer-1536x730');
    const footerVisible = await inViewport(dialog.getByRole('button', { name: 'Fermer' }).last());
    check('A3', footerVisible, '« Fermer » inside the viewport at 1536×730');
    await dialog.getByRole('button', { name: 'Fermer' }).last().click();
    await page.waitForTimeout(400);
    const t = await groupText(group('08:36 – 08:39'));
    check('A3', /4 photos gardées sur 5/.test(t), 'group reads « 4 photos gardées sur 5 »', t.slice(0, 160));
    check('A3', (await page.getByRole('button', { name: /^Voir la photo de 08:37 \(\d+ sur 5\), écartée$/ }).count()) === 1, 'thumbnail named « … (n sur 5), écartée »');
  });
  await scenario('B9', ['A1'], async () => {
    await page.getByRole('button', { name: 'Voir la photo de 08:36' }).first().click();
    const dialog = page.getByRole('dialog');
    await dialog.waitFor({ timeout: 20000 });
    const focusOnTitle = await page.evaluate(() => document.activeElement?.closest('[role="dialog"]') !== null);
    const t0 = await dialog.innerText();
    await page.keyboard.press('ArrowRight');
    await page.waitForTimeout(300);
    const t1 = await dialog.innerText();
    check('B9', /1 \/ 5/.test(t0) && /2 \/ 5/.test(t1), '→ moves to the next photo (« 1 / 5 » → « 2 / 5 »)', `${t0.slice(0, 60)} → ${t1.slice(0, 60)}`);
    await page.keyboard.press('ArrowLeft');
    await page.waitForTimeout(300);
    check('B9', /1 \/ 5/.test(await dialog.innerText()), '← moves back');
    await page.keyboard.press('Escape');
    await page.waitForTimeout(400);
    check('B9', (await page.getByRole('dialog').count()) === 0, 'Escape closes the viewer');
    const back = await page.evaluate(() => document.activeElement?.getAttribute('aria-label') || '');
    check('B9', /Voir la photo de 08:3[67]/.test(back), 'focus returns to a thumbnail of the group', back);
    check('B9', focusOnTitle, 'focus is inside the viewer when it opens');
  });
  await scenario('C2', ['A1'], async () => {
    for (const [w, h] of [[320, 640], [390, 844], [1536, 730]]) {
      await size(w, h);
      await page.getByRole('button', { name: 'Voir la photo de 08:36' }).first().click();
      const dialog = page.getByRole('dialog');
      await dialog.waitFor({ timeout: 20000 });
      await page.waitForTimeout(400);
      const buttons = ['Fermer', 'Écarter cette photo', 'Ranger chez un autre patient'];
      const outside = [];
      for (const name of buttons) {
        const b = dialog.getByRole('button', { name }).last();
        if (!(await b.count())) { outside.push(`${name}: absent`); continue; }
        if (!(await inViewport(b))) outside.push(`${name}: outside`);
      }
      const o = await overflow();
      check('C2', outside.length === 0 && o.doc <= 1, `viewer at ${w}×${h}: footer buttons on screen, no sideways scroll`, outside.join(', ') + ` doc ${o.doc}`);
      await shot(`C2-viewer-${w}x${h}`);
      await page.keyboard.press('Escape');
      await page.waitForTimeout(300);
    }
  });
  await scenario('C3', ['A1'], async () => {
    await size(740, 380);
    await page.getByRole('button', { name: 'Voir la photo de 08:36' }).first().click();
    const dialog = page.getByRole('dialog');
    await dialog.waitFor({ timeout: 20000 });
    await page.waitForTimeout(400);
    const close = dialog.getByRole('button', { name: 'Fermer' }).last();
    await close.scrollIntoViewIfNeeded().catch(() => {});
    const img = dialog.locator('img').first();
    const imgBox = await img.boundingBox();
    check('C3', Boolean(imgBox) && imgBox.height > 40 && await inViewport(close), 'landscape phone 740×380: image shown and « Fermer » reachable', JSON.stringify(imgBox));
    await shot('C3-viewer-740x380');
    await page.keyboard.press('Escape');
    await size(1536, 730);
  });
  await scenario('A4', ['A1'], async () => {
    const k = await groupText(group('09:52 – 09:54'));
    const l = await groupText(group('10:21 – 10:22'));
    check('A4', /Karim Hamdi/.test(k) && /Sûr/.test(k), 'late arrival: 09:52 – 09:54 → Karim Hamdi, « Sûr »', k.slice(0, 120));
    check('A4', /Leïla Trabelsi/.test(l) && /Sûr/.test(l), 'no practitioner: 10:21 – 10:22 → Leïla Trabelsi, « Sûr »', l.slice(0, 120));
  });
  await scenario('A5', ['A1'], async () => {
    const t = await groupText(group('11:08 – 11:09'));
    check('A5', /Deux patients étaient au fauteuil/.test(t) && /À vérifier/.test(t), 'overlap asks', t.slice(0, 160));
    await group('11:08 – 11:09').getByRole('button', { name: /Ines Chaabane/ }).click();
    await page.waitForTimeout(500);
    check('A5', /Choisi/.test(await groupText(group('11:08 – 11:09'))), 'after the pick: « Choisi », Ines');
  });
  await scenario('B15', ['A5'], async () => {
    await page.getByRole('button', { name: 'Autre carte', exact: true }).click();
    const ask = page.getByRole('alertdialog');
    await ask.waitFor({ timeout: 10000 });
    check('B15', /Abandonner le tri de/.test(await ask.innerText()), '« Autre carte » over decisions asks « Abandonner le tri de … ? »');
    await ask.getByRole('button', { name: 'Continuer le tri' }).click();
    await page.waitForTimeout(400);
    check('B15', /Choisi/.test(await groupText(group('11:08 – 11:09'))), '« Continuer le tri » keeps every choice');
  });
  await scenario('B5', ['A5'], async () => {
    await page.getByRole('button', { name: /Afficher aussi 6 photos plus anciennes/ }).click();
    await page.waitForTimeout(1200);
    await page.getByRole('button', { name: 'Masquer les photos plus anciennes' }).click();
    await page.waitForTimeout(1200);
    const t = await groupText(group('11:08 – 11:09'));
    check('B5', /Ines Chaabane/.test(t) && /Choisi/.test(t), 'the Ines choice survives show/hide « plus anciennes »', t.slice(0, 160));
  });
  await scenario('A6', ['A1'], async () => {
    await group('12:08 – 12:18').getByRole('button', { name: /^Couper à/ }).click();
    await group('12:16 – 12:18').waitFor({ timeout: 20000 });
    const a = await groupText(group('12:08 – 12:14'));
    const b = await groupText(group('12:16 – 12:18'));
    check('A6', /Mehdi Jaziri/.test(a) && /Sarra Ferchichi/.test(b), '« Couper à » → 12:08 – 12:14 Mehdi, 12:16 – 12:18 Sarra', `${a.slice(0, 80)} || ${b.slice(0, 80)}`);
    await page.getByRole('button', { name: /Afficher aussi 6 photos plus anciennes/ }).click();
    await page.waitForTimeout(1200);
    await page.getByRole('button', { name: 'Masquer les photos plus anciennes' }).click();
    await page.waitForTimeout(1200);
    check('A6', (await group('12:08 – 12:14').count()) === 1 && (await group('12:16 – 12:18').count()) === 1, 'the cut stands after a recount (« plus anciennes » shown and hidden)');
  });
  await scenario('A7', ['A1'], async () => {
    const t = await groupText(group('13:12'));
    check('A7', /Hichem Ayari/.test(t) && /Sûr/.test(t), 'walk-in fiche: 13:12 → Hichem Ayari, « Sûr »', t.slice(0, 120));
  });
  await scenario('B8', ['A1'], async () => {
    await group('17:12').getByRole('button', { name: /^Voir la photo/ }).first().click();
    const dialog = page.getByRole('dialog');
    await dialog.waitFor({ timeout: 20000 });
    check('B8', (await dialog.getByRole('button', { name: 'Ranger chez un autre patient' }).count()) === 0, 'single-photo group (17:12): no « Ranger chez un autre patient »');
    await page.keyboard.press('Escape');
    await page.waitForTimeout(300);
  });
  await scenario('C9', ['A1'], async () => {
    await group('14:36 – 14:37').getByRole('button', { name: 'Autre patient…' }).click();
    const input = group('14:36 – 14:37').getByPlaceholder('Nom ou téléphone du patient');
    await input.fill('Leila');
    await group('14:36 – 14:37').getByRole('button', { name: /Leïla Trabelsi/ }).waitFor({ timeout: 20000 }).catch(() => {});
    check('C9', (await group('14:36 – 14:37').getByRole('button', { name: /Leïla Trabelsi/ }).count()) > 0, '« Leila » (no accent) finds « Leïla Trabelsi »');
  });
  await scenario('A8', ['A1'], async () => {
    const input = group('14:36 – 14:37').getByPlaceholder('Nom ou téléphone du patient');
    if (!(await input.count())) await group('14:36 – 14:37').getByRole('button', { name: 'Autre patient…' }).click();
    await input.fill('Saidi');
    await group('14:36 – 14:37').getByRole('button', { name: /Rim Saidi/ }).click({ timeout: 20000 });
    await page.waitForTimeout(500);
    const t = await groupText(group('14:36 – 14:37'));
    check('A8', /Rim Saidi/.test(t) && /Choisi/.test(t), 'no visit → « Autre patient… » → Rim Saidi chosen', t.slice(0, 140));
  });
  await scenario('A9', ['A1'], async () => {
    await page.getByRole('button', { name: 'Voir la photo de 15:10' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.waitFor({ timeout: 20000 });
    await dialog.getByRole('button', { name: 'Ranger chez un autre patient' }).click();
    await group('15:10').waitFor({ timeout: 20000 });
    const t = await groupText(group('15:10'));
    check('A9', /Photo sortie de son groupe/.test(t), 'moved photo → its own card « Photo sortie de son groupe. »', t.slice(0, 160));
    check('A9', /Bilel Khemiri/.test(t) && !/Olfa Mansour/.test(t), 'offers Bilel, never Olfa', t.slice(0, 200));
    const olfa = await groupText(groupWith('Olfa Mansour'));
    check('A9', /3 photos/.test(olfa), 'Olfa keeps 3 photos', olfa.slice(0, 120));
  });
  await scenario('B10', ['A9'], async () => {
    await group('15:10').getByRole('button', { name: 'Remettre dans son groupe' }).click();
    await page.waitForTimeout(800);
    check('B10', (await group('15:10').count()) === 0 && /4 photos/.test(await groupText(groupWith('Olfa Mansour'))), '« Remettre dans son groupe » → back with Olfa (4 photos)');
    await page.getByRole('button', { name: 'Voir la photo de 15:10' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Ranger chez un autre patient' }).click();
    await group('15:10').getByRole('button', { name: /Bilel Khemiri/ }).click({ timeout: 20000 });
    await page.waitForTimeout(500);
    check('B10', /Bilel Khemiri/.test(await groupText(group('15:10'))), 'moved again and given to Bilel');
  });
  await scenario('A10', ['A1'], async () => {
    const g = group('16:25 – 16:26');
    await g.getByRole('button', { name: 'Autre patient…' }).click();
    await g.getByPlaceholder('Nom ou téléphone du patient').pressSequentially('Sonia Mejri', { delay: 30 });
    await g.getByText('Aucun patient pour cette recherche.').waitFor({ timeout: 20000 });
    ok('A10', '« Aucun patient pour cette recherche. »');
    await g.getByRole('button', { name: /Nouveau patient/ }).click();
    const first = await g.getByLabel(/^Prénom/).inputValue().catch(() => '');
    const last = await g.getByLabel(/^Nom/).first().inputValue().catch(() => '');
    check('A10', first === 'Sonia' && last === 'Mejri', 'form seeded Prénom Sonia / Nom Mejri', `${first}/${last}`);
  });
  await scenario('C4', ['A10'], async () => {
    for (const [w, h] of [[320, 640], [1536, 730]]) {
      await size(w, h);
      const g = group('16:25 – 16:26');
      await g.getByRole('button', { name: 'Créer et choisir' }).scrollIntoViewIfNeeded();
      const o = await overflow();
      const cardBox = await g.boundingBox();
      const btn = await g.getByRole('button', { name: 'Créer et choisir' }).boundingBox();
      const inside = cardBox && btn && btn.x >= cardBox.x - 0.5 && btn.x + btn.width <= cardBox.x + cardBox.width + 0.5;
      check('C4', o.main <= 1 && o.doc <= 1 && inside, `new-patient form at ${w}×${h}: no sideways scroll, buttons inside the card`, `main ${o.main} doc ${o.doc}`);
      await shot(`C4-new-patient-${w}x${h}`);
    }
    await setCoarse(true);
    const small = await smallTargets(group('16:25 – 16:26'));
    check('C5', small.length === 0, 'new-patient form: every control ≥ 44 px on a coarse pointer', small.join(' · '));
    await setCoarse(false);
    await size(1536, 730);
  });
  await scenario('B11', ['A10'], async () => {
    const g = group('16:25 – 16:26');
    await g.getByRole('button', { name: 'Annuler', exact: true }).click();
    await page.waitForTimeout(400);
    check('B11', (await g.getByPlaceholder('Nom ou téléphone du patient').count()) === 1, '« Annuler » → back to the search');
    const kept = await g.getByPlaceholder('Nom ou téléphone du patient').inputValue().catch(() => '');
    check('B11', kept === 'Sonia Mejri', '« Annuler » keeps the name that was typed', `search box holds « ${kept} »`);
    check('B11', sql(`select count(*) from "Patients" where "LastName"='Mejri'`) === '0', 'nothing created');
  });
  await scenario('A10b', ['A10'], async () => {
    const g = group('16:25 – 16:26');
    const box = g.getByPlaceholder('Nom ou téléphone du patient');
    if ((await box.inputValue().catch(() => '')) !== 'Sonia Mejri') await box.fill('Sonia Mejri');
    await g.getByRole('button', { name: /Nouveau patient/ }).click({ timeout: 20000 });
    await g.getByRole('button', { name: 'Créer et choisir' }).click();
    await g.getByText('Choisi', { exact: true }).waitFor({ timeout: 20000 });
    const toast = await page.getByText(/Patient créé : Sonia Mejri/).first().isVisible().catch(() => false);
    check('A10', toast, 'created and chosen; toast « Patient créé : Sonia Mejri »');
    check('A10', sql(`select count(*) from "Patients" where "FirstName"='Sonia' and "LastName"='Mejri'`) === '1', 'exactly one Sonia Mejri');
  });
  await scenario('B12', ['A1'], async () => {
    const g = group('17:12');
    await g.getByRole('button', { name: 'Autre patient…' }).click();
    await g.getByPlaceholder('Nom ou téléphone du patient').fill('Rim Saidi');
    await g.getByRole('button', { name: /Rim Saidi/ }).first().waitFor({ timeout: 20000 });
    await g.getByRole('button', { name: /Nouveau patient/ }).click({ timeout: 20000 });
    await g.getByRole('button', { name: 'Créer et choisir' }).waitFor({ timeout: 10000 });
    await shot('B12-form-before-create');
    await g.getByRole('button', { name: 'Créer et choisir' }).click();
    const question = page.getByRole('alertdialog');
    await question.waitFor({ timeout: 20000 });
    // Run 3 probe fix: the shared duplicate question (patients/duplicate-patient-dialog.tsx) reads « Créer quand même un
    // dossier pour … ? » with the server's own sentence, and its way out is « Choisir un patient existant » — back to
    // the search, by design — not an « Annuler » that keeps the form.
    const asked = (await question.innerText()).replace(/\s+/g, ' ');
    check('B12', /Créer quand même un dossier pour Rim Saidi/.test(asked) && /existe déjà/.test(asked), 'duplicate → « Créer quand même un dossier pour Rim Saidi ? » with the server\'s reason', asked.slice(0, 160));
    await shot('B12-duplicate-question');
    await question.getByRole('button', { name: 'Choisir un patient existant' }).click();
    await g.getByPlaceholder('Nom ou téléphone du patient').waitFor({ timeout: 20000 });
    await page.waitForTimeout(400);
    const kept = await g.getByPlaceholder('Nom ou téléphone du patient').inputValue();
    check('B12', kept === 'Rim Saidi' && (await g.getByRole('button', { name: /Rim Saidi/ }).count()) > 0, '« Choisir un patient existant » → back to the search, the name kept, Rim Saidi offered', `search « ${kept} »`);
    check('B12', sql(`select count(*) from "Patients" where "FirstName"='Rim' and "LastName"='Saidi'`) === '1', 'no second Rim Saidi');
    await g.getByRole('button', { name: /^(Fermer la recherche|Garder )/ }).first().click();
  });
  await scenario('A11', ['A1'], async () => {
    const t = await groupText(group('17:12'));
    check('A11', /Pas d'heure dans la photo/.test(t) && /Appareil inconnu/.test(t), 'edited copy → « Pas d\'heure dans la photo. » + « Appareil inconnu »', t.slice(0, 200));
    await group('17:12').getByRole('button', { name: 'Ignorer cette photo' }).click();
    await page.waitForTimeout(400);
    check('A11', /Photo ignorée/.test(await groupText(group('17:12'))), '« Ignorer cette photo » → « Photo ignorée : elle reste sur la carte. »');
  });
  await scenario('C1', ['A1'], async () => {
    for (const [w, h] of [[320, 640], [390, 844], [820, 1180], [1180, 820], [1440, 900]]) {
      await size(w, h);
      await page.evaluate(() => document.querySelector('main')?.scrollTo(0, 0));
      const o = await overflow();
      check('C1', o.main <= 1 && o.doc <= 1, `review at ${w}: no sideways scroll`, `main ${o.main} doc ${o.doc}`);
      await shot(`C1-review-${w}`);
      await group('12:08 – 12:14').scrollIntoViewIfNeeded().catch(() => {});
      await shot(`C1-review-${w}-middle`);
    }
    await size(1536, 730);
  });
  let filedCount = 0;
  await scenario('A12', ['A1'], async () => {
    const label = await ranger.innerText();
    filedCount = Number(label.match(/\d+/)?.[0] || 0);
    check('A12', !(await ranger.isDisabled()) && filedCount > 0, `everything resolved → « ${label} » enabled`);
    cutUploads = 2;
    await ranger.click();
    await page.getByText(/photos? non rangées? sur/).first().waitFor({ timeout: 300000 });
    const t = await page.locator('main').innerText();
    check('A12', /2 photos non rangées/.test(t), 'connection cut → « 2 photos non rangées »', (t.match(/[^\n]*non rangée[^\n]*/) || [''])[0]);
    await shot('A12-cut');
    await page.getByRole('button', { name: 'Réessayer' }).click();
    await page.getByText(`${filedCount} photos rangées`).first().waitFor({ timeout: 300000 });
    ok('A12', `« Réessayer » → « ${filedCount} photos rangées »`);
    await shot('A12-filed');
  });
  await scenario('A13', ['A12'], async () => {
    const after = Number(sql(`select count(*) from "PatientFiles" where "CaptureKey" is not null`));
    check('A13', after - filesBefore === filedCount, `${filedCount} new camera files in the database (before ${filesBefore}, after ${after})`);
    const bad = sql(`select count(*) from "PatientFiles" where "CaptureKey" is not null and "UploadedAt" > now() - interval '30 minutes' and ("CapturedAt" is null or "CameraModel" is null or "Origin" <> 1 or "PreviewStorageKey" is null)`);
    check('A13', bad === '0', 'each carries capture time, camera model, Origin = Camera and a preview', `${bad} incomplete`);
    const models = sql(`select string_agg(distinct "CameraModel", ', ') from "PatientFiles" where "CaptureKey" is not null and "UploadedAt" > now() - interval '30 minutes'`);
    check('A13', /Canon EOS 250D/.test(models), `the camera model is the one the camera wrote (${models})`);
    const dupKeys = sql(`select count(*) from (select "CaptureKey" from "PatientFiles" where "CaptureKey" is not null group by 1 having count(*) > 1) d`);
    check('A13', dupKeys === '0', 'no capture key filed twice (despite the cut and the retry)');
    const sizes = sql(`select coalesce(max("FileSize"),0) from "PatientFiles" where "CaptureKey" is not null and "UploadedAt" > now() - interval '1 hour'`);
    check('A13', Number(sizes) < 4_000_000, `hosted copies are the 2 560 px copy (largest ${sizes} bytes)`);
    const last = sql(`select "LastImportedAt" from "ClinicCameras" where "Model" = 'Canon EOS 250D'`);
    check('A13', /2026-11-15/.test(last), `Canon « dernière importation » advanced to the card's day (${last})`);
  });
  await scenario('B13', ['A12'], async () => {
    const row = sql(`select "PatientId"||'|'||"FolderId"||'|'||"CaptureKey" from "PatientFiles" where "CaptureKey" is not null order by "UploadedAt" desc limit 1`).split('|');
    const form = new FormData();
    form.append('file', new Blob([fs.readFileSync(path.join(CARDS.canon, 'DCIM', '100CANON', 'IMG_0401.JPG'))], { type: 'image/jpeg' }), 'IMG_0401.JPG');
    form.append('folderId', row[1]);
    form.append('captureKey', row[2]);
    form.append('capturedAtLocal', '2026-11-15T08:36:10');
    form.append('cameraModel', 'Canon EOS 250D');
    const before = sql(`select count(*) from "PatientFiles"`);
    const res = await api('POST', `/patients/${row[0]}/files/upload`, undefined, form);
    const after = sql(`select count(*) from "PatientFiles"`);
    check('B13', res.status >= 400 && res.status < 500 && res.status !== 404 && before === after, `same capture key again → ${res.status}, nothing stored`, res.text.slice(0, 200));
    check('B13', Boolean(res.json?.code || res.json?.error), 'the refusal names itself (code / French error)', res.text.slice(0, 200));
  });
  await scenario('A14', ['A12'], async () => {
    await page.getByRole('button', { name: /Importer d.autres photos/ }).click();
    await pickCard('canon');
    await page.getByText(/photos déjà rangées/).first().waitFor({ timeout: 180000 });
    const t = await page.locator('main').innerText();
    check('A14', new RegExp(`${6 + filedCount} photos déjà rangées`).test(t), `same card again → « ${6 + filedCount} photos déjà rangées »`, (t.match(/[^\n]*déjà rangée[^\n]*/) || [''])[0]);
    await backToSource();
  });

  // ═══ The Nikon: a new camera, its clock an hour behind ═══
  await scenario('A15', [], async () => {
    if (!(await page.getByRole('button', { name: /Choisir le dossier de la carte/ }).isVisible().catch(() => false))) await backToSource();
    await pickCard('nikon');
    await page.getByText(/Nouvel appareil/).first().waitFor({ timeout: 180000 });
    const cards = page.locator('ol[aria-label="Groupes de photos"] > li');
    const n = await cards.count();
    const asking = await cards.filter({ hasText: /ses photos suivent-elles l.agenda de/ }).count();
    check('A15', n > 0 && asking === n, `new camera → every group asks whose it is (${asking}/${n})`);
    check('A15', (await page.locator('ol[aria-label="Groupes de photos"] [role="group"][aria-label="Patients possibles"]').count()) === 0, 'no patient offered from the guessed agenda');
    const bar = await page.getByText(/confirmez à qui est/).first().innerText().catch(() => '');
    check('A15', /confirmez à qui est/.test(bar), 'the « Ranger » bar asks for the camera, not for N patients', bar);
    await shot('A15-nikon-new-1536x730');
    await size(390, 844);
    const o = await overflow();
    check('A15', o.main <= 1 && o.doc <= 1, 'owner prompt at 390: no sideways scroll', `main ${o.main} doc ${o.doc}`);
    await shot('A15-nikon-new-390');
    await size(1536, 730);
  });
  await scenario('B14', ['A15'], async () => {
    // Answered from a card: one press for every group.
    await page.locator('ol[aria-label="Groupes de photos"] > li').first().getByRole('button', { name: 'Oui, confirmer' }).click();
    await page.waitForTimeout(1500);
    const cards = page.locator('ol[aria-label="Groupes de photos"] > li');
    const stillOwner = await cards.filter({ hasText: /ses photos suivent-elles l.agenda de/ }).count();
    check('B14', stillOwner === 0, '« Oui, confirmer » in one card lifts the question on every group');
    check('B14', (await page.getByRole('combobox', { name: /Ses photos suivent l.agenda de/ }).count()) === 1, 'the agenda can still be changed after « Confirmer »');
  });
  await scenario('A16', ['A15'], async () => {
    await page.getByRole('combobox', { name: /Ses photos suivent l.agenda de/ }).click();
    await page.getByRole('option', { name: 'Nadia Trabelsi' }).click();
    await page.getByText(/semble avoir 1 h de retard/).waitFor({ timeout: 30000 });
    ok('A16', '« … semble avoir 1 h de retard » after picking Nadia Trabelsi');
    const cards = page.locator('ol[aria-label="Groupes de photos"] > li');
    const n = await cards.count();
    const sure = await cards.filter({ hasText: 'Sûr' }).count();
    check('A16', n > 0 && sure === n, `every Nikon group sure on Nadia's patients (${sure}/${n})`);
    await shot('A16-nikon-clock');
  });
  await scenario('B6', ['A16'], async () => {
    await page.getByRole('button', { name: 'Annuler la correction' }).click();
    await page.waitForTimeout(1200);
    const cards = page.locator('ol[aria-label="Groupes de photos"] > li');
    const sureOff = await cards.filter({ hasText: 'Sûr' }).count();
    const corr = page.getByRole('button', { name: /^Corriger de/ });
    check('B6', await corr.count() === 1, '« Annuler la correction » → offers « Corriger de 1 h » back', `sure groups without correction: ${sureOff}`);
    await corr.click();
    await page.waitForTimeout(1200);
    const n = await cards.count();
    check('B6', (await cards.filter({ hasText: 'Sûr' }).count()) === n, 'redo → every group sure again');
  });
  await scenario('A16b', ['A16'], async () => {
    const label = await ranger.innerText();
    const n = Number(label.match(/\d+/)?.[0] || 0);
    await ranger.click();
    await page.getByText(`${n} photos rangées`).first().waitFor({ timeout: 300000 });
    ok('A16', `« ${label} » → « ${n} photos rangées »`);
    const cam = sql(`select coalesce("DoctorId"::text,'-')||'|'||coalesce("ClockOffsetMinutes"::text,'-') from "ClinicCameras" where "Make" ilike 'nikon%'`);
    const [doc, off] = cam.split('|');
    check('A16', doc === NADIA && Math.abs(Number(off)) === 60, `Nikon registered to Dr Trabelsi with a 60 min correction (doctor|offset = ${cam})`);
  });
  await scenario('B4', ['A16b'], async () => {
    await page.getByRole('button', { name: /Importer d.autres photos/ }).click();
    await pickCard('nikon');
    await page.getByText('Aucune nouvelle photo à ranger').waitFor({ timeout: 120000 });
    check('B4', /Toutes les photos de la carte sont déjà rangées/.test(await page.locator('main').innerText()), 'all filed → « Aucune nouvelle photo à ranger » / « Toutes les photos de la carte sont déjà rangées. »');
    await backToSource();
  });
  await scenario('C7', [], async () => {
    if (!(await page.getByRole('button', { name: /Choisir le dossier de la carte/ }).isVisible().catch(() => false))) await backToSource();
    await pickCard('clockreset');
    await page.getByText('Aucune nouvelle photo à ranger').waitFor({ timeout: 120000 });
    const t = await page.locator('main').innerText();
    check('C7', /plus anciennes que la dernière importation/.test(t) && /Afficher aussi 5 photos plus anciennes/.test(t), 'reset clock → « plus anciennes que la dernière importation » + « Afficher aussi 5 … »', t.slice(0, 240).replace(/\n/g, ' | '));
    const mentionsClock = /horloge|date de l.appareil|réglage/i.test(t);
    check('C7', mentionsClock, 'the screen hints the camera clock may be wrong', 'no clock hint: « ' + t.slice(0, 160).replace(/\n/g, ' | ') + ' »');
    await shot('C7-clock-reset');
    await page.getByRole('button', { name: /Afficher aussi 5 photos plus anciennes/ }).click();
    await page.locator('ol[aria-label="Groupes de photos"] > li').first().waitFor({ timeout: 30000 });
    const g = await page.locator('ol[aria-label="Groupes de photos"]').innerText();
    check('C7', /À vérifier/.test(g), 'shown, the 2019 photos ask', g.slice(0, 160).replace(/\n/g, ' | '));
    await shot('C7-clock-reset-shown');
    await backToSource();
  });

  // ═══ After filing: patient files ═══
  await scenario('A17', ['A12'], async () => {
    await page.goto(`${BASE}/patients/${ids.mehdi}/files`);
    await page.getByRole('button', { name: /Ouvrir le dossier Photographie/ }).click({ timeout: 60000 });
    await page.getByRole('button', { name: /^Actions pour IMG_0423/ }).first().click({ timeout: 60000 });
    await page.getByRole('menuitem', { name: 'Changer de patient' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.waitFor({ timeout: 20000 });
    await dialog.getByPlaceholder('Nom ou téléphone du patient').fill('Zzqx');
    await dialog.getByText('Aucun patient pour cette recherche.').waitFor({ timeout: 20000 });
    check('D1', (await dialog.getByRole('button', { name: /Nouveau patient/ }).count()) === 0, '« Changer de patient »: no « Nouveau patient » offered there');
    await size(390, 844);
    await dialog.getByPlaceholder('Nom ou téléphone du patient').fill('Ferchichi');
    await dialog.getByRole('button', { name: /Sarra Ferchichi/ }).click({ timeout: 20000 });
    const o = await overflow();
    check('A17', o.doc <= 1, 'move dialog at 390: no sideways scroll', `doc ${o.doc}`);
    await shot('A17-move-390');
    await dialog.getByRole('button', { name: /Ranger chez Sarra Ferchichi/ }).click();
    await page.getByText('Fichier déplacé').first().waitFor({ timeout: 20000 });
    ok('A17', '« Fichier déplacé »');
    const owner = sql(`select p."FirstName"||' '||p."LastName" from "PatientFiles" f join "Patients" p on p."Id" = f."PatientId" where f."FileName" ilike 'IMG_0423.%' and f."CaptureKey" is not null`);
    check('A17', owner === 'Sarra Ferchichi', `IMG_0423 now belongs to ${owner}`);
    await size(1536, 730);
  });
  await scenario('D6', ['A12'], async () => {
    await page.goto(`${BASE}/patients/${ids.sarra}/files`);
    await page.getByRole('button', { name: /Ouvrir le dossier Photographie/ }).click({ timeout: 60000 });
    // Run 3 probe fix: a file card is a `Card role="button"`, never a <button>.
    await page.locator('[role="button"][aria-label^="Ouvrir IMG_0423."]:visible').first().click({ timeout: 30000 });
    const dialog = page.getByRole('dialog');
    await dialog.waitFor({ timeout: 20000 });
    check('D6', /Prise le .* à 12:14/.test(await dialog.innerText()), 'camera photo preview: « Prise le … à 12:14 »');
    await page.keyboard.press('Escape');
    // A DICOM and a HEIC uploaded through the drawer keep their own buttons.
    await page.goto(`${BASE}/patients/${ids.youssef}/files`);
    const input = page.locator('input[type=file]').first();
    await input.waitFor({ state: 'attached', timeout: 60000 });
    await input.setInputFiles([path.join(SAMPLES, 'etude-16-images.dcm'), path.join(SAMPLES, 'sourire-avant-traitement.heif')]);
    // Run 3 probe fix: the name shows in the upload queue at once, and a 51 Mpx HEIF builds its stand-in for ~11 s
    // before it is sent — reloading 3 s later aborted it. Wait for both rows on the server instead.
    const stored = (name) => Number(sql(`select count(*) from "PatientFiles" where "PatientId" = '${ids.youssef}' and "FileName" = '${name}'`));
    for (let i = 0; i < 90 && (stored('etude-16-images.dcm') === 0 || stored('sourire-avant-traitement.heif') === 0); i++) await page.waitForTimeout(2000);
    check('D6', stored('sourire-avant-traitement.heif') > 0 && stored('etude-16-images.dcm') > 0, 'DICOM and HEIC uploaded through the drawer');
    await page.goto(`${BASE}/patients/${ids.youssef}/files`);
    await page.locator('[role="button"][aria-label="Ouvrir etude-16-images.dcm"]:visible').first().click({ timeout: 60000 });
    // Run 3 probe fix: a bare count() ran before the dialog had rendered.
    // …and the accessible names are « Ouvrir … dans la visionneuse DICOM » / « Afficher en pleine résolution ».
    const viewer = await page.getByRole('dialog').getByRole('button', { name: /visionneuse/i }).waitFor({ timeout: 20000 }).then(() => true, () => false);
    check('D6', viewer, 'DICOM keeps « Visionneuse »');
    await page.keyboard.press('Escape');
    await page.getByRole('dialog').waitFor({ state: 'detached', timeout: 10000 }).catch(() => {});
    await page.locator('[role="button"][aria-label="Ouvrir sourire-avant-traitement.heif"]:visible').first().click({ timeout: 60000 });
    const full = await page.getByRole('dialog').getByRole('button', { name: /pleine résolution/i }).waitFor({ timeout: 30000 }).then(() => true, () => false);
    check('D6', full, 'HEIC keeps « Pleine résolution »');
    await shot('D6-heic-preview');
    await page.keyboard.press('Escape');
  });
  await scenario('D5', ['A12'], async () => {
    await page.goto(`${BASE}/settings`);
    const title = page.getByText('Appareils photo', { exact: true }).first();
    await title.waitFor({ timeout: 60000 });
    await title.scrollIntoViewIfNeeded();
    const card = page.locator('[data-slot="card"]').filter({ has: title }).last();
    const t = (await card.innerText()).replace(/\s+/g, ' ');
    // Run 3 probe fix: the Nikon is listed by the model it writes, « NIKON D5600 ».
    check('D5', /Canon EOS 250D/.test(t) && /nikon/i.test(t), 'both cameras listed', t.slice(0, 240));
    check('D5', /Ben Youssef/.test(t) && /Trabelsi/.test(t), 'each with its agenda (Ben Youssef / Trabelsi)', t.slice(0, 240));
    await shot('D5-cameras-1536');
    await size(390, 844);
    await title.scrollIntoViewIfNeeded();
    const o = await overflow();
    check('D5', o.main <= 1 && o.doc <= 1, 'cameras card at 390: no sideways scroll', `main ${o.main} doc ${o.doc}`);
    await shot('D5-cameras-390');
    await size(1536, 730);
  });
  await scenario('D9', [], async () => {
    // A coffre file registered through the product's own door, then the mirror's manifest read.
    const folder = sql(`select "Id" from "PatientFolders" where "PatientId" = '${ids.youssef}' order by "CreatedAt" limit 1`);
    const form = new FormData();
    const fileId = crypto.randomUUID();
    form.append('fileId', fileId); form.append('folderId', folder); form.append('fileName', 'cbct-coffre-qa.dcm');
    form.append('fileSize', String(2 * 1024 ** 3)); form.append('contentHash', 'a'.repeat(64));
    const reg = await api('POST', `/patients/${ids.youssef}/files/vault`, undefined, form);
    if (reg.status >= 300) return skip('D9', `coffre registration answered ${reg.status} ${reg.text.slice(0, 120)}`);
    let page1 = 1, names = [], total = 0;
    for (;;) {
      const m = await api('GET', `/backup/file-manifest?page=${page1}&pageSize=200`);
      if (m.status !== 200) return bad('D9', `manifest answered ${m.status}`, m.text.slice(0, 160));
      const items = m.json.items || m.json.Items || [];
      names.push(...items.map((i) => i.fileName || i.FileName || i.storageKey || ''));
      total = m.json.totalCount ?? m.json.TotalCount ?? names.length;
      if (!items.length || names.length >= total) break;
      page1++;
    }
    check('D9', names.length > 0 && !names.some((n) => /cbct-coffre-qa/.test(n)), `manifest lists ${names.length} hosted files and not the coffre file`);
  });
  await scenario('D7', [], async () => {
    const pol = await api('GET', `/meta/upload-policy?profile=patient-file`);
    const quota = pol.json?.storageQuotaBytes, used = pol.json?.storageUsedBytes;
    check('D7', typeof quota === 'number' && typeof used === 'number', `upload policy states the ceiling (${used} / ${quota})`);
    // Over the ceiling: one of THIS run's camera files is made to weigh 2 To in the private copy, then put back.
    const victim = sql(`select "Id"||'|'||"FileSize" from "PatientFiles" where "CaptureKey" is not null order by "UploadedAt" desc limit 1`).split('|');
    sql(`update "PatientFiles" set "FileSize" = 3298534883328 where "Id" = '${victim[0]}'`);
    try {
      const folder = sql(`select "Id" from "PatientFolders" where "PatientId" = '${ids.youssef}' order by "CreatedAt" limit 1`);
      const form = new FormData();
      form.append('file', new Blob([Buffer.from('%PDF-1.4\n%qa\n')], { type: 'application/pdf' }), 'qa-quota.pdf');
      form.append('folderId', folder);
      const before = sql(`select count(*) from "PatientFiles"`);
      const res = await api('POST', `/patients/${ids.youssef}/files/upload`, undefined, form);
      const after = sql(`select count(*) from "PatientFiles"`);
      check('D7', res.status >= 400 && before === after, `over the ceiling → refused (${res.status}), nothing stored`, res.text.slice(0, 200));
      check('D7', /[éèàô]|Espace|stockage|plein/i.test(res.text), 'refused in French, naming the storage', res.text.slice(0, 200));
    } finally {
      sql(`update "PatientFiles" set "FileSize" = ${victim[1]} where "Id" = '${victim[0]}'`);
    }
  });

  // ── Report ──
  await ctx.close();
  const errs = [...new Set(consoleErrors)].filter((e) => !/401|Failed to load resource: the server responded with a status of 40[14]/.test(e));
  console.log('\nconsole errors (deduplicated):', errs.length ? '\n  ' + errs.slice(0, 20).join('\n  ') : 'none');
  fs.writeFileSync(path.join(SHOTS, 'results.json'), JSON.stringify({ results, consoleErrors: errs }, null, 1));
  const red = results.filter((r) => r.outcome !== '✅');
  console.log(`\n${results.filter((r) => r.outcome === '✅').length} pass · ${results.filter((r) => r.outcome === '❌').length} fail · ${results.filter((r) => r.outcome === '⏭').length} not exercised`);
  red.forEach((r) => console.log(`  ${r.outcome} [${r.id}] ${r.m} ${r.d || ''}`));
})().catch((e) => { console.error('WALK CRASHED', e); process.exit(1); });
