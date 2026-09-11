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
 */

import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
// The one runtime import this gate makes, and it is the point of `phone-rule-matches-the-corpus`: the corpus is
// driven through the very library the browser ships, not through a re-implementation of it.
import { parsePhoneNumberFromString } from "libphonenumber-js/max";

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
  "A DialogContent / AlertDialogContent width override is prefixed at or above `md:`",
  "Two failures, one check. An UNPREFIXED max-w is the same tailwind-merge group as the base " +
    "`max-w-[calc(100%-2rem)]`, so the caller wins and the mobile gutter dies — but it cannot beat the base's " +
    "own prefixed clamp, which then holds the dialog at 512 px on every desktop. And an `sm:`-prefixed one is " +
    "the ambiguity P4 removed: the dialog presentation switches at `md:`, so between 640 and 767 px an " +
    "`sm:max-w-*` and the mobile sheet's width would both be live in different variants — twMerge keeps both " +
    "and the stylesheet order decides. " +
    "`md:`, `lg:`, `xl:` and `2xl:` all pass, because the rule is about the SWITCH, not about one breakpoint: " +
    "every one of those is at or above 768 px, where the desktop presentation is already in force, so none can " +
    "straddle the sheet's band. A dialog that widens again on a large screen (`md:max-w-2xl lg:max-w-4xl` — the " +
    "booking dialogs, which grow a second pane there) is two prefixed clamps in two variants, resolved by " +
    "variant order exactly as Tailwind intends. Write `md:max-w-*` or wider; never bare, never `sm:`.",
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
         * `md:` and above: see the `why` above. An unprefixed token loses the gutter, an `sm:` one straddles the
         * 640–767 px band where the mobile sheet is still in force, and everything from `md:` up is already
         * inside the desktop presentation.
         */
        for (const token of tag.split(/[\s"'`{}()]+/)) {
          if (/(^|:)max-w-/.test(token) && !/^(?:md|lg|xl|2xl):max-w-/.test(token)) {
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
  "dialog-owns-one-scroller",
  "N27",
  "A dialog's full-height scrolling middle is a `DialogBody`, so the primitive stands its own scroller down",
  "`DIALOG_DESKTOP` makes EVERY `DialogContent` a scroll container at `md:` and up, and the only thing that " +
    "turns it off is a descendant carrying `data-slot=\"dialog-body\"`. So a dialog that hand-rolls its " +
    "scrolling middle — `min-h-0 flex-1 overflow-y-auto` on a plain `div`, the exact classes `DialogBody` " +
    "sets — ends up with TWO nested scrollers, and writing `overflow-hidden` on the `DialogContent` to say " +
    "« I own my scrolling » does not help: it is unprefixed, so tailwind-merge keeps it AND the base's " +
    "`md:overflow-y-auto`, and the media-query utility wins above 768 px. Measured on both booking dialogs at " +
    "1440×900: the content computed `overflow-y: auto` with the form column scrolling inside it. " +
    "The outer one is dormant only while every child keeps shrinking, which is why this reads as intermittent " +
    "and keeps coming back — one child that cannot shrink turns it into a second full-height gutter. " +
    "Use `DialogBody` for the scrolling middle (it may be nested — the guard's selector is descendant-based), " +
    "or, when the region genuinely scrolls BOTH axes (a pan viewport for a document), say so on the " +
    "`DialogContent` with an explicit `md:overflow-hidden`.",
  () => {
    const hits = [];
    for (const file of tsx()) {
      const src = read(file);
      if (!/<DialogContent\b/.test(src)) continue;
      const lines = src.split(/\r?\n/);
      const inComment = commentMask(lines);
      // Real `\n` positions, not `length + 1` — see `page-scroller-contains-its-absolutes`, which lost a byte
      // per line on a CRLF checkout and read the wrong region.
      const lineStarts = [0];
      for (let i = 0; i < src.length; i++) if (src[i] === "\n") lineStarts.push(i + 1);
      const lineOf = (index) => {
        let lo = 0;
        let hi = lineStarts.length - 1;
        while (lo < hi) {
          const mid = (lo + hi + 1) >> 1;
          if (lineStarts[mid] <= index) lo = mid;
          else hi = mid - 1;
        }
        return lo;
      };

      /*
       * Every JSX opening tag, brace-aware so a multi-line `className={cn(…)}` is read whole — the same walk
       * `dialog-max-w` makes, for the same reason: two of these scrollers build their class list inside `cn()`
       * across several lines, and a per-line grep declares them clean while they carry the bug.
       */
      const tags = [];
      const re = /<([A-Za-z][A-Za-z0-9.]*)\b/g;
      let m;
      while ((m = re.exec(src)) !== null) {
        let i = m.index + m[0].length;
        let depth = 0;
        while (i < src.length) {
          const c = src[i];
          if (c === "{") depth++;
          else if (c === "}") depth--;
          else if (c === ">" && depth === 0) break;
          i++;
        }
        tags.push({ name: m[1], tag: src.slice(m.index, i), index: m.index });
      }

      // An explicit `md:overflow-hidden` on the DialogContent is the other honest way to stand the outer
      // scroller down, and the only one open to a region that must scroll sideways too.
      const optedOut = tags.some((t) => t.name === "DialogContent" && /\bmd:overflow-hidden\b/.test(t.tag));
      if (optedOut) continue;

      for (const t of tags) {
        if (inComment[lineOf(t.index)]) continue;
        const tokens = t.tag.split(/[\s"'`{}()[\],]+/);
        // `flex-1` + a scrolling overflow on one element IS a full-height scrolling middle, whatever it is named.
        if (!tokens.some((tok) => /(^|:)flex-1$/.test(tok))) continue;
        if (!tokens.some((tok) => /(^|:)overflow-(y-)?(auto|scroll)$/.test(tok))) continue;
        if (t.name === "DialogBody" || /data-slot=["']dialog-body["']/.test(t.tag)) continue;
        hits.push({
          file: rel(file),
          line: lineOf(t.index) + 1,
          text: `<${t.name}> scrolls full-height but is not a DialogBody`,
          full: t.tag.replace(/\s+/g, " ").slice(0, 120),
        });
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
  "popover-height-is-radix-measured",
  "P6",
  "No `PopoverContent` caps its own max-height — the base caps on Radix's measured available height",
  "A popover's constraint is the room left BETWEEN THE ANCHOR AND THE VIEWPORT EDGE, which only Radix can " +
    "measure and which it publishes as `--radix-popover-content-available-height`. A hand-written " +
    "`max-h-[70dvh]` measures the viewport instead, and tailwind-merge lets the caller's value win over the " +
    "base — so the override does not merely duplicate the cap, it DISABLES it. Measured on the odontogram's " +
    "tooth editor at 390x844: Radix flipped the panel above the tooth with 394 px of room, `70dvh` allowed " +
    "590.8 px, and the 428 px panel rendered at `y = -35` with its heading clipped off the top of the screen " +
    "and nothing to scroll. All three call sites in this repo had made the same guess, while `select.tsx` and " +
    "`dropdown-menu.tsx` had had the correct cap all along — the repo's dominant defect shape, at the " +
    "primitive layer. A popover that genuinely needs to be shorter should compose its own cap with a min() " +
    "against the Radix variable, and say why — which this deliberately still flags, so the reason gets " +
    "written down.",
  /*
   * ⚠️ **Tailwind v4 scans THIS FILE for class names**, so a bracketed utility written inside one of these
   * description strings is emitted as real CSS. Writing the illustrative form of the rule above — a bracketed
   * `max-h` with an ellipsis inside it, which is the shape a comment naturally reaches for — compiled to a
   * `max-height` with no value and took the whole stylesheet down with « Parsing CSS source code failed »,
   * pointing at a line of `globals.css` that nobody wrote. Describe a utility in words here, or quote one that
   * is genuinely valid (`max-h-[70dvh]` above is real, and merely emits a rule nothing uses).
   */
  /*
   * Scanned over the whole file rather than with `scanLines`, because the props of a long `<PopoverContent>`
   * are routinely wrapped onto their own lines and a per-line regex would only ever catch the one-liner form —
   * i.e. it would pass on exactly the call sites big enough to need the cap.
   */
  () => {
    const hits = [];
    for (const file of tsx()) {
      const src = read(file);
      const tag = /<PopoverContent\b/g;
      let m;
      while ((m = tag.exec(src)) !== null) {
        // Read forward to the end of the opening tag, ignoring `>` inside a quoted attribute value.
        let i = m.index + m[0].length;
        let quote = null;
        for (; i < src.length; i++) {
          const c = src[i];
          if (quote) { if (c === quote) quote = null; continue; }
          if (c === '"' || c === "'" || c === "`") { quote = c; continue; }
          if (c === ">") break;
        }
        const openTag = src.slice(m.index, i + 1);
        if (!/\bmax-h-\[/.test(openTag)) continue;
        hits.push({
          file: rel(file),
          line: src.slice(0, m.index).split("\n").length,
          text: (openTag.match(/\bmax-h-\[[^\]]*\]/) || ["une hauteur maximale"])[0],
          full: `<PopoverContent> caps its own height — this overrides the base's Radix-measured cap instead of tightening it`,
        });
      }
    }
    return hits;
  }
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
  "tooth-symbol-covers-every-condition",
  "P6",
  "Every `ToothCondition` the client knows has a glyph in `tooth-symbols.tsx`, and no glyph invents one",
  "The symbol view draws a tooth by looking its condition up in `TOOTH_SYMBOLS`. A condition with no entry " +
    "draws the bare tooth — no error, no warning, no console line: the state is simply not on the chart, and " +
    "the tooth reads as healthy. That is the exact shape of the defect this repo keeps finding (a correct map " +
    "wired to one of its call sites), and the six conditions added by `odontogram-plan-suggestions` are proof " +
    "the set grows. Compared in BOTH directions: a glyph for a condition the vocabulary dropped is dead code " +
    "that will be copied forward. `Sain` is excluded — it is the absence of a state and is never stored.",
  () => {
    const condFile = ALL_FILES.find((f) => rel(f) === "components/odontogram-conditions.ts");
    const symFile = ALL_FILES.find((f) => rel(f) === "components/tooth-symbols.tsx");
    if (!condFile || !symFile) {
      return [{ file: condFile ? "components/tooth-symbols.tsx" : "components/odontogram-conditions.ts", line: 0, text: "missing", full: "a file this check guards is gone — retarget or retire the check" }];
    }

    // Read the vocabulary out of `CONDITION_ORDER`, which is the list the picker and the legend already walk —
    // not out of `CONDITIONS`, whose keys could drift from what is offerable.
    const order = read(condFile).match(/export const CONDITION_ORDER\s*=\s*\[([\s\S]*?)\]/);
    const undrawn = read(symFile).match(/export const UNDRAWN_CONDITIONS\s*=\s*\[([\s\S]*?)\]/);
    if (!order || !undrawn) {
      return [{ file: rel(order ? symFile : condFile), line: 0, text: "unparsable", full: "CONDITION_ORDER / UNDRAWN_CONDITIONS no longer parse — this check is blind, fix it rather than deleting it" }];
    }
    const skip = new Set([...undrawn[1].matchAll(/"([A-Za-z]+)"/g)].map((m) => m[1]));
    const conditions = [...order[1].matchAll(/"([A-Za-z]+)"/g)].map((m) => m[1]).filter((c) => !skip.has(c));

    // The symbol map's own keys, taken from the object literal rather than from a hand-kept list.
    const body = read(symFile).match(/export const TOOTH_SYMBOLS[^{]*\{([\s\S]*?)\n\}/);
    if (!body) {
      return [{ file: rel(symFile), line: 0, text: "unparsable", full: "TOOTH_SYMBOLS no longer parses — this check is blind, fix it rather than deleting it" }];
    }
    const drawn = new Set([...body[1].matchAll(/^\s{2}([A-Z][A-Za-z]*):\s*\{/gm)].map((m) => m[1]));

    const hits = [];
    for (const c of conditions) {
      if (!drawn.has(c)) hits.push({ file: rel(symFile), line: 0, text: c, full: `\`${c}\` is charted but has no glyph — the symbol view would draw a healthy tooth for it` });
    }
    for (const c of drawn) {
      if (!conditions.includes(c)) hits.push({ file: rel(symFile), line: 0, text: c, full: `\`${c}\` has a glyph but is not in CONDITION_ORDER — dead symbol` });
    }
    return hits;
  }
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
    "components/dashboard/appointment-trend-chart.tsx",
    "the same 2 columns as its twin above, and the same reason: the table IS this chart's accessible fallback. " +
      "Note its sibling `appointment-status-chart` is NOT here — that one's table is 7 columns wide, which is " +
      "exactly the defect this check exists for, so it carries a real CardList",
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
  "agenda-scroll",
  "P5",
  "The agenda's week grid scrolls its own container, and the overlay's maths still lines up",
  "Three invariants that only a human eye would otherwise catch, and only at a width nobody develops at. " +
    "(a) The week grid must not be CLIPPED — `overflow-x-hidden` on the calendar is what AC-P3.14 forbids, and " +
    "it is the one place in the app that did it. (b) `HOUR_HEIGHT` must stay 48 and the week columns 120px: " +
    "the appointment overlay is positioned by `(100% - 60px) / 7` against a `w-max` wrapper of 60 + 7×120, so " +
    "the two numbers are an arithmetic contract. Change one and every block drifts sideways a few pixels per " +
    "column — a rendering-glitch-shaped maths error. (c) The fluid override must be `lg:`, not `md:`. At `md:` " +
    "the columns go `1fr` while the 256px rail is still expanded, so the whole 768–1023px tablet band shared " +
    "~514px across seven days: a ~61px appointment block, of which padding, gap and the duration badge leave " +
    "about 11px for the patient's name. The wrapper's own `md:w-full`/`lg:w-full` is half of the same " +
    "contract, so it is checked too — moving one side alone is what puts every block in the wrong column.",
  () => {
    const file = ALL_FILES.find((f) => rel(f) === "components/appointment-calendar.tsx");
    // Derived, not listed: if the calendar is ever renamed or split, this reports rather than silently
    // passing on a file that no longer exists — the failure mode a hardcoded path hides.
    if (!file) {
      return [{ file: "components/appointment-calendar.tsx", line: 0, text: "missing", full: "the agenda component this check guards is gone — retarget or retire the check" }];
    }

    const src = read(file);
    const hits = [];
    const lines = src.split(/\r?\n/);
    const inComment = commentMask(lines);

    lines.forEach((line, i) => {
      if (inComment[i]) return;
      if (/\boverflow-x-hidden\b/.test(line)) {
        hits.push({ file: rel(file), line: i + 1, text: "overflow-x-hidden", full: line.trim().slice(0, 110) });
      }
    });

    // The contract itself. Every number is read from the source rather than assumed, so this fails on a
    // change to EITHER side of `60 + 7 * 120 === wrapper width`.
    const hourHeight = src.match(/const HOUR_HEIGHT = (\d+)/)?.[1];
    if (hourHeight !== "48") {
      hits.push({ file: rel(file), line: 0, text: `HOUR_HEIGHT = ${hourHeight ?? "?"}`, full: "must be 48 — rows taller than it make appointment blocks drift upward" });
    }
    /*
     * `minmax(120px,1fr)`, not a bare `120px`: the wrapper carries `min-w-full`, so a container wider than the
     * grid's 900px intrinsic width stretches the wrapper while fixed tracks stay put — `100%` then means the
     * wrapper and every block drifts. A flexible track with a 120px floor absorbs the surplus, and under `w-max`
     * (indefinite space) it still resolves to exactly 120px. Both halves of the pattern are therefore checked.
     */
    const weekCol = src.match(/grid-cols-\[60px_repeat\(7,minmax\((\d+)px,1fr\)\)\]/)?.[1];
    if (weekCol !== "120") {
      hits.push({ file: rel(file), line: 0, text: `week column = ${weekCol ?? "?"}`, full: "must be `minmax(120px,1fr)` — `(100% - 60px) / 7` over a 60+7×120 wrapper resolves to exactly that" });
    }
    // The breakpoint at which the columns go fluid, and the wrapper that must switch with them.
    const fluidAt = src.match(/\b(sm|md|lg|xl):grid-cols-\[60px_repeat\(7,minmax\(0,1fr\)\)\]/)?.[1];
    if (fluidAt !== "lg") {
      hits.push({ file: rel(file), line: 0, text: `fluid columns at ${fluidAt ?? "?"}:`, full: "must be `lg:` — at `md:` the 768-1023px tablet band shares ~514px across seven days and the patient's name gets ~11px" });
    }
    // Quoted, so the prose above the constant cannot satisfy the check on its own — this file's comments
    // deliberately quote the classes they discuss, which is the same trap `commentMask` exists for.
    if (!/"w-max min-w-full lg:w-full"/.test(src)) {
      hits.push({ file: rel(file), line: 0, text: "week wrapper", full: "must be `w-max min-w-full lg:w-full` — it has to go fluid at the same breakpoint as WEEK_COLS or the overlay's `100%` stops meaning the grid" });
    }
    return hits;
  }
);

check(
  "agenda-phone-week-gutter",
  "P5",
  "The phone week grid's time column and the overlay's arithmetic still agree",
  "`agenda-scroll` above holds the DESKTOP half of the same contract (60 + 7x120 against `(100% - 60px) / 7`). " +
    "Below `md:` Semaine is a different grid — seven fluid tracks in a `w-full` wrapper — so `WEEK_COLS_PHONE`'s " +
    "leading track and the phone value of `gutterPx` are one number written twice, and the appointment overlay " +
    "resolves `(100% - {gutter}px) / 7` against it. Change either alone and every block on the phone's week sits " +
    "a fixed number of pixels right of its own column, with the last day overhanging the grid — an arithmetic " +
    "error shaped exactly like a rendering glitch, and one no type and no desktop eye pass can see. Both numbers " +
    "are read out of the source, and the band expressions are checked for the literal `60` the desktop pair used " +
    "to hardcode.",
  () => {
    const file = ALL_FILES.find((f) => rel(f) === "components/appointment-calendar.tsx");
    if (!file) {
      return [{ file: "components/appointment-calendar.tsx", line: 0, text: "missing", full: "the agenda component this check guards is gone — retarget or retire the check" }];
    }

    const src = read(file);
    const hits = [];

    const phoneCol = src.match(/const WEEK_COLS_PHONE = "grid-cols-\[(\d+)px_repeat\(7,minmax\(0,1fr\)\)\]"/)?.[1];
    const phoneGutter = src.match(/const GUTTER_PHONE = (\d+)/)?.[1];
    if (!phoneCol || !phoneGutter) {
      hits.push({ file: rel(file), line: 0, text: `WEEK_COLS_PHONE=${phoneCol ?? "?"} GUTTER_PHONE=${phoneGutter ?? "?"}`, full: "both constants must exist and keep their shape — the phone week grid's contract is unreadable without them" });
    } else if (phoneCol !== phoneGutter) {
      hits.push({ file: rel(file), line: 0, text: `${phoneCol}px column vs ${phoneGutter}px gutter`, full: "WEEK_COLS_PHONE's leading track must equal GUTTER_PHONE — the overlay resolves `(100% - GUTTER_PHONE) / 7` against that column" });
    }

    // `gutterPx` must BE the constant, not a second literal beside it.
    if (!/const gutterPx = isNarrow \? GUTTER_PHONE : 60/.test(src)) {
      hits.push({ file: rel(file), line: 0, text: "gutterPx", full: "must read `isNarrow ? GUTTER_PHONE : 60` — a literal here is the second copy this check exists to prevent" });
    }
    // And the week band expressions must be derived from it rather than hardcoding the desktop 60.
    const lines = src.split(/\r?\n/);
    const inComment = commentMask(lines);
    lines.forEach((line, i) => {
      if (inComment[i]) return;
      if (/const weekBand(Left|Width)Expr/.test(line) && /100% - 60px/.test(line)) {
        hits.push({ file: rel(file), line: i + 1, text: "100% - 60px", full: "must be `100% - ${gutterPx}px` — the phone's week gutter is not 60, so a literal puts every block in the wrong column below `md:`" });
      }
    });
    return hits;
  }
);

check(
  "agenda-gestures",
  "G1",
  "The agenda's drag gestures can still identify the cells they act on",
  "The two grid gestures resolve their target through the DOM — `elementFromPoint` then `dataset` — because a " +
    "week grid has 168 cells and a move may cross day columns, so per-cell handlers are neither affordable nor " +
    "able to answer « the pointer is over no cell at all ». That makes the attributes a **contract between two " +
    "files that nothing type-checks**: `agenda-grid-drag.ts` reads `dataset.agendaDay`/`agendaHour`, and " +
    "`appointment-calendar.tsx` has to emit them. Rename either side, or add a third grid branch and forget the " +
    "props, and `tsc` is silent, the build is clean, the grid looks perfect and **dragging simply stops doing " +
    "anything** — the exact shape of defect this gate exists for. The `data-time-slot` half is older and worse " +
    "to lose: the « maintenant » line and the opening scroll both find their row with it.",
  () => {
    const hookFile = ALL_FILES.find((f) => rel(f) === "components/agenda-grid-drag.ts");
    const gridFile = ALL_FILES.find((f) => rel(f) === "components/appointment-calendar.tsx");
    // Derived, not listed: a renamed or split file reports rather than passing on a file that is not there.
    if (!hookFile || !gridFile) {
      return [{ file: hookFile ? "components/appointment-calendar.tsx" : "components/agenda-grid-drag.ts", line: 0, text: "missing", full: "a file this check guards is gone — retarget or retire the check" }];
    }

    const hook = read(hookFile);
    const grid = read(gridFile);
    const hits = [];

    // The required attributes are READ OUT OF THE HOOK, so a new `dataset.x` is covered the day it is written.
    const required = [...new Set([...hook.matchAll(/\.dataset\.([A-Za-z][A-Za-z0-9]*)/g)].map((m) => m[1]))];
    for (const key of required) {
      const attr = `data-${key.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`)}`;
      if (!grid.includes(`"${attr}"`)) {
        hits.push({ file: rel(gridFile), line: 0, text: attr, full: `read as \`dataset.${key}\` in agenda-grid-drag.ts but never emitted — the gesture cannot resolve a cell` });
      }
    }

    // `data-time-slot` is consumed by this file's own DOM queries, so both halves live here and must agree.
    if (/\[data-time-slot/.test(grid) && !grid.includes('"data-time-slot"')) {
      hits.push({ file: rel(gridFile), line: 0, text: "data-time-slot", full: "queried by the « maintenant » line and the opening scroll, but no longer emitted" });
    }

    /*
     * A cell that starts a gesture must also be identifiable by it. Counting the two against each other is what
     * makes a THIRD grid branch safe: Jour and Semaine render the hour cell from separate branches today, and
     * whichever one forgot the props would lose the gesture silently in that view alone.
     */
    const starters = (grid.match(/beginCellGesture\(/g) ?? []).length;
    const labelled = (grid.match(/\{\.\.\.cellDataProps\(/g) ?? []).length;
    if (starters !== labelled) {
      hits.push({ file: rel(gridFile), line: 0, text: `${starters} gesture cell(s), ${labelled} labelled`, full: "every cell that calls `beginCellGesture` must spread `cellDataProps` — a branch with one and not the other drags nowhere" });
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
  "next-public-build-args",
  "N8",
  "Every `NEXT_PUBLIC_*` the code reads is declared as an `ARG` in `web/Dockerfile`",
  "`NEXT_PUBLIC_*` is substituted into the bundle by `npm run build`, so a Docker image can only receive one as a " +
    "**build arg** — and Docker **silently discards** a build arg the Dockerfile does not declare. So a compose " +
    "file can pass a value, the deploy can succeed, and the bundle still holds an empty string. This has now " +
    "happened three times in this file's history: `NEXT_PUBLIC_API_URL` baked `http://localhost:5000/api` into " +
    "every production image, and `NEXT_PUBLIC_META_APP_ID`/`_CONFIG_ID` shipped empty for the whole of the " +
    "WhatsApp forfait — « Connecter WhatsApp » answering « pas encore prête » for ever, which reads as a " +
    "transient hiccup rather than a value the build never got. Nothing typed can see it and no test can: the " +
    "code is correct, the deployment is correct, and only the image is wrong. Add the `ARG`/`ENV` pair.",
  () => {
    // Derived from BOTH sides rather than a list: a variable added to the code tomorrow is covered the day it is
    // written, which is the only kind of check that survives (an enumerated list cannot fail on the new case).
    const dockerfile = read(join(WEB_ROOT, "Dockerfile"));
    const declared = new Set(
      [...dockerfile.matchAll(/^\s*ARG\s+(NEXT_PUBLIC_[A-Z0-9_]+)/gm)].map((m) => m[1]),
    );

    const hits = [];
    for (const file of tsx()) {
      read(file)
        .split(/\r?\n/)
        .forEach((line, i) => {
          for (const m of line.matchAll(/process\.env\.(NEXT_PUBLIC_[A-Z0-9_]+)/g)) {
            if (!declared.has(m[1])) {
              hits.push({ file: rel(file), line: i + 1, text: m[1], full: line.trim() });
            }
          }
        });
    }
    return hits;
  },
);

check(
  "page-header-not-a-flex-item",
  "N9",
  "`<PageHeader>` is not wrapped in a hand-rolled flex row — its controls go through `actions`",
  "`PageHeader` is `flex flex-wrap items-end justify-between`: it spans the page and pushes its `actions` to " +
    "the far edge. Wrapped in a `flex … sm:flex-row` row beside a sibling cluster of controls, the header " +
    "becomes a flex item and shrinks to its title's width — so the controls bunch up against the heading with " +
    "the rest of the line left empty, and the page's one primary action stops being where it is on every " +
    "other screen. (It used to also cut the zone-tinted wash off mid-page with a hard vertical edge; that " +
    "wash is gone, and the layout half of the defect is not.) Five pages had it (`/caisse`, `/stock`, " +
    "`/lab-orders`, `/waiting-list`, `/recurring-series`), each having reinvented the row that " +
    "`PageHeader`'s own `actions` slot already is — " +
    "`actions` is `flex flex-wrap`, so it wraps below `sm:` exactly as the wrapper did. Pass the controls as " +
    "`actions={…}` and delete the wrapper. Nothing here is visible to `tsc` or to the eye at the width the " +
    "header happens to fit.",
  () => {
    const hits = [];
    for (const file of tsx()) {
      const lines = read(file).split(/\r?\n/);
      lines.forEach((line, i) => {
        if (!/<PageHeader\b/.test(line)) return;
        // Walk back to the nearest line that actually opens an element, skipping blanks and comment bodies.
        for (let j = i - 1; j >= 0 && j >= i - 40; j--) {
          const prev = lines[j].trim();
          if (!prev || prev.startsWith("//") || prev.startsWith("*") || prev.startsWith("/*") || prev.endsWith("*/")) continue;
          // A wrapper that lays its children out in a row at SOME width is the defect; a plain `<div>` or a
          // `flex-col`-only stack (which still gives the header the full width) is not.
          if (/<div\b[^>]*className=/.test(prev) && /\bflex\b/.test(prev) && !/^\{\/\*/.test(prev)) {
            const rowish = /(?:^|:)(?:flex-row|items-center|justify-between)/.test(prev);
            if (rowish) hits.push({ file: rel(file), line: j + 1, text: "<PageHeader> inside a flex row", full: prev.slice(0, 140) });
          }
          break;
        }
      });
    }
    return hits;
  },
);

check(
  "page-scroller-contains-its-absolutes",
  "N10",
  "`AppShell`'s `<main>` is `relative`, so the page scroller clips its own absolute children",
  "`overflow-y-auto` does NOT clip an absolutely-positioned descendant whose containing block lies OUTSIDE " +
    "the scroller — and Tailwind's `sr-only` sets `position: absolute`. With `<main>` left `position: static`, " +
    "every `sr-only` on the page (the charts' « Comparé aux 7 jours précédents », their tabular-fallback " +
    "notes) resolved against `<body>`, escaped the page scroller, and registered as DOCUMENT overflow. The " +
    "document then grew past the shell's `h-dvh` and the WINDOW became a third scroller onto blank space: " +
    "1168 px of it on the dashboard at 1440x900, and 2611 px at 390x844, where the header and the bottom bar " +
    "scroll off the screen with it. There is nothing to see at the far end, `tsc` cannot see it, and the " +
    "page's own scrollbar looks right the whole time — the only symptom is that scrolling does not stop when " +
    "the content does. `relative` makes `<main>` the containing block, so its `overflow-y-auto` clips them. " +
    "It is safe for the fixed overlays this shell hosts: `position: relative` creates no containing block for " +
    "`position: fixed`, which is exactly why it is the right tool and `transform` (banned in the shell's own " +
    "`animate-page-in` note, for that reason) is not.",
  () => {
    const file = ALL_FILES.find((f) => rel(f) === "components/app-shell.tsx");
    // A moved shell must fail loudly rather than pass vacuously — a check that greps a file that is gone is a
    // check that has stopped working.
    if (!file) return [{ file: "components/app-shell.tsx", line: 0, text: "shell not found", full: "AppShell has moved — repoint this check at its new home" }];
    const src = read(file);
    // Skipping comments is not optional here: this file's own doc block writes `<main>` in prose, and that is
    // the match a bare regex finds first.
    const srcLines = src.split(/\r?\n/);
    const srcComment = commentMask(srcLines);
    const hitLine = srcLines.findIndex((line, n) => !srcComment[n] && /<main\b/.test(line));
    if (hitLine === -1) return [{ file: rel(file), line: 0, text: "no <main>", full: "AppShell no longer renders <main> — repoint this check" }];
    // ⚠️ Walk the REAL newline positions instead of summing line lengths + 1. `srcLines` came from a
    // CR-LF-tolerant split, so a CRLF file's terminator is TWO characters and `+ 1` under-counts by one byte
    // per line: the slice below then starts mid-line, the brace-walk reads the wrong region, and a `<main>`
    // that IS `relative` gets reported as missing it. Dormant while this file happened to be LF, and exposed
    // the moment git handed it back as CRLF on a branch switch — so it fired on Windows and never in CI.
    let lineStart = 0;
    for (let n = 0; n < hitLine; n++) lineStart = src.indexOf("\n", lineStart) + 1;
    // Walk to the end of the opening tag, brace-aware, so the whole `className={cn(…)}` call is included.
    let i = lineStart + srcLines[hitLine].indexOf("<main") + "<main".length;
    let depth = 0;
    while (i < src.length) {
      const c = src[i];
      if (c === "{") depth++;
      else if (c === "}") depth--;
      else if (c === ">" && depth === 0) break;
      i++;
    }
    const tagLines = src.slice(lineStart, i).split(/\r?\n/);
    const inComment = commentMask(tagLines);
    // A BARE `relative`, never a prefixed one: the containing block has to exist at every width.
    const declared = tagLines.some((line, n) => !inComment[n] && /(?:^|["'`\s])relative(?:["'`\s]|$)/.test(line));
    if (declared) return [];
    return [{ file: rel(file), line: hitLine + 1, text: "<main> is not `relative`", full: "add `relative` to the always-applied classes on <main>" }];
  },
);

check(
  "version-from-a-read",
  "P9",
  "A round-tripped `version` comes from a server read, not from a prop or a list row",
  "Every update that echoes a `version` is checked against the row's `xmin`, and a mismatch is a 409 reading " +
    "« cet enregistrement a été modifié par quelqu'un d'autre ». A prop is a snapshot taken when the row was " +
    "clicked and a list row is as old as the last refetch — and the user's OWN save moves the version further " +
    "than the screen is told (saving a patient writes the patient, then each history entry, each of which touches " +
    "the patient row again). So the message fires on a record nobody else opened, and after a save that failed " +
    "partway it fires on every click until a full page reload. The row must be re-read: `useFreshVersion` for a " +
    "form, or a direct read before the write for a list-row action.",
  () => {
    // A version taken off an object — `version: x.version`, `version: a?.version ?? b.version`. A literal
    // (`version: 0`, the documented "not supplied") is not a round-trip and is not in scope.
    const SENDS = /version:\s*[A-Za-z_$][\w$]*\s*\??\./;
    // ⚠️ `Api.get*` / `Api.list*`, not only the bare two. A per-row action legitimately re-reads through a
    // named read (`cnamNomenclatureApi.listLetterValues()`, `usersApi.listPaged()`) and that is exactly the
    // shape this check asks for — a stricter pattern rejected the honest fix, and the answer to that would
    // have been a per-file exemption, which is how a check stops working.
    const READS = /(?:useFreshVersion|Api\.(?:get|list)[A-Za-z]*\()/;
    const hits = [];
    for (const file of tsx()) {
      const src = read(file);
      const lines = src.split(/\r?\n/);
      const inComment = commentMask(lines);
      const sender = lines.findIndex((line, i) => !inComment[i] && SENDS.test(line));
      if (sender === -1) continue;
      if (READS.test(src)) continue;
      hits.push({
        file: rel(file),
        line: sender + 1,
        text: "sends a version it never re-read",
        full: lines[sender].trim(),
      });
    }
    return hits;
  },
);

/** The one place a value gets wrapped in French guillemets. */
const QUOTE_HELPER = "lib/format.ts";

check(
  "french-quote-binding",
  "N1",
  "A value quoted into guillemets goes through `quoteFr()`, never `« ${x} »` with ordinary spaces",
  "An ordinary space is a BREAK OPPORTUNITY, so the closing guillemet is free to wrap onto a line of its own. " +
    "At 320 px `/fichiers` rendered « Aucun résultat pour « zzzznope » » with a final line containing nothing but " +
    "the closing guillemet, measured a full line below the text it closes. Unlike static prose the quoted value " +
    "is a search term, a file name or a patient's name — its width is unknown when the line is written, so it " +
    "cannot be eye-checked once and left alone. 52 sites across 26 files carried it. `quoteFr()` binds both " +
    "guillemets with a narrow no-break space (`U+202F`).",
  () =>
    scanLines(
      // DERIVED from the mechanism: an interpolation sitting between guillemets with a breakable space on either
      // side. Static prose is deliberately NOT matched — its width is known and reviewable at authoring time.
      ALL_FILES.filter((f) => rel(f) !== QUOTE_HELPER),
      /« \$?\{|\} »/,
    ),
);


check(
  "public-asset-not-guarded",
  "N9",
  "Every root-relative asset the code references is excluded from the auth middleware matcher",
  "Files under `public/` are served through Next's normal request pipeline, so `middleware.ts`'s matcher runs on " +
    "them and the auth guard answers **307 → /login** to anyone without a session. Every consumer of one is a " +
    "browser with no session by definition — the login screen's own mark and the icons the manifest names — so " +
    "each gets an HTML login page where it asked for an image. `/icon-192.png` shipped that way: a broken-image " +
    "glyph above « Connexion », on the one screen with nothing else to identify the product by, while the " +
    "manifest's icons failed identically and invisibly (a degraded installed-app tile). Nothing else can catch " +
    "it — the file IS in the image, the route answers 307 not 404, and redirecting an unauthenticated request " +
    "for an unknown path is correct behaviour. Add the filename to the matcher's alternation in `middleware.ts`.",
  () => {
    const source = read(join(WEB_ROOT, "middleware.ts"));
    // The matcher is one quoted string on one line; if that ever stops being true, fail loudly rather than
    // silently passing every path — a check that cannot find its subject must not report success.
    const line = source.split(/\r?\n/).find((l) => l.includes("(?!_next/static"));
    const pattern = line?.match(/'([^']+)'/)?.[1];
    if (!pattern) {
      return [{ file: "middleware.ts", line: 0, text: "could not read the matcher — this check cannot run" }];
    }
    const guards = new RegExp(`^${pattern}$`);

    // Root-relative asset literals: `src="/x.png"`, `href="/x.svg"`, and the metadata/manifest `"/x.png"` forms.
    // Only paths with a file extension and no further `/` — a real public/ file, not a route.
    // Comment lines are masked out, or the note explaining a removed path re-reports the path it removed —
    // which is how a check starts arguing with its own documentation and gets switched off.
    const referenced = new Map();
    for (const file of tsx()) {
      const lines = read(file).split(/\r?\n/);
      const inComment = commentMask(lines);
      lines.forEach((line, i) => {
        if (inComment[i]) return;
        for (const m of line.matchAll(/["'](\/[A-Za-z0-9._-]+\.[a-z]{2,5})["']/g)) {
          if (!referenced.has(m[1])) referenced.set(m[1], `${rel(file)}:${i + 1}`);
        }
      });
    }

    return [...referenced]
      .filter(([path]) => guards.test(path))
      .map(([path, where]) => ({ file: where.split(":")[0], line: Number(where.split(":")[1] ?? 0), text: `${path} is intercepted by the middleware` }));
  },
);

check(
  "clinic-code-gated",
  "N10",
  "Every surface that shows the clinic's join code gates on `useSelfRegistrationEnabled`",
  "Nothing in this product reads `ClinicCode` except the join path, so where self-registration is closed " +
    "(`HostedMultiTenant`) the code creates nothing — and a badge nobody can use, under « Communiquez ce code à " +
    "vos collègues », invites an admin to go hunting for a door that is not there. `multi-tenant-cloud` US-3 " +
    "gated the card on `/users` and MISSED the second copy in `clinic-settings.tsx`, which had no capability " +
    "read of any kind: the hosted deployment printed that sentence for months. Nothing can catch this by type " +
    "or by eye — the code is a real value and the sentence is grammatical; it is simply false on that profile. " +
    "Read the shared hook (`lib/hooks/use-password-policy.ts`) and gate on the flag, never on its negation.",
  () => {
    /*
     * Keyed on the ROLE — a file that RENDERS the code as content — rather than on a list of the two files that
     * do it today, which is what let the second one exist.
     *
     * ⚠️ « Renders it » is the whole test, and merely *mentioning* `clinicCode` is not it: `join-wizard.tsx`
     * carries the identifier four times and displays it never — it is the screen where somebody TYPES a code to
     * join, so it is the one surface that must keep working when self-registration is open and is gated at the
     * page level (`/join` renders `JoinUnavailable`). A check that flagged it would be demanding the opposite of
     * the rule on the one file that already satisfies it, and would have been switched off within a week.
     *
     * So the pattern is the JSX-child idiom both display sites use — the hole alone on its line, or between
     * tags — never `code: clinicCode` or `clinicCode={…}`.
     */
    const rendersIt = /(?:^|>)\s*\{(?:clinicCode|clinic\.code)\}\s*(?:<|$)/;
    const holders = tsx().filter((f) => read(f).split(/\r?\n/).some((l) => rendersIt.test(l)));

    // ⚠️ The "does it call the hook" test is COMMENT-MASKED, and it has to be: the comment beside each call site
    // names the hook, so a plain `includes` passes on a file whose call has been deleted and whose note about it
    // stayed — which is exactly how this check first reported green against a deliberate violation.
    const callsHook = (file) => {
      const lines = read(file).split(/\r?\n/);
      const inComment = commentMask(lines);
      return lines.some((l, i) => !inComment[i] && l.includes("useSelfRegistrationEnabled"));
    };

    return scanLines(holders.filter((f) => !callsHook(f)), rendersIt);
  },
);

check(
  "shell-save-route-is-a-method",
  "N12",
  "A native-shell save path is taken on `saveFile`, never on the bridge merely existing",
  "`window.__clinicShell` means « a shell is hosting this page », NOT « the shell can receive a file ». " +
    "`bridge.md`'s own table says the desktop needs no `saveFile` — « a WebView2 download works » — and " +
    "therefore no `maxFileBytes` to bound it, so its bridge carries `version` and `platform` alone. " +
    "`download.ts` branched on the bridge existing, and `clinic-file-vault` gave the desktop a bridge for the " +
    "first time: on Windows EVERY download then took the mobile path — above 25 Mo refused with a sentence " +
    "naming « l’application mobile », below it calling an undefined `saveFile` and reporting « Échec du " +
    "téléchargement ». Neither size worked, on the one platform that downloads natively, and nothing could see " +
    "it: the types make every bridge member optional, so `shell.saveFile(…)` type-checks. Branch on " +
    "`typeof shell.saveFile === \"function\"` and let a shell without it fall through to the browser routes.",
  () => {
    // Keyed on the ROLE — a file that calls `saveFile` across the bridge — rather than on `download.ts` by
    // name, so a second delivery path written later is covered on the day it is written.
    const callers = tsx().filter((f) => /\.saveFile\s*\(/.test(read(f)));

    // ⚠️ Comment-masked: this check's own prose names both `saveFile` and the guard, and a plain `includes`
    // would pass on a file whose guard was deleted and whose explanation stayed — the failure mode the
    // `clinic-code-gated` check documents right above.
    const guards = (file) => {
      const lines = read(file).split(/\r?\n/);
      const masked = commentMask(lines);
      return lines.some(
        (l, i) => !masked[i] && /typeof\s+\w+(?:\?)?\.saveFile\s*===\s*["']function["']/.test(l),
      );
    };

    return scanLines(callers.filter((f) => !guards(f)), /\.saveFile\s*\(/);
  },
);

check(
  "patient-name-is-a-link",
  "N13",
  "A patient's name rendered beside their id is the link to their fiche",
  "The pattern existed on six screens and was missing from eight — la caisse, les chèques, les factures, les " +
    "plans, les rappels, le détail d'une facture. A name that is a door on one screen and inert on the next " +
    "teaches nobody anything, and nothing catches it: the markup is valid, the name is correct, it simply does " +
    "not go anywhere. Render it through `PatientNameLink`, which also carries the two details a hand-written " +
    "`<Link>` drops — underlined AT REST (a touch screen has no hover to reveal it) and `coarse:min-h-11` for " +
    "the 44px target. A row whose name has no id beside it is exempt: there is nowhere to point.",
  () => {
    /*
     * Keyed on the ROLE — a file that renders `{x.patientName}` as JSX content while the same object also
     * carries `patientId` — never on a list of files, which is what let the eight accumulate.
     *
     * Two deliberate exemptions, both because the name is NOT a navigation affordance there:
     *  - `patient-name-link.tsx` itself, which is the implementation.
     *  - the agenda's calendar blocks, where the name sits inside a drag handle: a link inside a drag target
     *    fights the gesture, and the appointment dialog it opens already carries the link (commit df201ded).
     */
    const EXEMPT = /patient-name-link\.tsx$|appointment-calendar\.tsx$/;
    /*
     * The name must be the WHOLE of its element. That is the discriminator between an identity and prose:
     * `<TableCell>{r.patientName}</TableCell>` names the row's person, whereas
     * `<span>{x.patientName}</span> sera supprimée` is a sentence — and a link inside a deletion confirmation
     * invites navigating away mid-decision, which is worse than no link at all.
     */
    const rendersName = /(?:^|>)\s*\{\s*\w+(?:\?)?\.patientName[^}]*\}\s*(?:<\/[\w.]+>)?\s*$/;

    const offenders = [];
    for (const f of tsx()) {
      if (EXEMPT.test(f)) continue;
      const src = read(f);
      if (!src.includes(".patientId")) continue;
      if (src.includes("PatientNameLink")) continue;
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      lines.forEach((l, i) => {
        if (!masked[i] && rendersName.test(l)) {
          offenders.push({ file: f, line: i + 1, text: "patientName rendered without a link to the fiche" });
        }
      });
    }
    return offenders;
  },
);

check(
  "idle-limit-follows-the-device",
  "N15",
  "The inactivity limit is chosen by `trusted`, and the lock screen names no duration",
  "`idleLimitMinutes` answers two questions that must stay separate: `trusted` decides HOW LONG the wait is, " +
    "`canLock` decides WHAT HAPPENS at the end of it. It used to lead with `if (canLock) return " +
    "DEFAULT_IDLE_LIMIT_MINUTES`, which handed a lockable device 30 minutes however trusted it was — so " +
    "« Rester connecté sur cet appareil » had no effect on interruptions on the desktop app, the one platform " +
    "most likely to have it ticked, and a practitioner got Windows Hello every half hour all day with a patient " +
    "in the chair. Nothing failed; the feature simply did not do the thing it was for. The second half is the " +
    "same defect in prose: the lock card said « après 30 minutes d'inactivité », a number it is never told and " +
    "which is now wrong on exactly the trusted device that waits 8 h.",
  () => {
    const offenders = [];

    // Half one: the limit must not be decided by `canLock`.
    const limitLines = read("lib/auth/idle-limit.ts").split(/\r?\n/);
    const limitMask = commentMask(limitLines);
    limitLines.forEach((line, i) => {
      if (limitMask[i]) return;
      if (/\bif\s*\(\s*canLock\s*\)/.test(line)) {
        offenders.push({
          file: "lib/auth/idle-limit.ts",
          line: i + 1,
          text: "`canLock` decides the ending, never the duration — branch on `trusted` for the limit",
        });
      }
    });

    // Half two: the lock card must not name a duration it is not given.
    const gateLines = read("components/session-lock-gate.tsx").split(/\r?\n/);
    const gateMask = commentMask(gateLines);
    gateLines.forEach((line, i) => {
      if (gateMask[i]) return;
      // A digit (or a spelled-out small number) immediately followed by a unit, in user-facing prose.
      if (/\b(\d+|une|deux|trente|huit)\s*(minutes?|heures?|min\b|h\b)/i.test(line)) {
        offenders.push({
          file: "components/session-lock-gate.tsx",
          line: i + 1,
          text: "the limit is 30 min or 8 h depending on the device — say « une période d'inactivité »",
        });
      }
    });

    return offenders;
  },
);

check(
  "agreed-cost-reaches-the-fiche",
  "N14",
  "A booked act's negotiated price is carried into the fiche de soins at every prefill site",
  "« Prix pour ce rendez-vous » exists so a price haggled on the telephone is the price billed. But the fiche " +
    "does NOT read the appointment's act rows for pricing — it resolves each row's `procedureTypeId` back to " +
    "the CATALOGUE, and `applyProcedure` then prices the act from `defaultCost`. So a prefill site that " +
    "dispatches `applyAppointment` or `addFromProcedure` without `agreedCost` shows the negotiated figure in " +
    "the booking dialog and silently reverts to the tarif in the fiche — worse than not having the feature, " +
    "because the dentist has been given a number to trust. There are two such sites (the lead act, and the " +
    "« aussi prévu à ce rendez-vous » shortcuts) and they are reached by different code paths, which is exactly " +
    "how one of them gets fixed and the other does not. Pass `agreedCost` from the booked ROW, never from the " +
    "catalogue entry.",
  () => {
    const offenders = [];

    /*
     * `applyAppointment` is held by the TYPE, not by a grep: `BookedActPrefill.agreedCost` is required and
     * explicitly nullable, so a caller that omits the price does not compile. What a grep must still hold is
     * that the field stays mandatory — `agreedCost?:` would make every omission silent again.
     */
    const storeLines = read("components/record/use-session-acts.ts").split(/\r?\n/);
    const storeMask = commentMask(storeLines);
    const store = storeLines.map((l, i) => (storeMask[i] ? "" : l)).join("\n");
    const carrier = /export interface BookedActPrefill\s*\{[^}]*\}/.exec(store);
    if (!carrier) {
      offenders.push({
        file: "components/record/use-session-acts.ts",
        text: "BookedActPrefill not found — the guard cannot check the carrier and must not pass",
      });
    } else if (!/\bagreedCost\s*:/.test(carrier[0])) {
      offenders.push({
        file: "components/record/use-session-acts.ts",
        text: "BookedActPrefill.agreedCost is optional or gone — an omitted price reverts to the tarif in silence",
      });
    }

    // `addFromProcedure` passes the price inline, so its dispatch sites are the grep's half.
    const PREFILL = /type:\s*"addFromProcedure"/;
    for (const f of tsx()) {
      const src = read(f);
      if (!PREFILL.test(src)) continue;
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      lines.forEach((l, i) => {
        if (masked[i] || !PREFILL.test(l)) return;
        // The dispatch object may span lines, so the window is the statement rather than the one line.
        if (!/agreedCost/.test(lines.slice(i, i + 8).join("\n"))) {
          offenders.push({
            file: f,
            line: i + 1,
            text: "addFromProcedure dispatched without agreedCost — the fiche will re-price from the catalogue",
          });
        }
      });
    }
    return offenders;
  },
);

check(
  "plan-step-travels-with-the-act",
  "N16",
  "A booked devis STEP reaches the wire and survives a re-save, everywhere the act does",
  "A séance of a multi-step act carries `treatmentPlanItemStepId` beside `treatmentPlanItemId`, and the " +
    "server keys its duplicate rules on the PAIR. Two consequences, both silent. (1) A payload built without " +
    "the step sends « préparation » and « empreinte » as the same act twice, which the server refuses outright " +
    "— the feature rejected by its own client, on the one booking it exists for. (2) `SetProcedures` replaces " +
    "the whole list, so a hydration that drops the step makes the NEXT save of that visit — rescheduling it, " +
    "editing its note, anything — quietly forget which step the séance was for; the fiche then advances the " +
    "act's next pending step instead, marking the wrong one réalisé. This is `agreed-cost-reaches-the-fiche`'s " +
    "trap one field along, and it fails the same way: no error, plausible screens, wrong record.",
  () => {
    const offenders = [];

    // Half one: the single payload builder must emit the field. Every booking dialog goes through it precisely
    // so this is one fact in one place.
    const pickerLines = read("components/appointment-acts-picker.tsx").split(/\r?\n/);
    const pickerMask = commentMask(pickerLines);
    const picker = pickerLines.map((l, i) => (pickerMask[i] ? "" : l)).join("\n");
    const builder = /export function toProcedurePayloads\([^)]*\)[^{]*\{[\s\S]*?\n\}/.exec(picker);
    if (!builder) {
      offenders.push({
        file: "components/appointment-acts-picker.tsx",
        text: "toProcedurePayloads not found — the guard cannot check the payload builder and must not pass",
      });
    } else if (!/treatmentPlanItemStepId/.test(builder[0])) {
      offenders.push({
        file: "components/appointment-acts-picker.tsx",
        text:
          "toProcedurePayloads does not send treatmentPlanItemStepId — two steps of one act will be refused " +
          "as the same act twice",
      });
    }

    /*
     * Half two: every site that rebuilds a `SelectedAct` from a stored appointment row must carry the step.
     * Derived from the hydration idiom itself (`treatmentPlanItemId: <row>.treatmentPlanItemId`) rather than
     * from a list of files, so a third booking surface is covered the day it is written.
     */
    /*
     * The trigger is a `SelectedAct` being built, marked by `planLabel` — the one field only that type has.
     * An earlier version matched `treatmentPlanItemId: <x>.treatmentPlanItemId` alone and fired on
     * `lib/api/appointments.ts`'s CREATE payload, where the plan link is a REQUEST-level scalar and there is
     * deliberately no step twin (the single-act shorthand books a whole act, never one step of one). Keying on
     * the marker keeps the guard on the rows that get re-sent through `SetProcedures`, which is where dropping
     * the step actually costs something.
     */
    const HYDRATE = /planLabel:/;
    const NAMES_THE_TYPE = /\bSelectedAct\b/;
    for (const f of tsx()) {
      const src = read(f);
      /*
       * Scoped to files that NAME the type, not to a list of filenames — `planLabel` alone is not unique
       * (`lib/api/subscription.ts` has one, about a subscription plan) and an exemption for that file would be
       * an allow-list, i.e. a check that has begun to stop working. A third booking surface imports
       * `SelectedAct` like the two that exist, so it is covered the day it is written.
       */
      if (!NAMES_THE_TYPE.test(src)) continue;
      if (!HYDRATE.test(src)) continue;
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      lines.forEach((l, i) => {
        if (masked[i] || !HYDRATE.test(l)) return;
        /*
         * The window is generous — these object literals run to twenty lines because each field carries the
         * note explaining why it is sent, and the step may sit either side of the marker. A tight window
         * reported all three real sites as offenders, which is the failure mode that gets a check deleted
         * rather than a defect fixed.
         */
        if (!/treatmentPlanItemStepId/.test(lines.slice(Math.max(0, i - 10), i + 24).join("\n"))) {
          offenders.push({
            file: f,
            line: i + 1,
            text:
              "a SelectedAct is built without deciding treatmentPlanItemStepId — the next save of that visit " +
              "drops which step it was for. Pass the row's step, or an explicit null where there is none.",
          });
        }
      });
    }

    // Tripwire: no candidates at all means the idiom changed, not that the product is clean.
    if (!offenders.length) {
      const hydrationSites = tsx().filter(
        (f) => {
          const src = read(f);
          return NAMES_THE_TYPE.test(src) && HYDRATE.test(src);
        },
      ).length;
      if (hydrationSites < 1) {
        offenders.push({
          file: "components/",
          text:
            "found no SelectedAct construction site — the scan is broken, and a guard that matches nothing " +
            "cannot hold anything",
        });
      }
    }

    return offenders;
  },
);

/**
 * The text of the JSX opening tag beginning at `i`, plus the index just past its `>`.
 *
 * <p>Brace-depth aware, so `className={cn(a, b)}` and `onClick={() => f(x > 1)}` are included whole instead of
 * being cut at the first `>` that happens to sit inside an expression.</p>
 */
function openingTag(src, i) {
  let depth = 0;
  let j = i;
  while (j < src.length) {
    const c = src[j];
    if (c === "{") depth++;
    else if (c === "}") depth--;
    else if (c === ">" && depth === 0) return [src.slice(i, j + 1), j + 1];
    j++;
  }
  return [src.slice(i), src.length];
}

/** 1-based line number of a character offset. */
const lineAt = (src, index) => src.slice(0, index).split("\n").length;

check(
  "table-hinge-fits-its-box",
  "P6",
  "A `TABLE_ONLY` table of five or more columns uses the `_LG` hinge",
  "§ 1 of .claude/rules/frontend-web.md puts the table/cards threshold at « roughly eight or more columns », " +
    "and that figure is sized for a table at PAGE level: an 820 px tablet leaves ~532 px once the 256 px rail " +
    "is subtracted. A table nested one level further — a Card inside a TabsContent — gets ~451 px, so the " +
    "threshold falls to five. Measured on the patient file at 820x1024: Rendez-vous (7 cols) rendered 764 px " +
    "into 451 px and hid 313 of them, Fichiers (5) hid 180, Dossiers medicaux (6) hid 71, and the column that " +
    "pays is always the last one — Actions. A dentist on an iPad literally could not see « Modifier » or " +
    "« Supprimer », which is what a trialling dentist reported as « editing the medical record does not work ». " +
    "Cells wrap, so a text column shrinks; a Button does not, because Button is `whitespace-nowrap shrink-0`, " +
    "and Actions is the cell that holds buttons. Five columns is therefore the honest ceiling for `md:` here. " +
    "Fix by switching that table to TABLE_ONLY_LG / CARDS_ONLY_LG — and check the card form carries the same " +
    "actions before you do, because § 0 says no capability is removed by a layout decision.",
  () => {
    const hits = [];
    for (const file of tsx()) {
      const src = read(file);
      const inComment = commentMask(src.split(/\r?\n/));
      const re = /<Table\b/g;
      let m;
      while ((m = re.exec(src)) !== null) {
        const line = lineAt(src, m.index);
        if (inComment[line - 1]) continue;
        const [tag, end] = openingTag(src, m.index);
        // The `_LG` hinge is the fix, so only the plain one is judged, and `\b` is what keeps
        // `TABLE_ONLY_LG` out rather than a negative lookahead that would also skip it.
        /*
         * ⚠️ It matches the TEMPLATE-LITERAL form too, and that omission made this check silently blind on the
         * surface it mattered most. It required `containerClassName={TABLE_ONLY}` exactly, while
         * `plan-workspace.tsx` — the devis workspace — interpolates it to add the card's own border, so BOTH
         * its tables were skipped. Measured at 820x1024 the actes table rendered 679 px into a 450 px box,
         * hiding 229 of them: the Etat badge clipped and the whole ACTION column — which is where « Planifier
         * l'etape » lives — off screen, on the tablet this product is used on most. Found by looking at the
         * page, not by the check whose entire job it was, which is the failure mode section 14 names: a
         * too-tight check is noisy and you notice, a too-loose one is indistinguishable from passing.
         */
        if (!/containerClassName=\{`?\$?\{?TABLE_ONLY\b/.test(tag)) continue;
        const close = src.indexOf("</Table>", end);
        const body = close === -1 ? src.slice(end) : src.slice(end, close);
        // `<TableHead\b` cannot match `<TableHeader`: between "d" and "e" there is no word boundary.
        const cols = (body.match(/<TableHead\b/g) || []).length;
        if (cols >= 5) {
          hits.push({ file: rel(file), line, text: `${cols} columns on the md: hinge — use TABLE_ONLY_LG` });
        }
      }
    }
    return hits;
  }
);

check(
  "icon-button-is-named",
  "P2",
  "An icon-only `<Button>` carries an `aria-label`",
  "§ 13: aria-label on every icon-only control. A `title` is not a substitute — it needs a hover, and this " +
    "app's primary device is a tablet, so on the machine it is actually used on the label does not exist at " +
    "all (§ 9.2). Unlabelled, a screen reader announces « bouton » and nothing more; in a table of ten fiches " +
    "that is ten identical announcements over a destructive action on a clinical record. Five shipped: the " +
    "patient file's Facturer / Modifier / Supprimer trio, stock-table's history button — sitting between two " +
    "siblings that were already labelled — and the ordonnance editor's medication remove, which had no title " +
    "either, so it was unlabelled on every channel. Name what the control acts on, not just the verb: " +
    "`Supprimer la fiche de soins du 12/03/2026`, the way the delete confirmation already does. " +
    "Deliberately conservative — a Button whose children include any {expression} is skipped, because its " +
    "label may well be that expression, so this check reports only what is certainly unnamed.",
  () => {
    const hits = [];
    for (const file of tsx()) {
      const src = read(file);
      const inComment = commentMask(src.split(/\r?\n/));
      const re = /<Button\b/g;
      let m;
      while ((m = re.exec(src)) !== null) {
        const line = lineAt(src, m.index);
        if (inComment[line - 1]) continue;
        const [tag, end] = openingTag(src, m.index);
        if (/\baria-label\b/.test(tag)) continue;
        if (tag.trimEnd().endsWith("/>")) continue;
        const close = src.indexOf("</Button>", end);
        if (close === -1) continue;
        const body = src.slice(end, close);
        if (/<Button\b/.test(body)) continue;
        let text = body.replace(/\{\/\*[\s\S]*?\*\/\}/g, "");
        text = text.replace(/<[^>]*>/g, "");
        if (text.includes("{")) continue;
        if (text.trim()) continue;
        hits.push({ file: rel(file), line, text: "icon-only <Button> with no aria-label" });
      }
    }
    return hits;
  }
);

check(
  "decoder-extensions-are-in-the-catalog",
  "P1",
  "Every extension `lib/files/decoders` claims is one the server's catalog actually accepts",
  "The decoder registry is deliberately NOT a mirror of `FileTypeCatalog` — whether a browser paints a format " +
    "unaided is the server's answer, while whether THIS build ships a decoder for it is a fact about the " +
    "bundle, and the two are unioned at the point of use rather than compared. But that union only works if " +
    "both halves are talking about the same file: a registry entry for an extension the catalog never accepts " +
    "is a decoder that can never run, and a typo (`tif` for `tiff`, `jpg` for `jpeg`) produces exactly that — " +
    "silently, because the format simply keeps showing its icon and nothing anywhere errors. This checks the " +
    "one direction that can be wrong; the other (a catalog format with no decoder) is the ordinary case, and " +
    "is what the typed placeholder is for.",
  () => {
    const registryPath = join(WEB_ROOT, "lib", "files", "decoders", "index.ts");
    const catalogPath = join(
      WEB_ROOT, "..", "api", "ClinicManagement.Application", "Common", "Files", "FileTypeCatalog.cs"
    );

    let registrySrc;
    let catalogSrc;
    try {
      registrySrc = readFileSync(registryPath, "utf8");
      catalogSrc = readFileSync(catalogPath, "utf8");
    } catch (error) {
      // A guard that quietly finds nothing to check is indistinguishable from one that passes.
      return [{ file: rel(registryPath), text: `could not read both sides: ${error.message}` }];
    }

    /*
     * Both sides are read from source rather than imported: this script is plain node with no TypeScript
     * loader and no way to run C#, and a hand-kept list here would be a third copy — i.e. the very drift
     * being checked for.
     */
    const table = registrySrc.match(/const DECODERS[^=]*=\s*\{([\s\S]*?)\n\}/);
    const declared = table ? [...table[1].matchAll(/^\s*([a-z0-9]+)\s*:/gm)].map((m) => m[1]) : [];

    // Every quoted extension the catalog names — the standalone entries and the `Entries` array alike.
    const accepted = new Set([...catalogSrc.matchAll(/new\[?\]?\s*\{([^}]*)\}/g)]
      .flatMap((m) => [...m[1].matchAll(/"([a-z0-9]+)"/g)].map((e) => e[1])));

    const hits = [];
    if (declared.length === 0) {
      hits.push({ file: rel(registryPath), text: "no DECODERS entries parsed — the table's shape changed" });
    }
    if (accepted.size === 0) {
      hits.push({ file: "api/…/FileTypeCatalog.cs", text: "no extensions parsed — the catalog's shape changed" });
    }

    for (const extension of declared) {
      if (accepted.has(extension)) continue;
      hits.push({
        file: rel(registryPath),
        line: lineAt(registrySrc, registrySrc.indexOf(`${extension}:`)),
        text: `"${extension}" has a decoder but is not an extension FileTypeCatalog accepts`,
      });
    }

    return hits;
  }
);

check(
  "monochrome1-has-one-owner",
  "P1",
  "Exactly one file decides what `MONOCHROME1` means",
  "A DICOM's `PhotometricInterpretation` says which end of the stored scale is bright, and `MONOCHROME1` runs " +
    "the opposite way from everything else. Get it wrong and a radiograph renders as a photographic negative of " +
    "itself — bone dark, air bright — which is not an error anywhere: it looks like a real image, and it reads " +
    "as a FINDING to anyone who does not know the file's own tag. Apply it twice and you are back to the " +
    "original, which is correct-looking and wrong beside every MONOCHROME2 file in the same drawer. " +
    "The flag is therefore decided once, in `lib/files/dicom/study.ts`, and applied once, inside " +
    "`lib/files/dicom/window.ts`'s unexported lookup-table builder (module privacy holds that half). This " +
    "check holds the first half: a second file comparing the literal is a second answer to the question. It is " +
    "also this repo's dominant defect shape — a correct rule wired to one call site — and the DICOM decoder " +
    "already lived through it once, when the flattened preview owned the whole pixel pipeline and the " +
    "interactive viewer needed the same bytes unwindowed. Prose mentioning the tag is fine; only a real string " +
    "literal counts, because only a comparison can disagree.",
  () => {
    // Derived, not listed: whichever file holds it, there must be exactly one — and the message names it, so a
    // deliberate move needs no edit here.
    const owners = [];
    for (const file of tsx()) {
      const src = read(file);
      const hits = [...src.matchAll(/["']MONOCHROME1["']/g)];
      if (hits.length > 0) owners.push({ file, src, at: hits[0].index });
    }

    if (owners.length === 0) {
      return [{ file: "lib/files/dicom/study.ts", text: "nothing compares against a MONOCHROME1 literal any more — the inversion is gone, or the check needs retargeting" }];
    }
    if (owners.length === 1) return [];

    return owners.map((owner) => ({
      file: rel(owner.file),
      line: lineAt(owner.src, owner.at),
      text: `"MONOCHROME1" is compared in ${owners.length} files`,
      full: `the photometric inversion must be decided in ONE place — also compared in ${owners.filter((o) => o !== owner).map((o) => rel(o.file)).join(", ")}`,
    }));
  }
);

check(
  "webgl-context-is-given-back",
  "P1",
  "Every WebGL renderer this build creates is destroyed with `forceContextLoss`",
  "A browser allows only a small number of live WebGL contexts — sixteen in Chrome — and when the next one is " +
    "asked for, the OLDEST is killed to make room. That is not an error anywhere: the victim is an " +
    "already-open viewer somewhere else on the page, which simply goes black. `renderer.dispose()` does NOT " +
    "release the context; it stays live until garbage collection, which is exactly the non-determinism this " +
    "must not have. `forceContextLoss()` is the only call that hands it back immediately. It matters most " +
    "where it is least visible: `lib/files/mesh/thumbnail.ts` builds one renderer PER FILE on the way up, so a " +
    "dentist dropping a dozen models onto the upload zone reaches the limit inside one gesture.",
  () => {
    const hits = [];
    for (const file of tsx()) {
      const src = read(file);
      const created = [...src.matchAll(/new\s+(?:THREE\.)?WebGLRenderer\s*\(/g)];
      if (created.length === 0) continue;
      // ⚠️ Comments stripped before the presence test. Without this, commenting the call OUT still leaves the
      // text in the file and the check reports green over the exact edit it exists to catch — which is how the
      // first red-proof of this rule passed when it should have failed.
      const code = src.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:])\/\/.*/g, "$1");
      if (/forceContextLoss\s*\(/.test(code)) continue;
      hits.push({
        file: rel(file),
        line: lineAt(src, created[0].index),
        text: "creates a WebGLRenderer and never calls forceContextLoss()",
        full: "dispose() alone leaves the context live until garbage collection; the 17th context kills the oldest, blanking an already-open viewer with no error",
      });
    }
    return hits;
  }
);

check(
  "mesh-scene-has-one-owner",
  "P2",
  "Exactly one file decides how a 3D model is lit and coloured",
  "Two things draw these files: the interactive viewer, and the still frame `lib/files/mesh/thumbnail.ts` " +
    "renders on the way up so a drawer of `.stl` shows the arches rather than grey boxes. If each built its " +
    "own scene, a tile and the viewer it opens would light the same model differently — which reads as the " +
    "FILE having changed, not as two code paths disagreeing, and there is no error to find. This is the same " +
    "shape `monochrome1-has-one-owner` holds for DICOM, and this repo's dominant defect: a correct answer " +
    "wired to one call site. Prose is fine; only a real `new THREE.Scene()` counts, because only a second " +
    "construction can disagree.",
  () => {
    const owners = [];
    for (const file of tsx()) {
      const src = read(file);
      const hits = [...src.matchAll(/new\s+(?:THREE\.)?Scene\s*\(/g)];
      if (hits.length > 0) owners.push({ file, src, at: hits[0].index });
    }

    if (owners.length <= 1) return [];

    return owners.map((owner) => ({
      file: rel(owner.file),
      line: lineAt(owner.src, owner.at),
      text: `a three.js Scene is constructed in ${owners.length} files`,
      full: `how a model is lit belongs in lib/files/mesh/scene.ts alone — also built in ${owners.filter((o) => o !== owner).map((o) => rel(o.file)).join(", ")}`,
    }));
  }
);

check(
  "devis-act-carries-its-plan-id",
  "N17",
  "A booking surface offering devis acts resolves the appointment's own `treatmentPlanId` from them",
  "An appointment records exactly ONE `treatmentPlanId`, and `AppointmentPlanLink.ValidateManyAsync` refuses " +
    "the whole save with « Le plan de traitement est requis pour lier l'acte. » the moment any act carries a " +
    "`treatmentPlanItemId` without it. So a dialog that offers a patient's devis acts cannot take that id from " +
    "whatever it was OPENED with: the create dialog's `presetPlanId` exists only when the devis workspace " +
    "launched it, and an act picked from « Actes du devis » — or accepted from the suggestion — belongs to a " +
    "devis the dialog was never told about. It must come from the acts actually attached, through " +
    "`resolveAttachedPlanId`, which is also the one place « deux devis dans une séance » is refused in French " +
    "instead of reaching the server as a validation error on a save the user thought had worked. This is the " +
    "feature turned down by its own client, and the third booking surface will meet it the day it is written.",
  () => {
    const offenders = [];

    /*
     * Derived from the OFFER, not from a list of files: a surface handing `planActs` to the picker is by
     * definition one where the user can attach an act whose devis the caller was never told about. That prop
     * name is unique to this, so no scoping by type name is needed.
     */
    const OFFERS = /\bplanActs=\{/;
    const RESOLVES = /\bresolveAttachedPlanId\s*\(/;

    let candidates = 0;
    for (const f of tsx()) {
      const src = read(f);
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      const code = lines.map((l, i) => (masked[i] ? "" : l)).join("\n");
      if (!OFFERS.test(code)) continue;
      candidates++;
      if (RESOLVES.test(code)) continue;
      offenders.push({
        file: rel(f),
        line: lineAt(code, code.search(OFFERS)),
        text:
          "offers devis acts but never calls resolveAttachedPlanId — attaching one and saving is refused with " +
          "« Le plan de traitement est requis pour lier l'acte. »",
      });
    }

    // Tripwire: the prop was renamed, so the scan is measuring nothing rather than finding nothing.
    if (candidates === 0) {
      offenders.push({
        file: "components/",
        text:
          "found no surface passing planActs= — the scan is broken, and a guard that matches nothing cannot " +
          "hold anything",
      });
    }

    return offenders;
  },
);

check(
  "booking-materialises-its-treatments",
  "N26",
  "A booking surface rendering the acts picker turns everything it promised into a real treatment when it saves",
  "A multi-séance act is split BY DEFAULT — `resolvePlannedProtocols` decides it, the picker shows the séances " +
    "and offers « Tout faire en une seule séance » — and a séance chosen through « c'est la suite d'une séance " +
    "précédente » is the same kind of promise. Both are form state and nothing exists on the server until the " +
    "save calls `materialiseTreatments`, so a dialog that renders the picker and does not call it books an " +
    "ordinary one-off visit while its own card said « Traitement en 3 séances » — no error, no toast, and the " +
    "treatment is simply never created. This is the shape the feature shipped in the first time, inverted: " +
    "the offer was a prop only the CREATE dialog passed, so the edit dialog rendered the sentence « cet acte " +
    "se fait normalement en 3 séances » above no control at all, and so did a create form with no patient " +
    "chosen yet — one dentist saw the button and another, on the same act, did not. Deriving the guard from " +
    "the picker rather than from a list of files is what covers the third booking surface on the day it is " +
    "written; ONE materialiser rather than two is what stops that surface from remembering half of it.",
  () => {
    const offenders = [];

    // Derived from the picker itself: rendering it is what puts the split on screen.
    const RENDERS = /<AppointmentActsPicker\b/;
    const MATERIALISES = /\bmaterialiseTreatments\s*\(/;

    let candidates = 0;
    for (const f of tsx()) {
      const src = read(f);
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      const code = lines.map((l, i) => (masked[i] ? "" : l)).join("\n");
      if (!RENDERS.test(code)) continue;
      candidates++;
      if (MATERIALISES.test(code)) continue;
      offenders.push({
        file: rel(f),
        line: lineAt(code, code.search(RENDERS)),
        text:
          "renders the acts picker but never calls materialiseTreatments — an act the card says is split into " +
          "séances, or a séance the card says continues a previous one, is saved as an ordinary one-off and no " +
          "treatment is ever created",
      });
    }

    // Tripwire: the component was renamed, so the scan is measuring nothing rather than finding nothing.
    if (candidates === 0) {
      offenders.push({
        file: "components/",
        text:
          "found no surface rendering <AppointmentActsPicker — the scan is broken, and a guard that matches " +
          "nothing cannot hold anything",
      });
    }

    return offenders;
  },
);

check(
  "session-total-skips-carried-acts",
  "N27",
  "The séance total is spread over the acts that can hold money, never over one the treatment prices",
  "An act carried by a treatment is 0 by rule — the treatment prices it once, `PlanCarriedActPricing` imposes " +
    "that server-side and `act-card` renders its price field read-only. « Total », three blocks below, wrote " +
    "straight past both: `distributeSessionTotal` filtered on `isActNamed` alone, so typing 150 on a séance " +
    "carrying a couronne moved the LOCKED field to « 150,000 » on screen and the save silently put it back to " +
    "0 — measured on the running app. On a MIXED séance it is quieter and worse: a couronne (carried) beside a " +
    "détartrage (not), and the typed total is split between them, so the détartrage is under-billed by " +
    "whatever share went to the act that cannot hold it. Nothing errors in either case. The rule is one line " +
    "and this is what keeps it: the filter is where the money is apportioned, and no reader downstream can " +
    "put it back.",
  () => {
    const offenders = [];

    // Derived from the function itself, not from a file list — a second implementation would be found too.
    const DECLARES = /\bfunction\s+distributeSessionTotal\s*\(/;
    let candidates = 0;

    for (const f of tsx()) {
      const src = read(f);
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      const code = lines.map((l, i) => (masked[i] ? "" : l)).join("\n");
      const at = code.search(DECLARES);
      if (at < 0) continue;
      candidates++;
      // The body, up to the next top-level declaration — enough to hold the filter and nothing else.
      const body = code.slice(at, at + 2400);
      if (/\bbilledOnPlan\b/.test(body)) continue;
      offenders.push({
        file: rel(f),
        line: lineAt(code, at),
        text:
          "distributeSessionTotal does not exclude `billedOnPlan` acts — typing in « Total » writes through " +
          "the read-only price of an act the treatment already prices, and the server discards it on save",
      });
    }

    // Tripwire: the function was renamed, so the scan is measuring nothing rather than finding nothing.
    if (candidates === 0) {
      offenders.push({
        file: "components/record/",
        text:
          "found no `distributeSessionTotal` — the scan is broken, and a guard that matches nothing cannot " +
          "hold anything",
      });
    }

    return offenders;
  },
);

check(
  "booked-plan-act-is-schedulable",
  "N28",
  "A plan minted inside a booking dialog is booked through `schedulablePlanItems`, never `plan.items[0]`",
  "`planIdByItem` — the map `resolveAttachedPlanId` reads to fill the appointment's own `treatmentPlanId` — is " +
    "built from `schedulablePlanItems`. So any surface that picks an act off a freshly created plan by another " +
    "rule is picking one the map may not contain, and the save is then refused outright with « Le plan de " +
    "traitement est requis pour lier l'acte. » `plan.items[0]` is that other rule, and it is not hypothetical: " +
    "« Montant du travail restant » makes `ContinueRecordedActCommand` build TWO acts — the one already carried " +
    "out, whose single « 1re séance » step is marked done on creation, and the remaining work, which is where " +
    "the séance still to book lives. items[0] is therefore `Done`, `schedulablePlanItems` drops it, and the " +
    "booking was refused on the one flow the continuation door exists for. The two symptoms before the refusal " +
    "were just as wrong and looked ordinary: the card showed the 30 DT act already on a note instead of the " +
    "10 DT being booked, and `planItemToPreset` offers only steps still to carry out — that act has none left — " +
    "so the séance carried no step at all. `attachPlanAct`'s own doc had said « never `plan.items[0]` » since " +
    "the day it was written; a sentence in a comment is not a guard.",
  () => {
    const offenders = [];

    // Derived from participation in the plan-link contract, not from a file list: a surface that names
    // `planIdByItem` or `attachPlanAct` is one that puts a devis act on a seance, whatever it is called.
    const PARTICIPATES = /\bplanIdByItem\b|\battachPlanAct\b/;
    const FIRST_ITEM = /\.items\[0\]/;
    let candidates = 0;

    for (const f of tsx()) {
      const src = read(f);
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      const code = lines.map((l, i) => (masked[i] ? "" : l)).join("\n");
      if (!PARTICIPATES.test(code)) continue;
      candidates++;
      const at = code.search(FIRST_ITEM);
      if (at < 0) continue;
      offenders.push({
        file: rel(f),
        line: lineAt(code, at),
        text:
          "picks a plan act with `.items[0]` \u2014 take `schedulablePlanItems(plan)[0]`, the same gate " +
          "`planIdByItem` is built from, or the booking is refused with \u00ab Le plan de traitement est requis " +
          "pour lier l'acte. \u00bb the moment the first act is Done",
      });
    }

    // Tripwire: both markers were renamed, so the scan is measuring nothing rather than finding nothing.
    if (candidates === 0) {
      offenders.push({
        file: "components/treatment-plans/",
        text:
          "found no surface naming `planIdByItem` or `attachPlanAct` \u2014 the scan is broken, and a guard " +
          "that matches nothing cannot hold anything",
      });
    }

    return offenders;
  },
);

check(
  "bridge-run-has-one-owner",
  "N30",
  "A travee is drawn from `buildBridgeRuns`, never from a chart's own idea of which teeth are one bridge",
  "A bridge's EXTENT cannot be read off the arch, and this product proved it. `odontogram.tsx` joined " +
    "bridge-marked teeth by adjacency within three intervening sites, so a bridge on 14/15/16 beside one on " +
    "17/18 drew ONE five-unit bar -- the dentist's own report -- and because the run's `planned` flag was " +
    "OR-ed across the merge, a FINISHED crown on 16 beside a planned bridge rendered dashed-red, i.e. " +
    "asserted as not yet placed: a false clinical statement on the one diagram read at a glance. It failed " +
    "the other way too, drawing no bar at all for abutments four sites apart. `BridgeCharting`'s own summary " +
    "on the server already says a bridge's SHAPE cannot come from position; its extent is the same rule one " +
    "level up. `bridge-runs.ts` is the one owner: it groups on the record's own `bridgeGroupId` and keeps the " +
    "adjacency scan only as the fallback for ungrouped legacy rows. Nothing errors when a second reader " +
    "re-derives this -- two charts of one mouth simply disagree. Derived from the PROP rather than from a " +
    "file list, so the third chart to draw a travee is covered the day it is written.",
  () => {
    const offenders = [];

    // Any component handed a bridgeSpan must have obtained it from the one owner.
    const CONSUMES = /\bbridgeSpan=\{/;
    const OWNER = /\bbuildBridgeRuns\s*\(/;
    // The module that DEFINES the prop is not a consumer of it, and neither is the owner itself.
    const EXEMPT = new Set(["components/tooth-symbols.tsx", "components/bridge-runs.ts"]);

    let candidates = 0;
    for (const f of tsx()) {
      const rp = rel(f);
      if (EXEMPT.has(rp)) continue;
      const src = read(f);
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      const code = lines.map((l, i) => (masked[i] ? "" : l)).join("\n");
      if (!CONSUMES.test(code)) continue;
      candidates++;
      if (OWNER.test(code)) continue;
      offenders.push({
        file: rp,
        line: lineAt(code, code.search(CONSUMES)),
        text:
          "passes `bridgeSpan` without calling `buildBridgeRuns` -- it is deciding for itself which teeth " +
          "are one bridge, which is the arch-adjacency guess that merged two bridges into one bar and drew " +
          "a finished crown as still-to-place",
      });
    }

    // Tripwire: the prop was renamed, so the scan is measuring nothing rather than finding nothing.
    if (candidates === 0) {
      offenders.push({
        file: "components/",
        text:
          "found no surface passing `bridgeSpan=` -- the scan is broken, and a guard that matches nothing " +
          "cannot hold anything",
      });
    }

    return offenders;
  },
);

check(
  "devis-balance-has-one-reader",
  "N18",
  "Every surface that prints a devis' « Reste » reads it through `displayedOutstanding`",
  "A plan's own `outstanding` is `totalPlanned − Σ its own installments`, and a devis bridged into a note " +
    "d'honoraires has an auto-raised échéance that will never see a payment, because the money went to the " +
    "note. So that figure reports the WHOLE devis as unpaid about a patient who owes nothing — measured on " +
    "4 of 4 bridged plans in a live database, two of them fully settled: one patient's file showed « Solde dû " +
    "31,000 DT » in its header and « Reste 120,000 DT » in the plan strip on the same page, 89 000 apart, and " +
    "another showed a red « Reste » with an « En retard » badge on a treatment paid in full. `isPlanBilled()` " +
    "already existed and was called in three places, none of which printed a balance; seven surfaces printed " +
    "one and none of them called it. That is this repo's dominant defect shape — a correct rule wired to one " +
    "consumer — and a reader is only worth extracting if nothing may go round it. `displayedOutstanding` " +
    "returns null rather than a wrong number, and names the note when the note is what owes.",
  () => {
    const offenders = [];

    /*
     * Derived from the read itself: any `.outstanding` on something plan-shaped. Scoped to the surfaces that
     * could render one — the treatment-plan components and the pages that mount them — because `outstanding`
     * is also a legitimate field on an invoice, an échéance and a receivable, and those are not this rule.
     */
    const READS = /\b(?:plan|p)\??\.outstanding\b/;
    const READER = /\bdisplayedOutstanding\s*\(/;

    let candidates = 0;
    for (const f of tsx()) {
      const relPath = rel(f);
      const scoped =
        relPath.includes("treatment-plans") ||
        relPath.includes("treatments/") ||
        relPath.includes("plan-next-action");
      if (!scoped) continue;

      const src = read(f);
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);

      for (let i = 0; i < lines.length; i++) {
        if (masked[i] || !READS.test(lines[i])) continue;
        candidates++;
        // The reader itself is the one place allowed to touch it — that is what makes it the reader.
        if (READER.test(src) || relPath.endsWith("plan-next-action.ts")) continue;
        offenders.push({
          file: relPath,
          line: i + 1,
          text:
            "prints a plan's own `.outstanding` — on a devis billed into a note that figure is the untouched " +
            "auto-échéance, i.e. the whole devis reported as unpaid. Read `displayedOutstanding(plan)`.",
        });
      }
    }

    // Tripwire: the field was renamed, so the scan is measuring nothing rather than finding nothing.
    if (candidates === 0) {
      offenders.push({
        file: "components/treatment-plans/",
        text:
          "found no `.outstanding` read at all — the scan is broken, and a guard that matches nothing cannot " +
          "hold anything",
      });
    }

    return offenders;
  },
);

check(
  "billed-plan-act-keeps-its-guard",
  "N19",
  "A stored devis act hydrates with `billedOnPlan`, not just `planLabel`",
  "« A devis act is 0 for this séance » is enforced by exactly one client function — `agreedCostOf`, which " +
    "returns a hard 0 for any act carrying `billedOnPlan`. So a surface that builds a `SelectedAct` with a " +
    "`treatmentPlanItemId` and no `billedOnPlan` has silently opted that act out of the only guard against " +
    "pricing it twice. Measured on one act minutes apart: created through the picker it was read-only 0,000 " +
    "with « facturé sur le devis » and a « Déjà facturé » notice; re-opened through the edit dialog's " +
    "hydration — which set `planLabel` and not `billedOnPlan` — the field was editable, the notice was gone, " +
    "and a link offered « remettre au tarif (60,000 DT) », the CATALOGUE figure rather than the devis' own " +
    "120,000. Typing 120 saved 200 and `AgreedCost` went 0.000 → 120.000; the fiche then bills whatever it " +
    "finds. Re-opening a visit to move its time is routine. `presetToSelectedAct` pairs the two fields " +
    "correctly on the add paths; hydration was the third, and the third will be written again.",
  () => {
    const offenders = [];

    /*
     * Derived from the pairing, per object literal rather than per file: a literal naming `treatmentPlanItemId`
     * must also name `billedOnPlan` somewhere in the same braces. `presetToSelectedAct` is the shared builder
     * and satisfies it by construction; a caller that spreads its result satisfies it too.
     */
    const PAIR = /treatmentPlanItemId\s*:/;
    const GUARD = /\bbilledOnPlan\b|\bpresetToSelectedAct\s*\(|\bplanFieldsFor\s*\(/;

    let candidates = 0;
    for (const f of tsx()) {
      const relPath = rel(f);
      // The booking surfaces alone: a plan item id appears legitimately in payload builders and in the plan
      // components, which never construct a `SelectedAct`.
      if (!/appointment-dialog|acts-picker/.test(relPath)) continue;

      const src = read(f);
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      const code = lines.map((l, i) => (masked[i] ? "" : l)).join("\n");

      // `planLabel` is the tell: it is set only when building a SelectedAct for a devis act.
      const builds = [...code.matchAll(/planLabel\s*:/g)];
      if (builds.length === 0) continue;
      candidates += builds.length;
      if (GUARD.test(code)) continue;

      offenders.push({
        file: relPath,
        line: lineAt(code, builds[0].index),
        text:
          "builds a devis act with `planLabel` and no `billedOnPlan` — `agreedCostOf` then returns the typed " +
          "price instead of 0, so the séance re-charges an act the devis already carries",
      });
    }

    if (candidates === 0) {
      offenders.push({
        file: "components/",
        text:
          "found no `planLabel:` construction — the scan is broken, and a guard that matches nothing cannot " +
          "hold anything",
      });
    }

    return offenders;
  },
);

check(
  "a-due-date-is-a-day-not-an-instant",
  "N20",
  "A day-only value reaches `defaultDay`, never `defaultDate`",
  "`CreateAppointmentDialog` has two ways to be told when to open, and the difference is invisible in the " +
    "value: `defaultDate` is an **instant** whose hour and minute become the booked time, `defaultDay` is a " +
    "**day** whose hour the form supplies itself. A step's due date is a day — `DueFrom` returns " +
    "`previous.Date.AddDays(n)`, so it is always midnight — and passed as `defaultDate` it opened « Planifier " +
    "l'étape » on **00:00**. Nothing reports it: the sheet renders, the date is right, and the time field just " +
    "reads 00:00 until « Créer le rendez-vous » answers « Heure dans le passé ». It is the same defect as the " +
    "hardcoded 09:00 fallback this feature removed, arriving by the one path that fallback no longer covers — " +
    "a caller supplying a date short-circuits it entirely.",
  () => {
    const offenders = [];

    // Every value that is a calendar day by construction. `nextStepDueFrom` is the DTO field; the rest are the
    // names a local binding for it plausibly takes.
    const DAY_VALUED = /nextStepDueFrom|\bdueFrom\b|\bdueDay\b|\bdueDate\b/;

    let candidates = 0;
    for (const f of tsx()) {
      const relPath = rel(f);
      const src = read(f);
      if (!/defaultDate\s*=|defaultDay\s*=/.test(src)) continue;

      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      const code = lines.map((l, i) => (masked[i] ? "" : l)).join("\n");

      const props = [...code.matchAll(/defaultDate\s*=\s*\{([^}]*)\}/g)];
      candidates += props.length + [...code.matchAll(/defaultDay\s*=/g)].length;

      // Prong 1: the day-valued name is inside the prop's own braces.
      for (const p of props) {
        if (!DAY_VALUED.test(p[1])) continue;
        offenders.push({
          file: relPath,
          line: lineAt(code, p.index),
          text:
            "passes a day-only value as `defaultDate` — the dialog reads an hour off it, so the form opens on " +
            "00:00; use `defaultDay`",
        });
      }

      /*
       * Prong 2: the same mistake laundered through a local — the worklist held the due date in a `booking`
       * object and spread it into the prop, so prong 1 saw only an identifier. A file that both knows a
       * day-valued name and renders `defaultDate` has to be read; one that renders `defaultDay` has been.
       */
      if (props.length > 0 && DAY_VALUED.test(code) && !/defaultDay\s*=/.test(code)) {
        offenders.push({
          file: relPath,
          line: lineAt(code, props[0].index),
          text:
            "knows a day-only value (`nextStepDueFrom`) and renders `defaultDate` without `defaultDay` — trace " +
            "which one reaches the prop",
        });
      }
    }

    if (candidates === 0) {
      offenders.push({
        file: "components/",
        text:
          "found no `defaultDate`/`defaultDay` prop — the scan is broken, and a guard that matches nothing " +
          "cannot hold anything",
      });
    }

    return offenders;
  },
);

check(
  "a-devis-price-is-not-a-gesture",
  "N21",
  "A price set by a devis is never rendered as a discount, nor offered back to the tarif",
  "Zero on an act a devis carries means « already invoiced on the plan », and the surfaces that price such an " +
    "act must not read it as anything else. The fiche de soins did: `act-card` derives its « geste » line from " +
    "tariff-vs-typed alone, so a 120 DT act arriving at 0 announced « Tarif catalogue 120,000 DT — geste de " +
    "120,000 DT » beside a « remettre au tarif » link. Two defects in one line — it states a discount the " +
    "dentist never granted, on the screen that creates money, and the link is a single press from re-charging " +
    "an act the devis already bills. It is the THIRD surface of one rule (the picker's `agreedCostOf` and the " +
    "edit dialog's `billedOnPlan` are the other two), and the reopen path is where it came back, exactly as " +
    "N19's hydration half did. Any surface that shows a catalogue tarif beside a devis act's own price has to " +
    "consult `billedOnPlan` before drawing a conclusion from the difference.",
  () => {
    const offenders = [];

    // Derived from the tell: a file that computes a tariff difference must also know about `billedOnPlan`.
    const DIFFERENCE = /\btariff\s*-\s*\w+|remettre au tarif|geste de/g;
    const GUARD = /\bbilledOnPlan\b|\bagreedCostOf\s*\(/;

    let candidates = 0;
    for (const f of tsx()) {
      const relPath = rel(f);
      const src = read(f);
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      const code = lines.map((l, i) => (masked[i] ? "" : l)).join("\n");

      const hits = [...code.matchAll(DIFFERENCE)];
      if (hits.length === 0) continue;
      candidates += hits.length;
      if (GUARD.test(code)) continue;

      offenders.push({
        file: relPath,
        line: lineAt(code, hits[0].index),
        text:
          "prices an act against the catalogue tarif without consulting `billedOnPlan` — a devis act's 0 will " +
          "be reported as a « geste », and offered back to the tarif",
      });
    }

    if (candidates === 0) {
      offenders.push({
        file: "components/",
        text:
          "found no tariff-difference rendering — the scan is broken, and a guard that matches nothing cannot " +
          "hold anything",
      });
    }

    return offenders;
  },
);

check(
  "seance-rows-group-into-acts",
  "N22",
  "A surface turning `appointment.procedures` into acts groups them by `treatmentPlanItemId`",
  "A séance carrying two STEPS of one devis act arrives as TWO rows — the server keys its duplicate rule on " +
    "the (act, step) pair, which is what makes « préparation + empreinte dans la même séance » expressible. " +
    "Any surface that renders those rows as acts must fold them back, or one act is presented as two. The " +
    "picker learned this (`groupActs`, after one bridge rendered as two identical cards each claiming both " +
    "steps); the fiche de soins did not, and read « Actes de la séance 2 actes » for a single act, offered the " +
    "same act back twice in the « remettre » row with one React key between them, and would have doubled the " +
    "fee on any act the devis was not already paying for. Group on the PLAN ITEM, never on the procedure: two " +
    "devis lines quoting the same act (two teeth, priced apart) are genuinely two acts.",
  () => {
    const offenders = [];

    /*
     * Scoped to the surfaces that turn those rows into PRICED ACT CARDS — the ones a `BookedActPrefill` feeds.
     * Two neighbours legitimately do not group and were flagged by a looser version of this check:
     * `edit-appointment-dialog` hydrates the wire list and hands it to the picker, which groups it downstream
     * with `groupActs`; and `day-summary`'s `actsOf` aggregates the rows by `procedureTypeId` for the act mix,
     * where two rows of one act SHOULD contribute their chair time twice. Flagging either is noise, and a check
     * that cries wolf stops being read.
     */
    const READS_ROWS = /BookedActPrefill|applyAppointment/g;
    // The operative call, not the declaration: matching the bare name `seenPlanItem` passed even with the
    // `.has(...) continue` removed, i.e. the guard was satisfied by a Set nothing consulted.
    const GROUPS = /\bgroupActs\s*\(|seenPlanItem\.has\s*\(|byPlanItem\.get\s*\(/;

    let candidates = 0;
    for (const f of tsx()) {
      const relPath = rel(f);
      const src = read(f);
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      const code = lines.map((l, i) => (masked[i] ? "" : l)).join("\n");

      // The reducer that CONSUMES the prefill is not a producer of it, so it has nothing to group.
      if (/use-session-acts/.test(relPath)) continue;
      const hits = [...code.matchAll(READS_ROWS)];
      if (hits.length === 0) continue;
      candidates += hits.length;
      if (GROUPS.test(code)) continue;

      offenders.push({
        file: relPath,
        line: lineAt(code, hits[0].index),
        text:
          "builds act cards from a séance's rows without folding two steps of one act back together — one act " +
          "will render as two, be offered back twice, and on a non-devis act be charged twice",
      });
    }

    if (candidates === 0) {
      offenders.push({
        file: "components/",
        text:
          "found no surface building a `BookedActPrefill` — the scan is broken, and a guard that matches " +
          "nothing cannot hold anything",
      });
    }

    return offenders;
  },
);

check(
  "a-live-treatment-has-one-test",
  "N23",
  "« Is this treatment still running? » is asked through `isPlanLive`, never written out",
  "The test was written by hand as `status === \"Accepted\" || status === \"InProgress\"` in four places. When " +
    "« Suivre ce traitement » made an un-numbered `Draft` a live treatment — that is the whole point, so that " +
    "following an implant costs no numbered devis — three of the four were updated and the fourth was not: " +
    "`PlanActPrimaryAction` rendered « À planifier » on the act beside **no button at all**, so the treatment " +
    "the dentist had just created could not be booked. No error, no console line; the état was even correct. " +
    "It is the repository's signature defect (a rule wired to some of its call sites) on a status set that is " +
    "no longer closed, so the guard is the set of *writers*, not a list of files.",
  () => {
    const offenders = [];

    // The hand-written shape, in either order and with either operand spelling.
    const HAND_WRITTEN = /===\s*"(Accepted|InProgress)"\s*\|\|[^\n]*===\s*"(Accepted|InProgress)"/g;
    /*
     * ⚠️ **The NEGATIVE form, added 2026-09-09, and it is the one that actually shipped a defect.**
     *
     * `isPlanLive` itself was written as `status !== "Cancelled" && status !== "Completed"` — phrased that way
     * deliberately, because enumerating the OPEN statuses is what left `Draft` out when it became one. That
     * reasoning was sound and the phrasing still failed: appending `Stopped` (so « Arrêter le traitement » stops
     * writing `Completed`, and a stopped treatment can be told from a finished one) made a stopped plan read as
     * **live** — bookable, listed under « Traitements suivis », ringed on the odontogramme, its acts offered in
     * the booking dialogs — with no error anywhere. Both phrasings fail for the same reason: the set is not
     * closed. The answer is one deliberate list inside `isPlanLive`, with the server's twin held by
     * `TreatmentPlanStatusCoverageTests`.
     *
     * ⚠️ **It is anchored on a PLAN subject, and the first version was not.** `AppointmentStatus` has its own
     * `Cancelled` / `Completed` members, so an unanchored pattern flagged two entirely correct lines —
     * `patients/[id]/page.tsx`'s « can this visit still be recorded? » and the agenda's « is this block
     * draggable? » — neither of which has anything to do with a devis. A guard that fires on correct code is
     * one somebody deletes.
     */
    const HAND_WRITTEN_NEGATIVE =
      /\b(?:\w*[Pp]lan|p)\.status\s*!==\s*"(?:Cancelled|Completed|Stopped)"\s*&&[^\n]*!==\s*"(?:Cancelled|Completed|Stopped)"/g;
    let candidates = 0;

    for (const f of tsx()) {
      const relPath = rel(f);
      // The helper itself is where the answer lives.
      if (/plan-next-action\.ts$/.test(relPath)) continue;

      const src = read(f);
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      const code = lines.map((l, i) => (masked[i] ? "" : l)).join("\n");

      const hits = [...code.matchAll(HAND_WRITTEN)];
      const negatives = [...code.matchAll(HAND_WRITTEN_NEGATIVE)];
      if (hits.length === 0 && negatives.length === 0) continue;
      candidates += hits.length + negatives.length;
      if (hits.length > 0) {
        offenders.push({
          file: relPath,
          line: lineAt(code, hits[0].index),
          text:
            "writes the live-treatment test out by hand — call `isPlanLive(plan.status)` instead, or an " +
            "un-numbered treatment silently loses its actions here",
        });
      }
      if (negatives.length > 0) {
        offenders.push({
          file: relPath,
          line: lineAt(code, negatives[0].index),
          text:
            "excludes the closed statuses by hand — call `isPlanLive(plan.status)` instead. The status set is " +
            "not closed: `Stopped` was appended and this shape read it as LIVE, with no error anywhere",
        });
      }
    }

    /*
     * ⚠️ No `candidates === 0` tripwire here, unlike its neighbours: zero is the CORRECT steady state for this
     * one — every writer has been routed through the helper. The scan is instead proved by the helper's own
     * presence, so a rename that orphaned it would fail the build at `tsc` rather than pass silently here.
     */
    const helper = read(
      [...tsx()].find((f) => /plan-next-action\.ts$/.test(rel(f))) ?? "",
    );
    if (!/export function isPlanLive/.test(helper)) {
      offenders.push({
        file: "components/treatment-plans/plan-next-action.ts",
        text: "`isPlanLive` is gone — the guard has nothing to point callers at",
      });
    }

    return offenders;
  },
);

check(
  "public-routes-have-one-owner",
  "N24",
  "One list decides which routes are reached without a session, and every guard asks it",
  "A door a visitor reaches with no session — `/signup`, `/setup`, `/join`, both password-reset pages — answers " +
    "401 to anything authenticated, correctly: nobody is signed in yet. The app must not read that as an " +
    "expiry. It did. `middleware.ts` kept the list privately, so the three redirect guards in " +
    "`lib/auth/session.tsx` carried their own hand-written test against `/login` and agreed with it about " +
    "exactly one of the seven doors. On the other six, one authenticated call was enough to show " +
    "« Session expirée » and eject the visitor to the login screen — which is how public signup broke in " +
    "production: `SetupWizard` read the profile-image upload policy on mount and every attempt to create a " +
    "cabinet ended on `/login?returnTo=%2Fsignup`. Nothing else can catch it — both files are correct on " +
    "their own, the endpoint is right to refuse, the redirect is right for a guarded route, and it " +
    "type-checks. The list lives in `lib/auth/public-routes.ts`; compare a path against it with " +
    "`isPublicRoute()` rather than against a literal of your own.",
  () => {
    const OWNER = "lib/auth/public-routes.ts";
    const ownerFile = tsx().find((f) => rel(f) === OWNER);
    if (!ownerFile) {
      return [{ file: OWNER, line: 0, text: "the public-route list has no owner — this check cannot run" }];
    }

    // DERIVED from the owner rather than restated: the routes are read out of the list itself, so a door added
    // there is covered by this check on the day it is added.
    const routes = [...read(ownerFile).matchAll(/^\s*'(\/[^']*)',?\s*$/gm)].map((m) => m[1]);
    if (routes.length === 0) {
      return [{ file: OWNER, line: 0, text: "could not read the route list — this check cannot run" }];
    }
    // Escaped even though today's routes are plain path segments: a door named with a `.` one day must not
    // quietly widen this into a wildcard.
    const alternation = routes.map((r) => r.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("|");

    // `middleware.ts` sits outside SCAN_DIRS and is where the list used to live, so it is read by name.
    const candidates = [...tsx(), join(WEB_ROOT, "middleware.ts")].filter((f) => rel(f) !== OWNER);

    return scanLines(candidates, new RegExp(`["'](?:${alternation})["']`), (line) =>
      // A `<Link href="/login">` or a `router.push("/login")` is navigation, not a claim about who may be
      // there. Only a test *of the current path*, or a second list, is another answer to this question.
      /pathname|PUBLIC_ROUTE/i.test(line),
    );
  },
);

check(
  "password-has-a-reveal",
  "P2",
  'Every password field is `<PasswordInput>` — no raw `type="password"` outside the primitive',
  "A password box you cannot read is where « mot de passe incorrect » comes from on a phone keyboard, and this " +
    "app asks for one at the front door, at the forced first change, at every step-up, in the join and setup " +
    "wizards and on three secret fields in `reminder-settings`. Adding the eye to the login screen and stopping " +
    "there is this repository's dominant defect shape, so the reveal lives in `ui/password-input.tsx` and this " +
    "check is what keeps the ninth field from being written without it. " +
    "⚠️ The primitive is the only exemption, and it is not a screen-shaped one: a field that must stay legible " +
    "— the recovery code copied off paper — is `type=\"text\"` and matches nothing here.",
  () =>
    scanLines(
      tsx().filter((f) => !/components[\\/]ui[\\/]password-input\.tsx$/.test(rel(f))),
      /type=\{?["']password["']\}?/,
    ),
);

check(
  "shared-class-cannot-outrank-a-position-utility",
  "P2",
  "A `position` a shared primitive applies to every instance is declared at zero specificity",
  "`buttonVariants` puts `touch-target` at the head of EVERY `<Button>`, and globals.css gives that class a " +
    "`position`. So a button that also carries `absolute`/`fixed`/`sticky` declares `position` twice, in the " +
    "same `@layer utilities`, at the same specificity — and CSS settles it by SOURCE ORDER in the stylesheet, " +
    "which is neither the order of the class attribute nor anything `cn()` can see (tailwind-merge does not " +
    "know a hand-written class conflicts with a utility). That order is not stable across builds: measured on " +
    "a shipped bundle, `.absolute` sat at byte 14288 and `.touch-target` at 150882, so `relative` won and the " +
    "agenda's floating « + » was never positioned — it fell to its static place at the START of the flex " +
    "column and KEPT ITS SPACE IN THE FLOW, which also held the grid off the bottom of the screen. `next dev` " +
    "emits the two in the opposite order, so it renders correctly on a laptop and is wrong on the device; and " +
    "the whole rule is behind `@media (pointer: coarse)`, which a desktop browser never matches — so no " +
    "amount of resizing a desktop window can see it. Wrap the selector in `:where()`: specificity 0 still " +
    "gives `::after` its containing block when nothing else sets `position`, and loses to every explicit " +
    "utility. Two device reports, invisible to tsc, to this gate, and to every browser check that preceded it.",
  () => {
    const globals = ALL_FILES.find((f) => rel(f) === "app/globals.css");
    if (!globals) {
      return [{ file: "app/globals.css", line: 0, text: "missing", full: "the stylesheet this check guards is gone — retarget or retire the check" }];
    }
    const button = ALL_FILES.find((f) => rel(f) === "components/ui/button.tsx");

    /*
     * ⚠️ Comments are blanked, not stripped, and both halves of that mattered — this check shipped green
     * against the very defect it was written for until they were fixed. A CSS rule's "selector" here is
     * everything since the last `}`, so it swallows the preceding comment block — and that block *names*
     * `:where()`, which is exactly what the check looks for to clear a rule. And `cva(` is followed by a `//`
     * line comment before its base string, so a `\s*` between them matched nothing and the stamped set came
     * back empty, short-circuiting to a pass. Blanking preserves every offset, so `lineAt` still reports the
     * real line.
     */
    const blank = (s, re) => s.replace(re, (m) => m.replace(/[^\n]/g, " "));
    const src = blank(read(globals), /\/\*[\s\S]*?\*\//g);

    // Derived both ways: which classes a shared primitive stamps on every instance, and which of those
    // globals.css gives a `position`. A class the primitive stops applying drops out of the check by itself.
    const stamped = new Set();
    if (button) {
      // The base string is `cva(<comment?> "<base>", …)` — the first string literal is what lands on every Button.
      const base = blank(read(button), /\/\/[^\n]*/g).match(/cva\(\s*(["'`])([\s\S]*?)\1/);
      for (const cls of (base?.[2] ?? "").split(/\s+/)) {
        if (/^[a-z][a-z0-9-]*$/.test(cls)) stamped.add(cls);
      }
    }
    if (stamped.size === 0) {
      return [{ file: "components/ui/button.tsx", line: 0, text: "no base class string found", full: "this check derives the stamped classes from `cva`'s first string literal — if that shape changed, retarget it rather than letting it pass vacuously" }];
    }

    const hits = [];
    for (const rule of src.matchAll(/([^{}\n][^{}]*?)\{([^{}]*)\}/g)) {
      const [, selector, body] = rule;
      if (!/(^|[\s;])position\s*:/.test(body)) continue;
      if (selector.includes("::")) continue; // a pseudo-element competes with nothing
      for (const cls of stamped) {
        // The bare class, not already neutralised by `:where()`.
        const at = selector.search(new RegExp(`\\.${cls}(?![\\w-])`));
        if (at < 0) continue;
        if (/:where\(/.test(selector)) continue;
        hits.push({
          file: rel(globals),
          // The class's own line, not the start of the match — a "selector" here runs back to the previous `}`.
          line: lineAt(src, rule.index + at),
          text: `\`.${cls}\` sets \`position\` at class specificity`,
          full: `every <Button> carries \`${cls}\`, so this outranks or is outranked by \`absolute\`/\`fixed\`/\`sticky\` purely by stylesheet order — wrap the selector in \`:where()\``,
        });
      }
    }
    return hits;
  }
);

check(
  "phone-rule-matches-the-corpus",
  "N25",
  "The browser's phone rule answers `shared/phone-e164-corpus.json` exactly, and agrees on the default country",
  "The phone rule exists TWICE — `api/…/ValueObjects/PhoneNumber.cs` for the server and `web/lib/phone.ts` " +
    "for the browser's pre-check — and until the corpus existed the only thing holding them together was a " +
    "comment in each saying the other was a mirror. That is the state `OdontogramConditionMirrorTests` calls " +
    "« a mirror nobody checks is a mirror that is already wrong ». Both sides are pure total functions of a " +
    "string, which no other mirrored rule in this repo is, so the two can be driven from the same inputs and " +
    "pinned to the same outputs — stronger than comparing two declarations, and available for this rule alone. " +
    "`PhoneRuleCorpusTests` holds the C# half against the same file. ⚠️ This drives libphonenumber-js " +
    "DIRECTLY, so it proves the corpus and the shipped library agree; that `lib/phone.ts` actually delegates to " +
    "that library rather than hand-rolling a rule again is the sibling check's job.",
  () => {
    const corpusPath = join(WEB_ROOT, "..", "shared", "phone-e164-corpus.json");
    let corpus;
    try {
      corpus = JSON.parse(readFileSync(corpusPath, "utf8"));
    } catch (error) {
      // A guard that quietly finds nothing to check is indistinguishable from one that passes.
      return [{ file: rel(corpusPath), text: `could not read the corpus: ${error.message}` }];
    }

    const cases = Array.isArray(corpus.cases) ? corpus.cases : [];
    const hits = [];

    // Two non-vacuity tripwires: an empty corpus, and one with no refusals (which would pass while proving
    // only that valid numbers are valid).
    if (cases.length < 25) {
      hits.push({ file: "shared/phone-e164-corpus.json", text: `only ${cases.length} case(s) parsed — the corpus shape changed` });
      return hits;
    }
    if (!cases.some((c) => c.expected === null)) {
      hits.push({ file: "shared/phone-e164-corpus.json", text: "no refusal cases — a corpus of accepts alone proves nothing" });
    }

    // The fallback country has one home on each side (AC-4); the corpus states the value both are held to.
    const src = read(join(WEB_ROOT, "lib", "phone.ts"));
    const declared = src.match(/DEFAULT_REGION\s*:\s*CountryCode\s*=\s*"([A-Z]{2})"/)?.[1];
    if (!declared) {
      hits.push({ file: "lib/phone.ts", text: "DEFAULT_REGION is gone or reshaped — update this parser, do NOT inline the value here" });
    } else if (declared !== corpus.defaultRegion) {
      hits.push({ file: "lib/phone.ts", text: `DEFAULT_REGION is ${declared}, the corpus says ${corpus.defaultRegion}` });
    }

    for (const c of cases) {
      const region = c.region ?? corpus.defaultRegion;
      let actual = null;
      try {
        const parsed = parsePhoneNumberFromString(c.raw, region);
        if (parsed && parsed.isValid()) actual = parsed.number;
      } catch {
        actual = null;
      }
      const expected = c.expected ?? null;
      if (actual !== expected) {
        hits.push({
          file: "shared/phone-e164-corpus.json",
          text: `${JSON.stringify(c.raw)} (${region}) → ${actual ?? "null"}, corpus says ${expected ?? "null"}`,
          full: c.why ?? "",
        });
      }
    }

    return hits;
  }
);

check(
  "phone-rule-has-one-owner",
  "N25",
  "Exactly one file decides what a valid phone number is, and it delegates to the metadata library",
  "This repo's dominant defect shape is a correct rule wired to one call site, and the phone rule lived it: " +
    "the sentence « Utilisez un numéro tunisien à 8 chiffres (ou +216…) » was pasted into three server files " +
    "and written inline in a fourth in the browser, so widening the rule to accept any country made all four " +
    "false at once. Two shapes are banned here. A second file normalising a number itself — stripping " +
    "non-digits and prefixing a country code — is a second answer to « is this reachable? », and the browser " +
    "already had one that disagreed with nothing only because both were Tunisian. And any user-facing string " +
    "naming a digit count or a single country is now simply untrue: the field takes every country, and the " +
    "country is a control beside it. Derived, not listed — whichever file owns the rule, there must be exactly " +
    "one, and the message names the others so a deliberate move needs no edit here.",
  () => {
    const hits = [];
    const owner = join(WEB_ROOT, "lib", "phone.ts");

    // ── the rule itself: `+<cc>` composed from stripped digits, the hand-rolled shape this replaced ──────────
    const normalisers = [];
    for (const file of tsx()) {
      const lines = read(file).split(/\r?\n/);
      // ⚠️ `commentMask` returns a per-line BOOLEAN ARRAY, not the masked text — joining it and matching
      // against the result tests the string "false,false,true…", which silently matches nothing. Caught by
      // this check's own red proof, which is the whole reason § 14 demands one.
      const inComment = commentMask(lines);
      const code = lines.filter((_, i) => !inComment[i]).join("\n");
      // A `\D` strip in the same file as a `+`-prefixed country code — the old `toE164Tunisian` verbatim.
      if (/replace\(\s*\/\\D\/g/.test(code) && /["'`]\+(?:\$\{|\d)/.test(code)) {
        normalisers.push(file);
      }
    }
    /*
     * ⚠️ No `length === 0` tripwire here, unlike this file's other one-owner checks: **zero is the correct
     * steady state**. The owner delegates to libphonenumber-js and strips nothing itself, so a hit means
     * somebody has hand-rolled the rule back — the shape that shipped for the product's whole life and could
     * not refuse `201234567`. `a-live-treatment-has-one-test` carries the same note for the same reason.
     * The vacuity risk moves to the delegation assertion below, which fails on a rename.
     */
    for (const file of normalisers) {
      hits.push({
        file: rel(file),
        text: "normalises a phone number by hand",
        full:
          file === owner
            ? "even the owner must not: parse through libphonenumber-js, or per-country validity is lost"
            : "the rule has one owner, `lib/phone.ts`, and it delegates to libphonenumber-js",
      });
    }

    // ── the owner really does delegate, so it cannot be hand-rolled back ────────────────────────────────────
    const ownerSrc = read(owner);
    if (!/from\s+["']libphonenumber-js\/max["']/.test(ownerSrc) || !/isValid\(\)/.test(ownerSrc)) {
      hits.push({
        file: "lib/phone.ts",
        text: "the owner no longer parses through libphonenumber-js and checks isValid()",
        full: "a length test cannot refuse `201234567` — a Tunisian number with a ninth digit is a valid Egyptian one",
      });
    }

    /*
     * ── the sentence: no user-facing string names one country or the old 8-digit shape ──────────────────────
     * ⚠️ Anchored on « tunisien » and on **8** specifically, not on any digit count: `à\s+\d+\s+chiffres`
     * matched « code à 6 chiffres » on the login screen, which is the TOTP field and entirely correct. A check
     * that noisy earns an exemption list, and an exemption list that grows is a check that has stopped working
     * — so the pattern narrows instead.
     */
    hits.push(
      ...scanLines(tsx(), /num[ée]ro\s+tunisien|à\s+8\s+chiffres/i).map((h) => ({
        ...h,
        full: "the field accepts every country now, and the country is a control beside it — say « Choisissez le pays » instead",
      }))
    );

    return hits;
  }
);

check(
  "debt-rule-has-no-typescript-copy",
  "N29",
  "What a patient owes is SERVED — no browser file re-derives which plans carry debt",
  "`PlanBillingRules` is the single authority on which treatment plans carry patient debt, and « Solde " +
    "patient », « Créances », la caisse, the dashboard and the vendor console are held equal by " +
    "`MoneyReadConsistencyTests` because all five route through it. A browser cannot: the de-duplication " +
    "needs every invoice's plan link and status, so a client that sums the two lists a page happens to have " +
    "loaded counts a bridged devis on BOTH tracks. That is not hypothetical — `displayedOutstanding`'s own " +
    "docstring records it measured on a live database: 4 of 4 bridged plans reported the whole devis as " +
    "unpaid, two of them fully settled, one patient shown « Solde dû 31,000 DT » in their file header and " +
    "« Reste 120,000 DT » in the plan strip on the same page. `/factures` states the rule for its own figure " +
    "in as many words — « Served, never derived here: only the server can apply PlanBillingRules " +
    "BilledPlanIds ». So a hand-written disjunction over the debt-bearing statuses, or a local " +
    "`carriesDebt`/`billedPlanIds`, is a second authority whose disagreements are silent in both directions: " +
    "a debt shown that nobody owes, or one owed that is readable nowhere. Read the served figure — " +
    "`totalOutstanding`, `lines[].outstanding`, `displayedOutstanding(plan)`, `InstallmentDto.isOverdue`.",
  () => {
    const offenders = [];

    // The one file allowed to name these statuses at all: it holds `isPlanBilled`/`displayedOutstanding`,
    // which read the SERVER's own per-plan figures rather than re-deriving the rule.
    const OWNER = /components[\\/]treatment-plans[\\/]plan-next-action\.ts$/;
    // Derived from the rule itself, not from a file list: any file writing its own name for the concept.
    const NAMES = /\b(carriesDebt|billedPlanIds|debtBearing|debtBearingStatuses|isDebtBearing)\b/;
    // A hand-written disjunction over `PlanBillingRules.DebtBearingPlanStatuses`. Two of the three members in
    // one expression is the shape — the third is optional, since dropping `Completed` is itself the defect
    // that hid a finished treatment's créance.
    const DISJUNCTION =
      /["']?Accepted["']?[^;\n]{0,80}\|\|[^;\n]{0,80}["']?(InProgress|Completed)["']?/;

    let candidates = 0;

    for (const f of tsx()) {
      if (OWNER.test(f)) continue;
      const lines = read(f).split(/\r?\n/);
      const masked = commentMask(lines);
      candidates++;

      lines.forEach((line, i) => {
        if (masked[i]) return;
        // A *type* annotation naming a status is fine — the union is the DTO's own shape.
        if (/^\s*(\/\/|\*)/.test(line)) return;

        if (NAMES.test(line)) {
          offenders.push({
            file: rel(f),
            line: i + 1,
            text: "names the debt rule in the browser — read the served figure instead",
            full: line.trim(),
          });
          return;
        }
        if (DISJUNCTION.test(line) && /status/i.test(line)) {
          offenders.push({
            file: rel(f),
            line: i + 1,
            text: "hand-written debt-bearing status test — ask the server, or `isPlanLive` for the clinical question",
            full: line.trim(),
          });
        }
      });
    }

    // Tripwire: the scan is measuring nothing rather than finding nothing — a renamed owner, or a glob that
    // stopped matching. A guard that matches no candidates cannot hold anything.
    if (candidates === 0) {
      offenders.push({
        file: "components/",
        text: "found no files to scan — the guard is broken, not satisfied",
      });
    }
    if (!ALL_FILES.some((f) => OWNER.test(f))) {
      offenders.push({
        file: "components/treatment-plans/plan-next-action.ts",
        text: "the owner file is gone — this check's exemption now names nothing, so re-point it deliberately",
      });
    }

    return offenders;
  },
);

check(
  "step-counter-says-done-or-to-do",
  "N31",
  "A printed step counter carries the word that says whether it counts what is DONE or what is still to do",
  "A multi-séance act has TWO counters of the identical shape — « how many séances are behind us » and " +
    "« which séance comes next » — and a bare « 2 / 3 » does not say which one you are reading. Unlabelled it " +
    "is read as progress, every time, and it has now been measured three separate times in this product. " +
    "« Traitements en cours » shipped a bare « étape 2 / 2 » and three reviewers read it cold on a treatment " +
    "with one of two séances done: all three took it for finished. `patient-plans-strip` had the same bare " +
    "« 2 / 6 » and the same three readers made the same mistake. And the odontogramme's tooth tooltip printed " +
    "« séance 2 sur 3 · essai de l'armature à planifier » on a treatment whose only delivered séance was the " +
    "préparation — the worst of the three, because a chart of the mouth is where a dentist looks for what is " +
    "IN the mouth, so the reader takes every word of it as a clinical fact. The rank was `stepsDone + 1`, i.e. " +
    "the step still to come, and it read as the step just carried out. The first two were fixed by adding the " +
    "visible word « à faire »; the fix never reached the third, which is this repo's dominant defect shape. " +
    "So: any counter built from the step tokens must carry one of « faite(s) » · « réalisée(s) » · « à faire » " +
    "· « à planifier » · « incluse(s) » · « restante(s) » · « cette séance » within sight of the figure — " +
    "visible, not `sr-only`, which was already right in both of the first two cases while the sighted reader " +
    "had nothing. « prochaine » and « en cours » are NOT accepted: both sit beside either counter, so neither " +
    "tells them apart — and with « prochaine » allowed this check passed a stripped-down `plan-step-strip`, " +
    "satisfied by the `sr-only` « Prochaine étape : … » two lines under the bare figure.",
  () => {
    const offenders = [];

    /*
     * A ratio PRINTED for a reader: « {a} / {b} » in JSX, « ${a} / ${b} » in a template literal, or the two
     * joined by « sur » with prose between them (« ${done} étapes sur ${total} »). `[^{}]` keeps the prose
     * short and brace-free, so this cannot span from one interpolation to an unrelated one further down.
     */
    const RATIO = /\}\s*\/\s*\$?\{|\}[^{}]{0,40}?\bsur\b[^{}]{0,20}?\$?\{/g;
    // Derived from the QUANTITY, not from a file list: the counter is a step counter whatever it is called.
    const STEP_TOKEN = /\bstepsDone\b|\bstepsTotal\b|\bnextStepNumber\b|\bdoneSteps\b|\bsteps\.length\b|\bsequenceNumber\s*\+\s*1\b/;
    /*
     * ⚠️ « prochaine » and « en cours » are deliberately NOT here. Both were, and the guard then passed a
     * stripped-down `plan-step-strip` whose figure said nothing: the `sr-only` « Prochaine étape : … » two
     * lines below satisfied it. A word that can sit beside EITHER counter cannot tell them apart.
     */
    const READING =
      /faites?\b|réalisées?\b|à\s+faire|à\s+planifier|incluses?\b|restantes?\b|cette\s+séance/i;
    /*
     * ⚠️ **An `sr-only` word does not count, and that is the point of this check rather than a detail of it.**
     * In both surfaces where this defect was measured the screen-reader label was already correct — « Étapes
     * réalisées : » on the strip, « étape N sur M à faire » announced on the worklist — while the sighted
     * reader had a bare fraction. So the offscreen text is blanked before the scan (newlines kept, so the
     * reported line number is still the real one) and only what is painted can satisfy the rule.
     */
    const SR_ONLY = /<span[^>]*\bsr-only\b[^>]*>[\s\S]*?<\/span>/g;
    const blank = (m) => m.replace(/[^\n]/g, " ");

    let candidates = 0;

    for (const f of tsx()) {
      const lines = read(f).split(/\r?\n/);
      const masked = commentMask(lines);
      const code = lines.map((l, i) => (masked[i] ? "" : l)).join("\n").replace(SR_ONLY, blank);

      RATIO.lastIndex = 0;
      let m;
      while ((m = RATIO.exec(code)) !== null) {
        const window = code.slice(Math.max(0, m.index - 200), m.index + m[0].length + 380);
        if (!STEP_TOKEN.test(window)) continue;
        candidates++;
        if (READING.test(window)) continue;
        offenders.push({
          file: rel(f),
          line: lineAt(code, m.index),
          text:
            "prints a step counter with no word saying whether it counts séances DONE or the one still to " +
            "come — add « faites » / « à faire » beside the figure",
          full: code.slice(m.index, m.index + m[0].length).replace(/\s+/g, " "),
        });
      }
    }

    // Tripwire: the token names moved, so the scan is measuring nothing rather than finding nothing.
    if (candidates === 0) {
      offenders.push({
        file: "components/treatment-plans/",
        text:
          "found no printed step counter at all — the scan is broken, and a guard that matches nothing " +
          "cannot hold anything",
      });
    }

    return offenders;
  },
);

// ── run ─────────────────────────────────────────────────────────────────────────────────────────────────────

check(
  "prescription-line-has-one-owner",
  "N32",
  "The printed ordonnance line is composed in one place — no surface re-derives it",
  "A prescription line is THREE parts assembled from the optional fields the norms care about — the " +
    "médicament and its dosage, then the posologie (la dose, la fréquence, la durée in jours or mois), then " +
    "la voie and la quantité — printed on three lines under a « 1/ » number, with only the first two " +
    "underlined. It is rendered THREE times by contract: `PrescriptionContent` for the PDF, " +
    "`prescriptionLineParts` for the on-screen A4 preview, and the same function for the Word export. The " +
    "file that owns the browser half says in as many words that this formatting had already existed in three " +
    "copies once, before the voie and the quantité were added to two of them. Now that the fiche de soins " +
    "writes the same ordonnance, a fourth copy is exactly what a séance history row or a section summary " +
    "invites: both want a short label, and the tempting way to get one is to paste the parts and trim them. " +
    "The consequence is not cosmetic — the two renderings drift, and the ordonnance a pharmacist reads stops " +
    "matching the one the dentist proof-read on screen. `<n> / jour` is the fragment only that assembly " +
    "produces, so it is the marker: a real string literal outside `lib/documents.ts` is a second composer. " +
    "Prose is fine — comments are masked — because only code can disagree.",
  () => {
    const OWNER = "lib/documents.ts";
    // Derived, not listed: whichever file composes the line, there must be exactly one — and it must be the
    // registry, because that is the module both writing surfaces already import.
    const hits = scanLines(tsx(), /\/ jour/);
    const offenders = hits.filter((hit) => hit.file !== OWNER);

    if (hits.length === 0) {
      return [{ file: OWNER, text: "nothing composes a prescription line any more — the formatter moved, or this check needs retargeting" }];
    }

    return offenders.map((hit) => ({
      ...hit,
      text: "a second composer of the printed ordonnance line",
      full: `the printed line has one owner (${OWNER}); for a row-sized label use \`shortPrescriptionLabel\`, and for the sentence import \`formatPrescriptionLine\` — ${hit.full}`,
    }));
  }
);

check(
  "specialty-labels-have-one-owner",
  "N32",
  "The French label for a practitioner's specialty is the same map on both sides of the wire",
  "`Doctor.Specialty` stores an ENGLISH key, deliberately and permanently: those are the values already on " +
    "every row and snapshotted onto every `MedicalDocument` ever issued, so the display label is a map rather " +
    "than a migration. That map used to exist only in the browser, which was sound while every document was " +
    "composed by the browser too — the editor sent an already-translated `doctorSpecialty`. The fiche de soins " +
    "now issues its own ordonnance SERVER-side, so `DoctorSpecialtyLabels` is the second half of the pair, and " +
    "the halves must hold the same keys and the same labels. Drift is silent and lands on paper: a specialty " +
    "added here and not there prints « Orthodontist » under the prescriber's name on a French legal document, " +
    "while reading correctly on every screen. Compared in BOTH directions, because a key removed from one side " +
    "is the same defect as a key added to the other.",
  () => {
    const TS = "lib/specialties.ts";
    const CS = "../api/ClinicManagement.Application/Features/Documents/DoctorSpecialtyLabels.cs";

    const tsFile = ALL_FILES.find((f) => rel(f) === TS);
    if (!tsFile) {
      return [{ file: TS, text: "the browser's specialty map is gone — the pair needs retargeting" }];
    }

    let csSrc;
    try {
      csSrc = readFileSync(join(WEB_ROOT, CS), "utf8");
    } catch {
      return [{ file: TS, text: `the server's half is unreadable at ${CS} — a moved file breaks the pairing silently, which is what this check exists to prevent` }];
    }

    // Both maps are plain literal tables on purpose: one line per entry, so a regex is the whole parser and
    // there is nothing to keep in step but the entries themselves.
    const tsBody = read(tsFile).split("SPECIALTY_LABELS_FR")[1] ?? "";
    const tsPairs = new Map(
      [...tsBody.matchAll(/"?([A-Za-z ]+)"?\s*:\s*"([^"]+)"/g)].map((m) => [m[1].trim(), m[2]])
    );
    const csPairs = new Map(
      [...csSrc.matchAll(/\["([^"]+)"\]\s*=\s*"([^"]+)"/g)].map((m) => [m[1], m[2]])
    );

    if (tsPairs.size === 0 || csPairs.size === 0) {
      return [{ file: TS, text: `one half parsed empty (browser ${tsPairs.size}, server ${csPairs.size}) — the shape changed and this check can no longer see drift` }];
    }

    const problems = [];
    for (const [key, label] of tsPairs) {
      if (!csPairs.has(key)) {
        problems.push({ file: TS, text: `« ${key} » has no server label`, full: `add ["${key}"] = "${label}" to DoctorSpecialtyLabels, or an ordonnance issued by the fiche prints the English key` });
      } else if (csPairs.get(key) !== label) {
        problems.push({ file: TS, text: `« ${key} » reads differently on the two sides`, full: `browser "${label}" vs server "${csPairs.get(key)}"` });
      }
    }
    for (const [key] of csPairs) {
      if (!tsPairs.has(key)) {
        problems.push({ file: TS, text: `« ${key} » exists only on the server`, full: "the browser would render the raw English key for it" });
      }
    }

    return problems;
  }
);


check(
  "document-type-set-has-one-owner",
  "N33",
  "The set of document types is one set, and every surface that names a type knows all of them",
  "A `MedicalDocument.DocumentType` is a bare string the server never validates against a closed list, and " +
    "the set is mirrored in FIVE places: `DocumentTypes` on the server, `DOCUMENT_TEMPLATES` in the browser, " +
    "`DocumentFileNaming`, the PDF renderer's title map and the editor's heading map. Nothing fails when one " +
    "of them is behind — the type simply renders as its raw key, and that shipped: a saved arret de travail " +
    "was labelled `arret-travail` in the patient's own Documents tab for as long as the third copy of the set " +
    "existed, because `documentTypeLabel` falls back to the key. Compared in BOTH directions against the two " +
    "mirrors that have a total mapping, since a type removed from one side is the same defect as one added to " +
    "the other. It fires on the day a seventh type is written, which is the only day anyone would remember.",
  () => {
    const TS = "lib/documents.ts";
    const TYPES_CS = "../api/ClinicManagement.Application/Features/Documents/DocumentTypes.cs";
    const NAMING_CS = "../api/ClinicManagement.Application/Features/Documents/DocumentFileNaming.cs";

    const tsFile = ALL_FILES.find((f) => rel(f) === TS);
    if (!tsFile) {
      return [{ file: TS, text: "the browser's document registry is gone - the pairing needs retargeting" }];
    }

    let typesSrc, namingSrc;
    try {
      typesSrc = readFileSync(join(WEB_ROOT, TYPES_CS), "utf8");
      namingSrc = readFileSync(join(WEB_ROOT, NAMING_CS), "utf8");
    } catch {
      return [{ file: TS, text: "a server half is unreadable - a moved file breaks the pairing silently, which is what this check exists to prevent" }];
    }

    const serverTypes = new Set(
      [...typesSrc.matchAll(/public\s+const\s+string\s+\w+\s*=\s*"([^"]+)"/g)].map((m) => m[1])
    );
    // Templates are one object per line with the key first, so the type token is the whole parse.
    const browserTypes = new Set(
      [...read(tsFile).matchAll(/^\s*type:\s*"([^"]+)"/gm)].map((m) => m[1])
    );
    const namedTypes = new Set(
      [...namingSrc.matchAll(/DocumentTypes\.(\w+)\s*=>/g)].map((m) => m[1])
    );
    const serverConstNames = new Map(
      [...typesSrc.matchAll(/public\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"/g)].map((m) => [m[2], m[1]])
    );

    if (serverTypes.size === 0 || browserTypes.size === 0) {
      return [{ file: TS, text: "one half parsed empty (server " + serverTypes.size + ", browser " + browserTypes.size + ") - the shape changed and this check can no longer see drift" }];
    }

    const problems = [];
    for (const t of serverTypes) {
      if (!browserTypes.has(t)) {
        problems.push({ file: TS, text: "the type " + t + " has no template entry", full: "add it to DOCUMENT_TEMPLATES (with creatable: false when the gallery must not offer it), or a saved one renders its raw key in the patient's Documents tab" });
      }
      const constName = serverConstNames.get(t);
      if (constName && !namedTypes.has(constName)) {
        problems.push({ file: TS, text: "the type " + t + " has no French filename", full: "add a DocumentTypes." + constName + " arm to DocumentFileNaming, or its PDF is filed under the raw type name" });
      }
    }
    for (const t of browserTypes) {
      if (!serverTypes.has(t)) {
        problems.push({ file: TS, text: "the template " + t + " names no server type", full: "the gallery offers a type the server has no constant for" });
      }
    }

    return problems;
  }
);

check(
  "examens-intro-has-one-owner",
  "N33",
  "The demande d'examens' opening sentence is the same on paper as on screen",
  "A demande d'examens carries no posologie and no dosage, so its opening formula - « Priere de bien vouloir " +
    "faire pratiquer ... » - is the ONLY thing on the sheet that says what is being prescribed rather than " +
    "merely listing it. It is written twice by contract: `ExamenContent` composes the PDF and " +
    "`web/lib/documents.ts` holds the browser's copy. A drift between them is the medicament line's own " +
    "three-copies problem with a worse consequence, because there is no second sentence to fall back on. Both " +
    "forms are compared, singular and plural: the renderer picks between them on the line count, so a browser " +
    "that keeps only one of them shows a sentence the paper does not use.",
  () => {
    const TS = "lib/documents.ts";
    const CS = "../api/ClinicManagement.Infrastructure/Services/ExamenContent.cs";

    const tsFile = ALL_FILES.find((f) => rel(f) === TS);
    if (!tsFile) {
      return [{ file: TS, text: "the browser's document vocabulary is gone - the pairing needs retargeting" }];
    }

    let csSrc;
    try {
      csSrc = readFileSync(join(WEB_ROOT, CS), "utf8");
    } catch {
      return [{ file: TS, text: "the server's half is unreadable at " + CS + " - a moved file breaks the pairing silently" }];
    }

    // Deliberately indexOf rather than a built RegExp: the declaration form is fixed on both sides
    // (`Name = "…"`), and a constructed pattern here needs escaped escapes, which is how the first version of
    // this helper shipped matching nothing at all and reporting both sentences as missing.
    const grab = (src, name) => {
      const at = src.indexOf(name + ' = "');
      if (at < 0) return null;
      const open = src.indexOf('"', at);
      const close = src.indexOf('"', open + 1);
      return close < 0 ? null : src.slice(open + 1, close);
    };

    const pairs = [
      ["singular", grab(csSrc, "IntroSingular"), grab(read(tsFile), "EXAMENS_INTRO_SINGULAR")],
      ["plural", grab(csSrc, "IntroPlural"), grab(read(tsFile), "EXAMENS_INTRO_PLURAL")],
    ];

    const problems = [];
    for (const [form, server, browser] of pairs) {
      if (!server || !browser) {
        problems.push({ file: TS, text: "the " + form + " opening formula is missing on one side", full: "server " + (server ? "ok" : "MISSING") + ", browser " + (browser ? "ok" : "MISSING") + " - one half of a printed sentence cannot be dropped silently" });
      } else if (server !== browser) {
        problems.push({ file: TS, text: "the " + form + " opening formula differs between paper and screen", full: 'server "' + server + '" vs browser "' + browser + '"' });
      }
    }

    return problems;
  }
);

check(
  "carried-act-cost-names-its-note",
  "N34",
  "A devis act another document bills states the note, never a bare « 0,000 DT »",
  "A continuation prices the already-invoiced act 0 on the devis and deliberately leaves its note UNATTACHED — " +
    "that is what stops `PlanBillingRules.BilledPlanIds` dropping the plan and hiding the new work. The 0 is " +
    "therefore correct and imposed server-side, and printed alone it says nothing: it reads as a free act. " +
    "Measured on the reported case — a 90 DT soin billed on note 2026-0019 with 50 collected, continued at " +
    "10 — the treatment screen showed « 0,000 DT » on the line and « Total convenu 10,000 · Encaissé 0,000 » " +
    "above it, on a treatment the patient had already paid 50 towards and still owed 40 on. Nothing was " +
    "arithmetically wrong; the screen was silent. `act-card.tsx` had had the rule since the plan-carried act " +
    "landed (« Aucun honoraire sur cette séance ») and it had never been carried to the devis side — this " +
    "repo's dominant defect shape, and this is what stops a third surface repeating it.",
  () => {
    const offenders = [];

    // Derived from the reader itself rather than from a file list: a second implementation is found too.
    const DECLARES = /\bfunction\s+planActCost\s*\(/;
    // `formatDT(<something>.plannedCost)` — a plan act's fee printed AS money.
    const PRINTS = /formatDT\(\s*[A-Za-z_$][\w$]*\.plannedCost\s*\)/g;

    /*
     * The two sites that legitimately print it raw, each unreachable for a carried act rather than exempted by
     * taste — stated here so a future reader can re-derive the judgement instead of trusting the list:
     *
     *   · plan-workspace's « Arrêter le traitement » list renders `stoppableItems`, filtered on
     *     `!hasDeliveredWork` — and a carried act is ALWAYS Done (its 1re séance is marked against the fiche
     *     that evidences it), so it can never appear there.
     *   · plan-step-suggestion-notice prices the act a séance is about to advance; a carried act has no step
     *     left to advance, so the notice is never about one.
     *
     * Both are structural, so if either ever stops being true this list is the thing to revisit.
     */
    const STRUCTURALLY_SAFE = new Set([
      "components/treatment-plans/plan-workspace.tsx",
      "components/treatment-plans/plan-step-suggestion-notice.tsx",
    ]);

    let owners = 0;

    for (const f of tsx()) {
      const src = read(f);
      const lines = src.split(/\r?\n/);
      const masked = commentMask(lines);
      const code = lines.map((l, i) => (masked[i] ? "" : l)).join("\n");

      if (DECLARES.test(code)) owners++;

      const where = rel(f).replace(/\\/g, "/");
      if (STRUCTURALLY_SAFE.has(where)) continue;
      // The owner is allowed to print it — that is the branch for an act nothing else bills.
      if (DECLARES.test(code)) continue;
      /*
       * ⚠️ **The test is « does this file KNOW about a carried act », not « is this exact line guarded ».** A
       * per-line window was tried first and is the wrong instrument: it fails a print sitting in the correct
       * `else` of a carried test three lines up, which is precisely the shape the fix takes. File-level is also
       * the honest granularity — a surface either understands that a devis act's fee can live on another
       * document, or it does not.
       */
      if (/\bcarriedOnNoteNumber\b|\bbilledOnInvoiceId\b/.test(code)) continue;

      for (const m of code.matchAll(PRINTS)) {
        offenders.push({
          file: rel(f),
          line: lineAt(code, m.index),
          text:
            "prints a devis act's `plannedCost` directly — a carried act's is 0 by rule, so this renders " +
            "« 0,000 DT » for a fee a note d'honoraires already collects. Go through `planActCost`, which " +
            "names the note instead.",
        });
      }
    }

    // Tripwire: the reader was renamed, so this scan is measuring nothing rather than finding nothing.
    if (owners !== 1) {
      offenders.push({
        file: "components/treatment-plans/plan-act-row.tsx",
        text:
          `found ${owners} \`planActCost\` declarations, expected exactly 1 — either the reader was renamed ` +
          "(and this guard now holds nothing) or a second copy exists (and the two are free to disagree)",
      });
    }

    return offenders;
  }
);

check(
  "recorded-act-reaches-both-drawings",
  "N35",
  "An act that charts no tooth state reaches « Cases » AND « Symboles », and is described in one place",
  "Nearly half of all recorded work writes no `ToothState` at all — measured on the dev database, 258 acts of " +
    "566 name teeth and chart nothing. « Coiffage pulpaire », « Inlay-core », « Couronne provisoire », " +
    "« Incision d'abcès » and « Greffe osseuse » are seeded `ToothCondition.Sain` on purpose (the barème's own " +
    "line for a coiffage is « à l'exclusion de l'obturation définitive »), and `ToothChartingRules` withholds a " +
    "multi-séance act's end state until it finishes. So the odontogramme, which reads `ToothStateDto[]` and " +
    "nothing else, showed a treated tooth as untouched — reported from use on a coiffage, and true in BOTH " +
    "drawings. `odontogram-recorded-acts.ts` is the one owner of that second vocabulary; this holds the two " +
    "halves no type can see. First: the derived list must be read inside the `symbolBox` block and inside the " +
    "`boxesBox` block, because the failure mode is one drawing wired and the other forgotten, and it reports " +
    "nothing anywhere — the tooth simply stays blank on whichever view the reader happens to be on, which is " +
    "exactly the disagreement `odontogram-view-switch.tsx` forbids in as many words. Second: the mark's label " +
    "and its colours live only in that module, or the two drawings and the two legends are free to name one " +
    "fact four ways.",
  () => {
    const offenders = [];

    const OWNER = "components/odontogram-recorded-acts.ts";
    const CHART = "components/odontogram.tsx";

    const ownerFile = ALL_FILES.find((f) => rel(f).replace(/\\/g, "/") === OWNER);
    const chartFile = ALL_FILES.find((f) => rel(f).replace(/\\/g, "/") === CHART);
    if (!ownerFile || !chartFile) {
      return [
        {
          file: ownerFile ? CHART : OWNER,
          text: "missing",
          full: "a file this check guards is gone — retarget or retire the check rather than deleting it",
        },
      ];
    }

    /*
     * ⚠️ The two drawings are found by their own `const … = (` declarations and sliced to the next one, never
     * by a line range: the block boundaries move every time either drawing is touched, and a range that has
     * drifted is a check measuring the wrong half in silence.
     */
    const src = read(chartFile);
    const lines = src.split(/\r?\n/);
    const masked = commentMask(lines);
    const code = lines.map((l, i) => (masked[i] ? "" : l)).join("\n");

    const blockOf = (name) => {
      const open = code.indexOf(`const ${name} = (`);
      if (open < 0) return null;
      // The next drawing's declaration, or the swap that consumes both — whichever comes first.
      const rest = code.slice(open + 1);
      const ends = [/const \w+Box = \(/, /const box = chartView/]
        .map((re) => rest.search(re))
        .filter((i) => i >= 0);
      return ends.length > 0 ? rest.slice(0, Math.min(...ends)) : rest;
    };

    for (const name of ["symbolBox", "boxesBox"]) {
      const block = blockOf(name);
      if (block === null) {
        offenders.push({
          file: CHART,
          text: `\`const ${name} = (\` no longer parses — this check is blind, fix it rather than deleting it`,
        });
        continue;
      }
      if (!/\brecordedActs\b/.test(block)) {
        offenders.push({
          file: CHART,
          line: lineAt(code, code.indexOf(`const ${name} = (`)),
          text: `\`${name}\` never reads \`recordedActs\``,
          full:
            "an act that charts no state is then on one drawing and absent from the other, with no error " +
            "anywhere — the tooth just reads as untouched on whichever view the dentist is looking at",
        });
      }
    }

    // The second half: one describer, not four. Derived from the constants themselves, so a rename is fine.
    for (const f of tsx()) {
      const where = rel(f).replace(/\\/g, "/");
      if (where === OWNER) continue;
      const fsrc = read(f);
      const fl = fsrc.split(/\r?\n/);
      const fm = commentMask(fl);
      const fcode = fl.map((l, i) => (fm[i] ? "" : l)).join("\n");
      for (const m of fcode.matchAll(/\bRECORDED_ACT_(?:LABEL|LEGEND|BOX|SWATCH|COLOR)\s*=/g)) {
        offenders.push({
          file: rel(f),
          line: lineAt(fcode, m.index),
          text: "defines a second answer to what the « acte réalisé » mark is called or coloured",
          full: `the one owner is ${OWNER}; import from it instead`,
        });
      }
    }

    return offenders;
  }
);

const only = process.argv.find((a) => a.startsWith("--only="))?.slice("--only=".length);

let failed = 0;

console.log("");
console.log("  check-responsive — mobile & tablet mechanical gate (AC-50)");
console.log("  " + "─".repeat(90));

check(
  "act-field-survives-a-re-save",
  "N36",
  "Every field of a recorded act is read back into the editor AND sent again on save",
  "`DentalRecord.SetActs` replaces the WHOLE act list on every save, so the fiche's editor is the only thing " +
    "keeping a stored field alive: read it back and forget to send it, or send it and forget to read it back, " +
    "and an ordinary re-save silently rewrites the act. This has now cost three fields. `ponticToothNumbers` " +
    "flattens a bridge somebody detailed last week — three abutments and no pontic, which is a mouth that " +
    "cannot exist; `implantPilierToothNumbers` charts rooted abutments over implants; and `isUnfinished` marks " +
    "an act FINISHED, a clinical claim nobody made, whose only symptom is the act quietly leaving " +
    "« Suites à planifier » so the séance nobody booked is chased by nothing. None of the three errors " +
    "anywhere, none is visible in a type, and the gesture that causes it is opening a fiche to fix a typo and " +
    "pressing Enregistrer. So the required set is derived from `DentalRecordActDto` itself rather than listed " +
    "here — a fourth field is covered the day it is declared — and asserted in BOTH directions, since a key " +
    "sent but never read back is the same defect seen from the other end. `id` is the one exemption and it is " +
    "computed, not an allow-list: the server mints act ids per save, so the editor keys its cards on its own " +
    "counter and the input type has no `id` at all.",
  () => {
    const offenders = [];

    const TYPES = "lib/api/types.ts";
    const READER = "components/record/use-session-acts.ts";
    const WRITER = "components/patient-record-modal.tsx";

    const find = (p) => ALL_FILES.find((f) => rel(f).replace(/\\/g, "/") === p);
    for (const p of [TYPES, READER, WRITER]) {
      if (!find(p)) {
        offenders.push({
          file: p,
          text: "missing — a file this check guards is gone; retarget or retire the check rather than deleting it",
        });
      }
    }
    if (offenders.length > 0) return offenders;

    const codeOf = (p) => {
      const lines = read(find(p)).split(/\r?\n/);
      const masked = commentMask(lines);
      return lines.map((l, i) => (masked[i] ? "" : l)).join("\n");
    };

    // ── the required set, derived from the DTO's own declaration ────────────────────────────────────────
    const types = codeOf(TYPES);
    const decl = types.match(/export interface DentalRecordActDto \{([\s\S]*?)\n\}/);
    if (!decl) {
      return [{ file: TYPES, text: "`DentalRecordActDto` no longer parses — this check is blind, fix it" }];
    }
    const required = [...decl[1].matchAll(/^\s{2}(\w+)\??:/gm)]
      .map((m) => m[1])
      // The server mints an act id per save, so the editor never round-trips one — it keys its cards on its
      // own counter and `DentalActInput` has no `id` field to send it in.
      .filter((k) => k !== "id");

    if (required.length < 5) {
      return [
        {
          file: TYPES,
          text:
            `derived only ${required.length} act field(s) from \`DentalRecordActDto\` — the declaration's shape ` +
            "changed and this check is measuring nothing; fix the parse rather than trusting a green run",
        },
      ];
    }

    // ── the read-back half: `actFromDto` ────────────────────────────────────────────────────────────────
    const reader = codeOf(READER);
    const fn = reader.slice(reader.indexOf("function actFromDto"));
    if (!fn.startsWith("function actFromDto")) {
      return [{ file: READER, text: "`actFromDto` no longer parses — this check is blind, fix it" }];
    }
    // To the next top-level declaration, so the body's boundary moves with the file rather than with a range.
    const nextDecl = fn.slice(1).search(/\n(?:export )?(?:function|const|interface|type) /);
    const body = nextDecl >= 0 ? fn.slice(0, nextDecl + 1) : fn;

    for (const key of required) {
      if (!new RegExp(`\\ba\\.${key}\\b`).test(body)) {
        offenders.push({
          file: READER,
          text:
            `\`actFromDto\` never reads \`a.${key}\` — a fiche reopened for editing cannot see it, so the next ` +
            "save sends the default and overwrites what was stored",
        });
      }
    }

    // ── the send half: the modal's `DentalActInput[]` payload ───────────────────────────────────────────
    const writer = codeOf(WRITER);
    const at = writer.indexOf("const parsedActs: DentalActInput[] =");
    /*
     * ⚠️ Sliced to the next top-level statement of the same function, never to a fixed closing token: the
     * builder's own indentation moves whenever the surrounding save is touched, and a boundary pattern that
     * has drifted makes this check pass while measuring nothing.
     */
    const rest = at >= 0 ? writer.slice(at) : "";
    const end = rest.slice(1).search(/\n {4}(?:const|let|if|return|setLoading)\b/);
    const payload = at >= 0 ? (end >= 0 ? rest.slice(0, end + 1) : rest) : null;
    if (!payload || !/procedureName:/.test(payload)) {
      return [
        { file: WRITER, text: "the `DentalActInput[]` payload no longer parses — this check is blind, fix it" },
      ];
    }
    for (const key of required) {
      if (!new RegExp(`^\\s*${key}:`, "m").test(payload)) {
        offenders.push({
          file: WRITER,
          text:
            `the act payload never sends \`${key}\` — \`SetActs\` rebuilds every act from it, so saving the ` +
            "fiche rewrites that field to its default with no gesture, no toast and no error",
        });
      }
    }

    return offenders;
  }
);

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
