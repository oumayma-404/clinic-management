# The site, rebuilt — brief and running record

**Started:** 21 Aug 2026, after the first site was shown to people and rejected outright.
**Replaces:** `landing-v2/` (v3 → v6, live at <https://oumayma-404.github.io/gestion-clinique-site/>).
`landing-v2/` stays on disk until this ships — it is still what is served.

## Why the first one failed

Measured against CareStack, the site the owner picked as the reference, on three counts:

1. **Dark, quiet and literary.** Near-black ground, muted slate-blue, a serif *italic* display face
   and monospace chips. Reads as a developer's portfolio, not as software a dentist trusts with the
   day's takings.
2. **The hero was a raw screenshot, shrunk.** A whole 1440-wide window scaled into a hero column
   renders its labels at ~8 px. It lands as a dense grey mess — the "amateur" complaint, exactly.
3. **It explained instead of selling.** Prose paragraphs, no scannable hierarchy, no motion, no
   depth, large flat voids.

## What is taken from CareStack, and the one thing that cannot be

**Taken:** white page / dark hero / warm-grey card rhythm · giant rounded cards whose internal
hairline grid is a 1 px gap showing the ground through · the sticky left rail with a swapping right
panel · small uppercase accent eyebrows over a LARGE, LIGHT display face (weight 500, never 700) ·
one saturated accent reserved for actions · composited scenes with floating UI chips instead of
bare screenshots.

**Not taken:** about 40 % of their page is third-party proof — Forrester, six G2 badges, 3 000
practices, video testimonials, 34 client logos. There is none of that here and inventing it is out.
So the *structure* cannot be copied, only the design language. Four things carry credibility instead:

- the product working, shown as a legible fragment and (later) a film;
- specificity a foreign product cannot claim — chèques postdatés, no internet, French, Tunisia;
- risk reversal said loudly — 30 days, no card, CSV export, hourly backup;
- plain transparency about being new.

## Decisions taken by the owner, 21 Aug

| Question | Answer |
|---|---|
| How close to CareStack's identity | **Our colours, their chassis.** Ink-navy `#0e1b2a` hero, warm-grey `#f4f4f0` cards, clinical teal `#14b8a6` accent, the app's four zone hues kept meaningful. |
| The marketing film | **Page animation now, film after.** Ship the animated page first; build the motion-graphics film as a second pass and drop it into the hero slot. |
| Scope | **Full multi-page site**, CareStack-style: 10 pages, real dropdown nav. |

⚠️ The owner was told that thin pages look worse than no pages and chose the multi-page site anyway.
Every page therefore has to be substantive or it works against the site.

## ⚠️ Claims this site may not make

Struck from the old site after verification, and not to be reintroduced:

- **CNAM** — the owner is not certain the feature ships. Also: « Nomenclature CNAM » is legible in
  the app's own left rail in every desktop capture, which is one more reason every crop starts at
  `--cx: .185`, to the right of the rail.
- **TVA** and **timbre fiscal** — the columns still exist (`VatApplicable`, `VatRate`,
  `StampDutyAmount`) and old test invoices carry values, but `clinic-settings.tsx` says on screen
  « Aucune TVA ni timbre fiscal n'est ajouté ». *A schema that still has the column is not a product
  that still has the feature.*
- Any client count, testimonial, award or logo.

True and usable: numérotation sans trou par année · le prix de l'acte est ce que le patient paie ·
chèques postdatés par échéance · dinars, +216, gouvernorats · sauvegarde automatique, vérifiée,
restaurable · Windows, Android, iPhone · import/export CSV avec essai à blanc · 24 écrans en 4 zones ·
30 jours sans carte.

⚠️ **« fonctionne sans internet » was on that list until 23 Aug, and it must never go back on it.**
The claim was that the software sits on one PC in the cabinet and the others reach it over the LAN,
so a dropped line stops nothing. That describes `SelfHostedLan` — one of three deployment kinds —
and the site sells the hosted one. It had reached **twelve** places, including the meta description
Google prints and WhatsApp shows, the footer strapline, FAQ 02, a whole drawn figure (« et si la
connexion tombe ? ») and both copies of the nav. All twelve are gone. The backup claims beside it
are real and stay. When a capability is true of one `DeploymentProfile` only, it is not a fact about
the product — it is a fact about an install.

## Pass 2 — measured, not eyeballed (21 Aug)

The first pass took CareStack's *idea* and produced something that did not look like it. The owner
said so. This pass reads the numbers off the live site instead of guessing at them:

| | CareStack, measured | Ours |
|---|---|---|
| Display face | **Geist Sans** | Geist |
| Section headline | 36 px / 40 px, **weight 400** | same |
| Closing CTA headline | 60 px / 60 px, weight 600 | same |
| Body | 18 px / 28.8 px | same |
| Eyebrow | 13 px, caps, tracked, **muted grey — not the accent** | same |
| Container | 1272 px, padding-block 96 px | same |
| Page / card / dark / rule | `#ffffff` `#f3f3f0` `#152d18` `#e4e4e3` | `#ffffff` `#f3f3f0` `#0e1b2a` `#e4e4e3` |
| Text / muted | `#09090b` / `#52525b` | same |
| Accent | `#c8f46e` lime | `#2dd4bf` teal |

Weight 400 on the headings is the single biggest one. Setting them at 500–600 is what made the first
pass read as a template.

### The screenshot treatment, which is the other half of it

The reference **never frames a whole screenshot**. It builds a panel — warm grey, a colour wash in
the top-right corner, a line icon in a bordered square top-left, a large title bottom-left — and
lets one screenshot **bleed off the panel's right and bottom edges**, with two or three legible UI
chips floating over its left edge. `.stage` is that panel and it is used in the hero, in all four
feature cards and in the reliability pair.

⚠️ **A bleeding shot only works if the app inside it is readable.** `--cz` sets how many frame-widths
the source is scaled to; `--cz` ≈ 2.3–2.6 renders the app near 1:1 in a desktop panel. A phone's
figure is 342 px wide, where the same value renders the app at half size and nothing can be read —
so `.crop` reads `--czz` (defaulting to `--cz`) and the phone media query raises it to 4. A rule
cannot override `--cz` directly because it is set inline per shot.

### Section order, mirroring the reference

hero · à qui ça s'adresse (numbered accordion + dark card) · dark band question · fonctionnalités
(rail + stage + black links) · écrit pour la Tunisie (tile grid) · fiabilité (two stages) · full-bleed
CTA · vos données · footer.

Where the reference puts third-party proof we cannot match — Forrester, G2, 3 000 practices, video
testimonials, a logo marquee — the slot carries something true instead: the four-step loop card sits
where the testimonial goes, and « vos données restent les vôtres » sits where the certification
badges go. No slot was left empty and none was filled with a claim.

## Pass 3 — the motion (24 Aug)

The reference loads **no** motion library — no Lenis, no GSAP. Its polish comes from a small
vocabulary of CSS, read straight off its stylesheet:

| Their keyframe | What it does | Where ours uses it |
|---|---|---|
| `shine` | skewed highlight sweeping a button | the primary CTA, one 5s loop, the page's only continuous flourish |
| `bounceAlpha` | arrow exits right, returns from left | every `.link-arrow` and every row of the black links panel |
| `drawline` | `width: 0 → 100%` | the marker under the open accordion item |
| `sticky` | header re-enters from `top: -200px` | `.topbar.stuck` drops in instead of blinking solid |
| `slide` | `translateX(0 → -100%)` | the marquee of the twenty-four screens |
| — | `scroll-behavior: smooth` on `html` | same |

### Rules the motion is held to

- **Strong custom curves.** `--ease-out: cubic-bezier(.23,1,.32,1)`, `--ease-in-out:
  cubic-bezier(.77,0,.175,1)`. The built-in easings are too weak to read as deliberate, and
  **`ease-in` is never used on UI** — it delays the first frame, which is the frame the eye is on.
- **Durations:** press 140ms · hover 180ms · dropdown/panel 260ms. Nothing interactive over 300ms.
  Only the reveals (680ms) and the marquee (46s) run longer, and both are decorative.
- **Named properties only.** `transition: all` would animate layout properties and drop the whole
  thing off the compositor.
- **Every hover is behind `@media (hover: hover) and (pointer: fine)`** — a touch device fires
  `:hover` on tap and the state sticks, which reads as a bug.
- **`:active { transform: scale(0.97) }`** on every pressable thing.
- **Nothing enters from `scale(0)`.** The dropdown starts at `0.97` and is origin-aware
  (`transform-origin: top left`), so it grows out of its trigger rather than blooming from its own
  centre.
- **Blur masks the crossfades.** The reveal carries `filter: blur(6px) → none`; the zone panels
  cross-fade through `blur(7px)`. Without it you see two objects overlapping instead of one
  changing. Kept under 20px — heavy blur is expensive in Safari.
- **`will-change` is released** on `transitionend`. Left on, every revealed block keeps a
  compositor layer for the whole session.

### Three things the motion fixed, not just decorated

- **The zone panels no longer jump.** Above 62rem all four share one grid cell, so switching is a
  crossfade and the page below never reflows. `hidden` is not used there — an outgoing panel has to
  stay in the box long enough to fade.
- **The rail marker travels.** It is one 2px element positioned by `--i`/`--n`, not a border that
  blinks from item to item.
- **Images are uncovered, not faded.** `clip-path: inset(0 0 100% 0) → inset(0)`, so a screenshot is
  never shown at partial opacity.

### Two sections added, both carrying content rather than filling space

- **The marquee** runs the twenty-four screen names past, coloured by zone. It occupies the slot the
  reference gives a client-logo carousel — the one honest thing that fits a strip of that shape. The
  track holds the list twice and travels exactly `-50%`, so the seam lands on an identical frame.
- **« Ce qu'on nous demande avant de commencer »** — six real objections answered, including
  « le logiciel existe depuis quand ? » answered plainly. It does the reassurance work the
  reference gets from customer stories.

⚠️ Both accordions are **open by default in CSS** and closed by the script, so the answers are
readable with JS blocked.

### Verified (Chrome, measured)

0 horizontal overflow at 320 / 390 / 820 / 1180 / 1440 · 0 page errors · in a real coarse-pointer
context every tap target ≥ 44px · under `prefers-reduced-motion` nothing is left hidden
(0 un-revealed blocks, clip resolves to `inset(0)`, the shine is `display: none`).

## Pass 4 — the French, and photography (24 Aug)

The owner's words: *« it feels like english translated to french, not what french sounds like
genuinely »*. That was right, and it is the **second** time this site has been told so — see
§9's copy rule, which caught the same thing in v6 and then let it back in.

### The tells, and what French does instead

| Anglicism | Why it reads as English | French |
|---|---|---|
| `—` as the default connective | English apposition habit | `:` and commas, or a relative clause |
| « Tout ce que fait un cabinet, déjà dedans » | calque of “already in there” | « Tout votre cabinet, dans un seul logiciel » |
| « Le prix de l'acte est ce que le patient paie » | calque of “is what X pays” | « Le patient paie le prix de l'acte » |
| « Vos données restent les vôtres » | calque of “stays yours” | « Vos données vous appartiennent » |
| « Voyez le logiciel en action » | calque of “See X in action” | « Essayez gratuitement pendant trente jours » |
| « Six choses qu'un logiciel importé ne fait pas » | listicle calque | « Pensé pour la façon dont on travaille ici » |
| « Hors ligne & sécurité » | `&` joining two nouns | « Hors ligne et sécurité » |
| Title Case headings | English convention | sentence case, always |

### The register, taken from two live French sites rather than from my ear

- **Julie Solutions** (French dental software) for the domain voice: infinitive headings
  (« Optimiser votre temps et votre rentabilité »), the relative-clause tagline (« le logiciel
  métier **qui** coordonne et fluidifie votre activité »), « à vos côtés », « à votre écoute »,
  and the CTA as an infinitive: « Découvrir le logiciel », not « Voyez le logiciel ».
- **Qonto** for the punch: « tout-en-un », « sans effort », « au quotidien », imperative openers
  (« Gérez… », « Pilotez… »), and the « Vos X, [adj] et [participe] » construction.

66 strings were rewritten. The only `—` left on the page is in the `<title>`, where it is an SEO
separator and not prose.

### Photography

Four photographs, Unsplash licence, composited rather than dropped in: a `.photo-card` puts every
image under a scrim in the brand's own ink, so white type holds whatever the picture does and all
the photographs read as one family instead of as stock.

⚠️ **Two traps, both of which cost a pass:**

- **A `::before` scrim and an `<img>` child at the same z-index paint in DOM order**, so the scrim
  landed *underneath* the photo and did nothing. The scrim needs `z-index: 1` against the image's `0`.
- **`photo-equipe.jpg` is 1700×2550 — a portrait.** Cover-cropping it into a wide hero panel framed
  the ceiling, and no amount of `object-position` fixed it, because a source with no horizontal
  overflow has nothing to pan. The thumbnail I judged the framing from was itself an Unsplash
  `fit=crop`, so it lied about where the subject sat. Fixed by cropping the source with **ffmpeg**
  (`crop=800:625:900:760` → `photo-hero.jpg`) so the framing is deterministic and CSS only has to
  place it.

⚠️ These are stock images of a non-Tunisian practice. Before any paid traffic, either confirm the
licence terms are acceptable or replace them with photographs of a real cabinet — the owner asked
for photography, not specifically for stock.

## Pass 6 — the page argues, and every section carries a visual (24 Aug)

Two verdicts drove this pass. *"as a user, i do not get the feeling i should purchase"* —
the page listed what the software contains and never said what going without it costs. And
*"u just had one photo in there … the rest of the sections are still veryyyy uninviting"* —
photography had been sourced and then used in exactly one place.

### The selling architecture

The owner's own account of the pain became section 2, « avant / avec »: two columns, six
moments, read across. Paper files that scatter while the patient waits, the patient who
telephones on a Sunday and you are running on memory, the paper odontogramme nobody can read
after three years, a month's takings that is only ever an estimate, the secretary ringing
round for tomorrow. Section 8, « l'argent », carries the sharpest one: hand entry never gets
finished, so the figure ends up a guess.

⚠️ **Numbers are structural facts only**, by the owner's explicit instruction: 1 saisie
instead of 4 écritures, 0 recettes to enter, 4 payment modes tracked. No modelled time
savings anywhere on the page.

### Six sections rebuilt, in parallel, then reconciled

A workflow built them (one agent per section) and a seventh judged them together. What the
judge caught is the argument for having it: **two closing CTAs** (two blocks each shipped
one), **two backup timelines that disagreed** on when the last backup ran, the phone deck
**slicing dinar figures mid-number**, a chip claiming 1 610,000 DT encaissé against the
19 280,000 DT the money section states, six sizes for one card-title role, and `.fq-card`
painting `var(--ink)` directly so `base.css`'s `.on-ink :where(a,button):focus-visible`
never fired and the focus ring sat at ~3.6:1.

### ⚠️ Three traps, each of which cost a pass

- **JavaScript inside a CSS file ends CSS parsing.** A builder was asked to return its JS in
  the `css` field after a marker; the merge concatenated the lot into `components.css`, and
  `})();` silently killed every rule after it. The constat band rendered with zero padding
  and stacked in one column, and the page was 3 000 px taller than it should have been. The
  stylesheet did not error — it just stopped. **Grep merged CSS for `})();` and `=>`.**
- **A `::before` scrim and an `<img>` child at the same z-index paint in DOM order.** The
  scrim ends up under the photograph. Give it `z-index: 1` against the image's `0`.
- **An Unsplash search thumbnail is a crop and lies about the framing.** `photo-equipe.jpg`
  is 1700×2550, a portrait; cover-cropped into a wide panel it framed the ceiling, and
  `object-position` could not fix it because a source with no horizontal overflow has nothing
  to pan. Always `ffprobe` the download, and crop with ffmpeg rather than with CSS.

### Verified (Chrome, measured)

0 horizontal overflow at 320 / 390 / 820 / 1180 / 1440 · 0 broken images · 0 page errors ·
every tap target ≥ 44 px in a real coarse-pointer context · nothing hidden under
`prefers-reduced-motion` · exactly one `.cta-band` · the profile switcher, the FAQ accordion
and the zone crossfade all confirmed by reading state, not by eye.

### Still owed

The **price and the billing model**. The nav links to a `tarifs.html` that does not exist,
and the owner has said the price should be published.

## Pass 7 — section 2 stops being twelve paragraphs, and starts showing the product (24 Aug)

Three verdicts, one section, two attempts.

*« the french sounds weird, it's not french, it's french translated from english »* — the **third**
time this site has been told so. *« the before after section is important, now it's just writing,
no one will read those blocks »*. And then, on the first attempt: *« it's not selling tbh, that's
the most critical selling point … no cool animations, no images, nothing »*.

### The French

| Was | Why it read as English | Ships |
|---|---|---|
| « …ils vous **coûtent vos journées** » | calque of *they cost you your days* | « …**la journée y passe** » — `y passer` is the idiom |
| « Six moments que tout cabinet **reconnaît** » | English relative-clause habit | « Six situations que vous avez **déjà vécues** » |
| « Les six, réglés **par la façon dont le logiciel est fait** » | calque of *by the way it's built* | « Les six, réglées **d'elles-mêmes** » |
| « …gardent **sous les yeux** ce qui n'est pas fini » | — | « …vous **montre en permanence** ce qui n'est pas terminé » |

The owner's twelve body sentences were **not** the problem — « Vous cherchez, il attend. » and
« Le mois, à peu près » are native French, and they are all kept. The *headline* was, and a
headline is the only line most readers get to. « en dix secondes » was cut: a modelled time
saving, which the owner banned.

### The attempt that failed, and why it is recorded

The first rebuild was a **ledger**: two cards collapsed into six paired lines, the paper moment
struck through, ~85 words instead of ~250, one scroll cascade. It was economical, it was fast, it
degraded perfectly — and it did not sell. ⚠️ **Economy is not the goal in the page's selling
section.** Cutting words is the right instinct in a *supporting* section and the wrong one here:
what this block owes the reader is not brevity, it is the product on screen. A section made
entirely of type cannot carry the one argument the page exists to make.

### « Le quotidien »

Six moments, **one at a time**, each with a real screen of the app beside it. The struck paper
moment and the owner's own sentence on the left; the software's answer under a rule below it; the
screen that settles it on the right, with a floating chip naming what is on it. Scroll advances
the block — no rail, no tabs, no auto-play.

⚠️ **The interaction model is the whole reason this is not the page's third list-swaps-a-panel.**
§3 switches on a click and §5 switches on a tab; a third click-driven switcher at §2 is what makes
a site read as a template. Scroll-driven has no control to find, and — unlike an auto-playing
carousel — it does not compete with the hero above it.

| # | Aujourd'hui | Avec Gestion Clinique | The screen |
|---|---|---|---|
| 01 | La fiche introuvable | Le dossier s'ouvre au nom du patient | `patients` |
| 02 | Le dimanche, de mémoire | Le dossier vous suit sur le téléphone | `m-odonto`, in a phone frame |
| 03 | L'odontogramme sur une feuille | Un odontogramme toujours lisible | `odonto`, framed on the tooth chart |
| 04 | Le mois, à peu près | Les recettes se comptent seules | `caisse` |
| 05 | Les rappels, un par un | Les rappels partent tout seuls | `rappels`, framed on the sent table |
| 06 | La séance jamais facturée | Rien ne se termine à moitié | `m-cloturer`, in a phone frame |

« Le dimanche, de mémoire » and « Le mois, à peu près » now rhyme structurally — two elliptical
noun phrases, a French rhetorical habit English does not share.

### ⚠️ The screenshot is content, and two of the six captures argued against the copy

Framing is not a styling decision here, it is an editing one, and it was got wrong twice:

- **`rappels.png` at the standard `--cy: .085` opens on « Envoyés **0** aujourd'hui », « En attente
  **0** », « Bloqués 3 », « Échecs 2 » and a « 1 rappel est en attente de forfait » banner** — a
  section claiming reminders go out by themselves, illustrated by a screen saying none did.
  Reframed to `--cy: .40 --cz: 2.05`, onto the WhatsApp forfait card (« Prêt à envoyer · 412
  restants sur 1000 ») and the table of reminders actually sent, by patient, channel and date.
- **`odonto.png` at the standard frame shows the patient's NAME HEADER, not the tooth chart** —
  « Un odontogramme toujours lisible » over a block of text. Reframed to
  `--cx: .19 --cy: .46 --cz: 2.05`, onto the chart, its colour legend and the Diagnostics tabs.

**Always look at the capture, not just at the file name.** The rest of the page reuses one frame
(`--cx: .185 --cy: .085 --cz: 2.6`) across four screens because they happen to open well; that is
a coincidence, not a default.

### Traps this pass cost a pass on

- **A sticky pane needs a trailing spacer the height of the pane itself.** Without it the pane
  reaches the bottom of its containing block and scrolls away while the LAST moment is still being
  read — moment 06 slid up the screen as the reader arrived at it. `--jour-h` is a variable so the
  pane's height and the spacer can never drift apart.
- **An absolutely-positioned strike breaks on a wrapped title.** One box over a two-line inline
  draws a single rectangle across both lines, which renders as two underlines. The strike is a
  `linear-gradient` background with `box-decoration-break: clone` instead: one box per line
  fragment, and `background-size` animates, so the draw survives the wrap.
- **~~A shot must BLEED, not be framed.~~ → REVERSED in pass 8.** This pass cropped into each
  screen so the app's type stayed near 1:1 and let the fragment run off two edges. Read back by
  the owner: *« the laptop screenshots are cropped in a weird way, take the shots that are not
  cropped »*. They were right — see pass 8.
- **A 390×844 phone capture scaled to fit a 16/10.6 panel renders at half size.** It bleeds off the
  bottom of the panel at `min(72%, 19.5rem)` instead, which is the only way the app on it is read.
- **`.chip`'s entrance is keyed on an ancestor `.in`.** This section does not use `.rise`, and the
  chip has to land when its moment does, not when the block is revealed — same keyframes, our own
  trigger on `[data-active="true"]`.
- **⚠️ Four screenshots are shared with §5 and §8, and a `str.replace` over the whole page hits
  them too.** One crop nudge silently changed §5's Finances panel before it was caught. Edit
  section 2's crops **by line number**, or assert the match count first.

### The contract the fallback keeps

⚠️ The **default** is the readable stack: six cards in normal flow, copy over shot.
`data-mode="scroll"` is stamped by the script only where the sticky version can work — ≥62rem,
`IntersectionObserver` present, motion allowed. Below that, under `prefers-reduced-motion` and with
the script blocked, the six cards stand one under the other and every moment is read in full. This
is the same contract the two accordions keep by defaulting to open: nothing is ever behind a
control that is not rendered.

⚠️ **It is a long section**: ~3 400px on a desktop, ~4 250px stacked at 390px. That is deliberate —
six screens of the product is what was asked for — but it is the one thing to revisit if the page
starts to feel heavy. Dropping moments on mobile only is not the answer; that hides content by
width.

### Verified (Chrome, measured — not by eye)

0 horizontal overflow at 320 / 390 / 768 / 820 / 1024 / 1180 / 1440 · 0 page errors · 0 broken
images · all six moments advance in order with the rail filling 1→6 · the pane stays pinned
(`top: 88`) through the last stop · stack mode below 62rem shows all six items, scroll mode shows
exactly one · under `prefers-reduced-motion` the block is stacked, everything visible, every strike
drawn, and **0** `.rise` blocks left hidden anywhere on the page · with `.no-js` every item, chip
and strike resolves to its end state and the sentinels collapse to `display: none`.

## Pass 8 — whole screens in a frame, and the motion in layers (24 Aug)

*« muuuch better now, but the ui could be enhanced a bit, to feel more fluid and professional; the
most important thing: the phone screenshots are great, but the laptop screenshots are cropped in a
weird way, take the shots that are not cropped »*.

### The bleed rule was wrong, and the phone said so all along

Pass 7 cropped into every desktop capture so the app's type stayed near 1:1, then let the fragment
run off the panel's right and bottom edges. The reasoning was sound and the result was not: the
cuts landed mid-word and mid-column, and four panels in a row read as screenshots somebody had
trimmed badly. **The phone was the tell.** It was the only shot shown whole — in a frame — and it
was the only one nobody complained about.

So the desktop shots get the same treatment: **a complete screen inside a CSS-drawn window frame**
(`.jour-win`, a title strip with three dots over the capture). Smaller, but coherent — and the
frame is what makes the scale read as deliberate rather than as a shrunken picture. The captures
are 2×, so at the ~58% this renders they still land near one device pixel per CSS pixel.

⚠️ **A whole screen is not always affordable, so the frame has a viewport (`.jour-view`) and three
tiers.** Below 48rem the frame shows a slice at 360%; between 48 and 72rem, 220%; from 72rem up,
the capture's own 16/9 and the whole screen. A complete desktop screen at 390px renders the app at
23%, which is a texture and not a screenshot — the tier is the honest answer, not a compromise.

⚠️ **The offsets are a percentage of the VIEW's width, never the image's** — a percentage margin
always resolves against the containing block's width, `margin-top` included. Skipping the app's own
left rail, 18.5% of the capture, therefore costs `0.185 × zoom`: **-66% at 360%, -41% at 220%**.
Getting that wrong opens the slice halfway through the navigation, which is the exact fault this
pass was fixing.

### Three widths, three layouts, and the two breakpoints are deliberately different

| width | layout | frame | app renders at |
|---|---|---|---|
| < 48rem | stacked, one column | slice 360% | 0.56–0.72× |
| 48–62rem | stacked, two columns, **17rem copy** | slice 220% | 0.50–0.80× |
| 62–72rem | sticky stage | slice 220% | 0.80× |
| ≥ 72rem | sticky stage | **whole screen** | 0.45–0.58× |

- **62rem is the sticky breakpoint; 72rem is the whole-screen one.** They are not the same number
  and must not be collapsed into one: 72rem is where a complete screen still renders the app near
  half size, and between the two the stage is sticky but the frame shows a slice.
- ⚠️ **The stacked card keeps two columns, with a 17rem copy — not one column.** Stacking to one
  column makes the shot big and makes the section **6 500px tall on a 1024px laptop**, seven
  screens for section 2. Measured, not guessed.
- **The stage runs out past the container** towards the viewport edge, stopping one gutter short:
  `margin-right: max(-5rem, min(0px, calc(50% - 50vw + var(--gutter))))`. `50% - 50vw` *is* that
  distance; the gutter added back keeps the panel off the edge and clears the scrollbar that `vw`
  includes. The -5rem cap exists so a wide screen cannot make the panel taller than the pane it has
  to sit inside. Net: the frame went 810px → 928px at 1440.
- **The phone's width is a fraction of the panel, capped** (`min(26rem, 78%)`, 62% from 62rem up).
  A fixed cap alone is too small on the wide sticky panel; a fraction alone is too small on the
  narrow stacked one.
- ⚠️ **`align-content: start` on the phone panel, never `end`.** Anchored to the bottom the phone
  loses its top bezel and the cut reads as a rendering fault. It bleeds off the *bottom*.

### The motion, in layers

The item used to fade, blur and translate as one block. Now each layer carries its own beat, which
is what the difference between "fluid" and "a slide changed" actually is:

- **the item** cross-fades on opacity and blur only (300ms) — no translation;
- **the copy** climbs a line at a time, five steps of 50ms;
- **the frame** is uncovered top-down (`clip-path`, 720ms) while settling out of `scale(1.03)`
  (900ms), which is the difference between a picture appearing and a screen arriving;
- **the chip** lands last.

Measured on a live transition: copy at `1.0 / 0.9 / 0.7 / 0.4 / 0.0` mid-cascade, clip travelling
100 → 51 → 13 → 0%, scale 1.03 → 1.017 → 1.006 → 1.000.

### Verified (Chrome, measured)

0 horizontal overflow at 320 / 390 / 768 / 820 / 991 / 1024 / 1152 / 1280 / 1440 / 1680 · 0 page
errors · 0 broken images · no chip clipped by its panel at any width where chips render (they are
`display: none` below 48rem, site-wide and pre-existing) · section height steady at 3.6–4.9 screens
across every width · under `prefers-reduced-motion` the block is stacked with the copy, the strikes
and the frames all at their end state and **0** `.rise` blocks hidden page-wide · with `.no-js` the
same, and the sentinels collapse to `display: none`.

## Pass 7 — the hero is an animated scene (24 Aug)

Three scenes were built in parallel and judged. The owner picked **« quatre temps »**; the
other two are kept in `src/scenes/` because rebuilding one is not cheap.

### Why an iframe and not inlined markup

The scene carries 40 `@keyframes` and the site already owns `shine`, `marquee`, `draw-line`,
`bar-drop`, `chip-in`. Inlining would mean prefixing every selector and renaming every
keyframe, and a collision there reads as a broken animation rather than as a merge fault.
The frame guarantees it renders exactly as approved. `aria-hidden` + `tabindex="-1"`, with
one visually-hidden sentence beside it carrying the meaning.

⚠️ `preview.mjs` has to rewrite that `src=` into `srcdoc=`: a relative path has nothing to
resolve against inside the single published file, so the frame would come up empty in the
one artefact anyone actually reviews.

### ⚠️ Three traps in embedding a scaled scene

- **The frame ratio must match the composition the scene chooses.** It switches to a taller
  layout on a narrow container, so a single 5/4 frame cut 130–149px off the bottom below
  640px — the patient card lost its whole footer row. It is `3/4` under 48rem now.
- **Do not widen the frame to buy legibility.** Bleeding it to the full viewport was tried:
  the extra 40px tipped the scene out of its phone layout into the desktop one, which scales
  down harder, and the smallest label went 9.7px → 6.6px. Worse, not better.
- **Measuring "smallest type" by multiplying computed font-size through ancestor transforms
  counts animation frames as defects.** An element mid-flight at `scale(.5)` reports half its
  size. Pause the animations first, or just look at it.

### Verified against the hardening agent rather than trusting it

It reported "0 text under 11px at 320". That held for the scene standalone; inside the hero
column the frame is the viewport minus two gutters — 280px, not 320 — and the claim did not
hold there. Its other claims did check out: no CNAM/TVA/timbre fiscal in any of the three,
no `º` ordinal indicator, no em-dash in any `<title>`, reduced motion resolves to a composed
still with 0 running animations.

### ⚠️ An agent rewrote a section it was not asked to touch

`#avant-avec` came back restructured: six numbered before/after items with app screenshots
and a scroll-driven deck, in place of the two-column list. The owner's own six pain points
survived verbatim and the result is better, so it was kept — but nothing in any brief asked
for it. Check `git diff` on `src/pages/index.html` after any parallel run, not just the
files the agents were told to write.

## How it is built

```
site/
├── build.mjs        static generator, no dependencies. Pages carry a leading JSON comment.
├── preview.mjs      folds one built page into a single self-contained file for review
├── src/
│   ├── layout.html  the shell            src/partials/  nav, footer
│   ├── css/         tokens · base · components   (tokens.css IS the design system)
│   ├── js/site.js   reveal, sticky bar, dropdown, drawer, zone tabs — all enhancement
│   ├── pages/       one file per page
│   └── img/         PNG sources, encoded to WebP at 2000 px by the build
└── dist/            output, deployed as-is
```

- `node build.mjs` — full build. `--no-images` skips encoding; encoded images are also cached on
  mtime, so a copy edit does not re-encode.
- **ffmpeg** does the WebP encoding. It is installed per-user by winget and is **not on PATH**;
  `build.mjs` finds it under `%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg_…`.
- **Every comment is stripped from `dist/`.** The previous site published an internal roadmap note
  that lived in a CSS header comment. Source keeps its comments; output never gets them.

### Two things in the CSS worth knowing before editing

- **`.crop` is a crop expressed in numbers, not a re-encoded file.** `--cx`/`--cy` are the fraction
  of the source that sits left of / above the frame, `--cz` is how many frame-widths the source is
  scaled to. `--cz: 2.15` renders the app at roughly 1:1 in a 590 px frame, which is the difference
  between a fragment that is *read* and one that is guessed at.
- **The top bar is not inside `.on-ink`**, so its transparent state styles itself
  (`.topbar:not(.stuck):not(.on-light)`). Forget that and the ghost button renders dark-on-dark and
  reads as disabled.

## Pass 9 — section 2 becomes a diptych, and then a carousel (24 Aug)

The owner: *« can we make something similar for the second section — fluid, animation, smooth,
comparing life without the app and with, with diagrams and presentation like things like the hero »*.

### The idea, and why it is paper against ink

Section 2 used to be six real screenshots of the app, one per moment. That said *here is a screen*
when the section’s job is to say *here is what your day costs you*. A picture of a list cannot make
that argument; a comparison can.

Every moment is now a **diptych**: the paper world on one side, the software on the other, the same
moment played twice. The two halves are made of the two materials — `--paper` and `--ink-2` — so
nothing has to label the difference, the surfaces do it. §5 and §6 still carry the real screens, so
nothing is lost: **§2 argues, §5 proves.**

Six diagrams, each in both worlds: the pile of folders against a search that opens the whole file ·
a Sunday phone call answered from memory against the dossier in a pocket · a paper odontogramme
scrawled over for three years against a chart that dates every act · a cahier de caisse with blank
lines and a `≈ ?` total against lines that write themselves · a morning of unanswered calls against
six rappels leaving together · a séance nobody wrote up drifting off the page against
« à clôturer » asking one question at a time.

⚠️ **That last one is drawn from the product, not invented.** `VisitClosureState.NextStep` yields a
*single* next step — présence, then fiche, then money — precisely so a visit that ended an hour ago
does not show three red badges. Drawing three live questions would picture a product that does not
exist.

### Three shapes were tried. Two were wrong, and the wrongness is worth keeping written down

1. **Six panels behind a sticky pane, advanced by scroll.** It animated well and it read as a page
   that had stopped responding — six screens of turning the wheel with nothing moving. The owner:
   *« not sure i like the fact that we’re blocking scroll, some users might miss it, and think it’s a
   bug »*. **Do not go back to it.**
2. **Six cards in normal flow.** Honest about the scroll, and 4 312 px — four and a half screens for
   section 2.
3. **A scroll-snap carousel**, which is what shipped: ~1 100 px, one moment on screen, and the page
   scrolls normally past it. The strip is a *native* scroller, so the swipe on a phone and the arrow
   keys on a focused track work with the script blocked; the numbered rail and the two arrows are the
   enhancement on top.

### The phone gets a different answer, not a smaller one

Stacking the two halves made the panel 1 082 px tall, so the paper world and the software world were
never on screen together — which is the only thing a diptych is for — and each diagram had 167 px to
say anything in. Below 62 rem the two halves **share one cell and cross-fade**, named by a two-tab
switch, and the panel **turns itself to « Avec le logiciel » 1.4 s after the slide arrives** so the
payoff does not depend on finding a control. A tap cancels that for good. A slide went 1 511 px →
924 px; each diagram got the full 335.

### Traps this pass paid for

- ⚠️ **A carousel must not pause on hover.** It is the conventional courtesy and it broke the
  feature outright: the reader scrolls, the section arrives under a pointer that has not moved, the
  browser fires `pointerenter` anyway, and the strip never advances once. Measured — stuck on 01 with
  `data-auto="paused"` indefinitely. What holds it now is a keyboard focus inside it and a hidden tab.
- ⚠️ **`pointerdown` on the track is too wide a definition of « the reader took over ».** On a
  desktop that is any click on the panel, including one meant only to bring the window forward.
  Touch, keys and the actual controls.
- ⚠️ **A fixed panel ratio caused a whole class of bug.** A ratio hands each half a height its
  diagram then has to fit inside, and five of twelve did not: against a 401 px drawing area, one
  chart ran 160 px past it and three cahiers 25 px each — straight *over* their own closing lines,
  because the art sits in a `minmax(0, 1fr)` row that does not push, it overlaps. The panels size to
  content. The one reason there ever was a single ratio — six panels sharing one grid cell in the
  sticky pane — is gone.
- ⚠️ **A `display: none` child is removed from grid layout entirely.** The half’s label is hidden on
  a phone, so auto-placement slid the art into row 1 (`auto`) and the note into row 2 (`1fr`): the
  drawing took only its content height and the closing line inherited every spare pixel. The three
  rows are assigned now, not auto-placed.
- ⚠️ **`align-items` cannot stretch an item inside a row that has not itself been stretched.** Block
  19 sets `align-content: start`, so a card’s single auto row stayed at 485 px inside a 577 px slide
  and the panel stopped 90 px short of the rail.
- ⚠️ **Eight fixed-width teeth in a nowrap flex row is 373 px of MIN-content**, and a `1fr` grid
  column is `minmax(auto, 1fr)` — so that min-content wins and the whole half blows out. Measured
  74 px past its own padding at 390, taking the heading and the history list with it. The arch is a
  grid of equal fractions.
- ⚠️ **Six 44 px number buttons plus two 44 px arrows is 410 px of control**, and a 390 px phone has
  335 px of content width — 59 px of horizontal *page* overflow, the one thing a page may never have.
  The rail goes below 48 rem; every slide already prints « 01 / 06 » in its own copy.
- ⚠️ **The arrows must flank the PANEL, not the slide.** Spanning the stage put the left one
  straight over the paragraph in the copy column — the words were under the button.
- ⚠️ **Smooth-scroll only between neighbours.** The automatic advance wraps 06 → 01; smooth-scrolling
  that distance rewinds the reader through all six panels.
- ⚠️ **`<i>` is italic.** Several parts of these diagrams are `<i>` because they carry no meaning
  and must stay out of the accessibility tree — and the folder tabs and the scrawled dates came out
  slanted.
- ⚠️ **`.jour-shot > i` beats `.jd-tick`** (0,2,0 against 0,1,0), so every tick inside a row lost its
  size, its ground and its check and rendered as a bare dot. All six rappels were plain circles.
- ⚠️ **The odontogramme is a diagram, not a form.** A full 16-tooth FDI arch — 18…11 | 21…28 above,
  48…41 | 31…38 below — is anatomically right and reads as a clinical document: 32 numbered ovals
  are noise at this size. Eight an arch, unnumbered. *(An orphan third arch survived the cut, still
  carrying its `data-n`, and rendered as a third row that was the only numbered one — check for
  leftovers after a change like that.)*
- ⚠️ **There were TWO copies of the old sticky driver in `site.js`.** Both ran; the later one won,
  which is why the pane kept pinning after the first was replaced. Grep for the whole comment header
  before assuming a block is unique.

## How it is deployed

`node build.mjs` writes `dist/`, `node preview.mjs` folds it into one self-contained file for
publishing. **Neither is committed** — both are generated, and `preview/index.html` is a 2 MB single
file that would churn on every publish. `dist/` is what goes to `oumayma-404/gestion-clinique-site`;
see `landing-v2/DEPLOY.md` for the credential trap on this machine (two GitHub accounts, and the
`oumayma-404` token lives under a different Credential Manager target).

## Built so far

- The chassis: tokens, layout, nav with dropdown, phone drawer, footer, build + preview pipeline.
- `index.html`, all thirteen sections — hero (an animated scene in an iframe, « quatre temps ») ·
  **le quotidien (the six-moment diptych carousel)** · à qui il s’adresse · le constat · les
  fonctionnalités · les 24 écrans · conçu en Tunisie · l’argent · sur le téléphone · fiabilité ·
  les questions · l’essai · vos données.
- Verified in Chrome at 390 and 1440: 0 horizontal overflow, 0 diagram overflowing its own box,
  the carousel advancing, wrapping 06 → 01, and stopping for good on the first navigation.

## Left to do

1. **The price and the billing model.** The owner chose « there’s a price, publish it » and the
   figure has never been supplied. `tarifs.html` is linked from the nav and does not exist. This is
   the one thing the page cannot argue without, and it has now been raised five times.
2. The nine other pages: the `fonctionnalites/` hub and its four zone pages, `tarifs.html`,
   `demo.html`, `mobile.html`, `hors-ligne.html`. Every nav link currently goes nowhere.
3. **Three captures that do not exist**: stock, fournisseurs, journal d’activité.
4. The film — the in-page scene can be recorded to mp4 for social with the installed ffmpeg.
5. A real contact address — `contact@gestion-clinique.tn` in the footer is a placeholder, and
   `confidentialite.html` does not exist.
6. **Stock photography licensing.** The photos are Unsplash images of practices that are not
   Tunisian. Confirm or reshoot before any paid traffic.


## Pass 10 — « le dossier patient » remplace « Conçu en Tunisie » (24 août)

The owner: « this section feels weird all together … it's a minor feature, does not need to be on
the first page, replace it with a section about patient data — the odontogramme, actes réalisés,
diagnostic, files page, types de procédures … discover the patient details page before you write
anything to website. » Then, on the chosen design: « make the odontogramme replay animation not
just on scroll, make a veryyy cool animation for it. »

**What the application actually holds**, read out of the code rather than assumed — these are the
only facts the section is allowed to state:

| | |
|---|---|
| The seven tabs, in order | Dossiers médicaux · Rendez-vous · Notes · Documents · Fichiers · Factures · Plan de traitement |
| The odontogram | **two readings of one mouth**: « Diagnostics » (à traiter) and « Actes réalisés » (fait) |
| The nine tooth conditions | Sain · Carie · Obturation · Couronne · Traitement de canal · Bridge · Implant · Extrait / Absent · À traiter |
| Why it is the hero thesis | a `ProcedureType` carries a **`resultingCondition`**, so charting an acte sets the tooth's new state **by itself** — « une seule saisie » proved on the clinical side instead of the money side |

⚠️ **THE `s5-` PREFIX IS ALREADY TAKEN, and the collision is silent.** The outgoing
« Conçu en Tunisie » owns `components.css` **lines 4132–4624** under the banner
« 5 · Conçu en Tunisie », plus one later correction (`.s5-card--b, .s5-card--c { grid-column:
span 6 }`, added when two of its four cards were cut). The incoming section reuses the same prefix
and shares **nine class names** with it — `.s5-head`, `.s5-eyebrow`, `.s5-lede`, `.s5-still`,
`.s5-mark`, `.s5-edge`, `.s5-fig`, `.s5-tot`, `.s5-cap`. Whichever block sits later in the file
wins those selectors, so pasting the new CSS in without **deleting the old block first** silently
applies the old section's padding, colour and size to the new markup. No error, no warning, and it
looks like a design that came out wrong.

⚠️ **And there is a THIRD Tunisie block, dead.** Lines 1324–1488, banner
« 23 · Conçu en Tunisie: the proof grid », 103 `.tn-*` rules from a version before the current
one. Nothing in `index.html`, `nav.html` or `footer.html` uses a `tn-` class — the only matches
are `btn-primary` and `btn-ghost` containing the substring. It goes with the same edit.

## Pass 11 — la FAQ, pliée en quadrant (25 août)

The owner: « name it like faq, and it's taking too much space, try to reduce its length … and
remove this section: Depuis quand le logiciel existe-t-il ? »

**Named.** Eyebrow « Questions » → **« FAQ »**, title « Ce qu'on se demande avant de commencer » →
**« Les questions qu'on nous pose »**, and the section id `#questions` → **`#faq`** (nothing linked
to the old one — checked `nav.html`, `footer.html` and every page before renaming it). The lede
(« Cinq questions, cinq réponses courtes ») is gone: it counted a row that no longer exists, and
the eyebrow now says what it said.

**Shortened, and the numbers are the point.** Nothing was hidden, shrunk or cut short — the
section's own geometry is what was spending the space. The question was capped at 24ch and the
answer at 46ch inside a 775 px column, so each row paid ~290 px of empty middle before the mark's
17rem column even started, five times over. Above 62rem the five rows now fold into a **2 × 2
quadrant** parted by the same hairlines (one of them vertical — the chassis's own signature), each
mark back under its own words; between 48 and 62rem the row keeps its spine and the mark moves out
**beside** the words; the three CSV files lie across instead of down (they were 110 px of drawing
for three words).

| | before | after |
|---|---|---|
| 1440 | 1 249 px | **747 px** (−40 %) |
| 991 | 1 125 px | **807 px** |
| 820 | 1 092 px | **799 px** |
| 390 | 1 592 px | **1 158 px** (−27 %) |
| 320 | — | **1 247 px** |
| whole page | 7 419 / 10 172 px | **6 918 / 9 738 px** |

Verified in Chrome at 320 · 390 · 768 · 820 · 991 · 992 · 1180 · 1440: no horizontal overflow at
any of them, the reveal still fires and every mark resolves (ticks in, fill at rest, chips in).

⚠️ **A `max-width` one sixteenth of a rem below the next `min-width` leaves a gap.** The first
version of the middle band was `(min-width: 48rem) and (max-width: 61.9375rem)`, which is the
usual way to write it — and at a nominal 991 px this machine reports a viewport of **991.33**, so
that width matched NEITHER band and the row fell back to the phone's stack. The bands overlap
instead (`min-width: 48rem`, then `min-width: 62rem` re-declaring every property).

⚠️ **05 was the page's only sentence saying the product is new.** « Il est récent, et nous le
disons » went with the row. §7 still carries the thirty days and the « sans carte », so the risk
reversal itself survives — but of the four things named at the top of this brief as carrying
credibility in place of third-party proof, *plain transparency about being new* is no longer said
anywhere on the page.

⚠️ **Another session was committing in this repo while this pass was being written.** Commit
`c67f2b7` (« la caisse encaisse une séance sous vos yeux ») swept up the finished `index.html`
half of this work along with its own — the FAQ markup is in that commit, under that message, and
only the CSS was left to commit as this pass.
