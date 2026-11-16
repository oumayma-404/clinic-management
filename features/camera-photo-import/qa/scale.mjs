// QA plan row C8 — a full card: 399 full-size Canon JPEGs (≈ 1,9 Go) spread over 7 visits of the demo day, picked
// through « Choisir des photos » so every File is disk-backed, as with a real card. Measures reading, the review,
// filing, and the tab's memory while it runs. Run after walk.mjs (it needs the Canon paired and the day booked).
//   QA_HOME=<scratchpad> QA_EMAIL=… QA_PASSWORD=… QA_TOTP=… node scale.mjs
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HOME = process.env.QA_HOME;
if (!HOME || !process.env.QA_EMAIL || !process.env.QA_PASSWORD || !process.env.QA_TOTP) { console.error('Set QA_HOME, QA_EMAIL, QA_PASSWORD and QA_TOTP.'); process.exit(2); }
const { chromium } = createRequire(path.join(HOME, 'noop.js'))('playwright-core');
const HERE = path.dirname(fileURLToPath(import.meta.url));
const SHOTS = path.join(HERE, 'shots', `run-${process.env.QA_RUN || '2'}`);
fs.mkdirSync(SHOTS, { recursive: true });
const BASE = 'http://localhost:3099';
const BIG = path.join(HOME, 'qa-cards', 'big', 'DCIM', '100CANON');
const PROFILE = path.join(HOME, 'qa-scale-profile');
const sql = (q) => execSync(`docker exec clinic-postgres psql -U clinic_user -d clinic_camera_sd -Atc ${JSON.stringify(q)}`).toString().trim();
const log = (...a) => console.log(new Date().toTimeString().slice(0, 8), ...a);

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
/** Working set of every Chrome process of THIS profile, in Mo. */
function chromeMb() {
  try {
    const out = execSync(`powershell -NoProfile -Command "(Get-CimInstance Win32_Process -Filter \\"Name='chrome.exe'\\" | Where-Object { $_.CommandLine -like '*qa-scale-profile*' } | Measure-Object WorkingSetSize -Sum).Sum"`).toString().trim();
    return Math.round(Number(out) / 1048576);
  } catch { return -1; }
}

(async () => {
  fs.rmSync(PROFILE, { recursive: true, force: true });
  const ctx = await chromium.launchPersistentContext(PROFILE, { channel: 'chrome', headless: true, viewport: { width: 1536, height: 730 } });
  await ctx.route('**/notifications/pending-reviews**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await ctx.route('**/api/feedback/prompt', (r) => (r.request().method() === 'GET' ? r.fulfill({ status: 404, contentType: 'application/json', body: '{"error":"none"}' }) : r.continue()));
  const page = ctx.pages()[0] || (await ctx.newPage());
  const errors = [];
  page.on('pageerror', (e) => errors.push(String(e.message).slice(0, 200)));
  page.on('crash', () => errors.push('RENDERER CRASHED'));

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

  const cdp = await ctx.newCDPSession(page);
  await cdp.send('Performance.enable');
  const heapMb = async () => { const m = await cdp.send('Performance.getMetrics'); const v = m.metrics.find((x) => x.name === 'JSHeapUsedSize'); return v ? Math.round(v.value / 1048576) : -1; };
  const samples = [];
  let phase = 'idle';
  const timer = setInterval(async () => { try { samples.push({ t: Date.now(), phase, heap: await heapMb(), rss: chromeMb() }); } catch {} }, 3000);

  const files = fs.readdirSync(BIG).filter((f) => /\.jpe?g$/i.test(f)).sort().map((f) => path.join(BIG, f));
  const before = Number(sql(`select count(*) from "PatientFiles" where "CaptureKey" is not null`));
  log('files', files.length, 'camera files in the database before', before, 'heap', await heapMb(), 'Mo, chrome', chromeMb(), 'Mo');

  phase = 'reading';
  const t0 = Date.now();
  await page.locator('input[type=file][accept="image/jpeg,.jpg,.jpeg"]').setInputFiles(files);
  const ranger = page.getByRole('button', { name: /^Ranger \d+ photos?$/ });
  await ranger.waitFor({ timeout: 600000 });
  const readS = (Date.now() - t0) / 1000;
  phase = 'review';
  const label = await ranger.innerText();
  const groups = page.locator('ol[aria-label="Groupes de photos"] > li');
  const n = await groups.count();
  const sure = await groups.filter({ hasText: 'Sûr' }).count();
  log(`review after ${readS.toFixed(1)} s: ${label}, ${n} groups, ${sure} sure; heap ${await heapMb()} Mo, chrome ${chromeMb()} Mo`);
  await page.screenshot({ path: path.join(SHOTS, 'C8-big-review.png') });

  let fileS = null, done = null;
  if (!(await ranger.isDisabled())) {
    phase = 'filing';
    const t1 = Date.now();
    await ranger.click();
    const want = Number(label.match(/\d+/)[0]);
    const progress = setInterval(async () => {
      const t = await page.locator('section[aria-label="Rangement des photos"]').innerText().catch(() => '');
      log('filing…', (t.match(/\d+ photos? rangées?/) || [''])[0], `heap ${await heapMb().catch(() => -1)} Mo, chrome ${chromeMb()} Mo`);
    }, 20000);
    await page.getByText(new RegExp(`${want} photos rangées|non rangées? sur`)).first().waitFor({ timeout: 3600000 });
    clearInterval(progress);
    fileS = (Date.now() - t1) / 1000;
    done = (await page.locator('section[aria-label="Rangement des photos"]').innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 300);
    phase = 'done';
    log(`filing took ${fileS.toFixed(0)} s: ${done}`);
    await page.screenshot({ path: path.join(SHOTS, 'C8-big-filed.png') });
  }
  await page.waitForTimeout(4000);
  clearInterval(timer);

  const after = Number(sql(`select count(*) from "PatientFiles" where "CaptureKey" is not null`));
  const sizes = sql(`select round(avg("FileSize"))||'|'||max("FileSize")||'|0|'||count(*) filter (where "PreviewStorageKey" is null) from "PatientFiles" where "FileName" ~ '^IMG_5[0-3][0-9][0-9]'`);
  const peak = (k, p) => Math.max(...samples.filter((s) => !p || s.phase === p).map((s) => s[k]));
  const out = {
    photos: files.length, readSeconds: readS, groups: n, sureGroups: sure, rangerLabel: label,
    filingSeconds: fileS, filingResult: done, newCameraFiles: after - before,
    hostedCopy: { avgBytes: Number(sizes.split('|')[0]), maxBytes: Number(sizes.split('|')[1]), avgPreviewBytes: Number(sizes.split('|')[2]), withoutPreview: Number(sizes.split('|')[3]) },
    peakHeapMb: { reading: peak('heap', 'reading'), review: peak('heap', 'review'), filing: peak('heap', 'filing') },
    peakChromeMb: { reading: peak('rss', 'reading'), review: peak('rss', 'review'), filing: peak('rss', 'filing') },
    errors,
  };
  fs.writeFileSync(path.join(SHOTS, 'C8-scale.json'), JSON.stringify({ ...out, samples }, null, 1));
  console.log(JSON.stringify(out, null, 1));
  await ctx.close();
})().catch((e) => { console.error('SCALE CRASHED', e); process.exit(1); });
