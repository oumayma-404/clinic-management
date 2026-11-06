# QA plan — Part 2, « Nouveautés » dans l'app

**Written:** 2026-11-06
**Change under test:** `feature/console-news-broadcast` Part 2 (worktree, uncommitted on top of 5c579b88)
**Rendering files in the diff:** `web/app/nouveautes/page.tsx`, `web/components/news/news-feed.tsx`,
`web/components/dashboard-sidebar.tsx`, `web/components/bottom-nav.tsx`, `console/components/news-history.tsx`
**Blast radius (must re-test):** the rail (`renderItem` gained a count — every row goes through it), the phone bar's
« Plus », the console history table, the e-mail's button link.
**Budget:** small feature → A + B + C (reachable) + D · 19 rows

## Isolated stack (nothing shared is written)

API from the worktree on **:5094** (console listener **:5494**), DB `clinic_news_qa` (new), own key ring, MinIO bucket
`news-qa`; console `.next/standalone` on **:3094**; web `.next/standalone` on **:3095**; shared Mailpit, QA addresses
`@news-qa.test` only. Seed: « Cabinet Essai » with admin `a@` (authenticator + own password) and secretary `s@`;
console account `qa@`. 12 news sent through the console API; 9 of them back-dated (Aug–Oct) by SQL to before the
admin's account existed, so **3 unread**.

## Tier A — happy path

| ID | Surface | Steps | Expected | Layer | Widths |
|----|---------|-------|----------|-------|--------|
| A1 | rail | admin signs in, lands on `/` | foot row « Nouveautés » with a « 3 » pill | browser | 1440 |
| A2 | `/nouveautes` | open from the row | « Nouveautés » + « 3 non lues »; 10 cards, the first 3 « Nouveau »; timeline nodes; index lists 12 under 3+ months | browser | 1440 |
| A3 | rail after open | look at the row | no pill; cards still « Nouveau » | browser | 1440 |
| A4 | marker | SQL | one `PlatformNewsReadMarkers` row, `ReadUpToUtc` = newest `CreatedAtUtc` | sql | — |
| A5 | reload | reload `/nouveautes` | no « Nouveau », no « non lues », no pill | browser | 1440 |
| A6 | e-mail | Mailpit, newest news message | button href ends `/nouveautes` | api | — |

## Tier B — alternate paths

| ID | Surface | Steps | Expected | Layer | Widths |
|----|---------|-------|----------|-------|--------|
| B1 | console « Dans l'app » | before A2: press « Masquer » on the newest | « Masquée » + « Afficher »; web badge reads 2 | browser + api | 1440 |
| B2 | console | press « Afficher » | « Visible »; badge back to 3 | browser + api | 1440 |
| B3 | secretary | sign in as `s@` | no « Nouveautés » row; `/nouveautes` shows « Réservé aux administrateurs »; `GET /api/news` → 403 | browser + api | 1440 |
| B4 | « plus anciennes » | press it | 12 cards, button gone | browser | 1440 |
| B5 | index | press the oldest entry in the index (before B4, fresh load) | that card is rendered and scrolled into view | browser | 1440 |
| B6 | empty | console hides all 12 (API), reload | « Aucune nouveauté pour l'instant »; then show all again | browser + api | 1440 |

## Tier C — edge cases

| ID | Surface | Steps | Expected | Layer | Widths |
|----|---------|-------|----------|-------|--------|
| C1 | phone bar | before A2, coarse pointer 390×844 | « Plus » carries a dot, its name says « 3 nouveautés non lues »; the drawer's foot row shows « 3 » | browser | 390 |
| C2 | page at 320 | open `/nouveautes` | no horizontal scroll in `main`; date inside each card; no index | browser | 320 |
| C3 | collapsed rail | collapse the rail before reading | a dot on the « Nouveautés » icon | browser | 1440 |
| C4 | widths | screenshots | readable at 390 / 820 / 1180 / 1536×730 | browser | all |
| C5 | own PC (404) | — | ⏭ needs a SelfHostedLan API; covered by `NewsControllerTests` | — | — |

## Tier D — regression sweep

| ID | Surface | Expected | Layer | Widths |
|----|---------|----------|-------|--------|
| D1 | rail on `/patients` | « Patients » active (bar + bold), « Nouveautés » row unchanged | browser | 1440 |
| D2 | phone « Plus » | still opens the drawer | browser | 390 |
| D3 | console history | « Résultat » still reads « 1 / 1 envoyé »; cards below `lg:` carry « Dans l'app » | browser | 1440 / 390 |
