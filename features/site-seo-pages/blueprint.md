# Blueprint — apexa.tn ranks for « apexa » and every « logiciel … cabinet dentaire tunisie » search

Option 1 of `/think-solution` (2026-10-18), chosen by the owner. **Built locally 2026-10-18 — see `notes.md` for what shipped and the deviations.**
Nothing goes live without the owner's explicit OK for this change ([[never-push-without-consent]]).

## Summary

The technical setup is already right (canonical, sitemap, robots, redirects, JSON-LD, real icons —
commit `d9ac77c3`, `7627b8e1`). What is missing is **content and brand**: Google ranks *pages*, and
the site has one page per search for only three searches; it never says « application web »,
« logiciel dentaire » or « en ligne » although all three are true of the hosted formula; the homepage
links to none of the long-tail pages; and Google has nothing tying the word « APEXA » to apexa.tn
(no `sameAs`, no social profile, no `alternateName`).

The fix is four substantial pages built exactly like the existing three (zero new CSS), the missing
vocabulary where it is true, a brand entity in the structured data plus visible social links, homepage
links into every long-tail page — and two **derived** build guards, because adding pages is precisely
the change the current hand-listed guard cannot see.

## What the copy may and may not say

Every new sentence is held to `site/BRIEF.md` « Claims ». The constraints that bite on these pages:

| ❌ Never | Why / source |
|---|---|
| SMS / WhatsApp / « rappels automatiques » | not in service (owner, 29/09/2026); `check-tokens.mjs` |
| CNAM | no screen since 2026-09-13 (`features/cnam-ui-withdrawal/notes.md`) |
| TVA, timbre fiscal | BRIEF « Claims » |
| A price, a competitor, a client count, a testimonial | BRIEF Passe 14 · `jsonld/README.md` rules 1–2 |
| Time savings (« en dix secondes ») | BRIEF l.253 |
| « mises à jour comprises » | BRIEF l.802 |
| **« données hébergées en Tunisie »** | the hosted VPS is in London ([[compliance-status-tunisia]]) — say nothing about where |
| « sans internet » as a fact about the product | true of the local install only |
| « application iPhone » | no compiled iOS build; iPhone = the web app in Safari |
| AI anything | there is no AI subsystem |

| ✅ True today (verified in source 2026-10-18) |
|---|
| **Hosted formula = a web app**: `app.apexa.tn` in any browser, PC included; it needs internet (`logiciel-dentaire-hors-ligne.html:130-140` already says so) |
| Agenda day/week/month, drag to create or move, several acts per séance, waiting list, séances à clôturer, 24-hour clock |
| Caisse du jour + extrait, 4 payment modes, chèques postdatés par échéance, notes d'honoraires numbered without gaps per year, devis + échéancier, reste à payer, au millime |
| Dossier patient, fiche de soins that emits the ordonnance, demande d'examens on its own sheet, certificat, lettre de liaison, the cabinet's own letterhead, DICOM radiograph viewer, 3D scan viewer (STL/PLY/OBJ) |
| Two-factor login, three roles, tamper-evident journal, live refresh between colleagues |
| Backups: **local** = hourly, verified; **hosted** = the host's daily backups; **both** = 7 daily restore points restorable in one click |

⚠️ **Verify on screen before writing** (agents found the site and `FEATURE-OVERVIEW.md` disagree):
the dossier's tab count (site says 7 incl. « Notes », overview says 6) and the chart's tab names
(« État dentaire » / « Actes réalisés », not « Diagnostics »). Read `web/app/patients/[id]` — the
source settles it without a browser.

⚠️ **Google Agenda is left out** of the agenda page: it is push-only, admin-only, and needs Google
keys the hosted config marks optional. Add it only once the owner confirms it works on app.apexa.tn.

## Files to create

Each page follows `src/pages/odontogramme.html` exactly: JSON front-matter, a source comment listing
**target queries** and **claims that must stay true**, `.cp` / `.cp-head` / `.cp-grid` / `.cp-card*`
markup, a closing `.cp-card--contact` with « Demander une démonstration » → `index.html#demande`
and a « Voir aussi » line to two sibling pages. **~800 words of visible text each** (the guide's
length): BRIEF l.44 — « thin pages look worse than no pages ».

| Page (`src/pages/…`) | `title` (≤ 60) | Target queries |
|---|---|---|
| `application-web-cabinet-dentaire.html` | Application web de gestion de cabinet dentaire \| APEXA | application web cabinet dentaire · logiciel dentaire en ligne · logiciel cabinet dentaire cloud · gestion cabinet dentaire en ligne |
| `agenda-rendez-vous-cabinet-dentaire.html` | Agenda et rendez-vous du cabinet dentaire \| APEXA | logiciel rendez-vous dentiste · agenda cabinet dentaire · planning dentiste |
| `caisse-cabinet-dentaire.html` | Caisse et paiements du cabinet dentaire, en dinars \| APEXA | logiciel caisse cabinet dentaire · facturation dentiste tunisie · note d'honoraires dentiste · gestion des chèques |
| `dossier-patient-dentaire.html` | Dossier patient dentaire et ordonnances \| APEXA | dossier patient dentaire · fiche de soins dentaire · ordonnance dentiste logiciel |

The application-web page also carries a second CTA: « Ouvrir app.apexa.tn » (plain link). **Not**
`/signup`: self-signup depends on the live server's SMTP, which nothing here can confirm.

`src/jsonld/<same-name>.json` for each — the `WebPage` + `BreadcrumbList` shape of
`src/jsonld/odontogramme.json`, `@id`s on the page's own URL, `description` = the front-matter
`desc` word for word, `primaryImageOfPage` only with an image that already returns 200
(`m-agenda.webp`, `m-caisse.webp`, `m-dashboard.webp` exist; the dossier has no phone capture —
omit the key rather than point at a missing file, README rule 4).

## Files to modify

| File | Change |
|---|---|
| `src/pages/index.html` | ① `desc` gains « logiciel dentaire » and « en ligne ou installé au cabinet » (≤ 155 chars). ② `#telecharger` gains a 4th `.dl-block`: « Navigateur · Sur n'importe quel ordinateur · Ouvrez app.apexa.tn » — the one honest home for « application web ». ③ One visible link per section into its page: `#dossier` → dossier + odontogramme, `#argent` → caisse, `#mobile` → application web, the agenda cell of `.ov` → agenda. ④ FAQ 01's answer split by formula (hourly is local only). ⑤ One new FAQ row: « Faut-il installer quelque chose ? » — the browser answer. |
| `src/jsonld/index.json` | `Organization` + `WebSite`: `"alternateName": ["Apexa Tunisie", "apexa.tn"]` (WebSite's is what Google's *site name* feature reads). `Organization.sameAs`: `https://www.instagram.com/apexa.tn/`, `https://www.facebook.com/profile.php?id=61594891902273`. `SoftwareApplication`: `operatingSystem` → « Windows 10, Windows 11, Android, navigateur web »; `featureList` « Applications Android et iPhone » → « Application Android, et l'iPhone par le navigateur », plus « Application web : app.apexa.tn ». FAQ entries re-copied **word for word** from ④ and ⑤ in the same commit (README rule 3). |
| `src/partials/footer.html` | « Le logiciel » list gains the 4 pages (« Le dossier patient » now points at the page, not `#dossier`). Instagram + Facebook icon links (`target="_blank" rel="me noopener"`, `aria-label`, ≥ 44 px on a coarse pointer). The SEO paragraph gains « logiciel dentaire en ligne ». Fix its stale comment (« sur les deux pages » → every page). |
| `src/pages/guide-choisir-logiciel-cabinet-dentaire.html` | Q3 (hourly backup) and Q8 (« applications Android et iPhone ») say which formula they are true of; one link to the application-web page from Q1. `dateModified` in its JSON-LD. |
| `check-tokens.mjs` | **Derive the claim scope** (below). |
| `build.mjs` | **Dead internal link check** (below). |
| `BRIEF.md` « Trois pages de longue traîne » → seven; `REFERENCEMENT.md` §2/§3/§4 counts and the URL list for « Demander une indexation » | docs follow the code |

## Key shapes

**Claim guard — derived, with named exemptions** (`check-tokens.mjs` § Claims). Today the reminders
check reads a hand-written list of six files and the CNAM / TVA / « sans internet » check reads
`index.html` alone, so a new page is checked by nothing — the repo's own defect shape
([[fixes-dont-propagate]]).

```
pages   = walk('pages')  + walk('jsonld') + walk('scenes')        // every file, found, not listed
EXEMPT  = { reminders:   ['pages/confidentialite.html', 'jsonld/confidentialite.json'],   // a disclosure, not a claim
            sansInternet:['pages/logiciel-dentaire-hors-ligne.html', …] }                // each with its reason
text(f) = read(f) minus comments minus tags                         // test the words, not the hrefs
```

⚠️ **Test the text, not the markup**: `/hors[\s-]ligne/` matches the *href*
`logiciel-dentaire-hors-ligne.html`, so widening the scope over raw HTML fails every page that links
to that page. Strip tags first.
⚠️ Run the widened guard on **HEAD before writing any page**. Every finding on an existing page is
either a real claim to fix now or an exemption to justify in one line — never a silent addition.

**Dead internal link check** (`build.mjs`, after dist is written). For every `href`/`src` in
`dist/**/*.html` that is not `http(s):`, `mailto:`, `tel:` or `data:`: the file exists in `dist/`,
and a `#fragment` names an `id` on the target page. Allow-list with reasons: `APEXA-1.1.1.apk`
(lives only in the deploy repo). This is the guard the nine never-built pages of `235a9371` would
have tripped, and the change below adds ~20 new internal links.

## Potential pitfalls

- **`$` in page text**: the body is spliced with `String.replace`, so `$&` / `$$` in copy are
  replacement patterns (agent report, build.mjs:185-197). Write « DT », never a dollar sign.
- **A `"` in `desc` breaks the attribute** — front-matter values are not escaped.
- **`dl-cols` was laid out for three cards**: a fourth must hold at 320 / 390 / 820 / 1180 / 1440 px.
- **Footer icons on a coarse pointer** need 44 px targets ([[frontend-must-be-device-ready]]).
- **`sameAs` on the numeric Facebook URL**: when the owner sets a username, update it — a
  `sameAs` that redirects still works, but the vanity URL is the stronger signal.
- **Build from HEAD, deploy to the separate repo**, keep `CNAME` and the APK
  ([[website-work-needs-two-pushes]]).
- **FAQ JSON-LD drift**: edit `index.html` and `index.json` in the same commit, or Google drops the
  FAQ markup.

## Test strategy

| Check | Pass when |
|---|---|
| `node build.mjs` (from a `git archive HEAD` copy) | `check-tokens: clean`, link check clean |
| Plant « CNAM » in a new page, then a dead `href` | the build **fails** on each; revert |
| `dist/sitemap.xml` | 9 URLs, home first |
| A scratch script over `dist/*.html` | one `<title>` ≤ 60 chars, `description` ≤ 160, one `<h1>`, canonical = own URL, every JSON-LD block parses |
| Eye pass, served from `dist/` | the 4 pages, the homepage install section and the footer at 320 / 390 / 820 / 1180 / 1440 px; coarse-pointer targets |
| After deploy (owner's OK) | each new URL 200; Rich Results test on `/` and one new page; Search Console « Demander une indexation » for the 4 new URLs + `/` |

## Off the site — only the owner can do these

| Action | Effect |
|---|---|
| Search Console: open the `apexa.tn` property (the DNS record exists), resubmit `sitemap.xml` after deploy | Google learns the 4 new pages exist |
| Facebook: set the username `apexa.tn` | a brand URL instead of `profile.php?id=…` |
| Instagram + Facebook bios: link `https://apexa.tn` | closes the `sameAs` loop from their side |
| Google Business Profile (needs a public address + phone) | biggest lever for « … Tunisie » and the brand query |
| 3–5 links from real Tunisian sites | decides page 1 vs page 3 (`REFERENCEMENT.md` §7) |

Honest timeline: indexing within days, a stable ranking in 2–4 months **with** inbound links.
« apexa » alone: reachable on google.tn; apexa.ca will keep the worldwide result.
