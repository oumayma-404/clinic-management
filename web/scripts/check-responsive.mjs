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
 * STAGED ENABLEMENT
 * Every check is tagged with the part of `features/mobile-tablet-responsive/plan.md` that fixes it. A check
 * whose part has not landed yet reports as PENDING and does not fail the run — otherwise the gate would be red
 * from the moment it is written and would simply be ignored, which is how a check dies.
 *
 * As each part lands, delete its id from PENDING_PARTS below. That is one deliberate, visible line of
 * maintenance per part, and when the set is empty every check is enforced. Do NOT add per-file exemptions —
 * an allow-list that grows is a check that has stopped working.
 */

import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

const WEB_ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");
const SCAN_DIRS = ["app", "components", "lib", "contexts", "hooks"];

/** Parts not yet landed. Remove an id when that part is committed. */
const PENDING_PARTS = new Set(["P3", "P4", "P5", "P6", "P7", "P8"]);

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
  "No unprefixed `max-w-*` on a DialogContent / AlertDialogContent",
  "An unprefixed max-w is the same tailwind-merge group as the base `max-w-[calc(100%-2rem)]`, so the caller " +
    "wins and the mobile gutter dies — but it cannot beat `sm:max-w-lg`, which then clamps the dialog to 512 px " +
    "on every desktop. Prefix it (`sm:max-w-4xl`) so both survive.",
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
        // Tokenise every string-ish run in the tag; a prefixed class is one token (`sm:max-w-lg`).
        for (const token of tag.split(/[\s"'`{}()]+/)) {
          if (token.startsWith("max-w-")) {
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
const CARD_FALLBACK_EXEMPT = new Map([
  [
    "components/dashboard/collected-trend-chart.tsx",
    "2 columns, and the table IS the chart's accessible fallback — cards would be a fallback for a fallback",
  ],
  [
    "components/cnam-letter-values-card.tsx",
    "a form in a table: the value cell is an editable <Input> with a per-row save, not a value to read",
  ],
  [
    "components/factures/invoice-detail-modal.tsx",
    "4 read-only line columns inside a dialog that already fits — nothing to escape from",
  ],
  [
    "components/stock-table.tsx#mouvements",
    "the movement-history table lives in its own dialog at 4 columns; the main stock table IS converted",
  ],
]);

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

// ── run ─────────────────────────────────────────────────────────────────────────────────────────────────────

const only = process.argv.find((a) => a.startsWith("--only="))?.slice("--only=".length);
const strict = process.argv.includes("--strict"); // enforce every check regardless of PENDING_PARTS

let failed = 0;
let pending = 0;

console.log("");
console.log("  check-responsive — mobile & tablet mechanical gate (AC-50)");
console.log("  " + "─".repeat(90));

for (const c of checks) {
  if (only && c.id !== only) continue;
  const hits = c.run();
  const enforced = strict || !PENDING_PARTS.has(c.part);

  if (hits.length === 0) {
    console.log(`  ✓ ${c.id.padEnd(18)} ${c.part}  ${c.title}`);
    continue;
  }

  if (!enforced) {
    pending++;
    console.log(`  ○ ${c.id.padEnd(18)} ${c.part}  ${c.title}`);
    console.log(`      ${hits.length} hit(s) — PENDING, ${c.part} has not landed yet`);
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
  console.log(`  ${failed} check(s) failed, ${pending} pending.`);
  console.log("");
  process.exit(1);
}
console.log(`  All enforced checks passed${pending ? `, ${pending} pending (later parts).` : "."}`);
console.log("");
process.exit(0);
