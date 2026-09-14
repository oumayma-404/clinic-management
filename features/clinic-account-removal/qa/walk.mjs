/**
 * QA walk — clinic-account-removal, the console's « Zone dangereuse ».
 *
 * ONE launch, every scenario of qa/plan.md, no check throws and no check returns early. Re-run this same file
 * after a fix; a new narrower script loses the coverage.
 *
 * ⚠️ The deletion happens ONCE and destroys the screen under test, so the order is fixed: sign in → read the
 * fiche → every refusal → every width → the real deletion → the post-conditions.
 *
 * Run:  node walk.mjs            (needs playwright-core; see qa/run-1.md § Environment for the path)
 */

import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { chromium } from "playwright-core";

const BASE = "http://localhost:3100";
const EMAIL = "qa.suppression@editeur.tn";
const TEMP_PW = "f3Sa4Hk9KxVA";
const NEW_PW = "QaSuppr2026!x";
const SECRET = "X2MZYX6QBN55PNUTY3Z2SNCVR4CILIWE";
const CLINIC = "86249bfb-5ad9-4858-97f5-aa484cd70c61";
const ADDRESS = "jetable.qa@exemple.tn";
const CLINIC_NAME = "Cabinet Jetable QA";
const MOTIF = "cabinet de test QA — adresse a liberer";

const SHOTS = path.join(import.meta.dirname, "shots");
fs.mkdirSync(SHOTS, { recursive: true });

const findings = [];
const ok = (id, m) => console.log(`  OK   [${id}] ${m}`);
const bad = (id, m, d = "") => {
  console.log(`  FAIL [${id}] ${m} ${d}`);
  findings.push({ id, severity: "?", m, d });
};
const skip = (id, why) => {
  console.log(`  SKIP [${id}] not exercised - ${why}`);
  findings.push({ id, severity: "skip", why });
};

// ---------------------------------------------------------------- TOTP

function totp(secret, at = Date.now()) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = "";
  for (const c of secret.replace(/[\s=]/g, "").toUpperCase()) {
    bits += alphabet.indexOf(c).toString(2).padStart(5, "0");
  }
  const bytes = Buffer.from((bits.match(/.{8}/g) || []).map((b) => parseInt(b, 2)));
  const counter = Math.floor(at / 1000 / 30);
  const buf = Buffer.alloc(8);
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0);
  buf.writeUInt32BE(counter >>> 0, 4);
  const h = crypto.createHmac("sha1", bytes).update(buf).digest();
  const o = h[h.length - 1] & 0xf;
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, "0");
}

/** A code is single-use and expires in flight, so each submission gets its own fresh window. */
async function freshCode(page) {
  const secondsIn = Math.floor(Date.now() / 1000) % 30;
  await page.waitForTimeout((30 - secondsIn + 1) * 1000);
  return totp(SECRET);
}

// ---------------------------------------------------------------- helpers

// ⚠️ NOT `[role="dialog"]`: Next's dev error overlay carries that role too (it appears here for the CSP
// `unsafe-eval` warning React logs in development), so a bare role selector is a strict-mode violation and,
// worse, an aria-modal layer that swallows clicks.
const panel = (page) => page.locator('[data-slot="sheet-content"]');
const confirmationField = (page) => page.locator('input[id$="-confirmation"]');
const motifField = (page) => page.locator('textarea[id$="-reason"], input[id$="-reason"]');
const submitInPanel = (page) => panel(page).locator('button[type="submit"]');

async function textOf(locator) {
  try {
    return (await locator.innerText()).replace(/\s+/g, " ").trim();
  } catch {
    return "";
  }
}

async function openPanel(page) {
  await dismissDevOverlay(page);
  await page.locator('button[aria-label^="Supprimer définitivement le cabinet"]').click();
  await panel(page).waitFor({ state: "visible", timeout: 10000 });
}

/** Fills the panel and submits, then returns the panel's text once it has settled. */
async function attempt(page, { confirmation, motif }) {
  await confirmationField(page).fill(confirmation ?? "");
  await page.waitForTimeout(150);
  await motifField(page).fill(motif ?? "");
  await page.waitForTimeout(150);
  await dismissDevOverlay(page);
  await submitInPanel(page).click();
  await page.waitForTimeout(2500);
  return textOf(panel(page));
}

async function cabinetStillThere() {
  // A read, through the database, because « the panel says it refused » and « nothing was deleted » are two
  // different claims and only the second one matters.
  const { execSync } = await import("node:child_process");
  const out = execSync(
    `docker exec clinic-postgres psql -U clinic_user -d clinic_management -tAc ` +
      `"select count(*) from \\"Clinics\\" where \\"Id\\"='${CLINIC}'"`,
    { encoding: "utf8" },
  );
  return out.trim() === "1";
}

/**
 * Fills a REACT-CONTROLLED input and checks the value stuck.
 *
 * ⚠️ **Filling right after `domcontentloaded` is the hydration race**, and it is what made run 2 report « could
 * not sign in with either password » against a console that signs in perfectly: Playwright sets the DOM value
 * before React has attached its handler, React's state stays empty, and the form posts empty credentials — a
 * 401 indistinguishable from a wrong password.
 */
async function typeStable(page, selector, value) {
  const field = page.locator(selector);
  await field.waitFor({ state: "visible", timeout: 15000 });

  for (let attempt = 0; attempt < 6; attempt++) {
    await field.fill(value);
    await page.waitForTimeout(250);
    if ((await field.inputValue()) === value) {
      return true;
    }
  }

  bad("PROBE", `the value of ${selector} would not stick — hydration never completed`);
  return false;
}

async function hydrated(page) {
  await page.waitForLoadState("load").catch(() => {});
  await page.waitForTimeout(1200);
  await dismissDevOverlay(page);
}

/** Next's dev error overlay is `aria-modal` and intercepts every click beneath it. */
async function dismissDevOverlay(page) {
  await page
    .evaluate(() => document.querySelectorAll("nextjs-portal").forEach((el) => el.remove()))
    .catch(() => {});
}

async function submitLogin(page, password) {
  await hydrated(page);
  await typeStable(page, "#email", EMAIL);
  await typeStable(page, "#password", password);
  await typeStable(page, "#totpCode", await freshCode(page));
  await page.locator('button[type="submit"]').click();
  await page.waitForTimeout(3500);
}

/**
 * Signs in, doing the enrolment and the forced password change if this account still needs them.
 *
 * ⚠️ **After the password change it signs in AGAIN**, because `console/app/bff/password/route.ts` clears the
 * session cookie on success on purpose (`SetPassword` bumps `TokenVersion`, so the token is dead). Not
 * accounting for that is what made run 1 report « the portfolio was not reached » for a console that was
 * behaving exactly as documented.
 */
async function ensureSignedIn(page) {
  for (const pw of [NEW_PW, TEMP_PW]) {
    await page.goto(`${BASE}/login`, { waitUntil: "domcontentloaded" });
    await submitLogin(page, pw);
    let text = await textOf(page.locator("main"));
    console.log(`       (login pw=${pw === NEW_PW ? "new" : "temp"}) url=${page.url()}`);

    if (/Enrôler le second facteur/.test(text)) {
      await typeStable(page, "#password", pw);
      await typeStable(page, "#totpCode", await freshCode(page));
      await page.locator('button:has-text("Enrôler le second facteur")').click();
      await page.waitForTimeout(3500);

      text = await textOf(page.locator("main"));
      if (/Codes de récupération/.test(text)) {
        // Counted, never written down: they are one-time secrets and this folder is committed.
        const codes = (text.match(/[A-Z0-9]{20}/g) ?? []).length;
        console.log(`       (enrolled) ${codes} recovery codes shown once`);
        await page.locator('button:has-text("se connecter")').click();
        await page.waitForTimeout(1200);
      } else {
        bad("A1", "no recovery codes were shown after enrolment", text.slice(0, 200));
      }

      await submitLogin(page, pw);
    }

    if (await page.locator("#currentPassword").isVisible().catch(() => false)) {
      await typeStable(page, "#currentPassword", pw);
      await typeStable(page, "#newPassword", NEW_PW);
      await page.locator('button[type="submit"]').click();
      await page.waitForTimeout(3000);
      console.log(`       (password changed) url=${page.url()}`);
      await page.goto(`${BASE}/login`, { waitUntil: "domcontentloaded" });
      await submitLogin(page, NEW_PW);
    }

    await page.goto(`${BASE}/cabinets`, { waitUntil: "domcontentloaded" });
    await page.waitForTimeout(2500);
    const portfolio = await textOf(page.locator("main"));

    // The sign-in screen is the only page with this heading, so its absence IS « we are in ».
    if (!/Console éditeur/.test(portfolio)) {
      return portfolio;
    }
  }

  return null;
}

// ---------------------------------------------------------------- the walk

const browser = await chromium.launch({ channel: "chrome", headless: true });
const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "fr-FR" });
const page = await context.newPage();

// « Abandonner cette suppression ? » is a native confirm; answer it per-scenario.
let confirmAnswer = true;
page.on("dialog", async (d) => {
  console.log(`       (confirm) ${d.message()}`);
  await (confirmAnswer ? d.accept() : d.dismiss());
});

let previewTotal = null;

// The preview is the one request every later row depends on, so its status and body are printed.
let wirePreview = null;
let wireDeleted = null;

page.on("response", async (r) => {
  if (!r.url().includes("/bff/suppression")) {
    return;
  }
  const body = await r.text().catch(() => "");
  console.log(`       (bff ${r.request().method()}) ${r.status()} ${body.slice(0, 300)}`);
  try {
    const json = JSON.parse(body);
    // Read off the wire rather than parsed out of the prose: « comptes 1 » followed by « 371 lignes en tout »
    // makes any greedy digit class answer 1371.
    if (r.request().method() === "GET" && typeof json.rowsTotal === "number") wirePreview = json;
    if (r.request().method() === "POST" && typeof json.rowsDeleted === "number") wireDeleted = json;
  } catch {
    /* a refusal body, not a payload */
  }
});

try {
  // ---------------------------------------------------------------- A1 sign in
  const portfolioText = await ensureSignedIn(page);

  if (portfolioText === null) {
    bad("A1", "could not sign in to the console with either password");
  } else {
    ok("A1", "signed in: enrolment, recovery codes, forced password change, portfolio reached");
  }

  // ---------------------------------------------------------------- D1 portfolio
  if (portfolioText !== null && new RegExp(CLINIC_NAME).test(portfolioText)) {
    ok("D1", "the portfolio renders and lists the throwaway cabinet");
  } else {
    bad("D1", "the portfolio does not list the cabinet under test", (portfolioText ?? "").slice(0, 300));
  }

  // ---------------------------------------------------------------- A2 / D2 the fiche
  await page.goto(`${BASE}/cabinets/${CLINIC}`, { waitUntil: "domcontentloaded" });
  await hydrated(page);
  await page.locator('section[aria-labelledby="danger-heading"]').waitFor({ state: "visible", timeout: 20000 })
    .catch(() => bad("A2", "the « Zone dangereuse » section never rendered on the fiche"));

  const sections = await page.locator("main section[aria-labelledby]").evaluateAll((els) =>
    els.map((e) => e.getAttribute("aria-labelledby")),
  );
  const dangerLast = sections[sections.length - 1] === "danger-heading";
  const dangerClass = await page
    .locator('section[aria-labelledby="danger-heading"]')
    .getAttribute("class");
  const dangerText = await textOf(page.locator('section[aria-labelledby="danger-heading"]'));

  const namesAlternative = /Suspendre ce\s*cabinet/.test(dangerText) && /réversible/.test(dangerText);

  if (dangerLast && /destructive/.test(dangerClass ?? "") && namesAlternative) {
    ok("A2", "« Zone dangereuse » is last, on a destructive border, and names suspension as the reversible way");
  } else {
    bad(
      "A2",
      `last=${dangerLast} (order: ${sections.join(", ")}) · destructive-border=${/destructive/.test(dangerClass ?? "")} · names-alternative=${namesAlternative}`,
      dangerText.slice(0, 200),
    );
  }

  const expectedSections = ["subscription-heading", "suspension-heading", "activity-heading", "admin-heading"];
  const missing = expectedSections.filter((s) => !sections.includes(s));
  if (missing.length === 0) {
    ok("D2", `the fiche's other sections all render (${sections.length} sections)`);
  } else {
    bad("D2", `sections missing from the fiche: ${missing.join(", ")}`, sections.join(", "));
  }

  // ---------------------------------------------------------------- B4 the panel before the preview lands
  // The preview is delayed once, on purpose: « absent until it lands » is otherwise a race nobody can observe.
  let delayOnce = true;
  await context.route("**/bff/suppression**", async (route) => {
    if (delayOnce && route.request().method() === "GET") {
      delayOnce = false;
      await new Promise((r) => setTimeout(r, 3000));
    }
    await route.continue();
  });

  await openPanel(page);
  const early = await textOf(panel(page));
  const fieldEarly = await confirmationField(page).count();
  const submitEarly = await submitInPanel(page).count();

  if (fieldEarly === 0 && submitEarly === 0 && /Lecture de ce que contient ce cabinet/.test(early)) {
    ok("B4", "while the preview is in flight the field and the submit button are absent, not disabled");
  } else {
    bad("B4", `field=${fieldEarly}, submit=${submitEarly} before the preview landed`, early.slice(0, 160));
  }

  const fieldAppeared = await confirmationField(page)
    .waitFor({ state: "visible", timeout: 15000 })
    .then(() => true)
    .catch(() => false);

  if (!fieldAppeared) {
    const stuck = await textOf(panel(page));
    await page.screenshot({ path: path.join(SHOTS, "preview-never-landed.png") });
    bad("A3", "the preview never produced the confirmation field", stuck.slice(0, 400));
    const ids = await page.locator('[data-slot="sheet-content"] input, [data-slot="sheet-content"] textarea')
      .evaluateAll((els) => els.map((e) => `${e.tagName}#${e.id}`));
    console.log(`       (fields in the panel) ${ids.join(", ") || "none"}`);
    throw new Error("no confirmation field — the rows below it cannot be exercised");
  }

  await page.waitForTimeout(500);

  // ---------------------------------------------------------------- A3 the census
  const census = await textOf(panel(page));
  fs.writeFileSync(path.join(SHOTS, "preview-text.txt"), census);
  // Off the wire, not parsed out of the prose: « comptes 1 » followed by « 371 lignes en tout » makes any
  // greedy digit class answer 1371, i.e. a probe reporting a figure the screen never showed.
  previewTotal = wirePreview?.rowsTotal ?? null;
  const totalIsOnScreen = previewTotal !== null && census.includes(String(previewTotal));

  const namesAccounts = /comptes/.test(census);
  const listsAddress = census.includes(ADDRESS);
  const saysFinal = /définitive/.test(census) && /pas de corbeille/.test(census);

  if (namesAccounts && listsAddress && saysFinal && totalIsOnScreen && previewTotal >= 339) {
    ok("A3", `the census reads ${previewTotal} rows on screen, names « comptes », lists ${ADDRESS}, and says it is final`);
  } else {
    bad(
      "A3",
      `comptes=${namesAccounts} · address=${listsAddress} · final=${saysFinal} · total=${previewTotal} on screen=${totalIsOnScreen} (SQL baseline says >= 339)`,
      census.slice(0, 300),
    );
  }

  // ---------------------------------------------------------------- A4 what the field asks for
  const label = await textOf(panel(page).locator('label[for$="-confirmation"]'));
  const placeholder = await confirmationField(page).getAttribute("placeholder");
  if (/Adresse e-mail/i.test(label) && placeholder === ADDRESS) {
    ok("A4", `the field asks for « ${label} » and offers ${placeholder} — not the cabinet's name`);
  } else {
    bad("A4", `label=« ${label} » placeholder=« ${placeholder} »`);
  }

  // ---------------------------------------------------------------- C1–C6 widths, panel open
  const widths = [
    ["C1", 320, 720],
    ["C2", 390, 844],
    ["C3", 820, 1024],
    ["C4", 1180, 820],
    ["C5", 1440, 900],
    ["C6", 1536, 730],
  ];
  const cdp = await context.newCDPSession(page);

  for (const [id, w, h] of widths) {
    await page.setViewportSize({ width: w, height: h });
    // A resized desktop browser is blind to `coarse:` rules; emulate the pointer on the phone widths.
    await cdp.send("Emulation.setEmulatedMedia", {
      features: [{ name: "pointer", value: w <= 390 ? "coarse" : "fine" }],
    });
    await page.waitForTimeout(700);

    const shot = path.join(SHOTS, `${id}-${w}x${h}.png`);
    await page.screenshot({ path: shot });

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    const box = await submitInPanel(page).boundingBox();
    const reachable = box !== null && box.height >= 36;
    const inView = box !== null && box.y >= 0 && box.y + box.height <= h + 1;

    // The sheet scrolls its own body, so a submit below the fold is fine *if* scrolling that body reveals it.
    let afterScroll = inView;
    if (!inView) {
      await panel(page)
        .locator(".overflow-y-auto")
        .first()
        .evaluate((el) => (el.scrollTop = el.scrollHeight))
        .catch(() => {});
      await page.waitForTimeout(400);
      const b2 = await submitInPanel(page).boundingBox();
      afterScroll = b2 !== null && b2.y >= 0 && b2.y + b2.height <= h + 1;
      await page.screenshot({ path: path.join(SHOTS, `${id}-${w}x${h}-scrolled.png`) });
    }

    if (overflow <= 0 && reachable && afterScroll) {
      ok(id, `${w}×${h}: no page overflow, submit ${Math.round(box.height)} px tall and on screen`);
    } else {
      bad(id, `${w}×${h}: overflow=${overflow}px · submit box=${JSON.stringify(box)} · onScreen=${afterScroll}`);
    }
  }

  await page.setViewportSize({ width: 1440, height: 900 });
  await cdp.send("Emulation.setEmulatedMedia", { features: [{ name: "pointer", value: "fine" }] });
  await page.waitForTimeout(500);

  // ---------------------------------------------------------------- B1 blank motif
  const r1 = await attempt(page, { confirmation: ADDRESS, motif: "" });
  if (/motif/i.test(r1) && (await cabinetStillThere())) {
    ok("B1", "a blank motif is refused in French and the cabinet is still in the database");
  } else {
    bad("B1", `refusal text did not mention the motif, or the cabinet went`, r1.slice(0, 220));
  }

  // ---------------------------------------------------------------- B2 wrong address
  await attempt(page, { confirmation: "autre.adresse@exemple.tn", motif: MOTIF });
  // ⚠️ The REFUSAL's own text, never the panel's: the census above it lists the freed address on purpose, so
  // testing the whole panel reports « it spelled the answer out » for a refusal that did nothing of the kind.
  const r2 = await textOf(panel(page).locator('[role="alert"]'));
  const spellsItOut = r2.includes(ADDRESS.split("@")[0]);
  if ((await cabinetStillThere()) && /adresse/i.test(r2) && !spellsItOut) {
    ok("B2", "a wrong address is refused, nothing deleted, and the refusal does not spell the right one out");
  } else {
    bad("B2", `stillThere=${await cabinetStillThere()} · the refusal spells the right address out=${spellsItOut}`, r2.slice(0, 220));
  }

  // ---------------------------------------------------------------- B3 the cabinet's NAME
  const r3 = await attempt(page, { confirmation: CLINIC_NAME, motif: MOTIF });
  if (await cabinetStillThere()) {
    ok("B3", "the cabinet's NAME is refused — a name is not unique, so it can never confirm one cabinet");
  } else {
    bad("B3", "THE CABINET WAS DELETED BY TYPING ITS NAME", r3.slice(0, 220));
  }

  // ---------------------------------------------------------------- B5 abandon
  confirmAnswer = false;
  await panel(page).locator('button:has-text("Revenir")').click();
  await page.waitForTimeout(800);
  const stillOpen = await panel(page).isVisible().catch(() => false);
  confirmAnswer = true;

  if (stillOpen) {
    ok("B5", "« Annuler » with text typed asks before discarding, and staying answers « non »");
  } else {
    bad("B5", "the panel closed although the confirm was dismissed");
  }

  // ---------------------------------------------------------------- A5 the real deletion
  const outcome = await attempt(page, { confirmation: ADDRESS, motif: MOTIF });
  await page.waitForTimeout(1500);
  const outcomeText = await textOf(panel(page));
  fs.writeFileSync(path.join(SHOTS, "outcome-text.txt"), outcomeText);
  await page.screenshot({ path: path.join(SHOTS, "A5-outcome-1440.png") });

  const title = await textOf(panel(page).locator('[data-slot="sheet-title"], h2').first());
  const stayedOpen = await panel(page).isVisible().catch(() => false);
  const showsFreed = outcomeText.includes(ADDRESS);

  if (stayedOpen && /Cabinet supprimé/.test(title + outcomeText) && showsFreed) {
    ok("A5", `the panel stayed open, reads « ${title} » and lists the freed address`);
  } else {
    bad("A5", `stayedOpen=${stayedOpen} · title=« ${title} » · showsFreedAddress=${showsFreed}`, outcomeText.slice(0, 300));
  }

  const deletedCount = outcomeText.match(/([\d   ]+) lignes/);
  const reported = deletedCount ? Number(deletedCount[1].replace(/[^\d]/g, "")) : null;
  if (reported !== null && previewTotal !== null && reported === previewTotal) {
    ok("A5b", `what it says it removed (${reported}) is what the preview promised (${previewTotal})`);
  } else {
    bad("A5b", `removed=${reported} vs preview=${previewTotal}`, outcomeText.slice(0, 200));
  }

  // ---------------------------------------------------------------- B6 the fiche afterwards
  await page.goto(`${BASE}/cabinets/${CLINIC}`, { waitUntil: "domcontentloaded" });
  await page.waitForTimeout(2500);
  const gone = await textOf(page.locator("main"));
  if (/n.{0,3}existe plus/.test(gone)) {
    ok("B6", "reopening the fiche says « Ce cabinet n'existe plus »");
  } else {
    bad("B6", "the fiche of a deleted cabinet does not say it is gone", gone.slice(0, 220));
  }

  // ---------------------------------------------------------------- A6 portfolio + journal
  await page.goto(`${BASE}/cabinets`, { waitUntil: "domcontentloaded" });
  await page.waitForTimeout(2500);
  const after = await textOf(page.locator("main"));
  if (!new RegExp(CLINIC_NAME).test(after)) {
    ok("A6a", "the cabinet is absent from the portfolio");
  } else {
    bad("A6a", "the deleted cabinet still appears in the portfolio");
  }

  await page.goto(`${BASE}/journal`, { waitUntil: "domcontentloaded" });
  await page.waitForTimeout(3000);
  const journal = await textOf(page.locator("main"));
  fs.writeFileSync(path.join(SHOTS, "journal-text.txt"), journal.slice(0, 4000));
  await page.screenshot({ path: path.join(SHOTS, "A6-journal-1440.png"), fullPage: true });

  const hasAction = /Cabinet supprimé définitivement/.test(journal);
  const hasMotif = journal.includes("adresse a liberer");
  const namesClinic = new RegExp(CLINIC_NAME).test(journal);

  if (hasAction && hasMotif && namesClinic) {
    ok("A6b", "the journal carries « Cabinet supprimé définitivement », the cabinet's name and the typed motif");
  } else {
    bad("A6b", `action=${hasAction} · motif=${hasMotif} · names the cabinet=${namesClinic}`, journal.slice(0, 400));
  }

  // ---------------------------------------------------------------- D3 journal renders
  if (journal.length > 80) {
    ok("D3", "the journal page renders");
  } else {
    bad("D3", "the journal page rendered almost nothing", journal);
  }

  skip("C7", "a double submit is unreachable once the panel switches to its result view; idempotency was proven at the SQL layer instead");
} catch (e) {
  bad("WALK", "the walk threw", `${e.message}\n${e.stack?.split("\n").slice(0, 4).join(" | ")}`);
} finally {
  await page.screenshot({ path: path.join(SHOTS, "final.png") }).catch(() => {});
  await browser.close();
}

console.log("\n" + "-".repeat(78));
if (findings.length === 0) {
  console.log("ALL CLEAR");
} else {
  console.log(`${findings.length} finding(s):`);
  for (const f of findings) console.log("  " + JSON.stringify(f));
}
