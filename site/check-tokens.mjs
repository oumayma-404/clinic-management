#!/usr/bin/env node
/* ═══════════════════════════════════════════════════════════════════════════
   check-tokens.mjs — the site's style guard. No dependencies.

   The page read as « amateur » for a reason that could be counted: 77 font
   sizes, 41 radii, 30 shadows and about a hundred colour literals, because
   every section had been styled on its own. tokens.css now holds CLOSED
   scales, and this fails when a stylesheet reaches past them:

     font-size      var(--t-*), or a size relative to its box (em, %, cqi, vw
                    inside clamp/min/max), or inherit
     border-radius  var(--r-*), %, em, 0, inherit
     box-shadow     var(--shadow-*) / var(--glow-*), none, or an INSET hairline
                    (an inset 0-blur line is a border, not a shadow)
     colour         a var(), currentColor, transparent, inherit — or a
                    neutral tint written as rgb(255 255 255 / a) or
                    rgb(9 9 11 / a), which are --white and --text at an alpha

   tokens.css itself is exempt: it is where literals are allowed to live.
   A line may opt out with a trailing `/* token-exempt: <reason> *\/`.

   It also fails on a claim the site may not make (site/BRIEF.md « Claims »)
   appearing in index.html.

   node check-tokens.mjs            report and exit 1 on any finding
   node check-tokens.mjs --summary  counts per property only
   ═══════════════════════════════════════════════════════════════════════════ */

import { readFileSync } from 'node:fs'
import { join, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const HERE = dirname(fileURLToPath(import.meta.url))
const SRC = join(HERE, 'src')
const summary = process.argv.includes('--summary')

const FILES = ['css/base.css', 'css/components.css']

const NEUTRAL = /rgb\(\s*(255 255 255|9 9 11|0 0 0)\s*\/\s*[\d.]+%?\s*\)/g
const COLOUR_LITERAL = /#[0-9a-fA-F]{3,8}\b|\b(?:rgb|rgba|hsl|hsla|oklch|oklab)\([^)]*\)/g

const findings = []
const add = (file, line, prop, value, why) => findings.push({ file, line, prop, value: value.trim(), why })

// Comments are blanked, not removed, so every offset still maps to its line.
const stripComments = (s) => s.replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, ' '))
// Split on commas at paren depth 0 — `color-mix(in oklab, a, b)` is one layer.
const splitTop = (s) => { const out = []; let d = 0, cur = ''; for (const ch of s) { if (ch === '(') d++; if (ch === ')') d--; if (ch === ',' && !d) { out.push(cur); cur = '' } else cur += ch } out.push(cur); return out.map(x => x.trim()) }

for (const rel of FILES) {
  const raw = readFileSync(join(SRC, rel), 'utf8')
  const rawLines = raw.split('\n')
  const text = stripComments(raw)
  // Every declaration, however many lines it spans: `prop: value` up to ; or }.
  const DECL = /(-{0,2}[a-z][a-z0-9-]*)\s*:\s*([^;{}]+?)\s*(?=[;}])/g
  let m
  while ((m = DECL.exec(text))) {
    const i = text.slice(0, m.index).split('\n').length - 1
    const endLine = i + m[0].split('\n').length - 1
    if (rawLines.slice(i, endLine + 1).some(l => /token-exempt:/.test(l))) continue
    // a selector like `a:hover {` looks like a declaration; a real one is preceded by { ; or a newline
    const before = text.slice(Math.max(0, m.index - 1), m.index)
    if (before && !/[\s{;]/.test(before)) continue
    {
      const prop = m[1]
      const value = m[2].replace(/!important/, '').replace(/\s+/g, ' ').trim()
      if (/^(from|to|\d+%)$/.test(prop)) continue
      // Custom properties may hold literals only in tokens.css.
      if (prop.startsWith('-') && !prop.startsWith('--')) { /* vendor prefix: check like the property */ }
      else if (prop.startsWith('--')) {
        const lit = value.replace(NEUTRAL, '').match(COLOUR_LITERAL)
        if (lit) add(rel, i + 1, prop, lit.join(' '), 'colour literal in a custom property (put it in tokens.css)')
        continue
      }

      if (prop === 'font-size') {
        const ok = /^var\(--t-[\w-]+\)$/.test(value)
          || /^-?[\d.]+(em|%)$/.test(value)
          || value === 'inherit'
          || (/^(clamp|min|max)\(/.test(value) && /(cqi|cqw|vw|em)/.test(value))
        if (!ok) add(rel, i + 1, prop, value, 'font-size outside the scale')
      }

      if (prop === 'border-radius' || /^border-(top|bottom)-(left|right)-radius$/.test(prop)) {
        // calc() of tokens is a derived radius (a screen inside a bezel), not a new one
        const parts = value.replace(/calc\([^()]*(\([^()]*\)[^()]*)*\)/g, 'V').replace(/var\([^)]*\)/g, 'V').split(/\s+|\//).filter(Boolean)
        const bad = parts.filter(p => !(p === 'V' || p === '0' || p === 'inherit' || /^[\d.]+(%|em)$/.test(p)))
        if (bad.length) add(rel, i + 1, prop, value, 'radius outside the scale')
      }

      if (prop === 'box-shadow') {
        const layers = splitTop(value)
        const bad = layers.filter(l => !(l === 'none' || /^var\(--(shadow|glow)-[\w-]+\)$/.test(l) || /^inset\s+(0|-?[\d.]+px)\s+(0|-?[\d.]+px)\s+0\s/.test(l)))
        if (bad.length) add(rel, i + 1, prop, value, 'shadow outside the set')
      }

      // A mask is alpha only: its black is not a colour anybody sees.
      if (/mask-image$/.test(prop)) continue
      const colours = value.replace(NEUTRAL, '').match(COLOUR_LITERAL)
      if (colours) add(rel, i + 1, prop, colours.join(' '), 'colour literal (put it in tokens.css)')
    }
  }
}

/* ── Claims ─────────────────────────────────────────────────────────────── */
const BANNED = [
  [/sans\s+internet|hors[\s-]ligne/i, '« sans internet » is true of one deployment only (BRIEF « Claims »)'],
  [/\bCNAM\b/, 'CNAM is not on any screen (BRIEF « Claims »)'],
  [/\bTVA\b|timbre fiscal/i, 'no TVA nor timbre on a note (BRIEF « Claims »)'],
]
const read = (rel) => readFileSync(join(SRC, rel), 'utf8').replace(/<!--[\s\S]*?-->/g, '')
const index = read('pages/index.html')
for (const [re, why] of BANNED) if (re.test(index)) findings.push({ file: 'pages/index.html', line: 0, prop: 'claim', value: index.match(re)[0], why })

// Reminders are not live (owner, 29/09): no page may sell them, and neither may the hero film or
// Google's structured data. The privacy page is exempt — it lists the processors, which is a
// disclosure and not a claim.
const REMINDERS = /rappels?\s+(SMS|WhatsApp|de contrôle|automatique)|\bSMS\b|WhatsApp|les rappels partent/i
for (const rel of ['pages/index.html', 'pages/odontogramme.html', 'pages/logiciel-dentaire-hors-ligne.html',
                   'pages/guide-choisir-logiciel-cabinet-dentaire.html', 'scenes/hero-quatre-temps.html', 'jsonld/index.json']) {
  const m = read(rel).match(REMINDERS)
  if (m) findings.push({ file: rel, line: 0, prop: 'claim', value: m[0], why: 'reminders are not live yet (BRIEF « Claims »)' })
}

/* ── Report ─────────────────────────────────────────────────────────────── */
if (!findings.length) { console.log('check-tokens: clean'); process.exit(0) }
const byProp = {}
for (const f of findings) (byProp[f.why] ??= []).push(f)
for (const [why, list] of Object.entries(byProp)) {
  console.log(`\n${why} — ${list.length}`)
  if (!summary) for (const f of list) console.log(`  ${f.file}:${f.line}  ${f.prop}: ${f.value}`)
}
console.log(`\ncheck-tokens: ${findings.length} finding(s)`)
process.exit(1)
