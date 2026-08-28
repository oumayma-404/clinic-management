#!/usr/bin/env node
/* ═══════════════════════════════════════════════════════════════════════════
   preview.mjs — fold dist/<page> into ONE self-contained file for review.

   Not a second design. It inlines the built page's own stylesheet, script and
   images so the result can be published somewhere the owner can open it on a
   phone. The artifact host supplies <!doctype>/<html>/<head>/<body>, so this
   emits the page CONTENT only.

   node preview.mjs [page]      default: index.html
   ═══════════════════════════════════════════════════════════════════════════ */

import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'
import { join, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const HERE = dirname(fileURLToPath(import.meta.url))
const OUT = join(HERE, 'dist')
const page = process.argv[2] ?? 'index.html'

let html = readFileSync(join(OUT, page), 'utf8')
const css = readFileSync(join(OUT, 'assets', 'site.css'), 'utf8')
const js = readFileSync(join(OUT, 'assets', 'site.js'), 'utf8')

// Images → data: URIs. The host's CSP blocks every external host but Google
// Fonts, so nothing may stay a relative path.
html = html.replace(/src="([^"]*assets\/img\/([^"]+))"/g, (_m, _full, file) => {
  const b = readFileSync(join(OUT, 'assets', 'img', file))
  const type = file.endsWith('.webp') ? 'image/webp' : file.endsWith('.png') ? 'image/png' : 'image/jpeg'
  return `src="data:${type};base64,${b.toString('base64')}"`
})

// The gallery wants the product's NAME, not the page's SEO title.
// ⚠️ The hero scene is an <iframe src="assets/scenes/…">. A relative path has
// nothing to resolve against inside a single published document, so the frame
// would come up empty. Fold the scene in as srcdoc instead.
html = html.replace(/src="([^"]*assets\/scenes\/([^"]+))"/g, (_m, _full, file) => {
  const doc = readFileSync(join(OUT, 'assets', 'scenes', file), 'utf8')
  return 'srcdoc="' + doc.replace(/&/g, '&amp;').replace(/"/g, '&quot;') + '"'
})

const title = 'APEXA'
const fonts = (html.match(/<link rel="stylesheet" href="https:\/\/fonts\.googleapis[^>]*>/) || [''])[0]

// Keep only what sits between <body> and </body>, then drop the asset links.
let body = html.slice(html.indexOf('<body'), html.lastIndexOf('</body>'))
body = body.slice(body.indexOf('>') + 1)
body = body.replace(/<script src="[^"]*"[^>]*><\/script>/g, '')

// The other pages are not built yet, so every internal link would leave the
// preview for a 404. Anchors inside this page still work.
body = body.replace(/href="(?!#|mailto:|https?:)[^"]*"/g, (m) => {
  const hash = m.match(/#([^"]*)"/)
  return hash ? `href="#${hash[1]}"` : 'href="#" data-todo'
})

const single = `<title>${title}</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
${fonts}
<style>
/* The page owns its ground: the host paints its own theme behind the frame,
   and a transparent body would borrow it. */
html, body { background: #ffffff; }
${css}
</style>
${body}
<script>
${js}
</script>
`

mkdirSync(join(HERE, 'preview'), { recursive: true })
const dest = join(HERE, 'preview', page.replace(/[\\/]/g, '-'))
writeFileSync(dest, single)
console.log(`${dest}  ${(Buffer.byteLength(single) / 1024).toFixed(0)} KB`)
