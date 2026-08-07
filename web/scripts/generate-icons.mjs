#!/usr/bin/env node
/**
 * generate-icons — rasterise `branding/icon.svg` into the seven assets `app/layout.tsx` and `app/manifest.ts`
 * declare (AC-1).
 *
 * WHY THIS IS COMMITTED
 * Before this script, all seven declared icons **404'd** — `public/` held only the untouched `create-next-app`
 * SVGs — so an installed app got a blank or generic home-screen tile. Generated binaries in a repo need a
 * reproducible path back to their source or nobody can safely change the logo, which is what this is.
 *
 * ONE AUTHORITY FOR THE MARK
 * The glyph is defined **once**, as the `id="mark"` path in `branding/icon.svg`. This script extracts that `d`
 * and composes each variant's own background and ink around it. Replace the master, re-run, commit — the seven
 * outputs cannot drift from the vector they came from.
 *
 * ⚠️ It is Node + `sharp`, not Python + PIL, and that was forced rather than preferred: **PIL cannot read SVG**,
 * so a PIL script would have to hand-redraw the mark in drawing calls — a second authority for one logo. `sharp`
 * is already installed (Next's own dependency), so this adds nothing to `package.json`.
 *
 * DETERMINISM (plan R-9)
 * Generated binaries must not churn the diff on an unrelated re-run. Each SVG is emitted at its exact target
 * pixel size and rasterised at `density: 72`, so no resampling happens at all — the vector is rendered once, to
 * size. PNG options are pinned explicitly and no metadata is attached (`sharp` writes none unless asked). Two
 * runs on one machine produce byte-identical files; verify with `git diff --stat` after running.
 *
 *   node scripts/generate-icons.mjs
 */

import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import sharp from "sharp";

const WEB_ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");
const MASTER = join(WEB_ROOT, "branding", "icon.svg");
const OUT_DIR = join(WEB_ROOT, "public");

/** The app's own tokens, converted from the oklch values in `app/globals.css`. */
const PRIMARY = "#00736b"; //   --primary            oklch(0.49 0.105 188)
const INK_ON_PRIMARY = "#f8fdfc"; // --primary-foreground oklch(0.99 0.005 190)

/**
 * Extract the glyph from the master.
 *
 * A throw, not a fallback: a silently-empty `d` would rasterise to a blank tile and the run would report success —
 * which is the failure this whole script exists to end.
 */
function readMarkPath() {
  const svg = readFileSync(MASTER, "utf8");
  const match = svg.match(/<path\b[^>]*\bid="mark"[^>]*\bd="([^"]+)"/);
  if (!match) {
    throw new Error(
      `generate-icons: no <path id="mark" d="…"> in ${MASTER}. The script composes every asset from that one ` +
        `path — see the comment at the top of the master.`,
    );
  }
  return match[1];
}

const MARK = readMarkPath();

/**
 * One variant's SVG, sized to its exact output pixels so nothing is resampled.
 *
 * `scale` is the glyph's size relative to the 512-unit master, applied about the centre. It is the only knob the
 * variants disagree on, and each disagreement is a platform rule stated at the call site below.
 */
function variantSvg({ size, background, radius, ink, scale }) {
  const offset = (512 * (1 - scale)) / 2;
  const plate =
    background === null
      ? ""
      : `<rect width="512" height="512" rx="${radius}" fill="${background}"/>`;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="${size}" height="${size}">${plate}<g transform="translate(${offset} ${offset}) scale(${scale})"><path fill="${ink}" d="${MARK}"/></g></svg>`;
}

/**
 * The seven assets, each with the platform rule that fixes its shape.
 *
 * ⚠️ `scale`, `radius` and `flatten` are not styling choices — get one wrong and the tile is cropped, black, or
 * illegible on a device nobody develops on.
 */
const ASSETS = [
  {
    file: "icon-192.png",
    size: 192,
    background: PRIMARY,
    radius: 112,
    ink: INK_ON_PRIMARY,
    scale: 1,
    // The ordinary `purpose: "any"` tile: the app draws its own rounded plate, because "any" means the platform
    // will NOT mask it and a square of flat colour reads as an unfinished placeholder.
  },
  { file: "icon-512.png", size: 512, background: PRIMARY, radius: 112, ink: INK_ON_PRIMARY, scale: 1 },
  {
    file: "icon-maskable-512.png",
    size: 512,
    background: PRIMARY,
    radius: 0,
    ink: INK_ON_PRIMARY,
    scale: 0.78,
    /*
     * ⚠️ Maskable is the one asset with a hard geometric contract, and both halves of it matter.
     *
     *   • `radius: 0` — FULL BLEED. Android applies its own mask (circle, squircle, teardrop…), so a rounded
     *     plate here leaves transparent corners *outside* the mask and the tile gets a visible notch.
     *   • `scale: 0.78` — everything meaningful must sit inside the central circle of 80 % diameter (radius
     *     204.8 of 512), because that is all Android guarantees to keep. The mark's furthest point from centre
     *     is its top-left shoulder at (152, 128), i.e. 165 units, so at 0.78 it clears the safe circle with
     *     room rather than sitting on its edge.
     */
  },
  {
    file: "apple-icon.png",
    size: 180,
    background: PRIMARY,
    radius: 0,
    ink: INK_ON_PRIMARY,
    scale: 0.82,
    flatten: true,
    /*
     * ⚠️ 180 px, full bleed, and **no alpha**. iOS composites an apple-touch-icon over BLACK, so any
     * transparency — including the corners a rounded plate would leave — renders as black wedges around the
     * tile. iOS also rounds the icon itself, so `radius: 0` is right and the glyph is inset instead.
     */
  },
  {
    file: "icon-light-32x32.png",
    size: 32,
    background: null,
    radius: 0,
    ink: PRIMARY,
    scale: 1.18,
    /*
     * The favicons carry the mark ALONE on transparency, and slightly overscanned (`1.18`).
     *
     * A 32 px tab icon has ~26 px of usable glyph once a plate takes its margin, at which point the roots merge
     * into a blob. Dropping the plate and letting the mark bleed a little past the master's own bounds is what
     * keeps it readable at the one size where it is actually looked at.
     *
     * `light` = for a LIGHT browser chrome, so the ink is the primary. `layout.tsx` selects on
     * `prefers-color-scheme`.
     */
  },
  { file: "icon-dark-32x32.png", size: 32, background: null, radius: 0, ink: INK_ON_PRIMARY, scale: 1.18 },
];

/**
 * `/icon.svg` — the vector favicon, and the only asset that must answer both themes in ONE file.
 *
 * A browser picks a single SVG and then renders it under whatever chrome the user has, so the theme switch has to
 * live *inside* the document. `prefers-color-scheme` in an embedded stylesheet is the only mechanism for that;
 * the light ink is the default so a renderer that ignores the media query still shows something legible.
 */
function themedFaviconSvg() {
  const offset = (512 * (1 - 1.18)) / 2;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="32" height="32">
  <style>
    .mark { fill: ${PRIMARY}; }
    @media (prefers-color-scheme: dark) { .mark { fill: ${INK_ON_PRIMARY}; } }
  </style>
  <g transform="translate(${offset} ${offset}) scale(1.18)"><path class="mark" d="${MARK}"/></g>
</svg>
`;
}

for (const asset of ASSETS) {
  let pipeline = sharp(Buffer.from(variantSvg(asset)), { density: 72 });
  // `flatten` before the PNG encode, so the alpha channel is gone from the file rather than merely opaque.
  if (asset.flatten) pipeline = pipeline.flatten({ background: asset.background });
  const buffer = await pipeline
    .png({ compressionLevel: 9, adaptiveFiltering: false, palette: false })
    .toBuffer();
  writeFileSync(join(OUT_DIR, asset.file), buffer);
  console.log(`  ✓ ${asset.file.padEnd(26)} ${asset.size}×${asset.size}`);
}

writeFileSync(join(OUT_DIR, "icon.svg"), themedFaviconSvg(), "utf8");
console.log(`  ✓ ${"icon.svg".padEnd(26)} vector, theme-aware`);
