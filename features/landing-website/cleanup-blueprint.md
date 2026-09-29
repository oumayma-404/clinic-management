# Site clean-up — blueprint (Option A: clean base + calmer layout)

> **Status: implemented 28 Sep 2026.** What shipped and why: `site/BRIEF.md` § « Passe 15 ».
> Deviations from the plan below:
> - « Sans internet » stays in the footer: it links the 14 Sep offline page, which is accurate. Bug #8 was wrong.
> - The « Nouveau » line was not added (unconfirmed claim; « 30 jours » already in the hero).
> - `#telecharger` went light and `#demande` became the one closing blue band (not both).
> - `m-argent` was retired (the dev data inflates every monthly figure); the caisse on 1–4 Sep replaces it.
> - Type scale has 9 small-to-body steps, not 7: the drawn screens needed 11/12/13 px, running text 14/15/16/18.

**Date:** 28 Sep 2026 · **Chosen by owner:** Option A · **Scope:** `site/src/**` only; no copy, claim or
section-order change except where listed.

Measured on <https://apexa.tn> at 1440×900 and 390×844 (screens in the session scratchpad).

## Diagnosis in one line each

| # | Cause | Evidence |
|---|---|---|
| 1 | Every section styles itself (`.s3-*`, `.s4-*`, `.s5-*`…), so nothing matches | `components.css` 7 243 lines: 77 font sizes (7 tokens), 41 radii (3 tokens), 30 shadows (5 tokens), 106 colour literals |
| 2 | Leftovers from older palettes | `rgb(14 27 42 / …)` (old navy `#0e1b2a`) at 16 alphas; `#14b8a6` as `--c-bridge`; warm greys `#cfcfc7`, `#dcdcd4` |
| 3 | Blue ground on 5 of 8 sections, and the drawn UI is blue on that blue | hero, `#argent`, `#mobile`, `#telecharger`, dossier cards |
| 4 | Drawn UI reads as skeleton loaders | caisse chart bars; « Fichiers » card empty > 1.4 s before it fills |
| 5 | Test data in phone captures | `m-cloturer` shows « Dr QA Auditeur »; `m-caisse` shows `dd/mm/yyyy`; badge « 13 » |
| 6 | Dead zones / misalignment | `#avant-avec` empty left column; empty blue strips between `#mobile`→`#faq`→`#telecharger`; hero checklist indented off the button edge |
| 7 | Two equal CTAs everywhere | `nav.html:22-23`, `:56-57`, hero, `#telecharger` |
| 8 | Bugs | « 220,0( » clipped in the dossier total at 390 px; `footer.html:23` links « Sans internet » (banned claim, BRIEF « Claims ») |

## Target

- **Page is white/pale** (`--page`), **blue only twice**: the hero and one closing band (`#telecharger` +
  `#demande` merged visually into one blue « commencer » block). This is the original 21 Aug chassis
  (« white page / dark hero »), which the site drifted from — not a new decision.
- **Drawn product = white app windows** with a thin rule and one soft shadow, like the real app. Blue is
  kept for the window's title bar only.
- **One section template**: eyebrow · h2 · lede on the left (max 36ch), visual on the right at ≥ 64rem;
  stacked below. Same vertical rhythm in every section.
- **One primary action**: « Essayer gratuitement » is `btn-primary` everywhere; « Demander une démo » becomes
  a text link (`link-arrow`) except in `#demande` itself.

## Files to modify

| File | Change |
|---|---|
| `site/src/css/tokens.css` | Add the closed scales (below). Delete `--c-bridge` teal alias; map it to a zone hue. |
| `site/src/css/base.css` | Add shared primitives: `.sec`, `.sec-head`, `.eyebrow`, `.h2`, `.lede`, `.app-win` (+ `__bar`, `__body`), `.chip`, `.stat`, `.caption`. |
| `site/src/css/components.css` | Per section: delete its private head/eyebrow/h2/lede rules and use the primitives; replace every literal size/radius/shadow/colour by a token. Remove every `rgb(14 27 42 …)` and warm-grey literal. Expect roughly −30 % lines. |
| `site/src/pages/index.html` | Section classes → `sec` + modifier; `on-ink` only on hero and closing band; hero checklist moved inside the actions column; `#avant-avec` left column gets the step text at the top (no dead zone); dossier total no-wrap fix. |
| `site/src/partials/nav.html` | Démo → text link; one primary button (desktop + mobile menu). |
| `site/src/partials/footer.html` | Remove « Sans internet » (`:23`). Keep the other links. |
| `site/src/js/site.js` | Scenes: render the filled end state first, animate *from* it (no empty box on entry); keep `prefers-reduced-motion` path. |
| `site/src/img/m-*.png` | Re-capture on clean demo data (no « QA », French dates, no stray badges), straight (no tilt) or tilt ≤ 4°, at 2× for sharpness. Follow [[landing-screenshots-are-viewport-sized]]. |
| `site/src/pages/index.html` (new strip) | « Faits » strip under the hero — 4 true facts only: *Numérotation sans trou · Chèques postdatés par échéance · Sauvegarde toutes les heures, vérifiée · Export CSV quand vous voulez*. Plus one honest line: « Nouveau, et construit avec des cabinets tunisiens » — **only if the owner confirms the second half is true**, else « Nouveau. Essayez-le trente jours ». |

## Files to create

| File | Purpose |
|---|---|
| `site/check-tokens.mjs` | Derived guard (same idea as `check:responsive`): fails on any `font-size`, `border-radius`, `box-shadow` or colour literal in `components.css`/`base.css` that is not a `var(--…)`, and on any banned claim string (« sans internet », « hors ligne », « CNAM », « TVA ») in `src/**/*.html`. Run it from `build.mjs` before writing `dist`. |

## Closed scales (tokens.css)

```css
/* type — 7 steps, nothing else */
--t-xs: .75rem; --t-sm: .875rem; --t-body: 1.0625rem; --t-lg: 1.25rem;
--t-h3: clamp(1.375rem, 1.2rem + .7vw, 1.75rem);
--t-h2: clamp(1.875rem, 1.4rem + 1.9vw, 2.5rem);
--t-h1: clamp(2.25rem, 1.4rem + 3.4vw, 3.75rem);
/* space — 4-based */
--s-1: .25rem; --s-2: .5rem; --s-3: .75rem; --s-4: 1rem; --s-6: 1.5rem; --s-8: 2rem; --s-12: 3rem; --s-16: 4rem;
/* radius — 3 + pill */
--r-sm: 8px; --r-md: 12px; --r-lg: 20px; --r-pill: 999px;
/* shadow — 3 */
--sh-1: 0 1px 2px rgb(9 9 11 / .06);
--sh-2: 0 8px 24px -8px rgb(9 9 11 / .14);
--sh-3: 0 24px 60px -20px rgb(9 9 11 / .28);
```

Colours: keep the existing token set (ink, accent, text, rules, four zones). Nothing new.

Drawn UI inside an `.app-win` may use `--t-xs`/`--t-sm` only — a mock that needs a smaller size is a mock
that is too dense (BRIEF's « shrunk screenshot » failure).

## Order of work

1. `check-tokens.mjs` first, in report mode → the baseline count.
2. Tokens + primitives (`tokens.css`, `base.css`).
3. Migrate section by section, top to bottom; guard count falls each time. Commit per section.
4. Colour grounds (blue → white) + `.app-win` restyle.
5. Nav/CTA, facts strip, footer fix, dossier total fix.
6. Scene start state (`site.js`).
7. Phone re-captures.
8. Guard to fail mode; wire into `build.mjs`.

## Pitfalls

- **Do not re-open owner decisions**: Geist, h weight 400, `#295AA8` ink, `#2FC6E0` accent, no dark mode, no
  sticky scroll-lock pane, carousel does not pause on hover, no `—` connectives, no motion library.
- **Zone hues stay meaningful** — never recolour an odontogram state to fit the new palette.
- **White sections need a visible edge between them** (alternate `--page` / `--paper`), or the page becomes
  one long white sheet.
- **Contrast**: accent `#2FC6E0` on white fails AA for text — use `--accent-on-white` (`#0C6E85`) for links.
- **Hero stays blue** — the hero scene is tuned for the blue ground; only its inner cards get lighter.
- **Build from HEAD** and deploy through `landing-v2/dist` — [[website-work-needs-two-pushes]]. Keep the APK
  and `CNAME`.
- **Anchor offsets**: changing section padding moves the 96 px landing anchors — re-measure after settling
  ([[landing-anchor-measurement-needs-settling]]).

## Verification

- `node check-tokens.mjs` → 0.
- Eye pass at 320 / 390 / 820 / 1180 / 1440 px (+ 1536×730, the owner's laptop): every section, before/after
  pairs, viewport-sized shots.
- Scroll each scene into view and screenshot at 0 ms — nothing empty.
- All 14 nav anchors land at 96 px.
- No horizontal scroll at 320 px; the dossier total is not clipped.
- Live check on apexa.tn after deploy: new copy present, APK still 200.
