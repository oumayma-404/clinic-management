// QA walk — console « Nouveautés » (features/console-news-broadcast/qa/plan.md). Re-run this same file each cycle.
import { chromium } from "playwright-core";
import { mkdirSync, readFileSync } from "node:fs";
import { freshCode } from "./totp.mjs";

const BASE = "http://localhost:3094";
const MAILPIT = "http://localhost:8025/api/v1";
const SHOTS = process.argv[2] ?? "./shots";
mkdirSync(SHOTS, { recursive: true });
const acct = JSON.parse(readFileSync(new URL("./account.json", import.meta.url)));
const RUN = new Date().toISOString().slice(11, 19).replace(/:/g, "");
const SUBJECT = `QA nouveautés ${RUN}`;
const ROW1 = "Les rappels SMS partent la veille.";
const ROW2 = "Le nouvel agenda est plus rapide.";

const findings = [];
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`);
const bad = (id, m, d = "") => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }); };
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why, skipped: true }); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function poll(fn, timeoutMs = 15000, everyMs = 500) {
  const end = Date.now() + timeoutMs;
  let last;
  while (Date.now() < end) {
    last = await fn();
    if (last) return last;
    await sleep(everyMs);
  }
  return last;
}

async function mail(subject) {
  const r = await fetch(`${MAILPIT}/search?query=${encodeURIComponent(`subject:"${subject}"`)}&limit=50`);
  const j = await r.json();
  return (j.messages ?? []).filter((m) => m.Subject === subject);
}

async function shot(page, name) {
  await page.mouse.move(0, 0);
  await sleep(400);
  await page.screenshot({ path: `${SHOTS}/${name}.png`, fullPage: false, timeout: 15000 });
}

async function hScroll(page) {
  return page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
}

const browser = await chromium.launch({ channel: "chrome", headless: true });
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();

let sentBody = null;
page.on("request", (req) => {
  if (req.url().endsWith("/bff/nouveautes") && req.method() === "POST") sentBody = req.postData();
});

// D5 (added cycle 1) — the login password toggle sits inside its field
await page.goto(`${BASE}/login`);
try {
  const field = await page.locator("#password").boundingBox();
  const eye = await page.getByRole("button", { name: "Afficher le mot de passe" }).boundingBox();
  const inside = field && eye && eye.x >= field.x && eye.x + eye.width <= field.x + field.width + 1 && eye.y >= field.y - 1 && eye.y + eye.height <= field.y + field.height + 1;
  await shot(page, "D5-login-eye");
  inside ? ok("D5", "eye button inside the password field") : bad("D5", "eye button outside its field", JSON.stringify({ field, eye }));
} catch (e) { bad("D5", "threw", e.message); }

// ── sign in ──
await page.goto(`${BASE}/login`);
await page.fill("#email", acct.email);
await page.fill("#password", acct.password);
await page.fill("#totpCode", await freshCode(acct.secret));
await page.click("button[type=submit]");
await page.waitForURL(/\/cabinets/, { timeout: 20000 });

const visible = (sel) => page.locator(`${sel} >> visible=true`);
const sendBtn = () => page.getByRole("button", { name: /^Envoyer à \d+ cabinets?$/ });
const testBtn = () => page.getByRole("button", { name: "M'envoyer un test" });
const rowText = (i) => page.getByLabel(`Nouveauté ${i}`, { exact: true });
const frame = () => page.frameLocator('iframe[title="Aperçu de l\'e-mail"]');

// A1
try {
  await page.getByRole("link", { name: "Nouveautés" }).click();
  await page.waitForURL(/\/nouveautes$/, { timeout: 15000 });
  const label = (await sendBtn().textContent())?.trim();
  label === "Envoyer à 2 cabinets" ? ok("A1", `opened, button « ${label} »`) : bad("A1", "button label", label);
} catch (e) { bad("A1", "threw", e.message); }

// A2
try {
  await page.getByLabel("Objet").fill(SUBJECT);
  await page.getByLabel("Emoji de la nouveauté 1").selectOption("🚀");
  await rowText(1).fill(ROW1);
  await page.getByRole("button", { name: "Ajouter une nouveauté" }).click();
  await page.getByLabel("Emoji de la nouveauté 2").selectOption("📅");
  await rowText(2).fill(ROW2);
  const ready = await poll(async () => (await frame().locator("body").textContent().catch(() => ""))?.includes(ROW2), 10000);
  const body = (await frame().locator("body").textContent()) ?? "";
  const h1 = (await frame().locator("h1").textContent())?.trim();
  const imgOk = await frame().locator("img").first().evaluate((img) => img.complete && img.naturalWidth > 0);
  const checks = {
    kicker: body.includes("Happy Apexing ! 🦷✨"),
    heading: h1 === SUBJECT,
    row1: body.includes("🚀") && body.includes(ROW1),
    row2: body.includes("📅") && body.includes(ROW2),
    closing: body.includes("Apexa, toujours à vos côtés 💙"),
    logo: imgOk,
  };
  ready && Object.values(checks).every(Boolean)
    ? ok("A2", `preview ${JSON.stringify(checks)}`)
    : bad("A2", "preview", JSON.stringify({ ready, ...checks, h1 }));
  await shot(page, "A2-preview-1440");
} catch (e) { bad("A2", "threw", e.message); }

// A3
try {
  await testBtn().click();
  const status = await poll(async () => {
    const t = await page.getByRole("status").allTextContents().catch(() => []);
    return t.find((x) => x.includes("E-mail de test envoyé"));
  }, 20000);
  const msgs = await poll(async () => { const m = await mail(`[Test] ${SUBJECT}`); return m.length ? m : null; }, 20000);
  const to = (msgs ?? []).flatMap((m) => m.To.map((t) => t.Address.toLowerCase()));
  status === "E-mail de test envoyé à qa@news-qa.test." && msgs?.length === 1 && to[0] === "qa@news-qa.test"
    ? ok("A3", `« ${status} », 1 mail to ${to[0]}`)
    : bad("A3", "test send", JSON.stringify({ status, count: msgs?.length, to }));
} catch (e) { bad("A3", "threw", e.message); }

// A4
try {
  await sendBtn().click();
  const dialog = page.getByRole("dialog");
  await dialog.waitFor({ timeout: 10000 });
  const text = (await poll(async () => { const t = (await dialog.textContent()) ?? ""; return t.includes("2 cabinets · 3 adresses") ? t : null; }, 15000)) ?? ((await dialog.textContent()) ?? "");
  const counts = text.includes("2 cabinets · 3 adresses d'administrateur");
  const without = text.includes("1 cabinet sans adresse d'administrateur ne le recevra pas.");
  await shot(page, "A4-confirm-1440");
  await dialog.getByRole("button", { name: "Envoyer", exact: true }).click();
  const status = await poll(async () => {
    const t = await page.getByRole("status").allTextContents().catch(() => []);
    return t.find((x) => x.includes("Envoi en cours"));
  }, 20000);
  const subj = await page.getByLabel("Objet").inputValue();
  const rows = await page.getByLabel(/^Nouveauté \d+$/).count();
  const empty = (await rowText(1).inputValue()) === "";
  counts && without && status === "Envoi en cours vers 3 adresses." && subj === "Les nouveautés Apexa ✨" && rows === 1 && empty
    ? ok("A4", `confirm counts ok, « ${status} », form reset`)
    : bad("A4", "send", JSON.stringify({ counts, without, status, subj, rows, empty, text: text.slice(0, 200) }));
} catch (e) { bad("A4", "threw", e.message); }

// A5
let real = [];
try {
  real = (await poll(async () => { const m = await mail(SUBJECT); return m.length >= 3 ? m : null; }, 40000)) ?? (await mail(SUBJECT));
  await sleep(3000);
  real = await mail(SUBJECT);
  const to = real.flatMap((m) => m.To.map((t) => t.Address.toLowerCase())).sort();
  JSON.stringify(to) === JSON.stringify(["a@news-qa.test", "b2@news-qa.test", "b@news-qa.test"])
    ? ok("A5", `3 mails: ${to.join(", ")}`)
    : bad("A5", "recipients", JSON.stringify(to));
} catch (e) { bad("A5", "threw", e.message); }

// A6
try {
  const row = await poll(async () => {
    await page.reload();
    const t = (await visible("main").textContent()) ?? "";
    return t.includes(SUBJECT) && t.includes("3 / 3 envoyés") && !t.includes("en cours") ? t : null;
  }, 30000, 3000);
  const tableRows = await page.locator("table tbody tr", { hasText: SUBJECT }).count();
  row && tableRows === 1 ? ok("A6", "history: 1 row, « 3 / 3 envoyés », not in progress") : bad("A6", "history", JSON.stringify({ found: Boolean(row), tableRows }));
} catch (e) { bad("A6", "threw", e.message); }

// A7
try {
  const a = real.find((m) => m.To.some((t) => t.Address.toLowerCase() === "a@news-qa.test"));
  const detail = a ? await (await fetch(`${MAILPIT}/message/${a.ID}`)).json() : null;
  const html = detail?.HTML ?? "";
  const text = detail?.Text ?? "";
  const checks = {
    kicker: html.includes("Happy Apexing"),
    rowEmojiHtml: html.includes("&#128640;") || html.includes("🚀"),
    closingHtml: html.includes("toujours"),
    note: html.includes("administrateur du cabinet Cabinet Essai"),
    button: html.includes('href="http://localhost:3093/login"'),
    textRow: text.includes(`🚀 ${ROW1}`),
    textRow2: text.includes(`📅 ${ROW2}`),
    textClosing: text.includes("Apexa, toujours à vos côtés 💙"),
  };
  Object.values(checks).every(Boolean) ? ok("A7", JSON.stringify(checks)) : bad("A7", "mail body", JSON.stringify(checks));
} catch (e) { bad("A7", "threw", e.message); }

// B1
try {
  if (!sentBody) throw new Error("the send's request body was not captured");
  const r = await page.request.post(`${BASE}/bff/nouveautes`, { data: JSON.parse(sentBody) });
  const j = await r.json();
  await sleep(6000);
  const count = (await mail(SUBJECT)).length;
  await page.reload();
  const tableRows = await page.locator("table tbody tr", { hasText: SUBJECT }).count();
  r.status() === 200 && j.alreadyQueued === true && count === 3 && tableRows === 1
    ? ok("B1", "replay → alreadyQueued, still 3 mails, 1 history row")
    : bad("B1", "replay", JSON.stringify({ status: r.status(), j, count, tableRows }));
} catch (e) { bad("B1", "threw", e.message); }

// B4
try {
  await rowText(1).fill("");
  const tDis = await testBtn().isDisabled();
  const sDis = await sendBtn().isDisabled();
  tDis && sDis ? ok("B4", "both sends disabled with no text") : bad("B4", "disabled", JSON.stringify({ tDis, sDis }));
} catch (e) { bad("B4", "threw", e.message); }

// B5
try {
  const add = page.getByRole("button", { name: "Ajouter une nouveauté" });
  await rowText(1).fill("r1");
  await add.click(); await rowText(2).fill("r2");
  await add.click(); await rowText(3).fill("r3");
  await page.getByRole("button", { name: "Monter la nouveauté 3" }).click();
  const afterMove = [await rowText(1).inputValue(), await rowText(2).inputValue(), await rowText(3).inputValue()];
  await page.getByRole("button", { name: "Supprimer la nouveauté 2" }).click();
  const afterDelete = [await rowText(1).inputValue(), await rowText(2).inputValue()];
  while ((await page.getByLabel(/^Nouveauté \d+$/).count()) < 12) await add.click();
  const addDisabled = await add.isDisabled();
  while ((await page.getByLabel(/^Nouveauté \d+$/).count()) > 1) await page.getByRole("button", { name: /^Supprimer la nouveauté/ }).last().click();
  const delDisabled = await page.getByRole("button", { name: "Supprimer la nouveauté 1" }).isDisabled();
  JSON.stringify(afterMove) === '["r1","r3","r2"]' && JSON.stringify(afterDelete) === '["r1","r2"]' && addDisabled && delDisabled
    ? ok("B5", "move, delete, cap at 12, last row undeletable")
    : bad("B5", "row controls", JSON.stringify({ afterMove, afterDelete, addDisabled, delDisabled }));
} catch (e) { bad("B5", "threw", e.message); }

skip("B6", "the field's maxLength caps a row at 300 in the UI; the server refusal is B2");
skip("B7", "needs a second API with no SMTP — unit test in /test-small-feature");

// C3 — owner's laptop, confirm sheet
try {
  await page.setViewportSize({ width: 1536, height: 730 });
  await rowText(1).fill("Sheet check");
  await sendBtn().click();
  const dialog = page.getByRole("dialog");
  await dialog.waitFor({ timeout: 10000 });
  await sleep(500);
  const boxes = [];
  for (const name of ["Envoyer", "Revenir"]) boxes.push(await dialog.getByRole("button", { name, exact: true }).boundingBox());
  const inside = boxes.every((b) => b && b.y >= 0 && b.y + b.height <= 730 && b.x + b.width <= 1536);
  await shot(page, "C3-confirm-1536x730");
  // C6 (added cycle 1) — the close « X » is visible at the panel's top-right, and the panel ends under its footer
  const geo = await page.evaluate(() => {
    const d = document.querySelector('[data-slot="sheet-content"]');
    const x = d.querySelector('[data-slot="sheet-content"] > button');
    const foot = [...d.querySelectorAll("button")].find((b) => b.textContent.trim() === "Revenir").parentElement;
    const r = (e) => e.getBoundingClientRect();
    return { panel: r(d).toJSON(), x: r(x).toJSON(), xPos: getComputedStyle(x).position, footBottom: r(foot).bottom };
  });
  const xInside = geo.xPos === "absolute" && geo.x.top >= geo.panel.top && geo.x.top < geo.panel.top + 40 && geo.x.right <= geo.panel.right;
  const tight = Math.abs(geo.panel.bottom - geo.footBottom) <= 2;
  xInside && tight ? ok("C6", `X at top-right (${geo.xPos}), panel ends at footer`) : bad("C6", "sheet geometry", JSON.stringify(geo));
  await dialog.getByRole("button", { name: "Revenir", exact: true }).click();
  inside ? ok("C3", `buttons inside 1536×730 ${JSON.stringify(boxes.map((b) => Math.round(b.y + b.height)))}`) : bad("C3", "buttons off-screen", JSON.stringify(boxes));
} catch (e) { bad("C3", "threw", e.message); }

// C4 — widths
for (const w of [390, 820, 1180, 1440]) {
  try {
    await page.setViewportSize({ width: w, height: 900 });
    await page.goto(`${BASE}/nouveautes`);
    await sendBtn().waitFor();
    await sleep(800);
    const over = await hScroll(page);
    const tableVisible = await page.locator("table").isVisible();
    const expectTable = w >= 1024;
    await shot(page, `C4-nouveautes-${w}`);
    over <= 0 && tableVisible === expectTable
      ? ok("C4", `${w}px: no overflow, ${tableVisible ? "table" : "cards"}`)
      : bad("C4", `${w}px`, JSON.stringify({ over, tableVisible, expectTable }));
  } catch (e) { bad("C4", `${w}px threw`, e.message); }
}

// C1 — 320
try {
  await page.setViewportSize({ width: 320, height: 740 });
  await page.goto(`${BASE}/nouveautes`);
  await sendBtn().waitFor();
  await sleep(1500);
  const over = await hScroll(page);
  const formBottom = (await sendBtn().boundingBox())?.y ?? 0;
  const previewTop = (await page.locator('iframe[title="Aperçu de l\'e-mail"]').boundingBox())?.y ?? 0;
  const cards = await page.locator("ul li", { hasText: SUBJECT }).first().isVisible();
  await shot(page, "C1-nouveautes-320");
  await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
  await shot(page, "C1-nouveautes-320-bottom");
  over <= 0 && previewTop > formBottom && cards
    ? ok("C1", "320: no overflow, preview below form, history as cards")
    : bad("C1", "320", JSON.stringify({ over, formBottom, previewTop, cards }));
} catch (e) { bad("C1", "threw", e.message); }

// C2 — coarse pointer at 390
try {
  await page.setViewportSize({ width: 390, height: 844 });
  const cdp = await ctx.newCDPSession(page);
  await cdp.send("Emulation.setDeviceMetricsOverride", { width: 390, height: 844, deviceScaleFactor: 3, mobile: true });
  await cdp.send("Emulation.setTouchEmulationEnabled", { enabled: true, maxTouchPoints: 5 });
  await page.goto(`${BASE}/nouveautes`);
  await sendBtn().waitFor();
  await page.getByRole("button", { name: "Ajouter une nouveauté" }).click();
  const coarse = await page.evaluate(() => matchMedia("(pointer: coarse)").matches);
  const selectH = (await page.getByLabel("Emoji de la nouveauté 1").boundingBox())?.height ?? 0;
  const targets = await page.evaluate(() =>
    [...document.querySelectorAll("button")].filter((b) => b.offsetParent).map((b) => {
      const s = getComputedStyle(b, "::after");
      const r = b.getBoundingClientRect();
      const w = s.content !== "none" ? parseFloat(s.width) : r.width;
      const h = s.content !== "none" ? parseFloat(s.height) : r.height;
      return { name: b.getAttribute("aria-label") || b.textContent.trim(), w: Math.max(w, r.width), h: Math.max(h, r.height) };
    }));
  const small = targets.filter((t) => t.w < 44 || t.h < 44);
  coarse && selectH >= 44 && small.length === 0
    ? ok("C2", `coarse: select ${selectH}px, ${targets.length} buttons ≥ 44px`)
    : bad("C2", "touch targets", JSON.stringify({ coarse, selectH, small }));
  // R2 (review F2) — the three row buttons do not overhang each other on a finger
  try {
    const geo = await page.evaluate(() => {
      const names = ["Monter la nouveauté 1", "Descendre la nouveauté 1", "Supprimer la nouveauté 1"];
      return names.map((n) => {
        const b = document.querySelector(`[aria-label="${n}"]`);
        const r = b.getBoundingClientRect();
        const after = parseFloat(getComputedStyle(b, "::after").width);
        return { n, left: r.left, right: r.right, width: r.width, after };
      });
    });
    const wide = geo.every((g) => g.width >= 44 && g.after <= g.width + 0.5);
    const gaps = geo.slice(1).map((g, i) => g.left - geo[i].right);
    wide && gaps.every((gap) => gap >= 8) ? ok("R2", `row buttons ${geo.map((g) => g.width).join("/")} px, gaps ${gaps.join("/")} px`) : bad("R2", "row buttons overlap", JSON.stringify({ geo, gaps }));
  } catch (e) { bad("R2", "threw", e.message); }
  await cdp.send("Emulation.setTouchEmulationEnabled", { enabled: false });
  await cdp.send("Emulation.clearDeviceMetricsOverride");
} catch (e) { bad("C2", "threw", e.message); }

// R3 (review F3) — the history card's result wraps instead of being clipped
try {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`${BASE}/nouveautes`);
  await sendBtn().waitFor();
  const card = page.locator("ul li", { hasText: SUBJECT }).first();
  await card.waitFor();
  const res = await card.evaluate((li) => {
    const dd = [...li.querySelectorAll("dd")].pop();
    const root = dd.firstElementChild;
    return { ws: getComputedStyle(root).whiteSpace, clipped: dd.scrollWidth > dd.clientWidth + 1 };
  });
  res.ws === "normal" && !res.clipped ? ok("R3", "result wraps in the card") : bad("R3", "result clipped", JSON.stringify(res));
} catch (e) { bad("R3", "threw", e.message); }

// R14 (review F14) — links in the preview are inert
try {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${BASE}/nouveautes`);
  await rowText(1).fill("Lien inerte");
  await poll(async () => (await frame().locator("body").textContent().catch(() => ""))?.includes("Lien inerte"), 10000);
  const pe = await frame().locator("a").first().evaluate((a) => getComputedStyle(a).pointerEvents);
  pe === "none" ? ok("R14", "preview links: pointer-events none") : bad("R14", "preview link is live", pe);
} catch (e) { bad("R14", "threw", e.message); }

// R11 (review F11) — focus returns to « Envoyer » after « Revenir »
try {
  await sendBtn().click();
  const dialog = page.getByRole("dialog");
  await dialog.waitFor();
  await dialog.getByRole("button", { name: "Revenir", exact: true }).click();
  await dialog.waitFor({ state: "detached" });
  await sleep(400);
  const focused = await page.evaluate(() => document.activeElement?.textContent?.trim() ?? "");
  /^Envoyer à/.test(focused) ? ok("R11", `focus back on « ${focused} »`) : bad("R11", "focus lost", JSON.stringify(focused));
} catch (e) { bad("R11", "threw", e.message); }

// R12 (review F12) — deleting a row moves focus to the neighbouring text
try {
  await page.getByRole("button", { name: "Ajouter une nouveauté" }).click();
  await rowText(2).fill("À supprimer");
  await page.getByRole("button", { name: "Supprimer la nouveauté 2" }).click();
  await sleep(200);
  const left = await page.getByLabel(/^Nouveauté \d+$/).count();
  const expected = `Nouveauté ${Math.min(2, left)}`;
  const label = await page.evaluate(() => document.activeElement?.getAttribute("aria-label") ?? document.activeElement?.tagName);
  label === expected ? ok("R12", `focus on « ${label} » after deleting row 2 of ${left + 1}`) : bad("R12", "focus lost after delete", JSON.stringify({ label, expected }));
} catch (e) { bad("R12", "threw", e.message); }

// R9 (review F9) — the draft survives a reload
try {
  await page.getByLabel("Objet").fill("Brouillon QA gardé");
  await rowText(1).fill("Ligne gardée après rechargement");
  await sleep(300);
  await page.reload();
  await sendBtn().waitFor();
  const restored = await poll(async () => (await page.getByLabel("Objet").inputValue()) === "Brouillon QA gardé" ? true : null, 5000);
  const row = await rowText(1).inputValue();
  restored && row === "Ligne gardée après rechargement" ? ok("R9", "subject and row restored after reload") : bad("R9", "draft lost", JSON.stringify({ restored, row }));
} catch (e) { bad("R9", "threw", e.message); }

// R4 (review F4) — a failed history read stays in its section; the page and the form survive
try {
  const SUBJECT2 = `${SUBJECT} (bis)`;
  await page.route("**/bff/nouveautes/historique", (r) => r.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ error: "Serveur indisponible (QA)." }) }));
  await page.getByLabel("Objet").fill(SUBJECT2);
  await rowText(1).fill("Deuxième envoi QA");
  await sendBtn().click();
  const dialog = page.getByRole("dialog");
  await poll(async () => ((await dialog.textContent()) ?? "").includes("3 adresses"), 15000);
  await dialog.getByRole("button", { name: "Envoyer", exact: true }).click();
  const inline = await poll(async () => {
    const t = await page.getByRole("status").allTextContents().catch(() => []);
    return t.find((x) => x.includes("Actualisation impossible"));
  }, 15000);
  const heading = (await page.locator("h1").first().textContent())?.trim();
  const formAlive = await page.getByLabel("Objet").isVisible();
  await page.unroute("**/bff/nouveautes/historique");
  inline && heading === "Nouveautés" && formAlive ? ok("R4", `« ${inline} », page and form intact`) : bad("R4", "history failure took the page", JSON.stringify({ inline, heading, formAlive }));
} catch (e) { bad("R4", "threw", e.message); }

skip("R16", "an empty audience needs a second seed (no eligible admin) — covered by reading the code and the B2 refusal");

skip("D1", "byte-identical existing e-mails is a unit assertion — /test-small-feature");

// D2 — /cabinets header at 320
try {
  await page.setViewportSize({ width: 320, height: 740 });
  await page.goto(`${BASE}/cabinets`);
  const link = page.getByRole("link", { name: "Nouveautés" });
  await link.waitFor();
  const over = await hScroll(page);
  const all = await Promise.all(["Journal des accès", "Retours", "Nouveautés"].map((n) => page.getByRole("link", { name: n }).isVisible()));
  await shot(page, "D2-cabinets-320");
  over <= 0 && all.every(Boolean) ? ok("D2", "header wraps, no overflow at 320") : bad("D2", "header", JSON.stringify({ over, all }));
} catch (e) { bad("D2", "threw", e.message); }

// D4
for (const [path, heading] of [["/journal", "Journal des accès"], ["/retours", null]]) {
  try {
    await page.setViewportSize({ width: 1440, height: 900 });
    const r = await page.goto(`${BASE}${path}`);
    const h1 = (await page.locator("h1").first().textContent())?.trim();
    r?.status() === 200 && h1 && (!heading || h1 === heading) ? ok("D4", `${path} → « ${h1} »`) : bad("D4", path, JSON.stringify({ status: r?.status(), h1 }));
  } catch (e) { bad("D4", `${path} threw`, e.message); }
}

await browser.close();
const real_ = findings.filter((f) => !f.skipped);
console.log(`\nRUN ${RUN} — ${real_.length ? `${real_.length} failure(s)` : "ALL CLEAR"}; ${findings.length - real_.length} not exercised`);
findings.forEach((f) => console.log(JSON.stringify(f)));
