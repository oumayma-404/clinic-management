# site-seo-pages — shipped notes

What this pass changed on apexa.tn, and the decisions that are easy to undo by accident.
`blueprint.md` is what was planned (2026-10-18); this is what was built.

## What shipped (local, not deployed — needs the owner's OK)

- **Three long-tail pages, told in IMAGES**: `application-web-cabinet-dentaire`,
  `agenda-rendez-vous-cabinet-dentaire`, `dossier-patient-dentaire`, each with
  its `src/jsonld/` `WebPage` + `BreadcrumbList` (`primaryImageOfPage` = the page's laptop capture).
  ⚠️ **A first version was ~550 words of `.cp-*` prose per page, and the owner refused it** (« trop de
  texte, les gens préfèrent voir », 2026-10-18). Each page is now ~90 words: a laptop screen of the real
  app with a phone on its corner, then three close-ups with one line each. The text that stays is what
  Google reads — h1, one sentence, one title + sentence per close-up, and every image's `alt`. Its
  vocabulary is `.vp-*` (components.css § 9), the one place this site's « no page invents CSS » rule
  gave way. Each page's header comment lists its target queries and the claims it must not make.
- ⚠️ **No caisse page.** One was built and the owner refused it: « je ne veux pas de pages dédiées [à
  l'argent], ne les mettons pas en avant pour l'instant » (2026-10-18). It is deleted with its JSON-LD and
  images; no tile, title or link names it. Money visible inside another page's capture is tolerated.
- ⚠️ **The homepage is NOT touched, and neither is the footer** (it renders on the homepage). A first pass
  linked the overview cells, added a « Navigateur » install row, social links and FAQ wording; all of it
  was reverted to `HEAD` at the owner's word (« do not modify the main page, I like it as it is »). The new
  pages link to each other and are linked from `odontogramme.html` and `logiciel-dentaire-hors-ligne.html`.
- **Brand entity, invisible**: `Organization` and `WebSite` in `src/jsonld/index.json` carry
  `alternateName: ["Apexa Tunisie", "apexa.tn"]` (Google's site-name feature reads `WebSite`) and
  `Organization.sameAs` the Instagram and Facebook pages. The homepage FAQ markup is back to the page's
  exact words.
- **Formula-honest claims** on the pages this pass owns: guide Q3 (hourly backup = local install) and Q8
  (« l'iPhone par le navigateur »); the homepage's own FAQ 01 still says hourly, by the owner's choice.

## Les captures

`site/capture-screens.mjs` takes them and crops them, `capture-phones.mjs`'s twin: same QA account,
read-only, fixtures filtered from the API JSON in the browser, 1440x900 @2x (and 390x844 for `p-*`), then an
ffmpeg crop. What the browser shows is deliberately NOT the database, and nothing is written back:
- ⚠️ **The patients are the cast of « Friends »** (`CAST`): the owner refused real-looking names on the site.
  Renamed in every API answer, by `firstName`/`lastName` pair and inside every string.
- ⚠️ **The agenda week of 12 October 2026 is GENERATED** (`WEEK`): the dev clinic holds two visits a day and
  the owner found the agenda « sparse ». ~9 séances a day from the real act catalogue (ids, names, colours),
  status from a fixed clock, `isSyncedToGoogle: true` so a Google-less clinic paints no orange chip.
- **`odontogramme.html` became an image page too** (owner, 2026-10-19): the « Symboles » chart on top, then a
  tooth's editor, the « Cases » view (cropped tight: the chart sits centred in a wide white box) and the mixed
  dentition. The dossier page's top image is the « Actes faits » tab, so the two pages never share a hero.
- ⚠️ **The odontogram is shot in « Symboles », never « Cases »** (owner, 2026-10-18: the teeth drawn,
  diagnostics red, work done blue). On the phone, where one arch shows at a time, it is the LOWER arch —
  that is where this chart's crown, root canal and extraction are — scrolled to the card's title and no
  further, or the treatment card below brings a « Reste à payer » into frame.
- ⚠️ `p-*` phone shots, never `m-*`: `m-*` belong to the homepage. The phone WEEK view was rejected (names
  break letter by letter at that width), and the phone « À clôturer » too (its tab reads « À payer 421 »).
⚠️ **The app's left menu is always cropped off**: it shows « Rappels », not in service yet.
⚠️ **Two screens are deliberately not taken**: the dashboard counts the fixtures (« 697 séances à
clôturer », « 283 devis en attente ») and « Chèques à encaisser » reads « En retard » on every row
whatever the browser's clock says, because that badge is computed server-side.
⚠️ A full-page screenshot of these pages shows the phones BLACK and the tiles EMPTY — `loading="lazy"`
images below the fold never load when nothing scrolls. It is the probe, not the page: scroll first.

## Two derived guards — the part that must not be undone

- **`check-tokens.mjs` § Claims finds its files.** It used to read `index.html` for CNAM / TVA /
  « sans internet », and a hand list of six files for reminders — so any new page was checked by
  nothing. It now walks `pages/`, `jsonld/` and every scene a page **embeds**, and tests the readable
  text (title, description, body minus tags, JSON-LD strings minus URLs), never the markup: the href
  `logiciel-dentaire-hors-ligne.html` is not a claim. Exemptions are per rule, per file, with a reason.
  ⚠️ Run on HEAD it found three: the guide's « hors ligne » (a question to ask, answered « en formule
  locale » → exempted) and « un rappel de contrôle part tout seul » in `hero-journee-se-remplit.html`
  (kept unused on purpose → excluded because no page embeds it, and checked the day one does).
- **`build.mjs` step 7 fails on a dead internal link**, on the OUTPUT: every `href`/`src`, every
  `content=` on the site's own origin, every JSON-LD string on it; a `#fragment` must name an `id`
  (a JSON-LD `@id` fragment is an identifier, so there only the file is checked). Allow-list:
  `APEXA-1.1.1.apk`, which lives only in the deploy repo. `--no-images` skips `assets/img/` targets,
  so a planted missing image is only caught by a full build — measured.

## Deviations from the blueprint

- **The install section is three columns and a ROW, not four columns.** At four, « Télécharger l'APK »
  wrapped onto two lines up to 1440 px. « Navigateur » is `.dl-block--web`, full width under the
  three, its rule across like the stacked layout.
- **No fifth FAQ row**: each row carries a drawn figure; FAQ 04's answer gained the browser instead.
- **The guide's Q4 list was NOT malformed** — a truncated `sed` range showed a `</p>` that belongs
  to the next element. Nothing changed there.

## Left as found, and why

- The homepage's dossier illustration shows **seven** tabs including « Notes »; the product has six
  (`web/app/patients/[id]/page.tsx`). It is an animated drawing with per-tab coordinates — a design
  change for the owner, not an SEO edit. The dossier page states the six.
- The guide's title is 61 characters and the odontogramme description 165 — one and five over
  Google's display length; truncation, not an error.
