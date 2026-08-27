# Génériques d'accueil — animated hero scenes

Three animated scenes were built for the hero. Each is a **complete standalone HTML
document**: its own `<style>`, no JavaScript at all, no external request. They loop
seamlessly and resolve to a composed still under `prefers-reduced-motion`.

| file | name | loop | live? |
|---|---|---|---|
| `hero-quatre-temps.html` | Quatre temps — four acts with a progress bar, each disassembling into the next | 14s | **yes, in the hero** |
| `hero-une-seule-saisie.html` | Une seule saisie — a tooth mark travels and assembles fiche, note, caisse, rappel | 14s | kept, not live |
| `hero-journee-se-remplit.html` | La journée se remplit — the week agenda fills and takes its zone colours | 15.5s | kept, not live |

The owner picked **quatre temps** and asked that **une seule saisie** be kept, because it
may be wanted later. `journée se remplit` is kept too rather than deleted: it costs a few
KB and rebuilding one of these is not cheap.

## How the live one reaches the page

`build.mjs` copies every `.html` here to `dist/assets/scenes/`, and the hero embeds the
chosen one in an `<iframe>`.

⚠️ **An iframe, deliberately, not inlined markup.** Inlining would mean prefixing every
selector and renaming all 40 `@keyframes` to avoid colliding with the site's own
(`shine`, `marquee`, `draw-line`, `bar-drop`, `chip-in`…). Selector rewriting is
error-prone and a collision here would look like a broken animation rather than a merge
fault. The frame guarantees the scene renders exactly as it was approved. It is
`aria-hidden` with `tabindex="-1"`, and the hero's real content carries the meaning, so
nothing is lost to assistive technology.

⚠️ `preview.mjs` folds the whole site into one file for review, so it has to turn that
`src=` into a `srcdoc=` — a relative path has nothing to resolve against inside a single
published document.

## Swapping which one is live

Change the `src` in the hero's iframe in `src/pages/index.html`, and the caption in the
adjacent visually-hidden `<p>`. Nothing else refers to them.
