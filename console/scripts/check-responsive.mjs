#!/usr/bin/env node
/**
 * check-responsive — the mechanical half of the mobile/tablet gate (spec AC-50).
 *
 * WHY THIS EXISTS
 * `web/` has no test runner, no working ESLint and no CI, so `tsc --noEmit` + `npm run build` are the only
 * automated checks — and neither can see a layout defect. The 26-dialog `max-w` collision proved the cost of
 * that: a caller's unprefixed `max-w-*` silently removed the base mobile gutter and left every one of those
 * dialogs clamped to 512 px on a desktop, undetected across the whole codebase. Nobody could see it and no type
 * could catch it.
 *
 * Each check below is a class of defect that is invisible to the eye at the width you happen to be developing
 * at. They are greps with a stated intent, not style opinions.
 *
 * EVERY CHECK IS ENFORCED
 * There used to be a `PENDING_PARTS` set here so a check written ahead of its fix reported as PENDING instead of
 * failing — the gate must not be red from birth or it gets ignored. It is **gone**: `mobile-tablet-responsive`
 * P1–P6 have all landed, and the set still held `P7`/`P8`, which **no check declares** — i.e. it had been inert
 * for some time while still reading as the source of truth for what is enforced. A staging mechanism that
 * outlives its staging is worse than none, because it invites the next check to be parked rather than fixed.
 *
 * The `part` tag on each check stays, as provenance for which slice of work introduced the rule.
 *
 * Do NOT add per-file exemptions — an allow-list that grows is a check that has stopped working.
 *
 * ─────────────────────────────────────────────────────────────────────────────────────────────────────────
 * THE CONSOLE'S COPY (platform-console Part 1). Adapted from `web/scripts/check-responsive.mjs`, and the two
 * differences are recorded here rather than left to be re-discovered:
 *
 *   1. `agenda-scroll` is DELETED. Every one of its three invariants is about `components/appointment-calendar.tsx`
 *      — the clinic agenda's week grid, its 48 px hour height and its seven-column overlay arithmetic. This
 *      application has no agenda and never will (FR-7: no clinic surfaces here), so the check could only ever
 *      report a missing file. A check that cannot pass is a gate that gets ignored, which is precisely what the
 *      note about `PENDING_PARTS` above is about.
 *
 *   2. `CARD_FALLBACK_EXEMPT` is EMPTIED. Its four entries argue about four `web/` components; carried over they
 *      are stale by construction and the check reports them as such forever. The check ITSELF is kept and is
 *      fully live — it derives its table surfaces from the files present, so the first table this app grows must
 *      have a card list. ⚠️ Emptying an inherited allow-list is the opposite of adding one: it makes the check
 *      apply to more of this codebase, not less.
 *
 * Everything else is byte-identical, deliberately: two apps in one repository held to two different device
 * contracts is how the second one quietly stops being held to any.
 * ─────────────────────────────────────────────────────────────────────────────────────────────────────────
 */

import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

const WEB_ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");
const SCAN_DIRS = ["app", "components", "lib", "contexts", "hooks"];

// ── file walking ────────────────────────────────────────────────────────────────────────────────────────────

function walk(dir, out = []) {
  let entries;
  try {
    entries = readdirSync(dir, { withFileTypes: true });
  } catch {
    return out;
  }
  for (const entry of entries) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === "node_modules" || entry.name === ".next") continue;
      walk(full, out);
    } else if (/\.(tsx?|css)$/.test(entry.name)) {
      out.push(full);
    }
  }
  return out;
}

const ALL_FILES = SCAN_DIRS.flatMap((d) => walk(join(WEB_ROOT, d)));
const rel = (f) => relative(WEB_ROOT, f).split(sep).join("/");

/** Read a file once; checks share the cache. */
const cache = new Map();
function read(file) {
  if (!cache.has(file)) cache.set(file, readFileSync(file, "utf8"));
  return cache.get(file);
}

/**
 * Lines that are inside a comment, so a class name quoted in prose is not read as shipped CSS. Several of this
 * codebase's comments quote the very classes these checks ban, explaining why they are banned — and they run to
 * several lines, so a "does this line start with //" test is not enough: the continuation lines of a `/* … *\/`
 * block carry no marker of their own.
 */
function commentMask(lines) {
  const mask = new Array(lines.length).fill(false);
  let inBlock = false;
  lines.forEach((line, i) => {
    const trimmed = line.trim();
    if (inBlock) {
      mask[i] = true;
      if (trimmed.includes("*/")) inBlock = false;
      return;
    }
    if (trimmed.startsWith("//")) { mask[i] = true; return; }
    const open = line.lastIndexOf("/*");
    if (open !== -1 && !line.slice(open).includes("*/")) { mask[i] = true; inBlock = true; }
  });
  return mask;
}

/** Report `pattern` hits line-by-line across `files`, optionally filtered by `accept(line, file)`. */
function scanLines(files, pattern, accept = () => true) {
  const hits = [];
  for (const file of files) {
    const lines = read(file).split(/\r?\n/);
    const inComment = commentMask(lines);
    lines.forEach((line, i) => {
      if (inComment[i]) return;
      const re = new RegExp(pattern.source, pattern.flags.includes("g") ? pattern.flags : pattern.flags + "g");
      let m;
      while ((m = re.exec(line)) !== null) {
        if (accept(line, file, m)) hits.push({ file: rel(file), line: i + 1, text: m[0], full: line.trim() });
        if (m.index === re.lastIndex) re.lastIndex++;
      }
    });
  }
  return hits;
}

const tsx = () => ALL_FILES.filter((f) => f.endsWith(".tsx") || f.endsWith(".ts"));
const pages = () => ALL_FILES.filter((f) => /app[\\/].*page\.tsx$/.test(f));

// ── checks ──────────────────────────────────────────────────────────────────────────────────────────────────

const checks = [];
const check = (id, part, title, why, run) => checks.push({ id, part, title, why, run });

check(
  "dialog-max-w",
  "P4",
  "A DialogContent / AlertDialogContent width override is `md:`-prefixed",
  "Two failures, one check. An UNPREFIXED max-w is the same tailwind-merge group as the base " +
    "`max-w-[calc(100%-2rem)]`, so the caller wins and the mobile gutter dies — but it cannot beat the base's " +
    "own prefixed clamp, which then holds the dialog at 512 px on every desktop. And an `sm:`-prefixed one is " +
    "the ambiguity P4 removed: the dialog presentation switches at `md:`, so between 640 and 767 px an " +
    "`sm:max-w-*` and the mobile sheet's width would both be live in different variants — twMerge keeps both " +
    "and the stylesheet order decides. Write `md:max-w-*`.",
  () => {
    const hits = [];
    for (const file of tsx()) {
      const src = read(file);
      const re = /<(?:Alert)?DialogContent\b/g;
      let m;
      while ((m = re.exec(src)) !== null) {
        // Walk to the end of the opening tag, tracking brace depth so `className={...}` is included whole.
        let i = m.index + m[0].length;
        let depth = 0;
        while (i < src.length) {
          const c = src[i];
          if (c === "{") depth++;
          else if (c === "}") depth--;
          else if (c === ">" && depth === 0) break;
          i++;
        }
        const tag = src.slice(m.index, i);
        /*
         * Tokenise every string-ish run in the tag; a prefixed class is one token (`md:max-w-lg`). Splitting on
         * braces and backticks too is what reaches INSIDE a template literal — two call sites build the width
         * from a ternary (`patients/[id]` and `patient-files-manager`, both file previews), and a check that
         * only read `className="…"` would have declared them clean while they carried the bug.
         *
         * `md:` and nothing else: see the `why` above. An unprefixed token loses the gutter, an `sm:` one
         * straddles the 640–767 px band where the mobile sheet is still in force.
         */
        for (const token of tag.split(/[\s"'`{}()]+/)) {
          if (/(^|:)max-w-/.test(token) && !token.startsWith("md:max-w-")) {
            hits.push({
              file: rel(file),
              line: src.slice(0, m.index).split(/\r?\n/).length,
              text: token,
              full: tag.replace(/\s+/g, " ").slice(0, 120),
            });
          }
        }
      }
    }
    return hits;
  }
);

check(
  "viewport-height",
  "P1",
  "No `h-screen` / `min-h-screen` — use the dynamic viewport (`h-dvh` / `min-h-dvh`)",
  "`100vh` on iOS Safari is the LARGE viewport, so the bottom of the page sits under the URL bar and is " +
    "unreachable. `dvh` tracks the visible viewport.",
  () => scanLines(tsx(), /\b(?:min-)?h-screen\b/)
);

check(
  "type-scale",
  "P1",
  "No arbitrary `text-[Npx]` — use the scale",
  "Pixel-locked type does not respond to the user's text-size setting or to zoom, and the sub-11px values are " +
    "below the legibility floor on a phone (AC-2).",
  () => scanLines(tsx(), /text-\[[0-9.]+px\]/)
);

check(
  "breakpoint-tokens",
  "P1",
  "No `--breakpoint-*` declarations in globals.css",
  "Verified against tailwindcss@4.1.18: declaring a token ADDS a breakpoint, but REDEFINING an existing key " +
    "silently re-points every utility using it. The four device states are already the stock sm/lg/xl " +
    "boundaries, so no token is needed and declaring one is pure risk.",
  () => scanLines(ALL_FILES.filter((f) => f.endsWith(".css")), /--breakpoint-[a-z0-9]+\s*:/)
);

check(
  "sheet-vh",
  "P4",
  "No `vh` in a dialog / sheet height — use `dvh`",
  "A `max-h-[90vh]` cap does not shrink when the on-screen keyboard opens, so the sticky footer holding the " +
    "primary action is pushed off screen (AC-25).",
  () => scanLines(tsx(), /(?:max-)?h-\[[^\]]*\b[0-9.]+vh\b[^\]]*\]/)
);

check(
  "hover-movement",
  "P2",
  "No ungated `hover:scale-*` — gate movement hovers behind `hover-hover:`",
  "On a touch device a hover state sticks after the tap, so a transform reads as a stuck element. " +
    "globals.css declares the `hover-hover:` variant for exactly this.",
  /*
   * Anchored on a CLASS BOUNDARY, not a lookbehind. `(?<!hover-hover:)` looked right and was wrong: in
   * `hover-hover:group-hover:scale-105` the inner `hover:scale-` is preceded by `group-`, so the lookbehind
   * passed and the correctly-gated class was reported as a violation. Requiring the token to start after
   * whitespace or a quote means only a class that really begins with `hover:`/`group-hover:` matches.
   */
  () => scanLines(tsx(), /(?:^|[\s"'`{])(?:group-)?hover:scale-/)
);

check(
  "arch-clipping",
  "P6",
  "No `flex justify-center` inside a horizontally scrolling container",
  "When the content overflows, `justify-content: center` pushes the overflow to BOTH sides and the " +
    "inline-start half is not in the scrollable region — teeth 18-15 and 48-45 become unreachable by any means. " +
    "(Glyph-centring cells such as `flex h-9 w-7 items-center justify-center` are a different construct and are " +
    "not matched: this looks only for a bare `flex justify-center` row.)",
  () => scanLines(tsx(), /"flex justify-center\b/)
);

/**
 * The 5 surfaces that render a `<Table>` and deliberately do NOT get a card fallback.
 *
 * This is the one place a literal list is unavoidable: the source can tell you a file renders a table, but not
 * that the table *is* a chart's accessible fallback. Each entry therefore carries the reason it is here — an
 * exclusion without one is how an allow-list turns into a place to hide work.
 */
const CARD_FALLBACK_EXEMPT = new Map([]);

check(
  "card-fallback",
  "P3",
  "Every table surface has a card fallback below `md:`",
  "A `<Table>` with no `<CardList>` beside it is a 6-to-10 column grid on a 320px phone — the defect the whole " +
    "part exists to remove. Derived from the source rather than a checklist: a table added next month is " +
    "covered the day it is written, which a hand-maintained list could never be.",
  () => {
    const hits = [];
    for (const file of tsx()) {
      const src = read(file);
      const name = rel(file);
      if (CARD_FALLBACK_EXEMPT.has(name)) continue;

      /*
       * COUNTS, not presence. A file can render several tables — `plan-workspace` has two and
       * `patients/[id]` has four — and a presence test passes as soon as the FIRST one is converted, while
       * the rest go on scrolling sideways. That is the exact shape of a check that reports success over
       * unfinished work, so the rule is one card list per table.
       *
       * A file that legitimately holds a converted table plus an exempt one records the exempt one in
       * CARD_FALLBACK_EXEMPT with a `#suffix` and is counted here by hand — `stock-table` is the only such
       * case today (its movement history lives in a dialog).
       */
      const tables = (src.match(/<Table[\s>]/g) ?? []).length;
      if (tables === 0) continue;
      const cards = (src.match(/<CardList[\s>]/g) ?? []).length;
      const exemptInFile = [...CARD_FALLBACK_EXEMPT.keys()].filter((k) => k.startsWith(`${name}#`)).length;
      if (cards >= tables - exemptInFile) continue;

      hits.push({
        file: name,
        line: src.slice(0, src.search(/<Table[\s>]/)).split(/\r?\n/).length,
        text: `${tables} <Table>, ${cards} <CardList>${exemptInFile ? `, ${exemptInFile} exempt` : ""}`,
        full: "every table needs a card list below `md:`, or an argued entry in CARD_FALLBACK_EXEMPT",
      });
    }
    // An exemption for a file that no longer renders a table is dead weight that will outlive its reason.
    for (const [name, reason] of CARD_FALLBACK_EXEMPT) {
      const base = name.split("#")[0];
      const f = ALL_FILES.find((x) => rel(x) === base);
      if (!f || !/<Table[\s>]/.test(read(f))) {
        hits.push({ file: name, line: 0, text: "stale exemption", full: `no longer renders a table — ${reason}` });
      }
    }
    return hits;
  }
);

check(
  "header-orphans",
  "P2",
  "dashboard-header.tsx has no orphaned drawer-trigger symbols",
  "Removing the hamburger leaves `setMobileOpen` and the `Menu` import unreferenced. `tsc` does not flag an " +
    "unused destructured binding and lint is broken in this repo, so nothing else catches it.",
  () => {
    const file = ALL_FILES.find((f) => f.endsWith(join("components", "dashboard-header.tsx").split(sep).join(sep)));
    if (!file) return [];
    const src = read(file);
    const hits = [];
    const declares = /\bsetMobileOpen\b/.test(src);
    const calls = /setMobileOpen\s*\(/.test(src);
    if (declares && !calls) {
      hits.push({ file: rel(file), line: 0, text: "setMobileOpen", full: "destructured but never called" });
    }
    const importsMenu = /^import\s*\{[^}]*\bMenu\b[^}]*\}\s*from\s*["']lucide-react["']/m.test(src);
    const usesMenu = /<Menu\b/.test(src);
    if (importsMenu && !usesMenu) {
      hits.push({ file: rel(file), line: 0, text: "Menu", full: "imported from lucide-react but never rendered" });
    }
    return hits;
  }
);

check(
  "failed-read-as-empty",
  "P1",
  "No `.catch` that renders a failed read as an empty collection",
  "A read that FAILED and a read that returned nothing are different facts, and only one of them is ever true. " +
    "`.catch(() => [])` / `.catch(() => setX([]))` collapses them, so a dead endpoint renders as « Aucun " +
    "antécédent médical » on the card a dentist checks before injecting, « Aucun patient trouvé » about a " +
    "twelve-year patient, or an empty act catalogue that pushes the dentist into a free-text act with no tarif " +
    "and no resulting condition. The rules name this pattern three times and five instances still shipped — " +
    "prose has demonstrably failed, so it is mechanical now. Keep a `failed` flag distinct from " +
    "`items.length === 0` and render `ui/load-failure.tsx` beside whatever did load.",
  () => {
    /*
     * DERIVED, not an allow-list. The pattern is the *shape of the handler body*, which is what makes it precise
     * enough to leave legitimate catches alone:
     *
     *   - `.catch(() => [])`                → the awaited value becomes the empty collection
     *   - `.catch(() => setX([]))`          → state is emptied, which is the same thing one step later
     *   - `.catch(() => ({}))` / `setX({})` → the object-shaped version (a summary, a map)
     *
     * Deliberately NOT flagged, because none of them turns a failure into data:
     *   - `.catch(() => null)` / `undefined` — a nullable result the caller must branch on; `null` is not a
     *     renderable "empty list", and every one of these in the tree is a route handler or a blob fetch.
     *   - `.catch(() => { … })` with a real body — it may log, toast, or set a `failed` flag. Reading inside a
     *     multi-statement body is where a grep starts guessing; the single-expression form is unambiguous.
     *   - anything inside a comment (`commentMask`), since this file's own doc blocks quote the banned shape.
     *
     * ⚠️ Never add a per-file exemption here. A surface that legitimately has nothing to report on failure does
     * not need to *empty* anything — it can simply not set state.
     */
    const emptyLiteral = String.raw`(?:\[\s*\]|\(\s*\[\s*\]\s*\)|\{\s*\}|\(\s*\{\s*\}\s*\))`;
    const setterCall = String.raw`set[A-Z]\w*\s*\(\s*${emptyLiteral}\s*\)`;
    const pattern = new RegExp(
      String.raw`\.catch\(\s*\(\s*\)\s*=>\s*(?:${emptyLiteral}|${setterCall})\s*\)`,
    );
    return scanLines(
      ALL_FILES.filter((f) => /^(app|components)[\\/]/.test(relative(WEB_ROOT, f))),
      pattern,
    );
  }
);

/** `lib/download.ts` is the shared helper — the one place these mechanisms legitimately live. */
const DELIVERY_HELPER = "lib/download.ts";

check(
  "blob-delivery",
  "N1",
  "A file is delivered through `lib/download.ts`, never a hand-rolled anchor or `saveAs`",
  "`<a download>` on a `blob:` URL is **ignored by iOS Safari** — the file never arrives and nothing raises an " +
    "error, so on an iPhone the button simply does nothing. Five call sites had each hand-rolled it (a patient " +
    "file, an invoice PDF, a document PDF, a CSV export) and a sixth used `file-saver` as a third mechanism, " +
    "so the shared helper's device-aware share/open path reached none of them. In a WebView it is worse: there is " +
    "no `blob:` download and no `navigator.share`, so every one of those paths delivers nothing at all.",
  () =>
    scanLines(
      /*
       * DERIVED from the three mechanisms, not from a list of files — a sixth call site written next month fails
       * on the day it is written.
       *
       *   `.download =`            the anchor download attribute, whatever the variable is called
       *   `saveAs(`                file-saver
       *   `createElement("a")`     the anchor itself, which is what makes this precise
       *
       * ⚠️ Deliberately NOT a grep for `.click()`. Two legitimate call sites open a FILE PICKER that way
       * (`fileInputRef.current?.click()` in `import-patients-dialog` and `doctor-document-identity-dialog`), so a
       * bare `.click()` rule would report real code as a defect and get switched off. Anchoring on
       * `createElement("a")` catches the same mechanism at its root with nothing to exempt.
       */
      ALL_FILES.filter((f) => /^(app|components|lib)[\\/]/.test(relative(WEB_ROOT, f)) && rel(f) !== DELIVERY_HELPER),
      /\.download\s*=|\bsaveAs\s*\(|createElement\(\s*["']a["']\s*\)/,
    ),
);

check(
  "pdf-viewer-params",
  "N1",
  "No viewer-specific PDF URL fragment (`#toolbar=0`, `#navpanes=0`, …)",
  "Those are **Adobe/Chromium-only** parameters. Android WebView ignores them and renders the frame BLANK — a " +
    "white A4 rectangle with no error, which a dentist reads as a corrupted radiograph rather than as an " +
    "unsupported viewer. Two of the three `<iframe>` previews carried them. A PDF preview on a coarse pointer " +
    "delivers the file instead (`components/patient-file-pdf-preview.tsx`); nothing needs to ask a viewer to hide " +
    "its own toolbar.",
  // Only parameter names no app query string would ever legitimately use. `page=` is deliberately absent: it is a
  // real pagination parameter in this app, and a check that fires on `?page=2` would be turned off within a week.
  () => scanLines(tsx(), /\b(?:toolbar|navpanes|scrollbar|statusbar|pagemode)=/),
);

/** The one browser-side writer of clinic-API request headers. Everything else asks it. */
const API_HEADER_BUILDER = "lib/api/client.ts";

check(
  "api-headers",
  "N3",
  "Only `lib/api/client.ts` builds an `Authorization: Bearer` header for the clinic API",
  "The header a browser sends is now more than the token: `apiHeaders()` also attaches `X-Client-Version` from " +
    "the native shell bridge, which is what lets the server refuse a build below its floor (AC-31). Fourteen raw " +
    "`fetch` sites across eight modules used to hand-write the object themselves — every PDF, every CSV export, " +
    "every patient-file upload — so a hand-rolled fifteenth would send the token and silently omit the version, " +
    "and the floor would apply to some of the app and not the rest. That is not a failure anyone can see: the " +
    "calls keep working, right up until the one release where they must not. Import `apiHeaders` instead.",
  () =>
    scanLines(
      /*
       * Two roles are outside this rule, and both are roles rather than filenames — an allow-list of files is a
       * check that stops working:
       *
       *   lib/api/client.ts    the builder itself. Somebody has to write the header.
       *   app/**\/route.ts      a Next ROUTE HANDLER. It runs on the server, so there is no `window` and no
       *                        bridge to read a version from — and AC-32 states outright that a server-side BFF
       *                        hop sends no version header and is accepted unchanged. Adding one there would be
       *                        a header that describes nothing.
       */
      ALL_FILES.filter((f) => {
        const r = rel(f);
        return r !== API_HEADER_BUILDER && !/^app\/.*\/route\.ts$/.test(r);
      }),
      /Authorization.*Bearer/,
    ),
);

check(
  "local-network-wording",
  "N7",
  "No user-facing string sends someone to check the « réseau local »",
  "The same clinic server is reached over a LAN, over office Wi-Fi, over a mobile network and from a native " +
    "shell on cellular — so a message naming the local network is false everywhere except the offline-LAN " +
    "install, and it points a dentist at something that is not there (AC-64). Three strings carried it, and the " +
    "one that mattered most was the least obvious: `NETWORK_ERROR_MESSAGE` in `lib/api/client.ts`, which *every* " +
    "failed call in the app surfaces. Nothing typed can catch this and no reviewer sees it at the width they " +
    "happen to be working at. Say « Vérifiez votre connexion » instead — true on every network.",
  // No exemption list: there is no deployment where this wording is the right thing to tell a user, and a
  // per-file exemption here is how the phrase would creep back into the next French string.
  () => scanLines(tsx(), /réseau local/i),
);

check(
  "password-change-is-a-destination",
  "N8",
  "Every page that renders an API refusal also routes `must_change_password`",
  "`PlatformAccountStateMiddleware` refuses EVERY console read while a bootstrapped account still holds the " +
    "one-time password `platform-account create` printed. A page that renders that refusal as prose tells the " +
    "operator « Portefeuille illisible » about a server that read fine and answered precisely — and since no " +
    "screen links to `/mot-de-passe`, the very first account created on a deployment meets a dead end on its " +
    "first screen with no way forward but typing the URL. It shipped that way, on all three reading pages at " +
    "once, which is the point: this is a per-page decision that has to be made the same way every time. Call " +
    "`redirectIfPasswordChangeRequired(error)` first in the catch. Nothing typed can catch this — the refusal " +
    "is a valid `ConsoleApiError` and rendering it compiles.",
  () =>
    scanLines(
      // Keyed on the ROLE, not a file list: a page that knows about `ConsoleApiError` is a page that renders a
      // refusal, and every one of those is on a route a bootstrapped account can arrive at directly.
      pages().filter((f) => !read(f).includes("redirectIfPasswordChangeRequired")),
      /ConsoleApiError/,
    ),
);

// ── run ─────────────────────────────────────────────────────────────────────────────────────────────────────

const only = process.argv.find((a) => a.startsWith("--only="))?.slice("--only=".length);

let failed = 0;

console.log("");
console.log("  check-responsive — mobile & tablet mechanical gate (AC-50)");
console.log("  " + "─".repeat(90));

for (const c of checks) {
  if (only && c.id !== only) continue;
  const hits = c.run();

  if (hits.length === 0) {
    console.log(`  ✓ ${c.id.padEnd(18)} ${c.part}  ${c.title}`);
    continue;
  }

  failed++;
  console.log("");
  console.log(`  ✗ ${c.id.padEnd(18)} ${c.part}  ${c.title}`);
  console.log(`      ${c.why}`);
  console.log("");
  for (const h of hits.slice(0, 40)) {
    const where = h.line ? `${h.file}:${h.line}` : h.file;
    console.log(`        ${where}  ${h.text}`);
  }
  if (hits.length > 40) console.log(`        … and ${hits.length - 40} more`);
  console.log("");
}

console.log("  " + "─".repeat(90));
if (failed > 0) {
  console.log(`  ${failed} of ${checks.length} check(s) failed.`);
  console.log("");
  process.exit(1);
}
console.log(`  All ${checks.length} checks passed.`);
console.log("");
process.exit(0);
