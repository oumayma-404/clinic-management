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
/* ⚠️ LE DOMAINE RÉEL, jamais l'URL github.io : celle-ci 301 vers celui-là, donc un
   canonique qui la désigne désigne une redirection — et Google classe alors la page
   « Autre page avec balise canonique correcte », c'est-à-dire ne l'indexe jamais.
   Un seul littéral : canonical, og:url, og:image et le sitemap en sortent tous. */
const BASE = 'https://apexa.tn/'

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
/* The word half of the lockup, same story: it sits inside NAV and FOOTER, three times a page. */
const wordSrc = readFileSync(join(SRC, 'partials', 'wordmark.html'), 'utf8')
  .replace(/<!--[\s\S]*?-->/g, '').trim()

/* ── Le chemin CANONIQUE d'une page, qui n'est pas son chemin de fichier ──────
   L'accueil est servi à `/` ET à `/index.html` : deux URL, une page. Le canonique
   déclare la première. ⚠️ `meta.path` reste le chemin d'écriture, lui ne change pas. */
const canonOf = p => (p === 'index.html' ? '' : p)

/* ── Données structurées ─────────────────────────────────────────────────────
   Un fichier par page dans src/jsonld/, nommé comme la page (`index.json`). Absent
   = pas de balisage, et `{{JSONLD}}` devient vide. Le JSON est reparsé puis
   re-sérialisé : un fichier invalide fait échouer le build ici, jamais en ligne.
   ⚠️ `<` est échappé — un `</script>` dans une valeur fermerait la balise. */
function jsonLdFor (pagePath) {
  const f = join(SRC, 'jsonld', pagePath.replace(/\.html$/, '.json'))
  if (!existsSync(f)) return ''
  const data = JSON.parse(readFileSync(f, 'utf8'))
  const body = JSON.stringify(data).replaceAll('<', '\\u003c')
  return `<script type="application/ld+json">${body}</script>`
}

const pages = walk(join(SRC, 'pages'))
const built = []
/* Les URL du sitemap, remplies par la boucle : une page ajoutée demain y entre seule. */
const urls = []

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
    TITLE: meta.title, DESC: meta.desc, CANON: canonOf(meta.path),
    JSONLD: jsonLdFor(meta.path),
    ROOT: meta.root ?? '', BASE, MARK: markSrc, WORD: wordSrc,
    // "" on the home page so a nav anchor is a same-document jump; "index.html" elsewhere.
    HOME: meta.home ?? '',
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
  urls.push({ loc: BASE + canonOf(meta.path), priority: meta.path === 'index.html' ? '1.0' : '0.6' })
}

/* 5 · robots.txt + sitemap.xml. Les deux répondaient 404 en ligne.
   ⚠️ `Disallow: /assets/scenes/` : ce sont trois documents HTML autonomes, embarqués
   en <iframe>, donc indexables comme des pages à part entière — « Quatre temps » dans
   les résultats sous le nom de la marque. Elles portent aussi un `noindex`, parce
   qu'un Disallow n'enlève pas une URL déjà indexée : il empêche seulement de la relire. */
writeFileSync(join(OUT, 'robots.txt'), [
  'User-agent: *',
  'Allow: /',
  'Disallow: /assets/scenes/',
  '',
  `Sitemap: ${BASE}sitemap.xml`,
  '',
].join('\n'))

const lastmod = new Date().toISOString().slice(0, 10)
writeFileSync(join(OUT, 'sitemap.xml'), [
  '<?xml version="1.0" encoding="UTF-8"?>',
  '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
  // L'accueil d'abord, le reste par ordre alphabétique : `walk` rend l'ordre du disque.
  ...urls.sort((a, b) => Number(b.priority) - Number(a.priority) || a.loc.localeCompare(b.loc))
         .map(u => `  <url><loc>${u.loc}</loc><lastmod>${lastmod}</lastmod>` +
                   `<changefreq>monthly</changefreq><priority>${u.priority}</priority></url>`),
  '</urlset>',
  '',
].join('\n'))

// 6 · GitHub Pages needs this or it runs the output through Jekyll.
writeFileSync(join(OUT, '.nojekyll'), '')

console.log('pages')
for (const b of built) console.log('  ' + b)
console.log(`seo   robots.txt · sitemap.xml (${urls.length} url) · base ${BASE}`)
if (imgReport.length) { console.log('images'); for (const i of imgReport) console.log('  ' + i) }
console.log(`css   ${(statSync(join(OUT, 'assets', 'site.css')).size / 1024).toFixed(1)} KB`)
console.log(`js    ${(statSync(join(OUT, 'assets', 'site.js')).size / 1024).toFixed(1)} KB`)
