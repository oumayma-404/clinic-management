# Originals

Sources that are **not** built. `build.mjs` reads `src/img` only, so nothing here
reaches `dist/`.

- `photo-equipe-uncropped.jpg` — the 1700x2550 portrait behind `src/img/photo-hero.jpg`.
  ⚠️ Kept because the crop cannot be redone from the shipped file. Cover-cropping a
  portrait into a wide hero frames the ceiling, and `object-position` cannot fix it:
  a source with no horizontal overflow has nothing to pan. The hero crop is
  `crop=800:625:900:760`.
