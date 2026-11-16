// QA plan row D8 — the vendor console's storage screens: the « Stockage » column, the storage sort, a cabinet's
// « Quota de stockage » and « Modifier le quota » (motif required, journaled). Needs the isolated API with its console
// listener on (Console__Port) and the console app pointed at it, plus a throwaway console account created with
// `platform-account create` in the private database.
//   QA_HOME=<scratchpad> QA_CONSOLE=http://localhost:3196 QA_CONSOLE_EMAIL=… QA_CONSOLE_PASSWORD=… QA_CONSOLE_TOTP=… node console.mjs
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HOME = process.env.QA_HOME;
const { chromium } = createRequire(path.join(HOME, 'noop.js'))('playwright-core');
const HERE = path.dirname(fileURLToPath(import.meta.url));
const SHOTS = path.join(HERE, 'shots', `run-${process.env.QA_RUN || '2'}`);
fs.mkdirSync(SHOTS, { recursive: true });
const BASE = process.env.QA_CONSOLE;
const CLINIC = 'f64a8a75-94e1-4e08-a743-fa231abe438f';
const sql = (q) => execSync(`docker exec clinic-postgres psql -U clinic_user -d clinic_camera_sd -Atc ${JSON.stringify(q)}`).toString().trim();
const results = [];
const check = (id, cond, m, d = '') => { console.log(`  ${cond ? '✅' : '❌'} [${id}] ${m} ${cond ? '' : d}`); results.push({ id, outcome: cond ? '✅' : '❌', m, d }); };

function totp(secret, offsetWindows = 0) {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = '';
  for (const c of secret) bits += A.indexOf(c).toString(2).padStart(5, '0');
  const bytes = Buffer.from((bits.match(/.{8}/g) || []).map((b) => parseInt(b, 2)));
  const counter = Math.floor(Date.now() / 1000 / 30) + offsetWindows;
  const buf = Buffer.alloc(8);
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0); buf.writeUInt32BE(counter >>> 0, 4);
  const h = crypto.createHmac('sha1', bytes).update(buf).digest();
  const o = h[h.length - 1] & 0xf;
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, '0');
}
const nextWindow = async (page) => page.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000);

(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
  const shot = (name) => page.screenshot({ path: path.join(SHOTS, `${name}.png`), timeout: 20000 });
  try {
    const email = process.env.QA_CONSOLE_EMAIL, secret = process.env.QA_CONSOLE_TOTP;
    let password = process.env.QA_CONSOLE_PASSWORD;
    // 1. Sign in → enrol the second factor → recovery codes → sign in again → change the one-time password.
    await page.goto(`${BASE}/login`);
    await page.getByLabel('Adresse e-mail', { exact: true }).fill(email);
    await page.getByLabel('Mot de passe', { exact: true }).fill(password);
    await nextWindow(page);
    await page.getByLabel('Code de vérification', { exact: true }).fill(totp(secret));
    await page.getByRole('button', { name: 'Se connecter' }).click();
    const enrol = page.getByRole('button', { name: 'Enrôler le second facteur' });
    if (await enrol.waitFor({ timeout: 15000 }).then(() => true).catch(() => false)) {
      await nextWindow(page);
      await page.getByLabel('Code de vérification', { exact: true }).fill(totp(secret));
      await enrol.click();
      await page.getByText('Codes de récupération').first().waitFor({ timeout: 20000 });
      await page.locator('[role="status"] button').first().click();
      await page.getByLabel('Adresse e-mail', { exact: true }).fill(email);
      await page.getByLabel('Mot de passe', { exact: true }).fill(password);
      await nextWindow(page);
      await page.getByLabel('Code de vérification', { exact: true }).fill(totp(secret));
      await page.getByRole('button', { name: 'Se connecter' }).click();
    }
    await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30000 });
    // A one-time password is changed before anything else; every session ends with it, so sign in again after.
    await page.goto(`${BASE}/mot-de-passe`);
    if (await page.getByLabel('Mot de passe actuel').waitFor({ timeout: 15000 }).then(() => true).catch(() => false)) {
      const fresh = `Qa-${crypto.randomBytes(6).toString('hex')}-2026!`;
      await page.getByLabel('Mot de passe actuel').fill(password);
      await page.getByLabel('Nouveau mot de passe', { exact: true }).fill(fresh);
      await page.locator('form button[type=submit]').click();
      await page.waitForURL((u) => u.pathname.startsWith('/login'), { timeout: 30000 }).catch(() => {});
      password = fresh;
      fs.writeFileSync(path.join(HOME, 'console-account-current.txt'), fresh);
      await page.goto(`${BASE}/login`);
      await page.getByLabel('Adresse e-mail', { exact: true }).fill(email);
      await page.getByLabel('Mot de passe', { exact: true }).fill(password);
      await nextWindow(page);
      await page.getByLabel('Code de vérification', { exact: true }).fill(totp(secret));
      await page.getByRole('button', { name: 'Se connecter' }).click();
      await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30000 });
    }
    console.log('signed in to the console at', page.url());

    // 2. The portfolio: the « Stockage » column, then sorted by storage.
    await page.goto(`${BASE}/cabinets`);
    await page.getByRole('columnheader', { name: 'Stockage' }).waitFor({ timeout: 60000 });
    check('D8', true, 'portfolio lists a « Stockage » column');
    const ours = page.getByRole('row').filter({ has: page.locator(`a[href*="${CLINIC}"]`) }).first();
    const cell = await ours.innerText().catch(() => '');
    check('D8', /Mo|Go|o \//.test(cell), 'the demo cabinet shows its storage (used / quota)', cell.replace(/\s+/g, ' ').slice(0, 200));
    await shot('D8-console-portfolio');
    await page.goto(`${BASE}/cabinets?sort=storage`);
    await page.getByRole('columnheader', { name: 'Stockage' }).waitFor({ timeout: 60000 });
    const firstHref = await page.getByRole('row').nth(1).locator('a[href*="/cabinets/"]').first().getAttribute('href').catch(() => '');
    const maxClinic = sql(`select c."Id" from "Clinics" c left join "PatientFiles" f on f."ClinicId" = c."Id" and f."Residency" <> 2 group by c."Id" order by coalesce(sum(f."FileSize"),0) desc, c."Id" limit 1`);
    check('D8', (firstHref || '').includes(maxClinic), `« Stockage » sort puts the biggest cabinet first (${maxClinic})`, `first row links ${firstHref}`);
    await shot('D8-console-sort-storage');

    // 3. The cabinet's file: « Quota de stockage » → « Modifier le quota » with a motif → journaled.
    await page.goto(`${BASE}/cabinets/${CLINIC}`);
    await page.getByText('Stockage').first().waitFor({ timeout: 60000 });
    await page.getByRole('button', { name: /Modifier le quota de stockage de/ }).click();
    const dialog = page.getByRole('dialog');
    await dialog.waitFor({ timeout: 20000 });
    await dialog.getByLabel('Nouveau quota (Go)').fill('20');
    const save = dialog.getByRole('button', { name: 'Enregistrer le quota' });
    check('D8', await save.isDisabled(), '« Enregistrer le quota » waits for a motif');
    await dialog.getByLabel('Motif').fill('QA run 2 — photos de chaque patient');
    await shot('D8-console-quota-dialog');
    await save.click();
    await page.getByText('Quota enregistré.').first().waitFor({ timeout: 20000 });
    const stored = sql(`select coalesce("StorageQuotaBytes"::text,'-') from "Clinics" where "Id" = '${CLINIC}'`);
    check('D8', stored === String(20 * 1024 ** 3), `quota stored as 20 Go (${stored})`);
    const journal = sql(`select count(*) from "PlatformAccessEntries" where "ClinicId" = '${CLINIC}' and "Reason" like 'QA run 2%'`);
    check('D8', journal === '1', 'the change is journaled with its motif');
    await shot('D8-console-quota-saved');
  } catch (e) {
    check('D8', false, 'threw', String(e?.message || e).split('\n')[0]);
    await page.screenshot({ path: path.join(SHOTS, 'FAIL-D8-full.png'), fullPage: true }).catch(() => {});
    console.log('buttons:', JSON.stringify(await page.locator('button').evaluateAll((b) => b.map((x) => x.getAttribute('aria-label') || x.textContent.trim()).filter(Boolean))).slice(0, 1500));
  }
  await browser.close();
  fs.writeFileSync(path.join(SHOTS, 'D8-console.json'), JSON.stringify(results, null, 1));
})();
