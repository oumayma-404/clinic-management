#!/usr/bin/env node
/**
 * shots — drive the running app at the five contract widths, write a PNG each, and fail on overflow.
 *
 * WHY THIS EXISTS
 * `.claude/rules/frontend-web.md` § 14 ends with « Then **look at it**, at these widths: 320 / 390 / 820 / 1180 /
 * 1440 px, plus a landscape phone » — and calls that manual walk « the load-bearing half », because nothing in
 * `web/` can assert a layout. `check-responsive.mjs` is the mechanical half and works by grepping source; it
 * cannot see a rendered page. This is the third thing: a real browser at real widths, producing artefacts an
 * agent or a human can actually look at.
 *
 * It also mechanises ONE rule outright — § 11, « the page body never scrolls horizontally at 320 px ». That is a
 * measurement, not a judgement, so it does not need an eye: `scrollWidth > innerWidth` is the defect. Every other
 * § 13 item (does the empty state say the right thing, is the destructive confirm naming what it destroys) still
 * needs a reader, which is what the PNGs are for.
 *
 * WHY `playwright-core` AND `channel: "chrome"`
 * `playwright` downloads ~150 MB of browsers on install. `playwright-core` downloads none and drives the Chrome
 * already on the machine. One dev dependency, no browser cache, and the render comes from the same engine a
 * Tunisian clinic's Chrome will use.
 *
 * USAGE
 *   node scripts/shots.mjs                                   # the default route set, ../features/_shots/
 *   node scripts/shots.mjs --routes /,/appointments,/caisse
 *   node scripts/shots.mjs --out ../features/my-feature/screenshots
 *   node scripts/shots.mjs --base https://localhost:5001     # the Local same-origin front door
 *   node scripts/shots.mjs --widths 320,1440                 # a subset while iterating
 *   node scripts/shots.mjs --theme dark
 *
 * AUTH — every interesting screen is behind `ClinicGuard`, so a session is required.
 * Set `SHOTS_EMAIL` / `SHOTS_PASSWORD` (Local mode) and the first run signs in and saves the browser state to
 * `web/.auth/state.json`; later runs reuse it and skip the login. That file holds a live session cookie and is
 * gitignored — never commit it, never paste its contents. With no credentials the run still works for public
 * routes (`/login`, `/signup`, a marketing site) and says so for the rest.
 *
 * EXIT CODES — the repo's own convention (`verify-schema`, `reconcile-money`):
 *   0  every route rendered, no horizontal overflow
 *   1  could not run (app not reachable, Chrome missing, bad arguments)
 *   2  rendered, but at least one route overflows horizontally
 */

import { mkdirSync, existsSync, writeFileSync, readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";

const WEB_ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");
const AUTH_STATE = join(WEB_ROOT, ".auth", "state.json");

/**
 * The four device states of `frontend-web.md` § 1, plus the landscape phone that § 0 names separately.
 * `320` before `390` is deliberate: an iPad in Split View renders a phone layout on a 1024 pt device, so 320 is
 * the floor rather than an edge case.
 */
const WIDTHS = [
  { label: "320", width: 320, height: 720, note: "phone floor / Split View" },
  { label: "390", width: 390, height: 844, note: "phone" },
  { label: "820", width: 820, height: 1180, note: "tablet portrait — already past md:" },
  { label: "1180", width: 1180, height: 820, note: "tablet landscape — coarse pointer" },
  { label: "1440", width: 1440, height: 900, note: "desktop" },
  { label: "844x390", width: 844, height: 390, note: "landscape phone — ~380px of content height" },
];

const DEFAULT_ROUTES = ["/", "/appointments", "/patients", "/caisse", "/treatment-plans"];

// ── arguments ───────────────────────────────────────────────────────────────────────────────────────────────

function arg(name, fallback) {
  const i = process.argv.indexOf(`--${name}`);
  return i !== -1 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
}

const BASE = arg("base", process.env.SHOTS_BASE_URL ?? "http://localhost:3000").replace(/\/$/, "");
const OUT = resolve(WEB_ROOT, arg("out", "../features/_shots"));
const ROUTES = arg("routes", "")
  ? arg("routes", "").split(",").map((r) => r.trim()).filter(Boolean)
  : DEFAULT_ROUTES;
const THEME = arg("theme", "light");
const ONLY = arg("widths", "");
const TARGETS = ONLY ? WIDTHS.filter((w) => ONLY.split(",").map((s) => s.trim()).includes(w.label)) : WIDTHS;

if (TARGETS.length === 0) {
  console.error(`Aucune largeur retenue. Disponibles : ${WIDTHS.map((w) => w.label).join(", ")}`);
  process.exit(1);
}

// ── auth ────────────────────────────────────────────────────────────────────────────────────────────────────

/** Sign in once and persist the browser state, so a later run costs nothing. Local (email+password) mode only. */
async function signIn(browser) {
  const email = process.env.SHOTS_EMAIL;
  const password = process.env.SHOTS_PASSWORD;
  if (!email || !password) return null;

  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();
  await page.goto(`${BASE}/login`, { waitUntil: "domcontentloaded" });

  // Label-based, not selector-based: the form's own French <Label htmlFor> is the contract § 13 already requires.
  await page.getByLabel(/e-?mail|adresse/i).fill(email);
  await page.getByLabel(/mot de passe/i).fill(password);
  await page.getByRole("button", { name: /se connecter|connexion/i }).click();
  await page.waitForURL((u) => !u.pathname.startsWith("/login"), { timeout: 20_000 });

  mkdirSync(dirname(AUTH_STATE), { recursive: true });
  await context.storageState({ path: AUTH_STATE });
  await context.close();
  return AUTH_STATE;
}

/**
 * Scroll the whole page, then return to the top, before capturing.
 *
 * ⚠️ Without this a `fullPage` screenshot of any scroll-revealed section is a BLANK RECTANGLE. Reveals are driven
 * by `IntersectionObserver`, which only fires for elements that have actually entered the viewport — and
 * `fullPage` resizes the capture without ever scrolling, so every section below the fold is photographed at its
 * `opacity: 0` start state. Found by looking at the first run: the landing mockup's two dark bands came out as
 * large black voids, which reads as a rendering fault rather than as an un-fired animation.
 *
 * Emulating `prefers-reduced-motion` would fix the CSS half only — a reveal that toggles a class from JS stays
 * un-toggled — so the scroll is the general answer.
 */
async function scrollThroughPage(page) {
  await page.evaluate(async () => {
    const step = Math.max(200, Math.floor(window.innerHeight * 0.8));
    for (let y = 0; y < document.body.scrollHeight; y += step) {
      window.scrollTo(0, y);
      await new Promise((r) => setTimeout(r, 90));
    }
    window.scrollTo(0, document.body.scrollHeight);
    await new Promise((r) => setTimeout(r, 250));
    window.scrollTo(0, 0);
    await new Promise((r) => setTimeout(r, 250));
  });
}

// ── run ─────────────────────────────────────────────────────────────────────────────────────────────────────

const overflows = [];
const failures = [];
let shots = 0;

let browser;
try {
  browser = await chromium.launch({ channel: "chrome" });
} catch (err) {
  console.error(`Chrome introuvable ou impossible à lancer : ${err.message}`);
  console.error(`Installez Chrome, ou ajoutez un canal : chromium.launch({ channel: "msedge" }).`);
  process.exit(1);
}

// Reachability first — a per-route timeout six times over is a slow way to learn the app is not running.
try {
  const probe = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await probe.newPage();
  await page.goto(BASE, { waitUntil: "domcontentloaded", timeout: 15_000 });
  await probe.close();
} catch {
  console.error(`Serveur injoignable sur ${BASE}. Lancez l'application (/start-clinic) puis relancez.`);
  await browser.close();
  process.exit(1);
}

let statePath = existsSync(AUTH_STATE) ? AUTH_STATE : null;
if (!statePath) {
  try {
    statePath = await signIn(browser);
  } catch (err) {
    console.warn(`Connexion impossible (${err.message}) — seules les routes publiques seront lisibles.`);
  }
}
if (!statePath) {
  console.warn("Aucune session : définissez SHOTS_EMAIL / SHOTS_PASSWORD pour les écrans protégés.");
}

mkdirSync(OUT, { recursive: true });

for (const target of TARGETS) {
  const context = await browser.newContext({
    viewport: { width: target.width, height: target.height },
    // The 44px floor and every `coarse:` rule are keyed on the POINTER, not on a width — an ungated screenshot
    // at 1180px shows the mouse layout on the one device this product is held in a gloved hand at.
    hasTouch: target.width <= 1180,
    isMobile: target.width <= 820,
    deviceScaleFactor: 2,
    colorScheme: THEME === "dark" ? "dark" : "light",
    locale: "fr-TN",
    timezoneId: "Africa/Tunis",
    ignoreHTTPSErrors: true,
    storageState: statePath ?? undefined,
  });

  for (const route of ROUTES) {
    const page = await context.newPage();
    const slug = route === "/" ? "accueil" : route.replace(/^\//, "").replace(/\//g, "-");
    const file = join(OUT, `${slug}-${target.label}${THEME === "dark" ? "-dark" : ""}.png`);
    try {
      await page.goto(`${BASE}${route}`, { waitUntil: "networkidle", timeout: 30_000 });
      // The app fetches on mount and paints skeletons first; a screenshot of a skeleton proves nothing.
      await page.waitForTimeout(900);
      await scrollThroughPage(page);

      const measured = await page.evaluate(() => ({
        scrollWidth: document.documentElement.scrollWidth,
        innerWidth: window.innerWidth,
      }));
      // 1px of tolerance: a fractional layout width rounds up and is not an overflow anybody can scroll.
      if (measured.scrollWidth > measured.innerWidth + 1) {
        overflows.push({ route, width: target.label, ...measured });
      }

      await page.screenshot({ path: file, fullPage: true });
      shots += 1;
    } catch (err) {
      failures.push({ route, width: target.label, message: err.message.split("\n")[0] });
    } finally {
      await page.close();
    }
  }
  await context.close();
}

await browser.close();

// ── report ──────────────────────────────────────────────────────────────────────────────────────────────────

console.log(`\n${shots} capture(s) → ${OUT}`);
console.log(`Largeurs : ${TARGETS.map((t) => t.label).join(" · ")}   Thème : ${THEME}`);

if (failures.length > 0) {
  console.log(`\n${failures.length} route(s) n'ont pas pu être rendues :`);
  for (const f of failures) console.log(`  ✗ ${f.route} @ ${f.width} — ${f.message}`);
}

if (overflows.length > 0) {
  console.log(`\nDÉBORDEMENT HORIZONTAL (§ 11 — le corps de page ne doit jamais défiler latéralement) :`);
  for (const o of overflows) {
    console.log(`  ✗ ${o.route} @ ${o.width}px — scrollWidth ${o.scrollWidth} > ${o.innerWidth}`);
  }
  console.log(`\nNe corrigez pas avec overflow-x-hidden : c'est du rognage, pas une mise en page.`);
  process.exit(2);
}

console.log(`\nAucun débordement horizontal. Les captures restent à LIRE — § 13 demande un œil, pas une mesure.`);
if (failures.length > 0) process.exit(1);
