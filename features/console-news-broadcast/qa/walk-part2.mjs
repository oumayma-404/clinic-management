// QA walk — Part 2, « Nouveautés » dans l'app (plan-part2.md). Re-run this same file each cycle.
import { chromium } from "playwright-core";
import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync } from "node:fs";
import { freshCode } from "./totp.mjs";

const WEB = "http://localhost:3095";
const CONSOLE = "http://localhost:3094";
const API = "http://localhost:5094/api";
const MAILPIT = "http://localhost:8025/api/v1";
const SHOTS = process.argv[2] ?? "./shots";
mkdirSync(SHOTS, { recursive: true });
const consoleAcct = JSON.parse(readFileSync(new URL("./account.json", import.meta.url)));
const { admin, secretary } = JSON.parse(readFileSync(new URL("./app-accounts.json", import.meta.url)));
const NEWEST = "Premiers pas guidés";
const OLDEST = "Relevé de caisse du jour";

const findings = [];
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`);
const bad = (id, m, d = "") => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }); };
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why, skipped: true }); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
async function poll(fn, timeoutMs = 15000, everyMs = 400) {
  const end = Date.now() + timeoutMs;
  let last;
  while (Date.now() < end) { last = await fn().catch(() => undefined); if (last) return last; await sleep(everyMs); }
  return last;
}
async function shot(page, name) {
  await page.bringToFront();
  await page.mouse.move(0, 0).catch(() => {});
  await sleep(400);
  await page.screenshot({ path: `${SHOTS}/${name}.png`, fullPage: false, timeout: 15000 });
}
const sql = (q) => execFileSync("docker", ["exec", "clinic-postgres", "psql", "-U", "clinic_user", "-d", "clinic_news_qa", "-tAc", q]).toString().trim();

async function webSignIn(context, who, withCode) {
  const page = await context.newPage();
  await page.goto(`${WEB}/login`);
  const body = { email: who.email, password: who.password, ...(withCode ? { totpCode: await freshCode(who.secret) } : {}) };
  const status = await page.evaluate(async (b) => {
    const r = await fetch("/bff/auth/local-login", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(b) });
    return r.status;
  }, body);
  if (status !== 200) throw new Error(`sign-in ${who.email} → ${status}`);
  return page;
}
const railRow = (page) => page.locator('aside a[href="/nouveautes"]').first();
async function railPill(page) {
  const row = railRow(page);
  if (!(await row.isVisible().catch(() => false))) return null;
  const pill = row.locator("span.rounded-full.bg-primary");
  return (await pill.count()) ? ((await pill.first().textContent()) ?? "").trim() : "";
}

const browser = await chromium.launch({ channel: "chrome", headless: true });

// ── Desktop admin ──
const desk = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await webSignIn(desk, admin, true);

// A1
try {
  await page.goto(`${WEB}/`);
  const pill = await poll(() => railPill(page).then((p) => (p ? p : undefined)), 20000);
  const name = await railRow(page).getAttribute("aria-current");
  await shot(page, "A1-rail-badge-1440");
  pill === "3" ? ok("A1", `rail row with pill « ${pill} »`) : bad("A1", "rail pill", JSON.stringify({ pill, name }));
} catch (e) { bad("A1", "threw", e.message); }

// C3 — collapsed rail keeps a dot
try {
  await page.getByRole("button", { name: "Réduire la barre latérale" }).click();
  const dot = await poll(async () => (await railRow(page).locator("span.size-2.rounded-full.bg-primary").count()) > 0, 5000);
  const label = await railRow(page).innerText().catch(() => "");
  await shot(page, "C3-rail-collapsed-1440");
  dot ? ok("C3", "collapsed rail: dot on the icon") : bad("C3", "no dot on the collapsed row", label);
  await page.getByRole("button", { name: "Développer la barre latérale" }).evaluate((b) => b.click());
  await sleep(500);
} catch (e) { bad("C3", "threw", e.message); }

// D1 — another page's active row, news row unchanged
try {
  await page.goto(`${WEB}/patients`);
  const active = await poll(() => page.locator('aside a[href="/patients"][aria-current="page"]').count().then((n) => n > 0), 15000);
  const pill = await railPill(page);
  await shot(page, "D1-rail-patients-1440");
  active && pill === "3" ? ok("D1", "« Patients » active, news row still « 3 »") : bad("D1", "rail on /patients", JSON.stringify({ active, pill }));
} catch (e) { bad("D1", "threw", e.message); }

// ── Console ──
const cons = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const cpage = await cons.newPage();
let history = [];
try {
  await cpage.goto(`${CONSOLE}/login`);
  await cpage.fill("#email", consoleAcct.email);
  await cpage.fill("#password", consoleAcct.password);
  await cpage.fill("#totpCode", await freshCode(consoleAcct.secret));
  await cpage.click("button[type=submit]");
  await cpage.waitForURL(/\/cabinets/, { timeout: 20000 });
  await cpage.goto(`${CONSOLE}/nouveautes`);
  history = await cpage.evaluate(async () => (await fetch("/bff/nouveautes/historique")).json());
} catch (e) { bad("B1", "console sign-in threw", e.message); }

const consoleVisibility = (id, visible) =>
  cpage.evaluate(async ([broadcastId, v]) => {
    const r = await fetch("/bff/nouveautes/visibilite", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ broadcastId, visible: v }) });
    return r.status;
  }, [id, visible]);

// B1 / B2 — hide then show the newest, through the console's own button
try {
  const toggle = (action) => cpage.getByRole("button", { name: `${action} « ${NEWEST} » dans l'app` }).locator("visible=true");
  await toggle("Masquer").click();
  const hidden = await poll(async () => (await toggle("Afficher").count()) > 0, 8000);
  const rowText = await cpage.locator("tr", { hasText: NEWEST }).first().innerText();
  await shot(cpage, "B1-console-hidden-1440");
  await page.goto(`${WEB}/`);
  const pillHidden = await poll(() => railPill(page).then((p) => (p ? p : undefined)), 15000);
  hidden && /Masquée/.test(rowText) && pillHidden === "2"
    ? ok("B1", "« Masquée » in the console, app badge 2")
    : bad("B1", "hide", JSON.stringify({ hidden, rowText, pillHidden }));

  await toggle("Afficher").click();
  const shown = await poll(async () => (await toggle("Masquer").count()) > 0, 8000);
  await page.goto(`${WEB}/`);
  const pillShown = await poll(() => railPill(page).then((p) => (p === "3" ? p : undefined)), 15000);
  shown && pillShown === "3" ? ok("B2", "« Visible » again, app badge back to 3") : bad("B2", "show", JSON.stringify({ shown, pillShown }));
} catch (e) { bad("B1", "threw", e.message); }

// ── Phone (coarse pointer) before the page is read ──
const phone = await browser.newContext({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true, deviceScaleFactor: 2 });
let ppage;
try {
  ppage = await webSignIn(phone, admin, true);
  await ppage.goto(`${WEB}/`);
  const plus = ppage.getByRole("button", { name: /^Ouvrir la navigation — 3 nouveautés non lues$/ });
  const named = await poll(async () => (await plus.count()) > 0, 20000);
  const dot = named ? await plus.locator("span.size-2.rounded-full.bg-primary").count() : 0;
  await shot(ppage, "C1-plus-dot-390");
  await plus.click();
  const drawerRow = ppage.getByRole("dialog").getByRole("link", { name: /Nouveautés — 3 non lues/ });
  const inDrawer = await poll(async () => (await drawerRow.count()) > 0, 8000);
  await shot(ppage, "C1-drawer-390");
  named && dot && inDrawer ? ok("C1", "« Plus » dot + name, drawer row « 3 »") : bad("C1", "phone badge", JSON.stringify({ named, dot, inDrawer }));
  inDrawer ? ok("D2", "« Plus » still opens the drawer") : bad("D2", "drawer did not open");
  await ppage.keyboard.press("Escape");
} catch (e) { bad("C1", "threw", e.message); }

// ── The page, first visit (desktop) ──
try {
  await page.goto(`${WEB}/`);
  await railRow(page).click();
  await page.waitForURL(/\/nouveautes$/, { timeout: 15000 });
  const cards = page.locator("article[id^='nouveaute-']");
  await poll(async () => (await cards.count()) > 0, 15000);
  const count = await cards.count();
  const nouveau = await page.locator("article[id^='nouveaute-'] header span", { hasText: /^Nouveau$/ }).count();
  const unreadBadge = (await page.getByText(/^3 non lues$/).count()) > 0;
  const firstTitle = (await cards.first().locator("h2").textContent())?.trim();
  const index = page.getByRole("navigation", { name: "Toutes les nouveautés" });
  const indexEntries = await index.getByRole("button").count();
  const months = await index.locator("p").allTextContents();
  await shot(page, "A2-page-1440");
  count === 10 && nouveau === 3 && unreadBadge && firstTitle === NEWEST && indexEntries === 12 && months.length >= 3
    ? ok("A2", `10 cards, 3 « Nouveau », « 3 non lues », index 12 under ${months.join(" · ")}`)
    : bad("A2", "page", JSON.stringify({ count, nouveau, unreadBadge, firstTitle, indexEntries, months }));
} catch (e) { bad("A2", "threw", e.message); }

// A3 — the rail clears, the cards keep « Nouveau »
try {
  const cleared = await poll(async () => (await railPill(page)) === "", 10000);
  const nouveau = await page.locator("article[id^='nouveaute-'] header span", { hasText: /^Nouveau$/ }).count();
  cleared && nouveau === 3 ? ok("A3", "rail pill gone, cards still « Nouveau »") : bad("A3", "after open", JSON.stringify({ pill: await railPill(page), nouveau }));
} catch (e) { bad("A3", "threw", e.message); }

// A4 — the marker
try {
  const row = sql(`select m."ReadUpToUtc" = (select max(b."CreatedAtUtc") from "PlatformNewsBroadcasts" b where b."HiddenFromAppAtUtc" is null), count(*) over () from "PlatformNewsReadMarkers" m join "Users" u on u."Id" = m."UserId" where u."Email" = '${admin.email}'`);
  row === "t|1" ? ok("A4", "one marker, at the newest news") : bad("A4", "marker", row);
} catch (e) { bad("A4", "threw", e.message); }

// B5 — the index reaches the oldest news (not yet rendered: 10 shown of 12)
try {
  await page.getByRole("navigation", { name: "Toutes les nouveautés" }).getByRole("button", { name: new RegExp(`^${OLDEST}`) }).click();
  const target = page.locator("article[id^='nouveaute-']", { hasText: OLDEST });
  const inView = await poll(async () => {
    if (!(await target.count())) return false;
    const box = await target.boundingBox();
    return box && box.y >= -5 && box.y < 900;
  }, 6000);
  await shot(page, "B5-index-oldest-1440");
  inView ? ok("B5", "oldest card rendered and scrolled into view") : bad("B5", "index jump", JSON.stringify(await target.boundingBox().catch(() => null)));
} catch (e) { bad("B5", "threw", e.message); }

// A5 + B4 — reload: everything read; « plus anciennes »
try {
  await page.goto(`${WEB}/nouveautes`);
  const cards = page.locator("article[id^='nouveaute-']");
  await poll(async () => (await cards.count()) > 0, 15000);
  const nouveau = await page.locator("article[id^='nouveaute-'] header span", { hasText: /^Nouveau$/ }).count();
  const nonLues = await page.getByText(/non lues?$/).count();
  const pill = await railPill(page);
  nouveau === 0 && nonLues === 0 && pill === "" ? ok("A5", "reload: no « Nouveau », no count") : bad("A5", "reload", JSON.stringify({ nouveau, nonLues, pill }));
  const more = page.getByRole("button", { name: "Nouveautés plus anciennes" });
  const before = await cards.count();
  await more.click();
  const after = await poll(async () => ((await cards.count()) === 12 ? 12 : undefined), 5000);
  const gone = (await more.count()) === 0;
  before === 10 && after === 12 && gone ? ok("B4", "10 → 12 cards, button gone") : bad("B4", "older", JSON.stringify({ before, after, gone }));
} catch (e) { bad("A5", "threw", e.message); }

// C4 — widths
try {
  for (const [w, h] of [[820, 1180], [1180, 820], [1536, 730]]) {
    await page.setViewportSize({ width: w, height: h });
    await page.goto(`${WEB}/nouveautes`);
    await poll(async () => (await page.locator("article[id^='nouveaute-']").count()) > 0, 15000);
    const over = await page.evaluate(() => { const m = document.querySelector("main"); return m ? m.scrollWidth - m.clientWidth : -1; });
    await shot(page, `C4-page-${w}x${h}`);
    over <= 1 ? ok("C4", `${w}×${h}: no sideways scroll`) : bad("C4", `${w}×${h} overflows`, String(over));
  }
  await page.setViewportSize({ width: 1440, height: 900 });
} catch (e) { bad("C4", "threw", e.message); }

// C2 — 320 / 390 on the phone context
try {
  for (const w of [320, 390]) {
    await ppage.setViewportSize({ width: w, height: 760 });
    await ppage.goto(`${WEB}/nouveautes`);
    const cards = ppage.locator("article[id^='nouveaute-']");
    await poll(async () => (await cards.count()) > 0, 15000);
    const over = await ppage.evaluate(() => { const m = document.querySelector("main"); return m ? m.scrollWidth - m.clientWidth : -1; });
    const dateInCard = await cards.first().locator("time").isVisible();
    const indexShown = await ppage.getByRole("navigation", { name: "Toutes les nouveautés" }).isVisible().catch(() => false);
    await shot(ppage, `C2-page-${w}`);
    over <= 1 && dateInCard && !indexShown ? ok("C2", `${w}: fits, date in card, no index`) : bad("C2", `${w}`, JSON.stringify({ over, dateInCard, indexShown }));
  }
} catch (e) { bad("C2", "threw", e.message); }

// B6 — every news hidden → empty state; then shown again
try {
  const ids = history.map((h) => h.broadcastId);
  if (ids.length !== 12) throw new Error(`history has ${ids.length}`);
  for (const id of ids) await consoleVisibility(id, false);
  await page.goto(`${WEB}/nouveautes`);
  const empty = await poll(async () => (await page.getByText("Aucune nouveauté pour l'instant").count()) > 0, 15000);
  await shot(page, "B6-empty-1440");
  for (const id of ids) await consoleVisibility(id, true);
  await page.goto(`${WEB}/nouveautes`);
  const back = await poll(async () => (await page.locator("article[id^='nouveaute-']").count()) === 10, 15000);
  empty && back ? ok("B6", "all hidden → empty state; shown → back") : bad("B6", "empty state", JSON.stringify({ empty, back }));
} catch (e) { bad("B6", "threw", e.message); }

// D3 — console history at both forms
try {
  await cpage.goto(`${CONSOLE}/nouveautes`);
  const result = await poll(async () => (await cpage.locator("tr", { hasText: NEWEST }).first().innerText()), 10000);
  await shot(cpage, "D3-console-history-1440");
  await cpage.setViewportSize({ width: 390, height: 844 });
  await cpage.reload();
  const cardLabel = await poll(async () => (await cpage.getByText("Dans l'app", { exact: true }).locator("visible=true").count()) > 0, 10000);
  await cpage.getByText(NEWEST).locator("visible=true").last().scrollIntoViewIfNeeded().catch(() => {});
  await shot(cpage, "D3-console-history-390");
  /1 \/ 1 envoyé/.test(result) && /Visible/.test(result) && cardLabel
    ? ok("D3", "history row « 1 / 1 envoyé · Visible », cards carry « Dans l'app »")
    : bad("D3", "console history", JSON.stringify({ result, cardLabel }));
} catch (e) { bad("D3", "threw", e.message); }

// A6 — the e-mail's button
try {
  const r = await fetch(`${MAILPIT}/search?query=${encodeURIComponent(`subject:"${NEWEST}"`)}&limit=5`);
  const msg = (await r.json()).messages?.[0];
  const full = await (await fetch(`${MAILPIT}/message/${msg.ID}`)).json();
  const href = (full.HTML.match(/href="([^"]+)"[^>]*>[^<]*Ouvrir Apexa/) ?? [])[1];
  href?.endsWith("/nouveautes") ? ok("A6", `button → ${href}`) : bad("A6", "button href", String(href));
} catch (e) { bad("A6", "threw", e.message); }

// B3 — secretary
const sec = await browser.newContext({ viewport: { width: 1440, height: 900 } });
try {
  const spage = await webSignIn(sec, secretary, false);
  await spage.goto(`${WEB}/`);
  await sleep(2500);
  const row = await spage.locator('a[href="/nouveautes"]').count();
  await spage.goto(`${WEB}/nouveautes`);
  const denied = await poll(async () => (await spage.getByText("Réservé aux administrateurs").count()) > 0, 15000);
  const status = await spage.evaluate(async (api) => {
    const { accessToken } = await (await fetch("/bff/auth/token")).json();
    return (await fetch(`${api}/news/summary`, { headers: { Authorization: `Bearer ${accessToken}` } })).status;
  }, API);
  await shot(spage, "B3-secretary-1440");
  row === 0 && denied && status === 403 ? ok("B3", "secretary: no row, page refused, API 403") : bad("B3", "secretary", JSON.stringify({ row, denied, status }));
} catch (e) { bad("B3", "threw", e.message); }

skip("C5", "a cabinet's own PC needs a SelfHostedLan API; the 404-before-mediator is NewsControllerTests'");

await browser.close();
const failures = findings.filter((f) => !f.skipped);
console.log(failures.length ? `\nRED — ${failures.length} finding(s)` : "\nALL CLEAR");
findings.forEach((f) => console.log(JSON.stringify(f)));
