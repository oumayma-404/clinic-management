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
