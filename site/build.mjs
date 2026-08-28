#!/usr/bin/env node
/* ═══════════════════════════════════════════════════════════════════════════
   build.mjs — the whole static generator. No dependencies.

   src/pages/**.html   one file per page, with a leading JSON comment
   src/layout.html     the shell
   src/partials/*.html nav, footer
   src/css/*.css       concatenated, in order, into dist/assets/site.css
   src/js/site.js      copied to dist/assets/site.js
   src/img/*           encoded to WebP into dist/assets/img/

   ⚠️ Every comment is stripped from the OUTPUT. A previous version of this
   site published an internal roadmap note that lived in a CSS header comment.
   Source keeps its comments; dist never gets them.
   ═══════════════════════════════════════════════════════════════════════════ */

import { readFileSync, writeFileSync, mkdirSync, readdirSync, rmSync, existsSync, statSync, copyFileSync } from 'node:fs'
import { join, dirname, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { execFileSync } from 'node:child_process'

const HERE = dirname(fileURLToPath(import.meta.url))
const SRC = join(HERE, 'src')
const OUT = join(HERE, 'dist')
const BASE = 'https://oumayma-404.github.io/gestion-clinique-site/'

const args = new Set(process.argv.slice(2))
const skipImages = args.has('--no-images')

/* ── ffmpeg. Installed per-user by winget and NOT on PATH until a new shell,
      so it is looked for rather than assumed. ──────────────────────────── */
function ffmpegPath () {
  const local = process.env.LOCALAPPDATA
  if (local) {
    const p = join(local, 'Microsoft', 'WinGet', 'Packages',
      'Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe',
      'ffmpeg-9.0-full_build', 'bin', 'ffmpeg.exe')
    if (existsSync(p)) return p
  }
  return 'ffmpeg'
}

/* ── Comment stripping ───────────────────────────────────────────────────── */
const stripHtmlComments = s => s.replace(/<!--[\s\S]*?-->/g, '')
const stripCssComments  = s => s.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\n{3,}/g, '\n\n')
const stripJsComments   = s => s
  .replace(/\/\*[\s\S]*?\*\//g, '')
  .replace(/^[ \t]*\/\/.*$/gm, '')
  .replace(/\n{3,}/g, '\n\n')

/* ── Walk src/pages ──────────────────────────────────────────────────────── */
function walk (dir, acc = []) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name)
    if (statSync(p).isDirectory()) walk(p, acc)
    else if (name.endsWith('.html')) acc.push(p)
  }
  return acc
}

/* ── Go ──────────────────────────────────────────────────────────────────── */
// Everything but the encoded images, which are expensive and rebuilt on mtime.
for (const name of existsSync(OUT) ? readdirSync(OUT) : []) {
  if (name === 'assets') continue
  rmSync(join(OUT, name), { recursive: true, force: true })
}
for (const name of existsSync(join(OUT, 'assets')) ? readdirSync(join(OUT, 'assets')) : []) {
  if (name === 'img') continue
  rmSync(join(OUT, 'assets', name), { recursive: true, force: true })
}
mkdirSync(join(OUT, 'assets', 'img'), { recursive: true })

// 1 · CSS, in order. tokens first — everything else reads from it.
const cssOrder = ['tokens.css', 'base.css', 'components.css']
const css = cssOrder.map(f => readFileSync(join(SRC, 'css', f), 'utf8')).join('\n')
writeFileSync(join(OUT, 'assets', 'site.css'), stripCssComments(css))

// 2 · JS
writeFileSync(join(OUT, 'assets', 'site.js'), stripJsComments(readFileSync(join(SRC, 'js', 'site.js'), 'utf8')))

// 3 · Images → WebP
let imgReport = []
if (!skipImages && existsSync(join(SRC, 'img'))) {
  const FF = ffmpegPath()
  for (const name of readdirSync(join(SRC, 'img'))) {
    const from = join(SRC, 'img', name)
    // ⚠️ `og-*` is passed through untouched. WhatsApp — which is how a link
    //    gets shared in Tunisia — does not reliably render a WebP social card,
    //    so the one image on the site that must stay a JPEG is this one.
    if (/^og-/i.test(name)) {
      copyFileSync(from, join(OUT, 'assets', 'img', name))
      imgReport.push(`${name} (copied, social card)`)
    } else if (/\.(png|jpg|jpeg)$/i.test(name)) {
      const to = join(OUT, 'assets', 'img', name.replace(/\.\w+$/, '.webp'))
      if (existsSync(to) && statSync(to).mtimeMs > statSync(from).mtimeMs) {
        imgReport.push(`${name} → cached`)
        continue
      }
      execFileSync(FF, ['-y', '-loglevel', 'error', '-i', from,
        '-vf', "scale='min(2000,iw)':-2:flags=lanczos",
        '-quality', '78', '-compression_level', '6', to])
      imgReport.push(`${name} → ${(statSync(to).size / 1024).toFixed(0)} KB`)
    } else {
      copyFileSync(from, join(OUT, 'assets', 'img', name))
      imgReport.push(`${name} (copied)`)
    }
  }
}

// 3b · Animated hero scenes. Standalone documents, copied verbatim: they are
//      embedded in an <iframe>, so nothing here may rewrite them.
if (existsSync(join(SRC, 'scenes'))) {
  mkdirSync(join(OUT, 'assets', 'scenes'), { recursive: true })
  for (const name of readdirSync(join(SRC, 'scenes'))) {
    if (!name.endsWith('.html')) continue
    copyFileSync(join(SRC, 'scenes', name), join(OUT, 'assets', 'scenes', name))
  }
}

// 4 · Pages
const layout = readFileSync(join(SRC, 'layout.html'), 'utf8')
const navSrc = readFileSync(join(SRC, 'partials', 'nav.html'), 'utf8')
const footSrc = readFileSync(join(SRC, 'partials', 'footer.html'), 'utf8')
/* The mark is a partial of its own because it appears three times per page across TWO other
   partials. It is substituted after NAV/FOOTER are injected, so the `{{MARK}}` tokens inside
   them resolve in the same pass — and the leftover-token check below catches a typo'd one. */
const markSrc = readFileSync(join(SRC, 'partials', 'mark.html'), 'utf8')
  .replace(/<!--[\s\S]*?-->/g, '').trim()

const pages = walk(join(SRC, 'pages'))
const built = []

for (const file of pages) {
  const raw = readFileSync(file, 'utf8')
  const m = raw.match(/^\s*<!--(\{[\s\S]*?\})-->/)
  if (!m) throw new Error(`${relative(HERE, file)} has no leading JSON front-matter comment`)
  const meta = JSON.parse(m[1])
  const body = raw.slice(m[0].length)

  let html = layout
    .replace('{{NAV}}', navSrc)
    .replace('{{FOOTER}}', footSrc)
    .replace('{{BODY}}', body)

  for (const [k, v] of Object.entries({
    TITLE: meta.title, DESC: meta.desc, PATH: meta.path,
    ROOT: meta.root ?? '', BASE, MARK: markSrc,
    BODYCLASS: meta.bodyClass ?? '', BARCLASS: meta.barClass ?? '',
  })) html = html.replaceAll(`{{${k}}}`, v)

  // Mark the current nav item. Done here, not in the partial, so the partial
  // stays one file for every page.
  if (meta.nav) html = html.replace(`data-nav="${meta.nav}"`, `data-nav="${meta.nav}" aria-current="page"`)

  const leftover = html.match(/\{\{[A-Z_]+\}\}/g)
  if (leftover) throw new Error(`${meta.path}: unreplaced token(s) ${[...new Set(leftover)].join(', ')}`)

  html = stripHtmlComments(html)

  const dest = join(OUT, meta.path)
  mkdirSync(dirname(dest), { recursive: true })
  writeFileSync(dest, html)
  built.push(`${meta.path}  ${(Buffer.byteLength(html) / 1024).toFixed(1)} KB`)
}

// 5 · GitHub Pages needs this or it runs the output through Jekyll.
writeFileSync(join(OUT, '.nojekyll'), '')

console.log('pages')
for (const b of built) console.log('  ' + b)
if (imgReport.length) { console.log('images'); for (const i of imgReport) console.log('  ' + i) }
console.log(`css   ${(statSync(join(OUT, 'assets', 'site.css')).size / 1024).toFixed(1)} KB`)
console.log(`js    ${(statSync(join(OUT, 'assets', 'site.js')).size / 1024).toFixed(1)} KB`)
