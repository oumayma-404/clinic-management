#!/usr/bin/env node
/**
 * ci-stub-payloads — put a token file everywhere the installer expects a payload, so ISCC can COMPILE in CI.
 *
 * WHY THIS EXISTS
 * `packaging/lint-iss.mjs` catches one failure mode (a `{ }` comment closed early by its own `{app}`), and for
 * a long time it was the only mechanical check the installers had. Everything else — a bad `[Section]` tag, an
 * unbalanced `begin`/`end`, a misspelt flag, an undeclared Pascal identifier, a function called before it is
 * declared — is invisible until somebody runs ISCC on Windows. Nobody did for months, and in that window the
 * server installer stopped compiling outright: a clinic could not have been sent an update, and the only
 * symptom was an operator reading a line number.
 *
 * ISCC refuses to compile when a `Source:` matches no file, so the compile cannot be run against a clean
 * checkout — the real payloads are ~450 MB of PostgreSQL, Node and two published .NET apps, none of which
 * belong in CI. This writes one small file into each expected location instead.
 *
 * ⚠️ **THE LIST IS DERIVED FROM THE `.iss`, NEVER HAND-KEPT.** A hand-written list is the defect this repo
 * repeats: it goes stale, ISCC then fails with « no files found matching », and that reads exactly like a
 * broken script rather than like a stale stub list. Every `Source:` line is parsed out and stubbed, so a new
 * payload is covered the day it is added.
 *
 * WHAT THIS PROVES, AND WHAT IT DOES NOT
 * It proves the script COMPILES. It proves nothing about whether the installer INSTALLS — that stays the
 * operator checklist in README.md, which needs a real Windows machine, a real PostgreSQL cluster and a real
 * clinic LAN.
 *
 * Usage: node packaging/ci-stub-payloads.mjs
 */

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const ISS = join(HERE, "setup", "clinic-setup.iss");

/** `Source: "<path>"; …` — the path is always the first quoted value on the line. */
const SOURCE = /^\s*Source:\s*"([^"]+)"/;

/**
 * A source ISCC is already told it may not find needs no stub — and must not get one, because those are the
 * entries that live OUTSIDE `build-output/`: `packaging/server/tools/nssm.exe` is a binary the operator drops
 * in by hand, and a stub written over it would be a 122-byte file named `nssm.exe` that the next real build
 * would happily ship as a service host.
 */
const OPTIONAL = /\bskipifsourcedoesntexist\b/i;

/** Nothing is written outside this directory. The belt to `OPTIONAL`'s braces. */
const SANDBOX = join(HERE, "build-output");

/**
 * A `Source:` whose path ends in a wildcard needs at least one matching file; a named one needs that exact
 * name. Either way a single file is enough for ISCC.
 */
function stubTargetFor(sourcePath) {
  // `{#SourcePath}` is the directory holding the .iss. Nothing else is expanded, because nothing else is used.
  const expanded = sourcePath.replaceAll("{#SourcePath}", join(HERE, "setup"));
  return expanded.endsWith("*") ? join(expanded.slice(0, -1), "ci-stub.dat") : expanded;
}

const body =
  "This file exists only so ISCC can compile the installer in CI. It is never shipped.\r\n" +
  "See packaging/ci-stub-payloads.mjs.\r\n";

const sources = readFileSync(ISS, "utf8")
  .split(/\r?\n/)
  .filter((line) => !OPTIONAL.test(line))
  .map((line) => line.match(SOURCE)?.[1])
  .filter(Boolean);

if (sources.length === 0) {
  // The parse found nothing, which would let ISCC run against an empty tree and fail for a reason that has
  // nothing to do with the script. Fail here instead, where the message can say so.
  console.error(`ci-stub-payloads: no Source: line found in ${ISS} — the parser has stopped matching.`);
  process.exit(1);
}

const written = new Set();
for (const source of sources) {
  const target = normalize(stubTargetFor(source));
  if (written.has(target)) continue;

  if (!target.startsWith(normalize(SANDBOX))) {
    console.error(
      `ci-stub-payloads: refusing to write outside build-output/ — ${target}\n` +
        "  A required Source: now points at a file this script would have to fabricate in the repository.\n" +
        "  Either stage it under build-output/, or mark the entry skipifsourcedoesntexist."
    );
    process.exit(1);
  }

  mkdirSync(dirname(target), { recursive: true });
  writeFileSync(target, body);
  written.add(target);
  console.log(`  stubbed ${target}`);
}

console.log(`ci-stub-payloads: ${written.size} stub(s) for ${sources.length} Source: line(s).`);
