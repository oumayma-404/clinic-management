/**
 * Builds the three samples that cannot simply be downloaded: a laboratory's ZIP, a study large enough to be
 * filed in the coffre rather than hosted (over the catalogue's 25 Mo line), and an archive over the same line.
 *
 * Run it from this folder, AFTER the four `curl` downloads in README.md — the archives contain them.
 */
import { execFileSync } from 'node:child_process'
import fs from 'node:fs'
import path from 'node:path'

import { fileURLToPath, pathToFileURL } from 'node:url'

const DIR = path.dirname(fileURLToPath(import.meta.url))

// `sharp` is already a dependency of `web/` (the icon generator uses it); borrowing it beats a second install.
const sharp = (await import(
  pathToFileURL(path.join(DIR, '..', '..', 'web', 'node_modules', 'sharp', 'dist', 'index.cjs')).href
)).default

// ── A panoramique too large to host: uncompressed TIFF, ~35 Mo ────────────────────────────────────────────
const W = 4600
const H = 2400
const raw = Buffer.alloc(W * H * 3)
for (let y = 0; y < H; y++) {
  for (let x = 0; x < W; x++) {
    const i = (y * W + x) * 3
    // A crude radiograph-ish gradient with an arch, so the decoded picture is recognisably *something*.
    const arch = Math.abs(y - (H * 0.35 + Math.sin((x / W) * Math.PI) * H * 0.28))
    const v = Math.max(0, 235 - arch * 0.9) + ((x * 7 + y * 13) % 17)
    raw[i] = raw[i + 1] = raw[i + 2] = Math.min(255, v)
  }
}

const bigTiff = path.join(DIR, 'panoramique-haute-definition.tiff')
await sharp(raw, { raw: { width: W, height: H, channels: 3 } })
  .tiff({ compression: 'none' })
  .toFile(bigTiff)
console.log('big tiff:', (fs.statSync(bigTiff).size / 1048576).toFixed(1), 'Mo')

// ── A laboratory's package: small enough to be hosted ─────────────────────────────────────────────────────
const staging = path.join(DIR, '_labo')
fs.rmSync(staging, { recursive: true, force: true })
fs.mkdirSync(path.join(staging, 'photos'), { recursive: true })

fs.copyFileSync(path.join(DIR, 'photo-intrabuccale-iphone.heic'), path.join(staging, 'photos', 'teinte-vestibulaire.heic'))
fs.copyFileSync(path.join(DIR, 'radio-retroalveolaire.tif'), path.join(staging, 'photos', 'radio-controle.tif'))
fs.writeFileSync(
  path.join(staging, 'bon-de-commande.txt'),
  ['Laboratoire de prothèse Ben Ayed', '', 'Patient : A. T.', 'Travail : couronne céramo-céramique 26',
   'Teinte : A2', 'Livraison souhaitée : le 12/09/2026', ''].join('\r\n'),
  'utf8'
)

const labZip = path.join(DIR, 'bon-labo-couronne-26.zip')
fs.rmSync(labZip, { force: true })
execFileSync('powershell', [
  '-NoProfile', '-Command',
  `Compress-Archive -Path '${staging}\\*' -DestinationPath '${labZip}' -CompressionLevel Optimal`,
])
console.log('lab zip:', (fs.statSync(labZip).size / 1048576).toFixed(1), 'Mo')

// ── A study package too large to host, so the coffre takes it ─────────────────────────────────────────────
const bigStaging = path.join(DIR, '_etude')
fs.rmSync(bigStaging, { recursive: true, force: true })
fs.mkdirSync(bigStaging, { recursive: true })
fs.copyFileSync(bigTiff, path.join(bigStaging, 'coupe-axiale.tiff'))
fs.copyFileSync(path.join(DIR, 'sourire-avant-traitement.heif'), path.join(bigStaging, 'photo-visage.heif'))
fs.writeFileSync(path.join(bigStaging, 'lisez-moi.txt'), 'Export CBCT — 1 coupe de démonstration.\r\n', 'utf8')

const bigZip = path.join(DIR, 'etude-cbct-export.zip')
fs.rmSync(bigZip, { force: true })
execFileSync('powershell', [
  '-NoProfile', '-Command',
  // NoCompression keeps it over the 25 Mo line, which is the whole point of this sample.
  `Compress-Archive -Path '${bigStaging}\\*' -DestinationPath '${bigZip}' -CompressionLevel NoCompression`,
])
console.log('big zip:', (fs.statSync(bigZip).size / 1048576).toFixed(1), 'Mo')

fs.rmSync(staging, { recursive: true, force: true })
fs.rmSync(bigStaging, { recursive: true, force: true })
