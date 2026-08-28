#!/usr/bin/env node
/**
 * lint-iss — the mechanical half of « the installers still compile ».
 *
 * WHY THIS EXISTS
 * `packaging/CLAUDE.md` already states the rule: « Inno `{ }` comments must not contain `{app}`/`{sys}`/`{tmp}`
 * — Pascal comments do not nest, so the first `}` ends the comment early and the rest is parsed as code. Use
 * `//`. » The rule was written down and then broken anyway, twice, and nothing noticed: ISCC is not runnable in
 * CI (Windows + Inno Setup 6), the payloads are staged by hand, and the two `.iss` files went months without
 * being compiled at all. In that window a brace comment acquired an `{app}` and another acquired a `[Files]`
 * mention, and the SERVER installer stopped compiling outright — « Invalid section tag », then « Syntax error »
 * on a line of prose. A clinic could not have been given an update, and the only symptom was an operator
 * running ISCC and reading a line number.
 *
 * So this is the part of that check which needs no Windows and no Inno Setup: pure text, runnable anywhere,
 * cheap enough for CI. It does NOT prove the installers compile — only ISCC does that, and the release
 * procedure in README.md says to run it. It proves the one failure mode that is invisible until you do.
 *
 * Usage: node packaging/lint-iss.mjs            (exit 1 on any finding)
 */

import { readFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const FILES = [join(HERE, "server", "clinic-server.iss"), join(HERE, "client", "clinic-client.iss")];

/** Inno constants whose own `}` would close a `{ }` comment. Not exhaustive by design — any `{word}` counts. */
const CONSTANT = /\{[a-z][a-z0-9]*\}/g;
/** A section tag at the start of a line is what ISCC misreads once a comment has closed early. */
const SECTION = /^\s*\[[A-Za-z]+\]/;

const findings = [];

for (const file of FILES) {
  const rel = relative(join(HERE, ".."), file).split("\\").join("/");
  const lines = readFileSync(file, "utf8").split(/\r?\n/);

  for (let i = 0; i < lines.length; i++) {
    // A brace comment opens on a line whose first non-space character is `{`, and `{#` is a preprocessor
    // directive rather than a comment.
    if (!/^\s*\{[^#]/.test(lines[i])) continue;

    // Collect to the line carrying the first `}` — which is where ISCC will consider the comment ended.
    const body = [];
    let j = i;
    for (; j < lines.length; j++) {
      body.push(lines[j]);
      if (lines[j].includes("}")) break;
    }
    const text = body.join("\n");

    const consts = [...new Set((text.match(CONSTANT) ?? []))];
    if (consts.length > 0) {
      findings.push({
        file: rel,
        line: i + 1,
        what: `brace comment contains ${consts.join(", ")} — its \`}\` closes the comment early`,
      });
    }
    if (body.some((l) => SECTION.test(l))) {
      findings.push({
        file: rel,
        line: i + 1,
        what: "brace comment contains a [Section] tag at line start — ISCC reads it as a real section",
      });
    }

    i = j; // continue after this comment
  }
}

console.log("");
console.log("  lint-iss — Inno Setup comment traps");
console.log("  " + "─".repeat(88));

if (findings.length === 0) {
  console.log("  ✓ no brace comment closes itself early");
  console.log("");
  process.exit(0);
}

for (const f of findings) {
  console.log(`  ✗ ${f.file}:${f.line}  ${f.what}`);
}
console.log("");
console.log("  Use `//` line comments for anything mentioning a path constant or a section name.");
console.log(`  ${findings.length} finding(s).`);
console.log("");
process.exit(1);
